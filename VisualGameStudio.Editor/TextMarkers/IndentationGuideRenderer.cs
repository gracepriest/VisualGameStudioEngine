using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Renders vertical indentation guide lines at each tab stop, similar to VS Code.
/// </summary>
public class IndentationGuideRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private int _activeIndentLevel = -1;

    private static readonly IPen NormalGuidePen = new Pen(
        new SolidColorBrush(Color.FromArgb(0x20, 0x80, 0x80, 0x80)), 1);

    private static readonly IPen ActiveGuidePen = new Pen(
        new SolidColorBrush(Color.FromArgb(0x60, 0x80, 0x80, 0x80)), 1);

    public KnownLayer Layer => KnownLayer.Background;

    public bool IsEnabled { get; set; } = true;

    public IndentationGuideRenderer(TextEditor editor)
    {
        _editor = editor;
        _editor.TextArea.Caret.PositionChanged += (s, e) => UpdateActiveIndent();
    }

    private void UpdateActiveIndent()
    {
        if (_editor.Document == null) return;

        var line = _editor.TextArea.Caret.Line;
        if (line < 1 || line > _editor.Document.LineCount) return;

        var docLine = _editor.Document.GetLineByNumber(line);
        var text = _editor.Document.GetText(docLine.Offset, docLine.Length);
        var tabSize = _editor.Options.IndentationSize;

        int spaces = 0;
        foreach (var c in text)
        {
            if (c == ' ') spaces++;
            else if (c == '\t') spaces += tabSize - (spaces % tabSize);
            else break;
        }

        var newLevel = spaces / tabSize;
        if (newLevel != _activeIndentLevel)
        {
            _activeIndentLevel = newLevel;
            _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!IsEnabled || _editor.Document == null) return;

        var tabSize = _editor.Options.IndentationSize;
        if (tabSize <= 0) tabSize = 4;

        // Get the width of a single space character
        var charWidth = textView.WideSpaceWidth;
        var tabPixelWidth = charWidth * tabSize;

        foreach (var visualLine in textView.VisualLines)
        {
            var docLine = visualLine.FirstDocumentLine;
            if (docLine == null) continue;

            var text = _editor.Document.GetText(docLine.Offset, docLine.Length);

            // Count leading whitespace in terms of spaces
            int leadingSpaces = 0;
            bool allWhitespace = true;
            foreach (var c in text)
            {
                if (c == ' ') leadingSpaces++;
                else if (c == '\t') leadingSpaces += tabSize - (leadingSpaces % tabSize);
                else { allWhitespace = false; break; }
            }

            // Skip lines that are empty or all whitespace
            if (allWhitespace && text.Length == 0) continue;

            // For all-whitespace lines, use indent level from surrounding lines
            if (allWhitespace)
            {
                leadingSpaces = GetSurroundingIndent(docLine.LineNumber, tabSize);
            }

            var indentLevels = leadingSpaces / tabSize;
            if (indentLevels <= 0) continue;

            var lineTop = visualLine.VisualTop - textView.ScrollOffset.Y;
            var lineBottom = lineTop + visualLine.Height;

            // Don't draw outside visible area
            if (lineBottom < 0 || lineTop > textView.Bounds.Height) continue;

            for (int level = 1; level <= indentLevels; level++)
            {
                var x = tabPixelWidth * level - textView.ScrollOffset.X;

                // Skip if outside visible horizontal area
                if (x < 0 || x > textView.Bounds.Width) continue;

                // Snap to pixel grid for crisp lines
                x = Math.Round(x) + 0.5;

                var pen = (level == _activeIndentLevel) ? ActiveGuidePen : NormalGuidePen;
                drawingContext.DrawLine(pen,
                    new Point(x, lineTop),
                    new Point(x, lineBottom));
            }
        }
    }

    private int GetSurroundingIndent(int lineNumber, int tabSize)
    {
        var doc = _editor.Document;
        int maxIndent = 0;

        // Check up to 5 lines above and below
        for (int delta = -5; delta <= 5; delta++)
        {
            if (delta == 0) continue;
            var checkLine = lineNumber + delta;
            if (checkLine < 1 || checkLine > doc.LineCount) continue;

            var dl = doc.GetLineByNumber(checkLine);
            var text = doc.GetText(dl.Offset, dl.Length);

            int spaces = 0;
            bool hasContent = false;
            foreach (var c in text)
            {
                if (c == ' ') spaces++;
                else if (c == '\t') spaces += tabSize - (spaces % tabSize);
                else { hasContent = true; break; }
            }

            if (hasContent && spaces > maxIndent)
                maxIndent = spaces;
        }

        return maxIndent;
    }
}
