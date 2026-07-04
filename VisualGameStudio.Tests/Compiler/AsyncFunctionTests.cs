using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Async Function/Sub compilation tests (C# backend).
/// Covers VB.NET-style semantics: Async Function As Task(Of T) is the canonical form
/// (Return expr type-checks against the unwrapped T), and a bare As T is auto-wrapped
/// so the generated C# declares async Task&lt;T&gt;.
/// </summary>
[TestFixture]
public class AsyncFunctionTests
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
    // Async Function with bare return type (As Integer -> async Task<int>)
    // ========================================================================

    [Test]
    public void Compile_AsyncFunction_BareReturnType_WrapsInTask()
    {
        var source = @"
Async Function GetNumber() As Integer
    Await Task.Delay(10)
    Return 42
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("async Task<int> GetNumber"));
        Assert.That(output, Does.Contain("return 42;"));
    }

    [Test]
    public void Compile_AsyncFunction_StatementLevelAwait_IsEmitted()
    {
        var source = @"
Async Function GetNumber() As Integer
    Await Task.Delay(10)
    Return 42
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("await Task.Delay(10);"));
    }

    // ========================================================================
    // Async Function with explicit Task(Of T) return type (canonical VB form)
    // ========================================================================

    [Test]
    public void Compile_AsyncFunction_TaskOfInteger_ReturnTypeChecksAgainstT()
    {
        var source = @"
Async Function GetNumber() As Task(Of Integer)
    Await Task.Delay(10)
    Return 42
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("async Task<int> GetNumber"));
        // Must not double-wrap into Task<Task<int>>
        Assert.That(output, Does.Not.Contain("Task<Task"));
        Assert.That(output, Does.Contain("return 42;"));
    }

    [Test]
    public void Compile_AsyncFunction_TaskOfInteger_ReturnWrongType_Errors()
    {
        var source = @"
Async Function GetNumber() As Task(Of Integer)
    Return ""hello""
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(output, Is.Null);
        Assert.That(errors, Is.Not.Empty);
        Assert.That(string.Join("; ", errors), Does.Contain("Cannot return type"));
    }

    // ========================================================================
    // Async Sub (-> async Task)
    // ========================================================================

    [Test]
    public void Compile_AsyncSub_EmitsAsyncTask()
    {
        var source = @"
Async Sub DoWork()
    Await Task.Delay(10)
End Sub";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("async Task DoWork"));
        Assert.That(output, Does.Contain("await Task.Delay(10);"));
    }

    // ========================================================================
    // Await of an async function result at call sites
    // ========================================================================

    [Test]
    public void Compile_AwaitAsyncFunctionResult_BareReturnType_TypeChecksAsT()
    {
        var source = @"
Async Function GetNumber() As Integer
    Await Task.Delay(10)
    Return 42
End Function

Async Function Caller() As Integer
    Dim result As Integer = Await GetNumber()
    Return result
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("async Task<int> Caller"));
        Assert.That(output, Does.Contain("result = await GetNumber()"));
    }

    [Test]
    public void Compile_AwaitAsyncFunctionResult_TaskOfT_UnwrapsToT()
    {
        var source = @"
Async Function GetNumber() As Task(Of Integer)
    Await Task.Delay(10)
    Return 42
End Function

Async Function Caller() As Task(Of Integer)
    Dim r As Integer = Await GetNumber()
    Return r + 1
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("r = await GetNumber()"));
        Assert.That(output, Does.Contain("return r + 1;"));
    }

    [Test]
    public void Compile_AwaitResultUsedLater_AwaitNotDuplicated()
    {
        var source = @"
Async Function GetNumber() As Integer
    Await Task.Delay(10)
    Return 42
End Function

Async Function Caller() As Integer
    Dim r As Integer = Await GetNumber()
    Return r + 1
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);

        // The awaited call must appear exactly once - copy propagation must not
        // duplicate a side-effecting await into later uses of the variable
        var occurrences = output.Split("await GetNumber()").Length - 1;
        Assert.That(occurrences, Is.EqualTo(1));
        Assert.That(output, Does.Contain("return r + 1;"));
    }

    // ========================================================================
    // Async methods inside classes
    // ========================================================================

    [Test]
    public void Compile_AsyncClassMethod_EmitsAsyncTask()
    {
        var source = @"
Class Worker
    Public Async Function Compute() As Integer
        Await Task.Delay(5)
        Return 7
    End Function
End Class";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("public async Task<int> Compute"));
        Assert.That(output, Does.Contain("await Task.Delay(5);"));
    }

    // ========================================================================
    // Non-async functions are unaffected
    // ========================================================================

    [Test]
    public void Compile_NonAsyncFunction_NotWrappedInTask()
    {
        var source = @"
Function GetNumber() As Integer
    Return 42
End Function";

        var output = CompileToCSharp(source, out var errors);

        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Is.Not.Null);
        Assert.That(output, Does.Contain("int GetNumber"));
        Assert.That(output, Does.Not.Contain("async"));
        Assert.That(output, Does.Not.Contain("Task<int>"));
    }
}
