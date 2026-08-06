using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Manages IDE extensions with VS Code extension host support.
/// Handles discovery, installation, lifecycle, and static contribution loading
/// (themes, grammars, snippets, language configs) without requiring Node.js.
/// </summary>
public class ExtensionService : IExtensionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// The exact options this service binds a VS Code <c>package.json</c> with. PUBLIC
    /// deliberately — this assembly grants no <c>InternalsVisibleTo</c> for VisualGameStudio.Tests
    /// (precedent: <see cref="IntelliSenseEmissionService"/>, <c>DapSession</c>), and a
    /// manifest-binding test MUST bind under these options rather than a hand-rolled copy: a copy
    /// drifts from production and then proves nothing about what the IDE actually accepts.
    ///
    /// <para><c>PropertyNameCaseInsensitive</c>, <c>ReadCommentHandling</c> and
    /// <c>AllowTrailingCommas</c> are all load-bearing here — real VS Code manifests rely on
    /// each — so a test that omits any of them tests a stricter parser than the one that ships.</para>
    /// </summary>
    public static JsonSerializerOptions ManifestJsonOptions => JsonOptions;

    private readonly List<Extension> _extensions = new();
    private readonly Dictionary<string, bool> _enabledState = new();
    private readonly Dictionary<string, List<string>> _extensionCommands = new();

    // Activation event tracking: event string -> list of extension IDs
    private readonly Dictionary<string, List<string>> _activationEventIndex = new();
    // Track which languages have already triggered activation
    private readonly HashSet<string> _activatedLanguages = new();

    // Contributed commands from package.json (commandId -> ContributedCommand)
    private readonly Dictionary<string, ContributedCommand> _contributedCommands = new();
    // Contributed keybindings from package.json
    private readonly List<ContributedKeybinding> _contributedKeybindings = new();
    // Contributed menu items from package.json (menuId -> list of items)
    private readonly Dictionary<string, List<ContributedMenuItem>> _contributedMenuItems = new();
    // Track languages that have extension providers registered
    private readonly ConcurrentDictionary<string, byte> _extensionProviderLanguages = new();

    private readonly string _extensionsDir;
    private readonly string _stateFile;
    private readonly HttpClient _httpClient;
    private readonly IOutputService _outputService;
    private readonly ITextMateService? _textMateService;
    private readonly ISnippetService? _snippetService;
    private readonly ICommandService? _commandService;
    private readonly IKeybindingService? _keybindingService;
    private TextMateRegistrar? _textMateRegistrar;
    private ExtensionHost? _extensionHost;
    private bool _disposed;

    /// <param name="extensionsRoot">
    /// Overrides the <c>~/.vgs</c> root that extensions and their enabled-state file live under.
    /// Exists so behaviour at this layer can be tested at all: the path was previously derived from
    /// the user profile unconditionally, so any test that exercised discovery read — and any test
    /// that exercised install would have WRITTEN — inside the developer's own
    /// <c>~/.vgs/extensions</c>. Production passes nothing and is unaffected.
    /// </param>
    public ExtensionService(
        IOutputService outputService,
        ITextMateService? textMateService = null,
        ISnippetService? snippetService = null,
        ICommandService? commandService = null,
        IKeybindingService? keybindingService = null,
        string? extensionsRoot = null)
    {
        _outputService = outputService;
        _textMateService = textMateService;
        _snippetService = snippetService;
        _commandService = commandService;
        _keybindingService = keybindingService;

        if (_textMateService != null)
        {
            _textMateRegistrar = new TextMateRegistrar(_textMateService);
        }

        var root = extensionsRoot
                   ?? Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vgs");
        _extensionsDir = Path.Combine(root, "extensions");
        _stateFile = Path.Combine(root, "extensions-state.json");
        _httpClient = new HttpClient();

        Directory.CreateDirectory(_extensionsDir);
        LoadState();
    }

    public IReadOnlyList<Extension> InstalledExtensions => _extensions.ToList();

    public IReadOnlyList<Extension> EnabledExtensions => _extensions.Where(e => e.IsEnabled).ToList();

    public string ExtensionsDirectory => _extensionsDir;

    public bool IsExtensionHostRunning => _extensionHost?.IsRunning ?? false;

    public event EventHandler<ExtensionEventArgs>? ExtensionInstalled;
    public event EventHandler<ExtensionEventArgs>? ExtensionUninstalled;
    public event EventHandler<ExtensionEventArgs>? ExtensionEnabled;
    public event EventHandler<ExtensionEventArgs>? ExtensionDisabled;
    public event EventHandler<ExtensionEventArgs>? ExtensionActivated;
    public event EventHandler<ExtensionEventArgs>? ExtensionDeactivated;
    public event EventHandler<bool>? ExtensionHostStateChanged;
    public event EventHandler<ExtensionMessageEventArgs>? ExtensionMessageReceived;
    public event EventHandler<ExtensionContributionsLoadedEventArgs>? ContributionsLoaded;
    public event EventHandler<ExtensionDiagnosticsEventArgs>? ExtensionDiagnosticsReceived;
    public event EventHandler<ExtensionTreeViewEventArgs>? TreeViewCreated;
    public event EventHandler<ExtensionTreeViewEventArgs>? TreeViewRefreshRequested;
    public event EventHandler<WebViewCreatedEventArgs>? WebViewCreated;
    public event EventHandler<WebViewHtmlChangedEventArgs>? WebViewHtmlChanged;

    #region Discovery

    public async Task<IReadOnlyList<Extension>> DiscoverExtensionsAsync()
    {
        // Discovery is RE-ENTRANT: it runs at startup and again after every install. Everything
        // rebuilt from the manifests below must be cleared alongside _extensions, or each pass
        // stacks another copy on the last — the keybinding and menu registrations append
        // unconditionally, and entries belonging to an extension that has since been uninstalled
        // would otherwise never be forgotten.
        //
        // Only state REBUILT from the manifests below may be cleared here. Two fields look like
        // they belong in this list and do not:
        //
        //   _extensionCommands is fed by OnCommandRegistered — commands an extension registers at
        //   RUNTIME through the host, never rebuilt by discovery. Clearing it would erase a running
        //   extension's commands every time another extension is installed. Its lifecycle is the
        //   uninstall path, which removes by id.
        //
        //   _activatedLanguages is bounded by the number of open languages rather than by passes,
        //   so it is not a leak, and clearing it would re-fire onLanguage activation for languages
        //   already open — a behaviour change, not a fix.
        _extensions.Clear();
        _activationEventIndex.Clear();
        _contributedCommands.Clear();
        _contributedKeybindings.Clear();
        _contributedMenuItems.Clear();

        if (!Directory.Exists(_extensionsDir))
        {
            return _extensions;
        }

        foreach (var dir in Directory.GetDirectories(_extensionsDir))
        {
            // Check for package.json at root level or in extension/ subdirectory
            var manifestPath = Path.Combine(dir, "package.json");
            var extensionSubDir = Path.Combine(dir, "extension", "package.json");

            string? actualDir = null;
            if (File.Exists(manifestPath))
            {
                actualDir = dir;
            }
            else if (File.Exists(extensionSubDir))
            {
                actualDir = Path.Combine(dir, "extension");
            }

            if (actualDir != null)
            {
                try
                {
                    var extension = await LoadExtensionFromDirectoryAsync(actualDir);
                    if (extension != null)
                    {
                        extension.InstallPath = actualDir;

                        if (_enabledState.TryGetValue(extension.Id, out var enabled))
                        {
                            extension.IsEnabled = enabled;
                            extension.Status = enabled ? ExtensionStatus.Installed : ExtensionStatus.Disabled;
                        }
                        // Two directories can carry the same publisher.name — an interrupted
                        // version upgrade leaves both, and the uninstall path's cleanup is a
                        // silent catch, so a stale copy surviving is expected rather than rare.
                        // Without this guard the extension is listed twice and every contribution
                        // it makes is registered twice.
                        if (_extensions.Any(e => e.Id == extension.Id))
                        {
                            _outputService.WriteLine(
                                $"[Extensions] Ignoring duplicate of {extension.Id} at {actualDir}.",
                                OutputCategory.General);
                            continue;
                        }

                        _extensions.Add(extension);
                    }
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"[Extensions] Failed to load extension from {dir}: {ex.Message}", OutputCategory.General);
                }
            }
        }

        _outputService.WriteLine($"[Extensions] Discovered {_extensions.Count} extension(s).", OutputCategory.General);
        return _extensions;
    }

    #endregion

    #region Extension Host Lifecycle

    public async Task StartExtensionHostAsync(CancellationToken cancellationToken = default)
    {
        if (_extensionHost?.IsRunning == true) return;

        // Find the extension host script
        var scriptPath = FindExtensionHostScript();
        if (scriptPath == null)
        {
            _outputService.WriteError("[Extensions] Extension host script (ExtensionHost/main.js) not found.", OutputCategory.General);
            return;
        }

        _extensionHost = new ExtensionHost(_outputService, scriptPath);
        _extensionHost.StateChanged += (s, running) => ExtensionHostStateChanged?.Invoke(this, running);
        _extensionHost.MessageReceived += (s, args) => ExtensionMessageReceived?.Invoke(this, args);
        _extensionHost.CommandRegistered += OnCommandRegistered;
        _extensionHost.HostCrashed += OnHostCrashed;
        _extensionHost.DiagnosticsReceived += (s, e) => ExtensionDiagnosticsReceived?.Invoke(this, e);
        _extensionHost.TreeViewCreated += (s, e) => TreeViewCreated?.Invoke(this, new ExtensionTreeViewEventArgs
        {
            ExtensionId = e.ExtensionId,
            ViewId = e.ViewId,
            Title = e.Title
        });
        _extensionHost.TreeViewRefreshRequested += (s, e) => TreeViewRefreshRequested?.Invoke(this, new ExtensionTreeViewEventArgs
        {
            ViewId = e.ViewId,
            Element = e.Element
        });
        _extensionHost.WebViewCreated += (s, e) => WebViewCreated?.Invoke(this, new WebViewCreatedEventArgs
        {
            ExtensionId = e.ExtensionId,
            PanelId = e.PanelId,
            ViewType = e.ViewType,
            Title = e.Title
        });
        _extensionHost.WebViewHtmlChanged += (s, e) => WebViewHtmlChanged?.Invoke(this, new WebViewHtmlChangedEventArgs
        {
            PanelId = e.PanelId,
            Html = e.Html
        });
        _extensionHost.ProviderRegistered += (s, e) =>
        {
            // Which languages this provider claims decides whether the IDE will ever ASK for it:
            // the hover and completion paths are gated on HasExtensionProviders(languageId).
            foreach (var language in ExtractSelectorLanguages(e.SelectorJson))
            {
                _extensionProviderLanguages.TryAdd(language, 0);
            }
        };

        await _extensionHost.StartAsync(cancellationToken);

        if (_extensionHost.IsRunning)
        {
            // Auto-activate extensions with '*' activation event
            await ActivateStartupExtensionsAsync(cancellationToken);
        }
    }

    public async Task StopExtensionHostAsync()
    {
        if (_extensionHost == null) return;

        // Deactivate all active extensions
        foreach (var ext in _extensions.Where(e => e.IsActive).ToList())
        {
            ext.IsActive = false;
            ext.Status = ExtensionStatus.Installed;
        }

        await _extensionHost.StopAsync();
        _extensionHost.Dispose();
        _extensionHost = null;
    }

    public async Task RestartExtensionHostAsync(CancellationToken cancellationToken = default)
    {
        _outputService.WriteLine("[Extensions] Restarting extension host...", OutputCategory.General);
        await StopExtensionHostAsync();
        await StartExtensionHostAsync(cancellationToken);
    }

    #endregion

    #region Installation

    public async Task<ExtensionInstallResult> InstallFromFileAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(packagePath))
            {
                return new ExtensionInstallResult { Success = false, Error = "Package file not found" };
            }

            var extension = Path.GetExtension(packagePath).ToLowerInvariant();
            if (extension != ".vsix" && extension != ".zip")
            {
                return new ExtensionInstallResult { Success = false, Error = "Invalid package format. Use .vsix or .zip" };
            }

            // Create temp directory for extraction
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Extract package
                ZipFile.ExtractToDirectory(packagePath, tempDir);

                // Find package.json (might be in extension/ subdirectory for vsix)
                var manifestPath = Path.Combine(tempDir, "package.json");
                if (!File.Exists(manifestPath))
                {
                    manifestPath = Path.Combine(tempDir, "extension", "package.json");
                }

                if (!File.Exists(manifestPath))
                {
                    return new ExtensionInstallResult { Success = false, Error = "No package.json found in package" };
                }

                // Load manifest
                var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken);
                var manifest = JsonSerializer.Deserialize<ExtensionManifest>(manifestJson, JsonOptions);

                if (manifest == null || string.IsNullOrEmpty(manifest.Name))
                {
                    return new ExtensionInstallResult { Success = false, Error = "Invalid package.json" };
                }

                var extensionId = $"{manifest.Publisher}.{manifest.Name}";
                var installDirName = $"{extensionId}-{manifest.Version}";
                var installDir = Path.Combine(_extensionsDir, installDirName);

                // Remove existing installation (any version)
                foreach (var existingDir in Directory.GetDirectories(_extensionsDir))
                {
                    var dirName = Path.GetFileName(existingDir);
                    if (dirName.StartsWith(extensionId + "-", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Equals(extensionId, StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(existingDir, true); } catch { }
                    }
                }

                // Copy files to install directory
                var sourceDir = File.Exists(Path.Combine(tempDir, "package.json"))
                    ? tempDir
                    : Path.Combine(tempDir, "extension");

                CopyDirectory(sourceDir, installDir);

                // Load the installed extension
                var ext = await LoadExtensionFromDirectoryAsync(installDir);
                if (ext != null)
                {
                    _extensions.RemoveAll(e => e.Id == ext.Id);
                    _extensions.Add(ext);
                    ExtensionInstalled?.Invoke(this, new ExtensionEventArgs(ext));
                    SaveState();

                    _outputService.WriteLine($"[Extensions] Installed: {ext.Name} ({ext.Id} v{ext.Version})", OutputCategory.General);

                    // Activate static contributions immediately
                    await LoadContributionsAsync(ext);

                    // Mark pure static extensions as active
                    if (string.IsNullOrEmpty(ext.Manifest?.Main))
                    {
                        ext.IsActive = true;
                        ext.Status = ExtensionStatus.Active;
                    }

                    // If the host is running and extension has '*' activation, activate JS too
                    if (_extensionHost?.IsRunning == true && ext.ActivationEvents.Contains("*"))
                    {
                        await ActivateAsync(ext.Id);
                    }

                    return new ExtensionInstallResult
                    {
                        Success = true,
                        Extension = ext,
                        RequiresRestart = ext.Manifest?.Main != null && _extensionHost?.IsRunning != true
                    };
                }

                return new ExtensionInstallResult { Success = false, Error = "Failed to load installed extension" };
            }
            finally
            {
                // Cleanup temp directory
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            return new ExtensionInstallResult { Success = false, Error = ex.Message };
        }
    }

    public async Task<ExtensionInstallResult> InstallFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vsix");

            try
            {
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                using var fs = File.Create(tempFile);
                await response.Content.CopyToAsync(fs, cancellationToken);
            }
            catch (Exception ex)
            {
                return new ExtensionInstallResult { Success = false, Error = $"Download failed: {ex.Message}" };
            }

            var result = await InstallFromFileAsync(tempFile, cancellationToken);

            try { File.Delete(tempFile); } catch { }

            return result;
        }
        catch (Exception ex)
        {
            return new ExtensionInstallResult { Success = false, Error = ex.Message };
        }
    }

    public Task<bool> UninstallAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null)
        {
            return Task.FromResult(false);
        }

        if (extension.IsBuiltIn)
        {
            return Task.FromResult(false);
        }

        try
        {
            // Deactivate first if active
            if (extension.IsActive)
            {
                UnloadContributions(extension);
                if (_extensionHost?.IsRunning == true)
                {
                    _ = _extensionHost.DeactivateExtensionAsync(extensionId);
                }
            }

            if (Directory.Exists(extension.InstallPath))
            {
                Directory.Delete(extension.InstallPath, true);
            }

            _extensions.Remove(extension);
            _enabledState.Remove(extensionId);
            _extensionCommands.Remove(extensionId);
            SaveState();

            _outputService.WriteLine($"[Extensions] Uninstalled: {extension.Name} ({extensionId})", OutputCategory.General);
            ExtensionUninstalled?.Invoke(this, new ExtensionEventArgs(extension));
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[Extensions] Uninstall failed: {ex.Message}", OutputCategory.General);
            return Task.FromResult(false);
        }
    }

    #endregion

    #region Enable / Disable

    public async Task<bool> EnableAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null)
        {
            return false;
        }

        extension.IsEnabled = true;
        extension.Status = ExtensionStatus.Installed;
        _enabledState[extensionId] = true;
        SaveState();

        // Re-activate static contributions
        await LoadContributionsAsync(extension);

        ExtensionEnabled?.Invoke(this, new ExtensionEventArgs(extension));
        return true;
    }

    public Task<bool> DisableAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null)
        {
            return Task.FromResult(false);
        }

        // Deactivate if currently active
        if (extension.IsActive)
        {
            UnloadContributions(extension);
            if (_extensionHost?.IsRunning == true)
            {
                _ = _extensionHost.DeactivateExtensionAsync(extensionId);
            }
        }

        extension.IsEnabled = false;
        extension.IsActive = false;
        extension.Status = ExtensionStatus.Disabled;
        _enabledState[extensionId] = false;
        SaveState();

        ExtensionDisabled?.Invoke(this, new ExtensionEventArgs(extension));
        return Task.FromResult(true);
    }

    #endregion

    #region Activation / Deactivation

    public Extension? GetExtension(string extensionId)
    {
        return _extensions.FirstOrDefault(e => e.Id == extensionId);
    }

    public ExtensionManifest? GetExtensionManifest(string extensionId)
    {
        return _extensions.FirstOrDefault(e => e.Id == extensionId)?.Manifest;
    }

    public async Task<bool> ActivateAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null || !extension.IsEnabled)
        {
            return false;
        }

        if (extension.IsActive)
        {
            return true;
        }

        extension.Status = ExtensionStatus.Activating;

        // Load declarative contributions (grammars, themes, snippets, etc.) regardless of host
        await LoadContributionsAsync(extension);

        // If the extension has a JS entry point, activate it in the extension host
        if (!string.IsNullOrEmpty(extension.Manifest?.Main))
        {
            // Start the host on demand. This is the ONLY place that does: StartExtensionHostAsync's
            // other callers are RestartExtensionHostAsync and the crash handler, and the crash
            // handler can only fire if the host was already up. Without this the check below is a
            // guard whose precondition nothing establishes — every extension with a `main` fell to
            // "static only" forever, which is exactly what 23a631c's fix ran into one layer down.
            if (_extensionHost?.IsRunning != true)
            {
                try
                {
                    await StartExtensionHostAsync();
                }
                catch (Exception ex)
                {
                    // Never fatal: the extension's declarative contributions are already loaded and
                    // stay useful even when Node is missing or the host script cannot be found.
                    _outputService.WriteError(
                        $"[Extensions] Could not start the extension host for {extensionId}: {ex.Message}",
                        OutputCategory.General);
                }
            }

            if (_extensionHost?.IsRunning != true)
            {
                // Static contributions are already loaded, just mark as active for static-only
                extension.IsActive = true;
                extension.Status = ExtensionStatus.Active;
                ExtensionActivated?.Invoke(this, new ExtensionEventArgs(extension));
                _outputService.WriteLine($"[Extensions] Activated (static only): {extension.Name} ({extensionId})", OutputCategory.General);
                return true;
            }

            var success = await _extensionHost.ActivateExtensionAsync(
                extensionId,
                extension.InstallPath,
                extension.Manifest.Main);

            if (!success)
            {
                // Still keep static contributions loaded
                extension.IsActive = true;
                extension.Status = ExtensionStatus.Active;
                _outputService.WriteError($"[Extensions] JS activation failed for {extensionId}, static contributions still active.", OutputCategory.General);
            }
        }

        extension.IsActive = true;
        extension.Status = ExtensionStatus.Active;
        ExtensionActivated?.Invoke(this, new ExtensionEventArgs(extension));
        _outputService.WriteLine($"[Extensions] Activated: {extension.Name} ({extensionId})", OutputCategory.General);
        return true;
    }

    public async Task<bool> DeactivateAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null || !extension.IsActive)
        {
            return false;
        }

        extension.Status = ExtensionStatus.Deactivating;

        // Deactivate in extension host if it has a JS entry
        if (!string.IsNullOrEmpty(extension.Manifest?.Main) && _extensionHost?.IsRunning == true)
        {
            await _extensionHost.DeactivateExtensionAsync(extensionId);
        }

        // Unload declarative contributions
        UnloadContributions(extension);

        extension.IsActive = false;
        extension.Status = ExtensionStatus.Installed;
        ExtensionDeactivated?.Invoke(this, new ExtensionEventArgs(extension));
        _outputService.WriteLine($"[Extensions] Deactivated: {extension.Name} ({extensionId})", OutputCategory.General);
        return true;
    }

    public async Task<object?> ExecuteExtensionCommandAsync(string commandId, object?[]? args = null)
    {
        if (_extensionHost?.IsRunning != true)
        {
            throw new InvalidOperationException("Extension host is not running.");
        }

        return await _extensionHost.ExecuteCommandAsync(commandId, args);
    }

    #region Extension Provider Methods

    public bool HasExtensionProviders(string languageId) => _extensionProviderLanguages.ContainsKey(languageId);

    /// <summary>
    /// Converts an extension's diagnostics payload into the IDE's model.
    /// </summary>
    /// <remarks>
    /// ⛔ THE SEVERITY SCALES DIFFER BY ONE, and using the wrong one fails silently.
    /// LSP is Error=1/Warning=2/Information=3/Hint=4; VS Code — which is what an extension sends —
    /// is Error=0/Warning=1/Information=2/Hint=3. Reusing the LSP converter would render every
    /// extension ERROR as a warning and every warning as information: no crash, no log, just
    /// quietly wrong severities. That is why this is a separate function rather than a shared one.
    ///
    /// <para>Positions are 0-based on the wire and 1-based in the IDE, matching the LSP path.</para>
    ///
    /// <para>Every shape here degrades rather than throws: this runs on a notification, so an
    /// exception has nowhere to surface and would simply lose the batch.</para>
    /// </remarks>
    public static IEnumerable<DiagnosticItem> ConvertExtensionDiagnostics(JsonElement diagnostics, string filePath)
    {
        if (diagnostics.ValueKind != JsonValueKind.Array) yield break;

        foreach (var entry in diagnostics.EnumerateArray())
        {
            // One malformed entry must not discard the well-formed ones beside it.
            if (entry.ValueKind != JsonValueKind.Object) continue;

            var item = new DiagnosticItem
            {
                FilePath = filePath,
                Message = entry.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                    ? message.GetString() ?? ""
                    : "",
                Id = entry.TryGetProperty("code", out var code)
                    ? (code.ValueKind == JsonValueKind.String ? code.GetString() ?? "" : code.ToString())
                    : ""
            };

            // VS Code's scale, deliberately not the LSP one. A missing severity is Error, matching
            // VS Code's own default.
            item.Severity = entry.TryGetProperty("severity", out var severity)
                            && severity.ValueKind == JsonValueKind.Number
                            && severity.TryGetInt32(out var severityValue)
                ? severityValue switch
                {
                    0 => DiagnosticSeverity.Error,
                    1 => DiagnosticSeverity.Warning,
                    2 => DiagnosticSeverity.Info,
                    // VS Code's Hint is a faint suggestion. Mapped to Info rather than Hidden so it
                    // stays visible — Hidden is the IDE's "do not surface this" level, and silently
                    // dropping an extension's output is the failure mode this whole area suffers from.
                    3 => DiagnosticSeverity.Info,
                    _ => DiagnosticSeverity.Error
                }
                : DiagnosticSeverity.Error;

            if (entry.TryGetProperty("range", out var range)
                && range.ValueKind == JsonValueKind.Object
                && range.TryGetProperty("start", out var start)
                && start.ValueKind == JsonValueKind.Object)
            {
                item.Line = start.TryGetProperty("line", out var line) && line.TryGetInt32(out var lineValue)
                    ? lineValue + 1
                    : 0;
                item.Column = start.TryGetProperty("character", out var character)
                              && character.TryGetInt32(out var characterValue)
                    ? characterValue + 1
                    : 0;
            }

            yield return item;
        }
    }

    /// <summary>
    /// Turns the document identity an extension reports back into a local path the IDE recognises.
    /// </summary>
    /// <remarks>
    /// The host percent-encodes the drive colon — <c>file:///C%3A/…</c> — while the IDE identifies
    /// documents by plain Windows path. Observed directly: a diagnostic published for
    /// <c>file:///C%3A/Users/…/hover-test.js</c> could never be matched to the open document, so it
    /// never appeared in the Problems panel. This is the inverse of
    /// <see cref="ExtensionHost.ToDocumentUri"/>: canonicalising inside one process is not the same
    /// as agreeing across two.
    /// </remarks>
    public static string ToLocalDocumentPath(string uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return uriOrPath;

        if (!uriOrPath.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return uriOrPath;

        try
        {
            return new Uri(uriOrPath).LocalPath;
        }
        catch (UriFormatException)
        {
            return uriOrPath;
        }
    }

    /// <summary>
    /// Reads the language ids out of a VS Code document selector.
    /// </summary>
    /// <remarks>
    /// ⛔ ORDER IS LOAD-BEARING: <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>
    /// does NOT return false for a non-object — it THROWS
    /// <see cref="System.InvalidOperationException"/>. The original inline version called it on the
    /// root before checking for an array, so an array selector threw on the first line, landed in a
    /// bare <c>catch { }</c>, and the array-handling branch directly beneath it was unreachable.
    ///
    /// <para>Arrays are the ordinary case, not an edge case: <c>vscode-api/languages.js</c> puts
    /// every selector through <c>_normaliseSelector</c>, which wraps objects and bare strings alike
    /// into an array. So essentially every real selector was discarded, <c>HasExtensionProviders</c>
    /// stayed false forever, and the IDE never asked any extension for anything — while the log
    /// cheerfully reported the provider as registered.</para>
    ///
    /// <para>Public and static so this can be tested directly; it is pure, while the event handler
    /// that consumes it is reachable only through a running extension host.</para>
    /// </remarks>
    public static IEnumerable<string> ExtractSelectorLanguages(string? selectorJson)
    {
        if (string.IsNullOrWhiteSpace(selectorJson)) yield break;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(selectorJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in root.EnumerateArray())
                {
                    var language = LanguageOf(item);
                    if (language != null) yield return language;
                }
            }
            else
            {
                var language = LanguageOf(root);
                if (language != null) yield return language;
            }
        }

        // A selector entry is either a filter object with an optional `language`, or a bare
        // language string. Anything else — a scheme-only filter, a number — contributes nothing
        // and must not disturb its neighbours.
        static string? LanguageOf(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var bare = element.GetString();
                return string.IsNullOrEmpty(bare) ? null : bare;
            }

            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("language", out var languageProperty)
                && languageProperty.ValueKind == JsonValueKind.String)
            {
                var language = languageProperty.GetString();
                return string.IsNullOrEmpty(language) ? null : language;
            }

            return null;
        }
    }

    public async Task<JsonElement?> RequestCompletionAsync(string uri, int line, int character, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestCompletionAsync(uri, line, character, ct);
    }

    public async Task<JsonElement?> RequestHoverAsync(string uri, int line, int character, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestHoverAsync(uri, line, character, ct);
    }

    public async Task<JsonElement?> RequestDefinitionAsync(string uri, int line, int character, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestDefinitionAsync(uri, line, character, ct);
    }

    public async Task<JsonElement?> RequestReferencesAsync(string uri, int line, int character, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestReferencesAsync(uri, line, character, ct);
    }

    public async Task<JsonElement?> RequestFormattingAsync(string uri, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestFormattingAsync(uri, ct);
    }

    public async Task<JsonElement?> RequestDocumentSymbolsAsync(string uri, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestDocumentSymbolsAsync(uri, ct);
    }

    public async Task<JsonElement?> RequestTreeChildrenAsync(string viewId, string? element, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestTreeChildrenAsync(viewId, element, ct);
    }

    public async Task<JsonElement?> RequestTreeItemAsync(string viewId, string element, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return null;
        return await _extensionHost.RequestTreeItemAsync(viewId, element, ct);
    }

    public async Task NotifyDocumentOpenedAsync(string uri, string languageId, int version, string text, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return;
        await _extensionHost.NotifyDocumentOpenedAsync(uri, languageId, version, text, ct);
    }

    public async Task NotifyDocumentChangedAsync(string uri, int version, string text, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return;
        await _extensionHost.NotifyDocumentChangedAsync(uri, version, text, ct);
    }

    public async Task NotifyDocumentClosedAsync(string uri, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return;
        await _extensionHost.NotifyDocumentClosedAsync(uri, ct);
    }

    public async Task NotifyDocumentSavedAsync(string uri, string? text = null, CancellationToken ct = default)
    {
        if (_extensionHost == null || !_extensionHost.IsRunning) return;
        await _extensionHost.NotifyDocumentSavedAsync(uri, text, ct);
    }

    #endregion

    public async Task TriggerActivationEventAsync(string activationEvent)
    {
        // Find extensions that should be activated by this event
        foreach (var ext in _extensions.Where(e => e.IsEnabled && !e.IsActive))
        {
            if (ext.ActivationEvents.Contains(activationEvent) ||
                ext.ActivationEvents.Contains("*"))
            {
                await ActivateAsync(ext.Id);
            }
        }

        // Also forward to the extension host for extensions that are already active
        // but may need to respond to the event
        if (_extensionHost?.IsRunning == true)
        {
            await _extensionHost.FireActivationEventAsync(activationEvent);
        }
    }

    public async Task ActivateStaticContributionsAsync(CancellationToken cancellationToken = default)
    {
        _outputService.WriteLine("[Extensions] Loading static contributions from installed extensions...", OutputCategory.General);

        // Discover all installed extensions
        await DiscoverExtensionsAsync();

        // Activate each enabled extension's static contributions
        var activatedCount = 0;
        foreach (var ext in _extensions.Where(e => e.IsEnabled).ToList())
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                // Load static contributions (themes, grammars, snippets)
                await LoadContributionsAsync(ext);

                // Mark as active if it has no JS entry point (pure static extension)
                if (string.IsNullOrEmpty(ext.Manifest?.Main))
                {
                    ext.IsActive = true;
                    ext.Status = ExtensionStatus.Active;
                }

                activatedCount++;
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"[Extensions] Failed to activate {ext.Id}: {ex.Message}", OutputCategory.General);
                ext.Status = ExtensionStatus.Error;
            }
        }

        _outputService.WriteLine($"[Extensions] Loaded static contributions from {activatedCount} extension(s).", OutputCategory.General);

        // Fire onStartupFinished LAST, once every extension's declarative contributions are in
        // place. It is the only activation event many real extensions declare — ESLint's manifest
        // lists it and nothing else — so until now those could never activate however well the rest
        // of the pipeline worked. Firing it here rather than earlier means an extension it wakes can
        // rely on other extensions' grammars and themes already being registered.
        //
        // Extensions with a JS entry point will start the host from ActivateAsync; that is a
        // deliberate behaviour change, since it means opening the IDE can now spawn Node.
        if (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await TriggerActivationEventAsync("onStartupFinished");
            }
            catch (Exception ex)
            {
                _outputService.WriteError(
                    $"[Extensions] onStartupFinished activation failed: {ex.Message}", OutputCategory.General);
            }
        }
    }

    #endregion

    #region Updates

    public Task<IReadOnlyList<ExtensionUpdate>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        // Would check marketplace for updates
        return Task.FromResult<IReadOnlyList<ExtensionUpdate>>(new List<ExtensionUpdate>());
    }

    public async Task<ExtensionInstallResult> UpdateAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        var updates = await CheckForUpdatesAsync(cancellationToken);
        var update = updates.FirstOrDefault(u => u.Extension.Id == extensionId);

        if (update == null)
        {
            return new ExtensionInstallResult { Success = false, Error = "No update available" };
        }

        // Would download and install update
        return new ExtensionInstallResult { Success = false, Error = "Update not implemented" };
    }

    #endregion

    #region Private Methods

    private async Task ActivateStartupExtensionsAsync(CancellationToken cancellationToken)
    {
        foreach (var ext in _extensions.Where(e => e.IsEnabled && !e.IsActive).ToList())
        {
            if (ext.ActivationEvents.Contains("*") ||
                ext.ActivationEvents.Count == 0) // No activation events = activate on startup
            {
                try
                {
                    await ActivateAsync(ext.Id);
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"[Extensions] Failed to auto-activate {ext.Id}: {ex.Message}", OutputCategory.General);
                }
            }
        }
    }

    private void OnCommandRegistered(object? sender, ExtensionCommandRegisteredArgs args)
    {
        if (!_extensionCommands.ContainsKey(args.ExtensionId))
        {
            _extensionCommands[args.ExtensionId] = new List<string>();
        }
        _extensionCommands[args.ExtensionId].Add(args.CommandId);
    }

    private int _crashRestartCount;
    private const int MaxCrashRestarts = 5;

    private async void OnHostCrashed(object? sender, EventArgs e)
    {
        // Mark all extensions as inactive
        foreach (var ext in _extensions.Where(e => e.IsActive))
        {
            ext.IsActive = false;
            ext.Status = ExtensionStatus.Installed;
        }

        if (_crashRestartCount >= MaxCrashRestarts)
        {
            _outputService.WriteError($"[Extensions] Extension host crashed {MaxCrashRestarts} times. Not restarting.", OutputCategory.General);
            return;
        }

        _crashRestartCount++;
        _outputService.WriteError($"[Extensions] Extension host crashed. Attempting restart in 5 seconds (attempt {_crashRestartCount}/{MaxCrashRestarts})...", OutputCategory.General);

        await Task.Delay(5000);

        try
        {
            await StartExtensionHostAsync();
            _crashRestartCount = 0; // Reset on successful start
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[Extensions] Failed to restart extension host: {ex.Message}", OutputCategory.General);
        }
    }

    private async Task<Extension?> LoadExtensionFromDirectoryAsync(string directory)
    {
        var manifestPath = Path.Combine(directory, "package.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(manifestPath);
        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(json, JsonOptions);

        if (manifest == null)
        {
            return null;
        }

        var extension = new Extension
        {
            Id = $"{manifest.Publisher}.{manifest.Name}",
            Name = manifest.DisplayName ?? manifest.Name,
            Description = manifest.Description,
            Version = manifest.Version,
            Publisher = manifest.Publisher,
            Categories = manifest.Categories,
            Keywords = manifest.Keywords,
            License = manifest.License,
            InstallPath = directory,
            Manifest = manifest,
            ActivationEvents = manifest.ActivationEvents,
            IsEnabled = true,
            Status = ExtensionStatus.Installed
        };

        if (!string.IsNullOrEmpty(manifest.Icon))
        {
            extension.IconPath = Path.Combine(directory, manifest.Icon);
        }

        if (manifest.Repository != null)
        {
            extension.Repository = manifest.Repository.Url;
        }

        // Load contributions from manifest
        if (manifest.Contributes != null)
        {
            extension.Contributions = manifest.Contributes;

            // Report any section the converter had to drop. Deliberately drained HERE rather than
            // in LoadContributionsAsync: that method's Output line sits inside `if (total > 0)`,
            // where total counts successfully loaded contributions. An extension whose only
            // section was the dropped one has total == 0 — exactly the case where silence is
            // worst — so a warning gated on a success counter cannot report a total failure.
            //
            // Severity follows what the section is actually worth. Commands and keybindings are
            // the only two read from this DTO, so losing them is real functional loss: an empty
            // command palette and a dead onCommand path. The rest are either re-parsed from raw
            // JSON further down or read by nothing at all, so reporting them as errors would bury
            // the two that matter.
            foreach (var error in manifest.Contributes.LoadErrors)
            {
                var message =
                    $"[Extensions] {extension.Id}: skipped contributes.{error.Section} — {error.Message}. "
                    + "Other contributions loaded normally.";

                if (error.Section is "commands" or "keybindings" or "contributes")
                {
                    _outputService.WriteError(message, OutputCategory.General);
                }
                else
                {
                    _outputService.WriteLine(message, OutputCategory.General);
                }
            }
        }

        // Index activation events for fast lookup
        IndexActivationEvents(extension);

        // Parse and register contributed commands, keybindings, and menus
        ParseContributedCommands(extension);
        ParseContributedKeybindings(extension);
        ParseContributedMenus(extension);

        return extension;
    }

    /// <summary>
    /// Loads an extension's declarative contributions. Async because the grammar registrar is
    /// async: this runs on the UI thread (the Extensions panel's Install command is a
    /// RelayCommand) and the registrar's awaits capture the SynchronizationContext, so blocking
    /// on it here deadlocked the IDE on the first successful install. Await, never block.
    /// </summary>
    private async Task LoadContributionsAsync(Extension extension)
    {
        if (string.IsNullOrEmpty(extension.InstallPath) || !Directory.Exists(extension.InstallPath))
            return;

        var stats = new ExtensionContributionsLoadedEventArgs(extension);

        try
        {
            // Load themes
            stats.ThemesLoaded = LoadThemesFromExtension(extension, stats.ThemeContributions);

            // Load grammars and language configurations via TextMateRegistrar
            stats.GrammarsLoaded = await LoadGrammarsFromExtensionAsync(extension);

            // Load snippets
            stats.SnippetsLoaded = LoadSnippetsFromExtension(extension);

            // Register contributed commands with the IDE command service
            var commandsRegistered = RegisterContributedCommandsWithIDE(extension);

            // Register contributed keybindings with the IDE keybinding service
            var keybindingsRegistered = RegisterContributedKeybindingsWithIDE(extension);

            var total = stats.ThemesLoaded + stats.GrammarsLoaded + stats.SnippetsLoaded
                        + commandsRegistered + keybindingsRegistered;
            if (total > 0)
            {
                _outputService.WriteLine(
                    $"[Extensions] Loaded contributions from {extension.Name}: " +
                    $"{stats.ThemesLoaded} theme(s), {stats.GrammarsLoaded} grammar(s), " +
                    $"{stats.SnippetsLoaded} snippet file(s), " +
                    $"{commandsRegistered} command(s), {keybindingsRegistered} keybinding(s)",
                    OutputCategory.General);

                ContributionsLoaded?.Invoke(this, stats);
            }
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[Extensions] Failed to load contributions from {extension.Name}: {ex.Message}", OutputCategory.General);
        }
    }

    private void UnloadContributions(Extension extension)
    {
        // Unregister grammars loaded from this extension
        _textMateRegistrar?.UnregisterExtension(extension.Id);

        // Unregister commands contributed by this extension
        if (_commandService != null)
        {
            foreach (var cmd in _contributedCommands.Values.Where(c => c.ExtensionId == extension.Id))
            {
                _commandService.UnregisterCommand(cmd.CommandId);
            }
        }

        // Remove keybindings contributed by this extension
        // (keybinding service tracks by ExtensionId so we remove matching ones)
        if (_keybindingService != null)
        {
            foreach (var kb in _contributedKeybindings.Where(k => k.ExtensionId == extension.Id))
            {
                try
                {
                    var chord = KeyChord.Parse(kb.Key);
                    _keybindingService.RemoveKeybinding(kb.CommandId, chord);
                }
                catch
                {
                    // Ignore parse errors during cleanup
                }
            }
        }
    }

    /// <summary>
    /// Registers contributed commands with the IDE command service so they can be executed.
    /// </summary>
    private int RegisterContributedCommandsWithIDE(Extension extension)
    {
        if (_commandService == null) return 0;
        if (extension.Contributions?.Commands == null) return 0;

        var count = 0;
        foreach (var cmd in extension.Contributions.Commands)
        {
            if (string.IsNullOrEmpty(cmd.Command)) continue;
            if (_commandService.IsCommandRegistered(cmd.Command)) continue;

            var commandId = cmd.Command;
            _commandService.RegisterCommand(commandId, async _ =>
            {
                await ExecuteContributedCommandAsync(commandId);
            });
            count++;
        }
        return count;
    }

    /// <summary>
    /// Registers contributed keybindings with the IDE keybinding service.
    /// </summary>
    private int RegisterContributedKeybindingsWithIDE(Extension extension)
    {
        if (_keybindingService == null) return 0;
        if (extension.Contributions?.Keybindings == null) return 0;

        var count = 0;
        foreach (var kb in extension.Contributions.Keybindings)
        {
            if (string.IsNullOrEmpty(kb.Command) || string.IsNullOrEmpty(kb.Key)) continue;

            try
            {
                var chord = KeyChord.Parse(kb.Key);
                var keybinding = new Keybinding
                {
                    CommandId = kb.Command,
                    Key = chord,
                    When = kb.When,
                    ExtensionId = extension.Id,
                    Source = KeybindingSource.Extension
                };
                _keybindingService.RegisterKeybinding(keybinding);
                count++;
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"[Extensions] Failed to register keybinding '{kb.Key}' for '{kb.Command}': {ex.Message}", OutputCategory.General);
            }
        }
        return count;
    }

    /// <summary>
    /// Parses an extension's contributed themes into TextMate themes and records each theme
    /// file's absolute path in <paramref name="themeFilePaths"/>. Parsing alone does NOT make a
    /// theme selectable: the theme registry lives in the Shell, so the paths are surfaced on
    /// ContributionsLoaded for the Shell to register. Without that the loaded theme object is
    /// discarded and the count reports work attempted rather than work completed.
    /// </summary>
    private int LoadThemesFromExtension(Extension extension, List<ExtensionThemeContribution>? themeContributions = null)
    {
        if (_textMateService == null) return 0;

        var packageJsonPath = Path.Combine(extension.InstallPath, "package.json");
        if (!File.Exists(packageJsonPath)) return 0;

        try
        {
            var json = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            if (!root.TryGetProperty("contributes", out var contributes)) return 0;
            if (!contributes.TryGetProperty("themes", out var themes)) return 0;

            var count = 0;
            foreach (var themeEntry in themes.EnumerateArray())
            {
                var themePath = themeEntry.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;
                var label = themeEntry.TryGetProperty("label", out var labelEl) ? labelEl.GetString() : null;
                var uiTheme = themeEntry.TryGetProperty("uiTheme", out var uiEl) ? uiEl.GetString() : "vs-dark";

                if (string.IsNullOrEmpty(themePath)) continue;

                var fullPath = Path.Combine(extension.InstallPath, themePath.TrimStart('/'));
                if (!File.Exists(fullPath)) continue;

                // Surface the contribution even if TextMate parsing below fails — the Shell's
                // theme loader (VsCodeThemeLoader) is a different, more capable parser and may
                // well handle a file this one chokes on. The manifest label travels with the
                // path: theme files' internal names are not unique (Dracula's two variants both
                // call themselves "Dracula"), so deriving identity from the file loses one.
                themeContributions?.Add(new ExtensionThemeContribution
                {
                    Path = fullPath,
                    Label = label,
                    UiTheme = uiTheme
                });

                try
                {
                    var themeJson = File.ReadAllText(fullPath);
                    var theme = _textMateService.LoadThemeFromJson(themeJson, label ?? Path.GetFileNameWithoutExtension(fullPath));
                    if (theme != null)
                    {
                        theme.Type = uiTheme switch
                        {
                            "vs" => "light",
                            "vs-dark" => "dark",
                            "hc-black" => "hc-dark",
                            "hc-light" => "hc-light",
                            _ => "dark"
                        };
                        count++;
                    }
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"[Extensions] Failed to load theme '{label}' from {extension.Name}: {ex.Message}", OutputCategory.General);
                }
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<int> LoadGrammarsFromExtensionAsync(Extension extension)
    {
        if (_textMateRegistrar == null) return 0;

        try
        {
            return await _textMateRegistrar.RegisterFromExtensionAsync(extension.InstallPath, extension.Id);
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[Extensions] Failed to load grammars from {extension.Name}: {ex.Message}", OutputCategory.General);
            return 0;
        }
    }

    private int LoadSnippetsFromExtension(Extension extension)
    {
        if (_snippetService == null) return 0;

        var packageJsonPath = Path.Combine(extension.InstallPath, "package.json");
        if (!File.Exists(packageJsonPath)) return 0;

        try
        {
            var json = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            if (!root.TryGetProperty("contributes", out var contributes)) return 0;
            if (!contributes.TryGetProperty("snippets", out var snippets)) return 0;

            var count = 0;
            foreach (var snippetEntry in snippets.EnumerateArray())
            {
                var language = snippetEntry.TryGetProperty("language", out var langEl) ? langEl.GetString() : null;
                var snippetPath = snippetEntry.TryGetProperty("path", out var pathEl) ? pathEl.GetString() : null;

                if (string.IsNullOrEmpty(snippetPath)) continue;

                var fullPath = Path.Combine(extension.InstallPath, snippetPath.TrimStart('/'));
                if (!File.Exists(fullPath)) continue;

                try
                {
                    var snippetJson = File.ReadAllText(fullPath);
                    var parsedSnippets = ParseVSCodeSnippets(snippetJson, language ?? "");
                    foreach (var snippet in parsedSnippets)
                    {
                        snippet.Source = $"extension:{extension.Id}";
                        _snippetService.AddUserSnippet(snippet.Scope, snippet);
                    }
                    if (parsedSnippets.Count > 0) count++;
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"[Extensions] Failed to load snippets from {extension.Name}: {ex.Message}", OutputCategory.General);
                }
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Parses VS Code format snippet JSON: { "name": { prefix, body, description } }
    /// </summary>
    private static List<Snippet> ParseVSCodeSnippets(string json, string language)
    {
        var result = new List<Snippet>();

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var name = prop.Name;
                var entry = prop.Value;

                var prefix = "";
                if (entry.TryGetProperty("prefix", out var prefixEl))
                {
                    prefix = prefixEl.ValueKind == JsonValueKind.Array
                        ? prefixEl.EnumerateArray().FirstOrDefault().GetString() ?? ""
                        : prefixEl.GetString() ?? "";
                }

                var body = Array.Empty<string>();
                if (entry.TryGetProperty("body", out var bodyEl))
                {
                    if (bodyEl.ValueKind == JsonValueKind.Array)
                    {
                        body = bodyEl.EnumerateArray()
                            .Select(e => e.GetString() ?? "")
                            .ToArray();
                    }
                    else if (bodyEl.ValueKind == JsonValueKind.String)
                    {
                        body = (bodyEl.GetString() ?? "").Split('\n');
                    }
                }

                var description = entry.TryGetProperty("description", out var descEl)
                    ? descEl.GetString() ?? ""
                    : "";

                var scope = language;
                if (entry.TryGetProperty("scope", out var scopeEl))
                {
                    scope = scopeEl.GetString() ?? language;
                }

                result.Add(new Snippet
                {
                    Name = name,
                    Prefix = prefix,
                    Body = body,
                    Description = description,
                    Scope = scope,
                    IsBuiltIn = false,
                    Category = "Extension"
                });
            }
        }
        catch
        {
            // Skip malformed snippet files
        }

        return result;
    }

    private string? FindExtensionHostScript()
    {
        // Check alongside the application executable
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "ExtensionHost", "main.js"),
            Path.Combine(baseDir, "Services", "ExtensionHost", "main.js"),
            // Development paths
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VisualGameStudio.ProjectSystem", "Services", "ExtensionHost", "main.js")),
            // Relative to extensions dir
            Path.Combine(Path.GetDirectoryName(_extensionsDir) ?? "", "ExtensionHost", "main.js"),
            // Legacy paths (fallback)
            Path.Combine(baseDir, "ExtensionHostMain.js"),
            Path.Combine(baseDir, "Services", "ExtensionHostMain.js"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void LoadState()
    {
        if (File.Exists(_stateFile))
        {
            try
            {
                var json = File.ReadAllText(_stateFile);
                var state = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, JsonOptions);
                if (state != null)
                {
                    foreach (var kvp in state)
                    {
                        _enabledState[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch
            {
                // Ignore state load errors
            }
        }
    }

    private void SaveState()
    {
        try
        {
            var dir = Path.GetDirectoryName(_stateFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Save both enabled/disabled state
            var state = new Dictionary<string, bool>();
            foreach (var ext in _extensions)
            {
                state[ext.Id] = ext.IsEnabled;
            }
            foreach (var kvp in _enabledState)
            {
                if (!state.ContainsKey(kvp.Key))
                {
                    state[kvp.Key] = kvp.Value;
                }
            }

            var json = JsonSerializer.Serialize(state, JsonOptions);
            File.WriteAllText(_stateFile, json);
        }
        catch
        {
            // Ignore state save errors
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    #endregion

    #region Contributed Commands, Keybindings, Menus

    public IReadOnlyList<ContributedCommand> GetContributedCommands()
    {
        return _contributedCommands.Values.ToList();
    }

    public IReadOnlyList<ContributedKeybinding> GetContributedKeybindings()
    {
        return _contributedKeybindings.ToList();
    }

    public IReadOnlyList<ContributedMenuItem> GetContributedMenuItems(string menuId)
    {
        if (_contributedMenuItems.TryGetValue(menuId, out var items))
        {
            return items.ToList();
        }
        return Array.Empty<ContributedMenuItem>();
    }

    public async Task ExecuteContributedCommandAsync(string commandId)
    {
        if (!_contributedCommands.TryGetValue(commandId, out var contributed))
        {
            _outputService.WriteError($"[Extensions] Unknown contributed command: {commandId}", OutputCategory.General);
            return;
        }

        // Ensure the owning extension is activated (onCommand activation event)
        var extension = _extensions.FirstOrDefault(e => e.Id == contributed.ExtensionId);
        if (extension != null && extension.IsEnabled && !extension.IsActive)
        {
            _outputService.WriteLine($"[Extensions] Activating {extension.Name} for command: {commandId}", OutputCategory.General);
            await ActivateAsync(extension.Id);
        }

        // If the extension has a runtime, send the command to the extension host
        if (contributed.HasRuntime)
        {
            if (_extensionHost?.IsRunning == true)
            {
                try
                {
                    await _extensionHost.ExecuteCommandAsync(commandId);
                }
                catch (Exception ex)
                {
                    _outputService.WriteError($"[Extensions] Command '{commandId}' failed: {ex.Message}", OutputCategory.General);
                }
            }
            else
            {
                _outputService.WriteError($"[Extensions] Cannot execute '{commandId}': extension host is not running.", OutputCategory.General);
            }
        }
        else
        {
            _outputService.WriteLine($"[Extensions] Command '{commandId}' is not available (extension has no runtime).", OutputCategory.General);
        }
    }

    public async Task NotifyLanguageOpenedAsync(string languageId)
    {
        if (string.IsNullOrEmpty(languageId)) return;

        var activationEvent = $"onLanguage:{languageId}";

        // Avoid redundant activations for the same language
        if (!_activatedLanguages.Add(languageId)) return;

        _outputService.WriteLine($"[Extensions] Language opened: {languageId}", OutputCategory.General);
        await TriggerActivationEventAsync(activationEvent);
    }

    public async Task NotifyWorkspaceOpenedAsync(string workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

        _outputService.WriteLine($"[Extensions] Checking workspaceContains activation events for: {workspacePath}", OutputCategory.General);

        // Find all extensions with workspaceContains events
        foreach (var ext in _extensions.Where(e => e.IsEnabled && !e.IsActive).ToList())
        {
            foreach (var evt in ext.ActivationEvents)
            {
                if (evt.StartsWith("workspaceContains:"))
                {
                    var glob = evt.Substring("workspaceContains:".Length);
                    if (WorkspaceContainsMatch(workspacePath, glob))
                    {
                        _outputService.WriteLine($"[Extensions] Workspace match for {ext.Name}: {glob}", OutputCategory.General);
                        await ActivateAsync(ext.Id);
                        break; // Only need one match per extension
                    }
                }
            }
        }
    }

    #endregion

    #region Activation Event Indexing

    private void IndexActivationEvents(Extension extension)
    {
        foreach (var evt in extension.ActivationEvents)
        {
            if (!_activationEventIndex.TryGetValue(evt, out var extList))
            {
                extList = new List<string>();
                _activationEventIndex[evt] = extList;
            }
            if (!extList.Contains(extension.Id))
            {
                extList.Add(extension.Id);
            }
        }
    }

    private void ParseContributedCommands(Extension extension)
    {
        if (extension.Contributions?.Commands == null) return;

        var hasRuntime = !string.IsNullOrEmpty(extension.Manifest?.Main);

        foreach (var cmd in extension.Contributions.Commands)
        {
            if (string.IsNullOrEmpty(cmd.Command)) continue;

            _contributedCommands[cmd.Command] = new ContributedCommand
            {
                CommandId = cmd.Command,
                Title = cmd.Title,
                Category = cmd.Category,
                ExtensionId = extension.Id,
                HasRuntime = hasRuntime
            };
        }
    }

    private void ParseContributedKeybindings(Extension extension)
    {
        if (extension.Contributions?.Keybindings == null) return;

        foreach (var kb in extension.Contributions.Keybindings)
        {
            if (string.IsNullOrEmpty(kb.Command) || string.IsNullOrEmpty(kb.Key)) continue;

            _contributedKeybindings.Add(new ContributedKeybinding
            {
                CommandId = kb.Command,
                Key = kb.Key,
                Mac = kb.Mac,
                Linux = kb.Linux,
                When = kb.When,
                ExtensionId = extension.Id
            });
        }
    }

    private void ParseContributedMenus(Extension extension)
    {
        if (string.IsNullOrEmpty(extension.InstallPath)) return;

        var packageJsonPath = Path.Combine(extension.InstallPath, "package.json");
        if (!File.Exists(packageJsonPath)) return;

        try
        {
            var json = File.ReadAllText(packageJsonPath);
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            var root = doc.RootElement;
            if (!root.TryGetProperty("contributes", out var contributes)) return;
            if (!contributes.TryGetProperty("menus", out var menus)) return;

            // menus is { "editor/context": [...], "explorer/context": [...], ... }
            foreach (var menuProp in menus.EnumerateObject())
            {
                var menuId = menuProp.Name;
                if (menuProp.Value.ValueKind != JsonValueKind.Array) continue;

                foreach (var entry in menuProp.Value.EnumerateArray())
                {
                    var commandId = entry.TryGetProperty("command", out var cmdEl) ? cmdEl.GetString() : null;
                    if (string.IsNullOrEmpty(commandId)) continue;

                    var group = entry.TryGetProperty("group", out var groupEl) ? groupEl.GetString() : null;
                    var when = entry.TryGetProperty("when", out var whenEl) ? whenEl.GetString() : null;

                    // Resolve display title from contributed commands
                    var title = commandId;
                    string? category = null;
                    if (_contributedCommands.TryGetValue(commandId, out var contributed))
                    {
                        title = contributed.Title;
                        category = contributed.Category;
                    }

                    if (!_contributedMenuItems.TryGetValue(menuId, out var menuList))
                    {
                        menuList = new List<ContributedMenuItem>();
                        _contributedMenuItems[menuId] = menuList;
                    }

                    menuList.Add(new ContributedMenuItem
                    {
                        MenuId = menuId,
                        CommandId = commandId,
                        Group = group,
                        When = when,
                        ExtensionId = extension.Id,
                        Title = title,
                        Category = category
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _outputService.WriteError($"[Extensions] Failed to parse menus from {extension.Name}: {ex.Message}", OutputCategory.General);
        }
    }

    /// <summary>
    /// Simple glob matching for workspaceContains patterns.
    /// Supports ** and * patterns.
    /// </summary>
    private static bool WorkspaceContainsMatch(string workspacePath, string glob)
    {
        try
        {
            // Convert glob to a search pattern for Directory.EnumerateFiles
            // Handle common patterns like "**/*.py", "*.json", ".eslintrc"
            var searchPattern = glob;

            // Strip leading **/ for recursive search
            var searchOption = SearchOption.TopDirectoryOnly;
            if (searchPattern.StartsWith("**/"))
            {
                searchPattern = searchPattern.Substring(3);
                searchOption = SearchOption.AllDirectories;
            }
            else if (searchPattern.Contains("**"))
            {
                searchOption = SearchOption.AllDirectories;
                searchPattern = searchPattern.Replace("**/", "");
            }

            // Use Directory.EnumerateFiles with the simplified pattern
            // Only check for existence (take 1)
            var files = Directory.EnumerateFiles(workspacePath, searchPattern, searchOption);
            return files.Any();
        }
        catch
        {
            return false;
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _extensionHost?.Dispose();
        _httpClient.Dispose();
    }
}
