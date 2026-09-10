using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A lambda inside a class method — <c>items.ForEach(Sub(x As Integer) Total = Total + x)</c>.
///
/// <para>⛔ <b>The defect is in IRBuilder, and every backend inherits it.</b>
/// <c>Visit(ClassNode)</c> bound a method's implementation with
/// <c>Implementation = _module.Functions.LastOrDefault()</c> AFTER visiting the member —
/// but <c>Visit(FunctionNode)</c> registers the method's own IRFunction FIRST and then visits
/// the body, so every lambda in the body is appended after it, and <c>LastOrDefault</c>
/// returns the LAST LAMBDA. The class's method was then the lambda's body: the method
/// <c>AddAll</c> became <c>Total = Total + x</c> with the real body gone. The JavaScript
/// backend refused the shape (<c>impl.IsLambda</c> → NotYet), which is how it was found; the
/// C# backend emitted it. Constructors were bound the same way.</para>
///
/// <para>The fix binds by INDEX — the position <c>module.Functions</c> had before the visit,
/// which is where <c>CreateFunction</c> puts the member — so lambdas appended after it are
/// irrelevant. Property accessors were already bound by name and never had the problem.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node (and the C# leg compiles with dotnet)
public class JavaScriptMethodLambdaTests
{
    private const string Program =
        "Class Counter\nPublic Total As Integer\n" +
        "Public Sub AddAll(items As List(Of Integer))\n" +
        "items.ForEach(Sub(x As Integer) Total = Total + x)\nConsole.WriteLine(\"added\")\nEnd Sub\n" +
        "Public Sub PrintDoubled(items As List(Of Integer))\n" +
        "For Each d In items.Select(Function(x As Integer) x * 2)\nConsole.WriteLine(d)\nNext\nEnd Sub\n" +
        "End Class\n" +
        "Sub Main()\nDim c As New Counter()\nDim xs As New List(Of Integer)()\n" +
        "xs.Add(1)\nxs.Add(2)\nxs.Add(3)\n" +
        "c.AddAll(xs)\nConsole.WriteLine(c.Total)\n" +
        "c.PrintDoubled(xs)\nEnd Sub";

    private const string Expected = "added\n6\n2\n4\n6";

    /// <summary>The method body is the METHOD's, the lambda captures <c>this</c>, and the Function-lambda maps.</summary>
    [Test]
    public void LambdaInsideAMethod_TheMethodKeepsItsOwnBody_JavaScript()
        => Assert.That(JavaScriptExecutionTests.RunJs(Program), Is.EqualTo(Expected));

    /// <summary>
    /// The mirror: the same shape on the C# backend, which EMITTED the broken binding rather
    /// than refusing it. This is the leg that proves the fix is in the IR.
    ///
    /// <para>Only the AddAll half: <c>For Each d In items.Select(…)</c> inside a class method
    /// hits a separate, pre-existing C# backend defect (the Select result is emitted as a bare
    /// statement and the foreach reads an unbound <c>t0</c> — CS0103), which is not what this
    /// test is about.</para>
    /// </summary>
    [Test]
    public void LambdaInsideAMethod_TheMethodKeepsItsOwnBody_CSharp()
        => Assert.That(CliTestHarness.CompileRunCSharp(
                "Class Counter\nPublic Total As Integer\n" +
                "Public Sub AddAll(items As List(Of Integer))\n" +
                "items.ForEach(Sub(x As Integer) Total = Total + x)\nConsole.WriteLine(\"added\")\nEnd Sub\n" +
                "End Class\n" +
                "Sub Main()\nDim c As New Counter()\nDim xs As New List(Of Integer)()\n" +
                "xs.Add(1)\nxs.Add(2)\nxs.Add(3)\n" +
                "c.AddAll(xs)\nConsole.WriteLine(c.Total)\nEnd Sub").Replace("\r\n", "\n").Trim(),
            Is.EqualTo("added\n6"));

    [Test]
    public void LambdaInsideAConstructor_TheConstructorKeepsItsOwnBody()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Class Summer\nPublic Total As Integer\n" +
            "Public Sub New(items As List(Of Integer))\n" +
            "items.ForEach(Sub(x As Integer) Total = Total + x)\nConsole.WriteLine(\"built\")\nEnd Sub\n" +
            "End Class\n" +
            "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(4)\nxs.Add(5)\n" +
            "Dim s As New Summer(xs)\nConsole.WriteLine(s.Total)\nEnd Sub"),
            Is.EqualTo("built\n9"));

    [Test]
    public void TwoLambdasInOneMethod_BothRunAndTheBodySurvives()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Class Stats\nPublic Sum As Integer\nPublic Count As Integer\n" +
            "Public Sub Take(items As List(Of Integer))\n" +
            "items.ForEach(Sub(x As Integer) Sum = Sum + x)\n" +
            "items.ForEach(Sub(x As Integer) Count = Count + 1)\n" +
            "Console.WriteLine(\"taken\")\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(2)\nxs.Add(3)\n" +
            "Dim s As New Stats()\ns.Take(xs)\nConsole.WriteLine(s.Sum)\nConsole.WriteLine(s.Count)\nEnd Sub"),
            Is.EqualTo("taken\n5\n2"));

    /// <summary>A method AFTER one containing a lambda must still be bound to its own body.</summary>
    [Test]
    public void AMethodDeclaredAfterALambdaMethod_IsBoundToItself()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Class Pair\nPublic Total As Integer\n" +
            "Public Sub First(items As List(Of Integer))\nitems.ForEach(Sub(x As Integer) Total = Total + x)\nEnd Sub\n" +
            "Public Sub Second()\nConsole.WriteLine(\"second\")\nEnd Sub\nEnd Class\n" +
            "Sub Main()\nDim xs As New List(Of Integer)()\nxs.Add(1)\n" +
            "Dim p As New Pair()\np.First(xs)\np.Second()\nConsole.WriteLine(p.Total)\nEnd Sub"),
            Is.EqualTo("second\n1"));

    [Test]
    public void Optimized_LambdaInsideAMethod()
        => Assert.That(JavaScriptOptimizedExecutionTests.RunOptimized(Program), Is.EqualTo(Expected));
}
