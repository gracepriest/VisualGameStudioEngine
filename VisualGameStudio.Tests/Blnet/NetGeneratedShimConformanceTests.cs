using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BasicLang.Compiler.CodeGen.Net;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Blnet;

/// <summary>
/// §12.3's generated-shim conformance rows — P2a-2 Task 12.
///
/// <para>⛔ <b>NOT <c>BlnetConformanceTests</c>.</b> That file is the FROZEN P0 §12.2 suite: 16
/// hand-shim scenarios driven by a C++ harness whose binder expects seven <c>blnet_test_*</c>
/// exports a GENERATED shim does not have. Its scenarios stay exactly as they are. The names are
/// four letters apart on purpose-avoidance grounds: a careless
/// <c>--filter "…ConformanceTests"</c> would re-run 16 AOT-backed processes, so read the name
/// before editing.</para>
///
/// <para><b>The template is the publish-and-run pipeline</b>
/// (<c>NetShimPipelineTests.Milestone_</c> / <c>Section85_</c>), because a §12.3 row is a
/// BasicLang PROGRAM built through the shipping CLI, not a C++ TU.
/// <c>NetShimPipelineFixture.Run</c> already carries the async-read-before-<c>WaitForExit</c>
/// ordering and the 60 s hang guard the plan asks for.</para>
///
/// <para>⛔⛔ <b>THE RULE FOR EVERY ROW IN THIS FILE — shape substitution is the failure mode.</b>
/// When a §12.3 row will not compile, the natural repair is to rewrite it into a shape that does,
/// and every such rewrite silently deletes the coverage the row existed to add: the suite goes
/// fully green while the defect stays live. Author the row in §12.3's shape FIRST. If it will not
/// build or runs wrong, it is finished only as (a) a PINNED DIVERGENCE asserting the exact current
/// diagnostic/exit/output, or (b) an explicitly deferred row naming its chip — never as a
/// rewritten shape that passes. Record the rejected shape next to any row you substituted.</para>
///
/// <para>⚠ <b>Builds are expensive (~25 s plus an AOT publish), so programs are FEW and RICH.</b>
/// Adding a scenario to an existing program costs nothing; adding a program costs a publish.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class NetGeneratedShimConformanceTests
{
    private static readonly ConcurrentDictionary<string, Lazy<SharedBuild>> Builds = new();
    private static readonly ConcurrentBag<string> Dirs = new();

    private sealed record SharedBuild(string Dir, CppProjectBuildResult Result);

    /// <summary>
    /// Build a program once per fixture run and memoize it. Mirrors
    /// <c>NetShimPipelineTests.BuildOnce</c> rather than sharing it, because that one is private
    /// and its <c>[OneTimeTearDown]</c> owns its own directories.
    /// </summary>
    private static SharedBuild BuildOnce(string projectName, IReadOnlyDictionary<string, string> files)
    {
        var key = projectName;
        return Builds.GetOrAdd(key, _ => new Lazy<SharedBuild>(() =>
        {
            var dir = NetShimPipelineFixture.NewTempDir("blnet-conf-");
            Dirs.Add(dir);
            foreach (var file in files)
                File.WriteAllText(Path.Combine(dir, file.Key), file.Value);

            var projectPath = NetShimPipelineFixture.WriteProject(dir, projectName);
            var stopwatch = Stopwatch.StartNew();
            var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");
            stopwatch.Stop();
            TestContext.Out.WriteLine(
                $"{projectName}: conformance build took {stopwatch.Elapsed.TotalSeconds:F1}s");
            foreach (var message in result.Messages)
                TestContext.Out.WriteLine("  " + message);
            return new SharedBuild(dir, result);
        })).Value;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        Builds.Clear();
        while (Dirs.TryTake(out var dir))
            NetShimPipelineFixture.TryDeleteDir(dir);
    }

    /// <summary>
    /// Run a built program that is EXPECTED to fail, returning its exit code and both streams
    /// instead of asserting success.
    ///
    /// <para><c>NetShimPipelineFixture.Run</c> asserts <c>ExitCode == 0</c>, which is right for a
    /// happy-path row and unusable for §8.2's handle-0 rule, where dying IS the specified
    /// behaviour. Note <c>NetProxyEmitter.StartupFailureExitCode</c> (3) is asserted NOWHERE in
    /// the suite, so a row that pins an exit code needs this path.</para>
    ///
    /// <para>⛔ Both streams are drained CONCURRENTLY. Reading stdout to end and only then
    /// reading stderr deadlocks if the child fills the stderr pipe buffer while we are blocked on
    /// stdout — and the WaitForExit timeout does not save you, because the block is in the read,
    /// not the wait. A failing program is exactly the one likely to write a lot to stderr.</para>
    /// </summary>
    private static (int ExitCode, string StdOut, string StdErr) RunAllowingFailure(string exePath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            Assert.Fail("the built program did not exit within 60s — a §8.2 row must fail fast, "
                        + "not hang.");
        }

        return (process.ExitCode,
                outTask.GetAwaiter().GetResult().Replace("\r\n", "\n"),
                errTask.GetAwaiter().GetResult().Replace("\r\n", "\n"));
    }

    private static void AssertBuilt(CppProjectBuildResult result, string label)
    {
        Assert.That(result.Success, Is.True,
            label + " did not build.\n" + NetShimPipelineFixture.Diagnostics(result)
            + "\n" + result.RawToolchainOutput);
        Assert.That(File.Exists(result.ExecutablePath), Is.True, result.ExecutablePath);
    }

    // =====================================================================================
    // §12.3 — a NAMED property setter, and handle identity across statements.
    // =====================================================================================

    /// <summary>
    /// THE property row, and the reason it can exist at all.
    ///
    /// <para>§12.3 wants a named property WRITE proven at run level. The indexer-setter half was
    /// already run-proven (<c>Section85_</c>), but a named <c>set_X</c> was not — and writing one
    /// honestly needs the write to be OBSERVABLE, which needs the SAME handle in two statements.
    /// </para>
    ///
    /// <para>⛔ <b>Why the documented workaround cannot express this row.</b> The recorded way to
    /// use a non-curated .NET type without the §8.5 fifth-site fix is a static factory called
    /// INLINE — <c>Zoo.MakeDog().Speak()</c>. That mints a FRESH handle per call, so
    /// <c>Make().Value = 5</c> followed by <c>Make().Value</c> never touches the same object and
    /// a write-then-read row built on it would pass while observing nothing. That is precisely
    /// the shape substitution this fixture's header forbids.</para>
    ///
    /// <para><c>FileInfo</c> is one of the curated five, so <c>New FileInfo(…)</c> is legal
    /// anywhere. <c>fi.Create()</c> returns a <c>FileStream</c>, which is NOT curated — that local
    /// is admitted only because an inferred local off a member RESULT carries the handle marker,
    /// which <c>CppCapabilityChecker</c> began honouring in the same change that made this row
    /// writable. A real <c>FileStream</c> is seekable, so <c>Position</c> genuinely round-trips;
    /// <c>Stream.Null</c> would have read back 0 whether or not the setter ever crossed.</para>
    /// </summary>
    /// <summary>
    /// The property program's sources, hoisted so the §9.3 handshake row below can reuse the SAME
    /// memoized build instead of paying a second AOT publish. <see cref="BuildOnce"/> keys on the
    /// project name, so both callers must pass this exact dictionary.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PropertyProgram =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.bas"] = """
                Using System.IO

                Module Program
                 Sub Main()
                  Dim fi As New FileInfo("blnet_conf_probe.dat")
                  Dim s = fi.Create()
                  s.Position = 3
                  Console.WriteLine(s.Position)
                  s.Position = 11
                  Console.WriteLine(s.Position)
                  Console.WriteLine(s.CanWrite)
                  Console.WriteLine(Convert.ToInt32("A"c))
                 End Sub
                End Module
                """,
        };

    [Test]
    public void ANamedPropertySetter_CrossesAndTheGetterReadsItBack()
    {
        var built = BuildOnce("ConfProperty", PropertyProgram);

        AssertBuilt(built.Result, "the §12.3 named-property program");

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("3\n11\nTrue\n65\n"),
            "These are .NET's own answers for a seekable FileStream, asserted rather than "
            + "whatever the backend happens to produce. A repeated first value means the setter "
            + "never crossed and the getter is reading a stale or default Position; '0' twice "
            + "means the handle is not the same object across statements (the fresh-handle-per-"
            + "call failure the summary describes); a missing 'True' means the Boolean result row "
            + "regressed. The trailing 65 is §8.3's Char row crossing as an ARGUMENT to a real "
            + ".NET static (Convert.ToInt32) and coming back as Int32 — .NET's answer for 'A'.");
    }

    /// <summary>
    /// §12.3's Char row, non-ASCII half — authored as a PINNED DIVERGENCE, which is the only
    /// honest way to write it.
    ///
    /// <para>The plan specifies this row as parity and records the old symptom: <c>"é"c</c>
    /// printing 169 where .NET says 233, because the literal was emitted as multi-byte UTF-8
    /// inside a single-quoted C++ <c>char</c> and died BEFORE the §8.3 wire conversion. That
    /// symptom is STALE — the backend now REFUSES the literal outright, so authoring "169 vs 233"
    /// would fail for the wrong reason.</para>
    ///
    /// <para>⛔ The refusal is deliberate and is NOT a step toward widening: this backend
    /// represents Char as an 8-bit <c>char</c>, so U+00E9 and U+0429 would both truncate to A9 and
    /// compare EQUAL. A build error is the correct behaviour, and pinning it here stops a future
    /// "fix" from silently restoring the truncation. This row costs no publish — the capability
    /// checker refuses long before phase 5.</para>
    ///
    /// <para>⛔ <c>Convert.ToInt32</c>, never <c>AscW</c>/<c>ChrW</c>: those are CS0103 on the C#
    /// backend too, so a row written with them fails on both legs for an unrelated reason.</para>
    /// </summary>
    [Test]
    public void ANonAsciiCharLiteral_IsRefusedAtBuild_NotSilentlyTruncated()
    {
        var dir = NetShimPipelineFixture.NewTempDir("blnet-conf-char-");
        Dirs.Add(dir);
        File.WriteAllText(Path.Combine(dir, "Program.bas"), """
            Module Program
             Sub Main()
              Console.WriteLine(Convert.ToInt32("é"c))
             End Sub
            End Module
            """);

        var projectPath = NetShimPipelineFixture.WriteProject(dir, "ConfCharRefusal");
        var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");

        Assert.That(result.Success, Is.False,
            "a non-ASCII Char literal must FAIL the native build. If this starts succeeding, the "
            + "backend either widened Char (a real change — update this row to parity) or "
            + "reinstated the silent 8-bit truncation (a miscompile — U+00E9 and U+0429 both end "
            + "in A9 and would compare equal).");

        var text = NetShimPipelineFixture.Diagnostics(result) + "\n" + result.RawToolchainOutput;
        Assert.That(text, Does.Contain("U+00E9").And.Contain("above U+007F"),
            "the refusal must NAME the offending code point and the limit, so the message is "
            + "actionable. Got:\n" + text);
    }

    /// <summary>
    /// §8.2's handle-0 rule with <c>Nothing</c> as the RECEIVER.
    ///
    /// <para><c>Nothing</c> crosses as handle 0, and the generated shim guards every handle
    /// argument against 0 before consulting the table
    /// (<c>NetShimGeneratorTests.HandleArgumentsAreGuardedAgainstZeroBeforeTheTableIsConsulted</c>).
    /// So a call on a null receiver must fail in a CONTROLLED way — a detected bad handle — not
    /// by dereferencing 0 or by silently answering a default.</para>
    ///
    /// <para>⛔ <b>PINNED DIVERGENCE — the row does not reach runtime at all.</b> §12.3 wants this
    /// proven at run level and the plan expects a controlled failure. MEASURED: it does not
    /// COMPILE. <c>CType(Nothing, Stream)</c> emits <c>static_cast&lt;NetRef&gt;(nullptr)</c> and
    /// MSVC rejects it — <c>C2440: cannot convert from 'nullptr' to 'BasicLang::NetRef'</c>, with
    /// notes ruling out all three NetRef constructors. So §8.2's handle-0 rule is not implemented
    /// for <c>Nothing</c> in a handle slot: <c>MapType</c> answers a handle DEFAULT with
    /// <c>{}</c> (the empty handle), but a CAST to a handle type has no such arm.</para>
    ///
    /// <para>Two shapes were rejected before this one, and both are recorded rather than quietly
    /// swapped: bare <c>Dim s As Stream = Nothing</c> is <b>BL3001</b> (Nothing types as Object,
    /// so BasicLang demands the cast), and the cast form is the C2440 above. Chipped —
    /// see the chip for the repro. This test pins the CURRENT behaviour so the day it changes is
    /// deliberate; when the emission is fixed, replace it with the runtime row §12.3 actually
    /// asks for rather than deleting it.</para>
    /// </summary>
    [Test]
    public void NothingInAHandleSlot_DoesNotYetCompile_PinnedDivergence()
    {
        var dir = NetShimPipelineFixture.NewTempDir("blnet-conf-null-");
        Dirs.Add(dir);
        File.WriteAllText(Path.Combine(dir, "Program.bas"), """
            Using System.IO

            Module Program
             Sub Main()
              Dim s As Stream = CType(Nothing, Stream)
              Console.WriteLine("before")
              Console.WriteLine(s.CanRead)
              Console.WriteLine("after")
             End Sub
            End Module
            """);

        var projectPath = NetShimPipelineFixture.WriteProject(dir, "ConfNullReceiver");
        var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");
        var text = NetShimPipelineFixture.Diagnostics(result) + "\n" + result.RawToolchainOutput;

        Assert.That(result.Success, Is.False,
            "Nothing in a handle slot is currently expected NOT to build. If this starts "
            + "succeeding the emission was fixed — good — and this row should become the RUNTIME "
            + "row §12.3 asks for (call on a null receiver fails in a controlled way), not be "
            + "deleted.\n" + text);

        Assert.That(text, Does.Contain("NetRef"),
            "the failure must still be the nullptr-to-NetRef conversion. A DIFFERENT build "
            + "failure here means this row is now pinning something else entirely and is no "
            + "longer evidence about §8.2.\n" + text);
    }

    // =====================================================================================
    // §9.3 — the startup handshake's FAILURE modes.
    // =====================================================================================

    /// <summary>
    /// §9.3: a missing shim must fail the startup handshake with the SPECIFIED exit code.
    ///
    /// <para><c>NetProxyEmitter.StartupFailureExitCode</c> is 3, and its own doc comment says the
    /// handshake tests and the emitter "cannot disagree about it" — yet nothing in the suite
    /// asserted it. A constant no test reads is a constant the emitter can change freely.</para>
    ///
    /// <para>The generated startup TU loads the shim by BARE NAME, so the OS resolves it from the
    /// executable's own directory. Deleting it there is exactly the real-world failure: a build
    /// that succeeded but whose phase-7 deployment did not happen.</para>
    ///
    /// <para>⚠ The program runs in a COPY of the output directory, never the shared build's own.
    /// <see cref="BuildOnce"/> memoizes that directory and the property row above still needs it
    /// intact; deleting the shim in place would make the two tests order-dependent — green alone,
    /// red together, or vice versa. Costs no publish: the build is reused.</para>
    /// </summary>
    [Test]
    public void AMissingShim_FailsTheStartupHandshake_WithTheSpecifiedExitCode()
    {
        var built = BuildOnce("ConfProperty", PropertyProgram);
        AssertBuilt(built.Result, "the §12.3 named-property program");

        var sourceDir = Path.GetDirectoryName(built.Result.ExecutablePath)!;
        var sandbox = NetShimPipelineFixture.NewTempDir("blnet-conf-noshim-");
        Dirs.Add(sandbox);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(sandbox, Path.GetFileName(file)));

        var shimName = NetProxyEmitter.ShimModuleFileName(
            NetShimGenerator.ShimAssemblyName("ConfProperty"));
        var shimPath = Path.Combine(sandbox, shimName);

        Assert.That(File.Exists(shimPath), Is.True,
            "guard: the shim must be present in the copy before we remove it, or this test "
            + "proves nothing. Looked for " + shimName + " in " + sandbox);
        File.Delete(shimPath);

        var exe = Path.Combine(sandbox, Path.GetFileName(built.Result.ExecutablePath)!);
        var (exitCode, stdout, stderr) = RunAllowingFailure(exe);
        var dump = $"exit={exitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}";

        Assert.Multiple(() =>
        {
            Assert.That(exitCode, Is.EqualTo(NetProxyEmitter.StartupFailureExitCode),
                $"§9.3 specifies exit {NetProxyEmitter.StartupFailureExitCode} for a failed "
                + "startup handshake. 0 would mean the program ran happily without the .NET side, "
                + "which is worse than crashing.\n" + dump);
            Assert.That(stdout, Does.Not.Contain("3"),
                "the program must not have reached its first .NET call and printed a result.\n"
                + dump);
        });
    }
}
