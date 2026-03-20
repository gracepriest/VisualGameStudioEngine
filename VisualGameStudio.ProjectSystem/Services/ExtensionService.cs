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

    private readonly List<Extension> _extensions = new();
    private readonly Dictionary<string, bool> _enabledState = new();
    private readonly Dictionary<string, List<string>> _extensionCommands = new();
    private readonly string _extensionsDir;
    private readonly string _stateFile;
    private readonly HttpClient _httpClient;
    private readonly IOutputService _outputService;
    private readonly ITextMateService? _textMateService;
    private readonly ISnippetService? _snippetService;
    private TextMateRegistrar? _textMateRegistrar;
    private ExtensionHost? _extensionHost;
    private bool _disposed;

    public ExtensionService(IOutputService outputService, ITextMateService? textMateService = null, ISnippetService? snippetService = null)
    {
        _outputService = outputService;
        _textMateService = textMateService;
        _snippetService = snippetService;

        if (_textMateService != null)
        {
            _textMateRegistrar = new TextMateRegistrar(_textMateService);
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _extensionsDir = Path.Combine(userHome, ".vgs", "extensions");
        _stateFile = Path.Combine(userHome, ".vgs", "extensions-state.json");
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

    #region Discovery

    public async Task<IReadOnlyList<Extension>> DiscoverExtensionsAsync()
    {
        _extensions.Clear();

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
            _outputService.WriteError("[Extensions] ExtensionHostMain.js not found.", OutputCategory.General);
            return;
        }

        _extensionHost = new ExtensionHost(_outputService, scriptPath);
        _extensionHost.StateChanged += (s, running) => ExtensionHostStateChanged?.Invoke(this, running);
        _extensionHost.MessageReceived += (s, args) => ExtensionMessageReceived?.Invoke(this, args);
        _extensionHost.CommandRegistered += OnCommandRegistered;
        _extensionHost.HostCrashed += OnHostCrashed;

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
                    LoadContributions(ext);

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

    public Task<bool> EnableAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null)
        {
            return Task.FromResult(false);
        }

        extension.IsEnabled = true;
        extension.Status = ExtensionStatus.Installed;
        _enabledState[extensionId] = true;
        SaveState();

        // Re-activate static contributions
        LoadContributions(extension);

        ExtensionEnabled?.Invoke(this, new ExtensionEventArgs(extension));
        return Task.FromResult(true);
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
        LoadContributions(extension);

        // If the extension has a JS entry point, activate it in the extension host
        if (!string.IsNullOrEmpty(extension.Manifest?.Main))
        {
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
                LoadContributions(ext);

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

    private async void OnHostCrashed(object? sender, EventArgs e)
    {
        _outputService.WriteError("[Extensions] Extension host crashed. Attempting restart in 5 seconds...", OutputCategory.General);

        // Mark all extensions as inactive
        foreach (var ext in _extensions.Where(e => e.IsActive))
        {
            ext.IsActive = false;
            ext.Status = ExtensionStatus.Installed;
        }

        await Task.Delay(5000);

        try
        {
            await StartExtensionHostAsync();
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
        }

        return extension;
    }

    private void LoadContributions(Extension extension)
    {
        if (string.IsNullOrEmpty(extension.InstallPath) || !Directory.Exists(extension.InstallPath))
            return;

        var stats = new ExtensionContributionsLoadedEventArgs(extension);

        try
        {
            // Load themes
            stats.ThemesLoaded = LoadThemesFromExtension(extension);

            // Load grammars and language configurations via TextMateRegistrar
            stats.GrammarsLoaded = LoadGrammarsFromExtension(extension);

            // Load snippets
            stats.SnippetsLoaded = LoadSnippetsFromExtension(extension);

            var total = stats.ThemesLoaded + stats.GrammarsLoaded + stats.SnippetsLoaded;
            if (total > 0)
            {
                _outputService.WriteLine(
                    $"[Extensions] Loaded contributions from {extension.Name}: " +
                    $"{stats.ThemesLoaded} theme(s), {stats.GrammarsLoaded} grammar(s), " +
                    $"{stats.SnippetsLoaded} snippet file(s)",
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
    }

    private int LoadThemesFromExtension(Extension extension)
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

    private int LoadGrammarsFromExtension(Extension extension)
    {
        if (_textMateRegistrar == null) return 0;

        try
        {
            var task = _textMateRegistrar.RegisterFromExtensionAsync(extension.InstallPath, extension.Id);
            return task.GetAwaiter().GetResult();
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
            Path.Combine(baseDir, "ExtensionHostMain.js"),
            Path.Combine(baseDir, "Services", "ExtensionHostMain.js"),
            // Development paths
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "VisualGameStudio.ProjectSystem", "Services", "ExtensionHostMain.js")),
            // Relative to extensions dir
            Path.Combine(Path.GetDirectoryName(_extensionsDir) ?? "", "ExtensionHostMain.js"),
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _extensionHost?.Dispose();
        _httpClient.Dispose();
    }

    #endregion
}
