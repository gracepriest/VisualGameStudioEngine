using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Rendering;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// Inline peek definition/references view that appears between editor lines.
/// Uses a read-only AvalonEdit instance with full syntax highlighting.
/// </summary>
public partial class PeekViewControl : UserControl
{
    private TextEditor? _peekEditor;
    private Button? _prevButton;
    private Button? _nextButton;
    private TextBlock? _resultCounter;
    private TextBlock? _filePathText;
    private TextBlock? _lineInfoText;
    private TextBlock? _modeLabel;
    private Border? _loadingOverlay;
    private Border? _errorOverlay;
    private TextBlock? _errorText;

    private List<PeekLocation> _results = new();
    private int _currentIndex;
    private bool _isResizing;
    private Point _resizeStartPoint;
    private double _resizeStartHeight;
    private PeekDefinitionLineHighlighter? _lineHighlighter;

    /// <summary>
    /// The file path of the currently displayed definition.
    /// </summary>
    public string FilePath { get; private set; } = "";

    /// <summary>
    /// The line number (1-based) of the current definition.
    /// </summary>
    public int Line { get; private set; }

    /// <summary>
    /// The column number (1-based) of the current definition.
    /// </summary>
    public int Column { get; private set; }

    /// <summary>
    /// Total number of results available.
    /// </summary>
    public int TotalResults => _results.Count;

    /// <summary>
    /// The index (0-based) of the currently displayed result.
    /// </summary>
    public int CurrentResultIndex => _currentIndex;

    /// <summary>
    /// Whether the peek view is currently showing (alias for IsVisible).
    /// </summary>
    public bool IsPeekVisible => IsVisible;

    /// <summary>
    /// The peek mode: Definition or References.
    /// </summary>
    public PeekMode Mode { get; private set; } = PeekMode.Definition;

    /// <summary>
    /// Fired when the user requests to open the definition in a full editor tab.
    /// </summary>
    public event EventHandler<PeekNavigateEventArgs>? OpenInEditorRequested;

    /// <summary>
    /// Fired when the peek view is closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Fired when the user clicks the file path to navigate.
    /// </summary>
    public event EventHandler<PeekNavigateEventArgs>? FileNavigationRequested;

    public PeekViewControl()
    {
        InitializeComponent();
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        _peekEditor = this.FindControl<TextEditor>("PeekEditor");
        _prevButton = this.FindControl<Button>("PrevButton");
        _nextButton = this.FindControl<Button>("NextButton");
        _resultCounter = this.FindControl<TextBlock>("ResultCounter");
        _filePathText = this.FindControl<TextBlock>("FilePathText");
        _lineInfoText = this.FindControl<TextBlock>("LineInfoText");
        _modeLabel = this.FindControl<TextBlock>("ModeLabel");
        _loadingOverlay = this.FindControl<Border>("LoadingOverlay");
        _errorOverlay = this.FindControl<Border>("ErrorOverlay");
        _errorText = this.FindControl<TextBlock>("ErrorText");

        if (_peekEditor != null)
        {
            // Apply BasicLang syntax highlighting
            var highlighting = HighlightingManager.Instance.GetDefinition("BasicLang");
            if (highlighting != null)
            {
                _peekEditor.SyntaxHighlighting = highlighting;
            }

            // Add line highlighter for the definition line
            _lineHighlighter = new PeekDefinitionLineHighlighter();
            _peekEditor.TextArea.TextView.BackgroundRenderers.Add(_lineHighlighter);

            // Set dark theme colors
            _peekEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(80, 0, 122, 204));
            _peekEditor.TextArea.SelectionForeground = null;
        }
    }

    /// <summary>
    /// Shows a single definition at the given file/line/column.
    /// Reads the file and displays it with syntax highlighting.
    /// </summary>
    public async Task ShowAsync(string filePath, int line, int column)
    {
        await ShowMultipleAsync(new List<PeekLocation>
        {
            new PeekLocation { FilePath = filePath, Line = line, Column = column }
        }, PeekMode.Definition);
    }

    /// <summary>
    /// Shows multiple locations (definitions or references).
    /// </summary>
    public async Task ShowMultipleAsync(List<PeekLocation> results, PeekMode mode = PeekMode.Definition)
    {
        if (results.Count == 0)
        {
            ShowError(mode == PeekMode.Definition
                ? "No definition found."
                : "No references found.");
            return;
        }

        Mode = mode;
        _results = results;
        _currentIndex = 0;

        UpdateModeLabel();
        UpdateNavigationVisibility();

        IsVisible = true;
        ShowLoading(true);

        await LoadResultAsync(_results[0]);

        ShowLoading(false);
    }

    /// <summary>
    /// Shows the peek view displaying the given source code directly
    /// (for cases where the code is already available).
    /// </summary>
    public void ShowWithCode(string filePath, int line, int column, string sourceCode)
    {
        _results = new List<PeekLocation>
        {
            new PeekLocation { FilePath = filePath, Line = line, Column = column, SourceCode = sourceCode }
        };
        _currentIndex = 0;
        Mode = PeekMode.Definition;

        UpdateModeLabel();
        UpdateNavigationVisibility();

        IsVisible = true;
        ShowLoading(false);
        ShowError(null);

        LoadSourceCode(sourceCode, line);
        UpdateHeader(filePath, line);
    }

    /// <summary>
    /// Closes and hides the peek view.
    /// </summary>
    public void Close()
    {
        IsVisible = false;
        _results.Clear();
        _currentIndex = 0;

        if (_peekEditor != null)
        {
            _peekEditor.Text = "";
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Navigate to the next result.
    /// </summary>
    public async void NavigateNext()
    {
        if (_results.Count <= 1) return;

        _currentIndex = (_currentIndex + 1) % _results.Count;
        UpdateNavigationVisibility();
        ShowLoading(true);
        await LoadResultAsync(_results[_currentIndex]);
        ShowLoading(false);
    }

    /// <summary>
    /// Navigate to the previous result.
    /// </summary>
    public async void NavigatePrevious()
    {
        if (_results.Count <= 1) return;

        _currentIndex = (_currentIndex - 1 + _results.Count) % _results.Count;
        UpdateNavigationVisibility();
        ShowLoading(true);
        await LoadResultAsync(_results[_currentIndex]);
        ShowLoading(false);
    }

    private async Task LoadResultAsync(PeekLocation location)
    {
        try
        {
            ShowError(null);

            string sourceCode;
            if (!string.IsNullOrEmpty(location.SourceCode))
            {
                sourceCode = location.SourceCode;
            }
            else if (File.Exists(location.FilePath))
            {
                sourceCode = await File.ReadAllTextAsync(location.FilePath);
                location.SourceCode = sourceCode;
            }
            else
            {
                ShowError($"File not found: {location.FilePath}");
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                LoadSourceCode(sourceCode, location.Line);
                UpdateHeader(location.FilePath, location.Line);
            });
        }
        catch (Exception ex)
        {
            ShowError($"Error loading: {ex.Message}");
        }
    }

    private void LoadSourceCode(string sourceCode, int targetLine)
    {
        if (_peekEditor == null) return;

        _peekEditor.Text = sourceCode;

        // Set the highlighted line
        if (_lineHighlighter != null)
        {
            _lineHighlighter.HighlightedLine = targetLine;
            _peekEditor.TextArea.TextView.Redraw();
        }

        // Scroll to the definition line with context above
        int contextLines = 5;
        int scrollToLine = Math.Max(1, targetLine - contextLines);

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_peekEditor.Document != null && targetLine >= 1 && targetLine <= _peekEditor.Document.LineCount)
                {
                    _peekEditor.ScrollToLine(scrollToLine);

                    // Place caret on the definition line
                    var docLine = _peekEditor.Document.GetLineByNumber(targetLine);
                    _peekEditor.CaretOffset = docLine.Offset;
                }
            }
            catch
            {
                // Ignore scroll errors
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateHeader(string filePath, int line)
    {
        FilePath = filePath;
        Line = line;

        if (_filePathText != null)
        {
            _filePathText.Text = Path.GetFileName(filePath);
            ToolTip.SetTip(_filePathText, filePath);
        }

        if (_lineInfoText != null)
        {
            _lineInfoText.Text = $"Line {line}";
        }
    }

    private void UpdateModeLabel()
    {
        if (_modeLabel != null)
        {
            _modeLabel.Text = Mode == PeekMode.Definition ? "Definition" : "References";
        }
    }

    private void UpdateNavigationVisibility()
    {
        bool showNav = _results.Count > 1;

        if (_prevButton != null) _prevButton.IsVisible = showNav;
        if (_nextButton != null) _nextButton.IsVisible = showNav;

        if (_resultCounter != null)
        {
            _resultCounter.IsVisible = showNav;
            _resultCounter.Text = $"{_currentIndex + 1} of {_results.Count}";
        }
    }

    private void ShowLoading(bool show)
    {
        if (_loadingOverlay != null)
            _loadingOverlay.IsVisible = show;
        if (_peekEditor != null)
            _peekEditor.IsVisible = !show;
    }

    private void ShowError(string? message)
    {
        if (_errorOverlay != null)
        {
            _errorOverlay.IsVisible = !string.IsNullOrEmpty(message);
        }
        if (_errorText != null)
        {
            _errorText.Text = message ?? "";
        }
        if (_peekEditor != null && !string.IsNullOrEmpty(message))
        {
            _peekEditor.IsVisible = false;
        }
    }

    // --- Event Handlers ---

    private void OnPreviousResult(object? sender, RoutedEventArgs e)
    {
        NavigatePrevious();
    }

    private void OnNextResult(object? sender, RoutedEventArgs e)
    {
        NavigateNext();
    }

    private void OnFilePathClicked(object? sender, PointerPressedEventArgs e)
    {
        if (_results.Count > 0 && _currentIndex < _results.Count)
        {
            var loc = _results[_currentIndex];
            FileNavigationRequested?.Invoke(this, new PeekNavigateEventArgs(loc.FilePath, loc.Line, loc.Column));
        }
    }

    private void OnOpenInEditor(object? sender, RoutedEventArgs e)
    {
        OpenCurrentInEditor();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenCurrentInEditor()
    {
        if (_results.Count > 0 && _currentIndex < _results.Count)
        {
            var loc = _results[_currentIndex];
            OpenInEditorRequested?.Invoke(this, new PeekNavigateEventArgs(loc.FilePath, loc.Line, loc.Column));
            Close();
        }
    }

    // --- Keyboard handling ---

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter || e.Key == Key.F12)
        {
            OpenCurrentInEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && e.KeyModifiers == KeyModifiers.Alt)
        {
            NavigateNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && e.KeyModifiers == KeyModifiers.Alt)
        {
            NavigatePrevious();
            e.Handled = true;
        }
    }

    // --- Resize grip handling ---

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        _isResizing = true;
        _resizeStartPoint = e.GetPosition(this);
        _resizeStartHeight = Bounds.Height;
        e.Pointer.Capture((IInputElement)sender!);
        e.Handled = true;
    }

    private void OnResizeGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;

        var currentPoint = e.GetPosition(this);
        var delta = currentPoint.Y - _resizeStartPoint.Y;
        var newHeight = Math.Max(100, Math.Min(600, _resizeStartHeight + delta));

        // Update the MaxHeight of the parent border
        if (this.Content is Border border)
        {
            border.MaxHeight = newHeight;
        }

        e.Handled = true;
    }

    private void OnResizeGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isResizing = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// Applies the same font settings as the main editor.
    /// </summary>
    public void ApplyFontSettings(double fontSize, string fontFamily)
    {
        if (_peekEditor != null)
        {
            _peekEditor.FontSize = fontSize > 0 ? fontSize - 2 : 12; // Slightly smaller
            _peekEditor.FontFamily = new FontFamily(fontFamily);
        }
    }
}

/// <summary>
/// Represents a location to display in the peek view.
/// </summary>
public class PeekLocation
{
    public string FilePath { get; set; } = "";
    public int Line { get; set; }
    public int Column { get; set; }
    public string Preview { get; set; } = "";
    public string? SourceCode { get; set; }
}

/// <summary>
/// The mode of the peek view: definitions or references.
/// </summary>
public enum PeekMode
{
    Definition,
    References
}

/// <summary>
/// Event args for peek navigation events.
/// </summary>
public class PeekNavigateEventArgs : EventArgs
{
    public string FilePath { get; }
    public int Line { get; }
    public int Column { get; }

    public PeekNavigateEventArgs(string filePath, int line, int column)
    {
        FilePath = filePath;
        Line = line;
        Column = column;
    }
}

/// <summary>
/// Background renderer that highlights the definition line in the peek editor.
/// </summary>
internal class PeekDefinitionLineHighlighter : IBackgroundRenderer
{
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(40, 0, 122, 204));

    /// <summary>
    /// The 1-based line number to highlight.
    /// </summary>
    public int HighlightedLine { get; set; }

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (HighlightedLine < 1) return;
        if (textView.Document == null) return;
        if (HighlightedLine > textView.Document.LineCount) return;

        var line = textView.Document.GetLineByNumber(HighlightedLine);
        var segment = new AvaloniaEdit.Document.TextSegment
        {
            StartOffset = line.Offset,
            Length = line.Length
        };

        foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
        {
            // Draw full-width highlight
            drawingContext.FillRectangle(
                HighlightBrush,
                new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
        }
    }
}
