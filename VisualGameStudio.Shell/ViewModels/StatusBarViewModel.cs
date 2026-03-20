using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Core.Abstractions.ViewModels;

namespace VisualGameStudio.Shell.ViewModels;

/// <summary>
/// ViewModel for the VS Code-style status bar with all standard indicators.
/// Provides interactive items for git, diagnostics, cursor, encoding, etc.
/// </summary>
public partial class StatusBarViewModel : ViewModelBase
{
    // ── Left side items ──

    [ObservableProperty]
    private string _branchName = "";

    [ObservableProperty]
    private bool _isGitRepository;

    [ObservableProperty]
    private int _syncUpCount;

    [ObservableProperty]
    private int _syncDownCount;

    [ObservableProperty]
    private bool _hasUncommittedChanges;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _infoCount;

    [ObservableProperty]
    private bool _isTaskRunning;

    [ObservableProperty]
    private string _runningTaskName = "";

    // ── Right side items ──

    [ObservableProperty]
    private int _cursorLine = 1;

    [ObservableProperty]
    private int _cursorColumn = 1;

    [ObservableProperty]
    private string _selectionInfo = "";

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _lineEnding = "CRLF";

    [ObservableProperty]
    private string _encoding = "UTF-8";

    [ObservableProperty]
    private string _languageMode = "BasicLang";

    [ObservableProperty]
    private string _indentation = "Spaces: 4";

    [ObservableProperty]
    private bool _useSpaces = true;

    [ObservableProperty]
    private int _indentSize = 4;

    // ── Contextual items ──

    [ObservableProperty]
    private bool _isDebugging;

    [ObservableProperty]
    private string _debugTarget = "";

    [ObservableProperty]
    private bool _isBuildRunning;

    [ObservableProperty]
    private string _buildStatusText = "";

    [ObservableProperty]
    private bool _showBuildStatus;

    [ObservableProperty]
    private bool _buildSucceeded;

    [ObservableProperty]
    private string _lspStatus = "Stopped";

    [ObservableProperty]
    private bool _isLspRunning;

    [ObservableProperty]
    private int _notificationCount;

    [ObservableProperty]
    private int _mergeConflictCount;

    [ObservableProperty]
    private bool _hasMergeConflicts;

    // ── File type hint ──

    [ObservableProperty]
    private string _fileTypeHint = "";

    [ObservableProperty]
    private bool _hasFileTypeHint;

    // ── Notification center ──

    [ObservableProperty]
    private bool _isNotificationCenterOpen;

    [ObservableProperty]
    private ObservableCollection<NotificationItem> _notifications = new();

    // ── Branch picker ──

    [ObservableProperty]
    private bool _isBranchPickerOpen;

    [ObservableProperty]
    private ObservableCollection<string> _branches = new();

    [ObservableProperty]
    private string _branchSearchText = "";

    // ── Indent picker ──

    [ObservableProperty]
    private bool _isIndentPickerOpen;

    // ── Encoding picker ──

    [ObservableProperty]
    private bool _isEncodingPickerOpen;

    // ── Line ending picker ──

    [ObservableProperty]
    private bool _isLineEndingPickerOpen;

    // ── Language picker ──

    [ObservableProperty]
    private bool _isLanguagePickerOpen;

    [ObservableProperty]
    private string _languageSearchText = "";

    [ObservableProperty]
    private ObservableCollection<string> _availableLanguages = new()
    {
        "BasicLang", "BasicLang Module", "BasicLang Class", "BasicLang Project", "C#", "Visual Basic", "C++", "C++ Header",
        "C", "XML", "JSON", "Plain Text", "Markdown", "HTML", "CSS", "JavaScript",
        "TypeScript", "Python", "Java", "Go", "Rust", "YAML", "TOML", "INI",
        "SQL", "PowerShell", "Bash", "Batch"
    };

    // ── Events for commands that need MainWindow interaction ──
    public event EventHandler? BranchPickerRequested;
    public event EventHandler? SyncRequested;
    public event EventHandler? ShowProblemsRequested;
    public event EventHandler? GoToLineRequested;
    public event EventHandler? ShowNotificationsRequested;
    public event EventHandler? ShowFeedbackRequested;
    public event EventHandler? RestartLspRequested;
    public event EventHandler? ShowRunningTasksRequested;
    public event EventHandler<string>? BranchSwitchRequested;
    public event EventHandler<string>? CreateBranchRequested;
    public event EventHandler<string>? EncodingChangeRequested;
    public event EventHandler<string>? LineEndingChangeRequested;
    public event EventHandler<string>? LanguageModeChangeRequested;

    public event EventHandler? ShowMergeConflictsRequested;

    // Events to notify the MainWindow to show picker popups (legacy compat)
    public event EventHandler? LineEndingClicked;
    public event EventHandler? EncodingClicked;
    public event EventHandler? LanguageModeClicked;
    public event EventHandler? IndentationClicked;

    // ── Build status fade timer ──
    private System.Threading.Timer? _buildStatusTimer;

    /// <summary>
    /// Updates the language mode based on the file extension.
    /// </summary>
    public void UpdateForFile(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            LanguageMode = "Plain Text";
            FileTypeHint = "";
            HasFileTypeHint = false;
            return;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        LanguageMode = ext switch
        {
            ".bas" => "BasicLang",
            ".bl" => "BasicLang",
            ".mod" => "BasicLang Module",
            ".cls" or ".class" => "BasicLang Class",
            ".blproj" => "BasicLang Project",
            ".cs" => "C#",
            ".vb" => "Visual Basic",
            ".cpp" or ".cxx" or ".cc" => "C++",
            ".h" or ".hpp" => "C++ Header",
            ".c" => "C",
            ".xml" => "XML",
            ".json" => "JSON",
            ".txt" => "Plain Text",
            ".md" => "Markdown",
            ".html" or ".htm" => "HTML",
            ".css" => "CSS",
            ".js" => "JavaScript",
            ".ts" => "TypeScript",
            ".py" => "Python",
            ".java" => "Java",
            ".go" => "Go",
            ".rs" => "Rust",
            ".yaml" or ".yml" => "YAML",
            ".toml" => "TOML",
            ".ini" => "INI",
            ".sql" => "SQL",
            ".ps1" => "PowerShell",
            ".sh" => "Bash",
            ".bat" or ".cmd" => "Batch",
            _ => "Plain Text"
        };

        // Set file type hint for BasicLang file types
        UpdateFileTypeHint(filePath, ext);
    }

    /// <summary>
    /// Updates the file type hint shown in the status bar for BasicLang files.
    /// Shows contextual information about .mod (module) and .cls/.class (class) files.
    /// </summary>
    private void UpdateFileTypeHint(string filePath, string ext)
    {
        switch (ext)
        {
            case ".mod":
                FileTypeHint = "This module is globally accessible";
                HasFileTypeHint = true;
                break;
            case ".cls" or ".class":
                // Check if file content starts with Public to determine accessibility
                try
                {
                    if (File.Exists(filePath))
                    {
                        var firstLines = File.ReadLines(filePath).Take(5).ToList();
                        var hasPublic = firstLines.Any(line =>
                        {
                            var trimmed = line.TrimStart();
                            return !trimmed.StartsWith("'") && trimmed.StartsWith("Public", StringComparison.OrdinalIgnoreCase);
                        });
                        FileTypeHint = hasPublic
                            ? "This class is publicly accessible"
                            : "This class requires Import to access";
                    }
                    else
                    {
                        FileTypeHint = "This class requires Import to access";
                    }
                }
                catch
                {
                    FileTypeHint = "This class requires Import to access";
                }
                HasFileTypeHint = true;
                break;
            default:
                FileTypeHint = "";
                HasFileTypeHint = false;
                break;
        }
    }

    /// <summary>
    /// Detects the line ending style from file content.
    /// </summary>
    public void DetectLineEnding(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            LineEnding = "CRLF";
            return;
        }

        var crlfCount = 0;
        var lfCount = 0;

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r' && i + 1 < content.Length && content[i + 1] == '\n')
            {
                crlfCount++;
                i++;
            }
            else if (content[i] == '\n')
            {
                lfCount++;
            }
        }

        LineEnding = crlfCount >= lfCount ? "CRLF" : "LF";
    }

    /// <summary>
    /// Updates the cursor position display.
    /// </summary>
    public void UpdateCursorPosition(int line, int column)
    {
        CursorLine = line;
        CursorColumn = column;
    }

    /// <summary>
    /// Updates the selection info display.
    /// </summary>
    public void UpdateSelection(int selectedChars, int selectedLines)
    {
        if (selectedChars <= 0)
        {
            HasSelection = false;
            SelectionInfo = "";
        }
        else
        {
            HasSelection = true;
            SelectionInfo = selectedLines > 1
                ? $"{selectedLines} lines selected"
                : $"{selectedChars} selected";
        }
    }

    /// <summary>
    /// Updates git branch and sync status.
    /// </summary>
    public void UpdateGitStatus(string? branch, int ahead, int behind, bool isRepo, bool hasUncommitted = false)
    {
        IsGitRepository = isRepo;
        BranchName = branch ?? "";
        SyncUpCount = ahead;
        SyncDownCount = behind;
        HasUncommittedChanges = hasUncommitted;
    }

    /// <summary>
    /// Updates diagnostic counts from error list.
    /// </summary>
    public void UpdateDiagnostics(int errors, int warnings, int info)
    {
        ErrorCount = errors;
        WarningCount = warnings;
        InfoCount = info;
    }

    /// <summary>
    /// Updates debug state.
    /// </summary>
    public void UpdateDebugState(bool isDebugging, string? target)
    {
        IsDebugging = isDebugging;
        DebugTarget = target ?? "";
    }

    /// <summary>
    /// Updates LSP status.
    /// </summary>
    public void UpdateLspStatus(bool isRunning)
    {
        IsLspRunning = isRunning;
        LspStatus = isRunning ? "Running" : "Stopped";
    }

    /// <summary>
    /// Shows build started status.
    /// </summary>
    public void SetBuildStarted()
    {
        _buildStatusTimer?.Dispose();
        _buildStatusTimer = null;
        IsBuildRunning = true;
        ShowBuildStatus = true;
        BuildSucceeded = false;
        BuildStatusText = "Building...";
        IsTaskRunning = true;
        RunningTaskName = "Build";
    }

    /// <summary>
    /// Shows build completed status, auto-fades after 5 seconds.
    /// </summary>
    public void SetBuildCompleted(bool success, string message)
    {
        IsBuildRunning = false;
        BuildSucceeded = success;
        BuildStatusText = message;
        ShowBuildStatus = true;
        IsTaskRunning = false;
        RunningTaskName = "";

        // Auto-hide after 5 seconds
        _buildStatusTimer?.Dispose();
        _buildStatusTimer = new System.Threading.Timer(_ =>
        {
            ShowBuildStatus = false;
            BuildStatusText = "";
        }, null, TimeSpan.FromSeconds(5), System.Threading.Timeout.InfiniteTimeSpan);
    }

    // ── Suppressed notification types ──
    private readonly HashSet<string> _suppressedNotifications = new();

    /// <summary>
    /// Adds a notification to the notification center.
    /// </summary>
    public void AddNotification(string message, NotificationSeverity severity, string source,
        string? details = null, List<NotificationAction>? actions = null, string? suppressKey = null)
    {
        if (suppressKey != null && _suppressedNotifications.Contains(suppressKey))
            return;

        var item = new NotificationItem
        {
            Message = message,
            Severity = severity,
            Source = source,
            Timestamp = DateTime.Now,
            Details = details,
            Actions = actions,
            SuppressKey = suppressKey
        };

        Notifications.Insert(0, item);

        // Limit to last 100 notifications
        while (Notifications.Count > 100)
            Notifications.RemoveAt(Notifications.Count - 1);

        NotificationCount = Notifications.Count(n => !n.IsSuppressed);
    }

    /// <summary>
    /// Suppresses future notifications with the given key.
    /// </summary>
    public void SuppressNotificationType(string suppressKey)
    {
        _suppressedNotifications.Add(suppressKey);
    }

    /// <summary>
    /// Gets notifications grouped by time period for the notification center.
    /// </summary>
    public IEnumerable<IGrouping<string, NotificationItem>> GetGroupedNotifications()
    {
        return Notifications
            .Where(n => !n.IsSuppressed)
            .GroupBy(n => n.TimeGroup);
    }

    /// <summary>
    /// Updates the merge conflict count in the status bar.
    /// </summary>
    public void UpdateMergeConflicts(int count)
    {
        MergeConflictCount = count;
        HasMergeConflicts = count > 0;
    }

    // ── Commands ──

    [RelayCommand]
    private void ShowMergeConflicts()
    {
        ShowMergeConflictsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowBranchPicker()
    {
        CloseAllPickers();
        IsBranchPickerOpen = true;
        BranchPickerRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Sync()
    {
        SyncRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowProblems()
    {
        ShowProblemsRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoToLine()
    {
        GoToLineRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowIndentPicker()
    {
        CloseAllPickers();
        IsIndentPickerOpen = true;
    }

    [RelayCommand]
    private void SetIndentSpaces(string sizeStr)
    {
        if (int.TryParse(sizeStr, out var size))
        {
            UseSpaces = true;
            IndentSize = size;
            UpdateIndentationDisplay();
        }
        IsIndentPickerOpen = false;
    }

    [RelayCommand]
    private void SetIndentTabs(string sizeStr)
    {
        if (int.TryParse(sizeStr, out var size))
        {
            UseSpaces = false;
            IndentSize = size;
            UpdateIndentationDisplay();
        }
        IsIndentPickerOpen = false;
    }

    [RelayCommand]
    private void ShowEncodingPicker()
    {
        CloseAllPickers();
        IsEncodingPickerOpen = true;
    }

    [RelayCommand]
    private void SetEncoding(string encoding)
    {
        Encoding = encoding;
        IsEncodingPickerOpen = false;
        EncodingChangeRequested?.Invoke(this, encoding);
    }

    [RelayCommand]
    private void ShowLineEndingPicker()
    {
        CloseAllPickers();
        IsLineEndingPickerOpen = true;
    }

    [RelayCommand]
    private void SetLineEnding(string lineEnding)
    {
        LineEnding = lineEnding;
        IsLineEndingPickerOpen = false;
        LineEndingChangeRequested?.Invoke(this, lineEnding);
    }

    [RelayCommand]
    private void ShowLanguagePicker()
    {
        CloseAllPickers();
        IsLanguagePickerOpen = true;
        LanguageSearchText = "";
    }

    [RelayCommand]
    private void SetLanguageMode(string mode)
    {
        LanguageMode = mode;
        IsLanguagePickerOpen = false;
        LanguageModeChangeRequested?.Invoke(this, mode);
    }

    [RelayCommand]
    private void ShowNotifications()
    {
        CloseAllPickers();
        IsNotificationCenterOpen = !IsNotificationCenterOpen;
        if (IsNotificationCenterOpen)
        {
            NotificationCount = 0;
        }
    }

    [RelayCommand]
    private void DismissNotification(NotificationItem? item)
    {
        if (item != null)
        {
            Notifications.Remove(item);
        }
    }

    [RelayCommand]
    private void ClearAllNotifications()
    {
        Notifications.Clear();
        NotificationCount = 0;
    }

    [RelayCommand]
    private void ShowFeedback()
    {
        ShowFeedbackRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RestartLsp()
    {
        RestartLspRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SwitchBranch(string? branchName)
    {
        if (!string.IsNullOrEmpty(branchName))
        {
            IsBranchPickerOpen = false;
            BranchSwitchRequested?.Invoke(this, branchName);
        }
    }

    [RelayCommand]
    private void CreateNewBranch()
    {
        IsBranchPickerOpen = false;
        CreateBranchRequested?.Invoke(this, "");
    }

    [RelayCommand]
    private void ShowRunningTasks()
    {
        ShowRunningTasksRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClosePickers()
    {
        CloseAllPickers();
    }

    // Legacy commands for backward compat
    [RelayCommand]
    private void CycleLineEnding()
    {
        LineEnding = LineEnding == "CRLF" ? "LF" : "CRLF";
        LineEndingClicked?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CycleEncoding()
    {
        Encoding = Encoding switch
        {
            "UTF-8" => "UTF-8 with BOM",
            "UTF-8 with BOM" => "UTF-16 LE",
            "UTF-16 LE" => "UTF-16 BE",
            "UTF-16 BE" => "ASCII",
            "ASCII" => "UTF-8",
            _ => "UTF-8"
        };
        EncodingClicked?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ShowLanguageMode()
    {
        LanguageModeClicked?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CycleIndentation()
    {
        if (UseSpaces)
        {
            IndentSize = IndentSize switch
            {
                2 => 4,
                4 => 8,
                8 => 2,
                _ => 4
            };
            if (IndentSize == 2 && _previousIndentSize == 8)
            {
                UseSpaces = false;
                IndentSize = 4;
            }
        }
        else
        {
            UseSpaces = true;
            IndentSize = 2;
        }

        UpdateIndentationDisplay();
        IndentationClicked?.Invoke(this, EventArgs.Empty);
    }

    private int _previousIndentSize = 4;

    partial void OnIndentSizeChanged(int value)
    {
        _previousIndentSize = value;
    }

    private void UpdateIndentationDisplay()
    {
        Indentation = UseSpaces ? $"Spaces: {IndentSize}" : $"Tab Size: {IndentSize}";
    }

    private void CloseAllPickers()
    {
        IsBranchPickerOpen = false;
        IsIndentPickerOpen = false;
        IsEncodingPickerOpen = false;
        IsLineEndingPickerOpen = false;
        IsLanguagePickerOpen = false;
        IsNotificationCenterOpen = false;
    }
}

/// <summary>
/// Represents a single notification in the notification center.
/// </summary>
public class NotificationItem
{
    public string Message { get; set; } = "";
    public NotificationSeverity Severity { get; set; }
    public string Source { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string? Details { get; set; }
    public List<NotificationAction>? Actions { get; set; }
    public bool IsSuppressed { get; set; }
    public string? SuppressKey { get; set; }

    public string TimeText
    {
        get
        {
            var span = DateTime.Now - Timestamp;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            return Timestamp.ToString("MMM d");
        }
    }

    /// <summary>
    /// Returns a time group label for grouping in the notification center.
    /// </summary>
    public string TimeGroup
    {
        get
        {
            var today = DateTime.Today;
            if (Timestamp.Date == today) return "Today";
            if (Timestamp.Date == today.AddDays(-1)) return "Yesterday";
            return "Earlier";
        }
    }

    public string SeverityIcon => Severity switch
    {
        NotificationSeverity.Error => "\u274C",
        NotificationSeverity.Warning => "\u26A0",
        NotificationSeverity.Progress => "\u23F3",
        _ => "\u2139"
    };

    public string SeverityColor => Severity switch
    {
        NotificationSeverity.Error => "#E51400",
        NotificationSeverity.Warning => "#F0AD4E",
        NotificationSeverity.Progress => "#58A6FF",
        _ => "#58A6FF"
    };
}

public enum NotificationSeverity
{
    Info,
    Warning,
    Error,
    Progress
}
