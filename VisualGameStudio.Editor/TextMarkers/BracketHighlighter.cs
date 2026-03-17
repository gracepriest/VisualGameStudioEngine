using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Highlights matching brackets in the editor
/// </summary>
public class BracketHighlighter : DocumentColorizingTransformer
{
    private static readonly Dictionary<char, char> MatchingBrackets = new()
    {
        { '(', ')' },
        { ')', '(' },
        { '[', ']' },
        { ']', '[' },
        { '{', '}' },
        { '}', '{' }
    };

    private static readonly HashSet<char> OpeningBrackets = new() { '(', '[', '{' };

    private int _highlightOffset1 = -1;
    private int _highlightOffset2 = -1;

    public void UpdateBracketHighlight(TextDocument document, int caretOffset)
    {
        _highlightOffset1 = -1;
        _highlightOffset2 = -1;

        if (document == null || caretOffset < 0 || caretOffset > document.TextLength)
            return;

        // Check character before caret
        if (caretOffset > 0)
        {
            var charBefore = document.GetCharAt(caretOffset - 1);
            if (MatchingBrackets.ContainsKey(charBefore))
            {
                var matchOffset = FindMatchingBracket(document, caretOffset - 1, charBefore);
                if (matchOffset >= 0)
                {
                    _highlightOffset1 = caretOffset - 1;
                    _highlightOffset2 = matchOffset;
                    return;
                }
            }
        }

        // Check character at caret
        if (caretOffset < document.TextLength)
        {
            var charAt = document.GetCharAt(caretOffset);
            if (MatchingBrackets.ContainsKey(charAt))
            {
                var matchOffset = FindMatchingBracket(document, caretOffset, charAt);
                if (matchOffset >= 0)
                {
                    _highlightOffset1 = caretOffset;
                    _highlightOffset2 = matchOffset;
                }
            }
        }
    }

    private int FindMatchingBracket(TextDocument document, int offset, char bracket)
    {
        var isOpening = OpeningBrackets.Contains(bracket);
        var matchingBracket = MatchingBrackets[bracket];
        var direction = isOpening ? 1 : -1;
        var depth = 1;

        var pos = offset + direction;
        const int maxScanDistance = 10000;
        var scanned = 0;
        while (pos >= 0 && pos < document.TextLength && scanned < maxScanDistance)
        {
            var c = document.GetCharAt(pos);

            // Skip strings and comments
            if (c == '\'')
            {
                // BasicLang comment — skip to end/start of line
                if (direction == 1)
                {
                    // Forward: skip to end of line
                    while (pos < document.TextLength && scanned < maxScanDistance)
                    {
                        c = document.GetCharAt(pos);
                        if (c == '\n') break;
                        pos += direction;
                        scanned++;
                    }
                }
                else
                {
                    // Backward: skip to start of line (we're inside a comment)
                    while (pos >= 0 && scanned < maxScanDistance)
                    {
                        c = document.GetCharAt(pos);
                        if (c == '\n') break;
                        pos += direction;
                        scanned++;
                    }
                }
            }
            else if (c == '"')
            {
                // Skip to matching quote
                pos += direction;
                scanned++;
                while (pos >= 0 && pos < document.TextLength && scanned < maxScanDistance)
                {
                    c = document.GetCharAt(pos);
                    if (c == '"')
                        break;
                    pos += direction;
                    scanned++;
                }
            }
            else if (c == bracket)
            {
                depth++;
            }
            else if (c == matchingBracket)
            {
                depth--;
                if (depth == 0)
                    return pos;
            }

            pos += direction;
            scanned++;
        }

        return -1;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (_highlightOffset1 < 0 || _highlightOffset2 < 0)
            return;

        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;

        // Highlight first bracket
        if (_highlightOffset1 >= lineStart && _highlightOffset1 < lineEnd)
        {
            ChangeLinePart(_highlightOffset1, _highlightOffset1 + 1, ApplyBracketHighlight);
        }

        // Highlight second bracket
        if (_highlightOffset2 >= lineStart && _highlightOffset2 < lineEnd)
        {
            ChangeLinePart(_highlightOffset2, _highlightOffset2 + 1, ApplyBracketHighlight);
        }
    }

    private void ApplyBracketHighlight(VisualLineElement element)
    {
        element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.Parse("#3A3A3A")));
        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.Parse("#FFD700")));
    }
}
