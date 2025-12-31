using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Editor.Margins;

/// <summary>
/// A margin that displays bookmark indicators in the editor
/// </summary>
public class BookmarkMargin : AbstractMargin
{
    private readonly TextEditor _editor;
    private readonly IBookmarkService _bookmarkService;
    private string? _filePath;

    private static readonly IBrush BookmarkBrush = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush BookmarkHoverBrush = new SolidColorBrush(Color.Parse("#6BB5FF"));
    private const double MarginWidth = 16;

    private int _hoveredLine = -1;

    public BookmarkMargin(TextEditor editor, IBookmarkService bookmarkService)
    {
        _editor = editor;
        _bookmarkService = bookmarkService;
        _bookmarkService.BookmarkChanged += OnBookmarkChanged;

        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public void SetFilePath(string? filePath)
    {
        _filePath = filePath;
        InvalidateVisual();
    }

    private void OnBookmarkChanged(object? sender, BookmarkChangedEventArgs e)
    {
        if (_filePath == null) return;

        // Normalize paths for comparison
        var normalizedFilePath = Path.GetFullPath(_filePath).ToLowerInvariant();
        var normalizedEventPath = Path.GetFullPath(e.FilePath).ToLowerInvariant();

        if (normalizedFilePath == normalizedEventPath || e.ChangeType == BookmarkChangeType.Cleared)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(InvalidateVisual);
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(MarginWidth, 0);
    }

    public override void Render(DrawingContext context)
    {
        if (_filePath == null || TextView == null) return;

        var textView = TextView;
        var renderSize = Bounds.Size;

        // Draw background
        context.FillRectangle(new SolidColorBrush(Color.Parse("#1E1E1E")), new Rect(renderSize));

        // Get bookmarks for current file
        var bookmarks = _bookmarkService.GetBookmarks(_filePath);
        if (bookmarks.Count == 0) return;

        var bookmarkedLines = bookmarks.Select(b => b.Line).ToHashSet();

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;

            if (bookmarkedLines.Contains(lineNumber))
            {
                var y = visualLine.VisualTop - textView.ScrollOffset.Y;
                var lineHeight = visualLine.Height;

                // Draw bookmark indicator (filled rectangle with rounded corners)
                var rect = new Rect(3, y + 2, MarginWidth - 6, lineHeight - 4);
                var brush = lineNumber == _hoveredLine ? BookmarkHoverBrush : BookmarkBrush;

                // Draw rounded rectangle
                context.DrawRectangle(
                    brush,
                    null,
                    new RoundedRect(rect, 2));
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var line = GetLineFromPoint(e.GetPosition(this));
        if (line != _hoveredLine)
        {
            _hoveredLine = line;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoveredLine = -1;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_filePath == null) return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            var line = GetLineFromPoint(e.GetPosition(this));
            if (line > 0)
            {
                _bookmarkService.ToggleBookmark(_filePath, line);
                e.Handled = true;
            }
        }
    }

    private int GetLineFromPoint(Point point)
    {
        if (TextView == null) return -1;

        var visualLine = TextView.GetVisualLineFromVisualTop(point.Y + TextView.ScrollOffset.Y);
        return visualLine?.FirstDocumentLine.LineNumber ?? -1;
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
}
