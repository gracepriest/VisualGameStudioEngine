using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #134: <see cref="ProjectFile.OptimizationsEnabledFor"/> is the ONE answer to "does a build
/// in this configuration optimize?" — the CLI's managed build (C#, JavaScript, MSIL), the native
/// <c>CppProjectBuilder</c> (the CLI's and the IDE's C++ build and IntelliSense's <c>obj/gen</c>)
/// and the native <c>-O2</c>/<c>/O2</c> request all ask it, and a yes means the AGGRESSIVE IR
/// pipeline. This fixture pins the answer itself; <see cref="CppProjectOptimizerPipelineTests"/>
/// pins that every C++ route obeys it.
///
/// <para>Where a pin below records what the code DOES rather than what anyone would design, it says
/// so: the case-sensitive lookup and the replace-not-merge of a redeclared configuration are
/// <c>Dictionary</c> and loader behavior this change inherited, not chose.</para>
/// </summary>
[TestFixture]
public class ProjectFileOptimizationsEnabledForTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-optfor-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A loaded <c>.blproj</c>: <paramref name="projectWide"/> inside the one unconditional PropertyGroup, then the conditional ones.</summary>
    private ProjectFile Load(string projectWide = "", string configurationGroups = "")
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>Cpp</TargetBackend>
                {projectWide}
              </PropertyGroup>
            {configurationGroups}
            </BasicLangProject>
            """);
        return ProjectFile.Load(path);
    }

    private static string Group(string name, string body) =>
        $"<PropertyGroup Condition=\" '$(Configuration)' == '{name}' \">{body}</PropertyGroup>\n";

    // ---- the defaults ----

    [Test]
    public void ADefaultProject_OptimizesInRelease_AndNotInDebug()
    {
        var project = new ProjectFile();

        Assert.Multiple(() =>
        {
            Assert.That(project.OptimizationsEnabledFor("Release"), Is.True);
            Assert.That(project.OptimizationsEnabledFor("Debug"), Is.False);
        });
    }

    [Test]
    public void ALoadedProjectThatDeclaresNoConfiguration_KeepsThoseDefaults()
    {
        var project = Load();

        Assert.Multiple(() =>
        {
            Assert.That(project.OptimizationsEnabledFor("Release"), Is.True);
            Assert.That(project.OptimizationsEnabledFor("Debug"), Is.False);
        });
    }

    // ---- a configuration the project does not declare ----

    [TestCase("Foo")]
    [TestCase("Profile")]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("Release ")]
    public void AConfigurationTheProjectDoesNotDeclare_DoesNotOptimize(string configuration)
    {
        Assert.That(Load().OptimizationsEnabledFor(configuration), Is.False);
    }

    [Test]
    public void RemovingTheReleaseConfiguration_StopsReleaseOptimizing()
    {
        var project = new ProjectFile();
        project.Configurations.Remove("Release");

        Assert.That(project.OptimizationsEnabledFor("Release"), Is.False,
            "no declared configuration, no optimization: the answer comes from the dictionary, not from the name");
    }

    // ---- case: the dictionary's own ----

    /// <summary>
    /// ⚠ PINNED AS IMPLEMENTED: the lookup is the dictionary's, and the dictionary is
    /// ordinal/case-sensitive, so `-c release` and `-c RELEASE` do not find `Release` and do not
    /// optimize. Every C++ route and the CLI's managed route share this. The IDE's MANAGED build
    /// does NOT: it reads its own project model (<c>VisualGameStudio.Core.Models.BuildConfiguration.Optimize</c>
    /// off <c>BuildService.CurrentConfiguration</c>), which is a follow-up and not this ticket. If a
    /// future change makes the lookup case-insensitive this test should change with it, deliberately.
    /// </summary>
    [TestCase("release")]
    [TestCase("RELEASE")]
    [TestCase("rElEaSe")]
    public void TheLookupIsCaseSensitive_AsTheDictionaryIs(string configuration)
    {
        var project = Load();

        Assert.Multiple(() =>
        {
            Assert.That(project.OptimizationsEnabledFor("Release"), Is.True, "setup: the exact spelling does optimize");
            Assert.That(project.OptimizationsEnabledFor(configuration), Is.False);
        });
    }

    // ---- a declared configuration decides for itself ----

    [TestCase("Debug", "true", true)]
    [TestCase("Debug", "false", false)]
    [TestCase("Release", "true", true)]
    [TestCase("Release", "false", false)]
    [TestCase("Profile", "true", true)]
    [TestCase("Profile", "false", false)]
    [TestCase("Staging", "True", true)]
    [TestCase("Staging", "FALSE", false)]
    public void ADeclaredConfiguration_FollowsItsOwnOptimizeElement(string name, string optimize, bool expected)
    {
        var project = Load(configurationGroups: Group(name, $"<Optimize>{optimize}</Optimize>"));

        Assert.That(project.OptimizationsEnabledFor(name), Is.EqualTo(expected));
    }

    [Test]
    public void EachConfigurationAnswersForItself_NotForTheOthers()
    {
        var project = Load(configurationGroups:
            Group("Debug", "<Optimize>true</Optimize>") +
            Group("Release", "<Optimize>false</Optimize>") +
            Group("Profile", "<Optimize>true</Optimize>"));

        Assert.Multiple(() =>
        {
            Assert.That(project.OptimizationsEnabledFor("Debug"), Is.True);
            Assert.That(project.OptimizationsEnabledFor("Release"), Is.False);
            Assert.That(project.OptimizationsEnabledFor("Profile"), Is.True);
            Assert.That(project.OptimizationsEnabledFor("Other"), Is.False);
        });
    }

    /// <summary>
    /// ⚠ PINNED AS IMPLEMENTED: a declared configuration REPLACES the built-in one of its name (the
    /// loader assigns <c>Configurations[name]</c>; it does not merge), and a <c>BuildConfiguration</c>
    /// defaults to <c>OptimizationsEnabled = false</c>. So a <c>Release</c> that only sets
    /// <c>DefineConstants</c> or <c>DebugSymbols</c> has silently stopped optimizing. That is the
    /// loader's rule, shared with every backend, and not something #134 changed.
    /// </summary>
    [TestCase("<DebugSymbols>false</DebugSymbols>")]
    [TestCase("<DefineConstants>RELEASE;TRACE</DefineConstants>")]
    [TestCase("")]
    public void ARedeclaredRelease_ThatSaysNothingAboutOptimize_NoLongerOptimizes(string body)
    {
        var project = Load(configurationGroups: Group("Release", body));

        Assert.That(project.OptimizationsEnabledFor("Release"), Is.False);
    }

    [Test]
    public void ARedeclaredDebug_ThatSaysNothingAboutOptimize_StillDoesNotOptimize()
    {
        var project = Load(configurationGroups: Group("Debug", "<DebugSymbols>true</DebugSymbols>"));

        Assert.That(project.OptimizationsEnabledFor("Debug"), Is.False);
    }

    // ---- what it does NOT consult ----

    /// <summary>
    /// The project-wide <c>&lt;Optimize&gt;</c> (the unconditional PropertyGroup) is read into
    /// <see cref="ProjectFile.OptimizationsEnabled"/> and is NOT consulted: only the named
    /// configuration decides. (Its default is <c>true</c>, so were it consulted, an undeclared
    /// configuration and Debug would optimize.)
    /// </summary>
    [TestCase("false", "Release", true)]
    [TestCase("true", "Debug", false)]
    [TestCase("true", "Foo", false)]
    [TestCase("false", "Foo", false)]
    public void TheProjectWideOptimize_IsNotConsulted(string projectWide, string configuration, bool expected)
    {
        var project = Load(projectWide: $"<Optimize>{projectWide}</Optimize>");

        Assert.Multiple(() =>
        {
            Assert.That(project.OptimizationsEnabled, Is.EqualTo(bool.Parse(projectWide)), "setup: the flag was read");
            Assert.That(project.OptimizationsEnabledFor(configuration), Is.EqualTo(expected));
        });
    }

    [Test]
    public void TheProjectWideDefault_IsTrue_YetAnUndeclaredConfigurationStillDoesNotOptimize()
    {
        var project = new ProjectFile();

        Assert.Multiple(() =>
        {
            Assert.That(project.OptimizationsEnabled, Is.True, "setup: the project-wide default");
            Assert.That(project.OptimizationsEnabledFor("Foo"), Is.False);
            Assert.That(project.OptimizationsEnabledFor("Debug"), Is.False);
        });
    }

    [TestCase(true, false, true)]
    [TestCase(false, true, false)]
    public void ItAsksOptimize_NotDebugSymbols(bool optimize, bool debugSymbols, bool expected)
    {
        var project = new ProjectFile();
        project.Configurations["X"] = new BuildConfiguration
        {
            Name = "X",
            OptimizationsEnabled = optimize,
            DebugSymbols = debugSymbols,
        };

        Assert.That(project.OptimizationsEnabledFor("X"), Is.EqualTo(expected));
    }

    // ---- a configuration assigned in code, and a saved project ----

    [Test]
    public void AConfigurationAssignedInCode_IsHonoured_AndTheLiveValueIsRead()
    {
        var project = new ProjectFile();
        project.Configurations["Perf"] = new BuildConfiguration { Name = "Perf", OptimizationsEnabled = true };
        Assert.That(project.OptimizationsEnabledFor("Perf"), Is.True);

        project.Configurations["Perf"].OptimizationsEnabled = false;
        Assert.That(project.OptimizationsEnabledFor("Perf"), Is.False, "not cached: the dictionary is read on every call");
    }

    [Test]
    public void ASavedProject_KeepsTheAnswerForEveryConfiguration()
    {
        var project = Load(configurationGroups:
            Group("Debug", "<Optimize>true</Optimize>") +
            Group("Release", "<Optimize>false</Optimize>") +
            Group("Profile", "<Optimize>true</Optimize>"));
        var resaved = Path.Combine(_dir, "Resaved.blproj");
        project.Save(resaved);

        var reloaded = ProjectFile.Load(resaved);

        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "Debug", "Release", "Profile", "Undeclared" })
                Assert.That(reloaded.OptimizationsEnabledFor(name), Is.EqualTo(project.OptimizationsEnabledFor(name)), name);
        });
    }
}
