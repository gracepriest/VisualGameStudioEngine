using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Background renderer that highlights all search matches in the editor.
/// Draws translucent rectangles behind matching text, with the current match
/// highlighted in a brighter color with a border.
///
/// Supports:
/// - Case-sensitive/insensitive search
/// - Whole word matching
/// - Regular expression search
/// - Search within a selection range
/// - Selection range visualization
/// </summary>
public class SearchHighlightRenderer : IBackgroundRenderer
{
    private readonly TextDocument _document;
    private readonly List<SearchMatch> _matches = new();
    private int _currentMatchIndex = -1;

    // VS Code-style match colors
    private static readonly Color MatchBackground = Color.Parse("#515C6A");
    private static readonly Color CurrentMatchBackground = Color.Parse("#613214");
    private static readonly Color CurrentMatchBorder = Color.Parse("#F0A30A");
    private static readonly Color SelectionRangeColor = Color.Parse("#1A3C5E6B");

    // Selection range for "Find in Selection"
    private bool _hasSelectionRange;
    private int _selectionRangeStart;
    private int _selectionRangeEnd;

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
    /// Sets the selection range for "Find in Selection" mode.
    /// Only matches within this range will be found.
    /// </summary>
    public void SetSelectionRange(int start, int end)
    {
        _hasSelectionRange = true;
        _selectionRangeStart = Math.Min(start, end);
        _selectionRangeEnd = Math.Max(start, end);
    }

    /// <summary>
    /// Clears the selection range, searching the entire document.
    /// </summary>
    public void ClearSelectionRange()
    {
        _hasSelectionRange = false;
        _selectionRangeStart = 0;
        _selectionRangeEnd = 0;
    }

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

        // Determine search bounds
        var searchStart = _hasSelectionRange ? Math.Max(0, _selectionRangeStart) : 0;
        var searchEnd = _hasSelectionRange ? Math.Min(text.Length, _selectionRangeEnd) : text.Length;

        if (searchStart >= searchEnd)
            return 0;

        try
        {
            if (useRegex)
            {
                var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                // Support multiline search with \n
                options |= RegexOptions.Multiline;
                var regex = new Regex(searchText, options);
                var searchRegion = text.Substring(searchStart, searchEnd - searchStart);
                foreach (Match match in regex.Matches(searchRegion))
                {
                    if (match.Length > 0)
                    {
                        _matches.Add(new SearchMatch(searchStart + match.Index, match.Length));
                    }
                }
            }
            else
            {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var index = searchStart;
                while (index < searchEnd)
                {
                    var remaining = searchEnd - index;
                    if (remaining < searchText.Length) break;

                    var found = text.IndexOf(searchText, index, remaining, comparison);
                    if (found < 0 || found + searchText.Length > searchEnd) break;

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
        _hasSelectionRange = false;
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
        if (!textView.VisualLinesValid)
            return;

        // Draw selection range background if "Find in Selection" is active
        if (_hasSelectionRange && _selectionRangeEnd > _selectionRangeStart)
        {
            DrawSelectionRange(textView, drawingContext);
        }

        if (_matches.Count == 0)
            return;

        var matchBrush = new SolidColorBrush(MatchBackground);
        var currentBrush = new SolidColorBrush(CurrentMatchBackground);
        var borderPen = new Pen(new SolidColorBrush(CurrentMatchBorder), 1.5);

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

                    // Draw rounded rectangle for a polished look
                    drawingContext.FillRectangle(brush, highlightRect, 2);

                    if (isCurrent)
                    {
                        drawingContext.DrawRectangle(borderPen, highlightRect, 2);
                    }
                }
            }
        }
    }

    private void DrawSelectionRange(TextView textView, DrawingContext drawingContext)
    {
        var rangeBrush = new SolidColorBrush(SelectionRangeColor);

        foreach (var visualLine in textView.VisualLines)
        {
            var lineStart = visualLine.FirstDocumentLine.Offset;
            var lineEnd = visualLine.LastDocumentLine.EndOffset;

            // Check overlap with selection range
            var overlapStart = Math.Max(lineStart, _selectionRangeStart);
            var overlapEnd = Math.Min(lineEnd, _selectionRangeEnd);

            if (overlapStart >= overlapEnd) continue;

            var rects = BackgroundGeometryBuilder.GetRectsForSegment(
                textView,
                new SimpleSegment(overlapStart, overlapEnd - overlapStart));

            foreach (var rect in rects)
            {
                drawingContext.FillRectangle(rangeBrush, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
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
