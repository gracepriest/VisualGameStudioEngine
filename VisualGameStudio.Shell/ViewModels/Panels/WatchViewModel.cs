using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input.Platform;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class WatchViewModel : ViewModelBase, IDisposable
{
    private readonly IDebugService _debugService;

    [ObservableProperty]
    private ObservableCollection<WatchPanelItem> _watchItems = new();

    [ObservableProperty]
    private WatchPanelItem? _selectedItem;

    [ObservableProperty]
    private string _newExpression = "";

    public WatchViewModel(IDebugService debugService)
    {
        _debugService = debugService;
        _debugService.StateChanged += OnDebugStateChanged;
        _debugService.Stopped += OnDebugStopped;

        // Add empty item for new entries
        WatchItems.Add(new WatchPanelItem { Expression = "", IsEditable = true });
    }

    public void Dispose()
    {
        _debugService.StateChanged -= OnDebugStateChanged;
        _debugService.Stopped -= OnDebugStopped;
    }

    private async void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (e.NewState == DebugState.Paused)
                {
                    await RefreshAllAsync();
                }
                else if (e.NewState == DebugState.Stopped)
                {
                    ClearValues();
                }
                else if (e.NewState == DebugState.NotStarted)
                {
                    // Session fully ended — show "Not available"
                    MarkAllNotAvailable();
                }
            });
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    private async void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await RefreshAllAsync();
            });
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    [RelayCommand]
    private async Task AddWatchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewExpression)) return;

        var item = new WatchPanelItem
        {
            Expression = NewExpression.Trim(),
            IsEditable = false
        };

        // Insert before the empty editable item
        var insertIndex = WatchItems.Count > 0 ? WatchItems.Count - 1 : 0;
        WatchItems.Insert(insertIndex, item);

        NewExpression = "";

        // Evaluate if debugging
        if (_debugService.IsDebugging)
        {
            await EvaluateItemAsync(item);
        }
        else
        {
            item.Value = "Not available";
            item.Type = "";
            item.HasError = true;
        }
    }

    [RelayCommand]
    private async Task AddExpressionAsync(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) return;

        // Check if already exists
        if (WatchItems.Any(w => w.Expression == expression && !w.IsEditable))
            return;

        var item = new WatchPanelItem
        {
            Expression = expression.Trim(),
            IsEditable = false
        };

        var insertIndex = WatchItems.Count > 0 ? WatchItems.Count - 1 : 0;
        WatchItems.Insert(insertIndex, item);

        if (_debugService.IsDebugging)
        {
            await EvaluateItemAsync(item);
        }
        else
        {
            item.Value = "Not available";
            item.Type = "";
            item.HasError = true;
        }
    }

    [RelayCommand]
    private void RemoveWatch(WatchPanelItem? item)
    {
        if (item == null || item.IsEditable) return;
        WatchItems.Remove(item);
    }

    [RelayCommand]
    private void RemoveAll()
    {
        var editableItem = WatchItems.FirstOrDefault(w => w.IsEditable);
        WatchItems.Clear();
        if (editableItem != null)
        {
            editableItem.Expression = "";
            WatchItems.Add(editableItem);
        }
        else
        {
            WatchItems.Add(new WatchPanelItem { Expression = "", IsEditable = true });
        }
    }

    [RelayCommand]
    private async Task CopyValueAsync()
    {
        if (SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.Value))
        {
            try
            {
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
                var clipboard = topLevel?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(SelectedItem.Value);
                }
            }
            catch (Exception) { }
        }
    }

    [RelayCommand]
    private async Task CopyExpressionAsync()
    {
        if (SelectedItem != null && !string.IsNullOrEmpty(SelectedItem.Expression))
        {
            try
            {
                var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;
                var clipboard = topLevel?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(SelectedItem.Expression);
                }
            }
            catch (Exception) { }
        }
    }

    [RelayCommand]
    private void EditExpression()
    {
        if (SelectedItem != null && !SelectedItem.IsEditable)
        {
            SelectedItem.IsEditing = true;
        }
    }

    /// <summary>
    /// Called when the user finishes editing a watch expression inline.
    /// If the expression is empty, remove the watch. Otherwise, re-evaluate.
    /// </summary>
    [RelayCommand]
    private async Task CommitEditAsync(WatchPanelItem? item)
    {
        if (item == null) return;

        item.IsEditing = false;

        if (string.IsNullOrWhiteSpace(item.Expression))
        {
            // Empty expression on a non-placeholder item: remove it
            if (!item.IsEditable)
            {
                WatchItems.Remove(item);
            }
            return;
        }

        // If this was the placeholder row, convert it to a real watch and add a new placeholder
        if (item.IsEditable)
        {
            item.IsEditable = false;
            WatchItems.Add(new WatchPanelItem { Expression = "", IsEditable = true });
        }

        // Evaluate the expression
        await EvaluateItemAsync(item);
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (!_debugService.IsDebugging)
        {
            MarkAllNotAvailable();
            return;
        }

        foreach (var item in WatchItems.Where(w => !w.IsEditable && !string.IsNullOrEmpty(w.Expression)))
        {
            await EvaluateItemAsync(item);
        }
    }

    [RelayCommand]
    private async Task EvaluateAsync(WatchPanelItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.Expression)) return;

        if (item.IsEditable)
        {
            // Convert editable item to regular watch
            item.IsEditable = false;
            WatchItems.Add(new WatchPanelItem { Expression = "", IsEditable = true });
        }

        await EvaluateItemAsync(item);
    }

    private async Task EvaluateItemAsync(WatchPanelItem item)
    {
        if (!_debugService.IsDebugging)
        {
            item.Value = "Not available";
            item.Type = "";
            item.HasError = true;
            return;
        }

        try
        {
            var result = await _debugService.EvaluateAsync(item.Expression, context: "watch");
            if (result != null)
            {
                var value = result.Result ?? "";
                // Detect error responses from the debug adapter
                if (value.StartsWith("<error") || value.StartsWith("<'") || value.StartsWith("<member") || value.StartsWith("<no frame"))
                {
                    item.Value = value;
                    item.Type = result.Type ?? "";
                    item.HasError = true;
                }
                else
                {
                    item.Value = string.IsNullOrEmpty(value) ? "<null>" : value;
                    item.Type = result.Type ?? "";
                    item.HasError = false;

                    // Store variablesReference for expandable values
                    item.VariablesReference = result.VariablesReference;
                }
            }
            else
            {
                item.Value = "Error: no response";
                item.Type = "";
                item.HasError = true;
            }
        }
        catch (Exception ex)
        {
            item.Value = $"Error: {ex.Message}";
            item.Type = "";
            item.HasError = true;
        }
    }

    private void ClearValues()
    {
        foreach (var item in WatchItems.Where(w => !w.IsEditable))
        {
            item.Value = "";
            item.Type = "";
            item.HasError = false;
        }
    }

    private void MarkAllNotAvailable()
    {
        foreach (var item in WatchItems.Where(w => !w.IsEditable && !string.IsNullOrEmpty(w.Expression)))
        {
            item.Value = "Not available";
            item.Type = "";
            item.HasError = true;
        }
    }
}

public partial class WatchPanelItem : ObservableObject
{
    [ObservableProperty]
    private string _expression = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private string _type = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isEditable;

    /// <summary>
    /// True when the user is actively editing this expression inline (distinct from IsEditable
    /// which marks the placeholder row at the bottom of the watch list).
    /// </summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<WatchPanelItem> _children = new();

    /// <summary>
    /// DAP variables reference for expandable values (objects, arrays).
    /// Non-zero means the value can be expanded to show children.
    /// </summary>
    [ObservableProperty]
    private int _variablesReference;
}

public class WatchErrorColorConverter : IValueConverter
{
    public static readonly WatchErrorColorConverter Instance = new();

    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#F48771"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? ErrorBrush : NormalBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
