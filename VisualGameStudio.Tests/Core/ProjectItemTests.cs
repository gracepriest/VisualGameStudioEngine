using NUnit.Framework;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class ProjectItemTests
{
    [Test]
    public void DefaultConstructor_SetsDefaultValues()
    {
        var item = new ProjectItem();

        Assert.That(item.Include, Is.EqualTo(""));
        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.None));
        Assert.That(item.Metadata, Is.Empty);
    }

    [Test]
    public void Constructor_WithParameters_SetsValues()
    {
        var item = new ProjectItem("Source\\Main.bas", ProjectItemType.Compile);

        Assert.That(item.Include, Is.EqualTo("Source\\Main.bas"));
        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Compile));
    }

    [Test]
    public void FileName_ExtractsFileName()
    {
        var item = new ProjectItem("Source\\SubFolder\\Main.bas", ProjectItemType.Compile);

        Assert.That(item.FileName, Is.EqualTo("Main.bas"));
    }

    [Test]
    public void FileName_WithSimplePath_ReturnsFileName()
    {
        var item = new ProjectItem("Main.bas", ProjectItemType.Compile);

        Assert.That(item.FileName, Is.EqualTo("Main.bas"));
    }

    [Test]
    public void FileName_WithEmptyInclude_ReturnsEmpty()
    {
        var item = new ProjectItem();

        Assert.That(item.FileName, Is.EqualTo(""));
    }

    [Test]
    public void Directory_ExtractsDirectory()
    {
        var item = new ProjectItem("Source\\SubFolder\\Main.bas", ProjectItemType.Compile);

        Assert.That(item.Directory, Is.EqualTo("Source\\SubFolder"));
    }

    [Test]
    public void Directory_WithSimpleFileName_ReturnsEmpty()
    {
        var item = new ProjectItem("Main.bas", ProjectItemType.Compile);

        Assert.That(item.Directory, Is.EqualTo(""));
    }

    [Test]
    public void Metadata_CanAddAndRetrieve()
    {
        var item = new ProjectItem("Main.bas", ProjectItemType.Compile);
        item.Metadata["SubType"] = "Code";
        item.Metadata["Generator"] = "MSBuild";

        Assert.That(item.Metadata["SubType"], Is.EqualTo("Code"));
        Assert.That(item.Metadata["Generator"], Is.EqualTo("MSBuild"));
    }

    [Test]
    public void ItemType_CanBeSetToCompile()
    {
        var item = new ProjectItem { ItemType = ProjectItemType.Compile };

        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Compile));
    }

    [Test]
    public void ItemType_CanBeSetToContent()
    {
        var item = new ProjectItem { ItemType = ProjectItemType.Content };

        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Content));
    }

    [Test]
    public void ItemType_CanBeSetToResource()
    {
        var item = new ProjectItem { ItemType = ProjectItemType.Resource };

        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Resource));
    }

    [Test]
    public void ItemType_CanBeSetToFolder()
    {
        var item = new ProjectItem { ItemType = ProjectItemType.Folder };

        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Folder));
    }

    [Test]
    public void ItemType_CanBeSetToReference()
    {
        var item = new ProjectItem { ItemType = ProjectItemType.Reference };

        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Reference));
    }
}

[TestFixture]
public class ProjectItemTypeTests
{
    [Test]
    public void None_HasValue0()
    {
        Assert.That((int)ProjectItemType.None, Is.EqualTo(0));
    }

    [Test]
    public void Compile_HasValue1()
    {
        Assert.That((int)ProjectItemType.Compile, Is.EqualTo(1));
    }

    [Test]
    public void Content_HasValue2()
    {
        Assert.That((int)ProjectItemType.Content, Is.EqualTo(2));
    }

    [Test]
    public void Resource_HasValue3()
    {
        Assert.That((int)ProjectItemType.Resource, Is.EqualTo(3));
    }

    [Test]
    public void Folder_HasValue4()
    {
        Assert.That((int)ProjectItemType.Folder, Is.EqualTo(4));
    }

    [Test]
    public void Reference_HasValue5()
    {
        Assert.That((int)ProjectItemType.Reference, Is.EqualTo(5));
    }

    [Test]
    public void HasSixValues()
    {
        var values = Enum.GetValues<ProjectItemType>();
        Assert.That(values, Has.Length.EqualTo(6));
    }
}

[TestFixture]
public class ProjectReferenceTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var reference = new ProjectReference();

        Assert.That(reference.Name, Is.EqualTo(""));
        Assert.That(reference.Path, Is.Null);
        Assert.That(reference.Version, Is.Null);
        Assert.That(reference.IsProjectReference, Is.False);
    }

    [Test]
    public void Name_CanBeSetAndRetrieved()
    {
        var reference = new ProjectReference { Name = "System.Core" };

        Assert.That(reference.Name, Is.EqualTo("System.Core"));
    }

    [Test]
    public void Path_CanBeSetAndRetrieved()
    {
        var reference = new ProjectReference { Path = @"..\SharedLib\SharedLib.blproj" };

        Assert.That(reference.Path, Is.EqualTo(@"..\SharedLib\SharedLib.blproj"));
    }

    [Test]
    public void Version_CanBeSetAndRetrieved()
    {
        var reference = new ProjectReference { Version = "4.0.0.0" };

        Assert.That(reference.Version, Is.EqualTo("4.0.0.0"));
    }

    [Test]
    public void IsProjectReference_CanBeSetToTrue()
    {
        var reference = new ProjectReference { IsProjectReference = true };

        Assert.That(reference.IsProjectReference, Is.True);
    }

    [Test]
    public void AllProperties_CanBeSetTogether()
    {
        var reference = new ProjectReference
        {
            Name = "MyLibrary",
            Path = @"..\MyLibrary\MyLibrary.blproj",
            Version = "1.0.0",
            IsProjectReference = true
        };

        Assert.That(reference.Name, Is.EqualTo("MyLibrary"));
        Assert.That(reference.Path, Is.EqualTo(@"..\MyLibrary\MyLibrary.blproj"));
        Assert.That(reference.Version, Is.EqualTo("1.0.0"));
        Assert.That(reference.IsProjectReference, Is.True);
    }
}
