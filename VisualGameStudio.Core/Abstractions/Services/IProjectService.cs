using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides project and solution management for the IDE.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Gets the currently open project, or null if no project is open.
    /// </summary>
    BasicLangProject? CurrentProject { get; }

    /// <summary>
    /// Gets the currently open solution, or null if no solution is open.
    /// </summary>
    BasicLangSolution? CurrentSolution { get; }

    /// <summary>
    /// Gets whether the current project or solution has unsaved changes.
    /// </summary>
    bool HasUnsavedChanges { get; }

    /// <summary>
    /// Creates a new project with the specified name and template.
    /// </summary>
    /// <param name="name">The name of the project.</param>
    /// <param name="path">The directory path where the project will be created.</param>
    /// <param name="template">The project template to use.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created project.</returns>
    Task<BasicLangProject> CreateProjectAsync(string name, string path, ProjectTemplateKind template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an existing project from the specified path.
    /// </summary>
    /// <param name="path">The path to the project file (.blproj).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The opened project.</returns>
    Task<BasicLangProject> OpenProjectAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current project to disk.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveProjectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the current project.
    /// </summary>
    Task CloseProjectAsync();

    /// <summary>
    /// Creates a new solution with the specified name.
    /// </summary>
    /// <param name="name">The name of the solution.</param>
    /// <param name="path">The directory path where the solution will be created.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created solution.</returns>
    Task<BasicLangSolution> CreateSolutionAsync(string name, string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens an existing solution from the specified path.
    /// </summary>
    /// <param name="path">The path to the solution file (.blsln).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The opened solution.</returns>
    Task<BasicLangSolution> OpenSolutionAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves the current solution to disk.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task SaveSolutionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the current solution.
    /// </summary>
    Task CloseSolutionAsync();

    /// <summary>
    /// Adds an existing file to the current project.
    /// </summary>
    /// <param name="filePath">The path to the file to add.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task AddFileToProjectAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a file from the current project.
    /// </summary>
    /// <param name="filePath">The path to the file to remove.</param>
    Task RemoveFileFromProjectAsync(string filePath);

    /// <summary>
    /// Creates a new file from a template and adds it to the project.
    /// </summary>
    /// <param name="fileName">The name for the new file.</param>
    /// <param name="template">The template to use for the file content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The created project item.</returns>
    Task<ProjectItem> AddNewFileAsync(string fileName, string template, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when a project is opened.
    /// </summary>
    event EventHandler<ProjectEventArgs>? ProjectOpened;

    /// <summary>
    /// Raised when a project is closed.
    /// </summary>
    event EventHandler<ProjectEventArgs>? ProjectClosed;

    /// <summary>
    /// Raised when a solution is opened.
    /// </summary>
    event EventHandler<SolutionEventArgs>? SolutionOpened;

    /// <summary>
    /// Raised when a solution is closed.
    /// </summary>
    event EventHandler<SolutionEventArgs>? SolutionClosed;

    /// <summary>
    /// Raised when the project structure changes.
    /// </summary>
    event EventHandler? ProjectChanged;
}

/// <summary>
/// Event arguments for project-related events.
/// </summary>
public class ProjectEventArgs : EventArgs
{
    /// <summary>
    /// Gets the project associated with the event.
    /// </summary>
    public BasicLangProject Project { get; }

    /// <summary>
    /// Creates a new ProjectEventArgs instance.
    /// </summary>
    /// <param name="project">The project associated with the event.</param>
    public ProjectEventArgs(BasicLangProject project) => Project = project;
}

/// <summary>
/// Event arguments for solution-related events.
/// </summary>
public class SolutionEventArgs : EventArgs
{
    /// <summary>
    /// Gets the solution associated with the event.
    /// </summary>
    public BasicLangSolution Solution { get; }

    /// <summary>
    /// Creates a new SolutionEventArgs instance.
    /// </summary>
    /// <param name="solution">The solution associated with the event.</param>
    public SolutionEventArgs(BasicLangSolution solution) => Solution = solution;
}

/// <summary>
/// Project template kinds for creating new projects.
/// </summary>
public enum ProjectTemplateKind
{
    /// <summary>A console application that runs in a terminal window.</summary>
    ConsoleApplication,
    /// <summary>A Windows Forms application with a graphical user interface.</summary>
    WindowsFormsApplication,
    /// <summary>A game application using the BasicLang game engine.</summary>
    GameApplication,
    /// <summary>A class library that can be referenced by other projects.</summary>
    ClassLibrary
}
