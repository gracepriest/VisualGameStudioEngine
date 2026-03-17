using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// Inline find/replace overlay control, similar to VS Code's Ctrl+F bar.
/// Designed to be placed as an overlay at the top-right of the code editor.
/// Manages its own SearchHighlightRenderer for highlighting all matches.
/// </summary>
public partial class InlineFindReplaceControl : UserControl
{
    private TextEditor? _textEditor;
    private SearchHighlightRenderer? _searchHighlightRenderer;
    private DispatcherTimer? _debounceTimer;
    private bool _showReplace;
    private int _currentMatchIndex = -1;
    private int _totalMatches;

    /// <summary>
    /// Fired when the user closes the find bar (Escape or close button).
    /// </summary>
    public event EventHandler? CloseRequested;

    public InlineFindReplaceControl()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            PerformIncrementalSearch();
        };
    }

    /// <summary>
    /// Attaches this find bar to a TextEditor and installs the search highlight renderer.
    /// Must be called after the TextEditor is initialized.
    /// </summary>
    public void AttachToEditor(TextEditor textEditor)
    {
        if (_textEditor == textEditor) return;

        // Clean up previous attachment
        DetachFromEditor();

        _textEditor = textEditor;

        if (_textEditor?.Document != null)
        {
            _searchHighlightRenderer = new SearchHighlightRenderer(_textEditor.Document);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_searchHighlightRenderer);
        }
    }

    /// <summary>
    /// Detaches from the current TextEditor and removes the highlight renderer.
    /// </summary>
    public void DetachFromEditor()
    {
        if (_textEditor != null && _searchHighlightRenderer != null)
        {
            _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_searchHighlightRenderer);
        }
        _searchHighlightRenderer = null;
        _textEditor = null;
    }

    /// <summary>
    /// Re-attaches the search highlight renderer when the document changes.
    /// </summary>
    public void OnDocumentChanged()
    {
        if (_textEditor == null) return;

        // Remove old renderer
        if (_searchHighlightRenderer != null)
        {
            _textEditor.TextArea.TextView.BackgroundRenderers.Remove(_searchHighlightRenderer);
        }

        // Create new renderer for new document
        if (_textEditor.Document != null)
        {
            _searchHighlightRenderer = new SearchHighlightRenderer(_textEditor.Document);
            _textEditor.TextArea.TextView.BackgroundRenderers.Add(_searchHighlightRenderer);
        }

        // Re-run search if the bar is visible and has text
        if (IsVisible && !string.IsNullOrEmpty(SearchTextBox?.Text))
        {
            PerformIncrementalSearch();
        }
    }

    /// <summary>
    /// Opens the find bar in find-only mode.
    /// </summary>
    public void OpenFind()
    {
        SetReplaceVisible(false);
        IsVisible = true;
        PopulateFromSelection();
        FocusSearchBox();
        PerformIncrementalSearch();
    }

    /// <summary>
    /// Opens the find bar in find-and-replace mode.
    /// </summary>
    public void OpenFindReplace()
    {
        SetReplaceVisible(true);
        IsVisible = true;
        PopulateFromSelection();
        FocusSearchBox();
        PerformIncrementalSearch();
    }

    /// <summary>
    /// Closes the find bar and clears highlights.
    /// </summary>
    public void CloseFindBar()
    {
        IsVisible = false;
        ClearHighlights();
        _textEditor?.Focus();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Focuses the search text box and selects all text in it.
    /// </summary>
    public void FocusSearchBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var searchBox = this.FindControl<TextBox>("SearchTextBox");
            searchBox?.Focus();
            searchBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    // ---- Private: Search Logic ----

    private void PopulateFromSelection()
    {
        if (_textEditor == null) return;

        var selectedText = _textEditor.SelectedText;
        if (!string.IsNullOrWhiteSpace(selectedText) && !selectedText.Contains('\n'))
        {
            var searchBox = this.FindControl<TextBox>("SearchTextBox");
            if (searchBox != null)
            {
                searchBox.Text = selectedText;
                searchBox.SelectAll();
            }
        }
    }

    private void PerformIncrementalSearch()
    {
        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        var searchText = searchBox?.Text ?? "";
        var matchCase = MatchCaseToggle?.IsChecked == true;
        var wholeWord = WholeWordToggle?.IsChecked == true;
        var useRegex = RegexToggle?.IsChecked == true;

        if (_searchHighlightRenderer == null || _textEditor == null)
        {
            UpdateMatchDisplay(0, -1);
            return;
        }

        _totalMatches = _searchHighlightRenderer.UpdateMatches(searchText, matchCase, wholeWord, useRegex);

        if (_totalMatches > 0)
        {
            // Find the match closest to current caret position
            var caretOffset = _textEditor.CaretOffset;
            _currentMatchIndex = _searchHighlightRenderer.FindMatchIndexAtOrAfter(caretOffset);
            _searchHighlightRenderer.CurrentMatchIndex = _currentMatchIndex;

            // Select the current match in the editor
            NavigateToCurrentMatch();
        }
        else
        {
            _currentMatchIndex = -1;
            _searchHighlightRenderer.CurrentMatchIndex = -1;
        }

        UpdateMatchDisplay(_totalMatches, _currentMatchIndex);
        _textEditor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }

    private void NavigateToCurrentMatch()
    {
        if (_searchHighlightRenderer == null || _textEditor == null ||
            _currentMatchIndex < 0 || _currentMatchIndex >= _searchHighlightRenderer.MatchCount)
            return;

        var match = _searchHighlightRenderer.Matches[_currentMatchIndex];
        _textEditor.Select(match.StartOffset, match.Length);

        // Scroll to make the match visible
        var location = _textEditor.Document.GetLocation(match.StartOffset);
        _textEditor.ScrollTo(location.Line, location.Column);
    }

    private void FindNext()
    {
        if (_searchHighlightRenderer == null || _totalMatches == 0)
        {
            PerformIncrementalSearch();
            return;
        }

        _currentMatchIndex++;
        if (_currentMatchIndex >= _totalMatches)
            _currentMatchIndex = 0;

        _searchHighlightRenderer.CurrentMatchIndex = _currentMatchIndex;
        NavigateToCurrentMatch();
        UpdateMatchDisplay(_totalMatches, _currentMatchIndex);
        _textEditor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }

    private void FindPrevious()
    {
        if (_searchHighlightRenderer == null || _totalMatches == 0)
        {
            PerformIncrementalSearch();
            return;
        }

        _currentMatchIndex--;
        if (_currentMatchIndex < 0)
            _currentMatchIndex = _totalMatches - 1;

        _searchHighlightRenderer.CurrentMatchIndex = _currentMatchIndex;
        NavigateToCurrentMatch();
        UpdateMatchDisplay(_totalMatches, _currentMatchIndex);
        _textEditor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }

    private void ReplaceSingle()
    {
        if (_textEditor == null || _searchHighlightRenderer == null || _totalMatches == 0)
            return;

        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        var replaceBox = this.FindControl<TextBox>("ReplaceTextBox");
        var searchText = searchBox?.Text ?? "";
        var replaceText = replaceBox?.Text ?? "";
        var matchCase = MatchCaseToggle?.IsChecked == true;

        if (string.IsNullOrEmpty(searchText)) return;

        // Only replace if the current selection matches the search text
        var selection = _textEditor.SelectedText;
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (!string.IsNullOrEmpty(selection) && string.Equals(selection, searchText, comparison))
        {
            _textEditor.Document.Replace(_textEditor.SelectionStart, _textEditor.SelectionLength, replaceText);
        }

        // Re-search and move to next match
        PerformIncrementalSearch();
    }

    private void ReplaceAll()
    {
        if (_textEditor == null) return;

        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        var replaceBox = this.FindControl<TextBox>("ReplaceTextBox");
        var searchText = searchBox?.Text ?? "";
        var replaceText = replaceBox?.Text ?? "";
        var matchCase = MatchCaseToggle?.IsChecked == true;
        var wholeWord = WholeWordToggle?.IsChecked == true;
        var useRegex = RegexToggle?.IsChecked == true;

        if (string.IsNullOrEmpty(searchText)) return;

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
                    var options = matchCase
                        ? System.Text.RegularExpressions.RegexOptions.None
                        : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                    var regex = new System.Text.RegularExpressions.Regex(searchText, options);
                    count = regex.Matches(text).Count;
                    var newText = regex.Replace(text, replaceText);
                    document.Text = newText;
                }
                catch
                {
                    // Invalid regex
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

        // Update display
        var matchCountDisplay = this.FindControl<TextBlock>("MatchCountDisplay");
        if (matchCountDisplay != null)
        {
            matchCountDisplay.Text = $"{count} replaced";
            matchCountDisplay.IsVisible = true;
        }

        // Re-run search (should now find 0)
        Dispatcher.UIThread.Post(() => PerformIncrementalSearch(), DispatcherPriority.Background);
    }

    private void ClearHighlights()
    {
        _searchHighlightRenderer?.ClearMatches();
        _currentMatchIndex = -1;
        _totalMatches = 0;
        _textEditor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }

    private void UpdateMatchDisplay(int total, int currentIndex)
    {
        var matchCountDisplay = this.FindControl<TextBlock>("MatchCountDisplay");
        var searchBox = this.FindControl<TextBox>("SearchTextBox");
        var searchText = searchBox?.Text ?? "";

        if (matchCountDisplay == null) return;

        if (string.IsNullOrEmpty(searchText))
        {
            matchCountDisplay.IsVisible = false;
            searchBox?.Classes.Remove("hasError");
            return;
        }

        matchCountDisplay.IsVisible = true;

        if (total == 0)
        {
            matchCountDisplay.Text = "No results";
            matchCountDisplay.Foreground = Avalonia.Media.Brushes.IndianRed;
            searchBox?.Classes.Add("hasError");
        }
        else
        {
            matchCountDisplay.Text = $"{currentIndex + 1} of {total}";
            matchCountDisplay.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#808080"));
            searchBox?.Classes.Remove("hasError");
        }
    }

    private void SetReplaceVisible(bool visible)
    {
        _showReplace = visible;
        var replaceRow = this.FindControl<Grid>("ReplaceRow");
        var chevronRight = this.FindControl<TextBlock>("ChevronRight");
        var chevronDown = this.FindControl<TextBlock>("ChevronDown");

        if (replaceRow != null) replaceRow.IsVisible = visible;
        if (chevronRight != null) chevronRight.IsVisible = !visible;
        if (chevronDown != null) chevronDown.IsVisible = visible;
    }

    private static bool IsWholeWord(string text, int offset, int length)
    {
        var start = offset > 0 && char.IsLetterOrDigit(text[offset - 1]);
        var end = offset + length < text.Length && char.IsLetterOrDigit(text[offset + length]);
        return !start && !end;
    }

    // ---- Event Handlers ----

    private void OnToggleReplace(object? sender, RoutedEventArgs e)
    {
        SetReplaceVisible(!_showReplace);
        if (_showReplace)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var replaceBox = this.FindControl<TextBox>("ReplaceTextBox");
                replaceBox?.Focus();
            }, DispatcherPriority.Input);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    FindPrevious();
                else
                    FindNext();
                e.Handled = true;
                break;

            case Key.Escape:
                CloseFindBar();
                e.Handled = true;
                break;

            case Key.F3:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    FindPrevious();
                else
                    FindNext();
                e.Handled = true;
                break;
        }

        // Alt shortcuts for option toggles
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            switch (e.Key)
            {
                case Key.C:
                    if (MatchCaseToggle != null) MatchCaseToggle.IsChecked = !MatchCaseToggle.IsChecked;
                    PerformIncrementalSearch();
                    e.Handled = true;
                    break;
                case Key.W:
                    if (WholeWordToggle != null) WholeWordToggle.IsChecked = !WholeWordToggle.IsChecked;
                    PerformIncrementalSearch();
                    e.Handled = true;
                    break;
                case Key.R:
                    if (RegexToggle != null) RegexToggle.IsChecked = !RegexToggle.IsChecked;
                    PerformIncrementalSearch();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnReplaceKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    ReplaceAll();
                }
                else
                {
                    ReplaceSingle();
                }
                e.Handled = true;
                break;

            case Key.Escape:
                CloseFindBar();
                e.Handled = true;
                break;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Debounced incremental search
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnOptionChanged(object? sender, RoutedEventArgs e)
    {
        // Option toggles changed - re-run search
        Dispatcher.UIThread.Post(() => PerformIncrementalSearch(), DispatcherPriority.Input);
    }

    private void OnFindNext(object? sender, RoutedEventArgs e)
    {
        FindNext();
    }

    private void OnFindPrevious(object? sender, RoutedEventArgs e)
    {
        FindPrevious();
    }

    private void OnReplaceSingle(object? sender, RoutedEventArgs e)
    {
        ReplaceSingle();
    }

    private void OnReplaceAll(object? sender, RoutedEventArgs e)
    {
        ReplaceAll();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        CloseFindBar();
    }
}
