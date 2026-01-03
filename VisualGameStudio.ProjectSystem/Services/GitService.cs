using System.Diagnostics;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Git integration service using git command-line
/// </summary>
public class GitService : IGitService
{
    private string? _repositoryPath;
    private string? _currentBranch;
    private int _pendingChangesCount;
    private bool _isGitAvailable;
    private bool _isGitRepository;

    public bool IsGitAvailable => _isGitAvailable;
    public bool IsGitRepository => _isGitRepository;
    public string? CurrentBranch => _currentBranch;
    public int PendingChangesCount => _pendingChangesCount;

    public event EventHandler? StatusChanged;
    public event EventHandler<GitProgress>? Progress;

    public GitService()
    {
        CheckGitAvailability();
    }

    private void CheckGitAvailability()
    {
        try
        {
            var result = RunGitCommand("--version", null);
            _isGitAvailable = result.ExitCode == 0;
        }
        catch
        {
            _isGitAvailable = false;
        }
    }

    public async Task InitializeAsync(string repositoryPath)
    {
        _repositoryPath = repositoryPath;

        if (!_isGitAvailable)
            return;

        // Check if this is a git repository
        _isGitRepository = Directory.Exists(Path.Combine(repositoryPath, ".git"));

        if (_isGitRepository)
        {
            await RefreshAsync();
        }
    }

    public async Task<GitStatus> GetStatusAsync()
    {
        var status = new GitStatus();

        if (!_isGitRepository || _repositoryPath == null)
            return status;

        // Get current branch
        var branchResult = await RunGitCommandAsync("rev-parse --abbrev-ref HEAD");
        if (branchResult.ExitCode == 0)
        {
            status.CurrentBranch = branchResult.Output.Trim();
            _currentBranch = status.CurrentBranch;
        }

        // Get status
        var statusResult = await RunGitCommandAsync("status --porcelain=v1");
        if (statusResult.ExitCode == 0)
        {
            var lines = statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Length < 3) continue;

                var indexStatus = line[0];
                var workingStatus = line[1];
                var filePath = line.Substring(3).Trim();

                var change = new GitFileChange
                {
                    FilePath = filePath,
                    IsStaged = indexStatus != ' ' && indexStatus != '?'
                };

                change.Status = ParseStatus(indexStatus, workingStatus);
                status.Changes.Add(change);
            }
        }

        _pendingChangesCount = status.Changes.Count;

        // Get ahead/behind
        var trackingResult = await RunGitCommandAsync("rev-list --left-right --count @{u}...HEAD");
        if (trackingResult.ExitCode == 0)
        {
            var parts = trackingResult.Output.Trim().Split('\t');
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out var behind);
                int.TryParse(parts[1], out var ahead);
                status.Behind = behind;
                status.Ahead = ahead;
            }
        }

        return status;
    }

    private GitFileStatus ParseStatus(char index, char working)
    {
        if (index == '?' && working == '?') return GitFileStatus.Untracked;
        if (index == '!' && working == '!') return GitFileStatus.Ignored;
        if (index == 'A' || working == 'A') return GitFileStatus.Added;
        if (index == 'D' || working == 'D') return GitFileStatus.Deleted;
        if (index == 'R' || working == 'R') return GitFileStatus.Renamed;
        if (index == 'C' || working == 'C') return GitFileStatus.Copied;
        if (index == 'U' || working == 'U') return GitFileStatus.Conflicted;
        if (index == 'M' || working == 'M') return GitFileStatus.Modified;
        return GitFileStatus.Unmodified;
    }

    public async Task<GitFileStatus> GetFileStatusAsync(string filePath)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return GitFileStatus.Unmodified;

        var relativePath = GetRelativePath(filePath);
        var result = await RunGitCommandAsync($"status --porcelain -- \"{relativePath}\"");

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output))
        {
            var line = result.Output.Trim();
            if (line.Length >= 2)
            {
                return ParseStatus(line[0], line[1]);
            }
        }

        return GitFileStatus.Unmodified;
    }

    public async Task StageFileAsync(string filePath)
    {
        if (!_isGitRepository || _repositoryPath == null) return;

        var relativePath = GetRelativePath(filePath);
        await RunGitCommandAsync($"add \"{relativePath}\"");
        await RefreshAsync();
    }

    public async Task StageAllAsync()
    {
        if (!_isGitRepository || _repositoryPath == null) return;

        await RunGitCommandAsync("add -A");
        await RefreshAsync();
    }

    public async Task UnstageFileAsync(string filePath)
    {
        if (!_isGitRepository || _repositoryPath == null) return;

        var relativePath = GetRelativePath(filePath);
        await RunGitCommandAsync($"reset HEAD -- \"{relativePath}\"");
        await RefreshAsync();
    }

    public async Task<GitCommitResult> CommitAsync(string message)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitCommitResult { Success = false, ErrorMessage = "Not a git repository" };

        var escapedMessage = message.Replace("\"", "\\\"");
        var result = await RunGitCommandAsync($"commit -m \"{escapedMessage}\"");

        if (result.ExitCode == 0)
        {
            // Extract commit hash
            var match = Regex.Match(result.Output, @"\[[\w\-]+\s+([a-f0-9]+)\]");
            await RefreshAsync();
            return new GitCommitResult
            {
                Success = true,
                CommitHash = match.Success ? match.Groups[1].Value : null
            };
        }

        return new GitCommitResult
        {
            Success = false,
            ErrorMessage = result.Output
        };
    }

    public async Task<IReadOnlyList<string>> GetBranchesAsync()
    {
        var branches = new List<string>();

        if (!_isGitRepository || _repositoryPath == null)
            return branches;

        var result = await RunGitCommandAsync("branch --list");
        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var branchName = line.TrimStart('*', ' ').Trim();
                if (!string.IsNullOrEmpty(branchName))
                {
                    branches.Add(branchName);
                }
            }
        }

        return branches;
    }

    public async Task<bool> CheckoutBranchAsync(string branchName)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"checkout \"{branchName}\"");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }

        return false;
    }

    public async Task<bool> CreateBranchAsync(string branchName)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"checkout -b \"{branchName}\"");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }

        return false;
    }

    public async Task<GitPullResult> PullAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitPullResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync("pull");
        await RefreshAsync();

        if (result.ExitCode == 0)
        {
            return new GitPullResult { Success = true };
        }

        return new GitPullResult
        {
            Success = false,
            ErrorMessage = result.Output
        };
    }

    public async Task<GitPushResult> PushAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitPushResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync("push");

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return new GitPushResult { Success = true };
        }

        return new GitPushResult
        {
            Success = false,
            ErrorMessage = result.Output
        };
    }

    public async Task<bool> InitRepositoryAsync(string path)
    {
        var result = await RunGitCommandAsync("init", path);

        if (result.ExitCode == 0)
        {
            _repositoryPath = path;
            _isGitRepository = true;
            await RefreshAsync();
            return true;
        }

        return false;
    }

    public async Task DiscardChangesAsync(string filePath)
    {
        if (!_isGitRepository || _repositoryPath == null) return;

        var relativePath = GetRelativePath(filePath);
        await RunGitCommandAsync($"checkout -- \"{relativePath}\"");
        await RefreshAsync();
    }

    public async Task<string> GetDiffAsync(string filePath)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return "";

        var relativePath = GetRelativePath(filePath);
        var result = await RunGitCommandAsync($"diff -- \"{relativePath}\"");

        if (result.ExitCode == 0)
        {
            return result.Output;
        }

        return "";
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetRecentCommitsAsync(int count = 20)
    {
        var commits = new List<GitCommitInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return commits;

        var result = await RunGitCommandAsync($"log -n {count} --format=\"%H|%s|%an|%aI\"");

        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 4)
                {
                    commits.Add(new GitCommitInfo
                    {
                        Hash = parts[0],
                        Message = parts[1],
                        Author = parts[2],
                        Date = DateTime.TryParse(parts[3], out var date) ? date : DateTime.Now
                    });
                }
            }
        }

        return commits;
    }

    public async Task RefreshAsync()
    {
        if (!_isGitRepository || _repositoryPath == null) return;

        await GetStatusAsync();
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> DeleteBranchAsync(string branchName, bool force = false)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var flag = force ? "-D" : "-d";
        var result = await RunGitCommandAsync($"branch {flag} \"{branchName}\"");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<GitMergeResult> MergeBranchAsync(string branchName)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitMergeResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync($"merge \"{branchName}\"");

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return new GitMergeResult { Success = true };
        }

        // Check for conflicts
        var statusResult = await GetStatusAsync();
        var conflicted = statusResult.Changes.Where(c => c.Status == GitFileStatus.Conflicted).Select(c => c.FilePath).ToList();

        return new GitMergeResult
        {
            Success = false,
            HasConflicts = conflicted.Any(),
            ConflictedFiles = conflicted,
            ErrorMessage = result.Output
        };
    }

    public async Task<IReadOnlyList<GitBranchInfo>> GetBranchInfoAsync()
    {
        var branches = new List<GitBranchInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return branches;

        // Get local branches with details
        var result = await RunGitCommandAsync("branch -vv --format=\"%(refname:short)|%(HEAD)|%(upstream:short)|%(upstream:track)\"");
        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 2)
                {
                    var branch = new GitBranchInfo
                    {
                        Name = parts[0].Trim(),
                        IsCurrentBranch = parts[1].Trim() == "*",
                        IsRemote = false,
                        TrackingBranch = parts.Length > 2 ? parts[2].Trim() : null
                    };

                    if (parts.Length > 3)
                    {
                        var track = parts[3].Trim();
                        var aheadMatch = Regex.Match(track, @"ahead (\d+)");
                        var behindMatch = Regex.Match(track, @"behind (\d+)");
                        if (aheadMatch.Success)
                            branch.Ahead = int.Parse(aheadMatch.Groups[1].Value);
                        if (behindMatch.Success)
                            branch.Behind = int.Parse(behindMatch.Groups[1].Value);
                    }

                    branches.Add(branch);
                }
            }
        }

        // Get remote branches
        var remoteResult = await RunGitCommandAsync("branch -r --format=\"%(refname:short)\"");
        if (remoteResult.ExitCode == 0)
        {
            var lines = remoteResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var name = line.Trim();
                if (!string.IsNullOrEmpty(name) && !name.Contains("HEAD"))
                {
                    branches.Add(new GitBranchInfo
                    {
                        Name = name,
                        IsRemote = true
                    });
                }
            }
        }

        return branches;
    }

    public async Task<bool> StashAsync(string? message = null)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var command = string.IsNullOrEmpty(message) ? "stash" : $"stash push -m \"{message}\"";
        var result = await RunGitCommandAsync(command);

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<IReadOnlyList<GitStashInfo>> GetStashesAsync()
    {
        var stashes = new List<GitStashInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return stashes;

        var result = await RunGitCommandAsync("stash list --format=\"%gd|%s|%aI\"");
        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var parts = lines[i].Split('|');
                if (parts.Length >= 2)
                {
                    stashes.Add(new GitStashInfo
                    {
                        Index = i,
                        Message = parts[1],
                        Date = parts.Length > 2 && DateTime.TryParse(parts[2], out var date) ? date : DateTime.Now
                    });
                }
            }
        }

        return stashes;
    }

    public async Task<bool> ApplyStashAsync(int index, bool pop = false)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var command = pop ? $"stash pop stash@{{{index}}}" : $"stash apply stash@{{{index}}}";
        var result = await RunGitCommandAsync(command);

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<bool> DropStashAsync(int index)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"stash drop stash@{{{index}}}");
        return result.ExitCode == 0;
    }

    public async Task<IReadOnlyList<GitBlameLine>> GetBlameAsync(string filePath)
    {
        var blameLines = new List<GitBlameLine>();

        if (!_isGitRepository || _repositoryPath == null)
            return blameLines;

        var relativePath = GetRelativePath(filePath);
        var result = await RunGitCommandAsync($"blame --line-porcelain \"{relativePath}\"");

        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n');
            string? currentHash = null;
            string? author = null;
            DateTime? date = null;
            var lineNumber = 0;

            foreach (var line in lines)
            {
                if (line.StartsWith('\t'))
                {
                    // This is the actual content line
                    if (currentHash != null)
                    {
                        blameLines.Add(new GitBlameLine
                        {
                            LineNumber = lineNumber,
                            CommitHash = currentHash,
                            Author = author ?? "Unknown",
                            Date = date ?? DateTime.Now,
                            LineContent = line.Substring(1)
                        });
                    }
                }
                else if (Regex.IsMatch(line, @"^[a-f0-9]{40}"))
                {
                    var parts = line.Split(' ');
                    currentHash = parts[0];
                    if (parts.Length > 2)
                        lineNumber = int.TryParse(parts[2], out var ln) ? ln : 0;
                }
                else if (line.StartsWith("author "))
                {
                    author = line.Substring(7);
                }
                else if (line.StartsWith("author-time "))
                {
                    if (long.TryParse(line.Substring(12), out var timestamp))
                    {
                        date = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    }
                }
            }
        }

        return blameLines;
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetFileHistoryAsync(string filePath, int maxCount = 50)
    {
        var commits = new List<GitCommitInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return commits;

        var relativePath = GetRelativePath(filePath);
        var result = await RunGitCommandAsync($"log -n {maxCount} --format=\"%H|%s|%an|%aI\" -- \"{relativePath}\"");

        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 4)
                {
                    commits.Add(new GitCommitInfo
                    {
                        Hash = parts[0],
                        Message = parts[1],
                        Author = parts[2],
                        Date = DateTime.TryParse(parts[3], out var date) ? date : DateTime.Now
                    });
                }
            }
        }

        return commits;
    }

    public async Task<bool> FetchAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync("fetch");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<string?> GetRemoteUrlAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return null;

        var result = await RunGitCommandAsync("remote get-url origin");
        if (result.ExitCode == 0)
        {
            return result.Output.Trim();
        }
        return null;
    }

    private string GetRelativePath(string filePath)
    {
        if (_repositoryPath == null) return filePath;
        return Path.GetRelativePath(_repositoryPath, filePath);
    }

    private (int ExitCode, string Output) RunGitCommand(string arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? _repositoryPath ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, "Failed to start git process");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, process.ExitCode == 0 ? output : error);
    }

    private async Task<(int ExitCode, string Output)> RunGitCommandAsync(string arguments, string? workingDirectory = null)
    {
        return await Task.Run(() => RunGitCommand(arguments, workingDirectory));
    }

    public async Task<bool> RenameBranchAsync(string oldName, string newName)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"branch -m \"{oldName}\" \"{newName}\"");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<GitRebaseResult> RebaseAsync(string ontoBranch)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitRebaseResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync($"rebase \"{ontoBranch}\"");

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return new GitRebaseResult { Success = true };
        }

        var statusResult = await GetStatusAsync();
        var conflicted = statusResult.Changes.Where(c => c.Status == GitFileStatus.Conflicted).Select(c => c.FilePath).ToList();

        return new GitRebaseResult
        {
            Success = false,
            HasConflicts = conflicted.Any(),
            ConflictedFiles = conflicted,
            ErrorMessage = result.Output
        };
    }

    public async Task<bool> RebaseAbortAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync("rebase --abort");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<GitRebaseResult> RebaseContinueAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitRebaseResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync("rebase --continue");

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return new GitRebaseResult { Success = true };
        }

        var statusResult = await GetStatusAsync();
        var conflicted = statusResult.Changes.Where(c => c.Status == GitFileStatus.Conflicted).Select(c => c.FilePath).ToList();

        return new GitRebaseResult
        {
            Success = false,
            HasConflicts = conflicted.Any(),
            ConflictedFiles = conflicted,
            ErrorMessage = result.Output
        };
    }

    public async Task<GitCherryPickResult> CherryPickAsync(string commitHash)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitCherryPickResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync($"cherry-pick \"{commitHash}\"");

        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return new GitCherryPickResult { Success = true };
        }

        var statusResult = await GetStatusAsync();
        var conflicted = statusResult.Changes.Where(c => c.Status == GitFileStatus.Conflicted).Select(c => c.FilePath).ToList();

        return new GitCherryPickResult
        {
            Success = false,
            HasConflicts = conflicted.Any(),
            ConflictedFiles = conflicted,
            ErrorMessage = result.Output
        };
    }

    public async Task<GitRevertResult> RevertCommitAsync(string commitHash)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return new GitRevertResult { Success = false, ErrorMessage = "Not a git repository" };

        var result = await RunGitCommandAsync($"revert --no-edit \"{commitHash}\"");

        if (result.ExitCode == 0)
        {
            var logResult = await RunGitCommandAsync("rev-parse HEAD");
            await RefreshAsync();
            return new GitRevertResult
            {
                Success = true,
                NewCommitHash = logResult.Output.Trim()
            };
        }

        var statusResult = await GetStatusAsync();
        var conflicted = statusResult.Changes.Where(c => c.Status == GitFileStatus.Conflicted).Select(c => c.FilePath).ToList();

        return new GitRevertResult
        {
            Success = false,
            HasConflicts = conflicted.Any(),
            ErrorMessage = result.Output
        };
    }

    public async Task<IReadOnlyList<GitTagInfo>> GetTagsAsync()
    {
        var tags = new List<GitTagInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return tags;

        var result = await RunGitCommandAsync("tag --list --format=\"%(refname:short)|%(objectname:short)|%(taggername)|%(creatordate:iso)\"");
        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 1)
                {
                    tags.Add(new GitTagInfo
                    {
                        Name = parts[0],
                        CommitHash = parts.Length > 1 ? parts[1] : null,
                        Tagger = parts.Length > 2 ? parts[2] : null,
                        Date = parts.Length > 3 && DateTime.TryParse(parts[3], out var date) ? date : null,
                        IsAnnotated = parts.Length > 2 && !string.IsNullOrEmpty(parts[2])
                    });
                }
            }
        }

        return tags;
    }

    public async Task<bool> CreateTagAsync(string tagName, string? commitHash = null, string? message = null)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        string command;
        if (!string.IsNullOrEmpty(message))
        {
            command = $"tag -a \"{tagName}\" -m \"{message}\"";
        }
        else
        {
            command = $"tag \"{tagName}\"";
        }

        if (!string.IsNullOrEmpty(commitHash))
        {
            command += $" \"{commitHash}\"";
        }

        var result = await RunGitCommandAsync(command);
        return result.ExitCode == 0;
    }

    public async Task<bool> DeleteTagAsync(string tagName, bool deleteRemote = false)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"tag -d \"{tagName}\"");
        if (result.ExitCode != 0) return false;

        if (deleteRemote)
        {
            await RunGitCommandAsync($"push origin --delete \"{tagName}\"");
        }

        return true;
    }

    public async Task<bool> PushTagsAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync("push --tags");
        return result.ExitCode == 0;
    }

    public async Task<IReadOnlyList<GitSubmoduleInfo>> GetSubmodulesAsync()
    {
        var submodules = new List<GitSubmoduleInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return submodules;

        var result = await RunGitCommandAsync("submodule status");
        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var hash = parts[0].TrimStart('-', '+');
                    var path = parts[1];

                    submodules.Add(new GitSubmoduleInfo
                    {
                        Path = path,
                        Name = Path.GetFileName(path),
                        CommitHash = hash,
                        IsInitialized = !parts[0].StartsWith('-')
                    });
                }
            }
        }

        return submodules;
    }

    public async Task<bool> UpdateSubmodulesAsync(bool init = true, bool recursive = true)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var command = "submodule update";
        if (init) command += " --init";
        if (recursive) command += " --recursive";

        var result = await RunGitCommandAsync(command);
        return result.ExitCode == 0;
    }

    public async Task<GitCloneResult> CloneAsync(string url, string destinationPath, IProgress<GitProgress>? progress = null)
    {
        Progress?.Invoke(this, new GitProgress { Operation = "Cloning", Message = $"Cloning {url}..." });

        var result = await RunGitCommandAsync($"clone \"{url}\" \"{destinationPath}\"", Path.GetDirectoryName(destinationPath));

        if (result.ExitCode == 0)
        {
            return new GitCloneResult { Success = true, Path = destinationPath };
        }

        return new GitCloneResult { Success = false, ErrorMessage = result.Output };
    }

    public async Task<IReadOnlyList<GitCommitInfo>> GetLogAsync(string? fromRef = null, string? toRef = null, int maxCount = 100)
    {
        var commits = new List<GitCommitInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return commits;

        var range = "";
        if (!string.IsNullOrEmpty(fromRef) && !string.IsNullOrEmpty(toRef))
        {
            range = $"{fromRef}..{toRef}";
        }
        else if (!string.IsNullOrEmpty(toRef))
        {
            range = toRef;
        }

        var result = await RunGitCommandAsync($"log -n {maxCount} --format=\"%H|%s|%an|%aI\" {range}");

        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split('|');
                if (parts.Length >= 4)
                {
                    commits.Add(new GitCommitInfo
                    {
                        Hash = parts[0],
                        Message = parts[1],
                        Author = parts[2],
                        Date = DateTime.TryParse(parts[3], out var date) ? date : DateTime.Now
                    });
                }
            }
        }

        return commits;
    }

    public async Task<(int ahead, int behind)> GetAheadBehindAsync(string? branchName = null)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return (0, 0);

        var branch = branchName ?? _currentBranch ?? "HEAD";
        var result = await RunGitCommandAsync($"rev-list --left-right --count @{{u}}...{branch}");

        if (result.ExitCode == 0)
        {
            var parts = result.Output.Trim().Split('\t');
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out var behind);
                int.TryParse(parts[1], out var ahead);
                return (ahead, behind);
            }
        }

        return (0, 0);
    }

    public async Task<bool> CheckoutAsync(string target, string? filePath = null)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var command = filePath != null
            ? $"checkout \"{target}\" -- \"{GetRelativePath(filePath)}\""
            : $"checkout \"{target}\"";

        var result = await RunGitCommandAsync(command);
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<bool> ResetAsync(string commitHash, GitResetMode mode = GitResetMode.Mixed)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var modeFlag = mode switch
        {
            GitResetMode.Soft => "--soft",
            GitResetMode.Mixed => "--mixed",
            GitResetMode.Hard => "--hard",
            _ => "--mixed"
        };

        var result = await RunGitCommandAsync($"reset {modeFlag} \"{commitHash}\"");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<string?> GetRepositoryRootAsync()
    {
        if (!_isGitRepository || _repositoryPath == null)
            return null;

        var result = await RunGitCommandAsync("rev-parse --show-toplevel");
        if (result.ExitCode == 0)
        {
            return result.Output.Trim();
        }
        return null;
    }

    public async Task<IReadOnlyList<GitRemoteInfo>> GetRemotesAsync()
    {
        var remotes = new List<GitRemoteInfo>();

        if (!_isGitRepository || _repositoryPath == null)
            return remotes;

        var result = await RunGitCommandAsync("remote -v");
        if (result.ExitCode == 0)
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var remoteDict = new Dictionary<string, GitRemoteInfo>();

            foreach (var line in lines)
            {
                var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    var name = parts[0];
                    var urlAndType = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var url = urlAndType[0];
                    var isFetch = urlAndType.Length > 1 && urlAndType[1].Contains("fetch");

                    if (!remoteDict.TryGetValue(name, out var remote))
                    {
                        remote = new GitRemoteInfo { Name = name };
                        remoteDict[name] = remote;
                    }

                    if (isFetch)
                        remote.FetchUrl = url;
                    else
                        remote.PushUrl = url;
                }
            }

            remotes.AddRange(remoteDict.Values);
        }

        return remotes;
    }

    public async Task<bool> AddRemoteAsync(string name, string url)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"remote add \"{name}\" \"{url}\"");
        return result.ExitCode == 0;
    }

    public async Task<bool> RemoveRemoteAsync(string name)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var result = await RunGitCommandAsync($"remote remove \"{name}\"");
        return result.ExitCode == 0;
    }

    public async Task<bool> CleanAsync(bool removeDirectories = false, bool force = true)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return false;

        var flags = "";
        if (force) flags += "f";
        if (removeDirectories) flags += "d";

        var result = await RunGitCommandAsync($"clean -{flags}");
        if (result.ExitCode == 0)
        {
            await RefreshAsync();
            return true;
        }
        return false;
    }

    public async Task<string?> GetFileContentAtCommitAsync(string filePath, string commitHash)
    {
        if (!_isGitRepository || _repositoryPath == null)
            return null;

        var relativePath = GetRelativePath(filePath);
        var result = await RunGitCommandAsync($"show \"{commitHash}:{relativePath}\"");

        if (result.ExitCode == 0)
        {
            return result.Output;
        }
        return null;
    }
}
