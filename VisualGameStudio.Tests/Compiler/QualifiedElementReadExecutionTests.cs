using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  #204 — `b.Items(0)`, a paren element read through ANY receiver, lowers like the bare `Items(0)`.
//
//  WHAT WAS WRONG. A List, Dictionary or array FIELD (or property, Shared field, Module variable) indexed with VB's
//  paren syntax through a QUALIFIED receiver — `b.Items(0)`, `Make().Items(0)`, `Me.Box.Items(i)`, `MyBase.Items(1)` —
//  was typed by the analyzer as an element read and lowered by the IR builder as an INSTANCE METHOD CALL on the
//  receiver: CS1955 on C#, "b.Items is not a function" on JavaScript, MissingMethodException on MSIL, a call of a
//  shared_ptr on C++. The bare spelling `Items(0)` inside the class ran everywhere. The member arm's one exception was
//  an ARRAY member, indexed by its own type test — which ran the receiver TWICE (`Make().Arr(1)` called Make twice) and
//  ignored callability (`b.MakeArr(3)`, a METHOD returning an array, became `b.MakeArr[3]`).
//
//  THE FIX is one decision and one lowering: `SemanticAnalyzer.IsParenElementRead` exposes the decision the analyzer
//  already records (#267's `_elementReadCalls`), and `IRBuilder.TryEmitElementRead` is the lowering the bare callee
//  already had, now shared by the qualified one, with the receiver evaluated once.
//
//  THE ORACLE IS vbc. Every expected value below is the `.exp` of the program wrapped in a VB Module and run with vbc
//  (S/t204/tw/probes, merged from the implementer's probes P01-P14 and Q15-Q28 in S/t204/probes and probes2). A row that
//  merges two probes runs each in the order named, and its expectation is the answers one after the other. No backend is
//  the oracle.
//
//  HOW A ROW RUNS. Each row is one program run on all four backends (C#, C++, JavaScript, MSIL), each through the real
//  CLI, the CLI with --optimize and BasicCompiler.CompileProjectFiles with OptimizeAggressive
//  (`TempExec.AssertMatchesInEveryEntryPoint`, C# through CSharpProcessRunner in a child process with a time limit). A
//  backend whose tool is missing (no C++ compiler, no Node, no ilasm) is SKIPPED, never failed: the row still runs the
//  others, a failure on any of them fails the row, and a row that skipped a leg ends Ignored with the legs named, so it
//  cannot read as a green run of a leg that never happened.
//
//  MUTANTS (S/t204/tools/mut204.py rebuilds each from the fix; each is killed by the rows named):
//    M1  the qualified callee never takes the element-read lowering (the gate is `false`; the old array arm stays
//        deleted)                                   -> P01_P02, P04, P07 (and every other row that indexes a member)
//    M2  the member arm decides by TYPE alone, not by the analyzer's decision, so a METHOD returning an array is indexed
//                                                   -> Q21 (`b.MakeArr(3)` becomes `b.MakeArr[3]`)
//    M3  the receiver is evaluated once before the element read and again inside it
//                                                   -> Q15 (`Make().Arr(1)` runs Make twice: "8 2" where VB prints "8 1")
//    M4  the old type-tested member ARRAY arm is kept beside the new gate
//                                                   -> Q21 (the same `b.MakeArr[3]`: the old arm ignores callability)
//    M5  the accessor admits the explicit `.Item(i)` property too — SURVIVES, by design: an `.Item` member node is not
//        typed indexable, so TryEmitElementRead declines and the old path lowers it. The admission clause is defensive
//        and nothing observable depends on it; the Controls row pins that `.Item(i)` still runs.
//
//  KNOWN GAPS — listed here, deliberately NOT tested (none is #204; each fails identically BEFORE and AFTER the fix):
//    - P12: a String paren index, `Dim c As Char = b.Name(1)`, is refused ("Cannot assign value of type 'String' to
//      variable of type 'Char'") for a bare local `s(1)` too.
//    - Q25: `s.Split(","c)` is read by the analyzer as an array index ("Array index must be an integer type, got
//      'Char'"): a .NET method returning an array is typed as one, and nothing arbitrates.
//    - Q26 / Q27: a With block leaves `__with` undeclared for ANY member (`.Items(0)` as well as `.Items.Count`):
//      CS0103 on C#, an undeclared identifier on C++.
//
//  ⚠ Named "…ExecutionTests" on purpose: it RUNS under Node, so it is in JsExecutionTierRosterTests' roster.
// ================================================================================================

/// <summary>
/// #204 RUN: a paren element read through a qualified receiver — a List, a Dictionary, a List(Of Action), an array, through
/// `Me.` / `MyBase.` / a Shared field / an inherited field / a property / a Module variable / a nested receiver / a call —
/// prints vbc's answer on C#, C++, JavaScript and MSIL, through every entry point, and a method that returns an array stays a call.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the JavaScript legs, the C++ compiles and the C# child runs share the machine with the spawned CLI
public class QualifiedElementReadExecutionTests
{
    // ---- the programs (vbc's answer beside each) --------------------------------------------------

    /// <summary>P01 + P02 + Q20 — a List(Of Integer) and a List(Of String) member READ into a local, in arithmetic, a Print, a `.Length`, a call argument and a comparison. ⛔ Kills M1.</summary>
    private const string P01P02 = """
        Class Board
            Public Items As List(Of Integer)
            Public Names As List(Of String)
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(10)
                Items.Add(20)
                Names = New List(Of String)()
                Names.Add("alpha")
                Names.Add("beta")
            End Sub
        End Class

        Function Twice(n As Integer) As Integer
            Return n * 2
        End Function

        Sub Main()
            Dim b As New Board()
            Dim x As Integer = b.Items(0)
            Dim y As Integer = b.Items(1) + 1
            Console.WriteLine(x)
            Console.WriteLine(y)
            Console.WriteLine(b.Items(1))
            Dim s As String = b.Names(1)
            Console.WriteLine(s)
            Console.WriteLine(b.Names(0) & "!")
            Console.WriteLine(b.Names(1).Length)
            Console.WriteLine(Twice(b.Items(1)))
            If b.Items(0) < b.Items(1) Then
                Console.WriteLine("lt")
            End If
        End Sub
        """;

    private const string P01P02Expected = "10\n21\n20\nbeta\nalpha!\n4\n40\nlt";

    /// <summary>
    /// P03 + Q22 — a List member WRITTEN (`b.Items(0) = 5`, `b.Items(1) = b.Items(1) + 40`) and compound-assigned (`b.Items(0) += 10`), and an array member
    /// compound-assigned (`b.Arr(1) += 30`). A write already lowered to a store: it failed only on the READ half of its own statement.
    /// </summary>
    private const string P03Q22 = """
        Class Board
            Public Items As List(Of Integer)
            Public Arr() As Integer
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(1)
                Items.Add(2)
                Arr = New Integer() {2, 3}
            End Sub
            Public Sub Show()
                Console.WriteLine(Items(0) & " " & Items(1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            b.Items(0) = 5
            b.Items(1) = b.Items(1) + 40
            b.Show()
            b.Items(0) += 10
            b.Arr(1) += 30
            Console.WriteLine(b.Items(0))
            Console.WriteLine(b.Arr(1))
        End Sub
        """;

    private const string P03Q22Expected = "5 42\n15\n33";

    /// <summary>P05 — a Dictionary(Of String, Integer) member read, written and compound-assigned by key.</summary>
    private const string P05 = """
        Class Board
            Public Dict As Dictionary(Of String, Integer)
            Public Sub New()
                Dict = New Dictionary(Of String, Integer)()
                Dict.Add("k", 3)
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            Dim x As Integer = b.Dict("k")
            Console.WriteLine(x)
            b.Dict("k") = 33
            Console.WriteLine(b.Dict("k"))
            b.Dict("k") += 2
            Console.WriteLine(b.Dict("k"))
        End Sub
        """;

    private const string P05Expected = "3\n33\n35";

    /// <summary>P04 — an ARRAY member read and written. The one shape the old member arm DID lower, so it is also the control for "the arm that replaced it still indexes". ⛔ Kills M1.</summary>
    private const string P04 = """
        Class Board
            Public Arr() As Integer
            Public Sub New()
                Arr = New Integer() {7, 8, 9}
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            Dim x As Integer = b.Arr(1)
            Console.WriteLine(x)
            b.Arr(2) = 90
            Console.WriteLine(b.Arr(2))
        End Sub
        """;

    private const string P04Expected = "8\n90";

    /// <summary>P07 — a List(Of Action) member: the element copied into a typed local and called, then `b.Items(1)()` (an element INVOKED). ⛔ Kills M1.</summary>
    private const string P07 = """
        Class Board
            Public Items As List(Of Action)
            Public Sub New()
                Items = New List(Of Action)()
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            b.Items.Add(Sub() Console.WriteLine("one"))
            b.Items.Add(Sub() Console.WriteLine("two"))
            Dim a As Action = b.Items(0)
            a()
            b.Items(1)()
        End Sub
        """;

    private const string P07Expected = "one\ntwo";

    /// <summary>P08 — a computed index (`b.Items(i + 1)`) and a loop variable index (`b.Items(j)` summed over `b.Items.Count`).</summary>
    private const string P08 = """
        Class Board
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
                For k As Integer = 1 To 5
                    Items.Add(k * k)
                Next
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            Dim i As Integer = 2
            Dim x As Integer = b.Items(i + 1)
            Console.WriteLine(x)
            Dim total As Integer = 0
            For j As Integer = 0 To b.Items.Count - 1
                total = total + b.Items(j)
            Next
            Console.WriteLine(total)
        End Sub
        """;

    private const string P08Expected = "16\n55";

    /// <summary>
    /// Q15 — a CALL as the receiver: `Make().Arr(1)` and `Make().Items(1)` must call Make ONCE each. The old array arm visited the receiver and then
    /// visited it again through the member, so the array row printed "8 2". ⛔ Kills M3.
    /// </summary>
    private const string Q15 = """
        Class Board
            Public Arr() As Integer
            Public Items As List(Of Integer)
            Public Sub New()
                Arr = New Integer() {7, 8, 9}
                Items = New List(Of Integer)()
                Items.Add(70)
                Items.Add(80)
            End Sub
        End Class

        Dim calls As Integer = 0

        Function Make() As Board
            calls = calls + 1
            Return New Board()
        End Function

        Sub Main()
            Dim x As Integer = Make().Arr(1)
            Console.WriteLine(x & " " & calls)
            Dim y As Integer = Make().Items(1)
            Console.WriteLine(y & " " & calls)
        End Sub
        """;

    private const string Q15Expected = "8 1\n80 2";

    /// <summary>
    /// Q21 — a METHOD that returns an array (and one that returns a List), called with an argument: `b.MakeArr(3)` is a CALL, not an index into what
    /// the method returns. The old arm tested the member's TYPE (an array) and ignored that it is a procedure, so it emitted `b.MakeArr[3]`.
    /// ⛔ Kills M2 and M4.
    /// </summary>
    private const string Q21 = """
        Class Board
            Public Function MakeArr(n As Integer) As Integer()
                Dim a() As Integer = New Integer() {n * 10, 0, 0, 0}
                Return a
            End Function
            Public Function MakeList(n As Integer) As List(Of Integer)
                Dim l As New List(Of Integer)()
                l.Add(n * 100)
                Return l
            End Function
        End Class

        Sub Main()
            Dim b As New Board()
            Dim a() As Integer = b.MakeArr(3)
            Console.WriteLine(a(0) & " " & a.Length)
            Dim l As List(Of Integer) = b.MakeList(4)
            Console.WriteLine(l(0) & " " & l.Count)
        End Sub
        """;

    private const string Q21Expected = "30 4\n400 1";

    /// <summary>P14 + Q23 — `Me.Items(1)` inside the declaring class and `MyBase.Items(1)` from a derived class.</summary>
    private const string P14Q23 = """
        Class Board
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(4)
                Items.Add(9)
            End Sub
            Public Function Second() As Integer
                Return Me.Items(1)
            End Function
        End Class

        Class BaseBoard
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(12)
                Items.Add(13)
            End Sub
        End Class

        Class Derived
            Inherits BaseBoard
            Public Function Peek() As Integer
                Return MyBase.Items(1)
            End Function
        End Class

        Sub Main()
            Dim b As New Board()
            Console.WriteLine(b.Second())
            Dim d As New Derived()
            Console.WriteLine(d.Peek())
        End Sub
        """;

    private const string P14Q23Expected = "9\n13";

    /// <summary>Q17 + Q24 — a Shared List field (`Registry.Names(1)`), and a Module's List and array variables (`Helpers.Items(0)`, `Helpers.Arr(2)`), read through the TYPE name.</summary>
    private const string Q17Q24 = """
        Class Registry
            Public Shared Names As List(Of String)
        End Class

        Module Helpers
            Public Items As List(Of Integer)
            Public Arr() As Integer
        End Module

        Sub Main()
            Registry.Names = New List(Of String)()
            Registry.Names.Add("zero")
            Registry.Names.Add("one")
            Dim s As String = Registry.Names(1)
            Console.WriteLine(s)
            Helpers.Items = New List(Of Integer)()
            Helpers.Arr = New Integer() {4, 5, 6}
            Helpers.Items.Add(77)
            Dim x As Integer = Helpers.Items(0)
            Dim y As Integer = Helpers.Arr(2)
            Console.WriteLine(x & " " & y)
        End Sub
        """;

    private const string Q17Q24Expected = "one\n77 6";

    /// <summary>Q18 + Q16 — a List field INHERITED from a base class (`b.Items(0)` on the derived), and a ReadOnly List PROPERTY (`h.Items(1)`).</summary>
    private const string Q18Q16 = """
        Class BaseBoard
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(100)
            End Sub
        End Class

        Class Board
            Inherits BaseBoard
        End Class

        Class Shelf
            Private _items As List(Of Integer)
            Public Sub New()
                _items = New List(Of Integer)()
                _items.Add(31)
                _items.Add(32)
            End Sub
            Public ReadOnly Property Items As List(Of Integer)
                Get
                    Return _items
                End Get
            End Property
        End Class

        Sub Main()
            Dim b As New Board()
            Dim x As Integer = b.Items(0)
            Console.WriteLine(x)
            Dim h As New Shelf()
            Dim y As Integer = h.Items(1)
            Console.WriteLine(y)
        End Sub
        """;

    private const string Q18Q16Expected = "100\n32";

    /// <summary>P13 + Q19 — a NESTED receiver (`o.Box.Items(0)`, `Me.Box.Items(1)`) and a list of lists (`o.Grid(0)(1)`, `o.Grid(0).Count`).</summary>
    private const string P13Q19 = """
        Class Inner
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(7)
                Items.Add(8)
            End Sub
        End Class

        Class Outer
            Public Box As Inner
            Public Grid As List(Of List(Of Integer))
            Public Sub New()
                Box = New Inner()
                Grid = New List(Of List(Of Integer))()
                Dim row As New List(Of Integer)()
                row.Add(1)
                row.Add(2)
                Grid.Add(row)
            End Sub
            Public Function Peek() As Integer
                Return Me.Box.Items(1)
            End Function
        End Class

        Sub Main()
            Dim o As New Outer()
            Dim x As Integer = o.Box.Items(0)
            Console.WriteLine(x)
            Console.WriteLine(o.Peek())
            Dim y As Integer = o.Grid(0)(1)
            Console.WriteLine(y)
            Console.WriteLine(o.Grid(0).Count)
        End Sub
        """;

    private const string P13Q19Expected = "7\n8\n2\n2";

    /// <summary>
    /// The CONTROLS (P09 + P10 + Q28 + P11): shapes that were right before and must not move — a member's own `.Count` and `.Contains`, the explicit
    /// `.Item(2)` property on a member List, `b.Grid.Item(0)` then indexed and counted, and the BARE `Items(0)` / `Items(1) = ...` inside the declaring class.
    /// </summary>
    private const string Controls = """
        Class Board
            Public Items As List(Of Integer)
            Public Grid As List(Of List(Of Integer))
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(1)
                Items.Add(2)
                Items.Add(3)
                Grid = New List(Of List(Of Integer))()
                Dim row As New List(Of Integer)()
                row.Add(3)
                row.Add(4)
                Grid.Add(row)
            End Sub
            Public Function First() As Integer
                Return Items(0)
            End Function
            Public Sub Bump()
                Items(1) = Items(1) + 100
                Console.WriteLine(Items(1))
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            Dim n As Integer = b.Items.Count
            Console.WriteLine(n)
            Console.WriteLine(b.Items.Contains(2))
            Dim x As Integer = b.Items.Item(2)
            Console.WriteLine(x)
            Dim r As List(Of Integer) = b.Grid.Item(0)
            Console.WriteLine(r(1))
            Console.WriteLine(b.Grid.Item(0).Count)
            Console.WriteLine(b.First())
            b.Bump()
        End Sub
        """;

    private const string ControlsExpected = "3\nTrue\n3\n4\n2\n1\n102";

    // ---- the rows ---------------------------------------------------------------------------------

    /// <summary>
    /// Every program, on every backend, through every entry point, printing vbc's answer. Before #204 each of these failed on all four backends
    /// (CS1955 on C#, "is not a function" on JavaScript, MissingMethodException on MSIL, a call of a shared_ptr on C++; Q21 was CS0021 on C#)
    /// except P04 (an array member, which the old member arm did lower) and the Controls row, which ran.
    /// </summary>
    [TestCase(P01P02, P01P02Expected, TestName = "P01_P02_ListMemberRead_IntegerAndString_InArithmeticArgumentsAndComparison")]
    [TestCase(P03Q22, P03Q22Expected, TestName = "P03_Q22_ListMemberWrite_AndCompoundAssign_ListAndArray")]
    [TestCase(P05, P05Expected, TestName = "P05_DictionaryMember_ReadWriteAndCompoundAssign")]
    [TestCase(P04, P04Expected, TestName = "P04_ArrayMember_ReadAndWrite_StillIndexed")]
    [TestCase(P07, P07Expected, TestName = "P07_ListOfActionMember_ReadIntoALocal_AndInvoked")]
    [TestCase(P08, P08Expected, TestName = "P08_ComputedAndLoopIndex_OnAListMember")]
    [TestCase(Q15, Q15Expected, TestName = "Q15_CallAsReceiver_IsEvaluatedOnce_ArrayAndList")]
    [TestCase(Q21, Q21Expected, TestName = "Q21_MethodReturningAnArray_StaysACall_NotAnIndex")]
    [TestCase(P14Q23, P14Q23Expected, TestName = "P14_Q23_MeAndMyBaseReceivers")]
    [TestCase(Q17Q24, Q17Q24Expected, TestName = "Q17_Q24_SharedFieldAndModuleVariableReceivers")]
    [TestCase(Q18Q16, Q18Q16Expected, TestName = "Q18_Q16_InheritedFieldAndPropertyReceivers")]
    [TestCase(P13Q19, P13Q19Expected, TestName = "P13_Q19_NestedReceiverAndListOfLists")]
    [TestCase(Controls, ControlsExpected, TestName = "Controls_Count_Item_GridItem_AndBareIndex_StayUnchanged")]
    public void AParenElementRead_ThroughAQualifiedReceiver_PrintsVbcsAnswer_OnEveryBackend(string source, string expected)
        => OnEveryBackend(TestContext.CurrentContext.Test.Name, (backend, label) => TempExec.AssertMatchesInEveryEntryPoint(backend, source, expected, label, hangSafe: true));

    /// <summary>
    /// Runs <paramref name="leg"/> once per backend. A failure on any backend fails the row (after every backend has been tried);
    /// a backend whose tool is missing is skipped by the existing gates and named; a row that skipped any leg ends Ignored, after
    /// its failures have been thrown, so a skip can never hide one.
    /// </summary>
    private static void OnEveryBackend(string label, Action<Bk, string> leg)
    {
        var failures = new List<string>();
        var skipped = new List<string>();
        var ran = new List<string>();
        foreach (var backend in TempExec.Backends(Bk.All))
        {
            try
            {
                leg(backend, label);
                ran.Add(backend.ToString());
            }
            catch (IgnoreException ex)
            {
                skipped.Add($"{backend} ({ex.Message})");
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (skipped.Count > 0)
            Assert.Ignore($"ran and passed on [{string.Join(", ", ran)}]; skipped: {string.Join("; ", skipped)}");
    }
}

/// <summary>
/// #204, the text half, FAST (no process spawned, in the fast subset): the C# for `b.Items(0)` is an INDEXER, `b.Items[0]`, never a call,
/// on the standard passes and the optimizer's. Before #204 it was `b.Items(0)`, which csc refuses (CS1955). ⛔ Kills M1 without running anything.
/// </summary>
[TestFixture]
public class QualifiedElementReadEmissionTests
{
    private const string Source = """
        Class Board
            Public Items As List(Of Integer)
            Public Sub New()
                Items = New List(Of Integer)()
                Items.Add(10)
            End Sub
        End Class

        Sub Main()
            Dim b As New Board()
            Dim x As Integer = b.Items(0)
            Console.WriteLine(x)
        End Sub
        """;

    [Test]
    public void TheCSharpForAQualifiedParenRead_IsAnIndexer_NotACall_OnTheStandardAndTheOptimizedRoute()
    {
        Assert.Multiple(() =>
        {
            var standard = CSharpTestSupport.CompileToCSharp(Source);
            Assert.That(standard, Does.Contain("b.Items[0]"), "standard route: an element read");
            Assert.That(standard, Does.Not.Contain("b.Items(0)"), "standard route: never a method call on the receiver");

            var optimized = CSharpTestSupport.CompileToCSharpOptimized(Source);
            Assert.That(optimized, Does.Contain("b.Items[0]"), "optimized route: an element read");
            Assert.That(optimized, Does.Not.Contain("b.Items(0)"), "optimized route: never a method call on the receiver");
        });
    }
}
