namespace VisualGameStudio.Shell.ViewModels.Designer;

/// <summary>
/// Counts the member writes of ONE multi-selection gesture (property-grid slice 6, pre-flight 2026-10-05 D-5).
///
/// <para>⛔ The member rows of a merged row are built with <see cref="Mark"/> as their change callback, never the grid's
/// <c>RaiseEdited</c>: N members raising <c>Edited</c> would be N document writes and N undo steps for one edit. The merged
/// row resets the tally, lets each member commit (its own no-op / reset / write verdict), and raises ONE <c>Edited</c>
/// when the tally counted anything.</para>
/// </summary>
public sealed class FormEditTally
{
    /// <summary>How many member writes the current gesture made.</summary>
    public int Count { get; private set; }

    /// <summary>A member changed the model — the callback each member row is built with.</summary>
    public void Mark() => Count++;

    /// <summary>Starts a gesture.</summary>
    public void Reset() => Count = 0;
}
