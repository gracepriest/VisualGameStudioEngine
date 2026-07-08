using NUnit.Framework;
using BasicLang.Compiler;
using System.Collections.Generic;
using System.Linq;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class ParserErrorTests
{
    private List<Token> Tokenize(string source)
    {
        var lexer = new Lexer(source);
        return lexer.Tokenize();
    }

    #region Parser Error Message Tests

    [Test]
    public void Parse_UnexpectedTopLevelToken_CollectsError()
    {
        var source = @"x = 10";  // Code at top level without declaration
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        // Parser is error-tolerant and collects errors instead of throwing
        parser.Parse();

        // Should have at least one error for unexpected top-level token
        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors for invalid top-level code");
        Assert.That(parser.Errors[0].Message, Does.Contain("Unexpected").IgnoreCase.Or.Contain("top level").IgnoreCase);
    }

    [Test]
    public void Parse_MissingThen_CollectsError()
    {
        var source = @"Sub Test()
    If x = 10
        PrintLine(x)
    End If
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        // Parser is error-tolerant and collects errors instead of throwing
        parser.Parse();

        // Should have at least one error for missing 'Then'
        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "Expected parser errors for missing 'Then'");
    }

    [Test]
    public void Parse_MissingCommaBetweenCallArguments_SuggestsComma()
    {
        // A ')' expected where the next token starts another argument means a comma
        // was omitted (Foo(1, 2 3)), NOT a missing closing parenthesis. The suggestion
        // must point at the comma, not the paren.
        var source = @"Sub Test()
    Foo(1, 2 3)
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);
        parser.Parse();

        Assert.That(parser.Errors.Count, Is.GreaterThan(0), "expected a parse error for the missing comma");
        Assert.That(parser.Errors.Any(e => (e.Suggestion ?? "").Contains("comma")), Is.True,
            "expected a 'missing comma' suggestion, got: " +
            string.Join(" | ", parser.Errors.Select(e => $"{e.Message} => {e.Suggestion}")));
    }

    #endregion

    #region Valid Parsing Tests

    [Test]
    public void Parse_ValidSubroutine_DoesNotThrow()
    {
        var source = @"Sub Test()
    Dim x As Integer
    x = 10
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    [Test]
    public void Parse_ValidFunction_DoesNotThrow()
    {
        var source = @"Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    [Test]
    public void Parse_ValidClass_DoesNotThrow()
    {
        var source = @"Class Player
    Private _name As String

    Public Sub New(name As String)
        _name = name
    End Sub
End Class";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    [Test]
    public void Parse_ValidIfThen_DoesNotThrow()
    {
        var source = @"Sub Test()
    If x = 10 Then
        PrintLine(x)
    End If
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    [Test]
    public void Parse_ValidForLoop_DoesNotThrow()
    {
        var source = @"Sub Test()
    For i = 1 To 10
        PrintLine(i)
    Next
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    [Test]
    public void Parse_ValidWhileLoop_DoesNotThrow()
    {
        var source = @"Sub Test()
    While x < 10
        x = x + 1
    Wend
End Sub";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    #endregion

    #region ParseException Tests

    [Test]
    public void ParseException_HasToken()
    {
        var token = new Token(TokenType.Identifier, "test", null!, 1, 1);
        var ex = new ParseException("Test error", token);

        Assert.That(ex.Token, Is.EqualTo(token));
    }

    [Test]
    public void ParseException_HasSuggestion()
    {
        var token = new Token(TokenType.Identifier, "test", null!, 1, 1);
        var ex = new ParseException("Test error", token, "Try this instead");

        Assert.That(ex.Suggestion, Is.EqualTo("Try this instead"));
    }

    [Test]
    public void ParseException_Message_IncludesLocation()
    {
        var token = new Token(TokenType.Identifier, "test", null!, 5, 10);
        var ex = new ParseException("Test error", token);

        Assert.That(ex.Message, Does.Contain("Test error"));
    }

    #endregion

    #region Structure/Type/Union Member Parsing Tests (regression: infinite loop)

    // Runs parser.Parse() with a hard timeout so a parser infinite loop fails the
    // test instead of hanging the whole run.
    private Parser ParseWithTimeout(string source, int timeoutMs = 10000)
    {
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);
        var task = System.Threading.Tasks.Task.Run(() => parser.Parse());
        Assert.That(task.Wait(timeoutMs), Is.True,
            $"Parser did not finish within {timeoutMs}ms — likely infinite loop");
        return parser;
    }

    [Test]
    public void Parse_StructureWithPublicMember_ParsesSuccessfully()
    {
        // Regression: 'Public Field1 As Integer' inside a Structure used to cause
        // an infinite loop because the member loop never consumed the modifier token.
        var source = @"Module StructTest
    Structure StructName
        Public Field1 As Integer
        Private Field2 As String
        Dim Field3 As Double
    End Structure
End Module";
        var parser = ParseWithTimeout(source);

        Assert.That(parser.Errors, Is.Empty,
            "Expected no parse errors: " + string.Join("; ", parser.Errors.Select(e => e.Message)));
    }

    [Test]
    public void Parse_StructureWithPlainMember_ParsesSuccessfully()
    {
        var source = @"Structure Point
    X As Integer
    Y As Integer
End Structure";
        var parser = ParseWithTimeout(source);

        Assert.That(parser.Errors, Is.Empty,
            "Expected no parse errors: " + string.Join("; ", parser.Errors.Select(e => e.Message)));
    }

    [Test]
    public void Parse_StructureWithUnexpectedToken_ReportsErrorInsteadOfHanging()
    {
        // Regression: garbage inside a Structure body used to spin forever.
        var source = @"Structure Broken
    While
End Structure";
        var parser = ParseWithTimeout(source);

        Assert.That(parser.Errors.Count, Is.GreaterThan(0),
            "Expected parse errors for unexpected token inside Structure");
    }

    [Test]
    public void Parse_TypeBlockWithPublicMember_ParsesSuccessfully()
    {
        var source = @"Type Person
    Public Name As String
    Age As Integer
End Type";
        var parser = ParseWithTimeout(source);

        Assert.That(parser.Errors, Is.Empty,
            "Expected no parse errors: " + string.Join("; ", parser.Errors.Select(e => e.Message)));
    }

    [Test]
    public void Parse_UnionWithPublicMember_ParsesSuccessfully()
    {
        var source = @"Union Value
    Public IntValue As Integer
    FloatValue As Single
End Union";
        var parser = ParseWithTimeout(source);

        Assert.That(parser.Errors, Is.Empty,
            "Expected no parse errors: " + string.Join("; ", parser.Errors.Select(e => e.Message)));
    }

    #endregion

    #region Module and Namespace Tests

    [Test]
    public void Parse_ValidModule_DoesNotThrow()
    {
        // Simple module with just a Sub (simpler than Function for parsing)
        var source = @"Module TestModule
    Sub DoSomething()
    End Sub
End Module";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    [Test]
    public void Parse_ValidNamespace_DoesNotThrow()
    {
        var source = @"Namespace MyApp
    Class Player
        Public Property Name As String
    End Class
End Namespace";
        var tokens = Tokenize(source);
        var parser = new Parser(tokens);

        Assert.DoesNotThrow(() => parser.Parse());
    }

    #endregion
}
