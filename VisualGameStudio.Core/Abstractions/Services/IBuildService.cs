using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides build and compilation services for the IDE.
/// </summary>
public interface IBuildService
{
    /// <summary>
    /// Gets whether a build is currently in progress.
    /// </summary>
    bool IsBuilding { get; }

    /// <summary>
    /// Gets or sets the current build configuration (Debug/Release).
    /// </summary>
    BuildConfiguration CurrentConfiguration { get; set; }

    /// <summary>
    /// Builds a project.
    /// </summary>
    /// <param name="project">The project to build.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the build operation.</returns>
    Task<BuildResult> BuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds all projects in a solution.
    /// </summary>
    /// <param name="solution">The solution to build.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the build operation.</returns>
    Task<BuildResult> BuildSolutionAsync(BasicLangSolution solution, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cleans and rebuilds a project.
    /// </summary>
    /// <param name="project">The project to rebuild.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the rebuild operation.</returns>
    Task<BuildResult> RebuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the current build operation.
    /// </summary>
    Task CancelBuildAsync();

    /// <summary>
    /// Cleans the build output for a project.
    /// </summary>
    /// <param name="project">The project to clean.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task CleanAsync(BasicLangProject project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised when build progress is updated.
    /// </summary>
    event EventHandler<BuildProgressEventArgs>? BuildProgress;

    /// <summary>
    /// Raised when a build completes (successfully or with errors).
    /// </summary>
    event EventHandler<BuildCompletedEventArgs>? BuildCompleted;

    /// <summary>
    /// Raised when a build starts.
    /// </summary>
    event EventHandler? BuildStarted;

    /// <summary>
    /// Raised when a build is cancelled.
    /// </summary>
    event EventHandler? BuildCancelled;
}

/// <summary>
/// Event arguments for build progress updates.
/// </summary>
public class BuildProgressEventArgs : EventArgs
{
    /// <summary>
    /// Gets the progress message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the optional completion percentage (0-100).
    /// </summary>
    public int? PercentComplete { get; }

    /// <summary>
    /// Gets the file currently being compiled.
    /// </summary>
    public string? CurrentFile { get; }

    /// <summary>
    /// Creates a new BuildProgressEventArgs instance.
    /// </summary>
    /// <param name="message">The progress message.</param>
    /// <param name="percentComplete">The optional completion percentage.</param>
    /// <param name="currentFile">The file currently being compiled.</param>
    public BuildProgressEventArgs(string message, int? percentComplete = null, string? currentFile = null)
    {
        Message = message;
        PercentComplete = percentComplete;
        CurrentFile = currentFile;
    }
}

/// <summary>
/// Event arguments for build completion.
/// </summary>
public class BuildCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the build result.
    /// </summary>
    public BuildResult Result { get; }

    /// <summary>
    /// Gets the total duration of the build.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Creates a new BuildCompletedEventArgs instance.
    /// </summary>
    /// <param name="result">The build result.</param>
    /// <param name="duration">The build duration.</param>
    public BuildCompletedEventArgs(BuildResult result, TimeSpan duration)
    {
        Result = result;
        Duration = duration;
    }
}
