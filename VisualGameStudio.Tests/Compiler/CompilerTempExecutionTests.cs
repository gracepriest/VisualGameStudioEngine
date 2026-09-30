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
    // ⭐ CT_wbr_t0 — ADR-0017's by-name witness, held by RESERVATION since ADR-0018 (#121)
    // ============================================================================================

    private static IEnumerable<TestCaseData> WitnessCells() => Cells(TempProbes.CtWbrT0);

    /// <summary>
    /// ⭐ `Catch t0` with orphans before the Try. ADR-0017 kept the orphan `t0 = -a` by NAME, because deleting it renumbered the C++
    /// backend's own temp counter and put a surviving string temp on the user's `Catch t0`
    /// (`t0 = BasicLang::String(t0.what())`, clang: "no viable overloaded '='"). ADR-0018 D4 removed that rule: IRBuilder now
    /// RESERVES a Catch variable's name (D1), so no counter hands it out and the orphan is deleted like any other. The program
    /// compiles AND prints VB's answer on all four backends in every entry point — C# included, which did not compile before #121.
    /// <b>A build that does not reserve a Catch variable (ADR-0018's "Catch not reserved" mutant) returns the C++
    /// COMPILE-FAIL and fails this test.</b>
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
/// ⭐ The collision fence, FLIPPED (ADR-0018, #121). A user variable that IRBuilder did not reserve — a <c>For Each</c>,
/// <c>Catch</c>, pattern or LINQ range variable — spelled like a temp (<c>t0</c>) shared its name with a minted temp, so every
/// program here was wrong or failing on some backend before #163 (ADR-0017 Findings 3: 99 witness cells). ADR-0017 fenced them
/// as CURRENT behaviour and held one cell compiling with a by-name keep in dead-code elimination. ADR-0018 reserves every name
/// the program owns and removes the keep, so each cell prints VB's answer and this fixture says so.
///
/// <list type="bullet">
/// <item><b>Moved by #121</b> (<see cref="MovedBy121"/>): <c>LC_t0</c> on C++, JavaScript and MSIL (it was a wrong answer, a
/// ReferenceError and a segmentation fault) and R11 on all four backends (wrong on three, a ReferenceError on JavaScript).</item>
/// <item><b>Moved by #163</b> (<see cref="NowReachVb"/>): the 27 cells (9 program × backend pairs, three entry points each) that
/// reached VB's answer when dead-code elimination began deleting unused temps. Unchanged by #121.</item>
/// <item><b>Controls</b> (<see cref="Controls"/>): the same shapes with ordinary names, VB-correct everywhere before and after.</item>
/// </list>
///
/// <para>The rest of the ADR-0017 Findings 3 witness matrix (every program that now gives VB's answer, on every backend where it
/// does, with its control) is <see cref="NameReservationExecutionTests"/>; the cells listed here are the ones it does not repeat
/// (<see cref="Covered"/>).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CompilerTempCollisionFenceTests
{
    // ---- moved by #121: pinned WRONG or failing until then, VB's answer now --------------------------

    // LC_t0: a For Each variable `t0` captured by a lambda. (Its C# cell moved to VB's output with #163: see NowReachVbCells.)
    // R11: two For Each loops whose variables are t0 and t1. VB: 3 | 4 | 3 | 4.
    private static readonly (TempProbe Probe, Bk Backend)[] MovedBy121Cells =
    {
        (TempProbes.LcT0, Bk.Cpp),        // was WRONG `7|0|7|0`
        (TempProbes.LcT0, Bk.JavaScript), // was a ReferenceError
        (TempProbes.LcT0, Bk.Msil),       // was WRONG `7|-3|7|-4` before #163, `7` then a segmentation fault after it
        (TempProbes.R11, Bk.CSharp),      // was WRONG `-3|-4|3|4`
        (TempProbes.R11, Bk.Cpp),         // was WRONG `-3|-4|3|4` (`-3|-4|6|8` before #163)
        (TempProbes.R11, Bk.JavaScript),  // was a ReferenceError
        (TempProbes.R11, Bk.Msil),        // was WRONG `-3|-4|3|4`
    };

    private static IEnumerable<TestCaseData> MovedBy121()
        => MovedBy121Cells.Select(c => new TestCaseData(c.Probe, c.Backend).SetName($"{c.Probe.Id}_{c.Backend}"));

    /// <summary>
    /// The seven cells ADR-0017 pinned as wrong or failing (its "held until #121" table). #121 reserves the For Each variable's name,
    /// so the renamer separates it from the minted temp on every backend, and each cell prints VB's answer in all three entry
    /// points. <b>A build that does not reserve a For Each variable (ADR-0018's "For Each not reserved" mutant) fails them.</b>
    /// </summary>
    [TestCaseSource(nameof(MovedBy121))]
    public void ACollisionCellMovedBy121_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (moved to VB's answer by #121)");

    // ---- moved by #163: the cells dead-code elimination moved to VB's output ------------------------

    private static readonly (TempProbe Probe, Bk Backend)[] NowReachVbCells =
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

    private static IEnumerable<TestCaseData> NowReachVb()
        => NowReachVbCells.Select(c => new TestCaseData(c.Probe, c.Backend).SetName($"{c.Probe.Id}_{c.Backend}"));

    [TestCaseSource(nameof(NowReachVb))]
    public void ACollisionCell_NowReachesVbsOutput_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (moved toward VB by #163)");

    // ---- the fence's own roster ------------------------------------------------------------------

    /// <summary>
    /// The (probe, backend) pairs this fixture and <see cref="CompilerTempExecutionTests"/> already run, by probe id, so that
    /// <see cref="NameReservationExecutionTests"/> repeats none of them: the two tables above and the `CT_wbr_t0` witness.
    /// </summary>
    internal static IReadOnlySet<(string Id, Bk Backend)> Covered { get; } = MovedBy121Cells.Concat(NowReachVbCells)
        .Concat(TempExec.Backends(Bk.All).Select(b => (Probe: TempProbes.CtWbrT0, Backend: b)))
        .Select(c => (c.Probe.Id, c.Backend))
        .ToHashSet();

    /// <summary>
    /// The tables are the fence; a table that quietly loses a row is a fence with a hole in it. Pinned by count, and (since every
    /// other test here is table-driven, which <c>JsExecutionTierRosterTests</c> cannot count) the one plain test that lets this
    /// fixture be in that roster: its JavaScript cells run under Node.
    /// </summary>
    [Test]
    public void TheFenceTables_HaveTheirRows()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MovedBy121().Count(), Is.EqualTo(7), "LC_t0 on C++, JavaScript and MSIL; R11 on all four");
            Assert.That(NowReachVb().Count(), Is.EqualTo(9), "9 program × backend pairs = 27 cells that #163 moved to VB's output");
            Assert.That(Controls().Count(), Is.EqualTo(8), "LC_x and R11_yz on four backends");
            Assert.That(Covered, Has.Count.EqualTo(7 + 9 + 4), "the two tables and CT_wbr_t0 on four backends: no pair is listed twice");
        });
    }

    // ---- controls: ordinary names are VB-correct everywhere --------------------------------------

    private static IEnumerable<TestCaseData> Controls()
        => new[] { TempProbes.LcControl, TempProbes.R11Control }
            .SelectMany(p => TempExec.Backends(Bk.All).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    /// <summary>`LC_x` and `R11_yz`: the same shapes as `LC_t0` and R11 with ordinary names. Every temp-spelled failure ADR-0017
    /// fenced needed a temp-spelled, unreserved USER name, and nothing else.</summary>
    [TestCaseSource(nameof(Controls))]
    public void TheSameShapeWithAnOrdinaryName_IsVbCorrectEverywhere(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);
}
