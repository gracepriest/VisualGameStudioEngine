using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// ViewModel for the Call Hierarchy panel showing callers/callees
/// </summary>
public partial class CallHierarchyViewModel : Tool
{
    private readonly ILanguageService _languageService;

    [ObservableProperty]
    private ObservableCollection<CallHierarchyItem> _rootItems = new();

    [ObservableProperty]
    private CallHierarchyItem? _selectedItem;

    [ObservableProperty]
    private string _currentMethodName = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private CallHierarchyViewMode _viewMode = CallHierarchyViewMode.IncomingCalls;

    public event EventHandler<CallHierarchyNavigationEventArgs>? NavigationRequested;

    public CallHierarchyViewModel(ILanguageService languageService)
    {
        _languageService = languageService;
        Id = "CallHierarchy";
        Title = "Call Hierarchy";
    }

    /// <summary>
    /// Show call hierarchy for a method at the specified location
    /// </summary>
    [RelayCommand]
    public async Task ShowHierarchyAsync(CallHierarchyRequest request)
    {
        if (string.IsNullOrEmpty(request.FilePath))
            return;

        IsLoading = true;
        try
        {
            RootItems.Clear();
            CurrentMethodName = request.MethodName ?? "Unknown";

            var rootItem = new CallHierarchyItem
            {
                Name = CurrentMethodName,
                Kind = HierarchyCallableKind.Method,
                FilePath = request.FilePath,
                Line = request.Line,
                Column = request.Column,
                IsExpanded = true
            };

            // Load hierarchy based on view mode
            if (ViewMode == CallHierarchyViewMode.IncomingCalls)
            {
                await LoadIncomingCallsAsync(rootItem);
            }
            else
            {
                await LoadOutgoingCallsAsync(rootItem);
            }

            RootItems.Add(rootItem);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SwitchToIncoming()
    {
        ViewMode = CallHierarchyViewMode.IncomingCalls;
        RefreshHierarchy();
    }

    [RelayCommand]
    private void SwitchToOutgoing()
    {
        ViewMode = CallHierarchyViewMode.OutgoingCalls;
        RefreshHierarchy();
    }

    private void RefreshHierarchy()
    {
        if (!string.IsNullOrEmpty(CurrentMethodName) && RootItems.Count > 0)
        {
            var root = RootItems[0];
            ShowHierarchyCommand.Execute(new CallHierarchyRequest
            {
                FilePath = root.FilePath,
                Line = root.Line,
                Column = root.Column,
                MethodName = CurrentMethodName
            });
        }
    }

    [RelayCommand]
    private void NavigateToItem(CallHierarchyItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.FilePath))
            return;

        NavigationRequested?.Invoke(this, new CallHierarchyNavigationEventArgs
        {
            FilePath = item.FilePath,
            Line = item.Line,
            Column = item.Column
        });
    }

    [RelayCommand]
    private void NavigateToCallSite(CallSiteInfo? callSite)
    {
        if (callSite == null || string.IsNullOrEmpty(callSite.FilePath))
            return;

        NavigationRequested?.Invoke(this, new CallHierarchyNavigationEventArgs
        {
            FilePath = callSite.FilePath,
            Line = callSite.Line,
            Column = callSite.Column
        });
    }

    [RelayCommand]
    private async Task ExpandItemAsync(CallHierarchyItem? item)
    {
        if (item == null || item.IsLoaded)
            return;

        item.IsLoading = true;
        try
        {
            if (ViewMode == CallHierarchyViewMode.IncomingCalls)
            {
                await LoadIncomingCallsAsync(item);
            }
            else
            {
                await LoadOutgoingCallsAsync(item);
            }
            item.IsLoaded = true;
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        RootItems.Clear();
        CurrentMethodName = "";
    }

    partial void OnSelectedItemChanged(CallHierarchyItem? value)
    {
        // Could trigger preview or other actions
    }

    private async Task LoadIncomingCallsAsync(CallHierarchyItem item)
    {
        // Get incoming calls (callers) from language service
        var callers = await _languageService.GetIncomingCallsAsync(
            item.FilePath, item.Line, item.Column);

        foreach (var caller in callers)
        {
            var child = new CallHierarchyItem
            {
                Name = caller.Name,
                Kind = caller.Kind,
                Detail = caller.Detail,
                FilePath = caller.FilePath,
                Line = caller.Line,
                Column = caller.Column,
                CallSites = new ObservableCollection<CallSiteInfo>(
                    caller.CallSites.Select(cs => new CallSiteInfo
                    {
                        FilePath = cs.FilePath,
                        Line = cs.Line,
                        Column = cs.Column,
                        Preview = cs.Preview
                    }))
            };

            item.Children.Add(child);
        }
    }

    private async Task LoadOutgoingCallsAsync(CallHierarchyItem item)
    {
        // Get outgoing calls (callees) from language service
        var callees = await _languageService.GetOutgoingCallsAsync(
            item.FilePath, item.Line, item.Column);

        foreach (var callee in callees)
        {
            var child = new CallHierarchyItem
            {
                Name = callee.Name,
                Kind = callee.Kind,
                Detail = callee.Detail,
                FilePath = callee.FilePath,
                Line = callee.Line,
                Column = callee.Column,
                CallSites = new ObservableCollection<CallSiteInfo>(
                    callee.CallSites.Select(cs => new CallSiteInfo
                    {
                        FilePath = cs.FilePath,
                        Line = cs.Line,
                        Column = cs.Column,
                        Preview = cs.Preview
                    }))
            };

            item.Children.Add(child);
        }
    }
}

/// <summary>
/// Represents a method/function in the call hierarchy tree
/// </summary>
public partial class CallHierarchyItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private HierarchyCallableKind _kind = HierarchyCallableKind.Method;

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
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private ObservableCollection<CallHierarchyItem> _children = new();

    [ObservableProperty]
    private ObservableCollection<CallSiteInfo> _callSites = new();

    public string DisplayText => string.IsNullOrEmpty(Detail) ? Name : $"{Name} {Detail}";
    public string LocationText => $"{Path.GetFileName(FilePath)}:{Line}";
}

/// <summary>
/// Represents a specific call site location
/// </summary>
public class CallSiteInfo
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? Preview { get; set; }

    public string DisplayText => $"Line {Line}: {Preview ?? ""}";
}

/// <summary>
/// Request to show call hierarchy
/// </summary>
public class CallHierarchyRequest
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string? MethodName { get; set; }
}

/// <summary>
/// Event args for navigation requests
/// </summary>
public class CallHierarchyNavigationEventArgs : EventArgs
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
}

/// <summary>
/// Type of call hierarchy view
/// </summary>
public enum CallHierarchyViewMode
{
    IncomingCalls,  // Show callers (who calls this method)
    OutgoingCalls   // Show callees (what this method calls)
}

