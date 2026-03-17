using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.Views.Controls;

namespace VisualGameStudio.Shell.Views;

public partial class MainWindow : Window
{
    private Popup? _dataTipPopup;
    private DataTipPopup? _dataTipContent;
    private DispatcherTimer? _hideTimer;
    private MainWindowViewModel? _subscribedVm;

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
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Unsubscribe from old VM to prevent event handler leak
        if (_subscribedVm != null)
        {
            _subscribedVm.DataTipResult -= OnDataTipResult;
            _subscribedVm = null;
        }

        if (DataContext is MainWindowViewModel vm)
        {
            vm.DataTipResult += OnDataTipResult;
            _subscribedVm = vm;
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

    private void OnCommandPaletteHintClicked(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.OpenCommandPaletteCommand.Execute(null);
        }
    }
}
