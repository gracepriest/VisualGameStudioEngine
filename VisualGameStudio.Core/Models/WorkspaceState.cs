namespace VisualGameStudio.Core.Models;

/// <summary>
/// Persisted per-project IDE state: the dock layout tree plus the set of open
/// documents. Stored user-locally, keyed by project path (VS Code's model), so a
/// project reopens the way the user left it. See
/// docs/superpowers/specs/2026-07-08-per-project-layout-persistence-design.md.
/// </summary>
public class WorkspaceStateModel
{
    /// <summary>
    /// Schema version. Bumped when the layout tree shape changes incompatibly; a
    /// mismatch causes the store to discard the old state and fall back to defaults.
    /// </summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>Workbench width when the state was captured (reserved for rescaling).</summary>
    public double SavedAtWidth { get; set; }

    /// <summary>Workbench height when the state was captured (reserved for rescaling).</summary>
    public double SavedAtHeight { get; set; }

    /// <summary>The serialized dock layout tree, or null to use the default layout.</summary>
    public DockNode? DockLayout { get; set; }

    /// <summary>Documents that were open, in tab order.</summary>
    public List<OpenDocumentState> OpenDocuments { get; set; } = new();

    /// <summary>Absolute path of the document that was active, or null.</summary>
    public string? ActiveDocumentPath { get; set; }
}

/// <summary>An open document and where its caret was.</summary>
public class OpenDocumentState
{
    public string Path { get; set; } = "";
    public int CaretLine { get; set; } = 1;
    public int CaretColumn { get; set; } = 1;
}

/// <summary>
/// Serializable node in the dock layout tree. A faithful but UI-framework-agnostic
/// mirror of the Dock.Avalonia tree (Core must not depend on Avalonia), converted to
/// and from live dockables by the Shell's DockLayoutSerializer.
/// </summary>
public class DockNode
{
    public DockNodeKind Kind { get; set; }

    /// <summary>Stable dockable id (e.g. "SolutionExplorer", "BottomLeftTools", "DocumentDock").</summary>
    public string? Id { get; set; }

    public string? Title { get; set; }

    /// <summary>Split orientation; only meaningful for <see cref="DockNodeKind.Proportional"/>.</summary>
    public DockOrientation Orientation { get; set; }

    /// <summary>Size share within the parent split, or null for auto (Dock's NaN).</summary>
    public double? Proportion { get; set; }

    /// <summary>Id of the active child (selected tab), when applicable.</summary>
    public string? ActiveDockableId { get; set; }

    public List<DockNode> Children { get; set; } = new();
}

public enum DockNodeKind
{
    Root,
    Proportional,
    ToolDock,
    DocumentDock,
    Splitter,
    Tool
}

public enum DockOrientation
{
    Horizontal,
    Vertical
}
