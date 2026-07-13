using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CPlusPlus;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Split-emission tests for mixed BasicLang+C++ projects: GenerateSplit produces a
/// runtime header, one aggregate declarations header, per-module shim headers, and
/// per-module .g.cpp translation units (D1-D5, D11).
/// </summary>
[TestFixture]
public class CppSplitEmissionTests
{
    private static CppSplitResult Split(bool emitMain, params (string name, string code)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-split-" + Guid.NewGuid().ToString("N"));
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
    public void Split_TwoModules_EmitsRuntimeAggregateShimsAndPerModuleCpp()
    {
        var r = Split(emitMain: true,
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\nSub Main()\n    PrintLine CalculateScore(3)\nEnd Sub"),
            ("Player.bas", "Function PlayerTag() As String\n    Return \"p1\"\nEnd Function"));
        Assert.That(r.Files.Keys, Is.SupersetOf(new[] {
            "BasicLangRuntime.g.h", "Game.g.h", "Logic.g.h", "Player.g.h",
            "Logic.g.cpp", "Player.g.cpp", "Game.main.g.cpp" }));
        Assert.That(r.HasBasicLangMain, Is.True);
        Assert.That(r.ProjectHeaderFileName, Is.EqualTo("Game.g.h"));
        Assert.That(r.Files["Logic.g.h"], Does.Contain("#include \"Game.g.h\""));
        Assert.That(r.Files["Game.g.h"], Does.Contain("CalculateScore"));
        Assert.That(r.Files["Logic.g.cpp"], Does.Contain("CalculateScore"));
        Assert.That(r.Files["Player.g.cpp"], Does.Not.Contain("CalculateScore"));
        Assert.That(r.Files["Game.main.g.cpp"], Does.Contain("int main("));
        Assert.That(r.Files["Logic.g.cpp"], Does.Not.Contain("int main("));
        Assert.That(r.TranslationUnitFileNames, Is.EquivalentTo(new[] { "Logic.g.cpp", "Player.g.cpp", "Game.main.g.cpp" }));
    }

    [Test]
    public void Split_EmitMainFalse_OmitsMainTu()
    {
        var r = Split(emitMain: false, ("Logic.bas", "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"));
        Assert.That(r.HasBasicLangMain, Is.True);
        Assert.That(r.Files.Keys, Has.None.EqualTo("Game.main.g.cpp"));
        Assert.That(string.Join("|", r.Files.Values), Does.Not.Contain("int main("));
    }

    [Test]
    public void Split_HeadersHaveNoMainAndCppsHaveNoPragmaOnce()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\nEnd Sub"));
        foreach (var (name, content) in r.Files)
        {
            if (name.EndsWith(".g.h"))
            {
                Assert.That(content, Does.StartWith("#pragma once"), name);
                Assert.That(content, Does.Not.Contain("int main("), name);
            }
            if (name.EndsWith(".g.cpp")) Assert.That(content, Does.Not.Contain("#pragma once"), name);
        }
    }

    [Test]
    public void Split_ClassAndGenerics_LiveEntirelyInAggregateHeader()
    {
        var r = Split(emitMain: true, ("Logic.bas",
            "Class Player\n    Public Name As String\n    Function Tag() As String\n        Return Name\n    End Function\nEnd Class\nSub Main()\n    Dim p As New Player()\nEnd Sub"));
        Assert.That(r.Files["Game.g.h"], Does.Contain("class Player"));
        Assert.That(r.Files["Logic.g.cpp"], Does.Not.Contain("class Player"));
    }

    [Test]
    public void Split_RuntimeHeader_ContainsRuntimeAndFrameworkCatalogOnce()
    {
        var r = Split(emitMain: true, ("Logic.bas", "Sub Main()\n    Dim xs As New List(Of Integer)\n    xs.Add(1)\nEnd Sub"));
        var rt = r.Files["BasicLangRuntime.g.h"];
        Assert.That(rt, Does.Contain("namespace BasicLang"));
        Assert.That(rt, Does.Contain("Framework_Initialize"));
        Assert.That(r.Files["Game.g.h"], Does.Not.Contain("namespace BasicLangRt"));
    }

    [Test]
    public void Split_ModuleNamedLikeProject_SkipsShimWithoutCollision()
    {
        // deliberately lower-case on disk: module name comes back verbatim as "game",
        // and the shim-skip comparison MUST be OrdinalIgnoreCase (D11) or obj/gen gets
        // game.g.h AND Game.g.h — the same file on Windows, silently overwritten
        var r = Split(emitMain: true, ("game.bas", "Sub Main()\nEnd Sub"));
        Assert.That(r.Files.Keys.Count(k => k.Equals("Game.g.h", StringComparison.OrdinalIgnoreCase)),
                    Is.EqualTo(1));
    }

    [Test]
    public void Split_ModuleNamedBasicLangRuntime_ThrowsInsteadOfSilentlyOverwriting()
    {
        // "BasicLangRuntime.bas" would produce shim "BasicLangRuntime.g.h" — the SAME file
        // name as the reserved runtime header. The preflight check rejects the module by name
        // with an actionable message (Task 4 surfaces it verbatim as a diagnostic); the
        // case-insensitive Files.Add calls remain as a backstop.
        var ex = Assert.Throws<ArgumentException>(() => Split(emitMain: true,
            ("BasicLangRuntime.bas", "Sub Main()\nEnd Sub")));
        Assert.That(ex!.Message, Does.Contain("BasicLangRuntime"));
        Assert.That(ex.Message, Does.Contain("reserved"));
    }

    [Test]
    public void Split_NormalModules_ProduceNoSharedFallbackTu()
    {
        // The __shared fallback TU only exists for functions whose ModuleName is empty or not
        // in the unit-module roster. The real frontend auto-stamps ModuleName from file
        // basenames and the roster is built from the same units, so a normal program can never
        // reach the fallback via the public compile pipeline — pin the negative here; the
        // positive (roster-mismatch) case is pinned separately below.
        var r = Split(emitMain: true,
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\nSub Main()\n    PrintLine CalculateScore(3)\nEnd Sub"),
            ("Player.bas", "Function PlayerTag() As String\n    Return \"p1\"\nEnd Function"));
        Assert.That(r.Files.Keys, Has.None.EqualTo("Game.__shared.g.cpp"));
        Assert.That(r.TranslationUnitFileNames, Has.None.EqualTo("Game.__shared.g.cpp"));
    }

    [Test]
    public void Split_EmptyModuleRoster_RoutesDefinitionsToSharedFallbackTu()
    {
        // Public-API route to the fallback bucket: GenerateSplit's unitModules parameter IS the
        // roster, so a caller passing an empty roster (defensive contract; the Task-4 builder
        // always passes the real units) must still get every definition compiled — via
        // <Project>.__shared.g.cpp, not silently dropped.
        var dir = Path.Combine(Path.GetTempPath(), "bl-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "Logic.bas");
            File.WriteAllText(p, "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\nSub Main()\n    PrintLine CalculateScore(3)\nEnd Sub");
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(new[] { p });
            Assert.That(result.Success, Is.True, string.Join("\n", result.AllErrors.Select(e => e.Message)));

            var r = new CppCodeGenerator().GenerateSplit(
                result.CombinedIR, "Game", Array.Empty<BasicLang.Compiler.IR.IRModule>(), emitMain: true);

            Assert.That(r.Files.Keys, Does.Contain("Game.__shared.g.cpp"));
            Assert.That(r.TranslationUnitFileNames, Does.Contain("Game.__shared.g.cpp"));
            Assert.That(r.Files["Game.__shared.g.cpp"], Does.Contain("CalculateScore"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Test]
    public void Split_UsesFramework_FalseForPlainProgram_TrueWhenFrameworkCalled()
    {
        var plain = Split(emitMain: true,
            ("Logic.bas", "Sub Main()\n    PrintLine \"hi\"\nEnd Sub"));
        Assert.That(plain.UsesFramework, Is.False);

        // GameShutdown is a registered std-lib sub that lowers to the Framework_Shutdown
        // extern — bodies are captured before the runtime header renders, so the flag must
        // reflect real usage.
        var game = Split(emitMain: true,
            ("Logic.bas", "Sub Main()\n    GameShutdown()\nEnd Sub"));
        Assert.That(game.UsesFramework, Is.True);
    }

    [Test]
    public void SingleStringGenerate_IsUnchangedByThisTask()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-split-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = Path.Combine(dir, "m.bas");
            File.WriteAllText(p, "Sub Main()\n    PrintLine \"x\"\nEnd Sub");
            var compiler = new BasicCompiler(new CompilerOptions { TargetBackend = "cpp" });
            var result = compiler.CompileProjectFiles(new[] { p });
            var single = new CppCodeGenerator().Generate(result.CombinedIR);
            Assert.That(single, Does.Contain("int main("));
            Assert.That(single, Does.Contain("using namespace std;"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
