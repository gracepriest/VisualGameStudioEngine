using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>For Each</c> on MSIL — chip task_4cc381f1's continuation. Before this family's fix, EVERY
/// <c>For Each</c> on MSIL threw <c>InvalidProgramException</c>, from FOUR independent defects
/// stacked on one construct:
///
/// <para>⛔ <b>(A) the enumerator overwrote the collection's own slot.</b> The old emitter took
/// both the enumerator's and the loop variable's local index from <c>_localCounter++</c> — a
/// counter unrelated to <c>_localIndices</c>, the table every other local is allocated from — so
/// <c>stloc.s 0</c> for the enumerator landed on whatever slot 0 already meant, typically the
/// collection itself.</para>
///
/// <para>⛔ <b>(B) the loop variable had NO <c>.locals</c> slot at all.</b> <c>IRBuilder</c>
/// deliberately keeps it out of <c>IRFunction.LocalVariables</c> — correct for C#, C++ and
/// JavaScript, which each emit a real loop header that declares it — but that leaves MSIL with no
/// storage: every read emitted <c>// WARNING: Unknown local 'n'</c> and pushed nothing, so the
/// next instruction (an <c>add</c>, say) ran an operand short.</para>
///
/// <para>⛔ <b>(C) the body was emitted TWICE.</b> <c>ControlFlowGraph.Build</c> wires
/// <c>IRForEach.BodyBlock</c> in as a CFG successor of the block holding the instruction, and
/// <c>GenerateBasicBlock</c> walks successors — so the body ran once inlined by
/// <c>Visit(IRForEach)</c> and once again as an ordinary labelled block. The same class of defect
/// <c>Try</c>/<c>Catch</c> had (<c>docs/HANDOFF.md</c> ~line 219), same fix: a consumed-blocks set
/// consulted by both <see cref="object"/>-walking paths (<c>GenerateBasicBlock</c> AND
/// <c>EmitRegionBody</c>, whose block list is collected up front — a <c>For Each</c> inside a
/// <c>Try</c> needs both).</para>
///
/// <para>⛔ <b>(D) <c>Exit For</c> ran as <c>Continue For</c>.</b> <c>IRBuilder</c> gives a loop's
/// break and continue targets the SAME block (<c>LoopContext(endBlock, endBlock)</c>), and
/// <c>IRBranch.IsLoopExit</c> is the only thing that tells an <c>Exit For</c> branch apart from an
/// ordinary end-of-iteration branch to the identical target. C++ and JavaScript have read
/// <c>IsLoopExit</c> since task_4cc381f1; MSIL never did. Measured: a loop over 1,2,3,4 exiting at
/// 3 totalled <b>7</b> instead of <b>3</b> — from a program that ran clean, assembled, exited 0,
/// and printed a wrong answer with no warning anywhere.</para>
///
/// <para><b>The contract this fixture asserts:</b> for every shape, MSIL runs and prints what the
/// C# backend (compiled and run in-process) prints — <c>MsilAgreesWithCSharp</c>, the same pattern
/// <c>MsilClassTypeTests</c> uses. <b>Three shapes are the exception</b>, and all three are C#
/// COMPILE FAILURES, not wrong answers: <c>For Each</c> over a call's result (<c>CS0103</c>, an
/// undefined temp), a loop variable that shadows an outer local of the same name (<c>CS0136</c>),
/// and a property <c>Get</c> that declares any local at all (<c>CS0103</c>). Those assert MSIL
/// against the value C++ and JavaScript compute, and each names the measured C# divergence in its
/// own docstring rather than silently normalizing it away.</para>
///
/// <para>⚠ Two of those three descriptions are narrower than what was measured at a36262c, so do
/// not treat them as the predicate. <b>"Over a call's result" is not the boundary</b> — ANY
/// non-local collection operand fails the same way, including a plain field read
/// (<c>For Each n In b.Items</c>) and an indexer access; a local, a parameter and an array local
/// are all fine, and a call is not required. <b>"Shadows an outer local" is the right shape but
/// "collides" is not</b> — the name must collide with a local or parameter in the SAME emitted
/// method body (or an enclosing <c>For Each</c>'s variable); two sibling <c>For Each n</c> loops, a
/// module global, a class field and a counted <c>For i</c> all pass. And the property case is not
/// about loops at all — see <see cref="ForEachInAPropertyAccessorBody"/>. The governing rule for
/// the first of these is ADR-0001 in <c>docs/superpowers/decisions/</c>.</para>
///
/// <para>⭐ <b>The <c>Exit For</c> shapes used to be in that list and no longer are.</b> The C#
/// backend's own <c>Exit For</c> no-op was a separate, pre-existing C#-backend defect; it has since
/// been fixed, and those four cases now hold MSIL to C# like the rest. The C#-side contract for the
/// whole <c>Exit</c> family lives in <see cref="CSharpLoopExitTests"/>.</para>
///
/// <para>⚠ <b>Kept to ONE shape per test.</b> Both <c>MsilHarness.RunExpectingSuccess</c> and the
/// C# leg (<c>FourBackends.RunEmittedCSharp</c>) use <c>Assert.Multiple</c> internally — grouping
/// several shapes in one test mis-attributes one shape's failure onto another, which has bitten
/// two earlier sessions on this repo.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class MsilForEachTests
{
    /// <summary>Both .NET backends compile AND RUN the program, and agree with each other.</summary>
    private static void MsilAgreesWithCSharp(string program, string expected)
    {
        var cs = FourBackends.Norm(FourBackends.RunEmittedCSharp(program));
        var msil = FourBackends.Norm(MsilHarness.RunExpectingSuccess(program));
        Assert.Multiple(() =>
        {
            Assert.That(cs, Is.EqualTo(expected), "C# (the reference .NET backend)");
            Assert.That(msil, Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>
    /// MSIL only, against the value C++ and JavaScript both compute — for the one shape the C#
    /// backend cannot COMPILE, so it cannot be the oracle
    /// (<see cref="CollectionIsACallsResult"/>).
    /// </summary>
    private static void MsilMatchesCppAndJs(string program, string expected)
        => Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected));

    // ========================================================================================
    // RISKY EDGE CASES — measured by the implementer while characterizing the fix. Written
    // first, per the family's own risk assessment.
    // ========================================================================================

    [Test]
    public void EmptyCollection_LoopBodyNeverRuns()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  t = t + n\n" +
            " Next\n" +
            " PrintLine(\"t=\" & CStr(t))\n" +
            "End Sub",
            "t=0");

    /// <summary>outer {1,2,3} x inner {5,10}: (1+2+3)*(5+10) = 90.</summary>
    [Test]
    public void NestedForEach_BothLoopsRunToCompletion()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(1)\n" +
            " outer.Add(2)\n" +
            " outer.Add(3)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(5)\n" +
            " inner.Add(10)\n" +
            " Dim total As Integer = 0\n" +
            " For Each o In outer\n" +
            "  For Each i In inner\n" +
            "   total = total + o * i\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "90");

    /// <summary>
    /// The outer body has a statement AFTER the inner loop's <c>Next</c> — the assignment that
    /// actually appends to <c>s</c>. If the outer body did not continue past the inner loop (the
    /// same shape (C) misattributes as "body ends at the nested loop"), <c>s</c> stays empty and
    /// this prints nothing instead of <c>10:1;10:2;</c>.
    /// </summary>
    [Test]
    public void NestedForEach_OuterBodyContinuesAfterInnerLoop()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(10)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(1)\n" +
            " inner.Add(2)\n" +
            " Dim s As String = \"\"\n" +
            " For Each o In outer\n" +
            "  Dim line As String = \"\"\n" +
            "  For Each i In inner\n" +
            "   line = line & CStr(o) & \":\" & CStr(i) & \";\"\n" +
            "  Next\n" +
            "  s = s & line\n" +
            " Next\n" +
            " PrintLine(s)\n" +
            "End Sub",
            "10:1;10:2;");

    /// <summary>{1,2,2,3}, counting n=2: two matches.</summary>
    [Test]
    public void IfInTheBody_TakesTheTrueBranchOnMatchingElements()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim count As Integer = 0\n" +
            " For Each n In l\n" +
            "  If n = 2 Then\n" +
            "   count = count + 1\n" +
            "  End If\n" +
            " Next\n" +
            " PrintLine(CStr(count))\n" +
            "End Sub",
            "2");

    /// <summary>{2,3}: inner While runs n times each -> 2+3 = 5.</summary>
    [Test]
    public void WhileInTheBody_RunsPerElement()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  Dim i As Integer = 0\n" +
            "  While i < n\n" +
            "   total = total + 1\n" +
            "   i = i + 1\n" +
            "  End While\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "5");

    /// <summary>
    /// {2,3,4}: an ordinary classic-For loop nested in the body -> 2+3+4 = 9.
    ///
    /// <para>⚠ The induction variable is declared <c>As Integer</c> EXPLICITLY.
    /// <c>For i = 1 To n</c> (no <c>As</c>) turned out to be broken on MSIL independently of
    /// nesting or of this family — measured separately: it throws
    /// <c>InvalidProgramException</c> even completely ALONE at module scope
    /// (<c>// WARNING: Unknown local 'i'</c> in the emitted IL, the loop variable never gets a
    /// <c>.locals</c> slot at all — the same shape as defect (B) here, but for <c>IRFor</c>,
    /// never fixed). It also fails to COMPILE on the C# backend when nested inside another loop
    /// (any loop, not only <c>For Each</c> — reproduced nested in a bare <c>While</c> too):
    /// <c>CS0103 'i' does not exist</c>, the induction variable is never hoisted to method scope.
    /// Both are real, pre-existing, and entirely outside this family (a different IR node,
    /// <c>IRFor</c> not <c>IRForEach</c>) — not fixed or tested here, and not patched.</para>
    /// </summary>
    [Test]
    public void ClassicForInTheBody_RunsPerElement()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  For i As Integer = 1 To n\n" +
            "   total = total + 1\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "9");

    [Test]
    public void TryInTheBody_NoExceptionThrown()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  Try\n" +
            "   total = total + n\n" +
            "  Catch ex As Exception\n" +
            "   total = total + 100\n" +
            "  End Try\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "3");

    [Test]
    public void ForEachInsideATry_NoExceptionThrown()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(4)\n" +
            " l.Add(5)\n" +
            " Dim total As Integer = 0\n" +
            " Try\n" +
            "  For Each n In l\n" +
            "   total = total + n\n" +
            "  Next\n" +
            " Catch ex As Exception\n" +
            "  total = -1\n" +
            " End Try\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "9");

    /// <summary>{4,5,100}: returns as soon as the running total reaches 9, so 100 is never added.</summary>
    [Test]
    public void ReturnFromInsideTheBody_ExitsTheFunction()
        => MsilAgreesWithCSharp(
            "Function Sum(l As List(Of Integer)) As Integer\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  total = total + n\n" +
            "  If total >= 9 Then\n" +
            "   Return total\n" +
            "  End If\n" +
            " Next\n" +
            " Return -1\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(4)\n" +
            " l.Add(5)\n" +
            " l.Add(100)\n" +
            " PrintLine(CStr(Sum(l)))\n" +
            "End Sub",
            "9");

    /// <summary>Two independent loops over two different lists in the same method: 1+2=3, 10+20=30.</summary>
    [Test]
    public void TwoLoopsInOneMethod_EachGetsItsOwnState()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim a As New List(Of Integer)()\n" +
            " a.Add(1)\n" +
            " a.Add(2)\n" +
            " Dim sum1 As Integer = 0\n" +
            " For Each x In a\n" +
            "  sum1 = sum1 + x\n" +
            " Next\n" +
            " Dim b As New List(Of Integer)()\n" +
            " b.Add(10)\n" +
            " b.Add(20)\n" +
            " Dim sum2 As Integer = 0\n" +
            " For Each y In b\n" +
            "  sum2 = sum2 + y\n" +
            " Next\n" +
            " PrintLine(CStr(sum1) & \",\" & CStr(sum2))\n" +
            "End Sub",
            "3,30");

    /// <summary>
    /// Two loops SHARE the variable name <c>n</c> and the SAME element type (Integer). One slot
    /// for both would still work for correctness by accident here since the type agrees — the
    /// real hazard is the next test, different element types under the same name.
    /// </summary>
    [Test]
    public void TwoLoopsSharingAVariableNameAndType_EachGetsItsOwnSlot()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l1 As New List(Of Integer)()\n" +
            " l1.Add(2)\n" +
            " l1.Add(3)\n" +
            " Dim total1 As Integer = 0\n" +
            " For Each n In l1\n" +
            "  total1 = total1 + n\n" +
            " Next\n" +
            " PrintLine(CStr(total1))\n" +
            " Dim l2 As New List(Of Integer)()\n" +
            " l2.Add(3)\n" +
            " l2.Add(4)\n" +
            " Dim total2 As Integer = 0\n" +
            " For Each n In l2\n" +
            "  total2 = total2 + n\n" +
            " Next\n" +
            " PrintLine(CStr(total2))\n" +
            "End Sub",
            "5\n7");

    /// <summary>
    /// ⛔ THE case one slot per NAME (rather than per INSTRUCTION) gets wrong: <c>w</c> is a
    /// String in the second loop, <c>n</c> is an Integer in the first — one shared slot would
    /// declare one IL type and try to store the other kind of value into it.
    /// </summary>
    [Test]
    public void TwoLoopsDifferentNames_IntegerThenString()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim ints As New List(Of Integer)()\n" +
            " ints.Add(2)\n" +
            " ints.Add(3)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In ints\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            " Dim words As New List(Of String)()\n" +
            " words.Add(\"h\")\n" +
            " words.Add(\"i\")\n" +
            " Dim s As String = \"\"\n" +
            " For Each w In words\n" +
            "  s = s & w\n" +
            " Next\n" +
            " PrintLine(s)\n" +
            "End Sub",
            "5\nhi");

    /// <summary>
    /// The loop variable is WRITTEN inside the body. Observable only if the write lands on the
    /// loop variable's own slot rather than being silently dropped or landing elsewhere.
    /// </summary>
    [Test]
    public void LoopVariableModifiedInTheBody_WriteIsObservable()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  n = n * 10\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "30");

    [Test]
    public void ForEachInsideAClassInstanceMethod()
        => MsilAgreesWithCSharp(
            "Class Box\n" +
            " Public Function Total(l As List(Of Integer)) As Integer\n" +
            "  Dim total As Integer = 0\n" +
            "  For Each n In l\n" +
            "   total = total + n\n" +
            "  Next\n" +
            "  Return total\n" +
            " End Function\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(4)\n" +
            " l.Add(6)\n" +
            " Dim b As New Box()\n" +
            " PrintLine(CStr(b.Total(l)))\n" +
            "End Sub",
            "10");

    [Test]
    public void ForEachInAModuleSub_ThatIsNotMain()
        => MsilAgreesWithCSharp(
            "Module M\n" +
            " Sub Helper()\n" +
            "  Dim l As New List(Of Integer)()\n" +
            "  l.Add(3)\n" +
            "  l.Add(4)\n" +
            "  Dim total As Integer = 0\n" +
            "  For Each n In l\n" +
            "   total = total + n\n" +
            "  Next\n" +
            "  PrintLine(CStr(total))\n" +
            " End Sub\n" +
            " Sub Main()\n" +
            "  Helper()\n" +
            " End Sub\n" +
            "End Module",
            "7");

    [Test]
    public void EmptyBody_LoopStillRunsAndCompletes()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            " Next\n" +
            " PrintLine(\"ok\")\n" +
            "End Sub",
            "ok");

    [Test]
    public void AStatementAfterTheLoop_StillRuns()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            " PrintLine(\"1\")\n" +
            "End Sub",
            "7\n1");

    // ========================================================================================
    // CONTRACT ITEM 1 — every collection shape runs and gives .NET's answer.
    // ========================================================================================

    [Test]
    public void OverListOfInteger()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "10");

    [Test]
    public void OverListOfString()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of String)()\n" +
            " l.Add(\"a\")\n" +
            " l.Add(\"b\")\n" +
            " l.Add(\"c\")\n" +
            " Dim s As String = \"\"\n" +
            " For Each w In l\n" +
            "  s = s & w\n" +
            " Next\n" +
            " PrintLine(s)\n" +
            "End Sub",
            "abc");

    [Test]
    public void OverListOfAUserClass()
        => MsilAgreesWithCSharp(
            "Class Item\n" +
            " Public Value As Integer\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Item)()\n" +
            " Dim a As New Item()\n" +
            " a.Value = 3\n" +
            " l.Add(a)\n" +
            " Dim b As New Item()\n" +
            " b.Value = 4\n" +
            " l.Add(b)\n" +
            " Dim total As Integer = 0\n" +
            " For Each it In l\n" +
            "  total = total + it.Value\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "7");

    /// <summary>1.5 + 2.25 = 3.75 — deliberately not a whole number, so a formatting slip cannot
    /// hide behind ToString() rendering "4.0" as "4" on one side and not the other.</summary>
    [Test]
    public void OverListOfDouble()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Double)()\n" +
            " l.Add(1.5)\n" +
            " l.Add(2.25)\n" +
            " Dim total As Double = 0\n" +
            " For Each n In l\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "3.75");

    [Test]
    public void OverAnIntegerArray()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim a() As Integer = {5, 6, 7}\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In a\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "18");

    [Test]
    public void OverAStringArray()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim a() As String = {\"x\", \"y\"}\n" +
            " Dim s As String = \"\"\n" +
            " For Each w In a\n" +
            "  s = s & w\n" +
            " Next\n" +
            " PrintLine(s)\n" +
            "End Sub",
            "xy");

    /// <summary>
    /// A <c>String</c> iterated by its CHARACTERS — the loop variable declared <c>As Char</c> so
    /// the element type is really Char (not the Object a bare <c>For Each c In s</c> would infer
    /// here, since String is neither an Array nor carries GenericArguments). Counting iterations
    /// rather than printing the character avoids the STDLIB's own (unrelated) box-typing arm for
    /// non-primitive <c>PrintLine</c> arguments, which is not this family's concern.
    /// </summary>
    [Test]
    public void OverAStringsCharacters()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For Each c As Char In \"abc\"\n" +
            "  total = total + 1\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "3");

    // ========================================================================================
    // CONTRACT ITEM 2 — the loop variable's OWN slot, distinct from the collection's and the
    // enumerator's.
    // ========================================================================================

    /// <summary>
    /// ⛔ Reads <c>l.Count</c> from INSIDE the body, on every iteration. Defect (A) — the
    /// enumerator's <c>stloc.s 0</c> overwriting the COLLECTION's own slot — would corrupt or
    /// crash exactly this: the list is read again after the enumerator supposedly clobbered it.
    /// 1+3=4, 2+3=5, 3+3=6 -> 15.
    /// </summary>
    [Test]
    public void LoopVariable_IsDistinctFromTheCollectionsOwnSlot()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  total = total + n + l.Count\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "15");

    /// <summary>
    /// ⛔ IL TEXT, not a run — this is the ONE property in this family a round trip cannot see.
    /// <c>AllocateForEachLocals</c> must type the loop variable's <c>.locals</c> slot from
    /// <c>IRForEach.ElementType</c> (<c>int32</c> for a <c>List(Of Integer)</c>, <c>string</c> for
    /// a <c>List(Of String)</c>) rather than from the COLLECTION's type. Typing it from the
    /// collection instead still ASSEMBLES and still PRINTS THE RIGHT ANSWER — measured: it emits
    ///
    /// <code>
    /// [2] class [mscorlib]...List`1&lt;int32&gt; 'n',   // reference-typed, should be int32
    /// ...
    /// unbox.any [mscorlib]System.Int32
    /// stloc.2                                          // an int32 value stored into that slot
    /// </code>
    ///
    /// and .NET Core does not verify IL for fully-trusted code, so a value stored into a
    /// mistyped reference slot has no observable effect in a short-lived process — no output a
    /// test asserts on can distinguish this from the correct emission. ⛔ It is not cosmetic: a
    /// slot declared as a reference type is a GC ROOT. The collector traces it as an object
    /// pointer while it actually holds a raw <c>int32</c> bit pattern — undefined behaviour at
    /// the next collection, latent rather than absent. This backend already has three other
    /// run-time-invisible properties for the same reason (<c>docs/HANDOFF.md</c>: the
    /// <c>Select Case</c> default branch, the variable-less <c>Catch</c>'s <c>pop</c>, the wrong
    /// overload), and the established remedy for all of them is an IL-text assertion, not a
    /// run.
    /// </summary>
    [Test]
    public void LoopVariable_LocalsSlot_IsTypedFromTheElementType_NotTheCollection()
    {
        var integerIl = MsilHarness.CompileToIl(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            "  PrintLine(CStr(n))\n" +
            " Next\n" +
            "End Sub");

        var stringIl = MsilHarness.CompileToIl(
            "Sub Main()\n" +
            " Dim l As New List(Of String)()\n" +
            " l.Add(\"a\")\n" +
            " For Each w In l\n" +
            "  PrintLine(w)\n" +
            " Next\n" +
            "End Sub");

        Assert.Multiple(() =>
        {
            Assert.That(integerIl, Does.Contain("int32 'n',"),
                "the loop variable over a List(Of Integer) must declare an int32 slot: " + integerIl);
            Assert.That(integerIl, Does.Not.Contain("List`1<int32> 'n'"),
                "the loop variable must not be typed from the COLLECTION's own generic instantiation: "
                + integerIl);

            Assert.That(stringIl, Does.Contain("string 'w',"),
                "the loop variable over a List(Of String) must declare a string slot: " + stringIl);
            Assert.That(stringIl, Does.Not.Contain("List`1<string> 'w'"),
                "the loop variable must not be typed from the COLLECTION's own generic instantiation: "
                + stringIl);
        });
    }

    // ========================================================================================
    // CONTRACT ITEM 3 — the body is emitted EXACTLY ONCE. A doubled body would double these
    // sums; each is designed so a duplicate execution is arithmetically visible, not just a
    // possible crash.
    // ========================================================================================

    [Test]
    public void BodyRunsExactlyOnce_AtModuleScope()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim callCount As Integer = 0\n" +
            " For Each n In l\n" +
            "  callCount = callCount + 1\n" +
            " Next\n" +
            " PrintLine(CStr(callCount))\n" +
            "End Sub",
            "2");

    [Test]
    public void BodyRunsExactlyOnce_InsideAClassMethod()
        => MsilAgreesWithCSharp(
            "Class Counter\n" +
            " Public Function Count(l As List(Of Integer)) As Integer\n" +
            "  Dim callCount As Integer = 0\n" +
            "  For Each n In l\n" +
            "   callCount = callCount + 1\n" +
            "  Next\n" +
            "  Return callCount\n" +
            " End Function\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim c As New Counter()\n" +
            " PrintLine(CStr(c.Count(l)))\n" +
            "End Sub",
            "3");

    [Test]
    public void BodyRunsExactlyOnce_InsideATry()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim callCount As Integer = 0\n" +
            " Try\n" +
            "  For Each n In l\n" +
            "   callCount = callCount + 1\n" +
            "  Next\n" +
            " Catch ex As Exception\n" +
            "  callCount = -1\n" +
            " End Try\n" +
            " PrintLine(CStr(callCount))\n" +
            "End Sub",
            "2");

    // ========================================================================================
    // CONTRACT ITEM 4 — end-of-iteration goes to the loop HEAD; Exit For goes to the
    // CONTINUATION. The first case here is the "no exit" control: an If whose merge block also
    // branches to the loop's end block must stay an ordinary iteration, not be mistaken for an
    // exit.
    //
    // ⭐ The four Exit For cases below USED TO assert against C++ and JavaScript, because the C#
    // backend had an independent defect of its own that made Exit For a no-op there. That defect
    // is fixed (CSharpLoopExitTests owns its contract), and these are now ordinary
    // MsilAgreesWithCSharp cases. Each one's docstring still records what C# measured at f20435d,
    // because those numbers are what make the shapes discriminating.
    // ========================================================================================

    /// <summary>
    /// ⛔ THE case that breaks a positional guess at loop-exit: the <c>If</c>'s merge block
    /// branches to the SAME block an <c>Exit For</c> would, and that branch is an ordinary
    /// end-of-iteration, not an exit. 1, then "two"+2, then 3 -> "1two23".
    /// </summary>
    [Test]
    public void IfWithNoExitFor_StillIteratesEveryElement()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " Dim s As String = \"\"\n" +
            " For Each n In l\n" +
            "  If n = 2 Then\n" +
            "   s = s & \"two\"\n" +
            "  End If\n" +
            "  s = s & CStr(n)\n" +
            " Next\n" +
            " PrintLine(s)\n" +
            "End Sub",
            "1two23");

    /// <summary>
    /// ⛔ THE measured repro, reproduced here. Correct: 1+2=3 (Exit For at n=3 skips both the add
    /// for 3 AND element 4). ⛔ C# MEASURED AT f20435d: 10 — <c>Exit For</c> was a NO-OP on the C#
    /// backend, a separate, pre-existing C#-backend defect. ⭐ That is fixed; C# now prints 3
    /// (re-measured), so this holds both .NET backends to the same answer.
    /// </summary>
    [Test]
    public void ExitFor_LeavesTheLoop()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  If n = 3 Then\n" +
            "   Exit For\n" +
            "  End If\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// ⛔ NESTED: an inner <c>Exit For</c> must leave only the INNER loop, not the outer. Inner
    /// {5,10,15,20} exits at 15; outer runs 4 times.
    ///
    /// <para>⛔ <b>The element AFTER the exit point (20) is load-bearing, not decoration.</b> A
    /// first version of this test used inner {5,10,15} — but under the "Exit For runs as
    /// Continue For" mutant this defect (D) undoes, <c>Continue For</c> skips only the CURRENT
    /// element's remaining statements (here, just the <c>total = total + n</c> that would add
    /// 15) and the loop still proceeds normally to whatever comes after. With no element after
    /// 15, "skip 15, nothing follows" and "break before 15" are IDENTICAL — both give 5+10=15
    /// per outer pass, 60 total either way, and the mutant survived: it changed behaviour with no
    /// program here able to observe it. The 4th element (20) makes them diverge: correct (break)
    /// still stops at 5+10=15 per pass -> 60 total; "Exit For as Continue" skips only 15 and
    /// still adds 20 -> 5+10+20=35 per pass -> 140 total (the discriminating mutant's actual
    /// measured value).</para>
    ///
    /// <para>⛔ C# MEASURED AT f20435d: 200 — a DIFFERENT wrong number from a DIFFERENT cause.
    /// C#'s <c>Exit For</c> was a complete no-op (not even a same-element skip), so it added every
    /// element every pass: 5+10+15+20=50 per pass * 4 = 200. ⭐ That C#-backend defect is fixed;
    /// C# now prints 60 (re-measured), so C# is the oracle here like everywhere else.</para>
    ///
    /// <para>If the OUTER loop were wrongly exited too, this would total 15 (one outer pass
    /// only) instead of 60 — this case also proves outer continues, independent of the
    /// discrimination fix above.</para>
    /// </summary>
    [Test]
    public void ExitFor_InANestedForEach_LeavesOnlyTheInnerLoop()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(1)\n" +
            " outer.Add(2)\n" +
            " outer.Add(3)\n" +
            " outer.Add(4)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(5)\n" +
            " inner.Add(10)\n" +
            " inner.Add(15)\n" +
            " inner.Add(20)\n" +
            " Dim total As Integer = 0\n" +
            " For Each o In outer\n" +
            "  For Each n In inner\n" +
            "   If n = 15 Then\n" +
            "    Exit For\n" +
            "   End If\n" +
            "   total = total + n\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "60");

    /// <summary>
    /// ⛔ <c>Exit For</c> INSIDE a <c>Try</c> — combines defect (D)'s fix with the region-aware
    /// branch machinery (C) needs. Correct: 3, same shape as the unwrapped case. ⛔ C# MEASURED AT
    /// f20435d: 10, same cause (Exit For a no-op there). ⭐ Fixed; C# now prints 3 (re-measured).
    /// </summary>
    [Test]
    public void ExitFor_InsideATry_LeavesTheLoop()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Dim total As Integer = 0\n" +
            " Try\n" +
            "  For Each n In l\n" +
            "   If n = 3 Then\n" +
            "    Exit For\n" +
            "   End If\n" +
            "   total = total + n\n" +
            "  Next\n" +
            " Catch ex As Exception\n" +
            "  total = -1\n" +
            " End Try\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// ⛔ <c>Exit For</c> as the LAST statement in the body — no <c>If</c>/merge block, just an
    /// unconditional exit after one increment. Correct: 1 (one iteration, then leaves). ⛔ C#
    /// MEASURED AT f20435d: 2 — the no-op <c>Exit For</c> fell through and the loop ran BOTH
    /// elements (1, then 2). ⭐ Fixed; C# now prints 1 (re-measured).
    /// </summary>
    [Test]
    public void ExitFor_AsTheLastStatement_LeavesAfterOneIteration()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In l\n" +
            "  total = total + 1\n" +
            "  Exit For\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "1");

    /// <summary>
    /// ⛔ The collection is a CALL'S RESULT, not a local — a distinct emission path for
    /// <c>forEach.Collection</c> (a temporary rather than a named variable). Correct (MSIL): 7
    /// (3+4). ⛔ C# MEASURED: does not compile at all — CS0103, an undefined temp <c>t0</c> — so
    /// this case cannot run the <c>MsilAgreesWithCSharp</c>/<c>MsilMatchesCppAndJs</c> shared
    /// helper at all; the C# leg is not invoked here.
    /// </summary>
    [Test]
    public void CollectionIsACallsResult()
        => MsilMatchesCppAndJs(
            "Function Make() As List(Of Integer)\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(3)\n" +
            " l.Add(4)\n" +
            " Return l\n" +
            "End Function\n\n" +
            "Sub Main()\n" +
            " Dim total As Integer = 0\n" +
            " For Each n In Make()\n" +
            "  total = total + n\n" +
            " Next\n" +
            " PrintLine(CStr(total))\n" +
            "End Sub",
            "7");

    // ========================================================================================
    // CONTRACT ITEM 5 — the loop variable's name is bound to its slot for the body only; two
    // loops sharing a name each get their own slot (already covered above); and the name resolves
    // back to whatever it meant BEFORE the loop once the loop ends.
    // ========================================================================================

    /// <summary>
    /// <c>n</c> is declared OUTSIDE the loop (99) and then reused as the loop variable. The front
    /// end scopes a <c>For Each</c> variable to a fresh, SHADOWING symbol (see
    /// <c>SemanticAnalyzer.Visit(ForEachLoopNode)</c>'s <c>EnterScope</c>/<c>ExitScope</c>) rather
    /// than reusing the outer one, so after the loop <c>n</c> must read back 99 — untouched by
    /// anything the loop wrote to its own (different) slot. Backend-observable because MSIL's
    /// <c>_localIndices["n"]</c> rebinding in <c>EmitForEachBody</c> is saved and restored around
    /// the body rather than assigned outright.
    ///
    /// <para>⚠ Asserted against JavaScript, not C#: measured, the C# backend does not COMPILE
    /// this shape at all — it emits <c>foreach (int n in l)</c> literally inside the same method
    /// scope as the pre-existing <c>int n</c>, and C# itself refuses that
    /// (<c>CS0136: a local ... named 'n' cannot be declared in this scope</c>). That is a
    /// separate, pre-existing C#-backend defect (the emitted <c>foreach</c> reuses the SOURCE
    /// name verbatim rather than a scope-safe one), not this family's, and not fixed or asserted
    /// against here.</para>
    /// </summary>
    [Test]
    public void LoopVariableName_ResolvesBackToItsOuterMeaning_AfterTheLoop()
    {
        const string program =
            "Sub Main()\n" +
            " Dim n As Integer = 99\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " l.Add(3)\n" +
            " For Each n In l\n" +
            " Next\n" +
            " PrintLine(CStr(n))\n" +
            "End Sub";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo("99"), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("99"), "MSIL");
        });
    }

    /// <summary>
    /// ⛔ THE test above does not discriminate a binding that is never withdrawn. It shadows a
    /// real LOCAL named <c>n</c> — <c>EmitForEachBody</c>'s restore takes the <c>hadName</c>
    /// (<c>if</c>) arm there, which is exercised regardless of whether the withdrawal on the
    /// <c>else</c> arm (no PRIOR binding — <c>_localIndices.Remove(forEach.VariableName)</c>)
    /// actually runs. The discriminating shape needs a name with NO local meaning but an OUTER
    /// one: a MODULE-LEVEL variable the loop shadows, so the read after the loop can only be
    /// satisfied by <c>EmitLoadValue</c> falling through <c>_localIndices</c> (empty for
    /// <c>n</c>) to <c>_moduleGlobals</c> — which happens only if the loop's binding was
    /// genuinely removed, not merely restored to a prior local index.
    ///
    /// <para>⛔ Measured: with the binding never withdrawn, the stale loop slot SHADOWS the
    /// module global for the rest of the method — <c>ldloc.1</c> instead of
    /// <c>ldsfld int32 'MsilProbe'::'n'</c> — a clean run with a wrong answer (2, the loop's last
    /// element, instead of 7).</para>
    ///
    /// <para>C# was checked and agrees (7) — no separate C#-backend defect on this shape, so this
    /// uses the ordinary <c>MsilAgreesWithCSharp</c> pattern rather than falling back to
    /// JavaScript the way the case above has to.</para>
    /// </summary>
    [Test]
    public void LoopVariableBinding_IsWithdrawn_SoAModuleGlobalOfTheSameNameIsVisibleAfterTheLoop()
        => MsilAgreesWithCSharp(
            "Dim n As Integer = 7\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n" +
            " l.Add(2)\n" +
            " For Each n In l\n" +
            " Next\n" +
            " PrintLine(CStr(n))\n" +
            "End Sub",
            "7");

    // ========================================================================================
    // CONTRACT ITEM 6 — statements after the loop still run (also see
    // AStatementAfterTheLoop_StillRuns above, and Cpp_ForEach_TrailingStatementsAfterLoop in
    // CppCollectionTests for the sibling regression this mirrors on another backend).
    // ========================================================================================

    [Test]
    public void StatementsAfterTheLoop_SurviveEvenAfterAnEmptyCollection()
        => MsilAgreesWithCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " For Each n In l\n" +
            "  PrintLine(\"unreachable\")\n" +
            " Next\n" +
            " PrintLine(\"after\")\n" +
            "End Sub",
            "after");

    // ========================================================================================
    // ENTRY POINTS — the implementer flagged a constructor body and a property accessor body as
    // cheap, uncovered insurance: both share InitializeMethodContext with an ordinary instance
    // method, but neither is exercised by the cases above.
    // ========================================================================================

    /// <summary>
    /// ⚠ The accumulator is named <c>sum</c>, not <c>total</c> — the language is
    /// case-INSENSITIVE, and a local named <c>total</c> here collides with the field
    /// <c>Total</c> (same identifier, different case). Measured: with that name both C# AND
    /// MSIL silently print 0 instead of 7 — a shared, front-end-level naming hazard, not a
    /// backend defect, and not this family's; avoided here rather than exercised.
    /// </summary>
    [Test]
    public void ForEachInAConstructorBody()
        => MsilAgreesWithCSharp(
            "Class Box\n" +
            " Public Total As Integer\n" +
            " Public Sub New(l As List(Of Integer))\n" +
            "  Dim sum As Integer = 0\n" +
            "  For Each n In l\n" +
            "   sum = sum + n\n" +
            "  Next\n" +
            "  Total = sum\n" +
            " End Sub\n" +
            "End Class\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(2)\n" +
            " l.Add(5)\n" +
            " Dim b As New Box(l)\n" +
            " PrintLine(CStr(b.Total))\n" +
            "End Sub",
            "7");

    /// <summary>
    /// ⚠ Seeded through a <c>Sub</c>, not a field initializer — a field initializer is never
    /// emitted on MSIL (see <c>MsilPropertyTests.AComputedGetter_IsCalled_NotReadAsAField</c>'s
    /// note) and would make this read empty for an unrelated reason that has nothing to do with
    /// <c>For Each</c>. ⚠ And the accumulator is <c>sum</c>, not <c>total</c>, for the same
    /// case-insensitive naming hazard against the property <c>Total</c> noted on the constructor
    /// case above.
    ///
    /// <para>⚠ Asserted against JavaScript, not C#: measured, the C# backend does not compile this
    /// program — <c>CS0103: The name 'sum' does not exist in the current context</c>.
    /// ⛔ <b>The loop is not what breaks it.</b> An earlier version of this note blamed locals
    /// "declared inside a loop"; that is wrong, and the correction is measured:
    /// <c>GenerateProperty</c> emits NO local declarations AT ALL, so a property <c>Get</c> whose
    /// entire body is <c>Dim sum As Integer = 5</c> / <c>Return sum + 1</c> — no loop anywhere — is
    /// the same <c>CS0103</c>, while a <c>Get</c> that declares nothing (<c>Return 6</c>) compiles
    /// and prints 6. A separate, pre-existing, general C#-backend gap, unchanged by the
    /// C#-backend batch that fixed <c>Exit For</c>, and not fixed here. MSIL alone (verified
    /// directly, outside this helper) already prints the correct 7 for this exact program.</para>
    /// </summary>
    [Test]
    public void ForEachInAPropertyAccessorBody()
    {
        const string program =
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
            " PrintLine(CStr(b.Total))\n" +
            "End Sub";
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo("7"), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo("7"), "MSIL");
        });
    }
}
