using System.Text;
using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// The IDE serializer must round-trip &lt;PackageReference&gt;. Without it, any IDE save of a
/// .blproj silently DELETES its NuGet package references (the compiler-side ProjectFile and
/// BuildService restore packages from these — dropping them breaks the build).
/// </summary>
[TestFixture]
public class ProjectSerializerPackageReferenceTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vgs-projpkg-" + Guid.NewGuid().ToString("N"));
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

    private string WriteProjectWithPackages()
    {
        var path = Path.Combine(_dir, "P.blproj");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>P</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>CSharp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas" />
              </ItemGroup>
              <ItemGroup>
                <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                <PackageReference Include="Serilog">
                  <Version>3.1.1</Version>
                </PackageReference>
              </ItemGroup>
            </BasicLangProject>
            """, new UTF8Encoding(false));
        return path;
    }

    [Test]
    public async Task Load_parses_PackageReference_name_and_version()
    {
        var project = await new ProjectSerializer().LoadAsync(WriteProjectWithPackages());

        var json = project.PackageReferences.FirstOrDefault(p => p.Name == "Newtonsoft.Json");
        var serilog = project.PackageReferences.FirstOrDefault(p => p.Name == "Serilog");

        Assert.Multiple(() =>
        {
            Assert.That(project.PackageReferences, Has.Count.EqualTo(2));
            Assert.That(json, Is.Not.Null);
            Assert.That(json!.Version, Is.EqualTo("13.0.3"));       // Version attribute
            Assert.That(serilog, Is.Not.Null);
            Assert.That(serilog!.Version, Is.EqualTo("3.1.1"));     // <Version> child element
        });
    }

    [Test]
    public async Task Save_round_trip_preserves_PackageReferences()
    {
        var serializer = new ProjectSerializer();
        var path = WriteProjectWithPackages();

        var project = await serializer.LoadAsync(path);
        await serializer.SaveAsync(project);          // the save that used to drop them

        var xml = XDocument.Load(path);
        // ⚠ Version is read from EITHER form. It used to be safe to read only the attribute,
        // because SaveAsync rebuilt the document from the model and re-emitted every package in
        // attribute form. That normalization was a side effect of the rebuild that destroyed
        // unmodeled content, and it went away with it: a preserving save now leaves Serilog in the
        // <Version> child-element form the fixture wrote. Preserving the author's spelling is the
        // point, so this assertion pins the VERSION, not the syntax carrying it.
        var pkgs = xml.Descendants("PackageReference")
            .Select(e => ((string?)e.Attribute("Include"),
                          (string?)e.Attribute("Version") ?? (string?)e.Element("Version")))
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(pkgs, Has.Count.EqualTo(2));
            Assert.That(pkgs, Does.Contain(("Newtonsoft.Json", "13.0.3")));
            Assert.That(pkgs, Does.Contain(("Serilog", "3.1.1")));
        });

        // reload confirms the model survived a full round-trip
        var reloaded = await serializer.LoadAsync(path);
        Assert.That(reloaded.PackageReferences.Select(p => p.Name),
            Is.EquivalentTo(new[] { "Newtonsoft.Json", "Serilog" }));
    }
}
