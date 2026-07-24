using System.Text;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Every real .blproj/.blsln in this repo is BOM-less UTF-8. A plain
/// <c>XDocument.Save(path)</c> injects a UTF-8 BOM, which corrupts the file for
/// other tooling — the same hazard CLAUDE.md warns about for PowerShell
/// Get-Content/Set-Content round-trips, reached through a different mechanism.
/// </summary>
[TestFixture]
public class ProjectFileEncodingTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-projenc-" + Guid.NewGuid().ToString("N"));
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

    private static bool HasUtf8Bom(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

    private string WriteBomlessProject()
    {
        var path = Path.Combine(_dir, "Test.blproj");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Test</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <CppStandard>c++17</CppStandard>
                <CppToolchain>gcc</CppToolchain>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
              </ItemGroup>
            </BasicLangProject>
            """, new UTF8Encoding(false));
        return path;
    }

    [Test]
    public void Save_keeps_the_blproj_bom_less()
    {
        var path = WriteBomlessProject();
        Assert.That(HasUtf8Bom(File.ReadAllBytes(path)), Is.False,
            "fixture must start BOM-less, otherwise this test proves nothing");

        ProjectFile.Load(path).Save(path);

        Assert.That(HasUtf8Bom(File.ReadAllBytes(path)), Is.False,
            ".blproj must stay BOM-less UTF-8 after Save()");
    }

    [Test]
    public void Save_round_trip_preserves_project_content()
    {
        var path = WriteBomlessProject();

        ProjectFile.Load(path).Save(path);
        var reloaded = ProjectFile.Load(path);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.ProjectName, Is.EqualTo("Test"));
            Assert.That(reloaded.OutputType, Is.EqualTo("Exe"));
            Assert.That(reloaded.Language, Is.EqualTo("Cpp"));
            Assert.That(reloaded.CppStandard, Is.EqualTo("c++17"));
            Assert.That(reloaded.CppToolchain, Is.EqualTo("gcc"));
            Assert.That(reloaded.SourceFiles, Does.Contain("main.cpp"));
        });
    }
}
