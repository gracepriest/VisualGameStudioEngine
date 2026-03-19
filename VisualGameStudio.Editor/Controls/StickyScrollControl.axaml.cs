using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// Sticky scroll control that pins enclosing scope headers at the top of the editor
/// when the user scrolls past them (similar to VS Code's sticky scroll feature).
/// </summary>
public partial class StickyScrollControl : UserControl
{
    private TextEditor? _editor;
    private StackPanel? _stickyPanel;
    private bool _enabled = true;
    private int _maxLines = 5;

    // Cached scope data to avoid re-parsing on every scroll
    private List<ScopeInfo>? _cachedScopes;
    private int _cachedDocumentVersion = -1;

    /// <summary>
    /// Regex matching BasicLang scope-opening keywords.
    /// Captures the keyword and remainder so the header line can be displayed.
    /// </summary>
    private static readonly Regex ScopeStartPattern = new(
        @"^\s*(?:(?:Public|Private|Protected|Friend)\s+)?(?:(?:Shared|Overridable|Overrides|MustOverride|NotOverridable|Async)\s+)?" +
        @"(?:Sub|Function|Class|Module|Namespace|Structure|Interface|Enum|Property|Type|Template|If|For\s+Each|For|While|Do|Select|Try|With)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ScopeEndPattern = new(
        @"^\s*(?:End\s+(?:Sub|Function|Class|Module|Namespace|Structure|Interface|Enum|Property|Type|Template|If|Select|Try|With)|" +
        @"EndSub|EndFunction|EndClass|EndModule|EndNamespace|EndStructure|EndInterface|EndEnum|EndProperty|EndType|EndTemplate|EndIf|EndSelect|EndTry|EndWith|" +
        @"Next|Wend|Loop)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches single-line If statements that should NOT open a scope.
    /// </summary>
    private static readonly Regex SingleLineIfPattern = new(
        @"^\s*If\b.*\bThen\b.*[^\s].*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Raised when the user clicks a sticky line to navigate to it.
    /// The int parameter is the 1-based line number.
    /// </summary>
    public event EventHandler<int>? LineClicked;

    public StickyScrollControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _stickyPanel = this.FindControl<StackPanel>("StickyPanel");
    }

    /// <summary>
    /// Attach to a TextEditor instance. Subscribes to scroll and text change events.
    /// </summary>
    public void AttachEditor(TextEditor editor)
    {
        if (_editor != null)
        {
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
            _editor.TextChanged -= OnTextChanged;
        }

        _editor = editor;

        if (_editor != null)
        {
            _editor.TextArea.TextView.ScrollOffsetChanged += OnScrollChanged;
            _editor.TextChanged += OnTextChanged;
        }
    }

    /// <summary>
    /// Detach from the editor (cleanup).
    /// </summary>
    public void DetachEditor()
    {
        if (_editor != null)
        {
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
            _editor.TextChanged -= OnTextChanged;
            _editor = null;
        }
        _cachedScopes = null;
        _cachedDocumentVersion = -1;
    }

    /// <summary>
    /// Gets or sets whether sticky scroll is enabled.
    /// </summary>
    public bool IsEnabled2
    {
        get => _enabled;
        set
        {
            _enabled = value;
            if (!_enabled)
            {
                IsVisible = false;
                ClearStickyLines();
            }
            else
            {
                UpdateStickyHeaders();
            }
        }
    }

    /// <summary>
    /// Maximum number of sticky lines to show at once.
    /// </summary>
    public int MaxStickyLines
    {
        get => _maxLines;
        set => _maxLines = Math.Max(1, Math.Min(10, value));
    }

    private void OnScrollChanged(object? sender, EventArgs e)
    {
        if (_enabled)
            UpdateStickyHeaders();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Invalidate scope cache when text changes
        _cachedScopes = null;
        if (_enabled)
            UpdateStickyHeaders();
    }

    /// <summary>
    /// Rebuild the sticky header lines based on current scroll position.
    /// </summary>
    private void UpdateStickyHeaders()
    {
        if (_stickyPanel == null || _editor == null || !_enabled) return;

        var textView = _editor.TextArea.TextView;
        if (!textView.VisualLinesValid) return;

        // Determine the first visible line number (1-based)
        var firstVisualLine = textView.GetVisualLineFromVisualTop(textView.ScrollOffset.Y);
        if (firstVisualLine == null) return;

        int firstVisibleLineNumber = firstVisualLine.FirstDocumentLine.LineNumber;

        // If we're at the top, hide sticky scroll
        if (firstVisibleLineNumber <= 1)
        {
            IsVisible = false;
            ClearStickyLines();
            return;
        }

        // Get or rebuild scope info
        var scopes = GetScopes();
        if (scopes == null || scopes.Count == 0)
        {
            IsVisible = false;
            ClearStickyLines();
            return;
        }

        // Find which scopes enclose the first visible line
        var enclosingScopes = FindEnclosingScopes(scopes, firstVisibleLineNumber);

        // Only show scopes whose header line is scrolled above the viewport
        var stickyScopes = new List<ScopeInfo>();
        foreach (var scope in enclosingScopes)
        {
            if (scope.StartLine < firstVisibleLineNumber)
            {
                stickyScopes.Add(scope);
            }
        }

        // Limit to max sticky lines
        if (stickyScopes.Count > _maxLines)
        {
            stickyScopes = stickyScopes.Skip(stickyScopes.Count - _maxLines).ToList();
        }

        if (stickyScopes.Count == 0)
        {
            IsVisible = false;
            ClearStickyLines();
            return;
        }

        // Build the sticky panel content
        RebuildStickyPanel(stickyScopes);
        IsVisible = true;
    }

    /// <summary>
    /// Parse scope boundaries from the document text. Results are cached until text changes.
    /// </summary>
    private List<ScopeInfo>? GetScopes()
    {
        if (_editor?.Document == null) return null;

        var doc = _editor.Document;
        int version = doc.Version?.GetHashCode() ?? doc.Text.GetHashCode();
        if (_cachedScopes != null && _cachedDocumentVersion == version)
            return _cachedScopes;

        _cachedScopes = ParseScopes(doc);
        _cachedDocumentVersion = version;
        return _cachedScopes;
    }

    /// <summary>
    /// Parse the document to find all scope blocks with their start/end lines.
    /// Uses a stack-based approach matching start/end keywords.
    /// </summary>
    private static List<ScopeInfo> ParseScopes(TextDocument document)
    {
        var scopes = new List<ScopeInfo>();
        var stack = new Stack<(int line, string headerText)>();

        for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            var line = document.GetLineByNumber(lineNumber);
            var lineText = document.GetText(line.Offset, line.Length);

            // Skip single-line If statements
            if (SingleLineIfPattern.IsMatch(lineText))
                continue;

            if (ScopeStartPattern.IsMatch(lineText))
            {
                stack.Push((lineNumber, lineText));
            }
            else if (ScopeEndPattern.IsMatch(lineText) && stack.Count > 0)
            {
                var start = stack.Pop();
                scopes.Add(new ScopeInfo
                {
                    StartLine = start.line,
                    EndLine = lineNumber,
                    HeaderText = start.headerText
                });
            }
        }

        // Handle unclosed scopes (extend to end of document)
        while (stack.Count > 0)
        {
            var start = stack.Pop();
            scopes.Add(new ScopeInfo
            {
                StartLine = start.line,
                EndLine = document.LineCount,
                HeaderText = start.headerText
            });
        }

        // Sort by start line for efficient searching
        scopes.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
        return scopes;
    }

    /// <summary>
    /// Find all scopes that enclose the given line, ordered from outermost to innermost.
    /// </summary>
    private static List<ScopeInfo> FindEnclosingScopes(List<ScopeInfo> scopes, int lineNumber)
    {
        var enclosing = new List<ScopeInfo>();
        foreach (var scope in scopes)
        {
            // The scope encloses the line if: startLine < lineNumber <= endLine
            // (we want scopes where the header is above us but we're still inside)
            if (scope.StartLine < lineNumber && scope.EndLine >= lineNumber)
            {
                enclosing.Add(scope);
            }
        }

        // Sort by start line ascending (outermost first) then by scope size descending
        // to get proper nesting order
        enclosing.Sort((a, b) =>
        {
            int cmp = a.StartLine.CompareTo(b.StartLine);
            if (cmp != 0) return cmp;
            // Larger scope (wider range) should come first
            return (b.EndLine - b.StartLine).CompareTo(a.EndLine - a.StartLine);
        });

        return enclosing;
    }

    private void ClearStickyLines()
    {
        _stickyPanel?.Children.Clear();
    }

    /// <summary>
    /// Rebuild the sticky panel with the given scope headers.
    /// </summary>
    private void RebuildStickyPanel(List<ScopeInfo> stickyScopes)
    {
        if (_stickyPanel == null || _editor == null) return;

        _stickyPanel.Children.Clear();

        var fontFamily = _editor.FontFamily;
        var fontSize = _editor.FontSize;

        foreach (var scope in stickyScopes)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#252526")),
                BorderBrush = new SolidColorBrush(Color.Parse("#3C3C3C")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 1, 4, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = scope.StartLine,
            };

            var textBlock = new TextBlock
            {
                Text = FormatHeaderText(scope.HeaderText, scope.StartLine),
                FontFamily = fontFamily,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(Color.Parse("#CCCCCC")),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            border.Child = textBlock;

            // Click to navigate
            border.PointerPressed += OnStickyLineClicked;

            // Hover highlight
            border.PointerEntered += (s, e) =>
            {
                if (s is Border b)
                    b.Background = new SolidColorBrush(Color.Parse("#2A2D2E"));
            };
            border.PointerExited += (s, e) =>
            {
                if (s is Border b)
                    b.Background = new SolidColorBrush(Color.Parse("#252526"));
            };

            _stickyPanel.Children.Add(border);
        }
    }

    private void OnStickyLineClicked(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.Tag is int lineNumber)
        {
            LineClicked?.Invoke(this, lineNumber);
        }
    }

    /// <summary>
    /// Format the header text for display. Preserves indentation but adds line number.
    /// </summary>
    private static string FormatHeaderText(string headerText, int lineNumber)
    {
        // Preserve the indentation from the original line
        return headerText.TrimEnd();
    }
}

/// <summary>
/// Represents a scope block in the document with its start/end line and header text.
/// </summary>
internal class ScopeInfo
{
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string HeaderText { get; set; } = "";
}
