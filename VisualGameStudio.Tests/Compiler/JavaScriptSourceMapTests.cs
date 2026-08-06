using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Plan task 26 — Source Map v3.
///
/// <para><b>The plan's stated approach does not exist.</b> It says to "reuse the plumbing"
/// the C# backend threads for <c>#line</c> (CSharpBackend.cs:60-62). Those two lines are
/// private de-duplication fields on that generator; there is no cross-backend position
/// channel, and the C++ backend already keeps its own independent copy. It also says to track
/// <c>(sourceLine, sourceColumn)</c> — but the IR has NO column at all. So this is written
/// from scratch, and every segment maps to column 0.</para>
/// </summary>
[TestFixture]
public class JavaScriptSourceMapTests
{
    // ---------------------------------------------------------------- VLQ

    /// <summary>
    /// The encoder is the part with no forgiving failure mode: a wrong VLQ still decodes,
    /// just to the wrong number. These are the canonical values from the v3 spec.
    /// </summary>
    [TestCase(0, "A")]
    [TestCase(1, "C")]
    [TestCase(-1, "D")]
    [TestCase(2, "E")]
    [TestCase(-2, "F")]
    [TestCase(15, "e")]
    [TestCase(16, "gB")]
    [TestCase(-16, "hB")]
    [TestCase(123, "2H")]
    public void Vlq_EncodesKnownValues(int value, string expected)
    {
        var sb = new StringBuilder();
        JavaScriptSourceMap.EncodeVlq(sb, value);

        Assert.That(sb.ToString(), Is.EqualTo(expected));
    }

    [Test]
    public void Vlq_RoundTripsOverAWideRange()
    {
        var values = new List<int> { 0, 1, -1, 15, 16, -16, 512, -512, 100000, -100000, int.MaxValue };
        var sb = new StringBuilder();
        foreach (var v in values) JavaScriptSourceMap.EncodeVlq(sb, v);

        Assert.That(JavaScriptSourceMap.DecodeVlq(sb.ToString()), Is.EqualTo(values));
    }

    /// <summary>
    /// <c>int.MinValue</c> has no positive counterpart, so negating it in 32 bits overflows
    /// back to itself and the encoder would emit a positive number. No real line delta gets
    /// near this, but an encoder that silently flips a sign is not one to trust.
    /// </summary>
    [Test]
    public void Vlq_HandlesIntMinValue()
    {
        var sb = new StringBuilder();
        JavaScriptSourceMap.EncodeVlq(sb, int.MinValue);

        Assert.That(JavaScriptSourceMap.DecodeVlq(sb.ToString()), Is.EqualTo(new[] { int.MinValue }));
    }

    // ---------------------------------------------------------------- document

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Test]
    public void Document_HasTheRequiredV3Fields()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, "prog.bas", 1);

        var root = Parse(map.ToJson("app.js"));

        Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(3));
        Assert.That(root.GetProperty("file").GetString(), Is.EqualTo("app.js"));
        Assert.That(root.GetProperty("sources")[0].GetString(), Is.EqualTo("prog.bas"));
        Assert.That(root.GetProperty("names").GetArrayLength(), Is.Zero);
        Assert.That(root.GetProperty("mappings").GetString(), Is.Not.Empty);
    }

    /// <summary>
    /// A segment's source line is 0-based in the file and 1-based in the IR. Line 1 of the
    /// .bas must therefore encode as 0 — off by one here shifts every breakpoint and still
    /// decodes cleanly.
    /// </summary>
    [Test]
    public void Document_ConvertsOneBasedSourceLinesToZeroBased()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, "prog.bas", 1);

        var mappings = Parse(map.ToJson("app.js")).GetProperty("mappings").GetString();

        // [generatedColumn=0, sourceIndex=0, sourceLine=0, sourceColumn=0]
        Assert.That(JavaScriptSourceMap.DecodeVlq(mappings), Is.EqualTo(new[] { 0, 0, 0, 0 }));
    }

    /// <summary>
    /// IROptimizer preserves SourceLine at exactly one site, so most rewritten nodes carry 0.
    /// Those must be dropped — a segment pointing at line 0 sends the debugger to the top of
    /// the file, which reads as a wrong answer rather than as no answer.
    /// </summary>
    [Test]
    public void Document_DropsUnknownSourceLines()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, "prog.bas", 0);
        map.Add(1, 0, "prog.bas", -3);

        Assert.That(map.Count, Is.Zero);
    }

    /// <summary>
    /// THE delta asymmetry. generatedColumn resets at every line; sourceIndex and sourceLine
    /// accumulate across the whole file. Resetting all four per line yields a map that
    /// decodes without error and points at the wrong places.
    /// </summary>
    [Test]
    public void Document_ResetsColumnPerLineButAccumulatesSourceLine()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 4, "prog.bas", 10);   // generated line 0, col 4  -> source line 9
        map.Add(1, 4, "prog.bas", 11);   // generated line 1, col 4  -> source line 10

        var groups = Parse(map.ToJson("app.js")).GetProperty("mappings").GetString().Split(';');

        Assert.That(JavaScriptSourceMap.DecodeVlq(groups[0]), Is.EqualTo(new[] { 4, 0, 9, 0 }),
            "first line: absolute column 4, absolute source line 9");
        Assert.That(JavaScriptSourceMap.DecodeVlq(groups[1]), Is.EqualTo(new[] { 4, 0, 1, 0 }),
            "second line: column RESET so 4 again, but source line is a DELTA of 1");
    }

    [Test]
    public void Document_EmitsAnEmptyGroupForALineWithNoMapping()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, "prog.bas", 1);
        map.Add(2, 0, "prog.bas", 2);

        var mappings = Parse(map.ToJson("app.js")).GetProperty("mappings").GetString();

        Assert.That(mappings.Split(';').Length, Is.EqualTo(3), "line 1 must be an empty group");
        Assert.That(mappings.Split(';')[1], Is.Empty);
    }

    [Test]
    public void Document_IndexesMultipleSources()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, "a.bas", 1);
        map.Add(1, 0, "b.bas", 1);

        var root = Parse(map.ToJson("app.js"));
        Assert.That(root.GetProperty("sources").GetArrayLength(), Is.EqualTo(2));

        var second = JavaScriptSourceMap.DecodeVlq(root.GetProperty("mappings").GetString().Split(';')[1]);
        Assert.That(second[1], Is.EqualTo(1), "sourceIndex delta of 1");
    }

    /// <summary>
    /// A browser resolves `sources` against the map's own URL, so an absolute Windows path
    /// resolves to nothing servable and devtools silently shows no original source at all.
    /// </summary>
    [Test]
    public void Document_RelativisesSourcePathsAndUsesForwardSlashes()
    {
        var root = Path.Combine(Path.GetTempPath(), "site");
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, Path.Combine(root, "src", "prog.bas"), 1);

        var sources = Parse(map.ToJson("app.js", root)).GetProperty("sources")[0].GetString();

        Assert.That(sources, Is.EqualTo("src/prog.bas"));
    }

    [Test]
    public void Document_WithNoSegments_HasEmptyMappings()
        => Assert.That(Parse(new JavaScriptSourceMap().ToJson("app.js"))
            .GetProperty("mappings").GetString(), Is.Empty);

    [Test]
    public void TruncateTo_DropsLaterSegmentsOnly()
    {
        var map = new JavaScriptSourceMap();
        map.Add(0, 0, "prog.bas", 1);
        var mark = map.Count;
        map.Add(1, 0, "prog.bas", 2);
        map.Add(2, 0, "prog.bas", 3);

        map.TruncateTo(mark);

        Assert.That(map.Count, Is.EqualTo(1));
    }
}

/// <summary>
/// The generator half of task 26 — mappings taken from real compilations, including the
/// round-trip the plan asks for.
/// </summary>
[TestFixture]
public class JavaScriptGeneratorSourceMapTests
{
    /// <summary>Decodes `mappings` into (generatedLine, sourceLine) pairs, both 0-based.</summary>
    private static List<(int generated, int source)> Decode(string json)
    {
        var mappings = JsonDocument.Parse(json).RootElement.GetProperty("mappings").GetString() ?? "";
        var result = new List<(int, int)>();
        var sourceLine = 0;

        var groups = mappings.Split(';');
        for (var line = 0; line < groups.Length; line++)
        {
            if (groups[line].Length == 0) continue;
            foreach (var segment in groups[line].Split(','))
            {
                var fields = JavaScriptSourceMap.DecodeVlq(segment);
                if (fields.Count < 4) continue;
                sourceLine += fields[2];
                result.Add((line, sourceLine));
            }
        }
        return result;
    }

    private static (string js, string map) Compile(string source)
    {
        // A path is REQUIRED: with none, the generator has nothing to attribute a mapping to
        // and records none at all. The real compiler always supplies one.
        var module = JsTestSupport.BuildModule(source, sourceFilePath: "prog.bas");
        var generator = new JavaScriptCodeGenerator();
        var js = generator.Generate(module);
        return (js, generator.SourceMap.ToJson("app.js"));
    }

    /// <summary>
    /// THE ROUND-TRIP the plan asks for: a known generated line decodes back to the right
    /// .bas line. Each WriteLine sits on its own source line, so the mapping is checkable by
    /// reading the emitted JavaScript rather than by trusting an index.
    /// </summary>
    [Test]
    public void GeneratedLine_MapsBackToItsSourceLine()
    {
        //                        1                     2                     3
        var (js, map) = Compile("Sub Main()\nConsole.WriteLine(1)\nConsole.WriteLine(2)\nEnd Sub");

        var lines = js.Replace("\r\n", "\n").Split('\n');
        var pairs = Decode(map);

        foreach (var (probe, expectedSourceLine) in new[] { ("console.log(1)", 2), ("console.log(2)", 3) })
        {
            var generated = Array.FindIndex(lines, l => l.Contains(probe));
            Assert.That(generated, Is.GreaterThanOrEqualTo(0), $"'{probe}' must appear in the output");

            var mapped = pairs.Where(p => p.generated == generated).Select(p => p.source).ToList();
            Assert.That(mapped, Does.Contain(expectedSourceLine - 1),
                $"generated line {generated} ('{probe}') must map to .bas line {expectedSourceLine}");
        }
    }

    [Test]
    public void EveryMappedSourceLine_IsWithinTheFile()
    {
        var source = "Sub Main()\nDim x As Integer = 1\nIf x > 0 Then\nConsole.WriteLine(x)\nEnd If\nEnd Sub";
        var (_, map) = Compile(source);

        var lineCount = source.Split('\n').Length;
        foreach (var (_, sourceLine) in Decode(map))
            Assert.That(sourceLine, Is.InRange(0, lineCount - 1),
                "a segment points outside the source file");
    }

    /// <summary>
    /// Control-flow headers are TERMINATORS and never reach the instruction loop, so without
    /// a second recording site an `If` has no mapping and a breakpoint on it cannot bind.
    /// </summary>
    [Test]
    public void ControlFlowHeaders_AreMapped()
    {
        //                        1                     2                3                     4        5
        var (js, map) = Compile("Sub Main()\nDim x As Integer = 1\nIf x > 0 Then\nConsole.WriteLine(x)\nEnd If\nEnd Sub");

        var lines = js.Replace("\r\n", "\n").Split('\n');
        var ifLine = Array.FindIndex(lines, l => l.TrimStart().StartsWith("if ("));
        Assert.That(ifLine, Is.GreaterThanOrEqualTo(0), "the output must contain an if");

        Assert.That(Decode(map).Any(p => p.generated == ifLine), Is.True,
            "the `if` header must carry a mapping");
    }

    /// <summary>
    /// THE LAMBDA TRAP. RenderLambda renders into the shared buffer then rewinds it, so a
    /// naive implementation both keeps mappings for lines that no longer exist and leaves the
    /// line counter advanced — shifting every later mapping by the height of the lambda.
    /// Every LINQ chain contains a lambda, so this is the normal case, not an exotic one.
    /// </summary>
    [Test]
    public void LambdasDoNotCorruptLaterMappings()
    {
        var source =
            "Sub Main()\n" +                                    // 1
            "Dim l As New List(Of Integer)()\n" +               // 2
            "Dim r = l.Where(Function(x As Integer) x > 2)\n" + // 3
            "Console.WriteLine(99)\n" +                         // 4
            "End Sub";                                          // 5
        var (js, map) = Compile(source);

        var lines = js.Replace("\r\n", "\n").Split('\n');
        var marker = Array.FindIndex(lines, l => l.Contains("console.log(99)"));
        Assert.That(marker, Is.GreaterThanOrEqualTo(0));

        var mapped = Decode(map).Where(p => p.generated == marker).Select(p => p.source).ToList();
        Assert.That(mapped, Does.Contain(3),
            "the statement AFTER a lambda must still map to its own line (0-based 3 = line 4)");
    }

    [Test]
    public void Generate_ResetsTheMapBetweenRuns()
    {
        var generator = new JavaScriptCodeGenerator();
        var module = JsTestSupport.BuildModule("Sub Main()\nConsole.WriteLine(1)\nEnd Sub",
            sourceFilePath: "prog.bas");

        generator.Generate(module);
        var first = generator.SourceMap.Count;
        generator.Generate(module);

        Assert.That(generator.SourceMap.Count, Is.EqualTo(first),
            "a second Generate must not append to the first run's map");
    }
}
