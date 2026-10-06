using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #211, RUN. C#: a comparison with a statically Object operand is VB's LATE-BOUND comparison (ADR-0012), as MSIL's already was (#177).
//
//  ⛔ THE BUG. The C# backend emitted C#'s own operator for `o = 20`, `o < 10`, `Select Case o : Case Is <> 50`. On `object` and a number that is CS0019, and a String relational `Case Is < "m"` was CS8781. Where it
//  DID compile it answered a different question, from a green build: `object == object` and `object == string` are the REFERENCE comparison (two boxed 5s, or a String built at run time and an equal String,
//  compared unequal); `o = Nothing` was a null test (False for an Object holding 0 or ""); and `Select Case o : Case 1 To 5` became a relational pattern that TYPE-tests (an Object holding 3.5 answered Case Else).
//  The fix is in `CSharpBackend`: `IsLateBoundComparison` (the mirror of MSIL's: either operand statically Object, the Nothing literal excluded) makes `CompareText` emit
//  `Microsoft.VisualBasic.CompilerServices.Operators.ConditionalCompareObject{Equal,NotEqual,Less,LessEqual,Greater,GreaterEqual}(a, b, false)`, and a Select Case label with such a comparison (a value, a range bound,
//  `Case Is op`, an Or alternative, `Case Nothing` on an Object subject) becomes `case var _caseN when <test> [&& (guard)]:`. `Is` / `IsNot` and `Case Is Nothing` stay reference identity (ADR-0011).
//  ⚠ An Object holding a CLASS INSTANCE compared with `=` now THROWS InvalidCastException, as vbc and MSIL do; it used to answer by reference.
//
//  ⭐ THE ORACLE IS vbc. Each probe's `Vb` is what the SDK's vbc prints for the program wrapped in a VB Module (S/t211/probes, the `.exp` beside each `.bas`, re-run through vbc by the test-writer: every answer matched).
//  Three probes use syntax VB does not have, so their vbc run is a VB-legal TWIN, never the probe itself: `Case Is Nothing` and the `When` guard (L08, L09, X07: the twin is an If chain, S/t211/tw/vb and
//  S/t211/vbonly) and `Case 1 Or 2` (BasicLang's alternatives pattern; vbc reads `1 Or 2` as the bitwise value 3, so the twin is the comma list `Case 1, 2`, which is what the pattern means).
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"): every probe goes through the real CLI (standard passes), the real CLI with `--optimize` (aggressive) and
//  `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (what a Release .blproj and the IDE call), on C#: `TempExec.Run`. ⛔ Every C# RUN is HANG-SAFE (`CSharpProcessRunner`, a child process with a time limit).
//  The ten programs C# used to refuse are ALSO run, through the standard and aggressive emitters, by `MsilObjectBoxingExecutionTests.ObjectComparison_CompilesAndRunsOnCSharp_AsVbcAnswers_Task211` (the moved pins).
//  The FAST half is `CSharpLateBoundComparisonShapeTests` below: what the compiler WRITES (no process), the control that typed comparisons stay native, and the Nothing literal's own rule.
//
//  ⛔ Needs `Microsoft.VisualBasic.Core` among the in-process compile's references: `FourBackends.EmittedCSharpReferences` adds it by type. Without it the product's output ran right and the harness said CS0234.
//
//  ⭐ MUTANTS (each is the fix plus ONE change, built from a plain source copy of the fix outside the worktree and run against a copy of the test output with its BasicLang.dll swapped; the cases that go red, measured):
//    M1 no Select Case branch (every label keeps C#'s own patterns)   -> `CaseRange_…` (X08 prints "other" for 3.5), `AWhenGuard_…` (X07), `CaseNothing_…` (L08), `ANestedSelectCase_…` (3.5 meets `3 To 4`), `AStringSubject_…`
//                                                                          (CS0266), and the moved pins L07 (CS0019), L08, L10 (CS8781). Eight cases, nothing else.
//    M2 `Case Nothing` kept as identity (`case null`)                -> `CaseNothing_…` and the moved pin L08 ONLY (an Object holding 0, "" or False answers `something`).
//    M3 only the LEFT operand is asked (`5 < o`, `i = o`, `s = o`)     -> `ARunTimeString_…` (`s = o` is False), `AStringSubject_…`, `AnObjectOnTheRight_…` (`i = o` CS0019), `NotEqualAndLessThan_…` (X03's `s = o`), and the
//                                                                          moved pin L04 (`5 < o` CS0019). Five cases.
//    M4 the Nothing literal counts as an Object operand              -> `TheNothingLiteral_NeverMakesAStringComparisonLateBound` ONLY, and only because it is a TEXT test: every VB-legal shape the literal reaches (a String against
//                                                                          Nothing as a call result or an inlined If condition) answers the same through the late-bound call. The shape that shows M4 at run time, `f = Nothing` on a
//                                                                          class, is BC30452 in vbc and so is asserted nowhere.
//    M5 a late-bound label drops its When guard                      -> `AWhenGuard_…` (X07 prints "small" for 10, 50), `CaseNothing_…` (L08's Guard) and the moved pin L08.
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or a defect that is another task's). Each is the same before and after #211:
//    * L11b (`Dim s As Object = "20" : s = 20`) WAS a known gap here (False | True where vbc prints True | False, on every backend: copy propagation replaced the Object with its String constant, so no late-bound
//      call was ever emitted — #214). FIXED by `CopyPropagationPass.KeepsLateBinding`: C# now prints vbc's answer, asserted by `ObjectComparisonUnderOptimizerExecutionTests` (every entry point) and the moved pin
//      `MsilObjectBoxingExecutionTests.L11b_…`. Still open there, on JavaScript only: its own `===` on an Object (#215).
//    * An operand the ANALYZER mistypes as Object also goes late-bound, exactly as on MSIL: an Enum member typed to a local (`c = Color.Red`, n19) is IR-typed Object (the front-end gap behind #277). It runs right
//      (`Nothing_is_the_default_of_a_type_parameter_an_Enum_and_a_tuple_on_CSharp` runs it), but through the VB runtime rather than as an integer compare.
//    * JavaScript's `= Nothing`, `Case Nothing` and boxed `Is` disagree with VB (#215); this fixture is C# only.
//    * C++ has no Object at all ("Object has no C++ mapping"), so there is nothing to compare there.
//
//  ⚠ Named "…ExecutionTests" but it spawns no Node: it is C# only, so it is listed in JsExecutionTierRosterTests.NotJavaScriptExecution, and NOT in the roster.
// ================================================================================================

/// <summary>The #211 probe programs and vbc's answer for each. C# only; every run is hang-safe.</summary>
internal static class LateBoundProbes
{
    private static TempProbe P(string id, string source, string vb) => new(id, source, vb, Bk.CSharp, HangSafe: true);

    /// <summary>X04 — Object = Object. Two boxed 5s (not the same reference), a 5 and a 6, and two Strings built at run time (not interned): VB compares VALUES.</summary>
    internal static readonly TempProbe X04 = P("X04_object_equals_object", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeS(t As String) As Object
            Return t
        End Function
        Sub Main()
            Dim a As Object = Make(5)
            Dim b As Object = Make(5)
            Dim c As Object = Make(6)
            Console.WriteLine(a = b)
            Console.WriteLine(a <> b)
            Console.WriteLine(a = c)
            Dim t As String = "h"
            t = t & "i"
            Dim s1 As Object = MakeS("hi")
            Dim s2 As Object = MakeS(t)
            Console.WriteLine(s1 = s2)
            Console.WriteLine(s1 <> s2)
        End Sub
        """, "True\nFalse\nFalse\nTrue\nFalse");

    /// <summary>X05 — `o = Nothing` with o holding 0 is a VALUE comparison (Nothing is the other operand's default), not a null test; a 3 is not; and a typed Integer `i = Nothing` is unchanged.</summary>
    internal static readonly TempProbe X05 = P("X05_object_equals_nothing", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim o As Object = Make(0)
            If o = Nothing Then
                Console.WriteLine("eq-nothing")
            Else
                Console.WriteLine("ne-nothing")
            End If
            Console.WriteLine(o = Nothing)
            Console.WriteLine(o <> Nothing)
            Dim p As Object = Make(3)
            Console.WriteLine(p = Nothing)
            Dim i As Integer = 0
            Console.WriteLine(i = Nothing)
        End Sub
        """, "eq-nothing\nTrue\nFalse\nFalse\nTrue");

    /// <summary>X06 — `Is` / `IsNot` against Nothing and against another reference are IDENTITY: an Object holding 0 is not Nothing, and two boxed 1s are not the same object.</summary>
    internal static readonly TempProbe X06 = P("X06_is_stays_identity", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeN() As Object
            Return Nothing
        End Function
        Sub Main()
            Dim o As Object = Make(0)
            Console.WriteLine(o Is Nothing)
            Console.WriteLine(o IsNot Nothing)
            Dim n As Object = MakeN()
            Console.WriteLine(n Is Nothing)
            Dim a As Object = Make(1)
            Dim b As Object = Make(1)
            Console.WriteLine(a Is b)
            Dim c As Object = a
            Console.WriteLine(a Is c)
            If o Is Nothing Then
                Console.WriteLine("is-nothing")
            Else
                Console.WriteLine("not-nothing")
            End If
        End Sub
        """, "False\nTrue\nTrue\nFalse\nTrue\nnot-nothing");

    /// <summary>L09 — the same identity rule in a Select Case: `Case Is Nothing` is a null test (vbc has no such label; the twin is `If n Is Nothing`).</summary>
    internal static readonly TempProbe L09 = P("L09_case_is_nothing_stays_identity", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim a As Object = Make(5)
            Dim b As Object = Make(5)
            Dim c As Object = a
            Dim n As Object = Nothing
            Console.WriteLine(a Is b)
            Console.WriteLine(a Is c)
            Console.WriteLine(a IsNot b)
            Console.WriteLine(n Is Nothing)
            Console.WriteLine(a Is Nothing)
            Select Case n
                Case Is Nothing
                    Console.WriteLine("case-is-nothing")
                Case Else
                    Console.WriteLine("case-else")
            End Select
        End Sub
        """, "False\nTrue\nTrue\nTrue\nFalse\ncase-is-nothing");

    /// <summary>X08 — `Case 1 To 5` on an Object holding 3.5: a NUMERIC range (1-5), not a relational type-test pattern (which answered Case Else); 5.5 is outside it, and 7.0 meets `Case 6, 7`.</summary>
    internal static readonly TempProbe X08 = P("X08_case_range_holding_a_double", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Test(o As Object)
            Select Case o
                Case 1 To 5
                    Console.WriteLine("1-5")
                Case 6, 7
                    Console.WriteLine("6-7")
                Case Else
                    Console.WriteLine("other")
            End Select
        End Sub
        Sub Main()
            Test(Make(1))
            Test(Make(5))
            Test(Make(6))
            Test(Make(9))
            Test(3.5)
            Test(5.5)
            Test(7.0)
        End Sub
        """, "1-5\n1-5\n6-7\nother\n1-5\nother\n6-7");

    /// <summary>X11 — an Object holding a String built at run time against a String built another way, on both sides, with `=` and `<>`, and against a literal; and an Object holding "" against Nothing (VB reads Nothing as "").</summary>
    internal static readonly TempProbe X11 = P("X11_runtime_string_against_object_string", """
        Function Build(a As String, b As String) As String
            Return a & b
        End Function
        Function MakeS(t As String) As Object
            Return t
        End Function
        Sub Main()
            Dim o As Object = MakeS(Build("ab", "c"))
            Dim s As String = Build("a", "bc")
            Console.WriteLine(o = s)
            Console.WriteLine(s = o)
            Console.WriteLine(o <> s)
            Console.WriteLine(o = "abc")
            Dim e As Object = MakeS("")
            Console.WriteLine(e = Nothing)
        End Sub
        """, "True\nTrue\nFalse\nTrue\nTrue");

    /// <summary>X10 — an Object holding a CLASS INSTANCE compared with `=` THROWS InvalidCastException, as vbc does (it used to answer by reference); `Is Nothing` on it is identity and answers False.</summary>
    internal static readonly TempProbe X10 = P("X10_class_instance_equals_throws", """
        Class Foo
            Public V As Integer
        End Class
        Function MakeF() As Object
            Return New Foo()
        End Function
        Sub Main()
            Dim o As Object = MakeF()
            Console.WriteLine(o Is Nothing)
            Try
                Console.WriteLine(o = Nothing)
            Catch ex As Exception
                Console.WriteLine("cast-eq-nothing")
            End Try
            Dim p As Object = o
            Try
                Console.WriteLine(o = p)
            Catch ex As Exception
                Console.WriteLine("cast-eq-obj")
            End Try
        End Sub
        """, "False\ncast-eq-nothing\ncast-eq-obj");

    /// <summary>X01 — `o <> 5` on an Object holding 5 and 6 (CS0019 before), in a statement and in an If.</summary>
    internal static readonly TempProbe X01 = P("X01_object_not_equal_number", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim o As Object = Make(5)
            Console.WriteLine(o <> 5)
            Console.WriteLine(o <> 6)
            Dim p As Object = Make(7)
            If p <> 5 Then
                Console.WriteLine("ne")
            End If
        End Sub
        """, "False\nTrue\nne");

    /// <summary>X02 — `o < 2.5` and `o > 2.5` on an Object holding an Integer (2, 3), and on one initialised from a literal: Integer against Double compares NUMERICALLY.</summary>
    internal static readonly TempProbe X02 = P("X02_object_integer_less_than_double", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim o As Object = Make(2)
            Console.WriteLine(o < 2.5)
            Console.WriteLine(o > 2.5)
            Dim q As Object = Make(3)
            Console.WriteLine(q < 2.5)
            Dim k As Object = 2
            Console.WriteLine(k < 2.5)
        End Sub
        """, "True\nFalse\nFalse\nTrue");

    /// <summary>X03 — an Object holding a String built at run time against a String, both orders; an Object holding "10" against the number 10 (VB converts the String late-bound: True); an Object holding "" against Nothing.</summary>
    internal static readonly TempProbe X03 = P("X03_object_string_against_string_and_number", """
        Function Build(a As String, b As String) As String
            Return a & b
        End Function
        Function MakeS(t As String) As Object
            Return t
        End Function
        Sub Main()
            Dim o As Object = MakeS(Build("ab", "c"))
            Dim s As String = Build("a", "bc")
            Console.WriteLine(o = s)
            Console.WriteLine(s = o)
            Console.WriteLine(o <> s)
            Console.WriteLine(o = "abc")
            Dim n As Object = MakeS("10")
            Console.WriteLine(n = 10)
            Dim e As Object = MakeS("")
            Console.WriteLine(e = Nothing)
        End Sub
        """, "True\nTrue\nFalse\nTrue\nTrue\nTrue");

    /// <summary>X07 — a `When` guard on an Object subject: the guard is a late-bound comparison too, and it must be KEPT (a Case that drops it takes every value the first label's pattern meets). 9.5 is a Double.</summary>
    internal static readonly TempProbe X07 = P("X07_when_guard_on_an_object", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Test(o As Object)
            Select Case o
                Case Is >= 0 When o < 10
                    Console.WriteLine("small")
                Case Is >= 0 When o = 10
                    Console.WriteLine("ten")
                Case Is >= 0
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("neg")
            End Select
        End Sub
        Sub Main()
            Test(Make(3))
            Test(Make(10))
            Test(Make(50))
            Test(Make(-4))
            Test(9.5)
        End Sub
        """, "small\nten\nbig\nneg\nsmall");

    /// <summary>
    /// L08 — `Case Nothing` on an Object subject is a VALUE comparison (an Object holding 0, "" or False meets it), `Case Is Nothing` is IDENTITY (only a real Nothing does), and a guard on a `Case Is` is kept. vbc has neither
    /// `Case Is Nothing` nor a guard: its twin is the If chain in S/t211/tw/vb/L08vb.bas, which prints the same twelve lines.
    /// </summary>
    internal static readonly TempProbe L08 = P("L08_case_nothing_value_case_is_nothing_identity", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeN() As Object
            Return Nothing
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
        Sub Guard(o As Object, limit As Integer)
            Select Case o
                Case Is > 0 When o < limit
                    Console.WriteLine("in-range")
                Case Is > 0
                    Console.WriteLine("over")
                Case Else
                    Console.WriteLine("non-positive")
            End Select
        End Sub
        Sub Main()
            Test(Make(0))
            Test(MakeN())
            Test(Make(5))
            Test("")
            Test(False)
            TestIs(Make(0))
            TestIs(MakeN())
            TestIs("")
            TestIs(False)
            Guard(Make(3), 10)
            Guard(Make(30), 10)
            Guard(Make(-1), 10)
        End Sub
        """, "nothing\nnothing\nsomething\nnothing\nnothing\nnot-nothing\nis-nothing\nnot-nothing\nnot-nothing\nin-range\nover\nnon-positive");

    /// <summary>
    /// N-or — a Select Case NESTED in a Select Case, both on Objects, and BasicLang's Or pattern (`Case 1 Or 2`: either alternative; vbc reads `1 Or 2` as the bitwise 3, so its twin is the comma list `Case 1, 2`, S/t211/tw/vb/Nor_vb.bas).
    /// Every label binds its own copy of its subject (`_caseN`): two labels of one section sharing a pattern variable are CS0128, and a nested Select reusing one is CS0136. An alternative with a Double (`1 Or 2.5`) and a range
    /// plus a value (`3 To 4, 11`) are in the mix.
    /// </summary>
    internal static readonly TempProbe NestedOr = P("N_or_nested_select_and_an_or_pattern", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Test(o As Object, p As Object)
            Select Case o
                Case 1 Or 2
                    Select Case p
                        Case 1 Or 2.5
                            Console.WriteLine("o12-p1or25")
                        Case Is > 3
                            Console.WriteLine("o12-pbig")
                        Case Else
                            Console.WriteLine("o12-pelse")
                    End Select
                Case 3 To 4, 11
                    Console.WriteLine("o34-or-big")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        Sub Main()
            Test(Make(1), Make(1))
            Test(Make(2), 2.5)
            Test(Make(2), Make(9))
            Test(Make(1), Make(3))
            Test(3.5, Make(0))
            Test(Make(11), Make(0))
            Test(Make(5), Make(0))
        End Sub
        """, "o12-p1or25\no12-p1or25\no12-pbig\no12-pelse\no34-or-big\no34-or-big\nelse");

    /// <summary>N-str — a STRING subject with an OBJECT Case value (`Select Case s : Case o`): VB compares `s = o` late-bound, so an Object holding the number 10 meets the String "10".</summary>
    internal static readonly TempProbe StringSubject = P("N_str_string_subject_object_case_value", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub TestStr(s As String, o As Object)
            Select Case s
                Case o
                    Console.WriteLine("s-eq-o")
                Case Else
                    Console.WriteLine("s-ne-o")
            End Select
        End Sub
        Sub Main()
            TestStr("x", "x")
            TestStr("10", Make(10))
            TestStr("y", "x")
        End Sub
        """, "s-eq-o\ns-eq-o\ns-ne-o");

    /// <summary>
    /// X09 — the Object on the RIGHT (`i = o`, `o = i`) beside TYPED Integer comparisons that stay native (`i = 20`, `i < j`, `If i <> j`, `Select Case i`). The late-bound decision asks BOTH operands: a rule that asked only the
    /// left kept C#'s operator for `i = o` and was CS0019.
    /// </summary>
    internal static readonly TempProbe X09 = P("X09_object_on_the_right_beside_typed_controls", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Sub Main()
            Dim i As Integer = 20
            Dim j As Integer = 30
            Dim o As Object = Make(20)
            Console.WriteLine(i = 20)
            Console.WriteLine(i < j)
            Console.WriteLine(o = 20)
            Console.WriteLine(o = i)
            Console.WriteLine(i = o)
            If i <> j Then
                Console.WriteLine("i-ne-j")
            End If
            Select Case i
                Case 20
                    Console.WriteLine("i-twenty")
                Case Else
                    Console.WriteLine("i-other")
            End Select
        End Sub
        """, "True\nTrue\nTrue\nTrue\nTrue\ni-ne-j\ni-twenty");

    /// <summary>
    /// L04 — the ordering operators on an Object (`o < 10`, `o >= 8`, `5 < o` with the Object on the right), an If, and two LOOP CONDITIONS (`Do While c < 3`, `While c > 0`): the inlined condition of a loop is rendered through the
    /// same comparison as a statement. Loops, so hang-safe: a late-bound compare that answered wrongly would never leave one.
    /// </summary>
    internal static readonly TempProbe L04 = P("L04_ordering_and_loop_conditions_on_an_object", """
        Function Make(v As Integer) As Object
            Return v
        End Function
        Function MakeD() As Object
            Return 2.5
        End Function
        Sub Main()
            Dim o As Object = Make(7)
            Console.WriteLine(o < 10)
            Console.WriteLine(o > 10)
            Console.WriteLine(o <= 7)
            Console.WriteLine(o >= 8)
            Console.WriteLine(5 < o)
            Console.WriteLine(o < 7)
            Console.WriteLine(o >= 7)
            Console.WriteLine(o > 7)
            Dim d As Object = MakeD()
            Console.WriteLine(d > 2)
            Console.WriteLine(d < 3)
            If o > 5 Then
                Console.WriteLine("gt5")
            End If
            Dim k As Integer = 0
            Dim c As Object = 0
            Do While c < 3
                k = k + 1
                c = k
            Loop
            Console.WriteLine(k)
            While c > 0
                k = k - 1
                c = k
            End While
            Console.WriteLine(k)
        End Sub
        """, "True\nFalse\nTrue\nFalse\nTrue\nFalse\nTrue\nFalse\nTrue\nTrue\ngt5\n3\n0");

    /// <summary>
    /// The Nothing-literal control (M4). Every comparison here is VB-LEGAL and has a String operand and the Nothing literal: a call result and an inlined If condition. The literal is an Object-typed null constant in the IR but has
    /// no type of its own in VB, so it never makes a comparison late-bound: the String keeps #206's `(s ?? "") != ""`. (Behaviourally the late-bound call would answer the same here, which is why this is a TEXT test.)
    /// </summary>
    internal const string StringNothingShapes = """
        Function GetS() As String
            Return "x"
        End Function
        Sub Main()
            Dim s As String = GetS()
            If s <> Nothing Then
                Console.WriteLine("s-set")
            End If
            Console.WriteLine(GetS() = Nothing)
            Console.WriteLine(Nothing = s)
        End Sub
        """;

    /// <summary>
    /// The typed control: ONE Object comparison (`o = 20`) among typed Integer / Double / String comparisons, an If and two Select Cases on typed subjects. Only the Object one may emit a late-bound call, so the call count is
    /// exactly one: a rule that went late-bound for anything else fails it, and a rule that never did fails the count of one.
    /// </summary>
    internal const string TypedBesideOneObject = """
        Function MakeO() As Object
            Return 20
        End Function
        Function MakeI() As Integer
            Return 20
        End Function
        Function MakeD() As Double
            Return 2.5
        End Function
        Function MakeS() As String
            Return "x"
        End Function
        Sub Main()
            Dim o As Object = MakeO()
            Dim i As Integer = MakeI()
            Dim j As Integer = MakeI() + 1
            Dim d As Double = MakeD()
            Dim s As String = MakeS()
            Dim t As String = MakeS() & "y"
            Console.WriteLine(o = 20)
            Console.WriteLine(i = 20)
            Console.WriteLine(i < j)
            Console.WriteLine(d > 2.0)
            Console.WriteLine(s = t)
            Console.WriteLine(s <> "x")
            If i <> j Then
                Console.WriteLine("ne")
            End If
            Select Case i
                Case 1 To 5
                    Console.WriteLine("low")
                Case Is > 100
                    Console.WriteLine("big")
                Case 20
                    Console.WriteLine("twenty")
            End Select
            Select Case s
                Case "x"
                    Console.WriteLine("x")
                Case Else
                    Console.WriteLine("else")
            End Select
        End Sub
        """;
}

/// <summary>#211 — an Object comparison on C# is VB's late-bound comparison, RUN against vbc through every entry point.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the CLI legs spawn dotnet and the runners spawn children; keep the machine to this fixture
public class CSharpLateBoundComparisonExecutionTests
{
    /// <summary>The message of a failed assertion without the emitted program (which can be long): the first lines, up to the "--- emitted ---" marker. A compile failure keeps its Roslyn diagnostic (CS0019, CS8781).</summary>
    private static string Brief(string message)
        => string.Join(" // ", message.Replace("\r\n", "\n").Split('\n').TakeWhile(l => !l.StartsWith("--- emitted")).Take(4));

    /// <summary>
    /// Each probe on C#, through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>: each must print vbc's answer. A failure in one probe or entry point is collected, not thrown, so every other one still
    /// reports and the message names the probe and the entry point.
    /// </summary>
    private static void AssertPrintsVbcsAnswer(params TempProbe[] probes)
    {
        var failures = new List<string>();
        foreach (var probe in probes)
            foreach (var entry in Enum.GetValues<EntryPoint>())
            {
                try
                {
                    var got = TempExec.Norm(TempExec.Run(Bk.CSharp, entry, probe.Source, hangSafe: true));
                    if (got != TempExec.Norm(probe.Vb))
                        failures.Add($"{probe.Id} {entry}: printed [{got.Replace("\n", " | ")}] where vbc prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
                }
                catch (AssertionException ex)
                {
                    failures.Add($"{probe.Id} {entry}: {Brief(ex.Message)}");
                }
            }

        Assert.That(failures, Is.Empty, $"on C#, through {string.Join(", ", Enum.GetValues<EntryPoint>())}:\n" + string.Join("\n", failures));
    }

    /// <summary>(1) Object = Object compares the VALUES: two boxed 5s are equal, a 5 and a 6 are not, and two Strings built at run time are equal. (Before: C#'s reference `==`, which printed False for the first.)</summary>
    [Test]
    public void ObjectEqualsObject_ComparesTheValues_NotTheReferences()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X04);

    /// <summary>(2) `o = Nothing` with o holding 0 is True: a VALUE comparison, not a null test. A 3 is not Nothing; a typed `i = Nothing` is unchanged. (Before: a null test, so False for the 0.)</summary>
    [Test]
    public void ObjectEqualsNothing_IsAValueComparison_AnObjectHoldingZeroIsNothing()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X05);

    /// <summary>(3) `Is` / `IsNot` and `Case Is Nothing` stay reference identity: the 0 is not Nothing, two boxed 1s are not the same object. A rule that made `Is` late-bound would answer True for `o Is Nothing`.</summary>
    [Test]
    public void IsIsNotAndCaseIsNothing_StayReferenceIdentity()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X06, LateBoundProbes.L09);

    /// <summary>(4) `Case 1 To 5` on an Object holding 3.5 is Case 1-5. (Before: a relational pattern that type-tests, so Case Else.) M1 (no Select Case branch) fails it with the others in this group.</summary>
    [Test]
    public void CaseRange_OnAnObjectHoldingADouble_ComparesNumerically()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X08);

    /// <summary>(5) An Object holding a String built at run time equals an equal String, on either side, and `<>` says False. (Before: `object == string` is the reference comparison.) M3 (left operand only) fails the `s = o` line.</summary>
    [Test]
    public void ARunTimeString_EqualsTheSameStringInAnObject_OnEitherSide()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X11);

    /// <summary>(6) An Object holding a class instance compared with `=` THROWS InvalidCastException, as vbc does, instead of answering by reference. The program catches it and prints which comparison threw.</summary>
    [Test]
    public void AClassInstanceComparedWithEquals_ThrowsInvalidCast_AsVbcDoes()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X10);

    /// <summary>(7) `<>` and `<` mix an Object's Integer with Integer and Double operands, an Object String with a String and with a number: all CS0019 before.</summary>
    [Test]
    public void NotEqualAndLessThan_MixIntegerDoubleAndStringOperands()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X01, LateBoundProbes.X02, LateBoundProbes.X03);

    /// <summary>(8) A `When` guard on an Object subject is a late-bound comparison and is kept: M5 (the guard dropped) answers `small` for 10, 50 and 9.5 alike.</summary>
    [Test]
    public void AWhenGuard_OnAnObjectSubject_IsKept()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X07);

    /// <summary>(9) `Case Nothing` on an Object subject is a VALUE comparison (an Object holding 0, "" or False meets it) and `Case Is Nothing` stays identity; a guard on a `Case Is` is kept. M2 (`Case Nothing` as identity) fails the first five lines.</summary>
    [Test]
    public void CaseNothing_IsAValueComparison_WhileCaseIsNothingIsIdentity()
        => AssertPrintsVbcsAnswer(LateBoundProbes.L08);

    /// <summary>(10) A Select Case nested in a Select Case, both on Objects, with BasicLang's Or pattern: every label binds its own `_caseN` (a shared name is CS0128, a nested reuse CS0136).</summary>
    [Test]
    public void ANestedSelectCase_AndAnOrPattern_OnObjects()
        => AssertPrintsVbcsAnswer(LateBoundProbes.NestedOr);

    /// <summary>(11) A String subject with an Object Case value: `Select Case s : Case o` is `s = o` late-bound, so an Object holding 10 meets "10". (Before: CS0266 converting object to string.)</summary>
    [Test]
    public void AStringSubject_AgainstAnObjectCaseValue()
        => AssertPrintsVbcsAnswer(LateBoundProbes.StringSubject);

    /// <summary>
    /// (12) The Object on the RIGHT, beside typed comparisons that stay native (X09), and the ordering operators and two loop conditions on an Object (L04). M3 asks only the left operand: `i = o` and `5 < o` are CS0019.
    /// </summary>
    [Test]
    public void AnObjectOnTheRight_AndInLoopConditions_BesideTypedComparisons()
        => AssertPrintsVbcsAnswer(LateBoundProbes.X09, LateBoundProbes.L04);
}

/// <summary>
/// #211 SHAPE — the fast half. Nothing spawns: the C# the compiler writes, through the standard passes, the aggressive ones and <c>CompileProjectFiles</c>. The CONTROL that a typed comparison emits no late-bound call, and the
/// Nothing literal's own rule (M4), which a run cannot see because the late-bound call answers the same there.
/// </summary>
[TestFixture]
public class CSharpLateBoundComparisonShapeTests
{
    private const string LateBoundCall = "ConditionalCompareObject";

    private static IEnumerable<(string Name, string Text)> Emits(string source)
    {
        yield return ("standard", ReturnCoercionTests.EmitCSharpForTest(source).Replace("\r\n", "\n"));
        yield return ("aggressive", ReturnCoercionTests.EmitCSharpAggressiveForTest(source).Replace("\r\n", "\n"));
        yield return ("project", TempExec.Emit(Bk.CSharp, EntryPoint.ProjectRelease, source).Replace("\r\n", "\n"));
    }

    private static int Count(string text, string needle)
    {
        var n = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// (13) The CONTROL. A typed Integer / Double / String comparison, an If, and a Select Case on a typed subject emit NO late-bound call and no `_case` binding: next to ONE Object comparison (`o = 20`) the emitted C# holds
    /// exactly one <c>ConditionalCompareObjectEqual</c> and nothing else of the kind. Counting one (not "none") is what keeps it from passing on a backend that never goes late-bound.
    /// </summary>
    [Test]
    public void TypedComparisons_EmitNoLateBoundCall_NextToExactlyOneForTheObjectOne()
    {
        var failures = new List<string>();
        foreach (var (name, text) in Emits(LateBoundProbes.TypedBesideOneObject))
        {
            if (Count(text, LateBoundCall) != 1)
                failures.Add($"{name}: {Count(text, LateBoundCall)} late-bound calls where the one Object comparison is the only one:\n{text}");
            else if (Count(text, LateBoundCall + "Equal(") != 1)
                failures.Add($"{name}: the one late-bound call is not ConditionalCompareObjectEqual:\n{text}");
            if (text.Contains("_case")) failures.Add($"{name}: a typed Select Case was given a late-bound `_case` binding:\n{text}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    /// <summary>
    /// (14) M4. The Nothing LITERAL never makes a comparison late-bound: a String against Nothing, as a call result and as an inlined If condition (both VB-legal) keep #206's `?? ""` form. M4 counts the literal as an Object
    /// operand and emits <c>ConditionalCompareObjectEqual(GetS(), null, false)</c> instead. (vbc refuses the shape that would show M4 at run time: `f = Nothing` on a class is BC30452.)
    /// </summary>
    [Test]
    public void TheNothingLiteral_NeverMakesAStringComparisonLateBound()
    {
        var failures = new List<string>();
        foreach (var (name, text) in Emits(LateBoundProbes.StringNothingShapes))
        {
            if (text.Contains(LateBoundCall)) failures.Add($"{name}: the Nothing literal made a comparison late-bound:\n{text}");
            if (!text.Contains("?? \"\"")) failures.Add($"{name}: the String comparison against Nothing is not #206's `?? \"\"` form (the shape did not run):\n{text}");
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}
