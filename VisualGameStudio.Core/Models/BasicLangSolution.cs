namespace VisualGameStudio.Core.Models;

public class BasicLangSolution
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string SolutionDirectory => Path.GetDirectoryName(FilePath) ?? "";
    public string Version { get; set; } = "1.0";

    public List<SolutionProject> Projects { get; set; } = new();
    public List<SolutionFolder> Folders { get; set; } = new();
    public Dictionary<string, string> GlobalProperties { get; set; } = new();

    public SolutionProject? GetStartupProject()
    {
        return Projects.FirstOrDefault(p => p.IsStartupProject) ?? Projects.FirstOrDefault();
    }
}

public class SolutionProject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsStartupProject { get; set; }

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
