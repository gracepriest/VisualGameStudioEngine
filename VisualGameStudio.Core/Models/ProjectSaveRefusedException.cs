namespace VisualGameStudio.Core.Models;

/// <summary>
/// A project save the serializer REFUSED to perform, for a reason the user can act on.
///
/// <para>⛔⛔ Not a bug and not a crash — a decision. The structure-preserving save edits the
/// project file in place, and there are shapes it cannot edit without rebuilding the file from a
/// model that never fully read it (an old-style MSBuild <c>xmlns</c> is the one that exists today).
/// Rewriting such a file would discard everything the loader does not model.</para>
///
/// <para>⛔ It exists so the refusal can be CAUGHT. Making the serializer throw was itself a fix —
/// it used to return quietly, so the IDE reported "saved", the user believed their change was
/// persisted, and it was gone at the next reload. But a bare
/// <see cref="InvalidOperationException"/> left eleven <c>SaveProjectAsync</c> call sites with no
/// handler, which turns one silent failure into an unhandled exception in whichever flow the user
/// happened to be in — a strictly worse trade. A named type is what lets every one of those sites
/// say "this could not be saved, and here is why" instead.</para>
/// </summary>
public sealed class ProjectSaveRefusedException : Exception
{
    public ProjectSaveRefusedException(string filePath, string message) : base(message) =>
        FilePath = filePath;

    /// <summary>The project file that could not be written.</summary>
    public string FilePath { get; }
}
