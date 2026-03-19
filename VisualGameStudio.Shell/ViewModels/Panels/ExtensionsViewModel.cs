using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// ViewModel for the Extensions panel.
/// Provides browsing, searching, installing, uninstalling, enabling,
/// and disabling extensions from the Open VSX Registry.
/// </summary>
public partial class ExtensionsViewModel : ViewModelBase
{
    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private const string OpenVsxSearchUrl = "https://open-vsx.org/api/-/search";
    private const string OpenVsxApiUrl = "https://open-vsx.org/api";

    private readonly System.Timers.Timer _searchDebounceTimer;
    private CancellationTokenSource? _searchCts;

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

    public ExtensionsViewModel()
    {
        _extensionsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VisualGameStudio", "Extensions");

        Directory.CreateDirectory(_extensionsDirectory);

        _searchDebounceTimer = new System.Timers.Timer(400);
        _searchDebounceTimer.AutoReset = false;
        _searchDebounceTimer.Elapsed += OnSearchDebounceElapsed;

        // Load installed extensions on startup
        LoadInstalledExtensions();
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

    private async void OnSearchDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        var query = SearchQuery;
        if (!string.IsNullOrWhiteSpace(query))
        {
            await SearchMarketplaceAsync(query);
        }
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

            // Download the extension
            var extensionDir = Path.Combine(_extensionsDirectory, $"{extension.Publisher}.{extension.Name}");
            Directory.CreateDirectory(extensionDir);

            var vsixPath = Path.Combine(extensionDir, $"{extension.Name}-{extension.Version}.vsix");

            using var response = await _httpClient.GetAsync(extension.DownloadUrl);
            response.EnsureSuccessStatusCode();

            await using var fs = File.Create(vsixPath);
            await response.Content.CopyToAsync(fs);

            // Write extension metadata
            var metadataPath = Path.Combine(extensionDir, "extension.json");
            var metadata = new ExtensionMetadata
            {
                Name = extension.Name,
                DisplayName = extension.DisplayName,
                Description = extension.Description,
                Version = extension.Version,
                Publisher = extension.Publisher,
                Namespace = extension.Namespace,
                IconUrl = extension.IconUrl,
                Categories = extension.Categories.ToList(),
                IsEnabled = true,
                InstalledDate = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(metadataPath, json);

            extension.IsInstalled = true;
            extension.IsEnabled = true;
            extension.InstallPath = extensionDir;
            extension.Status = "Installed";

            // Add to installed list if not already there
            if (!InstalledExtensions.Any(e => e.Namespace == extension.Namespace))
            {
                InstalledExtensions.Add(extension);
            }

            StatusMessage = $"{extension.DisplayName} installed successfully.";
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
        UpdateExtensionMetadata(extension);
        StatusMessage = $"{extension.DisplayName} enabled.";
    }

    [RelayCommand]
    private void Disable(ExtensionItemViewModel? extension)
    {
        if (extension == null || !extension.IsInstalled) return;

        extension.IsEnabled = false;
        extension.Status = "Disabled";
        UpdateExtensionMetadata(extension);
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
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            IsSearching = true;
            ActiveFilter = string.IsNullOrEmpty(query) ? ActiveFilter : "Search";

            var url = $"{OpenVsxSearchUrl}?query={Uri.EscapeDataString(query)}&size=30&sortBy={sortBy}&sortOrder={sortOrder}";

            var response = await _httpClient.GetStringAsync(url, token);
            var searchResult = JsonSerializer.Deserialize<OpenVsxSearchResult>(response);

            if (token.IsCancellationRequested) return;

            SearchResults.Clear();

            if (searchResult?.Extensions != null)
            {
                foreach (var ext in searchResult.Extensions)
                {
                    var item = new ExtensionItemViewModel
                    {
                        Name = ext.Name ?? "",
                        DisplayName = ext.DisplayName ?? ext.Name ?? "",
                        Description = ext.Description ?? "",
                        Version = ext.Version ?? "",
                        Publisher = ext.Namespace ?? "",
                        Namespace = $"{ext.Namespace}.{ext.Name}",
                        IconUrl = ext.Files?.Icon,
                        DownloadUrl = ext.Files?.Download,
                        Rating = ext.AverageRating ?? 0,
                        InstallCount = ext.DownloadCount ?? 0,
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
            var url = $"{OpenVsxApiUrl}/{extension.Publisher}/{extension.Name}";
            var response = await _httpClient.GetStringAsync(url);
            var detail = JsonSerializer.Deserialize<OpenVsxExtensionDetail>(response);

            if (detail != null)
            {
                extension.DetailMarkdown = detail.Description ?? extension.Description;
                extension.DownloadUrl = detail.Files?.Download;
                if (detail.DownloadCount.HasValue)
                    extension.InstallCount = detail.DownloadCount.Value;
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
            var metadataPath = Path.Combine(dir, "extension.json");
            if (!File.Exists(metadataPath)) continue;

            try
            {
                var json = File.ReadAllText(metadataPath);
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

// --- Open VSX API response models ---

internal class OpenVsxSearchResult
{
    public List<OpenVsxExtension>? Extensions { get; set; }
    public int TotalSize { get; set; }
}

internal class OpenVsxExtension
{
    public string? Name { get; set; }
    public string? Namespace { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public double? AverageRating { get; set; }
    public int? DownloadCount { get; set; }
    public List<string>? Categories { get; set; }
    public OpenVsxFiles? Files { get; set; }
}

internal class OpenVsxExtensionDetail
{
    public string? Name { get; set; }
    public string? Namespace { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public int? DownloadCount { get; set; }
    public OpenVsxFiles? Files { get; set; }
}

internal class OpenVsxFiles
{
    public string? Download { get; set; }
    public string? Icon { get; set; }
    public string? Readme { get; set; }
    public string? Changelog { get; set; }
}
