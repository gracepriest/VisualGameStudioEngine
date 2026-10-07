using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

// ================================================================================================
//  Tasks #219 and #249 — the front end's own diagnostics about a Function's / a Get's IMPLICIT RETURN VARIABLE (`F = v` inside `Function F`), the fast half. `ImplicitReturnVariableExecutionTests`
//  (Integration) RUNS the accepted shapes on four backends; this fixture has no process: it parses and analyzes (`Analyze`), and takes the same program through `BasicCompiler.CompileProjectFiles`, which stops
//  at the combined IR (what a .blproj build and the IDE call), because a project compile analyzes a file inside a unit, a different path from the bare Parser + SemanticAnalyzer pair.
//
//  ORACLE: vbc, the SDK's, on the program wrapped in a VB Module (`S/t136/tools/vbv2.py`): the implementer's `S/t219/edge/Y5.bas`, `Y6.bas` and `Z3.bas` are refused with BC36946 (Y5, Y6) and BC30066 (Z3), the
//  line being the statement's own. The recursive Async program is `S/t219/probes3/K03.bas`, accepted and run by vbc (55 and 3).
//
//    * BC36946 — "The implicit return variable of an Iterator or Async method cannot be accessed." A Function that is `Async` or `Iterator` has no implicit return variable: `F = v` is refused at the
//      reference, and the reference keeps naming the procedure.
//    * BC30066 — "'Exit Property' is not valid in a Function or Sub." `Exit Property` PARSES (it did not before) and is valid only in a property accessor's own body.
//    * The M6 killer: a recursive Async CALL (`Await CountDown(n - 1)`) is NOT refused. A callee names the procedure, whatever the spelling, so it never touches the return variable.
//
//  MUTANT M6 (a callee is visited as a plain name, its exclusion `VisitAsProcedureName(node.Callee)` dropped) — a recursive call in a SYNC function marks the variable referenced and nothing else changes
//  (the call path re-resolves a callee itself), but in an Async function the reference is the BC36946: killed by `ARecursiveAsyncCall_IsNotRefused`, and by it alone.
// ================================================================================================

[TestFixture]
public class ImplicitReturnVariableDiagnosticsTests
{
    private const string BC36946 = "BC36946";
    private const string BC30066 = "BC30066";

    /// <summary>Parse (a parse error fails the test: a typo in a probe must not pass as a refusal) and analyze; every diagnostic, in the analyzer's order.</summary>
    private static List<SemanticError> Analyze(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "parse errors:\n" + string.Join("\n", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.ToList();
    }

    /// <summary><c>BasicCompiler.CompileProjectFiles</c> (aggressive), in process: what a Release .blproj build and the IDE call.</summary>
    private static List<SemanticError> ViaProject(string source)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-t219-proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Main.bas");
            File.WriteAllText(path, source);
            return new BasicCompiler(new CompilerOptions { OptimizeAggressive = true }).CompileProjectFiles(new List<string> { path }).AllErrors.ToList();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* temp */ } }
    }

    /// <summary>The (code, line) of every diagnostic that carries <paramref name="code"/>.</summary>
    private static (string Code, int Line)[] Of(List<SemanticError> errors, string code)
        => errors.Where(e => e.ErrorCode == code).Select(e => (e.ErrorCode, e.Line)).ToArray();

    private static string Said(List<SemanticError> errors) => string.Join(" | ", errors.Select(e => $"{e.Line}: {e.ErrorCode} {e.Message}"));

    // The source and the 1-based line vbc reports it on (the statement that names the variable).
    private const string AsyncFunction = """
        Async Function F() As Task(Of Integer)
            Await Task.Delay(1)
            F = 5
        End Function
        Sub Main()
            Console.WriteLine(F().Result)
        End Sub
        """;

    private const string IteratorFunction = """
        Module M
            Iterator Function F() As IEnumerable(Of Integer)
                Yield 1
                F = Nothing
            End Function
            Sub Main()
                For Each i In F()
                    Console.WriteLine(i)
                Next
            End Sub
        End Module
        """;

    /// <summary>
    /// BC36946 — `F = 5` inside an Async Function (Y5, line 3) and `F = Nothing` inside an Iterator Function (Y6, line 4) are refused, at the reference, by the analyzer and by the project entry point. The
    /// diagnostic carries VB's number and message; the Async refusal is the only BC36946 in its program (the reference keeps the procedure, so nothing else reports for it).
    /// </summary>
    [Test]
    public void BC36946_TheReturnVariableOfAnAsyncOrIteratorFunction_IsRefused()
    {
        Assert.Multiple(() =>
        {
            foreach (var (id, source, line) in new[] { ("Y5_async", AsyncFunction, 3), ("Y6_iterator", IteratorFunction, 4) })
            {
                var direct = Analyze(source);
                Assert.That(Of(direct, BC36946), Is.EqualTo(new[] { (BC36946, line) }), $"{id}, analyzer: BC36946 once, on the line of `F = …`; got: {Said(direct)}");
                Assert.That(direct.First(e => e.ErrorCode == BC36946).Message,
                    Does.Contain("The implicit return variable of an Iterator or Async method cannot be accessed."), $"{id}: VB's message");

                var project = ViaProject(source);
                Assert.That(Of(project, BC36946), Is.EqualTo(new[] { (BC36946, line) }), $"{id}, CompileProjectFiles: BC36946 once, on line {line}; got: {Said(project)}");
            }
        });
    }

    /// <summary>
    /// BC30066 — `Exit Property` in a Sub (Z3, line 2) and in a Function is refused, by the analyzer and by the project entry point; in a property's Get and in its Set it is accepted (it parsed as nothing
    /// before, so a program using it could not be compiled at all).
    /// </summary>
    [Test]
    public void BC30066_ExitProperty_OutsideAPropertyAccessor_IsRefused_AndInsideOneIsNot()
    {
        const string inASub = """
            Sub S()
                Exit Property
            End Sub
            Sub Main()
            End Sub
            """;
        const string inAFunction = """
            Function G() As Integer
                Exit Property
            End Function
            Sub Main()
            End Sub
            """;
        const string inAccessors = """
            Class C
                Private _v As Integer
                Public Property P As Integer
                    Get
                        P = _v
                        If _v > 1 Then Exit Property
                        P = 0
                    End Get
                    Set(value As Integer)
                        _v = value
                        Exit Property
                    End Set
                End Property
            End Class
            Sub Main()
            End Sub
            """;

        Assert.Multiple(() =>
        {
            foreach (var (id, source) in new[] { ("Z3_sub", inASub), ("Z3_function", inAFunction) })
            {
                var direct = Analyze(source);
                Assert.That(Of(direct, BC30066), Is.EqualTo(new[] { (BC30066, 2) }), $"{id}, analyzer: BC30066 once, on line 2; got: {Said(direct)}");
                Assert.That(direct.First(e => e.ErrorCode == BC30066).Message, Does.Contain("'Exit Property' is not valid in a Function or Sub."), $"{id}: VB's message");

                var project = ViaProject(source);
                Assert.That(Of(project, BC30066), Is.EqualTo(new[] { (BC30066, 2) }), $"{id}, CompileProjectFiles: BC30066 once, on line 2; got: {Said(project)}");
            }

            Assert.That(Analyze(inAccessors), Is.Empty, "Exit Property inside a Get and a Set is accepted");
            Assert.That(ViaProject(inAccessors), Is.Empty, "Exit Property inside a Get and a Set is accepted through CompileProjectFiles");
        });
    }

    /// <summary>
    /// M6 — a recursive Async call is NOT refused (K03). `Await CountDown(n - 1)` names the PROCEDURE: a callee never reaches the return variable, so an Async function that only calls itself reports nothing,
    /// and the sync recursion beside it (`Fib(n - 1)`) neither. The program runs on vbc (55 and 3). With the callee's exclusion dropped (M6) the call is a bare reference to the Async function's own name and is
    /// refused with BC36946.
    /// </summary>
    [Test]
    public void ARecursiveAsyncCall_IsNotRefused_BecauseACalleeIsTheProcedureNotTheVariable()
    {
        const string k03 = """
            Function Fib(n As Integer) As Integer
                If n < 2 Then Return n
                Return Fib(n - 1) + Fib(n - 2)
            End Function
            Async Function CountDown(n As Integer) As Task(Of Integer)
                If n = 0 Then Return 0
                Dim r As Integer = Await CountDown(n - 1)
                Return r + 1
            End Function
            Sub Main()
                Console.WriteLine(Fib(10))
                Console.WriteLine(CountDown(3).Result)
            End Sub
            """;

        Assert.Multiple(() =>
        {
            var direct = Analyze(k03);
            Assert.That(direct, Is.Empty, "K03, analyzer: nothing is refused; got: " + Said(direct));
            var project = ViaProject(k03);
            Assert.That(project, Is.Empty, "K03, CompileProjectFiles: nothing is refused; got: " + Said(project));
        });
    }
}
