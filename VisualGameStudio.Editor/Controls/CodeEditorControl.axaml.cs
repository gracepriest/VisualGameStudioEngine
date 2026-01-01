using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Editor.Folding;
using VisualGameStudio.Editor.Highlighting;
using VisualGameStudio.Editor.Margins;
using VisualGameStudio.Editor.MultiCursor;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Editor.Controls;

public partial class CodeEditorControl : UserControl
{
    private TextEditor _textEditor = null!;
    private FoldingManager? _foldingManager;
    private readonly BasicLangFoldingStrategy _foldingStrategy = new();
    private readonly BracketHighlighter _bracketHighlighter = new();
    private TextMarkerService? _textMarkerService;
    private BookmarkMargin? _bookmarkMargin;
    private System.Timers.Timer? _foldingUpdateTimer;
    private System.Timers.Timer? _hoverTimer;
    private Point _lastHoverPosition;
    private string? _lastHoverWord;
    private MultiCursorManager? _multiCursorManager;
    private MultiCursorRenderer? _multiCursorRenderer;
    private MultiCursorInputHandler? _multiCursorInputHandler;
    private bool _isInitialized = false;

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<CodeEditorControl, string>(nameof(Text), defaultValue: "");

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(IsReadOnly), defaultValue: false);

    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(ShowLineNumbers), defaultValue: true);

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(WordWrap), defaultValue: false);

    public static readonly StyledProperty<double> EditorFontSizeProperty =
        AvaloniaProperty.Register<CodeEditorControl, double>(nameof(EditorFontSize), defaultValue: 14.0);

    public static readonly StyledProperty<string> EditorFontFamilyProperty =
        AvaloniaProperty.Register<CodeEditorControl, string>(nameof(EditorFontFamily),
            defaultValue: "Cascadia Code, Consolas, Courier New, monospace");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => GetValue(ShowLineNumbersProperty);
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public bool WordWrap
    {
        get => GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    public double EditorFontSize
    {
        get => GetValue(EditorFontSizeProperty);
        set => SetValue(EditorFontSizeProperty, value);
    }

    public string EditorFontFamily
    {
        get => GetValue(EditorFontFamilyProperty);
        set => SetValue(EditorFontFamilyProperty, value);
    }

    public int CaretLine => _textEditor?.TextArea?.Caret?.Line ?? 1;
    public int CaretColumn => _textEditor?.TextArea?.Caret?.Column ?? 1;
    public int CaretOffset => _textEditor?.CaretOffset ?? 0;

    /// <summary>
    /// Gets information about the current selection
    /// </summary>
    public SelectionInfo? GetSelectionInfo()
    {
        if (_textEditor == null) return null;

        var selection = _textEditor.TextArea.Selection;
        if (selection.IsEmpty)
            return null;

        var segment = selection.SurroundingSegment;
        var document = _textEditor.Document;

        var startLocation = document.GetLocation(segment.Offset);
        var endLocation = document.GetLocation(segment.EndOffset);

        return new SelectionInfo
        {
            StartLine = startLocation.Line,
            StartColumn = startLocation.Column,
            EndLine = endLocation.Line,
            EndColumn = endLocation.Column,
            SelectedText = _textEditor.SelectedText
        };
    }

    /// <summary>
    /// Gets the selected text only
    /// </summary>
    public string? GetSelectedText()
    {
        return _textEditor?.SelectedText;
    }

    /// <summary>
    /// Returns true if there is an active selection
    /// </summary>
    public bool HasSelection => _textEditor?.TextArea?.Selection?.IsEmpty == false;

    public event EventHandler? TextChanged;
    public event EventHandler? CaretPositionChanged;
    public event EventHandler<string>? AddToWatchRequested;
    public event EventHandler<DataTipRequestEventArgs>? DataTipRequested;

    /// <summary>
    /// Initializes bookmark support for this editor
    /// </summary>
    public void InitializeBookmarks(IBookmarkService bookmarkService, string? filePath)
    {
        if (_textEditor == null || _bookmarkMargin != null) return;

        _bookmarkMargin = new BookmarkMargin(_textEditor, bookmarkService);
        _bookmarkMargin.SetFilePath(filePath);

        // Insert bookmark margin at the beginning (before line numbers)
        _textEditor.TextArea.LeftMargins.Insert(0, _bookmarkMargin);
    }

    /// <summary>
    /// Updates the file path for bookmark tracking
    /// </summary>
    public void UpdateBookmarkFilePath(string? filePath)
    {
        _bookmarkMargin?.SetFilePath(filePath);
    }

    public CodeEditorControl()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _textEditor = this.FindControl<TextEditor>("TextEditor")!;

        // Register syntax highlighting
        HighlightingLoader.RegisterHighlighting();

        // Apply BasicLang highlighting
        var highlighting = HighlightingManager.Instance.GetDefinition("BasicLang");
        if (highlighting != null)
        {
            _textEditor.SyntaxHighlighting = highlighting;
        }

        // Setup folding
        _foldingManager = FoldingManager.Install(_textEditor.TextArea);

        // Add click handler for fold margin
        _textEditor.TextArea.LeftMargins.CollectionChanged += (s, e) =>
        {
            foreach (var margin in _textEditor.TextArea.LeftMargins)
            {
                if (margin is FoldingMargin foldingMargin)
                {
                    foldingMargin.PointerPressed += OnFoldingMarginPointerPressed;
                }
            }
        };

        // Check existing margins
        foreach (var margin in _textEditor.TextArea.LeftMargins)
        {
            if (margin is FoldingMargin foldingMargin)
            {
                foldingMargin.PointerPressed += OnFoldingMarginPointerPressed;
            }
        }

        // Setup text marker service for error squiggles
        _textMarkerService = new TextMarkerService(_textEditor.Document);
        _textEditor.TextArea.TextView.LineTransformers.Add(_textMarkerService);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_textMarkerService);

        // Setup bracket highlighter
        _textEditor.TextArea.TextView.LineTransformers.Add(_bracketHighlighter);

        // Setup multi-cursor support
        _multiCursorManager = new MultiCursorManager(_textEditor);
        _multiCursorRenderer = new MultiCursorRenderer(_textEditor, _multiCursorManager);
        _multiCursorInputHandler = new MultiCursorInputHandler(_textEditor, _multiCursorManager);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_multiCursorRenderer);
        _multiCursorManager.CursorsChanged += OnMultiCursorsChanged;

        // Configure editor appearance
        ConfigureEditor();

        // Subscribe to text changes
        _textEditor.TextChanged += OnEditorTextChanged;
        _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForBrackets;

        // Subscribe to input events for multi-cursor
        _textEditor.TextArea.PointerPressed += OnTextAreaPointerPressed;
        _textEditor.TextArea.KeyDown += OnTextAreaKeyDown;
        _textEditor.TextArea.TextEntering += OnTextAreaTextEntering;

        // Listen at the TextEditor level for margin clicks (with tunneling)
        _textEditor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Setup folding update timer (debounced)
        _foldingUpdateTimer = new System.Timers.Timer(500);
        _foldingUpdateTimer.Elapsed += (s, e) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateFoldings);
        };
        _foldingUpdateTimer.AutoReset = false;

        // Setup hover timer for data tips (debounced)
        _hoverTimer = new System.Timers.Timer(500);
        _hoverTimer.Elapsed += OnHoverTimerElapsed;
        _hoverTimer.AutoReset = false;

        // Subscribe to pointer move for hover detection
        _textEditor.TextArea.PointerMoved += OnTextAreaPointerMoved;
        _textEditor.TextArea.PointerExited += OnTextAreaPointerExited;

        // Mark as initialized and apply any pending text from binding
        _isInitialized = true;

        if (!string.IsNullOrEmpty(Text) && _textEditor.Document.Text != Text)
        {
            _textEditor.Document.Text = Text;
            UpdateFoldings();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Apply initial text value when attached to visual tree
        // This handles the case where Text was set via binding before the control was fully initialized
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_textEditor != null && !string.IsNullOrEmpty(Text) && _textEditor.Document.Text != Text)
            {
                _textEditor.Document.Text = Text;
                _textEditor.CaretOffset = 0;
                _textEditor.ScrollToHome();
                UpdateFoldings();
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnTextAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Check if click is in the fold margin area (left side before line numbers)
        var point = e.GetCurrentPoint(_textEditor!.TextArea);
        if (point.Properties.IsLeftButtonPressed)
        {
            // The fold margin is typically the first ~12 pixels on the left
            // Line number margin comes after that
            var textView = _textEditor.TextArea.TextView;
            var leftMarginWidth = GetLeftMarginWidth();

            System.IO.File.AppendAllText(@"C:\Users\melvi\source\repos\Visual Game Studio\click_debug.txt",
                $"Click at X={point.Position.X}, leftMarginWidth={leftMarginWidth}\n");

            if (point.Position.X < leftMarginWidth)
            {
                // Click is in margin area - check for fold toggle
                var visualLine = textView.GetVisualLineFromVisualTop(point.Position.Y + textView.ScrollOffset.Y);
                if (visualLine != null && _foldingManager != null)
                {
                    var lineStartOffset = visualLine.FirstDocumentLine.Offset;
                    var lineEndOffset = visualLine.FirstDocumentLine.EndOffset;

                    var folding = _foldingManager.GetFoldingsContaining(lineStartOffset).FirstOrDefault()
                               ?? _foldingManager.AllFoldings.FirstOrDefault(f =>
                                   f.StartOffset >= lineStartOffset && f.StartOffset <= lineEndOffset);

                    if (folding != null)
                    {
                        folding.IsFolded = !folding.IsFolded;
                        textView.Redraw();
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        _multiCursorInputHandler?.HandlePointerPressed(e);
    }

    private double GetLeftMarginWidth()
    {
        double width = 0;
        foreach (var margin in _textEditor!.TextArea.LeftMargins)
        {
            width += margin.Bounds.Width;
        }
        return width;
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_textEditor == null || _foldingManager == null) return;

        var point = e.GetCurrentPoint(_textEditor);
        if (!point.Properties.IsLeftButtonPressed) return;

        // Check if click is in the fold margin area (first ~15 pixels)
        if (point.Position.X >= 0 && point.Position.X < 15)
        {
            var textView = _textEditor.TextArea.TextView;
            var visualLine = textView.GetVisualLineFromVisualTop(point.Position.Y + textView.ScrollOffset.Y);
            if (visualLine != null)
            {
                var lineStartOffset = visualLine.FirstDocumentLine.Offset;
                var lineEndOffset = visualLine.FirstDocumentLine.EndOffset;

                var folding = _foldingManager.GetFoldingsContaining(lineStartOffset).FirstOrDefault()
                           ?? _foldingManager.AllFoldings.FirstOrDefault(f =>
                               f.StartOffset >= lineStartOffset && f.StartOffset <= lineEndOffset);

                if (folding != null)
                {
                    folding.IsFolded = !folding.IsFolded;
                    textView.Redraw();
                    e.Handled = true;
                }
            }
        }
    }

    private void OnFoldingMarginPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_foldingManager == null || _textEditor == null) return;

        var point = e.GetCurrentPoint((Avalonia.Visual)sender!);
        if (!point.Properties.IsLeftButtonPressed) return;

        // Get the visual line at the click position
        var textView = _textEditor.TextArea.TextView;
        var visualLine = textView.GetVisualLineFromVisualTop(point.Position.Y + textView.ScrollOffset.Y);

        if (visualLine != null)
        {
            var lineNumber = visualLine.FirstDocumentLine.LineNumber;
            var lineStartOffset = visualLine.FirstDocumentLine.Offset;
            var lineEndOffset = visualLine.FirstDocumentLine.EndOffset;

            // Find folding at this line
            var folding = _foldingManager.GetFoldingsContaining(lineStartOffset).FirstOrDefault()
                       ?? _foldingManager.AllFoldings.FirstOrDefault(f =>
                           f.StartOffset >= lineStartOffset && f.StartOffset <= lineEndOffset);

            if (folding != null)
            {
                folding.IsFolded = !folding.IsFolded;
                textView.Redraw();
                e.Handled = true;
            }
        }
    }

    private void OnTextAreaKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Ctrl+M to toggle folding at current line
        if (e.Key == Key.M && e.KeyModifiers == KeyModifiers.Control)
        {
            ToggleFoldAtCaret();
            e.Handled = true;
            return;
        }

        // Handle Shift+F9 to add to watch
        if (e.Key == Key.F9 && e.KeyModifiers == KeyModifiers.Shift)
        {
            RequestAddToWatch();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+D to duplicate line
        if (e.Key == Key.D && e.KeyModifiers == KeyModifiers.Control)
        {
            DuplicateLine();
            e.Handled = true;
            return;
        }

        // Handle Alt+Up to move line up
        if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.Alt)
        {
            MoveLineUp();
            e.Handled = true;
            return;
        }

        // Handle Alt+Down to move line down
        if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.Alt)
        {
            MoveLineDown();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+K to delete line
        if (e.Key == Key.K && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            DeleteLine();
            e.Handled = true;
            return;
        }

        _multiCursorInputHandler?.HandleKeyDown(e);
    }

    private void OnTextAreaTextEntering(object? sender, TextInputEventArgs e)
    {
        _multiCursorInputHandler?.HandleTextInput(e);
    }

    private void OnMultiCursorsChanged(object? sender, EventArgs e)
    {
        _textEditor?.TextArea.TextView.Redraw();
    }

    private void ConfigureEditor()
    {
        // Dark theme colors
        _textEditor.Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        _textEditor.Foreground = new SolidColorBrush(Color.Parse("#D4D4D4"));

        // Line number styling
        _textEditor.LineNumbersForeground = new SolidColorBrush(Color.Parse("#858585"));

        // Current line highlighting
        _textEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#2A2D2E"));
        _textEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#2A2D2E")));

        // Selection colors
        _textEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#264F78"));
        _textEditor.TextArea.SelectionForeground = null; // Keep text color

        // Configure options
        _textEditor.Options.EnableHyperlinks = true;
        _textEditor.Options.EnableEmailHyperlinks = false;
        _textEditor.Options.EnableTextDragDrop = true;
        _textEditor.Options.ShowTabs = false;
        _textEditor.Options.ShowSpaces = false;
        _textEditor.Options.ShowEndOfLine = false;
        _textEditor.Options.HighlightCurrentLine = true;
        _textEditor.Options.IndentationSize = 4;
        _textEditor.Options.ConvertTabsToSpaces = true;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        SetCurrentValue(TextProperty, _textEditor.Text);
        TextChanged?.Invoke(this, EventArgs.Empty);

        // Restart folding update timer
        _foldingUpdateTimer?.Stop();
        _foldingUpdateTimer?.Start();
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaretPositionChangedForBrackets(object? sender, EventArgs e)
    {
        _bracketHighlighter.UpdateBracketHighlight(_textEditor.Document, _textEditor.CaretOffset);
        _textEditor.TextArea.TextView.Redraw();
    }

    private void UpdateFoldings()
    {
        if (_foldingManager != null && _textEditor.Document != null)
        {
            _foldingStrategy.UpdateFoldings(_foldingManager, _textEditor.Document);
        }
    }

    /// <summary>
    /// Toggles the folding at the current caret position
    /// </summary>
    public void ToggleFoldAtCaret()
    {
        if (_foldingManager == null || _textEditor == null) return;

        var offset = _textEditor.CaretOffset;
        var folding = _foldingManager.GetFoldingsContaining(offset).FirstOrDefault();

        if (folding != null)
        {
            folding.IsFolded = !folding.IsFolded;
            _textEditor.TextArea.TextView.Redraw();
        }
    }

    /// <summary>
    /// Folds all foldable regions
    /// </summary>
    public void FoldAll()
    {
        if (_foldingManager == null) return;
        foreach (var folding in _foldingManager.AllFoldings)
        {
            folding.IsFolded = true;
        }
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Unfolds all folded regions
    /// </summary>
    public void UnfoldAll()
    {
        if (_foldingManager == null) return;
        foreach (var folding in _foldingManager.AllFoldings)
        {
            folding.IsFolded = false;
        }
        _textEditor?.TextArea.TextView.Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Skip if not initialized yet - OnInitialized will apply pending values
        if (!_isInitialized || _textEditor == null) return;

        if (change.Property == TextProperty)
        {
            var newText = change.GetNewValue<string>() ?? "";
            if (_textEditor.Document.Text != newText)
            {
                _textEditor.Document.Text = newText;
                UpdateFoldings();
            }
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _textEditor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == ShowLineNumbersProperty)
        {
            _textEditor.ShowLineNumbers = change.GetNewValue<bool>();
        }
        else if (change.Property == WordWrapProperty)
        {
            _textEditor.WordWrap = change.GetNewValue<bool>();
        }
        else if (change.Property == EditorFontSizeProperty)
        {
            _textEditor.FontSize = change.GetNewValue<double>();
        }
        else if (change.Property == EditorFontFamilyProperty)
        {
            _textEditor.FontFamily = new FontFamily(change.GetNewValue<string>() ?? "Consolas");
        }
    }

    public void SetCaretPosition(int line, int column)
    {
        if (_textEditor?.TextArea?.Caret != null)
        {
            _textEditor.TextArea.Caret.Line = line;
            _textEditor.TextArea.Caret.Column = column;
            _textEditor.ScrollTo(line, column);
        }
    }

    public void Focus()
    {
        _textEditor?.Focus();
    }

    public void SelectAll()
    {
        _textEditor?.SelectAll();
    }

    public void Copy()
    {
        _textEditor?.Copy();
    }

    public void Cut()
    {
        _textEditor?.Cut();
    }

    public void Paste()
    {
        _textEditor?.Paste();
    }

    public void Undo()
    {
        _textEditor?.Undo();
    }

    public void Redo()
    {
        _textEditor?.Redo();
    }

    public bool CanUndo => _textEditor?.CanUndo ?? false;
    public bool CanRedo => _textEditor?.CanRedo ?? false;

    /// <summary>
    /// Gets the currently selected text, or the word under the caret if no selection
    /// </summary>
    public string? GetSelectedTextOrWordUnderCaret()
    {
        if (_textEditor == null) return null;

        var selectedText = _textEditor.SelectedText;
        if (!string.IsNullOrWhiteSpace(selectedText))
            return selectedText.Trim();

        return GetWordAtOffset(_textEditor.CaretOffset);
    }

    /// <summary>
    /// Triggers the Add to Watch request for the selected text or word under caret
    /// </summary>
    public void RequestAddToWatch()
    {
        var expression = GetSelectedTextOrWordUnderCaret();
        if (!string.IsNullOrWhiteSpace(expression))
        {
            AddToWatchRequested?.Invoke(this, expression);
        }
    }

    /// <summary>
    /// Gets the text marker service for adding error/warning markers
    /// </summary>
    public TextMarkerService? TextMarkerService => _textMarkerService;

    /// <summary>
    /// Adds an error marker at the specified location
    /// </summary>
    public void AddErrorMarker(int startOffset, int length, string? message = null)
    {
        _textMarkerService?.Create(startOffset, length, TextMarkerType.Error, message);
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Adds a warning marker at the specified location
    /// </summary>
    public void AddWarningMarker(int startOffset, int length, string? message = null)
    {
        _textMarkerService?.Create(startOffset, length, TextMarkerType.Warning, message);
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Clears all markers
    /// </summary>
    public void ClearMarkers()
    {
        _textMarkerService?.Clear();
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Toggles line comment on the current selection or line
    /// </summary>
    public void ToggleLineComment()
    {
        if (_textEditor == null) return;

        var selection = _textEditor.TextArea.Selection;
        var document = _textEditor.Document;

        int startLine, endLine;
        if (selection.IsEmpty)
        {
            startLine = endLine = _textEditor.TextArea.Caret.Line;
        }
        else
        {
            startLine = document.GetLineByOffset(selection.SurroundingSegment.Offset).LineNumber;
            endLine = document.GetLineByOffset(selection.SurroundingSegment.EndOffset).LineNumber;
        }

        // Check if all lines are commented
        var allCommented = true;
        for (var line = startLine; line <= endLine; line++)
        {
            var docLine = document.GetLineByNumber(line);
            var lineText = document.GetText(docLine.Offset, docLine.Length).TrimStart();
            if (!lineText.StartsWith("'") && !lineText.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
            {
                allCommented = false;
                break;
            }
        }

        // Toggle comments
        document.BeginUpdate();
        try
        {
            for (var line = startLine; line <= endLine; line++)
            {
                var docLine = document.GetLineByNumber(line);
                var lineText = document.GetText(docLine.Offset, docLine.Length);
                var trimmedText = lineText.TrimStart();
                var leadingWhitespace = lineText.Substring(0, lineText.Length - trimmedText.Length);

                if (allCommented)
                {
                    // Uncomment
                    if (trimmedText.StartsWith("' "))
                    {
                        document.Replace(docLine.Offset, docLine.Length, leadingWhitespace + trimmedText.Substring(2));
                    }
                    else if (trimmedText.StartsWith("'"))
                    {
                        document.Replace(docLine.Offset, docLine.Length, leadingWhitespace + trimmedText.Substring(1));
                    }
                }
                else
                {
                    // Comment
                    if (!string.IsNullOrWhiteSpace(lineText))
                    {
                        document.Replace(docLine.Offset, docLine.Length, leadingWhitespace + "' " + trimmedText);
                    }
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Duplicates the current line or selection
    /// </summary>
    public void DuplicateLine()
    {
        if (_textEditor == null) return;

        var document = _textEditor.Document;
        var selection = _textEditor.TextArea.Selection;

        document.BeginUpdate();
        try
        {
            if (selection.IsEmpty)
            {
                // Duplicate current line
                var line = document.GetLineByNumber(_textEditor.TextArea.Caret.Line);
                var lineText = document.GetText(line.Offset, line.TotalLength);

                // If this is the last line without newline, add one
                if (!lineText.EndsWith("\n") && !lineText.EndsWith("\r\n"))
                {
                    lineText = Environment.NewLine + document.GetText(line.Offset, line.Length);
                }

                document.Insert(line.EndOffset, lineText.TrimEnd('\r', '\n') + Environment.NewLine);

                // Move caret to the duplicated line
                _textEditor.TextArea.Caret.Line = line.LineNumber + 1;
            }
            else
            {
                // Duplicate selection
                var segment = selection.SurroundingSegment;
                var selectedText = document.GetText(segment);

                // Insert at the end of selection
                document.Insert(segment.EndOffset, selectedText);

                // Select the newly duplicated text
                _textEditor.Select(segment.EndOffset, selectedText.Length);
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Moves the current line or selected lines up
    /// </summary>
    public void MoveLineUp()
    {
        if (_textEditor == null) return;

        var document = _textEditor.Document;
        var selection = _textEditor.TextArea.Selection;

        int startLine, endLine;
        if (selection.IsEmpty)
        {
            startLine = endLine = _textEditor.TextArea.Caret.Line;
        }
        else
        {
            startLine = document.GetLineByOffset(selection.SurroundingSegment.Offset).LineNumber;
            endLine = document.GetLineByOffset(selection.SurroundingSegment.EndOffset).LineNumber;

            // If selection ends at start of line, don't include that line
            var endLineStart = document.GetLineByNumber(endLine).Offset;
            if (selection.SurroundingSegment.EndOffset == endLineStart && endLine > startLine)
            {
                endLine--;
            }
        }

        // Can't move up if already at first line
        if (startLine <= 1) return;

        document.BeginUpdate();
        try
        {
            var prevLine = document.GetLineByNumber(startLine - 1);
            var prevLineText = document.GetText(prevLine.Offset, prevLine.TotalLength);

            // Get the lines to move
            var firstLine = document.GetLineByNumber(startLine);
            var lastLine = document.GetLineByNumber(endLine);
            var linesToMoveOffset = firstLine.Offset;
            var linesToMoveLength = lastLine.EndOffset - firstLine.Offset;
            var linesToMoveText = document.GetText(linesToMoveOffset, linesToMoveLength);

            // Ensure lines to move end with newline
            if (!linesToMoveText.EndsWith("\n"))
            {
                linesToMoveText += Environment.NewLine;
            }

            // Remove the previous line's newline from prevLineText for proper insertion
            var prevLineContent = prevLineText.TrimEnd('\r', '\n');

            // Calculate new text: moved lines + previous line
            var startOffset = prevLine.Offset;
            var totalLength = lastLine.EndOffset - prevLine.Offset;

            // Handle case where last line doesn't have newline
            var newText = linesToMoveText.TrimEnd('\r', '\n') + Environment.NewLine + prevLineContent;
            if (endLine < document.LineCount)
            {
                newText += Environment.NewLine;
            }

            // Replace the entire region
            document.Replace(startOffset, totalLength, newText.TrimEnd('\r', '\n') + (endLine < document.LineCount ? Environment.NewLine : ""));

            // Adjust caret position
            var newCaretLine = _textEditor.TextArea.Caret.Line - 1;
            if (newCaretLine >= 1)
            {
                _textEditor.TextArea.Caret.Line = newCaretLine;
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Moves the current line or selected lines down
    /// </summary>
    public void MoveLineDown()
    {
        if (_textEditor == null) return;

        var document = _textEditor.Document;
        var selection = _textEditor.TextArea.Selection;

        int startLine, endLine;
        if (selection.IsEmpty)
        {
            startLine = endLine = _textEditor.TextArea.Caret.Line;
        }
        else
        {
            startLine = document.GetLineByOffset(selection.SurroundingSegment.Offset).LineNumber;
            endLine = document.GetLineByOffset(selection.SurroundingSegment.EndOffset).LineNumber;

            // If selection ends at start of line, don't include that line
            var endLineStart = document.GetLineByNumber(endLine).Offset;
            if (selection.SurroundingSegment.EndOffset == endLineStart && endLine > startLine)
            {
                endLine--;
            }
        }

        // Can't move down if already at last line
        if (endLine >= document.LineCount) return;

        document.BeginUpdate();
        try
        {
            var nextLine = document.GetLineByNumber(endLine + 1);
            var nextLineText = document.GetText(nextLine.Offset, nextLine.TotalLength);

            // Get the lines to move
            var firstLine = document.GetLineByNumber(startLine);
            var lastLine = document.GetLineByNumber(endLine);
            var linesToMoveOffset = firstLine.Offset;
            var linesToMoveLength = lastLine.EndOffset - firstLine.Offset;
            var linesToMoveText = document.GetText(linesToMoveOffset, linesToMoveLength);

            // Calculate the region to replace
            var startOffset = firstLine.Offset;
            var totalLength = nextLine.EndOffset - firstLine.Offset;

            // Build new text: next line + lines to move
            var nextLineContent = nextLineText.TrimEnd('\r', '\n');
            var movedContent = linesToMoveText.TrimEnd('\r', '\n');

            var newText = nextLineContent + Environment.NewLine + movedContent;
            if (endLine + 1 < document.LineCount)
            {
                newText += Environment.NewLine;
            }

            // Replace the entire region
            document.Replace(startOffset, totalLength, newText.TrimEnd('\r', '\n') + (endLine + 1 < document.LineCount ? Environment.NewLine : ""));

            // Adjust caret position
            var newCaretLine = _textEditor.TextArea.Caret.Line + 1;
            if (newCaretLine <= document.LineCount)
            {
                _textEditor.TextArea.Caret.Line = newCaretLine;
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Deletes the current line
    /// </summary>
    public void DeleteLine()
    {
        if (_textEditor == null) return;

        var document = _textEditor.Document;
        var line = document.GetLineByNumber(_textEditor.TextArea.Caret.Line);

        document.BeginUpdate();
        try
        {
            if (line.LineNumber == document.LineCount)
            {
                // Last line - also remove the preceding newline if there is one
                var startOffset = line.LineNumber > 1
                    ? document.GetLineByNumber(line.LineNumber - 1).EndOffset
                    : line.Offset;
                document.Remove(startOffset, line.EndOffset - startOffset);
            }
            else
            {
                // Remove line including its newline
                document.Remove(line.Offset, line.TotalLength);
            }
        }
        finally
        {
            document.EndUpdate();
        }
    }

    /// <summary>
    /// Finds text in the document
    /// </summary>
    public bool Find(string searchText, bool matchCase = false, bool wholeWord = false, bool useRegex = false)
    {
        if (_textEditor == null || string.IsNullOrEmpty(searchText))
            return false;

        var document = _textEditor.Document;
        var startOffset = _textEditor.CaretOffset;

        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Search from caret to end
        var text = document.Text;
        var foundIndex = -1;

        if (useRegex)
        {
            try
            {
                var options = matchCase ? System.Text.RegularExpressions.RegexOptions.None :
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                var regex = new System.Text.RegularExpressions.Regex(searchText, options);
                var match = regex.Match(text, startOffset);
                if (match.Success)
                {
                    foundIndex = match.Index;
                    _textEditor.Select(foundIndex, match.Length);
                    _textEditor.ScrollTo(_textEditor.TextArea.Caret.Line, _textEditor.TextArea.Caret.Column);
                    return true;
                }
                // Wrap around
                match = regex.Match(text, 0);
                if (match.Success && match.Index < startOffset)
                {
                    foundIndex = match.Index;
                    _textEditor.Select(foundIndex, match.Length);
                    _textEditor.ScrollTo(_textEditor.TextArea.Caret.Line, _textEditor.TextArea.Caret.Column);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
        else
        {
            foundIndex = text.IndexOf(searchText, startOffset, comparison);
            if (foundIndex < 0)
            {
                // Wrap around
                foundIndex = text.IndexOf(searchText, 0, comparison);
            }

            if (foundIndex >= 0)
            {
                if (wholeWord && !IsWholeWord(text, foundIndex, searchText.Length))
                {
                    // Continue searching for whole word match
                    var searchStart = foundIndex + 1;
                    while (searchStart < text.Length)
                    {
                        foundIndex = text.IndexOf(searchText, searchStart, comparison);
                        if (foundIndex < 0) break;
                        if (IsWholeWord(text, foundIndex, searchText.Length))
                        {
                            _textEditor.Select(foundIndex, searchText.Length);
                            _textEditor.ScrollTo(_textEditor.TextArea.Caret.Line, _textEditor.TextArea.Caret.Column);
                            return true;
                        }
                        searchStart = foundIndex + 1;
                    }
                    return false;
                }

                _textEditor.Select(foundIndex, searchText.Length);
                _textEditor.ScrollTo(_textEditor.TextArea.Caret.Line, _textEditor.TextArea.Caret.Column);
                return true;
            }
        }

        return false;
    }

    private bool IsWholeWord(string text, int offset, int length)
    {
        var start = offset > 0 && char.IsLetterOrDigit(text[offset - 1]);
        var end = offset + length < text.Length && char.IsLetterOrDigit(text[offset + length]);
        return !start && !end;
    }

    /// <summary>
    /// Replaces the current selection with the specified text
    /// </summary>
    public bool Replace(string searchText, string replaceText, bool matchCase = false, bool wholeWord = false)
    {
        if (_textEditor == null || string.IsNullOrEmpty(searchText))
            return false;

        var selection = _textEditor.SelectedText;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(selection, searchText, comparison))
        {
            _textEditor.Document.Replace(_textEditor.SelectionStart, _textEditor.SelectionLength, replaceText);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Replaces all occurrences of the search text
    /// </summary>
    public int ReplaceAll(string searchText, string replaceText, bool matchCase = false, bool wholeWord = false, bool useRegex = false)
    {
        if (_textEditor == null || string.IsNullOrEmpty(searchText))
            return 0;

        var document = _textEditor.Document;
        var text = document.Text;
        var count = 0;

        document.BeginUpdate();
        try
        {
            if (useRegex)
            {
                try
                {
                    var options = matchCase ? System.Text.RegularExpressions.RegexOptions.None :
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                    var regex = new System.Text.RegularExpressions.Regex(searchText, options);
                    var newText = regex.Replace(text, replaceText);
                    count = regex.Matches(text).Count;
                    document.Text = newText;
                }
                catch
                {
                    return 0;
                }
            }
            else
            {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var offset = 0;

                while (offset < document.TextLength)
                {
                    var foundIndex = document.Text.IndexOf(searchText, offset, comparison);
                    if (foundIndex < 0) break;

                    if (!wholeWord || IsWholeWord(document.Text, foundIndex, searchText.Length))
                    {
                        document.Replace(foundIndex, searchText.Length, replaceText);
                        offset = foundIndex + replaceText.Length;
                        count++;
                    }
                    else
                    {
                        offset = foundIndex + 1;
                    }
                }
            }
        }
        finally
        {
            document.EndUpdate();
        }

        return count;
    }

    /// <summary>
    /// Gets the word at the specified offset
    /// </summary>
    public string? GetWordAtOffset(int offset)
    {
        if (_textEditor == null || offset < 0 || offset > _textEditor.Document.TextLength)
            return null;

        var document = _textEditor.Document;
        var start = offset;
        var end = offset;

        // Find word start
        while (start > 0 && IsWordChar(document.GetCharAt(start - 1)))
            start--;

        // Find word end
        while (end < document.TextLength && IsWordChar(document.GetCharAt(end)))
            end++;

        if (start < end)
            return document.GetText(start, end - start);

        return null;
    }

    private bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    /// <summary>
    /// Gets the offset from line and column
    /// </summary>
    public int GetOffset(int line, int column)
    {
        if (_textEditor?.Document == null) return 0;
        var docLine = _textEditor.Document.GetLineByNumber(Math.Min(line, _textEditor.Document.LineCount));
        return docLine.Offset + Math.Min(column - 1, docLine.Length);
    }

    /// <summary>
    /// Gets the line number from offset
    /// </summary>
    public int GetLineFromOffset(int offset)
    {
        if (_textEditor?.Document == null) return 1;
        return _textEditor.Document.GetLineByOffset(Math.Min(offset, _textEditor.Document.TextLength)).LineNumber;
    }

    #region Multi-Cursor Support

    /// <summary>
    /// Gets whether multi-cursor mode is active
    /// </summary>
    public bool IsMultiCursorActive => _multiCursorManager?.IsEnabled ?? false;

    /// <summary>
    /// Gets the number of active cursors (including main cursor)
    /// </summary>
    public int CursorCount => _multiCursorManager?.CursorCount ?? 1;

    /// <summary>
    /// Adds a cursor at the specified offset
    /// </summary>
    public void AddCursorAt(int offset)
    {
        _multiCursorManager?.AddCursor(offset);
    }

    /// <summary>
    /// Adds a cursor above the current line
    /// </summary>
    public void AddCursorAbove()
    {
        _multiCursorManager?.AddCursorAbove();
    }

    /// <summary>
    /// Adds a cursor below the current line
    /// </summary>
    public void AddCursorBelow()
    {
        _multiCursorManager?.AddCursorBelow();
    }

    /// <summary>
    /// Adds the next occurrence of the selected word as a new cursor
    /// </summary>
    public void AddNextOccurrence()
    {
        _multiCursorManager?.AddNextOccurrence();
    }

    /// <summary>
    /// Selects all occurrences of the current word or selection
    /// </summary>
    public void SelectAllOccurrences()
    {
        _multiCursorManager?.SelectAllOccurrences();
    }

    /// <summary>
    /// Clears all additional cursors and exits multi-cursor mode
    /// </summary>
    public void ClearMultiCursors()
    {
        _multiCursorManager?.Disable();
    }

    #endregion

    #region Data Tips / Hover Evaluation

    private void OnTextAreaPointerMoved(object? sender, PointerEventArgs e)
    {
        // Reset and restart the hover timer
        _hoverTimer?.Stop();

        var point = e.GetPosition(_textEditor.TextArea.TextView);
        _lastHoverPosition = point;

        // Get the word under the mouse
        var offset = GetOffsetFromPoint(point);
        if (offset >= 0)
        {
            var word = GetWordAtOffset(offset);
            if (!string.IsNullOrWhiteSpace(word) && word != _lastHoverWord)
            {
                _lastHoverWord = word;
                _hoverTimer?.Start();
            }
        }
        else
        {
            _lastHoverWord = null;
        }
    }

    private void OnTextAreaPointerExited(object? sender, PointerEventArgs e)
    {
        _hoverTimer?.Stop();
        _lastHoverWord = null;
    }

    private void OnHoverTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrWhiteSpace(_lastHoverWord))
            {
                // Get screen position for the tooltip
                var textView = _textEditor.TextArea.TextView;
                var screenPoint = textView.PointToScreen(_lastHoverPosition);

                DataTipRequested?.Invoke(this, new DataTipRequestEventArgs(
                    _lastHoverWord,
                    screenPoint.X,
                    screenPoint.Y + 20 // Offset below cursor
                ));
            }
        });
    }

    private int GetOffsetFromPoint(Point point)
    {
        if (_textEditor?.TextArea?.TextView == null) return -1;

        var textView = _textEditor.TextArea.TextView;
        var visualLine = textView.GetVisualLineFromVisualTop(point.Y + textView.ScrollOffset.Y);

        if (visualLine == null) return -1;

        var textLine = visualLine.GetTextLineByVisualYPosition(point.Y + textView.ScrollOffset.Y);
        if (textLine == null) return -1;

        var xPos = point.X + textView.ScrollOffset.X;

        // Get visual column from x position using the Point-based method
        var visualColumn = visualLine.GetVisualColumn(new Point(xPos, point.Y + textView.ScrollOffset.Y - visualLine.VisualTop));
        var docOffset = visualLine.FirstDocumentLine.Offset + visualColumn;

        if (docOffset < 0 || docOffset > _textEditor.Document.TextLength)
            return -1;

        return docOffset;
    }

    /// <summary>
    /// Hides the current data tip (if any)
    /// </summary>
    public void HideDataTip()
    {
        _hoverTimer?.Stop();
        _lastHoverWord = null;
    }

    #endregion
}

/// <summary>
/// Event args for data tip requests
/// </summary>
public class DataTipRequestEventArgs : EventArgs
{
    public string Expression { get; }
    public double ScreenX { get; }
    public double ScreenY { get; }

    public DataTipRequestEventArgs(string expression, double screenX, double screenY)
    {
        Expression = expression;
        ScreenX = screenX;
        ScreenY = screenY;
    }
}

/// <summary>
/// Information about the current text selection
/// </summary>
public class SelectionInfo
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string SelectedText { get; set; } = "";

    /// <summary>
    /// Gets whether the selection spans multiple lines
    /// </summary>
    public bool IsMultiLine => StartLine != EndLine;
}
