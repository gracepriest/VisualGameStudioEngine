using System;
using System.Globalization;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>CSharpFloatingLiteral</c> — the C# backend's Single/Double constant spelling, the twin of
/// <see cref="CppFloatLiteralTests"/>.
///
/// <para>⛔ Before this fix a Double fell through to a bare CurrentCulture <c>ToString()</c> and a
/// Single to <c>$"{f}f"</c>. MEASURED through the CLI on master 1fd07d7:
/// <c>Dim inf As Double = 1.0 / 0.0</c> emitted <c>inf = 1 / 0;</c> — an integral Double lost its
/// point, became an INT literal, and constant folding (which refuses division by zero) left it
/// for Roslyn: CS0020. <c>Optional k As Single = 2.5F</c> emitted <c>float k = 2.5</c> (CS1750).
/// On de-DE, <c>h = 2,5;</c> and <c>double r = 0,25</c> — neither compiles.</para>
/// </summary>
[TestFixture]
public class CSharpFloatLiteralTests
{
    private static string Lit(object value) => ImprovedCSharpCodeGenerator.CSharpFloatingLiteral(value);

    // ---- table-driven ------------------------------------------------------------------------

    [TestCase(400f, "400.0f")]
    [TestCase(0f, "0.0f")]
    [TestCase(2.5f, "2.5f")]
    [TestCase(-3.75f, "-3.75f")]
    [TestCase(1e20f, "1E+20f")]
    public void Single_KnownValues(float value, string expected) => Assert.That(Lit(value), Is.EqualTo(expected));

    [TestCase(1d, "1.0")]
    [TestCase(0d, "0.0")]
    [TestCase(400d, "400.0")]
    [TestCase(2.5d, "2.5")]
    [TestCase(1e15d, "1000000000000000.0")]
    [TestCase(1e300d, "1E+300")]
    public void Double_KnownValues(double value, string expected) => Assert.That(Lit(value), Is.EqualTo(expected));

    [Test]
    public void NegativeZero_KeepsItsSign()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Lit(-0.0), Is.EqualTo("-0.0"));
            Assert.That(Lit(-0f), Is.EqualTo("-0.0f"));
        });
    }

    [Test]
    public void NaNAndInfinities_AreTheNamedConstants()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Lit(double.NaN), Is.EqualTo("double.NaN"));
            Assert.That(Lit(double.PositiveInfinity), Is.EqualTo("double.PositiveInfinity"));
            Assert.That(Lit(double.NegativeInfinity), Is.EqualTo("double.NegativeInfinity"));
            Assert.That(Lit(float.NaN), Is.EqualTo("float.NaN"));
            Assert.That(Lit(float.PositiveInfinity), Is.EqualTo("float.PositiveInfinity"));
            Assert.That(Lit(float.NegativeInfinity), Is.EqualTo("float.NegativeInfinity"));
        });
    }

    // ---- round-trip: bits, not strings --------------------------------------------------------

    [TestCaseSource(nameof(RoundTripDoubleValues))]
    public void Double_RoundTripsBits(double value)
    {
        var literal = Lit(value);
        var parsed = double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(BitConverter.DoubleToInt64Bits(parsed), Is.EqualTo(BitConverter.DoubleToInt64Bits(value)),
            $"{value:R} -> {literal} -> {parsed:R}");
    }

    [TestCaseSource(nameof(RoundTripFloatValues))]
    public void Single_RoundTripsBits(float value)
    {
        var literal = Lit(value);
        Assert.That(literal, Does.EndWith("f"), literal);
        var parsed = float.Parse(literal[..^1], NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(BitConverter.SingleToInt32Bits(parsed), Is.EqualTo(BitConverter.SingleToInt32Bits(value)),
            $"{value:R} -> {literal} -> {parsed:R}");
    }

    private static System.Collections.Generic.IEnumerable<double> RoundTripDoubleValues()
    {
        yield return -0.0;
        yield return 0.1;
        yield return double.MaxValue;
        yield return double.Epsilon;
        yield return 1.0 / 3;
        var rnd = new Random(67890);
        for (int i = 0; i < 50; i++)
        {
            var value = BitConverter.Int64BitsToDouble(((long)rnd.Next() << 32) | (uint)rnd.Next());
            if (double.IsFinite(value)) yield return value;
        }
    }

    private static System.Collections.Generic.IEnumerable<float> RoundTripFloatValues()
    {
        yield return -0f;
        yield return 0.1f;
        yield return float.MaxValue;
        yield return float.Epsilon;
        var rnd = new Random(12345);
        for (int i = 0; i < 50; i++)
        {
            var value = BitConverter.Int32BitsToSingle(rnd.Next(int.MinValue, int.MaxValue));
            if (float.IsFinite(value)) yield return value;
        }
    }

    // ---- culture independence -----------------------------------------------------------------

    private static void AssertInvariantSpelling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Lit(2.5f), Is.EqualTo("2.5f"));
            Assert.That(Lit(-3.75f), Is.EqualTo("-3.75f"));
            Assert.That(Lit(2.5d), Is.EqualTo("2.5"));
            Assert.That(Lit(-3.75d), Is.EqualTo("-3.75"));
        });
    }

    [Test]
    [SetCulture("de-DE")]
    public void UnderDeDECulture_OutputIsStillInvariant() => AssertInvariantSpelling();

    /// <summary>sv-SE's negative sign is U+2212, not a C# token.</summary>
    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_OutputIsStillInvariant() => AssertInvariantSpelling();

    // ---- emitted programs, through the optimizer ---------------------------------------------

    /// <summary>
    /// Constant folding refuses a division by zero, so these literals reach the backend as-is —
    /// the shape that exposed the bug. The zeros arrive as literals, not variables, on purpose.
    /// </summary>
    internal const string DivideByZeroProgram = @"
Sub Main()
    Dim inf As Double = 1.0 / 0.0
    Dim ninf As Double = -1.0 / 0.0
    Dim nan As Double = 0.0 / 0.0
    If inf > 1.0E300 Then
        Console.WriteLine(""inf ok"")
    End If
    If ninf < -1.0E300 Then
        Console.WriteLine(""ninf ok"")
    End If
    If nan <> nan Then
        Console.WriteLine(""nan ok"")
    End If
End Sub
";

    [Test]
    public void DivideByZero_EmitsFloatingLiteralsAndCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(DivideByZeroProgram);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("1.0 / 0.0"), cs);
            Assert.That(cs, Does.Not.Contain("1 / 0"), "an integral Double lost its point:\n" + cs);
        });
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(DivideByZeroProgram), Is.Empty,
            "the emitted C# must compile (it was CS0020)");
    }

    private const string OptionalDefaultsProgram = @"
Function Scale(Optional k As Single = 2.5F) As Single
    Return k
End Function
Function Ratio(Optional r As Double = 0.25) As Double
    Return r
End Function
Sub Main()
    Console.WriteLine(CStr(Scale() + Ratio()))
End Sub
";

    [Test]
    public void OptionalSingleDefault_IsAFloatLiteral()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(OptionalDefaultsProgram);
        Assert.That(cs, Does.Contain("float k = 2.5f"), cs);
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(OptionalDefaultsProgram), Is.Empty,
            "the emitted C# must compile (it was CS1750)");
    }

    /// <summary>The WHOLE compile runs under de-DE, the path a German machine takes.</summary>
    [Test]
    [SetCulture("de-DE")]
    public void UnderDeDECulture_EmittedProgramCompiles()
    {
        var cs = ReturnCoercionTests.EmitCSharpForTest(OptionalDefaultsProgram);
        Assert.Multiple(() =>
        {
            Assert.That(cs, Does.Contain("double r = 0.25"), cs);
            Assert.That(cs, Does.Not.Contain("0,25"), cs);
        });
        Assert.That(ReturnCoercionTests.CompileEmittedCSharpForTest(OptionalDefaultsProgram), Is.Empty);
    }
}

/// <summary>The divide-by-zero program compiled AND run: the values must be ±Infinity and NaN.</summary>
[Category("Integration")]
[NonParallelizable] // redirects Console.Out
public class CSharpFloatLiteralRunTests
{
    [Test]
    public void DivideByZero_Runs() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(CSharpFloatLiteralTests.DivideByZeroProgram)),
            Is.EqualTo("inf ok\nninf ok\nnan ok"));
}
