using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.LSP;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #174 — VB's lambda-boundary diagnostics, reported by the FRONT END before any backend
/// runs: BC36639 ("'ByRef' parameter 'n' cannot be used in a lambda expression."), BC30616
/// ("Variable 'x' hides a variable in an enclosing block."), BC30734 ("'x' is already declared
/// as a parameter of this method.") and BC36667 ("Variable 'a' is already declared as a
/// parameter of this or an enclosing lambda expression.").
///
/// <para>Fast subset — <see cref="Analyze"/> parses and semantically analyzes only, no IR, no
/// backend, no process; matches the house style set by <c>PropertyAccessDiagnosticsTests</c>
/// (task #178). See <c>LambdaBoundaryDiagnosticsExecutionTests</c> (Integration) for the same
/// probes taken through the CLI (every target), a Release <c>.blproj</c> build, and to a running
/// program where VB accepts the shape.</para>
///
/// <para>Every probe source and its VB verdict is transcribed verbatim from the implementer's own
/// measured probes (<c>S/t174/probes</c>, <c>/edge</c>, <c>/blk</c>, <c>/bs</c>) and re-verified
/// directly against this build's <c>BasicLang.dll</c> before being pinned here — never re-derived
/// from what the analyzer under test prints.</para>
/// </summary>
[TestFixture]
public class LambdaBoundaryDiagnosticsTests
{
    // ====================================================================================
    // Shared helpers.
    // ====================================================================================

    private static readonly string[] LambdaBoundaryCodes = { "BC36639", "BC30616", "BC30734", "BC36667" };

    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));

        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    /// <summary>Asserts exactly the diagnostic's promise: the CODE, the MESSAGE, and the
    /// LINE/COLUMN it lands on — the house style set by <c>PropertyAccessDiagnosticsTests.
    /// AssertRefused</c>.</summary>
    private static void AssertRefused(string source, string code, string message, int line, int column)
    {
        var errors = Analyze(source);
        var match = errors.FirstOrDefault(e => e.ErrorCode == code);
        Assert.That(match, Is.Not.Null,
            $"expected {code}; got: " + string.Join(" | ", errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
        Assert.That(match!.Message, Is.EqualTo(message), "message: " + match.Message);
        Assert.That(match.Line, Is.EqualTo(line), "diagnostic line: " + match.Message);
        Assert.That(match.Column, Is.EqualTo(column), "diagnostic column: " + match.Message);
    }

    /// <summary>Asserts NONE of the four lambda-boundary codes fire anywhere in the program — the
    /// mutant-killing "legal" half of the contract (kills M4, M6; proves the #231/#217 scope
    /// boundary is not crossed).</summary>
    private static void AssertNoLambdaBoundaryDiagnostic(string source)
    {
        var errors = Analyze(source);
        var stray = errors.Where(e => LambdaBoundaryCodes.Contains(e.ErrorCode)).ToList();
        Assert.That(stray, Is.Empty,
            "expected no lambda-boundary diagnostic; got: "
            + string.Join(" | ", stray.Select(e => $"{e.ErrorCode}:{e.Message}")));
    }

    /// <summary>Both entry points, per CLAUDE.md: the same source refused (or not) through
    /// <c>BasicCompiler.CompileProjectFiles</c> — the IDE's own build path, and a genuinely
    /// different code path than the single-file <c>Parser</c>/<c>SemanticAnalyzer</c> pair
    /// <see cref="Analyze"/> drives. No backend runs here (<c>CompileProjectFiles</c> stops at the
    /// combined IR), so this needs no process and stays in the fast subset.</summary>
    private static List<SemanticError> AnalyzeViaProjectEntryPoint(string source)
    {
        var compiler = new BasicCompiler(new CompilerOptions());
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bl-t174-proj-" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var path = System.IO.Path.Combine(dir, "Main.bas");
            System.IO.File.WriteAllText(path, source);
            var result = compiler.CompileProjectFiles(new List<string> { path });
            return result.AllErrors.ToList();
        }
        finally { try { System.IO.Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    private static void AssertRefusedViaProjectEntryPoint(string source, string code)
    {
        var errors = AnalyzeViaProjectEntryPoint(source);
        Assert.That(errors.Any(e => e.ErrorCode == code), Is.True,
            $"CompileProjectFiles (the IDE's own build path) must refuse this with {code} too; got: "
            + string.Join(" | ", errors.Select(e => $"{e.ErrorCode}:{e.Message}")));
    }

    // ====================================================================================
    // BC36639 — a ByRef parameter referenced (read or write) inside a lambda.
    // ====================================================================================

    // R1 — a WRITE, `n = n + 1` inside a Sub() lambda in the Sub that declares `ByRef n`.
    private const string R1 = """
        Sub Bump(ByRef n As Integer)
            Dim f As Action = Sub() n = n + 1
            f()
        End Sub
        Sub Main()
            Dim a As Integer = 1
            Bump(a)
            Console.WriteLine(a)
        End Sub
        """;

    [Test]
    public void R1_ByRefParameterWrittenInsideLambda_IsRefused_BC36639() => AssertRefused(
        R1, "BC36639", "BC36639: 'ByRef' parameter 'n' cannot be used in a lambda expression.", 2, 29);

    [Test]
    public void R1_AlsoRefusedThroughTheProjectEntryPoint() =>
        AssertRefusedViaProjectEntryPoint(R1, "BC36639");

    // R2 — a pure READ, `n * 2` inside a Function() lambda.
    private const string R2 = """
        Function Twice(ByRef n As Integer) As Integer
            Dim g As Func(Of Integer) = Function() n * 2
            Return g()
        End Function
        Sub Main()
            Dim a As Integer = 4
            Console.WriteLine(Twice(a))
        End Sub
        """;

    [Test]
    public void R2_ByRefParameterReadInsideLambda_IsRefused_BC36639() => AssertRefused(
        R2, "BC36639", "BC36639: 'ByRef' parameter 'n' cannot be used in a lambda expression.", 2, 44);

    // R4 — a NESTED lambda: the ByRef parameter is read two lambda levels down.
    private const string R4 = """
        Function Outer(ByRef n As Integer) As Integer
            Dim g As Func(Of Func(Of Integer)) = Function() Function() n + 1
            Dim h As Func(Of Integer) = g()
            Return h()
        End Function
        Sub Main()
            Dim a As Integer = 4
            Console.WriteLine(Outer(a))
        End Sub
        """;

    [Test]
    public void R4_ByRefParameterReadInsideNestedLambda_IsRefused_BC36639() => AssertRefused(
        R4, "BC36639", "BC36639: 'ByRef' parameter 'n' cannot be used in a lambda expression.", 2, 64);

    // R5 — a CLASS METHOD's own ByRef parameter, read inside a lambda in the same method.
    private const string R5 = """
        Class Box
            Public Function Twice(ByRef n As Integer) As Integer
                Dim g As Func(Of Integer) = Function() n * 2
                Return g()
            End Function
        End Class
        Sub Main()
            Dim b As New Box()
            Dim a As Integer = 4
            Console.WriteLine(b.Twice(a))
        End Sub
        """;

    [Test]
    public void R5_ByRefParameterOfAClassMethod_ReadInsideLambda_IsRefused_BC36639() => AssertRefused(
        R5, "BC36639", "BC36639: 'ByRef' parameter 'n' cannot be used in a lambda expression.", 3, 48);

    // E06 — `For n = 1 To 3` with NO `As` inside a lambda drives the OUTER ByRef `n` (the loop
    // variable's own declaration would otherwise hide it from everything else in the body) — a
    // WRITE, same as R1's, just spelled as a bare For loop instead of an assignment.
    private const string E06ForLoopNoAs = """
        Sub Run(ByRef n As Integer)
            Dim f As Action = Sub()
                    For n = 1 To 3
                    Next
                End Sub
            f()
        End Sub
        Sub Main()
            Dim a As Integer = 0
            Run(a)
            Console.WriteLine(a)
        End Sub
        """;

    [Test]
    public void E06_BareForLoopWithNoAs_InsideLambda_DrivesTheOuterByRefParameter_IsRefused_BC36639() =>
        AssertRefused(E06ForLoopNoAs, "BC36639",
            "BC36639: 'ByRef' parameter 'n' cannot be used in a lambda expression.", 3, 13);

    // ---- Must NOT be refused -----------------------------------------------------------------

    // R3 — the ByRef parameter is copied into a LOCAL before the lambda is created; the lambda
    // never touches the ByRef parameter itself.
    private const string R3 = """
        Function Twice(ByRef n As Integer) As Integer
            Dim copy As Integer = n
            Dim g As Func(Of Integer) = Function() copy * 2
            Return g()
        End Function
        Sub Main()
            Dim a As Integer = 4
            Console.WriteLine(Twice(a))
        End Sub
        """;

    [Test]
    public void R3_ByRefParameterCopiedIntoALocalFirst_IsNotRefused() =>
        AssertNoLambdaBoundaryDiagnostic(R3);

    // R6 — a ByVal parameter, read inside a lambda: never BC36639 at all.
    private const string R6 = """
        Function Twice(ByVal n As Integer) As Integer
            Dim g As Func(Of Integer) = Function() n * 2
            Return g()
        End Function
        Sub Main()
            Console.WriteLine(Twice(4))
        End Sub
        """;

    [Test]
    public void R6_ByValParameter_IsNeverRefused() =>
        AssertNoLambdaBoundaryDiagnostic(R6);

    // R7 — a LAMBDA PARAMETER spelled like the enclosing ByRef parameter SHADOWS it: decided by
    // the resolved SYMBOL (ADR-0013), never by spelling. Whether the shadowing itself should be
    // its own diagnostic (BC36641) is the owner's pending decision, #217 — the interim is
    // shadowing, silently, and #174 must not touch that path (per its own STOP condition).
    private const string R7 = """
        Sub Run(ByRef n As Integer)
            Dim f As Func(Of Integer, Integer) = Function(n) n + 1
            n = f(n)
        End Sub
        Sub Main()
            Dim a As Integer = 1
            Run(a)
            Console.WriteLine(a)
        End Sub
        """;

    [Test]
    public void R7_LambdaParameterShadowsTheByRefParameter_IsNotRefused_Task217Interim() =>
        AssertNoLambdaBoundaryDiagnostic(R7);

    // ====================================================================================
    // BC30616 — a lambda `Dim` hides a local of an enclosing block, of the creator, or of an
    // enclosing lambda (including a `For` control variable and a local `Const`).
    // ====================================================================================

    // N1 — the lambda's own Dim hides the CREATOR's local, inside a nested If block of the lambda.
    private const string N1 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim y As Integer = x
                    If y > 0 Then
                        Dim x As Integer = 10
                        y = y + x
                    End If
                    Return y
                End Function
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void N1_LambdaLocalInNestedBlock_HidesCreatorsLocal_IsRefused_BC30616() => AssertRefused(
        N1, "BC30616", "BC30616: Variable 'x' hides a variable in an enclosing block.", 6, 17);

    // N2 — hides the creator's local directly in the lambda's own top-level body.
    private const string N2 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 3
                    Return x
                End Function
            Console.WriteLine(f())
            Console.WriteLine(x)
        End Sub
        """;

    [Test]
    public void N2_LambdaLocalHidesCreatorsLocal_IsRefused_BC30616() => AssertRefused(
        N2, "BC30616", "BC30616: Variable 'x' hides a variable in an enclosing block.", 4, 13);

    [Test]
    public void N2_AlsoRefusedThroughTheProjectEntryPoint() =>
        AssertRefusedViaProjectEntryPoint(N2, "BC30616");

    // N3 — the lambda reads the creator's x (via its own local `s`) BEFORE declaring its own x;
    // still refused — VB's block scope is the whole block, not "from this point on".
    private const string N3 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim s As Integer = x
                    Dim x As Integer = 3
                    Return s + x
                End Function
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void N3_LambdaReadsCreatorsXBeforeDeclaringItsOwn_StillRefused_BC30616() => AssertRefused(
        N3, "BC30616", "BC30616: Variable 'x' hides a variable in an enclosing block.", 5, 13);

    // N6 — a NESTED lambda's own Dim hides the OUTER lambda's creator's local (two levels out).
    private const string N6 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim g As Func(Of Integer) = Function()
                            Dim x As Integer = 1
                            Return x
                        End Function
                    Return x + g()
                End Function
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void N6_NestedLambdaLocal_HidesTheOutermostCreatorsLocal_IsRefused_BC30616() => AssertRefused(
        N6, "BC30616", "BC30616: Variable 'x' hides a variable in an enclosing block.", 5, 21);

    // N7 — the creator's local is declared AFTER the lambda, in the SAME enclosing block: VB's
    // block scope is the WHOLE block, so this still hides it (the "later declaration" half of
    // the rule — kills mutant M3).
    private const string N7 = """
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 3
                    Return x
                End Function
            Dim x As Integer = 5
            Console.WriteLine(f() + x)
        End Sub
        """;

    [Test]
    public void N7_CreatorsLocalDeclaredAfterTheLambda_StillHidesIt_IsRefused_BC30616() => AssertRefused(
        N7, "BC30616", "BC30616: Variable 'x' hides a variable in an enclosing block.", 3, 13);

    [Test]
    public void N7_AlsoRefusedThroughTheProjectEntryPoint() =>
        AssertRefusedViaProjectEntryPoint(N7, "BC30616");

    // N10 — the CREATOR's `For` control variable, hidden by a lambda Dim inside the loop body.
    private const string N10 = """
        Sub Main()
            Dim total As Integer = 0
            For i As Integer = 1 To 2
                Dim f As Func(Of Integer) = Function()
                        Dim i As Integer = 100
                        Return i
                    End Function
                total = total + f()
            Next
            Console.WriteLine(total)
        End Sub
        """;

    [Test]
    public void N10_LambdaLocal_HidesTheCreatorsForControlVariable_IsRefused_BC30616() => AssertRefused(
        N10, "BC30616", "BC30616: Variable 'i' hides a variable in an enclosing block.", 5, 17);

    // N12 — the "later declaration" rule again, this time through a NESTED If block inside the
    // lambda (the hidden name is still the CREATOR's, declared textually after the lambda).
    private const string N12 = """
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 1
                    If x > 0 Then
                        Dim y As Integer = 2
                        x = x + y
                    End If
                    Return x
                End Function
            Dim y As Integer = 10
            Console.WriteLine(f() + y)
        End Sub
        """;

    [Test]
    public void N12_CreatorsLaterLocal_HidesTheLambdasOwnNestedBlockLocal_IsRefused_BC30616() => AssertRefused(
        N12, "BC30616", "BC30616: Variable 'y' hides a variable in an enclosing block.", 5, 17);

    [Test]
    public void N12_AlsoRefusedThroughTheProjectEntryPoint() =>
        AssertRefusedViaProjectEntryPoint(N12, "BC30616");

    // ---- Must NOT be refused: fields, module globals, and a lambda local sharing no name -----

    // N5 — a lambda local sharing NO name with anything outside it: never refused.
    private const string N5 = """
        Sub Main()
            Dim x As Integer = 5
            Dim f As Func(Of Integer) = Function()
                    Dim y As Integer = 3
                    Return x + y
                End Function
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void N5_LambdaLocalSharesNoNameWithAnythingOutside_IsNotRefused() =>
        AssertNoLambdaBoundaryDiagnostic(N5);

    // N8 — hiding a CLASS FIELD is allowed (a field is not a procedure-local scope).
    private const string N8 = """
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
        """;

    [Test]
    public void N8_LambdaLocal_HidingAClassField_IsNotRefused() =>
        AssertNoLambdaBoundaryDiagnostic(N8);

    // N9 — hiding a MODULE-LEVEL GLOBAL is allowed (same reasoning as N8).
    private const string N9 = """
        Dim x As Integer = 5
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                    Dim x As Integer = 3
                    Return x
                End Function
            Console.WriteLine(f() + x)
        End Sub
        """;

    [Test]
    public void N9_LambdaLocal_HidingAModuleGlobal_IsNotRefused() =>
        AssertNoLambdaBoundaryDiagnostic(N9);

    // ====================================================================================
    // BC30734 — a lambda `Dim` hides a PARAMETER of the enclosing PROCEDURE.
    // ====================================================================================

    // N4 — the lambda's own Dim, inside a For loop in the lambda body, hides the Function's own
    // parameter `x`.
    private const string N4 = """
        Function Calc(x As Integer) As Integer
            Dim f As Func(Of Integer) = Function()
                    Dim t As Integer = x * 2
                    For i As Integer = 1 To 1
                        Dim x As Integer = 100
                        t = t + x
                    Next
                    Return t
                End Function
            Return f()
        End Function
        Sub Main()
            Console.WriteLine(Calc(5))
        End Sub
        """;

    [Test]
    public void N4_LambdaLocal_HidesTheProceduresOwnParameter_IsRefused_BC30734() => AssertRefused(
        N4, "BC30734", "BC30734: 'x' is already declared as a parameter of this method.", 5, 17);

    [Test]
    public void N4_AlsoRefusedThroughTheProjectEntryPoint() =>
        AssertRefusedViaProjectEntryPoint(N4, "BC30734");

    // ====================================================================================
    // BC36667 — a lambda `Dim` hides a PARAMETER of an ENCLOSING LAMBDA.
    // ====================================================================================

    // N11 — a nested lambda's own Dim hides the OUTER lambda's own parameter `a`. Distinct code
    // from BC30734 — kills mutant M5 (which collapses both into BC30616).
    private const string N11 = """
        Sub Main()
            Dim f As Func(Of Integer, Integer) = Function(a)
                    Dim g As Func(Of Integer) = Function()
                            Dim a As Integer = 1
                            Return a
                        End Function
                    Return a + g()
                End Function
            Console.WriteLine(f(5))
        End Sub
        """;

    [Test]
    public void N11_NestedLambdaLocal_HidesTheOuterLambdasOwnParameter_IsRefused_BC36667() => AssertRefused(
        N11, "BC36667",
        "BC36667: Variable 'a' is already declared as a parameter of this or an enclosing lambda expression.",
        4, 21);

    [Test]
    public void N11_AlsoRefusedThroughTheProjectEntryPoint() =>
        AssertRefusedViaProjectEntryPoint(N11, "BC36667");

    // ====================================================================================
    // S5 — Me.V and a bare V both resolve to the SAME field inside a lambda: no diagnostic (a
    // field, never a procedure-local scope) and BOTH spellings must keep working (S5 is also run
    // in the Integration fixture, expecting 14).
    // ====================================================================================

    private const string S5 = """
        Class C
            Public V As Integer
            Public Function Getter() As Func(Of Integer)
                Return Function() V + Me.V
            End Function
        End Class
        Sub Main()
            Dim c As New C()
            c.V = 7
            Dim f As Func(Of Integer) = c.Getter()
            Console.WriteLine(f())
        End Sub
        """;

    [Test]
    public void S5_BareAndMeQualifiedFieldReads_InsideALambda_AreNotRefused() =>
        AssertNoLambdaBoundaryDiagnostic(S5);

    // ====================================================================================
    // Scope boundary — out of scope, still accepted, each a pin naming the task that owns it (so
    // a future change to that task's own area is noticed here too).
    // ====================================================================================

    /// <summary>X1 — a <c>For Each x As Integer In xs</c> control variable declared INSIDE a
    /// lambda, hiding the CREATOR's local `x`: ADR-0009 deliberately does not report BC30616 for
    /// `For Each x As T` at all, lambda or not, and #174's own scope is a <c>Dim</c> only. Task
    /// #231 owns reconciling this.</summary>
    [Test]
    public void X1_ForEachControlVariableInsideALambda_HidingTheCreatorsLocal_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
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
            """);

    /// <summary>X3 — a <c>Catch ex As Exception</c> variable declared INSIDE a lambda, hiding the
    /// CREATOR's local `ex`: <c>Catch</c> variables are out of #174's own scope (a <c>Dim</c>
    /// only). Task #231's job, same as X1.</summary>
    [Test]
    public void X3_CatchVariableInsideALambda_HidingTheCreatorsLocal_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
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
            """);

    /// <summary>E16 — a lambda's own <c>Dim</c> named like the ENCLOSING FUNCTION itself: VB
    /// refuses nothing lambda-specific here (the collision, if any, is with the function's own
    /// name as a call target — a #231-shaped general-scope question, not #174's). #174 must not
    /// widen to a procedure's own NAME.</summary>
    [Test]
    public void E16_LambdaLocalNamedLikeTheEnclosingFunction_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
            Function Calc() As Integer
                Dim f As Func(Of Integer) = Function()
                        Dim Calc As Integer = 3
                        Return Calc
                    End Function
                Return f()
            End Function
            Sub Main()
                Console.WriteLine(Calc())
            End Sub
            """);

    /// <summary>E26 — a lambda's own <c>Dim</c> named like the enclosing FUNCTION's own TYPE
    /// PARAMETER: VB's own diagnostic for this is BC32089, a different code entirely, and #174's
    /// contract is explicit that a type parameter is not covered here. Task #231's job.</summary>
    [Test]
    public void E26_LambdaLocalNamedLikeATypeParameter_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
            Function Pick(Of T)(v As T) As Integer
                Dim f As Func(Of Integer) = Function()
                        Dim T As Integer = 3
                        Return T
                    End Function
                Return f()
            End Function
            Sub Main()
                Console.WriteLine(Pick(Of String)("a"))
            End Sub
            """);

    // ====================================================================================
    // B1-B3 — the GENERAL nested-block rule (no lambda involved at all) is #231's job, not
    // #174's: #174 must not widen past "a Dim INSIDE A LAMBDA". VB itself refuses all three
    // (BC30616, BC30616, BC30734 respectively — S/t174/blk/*.exp), but BasicLang must not yet.
    // Kills mutant M6 (the rule fires with no lambda at all).
    // ====================================================================================

    [Test]
    public void B1_NestedBlockDimHidesAnOuterLocal_NoLambdaInvolved_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
            Sub Main()
                Dim x As Integer = 5
                If True Then
                    Dim x As Integer = 10
                    Console.WriteLine(x)
                End If
                Console.WriteLine(x)
            End Sub
            """);

    [Test]
    public void B2_ForLoopBodyDimHidesAnOuterLocal_NoLambdaInvolved_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
            Sub Main()
                Dim x As Integer = 5
                For i As Integer = 1 To 1
                    Dim x As Integer = 10
                    Console.WriteLine(x)
                Next
                Console.WriteLine(x)
            End Sub
            """);

    [Test]
    public void B3_NestedBlockDimHidesAParameter_NoLambdaInvolved_IsNotRefused_Task231()
        => AssertNoLambdaBoundaryDiagnostic("""
            Sub Show(x As Integer)
                If True Then
                    Dim x As Integer = 10
                    Console.WriteLine(x)
                End If
                Console.WriteLine(x)
            End Sub
            Sub Main()
                Show(5)
            End Sub
            """);

    // ====================================================================================
    // Dedup — a node the analyzer reports a lambda-boundary diagnostic for is reported ONCE, even
    // when more than one reason to report it exists. Kills mutant M8 (remove the dedup set —
    // without it, this reports the SAME node TWICE).
    //
    // Construction: the lambda's own `Dim x` finds no hit yet at its own declaration (nothing
    // named `x` exists in ANY currently-open ancestor scope), so it is remembered
    // (`_lambdaLocals`) for a later declaration to find. TWO separate later declarations of `x`
    // then each enclose the lambda — the immediate one (the If block directly around it) and the
    // outer one (that If block's own parent) — each independently satisfying
    // `ReportLambdaLocalsHiddenBy`'s "an ancestor of the lambda declared this name later" test.
    // Without `_lambdaDiagnosticSites`, BOTH would fire on the exact same `Dim x` node.
    // ====================================================================================

    private const string DoubleHitSameNode = """
        Sub Main()
            If True Then
                If True Then
                    Dim f As Func(Of Integer) = Function()
                            Dim x As Integer = 1
                            Return x
                        End Function
                    Dim x As Integer = 2
                    Console.WriteLine(f() + x)
                End If
                Dim x As Integer = 3
                Console.WriteLine(x)
            End If
        End Sub
        """;

    [Test]
    public void ANodeWithTwoReasonsToBeReported_IsReportedOnlyOnce()
    {
        var errors = Analyze(DoubleHitSameNode);
        var hits = errors.Where(e => e.ErrorCode == "BC30616").ToList();
        Assert.That(hits, Has.Count.EqualTo(1),
            "the lambda's own `Dim x` is hidden by TWO separate later declarations (an inner and "
            + "an outer enclosing block) — without the dedup set it reports on that same node "
            + "twice; got: " + string.Join(" | ", hits.Select(e => $"{e.Line}:{e.Column}")));
        Assert.That(hits[0].Line, Is.EqualTo(5), "reported at the lambda's own Dim, not either hider");
    }

    // ====================================================================================
    // The LSP path — DocumentManager surfaces each code with a REAL span on the use line, the
    // same idiom as PropertyAccessDiagnosticsTests' Lsp_ tests (task #178). This is the ONLY
    // place the implementer's mutant M7 (the LSP path not wired — DocumentManager filters these
    // four codes out) is visible: the CLI and CompileProjectFiles share the same SemanticAnalyzer
    // instance directly, but DocumentManager.RunSemanticAnalysis copies analyzer.Errors into its
    // OWN Diagnostic list, and a filter there would leave every assertion above completely blind
    // to it.
    // ====================================================================================

    private static DocumentState AnalyzeViaLsp(string source)
    {
        var documentManager = new DocumentManager();
        var uri = DocumentUri.From("untitled:LambdaBoundaryProbe.bas");
        return documentManager.UpdateDocument(uri, source);
    }

    [Test]
    public void Lsp_ByRefParameterWrite_SurfacesAsBC36639_WithARealSpanOnTheUseLine()
    {
        var state = AnalyzeViaLsp(R1);
        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC36639"));
        Assert.That(match, Is.Not.Null,
            "expected a BC36639 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("'n'"));
        Assert.That(match.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(match.Line, Is.EqualTo(2), "the use line");
        Assert.That(match.Column, Is.GreaterThan(0), "a real column, not a placeholder");
    }

    [Test]
    public void Lsp_LambdaLocalHidesCreatorsLocal_SurfacesAsBC30616_WithARealSpanOnTheUseLine()
    {
        var state = AnalyzeViaLsp(N2);
        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC30616"));
        Assert.That(match, Is.Not.Null,
            "expected a BC30616 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("'x'"));
        Assert.That(match.Severity, Is.EqualTo(DiagnosticSeverity.Error));
        Assert.That(match.Line, Is.EqualTo(4), "the use line");
        Assert.That(match.Column, Is.GreaterThan(0), "a real column, not a placeholder");
    }

    [Test]
    public void Lsp_LambdaLocalHidesTheProceduresParameter_SurfacesAsBC30734()
    {
        var state = AnalyzeViaLsp(N4);
        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC30734"));
        Assert.That(match, Is.Not.Null,
            "expected a BC30734 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("'x'"));
        Assert.That(match.Line, Is.EqualTo(5), "the use line");
    }

    [Test]
    public void Lsp_CreatorsLaterLocal_StillHidesTheLambdasEarlierDim_SurfacesAsBC30616()
    {
        // N7 — the LSP path must see the "later declaration" half of the rule too, not only the
        // immediate-hit half (which alone would not distinguish a correct implementation from
        // mutant M3).
        var state = AnalyzeViaLsp(N7);
        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC30616"));
        Assert.That(match, Is.Not.Null,
            "expected a BC30616 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Line, Is.EqualTo(3), "reported at the lambda's own (earlier) Dim");
    }

    [Test]
    public void Lsp_NestedLambdaLocal_HidesTheOuterLambdasParameter_SurfacesAsBC36667()
    {
        var state = AnalyzeViaLsp(N11);
        var match = state.Diagnostics.FirstOrDefault(d => d.Message.Contains("BC36667"));
        Assert.That(match, Is.Not.Null,
            "expected a BC36667 LSP diagnostic; got: " + string.Join(" | ", state.Diagnostics.Select(d => d.Message)));
        Assert.That(match!.Message, Does.Contain("'a'"));
        Assert.That(match.Line, Is.EqualTo(4), "the use line");
    }

    /// <summary>R7 must surface NOTHING through the LSP either — the same shadowing-by-symbol
    /// decision the fast in-process tests make, now checked on the path <c>DocumentManager</c>
    /// actually serves to the editor. This is the one assertion mutant M1 (BC36639 decided by
    /// NAME instead of symbol) would flip here: under M1 the LSP would show a spurious BC36639 on
    /// R7's shadowed `n`.</summary>
    [Test]
    public void Lsp_LambdaParameterShadowsTheByRefParameter_SurfacesNoDiagnostic()
    {
        var state = AnalyzeViaLsp(R7);
        var stray = state.Diagnostics.Where(d => LambdaBoundaryCodes.Any(c => d.Message.Contains(c))).ToList();
        Assert.That(stray, Is.Empty,
            "expected no lambda-boundary diagnostic over LSP for R7 (task #217 interim: shadowing, "
            + "silently); got: " + string.Join(" | ", stray.Select(d => d.Message)));
    }
}
