using System.Text.Json;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 4 of C++ Phase 2 (mixed projects): <see cref="CppProjectBuilder"/> is now the single
/// native-project orchestrator. It partitions a project's sources into BasicLang (.bas-family,
/// transpiled to C++) and hand-written C++ translation units, enforces the cross-language
/// entry-point rule, wires obj/gen onto the include path, and compiles everything together.
///
/// These tests write a real .blproj + sources to a temp dir and drive
/// <c>CppProjectBuilder.Build(ProjectFile.Load(path), "Debug")</c>. Toolchain-dependent cases
/// (those that actually compile) Assert.Ignore when no clang++/g++/MSVC is available; the
/// entry-rule and semantic-error cases fail BEFORE the toolchain is needed and run everywhere.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MixedProjectBuildTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-mixed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private ProjectFile MakeProject(string blproj, params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
        {
            var full = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, blproj);
        return ProjectFile.Load(path);
    }

    private static string LanguageCppBlproj(string projectName, string outputType = "Exe") => $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>{projectName}</ProjectName>
            <OutputType>{outputType}</OutputType>
            <Language>Cpp</Language>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
        </BasicLangProject>
        """;

    // A BasicLang project whose backend is the native C++ toolchain (no <Language>Cpp>).
    private static string BackendCppBlproj(string projectName, string outputType = "Exe") => $"""
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>{projectName}</ProjectName>
            <OutputType>{outputType}</OutputType>
            <TargetBackend>Cpp</TargetBackend>
          </PropertyGroup>
        </BasicLangProject>
        """;

    private static string DiagCodes(CppProjectBuildResult r) =>
        string.Join(", ", r.Diagnostics.Select(d => $"{d.Code}:{d.Message}"));

    private static string RunExe(string exePath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exePath)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.That(proc.ExitCode, Is.EqualTo(0), $"exe exited {proc.ExitCode}");
        return stdout;
    }

    // ------------------------------------------------------------------
    // Direction B: user C++ calls BasicLang (Language=Cpp project with a .bas file).
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_LanguageCppProject_WithBasFile_BuildsAndRuns()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var project = MakeProject(
            LanguageCppBlproj("MixedApp"),
            ("main.cpp", """
                #include "Logic.g.h"
                int main() {
                    std::cout << "score=" << CalculateScore(5) << std::endl;
                    return 0;
                }
                """),
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput + "\n" + DiagCodes(result));
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6008"));
        Assert.That(result.ExecutablePath, Does.EndWith("MixedApp.exe"));
        Assert.That(File.Exists(result.ExecutablePath), Is.True);
        Assert.That(RunExe(result.ExecutablePath!), Does.Contain("score=50"));
    }

    // ------------------------------------------------------------------
    // Direction A: BasicLang calls into user C++ (header next to the .blproj) — proves
    // projectDir is on the include path, impossible on the old single-TU path.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_BasicLangNativeProject_WithUserCpp_DirectionA()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var project = MakeProject(
            BackendCppBlproj("DirA"),
            ("helper.h", "#pragma once\nnamespace helper { struct Calc { int Value(){ return 77; } }; }\n"),
            ("App.bas",
                "#CppInclude \"helper.h\"\n" +
                "Sub Main()\n" +
                "    Dim c As helper::Calc\n" +
                "    Dim x = c.Value()\n" +
                "    Console.WriteLine(x)\n" +
                "End Sub\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput + "\n" + DiagCodes(result));
        Assert.That(result.ExecutablePath, Does.EndWith("DirA.exe"));
        Assert.That(RunExe(result.ExecutablePath!).Trim(), Is.EqualTo("77"));
    }

    // ------------------------------------------------------------------
    // Pure BasicLang, native backend — no user .cpp; BL6007 must NOT fire.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_PureBasicLang_NativeBackend_StillBuilds()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var project = MakeProject(
            BackendCppBlproj("PureBl"),
            ("App.bas", "Sub Main()\n    PrintLine 42\nEnd Sub\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6007"));
        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput + "\n" + DiagCodes(result));
        Assert.That(File.Exists(Path.Combine(_dir, "bin", "Debug", "PureBl.exe")), Is.True);
        Assert.That(RunExe(result.ExecutablePath!).Trim(), Is.EqualTo("42"));
    }

    // ------------------------------------------------------------------
    // Entry-point matrix — these fail the entry rule BEFORE the toolchain is needed.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_BothMains_FailsBL6012()
    {
        var project = MakeProject(
            LanguageCppBlproj("TwoMains"),
            ("main.cpp", "int main() { return 0; }\n"),
            ("Logic.bas", "Sub Main()\nEnd Sub\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6012"), DiagCodes(result));
    }

    [Test]
    public void Mixed_ExeNoMain_FailsBL6011()
    {
        var project = MakeProject(
            LanguageCppBlproj("NoMain"),
            ("Logic.bas", "Function F() As Integer\n    Return 1\nEnd Function\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6011"), DiagCodes(result));
    }

    [Test]
    public void Mixed_LibraryWithMain_FailsBL6013()
    {
        var project = MakeProject(
            LanguageCppBlproj("Lib", outputType: "Library"),
            ("Logic.bas", "Sub Main()\nEnd Sub\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6013"), DiagCodes(result));
    }

    // ------------------------------------------------------------------
    // No toolchain — environment branch (mirrors CppProjectCliBuildTests.Build_NoToolchain).
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_NoToolchain_HardFailsBL6005()
    {
        var project = MakeProject(
            LanguageCppBlproj("NoTc"),
            ("main.cpp", "int main() { return 0; }\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        if (CppToolchain.Find() == null)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6005"));
        }
        else
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6005"));
        }
    }

    // ------------------------------------------------------------------
    // Semantic error — attributed per-unit through Registry.Modules (Units is empty on
    // failure), so it lands on the .bas file with a real line and is Error-List clickable.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_SemanticError_SurfacesAsClickableDiagnostic()
    {
        var project = MakeProject(
            LanguageCppBlproj("Semantic"),
            ("Logic.bas", "Function Broken() As Integer\n    Return Undefined123\nEnd Function\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);
        var fileDiag = result.Diagnostics.FirstOrDefault(d =>
            d.FilePath != null && d.FilePath.EndsWith("Logic.bas", StringComparison.OrdinalIgnoreCase));
        Assert.That(fileDiag, Is.Not.Null, "expected a diagnostic attributed to Logic.bas: " + DiagCodes(result));
        Assert.That(fileDiag!.Line, Is.GreaterThan(0));
    }

    // ------------------------------------------------------------------
    // compile_commands.json covers BOTH user and generated TUs, and carries the obj/gen include.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_CompileCommands_IncludesGeneratedAndUserTus_AndObjGenInclude()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var project = MakeProject(
            LanguageCppBlproj("CcApp"),
            ("main.cpp", """
                #include "Logic.g.h"
                int main() { std::cout << CalculateScore(2) << std::endl; return 0; }
                """),
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\n"));

        var result = CppProjectBuilder.Build(project, "Debug");
        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput + "\n" + DiagCodes(result));

        var ccPath = Path.Combine(_dir, "obj", "compile_commands.json");
        Assert.That(File.Exists(ccPath), Is.True);

        using var doc = JsonDocument.Parse(File.ReadAllText(ccPath));
        var entries = doc.RootElement.EnumerateArray().ToList();
        var files = entries.Select(e => e.GetProperty("file").GetString() ?? "").ToList();

        Assert.That(files.Any(f => f.EndsWith("main.cpp", StringComparison.OrdinalIgnoreCase)), Is.True,
            "compile_commands must include the user main.cpp: " + string.Join(", ", files));
        Assert.That(files.Any(f => f.EndsWith("Logic.g.cpp", StringComparison.OrdinalIgnoreCase)), Is.True,
            "compile_commands must include the generated Logic.g.cpp: " + string.Join(", ", files));

        var objGen = Path.Combine("obj", "gen");
        var hasObjGenInclude = entries
            .SelectMany(e => e.GetProperty("arguments").EnumerateArray().Select(a => a.GetString() ?? ""))
            .Any(a => a.Contains(objGen, StringComparison.OrdinalIgnoreCase));
        Assert.That(hasObjGenInclude, Is.True, "an -I/ /I argument must reference obj\\gen");
    }

    // ------------------------------------------------------------------
    // Stale generated files from a previous build are cleaned before regeneration.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_StaleGeneratedFiles_AreCleaned()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var objGen = Path.Combine(_dir, "obj", "gen");
        Directory.CreateDirectory(objGen);
        var stale = Path.Combine(objGen, "Old.g.cpp");
        File.WriteAllText(stale, "this is not valid C++ !!! @#$");

        var project = MakeProject(
            BackendCppBlproj("Stale"),
            ("App.bas", "Sub Main()\n    PrintLine 7\nEnd Sub\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(File.Exists(stale), Is.False, "stale Old.g.cpp must be removed before regeneration");
        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput + "\n" + DiagCodes(result));
    }
}
