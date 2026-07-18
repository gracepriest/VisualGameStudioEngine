using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins <see cref="ClangdLocator"/> — how the IDE decides WHICH clangd to talk to.
///
/// <para>The override rule is deliberately the same one <c>basiclang.lsp.path</c> already uses: a
/// user override wins only when it names a file that exists, otherwise auto-probe. The auto-probe
/// is a CHAIN — the IDE-installed <c>~/.vgs/tools</c> copy, then PATH, then conventional LLVM
/// install directories — and every link is an injected seam here.</para>
///
/// <para>All probe dependencies are injected in the chain tests: a machine may or may not carry a
/// real clangd (this suite's dev boxes often have one under <c>~/.vgs/tools</c>), so any test
/// that let a real probe run could assert nothing stable. The <c>ToolsProbe_</c> tests that DO
/// touch the disk stage their own temp tools root instead.</para>
/// </summary>
[TestFixture]
public class ClangdLocatorTests
{
    private const string Key = LanguageServerDescriptor.ClangdSettingsKey;

    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new System.Collections.Generic.HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p);
    }

    // ---- Case 1: a configured override that exists wins outright. ----

    [Test]
    public void ResolveClangdPath_ConfiguredPathExists_WinsAndTheProbeIsNeverRun()
    {
        var configured = @"C:\my\clangd.exe";
        var probeRuns = 0;

        var resolved = ClangdLocator.ResolveClangdPath(
            configured,
            fileExists: Existing(configured),
            pathProbe: () => { probeRuns++; return @"C:\path\clangd.exe"; });

        Assert.That(resolved, Is.EqualTo(configured));
        Assert.That(probeRuns, Is.Zero, "an override that exists must short-circuit the PATH search");
    }

    [Test]
    public void ResolveClangdPath_ConfiguredPath_IsTrimmed()
    {
        var resolved = ClangdLocator.ResolveClangdPath(
            "  C:\\my\\clangd.exe  ",
            fileExists: Existing(@"C:\my\clangd.exe"),
            pathProbe: () => null);

        // Exact bytes: this string becomes a process filename.
        Assert.That(resolved, Is.EqualTo(@"C:\my\clangd.exe"));
    }

    // ---- Case 2: a configured override that does NOT exist falls back to the probe. ----

    [Test]
    public void ResolveClangdPath_ConfiguredPathMissing_FallsBackToTheProbe()
    {
        var probed = @"C:\tools\clangd.exe";

        var resolved = ClangdLocator.ResolveClangdPath(
            @"C:\stale\clangd.exe",
            fileExists: Existing(),      // nothing exists
            pathProbe: () => probed,
            toolsProbe: () => null,
            llvmProbe: () => null);

        Assert.That(resolved, Is.EqualTo(probed),
            "a stale override (uninstalled/renamed tool) must degrade to auto-detection, not to nothing");
    }

    // ---- Case 3: no override configured → probe. ----

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveClangdPath_NoConfiguredPath_UsesTheProbe(string? configured)
    {
        var probed = @"C:\tools\clangd.exe";

        var resolved = ClangdLocator.ResolveClangdPath(
            configured,
            fileExists: _ => throw new AssertionException(
                "an unset override must not be existence-checked"),
            pathProbe: () => probed,
            toolsProbe: () => null,
            llvmProbe: () => null);

        Assert.That(resolved, Is.EqualTo(probed));
    }

    // ---- Case 4: the whole chain finds nothing → null (the honest "no clangd here" answer). ----

    [Test]
    public void ResolveClangdPath_ProbeFindsNothing_ReturnsNull()
    {
        var resolved = ClangdLocator.ResolveClangdPath(
            "",
            fileExists: Existing(),
            pathProbe: () => null,
            toolsProbe: () => null,
            llvmProbe: () => null);

        Assert.That(resolved, Is.Null,
            "null is the signal Task 12 needs in order to degrade gracefully; a bare \"clangd\" " +
            "fallback string would be spawned and fail late instead");
    }

    [Test]
    public void ResolveClangdPath_ConfiguredMissingAndProbeFindsNothing_ReturnsNull()
    {
        Assert.That(ClangdLocator.ResolveClangdPath(
            @"C:\stale\clangd.exe", Existing(), () => null,
            toolsProbe: () => null, llvmProbe: () => null), Is.Null);
    }

    // ---- The auto-probe chain: override → ~/.vgs/tools → PATH → LLVM install dirs. ----
    // Each link is asserted against its immediate successor, so reordering any two adjacent links
    // fails exactly one of these.

    [Test]
    public void Chain_OverrideBeatsToolsDir()
    {
        var configured = @"C:\my\clangd.exe";
        var toolsRuns = 0;

        var resolved = ClangdLocator.ResolveClangdPath(
            configured,
            fileExists: Existing(configured),
            pathProbe: () => null,
            toolsProbe: () => { toolsRuns++; return @"C:\vgs\tools\clangd_1.0.0\bin\clangd.exe"; },
            llvmProbe: () => null);

        Assert.That(resolved, Is.EqualTo(configured),
            "an explicit user override outranks even the IDE's own installed copy");
        Assert.That(toolsRuns, Is.Zero, "an existing override must short-circuit the whole probe chain");
    }

    [Test]
    public void Chain_ToolsDirBeatsPath()
    {
        var tools = @"C:\vgs\tools\clangd_22.1.6\bin\clangd.exe";
        var pathRuns = 0;

        var resolved = ClangdLocator.ResolveClangdPath(
            null,
            fileExists: Existing(),
            pathProbe: () => { pathRuns++; return @"C:\onpath\clangd.exe"; },
            toolsProbe: () => tools,
            llvmProbe: () => null);

        Assert.That(resolved, Is.EqualTo(tools),
            "the IDE-installed clangd (a pinned, hash-verified release) must outrank whatever happens to be on PATH");
        Assert.That(pathRuns, Is.Zero, "a tools-root hit must short-circuit the PATH search");
    }

    [Test]
    public void Chain_PathBeatsLlvmDirs()
    {
        var onPath = @"C:\onpath\clangd.exe";
        var llvmRuns = 0;

        var resolved = ClangdLocator.ResolveClangdPath(
            null,
            fileExists: Existing(),
            pathProbe: () => onPath,
            toolsProbe: () => null,
            llvmProbe: () => { llvmRuns++; return @"C:\Program Files\LLVM\bin\clangd.exe"; });

        Assert.That(resolved, Is.EqualTo(onPath),
            "PATH is the user's own arrangement; the conventional-install-dir sweep is a fallback, not a peer");
        Assert.That(llvmRuns, Is.Zero, "a PATH hit must short-circuit the LLVM-dir sweep");
    }

    [Test]
    public void Chain_LlvmDirsAreTheLastResort()
    {
        var llvm = @"C:\Program Files\LLVM\bin\clangd.exe";

        var resolved = ClangdLocator.ResolveClangdPath(
            null,
            fileExists: Existing(),
            pathProbe: () => null,
            toolsProbe: () => null,
            llvmProbe: () => llvm);

        Assert.That(resolved, Is.EqualTo(llvm),
            "when nothing else answers, a conventional LLVM install location is still a real clangd");
    }

    // ---- The ~/.vgs/tools probe: versioned dirs under the tools root, NUMERIC-highest wins. ----

    /// <summary>Creates <c>root/dirName/bin/&lt;clangd binary&gt;</c> and returns the binary's path.</summary>
    private static string StageToolsInstall(string root, string dirName)
    {
        var bin = Path.Combine(root, dirName, "bin");
        Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, OperatingSystem.IsWindows() ? "clangd.exe" : "clangd");
        File.WriteAllText(exe, "stand-in for a real clangd — only its PATH is under test");
        return exe;
    }

    [Test]
    public void ToolsProbe_PicksHighestNumericVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vgs_tools_{Guid.NewGuid():N}");
        try
        {
            var newer = StageToolsInstall(root, "clangd_22.1.6");
            StageToolsInstall(root, "clangd_9.0.0");

            var found = ClangdLocator.FindInToolsRoot(root);

            // NUMERIC comparison is the point: an ordinal string sort ranks "clangd_9..." above
            // "clangd_22..." and would pin the probe to an ancient install forever.
            Assert.That(found, Is.EqualTo(newer));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void ToolsProbe_UnparseableSuffixLosesToParseable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vgs_tools_{Guid.NewGuid():N}");
        try
        {
            var parseable = StageToolsInstall(root, "clangd_1.0.0");
            StageToolsInstall(root, "clangd_backup"); // ordinally ABOVE "clangd_1.0.0" — a name sort would pick it

            var found = ClangdLocator.FindInToolsRoot(root);

            Assert.That(found, Is.EqualTo(parseable),
                "a dir whose suffix does not parse as a version must rank below ANY parsed version");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void ToolsProbe_PosixBinaryName()
    {
        // Pure: the filesystem is injected, so the POSIX branch is pinned from this Windows box.
        var dir = @"X:\vgs\tools\clangd_1.0.0";
        var expected = Path.Combine(dir, "bin", "clangd");
        var probed = new List<string>();

        var found = ClangdLocator.FindInToolsRoot(
            @"X:\vgs\tools",
            listToolDirectories: _ => new[] { dir },
            fileExists: p => { probed.Add(p); return p == expected; },
            isWindows: false);

        Assert.That(found, Is.EqualTo(expected), "a POSIX clangd carries no .exe");
        Assert.That(probed, Is.EqualTo(new[] { expected }),
            "off Windows the probe must look for bin/clangd and nothing else");
    }

    [Test]
    public void ToolsProbe_MissingRoot_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vgs_tools_missing_{Guid.NewGuid():N}");

        string? found = "sentinel";
        Assert.DoesNotThrow(() => found = ClangdLocator.FindInToolsRoot(root),
            "a missing tools root is the pre-first-install NORMAL, not an error");
        Assert.That(found, Is.Null);
    }

    // ---- The pinned LLVM install-dir sweep list. ----

    [Test]
    public void LlvmDirList_IsThePinnedSet()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("the pinned candidate list is Windows-only for now.");
        }

        // The env-var roots expand per machine, so the SHAPE is the pin: count, order, and the
        // stable tails. The VS root is injected, so those two entries can be asserted exactly.
        var dirs = ClangdLocator.BuildWindowsLlvmInstallDirectories(@"C:\FakeVS");

        Assert.That(dirs, Has.Count.EqualTo(9));
        Assert.That(dirs[0], Does.EndWith(@"\LLVM\bin"));   // %ProgramFiles% — the official installer default, deliberately first
        Assert.That(dirs[1], Does.EndWith(@"\LLVM\bin"));   // %ProgramFiles(x86)%
        Assert.That(dirs[0], Is.Not.EqualTo(dirs[1]));
        Assert.That(dirs[2], Is.EqualTo(@"C:\FakeVS\VC\Tools\Llvm\x64\bin"));
        Assert.That(dirs[3], Is.EqualTo(@"C:\FakeVS\VC\Tools\Llvm\bin"));
        Assert.That(dirs[4], Does.EndWith(@"\scoop\shims"));
        Assert.That(dirs[5], Does.EndWith(@"\scoop\apps\llvm\current\bin"));
        Assert.That(dirs[6], Is.EqualTo(@"C:\msys64\mingw64\bin"));
        Assert.That(dirs[7], Is.EqualTo(@"C:\msys64\ucrt64\bin"));
        Assert.That(dirs[8], Is.EqualTo(@"C:\msys64\clang64\bin"));
    }

    // ---- The settings read: key, consumer registration, and the resolved result. ----

    [Test]
    public void Locate_ReadsTheClangdSettingsKey_AndReturnsTheOverride()
    {
        var real = Path.Combine(Path.GetTempPath(), $"clangd_locate_{Guid.NewGuid():N}.exe");
        File.WriteAllText(real, "stand-in for a real clangd");
        try
        {
            var settings = new Mock<ISettingsService>();
            settings.Setup(s => s.Get(Key, "", SettingsScope.Effective)).Returns(real);

            var resolved = ClangdLocator.Locate(settings.Object);

            Assert.That(resolved, Is.EqualTo(real));
            settings.Verify(s => s.Get(Key, "", SettingsScope.Effective), Times.Once,
                "the override must be read from cpp.clangd.path — exactly the key the dialog writes");
        }
        finally
        {
            try { File.Delete(real); } catch { /* best effort */ }
        }
    }

    [Test]
    public void Locate_RegistersItselfAsTheConsumerOfTheClangdSettingsKey()
    {
        // The contract test (SettingsConsumerContractTests) demands a registered consumer for every
        // dialog key; this is the registration it forces, sitting next to the read above.
        ClangdLocator.Locate(new Mock<ISettingsService>().Object);

        Assert.That(SettingsConsumerRegistry.IsRegistered(Key), Is.True);
        Assert.That(SettingsConsumerRegistry.Consumers[Key], Does.Contain("ClangdLocator"));
    }

    [Test]
    public void Locate_WithNoSettingsService_StillProbes_AndDoesNotThrow()
    {
        // The registry may construct the locator before settings are wired; a null service means
        // "no override", not a crash.
        Assert.DoesNotThrow(() => ClangdLocator.Locate(null));
    }

    [Test]
    public void ClangdExecutableName_IsTheBareName_NotAWindowsSpecificFileName()
    {
        // Bare on purpose: ExecutableLocator applies PATHEXT, so hardcoding ".exe" here would
        // break the POSIX probe.
        Assert.That(ClangdLocator.ClangdExecutableName, Is.EqualTo("clangd"));
    }
}
