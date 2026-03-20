namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Manages IDE extensions/plugins with VS Code extension host support.
/// Extensions are Node.js packages that run in a separate process (Extension Host)
/// communicating with the IDE via JSON-RPC over stdin/stdout.
/// </summary>
public interface IExtensionService : IDisposable
{
    /// <summary>
    /// Gets all installed extensions.
    /// </summary>
    IReadOnlyList<Extension> InstalledExtensions { get; }

    /// <summary>
    /// Gets all enabled extensions.
    /// </summary>
    IReadOnlyList<Extension> EnabledExtensions { get; }

    /// <summary>
    /// Gets the extensions directory path.
    /// </summary>
    string ExtensionsDirectory { get; }

    /// <summary>
    /// Whether the Node.js extension host process is running.
    /// </summary>
    bool IsExtensionHostRunning { get; }

    /// <summary>
    /// Discovers and loads all extensions from the extensions directory.
    /// </summary>
    Task<IReadOnlyList<Extension>> DiscoverExtensionsAsync();

    /// <summary>
    /// Starts the extension host process and activates extensions that match startup events.
    /// </summary>
    Task StartExtensionHostAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the extension host process and deactivates all extensions.
    /// </summary>
    Task StopExtensionHostAsync();

    /// <summary>
    /// Restarts the extension host process.
    /// </summary>
    Task RestartExtensionHostAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs an extension from a file.
    /// </summary>
    /// <param name="packagePath">Path to the extension package (.vsix or .zip).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExtensionInstallResult> InstallFromFileAsync(string packagePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs an extension from a URL.
    /// </summary>
    /// <param name="url">URL to download the extension from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExtensionInstallResult> InstallFromUrlAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uninstalls an extension.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    Task<bool> UninstallAsync(string extensionId);

    /// <summary>
    /// Enables an extension.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    Task<bool> EnableAsync(string extensionId);

    /// <summary>
    /// Disables an extension.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    Task<bool> DisableAsync(string extensionId);

    /// <summary>
    /// Gets an extension by ID.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    Extension? GetExtension(string extensionId);

    /// <summary>
    /// Gets the parsed manifest for an extension.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    ExtensionManifest? GetExtensionManifest(string extensionId);

    /// <summary>
    /// Activates an extension (loads and initializes it in the extension host).
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    Task<bool> ActivateAsync(string extensionId);

    /// <summary>
    /// Deactivates an extension.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    Task<bool> DeactivateAsync(string extensionId);

    /// <summary>
    /// Executes a command contributed by an extension.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="args">Optional command arguments.</param>
    Task<object?> ExecuteExtensionCommandAsync(string commandId, object?[]? args = null);

    /// <summary>
    /// Triggers activation for extensions matching a specific event.
    /// </summary>
    /// <param name="activationEvent">The activation event (e.g., "onLanguage:python", "onCommand:myExt.run").</param>
    Task TriggerActivationEventAsync(string activationEvent);

    /// <summary>
    /// Discovers installed extensions and activates all static contributions
    /// (themes, grammars, snippets, language configs) without requiring Node.js.
    /// Call this on IDE startup.
    /// </summary>
    Task ActivateStaticContributionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for updates for all installed extensions.
    /// </summary>
    Task<IReadOnlyList<ExtensionUpdate>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an extension to the latest version.
    /// </summary>
    /// <param name="extensionId">The extension ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ExtensionInstallResult> UpdateAsync(string extensionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when an extension is installed.
    /// </summary>
    event EventHandler<ExtensionEventArgs>? ExtensionInstalled;

    /// <summary>
    /// Raised when an extension is uninstalled.
    /// </summary>
    event EventHandler<ExtensionEventArgs>? ExtensionUninstalled;

    /// <summary>
    /// Raised when an extension is enabled.
    /// </summary>
    event EventHandler<ExtensionEventArgs>? ExtensionEnabled;

    /// <summary>
    /// Raised when an extension is disabled.
    /// </summary>
    event EventHandler<ExtensionEventArgs>? ExtensionDisabled;

    /// <summary>
    /// Raised when an extension is activated.
    /// </summary>
    event EventHandler<ExtensionEventArgs>? ExtensionActivated;

    /// <summary>
    /// Raised when an extension is deactivated.
    /// </summary>
    event EventHandler<ExtensionEventArgs>? ExtensionDeactivated;

    /// <summary>
    /// Raised when the extension host process starts or stops.
    /// </summary>
    event EventHandler<bool>? ExtensionHostStateChanged;

    /// <summary>
    /// Raised when an extension sends a message to the IDE (e.g., showInformationMessage).
    /// </summary>
    event EventHandler<ExtensionMessageEventArgs>? ExtensionMessageReceived;

    /// <summary>
    /// Raised when static contributions (themes, grammars, snippets) are loaded
    /// from an extension, so the IDE can update its UI.
    /// </summary>
    event EventHandler<ExtensionContributionsLoadedEventArgs>? ContributionsLoaded;
}

#region Extension Types

/// <summary>
/// Represents an IDE extension.
/// </summary>
public class Extension
{
    /// <summary>
    /// Gets or sets the unique extension ID (publisher.name format).
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the description.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets the version.
    /// </summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Gets or sets the extension categories.
    /// </summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// Gets or sets the keywords for search.
    /// </summary>
    public List<string> Keywords { get; set; } = new();

    /// <summary>
    /// Gets or sets the icon path.
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Gets or sets the license.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets the repository URL.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the homepage URL.
    /// </summary>
    public string? Homepage { get; set; }

    /// <summary>
    /// Gets or sets the extension installation path.
    /// </summary>
    public string InstallPath { get; set; } = "";

    /// <summary>
    /// Gets or sets whether the extension is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the extension is active (loaded).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the current status of the extension.
    /// </summary>
    public ExtensionStatus Status { get; set; } = ExtensionStatus.Installed;

    /// <summary>
    /// Gets or sets whether this is a built-in extension.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Gets or sets the extension manifest.
    /// </summary>
    public ExtensionManifest? Manifest { get; set; }

    /// <summary>
    /// Gets or sets the activation events.
    /// </summary>
    public List<string> ActivationEvents { get; set; } = new();

    /// <summary>
    /// Gets or sets the extension dependencies.
    /// </summary>
    public List<ExtensionDependency> Dependencies { get; set; } = new();

    /// <summary>
    /// Gets or sets the contributed features.
    /// </summary>
    public ExtensionContributions Contributions { get; set; } = new();
}

/// <summary>
/// Extension manifest (package.json equivalent).
/// </summary>
public class ExtensionManifest
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string Publisher { get; set; } = "";
    public string? Main { get; set; }
    public string? Browser { get; set; }
    public List<string> Categories { get; set; } = new();
    public List<string> Keywords { get; set; } = new();
    public List<string> ActivationEvents { get; set; } = new();
    public ExtensionContributions? Contributes { get; set; }
    public Dictionary<string, string> Dependencies { get; set; } = new();
    public Dictionary<string, string> DevDependencies { get; set; } = new();
    public ExtensionEngines? Engines { get; set; }
    public string? Icon { get; set; }
    public string? License { get; set; }
    public ExtensionRepository? Repository { get; set; }
}

/// <summary>
/// Extension engines requirements.
/// </summary>
public class ExtensionEngines
{
    public string? Vscode { get; set; }
    public string? VisualGameStudio { get; set; }
}

/// <summary>
/// Extension repository info.
/// </summary>
public class ExtensionRepository
{
    public string Type { get; set; } = "git";
    public string Url { get; set; } = "";
}

/// <summary>
/// Extension dependency.
/// </summary>
public class ExtensionDependency
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsOptional { get; set; }
}

/// <summary>
/// Features contributed by an extension.
/// </summary>
public class ExtensionContributions
{
    public List<CommandContribution> Commands { get; set; } = new();
    public List<MenuContribution> Menus { get; set; } = new();
    public List<KeybindingContribution> Keybindings { get; set; } = new();
    public List<LanguageContribution> Languages { get; set; } = new();
    public List<GrammarContribution> Grammars { get; set; } = new();
    public List<ThemeContribution> Themes { get; set; } = new();
    public List<SnippetContribution> Snippets { get; set; } = new();
    public List<ViewContribution> Views { get; set; } = new();
    public List<ViewContainerContribution> ViewsContainers { get; set; } = new();
    public ConfigurationContribution? Configuration { get; set; }
    public List<DebuggerContribution> Debuggers { get; set; } = new();
    public List<TaskDefinitionContribution> TaskDefinitions { get; set; } = new();
    public List<ProblemMatcherContribution> ProblemMatchers { get; set; } = new();
}

/// <summary>
/// Command contribution.
/// </summary>
public class CommandContribution
{
    public string Command { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Category { get; set; }
    public string? Icon { get; set; }
    public string? EnablementCondition { get; set; }
}

/// <summary>
/// Menu contribution.
/// </summary>
public class MenuContribution
{
    public string MenuId { get; set; } = "";
    public string Command { get; set; } = "";
    public string? Group { get; set; }
    public string? When { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Keybinding contribution.
/// </summary>
public class KeybindingContribution
{
    public string Command { get; set; } = "";
    public string Key { get; set; } = "";
    public string? Mac { get; set; }
    public string? Linux { get; set; }
    public string? When { get; set; }
}

/// <summary>
/// Language contribution.
/// </summary>
public class LanguageContribution
{
    public string Id { get; set; } = "";
    public List<string> Extensions { get; set; } = new();
    public List<string> Aliases { get; set; } = new();
    public List<string> Filenames { get; set; } = new();
    public List<string> FirstLine { get; set; } = new();
    public string? Configuration { get; set; }
    public string? Icon { get; set; }
}

/// <summary>
/// Grammar contribution.
/// </summary>
public class GrammarContribution
{
    public string Language { get; set; } = "";
    public string ScopeName { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> EmbeddedLanguages { get; set; } = new();
    public List<string> TokenTypes { get; set; } = new();
}

/// <summary>
/// Theme contribution.
/// </summary>
public class ThemeContribution
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string UiTheme { get; set; } = "vs-dark";
    public string Path { get; set; } = "";
}

/// <summary>
/// Snippet contribution.
/// </summary>
public class SnippetContribution
{
    public string Language { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>
/// View contribution.
/// </summary>
public class ViewContribution
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? When { get; set; }
    public string? Icon { get; set; }
    public string? ContextualTitle { get; set; }
    public string? Visibility { get; set; }
}

/// <summary>
/// View container contribution.
/// </summary>
public class ViewContainerContribution
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Location { get; set; } = "activitybar"; // activitybar, panel
}

/// <summary>
/// Configuration contribution.
/// </summary>
public class ConfigurationContribution
{
    public string Title { get; set; } = "";
    public int Order { get; set; }
    public Dictionary<string, ConfigurationProperty> Properties { get; set; } = new();
}

/// <summary>
/// Configuration property.
/// </summary>
public class ConfigurationProperty
{
    public string Type { get; set; } = "string";
    public object? Default { get; set; }
    public string? Description { get; set; }
    public string? MarkdownDescription { get; set; }
    public List<object>? Enum { get; set; }
    public List<string>? EnumDescriptions { get; set; }
    public object? Minimum { get; set; }
    public object? Maximum { get; set; }
    public string? Pattern { get; set; }
    public string? Scope { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Debugger contribution.
/// </summary>
public class DebuggerContribution
{
    public string Type { get; set; } = "";
    public string Label { get; set; } = "";
    public string? Program { get; set; }
    public string? Runtime { get; set; }
    public List<string> Languages { get; set; } = new();
    public List<DebuggerConfiguration> ConfigurationAttributes { get; set; } = new();
    public List<DebuggerConfiguration> InitialConfigurations { get; set; } = new();
}

/// <summary>
/// Debugger configuration.
/// </summary>
public class DebuggerConfiguration
{
    public string Type { get; set; } = "";
    public string Request { get; set; } = "launch";
    public string Name { get; set; } = "";
    public Dictionary<string, object> Properties { get; set; } = new();
}

/// <summary>
/// Task definition contribution.
/// </summary>
public class TaskDefinitionContribution
{
    public string Type { get; set; } = "";
    public Dictionary<string, ConfigurationProperty> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

/// <summary>
/// Problem matcher contribution.
/// </summary>
public class ProblemMatcherContribution
{
    public string Name { get; set; } = "";
    public string? Label { get; set; }
    public string Owner { get; set; } = "";
    public string? FileLocation { get; set; }
    public ProblemPattern? Pattern { get; set; }
}

/// <summary>
/// Problem pattern.
/// </summary>
public class ProblemPattern
{
    public string Regexp { get; set; } = "";
    public int File { get; set; }
    public int Line { get; set; }
    public int? Column { get; set; }
    public int? EndLine { get; set; }
    public int? EndColumn { get; set; }
    public int? Severity { get; set; }
    public int Message { get; set; }
}

#endregion

#region Result Types

/// <summary>
/// Result of installing an extension.
/// </summary>
public class ExtensionInstallResult
{
    public bool Success { get; set; }
    public Extension? Extension { get; set; }
    public string? Error { get; set; }
    public bool RequiresRestart { get; set; }
}

/// <summary>
/// Available extension update.
/// </summary>
public class ExtensionUpdate
{
    public Extension Extension { get; set; } = new();
    public string CurrentVersion { get; set; } = "";
    public string NewVersion { get; set; } = "";
    public string? ChangeLog { get; set; }
}

#endregion

#region Event Args

/// <summary>
/// Extension event args.
/// </summary>
public class ExtensionEventArgs : EventArgs
{
    public Extension Extension { get; }

    public ExtensionEventArgs(Extension extension)
    {
        Extension = extension;
    }
}

/// <summary>
/// Event args for messages from extensions (showInformationMessage, showErrorMessage, etc.).
/// </summary>
public class ExtensionMessageEventArgs : EventArgs
{
    /// <summary>
    /// The extension that sent the message.
    /// </summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>
    /// Message severity: "info", "warning", "error".
    /// </summary>
    public string Severity { get; set; } = "info";

    /// <summary>
    /// The message text.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Optional action buttons (e.g., "Yes", "No", "Retry").
    /// </summary>
    public List<string> Actions { get; set; } = new();

    /// <summary>
    /// Task completion source to return the selected action back to the extension.
    /// </summary>
    public TaskCompletionSource<string?>? ResponseSource { get; set; }
}

/// <summary>
/// Event args for when static contributions are loaded from an extension.
/// </summary>
public class ExtensionContributionsLoadedEventArgs : EventArgs
{
    public Extension Extension { get; }
    public int ThemesLoaded { get; set; }
    public int GrammarsLoaded { get; set; }
    public int SnippetsLoaded { get; set; }
    public int LanguageConfigsLoaded { get; set; }

    public ExtensionContributionsLoadedEventArgs(Extension extension)
    {
        Extension = extension;
    }
}

/// <summary>
/// Status of an extension in its lifecycle.
/// </summary>
public enum ExtensionStatus
{
    /// <summary>Extension is installed but not loaded.</summary>
    Installed,

    /// <summary>Extension is currently being activated.</summary>
    Activating,

    /// <summary>Extension is active and running in the extension host.</summary>
    Active,

    /// <summary>Extension is disabled by the user.</summary>
    Disabled,

    /// <summary>Extension encountered an error during activation.</summary>
    Error,

    /// <summary>Extension is being deactivated.</summary>
    Deactivating
}

#endregion
