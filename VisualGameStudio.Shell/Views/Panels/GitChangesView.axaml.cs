using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Shell.ViewModels.Dialogs;
using VisualGameStudio.Shell.ViewModels.Panels;
using VisualGameStudio.Shell.Views.Dialogs;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class GitChangesView : UserControl
{
    public GitChangesView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is GitChangesViewModel vm)
        {
            vm.ShowDiffRequested -= OnShowDiffRequested;
            vm.ShowDiffRequested += OnShowDiffRequested;
        }
    }

    private void OnChangeItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border && border.Tag is GitChangeItem item)
        {
            if (DataContext is GitChangesViewModel vm)
            {
                vm.ShowDiffCommand.Execute(item);
            }
        }
    }

    private async void OnShowDiffRequested(object? sender, string filePath)
    {
        try
        {
            var gitService = App.Services.GetService<IGitService>();
            if (gitService == null) return;

            var diffVm = new DiffViewerViewModel(gitService);
            await diffVm.LoadDiffAsync(filePath);

            var diffView = new DiffViewerView
            {
                DataContext = diffVm
            };

            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            if (parentWindow != null)
            {
                await diffView.ShowDialog(parentWindow);
            }
            else
            {
                diffView.Show();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GitChangesView] Failed to show diff: {ex.Message}");
        }
    }
}
