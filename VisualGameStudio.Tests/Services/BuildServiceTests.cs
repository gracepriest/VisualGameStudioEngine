using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class BuildServiceTests
{
    private Mock<IOutputService> _mockOutputService = null!;
    private BuildService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _mockOutputService = new Mock<IOutputService>();
        _service = new BuildService(_mockOutputService.Object);
    }

    [Test]
    public void InitialState_IsNotBuilding()
    {
        Assert.That(_service.IsBuilding, Is.False);
    }

    [Test]
    public void CurrentConfiguration_DefaultIsDebug()
    {
        Assert.That(_service.CurrentConfiguration.Name, Is.EqualTo("Debug"));
    }

    [Test]
    public void CurrentConfiguration_CanBeChanged()
    {
        _service.CurrentConfiguration = new BuildConfiguration { Name = "Release" };

        Assert.That(_service.CurrentConfiguration.Name, Is.EqualTo("Release"));
    }

    [Test]
    public async Task CancelBuildAsync_WhenNotBuilding_DoesNotThrow()
    {
        await _service.CancelBuildAsync();

        Assert.Pass();
    }

    [Test]
    public void BuildStarted_Event_CanBeSubscribed()
    {
        var buildStarted = false;
        _service.BuildStarted += (s, e) => buildStarted = true;

        Assert.Pass();
    }

    [Test]
    public void BuildProgress_Event_CanBeSubscribed()
    {
        var progressReceived = false;
        _service.BuildProgress += (s, e) => progressReceived = true;

        Assert.Pass();
    }

    [Test]
    public void BuildCompleted_Event_CanBeSubscribed()
    {
        var buildCompleted = false;
        _service.BuildCompleted += (s, e) => buildCompleted = true;

        Assert.Pass();
    }

    [Test]
    public void BuildCancelled_Event_CanBeSubscribed()
    {
        var buildCancelled = false;
        _service.BuildCancelled += (s, e) => buildCancelled = true;

        Assert.Pass();
    }
}

[TestFixture]
public class BuildResultTests
{
    [Test]
    public void DefaultResult_HasDefaultValues()
    {
        var result = new BuildResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.OutputPath, Is.Null);
        Assert.That(result.ExecutablePath, Is.Null);
        Assert.That(result.GeneratedCode, Is.Null);
        Assert.That(result.GeneratedFileName, Is.Null);
        Assert.That(result.Diagnostics, Is.Empty);
        Assert.That(result.Duration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void Result_CanSetAllProperties()
    {
        var result = new BuildResult
        {
            Success = true,
            ExitCode = 0,
            OutputPath = @"C:\Projects\bin\Debug",
            ExecutablePath = @"C:\Projects\bin\Debug\MyApp.exe",
            GeneratedCode = "using System;",
            GeneratedFileName = "Program.cs",
            Duration = TimeSpan.FromSeconds(5.5)
        };

        Assert.That(result.Success, Is.True);
        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.OutputPath, Is.EqualTo(@"C:\Projects\bin\Debug"));
        Assert.That(result.ExecutablePath, Is.EqualTo(@"C:\Projects\bin\Debug\MyApp.exe"));
        Assert.That(result.GeneratedCode, Is.EqualTo("using System;"));
        Assert.That(result.GeneratedFileName, Is.EqualTo("Program.cs"));
        Assert.That(result.Duration, Is.EqualTo(TimeSpan.FromSeconds(5.5)));
    }

    [Test]
    public void ErrorCount_WithNoErrors_ReturnsZero()
    {
        var result = new BuildResult();

        Assert.That(result.ErrorCount, Is.EqualTo(0));
    }

    [Test]
    public void ErrorCount_WithErrors_ReturnsCorrectCount()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });

        Assert.That(result.ErrorCount, Is.EqualTo(2));
    }

    [Test]
    public void WarningCount_WithNoWarnings_ReturnsZero()
    {
        var result = new BuildResult();

        Assert.That(result.WarningCount, Is.EqualTo(0));
    }

    [Test]
    public void WarningCount_WithWarnings_ReturnsCorrectCount()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Severity = DiagnosticSeverity.Error });

        Assert.That(result.WarningCount, Is.EqualTo(2));
    }

    [Test]
    public void Errors_ReturnsOnlyErrors()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Message = "Error 1", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Message = "Warning 1", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Message = "Error 2", Severity = DiagnosticSeverity.Error });

        var errors = result.Errors.ToList();

        Assert.That(errors, Has.Count.EqualTo(2));
        Assert.That(errors.All(e => e.Severity == DiagnosticSeverity.Error), Is.True);
    }

    [Test]
    public void Warnings_ReturnsOnlyWarnings()
    {
        var result = new BuildResult();
        result.Diagnostics.Add(new DiagnosticItem { Message = "Error 1", Severity = DiagnosticSeverity.Error });
        result.Diagnostics.Add(new DiagnosticItem { Message = "Warning 1", Severity = DiagnosticSeverity.Warning });
        result.Diagnostics.Add(new DiagnosticItem { Message = "Warning 2", Severity = DiagnosticSeverity.Warning });

        var warnings = result.Warnings.ToList();

        Assert.That(warnings, Has.Count.EqualTo(2));
        Assert.That(warnings.All(w => w.Severity == DiagnosticSeverity.Warning), Is.True);
    }
}

[TestFixture]
public class BuildConfigurationTests
{
    [Test]
    public void DefaultConfiguration_HasDefaultValues()
    {
        var config = new BuildConfiguration();

        Assert.That(config.Name, Is.EqualTo("Debug"));
        Assert.That(config.OutputPath, Is.EqualTo(@"bin\Debug"));
        Assert.That(config.DebugSymbols, Is.True);
        Assert.That(config.Optimize, Is.False);
    }

    [Test]
    public void Configuration_CanSetAllProperties()
    {
        var config = new BuildConfiguration
        {
            Name = "Release",
            OutputPath = @"bin\Release",
            DebugSymbols = false,
            Optimize = true,
            DefineConstants = "RELEASE;TRACE",
            WarningLevel = WarningLevel.All,
            TreatWarningsAsErrors = true
        };

        Assert.That(config.Name, Is.EqualTo("Release"));
        Assert.That(config.OutputPath, Is.EqualTo(@"bin\Release"));
        Assert.That(config.DebugSymbols, Is.False);
        Assert.That(config.Optimize, Is.True);
        Assert.That(config.DefineConstants, Is.EqualTo("RELEASE;TRACE"));
        Assert.That(config.WarningLevel, Is.EqualTo(WarningLevel.All));
        Assert.That(config.TreatWarningsAsErrors, Is.True);
    }

    [Test]
    public void AdditionalProperties_CanBeAdded()
    {
        var config = new BuildConfiguration();
        config.AdditionalProperties["CustomKey"] = "CustomValue";

        Assert.That(config.AdditionalProperties["CustomKey"], Is.EqualTo("CustomValue"));
    }
}

[TestFixture]
public class WarningLevelTests
{
    [TestCase(WarningLevel.None, 0)]
    [TestCase(WarningLevel.Low, 1)]
    [TestCase(WarningLevel.Default, 2)]
    [TestCase(WarningLevel.High, 3)]
    [TestCase(WarningLevel.All, 4)]
    public void WarningLevel_HasCorrectValue(WarningLevel level, int expectedValue)
    {
        Assert.That((int)level, Is.EqualTo(expectedValue));
    }

    [Test]
    public void WarningLevel_HasFiveValues()
    {
        var values = Enum.GetValues<WarningLevel>();
        Assert.That(values, Has.Length.EqualTo(5));
    }
}

[TestFixture]
public class DiagnosticItemTests
{
    [Test]
    public void DefaultItem_HasDefaultValues()
    {
        var item = new DiagnosticItem();

        Assert.That(item.Id, Is.EqualTo(""));
        Assert.That(item.Message, Is.EqualTo(""));
        Assert.That(item.FilePath, Is.Null);
        Assert.That(item.Line, Is.EqualTo(0));
        Assert.That(item.Column, Is.EqualTo(0));
        Assert.That(item.EndLine, Is.EqualTo(0));
        Assert.That(item.EndColumn, Is.EqualTo(0));
        Assert.That(item.Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public void Item_CanSetAllProperties()
    {
        var item = new DiagnosticItem
        {
            Id = "BL0001",
            Message = "Undefined variable 'x'",
            FilePath = @"C:\Projects\main.bas",
            Line = 10,
            Column = 5,
            EndLine = 10,
            EndColumn = 6,
            Severity = DiagnosticSeverity.Error
        };

        Assert.That(item.Id, Is.EqualTo("BL0001"));
        Assert.That(item.Message, Is.EqualTo("Undefined variable 'x'"));
        Assert.That(item.FilePath, Is.EqualTo(@"C:\Projects\main.bas"));
        Assert.That(item.Line, Is.EqualTo(10));
        Assert.That(item.Column, Is.EqualTo(5));
        Assert.That(item.EndLine, Is.EqualTo(10));
        Assert.That(item.EndColumn, Is.EqualTo(6));
        Assert.That(item.Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [TestCase(DiagnosticSeverity.Error, 0)]
    [TestCase(DiagnosticSeverity.Warning, 1)]
    [TestCase(DiagnosticSeverity.Info, 2)]
    [TestCase(DiagnosticSeverity.Hidden, 3)]
    public void DiagnosticSeverity_HasCorrectValue(DiagnosticSeverity severity, int expectedValue)
    {
        Assert.That((int)severity, Is.EqualTo(expectedValue));
    }
}

[TestFixture]
public class BasicLangProjectTests
{
    [Test]
    public void DefaultProject_HasDefaultValues()
    {
        var project = new BasicLangProject();

        Assert.That(project.Name, Is.EqualTo(""));
        Assert.That(project.FilePath, Is.EqualTo(""));
        Assert.That(project.Items, Is.Empty);
        Assert.That(project.Configurations, Is.Empty);
    }

    [Test]
    public void Project_CanSetAllProperties()
    {
        var project = new BasicLangProject
        {
            Name = "MyProject",
            FilePath = @"C:\Projects\MyProject\MyProject.bsproj"
        };

        Assert.That(project.Name, Is.EqualTo("MyProject"));
        Assert.That(project.FilePath, Is.EqualTo(@"C:\Projects\MyProject\MyProject.bsproj"));
    }

    [Test]
    public void Project_CanAddItems()
    {
        var project = new BasicLangProject();
        project.Items.Add(new ProjectItem { Include = "main.bas", ItemType = ProjectItemType.Compile });

        Assert.That(project.Items, Has.Count.EqualTo(1));
        Assert.That(project.Items[0].Include, Is.EqualTo("main.bas"));
    }

    [Test]
    public void Project_CanAddConfigurations()
    {
        var project = new BasicLangProject();
        project.Configurations["Debug"] = new BuildConfiguration { Name = "Debug" };
        project.Configurations["Release"] = new BuildConfiguration { Name = "Release" };

        Assert.That(project.Configurations, Has.Count.EqualTo(2));
    }

    [Test]
    public void Project_GetConfiguration_ReturnsExisting()
    {
        var project = new BasicLangProject();
        var debugConfig = new BuildConfiguration { Name = "Debug" };
        project.Configurations["Debug"] = debugConfig;

        var result = project.GetConfiguration("Debug");

        Assert.That(result, Is.SameAs(debugConfig));
    }

    [Test]
    public void Project_GetConfiguration_ReturnsDefaultIfNotFound()
    {
        var project = new BasicLangProject();

        var result = project.GetConfiguration("NonExistent");

        Assert.That(result.Name, Is.EqualTo("Debug"));
    }
}

[TestFixture]
public class BasicLangSolutionTests
{
    [Test]
    public void DefaultSolution_HasDefaultValues()
    {
        var solution = new BasicLangSolution();

        Assert.That(solution.Name, Is.EqualTo(""));
        Assert.That(solution.FilePath, Is.EqualTo(""));
        Assert.That(solution.Projects, Is.Empty);
    }

    [Test]
    public void Solution_CanSetAllProperties()
    {
        var solution = new BasicLangSolution
        {
            Name = "MySolution",
            FilePath = @"C:\Projects\MySolution\MySolution.bsln"
        };

        Assert.That(solution.Name, Is.EqualTo("MySolution"));
        Assert.That(solution.FilePath, Is.EqualTo(@"C:\Projects\MySolution\MySolution.bsln"));
    }

    [Test]
    public void Solution_CanAddProjects()
    {
        var solution = new BasicLangSolution();
        solution.Projects.Add(new SolutionProject { Name = "Project1" });
        solution.Projects.Add(new SolutionProject { Name = "Project2" });

        Assert.That(solution.Projects, Has.Count.EqualTo(2));
    }

    [Test]
    public void Solution_GetStartupProject_ReturnsStartupProject()
    {
        var solution = new BasicLangSolution();
        solution.Projects.Add(new SolutionProject { Name = "Project1" });
        solution.Projects.Add(new SolutionProject { Name = "Project2", IsStartupProject = true });

        var startupProject = solution.GetStartupProject();

        Assert.That(startupProject!.Name, Is.EqualTo("Project2"));
    }

    [Test]
    public void Solution_GetStartupProject_ReturnsFirstIfNoStartup()
    {
        var solution = new BasicLangSolution();
        solution.Projects.Add(new SolutionProject { Name = "Project1" });
        solution.Projects.Add(new SolutionProject { Name = "Project2" });

        var startupProject = solution.GetStartupProject();

        Assert.That(startupProject!.Name, Is.EqualTo("Project1"));
    }

    [Test]
    public void Solution_GetStartupProject_ReturnsNullIfNoProjects()
    {
        var solution = new BasicLangSolution();

        var startupProject = solution.GetStartupProject();

        Assert.That(startupProject, Is.Null);
    }
}

[TestFixture]
public class SolutionProjectTests
{
    [Test]
    public void DefaultProject_HasDefaultValues()
    {
        var project = new SolutionProject();

        Assert.That(project.Name, Is.EqualTo(""));
        Assert.That(project.RelativePath, Is.EqualTo(""));
        Assert.That(project.IsStartupProject, Is.False);
        Assert.That(project.Id, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public void Project_CanSetAllProperties()
    {
        var id = Guid.NewGuid();
        var project = new SolutionProject
        {
            Id = id,
            Name = "MyProject",
            RelativePath = @"MyProject\MyProject.bsproj",
            IsStartupProject = true
        };

        Assert.That(project.Id, Is.EqualTo(id));
        Assert.That(project.Name, Is.EqualTo("MyProject"));
        Assert.That(project.RelativePath, Is.EqualTo(@"MyProject\MyProject.bsproj"));
        Assert.That(project.IsStartupProject, Is.True);
    }

    [Test]
    public void GetFullPath_CombinesPaths()
    {
        var project = new SolutionProject { RelativePath = @"MyProject\MyProject.bsproj" };

        var fullPath = project.GetFullPath(@"C:\Projects\MySolution");

        Assert.That(fullPath, Does.Contain("MyProject"));
        Assert.That(fullPath, Does.EndWith(".bsproj"));
    }
}

[TestFixture]
public class ProjectReferenceTests
{
    [Test]
    public void DefaultReference_HasDefaultValues()
    {
        var reference = new ProjectReference();

        Assert.That(reference.Name, Is.EqualTo(""));
        Assert.That(reference.Path, Is.Null);
        Assert.That(reference.Version, Is.Null);
        Assert.That(reference.IsProjectReference, Is.False);
    }

    [Test]
    public void Reference_CanSetAllProperties()
    {
        var reference = new ProjectReference
        {
            Name = "MyLibrary",
            Path = @"C:\Projects\MyLibrary\MyLibrary.bsproj",
            Version = "1.0.0",
            IsProjectReference = true
        };

        Assert.That(reference.Name, Is.EqualTo("MyLibrary"));
        Assert.That(reference.Path, Is.EqualTo(@"C:\Projects\MyLibrary\MyLibrary.bsproj"));
        Assert.That(reference.Version, Is.EqualTo("1.0.0"));
        Assert.That(reference.IsProjectReference, Is.True);
    }
}

[TestFixture]
public class ProjectItemTests
{
    [Test]
    public void DefaultItem_HasDefaultValues()
    {
        var item = new ProjectItem();

        Assert.That(item.Include, Is.EqualTo(""));
        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.None));
    }

    [Test]
    public void Item_CanSetAllProperties()
    {
        var item = new ProjectItem
        {
            Include = @"Source\main.bas",
            ItemType = ProjectItemType.Compile
        };

        Assert.That(item.Include, Is.EqualTo(@"Source\main.bas"));
        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Compile));
    }

    [Test]
    public void Constructor_WithParameters_SetsProperties()
    {
        var item = new ProjectItem("main.bas", ProjectItemType.Compile);

        Assert.That(item.Include, Is.EqualTo("main.bas"));
        Assert.That(item.ItemType, Is.EqualTo(ProjectItemType.Compile));
    }

    [Test]
    public void FileName_ReturnsFileNameOnly()
    {
        var item = new ProjectItem { Include = @"Source\main.bas" };

        Assert.That(item.FileName, Is.EqualTo("main.bas"));
    }

    [Test]
    public void Directory_ReturnsDirectoryOnly()
    {
        var item = new ProjectItem { Include = @"Source\Modules\main.bas" };

        Assert.That(item.Directory, Is.EqualTo(@"Source\Modules"));
    }

    [TestCase(ProjectItemType.None, 0)]
    [TestCase(ProjectItemType.Compile, 1)]
    [TestCase(ProjectItemType.Content, 2)]
    [TestCase(ProjectItemType.Resource, 3)]
    [TestCase(ProjectItemType.Folder, 4)]
    [TestCase(ProjectItemType.Reference, 5)]
    public void ProjectItemType_HasCorrectValue(ProjectItemType type, int expectedValue)
    {
        Assert.That((int)type, Is.EqualTo(expectedValue));
    }
}
