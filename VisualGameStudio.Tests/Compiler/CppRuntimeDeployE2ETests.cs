using System;
using System.IO;
using NUnit.Framework;
using BasicLang.Compiler.ProjectSystem;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Regression for the "libstdc++-6.dll not found" standalone-run failure: a MinGW
/// (g++ / winlibs-clang) linked exe dynamically imports libstdc++-6.dll,
/// libgcc_s_seh-1.dll and libwinpthread-1.dll. With the toolchain OFF PATH (the
/// winlibs override scenario) nothing put those next to the exe, so double-clicking
/// the build output died before entry. <see cref="CppProjectBuilder.Build"/> must now
/// deploy the toolchain's runtime DLLs into bin/&lt;config&gt; beside the exe, exactly
/// like it already deploys the engine's native DLLs.
///
/// Uses the real off-PATH winlibs g++ by explicit path (the exact configuration that
/// exhibits the bug); skipped when that toolchain is not installed on the box.
/// </summary>
[Category("Integration")]
[TestFixture]
public class CppRuntimeDeployE2ETests
{
    // The blessed off-PATH MinGW toolchain in this repo's environment. The whole point
    // of the bug is that this bin dir is NOT on PATH, so the built exe cannot resolve
    // its runtime DLLs unless the build copies them next to it.
    private const string WinlibsGxx = @"C:\winlibs\mingw64\bin\g++.exe";

    private static readonly string[] ExpectedRuntimeDlls =
        { "libstdc++-6.dll", "libgcc_s_seh-1.dll", "libwinpthread-1.dll" };

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        if (!File.Exists(WinlibsGxx))
            Assert.Ignore($"winlibs g++ not installed at {WinlibsGxx}; MinGW runtime-deploy e2e skipped.");
        _dir = Path.Combine(Path.GetTempPath(), "bl-rtdeploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (_dir == null) return;
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    private ProjectFile MakeMinimalCppProject()
    {
        const string blproj = """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
                <CppToolchain>gcc</CppToolchain>
              </PropertyGroup>
            </BasicLangProject>
            """;
        File.WriteAllText(Path.Combine(_dir, "App.bas"), "Sub Main()\n    PrintLine 7\nEnd Sub\n");
        var projPath = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(projPath, blproj);
        return ProjectFile.Load(projPath);
    }

    [Test]
    public void Build_With_Offpath_Mingw_Deploys_Runtime_Dlls_Beside_Exe()
    {
        var project = MakeMinimalCppProject();

        // Resolve the gcc toolchain to the off-PATH winlibs g++ by explicit path —
        // the same seam the IDE's override-aware BuildService injects.
        var result = CppProjectBuilder.Build(project, "Debug",
            resolveById: id => CppToolchain.FromExplicit(id, WinlibsGxx),
            resolveToolchain: () => CppToolchain.FromExplicit("gcc", WinlibsGxx));

        Assert.That(result.Success, Is.True,
            "off-PATH winlibs g++ must build the project. Diagnostics: "
            + string.Join("; ", result.Diagnostics.ConvertAll(d => $"{d.Code} {d.Message}"))
            + " | raw: " + result.RawToolchainOutput);

        var outputDir = Path.Combine(_dir, "bin", "Debug");
        Assert.That(File.Exists(Path.Combine(outputDir, "App.exe")), Is.True, "exe should be produced");

        foreach (var dll in ExpectedRuntimeDlls)
            Assert.That(File.Exists(Path.Combine(outputDir, dll)), Is.True,
                $"{dll} must be deployed next to the exe so it runs with the toolchain off PATH");
    }
}
