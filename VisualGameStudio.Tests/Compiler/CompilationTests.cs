using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// End-to-end compilation tests: BasicLang source -> Lexer -> Parser -> SemanticAnalyzer -> IRBuilder -> CSharpBackend -> C# output.
/// </summary>
[TestFixture]
public class CompilationTests
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
    // Variable declarations and types
    // ========================================================================

    [Test]
    public void Compile_VariableDeclaration_Integer()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 42
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int"));
        Assert.That(output, Does.Contain("42"));
    }

    [Test]
    public void Compile_VariableDeclaration_String()
    {
        var source = @"
Sub Main()
    Dim name As String = ""Hello""
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("string"));
        Assert.That(output, Does.Contain("Hello"));
    }

    [Test]
    public void Compile_FunctionParameters_ByteAndShort()
    {
        var source = @"
Sub ProcessByte(b As Byte)
    Dim x As Integer = 0
End Sub

Sub ProcessShort(s As Short)
    Dim y As Integer = 0
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("byte"));
        Assert.That(output, Does.Contain("short"));
    }

    [Test]
    public void Compile_FunctionParameters_UnsignedTypes()
    {
        var source = @"
Sub ProcessUInt(u As UInteger)
    Dim x As Integer = 0
End Sub

Sub ProcessULong(ul As ULong)
    Dim y As Integer = 0
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("uint"));
        Assert.That(output, Does.Contain("ulong"));
    }

    // ========================================================================
    // For / ForEach loops
    // ========================================================================

    [Test]
    public void Compile_ForLoop_BasicCounting()
    {
        var source = @"
Sub Main()
    Dim total As Integer = 0
    For i As Integer = 1 To 10
        total = total + i
    Next
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        // For loops compile to IR with increments and comparisons - verify the loop variable and bounds exist
        Assert.That(output, Does.Contain("total"));
        Assert.That(output, Does.Contain("10"));
    }

    [Test]
    public void Compile_ForEachLoop()
    {
        var source = @"
Sub Main()
    Dim arr() As Integer = {1, 2, 3}
    Dim total As Integer = 0
    For Each n In arr
        total = total + n
    Next
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("foreach"));
    }

    // ========================================================================
    // Functions with return types
    // ========================================================================

    [Test]
    public void Compile_FunctionWithReturnType()
    {
        var source = @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int"));
        Assert.That(output, Does.Contain("return"));
    }

    // ========================================================================
    // Classes with properties
    // ========================================================================

    [Test]
    public void Compile_ClassWithProperties()
    {
        var source = @"
Class Person
    Public Name As String
    Public Age As Integer

    Sub New(n As String, a As Integer)
        Name = n
        Age = a
    End Sub
End Class";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("class Person"));
        Assert.That(output, Does.Contain("Name"));
        Assert.That(output, Does.Contain("Age"));
    }

    // ========================================================================
    // Pattern matching (Select Case)
    // ========================================================================

    [Test]
    public void Compile_SelectCase_PatternMatching()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 5
    Select Case x
        Case 1
            Dim a As Integer = 1
        Case 2
            Dim b As Integer = 2
        Case Else
            Dim c As Integer = 0
    End Select
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("switch").Or.Contains("if"));
    }

    // ========================================================================
    // Hex/Octal/Binary literals
    // ========================================================================

    [Test]
    public void Compile_HexLiteral()
    {
        var source = @"
Sub Main()
    Dim x As Integer = &HFF
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("255"));
    }

    [Test]
    public void Compile_BinaryLiteral()
    {
        var source = @"
Sub Main()
    Dim x As Integer = &B1010
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("10"));
    }

    [Test]
    public void Compile_OctalLiteral()
    {
        var source = @"
Sub Main()
    Dim x As Integer = &O17
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("15"));
    }

    // ========================================================================
    // Protected modifier
    // ========================================================================

    [Test]
    public void Compile_ProtectedModifier()
    {
        var source = @"
Class Animal
    Protected Name As String

    Sub New(n As String)
        Name = n
    End Sub
End Class";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("protected"));
        Assert.That(output, Does.Contain("Name"));
    }

    // ========================================================================
    // If/ElseIf/Else control flow
    // ========================================================================

    [Test]
    public void Compile_IfElseIfElse()
    {
        var source = @"
Sub Main()
    Dim x As Integer = 10
    If x > 5 Then
        Dim a As Integer = 1
    ElseIf x > 0 Then
        Dim b As Integer = 2
    Else
        Dim c As Integer = 3
    End If
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("if"));
        Assert.That(output, Does.Contain("else"));
    }

    // ========================================================================
    // Line continuation
    // ========================================================================

    [Test]
    public void Compile_LineContinuation()
    {
        var source = "Sub Main()\n    Dim x As Integer = 1 + _\n        2\nEnd Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
    }
}
