using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #215, RUN. JavaScript: a comparison with a statically Object operand is VB's LATE-BOUND comparison (ADR-0012), as MSIL's (#177) and C#'s (#211) already were.
//
//  ⛔ THE BUG. The JavaScript backend emitted its own `===` and `<` for `o = 20`, `o = Nothing`, `Select Case o : Case 20`. `===` is true only for ONE JavaScript type on both sides, so from a green build: an Object
//  holding 0, "" or False was not `= Nothing` and `Case Nothing` did not meet it; an Object holding "20" was not `= 20`, "True" was not `= True`, and an Object True was not `= -1` (JavaScript reads true as 1); "abc"
//  against 20 printed False where vbc THROWS InvalidCastException, and so did a class instance. The fix is in `JavaScriptBackend`: `IsLateBoundComparison` (the C# / MSIL rule: either operand statically Object, the
//  Nothing literal excluded) makes `RenderCompare` call ONE prelude helper, `__blCompareObject(a, b, op)`, and a Select Case label with such a comparison (a value, each range bound, `Case Is op`, an Or alternative,
//  `Case Nothing` on an Object subject) goes through the same predicate (`IsLateBoundCase`, `PatternTest`, `CaseEqualityTest`). `Is` / `IsNot` and `Case Is Nothing` stay reference identity (ADR-0011). The helper is
//  emitted only when `UsesObjectComparison` finds a use (blocks, Select Case labels AND When guards), and `JsExceptionTypes` names InvalidCastException then, so an uncaught throw still finds its class.
//
//  ⭐ THE ORACLE IS vbc. Every `…Vb` below is what the SDK's vbc prints for the program wrapped in a VB Module (S/t215/tw/p, run through S/t215/vbfull.py by the test-writer: every answer matched the implementer's own
//  `.exp` in S/t215/mprobes). Never what a backend printed. Three programs use syntax VB does not have, so their vbc run is a VB-legal TWIN (S/t215/tw/vb), never the program itself: `Case Is Nothing` (the twin is
//  `If o Is Nothing`) and the `When` guard (the twin is an If chain with `AndAlso`).
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): every program goes through the real CLI (standard passes), the real CLI with `--optimize` (aggressive)
//  and `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj and the IDE call), PLUS the two in-process routes: `JavaScriptExecutionTests.RunJs` (NO optimizer) and
//  `JavaScriptOptimizedExecutionTests.RunOptimized` (the standard pipeline). Every run is Node under `RunNodeScript`'s 30 s kill timer; the CLI legs have their own limit. The FAST half is
//  `JavaScriptLateBoundComparisonShapeTests` below: what the compiler WRITES (no process), and that the prelude stays ADDITIVE, because the web-form output shares it.
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from a plain source copy of the fix outside the worktree, its BasicLang.dll swapped into a copy of the test output; the cases that go red, measured; the same
//  build with no change goes red nowhere):
//    M1 no late binding (`IsLateBoundComparison` is always False)   -> FOURTEEN: every row that compares an Object (`ObjectEqualsNothing_…`, `AnObjectHoldingNothing_…`, `CaseNothing_…`, `AnObjectString_IsConverted…`,
//                                                                       `AnObjectBoolean_…`, `ObjectAgainstObject_…`, `AnObjectString_ThatIsNoNumber_…`, `AnObjectHoldingAClassInstance_…`, `SelectCase_…`,
//                                                                       `AComparisonOnlyInAWhenGuard_…`), the fast `ThePreludeStaysAdditive_…` (no helper left to count) and the moved pins
//                                                                       `MsilObjectBoxing…L05_…JavaScript…`, `L08_…JavaScript…` and `L11b_…`. The NaN row (JavaScript's own operators already answer a NaN
//                                                                       the VB way), the two controls and the L09 gap pin stay green.
//    M2 `Case Nothing` on an Object kept as a null test             -> THREE: `CaseNothing_…`, `SelectCase_…` (an Object holding 0, "" or False answers `something`) and the moved pin `L08_…JavaScript…`.
//    M3 True is +1, not -1                                          -> THREE: `AnObjectBoolean_…` (`t = -1`, `t < 0`), `ObjectAgainstObject_…` (True against -1) and `SelectCase_…` (an Object True meets `Case 1 To 5`).
//    M4 the prelude scan is blind to a When guard                   -> ONE: `AComparisonOnlyInAWhenGuard_…`. The build is REFUSED at every entry point ("__blCompareObject is needed but UsesObjectComparison did not see it"): the
//                                                                       guard is the only late-bound comparison in that program, and `SelectCase_…` stays green because its other labels already make the scan emit the helper.
//    M5 a NaN compares equal (`c = 0` where the operands are unordered) -> ONE: `ANaN_…` (`n = n` True where vbc says False).
//    M6 (beyond the five asked) `JsExceptionTypes` forgets InvalidCastException -> TWO: `AnObjectString_ThatIsNoNumber_…` (its program never names the class, so the helper's `throw` is a ReferenceError and the message is wrong)
//                                                                       and the fast `ThePreludeStaysAdditive_…`. `AnObjectHoldingAClassInstance_…` stays green BY DESIGN: its handler names the class, which puts it in the prelude anyway.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect). Each is the same before and after #215 unless it says otherwise:
//    * A BOXED `Is`: `Dim a As Object = Make(5) : Dim b As Object = Make(5) : a Is b` is True on JavaScript where vbc says False. JavaScript primitives have no box identity and no boxing scheme is invented here.
//      The named pin is `MsilObjectBoxingExecutionTests.L09_IsIdentity_JavaScript_PinsPreExistingDisagreement_Against215`.
//    * A Char is refused (BL7004: JavaScript has no character type), so an Object holding `"A"c` never reaches the helper.
//    * The InvalidCastException MESSAGE for an operand that is no primitive (a class instance, an array) only approximates VB's wording; the primitive-to-primitive messages are asserted (P05).
//    * A currency symbol is not parsed (`"$5" = 5` throws where VB's ToDouble accepts it under a currency culture).
//    * An `&H` / `&O` string past 64 bits throws InvalidCastException where VB throws OverflowException.
//    * An Object holding Nothing compared with a number once the optimizer has propagated the Nothing literal into the comparison (`Dim n As Object = Nothing : n = 0`, `n < 1`, `n = False`) is #214's known gap: False where vbc
//      prints True. On JavaScript it is the Nothing-literal exemption in `CopyPropagationPass.KeepsLateBinding` alone: without it the helper answers vbc's True (the M2 note in `ObjectComparisonUnderOptimizerExecutionTests`).
//    * C++ has no Object at all ("Object has no C++ mapping"), so there is nothing to compare there; LLVM has no console.
//
//  ⚠ Named "…ExecutionTests" and every row spawns Node, so it is in JsExecutionTierRosterTests' roster. JavaScriptLateBoundComparisonShapeTests (no process, no [Category("Integration")]) is NOT.
// ================================================================================================

/// <summary>#215 — an Object comparison on JavaScript is VB's late-bound comparison, RUN under Node through every entry point against vbc.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the CLI legs spawn dotnet and every row spawns Node; keep the machine to this fixture
public class JavaScriptLateBoundComparisonExecutionTests
{
    // ---- the programs (S/t215/tw/p, one file each; vbc's answer is the `…Vb` beside it) ----

    /// <summary>P01 — `= Nothing` / `&lt;&gt; Nothing` on an Object holding 0, "", False, Nothing and 5, in a statement and in an `If`. Nothing is the other operand's DEFAULT (0, "" or False), so the 0, the "" and the False are all `= Nothing`. M1.</summary>
    internal const string NothingEquals = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Main()
            Dim a As Object = Make(0)
            Dim b As Object = Make("")
            Dim c As Object = Make(False)
            Dim d As Object = Make(Nothing)
            Dim e As Object = Make(5)
            Console.WriteLine(a = Nothing)
            Console.WriteLine(a <> Nothing)
            Console.WriteLine(b = Nothing)
            Console.WriteLine(b <> Nothing)
            Console.WriteLine(c = Nothing)
            Console.WriteLine(c <> Nothing)
            Console.WriteLine(d = Nothing)
            Console.WriteLine(d <> Nothing)
            Console.WriteLine(e = Nothing)
            Console.WriteLine(e <> Nothing)
            If a = Nothing Then
                Console.WriteLine("if-zero-is-nothing")
            End If
        End Sub
        """;

    /// <summary>vbc's output for <see cref="NothingEquals"/>.</summary>
    internal const string NothingEqualsVb = "True\nFalse\nTrue\nFalse\nTrue\nFalse\nTrue\nFalse\nFalse\nTrue\nif-zero-is-nothing";

    /// <summary>P02 — `Case Nothing` is VB's `subject = Nothing` (an Object holding 0, "" or False meets it), `Case Is Nothing` is IDENTITY (only a real Nothing does). vbc has no `Case Is Nothing`: its twin (S/t215/tw/vb) is `If o Is Nothing`. M2.</summary>
    internal const string CaseNothing = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Test(o As Object)
            Select Case o
                Case Nothing
                    Console.WriteLine("nothing")
                Case Else
                    Console.WriteLine("something")
            End Select
        End Sub
        Sub TestIs(o As Object)
            Select Case o
                Case Is Nothing
                    Console.WriteLine("is-nothing")
                Case Else
                    Console.WriteLine("not-nothing")
            End Select
        End Sub
        Sub Main()
            Test(Make(0))
            Test(Make(""))
            Test(Make(False))
            Test(Make(Nothing))
            Test(Make(5))
            Test(Make("x"))
            Test(Make(True))
            TestIs(Make(0))
            TestIs(Make(""))
            TestIs(Make(False))
            TestIs(Make(Nothing))
        End Sub
        """;

    /// <summary>vbc's output for <see cref="CaseNothing"/>.</summary>
    internal const string CaseNothingVb = "nothing\nnothing\nnothing\nnothing\nsomething\nsomething\nsomething\nnot-nothing\nnot-nothing\nnot-nothing\nis-nothing";

    /// <summary>P03 — an Object holding a String against an Integer and a Double, on either side, with `=`, `&lt;&gt;`, `&lt;` and `&gt;`: VB converts the String by ToDouble (" 20 ", "1e3", "-7" included), so the orderings are numeric too. The `lit` rows are initialised from a literal, so copy propagation (#214) is in the way.</summary>
    internal const string StringAgainstNumber = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Integers()
            Dim s As Object = Make("20")
            Dim n As Object = Make(20)
            Console.WriteLine(s = 20)
            Console.WriteLine(s <> 20)
            Console.WriteLine(20 = s)
            Console.WriteLine(s = n)
            Dim p As Object = Make(" 20 ")
            Console.WriteLine(p = 20)
            Console.WriteLine(s < 3)
            Console.WriteLine(s > 3)
            Dim lit As Object = "20"
            Console.WriteLine(lit = 20)
            Console.WriteLine(lit <> 20)
        End Sub
        Sub Doubles()
            Dim s As Object = Make("2.5")
            Console.WriteLine(s = 2.5)
            Console.WriteLine(s > 2)
            Console.WriteLine(s < 2)
            Dim e As Object = Make("1e3")
            Console.WriteLine(e = 1000)
            Dim m As Object = Make("-7")
            Console.WriteLine(m < 0)
            Dim lit As Object = "2.5"
            Console.WriteLine(lit = 2.5)
            Console.WriteLine(lit > 2)
        End Sub
        Sub Main()
            Integers()
            Doubles()
        End Sub
        """;

    /// <summary>vbc's output for <see cref="StringAgainstNumber"/>.</summary>
    internal const string StringAgainstNumberVb = "True\nFalse\nTrue\nTrue\nTrue\nFalse\nTrue\nTrue\nFalse\nTrue\nTrue\nFalse\nTrue\nTrue\nTrue\nTrue";

    /// <summary>P04 — an Object holding True against 1 and -1 (True is -1, never +1), 0 and an Object False; the number 1 against True; and an Object holding the String "True" / "false" / "1" against a Boolean (VB converts by ToBoolean). M3.</summary>
    internal const string BooleanAgainstNumberAndString = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub WithNumbers()
            Dim t As Object = Make(True)
            Dim f As Object = Make(False)
            Console.WriteLine(t = 1)
            Console.WriteLine(t = -1)
            Console.WriteLine(t < 0)
            Console.WriteLine(f = 0)
            Console.WriteLine(f > -1)
            Console.WriteLine(t = f)
            Dim one As Object = Make(1)
            Console.WriteLine(one = True)
            Dim m1 As Object = Make(-1)
            Console.WriteLine(m1 = True)
            Dim lit As Object = True
            Console.WriteLine(lit = -1)
        End Sub
        Sub WithStrings()
            Dim s As Object = Make("True")
            Console.WriteLine(s = True)
            Console.WriteLine(s <> True)
            Dim f As Object = Make("false")
            Console.WriteLine(f = False)
            Dim b As Object = Make(True)
            Console.WriteLine(b = "True")
            Dim one As Object = Make("1")
            Console.WriteLine(one = True)
        End Sub
        Sub Main()
            WithNumbers()
            WithStrings()
        End Sub
        """;

    /// <summary>vbc's output for <see cref="BooleanAgainstNumberAndString"/>.</summary>
    internal const string BooleanAgainstNumberAndStringVb = "False\nTrue\nTrue\nTrue\nTrue\nFalse\nFalse\nTrue\nTrue\nTrue\nFalse\nTrue\nTrue\nTrue";

    /// <summary>P05 — an Object holding "abc" or "" against a number, and "abc" `&lt;` 20, and "maybe" against True, THROW with VB's own message ('Double' for a number, 'Boolean' for True); "&amp;H10" is 16. Every catch is a plain `Exception`: the program NEVER names InvalidCastException, so the class has to exist because the helper throws it (`JsExceptionTypes`), or the throw is a ReferenceError and the message is wrong. M6.</summary>
    internal const string NotANumberThrows = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Main()
            Dim s As Object = Make("abc")
            Try
                Console.WriteLine(s = 20)
                Console.WriteLine("no-throw")
            Catch ex As Exception
                Console.WriteLine("caught")
                Console.WriteLine(ex.Message)
            End Try
            Dim e As Object = Make("")
            Try
                Console.WriteLine(e = 0)
                Console.WriteLine("no-throw")
            Catch ex As Exception
                Console.WriteLine("caught")
                Console.WriteLine(ex.Message)
            End Try
            Try
                Console.WriteLine(s < 20)
                Console.WriteLine("no-throw")
            Catch ex As Exception
                Console.WriteLine(ex.Message)
            End Try
            Dim t As Object = Make("maybe")
            Try
                Console.WriteLine(t = True)
                Console.WriteLine("no-throw")
            Catch ex As Exception
                Console.WriteLine(ex.Message)
            End Try
            Dim h As Object = Make("&H10")
            Console.WriteLine(h = 16)
        End Sub
        """;

    /// <summary>vbc's output for <see cref="NotANumberThrows"/>.</summary>
    internal const string NotANumberThrowsVb = "caught\nConversion from string \"abc\" to type 'Double' is not valid.\ncaught\nConversion from string \"\" to type 'Double' is not valid.\nConversion from string \"abc\" to type 'Double' is not valid.\nConversion from string \"maybe\" to type 'Boolean' is not valid.\nTrue";

    /// <summary>P06 — Object against Object compares the VALUES: an Integer and a Double, two Strings built at run time, two Nothings, Nothing against "", "20" against 20 in both orders, True against -1.</summary>
    internal const string ObjectAgainstObject = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Main()
            Dim a As Object = Make(5)
            Dim b As Object = Make(5.0)
            Dim c As Object = Make(6)
            Console.WriteLine(a = b)
            Console.WriteLine(a = c)
            Console.WriteLine(a <> c)
            Dim t As String = "ab"
            t = t & "c"
            Dim s1 As Object = Make("abc")
            Dim s2 As Object = Make(t)
            Dim s3 As Object = Make("abd")
            Console.WriteLine(s1 = s2)
            Console.WriteLine(s1 = s3)
            Dim n1 As Object = Make(Nothing)
            Dim n2 As Object = Make(Nothing)
            Console.WriteLine(n1 = n2)
            Dim e As Object = Make("")
            Console.WriteLine(n1 = e)
            Dim twenty As Object = Make("20")
            Dim num As Object = Make(20)
            Console.WriteLine(twenty = num)
            Console.WriteLine(num = twenty)
            Dim tr As Object = Make(True)
            Dim m1 As Object = Make(-1)
            Console.WriteLine(tr = m1)
        End Sub
        """;

    /// <summary>vbc's output for <see cref="ObjectAgainstObject"/>.</summary>
    internal const string ObjectAgainstObjectVb = "True\nFalse\nTrue\nTrue\nFalse\nTrue\nTrue\nTrue\nTrue\nTrue";

    /// <summary>P07 — Select Case on an Object: a value (`Case 20` meets "20"), a range (`1 To 5` meets "3" and " 4 "), `Case Is &gt; 100`, `Case Is &gt; 6 When o &lt; limit`, `Case Nothing` (meets 0) and, on Strings, an ORDINAL `Case Is &lt; "B"` ("A" is below it, "a" and "b" are not) beside `Case "a"`. vbc has no `When`: its twin (S/t215/tw/vb) is an If chain with `AndAlso`. M2.</summary>
    internal const string SelectCaseOnAnObject = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Test(o As Object, limit As Integer)
            Select Case o
                Case 20
                    Console.WriteLine("twenty")
                Case 1 To 5
                    Console.WriteLine("one-five")
                Case Is > 100
                    Console.WriteLine("big")
                Case Is > 6 When o < limit
                    Console.WriteLine("guarded")
                Case Nothing
                    Console.WriteLine("nothing")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub TestStr(o As Object)
            Select Case o
                Case Is < "B"
                    Console.WriteLine("below-B")
                Case "a"
                    Console.WriteLine("a")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub Main()
            Test(Make("20"), 50)
            Test(Make("3"), 50)
            Test(Make(" 4 "), 50)
            Test(Make("200"), 50)
            Test(Make("10"), 50)
            Test(Make("60"), 50)
            Test(Make(0), 50)
            Test(Make(True), 50)
            TestStr(Make("a"))
            TestStr(Make("A"))
            TestStr(Make("10"))
            TestStr(Make("b"))
        End Sub
        """;

    /// <summary>vbc's output for <see cref="SelectCaseOnAnObject"/>.</summary>
    internal const string SelectCaseOnAnObjectVb = "twenty\none-five\none-five\nbig\nguarded\nelse\nnothing\nelse\na\nbelow-B\nbelow-B\nelse";

    /// <summary>P08 — the ONLY late-bound comparison in the program is the `When o = 20` guard of a Select Case on an INTEGER subject. The helper is emitted ahead of every body, so the scan that decides to emit it must see a guard (it is built with emission suppressed, so in no block). vbc has no `When`: its twin is `If i &gt; 0 AndAlso o = 20`. M4.</summary>
    internal const string ComparisonOnlyInAWhenGuard = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Test(i As Integer, o As Object)
            Select Case i
                Case Is > 0 When o = 20
                    Console.WriteLine("positive-twenty")
                Case Is > 0
                    Console.WriteLine("positive")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub
        Sub Main()
            Test(1, Make("20"))
            Test(1, Make(" 20.0 "))
            Test(1, Make(21))
            Test(0, Make(20))
        End Sub
        """;

    /// <summary>vbc's output for <see cref="ComparisonOnlyInAWhenGuard"/>.</summary>
    internal const string ComparisonOnlyInAWhenGuardVb = "positive-twenty\npositive-twenty\npositive\nother";

    /// <summary>P09 — an Object holding a CLASS INSTANCE compared with a number, with itself and with Nothing THROWS InvalidCastException, as vbc does (JavaScript's `===` answered by reference); the first is caught BY NAME (`Catch ex As InvalidCastException`), so the helper's class is the one the program's own handler discriminates on.</summary>
    internal const string ClassInstance = """
        Class Foo
            Public X As Integer
        End Class
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Main()
            Dim a As Object = Make(New Foo())
            Try
                Console.WriteLine(a = 1)
                Console.WriteLine("no-throw")
            Catch ex As InvalidCastException
                Console.WriteLine("invalid-cast")
            End Try
            Try
                Console.WriteLine(a = a)
                Console.WriteLine("no-throw")
            Catch ex As Exception
                Console.WriteLine("caught")
            End Try
            Try
                Console.WriteLine(a = Nothing)
                Console.WriteLine("no-throw")
            Catch ex As Exception
                Console.WriteLine("caught")
            End Try
        End Sub
        """;

    /// <summary>vbc's output for <see cref="ClassInstance"/>.</summary>
    internal const string ClassInstanceVb = "invalid-cast\ncaught\ncaught";

    /// <summary>P10 — an Object holding NaN: unordered, so `=`, `&lt;` and `&gt;=` are False and `&lt;&gt;` is True, even against itself. M5.</summary>
    internal const string NaN = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Main()
            Dim zero As Double = 0
            Dim n As Object = Make(zero / zero)
            Console.WriteLine(n = n)
            Console.WriteLine(n <> n)
            Console.WriteLine(n < 1)
            Console.WriteLine(n >= 1)
        End Sub
        """;

    /// <summary>vbc's output for <see cref="NaN"/>.</summary>
    internal const string NaNVb = "False\nTrue\nFalse\nFalse";

    /// <summary>P11 — an Object holding Nothing against 1, 0, -1, False, True, "" and "x": Nothing is the other operand's default in the ORDERINGS too (`n &lt; 1`, `n &gt;= 0`, `n &gt; -1`).</summary>
    internal const string NothingOrdering = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub Main()
            Dim n As Object = Make(Nothing)
            Console.WriteLine(n < 1)
            Console.WriteLine(n >= 0)
            Console.WriteLine(n > -1)
            Console.WriteLine(n = False)
            Console.WriteLine(n = True)
            Console.WriteLine(n = "")
            Console.WriteLine(n = "x")
            Console.WriteLine(n = 0)
        End Sub
        """;

    /// <summary>vbc's output for <see cref="NothingOrdering"/>.</summary>
    internal const string NothingOrderingVb = "True\nTrue\nTrue\nTrue\nFalse\nTrue\nFalse\nTrue";

    /// <summary>P12 — CONTROL: typed Integer / String / Double / Boolean comparisons and Select Cases on typed subjects, which keep JavaScript's own operators. No Object anywhere.</summary>
    internal const string TypedControls = """
        Function GetI(v As Integer) As Integer
            Return v
        End Function
        Function GetS(v As String) As String
            Return v
        End Function
        Sub Main()
            Dim i As Integer = GetI(5)
            Dim j As Integer = GetI(7)
            Console.WriteLine(i = 5)
            Console.WriteLine(i < j)
            Console.WriteLine(i <> j)
            Dim s As String = GetS("a")
            Console.WriteLine(s = "a")
            Console.WriteLine(s <> "B")
            Dim d As Double = 2.5
            Console.WriteLine(d > i)
            Dim b As Boolean = True
            Console.WriteLine(b = True)
            Select Case i
                Case 1 To 4
                    Console.WriteLine("low")
                Case Is >= 5
                    Console.WriteLine("high")
            End Select
            Select Case s
                Case "a"
                    Console.WriteLine("is-a")
                Case Else
                    Console.WriteLine("not-a")
            End Select
        End Sub
        """;

    /// <summary>vbc's output for <see cref="TypedControls"/>.</summary>
    internal const string TypedControlsVb = "True\nTrue\nTrue\nTrue\nTrue\nFalse\nTrue\nhigh\nis-a";

    /// <summary>P13 — CONTROL: `Is` / `IsNot` and `Case Is Nothing` on Objects stay reference identity (ADR-0011): an Object holding 0, "" or False is NOT Nothing. vbc has no `Case Is Nothing`: its twin is `If o Is Nothing`.</summary>
    internal const string IdentityControls = """
        Function Make(v As Object) As Object
            Return v
        End Function
        Sub TestIs(o As Object)
            Select Case o
                Case Is Nothing
                    Console.WriteLine("is-nothing")
                Case Else
                    Console.WriteLine("not-nothing")
            End Select
        End Sub
        Sub Main()
            Dim z As Object = Make(0)
            Dim e As Object = Make("")
            Dim f As Object = Make(False)
            Dim n As Object = Make(Nothing)
            Console.WriteLine(z Is Nothing)
            Console.WriteLine(e Is Nothing)
            Console.WriteLine(f IsNot Nothing)
            Console.WriteLine(n Is Nothing)
            Console.WriteLine(n IsNot Nothing)
            TestIs(z)
            TestIs(e)
            TestIs(n)
        End Sub
        """;

    /// <summary>vbc's output for <see cref="IdentityControls"/>.</summary>
    internal const string IdentityControlsVb = "False\nFalse\nTrue\nTrue\nFalse\nnot-nothing\nnot-nothing\nis-nothing";

    // ---- plumbing ----

    /// <summary>The message of a failed assertion without the generated program (which carries the whole helper): everything before the "--- generated JS ---" marker, the first lines only.</summary>
    internal static string Brief(string message)
        => string.Join(" // ", message.Replace("\r\n", "\n").Split("--- generated JS ---")[0].Split('\n').Take(6));

    /// <summary>
    /// ⭐ One program, EVERY JavaScript entry point (the CLI, the CLI with <c>--optimize</c>, <c>CompileProjectFiles</c>, <c>RunJs</c>, <c>RunOptimized</c>), each printing vbc's answer. The Node check comes first and outside
    /// any multiple-assertion block, because NUnit fails an Ignore inside one. A leg that fails to compile, crashes or prints something else is COLLECTED, not thrown, so every other leg still reports and the message names it.
    /// </summary>
    private static void AssertPrintsVbcsAnswer(string source, string vb)
    {
        TempExec.RequireTool(Bk.JavaScript);
        var want = TempExec.Norm(vb);
        var failures = new List<string>();

        void Leg(string leg, Func<string> run)
        {
            try
            {
                var got = TempExec.Norm(run());
                if (got != want)
                    failures.Add($"{leg}: printed [{got.Replace("\n", " | ")}] where vbc prints [{want.Replace("\n", " | ")}]");
            }
            catch (Exception ex) when (ex is not (IgnoreException or InconclusiveException or SuccessException))
            {
                // A refused compile, a crash, a Node exit code: the first lines name it.
                failures.Add($"{leg}: {Brief(ex.Message)}");
            }
        }

        foreach (var entry in Enum.GetValues<EntryPoint>())
            Leg($"JavaScript {entry}", () => TempExec.Run(Bk.JavaScript, entry, source));
        Leg("JavaScript in process, no optimizer (RunJs)", () => JavaScriptExecutionTests.RunJs(source));
        Leg("JavaScript in process, standard passes (RunOptimized)", () => JavaScriptOptimizedExecutionTests.RunOptimized(source));

        Assert.That(failures, Is.Empty, "on JavaScript:\n" + string.Join("\n", failures));
    }

    // ---- an Object against Nothing ----

    /// <summary>
    /// `= Nothing` / `&lt;&gt; Nothing` on an Object is a VALUE comparison: Nothing is the other operand's default, so an Object holding 0, "" or False IS `= Nothing`. An Object holding Nothing against 1, 0, -1, False, True, ""
    /// and "x" is the same rule in the orderings. `Case Nothing` meets those values, `Case Is Nothing` does not (identity). Before: `===`, so none of them was Nothing.
    /// </summary>
    [TestCase(NothingEquals, NothingEqualsVb, TestName = "ObjectEqualsNothing_IsAValueComparison_ZeroEmptyAndFalseAreNothing")]
    [TestCase(NothingOrdering, NothingOrderingVb, TestName = "AnObjectHoldingNothing_IsTheOtherOperandsDefault_InTheOrderingsToo")]
    [TestCase(CaseNothing, CaseNothingVb, TestName = "CaseNothing_IsAValueComparison_CaseIsNothing_IsIdentity")]
    public void AnObjectAgainstNothing_PrintsVbcsAnswer_OnJavaScript(string source, string vb)
        => AssertPrintsVbcsAnswer(source, vb);

    // ---- an Object against a number, a Boolean, another Object ----

    /// <summary>
    /// An Object holding a String against an Integer and a Double (`=` `&lt;&gt;` `&lt;` `&gt;`, either side); True is -1 and "True" is a Boolean; Object against Object compares the values. Before: every `=` was `===`, so a String
    /// never met a number and True met 1.
    /// </summary>
    [TestCase(StringAgainstNumber, StringAgainstNumberVb, TestName = "AnObjectString_IsConvertedLikeANumber_AgainstAnIntegerAndADouble")]
    [TestCase(BooleanAgainstNumberAndString, BooleanAgainstNumberAndStringVb, TestName = "AnObjectBoolean_IsMinusOne_AndAStringTrueIsABoolean")]
    [TestCase(ObjectAgainstObject, ObjectAgainstObjectVb, TestName = "ObjectAgainstObject_ComparesTheValues")]
    public void AnObjectAgainstAValue_PrintsVbcsAnswer_OnJavaScript(string source, string vb)
        => AssertPrintsVbcsAnswer(source, vb);

    // ---- what throws, and what is unordered ----

    /// <summary>
    /// "abc" and "" against a number THROW InvalidCastException with VB's message, "maybe" against True throws; a class instance THROWS on every comparison; a NaN is unordered, even against itself. Before: a silent False for
    /// the first two groups. The NaN row is not a before-bug (JavaScript's own operators answer a NaN the VB way): it guards the helper's own NaN handling.
    /// </summary>
    [TestCase(NotANumberThrows, NotANumberThrowsVb, TestName = "AnObjectString_ThatIsNoNumber_ThrowsInvalidCast_AsVbcDoes")]
    [TestCase(ClassInstance, ClassInstanceVb, TestName = "AnObjectHoldingAClassInstance_ThrowsInvalidCast_OnEveryComparison")]
    [TestCase(NaN, NaNVb, TestName = "ANaN_IsUnordered_EvenAgainstItself")]
    public void WhatThrowsAndWhatIsUnordered_PrintsVbcsAnswer_OnJavaScript(string source, string vb)
        => AssertPrintsVbcsAnswer(source, vb);

    // ---- Select Case, and the helper's own prelude scan ----

    /// <summary>
    /// Select Case on an Object: a value, a range, `Case Is op`, a guard, `Case Nothing`, and an ordinal String `Case Is &lt;`; and a comparison ONLY inside a When guard, the one use the prelude scan reaches through a Select
    /// Case label's guard rather than through a block.
    /// </summary>
    [TestCase(SelectCaseOnAnObject, SelectCaseOnAnObjectVb, TestName = "SelectCase_OnAnObject_ValueRangeIsOperatorAndNothing_AreLateBound")]
    [TestCase(ComparisonOnlyInAWhenGuard, ComparisonOnlyInAWhenGuardVb, TestName = "AComparisonOnlyInAWhenGuard_StillMakesTheHelperAvailable")]
    public void SelectCaseAndGuards_PrintVbcsAnswer_OnJavaScript(string source, string vb)
        => AssertPrintsVbcsAnswer(source, vb);

    // ---- the controls ----

    /// <summary>
    /// CONTROL: typed Integer / String / Double / Boolean comparisons and typed Select Cases print what they printed. CONTROL: `Is` / `IsNot` / `Case Is Nothing` stay IDENTITY, so an Object holding 0, "" or False is NOT
    /// Nothing. A rule that turned `Is` into a value comparison fails the second.
    /// </summary>
    [TestCase(TypedControls, TypedControlsVb, TestName = "Control_TypedComparisonsAreUnchanged")]
    [TestCase(IdentityControls, IdentityControlsVb, TestName = "Control_IsIsNotAndCaseIsNothing_StayIdentity")]
    public void TheControls_StayAsTheyWere_OnJavaScript(string source, string vb)
        => AssertPrintsVbcsAnswer(source, vb);
}

/// <summary>
/// #215 SHAPE — the fast half. Nothing spawns: the JavaScript the compiler writes, with no optimizer and with the standard passes. The runs above say the helper answers right; this says it is THERE only when needed — the web-form
/// output shares this prelude, so a program with no Object comparison must be byte-for-byte what it was.
/// </summary>
[TestFixture]
public class JavaScriptLateBoundComparisonShapeTests
{
    private const string Helper = "__blCompareObject";

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// A program with NO Object comparison emits no <c>__blCompareObject</c> and no InvalidCastException class — a typed program (whose comparisons stay native: `i === 5`, `s === "a"`, `i !== j`) and an Object program
    /// that uses only IDENTITY (`Is`, `IsNot`, `Case Is Nothing`) — while a program that makes ONE Object comparison defines the helper exactly once, ahead of its first call, and names InvalidCastException. The positive half
    /// is what stops "no helper anywhere" passing the negative half.
    /// </summary>
    [Test]
    public void ThePreludeStaysAdditive_NoHelperWithoutAnObjectComparison_OneDefinitionWithIt()
    {
        var typed = JavaScriptLateBoundComparisonExecutionTests.TypedControls;
        var identity = JavaScriptLateBoundComparisonExecutionTests.IdentityControls;
        var late = JavaScriptLateBoundComparisonExecutionTests.NothingEquals;

        Assert.Multiple(() =>
        {
            foreach (var (pipeline, compile) in new (string, Func<string, string>)[]
            {
                ("no optimizer", s => JsTestSupport.Compile(s)),
                ("standard passes", s => JsTestSupport.CompileOptimized(s)),
            })
            {
                var typedJs = compile(typed);
                Assert.That(typedJs, Does.Not.Contain(Helper), $"a typed program ({pipeline})");
                Assert.That(typedJs, Does.Not.Contain("InvalidCastException"), $"a typed program ({pipeline})");
                Assert.That(typedJs, Does.Contain("i === 5").And.Contain("i !== j").And.Contain("s === \"a\""), $"typed comparisons stay JavaScript's own ({pipeline})");

                var identityJs = compile(identity);
                Assert.That(identityJs, Does.Not.Contain(Helper), $"Is / IsNot / Case Is Nothing on Objects ({pipeline})");
                Assert.That(identityJs, Does.Not.Contain("InvalidCastException"), $"Is / IsNot / Case Is Nothing on Objects ({pipeline})");

                var lateJs = compile(late);
                Assert.That(Count(lateJs, "function " + Helper + "("), Is.EqualTo(1), $"one definition ({pipeline})");
                Assert.That(lateJs, Does.Contain(Helper + "("), $"and a call ({pipeline})");
                Assert.That(lateJs.IndexOf("function " + Helper + "(", StringComparison.Ordinal),
                    Is.GreaterThanOrEqualTo(0).And.LessThan(lateJs.IndexOf("function Main", StringComparison.Ordinal)),
                    $"the definition precedes the program ({pipeline})");
                Assert.That(Count(lateJs, "class InvalidCastException"), Is.EqualTo(1), $"the helper's exception class exists ({pipeline})");
            }
        });
    }
}
