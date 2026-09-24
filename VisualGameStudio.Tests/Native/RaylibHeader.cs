using System.IO;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// The authoritative raylib 5.5 header the parity fixtures cross-check against.
///
/// <para>It is NOT in the repository: it comes from the <c>raylib</c> NuGet package in
/// <c>VisualGameStudioEngine/packages.config</c>, restored into <c>packages/</c> by the engine's
/// C++ (vcxproj) build. A fresh clone that has not built the engine — and any Linux/macOS machine,
/// where that build does not run — has no <c>packages/</c> folder.</para>
///
/// <para>Called AFTER each fixture's framework.h ⇄ RaylibWrapper.vb parity assertions, inside their
/// <c>Assert.Multiple</c>: when the header is missing the test is skipped only if those parity checks
/// passed (see <see cref="TestSkip.IgnoreEvenInsideMultiple"/>), so the wrapper-sync invariant is
/// still enforced everywhere and only the raylib cross-check is lost.</para>
/// </summary>
internal static class RaylibHeader
{
    public static string Read(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "packages", "raylib.5.5.0", "build", "native", "include", "raylib.h");
        if (!File.Exists(path))
        {
            TestSkip.IgnoreEvenInsideMultiple(
                $"raylib.h is not restored ({path}): it comes from the engine's NuGet packages.config, "
                + "restored by building VisualGameStudioEngine.vcxproj (Windows). The framework.h/wrapper "
                + "parity checks above this point passed; only the raylib cross-check was skipped.");
        }
        return File.ReadAllText(path);
    }
}
