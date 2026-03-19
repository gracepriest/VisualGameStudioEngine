using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.Views.Controls;
using VisualGameStudio.Shell.Views.Panels;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Shell.Views;

public partial class MainWindow : Window
{
    private Popup? _dataTipPopup;
    private DataTipPopup? _dataTipContent;
    private DispatcherTimer? _hideTimer;
    private MainWindowViewModel? _subscribedVm;
    private StackPanel? _notificationArea;

    /// <summary>
    /// Stores the window state before entering Zen mode so it can be restored on exit.
    /// </summary>
    private WindowState _preZenWindowState;

    public MainWindow()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        // Setup hide timer for data tip
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _hideTimer.Tick += (s, e) =>
        {
            HideDataTip();
            _hideTimer.Stop();
        };

        _notificationArea = this.FindControl<StackPanel>("NotificationArea");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Escape exits Zen mode
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm && vm.IsZenMode)
        {
            vm.ExitZenMode();
            RestoreFromZenMode();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old VM to prevent event handler leak
        if (_subscribedVm != null)
        {
            _subscribedVm.DataTipResult -= OnDataTipResult;
            _subscribedVm.NotificationRequested -= OnNotificationRequested;
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm.FocusPanelRequested -= OnFocusPanelRequested;
            _subscribedVm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.DataTipResult += OnDataTipResult;
            vm.NotificationRequested += OnNotificationRequested;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.FocusPanelRequested += OnFocusPanelRequested;
            _subscribedVm = vm;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsZenMode) && sender is MainWindowViewModel vm)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (vm.IsZenMode)
                    EnterZenMode();
                else
                    RestoreFromZenMode();
            });
        }
    }

    private void EnterZenMode()
    {
        _preZenWindowState = WindowState;
        WindowState = WindowState.Maximized;
    }

    private void RestoreFromZenMode()
    {
        WindowState = _preZenWindowState;
    }

    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ShowToastNotification(e.Message, e.Severity));
    }

    private void ShowToastNotification(string message, string severity)
    {
        if (_notificationArea == null) return;

        // Choose background color based on severity
        IBrush background = severity.ToLowerInvariant() switch
        {
            "warning" => new SolidColorBrush(Color.FromArgb(230, 180, 150, 20)),   // dark yellow
            "error"   => new SolidColorBrush(Color.FromArgb(230, 180, 40, 40)),     // dark red
            _         => new SolidColorBrush(Color.FromArgb(230, 40, 100, 180)),    // dark blue (info)
        };

        var textBlock = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var toast = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            MinWidth = 200,
            MaxWidth = 400,
            IsHitTestVisible = true,
            Child = textBlock,
            // Drop shadow effect via BoxShadow
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 2,
                Blur = 8,
                Color = Color.FromArgb(120, 0, 0, 0),
            }),
        };

        _notificationArea.Children.Add(toast);

        // Auto-remove after 5 seconds
        var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        removeTimer.Tick += (s, args) =>
        {
            removeTimer.Stop();
            if (_notificationArea.Children.Contains(toast))
            {
                _notificationArea.Children.Remove(toast);
            }
        };
        removeTimer.Start();
    }

    private void OnDataTipResult(object? sender, DataTipResultEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ShowDataTip(e.Expression, e.Value, e.Type, e.ScreenX, e.ScreenY, e.IsError);
        });
    }

    private void ShowDataTip(string expression, string value, string? type, double screenX, double screenY, bool isError)
    {
        // Create popup if needed
        if (_dataTipPopup == null)
        {
            _dataTipContent = new DataTipPopup();
            _dataTipContent.AddToWatchClicked += OnDataTipAddToWatch;
            _dataTipContent.PointerEntered += OnDataTipPointerEntered;
            _dataTipContent.PointerExited += OnDataTipPointerExited;

            _dataTipPopup = new Popup
            {
                Child = _dataTipContent,
                IsLightDismissEnabled = true,
                Placement = PlacementMode.Pointer,
                WindowManagerAddShadowHint = true
            };

            // Add popup to the adorner layer or content
            if (this.Content is Panel panel)
            {
                _dataTipPopup.PlacementTarget = panel;
            }
        }

        // Set content
        if (isError)
        {
            _dataTipContent?.SetError(expression, value);
        }
        else
        {
            _dataTipContent?.SetContent(expression, value, type);
        }

        // Position and show - convert screen coordinates to window-relative
        var windowPos = this.Position;
        _dataTipPopup.HorizontalOffset = screenX - windowPos.X;
        _dataTipPopup.VerticalOffset = screenY - windowPos.Y;
        _dataTipPopup.IsOpen = true;

        // Start hide timer
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    private void HideDataTip()
    {
        if (_dataTipPopup != null)
        {
            _dataTipPopup.IsOpen = false;
        }
    }

    private void OnDataTipPointerEntered(object? sender, PointerEventArgs e)
    {
        // Stop hide timer when mouse enters popup
        _hideTimer?.Stop();
    }

    private void OnDataTipPointerExited(object? sender, PointerEventArgs e)
    {
        // Restart hide timer when mouse leaves popup
        _hideTimer?.Start();
    }

    private async void OnDataTipAddToWatch(object? sender, string expression)
    {
        try
        {
            HideDataTip();
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.Watch.AddExpressionCommand.ExecuteAsync(expression);
            }
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    // Status bar click handlers
    private void OnIndentationClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.CycleIndentationCommand.Execute(null);
        }
    }

    private void OnEncodingClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.CycleEncodingCommand.Execute(null);
        }
    }

    private void OnLineEndingClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.CycleLineEndingCommand.Execute(null);
        }
    }

    private void OnLanguageModeClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.ShowLanguageModeCommand.Execute(null);
        }
    }

    private void OnDiagnosticCountsClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ShowErrorListCommand.Execute(null);
        }
    }

    private void OnCommandPaletteHintClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenCommandPaletteCommand.Execute(null);
        }
    }

    private void OnFocusPanelRequested(object? sender, string panelName)
    {
        Dispatcher.UIThread.Post(() =>
        {
            switch (panelName)
            {
                case "SolutionExplorer":
                    FocusFirstDescendant<SolutionExplorerView>();
                    break;
                case "Editor":
                    FocusActiveEditor();
                    break;
                case "Output":
                    FocusFirstDescendant<OutputPanelView>();
                    break;
                case "Terminal":
                    FocusFirstDescendant<TerminalView>();
                    break;
                case "ErrorList":
                    FocusFirstDescendant<ErrorListView>();
                    break;
                case "Variables":
                    FocusFirstDescendant<VariablesView>();
                    break;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Finds the first descendant of the given type in the visual tree and focuses
    /// its first focusable child control (TreeView, DataGrid, TextBox, etc.).
    /// </summary>
    private void FocusFirstDescendant<T>() where T : UserControl
    {
        var panel = this.GetVisualDescendants().OfType<T>().FirstOrDefault();
        if (panel == null) return;

        // Try to focus the first interactive control inside the panel
        var focusable = panel.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.Focusable && c is not Panel);

        if (focusable != null)
        {
            focusable.Focus();
        }
        else
        {
            // Fallback: focus the panel itself
            panel.Focus();
        }
    }

    /// <summary>
    /// Focuses the active code editor's TextArea so the user can immediately type.
    /// </summary>
    private void FocusActiveEditor()
    {
        // Find the active CodeEditorDocumentView and focus its text editor
        var editorViews = this.GetVisualDescendants().OfType<CodeEditorDocumentView>().ToList();

        // The active editor is typically the one that is visible (IsEffectivelyVisible)
        var activeEditor = editorViews.FirstOrDefault(v => v.IsEffectivelyVisible);
        if (activeEditor == null) return;

        // Find the AvaloniaEdit TextEditor inside the editor view and focus its TextArea
        var textEditor = activeEditor.GetVisualDescendants()
            .OfType<AvaloniaEdit.TextEditor>()
            .FirstOrDefault();

        if (textEditor?.TextArea != null)
        {
            textEditor.TextArea.Focus();
        }
    }
}
