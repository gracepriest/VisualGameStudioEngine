using System.Text.Json;

namespace VisualGameStudio.ProjectSystem.Services;

public class RecentProjectsService
{
    private readonly string _recentFilePath;
    private List<RecentProject> _recentProjects = new();
    private const int MaxRecentProjects = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<RecentProject> RecentProjects => _recentProjects.AsReadOnly();

    public event EventHandler? RecentProjectsChanged;

    public RecentProjectsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VisualGameStudio");

        if (!Directory.Exists(appDataPath))
        {
            Directory.CreateDirectory(appDataPath);
        }

        _recentFilePath = Path.Combine(appDataPath, "recent.json");
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_recentFilePath))
            {
                var json = await File.ReadAllTextAsync(_recentFilePath);
                var data = JsonSerializer.Deserialize<RecentProjectsData>(json, JsonOptions);
                if (data?.Projects != null)
                {
                    _recentProjects = data.Projects
                        .Where(p => File.Exists(p.FilePath))
                        .Take(MaxRecentProjects)
                        .ToList();
                }
            }
        }
        catch
        {
            _recentProjects = new List<RecentProject>();
        }
    }

    public async Task SaveAsync()
    {
        try
        {
            var data = new RecentProjectsData { Projects = _recentProjects };
            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(_recentFilePath, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    public async Task AddRecentProjectAsync(string name, string filePath)
    {
        // Remove existing entry if present
        _recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));

        // Add to top of list
        _recentProjects.Insert(0, new RecentProject
        {
            Name = name,
            FilePath = filePath,
            LastOpened = DateTime.Now
        });

        // Trim to max size
        if (_recentProjects.Count > MaxRecentProjects)
        {
            _recentProjects = _recentProjects.Take(MaxRecentProjects).ToList();
        }

        await SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RemoveRecentProjectAsync(string filePath)
    {
        _recentProjects.RemoveAll(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
        await SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync()
    {
        _recentProjects.Clear();
        await SaveAsync();
        RecentProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    private class RecentProjectsData
    {
        public List<RecentProject>? Projects { get; set; }
    }
}

public class RecentProject
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime LastOpened { get; set; }
}
