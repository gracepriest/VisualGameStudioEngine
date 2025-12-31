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
    }

    public GoToLineDialog(GoToLineDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.LineSelected += (s, e) =>
        {
            Close(viewModel.ResultLine);
        };

        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };
    }
}
