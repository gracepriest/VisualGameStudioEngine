using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A fluent method chain a.B().C() must emit ONE statement, not cumulative
/// duplicates (a.B(); a.B().C();) that would execute intermediate calls twice.
/// This is what the Avalonia template's builder chain hit.
/// </summary>
[TestFixture]
public class FluentChainTests
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
    public void FluentChain_EmitsSingleStatement_NotCumulativeDuplicates()
    {
        var source = @"Module M
    Public Class Builder
        Public Function Step1() As Builder
            Return Me
        End Function
        Public Function Step2() As Builder
            Return Me
        End Function
    End Class

    Sub Main()
        Dim b As New Builder()
        b.Step1().Step2()
    End Sub
End Module";

        var output = CompileToCSharp(source, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Contain("Step1().Step2()"), "the full chain must be emitted");
        // A stray "b.Step1();" statement would end the intermediate call with ";"
        // right after Step1() — the correct chain has ".Step1()." instead.
        Assert.That(output, Does.Not.Contain("Step1();"),
            "intermediate call must not be emitted as its own statement");
    }
}
