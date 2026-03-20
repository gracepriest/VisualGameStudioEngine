using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
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
    private readonly IGitService? _gitService;
    private readonly IWorkspaceService? _workspaceService;

    [ObservableProperty]
    private ObservableCollection<TreeNode> _nodes = new();

    [ObservableProperty]
    private TreeNode? _selectedNode;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = "";

    [ObservableProperty]
    private bool _isCreatingNew;

    [ObservableProperty]
    private string _newItemName = "";

    [ObservableProperty]
    private bool _isCreatingFolder;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _isFilterVisible;

    [ObservableProperty]
    private ObservableCollection<OpenEditorItem> _openEditors = new();

    [ObservableProperty]
    private bool _isOpenEditorsExpanded = true;

    /// <summary>
    /// Tracks multi-selected nodes (Ctrl+Click, Shift+Click).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<TreeNode> _selectedNodes = new();

    /// <summary>
    /// Clipboard for cut/copy operations.
    /// </summary>
    private List<TreeNode> _clipboardNodes = new();
    private bool _isCutOperation;

    /// <summary>
    /// Node being dragged for drag-and-drop.
    /// </summary>
    [ObservableProperty]
    private TreeNode? _dragNode;

    [ObservableProperty]
    private TreeNode? _dropTarget;

    [ObservableProperty]
    private DropPosition _dropPosition;

    public event EventHandler<string>? FileOpenRequested;
    public event EventHandler<string>? FileOpenToSideRequested;
    public event EventHandler<string>? FileDeleted;
    public event EventHandler<(string OldPath, string NewPath)>? FileRenamed;
    public event EventHandler<string>? OpenInTerminalRequested;
    public event EventHandler<string>? FindInFolderRequested;
    public event EventHandler<string>? ShowFileHistoryRequested;
    /// <summary>Raised to request clipboard set from the View (needs TopLevel access).</summary>
    public event EventHandler<string>? ClipboardCopyRequested;

    public SolutionExplorerViewModel(IProjectService projectService, IFileService fileService, IDialogService dialogService, IGitService? gitService = null, IWorkspaceService? workspaceService = null)
    {
        _projectService = projectService;
        _fileService = fileService;
        _dialogService = dialogService;
        _gitService = gitService;
        _workspaceService = workspaceService;

        _projectService.ProjectOpened += OnProjectOpened;
        _projectService.ProjectClosed += OnProjectClosed;
        _projectService.ProjectChanged += OnProjectChanged;

        if (_gitService != null)
        {
            _gitService.StatusChanged += OnGitStatusChanged;
        }

        if (_workspaceService != null)
        {
            _workspaceService.WorkspaceChanged += OnWorkspaceChanged;
        }
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

    private async void OnGitStatusChanged(object? sender, EventArgs e)
    {
        await RefreshGitDecorationsAsync();
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

        // Refresh git decorations
        _ = RefreshGitDecorationsAsync();

        // Apply filter if active
        if (!string.IsNullOrEmpty(FilterText))
        {
            ApplyFilter();
        }
    }

    // ─── Multi-Root Workspace Support ────────────────────────────────

    private void OnWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_workspaceService?.CurrentWorkspace != null && _workspaceService.Folders.Count > 0)
        {
            RefreshWorkspaceTree();
        }
    }

    /// <summary>
    /// Refreshes the tree to show all workspace folders as root nodes.
    /// Each folder gets its own expandable root in the solution explorer.
    /// </summary>
    public void RefreshWorkspaceTree()
    {
        if (_workspaceService?.CurrentWorkspace == null) return;

        Nodes.Clear();

        foreach (var folder in _workspaceService.Folders)
        {
            if (!Directory.Exists(folder.Path)) continue;

            var folderRootNode = new TreeNode
            {
                Name = folder.DisplayName,
                NodeType = TreeNodeType.WorkspaceFolder,
                FullPath = folder.Path,
                IsExpanded = true
            };

            BuildDirectoryTree(folderRootNode, folder.Path, maxDepth: 3);
            Nodes.Add(folderRootNode);
        }

        // Refresh git decorations
        _ = RefreshGitDecorationsAsync();

        if (!string.IsNullOrEmpty(FilterText))
        {
            ApplyFilter();
        }
    }

    /// <summary>
    /// Loads a single folder into the tree (for non-project folder opening).
    /// </summary>
    public void LoadFolderTree(string folderPath)
    {
        Nodes.Clear();

        var folderName = Path.GetFileName(folderPath) ?? folderPath;
        var rootNode = new TreeNode
        {
            Name = folderName,
            NodeType = TreeNodeType.Folder,
            FullPath = folderPath,
            IsExpanded = true
        };

        BuildDirectoryTree(rootNode, folderPath, maxDepth: 3);
        Nodes.Add(rootNode);

        _ = RefreshGitDecorationsAsync();
    }

    /// <summary>
    /// Recursively builds a directory tree under the given parent node.
    /// </summary>
    private void BuildDirectoryTree(TreeNode parentNode, string directoryPath, int maxDepth, int currentDepth = 0)
    {
        if (currentDepth >= maxDepth) return;

        try
        {
            // Add subdirectories
            var directories = Directory.GetDirectories(directoryPath)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);

            foreach (var dir in directories)
            {
                var dirName = Path.GetFileName(dir);
                // Skip hidden/build directories
                if (dirName.StartsWith('.') || dirName is "bin" or "obj" or "node_modules" or ".git")
                    continue;

                var dirNode = new TreeNode
                {
                    Name = dirName,
                    NodeType = TreeNodeType.Folder,
                    FullPath = dir,
                    IsExpanded = false
                };

                BuildDirectoryTree(dirNode, dir, maxDepth, currentDepth + 1);
                parentNode.Children.Add(dirNode);
            }

            // Add files
            var files = Directory.GetFiles(directoryPath)
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (fileName.StartsWith('.') && fileName != ".gitignore") continue;

                var fileNode = new TreeNode
                {
                    Name = fileName,
                    NodeType = GetFileNodeType(fileName),
                    FullPath = file
                };
                parentNode.Children.Add(fileNode);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static TreeNodeType GetFileNodeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".bas" or ".bl" or ".cs" or ".vb" or ".cpp" or ".h" or ".fs" => TreeNodeType.SourceFile,
            ".png" or ".jpg" or ".bmp" or ".ico" or ".svg" => TreeNodeType.Resource,
            _ => TreeNodeType.File
        };
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

    // ─── Git Decorations ────────────────────────────────────────────

    private async Task RefreshGitDecorationsAsync()
    {
        if (_gitService == null || !_gitService.IsGitRepository) return;

        try
        {
            var status = await _gitService.GetStatusAsync();
            var changeMap = new Dictionary<string, GitFileStatus>(StringComparer.OrdinalIgnoreCase);

            foreach (var change in status.Changes)
            {
                changeMap[change.FilePath] = change.Status;
            }

            foreach (var node in Nodes)
            {
                ApplyGitStatusRecursive(node, changeMap);
            }
        }
        catch
        {
            // Silently ignore git errors
        }
    }

    private void ApplyGitStatusRecursive(TreeNode node, Dictionary<string, GitFileStatus> changeMap)
    {
        if (node.IsFile && !string.IsNullOrEmpty(node.FullPath))
        {
            if (changeMap.TryGetValue(node.FullPath, out var status))
            {
                node.GitStatus = status;
            }
            else
            {
                node.GitStatus = GitFileStatus.Unmodified;
            }

            // Check read-only
            try
            {
                if (File.Exists(node.FullPath))
                {
                    var info = new FileInfo(node.FullPath);
                    node.IsReadOnly = info.IsReadOnly;
                }
            }
            catch { }
        }

        // Propagate: folder shows worst status of children
        if (node.Children.Count > 0)
        {
            var worstStatus = GitFileStatus.Unmodified;
            foreach (var child in node.Children)
            {
                ApplyGitStatusRecursive(child, changeMap);
                if (child.GitStatus != GitFileStatus.Unmodified && worstStatus == GitFileStatus.Unmodified)
                {
                    worstStatus = child.GitStatus;
                }
                else if (child.GitStatus == GitFileStatus.Conflicted)
                {
                    worstStatus = GitFileStatus.Conflicted;
                }
            }
            node.GitStatus = worstStatus;
        }
    }

    // ─── File Filter / Search ───────────────────────────────────────

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        foreach (var node in Nodes)
        {
            ApplyFilterRecursive(node, FilterText);
        }
    }

    private bool ApplyFilterRecursive(TreeNode node, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            node.IsVisible = true;
            node.FilterMatchIndices = null;
            foreach (var child in node.Children)
            {
                ApplyFilterRecursive(child, filter);
            }
            return true;
        }

        if (node.IsFile)
        {
            var indices = FuzzyMatch(node.Name, filter);
            if (indices != null)
            {
                node.IsVisible = true;
                node.FilterMatchIndices = indices;
                return true;
            }
            else
            {
                node.IsVisible = false;
                node.FilterMatchIndices = null;
                return false;
            }
        }

        // For folders/projects, show if any child matches
        bool anyChildVisible = false;
        foreach (var child in node.Children)
        {
            if (ApplyFilterRecursive(child, filter))
            {
                anyChildVisible = true;
            }
        }

        node.IsVisible = anyChildVisible || node.IsProject;
        node.FilterMatchIndices = null;

        if (anyChildVisible)
        {
            node.IsExpanded = true;
        }

        return anyChildVisible;
    }

    /// <summary>
    /// Simple fuzzy matching: returns matched character indices, or null if no match.
    /// </summary>
    private static List<int>? FuzzyMatch(string text, string pattern)
    {
        var indices = new List<int>();
        int patternIndex = 0;

        for (int i = 0; i < text.Length && patternIndex < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(pattern[patternIndex]))
            {
                indices.Add(i);
                patternIndex++;
            }
        }

        return patternIndex == pattern.Length ? indices : null;
    }

    [RelayCommand]
    private void ToggleFilter()
    {
        IsFilterVisible = !IsFilterVisible;
        if (!IsFilterVisible)
        {
            FilterText = "";
        }
    }

    // ─── File Operations ────────────────────────────────────────────

    [RelayCommand]
    private void OpenFile()
    {
        if (SelectedNode != null && SelectedNode.IsFile)
        {
            FileOpenRequested?.Invoke(this, SelectedNode.FullPath);
        }
    }

    [RelayCommand]
    private void OpenToSide()
    {
        if (SelectedNode != null && SelectedNode.IsFile)
        {
            FileOpenToSideRequested?.Invoke(this, SelectedNode.FullPath);
        }
    }

    [RelayCommand]
    private void DoubleClick()
    {
        OpenFile();
    }

    // ─── Inline New File ────────────────────────────────────────────

    [RelayCommand]
    private void StartInlineNewFile()
    {
        if (_projectService.CurrentProject == null) return;
        IsCreatingFolder = false;
        IsCreatingNew = true;
        NewItemName = "";
    }

    [RelayCommand]
    private void StartInlineNewFolder()
    {
        if (_projectService.CurrentProject == null) return;
        IsCreatingFolder = true;
        IsCreatingNew = true;
        NewItemName = "";
    }

    [RelayCommand]
    private async Task ConfirmNewItemAsync()
    {
        if (!IsCreatingNew || string.IsNullOrWhiteSpace(NewItemName)) return;
        if (_projectService.CurrentProject == null) return;

        var targetDir = GetTargetDirectory();
        if (targetDir == null) return;

        var name = NewItemName.Trim();

        // Validate name
        var invalidChars = Path.GetInvalidFileNameChars();
        if (name.Any(c => invalidChars.Contains(c)))
        {
            await _dialogService.ShowMessageAsync("Error", "The name contains invalid characters.");
            return;
        }

        if (IsCreatingFolder)
        {
            var folderPath = Path.Combine(targetDir, name);
            if (Directory.Exists(folderPath))
            {
                await _dialogService.ShowMessageAsync("Error", $"Folder '{name}' already exists.");
                return;
            }

            Directory.CreateDirectory(folderPath);
        }
        else
        {
            // Auto-add extension if none
            if (!Path.HasExtension(name))
            {
                name += ".bas";
            }

            var filePath = Path.Combine(targetDir, name);
            if (File.Exists(filePath))
            {
                await _dialogService.ShowMessageAsync("Error", $"File '{name}' already exists.");
                return;
            }

            // Create with template content based on extension
            var content = GetTemplateContent(name);
            await File.WriteAllTextAsync(filePath, content);

            // Add to project
            var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, filePath);
            _projectService.CurrentProject.Items.Add(new ProjectItem
            {
                Include = relativePath,
                ItemType = GetItemTypeForExtension(name)
            });
            await _projectService.SaveProjectAsync();

            CancelNewItem();
            RefreshTree(_projectService.CurrentProject);
            FileOpenRequested?.Invoke(this, filePath);
            return;
        }

        CancelNewItem();
        RefreshTree(_projectService.CurrentProject);
    }

    [RelayCommand]
    private void CancelNewItem()
    {
        IsCreatingNew = false;
        NewItemName = "";
    }

    private static string GetTemplateContent(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(fileName);

        return ext switch
        {
            ".bas" or ".bl" => $"' {fileName}\n' Created: {DateTime.Now:yyyy-MM-dd}\n\nModule {baseName}\n\n    Sub Main()\n        Console.WriteLine(\"Hello World\")\n    End Sub\n\nEnd Module\n",
            ".cs" => $"// {fileName}\nnamespace MyProject\n{{\n    public class {baseName}\n    {{\n    }}\n}}\n",
            ".vb" => $"' {fileName}\nPublic Class {baseName}\n\nEnd Class\n",
            ".json" => "{\n}\n",
            ".xml" => "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n</root>\n",
            ".html" or ".htm" => "<!DOCTYPE html>\n<html>\n<head>\n    <title></title>\n</head>\n<body>\n</body>\n</html>\n",
            ".css" => "/* Styles */\n",
            ".js" or ".ts" => "// " + fileName + "\n",
            _ => $"' {fileName}\n' Created: {DateTime.Now:yyyy-MM-dd}\n\n"
        };
    }

    private static ProjectItemType GetItemTypeForExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".bas" or ".bl" => ProjectItemType.Compile,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" => ProjectItemType.Resource,
            _ => ProjectItemType.Content
        };
    }

    // ─── Legacy dialog-based add (kept for backwards compat) ────────

    [RelayCommand]
    private async Task AddNewFileAsync()
    {
        if (_projectService.CurrentProject == null) return;

        var targetDir = GetTargetDirectory();
        if (targetDir == null) return;

        var fileName = await _dialogService.PromptAsync("New File", "Enter file name:", "NewFile.bas");
        if (string.IsNullOrWhiteSpace(fileName)) return;

        if (!fileName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".bl", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".bas";
        }

        var filePath = Path.Combine(targetDir, fileName);

        if (File.Exists(filePath))
        {
            await _dialogService.ShowMessageAsync("Error", $"File '{fileName}' already exists.");
            return;
        }

        var defaultContent = $"' {fileName}\n' Created: {DateTime.Now:yyyy-MM-dd}\n\n";
        await File.WriteAllTextAsync(filePath, defaultContent);

        var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, filePath);
        _projectService.CurrentProject.Items.Add(new ProjectItem
        {
            Include = relativePath,
            ItemType = ProjectItemType.Compile
        });

        await _projectService.SaveProjectAsync();
        RefreshTree(_projectService.CurrentProject);
        FileOpenRequested?.Invoke(this, filePath);
    }

    [RelayCommand]
    private async Task AddNewFolderAsync()
    {
        if (_projectService.CurrentProject == null) return;

        var targetDir = GetTargetDirectory();
        if (targetDir == null) return;

        var folderName = await _dialogService.PromptAsync("New Folder", "Enter folder name:", "NewFolder");
        if (string.IsNullOrWhiteSpace(folderName)) return;

        var folderPath = Path.Combine(targetDir, folderName);

        if (Directory.Exists(folderPath))
        {
            await _dialogService.ShowMessageAsync("Error", $"Folder '{folderName}' already exists.");
            return;
        }

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

    // ─── Inline Rename (F2) ─────────────────────────────────────────

    [RelayCommand]
    private void StartRename()
    {
        if (SelectedNode == null || SelectedNode.IsProject) return;

        // Pre-select filename without extension for files
        if (SelectedNode.IsFile)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(SelectedNode.Name);
            RenameText = SelectedNode.Name;
            RenameSelectionLength = nameWithoutExt.Length;
        }
        else
        {
            RenameText = SelectedNode.Name;
            RenameSelectionLength = SelectedNode.Name.Length;
        }

        IsRenaming = true;
    }

    [ObservableProperty]
    private int _renameSelectionLength;

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

        // Validate name
        var invalidChars = Path.GetInvalidFileNameChars();
        if (newName.Any(c => invalidChars.Contains(c)))
        {
            await _dialogService.ShowMessageAsync("Error", "The name contains invalid characters.");
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

    // ─── Delete with Confirmation ───────────────────────────────────

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_projectService.CurrentProject == null) return;

        // Support bulk delete from multi-select
        var nodesToDelete = SelectedNodes.Count > 0
            ? SelectedNodes.Where(n => !n.IsProject).ToList()
            : (SelectedNode != null && !SelectedNode.IsProject ? new List<TreeNode> { SelectedNode } : new List<TreeNode>());

        if (nodesToDelete.Count == 0) return;

        var itemNames = string.Join(", ", nodesToDelete.Select(n => $"'{n.Name}'"));
        var message = nodesToDelete.Count == 1
            ? $"Are you sure you want to delete '{nodesToDelete[0].Name}'?\n\nThis will move the item to the Recycle Bin."
            : $"Are you sure you want to delete {nodesToDelete.Count} items?\n\n{itemNames}\n\nThis will move items to the Recycle Bin.";

        var confirmed = await _dialogService.ConfirmAsync("Delete", message);
        if (!confirmed) return;

        try
        {
            foreach (var node in nodesToDelete)
            {
                var path = node.FullPath;

                if (node.IsFile)
                {
                    // Try recycle bin first, fall back to permanent delete
                    try
                    {
                        MoveToRecycleBin(path);
                    }
                    catch
                    {
                        File.Delete(path);
                    }

                    var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, path);
                    var item = _projectService.CurrentProject.Items.FirstOrDefault(i =>
                        string.Equals(i.Include, relativePath, StringComparison.OrdinalIgnoreCase));

                    if (item != null)
                    {
                        _projectService.CurrentProject.Items.Remove(item);
                    }

                    FileDeleted?.Invoke(this, path);
                }
                else if (node.IsFolder)
                {
                    try
                    {
                        MoveToRecycleBin(path);
                    }
                    catch
                    {
                        Directory.Delete(path, true);
                    }

                    var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, path);
                    var itemsToRemove = _projectService.CurrentProject.Items
                        .Where(i => i.Include.StartsWith(relativePath, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    foreach (var item in itemsToRemove)
                    {
                        _projectService.CurrentProject.Items.Remove(item);
                    }
                }
            }

            await _projectService.SaveProjectAsync();
            SelectedNodes.Clear();
            RefreshTree(_projectService.CurrentProject);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to delete: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a file or directory to the Windows Recycle Bin using shell API.
    /// </summary>
    private static void MoveToRecycleBin(string path)
    {
        // Use a simple approach: shell execute with del via PowerShell recycle
        // This is Windows-specific; on other platforms, just delete.
        if (OperatingSystem.IsWindows())
        {
            // Use PowerShell to move to recycle bin
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"Add-Type -AssemblyName Microsoft.VisualBasic; [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteFile('{path.Replace("'", "''")}', 'OnlyErrorDialogs', 'SendToRecycleBin')\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
            if (proc?.ExitCode != 0)
            {
                throw new InvalidOperationException("Recycle bin operation failed");
            }
        }
        else
        {
            // Fallback: permanent delete on non-Windows
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    // ─── Cut / Copy / Paste ─────────────────────────────────────────

    [RelayCommand]
    private void Cut()
    {
        var nodes = GetActionNodes();
        if (nodes.Count == 0) return;

        _clipboardNodes = nodes.ToList();
        _isCutOperation = true;

        foreach (var n in _clipboardNodes)
        {
            n.IsCut = true;
        }
    }

    [RelayCommand]
    private void Copy()
    {
        var nodes = GetActionNodes();
        if (nodes.Count == 0) return;

        // Clear previous cut state
        foreach (var n in _clipboardNodes)
        {
            n.IsCut = false;
        }

        _clipboardNodes = nodes.ToList();
        _isCutOperation = false;
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        if (_clipboardNodes.Count == 0 || _projectService.CurrentProject == null) return;

        var targetDir = GetTargetDirectory();
        if (targetDir == null) return;

        try
        {
            foreach (var node in _clipboardNodes)
            {
                var sourcePath = node.FullPath;
                var destName = node.Name;
                var destPath = Path.Combine(targetDir, destName);

                // Handle name collision
                if (File.Exists(destPath) || Directory.Exists(destPath))
                {
                    destName = GetUniqueName(targetDir, destName);
                    destPath = Path.Combine(targetDir, destName);
                }

                if (_isCutOperation)
                {
                    // Move
                    if (node.IsFile)
                    {
                        File.Move(sourcePath, destPath);

                        // Update project
                        var oldRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, sourcePath);
                        var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, destPath);
                        var item = _projectService.CurrentProject.Items.FirstOrDefault(i =>
                            string.Equals(i.Include, oldRel, StringComparison.OrdinalIgnoreCase));
                        if (item != null) item.Include = newRel;

                        FileRenamed?.Invoke(this, (sourcePath, destPath));
                    }
                    else if (node.IsFolder)
                    {
                        Directory.Move(sourcePath, destPath);

                        var oldRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, sourcePath);
                        var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, destPath);
                        foreach (var item in _projectService.CurrentProject.Items)
                        {
                            if (item.Include.StartsWith(oldRel, StringComparison.OrdinalIgnoreCase))
                            {
                                item.Include = newRel + item.Include.Substring(oldRel.Length);
                            }
                        }
                    }

                    node.IsCut = false;
                }
                else
                {
                    // Copy
                    if (node.IsFile)
                    {
                        File.Copy(sourcePath, destPath);

                        var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, destPath);
                        _projectService.CurrentProject.Items.Add(new ProjectItem
                        {
                            Include = newRel,
                            ItemType = GetItemTypeForExtension(destName)
                        });
                    }
                    else if (node.IsFolder)
                    {
                        CopyDirectory(sourcePath, destPath);

                        // Add all files in copied dir to project
                        foreach (var file in Directory.GetFiles(destPath, "*", SearchOption.AllDirectories))
                        {
                            var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, file);
                            if (!_projectService.CurrentProject.Items.Any(i =>
                                string.Equals(i.Include, newRel, StringComparison.OrdinalIgnoreCase)))
                            {
                                _projectService.CurrentProject.Items.Add(new ProjectItem
                                {
                                    Include = newRel,
                                    ItemType = GetItemTypeForExtension(Path.GetFileName(file))
                                });
                            }
                        }
                    }
                }
            }

            if (_isCutOperation)
            {
                _clipboardNodes.Clear();
                _isCutOperation = false;
            }

            await _projectService.SaveProjectAsync();
            RefreshTree(_projectService.CurrentProject);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Paste failed: {ex.Message}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)));
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    private static string GetUniqueName(string directory, string name)
    {
        var baseName = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        int counter = 1;

        while (true)
        {
            var candidate = $"{baseName} ({counter}){ext}";
            var candidatePath = Path.Combine(directory, candidate);
            if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
            {
                return candidate;
            }
            counter++;
        }
    }

    // ─── Copy Path / Copy Relative Path ─────────────────────────────

    [RelayCommand]
    private void CopyPath()
    {
        if (SelectedNode == null) return;
        ClipboardCopyRequested?.Invoke(this, SelectedNode.FullPath);
    }

    [RelayCommand]
    private void CopyRelativePath()
    {
        if (SelectedNode == null || _projectService.CurrentProject == null) return;
        var relativePath = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, SelectedNode.FullPath);
        ClipboardCopyRequested?.Invoke(this, relativePath);
    }

    // ─── Context Menu Actions ───────────────────────────────────────

    [RelayCommand]
    private void OpenInExplorer()
    {
        if (SelectedNode == null) return;

        var path = SelectedNode.FullPath;
        if (SelectedNode.IsFile)
        {
            // Select the file in Explorer
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        else if (Directory.Exists(path))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    private void OpenInTerminal()
    {
        if (SelectedNode == null) return;

        var path = SelectedNode.IsFile
            ? Path.GetDirectoryName(SelectedNode.FullPath) ?? SelectedNode.FullPath
            : SelectedNode.FullPath;

        OpenInTerminalRequested?.Invoke(this, path);
    }

    [RelayCommand]
    private void FindInFolder()
    {
        if (SelectedNode == null) return;

        var path = SelectedNode.IsFile
            ? Path.GetDirectoryName(SelectedNode.FullPath) ?? SelectedNode.FullPath
            : SelectedNode.FullPath;

        FindInFolderRequested?.Invoke(this, path);
    }

    [RelayCommand]
    private void ShowFileHistory()
    {
        if (SelectedNode == null || !SelectedNode.IsFile) return;
        ShowFileHistoryRequested?.Invoke(this, SelectedNode.FullPath);
    }

    // ─── Collapse / Expand All ──────────────────────────────────────

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in Nodes)
        {
            SetExpandedRecursive(node, false);
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in Nodes)
        {
            SetExpandedRecursive(node, true);
        }
    }

    private static void SetExpandedRecursive(TreeNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        foreach (var child in node.Children)
        {
            SetExpandedRecursive(child, expanded);
        }
    }

    // ─── Multi-Select ───────────────────────────────────────────────

    /// <summary>
    /// Called by the view when a node is clicked with modifier keys.
    /// </summary>
    public void HandleNodeClick(TreeNode node, bool ctrlHeld, bool shiftHeld)
    {
        if (ctrlHeld)
        {
            // Toggle selection
            if (SelectedNodes.Contains(node))
            {
                SelectedNodes.Remove(node);
                node.IsSelected = false;
            }
            else
            {
                SelectedNodes.Add(node);
                node.IsSelected = true;
            }
        }
        else if (shiftHeld && SelectedNode != null)
        {
            // Range selection
            var allNodes = FlattenNodes(Nodes).ToList();
            var startIdx = allNodes.IndexOf(SelectedNode);
            var endIdx = allNodes.IndexOf(node);

            if (startIdx >= 0 && endIdx >= 0)
            {
                var from = Math.Min(startIdx, endIdx);
                var to = Math.Max(startIdx, endIdx);

                SelectedNodes.Clear();
                for (int i = from; i <= to; i++)
                {
                    allNodes[i].IsSelected = true;
                    SelectedNodes.Add(allNodes[i]);
                }
            }
        }
        else
        {
            // Single select - clear multi-select
            foreach (var n in SelectedNodes)
            {
                n.IsSelected = false;
            }
            SelectedNodes.Clear();
            SelectedNode = node;
            node.IsSelected = true;
            SelectedNodes.Add(node);
        }
    }

    private static IEnumerable<TreeNode> FlattenNodes(IEnumerable<TreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            if (node.IsExpanded)
            {
                foreach (var child in FlattenNodes(node.Children))
                {
                    yield return child;
                }
            }
        }
    }

    private List<TreeNode> GetActionNodes()
    {
        if (SelectedNodes.Count > 0)
        {
            return SelectedNodes.Where(n => !n.IsProject).ToList();
        }

        if (SelectedNode != null && !SelectedNode.IsProject)
        {
            return new List<TreeNode> { SelectedNode };
        }

        return new List<TreeNode>();
    }

    // ─── Drag and Drop ──────────────────────────────────────────────

    public void StartDrag(TreeNode node)
    {
        if (node.IsProject) return;
        DragNode = node;
    }

    public void UpdateDropTarget(TreeNode? target, DropPosition position)
    {
        // Clear old highlight
        if (DropTarget != null)
        {
            DropTarget.IsDropTarget = false;
        }

        DropTarget = target;
        DropPosition = position;

        if (target != null)
        {
            target.IsDropTarget = true;
        }
    }

    public async Task CompleteDrop(bool isCopy)
    {
        if (DragNode == null || DropTarget == null || _projectService.CurrentProject == null)
        {
            CancelDrag();
            return;
        }

        var sourcePath = DragNode.FullPath;
        var targetDir = DropTarget.IsFolder || DropTarget.IsProject
            ? DropTarget.FullPath
            : Path.GetDirectoryName(DropTarget.FullPath) ?? "";

        if (DropTarget.IsProject)
        {
            targetDir = _projectService.CurrentProject.ProjectDirectory;
        }

        var destName = DragNode.Name;
        var destPath = Path.Combine(targetDir, destName);

        // Don't drop onto self or into same location
        if (string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
        {
            CancelDrag();
            return;
        }

        // Handle name collision
        if (File.Exists(destPath) || Directory.Exists(destPath))
        {
            destName = GetUniqueName(targetDir, destName);
            destPath = Path.Combine(targetDir, destName);
        }

        try
        {
            if (isCopy)
            {
                if (DragNode.IsFile)
                {
                    File.Copy(sourcePath, destPath);
                    var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, destPath);
                    _projectService.CurrentProject.Items.Add(new ProjectItem
                    {
                        Include = newRel,
                        ItemType = GetItemTypeForExtension(destName)
                    });
                }
                else if (DragNode.IsFolder)
                {
                    CopyDirectory(sourcePath, destPath);
                }
            }
            else
            {
                // Move
                if (DragNode.IsFile)
                {
                    File.Move(sourcePath, destPath);
                    var oldRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, sourcePath);
                    var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, destPath);
                    var item = _projectService.CurrentProject.Items.FirstOrDefault(i =>
                        string.Equals(i.Include, oldRel, StringComparison.OrdinalIgnoreCase));
                    if (item != null) item.Include = newRel;
                    FileRenamed?.Invoke(this, (sourcePath, destPath));
                }
                else if (DragNode.IsFolder)
                {
                    Directory.Move(sourcePath, destPath);
                    var oldRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, sourcePath);
                    var newRel = Path.GetRelativePath(_projectService.CurrentProject.ProjectDirectory, destPath);
                    foreach (var item in _projectService.CurrentProject.Items)
                    {
                        if (item.Include.StartsWith(oldRel, StringComparison.OrdinalIgnoreCase))
                        {
                            item.Include = newRel + item.Include.Substring(oldRel.Length);
                        }
                    }
                }
            }

            await _projectService.SaveProjectAsync();
            RefreshTree(_projectService.CurrentProject);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Move failed: {ex.Message}");
        }
        finally
        {
            CancelDrag();
        }
    }

    public void CancelDrag()
    {
        if (DropTarget != null)
        {
            DropTarget.IsDropTarget = false;
        }
        DragNode = null;
        DropTarget = null;
    }

    // ─── Open Editors ───────────────────────────────────────────────

    /// <summary>
    /// Updates the open editors list. Called by the shell when tabs change.
    /// </summary>
    public void UpdateOpenEditors(IEnumerable<(string FilePath, string Title, bool IsDirty)> editors)
    {
        OpenEditors.Clear();
        foreach (var (filePath, title, isDirty) in editors)
        {
            OpenEditors.Add(new OpenEditorItem
            {
                FilePath = filePath,
                Title = title,
                IsDirty = isDirty
            });
        }
    }

    [RelayCommand]
    private void SwitchToEditor(OpenEditorItem? item)
    {
        if (item != null)
        {
            FileOpenRequested?.Invoke(this, item.FilePath);
        }
    }

    [RelayCommand]
    private void CloseEditor(OpenEditorItem? item)
    {
        if (item != null)
        {
            // Request close via event - the shell handles actual closing
            FileDeleted?.Invoke(this, item.FilePath);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────

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

        return Path.GetDirectoryName(SelectedNode.FullPath);
    }
}

// ─── TreeNode ───────────────────────────────────────────────────────

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

    [ObservableProperty]
    private GitFileStatus _gitStatus = GitFileStatus.Unmodified;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private List<int>? _filterMatchIndices;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isCut;

    [ObservableProperty]
    private bool _isDropTarget;

    public bool IsFile => NodeType is TreeNodeType.SourceFile or TreeNodeType.ContentFile
        or TreeNodeType.Resource or TreeNodeType.File;

    public bool IsFolder => NodeType is TreeNodeType.Folder or TreeNodeType.WorkspaceFolder;
    public bool IsProject => NodeType == TreeNodeType.Project;

    /// <summary>
    /// Gets the git status decoration text (single letter).
    /// </summary>
    public string GitDecorationText => GitStatus switch
    {
        GitFileStatus.Modified => "M",
        GitFileStatus.Added or GitFileStatus.Untracked => "U",
        GitFileStatus.Deleted => "D",
        GitFileStatus.Conflicted => "!",
        GitFileStatus.Renamed => "R",
        _ => ""
    };

    /// <summary>
    /// Gets the git status decoration color hex string.
    /// </summary>
    public string GitDecorationColor => GitStatus switch
    {
        GitFileStatus.Modified => "#E2C08D",
        GitFileStatus.Added or GitFileStatus.Untracked => "#73C991",
        GitFileStatus.Deleted => "#C74E39",
        GitFileStatus.Conflicted => "#E51400",
        GitFileStatus.Renamed => "#73C991",
        _ => "#CCCCCC"
    };

    partial void OnGitStatusChanged(GitFileStatus value)
    {
        OnPropertyChanged(nameof(GitDecorationText));
        OnPropertyChanged(nameof(GitDecorationColor));
    }
}

public enum TreeNodeType
{
    Project,
    Folder,
    SourceFile,
    ContentFile,
    Resource,
    File,
    /// <summary>
    /// A root folder in a multi-root workspace.
    /// </summary>
    WorkspaceFolder
}

public enum DropPosition
{
    None,
    Before,
    Inside,
    After
}

/// <summary>
/// Represents an open editor tab in the Open Editors section.
/// </summary>
public partial class OpenEditorItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private bool _isDirty;
}

// ─── Converters ─────────────────────────────────────────────────────

/// <summary>
/// Converter to get an icon character for a tree node type.
/// Uses simple text glyphs for Avalonia compatibility.
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
                TreeNodeType.Project => "\u25A3",   // filled square with dot
                TreeNodeType.Folder => "\u25B6",    // right triangle (closed folder indicator)
                TreeNodeType.SourceFile => "\u25CB", // circle outline
                TreeNodeType.ContentFile => "\u25A1",// square outline
                TreeNodeType.Resource => "\u25C6",   // diamond
                TreeNodeType.File => "\u25CB",       // circle outline
                _ => "\u25CB"
            };
        }
        return "\u25CB";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns a colored Brush based on file extension for the file type indicator.
/// </summary>
public class FileExtensionIconConverter : IValueConverter
{
    public static readonly FileExtensionIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string fullPath)
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9E9E9E"));

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var hex = ext switch
        {
            ".bas" or ".bl" => "#569CD6",   // Blue for BasicLang
            ".cs" => "#68217A",             // Purple for C#
            ".vb" => "#00539C",             // Dark blue for VB
            ".cpp" or ".c" or ".h" => "#F34B7D", // Red for C/C++
            ".json" => "#F5D02F",           // Yellow for JSON
            ".xml" or ".xaml" or ".axaml" => "#E37933", // Orange for XML
            ".html" or ".htm" => "#E44D26", // Orange-red for HTML
            ".css" => "#264DE4",            // Blue for CSS
            ".js" => "#F0DB4F",             // Yellow for JS
            ".ts" => "#3178C6",             // Blue for TS
            ".md" => "#083FA1",             // Blue for markdown
            ".txt" => "#9E9E9E",            // Gray for text
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" => "#26A69A", // Teal for images
            ".sln" or ".csproj" or ".vbproj" or ".blproj" => "#854CC7", // Purple for project files
            _ => "#9E9E9E"                  // Gray default
        };

        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a hex color string to an Avalonia brush.
/// </summary>
public class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = Avalonia.Media.Color.Parse(hex);
                return new Avalonia.Media.SolidColorBrush(color);
            }
            catch
            {
                return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC"));
            }
        }
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts GitFileStatus to a foreground color for the file name.
/// Modified files show in their status color; normal files show default.
/// </summary>
public class GitStatusForegroundConverter : IValueConverter
{
    public static readonly GitStatusForegroundConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GitFileStatus status)
        {
            var hex = status switch
            {
                GitFileStatus.Modified => "#E2C08D",
                GitFileStatus.Added or GitFileStatus.Untracked => "#73C991",
                GitFileStatus.Deleted => "#C74E39",
                GitFileStatus.Conflicted => "#E51400",
                _ => "#CCCCCC"
            };
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(hex));
        }
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCCCCC"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Returns true if the node is a folder and expanded (for open folder icon).
/// </summary>
public class FolderExpandedIconConverter : IMultiValueConverter
{
    public static readonly FolderExpandedIconConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2 && values[0] is TreeNodeType nodeType && values[1] is bool isExpanded)
        {
            if (nodeType == TreeNodeType.Folder)
            {
                return isExpanded ? "\u25BC" : "\u25B6"; // down triangle / right triangle
            }
            if (nodeType == TreeNodeType.Project)
            {
                return isExpanded ? "\u25BC" : "\u25B6";
            }
        }
        return null;
    }
}
