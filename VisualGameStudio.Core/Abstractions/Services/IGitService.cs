namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Service for Git operations
/// </summary>
public interface IGitService
{
    /// <summary>
    /// Gets whether Git is available on the system
    /// </summary>
    bool IsGitAvailable { get; }

    /// <summary>
    /// Gets whether the current project is in a Git repository
    /// </summary>
    bool IsGitRepository { get; }

    /// <summary>
    /// Gets the current branch name
    /// </summary>
    string? CurrentBranch { get; }

    /// <summary>
    /// Gets pending changes count
    /// </summary>
    int PendingChangesCount { get; }

    /// <summary>
    /// Event raised when Git status changes
    /// </summary>
    event EventHandler? StatusChanged;

    /// <summary>
    /// Initializes Git service for the given repository path
    /// </summary>
    Task InitializeAsync(string repositoryPath);

    /// <summary>
    /// Gets the current Git status
    /// </summary>
    Task<GitStatus> GetStatusAsync();

    /// <summary>
    /// Gets the status of a specific file
    /// </summary>
    Task<GitFileStatus> GetFileStatusAsync(string filePath);

    /// <summary>
    /// Stages a file for commit
    /// </summary>
    Task StageFileAsync(string filePath);

    /// <summary>
    /// Stages all modified files
    /// </summary>
    Task StageAllAsync();

    /// <summary>
    /// Unstages a file
    /// </summary>
    Task UnstageFileAsync(string filePath);

    /// <summary>
    /// Commits staged changes
    /// </summary>
    Task<GitCommitResult> CommitAsync(string message);

    /// <summary>
    /// Gets the list of branches
    /// </summary>
    Task<IReadOnlyList<string>> GetBranchesAsync();

    /// <summary>
    /// Switches to a different branch
    /// </summary>
    Task<bool> CheckoutBranchAsync(string branchName);

    /// <summary>
    /// Creates a new branch
    /// </summary>
    Task<bool> CreateBranchAsync(string branchName);

    /// <summary>
    /// Pulls changes from remote
    /// </summary>
    Task<GitPullResult> PullAsync();

    /// <summary>
    /// Pushes changes to remote
    /// </summary>
    Task<GitPushResult> PushAsync();

    /// <summary>
    /// Initializes a new Git repository
    /// </summary>
    Task<bool> InitRepositoryAsync(string path);

    /// <summary>
    /// Discards changes in a file
    /// </summary>
    Task DiscardChangesAsync(string filePath);

    /// <summary>
    /// Gets the diff for a file
    /// </summary>
    Task<string> GetDiffAsync(string filePath);

    /// <summary>
    /// Gets recent commits
    /// </summary>
    Task<IReadOnlyList<GitCommitInfo>> GetRecentCommitsAsync(int count = 20);

    /// <summary>
    /// Refreshes Git status
    /// </summary>
    Task RefreshAsync();

    /// <summary>
    /// Deletes a branch
    /// </summary>
    Task<bool> DeleteBranchAsync(string branchName, bool force = false);

    /// <summary>
    /// Merges a branch into the current branch
    /// </summary>
    Task<GitMergeResult> MergeBranchAsync(string branchName);

    /// <summary>
    /// Gets detailed branch information
    /// </summary>
    Task<IReadOnlyList<GitBranchInfo>> GetBranchInfoAsync();

    /// <summary>
    /// Stashes current changes
    /// </summary>
    Task<bool> StashAsync(string? message = null);

    /// <summary>
    /// Lists all stashes
    /// </summary>
    Task<IReadOnlyList<GitStashInfo>> GetStashesAsync();

    /// <summary>
    /// Applies a stash
    /// </summary>
    Task<bool> ApplyStashAsync(int index, bool pop = false);

    /// <summary>
    /// Drops a stash
    /// </summary>
    Task<bool> DropStashAsync(int index);

    /// <summary>
    /// Gets blame information for a file
    /// </summary>
    Task<IReadOnlyList<GitBlameLine>> GetBlameAsync(string filePath);

    /// <summary>
    /// Gets file history (commits that touched the file)
    /// </summary>
    Task<IReadOnlyList<GitCommitInfo>> GetFileHistoryAsync(string filePath, int maxCount = 50);

    /// <summary>
    /// Fetches from remote
    /// </summary>
    Task<bool> FetchAsync();

    /// <summary>
    /// Gets remote URL
    /// </summary>
    Task<string?> GetRemoteUrlAsync();
}

public class GitStatus
{
    public List<GitFileChange> Changes { get; set; } = new();
    public string? CurrentBranch { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public bool HasUncommittedChanges => Changes.Any();
}

public class GitFileChange
{
    public string FilePath { get; set; } = "";
    public string FileName => Path.GetFileName(FilePath);
    public GitFileStatus Status { get; set; }
    public bool IsStaged { get; set; }
}

public enum GitFileStatus
{
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Ignored,
    Conflicted
}

public class GitCommitResult
{
    public bool Success { get; set; }
    public string? CommitHash { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitPullResult
{
    public bool Success { get; set; }
    public int CommitsPulled { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitPushResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitCommitInfo
{
    public string Hash { get; set; } = "";
    public string ShortHash => Hash.Length >= 7 ? Hash[..7] : Hash;
    public string Message { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime Date { get; set; }
}

public class GitMergeResult
{
    public bool Success { get; set; }
    public bool HasConflicts { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<string> ConflictedFiles { get; set; } = Array.Empty<string>();
}

public class GitBranchInfo
{
    public string Name { get; set; } = "";
    public bool IsCurrentBranch { get; set; }
    public bool IsRemote { get; set; }
    public string? TrackingBranch { get; set; }
    public int Ahead { get; set; }
    public int Behind { get; set; }
    public string? LastCommitHash { get; set; }
    public DateTime? LastCommitDate { get; set; }
}

public class GitStashInfo
{
    public int Index { get; set; }
    public string Message { get; set; } = "";
    public string BranchName { get; set; } = "";
    public DateTime Date { get; set; }
}

public class GitBlameLine
{
    public int LineNumber { get; set; }
    public string CommitHash { get; set; } = "";
    public string ShortHash => CommitHash.Length >= 7 ? CommitHash[..7] : CommitHash;
    public string Author { get; set; } = "";
    public DateTime Date { get; set; }
    public string LineContent { get; set; } = "";
    public bool IsOriginal { get; set; }
}
