using Avalonia.Controls;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class FunctionBreakpointDialog : Window
{
    public FunctionBreakpointDialog()
    {
        InitializeComponent();

        // Focus the textbox when the dialog opens
        Opened += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                FunctionNameTextBox?.Focus();
            }, DispatcherPriority.Input);
        };
    }

    public FunctionBreakpointDialog(FunctionBreakpointDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.FunctionBreakpointAdded += (s, functionName) =>
        {
            Close(functionName);
        };

        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };
    }
}
