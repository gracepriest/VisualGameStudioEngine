using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.Services;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.Views.Controls;
using VisualGameStudio.Shell.Views.Panels;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Dialogs;
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
    /// Tracks active progress notifications by ID so they can be updated in-place.
    /// </summary>
    private readonly Dictionary<string, Border> _activeProgressToasts = new();

    /// <summary>
    /// Timer used for temporary status bar messages that auto-revert.
    /// </summary>
    private DispatcherTimer? _statusBarMessageTimer;
    private string? _previousStatusText;

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

        // Initialize screen reader live regions
        var politeRegion = this.FindControl<TextBlock>("LiveRegionPolite");
        var assertiveRegion = this.FindControl<TextBlock>("LiveRegionAssertive");
        if (politeRegion != null && assertiveRegion != null)
        {
            ScreenReaderService.Instance.Initialize(politeRegion, assertiveRegion);
        }
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
            _subscribedVm.NotificationDismissed -= OnNotificationDismissed;
            _subscribedVm.StatusBarMessageRequested -= OnStatusBarMessageRequested;
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm.FocusPanelRequested -= OnFocusPanelRequested;
            _subscribedVm.ZoomLevelChanged -= OnZoomLevelChanged;
            _subscribedVm.CompareViewRequested -= OnCompareViewRequested;
            _subscribedVm.CompareWithClipboardRequested -= OnCompareWithClipboardRequested;
            _subscribedVm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.DataTipResult += OnDataTipResult;
            vm.NotificationRequested += OnNotificationRequested;
            vm.NotificationDismissed += OnNotificationDismissed;
            vm.StatusBarMessageRequested += OnStatusBarMessageRequested;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            vm.FocusPanelRequested += OnFocusPanelRequested;
            vm.ZoomLevelChanged += OnZoomLevelChanged;
            vm.CompareViewRequested += OnCompareViewRequested;
            vm.CompareWithClipboardRequested += OnCompareWithClipboardRequested;
            _subscribedVm = vm;

            // Apply initial zoom level from settings
            if (vm.ZoomLevel != 100)
            {
                ApplyZoom(vm.ZoomLevel);
            }
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
        Dispatcher.UIThread.Post(() => ShowToastNotification(e));
    }

    private void OnNotificationDismissed(object? sender, string notificationId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeProgressToasts.TryGetValue(notificationId, out var toast))
            {
                RemoveToast(toast);
                _activeProgressToasts.Remove(notificationId);
            }
        });
    }

    private void OnStatusBarMessageRequested(object? sender, StatusBarMessageEventArgs e)
    {
        Dispatcher.UIThread.Post(() => ShowTemporaryStatusBarMessage(e.Message, e.DurationSeconds));
    }

    /// <summary>
    /// Shows a temporary message in the status bar that reverts after the specified duration.
    /// </summary>
    private void ShowTemporaryStatusBarMessage(string message, double durationSeconds)
    {
        if (_subscribedVm == null) return;

        // Save current status text only if we don't already have a saved one
        if (_previousStatusText == null)
            _previousStatusText = _subscribedVm.StatusText;

        _subscribedVm.StatusText = message;

        // Cancel previous timer
        _statusBarMessageTimer?.Stop();
        _statusBarMessageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(durationSeconds) };
        _statusBarMessageTimer.Tick += (s, args) =>
        {
            _statusBarMessageTimer?.Stop();
            if (_subscribedVm != null && _previousStatusText != null)
            {
                _subscribedVm.StatusText = _previousStatusText;
                _previousStatusText = null;
            }
        };
        _statusBarMessageTimer.Start();
    }

    private void ShowToastNotification(NotificationEventArgs e)
    {
        if (_notificationArea == null) return;

        // If this is a progress update for an existing notification, update it in place
        if (e.ShowProgress && e.NotificationId != null && _activeProgressToasts.TryGetValue(e.NotificationId, out var existingToast))
        {
            UpdateProgressToast(existingToast, e);
            return;
        }

        var severity = e.Severity.ToLowerInvariant();

        // Choose icon and background color based on severity
        (IBrush background, string icon) = severity switch
        {
            "warning" => ((IBrush)new SolidColorBrush(Color.FromArgb(240, 60, 50, 10)), "\u26A0"),
            "error" => ((IBrush)new SolidColorBrush(Color.FromArgb(240, 80, 20, 20)), "\u274C"),
            _ => ((IBrush)new SolidColorBrush(Color.FromArgb(240, 20, 50, 80)), "\u2139"),
        };

        // Severity icon
        var iconBlock = new TextBlock
        {
            Text = icon,
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 8, 0),
        };

        // Message text
        var messageBlock = new TextBlock
        {
            Text = e.Message,
            Foreground = Brushes.White,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Content panel (vertical: message, optional details, optional progress, optional actions)
        var contentPanel = new StackPanel { Spacing = 6 };
        contentPanel.Children.Add(messageBlock);

        // Details section (expandable for errors)
        if (!string.IsNullOrEmpty(e.Details))
        {
            var detailsBlock = new TextBlock
            {
                Text = e.Details,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 80,
            };
            contentPanel.Children.Add(detailsBlock);
        }

        // Progress bar (for progress notifications)
        if (e.ShowProgress)
        {
            var progressBar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 1,
                Value = e.Progress,
                IsIndeterminate = e.IsIndeterminate,
                Height = 4,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
            };
            progressBar.Tag = "ProgressBar"; // Tag for updating later
            contentPanel.Children.Add(progressBar);
        }

        // Action buttons
        if (e.Actions.Count > 0)
        {
            var actionsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var action in e.Actions)
            {
                var btn = new Button
                {
                    Content = action.Label,
                    FontSize = 11,
                    Padding = new Thickness(8, 3),
                    Background = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                };
                var callback = action.Callback;
                btn.Click += (s, args) =>
                {
                    try { callback(); } catch { }
                };
                actionsPanel.Children.Add(btn);
            }
            contentPanel.Children.Add(actionsPanel);
        }

        // Close button
        var closeBtn = new Button
        {
            Content = "\u2715",
            FontSize = 12,
            Padding = new Thickness(4, 1),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };

        // Layout: icon + content + close button
        var innerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
        };
        Grid.SetColumn(iconBlock, 0);
        Grid.SetColumn(contentPanel, 1);
        Grid.SetColumn(closeBtn, 2);
        innerGrid.Children.Add(iconBlock);
        innerGrid.Children.Add(contentPanel);
        innerGrid.Children.Add(closeBtn);

        var toast = new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            MinWidth = 280,
            MaxWidth = 420,
            IsHitTestVisible = true,
            Child = innerGrid,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                OffsetX = 0,
                OffsetY = 2,
                Blur = 10,
                Color = Color.FromArgb(140, 0, 0, 0),
            }),
        };

        // Close button removes the toast
        closeBtn.Click += (s, args) =>
        {
            RemoveToast(toast);
            if (e.NotificationId != null)
                _activeProgressToasts.Remove(e.NotificationId);
        };

        _notificationArea.Children.Add(toast);

        // Track progress toasts by ID for updates
        if (e.ShowProgress && e.NotificationId != null)
        {
            _activeProgressToasts[e.NotificationId] = toast;
        }

        // Auto-dismiss: info toasts after 5s, warnings/errors stay unless autoDismiss is set
        bool shouldAutoDismiss = e.AutoDismiss || (severity == "info" && e.Actions.Count == 0 && !e.ShowProgress);
        if (shouldAutoDismiss)
        {
            var removeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            removeTimer.Tick += (s, args) =>
            {
                removeTimer.Stop();
                RemoveToast(toast);
                if (e.NotificationId != null)
                    _activeProgressToasts.Remove(e.NotificationId);
            };
            removeTimer.Start();
        }

        // Limit visible toasts to 5 — remove oldest
        while (_notificationArea.Children.Count > 5)
        {
            _notificationArea.Children.RemoveAt(0);
        }
    }

    /// <summary>
    /// Updates an existing progress toast's message and progress bar.
    /// </summary>
    private void UpdateProgressToast(Border toast, NotificationEventArgs e)
    {
        if (toast.Child is Grid grid)
        {
            // Find the content panel (column 1)
            foreach (var child in grid.Children)
            {
                if (child is StackPanel panel && Grid.GetColumn((Control)child) == 1)
                {
                    // Update message text (first child)
                    if (panel.Children.Count > 0 && panel.Children[0] is TextBlock msgBlock)
                    {
                        msgBlock.Text = e.Message;
                    }

                    // Update progress bar
                    foreach (var pChild in panel.Children)
                    {
                        if (pChild is ProgressBar progressBar)
                        {
                            progressBar.Value = e.Progress;
                            progressBar.IsIndeterminate = e.IsIndeterminate;
                            break;
                        }
                    }
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Safely removes a toast from the notification area.
    /// </summary>
    private void RemoveToast(Border toast)
    {
        if (_notificationArea != null && _notificationArea.Children.Contains(toast))
        {
            _notificationArea.Children.Remove(toast);
        }
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

    // Status bar click handlers (Button.Click for keyboard accessibility)
    private void OnIndentationButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.CycleIndentationCommand.Execute(null);
        }
    }

    private void OnEncodingButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.CycleEncodingCommand.Execute(null);
        }
    }

    private void OnLineEndingButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StatusBar.CycleLineEndingCommand.Execute(null);
        }
    }

    private void OnLanguageModeButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    // ------------------------------------------------------------------
    // IDE Zoom (Ctrl+/Ctrl-/Ctrl+0)
    // ------------------------------------------------------------------

    private void OnZoomLevelChanged(object? sender, int zoomPercent)
    {
        Dispatcher.UIThread.Post(() => ApplyZoom(zoomPercent));
    }

    /// <summary>
    /// Applies the zoom level by setting a ScaleTransform on the main content panel.
    /// The status bar is excluded from zoom so it remains at a constant size.
    /// </summary>
    private void ApplyZoom(int zoomPercent)
    {
        var scale = zoomPercent / 100.0;

        // Find the DockPanel (main content excluding status bar) inside the root Grid
        if (this.Content is not Avalonia.Controls.Grid rootGrid) return;

        foreach (var child in rootGrid.Children)
        {
            if (child is DockPanel dockPanel)
            {
                dockPanel.RenderTransform = new ScaleTransform(scale, scale);
                dockPanel.RenderTransformOrigin = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative);
                break;
            }
        }
    }

    private async void OnCompareViewRequested(object? sender, DiffViewerViewModel diffVm)
    {
        try
        {
            var diffView = new DiffViewerView { DataContext = diffVm };
            await diffView.ShowDialog(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to show compare view: {ex.Message}");
        }
    }

    private async void OnCompareWithClipboardRequested(object? sender, CodeEditorDocumentViewModel activeDoc)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            var clipboardText = await clipboard.GetTextAsync();
            if (string.IsNullOrEmpty(clipboardText))
            {
                if (DataContext is MainWindowViewModel vm2)
                    vm2.StatusText = "Clipboard is empty";
                return;
            }

            var gitService = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetService<VisualGameStudio.Core.Abstractions.Services.IGitService>(App.Services);
            if (gitService == null) return;

            var diffVm = new DiffViewerViewModel(gitService);
            diffVm.LoadContents(clipboardText, activeDoc.Text,
                "Clipboard",
                System.IO.Path.GetFileName(activeDoc.FilePath ?? "Untitled"));

            var diffView = new DiffViewerView { DataContext = diffVm };
            await diffView.ShowDialog(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Failed to compare with clipboard: {ex.Message}");
        }
    }
}
