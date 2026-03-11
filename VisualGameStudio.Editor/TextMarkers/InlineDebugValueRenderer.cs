using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders inline debug variable values next to code lines during debugging.
/// Similar to VS Code's "Inline Values" feature.
/// </summary>
public class InlineDebugValueRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private List<InlineDebugValue> _values = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public InlineDebugValueRenderer(TextEditor editor)
    {
        _editor = editor;
    }

    /// <summary>
    /// Updates the inline debug values to display.
    /// </summary>
    public void SetValues(IEnumerable<InlineDebugValue> values)
    {
        _values = values.ToList();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>
    /// Clears all inline debug values.
    /// </summary>
    public void Clear()
    {
        _values.Clear();
        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _values.Count == 0) return;

        var typeface = new Typeface("Cascadia Code, Consolas, monospace");
        var fontSize = _editor.FontSize * 0.85;
        var valueBrush = new SolidColorBrush(Color.FromArgb(200, 86, 156, 214)); // Blue for values
        var bgBrush = new SolidColorBrush(Color.FromArgb(30, 86, 156, 214)); // Light blue background

        // Group values by line so we can display multiple variables on the same line
        var valuesByLine = _values
            .GroupBy(v => v.Line)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (lineNumber, lineValues) in valuesByLine)
        {
            try
            {
                if (lineNumber < 1 || lineNumber > _editor.Document.LineCount) continue;

                var docLine = _editor.Document.GetLineByNumber(lineNumber);

                foreach (var visualLine in textView.VisualLines)
                {
                    if (visualLine.FirstDocumentLine.LineNumber != lineNumber) continue;

                    // Position after the end of the line text
                    var lineEndOffset = docLine.EndOffset - visualLine.FirstDocumentLine.Offset;
                    var endPos = visualLine.GetVisualPosition(lineEndOffset, VisualYPosition.TextTop);

                    // Build the display text: "  x = 5, name = "hello""
                    var displayText = "  " + string.Join(", ",
                        lineValues.Select(v => $"{v.Name} = {v.Value}"));

                    var formattedText = new FormattedText(
                        displayText,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        valueBrush);

                    var rect = new Rect(
                        endPos.X + 20, // 20px gap after line end
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
                // Skip rendering errors for individual lines
            }
        }
    }
}

/// <summary>
/// Represents a single inline debug value to display next to a code line.
/// </summary>
public class InlineDebugValue
{
    /// <summary>
    /// The 1-based line number where the value should be displayed.
    /// </summary>
    public int Line { get; set; }

    /// <summary>
    /// The variable name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The variable's current value as a string.
    /// </summary>
    public string Value { get; set; } = "";
}
