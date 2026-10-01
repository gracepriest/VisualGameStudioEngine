using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  #140 / ADR-0019 — the C++ closure programs RUN, with VB's output (vbc, S/t140/oracle), through
//  the standard pipeline, the aggressive pipeline, CompileProjectFiles and the real CLI plain and
//  --optimize (CLAUDE.md: "test both entry points", "validate codegen through the IR optimizer"),
//  and each program's path is asserted by CppClosurePathTests. [Category("Integration")]: compiles
//  and runs C++, spawns the CLI.
// =====================================================================================

// ⚠ Deliberately NOT named "...ExecutionTests": this fixture never runs JavaScript, and a name with that suffix is
// swept into JsExecutionTierRosterTests.RosterCoversEveryJavaScriptIntegrationFixture, whose roster is the tier that
// runs under Node (101 fixtures on this base; this one does not change that number).
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CppClosureRunTests
{
    private static string Norm(string s) => FourBackends.Norm(s);

    // ---- The by-copy fallback set RUNS (ruling D1/D3 obligation (a)) ------------------------------------

    private static IEnumerable<TestCaseData> FallbackSet() =>
        CppClosurePrograms.Fallback().Select(p => new TestCaseData(p).SetName("Fallback_" + p.Name.Replace('/', '_')));

    /// <summary>
    /// Every program of the fallback set prints VB's output through the standard, aggressive and project
    /// entry points and the real CLI plain and <c>--optimize</c>. They are the programs ClosureLowering
    /// refuses and the by-copy rule admits; the path itself is <c>CppClosurePathTests</c>' business.
    /// (X1 and X3 are programs VB rejects; see <see cref="CppClosureProgram.Note"/>.)
    /// </summary>
    [TestCaseSource(nameof(FallbackSet))]
    public void TheFallbackSet_RunsWithVbsOutput(CppClosureProgram program)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in CppClosures.RunEntries)
                Assert.That(CppClosures.Run(program.Source, entry), Is.EqualTo(program.Expected), $"{program.Name}: {entry}");
            Assert.That(CppClosures.RunViaCli(program.Source, optimize: false), Is.EqualTo(program.Expected), $"{program.Name}: CLI");
            Assert.That(CppClosures.RunViaCli(program.Source, optimize: true), Is.EqualTo(program.Expected), $"{program.Name}: CLI --optimize");
        });
    }

    // ---- The other falsifiers and rows that RUN ---------------------------------------------------------------

    /// <summary>(program, VB's output, the root's path).</summary>
    private static IEnumerable<TestCaseData> RunRows()
    {
        yield return new TestCaseData(ClosureLoweringContractPrograms.D10, "18").SetName("Lowered_D10_func10_runs");
        yield return new TestCaseData(ClosureLoweringContractPrograms.D11, "10").SetName("Lowered_D11_action9_runs");
        yield return new TestCaseData(CppClosurePrograms.AR1, "10\n20\n20").SetName("Lowered_AR1_func10WithACapturedWrite_runsRight");
        yield return new TestCaseData(CppClosurePrograms.EX1, "after loop 3\nfinally\n12\n22\n32").SetName("EX1_exitForInATryFinally_tailRuns_finallyOnce");
        yield return new TestCaseData(CppClosurePrograms.EX2, "46\n-1").SetName("EX2_returnFromTheLoopBodyWithTheLambdaAlive");
        yield return new TestCaseData(CppClosurePrograms.EX3, "tail 3\n2\n5\n9").SetName("EX3_forEachExitForThenTheTail");
        // The shapes that actually need #226's rule in ComputeInlineRegion: an Exit For straight out of a COUNTED loop's
        // per-iteration body (measured: without the rule clang says "cannot jump from this goto statement to its label").
        // Also in the ADR-0016 fence; repeated here, with vbc's output, so this fixture owns the falsifier.
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E06, PerIterationLoopBodyDimProbes.E06Expected).SetName("EX0a_exitForOutOfACountedPerIterationBody");
        yield return new TestCaseData(PerIterationLoopBodyDimProbes.E07, PerIterationLoopBodyDimProbes.E07Expected).SetName("EX0b_exitForOutOfTheInnerLoopTheOuterReenters");
        yield return new TestCaseData(CppClosurePrograms.CX1, "seen: bad state").SetName("CX1_messageReadAfterTheCatchExits");
        yield return new TestCaseData(CppClosurePrograms.CX2, "typed: original").SetName("CX2_throwOfTheCapturedExceptionKeepsItsType");
        yield return new TestCaseData(CppClosurePrograms.CX2b, "typed: original").SetName("CX2b_theRethrownExceptionIsHandledByTheClauseOfItsOwnType");
        yield return new TestCaseData(CppClosurePrograms.CX3, "A:arg1\nI:inv2").SetName("CX3_twoCatchTypes_isTheFallback");
        yield return new TestCaseData(CppClosurePrograms.NM1, "19").SetName("NM1_userNamesLikeTheLoweringsOwn");
        yield return new TestCaseData(ClosureLoweringContractPrograms.AT1, "16").SetName("AT1_aRootWithOneLowerableAndOneRefusedLambda_runsWhole");
        yield return new TestCaseData(ClosureLoweringContractPrograms.TwoRoots, "2").SetName("TwoRoots_oneLoweredOneByCopy");
        yield return new TestCaseData(CppClosurePrograms.RI3, "big\n10").SetName("RI3_refusedRootNothingWrites_runsOnTheFallback");
    }

    /// <summary>
    /// ⭐ The falsifiers of ruling D1/D2/D5 that RUN on C++, in all three in-process modes and the real CLI plain
    /// and <c>--optimize</c>:
    /// <list type="bullet">
    /// <item><b>D10/D11/AR1</b>: an arity-9/10 delegate, with a captured write, is LOWERED on C++ (no arity cap)
    /// and runs right. MSIL refuses D10/D11 (its facade caps are 8 / 9).</item>
    /// <item><b>EX1/EX2/EX3 (D5.2, #226's rule in <c>ComputeInlineRegion</c>)</b>: a lambda declared in a For Each, an
    /// <c>Exit For</c> inside a Try/Finally, statements after the loop — the tail executes and the Finally runs
    /// once, not swallowed into the iteration's own try; a <c>Return</c> out of the loop body with the lambda
    /// alive; and EX0a/EX0b, an <c>Exit For</c> straight out of a COUNTED loop's per-iteration body — ⚠ the shape
    /// that discriminates the rule: EX1, EX2 and EX3 still pass with the rule removed (measured), E06/E07 do not
    /// compile ("cannot jump from this goto statement to its label").</item>
    /// <item><b>CX1/CX2/CX2b (D5.3)</b>: a lambda reads the captured Catch variable's message after the Catch exits; a
    /// captured <c>Throw ex</c> rethrows the SAME exception — caught as its own type, not sliced to the base. ⚠ CX2's single
    /// clause cannot tell a sliced copy (a <c>std::runtime_error</c> still lands in the one clause's fallback handler); CX2b's
    /// two-clause ladder can, and kills the slicing mutant. CX3 (two Catch types) is the fallback.</item>
    /// <item><b>NM1 (D5.6)</b>: user names spelled like the lowering's own compile and run.</item>
    /// <item><b>AT1</b>: atomicity — a root with one lowerable and one refused lambda runs whole (vbc: 16).</item>
    /// <item><b>RI3 (D5.1)</b>: the refused root nothing writes runs on <c>[=]</c> — W2 and the lowering agree.</item>
    /// </list>
    /// </summary>
    [TestCaseSource(nameof(RunRows))]
    public void TheFalsifiers_RunWithVbsOutput(string source, string expected)
    {
        Assert.Multiple(() =>
        {
            foreach (var entry in CppClosures.RunEntries)
                Assert.That(CppClosures.Run(source, entry), Is.EqualTo(expected), entry.ToString());
            Assert.That(CppClosures.RunViaCli(source, optimize: false), Is.EqualTo(expected), "CLI");
            Assert.That(CppClosures.RunViaCli(source, optimize: true), Is.EqualTo(expected), "CLI --optimize");
        });
    }

    // ---- ⭐ Both entry points (CLAUDE.md "test both entry points") --------------------------------------------

    /// <summary>
    /// One lowered program (M01_L5: a captured local written by a lambda and read by its creator across a loop —
    /// the shape ADR-0016's fence was written to protect) through the real CLI and through the project entry
    /// points: the CLI (<c>CompileFile</c> → <c>Generate</c>), <c>CompileProjectFiles</c> → <c>Generate</c>, and
    /// <c>CompileProjectFiles</c> → <c>GenerateSplit</c> — what the IDE's C++ project build writes. All of them
    /// behave identically (vbc: seed, 12), and all of them emit the lowered form: one environment class, the
    /// holder struct, no <c>[=]</c> lambda.
    /// </summary>
    [Test]
    public void M01_L5_BehavesTheSameThroughTheCliAndThroughCompileProjectFiles()
    {
        const string expected = "seed\n12";
        var cli = CppClosures.Cli(CppClosurePrograms.M01_L5, optimize: true);
        Assert.That(cli.Exit, Is.EqualTo(0), cli.Console);
        var project = CppClosures.Compile(CppClosurePrograms.M01_L5, CppEntry.Project);
        var split = CppClosures.Compile(CppClosurePrograms.M01_L5, CppEntry.Split);

        Assert.Multiple(() =>
        {
            Assert.That(Norm(BclE2E.CompileRun(cli.Cpp)), Is.EqualTo(expected), "CLI --optimize");
            Assert.That(CppClosures.RunViaCli(CppClosurePrograms.M01_L5, optimize: false), Is.EqualTo(expected), "CLI");
            Assert.That(CppClosures.Run(project), Is.EqualTo(expected), "CompileProjectFiles → Generate");
            Assert.That(CppClosures.Run(split), Is.EqualTo(expected), "CompileProjectFiles → GenerateSplit (the IDE's build)");

            foreach (var (what, text) in new[] { ("CLI --optimize", cli.Cpp), ("Generate", project.Cpp), ("GenerateSplit", split.AllText) })
            {
                Assert.That(text.Contains("struct BasicLangClosures"), Is.True, $"{what}: the holder struct");
                Assert.That(text.Contains("class c__Env0"), Is.True, $"{what}: the environment class");
                Assert.That(text.Contains("[=]"), Is.False, $"{what}: no by-copy lambda");
            }
            Assert.That(System.Text.RegularExpressions.Regex.Matches(cli.Cpp, "class c__Env\\d+ :").Count,
                Is.EqualTo(System.Text.RegularExpressions.Regex.Matches(project.Cpp, "class c__Env\\d+ :").Count),
                "the CLI and the project entry point lower to the same number of environments");
        });
    }

    // ---- ⭐ Rule one: a both-refused program never emits C++ -------------------------------------------------

    /// <summary>
    /// ⭐ Falsifier for rule one (ruling D1, obligation: "D07 under cli, <c>--optimize</c> and project build writes
    /// no .cpp"): D07 is refused by both paths, so there is NO C++ to write — not on the CLI, not with
    /// <c>--optimize</c>, and not in a project build, which reaches <c>GenerateSplit</c> and reports the refusal as
    /// BL6001 before it touches <c>obj/gen</c>. The refusal text is W2's first, then the lowering's reason. RI1 is
    /// the same through root identity (D5.1).
    /// </summary>
    [TestCase(nameof(CppClosurePrograms.D07), "a Select Case 'When' guard in 'Main'", TestName = "RuleOne_D07_noCppWritten")]
    [TestCase(nameof(CppClosurePrograms.RI1), "a Select Case 'When' guard in '__lambda_0'", TestName = "RuleOne_RI1_noCppWritten")]
    public void ABothRefusedProgram_WritesNoCpp_OnTheCli_WithOptimize_AndInAProjectBuild(string which, string loweringReason)
    {
        var source = which == nameof(CppClosurePrograms.D07) ? CppClosurePrograms.D07 : CppClosurePrograms.RI1;
        Assert.Multiple(() =>
        {
            foreach (var (label, result) in new[]
            {
                ("CLI", CppClosures.Cli(source)),
                ("CLI --optimize", CppClosures.Cli(source, optimize: true)),
                ("project build", CppClosures.Cli(source, project: true)),
            })
            {
                Assert.That(result.Exit, Is.Not.EqualTo(0), $"{label} must refuse");
                Assert.That(result.CppFilesWritten, Is.Empty, $"{label}: no .cpp may be written for a refused program");
                Assert.That(result.Cpp, Is.Null, label);
                var w2 = result.Console.IndexOf("not supported on C++ (#140)", StringComparison.Ordinal);
                var lowering = result.Console.IndexOf("closure lowering cannot lower 'Main' either (#140): C++: " + loweringReason, StringComparison.Ordinal);
                Assert.That(w2, Is.GreaterThanOrEqualTo(0), $"{label}: W2's text\n{result.Console}");
                Assert.That(lowering, Is.GreaterThan(w2), $"{label}: then the lowering's reason\n{result.Console}");
            }
        });
    }

    /// <summary>The project build reports the refusal under BL6001, the capability checker's code, which is the
    /// one <c>CppProjectBuilder</c> maps <see cref="CppCapabilityException"/> to — the proof that the both-refused
    /// case travels the C++ backend's own refusal channel (ADR-0019's first interpretation).</summary>
    [Test]
    public void TheProjectBuild_ReportsABothRefusedProgramAsBL6001()
    {
        var result = CppClosures.Cli(CppClosurePrograms.D07, project: true);
        Assert.That(result.Console, Does.Contain("BL6001"), result.Console);
    }

    // ---- ⭐ Fallback that STILL fails the C++ compiler, named for the real gap --------------------------------

    /// <summary>
    /// ⭐ The rows ruling D1 keeps on the fallback because the fallback only REPRODUCES a C++ compiler failure
    /// that is not the lambda's: a lowering refusal there would name the wrong construct, and would make a future fix
    /// of the real gap unable to unlock them. Each is pinned as (a) fallback or lowered as measured, and (b) the
    /// C++ compiler still rejects the generated text, for the NAMED gap — so each flips to running the day that gap
    /// closes, with no #140 code touched. A DIFFERENT failure here means the pin is stale.
    /// </summary>
    private static IEnumerable<TestCaseData> StillFailsClang()
    {
        yield return new TestCaseData(CppClosurePrograms.AsyncReadOnly, "Work", CppClosurePath.ByCopy, "Result")
            .SetName("D17_asyncReadOnly_fallback_failsClang_namedForTheTaskResultGap");
        yield return new TestCaseData(CppClosurePrograms.Async, "Work", CppClosurePath.ByCopy, "Result")
            .SetName("D02_async_fallback_failsClang_namedForTheTaskResultGap");
        yield return new TestCaseData(CppClosurePrograms.GenericCapture, "Wrap", CppClosurePath.ByCopy, "'T'")
            .SetName("D04_genericTypedCapture_fallback_failsClang_namedForTheGenericMethodLocalGap");
        yield return new TestCaseData(CppClosurePrograms.GenericLambdaParameter, "Apply", CppClosurePath.Lowered, "'T'")
            .SetName("D16_lambdaParameterTypedByAGenericMethod_lowered_failsClang_sameGenericGap");
        yield return new TestCaseData(CppClosurePrograms.ListForEach, "Main", CppClosurePath.Lowered, "ForEach")
            .SetName("L6_listForEach_lowered_failsClang_namedForTheListForEachGap");
    }

    [TestCaseSource(nameof(StillFailsClang))]
    public void TheGapIsNotTheLambda_ItStillFailsTheCppCompiler_NamedForItsRealGap(string source, string root, CppClosurePath path, string gap)
    {
        var build = CppClosures.Compile(source);
        Assert.That(build.PathOf(root), Is.EqualTo(path), $"root '{root}'");

        var (compiled, output) = CppClosures.TryClang(build.Cpp);
        Assert.That(compiled, Is.False,
            "this program used to fail the C++ compiler for a gap that is not the lambda's — if it compiles now, the gap " +
            "is closed: flip this pin to a running one (and delete its row here)");
        Assert.That(output, Does.Contain(gap), $"the failure must still be the named gap ('{gap}'). A DIFFERENT failure means this pin is stale.\n{output}");
    }

    /// <summary>The async rows' gap is <c>Task.Result</c>, a C++ runtime gap: the SAME programs read their value
    /// through <c>Await</c> compile — the lambda is fine. (Pins the sentence the ruling rests on: "the Async rows
    /// fail on Task.Result, not the lambda".)</summary>
    [Test]
    public void TheAsyncRowsFail_OnTaskResult_NotOnTheLambda()
    {
        const string withoutResult = """
            Async Function Work(k As Integer) As Task(Of Integer)
                Dim f As Func(Of Integer, Integer) = Function(x As Integer) x + k
                Return f(1)
            End Function
            Sub Main()
                Dim t As Task(Of Integer) = Work(5)
                Console.WriteLine("done")
            End Sub
            """;
        var build = CppClosures.Compile(withoutResult);
        Assert.That(build.PathOf("Work"), Is.EqualTo(CppClosurePath.ByCopy));
        var (compiled, output) = CppClosures.TryClang(build.Cpp);
        Assert.That(compiled, Is.True, "the same lambda in the same async function compiles when nothing reads Task.Result\n" + output);
    }

    /// <summary>
    /// D09: a lambda in a module-level initializer is its own root; the by-copy emission is a non-local lambda
    /// with a capture default, which the C++ compiler refuses ("non-local lambda expression cannot have a
    /// capture-default") — pre-existing on master, byte-identical now. Run with the verifier in Log mode because it
    /// also fires Invariant P(d) (see <c>CppClosurePathTests.TheModuleInitializerLambda_IsTheOnlyPreExistingVerifierFire</c>).
    /// </summary>
    [Test]
    [NonParallelizable]
    public void D09_AModuleInitializerLambda_IsFallback_AndStillFailsTheCppCompiler()
    {
        var previousMode = BasicLang.Compiler.IR.Optimization.IRVerifier.Mode;
        var previousPath = BasicLang.Compiler.IR.Optimization.IRVerifier.LogPath;
        var log = Path.Combine(Path.GetTempPath(), "bl-t140-d09-" + Guid.NewGuid().ToString("N") + ".log");
        try
        {
            BasicLang.Compiler.IR.Optimization.IRVerifier.LogPath = log;
            BasicLang.Compiler.IR.Optimization.IRVerifier.Mode = BasicLang.Compiler.IR.Optimization.IRVerifierMode.Log;
            var build = CppClosures.Compile(CppClosurePrograms.ModuleInitializer);
            Assert.That(build.PathOf("__lambda_0"), Is.EqualTo(CppClosurePath.ByCopy));
            var (compiled, output) = CppClosures.TryClang(build.Cpp);
            Assert.That(compiled, Is.False);
            Assert.That(output, Does.Contain("capture-default").Or.Contain("capture default").Or.Contain("lambda"), output);
        }
        finally
        {
            BasicLang.Compiler.IR.Optimization.IRVerifier.Mode = previousMode;
            BasicLang.Compiler.IR.Optimization.IRVerifier.LogPath = previousPath;
            try { File.Delete(log); } catch { /* temp */ }
        }
    }

    // ---- The verifier under the real CLI ----------------------------------------------------------------------------

    /// <summary>
    /// Ruling D5.5: the verifier runs on the lowered clone in CI. Here through the SHIPPING compiler: the real CLI
    /// with <c>BASICLANG_VERIFY_IR=log:&lt;file&gt;</c> compiles lowered programs and writes nothing to the log — 0
    /// fires (D09 is the only program that fires, and it is not among these).
    /// </summary>
    [TestCase(nameof(CppClosurePrograms.M01_L5))]
    [TestCase(nameof(CppClosurePrograms.EX1))]
    [TestCase(nameof(CppClosurePrograms.CX1))]
    [TestCase(nameof(CppClosurePrograms.AR1))]
    [TestCase(nameof(CppClosurePrograms.NM1))]
    public void TheVerifier_RaisesNoFire_OnALoweredProgram_UnderTheRealCli(string which)
    {
        var source = typeof(CppClosurePrograms).GetField(which, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!
            .GetRawConstantValue() as string;
        var dir = Path.Combine(Path.GetTempPath(), "bl-t140-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var log = Path.Combine(dir, "verify.log");
            foreach (var optimize in new[] { false, true })
            {
                var args = new List<string> { "Prog.bas", "--target=cpp" };
                if (optimize) args.Add("--optimize");
                var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 300_000,
                    new Dictionary<string, string> { ["BASICLANG_VERIFY_IR"] = "log:" + log });
                Assert.That(exit, Is.EqualTo(0), stdout + stderr);
                Assert.That(File.Exists(Path.Combine(dir, "Prog.cpp")), Is.True);
                Assert.That(File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>(), Is.Empty,
                    $"the verifier must not fire (optimize={optimize})");
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    // ---- Per-iteration environment and Exit: the path matters (D5.2) --------------------------------------------------

    /// <summary>EX1's tail and Finally are the contract; the iteration's environment must also be a per-iteration
    /// object — each lambda sees ITS iteration's <c>y</c> (10, 20, 30), not the last one's — which the outputs 12,
    /// 22, 32 (= y + total, with total counting the iterations before the Exit) already show; this pins the shape
    /// in the emitted text: the iteration allocates its own environment inside the loop.</summary>
    [Test]
    public void EX1_AllocatesAnEnvironmentPerIteration_InsideTheLoop()
    {
        var cpp = CppClosures.Compile(CppClosurePrograms.EX1).Cpp;
        var allocations = System.Text.RegularExpressions.Regex.Matches(cpp, @"BasicLang::New<BasicLangClosures::c__Env\d+>\(\)").Count;
        Assert.That(allocations, Is.GreaterThanOrEqualTo(2), "one function environment and at least one per-iteration environment");
    }
}
