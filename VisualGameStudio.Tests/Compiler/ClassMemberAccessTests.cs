using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Accessing a field/property of a user-defined class must yield the field's
/// declared type, not Object (else "cannot convert Object to String" etc.).
/// </summary>
[TestFixture]
public class ClassMemberAccessTests
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
    public void StringField_AccessedThroughInstance_TypesAsString()
    {
        var source = @"Module M
    Public Class Player
        Public Name As String

        Public Sub New(n As String)
            Me.Name = n
        End Sub
    End Class

    Sub Main()
        Dim p As New Player(""Hero"")
        Dim s As String = p.Name
    End Sub
End Module";

        var output = CompileToCSharp(source, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
    }

    [Test]
    public void StringField_PassedToStringParameter_Compiles()
    {
        var source = @"Module M
    Public Class Player
        Public Name As String

        Public Sub New(n As String)
            Me.Name = n
        End Sub
    End Class

    Sub Greet(who As String)
    End Sub

    Sub Main()
        Dim p As New Player(""Hero"")
        Greet(p.Name)
    End Sub
End Module";

        var output = CompileToCSharp(source, out var errors);
        Assert.That(output, Is.Not.Null, "compile failed: " + string.Join("; ", errors));
    }
}
