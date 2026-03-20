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
    /// Gets the diff for a file (unstaged changes)
    /// </summary>
    Task<string> GetDiffAsync(string filePath);

    /// <summary>
    /// Gets the staged diff for a file (changes already in the index)
    /// </summary>
    Task<string> GetStagedDiffAsync(string filePath);

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
    /// Gets the diff for a file at a specific commit (shows what changed in that commit)
    /// </summary>
    Task<string> GetFileDiffAtCommitAsync(string filePath, string commitHash);

    /// <summary>
    /// Fetches from remote
    /// </summary>
    Task<bool> FetchAsync();

    /// <summary>
    /// Gets remote URL
    /// </summary>
    Task<string?> GetRemoteUrlAsync();

    /// <summary>
    /// Renames a branch
    /// </summary>
    Task<bool> RenameBranchAsync(string oldName, string newName);

    /// <summary>
    /// Rebases current branch onto another branch
    /// </summary>
    Task<GitRebaseResult> RebaseAsync(string ontoBranch);

    /// <summary>
    /// Aborts an ongoing rebase
    /// </summary>
    Task<bool> RebaseAbortAsync();

    /// <summary>
    /// Continues a rebase after resolving conflicts
    /// </summary>
    Task<GitRebaseResult> RebaseContinueAsync();

    /// <summary>
    /// Cherry-picks a commit
    /// </summary>
    Task<GitCherryPickResult> CherryPickAsync(string commitHash);

    /// <summary>
    /// Reverts a commit
    /// </summary>
    Task<GitRevertResult> RevertCommitAsync(string commitHash);

    /// <summary>
    /// Gets all tags
    /// </summary>
    Task<IReadOnlyList<GitTagInfo>> GetTagsAsync();

    /// <summary>
    /// Creates a new tag
    /// </summary>
    Task<bool> CreateTagAsync(string tagName, string? commitHash = null, string? message = null);

    /// <summary>
    /// Deletes a tag
    /// </summary>
    Task<bool> DeleteTagAsync(string tagName, bool deleteRemote = false);

    /// <summary>
    /// Pushes tags to remote
    /// </summary>
    Task<bool> PushTagsAsync();

    /// <summary>
    /// Gets submodule information
    /// </summary>
    Task<IReadOnlyList<GitSubmoduleInfo>> GetSubmodulesAsync();

    /// <summary>
    /// Updates submodules
    /// </summary>
    Task<bool> UpdateSubmodulesAsync(bool init = true, bool recursive = true);

    /// <summary>
    /// Clones a repository
    /// </summary>
    Task<GitCloneResult> CloneAsync(string url, string destinationPath, IProgress<GitProgress>? progress = null);

    /// <summary>
    /// Gets the log for commits between two refs
    /// </summary>
    Task<IReadOnlyList<GitCommitInfo>> GetLogAsync(string? fromRef = null, string? toRef = null, int maxCount = 100);

    /// <summary>
    /// Gets ahead/behind counts for a branch relative to its tracking branch
    /// </summary>
    Task<(int ahead, int behind)> GetAheadBehindAsync(string? branchName = null);

    /// <summary>
    /// Checks out a specific commit or file
    /// </summary>
    Task<bool> CheckoutAsync(string target, string? filePath = null);

    /// <summary>
    /// Resets to a specific commit
    /// </summary>
    Task<bool> ResetAsync(string commitHash, GitResetMode mode = GitResetMode.Mixed);

    /// <summary>
    /// Gets the root directory of the repository
    /// </summary>
    Task<string?> GetRepositoryRootAsync();

    /// <summary>
    /// Gets configured remotes
    /// </summary>
    Task<IReadOnlyList<GitRemoteInfo>> GetRemotesAsync();

    /// <summary>
    /// Adds a remote
    /// </summary>
    Task<bool> AddRemoteAsync(string name, string url);

    /// <summary>
    /// Removes a remote
    /// </summary>
    Task<bool> RemoveRemoteAsync(string name);

    /// <summary>
    /// Clean untracked files
    /// </summary>
    Task<bool> CleanAsync(bool removeDirectories = false, bool force = true);

    /// <summary>
    /// Gets the content of a file at a specific commit
    /// </summary>
    Task<string?> GetFileContentAtCommitAsync(string filePath, string commitHash);

    /// <summary>
    /// Gets line-level diff information for a file (compared to last commit)
    /// </summary>
    Task<IReadOnlyList<GitLineChange>> GetLineChangesAsync(string filePath);

    /// <summary>
    /// Stages a single hunk (patch fragment) from a file's diff.
    /// The patchText must be a valid unified diff fragment with header.
    /// </summary>
    Task<bool> StageHunkAsync(string filePath, string patchText);

    /// <summary>
    /// Unstages a single hunk (patch fragment) from a file's staged diff.
    /// The patchText must be a valid unified diff fragment with header.
    /// </summary>
    Task<bool> UnstageHunkAsync(string filePath, string patchText);

    /// <summary>
    /// Raised when an operation progresses
    /// </summary>
    event EventHandler<GitProgress>? Progress;
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

public class GitLineChange
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public GitLineChangeKind Kind { get; set; }
}

public enum GitLineChangeKind
{
    Added,
    Modified,
    Deleted
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
    public bool HasConflicts { get; set; }
    public IReadOnlyList<string> ConflictedFiles { get; set; } = Array.Empty<string>();
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
    public string Message { get; set; } = "";
    public string LineContent { get; set; } = "";
    public bool IsOriginal { get; set; }

    /// <summary>
    /// Returns a human-readable relative time string (e.g., "2 days ago").
    /// </summary>
    public string RelativeDate
    {
        get
        {
            var span = DateTime.Now - Date;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minute{((int)span.TotalMinutes == 1 ? "" : "s")} ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hour{((int)span.TotalHours == 1 ? "" : "s")} ago";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} day{((int)span.TotalDays == 1 ? "" : "s")} ago";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} month{((int)(span.TotalDays / 30) == 1 ? "" : "s")} ago";
            return $"{(int)(span.TotalDays / 365)} year{((int)(span.TotalDays / 365) == 1 ? "" : "s")} ago";
        }
    }

    /// <summary>
    /// Formatted blame annotation string: "Author, time ago - message"
    /// </summary>
    public string AnnotationText
    {
        get
        {
            var msg = Message.Length > 50 ? Message[..47] + "..." : Message;
            return $"{Author}, {RelativeDate} — {msg}";
        }
    }
}

public class GitRebaseResult
{
    public bool Success { get; set; }
    public bool HasConflicts { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<string> ConflictedFiles { get; set; } = Array.Empty<string>();
    public int CommitsApplied { get; set; }
}

public class GitCherryPickResult
{
    public bool Success { get; set; }
    public bool HasConflicts { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyList<string> ConflictedFiles { get; set; } = Array.Empty<string>();
}

public class GitRevertResult
{
    public bool Success { get; set; }
    public bool HasConflicts { get; set; }
    public string? ErrorMessage { get; set; }
    public string? NewCommitHash { get; set; }
}

public class GitTagInfo
{
    public string Name { get; set; } = "";
    public string? CommitHash { get; set; }
    public string? Message { get; set; }
    public string? Tagger { get; set; }
    public DateTime? Date { get; set; }
    public bool IsAnnotated { get; set; }
}

public class GitSubmoduleInfo
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public string? CommitHash { get; set; }
    public string? Branch { get; set; }
    public bool IsInitialized { get; set; }
}

public class GitCloneResult
{
    public bool Success { get; set; }
    public string? Path { get; set; }
    public string? ErrorMessage { get; set; }
}

public class GitRemoteInfo
{
    public string Name { get; set; } = "";
    public string FetchUrl { get; set; } = "";
    public string PushUrl { get; set; } = "";
}

public enum GitResetMode
{
    Soft,
    Mixed,
    Hard
}

public class GitProgress
{
    public string Operation { get; set; } = "";
    public int? TotalObjects { get; set; }
    public int? ReceivedObjects { get; set; }
    public int? IndexedObjects { get; set; }
    public long? BytesReceived { get; set; }
    public double? ProgressPercentage { get; set; }
    public string? Message { get; set; }
}

