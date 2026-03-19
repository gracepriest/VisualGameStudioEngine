using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class TerminalView : UserControl
{
    private ScrollViewer? _outputScroller;
    private SelectableTextBlock? _outputTextBlock;
    private ItemsControl? _tabsItemsControl;

    /// <summary>
    /// Stateful ANSI parser that tracks color state across appended chunks.
    /// </summary>
    private AnsiParser _ansiParser = new();

    public TerminalView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _outputScroller = this.FindControl<ScrollViewer>("OutputScroller");
        _outputTextBlock = this.FindControl<SelectableTextBlock>("OutputTextBlock");
        _tabsItemsControl = this.FindControl<ItemsControl>("TabsItemsControl");
        var inputBox = this.FindControl<TextBox>("InputBox");

        if (inputBox != null)
        {
            inputBox.KeyDown += OnInputKeyDown;
        }

        if (DataContext is TerminalViewModel vm)
        {
            vm.OutputAppended += OnOutputAppended;
            vm.OutputCleared += OnOutputCleared;
            vm.ActiveSessionSwitched += OnActiveSessionSwitched;
            vm.Sessions.CollectionChanged += (_, _) => UpdateTabHighlights();
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TerminalViewModel vm)
        {
            vm.SendInputCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handle clicking a tab to switch the active terminal session.
    /// </summary>
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is TerminalSession session
            && DataContext is TerminalViewModel vm)
        {
            vm.ActiveSession = session;
            UpdateTabHighlights();
        }
    }

    /// <summary>
    /// Handle clicking the close button on a tab.
    /// </summary>
    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TerminalSession session
            && DataContext is TerminalViewModel vm)
        {
            vm.CloseSessionCommand.Execute(session);
        }
    }

    private void OnActiveSessionSwitched(object? sender, EventArgs e)
    {
        UpdateTabHighlights();
    }

    /// <summary>
    /// Updates the visual highlight on each tab border to show which is active.
    /// </summary>
    private void UpdateTabHighlights()
    {
        if (_tabsItemsControl == null || DataContext is not TerminalViewModel vm) return;

        // Walk all rendered tab borders and set background based on active state
        foreach (var container in _tabsItemsControl.GetVisualDescendants())
        {
            if (container is Border border && border.Name == "TabBorder"
                && border.DataContext is TerminalSession session)
            {
                border.Background = session == vm.ActiveSession
                    ? new SolidColorBrush(Color.Parse("#3C3C3C"))
                    : new SolidColorBrush(Color.Parse("#2D2D2D"));

                // Add a bottom accent line for the active tab
                border.BorderThickness = session == vm.ActiveSession
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0);

                border.BorderBrush = session == vm.ActiveSession
                    ? new SolidColorBrush(Color.Parse("#569CD6"))
                    : null;
            }
        }
    }

    private void OnOutputCleared()
    {
        if (_outputTextBlock == null) return;

        _outputTextBlock.Inlines?.Clear();
        _ansiParser.Reset();
    }

    private void OnOutputAppended(string text)
    {
        if (_outputTextBlock == null || string.IsNullOrEmpty(text)) return;

        _outputTextBlock.Inlines ??= new InlineCollection();

        var segments = _ansiParser.Parse(text);

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            var run = new Run(segment.Text);

            if (segment.Foreground != null)
                run.Foreground = segment.Foreground;

            // Note: Avalonia Inline/Run does not support per-run Background.
            // Background colors from ANSI codes are not rendered on individual runs.

            if (segment.Bold)
                run.FontWeight = FontWeight.Bold;

            _outputTextBlock.Inlines.Add(run);
        }

        // Auto-scroll to bottom
        _outputScroller?.ScrollToEnd();
    }
}
