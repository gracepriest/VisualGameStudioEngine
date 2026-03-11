using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders inlay hints (parameter names, type annotations) inline with code.
/// </summary>
public class InlayHintRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private List<InlayHintItem> _hints = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public InlayHintRenderer(AvaloniaEdit.TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>
    /// Updates the inlay hints to display
    /// </summary>
    public void SetHints(IEnumerable<InlayHintItem> hints)
    {
        _hints = hints.ToList();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>
    /// Clears all inlay hints
    /// </summary>
    public void Clear()
    {
        _hints.Clear();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _hints.Count == 0) return;

        var typeface = new Typeface("Cascadia Code, Consolas, monospace");
        var fontSize = _editor.FontSize * 0.85;
        var brush = new SolidColorBrush(Color.FromArgb(180, 128, 128, 128));
        var bgBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128));

        foreach (var hint in _hints)
        {
            try
            {
                var line = hint.Line;
                var column = hint.Column;

                if (line < 1 || line > _editor.Document.LineCount) continue;

                var docLine = _editor.Document.GetLineByNumber(line);
                var offset = docLine.Offset + Math.Min(column - 1, docLine.Length);

                // Find the visual line
                foreach (var visualLine in textView.VisualLines)
                {
                    if (visualLine.FirstDocumentLine.LineNumber != line) continue;

                    var charIndex = offset - visualLine.FirstDocumentLine.Offset;
                    if (charIndex < 0) charIndex = 0;

                    var pos = visualLine.GetVisualPosition(charIndex, VisualYPosition.TextTop);

                    // Draw background
                    var formattedText = new FormattedText(
                        hint.Label,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        brush);

                    var padding = hint.Kind == InlayHintKind.Parameter ? 4 : 2;
                    var rect = new Rect(
                        pos.X + (hint.PaddingLeft ? 4 : 0),
                        pos.Y,
                        formattedText.Width + padding * 2,
                        formattedText.Height);

                    drawingContext.FillRectangle(bgBrush, rect, 2);
                    drawingContext.DrawText(formattedText, new Point(rect.X + padding, rect.Y));
                    break;
                }
            }
            catch { }
        }
    }
}

/// <summary>
/// Represents a single inlay hint to display
/// </summary>
public class InlayHintItem
{
    public int Line { get; set; }
    public int Column { get; set; }
    public string Label { get; set; } = "";
    public InlayHintKind Kind { get; set; }
    public bool PaddingLeft { get; set; }
    public bool PaddingRight { get; set; }
}

public enum InlayHintKind
{
    Type = 1,
    Parameter = 2
}
