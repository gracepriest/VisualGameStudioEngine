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

    // ========================================================================
    // Task 3: reference semantics - classes are std::shared_ptr, structures stay values
    // ========================================================================

    [Test]
    public void Cpp_ClassInstance_UsesSharedPtr()
    {
        var source = @"
Class Person
    Public Name As String
End Class

Sub Main()
    Dim p As New Person()
    p.Name = ""Alice""
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("std::shared_ptr<Person>"));
        Assert.That(output, Does.Contain("std::make_shared<Person>("));
        Assert.That(output, Does.Contain("->Name"));
    }

    [Test]
    public void Cpp_ClassMethodCall_UsesArrow()
    {
        var source = @"
Class Counter
    Private _n As Integer
    Public Sub Increment()
        _n = _n + 1
    End Sub
End Class

Sub Main()
    Dim c As New Counter()
    c.Increment()
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("c->Increment()"));
    }

    [Test]
    public void Cpp_Structure_ThrowsCapabilityError_NoCodegenExistsYet()
    {
        // Structures generate no IR on ANY backend today (IRBuilder.Visit(StructureNode) is
        // empty), so structure-typed values hit the permanent unmapped-type diagnostic.
        // When structure codegen lands, this test should flip to asserting value semantics
        // (no shared_ptr wrapper, '.' member access).
        var source = @"
Structure Point
    Public X As Integer
    Public Y As Integer
End Structure

Sub Main()
    Dim p As Point
    p.X = 1
End Sub";

        var ex = Assert.Throws<CppCapabilityException>(() =>
        {
            var output = CompileToCpp(source, out var errors);
            Assert.That(errors, Is.Empty, "expected capability exception, got pipeline errors: " + string.Join("; ", errors));
        });
        Assert.That(ex.Message, Does.Contain("Point"));
    }

    // ========================================================================
    // Task 4: throw + finally + exception type mapping
    // ========================================================================

    [Test]
    public void Cpp_ThrowStatement_EmitsCppThrow()
    {
        var source = @"
Sub Fail()
    Throw New Exception(""boom"")
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("throw std::runtime_error("));
        Assert.That(output, Does.Contain("boom"));
    }

    [Test]
    public void Cpp_TryCatchTyped_MapsExceptionType()
    {
        var source = @"
Sub Main()
    Try
        Dim x As Integer = 1
    Catch ex As Exception
        Dim y As Integer = 2
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("catch (const std::exception& ex)"));
    }

    [Test]
    public void Cpp_TryFinally_EmitsFinallyOnBothPaths()
    {
        var source = @"
Sub Main()
    Dim n As Integer = 0
    Try
        n = 1
    Finally
        n = 2
    End Try
End Sub";

        var output = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        // exceptional path: catch(...) { finally; throw; }
        Assert.That(output, Does.Contain("catch (...)"));
        Assert.That(output, Does.Contain("throw;"));
        // finally body appears on both the exceptional and the normal path
        Assert.That(CountOccurrences(output, "n = 2;"), Is.EqualTo(2),
            "finally body should be emitted twice (exceptional + normal path):\n" + output);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
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
