using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    /// <param name="withProbe">
    /// Emit <c>NetShimPipelineFixture</c>'s probe assembly (<c>Aot.Probe</c>) into the project
    /// directory and reference it. ⛔ Use THIS, never a hand-rolled emitter: it compiles against
    /// the net8.0 REFERENCE pack, and the other emitter in this suite
    /// (<c>NetDelegateTests.ProbeDir.EmitAssembly</c>) compiles against IMPLEMENTATION assemblies,
    /// which bakes in a <c>System.Private.CoreLib</c> reference and makes every use of the probe's
    /// types CS0012 inside the generated shim.
    /// </param>
    /// <param name="extraItemGroupXml">Appended after the reference item group — e.g. a
    /// <c>&lt;NetProxy&gt;</c> declaration.</param>
    private static SharedBuild BuildOnce(
        string projectName, IReadOnlyDictionary<string, string> files,
        bool withProbe = false, string extraItemGroupXml = "")
    {
        var key = projectName;
        return Builds.GetOrAdd(key, _ => new Lazy<SharedBuild>(() =>
        {
            var dir = NetShimPipelineFixture.NewTempDir("blnet-conf-");
            Dirs.Add(dir);
            foreach (var file in files)
                File.WriteAllText(Path.Combine(dir, file.Key), file.Value);

            var itemGroupXml = extraItemGroupXml;
            if (withProbe)
            {
                var probe = NetShimPipelineFixture.EmitProbeAssembly(dir);
                itemGroupXml = NetShimPipelineFixture.ReferenceItemGroup(probe) + extraItemGroupXml;
            }

            var projectPath = NetShimPipelineFixture.WriteProject(dir, projectName, itemGroupXml);
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

    // =====================================================================================
    // §7.2 / §11.4 — an omitted member is a WARNING, and the project still ships.
    // =====================================================================================

    /// <summary>
    /// §12.3's "<c>&lt;NetProxy&gt;</c> omitted-member BL6026-and-still-builds" row, at RUN level.
    ///
    /// <para>The emit-level half was already proven — <c>NetSurfaceCollectorTests</c> asserts the
    /// omission, the single BL6026, its warning severity and its message. But it does so with a
    /// FAKE toolchain and phase 5 switched off, so nothing showed that a real project carrying
    /// that warning publishes a shim, links, and RUNS. "Still builds" was the untested half of the
    /// claim.</para>
    ///
    /// <para><c>&lt;NetProxy Include="Aot.Probe.AotProbe"/&gt;</c> declares a static class whose
    /// only member is <c>[RequiresDynamicCode]</c>, so §7.2 omits it and announces BL6026 — a
    /// DECLARED surface that ends up empty. The program meanwhile calls <c>Bag</c> through
    /// ordinary call-site reachability, so the build has real work to do. Both halves matter: a
    /// project that only had the omitted type would prove "builds" trivially, with no shim to
    /// publish and nothing to run.</para>
    ///
    /// <para>⚠ BL6026 is NOT BL6020. BL6020 is the AOT-hostile-member diagnostic on the ILC side;
    /// BL6026 is §7.2's omission, and §11.4 marks only BL6026 as always-a-warning. Asserting the
    /// wrong code here would pass for the wrong reason, since this same probe member is what the
    /// BL6020 fixture uses too.</para>
    /// </summary>
    [Test]
    public void AnOmittedMemberWarnsWithBl6026_AndTheProjectStillPublishesAndRuns()
    {
        var built = BuildOnce(
            "ConfOmitted",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Program.bas"] = """
                    Using Aot.Probe

                    Module Program
                     Sub Main()
                      Dim Vals = Bag.Values()
                      Console.WriteLine(Vals(0))
                      Console.WriteLine(Bag.Sum(Vals))
                     End Sub
                    End Module
                    """,
            },
            withProbe: true,
            extraItemGroupXml:
                "\n  <ItemGroup>\n    <NetProxy Include=\"Aot.Probe.AotProbe\" />\n  </ItemGroup>");

        AssertBuilt(built.Result, "the §7.2 omitted-member program");

        var bl6026 = built.Result.Diagnostics.Where(d => d.Code == "BL6026").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(bl6026, Is.Not.Empty,
                "the declared type's [RequiresDynamicCode] member must be omitted WITH a BL6026. "
                + "No BL6026 means either the omission stopped happening or it went silent — the "
                + "silent form is worse, because the member simply vanishes from the surface. "
                + "Diagnostics: " + NetShimPipelineFixture.Diagnostics(built.Result));
            Assert.That(bl6026.TrueForAll(d => d.IsWarning), Is.True,
                "§11.4 marks BL6026 as ALWAYS a warning — an omitted declared member must not "
                + "fail a native build. If this became an error the whole row is moot: the build "
                + "would already have failed above.");
        });

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("7\n24\n"),
            "the project carrying a BL6026 must still publish its shim, link, and produce .NET's "
            + "answers — 7 is Values()(0) and 24 is Sum(7+8+9). This is the half the emit-level "
            + "test could not reach: it ran with a fake toolchain and phase 5 off.");
    }

    // =====================================================================================
    // §8.4 — delegates, the row §12.3 calls mandatory.
    // =====================================================================================

    /// <summary>
    /// §12.3's result-bearing delegate row, plus the <c>long</c> and <c>void</c> shapes.
    ///
    /// <para>⛔ <b>This row is NOT a duplicate of Task 11's, and the difference is the whole
    /// point.</b> <c>NetDelegateTests.ARunningProgram_DispatchesAResultBearingDelegateInline</c>
    /// runs a native binary against a C++ STUB LAMBDA returning canned values — no .NET delegate
    /// is ever constructed, and the real startup TU is skipped. Here a BasicLang lambda becomes an
    /// actual <c>Aot.Probe.IntFn</c> inside the shim, is invoked by .NET, and its result crosses
    /// back.</para>
    ///
    /// <para><b>The <c>-9</c> before the <c>1</c> is the load-bearing part.</b> It proves the
    /// callback runs INLINE, during the call, rather than being queued and dispatched afterwards —
    /// a deferred implementation would print <c>1</c> first and still "work".</para>
    ///
    /// <para>⚠ <b>double/float are deliberately absent.</b> They pass §8.4's blittable-scalar gate
    /// and then TRUNCATE on the wire — .NET's 3 and 2.75 arrive as 2 and 2, with a clean build
    /// (chip <c>task_75064f2e</c>). Writing this row with only <c>int</c> is exactly the
    /// shape-substitution trap: the green tick would sit on top of a live numeric miscompile. The
    /// <c>long</c> and NEGATIVE values here are the cheap insurance — they would catch a width or
    /// sign error even though they cannot catch the floating-point one.</para>
    /// </summary>
    [Test]
    public void ADelegateCrossesAsARealDotNetDelegate_AndDispatchesInline()
    {
        var built = BuildOnce(
            "ConfDelegate",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Program.bas"] = """
                    Using Aot.Probe

                    Module Program
                     Sub Main()
                      ' EVERY lambda parameter is annotated because BasicLang does NOT infer them
                      ' from the target delegate's signature — measured: `Function(a, b) a - b` at
                      ' an IntFn slot is BL3001 "Cannot infer type for lambda parameter 'a'".
                      ' Required spelling, not a workaround.
                      Console.WriteLine(Callbacks.Fold(10, Function(a As Integer, b As Integer) a - b))
                      Console.WriteLine(Callbacks.Big(Function(v As Long) v - 1))
                      Console.WriteLine(Callbacks.Run(Sub(v As Integer) Console.WriteLine(v)))
                     End Sub
                    End Module
                    """,
            },
            withProbe: true);

        AssertBuilt(built.Result, "the §8.4 delegate program");

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("7\n-4000000001\n-9\n1\n"),
            "7 is Fold(10, a-b) = f(10,3) = 10-3, so the BasicLang lambda really became an IntFn "
            + ".NET invoked and the result came back. -4000000001 is the long slot carrying a "
            + "value no 32-bit path survives, and it is NEGATIVE so a sign error shows. The -9 "
            + "MUST precede the 1: that ordering is what proves the void callback dispatched "
            + "INLINE rather than being deferred until after the call returned.");
    }

    // =====================================================================================
    // §8.3 — out / ref SCALAR slots, written by real .NET and read back natively.
    // =====================================================================================

    /// <summary>
    /// §12.3's <c>ref</c>/<c>out</c> row at run level.
    ///
    /// <para>These were stub-proven only (<c>NetProxyStubRunTests</c>): the stub asserts the wire
    /// SHAPE — that a by-ref scalar travels as a pointer — but never that a real .NET method wrote
    /// through that pointer and the native caller read the new value back. Those are different
    /// claims, and only the second is what a user experiences.</para>
    ///
    /// <para><c>TryDouble</c> returns a Boolean AS WELL AS writing its <c>out</c> slot, so one call
    /// proves both directions at once: a shim that dropped the write entirely would still return
    /// <c>True</c> and, without the second line, look correct.</para>
    ///
    /// <para>⚠ Scalars are NATIVE here — no handle is involved — which is why this needs no
    /// capability work and is not blocked by anything. The gap was purely that nothing ran it end
    /// to end.</para>
    /// </summary>
    [Test]
    public void OutAndRefScalarSlots_AreWrittenByDotNet_AndReadBackNatively()
    {
        var built = BuildOnce(
            "ConfSlots",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Program.bas"] = """
                    Using Aot.Probe

                    Module Program
                     Sub Main()
                      Dim R As Integer = 0
                      Console.WriteLine(Slots.TryDouble(21, R))
                      Console.WriteLine(R)
                      Dim B As Integer = 10
                      Slots.Bump(B)
                      Console.WriteLine(B)
                     End Sub
                    End Module
                    """,
            },
            withProbe: true);

        AssertBuilt(built.Result, "the §8.3 out/ref scalar program");

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("True\n42\n15\n"),
            "True is TryDouble's ordinary Boolean result; 42 is its OUT slot, and a 0 there means "
            + "the shim called .NET but threw the write away — the exact failure a shape-only "
            + "stub test cannot see. 15 is the REF slot: 10 sent in, +5 applied by .NET, read "
            + "back natively. A 10 means the ref travelled by value.");
    }

    // =====================================================================================
    // §12.3's inertness row — naming a .NET type is not the same as USING one.
    // =====================================================================================

    /// <summary>
    /// The empty-surface <c>Try</c>/<c>Catch ex As Exception</c> program: one of §12.3's four
    /// explicitly called-out rows.
    ///
    /// <para><c>Exception</c> and <c>ArgumentException</c> are .NET names, and the C++ backend has
    /// a whole §11.1 NetException ladder for them — but this program calls no .NET MEMBER, so the
    /// surface is empty and the boundary must stay completely inert: no shim published, nothing
    /// deployed, and the throw/catch lowered natively to <c>std::runtime_error</c> and
    /// <c>what()</c>.</para>
    ///
    /// <para><b>The analyzer half was already proven</b> —
    /// <c>NetFlipTests.ThrowAndCatchWithMessage_InsideAGenericBody_DrawsNoNetFindings</c> asserts
    /// no .NET diagnostics for this shape. But it runs at <c>Analyze</c> level. Nothing showed
    /// that such a program BUILDS and RUNS with no shim beside it, which is the actual promise: a
    /// user who merely writes <c>Catch ex As Exception</c> must not start paying for an AOT
    /// publish.</para>
    ///
    /// <para>⛔ The absence assertion is the load-bearing one. Asserting only the stdout would
    /// pass just as happily if the build had published a shim nobody needed — the cost regression
    /// that inertness exists to prevent, and one that is invisible in program output.</para>
    /// </summary>
    [Test]
    public void AnEmptySurfaceTryCatchProgram_RunsNatively_AndPublishesNoShim()
    {
        var built = BuildOnce("ConfInert", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.bas"] = """
                Module Program
                 Sub Main()
                  Try
                   Throw New ArgumentException("boom")
                  Catch ex As Exception
                   Console.WriteLine("caught " & ex.Message)
                  End Try
                  Console.WriteLine("done")
                 End Sub
                End Module
                """,
        });

        AssertBuilt(built.Result, "the empty-surface try/catch program");

        var outputDir = Path.GetDirectoryName(built.Result.ExecutablePath)!;
        var shimName = NetProxyEmitter.ShimModuleFileName(
            NetShimGenerator.ShimAssemblyName("ConfInert"));

        Assert.That(File.Exists(Path.Combine(outputDir, shimName)), Is.False,
            "a program that names .NET exception types but calls no .NET MEMBER has an empty "
            + "surface and must publish NO shim. Finding " + shimName + " here means naming a "
            + "type was enough to trigger an AOT publish — a silent cost regression that no "
            + "program output would reveal.");

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("caught boom\ndone\n"),
            "the throw/catch must still WORK, lowered natively to std::runtime_error and what(). "
            + "Proving inertness with a program that no longer runs correctly would be worthless.");
    }

    // =====================================================================================
    // §12.3 — a NAMED static call, and typed-catch SELECTIVITY.
    // =====================================================================================

    /// <summary>
    /// Typed-catch SELECTIVITY — authored as a PINNED DIVERGENCE, because measuring it found the
    /// shape does not compile at all.
    ///
    /// <para><b>Why the row was attempted.</b> A coverage audit showed the existing typed-catch
    /// row (<c>TypedCatchOfABaseType_MatchesTheGeneratedShimsChain</c>) proves the POSITIVE
    /// direction only: it has exactly ONE clause, and it is the one that must match. A
    /// <c>NetException::Matches</c> returning <c>true</c> unconditionally — or a shim emitting
    /// every element of every chain — passes it unchanged. Selectivity needs a clause that must
    /// NOT fire, an INTERMEDIATE base that must, and a later catch-all that must LOSE.</para>
    ///
    /// <para>⛔ <b>MEASURED: two .NET-typed Catch clauses cannot coexist.</b></para>
    /// <code>
    /// error C2312: 'const std::runtime_error &amp;': is caught by
    ///              'const std::runtime_error &amp;' on line 42
    /// </code>
    /// <para>Every .NET exception type maps to <c>std::runtime_error</c>
    /// (<c>MapCatchType</c>), so the PER-CLAUSE handlers emitted after the §11.1 ladder become
    /// duplicate C++ handlers and the second is unreachable. One clause compiles; two do not. The
    /// ladder itself is fine — it is an if/else-if chain over <c>Matches()</c> and handles many
    /// clauses — but it is not what breaks.</para>
    ///
    /// <para>So selectivity is not merely untested, it is currently INEXPRESSIBLE: any program
    /// that could distinguish "matches the right clause" from "matches everything" needs at least
    /// two clauses. Chipped. This row pins the diagnostic so the fix is
    /// noticed; when it lands, replace this with the runtime selectivity row described above
    /// rather than deleting it.</para>
    ///
    /// <para>⚠ The named STATIC-call half of this row was split out — it is a separate concern and
    /// hit an unrelated publish problem. <c>Convert.ToInt32</c> in the property program above is
    /// already a real .NET static crossing.</para>
    /// </summary>
    /// <summary>
    /// A NAMED static .NET call. The Milestone row is cited for "instance + static calls", but it
    /// contains no .NET static call at all: its only static-shaped call is
    /// <c>Console.WriteLine</c>, and <c>Console</c> is deliberately claimed for NATIVE handling —
    /// <c>NetClaimPredicate</c> calls routing it through the shim "the single most dangerous
    /// mistake in P2a". Other fixtures make static calls incidentally, inside larger §8.5/§8.6
    /// diffs. <c>Regex.Escape</c> is the named row: a static on a curated type, String in and
    /// String out, so both String directions cross in one call.
    /// </summary>
    [Test]
    public void AStaticDotNetCall_CrossesWithStringInAndStringOut()
    {
        var built = BuildOnce("ConfStatic", new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Program.bas"] = """
                Using System.Text.RegularExpressions

                Module Program
                 Sub Main()
                  Console.WriteLine(Regex.Escape("a.b"))
                 End Sub
                End Module
                """,
        });

        AssertBuilt(built.Result, "the static-call program");

        Assert.That(NetShimPipelineFixture.Run(built.Result.ExecutablePath!),
            Is.EqualTo("a\\.b\n"),
            "Regex.Escape(\"a.b\") is .NET's own answer for a real STATIC crossing with a String "
            + "argument and a String result. 'a.b' unchanged means the call never reached .NET.");
    }

    [Test]
    public void TwoDotNetTypedCatchClauses_DoNotYetCompile_PinnedDivergence()
    {
        var dir = NetShimPipelineFixture.NewTempDir("blnet-conf-multicatch-");
        Dirs.Add(dir);
        File.WriteAllText(Path.Combine(dir, "Program.bas"), """
            Using System.Text.RegularExpressions

            Module Program
             Sub Main()
              Try
               Dim R As New Regex("[")
               Console.WriteLine("WRONG-no-throw")
              Catch ex As InvalidOperationException
               Console.WriteLine("WRONG-unrelated-clause")
              Catch ex As ArgumentException
               Console.WriteLine("caught-intermediate")
              Catch ex As Exception
               Console.WriteLine("WRONG-root-clause")
              End Try
              Console.WriteLine("done")
             End Sub
            End Module
            """);

        var projectPath = NetShimPipelineFixture.WriteProject(dir, "ConfMultiCatch");
        var result = CppProjectBuilder.Build(ProjectFile.Load(projectPath), "Release");
        var text = NetShimPipelineFixture.Diagnostics(result) + "\n" + result.RawToolchainOutput;

        Assert.That(result.Success, Is.False,
            "Two .NET-typed Catch clauses are currently expected NOT to build. If this starts "
            + "succeeding the duplicate-handler defect was fixed — replace this row with the "
            + "runtime SELECTIVITY row (unrelated clause must not fire, intermediate base must, "
            + "trailing catch-all must lose), do not delete it.\n" + text);

        Assert.That(text, Does.Contain("C2312").Or.Contain("is caught by"),
            "the failure must still be the DUPLICATE C++ HANDLER — every .NET exception type maps "
            + "to std::runtime_error, so the per-clause handlers collide. A different failure "
            + "means this row is pinning something else and is no longer evidence about "
            + "multi-clause catch.\n" + text);
    }
}
