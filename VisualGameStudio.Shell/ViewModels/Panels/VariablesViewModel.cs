using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class VariablesViewModel : Tool
{
    private readonly IDebugService _debugService;
    private int _currentFrameId;

    [ObservableProperty]
    private ObservableCollection<VariableTreeItem> _variableTree = new();

    [ObservableProperty]
    private ObservableCollection<VariableItem> _variables = new();

    [ObservableProperty]
    private ObservableCollection<WatchItem> _watchExpressions = new();

    [ObservableProperty]
    private string _newWatchExpression = "";

    [ObservableProperty]
    private VariableTreeItem? _selectedVariable;

    public VariablesViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        Id = "Variables";
        Title = "Variables";

        _debugService.Stopped += OnDebugStopped;
        _debugService.StateChanged += OnDebugStateChanged;
    }

    public IDebugService DebugService => _debugService;

    private async void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await RefreshVariablesAsync();
        });
    }

    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (e.NewState == DebugState.Stopped || e.NewState == DebugState.NotStarted)
            {
                Variables.Clear();
                VariableTree.Clear();
            }
        });
    }

    public async Task SetFrameAsync(int frameId)
    {
        _currentFrameId = frameId;
        await RefreshVariablesAsync();
    }

    [RelayCommand]
    private async Task RefreshVariablesAsync()
    {
        Variables.Clear();
        VariableTree.Clear();

        if (_debugService.State != DebugState.Paused) return;

        // Get stack frames to get current frame
        var frames = await _debugService.GetStackTraceAsync();
        if (!frames.Any()) return;

        var frameId = _currentFrameId > 0 ? _currentFrameId : frames[0].Id;

        // Get scopes for the frame
        var scopes = await _debugService.GetScopesAsync(frameId);

        foreach (var scope in scopes)
        {
            // Create scope node for tree
            var scopeNode = new VariableTreeItem(_debugService)
            {
                Name = scope.Name,
                Value = "",
                Type = "",
                IsScope = true,
                VariablesReference = scope.VariablesReference,
                IsExpanded = true // Auto-expand scope nodes
            };
            VariableTree.Add(scopeNode);

            // Add scope header to flat list
            Variables.Add(new VariableItem
            {
                Name = $"▼ {scope.Name}",
                Value = "",
                Type = "",
                IsScope = true
            });

            // Get variables in this scope
            var vars = await _debugService.GetVariablesAsync(scope.VariablesReference);
            foreach (var v in vars)
            {
                // Add to tree
                var varNode = new VariableTreeItem(_debugService)
                {
                    Name = v.Name,
                    Value = v.Value,
                    Type = v.Type ?? "",
                    VariablesReference = v.VariablesReference,
                    IsExpandable = v.VariablesReference > 0
                };
                scopeNode.Children.Add(varNode);

                // Add to flat list
                Variables.Add(new VariableItem
                {
                    Name = "    " + v.Name,
                    Value = v.Value,
                    Type = v.Type ?? "",
                    VariablesReference = v.VariablesReference,
                    IsExpandable = v.VariablesReference > 0
                });
            }
        }

        // Evaluate watch expressions
        await EvaluateWatchExpressionsAsync(frameId);
    }

    public async Task ExpandVariableAsync(VariableTreeItem item)
    {
        if (item.VariablesReference <= 0 || item.IsLoaded) return;

        item.IsLoading = true;
        try
        {
            var children = await _debugService.GetVariablesAsync(item.VariablesReference);
            item.Children.Clear();

            foreach (var child in children)
            {
                item.Children.Add(new VariableTreeItem(_debugService)
                {
                    Name = child.Name,
                    Value = child.Value,
                    Type = child.Type ?? "",
                    VariablesReference = child.VariablesReference,
                    IsExpandable = child.VariablesReference > 0
                });
            }

            item.IsLoaded = true;
        }
        finally
        {
            item.IsLoading = false;
        }
    }

    private async Task EvaluateWatchExpressionsAsync(int frameId)
    {
        foreach (var watch in WatchExpressions)
        {
            try
            {
                var result = await _debugService.EvaluateAsync(watch.Expression, frameId);
                watch.Value = result.Result;
                watch.Type = result.Type ?? "";
            }
            catch (Exception ex)
            {
                watch.Value = $"<error: {ex.Message}>";
                watch.Type = "";
            }
        }
    }

    [RelayCommand]
    private async Task AddWatchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewWatchExpression)) return;

        var watch = new WatchItem
        {
            Expression = NewWatchExpression,
            Value = "<not evaluated>",
            Type = ""
        };

        WatchExpressions.Add(watch);
        NewWatchExpression = "";

        if (_debugService.State == DebugState.Paused)
        {
            var frames = await _debugService.GetStackTraceAsync();
            if (frames.Any())
            {
                var result = await _debugService.EvaluateAsync(watch.Expression, frames[0].Id);
                watch.Value = result.Result;
                watch.Type = result.Type ?? "";
            }
        }
    }

    [RelayCommand]
    private void RemoveWatch(WatchItem watch)
    {
        WatchExpressions.Remove(watch);
    }
}

public partial class VariableItem : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _type = "";

    public int VariablesReference { get; set; }
    public bool IsExpandable { get; set; }
    public bool IsScope { get; set; }
}

public partial class WatchItem : ObservableObject
{
    [ObservableProperty]
    private string _expression = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _type = "";
}

/// <summary>
/// Tree node for hierarchical variable display
/// </summary>
public partial class VariableTreeItem : ObservableObject
{
    private readonly IDebugService _debugService;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _type = "";

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    public int VariablesReference { get; set; }
    public bool IsExpandable { get; set; }
    public bool IsScope { get; set; }

    public ObservableCollection<VariableTreeItem> Children { get; } = new();

    public VariableTreeItem(IDebugService debugService)
    {
        _debugService = debugService;
    }

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && !IsLoaded && IsExpandable && VariablesReference > 0)
        {
            _ = LoadChildrenAsync();
        }
    }

    private async Task LoadChildrenAsync()
    {
        if (IsLoaded || IsLoading) return;

        IsLoading = true;
        try
        {
            var children = await _debugService.GetVariablesAsync(VariablesReference);
            Children.Clear();

            foreach (var child in children)
            {
                Children.Add(new VariableTreeItem(_debugService)
                {
                    Name = child.Name,
                    Value = child.Value,
                    Type = child.Type ?? "",
                    VariablesReference = child.VariablesReference,
                    IsExpandable = child.VariablesReference > 0
                });
            }

            IsLoaded = true;
        }
        catch
        {
            // Failed to load children
            Children.Clear();
            Children.Add(new VariableTreeItem(_debugService) { Name = "<error loading>", Value = "", Type = "" });
        }
        finally
        {
            IsLoading = false;
        }
    }

    public string DisplayName => IsScope ? $"📁 {Name}" : (IsExpandable ? $"📦 {Name}" : Name);
}
