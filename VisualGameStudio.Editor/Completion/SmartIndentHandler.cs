using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Provides smart auto-indentation for BasicLang, matching the VS Code extension's
/// indentationRules and onEnterRules from language-configuration.json.
///
/// Increase indent after: Sub, Function, Class, Module, Namespace, If, Else, ElseIf,
///   For, While, Do, Select, Case, Try, Catch, Finally, With, Get, Set, Property
/// Decrease indent for: End Sub/Function/Class/etc, Else, ElseIf, Case, Catch, Finally, Next, Loop
/// </summary>
public static class SmartIndentHandler
{
    // Keywords that increase indentation on the NEXT line
    private static readonly Regex IncreaseIndentPattern = new(
        @"^\s*(Sub|Function|Class|Module|Namespace|If|Else|ElseIf|For|While|Do|Select|Case|Try|Catch|Finally|With|Get|Set|Property)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Keywords on current line that should decrease indentation of THAT line
    private static readonly Regex DecreaseIndentPattern = new(
        @"^\s*(End\s+(Sub|Function|Class|Module|Namespace|If|For|While|Do|Select|Try|With|Get|Set|Property)|Else\b|ElseIf\b|Case\b|Catch\b|Finally\b|Next\b|Loop\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Calculates the indentation for a new line after pressing Enter.
    /// </summary>
    /// <param name="currentLineText">The text of the line the cursor is currently on (before Enter)</param>
    /// <param name="indentSize">Number of spaces per indent level</param>
    /// <param name="useTabs">Whether to use tabs instead of spaces</param>
    /// <returns>The indentation string for the new line</returns>
    public static string GetNewLineIndent(string currentLineText, int indentSize = 4, bool useTabs = false)
    {
        // Get current line's indentation
        var currentIndent = GetLineIndent(currentLineText);
        var currentIndentLevel = GetIndentLevel(currentIndent, indentSize);

        // Check if the current line should increase indent for the next line
        if (IncreaseIndentPattern.IsMatch(currentLineText))
        {
            currentIndentLevel++;
        }

        return BuildIndent(currentIndentLevel, indentSize, useTabs);
    }

    /// <summary>
    /// Checks if a just-typed line should be outdented (decreased indent).
    /// Called when the user types a keyword like "End Sub", "Else", "Next", etc.
    /// </summary>
    /// <param name="lineText">The current text of the line being edited</param>
    /// <param name="previousLineText">The text of the line above</param>
    /// <param name="indentSize">Number of spaces per indent level</param>
    /// <param name="useTabs">Whether to use tabs instead of spaces</param>
    /// <returns>The corrected indentation for the line, or null if no change needed</returns>
    public static string? GetCorrectedIndent(string lineText, string previousLineText, int indentSize = 4, bool useTabs = false)
    {
        var trimmed = lineText.TrimStart();

        // Check if this line matches a decrease-indent pattern
        if (!DecreaseIndentPattern.IsMatch(lineText))
            return null;

        // For Else/ElseIf/Case/Catch/Finally, they should be at the SAME level
        // as the opening statement (If/Select/Try), not indented inside it.
        // For End Xxx/Next/Loop, they should be at the same level as the opening statement.
        var prevIndent = GetLineIndent(previousLineText);
        var prevIndentLevel = GetIndentLevel(prevIndent, indentSize);

        // If previous line would increase indent, the current "closer" should stay at previous level
        if (IncreaseIndentPattern.IsMatch(previousLineText))
        {
            return BuildIndent(prevIndentLevel, indentSize, useTabs) + trimmed;
        }

        // Otherwise, outdent one level from previous
        var newLevel = Math.Max(0, prevIndentLevel - 1);
        return BuildIndent(newLevel, indentSize, useTabs) + trimmed;
    }

    /// <summary>
    /// Determines if the given keyword (just typed at end of a word) should trigger an outdent check.
    /// </summary>
    public static bool IsOutdentTriggerWord(string word)
    {
        var lower = word.Trim().ToLowerInvariant();
        return lower is "end" or "else" or "elseif" or "case" or "catch"
            or "finally" or "next" or "loop";
    }

    /// <summary>
    /// Extracts the leading whitespace from a line.
    /// </summary>
    public static string GetLineIndent(string lineText)
    {
        int i = 0;
        while (i < lineText.Length && (lineText[i] == ' ' || lineText[i] == '\t'))
            i++;
        return lineText.Substring(0, i);
    }

    /// <summary>
    /// Converts an indent string to an indent level count.
    /// </summary>
    public static int GetIndentLevel(string indent, int indentSize = 4)
    {
        int spaces = 0;
        foreach (var c in indent)
        {
            if (c == '\t') spaces += indentSize;
            else spaces++;
        }
        return spaces / indentSize;
    }

    /// <summary>
    /// Builds an indentation string for a given level.
    /// </summary>
    public static string BuildIndent(int level, int indentSize = 4, bool useTabs = false)
    {
        if (level <= 0) return "";
        if (useTabs) return new string('\t', level);
        return new string(' ', level * indentSize);
    }
}
