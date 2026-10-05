using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.5, M4, scope call S4 (portable-controls Task 13) — inside <c>Overrides Property Text</c>, <c>MyBase.Text</c>
/// lowered to <c>this.Text</c> on JavaScript AND C#, calling the override itself: RangeError / StackOverflow at run time
/// from a green build. The IR carried the base only as the receiver's TYPE; <c>ThroughBase</c> now says "bypass virtual
/// dispatch".
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class MyBasePropertyExecutionTests
{
    private const string Program =
        "Public Class Control\n Private _text As String = \"\"\n Public Overridable Property Text As String\n" +
        "  Get\n   Return _text\n  End Get\n  Set(value As String)\n   _text = value\n  End Set\n End Property\nEnd Class\n" +
        "Public Class Button\n Inherits Control\n Public Overrides Property Text As String\n" +
        "  Get\n   Return \"B:\" & MyBase.Text\n  End Get\n  Set(value As String)\n   MyBase.Text = value & \"!\"\n  End Set\n End Property\nEnd Class\n" +
        "Public Class Fancy\n Inherits Button\n Public Overrides Property Text As String\n" +
        "  Get\n   Return \"F:\" & MyBase.Text\n  End Get\n  Set(value As String)\n   MyBase.Text = value\n  End Set\n End Property\nEnd Class\n" +
        "Sub Main()\n Dim b As New Button()\n b.Text = \"hi\"\n Console.WriteLine(b.Text)\n" +
        " Dim c As Control = New Fancy()\n c.Text = \"yo\"\n Console.WriteLine(c.Text)\nEnd Sub\n";

    private const string Expected = "B:hi!\nF:B:yo!";

    [Test] public void OnJavaScript() => Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(Program)), Is.EqualTo(Expected));
    [Test] public void OnJavaScript_Optimized() => Assert.That(FourBackends.Norm(JavaScriptOptimizedExecutionTests.RunOptimized(Program)), Is.EqualTo(Expected));
    [Test] public void OnCSharp() => Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected));
    [Test] public void OnCpp() => Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected));

    /// <summary>A FIELD through MyBase is the instance's field (not virtual) — it must stay <c>this.field</c>.</summary>
    [Test]
    public void AFieldThroughMyBase_IsStillTheInstancesField() =>
        Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(
            "Public Class A\n Public n As Integer = 5\nEnd Class\nPublic Class B\n Inherits A\n Public Function Get2() As Integer\n  Return MyBase.n * 2\n End Function\nEnd Class\n" +
            "Sub Main()\n Dim b As New B()\n Console.WriteLine(b.Get2())\nEnd Sub\n")), Is.EqualTo("10"));
}
