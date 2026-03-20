using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.Margins;

/// <summary>
/// Custom line number margin that highlights the current line number with a brighter/bolder style.
/// Replaces AvaloniaEdit's built-in line number margin to provide VS Code-like current line emphasis.
/// </summary>
public class CurrentLineNumberMargin : AbstractMargin
{
    private readonly TextEditor _editor;
    private int _maxLineDigits;

    /// <summary>
    /// Normal line number color (dimmer).
    /// </summary>
    public IBrush NormalForeground { get; set; } = new SolidColorBrush(Color.Parse("#858585"));

    /// <summary>
    /// Active (current) line number color (brighter).
    /// </summary>
    public IBrush ActiveForeground { get; set; } = new SolidColorBrush(Color.Parse("#C6C6C6"));

    /// <summary>
    /// Font family matching the editor.
    /// </summary>
    public FontFamily FontFamily { get; set; } = new FontFamily("Cascadia Code, Consolas, Courier New, monospace");

    /// <summary>
    /// Font size, slightly smaller than editor font.
    /// </summary>
    public double FontSize { get; set; } = 13.0;

    public CurrentLineNumberMargin(TextEditor editor)
    {
        _editor = editor;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var document = _editor.Document;
        if (document == null) return new Size(0, 0);

        var lineCount = document.LineCount;
        _maxLineDigits = Math.Max(3, lineCount.ToString().Length);

        // Measure width needed using a sample string
        var sampleText = new string('9', _maxLineDigits);
        var formattedText = new FormattedText(
            sampleText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle.Normal, FontWeight.Bold),
            FontSize,
            NormalForeground);

        // Add padding on both sides
        return new Size(formattedText.Width + 16, 0);
    }

    public override void Render(DrawingContext drawingContext)
    {
        var textView = _editor.TextArea?.TextView;
        if (textView == null || !textView.VisualLinesValid) return;

        var currentLine = _editor.TextArea.Caret.Line;

        // Draw background
        var bgBrush = new SolidColorBrush(EditorTheme.MarginBackground);
        drawingContext.DrawRectangle(bgBrush, null, new Rect(0, 0, Bounds.Width, Bounds.Height));

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var isCurrentLine = lineNumber == currentLine;

            var text = lineNumber.ToString();
            var foreground = isCurrentLine ? ActiveForeground : NormalForeground;
            var weight = isCurrentLine ? FontWeight.Bold : FontWeight.Normal;

            var formattedText = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, FontStyle.Normal, weight),
                FontSize,
                foreground);

            // Right-align the number within the margin, with 8px right padding
            var x = Bounds.Width - formattedText.Width - 8;
            var y = visualLine.VisualTop - textView.ScrollOffset.Y
                    + (visualLine.Height - formattedText.Height) / 2;

            drawingContext.DrawText(formattedText, new Point(x, y));
        }
    }

    /// <summary>
    /// Call when the caret line changes to redraw.
    /// </summary>
    public void InvalidateLineNumbers()
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Updates the colors from the current theme.
    /// </summary>
    public void UpdateThemeColors()
    {
        NormalForeground = new SolidColorBrush(EditorTheme.LineNumbersForeground);
        // Active foreground: brighter version
        ActiveForeground = new SolidColorBrush(
            EditorTheme.IsHighContrast ? Color.Parse("#FFFFFF") :
            EditorTheme.IsDark ? Color.Parse("#C6C6C6") : Color.Parse("#0B216F"));
        InvalidateVisual();
    }
}
