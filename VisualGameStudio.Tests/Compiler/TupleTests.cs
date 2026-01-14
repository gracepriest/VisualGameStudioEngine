using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using System.Collections.Generic;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class TupleTests
{
    private List<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source);
        return lexer.Tokenize();
    }

    private ProgramNode Parse(string source)
    {
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);
        return parser.Parse();
    }

    #region Tuple Type Parsing Tests

    [Test]
    public void Parse_TupleType_Simple()
    {
        var source = @"Function GetPoint() As (Integer, Integer)
    Return (10, 20)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleType_Named()
    {
        var source = @"Function GetPoint() As (x As Integer, y As Integer)
    Return (10, 20)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleType_ThreeElements()
    {
        var source = @"Function GetColor() As (Integer, Integer, Integer)
    Return (255, 128, 64)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    #endregion

    #region Tuple Literal Parsing Tests

    [Test]
    public void Parse_TupleLiteral_Simple()
    {
        // Note: BasicLang requires explicit type declarations
        var source = @"Sub Test()
    Dim point As (Integer, Integer) = (10, 20)
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleLiteral_WithExpressions()
    {
        var source = @"Sub Test()
    Dim x As Integer = 5
    Dim point As (Integer, Integer) = (x * 2, x + 10)
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    #endregion

    #region Tuple Deconstruction Tests

    [Test]
    public void Parse_TupleDeconstruction_Simple()
    {
        var source = @"Sub Test()
    Dim (x, y) = GetPoint()
End Sub

Function GetPoint() As (Integer, Integer)
    Return (10, 20)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleDeconstruction_ThreeVariables()
    {
        var source = @"Sub Test()
    Dim (r, g, b) = GetColor()
End Sub

Function GetColor() As (Integer, Integer, Integer)
    Return (255, 128, 64)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleDeconstruction_FromLiteral()
    {
        var source = @"Sub Test()
    Dim (x, y) = (100, 200)
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    #endregion

    #region Tuple Return Statement Tests

    [Test]
    public void Parse_TupleReturn_Simple()
    {
        var source = @"Function GetMinMax() As (Integer, Integer)
    Return (1, 100)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleReturn_WithVariables()
    {
        var source = @"Function GetMinMax(start As Integer, finish As Integer) As (Integer, Integer)
    Dim minVal As Integer = start
    Dim maxVal As Integer = finish
    Return (minVal, maxVal)
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    #endregion

    #region Tuple Usage in Expressions Tests

    [Test]
    public void Parse_TupleInAssignment()
    {
        var source = @"Sub Test()
    Dim point As (Integer, Integer)
    point = (50, 75)
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    [Test]
    public void Parse_TupleAsParameter()
    {
        var source = @"Sub ProcessPoint(point As (Integer, Integer))
    PrintLine(point.Item1)
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
        Assert.That(parser.Errors.Count, Is.EqualTo(0), "Should have no parse errors");
    }

    #endregion
}
