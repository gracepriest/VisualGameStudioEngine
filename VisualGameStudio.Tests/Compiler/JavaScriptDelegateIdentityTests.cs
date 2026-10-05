using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §4.4, M2 (portable-controls Task 12) — <c>RemoveHandler b.Clicked, AddressOf Me.H</c> removed nothing on
/// JavaScript: each <c>AddressOf</c> was a fresh <c>.bind(recv)</c> function, and the event a <c>Set</c>, so
/// <c>delete</c> never found it. .NET's rules, now the page's: an invocation LIST (the same handler twice fires twice),
/// RemoveHandler takes the LAST matching entry, a delegate to an instance method is equal to another to the same
/// (instance, method), and a raise invokes the list as it was when the raise began. The same program runs on C# as the
/// oracle.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class JavaScriptDelegateIdentityTests
{
    private static void Same(string program, string expected)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FourBackends.Norm(JavaScriptExecutionTests.RunJs(program)), Is.EqualTo(expected), "JavaScript");
            Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(program)), Is.EqualTo(expected), "C# (the oracle)");
        });
    }

    private const string Btn =
        "Public Class Btn\n Public Event Clicked(n As Integer)\n Private count As Integer\n" +
        " Public Sub Press()\n  count = count + 1\n  RaiseEvent Clicked(count)\n End Sub\nEnd Class\n";

    [Test]
    public void AddedTwice_RemovedOnce_OneRemains() => Same(Btn +
        "Public Class Listener\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n" +
        "  AddHandler b.Clicked, AddressOf Me.H\n  AddHandler b.Clicked, AddressOf Me.H\n  b.Press()\n" +
        "  RemoveHandler b.Clicked, AddressOf Me.H\n  b.Press()\n  RemoveHandler b.Clicked, AddressOf Me.H\n  b.Press()\n" +
        "  Console.WriteLine(\"done\")\n End Sub\n Private Sub H(n As Integer)\n  Console.WriteLine(\"h \" & n)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim l As New Listener()\nEnd Sub\n",
        "h 1\nh 1\nh 2\ndone");

    [Test]
    public void ADelegateToAnotherInstance_IsNotRemoved() => Same(Btn +
        "Public Class Who\n Private name As String\n Public Sub New(n As String)\n  name = n\n End Sub\n" +
        " Public Sub H(n As Integer)\n  Console.WriteLine(name & \" \" & n)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim b As New Btn()\n Dim a As New Who(\"a\")\n Dim c As New Who(\"c\")\n" +
        " AddHandler b.Clicked, AddressOf a.H\n RemoveHandler b.Clicked, AddressOf c.H\n b.Press()\nEnd Sub\n",
        "a 1");

    [Test]
    public void AFreeFunction_AddsAndRemoves() => Same(Btn +
        "Sub F(n As Integer)\n Console.WriteLine(\"f \" & n)\nEnd Sub\n" +
        "Sub Main()\n Dim b As New Btn()\n AddHandler b.Clicked, AddressOf F\n b.Press()\n RemoveHandler b.Clicked, AddressOf F\n b.Press()\n Console.WriteLine(\"done\")\nEnd Sub\n",
        "f 1\ndone");

    [Test]
    public void RemovingANeverAddedHandler_ChangesNothing() => Same(Btn +
        "Sub F(n As Integer)\n Console.WriteLine(\"f \" & n)\nEnd Sub\nSub G(n As Integer)\n Console.WriteLine(\"g \" & n)\nEnd Sub\n" +
        "Sub Main()\n Dim b As New Btn()\n AddHandler b.Clicked, AddressOf F\n RemoveHandler b.Clicked, AddressOf G\n b.Press()\nEnd Sub\n",
        "f 1");

    /// <summary>A lambda is removed only by the SAME delegate value (as in .NET); a second, textually equal lambda is a
    /// different delegate and removes nothing.</summary>
    [Test]
    public void ALambda_IsRemovedOnlyByTheSameDelegate() => Same(Btn +
        "Sub Main()\n Dim b As New Btn()\n Dim h As Action(Of Integer) = Sub(n As Integer) Console.WriteLine(\"l \" & n)\n" +
        " AddHandler b.Clicked, h\n RemoveHandler b.Clicked, Sub(n As Integer) Console.WriteLine(\"l \" & n)\n b.Press()\n" +
        " RemoveHandler b.Clicked, h\n b.Press()\n Console.WriteLine(\"done\")\nEnd Sub\n",
        "l 1\ndone");

    /// <summary>A handler that subscribes another during a raise: the new one runs from the NEXT raise.</summary>
    [Test]
    public void ARaise_InvokesTheListAsItWas() => Same(Btn +
        "Public Class Grow\n Private b As Btn\n Public Sub New()\n  b = New Btn()\n  AddHandler b.Clicked, AddressOf Me.First\n" +
        "  b.Press()\n  b.Press()\n End Sub\n" +
        " Private Sub First(n As Integer)\n  Console.WriteLine(\"first \" & n)\n  If n = 1 Then AddHandler b.Clicked, AddressOf Me.Second\n End Sub\n" +
        " Private Sub Second(n As Integer)\n  Console.WriteLine(\"second \" & n)\n End Sub\nEnd Class\n" +
        "Sub Main()\n Dim g As New Grow()\nEnd Sub\n",
        "first 1\nfirst 2\nsecond 2");
}
