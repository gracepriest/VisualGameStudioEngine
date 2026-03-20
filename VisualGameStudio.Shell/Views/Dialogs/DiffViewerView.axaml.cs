using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class DiffViewerView : Window
{
    private ScrollViewer? _leftScrollViewer;
    private ScrollViewer? _rightScrollViewer;
    private bool _isSyncingScroll;

    public DiffViewerView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is DiffViewerViewModel vm)
        {
            vm.NavigateToChangeRequested -= OnNavigateToChange;
            vm.NavigateToChangeRequested += OnNavigateToChange;
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Find scroll viewers for synchronized scrolling
        _leftScrollViewer = this.FindControl<ScrollViewer>("LeftScrollViewer");
        _rightScrollViewer = this.FindControl<ScrollViewer>("RightScrollViewer");

        if (_leftScrollViewer != null)
        {
            _leftScrollViewer.ScrollChanged += OnLeftScrollChanged;
        }

        if (_rightScrollViewer != null)
        {
            _rightScrollViewer.ScrollChanged += OnRightScrollChanged;
        }
    }

    private void OnLeftScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || _rightScrollViewer == null || _leftScrollViewer == null)
            return;

        _isSyncingScroll = true;
        try
        {
            _rightScrollViewer.Offset = new Vector(
                _rightScrollViewer.Offset.X,
                _leftScrollViewer.Offset.Y);
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }

    private void OnRightScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingScroll || _leftScrollViewer == null || _rightScrollViewer == null)
            return;

        _isSyncingScroll = true;
        try
        {
            _leftScrollViewer.Offset = new Vector(
                _leftScrollViewer.Offset.X,
                _rightScrollViewer.Offset.Y);
        }
        finally
        {
            _isSyncingScroll = false;
        }
    }

    private void OnNavigateToChange(object? sender, int lineIndex)
    {
        // Scroll the appropriate scroll viewer to bring the change into view
        Dispatcher.UIThread.Post(() =>
        {
            const double lineHeight = 18.0;
            var targetOffset = lineIndex * lineHeight;

            if (DataContext is DiffViewerViewModel vm && vm.SideBySide)
            {
                if (_leftScrollViewer != null)
                {
                    _leftScrollViewer.Offset = new Vector(
                        _leftScrollViewer.Offset.X,
                        Math.Max(0, targetOffset - 100));
                }
            }
            else
            {
                var unifiedScroll = this.FindControl<ScrollViewer>("UnifiedScrollViewer");
                if (unifiedScroll != null)
                {
                    unifiedScroll.Offset = new Vector(
                        unifiedScroll.Offset.X,
                        Math.Max(0, targetOffset - 100));
                }
            }
        });
    }
}
