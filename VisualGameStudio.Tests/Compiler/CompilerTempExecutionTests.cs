using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0017 (#163), EXECUTION: every probe is compiled and RUN on the four backends that print, through
/// all three entry points — the real <c>BasicLang</c> CLI (standard passes), the real CLI with
/// <c>--optimize</c> (aggressive passes), and <c>BasicCompiler.CompileProjectFiles</c> with
/// <c>OptimizeAggressive</c> (what a Release <c>.blproj</c> build and the IDE's build service call) — and
/// compared with VB's own output. CLAUDE.md: "test both entry points" and "validate codegen through the
/// CLI and the IR optimizer".
///
/// <para><b>The oracle is <c>vbc</c>, not a backend.</b> Each expected value below was re-measured by the
/// test-writer: the probe, wrapped in a VB <c>Module</c> (classes and modules stay where they are), compiled
/// with the SDK's <c>vbc</c> and run. (Also re-measured: R7 as a `Function Main() As Integer` exits 135.)</para>
///
/// <para>What can go wrong is a backend that stops printing what it printed: the ADR's whole claim is that
/// the byte diff of every removal is confined to the removed temp's own lines. The probes that CAN differ
/// are the ones a removal touches (R1, R2, R7, R8, U4, U5) and the ones a removal must never touch
/// (R3, R4, R5, R6, R10 by kind or by a use; U1-U8 by spelling).</para>
///
/// <para>LLVM has no console and names its entry point `@Main`, so nothing it emits links (measured before and
/// after #163), and it is exercised through one program that returns its answer as an exit code
/// (<see cref="R7_OnLlvm_TheExitCodeIsVbs_PlainAndOptimize"/>).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CompilerTempExecutionTests
{
    private static IEnumerable<TestCaseData> Cells(params TempProbe[] probes)
        => probes.SelectMany(p => TempExec.Backends(p.Agrees).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    // ============================================================================================
    // The removal probes: a removal happened, and the program still prints what VB prints
    // ============================================================================================

    private static IEnumerable<TestCaseData> RemovalCells()
        => Cells(TempProbes.R1, TempProbes.R2, TempProbes.R8, TempProbes.U4, TempProbes.U5);

    /// <summary>R1, R2, R8, U4 and U5 lose compiler temps on C++, JavaScript, MSIL and LLVM (C# never emitted them).
    /// ⚠ R2 is the one that needs the flag to survive `InheritIdentity`, and U4 and U5 have an orphan (`-(-a)`,
    /// `-(-n)`) sitting next to user variables spelled `_t0`, `_t3`, `t0`, `t1`: the orphan goes and the variables stay.</summary>
    [TestCaseSource(nameof(RemovalCells))]
    public void ARemovedOrphan_LeavesVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    // ============================================================================================
    // Kept by kind or by a use: the program still prints what VB prints
    // ============================================================================================

    private static IEnumerable<TestCaseData> KeptCells()
        => Cells(TempProbes.R3, TempProbes.R4, TempProbes.R5, TempProbes.R6, TempProbes.R7, TempProbes.R10);

    /// <summary>
    /// R3 (the `tag` prints survive: a call is never deleted, and settled point 3 keeps its one adjacent use), R4
    /// (`MyBase.New(v + 1)`: the temp's only use is the base call — mutant M5 fails C++ to compile and MSIL to
    /// load), R5 (the Trail `FGF`: the call ORDER is kept), R6 (a division by zero still throws), R7 (the dead-store
    /// cascade: one store of `n` fewer, the same output) and R10 (MSIL keeps the element read: `caught`).
    ///
    /// <para>Backends left out are pre-existing defects, measured identical before and after #163 (see
    /// <see cref="TempProbe"/>): R4 on C# (CS0103), R6 on C# (prints `0`), R10 on C#, C++ and JavaScript (print `0 | 0`).</para>
    /// </summary>
    [TestCaseSource(nameof(KeptCells))]
    public void AKeptTemp_LeavesVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    // ============================================================================================
    // The U-spellings: user storage spelled like a temp — VB's output everywhere, 0 removals of a variable
    // ============================================================================================

    private static IEnumerable<TestCaseData> USpellingCells()
        => Cells(TempProbes.U1, TempProbes.U2, TempProbes.U3, TempProbes.U4, TempProbes.U5, TempProbes.U6, TempProbes.U7, TempProbes.U8);

    /// <summary>
    /// `Dim t5 = a + b` (82), `T5`, `_t3`, `_t0`, the fields `t0` and `t1`, the module variable `t3`, an unused `t1`/`t2`/`t9`, an
    /// accumulator `t1`: VB's output on all four backends in all three entry points. A guard on the name's spelling printed `12`
    /// for U1's `82` in 12 of 12 cells (#118).
    ///
    /// <para>⭐ <b>U2</b> (`Dim _tmp1 = a + b`) prints VB's <b>67</b>. It printed <b>0</b> before #163, in 12 of 12 cells: the old
    /// guard removed every unused value whose name STARTED with `_tmp`, and the renamed binop of `Dim _tmp1` is one. The
    /// "latent" removal was not latent for that spelling. This row is the fix.</para>
    /// </summary>
    [TestCaseSource(nameof(USpellingCells))]
    public void AUserVariableSpelledLikeATemp_IsNeverRemoved_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    // ============================================================================================
    // LLVM: R7 through an exit code
    // ============================================================================================

    /// <summary>R7 on the fifth backend: the answer (8 * 16 + 7 + 0 = 135, VB's own exit code) comes back as the process exit code
    /// through a two-line C shim, in the CLI's standard and `--optimize` passes. The dead-store cascade drops one store of `n`
    /// here as on every backend, and the answer must not move. (LLVM has no Release-`.blproj` route in this harness: the CLI
    /// is its only entry point, as in the implementer's matrix.)</summary>
    [TestCase(false, TestName = "R7_Llvm_Cli")]
    [TestCase(true, TestName = "R7_Llvm_CliOptimize")]
    public void R7_OnLlvm_TheExitCodeIsVbs_PlainAndOptimize(bool optimize)
    {
        if (!TempExec.ClangAvailable()) Assert.Ignore("clang is not on PATH: the LLVM leg links the emitted IR with it.");

        Assert.That(TempExec.RunLlvm(TempProbes.R7ForLlvm, optimize), Is.EqualTo(TempProbes.R7ForLlvmExitCode));
    }

    // ============================================================================================
    // ⭐ M8 — the by-name rule's WITNESS: `Catch t0` on C++
    // ============================================================================================

    private static IEnumerable<TestCaseData> WitnessCells()
    {
        // C#'s leg of this program does not compile, before and after #163 (measured): it is out.
        var probe = TempProbes.CtWbrT0 with { Agrees = Bk.Cpp | Bk.JavaScript | Bk.Msil };
        return Cells(probe);
    }

    /// <summary>
    /// ⭐ THE WITNESS (ADR-0017 Findings 3): `Catch t0` with orphans before the Try. The orphan `t0 = -a` IS deleted, so the C++
    /// backend's own temp counter renumbers, and WITHOUT D2's by-name rule the surviving string temp becomes `t0` inside the
    /// catch — `t0 = BasicLang::String(t0.what())`, clang: "no viable overloaded '='". OK before #163, OK with the rule,
    /// COMPILE-FAIL without it, in C++ through the CLI, the CLI with `--optimize` and the project route. **Mutant M8 (the
    /// rule dropped) must fail this test.** It must compile AND print VB's answer in every entry point.
    /// </summary>
    [TestCaseSource(nameof(WitnessCells))]
    public void CT_wbr_t0_CatchT0_StillCompilesAndRuns(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (`Catch t0`)");

    /// <summary>The witness's control: the SAME program with an ordinary name, correct on all four backends in every entry point.</summary>
    [TestCaseSource(nameof(ControlCells))]
    public void CT_wbr_k_TheSameProgramWithAnOrdinaryName_IsCorrectEverywhere(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    private static IEnumerable<TestCaseData> ControlCells() => Cells(TempProbes.CtWbrK);
}

/// <summary>
/// ⭐ ADR-0017 D2's by-name rule and D4, pinned: <b>#121's regression fence</b>. A user variable IRBuilder does not reserve — a
/// <c>For Each</c>, <c>Catch</c>, pattern or LINQ range variable (the #121 gap) — spelled like a temp collides with a minted
/// name. Every program here was already wrong or failing before #163. The by-name rule holds each at its pre-#163 answer, and
/// #163 moved a few cells; both kinds of row are pinned here as CURRENT behaviour, so that #121 (which reserves those names)
/// flips each one deliberately, in the same commit, rather than as a surprise.
///
/// <list type="bullet">
/// <item><b>Pinned WRONG or failing</b> (<see cref="Fence"/>): `LC_t0` and R11. `LC_t0` on MSIL is the one #163 change that is
/// not toward VB: it printed a wrong answer (`7|-3|7|-4`) BEFORE #163 and now prints `7` and then fails (3 cells) — with
/// `-a`'s orphan gone, MSIL conflates the delegate's local with another slot. The by-name rule does not reach it (no variable in
/// `Run` spells the deleted temp's name), so it is OPEN, and #121's to close.</item>
/// <item><b>Pinned CORRECT</b> (<see cref="NowReachVb"/>): the 27 cells (9 program × backend pairs, three entry points each) that
/// moved toward VB — C++ COMPILE-FAIL → OK in 12 and WRONG → OK in 9, C# WRONG → OK in 3, MSIL RUN-FAIL → OK in 3.</item>
/// <item><b>Controls</b>: the same shapes with ordinary names are VB-correct everywhere and identical before and after.</item>
/// </list>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CompilerTempCollisionFenceTests
{
    // ---- pinned CURRENT behaviour: wrong or failing, #121's to fix ---------------------------------

    private static IEnumerable<TestCaseData> Fence()
    {
        var lc = TempProbes.LcT0;
        // LC_t0: a For Each variable `t0` captured by a lambda. (Its C# cell moved to VB's output: see NowReachVb.)
        yield return new TestCaseData(lc, Bk.Cpp, "ran", "7\n0\n7\n0").SetName("LC_t0_Cpp_WrongBefore163AndAfter");
        yield return new TestCaseData(lc, Bk.JavaScript, "fails", "ReferenceError").SetName("LC_t0_JavaScript_ReferenceError_BeforeAndAfter");
        yield return new TestCaseData(lc, Bk.Msil, "failsAfterPrinting", "7").SetName("LC_t0_Msil_RunFail_WasAWrongAnswerBefore163");

        // R11: two For Each loops whose variables are t0 and t1. VB: 3 | 4 | 3 | 4.
        var r11 = TempProbes.R11;
        yield return new TestCaseData(r11, Bk.CSharp, "ran", "-3\n-4\n3\n4").SetName("R11_CSharp_MinusThreeMinusFourThreeFour");
        yield return new TestCaseData(r11, Bk.Cpp, "ran", "-3\n-4\n3\n4").SetName("R11_Cpp_MinusThreeMinusFourThreeFour_Was_6_8_Before163");
        yield return new TestCaseData(r11, Bk.JavaScript, "fails", "ReferenceError").SetName("R11_JavaScript_ReferenceError");
        yield return new TestCaseData(r11, Bk.Msil, "ran", "-3\n-4\n3\n4").SetName("R11_Msil_MinusThreeMinusFourThreeFour");
    }

    /// <summary>
    /// The by-name rule holds these programs at their pre-#163 answers. Without it (mutant M8) R11 prints VB's `3 | 4 | 3 | 4` in
    /// 12 of 12 cells — the removal of the orphan would have fixed it by accident, and would have broken `CT_wbr_t0` on C++. #121
    /// reserves the names and flips every row here.
    /// </summary>
    [TestCaseSource(nameof(Fence))]
    public void TheCollision_IsHeldAtItsCurrentAnswer_Until121(TempProbe probe, Bk backend, string how, string text)
        => TempExec.AssertPinnedInEveryEntryPoint(backend, probe.Source, how switch
        {
            "ran" => TempExec.Pin.Ran(text),
            "fails" => TempExec.Pin.RunFailed(text),
            "failsAfterPrinting" => TempExec.Pin.RunFailedAfterPrinting(text),
            _ => throw new ArgumentException(how),
        }, probe.Id);

    // ---- pinned CORRECT: the cells #163 moved to VB's output --------------------------------------

    private static IEnumerable<TestCaseData> NowReachVb()
    {
        (TempProbe Probe, Bk Backend)[] cells =
        {
            (TempProbes.CatchReadBeforeWrite("t2"), Bk.Cpp),   // C++ COMPILE-FAIL → OK
            (TempProbes.CatchReadBeforeWrite("t3"), Bk.Cpp),   // C++ COMPILE-FAIL → OK
            (TempProbes.CatchWriteBeforeRead("t2"), Bk.Cpp),   // C++ COMPILE-FAIL → OK
            (TempProbes.CatchWriteBeforeRead("t3"), Bk.Cpp),   // C++ COMPILE-FAIL → OK
            (TempProbes.ForEachReadBeforeWrite("t1"), Bk.Cpp), // C++ WRONG → OK  (3 | 7 | -3 before)
            (TempProbes.ForEachWriteBeforeRead("t2"), Bk.Cpp), // C++ WRONG → OK  (… -7 | 7 | -7 before)
            (TempProbes.LcT1, Bk.Cpp),                         // C++ WRONG → OK  (7 | 0 | 7 | 0 before)
            (TempProbes.LcT0, Bk.CSharp),                      // C# WRONG → OK   (7 | -7 | 7 | -7 before)
            (TempProbes.LcT3, Bk.Msil),                        // MSIL RUN-FAIL → OK (an AccessViolationException before)
        };
        foreach (var (probe, backend) in cells)
            yield return new TestCaseData(probe, backend).SetName($"{probe.Id}_{backend}");
    }

    [TestCaseSource(nameof(NowReachVb))]
    public void ACollisionCell_NowReachesVbsOutput_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (moved toward VB by #163)");

    // ---- the fence's own roster ------------------------------------------------------------------

    /// <summary>
    /// The three tables above are the fence; a table that quietly loses a row is a fence with a hole in it. Pinned by count, and
    /// (since every other test here is table-driven, which <c>JsExecutionTierRosterTests</c> cannot count) the one plain test that
    /// lets this fixture be in that roster: its JavaScript cells run under Node.
    /// </summary>
    [Test]
    public void TheFenceTables_HaveTheirRows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Fence().Count(), Is.EqualTo(7), "LC_t0 on C++, JavaScript and MSIL; R11 on all four");
            Assert.That(NowReachVb().Count(), Is.EqualTo(9), "9 program × backend pairs = 27 cells that #163 moved to VB's output");
            Assert.That(Controls().Count(), Is.EqualTo(8), "LC_x and R11_yz on four backends");
        });
    }

    // ---- controls: ordinary names are VB-correct everywhere --------------------------------------

    private static IEnumerable<TestCaseData> Controls()
        => new[] { TempProbes.LcControl, TempProbes.R11Control }
            .SelectMany(p => TempExec.Backends(Bk.All).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    /// <summary>`LC_x` and `R11_yz`: the same shapes as `LC_t0` and R11 with ordinary names. Every temp-spelled failure above needs a
    /// temp-spelled, unreserved USER name, and nothing else.</summary>
    [TestCaseSource(nameof(Controls))]
    public void TheSameShapeWithAnOrdinaryName_IsVbCorrectEverywhere(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);
}
