using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Handles downloading, installing, uninstalling, and updating VS Code extensions (.vsix files)
/// from the Open VSX Registry or local files. Extensions are extracted to ~/.vgs/extensions/.
/// </summary>
public class VsixInstaller : IDisposable
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

    private readonly OpenVsxClient _vsxClient;
    private readonly string _extensionsDir;
    private readonly string _stateFilePath;
    private readonly object _stateLock = new();
    private ExtensionStateFile _state;
    private bool _disposed;

    /// <summary>
    /// Gets the directory where extensions are installed.
    /// </summary>
    public string ExtensionsDirectory => _extensionsDir;

    /// <summary>
    /// Raised when an extension is installed or updated.
    /// </summary>
    public event EventHandler<VsixInstallerEventArgs>? ExtensionInstalled;

    /// <summary>
    /// Raised when an extension is uninstalled.
    /// </summary>
    public event EventHandler<VsixInstallerEventArgs>? ExtensionUninstalled;

    public VsixInstaller(OpenVsxClient? vsxClient = null, string? extensionsDir = null)
    {
        _vsxClient = vsxClient ?? new OpenVsxClient();

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _extensionsDir = extensionsDir ?? Path.Combine(userHome, ".vgs", "extensions");
        _stateFilePath = Path.Combine(_extensionsDir, "extensions.json");

        Directory.CreateDirectory(_extensionsDir);
        _state = LoadState();
    }

    /// <summary>
    /// Downloads a VSIX from Open VSX and returns the local file path of the downloaded .vsix.
    /// </summary>
    /// <param name="publisher">Publisher namespace (e.g., "redhat").</param>
    /// <param name="name">Extension name (e.g., "java").</param>
    /// <param name="version">Specific version or null/"latest" for the latest.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Path to the downloaded .vsix file in a temp directory.</returns>
    public async Task<string> DownloadExtensionAsync(
        string publisher,
        string name,
        string? version = null,
        CancellationToken ct = default)
    {
        // Resolve download URL
        var downloadUrl = await _vsxClient.ResolveDownloadUrlAsync(publisher, name, version, ct);
        if (string.IsNullOrEmpty(downloadUrl))
        {
            throw new InvalidOperationException(
                $"Extension {publisher}.{name}" +
                (version != null ? $" version {version}" : "") +
                " not found on Open VSX Registry.");
        }

        // Download to temp file
        var tempDir = Path.Combine(Path.GetTempPath(), "vgs-extensions");
        Directory.CreateDirectory(tempDir);

        var fileName = $"{publisher}.{name}-{version ?? "latest"}.vsix";
        var tempPath = Path.Combine(tempDir, fileName);

        await _vsxClient.DownloadVsixToFileAsync(downloadUrl, tempPath, ct: ct);

        return tempPath;
    }

    /// <summary>
    /// Installs an extension from a local .vsix file.
    /// Extracts to ~/.vgs/extensions/{publisher}.{name}-{version}/ and registers in state file.
    /// </summary>
    /// <param name="vsixPath">Path to the .vsix file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Information about the installed extension.</returns>
    public async Task<ExtensionInfo> InstallVsixAsync(string vsixPath, CancellationToken ct = default)
    {
        if (!File.Exists(vsixPath))
        {
            throw new FileNotFoundException("VSIX file not found.", vsixPath);
        }

        // Extract to temp directory first to read manifest
        var tempDir = Path.Combine(Path.GetTempPath(), $"vgs-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Extract ZIP (.vsix is a ZIP archive)
            ZipFile.ExtractToDirectory(vsixPath, tempDir, overwriteFiles: true);

            // Find package.json — may be at root or in extension/ subdirectory
            var packageJsonPath = FindPackageJson(tempDir);
            if (packageJsonPath == null)
            {
                throw new InvalidOperationException(
                    "Invalid VSIX: no package.json found. The archive must contain " +
                    "an extension/package.json or a root package.json.");
            }

            // Parse manifest
            var manifestJson = await File.ReadAllTextAsync(packageJsonPath, ct);
            var manifest = JsonSerializer.Deserialize<VsixPackageManifest>(manifestJson, JsonOptions);

            if (manifest == null || string.IsNullOrEmpty(manifest.Name))
            {
                throw new InvalidOperationException("Invalid package.json: missing required 'name' field.");
            }

            if (string.IsNullOrEmpty(manifest.Publisher))
            {
                throw new InvalidOperationException("Invalid package.json: missing required 'publisher' field.");
            }

            if (string.IsNullOrEmpty(manifest.Version))
            {
                throw new InvalidOperationException("Invalid package.json: missing required 'version' field.");
            }

            // Validate engine compatibility (loose check — accept if vscode engine is specified)
            ValidateCompatibility(manifest);

            var extensionId = $"{manifest.Publisher}.{manifest.Name}";
            var installDirName = $"{extensionId}-{manifest.Version}";
            var installDir = Path.Combine(_extensionsDir, installDirName);

            // Remove existing version if present
            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, true);
            }

            // Also remove any older versions of the same extension
            RemoveOldVersions(extensionId, installDirName);

            // Determine the source directory containing extension files.
            // In a VSIX, the extension content is typically under an "extension/" subdirectory.
            var extensionContentDir = Path.GetDirectoryName(packageJsonPath)!;

            // Copy extension content to install directory
            CopyDirectory(extensionContentDir, installDir);

            // Build extension info
            var info = new ExtensionInfo
            {
                Id = extensionId,
                Name = manifest.DisplayName ?? manifest.Name,
                Publisher = manifest.Publisher,
                Version = manifest.Version,
                Description = manifest.Description ?? "",
                InstallPath = installDir,
                Enabled = true,
                ActivationEvents = manifest.ActivationEvents ?? new List<string>(),
                InstalledAt = DateTime.UtcNow,
                UpdatedAt = null,
                HasStaticContributions = HasStaticContributions(manifest),
                HasDynamicExtension = !string.IsNullOrEmpty(manifest.Main) || !string.IsNullOrEmpty(manifest.Browser),
                Categories = manifest.Categories ?? new List<string>()
            };

            // Update state
            lock (_stateLock)
            {
                _state.Extensions.RemoveAll(e => e.Id == extensionId);
                _state.Extensions.Add(info);
                SaveState();
            }

            ExtensionInstalled?.Invoke(this, new VsixInstallerEventArgs(info, VsixInstallerAction.Installed));
            return info;
        }
        finally
        {
            // Clean up temp extraction directory
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    /// <summary>
    /// Downloads an extension from Open VSX and installs it in one step.
    /// </summary>
    /// <param name="publisher">Publisher namespace.</param>
    /// <param name="name">Extension name.</param>
    /// <param name="version">Specific version or null for latest.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<ExtensionInfo> DownloadAndInstallAsync(
        string publisher,
        string name,
        string? version = null,
        CancellationToken ct = default)
    {
        var vsixPath = await DownloadExtensionAsync(publisher, name, version, ct);
        try
        {
            return await InstallVsixAsync(vsixPath, ct);
        }
        finally
        {
            try { File.Delete(vsixPath); } catch { }
        }
    }

    /// <summary>
    /// Uninstalls an extension by its ID (publisher.name).
    /// Removes the extension directory and state entry.
    /// </summary>
    /// <param name="extensionId">Extension ID in publisher.name format.</param>
    public void UninstallExtension(string extensionId)
    {
        ExtensionInfo? removedInfo = null;

        lock (_stateLock)
        {
            var entry = _state.Extensions.FirstOrDefault(e =>
                e.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                throw new InvalidOperationException($"Extension '{extensionId}' is not installed.");
            }

            removedInfo = entry;

            // Remove files
            if (!string.IsNullOrEmpty(entry.InstallPath) && Directory.Exists(entry.InstallPath))
            {
                Directory.Delete(entry.InstallPath, true);
            }

            _state.Extensions.Remove(entry);
            SaveState();
        }

        if (removedInfo != null)
        {
            ExtensionUninstalled?.Invoke(this, new VsixInstallerEventArgs(removedInfo, VsixInstallerAction.Uninstalled));
        }
    }

    /// <summary>
    /// Updates an installed extension to the latest version from Open VSX.
    /// </summary>
    /// <param name="extensionId">Extension ID in publisher.name format.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Updated extension info, or null if no update was available.</returns>
    public async Task<ExtensionInfo?> UpdateExtensionAsync(string extensionId, CancellationToken ct = default)
    {
        ExtensionInfo? current;
        lock (_stateLock)
        {
            current = _state.Extensions.FirstOrDefault(e =>
                e.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        }

        if (current == null)
        {
            throw new InvalidOperationException($"Extension '{extensionId}' is not installed.");
        }

        // Parse publisher and name from ID
        var parts = extensionId.Split('.', 2);
        if (parts.Length != 2)
        {
            throw new InvalidOperationException($"Invalid extension ID format: '{extensionId}'. Expected 'publisher.name'.");
        }

        var publisher = parts[0];
        var name = parts[1];

        // Check latest version on Open VSX
        var detail = await _vsxClient.GetExtensionAsync(publisher, name, ct);
        if (detail == null)
        {
            return null; // Extension not found on registry
        }

        // Compare versions
        if (string.Equals(detail.Version, current.Version, StringComparison.OrdinalIgnoreCase))
        {
            return null; // Already up to date
        }

        // Download and install the new version
        var updated = await DownloadAndInstallAsync(publisher, name, detail.Version, ct);
        updated.UpdatedAt = DateTime.UtcNow;

        // Preserve enabled state from previous installation
        updated.Enabled = current.Enabled;

        lock (_stateLock)
        {
            var idx = _state.Extensions.FindIndex(e =>
                e.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _state.Extensions[idx] = updated;
            }
            SaveState();
        }

        ExtensionInstalled?.Invoke(this, new VsixInstallerEventArgs(updated, VsixInstallerAction.Updated));
        return updated;
    }

    /// <summary>
    /// Checks if a newer version is available for an installed extension.
    /// </summary>
    /// <param name="extensionId">Extension ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The latest version string if an update is available, or null if up to date.</returns>
    public async Task<string?> CheckForUpdateAsync(string extensionId, CancellationToken ct = default)
    {
        ExtensionInfo? current;
        lock (_stateLock)
        {
            current = _state.Extensions.FirstOrDefault(e =>
                e.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        }

        if (current == null) return null;

        var parts = extensionId.Split('.', 2);
        if (parts.Length != 2) return null;

        var detail = await _vsxClient.GetExtensionAsync(parts[0], parts[1], ct);
        if (detail == null) return null;

        if (!string.Equals(detail.Version, current.Version, StringComparison.OrdinalIgnoreCase))
        {
            return detail.Version;
        }

        return null;
    }

    /// <summary>
    /// Gets all installed extensions from the state file.
    /// </summary>
    public IReadOnlyList<ExtensionInfo> GetInstalledExtensions()
    {
        lock (_stateLock)
        {
            return _state.Extensions.ToList();
        }
    }

    /// <summary>
    /// Gets a specific installed extension by ID.
    /// </summary>
    public ExtensionInfo? GetInstalledExtension(string extensionId)
    {
        lock (_stateLock)
        {
            return _state.Extensions.FirstOrDefault(e =>
                e.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Enables or disables an installed extension.
    /// </summary>
    public void SetEnabled(string extensionId, bool enabled)
    {
        lock (_stateLock)
        {
            var entry = _state.Extensions.FirstOrDefault(e =>
                e.Id.Equals(extensionId, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.Enabled = enabled;
                SaveState();
            }
        }
    }

    #region Private Methods

    private static string? FindPackageJson(string extractDir)
    {
        // Check extension/ subdirectory first (standard VSIX layout)
        var inExtDir = Path.Combine(extractDir, "extension", "package.json");
        if (File.Exists(inExtDir))
            return inExtDir;

        // Check root (some extensions may have it at root)
        var atRoot = Path.Combine(extractDir, "package.json");
        if (File.Exists(atRoot))
            return atRoot;

        return null;
    }

    private static void ValidateCompatibility(VsixPackageManifest manifest)
    {
        // We accept any extension that targets VS Code (engines.vscode is present).
        // We don't enforce exact version matching since we support static contributions
        // (grammars, themes, snippets) which don't depend on VS Code API version.
        // Dynamic extensions (with main/browser entry points) will be flagged but still installed.

        if (manifest.Engines == null)
        {
            // No engines specified — allow installation
            return;
        }

        // If engines.vscode is not present, this might not be a VS Code extension
        if (!manifest.Engines.ContainsKey("vscode"))
        {
            System.Diagnostics.Debug.WriteLine(
                $"Warning: Extension {manifest.Publisher}.{manifest.Name} " +
                "does not specify engines.vscode. It may not be a VS Code extension.");
        }
    }

    private static bool HasStaticContributions(VsixPackageManifest manifest)
    {
        if (manifest.Contributes == null) return false;

        return (manifest.Contributes.Grammars?.Count > 0) ||
               (manifest.Contributes.Themes?.Count > 0) ||
               (manifest.Contributes.Snippets?.Count > 0) ||
               (manifest.Contributes.Languages?.Count > 0) ||
               (manifest.Contributes.IconThemes?.Count > 0);
    }

    private void RemoveOldVersions(string extensionId, string currentDirName)
    {
        try
        {
            var prefix = extensionId + "-";
            foreach (var dir in Directory.GetDirectories(_extensionsDir))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    !dirName.Equals(currentDirName, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(dir, true);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to remove old versions: {ex.Message}");
        }
    }

    private ExtensionStateFile LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            try
            {
                var json = File.ReadAllText(_stateFilePath);
                var state = JsonSerializer.Deserialize<ExtensionStateFile>(json, JsonOptions);
                if (state != null) return state;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load extension state: {ex.Message}");
            }
        }

        return new ExtensionStateFile();
    }

    private void SaveState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, JsonOptions);
            File.WriteAllText(_stateFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save extension state: {ex.Message}");
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
        if (!_disposed)
        {
            _vsxClient.Dispose();
            _disposed = true;
        }
    }

    #endregion
}

#region State and Manifest Models

/// <summary>
/// Persisted state file for installed extensions (~/.vgs/extensions/extensions.json).
/// </summary>
public class ExtensionStateFile
{
    [JsonPropertyName("extensions")]
    public List<ExtensionInfo> Extensions { get; set; } = new();
}

/// <summary>
/// Describes an installed extension, stored in the state file and returned by the installer.
/// </summary>
public class ExtensionInfo
{
    /// <summary>
    /// Extension ID in publisher.name format.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Publisher name.
    /// </summary>
    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Installed version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// Description.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Absolute path to the installation directory.
    /// </summary>
    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = "";

    /// <summary>
    /// Whether the extension is enabled (contributions will be loaded).
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Activation events from package.json (e.g., "onLanguage:python").
    /// </summary>
    [JsonPropertyName("activationEvents")]
    public List<string> ActivationEvents { get; set; } = new();

    /// <summary>
    /// When the extension was first installed.
    /// </summary>
    [JsonPropertyName("installedAt")]
    public DateTime InstalledAt { get; set; }

    /// <summary>
    /// When the extension was last updated, or null if never updated.
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Whether this extension has static contributions (grammars, themes, snippets)
    /// that can be loaded without Node.js.
    /// </summary>
    [JsonPropertyName("hasStaticContributions")]
    public bool HasStaticContributions { get; set; }

    /// <summary>
    /// Whether this extension has a dynamic entry point (main/browser field)
    /// that requires Node.js to run.
    /// </summary>
    [JsonPropertyName("hasDynamicExtension")]
    public bool HasDynamicExtension { get; set; }

    /// <summary>
    /// Extension categories.
    /// </summary>
    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();
}

/// <summary>
/// Package.json manifest parsed from the VSIX.
/// Contains both standard npm fields and VS Code extension fields.
/// </summary>
internal class VsixPackageManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    [JsonPropertyName("main")]
    public string? Main { get; set; }

    [JsonPropertyName("browser")]
    public string? Browser { get; set; }

    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    [JsonPropertyName("keywords")]
    public List<string>? Keywords { get; set; }

    [JsonPropertyName("activationEvents")]
    public List<string>? ActivationEvents { get; set; }

    [JsonPropertyName("engines")]
    public Dictionary<string, string>? Engines { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    [JsonPropertyName("repository")]
    public JsonElement? Repository { get; set; }

    [JsonPropertyName("contributes")]
    public VsixContributes? Contributes { get; set; }
}

/// <summary>
/// The "contributes" section of package.json, parsed for static contribution detection.
/// </summary>
internal class VsixContributes
{
    [JsonPropertyName("languages")]
    public List<JsonElement>? Languages { get; set; }

    [JsonPropertyName("grammars")]
    public List<JsonElement>? Grammars { get; set; }

    [JsonPropertyName("themes")]
    public List<JsonElement>? Themes { get; set; }

    [JsonPropertyName("snippets")]
    public List<JsonElement>? Snippets { get; set; }

    [JsonPropertyName("iconThemes")]
    public List<JsonElement>? IconThemes { get; set; }

    [JsonPropertyName("commands")]
    public List<JsonElement>? Commands { get; set; }

    [JsonPropertyName("keybindings")]
    public List<JsonElement>? Keybindings { get; set; }

    [JsonPropertyName("configuration")]
    public JsonElement? Configuration { get; set; }

    [JsonPropertyName("debuggers")]
    public List<JsonElement>? Debuggers { get; set; }
}

/// <summary>
/// Event args for VsixInstaller events.
/// </summary>
public class VsixInstallerEventArgs : EventArgs
{
    public ExtensionInfo Extension { get; }
    public VsixInstallerAction Action { get; }

    public VsixInstallerEventArgs(ExtensionInfo extension, VsixInstallerAction action)
    {
        Extension = extension;
        Action = action;
    }
}

/// <summary>
/// Action that triggered the installer event.
/// </summary>
public enum VsixInstallerAction
{
    Installed,
    Updated,
    Uninstalled
}

#endregion
