namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Auto-save modes matching VS Code behavior.
/// </summary>
public enum AutoSaveMode
{
    /// <summary>No auto-save.</summary>
    Off,
    /// <summary>Save after a configurable delay since last edit.</summary>
    AfterDelay,
    /// <summary>Save when the editor loses focus.</summary>
    OnFocusChange,
    /// <summary>Save when the application window loses focus.</summary>
    OnWindowChange
}

/// <summary>
/// Service for automatic file saving with configurable modes and delay.
/// </summary>
public interface IAutoSaveService : IDisposable
{
    /// <summary>
    /// Gets or sets the current auto-save mode.
    /// </summary>
    AutoSaveMode Mode { get; set; }

    /// <summary>
    /// Gets or sets the delay in milliseconds for AfterDelay mode.
    /// </summary>
    int DelayMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets whether to skip auto-save for files with syntax errors.
    /// </summary>
    bool SkipOnErrors { get; set; }

    /// <summary>
    /// Notifies the service that a document was edited. Resets the per-document timer.
    /// </summary>
    /// <param name="filePath">The file path of the edited document.</param>
    void NotifyDocumentChanged(string filePath);

    /// <summary>
    /// Notifies the service that an editor lost focus (for OnFocusChange mode).
    /// </summary>
    /// <param name="filePath">The file path of the document whose editor lost focus.</param>
    void NotifyEditorLostFocus(string filePath);

    /// <summary>
    /// Notifies the service that the application window lost focus (for OnWindowChange mode).
    /// </summary>
    void NotifyWindowLostFocus();

    /// <summary>
    /// Registers a document for auto-save tracking.
    /// </summary>
    /// <param name="filePath">The file path.</param>
    /// <param name="saveCallback">Async callback to save the document.</param>
    /// <param name="isDirtyFunc">Function to check if the document has unsaved changes.</param>
    /// <param name="isReadOnlyFunc">Function to check if the file is readonly.</param>
    void RegisterDocument(string filePath, Func<Task<bool>> saveCallback, Func<bool> isDirtyFunc, Func<bool> isReadOnlyFunc);

    /// <summary>
    /// Unregisters a document from auto-save tracking.
    /// </summary>
    /// <param name="filePath">The file path to unregister.</param>
    void UnregisterDocument(string filePath);

    /// <summary>
    /// Raised when a document is auto-saved.
    /// </summary>
    event EventHandler<AutoSaveEventArgs>? DocumentAutoSaved;
}

/// <summary>
/// Event args for auto-save events.
/// </summary>
public class AutoSaveEventArgs : EventArgs
{
    /// <summary>The file path that was auto-saved.</summary>
    public string FilePath { get; }

    /// <summary>Whether the save succeeded.</summary>
    public bool Success { get; }

    public AutoSaveEventArgs(string filePath, bool success)
    {
        FilePath = filePath;
        Success = success;
    }
}
