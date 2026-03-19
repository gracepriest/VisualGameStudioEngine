using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Shell.ViewModels;

/// <summary>
/// View model for an individual tab in the tab strip.
/// Wraps a CodeEditorDocumentViewModel with additional tab-specific state.
/// </summary>
public partial class TabItemViewModel : ObservableObject
{
    private readonly CodeEditorDocumentViewModel _document;

    public TabItemViewModel(CodeEditorDocumentViewModel document)
    {
        _document = document;
        _title = document.Title;
        _filePath = document.FilePath;
        _isModified = document.IsDirty;
        _isActive = false;

        // Wire up document change notifications
        document.DirtyChanged += (s, e) =>
        {
            IsModified = document.IsDirty;
            Title = document.Title;
        };
        document.TitleChanged += (s, e) =>
        {
            Title = document.Title;
        };
    }

    /// <summary>
    /// The underlying document view model.
    /// </summary>
    public CodeEditorDocumentViewModel Document => _document;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isPreview;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isReadOnly;

    [ObservableProperty]
    private bool _hasErrors;

    /// <summary>
    /// Git status for tab decoration (M = modified, A = added, D = deleted, etc.).
    /// </summary>
    [ObservableProperty]
    private string? _gitStatus;

    /// <summary>
    /// The last time this tab was activated (for MRU ordering).
    /// </summary>
    public DateTime LastActivated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Display name (just the filename portion).
    /// </summary>
    public string FileName => FilePath != null ? Path.GetFileName(FilePath) : "Untitled";

    /// <summary>
    /// The directory portion of the file path, for disambiguation.
    /// </summary>
    public string? DirectoryPath => FilePath != null ? Path.GetDirectoryName(FilePath) : null;

    /// <summary>
    /// File extension for icon resolution.
    /// </summary>
    public string Extension => FilePath != null ? Path.GetExtension(FilePath).ToLowerInvariant() : "";

    /// <summary>
    /// Icon text glyph based on file extension.
    /// </summary>
    public string IconGlyph => Extension switch
    {
        ".bas" or ".bl" => "B",
        ".cs" => "C#",
        ".vb" => "VB",
        ".cpp" or ".c" or ".h" or ".hpp" => "C",
        ".xml" or ".xaml" or ".axaml" => "<>",
        ".json" => "{}",
        ".txt" or ".md" => "T",
        ".sln" or ".csproj" or ".blproj" => "P",
        _ => "F"
    };

    /// <summary>
    /// Icon color based on file extension.
    /// </summary>
    public string IconColor => Extension switch
    {
        ".bas" or ".bl" => "#569CD6",
        ".cs" => "#68B723",
        ".vb" => "#945DB7",
        ".cpp" or ".c" or ".h" or ".hpp" => "#F0AD4E",
        ".xml" or ".xaml" or ".axaml" => "#E37933",
        ".json" => "#DCDCAA",
        _ => "#CCCCCC"
    };

    /// <summary>
    /// Git decoration color for the tab.
    /// </summary>
    public string GitDecorationColor => GitStatus switch
    {
        "M" => "#E2C08D",   // Modified - yellow
        "A" => "#89D185",   // Added - green
        "D" => "#C74E39",   // Deleted - red
        "U" => "#E51400",   // Untracked - bright red
        "C" => "#73C991",   // Copied - light green
        "R" => "#73C991",   // Renamed - light green
        _ => "Transparent"
    };

    /// <summary>
    /// Whether to show the git decoration dot on the tab.
    /// </summary>
    public bool ShowGitDecoration => !string.IsNullOrEmpty(GitStatus);

    partial void OnGitStatusChanged(string? value)
    {
        OnPropertyChanged(nameof(GitDecorationColor));
        OnPropertyChanged(nameof(ShowGitDecoration));
    }

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(DirectoryPath));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(IconGlyph));
        OnPropertyChanged(nameof(IconColor));
    }

    partial void OnIsModifiedChanged(bool value)
    {
        OnPropertyChanged(nameof(Title));
    }
}
