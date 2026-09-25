using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// An integral <c>\</c> or <c>Mod</c> by zero must throw a catchable DivideByZeroException on the
/// C++ and JavaScript backends, as it does in .NET (and on the C# backend).
///
/// <para><b>MEASURED on master</b>, <c>Try : q = n \ z : Catch ex As DivideByZeroException</c>
/// with z = 0:</para>
/// <code>
///   C++         the process died with SIGFPE (exit 136) before the Catch could run —
///               integer division by zero is undefined behaviour in C++.
///   JavaScript  no throw: `\` was Math.trunc(7 / 0) = Infinity and Mod was NaN, stored in an
///               Integer and printed. JavaScript has no integer division.
///   Both        a `When n \ z > 1` guard took its arm on JavaScript (Infinity > 1): guards skip
///               the semantic analyzer, so their operator trees carry no type.
/// </code>
/// <para>Fix (master's, kept at the merge — see IntegerDivisionByZeroTests): C++ routes integral
/// <c>\</c>/<c>Mod</c> through <c>BasicLang::IntDiv</c> / <c>IntMod</c>
/// (<c>CppIntegerDivisionRuntime</c>), which throw the typed <c>NetException</c> the Catch ladder
/// matches; JavaScript through <c>__blIntDiv</c> / <c>__blMod</c>, which throw its real
/// <c>DivideByZeroException</c> class. Floating division is untouched (Infinity, as in .NET).</para>
/// </summary>
[TestFixture]
public class IntegerDivideByZeroTests
{
    /// <summary>The C++ helpers' chain must be the one a typed Catch matches against.</summary>
    [Test]
    public void CppHelperChain_MatchesTheDivideByZeroExceptionTable()
    {
        Assert.That(CppExceptionTypes.TryGetInheritanceChain("DivideByZeroException", out var chain), Is.True);
        Assert.That(CppIntegerDivisionRuntime.Source, Does.Contain($"\"{chain}\""),
            "IntDiv/IntMod would throw an exception no `Catch … As DivideByZeroException` matches");
    }

    private const string DivSource =
        "Sub P(n As Integer, z As Integer)\n    Dim q As Integer = n \\ z\n    Dim m As Integer = n Mod z\n" +
        "    Console.WriteLine(q)\n    Console.WriteLine(m)\nEnd Sub\nSub Main()\n    P(7, 2)\nEnd Sub\n";

    private const string GuardSource =
        "Sub P(n As Integer, z As Integer)\n    Select Case n\n        Case 7 When n \\ z > 1\n" +
        "            Console.WriteLine(\"t\")\n        Case Else\n            Console.WriteLine(\"e\")\n" +
        "    End Select\nEnd Sub\nSub Main()\n    P(7, 2)\nEnd Sub\n";

    private const string DoubleSource =
        "Sub P(x As Double, y As Double)\n    Dim q As Double = x / y\n    Console.WriteLine(q)\nEnd Sub\n" +
        "Sub Main()\n    P(1.0, 2.0)\nEnd Sub\n";

    [Test]
    public void Cpp_IntegralDivisionAndModulo_GoThroughTheCheckedHelpers()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(DivSource));
        Assert.That(cpp, Does.Contain("BasicLang::IntDiv(n, z)"));
        Assert.That(cpp, Does.Contain("BasicLang::IntMod(n, z)"));
    }

    [Test]
    public void Cpp_UntypedWhenGuardDivision_GoesThroughTheCheckedHelper()
        => Assert.That(CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(GuardSource)),
            Does.Contain("BasicLang::IntDiv("));

    [Test]
    public void Cpp_FloatingDivision_IsLeftAlone()
        => Assert.That(CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(DoubleSource)),
            Does.Not.Contain("BasicLang::IntDiv("));

    [Test]
    public void Js_IntegralDivisionAndModulo_GoThroughTheCheckedHelpers()
    {
        var js = JsTestSupport.CompileOptimized(DivSource);
        Assert.That(js, Does.Contain("__blIntDiv(n, z)"));
        Assert.That(js, Does.Contain("__blMod(n, z)"));
        Assert.That(js, Does.Contain("function __blIntDiv("), "helper call sites with no definition");
        Assert.That(js, Does.Contain("class DivideByZeroException extends ArithmeticException"),
            "the helper throws a class the program never names, so the prelude must supply it");
    }

    [Test]
    public void Js_UntypedWhenGuardDivision_GoesThroughTheCheckedHelper()
    {
        var js = JsTestSupport.CompileOptimized(GuardSource);
        Assert.That(js, Does.Contain("__blIntDiv(n, z)"));
        Assert.That(js, Does.Contain("function __blIntDiv("));
    }

    /// <summary>The guard against overshooting: no division, no helper and no exception classes.</summary>
    [Test]
    public void Js_ProgramWithoutIntegralDivision_EmitsNoPrelude()
    {
        var js = JsTestSupport.CompileOptimized(DoubleSource);
        Assert.That(js, Does.Not.Contain("__blIntDiv"));
        Assert.That(js, Does.Not.Contain("class DivideByZeroException"));
    }
}

/// <summary>
/// The run half of <see cref="IntegerDivideByZeroTests"/>: one program through the optimizer on
/// C# (in-process Roslyn — real .NET, the reference), C++ and JavaScript. Values cover
/// truncation toward zero and the sign of Mod, so the helpers change nothing for a nonzero
/// divisor. Booleans are compared case-folded: JavaScript prints `true` (a known spelling gap).
///
/// <para>The <c>When</c> guard is exercised from a function called inside the Try, not written
/// inside it: a <c>Select Case</c> directly inside a <c>Try</c> is a separate, pre-existing defect
/// (C++ fails to compile with "jump to label", C# drops the Select entirely).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class IntegerDivideByZeroRunTests
{
    private const string Program = @"
Sub Values(a As Integer, b As Integer)
    Dim q As Integer = a \ b
    Dim m As Integer = a Mod b
    Console.WriteLine(q)
    Console.WriteLine(m)
End Sub

Sub AsDivideByZero(n As Integer, z As Integer)
    Try
        Dim q As Integer = n \ z
        Console.WriteLine(q)
    Catch ex As DivideByZeroException
        Console.WriteLine(""dbz"")
    End Try
End Sub

Sub AsArith(n As Integer, z As Integer)
    Try
        Dim q As Integer = n Mod z
        Console.WriteLine(q)
    Catch ex As ArithmeticException
        Console.WriteLine(""arith"")
    End Try
End Sub

Sub AsException(n As Integer, z As Integer)
    Try
        Dim q As Integer = 5 \ 0
        Console.WriteLine(q)
    Catch ex As Exception
        Console.WriteLine(ex.Message)
    End Try
End Sub

Sub GuardSel(n As Integer, z As Integer)
    Select Case n
        Case 7 When n \ z > 1
            Console.WriteLine(""guard true"")
        Case Else
            Console.WriteLine(""guard else"")
    End Select
End Sub

Sub Guard(n As Integer, z As Integer)
    Try
        GuardSel(n, z)
    Catch ex As DivideByZeroException
        Console.WriteLine(""guard dbz"")
    End Try
End Sub

Sub Dbl(x As Double, y As Double)
    Dim q As Double = x / y
    Console.WriteLine(q > 1.0E+308)
End Sub

Sub Main()
    Values(-7, 2)
    Values(7, -2)
    Values(-7, -2)
    AsDivideByZero(7, 0)
    AsArith(7, 0)
    AsException(7, 0)
    Guard(7, 0)
    Guard(7, 2)
    Dbl(1.0, 0.0)
End Sub
";

    private const string Expected =
        "-3\n-1\n-3\n1\n3\n-1\ndbz\narith\nAttempted to divide by zero.\nguard dbz\nguard true\ntrue";

    private static string Norm(string s) => FourBackends.Norm(s).Replace("True", "true");

    [Test]
    public void IntegerDivideByZero_Throws_CSharpReference()
        => Assert.That(Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected));

    [Test]
    public void IntegerDivideByZero_Throws_Cpp()
        => Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected),
            "an empty or truncated stdout means the process died of SIGFPE");

    // CompileOptimized, NOT JavaScriptExecutionTests.RunJs — that one runs no optimizer.
    [Test]
    public void IntegerDivideByZero_Throws_JavaScript()
        => Assert.That(Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(Program))),
            Is.EqualTo(Expected));

    /// <summary>64-bit on C++ only: JavaScript refuses Long (BL7003) by design.</summary>
    [Test]
    public void LongDivideByZero_Throws_Cpp()
    {
        const string program =
            "Sub P(n As Long, z As Long)\n    Try\n        Dim q As Long = n \\ z\n        Console.WriteLine(q)\n" +
            "    Catch ex As DivideByZeroException\n        Console.WriteLine(\"dbz\")\n    End Try\nEnd Sub\n" +
            "Sub Main()\n    P(7, 0)\nEnd Sub\n";
        Assert.That(Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program))), Is.EqualTo("dbz"));
    }
}
