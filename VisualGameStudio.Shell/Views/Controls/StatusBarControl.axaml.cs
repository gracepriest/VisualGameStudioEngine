using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Shell.Views.Controls;

public partial class StatusBarControl : UserControl
{
    private StatusBarViewModel? _subscribedVm;
    private readonly IBrush _normalBrush = new ImmutableSolidColorBrush(Color.Parse("#007ACC"));
    private readonly IBrush _debugBrush = new ImmutableSolidColorBrush(Color.Parse("#CC6633"));

    public StatusBarControl()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedVm = null;
        }

        if (DataContext is StatusBarViewModel vm)
        {
            _subscribedVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            UpdateStatusBarColor(vm.IsDebugging);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusBarViewModel.IsDebugging) && sender is StatusBarViewModel vm)
        {
            Dispatcher.UIThread.Post(() => UpdateStatusBarColor(vm.IsDebugging));
        }
        else if (e.PropertyName == nameof(StatusBarViewModel.IsTaskRunning) && sender is StatusBarViewModel vm2)
        {
            Dispatcher.UIThread.Post(() => UpdateSpinnerAnimation(vm2.IsTaskRunning));
        }
    }

    private void UpdateStatusBarColor(bool isDebugging)
    {
        var border = this.FindControl<Border>("StatusBarBorder");
        if (border != null)
        {
            border.Background = isDebugging ? _debugBrush : _normalBrush;
        }
    }

    private DispatcherTimer? _spinnerTimer;

    private void UpdateSpinnerAnimation(bool isRunning)
    {
        var spinner = this.FindControl<TextBlock>("SpinnerText");
        if (spinner == null) return;

        if (isRunning)
        {
            if (_spinnerTimer == null)
            {
                var angle = 0.0;
                _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                _spinnerTimer.Tick += (_, _) =>
                {
                    angle = (angle + 30) % 360;
                    spinner.RenderTransform = new RotateTransform(angle);
                };
                _spinnerTimer.Start();
            }
        }
        else
        {
            _spinnerTimer?.Stop();
            _spinnerTimer = null;
            spinner.RenderTransform = new RotateTransform(0);
        }
    }
}
