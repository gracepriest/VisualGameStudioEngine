using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 13 — the numeric model.
///
/// <para>Integer, Single and Double all erase to one JS <c>number</c> (spec D2, user
/// decision). That makes most arithmetic free and two operations dangerous:</para>
/// <list type="bullet">
/// <item><description><c>\</c> (integer division) has no JS operator at all — JS <c>/</c> is
/// always floating point.</description></item>
/// <item><description>Both <c>\</c> and <c>Mod</c> must truncate TOWARD ZERO to match .NET.
/// <c>Math.floor</c> is the obvious wrong answer and differs only for negatives, so a suite
/// that tests just <c>7 \ 2</c> passes while <c>-7 \ 2</c> ships -4 instead of -3.</description></item>
/// </list>
///
/// <para>Every case is an EXECUTION test. A text assertion on the emitted JS proves the
/// generator wrote what the test author expected, not that the arithmetic is right — and
/// silent wrong-number output is precisely the bug class this backend exists to avoid.</para>
///
/// <para><b>No <c>|0</c> masking anywhere.</b> 32-bit overflow is a documented limit of this
/// backend, not something to emulate (spec D2).</para>
/// </summary>
[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptNumericTests
{
    private static string Run(string body) =>
        JavaScriptExecutionTests.RunJs($"Sub Main()\n{body}\nEnd Sub");

    private static string Print(string expr) => Run($"Console.WriteLine({expr})");

    // ---------------------------------------------------------------- arithmetic

    [TestCase("2 + 3", "5", TestName = "Add")]
    [TestCase("7 - 2", "5", TestName = "Subtract")]
    [TestCase("6 * 7", "42", TestName = "Multiply")]
    [TestCase("7 / 2", "3.5", TestName = "Divide_IsFloatingPoint")]
    public void Arithmetic(string expr, string expected)
        => Assert.That(Print(expr), Is.EqualTo(expected));

    // ---------------------------------------------------------------- the dangerous two

    /// <summary>
    /// <c>\</c> truncates toward zero. Math.floor(-7/2) is -4; .NET gives -3. The positive
    /// case alone cannot distinguish the two, which is why both are here.
    /// </summary>
    [TestCase("7 \\ 2", "3", TestName = "IntegerDivision_Positive")]
    [TestCase("-7 \\ 2", "-3", TestName = "IntegerDivision_NegativeTruncatesTowardZero")]
    [TestCase("7 \\ -2", "-3", TestName = "IntegerDivision_NegativeDivisor")]
    public void IntegerDivision_TruncatesTowardZero(string expr, string expected)
        => Assert.That(Print(expr), Is.EqualTo(expected));

    /// <summary>
    /// .NET's Mod takes the sign of the DIVIDEND, and so does JS's <c>%</c> — they agree
    /// exactly, which is why this maps to a bare operator rather than a helper.
    /// </summary>
    [TestCase("7 Mod 2", "1", TestName = "Mod_Positive")]
    [TestCase("-7 Mod 2", "-1", TestName = "Mod_TakesSignOfDividend")]
    [TestCase("7 Mod -2", "1", TestName = "Mod_NegativeDivisor")]
    public void Mod_MatchesDotNetSign(string expr, string expected)
        => Assert.That(Print(expr), Is.EqualTo(expected));

    // ---------------------------------------------------------------- conversions

    [TestCase("CInt(3.7)", "3", TestName = "CInt_TruncatesDown")]
    [TestCase("CInt(-3.7)", "-3", TestName = "CInt_TruncatesTowardZero")]
    public void CInt_Truncates(string expr, string expected)
        => Assert.That(Print(expr), Is.EqualTo(expected));

    /// <summary>CDbl/CSng are identity under erasure — all three are one JS number.</summary>
    [Test]
    public void CDbl_IsIdentity()
        => Assert.That(Print("CDbl(3)"), Is.EqualTo("3"));

    // ---------------------------------------------------------------- comparisons

    [TestCase("1 = 1", "true", TestName = "Equal")]
    [TestCase("1 <> 2", "true", TestName = "NotEqual")]
    [TestCase("1 < 2", "true", TestName = "LessThan")]
    [TestCase("2 >= 2", "true", TestName = "GreaterOrEqual")]
    public void Comparisons(string expr, string expected)
        => Assert.That(Print(expr), Is.EqualTo(expected));

    // ---------------------------------------------------------------- through variables

    /// <summary>
    /// Constant folding could hide a broken operator: the optimizer may evaluate a literal
    /// expression at compile time, so the generator's arm never runs. Routing through
    /// variables forces the operator to be emitted.
    /// </summary>
    [Test]
    public void IntegerDivision_ThroughVariables_StillTruncates()
    {
        var output = Run(
            "Dim a As Integer\n" +
            "Dim b As Integer\n" +
            "a = -7\n" +
            "b = 2\n" +
            "Console.WriteLine(a \\ b)");

        Assert.That(output, Is.EqualTo("-3"));
    }

    /// <summary>String concatenation is its own BinaryOpKind (Concat), not Add.</summary>
    [Test]
    public void StringConcatenation()
        => Assert.That(Print("\"a\" & \"b\""), Is.EqualTo("ab"));
}
