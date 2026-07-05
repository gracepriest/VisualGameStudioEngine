using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VisualGameStudio.Editor.Services;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Editor.Rendering;

/// <summary>
/// A VisualLineElementGenerator that renders CodeLens items as an end-of-line inlay
/// (JetBrains-style): the lens panel appears AFTER the last character of the declaration
/// line, separated by a small margin. Each panel displays clickable text items
/// (e.g., "2 references | Run | Debug") styled with a smaller italic gray/blue font.
///
/// The lens is deliberately never inserted before the first character of the line:
/// a start-of-line inline object displaces the first token, inflates the line height,
/// and intercepts mouse interaction where the first word starts.
///
/// Works with <see cref="CodeLensManager"/> which provides the data, and fires
/// <see cref="CodeLensClicked"/> when a user clicks on a CodeLens item.
/// </summary>
public class CodeLensElementGenerator : VisualLineElementGenerator
{
    private readonly TextEditor _editor;
    private readonly CodeLensManager _manager;

    /// <summary>
    /// Font size for CodeLens text (px).
    /// </summary>
    private const double LensFontSize = 11;

    /// <summary>
    /// Horizontal gap between the end of the code line and the lens panel (px).
    /// </summary>
    private const double LensLeftMargin = 12;

    /// <summary>
    /// Fired when a CodeLens item is clicked.
    /// </summary>
    public event EventHandler<CodeLensClickedEventArgs>? CodeLensClicked;

    public CodeLensElementGenerator(TextEditor editor, CodeLensManager manager)
    {
        _editor = editor;
        _manager = manager;

        // Redraw the text view when the manager data changes so our generator re-evaluates.
        _manager.Changed += (_, _) =>
        {
            _editor?.TextArea?.TextView?.Redraw();
        };
    }

    /// <summary>
    /// Pure mapping from a generator query to the offset the lens anchors to.
    /// Returns the line's END offset (the position after the last character, before the
    /// line delimiter) when the line should display a lens, otherwise -1.
    ///
    /// Rules:
    /// - lines without lenses are not interesting;
    /// - empty lines are skipped (nothing to annotate; a lens-only visual line would
    ///   degrade caret navigation);
    /// - queries past the end offset (inside the line delimiter — AvaloniaEdit re-asks at
    ///   offset + 1 after constructing a zero-length element) are not interesting.
    /// The result is always &gt;= the line's first character offset, never before it.
    /// </summary>
    public static int ComputeInterestedOffset(int startOffset, int lineOffset, int lineEndOffset, bool lineHasLenses)
    {
        if (!lineHasLenses) return -1;
        if (lineEndOffset <= lineOffset) return -1;  // empty line
        if (startOffset > lineEndOffset) return -1;  // already past the anchor
        return lineEndOffset;
    }

    /// <summary>
    /// Returns the first offset &gt;= <paramref name="startOffset"/> where this generator
    /// wants to insert an element: the END offset of a lens-bearing line.
    /// Returns -1 when there is no interested offset in the queried region.
    /// </summary>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!_manager.HasLenses) return -1;

        var document = CurrentContext.Document;
        if (document == null) return -1;

        var line = document.GetLineByOffset(startOffset);
        if (line == null) return -1;

        return ComputeInterestedOffset(
            startOffset, line.Offset, line.EndOffset,
            _manager.HasLensesForLine(line.LineNumber));
    }

    /// <summary>
    /// Constructs the visual element to insert at the given offset (the line's end offset).
    /// Returns a <see cref="CodeLensInlineElement"/> wrapping a <see cref="CodeLensPanel"/>
    /// that displays the CodeLens items for this line after the line's text.
    /// </summary>
    public override VisualLineElement? ConstructElement(int offset)
    {
        var document = CurrentContext.Document;
        if (document == null) return null;

        var line = document.GetLineByOffset(offset);
        if (line == null || line.EndOffset != offset || line.Length == 0) return null;

        var lenses = _manager.GetLensesForLine(line.LineNumber);
        if (lenses.Count == 0) return null;

        // Build the CodeLens panel control. It sizes to its own text (no forced
        // width/height) so it does not inflate the visual line.
        var panel = new CodeLensPanel(lenses, LensFontSize, LensLeftMargin);
        panel.ItemClicked += (_, args) => CodeLensClicked?.Invoke(this, args);

        // Measure the panel so AvaloniaEdit knows its size.
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        return new CodeLensInlineElement(panel);
    }
}

/// <summary>
/// The inline object element used for CodeLens inlays. It spans 0 document characters
/// (inserted at the line's end offset without consuming text) and is invisible to the
/// caret:
/// - <see cref="GetNextCaretPosition"/> returns -1 so the element itself contributes no
///   caret stops (otherwise Left/Right would need an extra key press at end of line and
///   the caret would render after the lens);
/// - <see cref="HandlesLineBorders"/> is true so the implicit end-of-line caret stop
///   AFTER this element (at VisualLength) is suppressed; the last caret stop on the line
///   remains the boundary after the last text character, i.e. before the lens.
/// Typing at the line end inserts text at the same document offset the lens is anchored
/// to; since the element consumes no characters the edit is unaffected and the lens is
/// simply re-generated after the new text on the next redraw.
/// </summary>
public class CodeLensInlineElement : InlineObjectElement
{
    public CodeLensInlineElement(Control element)
        : base(0, element)
    {
    }

    /// <inheritdoc/>
    public override int GetNextCaretPosition(int visualColumn, LogicalDirection direction, CaretPositioningMode mode)
        => -1;

    /// <inheritdoc/>
    public override bool HandlesLineBorders => true;
}

/// <summary>
/// An Avalonia control that renders a row of CodeLens items separated by " | ".
/// Each item is a clickable TextBlock styled with a smaller italic blue font.
/// The panel sizes naturally to its content and carries a left margin that separates
/// it from the end of the code line.
/// </summary>
internal class CodeLensPanel : Panel
{
    private readonly IReadOnlyList<CodeLensItem> _lenses;
    private readonly double _fontSize;
    private readonly double _leftMargin;

    /// <summary>
    /// Fired when one of the CodeLens items is clicked.
    /// </summary>
    public event EventHandler<CodeLensClickedEventArgs>? ItemClicked;

    public CodeLensPanel(IReadOnlyList<CodeLensItem> lenses, double fontSize, double leftMargin)
    {
        _lenses = lenses;
        _fontSize = fontSize;
        _leftMargin = leftMargin;

        // Transparent background so the panel's own area produces no visuals;
        // clicks that miss the lens text bubble up to the text view as usual.
        Background = Brushes.Transparent;
        IsHitTestVisible = true;
        ClipToBounds = false;

        BuildContent();
    }

    private void BuildContent()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(_leftMargin, 0, 0, 0)
        };

        for (int i = 0; i < _lenses.Count; i++)
        {
            // Add separator before second and subsequent items
            if (i > 0)
            {
                var separator = new TextBlock
                {
                    Text = " | ",
                    FontSize = _fontSize,
                    FontStyle = FontStyle.Normal,
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)),
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };
                stack.Children.Add(separator);
            }

            var lens = _lenses[i];
            var itemText = new TextBlock
            {
                Text = lens.Title,
                FontSize = _fontSize,
                FontStyle = FontStyle.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 100, 149, 237)), // Cornflower blue
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = lens
            };

            // Hover effect: underline on pointer enter
            itemText.PointerEntered += (s, e) =>
            {
                if (s is TextBlock tb)
                {
                    tb.TextDecorations = TextDecorations.Underline;
                }
            };
            itemText.PointerExited += (s, e) =>
            {
                if (s is TextBlock tb)
                {
                    tb.TextDecorations = null;
                }
            };

            // Click handler
            itemText.PointerPressed += OnLensItemPointerPressed;

            // Tooltip
            if (!string.IsNullOrEmpty(lens.CommandName))
            {
                ToolTip.SetTip(itemText, lens.CommandName);
            }

            stack.Children.Add(itemText);
        }

        Children.Add(stack);
    }

    private void OnLensItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock tb && tb.Tag is CodeLensItem lens)
        {
            ItemClicked?.Invoke(this, new CodeLensClickedEventArgs
            {
                Title = lens.Title,
                CommandName = lens.CommandName,
                CommandArguments = lens.CommandArguments,
                Line = lens.Line
            });
            e.Handled = true;
        }
    }
}
