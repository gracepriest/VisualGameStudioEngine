namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Which of the known C++ toolchain ids ("llvm" / "gcc" / "msvc") are installed
/// on this machine, as seen by <see cref="ICppToolchainProbe.Probe"/>.
/// </summary>
public sealed record ToolchainAvailability(bool Llvm, bool Gcc, bool Msvc);

/// <summary>
/// Probes which C++ toolchains exist on this machine. Core-side seam over the
/// compiler's toolchain discovery so UI consumers (the New Project wizard's
/// toolchain picker) can be tested with a fake instead of the real machine.
/// </summary>
public interface ICppToolchainProbe
{
    /// <summary>Cheap existence probe (PATH + vswhere; never vcvars). Safe to call off the UI thread.</summary>
    ToolchainAvailability Probe();
}
