using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #123 — EXECUTION: the untyped Const (D1) and VB's conditional `If(cond, a, b)` (D2), run on the four backends that
//  print, through every entry point.
//
//  ⭐ THE ORACLE IS vbc, NOT A BACKEND. Every expected value is the output of the SDK's `vbc` running the probe (a file-scope
//  Const/Dim/Sub wrapped in a `Module`; S/t124/vbv2.py), taken from the implementer's `.exp` files (S/t123/probes) and
//  re-checked by the test-writer — the variants marked "B" were written by the test-writer and have vbc's answer of their own.
//
//  ⭐ ENTRY POINTS (CLAUDE.md: "test both entry points", "validate codegen through the CLI and the IR optimizer"). A
//  single-file probe goes through the real `BasicLang` CLI (standard passes), the real CLI with `--optimize` (aggressive) and
//  `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` — what a Release .blproj build and the IDE call. A multi-file
//  probe goes through `BasicLang build P.blproj` (Debug, Release) and `CompileProjectFiles` (standard, aggressive); C++ runs only
//  the two in-process entry points there (the CLI's C++ project build is MSVC-only).
//
//  ⭐ THE SIDE-EFFECT CONTRACT IS HELD ON EVERY BACKEND, NOT ON C# ALONE. Only the chosen operand of an If() may run. i3nest (nested
//  Ifs, each arm prints) and i10sc (AndAlso/OrElse inside If) run on all four; i1side runs on C#, C++ and JavaScript and its
//  twin i1sideB (no `Not`) on MSIL. The mutant that evaluates both arms (M2both) is visible on C# ONLY through i3nest, which is
//  why the other three backends' rows matter: they are what catches it elsewhere.
//
//  ⛔ CELLS WITH NO EXPECTATION, BY PROBE — each is measured identically on the BEFORE build's pre-existing machinery and is not
//  the conditional's or the constant's (asserting one would pin a defect). Where a twin runs the same construct on the backend
//  that fails the original, the twin is the row.
//
//    c1mod   JavaScript  BL7004 — a Char constant, by design (JavaScript has no char).           twin c1modB (no Char, no `Not`)
//    c1mod   MSIL        `Not E` over a Boolean Const prints True: MSIL's `Not` is bitwise.       #257     twin c1modB
//    c5big   JavaScript  BL7003 — a Long constant, by design (a JS number is a double).           none
//    i1side  MSIL        `If(Not t, …)` takes the wrong arm, same MSIL `Not`.                     #257     twin i1sideB
//    i4loop  C#          ⛔⛔ HANGS. `While If(…)` / `Do While If(…)` / `Loop Until If(…)` compute the condition's control flow ONCE,
//                        before the loop, so the loop never ends (the same is true of AndAlso on master).   #256
//                        NEVER run it on C# without a timeout. i4forB runs the For bounds on C#.
//    i4loop  JavaScript  "a loop header whose branch does not target the loop's own .end block" — refused.   #257
//    i4forB  JavaScript  the same refusal, for an If() in a For bound.                                        #257
//    i5lambda C#         CS1643 — a lambda whose body has control flow is emitted without its return paths.  #136
//    i6arg   C#          arguments evaluated OUT OF ORDER once one has control flow (`Pair(Note("first"), If(…), Note("third"))`
//                        prints second, first, third). Pre-existing; no task.                                  twin i6argB (no Pair)
//    i6arg   MSIL        `If(Not t, 9, 2)`, the same MSIL `Not`.                                          #257     twin i6argB
//    i13obj  C++         'Object' has no C++ mapping — by design.                                          none
//    i13obj  MSIL        NullReferenceException: `If(Not t, Nothing, New Box())` takes the wrong arm.     #257     twin i13objB
//    MC2     all         a Module-block Const in a file that comes AFTER its user is Object, typed or not — pre-existing,
//                        no task; MC1 (first) and MC3 (last, no Module block) are the rows.
//
//  ⚠ #256 and #257 are the inherited defects the carrier lowering shares with `AndAlso`/`OrElse`; they are not introduced by #123.
//  ⚠ Every other row is one the conditional and the constant DO run correctly on, in all three entry points.
// ================================================================================================

internal static class UntypedConstAndConditionalProbes
{
    // ---------------------------------------- D1: an untyped Const ----------------------------------------

    internal static readonly TempProbe c1mod = new("c1mod", """
        ' D1: module-scope untyped Const, each kind, and expressions over other constants
        Const A = 801
        Const H = A / 2
        Const C = 3.14
        Const D = "s"
        Const E = True
        Const G = A * 2
        Const N = -5
        Const X = &HFF
        Const K1 = "a"c
        Const M = A \ 2
        Const P = H + H
        Const Q = G + N

        Sub Main()
            Console.WriteLine(H)
            Console.WriteLine(C * 2)
            Console.WriteLine(D & "x")
            Console.WriteLine(Not E)
            Console.WriteLine(G)
            Console.WriteLine(N + X)
            Console.WriteLine(K1)
            Console.WriteLine(M)
            Console.WriteLine(P)
            Console.WriteLine(Q)
            ' an Integer constant makes an inferred Dim Integer: 2.5 narrows to 2; a Double one keeps 2.5
            Dim vi = A
            vi = 2.5
            Console.WriteLine(vi)
            Dim vd = H
            vd = 2.5
            Console.WriteLine(vd)
            Dim vp = P
            vp = 2.5
            Console.WriteLine(vp)
        End Sub
        """, """
        400.5
        6.28
        sx
        False
        1602
        250
        a
        400
        801
        1597
        2
        2.5
        2.5
        """, Bk.CSharp | Bk.Cpp);

    internal static readonly TempProbe c1modB = new("c1modB", """
        ' D1 (variant of c1mod for JavaScript and MSIL): no Char constant (JS BL7004 by design), no `Not` of a Boolean Const (MSIL #257)
        Const A = 801
        Const H = A / 2
        Const C = 3.14
        Const D = "s"
        Const E = True
        Const G = A * 2
        Const N = -5
        Const X = &HFF
        Const M = A \ 2
        Const P = H + H
        Const Q = G + N

        Sub Main()
            Console.WriteLine(H)
            Console.WriteLine(C * 2)
            Console.WriteLine(D & "x")
            Console.WriteLine(E)
            Console.WriteLine(G)
            Console.WriteLine(N + X)
            Console.WriteLine(M)
            Console.WriteLine(P)
            Console.WriteLine(Q)
            ' an Integer constant makes an inferred Dim Integer: 2.5 narrows to 2; a Double one keeps 2.5
            Dim vi = A
            vi = 2.5
            Console.WriteLine(vi)
            Dim vd = H
            vd = 2.5
            Console.WriteLine(vd)
            Dim vp = P
            vp = 2.5
            Console.WriteLine(vp)
        End Sub
        """, """
        400.5
        6.28
        sx
        True
        1602
        250
        400
        801
        1597
        2
        2.5
        2.5
        """, Bk.JavaScript | Bk.Msil);

    internal static readonly TempProbe c2loc = new("c2loc", """
        ' D1: local untyped Const, in a Sub and in a Function, including one over module constants
        Const W = 800

        Function Half() As Double
            Const HW = W / 2
            Return HW + 0.25
        End Function

        Sub Main()
            Const LA = 5
            Const LB = LA * 1.5
            Const LS = "loc"
            Const LT = LA > 3
            Const LW = W - LA
            Console.WriteLine(LA)
            Console.WriteLine(LB)
            Console.WriteLine(LS & LA)
            Console.WriteLine(LT)
            Console.WriteLine(LW)
            Console.WriteLine(Half())
            Dim v = LA
            v = 2.5
            Console.WriteLine(v)
            Dim u = LB
            u = 2.5
            Console.WriteLine(u)
        End Sub
        """, """
        5
        7.5
        loc5
        True
        795
        400.25
        2
        2.5
        """, Bk.All);

    internal static readonly TempProbe c3cls = new("c3cls", """
        ' D1: class-level untyped Const, read inside the class and through the class name
        Class Box
            Public Const Size = 12
            Public Const Scale = 1.5
            Public Const Label = "box"
            Private Const Hidden = 24

            Public Function Area() As Double
                Return Size * Size * Scale
            End Function

            Public Function Twice() As Integer
                Return Hidden
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            Console.WriteLine(b.Area())
            Console.WriteLine(b.Twice())
            Console.WriteLine(Box.Size + 1)
            Console.WriteLine(Box.Scale * 2)
            Console.WriteLine(Box.Label & "!")
            Dim v = Box.Size
            v = 2.5
            Console.WriteLine(v)
        End Sub
        """, """
        216
        24
        13
        3
        box!
        2
        """, Bk.All);

    internal static readonly TempProbe c4init = new("c4init", """
        ' D1: untyped Consts feeding module-scope initializers and an array bound (the Pong/SpaceShooter shapes)
        Const SPEED = 350.0
        Const COUNT = 4
        Const WIDTH = 800

        Dim half As Double = SPEED / 2
        Dim edge As Integer = WIDTH - 30
        Dim items(COUNT) As Integer

        Sub Main()
            Console.WriteLine(half)
            Console.WriteLine(edge)
            Console.WriteLine(items.Length)
            For i = 0 To COUNT - 1
                items(i) = i * WIDTH
            Next
            Console.WriteLine(items(COUNT - 1))
        End Sub
        """, """
        175
        770
        5
        2400
        """, Bk.All);

    internal static readonly TempProbe c5big = new("c5big", """
        ' D1: an integer literal past Integer is Long, and so is the constant (not JavaScript: BL7003 by design)
        Const BIG = 3000000000
        Const MAXI = 2147483647
        Const NEG = -2147483648

        Sub Main()
            Console.WriteLine(BIG + 1)
            Dim v = MAXI
            v = 2.5
            Console.WriteLine(v)
            Dim w As Long = MAXI
            Console.WriteLine(w + 1)
            Console.WriteLine(NEG - 1)
        End Sub
        """, """
        3000000001
        2
        2147483648
        -2147483649
        """, Bk.CSharp | Bk.Cpp | Bk.Msil);

    internal static readonly TempProbe c6use = new("c6use", """
        ' D1: an untyped Const as a Select Case label, an Optional default, an array bound, a For bound and an inherited class Const
        Const LIM = 3
        Const NAME = "bob"
        Class Base
            Public Const Step1 = 10
            Protected Const Half = 0.5
        End Class
        Class Derived
            Inherits Base
            Public Function Go() As Double
                Return Step1 * Half
            End Function
        End Class
        Function Def(Optional x As Integer = LIM) As Integer
            Return x * 2
        End Function
        Sub Main()
            Dim n As Integer = 3
            Select Case n
                Case LIM
                    Console.WriteLine("lim")
                Case Else
                    Console.WriteLine("other")
            End Select
            Select Case "bob"
                Case NAME
                    Console.WriteLine("name")
            End Select
            Console.WriteLine(Def())
            Console.WriteLine(Def(5))
            Dim arr(LIM + 1) As Integer
            Console.WriteLine(arr.Length)
            Dim total As Integer = 0
            For i = 1 To LIM
                total += i
            Next
            Console.WriteLine(total)
            Console.WriteLine(New Derived().Go())
            Console.WriteLine(Base.Step1)
        End Sub
        """, """
        lim
        name
        6
        10
        5
        6
        5
        10
        """, Bk.All);

    // ---------------------------------------- D2: If(cond, a, b) ----------------------------------------

    internal static readonly TempProbe i1side = new("i1side", """
        ' D2: only the chosen operand is evaluated
        Function Note(s As String, v As Integer) As Integer
            Console.WriteLine("eval " & s)
            Return v
        End Function

        Sub Main()
            Dim t As Boolean = True
            Dim r = If(t, Note("a", 1), Note("b", 2))
            Console.WriteLine(r)
            r = If(Not t, Note("c", 3), Note("d", 4))
            Console.WriteLine(r)
            Console.WriteLine(If(Note("cond", 1) > 0, Note("e", 5), Note("f", 6)))
        End Sub
        """, """
        eval a
        1
        eval d
        4
        eval cond
        eval e
        5
        """, Bk.CSharp | Bk.Cpp | Bk.JavaScript);

    internal static readonly TempProbe i1sideB = new("i1sideB", """
        ' D2 (variant of i1side for MSIL): only the chosen operand is evaluated; no `Not` (MSIL #257)
        Function Note(s As String, v As Integer) As Integer
            Console.WriteLine("eval " & s)
            Return v
        End Function

        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Dim r = If(t, Note("a", 1), Note("b", 2))
            Console.WriteLine(r)
            r = If(f, Note("c", 3), Note("d", 4))
            Console.WriteLine(r)
            Console.WriteLine(If(Note("cond", 1) > 0, Note("e", 5), Note("f", 6)))
        End Sub
        """, """
        eval a
        1
        eval d
        4
        eval cond
        eval e
        5
        """, Bk.Msil);

    internal static readonly TempProbe i2types = new("i2types", """
        ' D2: the result type is the dominant type of the two operands
        Class Animal
            Public Overridable Function Sound() As String
                Return "..."
            End Function
        End Class

        Class Dog
            Inherits Animal
            Public Overrides Function Sound() As String
                Return "woof"
            End Function
        End Class

        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            ' Integer and Double: Double, so a later 2.5 is kept
            Dim w = If(t, 1, 2.5)
            Console.WriteLine(w)
            w = 2.5
            Console.WriteLine(w)
            ' Integer and Integer: Integer, so a later 2.5 narrows
            Dim n = If(t, 1, 2)
            n = 2.5
            Console.WriteLine(n)
            ' String and Nothing: String
            Dim s = If(f, "yes", Nothing)
            Console.WriteLine(s Is Nothing)
            s = If(t, "yes", Nothing)
            Console.WriteLine(s & "!")
            ' class and Nothing: the class
            Dim a = If(f, New Dog(), Nothing)
            Console.WriteLine(a Is Nothing)
            ' Derived and Base: Base, with the derived override running
            Dim b = If(t, New Dog(), New Animal())
            Console.WriteLine(b.Sound())
            b = New Animal()
            Console.WriteLine(b.Sound())
            ' Nothing first, String second
            Dim z = If(t, Nothing, "later")
            Console.WriteLine(z Is Nothing)
            ' Single and Integer: Single
            Dim sg As Single = 1.5F
            Dim q = If(f, sg, 3)
            Console.WriteLine(q + sg)
        End Sub
        """, """
        1
        2.5
        2
        True
        yes!
        True
        woof
        ...
        True
        4.5
        """, Bk.All);

    internal static readonly TempProbe i3nest = new("i3nest", """
        ' D2: nested If(), each level evaluating only its chosen operand
        Function Note(s As String, v As Integer) As Integer
            Console.WriteLine("eval " & s)
            Return v
        End Function

        Sub Main()
            For i = 0 To 3
                Dim a As Boolean = i Mod 2 = 1
                Dim b As Boolean = i \ 2 = 1
                Dim r = If(a, If(b, Note("ab", 1), Note("a", 2)), If(b, Note("b", 3), Note("none", 4)))
                Console.WriteLine(r)
            Next
            Console.WriteLine(If(If(True, False, True), "x", If(False, "y", "z")))
        End Sub
        """, """
        eval none
        4
        eval a
        2
        eval b
        3
        eval ab
        1
        z
        """, Bk.All);

    internal static readonly TempProbe i4loop = new("i4loop", """
        ' D2: If() in loop conditions and bounds
        Dim n As Integer = 0

        Function Tick() As Boolean
            n = n + 1
            Console.WriteLine("tick " & n)
            Return n < 3
        End Function

        Sub Main()
            Dim i As Integer = 0
            While If(i < 3, True, False)
                Console.WriteLine("while " & i)
                i = i + 1
            End While
            Dim useTick As Boolean = True
            Do While If(useTick, Tick(), False)
                Console.WriteLine("do " & n)
            Loop
            Dim k As Integer = 10
            Do
                k = k - 3
            Loop Until If(k < 0, True, k = 1)
            Console.WriteLine("until " & k)
            For j = If(useTick, 1, 5) To If(useTick, 3, 9)
                Console.WriteLine("for " & j)
            Next
        End Sub
        """, """
        while 0
        while 1
        while 2
        tick 1
        do 1
        tick 2
        do 2
        tick 3
        until 1
        for 1
        for 2
        for 3
        """, Bk.Cpp | Bk.Msil);

    internal static readonly TempProbe i4forB = new("i4forB", """
        ' D2 (For bounds): If() in a counted For's bounds (C# #256 covers the While/Do condition forms only; JavaScript refuses a loop header with control flow)
        Sub Main()
            Dim useTick As Boolean = True
            For j = If(useTick, 1, 5) To If(useTick, 3, 9)
                Console.WriteLine("for " & j)
            Next
            For j = If(useTick, 5, 7) To If(useTick, 6, 9)
                Console.WriteLine("again " & j)
            Next
        End Sub
        """, """
        for 1
        for 2
        for 3
        again 5
        again 6
        """, Bk.CSharp | Bk.Cpp | Bk.Msil);

    internal static readonly TempProbe i5lambda = new("i5lambda", """
        ' D2: If() inside lambdas, with a captured variable
        Sub Main()
            Dim sign As Func(Of Integer, String) = Function(x As Integer) If(x > 0, "pos", If(x < 0, "neg", "zero"))
            Console.WriteLine(sign(5))
            Console.WriteLine(sign(-2))
            Console.WriteLine(sign(0))
            Dim limit As Integer = 10
            Dim clampIt As Func(Of Integer, Integer) = Function(x As Integer) If(x > limit, limit, x)
            Console.WriteLine(clampIt(3))
            Console.WriteLine(clampIt(42))
            Dim act As Action(Of Integer) = Sub(x As Integer) Console.WriteLine(If(x Mod 2 = 0, "even", "odd"))
            act(3)
            act(4)
        End Sub
        """, """
        pos
        neg
        zero
        3
        10
        odd
        even
        """, Bk.Cpp | Bk.JavaScript | Bk.Msil);

    internal static readonly TempProbe i6arg = new("i6arg", """
        ' D2: If() as an argument, in concatenation and interpolation, and as a receiver
        Function Note(s As String, v As Integer) As Integer
            Console.WriteLine("eval " & s)
            Return v
        End Function

        Function Pair(a As Integer, b As Integer, c As Integer) As String
            Return a & "," & b & "," & c
        End Function

        Function Max2(a As Integer, b As Integer) As Integer
            Return If(a > b, a, b)
        End Function

        Sub Main()
            Dim t As Boolean = True
            Console.WriteLine(Pair(Note("first", 1), If(t, Note("second", 2), Note("never", 0)), Note("third", 3)))
            Console.WriteLine("[" & If(t, "a", "b") & "]")
            Dim n As Integer = 7
            Console.WriteLine($"{n} is {If(n Mod 2 = 0, "even", "odd")}")
            Console.WriteLine(If(t, "abc", "de").Length)
            Console.WriteLine(Max2(If(t, 4, 1), If(Not t, 9, 2)))
        End Sub
        """, """
        eval first
        eval second
        eval third
        1,2,3
        [a]
        7 is odd
        3
        4
        """, Bk.Cpp | Bk.JavaScript);

    internal static readonly TempProbe i6argB = new("i6argB", """
        ' D2 (variant of i6arg for C# and MSIL): as an argument, in concatenation and interpolation, and as a receiver; no multi-argument call mixing calls with an If() (C# evaluation order), no `Not` (MSIL #257)
        Function Note(s As String, v As Integer) As Integer
            Console.WriteLine("eval " & s)
            Return v
        End Function

        Function Pair(a As Integer, b As Integer, c As Integer) As String
            Return a & "," & b & "," & c
        End Function

        Function Max2(a As Integer, b As Integer) As Integer
            Return If(a > b, a, b)
        End Function

        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Console.WriteLine("[" & If(t, "a", "b") & "]")
            Dim n As Integer = 7
            Console.WriteLine($"{n} is {If(n Mod 2 = 0, "even", "odd")}")
            Console.WriteLine(If(t, "abc", "de").Length)
            Console.WriteLine(Max2(If(t, 4, 1), If(f, 9, 2)))
        End Sub
        """, """
        [a]
        7 is odd
        3
        4
        """, Bk.CSharp | Bk.Msil);

    internal static readonly TempProbe i7guard = new("i7guard", """
        ' D2: the unchosen operand must not run: recursion, division by zero, a Nothing dereference
        Class Box
            Public V As Integer
            Public Sub New(v As Integer)
                Me.V = v
            End Sub
        End Class

        Function Fact(n As Integer) As Integer
            Return If(n <= 1, 1, n * Fact(n - 1))
        End Function

        Function SafeDiv(a As Integer, d As Integer) As Integer
            Return If(d = 0, -1, a \ d)
        End Function

        Function ValueOf(b As Box) As Integer
            Return If(b Is Nothing, -1, b.V)
        End Function

        Sub Main()
            Console.WriteLine(Fact(6))
            Console.WriteLine(SafeDiv(10, 0))
            Console.WriteLine(SafeDiv(10, 3))
            Console.WriteLine(ValueOf(Nothing))
            Console.WriteLine(ValueOf(New Box(9)))
        End Sub
        """, """
        720
        -1
        3
        -1
        9
        """, Bk.All);

    internal static readonly TempProbe i8const = new("i8const", """
        ' D2 x D1: If() in a local Const (VB: a constant expression), and If() over untyped constants
        Const SPEED = 350.0
        Const LIVES = 3

        Sub Main()
            Const PICK = If(True, 1, 2.5)
            Dim v = PICK
            Console.WriteLine(v)
            v = 2.5
            Console.WriteLine(v)
            Dim vx As Double = 12
            vx = SPEED * If(vx > 0.0, -1, 1)
            Console.WriteLine(vx)
            vx = SPEED * If(vx > 0.0, -1, 1)
            Console.WriteLine(vx)
            Dim msg = If(LIVES > 2, "healthy", "hurt")
            Console.WriteLine(msg)
        End Sub
        """, """
        1
        2.5
        -350
        350
        healthy
        """, Bk.All);

    internal static readonly TempProbe i9select = new("i9select", """
        ' D2: If() as a Select Case subject and a Case value
        Sub Main()
            Dim t As Boolean = True
            For i = 1 To 3
                Select Case If(t, i, -i)
                    Case 1
                        Console.WriteLine("one")
                    Case 2
                        Console.WriteLine("two")
                    Case Else
                        Console.WriteLine("other " & i)
                End Select
            Next
        End Sub
        """, """
        one
        two
        other 3
        """, Bk.All);

    internal static readonly TempProbe i10sc = new("i10sc", """
        ' D2: If() with AndAlso/OrElse in its condition and operands
        Function Note(s As String, v As Boolean) As Boolean
            Console.WriteLine("eval " & s)
            Return v
        End Function

        Sub Main()
            Dim s As String = Nothing
            Console.WriteLine(If(s IsNot Nothing AndAlso s.Length > 0, s, "empty"))
            s = "full"
            Console.WriteLine(If(s IsNot Nothing AndAlso s.Length > 0, s, "empty"))
            Dim r = If(Note("c", False) OrElse Note("d", True), Note("x", True) AndAlso Note("y", False), Note("z", True))
            Console.WriteLine(r)
            If If(r, False, True) AndAlso Note("tail", True) Then
                Console.WriteLine("then")
            End If
        End Sub
        """, """
        empty
        full
        eval c
        eval d
        eval x
        eval y
        False
        eval tail
        then
        """, Bk.All);

    internal static readonly TempProbe i11cls = new("i11cls", """
        ' D2: If() in a constructor, a property getter and a method
        Class Counter
            Private _n As Integer
            Public Sub New(start As Integer)
                _n = If(start < 0, 0, start)
            End Sub
            Public ReadOnly Property Label As String
                Get
                    Return If(_n = 1, "one item", _n & " items")
                End Get
            End Property
            Public Sub Bump(big As Boolean)
                _n = _n + If(big, 10, 1)
            End Sub
        End Class

        Sub Main()
            Dim c As New Counter(-4)
            Console.WriteLine(c.Label)
            c.Bump(False)
            Console.WriteLine(c.Label)
            c.Bump(True)
            Console.WriteLine(c.Label)
        End Sub
        """, """
        0 items
        one item
        11 items
        """, Bk.All);

    internal static readonly TempProbe i12pong = new("i12pong", """
        ' D2: the two Samples/Pong shapes, driven without the engine
        Const BALL_SPEED = 350.0
        Dim ballVX As Double = 350.0
        Dim score1 As Integer = 0

        Sub ResetBall()
            ballVX = BALL_SPEED * If(ballVX > 0, -1, 1)
        End Sub

        Sub Main()
            ResetBall()
            Console.WriteLine(ballVX)
            ResetBall()
            Console.WriteLine(ballVX)
            For Each s In New Integer() {3, 10, 12}
                score1 = s
                Dim winner = If(score1 >= 10, "Player 1", "Player 2")
                Console.WriteLine($"{winner} Wins!")
            Next
        End Sub
        """, """
        -350
        350
        Player 2 Wins!
        Player 1 Wins!
        Player 1 Wins!
        """, Bk.All);

    internal static readonly TempProbe i13obj = new("i13obj", """
        ' D2: operands with no dominant type make Object (VB, Option Strict Off); a Nothing operand takes the other's type
        Class Box
            Public V As Integer
        End Class

        Sub Main()
            Dim t As Boolean = True
            Dim o = If(t, 1, "one")
            Console.WriteLine(o)
            o = If(Not t, 1, "one")
            Console.WriteLine(o)
            Dim b = If(t, Nothing, New Box())
            Console.WriteLine(b Is Nothing)
            Dim c As Box = If(Not t, Nothing, New Box())
            c.V = 7
            Console.WriteLine(c.V)
        End Sub
        """, """
        1
        one
        True
        7
        """, Bk.CSharp | Bk.JavaScript);

    internal static readonly TempProbe i13objB = new("i13objB", """
        ' D2 (variant of i13obj for MSIL): operands with no dominant type make Object (VB, Option Strict Off); a Nothing operand takes the other's type
        Class Box
            Public V As Integer
        End Class

        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Dim o = If(t, 1, "one")
            Console.WriteLine(o)
            o = If(f, 1, "one")
            Console.WriteLine(o)
            Dim b = If(t, Nothing, New Box())
            Console.WriteLine(b Is Nothing)
            Dim c As Box = If(f, Nothing, New Box())
            c.V = 7
            Console.WriteLine(c.V)
        End Sub
        """, """
        1
        one
        True
        7
        """, Bk.Msil);

    internal static readonly TempProbe i14pos = new("i14pos", """
        ' D2: If() in every expression position: an If statement condition and ElseIf, an array literal element, a constructor argument, under Not, in arithmetic and concatenation, nested in a function, in a compound assignment and as a List.Add argument
        Class P
            Public V As Integer
            Public Sub New(v As Integer)
                Me.V = v
            End Sub
        End Class
        Function Sgn2(n As Integer) As Integer
            Return If(n > 0, 1, If(n < 0, -1, 0))
        End Function
        Sub Main()
            Dim t As Boolean = True
            Dim f As Boolean = False
            Dim n As Integer = 5
            If If(t, n > 3, n < 3) Then
                Console.WriteLine("if-cond")
            ElseIf If(f, True, False) Then
                Console.WriteLine("elseif")
            End If
            If n > 100 Then
                Console.WriteLine("no")
            ElseIf If(n > 4, True, False) Then
                Console.WriteLine("elseif-yes")
            End If
            Dim a = {If(t, 1, 2), If(f, 3, 4), 5}
            Console.WriteLine(a(0) + a(1) + a(2))
            Dim p As New P(If(t, 7, 8))
            Console.WriteLine(p.V)
            Console.WriteLine(Not If(t, f, t))
            Console.WriteLine(If(t, 1, 2) + If(f, 10, 20))
            Console.WriteLine(If(t, "a", "b") & If(f, "c", "d"))
            Dim s As String = If(n > 3, "long", "short")
            Console.WriteLine(s)
            Console.WriteLine("" & Sgn2(-9) & Sgn2(0) & Sgn2(4))
            Dim total As Integer = 0
            For i = 1 To 4
                total += If(i Mod 2 = 0, i, -i)
            Next
            Console.WriteLine(total)
            Dim q As Integer = If(t, 1, 2) * If(f, 3, 4) - If(t, 5, 6)
            Console.WriteLine(q)
            Console.WriteLine(If(t, 5, 0) \ 2)
            Dim lst As New List(Of Integer)
            lst.Add(If(t, 10, 20))
            Console.WriteLine(lst(0))
        End Sub
        """, """
        if-cond
        elseif-yes
        10
        7
        True
        21
        ad
        long
        -101
        2
        -1
        2
        10
        """, Bk.All);

    internal static readonly ProjectProbe MC1 = new("MC1", new[]
    {
        ("Consts.bas", """
            ' untyped Consts in the file that sorts FIRST
            Const WIDE = 800
            Const RATE = 1.5
            Const TITLE = "game"

            Module Settings
                Public Const Limit = 10
                Public Const Factor = 0.5
            End Module
            """),
        ("Main.bas", """
            Sub Main()
                Dim w As Integer = WIDE * 2
                Console.WriteLine(w)
                Dim r As Double = RATE * 3
                Console.WriteLine(r)
                Console.WriteLine(TITLE & "!")
                Console.WriteLine(Settings.Limit + 1)
                Console.WriteLine(Limit * Factor)
                Dim v = WIDE
                v = 2.5
                Console.WriteLine(v)
                Console.WriteLine(If(WIDE > 500, "wide", "narrow"))
            End Sub
            """),
    }, """
        1600
        4.5
        game!
        11
        5
        2
        wide
        """);

    private static readonly (string Name, string Source) Mc3Main = ("Main.bas", """
        Sub Main()
            Dim w As Integer = WIDE * 2
            Console.WriteLine(w)
            Dim r As Double = RATE * 3
            Console.WriteLine(r)
            Console.WriteLine(TITLE & "!")
            Dim v = WIDE
            v = 2.5
            Console.WriteLine(v)
            Console.WriteLine(If(WIDE > 500, "wide", "narrow"))
        End Sub
        """);

    private static readonly (string Name, string Source) Mc3Consts = ("Zconsts.bas", """
        ' untyped Consts in the file that sorts LAST, no Module block
        Const WIDE = 800
        Const RATE = 1.5
        Const TITLE = "game"
        """);

    private const string Mc3Expected = """
        1600
        4.5
        game!
        2
        wide
        """;

    internal static readonly ProjectProbe MC3 = new("MC3", new[] { Mc3Main, Mc3Consts }, Mc3Expected);

    /// <summary>MC3 with the files handed to the in-process entry points the other way round (the CLI reads a directory and sorts).</summary>
    internal static readonly ProjectProbe MC3r = new("MC3r", new[] { Mc3Consts, Mc3Main }, Mc3Expected);
}

/// <summary>
/// #123 D1 and D2 RUN on the four backends that print, through every entry point, and print vbc's answer. The constants and the
/// conditionals are one fixture because the samples use both and the probes that cross them (i8const, i12pong, c6use, MC1/MC3)
/// are the ones that matter most.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class UntypedConstAndConditionalExecutionTests
{
    private static IEnumerable<TestCaseData> Cells(params TempProbe[] probes)
        => probes.SelectMany(p => TempExec.Backends(p.Agrees).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> ProjectCells(params ProjectProbe[] probes)
        => probes.SelectMany(p => TempExec.Backends(p.Agrees).Select(b => new TestCaseData(p, b).SetName($"{p.Id}_{b}")));

    private static IEnumerable<TestCaseData> ConstCells() => Cells(UntypedConstAndConditionalProbes.c1mod, UntypedConstAndConditionalProbes.c1modB, UntypedConstAndConditionalProbes.c2loc, UntypedConstAndConditionalProbes.c3cls, UntypedConstAndConditionalProbes.c4init, UntypedConstAndConditionalProbes.c5big, UntypedConstAndConditionalProbes.c6use);

    private static IEnumerable<TestCaseData> ConditionalCells() => Cells(UntypedConstAndConditionalProbes.i1side, UntypedConstAndConditionalProbes.i1sideB, UntypedConstAndConditionalProbes.i2types, UntypedConstAndConditionalProbes.i3nest, UntypedConstAndConditionalProbes.i4loop, UntypedConstAndConditionalProbes.i4forB, UntypedConstAndConditionalProbes.i5lambda, UntypedConstAndConditionalProbes.i6arg, UntypedConstAndConditionalProbes.i6argB, UntypedConstAndConditionalProbes.i7guard, UntypedConstAndConditionalProbes.i8const, UntypedConstAndConditionalProbes.i9select, UntypedConstAndConditionalProbes.i10sc, UntypedConstAndConditionalProbes.i11cls, UntypedConstAndConditionalProbes.i12pong, UntypedConstAndConditionalProbes.i13obj, UntypedConstAndConditionalProbes.i13objB, UntypedConstAndConditionalProbes.i14pos);

    private static IEnumerable<TestCaseData> ProjectConstCells() => ProjectCells(UntypedConstAndConditionalProbes.MC1, UntypedConstAndConditionalProbes.MC3, UntypedConstAndConditionalProbes.MC3r);

    /// <summary>
    /// The tables ARE the proof, so their shape is pinned: a row cannot vanish (or a backend be dropped from a row) without this
    /// test saying so. It is also the fixture's one plain <c>[Test]</c> — <c>JsExecutionTierRosterTests</c> counts attributes, and a
    /// fixture whose tests are all <c>[TestCaseSource]</c> counts as empty. When #256 or #257 lands the excluded cells gain their
    /// backend and this changes on purpose.
    /// </summary>
    [Test]
    public void TheTables_HaveTheirRows()
    {
        static string Ids(IEnumerable<TestCaseData> cells) =>
            string.Join(",", cells.Select(c => c.Arguments[0]!.ToString()).Distinct());

        Assert.Multiple(() =>
        {
            Assert.That(Ids(ConstCells()), Is.EqualTo("c1mod,c1modB,c2loc,c3cls,c4init,c5big,c6use"));
            Assert.That(Ids(ConditionalCells()), Is.EqualTo("i1side,i1sideB,i2types,i3nest,i4loop,i4forB,i5lambda,i6arg,i6argB,i7guard,i8const,i9select,i10sc,i11cls,i12pong,i13obj,i13objB,i14pos"));
            Assert.That(Ids(ProjectConstCells()), Is.EqualTo("MC1,MC3,MC3r"));
            Assert.That(ConstCells().Count(), Is.EqualTo(23), "D1 cells (probe x backend)");
            Assert.That(ConditionalCells().Count(), Is.EqualTo(55), "D2 cells (probe x backend)");
            Assert.That(ProjectConstCells().Count(), Is.EqualTo(12), "MC1 x4, MC3 x4, MC3r x4");

            // The side-effect contract is held on all four backends: every backend has a row that prints which arm ran.
            var sideEffect = new[] { UntypedConstAndConditionalProbes.i1side, UntypedConstAndConditionalProbes.i1sideB, UntypedConstAndConditionalProbes.i3nest, UntypedConstAndConditionalProbes.i10sc };
            foreach (var backend in new[] { Bk.CSharp, Bk.Cpp, Bk.JavaScript, Bk.Msil })
                Assert.That(sideEffect.Any(p => p.Agrees.HasFlag(backend)), Is.True, $"no only-the-chosen-operand row on {backend}");

            // ⛔ i4loop must never reach C#: it hangs (#256).
            Assert.That(UntypedConstAndConditionalProbes.i4loop.Agrees.HasFlag(Bk.CSharp), Is.False, "i4loop on C# hangs (#256)");
        });
    }

    // ============================================================================================
    // D1 — an untyped Const, at every level, on every backend
    // ============================================================================================

    /// <summary>
    /// module (c1mod, c4init, c5big), local (c2loc), class (c3cls) and inherited/Case/Optional/bound (c6use) constants with no
    /// `As` clause print vbc's answer. The type is visible at run time: `Dim v = K : v = 2.5` prints 2 for an Integer constant and
    /// 2.5 for a Double one (mutants M1int and M1obj both change that).
    /// </summary>
    [TestCaseSource(nameof(ConstCells))]
    public void AnUntypedConst_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);

    /// <summary>
    /// MC1 and MC3: an untyped Const and a Module-block Const read from ANOTHER file, in the order that parses the constants
    /// first and the order that parses them last (where the analyzer of Main sees only the signature pass's stand-in).
    /// </summary>
    [TestCaseSource(nameof(ProjectConstCells))]
    public void AnUntypedConstInAnotherFile_PrintsVbsAnswer_InEveryEntryPoint(ProjectProbe probe, Bk backend)
        => BindingProjectExec.AssertMatchesInEveryEntryPoint(backend, probe);

    // ============================================================================================
    // D2 — If(cond, a, b), on every backend
    // ============================================================================================

    /// <summary>
    /// The conditional prints vbc's answer: only the chosen operand runs (i1side, i1sideB, i3nest, i10sc, i7guard — recursion,
    /// division by zero and a Nothing dereference must not run), the result type is the dominant one (i2types, i13obj), and it works
    /// in a lambda, an argument, a Select Case subject, a constructor, a getter, a For bound, and beside an Integer constant
    /// (i5lambda, i6arg, i9select, i11cls, i4forB, i8const, i12pong) — the two shapes Samples/Pong uses are i12pong.
    /// </summary>
    [TestCaseSource(nameof(ConditionalCells))]
    public void AConditionalExpression_PrintsVbsAnswer_InEveryEntryPoint(TempProbe probe, Bk backend)
        => TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id);
}
