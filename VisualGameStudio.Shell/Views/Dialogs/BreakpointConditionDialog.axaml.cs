using Avalonia.Controls;
using Avalonia.Threading;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class BreakpointConditionDialog : Window
{
    public BreakpointConditionDialog()
    {
        InitializeComponent();
    }

    public BreakpointConditionDialog(BreakpointConditionDialogViewModel viewModel) : this()
    {
        DataContext = viewModel;

        viewModel.ConditionSet += (s, e) =>
        {
            Close(viewModel);
        };

        viewModel.Cancelled += (s, e) =>
        {
            Close(null);
        };

        // Focus the appropriate textbox when the dialog opens
        Opened += (s, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (viewModel.IsConditionalExpression)
                {
                    ConditionTextBox?.Focus();
                }
                else if (viewModel.IsHitCount)
                {
                    HitCountTextBox?.Focus();
                }
                else if (viewModel.IsLogMessage)
                {
                    LogMessageTextBox?.Focus();
                }
            }, DispatcherPriority.Input);
        };
    }
}
