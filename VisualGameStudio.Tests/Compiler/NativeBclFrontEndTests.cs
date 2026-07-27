using System.Collections;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class NativeBclFrontEndTests
{
    private static ProgramNode Parse(string source)
    {
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    [Test]
    public void NumericLiteral_CarriesLexemeText()
    {
        var ast = Parse("Module M\n Sub Main()\n Dim d As Double = 1.50\n End Sub\nEnd Module");
        var lit = FindFirstNumericLiteral(ast);
        Assert.That(lit, Is.Not.Null);
        Assert.That(lit!.Text, Is.EqualTo("1.50"), "the literal's source text must survive parsing (scale is lost in the double 1.5)");
    }

    /// <summary>
    /// Small recursive walker over the AST that finds the first LiteralExpressionNode whose
    /// LiteralType is a numeric literal kind. Walks generically via reflection over every
    /// public property so it does not depend on a specific node's exact child shape.
    /// </summary>
    private static LiteralExpressionNode? FindFirstNumericLiteral(ASTNode node)
    {
        if (node is LiteralExpressionNode literal && IsNumericLiteral(literal.LiteralType))
        {
            return literal;
        }

        foreach (var property in node.GetType().GetProperties())
        {
            object? value;
            try
            {
                value = property.GetValue(node);
            }
            catch
            {
                continue;
            }

            if (value is ASTNode childNode)
            {
                var found = FindFirstNumericLiteral(childNode);
                if (found != null)
                {
                    return found;
                }
            }
            else if (value is IEnumerable enumerable and not string)
            {
                foreach (var item in enumerable)
                {
                    if (item is ASTNode childItem)
                    {
                        var found = FindFirstNumericLiteral(childItem);
                        if (found != null)
                        {
                            return found;
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool IsNumericLiteral(TokenType literalType)
    {
        return literalType is TokenType.IntegerLiteral or TokenType.LongLiteral
            or TokenType.SingleLiteral or TokenType.DoubleLiteral;
    }
}
