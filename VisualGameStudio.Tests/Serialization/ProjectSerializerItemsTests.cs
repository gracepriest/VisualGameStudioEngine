using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Serialization;

[TestFixture]
public class ProjectSerializerItemsTests
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
    public async Task SaveThenLoad_DoesNotStripResourceItems()
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
                <Content Include="readme.txt" />
                <Resource Include="assets\sprite.png" />
                <Resource Include="assets\music.ogg" />
              </ItemGroup>
            </BasicLangProject>
            """);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        await serializer.SaveAsync(project);   // the IDE save path that used to strip <Resource> items
        var reloaded = await serializer.LoadAsync(path);

        Assert.That(
            reloaded.Items.Where(i => i.ItemType == ProjectItemType.Resource).Select(i => i.Include),
            Is.EqualTo(new[] { @"assets\sprite.png", @"assets\music.ogg" }),
            "an IDE save must not strip <Resource> items from the project file");
        Assert.That(
            reloaded.Items.Where(i => i.ItemType == ProjectItemType.Compile).Select(i => i.Include),
            Is.EqualTo(new[] { "Main.bas" }));
        Assert.That(
            reloaded.Items.Where(i => i.ItemType == ProjectItemType.Content).Select(i => i.Include),
            Is.EqualTo(new[] { "readme.txt" }));
    }
}
