using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class AddProjectToSolutionDialog : AccessibleDialog
{
    public AddProjectToSolutionDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is AddProjectToSolutionViewModel vm)
        {
            vm.CloseDialog = () => Close(vm.DialogResult);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var browseButton = this.FindControl<Button>("BrowseButton");
        if (browseButton != null)
        {
            browseButton.Click += async (s, args) => await BrowseLocationAsync();
        }
    }

    private async Task BrowseLocationAsync()
    {
        if (DataContext is not AddProjectToSolutionViewModel vm) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Location",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            vm.ProjectLocation = folders[0].Path.LocalPath;
        }
    }
}
