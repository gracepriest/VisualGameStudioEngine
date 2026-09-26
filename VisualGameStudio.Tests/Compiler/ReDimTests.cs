using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>ReDim [Preserve] a[count]</c> / <c>ReDim [Preserve] a(upperBound)</c>, the same size rule as
/// Dim, with a size that may be computed at run time.
///
/// <para>It used to compile "successfully" into garbage: `ReDim a(n)` fell through to the
/// expression-statement path and became a call to a nonexistent function <c>ReDim(a[n])</c>, and
/// `ReDim Preserve a(6)` became <c>ReDim(Preserve)</c>, dropping the array and its size. The
/// editor still offered ReDim in completions. It now parses to the assignment
/// <c>a = &lt;resize&gt;</c> and every backend lowers the resize natively.</para>
/// </summary>
[TestFixture]
public class ReDimTests
{
    private static StatementNode FirstStatementOfMain(string body)
    {
        var parser = new Parser(new Lexer("Sub Main()\n" + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, string.Join("; ", parser.Errors.Select(e => e.Message)));
        var main = ast.Declarations.OfType<SubroutineNode>().Single();
        return main.Body.Statements.OfType<AssignmentStatementNode>().Single();
    }

    [Test]
    public void ReDim_IsAnAssignmentOfAResize_WithTheDimSizeRule()
    {
        var paren = (AssignmentStatementNode)FirstStatementOfMain("Dim a[] As Integer\nReDim a(4)");
        Assert.That(((IdentifierExpressionNode)paren.Target).Name, Is.EqualTo("a"));
        var resize = (ArrayResizeExpressionNode)paren.Value;
        Assert.That(resize.Preserve, Is.False);
        Assert.That(((LiteralExpressionNode)resize.Size).Value, Is.EqualTo(5), "(4) is an upper bound");

        var bracket = (ArrayResizeExpressionNode)((AssignmentStatementNode)
            FirstStatementOfMain("Dim a[] As Integer\nReDim Preserve a[4]")).Value;
        Assert.That(bracket.Preserve, Is.True);
        Assert.That(((LiteralExpressionNode)bracket.Size).Value, Is.EqualTo(4), "[4] is a count");
    }

    [Test]
    public void AVariableNamedReDim_StillWorks()
    {
        var parser = new Parser(new Lexer(
            "Sub Main()\nDim ReDim As Integer = 5\nReDim = ReDim + 1\nConsole.WriteLine(ReDim)\nEnd Sub").Tokenize());
        parser.Parse();
        Assert.That(parser.Errors, Is.Empty);
    }

    private static string Errors(string body)
    {
        var parser = new Parser(new Lexer("Sub Main()\n" + body + "\nEnd Sub").Tokenize());
        var ast = parser.Parse();
        if (parser.Errors.Count > 0) return string.Join("; ", parser.Errors.Select(e => e.Message));
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        return string.Join("; ", analyzer.Errors.Select(e => e.Message));
    }

    [TestCase("Dim x As Integer\nReDim x[3]", "ReDim needs an array")]
    [TestCase("Dim g[2, 2] As Integer\nReDim g[3]", "one-dimensional")]
    [TestCase("Dim a[] As Integer\nReDim a[3] As String", "cannot change the element type")]
    [TestCase("Dim a[] As Integer\nDim b[] As Integer\nReDim a[3], b[4]", "one array per statement")]
    [TestCase("Dim a[] As Integer\nReDim a[2, 2]", "exactly one size")]
    [TestCase("Dim a[] As Integer\nReDim a", "Expected '(' or '['")]
    public void AMisuse_IsRefusedWithAReason(string body, string reason)
        => Assert.That(Errors(body), Does.Contain(reason));

    [Test]
    public void ARunTimeSize_IsAllowed_AndTheAsClauseMayRestateTheType()
        => Assert.That(Errors("Dim a[] As Integer\nDim n As Integer = 3\nReDim a[n * 2] As Integer"), Is.Empty);

    [Test]
    public void TheDimDiagnostic_PointsToReDim()
        => Assert.That(Errors("Dim n As Integer = 3\nDim a[n] As Integer"), Does.Contain("ReDim a[n]"));
}

[TestFixture]
[Category("Integration")]   // spawns node, a C++ compiler and dotnet
[NonParallelizable]
public class ReDimExecutionTests
{
    /// <summary>
    /// Plain and Preserve, growing and shrinking, a String array, a class field resized from a
    /// method, and the grow-in-a-loop idiom. The expected text is what the C# backend (.NET) prints.
    /// </summary>
    private const string Program = @"
Class Bag
    Public Items() As Integer
    Public Sub Grow(n As Integer)
        ReDim Preserve Items[n]
    End Sub
End Class

Function Four() As Integer
    Return 4
End Function

Sub Main()
    Dim a[] As Integer
    Dim n As Integer = Four()
    ReDim a(n)
    a(4) = 9
    Console.WriteLine(a.Length)
    ReDim Preserve a[7]
    Console.WriteLine(a.Length & "" "" & a(4) & "" "" & a(6))
    ReDim a[2]
    Console.WriteLine(a.Length & "" "" & a(0))
    a(1) = 5
    ReDim Preserve a(0)
    Console.WriteLine(a.Length & "" "" & a(0))
    Dim s() As String
    ReDim s[2]
    s(0) = ""x""
    ReDim Preserve s[3]
    Console.WriteLine(s(0) & ""|"" & s(2) & ""|"" & s.Length)
    Dim b As New Bag()
    b.Grow(3)
    b.Items(2) = 8
    b.Grow(5)
    Console.WriteLine(b.Items.Length & "" "" & b.Items(2))
    Dim acc() As Integer
    ReDim acc[0]
    For i As Integer = 1 To 5
        ReDim Preserve acc[i]
        acc(i - 1) = i * i
    Next
    Dim t As Integer = 0
    For Each v As Integer In acc
        t = t + v
    Next
    Console.WriteLine(acc.Length & "" "" & t)
End Sub";

    private const string Expected = "5\n7 9 0\n2 0\n1 0\nx||3\n5 8\n5 55";

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd('\n');

    [Test]
    public void OnJavaScript() =>
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
