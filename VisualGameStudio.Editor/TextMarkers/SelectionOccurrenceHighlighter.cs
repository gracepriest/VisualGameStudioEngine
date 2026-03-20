using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Highlights all occurrences of the currently selected text in the editor.
/// Similar to VS Code's "editor.selectionHighlight" feature.
/// Only activates when a word or short phrase is selected (not entire lines or very long text).
/// </summary>
public class SelectionOccurrenceHighlighter : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private string? _selectedWord;
    private readonly List<(int offset, int length)> _occurrences = new();
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(50, 100, 160, 255));
    private static readonly IPen HighlightBorder = new Pen(new SolidColorBrush(Color.FromArgb(80, 100, 160, 255)), 1);

    /// <summary>
    /// Maximum length of selected text to trigger occurrence highlighting.
    /// </summary>
    private const int MaxSelectionLength = 200;

    /// <summary>
    /// Minimum length of selected text to trigger occurrence highlighting.
    /// </summary>
    private const int MinSelectionLength = 2;

    public SelectionOccurrenceHighlighter(TextEditor editor)
    {
        _editor = editor;
    }

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>
    /// Updates the occurrences based on the current selection.
    /// Call this from caret position changed or selection changed events.
    /// </summary>
    public void UpdateSelection()
    {
        _occurrences.Clear();
        _selectedWord = null;

        var textArea = _editor.TextArea;
        if (textArea?.Selection == null || textArea.Selection.IsEmpty)
            return;

        var selectedText = _editor.SelectedText;
        if (string.IsNullOrWhiteSpace(selectedText)
            || selectedText.Length < MinSelectionLength
            || selectedText.Length > MaxSelectionLength
            || selectedText.Contains('\n')
            || selectedText.Contains('\r'))
            return;

        _selectedWord = selectedText;

        // Find all occurrences in the document
        var document = _editor.Document;
        if (document == null) return;

        var text = document.Text;
        int index = 0;
        while (index < text.Length)
        {
            index = text.IndexOf(_selectedWord, index, StringComparison.Ordinal);
            if (index < 0) break;

            // Skip the current selection itself
            var selSegment = textArea.Selection.SurroundingSegment;
            if (selSegment != null && index == selSegment.Offset)
            {
                index += _selectedWord.Length;
                continue;
            }

            _occurrences.Add((index, _selectedWord.Length));
            index += _selectedWord.Length;
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_occurrences.Count == 0 || _selectedWord == null) return;

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0) return;

        foreach (var (offset, length) in _occurrences)
        {
            var segment = new TextSegment { StartOffset = offset, Length = length };

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                drawingContext.DrawRectangle(HighlightBrush, HighlightBorder,
                    new Rect(rect.X, rect.Y, rect.Width, rect.Height), 2, 2);
            }
        }
    }
}
