using System.Globalization;
using System.Linq;
using BasicLang.Compiler;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec ┬º4.11 D1/D15, spec-claims #6ΓÇô#7 ΓÇö `0.1D` lexed as `0.1` followed by an identifier `D` (ScanNumber knows only F
/// and L), and CDec was not a registered function at all, so `CDec("0.1")` typed as Object.
/// </summary>
[TestFixture]
public class DecimalLiteralTests
{
    private static Token[] Lex(string s) => new Lexer(s).Tokenize().Where(t => t.Type != TokenType.EOF && t.Type != TokenType.Newline).ToArray();

    [TestCase("0.1D", "0.1")]
    [TestCase("1.10D", "1.10")]
    [TestCase("5D", "5")]
    [TestCase("5d", "5")]
    [TestCase("79228162514264337593543950335D", "79228162514264337593543950335")]
    [TestCase("0.0000000000000000000000000001D", "0.0000000000000000000000000001")]
    public void ADSuffix_IsOneDecimalLiteral_KeepingItsScale(string text, string value)
    {
        var tokens = Lex(text);
        Assert.Multiple(() =>
        {
            Assert.That(tokens, Has.Length.EqualTo(1));
            Assert.That(tokens[0].Type, Is.EqualTo(TokenType.DecimalLiteral));
            Assert.That(((decimal)tokens[0].Value).ToString(CultureInfo.InvariantCulture), Is.EqualTo(value));
        });
    }

    [Test]
    public void AnIdentifierAfterTheDigits_IsNotASuffix() =>
        Assert.That(Lex("1Dx").Select(t => t.Type), Is.EqualTo(new[] { TokenType.IntegerLiteral, TokenType.Identifier }));

    /// <summary>In a Decimal context the suffix changes nothing in the C# output; inferred, it makes the variable Decimal.</summary>
    [Test]
    public void TheCSharpOutput()
    {
        var withSuffix = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim d As Decimal = 0.1D\n Console.WriteLine(d)\nEnd Sub");
        var without = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim d As Decimal = 0.1\n Console.WriteLine(d)\nEnd Sub");
        var inferred = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim x = 0.1D\n Console.WriteLine(x)\nEnd Sub");
        var cdec = CSharpTestSupport.CompileToCSharp("Sub Main()\n Dim d As Decimal = CDec(\"0.1\")\n Console.WriteLine(d)\nEnd Sub");
        Assert.Multiple(() =>
        {
            Assert.That(withSuffix, Is.EqualTo(without));
            Assert.That(withSuffix, Does.Contain("0.1m"));
            Assert.That(inferred, Does.Contain("decimal x").And.Contain("0.1m"));
            Assert.That(cdec, Does.Contain("Convert.ToDecimal("));
        });
    }
}
