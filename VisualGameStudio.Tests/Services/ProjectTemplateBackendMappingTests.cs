using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// The wizard's solution type must become the .blproj's TargetBackend — for EVERY
/// solution type. ProjectTemplateService maps the two with a switch whose default
/// used to be "CSharp", so a solution type without an arm did not fail: it silently
/// created a C# project. That is exactly how the first JavaScript project would have
/// been written as C# even though the build service, the CLI and the F5 preview all
/// already understood JavaScript. These tests need no compiler and run in the fast
/// subset; TemplateBuildSweepTests builds the same projects for real.
/// </summary>
[TestFixture]
public class ProjectTemplateBackendMappingTests
{
    private string _rootDir = null!;
    private ProjectTemplateService _service = null!;

    // The single source of truth for "which solution type writes which backend".
    // A solution type missing from this table FAILS (see the guard below) — add a
    // row, never widen the service's default.
    private static readonly Dictionary<string, (string Backend, string TemplateId)> Expected = new()
    {
        ["dotnet"]     = ("CSharp",     "console-app"),
        ["msil"]       = ("MSIL",       "console-app"),
        ["native"]     = ("Cpp",        "console-app"),
        ["llvm"]       = ("LLVM",       "console-app"),
        ["cpp"]        = ("Cpp",        "cpp-console-app"),
        ["javascript"] = ("JavaScript", "web-site"),
    };

    [SetUp]
    public void SetUp()
    {
        _service = new ProjectTemplateService();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-backend-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_rootDir)) Directory.Delete(_rootDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static IEnumerable<TestCaseData> EverySolutionType() =>
        SolutionTypes.All.Select(t => new TestCaseData(t.Id).SetName("{m}(" + t.Id + ")"));

    [TestCaseSource(nameof(EverySolutionType))]
    public async Task CreateProject_WritesTheSolutionTypesOwnBackend(string solutionTypeId)
    {
        Assert.That(Expected.ContainsKey(solutionTypeId), Is.True,
            $"SolutionTypes.All gained '{solutionTypeId}' — add its TargetBackend row to this test " +
            "and an explicit arm to ProjectTemplateService.GenerateProjectFileContent.");
        var (backend, templateId) = Expected[solutionTypeId];

        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = "Map" + solutionTypeId,
            Location = _rootDir,
            SolutionType = SolutionTypes.All.Single(t => t.Id == solutionTypeId),
            Template = ProjectTemplates.All.Single(t => t.Id == templateId),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        var blproj = File.ReadAllText(result.ProjectPath!);
        Assert.That(blproj, Does.Contain($"<TargetBackend>{backend}</TargetBackend>"),
            $"solution type '{solutionTypeId}' must write its own backend, not the CSharp default");
    }

    [Test]
    public void EverySolutionType_HasAtLeastOneTemplate()
    {
        // A solution type with no template renders an EMPTY list in the wizard —
        // selectable, then a dead end.
        foreach (var type in SolutionTypes.All)
        {
            Assert.That(ProjectTemplates.All.Any(t => t.SupportedSolutionTypes.Contains(type.Id)), Is.True,
                $"no built-in template supports solution type '{type.Id}'");
        }
    }

    [Test]
    public void JavaScriptSolutionType_IsListed_AndIsABasicLangTarget()
    {
        var js = SolutionTypes.All.SingleOrDefault(t => t.Id == "javascript");
        Assert.That(js, Is.Not.Null, "SolutionTypes.All must include the JavaScript backend");
        Assert.That(js!.SourceExtension, Is.EqualTo(".bas"), "the source is BasicLang; only the output is JavaScript");
        Assert.That(js.ProjectExtension, Is.EqualTo(".blproj"));
        Assert.That(js.IsAvailable, Is.True);
        Assert.That(ReferenceEquals(js, SolutionTypes.JavaScript), Is.True);
    }

    [Test]
    public async Task WebSiteTemplate_IsJavaScriptOnly_AndScaffoldsATypedDomPage()
    {
        var template = ProjectTemplates.All.Single(t => t.Id == "web-site");
        Assert.That(template.SupportedSolutionTypes, Is.EqualTo(new[] { "javascript" }),
            "the page reaches the DOM through ::document, which no other backend can lower");
        Assert.That(template.Category, Is.EqualTo("Web"));
        Assert.That(template.CreateSolution, Is.True);

        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = "Landing",
            Location = _rootDir,
            SolutionType = SolutionTypes.JavaScript,
            Template = template,
            CreateSolutionFolder = true,
            CreateGitRepository = false
        });
        Assert.That(result.Success, Is.True, result.Error);

        var projectDir = Path.GetDirectoryName(result.ProjectPath!)!;
        var blproj = File.ReadAllText(result.ProjectPath!);
        Assert.That(blproj, Does.Contain("<TargetBackend>JavaScript</TargetBackend>"));
        Assert.That(blproj, Does.Contain("<OutputType>Exe</OutputType>"), "a site has an entry point (Sub Main)");
        Assert.That(blproj, Does.Contain("<Compile Include=\"Main.bas\" />"));
        Assert.That(blproj, Does.Not.Contain("<Language>"), "BasicLang source — never the pure-C++ Language axis");
        Assert.That(blproj, Does.Not.Contain("<CppStandard>"));
        Assert.That(blproj, Does.Not.Contain("<UseWindowsForms>").And.Not.Contain("<UseWPF>"));

        var main = File.ReadAllText(Path.Combine(projectDir, "Main.bas"));
        Assert.That(main, Does.Contain("As Document = ::document"), "typed DOM entry via the shipped dom-core.bli");
        Assert.That(main, Does.Contain("Sub Main()"));
        Assert.That(main, Does.Contain("Landing"), "the project name is substituted into the page");
        Assert.That(main, Does.Not.Contain("{{ProjectName}}"), "no unsubstituted CLI placeholder");
        Assert.That(result.FilesToOpen, Does.Contain(Path.Combine(projectDir, "Main.bas")));

        Assert.That(result.SolutionPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(result.SolutionPath), Is.True);
    }

    [Test]
    public void WebSiteTemplate_RefusesOtherSolutionTypes()
    {
        var template = ProjectTemplates.All.Single(t => t.Id == "web-site");
        var validation = _service.ValidateProjectOptions(new CreateProjectOptions
        {
            Name = "Nope", Location = _rootDir, SolutionType = SolutionTypes.DotNet, Template = template
        });
        Assert.That(validation.IsValid, Is.False);
        Assert.That(validation.Errors, Has.Some.Contains("does not support solution type"));
    }

    [Test]
    public async Task UnknownSolutionType_FailsLoudly_InsteadOfWritingCSharp()
    {
        // The regression this fixture exists for: an id with no arm must surface as
        // an error, never as a silently-C# project.
        var stray = new SolutionType { Id = "stray", Name = "Stray", SourceExtension = ".bas" };
        var template = new ProjectTemplate
        {
            Id = "stray-app", Name = "Stray", SupportedSolutionTypes = new List<string> { "stray" }
        };
        _service.RegisterTemplate(template);

        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = "Stray", Location = _rootDir, SolutionType = stray, Template = template,
            CreateSolutionFolder = false, CreateGitRepository = false
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.Error, Does.Contain("stray").And.Contain("TargetBackend"));
        Assert.That(File.Exists(Path.Combine(_rootDir, "Stray", "Stray.blproj")), Is.False,
            "no project file may be written for an unmapped solution type");
    }
}
