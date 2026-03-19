using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// VS Code-style minimap control that renders a miniature overview of the entire document.
/// Features:
/// - Syntax-colored line rendering at ~1/6 scale
/// - Viewport indicator showing visible portion
/// - Click/drag to scroll
/// - Search result, diagnostic, git change, and breakpoint markers
/// - Current line highlight
/// - Bitmap-cached rendering with debounced updates
/// </summary>
public partial class MinimapControl : UserControl
{
    private TextEditor? _editor;
    private Border? _renderSurface;
    private Border? _viewportIndicator;
    private Border? _hoverHighlight;
    private bool _isDragging;
    private bool _isHovering;

    // Cached bitmap for efficient rendering
    private RenderTargetBitmap? _cachedBitmap;
    private bool _bitmapDirty = true;
    private int _cachedLineCount;
    private int _cachedDocVersion;

    // Debounce timer for text changes
    private DispatcherTimer? _debounceTimer;
    private const double DebounceMs = 100;

    // Rendering constants
    private const double LineHeight = 2.0;
    private const double LineGap = 0.5;
    private const double TotalLineHeight = LineHeight + LineGap;
    private const double LeftPadding = 4.0;
    private const double CharWidth = 0.7;
    private const double MarkerWidth = 3.0;

    // Data from the editor
    private IReadOnlyList<GitLineChange> _gitChanges = Array.Empty<GitLineChange>();
    private Dictionary<int, GitLineChangeKind> _gitLineMap = new();
    private HashSet<int> _breakpointLines = new();
    private Dictionary<int, DiagnosticSeverity> _diagnosticLines = new();
    private List<int> _searchMatchLines = new();
    private int _currentLine = -1;
    private int? _executionLine;

    // Settings
    private double _scale = 1.0;
    private bool _showSlider = true;

    // Syntax highlighting colors (minimap-friendly, slightly muted)
    private static readonly Color KeywordColor = Color.Parse("#569CD6");
    private static readonly Color CommentColor = Color.Parse("#608B4E");
    private static readonly Color StringColor = Color.Parse("#CE9178");
    private static readonly Color TypeColor = Color.Parse("#4EC9B0");
    private static readonly Color NumberColor = Color.Parse("#B5CEA8");
    private static readonly Color DefaultCodeColor = Color.Parse("#808080");
    private static readonly Color PreprocessorColor = Color.Parse("#C586C0");

    // Marker colors
    private static readonly Color SearchHighlightColor = Color.Parse("#E8AB53");
    private static readonly Color ErrorMarkerColor = Color.Parse("#E51400");
    private static readonly Color WarningMarkerColor = Color.Parse("#FFC107");
    private static readonly Color GitAddedColor = Color.Parse("#587C0C");
    private static readonly Color GitModifiedColor = Color.Parse("#1B81A8");
    private static readonly Color GitDeletedColor = Color.Parse("#CA4B51");
    private static readonly Color BreakpointColor = Color.Parse("#E51400");
    private static readonly Color CurrentLineColor = Color.Parse("#FFFFFF");
    private static readonly Color ExecutionLineColor = Color.Parse("#FFCC00");

    // Cached brushes
    private static readonly IBrush KeywordBrush = new SolidColorBrush(KeywordColor);
    private static readonly IBrush CommentBrush = new SolidColorBrush(CommentColor);
    private static readonly IBrush StringBrush = new SolidColorBrush(StringColor);
    private static readonly IBrush TypeBrush = new SolidColorBrush(TypeColor);
    private static readonly IBrush NumberBrush = new SolidColorBrush(NumberColor);
    private static readonly IBrush DefaultBrush = new SolidColorBrush(DefaultCodeColor);
    private static readonly IBrush PreprocessorBrush = new SolidColorBrush(PreprocessorColor);

    // Known BasicLang keywords for coloring
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "sub", "function", "class", "module", "interface", "enum", "structure", "struct",
        "end", "if", "then", "else", "elseif", "end if", "for", "to", "step", "next",
        "while", "wend", "do", "loop", "until", "select", "case", "end select",
        "dim", "const", "static", "public", "private", "protected", "friend",
        "as", "new", "return", "exit", "continue", "imports", "using",
        "try", "catch", "finally", "throw", "end try",
        "property", "get", "set", "end property",
        "event", "addhandler", "removehandler", "raiseevent",
        "async", "await", "yield", "iterator",
        "true", "false", "nothing", "me", "mybase",
        "and", "or", "not", "xor", "mod", "is", "isnot", "like",
        "of", "in", "each", "when", "with", "end with",
        "namespace", "end namespace", "implements", "inherits",
        "overrides", "overridable", "mustoverride", "shared",
        "readonly", "writeonly", "optional", "byval", "byref",
        "import", "from", "where", "order", "by", "group", "join",
        "let", "into", "aggregate", "distinct", "skip", "take",
    };

    private static readonly HashSet<string> TypeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "long", "short", "byte", "single", "double", "decimal",
        "string", "boolean", "char", "date", "object", "variant",
        "int", "float", "bool", "void",
    };

    public MinimapControl()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _renderSurface = this.FindControl<Border>("RenderSurface");
        _viewportIndicator = this.FindControl<Border>("ViewportIndicator");
        _hoverHighlight = this.FindControl<Border>("HoverHighlight");

        // Apply theme background
        UpdateBackground();
        EditorTheme.ThemeChanged += (_, _) =>
        {
            UpdateBackground();
            InvalidateBitmap();
        };
    }

    private void UpdateBackground()
    {
        if (_renderSurface != null)
        {
            _renderSurface.Background = new SolidColorBrush(EditorTheme.MinimapBackground);
        }
    }

    /// <summary>
    /// Attaches this minimap to an AvaloniaEdit TextEditor instance.
    /// Wires up scroll, text change, and caret events.
    /// </summary>
    public void AttachEditor(TextEditor editor)
    {
        DetachEditor();

        _editor = editor;

        if (_editor != null)
        {
            _editor.TextChanged += OnTextChanged;
            _editor.TextArea.TextView.ScrollOffsetChanged += OnScrollChanged;
            _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

            // Setup debounce timer
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DebounceMs) };
            _debounceTimer.Tick += OnDebounceTimerTick;

            InvalidateBitmap();
        }
    }

    /// <summary>
    /// Detaches from the current editor, removing all event subscriptions.
    /// </summary>
    public void DetachEditor()
    {
        if (_editor != null)
        {
            _editor.TextChanged -= OnTextChanged;
            _editor.TextArea.TextView.ScrollOffsetChanged -= OnScrollChanged;
            _editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        }

        _debounceTimer?.Stop();
        _debounceTimer = null;
        _editor = null;
        _cachedBitmap?.Dispose();
        _cachedBitmap = null;
    }

    /// <summary>
    /// Gets or sets the minimap rendering scale (1 = default, 2 = larger, 3 = largest).
    /// </summary>
    public double MinimapScale
    {
        get => _scale;
        set
        {
            _scale = Math.Clamp(value, 1.0, 3.0);
            InvalidateBitmap();
        }
    }

    /// <summary>
    /// Gets or sets whether the viewport slider/indicator is shown.
    /// </summary>
    public bool ShowSlider
    {
        get => _showSlider;
        set
        {
            _showSlider = value;
            if (_viewportIndicator != null)
                _viewportIndicator.IsVisible = value;
        }
    }

    #region Data Update Methods

    /// <summary>
    /// Updates git change markers displayed on the minimap.
    /// </summary>
    public void SetGitChanges(IReadOnlyList<GitLineChange> changes)
    {
        _gitChanges = changes;
        _gitLineMap.Clear();
        foreach (var change in changes)
        {
            for (int line = change.StartLine; line <= change.EndLine; line++)
            {
                _gitLineMap[line] = change.Kind;
            }
        }
        InvalidateBitmap();
    }

    /// <summary>
    /// Updates breakpoint markers displayed on the minimap.
    /// </summary>
    public void SetBreakpoints(HashSet<int> lines)
    {
        _breakpointLines = lines;
        InvalidateBitmap();
    }

    /// <summary>
    /// Updates diagnostic markers (errors/warnings) on the minimap.
    /// </summary>
    public void SetDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        _diagnosticLines.Clear();
        foreach (var diag in diagnostics)
        {
            if (diag.Line <= 0) continue;
            if (!_diagnosticLines.TryGetValue(diag.Line, out var existing) || diag.Severity > existing)
            {
                _diagnosticLines[diag.Line] = diag.Severity;
            }
        }
        InvalidateBitmap();
    }

    /// <summary>
    /// Updates search result highlight markers on the minimap.
    /// </summary>
    public void SetSearchMatches(IReadOnlyList<int> matchLines)
    {
        _searchMatchLines = matchLines.ToList();
        InvalidateBitmap();
    }

    /// <summary>
    /// Clears all search result markers.
    /// </summary>
    public void ClearSearchMatches()
    {
        _searchMatchLines.Clear();
        InvalidateBitmap();
    }

    /// <summary>
    /// Sets the current execution line (during debugging).
    /// </summary>
    public void SetExecutionLine(int? line)
    {
        _executionLine = line;
        InvalidateBitmap();
    }

    #endregion

    #region Event Handlers

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Debounce: restart timer on each keystroke
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnDebounceTimerTick(object? sender, EventArgs e)
    {
        _debounceTimer?.Stop();
        InvalidateBitmap();
    }

    private void OnScrollChanged(object? sender, EventArgs e)
    {
        UpdateViewportIndicator();
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_editor == null) return;
        var newLine = _editor.TextArea.Caret.Line;
        if (newLine != _currentLine)
        {
            _currentLine = newLine;
            // Current line highlight changes don't need full bitmap rebuild
            InvalidateVisual();
        }
    }

    #endregion

    #region Rendering

    private void InvalidateBitmap()
    {
        _bitmapDirty = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (_editor?.Document == null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var document = _editor.Document;
        var totalLines = document.LineCount;
        var scaledLineHeight = TotalLineHeight * _scale;
        var controlWidth = Bounds.Width;
        var controlHeight = Bounds.Height;

        // Draw background
        context.FillRectangle(
            new SolidColorBrush(EditorTheme.MinimapBackground),
            new Rect(0, 0, controlWidth, controlHeight));

        // Calculate scroll offset for the minimap itself
        // (when document is taller than control, minimap scrolls proportionally)
        var totalMinimapHeight = totalLines * scaledLineHeight;
        var minimapScrollOffset = 0.0;
        if (totalMinimapHeight > controlHeight && _editor.TextArea.TextView.DocumentHeight > 0)
        {
            var scrollFraction = _editor.TextArea.TextView.ScrollOffset.Y /
                Math.Max(1, _editor.TextArea.TextView.DocumentHeight - _editor.TextArea.TextView.Bounds.Height);
            scrollFraction = Math.Clamp(scrollFraction, 0, 1);
            minimapScrollOffset = scrollFraction * (totalMinimapHeight - controlHeight);
        }

        // Determine visible line range on the minimap
        var firstVisibleMinimapLine = Math.Max(1, (int)(minimapScrollOffset / scaledLineHeight) + 1);
        var lastVisibleMinimapLine = Math.Min(totalLines,
            (int)((minimapScrollOffset + controlHeight) / scaledLineHeight) + 2);

        // Render code lines
        for (int i = firstVisibleMinimapLine; i <= lastVisibleMinimapLine; i++)
        {
            var y = (i - 1) * scaledLineHeight - minimapScrollOffset;
            if (y > controlHeight) break;
            if (y + scaledLineHeight < 0) continue;

            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);

            if (!string.IsNullOrWhiteSpace(text))
            {
                RenderCodeLine(context, text, y, controlWidth);
            }

            // Right-side markers (git changes, errors, etc.)
            RenderLineMarkers(context, i, y, controlWidth);
        }

        // Current line highlight
        if (_currentLine >= 1 && _currentLine <= totalLines)
        {
            var currentY = (_currentLine - 1) * scaledLineHeight - minimapScrollOffset;
            if (currentY >= 0 && currentY < controlHeight)
            {
                context.FillRectangle(
                    new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    new Rect(0, currentY, controlWidth, LineHeight * _scale));
            }
        }

        // Execution line highlight (debugging)
        if (_executionLine.HasValue && _executionLine.Value >= 1 && _executionLine.Value <= totalLines)
        {
            var execY = (_executionLine.Value - 1) * scaledLineHeight - minimapScrollOffset;
            if (execY >= 0 && execY < controlHeight)
            {
                context.FillRectangle(
                    new SolidColorBrush(Color.FromArgb(60, 255, 204, 0)),
                    new Rect(0, execY, controlWidth, LineHeight * _scale));
            }
        }

        // Search match highlights (orange tick marks on right edge)
        if (_searchMatchLines.Count > 0)
        {
            var searchBrush = new SolidColorBrush(SearchHighlightColor);
            foreach (var matchLine in _searchMatchLines)
            {
                if (matchLine < 1 || matchLine > totalLines) continue;
                var matchY = (matchLine - 1) * scaledLineHeight - minimapScrollOffset;
                if (matchY >= 0 && matchY < controlHeight)
                {
                    context.FillRectangle(searchBrush,
                        new Rect(controlWidth - 6, matchY, 6, Math.Max(LineHeight * _scale, 2)));
                }
            }
        }

        // Update viewport indicator position
        UpdateViewportIndicatorPosition(totalLines, scaledLineHeight, minimapScrollOffset, controlHeight);
    }

    /// <summary>
    /// Renders a single code line as colored horizontal segments.
    /// </summary>
    private void RenderCodeLine(DrawingContext context, string text, double y, double controlWidth)
    {
        var trimmed = text.TrimStart();
        if (trimmed.Length == 0) return;

        var indent = text.Length - trimmed.Length;
        var x = LeftPadding + indent * CharWidth * _scale;
        var maxWidth = controlWidth - LeftPadding - MarkerWidth - 2;
        var scaledCharWidth = CharWidth * _scale;
        var scaledLineHeight = LineHeight * _scale;

        // Simple token-based coloring
        // For performance, we do a fast classification of the entire line
        // rather than full tokenization

        // Check for comment lines
        if (trimmed.StartsWith("'") || trimmed.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
        {
            var barWidth = Math.Min(trimmed.Length * scaledCharWidth, maxWidth - x + LeftPadding);
            if (barWidth > 0)
                context.FillRectangle(CommentBrush, new Rect(x, y, barWidth, scaledLineHeight));
            return;
        }

        // Check for preprocessor
        if (trimmed.StartsWith("#"))
        {
            var barWidth = Math.Min(trimmed.Length * scaledCharWidth, maxWidth - x + LeftPadding);
            if (barWidth > 0)
                context.FillRectangle(PreprocessorBrush, new Rect(x, y, barWidth, scaledLineHeight));
            return;
        }

        // Tokenize the line for more accurate coloring
        RenderTokenizedLine(context, trimmed, x, y, maxWidth, scaledCharWidth, scaledLineHeight);
    }

    /// <summary>
    /// Renders a line by splitting into simple tokens and coloring each.
    /// </summary>
    private void RenderTokenizedLine(DrawingContext context, string text, double startX, double y,
        double maxWidth, double charWidth, double lineHeight)
    {
        var x = startX;
        var i = 0;
        var inString = false;

        while (i < text.Length && x < maxWidth)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(text[i]))
            {
                x += charWidth;
                i++;
                continue;
            }

            // String literal
            if (text[i] == '"')
            {
                var start = i;
                i++; // skip opening quote
                while (i < text.Length && text[i] != '"') i++;
                if (i < text.Length) i++; // skip closing quote
                var width = Math.Min((i - start) * charWidth, maxWidth - x);
                if (width > 0)
                    context.FillRectangle(StringBrush, new Rect(x, y, width, lineHeight));
                x += (i - start) * charWidth;
                continue;
            }

            // Number
            if (char.IsDigit(text[i]) || (text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                var width = Math.Min((i - start) * charWidth, maxWidth - x);
                if (width > 0)
                    context.FillRectangle(new SolidColorBrush(NumberColor), new Rect(x, y, width, lineHeight));
                x += (i - start) * charWidth;
                continue;
            }

            // Word (identifier or keyword)
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text.Substring(start, i - start);
                var width = Math.Min((i - start) * charWidth, maxWidth - x);

                IBrush brush;
                if (Keywords.Contains(word))
                    brush = KeywordBrush;
                else if (TypeKeywords.Contains(word))
                    brush = TypeBrush;
                else
                    brush = DefaultBrush;

                if (width > 0)
                    context.FillRectangle(brush, new Rect(x, y, width, lineHeight));
                x += (i - start) * charWidth;
                continue;
            }

            // Operators and punctuation
            x += charWidth;
            i++;
        }
    }

    /// <summary>
    /// Renders right-edge markers for git changes, diagnostics, and breakpoints on a given line.
    /// </summary>
    private void RenderLineMarkers(DrawingContext context, int lineNumber, double y, double controlWidth)
    {
        var scaledLineHeight = LineHeight * _scale;
        var markerX = controlWidth - MarkerWidth;

        // Git change markers (left edge, thin bar)
        if (_gitLineMap.TryGetValue(lineNumber, out var gitKind))
        {
            var gitColor = gitKind switch
            {
                GitLineChangeKind.Added => GitAddedColor,
                GitLineChangeKind.Modified => GitModifiedColor,
                GitLineChangeKind.Deleted => GitDeletedColor,
                _ => Colors.Transparent
            };
            context.FillRectangle(new SolidColorBrush(gitColor),
                new Rect(0, y, 2, scaledLineHeight));
        }

        // Breakpoint markers (red dot, right side)
        if (_breakpointLines.Contains(lineNumber))
        {
            var dotSize = Math.Max(3, scaledLineHeight);
            context.DrawEllipse(
                new SolidColorBrush(BreakpointColor), null,
                new Rect(markerX - dotSize, y, dotSize, dotSize));
        }

        // Diagnostic markers (right side, thin bar)
        if (_diagnosticLines.TryGetValue(lineNumber, out var severity))
        {
            var diagColor = severity switch
            {
                DiagnosticSeverity.Error => ErrorMarkerColor,
                DiagnosticSeverity.Warning => WarningMarkerColor,
                _ => Colors.Transparent
            };
            if (diagColor != Colors.Transparent)
            {
                context.FillRectangle(new SolidColorBrush(diagColor),
                    new Rect(markerX, y, MarkerWidth, Math.Max(scaledLineHeight, 2)));
            }
        }
    }

    #endregion

    #region Viewport Indicator

    private void UpdateViewportIndicator()
    {
        if (_viewportIndicator == null || _editor == null) return;
        InvalidateVisual(); // Viewport is drawn as part of Render
    }

    private void UpdateViewportIndicatorPosition(int totalLines, double scaledLineHeight,
        double minimapScrollOffset, double controlHeight)
    {
        if (_viewportIndicator == null || _editor == null || !_showSlider) return;

        var textView = _editor.TextArea.TextView;
        var document = _editor.Document;
        if (document == null || document.LineCount == 0) return;

        // Calculate which lines are visible in the editor
        var editorLineHeight = textView.DefaultLineHeight;
        if (editorLineHeight <= 0) editorLineHeight = 16;

        var firstVisibleLine = Math.Max(1, (int)(textView.ScrollOffset.Y / editorLineHeight) + 1);
        var visibleLineCount = (int)(textView.Bounds.Height / editorLineHeight);
        var lastVisibleLine = Math.Min(totalLines, firstVisibleLine + visibleLineCount);

        // Map to minimap coordinates
        var top = (firstVisibleLine - 1) * scaledLineHeight - minimapScrollOffset;
        var height = Math.Max(10, (lastVisibleLine - firstVisibleLine + 1) * scaledLineHeight);

        // Clamp within control bounds
        top = Math.Clamp(top, 0, Math.Max(0, controlHeight - 10));
        height = Math.Min(height, controlHeight - top);

        _viewportIndicator.Margin = new Thickness(0, top, 0, 0);
        _viewportIndicator.Height = height;
        _viewportIndicator.IsVisible = _showSlider;
    }

    #endregion

    #region Mouse Interaction

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        ScrollToMinimapPosition(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isDragging)
        {
            ScrollToMinimapPosition(e.GetPosition(this).Y);
            e.Handled = true;
        }
        else
        {
            // Show hover highlight
            UpdateHoverHighlight(e.GetPosition(this).Y);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _isHovering = true;
        if (_hoverHighlight != null)
            _hoverHighlight.IsVisible = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _isHovering = false;
        _isDragging = false;
        if (_hoverHighlight != null)
            _hoverHighlight.IsVisible = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        // Forward mouse wheel to the main editor
        if (_editor != null)
        {
            var scrollViewer = _editor.TextArea.TextView;
            var currentOffset = scrollViewer.ScrollOffset;
            var delta = e.Delta.Y * _editor.TextArea.TextView.DefaultLineHeight * 3;
            var newY = Math.Max(0, currentOffset.Y - delta);
            _editor.ScrollToVerticalOffset(newY);
            e.Handled = true;
        }
    }

    private void UpdateHoverHighlight(double y)
    {
        if (_hoverHighlight == null || _editor?.Document == null) return;

        var document = _editor.Document;
        var totalLines = document.LineCount;
        var scaledLineHeight = TotalLineHeight * _scale;

        // Show a highlight region around the hover position matching ~visible editor lines
        var editorLineHeight = _editor.TextArea.TextView.DefaultLineHeight;
        if (editorLineHeight <= 0) editorLineHeight = 16;
        var visibleLineCount = (int)(_editor.TextArea.TextView.Bounds.Height / editorLineHeight);
        var hoverHeight = Math.Max(10, visibleLineCount * scaledLineHeight);

        var top = y - hoverHeight / 2;
        top = Math.Clamp(top, 0, Math.Max(0, Bounds.Height - hoverHeight));

        _hoverHighlight.Margin = new Thickness(0, top, 0, 0);
        _hoverHighlight.Height = hoverHeight;

        // Update tooltip with line info
        var line = GetLineFromMinimapY(y);
        if (line >= 1 && line <= totalLines)
        {
            var lineObj = document.GetLineByNumber(line);
            var text = document.GetText(lineObj.Offset, Math.Min(lineObj.Length, 80));
            if (text.Length > 0)
            {
                ToolTip.SetTip(this, $"Line {line}: {text.TrimStart()}");
                ToolTip.SetShowDelay(this, 200);
            }
        }
    }

    /// <summary>
    /// Scrolls the main editor to the position corresponding to a Y coordinate on the minimap.
    /// Centers the clicked line in the editor viewport.
    /// </summary>
    private void ScrollToMinimapPosition(double y)
    {
        if (_editor?.Document == null) return;

        var line = GetLineFromMinimapY(y);
        var document = _editor.Document;
        line = Math.Clamp(line, 1, document.LineCount);

        // Center the clicked line in the editor viewport
        var editorLineHeight = _editor.TextArea.TextView.DefaultLineHeight;
        if (editorLineHeight <= 0) editorLineHeight = 16;
        var visibleLines = (int)(_editor.TextArea.TextView.Bounds.Height / editorLineHeight);
        var targetLine = Math.Max(1, line - visibleLines / 2);

        var scrollY = (targetLine - 1) * editorLineHeight;
        var currentOffset = _editor.TextArea.TextView.ScrollOffset;
        _editor.ScrollToVerticalOffset(scrollY);
    }

    /// <summary>
    /// Converts a Y coordinate on the minimap to a document line number.
    /// </summary>
    private int GetLineFromMinimapY(double y)
    {
        if (_editor?.Document == null) return 1;

        var totalLines = _editor.Document.LineCount;
        var scaledLineHeight = TotalLineHeight * _scale;
        var controlHeight = Bounds.Height;
        var totalMinimapHeight = totalLines * scaledLineHeight;

        var minimapScrollOffset = 0.0;
        if (totalMinimapHeight > controlHeight && _editor.TextArea.TextView.DocumentHeight > 0)
        {
            var scrollFraction = _editor.TextArea.TextView.ScrollOffset.Y /
                Math.Max(1, _editor.TextArea.TextView.DocumentHeight - _editor.TextArea.TextView.Bounds.Height);
            scrollFraction = Math.Clamp(scrollFraction, 0, 1);
            minimapScrollOffset = scrollFraction * (totalMinimapHeight - controlHeight);
        }

        return (int)((y + minimapScrollOffset) / scaledLineHeight) + 1;
    }

    #endregion
}
