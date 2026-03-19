using Avalonia.Controls;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class GoToLineDialog : Window
{
    public GoToLineDialog()
    {
        InitializeComponent();

        Opened += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                LineNumberTextBox?.Focus();
                LineNumberTextBox?.SelectAll();
            }, DispatcherPriority.Input);
        };

        // Close on lost focus (like VS Code)
        Deactivated += (s, e) =>
        {
            if (IsVisible)
                Close(null);
        };
    }

    public GoToLineDialog(GoToLineDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.LineSelected += (s, e) =>
        {
            Close(new GoToLineResult(viewModel.ResultLine ?? 1, viewModel.ResultColumn ?? 1));
        };

        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };
    }
}

/// <summary>
/// Result from the Go To Line dialog, supporting both line and column.
/// </summary>
public class GoToLineResult
{
    public int Line { get; }
    public int Column { get; }

    public GoToLineResult(int line, int column)
    {
        Line = line;
        Column = column;
    }
}
