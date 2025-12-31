using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class GoToSymbolDialog : Window
{
    private readonly GoToSymbolDialogViewModel _viewModel;

    public GoToSymbolDialog(GoToSymbolDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;

        _viewModel.SymbolSelected += OnSymbolSelected;
        _viewModel.Cancelled += OnCancelled;

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        searchBox?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case Key.Enter:
                _viewModel.SelectSymbolCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                _viewModel.CancelCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Down:
                _viewModel.SelectNextCommand.Execute(null);
                e.Handled = true;
                ScrollToSelected();
                break;

            case Key.Up:
                _viewModel.SelectPreviousCommand.Execute(null);
                e.Handled = true;
                ScrollToSelected();
                break;
        }
    }

    private void ScrollToSelected()
    {
        var listBox = this.FindControl<ListBox>("SymbolListBox");
        if (listBox != null && _viewModel.SelectedSymbol != null)
        {
            listBox.ScrollIntoView(_viewModel.SelectedSymbol);
        }
    }

    private void OnSymbolSelected(object? sender, EventArgs e)
    {
        Close(_viewModel.ResultSymbol);
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Close(null);
    }
}
