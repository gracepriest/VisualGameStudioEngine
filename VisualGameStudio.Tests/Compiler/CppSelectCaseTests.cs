using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend <c>Select Case</c> lowering.
///
/// The parser routes EVERY case value into <c>CaseClauseNode.Patterns</c> (never
/// <c>.Values</c>), so <c>IRBuilder</c> fills <c>IRSwitch.PatternCases</c> and leaves
/// <c>IRSwitch.Cases</c> empty. C++'s native <c>switch</c> only accepts integral
/// compile-time-constant labels — no ranges, relational comparisons, When guards, or
/// even string constants — so the C++ backend lowers the whole switch to an
/// <c>if/else-if</c> chain of <c>goto</c>s (one boolean test per pattern case, falling
/// through to the default target). This matches the C#/MSIL/LLVM SEMANTICS (first match
/// wins, in source order).
///
/// The genuinely-hard pattern kinds (type/RTTI, tuple deconstruction, variable binding)
/// are rejected with a clean <see cref="CppCapabilityException"/> rather than silently
/// dropped — mirroring the honesty matrix the other backends already enforce.
///
/// These MUST run through <see cref="CompileToCppOptimized"/> (the CLI-faithful path): the
/// case-body blocks are only reachable via the switch's pattern-case edges, so if the CFG
/// does not wire them the DeadCodeEliminationPass deletes the bodies and the emitted
/// <c>goto</c>s dangle. The non-optimized path never exercised that.
/// </summary>
[TestFixture]
public class CppSelectCaseTests
{
    /// <summary>Compile BasicLang source to C++ AFTER the standard IR optimizer passes (CLI path).</summary>
    private string? CompileToCppOptimized(string source, out List<string> errors)
    {
        errors = new List<string>();

        var tokens = new Lexer(source).Tokenize();
        var parser = new Parser(tokens);
        ProgramNode ast;
        try { ast = parser.Parse(); }
        catch (Exception ex) { errors.Add($"Parse error: {ex.Message}"); return null; }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");

        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    /// <summary>Compile the generated C++ with a real compiler, run it, return normalized stdout.
    /// Ignores when no C++ compiler is available on the machine.</summary>
    private static string CompileRun(string cppSource)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cppSource, compiler.Value)
            .Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------
    // Constant cases (the core regression: `Case 42` was dropped entirely).
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_SelectCase_ConstantMatch_RunsMatchingBranch_CompileAndRun()
    {
        // The exact reproduction from the bug report: a plain integer Case 42 must match.
        var source = @"
Sub Main()
    Dim y As Integer = 42
    Select Case y
        Case 42
            Console.WriteLine(""matched"")
        Case Else
            Console.WriteLine(""no"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("matched\n"));
    }

    [Test]
    public void Cpp_SelectCase_ConstantNoMatch_RunsElse_CompileAndRun()
    {
        var source = @"
Sub Main()
    Dim y As Integer = 7
    Select Case y
        Case 1
            Console.WriteLine(""one"")
        Case 2
            Console.WriteLine(""two"")
        Case Else
            Console.WriteLine(""else"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("else\n"));
    }

    [Test]
    public void Cpp_SelectCase_MultipleConstants_PicksCorrectBranch_CompileAndRun()
    {
        // Middle branch must win (first-match-wins, in source order).
        var source = @"
Sub Main()
    Dim y As Integer = 2
    Select Case y
        Case 1
            Console.WriteLine(""one"")
        Case 2
            Console.WriteLine(""two"")
        Case 3
            Console.WriteLine(""three"")
        Case Else
            Console.WriteLine(""else"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("two\n"));
    }

    [Test]
    public void Cpp_SelectCase_CommaSeparatedConstants_ShareBranch_CompileAndRun()
    {
        // `Case 2, 4, 6` — three constant patterns targeting one block.
        var source = @"
Sub Main()
    Dim y As Integer = 4
    Select Case y
        Case 2, 4, 6
            Console.WriteLine(""even-small"")
        Case Else
            Console.WriteLine(""other"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("even-small\n"));
    }

    [Test]
    public void Cpp_SelectCase_StringConstant_CompileAndRun()
    {
        // A string discriminant — impossible with a native C++ switch, proving the if-chain.
        var source = @"
Sub Main()
    Dim s As String = ""hello""
    Select Case s
        Case ""hi""
            Console.WriteLine(""informal"")
        Case ""hello""
            Console.WriteLine(""formal"")
        Case Else
            Console.WriteLine(""unknown"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("formal\n"));
    }

    // ------------------------------------------------------------------
    // Range, comparison, When-guard, Or (the pattern kinds C++ switch cannot express).
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_SelectCase_RangePattern_CompileAndRun()
    {
        // `Case 1 To 10` -> (y >= 1 && y <= 10).
        var source = @"
Sub Main()
    Dim y As Integer = 5
    Select Case y
        Case 1 To 10
            Console.WriteLine(""low"")
        Case 11 To 20
            Console.WriteLine(""high"")
        Case Else
            Console.WriteLine(""out"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("low\n"));
    }

    [Test]
    public void Cpp_SelectCase_ComparisonPattern_CompileAndRun()
    {
        // `Case Is > 10` -> (y > 10).
        var source = @"
Sub Main()
    Dim y As Integer = 42
    Select Case y
        Case Is > 100
            Console.WriteLine(""huge"")
        Case Is > 10
            Console.WriteLine(""big"")
        Case Else
            Console.WriteLine(""small"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("big\n"));
    }

    [Test]
    public void Cpp_SelectCase_WhenGuard_CompileAndRun()
    {
        // `Case Is > 5 When y < 100` -> ((y > 5) && (y < 100)). Compound guard exercises
        // the inline guard renderer (the guard is built suppressed, never emitted as
        // instructions, so it must be rendered as an inline expression tree).
        var source = @"
Sub Main()
    Dim y As Integer = 42
    Select Case y
        Case Is > 5 When y < 100
            Console.WriteLine(""mid"")
        Case Else
            Console.WriteLine(""other"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("mid\n"));
    }

    [Test]
    public void Cpp_SelectCase_WhenGuardFails_FallsThrough_CompileAndRun()
    {
        // Same case shape, but the guard is false -> the pattern must NOT match; falls to Else.
        var source = @"
Sub Main()
    Dim y As Integer = 42
    Select Case y
        Case Is > 5 When y > 1000
            Console.WriteLine(""mid"")
        Case Else
            Console.WriteLine(""other"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("other\n"));
    }

    [Test]
    public void Cpp_SelectCase_OrPattern_CompileAndRun()
    {
        // `Case 1 Or 2 Or 3` -> a single Or pattern (y == 1 || y == 2 || y == 3).
        var source = @"
Sub Main()
    Dim y As Integer = 3
    Select Case y
        Case 1 Or 2 Or 3
            Console.WriteLine(""small"")
        Case Else
            Console.WriteLine(""big"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(CompileRun(output), Is.EqualTo("small\n"));
    }

    // ------------------------------------------------------------------
    // Codegen shape: the discriminant must NOT be emitted as a native C++ switch
    // (which would silently drop the pattern cases).
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_SelectCase_DoesNotEmitNativeSwitch()
    {
        var source = @"
Sub Main()
    Dim y As Integer = 42
    Select Case y
        Case 42
            Console.WriteLine(""matched"")
        Case Else
            Console.WriteLine(""no"")
    End Select
End Sub";
        var output = CompileToCppOptimized(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("switch ("),
            "the pattern-based Select Case must lower to an if/else-if goto chain, not a native switch");
        // The constant test must be present as a real comparison.
        Assert.That(output, Does.Contain("== 42"));
    }

    // ------------------------------------------------------------------
    // Honesty: pattern kinds with no clean C++ lowering are REJECTED, not dropped.
    // ------------------------------------------------------------------

    [Test]
    public void Cpp_SelectCase_BindingPattern_RejectedWithCleanCapabilityError()
    {
        // `Case n When n > 0` binds the discriminant to `n` and guards on it. Binding
        // patterns need discriminant-aliasing plus a body-scope declaration (the body may
        // reference `n`), which has no clean C++ lowering yet — so it must be a clean
        // capability diagnostic, not silently-dropped or broken C++. (Non-binding When
        // guards, e.g. `Case Is > 5 When y < 100`, ARE supported — see the run-tests above.)
        var source = @"
Sub Main()
    Dim y As Integer = 5
    Select Case y
        Case n When n > 0
            Console.WriteLine(""pos"")
        Case Else
            Console.WriteLine(""nonpos"")
    End Select
End Sub";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCppOptimized(source, out _));
        Assert.That(string.Join("\n", ex!.Diagnostics),
            Does.Contain("Select Case").And.Contains("pattern"),
            "a binding pattern in Select Case must be an actionable capability diagnostic, not broken C++");
    }
}
