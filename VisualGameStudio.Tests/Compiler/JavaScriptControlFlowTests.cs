using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 14 — control flow.
///
/// <para><b>Why this needs a structured emitter, not a block walk.</b> BasicLang IR is a
/// goto-style CFG and JavaScript has no <c>goto</c>. Worse, <c>IRFunction.Blocks</c> is in
/// CREATION order, which is not execution order: <c>if0.end</c> is created before
/// <c>if0.elseif0.then</c>, and <c>switch0.default</c> before any case block. So a linear walk
/// emits the merge block before the branch bodies, and back-edges have no linear rendering at
/// all. Emission must start at <c>EntryBlock</c> and follow terminators.</para>
///
/// <para><b>Everything here is an execution test.</b> Control flow that emits plausible-looking
/// JavaScript and iterates the wrong number of times is exactly the silent-wrong-output class
/// this backend exists to refuse — and a text assertion cannot tell the two apart.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptControlFlowTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    // ---------------------------------------------------------------- If

    [Test]
    public void If_TakesTheThenBranch()
        => Assert.That(Run("If 1 < 2 Then\nConsole.WriteLine(\"yes\")\nEnd If"), Is.EqualTo("yes"));

    [Test]
    public void If_SkipsTheThenBranch_WhenFalse()
        => Assert.That(Run("If 2 < 1 Then\nConsole.WriteLine(\"no\")\nEnd If\nConsole.WriteLine(\"after\")"),
            Is.EqualTo("after"));

    [Test]
    public void IfElse_TakesTheElseBranch()
        => Assert.That(Run("If 2 < 1 Then\nConsole.WriteLine(\"a\")\nElse\nConsole.WriteLine(\"b\")\nEnd If"),
            Is.EqualTo("b"));

    /// <summary>
    /// ElseIf is the shape that breaks a naive emitter twice over: its blocks are created
    /// AFTER the merge block, and the name <c>if0.elseif0.then</c> CONTAINS <c>.else</c> —
    /// so a detector testing <c>.else</c> before <c>.then</c> misfires on every ElseIf.
    /// </summary>
    [TestCase("1", "one")]
    [TestCase("2", "two")]
    [TestCase("3", "other")]
    public void ElseIf_SelectsTheRightBranch(string value, string expected)
    {
        var output = Run(
            $"Dim n As Integer\nn = {value}\n" +
            "If n = 1 Then\nConsole.WriteLine(\"one\")\n" +
            "ElseIf n = 2 Then\nConsole.WriteLine(\"two\")\n" +
            "Else\nConsole.WriteLine(\"other\")\nEnd If");

        Assert.That(output, Is.EqualTo(expected));
    }

    /// <summary>Code after an If must run exactly once, whichever branch was taken.</summary>
    [Test]
    public void If_ContinuesAfterTheMergeBlock_Once()
    {
        var output = Run(
            "If 1 < 2 Then\nConsole.WriteLine(\"in\")\nEnd If\nConsole.WriteLine(\"out\")");

        Assert.That(output, Is.EqualTo("in\nout"));
    }

    // ---------------------------------------------------------------- While

    [Test]
    public void While_RunsTheExpectedNumberOfTimes()
    {
        var output = Run(
            "Dim i As Integer\ni = 0\n" +
            "While i < 3\nConsole.WriteLine(i)\ni = i + 1\nEnd While");

        Assert.That(output, Is.EqualTo("0\n1\n2"));
    }

    /// <summary>
    /// A pre-test loop whose condition is false at entry must run ZERO times. This is the
    /// case that catches a body emitted before the loop.
    /// </summary>
    [Test]
    public void While_FalseAtEntry_RunsZeroTimes()
        => Assert.That(Run("Dim i As Integer\ni = 10\nWhile i < 3\nConsole.WriteLine(\"x\")\ni = i + 1\nEnd While\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("done"));

    // ---------------------------------------------------------------- For

    [Test]
    public void For_SumsOneToTen()
    {
        var output = Run(
            "Dim total As Integer\ntotal = 0\n" +
            "For i As Integer = 1 To 10\ntotal = total + i\nNext\n" +
            "Console.WriteLine(total)");

        Assert.That(output, Is.EqualTo("55"));
    }

    /// <summary>The bound is INCLUSIVE in BasicLang; an exclusive lowering loses one iteration.</summary>
    [Test]
    public void For_UpperBoundIsInclusive()
        => Assert.That(Run("For i As Integer = 1 To 3\nConsole.WriteLine(i)\nNext"), Is.EqualTo("1\n2\n3"));

    /// <summary>
    /// A negative Step flips the comparison from &lt;= to &gt;=. Emitting the wrong one makes
    /// the loop run zero times rather than counting down.
    /// </summary>
    [Test]
    public void For_NegativeStep_CountsDown()
        => Assert.That(Run("For i As Integer = 3 To 1 Step -1\nConsole.WriteLine(i)\nNext"),
            Is.EqualTo("3\n2\n1"));

    // ---------------------------------------------------------------- Exit

    /// <summary>
    /// `Exit For` emits a plain IRBranch to the loop's `.end` block — INDISTINGUISHABLE from
    /// an ordinary fall-through except by target block IDENTITY. Detection must key on
    /// identity, never on the `.end` name: two sibling loops both produce `.end` blocks.
    /// </summary>
    [Test]
    public void ExitFor_BreaksOutOfTheLoop()
    {
        var output = Run(
            "For i As Integer = 1 To 10\n" +
            "If i = 3 Then\nExit For\nEnd If\n" +
            "Console.WriteLine(i)\nNext\n" +
            "Console.WriteLine(\"after\")");

        Assert.That(output, Is.EqualTo("1\n2\nafter"));
    }

    [Test]
    public void ExitWhile_BreaksOutOfTheLoop()
    {
        var output = Run(
            "Dim i As Integer\ni = 0\n" +
            "While i < 10\n" +
            "If i = 2 Then\nExit While\nEnd If\n" +
            "Console.WriteLine(i)\ni = i + 1\nEnd While\n" +
            "Console.WriteLine(\"after\")");

        Assert.That(output, Is.EqualTo("0\n1\nafter"));
    }

    // ---------------------------------------------------------------- nesting

    /// <summary>
    /// Two sibling loops each create their own `.end` block. A break keyed on the NAME
    /// `.end` cannot tell them apart; only identity can.
    /// </summary>
    [Test]
    public void TwoSiblingLoops_EachRunIndependently()
    {
        var output = Run(
            "For i As Integer = 1 To 2\nConsole.WriteLine(i)\nNext\n" +
            "For j As Integer = 5 To 6\nConsole.WriteLine(j)\nNext");

        Assert.That(output, Is.EqualTo("1\n2\n5\n6"));
    }

    [Test]
    public void NestedLoops_ProduceTheCartesianProduct()
    {
        var output = Run(
            "For i As Integer = 1 To 2\n" +
            "For j As Integer = 1 To 2\n" +
            "Console.WriteLine(i * 10 + j)\n" +
            "Next\nNext");

        Assert.That(output, Is.EqualTo("11\n12\n21\n22"));
    }

    /// <summary>An inner Exit For must break only the INNER loop.</summary>
    [Test]
    public void ExitFor_InNestedLoop_BreaksOnlyTheInnerLoop()
    {
        var output = Run(
            "For i As Integer = 1 To 2\n" +
            "For j As Integer = 1 To 9\n" +
            "If j = 2 Then\nExit For\nEnd If\n" +
            "Console.WriteLine(i * 10 + j)\n" +
            "Next\nNext");

        Assert.That(output, Is.EqualTo("11\n21"));
    }

    [Test]
    public void IfInsideLoop_Alternates()
    {
        var output = Run(
            "For i As Integer = 1 To 4\n" +
            "If i Mod 2 = 0 Then\nConsole.WriteLine(\"even\")\nElse\nConsole.WriteLine(\"odd\")\nEnd If\n" +
            "Next");

        Assert.That(output, Is.EqualTo("odd\neven\nodd\neven"));
    }
}
