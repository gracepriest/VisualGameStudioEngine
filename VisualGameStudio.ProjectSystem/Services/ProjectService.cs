using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Constants;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.ProjectSystem.Services;

public class ProjectService : IProjectService
{
    private readonly IFileService _fileService;
    private readonly ProjectSerializer _projectSerializer;
    private readonly SolutionSerializer _solutionSerializer;

    public BasicLangProject? CurrentProject { get; private set; }
    public BasicLangSolution? CurrentSolution { get; private set; }
    public bool HasUnsavedChanges { get; private set; }

    public event EventHandler<ProjectEventArgs>? ProjectOpened;
    public event EventHandler<ProjectEventArgs>? ProjectClosed;
    public event EventHandler<SolutionEventArgs>? SolutionOpened;
    public event EventHandler<SolutionEventArgs>? SolutionClosed;
    public event EventHandler? ProjectChanged;

    public ProjectService(IFileService fileService)
    {
        _fileService = fileService;
        _projectSerializer = new ProjectSerializer();
        _solutionSerializer = new SolutionSerializer();
    }

    public async Task<BasicLangProject> CreateProjectAsync(string name, string path, ProjectTemplate template, CancellationToken cancellationToken = default)
    {
        var projectDir = Path.Combine(path, name);
        var projectFile = Path.Combine(projectDir, $"{name}{FileExtensions.Project}");

        await _fileService.CreateDirectoryAsync(projectDir);

        var project = new BasicLangProject
        {
            Name = name,
            FilePath = projectFile,
            RootNamespace = name,
            OutputType = template == ProjectTemplate.ClassLibrary ? OutputType.Library : OutputType.Exe,
            TargetBackend = TargetBackend.CSharp,
            Version = "1.0"
        };

        // Add default configurations
        project.Configurations["Debug"] = new BuildConfiguration
        {
            Name = "Debug",
            OutputPath = "bin\\Debug",
            DebugSymbols = true,
            Optimize = false
        };
        project.Configurations["Release"] = new BuildConfiguration
        {
            Name = "Release",
            OutputPath = "bin\\Release",
            DebugSymbols = false,
            Optimize = true
        };

        // Create main source file based on template
        var mainFileName = "Program.bas";
        var mainFilePath = Path.Combine(projectDir, mainFileName);
        var mainFileContent = GetTemplateContent(template, name);

        await _fileService.WriteFileAsync(mainFilePath, mainFileContent, cancellationToken);
        project.Items.Add(new ProjectItem(mainFileName, ProjectItemType.Compile));

        // Save project file
        await _projectSerializer.SaveAsync(project, cancellationToken);

        CurrentProject = project;
        HasUnsavedChanges = false;
        ProjectOpened?.Invoke(this, new ProjectEventArgs(project));

        return project;
    }

    public async Task<BasicLangProject> OpenProjectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (CurrentProject != null)
        {
            await CloseProjectAsync();
        }

        var project = await _projectSerializer.LoadAsync(path, cancellationToken);
        CurrentProject = project;
        HasUnsavedChanges = false;
        ProjectOpened?.Invoke(this, new ProjectEventArgs(project));

        return project;
    }

    public async Task SaveProjectAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentProject == null) return;
        await _projectSerializer.SaveAsync(CurrentProject, cancellationToken);
        HasUnsavedChanges = false;
    }

    public Task CloseProjectAsync()
    {
        if (CurrentProject != null)
        {
            var project = CurrentProject;
            CurrentProject = null;
            HasUnsavedChanges = false;
            ProjectClosed?.Invoke(this, new ProjectEventArgs(project));
        }
        return Task.CompletedTask;
    }

    public async Task<BasicLangSolution> CreateSolutionAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        var solutionFile = Path.Combine(path, $"{name}{FileExtensions.Solution}");

        var solution = new BasicLangSolution
        {
            Name = name,
            FilePath = solutionFile,
            Version = "1.0"
        };

        await _solutionSerializer.SaveAsync(solution, cancellationToken);

        CurrentSolution = solution;
        SolutionOpened?.Invoke(this, new SolutionEventArgs(solution));

        return solution;
    }

    public async Task<BasicLangSolution> OpenSolutionAsync(string path, CancellationToken cancellationToken = default)
    {
        if (CurrentSolution != null)
        {
            await CloseSolutionAsync();
        }

        var solution = await _solutionSerializer.LoadAsync(path, cancellationToken);
        CurrentSolution = solution;
        SolutionOpened?.Invoke(this, new SolutionEventArgs(solution));

        // Open the startup project if available
        var startupProject = solution.GetStartupProject();
        if (startupProject != null)
        {
            var projectPath = startupProject.GetFullPath(solution.SolutionDirectory);
            if (await _fileService.FileExistsAsync(projectPath))
            {
                await OpenProjectAsync(projectPath, cancellationToken);
            }
        }

        return solution;
    }

    public async Task SaveSolutionAsync(CancellationToken cancellationToken = default)
    {
        if (CurrentSolution == null) return;
        await _solutionSerializer.SaveAsync(CurrentSolution, cancellationToken);
    }

    public Task CloseSolutionAsync()
    {
        if (CurrentSolution != null)
        {
            var solution = CurrentSolution;
            CurrentSolution = null;
            SolutionClosed?.Invoke(this, new SolutionEventArgs(solution));
        }
        return CloseProjectAsync();
    }

    public async Task AddFileToProjectAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (CurrentProject == null) return;

        var relativePath = GetRelativePath(CurrentProject.ProjectDirectory, filePath);
        var itemType = FileExtensions.IsSourceFile(filePath) ? ProjectItemType.Compile : ProjectItemType.Content;

        if (!CurrentProject.Items.Any(i => i.Include.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
        {
            CurrentProject.Items.Add(new ProjectItem(relativePath, itemType));
            HasUnsavedChanges = true;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Task RemoveFileFromProjectAsync(string filePath)
    {
        if (CurrentProject == null) return Task.CompletedTask;

        var relativePath = GetRelativePath(CurrentProject.ProjectDirectory, filePath);
        var item = CurrentProject.Items.FirstOrDefault(i =>
            i.Include.Equals(relativePath, StringComparison.OrdinalIgnoreCase));

        if (item != null)
        {
            CurrentProject.Items.Remove(item);
            HasUnsavedChanges = true;
            ProjectChanged?.Invoke(this, EventArgs.Empty);
        }

        return Task.CompletedTask;
    }

    public async Task<ProjectItem> AddNewFileAsync(string fileName, string template, CancellationToken cancellationToken = default)
    {
        if (CurrentProject == null)
            throw new InvalidOperationException("No project is open");

        var filePath = Path.Combine(CurrentProject.ProjectDirectory, fileName);
        await _fileService.WriteFileAsync(filePath, template, cancellationToken);

        var item = new ProjectItem(fileName, ProjectItemType.Compile);
        CurrentProject.Items.Add(item);
        HasUnsavedChanges = true;
        ProjectChanged?.Invoke(this, EventArgs.Empty);

        return item;
    }

    private static string GetRelativePath(string basePath, string fullPath)
    {
        var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? basePath : basePath + Path.DirectorySeparatorChar);
        var fullUri = new Uri(fullPath);
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString().Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetTemplateContent(ProjectTemplate template, string projectName)
    {
        return template switch
        {
            ProjectTemplate.ConsoleApplication => $@"' {projectName} - Console Application
' Created by Visual Game Studio

Module Program
    Sub Main()
        PrintLine(""Hello, BasicLang!"")
        PrintLine(""Welcome to {projectName}"")
    End Sub
End Module
",
            ProjectTemplate.WindowsFormsApplication => $@"' {projectName} - Windows Forms Application
' Created by Visual Game Studio

Module Program
    Sub Main()
        ' Initialize application
        PrintLine(""Starting {projectName}..."")
    End Sub
End Module
",
            ProjectTemplate.GameApplication => $@"' {projectName} - Game Application
' Created by Visual Game Studio

Module Program
    Sub Main()
        ' Initialize game
        PrintLine(""Starting {projectName} Game..."")

        ' Main game loop
        GameLoop()
    End Sub

    Sub GameLoop()
        ' Game update and render logic goes here
        PrintLine(""Game running..."")
    End Sub
End Module
",
            ProjectTemplate.ClassLibrary => $@"' {projectName} - Class Library
' Created by Visual Game Studio

Namespace {projectName}
    Public Class Library
        Public Shared Function GetMessage() As String
            Return ""Hello from {projectName}!""
        End Function
    End Class
End Namespace
",
            _ => $@"' {projectName}
' Created by Visual Game Studio

Module Program
    Sub Main()
        PrintLine(""Hello, World!"")
    End Sub
End Module
"
        };
    }
}
