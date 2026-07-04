using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A user-defined Sub/Function must take precedence over a stdlib builtin of
/// the same name (e.g. a user "Run" must not generate Process.Start).
/// </summary>
[TestFixture]
public class BuiltinCollisionTests
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
    public void UserSubNamedRun_TakesPrecedenceOverBuiltin()
    {
        var src = @"Module M
    Sub Run()
        Console.WriteLine(""user run"")
    End Sub

    Sub Main()
        Run()
    End Sub
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("Process.Start"),
            "user-defined Run must not emit the Process.Start builtin");
        Assert.That(output, Does.Contain("Run()"),
            "the call should invoke the user's Run method");
    }

    [Test]
    public void BuiltinRun_StillWorks_WhenNoUserSymbol()
    {
        // With no user-defined Run, the stdlib Run(command, args) still resolves.
        var src = @"Module M
    Sub Main()
        Dim code As Integer = Run(""echo hi"", """")
    End Sub
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
    }
}
