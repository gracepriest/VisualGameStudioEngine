using NUnit.Framework;
using BasicLang.Compiler;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class LexerErrorTests
{
    #region Unterminated String Tests

    [Test]
    public void Lex_UnterminatedString_ThrowsLexerException()
    {
        var source = @"Dim x = ""hello";
        var lexer = new Lexer(source);

        var ex = Assert.Throws<LexerException>(() => lexer.Tokenize());

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.ErrorCode, Is.EqualTo(ErrorCode.BL1001_UnterminatedString));
    }

    [Test]
    public void Lex_UnterminatedString_HasHelpfulMessage()
    {
        var source = @"Dim x = ""hello";
        var lexer = new Lexer(source);

        var ex = Assert.Throws<LexerException>(() => lexer.Tokenize());

        Assert.That(ex!.Message, Does.Contain("closing quote"));
    }

    [Test]
    public void Lex_UnterminatedString_ReportsCorrectLine()
    {
        var source = "Line1\nDim x = \"hello";
        var lexer = new Lexer(source);

        var ex = Assert.Throws<LexerException>(() => lexer.Tokenize());

        Assert.That(ex!.Line, Is.EqualTo(2));
    }

    #endregion

    #region Unterminated Interpolated String Tests

    [Test]
    public void Lex_UnterminatedInterpolatedString_ThrowsLexerException()
    {
        var source = @"Dim x = $""hello {name}";
        var lexer = new Lexer(source);

        var ex = Assert.Throws<LexerException>(() => lexer.Tokenize());

        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.ErrorCode, Is.EqualTo(ErrorCode.BL1002_UnterminatedInterpolatedString));
    }

    [Test]
    public void Lex_UnterminatedInterpolatedString_HasHelpfulMessage()
    {
        var source = @"Dim x = $""hello {name}";
        var lexer = new Lexer(source);

        var ex = Assert.Throws<LexerException>(() => lexer.Tokenize());

        Assert.That(ex!.Message, Does.Contain("closing quote"));
    }

    #endregion

    #region Valid String Tests

    [Test]
    public void Lex_ValidString_DoesNotThrow()
    {
        var source = @"Dim x = ""hello""";
        var lexer = new Lexer(source);

        Assert.DoesNotThrow(() => lexer.Tokenize());
    }

    [Test]
    public void Lex_ValidInterpolatedString_DoesNotThrow()
    {
        var source = @"Dim x = $""hello {name}""";
        var lexer = new Lexer(source);

        Assert.DoesNotThrow(() => lexer.Tokenize());
    }

    [Test]
    public void Lex_StringWithEscapedQuote_DoesNotThrow()
    {
        var source = @"Dim x = ""hello \""world\""""";
        var lexer = new Lexer(source);

        Assert.DoesNotThrow(() => lexer.Tokenize());
    }

    [Test]
    public void Lex_EmptyString_DoesNotThrow()
    {
        var source = @"Dim x = """"";
        var lexer = new Lexer(source);

        Assert.DoesNotThrow(() => lexer.Tokenize());
    }

    #endregion

    #region LexerException Format Tests

    [Test]
    public void LexerException_ToString_IncludesErrorCode()
    {
        var ex = new LexerException(
            ErrorCode.BL1001_UnterminatedString,
            "Test message",
            1, 5,
            "test source");

        var result = ex.ToString();

        Assert.That(result, Does.Contain("BL1001"));
    }

    [Test]
    public void LexerException_ToString_IncludesLineAndColumn()
    {
        var ex = new LexerException(
            ErrorCode.BL1001_UnterminatedString,
            "Test message",
            10, 15,
            "test source");

        var result = ex.ToString();

        Assert.That(result, Does.Contain("line 10"));
        Assert.That(result, Does.Contain("column 15"));
    }

    #endregion

    #region Token Types Tests

    [Test]
    public void Lex_Integer_ReturnsIntegerLiteral()
    {
        var source = "123";
        var lexer = new Lexer(source);

        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IntegerLiteral));
    }

    [Test]
    public void Lex_String_ReturnsStringLiteral()
    {
        var source = @"""hello""";
        var lexer = new Lexer(source);

        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.StringLiteral));
    }

    [Test]
    public void Lex_InterpolatedString_ReturnsInterpolatedStringLiteral()
    {
        var source = @"$""hello {x}""";
        var lexer = new Lexer(source);

        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(1));
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.InterpolatedStringLiteral));
    }

    [Test]
    public void Lex_Keywords_ReturnsCorrectTypes()
    {
        var source = "Dim If Then Else End Sub Function";
        var lexer = new Lexer(source);

        var tokens = lexer.Tokenize();

        Assert.That(tokens.Any(t => t.Type == TokenType.Dim), Is.True);
        Assert.That(tokens.Any(t => t.Type == TokenType.If), Is.True);
        Assert.That(tokens.Any(t => t.Type == TokenType.Then), Is.True);
        Assert.That(tokens.Any(t => t.Type == TokenType.Else), Is.True);
        Assert.That(tokens.Any(t => t.Type == TokenType.Sub), Is.True);
        Assert.That(tokens.Any(t => t.Type == TokenType.Function), Is.True);
    }

    #endregion
}
