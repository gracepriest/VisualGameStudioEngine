namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// The type of external change detected on a file.
/// </summary>
public enum ExternalFileChangeType
{
    /// <summary>The file content was modified externally.</summary>
    Modified,
    /// <summary>The file was deleted externally.</summary>
    Deleted,
    /// <summary>A new file was created in the watched directory.</summary>
    Created
}

/// <summary>
/// Service that monitors open files and project directories for external changes.
/// Provides notifications when files are modified, deleted, or created outside the IDE.
/// </summary>
public interface IFileWatcherService : IDisposable
{
    /// <summary>
    /// Starts watching a specific file for external changes.
    /// </summary>
    /// <param name="filePath">The file path to watch.</param>
    void WatchFile(string filePath);

    /// <summary>
    /// Stops watching a specific file.
    /// </summary>
    /// <param name="filePath">The file path to stop watching.</param>
    void UnwatchFile(string filePath);

    /// <summary>
    /// Starts watching a directory for file system changes (new/deleted files).
    /// </summary>
    /// <param name="directoryPath">The directory to watch.</param>
    void WatchDirectory(string directoryPath);

    /// <summary>
    /// Stops watching a directory.
    /// </summary>
    /// <param name="directoryPath">The directory to stop watching.</param>
    void UnwatchDirectory(string directoryPath);

    /// <summary>
    /// Temporarily suppresses change notifications for a file path.
    /// Call this before the IDE saves a file to avoid self-triggered reload prompts.
    /// </summary>
    /// <param name="filePath">The file path to suppress.</param>
    /// <returns>A disposable that re-enables notifications when disposed.</returns>
    IDisposable SuppressNotifications(string filePath);

    /// <summary>
    /// Raised when an open file is modified externally.
    /// </summary>
    event EventHandler<FileChangedExternallyEventArgs>? FileChangedExternally;

    /// <summary>
    /// Raised when an open file is deleted externally.
    /// </summary>
    event EventHandler<FileDeletedExternallyEventArgs>? FileDeletedExternally;

    /// <summary>
    /// Raised when a new file is created in a watched directory.
    /// </summary>
    event EventHandler<FileCreatedExternallyEventArgs>? FileCreatedExternally;
}

/// <summary>
/// Event args when a file is modified outside the IDE.
/// </summary>
public class FileChangedExternallyEventArgs : EventArgs
{
    public string FilePath { get; }

    public FileChangedExternallyEventArgs(string filePath)
    {
        FilePath = filePath;
    }
}

/// <summary>
/// Event args when a file is deleted outside the IDE.
/// </summary>
public class FileDeletedExternallyEventArgs : EventArgs
{
    public string FilePath { get; }

    public FileDeletedExternallyEventArgs(string filePath)
    {
        FilePath = filePath;
    }
}

/// <summary>
/// Event args when a new file appears in a watched directory.
/// </summary>
public class FileCreatedExternallyEventArgs : EventArgs
{
    public string FilePath { get; }

    public FileCreatedExternallyEventArgs(string filePath)
    {
        FilePath = filePath;
    }
}
