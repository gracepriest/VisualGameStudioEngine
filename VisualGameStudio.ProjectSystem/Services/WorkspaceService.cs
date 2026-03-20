using System.Text.Json;
using System.Text.Json.Serialization;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Manages multi-root workspaces, persisted as .vgs-workspace JSON files.
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public Workspace? CurrentWorkspace { get; private set; }

    public IReadOnlyList<WorkspaceFolder> Folders =>
        CurrentWorkspace?.Folders.AsReadOnly() ?? (IReadOnlyList<WorkspaceFolder>)Array.Empty<WorkspaceFolder>();

    public bool HasUnsavedChanges { get; private set; }

    public event EventHandler? WorkspaceChanged;

    public async Task OpenWorkspaceAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (CurrentWorkspace != null)
        {
            await CloseWorkspaceAsync();
        }

        var json = await File.ReadAllTextAsync(filePath, cancellationToken);
        var workspace = JsonSerializer.Deserialize<Workspace>(json, _jsonOptions)
            ?? new Workspace();

        workspace.FilePath = filePath;

        // Resolve relative paths to absolute paths based on workspace file location
        var workspaceDir = Path.GetDirectoryName(filePath) ?? "";
        foreach (var folder in workspace.Folders)
        {
            if (!Path.IsPathRooted(folder.Path))
            {
                folder.Path = Path.GetFullPath(Path.Combine(workspaceDir, folder.Path));
            }
        }

        CurrentWorkspace = workspace;
        HasUnsavedChanges = false;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SaveWorkspaceAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        if (CurrentWorkspace == null) return;

        filePath ??= CurrentWorkspace.FilePath;
        if (string.IsNullOrEmpty(filePath))
            throw new InvalidOperationException("No file path specified for saving workspace.");

        CurrentWorkspace.FilePath = filePath;

        // Create a serialization copy with relative paths when possible
        var workspaceDir = Path.GetDirectoryName(filePath) ?? "";
        var serializeCopy = new Workspace
        {
            Settings = CurrentWorkspace.Settings
        };
        foreach (var folder in CurrentWorkspace.Folders)
        {
            var relativePath = TryGetRelativePath(workspaceDir, folder.Path);
            serializeCopy.Folders.Add(new WorkspaceFolder
            {
                Path = relativePath,
                Name = folder.Name
            });
        }

        var json = JsonSerializer.Serialize(serializeCopy, _jsonOptions);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await File.WriteAllTextAsync(filePath, json, cancellationToken);
        HasUnsavedChanges = false;
    }

    public Task CloseWorkspaceAsync()
    {
        CurrentWorkspace = null;
        HasUnsavedChanges = false;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void AddFolder(string folderPath, string? displayName = null)
    {
        if (CurrentWorkspace == null)
        {
            CreateNewWorkspace();
        }

        folderPath = Path.GetFullPath(folderPath);

        // Don't add duplicate folders
        if (CurrentWorkspace!.Folders.Any(f =>
            string.Equals(f.Path, folderPath, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        CurrentWorkspace.Folders.Add(new WorkspaceFolder
        {
            Path = folderPath,
            Name = displayName
        });

        HasUnsavedChanges = true;
        WorkspaceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFolder(string folderPath)
    {
        if (CurrentWorkspace == null) return;

        folderPath = Path.GetFullPath(folderPath);
        var folder = CurrentWorkspace.Folders.FirstOrDefault(f =>
            string.Equals(f.Path, folderPath, StringComparison.OrdinalIgnoreCase));

        if (folder != null)
        {
            CurrentWorkspace.Folders.Remove(folder);
            HasUnsavedChanges = true;
            WorkspaceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void CreateNewWorkspace()
    {
        CurrentWorkspace = new Workspace();
        HasUnsavedChanges = false;
    }

    private static string TryGetRelativePath(string basePath, string fullPath)
    {
        try
        {
            var relative = Path.GetRelativePath(basePath, fullPath);
            // Only use relative if it doesn't start with ".." too many levels deep
            if (!relative.StartsWith("..\\..\\..\\") && !relative.StartsWith("../../../"))
                return relative.Replace('\\', '/');
        }
        catch { }
        return fullPath.Replace('\\', '/');
    }
}
