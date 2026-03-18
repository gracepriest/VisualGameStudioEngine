using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.Margins;

/// <summary>
/// The visual kind of breakpoint to render.
/// </summary>
public enum BreakpointKind
{
    Normal,
    Conditional,
    HitCount,
    Logpoint
}

/// <summary>
/// Describes the visual appearance of a breakpoint.
/// </summary>
public class BreakpointVisualInfo
{
    public bool IsEnabled { get; set; } = true;
    public bool IsVerified { get; set; } = true;
    public BreakpointKind Kind { get; set; } = BreakpointKind.Normal;
}

/// <summary>
/// Margin that displays breakpoint indicators (red dots, diamonds, etc.) and current execution line (yellow arrow).
/// Supports normal, conditional, hit-count, and logpoint breakpoints with enabled/disabled states.
/// DPI-aware: scales all visuals based on the host window's render scaling.
/// </summary>
public class BreakpointMargin : AbstractMargin
{
    private const double BaseWidth = 16;

    private readonly TextEditor _editor;
    private Dictionary<int, BreakpointVisualInfo> _breakpoints;
    private int? _currentExecutionLine;
    private double _scaleFactor = 1.0;

    private static readonly IBrush BreakpointBrush = new SolidColorBrush(Color.Parse("#E51400"));
    private static readonly IBrush DisabledBreakpointBrush = new SolidColorBrush(Color.Parse("#808080"));
    private static readonly IBrush CurrentLineBrush = new SolidColorBrush(Color.Parse("#FFCC00"));
    private static IBrush MarginBackground => new SolidColorBrush(EditorTheme.MarginBackground);
    private static readonly IPen BreakpointBorderPen = new Pen(new SolidColorBrush(Color.Parse("#8B0000")), 1);
    private static readonly IPen DisabledBorderPen = new Pen(new SolidColorBrush(Color.Parse("#505050")), 1);
    private static readonly IPen UnverifiedBreakpointPen = new Pen(new SolidColorBrush(Color.Parse("#E51400")), 1.5);
    private static readonly IPen WhitePen = new Pen(Brushes.White, 1.5);
    private static readonly IPen WhiteThinPen = new Pen(Brushes.White, 1.0);

    public event EventHandler<int>? BreakpointToggled;

    public BreakpointMargin(TextEditor editor, HashSet<int>? initialBreakpoints = null)
    {
        _editor = editor;
        _breakpoints = new Dictionary<int, BreakpointVisualInfo>();
        if (initialBreakpoints != null)
        {
            foreach (var line in initialBreakpoints)
                _breakpoints[line] = new BreakpointVisualInfo();
        }
        Width = BaseWidth;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// Updates breakpoints with full visual info (kind, enabled state).
    /// </summary>
    public void UpdateBreakpoints(Dictionary<int, BreakpointVisualInfo> breakpoints)
    {
        _breakpoints = breakpoints;
        InvalidateVisual();
    }

    /// <summary>
    /// Backward-compatible overload: treats all lines as normal enabled breakpoints.
    /// </summary>
    public void UpdateBreakpoints(HashSet<int> breakpointLines)
    {
        _breakpoints = new Dictionary<int, BreakpointVisualInfo>();
        foreach (var line in breakpointLines)
            _breakpoints[line] = new BreakpointVisualInfo();
        InvalidateVisual();
    }

    public void SetCurrentLine(int? line)
    {
        _currentExecutionLine = line;
        InvalidateVisual();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateScaleFactor();
    }

    private void UpdateScaleFactor()
    {
        if (VisualRoot is TopLevel topLevel)
        {
            _scaleFactor = topLevel.RenderScaling;
            Width = BaseWidth * _scaleFactor;
            InvalidateVisual();
        }
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

            // Draw breakpoint indicator
            if (_breakpoints.TryGetValue(lineNumber, out var info))
            {
                if (info.IsEnabled && !info.IsVerified)
                {
                    // Unverified breakpoint: hollow circle/diamond
                    switch (info.Kind)
                    {
                        case BreakpointKind.Conditional:
                            DrawUnverifiedConditionalBreakpoint(context, y, visualLine.Height);
                            break;
                        default:
                            DrawUnverifiedBreakpoint(context, y, visualLine.Height);
                            break;
                    }
                }
                else
                {
                    switch (info.Kind)
                    {
                        case BreakpointKind.Conditional:
                            DrawConditionalBreakpoint(context, y, visualLine.Height, info.IsEnabled);
                            break;
                        case BreakpointKind.HitCount:
                            DrawHitCountBreakpoint(context, y, visualLine.Height, info.IsEnabled);
                            break;
                        case BreakpointKind.Logpoint:
                            DrawLogpointBreakpoint(context, y, visualLine.Height, info.IsEnabled);
                            break;
                        default:
                            DrawBreakpoint(context, y, visualLine.Height, info.IsEnabled);
                            break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Draws a normal breakpoint: filled red circle.
    /// </summary>
    private void DrawBreakpoint(DrawingContext context, double y, double lineHeight, bool isEnabled = true)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var x = (Bounds.Width - size) / 2;
        var yPos = y + (lineHeight - size) / 2;

        var fill = isEnabled ? BreakpointBrush : DisabledBreakpointBrush;
        var border = isEnabled ? BreakpointBorderPen : DisabledBorderPen;

        // Draw filled circle
        context.DrawEllipse(fill, null, new Rect(x, yPos, size, size));

        // Draw subtle border
        context.DrawEllipse(null, border, new Rect(x, yPos, size, size));
    }

    /// <summary>
    /// Draws a conditional breakpoint: filled red diamond (rotated square).
    /// </summary>
    private void DrawConditionalBreakpoint(DrawingContext context, double y, double lineHeight, bool isEnabled)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var cx = Bounds.Width / 2;
        var cy = y + lineHeight / 2;
        var half = size / 2;

        var fill = isEnabled ? BreakpointBrush : DisabledBreakpointBrush;
        var border = isEnabled ? BreakpointBorderPen : DisabledBorderPen;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - half), true);   // top
            ctx.LineTo(new Point(cx + half, cy));               // right
            ctx.LineTo(new Point(cx, cy + half));               // bottom
            ctx.LineTo(new Point(cx - half, cy));               // left
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, border, geometry);
    }

    /// <summary>
    /// Draws a hit-count breakpoint: red circle with a small white "+" inside.
    /// </summary>
    private void DrawHitCountBreakpoint(DrawingContext context, double y, double lineHeight, bool isEnabled)
    {
        // Draw the base circle first
        DrawBreakpoint(context, y, lineHeight, isEnabled);

        // Draw "+" in the center
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var cx = Bounds.Width / 2;
        var cy = y + lineHeight / 2;
        var arm = size * 0.25; // length of each arm of the plus

        // Horizontal line of "+"
        context.DrawLine(WhitePen,
            new Point(cx - arm, cy),
            new Point(cx + arm, cy));

        // Vertical line of "+"
        context.DrawLine(WhitePen,
            new Point(cx, cy - arm),
            new Point(cx, cy + arm));
    }

    /// <summary>
    /// Draws a logpoint (tracepoint): red diamond with a horizontal white line through the middle.
    /// </summary>
    private void DrawLogpointBreakpoint(DrawingContext context, double y, double lineHeight, bool isEnabled)
    {
        // Draw the base diamond
        DrawConditionalBreakpoint(context, y, lineHeight, isEnabled);

        // Draw horizontal line through the center
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var cx = Bounds.Width / 2;
        var cy = y + lineHeight / 2;
        var halfLine = size * 0.3;

        context.DrawLine(WhiteThinPen,
            new Point(cx - halfLine, cy),
            new Point(cx + halfLine, cy));
    }

    /// <summary>
    /// Draws an unverified breakpoint: hollow red circle (not yet bound by debugger).
    /// </summary>
    private void DrawUnverifiedBreakpoint(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var x = (Bounds.Width - size) / 2;
        var yPos = y + (lineHeight - size) / 2;

        // Draw hollow circle (no fill, just a red border)
        context.DrawEllipse(null, UnverifiedBreakpointPen, new Rect(x, yPos, size, size));
    }

    /// <summary>
    /// Draws an unverified conditional breakpoint: hollow red diamond.
    /// </summary>
    private void DrawUnverifiedConditionalBreakpoint(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var cx = Bounds.Width / 2;
        var cy = y + lineHeight / 2;
        var half = size / 2;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(cx, cy - half), false);   // top, not filled
            ctx.LineTo(new Point(cx + half, cy));
            ctx.LineTo(new Point(cx, cy + half));
            ctx.LineTo(new Point(cx - half, cy));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(null, UnverifiedBreakpointPen, geometry);
    }

    private void DrawExecutionArrow(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(10 * _scaleFactor, lineHeight - 4 * _scaleFactor);
        var x = 2 * _scaleFactor;
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
        return new Size(BaseWidth * _scaleFactor, 0);
    }
}
