using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 20 — lambdas and closures.
///
/// <para><b>They must be emitted INLINE as arrow functions, at the use site.</b> The
/// tempting alternative — hoist <c>__lambda_N</c> to a top-level function and pass its
/// captures as parameters — cannot work here: <c>IRFunction.CapturedVariables</c> is ALWAYS
/// EMPTY (it is populated from a list IRBuilder never fills), so the hoisted body would
/// reference every captured variable as a FREE identifier. That is a ReferenceError at
/// runtime from a build that reported success.</para>
///
/// <para>Emitting inline makes JavaScript's own lexical scope supply the capture, which is
/// also why the closure tests below are the ones that matter: they are the only thing that
/// distinguishes a real closure from a function that merely compiles.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptLambdaTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    private static string InMain(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    [Test]
    public void Lambda_IsCallable()
        => Assert.That(InMain(
            "Dim add = Function(a As Integer, b As Integer) a + b\n" +
            "Console.WriteLine(add(2, 3))"),
            Is.EqualTo("5"));

    [Test]
    public void Lambda_WithNoParameters()
        => Assert.That(InMain(
            "Dim get5 = Function() 5\nConsole.WriteLine(get5())"),
            Is.EqualTo("5"));

    /// <summary>
    /// THE closure test. The lambda must see the enclosing local — which only works if the
    /// arrow function is emitted where that local is in scope.
    /// </summary>
    [Test]
    public void Lambda_CapturesAnEnclosingLocal()
        => Assert.That(InMain(
            "Dim factor As Integer\nfactor = 10\n" +
            "Dim scale = Function(x As Integer) x * factor\n" +
            "Console.WriteLine(scale(3))"),
            Is.EqualTo("30"));

    /// <summary>
    /// A capture is BY REFERENCE to the variable, not by value at creation — changing the
    /// captured local afterwards must be visible inside the lambda.
    /// </summary>
    [Test]
    public void Lambda_SeesLaterChangesToACapturedLocal()
        => Assert.That(InMain(
            "Dim n As Integer\nn = 1\n" +
            "Dim read = Function() n\n" +
            "n = 99\n" +
            "Console.WriteLine(read())"),
            Is.EqualTo("99"));

    /// <summary>Two lambdas created in the same scope must not share a body.</summary>
    [Test]
    public void TwoLambdas_AreIndependent()
        => Assert.That(InMain(
            "Dim inc = Function(x As Integer) x + 1\n" +
            "Dim dbl = Function(x As Integer) x * 2\n" +
            "Console.WriteLine(inc(5))\nConsole.WriteLine(dbl(5))"),
            Is.EqualTo("6\n10"));

    [Test]
    public void Lambda_CapturesAParameter()
        => Assert.That(Run(
            "Function MakeAdder(n As Integer) As Integer\n" +
            "Dim f = Function(x As Integer) x + n\n" +
            "Return f(1)\n" +
            "End Function\n" +
            "Sub Main()\nConsole.WriteLine(MakeAdder(10))\nEnd Sub"),
            Is.EqualTo("11"));

    [Test]
    public void Lambda_UsedInsideALoop()
        => Assert.That(InMain(
            "Dim total As Integer\ntotal = 0\n" +
            "Dim add = Function(x As Integer) x * 2\n" +
            "For i As Integer = 1 To 3\ntotal = total + add(i)\nNext\n" +
            "Console.WriteLine(total)"),
            Is.EqualTo("12"));
}
