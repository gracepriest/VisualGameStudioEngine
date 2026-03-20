using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for managing IDE settings at different scopes with file watching and debounced save.
/// User settings: ~/.vgs/settings.json
/// Workspace settings: {projectRoot}/.vgs/settings.json
/// </summary>
public class SettingsService : ISettingsService, IDisposable
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

    private readonly string _userSettingsDir;
    private readonly string _userSettingsPath;
    private string? _workspacePath;

    private FileSystemWatcher? _userWatcher;
    private FileSystemWatcher? _workspaceWatcher;

    private CancellationTokenSource? _userSaveCts;
    private CancellationTokenSource? _workspaceSaveCts;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _disposed;

    // Debounce delay for saves
    private const int SaveDebounceMs = 500;

    public SettingsService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _userSettingsDir = Path.Combine(home, ".vgs");
        Directory.CreateDirectory(_userSettingsDir);
        _userSettingsPath = Path.Combine(_userSettingsDir, "settings.json");

        RegisterAllDefaultSchemas();
        SetupUserFileWatcher();
    }

    public event EventHandler<SettingChangedEventArgs>? SettingChanged;
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <summary>
    /// Raised when the workspace path changes (project opened/closed).
    /// The string argument is the new workspace path, or null if closed.
    /// </summary>
    public event EventHandler<string?>? WorkspacePathChanged;

    /// <summary>
    /// Gets the user settings file path.
    /// </summary>
    public string UserSettingsPath => _userSettingsPath;

    /// <summary>
    /// Gets the workspace settings file path, or null if no workspace is open.
    /// </summary>
    public string? WorkspaceSettingsPath =>
        _workspacePath != null ? Path.Combine(_workspacePath, ".vgs", "settings.json") : null;

    /// <summary>
    /// Gets whether a workspace is currently open.
    /// </summary>
    public bool HasWorkspace => _workspacePath != null;

    /// <summary>
    /// Gets the scope in which a setting is defined (for UI badges).
    /// Returns the highest-priority scope where the setting exists.
    /// </summary>
    public SettingsScope GetSettingScope(string key)
    {
        if (_workspaceSettings.ContainsKey(key))
            return SettingsScope.Workspace;
        if (_userSettings.ContainsKey(key))
            return SettingsScope.User;
        if (_defaultSettings.ContainsKey(key))
            return SettingsScope.Default;
        return SettingsScope.Default;
    }

    /// <summary>
    /// Returns true if the setting is overridden in workspace settings.
    /// </summary>
    public bool IsOverriddenInWorkspace(string key) => _workspaceSettings.ContainsKey(key);

    /// <summary>
    /// Returns true if the setting is modified from default in the given scope.
    /// </summary>
    public bool IsModifiedInScope(string key, SettingsScope scope)
    {
        var dict = GetDictForScope(scope);
        return dict != null && dict.ContainsKey(key);
    }

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

            // Handle numeric conversions
            if (typeof(T) == typeof(int) && value is long longVal)
                return (T)(object)(int)longVal;
            if (typeof(T) == typeof(int) && value is double dblVal)
                return (T)(object)(int)dblVal;
            if (typeof(T) == typeof(double) && value is int intVal)
                return (T)(object)(double)intVal;
            if (typeof(T) == typeof(bool) && value is bool boolVal)
                return (T)(object)boolVal;

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
        var dict = GetDictForScope(scope);
        if (dict == null) dict = _userSettings;

        var oldValue = GetFromDictionary(dict, key);
        SetToDictionary(dict, key, value);

        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, oldValue, value));
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(new[] { key }, scope));

        // Debounced save
        ScheduleSave(scope);
    }

    public void SetValue<T>(string key, T value)
    {
        Set(key, value, SettingsScope.User);
    }

    public void Remove(string key, SettingsScope scope = SettingsScope.User)
    {
        var dict = GetDictForScope(scope);
        if (dict == null) return;

        var oldValue = GetFromDictionary(dict, key);
        RemoveFromDictionary(dict, key);

        SettingChanged?.Invoke(this, new SettingChangedEventArgs(key, oldValue, null));
        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(new[] { key }, scope));

        ScheduleSave(scope);
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
        var affectedKeys = _userSettings.Keys.Concat(_workspaceSettings.Keys).Distinct().ToList();
        _userSettings.Clear();
        _workspaceSettings.Clear();
        _folderSettings.Clear();

        if (affectedKeys.Count > 0)
        {
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(affectedKeys, SettingsScope.User));
        }

        ScheduleSave(SettingsScope.User);
        ScheduleSave(SettingsScope.Workspace);
    }

    public async Task LoadAsync()
    {
        await LoadFromFileAsync(_userSettingsPath, _userSettings);
        await LoadWorkspaceSettingsAsync();
    }

    public async Task SaveAsync()
    {
        await SaveToFileAsync(_userSettingsPath, _userSettings);
        await SaveWorkspaceSettingsAsync();
    }

    /// <summary>
    /// Saves settings for a specific scope.
    /// </summary>
    public async Task SaveScopeAsync(SettingsScope scope)
    {
        switch (scope)
        {
            case SettingsScope.User:
                await SaveToFileAsync(_userSettingsPath, _userSettings);
                break;
            case SettingsScope.Workspace:
                await SaveWorkspaceSettingsAsync();
                break;
        }
    }

    public async Task ImportAsync(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            json = StripJsonComments(json);
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
        // Replace existing schema with same ID
        _schemas.RemoveAll(s => s.Id == schema.Id);
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
        return _schemas.OrderBy(s => s.Order).ToList();
    }

    public SettingsPropertySchema? GetPropertySchema(string key)
    {
        return _propertySchemas.TryGetValue(key, out var schema) ? schema : null;
    }

    public void SetWorkspacePath(string? path)
    {
        var oldPath = _workspacePath;
        _workspacePath = path;
        _workspaceSettings.Clear();
        TeardownWorkspaceWatcher();

        if (path != null)
        {
            _ = LoadWorkspaceSettingsAsync();
            SetupWorkspaceFileWatcher();
        }

        if (oldPath != path)
        {
            WorkspacePathChanged?.Invoke(this, path);

            // Fire SettingsChanged so consumers re-evaluate effective values
            var allKeys = _workspaceSettings.Keys.ToList();
            if (allKeys.Count > 0)
            {
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(allKeys, SettingsScope.Workspace));
            }
        }
    }

    /// <summary>
    /// Gets the raw JSON text for a scope's settings file.
    /// </summary>
    public string GetRawJson(SettingsScope scope)
    {
        try
        {
            var filePath = scope == SettingsScope.Workspace ? WorkspaceSettingsPath : _userSettingsPath;
            if (filePath != null && File.Exists(filePath))
            {
                return File.ReadAllText(filePath);
            }
        }
        catch { }

        return "{\n}";
    }

    /// <summary>
    /// Sets the raw JSON text for a scope's settings file.
    /// </summary>
    public async Task SetRawJsonAsync(string json, SettingsScope scope)
    {
        try
        {
            var stripped = StripJsonComments(json);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stripped, JsonOptions);
            if (settings == null) return;

            var dict = GetDictForScope(scope) ?? _userSettings;
            var affectedKeys = dict.Keys.Union(settings.Keys).Distinct().ToList();

            dict.Clear();
            foreach (var kvp in settings)
            {
                dict[kvp.Key] = kvp.Value;
            }

            // Save the raw JSON (preserving comments)
            var filePath = scope == SettingsScope.Workspace ? WorkspaceSettingsPath : _userSettingsPath;
            if (filePath != null)
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(filePath, json);
            }

            if (affectedKeys.Count > 0)
            {
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(affectedKeys, scope));
            }
        }
        catch
        {
            // Ignore parse errors
        }
    }

    /// <summary>
    /// Validates a JSON string and returns unknown keys.
    /// </summary>
    public List<string> ValidateJson(string json)
    {
        var unknownKeys = new List<string>();
        try
        {
            var stripped = StripJsonComments(json);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stripped, JsonOptions);
            if (settings != null)
            {
                foreach (var key in settings.Keys)
                {
                    if (!_propertySchemas.ContainsKey(key) && !_defaultSettings.ContainsKey(key))
                    {
                        unknownKeys.Add(key);
                    }
                }
            }
        }
        catch
        {
            // Parse error - handled by caller
        }
        return unknownKeys;
    }

    /// <summary>
    /// Gets all known setting keys for autocomplete.
    /// </summary>
    public IReadOnlyList<string> GetAllKnownKeys()
    {
        return _propertySchemas.Keys.OrderBy(k => k).ToList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _userWatcher?.Dispose();
        _workspaceWatcher?.Dispose();
        _userSaveCts?.Cancel();
        _userSaveCts?.Dispose();
        _workspaceSaveCts?.Cancel();
        _workspaceSaveCts?.Dispose();
        _saveLock.Dispose();
    }

    #region Private Methods

    private Dictionary<string, object?>? GetDictForScope(SettingsScope scope) => scope switch
    {
        SettingsScope.User => _userSettings,
        SettingsScope.Workspace => _workspaceSettings,
        SettingsScope.Folder => _folderSettings,
        SettingsScope.Default => _defaultSettings,
        _ => null
    };

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

        foreach (var kvp in _defaultSettings) result[kvp.Key] = kvp.Value;
        foreach (var kvp in _userSettings) result[kvp.Key] = kvp.Value;
        foreach (var kvp in _workspaceSettings) result[kvp.Key] = kvp.Value;
        foreach (var kvp in _folderSettings) result[kvp.Key] = kvp.Value;

        return result;
    }

    private static object? GetFromDictionary(Dictionary<string, object?> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            return value;
        }

        // Support dot-notation for nested JSON objects
        var parts = key.Split('.');
        if (parts.Length > 1)
        {
            var currentKey = "";
            for (int i = 0; i < parts.Length; i++)
            {
                currentKey = i == 0 ? parts[i] : currentKey + "." + parts[i];
                if (dict.TryGetValue(currentKey, out var current))
                {
                    if (i == parts.Length - 1)
                        return current;

                    if (current is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
                    {
                        var remaining = string.Join(".", parts.Skip(i + 1));
                        if (jsonElement.TryGetProperty(remaining, out var nestedValue))
                            return nestedValue;
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

    /// <summary>
    /// Strip single-line (//) and multi-line (/* */) comments from JSON.
    /// </summary>
    private static string StripJsonComments(string json)
    {
        // Remove single-line comments (// ...)
        // Be careful not to strip inside strings
        var result = new System.Text.StringBuilder();
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (escaped)
            {
                result.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                result.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"' && !escaped)
            {
                inString = !inString;
                result.Append(c);
                continue;
            }

            if (!inString)
            {
                // Single-line comment
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '/')
                {
                    // Skip to end of line
                    while (i < json.Length && json[i] != '\n')
                        i++;
                    if (i < json.Length)
                        result.Append('\n');
                    continue;
                }

                // Multi-line comment
                if (c == '/' && i + 1 < json.Length && json[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < json.Length && !(json[i] == '*' && json[i + 1] == '/'))
                        i++;
                    i += 1; // skip closing /
                    continue;
                }
            }

            result.Append(c);
        }

        // Also strip trailing commas before } or ]
        var cleaned = Regex.Replace(result.ToString(), @",\s*([\}\]])", "$1");
        return cleaned;
    }

    private async Task LoadFromFileAsync(string filePath, Dictionary<string, object?> dict)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            json = StripJsonComments(json);
            var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (settings != null)
            {
                dict.Clear();
                foreach (var kvp in settings)
                {
                    dict[kvp.Key] = ConvertJsonElement(kvp.Value);
                }
            }
        }
        catch
        {
            // Ignore load errors
        }
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out int i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element // Keep complex types as JsonElement
        };
    }

    private async Task SaveToFileAsync(string filePath, Dictionary<string, object?> dict)
    {
        await _saveLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(dict, JsonOptions);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch
        {
            // Ignore save errors
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task LoadWorkspaceSettingsAsync()
    {
        if (string.IsNullOrEmpty(_workspacePath)) return;
        var settingsPath = Path.Combine(_workspacePath, ".vgs", "settings.json");
        await LoadFromFileAsync(settingsPath, _workspaceSettings);
    }

    private async Task SaveWorkspaceSettingsAsync()
    {
        if (string.IsNullOrEmpty(_workspacePath)) return;

        var settingsDir = Path.Combine(_workspacePath, ".vgs");
        var settingsPath = Path.Combine(settingsDir, "settings.json");

        if (_workspaceSettings.Count == 0)
        {
            // If no workspace settings remain, delete the file (but keep directory)
            try
            {
                if (File.Exists(settingsPath))
                    File.Delete(settingsPath);
            }
            catch { }
            return;
        }

        // Create .vgs directory on first write
        bool dirCreated = !Directory.Exists(settingsDir);
        await SaveToFileAsync(settingsPath, _workspaceSettings);

        // Start watching if we just created the directory
        if (dirCreated && _workspaceWatcher == null)
        {
            SetupWorkspaceFileWatcher();
        }
    }

    private void ScheduleSave(SettingsScope scope)
    {
        if (scope == SettingsScope.User)
        {
            _userSaveCts?.Cancel();
            _userSaveCts = new CancellationTokenSource();
            var token = _userSaveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SaveDebounceMs, token);
                    if (!token.IsCancellationRequested)
                        await SaveToFileAsync(_userSettingsPath, _userSettings);
                }
                catch (OperationCanceledException) { }
            });
        }
        else if (scope == SettingsScope.Workspace)
        {
            _workspaceSaveCts?.Cancel();
            _workspaceSaveCts = new CancellationTokenSource();
            var token = _workspaceSaveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(SaveDebounceMs, token);
                    if (!token.IsCancellationRequested)
                        await SaveWorkspaceSettingsAsync();
                }
                catch (OperationCanceledException) { }
            });
        }
    }

    #endregion

    #region File Watchers

    private void SetupUserFileWatcher()
    {
        try
        {
            _userWatcher = new FileSystemWatcher(_userSettingsDir, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _userWatcher.Changed += OnUserSettingsFileChanged;
        }
        catch
        {
            // Watcher setup may fail on some platforms
        }
    }

    private void SetupWorkspaceFileWatcher()
    {
        if (string.IsNullOrEmpty(_workspacePath)) return;

        try
        {
            var vgsDir = Path.Combine(_workspacePath, ".vgs");

            // Only watch if the .vgs directory already exists
            // (it will be created on first workspace setting write)
            if (Directory.Exists(vgsDir))
            {
                _workspaceWatcher = new FileSystemWatcher(vgsDir, "settings.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                _workspaceWatcher.Changed += OnWorkspaceSettingsFileChanged;
            }
        }
        catch
        {
            // Watcher setup may fail
        }
    }

    private void TeardownWorkspaceWatcher()
    {
        if (_workspaceWatcher != null)
        {
            _workspaceWatcher.Changed -= OnWorkspaceSettingsFileChanged;
            _workspaceWatcher.Dispose();
            _workspaceWatcher = null;
        }
    }

    private void OnUserSettingsFileChanged(object sender, FileSystemEventArgs e)
    {
        // Reload on external change (debounced via semaphore)
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100); // small delay for file to be released
                await LoadFromFileAsync(_userSettingsPath, _userSettings);
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(
                    _userSettings.Keys.ToList(), SettingsScope.User));
            }
            catch { }
        });
    }

    private void OnWorkspaceSettingsFileChanged(object sender, FileSystemEventArgs e)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(100);
                await LoadWorkspaceSettingsAsync();
                SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(
                    _workspaceSettings.Keys.ToList(), SettingsScope.Workspace));
            }
            catch { }
        });
    }

    #endregion

    #region Default Schema Registration

    private void RegisterAllDefaultSchemas()
    {
        RegisterSchema(new SettingsSchema
        {
            Id = "editor",
            Title = "Editor",
            Description = "Code editor settings",
            Order = 1,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("editor.fontSize", SettingsPropertyType.Integer, "Font Size", 14, "Controls the font size in pixels.", min: 6, max: 72),
                Prop("editor.fontFamily", SettingsPropertyType.String, "Font Family", "Cascadia Code", "Controls the font family."),
                Prop("editor.fontLigatures", SettingsPropertyType.Boolean, "Font Ligatures", false, "Enable font ligatures for supported fonts (Cascadia Code, Fira Code, JetBrains Mono)."),
                Prop("editor.tabSize", SettingsPropertyType.Integer, "Tab Size", 4, "The number of spaces a tab is equal to.", min: 1, max: 16),
                Prop("editor.insertSpaces", SettingsPropertyType.Boolean, "Insert Spaces", true, "Insert spaces when pressing Tab."),
                Prop("editor.wordWrap", SettingsPropertyType.String, "Word Wrap", "off", "Controls how lines should wrap.",
                    enumVals: new[] { "off", "on", "wordWrapColumn" }),
                Prop("editor.wordWrapColumn", SettingsPropertyType.Integer, "Word Wrap Column", 80, "Controls the wrapping column when wordWrap is set to wordWrapColumn.", min: 1, max: 200),
                Prop("editor.lineNumbers", SettingsPropertyType.String, "Line Numbers", "on", "Controls the display of line numbers.",
                    enumVals: new[] { "on", "off", "relative" }),
                Prop("editor.minimap.enabled", SettingsPropertyType.Boolean, "Minimap Enabled", true, "Controls whether the minimap is shown."),
                Prop("editor.minimap.side", SettingsPropertyType.String, "Minimap Side", "right", "Controls the side where the minimap is rendered.",
                    enumVals: new[] { "left", "right" }),
                Prop("editor.bracketPairColorization", SettingsPropertyType.Boolean, "Bracket Pair Colorization", true, "Enable bracket pair colorization."),
                Prop("editor.stickyScroll.enabled", SettingsPropertyType.Boolean, "Sticky Scroll", true, "Pin enclosing scope headers at the top of the editor."),
                Prop("editor.formatOnSave", SettingsPropertyType.Boolean, "Format On Save", false, "Format a file on save."),
                Prop("editor.trimTrailingWhitespaceOnSave", SettingsPropertyType.Boolean, "Trim Trailing Whitespace", false, "Remove trailing whitespace from all lines when saving a file."),
                Prop("editor.formatOnType", SettingsPropertyType.Boolean, "Format On Type", true, "Format the line after typing a semicolon or newline."),
                Prop("editor.autoClosingBrackets", SettingsPropertyType.String, "Auto Closing Brackets", "always", "Controls auto closing of brackets.",
                    enumVals: new[] { "always", "languageDefined", "beforeWhitespace", "never" }),
                Prop("editor.autoClosingQuotes", SettingsPropertyType.String, "Auto Closing Quotes", "always", "Controls auto closing of quotes.",
                    enumVals: new[] { "always", "languageDefined", "beforeWhitespace", "never" }),
                Prop("editor.smoothScrolling", SettingsPropertyType.Boolean, "Smooth Scrolling", true, "Animate scrolling for a smoother experience."),
                Prop("editor.cursorBlinking", SettingsPropertyType.String, "Cursor Blinking", "blink", "Controls cursor blinking animation style.",
                    enumVals: new[] { "blink", "smooth", "phase", "expand", "solid" }),
                Prop("editor.renderWhitespace", SettingsPropertyType.String, "Render Whitespace", "none", "Controls rendering of whitespace characters.",
                    enumVals: new[] { "none", "all", "boundary", "selection" }),
                Prop("editor.guides.indentation", SettingsPropertyType.Boolean, "Indentation Guides", true, "Controls whether the editor shows indentation guides."),
                Prop("editor.inlayHints.enabled", SettingsPropertyType.Boolean, "Inlay Hints", true, "Enable inlay hints in the editor."),
                Prop("editor.highlightCurrentLine", SettingsPropertyType.Boolean, "Highlight Current Line", true, "Highlight the active line."),
                Prop("editor.autoIndent", SettingsPropertyType.Boolean, "Auto Indent", true, "Automatically indent new lines."),
                Prop("editor.trimTrailingWhitespaceOnSave", SettingsPropertyType.Boolean, "Trim Trailing Whitespace", false, "Remove trailing whitespace when saving a file."),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "workbench",
            Title = "Workbench",
            Description = "Workbench appearance and behavior",
            Order = 2,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("workbench.colorTheme", SettingsPropertyType.String, "Color Theme", "Dark", "Specifies the overall color theme.",
                    enumVals: new[] { "Dark", "Light", "High Contrast" }),
                Prop("workbench.iconTheme", SettingsPropertyType.String, "Icon Theme", "default", "Specifies the file icon theme.",
                    enumVals: new[] { "default", "minimal", "none" }),
                Prop("workbench.startupEditor", SettingsPropertyType.String, "Startup Editor", "welcomePage", "Controls which editor is shown at startup.",
                    enumVals: new[] { "welcomePage", "none", "newUntitledFile" }),
                Prop("workbench.sideBar.location", SettingsPropertyType.String, "Side Bar Location", "left", "Controls the location of the sidebar.",
                    enumVals: new[] { "left", "right" }),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "terminal",
            Title = "Terminal",
            Description = "Integrated terminal settings",
            Order = 3,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("terminal.integrated.fontFamily", SettingsPropertyType.String, "Font Family", "", "Controls the font family of the terminal. Defaults to editor font."),
                Prop("terminal.integrated.fontSize", SettingsPropertyType.Integer, "Font Size", 14, "Controls the font size in pixels.", min: 6, max: 72),
                Prop("terminal.integrated.cursorStyle", SettingsPropertyType.String, "Cursor Style", "block", "Controls the style of terminal cursor.",
                    enumVals: new[] { "block", "underline", "line" }),
                Prop("terminal.integrated.defaultProfile", SettingsPropertyType.String, "Default Profile", "", "The default terminal profile."),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "files",
            Title = "Files",
            Description = "File handling settings",
            Order = 4,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("files.autoSave", SettingsPropertyType.String, "Auto Save", "off", "Controls auto save of editors.",
                    enumVals: new[] { "off", "afterDelay", "onFocusChange", "onWindowChange" }),
                Prop("files.autoSaveDelay", SettingsPropertyType.Integer, "Auto Save Delay", 1000, "Controls the delay in ms after which an editor is saved automatically.", min: 100),
                Prop("files.exclude", SettingsPropertyType.Object, "Files Exclude", null, "Configure glob patterns for excluding files and folders."),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "debug",
            Title = "Debug",
            Description = "Debugging settings",
            Order = 5,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("debug.console.fontSize", SettingsPropertyType.Integer, "Console Font Size", 14, "Controls the font size of the debug console.", min: 6, max: 72),
                Prop("debug.allowBreakpointsEverywhere", SettingsPropertyType.Boolean, "Allow Breakpoints Everywhere", false, "Allow setting breakpoints in any file."),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "basiclang",
            Title = "BasicLang",
            Description = "BasicLang compiler and language settings",
            Order = 6,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("basiclang.compiler.backend", SettingsPropertyType.String, "Compiler Backend", "CSharp", "The default compilation backend.",
                    enumVals: new[] { "CSharp", "MSIL", "LLVM", "CPP" }),
                Prop("basiclang.lsp.path", SettingsPropertyType.String, "LSP Server Path", "", "Path to the BasicLang LSP server executable. Leave empty for auto-detection."),
                Prop("basiclang.lsp.autoStart", SettingsPropertyType.Boolean, "Auto Start LSP", true, "Automatically start the LSP server when a BasicLang file is opened."),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "intellisense",
            Title = "IntelliSense",
            Description = "Auto-completion and code assistance settings",
            Order = 7,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("intellisense.autoComplete", SettingsPropertyType.Boolean, "Enable Auto Complete", true, "Show completion suggestions as you type."),
                Prop("intellisense.quickInfo", SettingsPropertyType.Boolean, "Show Quick Info", true, "Display type and documentation info on hover."),
                Prop("intellisense.signatureHelp", SettingsPropertyType.Boolean, "Show Signature Help", true, "Show parameter info when typing function arguments."),
                Prop("intellisense.delay", SettingsPropertyType.Integer, "Completion Delay", 200, "Delay in ms before showing completions.", min: 0, max: 2000),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "build",
            Title = "Build",
            Description = "Build settings",
            Order = 8,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("build.saveBeforeBuild", SettingsPropertyType.Boolean, "Save Before Build", true, "Automatically save all files before building."),
                Prop("build.showOutput", SettingsPropertyType.Boolean, "Show Build Output", true, "Show the output panel when a build starts."),
                Prop("build.defaultConfiguration", SettingsPropertyType.String, "Default Configuration", "Debug", "Default build configuration.",
                    enumVals: new[] { "Debug", "Release" }),
            }
        });

        RegisterSchema(new SettingsSchema
        {
            Id = "git",
            Title = "Git",
            Description = "Source control settings",
            Order = 9,
            Properties = new List<SettingsPropertySchema>
            {
                Prop("git.autoFetch", SettingsPropertyType.Boolean, "Auto Fetch", true, "Automatically fetch from remotes."),
                Prop("git.autoFetchInterval", SettingsPropertyType.Integer, "Auto Fetch Interval", 180, "Fetch interval in seconds.", min: 60),
                Prop("git.confirmSync", SettingsPropertyType.Boolean, "Confirm Sync", true, "Confirm before synchronizing."),
            }
        });
    }

    private static SettingsPropertySchema Prop(
        string key, SettingsPropertyType type, string title, object? defaultValue,
        string description, double? min = null, double? max = null, string[]? enumVals = null)
    {
        return new SettingsPropertySchema
        {
            Key = key,
            Type = type,
            Title = title,
            Default = defaultValue,
            Description = description,
            Minimum = min,
            Maximum = max,
            Enum = enumVals?.Select(v => (object)v).ToList()
        };
    }

    #endregion
}
