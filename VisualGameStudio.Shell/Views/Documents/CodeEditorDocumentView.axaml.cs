using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualGameStudio.Editor.Controls;
using VisualGameStudio.Shell.ViewModels;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Controls;

namespace VisualGameStudio.Shell.Views.Documents;

public partial class CodeEditorDocumentView : UserControl
{
    private int _currentMatchIndex;
    private int _totalMatches;

    public CodeEditorDocumentView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnViewKeyDown;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.NavigationRequested += OnNavigationRequested;
            vm.FindRequested += OnFindRequested;
            vm.ReplaceRequested += OnReplaceRequested;
            vm.ToggleCommentRequested += (s, args) => MainEditor?.ToggleLineComment();
            vm.DuplicateLineRequested += (s, args) => MainEditor?.DuplicateLine();
            vm.MoveLineUpRequested += (s, args) => MainEditor?.MoveLineUp();
            vm.MoveLineDownRequested += (s, args) => MainEditor?.MoveLineDown();
            vm.DeleteLineRequested += (s, args) => MainEditor?.DeleteLine();

            // Wire up selection info callback for extract method
            vm.GetSelectionInfo = () =>
            {
                var selectionInfo = MainEditor?.GetSelectionInfo();
                if (selectionInfo == null) return null;

                return new SelectionInfoDto
                {
                    StartLine = selectionInfo.StartLine,
                    StartColumn = selectionInfo.StartColumn,
                    EndLine = selectionInfo.EndLine,
                    EndColumn = selectionInfo.EndColumn,
                    SelectedText = selectionInfo.SelectedText
                };
            };

            // Initialize bookmark margin if service is available
            if (vm.BookmarkService != null && MainEditor != null)
            {
                MainEditor.InitializeBookmarks(vm.BookmarkService, vm.FilePath);
            }
        }
    }

    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle F3 for find next/previous even when find bar is hidden
        if (e.Key == Key.F3 && FindReplaceBar.IsVisible)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                OnFindPrevious(this, EventArgs.Empty);
            }
            else
            {
                OnFindNext(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
    }

    private void OnFindRequested(object? sender, EventArgs e)
    {
        ShowFindBar(showReplace: false);
    }

    private void OnReplaceRequested(object? sender, EventArgs e)
    {
        ShowFindBar(showReplace: true);
    }

    public void ShowFindBar(bool showReplace = false)
    {
        FindReplaceBar.IsVisible = true;
        FindReplaceBar.ShowReplace = showReplace;

        // Set initial search text from selection
        var selectedText = MainEditor?.GetSelectedTextOrWordUnderCaret();
        if (!string.IsNullOrEmpty(selectedText))
        {
            FindReplaceBar.SetInitialSearchText(selectedText);
        }

        FindReplaceBar.FocusSearchBox();
        UpdateMatchCount();
    }

    public void HideFindBar()
    {
        FindReplaceBar.IsVisible = false;
        MainEditor?.Focus();
    }

    private void OnDataTipRequested(object? sender, DataTipRequestEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestDataTipEvaluation(e.Expression, e.ScreenX, e.ScreenY);
        }
    }

    private void OnNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        MainEditor?.SetCaretPosition(e.Line, e.Column);
        MainEditor?.Focus();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Text binding handles this
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (sender is CodeEditorControl editor && DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.UpdateCaretPosition(editor.CaretLine, editor.CaretColumn);
        }
    }

    private void OnCut(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Cut();
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Copy();
    }

    private void OnPaste(object? sender, RoutedEventArgs e)
    {
        MainEditor?.Paste();
    }

    private void OnAddToWatch(object? sender, RoutedEventArgs e)
    {
        MainEditor?.RequestAddToWatch();
    }

    private void OnAddToWatchRequested(object? sender, string expression)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestAddToWatch(expression);
        }
    }

    private void OnToggleComment(object? sender, RoutedEventArgs e)
    {
        MainEditor?.ToggleLineComment();
    }

    private void OnGoToDefinition(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestGoToDefinition();
        }
    }

    private void OnFindAllReferences(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestFindAllReferences();
        }
    }

    private void OnRenameSymbol(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestRenameSymbol();
        }
    }

    private void OnExtractMethod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestExtractMethod();
        }
    }

    private void OnInlineMethod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineMethod();
        }
    }

    private void OnIntroduceVariable(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestIntroduceVariable();
        }
    }

    private void OnExtractConstant(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestExtractConstant();
        }
    }

    private void OnInlineConstant(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineConstant();
        }
    }

    private void OnInlineVariable(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineVariable();
        }
    }

    private void OnChangeSignature(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestChangeSignature();
        }
    }

    private void OnEncapsulateField(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestEncapsulateField();
        }
    }

    private void OnInlineField(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInlineField();
        }
    }

    private void OnMoveTypeToFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestMoveTypeToFile();
        }
    }

    private void OnExtractInterface(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestExtractInterface();
        }
    }

    private void OnGenerateConstructor(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestGenerateConstructor();
        }
    }

    private void OnImplementInterface(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestImplementInterface();
        }
    }

    private void OnOverrideMethod(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestOverrideMethod();
        }
    }

    private void OnAddParameter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestAddParameter();
        }
    }

    private void OnRemoveParameter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestRemoveParameter();
        }
    }

    private void OnReorderParameters(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestReorderParameters();
        }
    }

    private void OnRenameParameter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestRenameParameter();
        }
    }

    private void OnChangeParameterType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestChangeParameterType();
        }
    }

    private void OnMakeParameterOptional(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestMakeParameterOptional();
        }
    }

    private void OnMakeParameterRequired(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestMakeParameterRequired();
        }
    }

    private void OnConvertToNamedArguments(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToNamedArguments();
        }
    }

    private void OnConvertToPositionalArguments(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToPositionalArguments();
        }
    }

    private void OnSafeDelete(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestSafeDelete();
        }
    }

    private void OnPullMembersUp(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestPullMembersUp();
        }
    }

    private void OnPushMembersDown(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestPushMembersDown();
        }
    }

    private void OnUseBaseType(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestUseBaseType();
        }
    }

    private void OnConvertToInterface(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToInterface();
        }
    }

    private void OnInvertIf(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestInvertIf();
        }
    }

    private void OnConvertToSelectCase(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestConvertToSelectCase();
        }
    }

    private void OnSplitDeclaration(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestSplitDeclaration();
        }
    }

    private void OnIntroduceField(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestIntroduceField();
        }
    }

    private void OnSurroundWith(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestSurroundWith();
        }
    }

    private void OnPeekDefinition(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CodeEditorDocumentViewModel vm)
        {
            vm.RequestPeekDefinition();
        }
    }

    #region Find/Replace

    private void OnFindNext(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        var found = MainEditor.Find(
            FindReplaceBar.SearchText,
            FindReplaceBar.MatchCase,
            FindReplaceBar.WholeWord,
            FindReplaceBar.UseRegex);

        if (found)
        {
            _currentMatchIndex++;
            if (_currentMatchIndex > _totalMatches)
                _currentMatchIndex = 1;
        }

        UpdateMatchCount();
    }

    private void OnFindPrevious(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        // For find previous, we need to search backwards
        // The editor's Find method searches forward, so we implement backward search here
        var found = FindPreviousMatch();

        if (found)
        {
            _currentMatchIndex--;
            if (_currentMatchIndex < 1)
                _currentMatchIndex = _totalMatches;
        }

        UpdateMatchCount();
    }

    private bool FindPreviousMatch()
    {
        if (MainEditor == null) return false;

        var text = MainEditor.Text;
        var searchText = FindReplaceBar.SearchText;
        var comparison = FindReplaceBar.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        // Start searching from before the current selection
        var startOffset = MainEditor.CaretOffset - 1;
        if (startOffset < 0) startOffset = text.Length - 1;

        if (FindReplaceBar.UseRegex)
        {
            try
            {
                var options = FindReplaceBar.MatchCase
                    ? System.Text.RegularExpressions.RegexOptions.None
                    : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                var regex = new System.Text.RegularExpressions.Regex(searchText, options);

                // Find all matches and get the one before current position
                var matches = regex.Matches(text);
                System.Text.RegularExpressions.Match? lastMatch = null;

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Index < startOffset)
                        lastMatch = match;
                    else
                        break;
                }

                // If no match found before, wrap to end
                if (lastMatch == null && matches.Count > 0)
                    lastMatch = matches[matches.Count - 1];

                if (lastMatch != null)
                {
                    SelectInEditor(lastMatch.Index, lastMatch.Length);
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
            var foundIndex = text.LastIndexOf(searchText, startOffset, comparison);
            if (foundIndex < 0)
            {
                // Wrap to end
                foundIndex = text.LastIndexOf(searchText, comparison);
            }

            if (foundIndex >= 0)
            {
                if (FindReplaceBar.WholeWord && !IsWholeWord(text, foundIndex, searchText.Length))
                {
                    // Continue searching backwards
                    var searchStart = foundIndex - 1;
                    while (searchStart >= 0)
                    {
                        foundIndex = text.LastIndexOf(searchText, searchStart, comparison);
                        if (foundIndex < 0) break;
                        if (IsWholeWord(text, foundIndex, searchText.Length))
                        {
                            SelectInEditor(foundIndex, searchText.Length);
                            return true;
                        }
                        searchStart = foundIndex - 1;
                    }
                    return false;
                }

                SelectInEditor(foundIndex, searchText.Length);
                return true;
            }
        }

        return false;
    }

    private void SelectInEditor(int offset, int length)
    {
        if (MainEditor == null) return;

        // Use reflection or direct method if available
        // For now, use Find which will select the text
        MainEditor.Find(FindReplaceBar.SearchText, FindReplaceBar.MatchCase, FindReplaceBar.WholeWord, FindReplaceBar.UseRegex);
    }

    private bool IsWholeWord(string text, int offset, int length)
    {
        var start = offset > 0 && char.IsLetterOrDigit(text[offset - 1]);
        var end = offset + length < text.Length && char.IsLetterOrDigit(text[offset + length]);
        return !start && !end;
    }

    private void OnReplace(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        MainEditor.Replace(
            FindReplaceBar.SearchText,
            FindReplaceBar.ReplaceText ?? "",
            FindReplaceBar.MatchCase,
            FindReplaceBar.WholeWord);

        UpdateMatchCount();
    }

    private void OnReplaceAll(object? sender, EventArgs e)
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
            return;

        var count = MainEditor.ReplaceAll(
            FindReplaceBar.SearchText,
            FindReplaceBar.ReplaceText ?? "",
            FindReplaceBar.MatchCase,
            FindReplaceBar.WholeWord,
            FindReplaceBar.UseRegex);

        FindReplaceBar.MatchCountText = $"{count} replaced";
        _totalMatches = 0;
        _currentMatchIndex = 0;
    }

    private void OnCloseFindReplace(object? sender, EventArgs e)
    {
        HideFindBar();
    }

    private void OnSearchTextChanged(object? sender, EventArgs e)
    {
        UpdateMatchCount();

        // Auto-find as you type (highlight first match)
        if (!string.IsNullOrEmpty(FindReplaceBar.SearchText) && MainEditor != null)
        {
            MainEditor.Find(
                FindReplaceBar.SearchText,
                FindReplaceBar.MatchCase,
                FindReplaceBar.WholeWord,
                FindReplaceBar.UseRegex);
        }
    }

    private void UpdateMatchCount()
    {
        if (MainEditor == null || string.IsNullOrEmpty(FindReplaceBar.SearchText))
        {
            FindReplaceBar.MatchCountText = "";
            _totalMatches = 0;
            _currentMatchIndex = 0;
            return;
        }

        var text = MainEditor.Text;
        var searchText = FindReplaceBar.SearchText;

        try
        {
            if (FindReplaceBar.UseRegex)
            {
                var options = FindReplaceBar.MatchCase
                    ? System.Text.RegularExpressions.RegexOptions.None
                    : System.Text.RegularExpressions.RegexOptions.IgnoreCase;
                var regex = new System.Text.RegularExpressions.Regex(searchText, options);
                _totalMatches = regex.Matches(text).Count;
            }
            else
            {
                var comparison = FindReplaceBar.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var count = 0;
                var index = 0;

                while ((index = text.IndexOf(searchText, index, comparison)) != -1)
                {
                    if (!FindReplaceBar.WholeWord || IsWholeWord(text, index, searchText.Length))
                    {
                        count++;
                    }
                    index += searchText.Length;
                }

                _totalMatches = count;
            }

            if (_totalMatches == 0)
            {
                FindReplaceBar.MatchCountText = "No results";
                _currentMatchIndex = 0;
            }
            else if (_currentMatchIndex == 0)
            {
                _currentMatchIndex = 1;
                FindReplaceBar.MatchCountText = $"1 of {_totalMatches}";
            }
            else
            {
                FindReplaceBar.MatchCountText = $"{_currentMatchIndex} of {_totalMatches}";
            }
        }
        catch
        {
            // Invalid regex
            FindReplaceBar.MatchCountText = "Invalid pattern";
            _totalMatches = 0;
        }
    }

    #endregion
}
