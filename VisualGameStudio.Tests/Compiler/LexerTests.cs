using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class LexerTests
{
    [Test]
    public void Lexer_EmptySource_ReturnsEOF()
    {
        var lexer = new Lexer("");
        var tokens = lexer.Tokenize();

        Assert.That(tokens, Is.Not.Empty);
        Assert.That(tokens.Last().Type, Is.EqualTo(TokenType.EOF));
    }

    [Test]
    public void Lexer_SingleKeyword_Sub()
    {
        var lexer = new Lexer("Sub");
        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(2)); // Sub + EOF
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Sub));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("Sub"));
    }

    [Test]
    public void Lexer_SingleKeyword_Function()
    {
        var lexer = new Lexer("Function");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Function));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("Function"));
    }

    [Test]
    public void Lexer_Integer_Literal()
    {
        var lexer = new Lexer("42");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.IntegerLiteral));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("42"));
    }

    [Test]
    public void Lexer_NegativeNumber_LiteralIsMinusFollowedByInteger()
    {
        var lexer = new Lexer("-42");
        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(3)); // Minus, Integer, EOF
        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Minus));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.IntegerLiteral));
    }

    [Test]
    public void Lexer_Double_Literal()
    {
        var lexer = new Lexer("3.14");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.DoubleLiteral));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("3.14"));
    }

    [Test]
    public void Lexer_String_Literal()
    {
        var lexer = new Lexer("\"Hello, World!\"");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.StringLiteral));
        Assert.That(tokens[0].Value, Is.EqualTo("Hello, World!"));
    }

    [Test]
    public void Lexer_EmptyString_Literal()
    {
        var lexer = new Lexer("\"\"");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.StringLiteral));
        Assert.That(tokens[0].Value, Is.EqualTo(""));
    }

    [Test]
    public void Lexer_Identifier()
    {
        var lexer = new Lexer("myVariable");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("myVariable"));
    }

    [Test]
    public void Lexer_Identifier_WithNumbers()
    {
        var lexer = new Lexer("var123");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("var123"));
    }

    [Test]
    public void Lexer_Identifier_WithUnderscore()
    {
        var lexer = new Lexer("_privateVar");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("_privateVar"));
    }

    [Test]
    public void Lexer_Operators_Plus()
    {
        var lexer = new Lexer("+");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Plus));
    }

    [Test]
    public void Lexer_Operators_Minus()
    {
        var lexer = new Lexer("-");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Minus));
    }

    [Test]
    public void Lexer_Operators_Multiply()
    {
        var lexer = new Lexer("*");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Multiply));
    }

    [Test]
    public void Lexer_Operators_Divide()
    {
        var lexer = new Lexer("/");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Divide));
    }

    [Test]
    public void Lexer_Operators_Assignment()
    {
        var lexer = new Lexer("=");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Assignment));
    }

    [Test]
    public void Lexer_Operators_NotEqual()
    {
        var lexer = new Lexer("<>");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.NotEqual));
    }

    [Test]
    public void Lexer_Operators_LessThan()
    {
        var lexer = new Lexer("<");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.LessThan));
    }

    [Test]
    public void Lexer_Operators_GreaterThan()
    {
        var lexer = new Lexer(">");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.GreaterThan));
    }

    [Test]
    public void Lexer_Operators_LessThanOrEqual()
    {
        var lexer = new Lexer("<=");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.LessThanOrEqual));
    }

    [Test]
    public void Lexer_Operators_GreaterThanOrEqual()
    {
        var lexer = new Lexer(">=");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.GreaterThanOrEqual));
    }

    [Test]
    public void Lexer_Punctuation_LeftParen()
    {
        var lexer = new Lexer("(");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.LeftParen));
    }

    [Test]
    public void Lexer_Punctuation_RightParen()
    {
        var lexer = new Lexer(")");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.RightParen));
    }

    [Test]
    public void Lexer_Punctuation_Comma()
    {
        var lexer = new Lexer(",");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Comma));
    }

    [Test]
    public void Lexer_Punctuation_Dot()
    {
        var lexer = new Lexer(".");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Dot));
    }

    [Test]
    public void Lexer_Comment_SingleLine()
    {
        var lexer = new Lexer("' This is a comment");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Comment));
        Assert.That(tokens.Last().Type, Is.EqualTo(TokenType.EOF));
    }

    [Test]
    public void Lexer_Whitespace_Ignored()
    {
        var lexer = new Lexer("   Sub   ");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Sub));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("Sub"));
    }

    [Test]
    public void Lexer_Newline_CreatesToken()
    {
        var lexer = new Lexer("Sub\nEnd Sub");
        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(3)); // Sub, Newline, EndSub, EOF
        Assert.That(tokens.Any(t => t.Type == TokenType.Newline), Is.True);
    }

    [Test]
    public void Lexer_Keywords_AreCaseInsensitive()
    {
        var lexer1 = new Lexer("Sub");
        var lexer2 = new Lexer("SUB");
        var lexer3 = new Lexer("sub");

        var tokens1 = lexer1.Tokenize();
        var tokens2 = lexer2.Tokenize();
        var tokens3 = lexer3.Tokenize();

        Assert.That(tokens1[0].Type, Is.EqualTo(TokenType.Sub));
        Assert.That(tokens2[0].Type, Is.EqualTo(TokenType.Sub));
        Assert.That(tokens3[0].Type, Is.EqualTo(TokenType.Sub));
    }

    [Test]
    public void Lexer_ComplexExpression()
    {
        var lexer = new Lexer("x = 10 + 20 * 3");
        var tokens = lexer.Tokenize();

        Assert.That(tokens.Count, Is.GreaterThanOrEqualTo(7)); // x, =, 10, +, 20, *, 3, EOF
    }

    [Test]
    public void Lexer_FunctionCall()
    {
        var lexer = new Lexer("PrintLine(\"Hello\")");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("PrintLine"));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.LeftParen));
        Assert.That(tokens[2].Type, Is.EqualTo(TokenType.StringLiteral));
        Assert.That(tokens[3].Type, Is.EqualTo(TokenType.RightParen));
    }

    [Test]
    public void Lexer_VariableDeclaration()
    {
        var lexer = new Lexer("Dim x As Integer");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Dim));
        Assert.That(tokens[0].Lexeme, Is.EqualTo("Dim"));
        Assert.That(tokens[1].Type, Is.EqualTo(TokenType.Identifier));
        Assert.That(tokens[1].Lexeme, Is.EqualTo("x"));
    }

    [Test]
    public void Lexer_Token_HasLineAndColumn()
    {
        var lexer = new Lexer("Sub Main");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Line, Is.GreaterThanOrEqualTo(1));
        Assert.That(tokens[0].Column, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void Lexer_MultipleLines_TracksLineNumbers()
    {
        var lexer = new Lexer("Sub Main\nEnd Sub");
        var tokens = lexer.Tokenize();

        // Find EndSub keyword
        var endToken = tokens.FirstOrDefault(t => t.Type == TokenType.EndSub);
        Assert.That(endToken, Is.Not.Null);
        Assert.That(endToken!.Line, Is.GreaterThan(1));
    }

    [Test]
    public void Lexer_InterpolatedString()
    {
        var lexer = new Lexer("$\"Hello {name}\"");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.InterpolatedStringLiteral));
    }

    [Test]
    public void Lexer_BooleanLiteral_True()
    {
        var lexer = new Lexer("True");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.BooleanLiteral));
        Assert.That(tokens[0].Value, Is.EqualTo(true));
    }

    [Test]
    public void Lexer_BooleanLiteral_False()
    {
        var lexer = new Lexer("False");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.BooleanLiteral));
        Assert.That(tokens[0].Value, Is.EqualTo(false));
    }

    [Test]
    public void Lexer_LongLiteral()
    {
        var lexer = new Lexer("42L");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.LongLiteral));
    }

    [Test]
    public void Lexer_SingleLiteral()
    {
        var lexer = new Lexer("3.14f");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.SingleLiteral));
    }

    [Test]
    public void Lexer_Increment()
    {
        var lexer = new Lexer("++");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Increment));
    }

    [Test]
    public void Lexer_Decrement()
    {
        var lexer = new Lexer("--");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Decrement));
    }

    [Test]
    public void Lexer_PlusAssign()
    {
        var lexer = new Lexer("+=");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.PlusAssign));
    }

    [Test]
    public void Lexer_Concatenate()
    {
        var lexer = new Lexer("&");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.Concatenate));
    }

    [Test]
    public void Lexer_AndAnd()
    {
        var lexer = new Lexer("&&");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.AndAnd));
    }

    [Test]
    public void Lexer_OrOr()
    {
        var lexer = new Lexer("||");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.OrOr));
    }

    [Test]
    public void Lexer_PreprocessorDefine()
    {
        var lexer = new Lexer("#Define DEBUG");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.PreprocessorDefine));
    }

    [Test]
    public void Lexer_PreprocessorIf()
    {
        var lexer = new Lexer("#If DEBUG");
        var tokens = lexer.Tokenize();

        Assert.That(tokens[0].Type, Is.EqualTo(TokenType.PreprocessorIf));
    }
}

[TestFixture]
public class TokenTypeTests
{
    [Test]
    public void TokenType_HasSub()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.Sub), Is.True);
    }

    [Test]
    public void TokenType_HasFunction()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.Function), Is.True);
    }

    [Test]
    public void TokenType_HasIdentifier()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.Identifier), Is.True);
    }

    [Test]
    public void TokenType_HasIntegerLiteral()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.IntegerLiteral), Is.True);
    }

    [Test]
    public void TokenType_HasDoubleLiteral()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.DoubleLiteral), Is.True);
    }

    [Test]
    public void TokenType_HasStringLiteral()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.StringLiteral), Is.True);
    }

    [Test]
    public void TokenType_HasEOF()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.EOF), Is.True);
    }

    [Test]
    public void TokenType_HasClass()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.Class), Is.True);
    }

    [Test]
    public void TokenType_HasIf()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.If), Is.True);
    }

    [Test]
    public void TokenType_HasFor()
    {
        Assert.That(Enum.IsDefined(typeof(TokenType), TokenType.For), Is.True);
    }
}

[TestFixture]
public class TokenTests
{
    [Test]
    public void Token_CanBeCreated()
    {
        var token = new Token(TokenType.Sub, "Sub", null!, 1, 1);

        Assert.That(token.Type, Is.EqualTo(TokenType.Sub));
        Assert.That(token.Lexeme, Is.EqualTo("Sub"));
        Assert.That(token.Line, Is.EqualTo(1));
        Assert.That(token.Column, Is.EqualTo(1));
    }

    [Test]
    public void Token_WithValue()
    {
        var token = new Token(TokenType.IntegerLiteral, "42", 42, 1, 1);

        Assert.That(token.Type, Is.EqualTo(TokenType.IntegerLiteral));
        Assert.That(token.Lexeme, Is.EqualTo("42"));
        Assert.That(token.Value, Is.EqualTo(42));
    }

    [Test]
    public void Token_ToString_ContainsTypeAndLexeme()
    {
        var token = new Token(TokenType.Sub, "Sub", null!, 1, 1);

        var str = token.ToString();

        Assert.That(str, Does.Contain("Sub"));
        Assert.That(str, Does.Contain("1"));
    }

    [Test]
    public void Token_Equality()
    {
        var token1 = new Token(TokenType.Sub, "Sub", null!, 1, 1);
        var token2 = new Token(TokenType.Sub, "Sub", null!, 1, 1);

        Assert.That(token1.Type, Is.EqualTo(token2.Type));
        Assert.That(token1.Lexeme, Is.EqualTo(token2.Lexeme));
    }
}
