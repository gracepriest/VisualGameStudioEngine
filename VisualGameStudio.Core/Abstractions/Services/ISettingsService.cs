namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for managing IDE settings at different scopes.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Gets a setting value.
    /// </summary>
    /// <typeparam name="T">The type of the setting value.</typeparam>
    /// <param name="key">The setting key (dot-notation path).</param>
    /// <param name="defaultValue">Default value if setting doesn't exist.</param>
    /// <param name="scope">The scope to read from (defaults to effective value).</param>
    T Get<T>(string key, T defaultValue, SettingsScope scope = SettingsScope.Effective);

    /// <summary>
    /// Gets a setting value (legacy compatibility).
    /// </summary>
    T? GetValue<T>(string key, T? defaultValue = default);

    /// <summary>
    /// Gets a setting value as object.
    /// </summary>
    object? Get(string key, SettingsScope scope = SettingsScope.Effective);

    /// <summary>
    /// Sets a setting value.
    /// </summary>
    /// <typeparam name="T">The type of the setting value.</typeparam>
    /// <param name="key">The setting key (dot-notation path).</param>
    /// <param name="value">The value to set.</param>
    /// <param name="scope">The scope to write to.</param>
    void Set<T>(string key, T value, SettingsScope scope = SettingsScope.User);

    /// <summary>
    /// Sets a setting value (legacy compatibility).
    /// </summary>
    void SetValue<T>(string key, T value);

    /// <summary>
    /// Removes a setting.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <param name="scope">The scope to remove from.</param>
    void Remove(string key, SettingsScope scope = SettingsScope.User);

    /// <summary>
    /// Checks if a setting exists.
    /// </summary>
    bool Has(string key, SettingsScope scope = SettingsScope.Effective);

    /// <summary>
    /// Gets all settings for a scope.
    /// </summary>
    IReadOnlyDictionary<string, object?> GetAll(SettingsScope scope = SettingsScope.Effective);

    /// <summary>
    /// Gets all settings with a specific prefix.
    /// </summary>
    IReadOnlyDictionary<string, object?> GetSection(string prefix, SettingsScope scope = SettingsScope.Effective);

    /// <summary>
    /// Resets a setting to its default value.
    /// </summary>
    void ResetToDefault(string key);

    /// <summary>
    /// Resets all settings to defaults.
    /// </summary>
    void ResetAllToDefaults();

    /// <summary>
    /// Loads settings from disk.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves settings to disk.
    /// </summary>
    Task SaveAsync();

    /// <summary>
    /// Imports settings from a file.
    /// </summary>
    Task ImportAsync(string filePath);

    /// <summary>
    /// Exports settings to a file.
    /// </summary>
    Task ExportAsync(string filePath, SettingsScope scope = SettingsScope.User);

    /// <summary>
    /// Registers a settings schema for validation and defaults.
    /// </summary>
    void RegisterSchema(SettingsSchema schema);

    /// <summary>
    /// Gets all registered settings schemas.
    /// </summary>
    IReadOnlyList<SettingsSchema> GetSchemas();

    /// <summary>
    /// Gets the schema for a setting.
    /// </summary>
    SettingsPropertySchema? GetPropertySchema(string key);

    /// <summary>
    /// Sets the workspace settings folder path.
    /// </summary>
    void SetWorkspacePath(string? path);

    /// <summary>
    /// Raised when a setting changes.
    /// </summary>
    event EventHandler<SettingChangedEventArgs>? SettingChanged;

    /// <summary>
    /// Raised when multiple settings change.
    /// </summary>
    event EventHandler<SettingsChangedEventArgs>? SettingsChanged;
}

#region Settings Types

/// <summary>
/// Scope for settings.
/// </summary>
public enum SettingsScope
{
    /// <summary>
    /// Default settings (built-in).
    /// </summary>
    Default,

    /// <summary>
    /// User-level settings.
    /// </summary>
    User,

    /// <summary>
    /// Workspace-level settings.
    /// </summary>
    Workspace,

    /// <summary>
    /// Folder-specific settings.
    /// </summary>
    Folder,

    /// <summary>
    /// Effective value (combines all scopes).
    /// </summary>
    Effective
}

/// <summary>
/// Settings schema for an extension or feature.
/// </summary>
public class SettingsSchema
{
    /// <summary>
    /// Gets or sets the schema ID (extension ID or feature name).
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets the properties in this schema.
    /// </summary>
    public List<SettingsPropertySchema> Properties { get; set; } = new();
}

/// <summary>
/// Schema for a single settings property.
/// </summary>
public class SettingsPropertySchema
{
    /// <summary>
    /// Gets or sets the property key (dot-notation path).
    /// </summary>
    public string Key { get; set; } = "";

    /// <summary>
    /// Gets or sets the property type.
    /// </summary>
    public SettingsPropertyType Type { get; set; } = SettingsPropertyType.String;

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets the markdown description.
    /// </summary>
    public string? MarkdownDescription { get; set; }

    /// <summary>
    /// Gets or sets the default value.
    /// </summary>
    public object? Default { get; set; }

    /// <summary>
    /// Gets or sets the allowed values (for enum type).
    /// </summary>
    public List<object>? Enum { get; set; }

    /// <summary>
    /// Gets or sets the descriptions for enum values.
    /// </summary>
    public List<string>? EnumDescriptions { get; set; }

    /// <summary>
    /// Gets or sets the minimum value (for number type).
    /// </summary>
    public double? Minimum { get; set; }

    /// <summary>
    /// Gets or sets the maximum value (for number type).
    /// </summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Gets or sets the pattern (for string type).
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Gets or sets the item type (for array type).
    /// </summary>
    public SettingsPropertyType? Items { get; set; }

    /// <summary>
    /// Gets or sets whether this is deprecated.
    /// </summary>
    public bool Deprecated { get; set; }

    /// <summary>
    /// Gets or sets the deprecation message.
    /// </summary>
    public string? DeprecationMessage { get; set; }

    /// <summary>
    /// Gets or sets the applicable scope.
    /// </summary>
    public SettingsPropertyScope Scope { get; set; } = SettingsPropertyScope.Resource;

    /// <summary>
    /// Gets or sets the display order.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Gets or sets tags for grouping/filtering.
    /// </summary>
    public List<string> Tags { get; set; } = new();
}

/// <summary>
/// Settings property types.
/// </summary>
public enum SettingsPropertyType
{
    String,
    Number,
    Integer,
    Boolean,
    Array,
    Object,
    Null
}

/// <summary>
/// Settings property scope.
/// </summary>
public enum SettingsPropertyScope
{
    /// <summary>
    /// Application-wide setting.
    /// </summary>
    Application,

    /// <summary>
    /// Machine-specific setting.
    /// </summary>
    Machine,

    /// <summary>
    /// Window-specific setting.
    /// </summary>
    Window,

    /// <summary>
    /// Resource/file-specific setting.
    /// </summary>
    Resource,

    /// <summary>
    /// Language-specific setting.
    /// </summary>
    Language
}

/// <summary>
/// Event args for a single setting change.
/// </summary>
public class SettingChangedEventArgs : EventArgs
{
    public string Key { get; }
    public object? OldValue { get; }
    public object? NewValue { get; }

    public SettingChangedEventArgs(string key, object? oldValue, object? newValue)
    {
        Key = key;
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// Event args for multiple settings changes.
/// </summary>
public class SettingsChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the keys that changed.
    /// </summary>
    public IReadOnlyList<string> AffectedKeys { get; }

    /// <summary>
    /// Gets the scope where the change occurred.
    /// </summary>
    public SettingsScope Scope { get; }

    public SettingsChangedEventArgs(IReadOnlyList<string> affectedKeys, SettingsScope scope)
    {
        AffectedKeys = affectedKeys;
        Scope = scope;
    }
}

#endregion

/// <summary>
/// Common settings keys.
/// </summary>
public static class SettingsKeys
{
    public const string Theme = "Appearance.Theme";
    public const string FontFamily = "Editor.FontFamily";
    public const string FontSize = "Editor.FontSize";
    public const string TabSize = "Editor.TabSize";
    public const string InsertSpaces = "Editor.InsertSpaces";
    public const string ShowLineNumbers = "Editor.ShowLineNumbers";
    public const string HighlightCurrentLine = "Editor.HighlightCurrentLine";
    public const string WordWrap = "Editor.WordWrap";
    public const string AutoSave = "Editor.AutoSave";
    public const string AutoSaveDelay = "Editor.AutoSaveDelay";
    public const string FormatOnSave = "Editor.FormatOnSave";
    public const string FormatOnPaste = "Editor.FormatOnPaste";
    public const string RenderWhitespace = "Editor.RenderWhitespace";
    public const string ShowMinimap = "Editor.ShowMinimap";
    public const string MinimapSide = "Editor.MinimapSide";
    public const string LineHeight = "Editor.LineHeight";
    public const string CursorStyle = "Editor.CursorStyle";
    public const string CursorBlinking = "Editor.CursorBlinking";
    public const string SmoothScrolling = "Editor.SmoothScrolling";
    public const string MouseWheelZoom = "Editor.MouseWheelZoom";
    public const string BracketPairColorization = "Editor.BracketPairColorization";
    public const string AutoClosingBrackets = "Editor.AutoClosingBrackets";
    public const string AutoClosingQuotes = "Editor.AutoClosingQuotes";
    public const string AutoIndent = "Editor.AutoIndent";
    public const string RecentProjects = "RecentProjects";
    public const string LastOpenedProject = "LastOpenedProject";
    public const string WindowState = "Window.State";
    public const string WindowBounds = "Window.Bounds";
    public const string TerminalFontFamily = "Terminal.FontFamily";
    public const string TerminalFontSize = "Terminal.FontSize";
    public const string TerminalCursorStyle = "Terminal.CursorStyle";
    public const string TerminalShell = "Terminal.Shell";
    public const string GitAutoFetch = "Git.AutoFetch";
    public const string GitAutoFetchInterval = "Git.AutoFetchInterval";
    public const string GitConfirmSync = "Git.ConfirmSync";
    public const string DebugConsoleFont = "Debug.Console.FontFamily";
    public const string DebugConsoleFontSize = "Debug.Console.FontSize";
}
