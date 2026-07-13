using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Native;

/// <summary>
/// Task 2 of C++ Phase 2 (mixed projects): the split emission
/// (<see cref="CppCodeGenerator.GenerateSplit"/>) must survive a REAL C++ toolchain —
/// multiple translation units compiled together, headers included by more than one TU
/// (ODR), and the Phase 2 headline direction: hand-written user C++ calling BasicLang
/// through the generated shim header.
///
/// Each test compiles real .bas sources through <c>CompileProjectFiles</c>, splits, writes
/// every generated file to a temp dir, compiles all TranslationUnitFileNames (plus any
/// hand-written consumer TU) on one command line, runs the exe, and asserts stdout.
/// Cleanly Ignored when no C++ compiler (clang++/g++/MSVC) is available.
/// </summary>
[TestFixture]
[NonParallelizable] // spawns compiler + child processes
public class CppSplitCompileTests
{
    private (string exe, string argsTemplate)? _compiler;

    [OneTimeSetUp]
    public void ProbeCompiler() => _compiler = CppCompile.FindRunCompiler();

    private (string exe, string argsTemplate) RequireCompiler()
    {
        if (_compiler is null)
            Assert.Ignore("No C++ compiler (clang++/g++/MSVC) available; skipping split compile test.");
        return _compiler!.Value;
    }

    /// <summary>Front half mirrors CppSplitEmissionTests.Split: real frontend, then GenerateSplit.</summary>
    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-splitc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = files.Select(f => { var p = Path.Combine(dir, f.name); File.WriteAllText(p, f.code); return p; }).ToList();
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(paths);
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));
            var gen = new CppCodeGenerator();
            return gen.GenerateSplit(result.CombinedIR, "Game", result.Units.Select(u => u.IR).ToList(), emitMain);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Test]
    public void Split_MultiModuleProgram_CompilesAndRuns()
    {
        var compiler = RequireCompiler();
        var r = Split(emitMain: true,
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function"),
            ("App.bas", "Sub Main()\n    PrintLine CalculateScore(3)\nEnd Sub"));

        var stdout = CppCompile.CompileAndRunFiles(r.Files, r.TranslationUnitFileNames, compiler);

        Assert.That(stdout, Does.Contain("30"));
    }

    [Test]
    public void Split_DirectionB_UserCppCallsBasicLang()
    {
        // THE Phase 2 headline: a hand-written C++ TU includes the generated per-module shim
        // header and calls a BasicLang function at global scope (D10: no namespace). BasicLang
        // provides no Main; the user C++ owns the entry point.
        var compiler = RequireCompiler();
        var r = Split(emitMain: false,
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function"));
        Assert.That(r.HasBasicLangMain, Is.False);

        var files = new Dictionary<string, string>(r.Files, StringComparer.OrdinalIgnoreCase)
        {
            ["consumer.cpp"] =
                "#include \"Logic.g.h\"\n" +
                "\n" +
                "int main() {\n" +
                "    std::cout << CalculateScore(4) << std::endl;\n" +
                "    return 0;\n" +
                "}\n"
        };
        var tus = r.TranslationUnitFileNames.Append("consumer.cpp");

        var stdout = CppCompile.CompileAndRunFiles(files, tus, compiler);

        Assert.That(stdout, Does.Contain("40"));
    }

    [Test]
    public void Split_CollectionsAsyncIterators_CompileThroughSharedRuntimeHeader()
    {
        // Collections, async Task emulation, and coroutine iterators each live in their OWN
        // module (own .g.cpp), all served by the one shared BasicLangRuntime.g.h — compiling
        // and linking together proves there is no ODR breakage in the runtime header.
        var compiler = RequireCompiler();
        var r = Split(emitMain: true,
            ("ListMod.bas",
                "Function SumList() As Integer\n" +
                "    Dim xs As New List(Of Integer)\n" +
                "    xs.Add(1)\n" +
                "    xs.Add(2)\n" +
                "    Return xs.Count\n" +
                "End Function"),
            ("AsyncMod.bas",
                "Async Function GetValueAsync() As Task(Of Integer)\n" +
                "    Return 42\n" +
                "End Function\n" +
                "\n" +
                "Async Function CallerAsync() As Task(Of Integer)\n" +
                "    Dim x As Integer = Await GetValueAsync()\n" +
                "    Return x\n" +
                "End Function"),
            ("IterMod.bas",
                "Iterator Function Numbers() As IEnumerable(Of Integer)\n" +
                "    Yield 1\n" +
                "    Yield 2\n" +
                "End Function"),
            ("App.bas",
                "Sub Main()\n" +
                "    Dim total As Integer = SumList()\n" +
                "    For Each i In Numbers()\n" +
                "        total = total + i\n" +
                "    Next\n" +
                "    PrintLine total\n" +
                "End Sub"));

        var stdout = CppCompile.CompileAndRunFiles(r.Files, r.TranslationUnitFileNames, compiler);

        // SumList() = 2 items, plus yielded 1 + 2 = 5.
        Assert.That(stdout, Does.Contain("5"));
    }

    [Test]
    public void Split_ClassAcrossModules_SharedPtrRoundTrip()
    {
        // A class defined in one module crosses a module boundary as std::shared_ptr (the
        // reference-semantics lowering) AND crosses into hand-written user C++, which creates
        // the instance itself with std::make_shared and round-trips it through a BasicLang
        // function. The consumer C++ below matches the emitted API surface exactly:
        //   class Player { public: std::string Name; ... };            (public field, default-constructible)
        //   std::string Describe(std::shared_ptr<Player> p);           (global scope, D10)
        var compiler = RequireCompiler();
        var r = Split(emitMain: false,
            ("Models.bas",
                "Class Player\n" +
                "    Public Name As String\n" +
                "    Function Tag() As String\n" +
                "        Return Name\n" +
                "    End Function\n" +
                "End Class"),
            ("Logic.bas",
                "Function Describe(p As Player) As String\n" +
                "    Return \"Player: \" & p.Name\n" +
                "End Function"));

        var files = new Dictionary<string, string>(r.Files, StringComparer.OrdinalIgnoreCase)
        {
            ["consumer.cpp"] =
                "#include \"Game.g.h\"\n" +
                "\n" +
                "int main() {\n" +
                "    auto p = std::make_shared<Player>();\n" +
                "    p->Name = \"Rex\";\n" +
                "    std::cout << Describe(p) << std::endl;\n" +
                "    std::cout << p->Tag() << std::endl;\n" +
                "    return 0;\n" +
                "}\n"
        };
        var tus = r.TranslationUnitFileNames.Append("consumer.cpp");

        var stdout = CppCompile.CompileAndRunFiles(files, tus, compiler);

        Assert.That(stdout, Does.Contain("Player: Rex"));
        Assert.That(stdout, Does.Contain("Rex"));
    }
}
