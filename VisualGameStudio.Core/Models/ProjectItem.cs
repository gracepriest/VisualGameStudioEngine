namespace VisualGameStudio.Core.Models;

public class ProjectItem
{
    public string Include { get; set; } = "";
    public ProjectItemType ItemType { get; set; } = ProjectItemType.None;
    public Dictionary<string, string> Metadata { get; set; } = new();

    public string FileName => Path.GetFileName(Include);
    public string Directory => Path.GetDirectoryName(Include) ?? "";

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
