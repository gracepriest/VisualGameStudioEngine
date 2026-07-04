using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class ImmediateWindowView : UserControl
{
    private ImmediateWindowViewModel? _subscribedViewModel;

    public ImmediateWindowView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        // Pick up a DataContext assigned before construction completed
        OnDataContextChanged(this, EventArgs.Empty);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // Detach from the previous view model so handlers never accumulate
        // when the DataContext is reassigned.
        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _subscribedViewModel = DataContext as ImmediateWindowViewModel;

        if (_subscribedViewModel != null)
        {
            _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    // Auto-scroll to bottom when output changes
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImmediateWindowViewModel.OutputText))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                OutputScroller?.ScrollToEnd();
            });
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ImmediateWindowViewModel vm)
            return;

        switch (e.Key)
        {
            case Key.Enter:
                vm.ExecuteCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Up:
                vm.HistoryUpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                vm.HistoryDownCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                vm.InputText = "";
                e.Handled = true;
                break;
        }
    }
}
