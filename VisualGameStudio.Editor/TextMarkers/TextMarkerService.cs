using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Service for managing text markers (error underlines, warnings, etc.)
/// </summary>
public class TextMarkerService : DocumentColorizingTransformer, IBackgroundRenderer
{
    private readonly TextDocument _document;
    private readonly List<TextMarker> _markers = new();

    public TextMarkerService(TextDocument document)
    {
        _document = document;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    public IReadOnlyList<TextMarker> Markers => _markers.AsReadOnly();

    public TextMarker Create(int startOffset, int length, TextMarkerType type, string? message = null)
    {
        var marker = new TextMarker(startOffset, length, type, message);
        _markers.Add(marker);
        return marker;
    }

    public void Clear()
    {
        _markers.Clear();
    }

    public void Remove(TextMarker marker)
    {
        _markers.Remove(marker);
    }

    public void RemoveAll(Predicate<TextMarker> predicate)
    {
        _markers.RemoveAll(predicate);
    }

    public IEnumerable<TextMarker> GetMarkersAtOffset(int offset)
    {
        return _markers.Where(m => m.StartOffset <= offset && offset <= m.EndOffset);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var lineStart = line.Offset;
        var lineEnd = line.EndOffset;

        foreach (var marker in _markers)
        {
            if (marker.EndOffset < lineStart || marker.StartOffset > lineEnd)
                continue;

            var startCol = Math.Max(marker.StartOffset, lineStart);
            var endCol = Math.Min(marker.EndOffset, lineEnd);

            if (startCol < endCol)
            {
                ChangeLinePart(startCol, endCol, element =>
                {
                    // Apply text decoration for the marker
                    if (marker.Type == TextMarkerType.Error)
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.Parse("#F48771")));
                    }
                    else if (marker.Type == TextMarkerType.Warning)
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.Parse("#CCA700")));
                    }
                });
            }
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid)
            return;

        foreach (var marker in _markers)
        {
            foreach (var rect in GetRects(textView, marker))
            {
                var color = marker.Type switch
                {
                    TextMarkerType.Error => Color.Parse("#E51400"),
                    TextMarkerType.Warning => Color.Parse("#CCA700"),
                    TextMarkerType.Info => Color.Parse("#3794FF"),
                    _ => Color.Parse("#808080")
                };

                var pen = new Pen(new SolidColorBrush(color), 1);

                // Draw squiggly underline
                DrawSquigglyLine(drawingContext, pen, rect);
            }
        }
    }

    private IEnumerable<Rect> GetRects(TextView textView, TextMarker marker)
    {
        var start = marker.StartOffset;
        var end = marker.EndOffset;

        foreach (var line in textView.VisualLines)
        {
            var lineStart = line.FirstDocumentLine.Offset;
            var lineEnd = line.LastDocumentLine.EndOffset;

            if (end < lineStart || start > lineEnd)
                continue;

            var segmentStart = Math.Max(start, lineStart);
            var segmentEnd = Math.Min(end, lineEnd);

            var startPos = line.GetVisualPosition(segmentStart - lineStart, VisualYPosition.LineBottom);
            var endPos = line.GetVisualPosition(segmentEnd - lineStart, VisualYPosition.LineBottom);

            if (endPos.X > startPos.X)
            {
                yield return new Rect(
                    startPos.X,
                    startPos.Y - 2,
                    endPos.X - startPos.X,
                    4);
            }
        }
    }

    private void DrawSquigglyLine(DrawingContext context, Pen pen, Rect rect)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var x = rect.X;
            var y = rect.Y + rect.Height / 2;
            var phase = 0;

            ctx.BeginFigure(new Point(x, y), false);

            while (x < rect.X + rect.Width)
            {
                var nextX = Math.Min(x + 2, rect.X + rect.Width);
                var nextY = y + (phase % 2 == 0 ? 2 : -2);
                ctx.LineTo(new Point(nextX, nextY));
                x = nextX;
                phase++;
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}

public class TextMarker
{
    public int StartOffset { get; }
    public int Length { get; }
    public int EndOffset => StartOffset + Length;
    public TextMarkerType Type { get; }
    public string? Message { get; }

    public TextMarker(int startOffset, int length, TextMarkerType type, string? message = null)
    {
        StartOffset = startOffset;
        Length = length;
        Type = type;
        Message = message;
    }
}

public enum TextMarkerType
{
    Error,
    Warning,
    Info,
    Hint
}
