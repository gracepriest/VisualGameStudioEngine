using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class GitServiceTests
{
    private GitService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new GitService();
    }

    #region Initial State Tests

    [Test]
    public void Constructor_ChecksGitAvailability()
    {
        // Git availability depends on system, so we just verify the property is set
        Assert.That(_service.IsGitAvailable, Is.TypeOf<bool>());
    }

    [Test]
    public void InitialState_IsNotGitRepository()
    {
        Assert.That(_service.IsGitRepository, Is.False);
    }

    [Test]
    public void InitialState_CurrentBranchIsNull()
    {
        Assert.That(_service.CurrentBranch, Is.Null);
    }

    [Test]
    public void InitialState_PendingChangesCountIsZero()
    {
        Assert.That(_service.PendingChangesCount, Is.EqualTo(0));
    }

    #endregion

    #region GetStatusAsync Tests (when not initialized)

    [Test]
    public async Task GetStatusAsync_WhenNotInitialized_ReturnsEmptyStatus()
    {
        var status = await _service.GetStatusAsync();

        Assert.That(status, Is.Not.Null);
        Assert.That(status.Changes, Is.Empty);
    }

    [Test]
    public async Task GetFileStatusAsync_WhenNotInitialized_ReturnsUnmodified()
    {
        var status = await _service.GetFileStatusAsync("/some/path/file.bas");

        Assert.That(status, Is.EqualTo(GitFileStatus.Unmodified));
    }

    #endregion

    #region Branch Operations Tests (when not initialized)

    [Test]
    public async Task GetBranchesAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var branches = await _service.GetBranchesAsync();

        Assert.That(branches, Is.Empty);
    }

    [Test]
    public async Task CheckoutBranchAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.CheckoutBranchAsync("main");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task CreateBranchAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.CreateBranchAsync("new-branch");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DeleteBranchAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.DeleteBranchAsync("branch-to-delete");

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetBranchInfoAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var branches = await _service.GetBranchInfoAsync();

        Assert.That(branches, Is.Empty);
    }

    #endregion

    #region Commit Operations Tests (when not initialized)

    [Test]
    public async Task CommitAsync_WhenNotInitialized_ReturnsFailure()
    {
        var result = await _service.CommitAsync("Test commit");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Not.Null);
    }

    [Test]
    public async Task GetRecentCommitsAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var commits = await _service.GetRecentCommitsAsync();

        Assert.That(commits, Is.Empty);
    }

    #endregion

    #region Remote Operations Tests (when not initialized)

    [Test]
    public async Task PullAsync_WhenNotInitialized_ReturnsFailure()
    {
        var result = await _service.PullAsync();

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task PushAsync_WhenNotInitialized_ReturnsFailure()
    {
        var result = await _service.PushAsync();

        Assert.That(result.Success, Is.False);
    }

    [Test]
    public async Task FetchAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.FetchAsync();

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetRemoteUrlAsync_WhenNotInitialized_ReturnsNull()
    {
        var url = await _service.GetRemoteUrlAsync();

        Assert.That(url, Is.Null);
    }

    #endregion

    #region Merge Operations Tests (when not initialized)

    [Test]
    public async Task MergeBranchAsync_WhenNotInitialized_ReturnsFailure()
    {
        var result = await _service.MergeBranchAsync("feature-branch");

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Not.Null);
    }

    #endregion

    #region Stash Operations Tests (when not initialized)

    [Test]
    public async Task StashAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.StashAsync();

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task GetStashesAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var stashes = await _service.GetStashesAsync();

        Assert.That(stashes, Is.Empty);
    }

    [Test]
    public async Task ApplyStashAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.ApplyStashAsync(0);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task DropStashAsync_WhenNotInitialized_ReturnsFalse()
    {
        var result = await _service.DropStashAsync(0);

        Assert.That(result, Is.False);
    }

    #endregion

    #region Diff and Blame Tests (when not initialized)

    [Test]
    public async Task GetDiffAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var diff = await _service.GetDiffAsync("/path/to/file.bas");

        Assert.That(diff, Is.Empty);
    }

    [Test]
    public async Task GetBlameAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var blame = await _service.GetBlameAsync("/path/to/file.bas");

        Assert.That(blame, Is.Empty);
    }

    [Test]
    public async Task GetFileHistoryAsync_WhenNotInitialized_ReturnsEmpty()
    {
        var history = await _service.GetFileHistoryAsync("/path/to/file.bas");

        Assert.That(history, Is.Empty);
    }

    #endregion

    #region Event Tests

    [Test]
    public void StatusChanged_Event_CanBeSubscribed()
    {
        _service.StatusChanged += (s, e) => { };
        Assert.Pass("Event subscription successful");
    }

    #endregion
}

#region Git Model Tests

[TestFixture]
public class GitStatusTests
{
    [Test]
    public void DefaultStatus_HasEmptyValues()
    {
        var status = new GitStatus();

        Assert.That(status.CurrentBranch, Is.Null);
        Assert.That(status.Changes, Is.Empty);
        Assert.That(status.Ahead, Is.EqualTo(0));
        Assert.That(status.Behind, Is.EqualTo(0));
    }

    [Test]
    public void Status_CanSetAllProperties()
    {
        var status = new GitStatus
        {
            CurrentBranch = "main",
            Ahead = 2,
            Behind = 1
        };
        status.Changes.Add(new GitFileChange { FilePath = "test.bas" });

        Assert.That(status.CurrentBranch, Is.EqualTo("main"));
        Assert.That(status.Ahead, Is.EqualTo(2));
        Assert.That(status.Behind, Is.EqualTo(1));
        Assert.That(status.Changes, Has.Count.EqualTo(1));
    }
}

[TestFixture]
public class GitFileChangeTests
{
    [Test]
    public void DefaultChange_HasDefaultValues()
    {
        var change = new GitFileChange();

        Assert.That(change.FilePath, Is.EqualTo(""));
        Assert.That(change.Status, Is.EqualTo(GitFileStatus.Unmodified));
        Assert.That(change.IsStaged, Is.False);
    }

    [Test]
    public void Change_CanSetAllProperties()
    {
        var change = new GitFileChange
        {
            FilePath = "/path/to/file.bas",
            Status = GitFileStatus.Modified,
            IsStaged = true
        };

        Assert.That(change.FilePath, Is.EqualTo("/path/to/file.bas"));
        Assert.That(change.Status, Is.EqualTo(GitFileStatus.Modified));
        Assert.That(change.IsStaged, Is.True);
    }
}

[TestFixture]
public class GitCommitResultTests
{
    [Test]
    public void DefaultResult_HasDefaultValues()
    {
        var result = new GitCommitResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.CommitHash, Is.Null);
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public void SuccessfulResult_HasCommitHash()
    {
        var result = new GitCommitResult
        {
            Success = true,
            CommitHash = "abc123def"
        };

        Assert.That(result.Success, Is.True);
        Assert.That(result.CommitHash, Is.EqualTo("abc123def"));
    }

    [Test]
    public void FailedResult_HasErrorMessage()
    {
        var result = new GitCommitResult
        {
            Success = false,
            ErrorMessage = "Nothing to commit"
        };

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.EqualTo("Nothing to commit"));
    }
}

[TestFixture]
public class GitPullResultTests
{
    [Test]
    public void DefaultResult_HasDefaultValues()
    {
        var result = new GitPullResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Null);
    }
}

[TestFixture]
public class GitPushResultTests
{
    [Test]
    public void DefaultResult_HasDefaultValues()
    {
        var result = new GitPushResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Is.Null);
    }
}

[TestFixture]
public class GitMergeResultTests
{
    [Test]
    public void DefaultResult_HasDefaultValues()
    {
        var result = new GitMergeResult();

        Assert.That(result.Success, Is.False);
        Assert.That(result.HasConflicts, Is.False);
        Assert.That(result.ConflictedFiles, Is.Empty);
        Assert.That(result.ErrorMessage, Is.Null);
    }

    [Test]
    public void ConflictResult_HasConflictedFiles()
    {
        var result = new GitMergeResult
        {
            Success = false,
            HasConflicts = true,
            ConflictedFiles = new List<string> { "file1.bas", "file2.bas" }
        };

        Assert.That(result.HasConflicts, Is.True);
        Assert.That(result.ConflictedFiles, Has.Count.EqualTo(2));
    }
}

[TestFixture]
public class GitCommitInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new GitCommitInfo();

        Assert.That(info.Hash, Is.EqualTo(""));
        Assert.That(info.Message, Is.EqualTo(""));
        Assert.That(info.Author, Is.EqualTo(""));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var date = DateTime.Now;
        var info = new GitCommitInfo
        {
            Hash = "abc123",
            Message = "Test commit",
            Author = "Test Author",
            Date = date
        };

        Assert.That(info.Hash, Is.EqualTo("abc123"));
        Assert.That(info.Message, Is.EqualTo("Test commit"));
        Assert.That(info.Author, Is.EqualTo("Test Author"));
        Assert.That(info.Date, Is.EqualTo(date));
    }
}

[TestFixture]
public class GitBranchInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new GitBranchInfo();

        Assert.That(info.Name, Is.EqualTo(""));
        Assert.That(info.IsCurrentBranch, Is.False);
        Assert.That(info.IsRemote, Is.False);
        Assert.That(info.TrackingBranch, Is.Null);
        Assert.That(info.Ahead, Is.EqualTo(0));
        Assert.That(info.Behind, Is.EqualTo(0));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new GitBranchInfo
        {
            Name = "feature/test",
            IsCurrentBranch = true,
            IsRemote = false,
            TrackingBranch = "origin/feature/test",
            Ahead = 3,
            Behind = 1
        };

        Assert.That(info.Name, Is.EqualTo("feature/test"));
        Assert.That(info.IsCurrentBranch, Is.True);
        Assert.That(info.TrackingBranch, Is.EqualTo("origin/feature/test"));
        Assert.That(info.Ahead, Is.EqualTo(3));
        Assert.That(info.Behind, Is.EqualTo(1));
    }
}

[TestFixture]
public class GitStashInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new GitStashInfo();

        Assert.That(info.Index, Is.EqualTo(0));
        Assert.That(info.Message, Is.EqualTo(""));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var date = DateTime.Now;
        var info = new GitStashInfo
        {
            Index = 2,
            Message = "WIP on feature",
            Date = date
        };

        Assert.That(info.Index, Is.EqualTo(2));
        Assert.That(info.Message, Is.EqualTo("WIP on feature"));
        Assert.That(info.Date, Is.EqualTo(date));
    }
}

[TestFixture]
public class GitBlameLineTests
{
    [Test]
    public void DefaultLine_HasDefaultValues()
    {
        var line = new GitBlameLine();

        Assert.That(line.LineNumber, Is.EqualTo(0));
        Assert.That(line.CommitHash, Is.EqualTo(""));
        Assert.That(line.Author, Is.EqualTo(""));
        Assert.That(line.LineContent, Is.EqualTo(""));
    }

    [Test]
    public void Line_CanSetAllProperties()
    {
        var date = DateTime.Now;
        var line = new GitBlameLine
        {
            LineNumber = 42,
            CommitHash = "abc123",
            Author = "Test Author",
            Date = date,
            LineContent = "    Dim x As Integer"
        };

        Assert.That(line.LineNumber, Is.EqualTo(42));
        Assert.That(line.CommitHash, Is.EqualTo("abc123"));
        Assert.That(line.Author, Is.EqualTo("Test Author"));
        Assert.That(line.Date, Is.EqualTo(date));
        Assert.That(line.LineContent, Is.EqualTo("    Dim x As Integer"));
    }
}

[TestFixture]
public class GitFileStatusEnumTests
{
    [Test]
    public void AllStatusValues_AreDefined()
    {
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Unmodified), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Modified), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Added), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Deleted), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Renamed), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Copied), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Untracked), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Ignored), Is.True);
        Assert.That(Enum.IsDefined(typeof(GitFileStatus), GitFileStatus.Conflicted), Is.True);
    }
}

#endregion
