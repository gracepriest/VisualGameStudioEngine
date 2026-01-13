namespace VisualGameStudio.Core.TextMate;

/// <summary>
/// Service for managing TextMate grammars and themes
/// </summary>
public interface ITextMateService
{
    /// <summary>
    /// Load a TextMate grammar from a file
    /// </summary>
    Task<TextMateGrammarInfo?> LoadGrammarAsync(string grammarPath);

    /// <summary>
    /// Load a TextMate grammar from JSON content
    /// </summary>
    TextMateGrammarInfo? LoadGrammarFromJson(string scopeName, string jsonContent);

    /// <summary>
    /// Get grammar for a language ID
    /// </summary>
    TextMateGrammarInfo? GetGrammarForLanguage(string languageId);

    /// <summary>
    /// Get grammar for a file extension
    /// </summary>
    TextMateGrammarInfo? GetGrammarForExtension(string extension);

    /// <summary>
    /// Register a grammar
    /// </summary>
    void RegisterGrammar(TextMateGrammarInfo grammar);

    /// <summary>
    /// Get all registered grammars
    /// </summary>
    IReadOnlyList<TextMateGrammarInfo> GetAllGrammars();

    /// <summary>
    /// Convert a VS Code theme to TextMate format
    /// </summary>
    TextMateTheme? ConvertVSCodeTheme(string themePath);

    /// <summary>
    /// Get current theme
    /// </summary>
    TextMateTheme? CurrentTheme { get; }

    /// <summary>
    /// Set the current theme
    /// </summary>
    void SetTheme(TextMateTheme theme);
}

/// <summary>
/// Information about a TextMate grammar
/// </summary>
public class TextMateGrammarInfo
{
    /// <summary>
    /// The scope name (e.g., "source.python", "source.csharp")
    /// </summary>
    public string ScopeName { get; set; } = "";

    /// <summary>
    /// Language ID this grammar applies to
    /// </summary>
    public string LanguageId { get; set; } = "";

    /// <summary>
    /// Display name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// File extensions this grammar applies to
    /// </summary>
    public List<string> FileExtensions { get; set; } = new();

    /// <summary>
    /// File names this grammar applies to (e.g., "Makefile")
    /// </summary>
    public List<string> FileNames { get; set; } = new();

    /// <summary>
    /// First line pattern for detection
    /// </summary>
    public string? FirstLineMatch { get; set; }

    /// <summary>
    /// Path to the grammar file
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Source extension ID
    /// </summary>
    public string? SourceExtension { get; set; }

    /// <summary>
    /// The parsed grammar patterns
    /// </summary>
    public List<TextMatePattern> Patterns { get; set; } = new();

    /// <summary>
    /// Repository of reusable patterns
    /// </summary>
    public Dictionary<string, TextMatePattern> Repository { get; set; } = new();

    /// <summary>
    /// Raw JSON content of the grammar
    /// </summary>
    public string? RawContent { get; set; }
}

/// <summary>
/// A pattern in a TextMate grammar
/// </summary>
public class TextMatePattern
{
    /// <summary>
    /// Name/scope of the pattern (e.g., "keyword.control")
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Regular expression to match
    /// </summary>
    public string? Match { get; set; }

    /// <summary>
    /// Begin pattern for multi-line matches
    /// </summary>
    public string? Begin { get; set; }

    /// <summary>
    /// End pattern for multi-line matches
    /// </summary>
    public string? End { get; set; }

    /// <summary>
    /// While pattern for continuing matches
    /// </summary>
    public string? While { get; set; }

    /// <summary>
    /// Reference to another pattern
    /// </summary>
    public string? Include { get; set; }

    /// <summary>
    /// Captures for the match
    /// </summary>
    public Dictionary<string, TextMateCapture>? Captures { get; set; }

    /// <summary>
    /// Captures for the begin pattern
    /// </summary>
    public Dictionary<string, TextMateCapture>? BeginCaptures { get; set; }

    /// <summary>
    /// Captures for the end pattern
    /// </summary>
    public Dictionary<string, TextMateCapture>? EndCaptures { get; set; }

    /// <summary>
    /// Nested patterns
    /// </summary>
    public List<TextMatePattern>? Patterns { get; set; }

    /// <summary>
    /// Name to apply to content between begin/end
    /// </summary>
    public string? ContentName { get; set; }
}

/// <summary>
/// A capture group in a pattern
/// </summary>
public class TextMateCapture
{
    /// <summary>
    /// Name/scope for this capture
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Nested patterns for this capture
    /// </summary>
    public List<TextMatePattern>? Patterns { get; set; }
}

/// <summary>
/// A TextMate theme
/// </summary>
public class TextMateTheme
{
    /// <summary>
    /// Theme name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Theme ID
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Whether this is a dark theme
    /// </summary>
    public bool IsDark { get; set; }

    /// <summary>
    /// Editor colors
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>
    /// Token color rules
    /// </summary>
    public List<TextMateTokenColor> TokenColors { get; set; } = new();
}

/// <summary>
/// Token color rule in a theme
/// </summary>
public class TextMateTokenColor
{
    /// <summary>
    /// Description/name of this rule
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Scopes this rule applies to
    /// </summary>
    public List<string> Scope { get; set; } = new();

    /// <summary>
    /// Settings for matching tokens
    /// </summary>
    public TextMateTokenSettings Settings { get; set; } = new();
}

/// <summary>
/// Settings for a token color
/// </summary>
public class TextMateTokenSettings
{
    /// <summary>
    /// Foreground color
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Background color
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Font style (bold, italic, underline)
    /// </summary>
    public string? FontStyle { get; set; }
}
