using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0018 (#121), EXECUTION: the whole ADR-0017 Findings 3 witness matrix, compiled and RUN on the four backends that print,
/// through the real <c>BasicLang</c> CLI (standard passes), the CLI with <c>--optimize</c> (aggressive), and
/// <c>BasicCompiler.CompileProjectFiles</c> with <c>OptimizeAggressive</c> (the Release <c>.blproj</c> and IDE route), each compared
/// with VB's own output (<c>vbc</c>: every expectation below is the one <c>S/t163/witness/*.exp</c> holds, re-run under the SDK's
/// <c>vbc</c> for the #163 work and again for #121's controls).
///
/// <para><b>What the matrix is.</b> ADR-0017 Findings 3 held 42 witness programs — a user variable that IRBuilder did not reserve, spelled
/// <c>t0</c>..<c>t3</c> — in twelve families. Ten of them (34 programs) print VB's answer on at least one backend and are the matrix: a
/// <c>Catch</c> variable read before / written before the orphans (CT_rbw, CT_wbr), a <c>For Each</c> variable likewise (FE_rbw,
/// FE_wbr), a For Each variable captured by a lambda (LC), a lambda parameter (LP), a pattern binding (PB), two For Each loops (R11), a
/// Catch (R12) and a <c>ReDim</c>ed array (RD). The other two are not in it — LQ (a LINQ range variable) and RD2 (a <c>ReDim</c> of an
/// undeclared array) printed on no backend when #121 landed, exactly as their controls did. (Since #224 LQ runs on C# and JavaScript and is refused by name on
/// C++ and MSIL; its reservation is pinned at the IR level and RUN in <c>TempMintingFacilityTests</c>, not in this matrix.) Before #121 the 42 collided with a
/// minted temp on 99 backend cells (C++ 15, C# 36, JavaScript 24, MSIL 24: wrong answers, compile failures, run failures). All 99 now
/// print VB's answer.</para>
///
/// <para><b>Three rows per cell</b>, each on every backend where the CONTROL prints VB's answer (the cells that still fail — LLVM,
/// LINQ on C++ and MSIL (refused by name since #224), patterns off C#, <c>ReDim</c> on MSIL, RD2 — fail exactly as their controls do, and are not this ADR's):
/// <list type="bullet">
/// <item>the witness, spelled <c>t{K}</c>;</item>
/// <item>the same program spelled <c>T{K}</c>: BasicLang is case-insensitive (ADR-0013), so the reservation must be too, and D2's
/// falsifier 5 names both spellings;</item>
/// <item>its control, spelled <c>v{K}</c> (the witness with <c>t</c> replaced by <c>v</c>): the same program with an ordinary name.</item>
/// </list></para>
///
/// <para>Rows <see cref="CompilerTempCollisionFenceTests"/> and <see cref="CompilerTempExecutionTests"/> already run are not
/// repeated (<see cref="CompilerTempCollisionFenceTests.Covered"/>); their <c>T</c> and <c>v</c> spellings are.</para>
///
/// <para>LLVM is not run: nothing it emits links (ADR-0017), before or after #121.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class NameReservationExecutionTests
{
    private const string Show = """
        Sub Show(n As Integer)
            Console.WriteLine(CStr(n))
        End Sub


        """;

    /// <summary>One family: a program with its variable spelled <c>{V}</c>, its VB output, and where it runs.</summary>
    private sealed record Family(string Id, Func<string, TempProbe> Make, int[] Ks, Bk Runs);

    private static TempProbe FromTemplate(string id, string template, string vb, string name)
        => new($"{id}_{name}", template.Replace("{V}", name), vb);

    private static readonly string LambdaParameterTemplate = Show + """
        Sub Run(a As Integer)
            Dim f As Func(Of Integer, Integer) = Function({V} As Integer) -(-{V}) + (({V} * 2) * 0)
            Show(-(-a))
            Show(f(a))
            Dim g As Func(Of Integer, Integer) = Function(x As Integer) x + a
            Show(g(1))
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """;

    private static readonly string PatternTemplate = Show + """
        Sub Run(o As Object, a As Integer)
            Select Case o
                Case {V} As Integer
                    Show(-(-a))
                    Show({V})
                    Show(-(-{V}))
                Case Else
                    Show((a * 2) * 0 + a)
            End Select
        End Sub

        Sub Main()
            Run(4, 7)
            Run("s", 7)
        End Sub
        """;

    private static readonly string CatchTwiceTemplate = Show + """
        Sub Run(a As Integer)
            Try
                Throw New Exception("x")
            Catch {V} As Exception
                Show(-(-a))
                Console.WriteLine({V}.Message)
            End Try
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """;

    private static readonly string ReDimTemplate = Show + """
        Sub Run(a As Integer)
            Dim {V}() As Integer
            ReDim {V}(2)
            Show(-(-a))
            {V}(1) = a
            Show((a * 2) * 0 + {V}(1))
            ReDim Preserve {V}(4)
            Show(-(-{V}(1)))
        End Sub

        Sub Main()
            Run(5)
        End Sub
        """;

    /// <summary>R11 spells TWO names, <c>t0</c> and <c>t1</c>; the other spellings replace both, keeping the digits.</summary>
    private static TempProbe TwoForEachLoops(string prefix)
    {
        if (prefix == "t") return TempProbes.R11;
        return new TempProbe($"R11_foreach_{prefix}0", TempProbes.R11.Source.Replace("t0", prefix + "0").Replace("t1", prefix + "1"), TempProbes.R11.Vb);
    }

    /// <summary>The name a family spells at index <paramref name="k"/> for a spelling <paramref name="prefix"/> (<c>t</c>, <c>T</c>, <c>v</c>).</summary>
    private static readonly Family[] Families =
    {
        new("CT_rbw", n => TempProbes.CatchReadBeforeWrite(n), new[] { 0, 1, 2, 3 }, Bk.All),
        new("CT_wbr", n => TempProbes.CatchWriteBeforeRead(n), new[] { 0, 1, 2, 3 }, Bk.All),
        new("FE_rbw", n => TempProbes.ForEachReadBeforeWrite(n), new[] { 0, 1, 2, 3 }, Bk.All),
        new("FE_wbr", n => TempProbes.ForEachWriteBeforeRead(n), new[] { 0, 1, 2, 3 }, Bk.All),
        new("LC", n => TempProbes.LambdaCaptureProbe(n), new[] { 0, 1, 2, 3 }, Bk.All),
        new("LP", n => FromTemplate("LP", LambdaParameterTemplate, "5\n5\n6", n), new[] { 0, 1, 2, 3 }, Bk.All),
        // A pattern binding compiles on C# alone: C++, JavaScript and MSIL refuse `Case x As Integer` (BL-FAIL, control included).
        new("PB", n => FromTemplate("PB", PatternTemplate, "7\n4\n4\n7", n), new[] { 0, 1, 2, 3 }, Bk.CSharp),
        new("R12", n => FromTemplate("R12_catch", CatchTwiceTemplate, "5\nx", n), new[] { 0 }, Bk.All),
        // ReDim does not compile on MSIL (BL-FAIL, control included).
        new("RD", n => FromTemplate("RD", ReDimTemplate, "5\n5\n5", n), new[] { 0, 1, 2, 3 }, Bk.CSharp | Bk.Cpp | Bk.JavaScript),
    };

    /// <summary>Every family's program at index K in a spelling; R11 is the one family that spells two names.</summary>
    private static IEnumerable<(string Family, TempProbe Probe, Bk Runs)> Programs(string prefix)
    {
        foreach (var family in Families)
            foreach (var k in family.Ks)
                yield return (family.Id, family.Make(prefix + k), family.Runs);
        yield return ("R11", TwoForEachLoops(prefix), Bk.All);
    }

    private static IEnumerable<TestCaseData> Cells(string prefix, bool skipCovered)
    {
        foreach (var (_, probe, runs) in Programs(prefix))
            foreach (var backend in TempExec.Backends(runs))
            {
                if (skipCovered && CompilerTempCollisionFenceTests.Covered.Contains((probe.Id, backend))) continue;
                yield return new TestCaseData(probe, backend).SetName($"{probe.Id}_{backend}");
            }
    }

    private static IEnumerable<TestCaseData> WitnessCells() => Cells("t", skipCovered: true);
    private static IEnumerable<TestCaseData> UpperCaseCells() => Cells("T", skipCovered: false);
    private static IEnumerable<TestCaseData> ControlCells() => Cells("v", skipCovered: false);

    /// <summary>
    /// ⭐ THE MATRIX. A For Each, Catch, pattern or lambda-parameter variable — or a ReDim'd array — spelled `t0`..`t3`: the program
    /// prints VB's answer on every backend that runs it, through all three entry points. A build that does not reserve one of those
    /// kinds returns its collision cells here (ADR-0018's "For Each / Catch / pattern / lambda parameter not reserved" mutants).
    /// </summary>
    [TestCaseSource(nameof(WitnessCells))]
    public void AReservedNameSpelledLikeATemp_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    /// <summary>The same programs spelled `T0`..`T3`. Reservation ignores case (ADR-0013): a user `T0` blocks the minted `t0` at
    /// IR level, and every backend's own counter skips it. D2's falsifier 5 names both spellings.
    ///
    /// <para>⚠ The four LP cells on MSIL (`Function(T0 As Integer) T0 * 2`) were refused before #121 and on its first cut, in every
    /// entry point: ClosureLowering's #169 case-guard compared the parameter with IRBuilder's STALE record of the lambda's own temp
    /// names (`t0,t1,t2,const_2,…` in <c>CapturedVariables</c>), and the temp `t0` differs from `T0` only by case. Found by this
    /// matrix and fixed in #121 (ADR-0018): the guard now compares only the variables the lambda's IR reads or writes that the creator
    /// owns. The direct tests are <c>ClosureLoweringRefusalTests.TheCaseGuard_*</c>.</para></summary>
    [TestCaseSource(nameof(UpperCaseCells))]
    public void TheUpperCaseSpelling_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (upper-case spelling)");

    /// <summary>The controls: each witness with an ordinary name (`v0`..`v3`), VB-correct on every backend where the witness is. The
    /// difference between a witness and its control is the NAME, and nothing else.</summary>
    [TestCaseSource(nameof(ControlCells))]
    public void TheSameProgramWithAnOrdinaryName_IsVbCorrect_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (control)");

    /// <summary>
    /// The matrix is the proof; a table that quietly loses a row is a proof with a hole in it. Pinned by count, and (since the other
    /// tests here are table-driven, which <c>JsExecutionTierRosterTests</c> cannot count) the one plain test that lets this fixture be
    /// in that roster: its JavaScript cells run under Node.
    /// </summary>
    [Test]
    public void TheMatrix_HasItsRows()
    {
        var witnesses = Programs("t").SelectMany(p => TempExec.Backends(p.Runs).Select(b => (p.Probe.Id, Backend: b))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Programs("t").Count(), Is.EqualTo(34), "eight families × four names, R12 alone, and R11 (which spells two names)");
            Assert.That(witnesses, Has.Count.EqualTo(120), "the 120 witness cells that print VB's answer on this base (ADR-0018's witness matrix)");
            Assert.That(witnesses, Is.SupersetOf(CompilerTempCollisionFenceTests.Covered), "every cell the other two fixtures run is a cell of the matrix");
            Assert.That(WitnessCells().Count(), Is.EqualTo(120 - CompilerTempCollisionFenceTests.Covered.Count), "…and it repeats none of them");
            Assert.That(UpperCaseCells().Count(), Is.EqualTo(120), "the upper-case rows, the four MSIL LP_T* cells included (ADR-0018)");
            Assert.That(ControlCells().Count(), Is.EqualTo(120));
        });
    }

    /// <summary>The spellings are what they claim: a `t` witness contains its `t{K}`, a `T` row its `T{K}`, a control its `v{K}` — and
    /// no other spelling — so a row that lost its substitution (a template whose `{V}` no longer matches) fails here, not silently.</summary>
    [Test]
    public void TheSpellings_AreWhatTheyClaim()
    {
        Assert.Multiple(() =>
        {
            foreach (var prefix in new[] { "t", "T", "v" })
                foreach (var (_, probe, _) in Programs(prefix))
                {
                    Assert.That(System.Text.RegularExpressions.Regex.IsMatch(probe.Source, $@"\b{prefix}[0-3]\b"), Is.True, $"{probe.Id} spells no {prefix}<digit>");
                    foreach (var other in new[] { "t", "T", "v" }.Where(c => c != prefix))
                        Assert.That(System.Text.RegularExpressions.Regex.IsMatch(probe.Source, $@"\b{other}[0-3]\b"), Is.False,
                            $"{probe.Id} also spells {other}<digit>: the spellings are mixed");
                }
        });
    }
}
