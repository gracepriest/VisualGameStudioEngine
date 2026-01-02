using NUnit.Framework;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class BasicLangSolutionTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var solution = new BasicLangSolution();

        Assert.That(solution.Name, Is.EqualTo(""));
        Assert.That(solution.FilePath, Is.EqualTo(""));
        Assert.That(solution.Version, Is.EqualTo("1.0"));
        Assert.That(solution.Projects, Is.Empty);
        Assert.That(solution.Folders, Is.Empty);
        Assert.That(solution.GlobalProperties, Is.Empty);
    }

    [Test]
    public void SolutionDirectory_ExtractsDirectoryFromFilePath()
    {
        var solution = new BasicLangSolution
        {
            FilePath = @"C:\Projects\MySolution\MySolution.blsln"
        };

        Assert.That(solution.SolutionDirectory, Is.EqualTo(@"C:\Projects\MySolution"));
    }

    [Test]
    public void SolutionDirectory_WithNoPath_ReturnsEmpty()
    {
        var solution = new BasicLangSolution { FilePath = "" };

        Assert.That(solution.SolutionDirectory, Is.EqualTo(""));
    }

    [Test]
    public void GetStartupProject_WithMarkedStartup_ReturnsMarkedProject()
    {
        var solution = new BasicLangSolution();
        var project1 = new SolutionProject { Name = "Project1", IsStartupProject = false };
        var project2 = new SolutionProject { Name = "Project2", IsStartupProject = true };
        var project3 = new SolutionProject { Name = "Project3", IsStartupProject = false };
        solution.Projects.Add(project1);
        solution.Projects.Add(project2);
        solution.Projects.Add(project3);

        var startup = solution.GetStartupProject();

        Assert.That(startup, Is.SameAs(project2));
    }

    [Test]
    public void GetStartupProject_NoMarkedStartup_ReturnsFirstProject()
    {
        var solution = new BasicLangSolution();
        var project1 = new SolutionProject { Name = "Project1", IsStartupProject = false };
        var project2 = new SolutionProject { Name = "Project2", IsStartupProject = false };
        solution.Projects.Add(project1);
        solution.Projects.Add(project2);

        var startup = solution.GetStartupProject();

        Assert.That(startup, Is.SameAs(project1));
    }

    [Test]
    public void GetStartupProject_NoProjects_ReturnsNull()
    {
        var solution = new BasicLangSolution();

        var startup = solution.GetStartupProject();

        Assert.That(startup, Is.Null);
    }

    [Test]
    public void GlobalProperties_CanAddAndRetrieve()
    {
        var solution = new BasicLangSolution();
        solution.GlobalProperties["Key1"] = "Value1";
        solution.GlobalProperties["Key2"] = "Value2";

        Assert.That(solution.GlobalProperties["Key1"], Is.EqualTo("Value1"));
        Assert.That(solution.GlobalProperties["Key2"], Is.EqualTo("Value2"));
    }
}

[TestFixture]
public class SolutionProjectTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var project = new SolutionProject();

        Assert.That(project.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(project.Name, Is.EqualTo(""));
        Assert.That(project.RelativePath, Is.EqualTo(""));
        Assert.That(project.IsStartupProject, Is.False);
    }

    [Test]
    public void Id_IsUniqueForEachInstance()
    {
        var project1 = new SolutionProject();
        var project2 = new SolutionProject();

        Assert.That(project1.Id, Is.Not.EqualTo(project2.Id));
    }

    [Test]
    public void GetFullPath_CombinesPaths()
    {
        var project = new SolutionProject
        {
            RelativePath = @"ProjectA\ProjectA.blproj"
        };

        var fullPath = project.GetFullPath(@"C:\Solutions\MySolution");

        Assert.That(fullPath, Does.EndWith(@"ProjectA\ProjectA.blproj"));
        Assert.That(fullPath, Does.StartWith(@"C:\"));
    }

    [Test]
    public void GetFullPath_WithDotDot_ResolvesCorrectly()
    {
        var project = new SolutionProject
        {
            RelativePath = @"..\OtherProject\Project.blproj"
        };

        var fullPath = project.GetFullPath(@"C:\Solutions\MySolution");

        Assert.That(fullPath, Does.Contain("OtherProject"));
    }

    [Test]
    public void Name_CanBeSetAndRetrieved()
    {
        var project = new SolutionProject { Name = "MyProject" };

        Assert.That(project.Name, Is.EqualTo("MyProject"));
    }

    [Test]
    public void IsStartupProject_CanBeSetToTrue()
    {
        var project = new SolutionProject { IsStartupProject = true };

        Assert.That(project.IsStartupProject, Is.True);
    }
}

[TestFixture]
public class SolutionFolderTests
{
    [Test]
    public void DefaultValues_AreSetCorrectly()
    {
        var folder = new SolutionFolder();

        Assert.That(folder.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(folder.Name, Is.EqualTo(""));
        Assert.That(folder.ParentId, Is.Null);
        Assert.That(folder.ProjectIds, Is.Empty);
    }

    [Test]
    public void Id_IsUniqueForEachInstance()
    {
        var folder1 = new SolutionFolder();
        var folder2 = new SolutionFolder();

        Assert.That(folder1.Id, Is.Not.EqualTo(folder2.Id));
    }

    [Test]
    public void Name_CanBeSetAndRetrieved()
    {
        var folder = new SolutionFolder { Name = "Source" };

        Assert.That(folder.Name, Is.EqualTo("Source"));
    }

    [Test]
    public void ParentId_CanBeSetToGuid()
    {
        var parentId = Guid.NewGuid();
        var folder = new SolutionFolder { ParentId = parentId };

        Assert.That(folder.ParentId, Is.EqualTo(parentId));
    }

    [Test]
    public void ProjectIds_CanAddMultipleIds()
    {
        var folder = new SolutionFolder();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        folder.ProjectIds.Add(id1);
        folder.ProjectIds.Add(id2);

        Assert.That(folder.ProjectIds, Has.Count.EqualTo(2));
        Assert.That(folder.ProjectIds, Contains.Item(id1));
        Assert.That(folder.ProjectIds, Contains.Item(id2));
    }

    [Test]
    public void NestedFolderStructure_Works()
    {
        var parentFolder = new SolutionFolder { Name = "Parent" };
        var childFolder = new SolutionFolder
        {
            Name = "Child",
            ParentId = parentFolder.Id
        };

        Assert.That(childFolder.ParentId, Is.EqualTo(parentFolder.Id));
    }
}
