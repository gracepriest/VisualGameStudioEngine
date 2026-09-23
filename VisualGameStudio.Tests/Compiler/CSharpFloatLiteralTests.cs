using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>CSharpFloatLiteral</c> / <c>CSharpDoubleLiteral</c> — the C# backend's Single/Double
/// constant spelling, culture-invariant and round-tripping; plus the integral fallback, the
/// enum-member value line, and <c>FormatDefaultValue</c> now delegating to <c>EmitConstant</c>.
/// Sibling to <see cref="CppFloatLiteralTests"/> (the C++ side of the same defect, PR #67).
///
/// <para>⛔ Measured BEFORE this fix: de-DE <c>Math.Log(8.2)</c> emitted <c>Math.Log(8,2)</c> —
/// valid C#, the TWO-ARGUMENT overload (log base 2 of 8) — so the build succeeded and printed 3
/// instead of ~2.104, a silent miscompile. de-DE a field/const <c>2.5</c> emitted <c>2,5</c> —
/// CS1001/CS1003. sv-SE negatives used U+2212 (not ASCII '-') — CS1056, on Integer, Long, Double,
/// Single and an Enum member alike. Double <c>-0.0</c> emitted <c>-0</c> — integer negation, i.e.
/// +0.0, losing the sign. ∞/NaN emitted <c>∞</c>/<c>NaN</c>/<c>NaNf</c> — not C# tokens.
/// <c>Dim o As Object = 400.0</c> emitted <c>o = 400;</c> — boxed an <c>Int32</c>, not a
/// <c>Double</c>. <c>Math.Max(400.0, t)</c> bound <c>Max(int, int)</c>. <c>Optional x As Single =
/// 2.5f</c> emitted <c>float x = 2.5</c> — CS1750 (a double default for a float parameter) in
/// EVERY culture, because <c>FormatDefaultValue</c> had its own copy of the literal logic with no
/// Single arm. <c>Optional c As Char = "a"c</c> emitted <c>char c = a</c> — CS0103, an unquoted
/// bare identifier.</para>
/// </summary>
[TestFixture]
public class CSharpFloatLiteralTests
{
    // ==========================================================================================
    // Table-driven: CSharpFloatLiteral. Unlike the C++ helper, an integral value gets NO ".0" —
    // `400f` is a valid C# float literal on its own; C++ needs the point in front of the suffix.
    // ==========================================================================================

    [TestCase(400f, "400f")]
    [TestCase(0f, "0f")]
    [TestCase(5f, "5f")]
    [TestCase(2.5f, "2.5f")]
    [TestCase(-3.75f, "-3.75f")]
    [TestCase(-400f, "-400f")]
    [TestCase(16777216f, "16777216f")]
    [TestCase(1e20f, "1E+20f")]
    [TestCase(1.5e-05f, "1.5E-05f")]
    public void CSharpFloatLiteral_KnownValues(float value, string expected)
    {
        Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(value), Is.EqualTo(expected));
    }

    [Test]
    public void CSharpFloatLiteral_NegativeZero_KeepsItsSign()
    {
        // A bit-pattern check on the input side too, so a platform where -0f == 0f folds away
        // cannot make this pass by accident.
        Assert.That(BitConverter.SingleToInt32Bits(-0f), Is.Not.EqualTo(BitConverter.SingleToInt32Bits(0f)));
        Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(-0f), Is.EqualTo("-0f"));
    }

    [Test]
    public void CSharpFloatLiteral_ExtremeValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.MaxValue), Is.EqualTo("3.4028235E+38f"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.Epsilon), Is.EqualTo("1E-45f"));
        });
    }

    [Test]
    public void CSharpFloatLiteral_NaNAndInfinities_AreConstantFieldExpressions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.NaN), Is.EqualTo("float.NaN"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.PositiveInfinity), Is.EqualTo("float.PositiveInfinity"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.NegativeInfinity), Is.EqualTo("float.NegativeInfinity"));
        });
    }

    // ==========================================================================================
    // Table-driven: CSharpDoubleLiteral. An integral round-trip string (no '.'/'E'/'e') gets
    // ".0" appended — a bare int literal would type the constant as `int`, not `double`.
    // ==========================================================================================

    [TestCase(400d, "400.0")]
    [TestCase(0d, "0.0")]
    [TestCase(2.5d, "2.5")]
    [TestCase(-3.75d, "-3.75")]
    [TestCase(1.2345678901234568e+17, "1.2345678901234568E+17")]
    public void CSharpDoubleLiteral_KnownValues(double value, string expected)
    {
        Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(value), Is.EqualTo(expected));
    }

    [Test]
    public void CSharpDoubleLiteral_NegativeZero_KeepsItsSign()
    {
        Assert.That(BitConverter.DoubleToInt64Bits(-0.0), Is.Not.EqualTo(BitConverter.DoubleToInt64Bits(0.0)));
        Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(-0.0), Is.EqualTo("-0.0"));
    }

    [Test]
    public void CSharpDoubleLiteral_ExtremeValues()
    {
        // double.Epsilon's round-trip string is "5E-324" — no '.', which is exactly the shape
        // that would be mis-suffixed by an exponent-blind ".0" check (see the mutant table).
        Assert.Multiple(() =>
        {
            Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(double.MaxValue), Is.EqualTo("1.7976931348623157E+308"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(double.Epsilon), Is.EqualTo("5E-324"));
        });
    }

    [Test]
    public void CSharpDoubleLiteral_NaNAndInfinities_AreConstantFieldExpressions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(double.NaN), Is.EqualTo("double.NaN"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(double.PositiveInfinity), Is.EqualTo("double.PositiveInfinity"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(double.NegativeInfinity), Is.EqualTo("double.NegativeInfinity"));
        });
    }

    // ==========================================================================================
    // Round-trip: bits, not strings. Under invariant, de-DE and sv-SE — an exponent-blind ".0"
    // append (mutant #2) would corrupt an exponential-but-dotless round-trip string like
    // double.Epsilon's "5E-324" into "5E-324.0", which fails to parse back at all.
    // ==========================================================================================

    [TestCaseSource(nameof(RoundTripFloatValues))]
    public void CSharpFloatLiteral_RoundTripsBits(float value)
    {
        AssertFloatRoundTrips(value);
    }

    [TestCaseSource(nameof(RoundTripDoubleValues))]
    public void CSharpDoubleLiteral_RoundTripsBits(double value)
    {
        AssertDoubleRoundTrips(value);
    }

    [Test]
    [SetCulture("de-DE")]
    public void CSharpFloatLiteral_RoundTripsBits_UnderDeDE()
    {
        foreach (var value in RoundTripFloatValues()) AssertFloatRoundTrips(value);
    }

    [Test]
    [SetCulture("sv-SE")]
    public void CSharpFloatLiteral_RoundTripsBits_UnderSvSE()
    {
        foreach (var value in RoundTripFloatValues()) AssertFloatRoundTrips(value);
    }

    [Test]
    [SetCulture("de-DE")]
    public void CSharpDoubleLiteral_RoundTripsBits_UnderDeDE()
    {
        foreach (var value in RoundTripDoubleValues()) AssertDoubleRoundTrips(value);
    }

    [Test]
    [SetCulture("sv-SE")]
    public void CSharpDoubleLiteral_RoundTripsBits_UnderSvSE()
    {
        foreach (var value in RoundTripDoubleValues()) AssertDoubleRoundTrips(value);
    }

    private static void AssertFloatRoundTrips(float value)
    {
        var literal = ImprovedCSharpCodeGenerator.CSharpFloatLiteral(value);
        if (float.IsNaN(value) || float.IsInfinity(value)) return; // not bit-parsed back; pinned by name above
        Assert.That(literal, Does.EndWith("f"), literal);
        var numeric = literal.Substring(0, literal.Length - 1);
        var parsed = float.Parse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(BitConverter.SingleToInt32Bits(parsed), Is.EqualTo(BitConverter.SingleToInt32Bits(value)),
            $"{value:R} -> {literal} -> {parsed:R}");
    }

    private static void AssertDoubleRoundTrips(double value)
    {
        var literal = ImprovedCSharpCodeGenerator.CSharpDoubleLiteral(value);
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        var parsed = double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(BitConverter.DoubleToInt64Bits(parsed), Is.EqualTo(BitConverter.DoubleToInt64Bits(value)),
            $"{value:R} -> {literal} -> {parsed:R}");
    }

    private static System.Collections.Generic.IEnumerable<float> RoundTripFloatValues()
    {
        yield return -0f;
        yield return 0.1f;
        yield return float.MaxValue;
        yield return float.Epsilon;
        yield return 16777216f;
        yield return 1e20f;
        yield return 1.5e-05f;

        var rnd = new Random(24680);
        for (int i = 0; i < 50; i++)
        {
            var value = BitConverter.Int32BitsToSingle(rnd.Next(int.MinValue, int.MaxValue));
            if (float.IsFinite(value)) yield return value;
        }
    }

    private static System.Collections.Generic.IEnumerable<double> RoundTripDoubleValues()
    {
        yield return -0.0;
        yield return 0.1;
        yield return double.MaxValue;
        yield return double.Epsilon; // "5E-324" — the exponent-blind-".0" killer
        yield return 1.0 / 3;
        yield return 1.2345678901234568e+17;

        var rnd = new Random(13579);
        for (int i = 0; i < 50; i++)
        {
            var bits = ((long)rnd.Next() << 32) | (uint)rnd.Next();
            var value = BitConverter.Int64BitsToDouble(bits);
            if (double.IsFinite(value)) yield return value;
        }
    }

    // ==========================================================================================
    // Case 1 — de-DE Math.Log(8.2): kills InvariantCulture -> CurrentCulture in the double
    // helper. Under CurrentCulture, `Math.Log(8,2)` is a valid but DIFFERENT call (the two-
    // argument log-base overload), so this needs an actual RUN, not just a compile, to catch it —
    // the old code built fine and printed the wrong number.
    // ==========================================================================================

    [Test]
    [SetCulture("de-DE")]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void UnderDeDECulture_MathLog_EmitsInvariantTextAndComputesTheRightValue()
    {
        const string src = """
            Module M
             Sub Main()
              Dim r As Double = Log(8.2)
              Console.WriteLine(r > 2.1 AndAlso r < 2.2)
             End Sub
            End Module
            """;

        var csText = ReturnCoercionTests.EmitCSharpForTest(src);
        Assert.That(csText, Does.Contain("Math.Log(8.2)"), csText);
        Assert.That(csText, Does.Not.Contain("Math.Log(8,2)"), csText);

        var output = FourBackends.RunEmittedCSharpText(csText).Trim();
        Assert.That(output, Is.EqualTo("True"),
            "Log(8.2) must land strictly between 2.1 and 2.2 (the actual value is ~2.104); " +
            "'False' here means the two-argument Math.Log(double,double) overload fired instead");
    }

    // ==========================================================================================
    // Case 2 — de-DE field/const 2.5 (double helper) and a Single twin (float helper): both must
    // still say "2.5", not "2,5", and the emitted C# must compile.
    // ==========================================================================================

    [Test]
    [SetCulture("de-DE")]
    public void UnderDeDECulture_DoubleFieldAndConst_EmitPeriodDecimalAndCompile()
    {
        const string src = """
            Class Box
             Public D As Double = 2.5
             Public Const K As Double = 2.5
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        var csText = ReturnCoercionTests.EmitCSharpForTest(src);
        Assert.That(csText, Does.Not.Contain("2,5"), csText);
        Assert.That(csText.Split("2.5", StringSplitOptions.None).Length - 1, Is.GreaterThanOrEqualTo(2),
            "expected '2.5' to appear at least twice (field default + const): " + csText);

        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(src);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    [Test]
    [SetCulture("de-DE")]
    public void UnderDeDECulture_SingleFieldAndConst_EmitPeriodDecimalAndCompile()
    {
        const string src = """
            Class Box
             Public F As Single = 2.5F
             Public Const K As Single = 2.5F
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        var csText = ReturnCoercionTests.EmitCSharpForTest(src);
        Assert.That(csText, Does.Not.Contain("2,5"), csText);
        Assert.That(csText, Does.Contain("2.5f"), csText);

        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(src);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    // ==========================================================================================
    // Case 3 — sv-SE Integer/Long/Double/Single/Enum negatives: ASCII '-', never U+2212, and the
    // emitted C# compiles. Kills reverting the integral fallback AND (separately) the enum line.
    // ==========================================================================================

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_NegativeNumericsAndEnum_UseAsciiMinusAndCompile()
    {
        const string src = """
            Enum Dir
             Forward = 0
             Back = -1
            End Enum

            Class Box
             Public NegI As Integer = -7
             Public NegL As Long = -9
             Public NegD As Double = -3.75
             Public NegF As Single = -3.75
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        var csText = ReturnCoercionTests.EmitCSharpForTest(src);
        Assert.Multiple(() =>
        {
            Assert.That(csText, Does.Not.Contain("−"), csText);
            Assert.That(csText, Does.Contain("-7"), csText);
            Assert.That(csText, Does.Contain("-9"), csText);
            Assert.That(csText, Does.Contain("-3.75"), csText);
            Assert.That(csText, Does.Contain("Back = -1"), csText);
        });

        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(src);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    // ==========================================================================================
    // Case 4 — Dim o As Object = 400.0: the literal's OWN type must be double, not int, else the
    // box picks up Int32. Kills dropping the ".0" append in the double helper.
    // ==========================================================================================

    [Test]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void ObjectBoxedFromDoubleLiteral_KeepsDoubleType()
    {
        const string src = """
            Module M
             Sub Main()
              Dim o As Object = 400.0
              Console.WriteLine(o.GetType().Name)
             End Sub
            End Module
            """;

        var csText = ReturnCoercionTests.EmitCSharpForTest(src);
        Assert.That(csText, Does.Contain("400.0"), csText);

        var output = FourBackends.RunEmittedCSharpText(csText).Trim();
        Assert.That(output, Is.EqualTo("Double"));
    }

    [Test]
    [Category("Integration")]
    public void Cli_ObjectBoxedFromDoubleLiteral_KeepsDoubleType()
    {
        const string src = """
            Module M
             Sub Main()
              Dim o As Object = 400.0
              Console.WriteLine(o.GetType().Name)
             End Sub
            End Module
            """;

        Assert.That(CliTestHarness.CompileRunCSharp(src).Trim(), Is.EqualTo("Double"));
    }

    // ==========================================================================================
    // Case 5 — Double -0.0: 1 / NZ must be negative infinity. A culture-proof runtime check
    // (equality against the same folded-infinity recipe Case 7 proves works), not a formatted
    // string. Kills dropping ".0" (an int -0.0 negates to +0, so 1/NZ would be POSITIVE
    // infinity instead, and the equality check would fail).
    // ==========================================================================================

    [Test]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void DoubleNegativeZero_OneOverIt_IsNegativeInfinity()
    {
        const string src = """
            Module M
             Sub Main()
              Dim nz As Double = -0.0
              Dim r As Double = 1.0 / nz
              Dim negInf As Double = -1.0E+300 * 1.0E+300
              Console.WriteLine(r = negInf)
             End Sub
            End Module
            """;

        var output = FourBackends.RunEmittedCSharp(src).Trim();
        Assert.That(output, Is.EqualTo("True"),
            "1 / -0.0 must be negative infinity; 'False' means the sign of -0.0 was lost in the emitted literal");
    }

    [Test]
    [Category("Integration")]
    public void Cli_DoubleNegativeZero_OneOverIt_IsNegativeInfinity()
    {
        const string src = """
            Module M
             Sub Main()
              Dim nz As Double = -0.0
              Dim r As Double = 1.0 / nz
              Dim negInf As Double = -1.0E+300 * 1.0E+300
              Console.WriteLine(r = negInf)
             End Sub
            End Module
            """;

        Assert.That(CliTestHarness.CompileRunCSharp(src).Trim(), Is.EqualTo("True"));
    }

    // ==========================================================================================
    // Case 6 — Single -0.0 keeps its sign too (guard: already covered by the helper table above,
    // restated here against the emitted TEXT so a regression in the field-initializer path,
    // rather than the helper itself, is also caught).
    // ==========================================================================================

    [Test]
    public void SingleField_NegativeZero_EmitsSignedLiteral()
    {
        const string src = """
            Class Box
             Public X As Single = -0.0
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        var csText = ReturnCoercionTests.EmitCSharpForTest(src);
        Assert.That(csText, Does.Contain("-0f"), csText);
    }

    // ==========================================================================================
    // Case 7 — NaN / +Inf / -Inf for Double via source (the lexer needs a '.' before an exponent,
    // so these are the smallest expressions that fold to the special values at compile time),
    // including as a Const. Single specials are pinned via the helper only — the analyzer rejects
    // a Single overflow written directly in source.
    // ==========================================================================================

    [Test]
    public void DoubleField_PositiveInfinity_EmitsConstantFieldExpression()
    {
        const string src = """
            Class Box
             Public D As Double = 1.0E+300 * 1.0E+300
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        Assert.That(ReturnCoercionTests.EmitCSharpForTest(src), Does.Contain("double.PositiveInfinity"));
    }

    [Test]
    public void DoubleField_NegativeInfinity_EmitsConstantFieldExpression()
    {
        const string src = """
            Class Box
             Public D As Double = -1.0E+300 * 1.0E+300
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        Assert.That(ReturnCoercionTests.EmitCSharpForTest(src), Does.Contain("double.NegativeInfinity"));
    }

    [Test]
    public void DoubleField_NaN_EmitsConstantFieldExpression()
    {
        const string src = """
            Class Box
             Public D As Double = (1.0E+300 * 1.0E+300) - (1.0E+300 * 1.0E+300)
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        Assert.That(ReturnCoercionTests.EmitCSharpForTest(src), Does.Contain("double.NaN"));
    }

    [Test]
    public void DoubleConst_PositiveInfinity_EmitsConstantFieldExpression()
    {
        const string src = """
            Class Box
             Public Const D As Double = 1.0E+300 * 1.0E+300
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """;

        Assert.That(ReturnCoercionTests.EmitCSharpForTest(src), Does.Contain("double.PositiveInfinity"));
    }

    [Test]
    public void SingleSpecials_ViaHelper_AreConstantFieldExpressions()
    {
        // The analyzer rejects a literal Single overflow written directly in source, so the
        // helper is exercised directly rather than through an end-to-end compile — the same
        // property CppFloatLiteralTests documents for its own Single-specials-via-helper case.
        Assert.Multiple(() =>
        {
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.NaN), Is.EqualTo("float.NaN"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.PositiveInfinity), Is.EqualTo("float.PositiveInfinity"));
            Assert.That(ImprovedCSharpCodeGenerator.CSharpFloatLiteral(float.NegativeInfinity), Is.EqualTo("float.NegativeInfinity"));
        });
    }

    // ==========================================================================================
    // Case 8 — Optional Single default 2.5f compiles and returns 2.5, in en-US. Kills reverting
    // FormatDefaultValue to its old copy (which had no Single arm and emitted a bare double
    // literal — CS1750 for a float parameter).
    // ==========================================================================================

    [Test]
    [NonParallelizable] // FourBackends' C# leg redirects Console.Out
    public void OptionalSingleDefault_CompilesAndReturnsItsValue()
    {
        const string src = """
            Module M
             Function Half(Optional x As Single = 2.5F) As Single
              Return x
             End Function
             Sub Main()
              Console.WriteLine(Half())
             End Sub
            End Module
            """;

        var output = FourBackends.RunEmittedCSharp(src).Trim();
        Assert.That(output, Is.EqualTo("2.5"));
    }

    [Test]
    [Category("Integration")]
    public void Cli_OptionalSingleDefault_CompilesAndReturnsItsValue()
    {
        const string src = """
            Module M
             Function Half(Optional x As Single = 2.5F) As Single
              Return x
             End Function
             Sub Main()
              Console.WriteLine(Half())
             End Sub
            End Module
            """;

        Assert.That(CliTestHarness.CompileRunCSharp(src).Trim(), Is.EqualTo("2.5"));
    }

    // ==========================================================================================
    // Case 9 — Optional Char default compiles (FormatDefaultValue delegating to EmitConstant
    // also fixed the unquoted-bare-identifier Char default).
    // ==========================================================================================

    [Test]
    public void OptionalCharDefault_Compiles()
    {
        const string src = """
            Module M
             Sub Greet(Optional c As Char = "a"c)
              Console.WriteLine(c)
             End Sub
             Sub Main()
              Greet()
             End Sub
            End Module
            """;

        var errors = ReturnCoercionTests.CompileEmittedCSharpForTest(src);
        Assert.That(errors, Is.Empty, string.Join("\n", errors));
    }

    // ==========================================================================================
    // Entry-point coverage: the IDE build path (BasicCompiler.CompileProjectFiles), generating
    // C# straight from the combined, optimized IR the same way the IDE does — not through the
    // Parser/Analyzer/IRBuilder pipeline EmitCSharpForTest hand-assembles.
    // ==========================================================================================

    [Test]
    public void CompileProjectFiles_EmitsInvariantLiteralsForTheIdeBuildPath()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bl-cs-float-lit-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var path = System.IO.Path.Combine(dir, "Main.bas");
            System.IO.File.WriteAllText(path, """
                Class Box
                 Public D As Double = 400.0
                 Public F As Single = 2.5
                End Class

                Module M
                 Sub Main()
                 End Sub
                End Module
                """);

            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "csharp" });
            var result = compiler.CompileProjectFiles(new[] { path });
            Assert.That(result.Success, Is.True, string.Join("; ", result.AllErrors.Select(e => e.Message)));

            var csText = new ImprovedCSharpCodeGenerator().Generate(result.CombinedIR);
            Assert.Multiple(() =>
            {
                Assert.That(csText, Does.Contain("400.0"), csText);
                Assert.That(csText, Does.Contain("2.5f"), csText);
            });
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
