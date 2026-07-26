using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Editor.Formatting;

/// <summary>
/// Applies LSP <see cref="TextEditInfo"/> edits (formatting, on-type formatting, refactors)
/// to a live <see cref="TextDocument"/> at their real character offsets, in reverse document
/// order, inside a single update.
///
/// This exists specifically to preserve the caret. The previous approach rebuilt the whole
/// buffer and called <c>TextDocument.Replace(0, TextLength, wholeNewText)</c>; AvaloniaEdit
/// maps a caret that sits inside a fully-replaced range to the END of the new text, so any
/// on-type-format (e.g. clangd re-indenting after Enter) yanked the caret to just past the
/// last brace. Targeted replaces only shift the caret when the edit is at or before it, which
/// is exactly how a caret should behave — AvaloniaEdit's <c>Caret</c> tracks each change via
/// <c>GetNewOffset(..., AnchorMovementType.Default)</c>.
/// </summary>
public static class LspTextEditApplier
{
    /// <summary>
    /// Applies <paramref name="edits"/> to <paramref name="document"/>. Edits are applied
    /// bottom-to-top so earlier offsets stay valid; grouped into one undo unit.
    /// Out-of-range or inverted edits are skipped rather than throwing.
    /// </summary>
    public static void Apply(TextDocument document, IReadOnlyList<TextEditInfo> edits)
    {
        if (document == null || edits == null || edits.Count == 0) return;

        var ordered = edits
            .OrderByDescending(e => e.StartLine)
            .ThenByDescending(e => e.StartColumn)
            .ToList();

        document.BeginUpdate();
        try
        {
            foreach (var edit in ordered)
            {
                if (!TryGetOffset(document, edit.StartLine, edit.StartColumn, out var start)) continue;
                if (!TryGetOffset(document, edit.EndLine, edit.EndColumn, out var end)) continue;
                if (end < start) continue;

                document.Replace(start, end - start, edit.NewText ?? string.Empty);
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Converts a 1-based (line, column) position to a document offset, clamping the column
    /// into the line so a server that reports a column past the line end can't throw.
    /// </summary>
    private static bool TryGetOffset(TextDocument document, int line1, int column1, out int offset)
    {
        offset = 0;
        if (line1 < 1 || line1 > document.LineCount) return false;

        var line = document.GetLineByNumber(line1);
        var col = Math.Max(1, column1) - 1; // 0-based within the line
        offset = Math.Min(line.Offset + col, line.EndOffset);
        return true;
    }
}
