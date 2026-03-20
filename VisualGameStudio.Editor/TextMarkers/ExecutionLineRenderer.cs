using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders a yellow/orange background highlight on the current execution line during debugging.
/// This complements the yellow arrow in the breakpoint margin gutter.
/// </summary>
public class ExecutionLineRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private int? _executionLine;

    private static readonly IBrush ExecutionLineBrush =
        new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xCC, 0x00)); // semi-transparent yellow

    private static readonly IPen ExecutionLineBorderPen =
        new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xCC, 0x00)), 1);

    public KnownLayer Layer => KnownLayer.Background;

    public ExecutionLineRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>
    /// Sets or clears the current execution line.
    /// Pass null to clear the highlight.
    /// </summary>
    public void SetExecutionLine(int? line)
    {
        _executionLine = line;
        _editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!_executionLine.HasValue || !textView.VisualLinesValid)
            return;

        var lineNumber = _executionLine.Value;
        if (lineNumber < 1 || lineNumber > _editor.Document.LineCount)
            return;

        foreach (var visualLine in textView.VisualLines)
        {
            if (visualLine.FirstDocumentLine.LineNumber == lineNumber)
            {
                var y = visualLine.VisualTop - textView.ScrollOffset.Y;
                var rect = new Rect(0, y, textView.Bounds.Width, visualLine.Height);

                drawingContext.FillRectangle(ExecutionLineBrush, rect);
                drawingContext.DrawRectangle(ExecutionLineBorderPen, rect);
                break;
            }
        }
    }
}
