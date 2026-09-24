using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Behaviour-level pins for family #111's C# materialisation fix (ADR-0001, gated per
/// ADR-0004 D2/D4): under the aggressive pipeline (<c>cli --optimize</c>, a Release
/// <c>.blproj</c> build), a value used more than once whose definition is not replicable is
/// evaluated exactly ONCE on C#; a replicable multi-use value stays inlined. Today
/// materialisation fires only on <c>AlgebraicSimplification</c>'s <c>2*x → x+x</c> output — every
/// program here goes through that rewrite.
///
/// <para>⚠ Every shape prints a side-effecting <c>Console.WriteLine("tag")</c>/<c>"seed"</c>/
/// <c>"next"</c> from inside the multi-use call, so a shape that silently evaluates it an EXTRA
/// time is visible in the transcript, not just in the final value — the discriminator CLAUDE.md
/// and <c>docs/HANDOFF.md</c> both call out for this family. Expected output is asserted as a
/// LITERAL string, never cross-backend agreement alone, so an extra/missing "tag" line fails
/// loudly regardless of what the other backends do.</para>
///
/// <para>⚠ <c>Assert.Multiple</c> inside <c>FourBackends.RunsOnEveryBackendAggressive</c>/
/// <c>MsilHarness</c>: ONE shape per test (docs/HANDOFF.md's trap). Constant folding destroys
/// these shapes if the multi-use value has no visible side effect, so every probe here feeds the
/// doubled value from a FUNCTION CALL, never a bare literal.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class Family111MaterialisationBehaviourTests
{
    // ====================================================================================
    // 2 — S1, M5: a top-level `2 * Tag()` materialises; "tag" prints exactly once.
    // ====================================================================================

    /// <summary>S1 — Integer. All four backends agree (C++/JS/MSIL already materialised before
    /// this fix; C# is the one this family repairs).</summary>
    [Test]
    public void S1_TopLevelDoubledCall_PrintsTagOnce()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim r As Integer = 2 * Tag()\n" +
            " Console.WriteLine(r)\n" +
            "End Sub",
            "tag\n6");

    /// <summary>M5 — Double, so the materialised local's default/type must be right too, not
    /// just Integer's. All four backends agree.</summary>
    [Test]
    public void M5_TopLevelDoubledCall_Double_PrintsTagOnce()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Function Tag() As Double\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 1.5\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim r As Double = 2 * Tag()\n" +
            " Console.WriteLine(r)\n" +
            "End Sub",
            "tag\n3");

    // ====================================================================================
    // 2 — LP2: the doubled call lives in a LOOP BODY (not the loop's condition block), one
    // materialisation per iteration. All four backends agree.
    // ====================================================================================

    [Test]
    public void LP2_DoubledCallInsideAForLoopBody_PrintsTagOncePerIteration()
        => FourBackends.RunsOnEveryBackendAggressive(
            "Function Tag(v As Integer) As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return v\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim s As Integer = 0\n" +
            " For i = 1 To 3\n" +
            "  s = s + 2 * Tag(i)\n" +
            " Next\n" +
            " Console.WriteLine(s)\n" +
            "End Sub",
            "tag\ntag\ntag\n12");

    // ====================================================================================
    // 2 — LP3: both arms of an If (a Function's Return) AND a property Get, each with their own
    // doubled call. ⚠ C++ is NOT part of the oracle here: docs/HANDOFF.md's "C++ cannot compile
    // ANY property with an explicit Get/Set accessor" gap (unrelated to this family, not fixed
    // here) refuses the Box class outright. C#, JavaScript and MSIL agree.
    // ====================================================================================

    [Test]
    public void LP3_IfArmsAndAPropertyGetter_EachPrintTagOnce()
    {
        const string program =
            "Function Tag(v As Integer) As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return v\n" +
            "End Function\n\n" +
            "Function Pick(b As Boolean) As Integer\n" +
            " If b Then\n" +
            "  Return 2 * Tag(10)\n" +
            " Else\n" +
            "  Return 2 * Tag(20)\n" +
            " End If\n" +
            "End Function\n\n" +
            "Class Box\n" +
            " Private _v As Integer\n" +
            " Public Sub Seed(v As Integer)\n" +
            "  _v = v\n" +
            " End Sub\n" +
            " Public ReadOnly Property Doubled As Integer\n" +
            "  Get\n" +
            "   Return 2 * Tag(_v)\n" +
            "  End Get\n" +
            " End Property\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Console.WriteLine(Pick(True))\n" +
            " Console.WriteLine(Pick(False))\n" +
            " Dim b As New Box()\n" +
            " b.Seed(4)\n" +
            " Console.WriteLine(b.Doubled)\n" +
            "End Sub";
        const string expected = "tag\n20\ntag\n40\ntag\n8";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(program)), Is.EqualTo(expected), "C#");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(program)), Is.EqualTo(expected), "JavaScript");
        });
    }

    // ====================================================================================
    // 3 — S4, S4b: property accessor locals compile and run on C#, on all THREE entry points —
    // the in-process aggressive-pipeline unit helper, the CLI single-file entry point
    // (BasicCompiler.CompileFile), and the project/Release-.blproj entry point
    // (BasicCompiler.CompileProjectFiles). Mirrors the pattern
    // StatementOperandUndeclaredTempFixTests.BothCompilerEntryPoints_* already uses for C4.
    //
    // ⭐ Before this fix, GenerateProperty emitted NO local declarations at all for an accessor
    // body — S4b (no loop, just `Dim sum As Integer = _v` / `Return sum + 1`) is the isolated
    // case proving that "the loop is not what breaks it" (see the promoted
    // MsilForEachTests.ForEachInAPropertyAccessorBody, S4's exact program, which used to exclude
    // C# for the same CS0103 on `sum`).
    // ====================================================================================

    private const string S4 =
        "Class Box\n" +
        " Private _items As List(Of Integer)\n" +
        " Public Sub Seed(l As List(Of Integer))\n" +
        "  _items = l\n" +
        " End Sub\n" +
        " Public ReadOnly Property Total As Integer\n" +
        "  Get\n" +
        "   Dim sum As Integer = 0\n" +
        "   For Each n In _items\n" +
        "    sum = sum + n\n" +
        "   Next\n" +
        "   Return sum\n" +
        "  End Get\n" +
        " End Property\n" +
        "End Class\n\n" +
        "Sub Main()\n" +
        " Dim l As New List(Of Integer)()\n" +
        " l.Add(3)\n" +
        " l.Add(4)\n" +
        " Dim b As New Box()\n" +
        " b.Seed(l)\n" +
        " Console.WriteLine(b.Total)\n" +
        "End Sub";

    private const string S4Expected = "7";

    [Test]
    public void S4_PropertyAccessorLocal_WithAForEachLoop_UnitHelperEntryPoint()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(S4)), Is.EqualTo(S4Expected),
            "C#, aggressive pipeline, in-process unit helper");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_S4_PropertyAccessorLocal")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_S4_PropertyAccessorLocal")]
    public void BothCompilerEntryPoints_S4_PropertyAccessorLocal(bool asProject)
        => Assert.That(RunThroughEntryPoint(S4, asProject), Is.EqualTo(S4Expected),
            (asProject ? "CompileProjectFiles" : "CompileFile") + " printed the wrong answer.");

    private const string S4b =
        "Class Box\n" +
        " Private _v As Integer\n" +
        " Public Sub Seed(v As Integer)\n" +
        "  _v = v\n" +
        " End Sub\n" +
        " Public ReadOnly Property Total As Integer\n" +
        "  Get\n" +
        "   Dim sum As Integer = _v\n" +
        "   Return sum + 1\n" +
        "  End Get\n" +
        " End Property\n" +
        "End Class\n\n" +
        "Sub Main()\n" +
        " Dim b As New Box()\n" +
        " b.Seed(5)\n" +
        " Console.WriteLine(b.Total)\n" +
        "End Sub";

    private const string S4bExpected = "6";

    /// <summary>⭐ THE ISOLATED CASE — no loop anywhere in this accessor body, so a fix that only
    /// covered the For Each shape would still leave this CS0103.</summary>
    [Test]
    public void S4b_PropertyAccessorLocal_NoLoopAtAll_UnitHelperEntryPoint()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(S4b)), Is.EqualTo(S4bExpected),
            "C#, aggressive pipeline, in-process unit helper");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_S4b_PropertyAccessorLocal")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_S4b_PropertyAccessorLocal")]
    public void BothCompilerEntryPoints_S4b_PropertyAccessorLocal(bool asProject)
        => Assert.That(RunThroughEntryPoint(S4b, asProject), Is.EqualTo(S4bExpected),
            (asProject ? "CompileProjectFiles" : "CompileFile") + " printed the wrong answer.");

    // ====================================================================================
    // 4 — Must-not-change controls.
    //
    // C4 (task #125's CSE-merge-across-reassignment hazard) is NOT duplicated here — it is
    // already pinned by StatementOperandUndeclaredTempFixTests.TheNamedBranch_WouldHave...
    // (and its _Aggressive / BothCompilerEntryPoints_ siblings). Confirmed still green on this
    // tree (see this task's report) rather than re-asserted here.
    //
    // S8 IS new here: a DIFFERENT value-stability shape — `p + q` computed twice as TWO
    // SEPARATE, un-merged IRBinaryOp instructions (`Dim a = p + q`, then `p = Seed(100)`, then
    // `Dim b = p + q`), each read through its own DECLARED local rather than through one of the
    // four EmitOperand-era sites C4 exercises. It is a control because this family's
    // materialisation must not conflate the two `p + q` occurrences into one shared,
    // once-computed value — each is its own instruction with its own (here, single) use count,
    // so neither is ever a materialisation CANDIDATE at all. `a` must stay `3` (computed before
    // the reassignment) and `b` must become `102` (computed after) — MEASURED unaffected by this
    // family's patch.
    // ====================================================================================

    [Test]
    public void S8_TwoSeparateSumsAroundAReassignment_ValueStabilityUnaffected()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(
            "Function Seed(v As Integer) As Integer\n" +
            " Console.WriteLine(\"seed\")\n" +
            " Return v\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim p As Integer = Seed(1)\n" +
            " Dim q As Integer = Seed(2)\n" +
            " Dim a As Integer = p + q\n" +
            " p = Seed(100)\n" +
            " Dim b As Integer = p + q\n" +
            " Console.WriteLine(\"a=\" & CStr(a) & \" b=\" & CStr(b))\n" +
            "End Sub")),
            Is.EqualTo("seed\nseed\nseed\na=3 b=102"),
            "C#, aggressive pipeline — `a` must be computed BEFORE `p` is reassigned (3) and `b` "
            + "AFTER (102); if this drifts, materialisation started sharing state across the two "
            + "textually-identical-but-distinct `p + q` instructions.");

    // ====================================================================================
    // G6, G7 — step9b's comment-only correction to ComputeMaterialisedTemps's named-after-
    // variable exclusion. C4 (StatementOperandUndeclaredTempFixTests) does NOT exercise this
    // exclusion: its shared `p + q` has two LOCAL operands, both unconditionally replicable, so
    // it is never a materialisation candidate regardless of the exclusion. G6/G7 instead give
    // the shared binop a NON-replicable operand (a non-Const global; a ByRef parameter) so it
    // IS a candidate, and the exclusion is what keeps C# right where C++/JS/MSIL are wrong
    // (task #125 — CSE merges `g + q` at `Dim a` and at `l(0)`/`l(1)` without accounting for the
    // intervening `a = z`, then each backend's OWN store-operand codegen reads the merged,
    // stale-named value back). Asserted as LITERAL output, never cross-backend agreement — same
    // convention as C4's pins in StatementOperandUndeclaredTempFixTests.
    // ====================================================================================

    private const string G6 =
        "Dim g As Integer\n\n" +
        "Function Seed(v As Integer) As Integer\n" +
        " Console.WriteLine(\"seed\")\n" +
        " Return v\n" +
        "End Function\n\n" +
        "Sub Main()\n" +
        " g = Seed(1)\n" +
        " Dim q As Integer = Seed(2)\n" +
        " Dim z As Integer = Seed(0)\n" +
        " Dim l As New List(Of Integer)()\n" +
        " l.Add(0)\n" +
        " l.Add(0)\n" +
        " Dim a As Integer = g + q\n" +
        " a = z\n" +
        " l(0) = g + q\n" +
        " l(1) = g + q\n" +
        " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)) & \",\" & CStr(a))\n" +
        "End Sub";

    private const string G6Correct = "seed\nseed\nseed\n3,3,0";
    private const string G6Wrong = "seed\nseed\nseed\n0,0,0";

    /// <summary>⭐ THE MUTANT KILL for "re-remove the named-after-variable exclusion" — the shape
    /// C4 cannot provide, because C4's shared value has only local operands (always replicable,
    /// never a materialisation candidate at all). Here the shared `g + q` has a non-Const GLOBAL
    /// operand, so without the exclusion it becomes eligible, gets materialised (declared once),
    /// and both `l(0)` and `l(1)` read the FROZEN, stale value instead of re-evaluating — exactly
    /// C++/JS/MSIL's #125 defect, now also on C#. MEASURED correct on all three entry points.</summary>
    [Test]
    public void G6_GlobalOperand_StandardPipeline()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(G6)), Is.EqualTo(G6Correct),
            "C#, standard pipeline (CSE is a standard pass) — must recompute `g + q` at each "
            + "store rather than reading a CSE-merged, since-reassigned name. If this fails with "
            + "'0,0,0' instead of '3,3,0', the named-after-variable exclusion in "
            + "ComputeMaterialisedTemps has been removed or weakened.");

    [Test]
    public void G6_GlobalOperand_AggressivePipeline()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(G6)), Is.EqualTo(G6Correct),
            "C#, aggressive pipeline — same reasoning, same kill.");

    [Test]
    public void G6_GlobalOperand_ReleaseProjectBuild()
        => Assert.That(RunThroughEntryPoint(G6, asProject: true), Is.EqualTo(G6Correct),
            "C#, CompileProjectFiles (Release .blproj) — same reasoning, same kill.");

    /// <summary>
    /// ⛔ PINNED WRONG — task #125, same mechanism as C4's C++/JS/MSIL pins. Not this family's
    /// to fix; if #125 is fixed these three go RED — that is progress, update or delete the pin.
    /// </summary>
    [Test]
    public void G6_Cpp_StillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(G6))), Is.EqualTo(G6Wrong),
            "if this changed, task #125 (CSE merging g+q across a's reassignment) may be fixed on "
            + "C++ — update or delete this pin, do not just widen it");

    [Test]
    public void G6_JavaScript_StillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(G6)), Is.EqualTo(G6Wrong),
            "if this changed, task #125 may be fixed on JavaScript's optimizing path — update or "
            + "delete this pin, do not just widen it");

    [Test]
    public void G6_Msil_StillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(G6)), Is.EqualTo(G6Wrong),
            "if this changed, task #125 may be fixed on MSIL — update or delete this pin, do not "
            + "just widen it");

    // ------------------------------------------------------------------------------------
    // G7 — the identical shape, but the shared binop's non-replicable operand is a ByRef
    // PARAMETER instead of a global. JavaScript refuses ByRef outright (BL7002) and is asserted
    // structurally (it must still refuse it, not silently accept it or fail some other way);
    // C++ and MSIL are the two backends #125 is measured wrong on here.
    // ------------------------------------------------------------------------------------

    private const string G7 =
        "Function Seed(v As Integer) As Integer\n" +
        " Console.WriteLine(\"seed\")\n" +
        " Return v\n" +
        "End Function\n\n" +
        "Sub Work(ByRef n As Integer)\n" +
        " Dim q As Integer = Seed(2)\n" +
        " Dim z As Integer = Seed(0)\n" +
        " Dim l As New List(Of Integer)()\n" +
        " l.Add(0)\n" +
        " l.Add(0)\n" +
        " Dim a As Integer = n + q\n" +
        " a = z\n" +
        " l(0) = n + q\n" +
        " l(1) = n + q\n" +
        " Console.WriteLine(CStr(l(0)) & \",\" & CStr(l(1)) & \",\" & CStr(a))\n" +
        "End Sub\n\n" +
        "Sub Main()\n" +
        " Dim v As Integer = 1\n" +
        " Work(v)\n" +
        "End Sub";

    private const string G7Correct = "seed\nseed\n3,3,0";
    private const string G7Wrong = "seed\nseed\n0,0,0";

    [Test]
    public void G7_ByRefOperand_StandardPipeline()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(G7)), Is.EqualTo(G7Correct),
            "C#, standard pipeline — same reasoning as G6, but the non-replicable operand is a "
            + "ByRef parameter rather than a global.");

    [Test]
    public void G7_ByRefOperand_AggressivePipeline()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(G7)), Is.EqualTo(G7Correct),
            "C#, aggressive pipeline — same reasoning, same kill.");

    [Test]
    public void G7_ByRefOperand_ReleaseProjectBuild()
        => Assert.That(RunThroughEntryPoint(G7, asProject: true), Is.EqualTo(G7Correct),
            "C#, CompileProjectFiles (Release .blproj) — same reasoning, same kill.");

    [Test]
    public void G7_Cpp_StillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppAggressive(G7))), Is.EqualTo(G7Wrong),
            "if this changed, task #125 may be fixed on C++ — update or delete this pin, do not "
            + "just widen it");

    [Test]
    public void G7_Msil_StillReadsTheCseMergedVariable_PinnedForTask125()
        => Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(G7)), Is.EqualTo(G7Wrong),
            "if this changed, task #125 may be fixed on MSIL — update or delete this pin, do not "
            + "just widen it");

    /// <summary>
    /// ⛔ JavaScript refuses ByRef outright — BL7002, structural (JS has no reference parameters
    /// at all; this is not #125's doing and is asserted here so a future change cannot silently
    /// make JS "agree" with C++/MSIL by accepting the program and printing 0,0,0 too).
    /// </summary>
    [Test]
    public void G7_JavaScript_RefusesByRef_BL7002()
    {
        var module = JsTestSupport.BuildModule(G7);
        var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
            () => new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator().Generate(module));
        Assert.That(ex!.Message, Does.Contain("BL7002"));
        Assert.That(ex.Message, Does.Contain("ByRef"));
    }

    // ====================================================================================
    // 5 — Known limitations, PINNED as known-wrong with the MEASURED output. Per this repo's
    // pinned-broken convention (LoopPassesDisabledTests / InductionVariableDisabledTests /
    // StatementOperandUndeclaredTempFixTests' task-125 pins): these fail LOUDLY, not silently,
    // the day the named fix lands — update or delete the pin then, do not widen it.
    // ====================================================================================

    /// <summary>
    /// ⛔ PINNED WRONG — LP1: the doubled call lives in a LOOP'S OWN CONDITION, not its body.
    /// <c>ComputeMaterialisedTemps</c> deliberately excludes a loop-condition block (its
    /// instructions are written once, before the C# <c>while</c>, and the condition text is
    /// RE-EMITTED each iteration — a local written once would freeze it), so the multi-use call
    /// stays inlined there and is evaluated TWICE per condition check instead of once.
    ///
    /// <para>MEASURED, on all three entry points (in-process unit helper, CLI single-file,
    /// project build): <c>next</c> ×4, final value <c>1</c> — <c>counter</c> only reaches 3 by
    /// the FOURTH check (2 evaluations/check except the last, which short-circuits), so the loop
    /// body (<c>n = n + 1</c>) runs once, not the CORRECT <c>3</c> a single evaluation per check
    /// would produce.</para>
    ///
    /// <para>⭐ <b>Fixed by:</b> ADR-0004 D4, step 3 — gating <c>AlgebraicSimplificationPass</c>'s
    /// <c>2*x → x+x</c> rewrite on <c>IsReplicable</c> ("#111 part 2 step c"). Once the rewrite
    /// itself does not fire for a non-replicable operand, there is no doubled call in the
    /// condition block for materialisation to have to reach.</para>
    /// </summary>
    [Test]
    public void LP1_DoubledCallInATheLoopsOwnCondition_PinnedWrong_UnitHelperEntryPoint()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(
            "Dim counter As Integer\n\n" +
            "Function NextVal() As Integer\n" +
            " counter = counter + 1\n" +
            " Console.WriteLine(\"next\")\n" +
            " Return counter\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " counter = 0\n" +
            " Dim n As Integer = 0\n" +
            " Do While 2 * NextVal() < 7\n" +
            "  n = n + 1\n" +
            " Loop\n" +
            " Console.WriteLine(n)\n" +
            "End Sub")),
            Is.EqualTo("next\nnext\nnext\nnext\n1"),
            "if this changed (correct is next×4 then 3), ADR-0004 D4 step 3's 2*x replicability "
            + "gate may be shipped — update or delete this pin, do not just widen it");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_LP1_PinnedWrong")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_LP1_PinnedWrong")]
    public void BothCompilerEntryPoints_LP1_PinnedWrong(bool asProject)
        => Assert.That(RunThroughEntryPoint(
            "Dim counter As Integer\n\n" +
            "Function NextVal() As Integer\n" +
            " counter = counter + 1\n" +
            " Console.WriteLine(\"next\")\n" +
            " Return counter\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " counter = 0\n" +
            " Dim n As Integer = 0\n" +
            " Do While 2 * NextVal() < 7\n" +
            "  n = n + 1\n" +
            " Loop\n" +
            " Console.WriteLine(n)\n" +
            "End Sub", asProject),
            Is.EqualTo("next\nnext\nnext\nnext\n1"),
            (asProject ? "CompileProjectFiles" : "CompileFile")
            + " — measured on this entry point too (\"C# cli-O/proj print 1 not 3\"); if this "
            + "changed, update or delete the pin, do not just widen it");

    /// <summary>
    /// ⛔ PINNED WRONG — T2: the user declares a local ALSO named <c>t0</c>, colliding with the
    /// IR's own temp-naming scheme (<c>t0</c>, <c>t1</c>, …), which is what the materialised
    /// call's temp is also named. <c>ComputeMaterialisedTemps</c>/<c>DeclareLocals</c> declare
    /// BOTH under the same identifier, so the materialised temp's assignment aliases the user's
    /// variable.
    ///
    /// <para>MEASURED, on all three entry points: <c>tag</c> prints THREE times (not once — E1 is
    /// violated, not merely a wrong final value), <c>r</c> is <c>6</c> (right, by construction of
    /// the doubling), and the user's own <c>t0</c> prints <c>3</c> instead of the <c>5</c> it was
    /// assigned — the user's variable and the compiler's temp are, after the collision, the SAME
    /// C# local.</para>
    ///
    /// <para>⭐ <b>Fixed by:</b> task #126, ADR-0004 D3 — <c>IRFunction.ReservedNames</c> populated
    /// from the AST before lowering, so <c>GetNextTempName()</c> never mints a name the function
    /// already declares. Not fixed here: D3 is a zero-churn reservation, out of this family's
    /// scope (materialisation + <c>IsReplicable</c> + the <c>2*x</c> gate).</para>
    /// </summary>
    [Test]
    public void T2_UserLocalNamedT0CollidesWithTheIRsTempNamespace_PinnedWrong_UnitHelperEntryPoint()
        => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharpAggressive(
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim t0 As Integer = 5\n" +
            " Dim r As Integer = 2 * Tag()\n" +
            " Console.WriteLine(r)\n" +
            " Console.WriteLine(t0)\n" +
            "End Sub")),
            Is.EqualTo("tag\ntag\ntag\n6\n3"),
            "if this changed (correct is tag once, then 6, then 5), task #126 / ADR-0004 D3's temp "
            + "reservation may be shipped — update or delete this pin, do not just widen it");

    [Test]
    [TestCase(false, TestName = "TheCliSingleFileEntryPoint_T2_PinnedWrong")]
    [TestCase(true, TestName = "TheProjectBuildEntryPoint_T2_PinnedWrong")]
    public void BothCompilerEntryPoints_T2_PinnedWrong(bool asProject)
        => Assert.That(RunThroughEntryPoint(
            "Function Tag() As Integer\n" +
            " Console.WriteLine(\"tag\")\n" +
            " Return 3\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim t0 As Integer = 5\n" +
            " Dim r As Integer = 2 * Tag()\n" +
            " Console.WriteLine(r)\n" +
            " Console.WriteLine(t0)\n" +
            "End Sub", asProject),
            Is.EqualTo("tag\ntag\ntag\n6\n3"),
            (asProject ? "CompileProjectFiles" : "CompileFile")
            + " — measured on this entry point too; if this changed, update or delete the pin, "
            + "do not just widen it");

    // ====================================================================================
    // Helpers.
    // ====================================================================================

    /// <summary>
    /// The CLI single-file (<c>CompileFile</c>) / project-build (<c>CompileProjectFiles</c>)
    /// entry points, exactly as <c>StatementOperandUndeclaredTempFixTests.BothCompilerEntryPoints_*</c>
    /// exercises them for C4 — <c>CompilerOptions.OptimizeAggressive = true</c> is what the CLI's
    /// <c>--optimize</c> and a Release <c>.blproj</c> build both request.
    /// </summary>
    private static string RunThroughEntryPoint(string source, bool asProject)
    {
        var dir = Path.Combine(Path.GetTempPath(),
            "BasicLang_Family111Entry_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "prog.bas");
            File.WriteAllText(file, source);

            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { OptimizeAggressive = true });
            var result = asProject ? compiler.CompileProjectFiles(new[] { file }) : compiler.CompileFile(file);

            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the entry point produced no combined IR");

            return FourBackends.Norm(FourBackends.RunEmittedCSharpText(
                new BasicLang.Compiler.CodeGen.CSharp.ImprovedCSharpCodeGenerator().Generate(result.CombinedIR)));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp */ } }
    }
}
