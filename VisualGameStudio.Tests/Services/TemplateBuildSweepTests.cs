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
