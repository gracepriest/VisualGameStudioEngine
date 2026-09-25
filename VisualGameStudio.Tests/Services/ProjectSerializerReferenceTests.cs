using System.Text;
using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// The IDE serializer must round-trip &lt;ProjectReference&gt;. Without it, any IDE
/// save of a .blproj silently DELETES project-to-project references — including the
/// ones the Solution Explorer "Add Project Reference" command writes, which feed
/// cross-project IntelliSense via the LSP WorkspaceManager.
/// </summary>
[TestFixture]
public class ProjectSerializerReferenceTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vgs-projser-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A content-rich .blproj: assembly ref + project ref + C++ settings + items.</summary>
    private string WriteRichProject()
    {
        var path = Path.Combine(_dir, "B.blproj");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>B</ProjectName>
                <OutputType>Exe</OutputType>
                <RootNamespace>B</RootNamespace>
                <TargetBackend>Cpp</TargetBackend>
                <CppStandard>c++20</CppStandard>
                <CppToolchain>gcc</CppToolchain>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Program.bas" />
              </ItemGroup>
              <ItemGroup>
                <Reference Include="System.Core">
                  <HintPath>lib\System.Core.dll</HintPath>
                </Reference>
                <ProjectReference Include="..\A\A.blproj" />
              </ItemGroup>
              <ItemGroup>
                <IncludeDir Include="vendor/include" />
                <NativeLib Include="Engine.lib" />
                <Define Include="MY_FLAG" />
              </ItemGroup>
            </BasicLangProject>
            """, new UTF8Encoding(false));
        return path;
    }

    [Test]
    public async Task Load_parses_ProjectReference_distinctly_from_assembly_Reference()
    {
        var project = await new ProjectSerializer().LoadAsync(WriteRichProject());

        var projRefs = project.References.Where(r => r.IsProjectReference).ToList();
        var asmRefs = project.References.Where(r => !r.IsProjectReference).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(projRefs, Has.Count.EqualTo(1), "the <ProjectReference> must be loaded");
            Assert.That(projRefs[0].Path, Is.EqualTo(@"..\A\A.blproj"), "the raw Include path must survive");
            Assert.That(asmRefs, Has.Count.EqualTo(1), "the assembly <Reference> must stay separate");
            Assert.That(asmRefs[0].Name, Is.EqualTo("System.Core"));
        });
    }

    [Test]
    public async Task Save_round_trip_preserves_ProjectReference()
    {
        var serializer = new ProjectSerializer();
        var path = WriteRichProject();

        var project = await serializer.LoadAsync(path);
        await serializer.SaveAsync(project);          // the save that used to drop it

        var xml = XDocument.Load(path);
        var projRefIncludes = xml.Descendants("ProjectReference")
            .Select(e => (string?)e.Attribute("Include")).ToList();

        Assert.That(projRefIncludes, Does.Contain(@"..\A\A.blproj"),
            "an IDE save must not delete the project reference");

        // and the assembly reference must not be turned into a project reference
        var asmIncludes = xml.Descendants("Reference")
            .Select(e => (string?)e.Attribute("Include")).ToList();
        Assert.That(asmIncludes, Does.Contain("System.Core"));
    }

    [Test]
    public async Task Save_round_trip_preserves_the_rest_of_the_project()
    {
        var serializer = new ProjectSerializer();
        var path = WriteRichProject();

        var project = await serializer.LoadAsync(path);
        await serializer.SaveAsync(project);
        var reloaded = await serializer.LoadAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.Name, Is.EqualTo("B"));
            Assert.That(reloaded.OutputType, Is.EqualTo(OutputType.Exe));
            Assert.That(reloaded.TargetBackend, Is.EqualTo(TargetBackend.Cpp));
            Assert.That(reloaded.CppSettings?.CppStandard, Is.EqualTo("c++20"));
            Assert.That(reloaded.CppSettings?.CppToolchain, Is.EqualTo("gcc"));
            Assert.That(reloaded.CppSettings?.IncludeDirs, Does.Contain("vendor/include"));
            Assert.That(reloaded.CppSettings?.NativeLibs, Does.Contain("Engine.lib"));
            Assert.That(reloaded.CppSettings?.Defines, Does.Contain("MY_FLAG"));
            Assert.That(reloaded.Items.Select(i => i.Include), Does.Contain("Program.bas"));
            Assert.That(reloaded.References.Count(r => r.IsProjectReference), Is.EqualTo(1));
            Assert.That(reloaded.References.Count(r => !r.IsProjectReference), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Save_writes_bom_less_utf8()
    {
        var serializer = new ProjectSerializer();
        var path = WriteRichProject();

        var project = await serializer.LoadAsync(path);
        await serializer.SaveAsync(project);

        var bytes = await File.ReadAllBytesAsync(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.That(hasBom, Is.False, ".blproj must stay BOM-less UTF-8");
    }

    [Test]
    public async Task ANamespacedProject_RefusesToSave_WithACatchableType()
    {
        // ⛔⛔ A save that CANNOT happen must not report success. This path used to return
        // quietly: the IDE said "saved", the user believed their change was persisted, and it was
        // gone at the next reload. The structure-preserving writer cannot edit a namespaced file
        // without rebuilding it from a model that never read it, so it refuses.
        //
        // ⚠ And the refusal is a NAMED type. Eleven call sites await SaveProjectAsync and none of
        // them caught anything, so throwing a bare InvalidOperationException traded one silent
        // failure for an unhandled exception in whichever flow the user was in — in the form case,
        // after both new files were already on disk. Callers must be able to catch this one thing
        // without swallowing real bugs.
        var path = Path.Combine(_dir, "Namespaced.blproj");
        await File.WriteAllTextAsync(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject xmlns="http://schemas.microsoft.com/developer/msbuild/2003" Version="1.0">
              <PropertyGroup>
                <ProjectName>Namespaced</ProjectName>
              </PropertyGroup>
            </BasicLangProject>
            """);

        var before = await File.ReadAllTextAsync(path);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);
        project.Name = "Renamed";

        var refusal = Assert.ThrowsAsync<ProjectSaveRefusedException>(
            async () => await serializer.SaveAsync(project));

        Assert.Multiple(async () =>
        {
            Assert.That(refusal!.FilePath, Is.EqualTo(path), "the caller has to name the file");
            Assert.That(refusal.Message, Does.Contain("xmlns"), "and say what to do about it");
            Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(before),
                "refusing means the file is untouched, not half-written");
        });
    }
}
