namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// The IDE's language servers — one <see cref="ILanguageService"/> per
/// <see cref="LanguageServerDescriptor"/> — and the routing that hands each document to the
/// server that owns it.
///
/// <para>
/// <b>It only routes.</b> Every piece of connection state a second server would corrupt (the
/// process, the reader/writer, the request-id space, the restart budget) is instance state on
/// <see cref="ILanguageService"/>, so one service per descriptor solves all of it by
/// construction. This interface deliberately adds no state of its own beyond the set of
/// services and their lifetime.
/// </para>
///
/// <para>
/// <b>⚠ This is the DI singleton, and it owns its services.</b> Disposing it disposes every
/// service it holds — that chain is what makes <c>App.ShutdownRequested</c>'s
/// <c>(Services as IDisposable)?.Dispose()</c> reach the server processes. A registry that
/// lazily constructed services outside the container would never be disposed and would orphan
/// a language-server child process on every IDE exit; that bug has been shipped here before.
/// Services are constructed with the container and handed to the registry, never created by it.
/// </para>
/// </summary>
public interface ILanguageServiceRegistry : IDisposable
{
    /// <summary>
    /// Every registered server, in registration order. For operations that are genuinely about
    /// all servers (a status display, a diagnostics dump); routing goes through
    /// <see cref="GetFor"/>.
    /// </summary>
    IReadOnlyList<ILanguageService> All { get; }

    /// <summary>
    /// The server that owns <paramref name="path"/>, or <b>null when none does</b>.
    /// </summary>
    /// <remarks>
    /// ⚠ Null is the common case, not an error: it is the answer for every file the IDE can open
    /// that no language server serves (<c>.txt</c>, <c>.json</c>, <c>.md</c>, …), and also for a
    /// language the routing map knows but no server is registered for — C++ before Task 12
    /// registers clangd, or C++ on a machine with no clangd installed.
    /// <para>
    /// <b>The compiler will not remind you.</b> CS8604 is in <c>NoWarn</c> across these projects,
    /// so this null flows into a non-nullable parameter silently. Callers that must have a server
    /// should ask, and do nothing when the answer is null — which is exactly what the
    /// <c>IsBasicLangSourceFile</c> exclusion gates it replaces used to do, only without assuming
    /// the answer is always BasicLang.
    /// </para>
    /// </remarks>
    ILanguageService? GetFor(string? path);

    /// <summary>
    /// Whether the server that owns <paramref name="path"/> is connected and ready — false when
    /// no server owns it.
    /// </summary>
    /// <remarks>
    /// The whole reason this is not <c>ILanguageService.IsConnected</c>: with N servers,
    /// "is the language server connected?" has no single answer. A BasicLang server that is
    /// down, restarting or missing must not gate C++ IntelliSense, and vice versa.
    /// </remarks>
    bool IsConnectedFor(string? path);

    /// <summary>
    /// Start every registered server, rooted at <paramref name="workspaceRoot"/>.
    /// <para>
    /// ⚠ Servers that are already connected are unaffected: <see cref="ILanguageService.StartAsync"/>
    /// is a no-op for them and does <b>not</b> re-root them. So this alone does not handle a project
    /// SWITCH — calling it with project B's root while a server is still connected to project A
    /// leaves that server rooted at A, silently answering from the wrong compilation database.
    /// Re-rooting means <see cref="StopAllAsync"/> then <see cref="StartAllAsync"/>.
    /// </para>
    /// </summary>
    /// <param name="workspaceRoot">
    /// The open project's directory. <b>Required</b>, and validated: a rootless bulk start is a
    /// bug, not a mode. clangd launched without a root omits <c>--compile-commands-dir</c>
    /// entirely, never finds <c>obj/compile_commands.json</c>, and answers from guessed flags —
    /// wrong IntelliSense with no error anywhere. Hence this is called on
    /// <c>IProjectService.ProjectOpened</c>, where a root is known, rather than from a
    /// constructor, where one is not.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="workspaceRoot"/> is null, empty or whitespace. Thrown before any server is
    /// started, so a rootless call cannot half-apply. The nullable annotation cannot enforce this
    /// (CS8604 is in <c>NoWarn</c>), so the check is at runtime.
    /// </exception>
    /// <exception cref="AggregateException">
    /// One or more servers failed to start. Every other server is started first: clangd failing
    /// to launch must not cost the user BasicLang IntelliSense.
    /// </exception>
    Task StartAllAsync(string workspaceRoot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop every registered server. Each is attempted even if another throws.
    /// </summary>
    /// <exception cref="AggregateException">One or more servers failed to stop.</exception>
    Task StopAllAsync();
}
