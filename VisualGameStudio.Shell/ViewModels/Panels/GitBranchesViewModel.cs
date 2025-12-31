using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class GitBranchesViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<BranchItemViewModel> _branches = new();

    [ObservableProperty]
    private BranchItemViewModel? _selectedBranch;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _showRemoteBranches = true;

    [ObservableProperty]
    private string _newBranchName = "";

    public GitBranchesViewModel(IGitService gitService, IDialogService dialogService)
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
        Branches.Clear();

        try
        {
            var branchInfos = await _gitService.GetBranchInfoAsync();
            foreach (var info in branchInfos)
            {
                if (!ShowRemoteBranches && info.IsRemote)
                    continue;

                Branches.Add(new BranchItemViewModel
                {
                    Name = info.Name,
                    IsCurrentBranch = info.IsCurrentBranch,
                    IsRemote = info.IsRemote,
                    TrackingBranch = info.TrackingBranch,
                    Ahead = info.Ahead,
                    Behind = info.Behind
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task CheckoutAsync(BranchItemViewModel? branch)
    {
        if (branch == null || branch.IsCurrentBranch) return;

        var branchName = branch.IsRemote
            ? branch.Name.Replace("origin/", "")
            : branch.Name;

        var success = await _gitService.CheckoutBranchAsync(branchName);
        if (success)
        {
            await RefreshAsync();
        }
        else
        {
            await _dialogService.ShowMessageAsync("Checkout Failed",
                $"Failed to checkout branch '{branchName}'",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewBranchName)) return;

        var success = await _gitService.CreateBranchAsync(NewBranchName.Trim());
        if (success)
        {
            NewBranchName = "";
            await RefreshAsync();
        }
        else
        {
            await _dialogService.ShowMessageAsync("Create Branch Failed",
                $"Failed to create branch '{NewBranchName}'",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteBranchAsync(BranchItemViewModel? branch)
    {
        if (branch == null || branch.IsCurrentBranch || branch.IsRemote) return;

        var result = await _dialogService.ShowMessageAsync("Delete Branch",
            $"Are you sure you want to delete branch '{branch.Name}'?",
            DialogButtons.YesNo, DialogIcon.Question);

        if (result == DialogResult.Yes)
        {
            var success = await _gitService.DeleteBranchAsync(branch.Name);
            if (success)
            {
                await RefreshAsync();
            }
            else
            {
                // Try force delete
                result = await _dialogService.ShowMessageAsync("Delete Branch",
                    "Branch has unmerged changes. Force delete?",
                    DialogButtons.YesNo, DialogIcon.Warning);

                if (result == DialogResult.Yes)
                {
                    await _gitService.DeleteBranchAsync(branch.Name, true);
                    await RefreshAsync();
                }
            }
        }
    }

    [RelayCommand]
    private async Task MergeBranchAsync(BranchItemViewModel? branch)
    {
        if (branch == null || branch.IsCurrentBranch) return;

        var mergeResult = await _gitService.MergeBranchAsync(branch.Name);
        if (mergeResult.Success)
        {
            await _dialogService.ShowMessageAsync("Merge Complete",
                $"Successfully merged '{branch.Name}' into current branch.",
                DialogButtons.Ok, DialogIcon.Information);
        }
        else if (mergeResult.HasConflicts)
        {
            await _dialogService.ShowMessageAsync("Merge Conflicts",
                $"Merge resulted in conflicts in {mergeResult.ConflictedFiles.Count} file(s).\n" +
                "Please resolve conflicts and commit.",
                DialogButtons.Ok, DialogIcon.Warning);
        }
        else
        {
            await _dialogService.ShowMessageAsync("Merge Failed",
                mergeResult.ErrorMessage ?? "Unknown error",
                DialogButtons.Ok, DialogIcon.Error);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task FetchAsync()
    {
        IsLoading = true;
        try
        {
            await _gitService.FetchAsync();
            await RefreshAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnShowRemoteBranchesChanged(bool value)
    {
        _ = RefreshAsync();
    }
}

public partial class BranchItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _isCurrentBranch;

    [ObservableProperty]
    private bool _isRemote;

    [ObservableProperty]
    private string? _trackingBranch;

    [ObservableProperty]
    private int _ahead;

    [ObservableProperty]
    private int _behind;

    public string DisplayName => IsRemote ? Name : Name;
    public string Icon => IsCurrentBranch ? "*" : (IsRemote ? "R" : "L");
    public string StatusText
    {
        get
        {
            if (Ahead > 0 && Behind > 0)
                return $"+{Ahead} -{Behind}";
            if (Ahead > 0)
                return $"+{Ahead}";
            if (Behind > 0)
                return $"-{Behind}";
            return "";
        }
    }
}
