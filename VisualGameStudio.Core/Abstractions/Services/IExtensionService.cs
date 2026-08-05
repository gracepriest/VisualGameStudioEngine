using System.Linq;
using System.Text.Json;

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
    /// Requests completion items from extension providers for the given document position.
    /// </summary>
    Task<JsonElement?> RequestCompletionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests hover information from extension providers for the given document position.
    /// </summary>
    Task<JsonElement?> RequestHoverAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests go-to-definition from extension providers for the given document position.
    /// </summary>
    Task<JsonElement?> RequestDefinitionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests find-references from extension providers for the given document position.
    /// </summary>
    Task<JsonElement?> RequestReferencesAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests document formatting from extension providers.
    /// </summary>
    Task<JsonElement?> RequestFormattingAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests document symbols from extension providers.
    /// </summary>
    Task<JsonElement?> RequestDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether any extension has registered providers for the given language.
    /// </summary>
    bool HasExtensionProviders(string languageId);

    /// <summary>
    /// Notifies extension providers that a document was opened.
    /// </summary>
    Task NotifyDocumentOpenedAsync(string uri, string languageId, int version, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies extension providers that a document was changed.
    /// </summary>
    Task NotifyDocumentChangedAsync(string uri, int version, string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies extension providers that a document was closed.
    /// </summary>
    Task NotifyDocumentClosedAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies extension providers that a document was saved.
    /// </summary>
    Task NotifyDocumentSavedAsync(string uri, string? text = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when an extension publishes diagnostics for a document.
    /// </summary>
    event EventHandler<ExtensionDiagnosticsEventArgs>? ExtensionDiagnosticsReceived;

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
    /// Gets all commands contributed by extensions (from package.json contributes.commands).
    /// </summary>
    IReadOnlyList<ContributedCommand> GetContributedCommands();

    /// <summary>
    /// Gets all keybindings contributed by extensions (from package.json contributes.keybindings).
    /// </summary>
    IReadOnlyList<ContributedKeybinding> GetContributedKeybindings();

    /// <summary>
    /// Gets all menu contributions from extensions (from package.json contributes.menus).
    /// </summary>
    IReadOnlyList<ContributedMenuItem> GetContributedMenuItems(string menuId);

    /// <summary>
    /// Notifies the extension system that a file with the given language ID was opened,
    /// triggering onLanguage activation events.
    /// </summary>
    Task NotifyLanguageOpenedAsync(string languageId);

    /// <summary>
    /// Notifies the extension system that a workspace was opened containing the given files,
    /// triggering workspaceContains activation events.
    /// </summary>
    Task NotifyWorkspaceOpenedAsync(string workspacePath);

    /// <summary>
    /// Executes a contributed command by ID. Activates the owning extension if needed.
    /// </summary>
    Task ExecuteContributedCommandAsync(string commandId);

    /// <summary>
    /// Requests the children of a tree view element from the extension host.
    /// Pass null for element to get root-level children.
    /// </summary>
    Task<JsonElement?> RequestTreeChildrenAsync(string viewId, string? element, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests the tree item representation for a given element.
    /// </summary>
    Task<JsonElement?> RequestTreeItemAsync(string viewId, string element, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when an extension creates a tree view (vscode.window.createTreeView).
    /// </summary>
    event EventHandler<ExtensionTreeViewEventArgs>? TreeViewCreated;

    /// <summary>
    /// Raised when an extension requests a tree view refresh.
    /// </summary>
    event EventHandler<ExtensionTreeViewEventArgs>? TreeViewRefreshRequested;

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

    /// <summary>
    /// Raised when an extension creates a webview panel via vscode.window.createWebviewPanel().
    /// </summary>
    event EventHandler<WebViewCreatedEventArgs>? WebViewCreated;

    /// <summary>
    /// Raised when an extension updates webview HTML content via webview.html = "...".
    /// </summary>
    event EventHandler<WebViewHtmlChangedEventArgs>? WebViewHtmlChanged;
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

    /// <summary>
    /// Either an object <c>{ "type": "git", "url": "..." }</c> or the npm SHORTHAND STRING
    /// <c>"https://github.com/owner/repo"</c>. This sits OUTSIDE <c>contributes</c>, so no
    /// contribution-level tolerance can rescue it — typed as an object only, the shorthand made the
    /// whole extension fail to bind. <c>VsixInstaller.VsixManifest</c> already tolerated both.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(ExtensionRepositoryConverter))]
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
/// A command contributed by an extension, ready for use in the command palette and menus.
/// </summary>
public class ContributedCommand
{
    /// <summary>The command identifier (e.g., "extension.sayHello").</summary>
    public string CommandId { get; set; } = "";

    /// <summary>The display title (e.g., "Say Hello").</summary>
    public string Title { get; set; } = "";

    /// <summary>Optional category for grouping (e.g., "My Extension").</summary>
    public string? Category { get; set; }

    /// <summary>The extension ID that contributed this command.</summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>Whether the owning extension has a Node.js entry point.</summary>
    public bool HasRuntime { get; set; }

    /// <summary>Display name formatted as "Category: Title".</summary>
    public string DisplayName => string.IsNullOrEmpty(Category) ? Title : $"{Category}: {Title}";
}

/// <summary>
/// A keybinding contributed by an extension.
/// </summary>
public class ContributedKeybinding
{
    /// <summary>The command identifier.</summary>
    public string CommandId { get; set; } = "";

    /// <summary>The key chord string (e.g., "ctrl+shift+h").</summary>
    public string Key { get; set; } = "";

    /// <summary>Optional Mac-specific key chord.</summary>
    public string? Mac { get; set; }

    /// <summary>Optional Linux-specific key chord.</summary>
    public string? Linux { get; set; }

    /// <summary>Optional when clause.</summary>
    public string? When { get; set; }

    /// <summary>The extension ID that contributed this keybinding.</summary>
    public string ExtensionId { get; set; } = "";
}

/// <summary>
/// A menu item contributed by an extension.
/// </summary>
public class ContributedMenuItem
{
    /// <summary>The menu location (e.g., "editor/context", "explorer/context").</summary>
    public string MenuId { get; set; } = "";

    /// <summary>The command identifier.</summary>
    public string CommandId { get; set; } = "";

    /// <summary>Optional group for ordering.</summary>
    public string? Group { get; set; }

    /// <summary>Optional when clause for visibility.</summary>
    public string? When { get; set; }

    /// <summary>The extension ID that contributed this menu item.</summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>The display title (resolved from the command contribution).</summary>
    public string Title { get; set; } = "";

    /// <summary>The category (resolved from the command contribution).</summary>
    public string? Category { get; set; }
}

/// <summary>
/// Features contributed by an extension.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(ExtensionContributionsConverter))]
public class ExtensionContributions
{
    public List<CommandContribution> Commands { get; set; } = new();

    /// <summary>
    /// An OBJECT MAP keyed by menu id — <c>{ "editor/context": [ ... ], "commandPalette": [ ... ] }</c>.
    /// There is NO array form in VS Code's schema. Declared as a <c>List&lt;MenuContribution&gt;</c>
    /// this could never have bound a real manifest: <see cref="MenuContribution.MenuId"/>
    /// corresponds to no JSON field at all — it IS the map key.
    /// </summary>
    public Dictionary<string, List<MenuContribution>> Menus { get; set; } = new();

    public List<KeybindingContribution> Keybindings { get; set; } = new();
    public List<LanguageContribution> Languages { get; set; } = new();
    public List<GrammarContribution> Grammars { get; set; } = new();
    public List<ThemeContribution> Themes { get; set; } = new();
    public List<SnippetContribution> Snippets { get; set; } = new();

    /// <summary>Object map keyed by container id — <c>{ "explorer": [ ... ] }</c>. Never an array.</summary>
    public Dictionary<string, List<ViewContribution>> Views { get; set; } = new();

    /// <summary>
    /// Object map keyed by location — <c>{ "activitybar": [ ... ], "panel": [ ... ] }</c>.
    /// As with <see cref="Menus"/>, <see cref="ViewContainerContribution.Location"/> is the map key,
    /// not a field.
    /// </summary>
    public Dictionary<string, List<ViewContainerContribution>> ViewsContainers { get; set; } = new();

    /// <summary>
    /// A single section object OR an array of them — both legal, and the array form is what
    /// multi-section extensions publish. Bound through
    /// <see cref="SingleOrArrayConverter{T}"/> so either shape yields a list.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(SingleOrArrayConverter<ConfigurationContribution>))]
    public List<ConfigurationContribution> Configuration { get; set; } = new();
    public List<DebuggerContribution> Debuggers { get; set; } = new();
    public List<TaskDefinitionContribution> TaskDefinitions { get; set; } = new();
    public List<ProblemMatcherContribution> ProblemMatchers { get; set; } = new();

    /// <summary>
    /// Sections that could not be bound and were degraded to their default. Populated by
    /// <see cref="ExtensionContributionsConverter"/> on the instance it is constructing — a
    /// converter is shared through static <see cref="JsonSerializerOptions"/>, so it can hold no
    /// per-parse state of its own and cannot reach a logger through DI; writing onto the object
    /// under construction is per-parse and thread-safe by construction.
    ///
    /// <para>Diagnostic state, not part of the manifest format, so it is never serialized.</para>
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<ContributionLoadError> LoadErrors { get; } = new();
}

/// <summary>
/// One contribution section that failed to bind. Deliberately strings only — holding the
/// <see cref="Exception"/> would keep a parse-time object graph alive on every loaded extension for
/// a value only ever rendered as text.
/// </summary>
/// <param name="Section">The manifest key, e.g. <c>"grammars"</c>.</param>
/// <param name="Message">The binding error, suitable for the Output pane.</param>
public sealed record ContributionLoadError(string Section, string Message);

/// <summary>
/// Command contribution.
/// </summary>
public class CommandContribution
{
    public string Command { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Category { get; set; }

    /// <summary>
    /// Either a string (an icon path or a <c>$(codicon)</c> reference) or an object
    /// <c>{ "light": "...", "dark": "..." }</c>. Both are legal and both are common — vscode.git
    /// and most icon-bearing extensions publish the object form. Kept as a raw
    /// <see cref="JsonElement"/> because nothing consumes it yet and typing it as a string made
    /// every such extension fail to load ENTIRELY: manifest binding is caught per-extension, so a
    /// decorative icon took down that extension's commands, grammars, themes and activation.
    /// </summary>
    public JsonElement? Icon { get; set; }

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
    /// <summary>A single regex matched against a file's first line — a STRING in VS Code, not a list.</summary>
    public string? FirstLine { get; set; }

    public string? Configuration { get; set; }

    /// <summary>String or <c>{ "light": ..., "dark": ... }</c> — see <see cref="CommandContribution.Icon"/>.</summary>
    public JsonElement? Icon { get; set; }
}

/// <summary>
/// Grammar contribution.
/// </summary>
public class GrammarContribution
{
    public string Language { get; set; } = "";
    public string ScopeName { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>
    /// Scope name -> language id, e.g. { "source.css": "css", "source.js": "javascript" }.
    /// An OBJECT in the VS Code schema, not an array. Typed as List&lt;string&gt; this threw, and
    /// since manifest parsing is caught per-EXTENSION that took all of vscode.html down with it —
    /// grammar, language and snippets included.
    /// </summary>
    public Dictionary<string, string> EmbeddedLanguages { get; set; } = new();

    /// <summary>Scope name -&gt; token type, e.g. { "meta.embedded.block.html": "other" }. Also an object.</summary>
    public Dictionary<string, string> TokenTypes { get; set; } = new();
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
    /// <summary>
    /// JSON Schema type. Accepts both the string form ("string") and the array form
    /// (["string", "null"]) — see <see cref="JsonSchemaTypeConverter"/> for why that matters.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(JsonSchemaTypeConverter))]
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
/// One theme contributed by an extension, as declared in its package.json.
///
/// <see cref="Label"/> is the manifest's own <c>contributes.themes[].label</c> and is the
/// identity the theme must register under. Deriving a name from the theme FILE instead loses
/// this: Dracula ships dracula.json and dracula-soft.json whose internal "name" fields are
/// both literally "Dracula", so the two collapsed onto one registry key and the second
/// silently overwrote the first. The manifest labels ("Dracula Theme" / "Dracula Theme Soft")
/// are distinct, which is why VS Code lists them separately.
/// </summary>
public sealed class ExtensionThemeContribution
{
    /// <summary>Absolute path to the theme JSON file.</summary>
    public string Path { get; init; } = "";

    /// <summary>Manifest label — the display name and registry key. Null falls back to the file's own name.</summary>
    public string? Label { get; init; }

    /// <summary>Manifest uiTheme ("vs", "vs-dark", "hc-black", "hc-light").</summary>
    public string? UiTheme { get; init; }
}

/// <summary>
/// Reads a JSON Schema "type" that may be either a string ("string") or an array of strings
/// (["string", "null"] for a nullable setting). Both forms are legal and both appear in real
/// VS Code manifests — vscode.html-language-features declares
/// <c>html.format.unformatted</c> as ["string", "null"].
///
/// Without this, deserializing the array form threw a JsonException, and because manifest
/// parsing is caught at the WHOLE-EXTENSION level, one optional field in a Settings-UI schema
/// aborted loading that extension's grammars, themes, commands and activation entirely.
///
/// The array form collapses to its first non-"null" entry, which is the type the settings UI
/// should present; "null" only signals nullability.
/// </summary>
public sealed class JsonSchemaTypeConverter : System.Text.Json.Serialization.JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString() ?? "string";
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            string? first = null;
            string? firstNonNull = null;

            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                {
                    reader.Skip();
                    continue;
                }

                if (reader.TokenType != JsonTokenType.String) continue;

                var value = reader.GetString();
                if (string.IsNullOrEmpty(value)) continue;

                first ??= value;
                if (firstNonNull == null && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
                {
                    firstNonNull = value;
                }
            }

            return firstNonNull ?? first ?? "string";
        }

        // Any other shape is not something the settings UI can render; skip it rather than
        // letting an unrecognised schema take the whole extension down.
        reader.Skip();
        return "string";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Binds <c>contributes</c> one section at a time so that a section which cannot bind costs only
/// itself.
///
/// <para>Without this, <c>JsonSerializer.Deserialize&lt;ExtensionManifest&gt;</c> is all-or-nothing
/// and the caller's catch is at WHOLE-EXTENSION scope, so a single unmodelled field aborts the bind
/// and the extension is never added at all. That is what made two cosmetic schema mismatches delete
/// <c>vscode.html</c> and <c>vscode.html-language-features</c> outright.</para>
///
/// <para>The real payoff is indirect: only <c>commands</c> and <c>keybindings</c> are read from this
/// DTO. Themes, snippets, menus, grammars and languages are re-parsed from raw JSON on paths that
/// ALREADY isolate per section — they simply never ran, because the bind threw first. Containing the
/// throw here is what makes that existing isolation reachable.</para>
/// </summary>
public sealed class ExtensionContributionsConverter
    : System.Text.Json.Serialization.JsonConverter<ExtensionContributions>
{
    /// <summary>
    /// The manifest keys this converter binds, matched case-insensitively.
    ///
    /// <para>Hard-coded rather than derived from the CLR property names: a naming policy does NOT
    /// apply inside a converter's own token walk, and deriving them would work only by coincidence
    /// for sections whose VS Code key diverges from their property name.
    /// <see cref="ExtensionContributions"/> gaining a section without a matching arm here is a
    /// silent bind-to-nothing, which is why a test pins this list against the DTO.</para>
    /// </summary>
    public static readonly IReadOnlyList<string> KnownSections = new[]
    {
        "commands", "menus", "keybindings", "languages", "grammars", "themes", "snippets",
        "views", "viewsContainers", "configuration", "debuggers", "taskDefinitions",
        "problemMatchers"
    };

    public override ExtensionContributions Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new ExtensionContributions();

        // FIRST statement: real manifests carry "contributes": [] or a string. Assuming an object
        // and walking anyway runs off the end of the value and throws out of the converter — making
        // this the very whole-extension kill path it exists to prevent. TrySkip, not Skip: Skip
        // throws on partial JSON, which is latent today but becomes live under a Stream overload.
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            var found = reader.TokenType;
            reader.TrySkip();
            result.LoadErrors.Add(new ContributionLoadError(
                "contributes", $"expected an object, found {found}; all contributions ignored"));
            return result;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            var name = reader.GetString() ?? "";
            if (!reader.Read()) break;

            // OUTSIDE the try, deliberately. ParseValue advances the reader to the last token of
            // the value before any binding runs, so a bind failure cannot move it — but a
            // SYNTACTICALLY broken subtree strands the reader mid-value, and the next iteration
            // would then throw something the catch below does not expect, escaping isolation
            // entirely. A malformed document is a whole-document failure and must propagate.
            var element = JsonElement.ParseValue(ref reader);

            var section = KnownSections.FirstOrDefault(
                s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));

            // Unknown sections are skipped in silence: the DTO models a fraction of VS Code's
            // schema, so warning on every unmodelled key would bury the ones that matter.
            if (section is null) continue;

            try
            {
                Bind(section, element, result, options);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException
                                          or NotSupportedException or ArgumentException)
            {
                // Broader than JsonException on purpose: a nested tolerant converter can surface
                // InvalidOperationException unwrapped, and JsonElement inspection throws it too.
                // Never bare Exception — that would swallow cancellation and OOM.
                result.LoadErrors.Add(new ContributionLoadError(section, ex.Message));
            }
        }

        return result;
    }

    /// <summary>
    /// Binds one section. Every nested call passes <paramref name="options"/>: the no-argument
    /// overload uses <see cref="JsonSerializerOptions.Default"/>, which has neither the camelCase
    /// policy nor case-insensitive matching, and would silently produce the right element count
    /// with every string property empty — no exception, no recorded error.
    /// </summary>
    private static void Bind(
        string section, JsonElement element, ExtensionContributions into, JsonSerializerOptions options)
    {
        switch (section)
        {
            case "commands":
                into.Commands = element.Deserialize<List<CommandContribution>>(options) ?? new();
                break;
            case "menus":
                into.Menus = element.Deserialize<Dictionary<string, List<MenuContribution>>>(options) ?? new();
                break;
            case "keybindings":
                into.Keybindings = element.Deserialize<List<KeybindingContribution>>(options) ?? new();
                break;
            case "languages":
                into.Languages = element.Deserialize<List<LanguageContribution>>(options) ?? new();
                break;
            case "grammars":
                into.Grammars = element.Deserialize<List<GrammarContribution>>(options) ?? new();
                break;
            case "themes":
                into.Themes = element.Deserialize<List<ThemeContribution>>(options) ?? new();
                break;
            case "snippets":
                into.Snippets = element.Deserialize<List<SnippetContribution>>(options) ?? new();
                break;
            case "views":
                into.Views = element.Deserialize<Dictionary<string, List<ViewContribution>>>(options) ?? new();
                break;
            case "viewsContainers":
                into.ViewsContainers =
                    element.Deserialize<Dictionary<string, List<ViewContainerContribution>>>(options) ?? new();
                break;
            case "configuration":
                // The property's own SingleOrArrayConverter attribute does not apply here — this
                // binds the value directly rather than through property metadata — so the
                // object-or-array tolerance is repeated explicitly.
                into.Configuration = element.ValueKind == JsonValueKind.Array
                    ? element.Deserialize<List<ConfigurationContribution>>(options) ?? new()
                    : element.ValueKind == JsonValueKind.Object
                        ? new List<ConfigurationContribution>
                            { element.Deserialize<ConfigurationContribution>(options)! }
                        : new List<ConfigurationContribution>();
                break;
            case "debuggers":
                into.Debuggers = element.Deserialize<List<DebuggerContribution>>(options) ?? new();
                break;
            case "taskDefinitions":
                into.TaskDefinitions = element.Deserialize<List<TaskDefinitionContribution>>(options) ?? new();
                break;
            case "problemMatchers":
                into.ProblemMatchers = element.Deserialize<List<ProblemMatcherContribution>>(options) ?? new();
                break;
        }
    }

    public override void Write(
        Utf8JsonWriter writer, ExtensionContributions value, JsonSerializerOptions options)
    {
        // Hand-written, and it MUST stay that way. A class-level [JsonConverter] binds serialization
        // too, and the natural body — JsonSerializer.Serialize(writer, value, options) — re-enters
        // this method and dies with a StackOverflowException. That is uncatchable in .NET, so it
        // terminates the IDE rather than failing one extension.
        //
        // NEVER pass ExtensionContributions as the type argument to Serialize or Deserialize from
        // inside this class. The section values below are all OTHER types, so they cannot re-enter.
        writer.WriteStartObject();

        WriteIfAny(writer, "commands", value.Commands, options);
        WriteIfAny(writer, "menus", value.Menus, options);
        WriteIfAny(writer, "keybindings", value.Keybindings, options);
        WriteIfAny(writer, "languages", value.Languages, options);
        WriteIfAny(writer, "grammars", value.Grammars, options);
        WriteIfAny(writer, "themes", value.Themes, options);
        WriteIfAny(writer, "snippets", value.Snippets, options);
        WriteIfAny(writer, "views", value.Views, options);
        WriteIfAny(writer, "viewsContainers", value.ViewsContainers, options);
        WriteIfAny(writer, "configuration", value.Configuration, options);
        WriteIfAny(writer, "debuggers", value.Debuggers, options);
        WriteIfAny(writer, "taskDefinitions", value.TaskDefinitions, options);
        WriteIfAny(writer, "problemMatchers", value.ProblemMatchers, options);

        // LoadErrors is deliberately absent: it is parse diagnostics, not manifest content.
        writer.WriteEndObject();
    }

    private static void WriteIfAny<T>(
        Utf8JsonWriter writer, string name, ICollection<T> value, JsonSerializerOptions options)
    {
        if (value is null || value.Count == 0) return;
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, options);
    }
}

/// <summary>
/// Binds a manifest field that is legally EITHER a single object OR an array of them, yielding a
/// list in both cases. VS Code's schema uses this shape for <c>contributes.configuration</c>:
/// single-section extensions publish an object, multi-section extensions publish an array, and a
/// DTO that accepts only one of them fails the whole extension on the other.
///
/// <para>Anything else — a string, a number, <c>null</c> — degrades to an empty list rather than
/// throwing. An unrecognised shape should cost that one section, never the extension.</para>
/// </summary>
public sealed class SingleOrArrayConverter<T> : System.Text.Json.Serialization.JsonConverter<List<T>>
{
    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Deserialize<T>/<List<T>> is called with `options` deliberately. Dropping it silently
        // binds every string property to "" — the right element count with blank values and no
        // error — because the no-argument overload uses JsonSerializerOptions.Default, which has
        // neither the camelCase naming policy nor case-insensitive matching.
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            return JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? new List<T>();
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var single = JsonSerializer.Deserialize<T>(ref reader, options);
            return single is null ? new List<T>() : new List<T> { single };
        }

        reader.Skip();
        return new List<T>();
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options)
    {
        // Serializing the ELEMENT type is safe: this converter is bound to List<T>, so it cannot
        // re-enter itself. Never pass List<T> here — that recurses into a StackOverflowException,
        // which is uncatchable in .NET and would take the IDE down rather than the extension.
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
    }
}

/// <summary>
/// Binds <c>repository</c>, which npm allows as either an object <c>{ "type", "url" }</c> or a bare
/// shorthand URL string. This field sits outside <c>contributes</c>, so nothing at the contribution
/// level can compensate: typed as an object only, a shorthand string aborted the entire manifest
/// bind and the extension vanished.
/// </summary>
public sealed class ExtensionRepositoryConverter : System.Text.Json.Serialization.JsonConverter<ExtensionRepository?>
{
    public override ExtensionRepository? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new ExtensionRepository { Url = reader.GetString() ?? "" };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            string type = "git";
            string url = "";

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                var name = reader.GetString();
                if (!reader.Read()) break;

                // Match case-insensitively: the naming policy on the options does NOT apply inside
                // a converter's own token walk, and manifests in the wild are not consistent.
                if (string.Equals(name, "url", StringComparison.OrdinalIgnoreCase)
                    && reader.TokenType == JsonTokenType.String)
                {
                    url = reader.GetString() ?? "";
                }
                else if (string.Equals(name, "type", StringComparison.OrdinalIgnoreCase)
                         && reader.TokenType == JsonTokenType.String)
                {
                    type = reader.GetString() ?? "git";
                }
                else
                {
                    reader.Skip();
                }
            }

            return new ExtensionRepository { Type = type, Url = url };
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, ExtensionRepository? value, JsonSerializerOptions options)
    {
        // Written by hand rather than delegating to Serialize(value): this converter is attached by
        // property attribute, and hand-writing keeps it immune to ever being promoted to a
        // type-level attribute, where delegation would self-recurse.
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WriteString("url", value.Url);
        writer.WriteEndObject();
    }
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
    /// <summary>
    /// A JSON-Schema-shaped OBJECT keyed by request type (<c>launch</c>/<c>attach</c>), not a list.
    /// Raw because nothing consumes it and the schema is open-ended.
    /// <c>Core/Extensions/VSCodeExtension.cs</c> already had this right.
    /// </summary>
    public JsonElement? ConfigurationAttributes { get; set; }

    /// <summary>An array of configurations OR a string naming a snippet. Raw for the same reason.</summary>
    public JsonElement? InitialConfigurations { get; set; }
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
    /// <summary>A string (<c>"absolute"</c>) or an array (<c>["relative", "${workspaceFolder}"]</c>).</summary>
    public JsonElement? FileLocation { get; set; }

    /// <summary>A single pattern object, an ARRAY of them (multi-line matchers), or a string naming a shared pattern.</summary>
    public JsonElement? Pattern { get; set; }
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

    /// <summary>
    /// The VS Code themes this extension contributes, each with the manifest's own label and
    /// uiTheme. The extension service parses themes but cannot register them: the theme
    /// registry (ThemeManager) lives in the Shell, which references ProjectSystem and not the
    /// other way round. Carrying the contributions on this event lets the Shell register them
    /// without inverting the dependency.
    /// </summary>
    public List<ExtensionThemeContribution> ThemeContributions { get; } = new();

    public ExtensionContributionsLoadedEventArgs(Extension extension)
    {
        Extension = extension;
    }
}

/// <summary>
/// Event args for extension tree view operations.
/// </summary>
public class ExtensionTreeViewEventArgs : EventArgs
{
    /// <summary>The extension that created or refreshed the tree view.</summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>The tree view identifier.</summary>
    public string ViewId { get; set; } = "";

    /// <summary>The display title for the tree view.</summary>
    public string? Title { get; set; }

    /// <summary>The element to refresh (null for entire tree).</summary>
    public string? Element { get; set; }
}

/// <summary>
/// Event args for extension diagnostics published for a document.
/// </summary>
public class ExtensionDiagnosticsEventArgs : EventArgs
{
    public string Uri { get; set; } = "";
    public JsonElement Diagnostics { get; set; }
    public string CollectionName { get; set; } = "";
}

/// <summary>
/// Event args for webview panel creation by an extension.
/// </summary>
public class WebViewCreatedEventArgs : EventArgs
{
    /// <summary>The extension that created the webview.</summary>
    public string ExtensionId { get; set; } = "";

    /// <summary>The unique panel identifier.</summary>
    public string PanelId { get; set; } = "";

    /// <summary>The view type identifier (e.g., "myExtension.preview").</summary>
    public string ViewType { get; set; } = "";

    /// <summary>The display title for the webview panel tab.</summary>
    public string? Title { get; set; }
}

/// <summary>
/// Event args for webview HTML content updates.
/// </summary>
public class WebViewHtmlChangedEventArgs : EventArgs
{
    /// <summary>The panel identifier to update.</summary>
    public string PanelId { get; set; } = "";

    /// <summary>The new HTML content.</summary>
    public string Html { get; set; } = "";
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
