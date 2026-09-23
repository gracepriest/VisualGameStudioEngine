using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// PeepholeOptimizationPass's ring identities were type-blind. They hold for integers and are
/// false in IEEE 754. MEASURED on the JavaScript backend before the guard:
/// <list type="bullet">
/// <item><c>inf - inf</c> compiled to <c>0</c> — must be NaN (<c>x - x -> 0</c>);</item>
/// <item><c>inf * 0</c> compiled to <c>0</c> — must be NaN (<c>x * 0 -> 0</c>);</item>
/// <item><c>-5.0 * 0</c> compiled to <c>+0</c> — must be -0, so <c>1.0 / e</c> is -Infinity;</item>
/// <item><c>x / x</c> compiled to <c>1</c> — NaN at 0 (the arm is now gone for every type).</item>
/// </list>
/// Every program goes THROUGH the optimizer; the non-optimizing helpers never run the pass.
/// </summary>
public class PeepholeFloatIdentityTests
{
    private const string FloatProgram = @"
Function DivSelf(x As Double) As Double
    Dim r As Double = x / x
    Return r
End Function
Sub Check(tag As String, ok As Boolean)
    If ok Then
        Console.WriteLine(tag & "" ok"")
    Else
        Console.WriteLine(tag & "" BAD"")
    End If
End Sub
Function Square(v As Double) As Double
    Return v * v
End Function
Sub Probe(inf As Double, n As Double)
    Dim a As Double = inf - inf
    Dim b As Double = inf * 0
    Dim e As Double = n * 0
    Dim q As Double = DivSelf(0.0)
    Check(""a"", a <> a)
    Check(""b"", b <> b)
    Check(""e"", 1.0 / e < 0.0)
    Check(""q"", q <> q)
End Sub
Sub Main()
    Probe(Square(1.0E200), -5.0)
End Sub
";

    // Infinity and -5.0 arrive as PARAMETERS (Infinity as 1e200 squared at run time) so that
    // neither constant folding nor copy propagation can see the values: the peephole rule is
    // then the only thing that could fold them, which is what this fixture pins.
    private const string Expected = "a ok\nb ok\ne ok\nq ok";

    private const string IntegerProgram = @"
Function SubSelf(k As Integer) As Integer
    Dim r As Integer = k - k
    Return r
End Function
Function MulZero(k As Integer) As Integer
    Dim r As Integer = k * 0
    Return r
End Function
Sub Main()
    Console.WriteLine(CStr(SubSelf(4) + MulZero(4)))
End Sub
";

    /// <summary>The emitted text still carries the arithmetic instead of a folded constant.</summary>
    [Test]
    public void JavaScript_FloatIdentities_AreNotFolded()
    {
        var js = JsTestSupport.CompileOptimized(FloatProgram);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Contain("inf - inf"), "x - x");
            Assert.That(js, Does.Contain("inf * 0"), "x * 0");
            Assert.That(js, Does.Contain("n * 0"), "-x * 0");
            Assert.That(js, Does.Contain("x / x"), "x / x");
        });
    }

    [Test]
    public void CSharp_FloatIdentities_AreNotFolded()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(FloatProgram);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("inf - inf"), "x - x");
            Assert.That(cs, Does.Contain("inf * 0"), "x * 0");
            Assert.That(cs, Does.Contain("n * 0"), "-x * 0");
            Assert.That(cs, Does.Contain("x / x"), "x / x");
        });
    }

    [Test]
    public void Cpp_FloatIdentities_AreNotFolded()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(FloatProgram));
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("inf - inf"), "x - x");
            Assert.That(cpp, Does.Contain("inf * 0"), "x * 0");
            Assert.That(cpp, Does.Contain("n * 0"), "-x * 0");
            Assert.That(cpp, Does.Contain("x / x"), "x / x");
        });
    }

    /// <summary>The integral identities are sound and must still fire.</summary>
    [Test]
    public void IntegerIdentities_StillFold()
    {
        var js = JsTestSupport.CompileOptimized(IntegerProgram);
        var cs = ReturnCoercionTests.EmitCSharpForTest(IntegerProgram);
        Assert.Multiple(() =>
        {
            Assert.That(js, Does.Not.Contain("k - k"), "JS x - x");
            Assert.That(js, Does.Not.Contain("k * 0"), "JS x * 0");
            Assert.That(cs, Does.Not.Contain("k - k"), "C# x - x");
            Assert.That(cs, Does.Not.Contain("k * 0"), "C# x * 0");
        });
    }

    internal static string Program => FloatProgram;
    internal static string ExpectedOutput => Expected;
}

/// <summary>The same program, compiled and run on each backend.</summary>
[Category("Integration")]
[NonParallelizable] // the C# leg redirects Console.Out
public class PeepholeFloatIdentityRunTests
{
    [Test]
    public void CSharp_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(PeepholeFloatIdentityTests.Program)),
            Is.EqualTo(PeepholeFloatIdentityTests.ExpectedOutput));

    [Test]
    public void Cpp_Runs() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(PeepholeFloatIdentityTests.Program))),
            Is.EqualTo(PeepholeFloatIdentityTests.ExpectedOutput));

    /// <summary>Through <see cref="JsTestSupport.CompileOptimized"/> — <c>RunJs</c> skips the optimizer.</summary>
    [Test]
    public void JavaScript_Runs() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunNodeScript(
            JsTestSupport.CompileOptimized(PeepholeFloatIdentityTests.Program))),
            Is.EqualTo(PeepholeFloatIdentityTests.ExpectedOutput));
}
