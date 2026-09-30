using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using TypeInfo = BasicLang.Compiler.SemanticAnalysis.TypeInfo;
using TypeKind = BasicLang.Compiler.SemanticAnalysis.TypeKind;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ ADR-0017 D1 (#163): <c>IRValue.IsCompilerTemp</c>, the marker that is DCE's only licence to delete
/// an unused instruction. Fast tier: front end and optimizer in process, no backend, no process.
///
/// <para>What is pinned, and by which mutant of the implementer's <c>mutants163.py</c> it dies:</para>
/// <list type="bullet">
/// <item>the minter RECORDS what it hands out, exactly (a user <c>T5</c> is not the minted <c>t5</c>);</item>
/// <item>⭐ <b>M4a</b> — a user <c>Dim</c>'s storage NEVER carries the flag, whatever its spelling. This
/// is the assertion only an IR test can make: DCE re-checks <c>NamedAfterVariable</c> itself, so a flag
/// wrongly set on user storage changes no program at all (measured: no cell moves), and would sit
/// there until the day that re-check is removed;</item>
/// <item>⭐ <b>M2</b> — <c>OptimizationPass.InheritIdentity</c> carries the flag to a replacement, and the
/// real strength-reduction replacement of R2 does carry it. Dropped, the replacement reads "not a temp"
/// and the orphan stays: code size only, so no execution test can see it.</item>
/// </list>
///
/// <para>Every "the flag is absent here" assertion is paired with a "and it is present THERE" one over
/// the same program, so a marker that is never set at all cannot pass them: the moved pins in
/// <c>DeadCodeEliminationUseAnalysisTests</c> went vacuous exactly that way.</para>
/// </summary>
[TestFixture]
public class CompilerTempMarkerTests
{
    private static readonly TypeInfo I = new("Integer", TypeKind.Primitive);

    // ============================================================================================
    // The minter
    // ============================================================================================

    [Test]
    public void TheMinter_RecordsExactlyTheNamesItHandsOut()
    {
        var f = new IRFunction("F", I);
        var minted = new[] { f.GetNextTempName(), f.GetNextTempName(), f.GetNextTempName() };

        Assert.Multiple(() =>
        {
            Assert.That(minted.Distinct().Count(), Is.EqualTo(3), "the minter never repeats a name");
            foreach (var name in minted)
                Assert.That(f.IsMintedTempName(name), Is.True, $"'{name}' was minted");
        });
    }

    [Test]
    public void TheRecord_IsOrdinal_AndAnswersFalseForEverythingTheMinterDidNotSay()
    {
        var f = new IRFunction("F", I);
        var first = f.GetNextTempName();

        Assert.Multiple(() =>
        {
            Assert.That(f.IsMintedTempName(first.ToUpperInvariant()), Is.False,
                "a user `T0` is not the minted `t0`: the record is ORDINAL, never a spelling test");
            Assert.That(f.IsMintedTempName("_tmp1"), Is.False);
            Assert.That(f.IsMintedTempName("_" + first), Is.False);
            Assert.That(f.IsMintedTempName("t99"), Is.False, "temp-SHAPED is not temp-MINTED");
            Assert.That(f.IsMintedTempName(""), Is.False);
            Assert.That(f.IsMintedTempName(null), Is.False);
        });
    }

    [Test]
    public void TheRecord_IsPerFunction()
    {
        var f = new IRFunction("F", I);
        var g = new IRFunction("G", I);
        f.GetNextTempName();
        f.GetNextTempName();
        var gName = g.GetNextTempName();
        var fSecond = "t1"; // F minted two; G one

        Assert.Multiple(() =>
        {
            Assert.That(f.IsMintedTempName(fSecond), Is.True);
            Assert.That(g.IsMintedTempName(fSecond), Is.False, "G never minted a second name: the record is not shared");
            Assert.That(g.IsMintedTempName(gName), Is.True);
        });
    }

    // ============================================================================================
    // The marker on real IR
    // ============================================================================================

    /// <summary>The invariant, over every value reachable from every function body: a flagged value is a
    /// minted name, and not user storage. Returns the number of flagged values, so a caller can insist the
    /// marker was set at all.</summary>
    private static int AssertNeverOnUserStorage(IRModule module, string label)
    {
        var flagged = 0;
        foreach (var function in TempIr.Functions(module))
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var local in function.LocalVariables) declared.Add(local.Name);
            foreach (var parameter in function.Parameters) declared.Add(parameter.Name);
            foreach (var global in module.GlobalVariables) { declared.Add(global.Key); declared.Add(global.Value?.Name); }
            foreach (var cls in module.Classes.Values)
                foreach (var field in cls.Fields) declared.Add(field.Name);

            foreach (var v in TempIr.Reachable(function))
            {
                if (!v.IsCompilerTemp) continue;
                flagged++;
                Assert.Multiple(() =>
                {
                    Assert.That(v, Is.Not.InstanceOf<IRVariable>(), $"{label}/{function.Name}: a variable (user storage) carries the flag: {v.Name}");
                    Assert.That(v, Is.Not.InstanceOf<IRConstant>(), $"{label}/{function.Name}: a constant carries the flag");
                    Assert.That(v.NamedAfterVariable, Is.False,
                        $"{label}/{function.Name}: `{v.Name}` IS a user variable's storage (NamedAfterVariable) and is flagged a compiler temp");
                    Assert.That(function.IsMintedTempName(v.Name), Is.True,
                        $"{label}/{function.Name}: `{v.Name}` is flagged but its function never minted that name");
                    Assert.That(declared.Contains(v.Name), Is.False,
                        $"{label}/{function.Name}: `{v.Name}` is flagged but the program declares storage of that name");
                });
            }
        }
        return flagged;
    }

    /// <summary>⭐ M4a. `Dim t5 = a + b` is ONE IRBinaryOp renamed `t5`, NamedAfterVariable. It must never
    /// carry the flag, and neither may any other Dim, whatever it is spelled and whatever kind of value
    /// initialises it. Unmarked names include the ones a spelling guard would take: `t5`, `T5`, `_tmp1`,
    /// `_t3`, `_t0`.</summary>
    private const string EverySpellingOfADim = """
        Function G(n As Integer) As Integer
            Return n + 1
        End Function

        Function F(a As Integer, b As Integer) As Integer
            Dim t5 As Integer = a + b
            Dim T6 As Integer = a * b
            Dim _tmp1 As Integer = -a
            Dim _t3 As Integer = a - b
            Dim _t0 As Boolean = a < b
            Dim t0 As Integer = G(a)
            Dim plain As Integer = a * 2
            Dim u As Integer = -(-a)
            If _t0 Then Return t5 + T6 + _tmp1 + _t3 + t0 + plain + u
            Return t5
        End Function

        Sub Main()
            Console.WriteLine(CStr(F(3, 4)))
        End Sub
        """;

    private static IEnumerable<TestCaseData> MarkerPrograms()
    {
        yield return new TestCaseData(EverySpellingOfADim).SetName("EverySpellingOfADim");
        foreach (var probe in new[] { TempProbes.R1, TempProbes.R2, TempProbes.R3, TempProbes.R4, TempProbes.R5, TempProbes.R6, TempProbes.R7,
                                      TempProbes.R8, TempProbes.R10, TempProbes.U1, TempProbes.U2, TempProbes.U3, TempProbes.U4, TempProbes.U5,
                                      TempProbes.U6, TempProbes.U7, TempProbes.U8, TempProbes.CtWbrT0, TempProbes.R11, TempProbes.LcT0 })
            yield return new TestCaseData(probe.Source).SetName(probe.Id);
        yield return new TestCaseData(TempProbes.R9Source).SetName("R9_Increment");
    }

    [TestCaseSource(nameof(MarkerPrograms))]
    public void TheMarker_IsNeverOnUserStorage_AndIsSetOnRealTemps(string source)
    {
        var module = TempIr.Build(source);
        var flagged = AssertNeverOnUserStorage(module, "IRBuilder");
        Assert.That(flagged, Is.GreaterThan(0),
            "the marker was never set: every 'never on user storage' assertion above is vacuous, and DCE could delete nothing");
    }

    [TestCaseSource(nameof(MarkerPrograms))]
    public void TheMarker_IsStillNeverOnUserStorage_AfterTheStandardAndAggressivePasses(string source)
    {
        AssertNeverOnUserStorage(TempIr.Optimized(source, aggressive: false), "standard");
        AssertNeverOnUserStorage(TempIr.Optimized(source, aggressive: true), "aggressive");
    }

    /// <summary>The spelled-alike collision: the program declares `t3`, and its expressions need ≥ 4 temps, so
    /// the minter reaches the name `t3` too. IRBuilder renames the temp
    /// (`SeparateTempsFromUserNames`) BEFORE the marker looks, so no flagged value carries a name the
    /// program declares, while the user's own `t3` stays unflagged.</summary>
    [Test]
    public void ADeclaredNameTheMinterAlsoReaches_IsNeverAFlaggedTemp()
    {
        const string source = """
            Sub Show(n As Integer)
                Console.WriteLine(CStr(n))
            End Sub

            Sub Run(a As Integer)
                Dim t3 As Integer = 5
                Show(-(-(-(-a))))
                Show(t3)
            End Sub

            Sub Main()
                Run(2)
            End Sub
            """;
        var module = TempIr.Build(source);
        var run = TempIr.Functions(module).Single(f => f.Name == "Run");

        var flagged = AssertNeverOnUserStorage(module, "collision");
        var namedT3 = TempIr.Reachable(run).Where(v => string.Equals(v.Name, "t3", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(flagged, Is.GreaterThanOrEqualTo(4), "the four negations mint four temps");
            Assert.That(namedT3, Is.Not.Empty, "the user's `t3` is in the body");
            Assert.That(namedT3.Any(v => v.IsCompilerTemp), Is.False, "nothing named `t3` — the user's — is a compiler temp");
        });
    }

    /// <summary>The marker reaches class members' bodies too (`IRTempNames.AllFunctions`): the `v + 1` of
    /// R4's `MyBase.New(v + 1)` is a flagged temp in a constructor. This is also the PRECONDITION of the
    /// base-call keep tests: the value is a licensed temp, so only the use analysis keeps it.</summary>
    [Test]
    public void TheMarker_ReachesAConstructorsBody_AndTheBaseCallsOperand()
    {
        var module = TempIr.Build(TempProbes.R4.Source);
        var call = TempIr.Functions(module).SelectMany(TempIr.Instructions).OfType<IRBaseConstructorCall>().Single();
        var computed = call.Args.Where(a => a is not IRVariable && a is not IRConstant).ToList();

        Assert.That(computed, Is.Not.Empty, "the argument `v + 1` is a computed value");
        Assert.That(computed.Select(a => a.IsCompilerTemp), Has.All.True, "and a flagged compiler temp");
    }

    // ============================================================================================
    // ⭐ M2 — a replacement keeps the flag
    // ============================================================================================

    private sealed class Inheriting : OptimizationPass
    {
        public Inheriting() : base("inheriting") { }
        public override bool Run(IRModule module) => false;
        public static T Copy<T>(T replacement, IRValue original) where T : IRValue => InheritIdentity(replacement, original);
    }

    [Test]
    public void InheritIdentity_CarriesTheFlag_TheNamedAfterVariableFlag_AndTheSourceLine()
    {
        var original = new IRBinaryOp("t0", BinaryOpKind.Mul, new IRVariable("a", I), new IRConstant(2, I), I)
        {
            IsCompilerTemp = true,
            NamedAfterVariable = true,
            SourceLine = 42,
        };
        var replacement = new IRBinaryOp("t0", BinaryOpKind.Shl, new IRVariable("a", I), new IRConstant(1, I), I);

        var returned = Inheriting.Copy(replacement, original);

        Assert.Multiple(() =>
        {
            Assert.That(returned, Is.SameAs(replacement), "it returns the replacement, so it can be used inline");
            Assert.That(replacement.IsCompilerTemp, Is.True, "⭐ the replacement IS the same compiler temp (M2)");
            Assert.That(replacement.NamedAfterVariable, Is.True);
            Assert.That(replacement.SourceLine, Is.EqualTo(42));
        });
    }

    [Test]
    public void InheritIdentity_NeverInventsTheFlag()
    {
        var original = new IRBinaryOp("t0", BinaryOpKind.Mul, new IRVariable("a", I), new IRConstant(2, I), I);
        var replacement = new IRBinaryOp("t0", BinaryOpKind.Shl, new IRVariable("a", I), new IRConstant(1, I), I) { IsCompilerTemp = true };

        Inheriting.Copy(replacement, original);

        Assert.That(replacement.IsCompilerTemp, Is.False, "an unmarked original leaves the replacement unmarked: it copies, it does not OR");
    }

    [Test]
    public void InheritIdentity_ANullOriginalOrReplacement_ChangesNothing()
    {
        var value = new IRBinaryOp("t0", BinaryOpKind.Add, new IRVariable("a", I), new IRConstant(1, I), I) { IsCompilerTemp = true };
        Assert.Multiple(() =>
        {
            Assert.That(Inheriting.Copy(value, null), Is.SameAs(value));
            Assert.That(value.IsCompilerTemp, Is.True);
            Assert.That(Inheriting.Copy<IRBinaryOp>(null, value), Is.Null);
        });
    }

    /// <summary>⭐ M2 on real IR. `(a * 2) * 0`: strength reduction replaces the multiply with a shift
    /// (through `InheritIdentity`), then the peephole's `x * 0` arm orphans the replacement. Taking the
    /// dead-code pass OUT leaves that orphan standing; it must be flagged. Then the shipped pipeline must
    /// delete it. With the flag dropped in the replacement the first assertion fails, and so does the
    /// second: the orphan stays.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public void TheShiftStrengthReductionMakes_IsAFlaggedTemp_SoTheOrphanIsDeleted(bool aggressive)
    {
        var without = TempIr.Build(TempProbes.R2.Source);
        TempIr.PipelineWithoutDce(aggressive).Run(without);
        var shifts = TempIr.PureOrphans(without).OfType<IRBinaryOp>().Where(b => b.Operation == BinaryOpKind.Shl).ToList();

        var with = TempIr.Build(TempProbes.R2.Source);
        TempIr.Pipeline(aggressive).Run(with);

        Assert.Multiple(() =>
        {
            Assert.That(shifts, Is.Not.Empty, "the probe leaves a strength-reduced orphan behind when nothing deletes it");
            Assert.That(shifts.Select(s => s.IsCompilerTemp), Has.All.True,
                "a value an optimizer pass REPLACED must still be a compiler temp (InheritIdentity)");
            Assert.That(TempIr.PureOrphans(with), Is.Empty, "and the shipped pipeline deletes it");
        });
    }
}
