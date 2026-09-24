namespace VisualGameStudio.Core.Models;

public class ProjectItem
{
    public string Include { get; set; } = "";
    public ProjectItemType ItemType { get; set; } = ProjectItemType.None;
    public Dictionary<string, string> Metadata { get; set; } = new();

    public string FileName => Path.GetFileName(LocalInclude);
    public string Directory => Path.GetDirectoryName(LocalInclude) ?? "";

    // Include may still carry the MSBuild-style backslashes of a Windows-authored project file
    // (items built directly, not through the loader); on Linux/macOS Path would read the whole
    // string as one file name. A no-op on Windows.
    private string LocalInclude => Path.DirectorySeparatorChar == '\\'
        ? Include
        : Include.Replace('\\', Path.DirectorySeparatorChar);

    public ProjectItem() { }

    public ProjectItem(string include, ProjectItemType itemType)
    {
        Include = include;
        ItemType = itemType;
    }
}

public enum ProjectItemType
{
    None,
    Compile,
    Content,
    Resource,
    Folder,
    Reference
}

public class ProjectReference
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? Version { get; set; }
    public bool IsProjectReference { get; set; }
}

/// <summary>
/// A NuGet package dependency (&lt;PackageReference Include="Name" Version="..." /&gt;). Modeled on
/// <see cref="BasicLangProject"/> so IDE saves round-trip it instead of silently dropping it;
/// the compiler-side build restores packages from these entries.
/// </summary>
public class PackageReference
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "*";
}
