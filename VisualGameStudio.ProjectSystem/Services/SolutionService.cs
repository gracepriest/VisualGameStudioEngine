using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Manages multi-project BasicLang solutions (.blsln files).
/// </summary>
public class SolutionService : ISolutionService
{
    private readonly SolutionSerializer _serializer = new();

    public BasicLangSolution? CurrentSolution { get; private set; }

    public bool HasSolution => CurrentSolution != null;

    public event EventHandler? SolutionChanged;
    public event EventHandler? SolutionLoaded;
    public event EventHandler? SolutionClosed;

    public async Task<BasicLangSolution> LoadSolutionAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Solution file not found.", filePath);

        CurrentSolution = await _serializer.LoadAsync(filePath);
        SolutionLoaded?.Invoke(this, EventArgs.Empty);
        return CurrentSolution;
    }

    public async Task<BasicLangSolution> CreateSolutionAsync(string name, string directory)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Solution name cannot be null or empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("Directory cannot be null or empty.", nameof(directory));

        Directory.CreateDirectory(directory);

        var solution = new BasicLangSolution
        {
            SolutionName = name,
            FilePath = Path.Combine(directory, $"{name}.blsln"),
            Version = "1.0"
        };

        await _serializer.SaveAsync(solution);

        CurrentSolution = solution;
        SolutionLoaded?.Invoke(this, EventArgs.Empty);
        return solution;
    }

    public async Task SaveSolutionAsync()
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");

        await _serializer.SaveAsync(CurrentSolution);
    }

    public Task CloseSolutionAsync()
    {
        CurrentSolution = null;
        SolutionClosed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task<SolutionProject> AddNewProjectAsync(string name, string type, string? relativePath = null)
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Project name cannot be null or empty.", nameof(name));

        if (CurrentSolution.GetProject(name) != null)
            throw new InvalidOperationException($"A project named '{name}' already exists in the solution.");

        relativePath ??= Path.Combine(name, $"{name}.blproj");

        var project = new SolutionProject
        {
            Name = name,
            RelativePath = relativePath,
            Type = type ?? "Exe"
        };

        // Resolve and set absolute path
        project.AbsolutePath = Path.GetFullPath(
            Path.Combine(CurrentSolution.SolutionDirectory, relativePath));

        // Create the project directory
        var projectDir = Path.GetDirectoryName(project.AbsolutePath);
        if (!string.IsNullOrEmpty(projectDir))
        {
            Directory.CreateDirectory(projectDir);
        }

        // Create a minimal .blproj file
        var blprojContent = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>{name}</ProjectName>
                <OutputType>{type ?? "Exe"}</OutputType>
                <RootNamespace>{name}</RootNamespace>
              </PropertyGroup>
              <ItemGroup>
              </ItemGroup>
            </BasicLangProject>
            """;
        await File.WriteAllTextAsync(project.AbsolutePath, blprojContent);

        CurrentSolution.Projects.Add(project);
        await _serializer.SaveAsync(CurrentSolution);

        SolutionChanged?.Invoke(this, EventArgs.Empty);
        return project;
    }

    public async Task AddExistingProjectAsync(string blprojPath)
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");
        if (string.IsNullOrWhiteSpace(blprojPath))
            throw new ArgumentException("Project path cannot be null or empty.", nameof(blprojPath));
        if (!File.Exists(blprojPath))
            throw new FileNotFoundException("Project file not found.", blprojPath);

        var absolutePath = Path.GetFullPath(blprojPath);
        var name = Path.GetFileNameWithoutExtension(absolutePath);

        if (CurrentSolution.GetProject(name) != null)
            throw new InvalidOperationException($"A project named '{name}' already exists in the solution.");

        // Compute relative path from solution directory
        var solutionDir = CurrentSolution.SolutionDirectory;
        var relativePath = Path.GetRelativePath(solutionDir, absolutePath);

        var project = new SolutionProject
        {
            Name = name,
            RelativePath = relativePath,
            AbsolutePath = absolutePath,
            Type = "Exe" // Default; caller can change after adding
        };

        CurrentSolution.Projects.Add(project);
        await _serializer.SaveAsync(CurrentSolution);

        SolutionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveProject(string projectName)
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");

        var project = CurrentSolution.GetProject(projectName)
            ?? throw new InvalidOperationException($"Project '{projectName}' not found in the solution.");

        CurrentSolution.Projects.Remove(project);

        // Remove references to this project from other projects
        foreach (var other in CurrentSolution.Projects)
        {
            other.ProjectReferences.Remove(projectName);
        }

        // Clear startup project if it was this one
        if (string.Equals(CurrentSolution.DefaultProject, projectName, StringComparison.OrdinalIgnoreCase))
        {
            CurrentSolution.DefaultProject = null;
        }

        SolutionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddProjectReference(string fromProject, string toProject)
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");

        var from = CurrentSolution.GetProject(fromProject)
            ?? throw new InvalidOperationException($"Project '{fromProject}' not found in the solution.");
        var _ = CurrentSolution.GetProject(toProject)
            ?? throw new InvalidOperationException($"Project '{toProject}' not found in the solution.");

        if (string.Equals(fromProject, toProject, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A project cannot reference itself.");

        if (!from.ProjectReferences.Contains(toProject, StringComparer.OrdinalIgnoreCase))
        {
            from.ProjectReferences.Add(toProject);
        }

        // Validate no circular dependency by attempting a topological sort
        try
        {
            GetBuildOrder();
        }
        catch (InvalidOperationException)
        {
            // Roll back the reference
            from.ProjectReferences.Remove(toProject);
            throw new InvalidOperationException(
                $"Adding a reference from '{fromProject}' to '{toProject}' would create a circular dependency.");
        }

        SolutionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveProjectReference(string fromProject, string toProject)
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");

        var from = CurrentSolution.GetProject(fromProject)
            ?? throw new InvalidOperationException($"Project '{fromProject}' not found in the solution.");

        from.ProjectReferences.Remove(toProject);
        SolutionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetStartupProject(string projectName)
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");

        _ = CurrentSolution.GetProject(projectName)
            ?? throw new InvalidOperationException($"Project '{projectName}' not found in the solution.");

        CurrentSolution.DefaultProject = projectName;
        SolutionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns projects in topological order using Kahn's algorithm.
    /// Dependencies appear before the projects that depend on them.
    /// </summary>
    public List<SolutionProject> GetBuildOrder()
    {
        if (CurrentSolution == null)
            throw new InvalidOperationException("No solution is currently open.");

        var projects = CurrentSolution.Projects;
        var projectMap = new Dictionary<string, SolutionProject>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Initialize graph
        foreach (var project in projects)
        {
            projectMap[project.Name] = project;
            inDegree[project.Name] = 0;
            adjacency[project.Name] = new List<string>();
        }

        // Build edges: if A references B, then B -> A (B must be built before A)
        foreach (var project in projects)
        {
            foreach (var dep in project.ProjectReferences)
            {
                if (projectMap.ContainsKey(dep))
                {
                    adjacency[dep].Add(project.Name);
                    inDegree[project.Name]++;
                }
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
                queue.Enqueue(kvp.Key);
        }

        var sorted = new List<SolutionProject>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(projectMap[current]);

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (sorted.Count != projects.Count)
        {
            throw new InvalidOperationException(
                "Circular dependency detected among projects. Cannot determine build order.");
        }

        return sorted;
    }
}
