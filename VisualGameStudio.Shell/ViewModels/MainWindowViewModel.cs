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
    private readonly IEventAggregator _eventAggregator;
    private readonly DockFactory _dockFactory;

    [ObservableProperty]
    private IRootDock? _layout;

    [ObservableProperty]
    private string _title = "Visual Game Studio";

    [ObservableProperty]
    private string _statusText = "Ready";

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

    private readonly Dictionary<string, CodeEditorDocumentViewModel> _openDocuments = new();

    public MainWindowViewModel(
        IProjectService projectService,
        IBuildService buildService,
        IDebugService debugService,
        ILanguageService languageService,
        IDialogService dialogService,
        IFileService fileService,
        IBookmarkService bookmarkService,
        IRefactoringService refactoringService,
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
        BookmarksViewModel bookmarks)
    {
        _projectService = projectService;
        _buildService = buildService;
        _debugService = debugService;
        _languageService = languageService;
        _dialogService = dialogService;
        _fileService = fileService;
        _bookmarkService = bookmarkService;
        _refactoringService = refactoringService;
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

        // Handle breakpoint condition editing
        Breakpoints.EditConditionRequested += OnEditBreakpointCondition;
        Breakpoints.EditFunctionConditionRequested += OnEditFunctionBreakpointCondition;

        // Handle bookmark navigation
        Bookmarks.NavigationRequested += OnBookmarkNavigationRequested;

        // Wire up Find in Files navigation
        FindInFiles.SetNavigationCallback(OpenFileAtLine);

        // Subscribe to language service diagnostics for error highlighting
        _languageService.DiagnosticsReceived += OnDiagnosticsReceived;

        // Start language service
        _ = _languageService.StartAsync();
    }

    private void OnDiagnosticsReceived(object? sender, DiagnosticsEventArgs e)
    {
        // Forward diagnostics to the error list
        ErrorList.UpdateDiagnostics(e.Diagnostics);

        // Forward to the specific document for error highlighting
        var filePath = e.Uri.Replace("file:///", "").Replace("/", "\\");
        if (_openDocuments.TryGetValue(filePath, out var doc))
        {
            doc.UpdateDiagnostics(e.Diagnostics);
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

    private void OnProjectOpened(object? sender, ProjectEventArgs e)
    {
        Title = $"{e.Project.Name} - Visual Game Studio";
        StatusText = $"Project loaded: {e.Project.Name}";
    }

    private void OnDocumentClosed(object? sender, string filePath)
    {
        _openDocuments.Remove(filePath);
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
        }
        else
        {
            StatusText = $"Build failed - {result.ErrorCount} error(s), {result.WarningCount} warning(s)";
        }

        // Update error list
        ErrorList.UpdateDiagnostics(result.Diagnostics);
    }

    private async void OnFileOpenRequested(object? sender, string filePath)
    {
        await OpenFileAsync(filePath);
    }

    private async void OnDiagnosticDoubleClicked(object? sender, DiagnosticItem diagnostic)
    {
        await OpenFileAndNavigateAsync(diagnostic.FilePath, diagnostic.Line, diagnostic.Column);
    }

    private async void OnEditBreakpointCondition(object? sender, BreakpointItem breakpoint)
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

    private async void OnEditFunctionBreakpointCondition(object? sender, FunctionBreakpointItem breakpoint)
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

    private async void OnAddToWatchRequested(object? sender, string expression)
    {
        await Watch.AddExpressionCommand.ExecuteAsync(expression);
        _dockFactory.ActivateTool("Watch");
    }

    public event EventHandler<DataTipResultEventArgs>? DataTipResult;

    private async void OnDataTipEvaluationRequested(object? sender, DataTipEvaluationRequestEventArgs e)
    {
        // Only evaluate if we're debugging and paused
        if (!_debugService.IsDebugging || _debugService.State != Core.Abstractions.Services.DebugState.Paused)
        {
            return;
        }

        try
        {
            var result = await _debugService.EvaluateAsync(e.Expression);
            if (result != null)
            {
                DataTipResult?.Invoke(this, new DataTipResultEventArgs(
                    e.Expression,
                    result.Result,
                    result.Type,
                    e.ScreenX,
                    e.ScreenY,
                    false
                ));
            }
        }
        catch (Exception ex)
        {
            DataTipResult?.Invoke(this, new DataTipResultEventArgs(
                e.Expression,
                ex.Message,
                null,
                e.ScreenX,
                e.ScreenY,
                true
            ));
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
        var folderPath = await _dialogService.ShowFolderDialogAsync(new FolderDialogOptions
        {
            Title = "Select Project Location"
        });

        if (string.IsNullOrEmpty(folderPath)) return;

        var projectName = await _dialogService.ShowInputDialogAsync(
            "New Project",
            "Enter project name:",
            "MyProject");

        if (string.IsNullOrEmpty(projectName)) return;

        try
        {
            SetBusy(true, "Creating project...");
            await _projectService.CreateProjectAsync(projectName, folderPath, ProjectTemplate.ConsoleApplication);
            StatusText = $"Project created: {projectName}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to create project: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
        finally
        {
            SetBusy(false);
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

            var document = new CodeEditorDocumentViewModel(_fileService, _eventAggregator, _bookmarkService)
            {
                FilePath = filePath,
                Text = content
            };

            document.CaretPositionChanged += (s, e) =>
            {
                CaretLine = document.CaretLine;
                CaretColumn = document.CaretColumn;
            };

            document.AddToWatchRequested += OnAddToWatchRequested;
            document.DataTipEvaluationRequested += OnDataTipEvaluationRequested;
            document.GoToDefinitionRequested += async (s, e) => await GoToDefinitionAsync();
            document.FindAllReferencesRequested += async (s, e) => await FindReferencesAsync();
            document.RenameSymbolRequested += async (s, e) => await RenameSymbolAsync();
            document.ExtractMethodRequested += async (s, e) => await ExtractMethodAsync();
            document.InlineMethodRequested += async (s, e) => await InlineMethodAsync();
            document.IntroduceVariableRequested += async (s, e) => await IntroduceVariableAsync();
            document.ExtractConstantRequested += async (s, e) => await ExtractConstantAsync();
            document.InlineConstantRequested += async (s, e) => await InlineConstantAsync();
            document.InlineVariableRequested += async (s, e) => await InlineVariableAsync();
            document.ChangeSignatureRequested += async (s, e) => await ChangeSignatureAsync();
            document.EncapsulateFieldRequested += async (s, e) => await EncapsulateFieldAsync();
            document.InlineFieldRequested += async (s, e) => await InlineFieldAsync();
            document.MoveTypeToFileRequested += async (s, e) => await MoveTypeToFileAsync();
            document.ExtractInterfaceRequested += async (s, e) => await ExtractInterfaceAsync();
            document.GenerateConstructorRequested += async (s, e) => await GenerateConstructorAsync();
            document.ImplementInterfaceRequested += async (s, e) => await ImplementInterfaceAsync();
            document.OverrideMethodRequested += async (s, e) => await OverrideMethodAsync();
            document.AddParameterRequested += async (s, e) => await AddParameterAsync();
            document.RemoveParameterRequested += async (s, e) => await RemoveParameterAsync();
            document.ReorderParametersRequested += async (s, e) => await ReorderParametersAsync();
            document.RenameParameterRequested += async (s, e) => await RenameParameterAsync();
            document.ChangeParameterTypeRequested += async (s, e) => await ChangeParameterTypeAsync();
            document.MakeParameterOptionalRequested += async (s, e) => await MakeParameterOptionalAsync();
            document.MakeParameterRequiredRequested += async (s, e) => await MakeParameterRequiredAsync();
            document.ConvertToNamedArgumentsRequested += async (s, e) => await ConvertToNamedArgumentsAsync();
            document.ConvertToPositionalArgumentsRequested += async (s, e) => await ConvertToPositionalArgumentsAsync();
            document.SafeDeleteRequested += async (s, e) => await SafeDeleteAsync();
            document.PullMembersUpRequested += async (s, e) => await PullMembersUpAsync();
            document.PushMembersDownRequested += async (s, e) => await PushMembersDownAsync();
            document.UseBaseTypeRequested += async (s, e) => await UseBaseTypeAsync();
            document.ConvertToInterfaceRequested += async (s, e) => await ConvertToInterfaceAsync();
            document.InvertIfRequested += async (s, e) => await InvertIfAsync();
            document.ConvertToSelectCaseRequested += async (s, e) => await ConvertToSelectCaseAsync();
            document.SplitDeclarationRequested += async (s, e) => await SplitDeclarationAsync();
            document.IntroduceFieldRequested += async (s, e) => await IntroduceFieldAsync();
            document.SurroundWithRequested += async (s, e) => await SurroundWithAsync();
            document.PeekDefinitionRequested += async (s, e) => await PeekDefinitionAsync();

            // Wire up code completion requests
            document.CompletionRequested += async (s, e) =>
            {
                if (_languageService.IsConnected && e != null)
                {
                    var completions = await _languageService.GetCompletionsAsync(
                        document.FilePath ?? "",
                        e.Line,
                        e.Column);

                    if (completions?.Any() == true)
                    {
                        document.ProvideCompletions(completions);
                    }
                }
            };

            _openDocuments[filePath] = document;
            _dockFactory.AddDocument(document);
        }
        catch (Exception ex)
        {
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
            await activeDoc.SaveAsync();
        }
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        foreach (var doc in _openDocuments.Values.Where(d => d.IsDirty))
        {
            await doc.SaveAsync();
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
    private void CancelBuild()
    {
        if (_buildService.IsBuilding)
        {
            _buildService.CancelBuildAsync();
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
        IsDebugging = e.NewState == DebugState.Running || e.NewState == DebugState.Paused;
        IsPaused = e.NewState == DebugState.Paused;

        DebugStatusText = e.NewState switch
        {
            DebugState.Running => "Running",
            DebugState.Paused => "Paused",
            DebugState.Stopped => "Stopped",
            _ => ""
        };

        if (e.NewState == DebugState.Stopped)
        {
            StatusText = "Debug session ended";
        }
    }

    private async void OnDebugStopped(object? sender, StoppedEventArgs e)
    {
        // Marshal to UI thread
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            StatusText = e.Reason switch
            {
                StopReason.Breakpoint => "Breakpoint hit",
                StopReason.Step => "Step complete",
                StopReason.Exception => $"Exception: {e.Text}",
                StopReason.Pause => "Paused",
                _ => "Stopped"
            };

            // Navigate to the stopped location
            var frames = await _debugService.GetStackTraceAsync();
            if (frames.Any() && frames[0].FilePath != null)
            {
                await OpenFileAsync(frames[0].FilePath);
                var doc = _openDocuments.GetValueOrDefault(frames[0].FilePath);
                // Could set caret position here
            }
        });
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

        // Find main source file for the debug adapter (it interprets source, not executable)
        var mainFile = _projectService.CurrentProject.Items
            .FirstOrDefault(i => i.ItemType == ProjectItemType.Compile);

        if (mainFile == null)
        {
            await _dialogService.ShowMessageAsync("Debug", "No source file found in project.",
                DialogButtons.Ok, DialogIcon.Error);
            return;
        }

        var sourceFilePath = Path.Combine(_projectService.CurrentProject.ProjectDirectory, mainFile.Include);
        if (!File.Exists(sourceFilePath))
        {
            await _dialogService.ShowMessageAsync("Debug", $"Source file not found: {sourceFilePath}",
                DialogButtons.Ok, DialogIcon.Error);
            return;
        }

        StatusText = "Starting debugger...";
        OutputPanel.SelectedCategory = OutputCategory.General;  // Switch to General output to see program output
        OutputPanel.AppendOutput($"\n========== Debugging: {Path.GetFileName(sourceFilePath)} ==========\n");

        var config = new DebugConfiguration
        {
            Program = sourceFilePath,  // Pass source file, not executable - debug adapter interprets source
            WorkingDirectory = _projectService.CurrentProject.ProjectDirectory,
            StopOnEntry = false
        };

        // Collect breakpoints to pass to debug service
        var breakpointsDict = Breakpoints.GetAllBreakpoints();

        var success = await _debugService.StartDebuggingAsync(config, breakpointsDict);
        if (!success)
        {
            OutputPanel.AppendOutput("Failed to start debugger.\n");
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
    private async Task StopDebuggingAsync()
    {
        if (!IsDebugging) return;
        await _debugService.StopDebuggingAsync();
    }

    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (!IsPaused) return;
        await _debugService.ContinueAsync();
    }

    [RelayCommand]
    private async Task StepOverAsync()
    {
        if (!IsPaused) return;
        await _debugService.StepOverAsync();
    }

    [RelayCommand]
    private async Task StepIntoAsync()
    {
        if (!IsPaused) return;
        await _debugService.StepIntoAsync();
    }

    [RelayCommand]
    private async Task StepOutAsync()
    {
        if (!IsPaused) return;
        await _debugService.StepOutAsync();
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (!IsDebugging || IsPaused) return;
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
    private async Task ShowExceptionSettingsAsync()
    {
        var result = await _dialogService.ShowExceptionSettingsDialogAsync(_currentExceptionSettings);
        if (result != null)
        {
            _currentExceptionSettings = result;

            // Apply exception breakpoints to debug service
            var filters = new List<string>();
            foreach (var setting in result.Where(s => s.BreakWhenThrown))
            {
                if (setting.ExceptionType == "All Exceptions")
                {
                    filters.Add("all");
                }
                else if (setting.BreakWhenUserUnhandled)
                {
                    filters.Add("uncaught");
                }
            }

            if (IsDebugging)
            {
                await _debugService.SetExceptionBreakpointsAsync(filters);
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

        var result = await _dialogService.ShowGoToSymbolDialogAsync(activeDoc.Text, activeDoc.FilePath);
        if (result != null)
        {
            activeDoc.NavigateTo(result.Line, result.Column);
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

        // Create and show the rename dialog
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
                catch { /* Ignore errors for files that may not exist */ }
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
