using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Service for interacting with the extension marketplace.
/// </summary>
public class MarketplaceService : IMarketplaceService, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly string _marketplaceUrl;
    private bool _isAvailable;
    private bool _disposed;

    public MarketplaceService(string? marketplaceUrl = null)
    {
        _marketplaceUrl = marketplaceUrl ?? "https://marketplace.visualgamestudio.com/api";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_marketplaceUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VisualGameStudio/1.0");
    }

    public bool IsAvailable => _isAvailable;

    public event EventHandler<bool>? AvailabilityChanged;

    public async Task<MarketplaceSearchResult> SearchAsync(
        string query,
        string? category = null,
        MarketplaceSortOrder sortBy = MarketplaceSortOrder.Relevance,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/search?q={Uri.EscapeDataString(query)}&page={page}&pageSize={pageSize}&sortBy={sortBy}";
            if (!string.IsNullOrEmpty(category))
            {
                url += $"&category={Uri.EscapeDataString(category)}";
            }

            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                var result = await response.Content.ReadFromJsonAsync<MarketplaceSearchResult>(JsonOptions, cancellationToken);
                return result ?? new MarketplaceSearchResult();
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        // Return mock/fallback data for demo purposes
        return CreateMockSearchResult(query, page, pageSize);
    }

    public async Task<IReadOnlyList<MarketplaceExtension>> GetFeaturedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/featured", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                var result = await response.Content.ReadFromJsonAsync<List<MarketplaceExtension>>(JsonOptions, cancellationToken);
                return result ?? new List<MarketplaceExtension>();
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        return CreateMockFeaturedExtensions();
    }

    public async Task<IReadOnlyList<MarketplaceExtension>> GetPopularAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/popular?count={count}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                var result = await response.Content.ReadFromJsonAsync<List<MarketplaceExtension>>(JsonOptions, cancellationToken);
                return result ?? new List<MarketplaceExtension>();
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        return CreateMockPopularExtensions(count);
    }

    public async Task<IReadOnlyList<MarketplaceExtension>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/recent?count={count}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                var result = await response.Content.ReadFromJsonAsync<List<MarketplaceExtension>>(JsonOptions, cancellationToken);
                return result ?? new List<MarketplaceExtension>();
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        return new List<MarketplaceExtension>();
    }

    public async Task<MarketplaceExtension?> GetExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/extensions/{Uri.EscapeDataString(extensionId)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                return await response.Content.ReadFromJsonAsync<MarketplaceExtension>(JsonOptions, cancellationToken);
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        return null;
    }

    public async Task<IReadOnlyList<ExtensionVersion>> GetVersionsAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/extensions/{Uri.EscapeDataString(extensionId)}/versions", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                var result = await response.Content.ReadFromJsonAsync<List<ExtensionVersion>>(JsonOptions, cancellationToken);
                return result ?? new List<ExtensionVersion>();
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        return new List<ExtensionVersion>();
    }

    public async Task<bool> DownloadAsync(
        string extensionId,
        string? version,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/extensions/{Uri.EscapeDataString(extensionId)}/download";
            if (!string.IsNullOrEmpty(version))
            {
                url += $"?version={Uri.EscapeDataString(version)}";
            }

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadProgress = new DownloadProgress { TotalBytes = totalBytes };

            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = File.Create(destinationPath);

            var buffer = new byte[8192];
            var bytesRead = 0;
            var totalRead = 0L;
            var lastReportTime = DateTime.UtcNow;
            var lastReportBytes = 0L;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;

                var now = DateTime.UtcNow;
                var elapsed = (now - lastReportTime).TotalSeconds;
                if (elapsed >= 0.1 && progress != null)
                {
                    downloadProgress.BytesDownloaded = totalRead;
                    downloadProgress.BytesPerSecond = (long)((totalRead - lastReportBytes) / elapsed);
                    progress.Report(downloadProgress);
                    lastReportTime = now;
                    lastReportBytes = totalRead;
                }
            }

            SetAvailability(true);
            return true;
        }
        catch (Exception)
        {
            SetAvailability(false);
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/categories", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                SetAvailability(true);
                var result = await response.Content.ReadFromJsonAsync<List<string>>(JsonOptions, cancellationToken);
                return result ?? GetDefaultCategories();
            }
        }
        catch (Exception)
        {
            SetAvailability(false);
        }

        return GetDefaultCategories();
    }

    public async Task<bool> ReportExtensionAsync(string extensionId, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = JsonContent.Create(new { extensionId, reason }, options: JsonOptions);
            var response = await _httpClient.PostAsync("/report", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }

    #region Private Methods

    private void SetAvailability(bool available)
    {
        if (_isAvailable != available)
        {
            _isAvailable = available;
            AvailabilityChanged?.Invoke(this, available);
        }
    }

    private static List<string> GetDefaultCategories()
    {
        return new List<string>
        {
            "Programming Languages",
            "Snippets",
            "Themes",
            "Debuggers",
            "Formatters",
            "Linters",
            "Keymaps",
            "Language Packs",
            "Data Science",
            "Machine Learning",
            "Visualization",
            "Testing",
            "SCM Providers",
            "Extension Packs",
            "Other"
        };
    }

    private static MarketplaceSearchResult CreateMockSearchResult(string query, int page, int pageSize)
    {
        return new MarketplaceSearchResult
        {
            Extensions = new List<MarketplaceExtension>(),
            TotalCount = 0,
            Page = page,
            PageSize = pageSize
        };
    }

    private static List<MarketplaceExtension> CreateMockFeaturedExtensions()
    {
        return new List<MarketplaceExtension>
        {
            new MarketplaceExtension
            {
                Id = "vgs.basiclang",
                DisplayName = "BasicLang Support",
                ShortDescription = "Full language support for BasicLang",
                Publisher = "visualgamestudio",
                PublisherDisplayName = "Visual Game Studio",
                Version = "1.0.0",
                Categories = new List<string> { "Programming Languages" },
                InstallCount = 10000,
                Rating = 4.8,
                RatingCount = 250,
                IsVerifiedPublisher = true
            },
            new MarketplaceExtension
            {
                Id = "vgs.dark-theme",
                DisplayName = "VGS Dark Theme",
                ShortDescription = "A beautiful dark theme for Visual Game Studio",
                Publisher = "visualgamestudio",
                PublisherDisplayName = "Visual Game Studio",
                Version = "1.0.0",
                Categories = new List<string> { "Themes" },
                InstallCount = 5000,
                Rating = 4.5,
                RatingCount = 100,
                IsVerifiedPublisher = true
            }
        };
    }

    private static List<MarketplaceExtension> CreateMockPopularExtensions(int count)
    {
        var extensions = new List<MarketplaceExtension>
        {
            new MarketplaceExtension
            {
                Id = "vgs.basiclang",
                DisplayName = "BasicLang Support",
                ShortDescription = "Full language support for BasicLang",
                Publisher = "visualgamestudio",
                PublisherDisplayName = "Visual Game Studio",
                Version = "1.0.0",
                Categories = new List<string> { "Programming Languages" },
                InstallCount = 10000,
                Rating = 4.8,
                RatingCount = 250,
                IsVerifiedPublisher = true
            },
            new MarketplaceExtension
            {
                Id = "vgs.gamedev-snippets",
                DisplayName = "Game Dev Snippets",
                ShortDescription = "Code snippets for game development",
                Publisher = "visualgamestudio",
                PublisherDisplayName = "Visual Game Studio",
                Version = "1.0.0",
                Categories = new List<string> { "Snippets" },
                InstallCount = 7500,
                Rating = 4.6,
                RatingCount = 180,
                IsVerifiedPublisher = true
            }
        };

        return extensions.Take(count).ToList();
    }

    #endregion
}
