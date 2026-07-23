using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class BlprojReferenceWriterTests
{
    private readonly System.Collections.Generic.List<string> _tempFiles = new();

    [TearDown]
    public void TearDown()
    {
        foreach (var path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
        _tempFiles.Clear();
    }

    [Test]
    public void Adds_project_reference_and_is_idempotent()
    {
        var path = WriteTempBlproj("""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>B</ProjectName>
              </PropertyGroup>
              <ItemGroup>
              </ItemGroup>
            </BasicLangProject>
            """);

        BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");
        BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");   // idempotent

        var refs = XDocument.Load(path).Descendants("ProjectReference")
            .Where(e => string.Equals((string?)e.Attribute("Include"), @"..\A\A.blproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(refs, Has.Count.EqualTo(1));
    }

    [Test]
    public void Preserves_existing_content()
    {
        var path = WriteTempBlproj("""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>B</ProjectName>
                <OutputType>Exe</OutputType>
                <RootNamespace>B</RootNamespace>
                <TargetBackend>Cpp</TargetBackend>
                <CppStandard>c++20</CppStandard>
                <CppToolchain>msvc</CppToolchain>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);

        BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");

        var doc = XDocument.Load(path);
        Assert.That((string?)doc.Descendants("ProjectName").FirstOrDefault(), Is.EqualTo("B"));
        Assert.That((string?)doc.Descendants("OutputType").FirstOrDefault(), Is.EqualTo("Exe"));
        Assert.That((string?)doc.Descendants("RootNamespace").FirstOrDefault(), Is.EqualTo("B"));
        Assert.That((string?)doc.Descendants("TargetBackend").FirstOrDefault(), Is.EqualTo("Cpp"));
        Assert.That((string?)doc.Descendants("CppStandard").FirstOrDefault(), Is.EqualTo("c++20"));
        Assert.That((string?)doc.Descendants("CppToolchain").FirstOrDefault(), Is.EqualTo("msvc"));

        var compileItems = doc.Descendants("Compile")
            .Where(e => string.Equals((string?)e.Attribute("Include"), "Main.bas", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(compileItems, Has.Count.EqualTo(1));

        var refs = doc.Descendants("ProjectReference")
            .Where(e => string.Equals((string?)e.Attribute("Include"), @"..\A\A.blproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(refs, Has.Count.EqualTo(1));
    }

    [Test]
    public void Writes_without_a_utf8_bom()
    {
        var path = WriteTempBlproj("""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>B</ProjectName>
              </PropertyGroup>
              <ItemGroup>
              </ItemGroup>
            </BasicLangProject>
            """);

        BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");

        var bytes = File.ReadAllBytes(path);
        Assert.That(bytes.Length, Is.GreaterThan(3));
        Assert.That(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, Is.False,
            "the .blproj must stay BOM-less UTF-8");
    }

    [Test]
    public void Creates_an_ItemGroup_when_none_exists()
    {
        var path = WriteTempBlproj("""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>B</ProjectName>
              </PropertyGroup>
            </BasicLangProject>
            """);

        BlprojReferenceWriter.AddReference(path, @"..\A\A.blproj");

        var doc = XDocument.Load(path);
        var refs = doc.Descendants("ProjectReference")
            .Where(e => string.Equals((string?)e.Attribute("Include"), @"..\A\A.blproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(refs, Has.Count.EqualTo(1));
    }

    private string WriteTempBlproj(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"BlprojReferenceWriterTest_{Guid.NewGuid()}.blproj");
        File.WriteAllText(path, xml, new UTF8Encoding(false));
        _tempFiles.Add(path);
        return path;
    }
}
