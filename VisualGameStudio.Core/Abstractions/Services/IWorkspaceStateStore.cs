using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// User-local, per-project store for IDE window/session state (dock layout + open
/// documents), keyed by a hash of the project path — VS Code's workspaceStorage model.
/// Nothing is written inside the project folder, so personal layout stays out of git.
/// All operations are best-effort: IO failures and corrupt/incompatible files are
/// swallowed (Load returns null) so persistence never breaks the IDE.
/// </summary>
public interface IWorkspaceStateStore
{
    /// <summary>
    /// Loads saved state for a project, or null if none exists, the file is corrupt,
    /// or its schema version is incompatible.
    /// </summary>
    WorkspaceStateModel? Load(string projectDirectory);

    /// <summary>Saves state for a project (creates the per-project store folder on first write).</summary>
    void Save(string projectDirectory, WorkspaceStateModel state);

    /// <summary>Deletes saved state for a project (used by "Reset Layout").</summary>
    void Clear(string projectDirectory);
}
