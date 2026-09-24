using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.IR.Optimization;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⭐ CONTRACT ITEM 2, AS AN INVARIANT: <b>no pass may reference a variable it has not
/// declared.</b> After the pipeline runs, every <c>IRVariable</c> name in an operand slot must be
/// in <c>IRFunction.LocalVariables</c> ∪ <c>IRFunction.Parameters</c> ∪ <c>IRModule</c> globals.
///
/// <para>⛔ <b>THE STRUCTURAL FACT THIS RESTS ON.</b> <c>IROptimizer.cs</c> mentions
/// <c>LocalVariables</c> only in comments — there is not one line of code in the whole optimizer
/// that WRITES it. The optimizer therefore has no facility at all for declaring a variable it
/// mints, and the practical form of contract item 2 is the stronger one this fixture asserts
/// instead: <b>a pass must not mint a fresh variable name AT ALL</b> until that facility exists.
/// Two passes ever wanted one. <c>FunctionInliningPass</c> minted <c>_inline_*</c> and is
/// disabled (<see cref="FunctionInliningDisabledTests"/>). <c>InductionVariablePass</c> minted
/// <c>_div_*</c> at <c>IROptimizer.cs:3073</c> — the only SHIPPING site that minted a fresh name
/// — and is disabled (<see cref="InductionVariableDisabledTests"/>).</para>
///
/// <para>⭐ <b>Why this fixture and not a backend test.</b> These assertions need no backend, no
/// toolchain and no process, and they are STRICTLY STRONGER than the dangling-operand invariant
/// in <see cref="OptimizerDanglingOperandInvariantTests"/> on the shape that matters most.
/// MEASURED with <c>InductionVariablePass</c> restored to <c>AddAggressivePasses</c>:</para>
///
/// <list type="bullet">
/// <item><c>Show(i * 3)</c> in a counted loop → 1 orphan AND <c>Run::_div_t1</c> undeclared.
/// Either invariant sees it.</item>
/// <item><c>x = i * 3</c> onto a NAMED local → <b>ZERO orphans</b> and <c>Run::_div_x</c>
/// undeclared. The dangling-operand invariant is BLIND to this shape; only this one sees it.</item>
/// <item>The same pass repaired so it DECLARES its minted variable and gives it a unique name →
/// zero orphans, zero undeclared, and <c>Run::_div_x_&lt;guid&gt;</c> still shows up in
/// <see cref="AddAggressivePasses_IntroducesNoVariableNameAbsentFromThePreOptimizationIr"/>.
/// That repair is silently WRONG (it never initialises the derived variable), and the delta
/// invariant is the only assertion in the suite that catches it without running a program.</item>
/// </list>
///
/// <para>⚠ The IR is walked by REFLECTION, through the same single walker
/// <c>OptimizerIr.DanglingOperandsOf</c> uses, and deliberately not through
/// <c>OptimizationPass.ReplaceUsesIn</c> or <c>CSharpBackend.GetOperands</c>: both are
/// hand-written one-arm-per-node-kind switches, and a MISSING ARM is the defect class this whole
/// family is about. See <c>OptimizerIr</c>'s docstring.</para>
/// </summary>
[TestFixture]
public class OptimizerMintedVariableTests
{
    // ====================================================================================
    // The battery.
    // ====================================================================================
    //
    // The first twelve entries are the standard-pass battery from
    // OptimizerDanglingOperandInvariantTests, so the two invariants cover the same ground.
    // The five `_AnInductionVariable*` / `_TwoMultiplies*` / `_AMultiplyOnTheInner*` entries are
    // the ones that reach an aggressive-only pass, and each is measured: with
    // InductionVariablePass restored, all five report an undeclared `_div_` name while all twelve
    // standard entries stay clean.
    //
    // ⛔ Every arithmetic entry uses `* 3`, never `* 2`. MEASURED: AlgebraicSimplificationPass
    // (aggressive pass 9) rewrites `2 * x` to `x + x` before InductionVariablePass (pass 12) sees
    // it, so a `* 2` shape is green even with the pass restored. A `While` loop is excluded for a
    // sibling reason — the pass bails unless some block is named `.inc`, which only a counted
    // `For` produces — so a "counted loop" case written with `While` would prove nothing either.

    private static string Wrap(string body) => $"""
        Sub Show(p As Integer)
         PrintLine("V=" & CStr(p))
        End Sub
        Sub Run(a As Integer, b As Integer)
         {body}
        End Sub
        Sub Main()
         Run(3, 5)
        End Sub
        """;

    /// <summary>
    /// ⭐ CONTRACT ITEM 2 on the AGGRESSIVE pipeline — the pipeline a Release <c>.blproj</c> build
    /// and <c>BasicLang.exe … --optimize</c> both take.
    ///
    /// <para>The PRE-optimization census is asserted empty as well, and that is not decoration:
    /// it is what makes the post-optimization assertion the full ABSOLUTE form rather than a
    /// delta. Two front-end shapes legitimately reference a name this declared set cannot see — a
    /// class field read inside a method, and a <c>For Each</c> loop variable (both measured) — and
    /// this battery is chosen to contain neither, so "empty before" is a real claim about the
    /// battery and not an accident. A future entry that breaks the pre-check is telling you it
    /// belongs in
    /// <see cref="AddAggressivePasses_IntroducesNoVariableNameAbsentFromThePreOptimizationIr"/>
    /// instead, which tolerates both.</para>
    /// </summary>
    [Test]
    [TestCase("Show(a + 7)\n Show(a + 7)", TestName = "Declared_CseDuplicateInACallArgument")]
    [TestCase("Show(a + 7)\n Dim q As Integer = a + 7\n Show(q)", TestName = "Declared_CseDuplicateOnANamedDestination")]
    [TestCase("Show(a + 7)\n Dim t0 As Integer = a + 7\n Show(t0)", TestName = "Declared_CseDuplicateOnATempSpelledName")]
    [TestCase("Show(a + 0)", TestName = "Declared_PeepholeAddZero")]
    [TestCase("Show(a + 0)\n Show(b + 0)", TestName = "Declared_PeepholeTwoRewritesInOneBlock")]
    [TestCase("Show(a - a)", TestName = "Declared_PeepholeSelfSubtraction")]
    [TestCase("Show(a * 0)", TestName = "Declared_PeepholeMultiplyByZero")]
    [TestCase("Show(-(-a))", TestName = "Declared_PeepholeDoubleNegation")]
    [TestCase("Dim t0 As Integer = a + 0\n Show(t0)", TestName = "Declared_PeepholeOnATempSpelledName")]
    [TestCase("Dim arr(3) As Integer\n Show(a + b)\n arr(0) = a + b\n Show(arr(0))", TestName = "Declared_CseIntoAnArrayStore")]
    [TestCase("Show(a + b)\n If (a + b) > 0 Then\n  PrintLine(\"POS\")\n End If", TestName = "Declared_CseIntoABranchCondition")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(a + 7)\n  Show(a + 7)\n Next", TestName = "Declared_CseInsideALoop")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(i * 3)\n Next",
        TestName = "Declared_AnInductionVariableTimesAConstant")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(3 * i)\n Next",
        TestName = "Declared_AConstantTimesAnInductionVariable")]
    [TestCase("Dim i As Integer\n For i = 1 To b\n  Show(i * 3)\n Next",
        TestName = "Declared_AnInductionVariableLoopThatDoesNotStartAtZero")]
    [TestCase("Dim i As Integer\n Dim x As Integer\n For i = 0 To b\n  x = i * 3\n  Show(x)\n  x = i * 5\n  Show(x)\n Next",
        TestName = "Declared_TwoMultipliesOntoOneNamedLocal_WhichLeavesNoOrphanAtAll")]
    [TestCase("Dim i As Integer\n Dim j As Integer\n For i = 0 To 1\n  For j = 0 To 2\n   Show(j * 3)\n  Next\n Next",
        TestName = "Declared_AMultiplyOnTheInnerCounterOfANestedLoop")]
    public void NoAggressivePassReferencesAVariableItHasNotDeclared(string body)
    {
        var source = Wrap(body);
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");

        Assert.That(OptimizerIr.UndeclaredOperandNames(module), Is.Empty,
            "the UNOPTIMIZED IR already references an undeclared variable, so the assertion below "
            + "would be a delta rather than the absolute contract. A class field or a For Each "
            + "variable does this legitimately; move this case to the delta test instead of "
            + "weakening the check.");

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        Assert.That(OptimizerIr.UndeclaredOperandNames(module), Is.Empty,
            "an AGGRESSIVE pass left an operand pointing at a variable that is in none of "
            + "LocalVariables, Parameters or the module globals. Every backend turns that into an "
            + "identifier nothing declares — C# CS0103, C++ 'use of undeclared identifier', "
            + "JavaScript ReferenceError, MSIL InvalidProgramException. Since no line of "
            + "IROptimizer.cs writes LocalVariables, a pass cannot fix this by declaring the name: "
            + "it must not mint one.");
    }

    /// <summary>
    /// The same invariant on <c>AddStandardPasses</c> — the pipeline that runs with NO optimizer
    /// flag, on every shipping route. Cheap, and it keeps the family symmetric: contract item 1 is
    /// asserted on both pipelines, and so is contract item 2.
    /// </summary>
    [Test]
    [TestCase("Show(a + 7)\n Show(a + 7)", TestName = "StdDeclared_CseDuplicateInACallArgument")]
    [TestCase("Show(a + 0)", TestName = "StdDeclared_PeepholeAddZero")]
    [TestCase("Show(a - a)", TestName = "StdDeclared_PeepholeSelfSubtraction")]
    [TestCase("Show(-(-a))", TestName = "StdDeclared_PeepholeDoubleNegation")]
    [TestCase("Dim arr(3) As Integer\n Show(a + b)\n arr(0) = a + b\n Show(arr(0))", TestName = "StdDeclared_CseIntoAnArrayStore")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(i * 3)\n Next", TestName = "StdDeclared_AnInductionVariableTimesAConstant")]
    public void NoStandardPassReferencesAVariableItHasNotDeclared(string body)
    {
        var module = JsTestSupport.BuildModule(Wrap(body), sourceFilePath: "prog.bas");

        var pipeline = new OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(module);

        Assert.That(OptimizerIr.UndeclaredOperandNames(module), Is.Empty,
            "a STANDARD pass left an operand pointing at an undeclared variable");
    }

    /// <summary>
    /// ⭐ THE STRONGEST FORM, and the one that catches the repair that LOOKS right:
    /// <b><c>AddAggressivePasses()</c> introduces no variable name that was not already in the
    /// pre-optimization IR.</b>
    ///
    /// <para>This is contract item 2's practical clause — "a pass must not mint a fresh variable
    /// name at all until that facility exists" — asserted directly, and it is strictly stronger
    /// than the undeclared-name check above. MEASURED on the repair that gives
    /// <c>InductionVariablePass</c> a GUID-unique name AND adds it to <c>LocalVariables</c>:
    /// zero orphans, zero undeclared names, every simple shape still printing the right values —
    /// and <c>Run::_div_x_&lt;guid&gt;</c> reported here. That repair is silently wrong on any
    /// loop that does not start at zero (measured: <c>For i = 1 To 3 : Show(i * 3)</c> prints
    /// 0, 3, 6 where 3, 6, 9 is correct), so this is the assertion that catches it without
    /// compiling or running anything.</para>
    ///
    /// <para>Because this is a DELTA it tolerates a front-end shape that references a name the
    /// declared set cannot see, so the battery here can be wider than the one above: it adds a
    /// <c>For Each</c> variable (measured: <c>Run::v</c> is undeclared BEFORE any pass runs, so the
    /// absolute form cannot carry this shape at all), the minted-name collision shape, and a shape
    /// with no loop. The other such front-end shape is a class field read inside a method
    /// (measured: <c>Value</c>); it is recorded in <c>OptimizerIr.UndeclaredOperandNames</c>'
    /// docstring rather than duplicated here, since one example of the category is enough to keep
    /// the two batteries' division of labour honest.</para>
    /// </summary>
    [Test]
    [TestCase("Show(a + 7)\n Show(a + 7)", TestName = "NoNewName_CseDuplicateInACallArgument")]
    [TestCase("Show(a + 0)", TestName = "NoNewName_PeepholeAddZero")]
    [TestCase("Show(a - a)", TestName = "NoNewName_PeepholeSelfSubtraction")]
    [TestCase("Dim arr(3) As Integer\n Show(a + b)\n arr(0) = a + b\n Show(arr(0))", TestName = "NoNewName_CseIntoAnArrayStore")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(a + 7)\n  Show(a + 7)\n Next", TestName = "NoNewName_CseInsideALoop")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(i * 3)\n Next",
        TestName = "NoNewName_AnInductionVariableTimesAConstant")]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(3 * i)\n Next",
        TestName = "NoNewName_AConstantTimesAnInductionVariable")]
    [TestCase("Dim i As Integer\n For i = 1 To b\n  Show(i * 3)\n Next",
        TestName = "NoNewName_AnInductionVariableLoopThatDoesNotStartAtZero")]
    [TestCase("Dim i As Integer\n Dim x As Integer\n For i = 0 To b\n  x = i * 3\n  Show(x)\n  x = i * 5\n  Show(x)\n Next",
        TestName = "NoNewName_TwoMultipliesOntoOneNamedLocal")]
    [TestCase("Dim i As Integer\n Dim j As Integer\n For i = 0 To 1\n  For j = 0 To 2\n   Show(j * 3)\n  Next\n Next",
        TestName = "NoNewName_AMultiplyOnTheInnerCounterOfANestedLoop")]
    // ⚠ A CONTROL that marks this invariant's OWN blind spot, measured: with the verbatim defect
    // restored, this shape reports NO new name — because the name the pass mints is one the USER
    // already declared, so it is not new. Zero orphans, zero undeclared names, zero minted names,
    // and a silently wrong program (198,204,210,216 for 99,102,105,108). It DOES go red for a
    // repair that gives the minted variable a unique name, which is why it is kept here. The shape
    // is guarded by VALUE in
    // InductionVariableDisabledTests.AUserVariableSpelledLikeTheMintedName_IsNotClobbered, which is
    // the only assertion in the suite that can see it.
    [TestCase("Dim i As Integer\n Dim _div_x As Integer = 99\n Dim x As Integer = 0\n For i = 0 To b\n  x = i * 3\n  Show(x + _div_x)\n Next",
        TestName = "NoNewName_AUserVariableAlreadySpelledLikeTheMintedName")]
    [TestCase("Dim l As New List(Of Integer)()\n l.Add(a)\n l.Add(b)\n For Each v In l\n  Show(v * 3)\n Next",
        TestName = "NoNewName_AForEachVariable_WhichTheAbsoluteFormCannotCover")]
    [TestCase("Show(a * b)", TestName = "NoNewName_NoLoopAtAll")]
    public void AddAggressivePasses_IntroducesNoVariableNameAbsentFromThePreOptimizationIr(string body)
    {
        var module = JsTestSupport.BuildModule(Wrap(body), sourceFilePath: "prog.bas");
        var before = OptimizerIr.AllVariableNames(module);

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        var introduced = OptimizerIr.AllVariableNames(module).Where(n => !before.Contains(n)).ToList();

        Assert.That(introduced, Is.Empty,
            "an aggressive pass MINTED a variable name. The optimizer has no facility for "
            + "declaring one — IROptimizer.cs mentions LocalVariables only in comments — so a "
            + "minted name is either undeclared (a build failure or a ReferenceError on every "
            + "backend) or, if the pass declares it by hand, a name in the USER's namespace that "
            + "ADR-0001's contract forbids. InductionVariablePass minted `_div_{name}` at "
            + "IROptimizer.cs:3073 and both of those happened: `_div_t2` came out undeclared, and "
            + "a program that already had `Dim _div_x As Integer = 99` compiled cleanly and "
            + "printed the wrong numbers.");
    }

    /// <summary>
    /// ⭐ THE OTHER HALF of the same claim: <b>the aggressive pipeline DECLARES nothing either.</b>
    /// <c>IRFunction.LocalVariables</c> comes out of the pipeline with exactly the names it went in
    /// with.
    ///
    /// <para>This is the behavioural form of the structural fact that
    /// <c>grep -n "LocalVariables" BasicLang/IROptimizer.cs</c> finds only comments. Together with
    /// <see cref="AddAggressivePasses_IntroducesNoVariableNameAbsentFromThePreOptimizationIr"/> it
    /// says the whole of it: the optimizer neither mints a name nor declares one. When a future
    /// change gives the optimizer a real declaration facility, THIS is the test that must be
    /// updated first and deliberately — and the delta test above then becomes the one that decides
    /// whether the new name is legal.</para>
    /// </summary>
    [Test]
    [TestCase("Dim i As Integer\n For i = 0 To b\n  Show(i * 3)\n Next",
        TestName = "NoNewLocal_AnInductionVariableTimesAConstant")]
    [TestCase("Dim i As Integer\n Dim x As Integer\n For i = 0 To b\n  x = i * 3\n  Show(x)\n  x = i * 5\n  Show(x)\n Next",
        TestName = "NoNewLocal_TwoMultipliesOntoOneNamedLocal")]
    [TestCase("Show(a + 7)\n Show(a + 7)\n Show(a + 0)", TestName = "NoNewLocal_TheStandardPassShapes")]
    public void TheAggressivePipelineAddsNoEntryToLocalVariables(string body)
    {
        var module = JsTestSupport.BuildModule(Wrap(body), sourceFilePath: "prog.bas");

        // Keyed by the function OBJECT, not its name: a name key would throw
        // KeyNotFoundException (an opaque failure inside Assert.Multiple) if a pass ever added a
        // function, and would throw on construction for two same-named functions.
        var before = module.Functions
            .Where(f => !f.IsExternal)
            .ToDictionary(f => (object)f, f => f.LocalVariables.Select(v => v.Name).OrderBy(n => n).ToArray());

        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();
        pipeline.Run(module);

        Assert.Multiple(() =>
        {
            foreach (var f in module.Functions.Where(f => !f.IsExternal))
            {
                Assert.That(before.ContainsKey(f), Is.True,
                    $"{f.Name}: the aggressive pipeline ADDED a function, which no pass does today "
                    + "— that is a separate finding, and this test cannot speak to its locals.");
                if (!before.ContainsKey(f)) continue;

                Assert.That(f.LocalVariables.Select(v => v.Name).OrderBy(n => n).ToArray(),
                    Is.EqualTo(before[f]),
                    $"{f.Name}: the aggressive pipeline changed LocalVariables. No line of "
                    + "IROptimizer.cs writes it today, so this is a new facility, not a bug fix — "
                    + "read this test's docstring before changing it.");
            }
        });
    }
}
