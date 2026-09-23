using System.Text.RegularExpressions;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// StrengthReductionPass rewrites <c>x * 2^k</c> to <c>x &lt;&lt; k</c>. Its guard used to test only
/// the constant, so a FLOATING operand matched too. MEASURED on master 7e2d3836 with
/// <c>Const Half As Double = 2.5</c> / <c>CStr(Half * 2)</c>:
/// <list type="bullet">
/// <item>C# emitted <c>Half &lt;&lt; 1</c> — CS0019, the program does not compile;</item>
/// <item>C++ emitted <c>Half &lt;&lt; 1</c> — ill-formed on a double;</item>
/// <item>JavaScript emitted <c>(Half &lt;&lt; 1)</c> — COMPILES and prints 4, because <c>&lt;&lt;</c>
/// truncates to int32 first. A silent miscompile.</item>
/// </list>
/// A Const Single, a Double local and a Single local all matched the same way. Every program here
/// goes THROUGH the optimizer — the non-optimizing helpers never run the pass.
/// </summary>
public class StrengthReductionFloatTests
{
    private const string FloatProgram = @"
Const Half As Double = 2.5
Const HalfS As Single = 2.5F
Sub Main()
    Dim d As Double = 1.25
    Dim s As Single = 1.5F
    Console.WriteLine(CStr(Half * 2))
    Console.WriteLine(CStr(HalfS * 4))
    Console.WriteLine(CStr(d * 8))
    Console.WriteLine(CStr(s * 2))
End Sub
";

    private const string IntegerProgram = @"
Function Scale(p As Integer) As Integer
    Return p * 8
End Function
Function ScaleL(q As Long) As Long
    Return q * 1024
End Function
Sub Main()
    Console.WriteLine(CStr(Scale(3)))
    Console.WriteLine(CStr(ScaleL(3)))
End Sub
";

    /// <summary>A left shift whose left operand is one of the program's floating names or literals.</summary>
    private static readonly Regex FloatShift =
        new(@"\b(Half|HalfS|1\.25|1\.5f?)\s*<<", RegexOptions.Compiled);

    [Test]
    public void CSharp_FloatTimesPowerOfTwo_StaysAMultiply()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(FloatProgram);
        Assert.Multiple(() =>
        {
            Assert.That(FloatShift.IsMatch(cs), Is.False, "a float operand was shifted:\n" + cs);
            Assert.That(cs, Does.Contain("Half * 2"), "Const Double");
            Assert.That(cs, Does.Contain("HalfS * 4"), "Const Single");
        });
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(FloatProgram), Is.Empty,
            "the emitted C# must compile (it was CS0019)");
    }

    [Test]
    public void Cpp_FloatTimesPowerOfTwo_StaysAMultiply()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(FloatProgram));
        Assert.Multiple(() =>
        {
            Assert.That(FloatShift.IsMatch(cpp), Is.False, "a float operand was shifted:\n" + cpp);
            Assert.That(cpp, Does.Contain("Half * 2"), "Const Double");
            Assert.That(cpp, Does.Contain("HalfS * 4"), "Const Single");
        });
    }

    [Test]
    public void JavaScript_FloatTimesPowerOfTwo_StaysAMultiply()
    {
        var js = JsTestSupport.CompileOptimized(FloatProgram);
        Assert.Multiple(() =>
        {
            Assert.That(FloatShift.IsMatch(js), Is.False, "a float operand was shifted:\n" + js);
            Assert.That(js, Does.Contain("Half * 2"), "Const Double");
            Assert.That(js, Does.Contain("HalfS * 4"), "Const Single");
        });
    }

    /// <summary>The integral reduction is sound and must survive untouched.</summary>
    [Test]
    public void IntegerTimesPowerOfTwo_StillShifts()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(IntegerProgram);
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(IntegerProgram));
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("p << 3"), "C# Integer");
            Assert.That(cs, Does.Contain("q << 10"), "C# Long");
            Assert.That(cpp, Does.Contain("p << 3"), "C++ Integer");
            Assert.That(cpp, Does.Contain("q << 10"), "C++ Long");
        });
    }
}

/// <summary>
/// The same shapes, COMPILED AND RUN. The answers are checked with comparisons inside the program
/// rather than by printing the number, so the C++ backend's separate CStr-of-Double formatting
/// ("5.000000") does not decide the result.
/// </summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class StrengthReductionFloatRunTests
{
    private const string Program = @"
Const Half As Double = 2.5
Const HalfS As Single = 2.5F
Sub Check(tag As String, ok As Boolean)
    If ok Then
        Console.WriteLine(tag & "" ok"")
    Else
        Console.WriteLine(tag & "" BAD"")
    End If
End Sub
Sub Main()
    Dim d As Double = 1.25
    Dim s As Single = 1.5F
    Dim n As Integer = 7
    Check(""a"", Half * 2 = 5.0)
    Check(""b"", HalfS * 4 = 10.0F)
    Check(""c"", d * 8 = 10.0)
    Check(""d"", s * 2 = 3.0F)
    Check(""e"", n * 4 = 28)
End Sub
";

    private const string Expected = "a ok\nb ok\nc ok\nd ok\ne ok";

    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected));

    /// <summary>
    /// The silent one: JavaScript's <c>&lt;&lt;</c> truncated 2.5 to 2 and printed 4. Through
    /// <see cref="JsTestSupport.CompileOptimized"/>, NOT <c>JavaScriptExecutionTests.RunJs</c>:
    /// that helper skips the optimizer, so this test PASSED on the unfixed compiler through it.
    /// </summary>
    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileOptimized(Program))),
            Is.EqualTo(Expected));
}
