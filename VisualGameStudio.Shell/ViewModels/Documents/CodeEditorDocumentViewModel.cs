using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.ViewModels;

namespace VisualGameStudio.Shell.ViewModels.Documents;

public partial class CodeEditorDocumentViewModel : Document, IDocumentViewModel
{
    private readonly IFileService _fileService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IBookmarkService? _bookmarkService;
    private string _originalText = "";

    /// <summary>
    /// The TextDocument that holds the text content and undo history.
    /// Using this instead of Text preserves undo when switching tabs.
    /// </summary>
    [ObservableProperty]
    private TextDocument _textDocument = new();

    [ObservableProperty]
    private string _text = "";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    [ObservableProperty]
    private int _totalLines = 1;

    [ObservableProperty]
    private bool _isSplitView;

    [ObservableProperty]
    private SplitOrientation _splitOrientation = SplitOrientation.Horizontal;

    public new string Id => FilePath ?? Guid.NewGuid().ToString();
    public new string Title => GetTitle();
    public new bool CanClose => true;

    public event EventHandler? DirtyChanged;
    public event EventHandler? TitleChanged;
    public event EventHandler? CaretPositionChanged;
    public event EventHandler<string>? TextChanged;
    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler<string>? AddToWatchRequested;
    public event EventHandler<DataTipEvaluationRequestEventArgs>? DataTipEvaluationRequested;
    public event EventHandler? FindRequested;
    public event EventHandler? ReplaceRequested;
    public event EventHandler? GoToDefinitionRequested;
    public event EventHandler? FindAllReferencesRequested;
    public event EventHandler? ToggleCommentRequested;
    public event EventHandler? DuplicateLineRequested;
    public event EventHandler? MoveLineUpRequested;
    public event EventHandler? MoveLineDownRequested;
    public event EventHandler? DeleteLineRequested;
    public event EventHandler? RenameSymbolRequested;
    public event EventHandler? ExtractMethodRequested;
    public event EventHandler? InlineMethodRequested;
    public event EventHandler? IntroduceVariableRequested;
    public event EventHandler? ExtractConstantRequested;
    public event EventHandler? InlineConstantRequested;
    public event EventHandler? InlineVariableRequested;
    public event EventHandler? ChangeSignatureRequested;
    public event EventHandler? EncapsulateFieldRequested;
    public event EventHandler? InlineFieldRequested;
    public event EventHandler? MoveTypeToFileRequested;
    public event EventHandler? ExtractInterfaceRequested;
    public event EventHandler? GenerateConstructorRequested;
    public event EventHandler? ImplementInterfaceRequested;
    public event EventHandler? OverrideMethodRequested;
    public event EventHandler? AddParameterRequested;
    public event EventHandler? RemoveParameterRequested;
    public event EventHandler? ReorderParametersRequested;
    public event EventHandler? RenameParameterRequested;
    public event EventHandler? ChangeParameterTypeRequested;
    public event EventHandler? MakeParameterOptionalRequested;
    public event EventHandler? MakeParameterRequiredRequested;
    public event EventHandler? ConvertToNamedArgumentsRequested;
    public event EventHandler? ConvertToPositionalArgumentsRequested;
    public event EventHandler? SafeDeleteRequested;
    public event EventHandler? PullMembersUpRequested;
    public event EventHandler? PushMembersDownRequested;
    public event EventHandler? UseBaseTypeRequested;
    public event EventHandler? ConvertToInterfaceRequested;
    public event EventHandler? InvertIfRequested;
    public event EventHandler? ConvertToSelectCaseRequested;
    public event EventHandler? SplitDeclarationRequested;
    public event EventHandler? IntroduceFieldRequested;
    public event EventHandler? SurroundWithRequested;
    public event EventHandler? PeekDefinitionRequested;
    public event EventHandler<int>? BreakpointToggled;
    public event EventHandler? FormatDocumentRequested;
    public event EventHandler? CodeActionsRequested;
    public event EventHandler<HoverRequestEventArgs>? HoverRequested;
    public event EventHandler<HoverResultEventArgs>? HoverResultReceived;
    public event EventHandler<CodeActionResultEventArgs>? CodeActionsReceived;
    public event EventHandler<LocationResultEventArgs>? DefinitionResultReceived;
    public event EventHandler<ReferencesResultEventArgs>? ReferencesResultReceived;
    public event EventHandler<SignatureHelpRequestEventArgs>? SignatureHelpRequested;
    public event EventHandler<SignatureHelpResultEventArgs>? SignatureHelpResultReceived;
    public event EventHandler<DocumentHighlightRequestEventArgs>? DocumentHighlightRequested;
    public event EventHandler<DocumentHighlightResultEventArgs>? DocumentHighlightResultReceived;
    public event EventHandler<RenameResultEventArgs>? RenameResultReceived;
    public event EventHandler<DocumentSymbolsResultEventArgs>? DocumentSymbolsReceived;
    public event EventHandler? ExpandSelectionRequested;
    public event EventHandler? ShrinkSelectionRequested;
    public event EventHandler<SelectionRangeResultEventArgs>? SelectionRangeReceived;

    /// <summary>
    /// Callback to get selection info from the view
    /// </summary>
    public Func<SelectionInfoDto?>? GetSelectionInfo { get; set; }

    public CodeEditorDocumentViewModel(IFileService fileService, IEventAggregator eventAggregator, IBookmarkService? bookmarkService = null)
    {
        _fileService = fileService;
        _eventAggregator = eventAggregator;
        _bookmarkService = bookmarkService;
    }

    public IBookmarkService? BookmarkService => _bookmarkService;

    private string GetTitle()
    {
        var fileName = FilePath != null ? Path.GetFileName(FilePath) : "Untitled";
        return IsDirty ? $"{fileName} *" : fileName;
    }

    partial void OnTextChanged(string value)
    {
        var wasDirty = IsDirty;
        IsDirty = value != _originalText;

        // Update total lines count
        TotalLines = string.IsNullOrEmpty(value) ? 1 : value.Split('\n').Length;

        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        // Notify listeners about text change (for LSP synchronization)
        TextChanged?.Invoke(this, value);
    }

    partial void OnFilePathChanged(string? value)
    {
        OnPropertyChanged(nameof(Title));
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCaretLineChanged(int value)
    {
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnCaretColumnChanged(int value)
    {
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateCaretPosition(int line, int column)
    {
        CaretLine = line;
        CaretColumn = column;
    }

    public void NavigateTo(int line, int column = 1)
    {
        NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(line, column));
    }

    public void ShowFind()
    {
        FindRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowReplace()
    {
        ReplaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestGoToDefinition()
    {
        GoToDefinitionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFindAllReferences()
    {
        FindAllReferencesRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestToggleComment()
    {
        ToggleCommentRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestDuplicateLine()
    {
        DuplicateLineRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMoveLineUp()
    {
        MoveLineUpRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMoveLineDown()
    {
        MoveLineDownRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestDeleteLine()
    {
        DeleteLineRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRenameSymbol()
    {
        RenameSymbolRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExtractMethod()
    {
        ExtractMethodRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineMethod()
    {
        InlineMethodRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestIntroduceVariable()
    {
        IntroduceVariableRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExtractConstant()
    {
        ExtractConstantRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineConstant()
    {
        InlineConstantRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineVariable()
    {
        InlineVariableRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestChangeSignature()
    {
        ChangeSignatureRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestEncapsulateField()
    {
        EncapsulateFieldRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInlineField()
    {
        InlineFieldRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMoveTypeToFile()
    {
        MoveTypeToFileRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestExtractInterface()
    {
        ExtractInterfaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestGenerateConstructor()
    {
        GenerateConstructorRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestImplementInterface()
    {
        ImplementInterfaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestOverrideMethod()
    {
        OverrideMethodRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestAddParameter()
    {
        AddParameterRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRemoveParameter()
    {
        RemoveParameterRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestReorderParameters()
    {
        ReorderParametersRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestRenameParameter()
    {
        RenameParameterRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestChangeParameterType()
    {
        ChangeParameterTypeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMakeParameterOptional()
    {
        MakeParameterOptionalRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestMakeParameterRequired()
    {
        MakeParameterRequiredRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToNamedArguments()
    {
        ConvertToNamedArgumentsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToPositionalArguments()
    {
        ConvertToPositionalArgumentsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSafeDelete()
    {
        SafeDeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPullMembersUp()
    {
        PullMembersUpRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPushMembersDown()
    {
        PushMembersDownRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestUseBaseType()
    {
        UseBaseTypeRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToInterface()
    {
        ConvertToInterfaceRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestInvertIf()
    {
        InvertIfRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestConvertToSelectCase()
    {
        ConvertToSelectCaseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSplitDeclaration()
    {
        SplitDeclarationRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestIntroduceField()
    {
        IntroduceFieldRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestSurroundWith()
    {
        SurroundWithRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestPeekDefinition()
    {
        PeekDefinitionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestFormatDocument()
    {
        FormatDocumentRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestCodeActions()
    {
        CodeActionsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestHover(int line, int column)
    {
        HoverRequested?.Invoke(this, new HoverRequestEventArgs(line, column));
    }

    public void ProvideHoverResult(HoverInfo? hover)
    {
        HoverResultReceived?.Invoke(this, new HoverResultEventArgs(hover));
    }

    public void ProvideCodeActions(IEnumerable<CodeActionInfo> actions)
    {
        CodeActionsReceived?.Invoke(this, new CodeActionResultEventArgs(actions.ToList()));
    }

    public void ProvideDefinitionResult(LocationInfo? location)
    {
        DefinitionResultReceived?.Invoke(this, new LocationResultEventArgs(location));
    }

    public void ProvideReferencesResult(IEnumerable<LocationInfo> locations)
    {
        ReferencesResultReceived?.Invoke(this, new ReferencesResultEventArgs(locations.ToList()));
    }

    public void RequestSignatureHelp(int line, int column)
    {
        SignatureHelpRequested?.Invoke(this, new SignatureHelpRequestEventArgs(line, column));
    }

    public void ProvideSignatureHelp(SignatureHelp? help)
    {
        SignatureHelpResultReceived?.Invoke(this, new SignatureHelpResultEventArgs(help));
    }

    public void RequestDocumentHighlight(int line, int column)
    {
        DocumentHighlightRequested?.Invoke(this, new DocumentHighlightRequestEventArgs(line, column));
    }

    public void ProvideDocumentHighlights(IEnumerable<DocumentHighlightInfo> highlights)
    {
        DocumentHighlightResultReceived?.Invoke(this, new DocumentHighlightResultEventArgs(highlights.ToList()));
    }

    public void ProvideRenameResult(WorkspaceEditInfo? edit, string? errorMessage = null)
    {
        RenameResultReceived?.Invoke(this, new RenameResultEventArgs(edit, errorMessage));
    }

    public void ProvideDocumentSymbols(IEnumerable<DocumentSymbol> symbols)
    {
        DocumentSymbolsReceived?.Invoke(this, new DocumentSymbolsResultEventArgs(symbols.ToList()));
    }

    public void RequestExpandSelection()
    {
        ExpandSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void RequestShrinkSelection()
    {
        ShrinkSelectionRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ProvideSelectionRange(SelectionRangeInfo? range)
    {
        SelectionRangeReceived?.Invoke(this, new SelectionRangeResultEventArgs(range));
    }

    public void OnBreakpointToggled(int line)
    {
        BreakpointToggled?.Invoke(this, line);
    }

    public void RequestAddToWatch(string expression)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            AddToWatchRequested?.Invoke(this, expression);
        }
    }

    public void RequestDataTipEvaluation(string expression, double screenX, double screenY)
    {
        if (!string.IsNullOrWhiteSpace(expression))
        {
            DataTipEvaluationRequested?.Invoke(this, new DataTipEvaluationRequestEventArgs(expression, screenX, screenY));
        }
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return false;
        }

        try
        {
            await _fileService.WriteFileAsync(FilePath, Text, cancellationToken);
            _originalText = Text;
            IsDirty = false;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);

            _eventAggregator.Publish(new FileSavedEvent(FilePath));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SaveAsAsync(string path, CancellationToken cancellationToken = default)
    {
        var oldPath = FilePath;
        FilePath = path;

        if (await SaveAsync(cancellationToken))
        {
            return true;
        }

        FilePath = oldPath;
        return false;
    }

    public Task<bool> CloseAsync()
    {
        return Task.FromResult(true);
    }

    public void SetContent(string content)
    {
        _originalText = content;
        // Set the TextDocument's text - this will clear undo history (which is correct for initial load)
        TextDocument.Text = content;
        // Keep Text in sync for backward compatibility
        Text = content;
        IsDirty = false;
    }

    /// <summary>
    /// Replace content while preserving undo history (VS Code-like behavior).
    /// Use this after refactoring operations instead of SetContent.
    /// </summary>
    public void ReplaceContent(string newContent)
    {
        if (TextDocument.Text == newContent) return;

        // Use Replace which creates an undoable operation
        TextDocument.BeginUpdate();
        try
        {
            TextDocument.Replace(0, TextDocument.TextLength, newContent);
        }
        finally
        {
            TextDocument.EndUpdate();
        }

        // Update backing fields
        _text = newContent;

        // Update dirty state
        var wasDirty = IsDirty;
        IsDirty = _text != _originalText;

        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Called by the view when editor text changes. Updates Text without triggering
    /// binding feedback that would clear the undo stack.
    /// </summary>
    public void UpdateTextFromEditor(string newText)
    {
        // Update the backing field directly to avoid triggering property change
        // that would push back to the editor and clear undo
        _text = newText;

        // Update dirty state
        var wasDirty = IsDirty;
        IsDirty = _text != _originalText;

        if (wasDirty != IsDirty)
        {
            DirtyChanged?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(Title));
            TitleChanged?.Invoke(this, EventArgs.Empty);
        }

        // Notify that text changed (for any listeners that need the new text)
        TextChanged?.Invoke(this, newText);
    }

    [RelayCommand]
    private void ToggleSplitView()
    {
        IsSplitView = !IsSplitView;
    }

    [RelayCommand]
    private void SplitHorizontal()
    {
        SplitOrientation = SplitOrientation.Horizontal;
        IsSplitView = true;
    }

    [RelayCommand]
    private void SplitVertical()
    {
        SplitOrientation = SplitOrientation.Vertical;
        IsSplitView = true;
    }

    [RelayCommand]
    private void CloseSplit()
    {
        IsSplitView = false;
    }

    #region Diagnostics and Completion

    public event EventHandler<IEnumerable<DiagnosticItem>>? DiagnosticsUpdated;
    public event EventHandler<CompletionRequestedEventArgs>? CompletionRequested;
    public event EventHandler<IEnumerable<Core.Abstractions.Services.CompletionItem>>? CompletionReceived;

    /// <summary>
    /// Updates diagnostics for this document (error highlighting)
    /// </summary>
    public void UpdateDiagnostics(IEnumerable<DiagnosticItem> diagnostics)
    {
        DiagnosticsUpdated?.Invoke(this, diagnostics);
    }

    /// <summary>
    /// Requests code completion at the specified position
    /// </summary>
    public void RequestCompletion(int line, int column)
    {
        CompletionRequested?.Invoke(this, new CompletionRequestedEventArgs(line, column));
    }

    /// <summary>
    /// Provides completion items to the editor
    /// </summary>
    public void ProvideCompletions(IEnumerable<Core.Abstractions.Services.CompletionItem> completions)
    {
        var list = completions.ToList();
        CompletionReceived?.Invoke(this, list);
    }

    #endregion

    #region Code Lens

    public event EventHandler<IEnumerable<CodeLensItemInfo>>? CodeLensUpdated;
    public event EventHandler<CodeLensClickedInfo>? CodeLensCommandRequested;

    /// <summary>
    /// Shows code lens annotations above function/class lines.
    /// </summary>
    public void ShowCodeLenses(IEnumerable<CodeLensItemInfo> lenses)
    {
        CodeLensUpdated?.Invoke(this, lenses);
    }

    /// <summary>
    /// Clears all code lens annotations.
    /// </summary>
    public void ClearCodeLenses()
    {
        CodeLensUpdated?.Invoke(this, Enumerable.Empty<CodeLensItemInfo>());
    }

    /// <summary>
    /// Called when user clicks a code lens item.
    /// </summary>
    public void OnCodeLensClicked(CodeLensClickedInfo info)
    {
        CodeLensCommandRequested?.Invoke(this, info);
    }

    #endregion

    #region Inline Debug Values

    public event EventHandler<IEnumerable<InlineDebugValueInfo>>? InlineDebugValuesUpdated;

    /// <summary>
    /// Shows inline debug variable values next to code lines.
    /// </summary>
    public void ShowInlineDebugValues(IEnumerable<InlineDebugValueInfo> values)
    {
        InlineDebugValuesUpdated?.Invoke(this, values);
    }

    /// <summary>
    /// Clears all inline debug values.
    /// </summary>
    public void ClearInlineDebugValues()
    {
        InlineDebugValuesUpdated?.Invoke(this, Enumerable.Empty<InlineDebugValueInfo>());
    }

    #endregion

    #region Execution Line

    public event EventHandler<int?>? ExecutionLineChanged;

    public void SetExecutionLine(int line)
    {
        ExecutionLineChanged?.Invoke(this, line);
    }

    public void ClearExecutionLine()
    {
        ExecutionLineChanged?.Invoke(this, null);
    }

    #endregion
}

/// <summary>
/// Represents a code lens item to display above a line.
/// </summary>
public class CodeLensItemInfo
{
    public int Line { get; set; }
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
}

/// <summary>
/// Info about a clicked code lens command.
/// </summary>
public class CodeLensClickedInfo
{
    public string Title { get; set; } = "";
    public string CommandName { get; set; } = "";
    public List<object>? CommandArguments { get; set; }
    public int Line { get; set; }
}

/// <summary>
/// Represents a variable value to display inline during debugging.
/// </summary>
public class InlineDebugValueInfo
{
    public int Line { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public class CompletionRequestedEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public CompletionRequestedEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public enum SplitOrientation
{
    Horizontal,
    Vertical
}

public class NavigationRequestedEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public NavigationRequestedEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class DataTipEvaluationRequestEventArgs : EventArgs
{
    public string Expression { get; }
    public double ScreenX { get; }
    public double ScreenY { get; }

    public DataTipEvaluationRequestEventArgs(string expression, double screenX, double screenY)
    {
        Expression = expression;
        ScreenX = screenX;
        ScreenY = screenY;
    }
}

public class HoverRequestEventArgs : EventArgs
{
    public int Line { get; }
    public int Column { get; }

    public HoverRequestEventArgs(int line, int column)
    {
        Line = line;
        Column = column;
    }
}

public class HoverResultEventArgs : EventArgs
{
    public HoverInfo? Hover { get; }

    public HoverResultEventArgs(HoverInfo? hover)
    {
        Hover = hover;
    }
}

public class CodeActionResultEventArgs : EventArgs
{
    public IReadOnlyList<CodeActionInfo> Actions { get; }

    public CodeActionResultEventArgs(IReadOnlyList<CodeActionInfo> actions)
    {
        Actions = actions;
    }
}

public class LocationResultEventArgs : EventArgs
{
    public LocationInfo? Location { get; }

    public LocationResultEventArgs(LocationInfo? location)
    {
        Location = location;
    }
}

public class ReferencesResultEventArgs : EventArgs
{
    public IReadOnlyList<LocationInfo> Locations { get; }

    public ReferencesResultEventArgs(IReadOnlyList<LocationInfo> locations)
    {
        Locations = locations;
    }
}

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

public class SignatureHelpResultEventArgs : EventArgs
{
    public SignatureHelp? Help { get; }

    public SignatureHelpResultEventArgs(SignatureHelp? help)
    {
        Help = help;
    }
}

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

public class DocumentHighlightResultEventArgs : EventArgs
{
    public IReadOnlyList<DocumentHighlightInfo> Highlights { get; }

    public DocumentHighlightResultEventArgs(IReadOnlyList<DocumentHighlightInfo> highlights)
    {
        Highlights = highlights;
    }
}

public class DocumentHighlightInfo
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public bool IsWrite { get; set; }
}

public class RenameResultEventArgs : EventArgs
{
    public WorkspaceEditInfo? Edit { get; }
    public string? ErrorMessage { get; }
    public bool Success => Edit != null && ErrorMessage == null;

    public RenameResultEventArgs(WorkspaceEditInfo? edit, string? errorMessage = null)
    {
        Edit = edit;
        ErrorMessage = errorMessage;
    }
}

public class DocumentSymbolsResultEventArgs : EventArgs
{
    public IReadOnlyList<DocumentSymbol> Symbols { get; }

    public DocumentSymbolsResultEventArgs(IReadOnlyList<DocumentSymbol> symbols)
    {
        Symbols = symbols;
    }
}

public class SelectionRangeResultEventArgs : EventArgs
{
    public SelectionRangeInfo? Range { get; }

    public SelectionRangeResultEventArgs(SelectionRangeInfo? range)
    {
        Range = range;
    }
}
