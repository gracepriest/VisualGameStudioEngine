using System.Text.Json.Serialization;

namespace VisualGameStudio.Core.Models;

/// <summary>
/// Represents a multi-root workspace containing one or more folders.
/// Persisted as a .vgs-workspace JSON file.
/// </summary>
public class Workspace
{
    /// <summary>
    /// The file path of the .vgs-workspace file, or null for an untitled workspace.
    /// </summary>
    [JsonIgnore]
    public string? FilePath { get; set; }

    /// <summary>
    /// The folders included in this workspace.
    /// </summary>
    [JsonPropertyName("folders")]
    public List<WorkspaceFolder> Folders { get; set; } = new();

    /// <summary>
    /// Per-workspace settings that override global settings.
    /// </summary>
    [JsonPropertyName("settings")]
    public Dictionary<string, object?> Settings { get; set; } = new();

    /// <summary>
    /// Gets a display name for the workspace.
    /// </summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(FilePath))
                return Path.GetFileNameWithoutExtension(FilePath);
            if (Folders.Count == 1)
                return Folders[0].DisplayName;
            if (Folders.Count > 1)
                return $"{Folders[0].DisplayName} (+{Folders.Count - 1} more)";
            return "Untitled Workspace";
        }
    }
}

/// <summary>
/// Represents a single folder within a workspace.
/// </summary>
public class WorkspaceFolder
{
    /// <summary>
    /// The absolute path to the folder.
    /// </summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>
    /// Optional display name for the folder. Defaults to the folder's directory name.
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Gets the effective display name (custom name or folder directory name).
    /// </summary>
    [JsonIgnore]
    public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : System.IO.Path.GetFileName(Path) ?? Path;
}
