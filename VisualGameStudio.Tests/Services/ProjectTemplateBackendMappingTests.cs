using System.Reflection;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
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

        // ...and the IDE must READ BACK what the wizard just wrote. Asserting the text alone
        // would still pass if the loader could not parse the token, which is the difference
        // between a project that builds as JavaScript and one that reopens as C#.
        var reloaded = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer()
            .LoadAsync(result.ProjectPath!);
        Assert.That(reloaded.TargetBackend.ToString(), Is.EqualTo(backend),
            "the project the wizard wrote does not round-trip through the IDE's own loader");
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

    // ------------------------------------------------------------------
    // The wizard's TFM picker stops being decorative
    // ------------------------------------------------------------------

    [Test]
    public async Task CreateProject_WritesTheChosenTargetFramework()
    {
        // CreateProjectOptions.TargetFramework was collected by the wizard and then discarded by
        // GenerateProjectFileContent, so every project silently got the "net8.0" default however
        // the picker was set.
        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = "Tfm",
            Location = _rootDir,
            SolutionType = SolutionTypes.DotNet,
            Template = ProjectTemplates.All.Single(t => t.Id == "console-app"),
            TargetFramework = "net9.0",
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(result.ProjectPath!),
            Does.Contain("<TargetFramework>net9.0</TargetFramework>"),
            "the TFM the user picked must reach the project file");

        var reloaded = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer()
            .LoadAsync(result.ProjectPath!);
        Assert.That(reloaded.TargetFramework, Is.EqualTo("net9.0"),
            "...and must round-trip through the IDE's own loader");
    }

    [Test]
    public async Task CreateProject_WinForms_DefinesTheHighDpiMode()
    {
        var result = await _service.CreateProjectAsync(new CreateProjectOptions
        {
            Name = "Dpi",
            Location = _rootDir,
            SolutionType = SolutionTypes.DotNet,
            Template = ProjectTemplates.All.Single(t => t.Id == "winforms-app"),
            CreateSolutionFolder = false,
            CreateGitRepository = false
        });

        Assert.That(result.Success, Is.True, result.Error);
        Assert.That(File.ReadAllText(result.ProjectPath!),
            Does.Contain("<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>"),
            "a WinForms project must declare a DPI mode: in the legacy unaware mode the designer's " +
            "pixel coordinates and the running window's are different units on a scaled display");

        var reloaded = await new VisualGameStudio.ProjectSystem.Serialization.ProjectSerializer()
            .LoadAsync(result.ProjectPath!);
        Assert.That(reloaded.ApplicationHighDpiMode, Is.EqualTo("PerMonitorV2"));
    }

    // ------------------------------------------------------------------
    // The same "never widen the default" rule, applied to BuildService
    // ------------------------------------------------------------------
    //
    // ProjectTemplateService's mapping was hardened first; BuildService kept two dispatches that
    // still ended in a silent C# fallback, so a backend with no arm built C# and reported success.
    // These pin the one that is reachable from a unit test. The codegen switch in BuildProject is
    // inline in a large async method and cannot be invoked without a real compile — it is covered
    // by TemplateBuildSweepTests (Integration), and its default now throws rather than emitting C#.

    private static MethodInfo GetBackendIdMethod()
    {
        var method = typeof(BuildService).GetMethod(
            "GetBackendId", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null,
            "BuildService.GetBackendId was renamed or removed — this test pins its totality, so " +
            "update it rather than deleting it.");
        return method!;
    }

    [Test]
    public void GetBackendId_MapsEveryTargetBackend_WithNoSilentCSharpFallback()
    {
        var method = GetBackendIdMethod();
        var expected = new Dictionary<TargetBackend, string>
        {
            [TargetBackend.CSharp] = "csharp",
            [TargetBackend.Cpp] = "cpp",
            [TargetBackend.LLVM] = "llvm",
            [TargetBackend.MSIL] = "msil",
            [TargetBackend.JavaScript] = "javascript",
        };

        foreach (TargetBackend backend in Enum.GetValues<TargetBackend>())
        {
            Assert.That(expected.ContainsKey(backend), Is.True,
                $"TargetBackend gained '{backend}' — add its id here AND an explicit arm to " +
                "BuildService.GetBackendId. A missing arm must not fall back to C#.");
            Assert.That(method.Invoke(null, new object[] { backend }), Is.EqualTo(expected[backend]),
                $"backend '{backend}' must map to its own id");
        }
    }

    [Test]
    public void GetBackendId_Throws_ForABackendWithNoArm()
    {
        // The regression itself: before this, an unmapped value returned "csharp" and the build
        // went on to emit C# and report success. Cast past the end of the enum to stand in for a
        // member someone adds tomorrow without touching the switch.
        var unmapped = (TargetBackend)9999;

        var ex = Assert.Throws<TargetInvocationException>(
            () => GetBackendIdMethod().Invoke(null, new object[] { unmapped }));

        Assert.That(ex!.InnerException, Is.TypeOf<NotSupportedException>(),
            "an unmapped backend must fail loudly, not silently build C#");
        Assert.That(ex.InnerException!.Message, Does.Contain("no backend-id mapping"));
    }

    // ------------------------------------------------------------------
    // winforms-app no longer offers the MSIL pipeline
    // ------------------------------------------------------------------

    [Test]
    public void WinFormsTemplate_DoesNotOfferMsil()
    {
        var winforms = ProjectTemplates.All.Single(t => t.Id == "winforms-app");
        Assert.That(winforms.SupportedSolutionTypes, Does.Not.Contain("msil"),
            "the MSIL pipeline stops at a .il file — it never produces the executable a WinForms " +
            "app is, and the combination has never been exercised");
        Assert.That(winforms.SupportedSolutionTypes, Does.Contain("dotnet"),
            "...but the template must still be reachable from the .NET solution type");
    }

    [Test]
    public void MsilSolutionType_KeepsTemplateCoverage_AfterWinFormsDroppedIt()
    {
        // Pins WHY dropping "msil" from winforms-app was safe: EverySolutionType_HasAtLeastOneTemplate
        // would have started failing if winforms-app had been msil's only template.
        var msilTemplates = ProjectTemplates.All
            .Where(t => t.SupportedSolutionTypes.Contains("msil"))
            .Select(t => t.Id)
            .ToList();

        Assert.That(msilTemplates, Is.Not.Empty,
            "the msil solution type would now render an empty template list in the wizard");
        Assert.That(msilTemplates, Does.Contain("console-app"));
    }
}
