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

public partial class WatchViewModel : ViewModelBase
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

    private async void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        try
        {
            if (e.NewState == DebugState.Paused)
            {
                await RefreshAllAsync();
            }
            else if (e.NewState == DebugState.Stopped)
            {
                ClearValues();
            }
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
            await RefreshAllAsync();
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
            catch { }
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
            catch { }
        }
    }

    [RelayCommand]
    private void EditExpression()
    {
        if (SelectedItem != null && !SelectedItem.IsEditable)
        {
            SelectedItem.IsEditable = true;
        }
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (!_debugService.IsDebugging) return;

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
            item.Value = "<not available>";
            item.Type = "";
            item.HasError = true;
            return;
        }

        try
        {
            var result = await _debugService.EvaluateAsync(item.Expression);
            if (result != null)
            {
                item.Value = result.Result ?? "<null>";
                item.Type = result.Type ?? "";
                item.HasError = false;
            }
            else
            {
                item.Value = "<error>";
                item.Type = "";
                item.HasError = true;
            }
        }
        catch (Exception ex)
        {
            item.Value = $"<error: {ex.Message}>";
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

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private ObservableCollection<WatchPanelItem> _children = new();
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
