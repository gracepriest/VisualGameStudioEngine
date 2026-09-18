using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// What <c>CInt</c> means at a midpoint, and the fact that all four backends now agree about it.
///
/// <para>⛔ THEY DID NOT. Measured on <c>CInt(7.5)</c>, <c>CInt(8.5)</c>, <c>CInt(7.9)</c>,
/// <c>CInt(-7.5)</c>: C# printed <b>8,8,8,-8</b> while MSIL, JavaScript and C++ all printed
/// <b>7,8,7,-7</b>. One language, two answers — a silent wrong result on three backends out of
/// four, with nothing to warn anyone.</para>
///
/// <para>⚠ C# was the RIGHT one. It emits <c>Convert.ToInt32</c>, which is exactly
/// <c>Math.Round(x, MidpointRounding.ToEven)</c> — banker's rounding, which is what VB's
/// <c>CInt</c> specifies. Verified directly against <c>Convert.ToInt32</c> on ten values before
/// changing anything, because the whole fix direction depended on it: the midpoints are what
/// separate ToEven from both truncation and AwayFromZero (<c>8.5</c> → 8, not 9; <c>2.5</c> → 2,
/// not 3; <c>-8.5</c> → -8, not -9).</para>
///
/// <para>⚠ The three backends were fixed to agree with C#, not the reverse: MSIL now emits
/// <c>Convert::ToInt32(float64)</c>, C++ wraps the cast in <c>std::nearbyint</c> (the default
/// FE_TONEAREST mode matches on all ten values), and JavaScript gets an emitted helper because it
/// has no built-in — <c>Math.round</c> is half-up toward +Infinity and answers -7 for -7.5.</para>
/// </summary>
[TestFixture]
public class ConversionRoundingTests
{
    private static string Program(string body) => $"""
        Module M
         Sub Main()
          {body}
         End Sub
        End Module
        """;

    /// <summary>
    /// ⚠ The midpoints are the whole point. <c>7.9</c> is here as the case that separates rounding
    /// from truncation at all; <c>8.5</c> and <c>2.5</c> separate half-to-even from AwayFromZero;
    /// <c>-7.5</c> separates it from JavaScript's <c>Math.round</c>, which rounds toward
    /// +Infinity.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("7.5", "8", TestName = "Round_SevenPointFive_ToEight")]
    [TestCase("8.5", "8", TestName = "Round_EightPointFive_ToEight_NotNine")]
    [TestCase("2.5", "2", TestName = "Round_TwoPointFive_ToTwo_NotThree")]
    [TestCase("7.9", "8", TestName = "Round_SevenPointNine_ToEight")]
    [TestCase("-7.5", "-8", TestName = "Round_MinusSevenPointFive_ToMinusEight")]
    [TestCase("-8.5", "-8", TestName = "Round_MinusEightPointFive_ToMinusEight")]
    [TestCase("7.4999", "7", TestName = "Round_BelowMidpoint_Down")]
    public void CInt_RoundsHalfToEven_OnEveryBackend(string literal, string expected)
    {
        var program = Program($"Dim a As Double = {literal}\n  PrintLine(CStr(CInt(a)))");

        Assert.Multiple(() =>
        {
            Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(program), Is.Empty);
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo(expected));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo(expected + "\n"));
        });
    }

    /// <summary>
    /// ⚠ <c>CLng</c> takes the same treatment on the two backends that have it. JavaScript is
    /// absent on purpose — it refuses Long outright (a JS number is exact only to 2^53), which is
    /// a documented capability refusal and not something this change touches.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase("7.5", "8", TestName = "RoundLong_SevenPointFive")]
    [TestCase("8.5", "8", TestName = "RoundLong_EightPointFive")]
    [TestCase("-7.5", "-8", TestName = "RoundLong_MinusSevenPointFive")]
    public void CLng_RoundsHalfToEven_OnMsilAndCpp(string literal, string expected)
    {
        var program = Program($"Dim a As Double = {literal}\n  PrintLine(CStr(CLng(a)))");

        Assert.Multiple(() =>
        {
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo(expected + "\n"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo(expected + "\n"));
        });
    }

    /// <summary>
    /// ⚠ An INTEGRAL argument keeps the plain conversion it always had.
    ///
    /// <para>⛔ For <c>CInt</c> that is an early-out, NOT a correctness claim — stated honestly
    /// because the tempting justification is wrong. Routing an integral through the
    /// <c>float64</c> overload does verify (a <c>conv.r8</c> in front makes it legal IL), and a
    /// 32-bit Integer is exact in a double, so a mutation that rounds the integral case too
    /// passes every test here. <see cref="CLng_OfALargeLong_KeepsFullPrecision"/> is where the
    /// guard actually earns itself.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void CInt_OfAnIntegral_IsUnchanged()
    {
        var program = Program("Dim i As Integer = 5\n  PrintLine(CStr(CInt(i)))");

        Assert.Multiple(() =>
        {
            Assert.That(Msil.MsilHarness.RunExpectingSuccess(program), Is.EqualTo("5\n"));
            Assert.That(JavaScriptExecutionTests.RunJs(program), Is.EqualTo("5"));
            Assert.That(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(program)),
                Is.EqualTo("5\n"));
        });
    }

    /// <summary>
    /// ⛔ Where the integral guard is CORRECTNESS rather than an early-out: a Long above 2^53 does
    /// not survive a double round trip. <c>9007199254740993</c> is 2^53+1, the smallest integer a
    /// double cannot represent — through <c>float64</c> it comes back as
    /// <c>9007199254740992</c>.
    ///
    /// <para>⚠ This is the test that kills the mutation which rounds integral arguments too; the
    /// <c>CInt</c> cases cannot, because a 32-bit Integer is exact in a double.</para>
    /// </summary>
    [Test]
    [Category("Integration")]
    public void CLng_OfALargeLong_KeepsFullPrecision()
    {
        var program = Program("Dim big As Long = 9007199254740993\n  PrintLine(CStr(CLng(big)))");

        Assert.That(Msil.MsilHarness.RunExpectingSuccess(program),
            Is.EqualTo("9007199254740993\n"),
            "a double round trip would answer 9007199254740992");
    }

    /// <summary>
    /// ⚠ The JavaScript helper is emitted ONLY by a program that calls <c>CInt</c>, the same
    /// "only what is required" shape as the exception prelude.
    ///
    /// <para>⛔ It is selected by SCANNING the module, not by a flag set while lowering — the
    /// prelude is emitted before any function body, so a flag is still false at that point.
    /// Measured: the first attempt emitted every call site and no definition, and the program died
    /// in Node with "__blCInt is not defined".</para>
    /// </summary>
    [Test]
    public void TheJavaScriptRoundingHelper_IsEmittedOnlyWhenCIntIsUsed()
    {
        var withCInt = JsTestSupport.Compile(
            Program("Dim a As Double = 7.5\n  PrintLine(CStr(CInt(a)))"));
        var without = JsTestSupport.Compile(Program("PrintLine(\"hi\")"));

        Assert.Multiple(() =>
        {
            Assert.That(withCInt, Does.Contain("function __blCInt"),
                "the call sites reference it, so the definition must be emitted too");
            Assert.That(without, Does.Not.Contain("__blCInt"),
                "hello world should carry none of it");
        });
    }
}
