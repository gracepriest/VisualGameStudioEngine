using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Manages multi-root workspaces, allowing multiple folders to be open in a single IDE window.
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Gets the currently active workspace, or null if no workspace is open.
    /// </summary>
    Workspace? CurrentWorkspace { get; }

    /// <summary>
    /// Gets the folders in the current workspace.
    /// </summary>
    IReadOnlyList<WorkspaceFolder> Folders { get; }

    /// <summary>
    /// Gets whether the current workspace has been modified since last save.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Opens a workspace from a .vgs-workspace file.
    /// </summary>
    Task OpenWorkspaceAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current workspace to disk. If filePath is null, saves to the existing path.
    /// </summary>
    Task SaveWorkspaceAsync(string? filePath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the current workspace.
    /// </summary>
    Task CloseWorkspaceAsync();

    /// <summary>
    /// Adds a folder to the current workspace. Creates a new workspace if none is open.
    /// </summary>
    void AddFolder(string folderPath, string? displayName = null);

    /// <summary>
    /// Removes a folder from the current workspace.
    /// </summary>
    void RemoveFolder(string folderPath);

    /// <summary>
    /// Creates a new empty workspace (untitled).
    /// </summary>
    void CreateNewWorkspace();

    /// <summary>
    /// Raised when the workspace changes (folders added/removed, workspace opened/closed).
    /// </summary>
    event EventHandler? WorkspaceChanged;
}
