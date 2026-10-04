using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using VisualGameStudio.Tests.Msil;

namespace VisualGameStudio.Tests.Compiler;

// =====================================================================================
//  Task #164, committed 151a8137 — a multi-line `Function(...) [As T] ... End Function`
//  lambda. Two defects, together making every one of these fail on every backend and
//  entry point:
//
//   1. IRBuilder.Visit(LambdaExpressionNode) read GetNodeType(node.Body) for a Function
//      lambda's return type; a STATEMENT lambda has no Body (its body is StatementBody),
//      so Dictionary.TryGetValue(null) threw "Value cannot be null. (Parameter 'key')",
//      reported as "Error compiling Main: …" at line 0 — the front end never got past
//      building the IR (F1, F3, F6, F7, F8: the crash repro).
//   2. A multi-line Function lambda with no `As` clause was analyzed as a Sub (ReturnType
//      = Void), so `Return c` inside it was refused ("Cannot return a value from a
//      subroutine") and the lambda was typed Func(Of Void) — breaking any caller expecting
//      Func(Of Integer) (F2, F4, F5).
//
//  The fix types a statement-bodied Function lambda VB's way: a written `As T`; else the R
//  of a Func(Of …, R) it is target-typed by (SemanticAnalyzer.TargetedLambdaReturnType);
//  else the DOMINANT type of its own Return expressions by the analyzer's existing
//  widening rule (SemanticAnalyzer.DominantReturnType), Object with none dominant or no
//  Return at all. IRBuilder then takes the IR return type from the analyzer's own Func
//  rather than re-deriving it, and a Function lambda that falls off its end returns its
//  type's default (not a bare `ret`, which is an InvalidProgramException on MSIL).
//
//  Probe sources (F1-F8, E1-E11, R1-R5, R2ref, R3b) are VERBATIM from the implementer's
//  scratch corpus (S/t164/probes, S/t164/edge) except ArityMismatch, written for this file
//  (see its own doc comment). Every expected value below was independently re-measured
//  against this working tree — front end (parse/analyze/build), JavaScript under Node,
//  MSIL under ilasm, C# and C++ through Roslyn/g++ in process — before being pinned; the
//  known-wrong C#/C++ cells are copied from neither JS nor MSIL (CLAUDE.md's rule for the
//  MSIL backend applies here too: never treat one backend's output as another's oracle).
//
//  Mutation-proved (S/t164/mut/mutate.py's twelve mutants, applied to a scratch tree of
//  this working tree, the production DLL swapped into an isolated CLI/harness run — never
//  this repo's own build). See each test's own doc comment for which mutant(s) it kills;
//  the mapping is summarized in the task's hand-back.
// =====================================================================================

/// <summary>Probe sources, named to match the implementer's/test-writer's letters.</summary>
internal static class MultiLineFunctionLambdaProbes
{
    // ---- F1-F8: the crash repro (F1,F3,F6,F7,F8) and the Sub-typing defect (F2,F4,F5) ----

    internal const string F1 = """
        Sub Main()
            Dim n As Integer = 1
            Dim q As Integer = 2
            Dim bump = Function() As Integer
                           n = n + 100
                           Return 0
                       End Function
            Dim a As Integer = n + q
            Dim z As Integer = bump()
            Dim b As Integer = n + q
            Console.WriteLine(CStr(a) & "," & CStr(b) & "," & CStr(z))
        End Sub
        """;
    internal const string F1Expected = "3,103,0";

    internal const string F2 = """
        Function MakeCounter() As Func(Of Integer)
            Dim c As Integer = 10
            Return Function()
                       c = c + 1
                       Return c
                   End Function
        End Function

        Sub Main()
            Dim next1 = MakeCounter()
            Console.WriteLine(next1())
            Console.WriteLine(next1())
        End Sub
        """;
    internal const string F2Expected = "11\n12";

    internal const string F3 = """
        Function Apply(g As Func(Of Integer, Integer), v As Integer) As Integer
            Return g(v)
        End Function

        Sub Main()
            Dim k As Integer = 3
            Dim r As Integer = Apply(Function(x As Integer) As Integer
                                         Dim t As Integer = x * 2
                                         Return t + k
                                     End Function, 5)
            Console.WriteLine(r)
        End Sub
        """;
    internal const string F3Expected = "13";

    internal const string F4 = """
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                                            Dim a As Integer = 40
                                            Return a + 2
                                        End Function
            Console.WriteLine(f())
        End Sub
        """;
    internal const string F4Expected = "42";

    internal const string F5 = """
        Sub Main()
            Dim f = Function()
                        Return 42
                    End Function
            Console.WriteLine(f())
            Dim g = Function(s As String)
                        Return s & "!"
                    End Function
            Console.WriteLine(g("hi"))
        End Sub
        """;
    internal const string F5Expected = "42\nhi!";

    internal const string F6 = """
        Sub Main()
            Dim sign = Function(n As Integer) As String
                           If n > 0 Then
                               Return "pos"
                           ElseIf n < 0 Then
                               Return "neg"
                           End If
                           Return "zero"
                       End Function
            Console.WriteLine(sign(5) & "," & sign(-2) & "," & sign(0))
        End Sub
        """;
    internal const string F6Expected = "pos,neg,zero";

    internal const string F7 = """
        Class Acc
            Private _total As Integer = 5
            Public Function Run() As Integer
                Dim add = Function(v As Integer) As Integer
                              _total = _total + v
                              Return _total
                          End Function
                add(10)
                Return add(1)
            End Function
        End Class

        Sub Main()
            Dim a As New Acc()
            Console.WriteLine(a.Run())
        End Sub
        """;
    internal const string F7Expected = "16";

    internal const string F8 = """
        Sub Main()
            Dim n As Integer = 1
            Dim outer = Function() As Integer
                            Dim inner = Function() As Integer
                                            n = n + 10
                                            Return n
                                        End Function
                            Return inner() + inner()
                        End Function
            Console.WriteLine(outer())
            Console.WriteLine(n)
        End Sub
        """;
    internal const string F8Expected = "32\n21";

    // ---- Edge and refusal probes (S/t164/edge) ----

    internal const string E1 = """
        Sub Main()
            Dim f = Function(b As Boolean)
                        If b Then
                            Return 1
                        End If
                        Return 2.5
                    End Function
            Dim d As Double = f(True) + f(False)
            Console.WriteLine(d)
        End Sub
        """;
    internal const string E1Expected = "3.5";

    internal const string E2 = """
        Sub Main()
            Dim outer = Function()
                            Dim inner = Function()
                                            Return "abc"
                                        End Function
                            Return Len(inner()) + 1
                        End Function
            Dim x As Integer = outer()
            Console.WriteLine(x)
        End Sub
        """;
    internal const string E2Expected = "4";

    internal const string E3 = """
        Sub Main()
            Dim f = Function(b As Boolean)
                        If b Then
                            Return Nothing
                        End If
                        Return "s"
                    End Function
            Dim t As String = f(False)
            Console.WriteLine(t)
        End Sub
        """;
    internal const string E3Expected = "s";

    internal const string E4 = """
        Function Apply(g As Func(Of Integer, Integer), v As Integer) As Integer
            Return g(v)
        End Function

        Sub Main()
            Dim r As Integer = Apply(Function(x)
                                         Dim t = x * 2
                                         Return t + 1
                                     End Function, 5)
            Console.WriteLine(r)
        End Sub
        """;
    internal const string E4Expected = "11";

    internal const string E5 = """
        Sub Main()
            Dim f As Func(Of Long)
            f = Function()
                    Return 5
                End Function
            Dim v As Long = f() * 1000000000
            Console.WriteLine(v)
        End Sub
        """;
    internal const string E5Expected = "5000000000";

    internal const string E6 = """
        Sub Main()
            Dim f = Function()
                        Console.WriteLine("side")
                    End Function
            f()
        End Sub
        """;
    internal const string E6Expected = "side";

    internal const string E9 = """
        Sub Main()
            Dim mk = Function(k As Integer)
                         Return Function(x As Integer) x + k
                     End Function
            Dim add5 = mk(5)
            Console.WriteLine(add5(10))
        End Sub
        """;
    internal const string E9Expected = "15";

    internal const string E11 = """
        Function Make(seed As Integer) As Func(Of Integer, String)
            Return Function(x)
                       Dim s As String = CStr(x + seed)
                       Return s & "!"
                   End Function
        End Function

        Sub Main()
            Dim g = Make(3)
            Console.WriteLine(g(4))
        End Sub
        """;
    internal const string E11Expected = "7!";

    internal const string R1 = """
        Sub Main()
            Dim s = Sub()
                        Console.WriteLine("x")
                        Return 1
                    End Sub
            s()
        End Sub
        """;

    internal const string R2 = """
        Sub Main()
            Dim f As Func(Of String) = Function()
                                           Return 5
                                       End Function
            Console.WriteLine(f())
        End Sub
        """;

    /// <summary>Same refusal as <see cref="R2"/>, from a NAMED Function instead of a lambda — the
    /// message must read identically either way (section 1's "R2: the same message as … R2ref").</summary>
    internal const string R2ref = """
        Function G() As String
            Return 5
        End Function

        Sub Main()
            Console.WriteLine(G())
        End Sub
        """;

    internal const string R3 = """
        Sub Main()
            Dim f = Function(b As Boolean)
                        If b Then
                            Return 1
                        End If
                        Return "x"
                    End Function
            Console.WriteLine(f(True))
            Console.WriteLine(f(False))
        End Sub
        """;
    internal const string R3Expected = "1\nx";

    internal const string R3b = """
        Sub Main()
            Dim f = Function(b As Boolean)
                        If b Then
                            Return 1
                        End If
                        Return "x"
                    End Function
            Dim n As Integer = f(True)
            Console.WriteLine(n)
        End Sub
        """;

    internal const string R4 = """
        Sub Main()
            Dim f = Function()
                        Return
                    End Function
            Console.WriteLine(f())
        End Sub
        """;

    internal const string R5 = """
        Sub Main()
            Dim f As Func(Of Integer) = Function()
                                            Return
                                        End Function
            Console.WriteLine(f())
        End Sub
        """;

    /// <summary>
    /// Written for this file to kill mutant (i): <c>TargetedLambdaReturnType</c>'s arity guard
    /// (<c>targetType.GenericArguments.Count != parameterCount + 1</c>) mutated to
    /// <c>&lt;= parameterCount</c>. The lambda declares ONE parameter against a Func target with
    /// THREE generic arguments (two params + R) — an arity mismatch a named <c>Dim</c> already
    /// refuses on its own ("Cannot assign value of type 'Func' to variable of type 'Func'"). The
    /// correct behaviour is exactly that ONE refusal; the mutant's relaxed guard does not reject
    /// the target, reads <c>GenericArguments[parameterCount]</c> — a PARAMETER type ('String'),
    /// not R — as the return type, and so ALSO refuses <c>Return x + 1</c> against 'String',
    /// producing a second, spurious diagnostic. Measured (test-writer, S/t164/mut/res-
    /// i_arity_unchecked.txt showed no probe here distinguishing the mutant; this one does).
    /// </summary>
    internal const string ArityMismatch = """
        Sub Main()
            Dim f As Func(Of Integer, String, Double) = Function(x)
                                                             Return x + 1
                                                         End Function
            Console.WriteLine(f(1, "y"))
        End Sub
        """;
}

/// <summary>
/// Front end only: parse → analyze → build IR, no process spawned. Fast subset (no
/// [Category("Integration")]).
/// </summary>
[TestFixture]
public class MultiLineFunctionLambdaTests
{
    private static ProgramNode Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var program = parser.Parse();
        Assert.That(parser.Errors, Is.Empty,
            "parse errors: " + string.Join("; ", parser.Errors.Select(e => e.Message)));
        return program;
    }

    private static (ProgramNode Program, SemanticAnalyzer Analyzer, bool Ok) Analyze(string source)
    {
        var program = Parse(source);
        var analyzer = new SemanticAnalyzer();
        var ok = analyzer.Analyze(program);
        return (program, analyzer, ok);
    }

    private static string ErrorText(SemanticAnalyzer analyzer) =>
        string.Join("; ", analyzer.Errors.Select(e => e.Message));

    private static List<string> ErrorMessages(SemanticAnalyzer analyzer) =>
        analyzer.Errors.Where(e => e.Severity == ErrorSeverity.Error).Select(e => e.Message).ToList();

    /// <summary>
    /// Every <see cref="LambdaExpressionNode"/> reachable from <paramref name="node"/>, in
    /// source (pre-)order — a Dim/assignment initializer, a Return's value, a bare expression
    /// statement, an If/ElseIf/Else branch, a call argument, a binary operand, and (recursively)
    /// a lambda's own body, so a NESTED lambda (F8, E2) is found too, after its enclosing one.
    /// Covers exactly the shapes S/t164's probes and edge cases use — not a general AST walker.
    /// </summary>
    private static void CollectLambdas(ASTNode node, List<LambdaExpressionNode> into)
    {
        switch (node)
        {
            case null:
                return;
            case ProgramNode p:
                foreach (var d in p.Declarations) CollectLambdas(d, into);
                return;
            case ClassNode c:
                foreach (var m in c.Members) CollectLambdas(m, into);
                return;
            case SubroutineNode s:
                CollectLambdas(s.Body, into);
                return;
            case FunctionNode f:
                CollectLambdas(f.Body, into);
                return;
            case BlockNode b:
                foreach (var st in b.Statements) CollectLambdas(st, into);
                return;
            case VariableDeclarationNode v:
                CollectLambdas(v.Initializer, into);
                return;
            case ReturnStatementNode r:
                CollectLambdas(r.Value, into);
                return;
            case AssignmentStatementNode a:
                CollectLambdas(a.Value, into);
                return;
            case ExpressionStatementNode e:
                CollectLambdas(e.Expression, into);
                return;
            case IfStatementNode iff:
                CollectLambdas(iff.Condition, into);
                CollectLambdas(iff.ThenBlock, into);
                foreach (var clause in iff.ElseIfClauses)
                {
                    CollectLambdas(clause.Condition, into);
                    CollectLambdas(clause.Block, into);
                }
                CollectLambdas(iff.ElseBlock, into);
                return;
            case CallExpressionNode call:
                CollectLambdas(call.Callee, into);
                foreach (var arg in call.Arguments) CollectLambdas(arg, into);
                return;
            case BinaryExpressionNode bin:
                CollectLambdas(bin.Left, into);
                CollectLambdas(bin.Right, into);
                return;
            case LambdaExpressionNode lam:
                into.Add(lam);
                CollectLambdas(lam.Body, into);
                CollectLambdas(lam.StatementBody, into);
                return;
        }
    }

    private static List<LambdaExpressionNode> Lambdas(ProgramNode program)
    {
        var result = new List<LambdaExpressionNode>();
        CollectLambdas(program, result);
        return result;
    }

    private static string LastGenericArgName(TypeInfo funcType) =>
        funcType?.GenericArguments?.Count > 0 ? funcType.GenericArguments[^1].Name : null;

    // ---- F1-F8 build without error (mutant a/a1 also fail here — see the Execution class for
    // the MSIL leg that distinguishes a1 specifically) --------------------------------------

    [TestCase(MultiLineFunctionLambdaProbes.F1, "F1")]
    [TestCase(MultiLineFunctionLambdaProbes.F2, "F2")]
    [TestCase(MultiLineFunctionLambdaProbes.F3, "F3")]
    [TestCase(MultiLineFunctionLambdaProbes.F4, "F4")]
    [TestCase(MultiLineFunctionLambdaProbes.F5, "F5")]
    [TestCase(MultiLineFunctionLambdaProbes.F6, "F6")]
    [TestCase(MultiLineFunctionLambdaProbes.F7, "F7")]
    [TestCase(MultiLineFunctionLambdaProbes.F8, "F8")]
    public void MultiLineFunctionLambda_BuildsWithoutError(string source, string label)
    {
        var (program, analyzer, ok) = Analyze(source);
        Assert.That(ok, Is.True, label + ": " + ErrorText(analyzer));
        Assert.That(analyzer.Errors, Is.Empty, label + ": " + ErrorText(analyzer));

        // Mutant (a)/(a2): IRBuilder.Visit(LambdaExpressionNode) reading
        // GetNodeType(node.Body) for a statement lambda (node.Body is null) throws
        // "Value cannot be null. (Parameter 'key')" — F1, F3, F6, F7, F8 are the repro.
        Assert.DoesNotThrow(() => new IRBuilder(analyzer).Build(program, "T"),
            label + ": IRBuilder must not crash on a statement-bodied Function lambda (#164)");
    }

    /// <summary>
    /// Mutant (f): IRBuilder hardcoding the lambda's IR return type as Object instead of taking
    /// it from the analyzer's own Func. F1's lambda is declared <c>As Integer</c>, so its IR
    /// function must carry Integer, not Object.
    /// </summary>
    [Test]
    public void F1_StatementLambda_IrReturnType_IsTheAnalyzersFuncR_NotObject()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.F1);
        Assert.That(ok, Is.True, ErrorText(analyzer));

        var module = new IRBuilder(analyzer).Build(program, "T");
        var lambdaFn = module.Functions.Single(fn => fn.IsLambda);

        Assert.That(lambdaFn.ReturnType?.Name, Is.EqualTo("Integer"),
            "mutant (f) hardcodes Object here regardless of the analyzer's Func(Of Integer)");
    }

    // ---- The analyzer's recorded delegate type for each lambda (section 1) -----------------

    [Test]
    public void F1_ExplicitAsInteger_IsFuncWithLastGenericArgumentInteger()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.F1);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var type = analyzer.GetNodeType(Lambdas(program).Single());
        Assert.That(type?.Name, Is.EqualTo("Func"));
        Assert.That(LastGenericArgName(type), Is.EqualTo("Integer"));
    }

    [Test]
    public void F4_TargetTypedFuncOfInteger_InfersInteger()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.F4);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var type = analyzer.GetNodeType(Lambdas(program).Single());
        Assert.That(LastGenericArgName(type), Is.EqualTo("Integer"));
    }

    /// <summary>No target, no <c>As</c>: each of F5's two lambdas infers ITS OWN Return type.</summary>
    [Test]
    public void F5_UntypedLambdas_EachInferOwnReturnType()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.F5);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var lambdas = Lambdas(program);
        Assert.That(lambdas, Has.Count.EqualTo(2));
        Assert.That(LastGenericArgName(analyzer.GetNodeType(lambdas[0])), Is.EqualTo("Integer"), "f: Return 42");
        Assert.That(LastGenericArgName(analyzer.GetNodeType(lambdas[1])), Is.EqualTo("String"), "g: Return s & \"!\"");
    }

    /// <summary>Mutants (d1)/(d2): Integer and Double widen to Double (VB's dominant type / the
    /// analyzer's WidensTo), not "first candidate wins" and not "no widening = Object".</summary>
    [Test]
    public void E1_IntegerAndDoubleReturns_WidenToDouble()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.E1);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var type = analyzer.GetNodeType(Lambdas(program).Single());
        Assert.That(LastGenericArgName(type), Is.EqualTo("Double"));
    }

    /// <summary>Mutant (h): a bare <c>Return Nothing</c> must not itself become a dominant-type
    /// candidate — Nothing + String must infer String, not Object.</summary>
    [Test]
    public void E3_NothingAndString_InfersString_NothingIsNotACandidate()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.E3);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var type = analyzer.GetNodeType(Lambdas(program).Single());
        Assert.That(LastGenericArgName(type), Is.EqualTo("String"));
    }

    /// <summary>Integer and String share no dominant type: VB's answer is Object (not a refusal —
    /// R3 is not in the refusal set; only R3b, using the Object result where an Integer is
    /// required, is refused).</summary>
    [Test]
    public void R3_IncompatibleReturns_InferObject_AndBuildsWithoutError()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.R3);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var type = analyzer.GetNodeType(Lambdas(program).Single());
        Assert.That(LastGenericArgName(type), Is.EqualTo("Object"));
    }

    /// <summary>No <c>Return</c> with a value at all: VB's answer (without Option Strict) is
    /// Object, the same as an inference with no dominant candidate.</summary>
    [Test]
    public void E6_NoReturnAtAll_InfersObject()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.E6);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var type = analyzer.GetNodeType(Lambdas(program).Single());
        Assert.That(LastGenericArgName(type), Is.EqualTo("Object"));
    }

    /// <summary>Mutant (e): a nested lambda's own <c>Return</c> must stay scoped to ITS function
    /// scope. Inner returns String; outer's own Return (<c>Len(inner())+1</c>) is Integer — if the
    /// inner's String leaked into the outer's candidates, Integer+String would (per R3) infer
    /// Object instead.</summary>
    [Test]
    public void E2_NestedLambdaReturn_DoesNotLeakIntoOuterInference()
    {
        var (program, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.E2);
        Assert.That(ok, Is.True, ErrorText(analyzer));
        var lambdas = Lambdas(program);
        Assert.That(lambdas, Has.Count.EqualTo(2), "outer, then inner");
        Assert.That(LastGenericArgName(analyzer.GetNodeType(lambdas[1])), Is.EqualTo("String"), "inner: Return \"abc\"");
        Assert.That(LastGenericArgName(analyzer.GetNodeType(lambdas[0])), Is.EqualTo("Integer"),
            "outer: Return Len(inner())+1 — mutant (e) would leak inner's String Return in and infer Object");
    }

    // ---- Refusals keep their messages (section 1) -------------------------------------------

    private static string SingleErrorMessage(string source)
    {
        var (_, analyzer, ok) = Analyze(source);
        Assert.That(ok, Is.False, "expected this program to be refused");
        var errors = ErrorMessages(analyzer);
        Assert.That(errors, Is.Not.Empty);
        return errors[0];
    }

    [Test]
    public void R1_ReturnValueInSubLambda_IsRefused()
        => Assert.That(SingleErrorMessage(MultiLineFunctionLambdaProbes.R1),
            Does.Contain("Cannot return a value from a subroutine"));

    /// <summary>R2 (a target-typed lambda) and R2ref (a named Function) must give the IDENTICAL
    /// message for the identical mismatch — the lambda path must not invent its own wording.</summary>
    [Test]
    public void R2_TargetTypedReturnMismatch_SameMessageAsNamedFunctionReference()
    {
        var lambdaMessage = SingleErrorMessage(MultiLineFunctionLambdaProbes.R2);
        var namedMessage = SingleErrorMessage(MultiLineFunctionLambdaProbes.R2ref);
        Assert.That(lambdaMessage, Is.EqualTo(namedMessage));
        Assert.That(lambdaMessage, Does.Contain("Cannot return type 'Integer' from function expecting 'String'"));
    }

    [Test]
    public void R3b_ObjectInferredLambdaResult_IntoIntegerTarget_IsRefused()
        => Assert.That(SingleErrorMessage(MultiLineFunctionLambdaProbes.R3b),
            Does.Contain("Cannot assign value of type 'Object' to variable of type 'Integer'"));

    /// <summary>Mutant (k): a bare <c>Return</c> while INFERRING must still be refused, exactly
    /// like a bare Return in any other Function.</summary>
    [Test]
    public void R4_BareReturnWhileInferring_IsRefused()
        => Assert.That(SingleErrorMessage(MultiLineFunctionLambdaProbes.R4),
            Does.Contain("Function lambda must return a value"));

    [Test]
    public void R5_TargetTypedBareReturn_IsRefused()
        => Assert.That(SingleErrorMessage(MultiLineFunctionLambdaProbes.R5),
            Does.Contain("Function must return a value of type 'Integer'"));

    /// <summary>
    /// Mutant (i): see <see cref="MultiLineFunctionLambdaProbes.ArityMismatch"/>'s doc comment.
    /// The correct behaviour is the ONE arity-mismatch refusal a named Dim already gets; the
    /// mutant's relaxed guard borrows a parameter slot as if it were R and adds a second, spurious
    /// diagnostic from checking the body against it.
    /// </summary>
    [Test]
    public void ArityMismatchedFuncTarget_GivesOnlyTheAssignmentRefusal()
    {
        var (_, analyzer, ok) = Analyze(MultiLineFunctionLambdaProbes.ArityMismatch);
        Assert.That(ok, Is.False);
        var errors = ErrorMessages(analyzer);
        Assert.That(errors, Has.Count.EqualTo(1), string.Join(" | ", errors));
        Assert.That(errors[0], Does.Contain("Cannot assign value of type 'Func' to variable of type 'Func'"));
    }
}

/// <summary>
/// Execution: JavaScript (Node) and MSIL (ilasm), both the standard and aggressive pipelines;
/// C# and C++ on the F-probes, each with VB's answer.
///
/// <para>C++ is not exercised for E1-E11 here: per the implementer's byte compare and this
/// file's own front-end coverage, the front-end fix is backend-agnostic and JS+MSIL already
/// prove it. (C++'s capture-by-copy gap, #140, is closed: F1, F2 and F8 run on C++ with VB's
/// answer. So does the C# lambda body, since #136: F1, F3, F6, F7 and F8 used to be pinned as
/// known-wrong C# cells and now run on C# with VB's answer, hang-safe, standard and aggressive.)</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg (FourBackends / ReturnCoercionTests) redirects Console.Out
public class MultiLineFunctionLambdaExecutionTests
{
    private static readonly (string Source, string Label, string Expected)[] JsMsilProbes =
    {
        (MultiLineFunctionLambdaProbes.F1, "F1", MultiLineFunctionLambdaProbes.F1Expected),
        (MultiLineFunctionLambdaProbes.F2, "F2", MultiLineFunctionLambdaProbes.F2Expected),
        (MultiLineFunctionLambdaProbes.F3, "F3", MultiLineFunctionLambdaProbes.F3Expected),
        (MultiLineFunctionLambdaProbes.F4, "F4", MultiLineFunctionLambdaProbes.F4Expected),
        (MultiLineFunctionLambdaProbes.F5, "F5", MultiLineFunctionLambdaProbes.F5Expected),
        (MultiLineFunctionLambdaProbes.F6, "F6", MultiLineFunctionLambdaProbes.F6Expected),
        (MultiLineFunctionLambdaProbes.F7, "F7", MultiLineFunctionLambdaProbes.F7Expected),
        (MultiLineFunctionLambdaProbes.F8, "F8", MultiLineFunctionLambdaProbes.F8Expected),
        (MultiLineFunctionLambdaProbes.E1, "E1", MultiLineFunctionLambdaProbes.E1Expected),
        (MultiLineFunctionLambdaProbes.E2, "E2", MultiLineFunctionLambdaProbes.E2Expected),
        (MultiLineFunctionLambdaProbes.E3, "E3", MultiLineFunctionLambdaProbes.E3Expected),
        (MultiLineFunctionLambdaProbes.E4, "E4", MultiLineFunctionLambdaProbes.E4Expected),
        (MultiLineFunctionLambdaProbes.E6, "E6", MultiLineFunctionLambdaProbes.E6Expected),
        (MultiLineFunctionLambdaProbes.E9, "E9", MultiLineFunctionLambdaProbes.E9Expected),
        (MultiLineFunctionLambdaProbes.E11, "E11", MultiLineFunctionLambdaProbes.E11Expected),
    };

    /// <summary>
    /// Standard pipeline, JavaScript and MSIL. Kills mutant (a)/(a2) (every row: the crash),
    /// (a1) (F2/F4/F5/E1-E4/E9/E11 on MSIL only — the Func-branch guard), (b) is covered by the
    /// dedicated E5 test below, (c) (F5 msil: TypeLoadException instead of "42\nhi!"), (d1)/(d2)
    /// (E1: "3" or a compile error instead of "3.5"), (e) (E2: a compile error instead of "4"),
    /// (g) (E6 msil: InvalidProgramException instead of "side"), (h) (E3: a compile error instead
    /// of "s").
    /// </summary>
    [Test]
    public void StandardPipeline_JavaScriptAndMsil_MatchExpectedValues()
        => Assert.Multiple(() =>
        {
            foreach (var (source, label, expected) in JsMsilProbes)
            {
                Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(source)), Is.EqualTo(expected), label + " JavaScript, standard");
                Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(source)), Is.EqualTo(expected), label + " MSIL, standard");
            }
        });

    /// <summary>The aggressive sibling — CLAUDE.md: validate codegen through the optimizer too.</summary>
    [Test]
    public void AggressivePipeline_JavaScriptAndMsil_MatchExpectedValues()
        => Assert.Multiple(() =>
        {
            foreach (var (source, label, expected) in JsMsilProbes)
            {
                Assert.That(FourBackends.Norm(FourBackends.RunAggressiveJs(source)), Is.EqualTo(expected), label + " JavaScript, aggressive");
                Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(source)), Is.EqualTo(expected), label + " MSIL, aggressive");
            }
        });

    /// <summary>
    /// E5 (a <c>Func(Of Long)</c> target) is MSIL only — JavaScript refuses Long by design
    /// (BL7003: a JS number cannot hold it exactly). Kills mutant (b): with the target ignored,
    /// the lambda infers Integer from the literal <c>Return 5</c> instead of taking Long from the
    /// target, and the assignment into <c>Func(Of Long)</c> is refused.
    /// </summary>
    [Test]
    public void E5_LongTarget_MsilOnly_BothPipelines()
        => Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(MsilHarness.RunExpectingSuccess(MultiLineFunctionLambdaProbes.E5)),
                Is.EqualTo(MultiLineFunctionLambdaProbes.E5Expected), "standard");
            Assert.That(FourBackends.Norm(MsilHarness.RunAggressiveExpectingSuccess(MultiLineFunctionLambdaProbes.E5)),
                Is.EqualTo(MultiLineFunctionLambdaProbes.E5Expected), "aggressive");
        });

    // ---- C# and C++: every F-probe, with VB's answer ---------------------------------------

    /// <summary>
    /// ⭐ MOVED PINS (#136, #165, #179). F1 (<c>n = n + 100</c> before the Return was dropped: 3,3,0 for 3,103,0), F3 (the lambda's
    /// <c>Dim t</c> was never declared: CS0103), F6 (an If/ElseIf/Return body came out as <c>() =&gt; { ; }</c>: CS1643), F7
    /// (<c>_total = _total + v</c> dropped: 5 for 16) and F8 (the nested <c>Dim inner</c> never declared and its call written
    /// twice: four CS0103) were pinned here as known-wrong C# cells. The C# backend now writes a lambda body with the function-body
    /// emitter, so all five print VB's answer, with F2, F4 and F5. ⛔ Through the hang-safe runner
    /// (<see cref="CSharpProcessRunner"/>), never the in-process one: a lambda body can hold a loop, and a loop that never ends
    /// freezes the whole test host when it runs in process.
    /// </summary>
    [TestCase(MultiLineFunctionLambdaProbes.F1, "F1", MultiLineFunctionLambdaProbes.F1Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F2, "F2", MultiLineFunctionLambdaProbes.F2Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F3, "F3", MultiLineFunctionLambdaProbes.F3Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F4, "F4", MultiLineFunctionLambdaProbes.F4Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F5, "F5", MultiLineFunctionLambdaProbes.F5Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F6, "F6", MultiLineFunctionLambdaProbes.F6Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F7, "F7", MultiLineFunctionLambdaProbes.F7Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F8, "F8", MultiLineFunctionLambdaProbes.F8Expected)]
    public void CSharp_RunsCorrectly(string source, string label, string expected)
        => Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpForTest(source))),
            Is.EqualTo(expected), label);

    /// <summary>The same eight through the aggressive pipeline (what <c>--optimize</c> and a Release project build run).</summary>
    [TestCase(MultiLineFunctionLambdaProbes.F1, "F1", MultiLineFunctionLambdaProbes.F1Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F2, "F2", MultiLineFunctionLambdaProbes.F2Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F3, "F3", MultiLineFunctionLambdaProbes.F3Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F4, "F4", MultiLineFunctionLambdaProbes.F4Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F5, "F5", MultiLineFunctionLambdaProbes.F5Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F6, "F6", MultiLineFunctionLambdaProbes.F6Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F7, "F7", MultiLineFunctionLambdaProbes.F7Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F8, "F8", MultiLineFunctionLambdaProbes.F8Expected)]
    public void CSharp_RunsCorrectly_Aggressive(string source, string label, string expected)
        => Assert.That(FourBackends.Norm(CSharpProcessRunner.RunExpectingSuccess(ReturnCoercionTests.EmitCSharpAggressiveForTest(source))),
            Is.EqualTo(expected), label + " (aggressive)");

    [TestCase(MultiLineFunctionLambdaProbes.F1, "F1", MultiLineFunctionLambdaProbes.F1Expected)]   // was refused by name (#170), #140
    [TestCase(MultiLineFunctionLambdaProbes.F2, "F2", MultiLineFunctionLambdaProbes.F2Expected)]   // was refused by name (#170), #140
    [TestCase(MultiLineFunctionLambdaProbes.F3, "F3", MultiLineFunctionLambdaProbes.F3Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F4, "F4", MultiLineFunctionLambdaProbes.F4Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F5, "F5", MultiLineFunctionLambdaProbes.F5Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F6, "F6", MultiLineFunctionLambdaProbes.F6Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F7, "F7", MultiLineFunctionLambdaProbes.F7Expected)]
    [TestCase(MultiLineFunctionLambdaProbes.F8, "F8", MultiLineFunctionLambdaProbes.F8Expected)]   // was refused by name (#170), #140
    public void Cpp_RunsCorrectly(string source, string label, string expected)
        => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(source))), Is.EqualTo(expected), label);

    /// <summary>
    /// ⭐ MOVED PINS (#140): F1, F2 and F8 on C++. F1: <c>bump()</c>'s write to <c>n</c> never reached the
    /// caller's <c>n</c> (silently 3,3,0 for 3,103,0); F2: <c>MakeCounter</c>'s closure captured <c>c</c> by
    /// copy, so each call incremented its OWN copy (10\n10 for 11\n12); F8: the same defect one level of
    /// nesting deeper (2\n1 for 32\n21). #170 refused all three by name; #140 lowers them — the closure's
    /// environment is shared with its creator — and they run, via <see cref="Cpp_RunsCorrectly"/> above. This
    /// test pins the PATH each creator took, which the output alone cannot show.
    /// </summary>
    [TestCase(MultiLineFunctionLambdaProbes.F1, "Main", TestName = "F1_Cpp_TakesTheLoweredPath_FormerlyPinnedForTask140")]
    [TestCase(MultiLineFunctionLambdaProbes.F2, "MakeCounter", TestName = "F2_Cpp_TakesTheLoweredPath_FormerlyPinnedForTask140")]
    [TestCase(MultiLineFunctionLambdaProbes.F8, "Main", TestName = "F8_Cpp_TakesTheLoweredPath_FormerlyPinnedForTask140")]
    public void Cpp_TakesTheLoweredPath(string source, string root)
        => Assert.That(CppClosures.Compile(source).PathOf(root), Is.EqualTo(CppClosurePath.Lowered));
}
