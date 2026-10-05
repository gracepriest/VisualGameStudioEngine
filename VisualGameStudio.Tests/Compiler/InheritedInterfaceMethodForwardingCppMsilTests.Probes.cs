using System.Collections.Generic;

namespace VisualGameStudio.Tests.Compiler;

// Rows taken from the implementer's #131 probes (S/t131/probes/mat) and two written for these tests (m27x, m28x), each source as it was run. A row's expected text is vbc's output for the row's
// CONTROL (the same program with the class declaring the method itself, an `Implements I.M` clause on it): vbc rejects every inherited shape with BC30149, because VB needs the clause on the member.
// The text was stored by the implementer as `probes/mat/<id>.exp` (m27x, m28x: run by the test-writer with the SDK's vbc). `Agrees` is Cpp | Msil for the rows the run test uses, 0 for the rows
// only the fast tests read (m02x, m03x, m09x, m10b, m16x, m18s, m27x, m28x). Edit this file by hand like any other.

/// <summary>
/// The programs of <see cref="InheritedInterfaceMethodForwardingCppMsilTests"/>, <c>InheritedInterfaceMethodLookupTests</c> and <c>InheritedInterfaceMethodForwardingTextTests</c>. Every program calls the
/// method through the CLASS, through an INTERFACE variable and through a `List(Of IShape)` loop (m25x: the class and both interfaces; m18s: the interface only).
/// </summary>
internal static class InheritedMethodProbes
{

    /// <summary>m01x: a Function with two ByVal Integer parameters, inherited from the direct base.</summary>
    internal static readonly TempProbe m01x_function = new("m01x_function", """
        Interface IShape
            Function Area(w As Integer, h As Integer) As Integer
        End Interface

        Class BaseShape
            Public Function Area(w As Integer, h As Integer) As Integer
                Return w * h
            End Function
        End Class

        Class Rect
            Inherits BaseShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim r As Rect = New Rect()
                Console.WriteLine(r.Area(2, 3))
                Dim s As IShape = r
                Console.WriteLine(s.Area(4, 5))
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Rect())
                items.Add(r)
                For Each it As IShape In items
                    Console.WriteLine(it.Area(1, 7))
                Next
            End Sub
        End Module
        """, """
        6
        20
        7
        7
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m02x: a Sub with a String parameter (C++: no `return` in the forwarder).</summary>
    internal static readonly TempProbe m02x_sub_string = new("m02x_sub_string", """
        Interface ILog
            Sub Write(msg As String)
        End Interface

        Class BaseLog
            Public Sub Write(msg As String)
                Console.WriteLine("log:" & msg)
            End Sub
        End Class

        Class FileLog
            Inherits BaseLog
            Implements ILog
        End Class

        Module M
            Sub Main()
                Dim r As FileLog = New FileLog()
                r.Write("a")
                Dim s As ILog = r
                s.Write("b")
                Dim items As List(Of ILog) = New List(Of ILog)()
                items.Add(New FileLog())
                items.Add(r)
                For Each it As ILog In items
                    it.Write("c")
                Next
            End Sub
        End Module
        """, """
        log:a
        log:b
        log:c
        log:c
        """, 0, HangSafe: true);

    /// <summary>m28x: five parameters of three types, each weighted differently, so an argument passed in another order or dropped shows (MSIL: `ldarg.s` past the third).</summary>
    /// <remarks>Kills: an off-by-one in the `ldarg` loop.</remarks>
    internal static readonly TempProbe m28x_fiveparams = new("m28x_fiveparams", """
        Interface IMixer
            Function Mix(a As Integer, b As String, c As Integer, d As Integer, e As Integer) As String
        End Interface

        Class BaseMixer
            Public Function Mix(a As Integer, b As String, c As Integer, d As Integer, e As Integer) As String
                Return b & (a + c * 10 + d * 100 + e * 1000)
            End Function
        End Class

        Class Mixer
            Inherits BaseMixer
            Implements IMixer
        End Class

        Module M
            Sub Main()
                Dim r As Mixer = New Mixer()
                Console.WriteLine(r.Mix(1, "p", 2, 3, 4))
                Dim s As IMixer = r
                Console.WriteLine(s.Mix(5, "q", 6, 7, 8))
                Dim items As List(Of IMixer) = New List(Of IMixer)()
                items.Add(New Mixer())
                items.Add(r)
                For Each it As IMixer In items
                    Console.WriteLine(it.Mix(9, "r", 1, 2, 3))
                Next
            End Sub
        End Module
        """, """
        p4321
        q8765
        r3219
        r3219
        """, 0, HangSafe: true);

    /// <summary>m09x: the method is declared on the GRANDPARENT; the parent declares nothing.</summary>
    internal static readonly TempProbe m09x_grandparent = new("m09x_grandparent", """
        Interface IShape
            Function Area(w As Integer, h As Integer) As Integer
        End Interface

        Class GrandShape
            Public Function Area(w As Integer, h As Integer) As Integer
                Return w * h
            End Function
        End Class

        Class MidShape
            Inherits GrandShape
        End Class

        Class Rect
            Inherits MidShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim r As Rect = New Rect()
                Console.WriteLine(r.Area(2, 3))
                Dim s As IShape = r
                Console.WriteLine(s.Area(4, 5))
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Rect())
                items.Add(r)
                For Each it As IShape In items
                    Console.WriteLine(it.Area(1, 7))
                Next
            End Sub
        End Module
        """, """
        6
        20
        7
        7
        """, 0, HangSafe: true);

    /// <summary>m17x: the parent AND the grandparent declare Area(); the nearest wins (2, not 1).</summary>
    /// <remarks>Kills: M4 the farthest base preferred.</remarks>
    internal static readonly TempProbe m17x_nearestbase = new("m17x_nearestbase", """
        Interface IShape
            Function Area() As Integer
        End Interface

        Class GrandShape
            Public Function Area() As Integer
                Return 1
            End Function
        End Class

        Class MidShape
            Inherits GrandShape
            Public Function Area() As Integer
                Return 2
            End Function
        End Class

        Class Sq
            Inherits MidShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim r As Sq = New Sq()
                Console.WriteLine(r.Area())
                Dim s As IShape = r
                Console.WriteLine(s.Area())
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Sq())
                items.Add(r)
                For Each it As IShape In items
                    Console.WriteLine(it.Area())
                Next
            End Sub
        End Module
        """, """
        2
        2
        2
        2
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m08x: an Overridable inherited method with an Overrides further down, called through the interface (3 for the Big in the list, not the base's 1).</summary>
    /// <remarks>Kills: M6 `call` for `callvirt`.</remarks>
    internal static readonly TempProbe m08x_overridable = new("m08x_overridable", """
        Interface IShape
            Function Area() As Integer
        End Interface

        Class BaseShape
            Public Overridable Function Area() As Integer
                Return 1
            End Function
        End Class

        Class Sq
            Inherits BaseShape
            Implements IShape
        End Class

        Class Big
            Inherits Sq
            Public Overrides Function Area() As Integer
                Return 3
            End Function
        End Class

        Module M
            Sub Main()
                Dim a As IShape = New Sq()
                Dim q0 As Sq = New Big()
                Dim b As IShape = q0
                Console.WriteLine(a.Area())
                Console.WriteLine(b.Area())
                Dim bb As BaseShape = New Big()
                Console.WriteLine(bb.Area())
                Dim q As Sq = New Big()
                Console.WriteLine(q.Area())
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Sq())
                items.Add(q0)
                For Each it As IShape In items
                    Console.WriteLine(it.Area())
                Next
            End Sub
        End Module
        """, """
        1
        3
        3
        3
        1
        3
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m24x: the NEARER base declares Area(Long) and the farther one Area(Integer): the slot is filled by the farther one (6, not 300). JavaScript refuses the Long (BL7003).</summary>
    /// <remarks>Kills: M12 the C++ call qualified by the direct base (prints 300 | 400).</remarks>
    internal static readonly TempProbe m24x_nearersamename = new("m24x_nearersamename", """
        Interface IShape
            Function Area(w As Integer) As Integer
        End Interface

        Class GrandShape
            Public Function Area(w As Integer) As Integer
                Return w * 2
            End Function
        End Class

        Class MidShape
            Inherits GrandShape
            Public Function Area(w As Long) As Long
                Return w * 100
            End Function
        End Class

        Class Rect
            Inherits MidShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim r As Rect = New Rect()
                Dim s As IShape = r
                Console.WriteLine(s.Area(3))
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Rect())
                For Each it As IShape In items
                    Console.WriteLine(it.Area(4))
                Next
            End Sub
        End Module
        """, """
        6
        8
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m26x: the nearer base declares Area(Double) As Integer, the farther Area(Integer) As Integer: the parameter TYPE decides (6, not 999).</summary>
    /// <remarks>Kills: M10 parameter types not compared.</remarks>
    internal static readonly TempProbe m26x_nearerdouble = new("m26x_nearerdouble", """
        Interface IShape
            Function Area(w As Integer) As Integer
        End Interface

        Class GrandShape
            Public Function Area(w As Integer) As Integer
                Return w * 2
            End Function
        End Class

        Class MidShape
            Inherits GrandShape
            Public Function Area(w As Double) As Integer
                Return 999
            End Function
        End Class

        Class Rect
            Inherits MidShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim r As Rect = New Rect()
                Dim s As IShape = r
                Console.WriteLine(s.Area(3))
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Rect())
                For Each it As IShape In items
                    Console.WriteLine(it.Area(4))
                Next
            End Sub
        End Module
        """, """
        6
        8
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m27x: two interfaces declare Area with different parameter TYPES (Integer, String), filled by two different bases: two forwarders for one name.</summary>
    /// <remarks>Kills: a C++ dedupe on the name alone.</remarks>
    internal static readonly TempProbe m27x_twointerfaces_othertypes = new("m27x_twointerfaces_othertypes", """
        Interface IA
            Function Area(w As Integer) As Integer
        End Interface

        Interface IB
            Function Area(s As String) As Integer
        End Interface

        Class GrandShape
            Public Function Area(w As Integer) As Integer
                Return w * 2
            End Function
        End Class

        Class MidShape
            Inherits GrandShape
            Public Function Area(s As String) As Integer
                Return s.Length * 100
            End Function
        End Class

        Class Both
            Inherits MidShape
            Implements IA, IB
        End Class

        Module M
            Sub Main()
                Dim r As Both = New Both()
                Console.WriteLine(r.Area("abc"))
                Dim a As IA = r
                Dim b As IB = r
                Console.WriteLine(a.Area(5))
                Console.WriteLine(b.Area("hello"))
                Dim ia As List(Of IA) = New List(Of IA)()
                ia.Add(r)
                Dim ib As List(Of IB) = New List(Of IB)()
                ib.Add(New Both())
                For Each x As IA In ia
                    Console.WriteLine(x.Area(7))
                Next
                For Each y As IB In ib
                    Console.WriteLine(y.Area("hi"))
                Next
            End Sub
        End Module
        """, """
        300
        10
        500
        14
        200
        """, 0, HangSafe: true);

    /// <summary>m06y: the class declares Area() itself and INHERITS the Area(scale) its interface asks for: a same-named own method fills no slot.</summary>
    /// <remarks>Kills: M5 the class's own method matched by name only.</remarks>
    internal static readonly TempProbe m06y_ownandinherited = new("m06y_ownandinherited", """
        Interface IShape
            Function Area(scale As Integer) As Integer
        End Interface

        Class BaseShape
            Public Function Area(scale As Integer) As Integer
                Return 10 * scale
            End Function
        End Class

        Class Sq
            Inherits BaseShape
            Implements IShape
            Public Function Area() As Integer
                Return 10
            End Function
        End Class

        Module M
            Sub Main()
                Dim r As Sq = New Sq()
                Console.WriteLine(r.Area())
                Dim s As IShape = r
                Console.WriteLine(s.Area(3))
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Sq())
                items.Add(r)
                For Each it As IShape In items
                    Console.WriteLine(it.Area(4))
                Next
            End Sub
        End Module
        """, """
        10
        30
        40
        40
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m16x: the base spells the name AREA, the interface Area (VB is case-insensitive).</summary>
    /// <remarks>Kills: M3 a case-sensitive name.</remarks>
    internal static readonly TempProbe m16x_othercase = new("m16x_othercase", """
        Interface IShape
            Function Area() As Integer
        End Interface

        Class BaseShape
            Public Function AREA() As Integer
                Return 16
            End Function
        End Class

        Class Sq
            Inherits BaseShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim r As Sq = New Sq()
                Console.WriteLine(r.Area())
                Dim s As IShape = r
                Console.WriteLine(s.Area())
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(New Sq())
                items.Add(r)
                For Each it As IShape In items
                    Console.WriteLine(it.Area())
                Next
            End Sub
        End Module
        """, """
        16
        16
        16
        16
        """, 0, HangSafe: true);

    /// <summary>m14x: two interfaces declare the same Name(): one slot each (MSIL: two stubs; C++: one forwarder overrides both).</summary>
    /// <remarks>Kills: M8 forwarders not deduplicated.</remarks>
    internal static readonly TempProbe m14x_twointerfaces = new("m14x_twointerfaces", """
        Interface IA
            Function Name() As String
        End Interface

        Interface IB
            Function Name() As String
        End Interface

        Class BaseBoth
            Public Function Name() As String
                Return "nm"
            End Function
        End Class

        Class Both
            Inherits BaseBoth
            Implements IA, IB
        End Class

        Module M
            Sub Main()
                Dim r As Both = New Both()
                Console.WriteLine(r.Name())
                Dim a As IA = r
                Dim b As IB = r
                Console.WriteLine(a.Name() & b.Name())
                Dim items As List(Of IB) = New List(Of IB)()
                items.Add(New Both())
                For Each it As IB In items
                    Console.WriteLine(it.Name())
                Next
            End Sub
        End Module
        """, """
        nm
        nmnm
        nm
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m25x: two interfaces declare Pick(a) and Pick(b): the same C++ signature under two parameter names.</summary>
    /// <remarks>Kills: M11 forwarders deduplicated by declarator, names included.</remarks>
    internal static readonly TempProbe m25x_twointerfaces_othernames = new("m25x_twointerfaces_othernames", """
        Interface IA
            Function Pick(a As Integer) As Integer
        End Interface

        Interface IB
            Function Pick(b As Integer) As Integer
        End Interface

        Class BasePick
            Public Function Pick(n As Integer) As Integer
                Return n + 100
            End Function
        End Class

        Class Both
            Inherits BasePick
            Implements IA, IB
        End Class

        Module M
            Sub Main()
                Dim r As Both = New Both()
                Dim a As IA = r
                Dim b As IB = r
                Console.WriteLine(r.Pick(1))
                Console.WriteLine(a.Pick(2))
                Console.WriteLine(b.Pick(3))
            End Sub
        End Module
        """, """
        101
        102
        103
        """, Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>m10b: the BASE lists the interface and declares the method; the derived class lists nothing: nothing to forward, and nothing must be.</summary>
    /// <remarks>Kills: a walk of the base's interface list.</remarks>
    internal static readonly TempProbe m10b_baseimplements = new("m10b_baseimplements", """
        Interface IShape
            Function Area(w As Integer, h As Integer) As Integer
        End Interface

        Class BaseShape
            Implements IShape
            Public Function Area(w As Integer, h As Integer) As Integer
                Return w * h
            End Function
        End Class

        Class Rect
            Inherits BaseShape
        End Class

        Module M
            Sub Main()
                Dim r As Rect = New Rect()
                Console.WriteLine(r.Area(2, 3))
                Dim rb As BaseShape = r
                Dim s As IShape = rb
                Console.WriteLine(s.Area(4, 5))
                Dim items As List(Of IShape) = New List(Of IShape)()
                items.Add(rb)
                items.Add(rb)
                For Each it As IShape In items
                    Console.WriteLine(it.Area(1, 7))
                Next
            End Sub
        End Module
        """, """
        6
        20
        7
        7
        """, 0, HangSafe: true);

    /// <summary>m03x: a ByRef parameter: C# and C++ print vbc's text; JavaScript refuses it (BL7002) and MSIL cannot load the class (the interface declares ByRef without its `&`).</summary>
    /// <remarks>Kills: M7 the IL-signature guard removed (InvalidProgramException).</remarks>
    internal static readonly TempProbe m03x_byref = new("m03x_byref", """
        Interface ICounter
            Sub Bump(ByRef x As Integer)
        End Interface

        Class BaseCounter
            Public Sub Bump(ByRef x As Integer)
                x = x + 10
            End Sub
        End Class

        Class Counter
            Inherits BaseCounter
            Implements ICounter
        End Class

        Module M
            Sub Main()
                Dim r As Counter = New Counter()
                Dim n As Integer = 1
                r.Bump(n)
                Console.WriteLine(n)
                Dim s As ICounter = r
                s.Bump(n)
                Console.WriteLine(n)
                Dim items As List(Of ICounter) = New List(Of ICounter)()
                items.Add(New Counter())
                items.Add(r)
                For Each it As ICounter In items
                    it.Bump(n)
                    Console.WriteLine(n)
                Next
            End Sub
        End Module
        """, """
        11
        21
        31
        41
        """, 0, HangSafe: true);

    /// <summary>m18s: the base method is Shared (invalid VB: BC30149): it fills nothing, so nothing is forwarded.</summary>
    /// <remarks>Kills: M9 a Shared method listed.</remarks>
    internal static readonly TempProbe m18s_shared = new("m18s_shared", """
        Interface IShape
            Function Area() As Integer
        End Interface

        Class BaseShape
            Public Shared Function Area() As Integer
                Return 18
            End Function
        End Class

        Class Sq
            Inherits BaseShape
            Implements IShape
        End Class

        Module M
            Sub Main()
                Dim s As IShape = New Sq()
                Console.WriteLine(s.Area())
            End Sub
        End Module
        """, "", 0, HangSafe: true);

    /// <summary>Every row, in the table's order.</summary>
    internal static readonly IReadOnlyList<TempProbe> All = new[]
    {
        m01x_function, m02x_sub_string, m28x_fiveparams,
        m09x_grandparent, m17x_nearestbase, m08x_overridable,
        m24x_nearersamename, m26x_nearerdouble, m27x_twointerfaces_othertypes,
        m06y_ownandinherited, m16x_othercase, m14x_twointerfaces,
        m25x_twointerfaces_othernames, m10b_baseimplements, m03x_byref,
        m18s_shared,
    };
}
