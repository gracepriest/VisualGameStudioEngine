using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Editor.Completion;
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
    private readonly BracketPairColorizer _bracketPairColorizer = new();
    private TextMarkerService? _textMarkerService;
    private BookmarkMargin? _bookmarkMargin;
    private System.Timers.Timer? _foldingUpdateTimer;
    private System.Timers.Timer? _hoverTimer;
    private Point _lastHoverPosition;
    private string? _lastHoverWord;
    private MultiCursorManager? _multiCursorManager;
    private MultiCursorRenderer? _multiCursorRenderer;
    private MultiCursorInputHandler? _multiCursorInputHandler;
    private IndentationGuideRenderer? _indentationGuideRenderer;
    private bool _isInitialized = false;
    private CompletionWindow? _completionWindow;
    private BreakpointMargin? _breakpointMargin;
    private GitGutterMargin? _gitGutterMargin;
    private DiagnosticMargin? _diagnosticMargin;
    private bool _isUpdatingFoldings = false;
    private bool _isFoldingEnabled = true;
    private MinimapControl? _minimap;
    private bool _isUpdatingTextFromEditor = false;  // Prevents binding feedback loop
    private bool _hasLoadedInitialText = false;  // After initial load, don't overwrite from binding
    private Avalonia.Controls.Primitives.Popup? _errorTooltip;
    private TextBlock? _errorTooltipText;
    private int _lastHoverOffset = -1;
    private IReadOnlyList<DocumentLinkInfo>? _documentLinks;
    private InlineFindReplaceControl? _inlineFindReplace;
    private StickyScrollControl? _stickyScroll;
    private bool _autoCloseBrackets = true;
    private BasicLangIndentationStrategy? _indentationStrategy;
    private ILanguageService? _languageService;
    private string? _documentFilePath;
    private bool _isColumnSelectionMode;
    private CurrentLineNumberMargin? _currentLineNumberMargin;
    private TextMarkers.SelectionOccurrenceHighlighter? _selectionOccurrenceHighlighter;

    // Smooth scrolling state
    private bool _smoothScrollingEnabled = true;
    private double _scrollTargetY;
    private double _scrollCurrentY;
    private double _scrollTargetX;
    private double _scrollCurrentX;
    private DispatcherTimer? _smoothScrollTimer;
    private const double SmoothScrollDuration = 100.0; // milliseconds
    private const double SmoothScrollInterval = 8.0;   // ~120fps timer tick
    private const double SmoothScrollEpsilon = 0.5;    // snap threshold in pixels
    private bool _isSmoothScrolling;

    // Cursor fade animation state
    private CursorFadeRenderer? _cursorFadeRenderer;

    // Inline color picker state
    private TextMarkers.InlineColorRenderer? _inlineColorRenderer;
    private Avalonia.Controls.Primitives.Popup? _colorPickerPopup;
    private ColorPickerPopup? _colorPickerControl;

    private static readonly Dictionary<char, char> AutoClosePairs = new()
    {
        { '(', ')' }, { '[', ']' }, { '{', '}' }, { '"', '"' }, { '\'', '\'' }
    };
    private static readonly HashSet<char> ClosingBrackets = new() { ')', ']', '}' };

    /// <summary>
    /// Pairs that wrap selected text when typed with an active selection.
    /// Mirrors VS Code's "surroundingPairs" from language-configuration.json.
    /// </summary>
    private static readonly Dictionary<char, char> SurroundingPairs = new()
    {
        { '(', ')' }, { '[', ']' }, { '{', '}' }, { '"', '"' }
    };

    public bool AutoCloseBrackets
    {
        get => _autoCloseBrackets;
        set => _autoCloseBrackets = value;
    }

    public static readonly StyledProperty<bool> StickyScrollEnabledProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(StickyScrollEnabled), defaultValue: true);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<CodeEditorControl, string>(nameof(Text), defaultValue: "");

    public static readonly StyledProperty<bool> IsReadOnlyProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(IsReadOnly), defaultValue: false);

    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(ShowLineNumbers), defaultValue: true);

    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(WordWrap), defaultValue: false);

    public static readonly StyledProperty<bool> BracketPairColorizationProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(BracketPairColorization), defaultValue: true);

    public static readonly StyledProperty<bool> SmoothScrollingProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(SmoothScrolling), defaultValue: true);

    public static readonly StyledProperty<double> EditorFontSizeProperty =
        AvaloniaProperty.Register<CodeEditorControl, double>(nameof(EditorFontSize), defaultValue: 14.0);

    public static readonly StyledProperty<string> EditorFontFamilyProperty =
        AvaloniaProperty.Register<CodeEditorControl, string>(nameof(EditorFontFamily),
            defaultValue: "Cascadia Code, Consolas, Courier New, monospace");

    public static readonly StyledProperty<bool> EnableFontLigaturesProperty =
        AvaloniaProperty.Register<CodeEditorControl, bool>(nameof(EnableFontLigatures), defaultValue: false);

    public static readonly StyledProperty<TextDocument?> DocumentProperty =
        AvaloniaProperty.Register<CodeEditorControl, TextDocument?>(nameof(Document));

    /// <summary>
    /// Gets or sets the TextDocument. When set, this document (with its undo history) is used directly.
    /// This is preferred over Text property for preserving undo across tab switches.
    /// </summary>
    public TextDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

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

    /// <summary>
    /// Gets or sets whether bracket pair colorization is enabled.
    /// When enabled, nested brackets are colored by depth (Gold, Violet, Cyan).
    /// </summary>
    public bool BracketPairColorization
    {
        get => GetValue(BracketPairColorizationProperty);
        set
        {
            SetValue(BracketPairColorizationProperty, value);
            _bracketPairColorizer.IsEnabled = value;
            _textEditor?.TextArea?.TextView?.Redraw();
        }
    }

    /// <summary>
    /// Gets or sets whether sticky scroll is enabled.
    /// When enabled, enclosing scope headers are pinned at the top of the editor during scrolling.
    /// </summary>
    public bool StickyScrollEnabled
    {
        get => GetValue(StickyScrollEnabledProperty);
        set => SetValue(StickyScrollEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether smooth scrolling is enabled.
    /// When enabled, mouse wheel scrolling is animated with easing instead of jumping instantly.
    /// </summary>
    public bool SmoothScrolling
    {
        get => GetValue(SmoothScrollingProperty);
        set => SetValue(SmoothScrollingProperty, value);
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

    /// <summary>
    /// Gets or sets whether font ligatures are enabled.
    /// When enabled, supported fonts (Cascadia Code, Fira Code, JetBrains Mono, etc.)
    /// will render character sequences like !=, ==, => as single ligature glyphs.
    /// </summary>
    public bool EnableFontLigatures
    {
        get => GetValue(EnableFontLigaturesProperty);
        set => SetValue(EnableFontLigaturesProperty, value);
    }

    public int CaretLine => _textEditor?.TextArea?.Caret?.Line ?? 1;
    public int CaretColumn => _textEditor?.TextArea?.Caret?.Column ?? 1;
    public int CaretOffset => _textEditor?.CaretOffset ?? 0;

    /// <summary>
    /// Gets or sets whether column (rectangular) selection mode is active.
    /// When enabled, all mouse and keyboard selections produce rectangular selections
    /// without requiring the Alt key to be held.
    /// Alt+click/drag always works for rectangular selection regardless of this setting.
    /// </summary>
    public bool IsColumnSelectionMode
    {
        get => _isColumnSelectionMode;
        set
        {
            if (_isColumnSelectionMode == value) return;
            _isColumnSelectionMode = value;

            if (_textEditor?.TextArea != null)
            {
                // When column mode is active, convert the current selection to rectangular
                // or clear to a simple caret position
                if (value && _textEditor.TextArea.Selection is not RectangleSelection
                    && !_textEditor.TextArea.Selection.IsEmpty)
                {
                    var start = _textEditor.TextArea.Selection.StartPosition;
                    var end = _textEditor.TextArea.Selection.EndPosition;
                    _textEditor.TextArea.Selection = new RectangleSelection(_textEditor.TextArea, start, end);
                }
            }

            ColumnSelectionModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Toggles column (rectangular) selection mode on/off.
    /// </summary>
    public void ToggleColumnSelectionMode()
    {
        IsColumnSelectionMode = !IsColumnSelectionMode;
    }

    /// <summary>
    /// Raised when column selection mode is toggled.
    /// </summary>
    public event EventHandler? ColumnSelectionModeChanged;

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
    public event EventHandler<CompletionRequestEventArgs>? CompletionRequested;
    public event EventHandler<int>? BreakpointToggled;
    public event EventHandler? EditorReady;
    public event EventHandler? GoToDefinitionRequested;
    public event EventHandler? PeekDefinitionRequested;
    public event EventHandler? FindAllReferencesRequested;
    public event EventHandler? RenameSymbolRequested;
    public event EventHandler? CodeActionsRequested;
    public event EventHandler? FormatDocumentRequested;
    public event EventHandler<SignatureHelpRequestEventArgs>? SignatureHelpRequested;
    public event EventHandler<DocumentHighlightRequestEventArgs>? DocumentHighlightRequested;
    public event EventHandler<DocumentLinkClickedEventArgs>? DocumentLinkClicked;
    public event EventHandler? GoToLineRequested;
    public event EventHandler<FileDroppedEventArgs>? FileDropped;

    /// <summary>
    /// Returns true if the editor is fully initialized and ready for use
    /// </summary>
    public bool IsReady => _isInitialized && _textEditor != null;

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

    /// <summary>
    /// Injects the LSP language service so folding ranges can be fetched from the server.
    /// When set, UpdateFoldings will prefer LSP-based ranges over the local regex strategy.
    /// </summary>
    public void SetLanguageService(ILanguageService? languageService, string? filePath)
    {
        _languageService = languageService;
        _documentFilePath = filePath;
    }

    /// <summary>
    /// Sets the syntax highlighting for the editor based on the file extension.
    /// Checks TextMate grammars loaded from VS Code extensions first, then falls back
    /// to AvalonEdit built-in definitions, and finally to BasicLang highlighting.
    /// </summary>
    /// <param name="filePath">The file path to determine highlighting for.</param>
    public void SetHighlightingForFile(string? filePath)
    {
        if (_textEditor == null || string.IsNullOrEmpty(filePath))
            return;

        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext))
            return;

        // BasicLang files use the built-in highlighting
        if (ext.Equals(".bas", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bl", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".basic", StringComparison.OrdinalIgnoreCase))
        {
            var basicLangDef = HighlightingManager.Instance.GetDefinition("BasicLang");
            if (basicLangDef != null)
            {
                _textEditor.SyntaxHighlighting = basicLangDef;
            }
            return;
        }

        // Check TextMate-derived definitions (from VS Code extensions)
        var definition = Highlighting.HighlightingLoader.GetDefinitionForExtension(ext);
        if (definition != null)
        {
            _textEditor.SyntaxHighlighting = definition;
            return;
        }

        // Fall back to AvalonEdit's built-in definitions
        var builtIn = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        if (builtIn != null)
        {
            _textEditor.SyntaxHighlighting = builtIn;
            return;
        }

        // No highlighting found - clear it
        _textEditor.SyntaxHighlighting = null;
    }

    /// <summary>
    /// Gets the active language configuration for the current file, if one was loaded
    /// from a VS Code extension. Used for bracket matching, auto-close, and comments.
    /// </summary>
    public Highlighting.LanguageConfigurationData? GetActiveLanguageConfiguration()
    {
        if (string.IsNullOrEmpty(_documentFilePath))
            return null;

        var ext = Path.GetExtension(_documentFilePath);
        return Highlighting.HighlightingLoader.GetLanguageConfigurationForExtension(ext);
    }

    public CodeEditorControl()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _textEditor = this.FindControl<TextEditor>("TextEditor")!;

        // Initialize minimap
        _minimap = this.FindControl<MinimapControl>("Minimap");
        _minimap?.AttachEditor(_textEditor);

        // Initialize sticky scroll
        _stickyScroll = this.FindControl<StickyScrollControl>("StickyScroll");
        if (_stickyScroll != null)
        {
            _stickyScroll.AttachEditor(_textEditor);
            _stickyScroll.LineClicked += OnStickyScrollLineClicked;
        }

        // Initialize inline find/replace overlay
        _inlineFindReplace = this.FindControl<InlineFindReplaceControl>("InlineFindReplace");
        _inlineFindReplace?.AttachToEditor(_textEditor);
        if (_inlineFindReplace != null)
        {
            _inlineFindReplace.CloseRequested += (_, _) =>
            {
                _textEditor?.Focus();
            };
        }

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
                    foldingMargin.PointerPressed -= OnFoldingMarginPointerPressed;
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

        // Setup diagnostic gutter margin (error/warning/info icons)
        _diagnosticMargin = new DiagnosticMargin(_textEditor);
        _textEditor.TextArea.LeftMargins.Insert(0, _diagnosticMargin);

        // Setup bracket highlighter
        _textEditor.TextArea.TextView.LineTransformers.Add(_bracketHighlighter);

        // Setup bracket pair colorization (colors nested brackets by depth)
        _bracketPairColorizer.IsEnabled = BracketPairColorization;
        _bracketPairColorizer.Invalidate(_textEditor.Document);
        _textEditor.TextArea.TextView.LineTransformers.Add(_bracketPairColorizer);

        // Setup multi-cursor support
        _multiCursorManager = new MultiCursorManager(_textEditor);
        _multiCursorRenderer = new MultiCursorRenderer(_textEditor, _multiCursorManager);
        _multiCursorInputHandler = new MultiCursorInputHandler(_textEditor, _multiCursorManager);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_multiCursorRenderer);
        _multiCursorManager.CursorsChanged += OnMultiCursorsChanged;

        // Setup indentation guide renderer
        _indentationGuideRenderer = new IndentationGuideRenderer(_textEditor);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_indentationGuideRenderer);

        // Configure editor appearance
        ConfigureEditor();

        // Setup custom line number margin with current-line highlighting
        SetupCurrentLineNumberMargin();

        // Setup selection occurrence highlighter (highlights all matches of selected text)
        _selectionOccurrenceHighlighter = new TextMarkers.SelectionOccurrenceHighlighter(_textEditor);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_selectionOccurrenceHighlighter);

        // Subscribe to text changes
        _textEditor.TextChanged += OnEditorTextChanged;
        _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForBrackets;
        _textEditor.TextArea.Caret.PositionChanged += OnCaretPositionChangedForLineNumbers;
        _textEditor.TextArea.SelectionChanged += OnSelectionChangedForOccurrences;

        // Subscribe to input events for multi-cursor
        _textEditor.TextArea.PointerPressed += OnTextAreaPointerPressed;
        _textEditor.TextArea.PointerReleased += OnTextAreaPointerReleasedForColumnSelection;
        _textEditor.TextArea.KeyUp += OnTextAreaKeyUpForColumnSelection;
        _textEditor.TextArea.KeyDown += OnTextAreaKeyDown;
        _textEditor.TextArea.TextEntering += OnTextAreaTextEntering;
        _textEditor.TextArea.TextEntered += OnTextAreaTextEntered;

        // Listen at the TextEditor level for margin clicks (with tunneling)
        _textEditor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Enable drag-and-drop for text movement and file drops
        DragDrop.SetAllowDrop(_textEditor, true);
        DragDrop.SetAllowDrop(_textEditor.TextArea, true);
        _textEditor.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        _textEditor.AddHandler(DragDrop.DropEvent, OnDrop);

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

        // Setup smooth scrolling: intercept mouse wheel on the TextEditor
        _textEditor.AddHandler(PointerWheelChangedEvent, OnEditorPointerWheelChanged,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Setup smooth scrolling timer
        _smoothScrollTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(SmoothScrollInterval)
        };
        _smoothScrollTimer.Tick += OnSmoothScrollTick;

        // Initialize scroll targets to current position
        _scrollCurrentY = _textEditor.TextArea.TextView.ScrollOffset.Y;
        _scrollCurrentX = _textEditor.TextArea.TextView.ScrollOffset.X;
        _scrollTargetY = _scrollCurrentY;
        _scrollTargetX = _scrollCurrentX;

        // Setup cursor fade animation renderer
        _cursorFadeRenderer = new CursorFadeRenderer(_textEditor);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_cursorFadeRenderer);

        // Setup inline color swatch renderer
        _inlineColorRenderer = new TextMarkers.InlineColorRenderer(_textEditor);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_inlineColorRenderer);
        _inlineColorRenderer.ColorSwatchClicked += OnColorSwatchClicked;

        // Mark as initialized and apply any pending document/text from binding
        _isInitialized = true;

        // Prefer Document property (preserves undo history across tab switches)
        if (Document != null)
        {
            ApplyDocument(Document);
            _hasLoadedInitialText = true;
        }
        else if (!string.IsNullOrEmpty(Text))
        {
            // Only set text if it's different - this preserves undo when text matches
            if (_textEditor.Document.Text != Text)
            {
                _textEditor.Document.Text = Text;
                // Clear undo stack only for initial load
                _textEditor.Document.UndoStack.ClearAll();
            }
            _hasLoadedInitialText = true;
            UpdateFoldings();
        }

        // Notify that the editor is ready for use
        EditorReady?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // When using Document binding, the Document carries the undo history
        // and is the source of truth. Don't touch it on reattach.
        if (Document != null)
        {
            // Just refresh the display
            UpdateFoldings();
            return;
        }

        // Fallback for Text-only binding (legacy mode)
        if (_textEditor != null && !string.IsNullOrEmpty(Text))
        {
            if (_textEditor.Document.Text != Text)
            {
                _textEditor.Document.Text = Text;
                UpdateFoldings();
            }
        }
    }

    /// <summary>
    /// Apply a new TextDocument to the editor, updating all dependent services
    /// </summary>
    private void ApplyDocument(TextDocument newDocument)
    {
        if (_textEditor == null || newDocument == null) return;

        // Uninstall folding from old document
        if (_foldingManager != null)
        {
            try { FoldingManager.Uninstall(_foldingManager); }
            catch (Exception) { /* Ignore */ }
        }

        // Set the new document
        _textEditor.Document = newDocument;

        // Reinstall folding on new document
        _foldingManager = FoldingManager.Install(_textEditor.TextArea);

        // Update text marker service for new document
        if (_textMarkerService != null)
        {
            try
            {
                _textEditor.TextArea.TextView.LineTransformers.Remove(_textMarkerService);
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_textMarkerService);
            }
            catch (Exception) { /* Ignore */ }
        }
        _textMarkerService = new TextMarkerService(_textEditor.Document);
        _textEditor.TextArea.TextView.LineTransformers.Add(_textMarkerService);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_textMarkerService);

        // Update inline find/replace renderer for new document
        _inlineFindReplace?.OnDocumentChanged();

        // Force visual refresh to display the document content
        _textEditor.TextArea.TextView.Redraw();
        UpdateFoldings();
    }

    /// <summary>
    /// Sets a shared TextDocument, preserving its undo history.
    /// Call this after the editor is ready to enable undo across tab switches.
    /// </summary>
    public void SetSharedDocument(TextDocument sharedDocument)
    {
        if (_textEditor == null || sharedDocument == null) return;

        // Apply the shared document directly to the inner TextEditor
        _textEditor.Document = sharedDocument;

        // Reinstall folding for the new document
        if (_foldingManager != null)
        {
            try { FoldingManager.Uninstall(_foldingManager); }
            catch (Exception) { /* Ignore */ }
        }
        _foldingManager = FoldingManager.Install(_textEditor.TextArea);

        // Update text marker service
        if (_textMarkerService != null)
        {
            try
            {
                _textEditor.TextArea.TextView.LineTransformers.Remove(_textMarkerService);
                _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_textMarkerService);
            }
            catch (Exception) { /* Ignore */ }
        }
        _textMarkerService = new TextMarkerService(_textEditor.Document);
        _textEditor.TextArea.TextView.LineTransformers.Add(_textMarkerService);
        _textEditor.TextArea.TextView.BackgroundRenderers.Add(_textMarkerService);

        _hasLoadedInitialText = true;

        // Force visual refresh
        _textEditor.TextArea.TextView.Redraw();
        UpdateFoldings();
    }

    private void OnTextAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Check if click is in the fold margin area (left side before line numbers)
        if (_textEditor == null) return;

        try
        {
            var point = e.GetCurrentPoint(_textEditor.TextArea);

            // Handle Ctrl+Click for document links and Go to Definition
            if (point.Properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                var textView = _textEditor.TextArea.TextView;
                var pos = textView.GetPositionFloor(point.Position + textView.ScrollOffset);
                if (pos.HasValue)
                {
                    // Check if click is on a document link first
                    var link = GetDocumentLinkAt(pos.Value.Line, pos.Value.Column);
                    if (link != null)
                    {
                        DocumentLinkClicked?.Invoke(this, new DocumentLinkClickedEventArgs(link.Target, link.Tooltip));
                        e.Handled = true;
                        return;
                    }

                    // Move caret to clicked position first
                    _textEditor.TextArea.Caret.Position = pos.Value;
                    // Trigger go to definition
                    GoToDefinitionRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    return;
                }
            }

            if (point.Properties.IsLeftButtonPressed && !_isUpdatingFoldings && !_isFoldingInProgress)
            {
                // The fold margin is typically the first ~12 pixels on the left
                // Line number margin comes after that
                var textView = _textEditor.TextArea.TextView;
                var leftMarginWidth = GetLeftMarginWidth();

                if (point.Position.X < leftMarginWidth)
                {
                    // Click is in margin area - check for fold toggle
                    var visualLine = textView.GetVisualLineFromVisualTop(point.Position.Y + textView.ScrollOffset.Y);
                    if (visualLine != null && _foldingManager != null)
                    {
                        var lineStartOffset = visualLine.FirstDocumentLine.Offset;
                        var lineEndOffset = visualLine.FirstDocumentLine.EndOffset;

                        // Validate offsets
                        if (lineStartOffset >= 0 && lineEndOffset <= _textEditor.Document.TextLength)
                        {
                            var allFoldings = _foldingManager.AllFoldings?.ToList();
                            var folding = _foldingManager.GetFoldingsContaining(lineStartOffset).FirstOrDefault()
                                       ?? allFoldings?.FirstOrDefault(f =>
                                           f.StartOffset >= lineStartOffset && f.StartOffset <= lineEndOffset);

                            if (folding != null)
                            {
                                folding.IsFolded = !folding.IsFolded;
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    try { textView?.Redraw(); }
                                    catch (Exception) { /* Ignore */ }
                                });
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in text area pointer pressed: {ex.Message}");
        }

        _multiCursorInputHandler?.HandlePointerPressed(e);
    }

    /// <summary>
    /// After a mouse selection completes, convert to rectangular selection if column mode is active.
    /// </summary>
    private void OnTextAreaPointerReleasedForColumnSelection(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isColumnSelectionMode || _textEditor?.TextArea == null) return;
        ConvertToRectangularSelectionIfNeeded();
    }

    /// <summary>
    /// After a keyboard selection (Shift+Arrow), convert to rectangular selection if column mode is active.
    /// </summary>
    private void OnTextAreaKeyUpForColumnSelection(object? sender, KeyEventArgs e)
    {
        if (!_isColumnSelectionMode || _textEditor?.TextArea == null) return;

        // Only act on shift+arrow key combinations that produce selections
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            switch (e.Key)
            {
                case Key.Up:
                case Key.Down:
                case Key.Left:
                case Key.Right:
                case Key.Home:
                case Key.End:
                case Key.PageUp:
                case Key.PageDown:
                    ConvertToRectangularSelectionIfNeeded();
                    break;
            }
        }
    }

    /// <summary>
    /// Converts the current normal selection to a rectangular selection if column mode is active.
    /// </summary>
    private void ConvertToRectangularSelectionIfNeeded()
    {
        if (_textEditor?.TextArea == null) return;
        var selection = _textEditor.TextArea.Selection;
        if (selection.IsEmpty || selection is RectangleSelection) return;

        try
        {
            var start = selection.StartPosition;
            var end = selection.EndPosition;
            _textEditor.TextArea.Selection = new RectangleSelection(_textEditor.TextArea, start, end);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error converting to rectangular selection: {ex.Message}");
        }
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
        if (_textEditor == null || _foldingManager == null || _isUpdatingFoldings || _isFoldingInProgress) return;

        try
        {
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

                    // Validate offsets
                    if (lineStartOffset >= 0 && lineEndOffset <= _textEditor.Document.TextLength)
                    {
                        var allFoldings = _foldingManager.AllFoldings?.ToList();
                        var folding = _foldingManager.GetFoldingsContaining(lineStartOffset).FirstOrDefault()
                                   ?? allFoldings?.FirstOrDefault(f =>
                                       f.StartOffset >= lineStartOffset && f.StartOffset <= lineEndOffset);

                        if (folding != null)
                        {
                            folding.IsFolded = !folding.IsFolded;
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                try { textView?.Redraw(); }
                                catch (Exception) { /* Ignore */ }
                            });
                            e.Handled = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in editor pointer pressed: {ex.Message}");
        }
    }

    private bool _isFoldingInProgress;

    private void OnFoldingMarginPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Prevent re-entrancy or clicks during folding update
        if (_isFoldingInProgress || _isUpdatingFoldings) return;

        try
        {
            _isFoldingInProgress = true;

            if (_foldingManager == null || _textEditor == null || sender == null) return;

            var point = e.GetCurrentPoint((Avalonia.Visual)sender);
            if (!point.Properties.IsLeftButtonPressed) return;

            // Get the visual line at the click position
            var textView = _textEditor.TextArea?.TextView;
            if (textView == null || !textView.VisualLinesValid) return;

            var visualLine = textView.GetVisualLineFromVisualTop(point.Position.Y + textView.ScrollOffset.Y);

            if (visualLine?.FirstDocumentLine != null)
            {
                var lineStartOffset = visualLine.FirstDocumentLine.Offset;
                var lineEndOffset = visualLine.FirstDocumentLine.EndOffset;

                // Validate offsets
                if (lineStartOffset < 0 || lineEndOffset > _textEditor.Document.TextLength) return;

                // Find folding at this line - use ToList() to avoid collection modification issues
                var allFoldings = _foldingManager.AllFoldings?.ToList();
                if (allFoldings == null) return;

                var folding = _foldingManager.GetFoldingsContaining(lineStartOffset).FirstOrDefault()
                           ?? allFoldings.FirstOrDefault(f =>
                               f.StartOffset >= lineStartOffset && f.StartOffset <= lineEndOffset);

                if (folding != null)
                {
                    folding.IsFolded = !folding.IsFolded;
                    // Use dispatcher to avoid issues during event handling
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        try { textView?.Redraw(); }
                        catch (Exception) { /* Ignore redraw errors */ }
                    });
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in folding margin click: {ex.Message}");
        }
        finally
        {
            _isFoldingInProgress = false;
        }
    }

    private void OnTextAreaKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Tab for snippet expansion: if no completion window is open and current word
        // matches a snippet prefix exactly, expand the snippet instead of inserting a tab
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && !IsCompletionWindowOpen)
        {
            if (TryExpandSnippet())
            {
                e.Handled = true;
                return;
            }
        }

        // Handle Ctrl+F to open inline Find bar
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            ShowInlineFind();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+H to open inline Find & Replace bar
        if (e.Key == Key.H && e.KeyModifiers == KeyModifiers.Control)
        {
            ShowInlineFindReplace();
            e.Handled = true;
            return;
        }

        // Handle Escape to close inline Find bar
        if (e.Key == Key.Escape && _inlineFindReplace?.IsVisible == true)
        {
            _inlineFindReplace.CloseFindBar();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Space for code completion
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.Control)
        {
            TriggerCompletion();
            e.Handled = true;
            return;
        }

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

        // Handle F12 to go to definition
        if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None)
        {
            GoToDefinitionRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle Alt+F12 for peek definition
        if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.Alt)
        {
            PeekDefinitionRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle Shift+F12 to find all references
        if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.Shift)
        {
            FindAllReferencesRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle F2 to rename symbol
        if (e.Key == Key.F2 && e.KeyModifiers == KeyModifiers.None)
        {
            RenameSymbolRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle Ctrl+. for code actions (quick fixes)
        if (e.Key == Key.OemPeriod && e.KeyModifiers == KeyModifiers.Control)
        {
            CodeActionsRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+F for format document
        if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            FormatDocumentRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle Alt+Z to toggle word wrap
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Alt)
        {
            WordWrap = !WordWrap;
            e.Handled = true;
            return;
        }

        // Handle Ctrl+G for Go to Line
        if (e.Key == Key.G && e.KeyModifiers == KeyModifiers.Control)
        {
            GoToLineRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        // Handle Ctrl+/ to toggle line comment
        if (e.Key == Key.Oem2 && e.KeyModifiers == KeyModifiers.Control)
        {
            ToggleLineComment();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Enter to insert line below
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            InsertLineBelow();
            e.Handled = true;
            return;
        }

        // Handle Ctrl+Shift+Enter to insert line above
        if (e.Key == Key.Enter && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
        {
            InsertLineAbove();
            e.Handled = true;
            return;
        }

        // Handle Enter to auto-insert End blocks (Sub/End Sub, etc.)
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            // Schedule after the default Enter handling (which inserts newline and applies indentation)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TryAutoInsertEndBlock();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        // Handle Backspace to delete empty auto-close pairs
        if (e.Key == Key.Back && e.KeyModifiers == KeyModifiers.None && _autoCloseBrackets)
        {
            if (DeleteEmptyPair())
            {
                e.Handled = true;
                return;
            }
        }

        // Handle Backspace dismissing completion window when prefix is fully deleted
        if (e.Key == Key.Back && _completionWindow != null)
        {
            // Schedule check after the backspace is processed
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_completionWindow == null) return;
                var prefix = GetCurrentWordPrefix();
                if (string.IsNullOrEmpty(prefix))
                {
                    _completionWindow.Close();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        _multiCursorInputHandler?.HandleKeyDown(e);
    }

    private void OnTextAreaTextEntering(object? sender, TextInputEventArgs e)
    {
        // Surround selected text with matching pairs (VS Code-style surrounding pairs)
        if (!string.IsNullOrEmpty(e.Text) && e.Text.Length == 1
            && _textEditor?.TextArea?.Selection != null
            && !_textEditor.TextArea.Selection.IsEmpty
            && SurroundingPairs.TryGetValue(e.Text[0], out var closingPair))
        {
            var selection = _textEditor.TextArea.Selection;
            var segment = selection.SurroundingSegment;
            var selectedText = _textEditor.SelectedText;
            var document = _textEditor.Document;

            // Wrap selection: replace "text" with "(text)" etc.
            var wrapped = e.Text[0] + selectedText + closingPair;
            document.BeginUpdate();
            try
            {
                document.Replace(segment.Offset, segment.Length, wrapped);
                // Select the inner text (between the pair chars)
                _textEditor.Select(segment.Offset + 1, selectedText.Length);
            }
            finally
            {
                document.EndUpdate();
            }
            e.Handled = true;
            return;
        }

        // Skip-over closing brackets if the next char is already that bracket
        if (_autoCloseBrackets && !string.IsNullOrEmpty(e.Text) && e.Text.Length == 1
            && ClosingBrackets.Contains(e.Text[0]) && _textEditor?.Document != null)
        {
            var offset = _textEditor.CaretOffset;
            if (offset < _textEditor.Document.TextLength
                && _textEditor.Document.GetCharAt(offset) == e.Text[0])
            {
                // Skip over the existing closing bracket
                _textEditor.CaretOffset = offset + 1;
                e.Handled = true;
                return;
            }
        }

        // Skip-over closing quote if next char matches
        if (_autoCloseBrackets && !string.IsNullOrEmpty(e.Text) && e.Text.Length == 1
            && (e.Text[0] == '"') && _textEditor?.Document != null)
        {
            var offset = _textEditor.CaretOffset;
            if (offset < _textEditor.Document.TextLength
                && _textEditor.Document.GetCharAt(offset) == e.Text[0])
            {
                _textEditor.CaretOffset = offset + 1;
                e.Handled = true;
                return;
            }
        }

        _multiCursorInputHandler?.HandleTextInput(e);

        // Auto-trigger completion when typing a dot (for member access)
        if (e.Text == ".")
        {
            // Schedule completion trigger after the dot is inserted
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TriggerCompletion();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
        // Auto-trigger signature help when typing ( or ,
        else if (e.Text == "(" || e.Text == ",")
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                TriggerSignatureHelp();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
        // Dismiss signature help when typing )
        else if (e.Text == ")")
        {
            DismissSignatureHelp();
        }
        // Auto-trigger completion after typing 2+ identifier characters
        else if (!string.IsNullOrEmpty(e.Text) && e.Text.Length == 1 && char.IsLetterOrDigit(e.Text[0]))
        {
            // Schedule check after the character is inserted
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var prefix = GetCurrentWordPrefix();
                if (prefix != null && prefix.Length >= 2)
                {
                    TriggerCompletion();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Get the current word being typed (prefix before cursor)
    /// </summary>
    private string? GetCurrentWordPrefix()
    {
        if (_textEditor?.Document == null) return null;

        var offset = _textEditor.CaretOffset;
        if (offset <= 0) return null;

        var document = _textEditor.Document;
        int start = offset;

        // Walk backwards to find the start of the word
        while (start > 0)
        {
            char c = document.GetCharAt(start - 1);
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                start--;
            }
            else
            {
                break;
            }
        }

        if (start == offset) return null;

        return document.GetText(start, offset - start);
    }

    private void OnMultiCursorsChanged(object? sender, EventArgs e)
    {
        _textEditor?.TextArea.TextView.Redraw();
    }

    private void ConfigureEditor()
    {
        // Apply theme-aware colors
        ApplyEditorThemeColors();

        // Subscribe to theme changes
        EditorTheme.ThemeChanged += OnEditorThemeChanged;

        // Configure options
        _textEditor.Options.EnableRectangularSelection = true;
        _textEditor.Options.EnableHyperlinks = true;
        _textEditor.Options.EnableEmailHyperlinks = false;
        _textEditor.Options.EnableTextDragDrop = true;
        _textEditor.Options.ShowTabs = false;
        _textEditor.Options.ShowSpaces = false;
        _textEditor.Options.ShowEndOfLine = false;
        _textEditor.Options.HighlightCurrentLine = true;
        _textEditor.Options.IndentationSize = 4;
        _textEditor.Options.ConvertTabsToSpaces = true;

        // Smart indentation for BasicLang (auto-indent after Sub, Function, If, etc.)
        _indentationStrategy = new BasicLangIndentationStrategy
        {
            IndentSize = _textEditor.Options.IndentationSize,
            UseTabs = !_textEditor.Options.ConvertTabsToSpaces
        };
        _textEditor.TextArea.IndentationStrategy = _indentationStrategy;

        // Apply initial font ligature setting
        ApplyFontLigatures(EnableFontLigatures);
    }

    /// <summary>
    /// Applies or removes font ligature support on the text editor.
    /// Avalonia 11 enables ligatures by default in its text rendering pipeline.
    /// To disable ligatures, we switch to a "noligs" font family variant string
    /// that tells the shaper to suppress the 'liga' and 'clig' features.
    /// AvaloniaEdit does not expose a direct ligature toggle, so we control this
    /// by setting a custom FontFeatures string on the TextArea's TextView.
    /// </summary>
    private void ApplyFontLigatures(bool enable)
    {
        if (_textEditor?.TextArea?.TextView == null)
            return;

        try
        {
            // In Avalonia 11, ligatures are on by default when the font supports them.
            // We re-apply the font family to force the text shaper to pick up the change.
            // The actual ligature control is done via the font family string:
            // Appending "#NoLig" does not work in Avalonia, so we use a different approach.
            //
            // AvaloniaEdit's TextView inherits from Avalonia.Controls.Control which has
            // the FontFeatures attached property in Avalonia 11.2+. For 11.1 we store
            // the setting and re-apply the font family which triggers a re-render.
            // Ligatures are rendered by the HarfBuzz shaper when the font supports them.
            //
            // For Avalonia 11.1: Ligatures are always rendered if the font has them.
            // We store the preference so that when Avalonia is updated to 11.2+ where
            // FontFeatures is available, we can apply "liga=0,clig=0" to disable them.

            _fontLigaturesEnabled = enable;

            // Force a re-render of all text by resetting the font family
            var currentFamily = _textEditor.FontFamily?.Name ?? EditorFontFamily;
            _textEditor.FontFamily = new FontFamily(currentFamily);
            _textEditor.TextArea.TextView.Redraw();
        }
        catch
        {
            // Font ligature application is non-critical
        }
    }

    private bool _fontLigaturesEnabled;

    /// <summary>
    /// Applies theme colors to the editor surface. Called on init and when the theme changes.
    /// </summary>
    private void ApplyEditorThemeColors()
    {
        _textEditor.Background = new SolidColorBrush(EditorTheme.Background);
        _textEditor.Foreground = new SolidColorBrush(EditorTheme.Foreground);

        // Line number styling
        _textEditor.LineNumbersForeground = new SolidColorBrush(EditorTheme.LineNumbersForeground);

        // Current line highlighting
        _textEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(EditorTheme.CurrentLineBackground);
        _textEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(EditorTheme.CurrentLineBackground));

        // Selection colors
        _textEditor.TextArea.SelectionBrush = new SolidColorBrush(EditorTheme.SelectionBackground);
        _textEditor.TextArea.SelectionForeground = null; // Keep text color

        // Refresh the text view
        _textEditor.TextArea.TextView.Redraw();
    }

    private void OnEditorThemeChanged(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ApplyEditorThemeColors();

            // Update custom line number margin colors
            _currentLineNumberMargin?.UpdateThemeColors();

            // Re-apply syntax highlighting with theme-appropriate colors
            // Try file-specific highlighting first, then fall back to BasicLang
            if (!string.IsNullOrEmpty(_documentFilePath))
            {
                SetHighlightingForFile(_documentFilePath);
            }
            else
            {
                var highlighting = HighlightingManager.Instance.GetDefinition("BasicLang");
                if (highlighting != null)
                {
                    _textEditor.SyntaxHighlighting = highlighting;
                }
            }
        });
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        // Update Text property but prevent feedback loop that clears undo
        _isUpdatingTextFromEditor = true;
        try
        {
            SetCurrentValue(TextProperty, _textEditor.Text);
        }
        finally
        {
            _isUpdatingTextFromEditor = false;
        }

        TextChanged?.Invoke(this, EventArgs.Empty);

        // Rebuild bracket pair colorization depth map
        _bracketPairColorizer.Invalidate(_textEditor.Document);

        // Restart folding update timer
        _foldingUpdateTimer?.Stop();
        _foldingUpdateTimer?.Start();

        // Request debounced semantic token refresh
        RequestSemanticTokenRefresh();
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

    private void OnCaretPositionChangedForLineNumbers(object? sender, EventArgs e)
    {
        _currentLineNumberMargin?.InvalidateLineNumbers();
    }

    private void OnSelectionChangedForOccurrences(object? sender, EventArgs e)
    {
        if (_selectionOccurrenceHighlighter != null)
        {
            _selectionOccurrenceHighlighter.UpdateSelection();
            _textEditor?.TextArea?.TextView?.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        }
    }

    /// <summary>
    /// Replaces the built-in line number margin with a custom one that highlights the current line number.
    /// </summary>
    private void SetupCurrentLineNumberMargin()
    {
        if (_textEditor?.TextArea == null) return;

        // Remove the built-in LineNumberMargin
        var margins = _textEditor.TextArea.LeftMargins;
        for (int i = margins.Count - 1; i >= 0; i--)
        {
            if (margins[i].GetType().Name == "LineNumberMargin")
            {
                margins.RemoveAt(i);
            }
        }

        // Add our custom margin that highlights the current line number
        _currentLineNumberMargin = new CurrentLineNumberMargin(_textEditor)
        {
            FontFamily = _textEditor.FontFamily,
            FontSize = _textEditor.FontSize - 1, // slightly smaller
        };
        _currentLineNumberMargin.UpdateThemeColors();

        // Insert after diagnostic margin (index 0) so it appears where line numbers normally go
        var insertIndex = 0;
        for (int i = 0; i < margins.Count; i++)
        {
            if (margins[i] is DiagnosticMargin)
            {
                insertIndex = i + 1;
                break;
            }
        }
        margins.Insert(insertIndex, _currentLineNumberMargin);
    }

    private void UpdateFoldings()
    {
        // If LSP is available, request folding ranges asynchronously
        if (!_isUpdatingFoldings && _isFoldingEnabled
            && _foldingManager != null && _textEditor?.Document != null && _textEditor?.TextArea != null
            && _languageService != null && _languageService.IsConnected && !string.IsNullOrEmpty(_documentFilePath))
        {
            _ = UpdateFoldingsFromLspAsync();
            return;
        }
        // Fall back to local regex-based folding strategy
        ApplyFoldings(null);
    }

    private async Task UpdateFoldingsFromLspAsync()
    {
        if (_isUpdatingFoldings || _languageService == null || _documentFilePath == null) return;
        try
        {
            var ranges = await _languageService.GetFoldingRangesAsync(_documentFilePath);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ApplyFoldings(ranges != null && ranges.Count > 0 ? ranges : null);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LSP folding error: {ex.Message}");
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyFoldings(null));
        }
    }

    private void ApplyFoldings(IReadOnlyList<FoldingRangeInfo>? lspRanges)
    {
        // Prevent re-entrancy and skip if folding disabled
        if (_isUpdatingFoldings || !_isFoldingEnabled) return;
        if (_foldingManager == null || _textEditor?.Document == null || _textEditor?.TextArea == null) return;

        _isUpdatingFoldings = true;
        try
        {
            // Store which lines were folded before update
            var foldedLines = new HashSet<int>();
            try
            {
                foreach (var folding in _foldingManager.AllFoldings)
                {
                    if (folding.IsFolded)
                    {
                        // Get line number from offset - may throw if offset is now invalid
                        try
                        {
                            var line = _textEditor.Document.GetLineByOffset(
                                Math.Min(folding.StartOffset, _textEditor.Document.TextLength));
                            foldedLines.Add(line.LineNumber);
                        }
                        catch (Exception) { /* Offset invalid, skip */ }
                    }
                }
            }
            catch (Exception) { /* Collection may have been modified, skip preservation */ }

            // Completely reinstall folding manager to avoid stale state
            try
            {
                FoldingManager.Uninstall(_foldingManager);
            }
            catch (Exception) { /* May already be invalid */ }

            _foldingManager = FoldingManager.Install(_textEditor.TextArea);

            // Apply new foldings - prefer LSP ranges, fall back to local strategy
            if (lspRanges != null && lspRanges.Count > 0)
            {
                var newFoldings = new List<AvaloniaEdit.Folding.NewFolding>();
                var doc = _textEditor.Document;

                foreach (var range in lspRanges)
                {
                    if (range.StartLine < 1 || range.EndLine < 1
                        || range.StartLine > doc.LineCount || range.EndLine > doc.LineCount)
                        continue;

                    var startLine = doc.GetLineByNumber(range.StartLine);
                    var endLine = doc.GetLineByNumber(range.EndLine);
                    var startOffset = startLine.Offset;
                    var endOffset = endLine.EndOffset;

                    if (endOffset > startOffset)
                    {
                        var lineText = doc.GetText(startLine.Offset, startLine.Length).Trim();
                        var name = lineText.Length > 50 ? lineText.Substring(0, 50) + "..." : lineText;

                        newFoldings.Add(new AvaloniaEdit.Folding.NewFolding(startOffset, endOffset)
                        {
                            Name = name,
                            DefaultClosed = false
                        });
                    }
                }

                newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
                _foldingManager.UpdateFoldings(newFoldings, -1);
            }
            else
            {
                _foldingStrategy.UpdateFoldings(_foldingManager, _textEditor.Document);
            }

            // Re-attach fold margin click handlers
            foreach (var margin in _textEditor.TextArea.LeftMargins)
            {
                if (margin is FoldingMargin foldingMargin)
                {
                    // Remove any existing handlers first to avoid duplicates
                    foldingMargin.PointerPressed -= OnFoldingMarginPointerPressed;
                    foldingMargin.PointerPressed += OnFoldingMarginPointerPressed;
                }
            }

            // Restore folded state for lines that were previously folded
            if (foldedLines.Count > 0)
            {
                foreach (var folding in _foldingManager.AllFoldings)
                {
                    try
                    {
                        var line = _textEditor.Document.GetLineByOffset(
                            Math.Min(folding.StartOffset, _textEditor.Document.TextLength));
                        if (foldedLines.Contains(line.LineNumber))
                        {
                            folding.IsFolded = true;
                        }
                    }
                    catch (Exception) { /* Ignore invalid folding */ }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error updating foldings: {ex.Message}");
            // If update fails, try to reinstall a fresh folding manager
            try
            {
                if (_foldingManager != null)
                {
                    try { FoldingManager.Uninstall(_foldingManager); }
                    catch (Exception) { /* Already uninstalled or invalid */ }
                }
                _foldingManager = FoldingManager.Install(_textEditor!.TextArea);
            }
            catch (Exception) { /* Give up on folding for now */ }
        }
        finally
        {
            _isUpdatingFoldings = false;
        }
    }

    /// <summary>
    /// Toggles the folding at the current caret position
    /// </summary>
    public void ToggleFoldAtCaret()
    {
        if (_foldingManager == null || _textEditor == null || _isUpdatingFoldings) return;

        try
        {
            var offset = _textEditor.CaretOffset;
            if (offset < 0 || offset > _textEditor.Document.TextLength) return;

            var folding = _foldingManager.GetFoldingsContaining(offset).FirstOrDefault();

            if (folding != null)
            {
                folding.IsFolded = !folding.IsFolded;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try { _textEditor?.TextArea?.TextView?.Redraw(); }
                    catch (Exception) { /* Ignore redraw errors */ }
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error toggling fold: {ex.Message}");
        }
    }

    /// <summary>
    /// Folds all foldable regions
    /// </summary>
    public void FoldAll()
    {
        if (_foldingManager == null || _isUpdatingFoldings) return;
        try
        {
            foreach (var folding in _foldingManager.AllFoldings.ToList())
            {
                try { folding.IsFolded = true; }
                catch (Exception) { /* Ignore invalid folding */ }
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { _textEditor?.TextArea?.TextView?.Redraw(); }
                catch (Exception) { /* Ignore redraw errors */ }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error folding all: {ex.Message}");
        }
    }

    /// <summary>
    /// Unfolds all folded regions
    /// </summary>
    public void UnfoldAll()
    {
        if (_foldingManager == null || _isUpdatingFoldings) return;
        try
        {
            foreach (var folding in _foldingManager.AllFoldings.ToList())
            {
                try { folding.IsFolded = false; }
                catch (Exception) { /* Ignore invalid folding */ }
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { _textEditor?.TextArea?.TextView?.Redraw(); }
                catch (Exception) { /* Ignore redraw errors */ }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error unfolding all: {ex.Message}");
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Skip if not initialized yet - OnInitialized will apply pending values
        if (!_isInitialized || _textEditor == null) return;

        if (change.Property == TextProperty)
        {
            // Skip if this change came from the editor itself (prevents feedback loop)
            if (_isUpdatingTextFromEditor) return;

            // Skip if we're using Document binding - Document is the source of truth
            // and handles its own undo stack. Setting Text would corrupt the undo history.
            if (Document != null) return;

            var newText = change.GetNewValue<string>() ?? "";

            // Only update document if text actually differs
            if (_textEditor.Document.Text != newText)
            {
                var isInitialLoad = !_hasLoadedInitialText;

                // Use BeginUpdate/EndUpdate to group as a single undo operation
                _textEditor.Document.BeginUpdate();
                try
                {
                    _textEditor.Document.Text = newText;
                }
                finally
                {
                    _textEditor.Document.EndUpdate();
                }
                _hasLoadedInitialText = true;

                // Only clear undo on initial file load
                if (isInitialLoad && !string.IsNullOrEmpty(newText))
                {
                    _textEditor.Document.UndoStack.ClearAll();
                }
                UpdateFoldings();
            }
        }
        else if (change.Property == IsReadOnlyProperty)
        {
            _textEditor.IsReadOnly = change.GetNewValue<bool>();
        }
        else if (change.Property == ShowLineNumbersProperty)
        {
            // Toggle our custom line number margin visibility instead of the built-in one
            if (_currentLineNumberMargin != null)
            {
                _currentLineNumberMargin.IsVisible = change.GetNewValue<bool>();
            }
            else
            {
                _textEditor.ShowLineNumbers = change.GetNewValue<bool>();
            }
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
        else if (change.Property == EnableFontLigaturesProperty)
        {
            ApplyFontLigatures(change.GetNewValue<bool>());
        }
        else if (change.Property == DocumentProperty)
        {
            var newDoc = change.GetNewValue<TextDocument?>();
            if (newDoc != null)
            {
                ApplyDocument(newDoc);
                _hasLoadedInitialText = true;  // Document has undo history, don't overwrite
            }
        }
        else if (change.Property == StickyScrollEnabledProperty)
        {
            if (_stickyScroll != null)
            {
                _stickyScroll.IsEnabled2 = change.GetNewValue<bool>();
            }
        }
        else if (change.Property == SmoothScrollingProperty)
        {
            _smoothScrollingEnabled = change.GetNewValue<bool>();
            if (!_smoothScrollingEnabled)
            {
                // Stop any in-progress animation
                _smoothScrollTimer?.Stop();
                _isSmoothScrolling = false;
            }
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

    /// <summary>
    /// Opens the inline find bar (Ctrl+F).
    /// </summary>
    public void ShowInlineFind()
    {
        _inlineFindReplace?.OpenFind();
    }

    /// <summary>
    /// Opens the inline find and replace bar (Ctrl+H).
    /// </summary>
    public void ShowInlineFindReplace()
    {
        _inlineFindReplace?.OpenFindReplace();
    }

    /// <summary>
    /// Closes the inline find/replace bar if open.
    /// </summary>
    public void HideInlineFind()
    {
        _inlineFindReplace?.CloseFindBar();
    }

    /// <summary>
    /// Gets whether the inline find/replace bar is currently visible.
    /// </summary>
    public bool IsInlineFindVisible => _inlineFindReplace?.IsVisible == true;

    public void SelectAll()
    {
        _textEditor?.SelectAll();
    }

    /// <summary>
    /// Sets the selection to a specific range (1-based line/column)
    /// </summary>
    public void SetSelection(int startLine, int startColumn, int endLine, int endColumn)
    {
        if (_textEditor?.Document == null) return;

        var doc = _textEditor.Document;
        var start = doc.GetLineByNumber(Math.Clamp(startLine, 1, doc.LineCount));
        var end = doc.GetLineByNumber(Math.Clamp(endLine, 1, doc.LineCount));

        var startOffset = start.Offset + Math.Min(startColumn - 1, start.Length);
        var endOffset = end.Offset + Math.Min(endColumn - 1, end.Length);

        _textEditor.Select(startOffset, endOffset - startOffset);
        _textEditor.ScrollToLine(startLine);
    }

    /// <summary>
    /// Sets the selection by offset and length, and scrolls to it.
    /// </summary>
    public void SetSelection(int offset, int length)
    {
        if (_textEditor?.Document == null) return;
        offset = Math.Clamp(offset, 0, _textEditor.Document.TextLength);
        length = Math.Clamp(length, 0, _textEditor.Document.TextLength - offset);
        _textEditor.Select(offset, length);
        var loc = _textEditor.Document.GetLocation(offset);
        _textEditor.ScrollToLine(loc.Line);
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
    /// Toggles whitespace rendering (spaces, tabs, end of line markers)
    /// </summary>
    /// <returns>True if whitespace is now visible, false if hidden</returns>
    public bool ToggleWhitespace()
    {
        if (_textEditor == null) return false;

        var newState = !_textEditor.Options.ShowSpaces;
        _textEditor.Options.ShowSpaces = newState;
        _textEditor.Options.ShowTabs = newState;
        _textEditor.Options.ShowEndOfLine = newState;
        return newState;
    }

    /// <summary>
    /// Sets whitespace rendering to a specific state
    /// </summary>
    public void SetWhitespaceVisible(bool visible)
    {
        if (_textEditor == null) return;

        _textEditor.Options.ShowSpaces = visible;
        _textEditor.Options.ShowTabs = visible;
        _textEditor.Options.ShowEndOfLine = visible;
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
        HideErrorTooltip();

        var point = e.GetPosition(_textEditor.TextArea.TextView);
        _lastHoverPosition = point;

        // Get the offset under the mouse
        var offset = GetOffsetFromPoint(point);
        _lastHoverOffset = offset;

        if (offset >= 0)
        {
            // Check for error markers first
            var markers = _textMarkerService?.GetMarkersAtOffset(offset);
            if (markers != null && markers.Any())
            {
                _hoverTimer?.Start();
                return;
            }

            // Otherwise check for word hover (for data tips)
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
        HideErrorTooltip();
    }

    private void OnHoverTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // Check for error markers first
            if (_lastHoverOffset >= 0 && _textMarkerService != null)
            {
                var markers = _textMarkerService.GetMarkersAtOffset(_lastHoverOffset);
                var markerWithMessage = markers.FirstOrDefault(m => !string.IsNullOrEmpty(m.Message));
                if (markerWithMessage != null)
                {
                    ShowErrorTooltip(markerWithMessage.Message!, _lastHoverPosition);
                    return;
                }
            }

            // Otherwise show data tip for word hover
            if (!string.IsNullOrWhiteSpace(_lastHoverWord) && _textEditor?.TextArea?.TextView != null)
            {
                // Get screen position for the tooltip
                var textView = _textEditor.TextArea.TextView;
                var screenPoint = textView.PointToScreen(_lastHoverPosition);

                // Get line/column from hover offset for LSP hover requests
                int hoverLine = 0, hoverColumn = 0;
                if (_lastHoverOffset >= 0 && _lastHoverOffset <= _textEditor.Document.TextLength)
                {
                    var location = _textEditor.Document.GetLocation(_lastHoverOffset);
                    hoverLine = location.Line;
                    hoverColumn = location.Column;
                }

                DataTipRequested?.Invoke(this, new DataTipRequestEventArgs(
                    _lastHoverWord,
                    screenPoint.X,
                    screenPoint.Y + 20, // Offset below cursor
                    hoverLine,
                    hoverColumn
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
    /// Shows an error tooltip at the specified position
    /// </summary>
    private void ShowErrorTooltip(string message, Point position)
    {
        if (_textEditor == null) return;

        // Create tooltip if not exists
        if (_errorTooltip == null)
        {
            _errorTooltipText = new TextBlock
            {
                Padding = new Thickness(8, 4),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 400
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D30")),
                BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = _errorTooltipText
            };

            _errorTooltip = new Avalonia.Controls.Primitives.Popup
            {
                Child = border,
                PlacementTarget = _textEditor.TextArea
            };
        }

        _errorTooltipText!.Text = message;
        _errorTooltipText.Foreground = new SolidColorBrush(Color.Parse("#F48771")); // Error color

        _errorTooltip.HorizontalOffset = position.X;
        _errorTooltip.VerticalOffset = position.Y + 20;
        _errorTooltip.IsOpen = true;
    }

    /// <summary>
    /// Shows a hover tooltip with LSP hover info at the current hover position
    /// </summary>
    public void ShowHoverTooltip(string contents)
    {
        if (_textEditor == null || string.IsNullOrWhiteSpace(contents)) return;

        // Reuse the error tooltip infrastructure but with different styling
        if (_errorTooltip == null)
        {
            _errorTooltipText = new TextBlock
            {
                Padding = new Thickness(8, 4),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                MaxWidth = 400
            };

            var border = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#2D2D30")),
                BorderBrush = new SolidColorBrush(Color.Parse("#3F3F46")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Child = _errorTooltipText
            };

            _errorTooltip = new Avalonia.Controls.Primitives.Popup
            {
                Child = border,
                PlacementTarget = _textEditor.TextArea
            };
        }

        _errorTooltipText!.Text = contents;
        _errorTooltipText.Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")); // Normal text color

        _errorTooltip.HorizontalOffset = _lastHoverPosition.X;
        _errorTooltip.VerticalOffset = _lastHoverPosition.Y + 20;
        _errorTooltip.IsOpen = true;
    }

    /// <summary>
    /// Hides the error tooltip
    /// </summary>
    private void HideErrorTooltip()
    {
        if (_errorTooltip != null)
        {
            _errorTooltip.IsOpen = false;
        }
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

    #region Code Completion

    /// <summary>
    /// Shows the code completion window with the given items
    /// </summary>
    public void ShowCompletion(IEnumerable<CompletionData> completionItems)
    {
        if (_textEditor == null) return;

        // Materialize the list first to check count
        var itemsList = completionItems.ToList();

        // Check for matching snippets before bailing out
        var snippetPrefix = GetCurrentWordPrefix() ?? "";
        var hasSnippets = !string.IsNullOrEmpty(snippetPrefix) && SnippetProvider.FindByPrefix(snippetPrefix).Any();

        // Don't do anything if no items and no snippets
        if (itemsList.Count == 0 && !hasSnippets) return;

        // Close any existing completion window
        _completionWindow?.Close();

        _completionWindow = new CompletionWindow(_textEditor.TextArea);

        // Calculate the start offset for text replacement
        // Find the start of the current word/identifier being typed
        var caretOffset = _textEditor.CaretOffset;
        var document = _textEditor.Document;
        var startOffset = caretOffset;

        // Move back to find the start of the identifier
        while (startOffset > 0)
        {
            var prevChar = document.GetCharAt(startOffset - 1);
            if (char.IsLetterOrDigit(prevChar) || prevChar == '_')
            {
                startOffset--;
            }
            else
            {
                break;
            }
        }

        // Set the start offset so the completion replaces the typed text
        _completionWindow.StartOffset = startOffset;

        var data = _completionWindow.CompletionList.CompletionData;

        // Add matching snippet completions first (they get a snippet icon)
        var currentPrefix = GetCurrentWordPrefix() ?? "";
        if (!string.IsNullOrEmpty(currentPrefix))
        {
            var matchingSnippets = SnippetProvider.FindByPrefix(currentPrefix);
            foreach (var snippet in matchingSnippets)
            {
                data.Add(new SnippetCompletionData(snippet));
            }
        }

        foreach (var item in itemsList)
        {
            data.Add(item);
        }

        try
        {
            // Set minimum size to ensure visibility
            _completionWindow.MinWidth = 200;
            _completionWindow.MinHeight = 100;
            _completionWindow.Width = 400;
            _completionWindow.MaxHeight = 300;

            // Get current word prefix for filtering
            var currentWord = GetCurrentWordPrefix() ?? "";

            _completionWindow.Show();

            // Defer selection to after the visual tree is built
            var wordToSelect = currentWord;
            var completionWindow = _completionWindow;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (completionWindow == null || !completionWindow.IsVisible) return;

                // Use SelectItem to filter and select best match
                if (!string.IsNullOrEmpty(wordToSelect))
                {
                    completionWindow.CompletionList.SelectItem(wordToSelect);
                }

                // Now try to access ListBox
                var listBox = completionWindow.CompletionList.ListBox;
                if (listBox != null)
                {
                    if (listBox.ItemCount > 0 && listBox.SelectedIndex < 0)
                    {
                        listBox.SelectedIndex = 0;
                    }
                }
                else
                {
                    // Try setting SelectedItem directly on CompletionList
                    var data = completionWindow.CompletionList.CompletionData;
                    if (data != null && data.Count > 0)
                    {
                        completionWindow.CompletionList.SelectedItem = data[0];
                    }
                }
            }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch (Exception)
        {
            // Silently ignore completion window errors
        }
        _completionWindow.Closed += (s, e) => _completionWindow = null;
    }

    /// <summary>
    /// Triggers completion request at the current caret position
    /// </summary>
    public void TriggerCompletion()
    {
        System.Diagnostics.Debug.WriteLine($"[Editor] TriggerCompletion called at line {CaretLine}, col {CaretColumn}");
        if (_textEditor == null) return;

        var args = new CompletionRequestEventArgs(
            CaretLine,
            CaretColumn,
            CaretOffset);

        CompletionRequested?.Invoke(this, args);
    }

    /// <summary>
    /// Returns true if a completion window is currently shown
    /// </summary>
    public bool IsCompletionWindowOpen => _completionWindow != null;

    /// <summary>
    /// Tries to expand a snippet at the current caret position.
    /// Looks at the word immediately before the caret and checks if it matches
    /// a snippet prefix exactly. If so, replaces the prefix with an AvaloniaEdit
    /// Snippet that supports Tab/Shift+Tab cycling between placeholder positions.
    /// </summary>
    private bool TryExpandSnippet()
    {
        if (_textEditor?.Document == null) return false;

        var prefix = GetCurrentWordPrefix();
        if (string.IsNullOrEmpty(prefix)) return false;

        var snippetDef = SnippetProvider.FindExactMatch(prefix);
        if (snippetDef == null) return false;

        var document = _textEditor.Document;
        var caretOffset = _textEditor.CaretOffset;
        var prefixStart = caretOffset - prefix.Length;

        // Get the indentation of the current line
        var line = document.GetLineByOffset(prefixStart);
        var lineTextBeforePrefix = document.GetText(line.Offset, prefixStart - line.Offset);
        var indent = "";
        foreach (var c in lineTextBeforePrefix)
        {
            if (c == ' ' || c == '\t') indent += c;
            else break;
        }

        // Remove the typed prefix text
        document.Remove(prefixStart, prefix.Length);
        _textEditor.CaretOffset = prefixStart;

        // Build and insert the AvaloniaEdit Snippet with interactive tab-stops.
        // Tab/Shift+Tab cycles between placeholders; Escape/Enter exits snippet mode.
        var snippet = snippetDef.BuildSnippet(indent);
        snippet.Insert(_textEditor.TextArea);

        return true;
    }

    #endregion

    #region Signature Help

    private Avalonia.Controls.Primitives.Popup? _signatureHelpPopup;
    private StackPanel? _signatureHelpPanel;
    private TextBlock? _signatureLabel;
    private TextBlock? _parameterLabel;
    private TextBlock? _signatureDocLabel;
    private int _signatureHelpActiveParam;

    /// <summary>
    /// Triggers signature help request at the current caret position
    /// </summary>
    public void TriggerSignatureHelp()
    {
        if (_textEditor == null) return;

        SignatureHelpRequested?.Invoke(this, new SignatureHelpRequestEventArgs(CaretLine, CaretColumn));
    }

    /// <summary>
    /// Shows signature help popup near the cursor
    /// </summary>
    public void ShowSignatureHelp(string signature, string? activeParameterName, string? documentation, int activeParameter, int signatureCount)
    {
        if (_textEditor == null) return;

        EnsureSignatureHelpPopup();

        if (_signatureLabel != null)
            _signatureLabel.Text = signature;
        if (_parameterLabel != null)
            _parameterLabel.Text = !string.IsNullOrEmpty(activeParameterName)
                ? $"Parameter: {activeParameterName}"
                : "";
        if (_signatureDocLabel != null)
        {
            _signatureDocLabel.Text = documentation ?? "";
            _signatureDocLabel.IsVisible = !string.IsNullOrEmpty(documentation);
        }

        _signatureHelpActiveParam = activeParameter;

        if (_signatureHelpPopup != null)
        {
            // Position above the caret using the caret rectangle
            var caretRect = _textEditor.TextArea.Caret.CalculateCaretRectangle();
            var screenPos = _textEditor.TextArea.TranslatePoint(
                new Point(caretRect.X, caretRect.Y), this);

            if (screenPos.HasValue)
            {
                _signatureHelpPopup.HorizontalOffset = screenPos.Value.X;
                _signatureHelpPopup.VerticalOffset = screenPos.Value.Y - 60;
            }

            _signatureHelpPopup.IsOpen = true;
        }
    }

    /// <summary>
    /// Dismisses the signature help popup
    /// </summary>
    public void DismissSignatureHelp()
    {
        if (_signatureHelpPopup != null)
            _signatureHelpPopup.IsOpen = false;
    }

    private void EnsureSignatureHelpPopup()
    {
        if (_signatureHelpPopup != null) return;

        _signatureLabel = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 12,
            Foreground = Brushes.White,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 500
        };

        _parameterLabel = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(86, 156, 214)),
            Margin = new Thickness(0, 2, 0, 0)
        };

        _signatureDocLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 500,
            Margin = new Thickness(0, 4, 0, 0),
            IsVisible = false
        };

        _signatureHelpPanel = new StackPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(37, 37, 38)),
            Children = { _signatureLabel, _parameterLabel, _signatureDocLabel },
            Margin = new Thickness(8, 4, 8, 4)
        };

        var border = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(69, 69, 69)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = _signatureHelpPanel
        };

        _signatureHelpPopup = new Avalonia.Controls.Primitives.Popup
        {
            PlacementTarget = _textEditor.TextArea,
            Child = border,
            IsLightDismissEnabled = true
        };

        // Add to visual tree
        if (this.Parent is Panel panel)
        {
            // Popup must be in the visual tree
        }
    }

    #endregion

    #region Document Highlight

    private List<TextMarkers.TextMarker>? _highlightMarkers;

    /// <summary>
    /// Shows document highlights (matching symbol occurrences)
    /// </summary>
    public void ShowDocumentHighlights(IEnumerable<(int startLine, int startCol, int endLine, int endCol, bool isWrite)> highlights)
    {
        ClearDocumentHighlights();
        if (_textMarkerService == null || _textEditor == null) return;

        _highlightMarkers = new List<TextMarkers.TextMarker>();

        foreach (var (startLine, startCol, endLine, endCol, isWrite) in highlights)
        {
            try
            {
                var line = _textEditor.Document.GetLineByNumber(
                    Math.Clamp(startLine, 1, _textEditor.Document.LineCount));
                var startOffset = line.Offset + Math.Min(startCol - 1, line.Length);

                var eLine = _textEditor.Document.GetLineByNumber(
                    Math.Clamp(endLine, 1, _textEditor.Document.LineCount));
                var endOffset = eLine.Offset + Math.Min(endCol - 1, eLine.Length);

                var length = Math.Max(1, endOffset - startOffset);

                var color = isWrite
                    ? Color.FromArgb(60, 255, 200, 100)  // Write: yellowish
                    : Color.FromArgb(40, 87, 166, 230);   // Read: bluish

                var marker = _textMarkerService.Create(startOffset, length, TextMarkers.TextMarkerType.Highlight);
                marker.BackgroundColor = color;
                _highlightMarkers.Add(marker);
            }
            catch { }
        }

        _textEditor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }

    /// <summary>
    /// Clears document highlight markers
    /// </summary>
    public void ClearDocumentHighlights()
    {
        if (_highlightMarkers != null && _textMarkerService != null)
        {
            foreach (var marker in _highlightMarkers)
            {
                _textMarkerService.Remove(marker);
            }
            _highlightMarkers.Clear();
        }
    }

    #endregion

    #region Inlay Hints

    private TextMarkers.InlayHintRenderer? _inlayHintRenderer;

    /// <summary>
    /// Shows inlay hints in the editor
    /// </summary>
    public void ShowInlayHints(IEnumerable<TextMarkers.InlayHintItem> hints)
    {
        if (_textEditor?.TextArea?.TextView == null) return;

        if (_inlayHintRenderer == null)
        {
            _inlayHintRenderer = new TextMarkers.InlayHintRenderer(_textEditor);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_inlayHintRenderer);
        }

        _inlayHintRenderer.SetHints(hints);
    }

    /// <summary>
    /// Clears all inlay hints
    /// </summary>
    public void ClearInlayHints()
    {
        _inlayHintRenderer?.Clear();
    }

    #endregion

    #region Execution Line Highlight

    private TextMarkers.ExecutionLineRenderer? _executionLineRenderer;

    #endregion

    #region Inline Debug Values

    private TextMarkers.InlineDebugValueRenderer? _inlineDebugValueRenderer;

    /// <summary>
    /// Shows inline debug variable values next to code lines during debugging.
    /// </summary>
    public void ShowInlineDebugValues(IEnumerable<TextMarkers.InlineDebugValue> values)
    {
        if (_textEditor?.TextArea?.TextView == null) return;

        if (_inlineDebugValueRenderer == null)
        {
            _inlineDebugValueRenderer = new TextMarkers.InlineDebugValueRenderer(_textEditor);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_inlineDebugValueRenderer);
        }

        _inlineDebugValueRenderer.SetValues(values);
    }

    /// <summary>
    /// Clears all inline debug values.
    /// </summary>
    public void ClearInlineDebugValues()
    {
        _inlineDebugValueRenderer?.Clear();
    }

    #endregion

    #region Inline Blame Annotations

    private TextMarkers.InlineBlameRenderer? _inlineBlameRenderer;

    /// <summary>
    /// Shows an inline git blame annotation after the end of the specified line.
    /// </summary>
    public void ShowInlineBlame(int lineNumber, string annotationText)
    {
        if (_textEditor?.TextArea?.TextView == null) return;

        if (_inlineBlameRenderer == null)
        {
            _inlineBlameRenderer = new TextMarkers.InlineBlameRenderer(_textEditor);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_inlineBlameRenderer);
        }

        _inlineBlameRenderer.SetAnnotation(lineNumber, annotationText);
    }

    /// <summary>
    /// Clears the inline blame annotation.
    /// </summary>
    public void ClearInlineBlame()
    {
        _inlineBlameRenderer?.Clear();
    }

    #endregion

    #region Code Lens

    private TextMarkers.CodeLensRenderer? _codeLensRenderer;

    /// <summary>
    /// Fired when a code lens item is clicked.
    /// </summary>
    public event EventHandler<TextMarkers.CodeLensClickedEventArgs>? CodeLensClicked;

    /// <summary>
    /// Shows code lens annotations above function/class lines.
    /// </summary>
    public void ShowCodeLenses(IEnumerable<TextMarkers.CodeLensItem> lenses)
    {
        if (_textEditor?.TextArea?.TextView == null) return;

        if (_codeLensRenderer == null)
        {
            _codeLensRenderer = new TextMarkers.CodeLensRenderer(_textEditor);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_codeLensRenderer);
            _codeLensRenderer.CodeLensClicked += (s, e) => CodeLensClicked?.Invoke(this, e);
        }

        _codeLensRenderer.SetLenses(lenses);
    }

    /// <summary>
    /// Clears all code lens annotations.
    /// </summary>
    public void ClearCodeLenses()
    {
        _codeLensRenderer?.Clear();
    }

    #endregion

    #region Inline Color Picker

    /// <summary>
    /// Handles a click on an inline color swatch: opens the color picker popup
    /// positioned near the swatch and pre-loaded with the current color values.
    /// </summary>
    private void OnColorSwatchClicked(object? sender, ColorSwatchClickedEventArgs e)
    {
        if (_textEditor?.TextArea?.TextView == null) return;

        // Create or reuse the picker control
        _colorPickerControl = new ColorPickerPopup(e.R, e.G, e.B, e.A);
        _colorPickerControl.Line = e.Line;
        _colorPickerControl.ColorTextStartOffset = e.ColorTextStartOffset;
        _colorPickerControl.ColorTextEndOffset = e.ColorTextEndOffset;
        _colorPickerControl.ColorPicked += OnColorPicked;
        _colorPickerControl.Cancelled += OnColorPickerCancelled;

        // Create the popup
        _colorPickerPopup = new Avalonia.Controls.Primitives.Popup
        {
            PlacementTarget = _textEditor.TextArea.TextView,
            Child = _colorPickerControl,
            IsLightDismissEnabled = true
        };

        // Position below the swatch
        _colorPickerPopup.HorizontalOffset = e.SwatchBounds.X;
        _colorPickerPopup.VerticalOffset = e.SwatchBounds.Bottom + 4;

        // Close any previously open popup
        _colorPickerPopup.Closed += (_, _) =>
        {
            if (_colorPickerControl != null)
            {
                _colorPickerControl.ColorPicked -= OnColorPicked;
                _colorPickerControl.Cancelled -= OnColorPickerCancelled;
            }
            _colorPickerControl = null;
        };

        _colorPickerPopup.IsOpen = true;
    }

    /// <summary>
    /// Handles the user confirming a new color in the picker: replaces the color
    /// values in the document text.
    /// </summary>
    private void OnColorPicked(object? sender, ColorPickedEventArgs e)
    {
        if (_textEditor?.Document == null) return;

        try
        {
            var document = _textEditor.Document;
            int startOffset = e.ColorTextStartOffset;
            int endOffset = e.ColorTextEndOffset;

            // Validate offsets
            if (startOffset < 0 || endOffset > document.TextLength || startOffset >= endOffset)
                return;

            var oldText = document.GetText(startOffset, endOffset - startOffset);

            // Determine what kind of color text we're replacing
            string newText;
            if (oldText.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
            {
                // Hex color literal: replace with new hex value
                if (e.A < 255)
                    newText = $"&H{e.A:X2}{e.R:X2}{e.G:X2}{e.B:X2}";
                else
                    newText = $"&H{e.R:X2}{e.G:X2}{e.B:X2}";
            }
            else
            {
                // RGB/RGBA numeric arguments: rebuild just the numeric values portion
                // The startOffset points to the first R value, endOffset to closing paren
                // We need to reconstruct: R, G, B or R, G, B, A
                // Detect if the original had an alpha component
                var commaCount = oldText.Count(c => c == ',');
                if (commaCount >= 3 || e.A < 255)
                    newText = $"{e.R}, {e.G}, {e.B}, {e.A})";
                else
                    newText = $"{e.R}, {e.G}, {e.B})";
            }

            document.Replace(startOffset, endOffset - startOffset, newText);

            // Invalidate the renderer so swatches redraw
            _textEditor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        }
        catch
        {
            // Silently handle replacement errors
        }

        // Close the popup
        if (_colorPickerPopup != null)
            _colorPickerPopup.IsOpen = false;
    }

    /// <summary>
    /// Handles the user cancelling the color picker.
    /// </summary>
    private void OnColorPickerCancelled(object? sender, EventArgs e)
    {
        if (_colorPickerPopup != null)
            _colorPickerPopup.IsOpen = false;
    }

    /// <summary>
    /// Gets or sets whether inline color swatches are displayed.
    /// </summary>
    public bool InlineColorSwatchesEnabled
    {
        get => _inlineColorRenderer?.IsEnabled ?? true;
        set
        {
            if (_inlineColorRenderer != null)
                _inlineColorRenderer.IsEnabled = value;
        }
    }

    #endregion

    #region Document Links

    /// <summary>
    /// Store document links returned by the LSP server.
    /// Links are rendered as underlined text and can be Ctrl+Clicked to navigate.
    /// </summary>
    public void ShowDocumentLinks(IReadOnlyList<DocumentLinkInfo> links)
    {
        _documentLinks = links;
    }

    /// <summary>
    /// Clear stored document links.
    /// </summary>
    public void ClearDocumentLinks()
    {
        _documentLinks = null;
    }

    /// <summary>
    /// Find a document link at the given 1-based line and column.
    /// </summary>
    private DocumentLinkInfo? GetDocumentLinkAt(int line, int column)
    {
        if (_documentLinks == null) return null;

        foreach (var link in _documentLinks)
        {
            if (line < link.StartLine || line > link.EndLine) continue;
            if (line == link.StartLine && column < link.StartColumn) continue;
            if (line == link.EndLine && column > link.EndColumn) continue;
            return link;
        }
        return null;
    }

    #endregion

    #region Semantic Token Highlighting

    private Highlighting.SemanticTokenHighlighter? _semanticTokenHighlighter;
    private System.Timers.Timer? _semanticTokenTimer;
    private CancellationTokenSource? _semanticTokenCts;

    /// <summary>
    /// Event raised when semantic tokens need to be refreshed (after debounce).
    /// The ViewModel/View should subscribe to this and call the LSP server.
    /// </summary>
    public event EventHandler? SemanticTokensRefreshNeeded;

    /// <summary>
    /// Updates the semantic token highlighting from LSP-provided encoded data.
    /// Must be called on the UI thread.
    /// </summary>
    /// <param name="encodedData">Raw LSP semantic token data: [deltaLine, deltaStartChar, length, tokenType, tokenModifiers] * N</param>
    public void UpdateSemanticTokens(int[] encodedData)
    {
        if (_textEditor?.TextArea?.TextView == null) return;

        if (_semanticTokenHighlighter == null)
        {
            _semanticTokenHighlighter = new Highlighting.SemanticTokenHighlighter();
            // Insert at position 0 so that lexer-based highlighting runs after and can be overridden
            // Actually, we want semantic tokens to override lexer, so add at end (last transformer wins)
            _textEditor.TextArea.TextView.LineTransformers.Add(_semanticTokenHighlighter);
        }

        _semanticTokenHighlighter.Update(encodedData, _textEditor.Document.LineCount);
        _textEditor.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Clears all semantic token highlighting.
    /// </summary>
    public void ClearSemanticTokens()
    {
        _semanticTokenHighlighter?.Clear();
        _textEditor?.TextArea?.TextView?.Redraw();
    }

    /// <summary>
    /// Starts the debounced semantic token refresh timer.
    /// Called internally when the document text changes.
    /// </summary>
    private void RequestSemanticTokenRefresh()
    {
        // Cancel any pending request
        _semanticTokenCts?.Cancel();
        _semanticTokenCts = new CancellationTokenSource();

        if (_semanticTokenTimer == null)
        {
            _semanticTokenTimer = new System.Timers.Timer(500);
            _semanticTokenTimer.AutoReset = false;
            _semanticTokenTimer.Elapsed += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    SemanticTokensRefreshNeeded?.Invoke(this, EventArgs.Empty);
                });
            };
        }

        _semanticTokenTimer.Stop();
        _semanticTokenTimer.Start();
    }

    #endregion

    #region Diagnostics / Error Markers

    /// <summary>
    /// Updates the diagnostic markers in the editor
    /// </summary>
    public void UpdateDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        if (_textMarkerService == null || _textEditor == null) return;

        // Materialize the enumerable so we can iterate twice (squiggles + gutter icons)
        var diagnosticList = diagnostics as IList<DiagnosticItem> ?? diagnostics.ToList();

        // Clear existing markers
        _textMarkerService.Clear();

        foreach (var diagnostic in diagnosticList)
        {
            try
            {
                // Convert line/column to offset
                var line = _textEditor.Document.GetLineByNumber(
                    Math.Min(diagnostic.Line, _textEditor.Document.LineCount));
                var startOffset = line.Offset + Math.Min(diagnostic.Column - 1, line.Length);

                // Calculate length - use end position if available, otherwise highlight to end of line
                int length;
                if (diagnostic.EndLine > 0 && diagnostic.EndColumn > 0)
                {
                    var endLine = _textEditor.Document.GetLineByNumber(
                        Math.Min(diagnostic.EndLine, _textEditor.Document.LineCount));
                    var endOffset = endLine.Offset + Math.Min(diagnostic.EndColumn - 1, endLine.Length);
                    length = Math.Max(1, endOffset - startOffset);
                }
                else
                {
                    // Default to highlighting the word at this position or rest of line
                    length = Math.Min(10, line.EndOffset - startOffset);
                    if (length < 1) length = 1;
                }

                var markerType = diagnostic.Severity switch
                {
                    DiagnosticSeverity.Error => TextMarkerType.Error,
                    DiagnosticSeverity.Warning => TextMarkerType.Warning,
                    DiagnosticSeverity.Info => TextMarkerType.Info,
                    _ => TextMarkerType.Hint
                };

                _textMarkerService.Create(startOffset, length, markerType, diagnostic.Message);
            }
            catch
            {
                // Ignore invalid diagnostics
            }
        }

        // Update diagnostic gutter margin icons
        _diagnosticMargin?.UpdateDiagnostics(diagnosticList);

        _textEditor.TextArea.TextView.Redraw();
    }

    #endregion

    #region Auto-Close Brackets & Line Insert

    private void OnTextAreaTextEntered(object? sender, TextInputEventArgs e)
    {
        if (!_autoCloseBrackets || string.IsNullOrEmpty(e.Text) || e.Text.Length != 1) return;
        if (_textEditor?.Document == null) return;

        var typed = e.Text[0];
        if (!AutoClosePairs.TryGetValue(typed, out var closing)) return;

        var offset = _textEditor.CaretOffset;

        // For quotes, don't auto-close if inside a string or comment
        if (typed == '"' || typed == '\'')
        {
            if (IsCaretInsideStringOrComment(offset - 1)) return;
            // Don't auto-close single quote if it looks like an apostrophe in text
            if (typed == '\'' && offset > 1)
            {
                var prevChar = _textEditor.Document.GetCharAt(offset - 2);
                if (char.IsLetterOrDigit(prevChar)) return; // e.g., "don't"
            }
        }

        // Don't auto-close if the next character is a letter/digit (likely editing mid-word)
        if (offset < _textEditor.Document.TextLength)
        {
            var nextChar = _textEditor.Document.GetCharAt(offset);
            if (char.IsLetterOrDigit(nextChar)) return;
        }

        // Insert closing character
        _textEditor.Document.Insert(offset, closing.ToString());
        // Keep caret between the pair
        _textEditor.CaretOffset = offset;
    }

    private bool IsCaretInsideStringOrComment(int offset)
    {
        if (_textEditor?.Document == null || offset < 0) return false;

        var doc = _textEditor.Document;
        var line = doc.GetLineByOffset(Math.Min(offset, doc.TextLength));
        var lineText = doc.GetText(line.Offset, Math.Min(offset - line.Offset, line.Length));

        bool inString = false;
        foreach (var c in lineText)
        {
            if (c == '\'') return true; // Rest of line is comment
            if (c == '"') inString = !inString;
        }
        return inString;
    }

    private bool DeleteEmptyPair()
    {
        if (_textEditor?.Document == null) return false;

        var offset = _textEditor.CaretOffset;
        if (offset <= 0 || offset >= _textEditor.Document.TextLength) return false;

        var before = _textEditor.Document.GetCharAt(offset - 1);
        var after = _textEditor.Document.GetCharAt(offset);

        if (AutoClosePairs.TryGetValue(before, out var expected) && after == expected)
        {
            _textEditor.Document.Remove(offset - 1, 2);
            _textEditor.CaretOffset = offset - 1;
            return true;
        }
        return false;
    }

    private void InsertLineBelow()
    {
        if (_textEditor?.Document == null) return;

        var doc = _textEditor.Document;
        var line = doc.GetLineByNumber(_textEditor.TextArea.Caret.Line);
        var lineText = doc.GetText(line.Offset, line.Length);

        // Get indentation of current line
        var indent = "";
        foreach (var c in lineText)
        {
            if (c == ' ' || c == '\t') indent += c;
            else break;
        }

        doc.Insert(line.EndOffset, Environment.NewLine + indent);
        _textEditor.TextArea.Caret.Line = line.LineNumber + 1;
        _textEditor.TextArea.Caret.Column = indent.Length + 1;
    }

    private void InsertLineAbove()
    {
        if (_textEditor?.Document == null) return;

        var doc = _textEditor.Document;
        var line = doc.GetLineByNumber(_textEditor.TextArea.Caret.Line);
        var lineText = doc.GetText(line.Offset, line.Length);

        // Get indentation of current line
        var indent = "";
        foreach (var c in lineText)
        {
            if (c == ' ' || c == '\t') indent += c;
            else break;
        }

        doc.Insert(line.Offset, indent + Environment.NewLine);
        _textEditor.TextArea.Caret.Line = line.LineNumber;
        _textEditor.TextArea.Caret.Column = indent.Length + 1;
    }

    /// <summary>
    /// Auto-inserts the corresponding End block (End Sub, End Function, etc.) when Enter is pressed
    /// after a block-opening keyword. The cursor stays on the blank indented line between the opener and closer.
    /// </summary>
    private void TryAutoInsertEndBlock()
    {
        if (_textEditor?.Document == null) return;

        var doc = _textEditor.Document;
        var caretLine = _textEditor.TextArea.Caret.Line;
        if (caretLine < 2) return;

        // The line ABOVE the cursor is the one the user just pressed Enter on
        var prevDocLine = doc.GetLineByNumber(caretLine - 1);
        var prevLineText = doc.GetText(prevDocLine.Offset, prevDocLine.Length);
        var trimmedPrev = prevLineText.TrimStart();

        // Determine if the previous line opens a block that needs an End
        var endKeyword = GetAutoEndKeyword(trimmedPrev);
        if (endKeyword == null) return;

        // Check if the end block already exists on the next line (avoid duplicates)
        if (caretLine < doc.LineCount)
        {
            var nextDocLine = doc.GetLineByNumber(caretLine + 1);
            var nextLineText = doc.GetText(nextDocLine.Offset, nextDocLine.Length).TrimStart();
            if (nextLineText.StartsWith(endKeyword, StringComparison.OrdinalIgnoreCase))
                return;
        }
        // Also check the current line (caret might be on a non-empty line)
        var currentDocLine = doc.GetLineByNumber(caretLine);
        var currentLineText = doc.GetText(currentDocLine.Offset, currentDocLine.Length).TrimStart();
        if (currentLineText.StartsWith(endKeyword, StringComparison.OrdinalIgnoreCase))
            return;

        // Get the indentation of the opening line
        var openIndent = Completion.SmartIndentHandler.GetLineIndent(prevLineText);

        // Save the current caret position
        var savedOffset = _textEditor.CaretOffset;

        // Insert a new line with the End keyword at the same indent level as the opener
        var endLine = Environment.NewLine + openIndent + endKeyword;
        doc.Insert(currentDocLine.EndOffset, endLine);

        // Restore caret to the blank indented line between opener and closer
        _textEditor.CaretOffset = savedOffset;
    }

    /// <summary>
    /// Returns the End keyword to auto-insert for the given line, or null if no auto-insert is needed.
    /// </summary>
    private static string? GetAutoEndKeyword(string trimmedLine)
    {
        var lower = trimmedLine.ToLowerInvariant();

        // Remove access modifiers for matching
        var stripped = System.Text.RegularExpressions.Regex.Replace(lower,
            @"^(public|private|protected|friend|shared|overridable|overrides|mustoverride|notoverridable|static)\s+", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Strip again in case there were two (e.g., "Public Shared")
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped,
            @"^(public|private|protected|friend|shared|overridable|overrides|mustoverride|notoverridable|static)\s+", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (stripped.StartsWith("sub ") || stripped == "sub")
            return "End Sub";
        if (stripped.StartsWith("function ") || stripped == "function")
            return "End Function";
        if (stripped.StartsWith("class ") || stripped == "class")
            return "End Class";
        if (stripped.StartsWith("module ") || stripped == "module")
            return "End Module";
        if (stripped.StartsWith("namespace ") || stripped == "namespace")
            return "End Namespace";
        if (stripped.StartsWith("structure ") || stripped == "structure")
            return "End Structure";
        if (stripped.StartsWith("interface ") || stripped == "interface")
            return "End Interface";
        if (stripped.StartsWith("enum ") || stripped == "enum")
            return "End Enum";
        if (stripped.StartsWith("property ") || stripped == "property")
            return "End Property";
        if (stripped.StartsWith("type ") || stripped == "type")
            return "End Type";
        if (stripped.StartsWith("select "))
            return "End Select";
        if (stripped.StartsWith("try"))
            return "End Try";
        if (stripped.StartsWith("with ") || stripped == "with")
            return "End With";

        // If...Then (multi-line only - not single-line If)
        if (stripped.StartsWith("if ") && stripped.EndsWith("then") && !HasCodeAfterThen(trimmedLine))
            return "End If";

        // For loops
        if (stripped.StartsWith("for each ") || stripped.StartsWith("for "))
            return "Next";

        // While loops
        if (stripped.StartsWith("while ") || stripped == "while")
            return "End While";

        // Do loops
        if (stripped.StartsWith("do ") || stripped == "do")
            return "Loop";

        return null;
    }

    /// <summary>
    /// Returns true if there is code after "Then" on the same line (single-line If).
    /// </summary>
    private static bool HasCodeAfterThen(string line)
    {
        var thenIndex = line.LastIndexOf("Then", StringComparison.OrdinalIgnoreCase);
        if (thenIndex < 0) return false;
        var afterThen = line.Substring(thenIndex + 4).TrimStart();
        return !string.IsNullOrEmpty(afterThen) && !afterThen.StartsWith("'");
    }

    #endregion

    #region Breakpoints

    /// <summary>
    /// Initializes breakpoint margin support
    /// </summary>
    public void InitializeBreakpoints(HashSet<int> breakpointLines, Action<int> onBreakpointToggled)
    {
        if (_textEditor == null || _breakpointMargin != null) return;

        _breakpointMargin = new BreakpointMargin(_textEditor, breakpointLines);
        _breakpointMargin.BreakpointToggled += (s, line) =>
        {
            onBreakpointToggled(line);
            BreakpointToggled?.Invoke(this, line);
        };

        // Insert after bookmark margin (if present) but before line numbers
        var insertIndex = _bookmarkMargin != null ? 1 : 0;
        _textEditor.TextArea.LeftMargins.Insert(insertIndex, _breakpointMargin);
    }

    /// <summary>
    /// Updates which lines have breakpoints
    /// </summary>
    public void UpdateBreakpoints(HashSet<int> breakpointLines)
    {
        _breakpointMargin?.UpdateBreakpoints(breakpointLines);
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Updates breakpoints with full visual info (kind, enabled state).
    /// </summary>
    public void UpdateBreakpoints(Dictionary<int, BreakpointVisualInfo> breakpoints)
    {
        _breakpointMargin?.UpdateBreakpoints(breakpoints);
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Highlights the current execution line (during debugging)
    /// </summary>
    public void SetCurrentExecutionLine(int? line)
    {
        _breakpointMargin?.SetCurrentLine(line);

        // Highlight the execution line background (yellow overlay)
        if (_textEditor?.TextArea?.TextView != null)
        {
            if (_executionLineRenderer == null)
            {
                _executionLineRenderer = new TextMarkers.ExecutionLineRenderer(_textEditor);
                _textEditor.TextArea.TextView.BackgroundRenderers.Add(_executionLineRenderer);
            }
            _executionLineRenderer.SetExecutionLine(line);
        }

        _textEditor?.TextArea.TextView.Redraw();

        // Also scroll to the line if set
        if (line.HasValue && _textEditor != null)
        {
            _textEditor.TextArea.Caret.Line = line.Value;
            _textEditor.ScrollToLine(line.Value);
        }
    }

    /// <summary>
    /// Navigates to a specific line in the editor
    /// </summary>
    public void GoToLine(int line)
    {
        if (_textEditor == null) return;
        _textEditor.TextArea.Caret.Line = line;
        _textEditor.TextArea.Caret.Column = 1;
        _textEditor.ScrollToLine(line);
        _textEditor.Focus();
    }

    /// <summary>
    /// Handles click on a sticky scroll header line to navigate to that line.
    /// </summary>
    private void OnStickyScrollLineClicked(object? sender, int lineNumber)
    {
        GoToLine(lineNumber);
    }

    #endregion

    #region Git Gutter

    /// <summary>
    /// Initializes the git gutter margin (narrow colored bar showing added/modified/deleted lines).
    /// Should be called once when the editor is ready.
    /// </summary>
    public void InitializeGitGutter()
    {
        if (_textEditor == null || _gitGutterMargin != null) return;

        _gitGutterMargin = new GitGutterMargin(_textEditor);

        // Insert at position 0 (leftmost, before bookmarks and breakpoints)
        _textEditor.TextArea.LeftMargins.Insert(0, _gitGutterMargin);
    }

    /// <summary>
    /// Updates the git gutter with the given line changes.
    /// </summary>
    public void SetGitChanges(IReadOnlyList<GitLineChange> changes)
    {
        if (_gitGutterMargin == null)
            InitializeGitGutter();

        _gitGutterMargin?.SetChanges(changes);
        _textEditor?.TextArea.TextView.Redraw();
    }

    /// <summary>
    /// Clears all git gutter indicators.
    /// </summary>
    public void ClearGitChanges()
    {
        _gitGutterMargin?.ClearChanges();
        _textEditor?.TextArea.TextView.Redraw();
    }

    #endregion

    #region Drag and Drop

    /// <summary>
    /// Handles DragOver to provide visual feedback for text and file drops.
    /// </summary>
    private void OnDragOver(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else if (e.Data.Contains(DataFormats.Text))
            {
                // Move caret to the position under the mouse to show drop target
                var pos = GetDropPosition(e);
                if (pos >= 0 && _textEditor != null)
                {
                    _textEditor.CaretOffset = pos;
                    e.DragEffects = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                        ? DragDropEffects.Copy
                        : DragDropEffects.Move;
                    e.Handled = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DragOver error: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles Drop for text movement and file opens.
    /// </summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        try
        {
            // Handle file drops (from Solution Explorer or file system)
            if (e.Data.Contains(DataFormats.Files))
            {
                var files = e.Data.GetFiles();
                if (files != null)
                {
                    foreach (var item in files)
                    {
                        var path = item.Path?.LocalPath;
                        if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                        {
                            FileDropped?.Invoke(this, new FileDroppedEventArgs(path));
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            // Handle text drops (drag-and-drop text movement within editor)
            if (e.Data.Contains(DataFormats.Text) && _textEditor != null)
            {
                var text = e.Data.GetText();
                if (string.IsNullOrEmpty(text)) return;

                var dropOffset = GetDropPosition(e);
                if (dropOffset < 0) return;

                bool isCopy = e.KeyModifiers.HasFlag(KeyModifiers.Control);
                var document = _textEditor.Document;

                // Get the current selection (source of the drag)
                var selection = _textEditor.TextArea.Selection;
                var hasSelection = !selection.IsEmpty;
                var selSegment = hasSelection ? selection.SurroundingSegment : null;

                if (hasSelection && selSegment != null && !isCopy)
                {
                    // Move operation: remove from source, insert at target
                    // Adjust drop offset if it falls after the removed text
                    int removeOffset = selSegment.Offset;
                    int removeLength = selSegment.Length;

                    // Don't drop inside the selection itself
                    if (dropOffset >= removeOffset && dropOffset <= removeOffset + removeLength)
                    {
                        e.Handled = true;
                        return;
                    }

                    document.BeginUpdate();
                    try
                    {
                        // If dropping after the selection, adjust for removal
                        if (dropOffset > removeOffset)
                        {
                            document.Remove(removeOffset, removeLength);
                            dropOffset -= removeLength;
                            document.Insert(dropOffset, text);
                        }
                        else
                        {
                            document.Insert(dropOffset, text);
                            document.Remove(removeOffset + text.Length, removeLength);
                        }

                        // Select the moved text at its new location
                        _textEditor.Select(dropOffset, text.Length);
                    }
                    finally
                    {
                        document.EndUpdate();
                    }
                }
                else
                {
                    // Copy operation or external text: just insert
                    document.Insert(dropOffset, text);
                    _textEditor.Select(dropOffset, text.Length);
                }

                _textEditor.Focus();
                e.Handled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Drop error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the document offset at the mouse position during a drag-drop operation.
    /// </summary>
    private int GetDropPosition(DragEventArgs e)
    {
        if (_textEditor == null) return -1;

        try
        {
            var textView = _textEditor.TextArea.TextView;
            var pos = e.GetPosition(textView);
            var textPos = textView.GetPositionFloor(pos + textView.ScrollOffset);
            if (textPos.HasValue)
            {
                return _textEditor.Document.GetOffset(textPos.Value.Location);
            }
        }
        catch (Exception)
        {
            // Position may be outside document bounds
        }
        return -1;
    }

    #endregion

    #region Smooth Scrolling

    /// <summary>
    /// Handles mouse wheel events on the editor. When smooth scrolling is enabled,
    /// the event is intercepted and an animated scroll is performed instead of the
    /// default instant jump.
    /// </summary>
    private void OnEditorPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_smoothScrollingEnabled || _textEditor?.TextArea?.TextView == null)
            return;

        var textView = _textEditor.TextArea.TextView;
        var lineHeight = textView.DefaultLineHeight;

        // Standard scroll: 3 lines per wheel notch
        double deltaY = -e.Delta.Y * lineHeight * 3;
        double deltaX = -e.Delta.X * lineHeight * 3;

        if (deltaY == 0 && deltaX == 0)
            return;

        // If not currently animating, sync targets from current actual position
        if (!_isSmoothScrolling)
        {
            _scrollCurrentY = textView.ScrollOffset.Y;
            _scrollCurrentX = textView.ScrollOffset.X;
            _scrollTargetY = _scrollCurrentY;
            _scrollTargetX = _scrollCurrentX;
        }

        // Accumulate target (allows quick successive scrolls to chain smoothly)
        _scrollTargetY += deltaY;
        _scrollTargetX += deltaX;

        // Clamp targets to valid scroll range
        var scrollViewer = _textEditor.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer != null)
        {
            var maxY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var maxX = Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);
            _scrollTargetY = Math.Clamp(_scrollTargetY, 0, maxY);
            _scrollTargetX = Math.Clamp(_scrollTargetX, 0, maxX);
        }

        // Start animation if not already running
        if (!_isSmoothScrolling)
        {
            _isSmoothScrolling = true;
            _smoothScrollTimer?.Start();
        }

        // Mark handled to prevent default scroll behavior
        e.Handled = true;
    }

    /// <summary>
    /// Timer tick that interpolates the scroll offset toward the target using
    /// an ease-out curve for a natural deceleration feel.
    /// </summary>
    private void OnSmoothScrollTick(object? sender, EventArgs e)
    {
        if (_textEditor?.TextArea?.TextView == null)
        {
            _smoothScrollTimer?.Stop();
            _isSmoothScrolling = false;
            return;
        }

        // Ease-out interpolation factor: lerp 25% of remaining distance each tick
        // This gives a fast start that decelerates smoothly
        const double lerpFactor = 0.25;

        _scrollCurrentY += (_scrollTargetY - _scrollCurrentY) * lerpFactor;
        _scrollCurrentX += (_scrollTargetX - _scrollCurrentX) * lerpFactor;

        // Snap to target when close enough to avoid endless tiny adjustments
        if (Math.Abs(_scrollTargetY - _scrollCurrentY) < SmoothScrollEpsilon)
            _scrollCurrentY = _scrollTargetY;
        if (Math.Abs(_scrollTargetX - _scrollCurrentX) < SmoothScrollEpsilon)
            _scrollCurrentX = _scrollTargetX;

        // Apply the interpolated offset
        var scrollViewer = _textEditor.FindDescendantOfType<ScrollViewer>();
        if (scrollViewer != null)
            scrollViewer.Offset = new Vector(_scrollCurrentX, _scrollCurrentY);

        // Stop the timer once we've reached the target
        if (Math.Abs(_scrollTargetY - _scrollCurrentY) < SmoothScrollEpsilon &&
            Math.Abs(_scrollTargetX - _scrollCurrentX) < SmoothScrollEpsilon)
        {
            _smoothScrollTimer?.Stop();
            _isSmoothScrolling = false;
        }
    }

    #endregion

    #region Cursor Fade Animation

    /// <summary>
    /// A background renderer that draws a smooth-fading cursor overlay on the caret position.
    /// Instead of the default hard on/off blink, the cursor opacity oscillates using a sine wave
    /// for a gentle fade-in/fade-out effect (similar to VS Code's "smooth" cursor blink).
    /// </summary>
    private class CursorFadeRenderer : IBackgroundRenderer
    {
        private readonly TextEditor _editor;
        private readonly DispatcherTimer _fadeTimer;
        private double _phase; // 0..2*PI oscillation phase
        private const double FadeCycleDuration = 1200.0; // full blink cycle in ms
        private const double FadeTimerInterval = 16.0;   // ~60fps
        private const double MinOpacity = 0.0;
        private const double MaxOpacity = 1.0;
        private DateTime _lastCaretMove;
        private const double HoldVisibleMs = 600.0; // hold cursor fully visible after movement

        public KnownLayer Layer => KnownLayer.Caret;

        public CursorFadeRenderer(TextEditor editor)
        {
            _editor = editor;
            _phase = 0;
            _lastCaretMove = DateTime.UtcNow;

            // Track caret movement to reset the blink cycle
            editor.TextArea.Caret.PositionChanged += (_, _) =>
            {
                _phase = 0; // reset to fully visible
                _lastCaretMove = DateTime.UtcNow;
            };

            _fadeTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(FadeTimerInterval)
            };
            _fadeTimer.Tick += (_, _) =>
            {
                // Advance phase only after the hold-visible period
                var elapsed = (DateTime.UtcNow - _lastCaretMove).TotalMilliseconds;
                if (elapsed > HoldVisibleMs)
                {
                    _phase += (2 * Math.PI * FadeTimerInterval) / FadeCycleDuration;
                    if (_phase > 2 * Math.PI)
                        _phase -= 2 * Math.PI;
                }
                else
                {
                    _phase = 0;
                }

                // Request a redraw of the caret area
                editor.TextArea.TextView.InvalidateLayer(KnownLayer.Caret);
            };
            _fadeTimer.Start();

            // Hide the built-in caret so our fade renderer replaces it
            editor.TextArea.Caret.CaretBrush = Brushes.Transparent;
        }

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid) return;

            try
            {
                var caret = _editor.TextArea.Caret;
                var caretOffset = _editor.Document.GetOffset(caret.Location);

                // Find the visual line containing the caret
                var visualLine = textView.GetVisualLine(caret.Line);
                if (visualLine == null) return;

                // Get position using relative offset from line start (same pattern as MultiCursorRenderer)
                var relativeOffset = caretOffset - visualLine.FirstDocumentLine.Offset;
                var pos = visualLine.GetVisualPosition(relativeOffset, VisualYPosition.LineTop);

                // Calculate opacity from sine wave: cos gives 1 at phase=0 (fully visible)
                double opacity = MinOpacity + (MaxOpacity - MinOpacity) *
                                 (0.5 + 0.5 * Math.Cos(_phase));

                // Draw the caret line (2px wide vertical bar)
                var caretColor = EditorTheme.Foreground;
                var brush = new SolidColorBrush(caretColor, opacity);

                var rect = new Rect(
                    pos.X - textView.ScrollOffset.X,
                    pos.Y - textView.ScrollOffset.Y,
                    2, // cursor width
                    visualLine.Height
                );

                drawingContext.FillRectangle(brush, rect);
            }
            catch (Exception)
            {
                // Ignore rendering errors (e.g., during document changes)
            }
        }

        public void Stop()
        {
            _fadeTimer.Stop();
        }
    }

    #endregion

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        EditorTheme.ThemeChanged -= OnEditorThemeChanged;

        _foldingUpdateTimer?.Stop();
        _foldingUpdateTimer?.Dispose();
        _foldingUpdateTimer = null;

        _hoverTimer?.Stop();
        _hoverTimer?.Dispose();
        _hoverTimer = null;

        _smoothScrollTimer?.Stop();
        _smoothScrollTimer = null;

        _cursorFadeRenderer?.Stop();

        _stickyScroll?.DetachEditor();
    }
}

/// <summary>
/// Event args for file drop operations on the editor
/// </summary>
public class FileDroppedEventArgs : EventArgs
{
    public string FilePath { get; }

    public FileDroppedEventArgs(string filePath)
    {
        FilePath = filePath;
    }
}

/// <summary>
/// Event args for completion requests
/// </summary>
public class CompletionRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }
    public int Offset { get; }

    public CompletionRequestEventArgs(int line, int column, int offset)
    {
        Line = line;
        Column = column;
        Offset = offset;
    }
}

/// <summary>
/// Event args for data tip requests
/// </summary>
public class DataTipRequestEventArgs : EventArgs
{
    public string Expression { get; }
    public double ScreenX { get; }
    public double ScreenY { get; }
    public int Line { get; }
    public int Column { get; }

    public DataTipRequestEventArgs(string expression, double screenX, double screenY, int line = 0, int column = 0)
    {
        Expression = expression;
        ScreenX = screenX;
        ScreenY = screenY;
        Line = line;
        Column = column;
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

/// <summary>
/// Event args for signature help requests
/// </summary>
public class SignatureHelpRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public SignatureHelpRequestEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

/// <summary>
/// Event args for document highlight requests
/// </summary>
public class DocumentHighlightRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public DocumentHighlightRequestEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class DocumentLinkClickedEventArgs : EventArgs
{
    public string Target { get; }
    public string? Tooltip { get; }

    public DocumentLinkClickedEventArgs(string target, string? tooltip)
    {
        Target = target;
        Tooltip = tooltip;
    }
}
