namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for interacting with the extension marketplace.
/// </summary>
public interface IMarketplaceService
{
    /// <summary>
    /// Gets whether the marketplace is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Searches for extensions in the marketplace.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <param name="category">Optional category filter.</param>
    /// <param name="sortBy">Sort order.</param>
    /// <param name="page">Page number (1-based).</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<MarketplaceSearchResult> SearchAsync(
        string query,
        string? category = null,
        MarketplaceSortOrder sortBy = MarketplaceSortOrder.Relevance,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets featured extensions.
    /// </summary>
    Task<IReadOnlyList<MarketplaceExtension>> GetFeaturedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets popular extensions.
    /// </summary>
    Task<IReadOnlyList<MarketplaceExtension>> GetPopularAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recently updated extensions.
    /// </summary>
    Task<IReadOnlyList<MarketplaceExtension>> GetRecentAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets extension details by ID.
    /// </summary>
    Task<MarketplaceExtension?> GetExtensionAsync(string extensionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets extension versions.
    /// </summary>
    Task<IReadOnlyList<ExtensionVersion>> GetVersionsAsync(string extensionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an extension package.
    /// </summary>
    /// <param name="extensionId">Extension ID.</param>
    /// <param name="version">Specific version or null for latest.</param>
    /// <param name="destinationPath">Path to save the package.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> DownloadAsync(
        string extensionId,
        string? version,
        string destinationPath,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available categories.
    /// </summary>
    Task<IReadOnlyList<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports an extension.
    /// </summary>
    Task<bool> ReportExtensionAsync(string extensionId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when the marketplace availability changes.
    /// </summary>
    event EventHandler<bool>? AvailabilityChanged;
}

#region Marketplace Types

/// <summary>
/// Sort order for marketplace search.
/// </summary>
public enum MarketplaceSortOrder
{
    Relevance,
    Installs,
    Rating,
    PublishedDate,
    UpdatedDate,
    Name
}

/// <summary>
/// Result of a marketplace search.
/// </summary>
public class MarketplaceSearchResult
{
    /// <summary>
    /// Gets or sets the extensions found.
    /// </summary>
    public List<MarketplaceExtension> Extensions { get; set; } = new();

    /// <summary>
    /// Gets or sets the total count of results.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets the current page.
    /// </summary>
    public int Page { get; set; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Gets whether there are more pages.
    /// </summary>
    public bool HasMorePages => Page * PageSize < TotalCount;
}

/// <summary>
/// Extension information from the marketplace.
/// </summary>
public class MarketplaceExtension
{
    /// <summary>
    /// Gets or sets the extension ID (publisher.name).
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Gets or sets the short description.
    /// </summary>
    public string ShortDescription { get; set; } = "";

    /// <summary>
    /// Gets or sets the full description (markdown).
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// Gets or sets the publisher name.
    /// </summary>
    public string Publisher { get; set; } = "";

    /// <summary>
    /// Gets or sets the publisher display name.
    /// </summary>
    public string PublisherDisplayName { get; set; } = "";

    /// <summary>
    /// Gets or sets the current version.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Gets or sets the categories.
    /// </summary>
    public List<string> Categories { get; set; } = new();

    /// <summary>
    /// Gets or sets the tags/keywords.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Gets or sets the icon URL.
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Gets or sets the repository URL.
    /// </summary>
    public string? RepositoryUrl { get; set; }

    /// <summary>
    /// Gets or sets the homepage URL.
    /// </summary>
    public string? HomepageUrl { get; set; }

    /// <summary>
    /// Gets or sets the license.
    /// </summary>
    public string? License { get; set; }

    /// <summary>
    /// Gets or sets the install count.
    /// </summary>
    public long InstallCount { get; set; }

    /// <summary>
    /// Gets or sets the average rating (0-5).
    /// </summary>
    public double Rating { get; set; }

    /// <summary>
    /// Gets or sets the rating count.
    /// </summary>
    public int RatingCount { get; set; }

    /// <summary>
    /// Gets or sets the published date.
    /// </summary>
    public DateTime PublishedDate { get; set; }

    /// <summary>
    /// Gets or sets the last updated date.
    /// </summary>
    public DateTime UpdatedDate { get; set; }

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Gets or sets whether this is a verified publisher.
    /// </summary>
    public bool IsVerifiedPublisher { get; set; }

    /// <summary>
    /// Gets or sets the extension statistics.
    /// </summary>
    public ExtensionStatistics? Statistics { get; set; }
}

/// <summary>
/// Extension version information.
/// </summary>
public class ExtensionVersion
{
    /// <summary>
    /// Gets or sets the version string.
    /// </summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// Gets or sets the target platform.
    /// </summary>
    public string? TargetPlatform { get; set; }

    /// <summary>
    /// Gets or sets the release date.
    /// </summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>
    /// Gets or sets whether this is a pre-release version.
    /// </summary>
    public bool IsPreRelease { get; set; }

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Gets or sets the changelog.
    /// </summary>
    public string? Changelog { get; set; }

    /// <summary>
    /// Gets or sets the minimum IDE version required.
    /// </summary>
    public string? MinIdeVersion { get; set; }
}

/// <summary>
/// Extension statistics.
/// </summary>
public class ExtensionStatistics
{
    /// <summary>
    /// Gets or sets the total install count.
    /// </summary>
    public long InstallCount { get; set; }

    /// <summary>
    /// Gets or sets the weekly downloads.
    /// </summary>
    public long WeeklyDownloads { get; set; }

    /// <summary>
    /// Gets or sets the daily downloads.
    /// </summary>
    public long DailyDownloads { get; set; }

    /// <summary>
    /// Gets or sets the trending score.
    /// </summary>
    public double TrendingScore { get; set; }
}

/// <summary>
/// Download progress information.
/// </summary>
public class DownloadProgress
{
    /// <summary>
    /// Gets or sets the bytes downloaded.
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Gets or sets the total bytes.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Gets the progress percentage (0-100).
    /// </summary>
    public double Percentage => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;

    /// <summary>
    /// Gets or sets the download speed in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; set; }
}

#endregion
