using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.MultiCursor;

/// <summary>
/// Renders additional cursors and their selections in the editor
/// </summary>
public class MultiCursorRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private readonly MultiCursorManager _manager;
    private readonly IBrush _cursorBrush;
    private readonly IBrush _selectionBrush;

    public KnownLayer Layer => KnownLayer.Selection;

    public MultiCursorRenderer(TextEditor editor, MultiCursorManager manager)
    {
        _editor = editor;
        _manager = manager;
        _cursorBrush = new SolidColorBrush(Color.Parse("#AEAFAD"));
        _selectionBrush = new SolidColorBrush(Color.Parse("#264F78")) { Opacity = 0.7 };
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!_manager.IsEnabled || textView.Document == null)
            return;

        foreach (var cursor in _manager.Cursors)
        {
            // Draw selection if present
            if (cursor.HasSelection)
            {
                var start = Math.Min(cursor.SelectionStart!.Value, cursor.SelectionEnd!.Value);
                var end = Math.Max(cursor.SelectionStart.Value, cursor.SelectionEnd.Value);

                DrawSelection(textView, drawingContext, start, end);
            }

            // Draw cursor
            DrawCursor(textView, drawingContext, cursor.Offset);
        }
    }

    private void DrawSelection(TextView textView, DrawingContext drawingContext, int start, int end)
    {
        var document = textView.Document;
        if (start < 0 || end > document.TextLength || start >= end)
            return;

        try
        {
            // Get the visual lines that contain the selection
            var startLocation = document.GetLocation(start);
            var endLocation = document.GetLocation(end);

            for (int lineNumber = startLocation.Line; lineNumber <= endLocation.Line; lineNumber++)
            {
                var line = textView.GetVisualLine(lineNumber);
                if (line == null) continue;

                var docLine = document.GetLineByNumber(lineNumber);
                var lineStart = lineNumber == startLocation.Line
                    ? start
                    : docLine.Offset;
                var lineEnd = lineNumber == endLocation.Line
                    ? end
                    : docLine.EndOffset;

                if (lineStart >= lineEnd) continue;

                var startPos = line.GetVisualPosition(lineStart - line.FirstDocumentLine.Offset, VisualYPosition.LineTop);
                var endPos = line.GetVisualPosition(lineEnd - line.FirstDocumentLine.Offset, VisualYPosition.LineTop);

                var rect = new Rect(
                    startPos.X - textView.ScrollOffset.X,
                    startPos.Y - textView.ScrollOffset.Y,
                    endPos.X - startPos.X,
                    line.Height
                );

                drawingContext.FillRectangle(_selectionBrush, rect);
            }
        }
        catch
        {
            // Ignore rendering errors for out-of-view content
        }
    }

    private void DrawCursor(TextView textView, DrawingContext drawingContext, int offset)
    {
        var document = textView.Document;
        if (offset < 0 || offset > document.TextLength)
            return;

        try
        {
            var location = document.GetLocation(offset);
            var line = textView.GetVisualLine(location.Line);
            if (line == null) return;

            var pos = line.GetVisualPosition(offset - line.FirstDocumentLine.Offset, VisualYPosition.LineTop);

            var rect = new Rect(
                pos.X - textView.ScrollOffset.X,
                pos.Y - textView.ScrollOffset.Y,
                2, // cursor width
                line.Height
            );

            drawingContext.FillRectangle(_cursorBrush, rect);
        }
        catch
        {
            // Ignore rendering errors for out-of-view content
        }
    }
}

/// <summary>
/// Handles blinking animation for multiple cursors
/// </summary>
public class MultiCursorBlinkTimer : IDisposable
{
    private readonly System.Timers.Timer _blinkTimer;
    private readonly Action _redrawAction;
    private bool _isVisible = true;

    public bool IsCursorVisible => _isVisible;

    public MultiCursorBlinkTimer(Action redrawAction)
    {
        _redrawAction = redrawAction;
        _blinkTimer = new System.Timers.Timer(500); // 500ms blink rate
        _blinkTimer.Elapsed += OnBlinkTimerElapsed;
        _blinkTimer.Start();
    }

    private void OnBlinkTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        _isVisible = !_isVisible;
        Avalonia.Threading.Dispatcher.UIThread.Post(_redrawAction);
    }

    public void Reset()
    {
        _isVisible = true;
        _blinkTimer.Stop();
        _blinkTimer.Start();
    }

    public void Dispose()
    {
        _blinkTimer.Stop();
        _blinkTimer.Dispose();
    }
}
