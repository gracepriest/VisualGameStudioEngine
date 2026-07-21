using System.Diagnostics;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// End-to-end template sweep: every built-in project template must produce a
/// project that the real BasicLang compiler can build. This is the invariant
/// behind File → New Project — a template that generates unbuildable code is
/// broken no matter how nice the dialog is.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TemplateBuildSweepTests
{
    private string _rootDir = null!;
    private ProjectTemplateService _service = null!;

    private static string? FindCompiler()
    {
        // TestDirectory = VisualGameStudio.Tests\bin\<Config>\net8.0
        var dir = TestContext.CurrentContext.TestDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", ".."));
        foreach (var config in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(repoRoot, "BasicLang", "bin", config, "net8.0", "BasicLang.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    [SetUp]
    public void SetUp()
    {
        _service = new ProjectTemplateService();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-template-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_rootDir))
                Directory.Delete(_rootDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [TestCase("console-app")]
    [TestCase("game-app")]
    [TestCase("winforms-app")]
    [TestCase("wpf-app")]
    [TestCase("avalonia-app")]
    [TestCase("class-library")]
    [TestCase("web-api")]
    [TestCase("unit-test")]
    public async Task Template_CreatesProject_ThatCompilerBuilds(string templateId)
    {
        var compiler = FindCompiler();
        if (compiler == null)
            Assert.Inconclusive("BasicLang.exe not built; run 'dotnet build BasicLang -c Release' first.");

        var template = ProjectTemplates.All.Single(t => t.Id == templateId);
        var name = "Sweep" + string.Concat(templateId.Split('-').Select(
            p => char.ToUpperInvariant(p[0]) + p.Substring(1)));

        var options = new CreateProjectOptions
        {
            Name = name,
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.DotNet,
            CreateSolutionFolder = true,
            CreateGitRepository = false
        };

        var result = await _service.CreateProjectAsync(options);

        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");
        Assert.That(result.ProjectPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(result.ProjectPath), Is.True, "project file missing on disk");

        var (exitCode, output) = RunCompilerBuild(compiler, result.ProjectPath!);

        Assert.That(exitCode, Is.EqualTo(0),
            $"'{templateId}' project failed to build.\n--- compiler output ---\n{output}");
    }

    [TestCase("cpp-console-app")]
    [TestCase("cpp-library")]
    [TestCase("cpp-game-app")]
    public async Task CppTemplate_CreatesProject_ThatCompilerBuilds(string templateId)
    {
        var compiler = FindCompiler();
        if (compiler == null)
            Assert.Inconclusive("BasicLang.exe not built; run 'dotnet build BasicLang -c Release' first.");
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");
        if (templateId == "cpp-game-app" &&
            BasicLang.Compiler.ProjectSystem.EngineDeployment.LocateImportLib() == null)
            Assert.Ignore("VisualGameStudioEngine.lib not found (engine not built)");

        var template = ProjectTemplates.All.Single(t => t.Id == templateId);
        var name = "Sweep" + string.Concat(templateId.Split('-').Select(
            p => char.ToUpperInvariant(p[0]) + p.Substring(1)));

        var options = new CreateProjectOptions
        {
            Name = name,
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.Cpp,
            CreateSolutionFolder = true,
            CreateGitRepository = false
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");
        Assert.That(File.ReadAllText(result.ProjectPath!), Does.Contain("<Language>Cpp</Language>"));

        var (exitCode, output) = RunCompilerBuild(compiler, result.ProjectPath!);
        Assert.That(exitCode, Is.EqualTo(0),
            $"'{templateId}' project failed to build.\n--- compiler output ---\n{output}");
    }

    [Test]
    public async Task CppLibraryTemplate_DoesNotLeaveBogusMainCpp()
    {
        // Pins the GenerateSourceFilesAsync gating: the generic BasicLang-content
        // Main.<ext> write must not run for cpp solution types.
        var template = ProjectTemplates.All.Single(t => t.Id == "cpp-library");
        var options = new CreateProjectOptions
        {
            Name = "SweepCppLibNoMain",
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.Cpp,
            CreateSolutionFolder = false,
            CreateGitRepository = false
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");
        var projDir = Path.GetDirectoryName(result.ProjectPath!)!;
        Assert.That(File.Exists(Path.Combine(projDir, "Main.cpp")), Is.False,
            "generic Main-file write must be gated off for cpp solution types");
        Assert.That(File.Exists(Path.Combine(projDir, "mathutils.cpp")), Is.True);
        Assert.That(File.Exists(Path.Combine(projDir, "mathutils.h")), Is.True);
    }

    // The IDE (ProjectTemplateService) and CLI (TemplateEngine) template systems
    // are separate implementations that must generate IDENTICAL source files —
    // the sync comments in both files ask for it, but only this test enforces it.
    [TestCase("cpp-console-app", "cpp-console", "main.cpp")]
    [TestCase("cpp-library", "cpp-library", "mathutils.cpp")]
    [TestCase("cpp-library", "cpp-library", "mathutils.h")]
    [TestCase("cpp-game-app", "cpp-game", "main.cpp")]
    public async Task CppTemplates_IdeAndCliSystems_GenerateIdenticalSourceFiles(
        string ideTemplateId, string cliShortName, string fileName)
    {
        // Unique project dir per TestCase (cpp-library appears twice).
        var name = "Equiv" + string.Concat((ideTemplateId + fileName).Where(char.IsLetterOrDigit));
        var template = ProjectTemplates.All.Single(t => t.Id == ideTemplateId);
        var options = new CreateProjectOptions
        {
            Name = name, Location = _rootDir, Template = template,
            SolutionType = SolutionTypes.Cpp, CreateSolutionFolder = false, CreateGitRepository = false
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, result.Error);
        var ideContent = File.ReadAllText(Path.Combine(Path.GetDirectoryName(result.ProjectPath!)!, fileName));

        var cliTemplate = new BasicLang.Compiler.ProjectSystem.TemplateEngine().GetTemplate(cliShortName);
        Assert.That(cliTemplate, Is.Not.Null);
        var cliContent = cliTemplate!.Files[fileName].Replace("{{ProjectName}}", name);

        // Line-ending differences alone are a pass by design (verbatim strings
        // inherit each source file's endings).
        Assert.That(ideContent.ReplaceLineEndings(), Is.EqualTo(cliContent.ReplaceLineEndings()),
            $"IDE and CLI template systems drifted on {fileName} — keep them in sync (see comments in both)");
    }

    // .blproj emission of wizard-chosen C++ options: CreateProjectOptions carries
    // CppStandard/CppToolchain into the generated project file. These tests read
    // the generated .blproj text directly — no compiler involved. (Intentional
    // IDE/CLI divergence: the CLI templates emit defaults only.)
    [Test]
    public async Task CreateProject_Cpp_EmitsChosenStandardAndToolchain()
    {
        var template = ProjectTemplates.All.Single(t => t.Id == "cpp-console-app");
        var options = new CreateProjectOptions
        {
            Name = "BlprojCppChosen",
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.Cpp,
            CreateSolutionFolder = false,
            CreateGitRepository = false,
            CppStandard = "c++17",
            CppToolchain = "gcc"
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");

        var content = File.ReadAllText(result.ProjectPath!);
        Assert.That(content, Does.Contain("<CppStandard>c++17</CppStandard>"));
        Assert.That(content, Does.Contain("<CppToolchain>gcc</CppToolchain>"));
    }

    [Test]
    public async Task CreateProject_Cpp_NullOptions_EmitsDefaultStandard_NoToolchain()
    {
        // The none-installed self-heal case: no toolchain chosen at creation
        // time → no <CppToolchain> element, so the machine probe decides at
        // build time; the standard falls back to the template default.
        var template = ProjectTemplates.All.Single(t => t.Id == "cpp-console-app");
        var options = new CreateProjectOptions
        {
            Name = "BlprojCppDefaults",
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.Cpp,
            CreateSolutionFolder = false,
            CreateGitRepository = false
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");

        var content = File.ReadAllText(result.ProjectPath!);
        Assert.That(content, Does.Contain("<CppStandard>c++20</CppStandard>"));
        Assert.That(content, Does.Not.Contain("<CppToolchain>"));
    }

    [Test]
    public async Task CreateProject_Cpp_EmptyStandard_EmitsDefault()
    {
        // Empty string is what an unset wizard field degrades to; it must fall
        // back to the template default exactly like null does (a bare
        // <CppStandard></CppStandard> would later become a bare `-std=`).
        var template = ProjectTemplates.All.Single(t => t.Id == "cpp-console-app");
        var options = new CreateProjectOptions
        {
            Name = "BlprojCppEmptyStd",
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.Cpp,
            CreateSolutionFolder = false,
            CreateGitRepository = false,
            CppStandard = ""
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");

        var content = File.ReadAllText(result.ProjectPath!);
        Assert.That(content, Does.Contain("<CppStandard>c++20</CppStandard>"));
    }

    [Test]
    public async Task CreateProject_BasicLang_EmitsNeither()
    {
        var template = ProjectTemplates.All.Single(t => t.Id == "console-app");
        var options = new CreateProjectOptions
        {
            Name = "BlprojBasicLangNone",
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.DotNet,
            CreateSolutionFolder = false,
            CreateGitRepository = false
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");

        var content = File.ReadAllText(result.ProjectPath!);
        Assert.That(content, Does.Not.Contain("<CppStandard>"));
        Assert.That(content, Does.Not.Contain("<CppToolchain>"));
    }

    private static (int ExitCode, string Output) RunCompilerBuild(string compiler, string projectFile)
    {
        var psi = new ProcessStartInfo
        {
            FileName = compiler,
            Arguments = $"build \"{projectFile}\"",
            WorkingDirectory = Path.GetDirectoryName(projectFile)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // GUI/web templates restore NuGet packages on first build — be generous.
        if (!process.WaitForExit(240_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "TIMEOUT after 240s\n" + stdout.Result + stderr.Result);
        }

        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
