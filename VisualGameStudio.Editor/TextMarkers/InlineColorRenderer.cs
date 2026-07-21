using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders small colored squares inline next to color values in source code.
/// Detection (which patterns, in which language) lives in the pure
/// <see cref="ColorMatchFinder"/>; this class keeps only geometry — swatch
/// layout, scroll-offset math, and click hit-testing.
/// Clicking a swatch opens an inline color picker popup.
/// </summary>
public class InlineColorRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private List<ColorSwatchHitRegion> _hitRegions = new();
    private bool _enabled = true;
    private ColorLanguage _language = ColorLanguage.None;

    /// <summary>
    /// Raised when a color swatch is clicked. The handler should open the color picker popup.
    /// </summary>
    public event EventHandler<ColorSwatchClickedEventArgs>? ColorSwatchClicked;

    public KnownLayer Layer => KnownLayer.Selection;

    // Swatch dimensions
    private const double SwatchSize = 12;
    private const double SwatchMarginLeft = 4;
    private const double SwatchBorderWidth = 1;

    public InlineColorRenderer(TextEditor editor)
    {
        _editor = editor;
        if (_editor?.TextArea?.TextView != null)
        {
            _editor.TextArea.TextView.PointerPressed += OnTextViewPointerPressed;
        }
    }

    /// <summary>
    /// Gets or sets whether inline color swatches are displayed.
    /// </summary>
    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            _editor?.TextArea?.TextView?.InvalidateLayer(KnownLayer.Selection);
        }
    }

    /// <summary>
    /// Tells the renderer which file it is rendering, so detection can be gated by
    /// language (via <see cref="ColorMatchFinder.ClassifyFile"/>). Null or unknown
    /// extensions classify as <see cref="ColorLanguage.None"/> — no swatches.
    /// </summary>
    public void SetFile(string? filePath)
    {
        _language = ColorMatchFinder.ClassifyFile(filePath);
        _editor?.TextArea?.TextView?.InvalidateLayer(KnownLayer.Selection);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || !_enabled) return;

        _hitRegions.Clear();

        var document = _editor.Document;
        if (document == null) return;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            if (lineNumber < 1 || lineNumber > document.LineCount) continue;

            var docLine = document.GetLineByNumber(lineNumber);
            var lineText = document.GetText(docLine.Offset, docLine.Length);

            // Detection is delegated to the pure finder (language-gated); this loop
            // keeps only geometry. For every match kind the swatch sits just past the
            // end of the replace range (RgbCall: after the closing paren; VbHex:
            // after the literal).
            foreach (var colorMatch in ColorMatchFinder.FindMatches(lineText, _language))
            {
                var color = Color.FromArgb(colorMatch.A, colorMatch.R, colorMatch.G, colorMatch.B);
                var endColumn = colorMatch.ReplaceStart + colorMatch.ReplaceLength;
                DrawSwatch(textView, drawingContext, visualLine, docLine, endColumn, color,
                    lineNumber, colorMatch.ReplaceStart + docLine.Offset,
                    endColumn, colorMatch.R, colorMatch.G, colorMatch.B, colorMatch.A);
            }
        }
    }

    private void DrawSwatch(TextView textView, DrawingContext drawingContext,
        VisualLine visualLine, DocumentLine docLine, int columnAfter,
        Color color, int lineNumber, int colorStartOffset, int colorEndRelative,
        int r, int g, int b, int a)
    {
        try
        {
            var charIndex = Math.Min(columnAfter, docLine.Length);
            var pos = visualLine.GetVisualPosition(charIndex, VisualYPosition.TextTop);
            var lineHeight = visualLine.Height;

            // GetVisualPosition returns document coordinates, but the Selection layer paints in
            // viewport space with no scroll transform — so subtract ScrollOffset (as the sibling
            // ExecutionLineRenderer does). Without this the swatch drifts away from its color as
            // the view scrolls, and click hit-testing (which compares against viewport pointer
            // coordinates) misses the swatch. The stored Bounds are therefore viewport-space too.
            var scrollOffset = textView.ScrollOffset;

            // Center the swatch vertically within the line
            double swatchX = pos.X + SwatchMarginLeft - scrollOffset.X;
            double swatchY = pos.Y + (lineHeight - SwatchSize) / 2 - scrollOffset.Y;

            var swatchRect = new Rect(swatchX, swatchY, SwatchSize, SwatchSize);

            // Draw checkerboard background for transparency indication
            if (a < 255)
            {
                var halfSize = SwatchSize / 2;
                var lightGray = new SolidColorBrush(Color.FromRgb(204, 204, 204));
                var white = new SolidColorBrush(Color.FromRgb(255, 255, 255));

                drawingContext.FillRectangle(white, swatchRect, 2);
                drawingContext.FillRectangle(lightGray, new Rect(swatchX, swatchY, halfSize, halfSize));
                drawingContext.FillRectangle(lightGray, new Rect(swatchX + halfSize, swatchY + halfSize, halfSize, halfSize));
            }

            // Draw the color fill
            var colorBrush = new SolidColorBrush(color);
            drawingContext.FillRectangle(colorBrush, swatchRect, 2);

            // Draw a border so the swatch is visible even for dark/light colors
            var borderBrush = new SolidColorBrush(Color.FromArgb(160, 128, 128, 128));
            var pen = new Pen(borderBrush, SwatchBorderWidth);
            drawingContext.DrawRectangle(null, pen, swatchRect.Inflate(-SwatchBorderWidth / 2), 2, 2);

            // Store hit region for click detection
            _hitRegions.Add(new ColorSwatchHitRegion
            {
                Bounds = swatchRect,
                Color = color,
                Line = lineNumber,
                R = r, G = g, B = b, A = a,
                ColorTextStartOffset = colorStartOffset,
                ColorTextEndOffset = docLine.Offset + colorEndRelative
            });
        }
        catch
        {
            // Ignore rendering errors
        }
    }

    private void OnTextViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_hitRegions.Count == 0 || !_enabled) return;

        var point = e.GetCurrentPoint(_editor.TextArea.TextView);
        var pos = point.Position;

        foreach (var region in _hitRegions)
        {
            if (region.Bounds.Contains(pos))
            {
                // Calculate screen position for the popup
                var textView = _editor.TextArea.TextView;
                var screenPoint = textView.PointToScreen(new Point(region.Bounds.X, region.Bounds.Bottom + 4));

                ColorSwatchClicked?.Invoke(this, new ColorSwatchClickedEventArgs
                {
                    R = region.R,
                    G = region.G,
                    B = region.B,
                    A = region.A,
                    Line = region.Line,
                    ColorTextStartOffset = region.ColorTextStartOffset,
                    ColorTextEndOffset = region.ColorTextEndOffset,
                    ScreenX = screenPoint.X,
                    ScreenY = screenPoint.Y,
                    SwatchBounds = region.Bounds
                });
                e.Handled = true;
                return;
            }
        }
    }

    private class ColorSwatchHitRegion
    {
        public Rect Bounds { get; set; }
        public Color Color { get; set; }
        public int Line { get; set; }
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }
        public int A { get; set; }
        public int ColorTextStartOffset { get; set; }
        public int ColorTextEndOffset { get; set; }
    }
}

/// <summary>
/// Event args when a color swatch is clicked.
/// </summary>
public class ColorSwatchClickedEventArgs : EventArgs
{
    public int R { get; set; }
    public int G { get; set; }
    public int B { get; set; }
    public int A { get; set; }
    public int Line { get; set; }
    public int ColorTextStartOffset { get; set; }
    public int ColorTextEndOffset { get; set; }
    public double ScreenX { get; set; }
    public double ScreenY { get; set; }
    public Rect SwatchBounds { get; set; }
}
