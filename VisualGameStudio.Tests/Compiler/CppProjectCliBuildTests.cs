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
}
