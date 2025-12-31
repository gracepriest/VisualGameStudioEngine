using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Abstractions.Services;

public interface IBuildService
{
    bool IsBuilding { get; }
    BuildConfiguration CurrentConfiguration { get; set; }

    Task<BuildResult> BuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default);
    Task<BuildResult> BuildSolutionAsync(BasicLangSolution solution, CancellationToken cancellationToken = default);
    Task<BuildResult> RebuildProjectAsync(BasicLangProject project, CancellationToken cancellationToken = default);
    Task CancelBuildAsync();
    Task CleanAsync(BasicLangProject project, CancellationToken cancellationToken = default);

    event EventHandler<BuildProgressEventArgs>? BuildProgress;
    event EventHandler<BuildCompletedEventArgs>? BuildCompleted;
    event EventHandler? BuildStarted;
    event EventHandler? BuildCancelled;
}

public class BuildProgressEventArgs : EventArgs
{
    public string Message { get; }
    public int? PercentComplete { get; }
    public string? CurrentFile { get; }

    public BuildProgressEventArgs(string message, int? percentComplete = null, string? currentFile = null)
    {
        Message = message;
        PercentComplete = percentComplete;
        CurrentFile = currentFile;
    }
}

public class BuildCompletedEventArgs : EventArgs
{
    public BuildResult Result { get; }
    public TimeSpan Duration { get; }

    public BuildCompletedEventArgs(BuildResult result, TimeSpan duration)
    {
        Result = result;
        Duration = duration;
    }
}
