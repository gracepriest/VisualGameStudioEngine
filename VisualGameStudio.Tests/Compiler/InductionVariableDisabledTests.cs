using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>InductionVariablePass</c> is DISABLED, and this fixture is the evidence for why plus the
/// guard that it stays out of the shipping pipeline. It is the THIRD pass
/// <c>OptimizationPipeline</c> keeps but does not ship, after <c>ConstantPropagationPass</c>
/// (<c>IROptimizer.cs:1594</c>) and <c>FunctionInliningPass</c> (<c>:1634</c>), and it follows the
/// same precedent on the same terms — see <see cref="FunctionInliningDisabledTests"/>, which this
/// fixture is modelled on.
///
/// <para>⛔ <b>THE AGGRESSIVE PIPELINE SHIPS.</b> This was never a dead code path.
/// <c>ProjectFile()</c> seeds <c>Configurations["Release"].OptimizationsEnabled = true</c>, which
/// <c>Program.cs:502</c> and <c>BuildService.cs:629</c> pass into
/// <c>CompilerOptions.OptimizeAggressive</c>, which reaches <c>AddAggressivePasses</c> at
/// <c>Compiler.cs:292</c> and <c>:459</c>. A Release <c>.blproj</c> build — CLI or IDE — took this
/// pass, and so did <c>BasicLang.exe file.bas --optimize</c>.</para>
///
/// <para>⛔ <b>IT NEVER PRODUCED A CORRECT PROGRAM FOR ANY LOOP IT ACTUALLY REWROTE.</b> Measured
/// at <c>67782af</c> on <c>For i = 0 To n : Show(i * 3) : Next</c>, compiled AND RUN out of
/// process on all four backends and through both entry points, the emitted C# was:</para>
///
/// <code>
/// t2 = _div_t2 + 3;   // CS0103 on BOTH names
/// Show(i * 3);        // ...and the multiply it claimed to remove is still here
/// </code>
///
/// <para>C++ refused to compile ("use of undeclared identifier '_div_t2'"); JavaScript threw
/// <c>ReferenceError: Cannot access '_div_t2' before initialization</c>; MSIL assembled and threw
/// <c>InvalidProgramException</c>. Unusually for this repo, no backend was right by luck.</para>
///
/// <para>⛔ <b>SIX DEFECTS, and the two that were written down are not the ones that matter.</b>
/// (The disabled <c>AddPass</c> line in <c>IROptimizer.cs</c> groups exactly the same material as
/// five, folding 5 into 4 and the single-definition gap into 6 — a counting convention, not a
/// disagreement about the facts.)
/// (1) The uses are never re-pointed — the same omission as CSE and the peephole pass, so the
/// derived variable is dead weight and the multiply is re-materialised from the orphan. (2) The
/// derived variable is never added to <c>IRFunction.LocalVariables</c>. (3) ⛔ It is never
/// INITIALISED, which is the undismissable one. (4) The minted name <c>_div_{name}</c> lives in
/// the USER's namespace. (5) Two multiplies onto one local both mint the same
/// <c>_div_x</c>. (6) The increment block found by the analysis is discarded and re-found as "any
/// block in the loop list whose name contains <c>.inc</c>", which for a nested loop can be the
/// wrong loop's latch — and nothing checks that the recognised increment is the only definition
/// of the counter.</para>
///
/// <para>⛔ <b>WHY REMOVAL RATHER THAN REPAIR, MEASURED.</b> Repairing exactly defects 1 and 2 —
/// the two this task was briefed on — does NOT fix the program: <c>t2</c> is a TEMP, so replacing
/// the <c>IRValue</c> that carried the name with an <c>IRAssignment</c> takes its DECLARATION
/// away, and C# still gave CS0103 on <c>t2</c> while JavaScript still threw. It also CONVERTED the
/// two-multiplies-onto-one-local shape from a loud build failure into a SILENT wrong answer:
/// 0,0,8,8,16,16,24,24 where 0,0,3,5,6,10,9,15 is correct, on C# and JavaScript. A loud failure
/// traded for a quiet one is a regression. And defect 3 cannot be fixed inside the pass at all: it
/// needs a loop preheader, and <c>ControlFlowGraph.IdentifyLoops</c> reports FOUR "natural loops"
/// for a five-block function holding ONE loop, every one of them containing <c>entry</c>. Adding
/// the initialisation to the entry block instead — which looks like the obvious fix — makes the
/// OPTIMIZER itself run out of memory on every shape measured, because the seed multiply it
/// inserts lands inside one of those bogus loops and the pass re-fires on its own output without
/// bound.</para>
///
/// <para>⚠ <b>THE CLASS IS KEPT, not deleted</b>, exactly as <c>FunctionInliningPass</c> is, so
/// <see cref="RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled"/> can construct it and
/// pin that it is still broken. Nothing is lost by not shipping it: LLVM's loop-strength-reduction,
/// the CLR JIT and V8 all perform this exact transform, better, downstream of every one of our
/// backends — and because defect 1 meant the multiply was emitted anyway, the pass never removed a
/// single multiply even when it "succeeded".</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class InductionVariableDisabledTests
{
    // ====================================================================================
    // Contract item 3: the pass is absent from the pipeline, and the class still exists.
    // ====================================================================================

    /// <summary>
    /// ⛔ THE GUARD THAT KEEPS THE PASS OUT, asserted on the PASS LIST itself rather than on
    /// emitted text — the list is the thing the decision is about, and a text assertion can go
    /// quietly vacuous if the pass changes its minted prefix.
    ///
    /// <para>The positive half is asserted too: the CLASS must still exist and still be
    /// constructible. Deleting it would make
    /// <see cref="RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled"/> unbuildable and
    /// silently delete the record of WHY the pass is disabled, which is the same trap
    /// <see cref="FunctionInliningDisabledTests"/> documents for <c>CloneAndRemap</c>.</para>
    ///
    /// <para>⚠ If someone repairs the pass, this test is what they must change deliberately — and
    /// the thing to check first is not the pass but
    /// <c>OptimizerMintedVariableTests.AddAggressivePasses_IntroducesNoVariableNameAbsentFromThePreOptimizationIr</c>:
    /// the optimizer still has no facility for declaring a variable it mints.</para>
    /// </summary>
    [Test]
    public void ThePassIsAbsentFromTheAggressivePipeline_AndTheClassStillExists()
    {
        var pipeline = new OptimizationPipeline();
        pipeline.AddAggressivePasses();

        var passes = PassesOf(pipeline);

        Assert.Multiple(() =>
        {
            Assert.That(passes.Select(p => p.GetType()), Does.Not.Contain(typeof(InductionVariablePass)),
                "InductionVariablePass is back in AddAggressivePasses. It has six defects and the "
                + "measured repairs are worse than the removal — read this fixture's docstring, and "
                + "the disabled AddPass line in IROptimizer.cs, before re-enabling it. The pass "
                + "list was: " + string.Join(", ", passes.Select(p => p.GetType().Name)));

            Assert.That(passes, Is.Not.Empty,
                "AddAggressivePasses added no passes at all, so the assertion above is vacuous");

            Assert.That(() => new InductionVariablePass(), Throws.Nothing,
                "the class must still exist and be constructible — the 'still broken' pin below "
                + "depends on it, and deleting the class would delete the record of the decision");
        });
    }

    /// <summary>
    /// The same guard at the emitted-output level, which is where the defect was actually visible:
    /// the aggressive pipeline must not introduce the pass's minted prefix, and must leave the
    /// multiply it would have strength-reduced standing.
    ///
    /// <para>⚠ <c>* 3</c>, not <c>* 2</c>. MEASURED: <c>AlgebraicSimplificationPass</c> (aggressive
    /// pass 9) rewrites <c>2 * x</c> to <c>x + x</c> before <c>InductionVariablePass</c> (pass 12)
    /// could see it, so a <c>* 2</c> shape was GREEN even with the pass enabled and would make this
    /// guard prove nothing.</para>
    /// </summary>
    [Test]
    public void TheAggressivePipeline_DoesNotStrengthReduceAnInductionVariable()
    {
        var js = JsTestSupport.CompileAggressive(CountedForWithAMultiply);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("_div_"),
                "the pass's minted prefix must not appear in shipped output:\n" + js);
            Assert.That(js, Does.Contain("Math.imul(i, 3)"),
                "the multiply stands — the JS backend's int-multiply lowering, the same spelling "
                + "FunctionInliningDisabledTests relies on:\n" + js);
        });
    }

    // ====================================================================================
    // Contract item 4: constructed directly, the pass is STILL BROKEN.
    // ====================================================================================

    /// <summary>
    /// ⛔ THE PASS IS STILL REACHABLE AND STILL BROKEN, pinned deliberately. This is what makes
    /// "disabled" a measured claim rather than a comment.
    ///
    /// <para>⚠ <b>ASSERTS THE MECHANISM, NOT A TEMP SPELLING.</b> Which identifier ends up
    /// undeclared depends on which OTHER passes run —
    /// <see cref="FunctionInliningDisabledTests.RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled"/>
    /// records that its own first draft asserted the <c>_inline_</c> spelling and FAILED on a
    /// pass-ordering artefact. So the assertions here are: an ORPHANED USE exists, and an
    /// UNDECLARED name with the pass's <c>_div_</c> prefix exists, and the pass MINTED a name that
    /// was not in the pre-optimization IR. All three are properties of the IR, found by the shared
    /// reflection walk, not strings in emitted code.</para>
    ///
    /// <para>Measured, run alone on the shape below, the JavaScript backend emits this
    /// <c>Run</c> — every defect visible at once:</para>
    ///
    /// <code>
    /// t1 = _div_t1;                            // t1 bare; _div_t1 read before its declaration
    /// Show(Math.imul(i, 3));                   // the original multiply, re-materialised
    /// const _div_t1 = ((_div_t1 + 3) | 0);     // re-declared each iteration, never initialised
    /// </code>
    ///
    /// <para>⚠ If someone repairs the pass, this test goes RED — it asserts that the defects are
    /// still there. ⛔ <b>RED HERE IS NOT A LICENCE TO RE-ENABLE THE PASS</b>, and this is
    /// measured, not cautionary: the "careful repair" (unique name, added to
    /// <c>LocalVariables</c>, definition removed and consumers forwarded the way
    /// <c>PeepholeOptimizationPass.ApplyRewrite</c> does it) turns every assertion in this test
    /// red — no orphan, no undeclared name, nothing minted with a user-visible spelling — and the
    /// resulting compiler still prints 0,3,6 for <c>For i = 1 To 3 : Show(i * 3)</c> where 3,6,9 is
    /// correct, because the derived variable is never initialised. Red here means READ
    /// <c>ACountedForStartingAtOne_RunsTheSameOnBothPipelines</c> and
    /// <c>OptimizerMintedVariableTests</c> next, and delete this test only once those are green
    /// too.</para>
    /// </summary>
    [Test]
    public void RunDirectly_ThePassStillMiscompiles_WhichIsWhyItIsDisabled()
    {
        var module = JsTestSupport.BuildModule(CountedForWithAMultiply, sourceFilePath: "prog.bas");
        var namesBefore = OptimizerIr.AllVariableNames(module);

        var pipeline = new OptimizationPipeline();
        pipeline.AddPass(new InductionVariablePass());
        pipeline.Run(module);

        var js = new JavaScriptCodeGenerator().Generate(module);
        var undeclared = OptimizerIr.UndeclaredOperandNames(module);
        var minted = OptimizerIr.AllVariableNames(module).Where(n => !namesBefore.Contains(n)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("_div_"),
                "the pass still rewrites — if it no longer does, this fixture's premise changed:\n" + js);

            Assert.That(OptimizerIr.DanglingOperandsOf(module), Is.Not.Empty,
                "defect 1: a consumer must still point at the IRBinaryOp the pass replaced. "
                + "The pass calls no ReplaceUses, so the multiply is re-materialised from the "
                + "orphan and the derived variable is dead weight:\n" + js);

            Assert.That(undeclared.Any(n => n.Contains("::_div_", StringComparison.Ordinal)), Is.True,
                "defect 2: the minted derived variable must still be referenced without being in "
                + "LocalVariables, Parameters or the globals. Undeclared names found: "
                + string.Join(", ", undeclared) + "\n" + js);

            Assert.That(minted.Any(n => n.Contains("::_div_", StringComparison.Ordinal)), Is.True,
                "defect 4: the pass must still mint a name that was absent from the "
                + "pre-optimization IR, in the user's own namespace. Minted: "
                + string.Join(", ", minted) + "\n" + js);

            Assert.That(js, Does.Contain("Math.imul(i, 3)"),
                "and the multiply the pass claims to have strength-reduced is STILL EMITTED, which "
                + "is why the pass never removed a single multiply even when it 'succeeded':\n" + js);
        });
    }

    // ====================================================================================
    // Contract item 5: the aggressive pipeline agrees with the default one, by VALUE.
    // ====================================================================================

    /// <summary>
    /// ⛔ THE PAYOFF: every counted-<c>For</c>-with-a-multiply shape now prints the same thing
    /// under <c>--optimize</c> as under the default pipeline, and both print the arithmetically
    /// correct answer. Asserting BOTH pipelines is the point — "the optimized path agrees with the
    /// unoptimized one" is the property, and a test that checked only one could not see a
    /// pipeline-specific miscompile at all.
    ///
    /// <para>⛔ <b>C++ AND MSIL ARE EXCLUDED, and the reason is measured and unrelated.</b> Under
    /// <c>AddAggressivePasses()</c> <c>LoopInvariantCodeMotionPass</c> sinks the loop condition's
    /// definition out of the condition block and into the loop's own LATCH, because
    /// <c>ControlFlowGraph.IdentifyLoops</c> hands it a "preheader" that IS the latch. C++ and MSIL
    /// emit the CFG as labels and branches, so they read the condition flag before anything writes
    /// it and run the loop ZERO TIMES; C# and JavaScript rebuild the condition from the CFG in
    /// their structured-loop emitters and are unaffected. MEASURED on
    /// <c>For i = 0 To n : Show(i) : Next</c> — the counter passed through untouched, so no
    /// arithmetic pass has anything to act on — where the emitted C++ is literally
    /// <c>bool t1 = {}; … for0_cond: if (t1) …</c> with <c>t1 = i &lt;= n</c> moved down into
    /// <c>for0_inc</c>, and both backends print only the line AFTER the loop. That is issue #114.
    /// A four-backend aggressive loop assertion CANNOT go green until it lands, and writing one
    /// that expects the right answer would just be a red test about a different defect.
    /// <see cref="TheSharedAggressiveFourBackendRunner_AgreesOnAShapeWithNoLoop"/> exercises all
    /// four legs of the shared runner on a shape #114 does not touch, so the runner itself is not
    /// unverified.</para>
    ///
    /// <para>⚠ ONE SHAPE PER TEST. Both harnesses use <c>Assert.Multiple</c>, and the C# leg is
    /// in-process Roslyn with NO timeout, so several programs in one case would report the first
    /// one's compile error for all of them and a non-terminating program would hang the test host.
    /// <c>[TestCase]</c> gives one NUnit test per shape.</para>
    ///
    /// <para>⚠ <c>Show(...)</c> is load-bearing in every shape: constant folding turns
    /// <c>PrintLine(CStr(2 * 3))</c> into a literal, which is a passing control that proves
    /// nothing. So is the runtime-valued bound <c>Bound()</c> — a literal bound lets the loop be
    /// unrolled or folded instead of rewritten.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("S=0\nS=3\nS=6\nS=9\nDONE", " Show(i * 3)",
        TestName = "AnInductionVariableTimesAConstant")]
    [TestCase("S=0\nS=3\nS=6\nS=9\nDONE", " Show(3 * i)",
        TestName = "AConstantTimesAnInductionVariable")]
    [TestCase("S=0\nS=3\nS=6\nS=9\nDONE", " Dim x As Integer = i * 3\n  Show(x)",
        TestName = "AMultiplyOntoADeclaredLocal_WhichMintsOneUndeclaredNameNotTwo")]
    [TestCase("S=0\nS=0\nS=3\nS=3\nS=6\nS=6\nS=9\nS=9\nDONE", " Show(i * 3)\n  Show(i * 3)",
        TestName = "TheSameMultiplyTwiceInOneBody")]
    public void ACountedForWithAMultiply_RunsTheSameOnBothPipelines(string expected, string body)
    {
        BothPipelinesAgree(ForBody(body), expected);
    }

    /// <summary>
    /// ⛔ A LOOP THAT DOES NOT START AT ZERO. This is the shape the whole family needs and the one
    /// a fixture built only on <c>For i = 0 To n</c> cannot supply: it is the ONLY thing that
    /// distinguishes "the pass is fixed" from "the derived variable is declared but never
    /// initialised", and it does so SILENTLY — the measured repair emits perfectly valid code that
    /// prints 0,3,6 instead of 3,6,9. Hence asserted BY VALUE, and hence on C# and JavaScript,
    /// the only two backends that can show a wrong VALUE at all while #114 is open.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ACountedForStartingAtOne_RunsTheSameOnBothPipelines()
    {
        BothPipelinesAgree(
            """
            Sub Show(v As Integer)
             PrintLine("S=" & CStr(v))
            End Sub
            Function Bound() As Integer
             Return 3
            End Function
            Sub Main()
             Dim n As Integer = Bound()
             For i As Integer = 1 To n
              Show(i * 3)
             Next
             PrintLine("DONE")
            End Sub
            """,
            "S=3\nS=6\nS=9\nDONE");
    }

    /// <summary>
    /// ⛔ TWO MULTIPLIES ONTO ONE LOCAL, asserted BY VALUE, because this is the shape that turns a
    /// candidate repair from a loud failure into a quiet one and only a value can tell them apart.
    /// At <c>67782af</c> it was a build failure — C# <c>CS0103: _div_x</c>, JavaScript
    /// <c>SyntaxError: Identifier '_div_x' has already been declared</c>, because both multiplies
    /// mint the SAME <c>_div_x</c>. Repairing the two briefed defects made it compile and print
    /// 0,0,8,8,16,16,24,24 — the two derived increments, +3 and +5, landing in one block and one
    /// variable — where 0,0,3,5,6,10,9,15 is correct.
    ///
    /// <para>⚠ The JavaScript error is a <c>SyntaxError</c> about a redeclaration, NOT the
    /// <c>ReferenceError</c> the single-multiply shape gives. That is why this fixture asserts
    /// values and mechanisms rather than error spellings.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TwoMultipliesOntoOneLocal_RunTheSameOnBothPipelines()
    {
        BothPipelinesAgree(
            """
            Sub Show(v As Integer)
             PrintLine("S=" & CStr(v))
            End Sub
            Function Bound() As Integer
             Return 3
            End Function
            Sub Main()
             Dim n As Integer = Bound()
             Dim x As Integer = 0
             For i As Integer = 0 To n
              x = i * 3
              Show(x)
              x = i * 5
              Show(x)
             Next
             PrintLine("DONE")
            End Sub
            """,
            "S=0\nS=0\nS=3\nS=5\nS=6\nS=10\nS=9\nS=15\nDONE");
    }

    /// <summary>
    /// ⭐ THE SILENT SHAPE, and the only assertion in the suite that can see it. A program that
    /// already contains <c>Dim _div_x As Integer = 99</c> COMPILED CLEANLY at <c>67782af</c> and
    /// printed 198, 204, 210, 216 where 99, 102, 105, 108 is correct — on C# (this suite's
    /// reference oracle) and on JavaScript. The pass's derived variable collided with the user's,
    /// inherited its 99, and <c>Show(x + _div_x)</c> then added the same number to itself.
    ///
    /// <para>⛔ NO IR-LEVEL INVARIANT CAN CATCH THIS. Measured: with the pass enabled, this shape
    /// reports ZERO orphaned operands, ZERO undeclared names and ZERO minted names — because the
    /// name the pass mints is one the USER declared. This is ADR-0001's "a namespace users cannot
    /// enter" bullet, live, and it is the reason a compiler pass must not name anything in the
    /// user's namespace even when it declares it properly. It can only be asserted BY VALUE.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void AUserVariableSpelledLikeTheMintedName_IsNotClobbered()
    {
        BothPipelinesAgree(
            """
            Sub Show(v As Integer)
             PrintLine("S=" & CStr(v))
            End Sub
            Function Bound() As Integer
             Return 3
            End Function
            Sub Main()
             Dim n As Integer = Bound()
             Dim _div_x As Integer = 99
             Dim x As Integer = 0
             For i As Integer = 0 To n
              x = i * 3
              Show(x + _div_x)
             Next
             PrintLine("DONE")
            End Sub
            """,
            "S=99\nS=102\nS=105\nS=108\nDONE");
    }

    /// <summary>
    /// ⛔ THE COUNTER BUMPED INSIDE THE BODY. The correct answer is 0, 6 — the body's <c>i = i + 1</c>
    /// and the loop's own increment both advance it, so it takes two steps per iteration. A derived
    /// variable stepping by the loop increment alone desynchronises and gives 0, 3. The pass's
    /// analysis records an increment without ever checking it is the ONLY definition of the counter
    /// in the loop, so any repair without a single-definition guard is wrong here.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ACounterBumpedInsideTheBody_RunsTheSameOnBothPipelines()
    {
        BothPipelinesAgree(
            """
            Sub Show(v As Integer)
             PrintLine("S=" & CStr(v))
            End Sub
            Function Bound() As Integer
             Return 3
            End Function
            Sub Main()
             Dim n As Integer = Bound()
             For i As Integer = 0 To n
              Show(i * 3)
              i = i + 1
             Next
             PrintLine("DONE")
            End Sub
            """,
            "S=0\nS=6\nDONE");
    }

    /// <summary>
    /// A <c>Step</c> other than 1: the derived increment is <c>step * constant</c>, so a repair that
    /// assumes a unit step is wrong here. Correct is 0, 6 — <c>i</c> takes the values 0 and 2.
    /// </summary>
    [Test]
    [Category("Integration")]
    public void ACountedForWithAStep_RunsTheSameOnBothPipelines()
    {
        BothPipelinesAgree(
            """
            Sub Show(v As Integer)
             PrintLine("S=" & CStr(v))
            End Sub
            Function Bound() As Integer
             Return 3
            End Function
            Sub Main()
             Dim n As Integer = Bound()
             For i As Integer = 0 To n Step 2
              Show(i * 3)
             Next
             PrintLine("DONE")
            End Sub
            """,
            "S=0\nS=6\nDONE");
    }

    /// <summary>
    /// ⛔ A MULTIPLY ON THE INNER COUNTER OF A NESTED LOOP, <b>C# ONLY</b>, asserted by value.
    /// Correct is 0,3,6,0,3,6: the inner counter restarts on every outer iteration, so a derived
    /// variable seeded once — in the function's ENTRY block, say, which is a correct preheader for
    /// a top-level loop and the obvious place to put it — is stale from the second outer iteration
    /// on. This is the shape that shows why the missing initialisation cannot be fixed without a
    /// real preheader.
    ///
    /// <para>⛔ <b>JAVASCRIPT IS EXCLUDED, measured, and it is NOT this defect.</b> Under
    /// <c>--optimize</c> a nested <c>For</c> gives
    /// <c>ReferenceError: t4 is not defined</c> on the JavaScript backend: LICM sinks the OUTER
    /// loop's increment <c>t4 = i + 1</c> into the INNER loop's latch, where the emitter declares
    /// it <c>const</c> inside the inner block, so the outer loop's <c>i = t4</c> reads a name that
    /// is out of scope. Same root cause as the C++/MSIL exclusion above — issue #114 — and newly
    /// VISIBLE now that the <c>_div_</c> failure no longer masks it. C++ and MSIL are excluded for
    /// the zero-iteration reason already documented. So C# is the only backend that can carry this
    /// shape today, and it is this suite's reference oracle.</para>
    /// </summary>
    // ⚠ NOT [Category("Integration")], deliberately: both legs are the in-process Roslyn C#
    // harness, which compiles and runs without a toolchain or a child process. Repo precedent is
    // CSharpRightReceiverTests.Right_WithANegativeLength_Throws, which uses the same harness and is
    // in the fast subset. Keeping it there matters — this is the shape that distinguishes a correct
    // repair from one whose derived variable is seeded outside the inner loop, and it should be
    // visible in the 2-minute run.
    [Test]
    public void AMultiplyOnTheInnerCounterOfANestedLoop_RunsTheSameOnBothPipelines_CSharpOnly()
    {
        const string program = """
            Sub Show(v As Integer)
             PrintLine("S=" & CStr(v))
            End Sub
            Sub Main()
             For i As Integer = 0 To 1
              For j As Integer = 0 To 2
               Show(j * 3)
              Next
             Next
             PrintLine("DONE")
            End Sub
            """;
        const string expected = "S=0\nS=3\nS=6\nS=0\nS=3\nS=6\nDONE";

        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected),
                "C#, default pipeline");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(program)), Is.EqualTo(expected),
                "C#, AGGRESSIVE pipeline — a derived induction variable seeded outside the inner "
                + "loop goes stale here, and the wrong answer is 0,3,6,9,12,15");
        });
    }

    // ====================================================================================
    // The shared aggressive four-backend runner, exercised on all four legs.
    // ====================================================================================

    /// <summary>
    /// ⭐ <c>FourBackends.RunsOnEveryBackendAggressive</c> exercised on ALL FOUR legs, so the
    /// shared runner this change adds is not unverified code that only ever runs two of its four
    /// backends.
    ///
    /// <para>The shape deliberately has NO LOOP — <c>Return (2 * p) + 1</c>, which
    /// <c>AlgebraicSimplificationPass</c> (aggressive-only) does rewrite, so the aggressive
    /// pipeline is genuinely doing something — because issue #114 makes any counted loop print the
    /// wrong thing on C++ and MSIL under this pipeline. MEASURED: all four backends print 13,
    /// under both pipelines.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void TheSharedAggressiveFourBackendRunner_AgreesOnAShapeWithNoLoop()
    {
        const string program = """
            Module M
             Function F(p As Integer) As Integer
              Return (2 * p) + 1
             End Function
             Sub Main()
              PrintLine(CStr(F(6)))
             End Sub
            End Module
            """;

        FourBackends.RunsOnEveryBackendAggressive(program, "13");
    }

    // ====================================================================================
    // Both shipping entry points, aggressive.
    // ====================================================================================

    /// <summary>
    /// ⛔ BOTH SHIPPING ENTRY POINTS, AGGRESSIVE — not just a fixture-local pipeline. This is where
    /// the pass actually shipped: <c>BasicCompiler.CompileFile</c> is what
    /// <c>BasicLang.exe prog.bas --target=… --optimize</c> runs, and
    /// <c>CompileProjectFiles</c> is what the CLI's <c>build</c> runs AND what the IDE's build
    /// service delegates to, which is the route a Release <c>.blproj</c> takes. Both read
    /// <c>CompilerOptions.OptimizeAggressive</c> and both build their own pipeline, so a fix
    /// verified only through the in-fixture helper could still break them.
    ///
    /// <para>The IR each entry point hands a backend is asserted directly, so this covers contract
    /// items 1 and 2 on the real routes: no orphaned use, and no operand naming a variable the
    /// pipeline did not declare.</para>
    ///
    /// <para>⚠ The SPAWNED <c>BasicLang.exe</c> leg (<c>CliTestHarness</c>) is deliberately not
    /// used: it is a Windows apphost that is not deployed on Linux, so every fixture built on it
    /// already sits in this machine's baseline failure set. Calling the compiler's own entry points
    /// in process exercises the same engine and actually runs here.</para>
    /// </summary>
    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_Aggressive_LeavesCleanIr")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_Aggressive_LeavesCleanIr")]
    public void BothCompilerEntryPoints_Aggressive_ProduceCleanIr(bool asProject)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "BasicLang_IvAggrEntry_" + System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var file = System.IO.Path.Combine(dir, "prog.bas");
            System.IO.File.WriteAllText(file, CountedForWithAMultiply);

            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { OptimizeAggressive = true });

            var result = asProject
                ? compiler.CompileProjectFiles(new[] { file })
                : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            Assert.Multiple(() =>
            {
                Assert.That(OptimizerIr.DanglingOperandsOf(result.CombinedIR), Is.Empty,
                    "contract item 1 on the real aggressive route: a use still points at an "
                    + "instruction a pass removed");
                Assert.That(OptimizerIr.UndeclaredOperandNames(result.CombinedIR), Is.Empty,
                    "contract item 2 on the real aggressive route: an operand names a variable the "
                    + "pipeline did not declare");
            });
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { /* temp */ } }
    }

    // ====================================================================================
    // Helpers.
    // ====================================================================================

    /// <summary>
    /// The canonical shape: a counted <c>For</c> whose body multiplies the counter by a constant.
    /// <c>* 3</c> rather than <c>* 2</c> because <c>AlgebraicSimplificationPass</c> rewrites
    /// <c>2 * x</c> first; a runtime bound rather than a literal so the loop is not folded or
    /// unrolled instead.
    /// </summary>
    private const string CountedForWithAMultiply = """
        Sub Show(p As Integer)
         PrintLine("V=" & CStr(p))
        End Sub
        Sub Run(n As Integer)
         Dim i As Integer
         For i = 0 To n
          Show(i * 3)
         Next
        End Sub
        Sub Main()
         Run(2)
        End Sub
        """;

    private static string ForBody(string body) => $"""
        Sub Show(v As Integer)
         PrintLine("S=" & CStr(v))
        End Sub
        Function Bound() As Integer
         Return 3
        End Function
        Sub Main()
         Dim n As Integer = Bound()
         For i As Integer = 0 To n
        {body}
         Next
         PrintLine("DONE")
        End Sub
        """;

    /// <summary>
    /// The oracle for contract item 5: C# and JavaScript, each under the DEFAULT pipeline and
    /// under the AGGRESSIVE one, all four equal to the arithmetically correct value.
    ///
    /// <para>C++ and MSIL are absent by measurement, not oversight — see
    /// <see cref="ACountedForWithAMultiply_RunsTheSameOnBothPipelines"/> for the #114 evidence.
    /// Asserting "both pipelines agree" alone would not be enough either: two pipelines can agree
    /// on a wrong answer, so the expected value is the arithmetic truth.</para>
    /// </summary>
    private static void BothPipelinesAgree(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected),
                "C#, default pipeline");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(program)), Is.EqualTo(expected),
                "C#, AGGRESSIVE pipeline — this was a CS0103 build failure while the pass ran");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected),
                "JavaScript, default pipeline");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(program)), Is.EqualTo(expected),
                "JavaScript, AGGRESSIVE pipeline — this was a ReferenceError while the pass ran");
        });
    }

    /// <summary>
    /// The pipeline's pass list. Reflection because <c>_passes</c> is private and the list IS the
    /// subject: contract item 3 is a statement about what <c>AddAggressivePasses</c> puts in it,
    /// and inferring that from emitted text instead would make the guard depend on the minted
    /// prefix staying the same.
    /// </summary>
    private static List<OptimizationPass> PassesOf(OptimizationPipeline pipeline)
    {
        var field = typeof(OptimizationPipeline).GetField("_passes",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null,
            "OptimizationPipeline._passes is gone — this guard needs re-anchoring, not deleting");
        return ((IEnumerable<OptimizationPass>)field!.GetValue(pipeline)!).ToList();
    }
}
