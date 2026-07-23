using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class NewSolutionView : Window
{
    private NewSolutionViewModel? _vm;

    public NewSolutionView()
    {
        InitializeComponent();
    }

    public NewSolutionView(NewSolutionViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        // Close(bool) so the host's `await ShowDialog<bool>(...)` gets a clean
        // confirmed/cancelled signal on every path (Confirm, Cancel, and the X-close,
        // which falls back to default(bool) = false = cancelled). Re-showing this
        // window after a Back no longer risks a stale DialogResult=true lingering
        // from a previous confirm, because the return value is read fresh each time.
        vm.CloseDialog = () => Close(vm.DialogResult);
    }

    // NewSolutionViewModel.BrowseLocation is a source-generated [RelayCommand] stub
    // ("Handled by the view code-behind") — unlike NewProjectWizardViewModel's
    // settable BrowseLocationCommand property, it can't be reassigned from here.
    // So this follows AddProjectToSolutionDialog's pattern instead: a named button
    // wired to a Click handler in OnLoaded, using the same StorageProvider call
    // NewProjectConfigureView uses for its own Browse handler.
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
        if (_vm == null) return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Solution Location",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            _vm.Location = folders[0].Path.LocalPath;
        }
    }
}
