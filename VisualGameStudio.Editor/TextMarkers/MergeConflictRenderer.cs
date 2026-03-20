using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.TextMarkers;

/// <summary>
/// Background renderer that detects and highlights merge conflict markers in the editor.
/// Draws colored backgrounds for current/incoming change sections and renders
/// clickable action buttons (Accept Current, Accept Incoming, Accept Both, Compare)
/// above each conflict block, similar to VS Code's merge conflict UI.
/// </summary>
public class MergeConflictRenderer : IBackgroundRenderer
{
    private readonly TextEditor _editor;
    private List<MergeConflictRegion> _conflicts = new();
    private List<ActionButtonHitRegion> _hitRegions = new();
    private bool _isEnabled = true;

    // VS Code-style merge conflict colors
    private static readonly Color CurrentChangeBackground = Color.Parse("#2ea04320");
    private static readonly Color CurrentChangeHeaderBackground = Color.Parse("#2ea04340");
    private static readonly Color IncomingChangeBackground = Color.Parse("#1d76db20");
    private static readonly Color IncomingChangeHeaderBackground = Color.Parse("#1d76db40");
    private static readonly Color SeparatorBackground = Color.Parse("#80808030");

    // Action button styling
    private static readonly Color ButtonTextColor = Color.Parse("#569CD6");
    private static readonly Color ButtonHoverBackground = Color.Parse("#569CD620");
    private static readonly Color ActionBarBackground = Color.Parse("#1E1E1E");

    // Conflict marker patterns
    private static readonly Regex CurrentMarkerPattern = new(@"^<<<<<<<\s*(.*?)$", RegexOptions.Compiled);
    private static readonly Regex SeparatorMarkerPattern = new(@"^=======$", RegexOptions.Compiled);
    private static readonly Regex IncomingMarkerPattern = new(@"^>>>>>>>\s*(.*?)$", RegexOptions.Compiled);

    /// <summary>
    /// Raised when a conflict action button is clicked.
    /// </summary>
    public event EventHandler<MergeConflictActionEventArgs>? ActionClicked;

    /// <summary>
    /// Raised when the set of detected conflicts changes (e.g., after a resolution).
    /// The int value is the number of remaining conflicts.
    /// </summary>
    public event EventHandler<int>? ConflictsChanged;

    public KnownLayer Layer => KnownLayer.Background;

    public MergeConflictRenderer(TextEditor editor)
    {
        _editor = editor;
        if (_editor?.TextArea?.TextView != null)
        {
            _editor.TextArea.TextView.PointerPressed += OnTextViewPointerPressed;
        }
    }

    /// <summary>
    /// Gets or sets whether merge conflict highlighting is enabled.
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            _editor?.TextArea?.TextView?.InvalidateLayer(Layer);
        }
    }

    /// <summary>
    /// Gets the number of detected merge conflicts.
    /// </summary>
    public int ConflictCount => _conflicts.Count;

    /// <summary>
    /// Gets the list of detected merge conflicts.
    /// </summary>
    public IReadOnlyList<MergeConflictRegion> Conflicts => _conflicts.AsReadOnly();

    /// <summary>
    /// Re-scans the document for conflict markers and updates the internal conflict list.
    /// Call this when the document text changes.
    /// </summary>
    public void ScanForConflicts()
    {
        var oldCount = _conflicts.Count;
        _conflicts.Clear();

        var document = _editor?.Document;
        if (document == null || document.TextLength == 0)
        {
            if (oldCount != 0)
                ConflictsChanged?.Invoke(this, 0);
            return;
        }

        var text = document.Text;
        MergeConflictRegion? currentConflict = null;

        for (int lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            var docLine = document.GetLineByNumber(lineNum);
            var lineText = document.GetText(docLine.Offset, docLine.Length);

            var currentMatch = CurrentMarkerPattern.Match(lineText);
            if (currentMatch.Success)
            {
                currentConflict = new MergeConflictRegion
                {
                    CurrentStartLine = lineNum,
                    CurrentLabel = currentMatch.Groups[1].Value.Trim(),
                    StartOffset = docLine.Offset
                };
                continue;
            }

            if (currentConflict != null)
            {
                var sepMatch = SeparatorMarkerPattern.Match(lineText);
                if (sepMatch.Success)
                {
                    currentConflict.SeparatorLine = lineNum;
                    continue;
                }

                var incomingMatch = IncomingMarkerPattern.Match(lineText);
                if (incomingMatch.Success && currentConflict.SeparatorLine > 0)
                {
                    currentConflict.IncomingEndLine = lineNum;
                    currentConflict.IncomingLabel = incomingMatch.Groups[1].Value.Trim();
                    currentConflict.EndOffset = docLine.EndOffset;
                    _conflicts.Add(currentConflict);
                    currentConflict = null;
                }
            }
        }

        if (oldCount != _conflicts.Count)
            ConflictsChanged?.Invoke(this, _conflicts.Count);
    }

    /// <summary>
    /// Returns true if the document text contains any conflict markers.
    /// A lightweight check without full scanning.
    /// </summary>
    public static bool HasConflictMarkers(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return text.Contains("<<<<<<<") && text.Contains("=======") && text.Contains(">>>>>>>");
    }

    /// <summary>
    /// Gets the line number of the next conflict marker relative to the current caret.
    /// Returns -1 if no conflicts exist.
    /// </summary>
    public int GetNextConflictLine(int currentLine)
    {
        if (_conflicts.Count == 0) return -1;

        foreach (var conflict in _conflicts)
        {
            if (conflict.CurrentStartLine > currentLine)
                return conflict.CurrentStartLine;
        }

        // Wrap around to the first conflict
        return _conflicts[0].CurrentStartLine;
    }

    /// <summary>
    /// Gets the line number of the previous conflict marker relative to the current caret.
    /// Returns -1 if no conflicts exist.
    /// </summary>
    public int GetPreviousConflictLine(int currentLine)
    {
        if (_conflicts.Count == 0) return -1;

        for (int i = _conflicts.Count - 1; i >= 0; i--)
        {
            if (_conflicts[i].CurrentStartLine < currentLine)
                return _conflicts[i].CurrentStartLine;
        }

        // Wrap around to the last conflict
        return _conflicts[^1].CurrentStartLine;
    }

    /// <summary>
    /// Resolves a conflict by applying the chosen action and modifying the document text.
    /// </summary>
    public void ResolveConflict(MergeConflictRegion conflict, MergeConflictAction action)
    {
        var document = _editor?.Document;
        if (document == null) return;

        try
        {
            var currentStartLine = document.GetLineByNumber(conflict.CurrentStartLine);
            var separatorLine = document.GetLineByNumber(conflict.SeparatorLine);
            var incomingEndLine = document.GetLineByNumber(conflict.IncomingEndLine);

            // Extract the content sections (excluding markers)
            string currentContent = "";
            if (conflict.SeparatorLine > conflict.CurrentStartLine + 1)
            {
                var contentStart = document.GetLineByNumber(conflict.CurrentStartLine + 1);
                var contentEnd = document.GetLineByNumber(conflict.SeparatorLine - 1);
                currentContent = document.GetText(contentStart.Offset,
                    contentEnd.EndOffset - contentStart.Offset);
            }

            string incomingContent = "";
            if (conflict.IncomingEndLine > conflict.SeparatorLine + 1)
            {
                var contentStart = document.GetLineByNumber(conflict.SeparatorLine + 1);
                var contentEnd = document.GetLineByNumber(conflict.IncomingEndLine - 1);
                incomingContent = document.GetText(contentStart.Offset,
                    contentEnd.EndOffset - contentStart.Offset);
            }

            string replacement;
            switch (action)
            {
                case MergeConflictAction.AcceptCurrent:
                    replacement = currentContent;
                    break;

                case MergeConflictAction.AcceptIncoming:
                    replacement = incomingContent;
                    break;

                case MergeConflictAction.AcceptBoth:
                    if (!string.IsNullOrEmpty(currentContent) && !string.IsNullOrEmpty(incomingContent))
                        replacement = currentContent + document.GetLineDelimiter() + incomingContent;
                    else
                        replacement = currentContent + incomingContent;
                    break;

                default:
                    return;
            }

            // Replace the entire conflict block (including trailing newline of the end marker if possible)
            var replaceStart = currentStartLine.Offset;
            var replaceEnd = incomingEndLine.EndOffset;

            // Include the newline after the end marker if it exists
            if (incomingEndLine.NextLine != null)
            {
                replaceEnd = incomingEndLine.NextLine.Offset;
            }

            // Include trailing newline in replacement if we took a full line ending
            if (replaceEnd > incomingEndLine.EndOffset && !string.IsNullOrEmpty(replacement))
            {
                replacement += document.GetLineDelimiter();
            }

            document.Replace(replaceStart, replaceEnd - replaceStart, replacement);

            // Re-scan after modification
            ScanForConflicts();
            _editor.TextArea.TextView.InvalidateLayer(Layer);
        }
        catch
        {
            // Ignore errors during conflict resolution
        }
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || !_isEnabled || _conflicts.Count == 0) return;

        _hitRegions.Clear();

        var currentBgBrush = new SolidColorBrush(CurrentChangeBackground);
        var currentHeaderBrush = new SolidColorBrush(CurrentChangeHeaderBackground);
        var incomingBgBrush = new SolidColorBrush(IncomingChangeBackground);
        var incomingHeaderBrush = new SolidColorBrush(IncomingChangeHeaderBackground);
        var separatorBrush = new SolidColorBrush(SeparatorBackground);

        foreach (var conflict in _conflicts)
        {
            DrawConflictRegion(textView, drawingContext, conflict,
                currentBgBrush, currentHeaderBrush, incomingBgBrush, incomingHeaderBrush, separatorBrush);
        }
    }

    private void DrawConflictRegion(TextView textView, DrawingContext drawingContext,
        MergeConflictRegion conflict,
        IBrush currentBgBrush, IBrush currentHeaderBrush,
        IBrush incomingBgBrush, IBrush incomingHeaderBrush,
        IBrush separatorBrush)
    {
        var viewWidth = textView.Bounds.Width;

        foreach (var visualLine in textView.VisualLines)
        {
            var lineNum = visualLine.FirstDocumentLine.LineNumber;
            var y = visualLine.VisualTop - textView.ScrollOffset.Y;
            var lineRect = new Rect(0, y, viewWidth, visualLine.Height);

            if (lineNum == conflict.CurrentStartLine)
            {
                // Draw the <<<<<<< header line
                drawingContext.FillRectangle(currentHeaderBrush, lineRect);

                // Draw action buttons above the conflict marker
                DrawActionButtons(textView, drawingContext, visualLine, conflict);
            }
            else if (lineNum > conflict.CurrentStartLine && lineNum < conflict.SeparatorLine)
            {
                // Current change content
                drawingContext.FillRectangle(currentBgBrush, lineRect);
            }
            else if (lineNum == conflict.SeparatorLine)
            {
                // Separator line (=======)
                drawingContext.FillRectangle(separatorBrush, lineRect);
            }
            else if (lineNum > conflict.SeparatorLine && lineNum < conflict.IncomingEndLine)
            {
                // Incoming change content
                drawingContext.FillRectangle(incomingBgBrush, lineRect);
            }
            else if (lineNum == conflict.IncomingEndLine)
            {
                // Draw the >>>>>>> footer line
                drawingContext.FillRectangle(incomingHeaderBrush, lineRect);
            }
        }
    }

    private void DrawActionButtons(TextView textView, DrawingContext drawingContext,
        VisualLine visualLine, MergeConflictRegion conflict)
    {
        var y = visualLine.VisualTop - textView.ScrollOffset.Y;

        // Draw action buttons in the line above the <<<<<<< marker
        var buttonY = y - 2; // Slightly above the marker line
        var buttonHeight = 16.0;

        // Only draw if the area is visible (above the marker, within view bounds)
        if (buttonY < -buttonHeight) return;

        var typeface = new Typeface("Segoe UI, Cascadia Code, Consolas", FontStyle.Normal, FontWeight.Normal);
        var fontSize = 11.5;
        var buttonTextBrush = new SolidColorBrush(ButtonTextColor);
        var separatorBrush = new SolidColorBrush(Color.Parse("#808080"));

        var buttons = new[]
        {
            ("Accept Current Change", MergeConflictAction.AcceptCurrent),
            ("Accept Incoming Change", MergeConflictAction.AcceptIncoming),
            ("Accept Both Changes", MergeConflictAction.AcceptBoth),
            ("Compare Changes", MergeConflictAction.Compare)
        };

        double x = 8.0; // Left margin

        for (int i = 0; i < buttons.Length; i++)
        {
            var (label, action) = buttons[i];

            var formattedText = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                buttonTextBrush);

            var textWidth = formattedText.Width;
            var buttonRect = new Rect(x - 2, buttonY, textWidth + 4, buttonHeight);

            // Draw the button text
            drawingContext.DrawText(formattedText, new Point(x, buttonY + 1));

            // Store hit region for click detection
            _hitRegions.Add(new ActionButtonHitRegion
            {
                Bounds = buttonRect,
                Conflict = conflict,
                Action = action
            });

            x += textWidth + 4;

            // Draw separator between buttons (except after the last)
            if (i < buttons.Length - 1)
            {
                var sepText = new FormattedText(
                    " | ",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    separatorBrush);

                drawingContext.DrawText(sepText, new Point(x, buttonY + 1));
                x += sepText.Width;
            }
        }
    }

    private void OnTextViewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_hitRegions.Count == 0 || !_isEnabled) return;

        var point = e.GetCurrentPoint(_editor.TextArea.TextView);
        var pos = point.Position;

        foreach (var region in _hitRegions)
        {
            if (region.Bounds.Contains(pos))
            {
                if (region.Action == MergeConflictAction.Compare)
                {
                    // Raise event for compare action (requires external diff view)
                    ActionClicked?.Invoke(this, new MergeConflictActionEventArgs
                    {
                        Conflict = region.Conflict,
                        Action = region.Action
                    });
                }
                else
                {
                    // Resolve the conflict directly
                    ResolveConflict(region.Conflict, region.Action);
                }

                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Detaches event handlers. Call this when removing the renderer from the text view.
    /// </summary>
    public void Detach()
    {
        if (_editor?.TextArea?.TextView != null)
        {
            _editor.TextArea.TextView.PointerPressed -= OnTextViewPointerPressed;
        }
    }

    private class ActionButtonHitRegion
    {
        public Rect Bounds { get; set; }
        public MergeConflictRegion Conflict { get; set; } = null!;
        public MergeConflictAction Action { get; set; }
    }
}

/// <summary>
/// Represents a single merge conflict region detected in the document.
/// </summary>
public class MergeConflictRegion
{
    /// <summary>Line number of the <<<<<<< marker.</summary>
    public int CurrentStartLine { get; set; }

    /// <summary>Line number of the ======= separator.</summary>
    public int SeparatorLine { get; set; }

    /// <summary>Line number of the >>>>>>> marker.</summary>
    public int IncomingEndLine { get; set; }

    /// <summary>Branch/ref label from the <<<<<<< marker.</summary>
    public string CurrentLabel { get; set; } = "";

    /// <summary>Branch/ref label from the >>>>>>> marker.</summary>
    public string IncomingLabel { get; set; } = "";

    /// <summary>Document offset of the start of the conflict block.</summary>
    public int StartOffset { get; set; }

    /// <summary>Document offset of the end of the conflict block.</summary>
    public int EndOffset { get; set; }
}

/// <summary>
/// Merge conflict resolution actions.
/// </summary>
public enum MergeConflictAction
{
    /// <summary>Keep only the current (HEAD) changes, remove incoming.</summary>
    AcceptCurrent,

    /// <summary>Keep only the incoming changes, remove current.</summary>
    AcceptIncoming,

    /// <summary>Keep both changes (current first, then incoming).</summary>
    AcceptBoth,

    /// <summary>Open a side-by-side diff comparing current vs incoming.</summary>
    Compare
}

/// <summary>
/// Event args for merge conflict action button clicks.
/// </summary>
public class MergeConflictActionEventArgs : EventArgs
{
    public MergeConflictRegion Conflict { get; set; } = null!;
    public MergeConflictAction Action { get; set; }
}

/// <summary>
/// Extension method to get line delimiter from a TextDocument.
/// </summary>
internal static class TextDocumentExtensions
{
    public static string GetLineDelimiter(this TextDocument document)
    {
        if (document.LineCount >= 2)
        {
            var firstLine = document.GetLineByNumber(1);
            if (firstLine.DelimiterLength == 2)
                return "\r\n";
        }
        return "\n";
    }
}
