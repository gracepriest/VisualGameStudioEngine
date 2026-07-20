using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// The IDE's roster of debug adapters — the DAP sibling of
/// <see cref="ILanguageServiceRegistry"/>, but deliberately NOT its twin: the LSP registry
/// owns long-lived server processes and therefore a dispose/stop lifecycle, while this
/// registry owns NO processes at all. DAP sessions are per-launch and owned by
/// <c>IDebugService</c>; what lives here is pure identity
/// (<see cref="DebugAdapterDescriptor"/>), so there is no <see cref="IDisposable"/> and no
/// start/stop surface — nothing to leak, nothing to orphan.
///
/// <para>
/// <b><see cref="Register"/> is the public door; built-ins and extensions use the same
/// method</b> (spec §2.3). The two shipped adapters are registered through it at DI
/// composition exactly the way a future extension would register a third — there is no
/// privileged side entrance for built-ins, so the door's rules (one registration per id)
/// are proven on day one.
/// </para>
///
/// <para>
/// Routing (<see cref="GetFor"/>) asks each descriptor's
/// <see cref="DebugAdapterDescriptor.Serves"/> in registration order and takes the first
/// yes. Whether an adapter is INSTALLED is deliberately not this registry's question:
/// descriptors resolve their launch command at session start
/// (<see cref="DebugAdapterDescriptor.ResolveLaunchCommand"/>), so an lldb-dap installed
/// mid-session is found on the next F5 — registering only-what-is-installed (the clangd
/// D1 pattern) would freeze that answer at IDE start.
/// </para>
/// </summary>
public interface IDebugAdapterRegistry
{
    /// <summary>Every registered descriptor, in registration order.</summary>
    IReadOnlyList<DebugAdapterDescriptor> All { get; }

    /// <summary>
    /// The adapter that debugs <paramref name="project"/>: the first registered descriptor
    /// whose <see cref="DebugAdapterDescriptor.Serves"/> says yes. Null when no project is
    /// in hand, or when nothing registered serves it — a real answer the F5 seam owns
    /// turning into a useful message, never a throw.
    /// </summary>
    DebugAdapterDescriptor? GetFor(BasicLangProject? project);

    /// <summary>
    /// The descriptor registered under <paramref name="id"/> (ordinal compare — ids are
    /// wire-stable tokens carried on <c>DebugConfiguration.AdapterId</c>, not display
    /// text), or null on a miss.
    /// </summary>
    DebugAdapterDescriptor? GetById(string id);

    /// <summary>
    /// Adds a descriptor to the roster — the public door; built-ins and extensions use the
    /// same method (spec §2.3). Throws <see cref="ArgumentException"/> when
    /// <paramref name="descriptor"/>'s id is already registered (ordinal): a second
    /// registration under one id is how an IDE ends up with a second adapter process
    /// nothing routes to, starts, stops or disposes — the LSP registry's two-process
    /// orphan lesson, refused at the door instead of discovered at first use.
    /// </summary>
    void Register(DebugAdapterDescriptor descriptor);
}
