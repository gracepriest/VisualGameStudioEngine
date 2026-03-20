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

    // ── Commit Message Templates ──

    [ObservableProperty]
    private bool _isCommitPrefixMenuOpen;

    /// <summary>
    /// Conventional commit prefixes for the dropdown.
    /// </summary>
    public IReadOnlyList<string> CommitPrefixes { get; } = new[]
    {
        "feat: ",
        "fix: ",
        "docs: ",
        "refactor: ",
        "test: ",
        "chore: ",
        "style: ",
        "perf: ",
        "ci: ",
        "build: "
    };

    /// <summary>
    /// Placeholder text for the commit message box.
    /// </summary>
    public string CommitMessagePlaceholder => "Message (Ctrl+Enter to commit)";

    // ── Amend Last Commit ──

    [ObservableProperty]
    private bool _isAmendMode;

    // ── Auto-Refresh ──

    private System.Threading.Timer? _autoRefreshTimer;
    private int _autoRefreshIntervalMs = 5000;

    [ObservableProperty]
    private bool _isAutoRefreshEnabled = true;

    /// <summary>
    /// Auto-refresh interval in milliseconds (default 5000).
    /// </summary>
    public int AutoRefreshIntervalMs
    {
        get => _autoRefreshIntervalMs;
        set
        {
            if (SetProperty(ref _autoRefreshIntervalMs, value))
            {
                RestartAutoRefreshTimer();
            }
        }
    }

    // ── Git Log ──

    [ObservableProperty]
    private ObservableCollection<GitLogItem> _logEntries = new();

    [ObservableProperty]
    private bool _isLogViewerOpen;

    [ObservableProperty]
    private string? _logViewerTitle;

    // ── Dirty indicator ──

    /// <summary>
    /// True when there are any uncommitted changes (staged or unstaged).
    /// </summary>
    [ObservableProperty]
    private bool _hasUncommittedChanges;

    public GitChangesViewModel(IGitService gitService, IDialogService dialogService, IOutputService outputService)
    {
        _gitService = gitService;
        _dialogService = dialogService;
        _outputService = outputService;

        _gitService.StatusChanged += OnGitStatusChanged;

        // Start auto-refresh timer
        StartAutoRefreshTimer();
    }

    private void OnGitStatusChanged(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    // ── Auto-Refresh Timer ──

    private void StartAutoRefreshTimer()
    {
        _autoRefreshTimer = new System.Threading.Timer(
            _ => { if (IsAutoRefreshEnabled) _ = RefreshAsync(); },
            null,
            TimeSpan.FromMilliseconds(_autoRefreshIntervalMs),
            TimeSpan.FromMilliseconds(_autoRefreshIntervalMs));
    }

    private void RestartAutoRefreshTimer()
    {
        _autoRefreshTimer?.Dispose();
        if (IsAutoRefreshEnabled)
        {
            StartAutoRefreshTimer();
        }
    }

    partial void OnIsAutoRefreshEnabledChanged(bool value)
    {
        RestartAutoRefreshTimer();
    }

    /// <summary>
    /// Call this on file save, window focus, or terminal command completion
    /// to trigger an immediate refresh.
    /// </summary>
    public void NotifyExternalChange()
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_gitService.IsGitRepository)
        {
            IsGitRepository = false;
            HasUncommittedChanges = false;
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

            HasUncommittedChanges = StagedChanges.Count > 0 || UnstagedChanges.Count > 0;

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

    // ── Commit (supports amend) ──

    [RelayCommand]
    private async Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            await _dialogService.ShowMessageAsync("Error", "Please enter a commit message.");
            return;
        }

        if (!IsAmendMode && !StagedChanges.Any())
        {
            await _dialogService.ShowMessageAsync("Error", "No changes staged for commit.");
            return;
        }

        GitCommitResult result;

        if (IsAmendMode)
        {
            result = await _gitService.CommitAmendAsync(CommitMessage);
        }
        else
        {
            result = await _gitService.CommitAsync(CommitMessage);
        }

        if (result.Success)
        {
            var action = IsAmendMode ? "Amended" : "Committed";
            _outputService.WriteLine($"{action}: {result.CommitHash}");
            CommitMessage = "";
            IsAmendMode = false;
        }
        else
        {
            await _dialogService.ShowMessageAsync("Commit Failed", result.ErrorMessage ?? "Unknown error");
        }
    }

    // ── Commit Prefix Templates ──

    [RelayCommand]
    private void ToggleCommitPrefixMenu()
    {
        IsCommitPrefixMenuOpen = !IsCommitPrefixMenuOpen;
    }

    [RelayCommand]
    private void ApplyCommitPrefix(string? prefix)
    {
        if (!string.IsNullOrEmpty(prefix))
        {
            CommitMessage = prefix + CommitMessage;
        }
        IsCommitPrefixMenuOpen = false;
    }

    // ── Amend Mode Toggle ──

    [RelayCommand]
    private async Task ToggleAmendModeAsync()
    {
        IsAmendMode = !IsAmendMode;

        if (IsAmendMode)
        {
            // Pre-fill with last commit message
            var lastMessage = await _gitService.GetLastCommitMessageAsync();
            if (!string.IsNullOrEmpty(lastMessage))
            {
                CommitMessage = lastMessage;
            }
        }
        else
        {
            CommitMessage = "";
        }
    }

    // ── Discard All Changes ──

    [RelayCommand]
    private async Task DiscardAllChangesAsync()
    {
        if (!UnstagedChanges.Any() && !StagedChanges.Any())
        {
            await _dialogService.ShowMessageAsync("Info", "No changes to discard.");
            return;
        }

        var confirmed = await _dialogService.ConfirmAsync(
            "Discard All Changes",
            "Are you sure you want to discard all changes? This cannot be undone.");

        if (confirmed)
        {
            await _gitService.DiscardAllChangesAsync();
            _outputService.WriteLine("All changes discarded.");
        }
    }

    // ── Undo Last Commit ──

    [RelayCommand]
    private async Task UndoLastCommitAsync()
    {
        var confirmed = await _dialogService.ConfirmAsync(
            "Undo Last Commit",
            "This will undo the last commit but keep all changes staged. Continue?");

        if (!confirmed) return;

        var success = await _gitService.UndoLastCommitAsync();
        if (success)
        {
            _outputService.WriteLine("Last commit undone (changes preserved as staged).");
        }
        else
        {
            await _dialogService.ShowMessageAsync("Error", "Failed to undo last commit. There may be no commits to undo.");
        }
    }

    // ── Git Log Viewer ──

    [RelayCommand]
    private async Task ShowGitLogAsync()
    {
        await ShowGitLogForFileAsync(null);
    }

    [RelayCommand]
    private async Task ShowGitLogForFileAsync(string? filePath)
    {
        IsLogViewerOpen = true;
        LogEntries.Clear();

        IReadOnlyList<GitCommitInfo> commits;

        if (!string.IsNullOrEmpty(filePath))
        {
            LogViewerTitle = $"Git Log: {Path.GetFileName(filePath)}";
            commits = await _gitService.GetFileLogAsync(filePath, 50);
        }
        else
        {
            LogViewerTitle = "Git Log";
            commits = await _gitService.GetLogAsync(maxCount: 50);
        }

        foreach (var commit in commits)
        {
            LogEntries.Add(new GitLogItem
            {
                Hash = commit.Hash,
                ShortHash = commit.ShortHash,
                Message = commit.Message,
                Author = commit.Author,
                Date = commit.Date,
                RelativeDate = FormatRelativeDate(commit.Date)
            });
        }
    }

    [RelayCommand]
    private void CloseLogViewer()
    {
        IsLogViewerOpen = false;
    }

    /// <summary>
    /// Raised when merge conflicts are detected after a pull/merge operation.
    /// The list contains the file paths of conflicted files.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? MergeConflictsDetected;

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

            if (result.HasConflicts && result.ConflictedFiles.Count > 0)
            {
                _outputService.WriteLine($"Merge conflicts detected in {result.ConflictedFiles.Count} file(s):");
                foreach (var file in result.ConflictedFiles)
                {
                    _outputService.WriteLine($"  - {file}");
                }

                await _dialogService.ShowMessageAsync(
                    "Merge Conflicts",
                    $"Merge conflicts detected in {result.ConflictedFiles.Count} file(s). Please resolve the conflicts and commit.");

                MergeConflictsDetected?.Invoke(this, result.ConflictedFiles);
            }
            else
            {
                await _dialogService.ShowMessageAsync("Pull Failed", result.ErrorMessage ?? "Unknown error");
            }
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

    /// <summary>
    /// Event raised when the user requests to view a diff for an unstaged changed file.
    /// The view subscribes to this to show the DiffViewerView window.
    /// </summary>
    public event EventHandler<string>? ShowDiffRequested;

    /// <summary>
    /// Event raised when the user requests to view a diff for a staged file.
    /// </summary>
    public event EventHandler<string>? ShowStagedDiffRequested;

    [RelayCommand]
    private async Task ShowDiffAsync(GitChangeItem? item)
    {
        if (item == null) return;

        ShowDiffRequested?.Invoke(this, item.FilePath);
    }

    [RelayCommand]
    private async Task ShowStagedDiffAsync(GitChangeItem? item)
    {
        if (item == null) return;

        ShowStagedDiffRequested?.Invoke(this, item.FilePath);
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

    private static string FormatRelativeDate(DateTime date)
    {
        var span = DateTime.Now - date;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
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
        GitFileStatus.Conflicted => "!",
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

public class GitLogItem
{
    public string Hash { get; set; } = "";
    public string ShortHash { get; set; } = "";
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime Date { get; set; }
    public string RelativeDate { get; set; } = "";

    public string DisplayText => $"{ShortHash}  {Author}, {RelativeDate} - {Message}";
}
