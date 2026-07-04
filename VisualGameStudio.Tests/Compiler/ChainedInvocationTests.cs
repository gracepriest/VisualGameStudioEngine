using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Invoking the delegate returned by a call — f(a)(b) — must generate valid C#.
/// </summary>
[TestFixture]
public class ChainedInvocationTests
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
    public void DelegateReturnedFromCall_StoredThenInvoked()
    {
        var source = @"
Function MakeAdder(x As Integer) As Func(Of Integer, Integer)
    Return Function(y As Integer) x + y
End Function

Sub Main()
    Dim add2 = MakeAdder(2)
    Dim result As Integer = add2(5)
End Sub";

        var output = CompileToCSharp(source, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Contain("add2(5)"));
    }

    [Test]
    public void DelegateReturnedFromCall_InvokedInline()
    {
        // f(a)(b): call MakeAdder(3), then invoke the returned delegate with (4).
        var source = @"
Function MakeAdder(x As Integer) As Func(Of Integer, Integer)
    Return Function(y As Integer) x + y
End Function

Sub Main()
    Dim result As Integer = MakeAdder(3)(4)
End Sub";

        var output = CompileToCSharp(source, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
        Assert.That(output, Does.Contain("MakeAdder(3)(4)"));
    }
}
