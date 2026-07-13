using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Serialization;

[TestFixture]
public class ProjectSerializerCppTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-ideser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    [Test]
    public async Task Load_ParsesLanguageAndCppSettings()
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <CppStandard>c++20</CppStandard>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <IncludeDir Include="inc" />
                <NativeLib Include="VisualGameStudioEngine.lib" />
                <Define Include="FLAG" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var project = await new ProjectSerializer().LoadAsync(path);

        Assert.That(project.Language, Is.EqualTo(ProjectLanguage.Cpp));
        Assert.That(project.CppSettings, Is.Not.Null);
        Assert.That(project.CppSettings!.CppStandard, Is.EqualTo("c++20"));
        Assert.That(project.CppSettings.IncludeDirs, Is.EqualTo(new[] { "inc" }));
        Assert.That(project.CppSettings.NativeLibs, Is.EqualTo(new[] { "VisualGameStudioEngine.lib" }));
        Assert.That(project.CppSettings.Defines, Is.EqualTo(new[] { "FLAG" }));
    }

    [Test]
    public async Task SaveThenLoad_DoesNotStripLanguageOrCppItems()
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <IncludeDir Include="inc" />
              </ItemGroup>
            </BasicLangProject>
            """);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        await serializer.SaveAsync(project);   // the IDE save path that used to strip unknown elements
        var reloaded = await serializer.LoadAsync(path);

        Assert.That(reloaded.Language, Is.EqualTo(ProjectLanguage.Cpp),
            "an IDE save must not strip <Language> from a C++ project");
        Assert.That(reloaded.CppSettings, Is.Not.Null);
        Assert.That(reloaded.CppSettings!.IncludeDirs, Is.EqualTo(new[] { "inc" }));
    }

    [Test]
    public async Task SaveThenLoad_RoundTripsNonDefaultCppStandardAndAllItemKinds()
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <Language>Cpp</Language>
                <CppStandard>c++17</CppStandard>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <NativeLib Include="x.lib" />
                <Define Include="D1" />
              </ItemGroup>
            </BasicLangProject>
            """);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        await serializer.SaveAsync(project);
        var reloaded = await serializer.LoadAsync(path);

        Assert.That(reloaded.CppSettings, Is.Not.Null);
        Assert.That(reloaded.CppSettings!.CppStandard, Is.EqualTo("c++17"),
            "a non-default CppStandard must survive an IDE save round-trip");
        Assert.That(reloaded.CppSettings.NativeLibs, Is.EqualTo(new[] { "x.lib" }));
        Assert.That(reloaded.CppSettings.Defines, Is.EqualTo(new[] { "D1" }));
    }

    [Test]
    public async Task Load_DefaultsToBasicLang_ForOldProjects()
    {
        var path = Path.Combine(_dir, "Old.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Old</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        var project = await new ProjectSerializer().LoadAsync(path);
        Assert.That(project.Language, Is.EqualTo(ProjectLanguage.BasicLang));
        Assert.That(project.CppSettings, Is.Null);
    }

    [Test]
    public async Task Save_BasicLangProject_EmitsNoLanguageElement()
    {
        var path = Path.Combine(_dir, "BL.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>BL</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        await serializer.SaveAsync(project);
        Assert.That(File.ReadAllText(path), Does.Not.Contain("<Language>"),
            "old-format files must stay untouched on IDE save");
    }
}
