using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Project files store paths MSBuild-style with BACKSLASHES — every Windows-authored
/// <c>.blproj</c> does. On Linux/macOS a backslash is an ordinary file-name character, so before
/// <see cref="ProjectFile.ToLocalPath"/> a <c>&lt;Compile Include="Source\Main.bas"/&gt;</c> named
/// one file literally called <c>Source\Main.bas</c>: the CLI's <c>GetSourceFiles</c> matched
/// nothing and SILENTLY dropped it from the build, and every relative HintPath failed BL6021.
///
/// <para>Both loaders are pinned (CLAUDE.md: test both entry points) — the CLI's
/// <see cref="ProjectFile"/> and the IDE's <see cref="ProjectSerializer"/>. On Windows these pass
/// trivially (the conversion is a no-op there); they are the Linux/macOS regression guard.</para>
/// </summary>
[TestFixture]
public class ProjectFilePathSeparatorTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-sep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "Source"));
        Directory.CreateDirectory(Path.Combine(_dir, "lib"));
        File.WriteAllText(Path.Combine(_dir, "Source", "Main.bas"), "Sub Main()\nEnd Sub\n");
        File.WriteAllText(Path.Combine(_dir, "lib", "MyLib.dll"), "");
        File.WriteAllText(Path.Combine(_dir, "App.blproj"), """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Source\Main.bas" />
                <Reference Include="MyLib">
                  <HintPath>lib\MyLib.dll</HintPath>
                </Reference>
              </ItemGroup>
            </BasicLangProject>
            """);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    [Test]
    public void Cli_ABackslashedCompileInclude_IsNotDroppedFromTheBuild()
    {
        var project = ProjectFile.Load(Path.Combine(_dir, "App.blproj"));

        Assert.That(project.GetSourceFiles().Select(Path.GetFullPath),
            Is.EquivalentTo(new[] { Path.GetFullPath(Path.Combine(_dir, "Source", "Main.bas")) }),
            "Source\\Main.bas must resolve to the file in the Source folder, not vanish");
    }

    [Test]
    public void Cli_ABackslashedHintPath_PointsAtTheFileOnDisk()
    {
        var project = ProjectFile.Load(Path.Combine(_dir, "App.blproj"));

        var hint = project.AssemblyReferences.Single(r => r.Name == "MyLib").HintPath;
        Assert.That(File.Exists(Path.Combine(_dir, hint!)), Is.True, $"HintPath '{hint}' does not resolve");
    }

    [Test]
    public async Task Ide_ABackslashedCompileInclude_ResolvesToTheFileOnDisk()
    {
        var project = await new ProjectSerializer().LoadAsync(Path.Combine(_dir, "App.blproj"));

        var item = project.Items.Single(i => i.FileName == "Main.bas");
        Assert.That(File.Exists(Path.Combine(project.ProjectDirectory, item.Include)), Is.True,
            $"Include '{item.Include}' does not resolve to Source/Main.bas");
    }

    [Test]
    public async Task Ide_ABackslashedSolutionProjectPath_ResolvesToTheProjectFile()
    {
        var slnDir = Path.Combine(_dir, "sln");
        Directory.CreateDirectory(Path.Combine(slnDir, "ProjectA"));
        File.WriteAllText(Path.Combine(slnDir, "ProjectA", "ProjectA.blproj"), "<BasicLangProject />");
        var slnPath = Path.Combine(slnDir, "My.blsln");
        File.WriteAllText(slnPath, """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangSolution>
              <Projects>
                <Project Name="ProjectA" Path="ProjectA\ProjectA.blproj" Type="Exe" />
              </Projects>
            </BasicLangSolution>
            """);

        var solution = await new SolutionSerializer().LoadAsync(slnPath);

        var project = solution.Projects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(project.AbsolutePath), Is.True,
                $"AbsolutePath '{project.AbsolutePath}' does not point at ProjectA/ProjectA.blproj");
            Assert.That(File.Exists(project.GetFullPath(slnDir)), Is.True, "GetFullPath must agree");
            Assert.That(project.RelativePath, Is.EqualTo(@"ProjectA\ProjectA.blproj"),
                "the stored path stays verbatim so a solution save round-trips it");
        });
    }

    [Test]
    public void ToLocalPath_UsesThePlatformSeparator()
        => Assert.That(ProjectFile.ToLocalPath(@"Source\Sub\Main.bas"),
            Is.EqualTo(Path.Combine("Source", "Sub", "Main.bas")));
}
