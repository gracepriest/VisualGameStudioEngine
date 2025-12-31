using Avalonia.Controls;
using Avalonia.Input;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class BookmarksView : UserControl
{
    public BookmarksView()
    {
        InitializeComponent();
    }

    private void OnListBoxDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is BookmarksViewModel vm && vm.SelectedBookmark != null)
        {
            vm.NavigateToBookmarkCommand.Execute(vm.SelectedBookmark);
        }
    }
}
