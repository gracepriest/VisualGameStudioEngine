using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
[NonParallelizable]
public class CppProjectCliBuildTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cppbuild-" + Guid.NewGuid().ToString("N"));
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

    private ProjectFile MakeCppProject(params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
        {
            var full = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        var blproj = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(blproj, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);
        return ProjectFile.Load(blproj);
    }

    [Test]
    public void Build_MultiFileProject_ProducesRunnableExe_AndCompileCommands()
    {
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");

        var project = MakeCppProject(
            ("main.cpp", """
                #include <iostream>
                #include "util.h"
                int main() { std::cout << "sum=" << Add(2, 3) << std::endl; return 0; }
                """),
            ("util.cpp", """
                #include "util.h"
                int Add(int a, int b) { return a + b; }
                """),
            ("util.h", "int Add(int a, int b);\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput
            + "\n" + string.Join("\n", result.Diagnostics.Select(CppDiagnosticsParser.FormatNormalized)));
        Assert.That(result.ExecutablePath, Does.EndWith("App.exe"));
        Assert.That(File.Exists(result.ExecutablePath), Is.True);
        Assert.That(File.Exists(Path.Combine(_dir, "obj", "compile_commands.json")), Is.True);

        var psi = new System.Diagnostics.ProcessStartInfo(result.ExecutablePath!)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.That(proc.ExitCode, Is.EqualTo(0));
        Assert.That(stdout, Does.Contain("sum=5"));
    }

    [Test]
    public void Build_CompileError_YieldsFileLineDiagnostic()
    {
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");

        var project = MakeCppProject(("main.cpp", "int main() { undeclared_symbol; return 0; }\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Is.Not.Empty, "raw output:\n" + result.RawToolchainOutput);
        var d = result.Diagnostics.First(x => !x.IsWarning);
        Assert.That(d.FilePath, Does.EndWith("main.cpp"));
        Assert.That(d.Line, Is.EqualTo(1));
    }

    [Test]
    public void Build_NoCppSources_FailsWithBL6007()
    {
        var project = MakeCppProject(); // no source files at all
        var result = CppProjectBuilder.Build(project, "Debug");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6007"));
    }

    [Test]
    public void Build_BasicLangSourcesPresent_FailsWithBL6008()
    {
        var project = MakeCppProject(("main.cpp", "int main() { return 0; }\n"),
                                     ("logic.bas", "Module M\nEnd Module\n"));
        var result = CppProjectBuilder.Build(project, "Debug");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6008"));
        Assert.That(result.Diagnostics.First(d => d.Code == "BL6008").Message, Does.Contain("logic.bas"));
    }

    [Test]
    public void Build_NoToolchain_FailsWithBL6005()
    {
        // Only assertable on machines without a toolchain; on machines with one,
        // assert the inverse (a toolchain build never emits BL6005).
        var project = MakeCppProject(("main.cpp", "int main() { return 0; }\n"));
        var result = CppProjectBuilder.Build(project, "Debug");
        if (CppToolchain.Find() == null)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6005"));
            Assert.That(result.Diagnostics.First(d => d.Code == "BL6005").Message,
                Does.Contain("clang").And.Contain("g++").And.Contain("MSVC"));
        }
        else
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6005"));
        }
    }

    [Test]
    public void Build_UnparseableFailure_FallsBackToBL6006()
    {
        // Carry-forward A: BL6006 is load-bearing. Simulate via internal seam:
        // a failed compile whose output parses to zero errors must produce BL6006
        // carrying the raw output.
        var result = new CppProjectBuildResult();
        CppProjectBuilder.ApplyCompileOutcome(result, ok: false,
            output: "ld.lld: some grammar the parser does not know", workingDirectory: _dir,
            projectFilePath: Path.Combine(_dir, "App.blproj"));
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6006"));
        Assert.That(result.Diagnostics.First(d => d.Code == "BL6006").Message,
            Does.Contain("ld.lld"));
        Assert.That(result.RawToolchainOutput, Does.Contain("ld.lld"));
    }

    // ------------------------------------------------------------------
    // CLI e2e: spawn BasicLang.exe (deployed next to the tests) and drive
    // build / run / new end-to-end.
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
    public async Task Cli_Build_CppProject_ProducesExe()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var project = MakeCppProject(("main.cpp", "#include <iostream>\nint main(){ std::cout << \"hi\"; return 0; }\n"));

        var (exit, stdout, stderr) = await RunCli(_dir, "build", project.FilePath);

        Assert.That(exit, Is.EqualTo(0), $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.That(Directory.GetFiles(_dir, "App.exe", SearchOption.AllDirectories), Is.Not.Empty);
    }

    [Test]
    public async Task Cli_Build_CppCompileError_PrintsNormalizedDiagnostic()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var project = MakeCppProject(("main.cpp", "int main() { undeclared_symbol; return 0; }\n"));

        var (exit, stdout, stderr) = await RunCli(_dir, "build", project.FilePath);

        Assert.That(exit, Is.Not.EqualTo(0));
        // Normalized MSBuild-style location: main.cpp(1,...): error ...
        Assert.That(stdout + stderr, Does.Match(@"main\.cpp\(1[,)]"),
            $"expected a normalized file(line[,col]) diagnostic.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Test]
    public async Task Cli_New_CppConsole_Builds_And_Runs()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var (exitNew, so, se) = await RunCli(_dir, "new", "cpp-console", "-n", "HelloCpp", "-o",
            Path.Combine(_dir, "HelloCpp"));
        Assert.That(exitNew, Is.EqualTo(0), $"new failed:\n{so}\n{se}");
        var blproj = Path.Combine(_dir, "HelloCpp", "HelloCpp.blproj");
        Assert.That(File.Exists(blproj), Is.True);
        Assert.That(File.ReadAllText(blproj), Does.Contain("<Language>Cpp</Language>"));

        var (exitBuild, so2, se2) = await RunCli(Path.Combine(_dir, "HelloCpp"), "build", blproj);
        Assert.That(exitBuild, Is.EqualTo(0), $"build failed:\n{so2}\n{se2}");

        var exe = Directory.GetFiles(Path.Combine(_dir, "HelloCpp"), "HelloCpp.exe", SearchOption.AllDirectories).Single();
        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.That(output, Does.Contain("Hello from HelloCpp"));
    }

    [Test]
    public async Task Cli_New_CppLibrary_Builds()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var (exitNew, _, _) = await RunCli(_dir, "new", "cpp-library", "-n", "MathLib", "-o",
            Path.Combine(_dir, "MathLib"));
        Assert.That(exitNew, Is.EqualTo(0));
        var blproj = Path.Combine(_dir, "MathLib", "MathLib.blproj");
        var (exitBuild, so, se) = await RunCli(Path.Combine(_dir, "MathLib"), "build", blproj);
        Assert.That(exitBuild, Is.EqualTo(0), $"library build failed:\n{so}\n{se}");
        Assert.That(Directory.GetFiles(Path.Combine(_dir, "MathLib"), "MathLib.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".a") || f.EndsWith(".lib")), Is.Not.Empty);
    }

    [Test]
    public async Task Cli_New_CppGame_Builds_WhenEngineLibAvailable()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        if (EngineDeployment.LocateImportLib() == null)
            Assert.Ignore("VisualGameStudioEngine.lib not found (engine not built on this machine)");

        var (exitNew, _, _) = await RunCli(_dir, "new", "cpp-game", "-n", "MyGame", "-o",
            Path.Combine(_dir, "MyGame"));
        Assert.That(exitNew, Is.EqualTo(0));
        var blproj = Path.Combine(_dir, "MyGame", "MyGame.blproj");
        var (exitBuild, so, se) = await RunCli(Path.Combine(_dir, "MyGame"), "build", blproj);
        Assert.That(exitBuild, Is.EqualTo(0), $"game build failed:\n{so}\n{se}");
        var exeDir = Path.GetDirectoryName(Directory.GetFiles(
            Path.Combine(_dir, "MyGame"), "MyGame.exe", SearchOption.AllDirectories).Single())!;
        Assert.That(File.Exists(Path.Combine(exeDir, "VisualGameStudioEngine.dll")), Is.True,
            "engine DLL must be deployed next to the game exe");
        // Do NOT run the game exe — it opens a window.
    }

    [Test]
    public async Task Cli_Run_CppProject_RunsNativeExe()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var project = MakeCppProject(("main.cpp", "#include <iostream>\nint main(){ std::cout << \"run-ok\"; return 0; }\n"));

        var (exitBuild, so, se) = await RunCli(_dir, "build", project.FilePath);
        Assert.That(exitBuild, Is.EqualTo(0), $"build failed:\n{so}\n{se}");

        var (exitRun, runOut, runErr) = await RunCli(_dir, "run", project.FilePath);
        Assert.That(exitRun, Is.EqualTo(0), $"run failed:\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");
        Assert.That(runOut, Does.Contain("run-ok"));
    }

    [Test]
    public async Task Cli_Run_CppProject_HonorsReleaseConfiguration()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var project = MakeCppProject(("main.cpp", "#include <iostream>\nint main(){ std::cout << \"rel-ok\"; return 0; }\n"));

        var (exitBuild, so, se) = await RunCli(_dir, "build", project.FilePath, "-c", "Release");
        Assert.That(exitBuild, Is.EqualTo(0), $"build failed:\n{so}\n{se}");
        Assert.That(File.Exists(Path.Combine(_dir, "bin", "Release", "App.exe")), Is.True);

        var (exitRun, runOut, runErr) = await RunCli(_dir, "run", project.FilePath, "-c", "Release");
        Assert.That(exitRun, Is.EqualTo(0), $"run failed:\nSTDOUT:\n{runOut}\nSTDERR:\n{runErr}");
        Assert.That(runOut, Does.Contain("rel-ok"));
    }
}
