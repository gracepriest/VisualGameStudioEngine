using System;
using System.Globalization;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// <c>CppFloatLiteral</c> / <c>CppDoubleLiteral</c> — the C++ backend's Single/Double constant
/// spelling, culture-invariant and round-tripping.
///
/// <para>⛔ Before this fix, <c>$"{f}f"</c> used CurrentCulture and never appended a decimal point:
/// <c>Public X As Single = 400</c> emitted <c>float X = 400f;</c> — MSVC C3688, "invalid literal
/// suffix 'f'" (a float suffix needs a '.' or an exponent in front of it). <c>-0.0</c> emitted
/// <c>-0f</c>, which compiles but loses the sign (<c>1 / NZ</c> printed <c>inf</c>, not
/// <c>-inf</c>). NaN/±Infinity emitted <c>NaNf</c>/<c>∞f</c>, not C++. Double had NO arm at all —
/// it fell through to a bare <c>ToString()</c>, and on de-DE <c>V = 2.5</c> emitted
/// <c>V = 2,5;</c>, the COMMA OPERATOR: the build succeeded and V held 2, a silent miscompile.</para>
/// </summary>
[TestFixture]
public class CppFloatLiteralTests
{
    // ---- table-driven: CppFloatLiteral -----------------------------------------------------

    [TestCase(400f, "400.0f")]
    [TestCase(0f, "0.0f")]
    [TestCase(5f, "5.0f")]
    [TestCase(2.5f, "2.5f")]
    [TestCase(-3.75f, "-3.75f")]
    [TestCase(-400f, "-400.0f")]
    [TestCase(16777216f, "16777216.0f")]
    [TestCase(1e20f, "1E+20f")]
    public void CppFloatLiteral_KnownValues(float value, string expected)
    {
        Assert.That(CppCodeGenerator.CppFloatLiteral(value), Is.EqualTo(expected));
    }

    [Test]
    public void CppFloatLiteral_NegativeZero_KeepsItsSign()
    {
        // A bit pattern check, not a string-equality one on the input side: -0f as a C# literal
        // and 0f negated must be the same bit pattern the implementation is handed.
        Assert.That(BitConverter.SingleToInt32Bits(-0f), Is.Not.EqualTo(BitConverter.SingleToInt32Bits(0f)));
        Assert.That(CppCodeGenerator.CppFloatLiteral(-0f), Is.EqualTo("-0.0f"));
    }

    [Test]
    public void CppFloatLiteral_ExtremeValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppCodeGenerator.CppFloatLiteral(float.MaxValue), Is.EqualTo("3.4028235E+38f"));
            Assert.That(CppCodeGenerator.CppFloatLiteral(float.Epsilon), Is.EqualTo("1E-45f"));
        });
    }

    [Test]
    public void CppFloatLiteral_NaNAndInfinities_AreNumericLimitsExpressions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppCodeGenerator.CppFloatLiteral(float.NaN),
                Is.EqualTo("std::numeric_limits<float>::quiet_NaN()"));
            Assert.That(CppCodeGenerator.CppFloatLiteral(float.PositiveInfinity),
                Is.EqualTo("std::numeric_limits<float>::infinity()"));
            Assert.That(CppCodeGenerator.CppFloatLiteral(float.NegativeInfinity),
                Is.EqualTo("-std::numeric_limits<float>::infinity()"));
        });
    }

    // ---- table-driven: CppDoubleLiteral -----------------------------------------------------

    [TestCase(400d, "400.0")]
    [TestCase(2.5d, "2.5")]
    [TestCase(1e15d, "1000000000000000.0")]
    public void CppDoubleLiteral_KnownValues(double value, string expected)
    {
        Assert.That(CppCodeGenerator.CppDoubleLiteral(value), Is.EqualTo(expected));
    }

    [Test]
    public void CppDoubleLiteral_NegativeZero_KeepsItsSign()
    {
        Assert.That(CppCodeGenerator.CppDoubleLiteral(-0.0), Is.EqualTo("-0.0"));
    }

    [Test]
    public void CppDoubleLiteral_OneThird_IsTheShortestRoundTripDigits()
    {
        Assert.That(CppCodeGenerator.CppDoubleLiteral(1.0 / 3), Is.EqualTo("0.3333333333333333"));
    }

    [Test]
    public void CppDoubleLiteral_ExtremeValues()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppCodeGenerator.CppDoubleLiteral(double.MaxValue),
                Is.EqualTo("1.7976931348623157E+308"));
            Assert.That(CppCodeGenerator.CppDoubleLiteral(double.Epsilon), Is.EqualTo("5E-324"));
        });
    }

    [Test]
    public void CppDoubleLiteral_NaNAndInfinities_AreNumericLimitsExpressions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppCodeGenerator.CppDoubleLiteral(double.NaN),
                Is.EqualTo("std::numeric_limits<double>::quiet_NaN()"));
            Assert.That(CppCodeGenerator.CppDoubleLiteral(double.PositiveInfinity),
                Is.EqualTo("std::numeric_limits<double>::infinity()"));
            Assert.That(CppCodeGenerator.CppDoubleLiteral(double.NegativeInfinity),
                Is.EqualTo("-std::numeric_limits<double>::infinity()"));
        });
    }

    // ---- round-trip: bits, not strings ------------------------------------------------------

    /// <summary>
    /// A finite float's literal, with the trailing "f" stripped, must parse back under
    /// InvariantCulture to the SAME bit pattern — checked via
    /// <see cref="BitConverter.SingleToInt32Bits"/> rather than <c>==</c> so a sign-losing
    /// round-trip of <c>-0.0</c> (which is <c>== 0.0</c>) cannot pass by accident.
    /// </summary>
    [TestCaseSource(nameof(RoundTripFloatValues))]
    public void CppFloatLiteral_RoundTripsBits(float value)
    {
        var literal = CppCodeGenerator.CppFloatLiteral(value);
        Assert.That(literal, Does.EndWith("f"), literal);
        var numeric = literal.Substring(0, literal.Length - 1);
        var parsed = float.Parse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture);
        Assert.That(BitConverter.SingleToInt32Bits(parsed), Is.EqualTo(BitConverter.SingleToInt32Bits(value)),
            $"{value:R} -> {literal} -> {parsed:R}");
    }

    /// <summary>Same property for Double, via <see cref="BitConverter.DoubleToInt64Bits"/>.</summary>
    [TestCaseSource(nameof(RoundTripDoubleValues))]
    public void CppDoubleLiteral_RoundTripsBits(double value)
    {
        var literal = CppCodeGenerator.CppDoubleLiteral(value);
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

        // A deterministic spread of arbitrary bit patterns, finite ones only — NaN/Infinity are
        // pinned separately above, and their sign/payload is explicitly not chased (see
        // CppFloatLiteral's doc comment).
        var rnd = new Random(12345);
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
        yield return double.Epsilon;
        yield return 1.0 / 3;

        var rnd = new Random(67890);
        for (int i = 0; i < 50; i++)
        {
            var bits = ((long)rnd.Next() << 32) | (uint)rnd.Next();
            var value = BitConverter.Int64BitsToDouble(bits);
            if (double.IsFinite(value)) yield return value;
        }
    }

    // ---- culture independence ---------------------------------------------------------------

    private static void AssertInvariantSpelling()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CppCodeGenerator.CppFloatLiteral(400f), Is.EqualTo("400.0f"));
            Assert.That(CppCodeGenerator.CppFloatLiteral(2.5f), Is.EqualTo("2.5f"));
            Assert.That(CppCodeGenerator.CppFloatLiteral(-3.75f), Is.EqualTo("-3.75f"));
            Assert.That(CppCodeGenerator.CppDoubleLiteral(400d), Is.EqualTo("400.0"));
            Assert.That(CppCodeGenerator.CppDoubleLiteral(2.5d), Is.EqualTo("2.5"));
            Assert.That(CppCodeGenerator.CppDoubleLiteral(-3.75d), Is.EqualTo("-3.75"));
        });
    }

    /// <summary>
    /// ⛔ Before the fix, de-DE (comma decimal separator) turned <c>2.5</c> into <c>2,5</c> —
    /// invalid as a float literal, and for Double the COMMA OPERATOR: a silent miscompile, not a
    /// build failure. <c>[SetCulture]</c> restores the culture afterwards.
    /// </summary>
    [Test]
    [SetCulture("de-DE")]
    public void UnderDeDECulture_OutputIsStillInvariant() => AssertInvariantSpelling();

    /// <summary>
    /// ⛔ Before the fix, sv-SE's negative sign is U+2212 (minus sign), not ASCII '-' — not a C++
    /// token at all. Same table as the de-DE case, byte-identical to invariant.
    /// </summary>
    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_OutputIsStillInvariant() => AssertInvariantSpelling();

    // ---- emitted text: combined-mode fields, globals, special values -----------------------

    [TestCase("400", "float X = 400.0f;")]
    [TestCase("0", "float X = 0.0f;")] // the commonest initializer the old code ever saw — pin it explicitly
    [TestCase("5.0", "float X = 5.0f;")]
    [TestCase("2.5", "float X = 2.5f;")]
    [TestCase("-400", "float X = -400.0f;")]
    public void SingleClassField_EmitsAValidLiteral(string vbLiteral, string expectedLine)
    {
        var cpp = BclE2E.CompileToCppOptimized($$"""
            Class Box
             Public X As Single = {{vbLiteral}}
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain(expectedLine), cpp);
    }

    [Test]
    public void SingleClassField_NegativeZero_EmitsSignedLiteral()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public X As Single = -0.0
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("float X = -0.0f;"), cpp);
    }

    [TestCase("400", "double D = 400.0;")]
    [TestCase("-0.0", "double D = -0.0;")]
    public void DoubleClassField_EmitsAValidLiteral(string vbLiteral, string expectedLine)
    {
        var cpp = BclE2E.CompileToCppOptimized($$"""
            Class Box
             Public D As Double = {{vbLiteral}}
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain(expectedLine), cpp);
    }

    [Test]
    public void ModuleScopeGlobal_SingleInitializer_EmitsAValidLiteral()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Dim Gravity As Single = 10.0

            Sub Main()
            End Sub
            """);

        Assert.That(cpp, Does.Contain("Gravity = 10.0f;"), cpp);
    }

    [Test]
    public void ModuleScopeConst_SingleInitializer_EmitsAValidLiteral()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Public Const MaxSpeed As Single = 12

            Sub Main()
            End Sub
            """);

        Assert.That(cpp, Does.Contain("MaxSpeed = 12.0f;"), cpp);
    }

    /// <summary>
    /// The lexer only accepts an exponent after a '.', so ∞/NaN cannot be written as a VB source
    /// literal directly — these are the smallest expressions that fold to them at compile time
    /// (the IR optimizer's constant folding), the same recipes the implementer measured.
    /// </summary>
    [Test]
    public void DoubleField_PositiveInfinity_EmitsNumericLimitsExpression()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public D As Double = 1.0E+300 * 1.0E+300
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("std::numeric_limits<double>::infinity()"), cpp);
    }

    [Test]
    public void DoubleField_NegativeInfinity_EmitsNumericLimitsExpression()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public D As Double = -1.0E+300 * 1.0E+300
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("-std::numeric_limits<double>::infinity()"), cpp);
    }

    [Test]
    public void DoubleField_NaN_EmitsNumericLimitsExpression()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public D As Double = (1.0E+300 * 1.0E+300) - (1.0E+300 * 1.0E+300)
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("std::numeric_limits<double>::quiet_NaN()"), cpp);
    }

    [Test]
    public void SingleField_NaN_EmitsNumericLimitsExpression()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public F As Single = (1.0E+30f * 1.0E+30f) - (1.0E+30f * 1.0E+30f)
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("std::numeric_limits<float>::quiet_NaN()"), cpp);
    }

    [Test]
    public void SingleLocal_PositiveInfinity_EmitsNumericLimitsExpression()
    {
        // Field-initializer folding does not apply inside a function body the same way, so
        // +Infinity for Single is pinned via a local, matching the implementer's own recipe.
        var cpp = BclE2E.CompileToCppOptimized("""
            Sub Main()
             Dim a As Single = 1.0E+30f
             Dim b As Single = a * a
            End Sub
            """);

        Assert.That(cpp, Does.Contain("std::numeric_limits<float>::infinity()"), cpp);
    }

    [Test]
    public void CombinedEmission_IncludesLimitsHeader()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public D As Double = 1.0
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.That(cpp, Does.Contain("#include <limits>"), cpp);
    }

    // ---- de-DE end-to-end through the optimizer pipeline ------------------------------------

    /// <summary>
    /// ⛔ The headline regression: before the fix, this emitted <c>double V = 2,5;</c> — valid
    /// C++ (the comma operator evaluates <c>2</c> then discards it), so the program BUILT and V
    /// held 2, not 2.5. Culture is applied to the WHOLE compile via <c>[SetCulture]</c> so the
    /// pin covers the same path a de-DE machine would actually take.
    /// </summary>
    [Test]
    [SetCulture("de-DE")]
    public void UnderDeDECulture_DoubleFieldNeverEmitsACommaLiteral()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public V As Double = 2.5
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("double V = 2.5;"), cpp);
            Assert.That(cpp, Does.Not.Contain("2,5"), cpp);
        });
    }

    // ---- integer/long/enum invariance under sv-SE (U+2212 minus) ---------------------------

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_IntegerFieldNeverEmitsAUnicodeMinus()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public NegI As Integer = -7
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("int32_t NegI = -7;"), cpp);
            Assert.That(cpp, Does.Not.Contain("−"), cpp);
        });
    }

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_LongFieldNeverEmitsAUnicodeMinus()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Class Box
             Public NegL As Long = -9
            End Class

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("-9LL"), cpp);
            Assert.That(cpp, Does.Not.Contain("−"), cpp);
        });
    }

    [Test]
    [SetCulture("sv-SE")]
    public void UnderSvSECulture_EnumMemberValueNeverEmitsAUnicodeMinus()
    {
        var cpp = BclE2E.CompileToCppOptimized("""
            Enum Dir
             Forward = 0
             Back = -1
            End Enum

            Module M
             Sub Main()
             End Sub
            End Module
            """);

        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("Back = -1"), cpp);
            Assert.That(cpp, Does.Not.Contain("−"), cpp);
        });
    }

    // ---- split emission: BasicLangRuntime.g.h must also carry <limits> ---------------------

    /// <summary>
    /// ⛔ MSVC's STL pulls in &lt;limits&gt; transitively through the other std headers it already
    /// includes (measured), so a compile-and-run test CANNOT tell whether the explicit include is
    /// actually there — only reading the generated text can. Split mode has its own, separate
    /// include set (<c>EmitRuntimeHeader</c>), so it needs its own pin.
    /// </summary>
    [Test]
    public void SplitEmission_RuntimeHeader_IncludesLimits()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bl-float-lit-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var path = System.IO.Path.Combine(dir, "Logic.bas");
            System.IO.File.WriteAllText(path, "Sub Main()\n    PrintLine \"hi\"\nEnd Sub");
            var compiler = new BasicLang.Compiler.BasicCompiler(
                new BasicLang.Compiler.CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(new[] { path });
            Assert.That(result.Success, Is.True, string.Join("; ", result.AllErrors.Select(e => e.Message)));

            var gen = new CppCodeGenerator();
            var split = gen.GenerateSplit(result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain: true);

            Assert.That(split.Files["BasicLangRuntime.g.h"], Does.Contain("#include <limits>"),
                split.Files["BasicLangRuntime.g.h"]);
        }
        finally { try { System.IO.Directory.Delete(dir, true); } catch { } }
    }
}
