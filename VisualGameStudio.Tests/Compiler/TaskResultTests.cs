using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Accessing .Result on a Task(Of T) must yield T, not Object, so member
/// chaining off the result resolves (e.g. Task(Of String).Result.ToUpper()).
/// </summary>
[TestFixture]
public class TaskResultTests
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
    public void TaskOfString_Result_IsString_SoMemberChainResolves()
    {
        // t.Result is String, so .ToUpper() must resolve. If Result typed as
        // Object this fails semantic analysis (Object has no ToUpper).
        var src = @"Module M
    Async Function GetName() As Task(Of String)
        Return ""hi""
    End Function

    Sub Run()
        Dim t As Task(Of String) = GetName()
        Console.WriteLine(t.Result.ToUpper())
    End Sub
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
    }

    [Test]
    public void TaskOfString_Result_AssignsToStringVariable()
    {
        var src = @"Module M
    Async Function GetName() As Task(Of String)
        Return ""hi""
    End Function

    Sub Run()
        Dim t As Task(Of String) = GetName()
        Dim s As String = t.Result
        Console.WriteLine(s)
    End Sub
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
    }

    [Test]
    public void TaskOfInteger_Result_IsInteger()
    {
        var src = @"Module M
    Async Function GetCount() As Task(Of Integer)
        Return 5
    End Function

    Sub Run()
        Dim t As Task(Of Integer) = GetCount()
        Dim n As Integer = t.Result
        Console.WriteLine(n)
    End Sub
End Module";

        var output = CompileToCSharp(src, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
    }
}
