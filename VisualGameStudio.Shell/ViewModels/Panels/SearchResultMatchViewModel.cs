using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Represents a single search match within a file in the Find in Files results tree.
/// </summary>
public partial class SearchResultMatchViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private int _lineNumber;

    [ObservableProperty]
    private int _column;

    [ObservableProperty]
    private int _matchLength;

    [ObservableProperty]
    private string _lineText = "";

    [ObservableProperty]
    private string _previewBefore = "";

    [ObservableProperty]
    private string _matchText = "";

    [ObservableProperty]
    private string _previewAfter = "";

    /// <summary>
    /// Formatted line number for display (right-aligned).
    /// </summary>
    public string LineNumberText => LineNumber.ToString();
}
