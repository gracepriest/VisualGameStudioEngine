using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 22 — iterators and <c>Yield</c>.
///
/// <para>The third feature the spec says JavaScript gets for free: <c>Iterator Function</c>
/// becomes a native generator (<c>function*</c>) and <c>Yield</c> becomes <c>yield</c>, where
/// the C++ backend hand-builds C++20 coroutines to achieve the same thing.</para>
///
/// <para><b>What makes these tests worth writing.</b> A generator that produces the right
/// values EAGERLY is indistinguishable from a lazy one by output alone — so laziness is
/// asserted directly, by printing from inside the iterator and observing the interleaving.
/// That is the property a non-generator lowering (build an array, return it) would lose while
/// still passing every value-based test.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptIteratorTests
{
    private static string Run(string source) => JavaScriptExecutionTests.RunJs(source);

    [Test]
    public void Iterator_YieldsItsValuesInOrder()
        => Assert.That(Run(
            "Iterator Function Numbers() As IEnumerable(Of Integer)\n" +
            "Yield 1\nYield 2\nYield 3\n" +
            "End Function\n" +
            "Sub Main()\nFor Each n As Integer In Numbers()\nConsole.WriteLine(n)\nNext\nEnd Sub"),
            Is.EqualTo("1\n2\n3"));

    /// <summary>
    /// THE laziness test. If the iterator ran to completion before the loop body started,
    /// the output would be "gen 1 / gen 2 / got 1 / got 2" instead of interleaved.
    /// </summary>
    [Test]
    public void Iterator_IsLazy_NotEagerlyMaterialised()
        => Assert.That(Run(
            "Iterator Function Numbers() As IEnumerable(Of Integer)\n" +
            "Console.WriteLine(\"gen 1\")\nYield 1\n" +
            "Console.WriteLine(\"gen 2\")\nYield 2\n" +
            "End Function\n" +
            "Sub Main()\n" +
            "For Each n As Integer In Numbers()\nConsole.WriteLine(\"got \" & n)\nNext\n" +
            "End Sub"),
            Is.EqualTo("gen 1\ngot 1\ngen 2\ngot 2"));

    [Test]
    public void Iterator_YieldingInsideALoop()
        => Assert.That(Run(
            "Iterator Function UpTo(n As Integer) As IEnumerable(Of Integer)\n" +
            "For i As Integer = 1 To n\nYield i\nNext\n" +
            "End Function\n" +
            "Sub Main()\nFor Each v As Integer In UpTo(4)\nConsole.WriteLine(v)\nNext\nEnd Sub"),
            Is.EqualTo("1\n2\n3\n4"));

    [Test]
    public void Iterator_YieldingNothing_ProducesNoIterations()
        => Assert.That(Run(
            "Iterator Function Empty() As IEnumerable(Of Integer)\n" +
            "End Function\n" +
            "Sub Main()\n" +
            "For Each v As Integer In Empty()\nConsole.WriteLine(v)\nNext\n" +
            "Console.WriteLine(\"done\")\nEnd Sub"),
            Is.EqualTo("done"));

    /// <summary>A conditional Yield must skip the values it is meant to skip.</summary>
    [Test]
    public void Iterator_ConditionalYield()
        => Assert.That(Run(
            "Iterator Function Evens(n As Integer) As IEnumerable(Of Integer)\n" +
            "For i As Integer = 1 To n\n" +
            "If i Mod 2 = 0 Then\nYield i\nEnd If\n" +
            "Next\n" +
            "End Function\n" +
            "Sub Main()\nFor Each v As Integer In Evens(6)\nConsole.WriteLine(v)\nNext\nEnd Sub"),
            Is.EqualTo("2\n4\n6"));
}
