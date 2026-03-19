using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class OutputPanelView : UserControl
{
    public OutputPanelView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox != null)
        {
            inputTextBox.KeyDown += OnInputKeyDown;
        }

        // Auto-scroll to bottom when new output lines are added
        if (DataContext is OutputPanelViewModel vm)
        {
            vm.OutputLines.CollectionChanged += (_, _) =>
            {
                var scroller = this.FindControl<ScrollViewer>("OutputScroller");
                scroller?.ScrollToEnd();
            };
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (DataContext is OutputPanelViewModel vm)
            {
                vm.SendInputCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles click on an output line. If the line is clickable (error/warning),
    /// navigates to the source file and line.
    /// </summary>
    private void OnOutputLinePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock { DataContext: OutputLine outputLine } && outputLine.IsClickable)
        {
            if (DataContext is OutputPanelViewModel vm)
            {
                vm.NavigateToSourceCommand.Execute(outputLine);
            }
        }
    }
}
