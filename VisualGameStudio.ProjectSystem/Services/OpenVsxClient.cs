using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// API client for the Open VSX Registry (https://open-vsx.org).
/// Enables searching, browsing, and downloading VS Code extensions.
/// </summary>
public class OpenVsxClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;
    private bool _disposed;

    /// <summary>
    /// Base URL for the Open VSX API.
    /// </summary>
    public string BaseUrl { get; }

    public OpenVsxClient(string? baseUrl = null)
    {
        BaseUrl = baseUrl ?? "https://open-vsx.org";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VisualGameStudio/1.0");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Searches for extensions on the Open VSX Registry.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="category">Optional category filter (e.g., "Themes", "Programming Languages").</param>
    /// <param name="sortBy">Sort criteria: "relevance", "downloadCount", "averageRating", "timestamp".</param>
    /// <param name="sortOrder">Sort direction: "asc" or "desc".</param>
    /// <param name="limit">Maximum number of results (default 20, max 100).</param>
    /// <param name="offset">Offset for pagination.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OpenVsxSearchResult> SearchAsync(
        string query,
        string? category = null,
        string sortBy = "relevance",
        string sortOrder = "desc",
        int limit = 20,
        int offset = 0,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"/api/-/search?query={Uri.EscapeDataString(query)}&size={limit}&offset={offset}&sortBy={sortBy}&sortOrder={sortOrder}";
            if (!string.IsNullOrEmpty(category))
            {
                url += $"&category={Uri.EscapeDataString(category)}";
            }

            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenVsxSearchResult>(JsonOptions, ct);
            return result ?? new OpenVsxSearchResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenVSX search failed: {ex.Message}");
            return new OpenVsxSearchResult();
        }
    }

    /// <summary>
    /// Gets detailed information about a specific extension.
    /// </summary>
    /// <param name="publisher">Publisher namespace (e.g., "redhat").</param>
    /// <param name="name">Extension name (e.g., "java").</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OpenVsxExtensionDetail?> GetExtensionAsync(
        string publisher,
        string name,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/{Uri.EscapeDataString(publisher)}/{Uri.EscapeDataString(name)}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<OpenVsxExtensionDetail>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenVSX get extension failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets information about a specific version of an extension.
    /// </summary>
    /// <param name="publisher">Publisher namespace.</param>
    /// <param name="name">Extension name.</param>
    /// <param name="version">Specific version string (e.g., "1.2.3").</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OpenVsxExtensionDetail?> GetExtensionVersionAsync(
        string publisher,
        string name,
        string version,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/{Uri.EscapeDataString(publisher)}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<OpenVsxExtensionDetail>(JsonOptions, ct);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OpenVSX get version failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets popular extensions from the registry.
    /// </summary>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OpenVsxSearchResult> GetPopularAsync(int limit = 20, CancellationToken ct = default)
    {
        return await SearchAsync("", sortBy: "downloadCount", sortOrder: "desc", limit: limit, ct: ct);
    }

    /// <summary>
    /// Gets recently updated extensions from the registry.
    /// </summary>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OpenVsxSearchResult> GetRecentAsync(int limit = 20, CancellationToken ct = default)
    {
        return await SearchAsync("", sortBy: "timestamp", sortOrder: "desc", limit: limit, ct: ct);
    }

    /// <summary>
    /// Gets top-rated extensions from the registry.
    /// </summary>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<OpenVsxSearchResult> GetTopRatedAsync(int limit = 20, CancellationToken ct = default)
    {
        return await SearchAsync("", sortBy: "averageRating", sortOrder: "desc", limit: limit, ct: ct);
    }

    /// <summary>
    /// Downloads a VSIX file for an extension. Returns the byte stream.
    /// </summary>
    /// <param name="downloadUrl">Direct download URL from the extension detail's Files dictionary.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<Stream> DownloadVsixAsync(string downloadUrl, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(ct);
    }

    /// <summary>
    /// Downloads a VSIX file to a specified path on disk, with optional progress reporting.
    /// </summary>
    /// <param name="downloadUrl">Direct download URL from the extension detail's Files dictionary.</param>
    /// <param name="destinationPath">Local path to save the .vsix file.</param>
    /// <param name="progress">Optional progress reporter (bytes downloaded, total bytes).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task DownloadVsixToFileAsync(
        string downloadUrl,
        string destinationPath,
        IProgress<(long bytesDownloaded, long totalBytes)>? progress = null,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        using var fileStream = File.Create(destinationPath);

        var buffer = new byte[81920]; // 80KB buffer
        var totalRead = 0L;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;
            progress?.Report((totalRead, totalBytes));
        }
    }

    /// <summary>
    /// Resolves the VSIX download URL for an extension by publisher/name/version.
    /// If version is null or "latest", gets the latest version.
    /// </summary>
    /// <param name="publisher">Publisher namespace.</param>
    /// <param name="name">Extension name.</param>
    /// <param name="version">Specific version or null for latest.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The download URL, or null if the extension was not found.</returns>
    public async Task<string?> ResolveDownloadUrlAsync(
        string publisher,
        string name,
        string? version = null,
        CancellationToken ct = default)
    {
        OpenVsxExtensionDetail? detail;

        if (string.IsNullOrEmpty(version) || version.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            detail = await GetExtensionAsync(publisher, name, ct);
        }
        else
        {
            detail = await GetExtensionVersionAsync(publisher, name, version, ct);
        }

        if (detail?.Files == null)
            return null;

        // The "download" key contains the VSIX download URL
        if (detail.Files.TryGetValue("download", out var downloadUrl))
            return downloadUrl;

        return null;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}

#region Open VSX API Response Models

/// <summary>
/// Search result from the Open VSX API.
/// </summary>
public class OpenVsxSearchResult
{
    /// <summary>
    /// Total number of matching extensions.
    /// </summary>
    [JsonPropertyName("totalSize")]
    public int TotalSize { get; set; }

    /// <summary>
    /// Offset for pagination.
    /// </summary>
    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    /// <summary>
    /// List of matching extensions.
    /// </summary>
    [JsonPropertyName("extensions")]
    public List<OpenVsxSearchExtension> Extensions { get; set; } = new();
}

/// <summary>
/// An extension entry in search results.
/// </summary>
public class OpenVsxSearchExtension
{
    /// <summary>
    /// Extension URL on Open VSX.
    /// </summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>
    /// File download URLs.
    /// </summary>
    [JsonPropertyName("files")]
    public Dictionary<string, string> Files { get; set; } = new();

    /// <summary>
    /// Extension name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Publisher namespace.
    /// </summary>
    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    /// <summary>
    /// Current version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// Display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Short description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Timestamp of last update.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Total download count.
    /// </summary>
    [JsonPropertyName("downloadCount")]
    public long DownloadCount { get; set; }

    /// <summary>
    /// Average rating (0-5).
    /// </summary>
    [JsonPropertyName("averageRating")]
    public double? AverageRating { get; set; }

    /// <summary>
    /// Extension ID in publisher.name format.
    /// </summary>
    public string Id => $"{Namespace}.{Name}";
}

/// <summary>
/// Detailed extension information from Open VSX.
/// </summary>
public class OpenVsxExtensionDetail
{
    /// <summary>
    /// Publisher namespace.
    /// </summary>
    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    /// <summary>
    /// Namespace details.
    /// </summary>
    [JsonPropertyName("namespaceAccess")]
    public string? NamespaceAccess { get; set; }

    /// <summary>
    /// Extension name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Display name.
    /// </summary>
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Current version.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>
    /// Pre-release flag.
    /// </summary>
    [JsonPropertyName("preRelease")]
    public bool PreRelease { get; set; }

    /// <summary>
    /// Verified publisher.
    /// </summary>
    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    /// <summary>
    /// List of all published versions.
    /// </summary>
    [JsonPropertyName("allVersions")]
    public Dictionary<string, string>? AllVersions { get; set; }

    /// <summary>
    /// File URLs (keys: download, manifest, icon, readme, changelog, license).
    /// The "download" key contains the .vsix download URL.
    /// </summary>
    [JsonPropertyName("files")]
    public Dictionary<string, string>? Files { get; set; }

    /// <summary>
    /// Categories (e.g., "Themes", "Programming Languages").
    /// </summary>
    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }

    /// <summary>
    /// Tags/keywords.
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>
    /// License identifier (e.g., "MIT").
    /// </summary>
    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Homepage URL.
    /// </summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    /// <summary>
    /// Repository URL.
    /// </summary>
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    /// <summary>
    /// Bug tracker URL.
    /// </summary>
    [JsonPropertyName("bugs")]
    public string? Bugs { get; set; }

    /// <summary>
    /// Download count.
    /// </summary>
    [JsonPropertyName("downloadCount")]
    public long DownloadCount { get; set; }

    /// <summary>
    /// Average rating.
    /// </summary>
    [JsonPropertyName("averageRating")]
    public double? AverageRating { get; set; }

    /// <summary>
    /// Review count.
    /// </summary>
    [JsonPropertyName("reviewCount")]
    public int ReviewCount { get; set; }

    /// <summary>
    /// VS Code engine version required.
    /// </summary>
    [JsonPropertyName("engines")]
    public Dictionary<string, string>? Engines { get; set; }

    /// <summary>
    /// Extension dependencies (publisher.name format).
    /// </summary>
    [JsonPropertyName("dependencies")]
    public List<OpenVsxExtensionRef>? Dependencies { get; set; }

    /// <summary>
    /// Bundled extensions.
    /// </summary>
    [JsonPropertyName("bundledExtensions")]
    public List<OpenVsxExtensionRef>? BundledExtensions { get; set; }

    /// <summary>
    /// Publication timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>
    /// Extension ID in publisher.name format.
    /// </summary>
    public string Id => $"{Namespace}.{Name}";
}

/// <summary>
/// Reference to another extension (dependency or bundled).
/// </summary>
public class OpenVsxExtensionRef
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("extension")]
    public string Extension { get; set; } = "";
}

#endregion
