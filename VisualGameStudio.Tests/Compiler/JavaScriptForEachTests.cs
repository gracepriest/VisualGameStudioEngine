using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 14, final piece — <c>For Each</c>. Unblocked by array support (task 16), since
/// its Collection operand needs something to iterate.
///
/// <para><b>Shape.</b> Unlike every other loop, <c>IRForEach</c> is a single INSTRUCTION
/// sitting inside a block, not a terminator: it carries BodyBlock and EndBlock itself, there
/// is no condition block, no increment block and no back-edge. The enclosing block continues
/// past it.</para>
///
/// <para><b>The ambiguity.</b> A body's natural fall-through and an <c>Exit For</c> BOTH emit
/// <c>IRBranch(EndBlock)</c> — the same target. They are separated by POSITION: the body
/// block's own terminator is the fall-through, and a branch from any nested block is a real
/// exit. That leaves one degenerate case undecidable — a bare unconditional <c>Exit For</c> as
/// the body's last statement — which is a known limitation shared with the other backends and
/// recorded below rather than silently mis-lowered.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptForEachTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    private const string Filled =
        "Dim a(3) As Integer\na(0) = 1\na(1) = 2\na(2) = 3\n";

    [Test]
    public void ForEach_VisitsEveryElementInOrder()
        => Assert.That(Run(Filled + "For Each n As Integer In a\nConsole.WriteLine(n)\nNext"),
            Is.EqualTo("1\n2\n3"));

    [Test]
    public void ForEach_Accumulates()
        => Assert.That(Run(
            Filled +
            "Dim total As Integer\ntotal = 0\n" +
            "For Each n As Integer In a\ntotal = total + n\nNext\n" +
            "Console.WriteLine(total)"),
            Is.EqualTo("6"));

    /// <summary>Execution must continue after the loop, exactly once.</summary>
    [Test]
    public void ForEach_ContinuesAfterTheLoop()
        => Assert.That(Run(Filled + "For Each n As Integer In a\nConsole.WriteLine(n)\nNext\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("1\n2\n3\ndone"));

    /// <summary>
    /// The realistic Exit For: guarded, so the exit branch comes from a NESTED block and is
    /// distinguishable from the body's fall-through.
    /// </summary>
    [Test]
    public void ForEach_ExitFor_FromANestedBlock_Breaks()
        => Assert.That(Run(
            Filled +
            "For Each n As Integer In a\n" +
            "If n = 3 Then\nExit For\nEnd If\n" +
            "Console.WriteLine(n)\nNext\n" +
            "Console.WriteLine(\"after\")"),
            Is.EqualTo("1\n2\nafter"));

    /// <summary>An empty array iterates zero times rather than once.</summary>
    [Test]
    public void ForEach_OverAnEmptyRange_RunsZeroTimes()
        => Assert.That(Run(
            "Dim a(0) As Integer\nFor Each n As Integer In a\nConsole.WriteLine(n)\nNext\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("done"));

    [Test]
    public void ForEach_OverStrings()
        => Assert.That(Run(
            "Dim s(2) As String\ns(0) = \"a\"\ns(1) = \"b\"\n" +
            "For Each t As String In s\nConsole.WriteLine(t)\nNext"),
            Is.EqualTo("a\nb"));

    [Test]
    public void ForEach_NestedInsideAForLoop()
        => Assert.That(Run(
            "Dim a(2) As Integer\na(0) = 1\na(1) = 2\n" +
            "For i As Integer = 1 To 2\n" +
            "For Each n As Integer In a\nConsole.WriteLine(i * 10 + n)\nNext\n" +
            "Next"),
            Is.EqualTo("11\n12\n21\n22"));
}
