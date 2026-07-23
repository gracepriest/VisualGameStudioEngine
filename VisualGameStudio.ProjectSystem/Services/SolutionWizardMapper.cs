using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Pure translation between the New Project wizard's <see cref="CreateProjectOptions"/> and the
/// solution-creation / add-to-solution option shapes. No I/O — unit-testable in isolation.
/// </summary>
public static class SolutionWizardMapper
{
    public static CreateSolutionOptions BuildSolutionOptions(
        string name, string location, bool initGit, CreateProjectOptions firstProject)
    {
        var opts = new CreateSolutionOptions
        {
            Name = name,
            Location = location,
            CreateGitRepository = initGit,
            SolutionType = firstProject.SolutionType,
        };
        opts.InitialProjects.Add(firstProject);
        return opts;
    }

    public static CreateProjectOptions BuildAddToSolutionOptions(CreateProjectOptions opts, BasicLangSolution solution)
    {
        opts.Location = solution.SolutionDirectory;
        opts.CreateSolutionFolder = false;
        opts.CreateGitRepository = false;
        opts.AddToExistingSolution = true;
        opts.ExistingSolutionPath = solution.FilePath;
        return opts;
    }

    public static ProjectCreationResult ToProjectResult(SolutionCreationResult sr)
    {
        if (!sr.Success)
            return new ProjectCreationResult { Success = false, Error = sr.Error, SolutionPath = sr.SolutionPath };
        if (sr.ProjectPaths.Count == 0)
            return new ProjectCreationResult
            {
                Success = false, SolutionPath = sr.SolutionPath,
                Error = "The solution was created but its first project could not be scaffolded."
            };
        return new ProjectCreationResult
        {
            Success = true, SolutionPath = sr.SolutionPath,
            ProjectPath = sr.ProjectPaths[0], FilesToOpen = sr.FilesToOpen
        };
    }
}
