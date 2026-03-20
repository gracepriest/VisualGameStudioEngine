namespace VisualGameStudio.Core.Models;

public class BasicLangSolution
{
    public string FilePath { get; set; } = "";
    public string SolutionName { get; set; } = "";
    public string SolutionDirectory => Path.GetDirectoryName(FilePath) ?? "";
    public string? DefaultProject { get; set; }
    public string Version { get; set; } = "1.0";

    public List<SolutionProject> Projects { get; set; } = new();
    public List<SolutionFolder> Folders { get; set; } = new();
    public Dictionary<string, string> GlobalProperties { get; set; } = new();

    /// <summary>
    /// Gets the default/startup project, falling back to the first project.
    /// </summary>
    public SolutionProject? GetStartupProject()
    {
        if (!string.IsNullOrEmpty(DefaultProject))
        {
            var defaultProj = Projects.FirstOrDefault(p =>
                p.Name.Equals(DefaultProject, StringComparison.OrdinalIgnoreCase));
            if (defaultProj != null) return defaultProj;
        }
        return Projects.FirstOrDefault();
    }

    /// <summary>
    /// Gets a project by name, case-insensitive.
    /// </summary>
    public SolutionProject? GetProject(string name)
    {
        return Projects.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

public class SolutionProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string AbsolutePath { get; set; } = "";
    public string Type { get; set; } = "Exe"; // Exe, Library, WinExe
    public List<string> ProjectReferences { get; set; } = new();

    /// <summary>
    /// Computes the full path from the solution directory and relative path.
    /// </summary>
    public string GetFullPath(string solutionDirectory)
    {
        return Path.GetFullPath(Path.Combine(solutionDirectory, RelativePath));
    }
}

public class SolutionFolder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public List<Guid> ProjectIds { get; set; } = new();
}
