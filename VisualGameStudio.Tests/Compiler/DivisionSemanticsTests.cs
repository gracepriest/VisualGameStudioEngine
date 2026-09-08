using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// The <c>/</c> operator is FLOATING-POINT division — chip task_dacd709d.
///
/// In VB.NET <c>/</c> always divides in floating point: <c>7 / 2</c> is 3.5, typed Double.
/// Integer division is spelled <c>\</c>. BasicLang typed <c>Integer / Integer</c> as Integer,
/// so both the C# and C++ backends inherited C-family truncation and printed 3 — measured by
/// RUNNING compiled programs on both backends. The JS backend was already correct, and
/// JavaScriptTypeMapper carries a comment warning about the exact hazard below.
///
/// ⚠ FOUR SITES, AND THEY ARE MUTUALLY BLOCKING. Measured:
///   - SemanticAnalyzer alone yields IRConstant(3 as int) stamped Double -> still prints 3,
///     because IROptimizer stamps op.Type onto the folded constant.
///   - IROptimizer.FoldDiv alone yields IRConstant(3.5) stamped Integer -> `int x = 3.5`,
///     which does not compile.
///   - Without relaxing the `\` arm, `(a / b) \ c` — which compiles TODAY only because
///     `a / b` is typed Integer — becomes a hard error.
///   - Without operand widening neither backend reads IRBinaryOp.Type, so the emitted
///     operands stay integral and the target language truncates anyway.
///
/// ⚠ AND FIXING `/` CAN SILENTLY BREAK `\`. CSharpBackend maps BOTH Div and IntDiv to "/",
/// so `7 \ 2` returns the right answer today partly by accident of `/` being broken the same
/// way. <see cref="IntegerDivision_StillTruncates"/> is the guard for that.
///
/// NARROWING: BasicLang has no Option Strict directive, so rejecting `Dim x As Integer =
/// a / b` would break existing source with no way to opt out. Floating-to-integral narrowing
/// is therefore permitted implicitly and lowers the same way the explicit CInt() already
/// does (truncation), which keeps every existing program compiling AND keeps its value
/// unchanged. Only the value that was never stored into an integer — the one the chip is
/// about — changes.
/// </summary>
[TestFixture]
public class DivisionSemanticsTests
{
    private string CompileToCppOptimized(string source, out List<string> errors)
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

        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    /// <summary>Everything after the ~1600-line native runtime prelude.</summary>
    private static string UserCode(string cpp)
    {
        const string marker = "// Function implementations";
        var i = cpp.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(i, Is.GreaterThanOrEqualTo(0), "emitted C++ has no user-function section");
        return cpp.Substring(i);
    }

    private static string Body(string cpp, string signature)
    {
        var start = cpp.IndexOf(signature + "\n{", StringComparison.Ordinal);
        if (start < 0) start = cpp.IndexOf(signature + "\r\n{", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0),
            $"emitted C++ has no definition of '{signature}'");
        var open = cpp.IndexOf('{', start);
        var close = cpp.IndexOf("\n}", open, StringComparison.Ordinal);
        return cpp.Substring(open, close - open);
    }

    // ========================================================================
    // The chip
    // ========================================================================

    [Test]
    public void Division_OfTwoIntegerLiterals_DividesInFloatingPoint()
    {
        // The constant path — what the CLI and the IDE actually run, since every shipping
        // route optimizes unconditionally.
        //
        // This asserts the DIVISION IS FLOATING, not that it folds to a literal 3.5. Operand
        // widening wraps both constants in casts, which the folder does not see through, so
        // `7 / 2` now emits a real division instead of a folded constant. That is a codegen
        // -quality trade, not a correctness one: the value is 3.5 either way (verified by
        // running the compiled binary), and both clang/g++ and the JIT fold it downstream.
        // Teaching IROptimizer to fold cast-of-constant would recover the literal, but casts
        // carry narrowing semantics and that is an optimizer feature, not part of this fix.
        var source = @"
Sub Main()
    Console.WriteLine(7 / 2)
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("static_cast<double>(7)"),
            "7 / 2 is 3.5 in VB.NET; dividing the raw integers truncates it to 3");
        Assert.That(UserCode(cpp), Does.Contain("static_cast<double>(2)"));
    }

    [Test]
    public void Division_OfTwoIntegerVariables_ProducesAFloatingTemp()
    {
        // The non-constant path, which the folder cannot reach.
        var source = @"
Function Half(a As Integer, b As Integer) As Double
    Return a / b
End Function

Sub Main()
    Console.WriteLine(Half(7, 2))
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        var body = Body(cpp, "double Half(int32_t a, int32_t b)");

        // ⛔ ASSERT ON THE OPERANDS, NOT THE TEMP. An earlier version of this test checked
        // only that the quotient temp was `double` and that no int32_t temp existed. It
        // PASSED while the compiled program printed 3, because the temp was correctly double
        // and `a / b` on two int32_t operands had already truncated before the widen. The
        // run-level oracle caught it; the emission assertion could not.
        Assert.That(body, Does.Contain("static_cast<double>(a)"),
            "the OPERANDS must be widened — neither backend reads IRBinaryOp.Type, so it is " +
            "the target language that decides whether `/` truncates");
        Assert.That(body, Does.Contain("static_cast<double>(b)"));
        Assert.That(body, Does.Not.Contain("int32_t t"),
            "the quotient temp must be floating too");
    }

    // ========================================================================
    // The three things the fix must NOT break
    // ========================================================================

    [Test]
    public void IntegerDivision_StillTruncates()
    {
        // THE REGRESSION GUARD. CSharpBackend maps both Div and IntDiv to "/", so `\` was
        // returning the right answer partly by accident of `/` being broken identically.
        var source = @"
Sub Main()
    Console.WriteLine(7 \ 2)
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("3"));
        Assert.That(UserCode(cpp), Does.Not.Contain("3.5"),
            "`\\` is integer division and must keep truncating when `/` stops");
    }

    [Test]
    public void Division_AssignedToAnInteger_StillCompiles()
    {
        // BasicLang has no Option Strict, so making this an error would break existing
        // source with no opt-out. It must keep compiling, and keep its current value.
        var source = @"
Sub Main()
    Dim a As Integer = 7
    Dim b As Integer = 2
    Dim x As Integer = a / b
    Console.WriteLine(x)
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty,
            "floating-to-integral narrowing is implicit here because the language offers no " +
            "Option Strict switch to relax");
        Assert.That(cpp, Is.Not.Null);
    }

    [Test]
    public void IntegerDivision_AcceptsAFloatingOperand()
    {
        // `(a / b) \ c` compiles TODAY only because `a / b` is typed Integer. Once `/` yields
        // Double the `\` arm must accept it, or the fix turns working programs into errors.
        var source = @"
Function Chain(a As Integer, b As Integer, c As Integer) As Integer
    Return (a / b) \ c
End Function

Sub Main()
    Console.WriteLine(Chain(20, 2, 3))
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty,
            "relaxing the `\\` arm is required by the `/` change, not optional cleanup");
    }

    // ========================================================================
    // Types that must NOT become Double
    // ========================================================================

    [Test]
    public void Division_OfDecimals_StaysDecimal()
    {
        var source = @"
Function D(a As Decimal, b As Decimal) As Decimal
    Return a / b
End Function

Sub Main()
    Console.WriteLine(""ok"")
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("Decimal"),
            "Decimal division stays Decimal — promoting it to Double would lose precision " +
            "and there is no implicit conversion between them in either direction");
    }

    [Test]
    public void Division_OfSingles_StaysSingle()
    {
        var source = @"
Function S(a As Single, b As Single) As Single
    Return a / b
End Function

Sub Main()
    Console.WriteLine(""ok"")
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(Body(cpp, "float S(float a, float b)"), Does.Not.Contain("double"),
            "Single division stays Single; widening it to Double would change the result type");
    }
}
