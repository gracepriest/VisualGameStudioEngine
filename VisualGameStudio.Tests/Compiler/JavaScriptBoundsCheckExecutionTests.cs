using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  #207 — JavaScript bounds-checks List / array element access and Dictionary reads.
//
//  WHAT WAS WRONG. On JavaScript a List and an array are plain JS arrays and a Dictionary is a Map, and the backend indexed
//  them directly. None of the three checks anything: `l(3)` on a three-element List and `a(3)` on `Dim a(2)` answered
//  `undefined`, `l(5) = 1` and `a(5) = 9` silently GREW the array (to six, holes and all), and `d("missing")` answered
//  `undefined` — where .NET throws ArgumentOutOfRangeException / IndexOutOfRangeException / KeyNotFoundException. So a Try never
//  entered its Catch and the program ran on with a garbage value, or died later with an unrelated TypeError.
//
//  THE FIX (JavaScriptBackend.cs, JsExceptionTypes.cs). Every element access the backend lowers goes through one of five small
//  prelude helpers — __blListGet / __blListSet / __blArrayGet / __blArraySet / __blDictGet — emitted ONLY when the module uses it
//  (an `ElementCheck` flag scan up front, `When` guards included). Out of range each throws the provided .NET exception class with
//  .NET's own message, so a typed Catch matches it and a catch-all's `Exception.Wrap` passes it through;
//  `JsExceptionTypes.CollectRequired` mentions the three classes so even an UNCAUGHT throw finds its class. A Dictionary WRITE stays
//  `Map.set` (insert-or-update, as in .NET). The JS prelude is shared with web-form code-behind, so the change must stay additive:
//  JavaScriptBoundsCheckEmissionTests pins that a program that never indexes emits none of it.
//
//  THE ORACLE. The B-probes are the implementer's (S/t207/probes, probes2), each with the `.exp` it measured with vbc; this file
//  embeds both unchanged. E03, E05, E06 and E10 are the test-writer's own: no vbc runs on this machine, so their expectations follow
//  from VB's rules for a List / array read and are marked as such on the program; E05's `++` / `--` is BasicLang's own operator
//  (C's meaning, #141), not VB's.
//
//  HOW A ROW RUNS. JavaScript only, each program through the real CLI, the CLI with --optimize and BasicCompiler.CompileProjectFiles
//  with OptimizeAggressive (`TempExec.AssertMatchesInEveryEntryPoint`), run under Node. A row that groups several programs runs each
//  in the order named, collects EVERY failure and names the probe, so one wrong program does not hide the next. No Node on the
//  machine: the row is Ignored (TempExec.RequireTool), never failed. B10 dies by design, so it drives `TempExec.Emit` and
//  `JavaScriptExecutionTests.RunNodeScriptForOutcome` itself. Nothing here runs C#: B20 would not (see the gaps).
//
//  MUTANTS (S/t207/mut and tools/mut.py recipes, rebuilt from a plain source copy of the fix; each is killed by the rows named):
//    M1  a List read accepts `i == Count` (`i <= l.length`)       -> ListRead (B01 / B02 / B12), LoopEndedByTheException (B20), MessageText (B11), UncaughtListRead (B10)
//    M2  `When` guards are not scanned (a guard-only read has no helper: refused at build time) -> WhenGuard (B16)
//    M3  the three exception classes are not mentioned (the helper throws a ReferenceError)    -> MessageText (B11), UncaughtListRead (B10), the emission test
//    M4  an array WRITE keeps the plain unchecked `a[i] = v`      -> Array (B06 / B14), Array2D (B13), EdgeSites (B17)
//    M5  a 2-D array READ checks only the last level (`m[2]` unchecked) -> Array2D (B13)
//    M0  (no fix at all: master's compiler) fails 14 of the 15 — only Controls (B09 / B23) passes, as it must.
//
//  KNOWN GAPS — listed here, deliberately NOT tested (none is #207's to fix; each behaves the same BEFORE and AFTER, or is another backend's):
//    - C++: a List read past the end throws `std::out_of_range`, which a typed `Catch ... As ArgumentOutOfRangeException` does not catch,
//      and a C++ array is not bounds-checked at all.
//    - MSIL refuses `Catch` of ArgumentOutOfRangeException / KeyNotFoundException ("not a recognized .NET exception name").
//    - B15: a String paren index, `s(5)`, is emitted as a CALL on JavaScript (`s is not a function`), so the Catch never sees an
//      IndexOutOfRangeException. The index is not an element read to the IR builder.
//    - `a(5) & F()`: F() runs BEFORE the throw, because an element load is read where it is used, not where it is written (#292 family).
//    - An array element whose index is a CALL runs the call TWICE on JavaScript — `a(F())` read, `a(F()) = v` write, `a(F()) += 1` (four
//      times) — where .NET runs it once. Measured identical on the compiler before #207 and after it; a List's `l(F())` read and write run it once.
//    - The C# backend DROPS a conditionless `Do ... Loop` (#293): B20's first loop never loops on C#, which is why this fixture is
//      JavaScript-only.
//
//  ⚠ Named "…ExecutionTests" on purpose: it RUNS under Node, so it is in JsExecutionTierRosterTests' roster (123 = master 121 + #203's 122
//  + this fixture; the integrator resolves the number if #203 lands differently).
// ================================================================================================

/// <summary>
/// #207 RUN: on JavaScript a List / array read or write out of range and a Dictionary read of a missing key throw .NET's exception with
/// .NET's message, through every entry point — typed or catch-all, at <c>Count</c>, negative, in a <c>When</c> guard, in a lambda, through a field,
/// in a 2-D array and a List of arrays — and the in-range accesses are unchanged.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the Node legs and the spawned CLI share the machine
public class JavaScriptBoundsCheckExecutionTests
{
    // ---- the programs (vbc's answer beside each; the E-probes are the test-writer's own) -----------

    /// <summary>B01 — a List(Of Integer) read AT <c>Count</c> (<c>l(3)</c> on three elements) inside a <c>Try</c> with a TYPED <c>Catch ... As ArgumentOutOfRangeException</c> ahead of a catch-all. ⛔ Kills M1.</summary>
    private const string B01 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            l.Add(3)
            Try
                Dim v As Integer = l(3)
                Console.WriteLine("no-throw " & v)
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Console.WriteLine("after")
        End Sub
        """;
    private const string B01Expected = "caught AOORE\nafter";

    /// <summary>B02 — the same read on a one-element List(Of String) (<c>l(1)</c>, again <c>Count</c>) under a catch-all only (no typed Catch to lean on). ⛔ Kills M1.</summary>
    private const string B02 = """
        Sub Main()
            Dim l As New List(Of String)()
            l.Add("a")
            Try
                Console.WriteLine("got " & l(1))
            Catch ex As Exception
                Console.WriteLine("caught Exception")
            End Try
            Console.WriteLine("after")
        End Sub
        """;
    private const string B02Expected = "caught Exception\nafter";

    /// <summary>B03 — a NEGATIVE index (<c>l(-1)</c> through a variable) is out of range too.</summary>
    private const string B03 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(10)
            l.Add(20)
            Dim i As Integer = -1
            Try
                Console.WriteLine("got " & l(i))
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE neg")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Console.WriteLine("after")
        End Sub
        """;
    private const string B03Expected = "caught AOORE neg\nafter";

    /// <summary>B07 — the explicit <c>l.Item(9)</c> spelling past the end, then <c>l.Item(0)</c> in range.</summary>
    private const string B07 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(4)
            Try
                Console.WriteLine("item " & l.Item(9))
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE item")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Console.WriteLine("item0 " & l.Item(0))
        End Sub
        """;
    private const string B07Expected = "caught AOORE item\nitem0 4";

    /// <summary>B12 — the read inside a <c>Function</c> taking the List as a parameter (two of three calls throw), and as a <c>Select Case</c> selector in range. ⛔ Kills M1.</summary>
    private const string B12 = """
        Function Pick(xs As List(Of Integer), i As Integer) As Integer
            Return xs(i)
        End Function

        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(7)
            Dim hits As Integer = 0
            For i As Integer = 0 To 2
                Try
                    Console.WriteLine("v" & i & "=" & Pick(l, i))
                Catch ex As ArgumentOutOfRangeException
                    hits = hits + 1
                End Try
            Next
            Select Case l(0)
                Case 7
                    Console.WriteLine("seven")
                Case Else
                    Console.WriteLine("else")
            End Select
            Console.WriteLine("hits=" & hits)
        End Sub
        """;
    private const string B12Expected = "v0=7\nseven\nhits=2";

    /// <summary>B04 — a List WRITE past the end (<c>l(5) = 1</c>) and AT <c>Count</c> (<c>l(2) = 7</c>) throws and never grows the List: <c>Count</c> stays 2 where the unchecked <c>l[5] = 1</c> made six slots.</summary>
    private const string B04 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            Try
                l(5) = 1
                Console.WriteLine("no-throw")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE write")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Console.WriteLine("count=" & l.Count)
            Try
                l(2) = 7
                Console.WriteLine("no-throw at Count")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE at Count")
            End Try
            Console.WriteLine("count=" & l.Count)
        End Sub
        """;
    private const string B04Expected = "caught AOORE write\ncount=2\ncaught AOORE at Count\ncount=2";

    /// <summary>B05 — an array READ past the end (<c>a(k)</c> with k = 3 on <c>Dim a(2)</c>) and at -1: <c>IndexOutOfRangeException</c>, not the List's class.</summary>
    private const string B05 = """
        Sub Main()
            Dim a(2) As Integer
            a(0) = 5
            Dim k As Integer = 3
            Try
                Dim v As Integer = a(k)
                Console.WriteLine("no-throw " & v)
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE read")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Try
                Console.WriteLine("neg " & a(-1))
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE neg")
            Catch ex As Exception
                Console.WriteLine("caught other neg")
            End Try
            Console.WriteLine("len=" & a.Length)
        End Sub
        """;
    private const string B05Expected = "caught IOORE read\ncaught IOORE neg\nlen=3";

    /// <summary>B06 — an array WRITE past the end (<c>a(k) = 9</c>, k = 5): throws and <c>Length</c> stays 3 (unchecked, <c>a[5] = 9</c> grew it). ⛔ Kills M4.</summary>
    private const string B06 = """
        Sub Main()
            Dim a(2) As Integer
            Dim k As Integer = 5
            Try
                a(k) = 9
                Console.WriteLine("no-throw")
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE write")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Console.WriteLine("len=" & a.Length)
        End Sub
        """;
    private const string B06Expected = "caught IOORE write\nlen=3";

    /// <summary>B14 — an array LITERAL (<c>{1, 2}</c>) read and written at its length, and a MODULE-level <c>Dim g(2)</c> written past the end. ⛔ Kills M4.</summary>
    private const string B14 = """
        Dim g(2) As Integer

        Sub Main()
            Dim a() As Integer = {1, 2}
            Try
                Console.WriteLine("lit " & a(2))
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE lit read")
            End Try
            Try
                a(2) = 3
                Console.WriteLine("no-throw lit write")
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE lit write")
            End Try
            g(1) = 8
            Try
                g(3) = 1
                Console.WriteLine("no-throw global write")
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE global write")
            End Try
            Console.WriteLine("a=" & a.Length & " g1=" & g(1) & " glen=" & g.Length)
        End Sub
        """;
    private const string B14Expected = "caught IOORE lit read\ncaught IOORE lit write\ncaught IOORE global write\na=2 g1=8 glen=3";

    /// <summary>B13 — a 2-D array: <c>m(2, 0)</c> (the FIRST level out) read and <c>m(0, 2)</c> (the LAST level out) written. A check on the last level only lets <c>m[2]</c> be <c>undefined</c> and then dies with a TypeError, not an IndexOutOfRangeException. ⛔ Kills M4 and M5.</summary>
    private const string B13 = """
        Sub Main()
            Dim m(1, 1) As Integer
            m(1, 1) = 4
            Try
                Console.WriteLine("m " & m(2, 0))
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE 2d read")
            Catch ex As Exception
                Console.WriteLine("caught other 2d")
            End Try
            Try
                m(0, 2) = 1
                Console.WriteLine("no-throw 2d write")
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE 2d write")
            Catch ex As Exception
                Console.WriteLine("caught other 2d write")
            End Try
            Console.WriteLine("m11=" & m(1, 1) & " m00=" & m(0, 0))
        End Sub
        """;
    private const string B13Expected = "caught IOORE 2d read\ncaught IOORE 2d write\nm11=4 m00=0";

    /// <summary>B08 — a Dictionary(Of String, Integer) READ of a missing key: <c>KeyNotFoundException</c>; the existing key still reads.</summary>
    private const string B08 = """
        Sub Main()
            Dim d As New Dictionary(Of String, Integer)()
            d("a") = 1
            Try
                Dim v As Integer = d("zz")
                Console.WriteLine("no-throw " & v)
            Catch ex As KeyNotFoundException
                Console.WriteLine("caught KNFE")
            Catch ex As Exception
                Console.WriteLine("caught other")
            End Try
            Console.WriteLine("a=" & d("a"))
        End Sub
        """;
    private const string B08Expected = "caught KNFE\na=1";

    /// <summary>B19 — a Dictionary's message names the key as .NET does (an Integer key <c>'6'</c>, a String key through <c>.Item</c>), a WRITE is still insert-or-update (<c>d("a") = 2</c>, <c>d.Item("b") = 3</c>) and <c>ContainsKey</c> guards a read.</summary>
    private const string B19 = """
        Sub Main()
            Dim d As New Dictionary(Of String, Integer)()
            d("a") = 1
            d("a") = 2
            d.Item("b") = 3
            Console.WriteLine(d("a") & " " & d.Item("b") & " " & d.Count)
            Dim e As New Dictionary(Of Integer, String)()
            e(5) = "five"
            Try
                Console.WriteLine(e(6))
            Catch ex As KeyNotFoundException
                Console.WriteLine("K:" & ex.Message)
            End Try
            Try
                Console.WriteLine(d.Item("q"))
            Catch ex As Exception
                Console.WriteLine("E:" & ex.Message)
            End Try
            If d.ContainsKey("a") Then Console.WriteLine("has a " & d("a"))
        End Sub
        """;
    private const string B19Expected = "2 3 2\nK:The given key '6' was not present in the dictionary.\nE:The given key 'q' was not present in the dictionary.\nhas a 2";

    /// <summary>B11 — <c>ex.Message</c> of a catch-all equals .NET's text for a List (<c>Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')</c>) and for an array (<c>Index was outside the bounds of the array.</c>). A catch-all is the only handler, so the exception CLASS is mentioned by nothing else in the program. ⛔ Kills M1 and M3.</summary>
    private const string B11 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            Dim a(1) As Integer
            Try
                Console.WriteLine(l(0))
            Catch ex As Exception
                Console.WriteLine("L:" & ex.Message)
            End Try
            Try
                Console.WriteLine(a(2))
            Catch ex As Exception
                Console.WriteLine("A:" & ex.Message)
            End Try
        End Sub
        """;
    private const string B11Expected = "L:Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')\nA:Index was outside the bounds of the array.";

    /// <summary>B22 — a compound assignment: <c>l(1) += 5</c>, <c>a(2) += 7</c>, <c>a(2) *= 2</c> in range, then <c>l(2) += 1</c> and <c>a(3) -= 1</c> out of range (the read half throws before the write, so neither grows).</summary>
    private const string B22 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            l(1) += 5
            Dim a(2) As Integer
            a(2) += 7
            a(2) *= 2
            Console.WriteLine(l(1) & " " & a(2))
            Try
                l(2) += 1
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE compound")
            End Try
            Try
                a(3) -= 1
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE compound")
            End Try
            Console.WriteLine(l.Count & " " & a.Length)
        End Sub
        """;
    private const string B22Expected = "7 14\ncaught AOORE compound\ncaught IOORE compound\n2 3";

    /// <summary>E05 — BasicLang's own <c>++</c> / <c>--</c> (C's meaning, #141) on an array element and a List element, in range and out of range. NOT vbc-answered: VB has no such operator, the expectation is what the same statement does in C#.</summary>
    private const string E05 = """
        Sub Main()
            Dim a(2) As Integer
            a(1) = 3
            Dim l As New List(Of Integer)()
            l.Add(2)
            a(1)++
            l(0)--
            Console.WriteLine(a(1) & " " & l(0))
            Try
                a(5)++
                Console.WriteLine("no-throw inc")
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE inc")
            End Try
            Try
                l(5)--
                Console.WriteLine("no-throw dec")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE dec")
            End Try
            Console.WriteLine(a.Length & " " & l.Count)
        End Sub
        """;
    private const string E05Expected = "4 1\ncaught IOORE inc\ncaught AOORE dec\n3 1";

    /// <summary>B16 — a List read inside a <c>When</c> guard: in range it matches, past the end it throws. A guard is built with emission suppressed and sits in NO block, so only the guard scan finds it. ⛔ Kills M2.</summary>
    private const string B16 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(3)
            Dim x As Integer = 1
            Select Case x
                Case Is > 0 When l(0) = 3
                    Console.WriteLine("guard in range")
                Case Else
                    Console.WriteLine("else")
            End Select
            Try
                Select Case x
                    Case Is > 0 When l(5) = 3
                        Console.WriteLine("guard past end matched")
                    Case Else
                        Console.WriteLine("guard past end else")
                End Select
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE guard")
            End Try
        End Sub
        """;
    private const string B16Expected = "guard in range\ncaught AOORE guard";

    /// <summary>B17 — a field array read in a method and written through <c>b.Cells(7) = 1</c>, a List field written in a method (<c>Items(i) = v</c>), and a List read inside a lambda (<c>Function(i) items(i)</c>). ⛔ Kills M4.</summary>
    private const string B17 = """
        Class Box
            Public Cells(2) As Integer
            Public Items As List(Of String)

            Public Sub New()
                Items = New List(Of String)()
            End Sub

            Public Function CellAt(i As Integer) As Integer
                Return Cells(i)
            End Function

            Public Sub SetItem(i As Integer, v As String)
                Items(i) = v
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            b.Items.Add("x")
            b.Cells(1) = 5
            Console.WriteLine("cell1=" & b.CellAt(1) & " f=" & b.Cells(1))
            Try
                Console.WriteLine(b.CellAt(3))
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE method")
            End Try
            Try
                b.SetItem(1, "y")
                Console.WriteLine("no-throw set")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE method")
            End Try
            Dim items As List(Of String) = b.Items
            Dim f As Func(Of Integer, String) = Function(i) items(i)
            Try
                Console.WriteLine(f(0))
                Console.WriteLine(f(4))
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE lambda")
            End Try
            Try
                b.Cells(7) = 1
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE field write")
            End Try
        End Sub
        """;
    private const string B17Expected = "cell1=5 f=5\ncaught IOORE method\ncaught AOORE method\nx\ncaught AOORE lambda\ncaught IOORE field write";

    /// <summary>B21 — <c>b.Items(3)</c> on a List FIELD through a qualified receiver (#204's lowering), in range and past the end.</summary>
    private const string B21 = """
        Class Box
            Public Items As List(Of String)

            Public Sub New()
                Items = New List(Of String)()
                Items.Add("x")
            End Sub
        End Class

        Sub Main()
            Dim b As New Box()
            Console.WriteLine("in " & b.Items(0))
            Try
                Console.WriteLine("past " & b.Items(3))
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE member")
            End Try
        End Sub
        """;
    private const string B21Expected = "in x\ncaught AOORE member";

    /// <summary>E03 — an array read in a CONSTRUCTOR (<c>First = Cells(2)</c>) and in a property Get, in range and past the end. Test-writer's own: vbc-free, VB's rules.</summary>
    private const string E03 = """
        Class K
            Public Cells() As Integer
            Public First As Integer
            Public Sub New()
                Cells = New Integer() {1, 2, 3}
                First = Cells(2)
            End Sub
            Public ReadOnly Property Second As Integer
                Get
                    Return Cells(1)
                End Get
            End Property
            Public ReadOnly Property Bad As Integer
                Get
                    Return Cells(9)
                End Get
            End Property
        End Class

        Sub Main()
            Dim k As New K()
            Console.WriteLine(k.First & " " & k.Second)
            Try
                Console.WriteLine(k.Bad)
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught property get")
            End Try
        End Sub
        """;
    private const string E03Expected = "3 2\ncaught property get";

    /// <summary>E10 — a List read in a constructor, and through a SHARED field in a Shared Function, in range and past the end. Test-writer's own: vbc-free, VB's rules.</summary>
    private const string E10 = """
        Class K
            Public Items As List(Of Integer)
            Public Shared Names As List(Of String)
            Public Sub New()
                Items = New List(Of Integer)()
                Names = New List(Of String)()
                Items.Add(3)
                Names.Add("n")
                Dim x As Integer = Items(0)
                Console.WriteLine("ctor " & x)
                Try
                    Dim y As Integer = Items(2)
                    Console.WriteLine("ctor no-throw " & y)
                Catch ex As ArgumentOutOfRangeException
                    Console.WriteLine("ctor caught")
                End Try
            End Sub
            Public Shared Function First() As String
                Return Names(0)
            End Function
            Public Shared Function Missing() As String
                Return Names(4)
            End Function
        End Class

        Sub Main()
            Dim k As New K()
            Console.WriteLine(K.First())
            Try
                Console.WriteLine(K.Missing())
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("shared caught")
            End Try
        End Sub
        """;
    private const string E10Expected = "ctor 3\nctor caught\nn\nshared caught";

    /// <summary>B18 — a <c>List(Of Integer())</c> chain: <c>lst(0)(0)</c> in range, <c>lst(0)(5)</c> (the ARRAY level out: IndexOutOfRange), <c>lst(3)(0)</c> (the LIST level out: ArgumentOutOfRange), and a nested List write <c>nest(0)(0) = 1</c> on an empty inner List.</summary>
    private const string B18 = """
        Sub Main()
            Dim lst As New List(Of Integer())()
            Dim row(1) As Integer
            row(0) = 6
            lst.Add(row)
            Console.WriteLine("ok " & lst(0)(0))
            Try
                Console.WriteLine(lst(0)(5))
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE inner")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE inner")
            End Try
            Try
                Console.WriteLine(lst(3)(0))
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught IOORE outer")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE outer")
            End Try
            Dim nest As New List(Of List(Of Integer))()
            nest.Add(New List(Of Integer)())
            Try
                nest(0)(0) = 1
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught AOORE nested write")
            End Try
            Console.WriteLine("n=" & nest(0).Count)
        End Sub
        """;
    private const string B18Expected = "ok 6\ncaught IOORE inner\ncaught AOORE outer\ncaught AOORE nested write\nn=0";

    /// <summary>B20 — the idiom the check exists for: <c>Do ... Loop</c> and <c>While True</c> over a List and an array, each ended only by the exception. Unchecked, the loop reads <c>undefined</c> forever (NaN totals, never throws). ⛔ Kills M1.</summary>
    private const string B20 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            Dim total As Integer = 0
            Dim i As Integer = 0
            Try
                Do
                    total = total + l(i)
                    i = i + 1
                Loop
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("stopped at " & i & " total " & total)
            End Try
            Dim a(1) As Integer
            a(0) = 10
            a(1) = 20
            Dim j As Integer = 0
            Try
                While True
                    total = total + a(j)
                    j = j + 1
                End While
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("stopped at " & j & " total " & total)
            End Try
        End Sub
        """;
    private const string B20Expected = "stopped at 2 total 3\nstopped at 2 total 33";

    /// <summary>B10 — a List read past the end with NO <c>Try</c>: prints <c>start</c>, then dies with the uncaught ArgumentOutOfRangeException, as on .NET. Unchecked, it printed <c>continued undefined</c> and exited 0.</summary>
    private const string B10 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            Console.WriteLine("start")
            Dim v As Integer = l(1)
            Console.WriteLine("continued " & v)
        End Sub
        """;

    /// <summary>E06 — a read whose result is NEVER USED (<c>Dim v As Integer = l(7)</c>, an array, a Dictionary) still throws: an element read is a side effect, so no pass may drop it as dead. Test-writer's own: vbc-free, VB's rules.</summary>
    private const string E06 = """
        Sub Main()
            Dim a(2) As Integer
            Dim l As New List(Of Integer)()
            l.Add(1)
            Try
                Dim v As Integer = l(7)
                Console.WriteLine("unused read survived")
            Catch ex As ArgumentOutOfRangeException
                Console.WriteLine("caught unused list read")
            End Try
            Try
                Dim w As Integer = a(7)
                Console.WriteLine("unused read survived")
            Catch ex As IndexOutOfRangeException
                Console.WriteLine("caught unused array read")
            End Try
            Dim d As New Dictionary(Of String, Integer)()
            Try
                Dim z As Integer = d("nope")
                Console.WriteLine("unused read survived")
            Catch ex As KeyNotFoundException
                Console.WriteLine("caught unused dict read")
            End Try
        End Sub
        """;
    private const string E06Expected = "caught unused list read\ncaught unused array read\ncaught unused dict read";

    /// <summary>B09 — controls: in-range List and array reads and writes, a computed index, a nested List read and write, ending in <c>l(l.Count - 1)</c>.</summary>
    private const string B09 = """
        Sub Main()
            Dim l As New List(Of Integer)()
            For i As Integer = 0 To 4
                l.Add(i * i)
            Next
            l(2) = 100
            Dim s As Integer = 0
            For i As Integer = 0 To l.Count - 1
                s = s + l(i)
            Next
            Dim a(3) As Integer
            For i As Integer = 0 To 3
                a(i) = i + 1
            Next
            a(3) = a(0) + a(1)
            Dim t As Integer = 0
            For i As Integer = 0 To a.Length - 1
                t = t + a(i)
            Next
            Dim n As New List(Of List(Of Integer))()
            n.Add(New List(Of Integer)())
            n(0).Add(42)
            n(0)(0) = n(0)(0) + 1
            Console.WriteLine(s & " " & t & " " & n(0)(0) & " " & l(l.Count - 1))
        End Sub
        """;
    private const string B09Expected = "126 9 43 16";

    /// <summary>B23 — controls: a sorted List, an array passed to a Function, a Dictionary of Lists (<c>d("k")(0)</c>) and a For Each, all in range.</summary>
    private const string B23 = """
        Function Total(xs As Integer()) As Integer
            Dim s As Integer = 0
            For i As Integer = 0 To xs.Length - 1
                s += xs(i)
            Next
            Return s
        End Function

        Sub Main()
            Dim words As New List(Of String)()
            words.Add("b")
            words.Add("a")
            words.Sort()
            Dim nums() As Integer = {3, 4, 5}
            Dim d As New Dictionary(Of String, List(Of Integer))()
            d("k") = New List(Of Integer)()
            d("k").Add(9)
            Console.WriteLine(words(0) & words(1) & " " & Total(nums) & " " & d("k")(0))
            For Each w As String In words
                Console.Write(w)
            Next
            Console.WriteLine()
        End Sub
        """;
    private const string B23Expected = "ab 12 9\nab";
    // ---- the rows ---------------------------------------------------------------------------------

    /// <summary>
    /// A List READ past the end: a typed Catch (B01), a catch-all (B02), AT <c>Count</c> (B01, B02), a negative index (B03), the explicit
    /// <c>.Item(9)</c> (B07), and through a Function parameter and a Select Case selector (B12). ⛔ Kills M1.
    /// </summary>
    [Test]
    public void ListRead_PastTheEnd_B01_B02_B03_B07_B12_ThrowsArgumentOutOfRange_OnJavaScript()
        => EveryProbeMatches(("B01", B01, B01Expected), ("B02", B02, B02Expected), ("B03", B03, B03Expected), ("B07", B07, B07Expected), ("B12", B12, B12Expected));

    /// <summary>A List WRITE past the end and at <c>Count</c> throws and never grows the List (B04).</summary>
    [Test]
    public void ListWrite_PastTheEnd_B04_Throws_AndTheListDoesNotGrow_OnJavaScript()
        => EveryProbeMatches(("B04", B04, B04Expected));

    /// <summary>An array READ (B05, negative too) and WRITE (B06, a literal and a module array B14) past the end: IndexOutOfRangeException, and the array does not grow. ⛔ Kills M4.</summary>
    [Test]
    public void Array_ReadAndWrite_PastTheEnd_B05_B06_B14_ThrowIndexOutOfRange_OnJavaScript()
        => EveryProbeMatches(("B05", B05, B05Expected), ("B06", B06, B06Expected), ("B14", B14, B14Expected));

    /// <summary>A 2-D array checks EVERY level: the first level out on a read, the last level out on a write (B13). ⛔ Kills M4 and M5.</summary>
    [Test]
    public void Array2D_ReadAndWrite_PastTheEnd_B13_CheckEveryLevel_OnJavaScript()
        => EveryProbeMatches(("B13", B13, B13Expected));

    /// <summary>A Dictionary READ of a missing key is KeyNotFoundException with the key in the message (B08, B19); a write is still insert-or-update (B19).</summary>
    [Test]
    public void Dictionary_MissingKey_B08_B19_ThrowsKeyNotFound_WithTheKeyInTheMessage_OnJavaScript()
        => EveryProbeMatches(("B08", B08, B08Expected), ("B19", B19, B19Expected));

    /// <summary><c>ex.Message</c> equals .NET's text for a List and for an array, read through a catch-all that is the only handler (B11). ⛔ Kills M1 and M3.</summary>
    [Test]
    public void MessageText_B11_EqualsDotNets_ForAListAndAnArray_OnJavaScript()
        => EveryProbeMatches(("B11", B11, B11Expected));

    /// <summary>A compound assignment (B22) and BasicLang's own <c>++</c> / <c>--</c> (E05) on an element out of range throw before they write.</summary>
    [Test]
    public void CompoundAssignAndIncrement_B22_E05_OutOfRange_Throw_AndWriteNothing_OnJavaScript()
        => EveryProbeMatches(("B22", B22, B22Expected), ("E05", E05, E05Expected));

    /// <summary>A List read inside a <c>When</c> guard (B16): the guard sits in no block, so the up-front scan must look in it. ⛔ Kills M2.</summary>
    [Test]
    public void WhenGuard_B16_ListReadPastTheEnd_Throws_OnJavaScript()
        => EveryProbeMatches(("B16", B16, B16Expected));

    /// <summary>
    /// The edge sites: a field array, a List field and a List captured by a lambda (B17); <c>b.Items(3)</c> through a qualified receiver (B21);
    /// a constructor and a property Get (E03); a constructor and a Shared Function (E10). ⛔ Kills M4.
    /// </summary>
    [Test]
    public void EdgeSites_B17_B21_E03_E10_FieldLambdaQualifiedReceiverConstructorPropertyShared_Throw_OnJavaScript()
        => EveryProbeMatches(("B17", B17, B17Expected), ("B21", B21, B21Expected), ("E03", E03, E03Expected), ("E10", E10, E10Expected));

    /// <summary>A <c>List(Of Integer())</c> chain checks each level and tells the two exceptions apart (B18).</summary>
    [Test]
    public void ListOfArrays_B18_ChainedIndexing_ChecksEveryLevel_OnJavaScript()
        => EveryProbeMatches(("B18", B18, B18Expected));

    /// <summary>A loop ended only by the exception (B20): unchecked, it never ended. ⛔ Kills M1.</summary>
    [Test]
    public void LoopEndedByTheException_B20_StopsAtTheEnd_OnJavaScript()
        => EveryProbeMatches(("B20", B20, B20Expected));

    /// <summary>
    /// An UNCAUGHT read past the end (B10) dies with ArgumentOutOfRangeException and a non-zero exit, as .NET does, instead of printing
    /// <c>continued undefined</c> and exiting 0. The uncaught error's HEADER must be .NET's class and message, which needs the class in the prelude (M3).
    /// </summary>
    [Test]
    public void UncaughtListRead_B10_DiesWithArgumentOutOfRange_AndNeverRunsPastIt_OnJavaScript()
    {
        TempExec.RequireTool(Bk.JavaScript);
        var failures = new List<string>();
        foreach (var entry in Enum.GetValues<EntryPoint>())
        {
            try
            {
                var js = TempExec.Emit(Bk.JavaScript, entry, B10);
                var (exit, stdout, stderr) = JavaScriptExecutionTests.RunNodeScriptForOutcome(js);
                if (exit == 0) failures.Add($"{entry}: exited 0 — .NET dies with an unhandled exception here (stdout [{TempExec.Norm(stdout).Replace("\n", " | ")}])");
                if (TempExec.Norm(stdout) != "start") failures.Add($"{entry}: printed [{TempExec.Norm(stdout).Replace("\n", " | ")}] where .NET prints only [start] before it dies");
                // ⚠ The ERROR HEADER, not a substring: Node prints the throwing source line above it, so a bare Contains("ArgumentOutOfRangeException")
                // is satisfied by `throw new ArgumentOutOfRangeException(...)` even when the class is missing from the prelude and the real error is
                // "ReferenceError: ArgumentOutOfRangeException is not defined" (M3).
                if (!System.Text.RegularExpressions.Regex.IsMatch(stderr, @"^ArgumentOutOfRangeException: Index was out of range\. Must be non-negative and less than the size of the collection\. \(Parameter 'index'\)", System.Text.RegularExpressions.RegexOptions.Multiline))
                    failures.Add($"{entry}: stderr does not open with .NET's ArgumentOutOfRangeException header:\n{stderr}");
                if (stderr.Contains("ReferenceError")) failures.Add($"{entry}: the throw itself failed with a ReferenceError (the exception class is missing from the prelude):\n{stderr}");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {ex.Message.Split('\n')[0]}");
            }
        }
        Assert.That(failures, Is.Empty, "B10 on JavaScript:\n" + string.Join("\n", failures));
    }

    /// <summary>
    /// A read whose result nothing uses still throws (E06), List, array and Dictionary: an element read is a side effect, so dead-code
    /// elimination under <c>--optimize</c> must not drop it.
    /// </summary>
    [Test]
    public void UnusedReads_E06_StillThrow_ThroughEveryEntryPoint_OnJavaScript()
        => EveryProbeMatches(("E06", E06, E06Expected));

    /// <summary>The controls: in-range reads and writes, a nested List, a sorted List, an array argument and a Dictionary of Lists are unchanged (B09, B23).</summary>
    [Test]
    public void Controls_B09_B23_InRangeReadsAndWrites_AreUnchanged_OnJavaScript()
        => EveryProbeMatches(("B09", B09, B09Expected), ("B23", B23, B23Expected));

    /// <summary>
    /// Runs every probe on JavaScript through the CLI, the CLI with --optimize and CompileProjectFiles. Every failure is collected and
    /// names its probe; Node missing Ignores the row before anything runs (an Ignore thrown inside the loop would hide the failures before it).
    /// </summary>
    private static void EveryProbeMatches(params (string Id, string Source, string Expected)[] probes)
    {
        TempExec.RequireTool(Bk.JavaScript);
        var failures = new List<string>();
        foreach (var (id, source, expected) in probes)
        {
            try
            {
                TempExec.AssertMatchesInEveryEntryPoint(Bk.JavaScript, source, expected, id);
            }
            catch (AssertionException ex)
            {
                failures.Add(ex.Message);
            }
        }
        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }
}

/// <summary>
/// #207, the text half, FAST (no process spawned, in the fast subset): the checked-access helpers are emitted only where the module
/// uses them. The JS prelude is SHARED with web-form code-behind, so a program that never indexes a List, an array or a Dictionary must
/// emit none of the five helpers and none of the three exception classes they throw — on the standard, the optimized and the aggressive route.
/// </summary>
[TestFixture]
public class JavaScriptBoundsCheckEmissionTests
{
    private static readonly string[] Helpers = { "__blListGet", "__blListSet", "__blArrayGet", "__blArraySet", "__blDictGet" };
    private static readonly string[] Classes = { "ArgumentOutOfRangeException", "IndexOutOfRangeException", "KeyNotFoundException" };

    /// <summary>
    /// A List, an array and a Dictionary, used every way EXCEPT indexing: Add / Count / For Each, <c>Dim a(2)</c> / Length / For Each, and a
    /// Dictionary WRITE (<c>d("k") = 5</c> stays <c>Map.set</c>), Add, ContainsKey and Count. Runs and prints "5 2 3".
    /// </summary>
    private const string NeverIndexes = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(1)
            l.Add(2)
            Dim a(2) As Integer
            Dim d As New Dictionary(Of String, Integer)()
            d("k") = 5
            d.Add("j", 6)
            Dim total As Integer = 0
            For Each x As Integer In l
                total = total + x
            Next
            For Each y As Integer In a
                total = total + y
            Next
            If d.ContainsKey("k") Then total = total + d.Count
            Console.WriteLine(total & " " & l.Count & " " & a.Length)
        End Sub
        """;
    private const string ReadsAListOnly = """
        Sub Main()
            Dim l As New List(Of Integer)()
            l.Add(4)
            Console.WriteLine(l(0))
        End Sub
        """;

    private static IEnumerable<(string Route, Func<string, string> Compile)> Routes()
    {
        yield return ("standard", s => JsTestSupport.Compile(s));
        yield return ("optimized", s => JsTestSupport.CompileOptimized(s));
        yield return ("aggressive", s => JsTestSupport.CompileAggressive(s));
    }

    [Test]
    public void AProgramThatNeverIndexes_EmitsNoElementHelper_AndOneThatReadsAListEmitsOnlyItsOwn()
    {
        Assert.Multiple(() =>
        {
            foreach (var (route, compile) in Routes())
            {
                var none = compile(NeverIndexes);
                foreach (var name in Helpers.Concat(Classes))
                    Assert.That(none, Does.Not.Contain(name), $"{route}: a program that never indexes must not emit {name} — the prelude must stay additive.\n{none}");

                // The control: the same check is not vacuous. A List read emits __blListGet and its exception class, and ONLY those.
                var listRead = compile(ReadsAListOnly);
                Assert.That(listRead, Does.Contain("function __blListGet("), $"{route}: a List read must emit its helper.\n{listRead}");
                Assert.That(listRead, Does.Contain("class ArgumentOutOfRangeException"), $"{route}: …and the class it throws.\n{listRead}");
                foreach (var name in Helpers.Where(h => h != "__blListGet").Concat(new[] { "IndexOutOfRangeException", "KeyNotFoundException" }))
                    Assert.That(listRead, Does.Not.Contain(name), $"{route}: a program that only reads a List must not emit {name}.\n{listRead}");
            }
        });
    }
}
