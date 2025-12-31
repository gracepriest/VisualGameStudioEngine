using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class SolutionExplorerViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<TreeNode> _nodes = new();

    [ObservableProperty]
    private TreeNode? _selectedNode;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = "";

    public event EventHandler<string>? FileOpenRequested;
    public event EventHandler<string>? FileDeleted;
    public event EventHandler<(string OldPath, string NewPath)>? FileRenamed;

    public SolutionExplorerViewModel(IProjectService projectService, IFileService fileService, IDialogService dialogService)
    {
        _projectService = projectService;
        _fileService = fileService;
        _dialogService = dialogService;

        _projectService.ProjectOpened += OnProjectOpened;
        _projectService.ProjectClosed += OnProjectClosed;
        _projectService.ProjectChanged += OnProjectChanged;
    }

    private void OnProjectOpened(object? sender, ProjectEventArgs e)
    {
        RefreshTree(e.Project);
    }

    private void OnProjectClosed(object? sender, ProjectEventArgs e)
    {
        Nodes.Clear();
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        if (_projectService.CurrentProject != null)
        {
            RefreshTree(_projectService.CurrentProject);
        }
    }

    private void RefreshTree(BasicLangProject project)
    {
        Nodes.Clear();

        var projectNode = new TreeNode
        {
            Name = project.Name,
            NodeType = TreeNodeType.Project,
            FullPath = project.FilePath,
            IsExpanded = true
        };

        // Group items by directory
        var itemsByDir = project.Items
            .GroupBy(i => i.Directory)
            .OrderBy(g => g.Key);

        foreach (var group in itemsByDir)
        {
            TreeNode parentNode = projectNode;

            if (!string.IsNullOrEmpty(group.Key))
            {
                // Create folder nodes for the path
                var parts = group.Key.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                foreach (var part in parts)
                {
                    var existingFolder = parentNode.Children.FirstOrDefault(
                        c => c.NodeType == TreeNodeType.Folder && c.Name == part);

                    if (existingFolder != null)
                    {
                        parentNode = existingFolder;
                    }
                    else
                    {
                        var folderNode = new TreeNode
                        {
                            Name = part,
                            NodeType = TreeNodeType.Folder,
                            FullPath = Path.Combine(project.ProjectDirectory, part),
                            IsExpanded = true
                        };
                        parentNode.Children.Add(folderNode);
                        parentNode = folderNode;
                    }
                }
            }

            // Add files
            foreach (var item in group.OrderBy(i => i.FileName))
            {
                var fileNode = new TreeNode
                {
                    Name = item.FileName,
                    NodeType = GetNodeType(item),
                    FullPath = Path.Combine(project.ProjectDirectory, item.Include)
                };
                parentNode.Children.Add(fileNode);
            }
        }

        Nodes.Add(projectNode);
    }

    private static TreeNodeType GetNodeType(ProjectItem item)
    {
        return item.ItemType switch
        {
            ProjectItemType.Compile => TreeNodeType.SourceFile,
            ProjectItemType.Content => TreeNodeType.ContentFile,
            ProjectItemType.Resource => TreeNodeType.Resource,
            _ => TreeNodeType.File
        };
    }

    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedNode != null && SelectedNode.IsFile)
        {
            FileOpenRequested?.Invoke(this, SelectedNode.FullPath);
        }
    }

    [RelayCommand]
    private void DoubleClick()
    {
        OpenFile();
    }

    [RelayCommand]
    private async Task AddNewFileAsync()
    {
        if (_projectService.CurrentProject == null) return;

        var targetDir = GetTargetDirectory();
        if (targetDir == null) return;

        // Prompt for file name
        var fileName = await _dialogService.PromptAsync("New File", "Enter file name:", "NewFile.bas");
        if (string.IsNullOrWhiteSpace(fileName)) return;

        // Ensure .bas extension
        if (!fileName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".bl", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".bas";
        }

        var filePath = Path.Combine(targetDir, fileName);

        // Check if file already exists
        if (File.Exists(filePath))
        {
            await _dialogService.ShowMessageAsync("Error", $"File '{fileName}' already exists.");
            return;
        }

        // Create the file with default content
        var defaultContent = $"' {fileName}\n' Created: {DateTime.Now:yyyy-MM-dd}\n\n";
        await File.WriteAllTextAsync(filePath, defaultContent);

        // Add to project
        var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, filePath);
        _projectService.CurrentProject.Items.Add(new ProjectItem
        {
            Include = relativePath,
            ItemType = ProjectItemType.Compile
        });

        await _projectService.SaveProjectAsync();
        RefreshTree(_projectService.CurrentProject);

        // Open the new file
        FileOpenRequested?.Invoke(this, filePath);
    }

    [RelayCommand]
    private async Task AddNewFolderAsync()
    {
        if (_projectService.CurrentProject == null) return;

        var targetDir = GetTargetDirectory();
        if (targetDir == null) return;

        // Prompt for folder name
        var folderName = await _dialogService.PromptAsync("New Folder", "Enter folder name:", "NewFolder");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var folderPath = Path.Combine(targetDir, folderName);

        // Check if folder already exists
        if (Directory.Exists(folderPath))
        {
            await _dialogService.ShowMessageAsync("Error", $"Folder '{folderName}' already exists.");
            return;
        }

        // Create the folder
        Directory.CreateDirectory(folderPath);
        RefreshTree(_projectService.CurrentProject);
    }

    [RelayCommand]
    private async Task AddExistingFileAsync()
    {
        if (_projectService.CurrentProject == null) return;

        var files = await _dialogService.ShowOpenFileDialogAsync(
            "Add Existing File",
            new[] { ("BasicLang Files", new[] { "*.bas", "*.bl" }), ("All Files", new[] { "*.*" }) },
            allowMultiple: true);

        if (files == null || files.Length == 0) return;

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, filePath);

            // Check if already in project
            if (_projectService.CurrentProject.Items.Any(i =>
                string.Equals(i.Include, relativePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _projectService.CurrentProject.Items.Add(new ProjectItem
            {
                Include = relativePath,
                ItemType = ProjectItemType.Compile
            });
        }

        await _projectService.SaveProjectAsync();
        RefreshTree(_projectService.CurrentProject);
    }

    [RelayCommand]
    private void StartRename()
    {
        if (SelectedNode == null || SelectedNode.IsProject) return;

        RenameText = SelectedNode.Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private async Task ConfirmRenameAsync()
    {
        if (!IsRenaming || SelectedNode == null || string.IsNullOrWhiteSpace(RenameText)) return;

        var oldPath = SelectedNode.FullPath;
        var newName = RenameText.Trim();
        var parentDir = Path.GetDirectoryName(oldPath) ?? "";
        var newPath = Path.Combine(parentDir, newName);

        if (oldPath == newPath)
        {
            CancelRename();
            return;
        }

        // Check if target exists
        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            await _dialogService.ShowMessageAsync("Error", $"'{newName}' already exists.");
            return;
        }

        try
        {
            if (SelectedNode.IsFile)
            {
                File.Move(oldPath, newPath);

                // Update project reference
                if (_projectService.CurrentProject != null)
                {
                    var oldRelative = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, oldPath);
                    var newRelative = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, newPath);

                    var item = _projectService.CurrentProject.Items.FirstOrDefault(i =>
                        string.Equals(i.Include, oldRelative, StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        item.Include = newRelative;
                        await _projectService.SaveProjectAsync();
                    }
                }

                FileRenamed?.Invoke(this, (oldPath, newPath));
            }
            else if (SelectedNode.IsFolder)
            {
                Directory.Move(oldPath, newPath);

                // Update all project references in this folder
                if (_projectService.CurrentProject != null)
                {
                    var oldRelative = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, oldPath);
                    var newRelative = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, newPath);

                    foreach (var item in _projectService.CurrentProject.Items)
                    {
                        if (item.Include.StartsWith(oldRelative, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Include = newRelative + item.Include.Substring(oldRelative.Length);
                        }
                    }

                    await _projectService.SaveProjectAsync();
                }
            }

            if (_projectService.CurrentProject != null)
            {
                RefreshTree(_projectService.CurrentProject);
            }
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to rename: {ex.Message}");
        }
        finally
        {
            CancelRename();
        }
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
        RenameText = "";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedNode == null || SelectedNode.IsProject) return;
        if (_projectService.CurrentProject == null) return;

        var itemName = SelectedNode.Name;
        var confirmed = await _dialogService.ConfirmAsync(
            "Delete",
            $"Are you sure you want to delete '{itemName}'?");

        if (!confirmed) return;

        try
        {
            var path = SelectedNode.FullPath;

            if (SelectedNode.IsFile)
            {
                File.Delete(path);

                // Remove from project
                var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, path);
                var item = _projectService.CurrentProject.Items.FirstOrDefault(i =>
                    string.Equals(i.Include, relativePath, StringComparison.OrdinalIgnoreCase));

                if (item != null)
                {
                    _projectService.CurrentProject.Items.Remove(item);
                    await _projectService.SaveProjectAsync();
                }

                FileDeleted?.Invoke(this, path);
            }
            else if (SelectedNode.IsFolder)
            {
                Directory.Delete(path, true);

                // Remove all items in this folder from project
                var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, path);
                var itemsToRemove = _projectService.CurrentProject.Items
                    .Where(i => i.Include.StartsWith(relativePath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var item in itemsToRemove)
                {
                    _projectService.CurrentProject.Items.Remove(item);
                }

                await _projectService.SaveProjectAsync();
            }

            RefreshTree(_projectService.CurrentProject);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to delete: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedNode == null) return;

        var path = SelectedNode.FullPath;
        if (SelectedNode.IsFile)
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedNode == null) return;
        // Note: Clipboard access requires platform-specific implementation
        // This would need to be wired up via the shell
    }

    private string? GetTargetDirectory()
    {
        if (_projectService.CurrentProject == null) return null;

        if (SelectedNode == null || SelectedNode.IsProject)
        {
            return _projectService.CurrentProject.ProjectDirectory;
        }

        if (SelectedNode.IsFolder)
        {
            return SelectedNode.FullPath;
        }

        // For files, return parent directory
        return Path.GetDirectoryName(SelectedNode.FullPath);
    }
}

public partial class TreeNode : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private TreeNodeType _nodeType;

    [ObservableProperty]
    private string _fullPath = "";

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<TreeNode> _children = new();

    public bool IsFile => NodeType is TreeNodeType.SourceFile or TreeNodeType.ContentFile
        or TreeNodeType.Resource or TreeNodeType.File;

    public bool IsFolder => NodeType == TreeNodeType.Folder;
    public bool IsProject => NodeType == TreeNodeType.Project;
}

public enum TreeNodeType
{
    Project,
    Folder,
    SourceFile,
    ContentFile,
    Resource,
    File
}

/// <summary>
/// Converter to get an icon for a tree node type
/// </summary>
public class NodeTypeIconConverter : IValueConverter
{
    public static readonly NodeTypeIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TreeNodeType nodeType)
        {
            return nodeType switch
            {
                TreeNodeType.Project => "📦",
                TreeNodeType.Folder => "📁",
                TreeNodeType.SourceFile => "📄",
                TreeNodeType.ContentFile => "📋",
                TreeNodeType.Resource => "🔧",
                TreeNodeType.File => "📄",
                _ => "📄"
            };
        }
        return "📄";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
