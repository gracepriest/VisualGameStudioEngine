using NUnit.Framework;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class BasicLangProjectTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var project = new BasicLangProject();

        Assert.That(project.Name, Is.EqualTo(""));
        Assert.That(project.FilePath, Is.EqualTo(""));
        Assert.That(project.OutputType, Is.EqualTo(OutputType.Exe));
        Assert.That(project.RootNamespace, Is.EqualTo(""));
        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.CSharp));
        Assert.That(project.Version, Is.EqualTo("1.0"));
        Assert.That(project.Items, Is.Empty);
        Assert.That(project.References, Is.Empty);
        Assert.That(project.Configurations, Is.Empty);
    }

    [Test]
    public void ProjectDirectory_ExtractsDirectoryFromFilePath()
    {
        var project = new BasicLangProject
        {
            FilePath = @"C:\Projects\MyProject\MyProject.blproj"
        };

        Assert.That(project.ProjectDirectory, Is.EqualTo(@"C:\Projects\MyProject"));
    }

    [Test]
    public void ProjectDirectory_WithNoPath_ReturnsEmpty()
    {
        var project = new BasicLangProject { FilePath = "" };

        Assert.That(project.ProjectDirectory, Is.EqualTo(""));
    }

    [Test]
    public void GetConfiguration_ExistingConfig_ReturnsConfig()
    {
        var project = new BasicLangProject();
        var debugConfig = new BuildConfiguration { Name = "Debug" };
        var releaseConfig = new BuildConfiguration { Name = "Release" };
        project.Configurations["Debug"] = debugConfig;
        project.Configurations["Release"] = releaseConfig;

        var result = project.GetConfiguration("Debug");

        Assert.That(result, Is.SameAs(debugConfig));
    }

    [Test]
    public void GetConfiguration_NonExistingConfig_ReturnsFirstConfig()
    {
        var project = new BasicLangProject();
        var debugConfig = new BuildConfiguration { Name = "Debug" };
        project.Configurations["Debug"] = debugConfig;

        var result = project.GetConfiguration("NonExistent");

        Assert.That(result, Is.SameAs(debugConfig));
    }

    [Test]
    public void GetConfiguration_NoConfigs_ReturnsDefaultDebugConfig()
    {
        var project = new BasicLangProject();

        var result = project.GetConfiguration("Debug");

        Assert.That(result.Name, Is.EqualTo("Debug"));
    }

    [Test]
    public void GetSourceFiles_ReturnsOnlyCompileItems()
    {
        var project = new BasicLangProject();
        project.Items.Add(new ProjectItem("Main.bas", ProjectItemType.Compile));
        project.Items.Add(new ProjectItem("Helper.bas", ProjectItemType.Compile));
        project.Items.Add(new ProjectItem("Config.json", ProjectItemType.Content));
        project.Items.Add(new ProjectItem("Resources", ProjectItemType.Folder));

        var sourceFiles = project.GetSourceFiles().ToList();

        Assert.That(sourceFiles, Has.Count.EqualTo(2));
        Assert.That(sourceFiles.All(i => i.ItemType == ProjectItemType.Compile), Is.True);
    }

    [Test]
    public void GetSourceFiles_NoCompileItems_ReturnsEmpty()
    {
        var project = new BasicLangProject();
        project.Items.Add(new ProjectItem("Config.json", ProjectItemType.Content));
        project.Items.Add(new ProjectItem("Resources", ProjectItemType.Folder));

        var sourceFiles = project.GetSourceFiles().ToList();

        Assert.That(sourceFiles, Is.Empty);
    }

    [Test]
    public void GetMainFile_FindsProgramBas()
    {
        var project = new BasicLangProject
        {
            FilePath = @"C:\Projects\Test\Test.blproj"
        };
        project.Items.Add(new ProjectItem("Program.bas", ProjectItemType.Compile));
        project.Items.Add(new ProjectItem("Helper.bas", ProjectItemType.Compile));

        var mainFile = project.GetMainFile();

        Assert.That(mainFile, Is.EqualTo(@"C:\Projects\Test\Program.bas"));
    }

    [Test]
    public void GetMainFile_FindsMainBas()
    {
        var project = new BasicLangProject
        {
            FilePath = @"C:\Projects\Test\Test.blproj"
        };
        project.Items.Add(new ProjectItem("Main.bas", ProjectItemType.Compile));
        project.Items.Add(new ProjectItem("Helper.bas", ProjectItemType.Compile));

        var mainFile = project.GetMainFile();

        Assert.That(mainFile, Is.EqualTo(@"C:\Projects\Test\Main.bas"));
    }

    [Test]
    public void GetMainFile_CaseInsensitive()
    {
        var project = new BasicLangProject
        {
            FilePath = @"C:\Projects\Test\Test.blproj"
        };
        project.Items.Add(new ProjectItem("PROGRAM.BAS", ProjectItemType.Compile));

        var mainFile = project.GetMainFile();

        Assert.That(mainFile, Is.Not.Null);
    }

    [Test]
    public void GetMainFile_NoMainFile_ReturnsNull()
    {
        var project = new BasicLangProject
        {
            FilePath = @"C:\Projects\Test\Test.blproj"
        };
        project.Items.Add(new ProjectItem("Helper.bas", ProjectItemType.Compile));
        project.Items.Add(new ProjectItem("Utils.bas", ProjectItemType.Compile));

        var mainFile = project.GetMainFile();

        Assert.That(mainFile, Is.Null);
    }

    [Test]
    public void GetMainFile_ProgramBasNotCompileType_ReturnsNull()
    {
        var project = new BasicLangProject
        {
            FilePath = @"C:\Projects\Test\Test.blproj"
        };
        project.Items.Add(new ProjectItem("Program.bas", ProjectItemType.Content));

        var mainFile = project.GetMainFile();

        Assert.That(mainFile, Is.Null);
    }

    [Test]
    public void OutputType_CanBeSetToLibrary()
    {
        var project = new BasicLangProject { OutputType = OutputType.Library };

        Assert.That(project.OutputType, Is.EqualTo(OutputType.Library));
    }

    [Test]
    public void OutputType_CanBeSetToWinExe()
    {
        var project = new BasicLangProject { OutputType = OutputType.WinExe };

        Assert.That(project.OutputType, Is.EqualTo(OutputType.WinExe));
    }

    [Test]
    public void TargetBackend_CanBeSetToCpp()
    {
        var project = new BasicLangProject { TargetBackend = TargetBackend.Cpp };

        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.Cpp));
    }

    [Test]
    public void TargetBackend_CanBeSetToLLVM()
    {
        var project = new BasicLangProject { TargetBackend = TargetBackend.LLVM };

        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.LLVM));
    }

    [Test]
    public void TargetBackend_CanBeSetToMSIL()
    {
        var project = new BasicLangProject { TargetBackend = TargetBackend.MSIL };

        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.MSIL));
    }
}

[TestFixture]
public class OutputTypeTests
{
    [Test]
    public void Exe_HasValue0()
    {
        Assert.That((int)OutputType.Exe, Is.EqualTo(0));
    }

    [Test]
    public void Library_HasValue1()
    {
        Assert.That((int)OutputType.Library, Is.EqualTo(1));
    }

    [Test]
    public void WinExe_HasValue2()
    {
        Assert.That((int)OutputType.WinExe, Is.EqualTo(2));
    }
}

[TestFixture]
public class TargetBackendTests
{
    [Test]
    public void CSharp_HasValue0()
    {
        Assert.That((int)TargetBackend.CSharp, Is.EqualTo(0));
    }

    [Test]
    public void Cpp_HasValue1()
    {
        Assert.That((int)TargetBackend.Cpp, Is.EqualTo(1));
    }

    [Test]
    public void LLVM_HasValue2()
    {
        Assert.That((int)TargetBackend.LLVM, Is.EqualTo(2));
    }

    [Test]
    public void MSIL_HasValue3()
    {
        Assert.That((int)TargetBackend.MSIL, Is.EqualTo(3));
    }
}
