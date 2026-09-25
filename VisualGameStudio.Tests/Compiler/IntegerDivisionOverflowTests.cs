using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A signed minimum <c>\</c> or <c>Mod</c> -1 must throw OverflowException ("Arithmetic operation
/// resulted in an overflow."), as .NET does for Integer and Long, on every backend.
///
/// <para><b>MEASURED on master</b> (the .NET answer taken from a hand-written C# program):</para>
/// <code>
///   C++         SIGFPE (exit 136) — INT_MIN / -1 is undefined behaviour and traps on x86. Also:
///               the literal `-2147483648` is `-(2147483648)`, typed LONG in C++, so the minimum
///               Integer silently became 64-bit (the helper then saw no overflow), and
///               `9223372036854775808LL` fits no signed type at all.
///   JavaScript  `\` returned 2147483648 (out of Integer range, stored anyway) and Mod 0 — no
///               throw. Separately, a zero Mod result with a negative dividend printed "-0":
///               JavaScript has a negative zero, .NET integers do not.
///   C#          correct for variables, but a CONSTANT minimum over a constant -1 was CS0220
///               ("overflows at compile time in checked mode") — the CS0020 trap again.
/// </code>
/// </summary>
[TestFixture]
public class IntegerDivisionOverflowTests
{
    [Test]
    public void CppHelperOverflowChain_MatchesTheOverflowExceptionTable()
    {
        Assert.That(CppExceptionTypes.TryGetInheritanceChain("OverflowException", out var chain), Is.True);
        Assert.That(CppIntegerDivisionRuntime.Source, Does.Contain($"\"{chain}\""));
    }

    [Test]
    public void Cpp_MinimumIntegerAndLongConstants_KeepTheirOwnType()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(
            "Sub Main()\n    Dim m As Integer = -2147483647 - 1\n    Dim l As Long = -9223372036854775807 - 1\n" +
            "    Console.WriteLine(m)\n    Console.WriteLine(l)\nEnd Sub\n"));
        Assert.That(cpp, Does.Contain("(-2147483647 - 1)"), "-2147483648 is a LONG expression in C++");
        Assert.That(cpp, Does.Not.Contain("-2147483648"));
        // (The Long source form is not folded by the optimizer — it emits `-9223372036854775807LL
        // - 1`, which is valid — so the folded-constant spelling is pinned on hand-built IR below.)
        Assert.That(cpp, Does.Not.Contain("9223372036854775808"), "9223372036854775808LL fits no signed type");
    }

    /// <summary>A folded Long.MinValue constant must not be spelled 9223372036854775808LL.</summary>
    [Test]
    public void Cpp_FoldedLongMinimumConstant_IsSpelledWithoutAnUnrepresentableLiteral()
    {
        var longType = new BasicLang.Compiler.SemanticAnalysis.TypeInfo("Long", BasicLang.Compiler.SemanticAnalysis.TypeKind.Primitive);
        var module = new BasicLang.Compiler.IR.IRModule("MinProbe");
        var f = module.CreateFunction("F", longType);
        var block = f.CreateBlock("entry");
        block.AddInstruction(new BasicLang.Compiler.IR.IRReturn(new BasicLang.Compiler.IR.IRConstant(long.MinValue, longType)));

        var cpp = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false }).Generate(module);
        Assert.That(cpp, Does.Contain("(-9223372036854775807LL - 1)"));
        Assert.That(cpp, Does.Not.Contain("9223372036854775808"));
    }

    [Test]
    public void CSharp_ConstantMinimumOverMinusOne_Compiles()
    {
        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(
            "Sub Main()\n    Try\n        Dim q As Integer = (-2147483647 - 1) \\ -1\n        Console.WriteLine(q)\n" +
            "    Catch ex As OverflowException\n        Console.WriteLine(\"o\")\n    End Try\nEnd Sub\n");
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    [Test]
    public void Js_Helpers_CheckTheMinimumAndNormaliseNegativeZero()
    {
        var js = JsTestSupport.CompileOptimized(
            "Sub P(a As Integer, b As Integer)\n    Console.WriteLine(a \\ b)\n    Console.WriteLine(a Mod b)\nEnd Sub\n" +
            "Sub Main()\n    P(7, 2)\nEnd Sub\n");
        Assert.That(js, Does.Contain("if (b === -1 && a === -2147483648) throw new OverflowException("));
        // `+ 0` turns a JavaScript negative zero into 0 (master's spelling, kept at the merge).
        Assert.That(js, Does.Contain("return Math.trunc(a / b) + 0;"));
        Assert.That(js, Does.Contain("return a % b + 0;"));
        Assert.That(js, Does.Contain("class OverflowException extends ArithmeticException"),
            "the helper throws a class the program never names");
    }
}

/// <summary>The run half: C# (real .NET), C++ and JavaScript through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class IntegerDivisionOverflowRunTests
{
    private const string IntegerProgram = @"
Sub DivOv(a As Integer, b As Integer)
    Try
        Dim q As Integer = a \ b
        Console.WriteLine(""div no throw "" & q)
    Catch ex As OverflowException
        Console.WriteLine(""div overflow: "" & ex.Message)
    End Try
End Sub

Sub ModOv(a As Integer, b As Integer)
    Try
        Dim q As Integer = a Mod b
        Console.WriteLine(""mod no throw "" & q)
    Catch ex As ArithmeticException
        Console.WriteLine(""mod arith"")
    End Try
End Sub

Sub LiteralOv()
    Try
        Dim q As Integer = (-2147483647 - 1) \ -1
        Console.WriteLine(""lit no throw "" & q)
    Catch ex As OverflowException
        Console.WriteLine(""lit overflow"")
    End Try
End Sub

Sub Normal(a As Integer, b As Integer)
    Console.WriteLine(a \ b)
    Console.WriteLine(a Mod b)
End Sub

Sub Main()
    Dim mn As Integer = -2147483647 - 1
    DivOv(mn, -1)
    ModOv(mn, -1)
    LiteralOv()
    Normal(mn, 2)
    Normal(-7, -1)
    Normal(-1, 5)
End Sub
";

    // The last four lines are the negative-zero cases: .NET prints 0, JavaScript printed -0.
    private const string IntegerExpected =
        "div overflow: Arithmetic operation resulted in an overflow.\nmod arith\nlit overflow\n" +
        "-1073741824\n0\n7\n0\n0\n-1";

    private const string LongProgram = @"
Sub LongOv(a As Long, b As Long)
    Try
        Dim q As Long = a \ b
        Console.WriteLine(q)
    Catch ex As OverflowException
        Console.WriteLine(""long overflow"")
    End Try
End Sub

Sub Main()
    Dim lm As Long = -9223372036854775807 - 1
    LongOv(lm, -1)
    LongOv(lm, 2)
    Try
        Dim q As Long = (-9223372036854775807 - 1) \ -1
        Console.WriteLine(q)
    Catch ex As OverflowException
        Console.WriteLine(""long lit overflow"")
    End Try
End Sub
";

    private const string LongExpected = "long overflow\n-4611686018427387904\nlong lit overflow";

    private static string Norm(string s) => FourBackends.Norm(s);

    [Test]
    public void IntegerMinimumOverMinusOne_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(IntegerProgram)), Is.EqualTo(IntegerExpected));

    [Test]
    public void IntegerMinimumOverMinusOne_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(IntegerProgram))), Is.EqualTo(IntegerExpected),
            "an empty stdout means SIGFPE");

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void IntegerMinimumOverMinusOne_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(IntegerProgram))),
            Is.EqualTo(IntegerExpected));

    [Test]
    public void LongMinimumOverMinusOne_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(LongProgram)), Is.EqualTo(LongExpected));

    /// <summary>C++ only among the native backends: JavaScript refuses Long (BL7003) by design.</summary>
    [Test]
    public void LongMinimumOverMinusOne_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(LongProgram))), Is.EqualTo(LongExpected));
}
