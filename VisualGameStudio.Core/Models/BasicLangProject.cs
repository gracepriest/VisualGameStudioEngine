namespace VisualGameStudio.Core.Models;

public class BasicLangProject
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ProjectDirectory => Path.GetDirectoryName(FilePath) ?? "";
    public OutputType OutputType { get; set; } = OutputType.Exe;
    public string RootNamespace { get; set; } = "";
    public TargetBackend TargetBackend { get; set; } = TargetBackend.CSharp;
    public string Version { get; set; } = "1.0";

    public List<ProjectItem> Items { get; set; } = new();
    public List<ProjectReference> References { get; set; } = new();
    public Dictionary<string, BuildConfiguration> Configurations { get; set; } = new();

    public BuildConfiguration GetConfiguration(string name)
    {
        if (Configurations.TryGetValue(name, out var config))
            return config;
        return Configurations.Values.FirstOrDefault() ?? new BuildConfiguration { Name = "Debug" };
    }

    public IEnumerable<ProjectItem> GetSourceFiles()
    {
        return Items.Where(i => i.ItemType == ProjectItemType.Compile);
    }

    public string? GetMainFile()
    {
        var mainFile = Items.FirstOrDefault(i =>
            i.ItemType == ProjectItemType.Compile &&
            (i.FileName.Equals("Program.bas", StringComparison.OrdinalIgnoreCase) ||
             i.FileName.Equals("Main.bas", StringComparison.OrdinalIgnoreCase)));

        return mainFile != null ? Path.Combine(ProjectDirectory, mainFile.Include) : null;
    }
}

public enum OutputType
{
    Exe,
    Library,
    WinExe
}

public enum TargetBackend
{
    CSharp,
    Cpp,
    LLVM,
    MSIL
}
