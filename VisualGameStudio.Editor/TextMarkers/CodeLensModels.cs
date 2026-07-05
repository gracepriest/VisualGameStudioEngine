namespace VisualGameStudio.Editor.TextMarkers;

// CodeLens data types shared by the CodeLens pipeline:
// CodeLensManager (data) -> CodeLensElementGenerator (rendering) -> Shell (click handling).
//
// Note: an older IBackgroundRenderer-based render path (CodeLensRenderer) that drew lens
// text ABOVE the target line was removed — it was never wired into the editor and competed
// with the element-generator path for the same data. The element generator in
// Rendering/CodeLensElementGenerator.cs is the single render path.

/// <summary>
/// Represents a single code lens item to display for a line.
/// </summary>
public class CodeLensItem
{
    /// <summary>
    /// The 1-based line number this code lens is attached to.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Display text (e.g., "3 references", "Run", "Debug").
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Command to execute when clicked.
    /// </summary>
    public string CommandName { get; set; } = "";

    /// <summary>
    /// Optional command arguments.
    /// </summary>
    public List<object>? CommandArguments { get; set; }
}

/// <summary>
/// Event args when a code lens is clicked.
/// </summary>
public class CodeLensClickedEventArgs : EventArgs
{
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
    public int Line { get; set; }
}
