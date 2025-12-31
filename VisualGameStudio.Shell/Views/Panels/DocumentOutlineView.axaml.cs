using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class DocumentOutlineView : UserControl
{
    public DocumentOutlineView()
    {
        InitializeComponent();
    }

    private void OnTreeViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is DocumentOutlineViewModel vm)
        {
            vm.NavigateToNodeCommand.Execute(null);
        }
    }
}
