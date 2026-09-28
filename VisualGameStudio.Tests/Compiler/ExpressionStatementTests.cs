using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.SemanticAnalysis;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A literal or an operator expression on a line of its own is not a statement.
///
/// <para><b>MEASURED on master</b> (<c>dc949a24</c>): <c>42</c> alone, <c>a + b</c>, or a string
/// literal on its own line reported "Compilation successful!" on every backend and the value was
/// silently thrown away — the defect docs/HANDOFF.md lists as <c>task_2e1de6b3</c>. VB refuses it
/// ("Expression is not a statement"), and it is almost always a typo for an assignment or a call.
/// The analyzer now refuses it too. A bare name or member access is still a CALL (<c>Hello</c>,
/// <c>obj.Method</c> naming a parameterless Sub), and <c>x++</c> / <c>x--</c> keep their effect.</para>
/// </summary>
[TestFixture]
public class ExpressionStatementTests
{
    private const string Header =
        "Sub Hello()\n    Console.WriteLine(\"hello\")\nEnd Sub\n\n" +
        "Sub Main()\n    Dim a As Integer = 1\n    Dim b As Integer = 2\n";

    private static string Program(string statement) => Header + "    " + statement + "\nEnd Sub\n";

    [TestCase("42")]
    [TestCase("3.5")]
    [TestCase("\"text\"")]
    [TestCase("True")]
    [TestCase("a + b")]
    [TestCase("a * 2")]
    [TestCase("a < b")]
    [TestCase("a AndAlso b")]
    public void AValueOnlyExpression_OnItsOwnLine_IsRefused(string statement)
    {
        var errors = SemanticErrors(Program(statement));
        Assert.That(string.Join("\n", errors), Does.Contain("Expression is not a statement"),
            $"`{statement}` on its own line must be refused, not silently discarded");
    }

    private static string[] SemanticErrors(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "the probe must PARSE — the refusal belongs to the analyzer");
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToArray();
    }

    [TestCase("Hello")]
    [TestCase("Hello()")]
    [TestCase("a++")]
    [TestCase("a--")]
    [TestCase("a += 1")]
    [TestCase("a = b")]
    [TestCase("Console.WriteLine(a)")]
    public void AStatementWithAnEffect_StillCompiles(string statement)
    {
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(Program(statement));
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }
}
