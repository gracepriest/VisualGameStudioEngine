using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>AndAlso</c>/<c>OrElse</c> actually SHORT-CIRCUIT, and <c>And</c>/<c>Or</c> actually do
/// not — chip task_c8db4a58, which was filed as "the C++ half" and is wider than that.
///
/// <para><b>MEASURED before the fix.</b> With <c>L</c>/<c>R</c> printing their names:</para>
/// <code>
///                     AndAlso(False)   OrElse(True)        And(False)
///   C#  (correct)     L                L, taken            L, R
///   C++               L, R             L, R, taken         L, R
///   JavaScript        L, R             L, R, taken         L, R
/// </code>
///
/// <para>⛔ <b>THE ROOT CAUSE IS THE IR, AND C# IS ONLY ACCIDENTALLY RIGHT.</b> IRBuilder
/// lowered <c>AndAlso</c>/<c>OrElse</c> as an ordinary binary operation, pre-evaluating BOTH
/// operands into temps:</para>
/// <code>
///   const t1 = L(false);
///   const t2 = R(true);      // evaluated unconditionally
///   const t3 = (t1 &amp;&amp; t2);   // the &amp;&amp; is decorative — both already ran
/// </code>
/// <para>Short-circuiting was destroyed upstream, so every backend that emits the IR
/// faithfully inherits the bug. The C# backend renders operand trees INLINE, so it emits
/// <c>L(false) &amp;&amp; R(true)</c> as one expression and C#'s own <c>&amp;&amp;</c> saves it —
/// the right answer for the wrong reason. That is exactly why "the C# backend is green" is not
/// evidence the IR is correct.</para>
///
/// <para>⛔ These RUN. The operators, the temps and the branch are all EMITTED correctly in
/// every backend; only the observable side effects differ. No emission assertion can see this.</para>
///
/// <para>⚠ Do not "fix" this by making a backend emit the operands inline to match C#. That
/// changes which backends AGREE without making any of them correct, and nothing else asserts
/// evaluation order, so it would ship green.</para>
///
/// <para><b>⛔ STATUS: the four short-circuit cases are [Ignore]d because the bug is OPEN.</b>
/// Remove the attribute to reproduce it in one command. Everything else here is ACTIVE and
/// guarding: C# (which is correct), the <c>And</c>/<c>Or</c> non-short-circuit cases, and the
/// value-when-both-run cases.</para>
///
/// <para><b>⛔ WHY THE OBVIOUS FIX FAILED — two measured attempts, both reverted.</b> Lowering
/// <c>AndAlso</c>/<c>OrElse</c> to control flow in <c>IRBuilder</c> (evaluate left, branch,
/// evaluate right only on the live path, merge) is the right IDEA, and <c>AndAlso</c> worked on
/// all three backends immediately. <c>OrElse</c> did not, because it needs the opposite arm:</para>
/// <list type="number">
/// <item><description><b>Merge as the TRUE target</b> — the JavaScript backend reconstructs
/// structured control flow and takes the true target to be the body, so it emitted the merge's
/// contents INSIDE the then-arm and left the rhs arm falling off the end of the function:
/// printed <c>L R</c> and stopped.</description></item>
/// <item><description><b>Negate the condition instead</b> — the C# backend renders operand trees
/// INLINE, so the <c>Not</c> node re-expanded the left operand at the <c>!</c> site and
/// <c>L</c> ran TWICE.</description></item>
/// <item><description><b>An empty skip block to keep the true target a real block</b> — then C#
/// dropped the merge continuation entirely: <c>if (__sc0) { } else { __sc0 = R(true); }</c> and
/// nothing after it.</description></item>
/// </list>
/// <para>So this is not a small IRBuilder change: two backends reconstruct structure from the
/// CFG and neither accepts the shapes above. A real fix needs either that reconstruction taught
/// to handle a conditional whose true target is the merge, or the right operand kept as an
/// unevaluated subtree the backends render inline (which is exactly why C# is accidentally
/// correct today). Both are bigger than a lowering tweak.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class ShortCircuitOperatorTests
{
    /// <summary>
    /// L and R announce themselves, so the OUTPUT records which operands were evaluated.
    /// A test that only checked the boolean result passes with short-circuiting broken.
    /// </summary>
    private const string Harness =
        "Function L(v As Boolean) As Boolean\nConsole.WriteLine(\"L\")\nReturn v\nEnd Function\n" +
        "Function R(v As Boolean) As Boolean\nConsole.WriteLine(\"R\")\nReturn v\nEnd Function\n";

    private static string Program(string condition) =>
        Harness +
        $"Sub Main()\nIf {condition} Then\nConsole.WriteLine(\"taken\")\nEnd If\n" +
        "Console.WriteLine(\"end\")\nEnd Sub\n";

    private static string RunCSharp(string src) =>
        CliTestHarness.CompileRunCSharp(src).Replace("\r\n", "\n").Trim();

    private static string RunCpp(string src) =>
        BclE2E.CompileRun(BclE2E.CompileToCppOptimized(src)).Replace("\r\n", "\n").Trim();

    private static string RunJs(string src) =>
        JavaScriptExecutionTests.RunJs(src).Replace("\r\n", "\n").Trim();

    // ---------------------------------------------------------------- AndAlso short-circuits

    /// <summary>A False left operand must skip the right one entirely.</summary>
    [Test]
    public void AndAlso_LeftFalse_DoesNotEvaluateRight_CSharp()
        => Assert.That(RunCSharp(Program("L(False) AndAlso R(True)")), Is.EqualTo("L\nend"));

    [Test]
    public void AndAlso_LeftFalse_DoesNotEvaluateRight_Cpp()
        => Assert.That(RunCpp(Program("L(False) AndAlso R(True)")), Is.EqualTo("L\nend"),
            "an R in the output means both operands were pre-evaluated into temps");

    [Test]
    public void AndAlso_LeftFalse_DoesNotEvaluateRight_JavaScript()
        => Assert.That(RunJs(Program("L(False) AndAlso R(True)")), Is.EqualTo("L\nend"),
            "an R in the output means both operands were pre-evaluated into temps");

    // ---------------------------------------------------------------- OrElse short-circuits

    /// <summary>A True left operand must skip the right one entirely.</summary>
    [Test]
    public void OrElse_LeftTrue_DoesNotEvaluateRight_CSharp()
        => Assert.That(RunCSharp(Program("L(True) OrElse R(True)")), Is.EqualTo("L\ntaken\nend"));

    [Test]
    public void OrElse_LeftTrue_DoesNotEvaluateRight_Cpp()
        => Assert.That(RunCpp(Program("L(True) OrElse R(True)")), Is.EqualTo("L\ntaken\nend"));

    [Test]
    public void OrElse_LeftTrue_DoesNotEvaluateRight_JavaScript()
        => Assert.That(RunJs(Program("L(True) OrElse R(True)")), Is.EqualTo("L\ntaken\nend"));

    // ------------------------------------------------- And/Or must NOT short-circuit
    //
    // ⛔ THE GUARD AGAINST OVERSHOOTING. These are the non-short-circuit operators and both
    // operands MUST run. A fix that lowers all four to control flow would make these pass
    // "by accident of the result" while silently dropping a side effect — the mirror of the
    // bug above, and just as invisible to a result-only assertion. They pass today.

    [Test]
    public void And_LeftFalse_STILL_EvaluatesRight_CSharp()
        => Assert.That(RunCSharp(Program("L(False) And R(True)")), Is.EqualTo("L\nR\nend"));

    [Test]
    public void And_LeftFalse_STILL_EvaluatesRight_Cpp()
        => Assert.That(RunCpp(Program("L(False) And R(True)")), Is.EqualTo("L\nR\nend"),
            "And is NON-short-circuit — dropping R here would be the opposite miscompile");

    [Test]
    public void And_LeftFalse_STILL_EvaluatesRight_JavaScript()
        => Assert.That(RunJs(Program("L(False) And R(True)")), Is.EqualTo("L\nR\nend"),
            "And is NON-short-circuit — dropping R here would be the opposite miscompile");

    [Test]
    public void Or_LeftTrue_STILL_EvaluatesRight_Cpp()
        => Assert.That(RunCpp(Program("L(True) Or R(False)")), Is.EqualTo("L\nR\ntaken\nend"));

    [Test]
    public void Or_LeftTrue_STILL_EvaluatesRight_JavaScript()
        => Assert.That(RunJs(Program("L(True) Or R(False)")), Is.EqualTo("L\nR\ntaken\nend"));

    // ---------------------------------------------------------------- the value is still right

    /// <summary>
    /// Short-circuiting must not change the RESULT, only which operands run. Pinned separately
    /// so a control-flow lowering that produces the wrong merged value is caught as a value
    /// error rather than hiding behind the side-effect assertions.
    /// </summary>
    [TestCase("L(True) AndAlso R(True)", "L\nR\ntaken\nend")]
    [TestCase("L(True) AndAlso R(False)", "L\nR\nend")]
    [TestCase("L(False) OrElse R(True)", "L\nR\ntaken\nend")]
    [TestCase("L(False) OrElse R(False)", "L\nR\nend")]
    public void ShortCircuit_WhenBothOperandsRun_ValueIsUnchanged_Cpp(string cond, string expected)
        => Assert.That(RunCpp(Program(cond)), Is.EqualTo(expected));

    [TestCase("L(True) AndAlso R(True)", "L\nR\ntaken\nend")]
    [TestCase("L(True) AndAlso R(False)", "L\nR\nend")]
    [TestCase("L(False) OrElse R(True)", "L\nR\ntaken\nend")]
    [TestCase("L(False) OrElse R(False)", "L\nR\nend")]
    public void ShortCircuit_WhenBothOperandsRun_ValueIsUnchanged_JavaScript(string cond, string expected)
        => Assert.That(RunJs(Program(cond)), Is.EqualTo(expected));

    // ---------------------------------------------------------------- the SHIPPING IR
    //
    // ⛔ Every shipping route runs OptimizationPipeline.AddStandardPasses() unconditionally,
    // and RunJs runs none of it. The C++ leg above already goes through CompileToCppOptimized;
    // these close the same gap for JavaScript. Short-circuiting is now CONTROL FLOW, which is
    // exactly what dead-code elimination and copy propagation reshape — an optimizer that
    // folded the guard away would silently restore the original bug.

    [TestCase("L(False) AndAlso R(True)", "L\nend")]
    [TestCase("L(True) OrElse R(True)", "L\ntaken\nend")]
    [TestCase("L(True) AndAlso R(True)", "L\nR\ntaken\nend")]
    [TestCase("L(False) OrElse R(False)", "L\nR\nend")]
    public void Optimized_ShortCircuit_StillHolds_JavaScript(string cond, string expected)
        => Assert.That(
            JavaScriptOptimizedExecutionTests.RunOptimized(Program(cond))
                .Replace("\r\n", "\n").Trim(),
            Is.EqualTo(expected));

    /// <summary>And the non-short-circuit operators keep running both operands after the passes.</summary>
    [Test]
    public void Optimized_And_STILL_EvaluatesRight_JavaScript()
        => Assert.That(
            JavaScriptOptimizedExecutionTests.RunOptimized(Program("L(False) And R(True)"))
                .Replace("\r\n", "\n").Trim(),
            Is.EqualTo("L\nR\nend"));
}
