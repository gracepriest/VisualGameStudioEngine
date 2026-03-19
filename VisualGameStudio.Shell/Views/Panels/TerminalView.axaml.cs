using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using VisualGameStudio.Shell.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Views.Panels;

public partial class TerminalView : UserControl
{
    private ScrollViewer? _outputScroller;
    private SelectableTextBlock? _outputTextBlock;
    private ItemsControl? _tabsItemsControl;
    private Border? _searchBar;
    private TextBox? _searchTextBox;
    private TextBlock? _searchMatchCount;

    /// <summary>
    /// Stateful ANSI parser that tracks color state across appended chunks.
    /// </summary>
    private AnsiParser _ansiParser = new();

    // Search state
    private string _lastSearchQuery = "";
    private List<SearchMatch> _searchMatches = new();
    private int _currentMatchIndex = -1;
    private bool _isSearchActive;

    /// <summary>
    /// Represents a match location within the Inlines collection.
    /// </summary>
    private record struct SearchMatch(int InlineIndex, int StartInInline, int Length);

    // Highlight colors
    private static readonly IBrush MatchHighlightBrush = new SolidColorBrush(Color.Parse("#5A4A00"));
    private static readonly IBrush CurrentMatchHighlightBrush = new SolidColorBrush(Color.Parse("#806B00"));
    private static readonly IBrush MatchBorderBrush = new SolidColorBrush(Color.Parse("#E8AB00"));

    // File link colors
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#3794FF"));
    private static readonly IBrush LinkHoverBrush = new SolidColorBrush(Color.Parse("#56AAFF"));

    public TerminalView()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _outputScroller = this.FindControl<ScrollViewer>("OutputScroller");
        _outputTextBlock = this.FindControl<SelectableTextBlock>("OutputTextBlock");
        _tabsItemsControl = this.FindControl<ItemsControl>("TabsItemsControl");
        _searchBar = this.FindControl<Border>("SearchBar");
        _searchTextBox = this.FindControl<TextBox>("SearchTextBox");
        _searchMatchCount = this.FindControl<TextBlock>("SearchMatchCount");
        var inputBox = this.FindControl<TextBox>("InputBox");

        if (inputBox != null)
        {
            inputBox.KeyDown += OnInputKeyDown;
        }

        if (_searchTextBox != null)
        {
            _searchTextBox.KeyDown += OnSearchTextBoxKeyDown;
            _searchTextBox.GetObservable(TextBox.TextProperty).Subscribe(OnSearchTextChanged);
        }

        // Handle Ctrl+F on the entire control
        KeyDown += OnTerminalKeyDown;

        if (DataContext is TerminalViewModel vm)
        {
            vm.OutputAppended += OnOutputAppended;
            vm.OutputCleared += OnOutputCleared;
            vm.ActiveSessionSwitched += OnActiveSessionSwitched;
            vm.Sessions.CollectionChanged += (_, _) => UpdateTabHighlights();
        }
    }

    private void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenSearchBar();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isSearchActive)
        {
            CloseSearchBar();
            e.Handled = true;
        }
    }

    private void OnSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                NavigateMatch(-1);
            else
                NavigateMatch(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseSearchBar();
            e.Handled = true;
        }
    }

    private void OnSearchTextChanged(string? text)
    {
        if (!_isSearchActive) return;
        PerformSearch(text ?? "");
    }

    private void OnSearchPrevClick(object? sender, RoutedEventArgs e)
    {
        NavigateMatch(-1);
    }

    private void OnSearchNextClick(object? sender, RoutedEventArgs e)
    {
        NavigateMatch(1);
    }

    private void OnSearchCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseSearchBar();
    }

    private void OpenSearchBar()
    {
        if (_searchBar == null) return;
        _isSearchActive = true;
        _searchBar.IsVisible = true;
        _searchTextBox?.Focus();
        _searchTextBox?.SelectAll();

        // If there is already text in the search box, perform search
        if (!string.IsNullOrEmpty(_searchTextBox?.Text))
        {
            PerformSearch(_searchTextBox.Text);
        }
    }

    private void CloseSearchBar()
    {
        if (_searchBar == null) return;
        _isSearchActive = false;
        _searchBar.IsVisible = false;
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _lastSearchQuery = "";

        // Remove all highlights by rebuilding inlines from the ViewModel's output text
        RebuildInlines();
    }

    /// <summary>
    /// Perform search across the terminal output text (ANSI-stripped).
    /// Rebuilds inlines with highlight Runs for matches.
    /// </summary>
    private void PerformSearch(string query)
    {
        _lastSearchQuery = query;
        _searchMatches.Clear();
        _currentMatchIndex = -1;

        if (string.IsNullOrEmpty(query) || _outputTextBlock == null)
        {
            UpdateMatchCountDisplay();
            RebuildInlines();
            return;
        }

        // Get the full plain text from the ViewModel
        var rawText = (DataContext as TerminalViewModel)?.OutputText ?? "";
        var plainText = AnsiParser.StripAnsi(rawText);

        // Find all match positions in the plain text
        var matchPositions = new List<(int Start, int Length)>();
        int searchFrom = 0;
        while (searchFrom <= plainText.Length - query.Length)
        {
            int idx = plainText.IndexOf(query, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;
            matchPositions.Add((idx, query.Length));
            searchFrom = idx + 1;
        }

        if (matchPositions.Count > 0)
        {
            _currentMatchIndex = 0;
        }

        // Rebuild inlines with highlights
        RebuildInlinesWithHighlights(rawText, plainText, matchPositions);

        // Store matches for navigation (we track them by plain-text offset)
        for (int i = 0; i < matchPositions.Count; i++)
        {
            _searchMatches.Add(new SearchMatch(0, matchPositions[i].Start, matchPositions[i].Length));
        }

        UpdateMatchCountDisplay();
    }

    /// <summary>
    /// Navigate to the next or previous match.
    /// </summary>
    private void NavigateMatch(int direction)
    {
        if (_searchMatches.Count == 0) return;

        _currentMatchIndex += direction;
        if (_currentMatchIndex >= _searchMatches.Count)
            _currentMatchIndex = 0;
        else if (_currentMatchIndex < 0)
            _currentMatchIndex = _searchMatches.Count - 1;

        // Rebuild highlights to show current match differently
        var rawText = (DataContext as TerminalViewModel)?.OutputText ?? "";
        var plainText = AnsiParser.StripAnsi(rawText);

        var matchPositions = new List<(int Start, int Length)>();
        foreach (var m in _searchMatches)
            matchPositions.Add((m.StartInInline, m.Length));

        RebuildInlinesWithHighlights(rawText, plainText, matchPositions);
        UpdateMatchCountDisplay();
    }

    /// <summary>
    /// Rebuilds the output inlines from the ViewModel's raw text, with no highlights.
    /// </summary>
    private void RebuildInlines()
    {
        if (_outputTextBlock == null) return;

        _outputTextBlock.Inlines?.Clear();
        _outputTextBlock.Inlines ??= new InlineCollection();
        _ansiParser.Reset();

        var rawText = (DataContext as TerminalViewModel)?.OutputText ?? "";
        if (string.IsNullOrEmpty(rawText)) return;

        var segments = _ansiParser.Parse(rawText);
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text)) continue;

            // Scan for file path links in rebuild too
            var linkSegments = TerminalLinkDetector.Detect(segment.Text);

            if (linkSegments.Count == 0)
            {
                AddRun(segment.Text, segment.Foreground, segment.Bold);
                continue;
            }

            foreach (var link in linkSegments)
            {
                if (link.IsLink)
                    AddFileLink(link, segment.Bold);
                else
                    AddRun(link.Text, segment.Foreground, segment.Bold);
            }
        }
    }

    /// <summary>
    /// Rebuilds inlines from raw ANSI text, splitting Runs at match boundaries and
    /// applying highlight backgrounds to matched segments.
    /// </summary>
    private void RebuildInlinesWithHighlights(string rawText, string plainText,
        List<(int Start, int Length)> matchPositions)
    {
        if (_outputTextBlock == null) return;

        _outputTextBlock.Inlines?.Clear();
        _outputTextBlock.Inlines ??= new InlineCollection();

        if (string.IsNullOrEmpty(rawText) || matchPositions.Count == 0)
        {
            // No matches - just rebuild normally
            _ansiParser.Reset();
            var segs = _ansiParser.Parse(rawText);
            foreach (var seg in segs)
            {
                if (string.IsNullOrEmpty(seg.Text)) continue;
                var r = new Run(seg.Text);
                if (seg.Foreground != null) r.Foreground = seg.Foreground;
                if (seg.Bold) r.FontWeight = FontWeight.Bold;
                _outputTextBlock.Inlines.Add(r);
            }
            return;
        }

        // Build a set of highlight ranges on the plain text
        var highlights = new bool[plainText.Length];
        var currentHighlight = new bool[plainText.Length];
        foreach (var (start, length) in matchPositions)
        {
            for (int i = start; i < start + length && i < highlights.Length; i++)
                highlights[i] = true;
        }

        // Mark the current match specifically
        if (_currentMatchIndex >= 0 && _currentMatchIndex < matchPositions.Count)
        {
            var (cStart, cLen) = matchPositions[_currentMatchIndex];
            for (int i = cStart; i < cStart + cLen && i < currentHighlight.Length; i++)
                currentHighlight[i] = true;
        }

        // Parse ANSI segments (these map to plain text positions sequentially)
        _ansiParser.Reset();
        var segments = _ansiParser.Parse(rawText);

        int plainPos = 0;
        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text)) continue;

            var text = segment.Text;
            int segStart = plainPos;
            int segEnd = segStart + text.Length;
            plainPos = segEnd;

            // Split this segment into sub-runs at highlight boundaries
            int i = 0;
            while (i < text.Length)
            {
                int globalPos = segStart + i;
                bool isHighlighted = globalPos < highlights.Length && highlights[globalPos];
                bool isCurrent = globalPos < currentHighlight.Length && currentHighlight[globalPos];

                // Find the end of this contiguous region (same highlight state)
                int j = i + 1;
                while (j < text.Length)
                {
                    int gp = segStart + j;
                    bool hl = gp < highlights.Length && highlights[gp];
                    bool cur = gp < currentHighlight.Length && currentHighlight[gp];
                    if (hl != isHighlighted || cur != isCurrent) break;
                    j++;
                }

                var subText = text.Substring(i, j - i);
                var run = new Run(subText);

                // Apply original ANSI foreground
                if (segment.Foreground != null)
                    run.Foreground = segment.Foreground;
                if (segment.Bold)
                    run.FontWeight = FontWeight.Bold;

                // Apply highlight background for matches
                if (isHighlighted)
                {
                    if (isCurrent)
                    {
                        // Current match: brighter highlight + border-like effect via distinct foreground
                        run.Background = CurrentMatchHighlightBrush;
                        // Make text more visible on current match
                        run.Foreground = new SolidColorBrush(Color.Parse("#FFFFFF"));
                    }
                    else
                    {
                        run.Background = MatchHighlightBrush;
                    }
                }

                _outputTextBlock.Inlines!.Add(run);
                i = j;
            }
        }

        // Scroll to bring the current match into view (approximate based on text position)
        ScrollToCurrentMatch(plainText);
    }

    /// <summary>
    /// Approximate scroll to bring the current match into view.
    /// </summary>
    private void ScrollToCurrentMatch(string plainText)
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;
        if (_outputScroller == null || _outputTextBlock == null) return;

        var match = _searchMatches[_currentMatchIndex];

        // Estimate the vertical position: count newlines before the match
        int newlineCount = 0;
        for (int i = 0; i < match.StartInInline && i < plainText.Length; i++)
        {
            if (plainText[i] == '\n') newlineCount++;
        }

        // Approximate line height (13px font + some padding)
        double lineHeight = 17.0;
        double estimatedY = newlineCount * lineHeight;

        // Scroll so the match is roughly centered
        double viewportHeight = _outputScroller.Viewport.Height;
        double targetOffset = Math.Max(0, estimatedY - viewportHeight / 2);
        _outputScroller.Offset = new Vector(_outputScroller.Offset.X, targetOffset);
    }

    private void UpdateMatchCountDisplay()
    {
        if (_searchMatchCount == null) return;

        if (_searchMatches.Count == 0)
        {
            _searchMatchCount.Text = string.IsNullOrEmpty(_lastSearchQuery) ? "" : "No results";
            _searchMatchCount.Foreground = new SolidColorBrush(Color.Parse("#808080"));
        }
        else
        {
            _searchMatchCount.Text = $"{_currentMatchIndex + 1} of {_searchMatches.Count}";
            _searchMatchCount.Foreground = new SolidColorBrush(Color.Parse("#CCCCCC"));
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is TerminalViewModel vm)
        {
            vm.SendInputCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Opens the shell profile dropdown popup.
    /// </summary>
    private void OnProfileDropdownClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalViewModel vm)
        {
            vm.IsProfileDropdownOpen = !vm.IsProfileDropdownOpen;
        }
    }

    /// <summary>
    /// Handles clicking a shell profile item in the dropdown.
    /// Creates a new terminal session with the selected profile.
    /// </summary>
    private void OnProfileItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ShellProfile profile
            && DataContext is TerminalViewModel vm)
        {
            vm.CreateSessionWithProfileCommand.Execute(profile);
        }
    }

    /// <summary>
    /// Handle clicking a tab to switch the active terminal session.
    /// </summary>
    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is TerminalSession session
            && DataContext is TerminalViewModel vm)
        {
            vm.ActiveSession = session;
            UpdateTabHighlights();
        }
    }

    /// <summary>
    /// Handle clicking the close button on a tab.
    /// </summary>
    private void OnCloseTabClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TerminalSession session
            && DataContext is TerminalViewModel vm)
        {
            vm.CloseSessionCommand.Execute(session);
        }
    }

    private void OnActiveSessionSwitched(object? sender, EventArgs e)
    {
        UpdateTabHighlights();

        // If search is active, re-run search on the new session's output
        if (_isSearchActive && !string.IsNullOrEmpty(_lastSearchQuery))
        {
            PerformSearch(_lastSearchQuery);
        }
    }

    /// <summary>
    /// Updates the visual highlight on each tab border to show which is active.
    /// </summary>
    private void UpdateTabHighlights()
    {
        if (_tabsItemsControl == null || DataContext is not TerminalViewModel vm) return;

        // Walk all rendered tab borders and set background based on active state
        foreach (var container in _tabsItemsControl.GetVisualDescendants())
        {
            if (container is Border border && border.Name == "TabBorder"
                && border.DataContext is TerminalSession session)
            {
                border.Background = session == vm.ActiveSession
                    ? new SolidColorBrush(Color.Parse("#3C3C3C"))
                    : new SolidColorBrush(Color.Parse("#2D2D2D"));

                // Add a bottom accent line for the active tab
                border.BorderThickness = session == vm.ActiveSession
                    ? new Thickness(0, 0, 0, 2)
                    : new Thickness(0);

                border.BorderBrush = session == vm.ActiveSession
                    ? new SolidColorBrush(Color.Parse("#569CD6"))
                    : null;
            }
        }
    }

    private void OnOutputCleared()
    {
        if (_outputTextBlock == null) return;

        _outputTextBlock.Inlines?.Clear();
        _ansiParser.Reset();
    }

    private void OnOutputAppended(string text)
    {
        if (_outputTextBlock == null || string.IsNullOrEmpty(text)) return;

        // If search is active, rebuild all inlines with highlights (new output may contain matches)
        if (_isSearchActive && !string.IsNullOrEmpty(_lastSearchQuery))
        {
            PerformSearch(_lastSearchQuery);
            return;
        }

        _outputTextBlock.Inlines ??= new InlineCollection();

        var segments = _ansiParser.Parse(text);

        foreach (var segment in segments)
        {
            if (string.IsNullOrEmpty(segment.Text))
                continue;

            // Scan each ANSI segment for file path links
            var linkSegments = TerminalLinkDetector.Detect(segment.Text);

            if (linkSegments.Count == 0)
            {
                // No links detected - add as plain run
                AddRun(segment.Text, segment.Foreground, segment.Bold);
                continue;
            }

            foreach (var link in linkSegments)
            {
                if (link.IsLink)
                    AddFileLink(link, segment.Bold);
                else
                    AddRun(link.Text, segment.Foreground, segment.Bold);
            }
        }

        // Auto-scroll to bottom
        _outputScroller?.ScrollToEnd();
    }

    /// <summary>
    /// Adds a plain text Run to the output.
    /// </summary>
    private void AddRun(string text, IBrush? foreground, bool bold)
    {
        var run = new Run(text);
        if (foreground != null)
            run.Foreground = foreground;
        if (bold)
            run.FontWeight = FontWeight.Bold;
        _outputTextBlock!.Inlines!.Add(run);
    }

    /// <summary>
    /// Adds a clickable file path link as an InlineUIContainer.
    /// The link is underlined, colored blue, and changes on hover.
    /// Clicking opens the file at the specified line/column.
    /// </summary>
    private void AddFileLink(TerminalLinkDetector.LinkSegment link, bool bold)
    {
        var linkText = new TextBlock
        {
            Text = link.Text,
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            FontSize = 13,
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = link, // Store link data for the click handler
        };

        if (bold)
            linkText.FontWeight = FontWeight.Bold;

        // Hover effects
        linkText.PointerEntered += (s, _) =>
        {
            if (s is TextBlock tb)
                tb.Foreground = LinkHoverBrush;
        };

        linkText.PointerExited += (s, _) =>
        {
            if (s is TextBlock tb)
                tb.Foreground = LinkBrush;
        };

        // Click to navigate
        linkText.PointerPressed += OnFileLinkPressed;

        var container = new InlineUIContainer
        {
            Child = linkText,
        };

        _outputTextBlock!.Inlines!.Add(container);
    }

    /// <summary>
    /// Handles clicking on a file path link. Opens the file at the specified line/column.
    /// </summary>
    private void OnFileLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TextBlock tb || tb.Tag is not TerminalLinkDetector.LinkSegment link)
            return;

        if (DataContext is not TerminalViewModel vm)
            return;

        var point = e.GetCurrentPoint(tb);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        // Resolve relative paths against the terminal working directory
        var filePath = link.FilePath;
        if (!Path.IsPathRooted(filePath))
        {
            var workDir = vm.WorkingDirectory;
            if (!string.IsNullOrEmpty(workDir))
            {
                var fullPath = Path.Combine(workDir, filePath);
                if (File.Exists(fullPath))
                    filePath = Path.GetFullPath(fullPath);
            }
        }

        // Normalize path separators
        filePath = filePath.Replace('/', Path.DirectorySeparatorChar);

        vm.NavigateToFile(filePath, link.Line, link.Column);
        e.Handled = true;
    }
}
