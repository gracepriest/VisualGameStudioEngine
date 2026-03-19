using System.Text.Json;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Persists and manages the list of recently opened projects.
/// Thread-safe with a lock object. Saves to ~/.vgs/recentProjects.json.
/// Pinned projects always sort to the top, then by LastOpened descending.
/// On load, entries whose files no longer exist are automatically pruned.
/// </summary>
public class RecentProjectsService : IRecentProjectsService
{
    private readonly string _recentFilePath;
    private readonly object _lock = new();
    private List<RecentProjectEntry> _entries = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int MaxRecentProjects { get; set; } = 20;

    public event EventHandler? RecentProjectsChanged;

    // ── Legacy compatibility ────────────────────────────────────────
    // The old API surface is kept so WelcomeDocumentViewModel (which
    // references RecentProjectsService directly) still compiles.

    /// <summary>Legacy: returns the internal entries as the old RecentProject type.</summary>
    public IReadOnlyList<RecentProject> RecentProjects
    {
        get
        {
            lock (_lock)
            {
                return _entries
                    .OrderByDescending(e => e.IsPinned)
                    .ThenByDescending(e => e.LastOpened)
                    .Select(e => new RecentProject
                    {
                        Name = e.Name,
                        FilePath = e.Path,
                        LastOpened = e.LastOpened
                    })
                    .ToList()
                    .AsReadOnly();
            }
        }
    }

    public async Task RemoveRecentProjectAsync(string filePath)
    {
        RemoveRecentProject(filePath);
        await Task.CompletedTask;
    }

    public async Task ClearAsync()
    {
        ClearRecentProjects();
        await Task.CompletedTask;
    }

    public async Task AddRecentProjectAsync(string name, string filePath)
    {
        AddRecentProject(filePath, name);
        await Task.CompletedTask;
    }

    // ── End legacy ──────────────────────────────────────────────────

    public RecentProjectsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VisualGameStudio");

        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        _recentFilePath = Path.Combine(appDataPath, "recentProjects.json");

        // Also check legacy path and migrate if needed
        var legacyPath = Path.Combine(appDataPath, "recent.json");
        if (!File.Exists(_recentFilePath) && File.Exists(legacyPath))
        {
            try { File.Copy(legacyPath, _recentFilePath); }
            catch { /* ignore migration errors */ }
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_recentFilePath))
            {
                var json = await File.ReadAllTextAsync(_recentFilePath);
                var data = JsonSerializer.Deserialize<RecentProjectsData>(json, JsonOptions);

                lock (_lock)
                {
                    if (data?.Projects != null)
                    {
                        // Prune entries whose files no longer exist
                        _entries = data.Projects
                            .Where(p => File.Exists(p.Path))
                            .ToList();
                    }
                    else
                    {
                        _entries = new List<RecentProjectEntry>();
                    }
                }

                // Save back pruned list
                await SaveAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load recent projects: {ex.Message}");
            lock (_lock)
            {
                _entries = new List<RecentProjectEntry>();
            }
        }
    }

    public IReadOnlyList<RecentProjectInfo> GetRecentProjects()
    {
        lock (_lock)
        {
            return _entries
                .OrderByDescending(e => e.IsPinned)
                .ThenByDescending(e => e.LastOpened)
                .Select(e => new RecentProjectInfo
                {
                    Path = e.Path,
                    Name = e.Name,
                    LastOpened = e.LastOpened,
                    IsPinned = e.IsPinned,
                    IconPath = e.IconPath
                })
                .ToList()
                .AsReadOnly();
        }
    }

    public void AddRecentProject(string path, string name, string? iconPath = null)
    {
        lock (_lock)
        {
            // Remove existing entry for same path
            _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

            _entries.Insert(0, new RecentProjectEntry
            {
                Path = path,
                Name = name,
                LastOpened = DateTime.Now,
                IsPinned = false,
                IconPath = iconPath
            });

            TrimEntries();
        }

        _ = SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveRecentProject(string path)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        _ = SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearRecentProjects()
    {
        lock (_lock)
        {
            _entries.Clear();
        }

        _ = SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PinProject(string path)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.IsPinned = true;
            }
        }

        _ = SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UnpinProject(string path)
    {
        lock (_lock)
        {
            var entry = _entries.FirstOrDefault(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                entry.IsPinned = false;
            }
        }

        _ = SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TrimEntries()
    {
        // Pinned entries don't count toward the limit
        var unpinned = _entries.Where(e => !e.IsPinned).ToList();
        if (unpinned.Count > MaxRecentProjects)
        {
            var toRemove = unpinned
                .OrderByDescending(e => e.LastOpened)
                .Skip(MaxRecentProjects)
                .ToList();

            foreach (var entry in toRemove)
            {
                _entries.Remove(entry);
            }
        }
    }

    private async Task SaveAsync()
    {
        List<RecentProjectEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<RecentProjectEntry>(_entries);
        }

        try
        {
            var data = new RecentProjectsData { Projects = snapshot };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(_recentFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save recent projects: {ex.Message}");
        }
    }

    private class RecentProjectsData
    {
        public List<RecentProjectEntry>? Projects { get; set; }
    }

    private class RecentProjectEntry
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime LastOpened { get; set; }
        public bool IsPinned { get; set; }
        public string? IconPath { get; set; }
    }
}

/// <summary>
/// Legacy model kept for backward compatibility with WelcomeDocumentViewModel.
/// </summary>
public class RecentProject
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime LastOpened { get; set; }
}
