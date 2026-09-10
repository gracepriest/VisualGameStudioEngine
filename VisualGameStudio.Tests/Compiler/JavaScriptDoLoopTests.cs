using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>Do … Loop</c> in all four shapes — pre-test / post-test × While / Until — plus the
/// condition-less <c>Do … Loop</c> and <c>Exit Do</c>.
///
/// <para><b>MEASURED before the fix, through the CLI (the shipping route):</b></para>
/// <code>
///   Do While c … Loop       0 1 2 done      correct
///   Do Until c … Loop       0               ⛔ RAN AND PRINTED WRONG OUTPUT
///   Do … Loop While c       refused          NotYet("post-test Do…Loop")
///   Do … Loop               refused          NotYet("a loop header with no conditional terminator")
/// </code>
///
/// <para>⛔ <b>The Until case is the dangerous one.</b> IRBuilder lowers <c>Until</c> by SWAPPING
/// the conditional branch's targets — <c>condbr c, end, body</c> instead of
/// <c>condbr c, body, end</c> — and <c>EmitLoop</c> took <c>TrueTarget</c> to be the body
/// unconditionally. So it emitted the CONTINUATION inside the loop and the loop's real body
/// after it:</para>
/// <code>
///   while (true) {
///       const t0 = (i >= 3);
///       if (!t0) break;
///       console.log("done");     // the continuation, inside the loop
///       return;
///   }
///   console.log(i);              // the body, after the loop
/// </code>
/// <para>That compiled cleanly and ran. It is exactly the wrong-but-running class this backend
/// exists to refuse, and no text assertion could tell it from the correct output — only stdout
/// can, which is why every test here executes.</para>
///
/// <para><b>Post-test shape.</b> The IR branches into the BODY first and the condition block
/// follows it, so by the time the structured walk reaches the header the body is already
/// emitted. The lowering is <c>while (true) { body; condition-instructions; if (!c) break; }</c>
/// — NOT <c>do { … } while (c)</c>, because the condition's instructions (its temps) live in
/// the header block and must run right before the test: inside a <c>do</c> body they would be
/// block-scoped <c>const</c>s invisible to the <c>while (c)</c> clause.</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptDoLoopTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    private static string RunOptimized(string body) =>
        JavaScriptOptimizedExecutionTests.RunOptimized($"Sub Main()\n{body}\nEnd Sub");

    private const string CountUp = "Dim i As Integer\ni = 0\n";

    // ---------------------------------------------------------------- pre-test

    [Test]
    public void DoWhile_PreTest_RunsWhileTrue()
        => Assert.That(Run(CountUp + "Do While i < 3\nConsole.WriteLine(i)\ni = i + 1\nLoop\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\n2\ndone"));

    /// <summary>THE miscompile: printed <c>0</c> and stopped, from a build that succeeded.</summary>
    [Test]
    public void DoUntil_PreTest_RunsUntilTrue()
        => Assert.That(Run(CountUp + "Do Until i >= 3\nConsole.WriteLine(i)\ni = i + 1\nLoop\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\n2\ndone"),
            "the Until form swaps the branch targets; a lowering that assumes TrueTarget is the " +
            "body puts the continuation INSIDE the loop");

    /// <summary>
    /// Pins the zero-iteration semantics. ⚠ It does NOT discriminate the swapped-arm miscompile
    /// (the old lowering printed "done" here too — the continuation ran inside the loop and
    /// returned); the sibling <see cref="DoUntil_PreTest_RunsUntilTrue"/> is the one that does.
    /// </summary>
    [Test]
    public void DoUntil_PreTest_TrueAtEntry_RunsZeroTimes()
        => Assert.That(Run(CountUp + "Do Until i = 0\nConsole.WriteLine(\"never\")\nLoop\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("done"));

    [Test]
    public void DoWhile_PreTest_FalseAtEntry_RunsZeroTimes()
        => Assert.That(Run(CountUp + "Do While i > 0\nConsole.WriteLine(\"never\")\nLoop\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("done"));

    // ---------------------------------------------------------------- post-test

    [Test]
    public void DoLoopWhile_PostTest_RunsWhileTrue()
        => Assert.That(Run(CountUp + "Do\nConsole.WriteLine(i)\ni = i + 1\nLoop While i < 3\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\n2\ndone"));

    [Test]
    public void DoLoopUntil_PostTest_RunsUntilTrue()
        => Assert.That(Run(CountUp + "Do\nConsole.WriteLine(i)\ni = i + 1\nLoop Until i >= 3\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\n2\ndone"));

    /// <summary>
    /// The defining property of a post-test loop: the body runs ONCE even when the condition
    /// is false from the start. A lowering that hoists the test above the body gets this wrong
    /// while passing every count-up case above.
    /// </summary>
    [Test]
    public void DoLoopWhile_PostTest_FalseAtEntry_StillRunsOnce()
        => Assert.That(Run(CountUp + "Do\nConsole.WriteLine(\"once\")\nLoop While i > 0\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("once\ndone"));

    [Test]
    public void DoLoopUntil_PostTest_TrueAtEntry_StillRunsOnce()
        => Assert.That(Run(CountUp + "Do\nConsole.WriteLine(\"once\")\nLoop Until i = 0\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("once\ndone"));

    /// <summary>
    /// The condition's INSTRUCTIONS live in the header block and must run every iteration,
    /// after the body. A function call in the condition makes that observable: it must print
    /// exactly once per iteration, after that iteration's body.
    /// </summary>
    [Test]
    public void PostTest_ConditionIsReEvaluatedAfterEveryIteration()
        => Assert.That(JavaScriptExecutionTests.RunJs(
            "Function Tick(v As Integer) As Integer\nConsole.WriteLine(\"tick\")\nReturn v\nEnd Function\n" +
            "Sub Main()\nDim i As Integer\ni = 0\n" +
            "Do\nConsole.WriteLine(\"body\")\ni = i + 1\nLoop While Tick(i) < 3\n" +
            "Console.WriteLine(\"done\")\nEnd Sub"),
            Is.EqualTo("body\ntick\nbody\ntick\nbody\ntick\ndone"));

    // ---------------------------------------------------------------- Do … Loop (no condition)

    /// <summary>A condition-less loop's header is an unconditional back-edge; the only way out is Exit Do.</summary>
    [Test]
    public void DoLoop_NoCondition_ExitsWithExitDo()
        => Assert.That(Run(CountUp + "Do\nConsole.WriteLine(i)\ni = i + 1\nIf i = 3 Then\nExit Do\nEnd If\nLoop\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\n2\ndone"));

    // ---------------------------------------------------------------- Exit Do

    [Test]
    public void ExitDo_InPostTestLoop_BreaksOut()
        => Assert.That(Run(CountUp + "Do\nIf i = 2 Then\nExit Do\nEnd If\nConsole.WriteLine(i)\ni = i + 1\nLoop While i < 10\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\ndone"));

    [Test]
    public void ExitDo_InPreTestUntilLoop_BreaksOut()
        => Assert.That(Run(CountUp + "Do Until i >= 10\nIf i = 2 Then\nExit Do\nEnd If\nConsole.WriteLine(i)\ni = i + 1\nLoop\nConsole.WriteLine(\"done\")"),
            Is.EqualTo("0\n1\ndone"));

    // ---------------------------------------------------------------- nesting

    /// <summary>A post-test loop inside a For: the inner loop's blocks must not be mistaken for the outer's.</summary>
    [Test]
    public void PostTestLoop_InsideFor_RunsPerOuterIteration()
        => Assert.That(Run(
            "For k As Integer = 1 To 2\n" +
            "Dim j As Integer\nj = 0\n" +
            "Do\nConsole.WriteLine(k * 10 + j)\nj = j + 1\nLoop While j < 2\n" +
            "Next"),
            Is.EqualTo("10\n11\n20\n21"));

    [Test]
    public void TwoSiblingPostTestLoops_RunIndependently()
        => Assert.That(Run(
            "Dim a As Integer\na = 0\nDo\nConsole.WriteLine(a)\na = a + 1\nLoop While a < 2\n" +
            "Dim b As Integer\nb = 5\nDo\nConsole.WriteLine(b)\nb = b + 1\nLoop Until b >= 7"),
            Is.EqualTo("0\n1\n5\n6"));

    [Test]
    public void IfInsidePostTestLoop_TakesBothArmsAcrossIterations()
        => Assert.That(Run(CountUp +
            "Do\nIf i Mod 2 = 0 Then\nConsole.WriteLine(\"even\")\nElse\nConsole.WriteLine(\"odd\")\nEnd If\ni = i + 1\nLoop While i < 4"),
            Is.EqualTo("even\nodd\neven\nodd"));

    // ---------------------------------------------------------------- the SHIPPING IR
    //
    // Every shipping route runs the standard passes; RunJs runs none. Loops are exactly what
    // copy propagation and dead-code elimination reshape.

    [TestCase("Do While i < 3\nConsole.WriteLine(i)\ni = i + 1\nLoop", "0\n1\n2")]
    [TestCase("Do Until i >= 3\nConsole.WriteLine(i)\ni = i + 1\nLoop", "0\n1\n2")]
    [TestCase("Do\nConsole.WriteLine(i)\ni = i + 1\nLoop While i < 3", "0\n1\n2")]
    [TestCase("Do\nConsole.WriteLine(i)\ni = i + 1\nLoop Until i >= 3", "0\n1\n2")]
    [TestCase("Do\nConsole.WriteLine(\"once\")\nLoop While i > 0", "once")]
    public void Optimized_DoLoops_StillRunCorrectly(string loop, string expected)
        => Assert.That(RunOptimized(CountUp + loop), Is.EqualTo(expected));
}
