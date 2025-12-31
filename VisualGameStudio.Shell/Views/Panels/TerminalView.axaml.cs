using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class TerminalView : UserControl
{
    private ScrollViewer? _outputScroller;

    public TerminalView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _outputScroller = this.FindControl<ScrollViewer>("OutputScroller");
        var inputBox = this.FindControl<TextBox>("InputBox");

        if (inputBox != null)
        {
            inputBox.KeyDown += OnInputKeyDown;
        }

        if (DataContext is TerminalViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TerminalViewModel.OutputText))
        {
            // Auto-scroll to bottom
            _outputScroller?.ScrollToEnd();
        }
    }
}
