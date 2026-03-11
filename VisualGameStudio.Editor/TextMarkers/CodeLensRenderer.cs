using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders code lens annotations (reference counts, run/debug buttons) above function/class lines.
/// Uses IBackgroundRenderer to draw clickable text above the target lines.
/// Also implements IVisualLineTransformer to add extra top padding on lines with code lenses.
/// </summary>
public class CodeLensRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private List<CodeLensItem> _lenses = new();
    private List<CodeLensHitRegion> _hitRegions = new();

    /// <summary>
    /// Fired when a code lens item is clicked.
    /// </summary>
    public event EventHandler<CodeLensClickedEventArgs>? CodeLensClicked;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>
    /// Height of the code lens line above the target code line.
    /// </summary>
    private const double LensHeight = 18;

    /// <summary>
    /// Vertical offset to position the code lens text above the line.
    /// </summary>
    private const double LensTopOffset = 2;

    public CodeLensRenderer(TextEditor editor)
    {
        _editor = editor;
        _editor.TextArea.TextView.PointerPressed += OnTextViewPointerPressed;
    }

    /// <summary>
    /// Updates the code lenses to display.
    /// Multiple lenses on the same line are grouped and displayed together.
    /// </summary>
    public void SetLenses(IEnumerable<CodeLensItem> lenses)
    {
        _lenses = lenses.ToList();
        _hitRegions.Clear();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    /// <summary>
    /// Clears all code lenses.
    /// </summary>
    public void Clear()
    {
        _lenses.Clear();
        _hitRegions.Clear();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _lenses.Count == 0) return;

        _hitRegions.Clear();

        var typeface = new Typeface("Cascadia Code, Consolas, monospace");
        var fontSize = _editor.FontSize * 0.8;
        var textBrush = new SolidColorBrush(Color.FromArgb(200, 100, 149, 237)); // Cornflower blue
        var separatorBrush = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128));

        // Group lenses by line number
        var lensesByLine = _lenses
            .GroupBy(l => l.Line)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var kvp in lensesByLine)
        {
            var line = kvp.Key;
            var lineLenses = kvp.Value;

            if (line < 1 || line > _editor.Document.LineCount) continue;

            try
            {
                // Find the visual line
                foreach (var visualLine in textView.VisualLines)
                {
                    if (visualLine.FirstDocumentLine.LineNumber != line) continue;

                    // Get the position at the start of the line
                    var pos = visualLine.GetVisualPosition(0, VisualYPosition.TextTop);

                    // Draw code lens text above the line
                    double x = pos.X;
                    double y = pos.Y - LensHeight + LensTopOffset;

                    // Don't draw above the visible area
                    if (y < -LensHeight) break;

                    for (int i = 0; i < lineLenses.Count; i++)
                    {
                        var lens = lineLenses[i];

                        // Draw separator between lenses on same line
                        if (i > 0)
                        {
                            var sepText = new FormattedText(
                                " | ",
                                System.Globalization.CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                typeface,
                                fontSize,
                                separatorBrush);

                            drawingContext.DrawText(sepText, new Point(x, y));
                            x += sepText.Width;
                        }

                        var formattedText = new FormattedText(
                            lens.Title,
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            textBrush);

                        // Store hit region for click detection
                        var hitRect = new Rect(x, y, formattedText.Width, formattedText.Height);
                        _hitRegions.Add(new CodeLensHitRegion
                        {
                            Bounds = hitRect,
                            Lens = lens
                        });

                        drawingContext.DrawText(formattedText, new Point(x, y));
                        x += formattedText.Width;
                    }

                    break;
                }
            }
            catch { }
        }
    }

    private void OnTextViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_hitRegions.Count == 0) return;

        var point = e.GetCurrentPoint(_editor.TextArea.TextView);
        var pos = point.Position;

        foreach (var region in _hitRegions)
        {
            if (region.Bounds.Contains(pos))
            {
                CodeLensClicked?.Invoke(this, new CodeLensClickedEventArgs
                {
                    Title = region.Lens.Title,
                    CommandName = region.Lens.CommandName,
                    CommandArguments = region.Lens.CommandArguments,
                    Line = region.Lens.Line
                });
                e.Handled = true;
                return;
            }
        }
    }

    private class CodeLensHitRegion
    {
        public Rect Bounds { get; set; }
        public CodeLensItem Lens { get; set; } = null!;
    }
}

/// <summary>
/// Represents a single code lens item to display above a line.
/// </summary>
public class CodeLensItem
{
    /// <summary>
    /// The 1-based line number where this code lens appears (above this line).
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// Display text (e.g., "3 references", "Run", "Debug").
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// Command to execute when clicked.
    /// </summary>
    public string CommandName { get; set; } = "";

    /// <summary>
    /// Optional command arguments.
    /// </summary>
    public List<object>? CommandArguments { get; set; }
}

/// <summary>
/// Event args when a code lens is clicked.
/// </summary>
public class CodeLensClickedEventArgs : EventArgs
{
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
    public int Line { get; set; }
}
