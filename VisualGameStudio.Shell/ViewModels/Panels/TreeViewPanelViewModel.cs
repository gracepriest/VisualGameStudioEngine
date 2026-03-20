using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// ViewModel for an extension-contributed tree view panel.
/// Each extension that calls vscode.window.createTreeView() gets one of these.
/// </summary>
public partial class TreeViewPanelViewModel : ViewModelBase
{
    private readonly Func<string, string?, CancellationToken, Task<JsonElement?>>? _getChildrenFunc;
    private readonly Func<string, string, CancellationToken, Task<JsonElement?>>? _getTreeItemFunc;
    private readonly Func<string, object?[]?, Task<object?>>? _executeCommandFunc;

    [ObservableProperty]
    private string _viewId = "";

    [ObservableProperty]
    private string _title = "";

    [ObservableProperty]
    private string _extensionId = "";

    [ObservableProperty]
    private ObservableCollection<TreeViewItemViewModel> _rootItems = new();

    [ObservableProperty]
    private TreeViewItemViewModel? _selectedItem;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Creates a TreeViewPanelViewModel with delegates for RPC calls back to the extension host.
    /// </summary>
    public TreeViewPanelViewModel(
        string viewId,
        string title,
        string extensionId,
        Func<string, string?, CancellationToken, Task<JsonElement?>>? getChildrenFunc,
        Func<string, string, CancellationToken, Task<JsonElement?>>? getTreeItemFunc,
        Func<string, object?[]?, Task<object?>>? executeCommandFunc)
    {
        _getChildrenFunc = getChildrenFunc;
        _getTreeItemFunc = getTreeItemFunc;
        _executeCommandFunc = executeCommandFunc;
        ViewId = viewId;
        Title = title;
        ExtensionId = extensionId;
    }

    /// <summary>
    /// Parameterless constructor for design-time / fallback.
    /// </summary>
    public TreeViewPanelViewModel()
    {
    }

    /// <summary>
    /// Loads the root-level children from the extension's tree data provider.
    /// </summary>
    public async Task LoadRootItemsAsync(CancellationToken cancellationToken = default)
    {
        if (_getChildrenFunc == null) return;

        IsLoading = true;
        try
        {
            var result = await _getChildrenFunc(ViewId, null, cancellationToken);
            RootItems.Clear();

            if (result == null) return;

            ParseAndAddItems(result.Value, RootItems);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TreeViewPanel] Failed to load root items for {ViewId}: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads children for a specific parent item.
    /// </summary>
    public async Task LoadChildrenAsync(TreeViewItemViewModel parent, CancellationToken cancellationToken = default)
    {
        if (_getChildrenFunc == null || parent.ChildrenLoaded) return;

        parent.IsLoadingChildren = true;
        try
        {
            var result = await _getChildrenFunc(ViewId, parent.Id, cancellationToken);
            parent.Children.Clear();

            if (result != null)
            {
                ParseAndAddItems(result.Value, parent.Children);
            }

            parent.ChildrenLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TreeViewPanel] Failed to load children for {parent.Id}: {ex.Message}");
        }
        finally
        {
            parent.IsLoadingChildren = false;
        }
    }

    /// <summary>
    /// Refreshes the entire tree or a specific subtree.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        // Reset all loaded flags and reload from root
        RootItems.Clear();
        await LoadRootItemsAsync();
    }

    /// <summary>
    /// Refreshes a specific element and its subtree, or the whole tree if element is null.
    /// </summary>
    public async Task RefreshElementAsync(string? element)
    {
        if (string.IsNullOrEmpty(element))
        {
            await RefreshAsync();
            return;
        }

        // Find the item and reload its children
        var item = FindItemById(RootItems, element);
        if (item != null)
        {
            item.ChildrenLoaded = false;
            item.Children.Clear();
            if (item.HasChildren)
            {
                // Add placeholder so it shows as expandable
                item.Children.Add(new TreeViewItemViewModel { Label = "Loading..." });
            }
            if (item.IsExpanded)
            {
                await LoadChildrenAsync(item);
            }
        }
    }

    /// <summary>
    /// Handles item click - executes the item's command if present.
    /// </summary>
    [RelayCommand]
    private async Task ItemClicked(TreeViewItemViewModel? item)
    {
        if (item?.Command == null || _executeCommandFunc == null) return;

        try
        {
            await _executeCommandFunc(item.Command, item.CommandArguments);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TreeViewPanel] Command execution failed: {ex.Message}");
        }
    }

    private void ParseAndAddItems(JsonElement element, ObservableCollection<TreeViewItemViewModel> target)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var itemElement in element.EnumerateArray())
            {
                var item = ParseTreeItem(itemElement);
                if (item != null)
                {
                    target.Add(item);
                }
            }
        }
    }

    private static TreeViewItemViewModel? ParseTreeItem(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;

        var item = new TreeViewItemViewModel();

        if (element.TryGetProperty("id", out var idProp))
            item.Id = idProp.GetString() ?? "";
        if (element.TryGetProperty("label", out var labelProp))
            item.Label = labelProp.GetString() ?? "";
        if (element.TryGetProperty("description", out var descProp))
            item.Description = descProp.GetString();
        if (element.TryGetProperty("tooltip", out var tooltipProp))
            item.Tooltip = tooltipProp.GetString();
        if (element.TryGetProperty("iconPath", out var iconProp))
            item.IconPath = iconProp.GetString();
        if (element.TryGetProperty("collapsibleState", out var collapseProp))
        {
            item.CollapsibleState = collapseProp.GetInt32();
            item.HasChildren = item.CollapsibleState > 0; // 1=Collapsed, 2=Expanded
            if (item.CollapsibleState == 2)
                item.IsExpanded = true;
        }
        if (element.TryGetProperty("command", out var cmdProp) && cmdProp.ValueKind == JsonValueKind.Object)
        {
            if (cmdProp.TryGetProperty("command", out var cmdIdProp))
                item.Command = cmdIdProp.GetString();
            if (cmdProp.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Array)
            {
                var args = new List<object?>();
                foreach (var arg in argsProp.EnumerateArray())
                {
                    args.Add(arg.ValueKind switch
                    {
                        JsonValueKind.String => arg.GetString(),
                        JsonValueKind.Number => arg.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => arg.ToString()
                    });
                }
                item.CommandArguments = args.ToArray();
            }
        }

        // If it has children, add a placeholder for lazy loading
        if (item.HasChildren)
        {
            item.Children.Add(new TreeViewItemViewModel { Label = "Loading..." });
        }

        // Determine icon text from iconPath or default based on collapsible state
        item.IconKind = GetIconKind(item);

        return item;
    }

    private static string GetIconKind(TreeViewItemViewModel item)
    {
        if (!string.IsNullOrEmpty(item.IconPath))
        {
            // Map common icon names to simple text icons
            var iconName = item.IconPath.ToLowerInvariant();
            if (iconName.Contains("folder")) return "D";
            if (iconName.Contains("file")) return "F";
            if (iconName.Contains("class")) return "C";
            if (iconName.Contains("method") || iconName.Contains("function")) return "m";
            if (iconName.Contains("property")) return "P";
            if (iconName.Contains("variable") || iconName.Contains("field")) return "v";
            if (iconName.Contains("interface")) return "I";
            if (iconName.Contains("enum")) return "E";
            if (iconName.Contains("warning")) return "!";
            if (iconName.Contains("error")) return "x";
            if (iconName.Contains("info")) return "i";
        }

        // Default icons based on whether the item is a container or leaf
        return item.HasChildren ? "D" : "F";
    }

    private static TreeViewItemViewModel? FindItemById(ObservableCollection<TreeViewItemViewModel> items, string id)
    {
        foreach (var item in items)
        {
            if (item.Id == id) return item;
            var found = FindItemById(item.Children, id);
            if (found != null) return found;
        }
        return null;
    }
}

/// <summary>
/// ViewModel for a single item in an extension-contributed tree view.
/// </summary>
public partial class TreeViewItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string? _tooltip;

    [ObservableProperty]
    private string _iconKind = "F";

    [ObservableProperty]
    private string? _iconPath;

    [ObservableProperty]
    private int _collapsibleState; // 0=None, 1=Collapsed, 2=Expanded

    [ObservableProperty]
    private string? _command;

    [ObservableProperty]
    private object?[]? _commandArguments;

    [ObservableProperty]
    private bool _hasChildren;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoadingChildren;

    [ObservableProperty]
    private ObservableCollection<TreeViewItemViewModel> _children = new();

    /// <summary>
    /// Whether children have already been fetched from the extension host.
    /// </summary>
    public bool ChildrenLoaded { get; set; }
}
