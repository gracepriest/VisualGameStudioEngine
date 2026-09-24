using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Pins step 1 of the uncommitted CSharpBackend/IRBuilder fix set: <c>CSharpBackend.GetOperands</c>
/// becomes TOTAL over IR node kinds (its default arm now throws instead of silently returning
/// zero uses), with new arms for <c>IRArrayStore</c>, <c>IRFieldStore</c>, <c>IRForEach.Collection</c>,
/// <c>IRThrow.Exception</c>, <c>IRYield.Value</c>, and <c>IRSwitch</c>'s <c>Cases</c> AND
/// <c>PatternCases</c> (via the new <c>AddPatternOperands</c> helper).
///
/// <para>⛔ <b>Why a missing arm is a silently-WRONG answer, not an absent feature.</b>
/// <c>GetOperands</c> feeds <c>AnalyzeUseCounts</c>, which decides whether a produced value (a
/// call's result, say) is materialized as its own declared statement. A missing arm reports ZERO
/// uses for an operand that is very much used, so the producing instruction is BOTH inlined at its
/// one visible use-site AND (because <c>ShouldEmitInstruction</c> also consults the same
/// zero-use verdict to decide the value needs no statement of its own) sometimes emitted a second
/// time — a side-effecting call runs TWICE. Measured on the reference (C#) backend before this
/// fix: <c>b.V = Tag()</c> (<see cref="AFieldStoreFromACall_CallsItsFunctionOnce"/>) and
/// <c>Throw MakeEx()</c> (<see cref="AThrowExceptionFromACall_CallsItsFunctionOnce"/>) each called
/// their function TWICE on C# where every other backend called it once.</para>
///
/// <para>⚠ <c>OperandWalkerTotalityTests.TheCSharpBackendsOperandWalkerIsTotal_ExceptForTwoNamedSlots</c>
/// is the STRUCTURAL pin for this same fix (every <c>IRValue</c>-typed property, by reflection) —
/// including the six arms this file exercises by VALUE. It has ONE blind spot this file fills:
/// <c>IRSwitch.PatternCases</c> is not an <c>IRValue</c>-typed slot (it is
/// <c>List&lt;IRPatternCase&gt;</c>, a type reflection cannot walk into for a sentinel), so a
/// deleted <c>AddPatternOperands</c> arm is INVISIBLE to that census. This file tests it directly.</para>
///
/// <para>⚠ Kept to ONE shape per <see cref="FourBackends"/>/<see cref="Msil.MsilHarness"/> call —
/// both use <c>Assert.Multiple</c> internally (docs/HANDOFF.md).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class GetOperandsUseCountFixTests
{
    private static readonly BasicLang.Compiler.SemanticAnalysis.TypeInfo IntType =
        new("Integer", TypeKind.Primitive);

    // ====================================================================================
    // G4 — b.V = Tag(): an IRFieldStore whose Value is a call. RAN WRONG on C# at the defect
    // ("tag | tag | 3" — the function ran twice); RAN OK on C++/JS/MSIL throughout. All four
    // backends agree once fixed, so the ordinary FourBackends helper applies.
    // ====================================================================================

    [Test]
    public void AFieldStoreFromACall_CallsItsFunctionOnce()
        => FourBackends.RunsOnEveryBackend(
            "Class Box\n" +
            " Public V As Integer\n" +
            "End Class\n\n" +
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim b As New Box()\n" +
            " b.V = Tag()\n" +
            " Console.WriteLine(b.V)\n" +
            "End Sub",
            "tag\n3");

    /// <summary>
    /// ⭐ THE AGGRESSIVE SIBLING — the same shape must not double-call under <c>--optimize</c>
    /// either; a different pass could reintroduce the same false zero-use verdict's symptom.
    /// </summary>
    [Test]
    public void AFieldStoreFromACall_CallsItsFunctionOnce_Aggressive()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Class Box\n" +
            " Public V As Integer\n" +
            "End Class\n\n" +
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim b As New Box()\n" +
            " b.V = Tag()\n" +
            " Console.WriteLine(b.V)\n" +
            "End Sub",
            "tag\n3");

    // ====================================================================================
    // G2 — Throw MakeEx(): an IRThrow whose Exception is a call. RAN WRONG on C# at the defect
    // ("make | make | boom"). ⚠ C++ is NOT part of the oracle here — it cannot compile ANY
    // Exception-typed value (a separate, pre-existing, unrelated gap: "unknown type name
    // 'Exception'"), so this asserts C#, JavaScript and MSIL only — the three backends that
    // actually run this program, measured.
    // ====================================================================================

    [Test]
    public void AThrowExceptionFromACall_CallsItsFunctionOnce()
    {
        const string program =
            "Function MakeEx() As Exception\n" +
            " Console.WriteLine(\"make\")\n" +
            " Return New Exception(\"boom\")\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Try\n" +
            "  Throw MakeEx()\n" +
            " Catch ex As Exception\n" +
            "  Console.WriteLine(ex.Message)\n" +
            " End Try\n" +
            "End Sub";
        const string expected = "make\nboom";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    // ====================================================================================
    // BOTH SHIPPING ENTRY POINTS — CompileProjectFiles (the Release .blproj / CLI `build` route)
    // AND CompileFile (the CLI single-file route), asserted by VALUE, mirroring
    // LoopPassesDisabledTests.BothCompilerEntryPoints_Aggressive_PrintTheCorrectValue. A fixture
    // built only on the in-test helper (FourBackends.RunEmittedCSharp, ReturnCoercionTests'
    // EmitCSharp) cannot see a divergence specific to either shipping entry point.
    // ====================================================================================

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_AFieldStoreFromACall_CallsItsFunctionOnce")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_AFieldStoreFromACall_CallsItsFunctionOnce")]
    public void BothCompilerEntryPoints_FieldStoreFromACall(bool asProject)
    {
        const string source =
            "Class Box\n" +
            " Public V As Integer\n" +
            "End Class\n\n" +
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim b As New Box()\n" +
            " b.V = Tag()\n" +
            " Console.WriteLine(b.V)\n" +
            "End Sub";
        const string expected = "tag\n3";

        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_GetOperandsEntry_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, source);

            var compiler = new BasicLang.Compiler.BasicCompiler(new BasicLang.Compiler.CompilerOptions());
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                    new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR))),
                Is.EqualTo(expected),
                (asProject ? "CompileProjectFiles" : "CompileFile")
                + " — the entry point the CLI `build` / a Release .blproj / the IDE build service "
                + "actually take, and where a fixture built only on the in-test helper could miss "
                + "a divergence.");
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ====================================================================================
    // AddPatternOperands — GetOperands's recursion into IRSwitch.PatternCases, asserted directly
    // via reflection because no BasicLang program can put a CALL's result in a C# relational
    // pattern's bound (C# 9+ pattern matching requires the bound to be a compile-time constant,
    // exactly as a plain `switch` case label does — so a running program cannot discriminate a
    // deleted AddPatternOperands arm by VALUE; nothing WalkerCensus in
    // OperandWalkerTotalityTests sees it either, since IRSwitch.PatternCases is not an
    // IRValue-typed slot). This is the one gap that fixture's structural census cannot close.
    //
    // Mirrors OperandWalkerTotalityTests.TheOptimizersWalkerRecursesIntoPatternCases, but against
    // GetOperands instead of OptimizationPass.ReplaceUsesIn, covering WhenGuard, a range bound, a
    // NESTED alternative inside an Or pattern, and a Tuple pattern element.
    // ====================================================================================

    [Test]
    public void GetOperandsRecursesIntoEveryPatternCaseShape()
    {
        var subject = new IRVariable("subject", IntType);
        var guard = new IRVariable("guard", IntType);
        var lower = new IRVariable("lo", IntType);
        var upper = new IRVariable("hi", IntType);
        var comparison = new IRVariable("cmp", IntType);
        var nestedConstant = new IRVariable("nested_const", IntType);
        var tupleElement = new IRVariable("tuple_elem", IntType);

        var target = new BasicBlock("case");

        var range = new IRRangePatternCase(lower, upper, target) { WhenGuard = guard };

        var compPattern = new IRComparisonPatternCase(">", comparison, target);

        var nested = new IRConstantPatternCase(nestedConstant, target);
        var orCase = new IROrPatternCase(target);
        orCase.Alternatives.Add(nested);

        var tuplePattern = new IRConstantPatternCase(tupleElement, target);
        var tupleCase = new IRTuplePatternCase(target);
        tupleCase.Elements.Add(tuplePattern);

        var switchInst = new IRSwitch(subject, target);
        switchInst.PatternCases.Add(range);
        switchInst.PatternCases.Add(compPattern);
        switchInst.PatternCases.Add(orCase);
        switchInst.PatternCases.Add(tupleCase);

        var seen = InvokeGetOperands(switchInst);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Does.Contain(subject), "the switch's own subject");
            Assert.That(seen, Does.Contain(guard), "a range pattern's WhenGuard");
            Assert.That(seen, Does.Contain(lower), "a range pattern's lower bound");
            Assert.That(seen, Does.Contain(upper), "a range pattern's upper bound");
            Assert.That(seen, Does.Contain(comparison), "a comparison pattern's CompareValue");
            Assert.That(seen, Does.Contain(nestedConstant),
                "a constant pattern NESTED inside an Or pattern's alternatives — recursion, not a flat scan");
            Assert.That(seen, Does.Contain(tupleElement), "a tuple pattern's element");
        });
    }

    /// <summary>
    /// ⛔ MUTANT (a), the <c>AddPatternOperands</c> arm specifically: if the whole recursion is
    /// deleted (the <c>if (sw.PatternCases != null) foreach (...) AddPatternOperands(...)</c>
    /// block removed from <c>GetOperands</c>'s <c>IRSwitch</c> case), every pattern-case operand
    /// above vanishes from the result while <see cref="GetOperandsRecursesIntoEveryPatternCaseShape"/>
    /// still constructs the same switch — so that test is this mutant's kill. This second test
    /// pins the SAME contract from the opposite direction: WalkerProbePass values that must NOT
    /// leak in are absent, so a future arm cannot pass by accident by returning everything.
    /// </summary>
    [Test]
    public void GetOperandsDoesNotConflatePatternOperandsAcrossDifferentSwitches()
    {
        var subjectA = new IRVariable("subjectA", IntType);
        var boundA = new IRVariable("boundA", IntType);
        var targetA = new BasicBlock("caseA");
        var switchA = new IRSwitch(subjectA, targetA);
        switchA.PatternCases.Add(new IRComparisonPatternCase(">", boundA, targetA));

        var subjectB = new IRVariable("subjectB", IntType);
        var targetB = new BasicBlock("caseB");
        var switchB = new IRSwitch(subjectB, targetB); // no pattern cases at all

        var seenA = InvokeGetOperands(switchA);
        var seenB = InvokeGetOperands(switchB);

        Assert.Multiple(() =>
        {
            Assert.That(seenA, Does.Contain(boundA));
            Assert.That(seenB, Does.Not.Contain(boundA),
                "a pattern operand from one IRSwitch instance must not appear for another");
            Assert.That(seenB, Does.Contain(subjectB));
        });
    }

    /// <summary>Invokes the private <c>CSharpBackend.GetOperands</c> by reflection — the same
    /// door <c>OperandWalkerTotalityTests</c>'s census uses.</summary>
    private static List<IRValue> InvokeGetOperands(IRInstruction instr)
    {
        var generatorType = typeof(BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator);
        var getOperands = generatorType.GetMethod("GetOperands", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.That(getOperands, Is.Not.Null, "CSharpBackend.GetOperands has been renamed or removed");
        var generator = RuntimeHelpers.GetUninitializedObject(generatorType);
        return ((IEnumerable<IRValue>)getOperands!.Invoke(generator, new object[] { instr })!)
            .Where(v => v != null).ToList();
    }
}
