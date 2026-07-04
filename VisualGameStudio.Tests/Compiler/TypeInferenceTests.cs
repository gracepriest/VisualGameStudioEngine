using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Local variable type inference tests: `Dim x = expr` (no As clause) infers the
/// variable's type from the initializer expression, matching the existing `Auto`
/// keyword behavior. `As Type` remains required when there is no initializer.
/// </summary>
[TestFixture]
public class TypeInferenceTests
{
    /// <summary>
    /// Helper: compile BasicLang source to C# output string.
    /// Returns null and populates errors list if compilation fails at any stage.
    /// </summary>
    private string CompileToCSharp(string source, out List<string> errors)
    {
        errors = new List<string>();

        // Lex
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        // Parse
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

        // Parser is error-tolerant inside blocks - surface recorded errors too
        if (parser.Errors.Count > 0)
        {
            foreach (var err in parser.Errors)
                errors.Add($"Parse error: {err.Message}");
            return null;
        }

        // Semantic analysis
        var analyzer = new SemanticAnalyzer();
        bool success = analyzer.Analyze(ast);
        if (!success)
        {
            foreach (var err in analyzer.Errors)
                errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        // IR generation
        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        // C# code generation
        var options = new CodeGenOptions
        {
            Namespace = "TestOutput",
            GenerateMainMethod = false,
            GenerateComments = false
        };
        var csharpGen = new ImprovedCSharpCodeGenerator(options);
        var output = csharpGen.Generate(irModule);

        return output;
    }

    // ========================================================================
    // Dim with initializer, no As clause - type inferred
    // ========================================================================

    [Test]
    public void Compile_DimEqualsIntegerLiteral_InfersInt()
    {
        var source = @"
Sub Main()
    Dim x = 42
    Print(x + 1)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int x"));
        Assert.That(output, Does.Not.Contain("object x"));
    }

    [Test]
    public void Compile_DimEqualsStringLiteral_InfersString()
    {
        var source = @"
Sub Main()
    Dim s = ""hello""
    Print(s)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("string s"));
    }

    [Test]
    public void Compile_DimEqualsDoubleLiteral_InfersDouble()
    {
        var source = @"
Sub Main()
    Dim d = 3.14
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("double d"));
    }

    [Test]
    public void Compile_DimEqualsFunctionCall_InfersReturnType()
    {
        var source = @"
Function GetNumber() As Integer
    Return 7
End Function

Sub Main()
    Dim n = GetNumber()
    Print(n)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int n"));
    }

    [Test]
    public void Compile_DimEqualsExpression_InfersFromOperands()
    {
        var source = @"
Sub Main()
    Dim a = 2 + 3
    Dim b = ""x"" & ""y""
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int a"));
        Assert.That(output, Does.Contain("string b"));
    }

    [Test]
    public void Compile_DimInferred_UsableAsInferredType()
    {
        // The inferred variable must participate in typed contexts (assignment
        // to a typed variable) without conversion errors
        var source = @"
Sub Main()
    Dim x = 10
    Dim y As Integer = x
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
    }

    // ========================================================================
    // Explicit As Type with initializer still works
    // ========================================================================

    [Test]
    public void Compile_DimWithAsAndInitializer_Unchanged()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 42
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int x"));
    }

    // ========================================================================
    // Error cases
    // ========================================================================

    [Test]
    public void Compile_DimWithoutAsOrInitializer_ReportsClearError()
    {
        var source = @"
Sub Main()
    Dim x
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail without As or initializer");
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("Expected 'As"));
    }

    [Test]
    public void Compile_DimEqualsNothing_ReportsCannotInferError()
    {
        var source = @"
Sub Main()
    Dim x = Nothing
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail when inferring from Nothing");
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("Cannot infer type for variable 'x'"));
    }
}
