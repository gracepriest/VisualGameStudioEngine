using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VisualGameStudio.Shell.ViewModels.Panels;

/// <summary>
/// Represents a file with search matches in the Find in Files results tree.
/// </summary>
public partial class SearchResultFileViewModel : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private string _relativePath = "";

    [ObservableProperty]
    private int _matchCount;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private ObservableCollection<SearchResultMatchViewModel> _matches = new();

    /// <summary>
    /// Display text for the file node: "filename (N matches)"
    /// </summary>
    public string DisplayText => $"{FileName} ({MatchCount} {(MatchCount == 1 ? "match" : "matches")})";

    /// <summary>
    /// File icon based on extension.
    /// </summary>
    public string FileIcon
    {
        get
        {
            var ext = System.IO.Path.GetExtension(FileName).ToLowerInvariant();
            return ext switch
            {
                ".bas" or ".bl" or ".basic" => "\u25B6", // source
                ".cs" => "#",
                ".vb" => "V",
                ".cpp" or ".c" or ".h" or ".hpp" => "C",
                ".xml" or ".xaml" or ".axaml" => "<>",
                ".json" => "{ }",
                ".txt" or ".md" => "\u2630",
                _ => "\u25A1"
            };
        }
    }
}
