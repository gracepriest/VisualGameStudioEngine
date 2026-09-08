using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// A constant fold may not delete an operand that does work — chip task_135da836.
///
/// <para><c>IROptimizer</c>'s <c>x And False -&gt; False</c> and <c>x Or True -&gt; True</c>
/// rewrites discarded THE OTHER OPERAND, whatever it was. The two neighbouring arms
/// (<c>x And True -&gt; x</c>) are safe, because they discard whichever side matched
/// <c>IsTrue</c>/<c>IsFalse</c> — and those only match an <c>IRConstant</c>. These two
/// discarded the side that could be a CALL.</para>
///
/// <para>VB's <c>And</c>/<c>Or</c> are NON-short-circuiting: <c>False And Probe()</c> must
/// still call <c>Probe</c>. Ungated, the rewrite produced TWO defects on that one shape:</para>
/// <list type="number">
/// <item><description>a dead store <c>t3 = false;</c> to a temp the declaration pass never
/// emitted — <b>CS0103, generated C# that does not compile</b>; and</description></item>
/// <item><description>a DOUBLE evaluation, because the consumer re-renders the original
/// operand tree inline instead of reading the folded temp, so the hoisted call ran and the
/// inlined one ran again.</description></item>
/// </list>
///
/// <para>⛔ <b>WHY THE OBVIOUS TEST MISSES THIS.</b> The bug needs a CONSTANT operand to
/// trigger the fold AND a side-effecting operand to be destroyed by it. With a non-constant
/// left operand nothing folds and the emission is already clean — measured. So a fixture
/// written with two opaque operands passes against the broken compiler.</para>
///
/// <para><c>AndAlso</c>/<c>OrElse</c> were never affected: they are different
/// <c>BinaryOpKind</c>s and never reached these arms. They are asserted here anyway, because
/// the correct answer for them is the OPPOSITE (the operand must NOT run), and a fix that
/// simply made everything evaluate would satisfy the And/Or half while breaking these.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class LogicalOperandSideEffectTests
{
    private const string Program = @"
Function Probe(v As Boolean) As Boolean
    Console.WriteLine(""RIGHT-EVALUATED"")
    Return v
End Function

Sub Main()
    Dim f As Boolean = False
    Dim t As Boolean = True
    Console.WriteLine(""A"")
    Console.WriteLine(f And Probe(True))
    Console.WriteLine(""B"")
    Console.WriteLine(f AndAlso Probe(True))
    Console.WriteLine(""C"")
    Console.WriteLine(t Or Probe(False))
    Console.WriteLine(""D"")
    Console.WriteLine(t OrElse Probe(False))
End Sub
";

    /// <summary>
    /// The C# leg builds through the shipped CLI, which is what turns a missing declaration
    /// into an observable failure — csc rejects it. An emission-only assertion would not.
    /// </summary>
    [Test]
    public void NonShortCircuitOperands_RunExactlyOnce_AndTheProgramCompiles()
    {
        var output = CliTestHarness.CompileRunCSharp(Program).Replace("\r\n", "\n");

        Assert.That(output, Is.EqualTo(
            "A\nRIGHT-EVALUATED\nFalse\n" +
            "B\nFalse\n" +
            "C\nRIGHT-EVALUATED\nTrue\n" +
            "D\nTrue\n"),
            "A and C must each show EXACTLY ONE RIGHT-EVALUATED (VB's And/Or always evaluate "
            + "the right operand); B and D must show NONE (AndAlso/OrElse skip it). Two "
            + "RIGHT-EVALUATED in A is the double-evaluation half of the bug; a build failure "
            + "is the CS0103 half.");
    }
}
