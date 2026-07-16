using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// ViewModel for the Type Hierarchy panel showing class inheritance
/// </summary>
public partial class TypeHierarchyViewModel : Tool
{
    private readonly ILanguageServiceRegistry _languageServices;

    [ObservableProperty]
    private ObservableCollection<TypeHierarchyItem> _rootItems = new();

    [ObservableProperty]
    private TypeHierarchyItem? _selectedItem;

    [ObservableProperty]
    private string _currentTypeName = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private HierarchyViewMode _viewMode = HierarchyViewMode.Supertypes;

    public event EventHandler<TypeHierarchyNavigationEventArgs>? NavigationRequested;

    public TypeHierarchyViewModel(ILanguageServiceRegistry languageServices)
    {
        _languageServices = languageServices;
        Id = "TypeHierarchy";
        Title = "Type Hierarchy";
    }

    /// <summary>
    /// Show type hierarchy for a type at the specified location
    /// </summary>
    [RelayCommand]
    public async Task ShowHierarchyAsync(TypeHierarchyRequest request)
    {
        if (string.IsNullOrEmpty(request.FilePath))
            return;

        IsLoading = true;
        try
        {
            RootItems.Clear();
            CurrentTypeName = request.TypeName ?? "Unknown";

            var rootItem = new TypeHierarchyItem
            {
                Name = CurrentTypeName,
                Kind = HierarchyTypeKind.Class,
                FilePath = request.FilePath,
                Line = request.Line,
                Column = request.Column,
                IsExpanded = true
            };

            // Load hierarchy based on view mode
            if (ViewMode == HierarchyViewMode.Supertypes)
            {
                await LoadSupertypesAsync(rootItem);
            }
            else
            {
                await LoadSubtypesAsync(rootItem);
            }

            RootItems.Add(rootItem);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SwitchToSupertypes()
    {
        ViewMode = HierarchyViewMode.Supertypes;
        if (!string.IsNullOrEmpty(CurrentTypeName) && SelectedItem != null)
        {
            ShowHierarchyCommand.Execute(new TypeHierarchyRequest
            {
                FilePath = SelectedItem.FilePath,
                Line = SelectedItem.Line,
                Column = SelectedItem.Column,
                TypeName = CurrentTypeName
            });
        }
    }

    [RelayCommand]
    private void SwitchToSubtypes()
    {
        ViewMode = HierarchyViewMode.Subtypes;
        if (!string.IsNullOrEmpty(CurrentTypeName) && SelectedItem != null)
        {
            ShowHierarchyCommand.Execute(new TypeHierarchyRequest
            {
                FilePath = SelectedItem.FilePath,
                Line = SelectedItem.Line,
                Column = SelectedItem.Column,
                TypeName = CurrentTypeName
            });
        }
    }

    [RelayCommand]
    private void NavigateToItem(TypeHierarchyItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.FilePath))
            return;

        NavigationRequested?.Invoke(this, new TypeHierarchyNavigationEventArgs
        {
            FilePath = item.FilePath,
            Line = item.Line,
            Column = item.Column
        });
    }

    [RelayCommand]
    private void Clear()
    {
        RootItems.Clear();
        CurrentTypeName = "";
    }

    partial void OnSelectedItemChanged(TypeHierarchyItem? value)
    {
        // Could trigger navigation or other actions
    }

    private Task LoadSupertypesAsync(TypeHierarchyItem item)
        => LoadSupertypesAsync(item, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

    private async Task LoadSupertypesAsync(TypeHierarchyItem item, HashSet<string> visited, int depth)
    {
        // Guard against cyclic supertype chains from the language server
        // (A : B, B : A) — unbounded recursion here is an uncatchable
        // StackOverflowException that kills the whole IDE.
        const int MaxDepth = 32;
        if (depth >= MaxDepth) return;
        if (!visited.Add($"{item.FilePath}|{item.Line}|{item.Column}|{item.Name}")) return;

        // Route by the node's own file — a supertype can live in a different file, and in a mixed
        // project each node is served by whichever language server owns it.
        var languageService = _languageServices.GetFor(item.FilePath);
        if (languageService is not { IsConnected: true }) return;

        // Get supertypes from language service
        var supertypes = await languageService.GetSupertypesAsync(
            item.FilePath, item.Line, item.Column);

        foreach (var supertype in supertypes)
        {
            var child = new TypeHierarchyItem
            {
                Name = supertype.Name,
                Kind = supertype.Kind,
                Detail = supertype.Detail,
                FilePath = supertype.FilePath,
                Line = supertype.Line,
                Column = supertype.Column,
                IsExpanded = true
            };

            // Recursively load supertypes
            await LoadSupertypesAsync(child, visited, depth + 1);

            item.Children.Add(child);
        }
    }

    private async Task LoadSubtypesAsync(TypeHierarchyItem item)
    {
        // Route by the node's own file (see LoadSupertypesAsync).
        var languageService = _languageServices.GetFor(item.FilePath);
        if (languageService is not { IsConnected: true }) return;

        // Get subtypes from language service
        var subtypes = await languageService.GetSubtypesAsync(
            item.FilePath, item.Line, item.Column);

        foreach (var subtype in subtypes)
        {
            var child = new TypeHierarchyItem
            {
                Name = subtype.Name,
                Kind = subtype.Kind,
                Detail = subtype.Detail,
                FilePath = subtype.FilePath,
                Line = subtype.Line,
                Column = subtype.Column
            };

            item.Children.Add(child);
        }
    }
}

/// <summary>
/// Represents a type in the hierarchy tree
/// </summary>
public partial class TypeHierarchyItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private HierarchyTypeKind _kind = HierarchyTypeKind.Class;

    [ObservableProperty]
    private string? _detail;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _line;

    [ObservableProperty]
    private int _column;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<TypeHierarchyItem> _children = new();

    public string DisplayText => string.IsNullOrEmpty(Detail) ? Name : $"{Name} : {Detail}";
}

/// <summary>
/// Request to show type hierarchy
/// </summary>
public class TypeHierarchyRequest
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? TypeName { get; set; }
}

/// <summary>
/// Event args for navigation requests
/// </summary>
public class TypeHierarchyNavigationEventArgs : EventArgs
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

/// <summary>
/// Type of hierarchy view
/// </summary>
public enum HierarchyViewMode
{
    Supertypes,  // Show base classes/interfaces
    Subtypes     // Show derived classes
}

