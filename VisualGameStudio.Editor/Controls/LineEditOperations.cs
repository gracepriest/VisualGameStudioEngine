using System;
using AvaloniaEdit.Document;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// Pure line-based document transformations, split out of <see cref="CodeEditorControl"/> so
/// they can be unit-tested against a <see cref="TextDocument"/> without a running UI. The
/// control computes the target line range (from the caret/selection) and delegates here.
/// </summary>
public static class LineEditOperations
{
    /// <summary>
    /// Comments or uncomments the inclusive line range using BasicLang apostrophe comments.
    /// If every non-blank line in the range is already commented it uncomments; otherwise it
    /// comments each non-blank line. Blank lines are ignored when deciding, so a block that
    /// contains blank lines still round-trips (comment then uncomment) correctly.
    /// </summary>
    public static void ToggleLineComment(TextDocument document, int startLine, int endLine)
    {
        if (document == null || startLine < 1 || startLine > endLine || endLine > document.LineCount)
            return;

        var allCommented = true;
        for (var line = startLine; line <= endLine; line++)
        {
            var docLine = document.GetLineByNumber(line);
            var trimmed = document.GetText(docLine.Offset, docLine.Length).TrimStart();
            if (trimmed.Length == 0) continue; // blank lines don't count against "all commented"
            if (!IsCommented(trimmed)) { allCommented = false; break; }
        }

        document.BeginUpdate();
        try
        {
            for (var line = startLine; line <= endLine; line++)
            {
                var docLine = document.GetLineByNumber(line);
                var lineText = document.GetText(docLine.Offset, docLine.Length);
                var trimmed = lineText.TrimStart();
                var leadingWhitespace = lineText.Substring(0, lineText.Length - trimmed.Length);

                if (allCommented)
                {
                    var uncommented = StripComment(trimmed);
                    if (uncommented != null)
                    {
                        document.Replace(docLine.Offset, docLine.Length, leadingWhitespace + uncommented);
                    }
                }
                else if (trimmed.Length > 0)
                {
                    document.Replace(docLine.Offset, docLine.Length, leadingWhitespace + "' " + trimmed);
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Deletes the inclusive line range including the trailing newline(s). When the range
    /// reaches the last line, the preceding newline is removed instead so no blank line is
    /// left behind.
    /// </summary>
    public static void DeleteLineRange(TextDocument document, int startLine, int endLine)
    {
        if (document == null || startLine < 1 || startLine > endLine || endLine > document.LineCount)
            return;

        document.BeginUpdate();
        try
        {
            var first = document.GetLineByNumber(startLine);

            if (endLine >= document.LineCount)
            {
                var last = document.GetLineByNumber(endLine);
                var startOffset = startLine > 1
                    ? document.GetLineByNumber(startLine - 1).EndOffset
                    : first.Offset;
                document.Remove(startOffset, last.EndOffset - startOffset);
            }
            else
            {
                // Remove from the block start up to the start of the line after the block,
                // which cleanly takes the block's trailing newline(s) with it.
                var nextLineOffset = document.GetLineByNumber(endLine + 1).Offset;
                document.Remove(first.Offset, nextLineOffset - first.Offset);
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    private static bool IsCommented(string trimmed) =>
        trimmed.StartsWith("'") ||
        trimmed.StartsWith("REM ", StringComparison.OrdinalIgnoreCase) ||
        trimmed.Equals("REM", StringComparison.OrdinalIgnoreCase);

    /// <summary>Strips a leading comment marker, or returns null if the line isn't commented.</summary>
    private static string? StripComment(string trimmed)
    {
        if (trimmed.StartsWith("' ")) return trimmed.Substring(2);
        if (trimmed.StartsWith("'")) return trimmed.Substring(1);
        if (trimmed.StartsWith("REM ", StringComparison.OrdinalIgnoreCase)) return trimmed.Substring(4);
        if (trimmed.Equals("REM", StringComparison.OrdinalIgnoreCase)) return "";
        return null;
    }
}
