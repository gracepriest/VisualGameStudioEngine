namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides code formatting services for BasicLang source files.
/// </summary>
public interface ICodeFormattingService
{
    /// <summary>
    /// Gets or sets the formatting options.
    /// </summary>
    FormattingOptions Options { get; set; }

    /// <summary>
    /// Formats the entire source code.
    /// </summary>
    /// <param name="sourceCode">The source code to format.</param>
    /// <returns>The formatted source code.</returns>
    string FormatDocument(string sourceCode);

    /// <summary>
    /// Formats a selection of source code.
    /// </summary>
    /// <param name="sourceCode">The entire source code.</param>
    /// <param name="startLine">The start line (1-based).</param>
    /// <param name="endLine">The end line (1-based).</param>
    /// <returns>The formatted source code.</returns>
    string FormatSelection(string sourceCode, int startLine, int endLine);

    /// <summary>
    /// Formats a single line of code.
    /// </summary>
    /// <param name="line">The line to format.</param>
    /// <param name="indentLevel">The current indent level.</param>
    /// <returns>The formatted line.</returns>
    string FormatLine(string line, int indentLevel);

    /// <summary>
    /// Calculates the indent level for a line based on context.
    /// </summary>
    /// <param name="sourceCode">The source code context.</param>
    /// <param name="lineNumber">The line number (1-based).</param>
    /// <returns>The calculated indent level.</returns>
    int CalculateIndentLevel(string sourceCode, int lineNumber);

    /// <summary>
    /// Removes trailing whitespace from all lines.
    /// </summary>
    /// <param name="sourceCode">The source code.</param>
    /// <returns>The source code with trailing whitespace removed.</returns>
    string RemoveTrailingWhitespace(string sourceCode);

    /// <summary>
    /// Normalizes line endings to the specified style.
    /// </summary>
    /// <param name="sourceCode">The source code.</param>
    /// <param name="lineEnding">The line ending to use.</param>
    /// <returns>The source code with normalized line endings.</returns>
    string NormalizeLineEndings(string sourceCode, LineEndingStyle lineEnding);

    /// <summary>
    /// Validates if the source code is properly formatted according to options.
    /// </summary>
    /// <param name="sourceCode">The source code to validate.</param>
    /// <returns>A list of formatting issues found.</returns>
    IReadOnlyList<FormattingIssue> ValidateFormatting(string sourceCode);
}

/// <summary>
/// Options for code formatting.
/// </summary>
public class FormattingOptions
{
    /// <summary>
    /// Gets or sets whether to use tabs for indentation (false = spaces).
    /// </summary>
    public bool UseTabs { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of spaces per indent level.
    /// </summary>
    public int IndentSize { get; set; } = 4;

    /// <summary>
    /// Gets or sets the tab width for display purposes.
    /// </summary>
    public int TabWidth { get; set; } = 4;

    /// <summary>
    /// Gets or sets whether to trim trailing whitespace.
    /// </summary>
    public bool TrimTrailingWhitespace { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to insert a final newline.
    /// </summary>
    public bool InsertFinalNewline { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum line length (0 = no limit).
    /// </summary>
    public int MaxLineLength { get; set; } = 120;

    /// <summary>
    /// Gets or sets the line ending style.
    /// </summary>
    public LineEndingStyle LineEnding { get; set; } = LineEndingStyle.SystemDefault;

    /// <summary>
    /// Gets or sets whether to add a space after keywords.
    /// </summary>
    public bool SpaceAfterKeywords { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to add spaces around operators.
    /// </summary>
    public bool SpaceAroundOperators { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to add a space after commas.
    /// </summary>
    public bool SpaceAfterCommas { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to align consecutive assignments.
    /// </summary>
    public bool AlignConsecutiveAssignments { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to place opening keywords on their own line.
    /// </summary>
    public bool OpeningKeywordOnNewLine { get; set; } = false;
}

/// <summary>
/// Line ending styles.
/// </summary>
public enum LineEndingStyle
{
    /// <summary>Use system default line endings.</summary>
    SystemDefault,
    /// <summary>Use LF line endings (Unix).</summary>
    LF,
    /// <summary>Use CRLF line endings (Windows).</summary>
    CRLF,
    /// <summary>Use CR line endings (old Mac).</summary>
    CR
}

/// <summary>
/// Represents a formatting issue found during validation.
/// </summary>
public class FormattingIssue
{
    /// <summary>
    /// Gets or sets the line number (1-based).
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Gets or sets the column number (1-based).
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the issue type.
    /// </summary>
    public FormattingIssueType Type { get; set; }

    /// <summary>
    /// Gets or sets the message describing the issue.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Gets or sets the suggested fix.
    /// </summary>
    public string? SuggestedFix { get; set; }
}

/// <summary>
/// Types of formatting issues.
/// </summary>
public enum FormattingIssueType
{
    /// <summary>Incorrect indentation.</summary>
    Indentation,
    /// <summary>Trailing whitespace present.</summary>
    TrailingWhitespace,
    /// <summary>Line exceeds maximum length.</summary>
    LineTooLong,
    /// <summary>Inconsistent spacing.</summary>
    Spacing,
    /// <summary>Missing or extra blank lines.</summary>
    BlankLines,
    /// <summary>Inconsistent line endings.</summary>
    LineEnding,
    /// <summary>Missing final newline.</summary>
    MissingFinalNewline
}
