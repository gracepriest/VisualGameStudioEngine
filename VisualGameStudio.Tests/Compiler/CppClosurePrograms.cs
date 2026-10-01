using System.Collections.Generic;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>One program of the #140 corpus: its name (the ruling's own), source, the display name of the
/// root that creates its lambdas (<c>Main</c>, <c>D.New</c>, <c>Derived.Tag</c>) and the output it must print.</summary>
public sealed record CppClosureProgram(string Name, string Source, string Root, string Expected, string Note = "");

/// <summary>
/// The programs of ADR-0019's obligation tables, spelled once. Every <c>Expected</c> is vbc's output
/// (<c>S/t140/oracle</c>) except where <c>Note</c> says otherwise: BasicLang's <c>Select Case … When</c> guard
/// does not exist in VB, so those two are derived by hand, and X1/X3 are programs VB REJECTS (BC30616: a
/// lambda's own `For Each x` / `Catch ex` hides the creator's `x` / `ex` — BasicLang accepts them on purpose,
/// the N9 backstop's shape), whose expectation is what C#, JavaScript and C++ all print.
/// </summary>
internal static class CppClosurePrograms
{
    // =========================================================================================
    // THE BY-COPY FALLBACK SET (ruling D3: "pinned BY NAME and may only shrink").
    // A root ClosureLowering refuses (D9 and beyond) that the by-copy rule W2 admits.
    // =========================================================================================

    /// <summary>R2 / D01: a lambda inside an Iterator function (D9: an iterator's frame is a coroutine).</summary>
    internal const string Iterator = """
        Iterator Function Gen() As IEnumerable(Of Integer)
            Dim k As Integer = 1
            Dim f = Function() k + 1
            Yield f()
        End Function
        Sub Main()
            For Each v As Integer In Gen()
                Console.WriteLine(v)
            Next
        End Sub
        """;

    /// <summary>D13: an Iterator whose lambda captures a PARAMETER and writes nothing.</summary>
    internal const string IteratorReadOnly = """
        Iterator Function Gen(k As Integer) As IEnumerable(Of Integer)
            Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * k
            For i As Integer = 1 To 3
                Yield f(i)
            Next
        End Function
        Sub Main()
            For Each v As Integer In Gen(10)
                Console.WriteLine(v)
            Next
        End Sub
        """;

    /// <summary>R14 / D06: <c>MyBase.Tag()</c> inside a lambda (D9: the environment has no base to call).</summary>
    internal const string MyBaseInLambda = """
        Class Base
            Public Overridable Function Tag() As Integer
                Return 1
            End Function
        End Class
        Class Derived
            Inherits Base
            Public Overrides Function Tag() As Integer
                Dim f = Function() MyBase.Tag() + 10
                Return f()
            End Function
        End Class
        Sub Main()
            Dim d As New Derived()
            Console.WriteLine(d.Tag())
        End Sub
        """;

    /// <summary>D15: a lambda in a method of a GENERIC class, capturing an Integer parameter (D9: an
    /// environment nested in a generic class would have to be generic).</summary>
    internal const string GenericClassIntCapture = """
        Class Holder(Of T)
            Public Item As T
            Public Function CountTo(n As Integer) As Integer
                Dim total As Integer = 0
                Dim add As Func(Of Integer, Integer) = Function(x As Integer) x + n
                For i As Integer = 1 To 3
                    total = add(total)
                Next
                Return total
            End Function
        End Class
        Sub Main()
            Dim h As New Holder(Of String)()
            Console.WriteLine(h.CountTo(4))
        End Sub
        """;

    /// <summary>D07b: a Select Case 'When' guard that reads a variable the lambda only READS (D9: a guard
    /// is rendered inline, where no environment load can be placed). Hand-derived: VB has no When guard.</summary>
    internal const string WhenGuardReadOnly = """
        Sub Main()
            Dim lim As Integer = 5
            Dim f As Func(Of Integer) = Function() lim + 1
            Console.WriteLine(f())
            Dim v As Integer = 7
            Select Case v
                Case Is > 0 When v > lim + 1
                    Console.WriteLine("big")
                Case Else
                    Console.WriteLine("small")
            End Select
        End Sub
        """;

    /// <summary>E12_later_sibling (N9): a lambda declares a local spelled like a LATER sibling block's
    /// local the same function declares (the name-based capture set is ambiguous: refused, never guessed).</summary>
    internal const string N9LaterSibling = """
        Sub Main()
            Dim f As Func(Of Integer) = Nothing
            If True Then
                f = Function()
                        Dim x As Integer = 3
                        Return x
                    End Function
            End If
            If True Then
                Dim x As Integer = 5
                Console.WriteLine(f() + x)
            End If
        End Sub
        """;

    /// <summary>E12_two_clauses_same_name: two Catch clauses of DIFFERENT types binding the same name, a
    /// lambda in each (the lowering refuses: one environment field cannot have two types).</summary>
    internal const string TwoCatchTypes = """
        Sub Main()
            Dim f As Action = Nothing
            For i As Integer = 1 To 2
                Try
                    If i = 1 Then
                        Throw New ArgumentException("arg" & i)
                    Else
                        Throw New InvalidOperationException("inv" & i)
                    End If
                Catch ex As ArgumentException
                    f = Sub() Console.WriteLine("A:" & ex.Message)
                Catch ex As InvalidOperationException
                    f = Sub() Console.WriteLine("I:" & ex.Message)
                End Try
                f()
            Next
        End Sub
        """;

    /// <summary>X1: a lambda's own <c>For Each x</c> hides the creator's <c>x</c> (N9). VB rejects it
    /// (BC30616); C#, JavaScript and C++ print 6.</summary>
    internal const string X1 = """
        Sub Main()
            Dim x As Integer = 5
            Dim xs As New List(Of Integer)()
            xs.Add(1)
            Dim f As Func(Of Integer) = Function()
                    Dim t As Integer = 0
                    For Each x As Integer In xs
                        t = t + x
                    Next
                    Return t
                End Function
            Dim g As Func(Of Integer) = Function() x
            Console.WriteLine(f() + g())
        End Sub
        """;

    /// <summary>X3: a lambda's own <c>Catch ex</c> hides the creator's <c>ex</c> (N9). VB rejects it
    /// (BC30616); C#, JavaScript and C++ print 6.</summary>
    internal const string X3 = """
        Sub Main()
            Dim ex As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Try
                        Throw New Exception("b")
                    Catch ex As Exception
                        Return 1
                    End Try
                    Return 0
                End Function
            Dim g As Func(Of Integer) = Function() ex
            Console.WriteLine(f() + g())
        End Sub
        """;

    /// <summary>The ten programs that RUN on the by-copy fallback with VB's output (ruling D1: "the H′
    /// fallback set (10: iterator ×3, MyBase.M() ×2, D15, D07b, the N9 pair incl. E20, two-type Catch)"),
    /// plus X1 and X3 (the N9 backstop's other two shapes). R2 and D01 are one program, as are R14 and D06.</summary>
    internal static IEnumerable<CppClosureProgram> Fallback()
    {
        yield return new("R2/D01_iterator", Iterator, "Gen", "2");
        yield return new("D13_iterator_readonly", IteratorReadOnly, "Gen", "10\n20\n30");
        yield return new("R14/D06_mybase_in_lambda", MyBaseInLambda, "Derived.Tag", "11");
        yield return new("D15_generic_class_int_capture", GenericClassIntCapture, "Holder.CountTo", "12");
        yield return new("D07b_when_guard_readonly", WhenGuardReadOnly, "Main", "6\nbig", "hand-derived: VB has no When guard");
        yield return new("E20_lambda_own_local", PerIterationLoopBodyDimProbes.E20, "Main", PerIterationLoopBodyDimProbes.E20Expected);
        yield return new("E12_later_sibling", N9LaterSibling, "Main", "8");
        yield return new("E12_two_clauses_same_name", TwoCatchTypes, "Main", "A:arg1\nI:inv2");
        yield return new("X1_foreach_in_lambda", X1, "Main", "6", "VB rejects (BC30616); C#, JavaScript and C++ print 6");
        yield return new("X3_catch_in_lambda", X3, "Main", "6", "VB rejects (BC30616); C#, JavaScript and C++ print 6");
    }

    // ---- fallback roots that ALSO fail the C++ compiler, for a gap that is not the lambda's ------

    /// <summary>D02 / R3: an Async function with a lambda; the failure is <c>Task.Result</c>.</summary>
    internal const string Async = """
        Async Function Work() As Task(Of Integer)
            Dim k As Integer = 1
            Dim f = Function() k + 1
            Return f()
        End Function
        Sub Main()
            Console.WriteLine(Work().Result)
        End Sub
        """;

    /// <summary>D17: an Async function whose lambda only READS a parameter — the by-copy fallback is sound,
    /// and the program still fails C++ for <c>Task.Result</c>, which has nothing to do with the lambda.</summary>
    internal const string AsyncReadOnly = """
        Async Function Work(k As Integer) As Task(Of Integer)
            Dim f As Func(Of Integer, Integer) = Function(x As Integer) x + k
            Return f(1)
        End Function
        Sub Main()
            Dim t As Task(Of Integer) = Work(5)
            Console.WriteLine(t.Result)
        End Sub
        """;

    /// <summary>D04 / R10: a lambda that captures a value typed by a generic METHOD's type parameter.</summary>
    internal const string GenericCapture = """
        Function Wrap(Of T)(v As T) As T
            Dim f = Function() v
            Return f()
        End Function
        Sub Main()
            Console.WriteLine(Wrap(Of Integer)(5))
        End Sub
        """;

    /// <summary>D16: a lambda whose PARAMETER is typed by a generic method's type parameter. Lowered, and the
    /// environment cannot name <c>T</c>.</summary>
    internal const string GenericLambdaParameter = """
        Function Apply(Of T)(v As T) As T
            Dim id As Func(Of T, T) = Function(x As T) x
            Return id(v)
        End Function
        Sub Main()
            Console.WriteLine(Apply(Of Integer)(9))
        End Sub
        """;

    /// <summary>D09: a lambda in a module-level initializer (a root of its own, never lowered).
    /// ⚠ A property, not a <c>const</c>: this is the ONE program that fires the IR verifier in the suite's default
    /// <c>Throw</c> mode (Invariant P(d), pre-existing on master), and the suite's corpus sweeps
    /// (<c>DeadCodeRemovalOnRealIrTests.ExistingTestPrograms</c> and this ticket's own) harvest every static string field that
    /// holds a program — a field would fail them, a property is out of their reach.</summary>
    internal static string ModuleInitializer => """
        Dim Twice As Func(Of Integer, Integer) = Function(x As Integer) x * 2
        Sub Main()
            Console.WriteLine(Twice(21))
        End Sub
        """;

    /// <summary>L6: a lambda handed to <c>List.ForEach</c> — lowered; the C++ <c>List</c> has no
    /// <c>ForEach</c>.</summary>
    internal const string ListForEach = """
        Sub Main()
            Dim total As Integer = 3
            Dim l As New List(Of Integer)()
            l.Add(10)
            l.Add(20)
            l.ForEach(Sub(x As Integer) total = total + x)
            Console.WriteLine(total)
        End Sub
        """;

    // =========================================================================================
    // THE BOTH-REFUSED SET (ruling D1 case 3): the lowering cannot lower the root AND W2 does not
    // admit the by-copy fallback. D07/R15 are one program; M07 is R12 (BaseConstructorCallCppRefusalTests).
    // =========================================================================================

    /// <summary>D07 / R15_guard: the guard reads a variable the lambda WRITES.</summary>
    internal const string D07 = ClosureLoweringContractPrograms.D07;

    /// <summary>D14: a generic METHOD whose creator writes a captured variable typed <c>T</c>.</summary>
    internal const string GenericMethodCaptureWrite = """
        Function Pick(Of T)(a As T, b As T) As T
            Dim cur As T = a
            Dim f As Func(Of T) = Function() cur
            cur = b
            Return f()
        End Function
        Sub Main()
            Console.WriteLine(Pick(Of Integer)(1, 2))
        End Sub
        """;

    /// <summary>K13: a lambda passes a captured variable ByRef.</summary>
    internal const string ByRefFromALambda = """
        Sub Inc(ByRef v As Integer)
            v = v + 100
        End Sub

        Sub Main()
            Dim n As Integer = 1
            Dim q As Integer = 2
            Dim bump = Sub() Inc(n)
            Dim a As Integer = n + q
            bump()
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b))
        End Sub
        """;

    // ---- ROOT IDENTITY (ruling D5.1) -------------------------------------------------------------------

    /// <summary>RI1: the OUTER lambda is read-only for the lowering's purposes but WRITES a capture; the nested
    /// INNER lambda hits a D9 refusal (a When guard). W2 and the lowering must agree the two lambdas are ONE
    /// root, or the outer's write slips onto <c>[=]</c>: it must be refused, with both reasons.</summary>
    internal const string RI1 = """
        Sub Main()
            Dim n As Integer = 1
            Dim lim As Integer = 5
            Dim outer As Action = Sub()
                                      n = n + 1
                                      Dim inner As Func(Of Integer) = Function() lim + 2
                                      Dim v As Integer = inner()
                                      Select Case v
                                          Case Is > 0 When v > lim
                                              Console.WriteLine("big")
                                          Case Else
                                              Console.WriteLine("small")
                                      End Select
                                  End Sub
            outer()
            Console.WriteLine(n)
        End Sub
        """;

    /// <summary>RI2, RI1's mirror: the INNER lambda writes, the OUTER one hits the refusal.</summary>
    internal const string RI2 = """
        Sub Main()
            Dim n As Integer = 1
            Dim lim As Integer = 5
            Dim outer As Action = Sub()
                                      Dim v As Integer = 7
                                      Select Case v
                                          Case Is > 0 When v > lim
                                              Console.WriteLine("big")
                                          Case Else
                                              Console.WriteLine("small")
                                      End Select
                                      Dim inner As Action = Sub() n = n + 1
                                      inner()
                                  End Sub
            outer()
            Console.WriteLine(n)
        End Sub
        """;

    /// <summary>RI3: the same two-lambda root with NOTHING written — the lowering still refuses it, and W2 admits
    /// it, so it runs on the by-copy fallback. Hand-derived: VB has no When guard.</summary>
    internal const string RI3 = """
        Sub Main()
            Dim lim As Integer = 5
            Dim outer As Action = Sub()
                                      Dim v As Integer = 7
                                      Select Case v
                                          Case Is > 0 When v > lim
                                              Console.WriteLine("big")
                                          Case Else
                                              Console.WriteLine("small")
                                      End Select
                                      Dim inner As Func(Of Integer) = Function() lim * 2
                                      Console.WriteLine(inner())
                                  End Sub
            outer()
        End Sub
        """;

    // =========================================================================================
    // LOWERED ROWS and the other falsifiers (ruling D1, D5)
    // =========================================================================================

    /// <summary>AR1: an arity-10 Func (9 parameters) that WRITES a capture — refused under H (MSIL's cap) and
    /// lowered under H′ (C++ has no arity cap). vbc: 10, 20, 20.</summary>
    internal const string AR1 = """
        Sub Main()
            Dim total As Integer = 0
            Dim g As Func(Of Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer, Integer) = Function(a As Integer, b As Integer, c As Integer, d As Integer, e As Integer, f As Integer, h As Integer, i As Integer, j As Integer)
                                                                                                                               total = total + a + j
                                                                                                                               Return total
                                                                                                                           End Function
            Console.WriteLine(g(1, 2, 3, 4, 5, 6, 7, 8, 9))
            Console.WriteLine(g(1, 2, 3, 4, 5, 6, 7, 8, 9))
            Console.WriteLine(total)
        End Sub
        """;

    /// <summary>EX1 (D5.2): a lambda declared in a For Each, an Exit For inside a Try/Finally, statements after
    /// the loop. The tail must run and the Finally exactly once. vbc: after loop 3 / finally / 12 / 22 / 32.</summary>
    internal const string EX1 = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(1)
            items.Add(2)
            items.Add(3)
            items.Add(4)
            Dim fs As New List(Of Func(Of Integer))()
            Dim total As Integer = 0
            Try
                For Each x As Integer In items
                    Dim y As Integer = x * 10
                    fs.Add(Function() y + total)
                    If x = 3 Then Exit For
                    total = total + 1
                Next
                Console.WriteLine("after loop " & CStr(fs.Count))
            Finally
                Console.WriteLine("finally")
            End Try
            For Each f As Func(Of Integer) In fs
                Console.WriteLine(f())
            Next
        End Sub
        """;

    /// <summary>EX2 (D5.2): a Return from the loop body with the lambda alive. vbc: 46, -1.</summary>
    internal const string EX2 = """
        Function Find(limit As Integer) As Func(Of Integer)
            Dim acc As Integer = 0
            For i As Integer = 1 To 10
                Dim k As Integer = i * i
                acc = acc + k
                If acc > limit Then
                    Return Function() acc + k
                End If
            Next
            Return Function() -1
        End Function
        Sub Main()
            Dim f As Func(Of Integer) = Find(20)
            Console.WriteLine(f())
            Console.WriteLine(Find(1000)())
        End Sub
        """;

    /// <summary>EX3: EX1 without the Try/Finally — a declaring For Each, a per-iteration <c>Dim</c> with copy-forward,
    /// an <c>Exit For</c>, and statements after the loop. vbc: tail 3, 2, 5, 9.</summary>
    internal const string EX3 = """
        Sub Main()
            Dim items As New List(Of Integer)()
            items.Add(1)
            items.Add(2)
            items.Add(3)
            items.Add(4)
            items.Add(5)
            Dim fs As New List(Of Func(Of Integer))()
            For Each n As Integer In items
                Dim x As Integer
                x = x + n
                fs.Add(Function() x + n)
                If n = 3 Then Exit For
            Next
            Console.WriteLine("tail " & CStr(fs.Count))
            For Each f As Func(Of Integer) In fs
                Console.WriteLine(f())
            Next
        End Sub
        """;

    /// <summary>CX1 (D5.3): the captured Catch variable's Message is read AFTER the Catch exits. vbc: seen: bad state.</summary>
    internal const string CX1 = """
        Sub Main()
            Dim f As Func(Of String) = Nothing
            Try
                Throw New InvalidOperationException("bad state")
            Catch ex As InvalidOperationException
                f = Function() "seen: " & ex.Message
            End Try
            Console.WriteLine(f())
        End Sub
        """;

    /// <summary>CX2 (D5.3): <c>Throw ex</c> of the captured variable rethrows the SAME exception — no slicing to
    /// the base. vbc: typed: original.</summary>
    internal const string CX2 = """
        Sub Main()
            Dim rethrow As Action = Nothing
            Try
                Throw New InvalidOperationException("original")
            Catch ex As InvalidOperationException
                rethrow = Sub() Throw ex
            End Try
            Try
                rethrow()
            Catch e2 As InvalidOperationException
                Console.WriteLine("typed: " & e2.Message)
            Catch e3 As Exception
                Console.WriteLine("base: " & e3.Message)
            End Try
        End Sub
        """;

    /// <summary>CX2b: CX2 with a first Catch of ANOTHER type on the outer ladder. The captured exception, rethrown, must be
    /// handled by the clause of ITS type: a copy sliced to <c>std::runtime_error</c> (the by-copy init-capture's way) lands in
    /// the first clause's fallback handler instead, which CX2's single clause cannot tell. vbc: typed: original.</summary>
    internal const string CX2b = """
        Sub Main()
            Dim rethrow As Action = Nothing
            Try
                Throw New InvalidOperationException("original")
            Catch ex As InvalidOperationException
                rethrow = Sub() Throw ex
            End Try
            Try
                rethrow()
            Catch ea As ArgumentException
                Console.WriteLine("wrong clause: " & ea.Message)
            Catch eb As InvalidOperationException
                Console.WriteLine("typed: " & eb.Message)
            End Try
        End Sub
        """;

    /// <summary>CX3 (D5.3): two Catch clauses of different types, a lambda in each — the by-copy fallback.
    /// vbc: A:arg1, I:inv2.</summary>
    internal const string CX3 = """
        Sub Main()
            Dim fs As New List(Of Func(Of String))()
            For i As Integer = 1 To 2
                Try
                    If i = 1 Then Throw New ArgumentException("arg" & CStr(i))
                    Throw New InvalidOperationException("inv" & CStr(i))
                Catch ex As ArgumentException
                    fs.Add(Function() "A:" & ex.Message)
                Catch ex As InvalidOperationException
                    fs.Add(Function() "I:" & ex.Message)
                End Try
            Next
            For Each f As Func(Of String) In fs
                Console.WriteLine(f())
            Next
        End Sub
        """;

    /// <summary>NM1 (D5.6): a user class, a user class named like the holder, and a user function spelled like
    /// the lowering's own names. It must compile and run (ADR-0018: names are minted, never chosen by
    /// spelling). vbc: 19.</summary>
    internal const string NM1 = """
        Class c__Env0
            Public V As Integer = 7
        End Class
        Class BasicLangClosures
            Public W As Integer = 8
        End Class
        Function blTarget(x As Integer) As Integer
            Return x + 1
        End Function
        Sub Main()
            Dim blArg0 As Integer = 2
            Dim e As New c__Env0()
            Dim h As New BasicLangClosures()
            Dim n As Integer = 1
            Dim bump As Action = Sub() n = n + e.V + h.W + blTarget(blArg0)
            bump()
            Console.WriteLine(n)
        End Sub
        """;

    /// <summary>M01_L5: a lowered program through both shipping entry points. vbc: seed, 12.</summary>
    internal const string M01_L5 = """
        Function Seed(v As Integer) As Integer
            Console.WriteLine("seed")
            Return v
        End Function

        Sub Main()
            Dim x As Integer = Seed(1)
            Dim bump = Sub() x = x + 1
            Dim s As Integer = 0
            For i As Integer = 1 To 3
                s = s + x * 2
                bump()
            Next
            Console.WriteLine(CStr(s))
        End Sub
        """;
}
