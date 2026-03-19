using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders small colored squares inline next to color values in BasicLang code.
/// Detects RGB patterns in function calls like ClearBackground(255, 128, 0) and
/// hex color literals like &amp;HFF8000.
/// Clicking a swatch opens an inline color picker popup.
/// </summary>
public class InlineColorRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private List<ColorSwatchHitRegion> _hitRegions = new();
    private bool _enabled = true;

    /// <summary>
    /// Raised when a color swatch is clicked. The handler should open the color picker popup.
    /// </summary>
    public event EventHandler<ColorSwatchClickedEventArgs>? ColorSwatchClicked;

    public KnownLayer Layer => KnownLayer.Selection;

    // Swatch dimensions
    private const double SwatchSize = 12;
    private const double SwatchMarginLeft = 4;
    private const double SwatchBorderWidth = 1;

    /// <summary>
    /// Matches RGB triplets in function calls: FuncName(R, G, B) or FuncName(..., R, G, B) or
    /// FuncName(..., R, G, B, A). Captures the three or four numeric arguments at the end.
    /// </summary>
    private static readonly Regex RgbCallPattern = new(
        @"(\w+)\s*\(([^)]*?)\b(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(\d{1,3}))?\s*\)",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches VB-style hex color literals: &amp;HRRGGBB or &amp;HAARRGGBB
    /// </summary>
    private static readonly Regex HexColorPattern = new(
        @"&H([0-9A-Fa-f]{6,8})\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Known engine functions that take color parameters (R, G, B or R, G, B, A at the end).
    /// If empty, all matching patterns are treated as potential colors.
    /// </summary>
    private static readonly HashSet<string> ColorFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ClearBackground", "DrawPixel", "DrawLine", "DrawCircle", "DrawCircleLines",
        "DrawRectangle", "DrawRectangleLines", "DrawTriangle", "DrawTriangleLines",
        "DrawText", "DrawTextEx", "DrawPoly", "DrawPolyLines",
        "DrawEllipse", "DrawEllipseLines", "DrawRing", "DrawRingLines",
        "DrawRectangleRounded", "DrawRectangleRoundedLines",
        "DrawRectangleGradientV", "DrawRectangleGradientH",
        "DrawRectangleGradientEx", "DrawLineBezier",
        "SetColor", "SetBackgroundColor", "SetForegroundColor",
        "Color", "NewColor", "MakeColor", "ColorFromRGB", "ColorFromRGBA",
        "DrawSprite", "DrawSpriteEx", "DrawTexture", "DrawTextureEx",
        "FillRectangle", "FillCircle", "FillEllipse", "FillTriangle",
        "SetPixel", "DrawString", "DrawLineEx", "DrawCircleSector",
        "DrawCircleSectorLines", "DrawCircleGradient",
        "DrawArc", "DrawArcLines", "SetTint", "SetTextColor"
    };

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

            // Detect RGB patterns in function calls
            var rgbMatches = RgbCallPattern.Matches(lineText);
            foreach (Match match in rgbMatches)
            {
                var funcName = match.Groups[1].Value;

                // Only render swatches for known color functions (or if list is empty, all matches)
                if (ColorFunctions.Count > 0 && !ColorFunctions.Contains(funcName))
                    continue;

                if (!int.TryParse(match.Groups[3].Value, out int r) || r > 255) continue;
                if (!int.TryParse(match.Groups[4].Value, out int g) || g > 255) continue;
                if (!int.TryParse(match.Groups[5].Value, out int b) || b > 255) continue;

                int a = 255;
                if (match.Groups[6].Success && int.TryParse(match.Groups[6].Value, out int alpha) && alpha <= 255)
                    a = alpha;

                var color = Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b);

                // Position the swatch after the closing paren
                var endColumn = match.Index + match.Length;
                DrawSwatch(textView, drawingContext, visualLine, docLine, endColumn, color,
                    lineNumber, match.Groups[3].Index + docLine.Offset,
                    match.Index + match.Length, r, g, b, a);
            }

            // Detect hex color patterns
            var hexMatches = HexColorPattern.Matches(lineText);
            foreach (Match match in hexMatches)
            {
                var hexStr = match.Groups[1].Value;
                byte r, g, b, a = 255;

                if (hexStr.Length == 8)
                {
                    a = Convert.ToByte(hexStr.Substring(0, 2), 16);
                    r = Convert.ToByte(hexStr.Substring(2, 2), 16);
                    g = Convert.ToByte(hexStr.Substring(4, 2), 16);
                    b = Convert.ToByte(hexStr.Substring(6, 2), 16);
                }
                else // 6 chars
                {
                    r = Convert.ToByte(hexStr.Substring(0, 2), 16);
                    g = Convert.ToByte(hexStr.Substring(2, 2), 16);
                    b = Convert.ToByte(hexStr.Substring(4, 2), 16);
                }

                var color = Color.FromArgb(a, r, g, b);
                var endColumn = match.Index + match.Length;
                DrawSwatch(textView, drawingContext, visualLine, docLine, endColumn, color,
                    lineNumber, match.Index + docLine.Offset,
                    match.Index + match.Length, r, g, b, a);
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

            // Center the swatch vertically within the line
            double swatchX = pos.X + SwatchMarginLeft;
            double swatchY = pos.Y + (lineHeight - SwatchSize) / 2;

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
