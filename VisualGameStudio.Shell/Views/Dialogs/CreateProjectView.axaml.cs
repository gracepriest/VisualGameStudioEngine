using Avalonia.Controls;
using Avalonia.Platform.Storage;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class CreateProjectView : Window
{
    private CreateProjectViewModel? _viewModel;

    public CreateProjectView()
    {
        InitializeComponent();
    }

    public CreateProjectView(IProjectTemplateService templateService) : this()
    {
        _viewModel = new CreateProjectViewModel(templateService);
        DataContext = _viewModel;

        _viewModel.ProjectCreated += OnProjectCreated;
        _viewModel.Cancelled += OnCancelled;

        // Override browse location command to use platform dialog
        _viewModel.BrowseLocationCommand = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(BrowseLocationAsync);
    }

    public ProjectCreationResult? Result { get; private set; }

    private void OnProjectCreated(object? sender, ProjectCreationResult result)
    {
        Result = result;
        Close(true);
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Close(false);
    }

    private async Task BrowseLocationAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Location",
            AllowMultiple = false
        });

        if (folders.Count > 0 && _viewModel != null)
        {
            _viewModel.Location = folders[0].Path.LocalPath;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ProjectCreated -= OnProjectCreated;
            _viewModel.Cancelled -= OnCancelled;
        }
        base.OnClosed(e);
    }
}
