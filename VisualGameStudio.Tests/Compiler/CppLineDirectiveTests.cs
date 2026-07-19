using System.Text.RegularExpressions;
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend #line directive tests (Phase 4, Task 0): with
/// <c>CppCodeGenOptions.EmitLineDirectives</c> the generator maps emitted statements back to
/// their .bas source lines so a native debugger (lldb-dap) can present BasicLang source.
/// Key invariants pinned here:
///  - C++ #line filenames are ESCAPE-PROCESSED string literals (unlike C#'s) — a raw
///    Windows path ("C:\Users\...") contains \U, a malformed universal-character-name, and
///    breaks the compile. Paths must be emitted with forward slashes.
///  - Consecutive instructions on the same source line emit ONE directive (dedupe).
///  - Default options emit nothing — every other constructor site stays byte-identical.
///  - C++ has no `#line hidden`: optimizer-synthesized instructions (SourceLine == 0) are
///    re-pointed at the generated file itself instead of smearing onto the last user line.
///  - Split emission resets dedupe state per captured file, not globally.
/// </summary>
[TestFixture]
public class CppLineDirectiveTests
{
    /// <summary>
    /// Mirror of CppCollectionTests.CompileToCpp/CompileToCppOptimized, but passing a
    /// SOURCE PATH to IRBuilder.Build — the third argument is the point: the existing
    /// helpers omit it, so IRFunction.SourceFilePath is null there and #line never fires.
    /// </summary>
    private string? CompileToCpp(string source, bool emitLineDirectives, bool optimize,
        out List<string> errors, string sourcePath = @"C:\proj\Main.bas")
    {
        errors = new List<string>();

        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();

        var parser = new Parser(tokens);
        ProgramNode ast;
        try { ast = parser.Parse(); }
        catch (Exception ex) { errors.Add($"Parse error: {ex.Message}"); return null; }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule", sourcePath);

        if (optimize)
        {
            var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
            pipeline.AddStandardPasses();
            pipeline.Run(irModule);
        }

        return new CppCodeGenerator(new CppCodeGenOptions
        {
            GenerateComments = false,
            EmitLineDirectives = emitLineDirectives
        }).Generate(irModule);
    }

    /// <summary>Compile the generated C++ with a real compiler, run it, return normalized
    /// stdout. Ignores when no C++ compiler is available (CppCollectionTests idiom).</summary>
    private static string CompileRun(string cppSource)
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        return VisualGameStudio.Tests.Native.CppCompile.CompileAndRun(cppSource, compiler.Value)
            .Replace("\r\n", "\n");
    }

    [Test]
    public void Debug_EmitsForwardSlashDirective_AtTheStatementLine()
    {
        // "Dim x As Integer = 42" is on source line 2.
        var source = "Sub Main()\n    Dim x As Integer = 42\nEnd Sub";
        var output = CompileToCpp(source, emitLineDirectives: true, optimize: false, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("#line 2 \"C:/proj/Main.bas\""),
            "the statement's directive must use the exact forward-slash-normalized path");
    }

    [Test]
    public void Directives_NeverContainBackslashes()
    {
        // The \U landmine, pinned: every #line filename must be free of backslashes.
        var source = "Sub Main()\n    Dim x As Integer = 42\n    Console.WriteLine(x)\nEnd Sub";
        var output = CompileToCpp(source, emitLineDirectives: true, optimize: false, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));

        var matches = Regex.Matches(output!, "^#line \\d+ \"(.*)\"\\r?$", RegexOptions.Multiline);
        Assert.That(matches.Count, Is.GreaterThan(0), "expected at least one #line directive");
        foreach (Match m in matches)
            Assert.That(m.Groups[1].Value, Does.Not.Contain('\\'),
                $"#line filename must never contain a backslash: {m.Value}");
    }

    [Test]
    public void ConsecutiveSameLineInstructions_EmitOneDirective()
    {
        // "Dim s As String = a & b & c" (line 5) lowers to several IR instructions
        // (two concats + the store) all stamped with the same source line -> ONE directive.
        var source =
            "Sub Main()\n" +
            "    Dim a As String = \"x\"\n" +
            "    Dim b As String = \"y\"\n" +
            "    Dim c As String = \"z\"\n" +
            "    Dim s As String = a & b & c\n" +
            "    Console.WriteLine(s)\n" +
            "End Sub";
        var output = CompileToCpp(source, emitLineDirectives: true, optimize: false, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(Regex.Matches(output!, "#line 5 \"C:/proj/Main\\.bas\"").Count, Is.EqualTo(1),
            "several instructions on one source line must dedupe to a single directive");
    }

    [Test]
    public void DefaultOptions_EmitNothing()
    {
        // EmitLineDirectives defaults false: Release output and every other constructor
        // site (BackendRegistry, MultiTargetCompiler, Program demos) stay byte-identical.
        var source = "Sub Main()\n    Dim x As Integer = 42\nEnd Sub";
        var output = CompileToCpp(source, emitLineDirectives: false, optimize: false, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Not.Contain("#line"));
    }

    [Test]
    public void OptimizedIR_StillEmits_AndSynthesizedCodeResetsToTheGeneratedFile()
    {
        // "i * 8" inside the loop is strength-reduced to a shift: the optimizer REPLACES the
        // IRBinaryOp with a new instruction whose SourceLine is 0 (synthesized). C++ has no
        // `#line hidden`, so synthesized code must reset coordinates to the generated file
        // itself instead of smearing onto the last user statement.
        //
        // Text-level assertions only: strength-reduced output currently ALSO trips a
        // pre-existing, directive-independent StrengthReductionPass bug (orphaned-value
        // consumer -> undeclared temp), so this output would not compile. The directive
        // placement pinned here is correct and independent of that bug.
        var source =
            "Sub Main()\n" +
            "    Dim total As Integer = 0\n" +
            "    For i As Integer = 1 To 3\n" +
            "        total = total + i * 8\n" +
            "    Next\n" +
            "    Console.WriteLine(total)\n" +
            "End Sub";
        var output = CompileToCpp(source, emitLineDirectives: true, optimize: true, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("\"C:/proj/Main.bas\""),
            "user statements must still carry .bas directives under the optimizer");
        Assert.That(Regex.IsMatch(output!, "#line \\d+ \"TestModule\\.g\\.cpp\""), Is.True,
            "synthesized instructions must reset the line table to the generated file");
    }

    [Test]
    public void SplitEmission_ResetsDedupePerFile()
    {
        // Both modules' first statement sits on .bas line 2 (the parser rejects top-level
        // code, so line 1 is always the Sub/Function header). SAME line number, different
        // captured files: each .g.cpp must contain its OWN directive for that line —
        // dedupe state is per captured file, not global.
        var dir = Path.Combine(Path.GetTempPath(), "bl-linedir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var files = new[]
            {
                (name: "Logic.bas", code: "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"),
                (name: "Player.bas", code: "Function PlayerTag() As String\n    Return \"p1\"\nEnd Function"),
            };
            var paths = files.Select(f =>
            {
                var p = Path.Combine(dir, f.name);
                File.WriteAllText(p, f.code);
                return p;
            }).ToList();

            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True,
                string.Join("\n", result.AllErrors.Select(e => e.Message)));

            var gen = new CppCodeGenerator(new CppCodeGenOptions
            {
                GenerateComments = false,
                EmitLineDirectives = true
            });
            var r = gen.GenerateSplit(result.CombinedIR, "Game",
                result.Units.Select(u => u.IR).ToList(), emitMain: true);

            Assert.That(Regex.IsMatch(r.Files["Logic.g.cpp"], "#line 2 \"[^\"]*Logic\\.bas\""), Is.True,
                "Logic.g.cpp must carry its own #line 2 directive:\n" + r.Files["Logic.g.cpp"]);
            Assert.That(Regex.IsMatch(r.Files["Player.g.cpp"], "#line 2 \"[^\"]*Player\\.bas\""), Is.True,
                "Player.g.cpp must carry its own #line 2 directive:\n" + r.Files["Player.g.cpp"]);

            // And nothing anywhere may emit a backslashed path.
            foreach (var (fileName, content) in r.Files)
                foreach (Match m in Regex.Matches(content, "^#line \\d+ \"(.*)\"\\r?$", RegexOptions.Multiline))
                    Assert.That(m.Groups[1].Value, Does.Not.Contain('\\'),
                        $"{fileName}: #line filename must never contain a backslash: {m.Value}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Test]
    public void LineDirectiveOutput_StillCompilesAndRuns()
    {
        // Directive-laden output must remain valid C++ that a real compiler accepts, with
        // identical program behavior. total = 1 + 2 + 3 = 6.
        //
        // Deliberately no `* 8` here: strength-reducible expressions currently miscompile
        // under the optimizer REGARDLESS of directives (pre-existing StrengthReductionPass
        // bug — the replaced IRBinaryOp's consumers still reference the orphaned value,
        // yielding an undeclared temp; confirmed on master via the prebuilt CLI). The
        // optimized variant below therefore exercises directives over optimizer-rewritten
        // loop code that stays valid.
        var source =
            "Sub Main()\n" +
            "    Dim total As Integer = 0\n" +
            "    For i As Integer = 1 To 3\n" +
            "        total = total + i\n" +
            "    Next\n" +
            "    Console.WriteLine(total)\n" +
            "End Sub";

        var plain = CompileToCpp(source, emitLineDirectives: false, optimize: false, out var e1);
        Assert.That(e1, Is.Empty, string.Join("; ", e1));
        var debug = CompileToCpp(source, emitLineDirectives: true, optimize: false, out var e2);
        Assert.That(e2, Is.Empty, string.Join("; ", e2));
        var debugOptimized = CompileToCpp(source, emitLineDirectives: true, optimize: true, out var e3);
        Assert.That(e3, Is.Empty, string.Join("; ", e3));

        Assert.That(debug, Does.Contain("#line"));

        Assert.That(CompileRun(plain!), Is.EqualTo("6\n"));
        Assert.That(CompileRun(debug!), Is.EqualTo("6\n"),
            "directive-laden Debug output must compile and behave identically");
        Assert.That(CompileRun(debugOptimized!), Is.EqualTo("6\n"),
            "directive-laden optimized output must still compile and behave identically");
    }
}
