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
        if (!proc.WaitForExit(30000))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            Assert.Fail($"produced exe did not exit within 30s: {exePath}");
        }
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
        // One undefined-call statement = exactly one semantic error. It must yield exactly
        // ONE diagnostic, attributed to Logic.bas, with the ORIGINAL message — never a
        // second copy pinned to the .blproj, and never the "Error at line ..." double-prefix
        // that FinalizeResult -> WithInlineLocation stamps onto the AllErrors copy.
        var project = MakeProject(
            LanguageCppBlproj("Semantic"),
            ("Logic.bas", "Function Broken() As Integer\n    Return Undefined123\nEnd Function\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);

        var errorDiags = result.Diagnostics.Where(d => !d.IsWarning).ToList();
        Assert.That(errorDiags.Count, Is.EqualTo(1), "one source error must produce one diagnostic: " + DiagCodes(result));

        var diag = errorDiags[0];
        Assert.That(diag.FilePath, Is.Not.Null.And.EndWith("Logic.bas"));
        Assert.That(diag.Line, Is.GreaterThan(0));
        Assert.That(result.Diagnostics.Any(d => d.FilePath != null
            && d.FilePath.EndsWith(".blproj", StringComparison.OrdinalIgnoreCase)), Is.False,
            "no diagnostic may be pinned to the .blproj when a source attribution exists");
        Assert.That(diag.Message, Does.Not.StartWith("Error at line"),
            "must be the original message, not the double-prefixed WithInlineLocation copy");
    }

    // ------------------------------------------------------------------
    // Framework auto-link — a BasicLang game that calls Framework_* builtins with NO
    // explicit <NativeLib> still links the engine import lib and deploys its DLL.
    // ------------------------------------------------------------------

    [Test]
    public void Mixed_FrameworkCall_AutoLinksEngineLib_AndDeploysDll()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        if (EngineDeployment.LocateImportLib() == null)
            Assert.Ignore("VisualGameStudioEngine.lib not found (engine not built on this machine)");

        var project = MakeProject(
            BackendCppBlproj("FwGame"),
            ("App.bas", "Sub Main()\n    GameShutdown()\nEnd Sub\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput + "\n" + DiagCodes(result));
        // Do NOT run the exe — it makes an engine call. Only verify the deploy: the runtime
        // DLL must sit next to the exe, proving the auto-link path resolved the engine lib
        // even though no <NativeLib> item was declared.
        var binDebug = Path.Combine(_dir, "bin", "Debug");
        Assert.That(File.Exists(Path.Combine(binDebug, "VisualGameStudioEngine.dll")), Is.True,
            "engine DLL must be deployed next to the exe via the framework auto-link path");
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

    // ------------------------------------------------------------------
    // Task 5: CLI routing — spawn the real BasicLang.exe (deployed next to the
    // tests) and drive `build`/`run` end-to-end. Before Task 5, a BasicLang
    // project on the C++ backend (no <Language>Cpp>) went down a separate,
    // legacy single-TU CLI path (CppToolchain.CompileToExecutable directly,
    // raw diagnostics, old bin/<config>/<TFM> layout). These tests pin that it
    // now goes through the same CppProjectBuilder as Language=Cpp projects.
    // ------------------------------------------------------------------

    private static string CliPath()
    {
        var cliPath = Path.Combine(AppContext.BaseDirectory, "BasicLang.exe");
        Assert.That(File.Exists(cliPath), Is.True,
            "BasicLang.exe not deployed next to the tests — project reference output changed?");
        return cliPath;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCli(
        string workingDir, params string[] args)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = CliPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);
        }
        catch (OperationCanceledException)
        {
            // Kill the whole tree (BasicLang.exe spawns the C++ toolchain) —
            // otherwise a timed-out compile leaks cl.exe/clang++ processes.
            try { process.Kill(entireProcessTree: true); } catch { }
            Assert.Fail($"CLI timed out after 5 minutes: BasicLang.exe {string.Join(" ", args)}");
        }
        return (process.ExitCode, await stdout, await stderr);
    }

    [Test]
    public async Task Cli_Build_MixedProject_BothDirections_AndRun()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var project = MakeProject(
            LanguageCppBlproj("MixedCliApp"),
            ("main.cpp", """
                #include "Logic.g.h"
                int main() {
                    std::cout << "score=" << CalculateScore(5) << std::endl;
                    return 0;
                }
                """),
            ("Logic.bas", "Function CalculateScore(hits As Integer) As Integer\n    Return hits * 10\nEnd Function\n"));

        var (exitBuild, buildOut, buildErr) = await RunCli(_dir, "build", project.FilePath);
        Assert.That(exitBuild, Is.EqualTo(0), $"build failed:\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

        var exePath = Path.Combine(_dir, "bin", "Debug", "MixedCliApp.exe");
        Assert.That(File.Exists(exePath), Is.True, $"expected exe at converged layout {exePath}");

        var (exitRun, runOut, runErr) = await RunCli(_dir, "run", project.FilePath);
        Assert.That(exitRun, Is.EqualTo(0), $"run failed:\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");
        Assert.That(runOut, Does.Contain("score=50"));
    }

    [Test]
    public async Task Cli_Build_BackendCppBasicLangProject_LandsInConvergedLayout()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        // Plain BasicLang project (no <Language>Cpp>) targeting the C++ backend —
        // before Task 5 this built via the legacy single-TU path to
        // bin/<config>/<TFM>/<name>.exe; it must now converge on
        // bin/<config>/<name>.exe, the same layout CppProjectBuilder gives
        // Language=Cpp projects.
        var project = MakeProject(
            BackendCppBlproj("BackendCliApp"),
            ("App.bas", "Sub Main()\n    Console.WriteLine(\"backend-cpp-ok\")\nEnd Sub\n"));

        var (exitBuild, buildOut, buildErr) = await RunCli(_dir, "build", project.FilePath);
        Assert.That(exitBuild, Is.EqualTo(0), $"build failed:\nSTDOUT:\n{buildOut}\nSTDERR:\n{buildErr}");

        var convergedExe = Path.Combine(_dir, "bin", "Debug", "BackendCliApp.exe");
        Assert.That(File.Exists(convergedExe), Is.True,
            $"expected the converged native layout bin/Debug/BackendCliApp.exe; not found (dir contents: " +
            string.Join(", ", Directory.GetFiles(_dir, "*", SearchOption.AllDirectories)) + ")");

        var oldManagedExe = Path.Combine(_dir, "bin", "Debug", "net8.0", "BackendCliApp.exe");
        Assert.That(File.Exists(oldManagedExe), Is.False,
            "must NOT build to the old managed bin/<config>/<TFM>/ layout (that was the legacy single-TU CLI path)");

        var (exitRun, runOut, runErr) = await RunCli(_dir, "run", project.FilePath);
        Assert.That(exitRun, Is.EqualTo(0), $"run failed:\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");
        Assert.That(runOut, Does.Contain("backend-cpp-ok"));
    }

    [Test]
    public async Task Cli_Build_MixedProject_CppError_EmitsNormalizedDiagnostic()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        // A BasicLang/native-backend project with a bad hand-written .cpp: before
        // Task 5 this hit the legacy CLI arm (CppToolchain.CompileToExecutable),
        // whose failure path dumped the toolchain's raw output rather than a
        // normalized file(line,col) diagnostic. It must now go through
        // CppProjectBuilder like a Language=Cpp project does.
        var project = MakeProject(
            BackendCppBlproj("BadCliApp"),
            ("main.cpp", "int main() { undeclared_symbol; return 0; }\n"));

        var (exit, stdout, stderr) = await RunCli(_dir, "build", project.FilePath);

        Assert.That(exit, Is.Not.EqualTo(0));
        // Normalized MSBuild-style location: main.cpp(1,...): error ...
        Assert.That(stdout + stderr, Does.Match(@"main\.cpp\(1[,)]"),
            $"expected a normalized file(line[,col]) diagnostic, not a raw toolchain blob.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }
}
