using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides solution-level management for multi-project BasicLang solutions.
/// </summary>
public interface ISolutionService
{
    /// <summary>
    /// Gets the currently open solution, or null if no solution is open.
    /// </summary>
    BasicLangSolution? CurrentSolution { get; }

    /// <summary>
    /// Gets whether a solution is currently open.
    /// </summary>
    bool HasSolution { get; }

    /// <summary>
    /// Loads a solution from a .blsln file.
    /// </summary>
    /// <param name="filePath">The path to the .blsln file.</param>
    /// <returns>The loaded solution.</returns>
    Task<BasicLangSolution> LoadSolutionAsync(string filePath);

    /// <summary>
    /// Creates a new empty solution with the specified name.
    /// </summary>
    /// <param name="name">The name of the solution.</param>
    /// <param name="directory">The directory where the solution file will be created.</param>
    /// <returns>The created solution.</returns>
    Task<BasicLangSolution> CreateSolutionAsync(string name, string directory);

    /// <summary>
    /// Saves the current solution to disk.
    /// </summary>
    Task SaveSolutionAsync();

    /// <summary>
    /// Closes the current solution.
    /// </summary>
    Task CloseSolutionAsync();

    /// <summary>
    /// Creates a new project and adds it to the current solution.
    /// </summary>
    /// <param name="name">The project name.</param>
    /// <param name="type">The project type (Exe, Library, WinExe).</param>
    /// <param name="relativePath">Optional relative path from the solution directory. Defaults to name/name.blproj.</param>
    /// <returns>The created solution project entry.</returns>
    Task<SolutionProject> AddNewProjectAsync(string name, string type, string? relativePath = null);

    /// <summary>
    /// Adds an existing .blproj file to the current solution.
    /// </summary>
    /// <param name="blprojPath">The absolute path to the .blproj file.</param>
    Task AddExistingProjectAsync(string blprojPath);

    /// <summary>
    /// Removes a project from the current solution by name.
    /// </summary>
    /// <param name="projectName">The name of the project to remove.</param>
    void RemoveProject(string projectName);

    /// <summary>
    /// Adds a project reference from one project to another within the solution.
    /// </summary>
    /// <param name="fromProject">The name of the project that will reference the other.</param>
    /// <param name="toProject">The name of the project being referenced.</param>
    void AddProjectReference(string fromProject, string toProject);

    /// <summary>
    /// Removes a project reference between two projects.
    /// </summary>
    /// <param name="fromProject">The name of the project with the reference.</param>
    /// <param name="toProject">The name of the referenced project to remove.</param>
    void RemoveProjectReference(string fromProject, string toProject);

    /// <summary>
    /// Sets the startup project for the solution.
    /// </summary>
    /// <param name="projectName">The name of the project to set as startup.</param>
    void SetStartupProject(string projectName);

    /// <summary>
    /// Returns projects in build order using topological sort based on project references.
    /// </summary>
    /// <returns>Projects sorted in dependency order (dependencies first).</returns>
    List<SolutionProject> GetBuildOrder();

    /// <summary>
    /// Raised when the solution structure changes (projects added/removed, references changed).
    /// </summary>
    event EventHandler? SolutionChanged;

    /// <summary>
    /// Raised when a solution is loaded or created.
    /// </summary>
    event EventHandler? SolutionLoaded;

    /// <summary>
    /// Raised when the current solution is closed.
    /// </summary>
    event EventHandler? SolutionClosed;
}
