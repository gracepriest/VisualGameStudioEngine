using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using VisualGameStudio.Editor.TextMarkers;
using System.Text.RegularExpressions;

namespace VisualGameStudio.Editor.Controls;

/// <summary>
/// VS Code-style inline find/replace overlay control.
/// Positioned at the top-right of the code editor. Supports:
/// - Incremental search with debounce
/// - Match Case, Whole Word, Regex toggles
/// - Find in Selection
/// - Preserve Case replacement
/// - Multi-line search (Ctrl+Enter inserts newline in search box)
/// - Recent search history (Up/Down arrows)
/// - Select All Matches (Alt+Enter)
/// - Regex capture group replacement ($1, $2, etc.)
/// </summary>
public partial class InlineFindReplaceControl : UserControl
{
    private TextEditor? _textEditor;
    private SearchHighlightRenderer? _searchHighlightRenderer;
    private DispatcherTimer? _debounceTimer;
    private bool _showReplace;
    private int _currentMatchIndex = -1;
    private int _totalMatches;

    // Search in selection
    private bool _findInSelection;
    private int _selectionStartOffset;
    private int _selectionEndOffset;

    // Recent search history
    private static readonly List<string> _searchHistory = new(20);
    private static readonly List<string> _replaceHistory = new(20);
    private int _searchHistoryIndex = -1;
    private int _replaceHistoryIndex = -1;

    // Multi-line search state
    private bool _isMultilineSearch;

    /// <summary>
    /// Fired when the user closes the find bar (Escape or close button).
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Fired when Alt+Enter is pressed to select all matches.
    /// The event args carry the list of match offsets/lengths.
    /// </summary>
    public event EventHandler<SelectAllMatchesEventArgs>? SelectAllMatchesRequested;

    /// <summary>
    /// Gets the last search text used, for F3/Shift+F3 when the widget is hidden.
    /// </summary>
    public string LastSearchText { get; private set; } = "";

    /// <summary>
    /// Gets whether Match Case was last enabled.
    /// </summary>
    public bool LastMatchCase { get; private set; }

    /// <summary>
    /// Gets whether Whole Word was last enabled.
    /// </summary>
    public bool LastWholeWord { get; private set; }

    /// <summary>
    /// Gets whether Regex was last enabled.
    /// </summary>
    public bool LastUseRegex { get; private set; }

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
        _findInSelection = false;
        if (FindInSelectionToggle != null)
            FindInSelectionToggle.IsChecked = false;
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
            SearchTextBox?.Focus();
            SearchTextBox?.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// Performs a FindNext using the last search parameters.
    /// Called externally from CodeEditorControl for F3 key handling.
    /// </summary>
    public void FindNextExternal()
    {
        if (string.IsNullOrEmpty(LastSearchText)) return;

        // If widget not visible, temporarily run the search
        if (!IsVisible)
        {
            EnsureSearchActive();
        }
        FindNext();
    }

    /// <summary>
    /// Performs a FindPrevious using the last search parameters.
    /// Called externally from CodeEditorControl for Shift+F3 key handling.
    /// </summary>
    public void FindPreviousExternal()
    {
        if (string.IsNullOrEmpty(LastSearchText)) return;

        if (!IsVisible)
        {
            EnsureSearchActive();
        }
        FindPrevious();
    }

    // ---- Private: Search Logic ----

    private void EnsureSearchActive()
    {
        if (_searchHighlightRenderer == null || _textEditor == null) return;
        if (_totalMatches > 0) return; // Already have results

        _totalMatches = _searchHighlightRenderer.UpdateMatches(
            LastSearchText, LastMatchCase, LastWholeWord, LastUseRegex);

        if (_totalMatches > 0)
        {
            var caretOffset = _textEditor.CaretOffset;
            _currentMatchIndex = _searchHighlightRenderer.FindMatchIndexAtOrAfter(caretOffset);
            _searchHighlightRenderer.CurrentMatchIndex = _currentMatchIndex;
        }
    }

    private void PopulateFromSelection()
    {
        if (_textEditor == null) return;

        var selectedText = _textEditor.SelectedText;
        if (!string.IsNullOrWhiteSpace(selectedText) && !selectedText.Contains('\n'))
        {
            if (SearchTextBox != null)
            {
                SearchTextBox.Text = selectedText;
                SearchTextBox.SelectAll();
            }
        }
    }

    private void PerformIncrementalSearch()
    {
        var searchText = SearchTextBox?.Text ?? "";
        var matchCase = MatchCaseToggle?.IsChecked == true;
        var wholeWord = WholeWordToggle?.IsChecked == true;
        var useRegex = RegexToggle?.IsChecked == true;

        // Save last search parameters for F3/Shift+F3
        if (!string.IsNullOrEmpty(searchText))
        {
            LastSearchText = searchText;
            LastMatchCase = matchCase;
            LastWholeWord = wholeWord;
            LastUseRegex = useRegex;
        }

        if (_searchHighlightRenderer == null || _textEditor == null)
        {
            UpdateMatchDisplay(0, -1);
            return;
        }

        // Configure selection range for Find in Selection
        if (_findInSelection)
        {
            _searchHighlightRenderer.SetSelectionRange(_selectionStartOffset, _selectionEndOffset);
        }
        else
        {
            _searchHighlightRenderer.ClearSelectionRange();
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
            _currentMatchIndex = 0; // Wrap around

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
            _currentMatchIndex = _totalMatches - 1; // Wrap around

        _searchHighlightRenderer.CurrentMatchIndex = _currentMatchIndex;
        NavigateToCurrentMatch();
        UpdateMatchDisplay(_totalMatches, _currentMatchIndex);
        _textEditor?.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
    }

    private void ReplaceSingle()
    {
        if (_textEditor == null || _searchHighlightRenderer == null || _totalMatches == 0)
            return;

        var searchText = SearchTextBox?.Text ?? "";
        var replaceText = ReplaceTextBox?.Text ?? "";
        var matchCase = MatchCaseToggle?.IsChecked == true;
        var useRegex = RegexToggle?.IsChecked == true;
        var preserveCase = PreserveCaseToggle?.IsChecked == true;

        if (string.IsNullOrEmpty(searchText)) return;

        // Add to replace history
        AddToHistory(_replaceHistory, replaceText);

        // Only replace if the current selection matches the search text
        var selection = _textEditor.SelectedText;
        if (string.IsNullOrEmpty(selection)) return;

        bool isMatch;
        if (useRegex)
        {
            try
            {
                var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                var regex = new Regex("^" + searchText + "$", options);
                isMatch = regex.IsMatch(selection);
                if (isMatch)
                {
                    var replaced = regex.Replace(selection, replaceText);
                    if (preserveCase)
                        replaced = ApplyPreserveCase(selection, replaced);
                    _textEditor.Document.Replace(_textEditor.SelectionStart, _textEditor.SelectionLength, replaced);
                }
            }
            catch
            {
                return;
            }
        }
        else
        {
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            isMatch = string.Equals(selection, searchText, comparison);
            if (isMatch)
            {
                var replaced = preserveCase ? ApplyPreserveCase(selection, replaceText) : replaceText;
                _textEditor.Document.Replace(_textEditor.SelectionStart, _textEditor.SelectionLength, replaced);
            }
        }

        // Re-search and move to next match
        PerformIncrementalSearch();
    }

    private void ReplaceAll()
    {
        if (_textEditor == null) return;

        var searchText = SearchTextBox?.Text ?? "";
        var replaceText = ReplaceTextBox?.Text ?? "";
        var matchCase = MatchCaseToggle?.IsChecked == true;
        var wholeWord = WholeWordToggle?.IsChecked == true;
        var useRegex = RegexToggle?.IsChecked == true;
        var preserveCase = PreserveCaseToggle?.IsChecked == true;

        if (string.IsNullOrEmpty(searchText)) return;

        // Add to replace history
        AddToHistory(_replaceHistory, replaceText);

        var document = _textEditor.Document;
        var text = document.Text;
        var count = 0;

        // Determine search range
        var rangeStart = _findInSelection ? _selectionStartOffset : 0;
        var rangeEnd = _findInSelection ? _selectionEndOffset : text.Length;

        document.BeginUpdate();
        try
        {
            if (useRegex)
            {
                try
                {
                    var options = matchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
                    var regex = new Regex(searchText, options);
                    var searchRegion = text.Substring(rangeStart, rangeEnd - rangeStart);

                    if (preserveCase)
                    {
                        // Need to iterate matches for preserve-case
                        var matches = regex.Matches(searchRegion);
                        count = matches.Count;
                        var offset = 0;
                        foreach (Match m in matches)
                        {
                            var replaced = regex.Replace(m.Value, replaceText);
                            replaced = ApplyPreserveCase(m.Value, replaced);
                            var absPos = rangeStart + m.Index + offset;
                            document.Replace(absPos, m.Length, replaced);
                            offset += replaced.Length - m.Length;
                        }
                    }
                    else
                    {
                        count = regex.Matches(searchRegion).Count;
                        var newText = regex.Replace(searchRegion, replaceText);
                        document.Replace(rangeStart, rangeEnd - rangeStart, newText);
                    }
                }
                catch
                {
                    // Invalid regex
                }
            }
            else
            {
                var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var offset = rangeStart;

                while (offset < rangeEnd + (count * (replaceText.Length - searchText.Length)))
                {
                    var currentText = document.Text;
                    var foundIndex = currentText.IndexOf(searchText, offset, comparison);
                    if (foundIndex < 0) break;

                    // Check if still within range (adjusted for previous replacements)
                    var adjustedEnd = rangeEnd + (count * (replaceText.Length - searchText.Length));
                    if (foundIndex >= adjustedEnd) break;

                    if (!wholeWord || IsWholeWord(currentText, foundIndex, searchText.Length))
                    {
                        var matchedText = currentText.Substring(foundIndex, searchText.Length);
                        var replacement = preserveCase
                            ? ApplyPreserveCase(matchedText, replaceText)
                            : replaceText;
                        document.Replace(foundIndex, searchText.Length, replacement);
                        offset = foundIndex + replacement.Length;
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
        if (MatchCountDisplay != null)
        {
            MatchCountDisplay.Text = $"{count} replaced";
            MatchCountDisplay.IsVisible = true;
        }

        // Re-run search (should now find 0 or updated matches)
        Dispatcher.UIThread.Post(() => PerformIncrementalSearch(), DispatcherPriority.Background);
    }

    /// <summary>
    /// Applies preserve-case logic: if the original was "Foo", replacement "bar" becomes "Bar".
    /// If "FOO", becomes "BAR". If "foo", stays "bar".
    /// </summary>
    private static string ApplyPreserveCase(string original, string replacement)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(replacement))
            return replacement;

        // All uppercase: FOO -> BAR
        if (original == original.ToUpperInvariant())
            return replacement.ToUpperInvariant();

        // All lowercase: foo -> bar
        if (original == original.ToLowerInvariant())
            return replacement.ToLowerInvariant();

        // Title case (first letter upper): Foo -> Bar
        if (char.IsUpper(original[0]) && (original.Length == 1 || original.Substring(1) == original.Substring(1).ToLowerInvariant()))
        {
            return char.ToUpperInvariant(replacement[0]) + (replacement.Length > 1 ? replacement.Substring(1).ToLowerInvariant() : "");
        }

        // Mixed case or other patterns: return as-is
        return replacement;
    }

    private void SelectAllMatches()
    {
        if (_searchHighlightRenderer == null || _totalMatches == 0) return;

        var matches = _searchHighlightRenderer.Matches;
        var matchData = new List<(int Offset, int Length)>();
        foreach (var m in matches)
        {
            matchData.Add((m.StartOffset, m.Length));
        }

        SelectAllMatchesRequested?.Invoke(this, new SelectAllMatchesEventArgs(matchData));
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
        var searchText = SearchTextBox?.Text ?? "";

        if (MatchCountDisplay == null) return;

        if (string.IsNullOrEmpty(searchText))
        {
            MatchCountDisplay.IsVisible = false;
            SearchTextBox?.Classes.Remove("hasError");
            return;
        }

        MatchCountDisplay.IsVisible = true;

        if (total == 0)
        {
            MatchCountDisplay.Text = "No results";
            MatchCountDisplay.Foreground = Avalonia.Media.Brushes.IndianRed;
            SearchTextBox?.Classes.Add("hasError");
        }
        else
        {
            MatchCountDisplay.Text = $"{currentIndex + 1} of {total}";
            MatchCountDisplay.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#808080"));
            SearchTextBox?.Classes.Remove("hasError");
        }
    }

    private void SetReplaceVisible(bool visible)
    {
        _showReplace = visible;
        if (ReplaceRow != null) ReplaceRow.IsVisible = visible;
        if (ChevronRight != null) ChevronRight.IsVisible = !visible;
        if (ChevronDown != null) ChevronDown.IsVisible = visible;
    }

    private static bool IsWholeWord(string text, int offset, int length)
    {
        var start = offset > 0 && char.IsLetterOrDigit(text[offset - 1]);
        var end = offset + length < text.Length && char.IsLetterOrDigit(text[offset + length]);
        return !start && !end;
    }

    // ---- Search History ----

    private static void AddToHistory(List<string> history, string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Remove duplicate if exists
        history.Remove(text);

        // Add to front
        history.Insert(0, text);

        // Cap at 20
        while (history.Count > 20)
            history.RemoveAt(history.Count - 1);
    }

    private void NavigateSearchHistory(bool up)
    {
        if (_searchHistory.Count == 0) return;

        if (up)
        {
            _searchHistoryIndex = Math.Min(_searchHistoryIndex + 1, _searchHistory.Count - 1);
        }
        else
        {
            _searchHistoryIndex = Math.Max(_searchHistoryIndex - 1, -1);
        }

        if (_searchHistoryIndex >= 0 && _searchHistoryIndex < _searchHistory.Count)
        {
            if (SearchTextBox != null)
                SearchTextBox.Text = _searchHistory[_searchHistoryIndex];
        }
        else if (_searchHistoryIndex < 0 && SearchTextBox != null)
        {
            SearchTextBox.Text = "";
        }
    }

    private void NavigateReplaceHistory(bool up)
    {
        if (_replaceHistory.Count == 0) return;

        if (up)
        {
            _replaceHistoryIndex = Math.Min(_replaceHistoryIndex + 1, _replaceHistory.Count - 1);
        }
        else
        {
            _replaceHistoryIndex = Math.Max(_replaceHistoryIndex - 1, -1);
        }

        if (_replaceHistoryIndex >= 0 && _replaceHistoryIndex < _replaceHistory.Count)
        {
            if (ReplaceTextBox != null)
                ReplaceTextBox.Text = _replaceHistory[_replaceHistoryIndex];
        }
        else if (_replaceHistoryIndex < 0 && ReplaceTextBox != null)
        {
            ReplaceTextBox.Text = "";
        }
    }

    // ---- Multi-line search helpers ----

    private void EnableMultilineSearch()
    {
        _isMultilineSearch = true;
        if (SearchTextBox != null)
        {
            SearchTextBox.AcceptsReturn = true;
            SearchTextBox.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            SearchTextBox.MaxHeight = 120;
        }
    }

    private void DisableMultilineSearch()
    {
        _isMultilineSearch = false;
        if (SearchTextBox != null)
        {
            SearchTextBox.AcceptsReturn = false;
            SearchTextBox.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
            SearchTextBox.MaxHeight = 26;
        }
    }

    // ---- Event Handlers ----

    private void OnToggleReplace(object? sender, RoutedEventArgs e)
    {
        SetReplaceVisible(!_showReplace);
        if (_showReplace)
        {
            Dispatcher.UIThread.Post(() =>
            {
                ReplaceTextBox?.Focus();
            }, DispatcherPriority.Input);
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Enter = insert newline for multi-line search
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            EnableMultilineSearch();
            if (SearchTextBox != null)
            {
                var caretIndex = SearchTextBox.CaretIndex;
                var text = SearchTextBox.Text ?? "";
                SearchTextBox.Text = text.Insert(caretIndex, "\n");
                SearchTextBox.CaretIndex = caretIndex + 1;
            }
            e.Handled = true;
            return;
        }

        // Alt+Enter = Select All Matches
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            // Add to search history
            AddToHistory(_searchHistory, SearchTextBox?.Text ?? "");
            SelectAllMatches();
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // Add to search history
                AddToHistory(_searchHistory, SearchTextBox?.Text ?? "");
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

            case Key.Up:
                if (!_isMultilineSearch)
                {
                    NavigateSearchHistory(true);
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (!_isMultilineSearch)
                {
                    NavigateSearchHistory(false);
                    e.Handled = true;
                }
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
                case Key.L:
                    ToggleFindInSelection();
                    e.Handled = true;
                    break;
                case Key.P:
                    if (PreserveCaseToggle != null) PreserveCaseToggle.IsChecked = !PreserveCaseToggle.IsChecked;
                    e.Handled = true;
                    break;
            }
        }
    }

    private void OnReplaceKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Enter = insert newline in replace box
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ReplaceTextBox != null)
            {
                // Allow multiline in replace box
                ReplaceTextBox.AcceptsReturn = true;
                var caretIndex = ReplaceTextBox.CaretIndex;
                var text = ReplaceTextBox.Text ?? "";
                ReplaceTextBox.Text = text.Insert(caretIndex, "\n");
                ReplaceTextBox.CaretIndex = caretIndex + 1;
            }
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
                {
                    AddToHistory(_replaceHistory, ReplaceTextBox?.Text ?? "");
                    ReplaceAll();
                }
                else
                {
                    AddToHistory(_replaceHistory, ReplaceTextBox?.Text ?? "");
                    ReplaceSingle();
                }
                e.Handled = true;
                break;

            case Key.Escape:
                CloseFindBar();
                e.Handled = true;
                break;

            case Key.Up:
                NavigateReplaceHistory(true);
                e.Handled = true;
                break;

            case Key.Down:
                NavigateReplaceHistory(false);
                e.Handled = true;
                break;
        }

        // Alt+P to toggle preserve case
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) && e.Key == Key.P)
        {
            if (PreserveCaseToggle != null) PreserveCaseToggle.IsChecked = !PreserveCaseToggle.IsChecked;
            e.Handled = true;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchHistoryIndex = -1; // Reset history navigation

        // Check if multiline content was removed
        var text = SearchTextBox?.Text ?? "";
        if (!text.Contains('\n') && _isMultilineSearch)
        {
            DisableMultilineSearch();
        }

        // Debounced incremental search
        _debounceTimer?.Stop();
        _debounceTimer?.Start();
    }

    private void OnOptionChanged(object? sender, RoutedEventArgs e)
    {
        // Option toggles changed - re-run search
        Dispatcher.UIThread.Post(() => PerformIncrementalSearch(), DispatcherPriority.Input);
    }

    private void OnFindInSelectionChanged(object? sender, RoutedEventArgs e)
    {
        ToggleFindInSelection();
    }

    private void ToggleFindInSelection()
    {
        _findInSelection = FindInSelectionToggle?.IsChecked == true;

        if (_findInSelection && _textEditor != null)
        {
            // Capture current selection as the search range
            _selectionStartOffset = _textEditor.SelectionStart;
            _selectionEndOffset = _textEditor.SelectionStart + _textEditor.SelectionLength;

            // Need at least some selection
            if (_selectionStartOffset == _selectionEndOffset)
            {
                _findInSelection = false;
                if (FindInSelectionToggle != null)
                    FindInSelectionToggle.IsChecked = false;
                return;
            }
        }

        PerformIncrementalSearch();
    }

    private void OnFindNext(object? sender, RoutedEventArgs e)
    {
        AddToHistory(_searchHistory, SearchTextBox?.Text ?? "");
        FindNext();
    }

    private void OnFindPrevious(object? sender, RoutedEventArgs e)
    {
        AddToHistory(_searchHistory, SearchTextBox?.Text ?? "");
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

    private void OnSelectAllMatches(object? sender, RoutedEventArgs e)
    {
        SelectAllMatches();
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        CloseFindBar();
    }
}

/// <summary>
/// Event args for the SelectAllMatchesRequested event.
/// </summary>
public class SelectAllMatchesEventArgs : EventArgs
{
    public IReadOnlyList<(int Offset, int Length)> Matches { get; }

    public SelectAllMatchesEventArgs(IReadOnlyList<(int Offset, int Length)> matches)
    {
        Matches = matches;
    }
}
