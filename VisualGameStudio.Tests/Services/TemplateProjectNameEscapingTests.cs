using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// A project file is XML, and a project NAME is user text that lands inside two elements of
/// it. Both template systems interpolated that text raw, so a name containing '&amp;' — which
/// <c>IsValidProjectName</c> admits, because it is a legal file-name character on every
/// platform — produced a .blproj that is not well-formed XML. Creation reported success and
/// the very next step failed:
/// <c>Build failed: '&lt;' is an unexpected token. The expected token is ';'. Line 3, position 21.</c>
///
/// <para>Pre-existing for every template, and inherited by the new web ones. One invariant,
/// two implementations (the IDE's ProjectTemplateService and the CLI's TemplateEngine), so it
/// is pinned once for both — the drift this repo keeps paying for.</para>
/// </summary>
[TestFixture]
public class TemplateProjectNameEscapingTests
{
    private string _rootDir = null!;
    private ProjectTemplateService _service = null!;

    // '&' and '<' are legal in file names; '"' and '\'' are legal too and round-trip through
    // attribute-free element text. Path.GetInvalidFileNameChars keeps out the rest.
    private const string NastyName = "Rock&Roll";

    [SetUp]
    public void SetUp()
    {
        _service = new ProjectTemplateService();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-name-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    /// <summary>Parses the file and reads the two elements back — the only honest assertion,
    /// since the bug is precisely that the text looked right and the DOCUMENT was broken.</summary>
    private static void AssertWellFormedAndNamed(string projectPath, string expectedName)
    {
        Assert.That(File.Exists(projectPath), Is.True, "project file missing on disk");

        XDocument doc;
        try { doc = XDocument.Load(projectPath); }
        catch (System.Xml.XmlException ex)
        {
            Assert.Fail($"the generated .blproj is not well-formed XML: {ex.Message}\n" +
                        $"--- file ---\n{File.ReadAllText(projectPath)}");
            return;
        }

        var props = doc.Root!.Element("PropertyGroup")!;
        Assert.That(props.Element("ProjectName")!.Value, Is.EqualTo(expectedName),
            "the name must round-trip through the XML unchanged");
        Assert.That(props.Element("RootNamespace")!.Value, Is.EqualTo(expectedName));
    }

    [TestCase("console-app", "dotnet")]
    [TestCase("web-site", "javascript")]
    public async Task Ide_ProjectNameWithAnAmpersand_ProducesWellFormedXml(string templateId, string solutionTypeId)
    {
        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = NastyName,
            Location = _rootDir,
            SolutionType = SolutionTypes.All.Single(t => t.Id == solutionTypeId),
            Template = ProjectTemplates.All.Single(t => t.Id == templateId),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        AssertWellFormedAndNamed(result.ProjectPath!, NastyName);
    }

    /// <summary>A custom namespace is a second, independent path into the same element.</summary>
    [Test]
    public async Task Ide_CustomNamespaceWithAnAmpersand_ProducesWellFormedXml()
    {
        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = "Plain",
            Namespace = "Acme&Co",
            Location = _rootDir,
            SolutionType = SolutionTypes.JavaScript,
            Template = ProjectTemplates.All.Single(t => t.Id == "web-site"),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        var doc = XDocument.Load(result.ProjectPath!);
        Assert.That(doc.Root!.Element("PropertyGroup")!.Element("RootNamespace")!.Value, Is.EqualTo("Acme&Co"));
    }

    [TestCase("console")]
    [TestCase("web")]
    public void Cli_ProjectNameWithAnAmpersand_ProducesWellFormedXml(string shortName)
    {
        var outputDir = Path.Combine(_rootDir, "cli-" + shortName);
        var created = new BasicLang.Compiler.ProjectSystem.TemplateEngine()
            .CreateProject(shortName, NastyName, outputDir);
        Assert.That(created, Is.True, $"TemplateEngine.CreateProject failed for '{shortName}'");

        AssertWellFormedAndNamed(Path.Combine(outputDir, NastyName + ".blproj"), NastyName);
    }

    /// <summary>
    /// The CLI's "sln" template emits the LEGACY PLAIN-TEXT solution format, not XML — so it
    /// must keep the raw name even though its extension looks like a sibling of .blproj.
    /// Extension alone cannot decide what needs escaping; this is the case that proves it.
    /// </summary>
    [Test]
    public void Cli_SolutionTemplate_IsPlainText_AndKeepsTheRawName()
    {
        var outputDir = Path.Combine(_rootDir, "cli-sln");
        var created = new BasicLang.Compiler.ProjectSystem.TemplateEngine()
            .CreateProject("sln", NastyName, outputDir);
        Assert.That(created, Is.True);

        var text = File.ReadAllText(Path.Combine(outputDir, NastyName + ".blsln"));
        Assert.That(text, Does.StartWith("BasicLang Solution File"), "still the plain-text format");
        Assert.That(text, Does.Contain(NastyName));
        Assert.That(text, Does.Not.Contain("&amp;"));
    }

    /// <summary>
    /// The escaping must not leak into SOURCE files: a .bas is not XML, and an
    /// <c>&amp;amp;</c> in a string literal would be a visible defect on the page.
    /// </summary>
    [Test]
    public void Cli_SourceFilesKeepTheRawName()
    {
        var outputDir = Path.Combine(_rootDir, "cli-src");
        new BasicLang.Compiler.ProjectSystem.TemplateEngine().CreateProject("web", NastyName, outputDir);

        var main = File.ReadAllText(Path.Combine(outputDir, "Main.bas"));
        Assert.That(main, Does.Contain(NastyName));
        Assert.That(main, Does.Not.Contain("&amp;"));
    }

    [Test]
    public async Task Ide_SourceFilesKeepTheRawName()
    {
        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = NastyName,
            Location = _rootDir,
            SolutionType = SolutionTypes.JavaScript,
            Template = ProjectTemplates.All.Single(t => t.Id == "web-site"),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        var main = File.ReadAllText(Path.Combine(Path.GetDirectoryName(result.ProjectPath!)!, "Main.bas"));
        Assert.That(main, Does.Contain(NastyName));
        Assert.That(main, Does.Not.Contain("&amp;"));
    }

    /// <summary>
    /// End-to-end: the compiler must actually build what the wizard just wrote. Creation
    /// succeeding while the first build dies on an XML parse error is the whole defect.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task Ide_AmpersandProject_ActuallyBuilds()
    {
        var compiler = FindCompiler();
        if (compiler == null)
            Assert.Inconclusive("BasicLang.exe not built; run 'dotnet build BasicLang -c Release' first.");

        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = NastyName,
            Location = _rootDir,
            SolutionType = SolutionTypes.JavaScript,
            Template = ProjectTemplates.All.Single(t => t.Id == "web-site"),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });
        Assert.That(result.Success, Is.True, result.Error);

        var (exitCode, output) = RunCompilerBuild(compiler, result.ProjectPath!);
        Assert.That(exitCode, Is.EqualTo(0), $"the just-created project does not build:\n{output}");
    }

    private static string? FindCompiler()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        var repoRoot = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", ".."));
        foreach (var config in new[] { "Release", "Debug" })
        {
            var candidate = Path.Combine(repoRoot, "BasicLang", "bin", config, "net8.0", "BasicLang.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static (int ExitCode, string Output) RunCompilerBuild(string compiler, string projectFile)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = compiler,
            WorkingDirectory = Path.GetDirectoryName(projectFile)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        // ArgumentList, not a quoted string: '&' in the path must reach the child verbatim.
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(projectFile);

        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (-1, "TIMEOUT");
        }
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
