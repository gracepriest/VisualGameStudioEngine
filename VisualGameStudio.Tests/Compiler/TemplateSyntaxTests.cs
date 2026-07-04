using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Regression tests for VB.NET-standard syntax used by the IDE's built-in
/// project templates that previously failed to parse.
/// </summary>
[TestFixture]
public class TemplateSyntaxTests
{
    private string CompileToCSharp(string source, out List<string> errors)
    {
        errors = new List<string>();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        ProgramNode ast;
        try
        {
            ast = parser.Parse();
        }
        catch (Exception ex)
        {
            errors.Add($"Parse error: {ex.Message}");
            return null;
        }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors)
                errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var csharpGen = new ImprovedCSharpCodeGenerator(new CodeGenOptions
        {
            Namespace = "TestOutput",
            GenerateMainMethod = false,
            GenerateComments = false
        });
        return csharpGen.Generate(irModule);
    }

    [Test]
    public void EndWhile_TerminatesWhileLoop()
    {
        var src = @"Module M
    Sub Run()
        Dim i As Integer = 0
        While i < 3
            i = i + 1
        End While
    End Sub
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Contain("while"));
    }

    [Test]
    public void EndWhile_AndWend_AreEquivalent()
    {
        var wend = @"Module M
    Sub Run()
        While True
        Wend
    End Sub
End Module";
        var endWhile = @"Module M
    Sub Run()
        While True
        End While
    End Sub
End Module";

        Assert.That(CompileToCSharp(wend, out var e1), Is.Not.Null, string.Join("; ", e1));
        Assert.That(CompileToCSharp(endWhile, out var e2), Is.Not.Null, string.Join("; ", e2));
    }

    [Test]
    public void ModuleLevelField_WithAccessModifierAndInitializer_Parses()
    {
        var src = @"Module GameState
    Public Score As Integer = 0
    Public Level As Integer = 1
    Public IsGameOver As Boolean = False
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Contain("Score"));
    }

    [Test]
    public void DoubledQuote_InString_IsEscapedNotTruncated()
    {
        // VB.NET escapes a literal quote by doubling it. "a""b" is the 3-char
        // string a"b, not the 1-char string a.
        var src = @"Module M
    Function Health() As String
        Return ""{""""status"""": """"ok""""}""
    End Function
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        // The generated C# must contain the escaped inner quotes, proving the
        // string was not truncated at the first doubled quote.
        Assert.That(output, Does.Contain("\\\"status\\\""));
    }

    [Test]
    public void ClassField_WithInitializer_Parses()
    {
        var src = @"Module M
    Public Class Player
        Public Name As String
        Public X As Single = 400
        Public Y As Single = 300
    End Class
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Contain("400"));
    }
}
