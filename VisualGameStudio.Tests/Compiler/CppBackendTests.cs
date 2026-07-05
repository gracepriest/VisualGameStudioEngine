using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend tests: BasicLang source -> Lexer -> Parser -> SemanticAnalyzer -> IRBuilder -> CppCodeGenerator -> C++ output.
/// Capability diagnostics (CppCapabilityException) are allowed to propagate out of CompileToCpp.
/// </summary>
[TestFixture]
public class CppBackendTests
{
    /// <summary>
    /// Helper: compile BasicLang source to C++ output string.
    /// Returns null and populates errors list if a pipeline stage fails.
    /// CppCapabilityException from the generator propagates to the caller.
    /// </summary>
    private string CompileToCpp(string source, out List<string> errors)
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
        bool success = analyzer.Analyze(ast);
        if (!success)
        {
            foreach (var err in analyzer.Errors)
                errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    // ========================================================================
    // Task 1: capability diagnostics - unsupported features are hard errors
    // ========================================================================

    [Test]
    public void Cpp_AsyncFunction_ThrowsCapabilityError()
    {
        var source = @"
Async Function GetValue() As Integer
    Return 42
End Function";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("Async"));
    }

    [Test]
    public void Cpp_IteratorYield_ThrowsCapabilityError()
    {
        var source = @"
Iterator Function Numbers() As IEnumerable(Of Integer)
    Yield 1
End Function";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("Yield").Or.Contain("Iterator"));
    }

    [Test]
    public void Cpp_TryFinally_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 0
    Try
        x = 1
    Finally
        x = 2
    End Try
End Sub";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("Finally"));
    }

    [Test]
    public void Cpp_Lambda_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Dim f As Func(Of Integer, Integer) = Function(x As Integer) x * 2
End Sub";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("Lambda").IgnoreCase);
    }

    // ========================================================================
    // Task 2: generics -> real C++ templates
    // ========================================================================

    [Test]
    public void Cpp_TemplateFunction_EmitsCppTemplate()
    {
        var source = @"
Template Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    End If
    Return b
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("template <typename T>"));
        Assert.That(output, Does.Match(@"T\s+Max\(const T& a, const T& b\)"));
    }

    [Test]
    public void Cpp_GenericClass_EmitsCppTemplate()
    {
        var source = @"
Class Pair(Of T)
    Public ItemA As T
    Public ItemB As T
End Class";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("template <typename T>"));
        Assert.That(output, Does.Contain("class Pair"));
        Assert.That(output, Does.Contain("T ItemA;"));
        Assert.That(output, Does.Contain("T ItemB;"));
    }

    [Test]
    public void Cpp_GenericInstantiation_EmitsTemplateArguments()
    {
        var source = @"
Class Pair(Of T)
    Public ItemA As T
End Class

Sub Main()
    Dim p As New Pair(Of Integer)()
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("Pair<int32_t>"));
    }

    [Test]
    public void Cpp_UnmappedNetType_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Dim items As List(Of Integer)
End Sub";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void Cpp_PlainProceduralCode_StillCompiles()
    {
        var source = @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int32_t Add(int32_t a, int32_t b)"));
    }

    [Test]
    public void Cpp_ClassWithFieldsAndMethods_StillCompiles()
    {
        var source = @"
Class Counter
    Private _n As Integer
    Public Sub Increment()
        _n = _n + 1
    End Sub
End Class";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("class Counter"));
    }
}
