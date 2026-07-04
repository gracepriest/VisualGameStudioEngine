using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Cast expression tests: CType(expr, Type), DirectCast(expr, Type) and
/// TryCast(expr, Type). CType/DirectCast emit a C# cast (or Convert.* call for
/// String-to-numeric and numeric-to-String conversions); TryCast emits C# 'as'.
/// </summary>
[TestFixture]
public class CastExpressionTests
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
    // CType - numeric conversions
    // ========================================================================

    [Test]
    public void Compile_CType_DoubleToInteger_EmitsCast()
    {
        var source = @"
Sub Main()
    Dim d As Double = 3.7
    Dim i As Integer = CType(d, Integer)
    Print(i)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("(int)"));
    }

    [Test]
    public void Compile_CType_ResultTypeIsTargetType()
    {
        // The cast result must type-check as the target type - assigning
        // CType(d, Integer) to an Integer variable must not report an error
        var source = @"
Sub Main()
    Dim d As Double = 3.7
    Dim i As Integer = CType(d, Integer)
    Dim j As Integer = i + 1
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
    }

    [Test]
    public void Compile_CType_IntegerToDouble_Compiles()
    {
        var source = @"
Sub Main()
    Dim i As Integer = 4
    Dim d As Double = CType(i, Double)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("(double)"));
    }

    // ========================================================================
    // CType - String conversions use Convert.*
    // ========================================================================

    [Test]
    public void Compile_CType_StringToInteger_EmitsConvert()
    {
        var source = @"
Sub Main()
    Dim s As String = ""123""
    Dim i As Integer = CType(s, Integer)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Convert.ToInt32"));
    }

    [Test]
    public void Compile_CType_IntegerToString_EmitsConvert()
    {
        var source = @"
Sub Main()
    Dim i As Integer = 42
    Dim s As String = CType(i, String)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("Convert.ToString"));
    }

    // ========================================================================
    // CType - reference conversions and member access on cast result
    // ========================================================================

    [Test]
    public void Compile_CType_ObjectToString_EmitsReferenceCast()
    {
        var source = @"
Sub Main()
    Dim o As Object = ""hello""
    Dim s As String = CType(o, String)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("(string)"));
    }

    [Test]
    public void Compile_CType_MemberAccessOnCastResult_TypesCorrectly()
    {
        // CType(o, String).ToUpper() - member access must see the target type
        var source = @"
Sub Main()
    Dim o As Object = ""hello""
    Dim u As String = CType(o, String).ToUpper()
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("ToUpper"));
    }

    // ========================================================================
    // DirectCast
    // ========================================================================

    [Test]
    public void Compile_DirectCast_ObjectToString_EmitsCast()
    {
        var source = @"
Sub Main()
    Dim o As Object = ""hello""
    Dim s As String = DirectCast(o, String)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("(string)"));
    }

    // ========================================================================
    // TryCast - C# 'as' operator
    // ========================================================================

    [Test]
    public void Compile_TryCast_ObjectToString_EmitsAsOperator()
    {
        var source = @"
Sub Main()
    Dim o As Object = ""hello""
    Dim s As String = TryCast(o, String)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain(" as string"));
    }

    [Test]
    public void Compile_TryCast_ToValueType_ReportsError()
    {
        // TryCast requires a reference target type (C# 'as' cannot produce int)
        var source = @"
Sub Main()
    Dim o As Object = 42
    Dim i As Integer = TryCast(o, Integer)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "TryCast to a value type should fail");
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("TryCast"));
    }

    // ========================================================================
    // Error cases
    // ========================================================================

    [Test]
    public void Compile_CType_UnresolvableTargetType_ReportsError()
    {
        // PascalCase names are permissively deferred to the C# compiler (same as
        // 'Dim x As SomeType'), but names that cannot be a type at all must error
        var source = @"
Sub Main()
    Dim o As Object = ""x""
    Dim y As Object = CType(o, nosuchtype)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null, "CType to unresolvable type should fail");
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("Unknown type"));
    }

    [Test]
    public void Compile_CType_PascalCaseNetType_DeferredToCSharp()
    {
        // Unknown PascalCase types are treated as potential .NET types and the
        // cast is emitted for the C# compiler to validate (existing Dim behavior)
        var source = @"
Sub Main()
    Dim o As Object = ""x""
    Dim s As Object = CType(o, StringBuilder)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("(StringBuilder)"));
    }

    [Test]
    public void Compile_CType_InsideExpression_Compiles()
    {
        // CType used as a sub-expression of arithmetic
        var source = @"
Sub Main()
    Dim d As Double = 3.7
    Dim i As Integer = CType(d, Integer) + 1
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("(int)"));
    }
}
