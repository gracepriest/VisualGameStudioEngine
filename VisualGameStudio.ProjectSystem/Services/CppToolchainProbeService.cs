using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Real <see cref="ICppToolchainProbe"/>: delegates to the compiler's
/// <c>CppToolchain.ProbeAvailability()</c> (PATH + vswhere existence checks).
/// Lives here because Core cannot reference BasicLang.
/// </summary>
public sealed class CppToolchainProbeService : ICppToolchainProbe
{
    public ToolchainAvailability Probe()
    {
        var a = BasicLang.Compiler.ProjectSystem.CppToolchain.ProbeAvailability();
        return new ToolchainAvailability(a.Llvm, a.Gcc, a.Msvc);
    }
}
