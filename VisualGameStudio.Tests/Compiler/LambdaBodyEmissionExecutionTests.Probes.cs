using System.Collections.Generic;

namespace VisualGameStudio.Tests.Compiler;

// Rows taken verbatim from the implementer's probes (S/t136/probes/m, m2, m3, m4) or written for #136 (S/t136/tw/probes*): every source as it was run,
// every expected value vbc's own output (the program wrapped in a VB Module and compiled with the SDK's vbc, then run). A row's Agrees flags are the
// backends whose three entry points print that text on this build (S/t136/mat-after-*.txt). Edit this file by hand like any other.

/// <summary>The probe table of <see cref="LambdaBodyEmissionExecutionTests"/>. ⛔ Every row is HangSafe: a lambda body can hold a loop.</summary>
internal static class LambdaBodyProbes
{
    // ================================================================================================
    // A. A Sub lambda WRITES something it captured (or its enclosing member owns)
    // ================================================================================================

    /// <summary>a captured local, then a print of it inside the lambda. Right before the fix (a single-block body of one named write and a call).</summary>
    internal static readonly TempProbe a_loc = new("a_loc", """
        Sub Main()
            Dim n As Integer = 1
            Dim f = Sub()
                        n = n + 100
                        Console.WriteLine("in " & n)
                    End Sub
            f()
            Console.WriteLine(n)
        End Sub
        """, """
        in 101
        101
        """, Bk.All, HangSafe: true);

    /// <summary>the same write as a single-statement `Sub() n = n + 100`: a one-line lambda keeps working.</summary>
    internal static readonly TempProbe a_loc_c = new("a_loc_c", """
        Sub Main()
            Dim n As Integer = 1
            Dim f = Sub() n = n + 100
            f()
            Console.WriteLine(n)
        End Sub
        """, """
        101
        """, Bk.All, HangSafe: true);

    /// <summary>a parameter of the enclosing Sub.</summary>
    internal static readonly TempProbe a_par = new("a_par", """
        Sub Work(p As Integer)
            Dim f = Sub()
                        p = p + 10
                        Console.WriteLine("in " & p)
                    End Sub
            f()
            Console.WriteLine(p)
        End Sub

        Sub Main()
            Work(5)
        End Sub
        """, """
        in 15
        15
        """, Bk.All, HangSafe: true);

    /// <summary>a field of the enclosing class, bare.</summary>
    internal static readonly TempProbe a_fld = new("a_fld", """
        Class C
            Public x As Integer = 1
            Public Sub Run()
                Dim f = Sub()
                            x = x + 5
                            Console.WriteLine("in " & x)
                        End Sub
                f()
                Console.WriteLine(x)
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Run()
        End Sub
        """, """
        in 6
        6
        """, Bk.All, HangSafe: true);

    /// <summary>the same, single statement.</summary>
    internal static readonly TempProbe a_fld_c = new("a_fld_c", """
        Class C
            Public x As Integer = 1
            Public Sub Run()
                Dim f = Sub() x = x + 5
                f()
                Console.WriteLine(x)
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Run()
        End Sub
        """, """
        6
        """, Bk.All, HangSafe: true);

    /// <summary>a field through `Me.`: printed 1 for 6 before the fix (the store through Me was skipped as a temp).</summary>
    internal static readonly TempProbe a_mefld = new("a_mefld", """
        Class C
            Public x As Integer = 1
            Public Sub Run()
                Dim f = Sub()
                            Me.x = Me.x + 5
                            Console.WriteLine("in " & Me.x)
                        End Sub
                f()
                Console.WriteLine(x)
            End Sub
        End Class

        Sub Main()
            Dim c As New C()
            c.Run()
        End Sub
        """, """
        in 6
        6
        """, Bk.All, HangSafe: true);

    /// <summary>a module global.</summary>
    internal static readonly TempProbe a_glob = new("a_glob", """
        Dim g As Integer = 1

        Sub Main()
            Dim f = Sub()
                        g = g + 7
                        Console.WriteLine("in " & g)
                    End Sub
            f()
            Console.WriteLine(g)
        End Sub
        """, """
        in 8
        8
        """, Bk.All, HangSafe: true);

    /// <summary>a Shared field of the class and the lambda's own parameter, written in turn: 10 for 15 before the fix.</summary>
    internal static readonly TempProbe k_shared = new("k_shared", """
        Class U
            Public Shared Total As Integer
            Public Shared Function Run(n As Integer) As Integer
                Dim f = Sub(v As Integer)
                            Total = Total + v
                            v = v * 2
                            Total = Total + v
                        End Sub
                f(n)
                Return Total
            End Function
        End Class

        Sub Main()
            Console.WriteLine(U.Run(5))
        End Sub
        """, """
        15
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // B. A lambda WRITES ITS OWN PARAMETER
    // ================================================================================================

    /// <summary>NameBindingExecutionTests K4: `Sub(X) x = x + 1` printed the untouched parameter (1 for 2) because the write is a value renamed after the parameter, which the old loop skipped as a temp. Written as `X`/`x` on purpose.</summary>
    internal static readonly TempProbe k4_ownparam = new("k4_ownparam", """
        Sub Main()
            Dim h = Sub(X As Integer)
                        x = x + 1
                        Console.WriteLine(x)
                    End Sub
            h(1)
        End Sub
        """, """
        2
        """, Bk.All, HangSafe: true);

    /// <summary>a one-line and a multi-line Sub lambda that each write their parameter: 3 for 15 before.</summary>
    internal static readonly TempProbe b_subpar1 = new("b_subpar1", """
        Sub Main()
            Dim h = Sub(x As Integer) x = x + 1
            h(1)
            Dim k = Sub(y As Integer)
                        y = y * 5
                        Console.WriteLine(y)
                    End Sub
            k(3)
        End Sub
        """, """
        15
        """, Bk.All, HangSafe: true);

    /// <summary>a Function lambda that writes its parameter before its Return: it became the expression lambda `x => x + 1` and printed 3 for 7.</summary>
    internal static readonly TempProbe b_fnpar = new("b_fnpar", """
        Sub Main()
            Dim f = Function(x As Integer) As Integer
                        x = x * 3
                        Return x + 1
                    End Function
            Console.WriteLine(f(2))
        End Sub
        """, """
        7
        """, Bk.All, HangSafe: true);

    /// <summary>the pure single-expression twin: stays `x => x * 3 + 1` (the fast tests pin the bytes).</summary>
    internal static readonly TempProbe b_fnpar_c = new("b_fnpar_c", """
        Sub Main()
            Dim f = Function(x As Integer) x * 3 + 1
            Console.WriteLine(f(2))
        End Sub
        """, """
        7
        """, Bk.All, HangSafe: true);

    /// <summary>a loop that writes the lambda's parameter: CS1643 before (every block after the entry block was dropped).</summary>
    internal static readonly TempProbe b_parloop = new("b_parloop", """
        Sub Main()
            Dim f = Function(n As Integer) As Integer
                        Dim steps As Integer = 0
                        While n > 1
                            n = n \ 2
                            steps = steps + 1
                        End While
                        Return steps
                    End Function
            Console.WriteLine(f(40))
        End Sub
        """, """
        5
        """, Bk.All, HangSafe: true);

    /// <summary>a Sub lambda inside a Function lambda writes the OUTER lambda's parameter; CS0103 'g' before.</summary>
    internal static readonly TempProbe d_nest_par = new("d_nest_par", """
        Sub Main()
            Dim f = Function(a As Integer) As Integer
                        Dim g = Sub()
                                    a = a + 5
                                End Sub
                        g()
                        g()
                        Return a
                    End Function
            Console.WriteLine(f(1))
        End Sub
        """, """
        11
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // C. A multi-line lambda with statements BEFORE its last: an assignment, a call, an If, a loop, a Dim, Select Case, Try, Exit
    // ================================================================================================

    /// <summary>an assignment before the Return (the F1/F7 shape): `n = n + 1 : Return 5` became `() => 5` and printed 5|0.</summary>
    internal static readonly TempProbe c_asg = new("c_asg", """
        Sub Main()
            Dim n As Integer = 0
            Dim f = Function() As Integer
                        n = n + 1
                        Return 5
                    End Function
            Console.WriteLine(f())
            Console.WriteLine(n)
        End Sub
        """, """
        5
        1
        """, Bk.All, HangSafe: true);

    /// <summary>a Sub call before the Return. Right before the fix; the call must stay once.</summary>
    internal static readonly TempProbe c_call = new("c_call", """
        Sub Note(s As String)
            Console.WriteLine("note " & s)
        End Sub

        Sub Main()
            Dim f = Function() As Integer
                        Note("a")
                        Return 5
                    End Function
            Console.WriteLine(f())
        End Sub
        """, """
        note a
        5
        """, Bk.All, HangSafe: true);

    /// <summary>an If/Else writing a local: CS1643 before.</summary>
    internal static readonly TempProbe c_if = new("c_if", """
        Sub Main()
            Dim f = Function(v As Integer) As Integer
                        Dim r As Integer = 0
                        If v > 2 Then
                            r = v * 10
                        Else
                            r = -v
                        End If
                        Return r
                    End Function
            Console.WriteLine(f(5))
            Console.WriteLine(f(1))
        End Sub
        """, """
        50
        -1
        """, Bk.All, HangSafe: true);

    /// <summary>a For loop accumulating into a lambda local: CS1643 before.</summary>
    internal static readonly TempProbe c_loop = new("c_loop", """
        Sub Main()
            Dim sum = Function(n As Integer) As Integer
                          Dim t As Integer = 0
                          For i As Integer = 1 To n
                              t = t + i
                          Next
                          Return t
                      End Function
            Console.WriteLine(sum(4))
        End Sub
        """, """
        10
        """, Bk.All, HangSafe: true);

    /// <summary>a While loop in a Sub lambda: the lambda did nothing before (0 for 6).</summary>
    internal static readonly TempProbe c_while = new("c_while", """
        Sub Main()
            Dim k As Integer = 0
            Dim f = Sub()
                        While k < 5
                            k = k + 2
                        End While
                    End Sub
            f()
            Console.WriteLine(k)
        End Sub
        """, """
        6
        """, Bk.All, HangSafe: true);

    /// <summary>Select Case returning from each arm: CS1643 before.</summary>
    internal static readonly TempProbe c_sel = new("c_sel", """
        Sub Main()
            Dim name = Function(v As Integer) As String
                           Select Case v
                               Case 1
                                   Return "one"
                               Case 2, 3
                                   Return "few"
                               Case Else
                                   Return "many"
                           End Select
                       End Function
            Console.WriteLine(name(1) & "," & name(3) & "," & name(9))
        End Sub
        """, """
        one,few,many
        """, Bk.All, HangSafe: true);

    /// <summary>Try/Catch returning from both: CS1643 before.</summary>
    internal static readonly TempProbe c_try = new("c_try", """
        Sub Main()
            Dim safeDiv = Function(a As Integer, b As Integer) As Integer
                              Try
                                  Return a \ b
                              Catch ex As Exception
                                  Return -1
                              End Try
                          End Function
            Console.WriteLine(safeDiv(10, 2))
            Console.WriteLine(safeDiv(1, 0))
        End Sub
        """, """
        5
        -1
        """, Bk.All, HangSafe: true);

    /// <summary>Try/Finally in a Sub lambda: printed an empty line before.</summary>
    internal static readonly TempProbe c_tryfin = new("c_tryfin", """
        Sub Main()
            Dim log As String = ""
            Dim f = Sub()
                        Try
                            log = log & "a"
                        Finally
                            log = log & "b"
                        End Try
                    End Sub
            f()
            Console.WriteLine(log)
        End Sub
        """, """
        ab
        """, Bk.All, HangSafe: true);

    /// <summary>a Catch of a typed exception around a Dim in a Sub lambda: empty before.</summary>
    internal static readonly TempProbe k_catch = new("k_catch", """
        Sub Main()
            Dim msg As String = ""
            Dim f = Sub(d As Integer)
                        Try
                            Dim q As Integer = 10 \ d
                            msg = msg & "q=" & q & ";"
                        Catch ex As DivideByZeroException
                            msg = msg & "div0;"
                        End Try
                    End Sub
            f(2)
            f(0)
            Console.WriteLine(msg)
        End Sub
        """, """
        q=5;div0;
        """, Bk.All, HangSafe: true);

    /// <summary>a Do While loop inside a Sub lambda that is itself created in a Do ... Loop While body (ADR-0014's per-iteration plan must not leak into the lambda).</summary>
    internal static readonly TempProbe k_dowhile = new("k_dowhile", """
        Sub Main()
            Dim i As Integer = 0
            Dim log As String = ""
            Do
                Dim f = Sub()
                            Dim j As Integer = 0
                            Do While j < i
                                log = log & j
                                j = j + 1
                            Loop
                            log = log & ";"
                        End Sub
                f()
                i = i + 1
            Loop While i < 3
            Console.WriteLine(log)
        End Sub
        """, """
        ;0;01;
        """, Bk.All, HangSafe: true);

    /// <summary>a Function lambda with an If/Return, created in an If branch inside a For: CS1643 before.</summary>
    internal static readonly TempProbe k_ifouter = new("k_ifouter", """
        Sub Main()
            Dim n As Integer = 0
            For i As Integer = 1 To 4
                If i Mod 2 = 0 Then
                    Dim f = Function(v As Integer) As Integer
                                If v > 2 Then
                                    Return v * 100
                                End If
                                Return v
                            End Function
                    n = n + f(i)
                Else
                    n = n + 1
                End If
            Next
            Console.WriteLine(n)
        End Sub
        """, """
        404
        """, Bk.All, HangSafe: true);

    /// <summary>a Return from inside a For in a lambda that sits in a For with an Exit For: the lambda's `break`s must not touch the enclosing loop (CS1643 before).</summary>
    internal static readonly TempProbe j_exitloop = new("j_exitloop", """
        Sub Main()
            Dim found As Integer = -1
            For k As Integer = 1 To 3
                Dim find = Function(limit As Integer) As Integer
                               For i As Integer = 1 To 10
                                   If i * i > limit Then
                                       Return i
                                   End If
                               Next
                               Return 0
                           End Function
                found = find(k * 10)
                If found > 4 Then
                    Exit For
                End If
            Next
            Console.WriteLine(found)
        End Sub
        """, """
        5
        """, Bk.All, HangSafe: true);

    /// <summary>Exit Sub in a Sub lambda: 0 for 7 before.</summary>
    internal static readonly TempProbe j_exitsub = new("j_exitsub", """
        Sub Main()
            Dim hits As Integer = 0
            Dim f = Sub(v As Integer)
                        If v < 0 Then
                            Exit Sub
                        End If
                        hits = hits + v
                    End Sub
            f(3)
            f(-1)
            f(4)
            Console.WriteLine(hits)
        End Sub
        """, """
        7
        """, Bk.All, HangSafe: true);

    /// <summary>a sized array Dim in a lambda is allocated at its declaration (`sizedArrays: true`): CS0103 'a' before. MSIL does not run it today (an unrelated InvalidProgramException).</summary>
    internal static readonly TempProbe j_lambdaarr = new("j_lambdaarr", """
        Sub Main()
            Dim f = Function(k As Integer) As Integer
                        Dim a(3) As Integer
                        For i As Integer = 0 To 3
                            a(i) = i * k
                        Next
                        Return a(3)
                    End Function
            Console.WriteLine(f(5))
        End Sub
        """, """
        15
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript, HangSafe: true);

    /// <summary>a For Each inside a lambda created in a For Each body: the open rename of the outer loop variable stays visible. (j_foreach is the same program with a variable named `out`: CS1001, #182, no row.)</summary>
    internal static readonly TempProbe j_foreach2 = new("j_foreach2", """
        Sub Main()
            Dim xs As New List(Of Integer)
            xs.Add(1)
            xs.Add(2)
            xs.Add(3)
            Dim acc As String = ""
            For Each x In xs
                Dim f = Sub()
                            For Each y In xs
                                If y <= x Then
                                    acc = acc & y
                                End If
                            Next
                            acc = acc & "|"
                        End Sub
                f()
            Next
            Console.WriteLine(acc)
        End Sub
        """, """
        1|12|123|
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda created in a Select Case arm inside a For that the Select leaves with Exit For: 0 for 6 before.</summary>
    internal static readonly TempProbe j_select = new("j_select", """
        Sub Main()
            Dim acc As Integer = 0
            For i As Integer = 1 To 5
                Select Case i
                    Case 2
                        Dim f = Sub()
                                    For j As Integer = 1 To 3
                                        acc = acc + j
                                    Next
                                End Sub
                        f()
                    Case 4
                        Exit For
                End Select
            Next
            Console.WriteLine(acc)
        End Sub
        """, """
        6
        """, Bk.All, HangSafe: true);

    /// <summary>what comes AFTER a lambda in the enclosing loop and function (an If around calls of it, a call result used after the loop) must be written as it was. Right before the fix.</summary>
    internal static readonly TempProbe j_afterlambda = new("j_afterlambda", """
        Function Side() As Integer
            Console.WriteLine("side")
            Return 3
        End Function

        Sub Main()
            Dim total As Integer = 0
            For i As Integer = 1 To 3
                Dim f = Sub(v As Integer)
                            total = total + v
                            Console.WriteLine("v" & v)
                        End Sub
                If i Mod 2 = 1 Then
                    f(i)
                Else
                    f(i * 10)
                End If
            Next
            Dim s As Integer = Side()
            Console.WriteLine(total + s)
        End Sub
        """, """
        v1
        v20
        v3
        side
        27
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // D. Nested lambdas
    // ================================================================================================

    /// <summary>a Sub lambda declared inside a Sub lambda, called twice: CS0103 'inner' before.</summary>
    internal static readonly TempProbe d_nest = new("d_nest", """
        Sub Main()
            Dim total As Integer = 0
            Dim outer = Sub(k As Integer)
                            Dim inner = Sub(j As Integer)
                                            total = total + j
                                        End Sub
                            inner(k)
                            inner(k * 10)
                        End Sub
            outer(2)
            Console.WriteLine(total)
        End Sub
        """, """
        22
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda that returns a lambda, both single expressions: stays right.</summary>
    internal static readonly TempProbe d_nest_c = new("d_nest_c", """
        Sub Main()
            Dim mk = Function(a As Integer) Function(b As Integer) a * 10 + b
            Dim g = mk(4)
            Console.WriteLine(g(2))
        End Sub
        """, """
        42
        """, Bk.All, HangSafe: true);

    /// <summary>NameBindingExecutionTests K5 (#165): a multi-line Function lambda declares its own nested lambda.</summary>
    internal static readonly TempProbe k5_nested_dim = new("k5_nested_dim", """
        Sub Main()
            Dim outer = Function(A As Integer) As Integer
                            Dim inner = Function(b As Integer) a + b
                            Return inner(2)
                        End Function
            Console.WriteLine(outer(1))
        End Sub
        """, """
        3
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // E. Where a lambda is written: a method, a constructor, MyBase.New(...) arguments, a property accessor, a module global's initializer
    // ================================================================================================

    /// <summary>a Sub lambda in a class method calling a method on a field and writing a local: 700 for 702.</summary>
    internal static readonly TempProbe e_meth2 = new("e_meth2", """
        Class Acc
            Private items As List(Of Integer)
            Public Sub New()
                items = New List(Of Integer)()
            End Sub
            Public Function Run() As Integer
                Dim s As Integer = 0
                Dim add = Sub(v As Integer)
                              items.Add(v)
                              s = s + v
                          End Sub
                add(3)
                add(4)
                Return s * 100 + items.Count
            End Function
        End Class

        Sub Main()
            Dim a As New Acc()
            Console.WriteLine(a.Run())
        End Sub
        """, """
        702
        """, Bk.All, HangSafe: true);

    /// <summary>`Hit(v)` and `Me.Hit(v * 10)` in a Sub lambda of a class method: the calls were skipped as temps (3 for 33).</summary>
    internal static readonly TempProbe j_mecall = new("j_mecall", """
        Class P
            Public Count As Integer
            Public Sub Hit(v As Integer)
                Count = Count + v
            End Sub
            Public Function Run() As Integer
                Dim f = Sub(v As Integer)
                            Hit(v)
                            Me.Hit(v * 10)
                        End Sub
                f(1)
                f(2)
                Return Count
            End Function
        End Class

        Sub Main()
            Dim p As New P()
            Console.WriteLine(p.Run())
        End Sub
        """, """
        33
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda in a constructor writing a local and a field. Right before the fix.</summary>
    internal static readonly TempProbe e_ctor = new("e_ctor", """
        Class W
            Public Total As Integer
            Public Sub New(n As Integer)
                Dim acc As Integer = 0
                Dim f = Sub(v As Integer)
                            acc = acc + v
                            Total = Total + v * 2
                        End Sub
                f(n)
                f(1)
                Console.WriteLine(acc)
            End Sub
        End Class

        Sub Main()
            Dim w As New W(5)
            Console.WriteLine(w.Total)
        End Sub
        """, """
        6
        12
        """, Bk.All, HangSafe: true);

    /// <summary>a block lambda with its own Dim in the arguments of `MyBase.New(...)`: CS0103 't' before.</summary>
    internal static readonly TempProbe e_mybase = new("e_mybase", """
        Class B
            Public V As Integer
            Public Sub New(f As Func(Of Integer, Integer))
                V = f(4)
            End Sub
        End Class

        Class D
            Inherits B
            Public Sub New(k As Integer)
                MyBase.New(Function(x As Integer) As Integer
                               Dim t As Integer = x * k
                               Return t + 1
                           End Function)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(3)
            Console.WriteLine(d.V)
        End Sub
        """, """
        13
        """, Bk.All, HangSafe: true);

    /// <summary>the single-expression twin in the same position: stays right.</summary>
    internal static readonly TempProbe e_mybase_c = new("e_mybase_c", """
        Class B
            Public V As Integer
            Public Sub New(f As Func(Of Integer, Integer))
                V = f(4)
            End Sub
        End Class

        Class D
            Inherits B
            Public Sub New(k As Integer)
                MyBase.New(Function(x As Integer) x * k + 1)
            End Sub
        End Class

        Sub Main()
            Dim d As New D(3)
            Console.WriteLine(d.V)
        End Sub
        """, """
        13
        """, Bk.All, HangSafe: true);

    /// <summary>a Sub lambda inside a property Get. Right before the fix.</summary>
    internal static readonly TempProbe k_prop = new("k_prop", """
        Class C
            Private _v As Integer = 4
            Public ReadOnly Property Doubled As Integer
                Get
                    Dim acc As Integer = 0
                    Dim f = Sub(k As Integer)
                                acc = acc + k * 2
                            End Sub
                    f(_v)
                    Return acc
                End Get
            End Property
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Doubled)
        End Sub
        """, """
        8
        """, Bk.All, HangSafe: true);

    /// <summary>lambdas in module-global INITIALISERS, one writing a global and one with its own Dim (JavaScript, C++ and MSIL do not build a non-constant module initialiser today).</summary>
    internal static readonly TempProbe k_global = new("k_global", """
        Dim counter As Integer = 0
        Dim bump As Action = Sub()
                                 counter = counter + 3
                                 Console.WriteLine("bump " & counter)
                             End Sub
        Dim calc As Func(Of Integer, Integer) = Function(v As Integer) As Integer
                                                    Dim t As Integer = v + counter
                                                    Return t * 2
                                                End Function

        Sub Main()
            bump()
            bump()
            Console.WriteLine(calc(1))
        End Sub
        """, """
        bump 3
        bump 6
        14
        """, Bk.CSharp, HangSafe: true);

    /// <summary>k_global's first lambda alone: `bump 0|bump 0|0` before, because a lambda outside any function has no enclosing names, and its write to a global was a temp.</summary>
    internal static readonly TempProbe k_global2 = new("k_global2", """
        Dim counter As Integer = 0
        Dim bump As Action = Sub()
                                 counter = counter + 3
                                 Console.WriteLine("bump " & counter)
                             End Sub

        Sub Main()
            bump()
            bump()
            Console.WriteLine(counter)
        End Sub
        """, """
        bump 3
        bump 6
        6
        """, Bk.CSharp, HangSafe: true);

    // ================================================================================================
    // F. A lambda passed to a Sub, stored in a List(Of Func(Of Integer)), returned, applied twice
    // ================================================================================================

    /// <summary>passed as an argument. Right before the fix.</summary>
    internal static readonly TempProbe f_pass = new("f_pass", """
        Sub Twice(a As Action)
            a()
            a()
        End Sub

        Sub Main()
            Dim n As Integer = 0
            Twice(Sub()
                      n = n + 1
                      Console.WriteLine("tick " & n)
                  End Sub)
            Console.WriteLine(n)
        End Sub
        """, """
        tick 1
        tick 2
        2
        """, Bk.All, HangSafe: true);

    /// <summary>built in a loop, stored in a List(Of Func(Of Integer)), called later: 0|0|0|0 before.</summary>
    internal static readonly TempProbe f_list = new("f_list", """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))
            Dim c As Integer = 0
            For i As Integer = 1 To 3
                fs.Add(Function() As Integer
                           c = c + 1
                           Return c * 10
                       End Function)
            Next
            For k As Integer = 0 To fs.Count - 1
                Console.WriteLine(fs(k)())
            Next
            Console.WriteLine(c)
        End Sub
        """, """
        10
        20
        30
        3
        """, Bk.All, HangSafe: true);

    /// <summary>returned from a Function. Right before the fix.</summary>
    internal static readonly TempProbe f_ret = new("f_ret", """
        Function MakeAdder(start As Integer) As Action(Of Integer)
            Dim total As Integer = start
            Return Sub(v As Integer)
                       total = total + v
                       Console.WriteLine(total)
                   End Sub
        End Function

        Sub Main()
            Dim a = MakeAdder(10)
            a(1)
            a(2)
        End Sub
        """, """
        11
        13
        """, Bk.All, HangSafe: true);

    /// <summary>two block lambdas in one expression, each with its own `y`, the first writing a captured local: CS0103 'y' before.</summary>
    internal static readonly TempProbe k_twice = new("k_twice", """
        Function Apply(f As Func(Of Integer, Integer), v As Integer) As Integer
            Return f(v)
        End Function

        Sub Main()
            Dim base As Integer = 10
            Dim r As Integer = Apply(Function(x As Integer) As Integer
                                         Dim y As Integer = x + base
                                         base = base + 1
                                         Return y
                                     End Function, 1) + Apply(Function(x As Integer) As Integer
                                                                  Dim y As Integer = x * base
                                                                  Return y
                                                              End Function, 2)
            Console.WriteLine(r)
            Console.WriteLine(base)
        End Sub
        """, """
        33
        11
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // G. A lambda's own Dim (#165) and the names around it
    // ================================================================================================

    /// <summary>two lambdas declaring the same `t`: CS0103 before.</summary>
    internal static readonly TempProbe j_names = new("j_names", """
        Sub Main()
            Dim f = Function(a As Integer) As Integer
                        Dim t As Integer = a + 1
                        Return t * 2
                    End Function
            Dim g = Function(a As Integer) As Integer
                        Dim t As Integer = a - 1
                        Return t * 3
                    End Function
            Console.WriteLine(f(1) + g(5))
        End Sub
        """, """
        16
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda's own `Dim T` (upper case) and a parameter `N2` beside a captured `n` the lambda writes: CS0103 'T' before.</summary>
    internal static readonly TempProbe j_case = new("j_case", """
        Sub Main()
            Dim n As Integer = 3
            Dim f = Function(N2 As Integer) As Integer
                        Dim T As Integer = N2 + n
                        n = T * 2
                        Return T
                    End Function
            Console.WriteLine(f(1))
            Console.WriteLine(n)
        End Sub
        """, """
        4
        8
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda's own `Dim t` beside a FIELD `t`: the lambda read and wrote the field (50050 for 7050).</summary>
    internal static readonly TempProbe j_shadowfld = new("j_shadowfld", """
        Class C
            Public t As Integer = 50
            Public Function Run() As Integer
                Dim f = Function(a As Integer) As Integer
                            Dim t As Integer = a * 2
                            t = t + 1
                            Return t
                        End Function
                Return f(3) * 1000 + t
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Run())
        End Sub
        """, """
        7050
        """, Bk.All, HangSafe: true);

    /// <summary>LambdaBoundaryDiagnosticsExecutionTests N8: the lambda's own local hides a class field (10 for 8 before).</summary>
    internal static readonly TempProbe n8_hide_field = new("n8_hide_field", """
        Class C
            Public x As Integer = 5
            Public Function Run() As Integer
                Dim f As Func(Of Integer) = Function()
                        Dim x As Integer = 3
                        Return x
                    End Function
                Return f() + x
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Run())
        End Sub
        """, """
        8
        """, Bk.All, HangSafe: true);

    /// <summary>N9: the same against a module global.</summary>
    internal static readonly TempProbe n9_hide_global = new("n9_hide_global", """
        Dim x As Integer = 5
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 3
                    Return x
                End Function
            Console.WriteLine(f() + x)
        End Sub
        """, """
        8
        """, Bk.All, HangSafe: true);

    /// <summary>a one-block Function lambda whose own Dim has NO initialiser: the block form must declare it (CS0103 't' before). Mutant M8.</summary>
    internal static readonly TempProbe n_uninit = new("n_uninit", """
        Sub Main()
            Dim f = Function(a As Integer) As Integer
                        Dim t As Integer
                        Return t + a
                    End Function
            Console.WriteLine(f(4))
        End Sub
        """, """
        4
        """, Bk.All, HangSafe: true);

    /// <summary>the same for a String.</summary>
    internal static readonly TempProbe n_uninit2 = new("n_uninit2", """
        Sub Main()
            Dim f = Function() As String
                        Dim s As String
                        Return s & "x"
                    End Function
            Console.WriteLine(f())
        End Sub
        """, """
        x
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // H. A call whose result is used runs exactly once (#179)
    // ================================================================================================

    /// <summary>`Return Twice(Inc()) + a`: the call was written as a statement AND inside the expression, so `Inc` ran twice (7|3 for 3|1).</summary>
    internal static readonly TempProbe c_call2 = new("c_call2", """
        Dim cnt As Integer = 0

        Function Inc() As Integer
            cnt = cnt + 1
            Return cnt
        End Function

        Function Twice(v As Integer) As Integer
            Return v * 2
        End Function

        Sub Main()
            Dim f = Function() As Integer
                        Dim a As Integer = 1
                        Return Twice(Inc()) + a
                    End Function
            Console.WriteLine(f())
            Console.WriteLine(cnt)
        End Sub
        """, """
        3
        1
        """, Bk.All, HangSafe: true);

    /// <summary>the same nested call in an expression lambda: 6|3 for 2|1.</summary>
    internal static readonly TempProbe c_callexpr = new("c_callexpr", """
        Dim cnt As Integer = 0

        Function Inc() As Integer
            cnt = cnt + 1
            Return cnt
        End Function

        Function Twice(v As Integer) As Integer
            Return v * 2
        End Function

        Sub Main()
            Dim f = Function() Twice(Inc())
            Console.WriteLine(f())
            Console.WriteLine(cnt)
        End Sub
        """, """
        2
        1
        """, Bk.All, HangSafe: true);

    /// <summary>`Function() Foo(Bar())` prints `bar` once, not three times.</summary>
    internal static readonly TempProbe i_expr = new("i_expr", """
        Function Bar() As Integer
            Console.WriteLine("bar")
            Return 2
        End Function

        Function Foo(v As Integer) As Integer
            Return v + 1
        End Function

        Sub Main()
            Dim a = Function(x As Integer) x * 2
            Dim b = Function() Foo(Bar())
            Dim c = Sub() Console.WriteLine("c")
            Dim d = Function(s As String) s.Length + 1
            Console.WriteLine(a(4))
            Console.WriteLine(b())
            c()
            Console.WriteLine(d("abc"))
        End Sub
        """, """
        8
        bar
        3
        c
        4
        """, Bk.All, HangSafe: true);

    /// <summary>a COUNTER: `Tick()` as a statement, then twice inside one expression, in a multi-line Function lambda. vbc: 23, and `calls` is 3. Before: 45 and 5 (every used call was written as a statement as well, #179).</summary>
    internal static readonly TempProbe g_once = new("g_once", """
        Dim calls As Integer = 0

        Function Tick() As Integer
            calls = calls + 1
            Return calls
        End Function

        Sub Main()
            Dim f = Function() As Integer
                        Tick()
                        Return Tick() * 10 + Tick()
                    End Function
            Console.WriteLine(f())
            Console.WriteLine(calls)
        End Sub
        """, """
        23
        3
        """, Bk.All, HangSafe: true);

    /// <summary>the counter in a Sub lambda: a call used as an argument, a call used as a statement and two used in one argument. vbc: `show 1`, `show 7`, 4. Before: `show 2`, `show 13`, 7.</summary>
    internal static readonly TempProbe g_once_sub = new("g_once_sub", """
        Dim calls As Integer = 0

        Function Tick() As Integer
            calls = calls + 1
            Return calls
        End Function

        Sub Show(v As Integer)
            Console.WriteLine("show " & v)
        End Sub

        Sub Main()
            Dim s = Sub()
                        Show(Tick())
                        Tick()
                        Show(Tick() + Tick())
                    End Sub
            s()
            Console.WriteLine(calls)
        End Sub
        """, """
        show 1
        show 7
        4
        """, Bk.All, HangSafe: true);

    /// <summary>the counter through lambda Dims (`Dim a = Tick()`, `Dim b = Tick() + Tick()`): CS0103 'a' before (#165), then #179.</summary>
    internal static readonly TempProbe g_once_dim = new("g_once_dim", """
        Dim calls As Integer = 0

        Function Tick() As Integer
            calls = calls + 1
            Return calls
        End Function

        Sub Main()
            Dim f = Function() As Integer
                        Dim a As Integer = Tick()
                        Dim b As Integer = Tick() + Tick()
                        Return a + b
                    End Function
            Console.WriteLine(f())
            Console.WriteLine(calls)
        End Sub
        """, """
        6
        3
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // I. `Me` captured by a lambda (#237)
    // ================================================================================================

    /// <summary>CppMeAsValueTests E09a: `Sub() k.Take(Me)` in an ordinary method. A call through an object was skipped as a temp: NullReferenceException.</summary>
    internal static readonly TempProbe e09a_me_method = new("e09a_me_method", """
        Class Sink
            Public Last As Counter
            Public Sub Take(c As Counter)
                Last = c
            End Sub
        End Class
        Class Counter
            Public N As Integer
            Public Sub Run(k As Sink)
                Dim f As Action = Sub() k.Take(Me)
                N = 5
                f()
            End Sub
        End Class
        Sub Main()
            Dim c As New Counter()
            Dim k As New Sink()
            c.Run(k)
            Console.WriteLine(k.Last.N)
            Console.WriteLine(k.Last Is c)
        End Sub
        """, """
        5
        True
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript | Bk.Msil, HangSafe: true);

    /// <summary>E09b: the same inside `Sub New`.</summary>
    internal static readonly TempProbe e09b_me_ctor = new("e09b_me_ctor", """
        Class Sink
            Public Last As Counter
            Public Sub Take(c As Counter)
                Last = c
            End Sub
        End Class
        Class Counter
            Public N As Integer = 2
            Public Sub New(k As Sink)
                Dim f As Action = Sub() k.Take(Me)
                f()
            End Sub
        End Class
        Sub Main()
            Dim k As New Sink()
            Dim c As New Counter(k)
            Console.WriteLine(k.Last.N)
            Console.WriteLine(k.Last Is c)
        End Sub
        """, """
        2
        True
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript | Bk.Msil, HangSafe: true);

    // ================================================================================================
    // J. A ByRef call in statement form (#166)
    // ================================================================================================

    /// <summary>a Sub lambda calls `Inc(n)` on a captured local: CS1620, the `ref` was lost. (JavaScript refuses a ByRef parameter by design; C++ refuses to lower it; MSIL does not build it.)</summary>
    internal static readonly TempProbe h166_byref = new("h166_byref", """
        Sub Inc(ByRef v As Integer)
            v = v + 100
        End Sub

        Sub Main()
            Dim n As Integer = 1
            Dim bump = Sub()
                           Inc(n)
                           Console.WriteLine("in " & n)
                       End Sub
            bump()
            Console.WriteLine(n)
        End Sub
        """, """
        in 101
        101
        """, Bk.CSharp, HangSafe: true);

    /// <summary>a Function lambda passes its own Dim: CS0103 't' before. (JavaScript: ByRef is refused, BL7002.)</summary>
    internal static readonly TempProbe h166_byref_loc = new("h166_byref_loc", """
        Sub Inc(ByRef v As Integer)
            v = v + 100
        End Sub

        Sub Main()
            Dim f = Function(a As Integer) As Integer
                        Dim t As Integer = a
                        Inc(t)
                        Return t
                    End Function
            Console.WriteLine(f(1))
        End Sub
        """, """
        101
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    /// <summary>a Function lambda passes its own parameter: CS1620 before.</summary>
    internal static readonly TempProbe h166_byref_par = new("h166_byref_par", """
        Sub Inc(ByRef v As Integer)
            v = v + 100
        End Sub

        Sub Main()
            Dim f = Function(a As Integer) As Integer
                        Inc(a)
                        Return a
                    End Function
            Console.WriteLine(f(2))
        End Sub
        """, """
        102
        """, Bk.CSharp | Bk.Cpp | Bk.Msil, HangSafe: true);

    // ================================================================================================
    // K. A lambda's name must not leak into the enclosing function's name map
    // ================================================================================================

    /// <summary>a lambda parameter `G` beside a module global `g`: the later `g` read after the lambda must stay `g`. Right before the fix too (the old code restored it); mutant M4b.</summary>
    internal static readonly TempProbe n_leakparam = new("n_leakparam", """
        Dim g As Integer = 5

        Sub Main()
            Dim f = Function(G As Integer) g + 1
            Console.WriteLine(f(10))
            Console.WriteLine(g)
        End Sub
        """, """
        11
        5
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda's own `Dim T` beside a global `t`: the `t` read after the lambda was written `T` (CS0103 before).</summary>
    internal static readonly TempProbe n_leaklocal = new("n_leaklocal", """
        Dim t As Integer = 5

        Sub Main()
            Dim f = Function(a As Integer) As Integer
                        Dim T As Integer = a * 2
                        Return T + 1
                    End Function
            Console.WriteLine(f(10))
            Console.WriteLine(t)
        End Sub
        """, """
        21
        5
        """, Bk.All, HangSafe: true);

    /// <summary>the same with a class field `t` read and written after the lambda.</summary>
    internal static readonly TempProbe n_leaklocal2 = new("n_leaklocal2", """
        Class C
            Public t As Integer = 5
            Public Function Run() As Integer
                Dim f = Function(a As Integer) As Integer
                            Dim T As Integer = a * 2
                            Return T + 1
                        End Function
                Dim r As Integer = f(10)
                t = t + 1
                Return r * 100 + t
            End Function
        End Class

        Sub Main()
            Dim c As New C()
            Console.WriteLine(c.Run())
        End Sub
        """, """
        2106
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // L2. Lambdas among loops and Ifs: ADR-0014's per-iteration Dim, a For Each inside a lambda, an ElseIf chain
    // ================================================================================================

    /// <summary>a loop-body Dim captured by `Function() x` (ADR-0014: one variable per iteration, copied forward): the per-iteration plan must still be the function's AFTER the lambda is written (mutant M11: CS1524 'Expected catch or finally').</summary>
    internal static readonly TempProbe p_loopdim_read = new("p_loopdim_read", """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))
            For i As Integer = 1 To 3
                Dim x As Integer
                x = x + 10
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Console.WriteLine(fs(k)())
            Next
        End Sub
        """, """
        10
        20
        30
        """, Bk.All, HangSafe: true);

    /// <summary>two lambdas in a per-iteration loop body (a Sub that WRITES the Dim, a Function that reads it) and a block lambda after the loop that reads them: CS1643 before the fix; M11 again.</summary>
    internal static readonly TempProbe p_loopdim_write = new("p_loopdim_write", """
        Sub Main()
            Dim gets As New List(Of Func(Of Integer))
            Dim total As Integer = 0
            For i As Integer = 1 To 3
                Dim x As Integer = i * 10
                Dim bump As Action = Sub() x = x + 1
                bump()
                gets.Add(Function() x)
                total = total + x
            Next
            Dim after = Function() As Integer
                            Dim s As Integer = 0
                            For k As Integer = 0 To gets.Count - 1
                                s = s * 100 + gets(k)()
                            Next
                            Return s
                        End Function
            Console.WriteLine(total & " " & after())
        End Sub
        """, """
        63 112131
        """, Bk.All, HangSafe: true);

    /// <summary>a For Each inside a Sub lambda that sits in a For Each body (the lambda reads the outer loop variable): 0 for 180 before the fix.</summary>
    internal static readonly TempProbe p_foreach_rename = new("p_foreach_rename", """
        Sub Main()
            Dim xs As New List(Of Integer)
            xs.Add(1)
            xs.Add(2)
            Dim ys As New List(Of Integer)
            ys.Add(10)
            ys.Add(20)
            Dim acc As Integer = 0
            For Each x As Integer In xs
                Dim f = Sub()
                            For Each y As Integer In ys
                                acc = acc + x * y
                            Next
                        End Sub
                f()
                f()
            Next
            Console.WriteLine(acc)
        End Sub
        """, """
        180
        """, Bk.All, HangSafe: true);

    /// <summary>a lambda in the MIDDLE arm of an ElseIf chain: the chain's arms share one merge block, and the enclosing function's claim on it must survive the lambda (mutant M21). Right before the fix too.</summary>
    internal static readonly TempProbe p_elseif_lambda = new("p_elseif_lambda", """
        Sub Main()
            Dim n As Integer = 0
            For i As Integer = 1 To 4
                If i = 1 Then
                    n = n + 1
                ElseIf i = 2 Then
                    Dim f = Sub() n = n + 10
                    f()
                ElseIf i = 3 Then
                    n = n + 100
                Else
                    n = n + 1000
                End If
            Next
            Console.WriteLine(n)
        End Sub
        """, """
        1111
        """, Bk.All, HangSafe: true);

    // ================================================================================================
    // L. Temps around a lambda
    // ================================================================================================

    /// <summary>a value computed from a call before the lambda and used after it, with the lambda called twice. Right before the fix.</summary>
    internal static readonly TempProbe j_tempafter = new("j_tempafter", """
        Function Calc(v As Integer) As Integer
            Console.WriteLine("calc " & v)
            Return v * 2
        End Function

        Sub Main()
            Dim n As Integer = 0
            Dim a As Integer = Calc(1) + 1
            Dim f = Sub()
                        n = n + a
                        Console.WriteLine("f " & n)
                    End Sub
            f()
            Dim b As Integer = Calc(a) + n
            f()
            Console.WriteLine(a & " " & b & " " & n)
        End Sub
        """, """
        calc 1
        f 3
        calc 3
        f 6
        3 9 6
        """, Bk.All, HangSafe: true);

    /// <summary>values read twice inside one-block lambdas (`q.F * q.F`, `(v + g) * (v + g)`): the shapes that came closest to an ADR-0001 materialised temp inside a lambda. No mutant of the materialised-temp check is killed by them (see the fixture header).</summary>
    internal static readonly TempProbe n_mat = new("n_mat", """
        Class P
            Public F As Integer = 6
        End Class

        Sub Main()
            Dim o As New P()
            Dim sq = Function(q As P) q.F * q.F
            Console.WriteLine(sq(o))
            Dim g As Integer = 3
            Dim h = Function(v As Integer) (v + g) * (v + g)
            Console.WriteLine(h(1))
        End Sub
        """, """
        36
        16
        """, Bk.All, HangSafe: true);

    /// <summary>`If(...)` in one-block lambdas: lowers to blocks, so a block lambda; CS1643 before.</summary>
    internal static readonly TempProbe n_mat3 = new("n_mat3", """
        Class P
            Public S As String
            Public F As Integer = 3
        End Class

        Dim gl As Integer = 4

        Sub Main()
            Dim o As New P()
            Dim h = Function(q As P) If(q.F > 2, q.F, 0)
            Console.WriteLine(h(o))
            Dim k = Function(v As Integer) If(gl + v > 4, gl + v, 0)
            Console.WriteLine(k(1))
            Dim m = Function(q As P) q.F * q.F + q.F
            Console.WriteLine(m(o))
        End Sub
        """, """
        3
        5
        12
        """, Bk.All, HangSafe: true);

    /// <summary>The rows, in table order.</summary>
    internal static readonly IReadOnlyList<TempProbe> All = new[]
    {
        a_loc, a_loc_c, a_par, a_fld, a_fld_c, a_mefld,
        a_glob, k_shared, k4_ownparam, b_subpar1, b_fnpar, b_fnpar_c,
        b_parloop, d_nest_par, c_asg, c_call, c_if, c_loop,
        c_while, c_sel, c_try, c_tryfin, k_catch, k_dowhile,
        k_ifouter, j_exitloop, j_exitsub, j_lambdaarr, j_foreach2, j_select,
        j_afterlambda, d_nest, d_nest_c, k5_nested_dim, e_meth2, j_mecall,
        e_ctor, e_mybase, e_mybase_c, k_prop, k_global, k_global2,
        f_pass, f_list, f_ret, k_twice, j_names, j_case,
        j_shadowfld, n8_hide_field, n9_hide_global, n_uninit, n_uninit2, c_call2,
        c_callexpr, i_expr, g_once, g_once_sub, g_once_dim, e09a_me_method,
        e09b_me_ctor, h166_byref, h166_byref_loc, h166_byref_par, n_leakparam, n_leaklocal,
        n_leaklocal2, p_loopdim_read, p_loopdim_write, p_foreach_rename, p_elseif_lambda, j_tempafter,
        n_mat, n_mat3
    };
}
