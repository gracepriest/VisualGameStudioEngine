using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #227 (and #293) — a BOTTOM-TESTED loop (`Do … Loop While`, `Do … Loop Until`, and `Do … Loop` with no condition at all) is written ONCE
//  on C#. RUN, against vbc, through every entry point.
//
//  ⛔ THE BUG. The C# backend wrote such a loop as a PEELED first iteration followed by `while (cond) { body }`. The structured walk reached
//  the body first (from the block before the loop) and wrote it straight; when it reached the condition, `GenerateLoop` wrote a SECOND copy of
//  the body, and that copy could not hold any block the first had already written: an If's continuation, a nested loop, and the statements
//  after them. MEASURED through the CLI, `--optimize` and a `.blproj` build:
//    - a statement after an If ran in the first iteration only (body=101 where vbc prints 103);
//    - a plain `Do … Loop Until` around a `Do While` never ended (HANG);
//    - a `Do … Loop` ran its body once, and the code after the loop was lost with it (#293);
//    - a Function that returns from inside a `Do … Loop` failed csc with CS0161.
//  JavaScript, C++ and MSIL were already right. The fix (CSharpBackend, `GenerateBottomTestedLoop`) writes the loop from its body, ONCE:
//  `do { body } while (c);` when the condition is one block that writes nothing, `while (true) { body; …condition…; if (!c) break; }` for
//  everything else (#256's AndAlso / OrElse / If() conditions, a storing condition, a loop with no condition). A reachable condition whose
//  exit test is never written THROWS instead of emitting a loop that hangs.
//
//  ⭐ THE ORACLE IS vbc, not a backend: every `Vb` below is what the SDK's vbc printed for the program wrapped in a Module (the implementer's
//  S/t227 probes, every one VB-RAN). The programs hold the shapes the old emission lost — an If / Else continuation, a nested For, a nested
//  loop, an AndAlso condition, a Select Case, a loop with no condition — and each ends in code that must run after the loop.
//
//  ⛔⛔ EVERY C# RUN HERE IS HANG-SAFE (`hangSafe: true`): a regression of the fix hangs, and in the test host that would freeze every
//  other test. The run is a child process with a 20 s limit that is killed, so a hang is one FAILURE whose first line starts "hung".
//  (`LoopConditionEmissionShapeTests.NoLoopTestRunsEmittedCSharpInTheTestHost` reads this file for exactly that.)
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): the real BasicLang CLI
//  (standard passes), the real CLI with `--optimize` (aggressive passes) and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive`
//  (what a Release .blproj build and the IDE call). Each row runs all three.
//
//  ⚠ C# ONLY, so this fixture is in `JsExecutionTierRosterTests.NotJavaScriptExecution`, not the roster: JavaScript refuses a loop whose
//  condition has control flow (#257), so most rows have no Node leg to run. C++ and MSIL printed vbc's answer for every row before the fix
//  and still do (measured; the matrix is S/t227/matrix-table.txt), so the cheap reference is a measurement, not a leg.
//
//  ⛔ KNOWN GAPS — each is a defect that is NOT #227's, with NO test (a test would pin it):
//    p11       a ByRef argument inside a loop CONDITION (`Loop While More(n)` with `ByRef n`) is emitted without `ref` — CS1620. The #232
//              family (an argument inlined into a condition loses its ByRef). Unchanged by #227.
//    k_LU k_W  `Continue Do` / `Continue While`: there is no Continue statement in BasicLang (#262), so there is no row.
//
//  ⭐ WHAT KILLS WHAT. Five mutants of the fix, each applied to a plain source copy of it and measured (every one killed, none hung the host):
//    M1  the loop is not recognised at its body (the old peel + copy)   8 of 10 rows: every one but the Select Case row (the old shape got it
//                                                                      right) and the controls
//    M2  `do { } while`: While / Until polarity swapped                 the 5 plain-condition rows: LoopWhile_IfElseContinuation,
//                                                                      LoopUntil_IfElseIfContinuation, LoopWhile_NestedFor,
//                                                                      LoopUntil_AroundADoWhile_n_LDctl, LoopUntil_BodyWritesWhatTheConditionReads
//    M3  `while (true)`: the exit test's polarity swapped               the 2 AndAlso rows
//    M4  the loop's end block is not pushed, so `Exit Do` is no break   DoLoop_NoCondition_ExitDo_CodeAfterRuns_293 and
//                                                                      LoopWhile_AndAlso_ExitDoFromSelectCase: both HANG, and the runner kills
//                                                                      them ("hung (#256/#227) …") — a failure, not a frozen host
//    M5  the body's branch to the condition is not walked into it       the 2 AndAlso rows (the safety net throws: the CLI refuses the program)
//  The fast shape tests (LoopConditionEmissionShapeTests) kill M1, M2, M3 and M5 without running anything, and the #256 fixture's five new C#
//  cells (f_LW_aa f_LW_ctl x_LW n_LD n_LDctl) kill every one of the five between them; M4 needs a run (an Exit Do), which is what the two Exit Do
//  rows above are for.
// ================================================================================================

/// <summary>
/// #227 / #293 RUN: a bottom-tested loop is written once on C#, and every iteration runs the whole body — the blocks after an If, a nested
/// loop, the code after the loop — as vbc runs it.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the spawned runners and the CLI processes share the machine
public class BottomTestedLoopCSharpExecutionTests
{
    /// <summary>Every row is C# only and hang-safe: <see cref="TempProbe.Agrees"/> says where the answer is asserted, <see cref="TempProbe.HangSafe"/> how it runs.</summary>
    private static TempProbe Row(string id, string source, string vb) => new(id, source, vb, Bk.CSharp, HangSafe: true);

    internal static readonly TempProbe[] Rows =
    {
        // ---- the shapes the peel + copy lost -------------------------------------------------------------------------------------
        // Do … Loop While: an If … Else, then a statement AFTER it. The copy ran the statement after the If in the first iteration only.
        Row("LoopWhile_IfElseContinuation", """
            Sub Main()
                Dim i As Integer = 0
                Dim a As Integer = 0
                Dim b As Integer = 0
                Dim t As Integer = 0
                Do
                    i = i + 1
                    If i Mod 2 = 0 Then
                        a = a + 10
                    Else
                        b = b + 1
                    End If
                    t = t + 100
                Loop While i < 6
                Console.WriteLine("i=" & i & " a=" & a & " b=" & b & " t=" & t)
            End Sub
            """, "i=6 a=30 b=3 t=600"),

        // Do … Loop Until: an If / ElseIf / Else, then a statement after it.
        Row("LoopUntil_IfElseIfContinuation", """
            Sub Main()
                Dim i As Integer = 0
                Dim tot As Integer = 0
                Do
                    i = i + 1
                    If i < 3 Then
                        tot = tot + 1
                    ElseIf i < 5 Then
                        tot = tot + 10
                    Else
                        tot = tot + 100
                    End If
                    tot = tot + 1000
                Loop Until i = 6
                Console.WriteLine("i=" & i & " tot=" & tot)
            End Sub
            """, "i=6 tot=6222"),

        // a nested counted For in the body, and a statement after it
        Row("LoopWhile_NestedFor", """
            Sub Main()
                Dim i As Integer = 0
                Dim s As Integer = 0
                Dim after As Integer = 0
                Do
                    i = i + 1
                    For k As Integer = 1 To i
                        s = s + k
                    Next
                    after = after + 1
                Loop While i < 4
                Console.WriteLine("i=" & i & " s=" & s & " after=" & after)
            End Sub
            """, "i=4 s=20 after=4"),

        // ⛔ n_LDctl — a `Do … Loop Until` around a `Do While`, both PLAIN conditions. It HUNG on C# before #227 (the copy lost the inner loop).
        Row("LoopUntil_AroundADoWhile_n_LDctl", """
            Sub Main()
                Dim i As Integer = 0
                Dim j As Integer = 0
                Dim body As Integer = 0
                Do
                    j = 0
                    Do While j < 2
                        j = j + 1
                        body = body + 1
                    Loop
                    i = i + 1
                Loop Until i >= 3
                Console.WriteLine("i=" & i & " body=" & body)
            End Sub
            """, "i=3 body=6"),

        // ---- a condition holding control flow (#256's `while (true)` shape, now around the body) -----------------------------------
        // AndAlso after an If / Else body: the counter shows each operand ran once per iteration and short-circuited, the sum that the body ran whole.
        Row("LoopWhile_AndAlso_IfElseContinuation", """
            Dim seen As String = ""

            Function P(tag As String, v As Boolean) As Boolean
                seen = seen & tag
                Return v
            End Function

            Sub Main()
                Dim i As Integer = 0
                Dim body As Integer = 0
                Do
                    i = i + 1
                    If i = 2 Then
                        body = body + 50
                    Else
                        body = body + 1
                    End If
                    body = body + 1000
                Loop While P("a", i < 4) AndAlso P("b", i < 9)
                Console.WriteLine("i=" & i & " body=" & body & " seen=" & seen)
            End Sub
            """, "i=4 body=4053 seen=abababa"),

        // ---- the condition reads what the body writes -----------------------------------------------------------------------------
        // The body moves both the counter and the bound the (Until) condition compares: a test written before the body, or on a stale copy, ends it
        // somewhere else. The trail `1/5 2/5 3/4 4/4` is the bound at the end of each iteration.
        Row("LoopUntil_BodyWritesWhatTheConditionReads", """
            Sub Main()
                Dim n As Integer = 0
                Dim lim As Integer = 6
                Dim s As String = ""
                Do
                    n = n + 1
                    If n Mod 2 = 1 Then
                        lim = lim - 1
                    Else
                        lim = lim + 0
                    End If
                    s = s & n & "/" & lim & " "
                Loop Until n >= lim
                Console.WriteLine(s & "n=" & n & " lim=" & lim)
            End Sub
            """, "1/5 2/5 3/4 4/4 n=4 lim=4"),

        // ---- #293 and its sibling: a `Do … Loop` with NO condition -----------------------------------------------------------------
        // ⛔ #293 — a `Do … Loop` left by `Exit Do` ran its body ONCE on C# and the code after it was lost. Two such loops, each followed by code that must run.
        Row("DoLoop_NoCondition_ExitDo_CodeAfterRuns_293", """
            Sub Main()
                Dim i As Integer = 0
                Dim body As Integer = 0
                Do
                    i = i + 1
                    If i Mod 2 = 0 Then
                        body = body + 10
                    End If
                    If i >= 5 Then Exit Do
                    body = body + 1
                Loop
                body = body + 1000
                Console.WriteLine("i=" & i & " body=" & body)
                Dim j As Integer = 0
                Do
                    j = j + 1
                    If j = 3 Then Exit Do
                Loop
                Console.WriteLine("after: j=" & j)
            End Sub
            """, "i=5 body=1024\nafter: j=3"),

        // a Function whose only way out of its `Do … Loop` is a Return: csc said CS0161 (not all code paths return a value) before #227
        Row("DoLoop_NoCondition_FunctionReturnsFromInside", """
            Function Collatz(n As Integer) As Integer
                Dim steps As Integer = 0
                Do
                    If n = 1 Then
                        Return steps
                    End If
                    If n Mod 2 = 0 Then
                        n = n \ 2
                    Else
                        n = 3 * n + 1
                    End If
                    steps = steps + 1
                Loop
            End Function

            Sub Main()
                Console.WriteLine(Collatz(6))
                Console.WriteLine(Collatz(7))
            End Sub
            """, "8\n16"),

        // ---- Exit Do out of a Select Case: a C# `break` would leave the switch, so it is a `goto` to a label after the loop ----------
        Row("LoopWhile_AndAlso_ExitDoFromSelectCase", """
            Dim seen As String = ""

            Function P(tag As String, v As Boolean) As Boolean
                seen = seen & tag
                Return v
            End Function

            Sub Main()
                Dim i As Integer = 0
                Dim s As String = ""
                Do
                    i = i + 1
                    Select Case i
                        Case 2
                            s = s & "two,"
                        Case 4
                            Exit Do
                        Case Else
                            s = s & i & ","
                    End Select
                Loop While P("a", i < 9) AndAlso P("b", i < 9)
                Console.WriteLine("i=" & i & " s=" & s & " seen=" & seen)
            End Sub
            """, "i=4 s=1,two,3, seen=ababab"),

        // ---- the CONTROLS: a top-tested loop is not a bottom-tested one, and must stay exactly as it was ----------------------------
        // `Do While … Loop` (an If, a nested For, a statement after) and `While … End While` (an If / Else, a nested While with a body Dim).
        Row("Controls_DoWhile_And_While", """
            Sub Main()
                Dim i As Integer = 0
                Dim a As Integer = 0
                Dim t As Integer = 0
                Do While i < 5
                    i = i + 1
                    If i Mod 2 = 0 Then
                        a = a + 10
                    End If
                    For k As Integer = 1 To 2
                        t = t + 1
                    Next
                    t = t + 100
                Loop
                Console.WriteLine("do while: i=" & i & " a=" & a & " t=" & t)
                i = 0
                a = 0
                t = 0
                While i < 4
                    i = i + 1
                    If i Mod 2 = 0 Then
                        a = a + 10
                    Else
                        a = a + 1
                    End If
                    Dim j As Integer = 0
                    While j < i
                        j = j + 1
                        t = t + 1
                    End While
                    t = t + 100
                End While
                Console.WriteLine("while: i=" & i & " a=" & a & " t=" & t)
            End Sub
            """, "do while: i=5 a=20 t=510\nwhile: i=4 a=22 t=410"),
    };

    private static IEnumerable<TestCaseData> Cells() => Rows.Select(r => new TestCaseData(r).SetName(r.Id));

    /// <summary>
    /// Each program, through the CLI, the CLI with `--optimize` and `CompileProjectFiles`, run on C# in a child process: it prints what vbc printed.
    /// A loop that never ends is a failure named "hung", not a frozen host.
    /// </summary>
    [TestCaseSource(nameof(Cells))]
    public void ABottomTestedLoop_RunsEveryIterationWhole_AsVbcRunsIt(TempProbe probe)
        => TempExec.AssertMatchesInEveryEntryPoint(probe.Agrees, probe.Source, probe.Vb, probe.Id, hangSafe: true);
}
