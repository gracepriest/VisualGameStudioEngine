using System.Collections.Concurrent;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Monitors open files for external changes and project directories for new/deleted files.
/// Uses FileSystemWatcher with debouncing to coalesce rapid change events.
/// </summary>
public class FileWatcherService : IFileWatcherService
{
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _fileWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _directoryWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _suppressedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Minimum interval between change notifications for the same file (debounce).
    /// </summary>
    private const int DebounceMilliseconds = 500;

    public event EventHandler<FileChangedExternallyEventArgs>? FileChangedExternally;
    public event EventHandler<FileDeletedExternallyEventArgs>? FileDeletedExternally;
    public event EventHandler<FileCreatedExternallyEventArgs>? FileCreatedExternally;

    public void WatchFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || _disposed) return;

        var normalizedPath = Path.GetFullPath(filePath);
        if (_fileWatchers.ContainsKey(normalizedPath)) return;

        var directory = Path.GetDirectoryName(normalizedPath);
        var fileName = Path.GetFileName(normalizedPath);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName)) return;
        if (!Directory.Exists(directory)) return;

        try
        {
            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += (s, e) => OnFileChanged(normalizedPath);
            watcher.Deleted += (s, e) => OnFileDeleted(normalizedPath);

            _fileWatchers[normalizedPath] = watcher;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileWatcher] Failed to watch {filePath}: {ex.Message}");
        }
    }

    public void UnwatchFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var normalizedPath = Path.GetFullPath(filePath);
        if (_fileWatchers.TryRemove(normalizedPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _lastNotified.TryRemove(normalizedPath, out _);
    }

    public void WatchDirectory(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath) || _disposed) return;
        if (!Directory.Exists(directoryPath)) return;

        var normalizedPath = Path.GetFullPath(directoryPath);
        if (_directoryWatchers.ContainsKey(normalizedPath)) return;

        try
        {
            var watcher = new FileSystemWatcher(normalizedPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            watcher.Created += (s, e) =>
            {
                if (!IsDebounced(e.FullPath) && !IsSuppressed(e.FullPath))
                {
                    FileCreatedExternally?.Invoke(this, new FileCreatedExternallyEventArgs(e.FullPath));
                }
            };

            watcher.Changed += (s, e) =>
            {
                // Directory watcher change events for individual files
                if (!IsDebounced(e.FullPath) && !IsSuppressed(e.FullPath) && File.Exists(e.FullPath))
                {
                    FileChangedExternally?.Invoke(this, new FileChangedExternallyEventArgs(e.FullPath));
                }
            };

            watcher.Deleted += (s, e) =>
            {
                if (!IsDebounced(e.FullPath) && !IsSuppressed(e.FullPath))
                {
                    FileDeletedExternally?.Invoke(this, new FileDeletedExternallyEventArgs(e.FullPath));
                }
            };

            _directoryWatchers[normalizedPath] = watcher;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileWatcher] Failed to watch directory {directoryPath}: {ex.Message}");
        }
    }

    public void UnwatchDirectory(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath)) return;

        var normalizedPath = Path.GetFullPath(directoryPath);
        if (_directoryWatchers.TryRemove(normalizedPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    public IDisposable SuppressNotifications(string filePath)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        _suppressedPaths[normalizedPath] = true;
        return new SuppressionHandle(this, normalizedPath);
    }

    private void OnFileChanged(string filePath)
    {
        if (IsSuppressed(filePath)) return;
        if (IsDebounced(filePath)) return;

        FileChangedExternally?.Invoke(this, new FileChangedExternallyEventArgs(filePath));
    }

    private void OnFileDeleted(string filePath)
    {
        if (IsSuppressed(filePath)) return;
        if (IsDebounced(filePath)) return;

        FileDeletedExternally?.Invoke(this, new FileDeletedExternallyEventArgs(filePath));
    }

    private bool IsDebounced(string filePath)
    {
        var now = DateTime.UtcNow;
        if (_lastNotified.TryGetValue(filePath, out var lastTime))
        {
            if ((now - lastTime).TotalMilliseconds < DebounceMilliseconds)
            {
                return true;
            }
        }

        _lastNotified[filePath] = now;
        return false;
    }

    private bool IsSuppressed(string filePath)
    {
        return _suppressedPaths.ContainsKey(filePath);
    }

    private void Unsuppress(string filePath)
    {
        // Delay unsuppression slightly to allow file system events to settle
        Task.Delay(200).ContinueWith(t =>
        {
            _suppressedPaths.TryRemove(filePath, out bool _removed);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var watcher in _fileWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _fileWatchers.Clear();

        foreach (var watcher in _directoryWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _directoryWatchers.Clear();
    }

    private class SuppressionHandle : IDisposable
    {
        private readonly FileWatcherService _service;
        private readonly string _filePath;
        private bool _disposed;

        public SuppressionHandle(FileWatcherService service, string filePath)
        {
            _service = service;
            _filePath = filePath;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.Unsuppress(_filePath);
        }
    }
}
