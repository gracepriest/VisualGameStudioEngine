using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Dialogs;

public partial class DiffViewerViewModel : ViewModelBase
{
    private readonly IGitService _gitService;

    [ObservableProperty]
    private string _leftTitle = "Original";

    [ObservableProperty]
    private string _rightTitle = "Modified";

    [ObservableProperty]
    private string _leftContent = "";

    [ObservableProperty]
    private string _rightContent = "";

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

    public DiffViewerViewModel(IGitService gitService)
    {
        _gitService = gitService;
    }

    public async Task LoadDiffAsync(string filePath)
    {
        FilePath = filePath;
        RightTitle = Path.GetFileName(filePath);
        LeftTitle = $"{Path.GetFileName(filePath)} (HEAD)";

        // Get current content
        if (File.Exists(filePath))
        {
            RightContent = await File.ReadAllTextAsync(filePath);
        }

        // Get diff from git
        var diff = await _gitService.GetDiffAsync(filePath);
        ParseDiff(diff);
    }

    public void LoadContents(string leftContent, string rightContent, string leftTitle = "Original", string rightTitle = "Modified")
    {
        LeftContent = leftContent;
        RightContent = rightContent;
        LeftTitle = leftTitle;
        RightTitle = rightTitle;

        ComputeDiff();
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
                var match = System.Text.RegularExpressions.Regex.Match(line, @"@@ -(\d+),?\d* \+(\d+),?\d* @@");
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
}

public enum DiffLineType
{
    Unchanged,
    Added,
    Removed,
    Modified,
    Header
}
