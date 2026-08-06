using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Integer division (<c>\</c>) on the C++ backend — chip task_7f9cf34d.
///
/// TWO defects, and they MUST be fixed together. Measured, not assumed:
///
///   1. CppCodeGenerator.MapBinaryOperator had no <c>BinaryOpKind.IntDiv</c> arm, so control
///      reached the <c>_ => "?"</c> default and a literal question mark was interpolated into
///      the generated source. The CLI still printed "Compilation successful!" and exited 0.
///
///   2. SemanticAnalyzer hardcoded the <c>\</c> result type to Integer, discarding operand
///      width. That was INVISIBLE while defect 1 existed, because the file could not compile
///      at all. Fixing only defect 1 turns a loud g++ syntax error into a SILENT wrong answer:
///      <c>Long \ Long</c> would emit an int32_t temp under an int64_t return, and
///      <c>9000000000 \ 2</c> prints 205032704 instead of 4500000000 — well-defined modulo
///      2^32, no narrowing diagnostic, no warning flag emitted by the generator.
///
/// This is the repo's durable lesson firing exactly: removing a compile error unblocks what
/// it was masking. The width test below is the one that keeps the pair honest.
///
/// EVERY fixture here uses NON-CONSTANT operands (function parameters). With literals the IR
/// optimizer folds the division away and emits the right answer, hiding both defects — a
/// literal-operand fixture passes against the broken compiler on the optimizing routes.
/// </summary>
[TestFixture]
public class CppIntegerDivisionTests
{
    /// <summary>Compile to C++ without the optimizer (mirrors CppBackendTests.CompileToCpp).</summary>
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
        if (!analyzer.Analyze(ast))
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

    /// <summary>Compile to C++ running the standard optimizer passes, exactly as the CLI does.</summary>
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

    /// <summary>
    /// Everything after the native runtime prelude.
    ///
    /// ⛔ SCOPE YOUR ASSERTIONS WITH THIS, NOT THE WHOLE FILE. The generated .cpp opens with
    /// ~1600 lines of runtime (DateTime, TimeSpan, StringBuilder) that legitimately contain
    /// ternaries and int32_t declarations. A whole-file <c>Does.Not.Contain(" ? ")</c> is red
    /// against a CORRECT compiler, which is how it looks like a working test while proving
    /// nothing about the code under test.
    /// </summary>
    private static string UserCode(string cpp)
    {
        const string marker = "// Function implementations";
        var i = cpp.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(i, Is.GreaterThanOrEqualTo(0), "emitted C++ has no user-function section");
        return cpp.Substring(i);
    }

    /// <summary>
    /// One emitted function's body. Needed for width assertions: a program's Main legitimately
    /// holds int32_t temps, so "no int32_t anywhere in user code" is not the claim being made.
    /// </summary>
    private static string Body(string cpp, string signature)
    {
        var start = cpp.IndexOf(signature + "\n{", StringComparison.Ordinal);
        if (start < 0) start = cpp.IndexOf(signature + "\r\n{", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0),
            $"emitted C++ has no definition of '{signature}'");
        var open = cpp.IndexOf('{', start);
        var close = cpp.IndexOf("\n}", open, StringComparison.Ordinal);
        Assert.That(close, Is.GreaterThan(open), "unterminated function body");
        return cpp.Substring(open, close - open);
    }

    // ========================================================================
    // Defect 1 — the literal "?" reaching generated source
    // ========================================================================

    [Test]
    public void IntDiv_WithNonConstantOperands_EmitsDivision_NotAQuestionMark()
    {
        // The statement position: Visit(IRBinaryOp) interpolates MapBinaryOperator's result
        // into "{result} = {left} {op} {right};".
        var source = @"
Function IDiv(a As Integer, b As Integer) As Integer
    Return a \ b
End Function

Sub Main()
    Console.WriteLine(IDiv(7, 2))
End Sub
";
        var cpp = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, "compilation should succeed");
        Assert.That(cpp, Is.Not.Null);
        Assert.That(UserCode(cpp), Does.Not.Contain(" ? "),
            "a literal '?' in the generated C++ is a syntax error that the CLI reports as success");
        Assert.That(UserCode(cpp), Does.Contain("a / b"),
            "integer division lowers to '/' on integral C++ operands");
    }

    [Test]
    public void IntDiv_InsideAWhenGuard_EmitsDivision_NotAQuestionMark()
    {
        // The SECOND call site: a suppressed `When` guard is never emitted as instructions, so
        // it goes through RenderInline rather than Visit(IRBinaryOp). Covering only the
        // statement position would leave this one broken.
        var source = @"
Function Pick(a As Integer, b As Integer) As String
    Select Case a
        Case Is > 0 When (a \ b) > 1
            Return ""big""
        Case Else
            Return ""small""
    End Select
End Function

Sub Main()
    Console.WriteLine(Pick(9, 2))
End Sub
";
        var cpp = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, "compilation should succeed");
        Assert.That(cpp, Is.Not.Null);
        Assert.That(UserCode(cpp), Does.Not.Contain(" ? "),
            "the inline guard renderer shares MapBinaryOperator and emitted '?' too");
    }

    [Test]
    public void IntDiv_ThroughTheOptimizingRoute_StillEmitsDivision()
    {
        // The CLI and the IDE always optimize; the non-optimizing test helper does not. A fix
        // verified only through the non-optimizing helper has been wrong before in this repo.
        var source = @"
Function IDiv(a As Integer, b As Integer) As Integer
    Return a \ b
End Function

Sub Main()
    Console.WriteLine(IDiv(7, 2))
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty, "compilation should succeed");
        Assert.That(cpp, Is.Not.Null);
        Assert.That(UserCode(cpp), Does.Not.Contain(" ? "));
        Assert.That(UserCode(cpp), Does.Contain("a / b"));
    }

    // ========================================================================
    // Defect 2 — the result width the "?" was hiding
    // ========================================================================

    [Test]
    public void IntDiv_OnLongOperands_KeepsTheSixtyFourBitWidth()
    {
        // THE TEST THAT MAKES FIXING DEFECT 1 SAFE. Every integral type in this program is
        // Long, so a single int32_t anywhere in the output means the division result was
        // narrowed. Measured pre-fix: "int64_t IDivL(int64_t a, int64_t b) { int32_t t0 = {};
        // ... }" — and with a plain assignment rather than brace-init, C++ narrows silently.
        var source = @"
Function IDivL(a As Long, b As Long) As Long
    Return a \ b
End Function

Sub Main()
    Console.WriteLine(IDivL(9000000000, 2))
End Sub
";
        var cpp = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, "compilation should succeed");
        Assert.That(cpp, Is.Not.Null);
        Assert.That(Body(cpp, "int64_t IDivL(int64_t a, int64_t b)"), Does.Not.Contain("int32_t"),
            "the division temp must stay 64-bit; an int32_t temp silently truncates modulo 2^32 " +
            "(9000000000 \\ 2 would print 205032704 instead of 4500000000)");
    }

    [Test]
    public void IntDiv_OnByteOperands_IsAcceptedByTheFrontEnd()
    {
        // The same hardcoded Integer result type leaked out as a user-visible error: because
        // `a \ b` typed as Integer regardless of operands, returning it from a Byte function
        // was rejected as a narrowing conversion. VB.NET types Byte \ Byte as Byte.
        var source = @"
Function IDivB(a As Byte, b As Byte) As Byte
    Return a \ b
End Function

Sub Main()
    Console.WriteLine(""ok"")
End Sub
";
        var cpp = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty,
            "Byte \\ Byte is Byte in VB.NET; hardcoding the result to Integer made this a " +
            "spurious narrowing error");
        Assert.That(cpp, Is.Not.Null);
    }

    [Test]
    public void IntDiv_OnMixedIntegerAndLong_WidensToLong()
    {
        var source = @"
Function IDivM(a As Long, b As Integer) As Long
    Return a \ b
End Function

Sub Main()
    Console.WriteLine(IDivM(9000000000, 2))
End Sub
";
        var cpp = CompileToCpp(source, out var errors);

        Assert.That(errors, Is.Empty, "compilation should succeed");
        Assert.That(cpp, Is.Not.Null);
        // NOT Does.Contain("int64_t") — the signature "int64_t IDivM(int64_t a, int32_t b)"
        // satisfies that on its own, so the assertion passed against the BROKEN compiler.
        // Scoped to the function body, and to "int32_t t" specifically, because the int32_t
        // parameter b legitimately appears in the signature.
        Assert.That(Body(cpp, "int64_t IDivM(int64_t a, int32_t b)"), Does.Not.Contain("int32_t t"),
            "mixed-width integer division widens to the larger operand type; a 32-bit temp " +
            "silently truncates the 64-bit quotient");
    }

    // ========================================================================
    // The seam that let defect 1 ship silently
    // ========================================================================

    [Test]
    public void AnUnmappedBinaryOperator_IsRefusedLoudly_NeverRenderedAsAQuestionMark()
    {
        // MapBinaryOperator's `_ => "?"` default could never produce valid C++ — so replacing
        // it with a throw cannot break a program that currently works, and it converts the
        // next missing arm from a silent miscompile into an immediate failure.
        //
        // BitwiseAnd is the probe because it has no producer anywhere in the compiler
        // (SemanticAnalyzer maps `&` to string concatenation), so it can only be reached the
        // way a future IRBuilder change would reach it.
        var generator = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        var method = typeof(CppCodeGenerator).GetMethod(
            "MapBinaryOperator",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null,
            "MapBinaryOperator is the single wired operator map for the C++ backend");

        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => method.Invoke(generator, new object[] { BinaryOpKind.BitwiseAnd }));

        Assert.That(ex.InnerException, Is.Not.Null);
        Assert.That(ex.InnerException.Message, Does.Contain("BitwiseAnd"),
            "the refusal must name the operator, or the next person gets a bare exception");
    }
}
