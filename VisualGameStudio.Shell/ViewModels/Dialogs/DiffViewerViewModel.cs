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

    /// <summary>
    /// Side-by-side aligned lines for left pane (with gap filling).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DiffLine> _leftPaneLines = new();

    /// <summary>
    /// Side-by-side aligned lines for right pane (with gap filling).
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DiffLine> _rightPaneLines = new();

    [ObservableProperty]
    private bool _sideBySide = true;

    [ObservableProperty]
    private int _addedLines;

    [ObservableProperty]
    private int _removedLines;

    [ObservableProperty]
    private int _modifiedLines;

    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private bool _canStageHunks;

    [ObservableProperty]
    private bool _canUnstageHunks;

    [ObservableProperty]
    private bool _canRevertHunks;

    [ObservableProperty]
    private string? _hunkOperationStatus;

    /// <summary>
    /// Total number of change regions (groups of consecutive added/removed/modified lines).
    /// </summary>
    [ObservableProperty]
    private int _changeCount;

    /// <summary>
    /// Current change index (1-based) for navigation display.
    /// </summary>
    [ObservableProperty]
    private int _currentChangeIndex;

    /// <summary>
    /// Indices into DiffLines that mark the start of each change region.
    /// </summary>
    private List<int> _changeStartIndices = new();

    /// <summary>
    /// Whether to show collapsed (folded) unchanged regions.
    /// </summary>
    [ObservableProperty]
    private bool _foldUnchangedRegions = true;

    /// <summary>
    /// The display lines after folding is applied. Used for rendering.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<DiffDisplayItem> _displayItems = new();

    /// <summary>
    /// Summary text: "N changes (+A -R ~M)"
    /// </summary>
    public string ChangesSummaryText => $"{ChangeCount} changes (+{AddedLines} -{RemovedLines}" +
        (ModifiedLines > 0 ? $" ~{ModifiedLines})" : ")");

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
        CanRevertHunks = true;

        // Get current content
        if (File.Exists(filePath))
        {
            RightContent = await File.ReadAllTextAsync(filePath);
        }

        // Get diff from git
        _rawDiff = await _gitService.GetDiffAsync(filePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
        BuildSideBySideLines();
        BuildChangeIndex();
        BuildDisplayItems();
        OnPropertyChanged(nameof(ChangesSummaryText));
    }

    public async Task LoadStagedDiffAsync(string filePath)
    {
        FilePath = filePath;
        RightTitle = $"{Path.GetFileName(filePath)} (staged)";
        LeftTitle = $"{Path.GetFileName(filePath)} (HEAD)";
        _isStaged = true;
        CanStageHunks = false;
        CanUnstageHunks = true;
        CanRevertHunks = false;

        // Get current content
        if (File.Exists(filePath))
        {
            RightContent = await File.ReadAllTextAsync(filePath);
        }

        // Get staged diff from git
        _rawDiff = await _gitService.GetStagedDiffAsync(filePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
        BuildSideBySideLines();
        BuildChangeIndex();
        BuildDisplayItems();
        OnPropertyChanged(nameof(ChangesSummaryText));
    }

    /// <summary>
    /// Loads a pre-computed raw unified diff (e.g. from git diff commit~1..commit -- file).
    /// No staging/unstaging is available for historical diffs.
    /// </summary>
    public void LoadFromRawDiff(string rawDiff, string filePath, string leftTitle = "Before", string rightTitle = "After")
    {
        FilePath = filePath;
        LeftTitle = leftTitle;
        RightTitle = rightTitle;
        CanStageHunks = false;
        CanUnstageHunks = false;
        CanRevertHunks = false;
        _rawDiff = rawDiff;

        ParseDiff(rawDiff);
        ParseHunks(rawDiff);
        BuildSideBySideLines();
        BuildChangeIndex();
        BuildDisplayItems();
        OnPropertyChanged(nameof(ChangesSummaryText));
    }

    public void LoadContents(string leftContent, string rightContent, string leftTitle = "Original", string rightTitle = "Modified")
    {
        LeftContent = leftContent;
        RightContent = rightContent;
        LeftTitle = leftTitle;
        RightTitle = rightTitle;
        CanStageHunks = false;
        CanUnstageHunks = false;
        CanRevertHunks = false;

        ComputeDiff();
        BuildSideBySideLines();
        BuildChangeIndex();
        BuildDisplayItems();
        OnPropertyChanged(nameof(ChangesSummaryText));
    }

    /// <summary>
    /// Builds side-by-side aligned lines with gap filling.
    /// Adjacent removed+added lines are paired as "modified" lines with character-level diff.
    /// </summary>
    private void BuildSideBySideLines()
    {
        LeftPaneLines.Clear();
        RightPaneLines.Clear();
        ModifiedLines = 0;

        var i = 0;
        while (i < DiffLines.Count)
        {
            var line = DiffLines[i];

            if (line.Type == DiffLineType.Unchanged || line.Type == DiffLineType.Header)
            {
                LeftPaneLines.Add(line);
                RightPaneLines.Add(line);
                i++;
            }
            else if (line.Type == DiffLineType.Removed)
            {
                // Collect consecutive removed lines
                var removedStart = i;
                while (i < DiffLines.Count && DiffLines[i].Type == DiffLineType.Removed)
                    i++;

                // Collect consecutive added lines that follow
                var addedStart = i;
                while (i < DiffLines.Count && DiffLines[i].Type == DiffLineType.Added)
                    i++;

                var removedCount = addedStart - removedStart;
                var addedCount = i - addedStart;

                // Pair them up as modified where possible
                var pairs = Math.Min(removedCount, addedCount);
                for (var p = 0; p < pairs; p++)
                {
                    var removed = DiffLines[removedStart + p];
                    var added = DiffLines[addedStart + p];

                    // Compute character-level diff highlights
                    ComputeCharacterDiff(removed.LeftContent, added.RightContent,
                        out var leftHighlights, out var rightHighlights);

                    var modifiedLeft = new DiffLine
                    {
                        LeftLineNumber = removed.LeftLineNumber,
                        LeftContent = removed.LeftContent,
                        RightContent = "",
                        Type = DiffLineType.Modified,
                        CharHighlights = leftHighlights
                    };
                    var modifiedRight = new DiffLine
                    {
                        RightLineNumber = added.RightLineNumber,
                        LeftContent = "",
                        RightContent = added.RightContent,
                        Type = DiffLineType.Modified,
                        CharHighlights = rightHighlights
                    };

                    LeftPaneLines.Add(modifiedLeft);
                    RightPaneLines.Add(modifiedRight);
                    ModifiedLines++;
                }

                // Remaining removed lines (no paired add)
                for (var r = pairs; r < removedCount; r++)
                {
                    LeftPaneLines.Add(DiffLines[removedStart + r]);
                    RightPaneLines.Add(DiffLine.Empty); // gap filler
                }

                // Remaining added lines (no paired remove)
                for (var a = pairs; a < addedCount; a++)
                {
                    LeftPaneLines.Add(DiffLine.Empty); // gap filler
                    RightPaneLines.Add(DiffLines[addedStart + a]);
                }
            }
            else if (line.Type == DiffLineType.Added)
            {
                // Added without preceding removed
                LeftPaneLines.Add(DiffLine.Empty);
                RightPaneLines.Add(line);
                i++;
            }
            else
            {
                LeftPaneLines.Add(line);
                RightPaneLines.Add(line);
                i++;
            }
        }
    }

    /// <summary>
    /// Computes character-level diff between two strings.
    /// Returns highlight ranges for each side indicating changed characters.
    /// </summary>
    private static void ComputeCharacterDiff(string left, string right,
        out List<CharHighlight> leftHighlights, out List<CharHighlight> rightHighlights)
    {
        leftHighlights = new List<CharHighlight>();
        rightHighlights = new List<CharHighlight>();

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return;

        // Find common prefix
        var prefixLen = 0;
        var minLen = Math.Min(left.Length, right.Length);
        while (prefixLen < minLen && left[prefixLen] == right[prefixLen])
            prefixLen++;

        // Find common suffix (don't overlap with prefix)
        var suffixLen = 0;
        while (suffixLen < (minLen - prefixLen) &&
               left[left.Length - 1 - suffixLen] == right[right.Length - 1 - suffixLen])
            suffixLen++;

        // The changed region
        var leftChangeStart = prefixLen;
        var leftChangeEnd = left.Length - suffixLen;
        var rightChangeStart = prefixLen;
        var rightChangeEnd = right.Length - suffixLen;

        if (leftChangeStart < leftChangeEnd)
        {
            leftHighlights.Add(new CharHighlight { Start = leftChangeStart, Length = leftChangeEnd - leftChangeStart });
        }
        if (rightChangeStart < rightChangeEnd)
        {
            rightHighlights.Add(new CharHighlight { Start = rightChangeStart, Length = rightChangeEnd - rightChangeStart });
        }
    }

    /// <summary>
    /// Builds the index of change start positions for F7/Shift+F7 navigation.
    /// </summary>
    private void BuildChangeIndex()
    {
        _changeStartIndices.Clear();
        var inChange = false;

        for (var i = 0; i < DiffLines.Count; i++)
        {
            var isChange = DiffLines[i].Type is DiffLineType.Added or DiffLineType.Removed or DiffLineType.Modified;
            if (isChange && !inChange)
            {
                _changeStartIndices.Add(i);
                inChange = true;
            }
            else if (!isChange)
            {
                inChange = false;
            }
        }

        ChangeCount = _changeStartIndices.Count;
        CurrentChangeIndex = ChangeCount > 0 ? 1 : 0;
    }

    /// <summary>
    /// Builds display items with folding of unchanged regions.
    /// Groups of more than 6 consecutive unchanged lines are collapsed.
    /// </summary>
    private void BuildDisplayItems()
    {
        DisplayItems.Clear();

        if (!FoldUnchangedRegions)
        {
            foreach (var line in DiffLines)
            {
                DisplayItems.Add(new DiffDisplayItem { Line = line, IsFoldPlaceholder = false });
            }
            return;
        }

        const int contextLines = 3; // Show 3 lines of context around changes

        // First, mark which lines should be visible
        var visible = new bool[DiffLines.Count];

        // Always show header lines
        for (var i = 0; i < DiffLines.Count; i++)
        {
            if (DiffLines[i].Type != DiffLineType.Unchanged)
                visible[i] = true;
        }

        // Show context around changes
        for (var i = 0; i < DiffLines.Count; i++)
        {
            if (DiffLines[i].Type != DiffLineType.Unchanged)
            {
                for (var c = Math.Max(0, i - contextLines); c <= Math.Min(DiffLines.Count - 1, i + contextLines); c++)
                    visible[c] = true;
            }
        }

        // If all lines are visible, no folding needed
        if (visible.All(v => v))
        {
            foreach (var line in DiffLines)
                DisplayItems.Add(new DiffDisplayItem { Line = line, IsFoldPlaceholder = false });
            return;
        }

        var i2 = 0;
        while (i2 < DiffLines.Count)
        {
            if (visible[i2])
            {
                DisplayItems.Add(new DiffDisplayItem { Line = DiffLines[i2], IsFoldPlaceholder = false });
                i2++;
            }
            else
            {
                // Count hidden lines
                var hiddenStart = i2;
                while (i2 < DiffLines.Count && !visible[i2])
                    i2++;
                var hiddenCount = i2 - hiddenStart;

                DisplayItems.Add(new DiffDisplayItem
                {
                    IsFoldPlaceholder = true,
                    FoldedLineCount = hiddenCount,
                    FoldStartIndex = hiddenStart
                });
            }
        }
    }

    /// <summary>
    /// Expand a folded region, showing all hidden lines.
    /// </summary>
    [RelayCommand]
    private void ExpandFoldedRegion(DiffDisplayItem? placeholder)
    {
        if (placeholder == null || !placeholder.IsFoldPlaceholder)
            return;

        var idx = DisplayItems.IndexOf(placeholder);
        if (idx < 0) return;

        DisplayItems.RemoveAt(idx);

        // Insert the hidden lines
        for (var i = 0; i < placeholder.FoldedLineCount; i++)
        {
            var lineIdx = placeholder.FoldStartIndex + i;
            if (lineIdx < DiffLines.Count)
            {
                DisplayItems.Insert(idx + i, new DiffDisplayItem
                {
                    Line = DiffLines[lineIdx],
                    IsFoldPlaceholder = false
                });
            }
        }
    }

    /// <summary>
    /// Navigate to the next change (F7).
    /// Returns the line index to scroll to, or -1 if no next change.
    /// </summary>
    [RelayCommand]
    private void GoToNextChange()
    {
        if (_changeStartIndices.Count == 0) return;

        if (CurrentChangeIndex < ChangeCount)
            CurrentChangeIndex++;
        else
            CurrentChangeIndex = 1; // wrap around

        // Fire event so view can scroll
        NavigateToChangeRequested?.Invoke(this, _changeStartIndices[CurrentChangeIndex - 1]);
    }

    /// <summary>
    /// Navigate to the previous change (Shift+F7).
    /// </summary>
    [RelayCommand]
    private void GoToPreviousChange()
    {
        if (_changeStartIndices.Count == 0) return;

        if (CurrentChangeIndex > 1)
            CurrentChangeIndex--;
        else
            CurrentChangeIndex = ChangeCount; // wrap around

        NavigateToChangeRequested?.Invoke(this, _changeStartIndices[CurrentChangeIndex - 1]);
    }

    /// <summary>
    /// Event raised when the view should scroll to a particular DiffLine index.
    /// </summary>
    public event EventHandler<int>? NavigateToChangeRequested;

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
        var leftLineNum = 0;
        var rightLineNum = 0;

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

                // Initialize per-hunk line counters
                leftLineNum = currentHunk.OldStart;
                rightLineNum = currentHunk.NewStart;
            }
            else if (currentHunk != null)
            {
                hunkBodyBuilder.AppendLine(line);

                if (line.StartsWith("-") && !line.StartsWith("---"))
                {
                    currentHunk.RemovedCount++;
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftLineNumber = leftLineNum,
                        LeftContent = line.Substring(1),
                        RightContent = "",
                        Type = DiffLineType.Removed
                    });
                    leftLineNum++;
                }
                else if (line.StartsWith("+") && !line.StartsWith("+++"))
                {
                    currentHunk.AddedCount++;
                    currentHunk.Lines.Add(new DiffLine
                    {
                        RightLineNumber = rightLineNum,
                        LeftContent = "",
                        RightContent = line.Substring(1),
                        Type = DiffLineType.Added
                    });
                    rightLineNum++;
                }
                else if (line.StartsWith(" "))
                {
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftLineNumber = leftLineNum,
                        RightLineNumber = rightLineNum,
                        LeftContent = line.Substring(1),
                        RightContent = line.Substring(1),
                        Type = DiffLineType.Unchanged
                    });
                    leftLineNum++;
                    rightLineNum++;
                }
                else if (!string.IsNullOrEmpty(line) && !line.StartsWith("\\"))
                {
                    currentHunk.Lines.Add(new DiffLine
                    {
                        LeftLineNumber = leftLineNum,
                        RightLineNumber = rightLineNum,
                        LeftContent = line,
                        RightContent = line,
                        Type = DiffLineType.Unchanged
                    });
                    leftLineNum++;
                    rightLineNum++;
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
            BuildSideBySideLines();
            BuildChangeIndex();
            BuildDisplayItems();
            OnPropertyChanged(nameof(ChangesSummaryText));
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
            BuildSideBySideLines();
            BuildChangeIndex();
            BuildDisplayItems();
            OnPropertyChanged(nameof(ChangesSummaryText));
        }
        else
        {
            HunkOperationStatus = $"Failed to unstage hunk {hunk.Index}.";
        }
    }

    [RelayCommand]
    private async Task RevertHunkAsync(DiffHunk? hunk)
    {
        if (hunk == null || string.IsNullOrEmpty(hunk.PatchText))
            return;

        HunkOperationStatus = $"Reverting hunk {hunk.Index}...";

        var success = await _gitService.RevertHunkAsync(FilePath, hunk.PatchText);

        if (success)
        {
            HunkOperationStatus = $"Hunk {hunk.Index} reverted.";

            // Reload file content and diff
            if (File.Exists(FilePath))
                RightContent = await File.ReadAllTextAsync(FilePath);

            _rawDiff = await _gitService.GetDiffAsync(FilePath);
            ParseDiff(_rawDiff);
            ParseHunks(_rawDiff);
            BuildSideBySideLines();
            BuildChangeIndex();
            BuildDisplayItems();
            OnPropertyChanged(nameof(ChangesSummaryText));
        }
        else
        {
            HunkOperationStatus = $"Failed to revert hunk {hunk.Index}.";
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
        BuildSideBySideLines();
        BuildChangeIndex();
        BuildDisplayItems();
        OnPropertyChanged(nameof(ChangesSummaryText));
    }

    [RelayCommand]
    private async Task UnstageAllHunksAsync()
    {
        await _gitService.UnstageFileAsync(FilePath);
        HunkOperationStatus = "All hunks unstaged.";

        _rawDiff = await _gitService.GetStagedDiffAsync(FilePath);
        ParseDiff(_rawDiff);
        ParseHunks(_rawDiff);
        BuildSideBySideLines();
        BuildChangeIndex();
        BuildDisplayItems();
        OnPropertyChanged(nameof(ChangesSummaryText));
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

    [RelayCommand]
    private void ToggleFolding()
    {
        FoldUnchangedRegions = !FoldUnchangedRegions;
        BuildDisplayItems();
    }
}

/// <summary>
/// A display item that is either a real DiffLine or a fold placeholder.
/// </summary>
public class DiffDisplayItem
{
    public DiffLine? Line { get; set; }
    public bool IsFoldPlaceholder { get; set; }
    public int FoldedLineCount { get; set; }
    public int FoldStartIndex { get; set; }

    public string FoldText => $"... {FoldedLineCount} unchanged lines hidden ...";
}

/// <summary>
/// Represents a highlighted character range within a line.
/// </summary>
public class CharHighlight
{
    public int Start { get; set; }
    public int Length { get; set; }
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

    /// <summary>Character-level highlight ranges for inline diff visualization.</summary>
    public List<CharHighlight>? CharHighlights { get; set; }

    /// <summary>True if this is an empty gap filler in side-by-side mode.</summary>
    public bool IsGapFiller { get; set; }

    public string LeftLineNumberText => LeftLineNumber?.ToString() ?? "";
    public string RightLineNumberText => RightLineNumber?.ToString() ?? "";

    public string BackgroundColor => Type switch
    {
        DiffLineType.Added => "#1E3A1E",
        DiffLineType.Removed => "#3A1E1E",
        DiffLineType.Modified => "#2B2B00",
        DiffLineType.Header => "#2D2D2D",
        _ => "Transparent"
    };

    public string LeftBackground => Type switch
    {
        DiffLineType.Removed => "#3A1E1E",
        DiffLineType.Modified => "#3A2A1E",
        _ => "Transparent"
    };

    public string RightBackground => Type switch
    {
        DiffLineType.Added => "#1E3A1E",
        DiffLineType.Modified => "#1E2A3A",
        _ => "Transparent"
    };

    /// <summary>Prefix character for unified diff view: +, -, or space</summary>
    public string UnifiedPrefix => Type switch
    {
        DiffLineType.Added => "+",
        DiffLineType.Removed => "-",
        DiffLineType.Modified => "~",
        DiffLineType.Header => "@@",
        _ => " "
    };

    /// <summary>Prefix color for unified diff view</summary>
    public string UnifiedPrefixColor => Type switch
    {
        DiffLineType.Added => "#4EC9B0",
        DiffLineType.Removed => "#F48771",
        DiffLineType.Modified => "#DCDCAA",
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

    /// <summary>Gutter indicator for the minimap</summary>
    public string GutterColor => Type switch
    {
        DiffLineType.Added => "#4EC9B0",
        DiffLineType.Removed => "#F48771",
        DiffLineType.Modified => "#DCDCAA",
        _ => "Transparent"
    };

    /// <summary>Whether this line has a gutter marker</summary>
    public bool HasGutterMark => Type is DiffLineType.Added or DiffLineType.Removed or DiffLineType.Modified;

    /// <summary>Creates an empty gap-filler line for side-by-side alignment.</summary>
    public static DiffLine Empty => new()
    {
        LeftContent = "",
        RightContent = "",
        Type = DiffLineType.Unchanged,
        IsGapFiller = true
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
