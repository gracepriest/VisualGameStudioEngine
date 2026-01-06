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
        Text = content;
        IsDirty = false;
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
        var hasSubscribers = CompletionReceived != null;
        System.Diagnostics.Trace.WriteLine($"[ViewModel] ProvideCompletions: {list.Count} items, hasSubscribers={hasSubscribers}");
        Console.WriteLine($"[ViewModel] ProvideCompletions: {list.Count} items, hasSubscribers={hasSubscribers}");
        CompletionReceived?.Invoke(this, list);
    }

    #endregion
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
