using System;
using AvaloniaEdit.Document;
using AvaloniaEdit.Indentation;

namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// An <see cref="IIndentationStrategy"/> whose indent width and tab/space mode can be
/// reconfigured after construction. Both the BasicLang and the C-style strategies
/// implement this so the editor can swap between them by file language while pushing
/// the user's indentation settings to whichever one is active.
/// </summary>
public interface IConfigurableIndentationStrategy : IIndentationStrategy
{
    int IndentSize { get; set; }
    bool UseTabs { get; set; }
}

/// <summary>
/// Pure, UI-free logic for C-style (curly-brace) auto-indentation, matching VS Code's
/// on-enter behavior for C/C++:
/// <list type="bullet">
///   <item>indent one level after a line whose code ends with <c>{</c>;</item>
///   <item>align a lone <c>}</c> with the line that opened the block;</item>
///   <item>otherwise copy the previous line's indentation;</item>
///   <item>pressing Enter with the caret between <c>{</c> and <c>}</c> opens an indented
///     blank body line and pushes the <c>}</c> down (see <see cref="BuildBraceExpansion"/>).</item>
/// </list>
/// Kept separate from <see cref="SmartIndentHandler"/> (BasicLang) on purpose: the two
/// languages have unrelated block syntax and must not share indent rules.
/// </summary>
public static class CStyleIndentation
{
    /// <summary>
    /// Indentation for the line created by pressing Enter, given the previous line and
    /// the (possibly non-empty) content already on the new line.
    /// </summary>
    public static string GetNewLineIndent(string prevLineText, string currentLineText, int indentSize = 4, bool useTabs = false)
    {
        var prevIndent = GetLineIndent(prevLineText);
        var opens = OpensBlock(prevLineText);
        var closes = currentLineText.TrimStart().StartsWith("}", StringComparison.Ordinal);

        if (closes)
            return opens ? prevIndent : Dedent(prevIndent, indentSize, useTabs);

        return opens ? Indent(prevIndent, indentSize, useTabs) : prevIndent;
    }

    /// <summary>
    /// Text to insert (and where the caret should land within it) when Enter is pressed
    /// with the caret directly between an opening <c>{</c> and a closing <c>}</c>.
    /// Produces "&lt;newline&gt;&lt;body indent&gt;&lt;newline&gt;&lt;base indent&gt;" so the closer
    /// drops to its own line and the caret rests on the indented body line.
    /// </summary>
    /// <param name="baseIndent">Leading whitespace of the line the <c>{</c> sits on.</param>
    /// <param name="newLine">The document's newline sequence (e.g. "\r\n" or "\n").</param>
    /// <returns>(text to insert, caret offset within that text).</returns>
    public static (string text, int caretOffset) BuildBraceExpansion(string baseIndent, string newLine, int indentSize = 4, bool useTabs = false)
    {
        var body = Indent(baseIndent, indentSize, useTabs);
        var text = newLine + body + newLine + baseIndent;
        var caretOffset = (newLine + body).Length;
        return (text, caretOffset);
    }

    /// <summary>
    /// The given indentation reduced by one level (never below column zero). Used when the
    /// user types <c>}</c> on an otherwise-blank line so the closer snaps back one level.
    /// </summary>
    public static string GetDedentedIndent(string currentIndent, int indentSize = 4, bool useTabs = false)
        => Dedent(currentIndent, indentSize, useTabs);

    /// <summary>Number of leading whitespace characters in the line.</summary>
    public static int LeadingWhitespaceLength(string lineText)
    {
        int i = 0;
        while (i < lineText.Length && (lineText[i] == ' ' || lineText[i] == '\t'))
            i++;
        return i;
    }

    /// <summary>Leading whitespace of the line.</summary>
    public static string GetLineIndent(string lineText)
        => lineText.Substring(0, LeadingWhitespaceLength(lineText));

    /// <summary>
    /// True when the previous line's code (ignoring a trailing <c>//</c> line comment)
    /// ends with an opening brace.
    /// </summary>
    private static bool OpensBlock(string prevLineText)
    {
        var code = prevLineText.TrimEnd();
        if (code.EndsWith("{", StringComparison.Ordinal))
            return true;

        // Tolerate a trailing line comment: "... {   // note"
        var commentIndex = code.IndexOf("//", StringComparison.Ordinal);
        if (commentIndex >= 0)
            code = code.Substring(0, commentIndex).TrimEnd();

        return code.EndsWith("{", StringComparison.Ordinal);
    }

    private static string Indent(string baseIndent, int indentSize, bool useTabs)
        => baseIndent + Unit(indentSize, useTabs);

    private static string Unit(int indentSize, bool useTabs)
        => useTabs ? "\t" : new string(' ', Math.Max(1, indentSize));

    private static string Dedent(string indent, int indentSize, bool useTabs)
    {
        int size = Math.Max(1, indentSize);
        int width = 0;
        foreach (var c in indent)
            width += c == '\t' ? size : 1;

        int newLevel = Math.Max(0, (width / size) - 1);
        return useTabs ? new string('\t', newLevel) : new string(' ', newLevel * size);
    }
}

/// <summary>
/// C-style indentation strategy for C/C++ files. Applies <see cref="CStyleIndentation"/>
/// to the line AvaloniaEdit creates on Enter. Brace-expansion (Enter between an empty
/// <c>{ }</c>) is handled by the editor control's key handler, which needs precise caret
/// control the <see cref="IIndentationStrategy"/> contract cannot express.
/// </summary>
public class CStyleIndentationStrategy : IConfigurableIndentationStrategy
{
    public int IndentSize { get; set; } = 4;
    public bool UseTabs { get; set; }

    public void IndentLine(TextDocument document, DocumentLine line)
    {
        if (document == null || line == null) return;

        var prev = line.PreviousLine;
        if (prev == null) return; // first line — leave as-is

        var prevText = document.GetText(prev.Offset, prev.Length);
        var currentText = document.GetText(line.Offset, line.Length);

        var newIndent = CStyleIndentation.GetNewLineIndent(prevText, currentText, IndentSize, UseTabs);
        var currentWsLen = CStyleIndentation.LeadingWhitespaceLength(currentText);

        if (currentText.Substring(0, currentWsLen) == newIndent)
            return; // already correct

        // RemoveAndInsert mirrors AvaloniaEdit's DefaultIndentationStrategy: it guarantees the
        // caret ends up behind the freshly inserted indentation rather than in front of it.
        document.Replace(line.Offset, currentWsLen, newIndent, OffsetChangeMappingType.RemoveAndInsert);
    }

    public void IndentLines(TextDocument document, int beginLine, int endLine)
    {
        for (int i = beginLine; i <= endLine; i++)
        {
            if (i >= 1 && i <= document.LineCount)
                IndentLine(document, document.GetLineByNumber(i));
        }
    }
}
