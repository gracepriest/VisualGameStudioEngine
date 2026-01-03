using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for managing IDE settings at different scopes.
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly Dictionary<string, object?> _defaultSettings = new();
    private readonly Dictionary<string, object?> _userSettings = new();
    private readonly Dictionary<string, object?> _workspaceSettings = new();
    private readonly Dictionary<string, object?> _folderSettings = new();
    private readonly List<SettingsSchema> _schemas = new();
    private readonly Dictionary<string, SettingsPropertySchema> _propertySchemas = new();

    private readonly string _userSettingsPath;
    private string? _workspacePath;

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var settingsDir = Path.Combine(appData, "VisualGameStudio");
        Directory.CreateDirectory(settingsDir);
        _userSettingsPath = Path.Combine(settingsDir, "settings.json");

        RegisterDefaultSchema();
    }

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    public T Get<T>(string key, T defaultValue, SettingsScope scope = SettingsScope.Effective)
    {
        var value = Get(key, scope);
        if (value == null)
        {
            return defaultValue;
        }

        try
        {
            if (value is JsonElement jsonElement)
            {
                return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), JsonOptions) ?? defaultValue;
            }

            if (value is T typedValue)
            {
                return typedValue;
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }

    public T? GetValue<T>(string key, T? defaultValue = default)
    {
        return Get(key, defaultValue!, SettingsScope.Effective);
    }

    public object? Get(string key, SettingsScope scope = SettingsScope.Effective)
    {
        return scope switch
        {
            SettingsScope.Default => GetFromDictionary(_defaultSettings, key),
            SettingsScope.User => GetFromDictionary(_userSettings, key),
            SettingsScope.Workspace => GetFromDictionary(_workspaceSettings, key),
            SettingsScope.Folder => GetFromDictionary(_folderSettings, key),
            SettingsScope.Effective => GetEffectiveValue(key),
            _ => null
        };
    }

    public void Set<T>(string key, T value, SettingsScope scope = SettingsScope.User)
    {
        var dict = scope switch
        {
            SettingsScope.User => _userSettings,
            SettingsScope.Workspace => _workspaceSettings,
            SettingsScope.Folder => _folderSettings,
            SettingsScope.Default => _defaultSettings,
            _ => _userSettings
        };

        var oldValue = GetFromDictionary(dict, key);
        SetToDictionary(dict, key, value);

        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, oldValue, value));
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(new[] { key }, scope));
    }

    public void SetValue<T>(string key, T value)
    {
        Set(key, value, SettingsScope.User);
    }

    public void Remove(string key, SettingsScope scope = SettingsScope.User)
    {
        var dict = scope switch
        {
            SettingsScope.User => _userSettings,
            SettingsScope.Workspace => _workspaceSettings,
            SettingsScope.Folder => _folderSettings,
            _ => null
        };

        if (dict == null) return;

        var oldValue = GetFromDictionary(dict, key);
        RemoveFromDictionary(dict, key);

        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, oldValue, null));
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(new[] { key }, scope));
    }

    public bool Has(string key, SettingsScope scope = SettingsScope.Effective)
    {
        return Get(key, scope) != null;
    }

    public IReadOnlyDictionary<string, object?> GetAll(SettingsScope scope = SettingsScope.Effective)
    {
        return scope switch
        {
            SettingsScope.Default => new Dictionary<string, object?>(_defaultSettings),
            SettingsScope.User => new Dictionary<string, object?>(_userSettings),
            SettingsScope.Workspace => new Dictionary<string, object?>(_workspaceSettings),
            SettingsScope.Folder => new Dictionary<string, object?>(_folderSettings),
            SettingsScope.Effective => GetAllEffective(),
            _ => new Dictionary<string, object?>()
        };
    }

    public IReadOnlyDictionary<string, object?> GetSection(string prefix, SettingsScope scope = SettingsScope.Effective)
    {
        var all = GetAll(scope);
        var normalizedPrefix = prefix.EndsWith(".") ? prefix : prefix + ".";

        return all
            .Where(kvp => kvp.Key.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public void ResetToDefault(string key)
    {
        Remove(key, SettingsScope.User);
        Remove(key, SettingsScope.Workspace);
        Remove(key, SettingsScope.Folder);
    }

    public void ResetAllToDefaults()
    {
        var affectedKeys = _userSettings.Keys.ToList();
        _userSettings.Clear();
        _workspaceSettings.Clear();
        _folderSettings.Clear();

        if (affectedKeys.Count > 0)
        {
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(affectedKeys, SettingsScope.User));
        }
    }

    public async Task LoadAsync()
    {
        if (File.Exists(_userSettingsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_userSettingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
                if (settings != null)
                {
                    _userSettings.Clear();
                    foreach (var kvp in settings)
                    {
                        _userSettings[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch
            {
                // Ignore load errors
            }
        }

        await LoadWorkspaceSettingsAsync();
    }

    public async Task SaveAsync()
    {
        try
        {
            var dir = Path.GetDirectoryName(_userSettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_userSettings, JsonOptions);
            await File.WriteAllTextAsync(_userSettingsPath, json);
        }
        catch
        {
            // Ignore save errors
        }

        await SaveWorkspaceSettingsAsync();
    }

    public async Task ImportAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (settings != null)
            {
                var affectedKeys = new List<string>();
                foreach (var kvp in settings)
                {
                    _userSettings[kvp.Key] = kvp.Value;
                    affectedKeys.Add(kvp.Key);
                }

                if (affectedKeys.Count > 0)
                {
                    SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(affectedKeys, SettingsScope.User));
                }
            }
        }
        catch
        {
            // Ignore import errors
        }
    }

    public async Task ExportAsync(string filePath, SettingsScope scope = SettingsScope.User)
    {
        try
        {
            var settings = GetAll(scope);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch
        {
            // Ignore export errors
        }
    }

    public void RegisterSchema(SettingsSchema schema)
    {
        _schemas.Add(schema);
        foreach (var prop in schema.Properties)
        {
            _propertySchemas[prop.Key] = prop;
            if (prop.Default != null && !_defaultSettings.ContainsKey(prop.Key))
            {
                _defaultSettings[prop.Key] = prop.Default;
            }
        }
    }

    public IReadOnlyList<SettingsSchema> GetSchemas()
    {
        return _schemas.ToList();
    }

    public SettingsPropertySchema? GetPropertySchema(string key)
    {
        return _propertySchemas.TryGetValue(key, out var schema) ? schema : null;
    }

    public void SetWorkspacePath(string? path)
    {
        _workspacePath = path;
        _workspaceSettings.Clear();
        if (path != null)
        {
            _ = LoadWorkspaceSettingsAsync();
        }
    }

    #region Private Methods

    private object? GetEffectiveValue(string key)
    {
        // Priority: Folder > Workspace > User > Default
        var value = GetFromDictionary(_folderSettings, key);
        if (value != null) return value;

        value = GetFromDictionary(_workspaceSettings, key);
        if (value != null) return value;

        value = GetFromDictionary(_userSettings, key);
        if (value != null) return value;

        return GetFromDictionary(_defaultSettings, key);
    }

    private Dictionary<string, object?> GetAllEffective()
    {
        var result = new Dictionary<string, object?>();

        // Start with defaults
        foreach (var kvp in _defaultSettings)
        {
            result[kvp.Key] = kvp.Value;
        }

        // Overlay user settings
        foreach (var kvp in _userSettings)
        {
            result[kvp.Key] = kvp.Value;
        }

        // Overlay workspace settings
        foreach (var kvp in _workspaceSettings)
        {
            result[kvp.Key] = kvp.Value;
        }

        // Overlay folder settings
        foreach (var kvp in _folderSettings)
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    private static object? GetFromDictionary(Dictionary<string, object?> dict, string key)
    {
        // Support dot-notation for nested objects
        if (dict.TryGetValue(key, out var value))
        {
            return value;
        }

        // Try to find nested value
        var parts = key.Split('.');
        if (parts.Length > 1)
        {
            object? current = null;
            var currentKey = "";
            for (int i = 0; i < parts.Length; i++)
            {
                currentKey = i == 0 ? parts[i] : currentKey + "." + parts[i];
                if (dict.TryGetValue(currentKey, out current))
                {
                    if (i == parts.Length - 1)
                    {
                        return current;
                    }

                    if (current is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        var remaining = string.Join(".", parts.Skip(i + 1));
                        if (jsonElement.TryGetProperty(remaining, out var nestedValue))
                        {
                            return nestedValue;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static void SetToDictionary(Dictionary<string, object?> dict, string key, object? value)
    {
        dict[key] = value;
    }

    private static void RemoveFromDictionary(Dictionary<string, object?> dict, string key)
    {
        dict.Remove(key);
    }

    private async Task LoadWorkspaceSettingsAsync()
    {
        if (string.IsNullOrEmpty(_workspacePath)) return;

        var settingsPath = Path.Combine(_workspacePath, ".vgs", "settings.json");
        if (!File.Exists(settingsPath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (settings != null)
            {
                _workspaceSettings.Clear();
                foreach (var kvp in settings)
                {
                    _workspaceSettings[kvp.Key] = kvp.Value;
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    private async Task SaveWorkspaceSettingsAsync()
    {
        if (string.IsNullOrEmpty(_workspacePath) || _workspaceSettings.Count == 0) return;

        try
        {
            var settingsDir = Path.Combine(_workspacePath, ".vgs");
            Directory.CreateDirectory(settingsDir);

            var settingsPath = Path.Combine(settingsDir, "settings.json");
            var json = JsonSerializer.Serialize(_workspaceSettings, JsonOptions);
            await File.WriteAllTextAsync(settingsPath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void RegisterDefaultSchema()
    {
        var editorSchema = new SettingsSchema
        {
            Id = "editor",
            Title = "Editor",
            Description = "Editor settings",
            Order = 1,
            Properties = new List<SettingsPropertySchema>
            {
                new() { Key = SettingsKeys.FontFamily, Type = SettingsPropertyType.String, Title = "Font Family", Default = "Consolas", Description = "Controls the font family." },
                new() { Key = SettingsKeys.FontSize, Type = SettingsPropertyType.Integer, Title = "Font Size", Default = 14, Minimum = 6, Maximum = 72, Description = "Controls the font size in pixels." },
                new() { Key = SettingsKeys.TabSize, Type = SettingsPropertyType.Integer, Title = "Tab Size", Default = 4, Minimum = 1, Maximum = 8, Description = "The number of spaces a tab is equal to." },
                new() { Key = SettingsKeys.InsertSpaces, Type = SettingsPropertyType.Boolean, Title = "Insert Spaces", Default = true, Description = "Insert spaces when pressing Tab." },
                new() { Key = SettingsKeys.ShowLineNumbers, Type = SettingsPropertyType.Boolean, Title = "Show Line Numbers", Default = true, Description = "Controls the visibility of line numbers." },
                new() { Key = SettingsKeys.HighlightCurrentLine, Type = SettingsPropertyType.Boolean, Title = "Highlight Current Line", Default = true, Description = "Highlight the active line." },
                new() { Key = SettingsKeys.WordWrap, Type = SettingsPropertyType.Boolean, Title = "Word Wrap", Default = false, Description = "Controls word wrapping." },
                new() { Key = SettingsKeys.AutoSave, Type = SettingsPropertyType.String, Title = "Auto Save", Default = "off", Enum = new List<object> { "off", "afterDelay", "onFocusChange", "onWindowChange" }, Description = "Controls auto save behavior." },
                new() { Key = SettingsKeys.AutoSaveDelay, Type = SettingsPropertyType.Integer, Title = "Auto Save Delay", Default = 1000, Minimum = 100, Description = "Auto save delay in milliseconds." },
                new() { Key = SettingsKeys.ShowMinimap, Type = SettingsPropertyType.Boolean, Title = "Show Minimap", Default = true, Description = "Controls whether the minimap is shown." },
                new() { Key = SettingsKeys.BracketPairColorization, Type = SettingsPropertyType.Boolean, Title = "Bracket Pair Colorization", Default = true, Description = "Enable bracket pair colorization." },
            }
        };

        var appearanceSchema = new SettingsSchema
        {
            Id = "appearance",
            Title = "Appearance",
            Description = "Appearance settings",
            Order = 0,
            Properties = new List<SettingsPropertySchema>
            {
                new() { Key = SettingsKeys.Theme, Type = SettingsPropertyType.String, Title = "Color Theme", Default = "dark", Enum = new List<object> { "dark", "light", "high-contrast" }, Description = "Specifies the color theme." },
            }
        };

        var terminalSchema = new SettingsSchema
        {
            Id = "terminal",
            Title = "Terminal",
            Description = "Integrated terminal settings",
            Order = 2,
            Properties = new List<SettingsPropertySchema>
            {
                new() { Key = SettingsKeys.TerminalFontFamily, Type = SettingsPropertyType.String, Title = "Font Family", Default = "Consolas", Description = "Controls the font family." },
                new() { Key = SettingsKeys.TerminalFontSize, Type = SettingsPropertyType.Integer, Title = "Font Size", Default = 14, Minimum = 6, Maximum = 72, Description = "Controls the font size." },
                new() { Key = SettingsKeys.TerminalShell, Type = SettingsPropertyType.String, Title = "Default Shell", Default = "", Description = "The path of the shell to use." },
            }
        };

        var gitSchema = new SettingsSchema
        {
            Id = "git",
            Title = "Git",
            Description = "Git settings",
            Order = 3,
            Properties = new List<SettingsPropertySchema>
            {
                new() { Key = SettingsKeys.GitAutoFetch, Type = SettingsPropertyType.Boolean, Title = "Auto Fetch", Default = true, Description = "Automatically fetch from remotes." },
                new() { Key = SettingsKeys.GitAutoFetchInterval, Type = SettingsPropertyType.Integer, Title = "Auto Fetch Interval", Default = 180, Minimum = 60, Description = "Fetch interval in seconds." },
                new() { Key = SettingsKeys.GitConfirmSync, Type = SettingsPropertyType.Boolean, Title = "Confirm Sync", Default = true, Description = "Confirm before synchronizing." },
            }
        };

        RegisterSchema(editorSchema);
        RegisterSchema(appearanceSchema);
        RegisterSchema(terminalSchema);
        RegisterSchema(gitSchema);
    }

    #endregion
}
