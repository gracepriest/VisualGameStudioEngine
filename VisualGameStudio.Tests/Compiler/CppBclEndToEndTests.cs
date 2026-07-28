using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 9 helper for tests that scan WHOLE generated C++ output with negative
/// assertions (Does.Not.Contain): the P1 BCL runtime bodies are now spliced into
/// EVERY generated program (both emission modes), so legitimate runtime text (e.g.
/// the ostream inserters' <c>v.ToString()</c>, or the word "Integer" in a body
/// comment) would otherwise trip scans aimed at USER-code lowering. Stripping the
/// verbatim spliced bodies keeps those assertions at full strength over the code
/// the test actually targets.
/// </summary>
internal static class CppGeneratedCode
{
    /// <summary>
    /// <paramref name="cpp"/> with the spliced P1 runtime bodies removed and line
    /// endings normalized to '\n' (the splice re-emits the consts line-by-line, so
    /// only line endings can differ from the source consts).
    /// </summary>
    internal static string WithoutBclRuntime(string cpp)
    {
        var norm = cpp.Replace("\r\n", "\n");
        norm = norm.Replace(CppBclRuntime.BclBody.Replace("\r\n", "\n"), "");
        norm = norm.Replace(CppDecimalRuntime.DecimalBody.Replace("\r\n", "\n"), "");
        return norm;
    }
}

/// <summary>
/// P1 Task 9 (spec §12 layer 1 + splice smokes): the native BCL runtime
/// (bl_bcltypes + bl_decimal bodies) is spliced UNCONDITIONALLY into BOTH emission
/// modes — the fast pins below guard presence without a compiler (the classic
/// both-modes drift trap), and the Integration smokes prove the spliced headers
/// actually compile and run inside a generated program in each mode. Task 11
/// extends this fixture with the per-type BL end-to-end battery.
/// </summary>
[TestFixture]
public class CppBclEndToEndTests
{
    // ------------------------------------------------------------------
    // Shared compile helpers (same idiom as CppCollectionTests).
    // ------------------------------------------------------------------

    /// <summary>Compile BL source to combined-mode C++ (no optimizer).</summary>
    private static string CompileToCpp(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");
        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
    }

    /// <summary>
    /// Compile BL source to combined-mode C++ THROUGH the standard optimizer passes —
    /// the CLI-equivalent path (repo law: validate codegen via the optimizer, not only
    /// the non-optimizing helper).
    /// </summary>
    private static string CompileToCppOptimized(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var ast = new Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        Assert.That(analyzer.Analyze(ast), Is.True,
            string.Join("; ", analyzer.Errors.Select(e => e.Message)));
        var irModule = new IRBuilder(analyzer).Build(ast, "TestModule");

        var pipeline = new BasicLang.Compiler.IR.Optimization.OptimizationPipeline();
        pipeline.AddStandardPasses();
        pipeline.Run(irModule);

        return new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false })
            .Generate(irModule);
    }

    /// <summary>Front half mirrors CppSplitEmissionTests.Split: real frontend, then GenerateSplit.</summary>
    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-bcl-splice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.name); File.WriteAllText(p, f.code); return p; }).ToList();
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            return new CppCodeGenerator().GenerateSplit(
                result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // ------------------------------------------------------------------
    // FAST both-modes pins (no compiler needed) — spec §12 layer 1.
    // ------------------------------------------------------------------

    /// <summary>
    /// Combined mode: a trivial program's single-string output carries the spliced
    /// native BCL runtime (both header bodies), unconditionally.
    /// </summary>
    [Test]
    public void Combined_TrivialProgram_CarriesSplicedBclRuntime()
    {
        var output = CompileToCpp("Sub Main()\n    Console.WriteLine(\"hi\")\nEnd Sub");
        Assert.That(output, Does.Contain("struct DateTime"));
        Assert.That(output, Does.Contain("struct Decimal"));
    }

    /// <summary>
    /// Split mode: BasicLangRuntime.g.h carries the spliced native BCL runtime —
    /// the both-modes drift guard for the combined pin above.
    /// </summary>
    [Test]
    public void Split_RuntimeHeader_CarriesSplicedBclRuntime()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"));
        var rt = r.Files["BasicLangRuntime.g.h"];
        Assert.That(rt, Does.Contain("struct DateTime"));
        Assert.That(rt, Does.Contain("struct Decimal"));
    }

    // ------------------------------------------------------------------
    // Integration splice smokes: the spliced headers must COMPILE (and run)
    // inside a generated program, in both emission modes.
    // ------------------------------------------------------------------

    /// <summary>
    /// Combined mode through the OPTIMIZER (CLI-equivalent path): a trivial program
    /// now carries the full spliced BCL runtime and must still compile and run —
    /// this is what catches generator-owned include-set gaps and any collision
    /// between the runtime bodies and the always-emitted preamble/user code.
    /// </summary>
    [Test, Category("Integration")]
    public void Combined_TrivialProgram_WithSplicedRuntime_CompilesAndRuns()
    {
        var output = CompileToCppOptimized("Sub Main()\n    Console.WriteLine(\"bcl-splice-ok\")\nEnd Sub");

        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");
        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRun(output, compiler.Value).Replace("\r\n", "\n");

        Assert.That(stdout, Is.EqualTo("bcl-splice-ok\n"));
    }

    /// <summary>
    /// Split mode: the runtime header (now carrying the BCL bodies) is included by
    /// multiple translation units — compiling and linking them together proves the
    /// splice is ODR-safe (every out-of-class definition in the bodies is inline)
    /// and that the split include set covers the bodies' needs.
    /// </summary>
    [Test, Category("Integration")]
    public void Split_TrivialProgram_WithSplicedRuntime_CompilesAndRuns()
    {
        var compiler = VisualGameStudio.Tests.Native.CppCompile.FindRunCompiler();
        if (compiler == null) Assert.Ignore("No C++ compiler available on this machine");

        var r = Split(emitMain: true,
            ("Logic.bas", "Function Score() As Integer\n    Return 7\nEnd Function"),
            ("App.bas", "Sub Main()\n    PrintLine Score()\nEnd Sub"));

        var stdout = VisualGameStudio.Tests.Native.CppCompile
            .CompileAndRunFiles(r.Files, r.TranslationUnitFileNames, compiler.Value);

        Assert.That(stdout.Trim(), Is.EqualTo("7"));
    }
}
