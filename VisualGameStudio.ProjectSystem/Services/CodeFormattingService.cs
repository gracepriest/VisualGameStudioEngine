using System.Text;
using System.Text.RegularExpressions;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.ProjectSystem.Services;

/// <summary>
/// Provides code formatting services for BasicLang source files.
/// </summary>
public class CodeFormattingService : ICodeFormattingService
{
    // Keywords that increase indent level
    private static readonly string[] IndentingKeywords = { "Sub", "Function", "Class", "Module", "If", "For", "While", "Do", "Select", "Try", "With", "Structure", "Enum", "Property", "Get", "Set" };

    // Keywords that decrease indent level permanently (block end)
    private static readonly string[] DedentingKeywords = { "End Sub", "End Function", "End Class", "End Module", "End If", "Next", "Wend", "Loop", "End Select", "End Try", "End With", "End Structure", "End Enum", "End Property", "End Get", "End Set" };

    // Keywords that temporarily dedent for the line but content after should be indented (mid-block)
    private static readonly string[] MidBlockKeywords = { "Else", "ElseIf", "Case", "Catch", "Finally" };

    // Operators that should have spaces around them (non-word operators only - word operators like And/Or are handled separately)
    private static readonly string[] SpacedOperators = { "=", "<>", "<=", ">=", "<", ">", "+", "-", "*", "/", "\\", "&" };

    /// <inheritdoc/>
    public FormattingOptions Options { get; set; } = new();

    /// <inheritdoc/>
    public string FormatDocument(string sourceCode)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return sourceCode;
        }

        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new StringBuilder();
        var indentLevel = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedLine = line.Trim();

            // Skip empty lines
            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                result.AppendLine();
                continue;
            }

            // Check for dedenting keywords BEFORE adding indent
            if (StartsWithDedentingKeyword(trimmedLine))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            // Check for mid-block keywords (Else, Catch, etc.) - they appear at parent level but don't change indent
            var isMidBlock = StartsWithMidBlockKeyword(trimmedLine);
            var lineIndentLevel = isMidBlock ? Math.Max(0, indentLevel - 1) : indentLevel;

            // Format the line content
            var formattedContent = FormatLineContent(trimmedLine);

            // Add proper indentation
            var indent = GetIndentString(lineIndentLevel);
            result.AppendLine(indent + formattedContent);

            // Check for indenting keywords AFTER processing the line
            if (StartsWithIndentingKeyword(trimmedLine) && !IsOneLiner(trimmedLine))
            {
                indentLevel++;
            }
        }

        var formatted = result.ToString();

        // Remove trailing whitespace if enabled
        if (Options.TrimTrailingWhitespace)
        {
            formatted = RemoveTrailingWhitespace(formatted);
        }

        // Normalize line endings
        formatted = NormalizeLineEndings(formatted, Options.LineEnding);

        // Handle final newline
        if (Options.InsertFinalNewline && !formatted.EndsWith(GetLineEnding(Options.LineEnding)))
        {
            formatted += GetLineEnding(Options.LineEnding);
        }
        else if (!Options.InsertFinalNewline)
        {
            formatted = formatted.TrimEnd('\r', '\n');
        }

        return formatted;
    }

    /// <inheritdoc/>
    public string FormatSelection(string sourceCode, int startLine, int endLine)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return sourceCode;
        }

        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new StringBuilder();

        // Calculate initial indent level based on context
        var indentLevel = CalculateIndentLevel(sourceCode, startLine);

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];

            if (lineNumber >= startLine && lineNumber <= endLine)
            {
                var trimmedLine = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    result.AppendLine();
                }
                else
                {
                    if (StartsWithDedentingKeyword(trimmedLine))
                    {
                        indentLevel = Math.Max(0, indentLevel - 1);
                    }

                    var formattedContent = FormatLineContent(trimmedLine);
                    var indent = GetIndentString(indentLevel);
                    result.AppendLine(indent + formattedContent);

                    if (StartsWithIndentingKeyword(trimmedLine) && !IsOneLiner(trimmedLine))
                    {
                        indentLevel++;
                    }
                }
            }
            else
            {
                result.AppendLine(line);
            }
        }

        return result.ToString().TrimEnd('\r', '\n');
    }

    /// <inheritdoc/>
    public string FormatLine(string line, int indentLevel)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "";
        }

        var trimmedLine = line.Trim();
        var formattedContent = FormatLineContent(trimmedLine);
        var indent = GetIndentString(indentLevel);

        return indent + formattedContent;
    }

    /// <inheritdoc/>
    public int CalculateIndentLevel(string sourceCode, int lineNumber)
    {
        if (string.IsNullOrEmpty(sourceCode) || lineNumber <= 1)
        {
            return 0;
        }

        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var indentLevel = 0;

        for (int i = 0; i < Math.Min(lineNumber - 1, lines.Length); i++)
        {
            var trimmedLine = lines[i].Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine))
            {
                continue;
            }

            if (StartsWithDedentingKeyword(trimmedLine))
            {
                indentLevel = Math.Max(0, indentLevel - 1);
            }

            if (StartsWithIndentingKeyword(trimmedLine) && !IsOneLiner(trimmedLine))
            {
                indentLevel++;
            }
        }

        return indentLevel;
    }

    /// <inheritdoc/>
    public string RemoveTrailingWhitespace(string sourceCode)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return sourceCode;
        }

        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            result.AppendLine(line.TrimEnd());
        }

        return result.ToString().TrimEnd('\r', '\n');
    }

    /// <inheritdoc/>
    public string NormalizeLineEndings(string sourceCode, LineEndingStyle lineEnding)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return sourceCode;
        }

        // First normalize to LF
        var normalized = sourceCode.Replace("\r\n", "\n").Replace("\r", "\n");

        // Then convert to desired style
        var ending = GetLineEnding(lineEnding);
        return normalized.Replace("\n", ending);
    }

    /// <inheritdoc/>
    public IReadOnlyList<FormattingIssue> ValidateFormatting(string sourceCode)
    {
        var issues = new List<FormattingIssue>();

        if (string.IsNullOrEmpty(sourceCode))
        {
            return issues;
        }

        var lines = sourceCode.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var expectedIndentLevel = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i];
            var trimmedLine = line.Trim();

            // Check for trailing whitespace
            if (Options.TrimTrailingWhitespace && line.Length > 0 && char.IsWhiteSpace(line[^1]))
            {
                issues.Add(new FormattingIssue
                {
                    Line = lineNumber,
                    Column = line.TrimEnd().Length + 1,
                    Type = FormattingIssueType.TrailingWhitespace,
                    Message = "Trailing whitespace detected"
                });
            }

            // Check line length
            if (Options.MaxLineLength > 0 && line.Length > Options.MaxLineLength)
            {
                issues.Add(new FormattingIssue
                {
                    Line = lineNumber,
                    Column = Options.MaxLineLength + 1,
                    Type = FormattingIssueType.LineTooLong,
                    Message = $"Line exceeds maximum length of {Options.MaxLineLength} characters"
                });
            }

            // Check indentation
            if (!string.IsNullOrWhiteSpace(trimmedLine))
            {
                if (StartsWithDedentingKeyword(trimmedLine))
                {
                    expectedIndentLevel = Math.Max(0, expectedIndentLevel - 1);
                }

                var actualIndent = GetLineIndentation(line);
                var expectedIndent = GetIndentString(expectedIndentLevel);

                if (actualIndent != expectedIndent)
                {
                    issues.Add(new FormattingIssue
                    {
                        Line = lineNumber,
                        Column = 1,
                        Type = FormattingIssueType.Indentation,
                        Message = $"Expected indent level {expectedIndentLevel}, found different indentation",
                        SuggestedFix = expectedIndent + trimmedLine
                    });
                }

                if (StartsWithIndentingKeyword(trimmedLine) && !IsOneLiner(trimmedLine))
                {
                    expectedIndentLevel++;
                }
            }
        }

        // Check for final newline
        if (Options.InsertFinalNewline && !sourceCode.EndsWith("\n") && !sourceCode.EndsWith("\r"))
        {
            issues.Add(new FormattingIssue
            {
                Line = lines.Length,
                Column = lines[^1].Length + 1,
                Type = FormattingIssueType.MissingFinalNewline,
                Message = "File should end with a newline"
            });
        }

        return issues;
    }

    private string FormatLineContent(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return "";
        }

        var result = line;

        // Format spacing around operators
        if (Options.SpaceAroundOperators)
        {
            foreach (var op in SpacedOperators.OrderByDescending(o => o.Length))
            {
                // Skip operators that are part of comments
                if (result.TrimStart().StartsWith("'") || result.TrimStart().StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // Handle string literals - don't format inside them
                result = FormatOperatorSpacing(result, op);
            }
        }

        // Format spacing after commas
        if (Options.SpaceAfterCommas)
        {
            result = Regex.Replace(result, @",(\S)", ", $1");
        }

        // Normalize multiple spaces to single space (except indentation)
        result = Regex.Replace(result, @"(?<!^)\s{2,}", " ");

        return result;
    }

    private static string FormatOperatorSpacing(string line, string op)
    {
        // Simple operator spacing - skip if inside a string literal
        var inString = false;
        var result = new StringBuilder();
        var i = 0;

        while (i < line.Length)
        {
            // Track string state
            if (line[i] == '"' && (i == 0 || line[i - 1] != '"'))
            {
                inString = !inString;
                result.Append(line[i]);
                i++;
                continue;
            }

            if (!inString && i + op.Length <= line.Length)
            {
                var potentialOp = line.Substring(i, op.Length);
                if (string.Equals(potentialOp, op, StringComparison.OrdinalIgnoreCase))
                {
                    // Check for compound operators (<=, >=, <>)
                    if (op.Length == 1 && (op == "<" || op == ">") && i + 1 < line.Length)
                    {
                        var nextChar = line[i + 1];
                        if (nextChar == '=' || nextChar == '>')
                        {
                            result.Append(line[i]);
                            i++;
                            continue;
                        }
                    }

                    // Add space before if needed
                    if (result.Length > 0 && !char.IsWhiteSpace(result[^1]))
                    {
                        result.Append(' ');
                    }

                    result.Append(op);

                    // Add space after if needed
                    i += op.Length;
                    if (i < line.Length && !char.IsWhiteSpace(line[i]))
                    {
                        result.Append(' ');
                    }
                    continue;
                }
            }

            result.Append(line[i]);
            i++;
        }

        return result.ToString();
    }

    private string GetIndentString(int level)
    {
        if (level <= 0)
        {
            return "";
        }

        if (Options.UseTabs)
        {
            return new string('\t', level);
        }

        return new string(' ', level * Options.IndentSize);
    }

    private static string GetLineIndentation(string line)
    {
        var indent = new StringBuilder();
        foreach (var c in line)
        {
            if (c == ' ' || c == '\t')
            {
                indent.Append(c);
            }
            else
            {
                break;
            }
        }
        return indent.ToString();
    }

    private static string GetLineEnding(LineEndingStyle style)
    {
        return style switch
        {
            LineEndingStyle.LF => "\n",
            LineEndingStyle.CRLF => "\r\n",
            LineEndingStyle.CR => "\r",
            _ => Environment.NewLine
        };
    }

    private static bool StartsWithIndentingKeyword(string line)
    {
        var trimmed = line.TrimStart();
        foreach (var keyword in IndentingKeywords)
        {
            if (Regex.IsMatch(trimmed, $@"^(Public\s+|Private\s+|Protected\s+|Friend\s+)?{keyword}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool StartsWithDedentingKeyword(string line)
    {
        var trimmed = line.TrimStart();
        foreach (var keyword in DedentingKeywords)
        {
            if (Regex.IsMatch(trimmed, $@"^{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool StartsWithMidBlockKeyword(string line)
    {
        var trimmed = line.TrimStart();
        foreach (var keyword in MidBlockKeywords)
        {
            if (Regex.IsMatch(trimmed, $@"^{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsOneLiner(string line)
    {
        // Check if this is a single-line If statement (If...Then...End If on one line)
        var trimmed = line.Trim();

        // Single-line If: If condition Then statement
        if (Regex.IsMatch(trimmed, @"^If\b.*\bThen\b.+", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(trimmed, @"\bEnd\s+If\b", RegexOptions.IgnoreCase))
        {
            // Has content after Then but no End If - it's an implicit single-liner
            var thenIndex = trimmed.IndexOf("Then", StringComparison.OrdinalIgnoreCase);
            if (thenIndex >= 0 && thenIndex + 4 < trimmed.Length)
            {
                var afterThen = trimmed.Substring(thenIndex + 4).Trim();
                if (!string.IsNullOrEmpty(afterThen) && !afterThen.StartsWith("'"))
                {
                    return true;
                }
            }
        }

        // Explicit single-line with End If
        if (Regex.IsMatch(trimmed, @"^If\b.*\bThen\b.*\bEnd\s+If\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }
}
