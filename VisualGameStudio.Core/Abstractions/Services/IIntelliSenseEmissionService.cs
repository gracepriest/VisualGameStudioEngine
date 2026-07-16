using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Produces the two artifacts clangd needs for a native project — the generated <c>obj/gen</c>
/// headers and <c>obj/compile_commands.json</c> — at moments when no build has run.
///
/// <para>
/// <b>Why this exists:</b> both artifacts are build outputs today, so on a fresh checkout clangd
/// has nothing to read and C++ IntelliSense is dead until the user's first successful build.
/// Emitting them on project open closes that hole (spec §4).
/// </para>
///
/// <para>
/// <b>Cost, and what it forces:</b> emission runs the entire compiler front end (transpile +
/// the full optimizer pipeline) for the whole project — seconds, with no incrementality. So
/// every implementation MUST run off the calling thread, must never let a failure reach the
/// caller, and must coalesce a burst of requests rather than queue one emission per request.
/// The project-open path calls this and DISCARDS the returned task: opening a project must
/// never wait on a compile.
/// </para>
/// </summary>
public interface IIntelliSenseEmissionService
{
    /// <summary>
    /// Requests emission for <paramref name="project"/>. Returns immediately; the work runs on
    /// the thread pool.
    ///
    /// <para>
    /// A no-op for anything that is not a native project (<see cref="BasicLangProject.IsNativeBuild"/>)
    /// — there is no C++ for clangd to read in a C#-backend project. Never throws: an emission
    /// failure is reported to the Output panel, never to the caller and never to a modal.
    /// </para>
    /// </summary>
    /// <param name="configuration">Build configuration name ("Debug"/"Release") whose flags the
    /// compile database should carry.</param>
    /// <returns>
    /// The scheduled emission. Exposed so tests and future callers can observe completion —
    /// <b>the project-open path deliberately discards it</b>, because awaiting it would block
    /// open behind a full front-end run. Never faults.
    /// </returns>
    Task RequestEmit(BasicLangProject? project, string configuration);
}
