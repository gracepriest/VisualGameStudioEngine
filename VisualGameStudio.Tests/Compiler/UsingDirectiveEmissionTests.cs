using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A single-word Using that is not a resolvable BasicLang module must be
/// passed through to the generated C# as a using directive. The parser only
/// whitelists System/Microsoft/Windows/Mono as single-word .NET namespaces,
/// so third-party roots like 'Using Avalonia' were silently dropped — the
/// generated C# then failed with CS0246 on every Avalonia type (this broke
/// the Avalonia project template).
/// </summary>
[TestFixture]
public class UsingDirectiveEmissionTests
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
            GenerateMainMethod = false
        });
        return csharpGen.Generate(irModule);
    }

    [Test]
    public void SingleWordThirdPartyUsing_IsEmittedAsCSharpUsing()
    {
        var source = @"
Using Avalonia
Using Avalonia.Controls

Module Main
    Sub Main()
        PrintLine(""hi"")
    End Sub
End Module
";
        var code = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(code, Does.Contain("using Avalonia;"),
            "'Using Avalonia' must survive into the generated C# — Application/AppBuilder/Thickness live in the bare Avalonia namespace");
        Assert.That(code, Does.Contain("using Avalonia.Controls;"));
    }

    [Test]
    public void DottedUsing_StillEmitted()
    {
        var source = @"
Using System.Text

Module Main
    Sub Main()
        Dim sb As New StringBuilder()
        sb.Append(""x"")
        PrintLine(sb.ToString())
    End Sub
End Module
";
        var code = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(code, Does.Contain("using System.Text;"));
    }
}
