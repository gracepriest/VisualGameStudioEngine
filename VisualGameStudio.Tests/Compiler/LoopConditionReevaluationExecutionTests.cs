using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #256 — a While / Do loop whose condition holds AndAlso, OrElse or If() must evaluate that condition ONCE PER ITERATION
//  on C#, and still short-circuit. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. IRBuilder lowers AndAlso, OrElse and If() to if-shaped blocks writing a carrier (`__sc0`), and the loop's own branch
//  ends the last of them. The C# backend found the loop only at that branch, so the structured walk had already written every
//  condition block ABOVE the loop — once — and `while (__sc0)` tested a carrier nothing wrote again. Every loop form hung; a loop
//  left by `Exit While`/`Exit Do` ended having run its condition ONE time, so its side effects were wrong. C++ and MSIL were right
//  (MSIL but for `Not`, #257). The fix (CSharpBackend, `OpensReentrantLoop`) writes such a loop as
//  `while (true) { …condition blocks…; if (!(c)) break; …body… }`.
//
//  ⭐ THE ORACLE IS vbc, not a backend (see LoopConditionProbes). THE COUNTER IS WHAT PROVES IT: every program appends a tag each time
//  an operand runs, so `seen=abababa` says "a, b, a, b, a, b, a" — each iteration ran the whole condition, once — and a loop that
//  evaluated it once, twice, or without short-circuiting prints something else. The answer alone (`i=3 body=3`) cannot tell.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE. A C# loop that never ends freezes the whole test host if it runs in process (the in-process
//  harness has no timeout; `[CancelAfter]` does not stop a synchronous spin and `[Timeout]` is obsolete and abandons the thread).
//  The C# leg goes through CSharpProcessRunner — a child process, 20 s, killed — so a regression of the fix is a FAILURE named
//  "hung", not a frozen run. The one place a program runs in process on C# is the runner's own equivalence test, and it is not a loop.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): the real BasicLang
//  CLI (standard passes), the real CLI with `--optimize` (aggressive) and `BasicCompiler.CompileProjectFiles` with
//  `OptimizeAggressive` (what a Release .blproj build and the IDE call) — plus `BasicLang build App.blproj -c Release` for four programs.
//
//  ⛔ CELLS WITH NO EXPECTATION — each is a defect that is NOT #256's, measured on the fixed build and on the build before it. Asserting
//  one would pin the defect (and a hang would have to be pinned through the runner). Their rows exist, with the other backends.
//
//    C# #227   f_LW_aa f_LW_ctl x_LW n_LD n_LDctl   a bottom-tested loop (`Do … Loop While/Until`) is emitted twice — the peel, then the
//                                                    loop's copy — and the copy drops every block the peel already wrote (an If's
//                                                    continuation, a nested loop). Wrong with a PLAIN condition too (f_LW_ctl); n_LDctl,
//                                                    the nesting with plain conditions, HANGS before and after #256.
//    C# #136   l_fn l_sub                            a loop inside a lambda: CS1643 (the lambda loses its return paths) / its writes are lost.
//    MSIL #257 W_nt DW_nt DU_nt LW_nt LU_nt          `Not (a AndAlso b)`: MSIL's `Not` is bitwise (three of those cells hang, two are wrong).
//    C++/MSIL #261 o_for                             a counted For's `To If(…)` bound is re-evaluated every iteration (VB, and C#, compute it once).
//    JavaScript #257  every loop whose condition has control flow   refused: "a loop header whose branch does not target the loop's own
//                                                    .end block". Pinned as a REFUSAL, below, so it flips when #257 is fixed.
//    — `Continue While` / `Continue Do`: there is no Continue statement in BasicLang (#262), so no row. (Mutant M3 — "Continue skips the
//      re-evaluation" — is equivalent for that reason.)
//
//  ⚠ i4loop (If() in While / Do While / Loop Until conditions and a For bound) is in UntypedConstAndConditionalExecutionTests, now with its C# cell.
//  ⚠ Named "…ExecutionTests" on purpose: its JavaScript CONTROL cells (the ten ctl/se rows, f_W_ctl, f_LW_ctl, n_LDctl) run under Node, so it is in
//  JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #256 RUN: a loop condition holding control flow runs once per iteration and short-circuits, on every backend that can print it.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs and the C++ compiles share the machine with the spawned runners
public class LoopConditionReevaluationExecutionTests
{
    private static IEnumerable<TestCaseData> Cells(IEnumerable<LoopProbe> probes)
        => probes.SelectMany(p => TempExec.Backends(p.Agrees).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> GridCells() => Cells(LoopConditionProbes.Grid);

    private static IEnumerable<TestCaseData> ExtraCells() => Cells(LoopConditionProbes.Extras);

    private static IEnumerable<TestCaseData> StressCells() => Cells(LoopConditionProbes.Stress);

    private static IEnumerable<TestCaseData> JsRefusedCells()
        => LoopConditionProbes.All.Where(p => p.JsRefused)
            .SelectMany(p => Enum.GetValues<EntryPoint>().Select(e => new TestCaseData(p, e).SetName($"{p.Id}_JavaScript_{e}")));

    private static IEnumerable<TestCaseData> BuildCommandCells()
        => new[] { "W_aa", "LU_iff", "x_sel", "t_in" }.Select(id => new TestCaseData(LoopConditionProbes.ById(id)).SetName($"{id}_BuildRelease"));

    private static string Ids(IEnumerable<LoopProbe> probes) => string.Join(",", probes.Select(p => p.Id));

    /// <summary>
    /// The tables ARE the proof, so their shape is pinned: a row cannot vanish, and a backend cannot be dropped from one, without this
    /// test saying so. The cells WITHOUT an expectation are pinned by name too — the header says why each is there, and a defect that gets
    /// fixed (#227, #136, #257, #261) must come back in as a row on purpose. It is also the fixture's one plain [Test]:
    /// <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture whose tests are all [TestCaseSource] counts as empty.
    /// </summary>
    [Test]
    public void TheTables_HaveTheirRows()
    {
        var all = LoopConditionProbes.All;

        Assert.Multiple(() =>
        {
            Assert.That(LoopConditionProbes.Grid, Has.Count.EqualTo(35), "5 loop forms x 7 condition kinds");
            Assert.That(all, Has.Count.EqualTo(56), "the grid and 21 extras");
            Assert.That(all.Select(p => p.Id).Distinct().Count(), Is.EqualTo(all.Count), "no id twice");

            // every row carries the counter: it is what proves "once per iteration" and "still short-circuits"
            Assert.That(all.Where(p => !p.Id.EndsWith("ctl", StringComparison.Ordinal) && !System.Text.RegularExpressions.Regex.IsMatch(p.Source, "\\bP\\(\"")).Select(p => p.Id),
                Is.Empty, "a row with no side-effect counter (only the plain-condition CONTROLS, `…ctl`, have none)");

            // the cells with NO expectation, by backend
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.CSharp))), Is.EqualTo("f_LW_aa,f_LW_ctl,l_fn,l_sub,n_LD,n_LDctl,x_LW"), "C# cells with no expectation (#227, #136)");
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.Msil))), Is.EqualTo("W_nt,DW_nt,DU_nt,LW_nt,LU_nt,o_for"),
                "MSIL cells with no expectation (#257 Not, #261 For bound)");
            Assert.That(Ids(all.Where(p => !p.Agrees.HasFlag(Bk.Cpp))), Is.EqualTo("o_for"), "C++ cells with no expectation (#261)");

            // JavaScript: the controls RUN, everything else is a pinned refusal
            Assert.That(Ids(all.Where(p => p.Agrees.HasFlag(Bk.JavaScript))),
                Is.EqualTo("W_ctl,W_se,DW_ctl,DW_se,DU_ctl,DU_se,LW_ctl,LW_se,LU_ctl,LU_se,f_W_ctl,f_LW_ctl,n_LDctl"));
            Assert.That(all.Count(p => p.JsRefused), Is.EqualTo(43), "25 grid programs and 18 extras are refused by JavaScript (#257)");
            Assert.That(all.Where(p => p.JsRefused && p.Agrees.HasFlag(Bk.JavaScript)).Select(p => p.Id), Is.Empty, "a program is a JS control or a JS refusal, not both");

            // the cell counts, per table
            Assert.That(GridCells().Count(), Is.EqualTo(110), "35 C# + 35 C++ + 30 MSIL + 10 JavaScript");
            Assert.That(ExtraCells().Count(), Is.EqualTo(57));
            Assert.That(JsRefusedCells().Count(), Is.EqualTo(129), "43 programs x 3 entry points");

            // the stress table: ten conditions the grid has no row for, in While and Do … Loop While, C# and C++
            Assert.That(Ids(LoopConditionProbes.Stress), Is.EqualTo("W_s1,W_s2,W_s3,W_s4,W_s5,W_s6,W_s7,W_s8,W_s9,W_s10,LW_s1,LW_s2,LW_s3,LW_s4,LW_s5,LW_s6,LW_s7,LW_s8,LW_s9,LW_s10"));
            Assert.That(StressCells().Count(), Is.EqualTo(40), "20 programs x (C# + C++)");
            Assert.That(LoopConditionProbes.Stress.Where(p => p.Agrees != (Bk.CSharp | Bk.Cpp) || p.JsRefused), Is.Empty);
        });
    }

    // ============================================================================================
    // THE GRID — 5 loop forms x 7 condition kinds, on every backend that prints vbc's answer.
    // ============================================================================================

    /// <summary>
    /// While, Do While, Do Until, Do … Loop While and Do … Loop Until, each over a plain compare (ctl), one counting call (se), AndAlso,
    /// OrElse, If(), Not (a AndAlso b) and (a AndAlso b) OrElse (c AndAlso d): the loop ends where vbc's ends and the operands ran exactly
    /// as often, in the same order, as vbc's ran them. C# goes through the hang-safe runner.
    /// </summary>
    [TestCaseSource(nameof(GridCells))]
    public void ALoopCondition_RunsOncePerIteration_AndShortCircuits(LoopProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, hangSafe: true);

    // ============================================================================================
    // THE EXTRAS — Exit, nesting, a class method, a Try, per-iteration Dims, an If in the body, a For bound.
    // ============================================================================================

    /// <summary>
    /// <c>Exit While</c> / <c>Exit Do</c> (also from a Select Case and from a Try/Finally) end the loop having run the condition once per
    /// COMPLETED iteration, not once in all; two nested loops each re-evaluate their own condition; the same loop in a class method and
    /// inside a Try; ADR-0014's per-iteration Dim captured by a lambda; an If in the body; a counted For's If() bound is still computed once
    /// (o_for, C# only: VB's rule, and C++/MSIL are #261).
    /// </summary>
    [TestCaseSource(nameof(ExtraCells))]
    public void ALoopAroundTheCondition_StillRunsOncePerIteration(LoopProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, hangSafe: true);

    // ============================================================================================
    // THE STRESS CONDITIONS — shapes the grid has no row for.
    // ============================================================================================

    /// <summary>
    /// A chain of three AndAlso, a chain of three OrElse, `Not (a OrElse b)`, an If with an AndAlso arm, an If as a call's argument, an If inside a compare,
    /// an If inside an AndAlso, an AndAlso over an OrElse, `Not If(…)` and an If whose condition is an AndAlso — each in While and in Do … Loop While.
    /// vbc's answer, on C# (hang-safe) and C++. MSIL is not asked (its `Not` is bitwise, #257, and two of the ten have one) and JavaScript refuses them.
    /// </summary>
    [TestCaseSource(nameof(StressCells))]
    public void AStressCondition_RunsOncePerIteration_AndShortCircuits(LoopProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, hangSafe: true);

    // ============================================================================================
    // THE REAL BUILD — `BasicLang build App.blproj -c Release`, the route the IDE's build service takes.
    // ============================================================================================

    /// <summary>
    /// The project build writes the exe and the test RUNS it with a 20 s limit, killing the tree: a hang is a failure. W_aa (the headline),
    /// LU_iff (a bottom-tested If()), x_sel (Exit Do out of a Select Case) and t_in (a loop inside a Try).
    /// </summary>
    [TestCaseSource(nameof(BuildCommandCells))]
    public void TheReleaseProjectBuild_RunsTheLoopOncePerIteration(LoopProbe probe)
    {
        if (!CliTestHarness.DotnetOnPath()) Assert.Ignore("dotnet not found on PATH.");

        var dir = Path.Combine(Path.GetTempPath(), "bl-t256-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.bas"), probe.Source);
            File.WriteAllText(Path.Combine(dir, "App.blproj"), """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>CSharp</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                  </ItemGroup>
                </BasicLangProject>
                """);

            var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
                CliTestHarness.CliPath(), new[] { "build", Path.Combine(dir, "App.blproj"), "-c", "Release" }, dir, timeoutMs: 180_000);
            Assert.That(buildExit, Is.EqualTo(0), $"BasicLang build -c Release failed:\n{buildOut}\n{buildErr}");

            var exes = Directory.GetFiles(dir, CliTestHarness.AppHostName("App"), SearchOption.AllDirectories);
            Assert.That(exes, Is.Not.Empty, $"the build produced no {CliTestHarness.AppHostName("App")}:\n{buildOut}");

            // RunProcess kills the process tree and FAILS the test when the program is still running at the limit.
            var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exes[0], Array.Empty<string>(), Path.GetDirectoryName(exes[0])!, timeoutMs: CSharpProcessRunner.DefaultTimeoutMs);

            Assert.Multiple(() =>
            {
                Assert.That(runExit, Is.EqualTo(0), $"the built program exited {runExit}:\n{runErr}");
                Assert.That(TempExec.Norm(runOut), Is.EqualTo(probe.Vb));
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }

    // ============================================================================================
    // JAVASCRIPT — a KNOWN REFUSAL (#257), pinned so it flips when #257 is fixed.
    // ============================================================================================

    /// <summary>
    /// ⛔ JavaScript cannot lower a loop header that holds control flow: "a loop header whose branch does not target the loop's own .end
    /// block". It is #257's (JavaScript's own structuring walk), not #256's — the C# fix does not touch it, and AndAlso is refused as
    /// well as If(). So no JavaScript answer is asserted for these 43 programs; the REFUSAL is, in every entry point, and the test below
    /// fails the day JavaScript starts accepting one. When it does: delete the row's <c>JsRefused</c>, add <c>Bk.JavaScript</c> to its
    /// <c>Agrees</c>, and it becomes a real cell (<c>TheTables_HaveTheirRows</c> pins the counts).
    /// </summary>
    [TestCaseSource(nameof(JsRefusedCells))]
    public void JavaScript_RefusesTheLoopHeader_KnownRefusal_257(LoopProbe probe, EntryPoint entry)
    {
        var refusal = JavaScriptRefusal(entry, probe.Source);

        if (refusal == null)
            Assert.Fail($"{probe.Id}: JavaScript now COMPILES this loop through {entry}. #257 may be fixed: promote the row (see this test's summary) and run it.");

        Assert.That(refusal, Does.Contain("a loop header whose branch does not target the loop's own .end block"),
            $"{probe.Id} through {entry}: JavaScript refused it, but not with the #257 refusal");
    }

    /// <summary>
    /// What JavaScript says when asked to compile <paramref name="source"/>, or null when it compiled. The CLI entry points are driven here, not
    /// through <c>TempExec.Emit</c>: that asserts the exit code is 0, and NUnit RECORDS a failed <c>Assert.That</c> even when the exception is caught,
    /// so catching its refusal would still fail the test. The project entry point throws <see cref="NotSupportedException"/> from the generator.
    /// </summary>
    private static string? JavaScriptRefusal(EntryPoint entry, string source)
    {
        if (entry == EntryPoint.ProjectRelease)
        {
            try
            {
                TempExec.Emit(Bk.JavaScript, entry, source);
                return null;
            }
            catch (NotSupportedException ex)
            {
                return ex.Message;
            }
        }

        var dir = Path.Combine(Path.GetTempPath(), "bl-t256-js-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Prog.bas"), source);
            var args = new List<string> { "Prog.bas", "--target=javascript" };
            if (entry == EntryPoint.CliOptimize) args.Add("--optimize");
            var (exit, stdout, stderr) = CliTestHarness.RunProcess(CliTestHarness.CliPath(), args.ToArray(), dir, timeoutMs: 120_000);

            // a refusal exits non-zero and writes no script
            return exit == 0 || File.Exists(Path.Combine(dir, "Prog.js")) ? null : stdout + stderr;
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}
