using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Pins <see cref="LldbDapLocator"/> — how the IDE decides WHICH lldb-dap launches a native debug
/// session. The structural sibling of <see cref="ClangdLocatorTests"/>: the same override rule
/// (<c>cpp.lldbDap.path</c> wins only when it names an existing file), the same auto-probe CHAIN
/// (the <c>~/.vgs/tools</c> copy, then PATH, then known install directories), every link an
/// injected seam.
///
/// <para><b>Null is load-bearing here, more than for clangd.</b>
/// <c>DebugAdapterDescriptor.LldbDap</c>'s factory pattern-matches
/// <c>resolveExecutable() is string p</c> — an empty string is a string, so <c>""</c> on a miss
/// would compose a launch command around an empty filename and fail at spawn instead of reporting
/// "not installed". Every miss path must answer null; <see cref="Chain_AllEmpty_ReturnsNull"/>
/// pins it.</para>
///
/// <para><b>And no probe may ever SPAWN lldb-dap</b> — <c>lldb-dap --version</c> parks on stdin
/// waiting for a DAP client and hangs. The sweep is file-existence checks only;
/// <see cref="KnownDirs_NeverSpawnAnything"/> pins that every probe in the known-dirs sweep is a
/// call through the injected existence func and nothing else.</para>
/// </summary>
[TestFixture]
public class LldbDapLocatorTests
{
    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p);
    }

    // ---- The auto-probe chain: override → ~/.vgs/tools → PATH → known install dirs. ----
    // Each link is asserted against its immediate successor, so reordering any two adjacent links
    // fails exactly one of these.

    [Test]
    public void Chain_OverrideBeatsToolsDir()
    {
        var configured = @"C:\my\lldb-dap.exe";
        var toolsRuns = 0;

        var resolved = LldbDapLocator.Resolve(
            configured,
            fileExists: Existing(configured),
            toolsProbe: () => { toolsRuns++; return @"C:\vgs\tools\lldb-dap_1.0.0\bin\lldb-dap.exe"; },
            pathProbe: () => null,
            knownDirsProbe: () => null);

        Assert.That(resolved, Is.EqualTo(configured),
            "an explicit user override outranks even the IDE's own installed copy");
        Assert.That(toolsRuns, Is.Zero, "an existing override must short-circuit the whole probe chain");
    }

    [Test]
    public void Chain_ToolsDirBeatsPath()
    {
        var tools = @"C:\vgs\tools\lldb-dap_22.1.0\bin\lldb-dap.exe";
        var pathRuns = 0;

        var resolved = LldbDapLocator.Resolve(
            null,
            fileExists: Existing(),
            toolsProbe: () => tools,
            pathProbe: () => { pathRuns++; return @"C:\onpath\lldb-dap.exe"; },
            knownDirsProbe: () => null);

        Assert.That(resolved, Is.EqualTo(tools),
            "the IDE-installed lldb-dap (a pinned, hash-verified release) must outrank whatever happens to be on PATH");
        Assert.That(pathRuns, Is.Zero, "a tools-root hit must short-circuit the PATH search");
    }

    [Test]
    public void Chain_PathBeatsKnownDirs()
    {
        var onPath = @"C:\onpath\lldb-dap.exe";
        var knownDirsRuns = 0;

        var resolved = LldbDapLocator.Resolve(
            null,
            fileExists: Existing(),
            toolsProbe: () => null,
            pathProbe: () => onPath,
            knownDirsProbe: () => { knownDirsRuns++; return @"C:\winlibs\mingw64\bin\lldb-dap.exe"; });

        Assert.That(resolved, Is.EqualTo(onPath),
            "PATH is the user's own arrangement; the known-install-dir sweep is a fallback, not a peer");
        Assert.That(knownDirsRuns, Is.Zero, "a PATH hit must short-circuit the known-dirs sweep");
    }

    [Test]
    public void Chain_AllEmpty_ReturnsNull()
    {
        var resolved = LldbDapLocator.Resolve(
            "",
            fileExists: Existing(),
            toolsProbe: () => null,
            pathProbe: () => null,
            knownDirsProbe: () => null);

        // Null, NEVER "": DebugAdapterDescriptor.LldbDap's factory pattern-matches
        // `resolveExecutable() is string p`, and an empty string would satisfy it and compose a
        // launch command around an empty filename. Null is the honest pre-install answer the
        // descriptor turns into "adapter not installed".
        Assert.That(resolved, Is.Null,
            "every miss path must answer null — an empty string would slip through the " +
            "descriptor's `is string` match and fail at spawn instead of reporting not-installed");
    }

    // ---- The ~/.vgs/tools probe: versioned dirs under the tools root, NUMERIC-highest wins. ----

    /// <summary>Creates <c>root/dirName/bin/&lt;lldb-dap binary&gt;</c> and returns the binary's path.</summary>
    private static string StageToolsInstall(string root, string dirName)
    {
        var bin = Path.Combine(root, dirName, "bin");
        Directory.CreateDirectory(bin);
        var exe = Path.Combine(bin, OperatingSystem.IsWindows() ? "lldb-dap.exe" : "lldb-dap");
        File.WriteAllText(exe, "stand-in for a real lldb-dap — only its PATH is under test");
        return exe;
    }

    [Test]
    public void ToolsProbe_PicksHighestNumericVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"vgs_lldb_tools_{Guid.NewGuid():N}");
        try
        {
            var newer = StageToolsInstall(root, "lldb-dap_22.1.0");
            StageToolsInstall(root, "lldb-dap_9.0.0");

            var found = LldbDapLocator.FindInToolsRoot(root);

            // NUMERIC comparison is the point: an ordinal string sort ranks "lldb-dap_9..." above
            // "lldb-dap_22..." and would pin the probe to an ancient install forever.
            Assert.That(found, Is.EqualTo(newer));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best effort */ }
        }
    }

    [Test]
    public void ToolsProbe_ProbesBinThenRootExeLayouts()
    {
        // Pure: the filesystem is injected, so both layouts are pinned without staging a disk.
        var dir = @"X:\vgs\tools\lldb-dap_1.0.0";
        var inBin = Path.Combine(dir, "bin", "lldb-dap.exe");
        var atRoot = Path.Combine(dir, "lldb-dap.exe");
        var probed = new List<string>();

        var found = LldbDapLocator.FindInToolsRoot(
            @"X:\vgs\tools",
            listToolDirectories: _ => new[] { dir },
            fileExists: p => { probed.Add(p); return p == atRoot; },
            isWindows: true);

        Assert.That(found, Is.EqualTo(atRoot),
            "an archive that unpacks lldb-dap.exe at the versioned dir's root must still be found");
        Assert.That(probed, Is.EqualTo(new[] { inBin, atRoot }),
            "bin\\lldb-dap.exe is the canonical layout and must be probed FIRST; the root-level " +
            "exe is the tolerated fallback, probed second");
    }

    // ---- The pinned known-install-dir sweep list. ----

    [Test]
    public void KnownDirs_IncludeWinlibsMingw64()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Ignore("the pinned candidate list is Windows-only for now.");
        }

        // Exact list: the clangd LLVM sweep list (itself exactly pinned by
        // ClangdLocatorTests.LlvmDirList_IsThePinnedSet) plus ONE appended entry — winlibs, which
        // ships lldb-dap where the official LLVM Windows installer does not. Appended to the lldb
        // list ONLY: the two lists are deliberately separate, and clangd's must not grow it.
        var dirs = LldbDapLocator.BuildKnownInstallDirectories(@"C:\FakeVS");
        var clangd = ClangdLocator.BuildWindowsLlvmInstallDirectories(@"C:\FakeVS");

        Assert.That(dirs, Has.Count.EqualTo(clangd.Count + 1));
        Assert.That(dirs.Take(clangd.Count), Is.EqualTo(clangd),
            "keep-in-sync: the lldb-dap sweep list IS the clangd LLVM list (plus winlibs) — if " +
            "this fails, the two lists have drifted apart");
        Assert.That(dirs[dirs.Count - 1], Is.EqualTo(@"C:\winlibs\mingw64\bin"),
            "winlibs is the one lldb-only entry, appended last (the sweep is a last resort)");
    }

    [Test]
    public void KnownDirs_NeverSpawnAnything()
    {
        // ⚠ lldb-dap CANNOT be probed by spawning it: "--version" parks on stdin waiting for a
        // DAP client and hangs. The sweep must be file-existence checks ONLY. Every probe the
        // sweep makes is recorded here through the injected existence func; the assertions pin
        // that the whole sweep — including the hit — is explainable by those calls alone, i.e.
        // nothing but knownDir\candidate existence checks ever happens.
        var dirs = LldbDapLocator.BuildKnownInstallDirectories(@"C:\FakeVS");
        var winlibsHit = @"C:\winlibs\mingw64\bin\lldb-dap.exe";
        var probed = new List<string>();

        var found = LldbDapLocator.FindInKnownInstallDirectories(
            directories: dirs,
            pathExtValue: ".exe",
            fileExists: p => { probed.Add(p); return string.Equals(p, winlibsHit, StringComparison.OrdinalIgnoreCase); },
            isWindows: true);

        Assert.That(found, Is.EqualTo(winlibsHit),
            "the sweep must resolve lldb-dap through the file-existence probe alone");
        Assert.That(probed, Has.Count.EqualTo(dirs.Count),
            "one existence check per candidate directory — nothing skipped, nothing extra");
        Assert.That(probed, Is.All.EndsWith(@"\lldb-dap.exe"),
            "every probe is a knownDir\\lldb-dap.exe existence check — never a spawn, never " +
            "anything else");
    }
}
