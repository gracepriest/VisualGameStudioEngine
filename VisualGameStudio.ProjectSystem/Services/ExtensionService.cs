using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Manages IDE extensions/plugins.
/// </summary>
public class ExtensionService : IExtensionService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly List<Extension> _extensions = new();
    private readonly Dictionary<string, bool> _enabledState = new();
    private readonly string _extensionsDir;
    private readonly string _stateFile;
    private readonly HttpClient _httpClient;

    public ExtensionService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _extensionsDir = Path.Combine(appData, "VisualGameStudio", "extensions");
        _stateFile = Path.Combine(appData, "VisualGameStudio", "extensions-state.json");
        _httpClient = new HttpClient();

        Directory.CreateDirectory(_extensionsDir);
        LoadState();
    }

    public IReadOnlyList<Extension> InstalledExtensions => _extensions.ToList();

    public IReadOnlyList<Extension> EnabledExtensions => _extensions.Where(e => e.IsEnabled).ToList();

    public string ExtensionsDirectory => _extensionsDir;

    public event EventHandler<ExtensionEventArgs>? ExtensionInstalled;
    public event EventHandler<ExtensionEventArgs>? ExtensionUninstalled;
    public event EventHandler<ExtensionEventArgs>? ExtensionEnabled;
    public event EventHandler<ExtensionEventArgs>? ExtensionDisabled;
    public event EventHandler<ExtensionEventArgs>? ExtensionActivated;
    public event EventHandler<ExtensionEventArgs>? ExtensionDeactivated;

    public async Task<IReadOnlyList<Extension>> DiscoverExtensionsAsync()
    {
        _extensions.Clear();

        if (!Directory.Exists(_extensionsDir))
        {
            return _extensions;
        }

        foreach (var dir in Directory.GetDirectories(_extensionsDir))
        {
            var manifestPath = Path.Combine(dir, "package.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var extension = await LoadExtensionFromDirectoryAsync(dir);
                    if (extension != null)
                    {
                        if (_enabledState.TryGetValue(extension.Id, out var enabled))
                        {
                            extension.IsEnabled = enabled;
                        }
                        _extensions.Add(extension);
                    }
                }
                catch
                {
                    // Skip invalid extensions
                }
            }
        }

        return _extensions;
    }

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
                var installDir = Path.Combine(_extensionsDir, extensionId);

                // Remove existing installation
                if (Directory.Exists(installDir))
                {
                    Directory.Delete(installDir, true);
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

                    return new ExtensionInstallResult { Success = true, Extension = ext };
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
            if (Directory.Exists(extension.InstallPath))
            {
                Directory.Delete(extension.InstallPath, true);
            }

            _extensions.Remove(extension);
            _enabledState.Remove(extensionId);
            SaveState();

            ExtensionUninstalled?.Invoke(this, new ExtensionEventArgs(extension));
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> EnableAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null)
        {
            return Task.FromResult(false);
        }

        extension.IsEnabled = true;
        _enabledState[extensionId] = true;
        SaveState();

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

        extension.IsEnabled = false;
        _enabledState[extensionId] = false;
        SaveState();

        ExtensionDisabled?.Invoke(this, new ExtensionEventArgs(extension));
        return Task.FromResult(true);
    }

    public Extension? GetExtension(string extensionId)
    {
        return _extensions.FirstOrDefault(e => e.Id == extensionId);
    }

    public Task<bool> ActivateAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null || !extension.IsEnabled)
        {
            return Task.FromResult(false);
        }

        if (extension.IsActive)
        {
            return Task.FromResult(true);
        }

        // Load extension contributions (grammars, themes, etc.)
        LoadContributions(extension);

        extension.IsActive = true;
        ExtensionActivated?.Invoke(this, new ExtensionEventArgs(extension));
        return Task.FromResult(true);
    }

    public Task<bool> DeactivateAsync(string extensionId)
    {
        var extension = _extensions.FirstOrDefault(e => e.Id == extensionId);
        if (extension == null || !extension.IsActive)
        {
            return Task.FromResult(false);
        }

        // Unload extension contributions
        UnloadContributions(extension);

        extension.IsActive = false;
        ExtensionDeactivated?.Invoke(this, new ExtensionEventArgs(extension));
        return Task.FromResult(true);
    }

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

    #region Private Methods

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
            IsEnabled = true
        };

        if (!string.IsNullOrEmpty(manifest.Icon))
        {
            extension.IconPath = Path.Combine(directory, manifest.Icon);
        }

        if (manifest.Repository != null)
        {
            extension.Repository = manifest.Repository.Url;
        }

        // Load contributions
        if (manifest.Contributes != null)
        {
            extension.Contributions = manifest.Contributes;
        }

        return extension;
    }

    private void LoadContributions(Extension extension)
    {
        // Load grammars, themes, snippets, etc.
        // This would integrate with TextMateService, theme service, etc.
    }

    private void UnloadContributions(Extension extension)
    {
        // Unload grammars, themes, snippets, etc.
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

            var json = JsonSerializer.Serialize(_enabledState, JsonOptions);
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
}
