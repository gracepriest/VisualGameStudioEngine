using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders an inline git blame annotation after the end of the current line,
/// similar to VS Code GitLens style. Only shows for a single line at a time.
/// </summary>
public class InlineBlameRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private int _lineNumber;
    private string _annotationText = "";

    public KnownLayer Layer => KnownLayer.Selection;

    public InlineBlameRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>
    /// Sets the blame annotation to display on the specified line.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="annotationText">The blame annotation text to display.</param>
    public void SetAnnotation(int lineNumber, string annotationText)
    {
        _lineNumber = lineNumber;
        _annotationText = annotationText;
        _editor?.TextArea?.TextView?.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>
    /// Clears the inline blame annotation.
    /// </summary>
    public void Clear()
    {
        _lineNumber = 0;
        _annotationText = "";
        _editor?.TextArea?.TextView?.InvalidateLayer(KnownLayer.Selection);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _lineNumber < 1 || string.IsNullOrEmpty(_annotationText))
            return;

        try
        {
            if (_lineNumber > _editor.Document.LineCount) return;

            var docLine = _editor.Document.GetLineByNumber(_lineNumber);

            foreach (var visualLine in textView.VisualLines)
            {
                if (visualLine.FirstDocumentLine.LineNumber != _lineNumber) continue;

                // Position after the end of the line text
                var lineEndOffset = docLine.EndOffset - visualLine.FirstDocumentLine.Offset;
                var endPos = visualLine.GetVisualPosition(lineEndOffset, VisualYPosition.TextTop);

                var typeface = new Typeface("Cascadia Code, Consolas, monospace");
                var fontSize = _editor.FontSize * 0.85;
                var textBrush = new SolidColorBrush(Color.FromArgb(140, 128, 128, 128)); // Subtle gray
                var bgBrush = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128));    // Very subtle background

                var displayText = "    " + _annotationText;

                var formattedText = new FormattedText(
                    displayText,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    textBrush);

                var rect = new Rect(
                    endPos.X + 24, // Gap after line end
                    endPos.Y,
                    formattedText.Width + 8,
                    formattedText.Height);

                drawingContext.FillRectangle(bgBrush, rect, 3);
                drawingContext.DrawText(formattedText, new Point(rect.X + 4, rect.Y));
                break;
            }
        }
        catch
        {
            // Skip rendering errors
        }
    }
}
