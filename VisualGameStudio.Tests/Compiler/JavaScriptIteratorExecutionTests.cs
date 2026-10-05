using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Task #145 — on JavaScript, an Iterator Function returns a RE-ITERABLE generator. RUN under Node, against vbc, through every entry point.
//
//  ⛔ THE BUG. A class's Iterator method was emitted as a plain method holding `yield` (a SyntaxError: the whole file failed to load), and a module-level Iterator Function was a bare
//  `function*` returning its generator OBJECT, which is ONE-SHOT: the `IEnumerable` VB returns can be walked again, each For Each running the body again from the top, and on the old code
//  the second walk printed nothing, from a build that reported success. The fix (JavaScriptBackend only) emits both as `return {[Symbol.iterator]: function* (params) {body}.bind(this,
//  params)}`: a fresh generator per walk, over its OWN copies of the call's arguments; and a `MyBase.M()` inside the body as `Object.getPrototypeOf(Class.prototype).M.call(this, ...)`,
//  because `super` is a SyntaxError inside a `function*` expression. An Async Iterator is emitted as it was.
//
//  ⭐ THE ORACLE IS vbc, not a backend. Every row is a program of the implementer's probes (S/t145/probes) and its expected text is what the SDK's vbc prints for it (the program wrapped in
//  a VB Module; the probe's top-level `Iterator Function` is accepted by tools/vbv145.py). Each runs on JavaScript through the real CLI, the CLI with `--optimize` and
//  `BasicCompiler.CompileProjectFiles` with `OptimizeAggressive` (`TempExec.AssertMatchesInEveryEntryPoint`), because the IDE build and the CLI are separate entries.
//
//  ⭐ THE ROWS, and what each one kills (S/t145/mut: each mutant is the fix plus ONE change):
//    Y1  a class iterator that reads a field — the method had to LOAD (it was a SyntaxError); `this` must be bound, and a walk sees the field as it is NOW (2, then 12).
//    FE1L  a class iterator walked three times: the body runs again each walk (K grows) — M2 (the one-shot generator object) prints `seed | 10`.
//    I2  a method WITH parameters and a field (`this` and the arguments both reach the generator).
//    I3  `Exit Function` inside a `Do` loop, and after a Yield — the generator ends there.
//    I4  a result held in a variable and walked TWICE, its body assigning a parameter: the body runs per walk, and each walk starts from the arguments AS PASSED. Kills M1 (the generator
//        closes over the call's parameters: the second walk prints 20/21 where vbc prints 10/11) and M2 (a second walk prints nothing).
//    I6  a `Shared` iterator walked twice, assigning its parameter — M1 and M2 again, through the static path (`_iteratorBaseHome` is the class itself).
//    I7  an `Overrides` iterator calling `MyBase.Sounds()` — M3 (`super.` kept inside the generator: SyntaxError on load).
//    I9  a lambda inside an iterator reading a field — the lambda's `this` is the bound one.
//
//  ⛔ KNOWN GAPS — each is NOT #145's, measured, and has NO test (asserting one would pin the defect; see docs/HANDOFF.md):
//    I5   `.ToList()` / `.Count()` on an iterator's result: the JS LINQ lowering treats every `IEnumerable` as an Array (`TypeError: t1.slice is not a function`), before and after #145.
//    C#   drops a `Do` loop and `Exit Function` in an iterator body (I3 prints the wrong text on C#).
//    C++  an iterator cannot be walked twice (the coroutine is one-shot).
//    all  the front end refuses a bare `Return` in an Iterator Function (BL3001): `Exit Function` is the spelling that runs (row I3).
//    MSIL has no `IEnumerable` at all.
//    JS   an Async Iterator is emitted as before #145 and is not tested here.
//
//  ⚠ Named "…ExecutionTests" on purpose: it RUNS under Node, so it is in JsExecutionTierRosterTests' roster. Its rows are [TestCase] attributes (not a [TestCaseSource]) because that roster counts
//  attributes. The FAST half is JavaScriptIteratorShapeTests, which reads the text.
// ================================================================================================

/// <summary>
/// #145 RUN: an Iterator Function or Iterator method on JavaScript can be walked more than once, starts each walk from the arguments it was called with, loads inside a class, and runs
/// <c>MyBase.M()</c> — and prints vbc's answer through the CLI, the CLI with <c>--optimize</c> and <c>CompileProjectFiles</c>.
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns the CLI and node
[NonParallelizable]
public class JavaScriptIteratorExecutionTests
{
    /// <summary>id -> (program, what vbc prints). The programs are S/t145/probes/*.bas verbatim; the text is the matching .exp.</summary>
    private static readonly Dictionary<string, (string Source, string Vb)> Probes = new()
    {
        ["Y1"] = (
            """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed")
                Return v
            End Function

            Class Box
                Public K As Integer

                Iterator Function Gen() As IEnumerable(Of Integer)
                    Dim a As Integer = K + 1
                    Yield a
                    Yield K + 1
                End Function
            End Class

            Sub Main()
                Dim b As New Box()
                b.K = Seed(1)
                For Each x In b.Gen()
                    Console.WriteLine(CStr(x))
                    b.K = b.K + 10
                Next
            End Sub
            """,
            "seed\n2\n12\n"),

        ["FE1L"] = (
            """
            Function Seed(v As Integer) As Integer
                Console.WriteLine("seed")
                Return v
            End Function

            Class Box
                Public K As Integer

                Iterator Function Gen() As IEnumerable(Of Integer)
                    K = K + 1
                    Yield 0
                End Function

                Sub Work()
                    K = Seed(1)
                    Dim g As IEnumerable(Of Integer) = Gen()
                    Dim s As Integer = 0
                    For i As Integer = 1 To 3
                        s = s + K * 2
                        For Each x In g
                            s = s + x
                        Next
                    Next
                    Console.WriteLine(CStr(s))
                End Sub
            End Class

            Sub Main()
                Dim b As New Box()
                b.Work()
            End Sub
            """,
            "seed\n12\n"),

        ["I2"] = (
            """
            Class Counter
                Public Base As Integer

                Iterator Function Range(lo As Integer, hi As Integer) As IEnumerable(Of Integer)
                    Dim i As Integer = lo
                    While i <= hi
                        If i Mod 3 = 0 Then
                            Yield Base + i
                        Else
                            Yield i
                        End If
                        i = i + 1
                    End While
                End Function
            End Class

            Sub Main()
                Dim c As New Counter()
                c.Base = 100
                Dim total As Integer = 0
                For Each v In c.Range(2, 7)
                    Console.WriteLine(CStr(v))
                    total = total + v
                Next
                Console.WriteLine("total " & CStr(total))
            End Sub
            """,
            "2\n103\n4\n5\n106\n7\ntotal 227\n"),

        ["I3"] = (
            """
            Iterator Function UpTo(limit As Integer) As IEnumerable(Of Integer)
                Dim i As Integer = 0
                Do
                    i = i + 1
                    If i > limit Then
                        Exit Function
                    End If
                    Yield i
                Loop
            End Function

            Iterator Function Words() As IEnumerable(Of String)
                Yield "alpha"
                Yield "beta"
                Exit Function
                Yield "never"
            End Function

            Sub Main()
                For Each v In UpTo(3)
                    Console.WriteLine(CStr(v))
                Next
                For Each w In Words()
                    Console.WriteLine(w)
                Next
                Console.WriteLine("done")
            End Sub
            """,
            "1\n2\n3\nalpha\nbeta\ndone\n"),

        ["I4"] = (
            """
            Dim calls As Integer = 0

            Iterator Function Gen(start As Integer) As IEnumerable(Of Integer)
                calls = calls + 1
                start = start * 2
                Yield start
                Yield start + 1
            End Function

            Sub Main()
                Dim g As IEnumerable(Of Integer) = Gen(5)
                Console.WriteLine("before " & CStr(calls))
                For pass As Integer = 1 To 2
                    For Each v In g
                        Console.WriteLine(CStr(pass) & ":" & CStr(v))
                    Next
                Next
                Console.WriteLine("calls " & CStr(calls))
            End Sub
            """,
            "before 0\n1:10\n1:11\n2:10\n2:11\ncalls 2\n"),

        ["I6"] = (
            """
            Class Gens
                Shared Iterator Function Countdown(n As Integer) As IEnumerable(Of Integer)
                    While n > 0
                        Yield n
                        n = n - 1
                    End While
                End Function
            End Class

            Sub Main()
                Dim seq As IEnumerable(Of Integer) = Gens.Countdown(3)
                For Each v In seq
                    Console.WriteLine(CStr(v))
                Next
                For Each v In seq
                    Console.WriteLine("again " & CStr(v))
                Next
            End Sub
            """,
            "3\n2\n1\nagain 3\nagain 2\nagain 1\n"),

        ["I7"] = (
            """
            Class Animal
                Overridable Iterator Function Sounds() As IEnumerable(Of String)
                    Yield "breath"
                End Function
            End Class

            Class Dog
                Inherits Animal
                Overrides Iterator Function Sounds() As IEnumerable(Of String)
                    For Each s In MyBase.Sounds()
                        Yield s
                    Next
                    Yield "woof"
                End Function
            End Class

            Sub Main()
                Dim a As Animal = New Dog()
                For Each s In a.Sounds()
                    Console.WriteLine(s)
                Next
            End Sub
            """,
            "breath\nwoof\n"),

        ["I9"] = (
            """
            Class Scaler
                Public F As Integer

                Iterator Function Scaled(xs As List(Of Integer)) As IEnumerable(Of Integer)
                    Dim mul As Func(Of Integer, Integer) = Function(v) v * F
                    For Each x In xs
                        Yield mul(x)
                    Next
                End Function
            End Class

            Sub Main()
                Dim s As New Scaler()
                s.F = 3
                Dim xs As New List(Of Integer)()
                xs.Add(1)
                xs.Add(2)
                For Each v In s.Scaled(xs)
                    Console.WriteLine(CStr(v))
                Next
            End Sub
            """,
            "3\n6\n"),
    };

    [TestCase("Y1", TestName = "Y1_AClassIteratorLoads_AndReadsItsField")]
    [TestCase("FE1L", TestName = "FE1L_AClassIteratorWalkedThreeTimes_RunsItsBodyEachWalk")]
    [TestCase("I2", TestName = "I2_AMethodIteratorWithParameters")]
    [TestCase("I3", TestName = "I3_ExitFunction_EndsTheGenerator")]
    [TestCase("I4", TestName = "I4_AResultWalkedTwice_RerunsTheBody_FromTheArgumentsAsPassed")]
    [TestCase("I6", TestName = "I6_ASharedIteratorWalkedTwice")]
    [TestCase("I7", TestName = "I7_AnOverridingIterator_CallsMyBase")]
    [TestCase("I9", TestName = "I9_ALambdaInsideAnIterator_ReadsTheField")]
    public void AnIterator_PrintsVbcsAnswer_OnJavaScript_InEveryEntryPoint(string id)
    {
        var probe = Probes[id];
        TempExec.AssertMatchesInEveryEntryPoint(Bk.JavaScript, probe.Source, probe.Vb, id);
    }
}
