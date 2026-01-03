namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides TextMate grammar support for syntax highlighting.
/// Enables using VS Code/TextMate grammar files (.tmLanguage, .json, .plist).
/// </summary>
public interface ITextMateService
{
    /// <summary>
    /// Gets the loaded grammars.
    /// </summary>
    IReadOnlyDictionary<string, TextMateGrammar> Grammars { get; }

    /// <summary>
    /// Gets the registered themes.
    /// </summary>
    IReadOnlyDictionary<string, TextMateTheme> Themes { get; }

    /// <summary>
    /// Gets or sets the current theme.
    /// </summary>
    TextMateTheme? CurrentTheme { get; set; }

    /// <summary>
    /// Loads a grammar from a file.
    /// </summary>
    /// <param name="filePath">Path to the grammar file (.json, .tmLanguage, .plist).</param>
    /// <returns>The loaded grammar, or null if failed.</returns>
    Task<TextMateGrammar?> LoadGrammarAsync(string filePath);

    /// <summary>
    /// Loads a grammar from JSON content.
    /// </summary>
    /// <param name="json">The JSON grammar content.</param>
    /// <param name="scopeName">The scope name for the grammar.</param>
    /// <returns>The loaded grammar, or null if failed.</returns>
    TextMateGrammar? LoadGrammarFromJson(string json, string scopeName);

    /// <summary>
    /// Registers a grammar for a file extension.
    /// </summary>
    /// <param name="extension">The file extension (e.g., ".js").</param>
    /// <param name="scopeName">The grammar scope name.</param>
    void RegisterExtension(string extension, string scopeName);

    /// <summary>
    /// Gets the grammar for a file extension.
    /// </summary>
    /// <param name="extension">The file extension.</param>
    /// <returns>The grammar, or null if not found.</returns>
    TextMateGrammar? GetGrammarForExtension(string extension);

    /// <summary>
    /// Loads a theme from a file.
    /// </summary>
    /// <param name="filePath">Path to the theme file.</param>
    /// <returns>The loaded theme, or null if failed.</returns>
    Task<TextMateTheme?> LoadThemeAsync(string filePath);

    /// <summary>
    /// Loads a theme from JSON content.
    /// </summary>
    /// <param name="json">The JSON theme content.</param>
    /// <param name="name">The theme name.</param>
    /// <returns>The loaded theme, or null if failed.</returns>
    TextMateTheme? LoadThemeFromJson(string json, string name);

    /// <summary>
    /// Tokenizes a line of text.
    /// </summary>
    /// <param name="line">The line of text.</param>
    /// <param name="grammar">The grammar to use.</param>
    /// <param name="previousState">The state from the previous line.</param>
    /// <returns>The tokenization result.</returns>
    TokenizationResult TokenizeLine(string line, TextMateGrammar grammar, TokenizerState? previousState = null);

    /// <summary>
    /// Tokenizes an entire document.
    /// </summary>
    /// <param name="content">The document content.</param>
    /// <param name="grammar">The grammar to use.</param>
    /// <returns>Tokens for each line.</returns>
    IReadOnlyList<TokenizationResult> TokenizeDocument(string content, TextMateGrammar grammar);

    /// <summary>
    /// Gets the style for a scope.
    /// </summary>
    /// <param name="scopes">The scope stack.</param>
    /// <returns>The resolved style.</returns>
    TokenStyle? GetStyleForScopes(IEnumerable<string> scopes);

    /// <summary>
    /// Raised when a grammar is loaded.
    /// </summary>
    event EventHandler<GrammarLoadedEventArgs>? GrammarLoaded;

    /// <summary>
    /// Raised when the theme changes.
    /// </summary>
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
}

#region TextMate Types

/// <summary>
/// A TextMate grammar for syntax highlighting.
/// </summary>
public class TextMateGrammar
{
    /// <summary>
    /// Gets or sets the scope name (e.g., "source.js").
    /// </summary>
    public string ScopeName { get; set; } = "";

    /// <summary>
    /// Gets or sets the grammar name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the file types this grammar applies to.
    /// </summary>
    public List<string> FileTypes { get; set; } = new();

    /// <summary>
    /// Gets or sets the patterns.
    /// </summary>
    public List<TextMatePattern> Patterns { get; set; } = new();

    /// <summary>
    /// Gets or sets the repository of reusable patterns.
    /// </summary>
    public Dictionary<string, TextMatePattern> Repository { get; set; } = new();

    /// <summary>
    /// Gets or sets the first line match regex.
    /// </summary>
    public string? FirstLineMatch { get; set; }

    /// <summary>
    /// Gets or sets the fold start marker.
    /// </summary>
    public string? FoldingStartMarker { get; set; }

    /// <summary>
    /// Gets or sets the fold end marker.
    /// </summary>
    public string? FoldingEndMarker { get; set; }

    /// <summary>
    /// Gets or sets the file path this grammar was loaded from.
    /// </summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// A TextMate pattern for matching.
/// </summary>
public class TextMatePattern
{
    /// <summary>
    /// Gets or sets the pattern name (scope).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the match regex (for single-line patterns).
    /// </summary>
    public string? Match { get; set; }

    /// <summary>
    /// Gets or sets the begin regex (for multi-line patterns).
    /// </summary>
    public string? Begin { get; set; }

    /// <summary>
    /// Gets or sets the end regex (for multi-line patterns).
    /// </summary>
    public string? End { get; set; }

    /// <summary>
    /// Gets or sets the while regex (for while patterns).
    /// </summary>
    public string? While { get; set; }

    /// <summary>
    /// Gets or sets the content name.
    /// </summary>
    public string? ContentName { get; set; }

    /// <summary>
    /// Gets or sets the captures for match.
    /// </summary>
    public Dictionary<string, CapturePattern>? Captures { get; set; }

    /// <summary>
    /// Gets or sets the begin captures.
    /// </summary>
    public Dictionary<string, CapturePattern>? BeginCaptures { get; set; }

    /// <summary>
    /// Gets or sets the end captures.
    /// </summary>
    public Dictionary<string, CapturePattern>? EndCaptures { get; set; }

    /// <summary>
    /// Gets or sets the while captures.
    /// </summary>
    public Dictionary<string, CapturePattern>? WhileCaptures { get; set; }

    /// <summary>
    /// Gets or sets nested patterns.
    /// </summary>
    public List<TextMatePattern>? Patterns { get; set; }

    /// <summary>
    /// Gets or sets an include reference.
    /// </summary>
    public string? Include { get; set; }

    /// <summary>
    /// Gets or sets whether to apply end pattern last.
    /// </summary>
    public bool ApplyEndPatternLast { get; set; }
}

/// <summary>
/// A capture pattern.
/// </summary>
public class CapturePattern
{
    /// <summary>
    /// Gets or sets the name (scope).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets nested patterns.
    /// </summary>
    public List<TextMatePattern>? Patterns { get; set; }
}

/// <summary>
/// A TextMate theme.
/// </summary>
public class TextMateTheme
{
    /// <summary>
    /// Gets or sets the theme name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the theme type (light/dark).
    /// </summary>
    public string Type { get; set; } = "dark";

    /// <summary>
    /// Gets or sets the token colors.
    /// </summary>
    public List<TokenColorRule> TokenColors { get; set; } = new();

    /// <summary>
    /// Gets or sets the editor colors.
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();

    /// <summary>
    /// Gets or sets the semantic token colors.
    /// </summary>
    public Dictionary<string, TokenStyle>? SemanticTokenColors { get; set; }
}

/// <summary>
/// A token color rule in a theme.
/// </summary>
public class TokenColorRule
{
    /// <summary>
    /// Gets or sets the rule name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the scope(s) this rule applies to.
    /// </summary>
    public object? Scope { get; set; }

    /// <summary>
    /// Gets the scopes as a list.
    /// </summary>
    public List<string> GetScopes()
    {
        if (Scope is string s)
            return new List<string> { s };
        if (Scope is List<string> list)
            return list;
        return new List<string>();
    }

    /// <summary>
    /// Gets or sets the style settings.
    /// </summary>
    public TokenStyle Settings { get; set; } = new();
}

/// <summary>
/// Token style settings.
/// </summary>
public class TokenStyle
{
    /// <summary>
    /// Gets or sets the foreground color.
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public string? Background { get; set; }

    /// <summary>
    /// Gets or sets the font style (bold, italic, underline).
    /// </summary>
    public string? FontStyle { get; set; }

    /// <summary>
    /// Gets whether this is bold.
    /// </summary>
    public bool IsBold => FontStyle?.Contains("bold", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets whether this is italic.
    /// </summary>
    public bool IsItalic => FontStyle?.Contains("italic", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Gets whether this is underlined.
    /// </summary>
    public bool IsUnderline => FontStyle?.Contains("underline", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Tokenizer state for continuation.
/// </summary>
public class TokenizerState
{
    /// <summary>
    /// Gets or sets the rule stack.
    /// </summary>
    public List<string> RuleStack { get; set; } = new();

    /// <summary>
    /// Gets or sets the scope stack.
    /// </summary>
    public List<string> ScopeStack { get; set; } = new();

    /// <summary>
    /// Creates a copy of this state.
    /// </summary>
    public TokenizerState Clone()
    {
        return new TokenizerState
        {
            RuleStack = new List<string>(RuleStack),
            ScopeStack = new List<string>(ScopeStack)
        };
    }

    /// <summary>
    /// Checks if this state equals another.
    /// </summary>
    public bool Equals(TokenizerState? other)
    {
        if (other == null) return false;
        return RuleStack.SequenceEqual(other.RuleStack) && ScopeStack.SequenceEqual(other.ScopeStack);
    }
}

/// <summary>
/// Result of tokenizing a line.
/// </summary>
public class TokenizationResult
{
    /// <summary>
    /// Gets or sets the tokens found.
    /// </summary>
    public List<TextMateToken> Tokens { get; set; } = new();

    /// <summary>
    /// Gets or sets the ending state.
    /// </summary>
    public TokenizerState EndState { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the line is within a multi-line construct.
    /// </summary>
    public bool IsMultiLine { get; set; }
}

/// <summary>
/// A token from TextMate tokenization.
/// </summary>
public class TextMateToken
{
    /// <summary>
    /// Gets or sets the start index.
    /// </summary>
    public int StartIndex { get; set; }

    /// <summary>
    /// Gets or sets the end index.
    /// </summary>
    public int EndIndex { get; set; }

    /// <summary>
    /// Gets the token length.
    /// </summary>
    public int Length => EndIndex - StartIndex;

    /// <summary>
    /// Gets or sets the scope stack for this token.
    /// </summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>
    /// Gets the most specific scope.
    /// </summary>
    public string? Scope => Scopes.LastOrDefault();
}

#endregion

#region Event Args

/// <summary>
/// Event args for grammar loaded.
/// </summary>
public class GrammarLoadedEventArgs : EventArgs
{
    public TextMateGrammar Grammar { get; }
    public string? FilePath { get; }

    public GrammarLoadedEventArgs(TextMateGrammar grammar, string? filePath = null)
    {
        Grammar = grammar;
        FilePath = filePath;
    }
}

/// <summary>
/// Event args for theme changed.
/// </summary>
public class ThemeChangedEventArgs : EventArgs
{
    public TextMateTheme? OldTheme { get; }
    public TextMateTheme? NewTheme { get; }

    public ThemeChangedEventArgs(TextMateTheme? oldTheme, TextMateTheme? newTheme)
    {
        OldTheme = oldTheme;
        NewTheme = newTheme;
    }
}

#endregion
