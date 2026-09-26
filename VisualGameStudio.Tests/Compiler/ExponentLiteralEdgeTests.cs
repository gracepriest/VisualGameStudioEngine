using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Edges of exponent lexing that <see cref="ExponentLiteralTests"/> does not pin: a signed
/// exponent with no decimal point, the F suffix after one, and the hex scanner keeping <c>E</c>
/// as a digit.
/// </summary>
[TestFixture]
public class ExponentLiteralEdgeTests
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
    [TestCase("1.5E+3", 1500.0)]
    public void ASignedOrLowerCaseExponent_MakesADouble(string literal, double expected)
    {
        var token = OnlyNumber(literal);
        Assert.That(token.Type, Is.EqualTo(TokenType.DoubleLiteral));
        Assert.That(token.Value, Is.EqualTo(expected));
    }

    [Test]
    public void AnFSuffix_AfterASignedExponent_MakesASingle()
    {
        var token = OnlyNumber("1E+15F");
        Assert.That(token.Type, Is.EqualTo(TokenType.SingleLiteral));
        Assert.That(token.Value, Is.EqualTo(1e15f));
    }

    [Test]
    public void AHexLiteral_KeepsItsEAsADigit()
    {
        var token = new Lexer("Dim d = &HE3").Tokenize().Single(t => t.Type == TokenType.IntegerLiteral);
        Assert.That(token.Value, Is.EqualTo(0xE3));
    }
}
