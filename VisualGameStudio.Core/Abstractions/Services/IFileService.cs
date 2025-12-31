namespace VisualGameStudio.Core.Abstractions.Services;

public interface IFileService
{
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);
    Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string path);
    Task<IEnumerable<string>> GetFilesAsync(string directory, string pattern, bool recursive = false);
    Task<IEnumerable<string>> GetDirectoriesAsync(string directory);
    Task CreateDirectoryAsync(string path);
    Task DeleteFileAsync(string path);
    Task<DateTime> GetLastWriteTimeAsync(string path);

    void WatchDirectory(string path, Action<string, FileChangeType> onChange);
    void StopWatching(string path);
}

public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}
