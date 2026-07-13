using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;

namespace VisualGameStudio.Shell.Views.Dialogs;

public partial class NewProjectConfigureView : Window
{
    public enum WizardOutcome { Back, Cancelled, Created }

    private readonly NewProjectWizardViewModel? _vm;

    public WizardOutcome Outcome { get; private set; } = WizardOutcome.Back;
    public ProjectCreationResult? Result { get; private set; }

    public NewProjectConfigureView()
    {
        InitializeComponent();
    }

    public NewProjectConfigureView(NewProjectWizardViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.BrowseLocationCommand = new AsyncRelayCommand(BrowseLocationAsync);
        vm.BackRequested += OnBack;
        vm.Cancelled += OnCancelled;
        vm.ProjectCreated += OnProjectCreated;
    }

    private async Task BrowseLocationAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Project Location",
            AllowMultiple = false
        });
        if (folders.Count > 0 && _vm != null)
            _vm.Location = folders[0].Path.LocalPath;
    }

    private void OnBack(object? sender, EventArgs e)
    {
        Outcome = WizardOutcome.Back;
        Close();
    }

    private void OnCancelled(object? sender, EventArgs e)
    {
        Outcome = WizardOutcome.Cancelled;
        Close();
    }

    private void OnProjectCreated(object? sender, ProjectCreationResult result)
    {
        Outcome = WizardOutcome.Created;
        Result = result;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
        {
            _vm.BackRequested -= OnBack;
            _vm.Cancelled -= OnCancelled;
            _vm.ProjectCreated -= OnProjectCreated;
        }
        base.OnClosed(e);
    }
}
