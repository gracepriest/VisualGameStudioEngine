using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Single/Double become text on the C++ backend through the runtime's
/// <c>BasicLang::FormatDouble</c>/<c>FormatSingle</c>, which print what .NET's
/// <c>ToString()</c> prints: shortest round-trip digits, E notation past 17 (Double) or 9
/// (Single) integer digits or below 1E-04, invariant NaN/Infinity.
///
/// <para>Before: <c>Console.WriteLine(d)</c> went straight to <c>cout</c> (six significant
/// digits: 0.1 + 0.2 printed "0.3", 12345.678 "12345.7", 1/0 "inf"); <c>d.ToString()</c> and
/// the <c>CStr</c> fallback used <c>std::to_string</c> ("2.500000"); and <c>"x" &amp; d</c>,
/// <c>$"{d}"</c> and <c>CType(d, String)</c> did not compile, deliberately, until a correct
/// formatter existed.</para>
/// </summary>
[TestFixture]
public class CppFloatFormattingTests
{
    /// <summary>
    /// (C++ expression, what .NET 8 prints for the same value's ToString()). Every expected
    /// string was produced by real .NET with InvariantGlobalization, not written by hand.
    /// </summary>
    private static readonly (string Expr, string Expected)[] Doubles =
    {
        ("2.5", "2.5"),
        ("0.1 + 0.2", "0.30000000000000004"),
        ("1.0 / 3.0", "0.3333333333333333"),
        ("100.0", "100"),
        ("1.0E+15", "1000000000000000"),
        ("1.0E+16", "10000000000000000"),
        ("123456789012345680.0", "1.2345678901234568E+17"),
        ("0.0001", "0.0001"),
        ("0.00001", "1E-05"),
        ("-0.0", "-0"),
        ("1.7976931348623157E+308", "1.7976931348623157E+308"),
        ("5.0E-324", "5E-324"),
        ("std::numeric_limits<double>::quiet_NaN()", "NaN"),
        ("std::numeric_limits<double>::infinity()", "Infinity"),
        ("-std::numeric_limits<double>::infinity()", "-Infinity"),
        ("1.0E+21", "1E+21"),
        ("12345.678", "12345.678"),
        ("-2.5E-7", "-2.5E-07"),
        ("1.0E+14", "100000000000000"),
        ("999999999999999.9", "999999999999999.9"),
        ("0.1", "0.1"),
        ("-1.5", "-1.5"),
    };

    private static readonly (string Expr, string Expected)[] Singles =
    {
        ("2.5f", "2.5"),
        ("0.1f", "0.1"),
        ("1.0f / 3.0f", "0.33333334"),
        ("1.0E+7f", "10000000"),
        ("1.0E+8f", "100000000"),
        ("16777216.0f", "16777216"),
        ("3.4028235E+38f", "3.4028235E+38"),
        ("1.0E-45f", "1E-45"),
        ("0.1f + 0.2f", "0.3"),
        ("123.456f", "123.456"),
    };

    [Test]
    public void TheFormatterLivesInTheAlwaysSplicedRuntime()
    {
        Assert.That(CppBclRuntime.BclBody, Does.Contain("inline std::string FormatDouble(double v)"));
        Assert.That(CppBclRuntime.BclBody, Does.Contain("inline std::string FormatSingle(float v)"));
        Assert.That(CppBclRuntime.BclIncludes, Does.Contain("#include <charconv>"));
    }

    private const string Program = @"
Function Third() As Double
    Return 1.0 / 3.0
End Function

Function FThird() As Single
    Return CSng(1.0) / CSng(3.0)
End Function

Sub Main()
    Dim d As Double = Third()
    Dim f As Single = FThird()
    Dim big As Double = 1.0E+21
    Console.WriteLine(""amp "" & d)
    Console.WriteLine(d & "" left"")
    Console.WriteLine(CStr(d))
    Console.WriteLine($""interp {d} {big}"")
    Console.WriteLine(d.ToString())
    Console.WriteLine(CType(d, String))
    Console.WriteLine(f)
    Console.WriteLine(""f "" & f)
    Console.WriteLine(f.ToString())
    Console.Write(d)
    Console.WriteLine()
    PrintLine(big)
    Console.WriteLine(0.1 + 0.2)
End Sub";

    /// <summary>What the C# backend (i.e. .NET) prints for <see cref="Program"/>.</summary>
    private const string Expected =
        "amp 0.3333333333333333\n0.3333333333333333 left\n0.3333333333333333\n" +
        "interp 0.3333333333333333 1E+21\n0.3333333333333333\n0.3333333333333333\n" +
        "0.33333334\nf 0.33333334\n0.33333334\n0.3333333333333333\n1E+21\n0.30000000000000004";

    private static string Cpp(string source) =>
        new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(InterpolatedStringLoweringTests.Optimized(source));

    [Test]
    public void EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout()
    {
        var cpp = Cpp(Program);
        var main = cpp.Substring(cpp.IndexOf("void Main()\n{", System.StringComparison.Ordinal));

        Assert.That(main, Does.Contain("BasicLang::FormatDouble("));
        Assert.That(main, Does.Contain("BasicLang::FormatSingle("));
        Assert.That(main, Does.Not.Contain("to_string("), main);
    }

    // ---------------- INTEGRATION: compile and run ----------------

    private static (string exe, string argsTemplate) Compiler()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return compiler.Value;
    }

    [Test, Category("Integration")]
    public void Formatter_MatchesDotNet_OnEveryReferenceValue()
    {
        var lines = Doubles.Select(v => $"    std::printf(\"%s\\n\", BasicLang::FormatDouble({v.Expr}).c_str());")
            .Concat(Singles.Select(v => $"    std::printf(\"%s\\n\", BasicLang::FormatSingle({v.Expr}).c_str());"));
        var src = "#include \"bl_bcltypes.hpp\"\n#include <cstdio>\n#include <limits>\nint main() {\n" +
                  string.Join("\n", lines) + "\n    return 0;\n}\n";

        var stdout = VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(src, Compiler(),
            new Dictionary<string, string> { ["bl_bcltypes.hpp"] = CppBclRuntime.BclHeader });

        var expected = Doubles.Concat(Singles).Select(v => v.Expected);
        Assert.That(stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n'), Is.EqualTo(expected));
    }

    [Test, Category("Integration")]
    public void AProgram_PrintsWhatDotNetPrints_ThroughEveryRoute()
    {
        var stdout = VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(Cpp(Program), Compiler());
        Assert.That(stdout.Replace("\r\n", "\n").TrimEnd('\n'), Is.EqualTo(Expected));
    }
}
