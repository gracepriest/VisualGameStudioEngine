using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Abstractions.ViewModels;
using VisualGameStudio.Core.Constants;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.Dock;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.ViewModels.Panels;
using FindResult = VisualGameStudio.Shell.ViewModels.Dialogs.FindResult;
using RenameDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.RenameDialogViewModel;
using ExtractMethodDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ExtractMethodDialogViewModel;
using InlineMethodDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.InlineMethodDialogViewModel;
using IntroduceVariableDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.IntroduceVariableDialogViewModel;
using ExtractConstantDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ExtractConstantDialogViewModel;
using InlineConstantDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.InlineConstantDialogViewModel;
using ChangeSignatureDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ChangeSignatureDialogViewModel;
using EncapsulateFieldDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.EncapsulateFieldDialogViewModel;
using InlineFieldDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.InlineFieldDialogViewModel;
using MoveTypeToFileDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.MoveTypeToFileDialogViewModel;
using ExtractInterfaceDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ExtractInterfaceDialogViewModel;
using GenerateConstructorDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.GenerateConstructorDialogViewModel;
using ImplementInterfaceDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ImplementInterfaceDialogViewModel;
using OverrideMethodDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.OverrideMethodDialogViewModel;
using AddParameterDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.AddParameterDialogViewModel;
using RemoveParameterDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.RemoveParameterDialogViewModel;
using ReorderParametersDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ReorderParametersDialogViewModel;
using RenameParameterDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.RenameParameterDialogViewModel;
using ChangeParameterTypeDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ChangeParameterTypeDialogViewModel;
using MakeParameterOptionalDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.MakeParameterOptionalDialogViewModel;
using MakeParameterRequiredDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.MakeParameterRequiredDialogViewModel;
using ConvertToNamedArgumentsDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ConvertToNamedArgumentsDialogViewModel;
using ConvertToPositionalArgumentsDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ConvertToPositionalArgumentsDialogViewModel;
using InlineVariableDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.InlineVariableDialogViewModel;
using SafeDeleteDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.SafeDeleteDialogViewModel;
using PullMembersUpDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.PullMembersUpDialogViewModel;
using PushMembersDownDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.PushMembersDownDialogViewModel;
using UseBaseTypeDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.UseBaseTypeDialogViewModel;
using ConvertToInterfaceDialogViewModel = VisualGameStudio.Shell.ViewModels.Dialogs.ConvertToInterfaceDialogViewModel;

namespace VisualGameStudio.Shell.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IProjectService _projectService;
    private readonly IBuildService _buildService;
    private readonly IDebugService _debugService;
    private readonly ILanguageService _languageService;
    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly IBookmarkService _bookmarkService;
    private readonly IRefactoringService _refactoringService;
    private readonly IProjectTemplateService _projectTemplateService;
    private readonly IGitService _gitService;
    private readonly IEventAggregator _eventAggregator;
    private readonly DockFactory _dockFactory;

    /// <summary>
    /// Cache of blame data per file path. Invalidated on file save.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<GitBlameLine>> _blameCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private IRootDock? _layout;

    [ObservableProperty]
    private string _title = "Visual Game Studio";

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>
    /// Blame annotation text for the current line, shown in status bar.
    /// Example: "John Smith, 2 days ago — Fix login bug"
    /// </summary>
    [ObservableProperty]
    private string _blameAnnotationText = "";

    /// <summary>
    /// Gets the current project name for breadcrumb display.
    /// </summary>
    public string? ProjectName => _projectService.CurrentProject?.Name;

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    [ObservableProperty]
    private string _currentConfiguration = "Debug";

    [ObservableProperty]
    private ObservableCollection<string> _configurations = new() { "Debug", "Release" };

    [ObservableProperty]
    private bool _isDebugging;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _debugStatusText = "";

    /// <summary>
    /// Whether whitespace characters (spaces, tabs, EOL) are rendered in the editor.
    /// </summary>
    [ObservableProperty]
    private bool _showWhitespace;

    /// <summary>
    /// Whether Zen mode (distraction-free editing) is active.
    /// Hides menu bar, toolbar, status bar, and side/bottom panels.
    /// </summary>
    [ObservableProperty]
    private bool _isZenMode;

    /// <summary>
    /// Whether column (rectangular) selection mode is active across all editors.
    /// When active, selections are always rectangular without needing to hold Alt.
    /// </summary>
    [ObservableProperty]
    private bool _isColumnSelectionMode;

    /// <summary>
    /// Tracks the executable name being debugged, for status bar display.
    /// </summary>
    private string _debugTargetName = "";

    /// <summary>
    /// Tracks the current (topmost) stack frame ID so debug hover evaluate
    /// requests are scoped to the correct frame.
    /// </summary>
    private int? _currentFrameId;

    /// <summary>
    /// Tracks whether debug panels have been auto-shown for the current debug session,
    /// so we only switch panels once per session start (not on every pause/resume).
    /// </summary>
    private bool _debugPanelsShown;

    /// <summary>
    /// Remembers the output category before debug session started, so we can restore it when debugging stops.
    /// </summary>
    private OutputCategory _preDebugOutputCategory;

    /// <summary>
    /// Enhanced status bar view model with interactive indicators.
    /// </summary>
    public StatusBarViewModel StatusBar { get; } = new();

    public SolutionExplorerViewModel SolutionExplorer { get; }
    public OutputPanelViewModel OutputPanel { get; }
    public ErrorListViewModel ErrorList { get; }
    public CallStackViewModel CallStack { get; }
    public VariablesViewModel Variables { get; }
    public BreakpointsViewModel Breakpoints { get; }
    public FindInFilesViewModel FindInFiles { get; }
    public TerminalViewModel Terminal { get; }
    public GitChangesViewModel GitChanges { get; }
    public GitBranchesViewModel GitBranches { get; }
    public GitStashViewModel GitStash { get; }
    public GitBlameViewModel GitBlame { get; }
    public WatchViewModel Watch { get; }
    public ImmediateWindowViewModel ImmediateWindow { get; }
    public DocumentOutlineViewModel DocumentOutline { get; }
    public BookmarksViewModel Bookmarks { get; }
    public CallHierarchyViewModel CallHierarchy { get; }
    public TypeHierarchyViewModel TypeHierarchy { get; }

    private readonly Dictionary<string, CodeEditorDocumentViewModel> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action> _documentCleanupActions = new(StringComparer.OrdinalIgnoreCase);

    public MainWindowViewModel(
        IProjectService projectService,
        IBuildService buildService,
        IDebugService debugService,
        ILanguageService languageService,
        IDialogService dialogService,
        IFileService fileService,
        IBookmarkService bookmarkService,
        IRefactoringService refactoringService,
        IProjectTemplateService projectTemplateService,
        IGitService gitService,
        IEventAggregator eventAggregator,
        DockFactory dockFactory,
        SolutionExplorerViewModel solutionExplorer,
        OutputPanelViewModel outputPanel,
        ErrorListViewModel errorList,
        CallStackViewModel callStack,
        VariablesViewModel variables,
        BreakpointsViewModel breakpoints,
        FindInFilesViewModel findInFiles,
        TerminalViewModel terminal,
        GitChangesViewModel gitChanges,
        GitBranchesViewModel gitBranches,
        GitStashViewModel gitStash,
        GitBlameViewModel gitBlame,
        WatchViewModel watch,
        ImmediateWindowViewModel immediateWindow,
        DocumentOutlineViewModel documentOutline,
        BookmarksViewModel bookmarks,
        CallHierarchyViewModel callHierarchy,
        TypeHierarchyViewModel typeHierarchy)
    {
        _projectService = projectService;
        _buildService = buildService;
        _debugService = debugService;
        _languageService = languageService;
        _dialogService = dialogService;
        _fileService = fileService;
        _bookmarkService = bookmarkService;
        _refactoringService = refactoringService;
        _projectTemplateService = projectTemplateService;
        _gitService = gitService;
        _eventAggregator = eventAggregator;
        _dockFactory = dockFactory;

        SolutionExplorer = solutionExplorer;
        OutputPanel = outputPanel;
        ErrorList = errorList;
        CallStack = callStack;
        Variables = variables;
        Breakpoints = breakpoints;
        FindInFiles = findInFiles;
        Terminal = terminal;
        GitChanges = gitChanges;
        GitBranches = gitBranches;
        GitStash = gitStash;
        GitBlame = gitBlame;
        Watch = watch;
        ImmediateWindow = immediateWindow;
        DocumentOutline = documentOutline;
        Bookmarks = bookmarks;
        CallHierarchy = callHierarchy;
        TypeHierarchy = typeHierarchy;

        // Setup dock layout
        _dockFactory.SetViewModels(solutionExplorer, outputPanel, errorList, callStack, variables, breakpoints, findInFiles, terminal, gitChanges, gitBranches, gitStash, gitBlame, watch, immediateWindow, documentOutline, bookmarks);
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);

        // Subscribe to document close event
        _dockFactory.DocumentClosed += OnDocumentClosed;

        // Subscribe to events
        _projectService.ProjectOpened += OnProjectOpened;
        _projectService.ProjectClosed += OnProjectClosed;
        _buildService.BuildCompleted += OnBuildCompleted;
        _debugService.StateChanged += OnDebugStateChanged;
        _debugService.Stopped += OnDebugStopped;
        _debugService.OutputReceived += OnDebugOutput;

        // Handle file open requests from solution explorer
        SolutionExplorer.FileOpenRequested += OnFileOpenRequested;

        // Handle error list navigation
        ErrorList.DiagnosticDoubleClicked += OnDiagnosticDoubleClicked;

        // Handle breakpoint condition editing and visual updates
        Breakpoints.EditConditionRequested += OnEditBreakpointCondition;
        Breakpoints.EditFunctionConditionRequested += OnEditFunctionBreakpointCondition;
        Breakpoints.BreakpointsChanged += OnBreakpointVisualsChanged;

        // Handle bookmark navigation
        Bookmarks.NavigationRequested += OnBookmarkNavigationRequested;

        // Handle call stack frame selection (navigate to source)
        CallStack.FrameSelected += OnCallStackFrameSelected;

        // Wire up Find in Files navigation
        FindInFiles.SetNavigationCallback(OpenFileAtLine);

        // Subscribe to language service diagnostics for error highlighting
        _languageService.DiagnosticsReceived += OnDiagnosticsReceived;

        // Start language service
        _ = _languageService.StartAsync();
    }

    private void OnDiagnosticsReceived(object? sender, DiagnosticsEventArgs e)
    {
        try
        {
            if (e?.Diagnostics == null) return;

            // Forward diagnostics to the error list
            ErrorList.UpdateDiagnostics(e.Diagnostics);

            // Reset error cycling index when diagnostics change
            _currentDiagnosticIndex = -1;

            // Forward to the specific document for error highlighting
            var uri = e.Uri ?? "";
            var filePath = uri.Replace("file:///", "").Replace("/", "\\");
            if (!string.IsNullOrEmpty(filePath) && _openDocuments.TryGetValue(filePath, out var doc))
            {
                doc.UpdateDiagnostics(e.Diagnostics);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Diagnostics] Error processing diagnostics: {ex.Message}");
        }
    }

    private void OpenFileAtLine(string filePath, int line)
    {
        _ = OpenFileAtLineAsync(filePath, line);
    }

    private async Task OpenFileAtLineAsync(string filePath, int line)
    {
        await OpenFileAsync(filePath);

        // Navigate to the line
        if (_openDocuments.TryGetValue(filePath, out var doc))
        {
            doc.NavigateTo(line, 1);
        }
    }

    private async void OnProjectOpened(object? sender, ProjectEventArgs e)
    {
        Title = $"{e.Project.Name} - Visual Game Studio";
        StatusText = $"Project loaded: {e.Project.Name}";

        // Load persisted breakpoints
        Breakpoints.SetProjectDirectory(e.Project.ProjectDirectory);
        await Breakpoints.LoadBreakpointsAsync();
    }

    private void OnDocumentClosed(object? sender, string filePath)
    {
        // Run cleanup actions (unsubscribe events) for this document
        if (_documentCleanupActions.TryGetValue(filePath, out var cleanup))
        {
            cleanup();
            _documentCleanupActions.Remove(filePath);
        }

        _openDocuments.Remove(filePath);

        // Remove blame cache for closed document
        _blameCache.Remove(filePath);

        // Notify LSP that the document was closed
        if (_languageService.IsConnected &&
            (filePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
             filePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)))
        {
            _ = _languageService.CloseDocumentAsync(filePath);
        }
    }

    private void OnProjectClosed(object? sender, ProjectEventArgs e)
    {
        Title = "Visual Game Studio";
        StatusText = "Ready";
    }

    private void OnBuildCompleted(object? sender, BuildCompletedEventArgs e)
    {
        var result = e.Result;
        if (result.Success)
        {
            StatusText = $"Build succeeded - {result.Duration.TotalSeconds:F1}s";
            ShowNotification($"Build succeeded - {result.Duration.TotalSeconds:F1}s", "info");
        }
        else
        {
            StatusText = $"Build failed - {result.ErrorCount} error(s), {result.WarningCount} warning(s)";
            ShowNotification($"Build failed: {result.ErrorCount} error(s), {result.WarningCount} warning(s)", "error");
        }

        // Update error list
        ErrorList.UpdateDiagnostics(result.Diagnostics);

        // Reset error cycling index when diagnostics change
        _currentDiagnosticIndex = -1;
    }

    private async void OnFileOpenRequested(object? sender, string filePath)
    {
        try
        {
            await OpenFileAsync(filePath);
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    private async void OnDiagnosticDoubleClicked(object? sender, DiagnosticItem diagnostic)
    {
        try
        {
            if (diagnostic.FilePath != null)
            {
                await OpenFileAndNavigateAsync(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);
            }
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    private async void OnEditBreakpointCondition(object? sender, BreakpointItem breakpoint)
    {
        try
        {
            var location = $"{breakpoint.FileName}:{breakpoint.Line}";
            var result = await _dialogService.ShowBreakpointConditionDialogAsync(
                location,
                breakpoint.Condition,
                breakpoint.HitCondition,
                breakpoint.LogMessage);

            if (result != null && result.DialogResult)
            {
                Breakpoints.UpdateBreakpointCondition(
                    breakpoint,
                    result.Condition,
                    result.HitCount,
                    result.LogMessage);
            }
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    private async void OnEditFunctionBreakpointCondition(object? sender, FunctionBreakpointItem breakpoint)
    {
        try
        {
            var result = await _dialogService.ShowBreakpointConditionDialogAsync(
                $"Function: {breakpoint.FunctionName}",
                breakpoint.Condition,
                breakpoint.HitCondition,
                null); // Function breakpoints don't support log messages

            if (result != null && result.DialogResult)
            {
                await Breakpoints.UpdateFunctionBreakpointConditionAsync(
                    breakpoint,
                    result.Condition,
                    result.HitCount);
            }
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    /// <summary>
    /// Refreshes breakpoint visuals (verified/unverified/kind) in all open editors
    /// when breakpoint state changes (e.g., after debugger binds breakpoints).
    /// </summary>
    private void OnBreakpointVisualsChanged(object? sender, EventArgs e)
    {
        foreach (var kvp in _openDocuments)
        {
            var filePath = kvp.Key;
            var doc = kvp.Value;
            var visuals = Breakpoints.GetBreakpointVisualsForFile(filePath);
            doc.UpdateBreakpointVisuals(visuals);
        }
    }

    private async void OnAddToWatchRequested(object? sender, string expression)
    {
        try
        {
            await Watch.AddExpressionCommand.ExecuteAsync(expression);
            _dockFactory.ActivateTool("Watch");
        }
        catch (Exception)
        {
            // Ignore exceptions in event handler
        }
    }

    public event EventHandler<DataTipResultEventArgs>? DataTipResult;

    /// <summary>
    /// Raised when a toast notification should be displayed in the UI.
    /// </summary>
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    /// <summary>
    /// Shows a toast notification in the bottom-right corner of the IDE.
    /// </summary>
    /// <param name="message">The notification message text.</param>
    /// <param name="severity">Severity level: "info", "warning", or "error".</param>
    public void ShowNotification(string message, string severity = "info")
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs(message, severity));
    }

    private async void OnDataTipEvaluationRequested(object? sender, DataTipEvaluationRequestEventArgs e)
    {
        // Only evaluate if we're debugging and paused
        if (!_debugService.IsDebugging || _debugService.State != Core.Abstractions.Services.DebugState.Paused)
        {
            // Not paused - fall back to LSP hover
            FallbackToLspHover(sender as CodeEditorDocumentViewModel, e);
            return;
        }

        try
        {
            var result = await _debugService.EvaluateAsync(e.Expression, _currentFrameId);
            if (result != null && !result.Result.StartsWith("Error:"))
            {
                DataTipResult?.Invoke(this, new DataTipResultEventArgs(
                    e.Expression,
                    result.Result,
                    result.Type,
                    e.ScreenX,
                    e.ScreenY,
                    false
                ));
                return; // Debug eval succeeded - don't show LSP hover
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DataTip] Evaluate failed for '{e.Expression}': {ex.Message}");
        }

        // Debug evaluation returned no result or an error - fall back to LSP hover
        FallbackToLspHover(sender as CodeEditorDocumentViewModel, e);
    }

    /// <summary>
    /// Falls back to LSP hover when debug evaluation is unavailable or fails.
    /// </summary>
    private async void FallbackToLspHover(CodeEditorDocumentViewModel? document, DataTipEvaluationRequestEventArgs e)
    {
        if (document == null || e.Line <= 0 || e.Column <= 0) return;
        try
        {
            var hover = await _languageService.GetHoverAsync(document.FilePath, e.Line, e.Column);
            document.ProvideHoverResult(hover);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DataTip] LSP hover fallback failed: {ex.Message}");
        }
    }

    private async Task OpenFileAndNavigateAsync(string filePath, int line, int column)
    {
        // Check if already open
        if (_openDocuments.TryGetValue(filePath, out var existingDoc))
        {
            // Activate the existing document and navigate
            _dockFactory.ActivateDocument(existingDoc);
            existingDoc.NavigateTo(line, column);
            return;
        }

        // Open the file first
        await OpenFileAsync(filePath);

        // Navigate after opening
        if (_openDocuments.TryGetValue(filePath, out var newDoc))
        {
            // Small delay to ensure view is loaded
            await Task.Delay(100);
            newDoc.NavigateTo(line, column);
        }
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (App.MainWindow == null) return;

        var dialog = new Views.Dialogs.CreateProjectView(_projectTemplateService);

        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true && dialog.Result != null)
        {
            var projectResult = dialog.Result;
            if (projectResult.Success && !string.IsNullOrEmpty(projectResult.ProjectPath))
            {
                try
                {
                    await _projectService.OpenProjectAsync(projectResult.ProjectPath);
                    StatusText = $"Project created: {Path.GetFileNameWithoutExtension(projectResult.ProjectPath)}";
                }
                catch (Exception ex)
                {
                    await _dialogService.ShowMessageAsync("Error", $"Failed to open created project: {ex.Message}",
                        DialogButtons.Ok, DialogIcon.Error);
                }
            }
        }
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(new FileDialogOptions
        {
            Title = "Open Project",
            Filters = new List<FileDialogFilter>
            {
                new("BasicLang Project", "blproj"),
                new("All Files", "*")
            }
        });

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            SetBusy(true, "Opening project...");
            await _projectService.OpenProjectAsync(filePath);
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to open project: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(new FileDialogOptions
        {
            Title = "Open File",
            Filters = new List<FileDialogFilter>
            {
                new("BasicLang Files", "bas", "bl", "basic"),
                new("All Files", "*")
            }
        });

        if (!string.IsNullOrEmpty(filePath))
        {
            await OpenFileAsync(filePath);
        }
    }

    /// <summary>
    /// Opens a file from a document link click (Import navigation).
    /// Public so the view can call it. Wraps OpenFileAsync with error handling.
    /// </summary>
    public async Task OpenFileFromLinkAsync(string filePath)
    {
        try
        {
            await OpenFileAsync(filePath);
            StatusText = $"Opened: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open file: {ex.Message}";
        }
    }

    private async Task OpenFileAsync(string filePath)
    {
        // Check if already open
        if (_openDocuments.TryGetValue(filePath, out var existingDoc))
        {
            // Activate the existing document
            _dockFactory.ActivateDocument(existingDoc);
            return;
        }

        try
        {
            var content = await _fileService.ReadFileAsync(filePath);

            // Notify LSP about opened document
            if (_languageService.IsConnected &&
                (filePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                 filePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)))
            {
                await _languageService.OpenDocumentAsync(filePath, content);
            }

            var document = new CodeEditorDocumentViewModel(_fileService, _eventAggregator, _bookmarkService)
            {
                FilePath = filePath,
                LanguageService = _languageService,
                GitService = _gitService
            };
            document.SetContent(content);

            // Update status bar for the new document
            StatusBar.UpdateForFile(filePath);
            StatusBar.DetectLineEnding(content);

            // Use named handler variables so we can unsubscribe on document close
            EventHandler onCaretChanged = (s, e) =>
            {
                CaretLine = document.CaretLine;
                CaretColumn = document.CaretColumn;
                _ = UpdateBlameForCurrentLineAsync(document);
            };
            document.CaretPositionChanged += onCaretChanged;

            document.AddToWatchRequested += OnAddToWatchRequested;
            document.DataTipEvaluationRequested += OnDataTipEvaluationRequested;

            // Named async handlers for refactoring commands
            EventHandler onGoToDef = async (s, e) => await GoToDefinitionAsync();
            EventHandler onFindRefs = async (s, e) => await FindReferencesAsync();
            EventHandler onRename = async (s, e) => await RenameSymbolAsync();
            EventHandler onExtractMethod = async (s, e) => await ExtractMethodAsync();
            EventHandler onInlineMethod = async (s, e) => await InlineMethodAsync();
            EventHandler onIntroduceVar = async (s, e) => await IntroduceVariableAsync();
            EventHandler onExtractConst = async (s, e) => await ExtractConstantAsync();
            EventHandler onInlineConst = async (s, e) => await InlineConstantAsync();
            EventHandler onInlineVar = async (s, e) => await InlineVariableAsync();
            EventHandler onChangeSig = async (s, e) => await ChangeSignatureAsync();
            EventHandler onEncapField = async (s, e) => await EncapsulateFieldAsync();
            EventHandler onInlineField = async (s, e) => await InlineFieldAsync();
            EventHandler onMoveType = async (s, e) => await MoveTypeToFileAsync();
            EventHandler onExtractIface = async (s, e) => await ExtractInterfaceAsync();
            EventHandler onGenCtor = async (s, e) => await GenerateConstructorAsync();
            EventHandler onImplIface = async (s, e) => await ImplementInterfaceAsync();
            EventHandler onOverride = async (s, e) => await OverrideMethodAsync();
            EventHandler onAddParam = async (s, e) => await AddParameterAsync();
            EventHandler onRemoveParam = async (s, e) => await RemoveParameterAsync();
            EventHandler onReorderParams = async (s, e) => await ReorderParametersAsync();
            EventHandler onRenameParam = async (s, e) => await RenameParameterAsync();
            EventHandler onChangeParamType = async (s, e) => await ChangeParameterTypeAsync();
            EventHandler onMakeOptional = async (s, e) => await MakeParameterOptionalAsync();
            EventHandler onMakeRequired = async (s, e) => await MakeParameterRequiredAsync();
            EventHandler onToNamed = async (s, e) => await ConvertToNamedArgumentsAsync();
            EventHandler onToPositional = async (s, e) => await ConvertToPositionalArgumentsAsync();
            EventHandler onSafeDelete = async (s, e) => await SafeDeleteAsync();
            EventHandler onPullUp = async (s, e) => await PullMembersUpAsync();
            EventHandler onPushDown = async (s, e) => await PushMembersDownAsync();
            EventHandler onUseBase = async (s, e) => await UseBaseTypeAsync();
            EventHandler onToIface = async (s, e) => await ConvertToInterfaceAsync();
            EventHandler onInvertIf = async (s, e) => await InvertIfAsync();
            EventHandler onToSelect = async (s, e) => await ConvertToSelectCaseAsync();
            EventHandler onSplitDecl = async (s, e) => await SplitDeclarationAsync();
            EventHandler onIntroField = async (s, e) => await IntroduceFieldAsync();
            EventHandler onSurround = async (s, e) => await SurroundWithAsync();
            EventHandler onPeek = async (s, e) => await PeekDefinitionAsync();
            EventHandler onFormat = async (s, e) => await FormatDocumentAsync();
            EventHandler onCodeActions = async (s, e) => await ShowCodeActionsAsync();
            EventHandler onExpandSel = async (s, e) => await ExpandSelectionAsync();
            EventHandler onShrinkSel = (s, e) => ShrinkSelection();

            document.GoToDefinitionRequested += onGoToDef;
            document.FindAllReferencesRequested += onFindRefs;
            document.RenameSymbolRequested += onRename;
            document.ExtractMethodRequested += onExtractMethod;
            document.InlineMethodRequested += onInlineMethod;
            document.IntroduceVariableRequested += onIntroduceVar;
            document.ExtractConstantRequested += onExtractConst;
            document.InlineConstantRequested += onInlineConst;
            document.InlineVariableRequested += onInlineVar;
            document.ChangeSignatureRequested += onChangeSig;
            document.EncapsulateFieldRequested += onEncapField;
            document.InlineFieldRequested += onInlineField;
            document.MoveTypeToFileRequested += onMoveType;
            document.ExtractInterfaceRequested += onExtractIface;
            document.GenerateConstructorRequested += onGenCtor;
            document.ImplementInterfaceRequested += onImplIface;
            document.OverrideMethodRequested += onOverride;
            document.AddParameterRequested += onAddParam;
            document.RemoveParameterRequested += onRemoveParam;
            document.ReorderParametersRequested += onReorderParams;
            document.RenameParameterRequested += onRenameParam;
            document.ChangeParameterTypeRequested += onChangeParamType;
            document.MakeParameterOptionalRequested += onMakeOptional;
            document.MakeParameterRequiredRequested += onMakeRequired;
            document.ConvertToNamedArgumentsRequested += onToNamed;
            document.ConvertToPositionalArgumentsRequested += onToPositional;
            document.SafeDeleteRequested += onSafeDelete;
            document.PullMembersUpRequested += onPullUp;
            document.PushMembersDownRequested += onPushDown;
            document.UseBaseTypeRequested += onUseBase;
            document.ConvertToInterfaceRequested += onToIface;
            document.InvertIfRequested += onInvertIf;
            document.ConvertToSelectCaseRequested += onToSelect;
            document.SplitDeclarationRequested += onSplitDecl;
            document.IntroduceFieldRequested += onIntroField;
            document.SurroundWithRequested += onSurround;
            document.PeekDefinitionRequested += onPeek;
            document.FormatDocumentRequested += onFormat;
            document.CodeActionsRequested += onCodeActions;
            document.ExpandSelectionRequested += onExpandSel;
            document.ShrinkSelectionRequested += onShrinkSel;

            EventHandler<OnTypeFormattingRequestEventArgs>? onTypeFormat = async (s, e) =>
            {
                try
                {
                    if (e == null || document.FilePath == null || !_languageService.IsConnected) return;
                    await OnTypeFormattingAsync(document, e.Line, e.Column, e.TriggerCharacter);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnTypeFormatting] Error: {ex.Message}");
                }
            };
            document.OnTypeFormattingRequested += onTypeFormat;

            EventHandler<HoverRequestEventArgs>? onHover = async (s, e) =>
            {
                try
                {
                    if (e == null || document.FilePath == null) return;
                    var hover = await _languageService.GetHoverAsync(document.FilePath, e.Line, e.Column);
                    document.ProvideHoverResult(hover);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Hover] Error: {ex.Message}");
                }
            };
            document.HoverRequested += onHover;

            // Wire up signature help
            EventHandler<SignatureHelpRequestEventArgs>? onSigHelp = async (s, e) =>
            {
                try
                {
                    if (e == null || document.FilePath == null || !_languageService.IsConnected) return;
                    var help = await _languageService.GetSignatureHelpAsync(document.FilePath, e.Line, e.Column);
                    document.ProvideSignatureHelp(help);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SignatureHelp] Error: {ex.Message}");
                }
            };
            document.SignatureHelpRequested += onSigHelp;

            // Wire up document highlight on caret position change
            EventHandler<DocumentHighlightRequestEventArgs>? onDocHighlight = async (s, e) =>
            {
                if (e == null || document.FilePath == null || !_languageService.IsConnected) return;
                try
                {
                    var result = await _languageService.GetDocumentHighlightsAsync(document.FilePath, e.Line, e.Column);
                    if (result.Count > 0)
                    {
                        var highlights = result.Select(r => new DocumentHighlightInfo
                        {
                            StartLine = r.StartLine,
                            StartColumn = r.StartColumn,
                            EndLine = r.EndLine,
                            EndColumn = r.EndColumn,
                            IsWrite = r.Kind == DocumentHighlightKind.Write
                        }).ToList();
                        document.ProvideDocumentHighlights(highlights);
                    }
                    else
                    {
                        document.ProvideDocumentHighlights(Array.Empty<DocumentHighlightInfo>());
                    }
                }
                catch
                {
                    document.ProvideDocumentHighlights(Array.Empty<DocumentHighlightInfo>());
                }
            };
            document.DocumentHighlightRequested += onDocHighlight;

            // Wire up code completion requests
            EventHandler<CompletionRequestedEventArgs>? onCompletion = async (s, e) =>
            {
                try
                {
                    if (e == null) return;

                    IReadOnlyList<CompletionItem>? completions = null;

                    if (_languageService.IsConnected)
                    {
                        completions = await _languageService.GetCompletionsAsync(
                            document.FilePath ?? "",
                            e.Line,
                            e.Column);
                    }

                    // Provide completions (either from LSP or fallback)
                    if (completions?.Any() == true)
                    {
                        // Filter to type-only completions after "As " keyword
                        completions = FilterCompletionsForContext(document, e.Line, e.Column, completions);
                        document.ProvideCompletions(completions);
                    }
                    else
                    {
                        // Fallback to basic completions when LSP is not available or returns nothing
                        var fallbackCompletions = GetFallbackCompletions();
                        if (fallbackCompletions.Any())
                        {
                            // Filter fallback completions too
                            var filtered = FilterCompletionsForContext(document, e.Line, e.Column, fallbackCompletions);
                            document.ProvideCompletions(filtered);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Completion] Error: {ex.Message}");
                }
            };
            document.CompletionRequested += onCompletion;

            // Wire up text change notifications for LSP
            var documentVersion = 1;
            EventHandler<string>? onTextChanged = async (s, newText) =>
            {
                try
                {
                    if (_languageService.IsConnected && document.FilePath != null &&
                        (document.FilePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                         document.FilePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)))
                    {
                        documentVersion++;
                        await _languageService.ChangeDocumentAsync(document.FilePath, newText, documentVersion);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LSP TextChanged] Error: {ex.Message}");
                }
            };
            document.TextChanged += onTextChanged;

            // Wire up breakpoint toggle from editor margin to debugger
            EventHandler<int>? onBreakpoint = (s, line) =>
            {
                if (!string.IsNullOrEmpty(document.FilePath))
                {
                    Breakpoints.AddBreakpoint(document.FilePath, line);
                }
            };
            document.BreakpointToggled += onBreakpoint;

            // Register cleanup action to unsubscribe all handlers on document close
            _documentCleanupActions[filePath] = () =>
            {
                document.CaretPositionChanged -= onCaretChanged;
                document.AddToWatchRequested -= OnAddToWatchRequested;
                document.DataTipEvaluationRequested -= OnDataTipEvaluationRequested;
                document.GoToDefinitionRequested -= onGoToDef;
                document.FindAllReferencesRequested -= onFindRefs;
                document.RenameSymbolRequested -= onRename;
                document.ExtractMethodRequested -= onExtractMethod;
                document.InlineMethodRequested -= onInlineMethod;
                document.IntroduceVariableRequested -= onIntroduceVar;
                document.ExtractConstantRequested -= onExtractConst;
                document.InlineConstantRequested -= onInlineConst;
                document.InlineVariableRequested -= onInlineVar;
                document.ChangeSignatureRequested -= onChangeSig;
                document.EncapsulateFieldRequested -= onEncapField;
                document.InlineFieldRequested -= onInlineField;
                document.MoveTypeToFileRequested -= onMoveType;
                document.ExtractInterfaceRequested -= onExtractIface;
                document.GenerateConstructorRequested -= onGenCtor;
                document.ImplementInterfaceRequested -= onImplIface;
                document.OverrideMethodRequested -= onOverride;
                document.AddParameterRequested -= onAddParam;
                document.RemoveParameterRequested -= onRemoveParam;
                document.ReorderParametersRequested -= onReorderParams;
                document.RenameParameterRequested -= onRenameParam;
                document.ChangeParameterTypeRequested -= onChangeParamType;
                document.MakeParameterOptionalRequested -= onMakeOptional;
                document.MakeParameterRequiredRequested -= onMakeRequired;
                document.ConvertToNamedArgumentsRequested -= onToNamed;
                document.ConvertToPositionalArgumentsRequested -= onToPositional;
                document.SafeDeleteRequested -= onSafeDelete;
                document.PullMembersUpRequested -= onPullUp;
                document.PushMembersDownRequested -= onPushDown;
                document.UseBaseTypeRequested -= onUseBase;
                document.ConvertToInterfaceRequested -= onToIface;
                document.InvertIfRequested -= onInvertIf;
                document.ConvertToSelectCaseRequested -= onToSelect;
                document.SplitDeclarationRequested -= onSplitDecl;
                document.IntroduceFieldRequested -= onIntroField;
                document.SurroundWithRequested -= onSurround;
                document.PeekDefinitionRequested -= onPeek;
                document.FormatDocumentRequested -= onFormat;
                document.OnTypeFormattingRequested -= onTypeFormat;
                document.CodeActionsRequested -= onCodeActions;
                document.ExpandSelectionRequested -= onExpandSel;
                document.ShrinkSelectionRequested -= onShrinkSel;
                document.HoverRequested -= onHover;
                document.SignatureHelpRequested -= onSigHelp;
                document.DocumentHighlightRequested -= onDocHighlight;
                document.CompletionRequested -= onCompletion;
                document.TextChanged -= onTextChanged;
                document.BreakpointToggled -= onBreakpoint;
            };

            // Wire up code lens commands
            EventHandler<CodeLensClickedInfo>? onCodeLensCmd = async (s, e) =>
            {
                try
                {
                    await HandleCodeLensCommandAsync(document, e);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CodeLens Command] Error: {ex.Message}");
                }
            };
            document.CodeLensCommandRequested += onCodeLensCmd;

            // Fetch code lenses after text changes (debounced via existing LSP change notification)
            EventHandler<string>? onTextChangedCodeLens = async (s, newText) =>
            {
                try
                {
                    await RefreshCodeLensesAsync(document);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CodeLens] Error: {ex.Message}");
                }
            };
            document.TextChanged += onTextChangedCodeLens;

            // Fetch semantic tokens after text changes (debounced)
            CancellationTokenSource? semanticTokenCts = null;
            EventHandler<string>? onTextChangedSemanticTokens = async (s, newText) =>
            {
                try
                {
                    semanticTokenCts?.Cancel();
                    semanticTokenCts = new CancellationTokenSource();
                    var ct = semanticTokenCts.Token;
                    await Task.Delay(500, ct);
                    if (ct.IsCancellationRequested) return;
                    await RefreshSemanticTokensAsync(document, ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SemanticTokens] Error: {ex.Message}");
                }
            };
            document.TextChanged += onTextChangedSemanticTokens;

            // Add code lens and semantic token handlers to cleanup
            var existingCleanup = _documentCleanupActions.GetValueOrDefault(filePath);
            _documentCleanupActions[filePath] = () =>
            {
                existingCleanup?.Invoke();
                document.CodeLensCommandRequested -= onCodeLensCmd;
                document.TextChanged -= onTextChangedCodeLens;
                document.TextChanged -= onTextChangedSemanticTokens;
                semanticTokenCts?.Cancel();
                semanticTokenCts?.Dispose();
            };

            _openDocuments[filePath] = document;
            _dockFactory.AddDocument(document);

            // Update document outline
            await UpdateDocumentOutlineAsync(filePath, content);

            // Fetch initial code lenses and semantic tokens
            await RefreshCodeLensesAsync(document);
            _ = RefreshSemanticTokensAsync(document);
        }
        catch (Exception ex)
        {
            // Log crash details to file for diagnostics
            try
            {
                var msg = $"[{DateTime.Now:HH:mm:ss}] [OpenFile] {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}\n";
                if (ex.InnerException != null)
                    msg += $"  [INNER] {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n  {ex.InnerException.StackTrace}\n";
                System.IO.File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "vgs_crash.log"), msg);
            }
            catch { }

            await _dialogService.ShowMessageAsync("Error", $"Failed to open file: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc != null)
        {
            if (await activeDoc.SaveAsync())
            {
                await NotifyLspDocumentSavedAsync(activeDoc);

                // Invalidate blame cache for saved file and refresh
                if (activeDoc.FilePath != null)
                {
                    _blameCache.Remove(activeDoc.FilePath);
                    _ = UpdateBlameForCurrentLineAsync(activeDoc);
                }
            }
        }
    }

    private async Task NotifyLspDocumentSavedAsync(CodeEditorDocumentViewModel doc)
    {
        if (_languageService.IsConnected && doc.FilePath != null &&
            (doc.FilePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
             doc.FilePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)))
        {
            try { await _languageService.SaveDocumentAsync(doc.FilePath, doc.Text); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        foreach (var doc in _openDocuments.Values.Where(d => d.IsDirty))
        {
            if (await doc.SaveAsync())
            {
                await NotifyLspDocumentSavedAsync(doc);

                // Invalidate blame cache for saved files
                if (doc.FilePath != null)
                {
                    _blameCache.Remove(doc.FilePath);
                }
            }
        }

        // Refresh blame for active document after saving all
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc != null)
        {
            _ = UpdateBlameForCurrentLineAsync(activeDoc);
        }

        if (_projectService.HasUnsavedChanges)
        {
            await _projectService.SaveProjectAsync();
        }
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (_projectService.CurrentProject == null)
        {
            await _dialogService.ShowMessageAsync("Build", "No project is open.",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        if (_buildService.IsBuilding)
        {
            return;
        }

        // Save all before building
        await SaveAllAsync();

        StatusText = "Building...";
        await _buildService.BuildProjectAsync(_projectService.CurrentProject);
    }

    [RelayCommand]
    private async Task RebuildAsync()
    {
        if (_projectService.CurrentProject == null) return;
        if (_buildService.IsBuilding) return;

        await SaveAllAsync();
        StatusText = "Rebuilding...";
        await _buildService.RebuildProjectAsync(_projectService.CurrentProject);
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        if (_projectService.CurrentProject == null) return;

        StatusText = "Cleaning...";
        await _buildService.CleanAsync(_projectService.CurrentProject);
        StatusText = "Clean completed";
    }

    [RelayCommand]
    private async Task CancelBuildAsync()
    {
        if (_buildService.IsBuilding)
        {
            await _buildService.CancelBuildAsync();
        }
    }

    [RelayCommand]
    private async Task ExitAsync()
    {
        // Check for unsaved changes
        var unsavedDocs = _openDocuments.Values.Where(d => d.IsDirty).ToList();
        if (unsavedDocs.Any() || _projectService.HasUnsavedChanges)
        {
            var result = await _dialogService.ShowMessageAsync(
                "Unsaved Changes",
                "You have unsaved changes. Save before exiting?",
                DialogButtons.YesNoCancel,
                DialogIcon.Question);

            if (result == DialogResult.Cancel) return;
            if (result == DialogResult.Yes)
            {
                await SaveAllAsync();
            }
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
        {
            lifetime.Shutdown();
        }
    }

    partial void OnCurrentConfigurationChanged(string value)
    {
        _buildService.CurrentConfiguration = new BuildConfiguration { Name = value };
    }

    // Debug event handlers
    private void OnDebugStateChanged(object? sender, DebugStateChangedEventArgs e)
    {
        // Must run on UI thread since we update observable properties bound to UI
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnDebugStateChanged(sender, e));
            return;
        }

        IsDebugging = e.NewState == DebugState.Running || e.NewState == DebugState.Paused;
        IsPaused = e.NewState == DebugState.Paused;

        // Propagate debug-paused state to all open documents so hover can
        // prioritize debug data tips over LSP hover info
        var debugPaused = e.NewState == DebugState.Paused;
        foreach (var doc in _openDocuments.Values)
        {
            doc.IsDebugPaused = debugPaused;
        }

        var targetLabel = string.IsNullOrEmpty(_debugTargetName) ? "" : $": {_debugTargetName}";
        DebugStatusText = e.NewState switch
        {
            DebugState.Running => $"Running{targetLabel}",
            DebugState.Paused => "Paused",
            DebugState.Stopped => "Stopped",
            _ => ""
        };

        if (e.NewState == DebugState.Stopped)
        {
            _currentFrameId = null;
            StatusText = "Ready";
            _debugTargetName = "";
            ClearAllInlineDebugValues();
            ClearAllExecutionLines();
            RestorePreDebugPanels();

            // Refresh breakpoint visuals: all revert to filled circles (verified) when not debugging
            OnBreakpointVisualsChanged(this, EventArgs.Empty);
        }
        else if (e.NewState == DebugState.Running || e.NewState == DebugState.Paused)
        {
            if (e.NewState == DebugState.Running)
            {
                _currentFrameId = null;
                StatusText = "Running...";
                // Clear inline values and execution line when resuming execution
                ClearAllInlineDebugValues();
                ClearAllExecutionLines();
            }

            // Auto-show debug panels on first transition into a debug state
            if (!_debugPanelsShown)
            {
                ShowDebugPanels();
                ShowNotification("Debug session started", "info");
            }
        }
    }

    /// <summary>
    /// Auto-shows debug-related panels when a debug session starts.
    /// Activates the Variables panel in the debug tool group, switches the Output
    /// panel to the Debug category, and ensures debug tools are visible.
    /// </summary>
    private void ShowDebugPanels()
    {
        _debugPanelsShown = true;

        // Remember current output category so we can restore it later
        _preDebugOutputCategory = OutputPanel.SelectedCategory;

        // Switch output panel to Debug category
        OutputPanel.SelectedCategory = OutputCategory.Debug;

        // Activate Output panel in the bottom-left tool group
        _dockFactory.ActivateTool("Output");

        // Activate Variables panel in the bottom-right debug tool group
        // (this is the most useful panel when paused at a breakpoint)
        _dockFactory.ActivateTool("Variables");
    }

    /// <summary>
    /// Restores the panel state from before the debug session started.
    /// Resets the output category and switches the debug tool group back to Breakpoints.
    /// </summary>
    private void RestorePreDebugPanels()
    {
        if (!_debugPanelsShown) return;
        _debugPanelsShown = false;

        // Restore the output category to what it was before debugging
        OutputPanel.SelectedCategory = _preDebugOutputCategory;

        // Switch debug tool group back to Breakpoints (the default non-debug view)
        _dockFactory.ActivateTool("Breakpoints");
    }

    private async void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        // Marshal to UI thread
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Bring IDE window to front when debugger stops
            if (App.MainWindow != null)
            {
                App.MainWindow.Activate();
                App.MainWindow.Topmost = true;
                App.MainWindow.Topmost = false;
            }

            // Navigate to the stopped location and build location string for status
            var frames = await _debugService.GetStackTraceAsync();
            var firstFrame = frames.FirstOrDefault();

            // Track current frame ID for debug hover evaluate requests
            _currentFrameId = firstFrame?.Id;

            var locationSuffix = "";
            if (firstFrame?.FilePath != null)
            {
                locationSuffix = $" \u2014 {Path.GetFileName(firstFrame.FilePath)}:{firstFrame.Line}";
            }

            StatusText = e.Reason switch
            {
                StopReason.Breakpoint => $"Breakpoint hit{locationSuffix}",
                StopReason.Step => $"Step{locationSuffix}",
                StopReason.Exception => $"Exception: {e.Text}{locationSuffix}",
                StopReason.Pause => $"Paused{locationSuffix}",
                StopReason.DataBreakpoint => $"Data breakpoint hit{locationSuffix}",
                StopReason.FunctionBreakpoint => $"Function breakpoint hit{locationSuffix}",
                _ => $"Stopped{locationSuffix}"
            };

            if (firstFrame?.FilePath != null)
            {
                // Clear ALL old execution line highlights before setting the new one.
                // This ensures the previous highlight is removed when stepping between
                // lines or across files, regardless of event ordering.
                ClearAllExecutionLines();

                await OpenFileAsync(firstFrame.FilePath);

                // Navigate to and highlight the current execution line
                if (_openDocuments.TryGetValue(firstFrame.FilePath, out var doc))
                {
                    doc.SetExecutionLine(firstFrame.Line);
                    doc.NavigateTo(firstFrame.Line);
                }

                // Show inline debug values for variables in scope
                await ShowInlineDebugValuesAsync(firstFrame);
            }
        });
    }

    /// <summary>
    /// Fetches variables from the debugger and displays them inline in the editor.
    /// </summary>
    private async Task ShowInlineDebugValuesAsync(StackFrameInfo frame)
    {
        try
        {
            if (frame.FilePath == null) return;

            // Get scopes for the top frame
            var scopes = await _debugService.GetScopesAsync(frame.Id);
            if (scopes.Count == 0) return;

            var allVariables = new List<VariableInfo>();
            foreach (var scope in scopes)
            {
                // Skip expensive scopes (e.g., globals) to avoid slow lookups
                if (scope.Expensive) continue;

                var variables = await _debugService.GetVariablesAsync(scope.VariablesReference);
                allVariables.AddRange(variables);
            }

            if (allVariables.Count == 0) return;

            // Get the document text to find where variables are referenced
            if (!_openDocuments.TryGetValue(frame.FilePath, out var doc)) return;

            var text = doc.Text;
            if (string.IsNullOrEmpty(text)) return;

            var lines = text.Split('\n');
            var inlineValues = new List<Documents.InlineDebugValueInfo>();

            // For each variable, find the lines near the stopped line where it appears
            var stoppedLine = frame.Line;
            // Search a window around the stopped line (up to 50 lines above)
            var searchStart = Math.Max(0, stoppedLine - 50);
            var searchEnd = Math.Min(lines.Length, stoppedLine);

            foreach (var variable in allVariables)
            {
                if (string.IsNullOrEmpty(variable.Name)) continue;

                // Truncate long values for display
                var displayValue = variable.Value;
                if (displayValue.Length > 80)
                {
                    displayValue = displayValue.Substring(0, 77) + "...";
                }

                // Find the last line before/at the stopped line that references this variable
                int lastReferenceLine = -1;
                for (int i = searchEnd - 1; i >= searchStart; i--)
                {
                    var line = lines[i];
                    // Check if the variable name appears as a whole word on this line
                    var idx = line.IndexOf(variable.Name, StringComparison.Ordinal);
                    while (idx >= 0)
                    {
                        // Verify it's a whole word match (not part of a larger identifier)
                        var before = idx > 0 ? line[idx - 1] : ' ';
                        var after = idx + variable.Name.Length < line.Length
                            ? line[idx + variable.Name.Length]
                            : ' ';

                        if (!char.IsLetterOrDigit(before) && before != '_' &&
                            !char.IsLetterOrDigit(after) && after != '_')
                        {
                            lastReferenceLine = i + 1; // Convert to 1-based
                            break;
                        }

                        idx = line.IndexOf(variable.Name, idx + 1, StringComparison.Ordinal);
                    }

                    if (lastReferenceLine > 0) break;
                }

                // If we found a reference line, show the value there;
                // otherwise show it on the stopped line itself
                var targetLine = lastReferenceLine > 0 ? lastReferenceLine : stoppedLine;

                // Avoid duplicating a variable on the same line
                if (!inlineValues.Any(v => v.Line == targetLine && v.Name == variable.Name))
                {
                    inlineValues.Add(new Documents.InlineDebugValueInfo
                    {
                        Line = targetLine,
                        Name = variable.Name,
                        Value = displayValue
                    });
                }
            }

            if (inlineValues.Count > 0)
            {
                doc.ShowInlineDebugValues(inlineValues);
            }
        }
        catch
        {
            // Don't let inline value display errors break the debug experience
        }
    }

    /// <summary>
    /// Clears inline debug values from all open documents.
    /// </summary>
    private void ClearAllInlineDebugValues()
    {
        foreach (var doc in _openDocuments.Values)
        {
            doc.ClearInlineDebugValues();
        }
    }

    private void ClearAllExecutionLines()
    {
        foreach (var doc in _openDocuments.Values)
        {
            doc.ClearExecutionLine();
        }
    }

    private async void OnCallStackFrameSelected(object? sender, StackFrameItem frame)
    {
        try
        {
            if (string.IsNullOrEmpty(frame.FilePath)) return;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await OpenFileAsync(frame.FilePath);

                // Clear previous execution line highlights, then set the new one
                ClearAllExecutionLines();

                if (_openDocuments.TryGetValue(frame.FilePath, out var doc))
                {
                    doc.SetExecutionLine(frame.Line);
                    doc.NavigateTo(frame.Line, frame.Column > 0 ? frame.Column : 1);
                }
            });
        }
        catch (Exception)
        {
            // Ignore navigation errors to prevent crashes
        }
    }

    private void OnDebugOutput(object? sender, DebugOutputEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            OutputPanel.AppendOutput(e.Output);
        });
    }

    // Debug commands
    [RelayCommand]
    private async Task StartDebuggingAsync()
    {
        if (_projectService.CurrentProject == null)
        {
            await _dialogService.ShowMessageAsync("Debug", "No project is open.",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        if (IsDebugging) return;

        // Build first
        await SaveAllAsync();
        StatusText = "Building before debug...";
        var buildResult = await _buildService.BuildProjectAsync(_projectService.CurrentProject);

        if (!buildResult.Success)
        {
            await _dialogService.ShowMessageAsync("Build Failed",
                "Cannot start debugging because the build failed.",
                DialogButtons.Ok, DialogIcon.Error);
            return;
        }

        // Use the executable path from build result
        if (string.IsNullOrEmpty(buildResult.ExecutablePath) || !File.Exists(buildResult.ExecutablePath))
        {
            await _dialogService.ShowMessageAsync("Debug", "No executable found after build.",
                DialogButtons.Ok, DialogIcon.Error);
            return;
        }

        _debugTargetName = Path.GetFileName(buildResult.ExecutablePath);
        StatusText = $"Debugging: {_debugTargetName}";
        OutputPanel.SelectedCategory = OutputCategory.Debug;
        OutputPanel.AppendOutput($"\n========== Debugging: {_debugTargetName} ==========\n");

        var config = new DebugConfiguration
        {
            Program = buildResult.ExecutablePath,  // Pass compiled .exe, not .bas
            WorkingDirectory = _projectService.CurrentProject.ProjectDirectory
        };

        // Collect all breakpoints to send to the debug adapter
        var breakpoints = Breakpoints.GetAllBreakpoints();

        var success = await _debugService.StartDebuggingAsync(config, breakpoints);
        if (!success)
        {
            OutputPanel.AppendOutput("Failed to start debugger.\n");
            StatusText = "Debug failed";
        }
    }

    [RelayCommand]
    private async Task StartWithoutDebuggingAsync()
    {
        if (_projectService.CurrentProject == null) return;
        if (IsDebugging) return;

        await SaveAllAsync();
        var buildResult = await _buildService.BuildProjectAsync(_projectService.CurrentProject);
        if (!buildResult.Success) return;

        // Use the executable path from build result
        if (string.IsNullOrEmpty(buildResult.ExecutablePath) || !File.Exists(buildResult.ExecutablePath))
        {
            OutputPanel.AppendOutput("Error: No executable found after build.\n");
            return;
        }

        var config = new DebugConfiguration
        {
            Program = buildResult.ExecutablePath,
            WorkingDirectory = _projectService.CurrentProject.ProjectDirectory
        };

        OutputPanel.SelectedCategory = OutputCategory.General;  // Switch to General output to see program output
        OutputPanel.AppendOutput($"\n========== Running: {Path.GetFileName(buildResult.ExecutablePath)} ==========\n");

        var success = await _debugService.StartWithoutDebuggingAsync(config);
        if (!success)
        {
            OutputPanel.AppendOutput("Failed to start program.\n");
        }
    }

    [RelayCommand]
    private async Task RunInExternalConsoleAsync()
    {
        if (_projectService.CurrentProject == null) return;

        await SaveAllAsync();
        var buildResult = await _buildService.BuildProjectAsync(_projectService.CurrentProject);
        if (!buildResult.Success) return;

        if (string.IsNullOrEmpty(buildResult.ExecutablePath) || !File.Exists(buildResult.ExecutablePath))
        {
            OutputPanel.AppendOutput("Error: No executable found after build.\n");
            return;
        }

        try
        {
            // Create a batch file to run the exe and pause
            var batchPath = Path.Combine(Path.GetTempPath(), "vgs_run.bat");
            var exePath = buildResult.ExecutablePath;
            var batchContent = $"@echo off\r\n" +
                $"echo Running: {Path.GetFileName(exePath)}\r\n" +
                $"echo.\r\n" +
                $"cd /d \"{_projectService.CurrentProject.ProjectDirectory}\"\r\n" +
                $"call \"{exePath}\"\r\n" +
                $"echo.\r\n" +
                $"echo Exit code: %ERRORLEVEL%\r\n" +
                $"echo.\r\n" +
                $"echo Program finished. Press any key to close...\r\n" +
                $"pause > nul";
            File.WriteAllText(batchPath, batchContent);

            OutputPanel.AppendOutput($"Exe path: {exePath}\n");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = batchPath,
                UseShellExecute = true,
                WorkingDirectory = _projectService.CurrentProject.ProjectDirectory
            };

            System.Diagnostics.Process.Start(startInfo);
            OutputPanel.AppendOutput($"Launched in external console: {Path.GetFileName(buildResult.ExecutablePath)}\n");
        }
        catch (Exception ex)
        {
            OutputPanel.AppendOutput($"Failed to launch external console: {ex.Message}\n");
        }
    }

    [RelayCommand]
    private async Task StopDebuggingAsync()
    {
        if (!IsDebugging) return;
        await _debugService.StopDebuggingAsync();
    }

    [RelayCommand]
    private async Task RestartDebuggingAsync()
    {
        if (!IsDebugging) return;
        await _debugService.RestartAsync();
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (!IsPaused) return;
        StatusText = "Running...";
        await _debugService.ContinueAsync();
    }

    [RelayCommand]
    private async Task StepOverAsync()
    {
        if (!IsPaused) return;
        StatusText = "Stepping...";
        await _debugService.StepOverAsync();
    }

    [RelayCommand]
    private async Task StepIntoAsync()
    {
        if (!IsPaused) return;
        StatusText = "Stepping into...";
        await _debugService.StepIntoAsync();
    }

    [RelayCommand]
    private async Task StepOutAsync()
    {
        if (!IsPaused) return;
        StatusText = "Stepping out...";
        await _debugService.StepOutAsync();
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (!IsDebugging || IsPaused) return;
        StatusText = "Pausing...";
        await _debugService.PauseAsync();
    }

    [RelayCommand]
    private async Task RunToCursorAsync()
    {
        if (!IsPaused) return;

        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get existing breakpoints for this file to preserve them
        var existingBreakpoints = Breakpoints.GetBreakpointsForFile(activeDoc.FilePath)
            .Select(bp => new SourceBreakpoint
            {
                Line = bp.Line,
                Condition = bp.Condition,
                HitCondition = bp.HitCondition,
                LogMessage = bp.LogMessage
            });

        await _debugService.RunToCursorAsync(activeDoc.FilePath, activeDoc.CaretLine, existingBreakpoints);
    }

    [RelayCommand]
    private async Task SetNextStatementAsync()
    {
        if (!IsPaused) return;

        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        await _debugService.SetNextStatementAsync(activeDoc.FilePath, activeDoc.CaretLine);
    }

    [RelayCommand]
    private void ToggleBreakpoint()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath != null)
        {
            Breakpoints.ToggleBreakpoint(activeDoc.FilePath, activeDoc.CaretLine);
        }
    }

    [RelayCommand]
    private async Task NewFunctionBreakpointAsync()
    {
        var result = await _dialogService.ShowFunctionBreakpointDialogAsync();
        if (!string.IsNullOrEmpty(result))
        {
            await Breakpoints.AddFunctionBreakpointAsync(result);
            _dockFactory.ActivateTool("Breakpoints");
        }
    }

    [RelayCommand]
    private async Task AddDataBreakpointAsync()
    {
        // Get the selected variable from the Variables panel
        var selectedVar = Variables.SelectedVariable;

        if (selectedVar == null || selectedVar.IsScope)
        {
            await _dialogService.ShowMessageAsync("Data Breakpoint",
                "Select a variable in the Variables panel first.",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        var variableName = selectedVar.Name;
        var variablesReference = selectedVar.VariablesReference;

        // Query the debug adapter for data breakpoint support
        var accessInfo = await _debugService.GetDataBreakpointInfoAsync(variablesReference, variableName);
        if (accessInfo == null || string.IsNullOrEmpty(accessInfo.DataId))
        {
            await _dialogService.ShowMessageAsync("Data Breakpoint",
                $"The variable '{variableName}' does not support data breakpoints.",
                DialogButtons.Ok, DialogIcon.Warning);
            return;
        }

        // Determine access type: prefer "write" if available, otherwise use the first available
        var accessType = accessInfo.AccessTypes.Contains("write") ? "write" :
                         accessInfo.AccessTypes.FirstOrDefault() ?? "write";

        // If multiple access types are supported, let the user choose
        if (accessInfo.AccessTypes.Count > 1)
        {
            var choice = await _dialogService.ShowListSelectionAsync(
                "Data Breakpoint Access Type",
                $"Break when '{variableName}' is:",
                accessInfo.AccessTypes.Select(at => at switch
                {
                    "write" => "Written to",
                    "read" => "Read from",
                    "readWrite" => "Read from or written to",
                    _ => at
                }));

            if (choice >= 0 && choice < accessInfo.AccessTypes.Count)
            {
                accessType = accessInfo.AccessTypes[choice];
            }
            else
            {
                return; // User cancelled
            }
        }

        await Breakpoints.AddDataBreakpointAsync(accessInfo.DataId, variableName, accessType);
        _dockFactory.ActivateTool("Breakpoints");
        StatusText = $"Data breakpoint set on '{variableName}' ({accessType})";
    }

    [RelayCommand]
    private async Task ShowExceptionSettingsAsync()
    {
        var result = await _dialogService.ShowExceptionSettingsDialogAsync(_currentExceptionSettings);
        if (result != null)
        {
            _currentExceptionSettings = result;

            // Apply exception breakpoints to debug service
            var filters = new List<string>();
            var filterOptions = new List<ExceptionFilterOption>();

            foreach (var setting in result.Where(s => s.BreakWhenThrown))
            {
                if (setting.ExceptionType == "All Exceptions")
                {
                    filters.Add("all");
                }
                else if (setting.ExceptionType == "Runtime Exceptions" ||
                         setting.ExceptionType == "IO Exceptions" ||
                         setting.ExceptionType == "User Exceptions")
                {
                    // Category-level filter: add "uncaught" if user-unhandled is also set
                    if (setting.BreakWhenUserUnhandled && !filters.Contains("uncaught"))
                    {
                        filters.Add("uncaught");
                    }
                }
                else
                {
                    // Individual exception type: send as a filter option with condition
                    if (!filters.Contains("thrown"))
                    {
                        filters.Add("thrown");
                    }
                    filterOptions.Add(new ExceptionFilterOption
                    {
                        FilterId = "thrown",
                        Condition = setting.ExceptionType
                    });
                }
            }

            // Also add user-unhandled filter for settings that only have BreakWhenUserUnhandled
            foreach (var setting in result.Where(s => !s.BreakWhenThrown && s.BreakWhenUserUnhandled))
            {
                if (setting.ExceptionType != "All Exceptions" &&
                    setting.ExceptionType != "Runtime Exceptions" &&
                    setting.ExceptionType != "IO Exceptions" &&
                    setting.ExceptionType != "User Exceptions")
                {
                    filterOptions.Add(new ExceptionFilterOption
                    {
                        FilterId = "uncaught",
                        Condition = setting.ExceptionType
                    });
                }
            }

            if (IsDebugging)
            {
                await _debugService.SetExceptionBreakpointsAsync(
                    filters,
                    filterOptions.Count > 0 ? filterOptions : null);
            }
        }
    }

    private List<ExceptionSettingResult>? _currentExceptionSettings;

    // View/Debug Window commands
    [RelayCommand]
    private void ShowBreakpoints()
    {
        _dockFactory.ActivateTool("Breakpoints");
    }

    [RelayCommand]
    private void ShowCallStack()
    {
        _dockFactory.ActivateTool("CallStack");
    }

    [RelayCommand]
    private void ShowVariables()
    {
        _dockFactory.ActivateTool("Variables");
    }

    [RelayCommand]
    private void ShowWatch()
    {
        _dockFactory.ActivateTool("Watch");
    }

    [RelayCommand]
    private void ShowOutput()
    {
        _dockFactory.ActivateTool("Output");
    }

    [RelayCommand]
    private void ShowErrorList()
    {
        _dockFactory.ActivateTool("ErrorList");
    }

    /// <summary>
    /// Tracks current position when cycling through diagnostics with F8/Shift+F8.
    /// </summary>
    private int _currentDiagnosticIndex = -1;

    /// <summary>
    /// Navigates to the next error/warning in the Error List (F8).
    /// Wraps around to the first diagnostic after the last one.
    /// </summary>
    [RelayCommand]
    private async Task GoToNextErrorAsync()
    {
        var diagnostics = ErrorList.Diagnostics;
        if (diagnostics.Count == 0)
        {
            StatusText = "No errors or warnings";
            return;
        }

        _currentDiagnosticIndex++;
        if (_currentDiagnosticIndex >= diagnostics.Count)
        {
            _currentDiagnosticIndex = 0;
        }

        var diagnostic = diagnostics[_currentDiagnosticIndex];
        ErrorList.SelectedDiagnostic = diagnostic;

        if (diagnostic.FilePath != null)
        {
            await OpenFileAndNavigateAsync(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);
        }

        StatusText = $"Error {_currentDiagnosticIndex + 1} of {diagnostics.Count}: {diagnostic.Message}";
    }

    /// <summary>
    /// Navigates to the previous error/warning in the Error List (Shift+F8).
    /// Wraps around to the last diagnostic before the first one.
    /// </summary>
    [RelayCommand]
    private async Task GoToPreviousErrorAsync()
    {
        var diagnostics = ErrorList.Diagnostics;
        if (diagnostics.Count == 0)
        {
            StatusText = "No errors or warnings";
            return;
        }

        _currentDiagnosticIndex--;
        if (_currentDiagnosticIndex < 0)
        {
            _currentDiagnosticIndex = diagnostics.Count - 1;
        }

        var diagnostic = diagnostics[_currentDiagnosticIndex];
        ErrorList.SelectedDiagnostic = diagnostic;

        if (diagnostic.FilePath != null)
        {
            await OpenFileAndNavigateAsync(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);
        }

        StatusText = $"Error {_currentDiagnosticIndex + 1} of {diagnostics.Count}: {diagnostic.Message}";
    }

    [RelayCommand]
    private void ShowSolutionExplorer()
    {
        _dockFactory.ActivateTool("SolutionExplorer");
    }

    [RelayCommand]
    private void ShowFindResults()
    {
        _dockFactory.ActivateTool("FindInFiles");
    }

    [RelayCommand]
    private void ShowTerminal()
    {
        _dockFactory.ActivateTool("Terminal");
    }

    [RelayCommand]
    private void CreateNewTerminal()
    {
        _dockFactory.ActivateTool("Terminal");
        Terminal.CreateNewSessionCommand.Execute(null);
    }

    /// <summary>
    /// Toggles Zen mode (distraction-free editing).
    /// Hides menu bar, toolbar, status bar, and side/bottom panels.
    /// Press Escape or re-toggle to exit.
    /// </summary>
    [RelayCommand]
    private void ToggleZenMode()
    {
        IsZenMode = !IsZenMode;
        StatusText = IsZenMode ? "Zen Mode — press Escape to exit" : "Ready";
    }

    /// <summary>
    /// Exits Zen mode if currently active. Called from code-behind on Escape key.
    /// </summary>
    public void ExitZenMode()
    {
        if (IsZenMode)
        {
            IsZenMode = false;
            StatusText = "Ready";
        }
    }

    [RelayCommand]
    private void ToggleWhitespace()
    {
        ShowWhitespace = !ShowWhitespace;
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleWhitespace(ShowWhitespace);
        }
        StatusText = ShowWhitespace ? "Whitespace characters visible" : "Whitespace characters hidden";
    }

    [RelayCommand]
    private void ToggleColumnSelectionMode()
    {
        IsColumnSelectionMode = !IsColumnSelectionMode;
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleColumnSelection(IsColumnSelectionMode);
        }
        StatusText = IsColumnSelectionMode ? "Column selection mode ON" : "Column selection mode OFF";
    }

    [RelayCommand]
    private async Task OpenCommandPaletteAsync()
    {
        await OpenCommandPaletteInternalAsync(ViewModels.Dialogs.CommandPaletteMode.Command);
    }

    [RelayCommand]
    private async Task OpenQuickOpenAsync()
    {
        await OpenCommandPaletteInternalAsync(ViewModels.Dialogs.CommandPaletteMode.File);
    }

    private async Task OpenCommandPaletteInternalAsync(ViewModels.Dialogs.CommandPaletteMode mode)
    {
        if (App.MainWindow == null) return;

        var vm = new ViewModels.Dialogs.CommandPaletteViewModel();
        vm.RegisterCommands(this);
        vm.RegisterFiles(_projectService);
        vm.Open(mode);

        var dialog = new Views.Dialogs.CommandPaletteDialog
        {
            DataContext = vm
        };

        var result = await dialog.ShowDialog<ViewModels.Dialogs.CommandPaletteItem?>(App.MainWindow);

        // Handle file open request from Quick Open mode
        if (dialog.SelectedFilePath != null)
        {
            try
            {
                await OpenFileFromLinkAsync(dialog.SelectedFilePath);
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to open file: {ex.Message}";
            }
            return;
        }

        // Handle go-to-line request
        if (dialog.RequestedLineNumber > 0)
        {
            try
            {
                GoToLineCommand.Execute(null);
            }
            catch (Exception ex)
            {
                StatusText = $"Go to line failed: {ex.Message}";
            }
            return;
        }

        if (result?.Execute != null)
        {
            try
            {
                result.Execute();
            }
            catch (Exception ex)
            {
                StatusText = $"Command failed: {ex.Message}";
            }
        }
    }

    [RelayCommand]
    private async Task ShowKeyboardShortcutsAsync()
    {
        if (App.MainWindow == null) return;

        var vm = new ViewModels.Dialogs.KeyboardShortcutsViewModel();
        vm.LoadFromCommandPalette(this);

        var dialog = new Views.Dialogs.KeyboardShortcutsDialog
        {
            DataContext = vm
        };

        await dialog.ShowDialog(App.MainWindow);
    }

    [RelayCommand]
    private async Task SettingsAsync()
    {
        if (App.MainWindow == null) return;

        var vm = new ViewModels.Dialogs.SettingsViewModel();

        var dialog = new Views.Dialogs.SettingsDialog
        {
            DataContext = vm
        };

        await dialog.ShowDialog(App.MainWindow);
    }

    [RelayCommand]
    private void ShowImmediateWindow()
    {
        _dockFactory.ActivateTool("ImmediateWindow");
    }

    [RelayCommand]
    private async Task GoToLineAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc == null) return;

        var totalLines = activeDoc.TotalLines;
        if (totalLines < 1) totalLines = 1;

        var result = await _dialogService.ShowGoToLineDialogAsync(activeDoc.CaretLine, totalLines);
        if (result.HasValue)
        {
            activeDoc.NavigateTo(result.Value);
        }
    }

    [RelayCommand]
    private async Task GoToSymbolAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc == null) return;

        // Try LSP document symbols first for current document
        if (_languageService.IsConnected && activeDoc.FilePath != null)
        {
            try
            {
                var symbols = await _languageService.GetDocumentSymbolsAsync(activeDoc.FilePath);
                if (symbols.Count > 0)
                {
                    var symbolNames = FlattenSymbols(symbols).Select(s => $"{s.Name} ({s.Kind})").ToList();
                    var flatSymbols = FlattenSymbols(symbols).ToList();

                    var selected = await _dialogService.ShowListSelectionAsync(
                        "Go to Symbol",
                        "Select a symbol to navigate to:",
                        symbolNames);

                    if (selected >= 0 && selected < flatSymbols.Count)
                    {
                        activeDoc.NavigateTo(flatSymbols[selected].Line, flatSymbols[selected].Column);
                    }
                    return;
                }
            }
            catch { }
        }

        // Fallback to text-based parsing
        var result = await _dialogService.ShowGoToSymbolDialogAsync(activeDoc.Text, activeDoc.FilePath);
        if (result != null)
        {
            activeDoc.NavigateTo(result.Line, result.Column);
        }
    }

    private static IEnumerable<DocumentSymbol> FlattenSymbols(IEnumerable<DocumentSymbol> symbols)
    {
        foreach (var symbol in symbols)
        {
            yield return symbol;
            foreach (var child in FlattenSymbols(symbol.Children))
            {
                yield return child;
            }
        }
    }

    [RelayCommand]
    private async Task GoToWorkspaceSymbolAsync()
    {
        if (!_languageService.IsConnected)
        {
            StatusText = "Language server not connected";
            return;
        }

        var query = await _dialogService.ShowInputDialogAsync(
            "Go to Workspace Symbol",
            "Enter symbol name to search:",
            "");

        if (string.IsNullOrEmpty(query)) return;

        try
        {
            // Use LSP workspace/symbol request to search across the entire workspace
            var workspaceSymbols = await _languageService.GetWorkspaceSymbolsAsync(query);

            if (workspaceSymbols.Count == 0)
            {
                StatusText = $"No symbols matching '{query}' found";
                return;
            }

            var symbolNames = workspaceSymbols.Select(s =>
            {
                var container = string.IsNullOrEmpty(s.ContainerName) ? "" : $" ({s.ContainerName})";
                return $"{s.Name}{container} [{s.Kind}] - {Path.GetFileName(s.FilePath)}:{s.Line}";
            }).ToList();

            var selected = await _dialogService.ShowListSelectionAsync(
                "Workspace Symbols",
                $"Found {workspaceSymbols.Count} symbol(s) matching '{query}':",
                symbolNames);

            if (selected >= 0 && selected < workspaceSymbols.Count)
            {
                var sym = workspaceSymbols[selected];
                if (!string.IsNullOrEmpty(sym.FilePath))
                {
                    await OpenFileAsync(sym.FilePath);
                    var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
                    activeDoc?.NavigateTo(sym.Line, sym.Column);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Symbol search failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Find()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.ShowFind();
    }

    [RelayCommand]
    private void Replace()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.ShowReplace();
    }

    [RelayCommand]
    private void ShowFindInFiles()
    {
        _dockFactory.ActivateTool("FindInFiles");
    }

    #region Bookmarks

    private async void OnBookmarkNavigationRequested(object? sender, BookmarkNavigationEventArgs e)
    {
        await OpenFileAsync(e.FilePath);
        var doc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        doc?.NavigateTo(e.Line);
    }

    [RelayCommand]
    private void ToggleBookmark()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        _bookmarkService.ToggleBookmark(activeDoc.FilePath, activeDoc.CaretLine);
        StatusText = _bookmarkService.GetBookmarks(activeDoc.FilePath).Any(b => b.Line == activeDoc.CaretLine)
            ? $"Bookmark added at line {activeDoc.CaretLine}"
            : $"Bookmark removed from line {activeDoc.CaretLine}";
    }

    [RelayCommand]
    private async Task NextBookmarkAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        var next = _bookmarkService.GetNextBookmark(activeDoc.FilePath, activeDoc.CaretLine);
        if (next != null)
        {
            if (next.FilePath != activeDoc.FilePath)
            {
                await OpenFileAsync(next.FilePath);
            }
            var doc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
            doc?.NavigateTo(next.Line);
        }
        else
        {
            StatusText = "No bookmarks found";
        }
    }

    [RelayCommand]
    private async Task PreviousBookmarkAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        var prev = _bookmarkService.GetPreviousBookmark(activeDoc.FilePath, activeDoc.CaretLine);
        if (prev != null)
        {
            if (prev.FilePath != activeDoc.FilePath)
            {
                await OpenFileAsync(prev.FilePath);
            }
            var doc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
            doc?.NavigateTo(prev.Line);
        }
        else
        {
            StatusText = "No bookmarks found";
        }
    }

    [RelayCommand]
    private void ClearAllBookmarks()
    {
        _bookmarkService.ClearBookmarks();
        StatusText = "All bookmarks cleared";
    }

    [RelayCommand]
    private void ShowBookmarks()
    {
        _dockFactory.ActivateTool("Bookmarks");
    }

    #endregion

    #region Go to Definition

    [RelayCommand]
    private async Task GoToTypeDefinitionAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null || !_languageService.IsConnected) return;

        var location = await _languageService.GetTypeDefinitionAsync(
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        if (location != null)
        {
            await NavigateToLocationAsync(location);
        }
        else
        {
            StatusText = "No type definition found";
        }
    }

    [RelayCommand]
    private async Task GoToDefinitionAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Try language service first
        if (_languageService.IsConnected)
        {
            var location = await _languageService.GetDefinitionAsync(
                activeDoc.FilePath,
                activeDoc.CaretLine,
                activeDoc.CaretColumn);

            if (location != null)
            {
                await NavigateToLocationAsync(location);
                return;
            }
        }

        // Fall back to simple symbol search
        await GoToDefinitionFallbackAsync(activeDoc);
    }

    [RelayCommand]
    private async Task GoToImplementationAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        if (_languageService.IsConnected)
        {
            var location = await _languageService.GetImplementationAsync(
                activeDoc.FilePath,
                activeDoc.CaretLine,
                activeDoc.CaretColumn);

            if (location != null)
            {
                await NavigateToLocationAsync(location);
                return;
            }
        }

        StatusText = "No implementation found";
    }

    private async Task GoToDefinitionFallbackAsync(CodeEditorDocumentViewModel activeDoc)
    {
        if (activeDoc?.FilePath == null) return;

        // Get the word under cursor
        var word = GetWordAtCaret(activeDoc);
        if (string.IsNullOrEmpty(word))
        {
            StatusText = "No symbol at cursor position";
            return;
        }

        // Search patterns for BasicLang definitions
        var patterns = new[]
        {
            $@"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?Sub\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*\(",
            $@"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?Function\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*\(",
            $@"^\s*(?:Public\s+|Private\s+)?Class\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*$",
            $@"^\s*(?:Public\s+)?Module\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*$",
            $@"^\s*(?:Public\s+|Private\s+)?Interface\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*$",
            $@"^\s*(?:Public\s+|Private\s+)?Enum\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*$",
            $@"^\s*(?:Public\s+|Private\s+)?(?:Shared\s+)?Property\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s*(?:\(|As)",
            $@"^\s*(?:Public\s+|Private\s+)?Const\s+{System.Text.RegularExpressions.Regex.Escape(word)}\s+As",
            $@"^\s*Namespace\s+{System.Text.RegularExpressions.Regex.Escape(word)}",
        };

        // First search in the current file
        var result = SearchForDefinition(activeDoc.Text, activeDoc.FilePath, patterns);
        if (result != null)
        {
            activeDoc.NavigateTo(result.Line, result.Column);
            StatusText = $"Found definition at line {result.Line}";
            return;
        }

        // Search in project files if we have a project
        if (_projectService.CurrentProject != null)
        {
            var projectDir = Path.GetDirectoryName(_projectService.CurrentProject.FilePath);
            if (projectDir != null)
            {
                var files = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(projectDir, "*.bl", SearchOption.AllDirectories))
                    .Where(f => !f.Equals(activeDoc.FilePath, StringComparison.OrdinalIgnoreCase));

                foreach (var file in files)
                {
                    try
                    {
                        var content = await _fileService.ReadFileAsync(file);
                        result = SearchForDefinition(content, file, patterns);
                        if (result != null)
                        {
                            await NavigateToLocationAsync(result);
                            StatusText = $"Found definition in {Path.GetFileName(file)} at line {result.Line}";
                            return;
                        }
                    }
                    catch
                    {
                        // Skip files we can't read
                    }
                }
            }
        }

        StatusText = $"Definition for '{word}' not found";
    }

    private string? GetWordAtCaret(CodeEditorDocumentViewModel doc)
    {
        var text = doc.Text;
        var lines = text.Split('\n');
        if (doc.CaretLine < 1 || doc.CaretLine > lines.Length) return null;

        var line = lines[doc.CaretLine - 1];
        var col = doc.CaretColumn - 1;
        if (col < 0 || col >= line.Length) return null;

        // Find word boundaries
        var start = col;
        var end = col;

        while (start > 0 && IsWordChar(line[start - 1])) start--;
        while (end < line.Length && IsWordChar(line[end])) end++;

        if (start >= end) return null;
        return line.Substring(start, end - start);
    }

    private static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private static LocationInfo? SearchForDefinition(string content, string filePath, string[] patterns)
    {
        var lines = content.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var pattern in patterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(line, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    return new LocationInfo
                    {
                        Uri = filePath,
                        Line = i + 1,
                        Column = 1
                    };
                }
            }
        }

        return null;
    }

    private async Task NavigateToLocationAsync(LocationInfo location)
    {
        // Convert URI to file path if needed
        var filePath = location.Uri;
        if (filePath.StartsWith("file:///"))
        {
            filePath = new Uri(filePath).LocalPath;
        }

        await OpenFileAsync(filePath);
        var doc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        doc?.NavigateTo(location.Line, location.Column);
    }

    #endregion

    #region Find All References

    [RelayCommand]
    private async Task FindReferencesAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the word under cursor
        var word = GetWordAtCaret(activeDoc);
        if (string.IsNullOrEmpty(word))
        {
            StatusText = "No symbol at cursor position";
            return;
        }

        StatusText = $"Finding references for '{word}'...";

        // Try language service first
        if (_languageService.IsConnected)
        {
            var references = await _languageService.FindReferencesAsync(
                activeDoc.FilePath,
                activeDoc.CaretLine,
                activeDoc.CaretColumn);

            if (references.Count > 0)
            {
                await ShowReferencesInFindPanel(word, references);
                return;
            }
        }

        // Fall back to simple text search
        await FindReferencesFallbackAsync(word);
    }

    private async Task FindReferencesFallbackAsync(string symbol)
    {
        if (_projectService.CurrentProject == null)
        {
            StatusText = "No project open";
            return;
        }

        var projectDir = Path.GetDirectoryName(_projectService.CurrentProject.FilePath);
        if (projectDir == null) return;

        var results = new List<LocationInfo>();

        // Search all BasicLang files in the project
        var files = Directory.GetFiles(projectDir, "*.bas", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(projectDir, "*.bl", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(projectDir, "*.basic", SearchOption.AllDirectories));

        // Create word boundary pattern for the symbol
        var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(symbol)}\b";
        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var file in files)
        {
            try
            {
                var content = await _fileService.ReadFileAsync(file);
                var lines = content.Split('\n');

                for (var i = 0; i < lines.Length; i++)
                {
                    var matches = regex.Matches(lines[i]);
                    foreach (System.Text.RegularExpressions.Match match in matches)
                    {
                        results.Add(new LocationInfo
                        {
                            Uri = file,
                            Line = i + 1,
                            Column = match.Index + 1,
                            EndColumn = match.Index + match.Length + 1
                        });
                    }
                }
            }
            catch
            {
                // Skip files we can't read
            }
        }

        if (results.Count > 0)
        {
            await ShowReferencesInFindPanel(symbol, results);
        }
        else
        {
            StatusText = $"No references found for '{symbol}'";
        }
    }

    private async Task ShowReferencesInFindPanel(string symbol, IReadOnlyList<LocationInfo> references)
    {
        // Group by file
        var groups = references.GroupBy(r => r.Uri);

        FindInFiles.ResultGroups.Clear();
        FindInFiles.SearchText = symbol;

        foreach (var group in groups)
        {
            var filePath = group.Key;
            var fileName = Path.GetFileName(filePath);

            var resultGroup = new FindResultGroup
            {
                FilePath = filePath,
                FileName = fileName,
                MatchCount = group.Count()
            };

            // Read file to get preview text
            string? content = null;
            try
            {
                content = await _fileService.ReadFileAsync(filePath);
            }
            catch
            {
                // Continue without preview
            }

            var lines = content?.Split('\n') ?? Array.Empty<string>();

            foreach (var location in group)
            {
                var previewText = "";
                if (lines.Length >= location.Line)
                {
                    previewText = lines[location.Line - 1].Trim();
                    if (previewText.Length > 150)
                    {
                        previewText = previewText.Substring(0, 147) + "...";
                    }
                }

                resultGroup.Results.Add(new FindResult
                {
                    FilePath = filePath,
                    FileName = fileName,
                    Line = location.Line,
                    Column = location.Column,
                    PreviewText = previewText
                });
            }

            FindInFiles.ResultGroups.Add(resultGroup);
        }

        // Activate the Find in Files panel
        _dockFactory.ActivateTool("FindInFiles");

        var totalRefs = references.Count;
        var fileCount = groups.Count();
        StatusText = $"Found {totalRefs} reference{(totalRefs == 1 ? "" : "s")} in {fileCount} file{(fileCount == 1 ? "" : "s")}";
    }

    #endregion

    #region Line Operations

    [RelayCommand]
    private void ToggleComment()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestToggleComment();
    }

    [RelayCommand]
    private void DuplicateLine()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestDuplicateLine();
    }

    [RelayCommand]
    private void MoveLineUp()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestMoveLineUp();
    }

    [RelayCommand]
    private void MoveLineDown()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestMoveLineDown();
    }

    [RelayCommand]
    private void DeleteLine()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestDeleteLine();
    }

    #endregion

    #region Refactoring

    [RelayCommand]
    private async Task RenameSymbolAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the word under cursor
        var word = GetWordAtCaret(activeDoc);
        if (string.IsNullOrEmpty(word))
        {
            StatusText = "No symbol at cursor position";
            return;
        }

        // Try LSP rename first (supports cross-file rename via WorkspaceEdit)
        if (_languageService.IsConnected)
        {
            var newName = await _dialogService.ShowInputDialogAsync(
                "Rename Symbol",
                $"Rename '{word}' to:",
                word);

            if (string.IsNullOrEmpty(newName) || newName == word) return;

            var lspEdit = await _languageService.RenameAsync(
                activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, newName);

            if (lspEdit != null)
            {
                await ApplyWorkspaceEditAsync(lspEdit);
                StatusText = $"Renamed '{word}' to '{newName}' via LSP";
                return;
            }
        }

        // Fall back to refactoring service rename
        var viewModel = new RenameDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, word);

        var dialog = new Views.Dialogs.RenameDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<RenameResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload affected files
            foreach (var fileEdit in result.FileEdits)
            {
                if (_openDocuments.TryGetValue(fileEdit.FilePath, out var doc))
                {
                    var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
                    doc.SetContent(content);
                }
            }

            StatusText = $"Renamed '{word}' to '{viewModel.NewName}' in {result.FileEdits.Count} file(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Rename failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ExtractMethodAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the selection info from the view
        // We need to get this from the CodeEditorDocumentView
        var selectionInfo = GetSelectionFromActiveDocument();
        if (selectionInfo == null)
        {
            StatusText = "No code selected. Please select the code to extract.";
            return;
        }

        // Create and show the extract method dialog
        var viewModel = new ExtractMethodDialogViewModel(_refactoringService, _fileService);
        viewModel.Initialize(
            activeDoc.FilePath,
            selectionInfo.StartLine,
            selectionInfo.StartColumn,
            selectionInfo.EndLine,
            selectionInfo.EndColumn,
            selectionInfo.SelectedText);

        var dialog = new Views.Dialogs.ExtractMethodDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<ExtractMethodResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Extracted method '{viewModel.MethodName}'";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Extract method failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task InlineMethodAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the word under cursor (method name)
        var word = GetWordAtCaret(activeDoc);
        if (string.IsNullOrEmpty(word))
        {
            StatusText = "No method at cursor position";
            return;
        }

        // Create and show the inline method dialog
        var viewModel = new InlineMethodDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, word);

        var dialog = new Views.Dialogs.InlineMethodDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<InlineMethodResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload affected files
            foreach (var fileEdit in result.FileEdits)
            {
                if (_openDocuments.TryGetValue(fileEdit.FilePath, out var doc))
                {
                    var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
                    doc.SetContent(content);
                }
            }

            var removedText = result.DefinitionRemoved ? " and removed definition" : "";
            StatusText = $"Inlined {result.CallSitesInlined} call site(s){removedText}";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Inline method failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task IntroduceVariableAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the selection info from the view
        var selectionInfo = GetSelectionFromActiveDocument();
        if (selectionInfo == null)
        {
            StatusText = "No expression selected. Please select the expression to extract.";
            return;
        }

        // Create and show the introduce variable dialog
        var viewModel = new IntroduceVariableDialogViewModel(_refactoringService, _fileService);
        viewModel.Initialize(
            activeDoc.FilePath,
            selectionInfo.StartLine,
            selectionInfo.StartColumn,
            selectionInfo.EndLine,
            selectionInfo.EndColumn,
            selectionInfo.SelectedText);

        var dialog = new Views.Dialogs.IntroduceVariableDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<IntroduceVariableResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            var countText = result.OccurrencesReplaced > 1 ? $" ({result.OccurrencesReplaced} occurrences)" : "";
            StatusText = $"Introduced variable '{viewModel.VariableName}'{countText}";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Introduce variable failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ExtractConstantAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the selection info from the view
        var selectionInfo = GetSelectionFromActiveDocument();
        if (selectionInfo == null)
        {
            StatusText = "No literal selected. Please select a string, number, or boolean value.";
            return;
        }

        // Create and show the extract constant dialog
        var viewModel = new ExtractConstantDialogViewModel(
            _refactoringService,
            _fileService,
            activeDoc.FilePath,
            selectionInfo.StartLine,
            selectionInfo.StartColumn,
            selectionInfo.EndLine,
            selectionInfo.EndColumn);

        var dialog = new Views.Dialogs.ExtractConstantDialog(viewModel);
        var result = await dialog.ShowDialog<bool>(App.MainWindow);

        if (result)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            var countText = viewModel.OccurrenceCount > 1 && viewModel.ReplaceAllOccurrences
                ? $" ({viewModel.OccurrenceCount} occurrences replaced)"
                : "";
            StatusText = $"Extracted constant '{viewModel.ConstantName}'{countText}";
        }
    }

    [RelayCommand]
    private async Task InlineConstantAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the inline constant dialog
        var viewModel = new InlineConstantDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.InlineConstantDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            var countText = viewModel.ReferenceCount > 1 ? $" ({viewModel.ReferenceCount} references inlined)" : "";
            StatusText = $"Inlined constant '{viewModel.ConstantName}'{countText}";
        }
    }

    private async Task InlineVariableAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the inline variable dialog
        var viewModel = new InlineVariableDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.InlineVariableDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Variable inlined successfully";
        }
    }

    [RelayCommand]
    private async Task SafeDeleteAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the safe delete dialog
        var viewModel = new SafeDeleteDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.SafeDeleteDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Deleted '{viewModel.SymbolName}'";
        }
    }

    [RelayCommand]
    private async Task PullMembersUpAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the pull members up dialog
        var viewModel = new PullMembersUpDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.PullMembersUpDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Pulled {viewModel.Members.Count(m => m.IsSelected)} member(s) to '{viewModel.SelectedDestination?.Name}'";
        }
    }

    [RelayCommand]
    private async Task PushMembersDownAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the push members down dialog
        var viewModel = new PushMembersDownDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.PushMembersDownDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload all open files that may have been modified
            foreach (var filePath in _openDocuments.Keys.ToList())
            {
                try
                {
                    var content = await _fileService.ReadFileAsync(filePath);
                    _openDocuments[filePath].SetContent(content);
                }
                catch (Exception) { /* Ignore errors for files that may not exist */ }
            }

            var selectedMemberCount = viewModel.Members.Count(m => m.IsSelected);
            var selectedDestCount = viewModel.Destinations.Count(d => d.IsSelected);
            StatusText = $"Pushed {selectedMemberCount} member(s) to {selectedDestCount} derived class(es)";
        }
    }

    [RelayCommand]
    private async Task UseBaseTypeAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the use base type dialog
        var viewModel = new UseBaseTypeDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.UseBaseTypeDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Changed type from '{viewModel.CurrentType}' to '{viewModel.SelectedBaseType?.TypeName}'";
        }
    }

    [RelayCommand]
    private async Task ConvertToInterfaceAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the convert to interface dialog
        var viewModel = new ConvertToInterfaceDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ConvertToInterfaceDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<ConvertToInterfaceResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            // Open the new interface file if created in separate file
            if (result.InterfaceFilePath != null && result.InterfaceFilePath != activeDoc.FilePath)
            {
                await OpenFileAsync(result.InterfaceFilePath);
            }

            StatusText = $"Created interface '{result.InterfaceName}' with {result.MembersIncluded} member(s)";
        }
    }

    [RelayCommand]
    private async Task InvertIfAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get invert if info
        var info = await _refactoringService.GetInvertIfInfoAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);
        if (info == null)
        {
            StatusText = "No If statement found at cursor position";
            return;
        }

        // Perform the inversion
        var result = await _refactoringService.InvertIfAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        if (result.Success)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "If statement inverted";
        }
        else
        {
            StatusText = $"Invert If failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ConvertToSelectCaseAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get convert info
        var info = await _refactoringService.GetConvertToSelectCaseInfoAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);
        if (info == null)
        {
            StatusText = "No convertible If-ElseIf chain found at cursor position";
            return;
        }

        // Perform the conversion
        var result = await _refactoringService.ConvertToSelectCaseAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        if (result.Success)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Converted {result.CasesCreated} cases to Select Case";
        }
        else
        {
            StatusText = $"Convert to Select Case failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task SplitDeclarationAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get split info
        var info = await _refactoringService.GetSplitDeclarationInfoAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);
        if (info == null)
        {
            StatusText = "No declaration with initializer found at cursor position";
            return;
        }

        // Perform the split
        var result = await _refactoringService.SplitDeclarationAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        if (result.Success)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Split declaration for '{info.VariableName}'";
        }
        else
        {
            StatusText = $"Split declaration failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task IntroduceFieldAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get field info
        var info = await _refactoringService.GetIntroduceFieldInfoAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);
        if (info == null)
        {
            StatusText = "No local variable found at cursor position";
            return;
        }

        // Use suggested field name from info, or generate one
        var fieldName = !string.IsNullOrEmpty(info.SuggestedFieldName)
            ? info.SuggestedFieldName
            : "_" + char.ToLower(info.VariableName[0]) + info.VariableName.Substring(1);

        // For now, use default options - could add a dialog later
        var options = new IntroduceFieldOptions
        {
            FieldName = fieldName,
            Accessibility = FieldAccessibility.Private,
            InitializeInline = true,
            RemoveLocalVariable = true
        };

        // Perform the introduction
        var result = await _refactoringService.IntroduceFieldAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, options);

        if (result.Success)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Introduced field '{options.FieldName}'";
        }
        else
        {
            StatusText = $"Introduce field failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ChangeSignatureAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the word under cursor (method name)
        var word = GetWordAtCaret(activeDoc);
        if (string.IsNullOrEmpty(word))
        {
            StatusText = "No method at cursor position";
            return;
        }

        // Create and show the change signature dialog
        var viewModel = new ChangeSignatureDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, word);

        var dialog = new Views.Dialogs.ChangeSignatureDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<ChangeSignatureResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload affected files
            foreach (var fileEdit in result.FileEdits)
            {
                if (_openDocuments.TryGetValue(fileEdit.FilePath, out var doc))
                {
                    var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
                    doc.SetContent(content);
                }
            }

            StatusText = $"Changed signature, updated {result.CallSitesUpdated} call site(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Change signature failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task EncapsulateFieldAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Get the word under cursor (field name)
        var word = GetWordAtCaret(activeDoc);
        if (string.IsNullOrEmpty(word))
        {
            StatusText = "No field at cursor position";
            return;
        }

        // Create and show the encapsulate field dialog
        var viewModel = new EncapsulateFieldDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, word);

        var dialog = new Views.Dialogs.EncapsulateFieldDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<EncapsulateFieldResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload affected files
            foreach (var fileEdit in result.FileEdits)
            {
                if (_openDocuments.TryGetValue(fileEdit.FilePath, out var doc))
                {
                    var content = await _fileService.ReadFileAsync(fileEdit.FilePath);
                    doc.SetContent(content);
                }
            }

            var refText = result.ReferencesUpdated > 0 ? $", updated {result.ReferencesUpdated} reference(s)" : "";
            StatusText = $"Encapsulated field '{result.FieldName}' as property '{result.PropertyName}'{refText}";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Encapsulate field failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task InlineFieldAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the inline field dialog
        var viewModel = new InlineFieldDialogViewModel(_refactoringService, activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.InlineFieldDialog(viewModel);

        var result = await dialog.ShowDialog<bool>(App.MainWindow);

        if (result)
        {
            // Reload the file to show changes
            var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
            activeDoc.SetContent(content);

            StatusText = $"Inlined field '{viewModel.FieldName}'";
        }
    }

    [RelayCommand]
    private async Task MoveTypeToFileAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the move type to file dialog
        var viewModel = new MoveTypeToFileDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.MoveTypeToFileDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<MoveTypeToFileResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Write the new file
            await _fileService.WriteFileAsync(result.NewFilePath, result.NewFileContent);

            // Apply edits to original file if type was removed
            if (result.OriginalFileEdit != null)
            {
                if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
                {
                    var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                    doc.SetContent(content);
                }
            }

            // Add new file to project if requested
            if (viewModel.AddToProject && _projectService.CurrentProject != null)
            {
                await _projectService.AddFileToProjectAsync(result.NewFilePath);
            }

            // Open the new file
            await OpenFileAsync(result.NewFilePath);

            StatusText = $"Moved type '{viewModel.TypeName}' to '{viewModel.NewFileName}'";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Move type failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ExtractInterfaceAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the extract interface dialog
        var viewModel = new ExtractInterfaceDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ExtractInterfaceDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<ExtractInterfaceResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the original file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            // Add new file to project if created
            if (!string.IsNullOrEmpty(result.NewFilePath) && _projectService.CurrentProject != null)
            {
                await _projectService.AddFileToProjectAsync(result.NewFilePath);

                // Open the new file
                await OpenFileAsync(result.NewFilePath);
            }

            StatusText = $"Extracted interface '{result.InterfaceName}' with {result.MembersExtracted} member(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Extract interface failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GenerateConstructorAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the generate constructor dialog
        var viewModel = new GenerateConstructorDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.GenerateConstructorDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<GenerateConstructorResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Generated constructor with {result.ParameterCount} parameter(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Generate constructor failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task ImplementInterfaceAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the implement interface dialog
        var viewModel = new ImplementInterfaceDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ImplementInterfaceDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<ImplementInterfaceResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Implemented {result.MembersImplemented} interface member(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Implement interface failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task OverrideMethodAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the override method dialog
        var viewModel = new OverrideMethodDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.OverrideMethodDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<OverrideMethodResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Overrode {result.MethodsOverridden} method(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Override method failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task AddParameterAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the add parameter dialog
        var viewModel = new AddParameterDialogViewModel(_refactoringService, _fileService);
        await viewModel.InitializeAsync(activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.AddParameterDialog
        {
            DataContext = viewModel
        };

        var result = await dialog.ShowDialog<AddParameterResult?>(App.MainWindow);

        if (result?.Success == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Added parameter, updated {result.CallSitesUpdated} call site(s)";
        }
        else if (result != null && !result.Success)
        {
            StatusText = $"Add parameter failed: {result.ErrorMessage}";
        }
    }

    private async Task RemoveParameterAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the remove parameter dialog
        var viewModel = new RemoveParameterDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.RemoveParameterDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Parameters removed successfully";
        }
    }

    private async Task ReorderParametersAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the reorder parameters dialog
        var viewModel = new ReorderParametersDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ReorderParametersDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Parameters reordered successfully";
        }
    }

    private async Task RenameParameterAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the rename parameter dialog
        var viewModel = new RenameParameterDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.RenameParameterDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Parameter renamed successfully";
        }
    }

    private async Task ChangeParameterTypeAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the change parameter type dialog
        var viewModel = new ChangeParameterTypeDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ChangeParameterTypeDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Parameter type changed successfully";
        }
    }

    private async Task MakeParameterOptionalAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the make parameter optional dialog
        var viewModel = new MakeParameterOptionalDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.MakeParameterOptionalDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Parameter made optional successfully";
        }
    }

    private async Task MakeParameterRequiredAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the make parameter required dialog
        var viewModel = new MakeParameterRequiredDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.MakeParameterRequiredDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Parameter made required successfully";
        }
    }

    private async Task ConvertToNamedArgumentsAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the convert to named arguments dialog
        var viewModel = new ConvertToNamedArgumentsDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ConvertToNamedArgumentsDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Converted to named arguments successfully";
        }
    }

    private async Task ConvertToPositionalArgumentsAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        // Create and show the convert to positional arguments dialog
        var viewModel = new ConvertToPositionalArgumentsDialogViewModel(
            _refactoringService,
            activeDoc.FilePath,
            activeDoc.CaretLine,
            activeDoc.CaretColumn);

        var dialog = new Views.Dialogs.ConvertToPositionalArgumentsDialog(viewModel);
        var result = await dialog.ShowDialog<bool?>(App.MainWindow);

        if (result == true)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = "Converted to positional arguments successfully";
        }
    }

    private SelectionInfoDto? GetSelectionFromActiveDocument()
    {
        // This will be populated by the view via a callback
        // For now, we need to wire this up through the document view model
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc == null) return null;

        // Get selection info through the event aggregator or direct callback
        // The view will need to provide this information
        return activeDoc.GetSelectionInfo?.Invoke();
    }

    #endregion

    #region Surround With and Navigation

    [RelayCommand]
    private async Task SurroundWithAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        var selection = GetSelectionFromActiveDocument();
        if (selection == null || selection.StartLine == selection.EndLine && selection.StartColumn == selection.EndColumn)
        {
            StatusText = "Please select code to surround";
            return;
        }

        // Get available surround options
        var options = await _refactoringService.GetSurroundWithOptionsAsync(
            activeDoc.FilePath, selection.StartLine, selection.StartColumn,
            selection.EndLine, selection.EndColumn);

        // Show selection dialog
        var optionNames = options.Select(o => o.Name).ToList();
        var selectedIndex = await _dialogService.ShowListSelectionAsync("Surround With", "Select surround type:", optionNames);

        if (selectedIndex < 0 || selectedIndex >= options.Count) return;

        var selectedOption = options[selectedIndex];

        var result = await _refactoringService.SurroundWithAsync(
            activeDoc.FilePath, selection.StartLine, selection.StartColumn,
            selection.EndLine, selection.EndColumn, selectedOption.Type);

        if (result.Success)
        {
            // Reload the file
            if (_openDocuments.TryGetValue(activeDoc.FilePath, out var doc))
            {
                var content = await _fileService.ReadFileAsync(activeDoc.FilePath);
                doc.SetContent(content);
            }

            StatusText = $"Surrounded with {selectedOption.Name}";
        }
        else
        {
            StatusText = $"Surround with failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task PeekDefinitionAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        var result = await _refactoringService.PeekDefinitionAsync(
            activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn);

        if (result.Success && result.SourceCode != null)
        {
            // Show the peek definition in a dialog
            await _dialogService.ShowMessageAsync(
                $"Definition of '{result.SymbolName}'",
                $"File: {Path.GetFileName(result.FilePath)}\nLines {result.StartLine}-{result.EndLine}\n\n{result.SourceCode}",
                DialogButtons.Ok, DialogIcon.Information);

            StatusText = $"Peeked at {result.SymbolKind} '{result.SymbolName}'";
        }
        else
        {
            StatusText = result.ErrorMessage ?? "Definition not found";
        }
    }

    [RelayCommand]
    private async Task ShowCallHierarchyAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null || !_languageService.IsConnected) return;

        var word = GetWordAtCaret(activeDoc);
        await CallHierarchy.ShowHierarchyAsync(new CallHierarchyRequest
        {
            FilePath = activeDoc.FilePath,
            Line = activeDoc.CaretLine,
            Column = activeDoc.CaretColumn,
            MethodName = word
        });

        // Wire navigation
        CallHierarchy.NavigationRequested -= OnCallHierarchyNavigation;
        CallHierarchy.NavigationRequested += OnCallHierarchyNavigation;
    }

    private async void OnCallHierarchyNavigation(object? sender, CallHierarchyNavigationEventArgs e)
    {
        await OpenFileAsync(e.FilePath);
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.NavigateTo(e.Line, 1);
    }

    [RelayCommand]
    private async Task ShowTypeHierarchyAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null || !_languageService.IsConnected) return;

        var word = GetWordAtCaret(activeDoc);
        await TypeHierarchy.ShowHierarchyAsync(new TypeHierarchyRequest
        {
            FilePath = activeDoc.FilePath,
            Line = activeDoc.CaretLine,
            Column = activeDoc.CaretColumn,
            TypeName = word
        });

        // Wire navigation
        TypeHierarchy.NavigationRequested -= OnTypeHierarchyNavigation;
        TypeHierarchy.NavigationRequested += OnTypeHierarchyNavigation;
    }

    private async void OnTypeHierarchyNavigation(object? sender, TypeHierarchyNavigationEventArgs e)
    {
        await OpenFileAsync(e.FilePath);
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.NavigateTo(e.Line, 1);
    }

    private SelectionRangeInfo? _currentSelectionRange;

    [RelayCommand]
    private async Task ExpandSelectionAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null || !_languageService.IsConnected) return;

        if (_currentSelectionRange == null)
        {
            // Get fresh selection ranges from LSP
            var positions = new List<(int line, int column)> { (activeDoc.CaretLine, activeDoc.CaretColumn) };
            var ranges = await _languageService.GetSelectionRangesAsync(activeDoc.FilePath, positions);
            if (ranges.Count > 0)
            {
                _currentSelectionRange = ranges[0];
            }
        }
        else if (_currentSelectionRange.Parent != null)
        {
            _currentSelectionRange = _currentSelectionRange.Parent;
        }

        if (_currentSelectionRange != null)
        {
            activeDoc.ProvideSelectionRange(_currentSelectionRange);
        }
    }

    [RelayCommand]
    private void ShrinkSelection()
    {
        // To shrink, we'd need to track the history. Reset for now.
        _currentSelectionRange = null;
    }

    private async Task UpdateDocumentOutlineAsync(string filePath, string content)
    {
        try
        {
            if (_languageService.IsConnected &&
                (filePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                 filePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)))
            {
                await DocumentOutline.UpdateOutlineFromLspAsync(filePath, _languageService);
            }
            else
            {
                DocumentOutline.UpdateOutline(filePath, content);
            }
        }
        catch
        {
            // Fallback to text-based parsing
            DocumentOutline.UpdateOutline(filePath, content);
        }
    }

    /// <summary>
    /// Fetches code lenses from the LSP server and displays them in the editor.
    /// </summary>
    private async Task RefreshCodeLensesAsync(CodeEditorDocumentViewModel document)
    {
        try
        {
            if (!_languageService.IsConnected || document.FilePath == null) return;
            if (!document.FilePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) &&
                !document.FilePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)) return;

            var lenses = await _languageService.GetCodeLensAsync(document.FilePath);
            if (lenses.Count > 0)
            {
                document.ShowCodeLenses(lenses.Select(l => new CodeLensItemInfo
                {
                    Line = l.Line,
                    Title = l.Title,
                    CommandName = l.CommandName,
                    CommandArguments = l.CommandArguments
                }));
            }
            else
            {
                document.ClearCodeLenses();
            }
        }
        catch
        {
            // Silently fail — code lenses are non-critical
        }
    }

    /// <summary>
    /// Fetches semantic tokens from the LSP server and displays them in the editor.
    /// </summary>
    private async Task RefreshSemanticTokensAsync(CodeEditorDocumentViewModel document, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_languageService.IsConnected || document.FilePath == null) return;
            if (!document.FilePath.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) &&
                !document.FilePath.EndsWith(".bl", StringComparison.OrdinalIgnoreCase)) return;

            var result = await _languageService.GetSemanticTokensAsync(document.FilePath, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (result?.Data != null && result.Data.Length > 0)
            {
                document.UpdateSemanticTokens(result.Data);
            }
            else
            {
                document.ClearSemanticTokens();
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Silently fail — semantic tokens are non-critical
        }
    }

    /// <summary>
    /// Handles code lens command execution (show references, run, debug).
    /// </summary>
    private async Task HandleCodeLensCommandAsync(CodeEditorDocumentViewModel document, CodeLensClickedInfo info)
    {
        try
        {
            switch (info.CommandName)
            {
                case "basiclang.showReferences":
                    // Find references for the symbol on this line
                    if (document.FilePath != null)
                    {
                        var refs = await _languageService.FindReferencesAsync(
                            document.FilePath, info.Line, 1);
                        if (refs.Count > 0)
                        {
                            // Navigate to find results or show in output
                            StatusText = $"Found {refs.Count} reference(s)";
                            // Navigate to first reference if it's in a different location
                            var firstRef = refs.FirstOrDefault(r =>
                                r.Line != info.Line || !string.Equals(r.Uri, document.FilePath, StringComparison.OrdinalIgnoreCase));
                            if (firstRef != null)
                            {
                                var refPath = firstRef.Uri;
                                if (refPath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                                    refPath = Uri.UnescapeDataString(refPath.Substring(8));
                                await OpenFileAsync(refPath);
                                var refDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
                                refDoc?.NavigateTo(firstRef.Line);
                            }
                        }
                    }
                    break;

                case "basiclang.run":
                    await StartWithoutDebuggingAsync();
                    break;

                case "basiclang.debug":
                    await StartDebuggingAsync();
                    break;

                case "basiclang.goToDefinition":
                    if (document.FilePath != null)
                    {
                        var def = await _languageService.GetDefinitionAsync(
                            document.FilePath, info.Line, 1);
                        if (def != null)
                        {
                            var defPath = def.Uri;
                            if (defPath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                                defPath = Uri.UnescapeDataString(defPath.Substring(8));
                            await OpenFileAsync(defPath);
                            var defDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
                            defDoc?.NavigateTo(def.Line);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Code lens command failed: {ex.Message}";
        }
    }

    private async Task FormatDocumentAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        if (!_languageService.IsConnected)
        {
            StatusText = "Language server not connected";
            return;
        }

        var edits = await _languageService.FormatDocumentAsync(activeDoc.FilePath);
        if (edits.Count > 0)
        {
            // Apply the edits in reverse order to preserve offsets
            var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();
            var text = activeDoc.Text;
            var lines = text.Split('\n');

            foreach (var edit in sortedEdits)
            {
                // Convert to 0-based indices
                var startLine = edit.StartLine - 1;
                var endLine = edit.EndLine - 1;
                var startCol = edit.StartColumn - 1;
                var endCol = edit.EndColumn - 1;

                // Simple line-based replacement for now
                if (startLine >= 0 && startLine < lines.Length)
                {
                    if (startLine == endLine && startCol >= 0 && endCol <= lines[startLine].Length)
                    {
                        var line = lines[startLine];
                        lines[startLine] = line.Substring(0, startCol) + edit.NewText + line.Substring(endCol);
                    }
                }
            }

            activeDoc.ReplaceContent(string.Join("\n", lines));
            StatusText = $"Applied {edits.Count} formatting changes";
        }
        else
        {
            StatusText = "Document is already formatted";
        }
    }

    private async Task OnTypeFormattingAsync(CodeEditorDocumentViewModel document, int line, int column, string triggerCharacter)
    {
        if (document.FilePath == null || !_languageService.IsConnected) return;

        var edits = await _languageService.OnTypeFormattingAsync(document.FilePath, line, column, triggerCharacter);
        if (edits.Count > 0)
        {
            // Apply the edits in reverse order to preserve offsets
            var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();
            var text = document.Text;
            var lines = text.Split('\n');

            foreach (var edit in sortedEdits)
            {
                // Convert to 0-based indices
                var startLine = edit.StartLine - 1;
                var endLine = edit.EndLine - 1;
                var startCol = edit.StartColumn - 1;
                var endCol = edit.EndColumn - 1;

                // Simple line-based replacement for now
                if (startLine >= 0 && startLine < lines.Length)
                {
                    if (startLine == endLine && startCol >= 0 && endCol <= lines[startLine].Length)
                    {
                        var lineText = lines[startLine];
                        lines[startLine] = lineText.Substring(0, startCol) + edit.NewText + lineText.Substring(endCol);
                    }
                }
            }

            document.ReplaceContent(string.Join("\n", lines));
        }
    }

    private async Task ShowCodeActionsAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        if (!_languageService.IsConnected)
        {
            StatusText = "Language server not connected";
            return;
        }

        // Get selection range or use caret position
        var selectionInfo = activeDoc.GetSelectionInfo?.Invoke();
        int startLine, startCol, endLine, endCol;

        if (selectionInfo != null && selectionInfo.SelectedText?.Length > 0)
        {
            startLine = selectionInfo.StartLine;
            startCol = selectionInfo.StartColumn;
            endLine = selectionInfo.EndLine;
            endCol = selectionInfo.EndColumn;
        }
        else
        {
            startLine = endLine = activeDoc.CaretLine;
            startCol = endCol = activeDoc.CaretColumn;
        }

        var actions = await _languageService.GetCodeActionsAsync(
            activeDoc.FilePath, startLine, startCol, endLine, endCol);

        if (actions.Count == 0)
        {
            StatusText = "No code actions available";
            return;
        }

        // Show actions in a quick menu
        var actionTitles = actions.Select(a => a.Title).ToList();
        var result = await _dialogService.ShowListSelectionAsync(
            "Code Actions",
            "Select an action to apply:",
            actionTitles);

        if (result >= 0 && result < actions.Count)
        {
            var selectedAction = actions[result];
            if (selectedAction.Edit != null)
            {
                await ApplyWorkspaceEditAsync(selectedAction.Edit);
                StatusText = $"Applied: {selectedAction.Title}";
            }
        }
    }

    private async Task ApplyWorkspaceEditAsync(WorkspaceEditInfo edit)
    {
        foreach (var (filePath, fileEdits) in edit.Changes)
        {
            // Check if the file is open
            if (_openDocuments.TryGetValue(filePath, out var doc))
            {
                // Apply edits in reverse order to preserve offsets
                var sortedEdits = fileEdits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();
                var text = doc.Text;
                var lines = text.Split('\n');

                foreach (var e in sortedEdits)
                {
                    var startLine = e.StartLine - 1;
                    var endLine = e.EndLine - 1;
                    var startCol = e.StartColumn - 1;
                    var endCol = e.EndColumn - 1;

                    if (startLine >= 0 && startLine < lines.Length)
                    {
                        if (startLine == endLine && startCol >= 0 && endCol <= lines[startLine].Length)
                        {
                            var line = lines[startLine];
                            lines[startLine] = line.Substring(0, startCol) + e.NewText + line.Substring(endCol);
                        }
                    }
                }

                doc.SetContent(string.Join("\n", lines));
            }
            else
            {
                // File not open - apply edits directly to file
                var content = await _fileService.ReadFileAsync(filePath);
                var lines = content.Split('\n');

                var sortedEdits = fileEdits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();
                foreach (var e in sortedEdits)
                {
                    var startLine = e.StartLine - 1;
                    var endLine = e.EndLine - 1;
                    var startCol = e.StartColumn - 1;
                    var endCol = e.EndColumn - 1;

                    if (startLine >= 0 && startLine < lines.Length)
                    {
                        if (startLine == endLine && startCol >= 0 && endCol <= lines[startLine].Length)
                        {
                            var line = lines[startLine];
                            lines[startLine] = line.Substring(0, startCol) + e.NewText + line.Substring(endCol);
                        }
                    }
                }

                await _fileService.WriteFileAsync(filePath, string.Join("\n", lines));
            }
        }
    }

    /// <summary>
    /// Gets fallback completions when LSP is not connected
    /// </summary>
    private static IReadOnlyList<CompletionItem> GetFallbackCompletions()
    {
        var completions = new List<CompletionItem>();

        // BasicLang keywords
        var keywords = new[]
        {
            "Function", "Sub", "End", "If", "Then", "Else", "ElseIf", "EndIf",
            "For", "To", "Step", "Next", "While", "Wend", "Do", "Loop", "Until",
            "Select", "Case", "EndSelect", "Dim", "As", "Integer", "String", "Double",
            "Boolean", "Object", "True", "False", "And", "Or", "Not", "Return",
            "Class", "EndClass", "Public", "Private", "Property", "Get", "Set",
            "New", "Me", "MyBase", "Imports", "Module", "EndModule", "Const",
            "Static", "ByVal", "ByRef", "Optional", "ParamArray", "Inherits",
            "Implements", "Interface", "EndInterface", "Enum", "EndEnum",
            "Try", "Catch", "Finally", "EndTry", "Throw", "Exit", "Continue"
        };

        foreach (var keyword in keywords)
        {
            completions.Add(new CompletionItem
            {
                Label = keyword,
                Kind = CompletionItemKind.Keyword,
                Detail = "Keyword"
            });
        }

        // Built-in functions
        var builtins = new[]
        {
            ("Print", "Output text to console"),
            ("Input", "Read input from user"),
            ("Len", "Get string length"),
            ("Mid", "Get substring"),
            ("Left", "Get left portion of string"),
            ("Right", "Get right portion of string"),
            ("InStr", "Find substring position"),
            ("Val", "Convert string to number"),
            ("Str", "Convert number to string"),
            ("Int", "Convert to integer"),
            ("Abs", "Absolute value"),
            ("Sin", "Sine function"),
            ("Cos", "Cosine function"),
            ("Tan", "Tangent function"),
            ("Sqrt", "Square root"),
            ("Log", "Natural logarithm"),
            ("Exp", "Exponential function"),
            ("Rnd", "Random number"),
            ("Timer", "System timer"),
            ("Date", "Current date"),
            ("Time", "Current time"),
            ("UCase", "Convert to uppercase"),
            ("LCase", "Convert to lowercase"),
            ("Trim", "Remove whitespace"),
            ("Chr", "Character from ASCII"),
            ("Asc", "ASCII from character")
        };

        foreach (var (name, description) in builtins)
        {
            completions.Add(new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Function,
                Detail = description
            });
        }

        return completions;
    }

    /// <summary>
    /// Filters completions based on context. After "As " keyword, only type-related
    /// completion items are shown (Class, Struct, Interface, Enum, Module, TypeParameter).
    /// </summary>
    private static IReadOnlyList<CompletionItem> FilterCompletionsForContext(
        CodeEditorDocumentViewModel document, int line, int column,
        IReadOnlyList<CompletionItem> completions)
    {
        try
        {
            var text = document.Text;
            if (string.IsNullOrEmpty(text)) return completions;

            // Get the text of the current line up to the cursor position
            var lines = text.Split('\n');
            var lineIndex = line - 1; // Convert to 0-based
            if (lineIndex < 0 || lineIndex >= lines.Length) return completions;

            var lineText = lines[lineIndex];
            // Column is 1-based; get text before the current word being typed
            var colIndex = Math.Min(column - 1, lineText.Length);
            var textBeforeCursor = lineText.Substring(0, colIndex);

            // Walk back past the current word prefix (letters/digits/underscore)
            var prefixEnd = textBeforeCursor.Length;
            while (prefixEnd > 0 && (char.IsLetterOrDigit(textBeforeCursor[prefixEnd - 1]) || textBeforeCursor[prefixEnd - 1] == '_'))
            {
                prefixEnd--;
            }
            var textBeforeWord = textBeforeCursor.Substring(0, prefixEnd).TrimEnd();

            // Check if the text before the word ends with "As" (case-insensitive)
            if (textBeforeWord.EndsWith("As", StringComparison.OrdinalIgnoreCase))
            {
                // Verify "As" is a standalone keyword (preceded by space, start of trimmed line, or paren)
                var asIndex = textBeforeWord.Length - 2;
                if (asIndex == 0 || !char.IsLetterOrDigit(textBeforeWord[asIndex - 1]))
                {
                    // Filter to only type-related completion kinds
                    var typeKinds = new HashSet<CompletionItemKind>
                    {
                        CompletionItemKind.Class,
                        CompletionItemKind.Struct,
                        CompletionItemKind.Interface,
                        CompletionItemKind.Enum,
                        CompletionItemKind.Module,
                        CompletionItemKind.TypeParameter
                    };

                    var filtered = completions.Where(c => typeKinds.Contains(c.Kind)).ToList();

                    // If LSP didn't return any type items, provide built-in type names as fallback
                    if (filtered.Count == 0)
                    {
                        var builtinTypes = new[]
                        {
                            "Integer", "String", "Double", "Boolean", "Single", "Long",
                            "Short", "Byte", "Char", "Decimal", "Date", "Object",
                            "SByte", "UShort", "UInteger", "ULong"
                        };
                        foreach (var typeName in builtinTypes)
                        {
                            filtered.Add(new CompletionItem
                            {
                                Label = typeName,
                                Kind = CompletionItemKind.Class,
                                Detail = "Built-in type"
                            });
                        }
                    }

                    return filtered;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FilterCompletions] Error: {ex.Message}");
        }

        return completions;
    }

    #endregion

    #region Git Blame Annotations

    /// <summary>
    /// Updates the inline blame annotation for the current caret line.
    /// Fetches blame data from cache or git, then updates both the status bar
    /// text and the inline annotation on the document.
    /// </summary>
    private async Task UpdateBlameForCurrentLineAsync(CodeEditorDocumentViewModel document)
    {
        try
        {
            if (document.FilePath == null || !_gitService.IsGitRepository)
            {
                BlameAnnotationText = "";
                document.ClearBlameAnnotation();
                return;
            }

            var filePath = document.FilePath;
            var lineNumber = document.CaretLine;

            // Try to get blame data from cache
            if (!_blameCache.TryGetValue(filePath, out var blameLines))
            {
                // Fetch blame data asynchronously
                blameLines = await _gitService.GetBlameAsync(filePath);
                if (blameLines.Count > 0)
                {
                    _blameCache[filePath] = blameLines;
                }
            }

            // Find the blame entry for the current line
            var blameLine = blameLines.FirstOrDefault(b => b.LineNumber == lineNumber);
            if (blameLine != null && !string.IsNullOrEmpty(blameLine.Author))
            {
                // Update status bar
                BlameAnnotationText = blameLine.AnnotationText;

                // Update inline annotation on the current line
                document.UpdateBlameAnnotation(lineNumber, blameLine.AnnotationText);
            }
            else
            {
                BlameAnnotationText = "";
                document.ClearBlameAnnotation();
            }
        }
        catch
        {
            // Silently handle blame errors (file not tracked, etc.)
            BlameAnnotationText = "";
        }
    }

    #endregion
}

public class SelectionInfoDto
{
    public int StartLine { get; set; }
    public int StartColumn { get; set; }
    public int EndLine { get; set; }
    public int EndColumn { get; set; }
    public string SelectedText { get; set; } = "";
}

public class DataTipResultEventArgs : EventArgs
{
    public string Expression { get; }
    public string Value { get; }
    public string? Type { get; }
    public double ScreenX { get; }
    public double ScreenY { get; }
    public bool IsError { get; }

    public DataTipResultEventArgs(string expression, string value, string? type, double screenX, double screenY, bool isError)
    {
        Expression = expression;
        Value = value;
        Type = type;
        ScreenX = screenX;
        ScreenY = screenY;
        IsError = isError;
    }
}

public class NotificationEventArgs : EventArgs
{
    public string Message { get; }
    public string Severity { get; }

    public NotificationEventArgs(string message, string severity)
    {
        Message = message;
        Severity = severity;
    }
}
