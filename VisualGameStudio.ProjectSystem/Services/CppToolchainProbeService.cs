using System;
using BasicLang.Compiler.ProjectSystem;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Real <see cref="ICppToolchainProbe"/>: delegates to the compiler's
/// <c>CppToolchain.ProbeAvailability()</c> (PATH + vswhere existence checks), then applies
/// the <b>authoritative-when-set</b> rule from the injected <see cref="CppToolchainOverrides"/>
/// reader: a backend with a set, Usable compiler override is always available (even off
/// PATH — the winlibs-off-PATH goal); a set, Invalid override greys the backend even when it
/// IS on PATH; a blank override (None) leaves the pure PATH/vswhere probe result untouched.
/// The compiler-side <c>CppToolchain.ProbeAvailability()</c> itself stays pure PATH/vswhere —
/// the override logic lives only here. Lives here because Core cannot reference BasicLang.
/// </summary>
public sealed class CppToolchainProbeService : ICppToolchainProbe
{
    private readonly CppToolchainOverrides _overrides;
    private readonly Func<CppToolchainAvailability> _baseProbe;

    /// <param name="overrides">DI-registered singleton reader for the six cpp.toolchain.* keys.</param>
    /// <param name="baseProbe">
    /// Test seam for the pure PATH/vswhere probe; defaults to
    /// <see cref="CppToolchain.ProbeAvailability"/>. Tests inject a fake to make "on PATH" /
    /// "off PATH" deterministic without touching a real installed toolchain.
    /// </param>
    public CppToolchainProbeService(CppToolchainOverrides overrides,
        Func<CppToolchainAvailability>? baseProbe = null)
    {
        _overrides = overrides;
        _baseProbe = baseProbe ?? CppToolchain.ProbeAvailability;
    }

    public ToolchainAvailability Probe()
    {
        var basep = _baseProbe();

        bool Avail(string id, bool onPath) => _overrides.ResolveCompiler(id).State switch
        {
            OverrideState.None => onPath,       // blank -> probe
            OverrideState.Usable => true,       // set & usable -> available
            OverrideState.Invalid => false,     // set & broken -> greyed, even if on PATH
            _ => onPath,
        };

        return new ToolchainAvailability(
            Avail("llvm", basep.Llvm), Avail("gcc", basep.Gcc), Avail("msvc", basep.Msvc));
    }
}
