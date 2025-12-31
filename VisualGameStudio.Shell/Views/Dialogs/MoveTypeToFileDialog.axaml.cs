using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class MoveTypeToFileDialog : Window
{
    public MoveTypeToFileDialog()
    {
        InitializeComponent();

        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Focus the file name textbox
        FileNameTextBox?.Focus();
        FileNameTextBox?.SelectAll();

        // Wire up events from the view model
        if (DataContext is MoveTypeToFileDialogViewModel vm)
        {
            vm.MoveCompleted += OnMoveCompleted;
            vm.Cancelled += OnCancelled;
        }
    }

    // Handle browse directory from the view via button click
    private async void OnBrowseDirectory(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await BrowseDirectoryAsync();
    }

    private async Task BrowseDirectoryAsync()
    {
        if (DataContext is not MoveTypeToFileDialogViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Target Directory",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            vm.TargetDirectory = folders[0].Path.LocalPath;
        }
    }

    private void OnMoveCompleted(object? sender, MoveTypeToFileResult result)
    {
        Close(result);
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Close(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && DataContext is MoveTypeToFileDialogViewModel vm)
        {
            if (vm.IsValid)
            {
                vm.MoveTypeCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
