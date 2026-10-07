using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Tasks #219 and #249, RUN. Every VB `Function F` and every property `Get` has an IMPLICIT RETURN VARIABLE, spelled like the procedure and typed as its return type. `F = v` assigns it, and the procedure
//  returns it when it ends without a `Return`: falling off the end, `Exit Function`, `Exit Property`. BasicLang had no such variable: `F = v` was a store into the function's own name, which did not compile on C#
//  (CS1656) or C++, overwrote the function or printed 0 on JavaScript (#249), and printed 0 or threw on MSIL; and `P = v` inside a Get was a property SET (MSIL MissingMethodException set_P, #219; C# CS0200).
//  Now the analyzer binds a bare reference to a Local of the procedure's scope (`SemanticAnalyzer.AsReturnVariable`), and the IR builder stores it in a carrier, `__ret`, reached through the analyzer's declaration
//  (by reference, never by the name), and returns the carrier at the end of the procedure, at `Exit Function` and at `Exit Property`.
//
//  What stays the PROCEDURE, in any spelling: a callee (`F(n - 1)`, and `F()` too), an `AddressOf` operand, a name standing alone as a statement. A Sub has no return variable. In a Get, only the Get's own body has
//  it: `P = 7` in another member of the class is still a property write.
//
//  ORACLE: vbc. Each program below is the implementer's probes (`S/t219/probes`, `probes2`: F01-F12, P01-P03, K01-K02, X3, Y1, Y4, Y7, Y9-Y12, Z2, Z5, Z7) joined into one program per test where they share a
//  shape, each function renamed so they can share a file; the expected text is the output of the SDK's vbc on the program wrapped in a VB Module (`S/t136/tools/vbv2.py`), never a backend's. The test-writer
//  measured every program through vbc before writing the row.
//
//  ENTRY POINTS: every probe goes through the spawned CLI, the CLI with `--optimize` and `BasicCompiler.CompileProjectFiles` (aggressive), on all four backends: `TempExec.AssertMatchesInEveryEntryPoint`.
//  ⛔ Every probe is `HangSafe` (a loop probe's C# leg runs in a child process with a time limit, `CSharpProcessRunner`, #256). A backend whose tool is missing (a C++ compiler, Node, ilasm) SKIPS its cells; the
//  test is ignored only when no cell could run.
//  ⚠ Named "...ExecutionTests" and runs JavaScript under Node: it is in `JsExecutionTierRosterTests`' roster.
//
//  MUTANTS (each built from a plain source copy of the fix with ONE change and run against a copy of the test output with its BasicLang.dll swapped; recipes `S/t219/tools/mut219b.py`). The tests that go RED, measured:
//    M1 a CALL or an `AddressOf` operand binds the return variable (the `_procedureNameUse` exclusion dropped) -> `M1_AddressOf…` (the delegate is over the carrier) and, in the diagnostics fixture, `ARecursiveAsyncCall_IsNotRefused…`
//    M2 falling off the end of a Function returns the default, not the variable -> 8 of 11: `M2_FallingOff…`, `M3_ExitFunction…` (its `Early(0)` path), `ARecursiveCall…`, `AFunctionInAClass…`, `ForLoopOver…`, `M4_ALambdaInAFunction…`, `M1_AddressOf…`, `Controls…`
//    M3 `Exit Function` / `Exit Property` and the end of a Get ignore the carrier and return the default -> 6 of 11 (+ the moved `PropertyAccessExecutionTests.E09_…`): `M3_ExitFunction…`, `M3_AGet_ExitProperty…`, `M5_AGet…`, `M4_ALambdaInAReadOnlyGet…`, `ARecursiveCall…` (`Walk`), `Controls…`
//    M4 a lambda written in the procedure does not see its return variable -> exactly `M4_ALambdaInAFunction…` and `M4_ALambdaInAReadOnlyGet…`
//    M5 a property Get has no return variable -> 4 of 11 (+ the moved E09 pin and the fast `PropertyAccessDiagnosticsTests.Legal_AssignmentToPInsideItsOwnGet_…`): `M5_AGet…`, `M3_AGet_ExitProperty…`, `M4_ALambdaInAReadOnlyGet…`, `Controls…`
//    M6 a CALLEE is visited as a plain name (its exclusion dropped): a recursive Async call would mark the variable and be refused -> ONLY `ImplicitReturnVariableDiagnosticsTests.ARecursiveAsyncCall_IsNotRefused…` (no execution cell sees it)
//
//  ⛔ KNOWN GAPS — listed, deliberately NOT tested (asserting one would pin a defect, or the diagnostic does not exist):
//    * F06: a String Function with no assignment at all returns "", not Nothing. The fall-off default is `CreateDefaultValue` and was never Nothing for a String; C#, JavaScript and MSIL print `[]` for vbc's
//      `True` on `r Is Nothing`. Measured identical before the change.
//    * `Exit Function` inside a LAMBDA (Z6): the lambda's own return is a different return from the procedure's, and vbc's answer (2) is not what any backend prints. Lambdas are untouched by #219.
//    * BC30290 (`Dim F` inside Function F) and BC30067 (`Exit Function` inside a Property) are not reported: BasicLang models neither.
//    * A bare parameterless call STATEMENT is dropped (#289): `F` alone on a line calls F in VB, and BasicLang emits nothing for it. The statement is the procedure (never the variable), but it does not run.
//    * A user local spelled like the CARRIER (`Dim __ret As Integer = 100` in a Function that also uses its return variable, `Total = __ret + n`) merges with it: the user's variable and the carrier are ONE IR
//      variable (C# and JavaScript print 0, C++ and MSIL do not compile; vbc prints 105). Measured by the test-writer. The same holds for `__with` and a With block (pre-existing): the double-underscore carriers are
//      reserved against MINTED temps (ADR-0018), never against a user spelling the carrier's name.
//    * C++ and JavaScript have no `String.ToUpper` (X2: `F = F.ToUpper()` fails identically with a local), and JavaScript refuses a ByRef parameter (Y3: BL7002, `Bump(F)`).
// ================================================================================================

/// <summary>#219 / #249 — a Function's and a property Get's implicit return variable, on C#, C++, JavaScript and MSIL.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the g++, Node, ilasm and C# child runs share the machine with the spawned CLI
public class ImplicitReturnVariableExecutionTests
{
    /// <summary>
    /// One group of single-file probes, each on every backend it runs on, through every entry point. A failing cell is collected, not thrown, so every other one still reports and the failure text names
    /// the probe, the backend and the entry point. (A copy of the helper `AutoPropertyInitializerExecutionTests` keeps: each fixture owns its own.)
    /// </summary>
    private static void AssertSingleFile(params TempProbe[] probes)
    {
        var failures = new List<string>();
        int ran = 0, skipped = 0;
        foreach (var probe in probes)
        {
            foreach (var backend in TempExec.Backends(probe.Agrees))
            {
                try
                {
                    TempExec.AssertMatchesInEveryEntryPoint(backend, probe.Source, probe.Vb, probe.Id, probe.HangSafe);
                    ran++;
                }
                catch (IgnoreException)
                {
                    skipped++;
                }
                catch (AssertionException ex)
                {
                    ran++;
                    failures.Add(ex.Message);
                }
            }
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
        if (ran == 0) Assert.Ignore($"no execution tool on this machine ({skipped} cells skipped).");
    }

    /// <summary>
    /// (1) M2. `F = 5` and nothing else: the Function returns the variable it fell off the end holding (F01). `F += 1` READS the variable, and `F = F * 2` too (F04: 3 -> 8). A loop accumulating into it (F10 `Sum`), and
    /// a Boolean one set on one path only (`Found(2)` falls off with the default False). M2 (the fall-off returns the default) prints 0 / 0 / 0 / False / False.
    /// </summary>
    [Test]
    public void M2_FallingOffTheEnd_ReturnsTheVariable_AndACompoundWriteReadsIt()
        => AssertSingleFile(ImplicitReturnVariableProbes.T1_FallOff);

    /// <summary>
    /// (2) M3. `Exit Function` returns the variable (F02: `F = 5 : Exit Function` gives 5, falling on to `F = 9` gives 9); a `Return` wins over an earlier `F = 5` (F03: 7, and 5 on the path that does not return);
    /// `Exit Function` inside a Try with a Finally (F11: "fin" printed once per call, 5 and -1). M3 (an exit ignores the carrier) prints the default 0 for the `Exit Function` calls.
    /// </summary>
    [Test]
    public void M3_ExitFunction_ReturnsTheVariable_AndAReturnStillWins()
        => AssertSingleFile(ImplicitReturnVariableProbes.T2_ExitFunction);

    /// <summary>
    /// (3) A name that is CALLED stays the procedure (vbc: parentheses always call, even `F()` with no arguments), beside a bare `F` that is the variable: `Fact = n * Fact(n - 1)` (F05: 120), a zero-argument
    /// `Countdown = Countdown() + 10` (F09: 21; before, `F()` on its own stack-overflowed in the measuring probe X1), `Tri = Tri(x - 1) + Tri` with the call and the variable in ONE expression (Z7: 10), and an
    /// `Exit Function` guard with a recursive call after it (X3: 24).
    /// </summary>
    [Test]
    public void ARecursiveCall_StaysACall_BesideTheVariable_InEveryShape()
        => AssertSingleFile(ImplicitReturnVariableProbes.T3_Recursion);

    /// <summary>
    /// (4) M1. `AddressOf F` inside F names the PROCEDURE (Y7), not the variable: the delegate is made over the method and `F = 2` still returns 2. M1 (the `AddressOf` operand binds the variable) lowers the
    /// operand to the carrier, a delegate over an Integer local: it does not compile or run on any backend.
    /// </summary>
    [Test]
    public void M1_AddressOfTheFunctionsOwnName_IsTheProcedure_NotTheVariable()
        => AssertSingleFile(ImplicitReturnVariableProbes.T4_AddressOf);

    /// <summary>
    /// (5) A Function in a class (`G = _v + k`, F07: 15), a Shared Function (`H = k * 3`, F08: 12), and a Function in a Module block called bare and qualified (`F()` and `M.F()`, Y12: 6, 6 — the qualified
    /// spelling is the procedure, never the variable).
    /// </summary>
    [Test]
    public void AFunctionInAClass_ASharedFunction_AndAModuleFunction_HaveTheVariable()
        => AssertSingleFile(ImplicitReturnVariableProbes.T5a_ClassAndShared, ImplicitReturnVariableProbes.T5b_Module);

    /// <summary>
    /// (6) M5. A ReadOnly Get that assigns its own name and falls off the end returns it (P01 / E09: `P = _v + 1`, 43; before, MSIL threw MissingMethodException set_P and C# gave CS0200), and a READ of the name
    /// inside the Get is the variable too (Y4: `Console.WriteLine(Q)` prints the default 0, then `Q = 4 : Q += 1` returns 5). M5 (a Get has no return variable) lowers `P = v` back to a property set.
    /// </summary>
    [Test]
    public void M5_AGet_ReturnsTheVariableItFellOffHolding_AndAReadOfItIsTheVariable()
        => AssertSingleFile(ImplicitReturnVariableProbes.T6_GetFallOff);

    /// <summary>
    /// (7) M3 on a Get. `Exit Property` PARSES (P02, before: a parse failure) and returns the variable (42, then -1 once the field is 5: the early exit is not taken); a Get mixing `P = 1` with
    /// `Return _v` (P03: the Return wins, 42; with `_v = 3` the Get falls off holding 1 + 2 = 3); and `Exit Property` in a Set leaves the Set (Z5: "set 5", never the line after it). M3 returns the default 0 from
    /// every exit of the Get.
    /// </summary>
    [Test]
    public void M3_AGet_ExitProperty_AndAReturnMixedWithTheVariable()
        => AssertSingleFile(ImplicitReturnVariableProbes.T7_GetExit);

    /// <summary>
    /// (8) M4. A lambda written in the body READS the variable (Y1: `Function() Reads + 1` prints 4, the Function still returns 3) and WRITES it (Y11: `Sub() Writes = 8` makes the Function return 8). vbc treats the
    /// variable as an ordinary local the lambda captures. M4 (lambdas are excluded) binds the lambda's name to the procedure: a delegate arithmetic error, or the wrong value.
    /// </summary>
    [Test]
    public void M4_ALambdaInAFunction_ReadsAndWritesTheVariable()
        => AssertSingleFile(ImplicitReturnVariableProbes.T8_Lambda);

    /// <summary>
    /// (9) M4 and M5 together, and the refusal #219 removed. A lambda in a ReadOnly Get that WRITES `P` (Y10: `Sub() P = 9`, then a read through `Function() P + 1`: 10, and the Get returns 9). #178 refused the write with
    /// BC30526 (a write to a ReadOnly property); vbc accepts it, because `P` is the Get's return variable.
    /// </summary>
    [Test]
    public void M4_ALambdaInAReadOnlyGet_WritesTheVariable_AsVbcAccepts()
        => AssertSingleFile(ImplicitReturnVariableProbes.T9_GetLambda);

    /// <summary>
    /// (10) `For F = 1 To 3` DRIVES the variable (Z2: the loop leaves it at 4), and a name spelled in another case is still the variable: `twice = x * 2` in `Twice` (F12; #249's MEr / MErc: the binding records
    /// the DECLARED spelling, ADR-0013), and `Name = Name & "b"`.
    /// </summary>
    [Test]
    public void ForLoopOverTheVariable_AndANameInAnotherCase_AreTheVariable()
        => AssertSingleFile(ImplicitReturnVariableProbes.T10_ForAndCase);

    /// <summary>
    /// (11) CONTROLS, which must not move. A local named like ANOTHER function (K01: `Dim Other` inside F hides `Other` and nothing is an implicit variable), a Sub that recurses and writes a global (K02), and `P = 7`
    /// in ANOTHER member of a class whose property has a Get (Y9: a property SET, not the Get's variable; `Q = P` reads the property back: 7, 7).
    /// </summary>
    [Test]
    public void Controls_ALocalNamedLikeAFunction_ASub_AndAPropertyWrittenFromAnotherMember_AreUnchanged()
        => AssertSingleFile(ImplicitReturnVariableProbes.T11_Controls);
}

/// <summary>
/// The #219 / #249 probes: the implementer's `S/t219/probes` / `probes2` programs, each function renamed so several share a file, and every expected value is vbc's OWN output for it (see the fixture header).
/// </summary>
internal static class ImplicitReturnVariableProbes
{
    /// <summary>Every probe is HangSafe (#256): its C# leg runs in a time-limited child process.</summary>
    private static TempProbe P(string id, string source, string vb, Bk agrees = Bk.All) => new(id, source, vb, agrees, HangSafe: true);

    /// <summary>F01, F04, F10: falling off the end; a compound write reads the variable; a loop accumulates; a Boolean set on one path.</summary>
    internal static readonly TempProbe T1_FallOff = P("T1_falloff", """
        Function Five() As Integer
            Five = 5
        End Function
        Function Bump(n As Integer) As Integer
            Bump = n
            Bump += 1
            Bump = Bump * 2
        End Function
        Function Sum(n As Integer) As Integer
            For i As Integer = 1 To n
                Sum = Sum + i
            Next
        End Function
        Function Found(x As Integer) As Boolean
            If x = 3 Then Found = True
        End Function
        Sub Main()
            Console.WriteLine(Five())
            Console.WriteLine(Bump(3))
            Console.WriteLine(Sum(4))
            Console.WriteLine(Found(3))
            Console.WriteLine(Found(2))
        End Sub
        """, "5\n8\n10\nTrue\nFalse");

    /// <summary>F02, F03, F11: `Exit Function` returns the variable; a `Return` wins over it; `Exit Function` through a Try / Finally.</summary>
    internal static readonly TempProbe T2_ExitFunction = P("T2_exitfunction", """
        Function Early(x As Integer) As Integer
            Early = 5
            If x > 0 Then Exit Function
            Early = 9
        End Function
        Function Wins(x As Integer) As Integer
            Wins = 5
            If x > 0 Then
                Return 7
            End If
        End Function
        Function Safe(x As Integer) As Integer
            Try
                Safe = 10 \ x
                Exit Function
            Catch ex As DivideByZeroException
                Safe = -1
            Finally
                Console.WriteLine("fin")
            End Try
        End Function
        Sub Main()
            Console.WriteLine(Early(1))
            Console.WriteLine(Early(0))
            Console.WriteLine(Wins(1))
            Console.WriteLine(Wins(0))
            Console.WriteLine(Safe(2))
            Console.WriteLine(Safe(0))
        End Sub
        """, "5\n9\n7\n5\nfin\n5\nfin\n-1");

    /// <summary>F05, F09, Z7, X3: a recursive CALL beside the variable, with arguments, with none, in the same expression, and after an `Exit Function`.</summary>
    internal static readonly TempProbe T3_Recursion = P("T3_recursion", """
        Dim counter As Integer = 3
        Function Fact(n As Integer) As Integer
            If n <= 1 Then
                Fact = 1
            Else
                Fact = n * Fact(n - 1)
            End If
        End Function
        Function Countdown() As Integer
            counter -= 1
            If counter > 0 Then
                Countdown = Countdown() + 10
            Else
                Countdown = 1
            End If
        End Function
        Function Tri(x As Integer) As Integer
            Tri = x
            If x > 0 Then
                Tri = Tri(x - 1) + Tri
            End If
        End Function
        Function Walk(n As Integer) As Integer
            If n = 0 Then
                Walk = 1
                Exit Function
            End If
            Walk = n * Walk(n - 1)
        End Function
        Sub Main()
            Console.WriteLine(Fact(5))
            Console.WriteLine(Countdown())
            Console.WriteLine(Tri(4))
            Console.WriteLine(Walk(4))
        End Sub
        """, "120\n21\n10\n24");

    /// <summary>Y7, verbatim: `AddressOf F` inside F is the procedure.</summary>
    internal static readonly TempProbe T4_AddressOf = P("T4_addressof", """
        Function F() As Integer
            Dim d As Func(Of Integer) = AddressOf F
            F = 2
        End Function
        Sub Main()
            Console.WriteLine(F())
        End Sub
        """, "2");

    /// <summary>F07 + F08: an instance Function and a Shared Function.</summary>
    internal static readonly TempProbe T5a_ClassAndShared = P("T5a_class_shared", """
        Class C
            Private _v As Integer = 10
            Public Function G(k As Integer) As Integer
                G = _v + k
            End Function
            Public Shared Function H(k As Integer) As Integer
                H = k * 3
            End Function
        End Class
        Sub Main()
            Dim o As New C()
            Console.WriteLine(o.G(5))
            Console.WriteLine(C.H(4))
        End Sub
        """, "15\n12");

    /// <summary>Y12, verbatim: a Module member, called bare and qualified.</summary>
    internal static readonly TempProbe T5b_Module = P("T5b_module", """
        Module M
            Function F() As Integer
                F = 6
            End Function
            Sub Main()
                Console.WriteLine(F())
                Console.WriteLine(M.F())
            End Sub
        End Module
        """, "6\n6");

    /// <summary>P01 (E09) and Y4: a ReadOnly Get assigns its own name; another reads it before assigning.</summary>
    internal static readonly TempProbe T6_GetFallOff = P("T6_get_falloff", """
        Class Ctx
            Private _v As Integer = 42
            Public ReadOnly Property P As Integer
                Get
                    P = _v + 1
                End Get
            End Property
            Public ReadOnly Property Q As Integer
                Get
                    Console.WriteLine(Q)
                    Q = 4
                    Q += 1
                End Get
            End Property
        End Class
        Sub Main()
            Dim o As New Ctx()
            Console.WriteLine(o.P)
            Console.WriteLine(o.Q)
        End Sub
        """, "43\n0\n5");

    /// <summary>P02, P03, Z5: `Exit Property` in a Get and in a Set; a Get that mixes `P = v` with `Return`.</summary>
    internal static readonly TempProbe T7_GetExit = P("T7_get_exit", """
        Class A
            Private _v As Integer = 42
            Public Property P As Integer
                Get
                    P = _v
                    If _v > 40 Then Exit Property
                    P = -1
                End Get
                Set(value As Integer)
                    _v = value
                    Console.WriteLine("set " & value)
                    Exit Property
                    Console.WriteLine("never")
                End Set
            End Property
        End Class
        Class B
            Private _v As Integer = 42
            Public ReadOnly Property P As Integer
                Get
                    P = 1
                    If _v > 40 Then Return _v
                    P = P + 2
                End Get
            End Property
            Public Sub SetV(v As Integer)
                _v = v
            End Sub
        End Class
        Sub Main()
            Dim oa As New A()
            Console.WriteLine(oa.P)
            oa.P = 5
            Console.WriteLine(oa.P)
            Dim ob As New B()
            Console.WriteLine(ob.P)
            ob.SetV(3)
            Console.WriteLine(ob.P)
        End Sub
        """, "42\nset 5\n-1\n42\n3");

    /// <summary>Y1 and Y11: a lambda in a Function reads, and writes, its return variable.</summary>
    internal static readonly TempProbe T8_Lambda = P("T8_lambda", """
        Function Reads() As Integer
            Reads = 3
            Dim g As Func(Of Integer) = Function() Reads + 1
            Console.WriteLine(g())
        End Function
        Function Writes() As Integer
            Writes = 3
            Dim s As Action = Sub() Writes = 8
            s()
        End Function
        Sub Main()
            Console.WriteLine(Reads())
            Console.WriteLine(Writes())
        End Sub
        """, "4\n3\n8");

    /// <summary>Y10, verbatim: a lambda in a ReadOnly Get writes and reads the Get's variable.</summary>
    internal static readonly TempProbe T9_GetLambda = P("T9_get_lambda", """
        Class C
            Public ReadOnly Property P As Integer
                Get
                    P = 2
                    Dim g As Func(Of Integer) = Function() P + 1
                    Dim s As Action = Sub() P = 9
                    s()
                    Console.WriteLine(g())
                End Get
            End Property
        End Class
        Sub Main()
            Console.WriteLine(New C().P)
        End Sub
        """, "10\n9");

    /// <summary>Z2 and F12: a `For` over the variable; `twice = …` in `Twice`; `Name = Name &amp; "b"`.</summary>
    internal static readonly TempProbe T10_ForAndCase = P("T10_for_and_case", """
        Function Name() As String
            Name = "a"
            Name = Name & "b"
        End Function
        Function Twice(x As Integer) As Integer
            twice = x * 2
        End Function
        Function Loop3() As Integer
            For Loop3 = 1 To 3
            Next
        End Function
        Sub Main()
            Console.WriteLine(Name())
            Console.WriteLine(Twice(21))
            Console.WriteLine(Loop3())
        End Sub
        """, "ab\n42\n4");

    /// <summary>K01, K02, Y9: the controls — no implicit variable where there is none.</summary>
    internal static readonly TempProbe T11_Controls = P("T11_controls", """
        Dim acc As Integer = 0
        Function Other() As Integer
            Return 3
        End Function
        Function F() As Integer
            Dim Other As Integer = 4
            Return Other + 1
        End Function
        Sub Walk(n As Integer)
            If n <= 0 Then Exit Sub
            acc += n
            Walk(n - 1)
        End Sub
        Class C
            Private _n As Integer = 0
            Public Property P As Integer
                Get
                    P = _n
                End Get
                Set(value As Integer)
                    _n = value
                End Set
            End Property
            Public Function Q() As Integer
                P = 7
                Q = P
            End Function
        End Class
        Sub Main()
            Console.WriteLine(F())
            Console.WriteLine(Other())
            Walk(4)
            Console.WriteLine(acc)
            Dim oc As New C()
            Console.WriteLine(oc.Q())
            Console.WriteLine(oc.P)
        End Sub
        """, "5\n3\n10\n7\n7");
}
