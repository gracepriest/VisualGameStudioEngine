using System;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Skips for native (C++ backend) BUILD tests whose requirement is missing on this machine.
///
/// <para>⛔ "Some C++ compiler exists" (<c>CppToolchain.Find() != null</c>) is the wrong gate for a
/// BasicLang native project: those ALWAYS build with MSVC (<c>ProjectFile.EffectiveCppToolchain</c>,
/// an owner directive — see CppProjectBuilder's toolchain step). On a machine with only clang or gcc
/// that gate passes and the build then fails with BL6015, so the test FAILED where it should have
/// been skipped — 27 such rows on Linux.</para>
///
/// <para>Call these OUTSIDE any <c>Assert.Multiple</c> block: NUnit refuses <c>Assert.Ignore</c>
/// inside one.</para>
/// </summary>
internal static class NativeBuildSkip
{
    private static readonly Lazy<bool> MsvcInstalled =
        new(() => CppToolchain.TryFindById("msvc") != null);

    /// <summary>Skip unless MSVC — the toolchain every BasicLang native build uses — is installed.</summary>
    public static void RequireMsvcForBasicLangNative()
    {
        if (!MsvcInstalled.Value)
            Assert.Ignore("BasicLang native projects always build with MSVC, which is not installed "
                          + "on this machine (the build would fail with BL6015).");
    }

    /// <summary>
    /// Skip unless the engine's import library can actually be linked. It is an MSVC import
    /// library, so it only links on Windows. Off Windows the test process can still FIND a copy
    /// (one is deployed into the test output folder), which is why "LocateImportLib() != null"
    /// alone is not enough: the link then fails.
    /// </summary>
    public static void RequireUsableEngineImportLib()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("VisualGameStudioEngine.lib is an MSVC import library (Windows only)");
        if (EngineDeployment.LocateImportLib() == null)
            Assert.Ignore("VisualGameStudioEngine.lib not found (engine not built)");
    }
}
