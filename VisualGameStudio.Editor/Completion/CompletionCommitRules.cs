namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Pure decision rules for what a typed character does to an open completion
/// window.
///
/// The BasicLang server marks most keyword/method items as block SNIPPETS
/// (e.g. the 'For' item carries a whole For...Next body, methods carry
/// "Name($0)"), so committing the selected item from an ordinarily typed
/// character corrupts plain typing: "For" + space must produce "For ", never
/// expand a For-block; "Console.WriteL" + '(' must produce "WriteLine(",
/// never a placeholder-wrapped snippet. The contract is therefore:
///
///   - Identifier characters (and the keys AvaloniaEdit itself owns while the
///     window is open: Enter/Tab) are ignored here — the list refilters in
///     place, and Enter/Tab keep FULL commit semantics including snippet
///     expansion.
///   - Only '.' and '(' commit from typing — and such a commit inserts the
///     item's identifier WORD only, never the snippet body / full InsertText.
///   - Every other character (space, '=', ',', ')', quotes, ...) closes the
///     window WITHOUT committing and inserts normally, matching VS Code's
///     default for a server that declares no commitCharacters.
/// </summary>
public static class CompletionCommitRules
{
    public enum TypedCharAction
    {
        /// <summary>Do nothing: the window refilters in place (identifier chars) or AvaloniaEdit owns the key (Enter/Tab).</summary>
        Ignore,

        /// <summary>Commit the selected item's identifier word, then let the char insert.</summary>
        CommitWord,

        /// <summary>Close the window without committing; the char inserts normally.</summary>
        CloseWithoutCommit
    }

    public static TypedCharAction GetActionForTypedChar(char c)
    {
        if (char.IsLetterOrDigit(c) || c == '_') return TypedCharAction.Ignore;

        // Enter/Tab reach TextEntering on some platforms as control chars —
        // they belong to AvaloniaEdit's completion list (full commit).
        if (c is '\r' or '\n' or '\t') return TypedCharAction.Ignore;

        if (c is '.' or '(') return TypedCharAction.CommitWord;

        return TypedCharAction.CloseWithoutCommit;
    }

    /// <summary>
    /// The word a typed-character commit inserts for the selected item: its
    /// Label when identifier-like, else its FilterText when identifier-like,
    /// else null — meaning the item has no identifier word (e.g. "If...Else")
    /// and a typed character must NOT commit it at all.
    /// </summary>
    public static string? GetCommitWord(string? label, string? filterText)
    {
        if (IsIdentifierWord(label)) return label;
        if (IsIdentifierWord(filterText)) return filterText;
        return null;
    }

    /// <summary>True when text matches ^[A-Za-z_][A-Za-z0-9_]*$.</summary>
    public static bool IsIdentifierWord(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!char.IsAsciiLetter(text[0]) && text[0] != '_') return false;

        for (var i = 1; i < text.Length; i++)
        {
            if (!char.IsAsciiLetterOrDigit(text[i]) && text[i] != '_') return false;
        }

        return true;
    }
}
