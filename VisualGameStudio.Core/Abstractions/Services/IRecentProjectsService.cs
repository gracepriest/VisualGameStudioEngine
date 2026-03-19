namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Manages the list of recently opened projects, including persistence,
/// pinning, and ordering.
/// </summary>
public interface IRecentProjectsService
{
    /// <summary>
    /// Gets the current list of recent projects, with pinned items first,
    /// then sorted by LastOpened descending.
    /// </summary>
    IReadOnlyList<RecentProjectInfo> GetRecentProjects();

    /// <summary>
    /// Adds or updates a project in the recent list. If the project already exists,
    /// it is moved to the top and its LastOpened timestamp is updated.
    /// </summary>
    void AddRecentProject(string path, string name, string? iconPath = null);

    /// <summary>
    /// Removes a project from the recent list.
    /// </summary>
    void RemoveRecentProject(string path);

    /// <summary>
    /// Clears all recent projects (both pinned and unpinned).
    /// </summary>
    void ClearRecentProjects();

    /// <summary>
    /// Pins a project so it always appears at the top of the list.
    /// </summary>
    void PinProject(string path);

    /// <summary>
    /// Unpins a project, returning it to the normal sorted position.
    /// </summary>
    void UnpinProject(string path);

    /// <summary>
    /// Gets or sets the maximum number of recent projects to keep (default 20).
    /// Pinned projects do not count toward this limit.
    /// </summary>
    int MaxRecentProjects { get; set; }

    /// <summary>
    /// Raised when the recent projects list changes (add, remove, pin, clear).
    /// </summary>
    event EventHandler? RecentProjectsChanged;

    /// <summary>
    /// Loads persisted recent projects from disk. Should be called once at startup.
    /// </summary>
    Task LoadAsync();
}

/// <summary>
/// Represents a recently opened project with metadata for display.
/// </summary>
public class RecentProjectInfo
{
    /// <summary>Full path to the project file (.blproj).</summary>
    public string Path { get; set; } = "";

    /// <summary>Display name of the project.</summary>
    public string Name { get; set; } = "";

    /// <summary>When the project was last opened.</summary>
    public DateTime LastOpened { get; set; }

    /// <summary>Whether this project is pinned to the top of the list.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Optional path to a custom icon for the project.</summary>
    public string? IconPath { get; set; }

    /// <summary>Gets the directory containing the project file.</summary>
    public string DirectoryName => System.IO.Path.GetDirectoryName(Path) ?? "";

    /// <summary>Gets whether the project file still exists on disk.</summary>
    public bool Exists => File.Exists(Path);

    /// <summary>Gets a human-friendly relative time string like "2 days ago".</summary>
    public string TimeAgo
    {
        get
        {
            var span = DateTime.Now - LastOpened;

            if (span.TotalSeconds < 60) return "Just now";
            if (span.TotalMinutes < 60)
            {
                var m = (int)span.TotalMinutes;
                return m == 1 ? "1 minute ago" : $"{m} minutes ago";
            }
            if (span.TotalHours < 24)
            {
                var h = (int)span.TotalHours;
                return h == 1 ? "1 hour ago" : $"{h} hours ago";
            }
            if (span.TotalDays < 7)
            {
                var d = (int)span.TotalDays;
                return d == 1 ? "Yesterday" : $"{d} days ago";
            }
            if (span.TotalDays < 30)
            {
                var w = (int)(span.TotalDays / 7);
                return w == 1 ? "1 week ago" : $"{w} weeks ago";
            }
            if (span.TotalDays < 365)
            {
                var mo = (int)(span.TotalDays / 30);
                return mo == 1 ? "1 month ago" : $"{mo} months ago";
            }

            return LastOpened.ToString("MMM d, yyyy");
        }
    }
}
