using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using NUnit.Framework;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

/// <summary>
/// Pins <see cref="ExecutableLocator"/> — the PATH search that turns a tool NAME into a full path.
///
/// <para><b>Why this fixture exists.</b> The search it replaces
/// (<c>ShellProfileDetector.FindOnPath</c>) probed <c>Path.Combine(dir, name)</c> literally, which
/// only ever worked because every caller passed an explicit <c>"pwsh.exe"</c>/<c>"bash.exe"</c>.
/// A bare <c>"clangd"</c> would have silently found nothing on Windows — exactly the silent-failure
/// class this phase exists to kill. <see cref="ExecutableLocator.FindIn"/>'s PATHEXT resolution is
/// therefore the load-bearing behavior here, and it is asserted directly.</para>
///
/// <para>Every environmental input (PATH, PATHEXT, file existence, OS) is an explicit parameter of
/// the pure <see cref="ExecutableLocator.FindIn"/>/<see cref="ExecutableLocator.CandidateNames"/>
/// core, so these tests pin the rules on any machine — clangd is not installed on the dev box, and
/// a probe that depended on it being installed would be untestable. The <c>Find_</c>-prefixed tests
/// cover the thin environment-reading wrapper separately, against a real file on a real PATH.</para>
/// </summary>
[TestFixture]
public class ExecutableLocatorTests
{
    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>A fileExists probe that answers true for exactly the given full paths.</summary>
    private static Func<string, bool> Existing(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return p => set.Contains(p);
    }

    private const string WinPathExt = ".COM;.EXE;.BAT;.CMD";

    // ──────────────────────────────────────────────────────────────────
    //  CandidateNames — the PATHEXT rule
    // ──────────────────────────────────────────────────────────────────

    [Test]
    public void CandidateNames_BareName_OnWindows_ResolvesThroughPathExt_AndNotLiterally()
    {
        var names = ExecutableLocator.CandidateNames("clangd", WinPathExt, isWindows: true);

        // Exact sequence, not "contains": these names are combined into a path that is handed to
        // Process.Start, and their ORDER decides which of two co-located files wins.
        // The bare "clangd" is deliberately ABSENT — on Windows an extension in PATHEXT is what
        // makes a file executable by name, so a literal extensionless file is not a candidate.
        Assert.That(names, Is.EqualTo(new[] { "clangd.COM", "clangd.EXE", "clangd.BAT", "clangd.CMD" }));
    }

    [Test]
    public void CandidateNames_NameAlreadyCarryingAPathExtExtension_IsLiteralOnly()
    {
        // Expanding "pwsh.exe" to "pwsh.exe.COM" etc. would be nonsense; the literal must be the
        // only candidate.
        Assert.That(ExecutableLocator.CandidateNames("pwsh.exe", WinPathExt, isWindows: true),
            Is.EqualTo(new[] { "pwsh.exe" }));
    }

    // These three names ARE ShellProfileDetector's three call sites (:114, :209, :324) — the only
    // callers FindOnPath ever had. Each carries an extension in PATHEXT, so each is probed
    // literally and only literally: the same single candidate the deleted FindOnPath probed, in a
    // search that still walks PATH in order with the same existence check. So the lift selects the
    // same FILE for the terminal profiles; Find now additionally returns it as an absolute path,
    // which is identical for the absolute PATH entries these shells actually live under. Pinned
    // here rather than reasoned about once in a review.
    [TestCase("pwsh.exe")]
    [TestCase("bash.exe")]
    [TestCase("nu.exe")]
    public void CandidateNames_TheLiftedShellProfileDetectorCallSites_AreUnaffected(string name)
    {
        Assert.That(ExecutableLocator.CandidateNames(name, WinPathExt, isWindows: true),
            Is.EqualTo(new[] { name }));
    }

    [Test]
    public void CandidateNames_ExtensionMatchIsCaseInsensitive()
    {
        // PATHEXT is conventionally upper-case; the executable name is conventionally lower-case.
        Assert.That(ExecutableLocator.CandidateNames("bash.EXE", ".com;.exe", isWindows: true),
            Is.EqualTo(new[] { "bash.EXE" }));
    }

    [Test]
    public void CandidateNames_ExtensionNotInPathExt_IsTreatedAsAStem()
    {
        // "tool.old" is not executable by extension, so it is a stem to resolve through PATHEXT
        // rather than a name to probe literally — the same rule as a bare name.
        Assert.That(ExecutableLocator.CandidateNames("tool.old", ".EXE", isWindows: true),
            Is.EqualTo(new[] { "tool.old.EXE" }));
    }

    [Test]
    public void CandidateNames_OnNonWindows_IsLiteralOnly()
    {
        // PATHEXT is a Windows concept; POSIX executables carry no extension.
        Assert.That(ExecutableLocator.CandidateNames("clangd", WinPathExt, isWindows: false),
            Is.EqualTo(new[] { "clangd" }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase(";;;")]      // parses to nothing, which is not the same as being unset
    [TestCase(" ; ; ")]
    public void CandidateNames_UnusablePathExt_FallsBackToBuiltInDefaults(string? pathExt)
    {
        var names = ExecutableLocator.CandidateNames("clangd", pathExt, isWindows: true);

        // Since the bare name is not a candidate, an empty extension list would mean NO candidates
        // and a search that silently never matches. The fallback must survive a PATHEXT that is
        // present but yields nothing, not merely one that is absent.
        Assert.That(names, Is.EqualTo(new[] { "clangd.COM", "clangd.EXE", "clangd.BAT", "clangd.CMD" }),
            "an unusable PATHEXT must not silently disable the probe entirely");
    }

    [Test]
    public void CandidateNames_PathExtEntries_AreTrimmedDottedAndDeduplicated()
    {
        var names = ExecutableLocator.CandidateNames("clangd", " .EXE ; EXE ;; .CMD ", isWindows: true);

        Assert.That(names, Is.EqualTo(new[] { "clangd.EXE", "clangd.CMD" }));
    }

    // ──────────────────────────────────────────────────────────────────
    //  FindIn — the search
    // ──────────────────────────────────────────────────────────────────

    [Test]
    public void FindIn_BareName_FindsTheExeOnWindows()
    {
        // THE test this class exists for: probing "clangd" must find "clangd.exe".
        var found = ExecutableLocator.FindIn(
            "clangd",
            pathValue: @"C:\windows;C:\tools\llvm",
            pathExtValue: WinPathExt,
            fileExists: Existing(@"C:\tools\llvm\clangd.exe"), // as the file is really spelled on disk
            isWindows: true);

        // The extension's CASE comes from PATHEXT (".EXE"), not from the file system — the probe
        // constructs the name it looks for and returns that string. Harmless: Windows paths are
        // case-insensitive, so this path opens the same file (which is exactly why the probe found
        // it above). Pinned ordinally so a future change to that construction is a deliberate one.
        Assert.That(found, Is.EqualTo(@"C:\tools\llvm\clangd.EXE"));
    }

    [Test]
    public void FindIn_ExtensionlessShimBesideTheExe_TheExeWins()
    {
        // THE hazard: MSYS2 / Cygwin / git-for-windows install extensionless shims beside real
        // .exes, so this pair in one directory is an ordinary shape — not a contrivance. Probing
        // the literal name first would return the shim, which Windows' own name resolution would
        // never select, and the caller would spawn a plausible-looking path that does not work.
        var found = ExecutableLocator.FindIn(
            "clangd",
            pathValue: @"C:\msys64\usr\bin",
            pathExtValue: WinPathExt,
            fileExists: Existing(
                @"C:\msys64\usr\bin\clangd",       // the extensionless shim
                @"C:\msys64\usr\bin\clangd.exe"),  // the real executable
            isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\msys64\usr\bin\clangd.EXE"));
    }

    [Test]
    public void FindIn_OnNonWindows_AnExtensionlessFileIsTheAnswer()
    {
        // The mirror image: dropping the bare name is a WINDOWS rule. On POSIX an extensionless
        // file is exactly what an executable looks like, so it must still be found.
        var expected = Path.Combine("/usr/bin", "clangd");

        Assert.That(ExecutableLocator.FindIn(
            "clangd", "/usr/bin", WinPathExt, Existing(expected), isWindows: false),
            Is.EqualTo(expected));
    }

    [Test]
    public void FindIn_EarlierPathDirectoryWins_EvenAgainstAHigherPriorityExtensionLater()
    {
        // Directory-major search: PATH order outranks PATHEXT order, matching how Windows resolves a
        // command. A candidate-major loop would wrongly return the .EXE from the later directory.
        var found = ExecutableLocator.FindIn(
            "clangd",
            pathValue: @"C:\first;C:\second",
            pathExtValue: WinPathExt,
            fileExists: Existing(@"C:\first\clangd.CMD", @"C:\second\clangd.EXE"),
            isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\first\clangd.CMD"));
    }

    [Test]
    public void FindIn_WithinOneDirectory_PathExtOrderDecides()
    {
        var found = ExecutableLocator.FindIn(
            "clangd",
            pathValue: @"C:\tools",
            pathExtValue: WinPathExt,
            fileExists: Existing(@"C:\tools\clangd.CMD", @"C:\tools\clangd.EXE"),
            isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\tools\clangd.EXE"), ".EXE precedes .CMD in PATHEXT");
    }

    [Test]
    public void FindIn_NothingMatches_ReturnsNull()
    {
        // The real answer on this dev box: clangd is not installed.
        Assert.That(ExecutableLocator.FindIn(
            "clangd", @"C:\windows;C:\tools", WinPathExt, Existing(), isWindows: true), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    public void FindIn_NoPathAtAll_ReturnsNull(string? pathValue)
    {
        Assert.That(ExecutableLocator.FindIn(
            "clangd", pathValue, WinPathExt, Existing(@"C:\tools\clangd.exe"), isWindows: true), Is.Null);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void FindIn_NoExecutableName_ReturnsNull(string? executable)
    {
        Assert.That(ExecutableLocator.FindIn(
            executable, @"C:\tools", WinPathExt, _ => true, isWindows: true), Is.Null,
            "a blank name must not resolve to the directory itself");
    }

    [Test]
    public void FindIn_TrimsPathEntries_AndSkipsEmptyOnes()
    {
        var found = ExecutableLocator.FindIn(
            "clangd",
            pathValue: @" ;; C:\tools ;",
            pathExtValue: WinPathExt,
            fileExists: Existing(@"C:\tools\clangd.exe"),
            isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\tools\clangd.EXE")); // .EXE casing: see FindIn_BareName_FindsTheExeOnWindows
    }

    [Test]
    public void FindIn_OnNonWindows_UsesColonSeparator_AndNoExtension()
    {
        // Path.Combine uses the HOST's separator, so on this Windows box the expected hit is spelled
        // with a backslash. Building it the same way keeps the test about what it is actually
        // pinning: PATH split on ':' (a ';' split would make the whole value one unusable entry) and
        // no extension appended. The POSIX separator itself is the runtime's job, not this class's.
        var expected = Path.Combine("/usr/local/bin", "clangd");

        var found = ExecutableLocator.FindIn(
            "clangd",
            pathValue: "/usr/bin:/usr/local/bin",
            pathExtValue: WinPathExt, // set, and must still be ignored off Windows
            fileExists: Existing(expected),
            isWindows: false);

        Assert.That(found, Is.EqualTo(expected));
    }

    [Test]
    public void FindIn_AnInvalidPathEntry_DoesNotThrow_AndTheSearchContinues()
    {
        // A malformed PATH entry is common on real machines; it must not abort the whole probe.
        Func<string, bool> exists = p =>
            p.Contains("bad", StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("boom")
                : string.Equals(p, @"C:\good\clangd.exe", StringComparison.OrdinalIgnoreCase);

        var found = ExecutableLocator.FindIn(
            "clangd", @"C:\bad;C:\good", WinPathExt, exists, isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\good\clangd.EXE"));
    }

    // ──────────────────────────────────────────────────────────────────
    //  FindInDirectories — the explicit-directory-list search (no PATH)
    // ──────────────────────────────────────────────────────────────────

    [Test]
    public void FindInDirectories_ProbesEachDirInOrder_WithCandidateNames()
    {
        var probed = new List<string>();
        Func<string, bool> recording = p =>
        {
            probed.Add(p);
            return string.Equals(p, @"C:\second\clangd.EXE", StringComparison.Ordinal);
        };

        var found = ExecutableLocator.FindInDirectories(
            "clangd",
            new[] { @"C:\first", @"C:\second" },
            WinPathExt,
            recording,
            isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\second\clangd.EXE"));
        // Directory-major with PATHEXT applied inside each directory, stopping at the first hit —
        // the exact probe sequence is the contract, mirroring FindIn's. The two candidates after
        // the hit (second\clangd.BAT, second\clangd.CMD) must never be probed.
        Assert.That(probed, Is.EqualTo(new[]
        {
            @"C:\first\clangd.COM", @"C:\first\clangd.EXE", @"C:\first\clangd.BAT", @"C:\first\clangd.CMD",
            @"C:\second\clangd.COM", @"C:\second\clangd.EXE",
        }));
    }

    [Test]
    public void FindInDirectories_ReturnsAbsolutePaths()
    {
        // Same rule as Find: the hit is handed to Process.Start, where a relative path would
        // re-resolve against the CHILD's working directory. A relative candidate directory must
        // therefore yield an absolute result.
        var relativeHit = Path.Combine("toolbin", "clangd.EXE");

        var found = ExecutableLocator.FindInDirectories(
            "clangd",
            new[] { "toolbin" },
            WinPathExt,
            p => string.Equals(p, relativeHit, StringComparison.OrdinalIgnoreCase),
            isWindows: true);

        Assert.That(found, Is.Not.Null);
        Assert.That(Path.IsPathRooted(found), Is.True,
            $"the hit must be absolute, but was \"{found}\"");
        Assert.That(found, Is.EqualTo(Path.GetFullPath(relativeHit)));
    }

    [Test]
    public void FindInDirectories_SkipsDirsThatThrow()
    {
        // Mirrors FindIn's per-directory guard: one inaccessible candidate directory (dead network
        // share, ACL) must not abort the sweep — the remaining directories are still searched.
        Func<string, bool> exists = p =>
            p.Contains("bad", StringComparison.OrdinalIgnoreCase)
                ? throw new IOException("boom")
                : string.Equals(p, @"C:\good\clangd.exe", StringComparison.OrdinalIgnoreCase);

        var found = ExecutableLocator.FindInDirectories(
            "clangd", new[] { @"C:\bad", @"C:\good" }, WinPathExt, exists, isWindows: true);

        Assert.That(found, Is.EqualTo(@"C:\good\clangd.EXE"));
    }

    // ──────────────────────────────────────────────────────────────────
    //  Find — the environment-reading wrapper
    // ──────────────────────────────────────────────────────────────────

    [Test]
    [NonParallelizable] // mutates the process-wide PATH
    public void Find_ReadsTheRealEnvironment_AndHonorsPathExtForABareName()
    {
        // End-to-end proof that the wrapper passes the real PATH, the real PATHEXT and the real OS
        // flag through — the parts FindIn's injected parameters cannot cover. clangd is not
        // installed here, so this stages a real file of our own instead.
        if (!IsWindows)
        {
            Assert.Ignore("PATHEXT expansion is Windows-only.");
        }

        var dir = Path.Combine(Path.GetTempPath(), $"ExecutableLocator_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var bareName = "vgs_probe_tool";
        var exe = Path.Combine(dir, bareName + ".exe");
        File.WriteAllText(exe, "not a real executable — only its NAME is under test");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", dir + ";" + originalPath);

            // IgnoreCase because the extension is spelled from this machine's PATHEXT (".EXE"),
            // while the staged file is ".exe" — the same file either way. Still a whole-string
            // equality: the directory and stem must match exactly.
            Assert.That(ExecutableLocator.Find(bareName), Is.EqualTo(exe).IgnoreCase,
                "a bare name must resolve to the .exe via PATHEXT");
            Assert.That(ExecutableLocator.Find(bareName + ".exe"), Is.EqualTo(exe),
                "an explicit .exe name must keep working, spelled exactly as the caller wrote it " +
                "(the ShellProfileDetector call pattern)");
            Assert.That(ExecutableLocator.Find("vgs_tool_that_does_not_exist"), Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            try { Directory.Delete(dir, true); } catch { /* best effort */ }
        }
    }

    [Test]
    [NonParallelizable] // mutates the process-wide PATH and working directory
    public void Find_RelativePathEntry_StillYieldsAnAbsolutePath()
    {
        // A relative PATH entry is legal and resolves against the CURRENT working directory. Task 12
        // spawns whatever comes back, so a relative result would silently re-resolve against the
        // child's working directory instead. Staged for real rather than injected: FindIn cannot see
        // a working directory at all, so only the wrapper can be held to this.
        var root = Path.Combine(Path.GetTempPath(), $"ExecutableLocator_rel_{Guid.NewGuid():N}");
        var toolDir = Path.Combine(root, "toolbin");
        Directory.CreateDirectory(toolDir);
        var bareName = "vgs_relative_probe";
        var exe = Path.Combine(toolDir, bareName + ".exe");
        File.WriteAllText(exe, "not a real executable — only its PATH is under test");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalCwd = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = root;
            Environment.SetEnvironmentVariable("PATH", "toolbin"); // relative, on purpose

            var found = ExecutableLocator.Find(bareName);

            Assert.That(found, Is.Not.Null, "a relative PATH entry must still be searched");
            Assert.That(Path.IsPathRooted(found), Is.True,
                $"the hit must be absolute, but was \"{found}\"");
            // The exact file, not merely something rooted: temp paths can differ from the staged
            // spelling, so compare the resolved forms.
            Assert.That(Path.GetFullPath(found!), Is.EqualTo(Path.GetFullPath(exe)).IgnoreCase);
        }
        finally
        {
            Environment.CurrentDirectory = originalCwd;
            Environment.SetEnvironmentVariable("PATH", originalPath);
            try { Directory.Delete(root, true); } catch { /* best effort */ }
        }
    }
}
