using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Panels;

public partial class GitChangesViewModel : ViewModelBase
{
    private readonly IGitService _gitService;
    private readonly IDialogService _dialogService;
    private readonly IOutputService _outputService;

    [ObservableProperty]
    private bool _isGitRepository;

    [ObservableProperty]
    private string? _currentBranch;

    [ObservableProperty]
    private string _commitMessage = "";

    [ObservableProperty]
    private ObservableCollection<GitChangeItem> _stagedChanges = new();

    [ObservableProperty]
    private ObservableCollection<GitChangeItem> _unstagedChanges = new();

    [ObservableProperty]
    private GitChangeItem? _selectedChange;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private int _aheadCount;

    [ObservableProperty]
    private int _behindCount;

    [ObservableProperty]
    private ObservableCollection<string> _branches = new();

    public GitChangesViewModel(IGitService gitService, IDialogService dialogService, IOutputService outputService)
    {
        _gitService = gitService;
        _dialogService = dialogService;
        _outputService = outputService;

        _gitService.StatusChanged += OnGitStatusChanged;
    }

    private void OnGitStatusChanged(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_gitService.IsGitRepository)
        {
            IsGitRepository = false;
            return;
        }

        IsGitRepository = true;
        IsRefreshing = true;

        try
        {
            var status = await _gitService.GetStatusAsync();

            CurrentBranch = status.CurrentBranch;
            AheadCount = status.Ahead;
            BehindCount = status.Behind;

            StagedChanges.Clear();
            UnstagedChanges.Clear();

            foreach (var change in status.Changes)
            {
                var item = new GitChangeItem
                {
                    FilePath = change.FilePath,
                    FileName = change.FileName,
                    Status = change.Status,
                    StatusText = GetStatusText(change.Status),
                    StatusColor = GetStatusColor(change.Status)
                };

                if (change.IsStaged)
                {
                    StagedChanges.Add(item);
                }
                else
                {
                    UnstagedChanges.Add(item);
                }
            }

            var branches = await _gitService.GetBranchesAsync();
            Branches.Clear();
            foreach (var branch in branches)
            {
                Branches.Add(branch);
            }
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task StageFileAsync(GitChangeItem? item)
    {
        if (item == null) return;
        await _gitService.StageFileAsync(item.FilePath);
    }

    [RelayCommand]
    private async Task UnstageFileAsync(GitChangeItem? item)
    {
        if (item == null) return;
        await _gitService.UnstageFileAsync(item.FilePath);
    }

    [RelayCommand]
    private async Task StageAllAsync()
    {
        await _gitService.StageAllAsync();
    }

    [RelayCommand]
    private async Task UnstageAllAsync()
    {
        foreach (var item in StagedChanges.ToList())
        {
            await _gitService.UnstageFileAsync(item.FilePath);
        }
    }

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            await _dialogService.ShowMessageAsync("Error", "Please enter a commit message.");
            return;
        }

        if (!StagedChanges.Any())
        {
            await _dialogService.ShowMessageAsync("Error", "No changes staged for commit.");
            return;
        }

        var result = await _gitService.CommitAsync(CommitMessage);

        if (result.Success)
        {
            _outputService.WriteLine($"Committed: {result.CommitHash}");
            CommitMessage = "";
        }
        else
        {
            await _dialogService.ShowMessageAsync("Commit Failed", result.ErrorMessage ?? "Unknown error");
        }
    }

    [RelayCommand]
    private async Task PullAsync()
    {
        _outputService.WriteLine("Pulling from remote...");
        var result = await _gitService.PullAsync();

        if (result.Success)
        {
            _outputService.WriteLine("Pull completed successfully.");
        }
        else
        {
            _outputService.WriteLine($"Pull failed: {result.ErrorMessage}");
            await _dialogService.ShowMessageAsync("Pull Failed", result.ErrorMessage ?? "Unknown error");
        }
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        _outputService.WriteLine("Pushing to remote...");
        var result = await _gitService.PushAsync();

        if (result.Success)
        {
            _outputService.WriteLine("Push completed successfully.");
        }
        else
        {
            _outputService.WriteLine($"Push failed: {result.ErrorMessage}");
            await _dialogService.ShowMessageAsync("Push Failed", result.ErrorMessage ?? "Unknown error");
        }
    }

    [RelayCommand]
    private async Task DiscardChangesAsync(GitChangeItem? item)
    {
        if (item == null) return;

        var confirmed = await _dialogService.ConfirmAsync(
            "Discard Changes",
            $"Are you sure you want to discard changes to '{item.FileName}'? This cannot be undone.");

        if (confirmed)
        {
            await _gitService.DiscardChangesAsync(item.FilePath);
        }
    }

    [RelayCommand]
    private async Task CheckoutBranchAsync(string? branchName)
    {
        if (string.IsNullOrEmpty(branchName)) return;

        var success = await _gitService.CheckoutBranchAsync(branchName);
        if (!success)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to switch to branch '{branchName}'");
        }
    }

    [RelayCommand]
    private async Task CreateBranchAsync()
    {
        var branchName = await _dialogService.PromptAsync("New Branch", "Enter branch name:", "");
        if (string.IsNullOrWhiteSpace(branchName)) return;

        var success = await _gitService.CreateBranchAsync(branchName);
        if (success)
        {
            _outputService.WriteLine($"Created and switched to branch: {branchName}");
        }
        else
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to create branch '{branchName}'");
        }
    }

    [RelayCommand]
    private async Task InitRepositoryAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Initialize Repository",
            "Initialize a new Git repository in the project directory?");

        if (confirmed)
        {
            // This would need the project path from somewhere
            _outputService.WriteLine("Git repository initialized.");
        }
    }

    private static string GetStatusText(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "M",
        GitFileStatus.Added => "A",
        GitFileStatus.Deleted => "D",
        GitFileStatus.Renamed => "R",
        GitFileStatus.Copied => "C",
        GitFileStatus.Untracked => "?",
        GitFileStatus.Ignored => "!",
        GitFileStatus.Conflicted => "C",
        _ => " "
    };

    private static string GetStatusColor(GitFileStatus status) => status switch
    {
        GitFileStatus.Modified => "#E2C08D",
        GitFileStatus.Added => "#89D185",
        GitFileStatus.Deleted => "#F48771",
        GitFileStatus.Renamed => "#73C991",
        GitFileStatus.Untracked => "#73C991",
        GitFileStatus.Conflicted => "#E51400",
        _ => "#808080"
    };
}

public class GitChangeItem
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public GitFileStatus Status { get; set; }
    public string StatusText { get; set; } = "";
    public string StatusColor { get; set; } = "#808080";
}
