using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Boolean rendering on the C++ backend — chip task_6fd2c7e4.
///
/// <c>cout</c> streams a bool as 1/0. .NET, VB and the C# backend all print True/False, so
/// identical BasicLang source printed different text depending on the backend. Measured
/// broken in all four positions: literal, variable, comparison result, and function return.
///
/// ⛔ THE PARENTHESES IN THE FIX ARE LOAD-BEARING, NOT STYLE, and the failure mode is
/// SILENT. The four call sites splice this arg into <c>cout &lt;&lt; {arg} &lt;&lt; endl</c>,
/// and <c>&lt;&lt;</c> binds TIGHTER than <c>?:</c>. Unparenthesized,
/// <c>cout &lt;&lt; b ? "True" : "False"</c> parses as <c>(cout &lt;&lt; b) ? ... : ...</c> —
/// it COMPILES CLEAN, prints 1, and discards both strings. A reviewer reading the generated
/// .cpp would see ternaries everywhere and believe it fixed. Both forms were hand-compiled to
/// confirm this before the fix was written; <see cref="BooleanArgument_IsParenthesized"/>
/// is the test that keeps it honest.
/// </summary>
[TestFixture]
public class CppBooleanPrintingTests
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

    /// <summary>
    /// Everything after the native runtime prelude. The prelude legitimately contains
    /// <c>? "True" : "False"</c> (StringBuilder.Append(bool) already got this right), so a
    /// whole-file assertion would pass without the backend emitting anything at all.
    /// </summary>
    private static string UserCode(string cpp)
    {
        const string marker = "// Function implementations";
        var i = cpp.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(i, Is.GreaterThanOrEqualTo(0), "emitted C++ has no user-function section");
        return cpp.Substring(i);
    }

    [Test]
    public void BooleanParameter_PrintsTrueFalse_NotOneZero()
    {
        var source = @"
Function Show(flag As Boolean) As Boolean
    Console.WriteLine(flag)
    Return flag
End Function

Sub Main()
    Console.WriteLine(Show(True))
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("? \"True\" : \"False\""),
            "cout streams a bool as 1/0; .NET and the C# backend print True/False");
    }

    [Test]
    public void BooleanArgument_IsParenthesized()
    {
        // THE TEST THAT MAKES THE FIX REAL. Without the outer parens the generated source
        // still LOOKS fixed but prints 1, because `<<` binds tighter than `?:`.
        var source = @"
Function Show(flag As Boolean) As Boolean
    Console.WriteLine(flag)
    Return flag
End Function

Sub Main()
    Console.WriteLine(Show(True))
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("(flag ? \"True\" : \"False\")"),
            "the ternary MUST be parenthesized — `cout << flag ? ... : ...` parses as " +
            "`(cout << flag) ? ... : ...`, compiles clean, and silently prints 1");
    }

    [Test]
    public void BooleanLiteral_PrintsTrueFalse()
    {
        // The chip's own repro. A literal reaches the print arm as an IRConstant, and its
        // Type channel is not guaranteed to be populated — so a type-name-only predicate
        // misses exactly the case the chip reported.
        var source = @"
Sub Main()
    Console.WriteLine(True)
    Console.WriteLine(False)
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("(true ? \"True\" : \"False\")"));
        Assert.That(UserCode(cpp), Does.Contain("(false ? \"True\" : \"False\")"));
    }

    [Test]
    public void BooleanComparisonResult_PrintsTrueFalse()
    {
        var source = @"
Function Bigger(x As Integer, y As Integer) As Boolean
    Return x > y
End Function

Sub Main()
    Console.WriteLine(Bigger(20, 3))
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("? \"True\" : \"False\""));
    }

    [Test]
    public void ANonBooleanArgument_IsNotWrappedInATernary()
    {
        // Specificity guard: a predicate that fired on everything would satisfy every test
        // above while corrupting all other printing.
        var source = @"
Sub Main()
    Dim n As Integer = 42
    Console.WriteLine(n)
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Not.Contain("\"True\""),
            "only Boolean arguments become True/False");
    }

    [Test]
    public void ByteArgument_StillWidensRatherThanPrintingAsACharacter()
    {
        // Pre-existing behaviour sharing the same method (spec §14.6): cout streams
        // uint8_t as a CHARACTER, so Console.WriteLine(b) with b = 65 printed 'A'. The
        // Boolean arm is inserted ahead of this one and must not displace it.
        var source = @"
Function ShowByte(b As Byte) As Byte
    Console.WriteLine(b)
    Return b
End Function

Sub Main()
    Console.WriteLine(""ok"")
End Sub
";
        var cpp = CompileToCppOptimized(source, out var errors);

        Assert.That(errors, Is.Empty);
        Assert.That(UserCode(cpp), Does.Contain("static_cast<int32_t>(b)"),
            "Byte still widens for printing; the Boolean arm must not displace it");
    }
}
