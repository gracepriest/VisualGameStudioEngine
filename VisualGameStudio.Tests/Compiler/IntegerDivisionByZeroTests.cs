using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Integral <c>\</c> and <c>Mod</c> by zero throw <c>DivideByZeroException</c>, and
/// <c>MinValue \ -1</c> / <c>MinValue Mod -1</c> throw <c>OverflowException</c>, as in .NET.
/// MEASURED on master ad43edd with <c>z = Zero()</c> and <c>Try … Catch e As DivideByZeroException</c>:
/// <list type="bullet">
/// <item>JavaScript printed <c>Infinity</c> for <c>7 \ z</c> and <c>NaN</c> for <c>7 Mod z</c> and
/// carried on — the handler never ran (JS numbers are doubles).</item>
/// <item>C++ died with SIGFPE, exit 136 — a bare integer <c>/</c> or <c>%</c> by zero is undefined
/// behaviour and an x86 hardware trap, so no <c>Catch</c> could see it.</item>
/// <item>C# was right at run time, but <c>7 \ 0</c> emitted <c>7 / 0</c>, and the C# build failed
/// with CS0020 "Division by constant zero" where the other backends now throw at run time.</item>
/// </list>
/// </summary>
public class IntegerDivisionByZeroTests
{
    internal const string Program = @"
Function Zero() As Integer
    Return 0
End Function
Function MinusOne() As Integer
    Return -1
End Function
Sub Guard(z As Integer)
    Select Case z
        Case Is <= 1 When 5 \ z = 5
            Console.WriteLine(""guard true"")
        Case Else
            Console.WriteLine(""guard else"")
    End Select
End Sub
Sub Main()
    Dim a As Integer = 7
    Dim z As Integer = Zero()
    Dim m As Integer = -2147483647 - 1
    Dim n1 As Integer = MinusOne()
    Try
        Console.WriteLine(a \ z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""div: "" & ex.Message)
    End Try
    Try
        Console.WriteLine(a Mod z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""mod: "" & ex.Message)
    End Try
    Try
        Console.WriteLine(a \ 0)
    Catch ex As DivideByZeroException
        Console.WriteLine(""literal zero"")
    End Try
    Try
        Console.WriteLine(7 Mod 0)
    Catch ex As DivideByZeroException
        Console.WriteLine(""constant by zero"")
    End Try
    Try
        Console.WriteLine(z \ z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""zero by zero"")
    End Try
    Try
        Console.WriteLine(m \ n1)
    Catch ex As OverflowException
        Console.WriteLine(""overflow: "" & ex.Message)
    End Try
    Try
        Console.WriteLine(m Mod n1)
    Catch ex As OverflowException
        Console.WriteLine(""overflow mod"")
    End Try
    Try
        Guard(z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""guard threw"")
    End Try
    Guard(1)
    Console.WriteLine(m \ 1)
    Console.WriteLine(-7 \ 2)
    Console.WriteLine(-7 Mod 2)
    Console.WriteLine(-7 Mod n1)
    Console.WriteLine((z - 1) \ 5)
    Console.WriteLine(""end"")
End Sub
";

    internal const string Expected =
        "div: Attempted to divide by zero.\n" +
        "mod: Attempted to divide by zero.\n" +
        "literal zero\n" +
        "constant by zero\n" +
        "zero by zero\n" +
        "overflow: Arithmetic operation resulted in an overflow.\n" +
        "overflow mod\n" +
        "guard threw\n" +
        "guard true\n" +
        "-2147483648\n" +
        "-3\n" +
        "-1\n" +
        "0\n" +
        "0\n" +
        "end";

    [Test]
    public void CSharp_LiteralZeroDivisor_IsNotAConstant_SoTheBuildSucceeds()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("7 % ((int)(object)(int)0)"), cs);
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(Program), Is.Empty);
        });
    }

    [Test]
    public void CSharp_NonZeroLiteralDivisor_IsUnchanged()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(
            "Function Seven() As Integer\n    Return 7\nEnd Function\n" +
            "Sub Main()\n    Dim a As Integer = Seven()\n    Console.WriteLine(a \\ 2)\n    Console.WriteLine(a Mod 3)\nEnd Sub\n");
        Assert.That(cs, Does.Not.Contain("(object)"), cs);
    }

    [Test]
    public void Cpp_IntegralDivisionAndMod_GoThroughTheCheckedHelpers()
    {
        var cpp = BclE2E.CompileToCppOptimized(Program);
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::IntDiv(a, z)"));
            Assert.That(cpp, Does.Contain("BasicLang::IntMod(a, z)"));
            // The When guard renders inline, through RenderInline — the other emission path.
            Assert.That(cpp, Does.Contain("BasicLang::IntDiv(5, z)"));
            Assert.That(cpp, Does.Contain("\"System.DivideByZeroException;System.ArithmeticException;System.SystemException;System.Exception\""));
        });
    }

    [Test]
    public void JavaScript_EmitsTheHelpers_AndTheClassesTheyThrow()
    {
        var js = JsTestSupport.CompileAggressive(Program);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("function __blIntDiv(a, b)"));
            Assert.That(js, Does.Contain("function __blMod(a, b)"));
            Assert.That(js, Does.Contain("class DivideByZeroException extends ArithmeticException"));
            Assert.That(js, Does.Contain("class OverflowException extends ArithmeticException"));
            Assert.That(js, Does.Not.Contain("Math.trunc(a / z)"));
        });
    }

    /// <summary>
    /// The helper is emitted on demand. A When guard is built with emission suppressed, so it is
    /// in no block — a block-only scan would miss it and Node would die with
    /// "__blIntDiv is not defined".
    /// </summary>
    [Test]
    public void JavaScript_HelperIsEmitted_ForAGuardOnlyUse()
    {
        var js = JsTestSupport.CompileAggressive(@"
Sub Guard(z As Integer)
    Select Case z
        Case Is <= 1 When 5 \ z = 5
            Console.WriteLine(""t"")
        Case Else
            Console.WriteLine(""f"")
    End Select
End Sub
Sub Main()
    Guard(1)
End Sub
");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("__blIntDiv(5, z)"), js);
            Assert.That(js, Does.Contain("function __blIntDiv(a, b)"), js);
            Assert.That(js, Does.Contain("class DivideByZeroException"), js);
        });
    }

    [Test]
    public void JavaScript_HelperIsAbsent_WithoutIntegralDivision()
    {
        var js = JsTestSupport.CompileAggressive(
            "Sub Main()\n    Dim d As Double = Val(\"7.5\")\n    Console.WriteLine(d Mod 2.0)\nEnd Sub\n");
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("__blIntDiv"), js);
            Assert.That(js, Does.Not.Contain("__blMod"), js);
            Assert.That(js, Does.Not.Contain("DivideByZeroException"), js);
        });
    }
}

/// <summary>The program run on each backend, through the optimizer.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class IntegerDivisionByZeroRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(IntegerDivisionByZeroTests.Program)),
            Is.EqualTo(IntegerDivisionByZeroTests.Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(IntegerDivisionByZeroTests.Program))),
            Is.EqualTo(IntegerDivisionByZeroTests.Expected));

    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(IntegerDivisionByZeroTests.Program))),
            Is.EqualTo(IntegerDivisionByZeroTests.Expected));

    /// <summary>64-bit on C++ (JavaScript refuses Long, BL7003): the Long.MinValue \ -1 trap.</summary>
    [Test]
    public void Cpp_Long_Runs()
    {
        const string program = @"
Function MinusOne() As Long
    Return -1
End Function
Sub Main()
    Dim lm As Long = -9223372036854775807L - 1L
    Dim n1 As Long = MinusOne()
    Dim z As Long = n1 + 1L
    Try
        Console.WriteLine(lm \ n1)
    Catch ex As OverflowException
        Console.WriteLine(""overflow"")
    End Try
    Try
        Console.WriteLine(lm Mod z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""zero"")
    End Try
End Sub
";
        // Not Assert.Multiple: CompileRun's no-compiler Assert.Ignore would FAIL inside one.
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo("overflow\nzero"));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))),
            Is.EqualTo("overflow\nzero"));
    }
}
