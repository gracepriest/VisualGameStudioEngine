using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using VisualGameStudio.Core.Abstractions.Services;

namespace VisualGameStudio.Editor.Margins;

/// <summary>
/// A narrow margin (4px wide) that displays git change indicators:
/// - Green bar: added lines
/// - Blue/cyan bar: modified lines
/// - Red triangle: deleted lines (between existing lines)
/// </summary>
public class GitGutterMargin : AbstractMargin
{
    private const double BaseWidth = 4;

    private readonly TextEditor _editor;
    private IReadOnlyList<GitLineChange> _changes = Array.Empty<GitLineChange>();
    private double _scaleFactor = 1.0;

    // Lookup: line number -> change kind for O(1) rendering
    private Dictionary<int, GitLineChangeKind> _lineChangeMap = new();

    private static readonly IBrush AddedBrush = new SolidColorBrush(Color.Parse("#587C0C"));
    private static readonly IBrush ModifiedBrush = new SolidColorBrush(Color.Parse("#1B81A8"));
    private static readonly IBrush DeletedBrush = new SolidColorBrush(Color.Parse("#CA4B51"));
    private static IBrush MarginBackground => new SolidColorBrush(EditorTheme.MarginBackground);

    public GitGutterMargin(TextEditor editor)
    {
        _editor = editor;
        Width = BaseWidth;
        IsHitTestVisible = false; // Don't intercept mouse events
    }

    /// <summary>
    /// Updates the displayed git changes.
    /// </summary>
    public void SetChanges(IReadOnlyList<GitLineChange> changes)
    {
        _changes = changes;
        RebuildLineMap();
        InvalidateVisual();
    }

    /// <summary>
    /// Clears all displayed changes.
    /// </summary>
    public void ClearChanges()
    {
        _changes = Array.Empty<GitLineChange>();
        _lineChangeMap.Clear();
        InvalidateVisual();
    }

    private void RebuildLineMap()
    {
        _lineChangeMap.Clear();
        foreach (var change in _changes)
        {
            if (change.Kind == GitLineChangeKind.Deleted && change.StartLine == change.EndLine)
            {
                // Deleted lines: mark the line where the deletion indicator should appear
                // Only set if there isn't already a stronger indicator (added/modified)
                if (!_lineChangeMap.ContainsKey(change.StartLine))
                {
                    _lineChangeMap[change.StartLine] = GitLineChangeKind.Deleted;
                }
            }
            else
            {
                for (int line = change.StartLine; line <= change.EndLine; line++)
                {
                    _lineChangeMap[line] = change.Kind;
                }
            }
        }
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

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView == null || !textView.VisualLinesValid)
            return;

        // Draw background
        context.FillRectangle(MarginBackground, new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (_lineChangeMap.Count == 0)
            return;

        var barWidth = Bounds.Width;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.VisualTop - textView.ScrollOffset.Y;

            if (!_lineChangeMap.TryGetValue(lineNumber, out var kind))
                continue;

            switch (kind)
            {
                case GitLineChangeKind.Added:
                    // Green bar spanning the full line height
                    context.FillRectangle(AddedBrush,
                        new Rect(0, y, barWidth, visualLine.Height));
                    break;

                case GitLineChangeKind.Modified:
                    // Blue/cyan bar spanning the full line height
                    context.FillRectangle(ModifiedBrush,
                        new Rect(0, y, barWidth, visualLine.Height));
                    break;

                case GitLineChangeKind.Deleted:
                    // Red triangle/arrow pointing right at the bottom of the line
                    DrawDeletedIndicator(context, y, visualLine.Height, barWidth);
                    break;
            }
        }
    }

    /// <summary>
    /// Draws a small red triangle at the bottom edge of the line to indicate deleted content.
    /// </summary>
    private void DrawDeletedIndicator(DrawingContext context, double y, double lineHeight, double width)
    {
        var triangleHeight = Math.Min(4 * _scaleFactor, lineHeight / 2);
        var triangleWidth = width + 2 * _scaleFactor;
        var baseY = y + lineHeight;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(0, baseY - triangleHeight), true);
            ctx.LineTo(new Point(triangleWidth, baseY));
            ctx.LineTo(new Point(0, baseY + triangleHeight));
            ctx.EndFigure(true);
        }

        context.DrawGeometry(DeletedBrush, null, geometry);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(BaseWidth * _scaleFactor, 0);
    }
}
