using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using VisualGameStudio.Tests.Native;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Single/Double as text on the C++ backend must be .NET's invariant <c>ToString()</c>: the
/// SHORTEST digits that round-trip, E-notation from exponent 17 (Double) / 9 (Single) up or -5
/// down, "NaN" and "Infinity". MEASURED on master 96cd93f, .NET first:
/// <list type="bullet">
/// <item><c>CStr</c>: 5 vs <c>5.000000</c>; 0.30000000000000004 vs <c>0.300000</c>; 5E-07 vs
/// <c>0.000000</c> (the value lost); 5E+20 vs <c>500000000000000000000.000000</c>; Infinity/NaN vs
/// <c>inf</c>/<c>-nan</c> — <c>std::to_string</c> is printf <c>%f</c>.</item>
/// <item><c>"b=" &amp; b</c> and <c>CType(b, String)</c> did not compile ("no match for
/// 'operator+'") — deliberately, until a correct formatter existed.</item>
/// <item><c>Console.WriteLine(b)</c> went through <c>cout</c>: six significant digits.</item>
/// <item><c>StringBuilder.Append(1.5)</c> gave <c>1.500000</c>.</item>
/// </list>
/// </summary>
public class CppDoubleFormattingTests
{
    internal const string Program = @"
Function Div(a As Double, b As Double) As Double
    Return a / b
End Function
Sub Main()
    Dim a As Double = 5.0
    Dim b As Double = 5.1
    Dim c As Double = 0.1
    Dim s As Single = 2.5F
    Dim t As Single = 0.1F
    Console.WriteLine(CStr(a))
    Console.WriteLine(CStr(b))
    Console.WriteLine(CStr(c + 0.2))
    Console.WriteLine(CStr(Div(1.0, 3.0)))
    Console.WriteLine(CStr(Div(1.0, 0.0)))
    Console.WriteLine(CStr(Div(-1.0, 0.0)))
    Console.WriteLine(CStr(Div(0.0, 0.0)))
    Console.WriteLine(CStr(a * 1.0E20))
    Console.WriteLine(CStr(a * 1.0E-7))
    Console.WriteLine(CStr(a * 1.0E16))
    Console.WriteLine(CStr(a * 2.0E16))
    Console.WriteLine(CStr(c * 0.001))
    Console.WriteLine(CStr(c * 0.0001))
    Console.WriteLine(CStr(-b * 0.0))
    Console.WriteLine(CStr(-b))
    Console.WriteLine(CStr(s))
    Console.WriteLine(CStr(t))
    Console.WriteLine(CStr(s * 400000000.0F))
    Console.WriteLine(CStr(s * 4000000000.0F))
    Console.WriteLine(""b="" & CStr(b))
    Console.WriteLine(""b="" & b)
    Console.WriteLine(CType(b, String))
    Console.WriteLine(b.ToString())
    Console.WriteLine(b)
    Console.WriteLine(s)
    Console.WriteLine(Div(1.0, 3.0))
End Sub
";

    /// <summary>What .NET prints for <see cref="Program"/> — taken from the C# backend's run.</summary>
    internal static readonly string Expected = string.Join("\n",
        "5", "5.1", "0.30000000000000004", "0.3333333333333333",
        "Infinity", "-Infinity", "NaN",
        "5E+20", "5E-07", "50000000000000000", "1E+17", "0.0001", "1E-05", "-0",
        "-5.1", "2.5", "0.1", "1E+09", "1E+10",
        "b=5.1", "b=5.1", "5.1", "5.1", "5.1", "2.5", "0.3333333333333333");

    [Test]
    public void EveryTextSite_UsesTheShortestRoundTripFormatter()
    {
        var cpp = CppGeneratedCode.WithoutBclRuntime(BclE2E.CompileToCppOptimized(Program));
        Assert.Multiple(() =>
        {
            Assert.That(cpp, Does.Contain("BasicLang::FormatDouble("));
            Assert.That(cpp, Does.Contain("BasicLang::FormatSingle("));
            Assert.That(cpp, Does.Not.Match(@"to_string\((b|s|t|a)\)"), "a float reached std::to_string:\n" + cpp);
        });
    }

    /// <summary>The C# backend is the oracle for <see cref="Expected"/> — pin that it still agrees.</summary>
    [Test, Category("Integration"), NonParallelizable]
    public void Expected_IsWhatDotNetPrints() =>
        Assert.That(FourBackends.Norm(FourBackends.RunEmittedCSharp(Program)), Is.EqualTo(Expected));

    [Test, Category("Integration")]
    public void Cpp_PrintsWhatDotNetPrints() =>
        Assert.That(FourBackends.Norm(BclE2E.CompileRun(BclE2E.CompileToCppOptimized(Program))), Is.EqualTo(Expected));

    /// <summary>
    /// The runtime formatter against .NET itself, value by value: 4,000 random Double bit
    /// patterns and 4,000 random Single ones (NaN and Infinity included when they come up), plus
    /// the boundaries. Each line of the C++ program's output must equal .NET's invariant
    /// ToString() of the same bits.
    /// </summary>
    [Test, Category("Integration")]
    public void RuntimeFormatter_MatchesDotNet_BitForBit()
    {
        var compiler = CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var rnd = new Random(20260924);
        var doubles = new List<double>
        {
            0.0, -0.0, 1.0, 5.1, 0.1 + 0.2, 1.0 / 3, 1e16, 1e17, 9.9e16, 1e-4, 9.9e-5, 1e-5,
            double.Epsilon, double.MaxValue, double.MinValue, double.NaN,
            double.PositiveInfinity, double.NegativeInfinity, 123456789012345678.0,
        };
        for (int i = 0; i < 4000; i++)
            doubles.Add(BitConverter.Int64BitsToDouble(((long)rnd.Next() << 33) ^ ((long)rnd.Next() << 2) ^ rnd.Next(4)));

        var floats = new List<float>
        {
            0f, -0f, 2.5f, 0.1f, 999999900f, 1e9f, 1e-4f, 9.9e-5f, float.Epsilon, float.MaxValue,
            float.NaN, float.PositiveInfinity, 16777216f,
        };
        for (int i = 0; i < 4000; i++)
            floats.Add(BitConverter.Int32BitsToSingle(rnd.Next(int.MinValue, int.MaxValue)));

        var src = new StringBuilder();
        src.AppendLine("#include \"bl_bcltypes.hpp\"");
        src.AppendLine("#include <cstring>");
        src.AppendLine("#include <iostream>");
        src.AppendLine("static const unsigned long long D[] = {");
        src.AppendLine(string.Join(",\n", doubles.Select(d => $"0x{BitConverter.DoubleToInt64Bits(d):X16}ULL")));
        src.AppendLine("};");
        src.AppendLine("static const unsigned int F[] = {");
        src.AppendLine(string.Join(",\n", floats.Select(f => $"0x{BitConverter.SingleToInt32Bits(f):X8}U")));
        src.AppendLine("};");
        src.AppendLine("int main() {");
        src.AppendLine("  for (auto bits : D) { double v; std::memcpy(&v, &bits, 8); std::cout << BasicLang::FormatDouble(v) << '\\n'; }");
        src.AppendLine("  for (auto bits : F) { float v; std::memcpy(&v, &bits, 4); std::cout << BasicLang::FormatSingle(v) << '\\n'; }");
        src.AppendLine("  return 0;");
        src.AppendLine("}");

        var output = CppCompile.CompileAndRun(src.ToString(), compiler.Value,
            new[] { new KeyValuePair<string, string>("bl_bcltypes.hpp", CppBclRuntime.BclHeader) });
        var actual = output.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        var expected = doubles.Select(d => d.ToString(CultureInfo.InvariantCulture))
            .Concat(floats.Select(f => f.ToString(CultureInfo.InvariantCulture))).ToArray();

        Assert.That(actual.Length, Is.EqualTo(expected.Length), "line count");
        var mismatches = Enumerable.Range(0, expected.Length)
            .Where(i => actual[i] != expected[i])
            .Take(10)
            .Select(i => $"#{i}: .NET {expected[i]}  C++ {actual[i]}")
            .ToList();
        Assert.That(mismatches, Is.Empty, string.Join("\n", mismatches));
    }
}
