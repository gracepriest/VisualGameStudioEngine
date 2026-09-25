using BasicLang.Compiler;

namespace BasicLang.Forms.Recognizer;

/// <summary>
/// Lifts the raw source text of a statement's right-hand side. Shared by both dialects so they
/// cannot disagree about where a value ends.
/// </summary>
internal static class RawSlice
{
    /// <summary>
    /// The RAW source text of the right-hand side, from the token <paramref name="ahead"/> positions
    /// along to the end of the value.
    ///
    /// <para>Raw text rather than a parsed value, because the recognizer is an importer (D12): a
    /// value it cannot interpret — <c>New Font("Segoe UI", 12)</c> — must still survive into the
    /// document, and D9's tiering decides later what is editable. Parsing here would mean discarding
    /// whatever did not fit.</para>
    ///
    /// <para>⚠ The slice stops at a trailing COMMENT, not at the end of the line. Slicing to the
    /// line end swallowed it — <c>lblMessage.Text = "Hi"   ' the greeting</c> produced the property
    /// value <c>"Hi"   ' the greeting</c>, which the writer would then emit back into generated code
    /// as part of the string. Neither shipped template puts a comment on an assignment line, so no
    /// fixture caught it.</para>
    ///
    /// <para>⚠ Known limitation, stated rather than hidden: a value continued across lines with
    /// <c>_</c> is truncated to its first physical line. Handling it properly needs the writer to
    /// round-trip a multi-line value too, so it belongs with the writer rather than here.</para>
    /// </summary>
    public static string RightHandSide(TokenCursor cursor, SourceIndex index, int ahead)
    {
        var first = cursor.Peek(ahead);
        if (first == null || first.Type == TokenType.Newline || first.Type == TokenType.Comment)
        {
            return "";
        }

        var start = index.OffsetOf(first.Line, first.Column);
        var comment = cursor.FirstCommentOnLine(ahead);
        var end = comment != null
            ? index.OffsetOf(comment.Line, comment.Column)
            : index.EndOfLine(first.Line);

        return index.Slice(start, end).Trim();
    }
}
