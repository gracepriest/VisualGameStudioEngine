using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Is</c> / <c>IsNot</c> — reference identity, and VB's null test <c>x Is Nothing</c> — and
/// <c>Not</c>'s VB precedence.
///
/// <para>⛔ Neither was an operator. <c>If x Is Nothing Then</c> failed "Expected 'Then' after If
/// condition" and <c>Console.WriteLine(x Is Nothing)</c> "Expected ')' after arguments": the lexer
/// had an <c>Is</c> token only for <c>Case Is</c>, and <c>IsNot</c> lexed as an identifier. And
/// <c>Not</c> bound at unary precedence, so <c>Not x Is Nothing</c> — and <c>Not n = 5</c> — read
/// as <c>(Not x) …</c> and were refused ("Logical NOT requires Boolean operand"). In VB, Not binds
/// looser than every comparison.</para>
/// </summary>
[TestFixture]
public class IsOperatorTests
{
    private static (ProgramNode Ast, string Errors) Analyze(string body)
    {
        var parser = new Parser(new Lexer(
            "Class Box\nEnd Class\nSub Main()\n    Dim a As Box\n    Dim n As Integer = 5\n    " + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return (ast, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return (ast, string.Join("; ", analyzer.Errors.Select(e => e.Message)));
    }

    [TestCase("If a Is Nothing Then n = 1")]
    [TestCase("If a IsNot Nothing Then n = 1")]
    [TestCase("Dim t As Boolean = a Is Nothing")]
    [TestCase("Console.WriteLine(a IsNot Nothing)")]
    [TestCase("Dim t As Boolean = Not a Is Nothing")]
    [TestCase("Dim t As Boolean = Not n = 5")]
    [TestCase("Dim t As Boolean = a Is Nothing OrElse n = 0")]
    [TestCase("Dim s As String = \"x\"\n    Dim t As Boolean = s IsNot Nothing")]
    public void EveryShape_Analyzes(string body)
        => Assert.That(Analyze(body).Errors, Is.Empty);

    /// <summary>VB refuses Is on a value (BC30020): `=` compares values.</summary>
    [TestCase("Dim t As Boolean = n Is 5")]
    [TestCase("Dim t As Boolean = n IsNot Nothing")]
    public void AValueOperand_IsRefused(string body)
        => Assert.That(Analyze(body).Errors, Does.Contain("compares references"));

    /// <summary><c>Not a Is Nothing</c> is <c>Not (a Is Nothing)</c>, not <c>(Not a) Is Nothing</c>.</summary>
    [Test]
    public void Not_BindsLooserThanIs()
    {
        var (ast, errors) = Analyze("Dim t As Boolean = Not a Is Nothing");
        Assert.That(errors, Is.Empty);
        var main = ast.Declarations.OfType<SubroutineNode>().Single(s => s.Name == "Main");
        var decl = main.Body.Statements.OfType<VariableDeclarationNode>().Last();
        var not = (UnaryExpressionNode)decl.Initializer;
        Assert.That(not.Operator, Is.EqualTo("Not"));
        Assert.That(((BinaryExpressionNode)not.Operand).Operator, Is.EqualTo("Is"));
    }
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class IsOperatorExecutionTests
{
    private const string Program = """
        Class Box
            Public V As Integer
            Public Sub New(v As Integer)
                Me.V = v
            End Sub
        End Class

        Function Describe(b As Box) As String
            If b Is Nothing OrElse b.V = 0 Then Return "empty"
            Return "box " & CStr(b.V)
        End Function

        Sub Main()
            Dim a As Box
            Dim x As New Box(1)
            Dim y As New Box(1)
            Dim same As Box = x
            If a Is Nothing Then Console.WriteLine("a is Nothing")
            If x IsNot Nothing Then Console.WriteLine("x is something")
            Console.WriteLine(Describe(a) & " | " & Describe(x))
            Console.WriteLine(Not a Is Nothing)
            Console.WriteLine(x IsNot Nothing AndAlso x.V = 1)
            Console.WriteLine(x Is y)
            Console.WriteLine(x Is same)
            Console.WriteLine(x IsNot y)
            Dim n As Integer = 5
            Console.WriteLine(Not n = 5)
            Console.WriteLine(Not n > 3 And n < 10)
            Select Case n
                Case Is > 3
                    Console.WriteLine("big")
            End Select
            Dim s As String = "hi"
            Console.WriteLine(s IsNot Nothing)
        End Sub
        """;

    private const string Expected =
        "a is Nothing\nx is something\nempty | box 1\nFalse\nTrue\nFalse\nTrue\nTrue\nFalse\nFalse\nbig\nTrue";

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
