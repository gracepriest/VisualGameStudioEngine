namespace VisualGameStudio.Editor.Highlighting;

/// <summary>
/// Stores language configuration from a VS Code language-configuration.json file.
/// Used for bracket matching, comment toggling, auto-close pairs, and surround pairs.
/// </summary>
public class LanguageConfigurationData
{
    /// <summary>
    /// Language ID this configuration applies to (e.g., "python").
    /// </summary>
    public string LanguageId { get; set; } = "";

    /// <summary>
    /// Single-line comment prefix (e.g., "//", "#", "'").
    /// </summary>
    public string? LineComment { get; set; }

    /// <summary>
    /// Block comment start token (e.g., "/*", "<!--").
    /// </summary>
    public string? BlockCommentStart { get; set; }

    /// <summary>
    /// Block comment end token (e.g., "*/", "-->").
    /// </summary>
    public string? BlockCommentEnd { get; set; }

    /// <summary>
    /// Bracket pairs for matching (open, close).
    /// </summary>
    public List<(string Open, string Close)> Brackets { get; set; } = new();

    /// <summary>
    /// Pairs that auto-close when the opening character is typed.
    /// </summary>
    public List<(string Open, string Close)> AutoClosingPairs { get; set; } = new();

    /// <summary>
    /// Pairs used for surround-with functionality (wrapping selected text).
    /// </summary>
    public List<(string Open, string Close)> SurroundingPairs { get; set; } = new();

    /// <summary>
    /// Regex pattern that marks the start of a foldable region.
    /// </summary>
    public string? FoldingStartMarker { get; set; }

    /// <summary>
    /// Regex pattern that marks the end of a foldable region.
    /// </summary>
    public string? FoldingEndMarker { get; set; }

    /// <summary>
    /// Regex pattern for lines that should increase indent.
    /// </summary>
    public string? IncreaseIndentPattern { get; set; }

    /// <summary>
    /// Regex pattern for lines that should decrease indent.
    /// </summary>
    public string? DecreaseIndentPattern { get; set; }

    /// <summary>
    /// Word pattern regex for word selection.
    /// </summary>
    public string? WordPattern { get; set; }
}
