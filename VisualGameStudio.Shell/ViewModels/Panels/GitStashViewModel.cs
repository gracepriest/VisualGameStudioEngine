using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class GitStashViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<StashItemViewModel> _stashes = new();

    [ObservableProperty]
    private StashItemViewModel? _selectedStash;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _stashMessage = "";

    public GitStashViewModel(IGitService gitService, IDialogService dialogService)
    {
        _gitService = gitService;
        _dialogService = dialogService;
        _gitService.StatusChanged += OnStatusChanged;
    }

    private async void OnStatusChanged(object? sender, EventArgs e)
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        Stashes.Clear();

        try
        {
            var stashInfos = await _gitService.GetStashesAsync();
            foreach (var info in stashInfos)
            {
                Stashes.Add(new StashItemViewModel
                {
                    Index = info.Index,
                    Message = info.Message,
                    BranchName = info.BranchName,
                    Date = info.Date
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task StashChangesAsync()
    {
        var message = string.IsNullOrWhiteSpace(StashMessage) ? null : StashMessage.Trim();
        var success = await _gitService.StashAsync(message);

        if (success)
        {
            StashMessage = "";
            await RefreshAsync();
        }
        else
        {
            await _dialogService.ShowMessageAsync("Stash Failed",
                "Failed to stash changes. Make sure you have uncommitted changes.",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task ApplyStashAsync(StashItemViewModel? stash)
    {
        if (stash == null) return;

        var success = await _gitService.ApplyStashAsync(stash.Index, pop: false);
        if (!success)
        {
            await _dialogService.ShowMessageAsync("Apply Failed",
                "Failed to apply stash. There may be conflicts with your working directory.",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task PopStashAsync(StashItemViewModel? stash)
    {
        if (stash == null) return;

        var success = await _gitService.ApplyStashAsync(stash.Index, pop: true);
        if (success)
        {
            await RefreshAsync();
        }
        else
        {
            await _dialogService.ShowMessageAsync("Pop Failed",
                "Failed to pop stash. There may be conflicts with your working directory.",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task DropStashAsync(StashItemViewModel? stash)
    {
        if (stash == null) return;

        var result = await _dialogService.ShowMessageAsync("Drop Stash",
            $"Are you sure you want to drop stash '{stash.DisplayName}'?\nThis action cannot be undone.",
            DialogButtons.YesNo, DialogIcon.Warning);

        if (result == DialogResult.Yes)
        {
            var success = await _gitService.DropStashAsync(stash.Index);
            if (success)
            {
                await RefreshAsync();
            }
            else
            {
                await _dialogService.ShowMessageAsync("Drop Failed",
                    "Failed to drop stash.",
                    DialogButtons.Ok, DialogIcon.Error);
            }
        }
    }

    [RelayCommand]
    private async Task DropAllStashesAsync()
    {
        if (Stashes.Count == 0) return;

        var result = await _dialogService.ShowMessageAsync("Drop All Stashes",
            $"Are you sure you want to drop all {Stashes.Count} stash(es)?\nThis action cannot be undone.",
            DialogButtons.YesNo, DialogIcon.Warning);

        if (result == DialogResult.Yes)
        {
            // Drop from highest index to lowest to maintain correct indices
            for (int i = Stashes.Count - 1; i >= 0; i--)
            {
                await _gitService.DropStashAsync(0); // Always drop the first one
            }
            await RefreshAsync();
        }
    }
}

public partial class StashItemViewModel : ObservableObject
{
    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string _branchName = "";

    [ObservableProperty]
    private DateTime _date;

    public string DisplayName => $"stash@{{{Index}}}";
    public string DateFormatted => Date.ToString("MMM dd, yyyy HH:mm");
    public string Summary => string.IsNullOrEmpty(Message) ? $"WIP on {BranchName}" : Message;
}
