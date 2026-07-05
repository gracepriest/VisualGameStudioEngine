using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VisualGameStudio.Core.Utilities;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class OutputPanelView : UserControl
{
    private OutputPanelViewModel? _subscribedViewModel;

    public OutputPanelView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox != null)
        {
            inputTextBox.KeyDown -= OnInputKeyDown;
            inputTextBox.KeyDown += OnInputKeyDown;
        }

        // Auto-scroll to the newest line when output is appended.
        if (DataContext is OutputPanelViewModel vm && !ReferenceEquals(_subscribedViewModel, vm))
        {
            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.OutputLines.CollectionChanged -= OnOutputLinesChanged;
            }

            _subscribedViewModel = vm;
            vm.OutputLines.CollectionChanged += OnOutputLinesChanged;
        }
    }

    private void OnOutputLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Defer so the ListBox has processed the collection change before scrolling.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var listBox = this.FindControl<ListBox>("OutputListBox");
            var count = _subscribedViewModel?.OutputLines.Count ?? 0;
            if (listBox != null && count > 0)
            {
                listBox.ScrollIntoView(count - 1);
            }
        });
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is OutputPanelViewModel vm)
            {
                vm.SendInputCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl+C copies the selected lines; Ctrl+A selects every line.
    /// </summary>
    private async void OnOutputListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || sender is not ListBox listBox)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.C)
        {
            e.Handled = true;
            await CopySelectedLinesAsync(listBox);
        }
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
        {
            e.Handled = true;
            listBox.SelectAll();
        }
    }

    /// <summary>
    /// Right-clicking an unselected line selects it (standard behavior) so the
    /// context menu's Copy acts on the line under the cursor. Right-clicking a
    /// line that is already part of the selection keeps the multi-selection.
    /// </summary>
    private void OnOutputListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ListBox listBox)
        {
            return;
        }

        if (!e.GetCurrentPoint(listBox).Properties.IsRightButtonPressed)
        {
            return;
        }

        var container = (e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (container?.DataContext is OutputLine line &&
            listBox.SelectedItems is { } selectedItems &&
            !selectedItems.Contains(line))
        {
            selectedItems.Clear();
            selectedItems.Add(line);
        }
    }

    /// <summary>
    /// Double-click on an error/warning line navigates to the source location.
    /// (Single click is reserved for selection so lines can be copied; this
    /// matches Visual Studio's Output/Error List behavior.)
    /// </summary>
    private void OnOutputListDoubleTapped(object? sender, TappedEventArgs e)
    {
        var container = (e.Source as Avalonia.Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (container?.DataContext is OutputLine { IsClickable: true } line &&
            DataContext is OutputPanelViewModel vm)
        {
            vm.NavigateToSourceCommand.Execute(line);
        }
    }

    /// <summary>
    /// Disables context menu items that would do nothing.
    /// </summary>
    private void OnOutputContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        var listBox = this.FindControl<ListBox>("OutputListBox");
        var hasLines = (DataContext as OutputPanelViewModel)?.OutputLines.Count > 0;
        var hasSelection = (listBox?.SelectedItems?.Count ?? 0) > 0;

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Name switch
            {
                "OutputCopyMenuItem" => hasSelection,
                "OutputCopyAllMenuItem" => hasLines,
                "OutputClearMenuItem" => hasLines,
                _ => item.IsEnabled
            };
        }
    }

    private async void OnCopySelectedClick(object? sender, RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("OutputListBox");
        if (listBox != null)
        {
            await CopySelectedLinesAsync(listBox);
        }
    }

    private async void OnCopyAllClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not OutputPanelViewModel vm)
        {
            return;
        }

        var text = OutputCopyHelper.BuildCopyAllText(vm.OutputLines, line => line.Text);
        await SetClipboardTextAsync(text);
    }

    /// <summary>
    /// Copies the selected lines (in display order, newline-joined) to the clipboard.
    /// </summary>
    private Task CopySelectedLinesAsync(ListBox listBox)
    {
        if (DataContext is not OutputPanelViewModel vm)
        {
            return Task.CompletedTask;
        }

        var selected = listBox.SelectedItems?.OfType<OutputLine>() ?? Enumerable.Empty<OutputLine>();
        var text = OutputCopyHelper.BuildCopyText(vm.OutputLines, selected, line => line.Text);
        return SetClipboardTextAsync(text);
    }

    private async Task SetClipboardTextAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}
