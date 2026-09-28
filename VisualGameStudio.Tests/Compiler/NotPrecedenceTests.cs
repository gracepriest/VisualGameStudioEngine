using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Not</c> at VB's precedence (#195): looser than every comparison — <c>Is</c>/<c>IsNot</c>
/// included — and tighter than <c>And</c>/<c>Or</c>.
///
/// <para>⛔ <c>Not</c> bound at unary precedence, so <c>Not x Is Nothing</c> read as
/// <c>(Not x) Is Nothing</c> (refused by ADR-0011 D1 (3)) and <c>Not n = 5</c> as
/// <c>(Not n) = 5</c> ("Logical NOT requires Boolean operand"). VB reads both as <c>Not (…)</c>.
/// BasicLang has TWO expression parsers — the recursive-descent chain and the precedence-climbing
/// continuation — and each needs the rule, so every shape is checked in a <c>Dim</c> initializer and
/// as a plain assignment.</para>
/// </summary>
[TestFixture]
public class NotPrecedenceTests
{
    private static (ProgramNode Ast, string Errors) Analyze(string body)
    {
        var parser = new Parser(new Lexer(
            "Class Box\nEnd Class\nSub Main()\n    Dim a As Box\n    Dim n As Integer = 5\n    Dim t As Boolean\n    " +
            body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return (ast, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return (ast, string.Join("; ", analyzer.Errors.Select(e => e.Message)));
    }

    [TestCase("If Not a Is Nothing Then n = 1")]
    [TestCase("Dim u As Boolean = Not a Is Nothing")]
    [TestCase("t = Not a Is Nothing")]
    [TestCase("Dim u As Boolean = Not n = 5")]
    [TestCase("t = Not n = 5")]
    [TestCase("Dim u As Boolean = n > 0 AndAlso Not a IsNot Nothing")]
    [TestCase("t = n > 0 AndAlso Not a IsNot Nothing")]
    [TestCase("t = n > 0 OrElse Not Not n < 3")]
    [TestCase("Dim u As Boolean = Not n > 3 And n < 10")]
    [TestCase("Dim b As Boolean = True\n    t = Not b")]
    public void EveryShape_Analyzes(string body)
        => Assert.That(Analyze(body).Errors, Is.Empty);

    /// <summary>Parse only: a bare expression statement parses (through the continuation parser)
    /// but the analyzer rightly refuses it as a discarded value.</summary>
    private static ProgramNode Parse(string body)
    {
        var parser = new Parser(new Lexer(
            "Sub Main()\n    Dim a As Object\n    Dim n As Integer = 5\n    Dim t As Boolean\n    " +
            body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors.Select(e => e.Message), Is.Empty);
        return ast;
    }

    private static UnaryExpressionNode NotIn(ExpressionNode e) => e switch
    {
        UnaryExpressionNode u => u,
        BinaryExpressionNode b => NotIn(b.Left) ?? NotIn(b.Right),
        _ => null
    };

    private static ExpressionNode LastValue(ProgramNode ast)
    {
        var main = ast.Declarations.OfType<SubroutineNode>().Single(s => s.Name == "Main");
        return main.Body.Statements.Last() switch
        {
            VariableDeclarationNode d => d.Initializer,
            AssignmentStatementNode a => a.Value,
            ExpressionStatementNode e => e.Expression,
            var s => throw new AssertionException("unexpected statement " + s.GetType().Name)
        };
    }

    /// <summary><c>Not</c>'s operand is the WHOLE comparison, in both parsers.</summary>
    [TestCase("Dim u As Boolean = Not a Is Nothing", "Is")]
    [TestCase("t = Not a Is Nothing", "Is")]
    [TestCase("Dim u As Boolean = Not n = 5", "=")]
    [TestCase("t = Not n = 5", "=")]
    [TestCase("t = n > 0 AndAlso Not a IsNot Nothing", "IsNot")]
    // A bare expression statement: the PRECEDENCE-CLIMBING continuation parses everything after
    // its first operand (ParseNotContinuation).
    [TestCase("a Is Nothing AndAlso Not a Is Nothing", "Is")]
    [TestCase("a Is Nothing OrElse Not n = 5", "=")]
    public void Not_TakesTheWholeComparison(string body, string op)
    {
        var not = NotIn(LastValue(Parse(body)));
        Assert.That(not, Is.Not.Null);
        Assert.That(not.Operator, Is.EqualTo("Not"));
        Assert.That(((BinaryExpressionNode)not.Operand).Operator, Is.EqualTo(op));
    }

    /// <summary>…and stops at <c>And</c>: <c>Not x And y</c> is <c>(Not x) And y</c>.</summary>
    [TestCase("Dim u As Boolean = Not n > 3 And n < 10")]
    [TestCase("t = Not n > 3 And n < 10")]
    [TestCase("t = n = 0 Or Not n > 3 And n < 10")]
    [TestCase("a Is Nothing Or Not n > 3 And n < 10")]
    public void Not_StopsAtAnd(string body)
    {
        var not = NotIn(LastValue(Parse(body)));
        Assert.That(((BinaryExpressionNode)not.Operand).Operator, Is.EqualTo(">"));
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class NotPrecedenceExecutionTests
{
    private const string Program = """
        Class Box
            Public V As Integer
            Public Sub New(v As Integer)
                Me.V = v
            End Sub
        End Class

        Sub Main()
            Dim a As Box
            Dim x As New Box(1)
            If Not a Is Nothing Then Console.WriteLine("wrong") Else Console.WriteLine("a is Nothing")
            If Not x Is Nothing Then Console.WriteLine("x is something")
            Console.WriteLine(Not a Is Nothing)
            Dim t As Boolean
            t = Not x Is Nothing
            Console.WriteLine(t)
            Dim n As Integer = 5
            Console.WriteLine(Not n = 5)
            t = Not n = 4
            Console.WriteLine(t)
            Console.WriteLine(Not n > 3 And n < 10)
            t = n = 0 Or Not n > 3 And n < 10
            Console.WriteLine(t)
            t = x IsNot Nothing AndAlso Not x.V = 2
            Console.WriteLine(t)
            Console.WriteLine(Not Not n < 3)
            Dim b As Boolean = True
            t = Not b
            Console.WriteLine(t)
        End Sub
        """;

    private const string Expected =
        "a is Nothing\nx is something\nFalse\nTrue\nFalse\nTrue\nFalse\nFalse\nTrue\nFalse\nFalse";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
        Assert.That(Normalize(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnJavaScript_Optimized() =>
        Assert.That(Normalize(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));

    [Test]
    public void OnCpp()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(Program));
        Assert.That(Normalize(VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cpp, compiler.Value)),
            Is.EqualTo(Expected));
    }

    [Test]
    public void OnCSharp() =>
        Assert.That(Normalize(CliTestHarness.CompileRunCSharp(Program)), Is.EqualTo(Expected));
}
