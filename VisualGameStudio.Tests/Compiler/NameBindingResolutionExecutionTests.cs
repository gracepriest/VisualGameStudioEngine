using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #124 — ADR-0013 D3, EXECUTION: every probe the fix moved to vbc's answer, run on the four
//  backends that print, through every entry point.
//
//  ⭐ THE ORACLE IS vbc, NOT A BACKEND. Every expected value below is the output of the SDK's `vbc`
//  running the probe (wrapped in a `Module`, exactly as the #163 fixture does), taken from the
//  implementer's `.exp` files and re-checked by the test-writer. Never a BasicLang backend's answer.
//
//  ⭐ EVERY ROW HAS ITS CONTROL. The case-differing program `X` and the same-case program `Xc`
//  differ in the case of the references and nothing else; both must print vbc's answer. A backend
//  where `Xc` was already right and `X` is now right is the fix; a backend where BOTH are wrong is a
//  defect that predates #124 and is not this fixture's to assert (see the table).
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the
//  IR optimizer"). A single-file probe goes through the real `BasicLang` CLI (standard passes), the
//  real CLI with `--optimize` (aggressive passes) and `BasicCompiler.CompileProjectFiles` with
//  `OptimizeAggressive` — what a Release .blproj build and the IDE call. A multi-file probe goes
//  through `BasicLang build P.blproj` (Debug, and Release = aggressive) and `CompileProjectFiles`
//  (standard, and aggressive). ⚠ The C++ backend's CLI project build is MSVC-only (BL6015 without
//  it; `Native/NativeBuildSkip` is that gate), so a multi-file C++ cell runs the two in-process
//  entry points; its single-file cells do run the CLI (`--target=cpp` emits the .cpp).
//
//  ⛔ CELLS WITH NO EXPECTATION, by row (a backend that is wrong in the control too, or for a filed
//  follow-up — measured identical before and after #124; asserting them would pin a defect):
//
//    FL  / FLc   C#   prints 7 where vbc prints 42 — a lambda's write to a field; the control too. Pre-existing; no follow-up filed.
//    FOg / FOgc  C#   CS0103 — the increment of a For over ANOTHER module's global.                  #246 (pinned below)
//    MF5 / MF5c  C#   the same, across files.                                                        #246 (pinned below)
//    EVb / EVbc  C++, MSIL   events do not run there in ANY case (C++ does not compile, MSIL BL-FAILs).      #245
//    EVr / EVrc  C++, MSIL   the same.                                                               #245
//    AWa / AWac  C++, JS, MSIL  Await on those backends: C++ and MSIL do not compile the control (`member reference type Task<int>`,
//                            `undefined class 'Task'`) and JavaScript prints `undefined` for it. Pre-existing async gaps; none filed. C# is the only cell the fix moved.
//
//  (PRa / PRac, an auto-property written by its bare name, were excluded on C++ — it printed 0 for vbc's 30, the control too. FIXED by #218 / #254: both now run on all four backends.)
//
//  Rows NOT here because #124 does not fix them: EVa (`AddHandler b.clicked` — a member-access event, #245), FOac (`For x As T`
//  leaves the variable bound, #247), CAn (`Catch err` with no `As`, #248), MEr (a Function's own name as its return value, #249),
//  MF3/MF3u (cross-file module globals that differ only by case, #244). RDa (ReDim: MSIL), TYb (`TypeOf`/`CType` to a class: C++),
//  TYe (an Enum: every backend) and USa (`Using`: every backend) fail identically in their same-case controls — pre-existing, none filed.
// ================================================================================================

/// <summary>A multi-file probe: its files, and vbc's answer for the WHOLE program.</summary>
public sealed record ProjectProbe(string Id, (string Name, string Source)[] Files, string Vb, Bk Agrees = Bk.All)
{
    public override string ToString() => Id;
}

/// <summary>The entry points a multi-file probe goes through.</summary>
internal enum ProjectEntry
{
    /// <summary><c>BasicLang build P.blproj -c Debug</c>: the real CLI, standard passes.</summary>
    CliDebug,

    /// <summary><c>BasicLang build P.blproj -c Release</c>: the real CLI, aggressive passes.</summary>
    CliRelease,

    /// <summary><c>CompileProjectFiles</c>, standard passes.</summary>
    ProjectStandard,

    /// <summary><c>CompileProjectFiles</c> with <c>OptimizeAggressive</c>: what the IDE's build service calls.</summary>
    ProjectAggressive,
}

/// <summary>The probes. Sources are the implementer's (<c>S/t124/probes</c>, <c>S/t124/mf</c>); expectations are vbc's.</summary>
internal static class BindingProbes
{
    private static TempProbe P(string id, string source, string vb, Bk agrees) => new(id, source, vb, agrees);

    /// <summary>A bare field READ.</summary>
    internal static readonly TempProbe FR = P("FR", """
        Class C
            Private Total As Integer = 5
            Public Function Run() As Integer
                Return total * 2
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "10", Bk.All);

    /// <summary>The same-case control of FR.</summary>
    internal static readonly TempProbe FRc = P("FRc", """
        Class C
            Private Total As Integer = 5
            Public Function Run() As Integer
                Return Total * 2
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "10", Bk.All);

    /// <summary>A bare field WRITE and compound write.</summary>
    internal static readonly TempProbe FW = P("FW", """
        Class C
            Private Total As Integer = 5
            Public Sub Bump()
                total = total + 7
                TOTAL += 1
            End Sub
            Public Function Read() As Integer
                Return Me.Total
            End Function
        End Class
        Sub Main()
            Dim o As New C()
            o.Bump()
            Console.WriteLine(o.Read())
        End Sub
        """, "13", Bk.All);

    /// <summary>The same-case control of FW.</summary>
    internal static readonly TempProbe FWc = P("FWc", """
        Class C
            Private Total As Integer = 5
            Public Sub Bump()
                Total = Total + 7
                Total += 1
            End Sub
            Public Function Read() As Integer
                Return Me.Total
            End Function
        End Class
        Sub Main()
            Dim o As New C()
            o.Bump()
            Console.WriteLine(o.Read())
        End Sub
        """, "13", Bk.All);

    /// <summary>A field read and written from two lambdas.</summary>
    internal static readonly TempProbe FL = P("FL", """
        Class C
            Private Total As Integer = 5
            Public Function Run() As Integer
                Dim f = Function(k As Integer) k + total
                Dim s = Sub(k As Integer) TOTAL = k
                s(40)
                Return f(2)
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "42", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>The same-case control of FL.</summary>
    internal static readonly TempProbe FLc = P("FLc", """
        Class C
            Private Total As Integer = 5
            Public Function Run() As Integer
                Dim f = Function(k As Integer) k + Total
                Dim s = Sub(k As Integer) Total = k
                s(40)
                Return f(2)
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "42", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>An INHERITED Protected field, read and written.</summary>
    internal static readonly TempProbe FIn = P("FIn", """
        Class B
            Protected Level As Integer = 3
        End Class
        Class D
            Inherits B
            Public Function Run() As Integer
                level = level * 5
                Return LEVEL + 1
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New D().Run())
        End Sub
        """, "16", Bk.All);

    /// <summary>The same-case control of FIn.</summary>
    internal static readonly TempProbe FInc = P("FInc", """
        Class B
            Protected Level As Integer = 3
        End Class
        Class D
            Inherits B
            Public Function Run() As Integer
                Level = Level * 5
                Return Level + 1
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New D().Run())
        End Sub
        """, "16", Bk.All);

    /// <summary>A Shared field, bare, compound and plain write.</summary>
    internal static readonly TempProbe FShB = P("FShB", """
        Class C
            Private Shared Count As Integer = 0
            Public Shared Function Tick() As Integer
                count += 1
                COUNT = count + 10
                Return Count
            End Function
        End Class
        Sub Main()
            Console.WriteLine(C.Tick())
        End Sub
        """, "11", Bk.All);

    /// <summary>The same-case control of FShB.</summary>
    internal static readonly TempProbe FShBc = P("FShBc", """
        Class C
            Private Shared Count As Integer = 0
            Public Shared Function Tick() As Integer
                Count += 1
                Count = Count + 10
                Return Count
            End Function
        End Class
        Sub Main()
            Console.WriteLine(C.Tick())
        End Sub
        """, "11", Bk.All);

    /// <summary>An auto-property named bare, then through an instance.</summary>
    internal static readonly TempProbe PRa = P("PRa", """
        Class C
            Public Property Count As Integer
            Public Sub Bump()
                count = count + 1
                COUNT += 2
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.Bump()
            o.count = o.COUNT * 10
            Console.WriteLine(o.Count)
        End Sub
        """, "30", Bk.All);

    /// <summary>The same-case control of PRa.</summary>
    internal static readonly TempProbe PRac = P("PRac", """
        Class C
            Public Property Count As Integer
            Public Sub Bump()
                Count = Count + 1
                Count += 2
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.Bump()
            o.Count = o.Count * 10
            Console.WriteLine(o.Count)
        End Sub
        """, "30", Bk.All);

    /// <summary>A For Each over a FIELD reuses it.</summary>
    internal static readonly TempProbe FEf = P("FEf", """
        Class C
            Private Item As Integer
            Public Function Run() As Integer
                For Each item In New Integer() {4, 5, 6}
                    Console.WriteLine(ITEM)
                Next
                Return Item
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "4\n5\n6\n6", Bk.All);

    /// <summary>The same-case control of FEf.</summary>
    internal static readonly TempProbe FEfc = P("FEfc", """
        Class C
            Private Item As Integer
            Public Function Run() As Integer
                For Each Item In New Integer() {4, 5, 6}
                    Console.WriteLine(Item)
                Next
                Return Item
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "4\n5\n6\n6", Bk.All);

    /// <summary>A For Each over a field, read from a lambda.</summary>
    internal static readonly TempProbe FEla = P("FEla", """
        Class C
            Private Item As Integer = 0
            Public Function Run() As Integer
                Dim f = Function() item * 10
                For Each ITEM In New Integer() {2, 3}
                Next
                Return f()
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "30", Bk.All);

    /// <summary>The same-case control of FEla.</summary>
    internal static readonly TempProbe FElac = P("FElac", """
        Class C
            Private Item As Integer = 0
            Public Function Run() As Integer
                Dim f = Function() Item * 10
                For Each Item In New Integer() {2, 3}
                Next
                Return f()
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "30", Bk.All);

    /// <summary>A counted For over a field.</summary>
    internal static readonly TempProbe FOf = P("FOf", """
        Class C
            Private Total As Integer = 50
            Public Function Run() As Integer
                For total = 1 To 3
                    Console.WriteLine(TOTAL)
                Next
                Return Total
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "1\n2\n3\n4", Bk.All);

    /// <summary>The same-case control of FOf.</summary>
    internal static readonly TempProbe FOfc = P("FOfc", """
        Class C
            Private Total As Integer = 50
            Public Function Run() As Integer
                For Total = 1 To 3
                    Console.WriteLine(Total)
                Next
                Return Total
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New C().Run())
        End Sub
        """, "1\n2\n3\n4", Bk.All);

    /// <summary>A counted For over a MODULE's global.</summary>
    internal static readonly TempProbe FOg = P("FOg", """
        Module Data
            Public G As Integer = 0
        End Module
        Sub Bump()
            For g = 1 To 3
            Next
        End Sub
        Sub Main()
            Bump()
            Console.WriteLine(Data.G)
        End Sub
        """, "4", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>The same-case control of FOg.</summary>
    internal static readonly TempProbe FOgc = P("FOgc", """
        Module Data
            Public G As Integer = 0
        End Module
        Sub Bump()
            For G = 1 To 3
            Next
        End Sub
        Sub Main()
            Bump()
            Console.WriteLine(Data.G)
        End Sub
        """, "4", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>A counted For over a file-scope global.</summary>
    internal static readonly TempProbe FOgf = P("FOgf", """
        Dim G As Integer = 0
        Sub Bump()
            For g = 1 To 3
            Next
        End Sub
        Sub Main()
            Bump()
            Console.WriteLine(G)
        End Sub
        """, "4", Bk.All);

    /// <summary>The same-case control of FOgf.</summary>
    internal static readonly TempProbe FOgfc = P("FOgfc", """
        Dim G As Integer = 0
        Sub Bump()
            For G = 1 To 3
            Next
        End Sub
        Sub Main()
            Bump()
            Console.WriteLine(G)
        End Sub
        """, "4", Bk.All);

    /// <summary>A counted For over a local.</summary>
    internal static readonly TempProbe FOl = P("FOl", """
        Sub Main()
            Dim Total As Integer = 100
            For total = 1 To 3
            Next
            Console.WriteLine(Total)
        End Sub
        """, "4", Bk.All);

    /// <summary>The same-case control of FOl.</summary>
    internal static readonly TempProbe FOlc = P("FOlc", """
        Sub Main()
            Dim Total As Integer = 100
            For Total = 1 To 3
            Next
            Console.WriteLine(Total)
        End Sub
        """, "4", Bk.All);

    /// <summary>A counted For over a file-scope global, read from a lambda.</summary>
    internal static readonly TempProbe FOla = P("FOla", """
        Dim G As Integer = 7
        Sub Main()
            Dim f = Function() g * 2
            For g = 1 To 2
            Next
            Console.WriteLine(f())
        End Sub
        """, "6", Bk.All);

    /// <summary>The same-case control of FOla.</summary>
    internal static readonly TempProbe FOlac = P("FOlac", """
        Dim G As Integer = 7
        Sub Main()
            Dim f = Function() G * 2
            For G = 1 To 2
            Next
            Console.WriteLine(f())
        End Sub
        """, "6", Bk.All);

    /// <summary>A counted For over a parameter.</summary>
    internal static readonly TempProbe FOp = P("FOp", """
        Sub Run(N As Integer)
            For n = 1 To 2
            Next
            Console.WriteLine(N)
        End Sub
        Sub Main()
            Run(50)
        End Sub
        """, "3", Bk.All);

    /// <summary>The same-case control of FOp.</summary>
    internal static readonly TempProbe FOpc = P("FOpc", """
        Sub Run(N As Integer)
            For N = 1 To 2
            Next
            Console.WriteLine(N)
        End Sub
        Sub Main()
            Run(50)
        End Sub
        """, "3", Bk.All);

    /// <summary>AddressOf a Sub in another case.</summary>
    internal static readonly TempProbe MEa = P("MEa", """
        Sub Show(v As Integer)
            Console.WriteLine(v * 3)
        End Sub
        Sub Main()
            Dim f As Action(Of Integer) = AddressOf show
            f(5)
        End Sub
        """, "15", Bk.All);

    /// <summary>The same-case control of MEa.</summary>
    internal static readonly TempProbe MEac = P("MEac", """
        Sub Show(v As Integer)
            Console.WriteLine(v * 3)
        End Sub
        Sub Main()
            Dim f As Action(Of Integer) = AddressOf Show
            f(5)
        End Sub
        """, "15", Bk.All);

    /// <summary>MyBase.method in another case.</summary>
    internal static readonly TempProbe MEb = P("MEb", """
        Class B
            Public Overridable Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
        End Class
        Class D
            Inherits B
            Public Overrides Function Twice(x As Integer) As Integer
                Return MyBase.twice(x) + 1
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New D().Twice(5))
        End Sub
        """, "11", Bk.All);

    /// <summary>The same-case control of MEb.</summary>
    internal static readonly TempProbe MEbc = P("MEbc", """
        Class B
            Public Overridable Function Twice(x As Integer) As Integer
                Return x * 2
            End Function
        End Class
        Class D
            Inherits B
            Public Overrides Function Twice(x As Integer) As Integer
                Return MyBase.Twice(x) + 1
            End Function
        End Class
        Sub Main()
            Console.WriteLine(New D().Twice(5))
        End Sub
        """, "11", Bk.All);

    /// <summary>A Shared method through its class, in another case.</summary>
    internal static readonly TempProbe MEs = P("MEs", """
        Class C
            Public Shared Function Make(x As Integer) As Integer
                Return x + 100
            End Function
        End Class
        Sub Main()
            Console.WriteLine(C.make(1))
            Console.WriteLine(C.MAKE(2))
        End Sub
        """, "101\n102", Bk.All);

    /// <summary>The same-case control of MEs.</summary>
    internal static readonly TempProbe MEsc = P("MEsc", """
        Class C
            Public Shared Function Make(x As Integer) As Integer
                Return x + 100
            End Function
        End Class
        Sub Main()
            Console.WriteLine(C.Make(1))
            Console.WriteLine(C.Make(2))
        End Sub
        """, "101\n102", Bk.All);

    /// <summary>A Shared method through a class named in another case.</summary>
    internal static readonly TempProbe MEt = P("MEt", """
        Class Util
            Public Shared Function Make(x As Integer) As Integer
                Return x + 100
            End Function
        End Class
        Sub Main()
            Console.WriteLine(util.Make(1))
            Console.WriteLine(UTIL.make(2))
        End Sub
        """, "101\n102", Bk.All);

    /// <summary>The same-case control of MEt.</summary>
    internal static readonly TempProbe MEtc = P("MEtc", """
        Class Util
            Public Shared Function Make(x As Integer) As Integer
                Return x + 100
            End Function
        End Class
        Sub Main()
            Console.WriteLine(Util.Make(1))
            Console.WriteLine(Util.Make(2))
        End Sub
        """, "101\n102", Bk.All);

    /// <summary>New of a class in another case.</summary>
    internal static readonly TempProbe TYa = P("TYa", """
        Class Box
            Public V As Integer = 3
        End Class
        Sub Main()
            Dim b As box = New BOX()
            Dim l As New List(Of box)()
            l.Add(b)
            Console.WriteLine(l(0).V + l.Count)
        End Sub
        """, "4", Bk.All);

    /// <summary>The same-case control of TYa.</summary>
    internal static readonly TempProbe TYac = P("TYac", """
        Class Box
            Public V As Integer = 3
        End Class
        Sub Main()
            Dim b As Box = New Box()
            Dim l As New List(Of Box)()
            l.Add(b)
            Console.WriteLine(l(0).V + l.Count)
        End Sub
        """, "4", Bk.All);

    /// <summary>AddHandler of an event in another case.</summary>
    internal static readonly TempProbe EVb = P("EVb", """
        Class Button
            Public Event Clicked(count As Integer)
            Public Sub Wire()
                AddHandler clicked, AddressOf Me.OnClicked
            End Sub
            Public Sub OnClicked(n As Integer)
                Console.WriteLine(n + 100)
            End Sub
            Public Sub Press()
                RaiseEvent Clicked(5)
            End Sub
        End Class
        Sub Main()
            Dim b As New Button()
            b.Wire()
            b.Press()
        End Sub
        """, "105", Bk.CSharp | Bk.JavaScript);

    /// <summary>The same-case control of EVb.</summary>
    internal static readonly TempProbe EVbc = P("EVbc", """
        Class Button
            Public Event Clicked(count As Integer)
            Public Sub Wire()
                AddHandler Clicked, AddressOf Me.OnClicked
            End Sub
            Public Sub OnClicked(n As Integer)
                Console.WriteLine(n + 100)
            End Sub
            Public Sub Press()
                RaiseEvent Clicked(5)
            End Sub
        End Class
        Sub Main()
            Dim b As New Button()
            b.Wire()
            b.Press()
        End Sub
        """, "105", Bk.CSharp | Bk.JavaScript);

    /// <summary>Await of a Function in another case.</summary>
    internal static readonly TempProbe AWa = P("AWa", """
        Async Function GetNumber() As Task(Of Integer)
            Return 42
        End Function
        Async Function Run() As Task(Of Integer)
            Dim n As Integer = Await getnumber()
            Return n + 1
        End Function
        Sub Main()
            Console.WriteLine(Run().Result)
        End Sub
        """, "43", Bk.CSharp);

    /// <summary>The same-case control of AWa.</summary>
    internal static readonly TempProbe AWac = P("AWac", """
        Async Function GetNumber() As Task(Of Integer)
            Return 42
        End Function
        Async Function Run() As Task(Of Integer)
            Dim n As Integer = Await GetNumber()
            Return n + 1
        End Function
        Sub Main()
            Console.WriteLine(Run().Result)
        End Sub
        """, "43", Bk.CSharp);

    /// <summary>A Module variable used before its Module, in another case.</summary>
    internal static readonly TempProbe MGw = P("MGw", """
        Sub Main()
            counter = COUNTER + 5
            Console.WriteLine(Counter)
        End Sub
        Module Data
            Public Counter As Integer = 7
        End Module
        """, "12", Bk.All);

    /// <summary>The same-case control of MGw.</summary>
    internal static readonly TempProbe MGwc = P("MGwc", """
        Sub Main()
            Counter = Counter + 5
            Console.WriteLine(Counter)
        End Sub
        Module Data
            Public Counter As Integer = 7
        End Module
        """, "12", Bk.All);

    /// <summary>RaiseEvent in another case.</summary>
    internal static readonly TempProbe EVr = P("EVr", """
        Class Button
            Public Event Clicked(count As Integer)
            Private clicks As Integer
            Public Sub Press()
                clicks = clicks + 1
                RaiseEvent clicked(clicks)
                RaiseEvent CLICKED(clicks * 10)
            End Sub
        End Class
        Sub OnClicked(n As Integer)
            Console.WriteLine(n)
        End Sub
        Sub Main()
            Dim b As New Button()
            AddHandler b.Clicked, AddressOf OnClicked
            b.Press()
        End Sub
        """, "1\n10", Bk.CSharp | Bk.JavaScript);

    /// <summary>The same-case control of EVr.</summary>
    internal static readonly TempProbe EVrc = P("EVrc", """
        Class Button
            Public Event Clicked(count As Integer)
            Private clicks As Integer
            Public Sub Press()
                clicks = clicks + 1
                RaiseEvent Clicked(clicks)
                RaiseEvent Clicked(clicks * 10)
            End Sub
        End Class
        Sub OnClicked(n As Integer)
            Console.WriteLine(n)
        End Sub
        Sub Main()
            Dim b As New Button()
            AddHandler b.Clicked, AddressOf OnClicked
            b.Press()
        End Sub
        """, "1\n10", Bk.CSharp | Bk.JavaScript);

    /// <summary>FOsb — a `Dim i` in an EARLIER, CLOSED block, then a counted `For i` with no `As`. The analyzer no longer sees the Dim, so it
    /// binds nothing; the IR builder's Ordinal test is what stops the loop declaring `i` a second time (M16).</summary>
    internal static readonly TempProbe FOsb = P("FOsb", """
        Sub Main()
            If True Then
                Dim i As Integer = 5
                Console.WriteLine(i)
            End If
            For i = 1 To 2
                Console.WriteLine(i)
            Next
        End Sub
        """, "5\n1\n2", Bk.All);

    /// <summary>NIb — a derived class reading its base's field in another case, the base declared AFTER the derived class: the IR class list is
    /// filled in declaration order, so the base is not registered when the reference is built (the deliberate non-ICE).</summary>
    internal static readonly TempProbe NIb = P("NIb", """
        Class Derived
            Inherits Base
            Public Function Run() As Integer
                Return total * 2
            End Function
        End Class
        Class Base
            Protected Total As Integer = 4
        End Class
        Sub Main()
            Console.WriteLine(New Derived().Run())
        End Sub
        """, "8", Bk.All);

    /// <summary>The same-case control of NIb.</summary>
    internal static readonly TempProbe NIbc = P("NIbc", """
        Class Derived
            Inherits Base
            Public Function Run() As Integer
                Return Total * 2
            End Function
        End Class
        Class Base
            Protected Total As Integer = 4
        End Class
        Sub Main()
            Console.WriteLine(New Derived().Run())
        End Sub
        """, "8", Bk.All);

    /// <summary>A Module variable of ANOTHER FILE, named bare and through its Module, in another case (the imported global; read and write).</summary>
    internal static readonly ProjectProbe MF1 = new("MF1", new[]
        {
            ("Data.bas", """
                Module Data
                    Public Counter As Integer = 7
                End Module
                """),
            ("Main.bas", """
                Sub Main()
                    counter = COUNTER + 1
                    Data.counter = Data.COUNTER * 2
                    Console.WriteLine(Counter)
                End Sub
                """),
        }, "16", Bk.All);

    /// <summary>The same-case control of MF1.</summary>
    internal static readonly ProjectProbe MF1c = new("MF1c", new[]
        {
            ("Data.bas", """
                Module Data
                    Public Counter As Integer = 7
                End Module
                """),
            ("Main.bas", """
                Sub Main()
                    Counter = Counter + 1
                    Data.Counter = Data.Counter * 2
                    Console.WriteLine(Counter)
                End Sub
                """),
        }, "16", Bk.All);

    /// <summary>A class of ANOTHER FILE: New, a field write, a call and a read, all in another case.</summary>
    internal static readonly ProjectProbe MF2 = new("MF2", new[]
        {
            ("Box.bas", """
                Class Box
                    Public Value As Integer
                    Public Sub Bump()
                        Value += 1
                    End Sub
                End Class
                """),
            ("Main.bas", """
                Sub Main()
                    Dim b As box = New BOX()
                    b.value = 3
                    b.bump()
                    Console.WriteLine(b.VALUE)
                End Sub
                """),
        }, "4", Bk.All);

    /// <summary>The same-case control of MF2.</summary>
    internal static readonly ProjectProbe MF2c = new("MF2c", new[]
        {
            ("Box.bas", """
                Class Box
                    Public Value As Integer
                    Public Sub Bump()
                        Value += 1
                    End Sub
                End Class
                """),
            ("Main.bas", """
                Sub Main()
                    Dim b As Box = New Box()
                    b.Value = 3
                    b.Bump()
                    Console.WriteLine(b.Value)
                End Sub
                """),
        }, "4", Bk.All);

    /// <summary>A For and a For Each over ANOTHER FILE'S Module variables, in another case. ⛔ C# is #246 (pinned separately).</summary>
    internal static readonly ProjectProbe MF5 = new("MF5", new[]
        {
            ("Data.bas", """
                Module Data
                    Public Item As Integer = 0
                    Public Total As Integer = 0
                End Module
                """),
            ("Main.bas", """
                Sub Main()
                    For Each item In New Integer() {4, 6}
                    Next
                    For total = 1 To 3
                    Next
                    Console.WriteLine(Data.Item + Data.Total)
                End Sub
                """),
        }, "10", Bk.Cpp | Bk.JavaScript | Bk.Msil);

    /// <summary>The same-case control of MF5. ⛔ C# is #246 (pinned separately).</summary>
    internal static readonly ProjectProbe MF5c = new("MF5c", new[]
        {
            ("Data.bas", """
                Module Data
                    Public Item As Integer = 0
                    Public Total As Integer = 0
                End Module
                """),
            ("Main.bas", """
                Sub Main()
                    For Each Item In New Integer() {4, 6}
                    Next
                    For Total = 1 To 3
                    Next
                    Console.WriteLine(Data.Item + Data.Total)
                End Sub
                """),
        }, "10", Bk.Cpp | Bk.JavaScript | Bk.Msil);

}

/// <summary>Compile a multi-file project on one backend, through one entry point, and run it.</summary>
internal static class BindingProjectExec
{
    internal static IEnumerable<ProjectEntry> Entries(Bk backend) => backend == Bk.Cpp
        ? new[] { ProjectEntry.ProjectStandard, ProjectEntry.ProjectAggressive }
        : Enum.GetValues<ProjectEntry>();

    private static string TargetBackendElement(Bk backend) => backend switch
    {
        Bk.CSharp => "CSharp",
        Bk.Cpp => "Cpp",
        Bk.JavaScript => "JavaScript",
        Bk.Msil => "MSIL",
        _ => throw new ArgumentException(backend.ToString()),
    };

    /// <summary>The project's files in a fresh temp directory, plus a <c>P.blproj</c> for the CLI.</summary>
    private static string Materialise(Bk backend, IEnumerable<(string Name, string Source)> files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t124-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (name, source) in files) File.WriteAllText(Path.Combine(dir, name), source);
        File.WriteAllText(Path.Combine(dir, "P.blproj"),
            "<BasicLangProject>\n  <PropertyGroup><ProjectName>P</ProjectName><AssemblyName>P</AssemblyName>"
            + $"<OutputType>Exe</OutputType><TargetBackend>{TargetBackendElement(backend)}</TargetBackend></PropertyGroup>\n</BasicLangProject>\n");
        return dir;
    }

    /// <summary>The CLI's C# project build runs <c>dotnet build</c> on the generated project.</summary>
    internal static void RequireDotnetForCliCSharp(Bk backend)
    {
        if (backend == Bk.CSharp && !CliTestHarness.DotnetOnPath())
            Assert.Ignore("dotnet SDK not found on PATH — the CLI's C# backend cannot build the generated project.");
    }

    /// <summary><c>BasicLang build P.blproj -c &lt;cfg&gt;</c>: the exit code and everything it printed.</summary>
    internal static (int Exit, string Output, string Dir) CliBuild(Bk backend, string configuration, (string Name, string Source)[] files)
    {
        var dir = Materialise(backend, files);
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", "P.blproj", "-c", configuration }, dir, timeoutMs: 300_000);
        return (exit, stdout + stderr, dir);
    }

    /// <summary>The generated text, asserting the compile succeeded.</summary>
    internal static string Emit(Bk backend, ProjectEntry entry, (string Name, string Source)[] files)
    {
        if (entry is ProjectEntry.CliDebug or ProjectEntry.CliRelease)
        {
            var (exit, output, dir) = CliBuild(backend, entry == ProjectEntry.CliDebug ? "Debug" : "Release", files);
            try
            {
                var extension = TempExec.Extension(backend);
                var built = Directory.Exists(Path.Combine(dir, "bin"))
                    ? Directory.GetFiles(Path.Combine(dir, "bin"), "P" + extension, SearchOption.AllDirectories).FirstOrDefault()
                    : null;
                Assert.That(exit, Is.EqualTo(0), $"BasicLang build ({entry}, {backend}) failed:\n{output}");
                Assert.That(built, Is.Not.Null, $"the CLI wrote no P{extension}:\n{output}");
                return File.ReadAllText(built!);
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
        }

        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = entry == ProjectEntry.ProjectAggressive });
        var projectDir = Materialise(backend, files);
        try
        {
            var result = compiler.CompileProjectFiles(files.Select(f => Path.Combine(projectDir, f.Name)).ToList());
            Assert.That(result.HasErrors, Is.False, string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            var ir = result.CombinedIR;
            return backend switch
            {
                Bk.CSharp => new ImprovedCSharpCodeGenerator().Generate(ir),
                Bk.Cpp => new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir),
                Bk.JavaScript => new JavaScriptCodeGenerator().Generate(ir),
                Bk.Msil => new MSILCodeGenerator().Generate(ir),
                _ => throw new ArgumentException(backend.ToString()),
            };
        }
        finally { try { Directory.Delete(projectDir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>One project, one backend, EVERY entry point: each must print vbc's answer. A failure in one entry point is
    /// collected, not thrown, so the others still report.</summary>
    internal static void AssertMatchesInEveryEntryPoint(Bk backend, ProjectProbe probe)
    {
        TempExec.RequireTool(backend);
        RequireDotnetForCliCSharp(backend);
        var failures = new List<string>();
        foreach (var entry in Entries(backend))
        {
            try
            {
                var got = TempExec.Norm(TempExec.Run(backend, Emit(backend, entry, probe.Files)));
                if (got != TempExec.Norm(probe.Vb))
                    failures.Add($"{entry}: printed [{got.Replace("\n", " | ")}] where VB prints [{TempExec.Norm(probe.Vb).Replace("\n", " | ")}]");
            }
            catch (AssertionException ex)
            {
                failures.Add($"{entry}: {ex.Message.Split('\n')[0]}");
            }
        }
        Assert.That(failures, Is.Empty, $"{probe.Id} on {backend}, through {string.Join(", ", Entries(backend))}:\n" + string.Join("\n", failures));
    }
}

/// <summary>
/// ADR-0013 D3 (#124): a name spelled in another case from its declaration reaches every backend under the DECLARED
/// spelling, so the program does what VB does. Each case-differing row, and its same-case control, prints vbc's answer.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class NameBindingResolutionExecutionTests
{
    private static IEnumerable<TestCaseData> Cells(params TempProbe[] probes)
        => probes.SelectMany(p => TempExec.Backends(p.Agrees).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> ProjectCells(params ProjectProbe[] probes)
        => probes.SelectMany(p => TempExec.Backends(p.Agrees).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> VariantCells() => Cells(BindingProbes.FR, BindingProbes.FW, BindingProbes.FL, BindingProbes.FIn, BindingProbes.FShB, BindingProbes.PRa, BindingProbes.FEf, BindingProbes.FEla, BindingProbes.FOf, BindingProbes.FOg, BindingProbes.FOgf, BindingProbes.FOl, BindingProbes.FOla, BindingProbes.FOp, BindingProbes.MEa, BindingProbes.MEb, BindingProbes.MEs, BindingProbes.MEt, BindingProbes.TYa, BindingProbes.EVb, BindingProbes.AWa, BindingProbes.MGw, BindingProbes.EVr, BindingProbes.NIb, BindingProbes.FOsb);

    private static IEnumerable<TestCaseData> ControlCells() => Cells(BindingProbes.FRc, BindingProbes.FWc, BindingProbes.FLc, BindingProbes.FInc, BindingProbes.FShBc, BindingProbes.PRac, BindingProbes.FEfc, BindingProbes.FElac, BindingProbes.FOfc, BindingProbes.FOgc, BindingProbes.FOgfc, BindingProbes.FOlc, BindingProbes.FOlac, BindingProbes.FOpc, BindingProbes.MEac, BindingProbes.MEbc, BindingProbes.MEsc, BindingProbes.MEtc, BindingProbes.TYac, BindingProbes.EVbc, BindingProbes.AWac, BindingProbes.MGwc, BindingProbes.EVrc, BindingProbes.NIbc);

    private static IEnumerable<TestCaseData> ProjectVariantCells() => ProjectCells(BindingProbes.MF1, BindingProbes.MF2, BindingProbes.MF5);

    private static IEnumerable<TestCaseData> ProjectControlCells() => ProjectCells(BindingProbes.MF1c, BindingProbes.MF2c, BindingProbes.MF5c);

    /// <summary>
    /// The tables ARE the proof, so their shape is pinned: a row cannot vanish (or a backend be dropped from a row) without this test
    /// saying so. It is also the fixture's one plain <c>[Test]</c> — <c>JsExecutionTierRosterTests</c> counts attributes, and a fixture
    /// whose tests are all <c>[TestCaseSource]</c> counts as empty. When #246 lands, FOg/FOgc/MF5/MF5c gain C# and this changes on purpose.
    /// </summary>
    [Test]
    public void TheTables_HaveTheirRows()
    {
        static string Ids(IEnumerable<TestCaseData> cells) =>
            string.Join(",", cells.Select(c => c.Arguments[0]!.ToString()).Distinct());

        Assert.Multiple(() =>
        {
            Assert.That(Ids(VariantCells()), Is.EqualTo("FR,FW,FL,FIn,FShB,PRa,FEf,FEla,FOf,FOg,FOgf,FOl,FOla,FOp,MEa,MEb,MEs,MEt,TYa,EVb,AWa,MGw,EVr,NIb,FOsb"));
            Assert.That(Ids(ControlCells()), Is.EqualTo("FRc,FWc,FLc,FInc,FShBc,PRac,FEfc,FElac,FOfc,FOgc,FOgfc,FOlc,FOlac,FOpc,MEac,MEbc,MEsc,MEtc,TYac,EVbc,AWac,MGwc,EVrc,NIbc"));
            Assert.That(Ids(ProjectVariantCells()), Is.EqualTo("MF1,MF2,MF5"));
            Assert.That(Ids(ProjectControlCells()), Is.EqualTo("MF1c,MF2c,MF5c"));
            Assert.That(VariantCells().Count(), Is.EqualTo(91), "case-differing cells (id x backend)");
            Assert.That(ControlCells().Count(), Is.EqualTo(87), "same-case control cells (id x backend)");
            Assert.That(ProjectVariantCells().Count(), Is.EqualTo(11), "MF1 x4, MF2 x4, MF5 x3 (C# is #246)");
            Assert.That(ProjectControlCells().Count(), Is.EqualTo(11), "MF1c x4, MF2c x4, MF5c x3 (C# is #246)");
        });
    }

    // ============================================================================================
    // Single-file: the case-differing program, and its control
    // ============================================================================================

    /// <summary>
    /// A field, an inherited field, a Shared field, an auto-property, a For / For Each control over a local, a parameter, a
    /// field or a global, AddressOf a Sub, <c>MyBase.m</c>, a Shared method through its class, <c>New</c>, AddHandler,
    /// RaiseEvent, an Await callee and a Module variable used before its Module — each spelled in another case from its
    /// declaration — prints VB's answer through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>.
    /// FOsb (no case difference: an earlier closed block's <c>Dim i</c> and a counted <c>For i</c>) rides here too, as the
    /// fallback row for the Ordinal storage test that only runs when the analyzer binds nothing.
    /// </summary>
    [TestCaseSource(nameof(VariantCells))]
    public void ACaseDifferingReference_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    /// <summary>The same programs spelled EXACTLY as declared — what was already right before #124, and must stay so.</summary>
    [TestCaseSource(nameof(ControlCells))]
    public void TheSameCaseControl_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id + " (same-case control)");

    // ============================================================================================
    // Multi-file
    // ============================================================================================

    /// <summary>
    /// MF1 (another file's Module variable, bare and qualified), MF2 (another file's class: <c>New</c>, a store, a call, a read)
    /// and MF5 (a For and a For Each over another file's Module variables), each in another case.
    /// </summary>
    [TestCaseSource(nameof(ProjectVariantCells))]
    public void AProjectWithACaseDifferingReference_PrintsVbsAnswer_InEveryEntryPoint(ProjectProbe probe, Bk backend)
        => BindingProjectExec.AssertMatchesInEveryEntryPoint(backend, probe);

    [TestCaseSource(nameof(ProjectControlCells))]
    public void AProjectSpelledAsDeclared_PrintsVbsAnswer_InEveryEntryPoint(ProjectProbe probe, Bk backend)
        => BindingProjectExec.AssertMatchesInEveryEntryPoint(backend, probe);

    // ============================================================================================
    // ⛔ KNOWN DEFECT CELLS — pinned at their CURRENT state, so the day the follow-up lands they go red
    // ============================================================================================

    private static string[] RoslynErrors(string csharp)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            "T124Pin_" + Guid.NewGuid().ToString("N"),
            new[] { CSharpSyntaxTree.ParseText(csharp) },
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var ms = new MemoryStream();
        var emitted = compilation.Emit(ms);
        return emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()).ToArray();
    }

    private static void AssertRejectedWithCs0103(string csharp, string undeclaredName, string what)
    {
        var errors = RoslynErrors(csharp);
        Assert.That(errors, Is.Not.Empty, $"{what}: the emitted C# COMPILES now — task #246 has landed; promote this pin to VB's answer.\n--- emitted ---\n{csharp}");
        Assert.That(errors, Has.All.Contain("CS0103").And.Contain($"'{undeclaredName}'"),
            $"{what}: rejected, but not by the known defect (an undeclared '{undeclaredName}'):\n" + string.Join("\n", errors));
    }

    /// <summary>
    /// ⛔ #246 — C#: a For over ANOTHER module's global writes its increment to a plain variable. FOg (case-differing) and FOgc
    /// (its control) are the SAME defect: it predates #124 (the control was CS0103 before too; FOg went from a wrong answer to
    /// CS0103 because #124 stopped it driving nothing). Pinned through the CLI, the CLI with <c>--optimize</c> and
    /// <c>CompileProjectFiles</c>; every other backend prints VB's 4 (the rows above).
    /// </summary>
    [TestCase("FOg", TestName = "FOg_OnCSharp_IsCS0103_KnownDefect_Task246")]
    [TestCase("FOgc", TestName = "FOgc_OnCSharp_IsCS0103_KnownDefect_Task246")]
    public void AForOverAnotherModulesGlobal_OnCSharp_IsCS0103_KnownDefect_Task246(string id)
    {
        var probe = id == "FOg" ? BindingProbes.FOg : BindingProbes.FOgc;
        Assert.Multiple(() =>
        {
            foreach (var entry in Enum.GetValues<EntryPoint>())
                AssertRejectedWithCs0103(TempExec.Emit(Bk.CSharp, entry, probe.Source), "G", $"{id}, {entry}");
        });
    }

    /// <summary>
    /// ⛔ #246 — MF5c on C# (a For and a For Each over another FILE's Module variables, spelled as declared): WRONG before #124
    /// (it printed a number that was not VB's), CS0103 now — the increment of the counted For, written to an undeclared
    /// <c>Total</c>. MF5 (case-differing) is the same defect. Pinned through <c>BasicLang build</c> (Release) and
    /// <c>CompileProjectFiles</c> (standard and aggressive), so the day #246 lands they go red.
    /// </summary>
    [TestCase("MF5", TestName = "MF5_OnCSharp_IsCS0103_KnownDefect_Task246")]
    [TestCase("MF5c", TestName = "MF5c_OnCSharp_IsCS0103_KnownDefect_Task246")]
    public void AForOverAnotherFilesModuleGlobal_OnCSharp_IsCS0103_KnownDefect_Task246(string id)
    {
        var probe = id == "MF5" ? BindingProbes.MF5 : BindingProbes.MF5c;
        BindingProjectExec.RequireDotnetForCliCSharp(Bk.CSharp);
        Assert.Multiple(() =>
        {
            foreach (var entry in new[] { ProjectEntry.ProjectStandard, ProjectEntry.ProjectAggressive })
                AssertRejectedWithCs0103(BindingProjectExec.Emit(Bk.CSharp, entry, probe.Files), "Total", $"{id}, {entry}");

            var (exit, output, dir) = BindingProjectExec.CliBuild(Bk.CSharp, "Release", probe.Files);
            try
            {
                Assert.That(exit, Is.Not.EqualTo(0), $"{id}: `BasicLang build` now SUCCEEDS on C# — task #246 has landed; promote this pin.\n{output}");
                Assert.That(output, Does.Contain("CS0103").And.Contain("'Total'"), $"{id}: `BasicLang build` failed, but not by the known defect:\n{output}");
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
        });
    }
}
