using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class ImmediateWindowView : UserControl
{
    public ImmediateWindowView()
    {
        InitializeComponent();

        // Auto-scroll to bottom when output changes
        if (DataContext is ImmediateWindowViewModel vm)
        {
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ImmediateWindowViewModel.OutputText))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        OutputScroller?.ScrollToEnd();
                    });
                }
            };
        }

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ImmediateWindowViewModel vm)
        {
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(ImmediateWindowViewModel.OutputText))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        OutputScroller?.ScrollToEnd();
                    });
                }
            };
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
