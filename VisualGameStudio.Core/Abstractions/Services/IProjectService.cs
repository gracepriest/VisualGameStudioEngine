using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

public interface IProjectService
{
    BasicLangProject? CurrentProject { get; }
    BasicLangSolution? CurrentSolution { get; }
    bool HasUnsavedChanges { get; }

    Task<BasicLangProject> CreateProjectAsync(string name, string path, ProjectTemplate template, CancellationToken cancellationToken = default);
    Task<BasicLangProject> OpenProjectAsync(string path, CancellationToken cancellationToken = default);
    Task SaveProjectAsync(CancellationToken cancellationToken = default);
    Task CloseProjectAsync();

    Task<BasicLangSolution> CreateSolutionAsync(string name, string path, CancellationToken cancellationToken = default);
    Task<BasicLangSolution> OpenSolutionAsync(string path, CancellationToken cancellationToken = default);
    Task SaveSolutionAsync(CancellationToken cancellationToken = default);
    Task CloseSolutionAsync();

    Task AddFileToProjectAsync(string filePath, CancellationToken cancellationToken = default);
    Task RemoveFileFromProjectAsync(string filePath);
    Task<ProjectItem> AddNewFileAsync(string fileName, string template, CancellationToken cancellationToken = default);

    event EventHandler<ProjectEventArgs>? ProjectOpened;
    event EventHandler<ProjectEventArgs>? ProjectClosed;
    event EventHandler<SolutionEventArgs>? SolutionOpened;
    event EventHandler<SolutionEventArgs>? SolutionClosed;
    event EventHandler? ProjectChanged;
}

public class ProjectEventArgs : EventArgs
{
    public BasicLangProject Project { get; }
    public ProjectEventArgs(BasicLangProject project) => Project = project;
}

public class SolutionEventArgs : EventArgs
{
    public BasicLangSolution Solution { get; }
    public SolutionEventArgs(BasicLangSolution solution) => Solution = solution;
}

public enum ProjectTemplate
{
    ConsoleApplication,
    WindowsFormsApplication,
    GameApplication,
    ClassLibrary
}
