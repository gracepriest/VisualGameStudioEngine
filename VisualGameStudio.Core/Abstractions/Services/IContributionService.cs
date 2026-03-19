namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Manages extension contribution points — registers commands, menus, keybindings,
/// themes, snippets, languages, and configuration from VS Code-compatible extensions.
/// </summary>
public interface IContributionService
{
    /// <summary>
    /// Registers a command from an extension.
    /// </summary>
    /// <param name="extensionId">The extension that owns this command.</param>
    /// <param name="id">The command identifier (e.g., "myExtension.doSomething").</param>
    /// <param name="title">The display title.</param>
    /// <param name="category">Optional category for grouping in the Command Palette.</param>
    /// <param name="icon">Optional icon path relative to extension root.</param>
    /// <param name="keybinding">Optional default keybinding string (e.g., "Ctrl+Shift+K").</param>
    void RegisterCommand(string extensionId, string id, string title, string? category = null, string? icon = null, string? keybinding = null);

    /// <summary>
    /// Unregisters a command.
    /// </summary>
    void UnregisterCommand(string id);

    /// <summary>
    /// Registers a menu contribution.
    /// </summary>
    /// <param name="extensionId">The owning extension.</param>
    /// <param name="menuId">The target menu (e.g., "editor/context", "view/title").</param>
    /// <param name="commandId">The command to add to the menu.</param>
    /// <param name="when">Optional when-clause for visibility.</param>
    /// <param name="group">Optional menu group for separator placement.</param>
    /// <param name="order">Sort order within the group.</param>
    void RegisterMenu(string extensionId, string menuId, string commandId, string? when = null, string? group = null, int order = 0);

    /// <summary>
    /// Registers a keybinding from an extension.
    /// </summary>
    /// <param name="extensionId">The owning extension.</param>
    /// <param name="commandId">The command to bind.</param>
    /// <param name="key">The key chord string (e.g., "Ctrl+Shift+P").</param>
    /// <param name="when">Optional when-clause context.</param>
    void RegisterKeybinding(string extensionId, string commandId, string key, string? when = null);

    /// <summary>
    /// Registers a color theme from an extension.
    /// </summary>
    /// <param name="extensionId">The owning extension.</param>
    /// <param name="id">The theme identifier.</param>
    /// <param name="label">The display label for the theme.</param>
    /// <param name="path">Path to the theme JSON file relative to extension root.</param>
    /// <param name="uiTheme">Base theme: "vs-dark", "vs", or "hc-black".</param>
    void RegisterTheme(string extensionId, string id, string label, string path, string uiTheme);

    /// <summary>
    /// Registers a snippet file from an extension.
    /// </summary>
    /// <param name="extensionId">The owning extension.</param>
    /// <param name="language">The language ID for these snippets.</param>
    /// <param name="snippetFilePath">Absolute path to the snippet JSON file.</param>
    void RegisterSnippet(string extensionId, string language, string snippetFilePath);

    /// <summary>
    /// Registers a language contribution from an extension.
    /// </summary>
    /// <param name="extensionId">The owning extension.</param>
    /// <param name="id">The language identifier (e.g., "javascript").</param>
    /// <param name="extensions">File extensions (e.g., [".js", ".mjs"]).</param>
    /// <param name="aliases">Display names (e.g., ["JavaScript", "JS"]).</param>
    /// <param name="configuration">Path to language-configuration.json relative to extension root.</param>
    /// <param name="grammarPath">Path to the TextMate grammar file relative to extension root.</param>
    /// <param name="grammarScopeName">The TextMate scope name (e.g., "source.js").</param>
    void RegisterLanguage(string extensionId, string id, string[] extensions, string[]? aliases = null,
        string? configuration = null, string? grammarPath = null, string? grammarScopeName = null);

    /// <summary>
    /// Registers configuration properties from an extension.
    /// </summary>
    /// <param name="extensionId">The owning extension.</param>
    /// <param name="section">The configuration section title.</param>
    /// <param name="properties">The configuration properties keyed by setting path.</param>
    void RegisterConfiguration(string extensionId, string section, Dictionary<string, ConfigurationProperty> properties);

    /// <summary>
    /// Gets all registered extension commands.
    /// </summary>
    IReadOnlyList<ExtensionCommand> GetCommands();

    /// <summary>
    /// Gets all registered extension commands for a specific extension.
    /// </summary>
    IReadOnlyList<ExtensionCommand> GetCommands(string extensionId);

    /// <summary>
    /// Gets all registered extension themes.
    /// </summary>
    IReadOnlyList<ExtensionThemeRegistration> GetThemes();

    /// <summary>
    /// Gets all registered extension snippet registrations.
    /// </summary>
    IReadOnlyList<ExtensionSnippetRegistration> GetSnippets();

    /// <summary>
    /// Gets all registered extension language contributions.
    /// </summary>
    IReadOnlyList<ExtensionLanguageRegistration> GetLanguages();

    /// <summary>
    /// Gets all registered extension menu contributions.
    /// </summary>
    IReadOnlyList<ExtensionMenuRegistration> GetMenus();

    /// <summary>
    /// Gets menus for a specific menu location.
    /// </summary>
    IReadOnlyList<ExtensionMenuRegistration> GetMenus(string menuId);

    /// <summary>
    /// Applies all contributions from an extension manifest.
    /// This is the main entry point called when an extension is activated.
    /// </summary>
    /// <param name="manifest">The parsed extension manifest (package.json).</param>
    /// <param name="extensionId">The extension ID (publisher.name).</param>
    /// <param name="extensionPath">The absolute path to the extension root directory.</param>
    void ApplyContributions(ExtensionManifest manifest, string extensionId, string extensionPath);

    /// <summary>
    /// Removes all contributions from an extension.
    /// Called when an extension is deactivated or uninstalled.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    void RemoveContributions(string extensionId);

    /// <summary>
    /// Raised when a new command is registered.
    /// </summary>
    event EventHandler<ContributionChangedEventArgs>? CommandRegistered;

    /// <summary>
    /// Raised when a new theme is registered.
    /// </summary>
    event EventHandler<ContributionChangedEventArgs>? ThemeRegistered;

    /// <summary>
    /// Raised when a new language is registered.
    /// </summary>
    event EventHandler<ContributionChangedEventArgs>? LanguageRegistered;

    /// <summary>
    /// Raised when contributions are removed.
    /// </summary>
    event EventHandler<ContributionChangedEventArgs>? ContributionsRemoved;
}

#region Contribution Registration Models

/// <summary>
/// A command registered by an extension, suitable for the Command Palette.
/// </summary>
public class ExtensionCommand
{
    /// <summary>
    /// Gets or sets the owning extension ID.
    /// </summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>
    /// Gets or sets the command identifier.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the display title.
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Gets or sets the category (e.g., "Git", "Debug").
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Gets or sets the icon path (absolute).
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the default keybinding string.
    /// </summary>
    public string? Keybinding { get; set; }

    /// <summary>
    /// Gets the formatted title for Command Palette display.
    /// </summary>
    public string DisplayTitle => Category != null ? $"{Category}: {Title}" : Title;
}

/// <summary>
/// A menu item registered by an extension.
/// </summary>
public class ExtensionMenuRegistration
{
    public string ExtensionId { get; set; } = "";
    public string MenuId { get; set; } = "";
    public string CommandId { get; set; } = "";
    public string? When { get; set; }
    public string? Group { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// A theme registered by an extension.
/// </summary>
public class ExtensionThemeRegistration
{
    public string ExtensionId { get; set; } = "";
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
    public string UiTheme { get; set; } = "vs-dark";
    public bool IsLoaded { get; set; }
}

/// <summary>
/// A snippet file registered by an extension.
/// </summary>
public class ExtensionSnippetRegistration
{
    public string ExtensionId { get; set; } = "";
    public string Language { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsLoaded { get; set; }
}

/// <summary>
/// A language registered by an extension.
/// </summary>
public class ExtensionLanguageRegistration
{
    public string ExtensionId { get; set; } = "";
    public string Id { get; set; } = "";
    public List<string> Extensions { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
    public string? ConfigurationPath { get; set; }
    public string? GrammarPath { get; set; }
    public string? GrammarScopeName { get; set; }
}

/// <summary>
/// Event args for contribution changes.
/// </summary>
public class ContributionChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the extension ID that owns the contribution.
    /// </summary>
    public string ExtensionId { get; }

    /// <summary>
    /// Gets the type of contribution that changed.
    /// </summary>
    public ContributionType Type { get; }

    /// <summary>
    /// Gets the ID of the specific contribution (command ID, theme ID, etc.).
    /// </summary>
    public string? ContributionId { get; }

    public ContributionChangedEventArgs(string extensionId, ContributionType type, string? contributionId = null)
    {
        ExtensionId = extensionId;
        Type = type;
        ContributionId = contributionId;
    }
}

/// <summary>
/// Types of extension contributions.
/// </summary>
public enum ContributionType
{
    Command,
    Menu,
    Keybinding,
    Theme,
    Snippet,
    Language,
    Grammar,
    Configuration,
    All
}

#endregion
