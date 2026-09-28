using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A non-String hole in an interpolated string (<c>$"n={N()}"</c>) converts through <c>CStr</c>.
/// IRBuilder used to emit an IRCall to a free function named <c>ToString</c>, which no backend
/// has. MEASURED on master 5adccd8, both pipelines: C# failed to build with CS1501 "No overload for
/// method 'ToString' takes 1 arguments", C++ with "'ToString' was not declared in this scope",
/// and JavaScript called an undefined <c>ToString</c>. Only an all-String interpolation worked.
/// </summary>
public class StringInterpolationTests
{
    // No Char: JavaScript refuses the type (BL7004). CharProgram covers it where it exists.
    internal const string Program = @"
Function T() As Boolean
    Return True
End Function
Function N() As Integer
    Return 5
End Function
Sub Main()
    Dim d As Double = 2.5
    Dim s As String = ""abc""
    Console.WriteLine($""n={N()} b={T()} d={d} s={s}"")
    Console.WriteLine($""{N()}"")
    Console.WriteLine($""{N() + 1}{N()}"")
    Dim msg As String = $""sum {N() * 2}!""
    Console.WriteLine(msg)
    Console.WriteLine($""plain"")
    Console.WriteLine($""{d / 4}"")
End Sub
";

    internal const string Expected = "n=5 b=True d=2.5 s=abc\n5\n65\nsum 10!\nplain\n0.625";

    internal const string CharProgram = @"
Sub Main()
    Dim c As Char = ""z""c
    Console.WriteLine($""c={c}"")
End Sub
";

    [Test]
    public void CSharp_ConvertsTheHole_AndCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(Program);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Not.Match(@"[^.\w]ToString\("), "a free ToString(x) call does not exist:\n" + cs);
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(Program), Is.Empty);
        });
    }

    [Test]
    public void Cpp_HasNoFreeToStringCall()
    {
        var user = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program));
        Assert.That(user, Does.Not.Match(@"[^.>\w:]ToString\("), user);
    }

    [Test]
    public void JavaScript_HasNoFreeToStringCall()
    {
        var js = JsTestSupport.CompileAggressive(Program);
        Assert.That(js, Does.Not.Match(@"[^.\w]ToString\("), js);
    }
}

/// <summary>The program on each backend, through the optimizer.</summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class StringInterpolationRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(StringInterpolationTests.Program)),
            Is.EqualTo(StringInterpolationTests.Expected));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(StringInterpolationTests.Program))),
            Is.EqualTo(StringInterpolationTests.Expected));

    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(JsTestSupport.CompileAggressive(StringInterpolationTests.Program))),
            Is.EqualTo(StringInterpolationTests.Expected));

    /// <summary>Skips without ilasm (Windows-only).</summary>
    [Test]
    public void Msil_Runs() =>
        Assert.That(FourBackends.Norm(Msil.MsilHarness.RunAggressiveExpectingSuccess(StringInterpolationTests.Program)),
            Is.EqualTo(StringInterpolationTests.Expected));

    [Test]
    public void Char_CSharpAndCpp_Run()
    {
        // Not Assert.Multiple: CompileRun's no-compiler Assert.Ignore would FAIL inside one.
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(StringInterpolationTests.CharProgram)), Is.EqualTo("c=z"));
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(StringInterpolationTests.CharProgram))),
            Is.EqualTo("c=z"));
    }
}
