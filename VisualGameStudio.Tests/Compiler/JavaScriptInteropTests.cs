using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class JavaScriptInteropTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BasicLang_JsInterop_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Test]
    public void JsImport_IsCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(module.JsImports, Is.EqualTo(new[] { "./chart.js" }));
    }

    /// <summary>The directive name is matched case-insensitively, like every other one.</summary>
    [Test]
    public void JsImport_MixedCaseDirective_IsCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#jsimport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(module.JsImports, Is.EqualTo(new[] { "./a.js" }));
    }

    /// <summary>
    /// An import gated behind an inactive conditional must not be collected.
    ///
    /// ⛔ #IfDef, NOT #If. The Preprocessor implements #IfDef/#IfNDef/#Else/#EndIf; `#If` is a
    /// LEXER/parser construct and never reaches the directive collector, so a test written
    /// with #If fails through JsTestSupport's parse guard no matter how correct the
    /// implementation is.
    /// </summary>
    [Test]
    public void JsImport_InsideInactiveConditional_IsNotCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#IfDef NEVER_DEFINED\n#JsImport \"./nope.js\"\n#EndIf\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        Assert.That(module.JsImports, Is.Empty);
    }

    /// <summary>
    /// The directive must be COMMENTED OUT, not removed. A source map built from
    /// IRInstruction.SourceLine is off by one for the whole file otherwise — exactly the
    /// .mod/.cls off-by-one class of bug this backend already had to fix once.
    /// </summary>
    [Test]
    public void JsImport_PreservesLineNumbers()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
            runPreprocessor: true, sourceFilePath: "prog.bas");

        var lines = module.Functions.Single(f => f.Name == "Main")
            .Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.SourceLine > 0).Select(i => i.SourceLine).ToList();

        Assert.That(lines, Does.Contain(3), "Console.WriteLine is on source line 3");
    }

    // ------------------------------------------------------------------
    // Rejections. All of these surface through JsTestSupport's preprocess
    // guard, which THROWS rather than returning the diagnostics.
    // ------------------------------------------------------------------

    /// <summary>
    /// Assert the preprocessor rejected a directive, by MESSAGE and not merely by "something
    /// threw".
    ///
    /// <para>⛔ <c>Throws.Exception</c> is a false green here. BuildModule throws from four
    /// separate places (preprocess, the parse guard, semantic analysis, and its own broken-test
    /// guard), so a bare Throws.Exception is satisfied by the exact OPPOSITE of the behaviour
    /// under test: drop the comment-out step and the raw <c>#JsImport ...</c> line reaches the
    /// lexer, the parse guard throws, and the test stays green while both the syntax rejection
    /// AND line preservation are broken. Matching the message pins it to the preprocessor.
    /// Same reasoning as CppPassthroughTests' <c>Does.Contain("Invalid #CppInclude")</c>.</para>
    /// </summary>
    private static void AssertRejected(string source, string expectedMessageFragment) =>
        Assert.That(() => JsTestSupport.BuildModule(source, runPreprocessor: true),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.Contains(expectedMessageFragment));

    /// <summary>A bare path is not a module specifier — JavaScript specifiers are quoted.</summary>
    [Test]
    public void JsImport_WithoutQuotes_IsAnError()
        => AssertRejected("#JsImport ./a.js\nSub Main()\nEnd Sub", "Invalid #JsImport syntax");

    /// <summary>
    /// There are no angle-bracket module specifiers in JavaScript, so <c>&lt;./a.js&gt;</c> is a
    /// mistake rather than the alternate spelling it is for #CppInclude — which DOES accept both
    /// forms, because &lt;x&gt; and "x" mean different things to a C++ compiler.
    /// </summary>
    [Test]
    public void JsImport_AngleBrackets_IsAnError()
        => AssertRejected("#JsImport <./a.js>\nSub Main()\nEnd Sub", "Invalid #JsImport syntax");

    /// <summary>
    /// An empty specifier is rejected, not collected as "". The capture group is <c>[^"]+</c>
    /// rather than <c>[^"]*</c> deliberately; a <c>*</c> would emit <c>import "";</c>.
    /// </summary>
    [Test]
    public void JsImport_EmptySpecifier_IsAnError()
        => AssertRejected("#JsImport \"\"\nSub Main()\nEnd Sub", "Invalid #JsImport syntax");

    /// <summary>
    /// A Windows-style path is the one bad specifier that would otherwise produce no diagnostic
    /// at all: it collects, it escapes cleanly at emission, and it 404s at run time.
    /// </summary>
    [Test]
    public void JsImport_BackslashSpecifier_IsAnError()
        => AssertRejected("#JsImport \"..\\lib\\chart.js\"\nSub Main()\nEnd Sub",
            "forward slashes, not backslashes");

    // ------------------------------------------------------------------
    // END-TO-END through the REAL compiler.
    //
    // ⛔ Every test above routes through JsTestSupport.BuildModule, which HAND-COPIES
    // pre.JsImports onto the module it builds. Delete BOTH
    // `result.CombinedIR.JsImports.AddRange(_preprocessor.JsImports)` lines in Compiler.cs
    // and every one of them still passes — they read as end-to-end and are not. The two
    // tests below are the only thing holding those joins down, one per route, because the
    // single-file and project routes each have their OWN copy of the wiring and nothing but
    // a comment keeps them in step. This is the gap #CppInclude closed on purpose; see
    // CppPassthroughTests.Cpp_CppInclude_EndToEndThroughRealCompiler_EmitsInclude.
    // ------------------------------------------------------------------

    [Test]
    public void JsImport_EndToEndThroughRealCompiler_LandsOnCombinedIR()
    {
        var path = Path.Combine(_tempDir, "Program.bas");
        File.WriteAllText(path, "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub");

        // Fresh compiler per compile — the module registry is stateful.
        var result = new BasicCompiler().CompileFile(path);

        Assert.That(result.AllErrors.Select(e => e.Message), Is.Empty);
        Assert.That(result.CombinedIR, Is.Not.Null);
        Assert.That(result.CombinedIR.JsImports, Is.EqualTo(new[] { "./chart.js" }),
            "#JsImport must survive CompileFile's CombineIRModules -> CombinedIR.JsImports join");
    }

    /// <summary>
    /// The project route (<c>build x.blproj</c> and the IDE both land here) has its own copy of
    /// the join. Specifiers from EVERY unit accumulate, because the Preprocessor instance is
    /// shared across the whole compilation and never cleared.
    /// </summary>
    [Test]
    public void JsImport_EndToEndThroughProjectRoute_LandsOnCombinedIR()
    {
        var main = Path.Combine(_tempDir, "Program.bas");
        var helper = Path.Combine(_tempDir, "Helpers.bas");
        File.WriteAllText(main, "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub");
        File.WriteAllText(helper, "#JsImport \"./util.js\"\nSub Helper()\nEnd Sub");

        var result = new BasicCompiler().CompileProjectFiles(new[] { main, helper });

        Assert.That(result.AllErrors.Select(e => e.Message), Is.Empty);
        Assert.That(result.CombinedIR, Is.Not.Null);
        Assert.That(result.CombinedIR.JsImports,
            Is.EquivalentTo(new[] { "./chart.js", "./util.js" }),
            "#JsImport must survive CompileProjectFiles' CombineIRModules -> CombinedIR.JsImports join");
    }

    // ------------------------------------------------------------------
    // EMISSION. Collecting a specifier is worth nothing until it reaches
    // the generated JavaScript as a real ES import.
    // ------------------------------------------------------------------

    [Test]
    public void JsImport_EmitsAnImportStatement()
        => Assert.That(
            JsTestSupport.Compile("#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true),
            Does.Contain("import \"./chart.js\";"));

    /// <summary>
    /// Imports go first. ESM hoists them, so this is convention and readability rather than a
    /// correctness requirement — but the output is meant to be READ in devtools, and an import
    /// buried under function declarations reads as generated sludge.
    /// </summary>
    [Test]
    public void JsImport_ImportsPrecedeDeclarations()
    {
        var lines = JsTestSupport
            .Compile("#JsImport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true)
            .Replace("\r\n", "\n").Split('\n')
            .Where(l => l.Trim().Length > 0).ToList();

        var firstImport = lines.FindIndex(l => l.TrimStart().StartsWith("import "));
        var firstFunction = lines.FindIndex(l => l.TrimStart().StartsWith("function "));

        Assert.That(firstImport, Is.GreaterThanOrEqualTo(0), "no import emitted");
        // Guarded too: FindIndex returns -1 when absent, so without this `firstImport < -1`
        // fails with a message about ORDERING when the real cause is "nothing was emitted".
        Assert.That(firstFunction, Is.GreaterThanOrEqualTo(0), "no function emitted");
        Assert.That(firstImport, Is.LessThan(firstFunction));
    }

    /// <summary>Two files in one project may import the same module.</summary>
    [Test]
    public void JsImport_DeduplicatesRepeatedSpecifiers()
    {
        var js = JsTestSupport.Compile(
            "#JsImport \"./a.js\"\n#JsImport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(System.Text.RegularExpressions.Regex.Matches(js, @"import ""\./a\.js""").Count,
            Is.EqualTo(1));
    }

    /// <summary>
    /// A source map must still point at the right .bas lines with imports above the code —
    /// which it does only because Line() maintains _generatedLine.
    /// </summary>
    [Test]
    public void JsImport_DoesNotShiftSourceMapPositions()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
            runPreprocessor: true, sourceFilePath: "prog.bas");
        var generator = new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator();
        var js = generator.Generate(module).Replace("\r\n", "\n").Split('\n');

        var generatedLine = System.Array.FindIndex(js, l => l.Contains("console.log(1)"));
        Assert.That(generatedLine, Is.GreaterThanOrEqualTo(0), "no console.log emitted");

        // Reuse the existing decoder rather than asserting on the raw mappings string —
        // Does.Contain("mappings") passes on ANY source map and proves nothing.
        var pairs = JavaScriptGeneratorSourceMapTests.Decode(generator.SourceMap.ToJson("app.js"));
        var mapped = pairs.Where(p => p.generated == generatedLine).Select(p => p.source).ToList();

        Assert.That(mapped, Does.Contain(2),
            "Console.WriteLine is on source line 3 (0-based 2); imports above it must not shift it");
    }
}
