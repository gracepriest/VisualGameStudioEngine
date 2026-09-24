using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A number with an exponent but no decimal point is floating point, as in VB: <c>1E+15</c> is a
/// Double and <c>1E+15F</c> a Single. The lexer only read an exponent after a decimal point, so
/// <c>1E+15</c> lexed as the integer 1 followed by the identifier E ("Undefined identifier 'E'"),
/// and <c>5E3</c> as 5 followed by an identifier E3 that the analyzer let through: the program
/// compiled and printed 5.
/// </summary>
[TestFixture]
public class ExponentLiteralTests
{
    private static Token OnlyNumber(string literal)
    {
        var tokens = new Lexer("Dim d = " + literal).Tokenize()
            .Where(t => t.Type is TokenType.IntegerLiteral or TokenType.LongLiteral
                or TokenType.SingleLiteral or TokenType.DoubleLiteral)
            .ToList();
        Assert.That(tokens, Has.Count.EqualTo(1), $"'{literal}' should be ONE number token");
        return tokens[0];
    }

    [TestCase("1E+15", 1e15)]
    [TestCase("1e15", 1e15)]
    [TestCase("2E-3", 0.002)]
    [TestCase("5E3", 5000.0)]
    [TestCase("7e0", 7.0)]
    [TestCase("1.5E+3", 1500.0)]
    public void AnExponent_MakesADouble_WithOrWithoutADecimalPoint(string literal, double expected)
    {
        var token = OnlyNumber(literal);
        Assert.That(token.Type, Is.EqualTo(TokenType.DoubleLiteral));
        Assert.That(token.Value, Is.EqualTo(expected));
    }

    [Test]
    public void AnFSuffix_AfterAnExponent_MakesASingle()
    {
        var token = OnlyNumber("1E+15F");
        Assert.That(token.Type, Is.EqualTo(TokenType.SingleLiteral));
        Assert.That(token.Value, Is.EqualTo(1e15f));
    }

    [TestCase("42", TokenType.IntegerLiteral)]
    [TestCase("12L", TokenType.LongLiteral)]
    [TestCase("2.5", TokenType.DoubleLiteral)]
    public void NumbersWithoutAnExponent_AreUnchanged(string literal, TokenType type)
        => Assert.That(OnlyNumber(literal).Type, Is.EqualTo(type));

    /// <summary>An E not followed by digits is not an exponent: the number stops before it.</summary>
    [Test]
    public void AnEWithoutDigits_IsNotSwallowed()
    {
        var tokens = new Lexer("Dim d = 3E").Tokenize();
        Assert.That(tokens.Any(t => t.Type == TokenType.IntegerLiteral && (int)t.Value! == 3), Is.True);
        Assert.That(tokens.Any(t => t.Type == TokenType.Identifier && t.Lexeme == "E"), Is.True);
    }

    [Test]
    public void AHexLiteral_KeepsItsEAsADigit()
    {
        var token = new Lexer("Dim d = &HE3").Tokenize().Single(t => t.Type == TokenType.IntegerLiteral);
        Assert.That(token.Value, Is.EqualTo(0xE3));
    }
}
