using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Serialization;

/// <summary>
/// The guard for the most dangerous behaviour this feature touches.
///
/// <para>⛔⛔ <c>ProjectFile.GetSourceFiles()</c> globs <c>**/*.bas</c> <b>only while the project has
/// no explicit <c>&lt;Compile&gt;</c> items</b>; the first one flips it to the explicit list. So
/// adding ONE file — a new form, a new class, anything — to a project that had been relying on the
/// glob silently removes every other source from the build. No diagnostic: the build compiles one
/// file and reports success.</para>
/// </summary>
[TestFixture]
public class ProjectGlobSafetyTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-glob-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    private BasicLangProject ProjectWith(params string[] relativeFiles)
    {
        foreach (var relative in relativeFiles)
        {
            var path = Path.Combine(_dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "' " + relative + "\n");
        }

        return new BasicLangProject { FilePath = Path.Combine(_dir, "App.blproj"), Name = "App" };
    }

    [Test]
    public void Materialise_ListsWhatTheGlobWouldHaveFound()
    {
        var project = ProjectWith("Program.bas", "Helper.bas", Path.Combine("sub", "Nested.bas"));

        var added = ProjectGlobSafety.MaterialiseGlobbedSources(project);

        Assert.Multiple(() =>
        {
            Assert.That(added, Has.Count.EqualTo(3));
            Assert.That(project.Items.Select(i => i.Include),
                Is.EquivalentTo(new[] { "Program.bas", "Helper.bas", Path.Combine("sub", "Nested.bas") }));
            Assert.That(project.Items, Has.All.Property(nameof(ProjectItem.ItemType))
                .EqualTo(ProjectItemType.Compile));
        });
    }

    [Test]
    public void Materialise_RecursesLikeTheCompilersGlobDoes()
    {
        // ⛔ The compiler's default branch uses SearchOption.AllDirectories. Listing only the top
        // directory would drop every nested source at the moment the project becomes explicit —
        // which is the exact failure this class exists to prevent, just narrower.
        var project = ProjectWith(Path.Combine("a", "b", "Deep.bas"));

        ProjectGlobSafety.MaterialiseGlobbedSources(project);

        Assert.That(project.Items.Single().Include, Is.EqualTo(Path.Combine("a", "b", "Deep.bas")));
    }

    [Test]
    public void Materialise_CoversEveryExtensionTheCompilerGlobs()
    {
        var project = ProjectWith(ProjectFile.BasicLangSourceExtensions.Select((e, i) => $"File{i}{e}").ToArray());

        ProjectGlobSafety.MaterialiseGlobbedSources(project);

        Assert.That(project.Items, Has.Count.EqualTo(ProjectFile.BasicLangSourceExtensions.Length),
            "any extension this misses is one that vanishes from the build when the project " +
            "becomes explicit");
    }

    [Test]
    public void Materialise_IsANoOp_WhenTheProjectIsAlreadyExplicit()
    {
        var project = ProjectWith("Program.bas", "Helper.bas");
        project.Items.Add(new ProjectItem("Program.bas", ProjectItemType.Compile));

        var added = ProjectGlobSafety.MaterialiseGlobbedSources(project);

        Assert.Multiple(() =>
        {
            Assert.That(added, Is.Empty, "an explicit project's list is the user's, not ours to widen");
            Assert.That(project.Items, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Materialise_SkipsBuildOutput()
    {
        // ⚠ The one deliberate divergence from the compiler's glob, which DOES sweep bin\ and obj\.
        // That is harmless while the list is recomputed every build, but freezing generated output
        // into the project file is permanent — the project would compile its own artifacts forever.
        var project = ProjectWith(
            "Program.bas",
            Path.Combine("bin", "Debug", "Generated.bas"),
            Path.Combine("obj", "gen", "Temp.bas"));

        ProjectGlobSafety.MaterialiseGlobbedSources(project);

        Assert.That(project.Items.Select(i => i.Include), Is.EqualTo(new[] { "Program.bas" }));
    }

    [Test]
    public void Materialise_ToleratesAProjectWhoseDirectoryDoesNotExist()
    {
        var project = new BasicLangProject
        {
            FilePath = Path.Combine(_dir, "gone", "App.blproj"), Name = "App"
        };

        Assert.DoesNotThrow(() => ProjectGlobSafety.MaterialiseGlobbedSources(project));
    }

    // ==================================================================
    // The end-to-end property: the build must still see everything
    // ==================================================================

    [Test]
    public async Task AddingAFormAfterMaterialising_KeepsEverySourceInTheBuild()
    {
        // The whole point, exercised through the real serializer and the real compiler-side loader.
        var project = ProjectWith("Program.bas", "Helper.bas");
        var serializer = new ProjectSerializer();
        await serializer.SaveAsync(project);

        var beforeGlob = ProjectFile.Load(project.FilePath).GetSourceFiles()
            .Select(Path.GetFileName).ToList();
        Assert.That(beforeGlob, Does.Contain("Program.bas").And.Contain("Helper.bas"),
            "sanity: with no <Compile> items the compiler globs both");

        ProjectGlobSafety.MaterialiseGlobbedSources(project);
        File.WriteAllText(Path.Combine(_dir, "LoginForm.blwebform"), "<WebForm Name=\"LoginForm\" Version=\"1\"/>");
        project.Items.Add(new ProjectItem("LoginForm.blwebform", ProjectItemType.Compile));
        await serializer.SaveAsync(project);

        var afterExplicit = ProjectFile.Load(project.FilePath).GetSourceFiles()
            .Select(Path.GetFileName).ToList();

        Assert.That(afterExplicit, Does.Contain("Program.bas").And.Contain("Helper.bas"),
            "⛔ adding the form must NOT have removed the existing sources from the build");
        Assert.That(afterExplicit, Does.Contain("LoginForm.blwebform"),
            "and the form itself must be listed, so the markup emitter can find it");
    }

    [Test]
    public async Task WithoutMaterialising_AddingAFormWouldHaveDroppedTheSources()
    {
        // The failure, pinned. If this ever stops failing to find the sources, the underlying
        // GetSourceFiles behaviour changed and ProjectGlobSafety may no longer be needed — which is
        // worth knowing rather than carrying dead machinery.
        var project = ProjectWith("Program.bas", "Helper.bas");
        var serializer = new ProjectSerializer();

        File.WriteAllText(Path.Combine(_dir, "LoginForm.blwebform"), "<WebForm Name=\"LoginForm\" Version=\"1\"/>");
        project.Items.Add(new ProjectItem("LoginForm.blwebform", ProjectItemType.Compile));
        await serializer.SaveAsync(project);

        var sources = ProjectFile.Load(project.FilePath).GetSourceFiles()
            .Select(Path.GetFileName).ToList();

        Assert.That(sources, Does.Not.Contain("Program.bas"),
            "this is the hazard ProjectGlobSafety exists for — one explicit item silences the glob");
    }
}
