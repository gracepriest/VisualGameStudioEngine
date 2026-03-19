using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Editor.Margins;

/// <summary>
/// Margin that displays diagnostic severity icons (error, warning, info) in the editor gutter.
/// Shows a red circle for errors, yellow triangle for warnings, and blue "i" for info/hints.
/// Hovering over an icon displays the diagnostic message in a tooltip.
/// </summary>
public class DiagnosticMargin : AbstractMargin
{
    private const double BaseWidth = 16;

    private readonly TextEditor _editor;
    private Dictionary<int, DiagnosticInfo> _diagnosticsByLine = new();
    private double _scaleFactor = 1.0;
    private int _hoveredLine = -1;

    // Error: red circle
    private static readonly IBrush ErrorFillBrush = new SolidColorBrush(Color.Parse("#E51400"));
    private static readonly IPen ErrorBorderPen = new Pen(new SolidColorBrush(Color.Parse("#8B0000")), 0.5);

    // Warning: yellow/amber triangle
    private static readonly IBrush WarningFillBrush = new SolidColorBrush(Color.Parse("#FFC107"));
    private static readonly IPen WarningBorderPen = new Pen(new SolidColorBrush(Color.Parse("#B8860B")), 0.5);

    // Info/Hint: blue circle with "i"
    private static readonly IBrush InfoFillBrush = new SolidColorBrush(Color.Parse("#1E90FF"));
    private static readonly IPen InfoBorderPen = new Pen(new SolidColorBrush(Color.Parse("#104E8B")), 0.5);

    // Text for the "i" icon and the "!" icon
    private static readonly IBrush WhiteBrush = Brushes.White;

    private static IBrush MarginBackground => new SolidColorBrush(EditorTheme.MarginBackground);

    public DiagnosticMargin(TextEditor editor)
    {
        _editor = editor;
        Width = BaseWidth;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>
    /// Updates the diagnostics displayed in the margin.
    /// Groups by line and picks the highest severity per line.
    /// </summary>
    public void UpdateDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        _diagnosticsByLine.Clear();

        foreach (var diag in diagnostics)
        {
            if (diag.Line <= 0) continue;

            if (_diagnosticsByLine.TryGetValue(diag.Line, out var existing))
            {
                // Keep the highest severity (Error > Warning > Info > Hidden)
                if (diag.Severity > existing.Severity)
                {
                    existing.Severity = diag.Severity;
                }
                // Append message
                existing.Messages.Add(diag.Message);
            }
            else
            {
                _diagnosticsByLine[diag.Line] = new DiagnosticInfo
                {
                    Severity = diag.Severity,
                    Messages = new List<string> { diag.Message }
                };
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Clears all diagnostic icons from the margin.
    /// </summary>
    public void ClearDiagnostics()
    {
        _diagnosticsByLine.Clear();
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

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var line = GetLineFromPoint(e.GetPosition(this));
        if (line != _hoveredLine)
        {
            _hoveredLine = line;

            if (line > 0 && _diagnosticsByLine.TryGetValue(line, out var info))
            {
                ShowTooltip(info);
            }
            else
            {
                HideTooltip();
            }
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredLine = -1;
        HideTooltip();
    }

    private void ShowTooltip(DiagnosticInfo info)
    {
        var message = string.Join("\n", info.Messages);
        var severityPrefix = info.Severity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            DiagnosticSeverity.Info => "Info",
            _ => "Hint"
        };

        ToolTip.SetTip(this, $"{severityPrefix}: {message}");
        ToolTip.SetShowDelay(this, 0);
        ToolTip.SetIsOpen(this, true);
    }

    private void HideTooltip()
    {
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }

    private int GetLineFromPoint(Point point)
    {
        if (TextView == null) return -1;
        var visualLine = TextView.GetVisualLineFromVisualTop(point.Y + TextView.ScrollOffset.Y);
        return visualLine?.FirstDocumentLine.LineNumber ?? -1;
    }

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid)
            return;

        // Draw background
        context.FillRectangle(MarginBackground, new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (_diagnosticsByLine.Count == 0) return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;

            if (_diagnosticsByLine.TryGetValue(lineNumber, out var info))
            {
                var y = visualLine.VisualTop - textView.ScrollOffset.Y;
                var lineHeight = visualLine.Height;

                switch (info.Severity)
                {
                    case DiagnosticSeverity.Error:
                        DrawErrorIcon(context, y, lineHeight);
                        break;
                    case DiagnosticSeverity.Warning:
                        DrawWarningIcon(context, y, lineHeight);
                        break;
                    case DiagnosticSeverity.Info:
                    case DiagnosticSeverity.Hidden:
                    default:
                        DrawInfoIcon(context, y, lineHeight);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Draws a red filled circle with a white "x" for error diagnostics.
    /// </summary>
    private void DrawErrorIcon(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var x = (Bounds.Width - size) / 2;
        var yPos = y + (lineHeight - size) / 2;

        // Red filled circle
        context.DrawEllipse(ErrorFillBrush, ErrorBorderPen, new Rect(x, yPos, size, size));

        // White "x" inside
        var cx = x + size / 2;
        var cy = yPos + size / 2;
        var arm = size * 0.22;
        var pen = new Pen(WhiteBrush, 1.5 * _scaleFactor);
        context.DrawLine(pen, new Point(cx - arm, cy - arm), new Point(cx + arm, cy + arm));
        context.DrawLine(pen, new Point(cx + arm, cy - arm), new Point(cx - arm, cy + arm));
    }

    /// <summary>
    /// Draws a yellow filled triangle with a dark "!" for warning diagnostics.
    /// </summary>
    private void DrawWarningIcon(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var cx = Bounds.Width / 2;
        var yPos = y + (lineHeight - size) / 2;

        // Triangle pointing up
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(cx, yPos), true);                        // top center
            ctx.LineTo(new Point(cx + size / 2, yPos + size));                  // bottom right
            ctx.LineTo(new Point(cx - size / 2, yPos + size));                  // bottom left
            ctx.EndFigure(true);
        }

        context.DrawGeometry(WarningFillBrush, WarningBorderPen, geometry);

        // Dark "!" inside the triangle
        var exclamationPen = new Pen(new SolidColorBrush(Color.Parse("#333333")), 1.5 * _scaleFactor);
        var ey = yPos + size * 0.35;
        var eHeight = size * 0.32;
        context.DrawLine(exclamationPen, new Point(cx, ey), new Point(cx, ey + eHeight));

        // Dot of "!"
        var dotY = ey + eHeight + size * 0.1;
        var dotSize = 1.5 * _scaleFactor;
        context.DrawEllipse(
            new SolidColorBrush(Color.Parse("#333333")),
            null,
            new Rect(cx - dotSize / 2, dotY, dotSize, dotSize));
    }

    /// <summary>
    /// Draws a blue filled circle with a white "i" for info/hint diagnostics.
    /// </summary>
    private void DrawInfoIcon(DrawingContext context, double y, double lineHeight)
    {
        var size = Math.Min(12 * _scaleFactor, lineHeight - 2 * _scaleFactor);
        var x = (Bounds.Width - size) / 2;
        var yPos = y + (lineHeight - size) / 2;

        // Blue filled circle
        context.DrawEllipse(InfoFillBrush, InfoBorderPen, new Rect(x, yPos, size, size));

        // White "i" inside
        var cx = x + size / 2;
        var cy = yPos + size / 2;
        var pen = new Pen(WhiteBrush, 1.5 * _scaleFactor);

        // Dot of "i"
        var dotY = cy - size * 0.22;
        var dotSize = 1.8 * _scaleFactor;
        context.DrawEllipse(WhiteBrush, null,
            new Rect(cx - dotSize / 2, dotY - dotSize / 2, dotSize, dotSize));

        // Stem of "i"
        var stemTop = cy - size * 0.05;
        var stemBottom = cy + size * 0.25;
        context.DrawLine(pen, new Point(cx, stemTop), new Point(cx, stemBottom));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(BaseWidth * _scaleFactor, 0);
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Holds aggregated diagnostic info for a single line.
    /// </summary>
    private class DiagnosticInfo
    {
        public DiagnosticSeverity Severity { get; set; }
        public List<string> Messages { get; set; } = new();
    }
}
