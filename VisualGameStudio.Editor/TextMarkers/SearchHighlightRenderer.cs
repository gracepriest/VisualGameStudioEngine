using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Background renderer that highlights all search matches in the editor.
/// Draws translucent rectangles behind matching text, with the current match
/// highlighted in a different (brighter) color.
/// </summary>
public class SearchHighlightRenderer : IBackgroundRenderer
{
    private readonly TextDocument _document;
    private readonly List<SearchMatch> _matches = new();
    private int _currentMatchIndex = -1;

    private static readonly Color MatchColor = Color.Parse("#44FFD700");
    private static readonly Color CurrentMatchColor = Color.Parse("#88FFD700");
    private static readonly Color MatchBorderColor = Color.Parse("#66FFD700");

    public SearchHighlightRenderer(TextDocument document)
    {
        _document = document;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<SearchMatch> Matches => _matches.AsReadOnly();

    public int CurrentMatchIndex
    {
        get => _currentMatchIndex;
        set => _currentMatchIndex = value;
    }

    public int MatchCount => _matches.Count;

    /// <summary>
    /// Updates the search highlights for the given search parameters.
    /// Returns the total number of matches found.
    /// </summary>
    public int UpdateMatches(string searchText, bool matchCase, bool wholeWord, bool useRegex)
    {
        _matches.Clear();
        _currentMatchIndex = -1;

        if (string.IsNullOrEmpty(searchText))
            return 0;

        var text = _document.Text;
        if (string.IsNullOrEmpty(text))
            return 0;

        try
        {
            if (useRegex)
            {
                var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex(searchText, options);
                foreach (Match match in regex.Matches(text))
                {
                    if (match.Length > 0)
                    {
                        _matches.Add(new SearchMatch(match.Index, match.Length));
                    }
                }
            }
            else
            {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var index = 0;
                while (index < text.Length)
                {
                    var found = text.IndexOf(searchText, index, comparison);
                    if (found < 0) break;

                    if (!wholeWord || IsWholeWord(text, found, searchText.Length))
                    {
                        _matches.Add(new SearchMatch(found, searchText.Length));
                    }
                    index = found + 1;
                }
            }
        }
        catch (RegexParseException)
        {
            // Invalid regex pattern
        }

        return _matches.Count;
    }

    /// <summary>
    /// Clears all search highlights.
    /// </summary>
    public void ClearMatches()
    {
        _matches.Clear();
        _currentMatchIndex = -1;
    }

    /// <summary>
    /// Finds the match index closest to (at or after) the given offset.
    /// </summary>
    public int FindMatchIndexAtOrAfter(int offset)
    {
        for (int i = 0; i < _matches.Count; i++)
        {
            if (_matches[i].StartOffset >= offset)
                return i;
        }
        return _matches.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Finds the match index closest to (at or before) the given offset.
    /// </summary>
    public int FindMatchIndexAtOrBefore(int offset)
    {
        for (int i = _matches.Count - 1; i >= 0; i--)
        {
            if (_matches[i].StartOffset <= offset)
                return i;
        }
        return _matches.Count > 0 ? _matches.Count - 1 : -1;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_matches.Count == 0 || !textView.VisualLinesValid)
            return;

        var matchBrush = new SolidColorBrush(MatchColor);
        var currentBrush = new SolidColorBrush(CurrentMatchColor);
        var borderPen = new Pen(new SolidColorBrush(MatchBorderColor), 1);

        foreach (var visualLine in textView.VisualLines)
        {
            var lineStart = visualLine.FirstDocumentLine.Offset;
            var lineEnd = visualLine.LastDocumentLine.EndOffset;

            for (int i = 0; i < _matches.Count; i++)
            {
                var match = _matches[i];
                if (match.EndOffset < lineStart || match.StartOffset > lineEnd)
                    continue;

                var segmentStart = Math.Max(match.StartOffset, lineStart);
                var segmentEnd = Math.Min(match.EndOffset, lineEnd);

                var rects = BackgroundGeometryBuilder.GetRectsForSegment(
                    textView,
                    new SimpleSegment(segmentStart, segmentEnd - segmentStart));

                var isCurrent = (i == _currentMatchIndex);
                var brush = isCurrent ? currentBrush : matchBrush;

                foreach (var rect in rects)
                {
                    var highlightRect = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
                    drawingContext.FillRectangle(brush, highlightRect);
                    if (isCurrent)
                    {
                        drawingContext.DrawRectangle(borderPen, highlightRect);
                    }
                }
            }
        }
    }

    private static bool IsWholeWord(string text, int offset, int length)
    {
        var start = offset > 0 && char.IsLetterOrDigit(text[offset - 1]);
        var end = offset + length < text.Length && char.IsLetterOrDigit(text[offset + length]);
        return !start && !end;
    }
}

/// <summary>
/// Represents a single search match in the document.
/// </summary>
public class SearchMatch
{
    public int StartOffset { get; }
    public int Length { get; }
    public int EndOffset => StartOffset + Length;

    public SearchMatch(int startOffset, int length)
    {
        StartOffset = startOffset;
        Length = length;
    }
}
