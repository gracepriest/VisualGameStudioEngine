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
/// A VisualLineElementGenerator that inserts CodeLens panels above lines that have CodeLens items.
/// Each CodeLens panel displays clickable text items (e.g., "2 references | Run | Debug")
/// separated by " | " delimiters, styled with a smaller italic font in gray/blue.
///
/// Works with <see cref="CodeLensManager"/> which provides the data, and fires
/// <see cref="CodeLensClicked"/> when a user clicks on a CodeLens item.
/// </summary>
public class CodeLensElementGenerator : VisualLineElementGenerator
{
    private readonly TextEditor _editor;
    private readonly CodeLensManager _manager;

    /// <summary>
    /// Height of the CodeLens row above the target code line (pixels).
    /// </summary>
    private const double LensRowHeight = 20;

    /// <summary>
    /// Font size for CodeLens text (px).
    /// </summary>
    private const double LensFontSize = 11;

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
    /// Scans forward from <paramref name="startOffset"/> looking for the first document offset
    /// that sits at the beginning of a line which has CodeLens items.
    /// Returns -1 when no more interested offsets exist in the current visual line.
    /// </summary>
    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!_manager.HasLenses) return -1;

        var document = CurrentContext.Document;
        if (document == null) return -1;

        // Find the line that contains startOffset
        var line = document.GetLineByOffset(startOffset);
        if (line == null) return -1;

        // We are only interested in the very start of lines that have CodeLens.
        // If startOffset is at the beginning of such a line, return it.
        if (line.Offset == startOffset && _manager.HasLensesForLine(line.LineNumber))
        {
            return startOffset;
        }

        // Otherwise, check the next line (element generators are called per visual line,
        // so we only need to check the current line's start offset).
        return -1;
    }

    /// <summary>
    /// Constructs the visual element to insert at the given offset.
    /// Returns an <see cref="InlineObjectElement"/> wrapping a <see cref="CodeLensPanel"/>
    /// that displays the CodeLens items for this line.
    /// </summary>
    public override VisualLineElement? ConstructElement(int offset)
    {
        var document = CurrentContext.Document;
        if (document == null) return null;

        var line = document.GetLineByOffset(offset);
        if (line == null || line.Offset != offset) return null;

        var lenses = _manager.GetLensesForLine(line.LineNumber);
        if (lenses.Count == 0) return null;

        // Build the CodeLens panel control
        var panel = new CodeLensPanel(lenses, LensFontSize, LensRowHeight);
        panel.ItemClicked += (_, args) => CodeLensClicked?.Invoke(this, args);

        // Measure the panel so AvalonEdit knows its size
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Create an inline object element spanning 0 document characters.
        // documentLength = 0 means the element is inserted without consuming any text.
        var element = new InlineObjectElement(0, panel)
        {
            // This element sits before the first character of the line.
        };

        return element;
    }
}

/// <summary>
/// An Avalonia control that renders a row of CodeLens items separated by " | ".
/// Each item is a clickable TextBlock styled with a smaller italic blue font.
/// The entire panel has a fixed height and sits above the code line.
/// </summary>
internal class CodeLensPanel : Panel
{
    private readonly IReadOnlyList<CodeLensItem> _lenses;
    private readonly double _fontSize;
    private readonly double _rowHeight;

    /// <summary>
    /// Fired when one of the CodeLens items is clicked.
    /// </summary>
    public event EventHandler<CodeLensClickedEventArgs>? ItemClicked;

    public CodeLensPanel(IReadOnlyList<CodeLensItem> lenses, double fontSize, double rowHeight)
    {
        _lenses = lenses;
        _fontSize = fontSize;
        _rowHeight = rowHeight;

        // Transparent background so clicks pass through to the text view where there is no text
        Background = Brushes.Transparent;
        Height = _rowHeight;
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
            Margin = new Thickness(0, 0, 0, 2)
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

    /// <summary>
    /// Override MeasureOverride to ensure the panel reports the correct desired size.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var result = base.MeasureOverride(availableSize);
        // Ensure minimum height for the CodeLens row
        return new Size(Math.Max(result.Width, 50), Math.Max(result.Height, _rowHeight));
    }
}
