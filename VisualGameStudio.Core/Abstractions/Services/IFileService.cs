namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides file system operations for the IDE.
/// </summary>
public interface IFileService
{
    /// <summary>
    /// Reads the contents of a file asynchronously.
    /// </summary>
    /// <param name="path">The path to the file to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The contents of the file as a string.</returns>
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes content to a file asynchronously.
    /// </summary>
    /// <param name="path">The path to the file to write.</param>
    /// <param name="content">The content to write to the file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a file exists at the specified path.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns>True if the file exists; otherwise, false.</returns>
    Task<bool> FileExistsAsync(string path);

    /// <summary>
    /// Gets all files in a directory matching a pattern.
    /// </summary>
    /// <param name="directory">The directory to search.</param>
    /// <param name="pattern">The search pattern (e.g., "*.bas").</param>
    /// <param name="recursive">Whether to search subdirectories.</param>
    /// <returns>A collection of file paths matching the pattern.</returns>
    Task<IEnumerable<string>> GetFilesAsync(string directory, string pattern, bool recursive = false);

    /// <summary>
    /// Gets all subdirectories in a directory.
    /// </summary>
    /// <param name="directory">The directory to search.</param>
    /// <returns>A collection of directory paths.</returns>
    Task<IEnumerable<string>> GetDirectoriesAsync(string directory);

    /// <summary>
    /// Creates a directory at the specified path.
    /// </summary>
    /// <param name="path">The path of the directory to create.</param>
    Task CreateDirectoryAsync(string path);

    /// <summary>
    /// Deletes a file at the specified path.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    Task DeleteFileAsync(string path);

    /// <summary>
    /// Gets the last write time of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>The last write time of the file.</returns>
    Task<DateTime> GetLastWriteTimeAsync(string path);

    /// <summary>
    /// Starts watching a directory for file changes.
    /// </summary>
    /// <param name="path">The directory path to watch.</param>
    /// <param name="onChange">Callback invoked when a file changes.</param>
    void WatchDirectory(string path, Action<string, FileChangeType> onChange);

    /// <summary>
    /// Stops watching a directory for changes.
    /// </summary>
    /// <param name="path">The directory path to stop watching.</param>
    void StopWatching(string path);
}

/// <summary>
/// Types of file system changes that can occur.
/// </summary>
public enum FileChangeType
{
    /// <summary>A new file was created.</summary>
    Created,
    /// <summary>An existing file was modified.</summary>
    Modified,
    /// <summary>A file was deleted.</summary>
    Deleted,
    /// <summary>A file was renamed.</summary>
    Renamed
}
