namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Hot exit modes.
/// </summary>
public enum HotExitMode
{
    /// <summary>Hot exit disabled.</summary>
    Off,
    /// <summary>Save backups on application exit.</summary>
    OnExit,
    /// <summary>Save backups on exit and window close.</summary>
    OnExitAndWindowClose
}

/// <summary>
/// Service that preserves unsaved work across IDE restarts by backing up
/// dirty document content to a persistent backup location.
/// </summary>
public interface IHotExitService
{
    /// <summary>
    /// Gets or sets the hot exit mode.
    /// </summary>
    HotExitMode Mode { get; set; }

    /// <summary>
    /// Saves all unsaved document backups. Call on IDE exit.
    /// </summary>
    /// <param name="documents">The documents to back up, with their state.</param>
    Task SaveBackupsAsync(IEnumerable<HotExitDocumentState> documents);

    /// <summary>
    /// Checks if there are any backup files from a previous session.
    /// </summary>
    /// <returns>True if backups exist.</returns>
    Task<bool> HasBackupsAsync();

    /// <summary>
    /// Gets all backup entries from a previous session.
    /// </summary>
    /// <returns>The list of backed-up document states.</returns>
    Task<IReadOnlyList<HotExitDocumentState>> GetBackupsAsync();

    /// <summary>
    /// Cleans up backup for a specific file (e.g., after it has been saved normally).
    /// </summary>
    /// <param name="filePath">The original file path.</param>
    Task CleanupBackupAsync(string filePath);

    /// <summary>
    /// Cleans up all backups.
    /// </summary>
    Task CleanupAllBackupsAsync();
}

/// <summary>
/// Represents the state of a document for hot exit backup/restore.
/// </summary>
public class HotExitDocumentState
{
    /// <summary>The original file path.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>The unsaved content.</summary>
    public string Content { get; set; } = "";

    /// <summary>The caret line position.</summary>
    public int CaretLine { get; set; } = 1;

    /// <summary>The caret column position.</summary>
    public int CaretColumn { get; set; } = 1;

    /// <summary>The vertical scroll offset.</summary>
    public double ScrollOffset { get; set; }

    /// <summary>Whether the document had unsaved changes.</summary>
    public bool IsDirty { get; set; }
}
