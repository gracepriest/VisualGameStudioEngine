using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// VB's compound assignments <c>\=</c>, <c>&amp;=</c>, <c>&lt;&lt;=</c> and <c>&gt;&gt;=</c>, and
/// <c>x =-1</c> meaning <c>x = -1</c>. MEASURED on master a633b4a:
/// <list type="bullet">
/// <item><c>x \= 2</c>, <c>s &amp;= "b"</c>, <c>x &lt;&lt;= 2</c>, <c>x &gt;&gt;= 1</c> were parse errors ("End of
/// statement expected, found '\'"): the lexer had no token for any of them, although IRBuilder
/// already lowered all four.</item>
/// <item><c>x =-1</c> compiled as <c>x -= 1</c> (7 printed 6) and <c>x =+1</c> as <c>x += 1</c>: the
/// lexer turned <c>=-</c> / <c>=+</c> into MinusAssign / PlusAssign. <c>Dim x As Integer =-1</c>
/// failed to parse for the same reason.</item>
/// </list>
/// </summary>
public class CompoundAssignmentOperatorTests
{
    private static Token[] Lex(string source) =>
        new Lexer(source).Tokenize()
            .Where(t => t.Type != TokenType.EOF && t.Type != TokenType.Newline).ToArray();

    [TestCase("x \\= 2", TokenType.IntegerDivideAssign, "\\=")]
    [TestCase("s &= t", TokenType.ConcatAssign, "&=")]
    [TestCase("x <<= 2", TokenType.LeftShiftAssign, "<<=")]
    [TestCase("x >>= 1", TokenType.RightShiftAssign, ">>=")]
    public void CompoundOperator_IsOneToken(string source, TokenType type, string lexeme)
    {
        var tokens = Lex(source);
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Length.EqualTo(3), string.Join(" | ", tokens.Select(t => $"{t.Type}:{t.Lexeme}")));
            Assert.That(tokens[1].Type, Is.EqualTo(type));
            Assert.That(tokens[1].Lexeme, Is.EqualTo(lexeme));
        });
    }

    /// <summary>A space must not change the meaning: <c>=-</c> is <c>=</c> then a minus.</summary>
    [TestCase("x =-1", TokenType.Minus)]
    [TestCase("x =+1", TokenType.Plus)]
    public void EqualsThenSign_IsAssignmentThenUnaryOperator(string source, TokenType sign)
    {
        var tokens = Lex(source);
        Assert.Multiple(() =>
        {
            Assert.That(tokens[1].Type, Is.EqualTo(TokenType.Assignment), string.Join(" | ", tokens.Select(t => $"{t.Type}:{t.Lexeme}")));
            Assert.That(tokens[2].Type, Is.EqualTo(sign));
        });
    }

    /// <summary>The operators that already lexed are unchanged.</summary>
    [TestCase("a \\ b", TokenType.IntegerDivide)]
    [TestCase("a & b", TokenType.Concatenate)]
    [TestCase("a << b", TokenType.LeftShift)]
    [TestCase("a >> b", TokenType.RightShift)]
    [TestCase("a >= b", TokenType.GreaterThanOrEqual)]
    [TestCase("a <= b", TokenType.LessThanOrEqual)]
    [TestCase("a == b", TokenType.IsEqual)]
    public void NeighbouringOperators_AreUnchanged(string source, TokenType type) =>
        Assert.That(Lex(source)[1].Type, Is.EqualTo(type));

    private static string[] AnalyzerErrors(string body)
    {
        var source = "Sub Main()\n    Dim x As Integer = 7\n    Dim d As Double = 7.5\n" + body + "\nEnd Sub\n";
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors, Is.Empty, "must parse");
        var analyzer = new BasicLang.Compiler.SemanticAnalysis.SemanticAnalyzer();
        analyzer.Analyze(ast);
        return analyzer.Errors.Select(e => e.Message).ToArray();
    }

    [Test]
    public void ConcatAssign_ToANonStringTarget_IsAnError() =>
        Assert.That(AnalyzerErrors("    x &= 1"),
            Has.Some.Contains("Operator '&=' requires a String target"));

    [TestCase("    d <<= 1")]
    [TestCase("    x >>= d")]
    public void ShiftAssign_OnAFloatingOperand_IsAnError(string body) =>
        Assert.That(AnalyzerErrors(body), Has.Some.Contains("requires integral operands"));

    [TestCase("    Dim s As String = \"a\"\n    s &= \"b\"\n    s &= x\n    s &= d")]
    [TestCase("    x \\= 2\n    d \\= 2\n    x <<= 2\n    x >>= 1")]
    public void WellTypedCompoundAssignments_AreAccepted(string body) =>
        Assert.That(AnalyzerErrors(body), Is.Empty);

    internal const string Program = @"
Dim total As Integer = 100
Class Box
    Public N As Integer
    Public Label As String
End Class
Function Zero() As Integer
    Return 0
End Function
Sub Main()
    Dim x As Integer = 17
    x \= 5
    Console.WriteLine(x)
    Dim neg As Integer = -17
    neg \= 5
    Console.WriteLine(neg)
    Dim d As Double = 7.5
    d \= 2
    Console.WriteLine(d)
    total \= 7
    Console.WriteLine(total)
    Dim b As New Box()
    b.N = 50
    b.N \= 6
    b.Label = ""n=""
    b.Label &= b.N
    Console.WriteLine(b.Label)
    Dim arr(2) As Integer
    arr(1) = 99
    arr(1) \= 10
    Console.WriteLine(arr(1))
    Dim s As String = ""a""
    s &= ""b""
    s &= 3
    s &= 2.5
    Console.WriteLine(s)
    Dim sh As Integer = 5
    sh <<= 3
    Console.WriteLine(sh)
    sh >>= 2
    Console.WriteLine(sh)
    Dim z As Integer = Zero()
    Try
        x \= z
        Console.WriteLine(x)
    Catch ex As DivideByZeroException
        Console.WriteLine(""dbz"")
    End Try
    Dim m As Integer =-1
    Console.WriteLine(m)
    m =+4
    Console.WriteLine(m)
    m =-m
    Console.WriteLine(m)
    If m =-4 Then
        Console.WriteLine(""eq"")
    End If
End Sub
";

    // d \= 2 on 7.5: the Double operand converts to Long half-to-even (8), then 8 \ 2 = 4.
    internal const string Expected =
        "3\n-3\n4\n14\nn=8\n9\nab32.5\n40\n10\ndbz\n-1\n4\n-4\neq";

    [Test]
    public void CSharp_Compiles()
    {
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(Program), Is.Empty);
    }
}

/// <summary>The program on each backend, through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class CompoundAssignmentOperatorRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CompoundAssignmentOperatorTests.Program)),
            Is.EqualTo(CompoundAssignmentOperatorTests.Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(CompoundAssignmentOperatorTests.Program))),
            Is.EqualTo(CompoundAssignmentOperatorTests.Expected));

    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(CompoundAssignmentOperatorTests.Program))),
            Is.EqualTo(CompoundAssignmentOperatorTests.Expected));
}
