using BasicLang.Compiler.ProjectSystem;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Runs <see cref="IntelliSenseEmitter"/> for the IDE, so a native project has
/// <c>obj/compile_commands.json</c> and the generated <c>obj/gen</c> headers before it has ever
/// been built (Task 10 / spec §4). Without this, clangd on a fresh checkout has nothing to read.
///
/// <para>
/// The emitter itself is Task 9's business; everything here is about being a well-behaved IDE
/// caller of a multi-second, non-incremental operation:
/// </para>
/// <list type="bullet">
/// <item><b>Off the calling thread.</b> The expensive part — the front-end run — is inside
/// <c>Task.Run</c>. What executes on the caller is only the native-project gate and the supersede
/// bookkeeping: all O(1), none blocking. <c>Cancel()</c> is O(1) ONLY because nothing registers a
/// cancellation callback — callbacks run synchronously on the canceller's thread, so if any are
/// ever added (Tasks 11-13 kill a clangd process; <c>token.Register(() =&gt; process.Kill())</c> is
/// the obvious way to wire it) they must move inside the <c>Task.Run</c>.</item>
/// <item><b>Coalesced.</b> A superseded request never runs. N rapid opens cost the in-flight
/// emission plus the last one, not N front-end runs.</item>
/// <item><b>Serialized.</b> Emissions never overlap: two of them writing <c>obj/gen</c> for the
/// same project at once would interleave their writes.</item>
/// <item><b>Silent on success, never fatal on failure.</b> Failures go to the Output panel as one
/// line. Never a modal, never an exception reaching the caller.</item>
/// </list>
///
/// <para>
/// <b>D2 — project open does NOT probe for a toolchain.</b> <see cref="IntelliSenseEmitter.Emit"/>
/// takes the toolchain from its caller and never probes on its own, so this class decides. It
/// passes <c>null</c>, and the database is emitted for the blessed default (clang++ / GNU flags)
/// even on a machine where <see cref="CppToolchain.Find"/> would answer MSVC. The reasoning:
/// </para>
/// <list type="bullet">
/// <item><b>The fidelity delta is small.</b> The two flavors differ only in
/// <c>CppToolchain.FlagsFor</c>: driver name and flag SPELLING (<c>-std=</c>/<c>-I</c>/<c>-D</c>
/// vs <c>/std:</c>/<c>/I</c>/<c>/D</c>). The include dirs and defines they carry are identical,
/// and NEITHER flavor carries system include paths — clangd resolves those from the driver, and
/// on Windows both drivers resolve to the same msvc target and the same system headers. Kind and
/// driver are always paired from one source, so a clang++-flavored database is coherent, not
/// mixed.</item>
/// <item><b>The cost is not.</b> <c>Find()</c> spawns processes (clang++/g++ <c>--version</c>,
/// then vswhere) with waits totalling up to 35s worst case. That would land mid-emission and
/// delay the artifacts clangd is waiting for, on every open, for a flag-spelling refinement.</item>
/// <item><b>The divergence self-corrects, and is bounded.</b> <c>Build</c> drives the same core
/// with a real probe, so the first build of a session rewrites the database with the real
/// toolchain's own identity. The cost of not probing is therefore at most one extra clangd
/// re-parse per session, not an ongoing wrongness.</item>
/// </list>
/// <para>
/// If fidelity ever demands the real toolchain here, this is a one-line change — pass a probed
/// <see cref="CppToolchain"/> to <see cref="IntelliSenseEmitter.Emit"/> instead of null. Keep it
/// inside the <c>Task.Run</c>.
/// </para>
/// </summary>
public sealed class IntelliSenseEmissionService : IIntelliSenseEmissionService, IDisposable
{
    private readonly IOutputService _output;
    private readonly Func<string, string, CppProjectBuildResult> _emit;

    private readonly object _gate = new();
    private CancellationTokenSource? _current;
    private Task _chain = Task.CompletedTask;
    private bool _disposed;

    /// <summary>
    /// D2/back-compat entry point: the toolchain-agnostic, no-override <see cref="DefaultEmit"/>.
    /// Kept for this class's own tests (which exercise it directly, unaware of overrides);
    /// production DI (Task 7) resolves through the <see cref="CppToolchainOverrides"/> ctor below
    /// instead.
    /// </summary>
    public IntelliSenseEmissionService(IOutputService output)
        : this(output, DefaultEmit)
    {
    }

    /// <summary>
    /// Production entry point once per-backend C++ toolchain overrides exist (Task 7): the
    /// emitted <c>compile_commands.json</c> must name whatever a real build would use, including
    /// a pinned, possibly off-PATH override — otherwise a project pinned to e.g. an off-PATH gcc
    /// gets a clangd database that still says <c>clang++</c> while the build itself compiles with
    /// the override, and clangd's diagnostics drift from what actually happened.
    /// </summary>
    public IntelliSenseEmissionService(IOutputService output, CppToolchainOverrides overrides)
        : this(output, (projectPath, configuration) => IntelliSenseEmitter.Emit(
            ProjectFile.Load(projectPath), configuration, toolchain: null,
            resolveById: id => overrides.UsableCompilerToolchain(id)))
    {
    }

    /// <summary>
    /// Seam constructor: <paramref name="emit"/> receives (project file path, configuration).
    /// PUBLIC deliberately — this assembly has no <c>InternalsVisibleTo</c> for
    /// VisualGameStudio.Tests, and the properties worth testing here (off-thread, coalescing,
    /// serialization, failure handling) all need an emitter that can be made to block or throw
    /// on demand. The real front end can do neither.
    /// </summary>
    public IntelliSenseEmissionService(IOutputService output, Func<string, string, CppProjectBuildResult> emit)
    {
        _output = output;
        _emit = emit;
    }

    /// <summary>See <see cref="IIntelliSenseEmissionService.RequestEmit"/>. Never throws, never faults.</summary>
    public Task RequestEmit(BasicLangProject? project, string configuration)
    {
        // Native projects only: a C#-backend project has no C++ for clangd to read, and emission
        // is a whole front-end run. Keyed on IsNativeBuild, so it covers BOTH Language=Cpp and
        // BasicLang-on-the-C++-backend — the same property BuildService routes on.
        if (project == null || !project.IsNativeBuild || string.IsNullOrEmpty(project.FilePath))
            return Task.CompletedTask;

        // Captured now: the caller may mutate the project object while we run on the pool.
        var projectPath = project.FilePath;
        var projectName = project.Name;

        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;

            // Supersede whatever is pending. An emission already inside the front end cannot be
            // interrupted (CompileProjectFiles takes no CancellationToken), so this only stops a
            // request that has not started — which is exactly the one worth stopping.
            //
            // ⚠ Superseding is unconditional — it does NOT compare projects. That is correct only
            // because IProjectService holds ONE project: OpenProjectAsync closes the current one
            // before firing ProjectOpened, and SolutionService never opens projects through it. So
            // a superseded request is always for a project that is now CLOSED.
            //
            // THE precondition is single-project, and nothing else. Phase 3b's extra triggers
            // (.bas-save, .blproj-change) are harmless on their own: with one project open, a
            // save-triggered emission superseding an open-triggered one is the SAME project, which
            // is exactly what superseding is for. The day the IDE holds several projects open,
            // this must key on the project — otherwise opening a 3-project solution emits for the
            // first and last and silently skips the middle one, leaving it with no compile
            // database and no error to say so.
            _current?.Cancel();
            var cts = new CancellationTokenSource();
            _current = cts;

            var previous = _chain;
            _chain = Task.Run(() => RunAsync(projectPath, projectName, configuration, previous, cts.Token));
            return _chain;
        }
    }

    private async Task RunAsync(
        string projectPath, string projectName, string configuration, Task previous, CancellationToken token)
    {
        // Wait out the previous emission rather than racing it into obj/gen. Its faults are its
        // own business — RunAsync never faults, but a future edit must not make this the place a
        // predecessor's failure takes down its successor.
        try { await previous.ConfigureAwait(false); } catch { }

        // The only point cancellation can bite. A newer request arrived while we queued, and it
        // will emit the same artifacts for the same project — running now would be pure waste.
        if (token.IsCancellationRequested) return;

        try
        {
            var result = _emit(projectPath, configuration);

            // Superseded while we ran: a newer emission is about to overwrite these artifacts, so
            // this result describes state that no longer holds. Reporting it would be noise.
            if (token.IsCancellationRequested) return;

            if (!result.Success)
            {
                // ONE line. A transpile failure here is the normal mid-edit state (regen-on-
                // success-only means clangd keeps serving the last good headers), so it must not
                // read like a failed build — but silence would leave stale IntelliSense with no
                // explanation, which is worse.
                var first = result.Diagnostics.FirstOrDefault();
                var detail = first != null
                    ? $"{first.Code}: {first.Message}"
                    : "no diagnostics reported";
                var more = result.Diagnostics.Count > 1
                    ? $" (+{result.Diagnostics.Count - 1} more)"
                    : "";
                _output.WriteLine(
                    $"[IntelliSense] {projectName}: could not regenerate C++ headers — {detail}{more}. "
                    + "Previously generated headers, if any, remain in use.",
                    OutputCategory.LanguageServer);
            }
        }
        catch (Exception ex)
        {
            // Project deleted mid-open, an unreadable .blproj, obj/ read-only. None of it may
            // reach the caller: the open path discards this task, so a fault here would be an
            // unobserved exception — and opening a project must survive a broken one anyway.
            _output.WriteLine(
                $"[IntelliSense] {projectName}: emission failed — {ex.Message}",
                OutputCategory.LanguageServer);
        }
    }

    /// <summary>
    /// D2 lives here: <c>toolchain: null</c>. See the class remarks.
    /// </summary>
    private static CppProjectBuildResult DefaultEmit(string projectPath, string configuration)
        => IntelliSenseEmitter.Emit(ProjectFile.Load(projectPath), configuration, toolchain: null);

    /// <summary>
    /// Cancels pending work and refuses new requests. Deliberately does NOT wait: a flush would
    /// hold shutdown for a full front-end run, and the artifacts are regenerated on next open
    /// anyway. Disposal is via the DI container (App.ShutdownRequested disposes it).
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _current?.Cancel();
            // The CancellationTokenSources are deliberately never disposed — including this one.
            // A RunAsync that has not reached its token check still reads it, and a CTS with no
            // registrations, no timer and no linked source holds nothing Dispose would release:
            // it is ordinary garbage. Disposing it here would be a race for no benefit.
            _current = null;
        }
    }
}
