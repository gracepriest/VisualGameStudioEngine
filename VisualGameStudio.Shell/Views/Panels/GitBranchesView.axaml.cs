using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class GitBranchesView : UserControl
{
    public GitBranchesView()
    {
        InitializeComponent();
    }

    private void OnBranchPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Border border && border.DataContext is BranchItemViewModel branch)
        {
            if (DataContext is GitBranchesViewModel vm && !branch.IsCurrentBranch)
            {
                vm.CheckoutCommand.Execute(branch);
            }
        }
    }
}
