using System.Collections.ObjectModel;
using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class DiffViewerViewModel : ViewModelBase
{
    private readonly IGitService _gitService;

    /// <summary>
    /// The raw unified diff output from git, stored for hunk extraction.
    /// </summary>
    private string _rawDiff = "";

    /// <summary>
    /// Whether the diff is for staged changes (true) or unstaged changes (false).
    /// </summary>
    private bool _isStaged;

    [ObservableProperty]
    private string _leftTitle = "Original";

    [ObservableProperty]
    private string _rightTitle = "Modified";

    [ObservableProperty]
    private string _leftContent = "";

    [ObservableProperty]
    private string _rightContent = "";

    [ObservableProperty]
    private ObservableCollection<DiffHunk> _hunks = new();

    [ObservableProperty]
    private ObservableCollection<DiffLine> _diffLines = new();

    [ObservableProperty]
    private bool _sideBySide = true;

    [ObservableProperty]
    private int _addedLines;

    [ObservableProperty]
    private int _removedLines;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private bool _canStageHunks;

    [ObservableProperty]
    private bool _canUnstageHunks;

    [ObservableProperty]
    private string? _hunkOperationStatus;

    public DiffViewerViewModel(IGitService gitService)
    {
        _gitService = gitService;
    }

    public async Task LoadDiffAsync(string filePath)
    {
        FilePath = filePath;
        RightTitle = Path.GetFileName(filePath);
        LeftTitle = $"{Path.GetFileName(filePath)} (HEAD)";
        _isStaged = false;
        CanStageHunks = true;
        CanUnstageHunks = false;

        // Get current content
        if (File.Exists(filePath))
        {
            RightContent = await File.ReadAllTextAsync(filePath);
        }

        // Get diff from git
        _rawDiff = await _gitService.GetDiffAsync(filePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
    }

    public async Task LoadStagedDiffAsync(string filePath)
    {
        FilePath = filePath;
        RightTitle = $"{Path.GetFileName(filePath)} (staged)";
        LeftTitle = $"{Path.GetFileName(filePath)} (HEAD)";
        _isStaged = true;
        CanStageHunks = false;
        CanUnstageHunks = true;

        // Get current content
        if (File.Exists(filePath))
        {
            RightContent = await File.ReadAllTextAsync(filePath);
        }

        // Get staged diff from git
        _rawDiff = await _gitService.GetStagedDiffAsync(filePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
    }

    public void LoadContents(string leftContent, string rightContent, string leftTitle = "Original", string rightTitle = "Modified")
    {
        LeftContent = leftContent;
        RightContent = rightContent;
        LeftTitle = leftTitle;
        RightTitle = rightTitle;
        CanStageHunks = false;
        CanUnstageHunks = false;

        ComputeDiff();
    }

    /// <summary>
    /// Extracts individual hunks from raw unified diff output. Each hunk can be
    /// independently staged or unstaged via git apply --cached.
    /// </summary>
    private void ParseHunks(string diff)
    {
        Hunks.Clear();

        if (string.IsNullOrEmpty(diff))
            return;

        var lines = diff.Split('\n');

        // Extract the diff header (diff --git, index, ---, +++ lines)
        var headerBuilder = new StringBuilder();
        var i = 0;
        while (i < lines.Length && !lines[i].StartsWith("@@"))
        {
            headerBuilder.AppendLine(lines[i]);
            i++;
        }
        var diffHeader = headerBuilder.ToString();

        // Now parse each hunk starting with @@
        DiffHunk? currentHunk = null;
        var hunkIndex = 0;
        var hunkBodyBuilder = new StringBuilder();

        for (; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("@@"))
            {
                // Save the previous hunk
                if (currentHunk != null)
                {
                    currentHunk.PatchText = diffHeader + hunkBodyBuilder.ToString();
                    Hunks.Add(currentHunk);
                }

                // Start a new hunk
                hunkIndex++;
                var match = Regex.Match(line, @"@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@(.*)");
                var startLine = match.Success ? int.Parse(match.Groups[3].Value) : 0;
                var headerSuffix = match.Success ? match.Groups[5].Value.Trim() : "";

                currentHunk = new DiffHunk
                {
                    Index = hunkIndex,
                    HeaderLine = line,
                    StartLine = startLine,
                    Summary = string.IsNullOrEmpty(headerSuffix) ? $"Hunk {hunkIndex}" : headerSuffix,
                    Lines = new ObservableCollection<DiffLine>()
                };

                hunkBodyBuilder.Clear();
                hunkBodyBuilder.AppendLine(line);

                // Parse line numbers for header display
                if (match.Success)
                {
                    var oldStart = int.Parse(match.Groups[1].Value);
                    var oldCount = match.Groups[2].Value.Length > 0 ? int.Parse(match.Groups[2].Value) : 1;
                    var newStart = int.Parse(match.Groups[3].Value);
                    var newCount = match.Groups[4].Value.Length > 0 ? int.Parse(match.Groups[4].Value) : 1;
                    currentHunk.OldStart = oldStart;
                    currentHunk.OldCount = oldCount;
                    currentHunk.NewStart = newStart;
                    currentHunk.NewCount = newCount;
                }

                // Add header as a DiffLine in the hunk
                currentHunk.Lines.Add(new DiffLine
                {
                    LeftContent = line,
                    RightContent = "",
                    Type = DiffLineType.Header
                });
            }
            else if (currentHunk != null)
            {
                hunkBodyBuilder.AppendLine(line);

                if (line.StartsWith("-") && !line.StartsWith("---"))
                {
                    currentHunk.RemovedCount++;
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftContent = line.Substring(1),
                        RightContent = "",
                        Type = DiffLineType.Removed
                    });
                }
                else if (line.StartsWith("+") && !line.StartsWith("+++"))
                {
                    currentHunk.AddedCount++;
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftContent = "",
                        RightContent = line.Substring(1),
                        Type = DiffLineType.Added
                    });
                }
                else if (line.StartsWith(" "))
                {
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftContent = line.Substring(1),
                        RightContent = line.Substring(1),
                        Type = DiffLineType.Unchanged
                    });
                }
                else if (!string.IsNullOrEmpty(line) && !line.StartsWith("\\"))
                {
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftContent = line,
                        RightContent = line,
                        Type = DiffLineType.Unchanged
                    });
                }
            }
        }

        // Save the last hunk
        if (currentHunk != null)
        {
            currentHunk.PatchText = diffHeader + hunkBodyBuilder.ToString();
            Hunks.Add(currentHunk);
        }
    }

    [RelayCommand]
    private async Task StageHunkAsync(DiffHunk? hunk)
    {
        if (hunk == null || string.IsNullOrEmpty(hunk.PatchText))
            return;

        HunkOperationStatus = $"Staging hunk {hunk.Index}...";

        var success = await _gitService.StageHunkAsync(FilePath, hunk.PatchText);

        if (success)
        {
            HunkOperationStatus = $"Hunk {hunk.Index} staged successfully.";

            // Reload the diff to reflect the changes
            _rawDiff = await _gitService.GetDiffAsync(FilePath);
            ParseDiff(_rawDiff);
            ParseHunks(_rawDiff);
        }
        else
        {
            HunkOperationStatus = $"Failed to stage hunk {hunk.Index}.";
        }
    }

    [RelayCommand]
    private async Task UnstageHunkAsync(DiffHunk? hunk)
    {
        if (hunk == null || string.IsNullOrEmpty(hunk.PatchText))
            return;

        HunkOperationStatus = $"Unstaging hunk {hunk.Index}...";

        var success = await _gitService.UnstageHunkAsync(FilePath, hunk.PatchText);

        if (success)
        {
            HunkOperationStatus = $"Hunk {hunk.Index} unstaged successfully.";

            // Reload the staged diff to reflect the changes
            _rawDiff = await _gitService.GetStagedDiffAsync(FilePath);
            ParseDiff(_rawDiff);
            ParseHunks(_rawDiff);
        }
        else
        {
            HunkOperationStatus = $"Failed to unstage hunk {hunk.Index}.";
        }
    }

    [RelayCommand]
    private async Task StageAllHunksAsync()
    {
        await _gitService.StageFileAsync(FilePath);
        HunkOperationStatus = "All hunks staged.";

        _rawDiff = await _gitService.GetDiffAsync(FilePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
    }

    [RelayCommand]
    private async Task UnstageAllHunksAsync()
    {
        await _gitService.UnstageFileAsync(FilePath);
        HunkOperationStatus = "All hunks unstaged.";

        _rawDiff = await _gitService.GetStagedDiffAsync(FilePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
    }

    private void ParseDiff(string diff)
    {
        DiffLines.Clear();
        AddedLines = 0;
        RemovedLines = 0;

        if (string.IsNullOrEmpty(diff))
        {
            // No changes - show current content
            var lines = RightContent.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                DiffLines.Add(new DiffLine
                {
                    LeftLineNumber = i + 1,
                    RightLineNumber = i + 1,
                    LeftContent = lines[i].TrimEnd('\r'),
                    RightContent = lines[i].TrimEnd('\r'),
                    Type = DiffLineType.Unchanged
                });
            }
            return;
        }

        // Parse unified diff format
        var diffLines = diff.Split('\n');
        var leftLineNum = 0;
        var rightLineNum = 0;

        foreach (var line in diffLines)
        {
            if (line.StartsWith("@@"))
            {
                // Parse hunk header: @@ -start,count +start,count @@
                var match = Regex.Match(line, @"@@ -(\d+),?\d* \+(\d+),?\d* @@");
                if (match.Success)
                {
                    leftLineNum = int.Parse(match.Groups[1].Value) - 1;
                    rightLineNum = int.Parse(match.Groups[2].Value) - 1;
                }

                DiffLines.Add(new DiffLine
                {
                    LeftContent = line,
                    RightContent = "",
                    Type = DiffLineType.Header
                });
            }
            else if (line.StartsWith("-") && !line.StartsWith("---"))
            {
                leftLineNum++;
                RemovedLines++;
                DiffLines.Add(new DiffLine
                {
                    LeftLineNumber = leftLineNum,
                    LeftContent = line.Substring(1),
                    RightContent = "",
                    Type = DiffLineType.Removed
                });
            }
            else if (line.StartsWith("+") && !line.StartsWith("+++"))
            {
                rightLineNum++;
                AddedLines++;
                DiffLines.Add(new DiffLine
                {
                    RightLineNumber = rightLineNum,
                    LeftContent = "",
                    RightContent = line.Substring(1),
                    Type = DiffLineType.Added
                });
            }
            else if (line.StartsWith(" "))
            {
                leftLineNum++;
                rightLineNum++;
                DiffLines.Add(new DiffLine
                {
                    LeftLineNumber = leftLineNum,
                    RightLineNumber = rightLineNum,
                    LeftContent = line.Substring(1),
                    RightContent = line.Substring(1),
                    Type = DiffLineType.Unchanged
                });
            }
            else if (!line.StartsWith("diff ") && !line.StartsWith("index ") &&
                     !line.StartsWith("---") && !line.StartsWith("+++"))
            {
                // Context line without prefix
                if (!string.IsNullOrEmpty(line))
                {
                    leftLineNum++;
                    rightLineNum++;
                    DiffLines.Add(new DiffLine
                    {
                        LeftLineNumber = leftLineNum,
                        RightLineNumber = rightLineNum,
                        LeftContent = line,
                        RightContent = line,
                        Type = DiffLineType.Unchanged
                    });
                }
            }
        }
    }

    private void ComputeDiff()
    {
        DiffLines.Clear();
        AddedLines = 0;
        RemovedLines = 0;

        var leftLines = LeftContent.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var rightLines = RightContent.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();

        // Simple line-by-line diff using LCS
        var lcs = ComputeLCS(leftLines, rightLines);
        var leftIdx = 0;
        var rightIdx = 0;
        var lcsIdx = 0;

        while (leftIdx < leftLines.Length || rightIdx < rightLines.Length)
        {
            if (lcsIdx < lcs.Count && leftIdx < leftLines.Length && rightIdx < rightLines.Length &&
                leftLines[leftIdx] == lcs[lcsIdx] && rightLines[rightIdx] == lcs[lcsIdx])
            {
                // Common line
                DiffLines.Add(new DiffLine
                {
                    LeftLineNumber = leftIdx + 1,
                    RightLineNumber = rightIdx + 1,
                    LeftContent = leftLines[leftIdx],
                    RightContent = rightLines[rightIdx],
                    Type = DiffLineType.Unchanged
                });
                leftIdx++;
                rightIdx++;
                lcsIdx++;
            }
            else if (leftIdx < leftLines.Length &&
                     (lcsIdx >= lcs.Count || leftLines[leftIdx] != lcs[lcsIdx]))
            {
                // Removed line
                RemovedLines++;
                DiffLines.Add(new DiffLine
                {
                    LeftLineNumber = leftIdx + 1,
                    LeftContent = leftLines[leftIdx],
                    RightContent = "",
                    Type = DiffLineType.Removed
                });
                leftIdx++;
            }
            else if (rightIdx < rightLines.Length &&
                     (lcsIdx >= lcs.Count || rightLines[rightIdx] != lcs[lcsIdx]))
            {
                // Added line
                AddedLines++;
                DiffLines.Add(new DiffLine
                {
                    RightLineNumber = rightIdx + 1,
                    LeftContent = "",
                    RightContent = rightLines[rightIdx],
                    Type = DiffLineType.Added
                });
                rightIdx++;
            }
        }
    }

    private static List<string> ComputeLCS(string[] left, string[] right)
    {
        var m = left.Length;
        var n = right.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                if (left[i - 1] == right[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to find LCS
        var result = new List<string>();
        var x = m;
        var y = n;
        while (x > 0 && y > 0)
        {
            if (left[x - 1] == right[y - 1])
            {
                result.Insert(0, left[x - 1]);
                x--;
                y--;
            }
            else if (dp[x - 1, y] > dp[x, y - 1])
            {
                x--;
            }
            else
            {
                y--;
            }
        }

        return result;
    }

    [RelayCommand]
    private void ToggleView()
    {
        SideBySide = !SideBySide;
    }
}

/// <summary>
/// Represents a single hunk (contiguous group of changes) from a unified diff.
/// Each hunk can be independently staged or unstaged.
/// </summary>
public class DiffHunk
{
    /// <summary>1-based hunk index within the diff</summary>
    public int Index { get; set; }

    /// <summary>The @@ header line</summary>
    public string HeaderLine { get; set; } = "";

    /// <summary>Starting line number in the new file</summary>
    public int StartLine { get; set; }

    /// <summary>Human-readable summary (from the @@ line suffix, or "Hunk N")</summary>
    public string Summary { get; set; } = "";

    /// <summary>Number of added lines in this hunk</summary>
    public int AddedCount { get; set; }

    /// <summary>Number of removed lines in this hunk</summary>
    public int RemovedCount { get; set; }

    /// <summary>Old file start line</summary>
    public int OldStart { get; set; }

    /// <summary>Old file line count</summary>
    public int OldCount { get; set; }

    /// <summary>New file start line</summary>
    public int NewStart { get; set; }

    /// <summary>New file line count</summary>
    public int NewCount { get; set; }

    /// <summary>The full patch text (diff header + this hunk) for git apply --cached</summary>
    public string PatchText { get; set; } = "";

    /// <summary>Diff lines within this hunk for display</summary>
    public ObservableCollection<DiffLine> Lines { get; set; } = new();

    /// <summary>Display text showing added/removed counts</summary>
    public string ChangesSummary => $"+{AddedCount} -{RemovedCount}";

    /// <summary>Display text for the hunk: "Lines N-M: summary"</summary>
    public string DisplayText => NewCount > 1
        ? $"Lines {NewStart}-{NewStart + NewCount - 1}: {Summary}"
        : $"Line {NewStart}: {Summary}";
}

public class DiffLine
{
    public int? LeftLineNumber { get; set; }
    public int? RightLineNumber { get; set; }
    public string LeftContent { get; set; } = "";
    public string RightContent { get; set; } = "";
    public DiffLineType Type { get; set; }

    public string LeftLineNumberText => LeftLineNumber?.ToString() ?? "";
    public string RightLineNumberText => RightLineNumber?.ToString() ?? "";

    public string BackgroundColor => Type switch
    {
        DiffLineType.Added => "#1E3A1E",
        DiffLineType.Removed => "#3A1E1E",
        DiffLineType.Header => "#2D2D2D",
        _ => "Transparent"
    };

    public string LeftBackground => Type == DiffLineType.Removed ? "#3A1E1E" : "Transparent";
    public string RightBackground => Type == DiffLineType.Added ? "#1E3A1E" : "Transparent";

    /// <summary>Prefix character for unified diff view: +, -, or space</summary>
    public string UnifiedPrefix => Type switch
    {
        DiffLineType.Added => "+",
        DiffLineType.Removed => "-",
        DiffLineType.Header => "@@",
        _ => " "
    };

    /// <summary>Prefix color for unified diff view</summary>
    public string UnifiedPrefixColor => Type switch
    {
        DiffLineType.Added => "#4EC9B0",
        DiffLineType.Removed => "#F48771",
        DiffLineType.Header => "#569CD6",
        _ => "#606060"
    };

    /// <summary>Content text for unified diff view (shows the relevant side)</summary>
    public string UnifiedContent => Type switch
    {
        DiffLineType.Added => RightContent,
        DiffLineType.Removed => LeftContent,
        DiffLineType.Header => LeftContent,
        _ => LeftContent
    };
}

public enum DiffLineType
{
    Unchanged,
    Added,
    Removed,
    Modified,
    Header
}
