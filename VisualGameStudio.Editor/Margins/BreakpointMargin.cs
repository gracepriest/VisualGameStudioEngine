using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.Margins;

/// <summary>
/// Margin that displays breakpoint indicators (red dots) and current execution line (yellow arrow)
/// </summary>
public class BreakpointMargin : AbstractMargin
{
    private readonly TextEditor _editor;
    private HashSet<int> _breakpointLines;
    private int? _currentExecutionLine;

    private static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.Parse("#E51400"));
    private static readonly IBrush DisabledBreakpointBrush = new SolidColorBrush(Color.Parse("#808080"));
    private static readonly IBrush CurrentLineBrush = new SolidColorBrush(Color.Parse("#FFCC00"));
    private static readonly IBrush MarginBackground = new SolidColorBrush(Color.Parse("#1E1E1E"));

    public event EventHandler<int>? BreakpointToggled;

    public BreakpointMargin(TextEditor editor, HashSet<int>? initialBreakpoints = null)
    {
        _editor = editor;
        _breakpointLines = initialBreakpoints ?? new HashSet<int>();
        Width = 16;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void UpdateBreakpoints(HashSet<int> breakpointLines)
    {
        _breakpointLines = breakpointLines;
        InvalidateVisual();
    }

    public void SetCurrentLine(int? line)
    {
        _currentExecutionLine = line;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            var textView = _editor.TextArea.TextView;
            var visualLine = textView.GetVisualLineFromVisualTop(point.Position.Y + textView.ScrollOffset.Y);

            if (visualLine != null)
            {
                var lineNumber = visualLine.FirstDocumentLine.LineNumber;
                BreakpointToggled?.Invoke(this, lineNumber);
                e.Handled = true;
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid)
            return;

        // Draw background
        context.FillRectangle(MarginBackground, new Rect(0, 0, Bounds.Width, Bounds.Height));

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.VisualTop - textView.ScrollOffset.Y;

            // Draw current execution line indicator (yellow arrow)
            if (_currentExecutionLine == lineNumber)
            {
                DrawExecutionArrow(context, y, visualLine.Height);
            }

            // Draw breakpoint indicator (red circle)
            if (_breakpointLines.Contains(lineNumber))
            {
                DrawBreakpoint(context, y, visualLine.Height);
            }
        }
    }

    private void DrawBreakpoint(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(12, lineHeight - 2);
        var x = (Bounds.Width - size) / 2;
        var yPos = y + (lineHeight - size) / 2;

        // Draw filled red circle
        context.DrawEllipse(
            BreakpointBrush,
            null,
            new Rect(x, yPos, size, size));

        // Draw a subtle dark border
        context.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(Color.Parse("#8B0000")), 1),
            new Rect(x, yPos, size, size));
    }

    private void DrawExecutionArrow(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(10, lineHeight - 4);
        var x = 2;
        var yPos = y + (lineHeight - size) / 2;

        // Draw arrow pointing right
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x, yPos), true);
            ctx.LineTo(new Point(x + size, yPos + size / 2));
            ctx.LineTo(new Point(x, yPos + size));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(CurrentLineBrush, new Pen(Brushes.Black, 0.5), geometry);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(16, 0);
    }
}
