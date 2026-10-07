using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.CodeGen.JavaScript;
using BasicLang.Compiler.CodeGen.MSIL;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  Task #172 — ADR-0014 (D1-D6, amended A1/A2): VB's per-iteration loop-body `Dim`, with
//  copy-forward, on every backend. Fix committed ef1e949b (D1-D6) + 4cdf2dd1 (A1/A2 amendment),
//  on branch claude/jolly-pasteur-l4mpzs, PR #137. Also fixes #226 (MSIL: an `Exit` inside a Try
//  that a wrapped loop encloses used to drag the loop's end block, and everything after it, into
//  the protected region — InvalidProgramException).
//
//  Every probe below is transcribed VERBATIM from the architect's own measurement probes
//  (scratchpad t172/probes, t172/edge, t172/licm, t172/licm2, t172/pb, t172/sc3) and every
//  expected value is the one the architect's ruling/amendment measured against VB (vbc) or, where
//  a backend is PINNED as known-wrong, the value THIS working tree was independently re-measured
//  to print (S/t172/m-r2c.txt — round 2, HEAD 4cdf2dd1) before being written here. A pin never
//  presents a wrong answer as VB-correct: every pinned assertion is documented as a pin, naming
//  the tracking task, per CLAUDE.md's rule for this backend.
//
//  Where a probe's outcome does not depend on the loop kind or the exact number chosen, this file
//  reuses the architect's own letter (L1-L8, E01-E20 and variants, K1-K5, P226*) rather than
//  renaming it — the letter is how the ADR and the ruling/amendment documents refer to it, and
//  keeping the name lets a reader cross-reference the ADR directly.
//
//  PIN MAP (every non-VB cell asserted below, and why):
//    C++  (none since #140 — ADR-0019)
//         C++ used to lose every write through a captured variable (capture by copy), so L7, L8,
//         L8b, cl, E03, E04, E07f, E12, E17 and E18 were pinned here as a wrong answer (#172), then
//         as a refusal by name (#170's W2). #140 sends every C++ root through ClosureLowering with
//         its per-iteration environments (ADR-0014 D5 / ADR-0010), so they run on C++ and print what
//         the other backends print: L7, E03, E04, E07f, E12, E17, E18 and cl print VB's answer, and
//         L8 / L8b print D2's recorded divergence (11|21|31, as JavaScript and MSIL do).
//    C#   (none since #136: L8(edge), cl(sc3) and E08 were pinned here as the C# backend's own
//         pre-existing multi-statement-lambda-body defect; a lambda body is written by the
//         function-body emitter now, cl and E08 print VB's answer and L8 prints D2's recorded
//         divergence, 11|21|31, like the other three backends.)
//    C#   E07w, P226b                                                          -> #227
//    E15, E15n                                                                  -> #228
//         (a sized array `Dim a(2)` in a loop body is not an IR instruction — every backend
//         allocates it once, at function top, with or without a lambda; ADR-0014 changes nothing
//         there. Recorded in the ADR's implementation notes as a known, separately-tracked gap.)
//    E16, E20                                                                   -> #229 (E16 on all four backends since #140; E20 on C# prints
//                                                                                  JavaScript's 50|2|2 since #136: the h() line is right, the loop's y is not)
//         (the one-declaration rule: a name with two `Dim`s in one function — sibling loops for
//         E16, a lambda's OWN local of the same name for E20 — stays function-level rather than
//         per-iteration, by construction, so it keeps its pre-#172 behaviour instead of taking
//         one loop's per-iteration identity away from the other declaration.)
//    MSIL K1, K2   (none since #225: they were pinned here as ILASM-FAIL — the MSIL backend
//         emitted `newarr [mscorlib]System.Func`1<int32>` for an array of a generic delegate,
//         without the `class` a generic token needs. Both now run on MSIL and print VB's answer,
//         through both pipelines; see K1_Msil_… / K2_Msil_… below.)
//    MSIL E20                                                                   pre-existing, N9
//         (ClosureLoweringRefusalTests' own D9 backstop: a lambda that declares its own local of
//         a name an ENCLOSING scope also captures is refused by the front end today — #155/
//         ADR-0010, unrelated to #172's mechanism; E20's lambda `h` is not even created inside
//         the loop ADR-0014 governs.)
// =====================================================================================

/// <summary>Every BASIC source this file runs, named after the architect's own probe letters.
/// See the scratchpad paths named on each group below for where a probe's bytes come from.</summary>
internal static class PerIterationLoopBodyDimProbes
{
    // ---- t172/probes: L1-L7 (the ADR's own measured table) --------------------------------

    internal const string L1 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim x As Integer = i * 10
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string L1Expected = "10\n20\n30";

    internal const string L2 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim x As Integer
                x = x + i
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string L2Expected = "1\n3\n6";

    /// <summary>The D6 byte-identity probe: identical to L2 but with no lambda at all.</summary>
    internal const string L3 = """
        Sub Main()
            For i As Integer = 1 To 3
                Dim x As Integer
                x = x + i
                Console.WriteLine(x)
            Next
        End Sub
        """;
    internal const string L3Expected = "1\n3\n6";

    internal const string L4 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            Dim i As Integer = 0
            While i < 3
                i = i + 1
                Dim x As Integer = i + 100
                fs.Add(Function() x)
            End While
            Dim k As Integer = 0
            Do While k < 2
                k = k + 1
                Dim y As Integer = k * 7
                fs.Add(Function() y)
            Loop
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string L4Expected = "101\n102\n103\n7\n14";

    internal const string L5 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim a As Integer = i
                For j As Integer = 1 To 2
                    Dim b As Integer = j * 10
                    fs.Add(Function() a + b)
                Next
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string L5Expected = "11\n21\n12\n22";

    internal const string L6 = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(4)
            items.Add(5)
            Dim fs As New List(Of Func(Of Integer))()
            For Each n As Integer In items
                Dim sq As Integer = n * n
                fs.Add(Function() sq)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string L6Expected = "16\n25";

    /// <summary>A lambda WRITES the iteration-1 variable after the loop. C++'s capture-by-copy
    /// loses the write (#140, pre-existing — unrelated to #172, unfixed until #140).</summary>
    internal const string L7 = """
        Sub Main()
            Dim acts As New List(Of Action)()
            Dim gets As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim c As Integer = i
                acts.Add(Sub() c = c + 100)
                gets.Add(Function() c)
            Next
            Dim a0 As Action = acts(0)
            a0()
            For k As Integer = 0 To gets.Count - 1
                Dim g As Func(Of Integer) = gets(k)
                Console.WriteLine(g())
            Next
        End Sub
        """;
    internal const string L7Expected = "101\n2";
    internal const string L7CppActual = "1\n2";

    // ---- t172/edge: E01-E20 (+ E07 variants) -----------------------------------------------

    internal const string E01 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 4
                If i Mod 2 = 0 Then
                    Dim x As Integer = i * 10
                    fs.Add(Function() x)
                End If
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E01Expected = "20\n40";

    internal const string E02 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim x As Integer = x + 1
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E02Expected = "1\n2\n3";

    /// <summary>A user Finally writes x, no Exit at all. C++ loses it (#140).</summary>
    internal const string E03 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim x As Integer
                Try
                    x = x + i
                    fs.Add(Function() x)
                Finally
                    x = x * 10
                End Try
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E03Expected = "10\n120\n1230";
    internal const string E03CppActual = "1\n12\n123";

    /// <summary>Exit For from inside a user Try/Finally that writes x. C++ loses it (#140).</summary>
    internal const string E04 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim x As Integer
                Try
                    x = x + i
                    fs.Add(Function() x)
                    If i = 2 Then Exit For
                Finally
                    x = x * 10
                End Try
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E04Expected = "10\n120";
    internal const string E04CppActual = "1\n12";

    /// <summary>Required group 3: "an inner Exit" — Exit For out of a nested For does not disturb
    /// the enclosing While's own body-local capture.</summary>
    internal const string E05 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            Dim i As Integer = 0
            While i < 3
                i = i + 1
                Dim w As Integer
                w = w + i
                For j As Integer = 1 To 5
                    If j = 2 Then Exit For
                    w = w + 100
                Next
                fs.Add(Function() w)
            End While
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E05Expected = "101\n203\n306";

    /// <summary>Required group 3: "Exit mid-body" — the loop never re-enters after the Exit.</summary>
    internal const string E06 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 5
                Dim x As Integer
                x = x + i
                fs.Add(Function() x)
                If i = 3 Then Exit For
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E06Expected = "1\n3\n6";

    /// <summary>THE A1 headline probe: Exit For out of the inner loop, the OUTER loop re-enters
    /// it. Before A1 (D2's original "no carrier write on Exit") this printed 1 2 2 3 on C#,
    /// JavaScript and MSIL — the next entry copied forward from the last iteration that completed
    /// NORMALLY, not the one the Exit actually left. VB (vbc) prints 1 2 3 4.</summary>
    internal const string E07 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For r As Integer = 1 To 2
                For i As Integer = 1 To 3
                    Dim x As Integer
                    x = x + 1
                    fs.Add(Function() x)
                    If i = 2 Then Exit For
                Next
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E07Expected = "1\n2\n3\n4";

    /// <summary>E07's own shape with no lambda at all — the plain, always-correct control flow,
    /// kept as a sanity anchor that E07's 1 2 3 4 is not an accident of how many times the print
    /// loop runs.</summary>
    internal const string E07n = """
        Sub Main()
            For r As Integer = 1 To 2
                For i As Integer = 1 To 3
                    Dim x As Integer
                    x = x + 1
                    Console.WriteLine(x)
                    If i = 2 Then Exit For
                Next
            Next
        End Sub
        """;
    internal const string E07nExpected = "1\n2\n3\n4";

    /// <summary>E07's own shape, For Each instead of a counted For.</summary>
    internal const string E07e = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(1)
            items.Add(2)
            items.Add(3)
            Dim fs As New List(Of Func(Of Integer))()
            For r As Integer = 1 To 2
                For Each n As Integer In items
                    Dim x As Integer
                    x = x + n
                    fs.Add(Function() x)
                    If n = 2 Then Exit For
                Next
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E07eExpected = "1\n3\n4\n6";

    /// <summary>E07's own shape with an inner Do wrapped by a user Try/Finally that writes x —
    /// the carrier write must happen AFTER the user Finally, in nesting order.</summary>
    internal const string E07f = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For r As Integer = 1 To 2
                For i As Integer = 1 To 3
                    Dim x As Integer
                    Try
                        x = x + 1
                        fs.Add(Function() x)
                        If i = 2 Then Exit For
                    Finally
                        x = x + 100
                    End Try
                Next
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E07fExpected = "101\n202\n303\n404";
    internal const string E07fCppActual = "1\n102\n203\n304";

    /// <summary>E07's own shape, `Exit Do` out of an inner Do, the OUTER `While` re-enters —
    /// still A1's fix, not D3's loop-kind coverage: C# alone stays known-wrong (#227).</summary>
    internal const string E07w = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            Dim r As Integer = 0
            While r < 2
                r = r + 1
                Dim n As Integer = 0
                Do
                    n = n + 1
                    Dim x As Integer
                    x = x + 1
                    fs.Add(Function() x)
                    If n = 2 Then Exit Do
                Loop
            End While
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E07wExpected = "1\n2\n3\n4";
    internal const string E07wCSharpActual = "1\n2";

    /// <summary>E07's own shape via the EXCEPTION route: the loop is left by a thrown exception
    /// (not an Exit), caught by an enclosing Try, and the outer loop re-enters — A1's own "the
    /// exception route comes free" claim.</summary>
    internal const string E07x = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For r As Integer = 1 To 2
                Try
                    For i As Integer = 1 To 3
                        Dim x As Integer
                        x = x + 1
                        fs.Add(Function() x)
                        If i = 2 Then Throw New Exception("boom")
                    Next
                Catch ex As Exception
                    Console.WriteLine("caught")
                End Try
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E07xExpected = "caught\ncaught\n1\n2\n3\n4";

    /// <summary>A lambda inside a lambda's OWN loop — the lambda's `Dim` belongs to the LAMBDA's
    /// loop, never the creator's (D3's last bullet). On C# alone it was CS1643 until #136: a
    /// `Function(n)` body with a loop and a trailing `Return` lost every block after its entry
    /// block, so Roslyn refused "not all code paths return a value". It prints VB's answer on every
    /// backend now.</summary>
    internal const string E08 = """
        Sub Main()
            Dim outer As Func(Of Integer, Integer) = Function(n As Integer)
                Dim fs As New List(Of Func(Of Integer))()
                For i As Integer = 1 To n
                    Dim x As Integer = i * 100
                    fs.Add(Function() x)
                Next
                Dim total As Integer = 0
                For k As Integer = 0 To fs.Count - 1
                    Dim f As Func(Of Integer) = fs(k)
                    total = total * 1000 + f()
                Next
                Return total
            End Function
            Console.WriteLine(outer(2))
        End Sub
        """;
    internal const string E08Expected = "100200";

    /// <summary>D5: a For Each's declaring variable AND a body Dim are BOTH captured — the ADR
    /// requires ONE shared per-iteration environment, never two (the M9 mutant shape).</summary>
    internal const string E09 = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(2)
            items.Add(3)
            Dim fs As New List(Of Func(Of Integer))()
            For Each n As Integer In items
                Dim sq As Integer = n * n
                fs.Add(Function() n * 1000 + sq)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E09Expected = "2004\n3009";

    internal const string E10 = """
        Sub Main()
            For i As Integer = 1 To 3
                Dim x As Integer
                x = x + i
                Dim g As Func(Of Integer) = Function() x * 2
                Console.WriteLine(g())
            Next
        End Sub
        """;
    internal const string E10Expected = "2\n6\n12";

    /// <summary>A reference-typed body local (`As New List`) — each iteration's captured
    /// reference is a DIFFERENT list object, so the count/first-element pair is per-iteration
    /// too, not just an Integer.</summary>
    internal const string E11 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim a As New List(Of Integer)()
                a.Add(i)
                fs.Add(Function() a.Count * 10 + a(0))
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E11Expected = "11\n12\n13";

    /// <summary>NOT a body Dim — the counted For's OWN control variable, ADR-0010 L14's "one
    /// binding for the whole loop" territory, unaffected by #172. C++ stays wrong for the SAME
    /// pre-existing reason L7 does (#140).</summary>
    internal const string E12 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                fs.Add(Function() i)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E12Expected = "4\n4\n4";
    internal const string E12CppActual = "1\n2\n3";

    /// <summary>Both post-conditioned Do forms (`Loop While` / `Loop Until`) in one program.</summary>
    internal const string E13 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            Dim c As Integer = 0
            Do
                c = c + 1
                Dim y As Integer
                y = y + c
                fs.Add(Function() y)
            Loop While c < 3
            Dim m As Integer = 0
            Do
                m = m + 1
                Dim z As Integer = m * 3
                fs.Add(Function() z)
            Loop Until m >= 2
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E13Expected = "1\n3\n6\n3\n6";

    /// <summary>A For Each nested inside a counted For — BOTH levels' body Dims captured
    /// together in one closure (D5's chain: function env <- outer iteration env <- inner
    /// iteration env).</summary>
    internal const string E14 = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(1)
            items.Add(2)
            Dim fs As New List(Of Func(Of Integer))()
            For r As Integer = 1 To 2
                Dim a As Integer = r * 100
                For Each n As Integer In items
                    Dim b As Integer = n * 10
                    fs.Add(Function() a + b + n)
                Next
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E14Expected = "111\n122\n211\n222";

    /// <summary>#228 (recorded, not fixed here): a SIZED array's `Dim a(2)` is never an IR
    /// instruction — every backend allocates it once at function top, so the same array is
    /// shared, and REFERENCED, across every iteration's closure. VB re-creates the array fresh
    /// each iteration (1, 2, 3); every backend here prints the array's value AFTER the loop ends
    /// (6, 6, 6) instead, on all four backends identically.</summary>
    internal const string E15 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim a(2) As Integer
                a(0) = a(0) + i
                fs.Add(Function() a(0))
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E15Expected = "1\n2\n3";
    internal const string E15ActualAllBackends = "6\n6\n6";

    /// <summary>E15's own shape with no lambda — proves the divergence is NOT about capture at
    /// all: even with nothing to capture it, the array is still one function-level allocation, so
    /// VB's fresh-array-per-iteration (1, 2, 3) still diverges from every backend's accumulating
    /// (1, 3, 6).</summary>
    internal const string E15n = """
        Sub Main()
            For i As Integer = 1 To 3
                Dim a(2) As Integer
                a(0) = a(0) + i
                Console.WriteLine(a(0))
            Next
        End Sub
        """;
    internal const string E15nExpected = "1\n2\n3";
    internal const string E15nActualAllBackends = "1\n3\n6";

    /// <summary>#229 (the one-declaration rule): TWO SIBLING loops each declare their own `Dim x`
    /// — the IR is flat and every backend spells both by the same NAME, so `x` has two
    /// declarations in one function and stays function-level rather than taking one loop's
/// per-iteration identity away from the other. C++ did not even compile it before #140 (it declared
    /// every local at function top by name regardless of BodyLocals, and two locals spelled `x` at one
    /// scope is a plain C++ redefinition — the SAME one-name-one-declaration fact, a compile error instead
    /// of a wrong value). Since #140 C++ runs it and prints the SAME wrong answer as the other three.</summary>
    internal const string E16 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim x As Integer = i
                fs.Add(Function() x)
            Next
            For j As Integer = 1 To 2
                Dim x As Integer = j * 10
                fs.Add(Function() x)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E16Expected = "1\n2\n10\n20";
    internal const string E16ActualAllBackends = "20\n20\n20\n20";

    /// <summary>L7's own shape but SUB, not Function — a lambda writes the previous iteration's
    /// variable through a mutating Action. C++ loses the write (#140).</summary>
    internal const string E17 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            Dim bumps As New List(Of Action)()
            For i As Integer = 1 To 3
                Dim c As Integer
                c = c + 1
                bumps.Add(Sub() c = c + 1000)
                fs.Add(Function() c)
            Next
            Dim b1 As Action = bumps(1)
            b1()
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E17Expected = "1\n1002\n3";
    internal const string E17CppActual = "1\n2\n3";

    /// <summary>The BareCallNames extension of the capture rule (IRLoops.IsCaptured): `g` is a
    /// per-iteration Func, called BY NAME (`g()`) from a SECOND lambda `fs.Add(Function() g() +
    /// i)`, so `g`'s own per-iteration `m` must be captured even though nothing calls
    /// `Function() m * 7` directly. C++ loses it (#140).</summary>
    internal const string E18 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 3
                Dim m As Integer = i
                Dim g As Func(Of Integer) = Function() m * 7
                fs.Add(Function() g() + i)
            Next
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E18Expected = "11\n18\n25";
    internal const string E18CppActual = "8\n16\n24";

    /// <summary>A CLASS method's own loop-body Dim, alongside a private field read through the
    /// implicit `Me` every lambda in a class method captures.</summary>
    internal const string E19 = """
        Class Box
            Private _k As Integer = 5
            Public Function Make() As List(Of Func(Of Integer))
                Dim fs As New List(Of Func(Of Integer))()
                For i As Integer = 1 To 3
                    Dim x As Integer = i * _k
                    fs.Add(Function() x + _k)
                Next
                Return fs
            End Function
        End Class

        Sub Main()
            Dim b As New Box()
            Dim fs As List(Of Func(Of Integer)) = b.Make()
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E19Expected = "10\n15\n20";

    /// <summary>#229 again, the LAMBDA-owns-the-name half of the one-declaration rule: a SECOND,
    /// unrelated lambda `h` (created OUTSIDE any loop) declares its OWN local `y`, the same
    /// spelling the loop's captured body Dim `y` uses. `IRBuilder.AssignBodyLocals` excludes any
    /// name a lambda the function creates declares as ITS OWN local (the C# backend does not
    /// declare a lambda's locals — they bind to the creator's spelling), so the loop's `y` stays
    /// function-level. On MSIL the front end refuses the program outright before #172 or after it
    /// (a pre-existing D9 backstop, task #155/ADR-0010's own N9: "a lambda declares its own name
    /// while an enclosing scope's same name is captured" — `h`'s `y` and the loop's captured `y`
    /// collide the same way regardless of #172).</summary>
    internal const string E20 = """
        Sub Main()
            Dim fs As New List(Of Func(Of Integer))()
            For i As Integer = 1 To 2
                Dim y As Integer = i
                fs.Add(Function() y)
            Next
            Dim h As Func(Of Integer) = Function()
                    Dim y As Integer = 50
                    Console.Write("")
                    Return y
                End Function
            Console.WriteLine(h())
            For k As Integer = 0 To fs.Count - 1
                Dim f As Func(Of Integer) = fs(k)
                Console.WriteLine(f())
            Next
        End Sub
        """;
    internal const string E20Expected = "50\n1\n2";
    /// <summary>What C# and JavaScript print (known-wrong, #229). C# printed 2|2|2 until #136: `h()`'s 50 is right now, the loop's
    /// per-iteration `y` (1, 2) is still one shared variable.</summary>
    internal const string E20CSharpJsActual = "50\n2\n2";
    internal const string E20CppActual = "50\n1\n2";

    /// <summary>Required group 7 (D2's own recorded divergence, all backends): `f`, the loop's
    /// own condition, is REASSIGNED inside the body to a NEW lambda that writes the previous
    /// iteration's `x` — D2's timing rule (copy-forward snapshots at the CONTINUE TARGET, not
    /// exact-at-start) means the loop's OWN condition call sees last iteration's carrier before
    /// the write from `f`'s SECOND incarnation lands. VB (vbc) prints 11 22 33; JavaScript and
    /// MSIL agree on 11 21 31 (the deliberate, all-backend, D2 divergence: this is NEVER asserted
    /// as VB-correct — it is documented as the divergence D2 itself records). C# too, since #136:
    /// it printed 10 20 30 before, the lambda `f`'s write to `x` being dropped from its body, and
    /// reaches the divergence now.</summary>
    internal const string L8 = """
        Sub Main()
            Dim gets As New List(Of Func(Of Integer))()
            Dim n As Integer = 0
            Dim f As Func(Of Boolean) = Function() True
            Do While f()
                n = n + 1
                Dim x As Integer
                x = x + 10
                gets.Add(Function() x)
                f = Function()
                        x = x + 1
                        Return n < 3
                    End Function
            Loop
            For k As Integer = 0 To gets.Count - 1
                Dim g As Func(Of Integer) = gets(k)
                Console.WriteLine(g())
            Next
        End Sub
        """;
    internal const string L8VbExpected = "11\n22\n33";
    /// <summary>D2's recorded output, printed by C#, C++, JavaScript and MSIL alike (vbc prints <see cref="L8VbExpected"/>).</summary>
    internal const string L8D2Actual = "11\n21\n31";

    // ---- t172/licm + t172/licm2: K1-K5 (A2's own optimizer probes) -------------------------

    /// <summary>A captured loop-body local READ but never called through — before A2, LICM
    /// hoisted the pure read `x` out of the loop (no call in the body), and JavaScript's `-O`
    /// then threw `ReferenceError: x is not defined` because the hoisted read ran before any
    /// iteration declared `x`. Its array of `Func(Of Integer)` did not assemble on MSIL until #225
    /// (`newarr Func\`1&lt;int32&gt;` without `class`); it runs there now.</summary>
    internal const string K1 = """
        Sub Main()
            Dim fs(2) As Func(Of Integer)
            Dim s As Integer = 0
            For i As Integer = 0 To 2
                Dim x As Integer
                fs(i) = Function() x
                s = s + x * 2
            Next
            Console.WriteLine(s)
            Dim f As Func(Of Integer) = fs(1)
            Console.WriteLine(f())
        End Sub
        """;
    internal const string K1Expected = "0\n0";

    /// <summary>K1's array of `Func(Of Integer)` on a `While` loop (on MSIL since #225, like K1).</summary>
    internal const string K2 = """
        Sub Main()
            Dim fs(2) As Func(Of Integer)
            Dim s As Integer = 0
            Dim i As Integer = 0
            While i < 3
                Dim x As Integer
                Dim y As Integer = x + 5
                fs(i) = Function() x + y
                s = s + y
                i = i + 1
            End While
            Console.WriteLine(s)
            Dim f As Func(Of Integer) = fs(1)
            Console.WriteLine(f())
        End Sub
        """;
    internal const string K2Expected = "15\n5";

    /// <summary>K1's own shape with a `List(Of Func)` instead of a fixed array — no MSIL
    /// array-of-delegate gap, so all four backends run and agree.</summary>
    internal const string K3 = """
        Sub Main()
            Dim last As Func(Of Integer) = Nothing
            Dim s As Integer = 0
            For i As Integer = 0 To 2
                Dim x As Integer
                last = Function() x
                s = s + x * 2
            Next
            Console.WriteLine(s)
            Console.WriteLine(last())
        End Sub
        """;
    internal const string K3Expected = "0\n0";

    /// <summary>A per-iteration local that IS assigned a real value (not just default 0) and is
    /// read both inside the loop and by the surviving closure — proves LICM's write-at-body-entry
    /// rule does not over-conservatively break a genuinely correct hoist candidate elsewhere.</summary>
    internal const string K4 = """
        Sub Main()
            Dim last As Func(Of Integer) = Nothing
            Dim y As Integer = 0
            For i As Integer = 0 To 2
                Dim x As Integer = i * 3
                last = Function() x
                y = x
            Next
            Console.WriteLine(y)
            Console.WriteLine(last())
        End Sub
        """;
    internal const string K4Expected = "6\n6";

    /// <summary>K1's own shape on a `Do While` loop instead of a counted `For`.</summary>
    internal const string K5 = """
        Sub Main()
            Dim last As Func(Of Integer) = Nothing
            Dim s As Integer = 0
            Dim i As Integer = 0
            Do While i < 3
                Dim x As Integer
                last = Function() x + 1
                s = s + (x + 7)
                i = i + 1
            Loop
            Console.WriteLine(s)
            Console.WriteLine(last())
        End Sub
        """;
    internal const string K5Expected = "21\n1";

    // ---- t172/sc3: `cl` (no loop at all) and `L8b` (L8 with the SAME divergence, no cascading
    //      reassignment of `f` itself) -------------------------------------------------------

    /// <summary>NOT a loop-body Dim at all — a plain function-top local captured and WRITTEN by
    /// a lambda with no loop anywhere in the program. Included as the non-loop control for #136's
    /// C# pin on L8/E17/E18 above: the SAME write-capture defect reproduced on C# with no loop, no
    /// per-iteration mechanism, and no ADR-0014 machinery involved at all (5 0 for 5 1). C++ printed the same wrong
    /// answer until #140, then was refused by name (#170), and prints VB's 5 1 since #140; C# prints it since #136.</summary>
    internal const string Cl = """
        Sub Main()
            Dim x As Integer = 0
            Dim f As Func(Of Integer) = Function()
                    x = x + 1
                    Return 5
                End Function
            Console.WriteLine(f())
            Console.WriteLine(x)
        End Sub
        """;
    internal const string ClExpected = "5\n1";

    /// <summary>Required group 7's own second half: "L8b agrees across C#, JS and MSIL." Same
    /// shape as L8 but `f`'s SECOND incarnation also prints a blank line (`Console.Write("")`),
    /// which changes nothing about the printed numbers — it is here only because it is the exact
    /// probe the architect's own D2 divergence note names alongside L8.</summary>
    internal const string L8b = """
        Sub Main()
            Dim gets As New List(Of Func(Of Integer))()
            Dim n As Integer = 0
            Dim f As Func(Of Boolean) = Function() True
            Do While f()
                n = n + 1
                Dim x As Integer
                x = x + 10
                gets.Add(Function() x)
                f = Function()
                        x = x + 1
                        Console.Write("")
                        Return n < 3
                    End Function
            Loop
            For k As Integer = 0 To gets.Count - 1
                Dim g As Func(Of Integer) = gets(k)
                Console.WriteLine(g())
            Next
        End Sub
        """;
    internal const string L8bExpected = "11\n21\n31";
}

// =====================================================================================
//  Group 1/3/7 — VB per-iteration identity, Exit/re-entry (A1), and the D2 divergence, run for
//  REAL on all four backends (CLI entry point) plus, for a representative headline subset, the
//  project-build entry point (`CompileProjectFiles`, the IDE's own path).
// =====================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class PerIterationLoopBodyDimExecutionTests
{
    // ---- Shared plumbing -------------------------------------------------------------------

    private static void AssertAllFourAgree(string source, string expected) =>
        FourBackends.RunsOnEveryBackend(source, expected);

    private static void AssertAllFourAgreeAggressive(string source, string expected) =>
        FourBackends.RunsOnEveryBackendAggressive(source, expected);

    /// <summary>A probe where every backend RUNS, but one or more print a pinned, KNOWN-WRONG
    /// value instead of VB's own — never asserted as though it were correct. Passing null for a
    /// backend means "agrees with <paramref name="vb"/>".</summary>
    private static void AssertWithPins(string source, string vb,
        string csharp = null, string cpp = null, string js = null, string msil = null) =>
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(csharp ?? vb),
                csharp != null ? "C# (PINNED known-wrong)" : "C#");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(cpp ?? vb),
                cpp != null ? "C++ (PINNED known-wrong, task #140)" : "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(js ?? vb),
                js != null ? "JavaScript (PINNED known-wrong)" : "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(msil ?? vb),
                msil != null ? "MSIL (PINNED known-wrong)" : "MSIL");
        });

    /// <summary>
    /// ⭐ #140: every probe that used to be REFUSED BY NAME on C++ (ADR-0016 D3/W2) now runs, and prints
    /// what C#, JavaScript and MSIL print. All four backends agree with <paramref name="vb"/>, and the C++
    /// root (<paramref name="cppRoot"/>) took the LOWERED path — a program that runs right on the by-copy
    /// fallback would pass the output check alone, which is exactly how a lowering regression would hide.
    /// </summary>
    private static void AssertAllFourAgree_CppLowered(string source, string vb, string cppRoot = "Main")
    {
        Assert.That(CppClosures.Compile(source).PathOf(cppRoot), Is.EqualTo(CppClosurePath.Lowered),
            $"C++ root '{cppRoot}' must take the lowered path");
        FourBackends.RunsOnEveryBackend(source, vb);
    }

    /// <summary>
    /// The project-build entry point (<c>BasicCompiler.CompileProjectFiles</c>, aggressive
    /// options — what the CLI's own <c>-c Release</c> and the IDE's build service both use) as
    /// opposed to the single-file <c>CompileFile</c> path every helper above goes through. Mirrors
    /// <c>NameBindingExecutionTests.RunViaProjectEntryPoint</c> (task #169's own convention) —
    /// kept as its own private copy per CLAUDE.md's "change shared source once" rule not applying
    /// here: this is TEST plumbing, not production source shared across consumers.
    /// </summary>
    private static string RunViaProjectEntryPoint(string backend, string source)
    {
        var compiler = new BasicCompiler(new CompilerOptions { OptimizeAggressive = true });
        var dir = Path.Combine(Path.GetTempPath(), "bl-t172-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);

            var result = compiler.CompileProjectFiles(new List<string> { path });
            Assert.That(result.HasErrors, Is.False,
                string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            Assert.That(result.CombinedIR, Is.Not.Null, "the project entry point produced no combined IR");
            var ir = result.CombinedIR;

            return backend switch
            {
                "csharp" => FourBackends.RunEmittedCSharpText(new ImprovedCSharpCodeGenerator().Generate(ir)),
                "cpp" => BclE2E.CompileRun(
                    new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(ir)),
                "javascript" => JavaScriptExecutionTests.RunNodeScript(
                    new JavaScriptCodeGenerator().Generate(ir)),
                "msil" => MsilHarness.RunIlExpectingSuccess(new MSILCodeGenerator().Generate(ir), "T"),
                _ => throw new ArgumentException("unknown backend " + backend),
            };
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static void AssertProjectEntryPointAgrees(string source, string expected)
    {
        Assert.That(FourBackends.Norm(RunViaProjectEntryPoint("msil", source)), Is.EqualTo(expected),
            "Release .blproj leg (MSIL, CompileProjectFiles)");
        Assert.That(FourBackends.Norm(RunViaProjectEntryPoint("csharp", source)), Is.EqualTo(expected),
            "Release .blproj leg (C#, CompileProjectFiles)");
    }

    // ============================================================================================
    // Group 1: VB per-iteration identity — L1, L2, L4 (While and Do), L5 (nested), L6 (For Each).
    // L7 (a lambda writes) is its own test below: C++ is pinned #140.
    // Bonus D3/D4 coverage from t172/edge, all four backends agreeing with VB: E01 (through an
    // If), E02 (self-initializing Dim), E05 (an inner Exit that does not disturb the outer
    // capture), E06 (Exit mid-body, no re-entry), E07/E07e/E07n/E07x (A1's Exit-and-re-entry
    // family — For, For Each, no-lambda control, and the exception route), E09 (D5's shared
    // environment), E10, E11 (a reference-typed body local), E13 (both post-conditioned Do
    // forms), E14 (For Each nested in For), E19 (a class method + Me).
    // ============================================================================================

    private static readonly (string Name, string Source, string Expected)[] HeadlineAgreeingProbes =
    {
        ("L1", PerIterationLoopBodyDimProbes.L1, PerIterationLoopBodyDimProbes.L1Expected),
        ("L2", PerIterationLoopBodyDimProbes.L2, PerIterationLoopBodyDimProbes.L2Expected),
        ("L4_WhileAndDo", PerIterationLoopBodyDimProbes.L4, PerIterationLoopBodyDimProbes.L4Expected),
        ("L5_Nested", PerIterationLoopBodyDimProbes.L5, PerIterationLoopBodyDimProbes.L5Expected),
        ("L6_ForEach", PerIterationLoopBodyDimProbes.L6, PerIterationLoopBodyDimProbes.L6Expected),
        ("E01_ThroughAnIf", PerIterationLoopBodyDimProbes.E01, PerIterationLoopBodyDimProbes.E01Expected),
        ("E02_SelfInitializingDim", PerIterationLoopBodyDimProbes.E02, PerIterationLoopBodyDimProbes.E02Expected),
        ("E05_InnerExitDoesNotDisturbOuterCapture", PerIterationLoopBodyDimProbes.E05, PerIterationLoopBodyDimProbes.E05Expected),
        ("E06_ExitMidBodyNoReentry", PerIterationLoopBodyDimProbes.E06, PerIterationLoopBodyDimProbes.E06Expected),
        ("E07_ExitAndReenterOuterLoop", PerIterationLoopBodyDimProbes.E07, PerIterationLoopBodyDimProbes.E07Expected),
        ("E07e_ForEachExitAndReenter", PerIterationLoopBodyDimProbes.E07e, PerIterationLoopBodyDimProbes.E07eExpected),
        ("E07n_ExitAndReenter_NoLambdaControl", PerIterationLoopBodyDimProbes.E07n, PerIterationLoopBodyDimProbes.E07nExpected),
        ("E07x_ExceptionRouteAndReenter", PerIterationLoopBodyDimProbes.E07x, PerIterationLoopBodyDimProbes.E07xExpected),
        ("E09_ForEachElementAndBodyLocalShareOneEnvironment", PerIterationLoopBodyDimProbes.E09, PerIterationLoopBodyDimProbes.E09Expected),
        ("E10_SameIterationRead", PerIterationLoopBodyDimProbes.E10, PerIterationLoopBodyDimProbes.E10Expected),
        ("E11_ReferenceTypedBodyLocal", PerIterationLoopBodyDimProbes.E11, PerIterationLoopBodyDimProbes.E11Expected),
        ("E13_BothPostConditionedDoForms", PerIterationLoopBodyDimProbes.E13, PerIterationLoopBodyDimProbes.E13Expected),
        ("E14_ForEachNestedInFor", PerIterationLoopBodyDimProbes.E14, PerIterationLoopBodyDimProbes.E14Expected),
        ("E19_ClassMethodFieldAndPerIterationLocal", PerIterationLoopBodyDimProbes.E19, PerIterationLoopBodyDimProbes.E19Expected),
    };

    private static IEnumerable<TestCaseData> HeadlineStandardCases() =>
        HeadlineAgreeingProbes.Select(p => new TestCaseData(p.Source, p.Expected).SetName(p.Name + "_StandardPipeline_AllFourBackendsAgree"));

    private static IEnumerable<TestCaseData> HeadlineAggressiveCases() =>
        HeadlineAgreeingProbes.Select(p => new TestCaseData(p.Source, p.Expected).SetName(p.Name + "_AggressivePipeline_AllFourBackendsAgree"));

    [TestCaseSource(nameof(HeadlineStandardCases))]
    public void StandardPipeline_AllFourBackendsAgree(string source, string expected) => AssertAllFourAgree(source, expected);

    [TestCaseSource(nameof(HeadlineAggressiveCases))]
    public void AggressivePipeline_AllFourBackendsAgree(string source, string expected) => AssertAllFourAgreeAggressive(source, expected);

    // ---- L1 and E07, ALSO through the project-build entry point (CompileProjectFiles) --------

    [Test]
    public void L1_ThroughProjectEntryPoint()
        => AssertProjectEntryPointAgrees(PerIterationLoopBodyDimProbes.L1, PerIterationLoopBodyDimProbes.L1Expected);

    [Test]
    public void L2_ThroughProjectEntryPoint()
        => AssertProjectEntryPointAgrees(PerIterationLoopBodyDimProbes.L2, PerIterationLoopBodyDimProbes.L2Expected);

    [Test]
    public void L6_ForEach_ThroughProjectEntryPoint()
        => AssertProjectEntryPointAgrees(PerIterationLoopBodyDimProbes.L6, PerIterationLoopBodyDimProbes.L6Expected);

    [Test]
    public void E07_ExitAndReenterOuterLoop_ThroughProjectEntryPoint()
        => AssertProjectEntryPointAgrees(PerIterationLoopBodyDimProbes.E07, PerIterationLoopBodyDimProbes.E07Expected);

    [Test]
    public void E09_ForEachBoth_ThroughProjectEntryPoint()
        => AssertProjectEntryPointAgrees(PerIterationLoopBodyDimProbes.E09, PerIterationLoopBodyDimProbes.E09Expected);

    // ============================================================================================
    // L7 — a lambda WRITES the iteration-1 variable after the loop. C++ lost the write (capture by
    // copy), then refused the program by name (#170, ADR-0016 D3/W2); since #140 it runs, lowered, and
    // all four backends print VB's own answer.
    // ============================================================================================

    /// <summary>⭐ MOVED PIN (#140). USED TO print 1\n2 for 101\n2 (C++'s capture-by-copy losing
    /// acts(0)'s write to <c>c</c>), then to be refused by name (#170); now runs and prints 101\n2.</summary>
    [Test]
    public void L7_LambdaWritesPreviousIterationVariable_StandardPipeline()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.L7, PerIterationLoopBodyDimProbes.L7Expected);

    // ============================================================================================
    // Group 3: A1's Exit-and-carrier family, the pinned edge cases. E03, E04, E07f, E12, E17 and E18
    // were REFUSED BY NAME on C++ by #170's by-copy rule (ADR-0016 D3/W2: a Finally that runs after the
    // lambda is created for E03/E04/E07f, the For loop's own control-variable increment for E12/E18, a
    // second lambda that writes what it captures for E17). #140 lowers them, and they run on all four
    // backends; E07w stays #227 on C# only.
    // ============================================================================================

    /// <summary>⭐ MOVED PIN (#140). USED TO print 1\n12\n123 for 10\n120\n1230 (C++ losing the
    /// Finally's write to <c>x</c>), then to be refused by name; the lowered per-iteration environment
    /// writes the carrier back in its Finally, so C++ prints 10\n120\n1230.</summary>
    [Test]
    public void E03_UserFinallyWritesX_NoExit()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.E03, PerIterationLoopBodyDimProbes.E03Expected);

    /// <summary>⭐ MOVED PIN (#140). Same Try/Finally shape as E03, plus an Exit For — the #226 rule in
    /// <c>ComputeInlineRegion</c> keeps the exit out of the iteration's try.</summary>
    [Test]
    public void E04_ExitForInsideUserTryFinallyThatWritesX()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.E04, PerIterationLoopBodyDimProbes.E04Expected);

    /// <summary>⭐ MOVED PIN (#140). Nested loop, same Try/Finally shape one level deeper.</summary>
    [Test]
    public void E07f_ExitInsideUserTryFinally_OuterLoopReenters()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.E07f, PerIterationLoopBodyDimProbes.E07fExpected);

    /// <summary>Task #227: C# alone prints 1|2 instead of VB's 1|2|3|4 for this exact
    /// Do-inside-While Exit-and-re-entry shape — re-measure before touching (S/t172/m-r2c.txt).
    /// Unaffected by #170 — no lambda writes a captured variable anywhere in this probe.</summary>
    [Test]
    public void E07w_ExitDoInsideWhile_KnownWrongOnCSharp_PinnedForTask227()
        => AssertWithPins(PerIterationLoopBodyDimProbes.E07w, PerIterationLoopBodyDimProbes.E07wExpected,
            csharp: PerIterationLoopBodyDimProbes.E07wCSharpActual);

    /// <summary>⭐ MOVED PIN (#140). USED TO print 1\n2\n3 for 4\n4\n4: the counted For's OWN
    /// control variable <c>i</c> is not a body <c>Dim</c> (it is NOT in <c>BodyLocals</c>), so there is
    /// one <c>i</c> for the whole loop and every lambda sees its final value — C++ now shares it, as the
    /// other backends do (4\n4\n4). This is exactly the shape that distinguishes a counted For's control
    /// variable from a per-iteration body Dim (the title's own point).</summary>
    [Test]
    public void E12_CountedForControlVariable_NotABodyDim()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.E12, PerIterationLoopBodyDimProbes.E12Expected);

    /// <summary>⭐ MOVED PIN (#140). USED TO print 1\n2\n3 for 1\n1002\n3 (bumps(1)'s write to
    /// <c>c</c> lost); the <c>bumps</c> lambda writes <c>c</c> through the shared environment now.</summary>
    [Test]
    public void E17_LambdaWritesThroughAMutatingSub()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.E17, PerIterationLoopBodyDimProbes.E17Expected);

    /// <summary>⭐ MOVED PIN (#140). USED TO print 8\n16\n24 for 11\n18\n25 (<c>i</c>, the counted For's
    /// own control variable, is written by the loop's own increment — the same shape as E12).</summary>
    [Test]
    public void E18_DelegateCalledByNameFromASecondLambda()
        => AssertAllFourAgree_CppLowered(PerIterationLoopBodyDimProbes.E18, PerIterationLoopBodyDimProbes.E18Expected);

    /// <summary>
    /// ⭐ MOVED PIN (#136). CS1643 on C# — the emptied-multi-statement-lambda-body defect <c>MultiLineFunctionLambdaExecutionTests</c> F6
    /// used to pin, reached here through a lambda-in-a-lambda's own loop instead of an If/ElseIf chain (every block after the lambda's
    /// entry block was never written). The lambda body is written by the function-body emitter now, and C# prints 100200 like the other
    /// three backends. Hang-safe runner: the body holds two loops.
    /// </summary>
    [Test]
    public void E08_LambdaInLambdaLoop_RunsOnEveryBackend()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(PerIterationLoopBodyDimProbes.E08))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E08Expected), "C#");
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(PerIterationLoopBodyDimProbes.E08))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E08Expected), "C#, aggressive");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(PerIterationLoopBodyDimProbes.E08))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E08Expected), "C++");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(PerIterationLoopBodyDimProbes.E08)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E08Expected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimProbes.E08)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E08Expected), "MSIL");
        });
    }

    // ============================================================================================
    // #228 (recorded, not fixed by #172): a sized array Dim in a loop body never gets per-iteration
    // identity, with or without a lambda, on any backend.
    // ============================================================================================

    /// <summary>Task #228: every backend prints the array's post-loop value (6, 6, 6) where VB's
    /// own fresh-array-per-iteration prints 1, 2, 3 — recorded in ADR-0014's implementation notes
    /// as a known gap #172 does not close.</summary>
    [Test]
    public void E15_SizedArrayInLoopBody_KnownWrongOnAllBackends_PinnedForTask228()
        => AssertWithPins(PerIterationLoopBodyDimProbes.E15, PerIterationLoopBodyDimProbes.E15ActualAllBackends,
            csharp: PerIterationLoopBodyDimProbes.E15ActualAllBackends,
            cpp: PerIterationLoopBodyDimProbes.E15ActualAllBackends,
            js: PerIterationLoopBodyDimProbes.E15ActualAllBackends,
            msil: PerIterationLoopBodyDimProbes.E15ActualAllBackends);

    /// <summary>Task #228's own no-lambda control: even with nothing to capture the array, VB's
    /// fresh allocation per iteration still diverges from every backend's one shared array.</summary>
    [Test]
    public void E15n_SizedArrayNoLambda_KnownWrongOnAllBackends_PinnedForTask228()
        => AssertWithPins(PerIterationLoopBodyDimProbes.E15n, PerIterationLoopBodyDimProbes.E15nActualAllBackends,
            csharp: PerIterationLoopBodyDimProbes.E15nActualAllBackends,
            cpp: PerIterationLoopBodyDimProbes.E15nActualAllBackends,
            js: PerIterationLoopBodyDimProbes.E15nActualAllBackends,
            msil: PerIterationLoopBodyDimProbes.E15nActualAllBackends);

    // ============================================================================================
    // #229 (the one-declaration rule, by construction): E16 (sibling loops, same name) and E20
    // (a lambda's own local of the same name) both stay function-level rather than per-iteration.
    // ============================================================================================

    /// <summary>
    /// ⭐ Task #229, ONE test over all FOUR backends (#140 moved the C++ leg in here from two pins:
    /// <c>E16_SiblingSameName_CppDoesNotCompile</c> and BaseConstructorCallCppRefusalTests'
    /// <c>E16_StaysANamedClangFailure_NeitherRefusedNorRun</c>). C#, JavaScript, MSIL and, since #140, C++
    /// all print 20|20|20|20 — the SECOND loop's final value, function-level — where VB prints 1|2|10|20
    /// (<see cref="PerIterationLoopBodyDimProbes.E16Expected"/>, documented here and NEVER asserted as a
    /// backend's actual output): the one-declaration rule leaves a name declared twice in one function
    /// function-level (ADR-0014 D2 / IRBuilder.AssignBodyLocals). C++ used to fail clang on it (two sibling
    /// <c>x</c> locals are a C++ redefinition); lowering gives each its environment field, so it compiles
    /// and prints the #229 answer. When #229 is fixed the four rows flip TOGETHER, to
    /// <c>E16Expected</c>.
    ///
    /// <para><b>Owner decision (ADR-0019 D4, 2026-10-01): ADMIT.</b> C++ runs E16 and is pinned to the #229
    /// output with the other three backends; rule one is read per class of wrong answer, so fixing #229
    /// flips all four rows together. The rejected alternative (C++ refuses E16 by name until #229) is
    /// recorded in ADR-0019 D4.</para>
    /// </summary>
    [TestCase("csharp", TestName = "E16_SiblingLoopsSameName_KnownWrong_PinnedForTask229_csharp")]
    [TestCase("javascript", TestName = "E16_SiblingLoopsSameName_KnownWrong_PinnedForTask229_javascript")]
    [TestCase("msil", TestName = "E16_SiblingLoopsSameName_KnownWrong_PinnedForTask229_msil")]
    [TestCase("cpp", TestName = "E16_SiblingLoopsSameName_KnownWrong_PinnedForTask229_cpp")]
    public void E16_SiblingLoopsSameName_KnownWrongOnAllFourBackends_PinnedForTask229(string backend)
    {
        var source = PerIterationLoopBodyDimProbes.E16;
        var expected = PerIterationLoopBodyDimProbes.E16ActualAllBackends;
        switch (backend)
        {
            case "csharp":
                Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(source)), Is.EqualTo(expected), "C# (PINNED known-wrong, task #229)");
                break;
            case "javascript":
                Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), "JavaScript (PINNED known-wrong, task #229)");
                break;
            case "msil":
                Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), "MSIL (PINNED known-wrong, task #229)");
                break;
            case "cpp":
                // The owner may instead rule that C++ refuses this by name; flip ONLY this row.
                Assert.That(CppClosures.Compile(source).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
                CppClosures.RunsInAllModes(source, expected, "C++ (PINNED known-wrong, task #229)");
                break;
            default: throw new ArgumentException(backend);
        }
    }

    /// <summary>Task #229: C# and JavaScript print the loop's `y` as though it were never
    /// per-iteration (excluded because the unrelated lambda `h` declares its OWN `y`), where VB
    /// prints 50|1|2. ⭐ MOVED PIN (#136): C# printed 2|2|2 — `h()`'s own local `y = 50` was never declared, so
    /// `h()` read the loop's `y` (2) — and now prints 50|2|2, JavaScript's pinned answer. The `h()` line is right
    /// (vbc: 50); the loop's `y`, 2 for 1, is #229 and stays known-wrong. C++ runs it correctly: ClosureLowering refuses N9 (a lambda declares a name its creator
    /// also captures), so the root takes the by-copy FALLBACK, whose capture gives per-iteration behaviour
    /// for free — the fallback W2 admits it onto (#140 ruling D3; the regression fence runs it too, and
    /// asserts the path). MSIL refuses the program outright — a pre-existing #155/ADR-0010 front-end
    /// backstop (N9), unrelated to #172.</summary>
    [Test]
    public void E20_LambdaOwnLocalSameNameAsCapturedBodyDim_KnownWrongOnCSharpJs_PinnedForTask229()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(PerIterationLoopBodyDimProbes.E20))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E20CSharpJsActual), "C# (PINNED known-wrong, task #229)");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(PerIterationLoopBodyDimProbes.E20)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E20CSharpJsActual), "JavaScript (PINNED known-wrong, task #229)");
            Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(PerIterationLoopBodyDimProbes.E20))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.E20CppActual), "C++ (correct here, by the by-copy fallback)");
            Assert.That(CppClosures.Compile(PerIterationLoopBodyDimProbes.E20).PathOf("Main"), Is.EqualTo(CppClosurePath.ByCopy),
                "C++: the N9 shape is a lowering refusal that W2 admits — the fallback");
        });

    /// <summary>MSIL's own pre-existing D9/N9 refusal (#155/ADR-0010), unrelated to #172: `h`
    /// declares its own `y` while the loop's `y` is captured elsewhere in the same function.</summary>
    [Test]
    public void E20_LambdaOwnLocal_MsilRefusesToBuild_PreExisting()
    {
        var run = MsilHarness.Run(PerIterationLoopBodyDimProbes.E20);
        Assert.That(run.Outcome, Is.Not.EqualTo(MsilHarness.MsilOutcome.Ran),
            "expected MSIL to REFUSE this program (pre-existing N9 backstop) — if it now runs, " +
            "re-measure and update this pin");
    }

    // ============================================================================================
    // Group 7: L8's own D2 divergence (documented, never asserted as VB-correct on any backend)
    // and L8b (same output). `cl` is the non-loop control for #136/#140.
    // ============================================================================================

    /// <summary>
    /// ⭐ MOVED PIN (C++ leg #140, C# leg #136). D2's own recorded, deliberate, all-backend divergence. VB prints 11 22 33
    /// (<see cref="PerIterationLoopBodyDimProbes.L8VbExpected"/>, documented here — NEVER asserted as a
    /// backend's actual output). ALL FOUR backends agree with EACH OTHER on 11 21 31 (the divergence D2 records:
    /// the loop's own condition call observes the carrier's value at ITS continue-target snapshot, before the
    /// reassigned `f`'s own write lands) — so D2's revisit-if is ONE cross-backend assertion (ruling D4). C# was the
    /// outlier for a SEPARATE, pre-existing reason, #136 (its multi-statement lambda body defect: `f`'s write to
    /// `x` was dropped from its body, and it printed 10 20 30); it reaches the divergence now. C++ USED TO silently
    /// print 10 20 30 (task #140), then to be refused by name (#170).
    /// </summary>
    [Test]
    public void L8_DoWhileConditionReassignsToALambdaThatWritesX_D2DivergenceOnAllFourBackends()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(PerIterationLoopBodyDimProbes.L8)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8D2Actual),
                "JavaScript — D2's recorded divergence from VB's 11 22 33, not VB-correct");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimProbes.L8)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8D2Actual),
                "MSIL — D2's recorded divergence from VB's 11 22 33, not VB-correct");
            Assert.That(CppClosures.Compile(PerIterationLoopBodyDimProbes.L8).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
            CppClosures.RunsInAllModes(PerIterationLoopBodyDimProbes.L8, PerIterationLoopBodyDimProbes.L8D2Actual,
                "C++ — D2's recorded divergence from VB's 11 22 33, not VB-correct");
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(PerIterationLoopBodyDimProbes.L8))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8D2Actual), "C# — D2's recorded divergence from VB's 11 22 33, not VB-correct (since #136)");
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(PerIterationLoopBodyDimProbes.L8))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8D2Actual), "C#, aggressive — the same");
        });

    /// <summary>
    /// ⭐ MOVED PIN (C++ leg only, #140). Group 7's other half: L8b agrees across C#, JavaScript, MSIL AND, since
    /// #140, C++ (11 21 31, D2's recorded output). C# printed it before #136 too: its lambda body defect did not reach
    /// this exact shape. C++ USED TO silently print 10 20 30, then to be refused by name (#170).
    /// </summary>
    [Test]
    public void L8b_AgreesAcrossCSharpJavaScriptMsilAndCpp()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(PerIterationLoopBodyDimProbes.L8b))),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8bExpected), "C#");
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(PerIterationLoopBodyDimProbes.L8b)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8bExpected), "JavaScript");
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimProbes.L8b)),
                Is.EqualTo(PerIterationLoopBodyDimProbes.L8bExpected), "MSIL");
            Assert.That(CppClosures.Compile(PerIterationLoopBodyDimProbes.L8b).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered), "C++ root 'Main'");
            CppClosures.RunsInAllModes(PerIterationLoopBodyDimProbes.L8b, PerIterationLoopBodyDimProbes.L8bExpected, "C++");
        });

    /// <summary>
    /// ⭐ MOVED PIN (C# leg #136, C++ leg #140). The non-loop control for #136 (C#): a plain function-top local
    /// captured and written by a lambda, no loop anywhere in the program. C# printed 5\n0 for VB's 5\n1 until #136 wrote the lambda's
    /// body; C++ printed that same wrong answer until #140, was refused by name (#170), and prints VB's 5\n1 now.
    /// </summary>
    [Test]
    public void Cl_NonLoopClosureWrite_CSharpAndCppRunVbsAnswer()
    {
        Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(PerIterationLoopBodyDimProbes.Cl))),
            Is.EqualTo(PerIterationLoopBodyDimProbes.ClExpected), "C#");
        Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(PerIterationLoopBodyDimProbes.Cl))),
            Is.EqualTo(PerIterationLoopBodyDimProbes.ClExpected), "C#, aggressive");

        Assert.That(CppClosures.Compile(PerIterationLoopBodyDimProbes.Cl).PathOf("Main"), Is.EqualTo(CppClosurePath.Lowered));
        CppClosures.RunsInAllModes(PerIterationLoopBodyDimProbes.Cl, PerIterationLoopBodyDimProbes.ClExpected, "C++");
    }
}

// =====================================================================================
//  Group 4 (execution half) — A2's own optimizer probes, K1-K5, run for real under BOTH
//  pipelines. Before A2: LICM hoisted a pure read of a captured-but-never-called-through
//  per-iteration local out of the loop (no call in the body), which threw
//  "ReferenceError: x is not defined" on JavaScript `-O` and tripped Invariant S' on MSIL `-O`.
//  K1/K2's MSIL leg is its own test per probe: their array of `Func(Of Integer)` did not assemble
//  on MSIL until #225 (they were pinned here as ILASM-FAIL), and now runs on both pipelines.
// =====================================================================================

[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class PerIterationLoopBodyDimOptimizerExecutionTests
{
    /// <summary>K1/K3/K5's own shape (and K2/K4's siblings): C#, C++ and JavaScript, both
    /// pipelines, no MSIL leg. MSIL is asserted separately per probe below (K1/K2 by their own
    /// MSIL test since #225, K3-K5 included in the full four-backend assertion).</summary>
    private static void AssertCSharpCppJs(string source, string expected, bool aggressive) => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(aggressive ? FourBackends.RunEmittedCSharpAggressive(source) : FourBackends.RunEmittedCSharp(source)),
            Is.EqualTo(expected), "C#");
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(aggressive ? BclE2E.CompileToCppAggressive(source) : BclE2E.CompileToCppOptimized(source))),
            Is.EqualTo(expected), "C++");
        Assert.That(FourBackends.Norm(aggressive ? FourBackends.RunAggressiveJs(source) : JavaScriptExecutionTests.RunJs(source)),
            Is.EqualTo(expected), "JavaScript — must not throw ReferenceError under -O (A2's own regression)");
    });

    // ---- K1/K2: MSIL by its own test (pinned as ILASM-FAIL until #225) ----------------------

    [Test]
    public void K1_CapturedNeverCalledThrough_StandardPipeline_CSharpCppJsAgree()
        => AssertCSharpCppJs(PerIterationLoopBodyDimProbes.K1, PerIterationLoopBodyDimProbes.K1Expected, aggressive: false);

    [Test]
    public void K1_CapturedNeverCalledThrough_AggressivePipeline_CSharpCppJsAgree()
        => AssertCSharpCppJs(PerIterationLoopBodyDimProbes.K1, PerIterationLoopBodyDimProbes.K1Expected, aggressive: true);

    /// <summary>MOVED from the #225 pin (<c>K1_Msil_DoesNotAssemble_PinnedForTask225</c>): MSIL emitted
    /// <c>newarr [mscorlib]System.Func\`1&lt;int32&gt;</c> for the fixed array of a generic delegate —
    /// no <c>class</c>, a syntax error to ilasm — on every pipeline. It assembles and prints VB's
    /// answer now, through the standard and the aggressive pipeline.</summary>
    [Test]
    public void K1_Msil_RunsAndPrintsVbsAnswer_BothPipelines() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimProbes.K1)),
            Is.EqualTo(PerIterationLoopBodyDimProbes.K1Expected), "MSIL, standard pipeline");
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(PerIterationLoopBodyDimProbes.K1)),
            Is.EqualTo(PerIterationLoopBodyDimProbes.K1Expected), "MSIL, aggressive pipeline");
    });

    [Test]
    public void K2_WhileLoop_StandardPipeline_CSharpCppJsAgree()
        => AssertCSharpCppJs(PerIterationLoopBodyDimProbes.K2, PerIterationLoopBodyDimProbes.K2Expected, aggressive: false);

    [Test]
    public void K2_WhileLoop_AggressivePipeline_CSharpCppJsAgree()
        => AssertCSharpCppJs(PerIterationLoopBodyDimProbes.K2, PerIterationLoopBodyDimProbes.K2Expected, aggressive: true);

    /// <summary>MOVED from the #225 pin (<c>K2_Msil_DoesNotAssemble_PinnedForTask225</c>), as K1.</summary>
    [Test]
    public void K2_Msil_RunsAndPrintsVbsAnswer_BothPipelines() => Assert.Multiple(() =>
    {
        Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(PerIterationLoopBodyDimProbes.K2)),
            Is.EqualTo(PerIterationLoopBodyDimProbes.K2Expected), "MSIL, standard pipeline");
        Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(PerIterationLoopBodyDimProbes.K2)),
            Is.EqualTo(PerIterationLoopBodyDimProbes.K2Expected), "MSIL, aggressive pipeline");
    });

    // ---- K3/K4/K5: all four backends, both pipelines -----------------------------------------

    [Test]
    public void K3_ListOfFuncInsteadOfArray_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(PerIterationLoopBodyDimProbes.K3, PerIterationLoopBodyDimProbes.K3Expected);

    [Test]
    public void K3_ListOfFuncInsteadOfArray_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(PerIterationLoopBodyDimProbes.K3, PerIterationLoopBodyDimProbes.K3Expected);

    [Test]
    public void K4_RealValueReadInsideAndAfterTheLoop_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(PerIterationLoopBodyDimProbes.K4, PerIterationLoopBodyDimProbes.K4Expected);

    [Test]
    public void K4_RealValueReadInsideAndAfterTheLoop_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(PerIterationLoopBodyDimProbes.K4, PerIterationLoopBodyDimProbes.K4Expected);

    [Test]
    public void K5_DoWhileLoop_StandardPipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackend(PerIterationLoopBodyDimProbes.K5, PerIterationLoopBodyDimProbes.K5Expected);

    [Test]
    public void K5_DoWhileLoop_AggressivePipeline_AllFourBackendsAgree()
        => FourBackends.RunsOnEveryBackendAggressive(PerIterationLoopBodyDimProbes.K5, PerIterationLoopBodyDimProbes.K5Expected);
}

// =====================================================================================
//  Group 2 (D6) — byte identity for a lambda-free program. Not a literal byte-for-byte diff
//  against master (no baseline checkout is taken here — CLAUDE.md forbids touching git state in
//  this task), but a STRUCTURAL assertion the ADR itself calls out as sufficient: a function with
//  an empty perIter emits NO carrier and NO try/finally wrapper at all, so its C#/JavaScript
//  output is indistinguishable in SHAPE from what master's non-per-iteration codegen already
//  emitted — the exact claim D6 makes ("every backend takes its existing path unchanged").
// =====================================================================================

[TestFixture]
public class PerIterationLoopBodyDimByteIdentityTests
{
    /// <summary>A loop-body Dim with NO lambda anywhere in the function — L3 itself, and the
    /// probe the ADR's own D6 revisit clause names ("a program with no lambda in the function
    /// changes output on any backend"). Also covers the OTHER four loop kinds this same way,
    /// since D1's contract is "every loop node", not just a counted For.</summary>
    private static readonly (string Name, string Source)[] LambdaFreeLoopBodyDimPrograms =
    {
        ("L3_CountedFor", PerIterationLoopBodyDimProbes.L3),
        ("E07n_NestedForNoLambda", PerIterationLoopBodyDimProbes.E07n),
        ("E15n_SizedArrayNoLambda", PerIterationLoopBodyDimProbes.E15n),
    };

    private static IEnumerable<TestCaseData> Cases() =>
        LambdaFreeLoopBodyDimPrograms.Select(p => new TestCaseData(p.Source).SetName(p.Name + "_NoCarrierNoTryFinally"));

    [TestCaseSource(nameof(Cases))]
    public void EmitsNoCarrierAndNoTryFinally_CSharp(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var standard = new OptimizationPipeline();
        standard.AddStandardPasses();
        standard.Run(module);
        var csharp = new ImprovedCSharpCodeGenerator().Generate(module);

        Assert.Multiple(() =>
        {
            Assert.That(csharp, Does.Not.Contain("__carry_"),
                "D6: a lambda-free function must declare no carrier at all");
            // A lambda-free program may still contain a `try` from the SOURCE (E03/E04's own
            // shape does), so the guard is specifically the carrier's own finally text, not the
            // bare keyword.
            Assert.That(csharp, Does.Not.Contain("finally\n{"),
                "D6: a lambda-free function must not wrap its loop body in ADR-0014's finally");
        });
    }

    [TestCaseSource(nameof(Cases))]
    public void EmitsNoCarrierAndNoTryFinally_JavaScript(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var standard = new OptimizationPipeline();
        standard.AddStandardPasses();
        standard.Run(module);
        var js = new JavaScriptCodeGenerator().Generate(module);

        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("__carry_"),
                "D6: a lambda-free function must declare no carrier at all");
            Assert.That(js, Does.Not.Contain("} finally {"),
                "D6: a lambda-free function must not wrap its loop body in ADR-0014's finally");
        });
    }

    /// <summary>The AGGRESSIVE pipeline's own D6 claim: LICM's new perIter-is-written-at-entry
    /// rule (A2) must not change a lambda-free function's output either, since perIter is empty
    /// for every one of them.</summary>
    [TestCaseSource(nameof(Cases))]
    public void EmitsNoCarrierAndNoTryFinally_AggressivePipeline_CSharp(string source)
    {
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        AggressivePipeline.Apply(module);
        var csharp = new ImprovedCSharpCodeGenerator().Generate(module);
        Assert.That(csharp, Does.Not.Contain("__carry_"),
            "D6 under -O: a lambda-free function must declare no carrier at all");
    }
}

// =====================================================================================
//  Group 5 — the IR facts themselves (D1's Obligations, D3's scoping, A2's Invariant S″, and
//  Invariant B). Fast subset: no process spawned, IRBuilder's own output inspected directly.
// =====================================================================================

[TestFixture]
public class PerIterationLoopBodyDimIrFactTests
{
    private static IRFunction MainOf(IRModule module) =>
        module.Functions.First(f => f.Name == "Main" && !f.IsExternal);

    /// <summary>D1's Obligation: <c>IRBuilder</c> populates <c>BasicBlock.BodyLocals</c> on the
    /// INNERMOST loop's body block only. L5 declares `a` in the OUTER loop's body and `b` in the
    /// INNER loop's — each must land on its own loop, never the other's, and never both.</summary>
    [Test]
    public void BodyLocals_IsPopulatedOnTheInnermostLoopsBodyBlockOnly()
    {
        var module = JsTestSupport.BuildModule(PerIterationLoopBodyDimProbes.L5, sourceFilePath: "prog.bas");
        var function = MainOf(module);
        var loops = IRLoops.Of(function);

        var outer = loops.Single(l => l.Body.BodyLocals.Any(v => v.Name == "a"));
        var inner = loops.Single(l => l.Body.BodyLocals.Any(v => v.Name == "b"));

        Assert.Multiple(() =>
        {
            Assert.That(outer.Body, Is.Not.SameAs(inner.Body), "a and b must be on DIFFERENT loops' body blocks");
            Assert.That(outer.Body.BodyLocals.Select(v => v.Name), Does.Not.Contain("b"),
                "the OUTER loop's body must not claim the INNER loop's own Dim");
            Assert.That(inner.Body.BodyLocals.Select(v => v.Name), Does.Not.Contain("a"),
                "the INNER loop's body must not claim the OUTER loop's own Dim");
        });
    }

    /// <summary>D3's last bullet: "a Dim inside a lambda body belongs to the lambda's own loops,
    /// never the creator's." E08's lambda `outer` has its own For loop with its own body-local
    /// `x` — it must be recorded on the LAMBDA function's own block, and the CREATOR (`Main`,
    /// which has no loop of its own in this probe) must carry no BodyLocals entry at all.</summary>
    [Test]
    public void BodyLocals_NeverCrossesFromALambdaToItsCreator()
    {
        var module = JsTestSupport.BuildModule(PerIterationLoopBodyDimProbes.E08, sourceFilePath: "prog.bas");
        var main = MainOf(module);
        var lambda = module.Functions.Single(f => f.IsLambda && f.Blocks.Any(b => b.BodyLocals.Any(v => v.Name == "x")));

        Assert.Multiple(() =>
        {
            Assert.That(main.Blocks.SelectMany(b => b.BodyLocals), Is.Empty,
                "Main has no loop of its own in this probe — nothing should ever be attributed to it");
            Assert.That(lambda.LocalVariables.Select(v => v.Name), Does.Contain("x"),
                "x must be a genuine local of the LAMBDA function, not the creator's");
        });
    }

    /// <summary>The one-declaration rule (#229): E16's two SIBLING loops each declare their own
    /// `Dim x` — a name declared twice in one function keeps today's function-level behaviour on
    /// EVERY loop that shares it, by construction (IRBuilder.AssignBodyLocals' own
    /// <c>declarations[…] != 1</c> guard), never just one of the two.</summary>
    [Test]
    public void OneDeclarationRule_NeitherSiblingLoopClaimsASharedName()
    {
        var module = JsTestSupport.BuildModule(PerIterationLoopBodyDimProbes.E16, sourceFilePath: "prog.bas");
        var function = MainOf(module);
        var loops = IRLoops.Of(function).Where(l => l.Kind == IRLoopKind.For).ToList();

        Assert.That(loops, Has.Count.GreaterThanOrEqualTo(2), "E16 declares two sibling For loops");
        foreach (var loop in loops)
            Assert.That(loop.Body.BodyLocals.Select(v => v.Name), Does.Not.Contain("x"),
                $"{loop.Body.Name} must not claim 'x' — it has two declarations in this function");
    }

    /// <summary>The one-declaration rule's other half (#229): E20's loop-body `Dim y` is excluded
    /// because a SECOND, unrelated lambda the same function creates (`h`) declares its OWN local
    /// also spelled `y` — <c>LambdaLocalNames</c>' own exclusion.</summary>
    [Test]
    public void OneDeclarationRule_ExcludesANameALambdaDeclaresAsItsOwnLocal()
    {
        var module = JsTestSupport.BuildModule(PerIterationLoopBodyDimProbes.E20, sourceFilePath: "prog.bas");
        var function = MainOf(module);
        // E20 has TWO top-level For loops (the capturing one and the print loop after `h`); pick
        // the one whose body actually assigns 'y' — the OTHER For loop's body never mentions it.
        var loop = IRLoops.Of(function).Single(l =>
            l.Kind == IRLoopKind.For && l.Body.Instructions.Any(i => IRLoops.VariableMentions(i).Contains("y")));

        Assert.That(loop.Body.BodyLocals.Select(v => v.Name), Does.Not.Contain("y"),
            "the loop's own 'y' must be excluded — a lambda this function creates declares its OWN 'y'");
    }

    // ----------------------------------------------------------------------------------------
    // Invariant B: every BodyLocals entry is in the function's LocalVariables and appears in
    // exactly one loop. Structural check over real compiled probes (both pipelines), plus two
    // hand-built violations proving the check itself is falsifiable.
    // ----------------------------------------------------------------------------------------

    private static readonly string[] InvariantBProbes =
    {
        PerIterationLoopBodyDimProbes.L1, PerIterationLoopBodyDimProbes.L2, PerIterationLoopBodyDimProbes.L4,
        PerIterationLoopBodyDimProbes.L5, PerIterationLoopBodyDimProbes.L6, PerIterationLoopBodyDimProbes.L7,
        PerIterationLoopBodyDimProbes.E01, PerIterationLoopBodyDimProbes.E02, PerIterationLoopBodyDimProbes.E03,
        PerIterationLoopBodyDimProbes.E05, PerIterationLoopBodyDimProbes.E06, PerIterationLoopBodyDimProbes.E07,
        PerIterationLoopBodyDimProbes.E07e, PerIterationLoopBodyDimProbes.E07f, PerIterationLoopBodyDimProbes.E07w,
        PerIterationLoopBodyDimProbes.E07x, PerIterationLoopBodyDimProbes.E09, PerIterationLoopBodyDimProbes.E10,
        PerIterationLoopBodyDimProbes.E11, PerIterationLoopBodyDimProbes.E13, PerIterationLoopBodyDimProbes.E14,
        PerIterationLoopBodyDimProbes.E16, PerIterationLoopBodyDimProbes.E17, PerIterationLoopBodyDimProbes.E18,
        PerIterationLoopBodyDimProbes.E19, PerIterationLoopBodyDimProbes.E20, PerIterationLoopBodyDimProbes.L8,
        PerIterationLoopBodyDimProbes.L8b, PerIterationLoopBodyDimProbes.Cl,
        PerIterationLoopBodyDimProbes.K1, PerIterationLoopBodyDimProbes.K2, PerIterationLoopBodyDimProbes.K3,
        PerIterationLoopBodyDimProbes.K4, PerIterationLoopBodyDimProbes.K5,
    };

    [Test]
    public void InvariantB_HoldsOverEveryProbe_StandardPipeline()
        => Assert.Multiple(() =>
        {
            foreach (var source in InvariantBProbes)
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                var pipeline = new OptimizationPipeline();
                pipeline.AddStandardPasses();
                pipeline.Run(module);
                var violations = IRVerifier.CheckInvariantB(module);
                Assert.That(violations, Is.Empty, string.Join(" | ", violations.Select(v => v.ToString())));
            }
        });

    [Test]
    public void InvariantB_HoldsOverEveryProbe_AggressivePipeline()
        => Assert.Multiple(() =>
        {
            foreach (var source in InvariantBProbes)
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                AggressivePipeline.Apply(module);
                var violations = IRVerifier.CheckInvariantB(module);
                Assert.That(violations, Is.Empty, string.Join(" | ", violations.Select(v => v.ToString())));
            }
        });

    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    [Test]
    public void InvariantB_FiresWhenTwoLoopsClaimTheSameVariable()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var x = new IRVariable("x", IntType);
        function.LocalVariables.Add(x);
        var body1 = function.CreateBlock("for1.body");
        var body2 = function.CreateBlock("for2.body");
        body1.BodyLocals.Add(x);
        body2.BodyLocals.Add(x);

        var violations = IRVerifier.CheckInvariantB(module);
        Assert.That(violations, Is.Not.Empty, "one variable listed by two loop bodies must FIRE");
        Assert.That(violations.Any(v => v.Invariant == "B" && v.Variable == "x"), Is.True);
    }

    [Test]
    public void InvariantB_FiresWhenTheVariableIsNotInLocalVariables()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var x = new IRVariable("x", IntType); // deliberately NOT added to LocalVariables
        var body = function.CreateBlock("for1.body");
        body.BodyLocals.Add(x);

        var violations = IRVerifier.CheckInvariantB(module);
        Assert.That(violations, Is.Not.Empty, "an entry absent from LocalVariables must FIRE");
        Assert.That(violations.Any(v => v.Invariant == "B" && v.Variable == "x"), Is.True);
    }

    [Test]
    public void InvariantB_QuietOnAnEmptyModule()
        => Assert.That(IRVerifier.CheckInvariantB(new IRModule("M")), Is.Empty);
}

// =====================================================================================
//  Group 4 (verifier half) — A2's Invariant S″: "every IR reference to a perIter(L) variable
//  lies in a block of L's body." A hand-built pair proves the check itself is falsifiable (fires
//  / quiet on the SAME fixture, one line different); a structural sweep proves it never fires on
//  the compiler's own output, both pipelines, over every probe in this file.
//
//  ⚠ THE TEST HOST ALREADY VERIFIES IN THROW MODE (VisualGameStudio.Tests.csproj sets the
//  BasicLang.VerifyIR runtime switch — see DynamicUseSPrimeTests.cs's own note on this), so every
//  execution test in this file ALREADY exercises S″ on every pass: a violation would have thrown
//  IRVerificationException and failed the compile, not printed a wrong number. The structural
//  fixture below additionally calls CheckInvariantSDoublePrime directly, so "no violation" is
//  ASSERTED rather than merely relied upon not to have thrown.
// =====================================================================================

[TestFixture]
public class PerIterationLoopBodyDimVerifierTests
{
    private static readonly TypeInfo IntType = new TypeInfo("Integer", TypeKind.Primitive);

    /// <summary>A minimal hand-built <c>perIter(L) = { x }</c> fixture: one loop ("for1.body"),
    /// one captured local ("x") declared in its body. A reference to <c>__lambda_0</c> anywhere
    /// in the function is enough to make <c>x</c> captured — <c>IsLambdaCaptured</c>'s own
    /// interim rule (#122) treats EVERY local as captured once a function references a lambda by
    /// name and has recorded no capture set for it (null means NOT COMPUTED, which hand-built IR
    /// never does), so no lambda IRFunction needs to be built at all.</summary>
    private static (IRModule Module, IRFunction Function, IRVariable X, BasicBlock Body) BuildHandBuiltPerIterationFixture()
    {
        var module = new IRModule("M");
        var function = new IRFunction("Main", IntType);
        module.Functions.Add(function);
        var x = new IRVariable("x", IntType);
        function.LocalVariables.Add(x);
        var body = function.CreateBlock("for1.body");
        body.BodyLocals.Add(x);
        body.Instructions.Add(new IRAssignment(x, new IRConstant(1, IntType)));

        var lambdaRef = new IRVariable("__lambda_0", IntType);
        var captureSite = new IRCall("", "Add", IntType);
        captureSite.Arguments.Add(lambdaRef);
        body.Instructions.Add(captureSite);

        return (module, function, x, body);
    }

    [Test]
    public void SDoublePrime_QuietWhenEveryReferenceToAPerIterationVariableStaysInsideItsBody()
    {
        var (module, _, _, _) = BuildHandBuiltPerIterationFixture();
        var violations = IRVerifier.CheckInvariantSDoublePrime(module);
        Assert.That(violations, Is.Empty, string.Join(" | ", violations.Select(v => v.ToString())));
    }

    /// <summary>The SAME fixture, ONE rogue instruction added in a block OUTSIDE the loop's body
    /// — the shape A2 exists to catch: a per-iteration variable named from anywhere a pass (or,
    /// here, a hand-tampered module) might move a reference to.</summary>
    [Test]
    public void SDoublePrime_FiresWhenAPerIterationVariableIsReferencedOutsideItsLoopBody()
    {
        var (module, function, x, _) = BuildHandBuiltPerIterationFixture();
        var after = function.CreateBlock("after.loop");
        after.Instructions.Add(new IRAssignment(x, new IRConstant(0, IntType)));

        var violations = IRVerifier.CheckInvariantSDoublePrime(module);
        Assert.That(violations, Is.Not.Empty, "a reference outside the loop's body must FIRE");
        Assert.Multiple(() =>
        {
            Assert.That(violations[0].Invariant, Is.EqualTo("S″"));
            Assert.That(violations[0].Variable, Is.EqualTo("x"));
            Assert.That(violations[0].Function, Is.EqualTo("Main"));
        });
    }

    /// <summary>A READ, not just a write, outside the body also fires — VariableMentions covers
    /// both directions, and S″ makes no distinction (D1's contract: "every IR reference (read or
    /// write)").</summary>
    [Test]
    public void SDoublePrime_FiresOnAReadOutsideTheBodyToo()
    {
        var (module, function, x, _) = BuildHandBuiltPerIterationFixture();
        var after = function.CreateBlock("after.loop");
        var read = new IRCall("", "Show", IntType);
        read.Arguments.Add(x);
        after.Instructions.Add(read);

        var violations = IRVerifier.CheckInvariantSDoublePrime(module);
        Assert.That(violations, Is.Not.Empty, "a READ outside the loop's body must FIRE too");
    }

    // ----------------------------------------------------------------------------------------
    // Structural sweep: S″ never fires on the compiler's OWN output, over every probe in this
    // file, both pipelines. The K-series is A2's own regression set (LICM's new write-at-entry
    // rule); the rest is the same broad probe list InvariantB's own sweep uses.
    // ----------------------------------------------------------------------------------------

    private static readonly string[] SweepProbes =
    {
        PerIterationLoopBodyDimProbes.L1, PerIterationLoopBodyDimProbes.L2, PerIterationLoopBodyDimProbes.L4,
        PerIterationLoopBodyDimProbes.L5, PerIterationLoopBodyDimProbes.L6, PerIterationLoopBodyDimProbes.L7,
        PerIterationLoopBodyDimProbes.E01, PerIterationLoopBodyDimProbes.E02, PerIterationLoopBodyDimProbes.E03,
        PerIterationLoopBodyDimProbes.E05, PerIterationLoopBodyDimProbes.E06, PerIterationLoopBodyDimProbes.E07,
        PerIterationLoopBodyDimProbes.E07e, PerIterationLoopBodyDimProbes.E07f, PerIterationLoopBodyDimProbes.E07w,
        PerIterationLoopBodyDimProbes.E07x, PerIterationLoopBodyDimProbes.E09, PerIterationLoopBodyDimProbes.E10,
        PerIterationLoopBodyDimProbes.E11, PerIterationLoopBodyDimProbes.E13, PerIterationLoopBodyDimProbes.E14,
        PerIterationLoopBodyDimProbes.E16, PerIterationLoopBodyDimProbes.E17, PerIterationLoopBodyDimProbes.E18,
        PerIterationLoopBodyDimProbes.E19, PerIterationLoopBodyDimProbes.E20, PerIterationLoopBodyDimProbes.L8,
        PerIterationLoopBodyDimProbes.L8b, PerIterationLoopBodyDimProbes.Cl,
        PerIterationLoopBodyDimProbes.K1, PerIterationLoopBodyDimProbes.K2, PerIterationLoopBodyDimProbes.K3,
        PerIterationLoopBodyDimProbes.K4, PerIterationLoopBodyDimProbes.K5,
    };

    [Test]
    public void SDoublePrime_QuietOverEveryProbe_StandardPipeline()
        => Assert.Multiple(() =>
        {
            foreach (var source in SweepProbes)
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                var pipeline = new OptimizationPipeline();
                pipeline.AddStandardPasses();
                pipeline.Run(module);
                var violations = IRVerifier.CheckInvariantSDoublePrime(module);
                Assert.That(violations, Is.Empty, string.Join(" | ", violations.Select(v => v.ToString())));
            }
        });

    /// <summary>The regression this invariant exists to catch (A2's own "Finding 2"): before A2,
    /// K1/K3/K5's read of a captured-but-never-called-through loop-body local was hoisted out of
    /// the loop by LICM under <c>-O</c>, which is EXACTLY an S″ breach — a reference to a
    /// perIter variable that moved outside its loop's body. LICM's new write-at-entry rule must
    /// keep S″ quiet on these three specifically, aggressive pipeline.</summary>
    [Test]
    public void SDoublePrime_QuietOverTheLicmRegressionProbes_AggressivePipeline()
        => Assert.Multiple(() =>
        {
            foreach (var source in new[]
                     {
                         PerIterationLoopBodyDimProbes.K1, PerIterationLoopBodyDimProbes.K3,
                         PerIterationLoopBodyDimProbes.K4, PerIterationLoopBodyDimProbes.K5,
                     })
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                AggressivePipeline.Apply(module);
                var violations = IRVerifier.CheckInvariantSDoublePrime(module);
                Assert.That(violations, Is.Empty, string.Join(" | ", violations.Select(v => v.ToString())));
            }
        });

    [Test]
    public void SDoublePrime_QuietOverEveryProbe_AggressivePipeline()
        => Assert.Multiple(() =>
        {
            foreach (var source in SweepProbes)
            {
                var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
                AggressivePipeline.Apply(module);
                var violations = IRVerifier.CheckInvariantSDoublePrime(module);
                Assert.That(violations, Is.Empty, string.Join(" | ", violations.Select(v => v.ToString())));
            }
        });
}
