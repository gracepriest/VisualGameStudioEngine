using System.Text.Json;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Persists and manages the list of recently opened projects.
/// Thread-safe with a lock object. Saves to %APPDATA%\VisualGameStudio\recentProjects.json
/// (or an injected storage directory, used by tests to avoid touching the real store).
/// Pinned projects always sort to the top, then by LastOpened descending.
/// Entries whose files are currently missing are hidden from the getters but kept
/// in storage, so projects on temporarily unavailable paths (OneDrive offline,
/// unplugged drives) reappear instead of being permanently erased.
/// </summary>
public class RecentProjectsService : IRecentProjectsService
{
    private readonly string _recentFilePath;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private List<RecentProjectEntry> _entries = new();
    private bool _loaded;

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
            return SnapshotExisting()
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

    public async Task RemoveRecentProjectAsync(string filePath)
    {
        RemoveRecentProjectCore(filePath);
        await SaveAsync();
    }

    public async Task ClearAsync()
    {
        ClearRecentProjectsCore();
        await SaveAsync();
    }

    public async Task AddRecentProjectAsync(string name, string filePath)
    {
        AddRecentProjectCore(filePath, name, null);
        await SaveAsync();
    }

    // ── End legacy ──────────────────────────────────────────────────

    public RecentProjectsService() : this(null)
    {
    }

    public RecentProjectsService(string? storageDirectory)
    {
        var appDataPath = storageDirectory ?? Path.Combine(
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
        var fromDisk = await ReadStoreAsync();

        bool hadPreLoadEntries;
        lock (_lock)
        {
            hadPreLoadEntries = _entries.Count > 0;
            if (fromDisk != null)
            {
                MergeLocked(fromDisk);
            }
            _loaded = true;
        }

        // The shell may have opened a project before load finished; persist the
        // merged union so those early entries survive.
        if (hadPreLoadEntries)
        {
            await SaveAsync();
        }

        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<List<RecentProjectEntry>?> ReadStoreAsync()
    {
        try
        {
            if (!File.Exists(_recentFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_recentFilePath);
            var data = JsonSerializer.Deserialize<RecentProjectsData>(json, JsonOptions);
            return data?.Projects;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load recent projects: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Merges disk entries into the in-memory list. In-memory entries win on
    /// path collisions (they are newer than anything persisted). Missing files
    /// are NOT pruned here — they are only hidden by the getters.
    /// </summary>
    private void MergeLocked(List<RecentProjectEntry> fromDisk)
    {
        foreach (var diskEntry in fromDisk)
        {
            if (!_entries.Any(e => e.Path.Equals(diskEntry.Path, StringComparison.OrdinalIgnoreCase)))
            {
                _entries.Add(diskEntry);
            }
        }

        TrimEntries();
    }

    public IReadOnlyList<RecentProjectInfo> GetRecentProjects()
    {
        return SnapshotExisting()
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

    /// <summary>
    /// Ordered snapshot of entries whose files currently exist. Missing files are
    /// hidden, not deleted — File.Exists runs outside the lock on a copy.
    /// </summary>
    private List<RecentProjectEntry> SnapshotExisting()
    {
        List<RecentProjectEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<RecentProjectEntry>(_entries);
        }

        return snapshot
            .Where(e => File.Exists(e.Path))
            .OrderByDescending(e => e.IsPinned)
            .ThenByDescending(e => e.LastOpened)
            .ToList();
    }

    public void AddRecentProject(string path, string name, string? iconPath = null)
    {
        AddRecentProjectCore(path, name, iconPath);
        _ = SaveAsync();
    }

    public void RemoveRecentProject(string path)
    {
        RemoveRecentProjectCore(path);
        _ = SaveAsync();
    }

    public void ClearRecentProjects()
    {
        ClearRecentProjectsCore();
        _ = SaveAsync();
    }

    private void AddRecentProjectCore(string path, string name, string? iconPath)
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

        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveRecentProjectCore(string path)
    {
        lock (_lock)
        {
            _entries.RemoveAll(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        }

        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRecentProjectsCore()
    {
        lock (_lock)
        {
            _entries.Clear();
            // Clear is authoritative: don't let a pre-load save merge the old
            // store back in.
            _loaded = true;
        }

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
        // A save can fire before the initial load (fire-and-forget from an early
        // AddRecentProject). Merge the store in first so the write doesn't wipe
        // the persisted history.
        bool needsMerge;
        lock (_lock)
        {
            needsMerge = !_loaded;
        }

        if (needsMerge)
        {
            var fromDisk = await ReadStoreAsync();
            lock (_lock)
            {
                if (fromDisk != null)
                {
                    MergeLocked(fromDisk);
                }
                _loaded = true;
            }
        }

        List<RecentProjectEntry> snapshot;
        lock (_lock)
        {
            snapshot = new List<RecentProjectEntry>(_entries);
        }

        await _saveGate.WaitAsync();
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
        finally
        {
            _saveGate.Release();
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
