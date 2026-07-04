using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// End-to-end lambda compilation tests: typed/untyped lambdas, Func/Action
/// delegate types, target-typed parameter inference, and delegate invocation.
/// </summary>
[TestFixture]
public class LambdaTests
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
    // Typed lambdas
    // ========================================================================

    [Test]
    public void Compile_TypedLambda_AssignedToFuncVariable()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Func<int, int>"));
        Assert.That(output, Does.Contain("=>"));
        Assert.That(output, Does.Contain("x * 2"));
    }

    [Test]
    public void Compile_TypedLambda_MultipleParameters()
    {
        var source = @"
Sub Main()
    Dim add As Func(Of Integer, Integer, Integer) = Function(a As Integer, b As Integer) a + b
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Func<int, int, int>"));
        Assert.That(output, Does.Contain("(int a, int b) =>"));
        Assert.That(output, Does.Contain("a + b"));
    }

    // ========================================================================
    // Untyped lambdas (target-typed parameter inference)
    // ========================================================================

    [Test]
    public void Compile_UntypedLambda_ParametersInferredFromTargetType()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x) x * 2
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Func<int, int>"));
        // Parameter should be inferred as int, not object
        Assert.That(output, Does.Contain("(int x) =>"));
        Assert.That(output, Does.Not.Contain("(object x)"));
    }

    [Test]
    public void Compile_UntypedLambda_MultiParam_InferredFromTargetType()
    {
        var source = @"
Sub Main()
    Dim add As Func(Of Integer, Integer, Integer) = Function(a, b) a + b
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Func<int, int, int>"));
        Assert.That(output, Does.Contain("(int a, int b) =>"));
    }

    [Test]
    public void Compile_UntypedLambda_NoTargetType_ReportsClearError()
    {
        var source = @"
Sub Main()
    Dim f As Object = Function(x) x * 2
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail without a typed delegate target");
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("Cannot infer type for lambda parameter 'x'"));
    }

    // ========================================================================
    // Func(Of ...) type resolution
    // ========================================================================

    [Test]
    public void Compile_FuncOfType_ResolvesGenericArguments()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x + 1
    Dim y As Integer = f(5)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        // Generic arguments must not be dropped (bare "Func" would be invalid C#)
        Assert.That(output, Does.Contain("Func<int, int>"));
        Assert.That(output, Does.Contain("f(5)"));
    }

    [Test]
    public void Compile_FuncOfType_ReturnedFromFunction()
    {
        var source = @"
Function MakeAdder() As Func(Of Integer, Integer)
    Return Function(x) x + 1
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Func<int, int> MakeAdder"));
        Assert.That(output, Does.Contain("(int x) => x + 1"));
    }

    // ========================================================================
    // Action (Sub) lambdas
    // ========================================================================

    [Test]
    public void Compile_SubLambda_AssignedToActionVariable()
    {
        var source = @"
Sub Main()
    Dim greet As Action(Of String) = Sub(name As String) Print(name)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Action<string>"));
        Assert.That(output, Does.Contain("(string name) =>"));
        // The lambda body must not "return" the result of a void call
        Assert.That(output, Does.Not.Contain("return Console.Write"));
    }

    // ========================================================================
    // Delegate invocation argument validation
    // ========================================================================

    [Test]
    public void Compile_DelegateInvocation_WrongArgumentType_ReportsError()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    Dim r As Integer = f(""wrong"")
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail for wrong argument type");
        Assert.That(errors, Is.Not.Empty);
        var allErrors = string.Join("; ", errors);
        Assert.That(allErrors, Does.Contain("cannot convert from 'String' to 'Integer'"));
        Assert.That(allErrors, Does.Contain("Func(Of Integer, Integer)"));
    }

    [Test]
    public void Compile_DelegateInvocation_TooManyArguments_ReportsError()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    Dim r As Integer = f(1, 2)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail for too many arguments");
        Assert.That(errors, Is.Not.Empty);
        var allErrors = string.Join("; ", errors);
        Assert.That(allErrors, Does.Contain("expects 1 argument(s), got 2"));
        Assert.That(allErrors, Does.Contain("Func(Of Integer, Integer)"));
    }

    [Test]
    public void Compile_DelegateInvocation_TooFewArguments_ReportsError()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    Dim r As Integer = f()
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail for too few arguments");
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("expects 1 argument(s), got 0"));
    }

    [Test]
    public void Compile_DelegateInvocation_ValidArguments_Compiles()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
    Dim r As Integer = f(5)
    Dim g As Func(Of Double, Double) = Function(x As Double) x * 2.0
    Dim d As Double = g(3)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("f(5)"));
        // Implicit Integer -> Double widening must still be allowed
        Assert.That(output, Does.Contain("g(3)"));
    }

    [Test]
    public void Compile_ActionInvocation_WrongArgumentType_ReportsError()
    {
        var source = @"
Sub Main()
    Dim greet As Action(Of String) = Sub(name As String) Print(name)
    greet(123)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "Compilation should fail for wrong Action argument type");
        Assert.That(errors, Is.Not.Empty);
        var allErrors = string.Join("; ", errors);
        Assert.That(allErrors, Does.Contain("cannot convert from 'Integer' to 'String'"));
        Assert.That(allErrors, Does.Contain("Action(Of String)"));
    }

    [Test]
    public void Compile_ActionInvocation_ValidArgument_Compiles()
    {
        var source = @"
Sub Main()
    Dim greet As Action(Of String) = Sub(name As String) Print(name)
    greet(""hello"")
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("greet(\"hello\")"));
    }
}
