using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #196 (ADR-0011 D3(c)) -- a C++ `BasicLang::Array<T>` has a REAL null state. RUN, against vbc, through every entry point. C++ only.
//
//  THE BUG. The array handle's default constructor ALLOCATED, so Nothing, an unsized `Dim a() As Integer` and an array field never assigned were all an EMPTY array: `a.Length` printed 0 and `For Each`
//  ran zero times where .NET throws NullReferenceException, `a(0)` was an out-of-bounds read, and because ADR-0011 D3 made the C++ null test read EMPTINESS, an empty array (`{}`, `New Integer() {}`, a
//  Function result, a zero-argument ParamArray, a Structure field, a List element) was `Is Nothing` while `a Is b` on two unsized arrays was False. Now a default-constructed handle owns no storage and
//  is Nothing, every member that reaches the storage throws a NetException with NullReferenceException's chain, `is_nothing()` is the null test (`CppCodeGenerator.EmitNullTest`'s array arm, shared by
//  `Is Nothing` / `IsNot Nothing` and `Case Is Nothing`), `ReDim Preserve` of a Nothing array preserves nothing, and an empty array literal inside a `When` guard is spelled from an empty std::vector.
//
//  THE ORACLE IS vbc. Each row's expected text is what the SDK's vbc prints for the program wrapped in a VB Module (S/t196/tw/rows, the `.exp` beside each `.bas`; S/t136/tools/vbv2.py), never what C++
//  printed. vbc has no `Case Is Nothing` or `When`, so R05 and R11 take the answer of their `If x Is Nothing` / `If n > 0 AndAlso ...` equivalents. A row is one TestCase: one program, each Sub one
//  probe's body, run through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (TempExec.AssertMatchesInEveryEntryPoint). The cells SKIP, never fail, where no C++
//  compiler is installed. The fixture is C++ only: C# printed vbc's answer on every probe behind these rows before #196 and prints it now, bar the two gaps below; JavaScript and MSIL differ from vbc on several of them (an unsized array, `ReDim`, `UBound`) for causes of their own, not #196's (S/t196/mat-after.txt).
//
//  KNOWN GAPS -- each is NOT #196's, listed with NO test (asserting one would pin the defect):
//    A11  `UBound(a)` of a Nothing array throws NullReferenceException where VB's Information.UBound throws ArgumentNullException. The C# backend gives the same answer.
//    A15  `Dim d(-1) As Integer` is folded to the same size as `Dim d() As Integer`, so d is Nothing; VB makes an EMPTY array. A front-end issue on every backend (C# and MSIL answer the same), task #284.
//    ---  the C++ String `Is Nothing` is still EMPTINESS (ADR-0011 D3): `"" Is Nothing` is True on C++ and False elsewhere. IsIsNotOperatorExecutionTests pins that divergence, which now covers the String only.
//    ---  jagged arrays (`Integer()()`) are refused by the front end, so no row can hold one.
//
//  MUTANTS (S/t196/tw/mut: the fix plus ONE change, built from a plain source copy and run against a copy of the test output with its BasicLang.dll swapped; each is killed by a committed test):
//    M1  the array null test reads emptiness again (`(x).empty()`):         R01 R03 R05 R06 R07 R09 R10 R11 (every row that asks `Is Nothing` of an array; an empty array is Nothing again and a Nothing array throws)
//    M2  `ReDim Preserve` of a Nothing array touches its storage:           R02
//    M3  the empty guard literal is spelled with empty braces again:        R11
//
//  Named "...ExecutionTests" but it runs no JavaScript: it is in JsExecutionTierRosterTests.NotJavaScriptExecution, NOT the roster, and its case count is not in the roster's pin.
// ================================================================================================

/// <summary>
/// #196 RUN: an unsized or `Nothing` C++ array is Nothing (and throws on every member), an empty one is not, through the CLI, `--optimize` and `CompileProjectFiles`.
/// </summary>

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class CppArrayNothingExecutionTests
{
    // ------------------------------------------------------------------------------------------------
    // The programs. Each is the implementer's probes (S/t196/probes/A01..A18) grouped by kind; each Sub is one probe's body, so a
    // group's expected output is the concatenation of its probes' vbc answers.
    // ------------------------------------------------------------------------------------------------

    // R01: an unsized `Dim` (Integer() and String()) is Nothing, `IsNot` agrees, and `a = Nothing` makes a Nothing while an alias taken before keeps the array.
    private const string R01 = """
        Sub Unsized()
            Dim a() As Integer
            Console.WriteLine(a Is Nothing)
            Console.WriteLine(a IsNot Nothing)
            Dim s As String()
            Console.WriteLine(s Is Nothing)
        End Sub

        Sub Assigned()
            Dim a() As Integer = {1, 2, 3}
            Dim b() As Integer = a
            Console.WriteLine(a Is Nothing)
            a = Nothing
            Console.WriteLine(a Is Nothing)
            Console.WriteLine(b Is Nothing)
            Console.WriteLine(b.Length)
            If a Is Nothing Then Console.WriteLine("null")
        End Sub

        Sub Main()
            Unsized()
            Assigned()
        End Sub
        """;
    private const string R01_Exp = "True\nFalse\nTrue\nFalse\nTrue\nFalse\n3\nnull";

    // R02: `ReDim` of an unsized array allocates; `ReDim Preserve` of one preserves nothing and allocates, then keeps what was written across the next Preserve (M2).
    private const string R02 = """
        Sub Main()
            Dim a() As Integer
            ReDim a(3)
            Console.WriteLine(a Is Nothing)
            Console.WriteLine(a.Length)
            Dim c() As Integer
            ReDim Preserve c(2)
            Console.WriteLine(c Is Nothing)
            Console.WriteLine(c.Length)
            c(1) = 7
            ReDim Preserve c(4)
            Console.WriteLine(c(1) & " " & c.Length)
        End Sub
        """;
    private const string R02_Exp = "False\n4\nFalse\n3\n7 5";

    // R03: `{}` and `New Integer() {}` are arrays of length 0 and NOT Nothing (the row ADR-0011 D3 used to pin the other way; M1).
    private const string R03 = """
        Sub Main()
            Dim b() As Integer = {}
            Console.WriteLine(b Is Nothing)
            Console.WriteLine(b.Length)
            Dim c As Integer() = New Integer() {}
            Console.WriteLine(c Is Nothing)
            Console.WriteLine(c.Length)
        End Sub
        """;
    private const string R03_Exp = "False\n0\nFalse\n0";

    // R04: `.Length`, `For Each` and an indexed read and write of a Nothing array throw, and a typed `Catch ex As NullReferenceException` sees each; `For Each` over an EMPTY array runs zero times.
    private const string R04 = """
        Sub LengthOf()
            Dim a() As Integer
            Try
                Console.WriteLine(a.Length)
                Console.WriteLine("no throw")
            Catch ex As NullReferenceException
                Console.WriteLine("Length NullReferenceException")
            Catch ex As Exception
                Console.WriteLine("Length other")
            End Try
            Dim b() As Integer = {4, 5}
            b = Nothing
            Try
                Console.WriteLine(b.Length)
                Console.WriteLine("no throw")
            Catch ex As NullReferenceException
                Console.WriteLine("Length-after-Nothing NullReferenceException")
            End Try
        End Sub

        Sub EachOver()
            Dim a() As Integer
            Try
                For Each v As Integer In a
                    Console.WriteLine(v)
                Next
                Console.WriteLine("no throw")
            Catch ex As NullReferenceException
                Console.WriteLine("For Each NullReferenceException")
            Catch ex As Exception
                Console.WriteLine("For Each other")
            End Try
            Dim e() As Integer = {}
            For Each v As Integer In e
                Console.WriteLine(v)
            Next
            Console.WriteLine("empty ok")
        End Sub

        Sub IndexOf()
            Dim a() As Integer
            Try
                Console.WriteLine(a(0))
                Console.WriteLine("no throw")
            Catch ex As NullReferenceException
                Console.WriteLine("read NullReferenceException")
            Catch ex As Exception
                Console.WriteLine("read other")
            End Try
            Try
                a(0) = 5
                Console.WriteLine("no throw")
            Catch ex As NullReferenceException
                Console.WriteLine("write NullReferenceException")
            Catch ex As Exception
                Console.WriteLine("write other")
            End Try
        End Sub

        Sub Main()
            LengthOf()
            EachOver()
            IndexOf()
        End Sub
        """;
    private const string R04_Exp = "Length NullReferenceException\nLength-after-Nothing NullReferenceException\nFor Each NullReferenceException\nempty ok\nread NullReferenceException\nwrite NullReferenceException";

    // R05: `Select Case x` / `Case Is Nothing` on an unsized, an empty, a filled and a re-Nothinged array - the other consumer of the one `EmitNullTest` helper.
    private const string R05 = """
        Sub Check(label As String, x As Integer())
            Select Case x
                Case Is Nothing
                    Console.WriteLine(label & ":null")
                Case Else
                    Console.WriteLine(label & ":set")
            End Select
        End Sub

        Sub Main()
            Dim x() As Integer
            Check("a", x)
            x = New Integer() {}
            Check("b", x)
            x = New Integer() {1, 2}
            Check("c", x)
            x = Nothing
            Check("d", x)
        End Sub
        """;
    private const string R05_Exp = "a:null\nb:set\nc:set\nd:null";

    // R06: a class field `Items()` / `Names As String()` is Nothing until assigned; a SIZED field `Cells(3)` and a sized global `G(2)` are arrays; an unsized global is Nothing until its `ReDim`.
    private const string R06 = """
        Class Holder
            Public Items() As Integer
            Public Names As String()
        End Class

        Class Grid
            Public Cells(3) As Integer
            Public Spare() As Integer
        End Class

        Dim G(2) As Integer
        Dim U() As Integer

        Sub Main()
            Dim h As New Holder()
            Console.WriteLine(h.Items Is Nothing)
            Console.WriteLine(h.Names Is Nothing)
            h.Items = New Integer() {1}
            Console.WriteLine(h.Items Is Nothing)
            Console.WriteLine(h.Items.Length)
            Dim g1 As New Grid()
            g1.Cells(1) = 5
            Console.WriteLine(g1.Cells.Length & " " & g1.Cells(1))
            Console.WriteLine(g1.Spare Is Nothing)
            G(2) = 8
            Console.WriteLine(G.Length & " " & G(2))
            Console.WriteLine(U Is Nothing)
            ReDim U(1)
            Console.WriteLine(U Is Nothing)
            Console.WriteLine(U.Length)
        End Sub
        """;
    private const string R06_Exp = "True\nTrue\nFalse\n1\n4 5\nTrue\n3 8\nTrue\nFalse\n2";

    // R07: a Function returning Nothing, an empty and a filled array, and an array passed as a parameter (`Nothing`, empty, filled, an unsized local).
    private const string R07 = """
        Function GetNone() As Integer()
            Return Nothing
        End Function

        Function GetEmpty() As Integer()
            Return New Integer() {}
        End Function

        Function GetSome() As Integer()
            Return New Integer() {1, 2}
        End Function

        Sub Show(label As String, x As Integer())
            If x Is Nothing Then
                Console.WriteLine(label & " Nothing")
            Else
                Console.WriteLine(label & " " & x.Length)
            End If
        End Sub

        Sub Main()
            Console.WriteLine(GetNone() Is Nothing)
            Console.WriteLine(GetEmpty() Is Nothing)
            Console.WriteLine(GetSome() Is Nothing)
            Dim r() As Integer = GetNone()
            Console.WriteLine(r Is Nothing)
            Show("a", Nothing)
            Show("b", New Integer() {})
            Show("c", New Integer() {5, 6})
            Dim u() As Integer
            Show("d", u)
        End Sub
        """;
    private const string R07_Exp = "True\nFalse\nFalse\nTrue\na Nothing\nb 0\nc 2\nd Nothing";

    // R08: `a Is b` on two unsized arrays is True (two Nothings), False once one is an empty array, False for two distinct empty arrays, True for an alias.
    private const string R08 = """
        Sub Main()
            Dim a() As Integer
            Dim b() As Integer
            Console.WriteLine(a Is b)
            a = New Integer() {}
            Console.WriteLine(a Is b)
            b = New Integer() {}
            Console.WriteLine(a Is b)
            b = a
            Console.WriteLine(a Is b)
        End Sub
        """;
    private const string R08_Exp = "True\nFalse\nFalse\nTrue";

    // R09: an unsized rank-2 array (`m(,)`) and an array field of a Structure are Nothing, then an empty array in that field is not.
    private const string R09 = """
        Structure Bag
            Public Items() As Integer
        End Structure

        Sub Main()
            Dim m(,) As Integer
            Console.WriteLine(m Is Nothing)
            Dim b As Bag
            Console.WriteLine(b.Items Is Nothing)
            b.Items = New Integer() {}
            Console.WriteLine(b.Items Is Nothing)
        End Sub
        """;
    private const string R09_Exp = "True\nTrue\nFalse";

    // R10: a `List(Of Integer())` holding a Nothing array and an empty one keeps them apart.
    private const string R10 = """
        Sub Main()
            Dim l As New List(Of Integer())
            Dim none() As Integer
            l.Add(none)
            l.Add(New Integer() {})
            Console.WriteLine(l(0) Is Nothing)
            Console.WriteLine(l(1) Is Nothing)
        End Sub
        """;
    private const string R10_Exp = "True\nFalse";

    // R11: an empty array literal and a zero-argument ParamArray inside a `When` guard, which `RenderInline` spells itself, are arrays and not Nothing (M3); `CountAll()` prints 0, not -1.
    private const string R11 = """
        Function CountAll(ParamArray xs() As Integer) As Integer
            If xs Is Nothing Then Return -1
            Return xs.Length
        End Function

        Sub Main()
            Dim n As Integer = 3
            Select Case n
                Case Is > 0 When CountAll() = 0
                    Console.WriteLine("zero args")
                Case Else
                    Console.WriteLine("else")
            End Select
            Select Case n
                Case Is > 0 When CountAll(New Integer() {}) = 0
                    Console.WriteLine("empty literal")
                Case Else
                    Console.WriteLine("else")
            End Select
            Console.WriteLine(CountAll())
        End Sub
        """;
    private const string R11_Exp = "zero args\nempty literal\n0";

    private static IEnumerable<TestCaseData> Rows()
    {
        yield return new TestCaseData(R01, R01_Exp, "R01").SetName("R01_UnsizedDim_AndAssignedNothing_AreNothing");
        yield return new TestCaseData(R02, R02_Exp, "R02").SetName("R02_ReDim_AndReDimPreserve_OfANothingArray_M2");
        yield return new TestCaseData(R03, R03_Exp, "R03").SetName("R03_EmptyArray_IsNotNothing_M1");
        yield return new TestCaseData(R04, R04_Exp, "R04").SetName("R04_NothingArray_LengthForEachAndIndex_ThrowNullReference");
        yield return new TestCaseData(R05, R05_Exp, "R05").SetName("R05_CaseIsNothing_OnAnArray");
        yield return new TestCaseData(R06, R06_Exp, "R06").SetName("R06_ArrayFieldsAndGlobals_SizedAndUnsized");
        yield return new TestCaseData(R07, R07_Exp, "R07").SetName("R07_FunctionResultAndParameter_NothingVsEmptyVsFilled");
        yield return new TestCaseData(R08, R08_Exp, "R08").SetName("R08_Identity_TwoNothingArraysAreTheSameReference");
        yield return new TestCaseData(R09, R09_Exp, "R09").SetName("R09_Rank2Array_AndStructureArrayField");
        yield return new TestCaseData(R10, R10_Exp, "R10").SetName("R10_ListOfArrays_NothingElementVsEmptyElement");
        yield return new TestCaseData(R11, R11_Exp, "R11").SetName("R11_EmptyGuardLiteral_AndZeroArgParamArray_AreNotNothing_M3");
    }

    /// <summary>
    /// One row, one program, three entry points: the spawned CLI (standard passes), the spawned CLI with <c>--optimize</c> (aggressive passes) and
    /// <c>BasicCompiler.CompileProjectFiles</c> (aggressive, what a Release .blproj build and the IDE call). Each is compiled by a real C++ compiler,
    /// run, and must print vbc's answer. Every failing entry point is reported, not only the first (<see cref="TempExec.AssertMatchesInEveryEntryPoint"/>).
    /// </summary>
    [TestCaseSource(nameof(Rows))]
    public void ArrayNothing_PrintsVbcsAnswer_OnCpp_ThroughCliCliOptimizeAndProjectBuild(string source, string expected, string row)
        => TempExec.AssertMatchesInEveryEntryPoint(Bk.Cpp, source, expected, row);
}
