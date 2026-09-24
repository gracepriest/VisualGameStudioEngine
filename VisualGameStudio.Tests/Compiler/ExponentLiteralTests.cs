using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An exponent without a decimal point (<c>1E40</c>, <c>2E3</c>, <c>5E-3</c>) is a floating literal.
/// MEASURED on master fd67f57: the lexer read an exponent only after a '.', so <c>1E40</c> lexed as
/// the Integer 1 followed by an identifier <c>E40</c>. At module scope that was "Unexpected token
/// at top level: 'E40'"; inside a procedure the parser dropped the stray identifier and
/// <c>Dim x As Double = 1E40</c> compiled with x = 1 on every backend — <c>2E3</c> was 2.
/// </summary>
public class ExponentLiteralTests
{
    private static Token[] Lex(string source) =>
        new Lexer(source).Tokenize().Where(t => t.Type != TokenType.EOF && t.Type != TokenType.Newline).ToArray();

    [TestCase("1E40", 1e40)]
    [TestCase("2E3", 2000.0)]
    [TestCase("5E-3", 0.005)]
    [TestCase("3e+2", 300.0)]
    [TestCase("1.5E2", 150.0)]
    [TestCase("7E0", 7.0)]
    public void ExponentLiteral_IsOneDoubleToken(string text, double expected)
    {
        var tokens = Lex(text);
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Length.EqualTo(1), string.Join(" | ", tokens.Select(t => $"{t.Type}:{t.Lexeme}")));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.DoubleLiteral));
            Assert.That(tokens[0].Value, Is.EqualTo(expected));
        });
    }

    [Test]
    public void ExponentLiteral_WithFSuffix_IsASingle()
    {
        var tokens = Lex("2E3F");
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Length.EqualTo(1));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.SingleLiteral));
            Assert.That(tokens[0].Value, Is.EqualTo(2000f));
        });
    }

    /// <summary>
    /// An E with no digits after it is not an exponent: it stays for the next token. <c>1.5E</c>
    /// used to reach double.Parse as "1.5E" and throw a FormatException out of the lexer.
    /// </summary>
    [TestCase("1E", TokenType.IntegerLiteral)]
    [TestCase("1.5E", TokenType.DoubleLiteral)]
    [TestCase("1E+", TokenType.IntegerLiteral)]
    public void EWithoutDigits_IsNotConsumed(string text, TokenType firstType)
    {
        var tokens = Lex(text);
        Assert.Multiple(() =>
        {
            Assert.That(tokens[0].Type, Is.EqualTo(firstType));
            Assert.That(tokens.Length, Is.GreaterThan(1), "the E must be left for the next token");
        });
    }

    [Test]
    public void IntegerLiterals_AreUnchanged()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Lex("42")[0].Type, Is.EqualTo(TokenType.IntegerLiteral));
            Assert.That(Lex("42L")[0].Type, Is.EqualTo(TokenType.LongLiteral));
            Assert.That(Lex("42F")[0].Type, Is.EqualTo(TokenType.SingleLiteral));
        });
    }

    internal const string Program = @"
Const Big As Double = 1E40
Sub Check(tag As String, ok As Boolean)
    If ok Then
        Console.WriteLine(tag & "" ok"")
    Else
        Console.WriteLine(tag & "" BAD"")
    End If
End Sub
Sub Main()
    Dim a As Double = 1E40
    Dim b As Double = 2E3
    Dim c As Double = 5E-3
    Dim f As Single = 2E3F
    Check(""big"", a > 1.0E39)
    Check(""2E3"", b = 2000.0)
    Check(""5E-3"", c = 0.005)
    Check(""2E3F"", f = 2000.0F)
    Check(""const"", Big = a)
End Sub
";

    internal const string Expected = "big ok\n2E3 ok\n5E-3 ok\n2E3F ok\nconst ok";

    [Test]
    public void CSharp_EmitsTheExponentValues_AndCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("1E+40"), cs);
            Assert.That(cs, Does.Not.Contain("a = 1.0;"), "1E40 was lexed as 1:\n" + cs);
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(Program), Is.Empty);
        });
    }
}

/// <summary>The program run on each backend, through the optimizer.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class ExponentLiteralRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(ExponentLiteralTests.Program)),
            Is.EqualTo(ExponentLiteralTests.Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(ExponentLiteralTests.Program))),
            Is.EqualTo(ExponentLiteralTests.Expected));

    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(ExponentLiteralTests.Program))),
            Is.EqualTo(ExponentLiteralTests.Expected));
}
