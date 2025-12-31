using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

public class FileService : IFileService, IDisposable
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly object _lock = new();

    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    public Task<bool> FileExistsAsync(string path)
    {
        return Task.FromResult(File.Exists(path));
    }

    public Task<IEnumerable<string>> GetFilesAsync(string directory, string pattern, bool recursive = false)
    {
        if (!Directory.Exists(directory))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(directory, pattern, searchOption);
        return Task.FromResult(files.AsEnumerable());
    }

    public Task<IEnumerable<string>> GetDirectoriesAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Task.FromResult(Enumerable.Empty<string>());
        }

        var dirs = Directory.GetDirectories(directory);
        return Task.FromResult(dirs.AsEnumerable());
    }

    public Task CreateDirectoryAsync(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
        return Task.CompletedTask;
    }

    public Task DeleteFileAsync(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public Task<DateTime> GetLastWriteTimeAsync(string path)
    {
        if (File.Exists(path))
        {
            return Task.FromResult(File.GetLastWriteTime(path));
        }
        return Task.FromResult(DateTime.MinValue);
    }

    public void WatchDirectory(string path, Action<string, FileChangeType> onChange)
    {
        lock (_lock)
        {
            if (_watchers.ContainsKey(path))
            {
                return;
            }

            var watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += (s, e) => onChange(e.FullPath, FileChangeType.Created);
            watcher.Changed += (s, e) => onChange(e.FullPath, FileChangeType.Modified);
            watcher.Deleted += (s, e) => onChange(e.FullPath, FileChangeType.Deleted);
            watcher.Renamed += (s, e) => onChange(e.FullPath, FileChangeType.Renamed);

            _watchers[path] = watcher;
        }
    }

    public void StopWatching(string path)
    {
        lock (_lock)
        {
            if (_watchers.TryGetValue(path, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watchers.Remove(path);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var watcher in _watchers.Values)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            _watchers.Clear();
        }
    }
}
