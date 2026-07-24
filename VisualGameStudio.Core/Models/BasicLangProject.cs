namespace VisualGameStudio.Core.Models;

public class BasicLangProject
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ProjectDirectory => Path.GetDirectoryName(FilePath) ?? "";
    public OutputType OutputType { get; set; } = OutputType.Exe;
    public string RootNamespace { get; set; } = "";
    public TargetBackend TargetBackend { get; set; } = TargetBackend.CSharp;
    public ProjectLanguage Language { get; set; } = ProjectLanguage.BasicLang;

    /// <summary>True when this project builds to a native binary (C++ language OR C++ backend) — routed through CppProjectBuilder.</summary>
    public bool IsNativeBuild => Language == ProjectLanguage.Cpp || TargetBackend == TargetBackend.Cpp;

    /// <summary>C++-only settings; null for BasicLang projects. Modeled so IDE saves round-trip them.</summary>
    public CppProjectSettings? CppSettings { get; set; }
    public string Version { get; set; } = "1.0";

    public List<ProjectItem> Items { get; set; } = new();
    public List<ProjectReference> References { get; set; } = new();
    public List<PackageReference> PackageReferences { get; set; } = new();
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

public enum ProjectLanguage
{
    BasicLang,
    Cpp
}

public class CppProjectSettings
{
    public string CppStandard { get; set; } = "c++20";
    /// <summary>Toolchain pin ("llvm" | "gcc" | "msvc", lowercase); null = machine probe.</summary>
    public string? CppToolchain { get; set; }
    public List<string> IncludeDirs { get; set; } = new();
    public List<string> NativeLibs { get; set; } = new();
    public List<string> Defines { get; set; } = new();
}
