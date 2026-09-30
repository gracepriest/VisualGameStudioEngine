using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0017 (#163) on REAL IR: IRBuilder's output through the shipped standard and aggressive
/// pipelines, with the dead-code pass recorded (<see cref="RecordingDeadCodePass"/>) or taken out
/// (<see cref="TempIr.PipelineWithoutDce"/>). Fast tier: front end and optimizer, no backend, no process.
///
/// <para>Every claim of the ADR's "Measured at landing" that only IR can see is pinned here, each in its two
/// halves — the value the pass keeps is FLAGGED and would be removed if the one clause under test did not hold,
/// and the value it removes is deleted. The execution tier (<see cref="CompilerTempExecutionTests"/>) proves the
/// outputs; this file proves WHY they are the outputs.</para>
/// </summary>
[TestFixture]
[NonParallelizable] // the verifier test sets IRVerifier.Mode, which is process-wide
public class DeadCodeRemovalOnRealIrTests
{
    private static IEnumerable<bool> Pipelines => new[] { false, true };

    private static string Label(bool aggressive) => aggressive ? "aggressive" : "standard";

    // ============================================================================================
    // What is removed: the orphans a peephole rewrite leaves, and only compiler temps
    // ============================================================================================

    /// <summary>Per probe, the removals the ADR measured: kind → count. (`Binary.Shl` in R2 is the strength-reduced
    /// multiply, which is why M2 is visible there.)</summary>
    private static IEnumerable<TestCaseData> Removals(bool aggressive)
    {
        yield return new TestCaseData(TempProbes.R1, "Unary.Neg=1 Unary.Not=1", aggressive).SetName($"R1_{Label(aggressive)}");
        yield return new TestCaseData(TempProbes.R2, "Binary.Shl=1", aggressive).SetName($"R2_{Label(aggressive)}");
        yield return new TestCaseData(TempProbes.R7, "Binary.Mul=1 Unary.Neg=1", aggressive).SetName($"R7_{Label(aggressive)}");
        yield return new TestCaseData(TempProbes.R8, "Unary.Not=3", aggressive).SetName($"R8_{Label(aggressive)}");
        yield return new TestCaseData(TempProbes.U4, "Unary.Neg=1", aggressive).SetName($"U4_{Label(aggressive)}");
        yield return new TestCaseData(TempProbes.U5, "Unary.Neg=1", aggressive).SetName($"U5_{Label(aggressive)}");
    }

    private static IEnumerable<TestCaseData> RemovalsStandard() => Removals(false);
    private static IEnumerable<TestCaseData> RemovalsAggressive() => Removals(true);

    [TestCaseSource(nameof(RemovalsStandard))]
    [TestCaseSource(nameof(RemovalsAggressive))]
    public void ThePassDeletesTheOrphans_AndOnlyMintedFlaggedTemps(TempProbe probe, string expectedKinds, bool aggressive)
    {
        var without = TempIr.Build(probe.Source);
        TempIr.PipelineWithoutDce(aggressive).Run(without);
        var orphansWithout = TempIr.PureOrphans(without);

        var module = TempIr.Build(probe.Source);
        var recorder = TempIr.RunRecording(module, aggressive);

        Assert.Multiple(() =>
        {
            Assert.That(orphansWithout, Is.Not.Empty, "precondition: with nothing deleting them, the probe leaves orphans behind");
            Assert.That(RecordingDeadCodePass.KindCounts(recorder.Removals), Is.EqualTo(expectedKinds), "what was deleted, by kind (ADR-0017 'Removals')");
            Assert.That(recorder.Removals.Count, Is.EqualTo(orphansWithout.Count), "every orphan the optimizer left was deleted, and nothing else");
            Assert.That(TempIr.PureOrphans(module), Is.Empty, "no unused compiler temp of a removable kind survives the shipped pipeline");
            foreach (var removal in recorder.Removals)
            {
                Assert.That(removal.Value.IsCompilerTemp, Is.True, $"{removal.Kind} '{removal.Value.Name}' was deleted without carrying the flag");
                Assert.That(removal.Value.NamedAfterVariable, Is.False, $"{removal.Kind} '{removal.Value.Name}' is user storage");
                Assert.That(removal.Function.IsMintedTempName(removal.Value.Name), Is.True, $"{removal.Kind} '{removal.Value.Name}' is not a name its function minted");
            }
        });
    }

    /// <summary>The ADR's own totals over its 18 probes (R1-R10, U1-U8): 10 removals per cell — Neg 4, Not 4, Mul 1,
    /// Shl 1 (140 over 14 cells, identical on every backend because the IR is backend-independent).</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TheEighteenProbes_RemoveExactlyTheKindsTheADRMeasured(bool aggressive)
    {
        var sources = new[]
        {
            TempProbes.R1.Source, TempProbes.R2.Source, TempProbes.R3.Source, TempProbes.R4.Source, TempProbes.R5.Source,
            TempProbes.R6.Source, TempProbes.R7.Source, TempProbes.R8.Source, TempProbes.R9Source, TempProbes.R10.Source,
            TempProbes.U1.Source, TempProbes.U2.Source, TempProbes.U3.Source, TempProbes.U4.Source, TempProbes.U5.Source,
            TempProbes.U6.Source, TempProbes.U7.Source, TempProbes.U8.Source,
        };
        Assert.That(sources, Has.Length.EqualTo(18));

        var removals = sources.SelectMany(s => TempIr.RunRecording(TempIr.Build(s), aggressive).Removals).ToList();

        Assert.That(RecordingDeadCodePass.KindCounts(removals), Is.EqualTo("Binary.Mul=1 Binary.Shl=1 Unary.Neg=4 Unary.Not=4"), "ADR-0017: 'Neg 56, Not 56, Mul 14, Shl 14' over 14 cells");
    }

    // ============================================================================================
    // What is never removed: user storage spelled like a temp (the U probes)
    // ============================================================================================

    private static IEnumerable<TestCaseData> USpellings()
    {
        // (probe, orphans an optimizer rewrite really leaves and the pass removes: `-(-a)` in U4 and U5, none elsewhere)
        yield return new TestCaseData(TempProbes.U1, 0).SetName("U1_LocalT5");
        yield return new TestCaseData(TempProbes.U2, 0).SetName("U2_LocalTmp1");
        yield return new TestCaseData(TempProbes.U3, 0).SetName("U3_LocalUpperT5");
        yield return new TestCaseData(TempProbes.U4, 1).SetName("U4_LocalUnderscoreT");
        yield return new TestCaseData(TempProbes.U5, 1).SetName("U5_FieldsT0T1");
        yield return new TestCaseData(TempProbes.U6, 0).SetName("U6_GlobalT3");
        yield return new TestCaseData(TempProbes.U7, 0).SetName("U7_UnusedUserTemps");
        yield return new TestCaseData(TempProbes.U8, 0).SetName("U8_LoopAccumulatorT1");
    }

    /// <summary>Everything a user wrote as a definition of a named variable, per function: an assignment's target, and a
    /// value renamed after a variable (`Dim t5 = a + b`, `t3 = n + 7`). Sorted, so two builds compare.</summary>
    private static List<string> UserDefinitions(IRModule module)
        => TempIr.Functions(module).SelectMany(f => TempIr.Instructions(f).Select(i => i switch
        {
            IRAssignment a when a.Target != null => f.Name + ":" + a.Target.Name,
            IRValue v when v.NamedAfterVariable => f.Name + ":" + v.Name,
            _ => null,
        })).Where(s => s != null).OrderBy(s => s, StringComparer.Ordinal).ToList();

    [TestCaseSource(nameof(USpellings))]
    public void UserVariablesSpelledLikeTemps_AreNeverRemoved_InEitherPipeline(TempProbe probe, int expectedRemovals)
    {
        foreach (var aggressive in Pipelines)
        {
            var without = TempIr.Build(probe.Source);
            TempIr.PipelineWithoutDce(aggressive).Run(without);

            var with = TempIr.Build(probe.Source);
            var recorder = TempIr.RunRecording(with, aggressive);

            Assert.Multiple(() =>
            {
                Assert.That(UserDefinitions(with), Is.Not.Empty, $"{probe.Id}/{Label(aggressive)}: the probe defines user variables");
                Assert.That(UserDefinitions(with), Is.EqualTo(UserDefinitions(without)),
                    $"{probe.Id}/{Label(aggressive)}: the dead-code pass removed a definition of a user variable");
                Assert.That(recorder.Removals.Count, Is.EqualTo(expectedRemovals), $"{probe.Id}/{Label(aggressive)}: {RecordingDeadCodePass.KindCounts(recorder.Removals)}");
                Assert.That(recorder.Removals.Select(r => r.Value.NamedAfterVariable), Has.None.True);
            });
        }
    }

    /// <summary>
    /// ⭐ U2, said in a test: BEFORE #163 this program printed `0` for VB's `67` in 12 of 12 cells. The old guard
    /// removed every unused value whose name started with `_tmp`; `Dim _tmp1 = a + b` is such a value (later reads
    /// are reads of the variable, so the renamed binop has no operand use). Here is the IR half: the two `_tmp` values
    /// are user storage, defined after the pass exactly as before it.
    /// </summary>
    [Test]
    public void U2_TheUserVariablesCalledTmp_SurviveTheDeadCodePass()
    {
        var module = TempIr.Build(TempProbes.U2.Source);
        var f = TempIr.Functions(module).Single(x => x.Name == "F");
        var tmp = TempIr.Instructions(f).OfType<IRValue>().Where(v => v.Name.StartsWith("_tmp", StringComparison.Ordinal) && v is not IRVariable).ToList();

        TempIr.Pipeline(false).Run(module);

        Assert.Multiple(() =>
        {
            Assert.That(tmp.Select(v => v.Name), Is.EquivalentTo(new[] { "_tmp1", "_tmp2" }));
            Assert.That(tmp.Select(v => v.NamedAfterVariable), Has.All.True, "they ARE the storage of the user's variables");
            Assert.That(tmp.Select(v => v.IsCompilerTemp), Has.All.False);
            foreach (var v in tmp)
                Assert.That(TempIr.Instructions(f).Any(i => ReferenceEquals(i, v)), Is.True, $"{v.Name} was removed");
        });
    }

    // ============================================================================================
    // ⭐ M6 — settled point 3, on the shapes built to hit it (R3 and R5)
    // ============================================================================================

    /// <summary>The unused compiler temps left standing after the shipped pipeline, each with the one fact that keeps it.</summary>
    private static List<IRValue> Survivors(string source, bool aggressive)
    {
        var module = TempIr.Optimized(source, aggressive);
        var orphans = TempIr.PureOrphans(module);
        foreach (var orphan in orphans)
            Assert.That(orphan.IsCompilerTemp, Is.True, "precondition: a survivor is flagged, so only a clause of the licence keeps it");
        return orphans;
    }

    /// <summary>R3 `Show(-(-Tag()))`: the dead inner `-` and `Show(...)` are the call's two uses, adjacent.
    /// R5 `Show2(-(-F()), G())`: the dead inner `-` and `Show2` are F's two uses, with the `G()` call BETWEEN them —
    /// "single use and not adjacent" once the `-` is gone — and `Dim x = -(-F())` is the adjacent twin. Removal would move
    /// C#'s evaluation of the call (R5's Trail `FGF` is the order). All three refusals are D3's alone.</summary>
    [TestCase("R3", 1, TestName = "R3_OneAdjacentRefusal")]
    [TestCase("R5", 2, TestName = "R5_TheNotAdjacentRefusal_AndItsAdjacentTwin")]
    public void SettledPoint3_RefusesTheDeletionThatWouldMoveACallsEvaluation(string which, int refusals)
    {
        var source = which == "R3" ? TempProbes.R3.Source : TempProbes.R5.Source;
        foreach (var aggressive in Pipelines)
        {
            var module = TempIr.Optimized(source, aggressive);
            var main = TempIr.Functions(module).Single(f => f.Name == "Main");
            var survivors = TempIr.PureOrphans(module);

            Assert.That(survivors, Has.Count.EqualTo(refusals), $"{which}/{Label(aggressive)}: the dead negations D3 refuses to delete");
            var counts = RecordingDeadCodePass.UseCounts(main);
            foreach (var survivor in survivors.Cast<IRUnaryOp>())
            {
                Assert.Multiple(() =>
                {
                    Assert.That(survivor.Operation, Is.EqualTo(UnaryOpKind.Neg));
                    Assert.That(survivor.Operand, Is.InstanceOf<IRCall>(), "over a call: a non-replicable operand");
                    Assert.That(survivor.IsCompilerTemp, Is.True);
                    Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(survivor, main), Is.True,
                        "isolation: the licence's other clauses all allow it, so settled point 3 alone keeps it");
                    Assert.That(counts[survivor.Operand], Is.EqualTo(2), "the call keeps its two uses: it is still declared where it was computed");
                });
            }
        }
    }

    /// <summary>The property itself, over every probe: whatever the pass deleted, a non-replicable operand of it keeps
    /// at least two operand uses if it had them, and keeps its one if it had one. Re-measured by the test, from the IR
    /// before and after each run — not by asking the pass whether it complied.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void NoDeletion_TakesANonReplicableOperandBelowTwoUses_OrBelowItsOwnCountOfOne(bool aggressive)
    {
        var sources = new List<string>
        {
            TempProbes.R1.Source, TempProbes.R2.Source, TempProbes.R3.Source, TempProbes.R4.Source, TempProbes.R5.Source, TempProbes.R6.Source,
            TempProbes.R7.Source, TempProbes.R8.Source, TempProbes.R9Source, TempProbes.R10.Source, TempProbes.U1.Source, TempProbes.U2.Source,
            TempProbes.U3.Source, TempProbes.U4.Source, TempProbes.U5.Source, TempProbes.U6.Source, TempProbes.U7.Source, TempProbes.U8.Source,
            TempProbes.CtWbrT0.Source, TempProbes.R11.Source, TempProbes.LcT0.Source, TempProbes.CtWbrK.Source,
        };

        var protectedCandidates = 0;
        foreach (var source in sources)
        {
            var recorder = TempIr.RunRecording(TempIr.Build(source), aggressive);
            foreach (var count in recorder.OperandCounts.Where(c => !IRReplicability.IsReplicable(c.Operand)))
                Assert.That(count.After, Is.GreaterThanOrEqualTo(Math.Min(count.Before, 2)),
                    $"a deletion took '{count.Operand.Name}' ({count.Operand.GetType().Name}) from {count.Before} operand uses to {count.After}: " +
                    "the C# backend would now evaluate it at its one remaining use, or emit it as a statement (ADR-0008 settled point 3)");

            // The property must have had something to protect: dead consumers of a non-replicable operand that the pass REFUSED to delete.
            var survivors = TempIr.PureOrphans(TempIr.Optimized(source, aggressive));
            protectedCandidates += survivors.Count(o => o is IRUnaryOp u && !IRReplicability.IsReplicable(u.Operand));
        }
        Assert.That(protectedCandidates, Is.GreaterThan(0),
            "no probe left a dead consumer of a non-replicable operand standing: the property above is vacuous (R3 and R5 are built to be such probes)");
    }

    // ============================================================================================
    // Kept by KIND on real IR (M3, M7, M9, M10): the survivor is flagged, unused, and of an unlicensed kind
    // ============================================================================================

    private static IRFunction Run(IRModule module) => TempIr.Functions(module).Single(f => f.Name == "Run");

    private static IRValue OnlyUnusedFlagged<T>(IRModule module, IRFunction function, Func<T, bool> predicate) where T : IRValue
    {
        var used = TempIr.Used(function);
        var found = TempIr.Instructions(function).OfType<T>().Where(predicate).ToList();
        Assert.That(found, Has.Count.EqualTo(1), $"expected exactly one {typeof(T).Name} in {function.Name}");
        Assert.That(found[0].IsCompilerTemp, Is.True, "precondition: flagged, so only its KIND keeps it");
        Assert.That(used.Contains(found[0]), Is.False, "precondition: nothing uses it");
        return found[0];
    }

    /// <summary>R6 `(a \ z) * 0`: the multiply is the orphan and goes; the division is unused, flagged and STAYS, because
    /// an integer division by zero throws and the program catches it (VB prints `caught`). M7.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void R6_TheDivisionSurvives_BecauseItCanTrap(bool aggressive)
    {
        var module = TempIr.Optimized(TempProbes.R6.Source, aggressive);
        var division = OnlyUnusedFlagged<IRBinaryOp>(module, Run(module), b => b.Operation == BinaryOpKind.IntDiv);

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(division, Run(module)), Is.False);
        Assert.That(TempIr.Instructions(Run(module)).OfType<IRBinaryOp>().Any(b => b.Operation == BinaryOpKind.Mul), Is.False, "and the orphaned multiply went");
    }

    /// <summary>R9 `(++a) * 0`: `++` writes `a`. The unary is unused, flagged and stays. (On real IR the kill vocabulary
    /// refuses it too — that is why M9 changes no output; the hand-built isolation test kills M9.)</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void R9_TheIncrementSurvives_BecauseItWritesItsOperand(bool aggressive)
    {
        var module = TempIr.Optimized(TempProbes.R9Source, aggressive);
        var increment = OnlyUnusedFlagged<IRUnaryOp>(module, Run(module), u => u.Operation == UnaryOpKind.Inc);

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(increment, Run(module)), Is.False);
    }

    /// <summary>R10 `arr(i) * 0`, `i` out of range: the element read can trap (MSIL keeps it: `caught`). The load is
    /// unused, flagged and stays. (Settled point 3 would also refuse it — the element pointer would lose its only use —
    /// so M10 changes no output; the isolation test kills M10.)</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void R10_TheElementReadSurvives_BecauseItCanTrap(bool aggressive)
    {
        var module = TempIr.Optimized(TempProbes.R10.Source, aggressive);
        var load = OnlyUnusedFlagged<IRLoad>(module, Run(module), l => l.Address is IRGetElementPtr);

        Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(load, Run(module)), Is.False);
    }

    /// <summary>R3: both `Tag()` calls run. A call is never removed, however dead its result.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void R3_BothCallsToTagSurvive(bool aggressive)
    {
        var module = TempIr.Optimized(TempProbes.R3.Source, aggressive);
        var main = TempIr.Functions(module).Single(f => f.Name == "Main");

        Assert.That(TempIr.Instructions(main).OfType<IRCall>().Count(c => c.FunctionName == "Tag"), Is.EqualTo(2));
    }

    // ============================================================================================
    // ⭐ M5 — the base call's operand (the #170 interaction), on real IR
    // ============================================================================================

    /// <summary>R4 `MyBase.New(v + 1)`: the temp's only use is an <c>IRBaseConstructorCall</c> operand. It is flagged, and it stays;
    /// the verifier's Invariant P (an operand defined before the call) holds after both pipelines. Without the use
    /// analysis seeing the base call, DCE deletes it: Invariant P(a) fires, C++ does not compile, MSIL throws
    /// InvalidProgramException.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void R4_TheBaseCallsOperandSurvives_AndInvariantPHolds(bool aggressive)
    {
        var module = TempIr.Optimized(TempProbes.R4.Source, aggressive);
        var constructor = TempIr.Functions(module).Single(f => TempIr.Instructions(f).OfType<IRBaseConstructorCall>().Any());
        var call = TempIr.Instructions(constructor).OfType<IRBaseConstructorCall>().Single();
        var computed = call.Args.Where(a => a is not IRVariable && a is not IRConstant).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(computed, Has.Count.EqualTo(1), "`v + 1`");
            Assert.That(computed[0].IsCompilerTemp, Is.True, "precondition: a licensed temp");
            Assert.That(DeadCodeEliminationPass.IsRemovableWhenUnused(computed[0], constructor), Is.True, "precondition: nothing but its USE keeps it");
            Assert.That(TempIr.Instructions(constructor).Any(i => ReferenceEquals(i, computed[0])), Is.True, "the operand is still defined before the call");
            Assert.That(IRVerifier.CheckInvariantP(module), Is.Empty);
        });
    }

    // ============================================================================================
    // ADR-0017 Finding 2 — the dead-store cascade (R7)
    // ============================================================================================

    /// <summary>R7 `Dim n = 0 : n = (a * 3) * 0`: the peephole's dead-store arm only sees ADJACENT stores. With the orphan
    /// `a * 3` gone the two stores to `n` are adjacent and the first is dropped: one definition of `n` with the pass, two
    /// without. The output is unchanged (`0 | 8 | 7`, VB's; see the execution tier); this is the byte diff the classifier called
    /// DEADSTORE-CASCADE.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void R7_TheDeadStoreCascade_LeavesOneStoreOfN_Not2(bool aggressive)
    {
        int Definitions(IRModule module) => TempIr.Instructions(Run(module)).Count(i =>
            (i is IRAssignment a && a.Target?.Name == "n") || (i is IRValue v && v.Name == "n" && v is not IRVariable));

        var with = TempIr.Build(TempProbes.R7.Source);
        TempIr.Pipeline(aggressive).Run(with);
        var without = TempIr.Build(TempProbes.R7.Source);
        TempIr.PipelineWithoutDce(aggressive).Run(without);

        Assert.Multiple(() =>
        {
            Assert.That(Definitions(without), Is.EqualTo(2), "without the pass, both stores stay (the orphan sits between them)");
            Assert.That(Definitions(with), Is.EqualTo(1), "with it, they are adjacent and the first is dead");
        });
    }

    // ============================================================================================
    // The verifier stays silent (ADR-0017: 'Verifier fires: 0')
    // ============================================================================================

    [Test]
    public void TheVerifier_StaysSilent_OverEveryProbe_InBothPipelines()
    {
        var previous = IRVerifier.Mode;
        IRVerifier.Mode = IRVerifierMode.Throw;
        try
        {
            foreach (var probe in AllProbes())
                foreach (var aggressive in Pipelines)
                {
                    var module = TempIr.Build(probe.Source);
                    Assert.DoesNotThrow(() =>
                    {
                        TempIr.Pipeline(aggressive).Run(module);
                        IRVerifier.VerifyAfterOptimization(module);
                    }, $"{probe.Id}/{Label(aggressive)}: the verifier fired after the dead-code pass");
                }
        }
        finally { IRVerifier.Mode = previous; }
    }

    private static IEnumerable<TempProbe> AllProbes() => new[]
    {
        TempProbes.R1, TempProbes.R2, TempProbes.R3, TempProbes.R4, TempProbes.R5, TempProbes.R6, TempProbes.R7, TempProbes.R8, TempProbes.R10,
        TempProbes.U1, TempProbes.U2, TempProbes.U3, TempProbes.U4, TempProbes.U5, TempProbes.U6, TempProbes.U7, TempProbes.U8,
        TempProbes.CtWbrT0, TempProbes.CtWbrK, TempProbes.R11, TempProbes.R11Control, TempProbes.LcT0, TempProbes.LcT1, TempProbes.LcT3, TempProbes.LcControl,
        new TempProbe("R9_Increment", TempProbes.R9Source, ""),
    };

    // ============================================================================================
    // "No effect on existing programs": the in-repo corpus
    // ============================================================================================

    /// <summary>Test programs that DO contain the orphan shape #163 exists to clean up, and what the pass deletes there.
    /// Both are the shape by design: <c>OptimizerOrphanedTempTests</c> is the fixture about optimizer-orphaned temps, and
    /// <c>NotPrecedenceExecutionTests</c> writes `Not Not n &lt; 3`. Measured at #163: 2 programs of 674, 3 deletions per
    /// pipeline, each a flagged, minted, pure temp.</summary>
    private static readonly Dictionary<string, string> KnownOrphanPrograms = new()
    {
        ["NotPrecedenceExecutionTests.Program"] = "Unary.Not=1",
        ["OptimizerOrphanedTempTests.FoldProgram"] = "Unary.Neg=1 Unary.Not=1",
    };

    private static bool IsThisTasksOwnFixture(Type type)
    {
        var name = type.Name;
        return name.StartsWith("CompilerTemp", StringComparison.Ordinal) || name.StartsWith("DeadCodeRemoval", StringComparison.Ordinal)
            || name is nameof(TempProbes) or nameof(TempIr) or nameof(TempExec) or nameof(RecordingDeadCodePass);
    }

    /// <summary>Every string constant or static field in the test assembly that holds a BasicLang program (contains `Sub Main` or
    /// `Function Main`), deduplicated by text, other than this task's own probes.</summary>
    internal static IEnumerable<(string Origin, string Source)> ExistingTestPrograms()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(DeadCodeRemovalOnRealIrTests).Assembly.GetTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (IsThisTasksOwnFixture(type)) continue;
            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (field.FieldType != typeof(string)) continue;
                string text;
                try { text = (string)field.GetValue(null); } catch (Exception) { continue; }
                if (text == null) continue;
                if (!text.Contains("Sub Main", StringComparison.OrdinalIgnoreCase) && !text.Contains("Function Main", StringComparison.OrdinalIgnoreCase)) continue;
                if (seen.Add(text)) yield return (type.Name + "." + field.Name, text);
            }
        }
    }

    /// <summary>
    /// ⭐ The claim "#163 changes no existing program", as a test. Every program the suite already holds is taken through
    /// both shipped pipelines with the dead-code pass recorded. Measured: 720 candidate strings, 674 build, and the pass deletes
    /// something in exactly TWO — the two that contain the orphan shape on purpose (<see cref="KnownOrphanPrograms"/>), deleting
    /// only flagged, minted, pure temps. A new deletion anywhere else fails here, so it is a decision and not a side effect.
    ///
    /// <para>⚠ The brief asked for "removes nothing", and that is not what the in-repo corpus measures: the implementer's
    /// 854-program corpus (probes of earlier tasks, samples) had 0, but it never held these two programs.</para>
    /// </summary>
    [Test]
    public void ExistingTestPrograms_LoseNothing_ExceptTheTwoThatContainTheOrphanShape()
    {
        var built = 0;
        var unexpected = new List<string>();
        var seenKnown = new Dictionary<string, List<string>>();

        foreach (var (origin, source) in ExistingTestPrograms())
        {
            if (TempIr.TryBuild(source) == null) continue;
            built++;
            foreach (var aggressive in Pipelines)
            {
                RecordingDeadCodePass recorder;
                try { recorder = TempIr.RunRecording(TempIr.TryBuild(source), aggressive); }
                catch (Exception ex) { unexpected.Add($"{origin} ({Label(aggressive)}): the pipeline threw {ex.GetType().Name}: {ex.Message.Split('\n')[0]}"); continue; }

                if (recorder.Removals.Count == 0) continue;
                var kinds = RecordingDeadCodePass.KindCounts(recorder.Removals);
                if (KnownOrphanPrograms.TryGetValue(origin, out var expected) && kinds == expected)
                {
                    foreach (var removal in recorder.Removals)
                        if (!removal.Value.IsCompilerTemp || removal.Value.NamedAfterVariable || !removal.Function.IsMintedTempName(removal.Value.Name))
                            unexpected.Add($"{origin}: deleted '{removal.Value.Name}' ({removal.Kind}), which is not a flagged minted temp");
                    if (!seenKnown.TryGetValue(origin, out var list)) seenKnown[origin] = list = new List<string>();
                    list.Add(Label(aggressive));
                }
                else
                    unexpected.Add($"{origin} ({Label(aggressive)}): deleted {kinds}" + (expected != null ? $" — the known program is expected to delete {expected}" : ""));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(built, Is.GreaterThanOrEqualTo(500), "the sample shrank: the harvest of program strings from the test assembly no longer finds what it did (674)");
            Assert.That(unexpected, Is.Empty,
                "DCE deleted an instruction in an existing test program that is not in KnownOrphanPrograms. If it is a flagged orphan (a peephole leftover), " +
                "add the program with its kinds; if it is anything else, that is a regression.");
            foreach (var known in KnownOrphanPrograms.Keys)
                Assert.That(seenKnown.GetValueOrDefault(known), Is.EquivalentTo(new[] { "standard", "aggressive" }),
                    $"{known} no longer loses its orphan in both pipelines: the sample changed under this test");
        });
    }
}
