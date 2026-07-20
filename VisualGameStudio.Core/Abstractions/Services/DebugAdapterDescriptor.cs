using System;
using System.Collections.Generic;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// The identity of one debug adapter: who it is, which projects it debugs, and what to
/// launch. The DAP sibling of <see cref="LanguageServerDescriptor"/> — everything the debug
/// service used to hardcode about the managed adapter lives here instead, so one session
/// class can drive N adapters.
///
/// <para>
/// <b>Routing is one predicate:</b> <see cref="BasicLangProject.IsNativeBuild"/>. The managed
/// adapter serves managed builds, lldb-dap serves native ones — the same line that routes the
/// BUILD (<c>CppProjectBuilder</c>) routes the DEBUGGER, so the two can never disagree about
/// what a project is.
/// </para>
///
/// <para>
/// <b>Launch resolution happens AT SESSION START, never at construction.</b> This is the
/// deliberate contrast with clangd's DI-time path resolution: lldb-dap may be installed
/// mid-session (the one-click acquisition flow), and a descriptor that resolved once at
/// startup would answer "not installed" forever. Hence a delegate invoked per call — see
/// <see cref="ResolveLaunchCommand"/> — and hence never cache its result.
/// </para>
///
/// <para>
/// <b>Not a record, deliberately.</b> It carries delegates (launch resolution, routing), for
/// which value equality would compare delegate references — a meaningless answer that
/// "descriptors are records" would invite callers to rely on. Nothing compares descriptors;
/// they are compared by <see cref="Id"/> if at all.
/// </para>
///
/// <para>
/// <b>The constructor is private on purpose.</b> Every descriptor comes from a factory below:
/// the routing predicates are complements of one exact expression, and hand-built descriptors
/// are how a project ends up served by both adapters — or neither.
/// </para>
/// </summary>
public sealed class DebugAdapterDescriptor
{
    /// <summary>
    /// Stable <see cref="Id"/> of the managed BasicLang adapter
    /// (<c>dotnet BasicLang.dll --debug-adapter</c>).
    /// </summary>
    public const string BasicLangManagedId = "basiclang-managed";

    /// <summary>Stable <see cref="Id"/> of lldb-dap, the native C++ adapter.</summary>
    public const string LldbDapId = "lldb-dap";

    /// <summary>
    /// Settings key overriding the discovered lldb-dap executable path — the mirror of
    /// <see cref="LanguageServerDescriptor.ClangdSettingsKey"/>. Resolving it is the job of
    /// whoever builds the locator this descriptor wraps; it is here to be reported, not read
    /// behind a caller's back.
    /// </summary>
    public const string LldbDapSettingsKey = "cpp.lldbDap.path";

    private readonly Func<DapLaunchCommand?> _resolveLaunchCommand;
    private readonly Func<BasicLangProject, bool> _serves;

    private DebugAdapterDescriptor(
        string id,
        string displayName,
        Func<DapLaunchCommand?> resolveLaunchCommand,
        Func<BasicLangProject, bool> serves,
        IReadOnlyList<string> toolchains,
        DapTimeoutProfile timeouts,
        IReadOnlyList<DapExceptionFilter> fallbackExceptionFilters)
    {
        Id = id;
        DisplayName = displayName;
        Toolchains = toolchains;
        Timeouts = timeouts;
        FallbackExceptionFilters = fallbackExceptionFilters;
        _resolveLaunchCommand = resolveLaunchCommand;
        _serves = serves;
    }

    /// <summary>Stable identifier for this adapter — <c>basiclang-managed</c>, <c>lldb-dap</c>.</summary>
    public string Id { get; }

    /// <summary>Human-readable name for logs, the status bar and notifications.</summary>
    public string DisplayName { get; }

    /// <summary>
    /// The toolchains this adapter can debug the output of. Pairing metadata — v1 is
    /// informational (nothing routes on it yet), but pinned so a route can't silently drop:
    /// lldb-dap is one engine serving all three C++ routes (spec §6).
    /// </summary>
    public IReadOnlyList<string> Toolchains { get; }

    /// <summary>Per-request timeout budgets for sessions on this adapter (spec §8).</summary>
    public DapTimeoutProfile Timeouts { get; }

    /// <summary>
    /// The exception breakpoint filters to offer when the adapter's initialize response
    /// discloses none (<see cref="DapCapabilities.ExceptionBreakpointFilters"/> empty) —
    /// the known vocabulary of the adapter, not an invention.
    /// </summary>
    public IReadOnlyList<DapExceptionFilter> FallbackExceptionFilters { get; }

    /// <summary>
    /// What to launch for a new session — resolved AT SESSION START, because the adapter may
    /// be installed mid-session. Never cache the result; ask again for every session. Null
    /// means the adapter is not installed (and the caller owns saying so usefully).
    /// </summary>
    public DapLaunchCommand? ResolveLaunchCommand() => _resolveLaunchCommand();

    /// <summary>
    /// Whether this adapter debugs <paramref name="project"/> — the
    /// <see cref="BasicLangProject.IsNativeBuild"/> predicate, asked of the descriptor so
    /// callers never re-derive the routing rule.
    /// </summary>
    public bool Serves(BasicLangProject project) => _serves(project);

    /// <summary>
    /// The managed BasicLang adapter: <c>dotnet &lt;compilerPath&gt; --debug-adapter</c>.
    /// </summary>
    /// <param name="resolveCompilerPath">
    /// Locator for <c>BasicLang.dll</c> (override or probe) — invoked per session, null when
    /// the compiler cannot be found.
    /// </param>
    public static DebugAdapterDescriptor BasicLangManaged(Func<string?> resolveCompilerPath) => new(
        id: BasicLangManagedId,
        displayName: "BasicLang (managed)",
        // The adapter is a managed assembly, so the process to start is the .NET host and the
        // assembly is its first argument. Quoted — the IDE is routinely installed under a path
        // with spaces.
        resolveLaunchCommand: () => resolveCompilerPath() is string p
            ? new DapLaunchCommand("dotnet", $"\"{p}\" --debug-adapter")
            : null,
        serves: p => !p.IsNativeBuild,
        toolchains: Array.Empty<string>(),
        timeouts: DapTimeoutProfile.Managed,
        fallbackExceptionFilters: new[]
        {
            new DapExceptionFilter("all", "All Exceptions", false),
            new DapExceptionFilter("uncaught", "Uncaught Exceptions", true),
            new DapExceptionFilter("thrown", "Thrown Exceptions", false, SupportsCondition: true),
        });

    /// <summary>
    /// lldb-dap, the native C++ adapter — launched by absolute path, no arguments.
    /// </summary>
    /// <param name="resolveExecutable">
    /// Locator for the lldb-dap executable (settings override, then probe) — invoked per
    /// session so a mid-session install is picked up, null when nothing is found.
    /// </param>
    public static DebugAdapterDescriptor LldbDap(Func<string?> resolveExecutable) => new(
        id: LldbDapId,
        displayName: "lldb-dap (native C++)",
        resolveLaunchCommand: () => resolveExecutable() is string p
            ? new DapLaunchCommand(p, string.Empty)
            : null,
        serves: p => p.IsNativeBuild,
        toolchains: new[] { "msvc", "clang", "g++" }, // one engine, three routes (spec §6)
        timeouts: DapTimeoutProfile.LldbDap,
        fallbackExceptionFilters: new[]
        {
            new DapExceptionFilter("cpp_throw", "C++ Throw", false),
            new DapExceptionFilter("cpp_catch", "C++ Catch", false),
        });
}

/// <summary>
/// What to launch for a debug adapter process —
/// <see cref="System.Diagnostics.ProcessStartInfo.FileName"/> / <c>Arguments</c>, pre-composed
/// so no caller re-derives quoting rules.
/// </summary>
public sealed record DapLaunchCommand(string FileName, string Arguments);
