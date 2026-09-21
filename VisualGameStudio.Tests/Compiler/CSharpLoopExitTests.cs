using System.Text.RegularExpressions;
using NUnit.Framework;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Exit For</c> / <c>Exit While</c> / <c>Exit Do</c> / <c>Exit Sub</c> on the C# backend.
///
/// <para>⛔ <b>What was wrong.</b> Five independent defects in <c>CSharpBackend.cs</c>, every one of
/// them a program that compiled, ran, exited 0 and printed a wrong answer (or did not stop):</para>
///
/// <para>⛔ <b>(1) <c>Exit For</c> in a <c>For Each</c> emitted NOTHING.</b> <c>IRBuilder</c> gives a
/// loop's break and continue targets the SAME block, so <c>IRBranch.IsLoopExit</c> is the only thing
/// that tells an <c>Exit For</c> apart from an ordinary end-of-iteration branch to the identical
/// target. C++, JavaScript and MSIL all read it; C# was the one backend that did not, and
/// <c>Visit(IRForEach)</c> never registered its end block as a loop end at all. Measured: a loop over
/// 1,2,3,4 exiting at 3 totalled <b>10</b>.</para>
///
/// <para>⛔ <b>(2) <c>break</c> inside a C# <c>switch</c> leaves the SWITCH, not the loop.</b> An
/// <c>Exit For</c> in a <c>Select Case</c> arm therefore fell out of the <c>Select Case</c> and the
/// loop carried on. Measured on the pre-existing counted-<c>For</c> path: <b>7</b> instead of 3. The
/// fix emits <c>goto __exit_&lt;block&gt;_end;</c> plus a label after the loop, and emits the LABEL
/// only when a <c>goto</c> was actually written (an unreferenced label is CS0164).</para>
///
/// <para>⛔ <b>(3) a <c>Select Case</c> that was the WHOLE body of a <c>For Each</c> was DROPPED.</b>
/// <c>Visit(IRForEach)</c>'s terminator dispatch handled <c>IRConditionalBranch</c> and
/// <c>IRBranch</c> but not <c>IRSwitch</c>, and <c>EmitBlockInstructions</c> skips every
/// <c>IRSwitch</c> because control flow is emitted structurally. Measured: <b>0</b> where every other
/// backend printed 104.</para>
///
/// <para>⛔ <b>(4) a counted <c>For</c> whose body ended in <c>Exit For</c> LOOPED FOREVER.</b> With
/// the exit dropped by (1) and the <c>.inc</c> block unreachable (so the optimizer deletes it), the
/// emitted C# was <c>while (i &lt;= 4) { t = t + 1; }</c>. It is the <c>break</c> from (1) that stops
/// it — see <see cref="CountedFor_WhoseBodyEndsInExitFor_EmitsTheBreakThatStopsIt"/>.</para>
///
/// <para>⛔ <b>(5) <c>Exit Sub</c> was a complete no-op.</b> <c>Visit(IRReturn)</c> suppressed a void
/// return when <c>ParentBlock.Successors.Count == 0</c> — but a return TERMINATES its block, so that
/// is true of EVERY void return, early or not. Measured: a <c>Sub</c> that prints "5" and exits, then
/// prints "-1", printed BOTH.</para>
///
/// <para><b>The contract this fixture asserts:</b> for every shape, the C# backend (emitted, compiled
/// and run in process) prints what C++, JavaScript and MSIL print — <c>FourBackends</c>. Where a
/// shape is asserted against FEWER than four backends, the excluded backend and the MEASURED reason
/// are named in that case's own docstring; those exclusions are pre-existing gaps on the OTHER
/// backend, not this family's, and are never normalized away silently.</para>
///
/// <para>⭐ <b>The no-exit controls are load-bearing.</b>
/// <see cref="AnIfWithNoExitFor_StillIteratesEveryElement"/> is what separates "read
/// <c>IsLoopExit</c>" from "every branch to the loop end is a break": measured, the latter turns that
/// program's 1two23 into <b>1</b>. Without it the whole family passes under a mutant that breaks out
/// of every ordinary iteration.</para>
///
/// <para>⛔ <b>Three shapes deliberately do NOT run the C# leg.</b> The C# leg is in-process Roslyn
/// with NO timeout, so a program that loops forever hangs the test host instead of failing. For a
/// counted <c>For</c> whose body ends in <c>Exit For</c> (and for <c>Exit Sub</c> in the same
/// position) a regression puts the emitted C# back into an infinite loop — measured, at f20435d and
/// under the mutants for this family. Those pin the C# side on the emitted TEXT instead, and run on
/// JavaScript / C++ / MSIL.</para>
///
/// <para>⚠ <b>Kept to ONE shape per test.</b> <c>FourBackends</c> and <c>MsilHarness</c> both use
/// <c>Assert.Multiple</c> internally, so grouping shapes mis-attributes one shape's failure onto
/// another. (Measured the hard way while characterizing this family: a probe that compiled five
/// programs into one C++ temp directory reported one shape's compile error for all five.)</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CSharpLoopExitTests
{
    /// <summary>All four backends compile AND RUN the program, and agree.</summary>
    private static void EveryBackend(string program, string expected)
        => FourBackends.RunsOnEveryBackend(program, expected);

    /// <summary>The three backends that are not C++ — for shapes C++ cannot compile.</summary>
    private static void EveryBackendExceptCpp(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
    }

    /// <summary>The three backends that are not MSIL — for shapes MSIL cannot run.</summary>
    private static void EveryBackendExceptMsil(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
    }

    /// <summary>JavaScript and C# only — for the one shape C++ and MSIL both get wrong.</summary>
    private static void JavaScriptAndCSharp(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C#");
        });
    }

    /// <summary>
    /// ⛔ The three backends whose harness can TIME OUT — for the shapes where a regression makes the
    /// emitted C# loop forever. The C# side of those shapes is pinned on the emitted text instead.
    /// </summary>
    private static void WithoutTheInProcessCSharpRun(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo(expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(program)), Is.EqualTo(expected), "MSIL");
        });
    }

    /// <summary>The emitted C#, through the optimizer, exactly as the CLI and the IDE build emit it.</summary>
    private static string EmittedCSharp(string program) => ReturnCoercionTests.EmitCSharpForTest(program);

    /// <summary>Every run of whitespace collapsed to one space, so an assertion can quote a statement
    /// sequence without depending on the emitter's indentation.</summary>
    private static string Flat(string csharp) => Regex.Replace(csharp, @"\s+", " ");

    // ============================================================================================
    // CONTRACT 1 — `Exit For` in a `For Each` leaves THAT loop, from every syntactic position.
    // Each case below owns a DIFFERENT place the emitter asks "is this branch an Exit For?", so a
    // failure names the position rather than just the construct.
    // ============================================================================================

    /// <summary>
    /// The plain repro. 1+2=3 — the exit at n=3 skips both the add for 3 and element 4. ⛔ The 4th
    /// element is load-bearing: with {1,2,3} alone, "break before 3" and "skip 3 and carry on" both
    /// total 3 and the shape cannot tell them apart. ⛔ MEASURED at f20435d: 10.
    /// </summary>
    [Test]
    public void ExitFor_InAnIfThen_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  If n = 3 Then\n" +
            "   Exit For\n" +
            "  End If\n" +
            "  t = t + n\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// The THEN arm of an <c>If</c>/<c>Else</c> — a different emitter path from the bare
    /// <c>If</c> above. ⛔ <b>1..4, not 1..3</b>: over {1,2,3} this program totals 3 whether the exit
    /// works or not, because the only elements that reach the <c>Else</c> are 1 and 2 either way.
    /// Over {1,2,3,4} a no-op exit adds 4 as well and totals 7. ⛔ MEASURED at f20435d: 7.
    /// </summary>
    [Test]
    public void ExitFor_InTheThenArmOfAnIfElse_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  If n = 3 Then\n" +
            "   Exit For\n" +
            "  Else\n" +
            "   t = t + n\n" +
            "  End If\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// The ELSE arm, which the emitter handles at its own site. Same 1..4 requirement as the THEN
    /// arm above, for the same reason. ⛔ MEASURED at f20435d: 7.
    /// </summary>
    [Test]
    public void ExitFor_InTheElseArmOfAnIfElse_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  If n <> 3 Then\n" +
            "   t = t + n\n" +
            "  Else\n" +
            "   Exit For\n" +
            "  End If\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// ⭐ An <c>Exit For</c> inside a <c>Case</c> arm — the shape defect (2) is about, because a C#
    /// <c>break</c> here leaves the SWITCH. ⛔ <b>1..4 again, and for a sharper reason:</b> over
    /// {1,2,3} a correct <c>goto</c> and a switch-only <c>break</c> BOTH total 3, because there is
    /// nothing after 3 left to add. Over {1,2,3,4} the switch-only <c>break</c> falls back into the
    /// loop and adds 4, totalling 7 — measured. ⛔ MEASURED at f20435d: 0 (defect (3) dropped the
    /// whole <c>Select Case</c>).
    /// </summary>
    [Test]
    public void ExitFor_InACaseArm_LeavesTheLoop_NotJustTheSelectCase()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  Select Case n\n" +
            "   Case 3\n" +
            "    Exit For\n" +
            "   Case Else\n" +
            "    t = t + n\n" +
            "  End Select\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// ⭐ The <c>Case Else</c> arm, which the emitter handles at a site of its own (the default arm
    /// otherwise gets an unconditional trailing <c>break;</c>). ⛔ <b>The value arm must list an
    /// element AFTER the exit trigger</b> — hence <c>Case 1, 2, 4</c> rather than
    /// <c>Case Is &lt; 3</c>: if everything past the trigger also lands in <c>Case Else</c>, a
    /// switch-only <c>break</c> re-enters the same arm, exits the switch again, and totals 3 exactly
    /// like a correct exit. With 4 routed to the adding arm the two diverge — measured 7 for the
    /// switch-only break.
    ///
    /// <para>⚠ <b>C++ is excluded</b>: measured, this program does not compile on the C++ backend —
    /// <c>error: use of undeclared identifier 'n'</c>, the loop variable going out of scope inside
    /// the emitted switch. A pre-existing C++ gap, not this family's, and not fixed here. C++ DOES
    /// compile and agree on the <c>Case</c>-arm shape above.</para>
    ///
    /// <para>⛔ MEASURED at f20435d: 0.</para>
    /// </summary>
    [Test]
    public void ExitFor_InTheCaseElseArm_LeavesTheLoop_NotJustTheSelectCase()
        => EveryBackendExceptCpp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  Select Case n\n" +
            "   Case 1, 2, 4\n" +
            "    t = t + n\n" +
            "   Case Else\n" +
            "    Exit For\n" +
            "  End Select\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// The whole loop wrapped in a <c>Try</c> — the exit crosses a region boundary.
    /// ⛔ MEASURED at f20435d: 10.
    /// </summary>
    [Test]
    public void ExitFor_WhenTheWholeLoopIsInsideATry_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " Try\n" +
            "  For Each n In l\n" +
            "   If n = 3 Then\n" +
            "    Exit For\n" +
            "   End If\n" +
            "   t = t + n\n" +
            "  Next\n" +
            " Catch ex As Exception\n" +
            "  t = -1\n" +
            " End Try\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// The inverse nesting: the <c>Try</c> is INSIDE the loop and the <c>Exit For</c> is the last
    /// statement of its body, so the branch leaves both constructs at once. One iteration, t=1.
    ///
    /// <para>⚠ <b>C++ and MSIL are both excluded, and both are pre-existing gaps, not this
    /// family's.</b> Measured: C++ compiles and runs but prints <b>0</b> — the accumulate inside the
    /// <c>Try</c> does not survive the exit. MSIL dies with
    /// <c>InvalidProgramException: Common Language Runtime detected an invalid program</c>.
    /// ⚠ <b>I could not explain C++'s 0</b> and did not try to fix it; it is recorded here so the
    /// next reader does not mistake it for agreement. JavaScript (1) is the oracle C# is held to.</para>
    ///
    /// <para>⛔ MEASURED at f20435d on C#: 10.</para>
    /// </summary>
    [Test]
    public void ExitFor_AsTheLastStatementOfATryBody_LeavesTheLoop()
        => JavaScriptAndCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  Try\n" +
            "   t = t + n\n" +
            "   Exit For\n" +
            "  Catch ex As Exception\n" +
            "   t = -1\n" +
            "  End Try\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "1");

    /// <summary>
    /// The same, from a <c>Catch</c> body: the first element throws, the handler exits the loop.
    ///
    /// <para>⚠ <b>MSIL is excluded</b>: measured, this program dies with
    /// <c>InvalidProgramException</c> — an <c>Exit For</c> as the last statement of a <c>Catch</c>
    /// body inside a loop is a pre-existing MSIL gap, not this family's. C++ and JavaScript both
    /// compile, run and print 1.</para>
    ///
    /// <para>⛔ MEASURED at f20435d on C#: 10.</para>
    /// </summary>
    [Test]
    public void ExitFor_AsTheLastStatementOfACatchBody_LeavesTheLoop()
        => EveryBackendExceptMsil(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  Try\n" +
            "   t = t + n\n" +
            "   Throw New Exception(\"boom\")\n" +
            "  Catch ex As Exception\n" +
            "   Exit For\n" +
            "  End Try\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "1");

    /// <summary>
    /// ⭐ <c>Exit For</c> as the body's LAST statement — no <c>If</c>, no merge block, so the exit IS
    /// the body's terminator and the emitter's own <c>target != endBlock</c> guard used to discard
    /// exactly this branch. ⛔ MEASURED at f20435d: 2 (the loop ran both elements).
    /// </summary>
    [Test]
    public void ExitFor_AsTheBodysLastStatement_LeavesAfterOneIteration()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  t = t + 1\n" +
            "  Exit For\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "1");

    /// <summary>
    /// ⭐ <c>Exit For</c> written AFTER an <c>If … End If</c>, so the exit lives in the <c>If</c>'s
    /// MERGE block and reaches the emitter through the GENERAL fall-through path rather than through
    /// any construct's own terminator handling. ⛔ <b>This is the only shape in the fixture that
    /// reaches that path.</b> Measured by instrumenting every site in the emitter that asks the
    /// question: each of the other positions here is answered by its own construct's terminator
    /// handling and never reaches the general one, so nothing else in the family can see it break.
    ///
    /// <para>⛔ <b>The <c>t = t + n</c> before the exit is load-bearing.</b> Without it the program
    /// totals 100 whether the exit works or not (only n=1 adds anything), and the shape cannot
    /// discriminate. With it: correct 100+1 = 101; a dropped exit adds 2, 3 and 4 as well and totals
    /// 110 — measured, both at f20435d and under the mutant for that path.</para>
    /// </summary>
    [Test]
    public void ExitFor_AfterAnIfBlock_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  If n = 1 Then\n" +
            "   t = t + 100\n" +
            "  End If\n" +
            "  t = t + n\n" +
            "  Exit For\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "101");

    /// <summary>
    /// NESTED: an inner <c>Exit For</c> leaves ONLY the inner loop. Inner {5,10,15,20} exits at 15,
    /// so 5+10 = 15 per outer pass over four elements = 60. ⛔ The element after the exit point (20)
    /// is load-bearing — with inner {5,10,15}, "break before 15" and "skip 15 and carry on" are
    /// identical. If the OUTER loop were wrongly exited too this totals 15.
    /// ⛔ MEASURED at f20435d: 200 (every element added on every pass).
    /// </summary>
    [Test]
    public void ExitFor_InTheInnerOfANestedForEach_LeavesOnlyTheInnerLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(1)\n outer.Add(2)\n outer.Add(3)\n outer.Add(4)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(5)\n inner.Add(10)\n inner.Add(15)\n inner.Add(20)\n" +
            " Dim t As Integer = 0\n" +
            " For Each o In outer\n" +
            "  For Each n In inner\n" +
            "   If n = 15 Then\n" +
            "    Exit For\n" +
            "   End If\n" +
            "   t = t + n\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "60");

    /// <summary>
    /// ⚠ The OUTER loop of a nested pair exits, which the inner-loop case above does NOT cover: the
    /// exit must skip the whole remaining outer body, INCLUDING the inner loop. Outer {1,2,3} exits
    /// at 3, so the inner {5,6} runs twice: (5+6)*2 = 22.
    /// ⛔ MEASURED at f20435d: 33 — the outer ran all three passes.
    /// </summary>
    [Test]
    public void ExitFor_InTheOuterOfANestedForEach_LeavesTheOuterLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim outer As New List(Of Integer)()\n" +
            " outer.Add(1)\n outer.Add(2)\n outer.Add(3)\n" +
            " Dim inner As New List(Of Integer)()\n" +
            " inner.Add(5)\n inner.Add(6)\n" +
            " Dim t As Integer = 0\n" +
            " For Each o In outer\n" +
            "  If o = 3 Then\n" +
            "   Exit For\n" +
            "  End If\n" +
            "  For Each i In inner\n" +
            "   t = t + i\n" +
            "  Next\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "22");

    /// <summary>
    /// ⚠ Two SIBLING loops, exit in the first only: the SECOND loop must still run to completion.
    /// 1 (first loop stops at 2) + 10 + 20 = 31.
    ///
    /// <para>⚠ This and the outer-of-nested case above BOTH measured 33 at f20435d, from OPPOSITE
    /// causes — there, an exit that leaks into an enclosing loop and an exit that is dropped are
    /// indistinguishable. Neither is covered by the inner-loop nesting case: keep both.</para>
    /// </summary>
    [Test]
    public void ExitFor_InTheFirstOfTwoSiblingForEach_LeavesOnlyThatLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim a As New List(Of Integer)()\n" +
            " a.Add(1)\n a.Add(2)\n" +
            " Dim b As New List(Of Integer)()\n" +
            " b.Add(10)\n b.Add(20)\n" +
            " Dim t As Integer = 0\n" +
            " For Each x In a\n" +
            "  If x = 2 Then\n" +
            "   Exit For\n" +
            "  End If\n" +
            "  t = t + x\n" +
            " Next\n" +
            " For Each y In b\n" +
            "  t = t + y\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "31");

    // ============================================================================================
    // CONTRACT 2 — the ORDINARY end of an iteration still emits nothing. ⭐ THE anti-regression
    // guard for the whole family: the If's merge block branches to the SAME block an Exit For would,
    // and only IRBranch.IsLoopExit separates them.
    // ============================================================================================

    /// <summary>
    /// ⭐ <b>Load-bearing control.</b> No <c>Exit For</c> anywhere, and the <c>If</c>'s merge block
    /// branches to the loop's end block exactly as an <c>Exit For</c> would. 1, then "two"+2, then 3.
    /// ⛔ MEASURED under the mutant that makes every branch to a <c>For Each</c>'s end a
    /// <c>break</c>: <b>1</b> — the loop leaves after its first iteration. Every other case in this
    /// fixture passes under that mutant; this one is the only thing that fails.
    /// </summary>
    [Test]
    public void AnIfWithNoExitFor_StillIteratesEveryElement()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n" +
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

    // ============================================================================================
    // CONTRACT 3 — `Exit While` / `Exit Do` / counted `Exit For` keep working, in the same positions.
    // These loops are emitted by a DIFFERENT emitter path from For Each (a `while` header with an
    // explicit end block), so the For Each cases above say nothing about them.
    // ============================================================================================

    /// <summary>
    /// A counted <c>For</c> with the exit in a <c>Case</c> arm — defect (2) on the while-shaped path.
    /// ⛔ <b>1 To 4, not 1 To 3</b>, for the same reason the <c>For Each</c> <c>Case</c> case needs a
    /// 4th element: over 1..3 a switch-only <c>break</c> also totals 3. ⛔ MEASURED at f20435d: 7.
    /// </summary>
    [Test]
    public void CountedFor_ExitForInACaseArm_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim t As Integer = 0\n" +
            " For i As Integer = 1 To 4\n" +
            "  Select Case i\n" +
            "   Case 3\n" +
            "    Exit For\n" +
            "   Case Else\n" +
            "    t = t + i\n" +
            "  End Select\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// ⛔ <c>Exit For</c> as the LAST statement of a counted <c>For</c>. ⛔ <b>The C# leg is NOT
    /// run.</b> At f20435d the emitted C# for this program was
    /// <c>while (i &lt;= 4) { t = t + 1; }</c> — no exit, no increment, an INFINITE LOOP — and the
    /// C# leg is in-process Roslyn with no timeout, so it hangs the test host rather than failing.
    /// Measured again under the mutant that drops the while-shaped body terminator's exit test: the
    /// same hang. The C# side is pinned on the emitted TEXT by
    /// <see cref="CountedFor_WhoseBodyEndsInExitFor_EmitsTheBreakThatStopsIt"/>.
    /// </summary>
    [Test]
    public void CountedFor_ExitForAsTheBodysLastStatement_LeavesAfterOneIteration()
        => WithoutTheInProcessCSharpRun(
            "Sub Main()\n" +
            " Dim t As Integer = 0\n" +
            " For i As Integer = 1 To 4\n" +
            "  t = t + 1\n" +
            "  Exit For\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "1");

    [Test]
    public void While_ExitWhileInAnIf_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim t As Integer = 0\n" +
            " Dim i As Integer = 0\n" +
            " While i < 4\n" +
            "  i = i + 1\n" +
            "  If i = 3 Then\n" +
            "   Exit While\n" +
            "  End If\n" +
            "  t = t + i\n" +
            " End While\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            // 1+2, then the exit at i=3.
            "3");

    /// <summary>⛔ 4 iterations available, so a switch-only <c>break</c> adds 4 and totals 7.
    /// ⛔ MEASURED at f20435d: 7.</summary>
    [Test]
    public void While_ExitWhileInACaseArm_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim t As Integer = 0\n" +
            " Dim i As Integer = 0\n" +
            " While i < 4\n" +
            "  i = i + 1\n" +
            "  Select Case i\n" +
            "   Case 3\n" +
            "    Exit While\n" +
            "   Case Else\n" +
            "    t = t + i\n" +
            "  End Select\n" +
            " End While\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>
    /// <c>Exit While</c> as the body's LAST statement. ⚠ Unlike the counted-<c>For</c> case above
    /// this one IS safe to run in process: a <c>While</c>'s advance is written in its own body, so a
    /// dropped exit leaves the loop bounded — measured, it prints 4 instead of 1, it does not hang.
    /// ⛔ MEASURED at f20435d: 4.
    /// </summary>
    [Test]
    public void While_ExitWhileAsTheBodysLastStatement_LeavesAfterOneIteration()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim i As Integer = 0\n" +
            " While i < 4\n" +
            "  i = i + 1\n" +
            "  Exit While\n" +
            " End While\n" +
            " PrintLine(CStr(i))\n" +
            "End Sub",
            "1");

    [Test]
    public void Do_ExitDoInAnIf_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim t As Integer = 0\n" +
            " Dim i As Integer = 0\n" +
            " Do While i < 4\n" +
            "  i = i + 1\n" +
            "  If i = 3 Then\n" +
            "   Exit Do\n" +
            "  End If\n" +
            "  t = t + i\n" +
            " Loop\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>⛔ MEASURED at f20435d: 7.</summary>
    [Test]
    public void Do_ExitDoInACaseArm_LeavesTheLoop()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim t As Integer = 0\n" +
            " Dim i As Integer = 0\n" +
            " Do While i < 4\n" +
            "  i = i + 1\n" +
            "  Select Case i\n" +
            "   Case 3\n" +
            "    Exit Do\n" +
            "   Case Else\n" +
            "    t = t + i\n" +
            "  End Select\n" +
            " Loop\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "3");

    /// <summary>⛔ MEASURED at f20435d: 4. Safe to run in process for the same reason as the
    /// <c>While</c> case: the advance is in the body.</summary>
    [Test]
    public void Do_ExitDoAsTheBodysLastStatement_LeavesAfterOneIteration()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim i As Integer = 0\n" +
            " Do While i < 4\n" +
            "  i = i + 1\n" +
            "  Exit Do\n" +
            " Loop\n" +
            " PrintLine(CStr(i))\n" +
            "End Sub",
            "1");

    // ============================================================================================
    // CONTRACT 4 — a counted `For` and its increment. ⛔ These are the shapes a RUN cannot pin,
    // because the failure mode is a program that never stops. They assert the emitted C# TEXT.
    // ============================================================================================

    /// <summary>
    /// ⭐ <b>What actually stops the loop.</b> The whole emitted body is quoted, because "the loop
    /// terminates" is exactly the property a run of this program cannot report — at f20435d it hung,
    /// and the in-process C# leg has no timeout.
    ///
    /// <para>⚠ <b>Read the quoted body carefully: there is NO increment, and that is correct.</b>
    /// The <c>Exit For</c> is unconditional, so the loop's <c>.inc</c> block is unreachable and the
    /// IR optimizer deletes it before the backend ever sees it — the <c>break</c> is the only thing
    /// that ends this loop, and the only thing that needs to be here. ⛔ Do not "fix" this assertion
    /// by expecting <c>i = i + 1</c>: it is not emitted, on purpose, and a sibling loop's increment
    /// appearing here would be the <see cref="ASecondCountedFor_StillAdvancesItsOwnVariable"/>
    /// defect.</para>
    /// </summary>
    [Test]
    public void CountedFor_WhoseBodyEndsInExitFor_EmitsTheBreakThatStopsIt()
        => Assert.That(
            Flat(EmittedCSharp(
                "Sub Main()\n" +
                " Dim t As Integer = 0\n" +
                " For i As Integer = 1 To 4\n" +
                "  t = t + 1\n" +
                "  Exit For\n" +
                " Next\n" +
                " PrintLine(CStr(t))\n" +
                "End Sub")),
            Does.Contain("while (i <= 4) { t = t + 1; break; }"),
            "the counted For's body must end in the break that stops it");

    /// <summary>
    /// ⭐ A counted <c>For</c> that FOLLOWS one whose body ends in <c>Exit For</c> must still advance
    /// its OWN variable. ⛔ Measured under a by-name increment lookup that matched any <c>.inc</c>
    /// block rather than this loop's: <c>k = k + 1</c> was emitted inside the FIRST loop (after its
    /// <c>break</c>, unreachable) and the second loop was left with no increment at all — an INFINITE
    /// LOOP. That lookup has since been removed as dead code (see
    /// <see cref="CountedFor_WhoseBodyEndsInExitFor_EmitsTheBreakThatStopsIt"/>), so no mutant
    /// reproduces it today; the shape is kept because a sibling loop keeping its own increment is
    /// worth pinning on its own. Asserted on the text because the program's value (31) is checked on
    /// JavaScript, C++ and MSIL by
    /// <see cref="ASecondCountedFor_StillRunsToCompletion_OnTheOtherBackends"/>.
    /// </summary>
    [Test]
    public void ASecondCountedFor_StillAdvancesItsOwnVariable()
        => Assert.That(
            Flat(EmittedCSharp(TwoSiblingCountedFors)),
            Does.Contain("while (k <= 3) { t = t + 10; k = k + 1; }"),
            "the second loop must advance k inside its own body");

    /// <summary>The run half of the case above, on the three backends whose harnesses time out.</summary>
    [Test]
    public void ASecondCountedFor_StillRunsToCompletion_OnTheOtherBackends()
        => WithoutTheInProcessCSharpRun(TwoSiblingCountedFors, "31");

    private const string TwoSiblingCountedFors =
        "Sub Main()\n" +
        " Dim t As Integer = 0\n" +
        " For i As Integer = 1 To 4\n" +
        "  t = t + i\n" +
        "  Exit For\n" +
        " Next\n" +
        " For k As Integer = 1 To 3\n" +
        "  t = t + 10\n" +
        " Next\n" +
        " PrintLine(CStr(t))\n" +
        "End Sub";

    // ============================================================================================
    // CONTRACT 5 — a `Select Case` as the WHOLE body of a `For Each` (defect (3)), and one label per
    // loop.
    // ============================================================================================

    /// <summary>
    /// ⭐ No <c>Exit For</c> at all — the <c>Select Case</c> IS the loop body, and its terminator is
    /// an <c>IRSwitch</c> the emitter had no arm for. 100 + 3 + 1 = 104.
    /// ⛔ MEASURED at f20435d: <b>0</b>, from a program that compiled and ran clean.
    ///
    /// <para>⚠ <b>C++ is excluded</b>: measured, this program does not compile on the C++ backend —
    /// <c>error: cannot jump from this goto statement to its label</c>, the emitted switch jumping
    /// across the range-<c>for</c>'s declarations. Pre-existing, C++-only, not this family's.
    /// JavaScript and MSIL both print 104 and are the oracle here.</para>
    /// </summary>
    [Test]
    public void ASelectCaseThatIsTheWholeBodyOfAForEach_IsEmitted()
        => EveryBackendExceptCpp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  Select Case n\n" +
            "   Case 1\n" +
            "    t = t + 100\n" +
            "   Case 2\n" +
            "    t = t + 3\n" +
            "   Case Else\n" +
            "    t = t + 1\n" +
            "  End Select\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "104");

    /// <summary>
    /// ⭐ <b>TWO switch-carrying loops in ONE method</b> — the only shape that pins the
    /// <c>goto</c> label being derived per loop. ⛔ Measured with one shared label name: the emitted
    /// C# does not compile, <c>CS0140: The label '__exit_loopx' is a duplicate</c>. A single such
    /// loop cannot see it. First loop: 1+2 = 3, exits at 3. Second: (1+2+3)*10 = 60, exits at 4.
    /// ⛔ MEASURED at f20435d: 0.
    /// </summary>
    [Test]
    public void TwoForEachLoopsEachWithASelectCaseExit_GetDistinctLabels()
        => EveryBackend(
            "Sub Main()\n" +
            " Dim a As New List(Of Integer)()\n" +
            " a.Add(1)\n a.Add(2)\n a.Add(3)\n a.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each x In a\n" +
            "  Select Case x\n" +
            "   Case 3\n" +
            "    Exit For\n" +
            "   Case Else\n" +
            "    t = t + x\n" +
            "  End Select\n" +
            " Next\n" +
            " For Each y In a\n" +
            "  Select Case y\n" +
            "   Case 4\n" +
            "    Exit For\n" +
            "   Case Else\n" +
            "    t = t + y * 10\n" +
            "  End Select\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub",
            "63");

    // ============================================================================================
    // CONTRACT 6 — `Exit Sub` returns immediately; nothing after it runs.
    // ============================================================================================

    /// <summary>
    /// ⭐ The measured repro for defect (5). ⛔ MEASURED at f20435d: "5\n-1" — the <c>Exit Sub</c>
    /// emitted nothing and control fell through to the line after the <c>If</c>. C++, JavaScript and
    /// MSIL all printed "5".
    /// </summary>
    [Test]
    public void ExitSub_ReturnsImmediately()
        => EveryBackend(
            "Sub Helper(n As Integer)\n" +
            " If n > 0 Then\n" +
            "  PrintLine(\"5\")\n" +
            "  Exit Sub\n" +
            " End If\n" +
            " PrintLine(\"-1\")\n" +
            "End Sub\n\n" +
            "Sub Main()\n" +
            " Helper(1)\n" +
            "End Sub",
            "5");

    /// <summary>
    /// ⚠ The CONTROL for the case above: an <c>Exit Sub</c> that IS the end of the <c>Sub</c>. This
    /// one was never broken — unchanged at f20435d — and it is the shape the return suppression
    /// exists for. Its emitted text is pinned separately by
    /// <see cref="AVoidSubsTrailingReturn_IsStillSuppressed"/>.
    /// </summary>
    [Test]
    public void ExitSub_AsTheSubsLastStatement_StillEndsTheSub()
        => EveryBackend(
            "Sub Helper()\n" +
            " PrintLine(\"a\")\n" +
            " Exit Sub\n" +
            "End Sub\n\n" +
            "Sub Main()\n" +
            " Helper()\n" +
            " PrintLine(\"b\")\n" +
            "End Sub",
            "a\nb");

    /// <summary>
    /// An <c>Exit Sub</c> from inside a <c>For Each</c> — an exit that is ALSO a loop escape, and
    /// must not be mistaken for one. The trailing "end" must NOT print.
    /// ⛔ MEASURED at f20435d: "1\n2\n3\nend\nafter".
    /// </summary>
    [Test]
    public void ExitSub_FromInsideAForEach_LeavesTheSubNotJustTheLoop()
        => EveryBackend(
            "Sub Helper(l As List(Of Integer))\n" +
            " For Each n In l\n" +
            "  PrintLine(CStr(n))\n" +
            "  If n = 2 Then\n" +
            "   Exit Sub\n" +
            "  End If\n" +
            " Next\n" +
            " PrintLine(\"end\")\n" +
            "End Sub\n\n" +
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n" +
            " Helper(l)\n" +
            " PrintLine(\"after\")\n" +
            "End Sub",
            "1\n2\nafter");

    /// <summary>
    /// ⛔ <c>Exit Sub</c> as the last statement of a counted <c>For</c> body — the return is the ONLY
    /// thing that ends that loop, because the unconditional exit makes the <c>.inc</c> block
    /// unreachable and the optimizer deletes it. ⛔ <b>The C# leg is NOT run</b>: at f20435d this
    /// printed "1" forever, and so does the mutant that restores the old suppression test. The C#
    /// side is pinned on the text by
    /// <see cref="ExitSub_InACountedFor_EmitsTheReturnThatStopsIt"/>.
    /// </summary>
    [Test]
    public void ExitSub_FromInsideACountedFor_LeavesTheSub()
        => WithoutTheInProcessCSharpRun(ExitSubInACountedFor, "1\nafter");

    /// <summary>
    /// The C# half of the case above, on the emitted text. ⚠ This asserts the EARLY return is
    /// PRESENT — not that no <c>return;</c> exists anywhere, which would be false for a different
    /// reason (a void method's genuinely trailing return is still suppressed, on purpose).
    /// </summary>
    [Test]
    public void ExitSub_InACountedFor_EmitsTheReturnThatStopsIt()
        => Assert.That(
            Flat(EmittedCSharp(ExitSubInACountedFor)),
            Does.Contain("while (i <= 4) { Console.WriteLine(Convert.ToString(i)); return; }"),
            "the loop body must end in the return that leaves the Sub");

    private const string ExitSubInACountedFor =
        "Sub Helper()\n" +
        " For i As Integer = 1 To 4\n" +
        "  PrintLine(CStr(i))\n" +
        "  Exit Sub\n" +
        " Next\n" +
        "End Sub\n\n" +
        "Sub Main()\n" +
        " Helper()\n" +
        " PrintLine(\"after\")\n" +
        "End Sub";

    /// <summary>
    /// ⭐ The other half of the suppression contract, and the ONLY thing that can see it: a void
    /// <c>Sub</c> whose only return is its trailing one emits NO <c>return;</c> at all. Dropping the
    /// suppression entirely is semantically harmless — the program still prints "a\nb" — so no run
    /// of any shape can tell the difference. Measured with the suppression removed: two
    /// <c>return;</c> statements appear in this program's emitted C#.
    /// </summary>
    [Test]
    public void AVoidSubsTrailingReturn_IsStillSuppressed()
        => Assert.That(
            EmittedCSharp(
                "Sub Helper()\n" +
                " PrintLine(\"a\")\n" +
                " Exit Sub\n" +
                "End Sub\n\n" +
                "Sub Main()\n" +
                " Helper()\n" +
                " PrintLine(\"b\")\n" +
                "End Sub"),
            Does.Not.Contain("return;"),
            "a void Sub's trailing return is still suppressed");

    // ============================================================================================
    // THE SPELLING — `break` where `break` works, `goto` only where it does not. Both halves are
    // needed: an emitter that always emits `goto` and always emits the label is SEMANTICALLY
    // CORRECT everywhere, so only an assertion on the emitted text can hold it to the readable
    // spelling (and an unreferenced label is CS0164).
    // ============================================================================================

    /// <summary>
    /// ⭐ A <c>Select Case</c> that CLOSES before the <c>Exit For</c>: the exit is no longer inside a
    /// switch, so it must go back to reading <c>break;</c>, and no label may be emitted.
    ///
    /// <para>⛔ This shape, not a loop with no switch in it at all, is what pins the switch nesting
    /// being UNWOUND. Measured with the unwind removed: everything after the first <c>Select Case</c>
    /// in a method still looks like it is inside one, and this loop's exit becomes
    /// <c>goto __exit_foreach0_end;</c> with the label after the loop. The program still prints 102
    /// either way — there is nothing for a run to see.</para>
    ///
    /// <para>⚠ <b>C++ is excluded from the RUN half of this shape</b>
    /// (<see cref="AClosedSelectCaseThenAnExitFor_StillRuns"/>): a <c>Select Case</c> inside a
    /// <c>For Each</c> does not compile on the C++ backend, the same pre-existing
    /// <c>cannot jump from this goto statement to its label</c> gap noted above.</para>
    /// </summary>
    [Test]
    public void AnExitForAfterAClosedSelectCase_IsSpelledBreak()
    {
        var cs = EmittedCSharp(ClosedSelectCaseThenExitFor);
        Assert.Multiple(() =>
        {
            Assert.That(Flat(cs), Does.Contain("if (n == 3) { break; }"),
                "the exit is outside the switch, so break means the loop");
            Assert.That(cs, Does.Not.Contain("goto "), "no goto is needed here");
            Assert.That(cs, Does.Not.Contain("__exit_"),
                "and no loop-exit label may be emitted when nothing jumps to it (CS0164)");
        });
    }

    /// <summary>The run half of the case above. ⚠ C++ excluded — see that case's docstring.</summary>
    [Test]
    public void AClosedSelectCaseThenAnExitFor_StillRuns()
        => EveryBackendExceptCpp(ClosedSelectCaseThenExitFor, "102");

    private const string ClosedSelectCaseThenExitFor =
        "Sub Main()\n" +
        " Dim l As New List(Of Integer)()\n" +
        " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
        " Dim t As Integer = 0\n" +
        " For Each n In l\n" +
        "  Select Case n\n" +
        "   Case 1\n" +
        "    t = t + 100\n" +
        "   Case Else\n" +
        "    t = t + 1\n" +
        "  End Select\n" +
        "  If n = 3 Then\n" +
        "   Exit For\n" +
        "  End If\n" +
        " Next\n" +
        " PrintLine(CStr(t))\n" +
        "End Sub";

    /// <summary>
    /// The other half: inside a <c>Case</c> arm the exit IS a <c>goto</c>, the label IS emitted, and
    /// ⭐ there is NO trailing <c>break;</c> behind the <c>goto</c> — that would be unreachable code
    /// (CS0162). Measured with the arm's exit test removed: the emitter falls through to its ordinary
    /// "end of a case arm" handling and writes the <c>goto</c> AND a <c>break;</c>. The program runs
    /// correctly either way, so only the text can see it.
    /// </summary>
    [Test]
    public void AnExitForInACaseArm_IsSpelledAsABareGoto()
    {
        var cs = EmittedCSharp(
            "Sub Main()\n" +
            " Dim l As New List(Of Integer)()\n" +
            " l.Add(1)\n l.Add(2)\n l.Add(3)\n l.Add(4)\n" +
            " Dim t As Integer = 0\n" +
            " For Each n In l\n" +
            "  Select Case n\n" +
            "   Case 3\n" +
            "    Exit For\n" +
            "   Case Else\n" +
            "    t = t + n\n" +
            "  End Select\n" +
            " Next\n" +
            " PrintLine(CStr(t))\n" +
            "End Sub");
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Match(@"goto __exit_\w+;"),
                "break would leave only the switch, so the exit must be a goto");
            Assert.That(cs, Does.Match(@"__exit_\w+: ;"),
                "and the label it jumps to must be emitted after the loop (CS0159)");
            Assert.That(Flat(cs), Does.Not.Match(@"goto __exit_\w+; break;"),
                "no unreachable break may follow the goto (CS0162)");
        });
    }
}
