using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// ViewModel for the Extensions panel.
/// Provides browsing, searching, installing, uninstalling, enabling,
/// and disabling extensions from the Open VSX Registry.
/// Downloads VSIX files, extracts them, and activates static contributions.
/// </summary>
public partial class ExtensionsViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// The registry client. The panel used to hold its own HttpClient, its own base URLs and its own
    /// copy of the response DTOs — the last of the four Open VSX clients that existed in this repo.
    /// </summary>
    private readonly OpenVsxClient _openVsxClient;

    /// <summary>
    /// True when this ViewModel created the client — i.e. the designer's parameterless path. The
    /// container's client is shared with VsixInstaller and outlives this panel.
    /// </summary>
    private readonly bool _ownsOpenVsxClient;

    private readonly System.Timers.Timer _searchDebounceTimer;
    private CancellationTokenSource? _searchCts;

    /// <summary>
    /// Extension service for installation, activation, and lifecycle management.
    /// </summary>
    private IExtensionService? _extensionService;

    /// <summary>
    /// Path where extensions are installed locally.
    /// </summary>
    private readonly string _extensionsDirectory;

    [ObservableProperty]
    private ObservableCollection<ExtensionItemViewModel> _installedExtensions = new();

    [ObservableProperty]
    private ObservableCollection<ExtensionItemViewModel> _searchResults = new();

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _isInstalling;

    [ObservableProperty]
    private ExtensionItemViewModel? _selectedExtension;

    [ObservableProperty]
    private bool _showDetail;

    [ObservableProperty]
    private string _activeFilter = "Installed";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _hasSearchResults = true;

    /// <summary>
    /// Detail tab selection: 0 = Details, 1 = Contributions, 2 = Changelog.
    /// </summary>
    [ObservableProperty]
    private int _selectedDetailTab;

    /// <param name="openVsxClient">
    /// The shared registry client. Optional so the designer's parameterless construction still
    /// works; the container passes its singleton, which is the same instance VsixInstaller uses.
    /// </param>
    public ExtensionsViewModel(OpenVsxClient? openVsxClient = null)
    {
        _openVsxClient = openVsxClient ?? new OpenVsxClient();
        _ownsOpenVsxClient = openVsxClient == null;

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _extensionsDirectory = Path.Combine(userHome, ".vgs", "extensions");

        Directory.CreateDirectory(_extensionsDirectory);

        _searchDebounceTimer = new System.Timers.Timer(400);
        _searchDebounceTimer.AutoReset = false;
        _searchDebounceTimer.Elapsed += OnSearchDebounceElapsed;

        // Load installed extensions on startup
        LoadInstalledExtensions();
    }

    /// <summary>
    /// Sets the extension service for proper install/activate integration.
    /// Called from MainWindowViewModel after DI construction.
    /// </summary>
    public void SetExtensionService(IExtensionService extensionService)
    {
        _extensionService = extensionService;
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchDebounceTimer.Stop();
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            HasSearchResults = true;
            if (ActiveFilter != "Installed")
            {
                ActiveFilter = "Installed";
            }
            return;
        }
        _searchDebounceTimer.Start();
    }

    partial void OnSelectedExtensionChanged(ExtensionItemViewModel? value)
    {
        ShowDetail = value != null;
        if (value != null && !value.IsInstalled && string.IsNullOrEmpty(value.DetailMarkdown))
        {
            _ = LoadExtensionDetailAsync(value);
        }
    }

    private void OnSearchDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        // Timer callbacks arrive on a threadpool thread. SearchMarketplaceAsync
        // mutates UI-bound state (SearchResults, IsSearching, ...), so it must
        // start on the UI thread; its awaits then also resume there.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var query = SearchQuery;
            if (!string.IsNullOrWhiteSpace(query))
            {
                _ = SearchMarketplaceAsync(query);
            }
        });
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            await SearchMarketplaceAsync(SearchQuery);
        }
    }

    [RelayCommand]
    private async Task InstallAsync(ExtensionItemViewModel? extension)
    {
        if (extension == null || extension.IsInstalled) return;

        try
        {
            IsInstalling = true;
            StatusMessage = $"Installing {extension.DisplayName}...";

            // Fetch detail to get download URL if not already available
            if (string.IsNullOrEmpty(extension.DownloadUrl))
            {
                await LoadExtensionDetailAsync(extension);
            }

            if (string.IsNullOrEmpty(extension.DownloadUrl))
            {
                StatusMessage = $"Failed to install {extension.DisplayName}: download URL not available.";
                return;
            }

            // The panel used to download, extract and copy the .vsix itself — a third copy of an
            // install, beside ExtensionService's and VsixInstaller's, with its own extraction, its
            // own idea of a valid manifest and its own HttpClient. It now asks the service, which
            // owns acquisition (through VsixInstaller) and runtime registration alike.
            if (_extensionService == null)
            {
                // Only reachable from the designer's parameterless ctor; MainWindowViewModel hands
                // the service over at startup and ExtensionsPanelWiringTests guards that it does.
                StatusMessage = $"Failed to install {extension.DisplayName}: the extension service is unavailable.";
                return;
            }

            var result = await _extensionService.InstallFromUrlAsync(extension.DownloadUrl);

            if (!result.Success)
            {
                StatusMessage = $"Failed to install {extension.DisplayName}: {result.Error}";
                return;
            }

            // ⛔ NO DiscoverExtensionsAsync AND NO ActivateAsync HERE. Both used to follow the
            // hand-rolled install, and both are now already done: InstallFromUrlAsync loads the
            // contributions and activates. Calling them again re-registers the same extension —
            // duplicated commands and keybindings that fire twice, surfacing nowhere near the cause.
            extension.IsInstalled = true;
            extension.IsEnabled = true;
            extension.InstallPath = result.Extension?.InstallPath ?? "";
            extension.IsActive = result.Extension?.IsActive ?? false;
            extension.Status = result.RequiresRestart ? "Restart required" : "Installed";

            if (!InstalledExtensions.Any(e => e.Namespace == extension.Namespace))
            {
                InstalledExtensions.Add(extension);
            }

            StatusMessage = result.RequiresRestart
                ? $"{extension.DisplayName} installed — restart to activate."
                : $"{extension.DisplayName} installed successfully.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to install {extension.DisplayName}: {ex.Message}";
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(ExtensionItemViewModel? extension)
    {
        if (extension == null || !extension.IsInstalled) return;

        try
        {
            StatusMessage = $"Uninstalling {extension.DisplayName}...";

            // Uninstall via ExtensionService if available
            if (_extensionService != null && !string.IsNullOrEmpty(extension.Namespace))
            {
                await _extensionService.UninstallAsync(extension.Namespace);
            }

            var extensionDir = extension.InstallPath
                ?? Path.Combine(_extensionsDirectory, $"{extension.Publisher}.{extension.Name}");

            if (Directory.Exists(extensionDir))
            {
                Directory.Delete(extensionDir, true);
            }

            extension.IsInstalled = false;
            extension.IsEnabled = false;
            extension.InstallPath = null;
            extension.Status = "";

            InstalledExtensions.Remove(extension);

            StatusMessage = $"{extension.DisplayName} uninstalled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to uninstall {extension.DisplayName}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Enable(ExtensionItemViewModel? extension)
    {
        if (extension == null || !extension.IsInstalled) return;

        extension.IsEnabled = true;
        extension.Status = "";

        if (_extensionService != null && !string.IsNullOrEmpty(extension.Namespace))
        {
            _ = _extensionService.EnableAsync(extension.Namespace);
        }

        StatusMessage = $"{extension.DisplayName} enabled.";
    }

    [RelayCommand]
    private void Disable(ExtensionItemViewModel? extension)
    {
        if (extension == null || !extension.IsInstalled) return;

        extension.IsEnabled = false;
        extension.Status = "Disabled";

        if (_extensionService != null && !string.IsNullOrEmpty(extension.Namespace))
        {
            _ = _extensionService.DisableAsync(extension.Namespace);
        }

        StatusMessage = $"{extension.DisplayName} disabled.";
    }

    [RelayCommand]
    private void Refresh()
    {
        LoadInstalledExtensions();
        StatusMessage = "Extensions refreshed.";
    }

    [RelayCommand]
    private void OpenExtensionSettings()
    {
        // Navigate to extension settings directory
        if (Directory.Exists(_extensionsDirectory))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _extensionsDirectory,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private void SetFilter(string filter)
    {
        ActiveFilter = filter;

        switch (filter)
        {
            case "Installed":
                SelectedExtension = null;
                break;
            case "Popular":
                _ = SearchMarketplaceAsync("", "downloadCount", "desc");
                break;
            case "Recommended":
                _ = SearchMarketplaceAsync("", "relevance", "desc");
                break;
        }
    }

    [RelayCommand]
    private void BackToList()
    {
        SelectedExtension = null;
        ShowDetail = false;
    }

    private async Task SearchMarketplaceAsync(string query, string sortBy = "relevance", string sortOrder = "desc")
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            IsSearching = true;
            ActiveFilter = string.IsNullOrEmpty(query) ? ActiveFilter : "Search";

            var searchResult = await _openVsxClient.SearchAsync(
                query, sortBy: sortBy, sortOrder: sortOrder, limit: 30, ct: token);

            // ⛔ Cancellation is checked BEFORE the error channel, and the order is load-bearing.
            // SearchAsync deliberately never throws — it turns every failure into Error, INCLUDING
            // the OperationCanceledException raised when the next keystroke supersedes this search.
            // Reading Error first would flash "Search failed: A task was canceled" at anyone typing
            // faster than the 400ms debounce.
            if (token.IsCancellationRequested) return;

            // An empty list now means the registry genuinely matched nothing; a non-null Error means
            // the answer is unknown. The panel could previously only ever say "No extensions found",
            // including when Open VSX was down (9da01b0).
            if (searchResult.Error != null)
            {
                SearchResults.Clear();
                HasSearchResults = false;
                StatusMessage = $"Search failed: {searchResult.Error}";
                return;
            }

            SearchResults.Clear();

            if (searchResult?.Extensions != null)
            {
                foreach (var ext in searchResult.Extensions)
                {
                    var item = new ExtensionItemViewModel
                    {
                        Name = ext.Name,
                        DisplayName = ext.DisplayName ?? ext.Name,
                        Description = ext.Description ?? "",
                        Version = ext.Version,
                        Publisher = ext.Namespace,
                        Namespace = $"{ext.Namespace}.{ext.Name}",
                        // The shared DTO models Open VSX's `files` as the dictionary it actually is,
                        // rather than the fixed four-property shape the panel's own copy assumed.
                        IconUrl = ext.Files.GetValueOrDefault("icon"),
                        DownloadUrl = ext.Files.GetValueOrDefault("download"),
                        Rating = ext.AverageRating ?? 0,
                        // Open VSX counts downloads in a long; the panel binds an int. Saturating
                        // rather than casting, because an unchecked narrowing would wrap a very
                        // popular extension's count to a negative number.
                        InstallCount = (int)Math.Min(ext.DownloadCount, int.MaxValue),
                        IsInstalled = InstalledExtensions.Any(
                            i => string.Equals(i.Namespace, $"{ext.Namespace}.{ext.Name}", StringComparison.OrdinalIgnoreCase))
                    };

                    if (ext.Categories != null)
                    {
                        foreach (var cat in ext.Categories)
                        {
                            item.Categories.Add(cat);
                        }
                    }

                    SearchResults.Add(item);
                }
            }

            HasSearchResults = SearchResults.Count > 0;
            StatusMessage = SearchResults.Count > 0
                ? $"Found {SearchResults.Count} extension(s)"
                : "No extensions found";
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled by a newer search
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
            HasSearchResults = false;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private async Task LoadExtensionDetailAsync(ExtensionItemViewModel extension)
    {
        try
        {
            var detail = await _openVsxClient.GetExtensionAsync(extension.Publisher, extension.Name);

            if (detail != null)
            {
                extension.DetailMarkdown = detail.Description ?? extension.Description;
                extension.DownloadUrl = detail.Files?.GetValueOrDefault("download");
                extension.InstallCount = (int)Math.Min(detail.DownloadCount, int.MaxValue);
            }
        }
        catch
        {
            // Non-critical: detail load failed, still show what we have
        }
    }

    private void LoadInstalledExtensions()
    {
        InstalledExtensions.Clear();

        if (!Directory.Exists(_extensionsDirectory)) return;

        foreach (var dir in Directory.GetDirectories(_extensionsDirectory))
        {
            // Check for package.json (extracted VSIX format)
            var packageJsonPath = Path.Combine(dir, "package.json");
            var extensionSubPath = Path.Combine(dir, "extension", "package.json");
            // Also support legacy extension.json metadata
            var legacyMetaPath = Path.Combine(dir, "extension.json");

            if (File.Exists(packageJsonPath) || File.Exists(extensionSubPath))
            {
                var jsonPath = File.Exists(packageJsonPath) ? packageJsonPath : extensionSubPath;
                var actualDir = Path.GetDirectoryName(jsonPath)!;

                try
                {
                    var json = File.ReadAllText(jsonPath);
                    using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
                    var root = doc.RootElement;

                    var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                    var displayName = root.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() ?? name : name;
                    var description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() ?? "" : "";
                    var version = root.TryGetProperty("version", out var verEl) ? verEl.GetString() ?? "" : "";
                    var publisher = root.TryGetProperty("publisher", out var pubEl) ? pubEl.GetString() ?? "" : "";
                    var icon = root.TryGetProperty("icon", out var iconEl) ? iconEl.GetString() : null;

                    var item = new ExtensionItemViewModel
                    {
                        Name = name,
                        DisplayName = displayName,
                        Description = description,
                        Version = version,
                        Publisher = publisher,
                        Namespace = $"{publisher}.{name}",
                        IconUrl = icon != null ? Path.Combine(actualDir, icon) : null,
                        IsInstalled = true,
                        IsEnabled = true,
                        InstallPath = actualDir,
                        Status = ""
                    };

                    if (root.TryGetProperty("categories", out var catsEl) && catsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var cat in catsEl.EnumerateArray())
                        {
                            var catStr = cat.GetString();
                            if (!string.IsNullOrEmpty(catStr))
                                item.Categories.Add(catStr);
                        }
                    }

                    InstalledExtensions.Add(item);
                }
                catch
                {
                    // Skip corrupted package.json
                }
            }
            else if (File.Exists(legacyMetaPath))
            {
                try
                {
                    var json = File.ReadAllText(legacyMetaPath);
                    var metadata = JsonSerializer.Deserialize<ExtensionMetadata>(json);
                    if (metadata == null) continue;

                    var item = new ExtensionItemViewModel
                    {
                        Name = metadata.Name ?? "",
                        DisplayName = metadata.DisplayName ?? metadata.Name ?? "",
                        Description = metadata.Description ?? "",
                        Version = metadata.Version ?? "",
                        Publisher = metadata.Publisher ?? "",
                        Namespace = metadata.Namespace ?? "",
                        IconUrl = metadata.IconUrl,
                        IsInstalled = true,
                        IsEnabled = metadata.IsEnabled,
                        InstallPath = dir,
                        Status = metadata.IsEnabled ? "" : "Disabled"
                    };

                    if (metadata.Categories != null)
                    {
                        foreach (var cat in metadata.Categories)
                        {
                            item.Categories.Add(cat);
                        }
                    }

                    InstalledExtensions.Add(item);
                }
                catch
                {
                    // Skip corrupted extension metadata
                }
            }
        }
    }

    private void UpdateExtensionMetadata(ExtensionItemViewModel extension)
    {
        var extensionDir = extension.InstallPath
            ?? Path.Combine(_extensionsDirectory, $"{extension.Publisher}.{extension.Name}");

        var metadataPath = Path.Combine(extensionDir, "extension.json");
        if (!File.Exists(metadataPath)) return;

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize<ExtensionMetadata>(json);
            if (metadata == null) return;

            metadata.IsEnabled = extension.IsEnabled;

            var updatedJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metadataPath, updatedJson);
        }
        catch
        {
            // Non-critical metadata update failure
        }
    }

    public void Dispose()
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Elapsed -= OnSearchDebounceElapsed;
        _searchDebounceTimer.Dispose();

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        // Only the designer's self-built client. The container's is shared with VsixInstaller and
        // disposing it here would break installs.
        if (_ownsOpenVsxClient)
        {
            _openVsxClient.Dispose();
        }
    }
}

/// <summary>
/// Local metadata stored for each installed extension.
/// </summary>
internal class ExtensionMetadata
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public string? Namespace { get; set; }
    public string? IconUrl { get; set; }
    public List<string>? Categories { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime InstalledDate { get; set; }
}

// The Open VSX response models that used to live here were a second, thinner copy of the ones in
// OpenVsxClient.cs — same wire format, different C# shape (a fixed four-property `files` object
// where the API returns a dictionary), and no error channel. They went with the HttpClient.
