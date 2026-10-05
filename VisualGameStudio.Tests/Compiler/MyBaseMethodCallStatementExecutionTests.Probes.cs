using System.Collections.Generic;

namespace VisualGameStudio.Tests.Compiler;

// Rows taken verbatim from the implementer's probes (S/t139/probes/m) or written for #139 (S/t139/tw/x), every source as it was run, every expected value
// vbc's own output (the program wrapped in a VB Module and compiled with the SDK's vbc, then run). A row's Agrees flags are the backends whose three entry points
// print that text on this build (S/t139/mat-after-m.txt and S/t139/tw/x-matrix*.txt). Edit this file by hand like any other.

/// <summary>
/// The probe table of <see cref="MyBaseMethodCallStatementExecutionTests"/>. ⛔ Every row is HangSafe: its C# run goes through <see cref="CSharpProcessRunner"/>
/// (some rows hold loops). Every row carries a counter or a print inside the base method, so a call that is DROPPED and a call that is DOUBLED both change the text.
/// </summary>
internal static class MyBaseCallProbes
{
    // ================================================================================================
    // A. The statement forms: a Sub, a Function whose result is discarded, no arguments
    // ================================================================================================

    /// <summary>a Sub, from the override of that Sub.</summary>
    internal static readonly TempProbe s1_sub = new("s1_sub", """
        ' MyBase.Sub(args) as a statement, from an override of the same Sub
        Class Base
            Public K As Integer
            Public Overridable Sub Show(n As Integer)
                K = K + n
                Console.WriteLine("base " & n)
            End Sub
            Public Sub Other(n As Integer)
                K = K + n
                Console.WriteLine("other " & n)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Show(n As Integer)
                MyBase.Show(n + 1)
                Console.WriteLine("derived " & n & " K=" & K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Show(5)
            Console.WriteLine(d.K)
        End Sub
        """, """
        base 6
        derived 5 K=6
        6
        """, Bk.All, HangSafe: true);

    /// <summary>a Function whose result is discarded, from another method.</summary>
    internal static readonly TempProbe s2_func = new("s2_func", """
        ' MyBase.F(args) as a statement, F a Function whose result is discarded
        Class Base
            Public K As Integer
            Public Overridable Function F(n As Integer) As Integer
                K = K + n
                Console.WriteLine("F " & n)
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                K = K + n
                Console.WriteLine("G " & n)
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Console.WriteLine("derived F")
                Return 0
            End Function
            Public Sub Work(x As Integer)
                MyBase.F(x + 1)
                Console.WriteLine("K=" & K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        F 5
        K=5
        """, Bk.All, HangSafe: true);

    /// <summary>a Function whose result is discarded, inside the override of that same Function (which returns its own result separately).</summary>
    internal static readonly TempProbe s2b_funcself = new("s2b_funcself", """
        ' MyBase.F(args) as a statement inside the override of F itself, whose own result is returned separately
        Class Base
            Public K As Integer
            Public Overridable Function F(n As Integer) As Integer
                K = K + n
                Console.WriteLine("base F " & n)
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                K = K + n
                Console.WriteLine("G " & n)
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                MyBase.F(n)
                Return K * 10
            End Function
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.F(3))
        End Sub
        """, """
        base F 3
        30
        """, Bk.All, HangSafe: true);

    /// <summary>discarded String, Boolean and Double Functions: a bare call whatever the result type (the old visit declared `T tN = ...` for each).</summary>
    internal static readonly TempProbe s2c_types = new("s2c_types", """
        ' a discarded String-returning Function, a discarded Boolean one, and a discarded Structure-free Double one
        Class Base
            Public Calls As Integer
            Public Overridable Function S(n As Integer) As String
                Calls = Calls + 1
                Return "s" & n
            End Function
            Public Overridable Function B(n As Integer) As Boolean
                Calls = Calls + 10
                Return n > 0
            End Function
            Public Overridable Function D(n As Integer) As Double
                Calls = Calls + 100
                Return n / 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Sub Work()
                MyBase.S(1)
                MyBase.B(1)
                MyBase.D(1)
                Console.WriteLine(Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        111
        """, Bk.All, HangSafe: true);

    /// <summary>a discarded `MyBase.ToString()`, a member the base overrides from Object. C++ has no cell: `override` on ToString is a clang error that has nothing to do with the call.</summary>
    internal static readonly TempProbe s2d_tostring = new("s2d_tostring", """
        ' MyBase.ToString() discarded, and MyBase.GetHashCode() used (an Object member through MyBase)
        Class Base
            Public Overrides Function ToString() As String
                Console.WriteLine("base ToString")
                Return "B"
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function ToString() As String
                MyBase.ToString()
                Return "D"
            End Function
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.ToString())
        End Sub
        """, """
        base ToString
        D
        """, Bk.CSharp | Bk.JavaScript | Bk.Msil, HangSafe: true);

    /// <summary>no arguments.</summary>
    internal static readonly TempProbe s3_noargs = new("s3_noargs", """
        ' MyBase.M() with no arguments
        Class Base
            Public K As Integer
            Public Overridable Sub Bump()
                K = K + 10
            End Sub
            Public Sub Bump2()
                K = K + 10
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump()
                MyBase.Bump()
                K = K + 1
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Bump()
            d.Bump()
            Console.WriteLine(d.K)
        End Sub
        """, """
        22
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // B. Contexts: where the statement sits
    // ================================================================================================

    /// <summary>a constructor, after `MyBase.New(...)`.</summary>
    internal static readonly TempProbe c1_ctor = new("c1_ctor", """
        ' MyBase.M() inside a constructor after MyBase.New
        Class Base
            Public K As Integer
            Public Sub New(k0 As Integer)
                K = k0
            End Sub
            Public Overridable Sub Init(n As Integer)
                K = K + n
                Console.WriteLine("init " & n)
            End Sub
            Public Sub Init2(n As Integer)
                K = K + n
                Console.WriteLine("init2 " & n)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub New()
                MyBase.New(10)
                MyBase.Init(5)
                Console.WriteLine("ctor K=" & K)
            End Sub
            Public Overrides Sub Init(n As Integer)
                Console.WriteLine("derived init")
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.K)
        End Sub
        """, """
        init 5
        ctor K=15
        15
        """, Bk.All, HangSafe: true);

    /// <summary>a property Get and a property Set.</summary>
    internal static readonly TempProbe c2_prop = new("c2_prop", """
        ' MyBase.M() inside a property Get and Set
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Private v As Integer
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Property P As Integer
                Get
                    MyBase.Bump(1)
                    Return v
                End Get
                Set(value As Integer)
                    MyBase.Bump(100)
                    v = value
                End Set
            End Property
        End Class

        Sub Main()
            Dim d As New Derived()
            d.P = 7
            Console.WriteLine(d.P)
            Console.WriteLine(d.K)
        End Sub
        """, """
        7
        101
        """, Bk.All, HangSafe: true);

    /// <summary>a Sub lambda and a multi-line Function lambda. MSIL has no cell: it refuses `MyBase` inside a lambda by design ("has no IL lowering").</summary>
    internal static readonly TempProbe c3_lambda = new("c3_lambda", """
        ' MyBase.M() inside a Sub lambda and a multi-line Function lambda
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work()
                Dim a As Action = Sub() MyBase.Bump(1)
                a()
                a()
                Dim f As Func(Of Integer) = Function()
                                                MyBase.Bump(10)
                                                Return K
                                            End Function
                Console.WriteLine(f())
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        12
        12
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    /// <summary>a USED base result inside an expression lambda: it stays an expression lambda (`f = () =&gt; base.Tag(n) + 10;`), the half of the rule a statement never reaches. MSIL refuses, as c3_lambda.</summary>
    internal static readonly TempProbe c3b_lamvalue = new("c3b_lamvalue", """
        ' a used MyBase result inside an expression lambda (stays an expression lambda), with a call counter
        Class Base
            Public Calls As Integer
            Public Overridable Function Tag(n As Integer) As Integer
                Calls = Calls + 1
                Return n
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function Tag(n As Integer) As Integer
                Dim f = Function() MyBase.Tag(n) + 10
                Return f() + f()
            End Function
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.Tag(1))
            Console.WriteLine("calls=" & d.Calls)
        End Sub
        """, """
        22
        calls=2
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    /// <summary>an If branch and an Else branch.</summary>
    internal static readonly TempProbe c4_if = new("c4_if", """
        ' MyBase.M() in an If branch and an Else branch
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work(x As Integer)
                If x > 0 Then
                    MyBase.Bump(1)
                Else
                    MyBase.Bump(100)
                End If
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(1)
            d.Work(-1)
        End Sub
        """, """
        1
        101
        """, Bk.All, HangSafe: true);

    /// <summary>a For body and a While body.</summary>
    internal static readonly TempProbe c5_loop = new("c5_loop", """
        ' MyBase.M() in a For body and a While body
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work()
                For i As Integer = 1 To 3
                    MyBase.Bump(i)
                Next
                Console.WriteLine(K)
                Dim j As Integer = 0
                While j < 2
                    MyBase.Bump(100)
                    j = j + 1
                End While
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        6
        206
        """, Bk.All, HangSafe: true);

    /// <summary>a bottom-tested loop (`Do ... Loop While`, `Do ... Loop`) whose body C# writes more than once (#227's peel): the call must still run once per iteration.</summary>
    internal static readonly TempProbe c5b_dowhile = new("c5b_dowhile", """
        ' MyBase.M() in the body of a bottom-tested loop (Do ... Loop While), counter
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work()
                Dim i As Integer = 0
                Do
                    MyBase.Bump(10)
                    i = i + 1
                Loop While i < 3
                Console.WriteLine(K)
                Do Until i >= 5
                    MyBase.Bump(1)
                    i = i + 1
                Loop
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        30
        32
        """, Bk.All, HangSafe: true);

    /// <summary>a For Each body holding an If / ElseIf / Else chain.</summary>
    internal static readonly TempProbe c5c_foreach = new("c5c_foreach", """
        ' MyBase.M() in a For Each body, ElseIf chain
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work()
                Dim a() As Integer = {1, 2, 3}
                For Each v As Integer In a
                    If v = 1 Then
                        MyBase.Bump(1)
                    ElseIf v = 2 Then
                        MyBase.Bump(20)
                    Else
                        MyBase.Bump(300)
                    End If
                Next
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        321
        """, Bk.All, HangSafe: true);

    /// <summary>Select Case arms.</summary>
    internal static readonly TempProbe c6_select = new("c6_select", """
        ' MyBase.M() in Select Case arms
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work(x As Integer)
                Select Case x
                    Case 1
                        MyBase.Bump(1)
                    Case 2
                        MyBase.Bump(20)
                    Case Else
                        MyBase.Bump(300)
                End Select
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(1)
            d.Work(2)
            d.Work(3)
        End Sub
        """, """
        1
        21
        321
        """, Bk.All, HangSafe: true);

    /// <summary>a Try body, a Catch and a Finally.</summary>
    internal static readonly TempProbe c7_try = new("c7_try", """
        ' MyBase.M() in a Try body, a Catch and a Finally
        Class Base
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Bump(n As Integer)
                Console.WriteLine("derived bump")
            End Sub
            Public Sub Work(x As Integer)
                Try
                    MyBase.Bump(1)
                    If x > 0 Then
                        Throw New Exception("boom")
                    End If
                Catch ex As Exception
                    MyBase.Bump(10)
                Finally
                    MyBase.Bump(100)
                End Try
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(0)
            d.Work(1)
        End Sub
        """, """
        101
        212
        """, Bk.All, HangSafe: true);

    /// <summary>a call that reaches a GRANDPARENT's method (the middle class does not override it).</summary>
    internal static readonly TempProbe c8_twolevel = new("c8_twolevel", """
        ' MyBase.M() reaching a GRANDPARENT method (the middle class does not override it)
        Class A
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
                Console.WriteLine("A.Bump " & n)
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
                Console.WriteLine("A.Bump2 " & n)
            End Sub
        End Class

        Class B
            Inherits A
        End Class

        Class C
            Inherits B
            Public Overrides Sub Bump(n As Integer)
                MyBase.Bump(n + 1)
                Console.WriteLine("C.Bump " & n)
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Bump(5)
            Console.WriteLine(c.K)
        End Sub
        """, """
        A.Bump 6
        C.Bump 5
        6
        """, Bk.All, HangSafe: true);

    /// <summary>a three-level override chain: C calls B's override, which calls A's.</summary>
    internal static readonly TempProbe c8b_chain = new("c8b_chain", """
        ' MyBase.M() through a chain: C -> B (overrides, calls MyBase) -> A
        Class A
            Public K As Integer
            Public Overridable Sub Bump(n As Integer)
                K = K + n
                Console.WriteLine("A.Bump " & n)
            End Sub
            Public Sub Bump2(n As Integer)
                K = K + n
                Console.WriteLine("A.Bump2 " & n)
            End Sub
        End Class

        Class B
            Inherits A
            Public Overrides Sub Bump(n As Integer)
                MyBase.Bump(n * 10)
                Console.WriteLine("B.Bump " & n)
            End Sub
        End Class

        Class C
            Inherits B
            Public Overrides Sub Bump(n As Integer)
                MyBase.Bump(n + 1)
                Console.WriteLine("C.Bump " & n)
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Bump(5)
            Console.WriteLine(c.K)
        End Sub
        """, """
        A.Bump 60
        B.Bump 6
        C.Bump 5
        60
        """, Bk.All, HangSafe: true);

    /// <summary>from a GENERIC derived class over a non-generic base (`Inherits Base(Of T)` does not parse on any backend; see the header). MSIL has no cell: `Reference to undefined class 'T'` (generics on MSIL).</summary>
    internal static readonly TempProbe c9b_genderived = new("c9b_genderived", """
        ' MyBase.M(x) from a GENERIC derived class (Inherits a non-generic base; `Inherits Base(Of T)` does not parse)
        Class Base
            Public Count As Integer
            Public Overridable Sub Show(n As Integer)
                Count = Count + n
                Console.WriteLine("base " & n)
            End Sub
            Public Sub Show2(n As Integer)
                Count = Count + n
                Console.WriteLine("show2 " & n)
            End Sub
        End Class

        Class Holder(Of T)
            Inherits Base
            Public Item As T
            Public Overrides Sub Show(n As Integer)
                MyBase.Show(n + 1)
                Console.WriteLine("holder " & n)
            End Sub
        End Class

        Sub Main()
            Dim h As New Holder(Of String)()
            h.Show(5)
            Console.WriteLine(h.Count)
        End Sub
        """, """
        base 6
        holder 5
        6
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    /// <summary>a generic METHOD of the base, called through MyBase with an explicit and an inferred type argument. MSIL has no cell, as c9b.</summary>
    internal static readonly TempProbe c9c_genmethod = new("c9c_genmethod", """
        ' MyBase.M(Of T)(x): a generic METHOD of the base, called through MyBase
        Class Base
            Public Count As Integer
            Public Sub Put(Of T)(x As T)
                Count = Count + 1
                Console.WriteLine(x)
            End Sub
            Public Sub Put2(Of T)(x As T)
                Count = Count + 10
                Console.WriteLine(x)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub Work()
                MyBase.Put(Of Integer)(5)
                MyBase.Put("s")
                Console.WriteLine(Count)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        5
        s
        2
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    /// <summary>user locals spelled like compiler temps (t0..t3) around a base call in a method and in a lambda in it (ADR-0017: a temp is never recognised by its spelling). MSIL refuses a MyBase call in a lambda.</summary>
    internal static readonly TempProbe n1_tempname = new("n1_tempname", """
        ' A base call whose IR temp could be spelled like a user local (t0..t3) in the method and in a lambda inside it
        Class Base
            Public Calls As Integer
            Public Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Sub Work(x As Integer)
                Dim t0 As Integer = 100
                Dim t1 As Integer = 200
                Dim t2 As Integer = 300
                Dim t3 As Integer = 400
                MyBase.F(x)
                Dim g As Func(Of Integer) = Function()
                                                MyBase.F(x)
                                                Return t0 + t1
                                            End Function
                Console.WriteLine(g() + t2 + t3)
                Console.WriteLine("calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(1)
        End Sub
        """, """
        1000
        calls=2
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    // ================================================================================================
    // C. Parameters of the method the base call targets: an Object parameter (#213), an Optional left out, a ParamArray (#265's root: the call now carries them)
    // ================================================================================================

    /// <summary>`MyBase.Show(5)` into an `Object` parameter (#213's shape). C++ has no cell (`'Object' has no C++ mapping`, the parameter itself); MSIL names `Show(object)` since #213 was fixed (MsilObjectBoxingExecutionTests runs it too).</summary>
    internal static readonly TempProbe p2_object = new("p2_object", """
        ' MyBase.M(5) into an Object parameter (#213's shape)
        Class Base
            Public Sub Show(o As Object)
                Console.WriteLine(o)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub Relay()
                MyBase.Show(5)
                Console.WriteLine("done")
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Relay()
        End Sub
        """, """
        5
        done
        """, Bk.CSharp | Bk.JavaScript | Bk.Msil, HangSafe: true);

    /// <summary>an Optional argument left out, and given. Every backend since #265: C++ was a clang `too few arguments`, JavaScript printed `base 1,undefined`, MSIL named the wrong signature.</summary>
    internal static readonly TempProbe p3_optional = new("p3_optional", """
        ' MyBase.M(a) / MyBase.M(a, b) with an Optional parameter
        Class Base
            Public Overridable Sub Show(a As Integer, Optional b As Integer = 7)
                Console.WriteLine("base " & a & "," & b)
            End Sub
            Public Sub Show2(a As Integer, Optional b As Integer = 7)
                Console.WriteLine("show2 " & a & "," & b)
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Show(a As Integer, Optional b As Integer = 7)
                Console.WriteLine("derived " & a & "," & b)
            End Sub
            Public Sub Work()
                MyBase.Show(1)
                MyBase.Show(1, 2)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        base 1,7
        base 1,2
        """, Bk.All, HangSafe: true);

    /// <summary>a ParamArray method called with three loose arguments, packed on every backend since #265: C++ was a clang `too many arguments`, JavaScript a `TypeError` (`xs is not iterable`), MSIL a MissingMethodException.</summary>
    internal static readonly TempProbe p4_paramarray = new("p4_paramarray", """
        ' MyBase.M(a, b, c) into a ParamArray parameter
        Class Base
            Public K As Integer
            Public Overridable Sub Sum(ParamArray xs() As Integer)
                For Each x As Integer In xs
                    K = K + x
                Next
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub Sum(ParamArray xs() As Integer)
                Console.WriteLine("derived")
            End Sub
            Public Sub Work()
                MyBase.Sum(1, 2, 3)
                Console.WriteLine(K)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work()
        End Sub
        """, """
        6
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // D. The value forms that were right before the fix: a USED result is inlined once (a call counter in every row)
    // ================================================================================================

    /// <summary>`Dim r = MyBase.F(x)`.</summary>
    internal static readonly TempProbe v1_dim = new("v1_dim", """
        ' Dim r = MyBase.F(x)
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Dim r = MyBase.F(x)
                Console.WriteLine(r & " calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        8 calls=1
        """, Bk.All, HangSafe: true);

    /// <summary>an assignment to a declared local.</summary>
    internal static readonly TempProbe v2_local = new("v2_local", """
        ' r = MyBase.F(x) into a declared local
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Dim r As Integer = 0
                Console.WriteLine(r)
                r = MyBase.F(x)
                Console.WriteLine(r & " calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        0
        8 calls=1
        """, Bk.All, HangSafe: true);

    /// <summary>an assignment to a field, bare and through `Me.`.</summary>
    internal static readonly TempProbe v3_field = new("v3_field", """
        ' Total = MyBase.F(x) into a field, bare and through Me
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Total As Integer
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Total = MyBase.F(x)
                Console.WriteLine(Total & " calls=" & Calls)
                Me.Total = MyBase.F(x + 1)
                Console.WriteLine(Total & " calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        8 calls=1
        10 calls=2
        """, Bk.All, HangSafe: true);

    /// <summary>an assignment to a parameter.</summary>
    internal static readonly TempProbe v4_param = new("v4_param", """
        ' x = MyBase.F(x) into a parameter
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                x = MyBase.F(x)
                Console.WriteLine(x & " calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        8 calls=1
        """, Bk.All, HangSafe: true);

    /// <summary>two calls in one expression.</summary>
    internal static readonly TempProbe v5_twice = new("v5_twice", """
        ' MyBase.F(x) twice in one expression
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Console.WriteLine(MyBase.F(x) + MyBase.F(x + 1))
                Console.WriteLine("calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        18
        calls=2
        """, Bk.All, HangSafe: true);

    /// <summary>`Select Case MyBase.F(x)`: the selector read by several Case tests, one call.</summary>
    internal static readonly TempProbe v6_select = new("v6_select", """
        ' Select Case MyBase.F(x): the selector read by several Case tests (one call, several uses)
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Select Case MyBase.F(x)
                    Case 2
                        Console.WriteLine("two")
                    Case 4
                        Console.WriteLine("four")
                    Case 8
                        Console.WriteLine("eight")
                    Case Else
                        Console.WriteLine("else")
                End Select
                Console.WriteLine("calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        eight
        calls=1
        """, Bk.All, HangSafe: true);

    /// <summary>the same with range and relational Cases, three calls in all.</summary>
    internal static readonly TempProbe v6b_selrange = new("v6b_selrange", """
        ' Select Case MyBase.F(x) with range and relational Cases: one call whose value is read several times (ADR-0001)
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Select Case MyBase.F(x)
                    Case 1 To 3
                        Console.WriteLine("low")
                    Case 4 To 7
                        Console.WriteLine("mid")
                    Case Is > 7
                        Console.WriteLine("high")
                    Case Else
                        Console.WriteLine("else")
                End Select
                Console.WriteLine("calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(1)
            d.Work(3)
            d.Work(9)
        End Sub
        """, """
        low
        calls=1
        mid
        calls=2
        high
        calls=3
        """, Bk.All, HangSafe: true);

    /// <summary>a call inside an expression, and one as the argument of another.</summary>
    internal static readonly TempProbe v7_inline = new("v7_inline", """
        ' MyBase.F(x) used once inside an expression, and as an argument
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Sub Work(x As Integer)
                Console.WriteLine(MyBase.F(x) * 3)
                Console.WriteLine(MyBase.F(MyBase.F(x)))
                Console.WriteLine("calls=" & Calls)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(4)
        End Sub
        """, """
        24
        16
        calls=3
        """, Bk.All, HangSafe: true);

    /// <summary>an If condition operand, and a Return value.</summary>
    internal static readonly TempProbe v8_ifcond = new("v8_ifcond", """
        ' MyBase.F(x) as an If condition operand and a Return value
        Class Base
            Public Calls As Integer
            Public Overridable Function F(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
            Public Function G(n As Integer) As Integer
                Calls = Calls + 1
                Return n * 2
            End Function
        End Class

        Class Derived
            Inherits Base
            Public Overrides Function F(n As Integer) As Integer
                Return -1
            End Function
            Public Function Work(x As Integer) As Integer
                If MyBase.F(x) > 5 Then
                    Console.WriteLine("big calls=" & Calls)
                End If
                Return MyBase.F(x + 1)
            End Function
        End Class

        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.Work(4))
            Console.WriteLine("calls=" & d.Calls)
        End Sub
        """, """
        big calls=1
        10
        calls=2
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // E. A ByRef argument to a base method (#265, #142). NOT in `All`: JavaScript refuses ByRef (BL7002), so these rows have their own list and their own tests.
    // ================================================================================================

    /// <summary>a ByRef argument to a base method that Derived overrides. C#, C++ and MSIL print vbc's answer (C# was CS1620 and MSIL a MissingMethodException before #265/#142); JavaScript refuses ByRef (BL7002, by design).</summary>
    internal static readonly TempProbe p1_byref = new("p1_byref", """
        ' MyBase.M(x) with a ByRef parameter, M overridden in Derived
        Class Base
            Public Overridable Sub SetIt(ByRef n As Integer)
                n = n + 100
            End Sub
            Public Sub SetIt2(ByRef n As Integer)
                n = n + 100
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Overrides Sub SetIt(ByRef n As Integer)
                n = -1
            End Sub
            Public Sub Work(p As Integer)
                MyBase.SetIt(p)
                Console.WriteLine(p)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(5)
        End Sub
        """, """
        105
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>a ByRef argument to an inherited base method (KillVocabularyExtensions B2's shape). Same cells as p1_byref.</summary>
    internal static readonly TempProbe p1b_byrefinh = new("p1b_byrefinh", """
        ' MyBase.M(x) with a ByRef parameter, M only inherited (KillVocabularyExtensions B2's shape, #142 on MSIL)
        Class Base
            Public Sub SetIt(ByRef n As Integer)
                n = n + 100
            End Sub
        End Class

        Class Derived
            Inherits Base
            Public Sub Work(p As Integer)
                Dim q As Integer = p
                MyBase.SetIt(q)
                Console.WriteLine(q)
            End Sub
        End Class

        Sub Main()
            Dim d As New Derived()
            d.Work(5)
        End Sub
        """, """
        105
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>The rows with a C# expectation, in table order.</summary>
    internal static readonly IReadOnlyList<TempProbe> All = new[]
    {
        s1_sub, s2_func, s2b_funcself, s2c_types, s2d_tostring, s3_noargs,
        c1_ctor, c2_prop, c3_lambda, c3b_lamvalue, c4_if, c5_loop,
        c5b_dowhile, c5c_foreach, c6_select, c7_try, c8_twolevel, c8b_chain,
        c9b_genderived, c9c_genmethod, n1_tempname, p2_object, p3_optional, p4_paramarray,
        v1_dim, v2_local, v3_field, v4_param, v5_twice, v6_select,
        v6b_selrange, v7_inline, v8_ifcond
    };

    /// <summary>The ByRef rows: C#, C++ and MSIL print vbc's answer; JavaScript refuses (BL7002).</summary>
    internal static readonly IReadOnlyList<TempProbe> ByRef = new[] { p1_byref, p1b_byrefinh };
}
