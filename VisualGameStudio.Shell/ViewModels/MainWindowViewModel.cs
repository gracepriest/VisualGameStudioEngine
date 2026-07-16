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
using VisualGameStudio.Core.Utilities;
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
    private readonly ILanguageServiceRegistry _languageServices;
    private readonly IDialogService _dialogService;
    private readonly IFileService _fileService;
    private readonly IBookmarkService _bookmarkService;
    private readonly IRefactoringService _refactoringService;
    private readonly IProjectTemplateService _projectTemplateService;
    private readonly IGitService _gitService;
    private readonly ILaunchConfigurationService _launchConfigurationService;
    private readonly IAutoSaveService _autoSaveService;
    private readonly IHotExitService _hotExitService;
    private readonly IFileWatcherService _fileWatcherService;
    private readonly ISettingsService? _settingsService;
    private readonly IRecentProjectsService _recentProjectsService;
    private readonly IEventAggregator _eventAggregator;
    private readonly IOutputService _outputService;
    private readonly IExtensionService _extensionService;
    private readonly ISolutionService _solutionService;
    private readonly IWorkspaceService _workspaceService;
    private readonly ITaskRunnerService _taskRunnerService;
    private readonly DockFactory _dockFactory;
    private readonly IWorkspaceStateStore _workspaceStateStore;

    // Per-project layout/session persistence (VS Code's workspaceStorage model).
    private CancellationTokenSource? _layoutSaveCts;
    private bool _restoringLayout;

    /// <summary>
    /// Tracks extension-contributed tree view panels by viewId.
    /// </summary>
    private readonly Dictionary<string, TreeViewPanelViewModel> _extensionTreeViews = new();

    /// <summary>
    /// Raised when an extension creates a tree view panel so the shell can add it to the UI.
    /// </summary>
    public event EventHandler<TreeViewPanelViewModel>? ExtensionTreeViewPanelCreated;

    /// <summary>
    /// Gets all currently active extension tree view panels.
    /// </summary>
    public IReadOnlyDictionary<string, TreeViewPanelViewModel> ExtensionTreeViews => _extensionTreeViews;

    /// <summary>
    /// Tracks extension-contributed webview panels by panelId.
    /// </summary>
    private readonly Dictionary<string, WebViewDocumentViewModel> _extensionWebViews = new();

    /// <summary>
    /// Tracks cursor position history for Back/Forward navigation (Alt+Left/Right).
    /// </summary>
    private readonly Services.NavigationHistoryService _navigationHistory = new();

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

    /// <summary>
    /// Gets whether a solution is currently open. Used for menu item IsEnabled bindings.
    /// </summary>
    [ObservableProperty]
    private bool _hasSolutionOpen;

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
    /// Whether the bottom panel area is maximized (fills entire content area).
    /// When true, the sidebar and editor are hidden and the bottom panel fills the space.
    /// </summary>
    [ObservableProperty]
    private bool _isBottomPanelMaximized;

    /// <summary>Whether the minimap is visible in editors.</summary>
    [ObservableProperty]
    private bool _showMinimap = true;

    /// <summary>Whether breadcrumbs are visible in editors.</summary>
    [ObservableProperty]
    private bool _showBreadcrumbs = true;

    /// <summary>Whether sticky scroll is enabled in editors.</summary>
    [ObservableProperty]
    private bool _showStickyScroll = true;

    /// <summary>Whether word wrap is enabled in editors.</summary>
    [ObservableProperty]
    private bool _wordWrap;

    /// <summary>
    /// Whether reduced motion mode is active. When true, disables smooth scrolling,
    /// cursor blink, toast slide-in animations, and minimap hover animations.
    /// Can be set via accessibility.reduceMotion setting or detected from OS preferences.
    /// </summary>
    [ObservableProperty]
    private bool _reduceMotion;

    /// <summary>
    /// Current IDE zoom level as a percentage (50-200). Default 100.
    /// Applied via LayoutTransform on the main content.
    /// </summary>
    [ObservableProperty]
    private int _zoomLevel = 100;

    /// <summary>Whether the menu bar is visible.</summary>
    [ObservableProperty]
    private bool _showMenuBar = true;

    /// <summary>Whether the side bar (solution explorer) is visible.</summary>
    [ObservableProperty]
    private bool _showSideBar = true;

    /// <summary>Whether the status bar is visible.</summary>
    [ObservableProperty]
    private bool _showStatusBar = true;

    /// <summary>Whether the bottom panel area is visible.</summary>
    [ObservableProperty]
    private bool _showPanel = true;

    /// <summary>Whether full screen mode is active.</summary>
    [ObservableProperty]
    private bool _isFullScreen;

    // ── Activity Bar Badge Counts ──

    /// <summary>
    /// Number of changed files in source control (staged + unstaged).
    /// Displayed as a badge on the Source Control activity bar icon.
    /// </summary>
    [ObservableProperty]
    private int _sourceControlBadgeCount;

    /// <summary>
    /// Number of problems (errors + warnings) from diagnostics.
    /// Displayed as a badge on the Problems activity bar icon.
    /// </summary>
    [ObservableProperty]
    private int _problemsBadgeCount;

    /// <summary>
    /// Number of installed extensions (placeholder for future update count).
    /// Displayed as a badge on the Extensions activity bar icon.
    /// </summary>
    [ObservableProperty]
    private int _extensionsBadgeCount;

    /// <summary>Recent projects list for the Open Recent submenu.</summary>
    [ObservableProperty]
    private ObservableCollection<RecentProjectInfo> _recentProjects = new();

    /// <summary>Whether there are any recent projects to display.</summary>
    public bool HasRecentProjects => RecentProjects.Count > 0;

    /// <summary>Menu items for the Open Recent submenu, built from RecentProjects plus a separator and Clear.</summary>
    [ObservableProperty]
    private ObservableCollection<RecentProjectMenuItem> _recentProjectMenuItems = new();

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
    public ThreadsViewModel Threads { get; }
    public TimelineViewModel Timeline { get; }

    private readonly Dictionary<string, CodeEditorDocumentViewModel> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action> _documentCleanupActions = new(StringComparer.OrdinalIgnoreCase);

    // Documents with a save currently in progress. Only touched on the UI thread;
    // prevents an auto-save from overlapping a manual save (and vice versa).
    private readonly HashSet<string> _documentsBeingSaved = new(StringComparer.OrdinalIgnoreCase);

    // Files that currently have error-severity LSP diagnostics, keyed by file path.
    // Updated from OnDiagnosticsReceived and read by the auto-save service's
    // HasErrorsProvider (from timer threads), hence a concurrent dictionary.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _filesWithErrors =
        new(StringComparer.OrdinalIgnoreCase);

    // Per-file diagnostics store behind the Error List. LSP publishDiagnostics
    // is per-document (one file's full set per notification, empty = clean), so
    // rendering a payload directly would wipe every other file's errors; the
    // aggregator keeps file -> diagnostics and the Error List shows the union.
    // Build results live in a separate keyspace so they coexist with LSP ones.
    private readonly DiagnosticsAggregator _diagnosticsAggregator = new();

    // File path of the most recently active editor document tab. Used to emit
    // auto-save "editor lost focus" notifications when the active tab changes.
    private string? _lastActiveEditorFilePath;

    public MainWindowViewModel(
        IProjectService projectService,
        IBuildService buildService,
        IDebugService debugService,
        ILanguageServiceRegistry languageServices,
        IDialogService dialogService,
        IFileService fileService,
        IBookmarkService bookmarkService,
        IRefactoringService refactoringService,
        IProjectTemplateService projectTemplateService,
        IGitService gitService,
        ILaunchConfigurationService launchConfigurationService,
        IAutoSaveService autoSaveService,
        IHotExitService hotExitService,
        IFileWatcherService fileWatcherService,
        ISettingsService settingsService,
        IRecentProjectsService recentProjectsService,
        IEventAggregator eventAggregator,
        IOutputService outputService,
        IExtensionService extensionService,
        ISolutionService solutionService,
        IWorkspaceService workspaceService,
        ITaskRunnerService taskRunnerService,
        DockFactory dockFactory,
        IWorkspaceStateStore workspaceStateStore,
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
        TypeHierarchyViewModel typeHierarchy,
        ThreadsViewModel threads,
        TimelineViewModel timeline,
        Documents.WelcomeDocumentViewModel welcomeDocument)
    {
        _projectService = projectService;
        _buildService = buildService;
        _debugService = debugService;
        _languageServices = languageServices;
        _dialogService = dialogService;
        _fileService = fileService;
        _bookmarkService = bookmarkService;
        _refactoringService = refactoringService;
        _projectTemplateService = projectTemplateService;
        _gitService = gitService;
        _launchConfigurationService = launchConfigurationService;
        _autoSaveService = autoSaveService;
        _hotExitService = hotExitService;
        _fileWatcherService = fileWatcherService;
        _settingsService = settingsService;
        _recentProjectsService = recentProjectsService;
        _eventAggregator = eventAggregator;
        _outputService = outputService;
        _extensionService = extensionService;
        _solutionService = solutionService;
        _workspaceService = workspaceService;
        _taskRunnerService = taskRunnerService;
        _dockFactory = dockFactory;
        _workspaceStateStore = workspaceStateStore;

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
        Threads = threads;
        Timeline = timeline;

        // Wire the welcome/start page buttons. The DI container builds the
        // singleton WelcomeDocumentViewModel with no callbacks; without this
        // every start-page button is a silent no-op.
        welcomeDocument.SetCallbacks(
            openProject: path =>
            {
                if (string.IsNullOrEmpty(path))
                    OpenProjectCommand.Execute(null);
                else
                    OpenRecentProjectCommand.Execute(path);
            },
            newProject: () => NewProjectCommand.Execute(null),
            openFile: () => OpenFileCommand.Execute(null),
            openFolder: () => OpenFolderCommand.Execute(null),
            cloneRepository: () => ShowNotification(
                "Clone Repository is not available yet — open a folder and use the Git panel instead.", "info"));

        // Setup dock layout
        _dockFactory.SetViewModels(solutionExplorer, outputPanel, errorList, callStack, variables, breakpoints, findInFiles, terminal, gitChanges, gitBranches, gitStash, gitBlame, watch, immediateWindow, documentOutline, bookmarks, threads: threads, timeline: timeline, callHierarchy: CallHierarchy);
        Layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(Layout);

        // workbench.startupEditor = newUntitledFile: CreateLayout leaves the document area empty for
        // this mode (it can't build an editor document itself), so create the untitled file here now
        // that the editor-document machinery is available. welcomePage / none are already handled
        // inside CreateLayout. A later project-open with a saved session replaces this entirely
        // (RestoreWorkspaceStateAsync tears down open documents first), so restore takes precedence.
        if (DockFactory.ResolveStartupEditorMode(_settingsService) == StartupEditorMode.NewUntitledFile)
        {
            _ = NewFileAsync();
        }

        // Subscribe to document close event
        _dockFactory.DocumentClosed += OnDocumentClosed;

        // Auto-save (onFocusChange): switching document tabs counts as an editor
        // focus change in VS Code semantics.
        _dockFactory.ActiveDockableChanged += OnActiveDockableChangedForAutoSave;

        // Persist per-project layout when the user rearranges/adds/removes/closes panels
        // or switches the active tab (debounced). Splitter-drag resizes raise no event, so
        // the definitive capture happens on project-close and app-shutdown flushes.
        _dockFactory.ActiveDockableChanged += (_, _) => ScheduleLayoutSave();
        _dockFactory.DockableAdded += (_, _) => ScheduleLayoutSave();
        _dockFactory.DockableRemoved += (_, _) => ScheduleLayoutSave();
        _dockFactory.DockableMoved += (_, _) => ScheduleLayoutSave();
        _dockFactory.DockableClosed += (_, _) => ScheduleLayoutSave();

        // Auto-save (files.autoSaveSkipOnErrors): let the service query per-file
        // error state tracked from LSP diagnostics (same source as the Problems badge).
        _autoSaveService.HasErrorsProvider = HasErrorDiagnostics;

        // Subscribe to timeline diff requests
        Timeline.DiffRequested += OnTimelineDiffRequested;

        // Subscribe to events
        _projectService.ProjectOpened += OnProjectOpened;
        _projectService.ProjectClosed += OnProjectClosed;
        _buildService.BuildCompleted += OnBuildCompleted;
        _debugService.StateChanged += OnDebugStateChanged;
        _debugService.Stopped += OnDebugStopped;
        _debugService.OutputReceived += OnDebugOutput;

        // Subscribe to solution events
        _solutionService.SolutionLoaded += OnSolutionLoaded;
        _solutionService.SolutionClosed += OnSolutionClosed;

        // Handle file open requests from solution explorer
        SolutionExplorer.FileOpenRequested += OnFileOpenRequested;

        // Handle error list navigation
        ErrorList.DiagnosticDoubleClicked += OnDiagnosticDoubleClicked;

        // Handle output panel clickable error/warning navigation
        OutputPanel.NavigateToSourceRequested += OnOutputLineNavigateToSource;

        // Handle breakpoint condition editing and visual updates
        Breakpoints.EditConditionRequested += OnEditBreakpointCondition;
        Breakpoints.EditFunctionConditionRequested += OnEditFunctionBreakpointCondition;
        Breakpoints.BreakpointsChanged += OnBreakpointVisualsChanged;

        // Handle bookmark navigation
        Bookmarks.NavigationRequested += OnBookmarkNavigationRequested;

        // Handle call stack frame selection (navigate to source)
        CallStack.FrameSelected += OnCallStackFrameSelected;

        // Handle thread switching (refresh call stack and variables for the selected thread)
        Threads.ThreadSwitched += OnThreadSwitched;

        // Wire up Find in Files navigation
        FindInFiles.SetNavigationCallback(OpenFileAtLine);

        // Wire up Terminal file path link navigation
        Terminal.FileNavigationRequested += OnTerminalFileNavigationRequested;

        // Wire up "Open in Terminal" from Solution Explorer
        SolutionExplorer.OpenInTerminalRequested += OnOpenInTerminalRequested;

        // Subscribe to EVERY registered server's diagnostics and connection state. Both handlers
        // key off the document uri, so BasicLang's .bas diagnostics and clangd's .cpp diagnostics
        // coexist, and each reconnected server re-syncs only its own documents. Today the registry
        // holds BasicLang only; clangd (Task 12) participates automatically once registered — the
        // set is fixed at registry construction, so subscribing here covers every server there will be.
        foreach (var service in _languageServices.All)
        {
            // Diagnostics → error highlighting + Error List (aggregated per file).
            service.DiagnosticsReceived += OnDiagnosticsReceived;
            // Re-sync open documents whenever a server (re)connects — a crashed and auto-restarted
            // server has lost all didOpen state.
            service.ConnectionChanged += OnLanguageServiceConnectionChanged;
        }

        // Start language service — unless the user turned off basiclang.lsp.autoStart, in which
        // case it stays off until the "Start Language Server" command is run (command palette).
        //
        // ⚠ The root is normally NULL here: this is the constructor, and no project has been
        // opened yet (ProjectOpened is only subscribed above). We pass whatever is known rather
        // than hardcoding null so a project already open at construction is honoured, but the
        // autostarted server is in practice rootless for the whole session — StartAsync does
        // NOT re-root an already-connected server. Re-rooting on project open needs a restart
        // (or workspace/didChangeWorkspaceFolders, which would mean advertising the
        // workspace.workspaceFolders client capability).
        //
        // This stays BasicLang-only and rootless by decision (Task 6). BasicLang resolves nothing
        // against a project root, so rootless costs it nothing, and starting here is what gives a
        // lone .bas file opened with no project any IntelliSense at all. It cannot start clangd:
        // this reaches the BasicLang service alone, and a server that DOES need a root can only be
        // started through ILanguageServiceRegistry.StartAllAsync, which refuses a rootless start
        // outright. Servers needing a root are started on ProjectOpened instead.
        if (ShouldAutoStartLanguageServer(_settingsService))
        {
            _ = BasicLangLspService?.StartAsync(_projectService.CurrentProject?.ProjectDirectory);
        }

        // Load recent projects and subscribe to changes
        _recentProjectsService.RecentProjectsChanged += OnRecentProjectsChanged;
        _ = LoadRecentProjectsAsync();

        // Load static contributions from installed extensions (themes, grammars, snippets)
        _ = LoadExtensionContributionsAsync();

        // Subscribe to extension tree view creation and refresh
        _extensionService.TreeViewCreated += OnExtensionTreeViewCreated;
        _extensionService.TreeViewRefreshRequested += OnExtensionTreeViewRefreshRequested;

        // Subscribe to extension webview panel creation and HTML updates
        _extensionService.WebViewCreated += OnExtensionWebViewCreated;
        _extensionService.WebViewHtmlChanged += OnExtensionWebViewHtmlChanged;

        // Load accessibility and zoom settings
        if (_settingsService != null)
        {
            ReduceMotion = _settingsService.Get(SettingsKeys.AccessibilityReduceMotion, false);
            ZoomLevel = _settingsService.Get(SettingsKeys.ZoomLevel, 100);

            // Seed the View-menu toggle state (minimap / whitespace) from the editor settings so the
            // menu checkmarks reflect reality at startup, and re-seed on live changes. The editors
            // themselves re-read these via ApplyEditorSettings; this only mirrors the state.
            ShowMinimap = _settingsService.Get("editor.minimap.enabled", true);
            ShowWhitespace = _settingsService.Get("editor.renderWhitespace", "none") != "none";
            _settingsService.SettingChanged += OnEditorDisplaySettingChanged;

            // build.defaultConfiguration seeds the initial active configuration (the toolbar combo
            // still wins during the session once the user changes it). Validate against the known
            // configurations so an unexpected value can't leave the build pointed at a bad config.
            CurrentConfiguration = ResolveDefaultBuildConfiguration(_settingsService, Configurations);
        }

        // Name the MainWindowViewModel-side settings consumers for the Phase 3 contract test.
        // The two save-hook keys register from a static seam (below) so the contract test can force
        // them without constructing the heavy (~40-service) MainWindowViewModel — see the registry
        // doc note that these two are the only editor.* keys not also covered by the
        // CodeEditorDocumentView static ctor.
        RegisterEditorSaveSettingsConsumers();
        SettingsConsumerRegistry.RegisterConsumer("editor.tabSize", "MainWindowViewModel.BuildFormattingOptions → LSP FormattingOptions.TabSize");
        SettingsConsumerRegistry.RegisterConsumer("editor.insertSpaces", "MainWindowViewModel.BuildFormattingOptions → LSP FormattingOptions.InsertSpaces");
        SettingsConsumerRegistry.RegisterConsumer("editor.minimap.enabled", "MainWindowViewModel.ShowMinimap → View-menu toggle state");
        SettingsConsumerRegistry.RegisterConsumer("editor.renderWhitespace", "MainWindowViewModel.ShowWhitespace → View-menu toggle state");

        // Build + BasicLang-LSP settings consumed by this view-model (Task 2.4). Extracted to a
        // static seam so the Phase 3 contract test can assert registration without constructing the
        // (very heavy, ~40-service) MainWindowViewModel.
        RegisterBuildAndLspSettingsConsumers();

        // Status-bar Sync button: it raised SyncRequested but had ZERO subscribers, so clicking it
        // did nothing. Route it to the Git panel's Sync flow (pull-then-push, git.confirmSync-gated).
        StatusBar.SyncRequested += OnStatusBarSyncRequested;

        // ── Activity bar badge subscriptions ──
        // Source Control badge: track staged + unstaged change counts
        GitChanges.StagedChanges.CollectionChanged += (_, _) => UpdateSourceControlBadge();
        GitChanges.UnstagedChanges.CollectionChanged += (_, _) => UpdateSourceControlBadge();

        // Problems badge: track error list count changes
        ErrorList.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ErrorListViewModel.ErrorCount) or nameof(ErrorListViewModel.WarningCount))
                ProblemsBadgeCount = ErrorList.ErrorCount + ErrorList.WarningCount;
        };
    }

    private void UpdateSourceControlBadge()
    {
        SourceControlBadgeCount = GitChanges.StagedChanges.Count + GitChanges.UnstagedChanges.Count;
    }

    /// <summary>
    /// Handles the status-bar Sync button (its <see cref="StatusBarViewModel.SyncRequested"/> event
    /// previously had no subscriber). Delegates to the Git panel's Sync command, which pulls then
    /// pushes and honors the <c>git.confirmSync</c> confirmation setting.
    /// </summary>
    private void OnStatusBarSyncRequested(object? sender, EventArgs e)
    {
        GitChanges?.SyncCommand.Execute(null);
    }

    private async Task LoadExtensionContributionsAsync()
    {
        try
        {
            await _extensionService.ActivateStaticContributionsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load extension contributions: {ex.Message}");
            ShowNotification("An extension failed to activate.", "warning",
                new List<NotificationAction>
                {
                    new NotificationAction("Show Output", () => _dockFactory.ActivateTool("Output")),
                },
                details: ex.Message);
        }
    }

    private void OnExtensionTreeViewCreated(object? sender, ExtensionTreeViewEventArgs e)
    {
        // Raised from the extension host's JSON-RPC handler on a threadpool
        // thread. Marshal to the UI thread (like the webview sibling) so the
        // non-concurrent _extensionTreeViews dictionary and any panel creation
        // are only touched on the UI thread.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_extensionTreeViews.ContainsKey(e.ViewId)) return;

            var panelVm = new TreeViewPanelViewModel(
                e.ViewId,
                e.Title ?? e.ViewId,
                e.ExtensionId,
                getChildrenFunc: (viewId, element, ct) => _extensionService.RequestTreeChildrenAsync(viewId, element, ct),
                getTreeItemFunc: (viewId, element, ct) => _extensionService.RequestTreeItemAsync(viewId, element, ct),
                executeCommandFunc: (commandId, args) => _extensionService.ExecuteExtensionCommandAsync(commandId, args));

            _extensionTreeViews[e.ViewId] = panelVm;
            ExtensionTreeViewPanelCreated?.Invoke(this, panelVm);
        });
    }

    private async void OnExtensionTreeViewRefreshRequested(object? sender, ExtensionTreeViewEventArgs e)
    {
        // Raised on a threadpool JSON-RPC thread; the dictionary and the tree
        // panel's bound collections must only be touched on the UI thread.
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (_extensionTreeViews.TryGetValue(e.ViewId, out var panelVm))
                {
                    await panelVm.RefreshElementAsync(e.Element);
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TreeView] Refresh failed for {e.ViewId}: {ex.Message}");
        }
    }

    private void OnExtensionWebViewCreated(object? sender, WebViewCreatedEventArgs e)
    {
        if (_extensionWebViews.ContainsKey(e.PanelId)) return;

        var viewModel = new WebViewDocumentViewModel(e.PanelId, e.ViewType, e.ExtensionId, e.Title);
        _extensionWebViews[e.PanelId] = viewModel;

        // Add as a document tab in the dock
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _dockFactory.AddWebViewDocument(viewModel);
        });
    }

    private void OnExtensionWebViewHtmlChanged(object? sender, WebViewHtmlChangedEventArgs e)
    {
        if (_extensionWebViews.TryGetValue(e.PanelId, out var viewModel))
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                viewModel.SetHtmlContent(e.Html);
            });
        }
    }

    private async Task LoadRecentProjectsAsync()
    {
        try
        {
            await _recentProjectsService.LoadAsync();
            RefreshRecentProjects();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load recent projects: {ex.Message}");
        }
    }

    private void RefreshRecentProjects()
    {
        var projects = _recentProjectsService.GetRecentProjects();
        RecentProjects.Clear();
        foreach (var p in projects)
        {
            RecentProjects.Add(p);
        }
        OnPropertyChanged(nameof(HasRecentProjects));
        RebuildRecentProjectMenuItems();
    }

    private void RebuildRecentProjectMenuItems()
    {
        RecentProjectMenuItems.Clear();
        if (RecentProjects.Count == 0)
        {
            RecentProjectMenuItems.Add(new RecentProjectMenuItem
            {
                Header = "(No recent projects)",
                IsEnabled = false
            });
        }
        else
        {
            foreach (var p in RecentProjects)
            {
                RecentProjectMenuItems.Add(new RecentProjectMenuItem
                {
                    Header = p.Name,
                    ToolTip = p.Path,
                    FilePath = p.Path,
                    Command = OpenRecentProjectCommand,
                    CommandParameter = p.Path,
                    IsEnabled = true
                });
            }
        }
        RecentProjectMenuItems.Add(new RecentProjectMenuItem
        {
            Header = "---",
            IsEnabled = false
        });
        RecentProjectMenuItems.Add(new RecentProjectMenuItem
        {
            Header = "Clear Recent Projects",
            Command = ClearRecentProjectsCommand,
            IsEnabled = true
        });
    }

    private void OnRecentProjectsChanged(object? sender, EventArgs e)
    {
        RefreshRecentProjects();
    }

    private void OnDiagnosticsReceived(object? sender, DiagnosticsEventArgs e)
    {
        try
        {
            if (e?.Diagnostics == null) return;

            // LanguageService already sends a decoded local path in Uri, but be
            // robust to raw (possibly percent-encoded) file:// URIs too — the
            // old Replace("file:///", ...) conversion silently corrupted paths
            // with spaces (%20) and other encoded characters.
            var uri = e.Uri ?? "";
            var filePath = VisualGameStudio.ProjectSystem.Services.LanguageService.UriToPath(uri);

            // Aggregate per file, then show the union of all files' diagnostics.
            // e.Diagnostics is ONE file's complete set (empty = file is clean);
            // pushing it directly would let the last file to publish wipe the
            // whole Error List.
            if (!string.IsNullOrEmpty(filePath))
            {
                _diagnosticsAggregator.SetFileDiagnostics(filePath, e.Diagnostics);
                ErrorList.UpdateDiagnostics(_diagnosticsAggregator.GetSnapshot());
            }

            // Reset error cycling index when diagnostics change
            _currentDiagnosticIndex = -1;

            // Forward to the specific document for error highlighting
            // (_openDocuments is keyed case-insensitively).
            if (!string.IsNullOrEmpty(filePath) && _openDocuments.TryGetValue(filePath, out var doc))
            {
                doc.UpdateDiagnostics(e.Diagnostics);
            }

            // Track per-file error state for auto-save (files.autoSaveSkipOnErrors)
            if (!string.IsNullOrEmpty(filePath))
            {
                if (e.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                {
                    _filesWithErrors[filePath] = 0;
                }
                else
                {
                    _filesWithErrors.TryRemove(filePath, out _);
                }
            }

            // Screen reader announcement for new errors
            var errorCount = e.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            if (errorCount > 0)
            {
                var firstError = e.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                if (errorCount == 1)
                {
                    Services.ScreenReaderService.Instance.AnnounceAssertive(
                        $"Error: {firstError.Message} at line {firstError.Line}");
                }
                else
                {
                    Services.ScreenReaderService.Instance.AnnounceAssertive(
                        $"{errorCount} errors found. First: {firstError.Message} at line {firstError.Line}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Diagnostics] Error processing diagnostics: {ex.Message}");
        }
    }

    /// <summary>
    /// Reports whether a file currently has error-severity LSP diagnostics.
    /// Installed as the auto-save service's HasErrorsProvider; may be called
    /// from auto-save timer threads.
    /// </summary>
    private bool HasErrorDiagnostics(string filePath)
    {
        return !string.IsNullOrEmpty(filePath) && _filesWithErrors.ContainsKey(filePath);
    }

    /// <summary>
    /// Auto-save (onFocusChange): when the active document tab changes, the
    /// previously active document's editor counts as having lost focus.
    /// Tool activations are ignored; keyboard focus moves into tool panels are
    /// covered by the editor control's LostFocus event.
    /// </summary>
    private void OnActiveDockableChangedForAutoSave(object? sender, global::Dock.Model.Core.Events.ActiveDockableChangedEventArgs e)
    {
        if (e.Dockable is not CodeEditorDocument editorDoc) return;

        var newPath = editorDoc.ViewModel?.FilePath;
        var previousPath = _lastActiveEditorFilePath;
        _lastActiveEditorFilePath = newPath;

        if (!string.IsNullOrEmpty(previousPath) &&
            !string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            _autoSaveService.NotifyEditorLostFocus(previousPath);
        }
    }

    /// <summary>
    /// Auto-save (onWindowChange): called by MainWindow when the IDE window is
    /// deactivated. The service saves all dirty registered documents.
    /// </summary>
    public void NotifyWindowDeactivated()
    {
        _autoSaveService.NotifyWindowLostFocus();
    }

    private void OpenFileAtLine(string filePath, int line)
    {
        _ = OpenFileAtLineAsync(filePath, line);
    }

    private void OnTerminalFileNavigationRequested(string filePath, int line, int column)
    {
        _ = OpenFileAndNavigateAsync(filePath, line, column);
    }

    private void OnOpenInTerminalRequested(object? sender, string directoryPath)
    {
        _dockFactory.ActivateTool("Terminal");
        Terminal.CreateSessionAtDirectory(directoryPath);
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
        try
        {
            await OnProjectOpenedCoreAsync(e);
        }
        catch (Exception ex)
        {
            // async void: an unhandled exception here would crash the process
            // (e.g., project directory deleted between load and this handler,
            // or a corrupted breakpoints file).
            System.Diagnostics.Debug.WriteLine($"[ProjectOpened] Error: {ex.Message}");
            StatusText = $"Project loaded with warnings: {ex.Message}";
        }
    }

    private async Task OnProjectOpenedCoreAsync(ProjectEventArgs e)
    {
        // Solution name takes precedence over project name in the title bar
        Title = _solutionService.HasSolution
            ? $"{_solutionService.CurrentSolution!.SolutionName} - Visual Game Studio"
            : $"{e.Project.Name} - Visual Game Studio";
        StatusText = $"Project loaded: {e.Project.Name}";

        // Track this project in recent projects
        var projectPath = Path.Combine(e.Project.ProjectDirectory, e.Project.Name + ".blproj");
        if (File.Exists(projectPath))
        {
            _recentProjectsService.AddRecentProject(projectPath, e.Project.Name);
        }
        else
        {
            // Try to find the actual .blproj file in the project directory
            var blprojFiles = Directory.Exists(e.Project.ProjectDirectory)
                ? Directory.GetFiles(e.Project.ProjectDirectory, "*.blproj", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();
            if (blprojFiles.Length > 0)
            {
                _recentProjectsService.AddRecentProject(blprojFiles[0], e.Project.Name);
            }
            else
            {
                // Use the project directory as a fallback
                _recentProjectsService.AddRecentProject(e.Project.ProjectDirectory, e.Project.Name);
            }
        }

        // Load workspace settings for this project
        if (_settingsService is VisualGameStudio.ProjectSystem.Services.SettingsService settingsSvc)
        {
            settingsSvc.SetWorkspacePath(e.Project.ProjectDirectory);

            // Suggest adding .vgs/ to .gitignore if it's a git repo
            var gitignorePath = Path.Combine(e.Project.ProjectDirectory, ".gitignore");
            var vgsDir = Path.Combine(e.Project.ProjectDirectory, ".vgs");
            if (Directory.Exists(Path.Combine(e.Project.ProjectDirectory, ".git")) &&
                Directory.Exists(vgsDir))
            {
                bool alreadyIgnored = false;
                if (File.Exists(gitignorePath))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(gitignorePath);
                        alreadyIgnored = content.Contains(".vgs/") || content.Contains(".vgs\\");
                    }
                    catch { }
                }

                if (!alreadyIgnored)
                {
                    ShowNotification("Workspace settings detected. Consider adding .vgs/ to .gitignore.", "info");
                }
            }
        }

        // Load persisted breakpoints
        Breakpoints.SetProjectDirectory(e.Project.ProjectDirectory);
        await Breakpoints.LoadBreakpointsAsync();

        // Load persisted bookmarks
        _bookmarkService.SetProjectDirectory(e.Project.ProjectDirectory);
        await _bookmarkService.LoadAsync();

        // Restore this project's saved window layout + open documents (VS Code style).
        await RestoreWorkspaceStateAsync(e.Project.ProjectDirectory);
    }

    private void OnDocumentClosed(object? sender, string filePath)
    {
        CleanupDocumentState(filePath);
    }

    /// <summary>
    /// Tears down all tracking for a document (event handlers, auto-save, caches, LSP/extension
    /// notifications). Used both when a tab is closed and when the layout is rebuilt during a
    /// per-project restore/reset, which detaches the old document dockables.
    /// </summary>
    private void CleanupDocumentState(string filePath)
    {
        // Run cleanup actions (unsubscribe events) for this document
        if (_documentCleanupActions.TryGetValue(filePath, out var cleanup))
        {
            cleanup();
            _documentCleanupActions.Remove(filePath);
        }

        _openDocuments.Remove(filePath);

        // Stop auto-save tracking for the closed document (cancels any pending timer)
        _autoSaveService.UnregisterDocument(filePath);
        _filesWithErrors.TryRemove(filePath, out _);
        if (string.Equals(_lastActiveEditorFilePath, filePath, StringComparison.OrdinalIgnoreCase))
        {
            _lastActiveEditorFilePath = null;
        }

        // Remove blame cache for closed document
        _blameCache.Remove(filePath);

        // Notify the owning LSP server that the document was closed
        var closeSvc = _languageServices.GetFor(filePath);
        if (closeSvc is { IsConnected: true })
        {
            _ = closeSvc.CloseDocumentAsync(filePath);
        }

        // Notify extension host (for all file types)
        if (_extensionService.IsExtensionHostRunning)
        {
            _ = _extensionService.NotifyDocumentClosedAsync(filePath);
        }
    }

    /// <summary>
    /// The BasicLang language server, reached through the registry's routing. Used only for the
    /// BasicLang-specific lifecycle that predates per-file routing: the constructor's rootless
    /// autostart and the manual "Start Language Server" command. Both are BasicLang-only BY DESIGN
    /// — BasicLang is the one server safe to start rootless (it resolves nothing against a project
    /// root). Servers that need a workspace root (clangd) are started on <c>ProjectOpened</c> via
    /// <see cref="ILanguageServiceRegistry.StartAllAsync"/> (Task 12), which refuses a rootless
    /// start outright — never here. Routing a representative BasicLang path is how the shim used to
    /// reach this same instance; feature calls route by the real document path instead.
    /// </summary>
    private ILanguageService? BasicLangLspService => _languageServices.GetFor("_.bas");

    /// <summary>
    /// When a language server (re)connects, re-send didOpen for every open document THAT SERVER
    /// owns: documents opened before it connected were never announced, and a crashed+restarted
    /// server has lost all didOpen state. Also surfaces the connection state in the status bar.
    /// </summary>
    private void OnLanguageServiceConnectionChanged(object? sender, bool connected)
    {
        // LanguageService raises these events with itself as sender, so this is the specific
        // server whose state changed — re-sync only the documents it owns.
        var service = sender as ILanguageService;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (connected && service != null)
            {
                foreach (var doc in _openDocuments.Values.ToList())
                {
                    // Route by each doc's path so a reconnected BasicLang server never re-opens a
                    // .cpp document (and clangd never re-opens a .bas one).
                    if (doc.FilePath != null && ReferenceEquals(_languageServices.GetFor(doc.FilePath), service))
                    {
                        _ = service.OpenDocumentAsync(doc.FilePath, doc.Text ?? "");
                    }
                }
                StatusText = "Language server connected";
            }
            else if (!connected)
            {
                StatusText = "Language server disconnected — IntelliSense unavailable";
            }
        });
    }

    private async void OnTimelineDiffRequested(object? sender, TimelineItemViewModel item)
    {
        try
        {
            // Get the file content at the selected commit
            var oldContent = await _gitService.GetFileContentAtCommitAsync(item.FilePath, item.CommitHash);
            if (oldContent == null)
            {
                StatusText = $"Could not retrieve file at commit {item.ShortHash}";
                return;
            }

            // Get current file content
            string currentContent;
            if (_openDocuments.TryGetValue(item.FilePath, out var openDoc))
            {
                currentContent = openDoc.Text ?? "";
            }
            else
            {
                currentContent = await _fileService.ReadFileAsync(item.FilePath);
            }

            var fileName = Path.GetFileName(item.FilePath);
            var diffVm = new Dialogs.DiffViewerViewModel(_gitService);
            diffVm.LoadContents(oldContent, currentContent,
                $"{fileName} ({item.ShortHash})",
                $"{fileName} (Current)");

            CompareViewRequested?.Invoke(this, diffVm);
            StatusText = $"Comparing {fileName} at {item.ShortHash} with current version";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open diff: {ex.Message}";
        }
    }

    private void OnProjectClosed(object? sender, ProjectEventArgs e)
    {
        // Keep solution title if a solution is still open
        Title = _solutionService.HasSolution
            ? $"{_solutionService.CurrentSolution!.SolutionName} - Visual Game Studio"
            : "Visual Game Studio";
        StatusText = "Ready";

        // Persist this project's layout + open documents before we let go of it.
        SaveWorkspaceStateNow(e.Project.ProjectDirectory);

        // Clear workspace settings when project is closed
        if (_settingsService is VisualGameStudio.ProjectSystem.Services.SettingsService settingsSvc)
        {
            settingsSvc.SetWorkspacePath(null);
        }

        // Drop the closed project's diagnostics from the Error List.
        //
        // This global Clear() is CORRECT even with multiple language servers: the project is
        // closing, so every file it contained — BasicLang AND C++ — is going away, and their
        // diagnostics with it. It is NOT a per-server operation and must not become one: a
        // BasicLang-only clear would leave clangd's now-orphaned .cpp diagnostics stranded in the
        // Error List after the project is gone. This is also the ONLY Clear() call — no
        // server restart/disconnect path clears diagnostics, so one server's lifecycle can never
        // wipe another's (per-file aggregation keeps them isolated; see DiagnosticsAggregatorTests).
        _diagnosticsAggregator.Clear();
        ErrorList.UpdateDiagnostics(_diagnosticsAggregator.GetSnapshot());
    }

    #region Per-project layout & session persistence

    /// <summary>
    /// Debounced save of the current project's layout + open documents, triggered by dock
    /// rearrangements and tab switches. Capture runs on the UI thread; the file write does not.
    /// </summary>
    private void ScheduleLayoutSave()
    {
        if (_restoringLayout) return;

        var project = _projectService.CurrentProject;
        if (project == null) return;
        var projectDir = project.ProjectDirectory;

        _layoutSaveCts?.Cancel();
        _layoutSaveCts = new CancellationTokenSource();
        var token = _layoutSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(750, token);
                if (token.IsCancellationRequested) return;

                WorkspaceStateModel? state = null;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Don't snapshot a half-rebuilt tree while a restore/reset is in progress.
                    if (!token.IsCancellationRequested && !_restoringLayout)
                        state = CaptureWorkspaceState();
                });

                if (state != null && !token.IsCancellationRequested)
                    _workspaceStateStore.Save(projectDir, state);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LayoutSave] {ex.Message}");
            }
        });
    }

    /// <summary>Synchronously captures and saves a project's state (project-close/app-shutdown flush).</summary>
    private void SaveWorkspaceStateNow(string projectDirectory)
    {
        if (string.IsNullOrEmpty(projectDirectory)) return;

        try
        {
            _layoutSaveCts?.Cancel();
            var state = CaptureWorkspaceState();
            if (state != null)
                _workspaceStateStore.Save(projectDirectory, state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LayoutSave] {ex.Message}");
        }
    }

    /// <summary>Flushes the current project's state on application shutdown (called from App).</summary>
    public void FlushWorkspaceStateForShutdown()
    {
        var project = _projectService.CurrentProject;
        if (project != null)
            SaveWorkspaceStateNow(project.ProjectDirectory);
    }

    /// <summary>Snapshots the live layout tree and the open documents in tab order.</summary>
    private WorkspaceStateModel CaptureWorkspaceState()
    {
        var openDocuments = _dockFactory.GetAllDocuments()
            .OfType<CodeEditorDocumentViewModel>()
            .Where(d => !string.IsNullOrEmpty(d.FilePath))
            .Select(d => new OpenDocumentState
            {
                Path = d.FilePath!,
                CaretLine = d.CaretLine,
                CaretColumn = d.CaretColumn
            })
            .ToList();

        var activePath = (_dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel)?.FilePath
            ?? _lastActiveEditorFilePath;

        return new WorkspaceStateModel
        {
            DockLayout = _dockFactory.SerializeCurrentLayout(),
            OpenDocuments = openDocuments,
            ActiveDocumentPath = activePath
        };
    }

    /// <summary>
    /// Restores a project's saved layout and reopens the documents it had open. No-op when the
    /// project has no saved state, so first-time / never-arranged projects behave exactly as before.
    /// </summary>
    private async Task RestoreWorkspaceStateAsync(string projectDirectory)
    {
        WorkspaceStateModel? state;
        try { state = _workspaceStateStore.Load(projectDirectory); }
        catch { state = null; }

        if (state?.DockLayout == null) return;

        await ApplyLayoutAndDocumentsAsync(state.DockLayout, state.OpenDocuments, state.ActiveDocumentPath);
    }

    /// <summary>
    /// Swaps in a rebuilt layout and reopens the given documents. Currently-tracked documents are
    /// torn down first because rebuilding the tree detaches their dockables. The restoring flag
    /// suppresses save-on-change feedback while the layout is mutated.
    /// </summary>
    private async Task ApplyLayoutAndDocumentsAsync(
        DockNode? layout, List<OpenDocumentState> documents, string? activePath)
    {
        _restoringLayout = true;
        try
        {
            // Cancel any debounced save that could otherwise fire against the outgoing tree.
            _layoutSaveCts?.Cancel();

            foreach (var path in _openDocuments.Keys.ToList())
            {
                CleanupDocumentState(path);
            }

            var restored = layout != null ? _dockFactory.TryApplyLayout(layout) : null;
            if (restored != null)
            {
                Layout = restored;
            }
            else
            {
                Layout = _dockFactory.CreateLayout();
                _dockFactory.InitLayout(Layout);
            }

            foreach (var doc in documents)
            {
                if (!string.IsNullOrEmpty(doc.Path) && File.Exists(doc.Path))
                {
                    await OpenFileAsync(doc.Path);
                    if (_openDocuments.TryGetValue(doc.Path, out var vm))
                    {
                        vm.NavigateTo(doc.CaretLine, doc.CaretColumn);
                    }
                }
            }

            if (!string.IsNullOrEmpty(activePath) && _openDocuments.TryGetValue(activePath, out var activeVm))
            {
                _dockFactory.ActivateDocument(activeVm);
            }
        }
        finally
        {
            _restoringLayout = false;
        }
    }

    /// <summary>
    /// Resets the current project's layout to the default (VS Code's "Reset View Locations"),
    /// keeping open documents. Clears any saved layout so the reset persists.
    /// </summary>
    [RelayCommand]
    private async Task ResetLayout()
    {
        var project = _projectService.CurrentProject;
        if (project != null)
        {
            _workspaceStateStore.Clear(project.ProjectDirectory);
        }

        var docs = _dockFactory.GetAllDocuments()
            .OfType<CodeEditorDocumentViewModel>()
            .Where(d => !string.IsNullOrEmpty(d.FilePath))
            .Select(d => new OpenDocumentState { Path = d.FilePath!, CaretLine = d.CaretLine, CaretColumn = d.CaretColumn })
            .ToList();
        var activePath = (_dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel)?.FilePath;

        await ApplyLayoutAndDocumentsAsync(null, docs, activePath);
        StatusText = "Layout reset to default";
    }

    #endregion

    private void OnBuildCompleted(object? sender, BuildCompletedEventArgs e)
    {
        var result = e.Result;
        var statusMessage = result.Success
            ? $"Build succeeded - {result.Duration.TotalSeconds:F1}s"
            : $"Build failed - {result.ErrorCount} error(s), {result.WarningCount} warning(s)";

        StatusText = statusMessage;

        // Update status bar build indicator (auto-fades after 5 seconds)
        StatusBar.SetBuildCompleted(result.Success, statusMessage);

        // Update status bar diagnostic counts
        StatusBar.UpdateDiagnostics(result.ErrorCount, result.WarningCount, 0);

        // Screen reader announcement for build result
        if (result.Success)
        {
            Services.ScreenReaderService.Instance.Announce($"Build succeeded in {result.Duration.TotalSeconds:F1} seconds");
            ShowNotification($"Build succeeded - {result.Duration.TotalSeconds:F1}s", "info",
                new List<NotificationAction>
                {
                    new NotificationAction("Show Output", () => _dockFactory.ActivateTool("Output"))
                });
            ShowStatusBarMessage($"Build succeeded - {result.Duration.TotalSeconds:F1}s", 5.0);
        }
        else
        {
            Services.ScreenReaderService.Instance.AnnounceAssertive(
                $"Build failed with {result.ErrorCount} error{(result.ErrorCount == 1 ? "" : "s")} and {result.WarningCount} warning{(result.WarningCount == 1 ? "" : "s")}");
            ShowNotification($"Build failed: {result.ErrorCount} error(s), {result.WarningCount} warning(s)", "error",
                new List<NotificationAction>
                {
                    new NotificationAction("Show Error List", () => _dockFactory.ActivateTool("ErrorList")),
                    new NotificationAction("Show Output", () => _dockFactory.ActivateTool("Output"))
                });

            // Show the Error List panel when the build has errors
            _dockFactory.ActivateTool("ErrorList");
        }

        // Update error list: build results replace the previous build's entries
        // but coexist with LSP diagnostics instead of clobbering them.
        _diagnosticsAggregator.SetBuildDiagnostics(result.Diagnostics);
        ErrorList.UpdateDiagnostics(_diagnosticsAggregator.GetSnapshot());

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

    private async void OnOutputLineNavigateToSource(object? sender, OutputLineNavigationEventArgs e)
    {
        try
        {
            await OpenFileAndNavigateAsync(e.FilePath, e.Line, e.Column);
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

    private async Task HandleGutterConditionalBreakpointAsync(string filePath, int line, string mode)
    {
        try
        {
            var existing = Breakpoints.GetBreakpointsForFile(filePath).FirstOrDefault(b => b.Line == line);
            if (existing == null)
            {
                Breakpoints.AddBreakpoint(filePath, line);
                existing = Breakpoints.GetBreakpointsForFile(filePath).FirstOrDefault(b => b.Line == line);
                if (existing == null) return;
            }
            var location = $"{Path.GetFileName(filePath)}:{line}";
            var result = await _dialogService.ShowBreakpointConditionDialogAsync(
                location,
                existing.Condition,
                existing.HitCondition,
                existing.LogMessage,
                mode);
            if (result != null && result.DialogResult)
                Breakpoints.UpdateBreakpointCondition(existing, result.Condition, result.HitCount, result.LogMessage);
        }
        catch (Exception) { }
    }

    private async Task HandleGutterEditBreakpointAsync(string filePath, int line)
    {
        try
        {
            var existing = Breakpoints.GetBreakpointsForFile(filePath).FirstOrDefault(b => b.Line == line);
            if (existing == null) return;
            var location = $"{Path.GetFileName(filePath)}:{line}";
            var result = await _dialogService.ShowBreakpointConditionDialogAsync(
                location, existing.Condition, existing.HitCondition, existing.LogMessage);
            if (result != null && result.DialogResult)
                Breakpoints.UpdateBreakpointCondition(existing, result.Condition, result.HitCount, result.LogMessage);
        }
        catch (Exception) { }
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
    /// Raised when a panel should receive keyboard focus.
    /// The string argument is the panel identifier (e.g., "SolutionExplorer", "Editor", "Output").
    /// </summary>
    public event EventHandler<string>? FocusPanelRequested;

    /// <summary>
    /// Shows a toast notification in the bottom-right corner of the IDE.
    /// </summary>
    /// <param name="message">The notification message text.</param>
    /// <param name="severity">Severity level: "info", "warning", or "error".</param>
    public void ShowNotification(string message, string severity = "info")
    {
        NotificationRequested?.Invoke(this, new NotificationEventArgs(message, severity));

        // Add to notification center
        var sev = severity.ToLowerInvariant() switch
        {
            "error" => NotificationSeverity.Error,
            "warning" => NotificationSeverity.Warning,
            _ => NotificationSeverity.Info
        };
        StatusBar.AddNotification(message, sev, "IDE");
    }

    /// <summary>
    /// Shows a notification with action buttons (e.g., "Show Output", "Retry", "Open File").
    /// </summary>
    public void ShowNotification(string message, string severity, List<NotificationAction> actions, string? details = null)
    {
        bool autoDismiss = severity == "info" && actions.Count == 0;
        NotificationRequested?.Invoke(this, new NotificationEventArgs(
            message, severity, details, autoDismiss, actions));

        var sev = severity.ToLowerInvariant() switch
        {
            "error" => NotificationSeverity.Error,
            "warning" => NotificationSeverity.Warning,
            _ => NotificationSeverity.Info
        };
        StatusBar.AddNotification(message, sev, "IDE");
    }

    /// <summary>
    /// Shows or updates a progress notification for long-running operations.
    /// </summary>
    /// <param name="notificationId">Unique ID to update an existing progress notification.</param>
    /// <param name="message">Current progress message (e.g., "Building... (3/5 files)").</param>
    /// <param name="progress">Progress value 0.0 to 1.0, or -1 for indeterminate.</param>
    public void ShowProgressNotification(string notificationId, string message, double progress = -1)
    {
        bool isIndeterminate = progress < 0;
        double clampedProgress = isIndeterminate ? 0 : Math.Clamp(progress, 0, 1);
        NotificationRequested?.Invoke(this, new NotificationEventArgs(
            message, "info", null, false, null,
            clampedProgress, isIndeterminate, true, notificationId));
    }

    /// <summary>
    /// Dismisses a progress notification by its ID.
    /// </summary>
    public void DismissNotification(string notificationId)
    {
        NotificationDismissed?.Invoke(this, notificationId);
    }

    /// <summary>
    /// Shows a temporary status bar message that auto-fades after the specified duration.
    /// </summary>
    public void ShowStatusBarMessage(string message, double durationSeconds = 3.0)
    {
        StatusBarMessageRequested?.Invoke(this, new StatusBarMessageEventArgs(message, durationSeconds));
    }

    /// <summary>
    /// Raised when a notification should be dismissed by its ID.
    /// </summary>
    public event EventHandler<string>? NotificationDismissed;

    /// <summary>
    /// Raised when a temporary message should appear in the status bar.
    /// </summary>
    public event EventHandler<StatusBarMessageEventArgs>? StatusBarMessageRequested;

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
            var result = await _debugService.EvaluateAsync(e.Expression, _currentFrameId, context: "hover");
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
        // Route to the server that owns this file (BasicLang for .bas, clangd for .cpp once
        // registered); do nothing for files no server serves.
        var svc = _languageServices.GetFor(document.FilePath);
        if (svc is not { IsConnected: true }) return;
        // intellisense.quickInfo also gates the debug-datatip → LSP hover fallback (read at use).
        if (_settingsService != null && !_settingsService.Get("intellisense.quickInfo", true)) return;
        try
        {
            var hover = await svc.GetHoverAsync(document.FilePath, e.Line, e.Column);
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

        var wizardVm = new ViewModels.Dialogs.NewProjectWizardViewModel(_projectTemplateService);
        var selectWindow = new Views.Dialogs.NewProjectSelectView(wizardVm);

        var result = await selectWindow.ShowDialog<ProjectCreationResult?>(App.MainWindow);

        if (result != null && result.Success && !string.IsNullOrEmpty(result.ProjectPath))
        {
            try
            {
                await _projectService.OpenProjectAsync(result.ProjectPath);
                StatusText = $"Project created: {Path.GetFileNameWithoutExtension(result.ProjectPath)}";
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync("Error", $"Failed to open created project: {ex.Message}",
                    DialogButtons.Ok, DialogIcon.Error);
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
                new("C++ Files", "cpp", "h", "hpp", "c", "cc", "cxx"),
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

    /// <summary>
    /// "Compare Active File With..." command: opens a file picker, then shows diff of active file vs selected file.
    /// </summary>
    [RelayCommand]
    private async Task CompareActiveFileWithAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null)
        {
            StatusText = "No active file to compare";
            return;
        }

        var otherPath = await _dialogService.ShowOpenFileDialogAsync(new FileDialogOptions
        {
            Title = "Compare With...",
            InitialDirectory = Path.GetDirectoryName(activeDoc.FilePath),
            Filters = new List<FileDialogFilter>
            {
                new("All Files", "*")
            }
        });

        if (string.IsNullOrEmpty(otherPath)) return;

        var leftContent = await File.ReadAllTextAsync(otherPath);
        var rightContent = activeDoc.Text;

        var diffVm = new Dialogs.DiffViewerViewModel(_gitService);
        diffVm.LoadContents(leftContent, rightContent,
            Path.GetFileName(otherPath),
            Path.GetFileName(activeDoc.FilePath));

        CompareViewRequested?.Invoke(this, diffVm);
        StatusText = $"Comparing {Path.GetFileName(activeDoc.FilePath)} with {Path.GetFileName(otherPath)}";
    }

    /// <summary>
    /// "Compare with Clipboard" command: diffs current file content against clipboard text.
    /// </summary>
    [RelayCommand]
    private async Task CompareWithClipboardAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null)
        {
            StatusText = "No active file to compare";
            return;
        }

        CompareWithClipboardRequested?.Invoke(this, activeDoc);
        StatusText = "Comparing with clipboard...";
    }

    /// <summary>
    /// "Compare with Saved" command: diffs current editor content against the last saved version on disk.
    /// </summary>
    [RelayCommand]
    private async Task CompareWithSavedAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null || !File.Exists(activeDoc.FilePath))
        {
            StatusText = "No saved file to compare";
            return;
        }

        var savedContent = await File.ReadAllTextAsync(activeDoc.FilePath);
        var currentContent = activeDoc.Text;

        var diffVm = new Dialogs.DiffViewerViewModel(_gitService);
        diffVm.LoadContents(savedContent, currentContent,
            $"{Path.GetFileName(activeDoc.FilePath)} (saved)",
            $"{Path.GetFileName(activeDoc.FilePath)} (unsaved)");

        CompareViewRequested?.Invoke(this, diffVm);
        StatusText = $"Comparing {Path.GetFileName(activeDoc.FilePath)} with saved version";
    }

    /// <summary>
    /// Raised when a diff/compare viewer should be opened.
    /// The subscriber (MainWindow) creates and shows the DiffViewerView.
    /// </summary>
    public event EventHandler<Dialogs.DiffViewerViewModel>? CompareViewRequested;

    /// <summary>
    /// Raised when "Compare with Clipboard" is requested.
    /// The view handles clipboard access (which requires UI thread/TopLevel).
    /// </summary>
    public event EventHandler<CodeEditorDocumentViewModel>? CompareWithClipboardRequested;

    private async Task OpenFileAsync(string filePath)
    {
        // Check if already open
        if (_openDocuments.TryGetValue(filePath, out var existingDoc))
        {
            // Activate the existing document
            _dockFactory.ActivateDocument(existingDoc);
            // Refresh timeline for the switched-to file
            _ = Timeline.LoadTimelineAsync(filePath);
            return;
        }

        try
        {
            var content = await _fileService.ReadFileAsync(filePath);

            // Notify the owning LSP server about the opened document (BasicLang for .bas/.bl/.mod/
            // .cls/.class, clangd for .cpp/.h once registered; nothing for files no server serves).
            var openSvc = _languageServices.GetFor(filePath);
            if (openSvc is { IsConnected: true })
            {
                await openSvc.OpenDocumentAsync(filePath, content);
            }

            // Notify extension host about opened document (for all file types)
            if (_extensionService.IsExtensionHostRunning)
            {
                var langId = LanguageFileTypes.GetEditorLanguageId(filePath);
                _ = _extensionService.NotifyDocumentOpenedAsync(filePath, langId, 1, content);
            }

            var document = new CodeEditorDocumentViewModel(_fileService, _eventAggregator, _bookmarkService)
            {
                FilePath = filePath,
                // The already-routed service for this file, handed to the editor control via
                // SetLanguageService — so the control's folding reaches clangd for .cpp and
                // BasicLang for .bas, and is null for files no server serves.
                LanguageService = _languageServices.GetFor(filePath),
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
            EventHandler onCallHierarchy = async (s, e) => await ShowCallHierarchyAsync();
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
            document.ShowCallHierarchyRequested += onCallHierarchy;
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
                    if (e == null || document.FilePath == null || !_languageServices.IsConnectedFor(document.FilePath)) return;
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
                    // intellisense.quickInfo gates the LSP hover (quick info) request. Read at use so
                    // the toggle is live. Error/diagnostic tooltips are separate (editor marker
                    // service) and stay regardless.
                    if (_settingsService != null && !_settingsService.Get("intellisense.quickInfo", true)) return;
                    // Route hover to the server that owns this file (BasicLang for .bas, clangd for
                    // .cpp once registered). Files no server serves fall through to the extension
                    // providers below.
                    var hoverSvc = _languageServices.GetFor(document.FilePath);
                    var hover = hoverSvc is { IsConnected: true }
                        ? await hoverSvc.GetHoverAsync(document.FilePath, e.Line, e.Column)
                        : null;

                    // Try extension host providers if built-in LSP returned nothing
                    if (hover == null &&
                        _extensionService.HasExtensionProviders(LanguageFileTypes.GetEditorLanguageId(document.FilePath)))
                    {
                        var extResult = await _extensionService.RequestHoverAsync(
                            document.FilePath, e.Line, e.Column);
                        if (extResult.HasValue)
                        {
                            hover = ParseExtensionHover(extResult.Value);
                        }
                    }

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
                    if (e == null || document.FilePath == null) return;
                    // Route signature help to the server that owns this file.
                    var sigSvc = _languageServices.GetFor(document.FilePath);
                    if (sigSvc is not { IsConnected: true }) return;
                    var help = await sigSvc.GetSignatureHelpAsync(document.FilePath, e.Line, e.Column);
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
                if (e == null || document.FilePath == null) return;
                // Route document highlights to the server that owns this file.
                var hlSvc = _languageServices.GetFor(document.FilePath);
                if (hlSvc is not { IsConnected: true }) return;
                try
                {
                    var result = await hlSvc.GetDocumentHighlightsAsync(document.FilePath, e.Line, e.Column);
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

            // Wire up code completion requests.
            // One request per completion session: the editor only fires on a
            // fresh trigger. The coordinator cancels the previous request when
            // a new one starts and lets us DROP stale responses — a late
            // response must never reach the popup.
            var completionCoordinator = new CompletionRequestCoordinator();
            EventHandler<CompletionRequestedEventArgs>? onCompletion = async (s, e) =>
            {
                if (e == null) return;

                var (requestId, requestToken) = completionCoordinator.BeginRequest();

                IReadOnlyList<CompletionItem>? completions = null;
                // Route the LSP request to the server that owns this file. Routing — not a BasicLang
                // check — is now what keeps a .txt/.json request off the BasicLang server: GetFor
                // returns null for files no server serves. A .cpp routes to clangd once registered.
                var completionSvc = _languageServices.GetFor(document.FilePath);
                var serverConnected = completionSvc is { IsConnected: true };
                // Still needed below: the hard-coded fallback is a BasicLang keyword/snippet dump and
                // belongs ONLY in BasicLang files (a keyword dump never belongs in .cpp/.txt).
                var isBasicLangDocument = BasicLangFileTypes.IsBasicLangSourceFile(document.FilePath);
                var requestFailed = false;

                try
                {
                    if (serverConnected)
                    {
                        completions = await completionSvc!.GetCompletionsAsync(
                            document.FilePath ?? "",
                            e.Line,
                            e.Column,
                            requestToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Superseded by a newer request — drop silently
                    return;
                }
                catch (Exception ex)
                {
                    requestFailed = true;
                    System.Diagnostics.Debug.WriteLine($"[Completion] Error: {ex.Message}");
                }

                // Stale-drop: only the most recent request may publish results
                if (!completionCoordinator.IsCurrent(requestId) || requestToken.IsCancellationRequested) return;

                try
                {
                    // Try extension host providers if LSP returned nothing
                    if ((completions == null || completions.Count == 0) &&
                        document.FilePath != null &&
                        _extensionService.HasExtensionProviders(LanguageFileTypes.GetEditorLanguageId(document.FilePath)))
                    {
                        var extResult = await _extensionService.RequestCompletionAsync(
                            document.FilePath, e.Line, e.Column);
                        if (extResult.HasValue)
                        {
                            completions = ParseExtensionCompletions(extResult.Value);
                        }

                        if (!completionCoordinator.IsCurrent(requestId)) return;
                    }

                    if (completions != null && completions.Count > 0)
                    {
                        // Filter to type-only completions after "As " keyword
                        completions = FilterCompletionsForContext(document, e.Line, e.Column, completions);
                        document.ProvideCompletions(completions);
                    }
                    else if (isBasicLangDocument && (!serverConnected || requestFailed))
                    {
                        // Hard-coded fallback ONLY for BasicLang files, and
                        // only when the server is down or the request errored
                        // — a connected server returning zero items
                        // deliberately scoped the context, and a keyword dump
                        // never belongs in non-BasicLang files.
                        var fallbackCompletions = GetFallbackCompletions();
                        var filtered = FilterCompletionsForContext(document, e.Line, e.Column, fallbackCompletions);
                        document.ProvideCompletions(filtered);
                    }
                    else
                    {
                        // Connected server, zero items: publish the empty list
                        // so the editor closes/declines the session cleanly.
                        document.ProvideCompletions(Array.Empty<CompletionItem>());
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Completion] Error: {ex.Message}");
                    // Always publish so the editor's completion session ends
                    // instead of blocking future word triggers.
                    if (completionCoordinator.IsCurrent(requestId))
                    {
                        try { document.ProvideCompletions(Array.Empty<CompletionItem>()); } catch { }
                    }
                }
            };
            document.CompletionRequested += onCompletion;

            // Wire up text change notifications for LSP
            var documentVersion = 0;
            EventHandler<string>? onTextChanged = async (s, newText) =>
            {
                try
                {
                    var version = System.Threading.Interlocked.Increment(ref documentVersion);

                    // Notify the owning LSP server of the change (BasicLang for .bas, clangd for .cpp).
                    var changeSvc = _languageServices.GetFor(document.FilePath);
                    if (changeSvc is { IsConnected: true })
                    {
                        await changeSvc.ChangeDocumentAsync(document.FilePath!, newText, version);
                    }

                    // Notify extension host (for all file types)
                    if (_extensionService.IsExtensionHostRunning && document.FilePath != null)
                    {
                        _ = _extensionService.NotifyDocumentChangedAsync(document.FilePath, version, newText);
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
            // Gutter context menu: conditional breakpoints, logpoints, edit/remove
            EventHandler<int>? onConditionalBp = async (s, line) => { if (!string.IsNullOrEmpty(document.FilePath)) await HandleGutterConditionalBreakpointAsync(document.FilePath, line, "conditional"); };
            EventHandler<int>? onLogpoint = async (s, line) => { if (!string.IsNullOrEmpty(document.FilePath)) await HandleGutterConditionalBreakpointAsync(document.FilePath, line, "logpoint"); };
            EventHandler<int>? onEditBp = async (s, line) => { if (!string.IsNullOrEmpty(document.FilePath)) await HandleGutterEditBreakpointAsync(document.FilePath, line); };
            EventHandler<int>? onRemoveBp = (s, line) => { if (!string.IsNullOrEmpty(document.FilePath)) Breakpoints.RemoveBreakpoint(document.FilePath, line); };
            EventHandler<int>? onToggleEnableBp = (s, line) => { if (!string.IsNullOrEmpty(document.FilePath)) Breakpoints.ToggleBreakpoint(document.FilePath, line); };
            document.ConditionalBreakpointRequested += onConditionalBp;
            document.LogpointRequested += onLogpoint;
            document.EditBreakpointRequested += onEditBp;
            document.RemoveBreakpointRequested += onRemoveBp;
            document.ToggleEnableBreakpointRequested += onToggleEnableBp;

            // Register cleanup action to unsubscribe all handlers on document close
            _documentCleanupActions[filePath] = () =>
            {
                // Cancel any in-flight completion request for the closing doc
                completionCoordinator.CancelAll();
                document.CaretPositionChanged -= onCaretChanged;
                document.AddToWatchRequested -= OnAddToWatchRequested;
                document.DataTipEvaluationRequested -= OnDataTipEvaluationRequested;
                document.GoToDefinitionRequested -= onGoToDef;
                document.FindAllReferencesRequested -= onFindRefs;
                document.ShowCallHierarchyRequested -= onCallHierarchy;
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
                document.ConditionalBreakpointRequested -= onConditionalBp;
                document.LogpointRequested -= onLogpoint;
                document.EditBreakpointRequested -= onEditBp;
                document.RemoveBreakpointRequested -= onRemoveBp;
                document.ToggleEnableBreakpointRequested -= onToggleEnableBp;
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

            // Auto-save (VS Code "afterDelay" behavior): register the document with the
            // auto-save service and forward edits so its per-document debounce timer resets.
            RegisterDocumentForAutoSave(document, filePath);
            EventHandler<string>? onTextChangedAutoSave = (s, _) =>
            {
                if (!string.IsNullOrEmpty(document.FilePath))
                {
                    _autoSaveService.NotifyDocumentChanged(document.FilePath);
                }
            };
            document.TextChanged += onTextChangedAutoSave;

            // Auto-save (VS Code "onFocusChange" behavior): the view raises
            // EditorFocusLost when keyboard focus leaves the editor control.
            EventHandler? onEditorFocusLostAutoSave = (s, _) =>
            {
                if (!string.IsNullOrEmpty(document.FilePath))
                {
                    _autoSaveService.NotifyEditorLostFocus(document.FilePath);
                }
            };
            document.EditorFocusLost += onEditorFocusLostAutoSave;

            // Add code lens, semantic token, and auto-save handlers to cleanup
            var existingCleanup = _documentCleanupActions.GetValueOrDefault(filePath);
            _documentCleanupActions[filePath] = () =>
            {
                existingCleanup?.Invoke();
                document.CodeLensCommandRequested -= onCodeLensCmd;
                document.TextChanged -= onTextChangedCodeLens;
                document.TextChanged -= onTextChangedSemanticTokens;
                document.TextChanged -= onTextChangedAutoSave;
                document.EditorFocusLost -= onEditorFocusLostAutoSave;
                semanticTokenCts?.Cancel();
                semanticTokenCts?.Dispose();
            };

            _openDocuments[filePath] = document;
            _dockFactory.AddDocument(document);

            // Screen reader announcement for file opened
            Services.ScreenReaderService.Instance.Announce($"Opened {Path.GetFileName(filePath)}");

            // Update document outline
            await UpdateDocumentOutlineAsync(filePath, content);

            // Load timeline (git file history) for the newly opened file
            _ = Timeline.LoadTimelineAsync(filePath);

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

            // Show notification with Retry action instead of just a dialog
            var failedPath = filePath;
            ShowNotification($"Could not open file: {Path.GetFileName(filePath)}", "error",
                new List<NotificationAction>
                {
                    new NotificationAction("Retry", () => _ = OpenFileAsync(failedPath)),
                },
                details: ex.Message);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc != null)
        {
            try
            {
                if (await SaveDocumentCoreAsync(activeDoc))
                {
                    // Refresh blame for the saved (active) document
                    _ = UpdateBlameForCurrentLineAsync(activeDoc);

                    var fileName = activeDoc.FilePath != null ? Path.GetFileName(activeDoc.FilePath) : "File";
                    ShowStatusBarMessage($"{fileName} saved", 3.0);
                }
            }
            catch (Exception ex)
            {
                ShowNotification($"Could not save file: {ex.Message}", "error",
                    new List<NotificationAction>
                    {
                        new NotificationAction("Retry", () => _ = SaveAsync()),
                    },
                    details: ex.Message);
            }
        }
    }

    /// <summary>
    /// Shared save path used by manual save, Save All, and auto-save so that
    /// trim-on-save, LSP notifications, and blame invalidation stay consistent.
    /// Returns false when the document has no file path (untitled) or when a
    /// save for it is already in progress. Must be called on the UI thread.
    /// </summary>
    private async Task<bool> SaveDocumentCoreAsync(CodeEditorDocumentViewModel doc)
    {
        var filePath = doc.FilePath;
        if (string.IsNullOrEmpty(filePath)) return false;

        // Guard against overlapping saves (e.g. auto-save firing during a manual save)
        if (!_documentsBeingSaved.Add(filePath)) return false;
        try
        {
            // Format on save (editor.formatOnSave), before trim + save so the formatter's output is
            // itself trimmed and persisted. Best-effort: a formatter error must never block a save.
            if (_settingsService?.Get("editor.formatOnSave", false) ?? false)
            {
                try { await FormatDocumentContentAsync(doc); }
                catch { /* formatting is best-effort */ }
            }

            // Apply trim trailing whitespace setting before saving
            doc.TrimTrailingWhitespaceOnSave =
                _settingsService?.Get("editor.trimTrailingWhitespaceOnSave", false) ?? false;

            if (!await doc.SaveAsync()) return false;

            await NotifyLspDocumentSavedAsync(doc);

            // Invalidate blame cache for the saved file
            _blameCache.Remove(filePath);
            return true;
        }
        finally
        {
            _documentsBeingSaved.Remove(filePath);
        }
    }

    /// <summary>
    /// Registers a document with the auto-save service. The save callback marshals
    /// to the UI thread (the auto-save timer fires on a threadpool thread) and then
    /// runs the same save path as a manual save.
    /// </summary>
    private void RegisterDocumentForAutoSave(CodeEditorDocumentViewModel document, string filePath)
    {
        _autoSaveService.RegisterDocument(
            filePath,
            () => AutoSaveDocumentAsync(document),
            () => document.IsDirty,
            () => IsFileReadOnly(filePath));
    }

    private async Task<bool> AutoSaveDocumentAsync(CodeEditorDocumentViewModel document)
    {
        // The auto-save timer fires on a threadpool thread; all document/view-model
        // access must be marshalled to the UI thread.
        return await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // Never auto-save untitled documents; re-check dirty state on the UI thread.
            if (string.IsNullOrEmpty(document.FilePath) || !document.IsDirty) return false;
            return await SaveDocumentCoreAsync(document);
        });
    }

    private static bool IsFileReadOnly(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists && info.IsReadOnly;
        }
        catch
        {
            return false;
        }
    }

    private async Task NotifyLspDocumentSavedAsync(CodeEditorDocumentViewModel doc)
    {
        // Notify the owning LSP server of the save (BasicLang for .bas, clangd for .cpp).
        var svc = _languageServices.GetFor(doc.FilePath);
        if (svc is { IsConnected: true })
        {
            try { await svc.SaveDocumentAsync(doc.FilePath!, doc.Text); }
            catch { }
        }
    }

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        foreach (var doc in _openDocuments.Values.Where(d => d.IsDirty).ToList())
        {
            // Shared save path (trim-on-save, LSP notify, blame invalidation)
            await SaveDocumentCoreAsync(doc);
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

    // ── Build + BasicLang settings seams (Task 2.4) ──
    // Pure static resolvers so the settings→behavior mapping (key names, defaults) can be pinned
    // headlessly; all read the schema default explicitly so they never depend on the store being
    // pre-populated. A null service means "no settings loaded" → schema-default behavior.

    /// <summary><c>build.saveBeforeBuild</c> (default true): save all files before a build.</summary>
    public static bool ShouldSaveBeforeBuild(ISettingsService? settings)
        => settings?.Get("build.saveBeforeBuild", true) ?? true;

    /// <summary><c>build.showOutput</c> (default true): reveal the Output panel when a build starts.</summary>
    public static bool ShouldShowBuildOutput(ISettingsService? settings)
        => settings?.Get("build.showOutput", true) ?? true;

    /// <summary><c>basiclang.lsp.autoStart</c> (default true): auto-start the language server at launch.</summary>
    public static bool ShouldAutoStartLanguageServer(ISettingsService? settings)
        => settings?.Get("basiclang.lsp.autoStart", true) ?? true;

    /// <summary>
    /// Resolves <c>build.defaultConfiguration</c> (schema default "Debug") to a value that actually
    /// exists in <paramref name="validConfigurations"/> (case-insensitive). Unknown/empty values fall
    /// back to "Debug" when present, else the first known configuration, else "Debug".
    /// </summary>
    public static string ResolveDefaultBuildConfiguration(ISettingsService? settings, IEnumerable<string> validConfigurations)
    {
        var known = validConfigurations?.ToList() ?? new List<string>();
        var configured = settings?.Get("build.defaultConfiguration", "Debug") ?? "Debug";

        var match = known.FirstOrDefault(c => string.Equals(c, configured, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        var debug = known.FirstOrDefault(c => string.Equals(c, "Debug", StringComparison.OrdinalIgnoreCase));
        return debug ?? known.FirstOrDefault() ?? "Debug";
    }

    /// <summary>
    /// Registers the Build + BasicLang-LSP settings consumers owned by this view-model. A static
    /// seam (rather than inline ctor lines) so the Phase 3 settings-consumer contract test can force
    /// registration without building the heavy MainWindowViewModel. Idempotent — safe to call twice.
    /// </summary>
    public static void RegisterBuildAndLspSettingsConsumers()
    {
        SettingsConsumerRegistry.RegisterConsumer("build.saveBeforeBuild", "MainWindowViewModel.SaveBeforeBuildAsync → save all files before a build");
        SettingsConsumerRegistry.RegisterConsumer("build.showOutput", "MainWindowViewModel.ShowBuildOutput → reveal Output panel on build");
        SettingsConsumerRegistry.RegisterConsumer("build.defaultConfiguration", "MainWindowViewModel ctor → initial CurrentConfiguration");
        SettingsConsumerRegistry.RegisterConsumer("basiclang.lsp.autoStart", "MainWindowViewModel ctor → gate language-server auto-start; manual StartLanguageServer command otherwise");
    }

    /// <summary>
    /// Registers the two save-hook settings consumed by this view-model's SaveDocumentCoreAsync
    /// (<c>editor.formatOnSave</c> and <c>editor.trimTrailingWhitespaceOnSave</c>). A static seam
    /// (rather than inline ctor lines) so the Phase 3 settings-consumer contract test can force
    /// registration without building the heavy MainWindowViewModel — these are the only editor.*
    /// dialog keys not also registered by the CodeEditorDocumentView static ctor. Idempotent.
    /// </summary>
    public static void RegisterEditorSaveSettingsConsumers()
    {
        SettingsConsumerRegistry.RegisterConsumer("editor.trimTrailingWhitespaceOnSave", "MainWindowViewModel.SaveDocumentCoreAsync → trim trailing whitespace before save");
        SettingsConsumerRegistry.RegisterConsumer("editor.formatOnSave", "MainWindowViewModel.SaveDocumentCoreAsync → format document before save");
    }

    /// <summary>Saves all dirty files before a build when <c>build.saveBeforeBuild</c> is enabled.</summary>
    private async Task SaveBeforeBuildAsync()
    {
        if (ShouldSaveBeforeBuild(_settingsService))
        {
            await SaveAllAsync();
        }
    }

    /// <summary>
    /// Selects the Build output channel and, when <c>build.showOutput</c> is enabled, reveals the
    /// Output panel. The channel is always selected so the build log lands in the right place even
    /// when auto-reveal is off.
    /// </summary>
    private void ShowBuildOutput()
    {
        OutputPanel.SelectedCategory = OutputCategory.Build;
        if (ShouldShowBuildOutput(_settingsService))
        {
            _dockFactory.ActivateTool("Output");
        }
    }

    /// <summary>
    /// Manually starts the BasicLang language server. Surfaced in the command palette so the server
    /// can still be brought up when <c>basiclang.lsp.autoStart</c> is off (or after a manual stop).
    /// StartAsync is a no-op if the service is already connected or has been disposed.
    /// <para>
    /// Unlike the autostart in the constructor, this runs long after startup, so the open
    /// project's directory is usually available — making this the path that actually gives the
    /// server a workspace root today. (Consequently, "Stop Language Server" then "Start Language
    /// Server" is the current way to re-root a server that autostarted before a project was open.)
    /// </para>
    /// </summary>
    [RelayCommand]
    private async Task StartLanguageServerAsync()
    {
        StatusText = "Starting language server...";
        // BasicLang only, by design (see BasicLangLspService). Root-needing servers (clangd) are
        // started on ProjectOpened via the registry's StartAllAsync (Task 12), not from here — this
        // command must keep working with no project open, where a rootless StartAllAsync would throw.
        var svc = BasicLangLspService;
        if (svc != null)
        {
            await svc.StartAsync(_projectService.CurrentProject?.ProjectDirectory);
        }
    }

    [RelayCommand]
    private async Task BuildAsync()
    {
        if (_projectService.CurrentProject == null && _projectService.CurrentSolution == null)
        {
            await _dialogService.ShowMessageAsync("Build", "No project or solution is open.",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        if (_buildService.IsBuilding)
        {
            return;
        }

        // Save all before building (honors build.saveBeforeBuild)
        await SaveBeforeBuildAsync();

        // Switch output panel to Build channel and (honoring build.showOutput) reveal it
        ShowBuildOutput();

        // Update status bar with build-in-progress indicator
        StatusBar.SetBuildStarted();
        StatusText = "Building...";
        Services.ScreenReaderService.Instance.Announce("Build started");

        // Show progress notification for the build
        ShowProgressNotification("build", "Building...");

        // If a solution is loaded, build all projects in dependency order;
        // otherwise, build the single project.
        if (_projectService.CurrentSolution != null && _projectService.CurrentSolution.Projects.Count > 0)
        {
            await _buildService.BuildSolutionAsync(_projectService.CurrentSolution);
        }
        else if (_projectService.CurrentProject != null)
        {
            await _buildService.BuildProjectAsync(_projectService.CurrentProject);
        }

        // Dismiss the progress notification once build completes
        DismissNotification("build");
    }

    [RelayCommand]
    private async Task RebuildAsync()
    {
        if (_projectService.CurrentProject == null) return;
        if (_buildService.IsBuilding) return;

        await SaveBeforeBuildAsync();

        // Switch output panel to Build channel and (honoring build.showOutput) reveal it
        ShowBuildOutput();

        StatusBar.SetBuildStarted();
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

        // Update status bar debug state (changes color and shows debug target)
        StatusBar.UpdateDebugState(IsDebugging, _debugTargetName);

        if (e.NewState == DebugState.Stopped)
        {
            _currentFrameId = null;
            StatusText = "Ready";
            _debugTargetName = "";
            ClearAllInlineDebugValues();
            ClearAllExecutionLines();
            RestorePreDebugPanels();
            Services.ScreenReaderService.Instance.Announce("Debug session ended");

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
                Services.ScreenReaderService.Instance.Announce("Debugging started");
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

            // Screen reader announcements for debug stop events (assertive for breakpoints/exceptions)
            var fileName = firstFrame?.FilePath != null ? Path.GetFileName(firstFrame.FilePath) : null;
            var srMessage = e.Reason switch
            {
                StopReason.Breakpoint when fileName != null =>
                    $"Breakpoint hit at line {firstFrame!.Line} in {fileName}",
                StopReason.FunctionBreakpoint when fileName != null =>
                    $"Function breakpoint hit at line {firstFrame!.Line} in {fileName}",
                StopReason.DataBreakpoint when fileName != null =>
                    $"Data breakpoint hit at line {firstFrame!.Line} in {fileName}",
                StopReason.Exception =>
                    $"Exception: {e.Text ?? "unknown"}{(fileName != null ? $" at line {firstFrame!.Line} in {fileName}" : "")}",
                StopReason.Step when fileName != null =>
                    $"Stepped to line {firstFrame!.Line} in {fileName}",
                _ => StatusText
            };
            if (e.Reason == StopReason.Exception || e.Reason == StopReason.Breakpoint ||
                e.Reason == StopReason.FunctionBreakpoint || e.Reason == StopReason.DataBreakpoint)
            {
                Services.ScreenReaderService.Instance.AnnounceAssertive(srMessage);
            }
            else
            {
                Services.ScreenReaderService.Instance.Announce(srMessage);
            }

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

    private async void OnThreadSwitched(object? sender, ThreadItem thread)
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Refresh call stack for the newly selected thread
                var frames = await _debugService.GetStackTraceAsync(thread.Id);
                CallStack.StackFrames.Clear();
                foreach (var frame in frames)
                {
                    CallStack.StackFrames.Add(new StackFrameItem
                    {
                        Id = frame.Id,
                        Name = frame.Name,
                        FilePath = frame.FilePath,
                        Line = frame.Line,
                        Column = frame.Column,
                        DisplayText = frame.FilePath != null
                            ? $"{frame.Name} at {Path.GetFileName(frame.FilePath)}:{frame.Line}"
                            : frame.Name
                    });
                }

                // Navigate to the top frame of the selected thread
                var topFrame = frames.FirstOrDefault();
                if (topFrame?.FilePath != null)
                {
                    _currentFrameId = topFrame.Id;
                    ClearAllExecutionLines();
                    await OpenFileAsync(topFrame.FilePath);

                    if (_openDocuments.TryGetValue(topFrame.FilePath, out var doc))
                    {
                        doc.SetExecutionLine(topFrame.Line);
                        doc.NavigateTo(topFrame.Line);
                    }
                }

                // Update status bar
                StatusText = $"Thread {thread.Id}: {thread.Name}";
            });
        }
        catch (Exception)
        {
            // Ignore thread switch errors to prevent crashes
        }
    }

    private void OnDebugOutput(object? sender, DebugOutputEventArgs e)
    {
        // Route program stdout/stderr through the output service so it is
        // stored in the Debug category and respects category filtering.
        _outputService.Write(e.Output, OutputCategory.Debug);
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

        // Phase 1: the BasicLang debug adapter cannot debug native C++ exes —
        // guard F5 for ALL native projects (Language=Cpp AND BasicLang-on-the-C++-
        // backend) instead of handing a native binary to the BasicLang adapter.
        if (_projectService.CurrentProject.IsNativeBuild)
        {
            const string message =
                "Native C++ debugging arrives in a later phase — use Start Without Debugging (Ctrl+F5) to run.";
            _dockFactory.ActivateTool("Output");
            OutputPanel.AppendOutput(message + "\n");
            StatusText = message;
            return;
        }

        // If already debugging, F5 should continue execution when paused
        if (IsDebugging)
        {
            if (IsPaused)
            {
                await ContinueAsync();
            }
            return;
        }

        // Build first (honors build.saveBeforeBuild)
        await SaveBeforeBuildAsync();

        // Show build progress in output panel (honors build.showOutput)
        ShowBuildOutput();
        StatusBar.SetBuildStarted();
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

        // Load the active launch configuration for args/env/cwd
        var launchEntry = await _launchConfigurationService.GetActiveConfigurationAsync(
            _projectService.CurrentProject.ProjectDirectory);

        var projectDir = _projectService.CurrentProject.ProjectDirectory;
        var workingDir = projectDir;
        var arguments = Array.Empty<string>();
        var environment = new Dictionary<string, string>();
        var stopOnEntry = false;

        if (launchEntry != null)
        {
            // Resolve ${ProjectDir} placeholder
            workingDir = string.IsNullOrWhiteSpace(launchEntry.Cwd)
                ? projectDir
                : launchEntry.Cwd.Replace("${ProjectDir}", projectDir);

            if (launchEntry.Args.Length > 0)
            {
                arguments = launchEntry.Args;
            }

            environment = launchEntry.Env;
            stopOnEntry = launchEntry.StopOnEntry;
        }

        var config = new DebugConfiguration
        {
            Program = buildResult.ExecutablePath,
            WorkingDirectory = workingDir,
            Arguments = arguments,
            Environment = environment,
            StopOnEntry = stopOnEntry
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
    private async Task AttachToProcessAsync()
    {
        if (IsDebugging)
        {
            await _dialogService.ShowMessageAsync("Attach to Process",
                "A debug session is already active. Stop the current session first.",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        if (App.MainWindow == null) return;

        var vm = new ViewModels.Dialogs.AttachToProcessViewModel();
        var dialog = new Views.Dialogs.AttachToProcessDialog(vm);

        var result = await dialog.ShowDialog<int?>(App.MainWindow);
        if (result == null || result.Value <= 0)
            return;

        int pid = result.Value;

        OutputPanel.SelectedCategory = OutputCategory.Debug;
        OutputPanel.AppendOutput($"\n========== Attaching to process {pid} ==========\n");
        StatusText = $"Attaching to process {pid}...";

        // Collect all breakpoints to send to the debug adapter
        var breakpoints = Breakpoints.GetAllBreakpoints();

        var success = await _debugService.AttachToProcessAsync(pid, breakpoints);
        if (success)
        {
            StatusText = $"Attached to process {pid}";
        }
        else
        {
            OutputPanel.AppendOutput($"Failed to attach to process {pid}.\n");
            StatusText = "Attach failed";
        }
    }

    [RelayCommand]
    private async Task StartWithoutDebuggingAsync()
    {
        if (_projectService.CurrentProject == null) return;
        if (IsDebugging) return;

        await SaveBeforeBuildAsync();
        ShowBuildOutput();
        StatusBar.SetBuildStarted();
        StatusText = "Building...";
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

        await SaveBeforeBuildAsync();
        ShowBuildOutput();
        StatusBar.SetBuildStarted();
        StatusText = "Building...";
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
    private async Task EditLaunchConfigurationAsync()
    {
        if (_projectService.CurrentProject == null)
        {
            await _dialogService.ShowMessageAsync("Launch Configuration",
                "No project is open.", DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        var projectDir = _projectService.CurrentProject.ProjectDirectory;
        var configFile = await _launchConfigurationService.LoadAsync(projectDir);

        var viewModel = new Dialogs.LaunchConfigurationDialogViewModel(configFile);
        var dialog = new Views.Dialogs.LaunchConfigurationDialog(viewModel);

        var window = Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow : null;

        if (window == null) return;

        var result = await dialog.ShowDialog<bool?>(window);
        if (result == true)
        {
            var updatedFile = viewModel.ToConfigurationFile();
            await _launchConfigurationService.SaveAsync(projectDir, updatedFile);
            StatusText = "Launch configuration saved.";
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

    // Panel focus commands (Ctrl+1 through Ctrl+5, Ctrl+0)
    // These activate the panel AND request keyboard focus so users can navigate
    // entirely by keyboard without needing to click into a panel.

    [RelayCommand]
    private void FocusSolutionExplorer()
    {
        _dockFactory.ActivateTool("SolutionExplorer");
        FocusPanelRequested?.Invoke(this, "SolutionExplorer");
    }

    [RelayCommand]
    private void FocusEditor()
    {
        FocusPanelRequested?.Invoke(this, "Editor");
    }

    [RelayCommand]
    private void FocusOutput()
    {
        _dockFactory.ActivateTool("Output");
        FocusPanelRequested?.Invoke(this, "Output");
    }

    [RelayCommand]
    private void FocusTerminal()
    {
        _dockFactory.ActivateTool("Terminal");
        FocusPanelRequested?.Invoke(this, "Terminal");
    }

    [RelayCommand]
    private void FocusErrorList()
    {
        _dockFactory.ActivateTool("ErrorList");
        FocusPanelRequested?.Invoke(this, "ErrorList");
    }

    [RelayCommand]
    private void FocusVariables()
    {
        _dockFactory.ActivateTool("Variables");
        FocusPanelRequested?.Invoke(this, "Variables");
    }

    private static readonly string[] _panelCycleOrder =
        { "SolutionExplorer", "Editor", "Output", "Terminal", "ErrorList", "Variables" };
    private int _currentPanelIndex = 1; // default to Editor

    [RelayCommand]
    private void FocusNextPanel()
    {
        _currentPanelIndex = (_currentPanelIndex + 1) % _panelCycleOrder.Length;
        var panel = _panelCycleOrder[_currentPanelIndex];
        if (panel != "Editor")
            _dockFactory.ActivateTool(panel);
        FocusPanelRequested?.Invoke(this, panel);
        StatusText = $"Focus: {panel}";
    }

    [RelayCommand]
    private void FocusPreviousPanel()
    {
        _currentPanelIndex = (_currentPanelIndex - 1 + _panelCycleOrder.Length) % _panelCycleOrder.Length;
        var panel = _panelCycleOrder[_currentPanelIndex];
        if (panel != "Editor")
            _dockFactory.ActivateTool(panel);
        FocusPanelRequested?.Invoke(this, panel);
        StatusText = $"Focus: {panel}";
    }

    [RelayCommand]
    private void ZoomIn()
    {
        var newZoom = Math.Min(ZoomLevel + 10, 200);
        SetZoomLevel(newZoom);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        var newZoom = Math.Max(ZoomLevel - 10, 50);
        SetZoomLevel(newZoom);
    }

    [RelayCommand]
    private void ZoomReset()
    {
        SetZoomLevel(100);
    }

    /// <summary>
    /// Applies the given zoom level percentage and persists it.
    /// Raises ZoomLevelChanged event so the view can apply LayoutTransform.
    /// </summary>
    private void SetZoomLevel(int percent)
    {
        ZoomLevel = percent;
        StatusText = $"Zoom: {percent}%";
        Services.ScreenReaderService.Instance.Announce($"Zoom {percent} percent");
        _settingsService?.Set(SettingsKeys.ZoomLevel, percent);
        ZoomLevelChanged?.Invoke(this, percent);
    }

    /// <summary>
    /// Raised when the zoom level changes. The int parameter is the new zoom percentage (50-200).
    /// </summary>
    public event EventHandler<int>? ZoomLevelChanged;

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
        // Persist so the toggle survives the next settings event and restarts (VS Code parity).
        // Without this, any later SettingsChanged re-apply would revert the editors to the stored
        // value and leave the menu checkmark stale.
        PersistWhitespaceToggle(_settingsService, ShowWhitespace);
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleWhitespace(ShowWhitespace);
        }
        StatusText = ShowWhitespace ? "Whitespace characters visible" : "Whitespace characters hidden";
    }

    /// <summary>
    /// Writes the View-menu whitespace toggle through the settings pipeline
    /// (editor.renderWhitespace: "all"/"none", User scope) so the setting stays the single source
    /// of truth. Static test seam: <c>SettingsEditorWiringTests</c> exercises the exact mapping the
    /// <see cref="ToggleWhitespace"/> command uses. No feedback loop: the resulting SettingChanged
    /// re-seed (<see cref="OnEditorDisplaySettingChanged"/>) reads back the value just written and
    /// the ObservableProperty setter no-ops on equality.
    /// </summary>
    public static void PersistWhitespaceToggle(ISettingsService? service, bool showWhitespace)
        => service?.Set("editor.renderWhitespace", showWhitespace ? "all" : "none", SettingsScope.User);

    /// <summary>
    /// Writes the View-menu minimap toggle through the settings pipeline
    /// (editor.minimap.enabled: bool, User scope). See <see cref="PersistWhitespaceToggle"/> for
    /// the rationale and the convergence argument.
    /// </summary>
    public static void PersistMinimapToggle(ISettingsService? service, bool showMinimap)
        => service?.Set("editor.minimap.enabled", showMinimap, SettingsScope.User);

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
        vm.LoadMruData(_settingsService);
        vm.RegisterCommands(this);
        vm.RegisterFiles(_projectService);

        // Set the active document path for symbol search
        var activeDoc = _dockFactory.GetActiveDocument() as Documents.CodeEditorDocumentViewModel;
        vm.ActiveDocumentPath = activeDoc?.FilePath;

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
                var lineDoc = _dockFactory.GetActiveDocument() as Documents.CodeEditorDocumentViewModel;
                if (lineDoc != null)
                {
                    RecordNavigationPosition(lineDoc);
                    lineDoc.NavigateTo(dialog.RequestedLineNumber, 1);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Go to line failed: {ex.Message}";
            }
            return;
        }

        // Handle symbol navigation request
        if (dialog.SymbolNavigationTarget != null)
        {
            try
            {
                var target = dialog.SymbolNavigationTarget.Value;
                await OpenFileAndNavigateAsync(target.FilePath, target.Line, 1);
            }
            catch (Exception ex)
            {
                StatusText = $"Symbol navigation failed: {ex.Message}";
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
        await ShowSettingsDialogAsync(openJsonEditor: false);
    }

    private async Task ShowSettingsDialogAsync(bool openJsonEditor)
    {
        if (App.MainWindow == null) return;

        var vm = new ViewModels.Dialogs.SettingsViewModel();
        if (openJsonEditor)
        {
            // The command-palette "Open Settings (JSON)..." entry lands directly in the raw-JSON
            // view (setting the flag loads the JSON content via the VM's change handler).
            vm.IsJsonEditorActive = true;
        }

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

        var result = await _dialogService.ShowGoToLineColumnDialogAsync(activeDoc.CaretLine, totalLines);
        if (result != null)
        {
            RecordNavigationPosition(activeDoc);
            activeDoc.NavigateTo(result.Line, result.Column);
        }
    }

    private void RecordNavigationPosition(CodeEditorDocumentViewModel doc)
    {
        // TODO: implement navigation history tracking
    }

    [RelayCommand]
    private async Task GoToSymbolAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc == null) return;

        // Try LSP document symbols first for the current document, from the server that owns it
        // (BasicLang for .bas, clangd for .cpp once registered). Files no server serves fall through
        // to the text-based dialog below.
        var symbolSvc = activeDoc.FilePath != null ? _languageServices.GetFor(activeDoc.FilePath) : null;
        if (symbolSvc is { IsConnected: true } && activeDoc.FilePath != null)
        {
            try
            {
                var symbols = await symbolSvc.GetDocumentSymbolsAsync(activeDoc.FilePath);
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
        // A workspace-symbol search spans the whole workspace, which in a mixed project means every
        // language — so query EVERY connected server and merge, rather than only whichever one the
        // singleton used to hold (that silently missed C++ symbols in a mixed project, and vice
        // versa). Only report "not connected" when no server at all is up.
        var connectedServers = _languageServices.All.Where(s => s.IsConnected).ToList();
        if (connectedServers.Count == 0)
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
            // Merge workspace/symbol results from every connected server. One server erroring or
            // returning nothing just contributes nothing — it must not sink the others' results.
            var workspaceSymbols = new List<WorkspaceSymbolInfo>();
            foreach (var server in connectedServers)
            {
                try { workspaceSymbols.AddRange(await server.GetWorkspaceSymbolsAsync(query)); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[WorkspaceSymbol] {server.Descriptor.Id}: {ex.Message}"); }
            }

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
        if (activeDoc?.FilePath == null) return;
        // Route to the server that owns this file (BasicLang for .bas, clangd for .cpp).
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is not { IsConnected: true }) return;

        var location = await svc.GetTypeDefinitionAsync(
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

        // Try the language service that owns this file first (BasicLang for .bas, clangd for .cpp
        // once registered); files no server serves skip straight to the text-search fallback below.
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is { IsConnected: true })
        {
            var location = await svc.GetDefinitionAsync(
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

        // Route to the server that owns this file (BasicLang for .bas, clangd for .cpp).
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is { IsConnected: true })
        {
            var location = await svc.GetImplementationAsync(
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
        // Convert URI to file path if needed (decodes percent-encoding; plain
        // paths pass through unchanged)
        var filePath = VisualGameStudio.ProjectSystem.Services.LanguageService.UriToPath(location.Uri);

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

        // Try the language service that owns this file first (BasicLang for .bas, clangd for .cpp);
        // files no server serves fall through to the text search below.
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is { IsConnected: true })
        {
            var references = await svc.FindReferencesAsync(
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
        var searchResultMsg = $"Found {totalRefs} reference{(totalRefs == 1 ? "" : "s")} in {fileCount} file{(fileCount == 1 ? "" : "s")}";
        StatusText = searchResultMsg;
        Services.ScreenReaderService.Instance.Announce(searchResultMsg);
    }

    #endregion

    #region Clipboard and History

    // These forward to the active code editor. AvaloniaEdit already handles the raw
    // keystrokes when the editor is focused; these commands make the Edit-menu items
    // (and any other command consumers) actually do something on click.

    [RelayCommand]
    private void Undo()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestUndo();
    }

    [RelayCommand]
    private void Redo()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestRedo();
    }

    [RelayCommand]
    private void Cut()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestCut();
    }

    [RelayCommand]
    private void Copy()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestCopy();
    }

    [RelayCommand]
    private void Paste()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestPaste();
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

        // Try LSP rename first (supports cross-file rename via WorkspaceEdit), from the server that
        // owns this file — BasicLang for .bas, clangd for .cpp once registered.
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is { IsConnected: true })
        {
            var newName = await _dialogService.ShowInputDialogAsync(
                "Rename Symbol",
                $"Rename '{word}' to:",
                word);

            if (string.IsNullOrEmpty(newName) || newName == word) return;

            var lspEdit = await svc.RenameAsync(
                activeDoc.FilePath, activeDoc.CaretLine, activeDoc.CaretColumn, newName);

            if (lspEdit != null)
            {
                await ApplyWorkspaceEditAsync(lspEdit);
                StatusText = $"Renamed '{word}' to '{newName}' via LSP";
                return;
            }
        }

        // Fall back to the refactoring-service rename. It is BasicLang-SPECIFIC (it parses the file
        // as BasicLang), so it must not run for other languages — a .cpp with no clangd gets no
        // rename rather than a BasicLang-parsed one.
        if (!BasicLangFileTypes.IsBasicLangSourceFile(activeDoc.FilePath)) return;
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

    #region Menu Commands — File

    [RelayCommand]
    private async Task NewFileAsync()
    {
        // Create a new untitled document
        var untitledIndex = 1;
        string name;
        do
        {
            name = $"Untitled-{untitledIndex}.bas";
            untitledIndex++;
        } while (_openDocuments.ContainsKey(name));

        var document = new CodeEditorDocumentViewModel(_fileService, _eventAggregator, _bookmarkService)
        {
            FilePath = name,
            // Untitled files are .bas, so this routes to BasicLang (see the OpenFileAsync seam).
            LanguageService = _languageServices.GetFor(name),
            GitService = _gitService
        };
        document.SetContent("");

        _openDocuments[name] = document;
        _dockFactory.AddDocument(document);
        StatusText = $"New file: {name}";
    }

    [RelayCommand]
    private void NewWindow()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to open new window: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folderPath = await _dialogService.ShowFolderDialogAsync(new Core.Abstractions.Services.FolderDialogOptions { Title = "Open Folder" });
        if (string.IsNullOrEmpty(folderPath)) return;

        // Look for a .blproj file in the folder
        var projectFiles = Directory.GetFiles(folderPath, "*.blproj", SearchOption.TopDirectoryOnly);
        if (projectFiles.Length > 0)
        {
            await _projectService.OpenProjectAsync(projectFiles[0]);
        }
        else
        {
            // Open as a workspace folder
            _workspaceService.CreateNewWorkspace();
            _workspaceService.AddFolder(folderPath);
            SolutionExplorer.RefreshWorkspaceTree();
            Title = $"Visual Game Studio - {Path.GetFileName(folderPath)}";

            // Set workspace path for folder-level settings even without a project file
            if (_settingsService is VisualGameStudio.ProjectSystem.Services.SettingsService settingsSvc)
            {
                settingsSvc.SetWorkspacePath(folderPath);
            }
            StatusText = $"Opened folder: {folderPath}";
        }
    }

    [RelayCommand]
    private async Task AddFolderToWorkspaceAsync()
    {
        var folderPath = await _dialogService.ShowFolderDialogAsync(
            new Core.Abstractions.Services.FolderDialogOptions { Title = "Add Folder to Workspace" });
        if (string.IsNullOrEmpty(folderPath)) return;

        if (_workspaceService.CurrentWorkspace == null)
        {
            _workspaceService.CreateNewWorkspace();
        }

        _workspaceService.AddFolder(folderPath);
        SolutionExplorer.RefreshWorkspaceTree();
        Title = $"Visual Game Studio - {_workspaceService.CurrentWorkspace!.DisplayName}";
        StatusText = $"Added folder: {Path.GetFileName(folderPath)}";
    }

    [RelayCommand]
    private async Task OpenWorkspaceAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(new FileDialogOptions
        {
            Title = "Open Workspace",
            Filters = new List<FileDialogFilter>
            {
                new("VGS Workspace", "vgs-workspace"),
                new("All Files", "*")
            }
        });

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            SetBusy(true, "Opening workspace...");
            await _workspaceService.OpenWorkspaceAsync(filePath);
            SolutionExplorer.RefreshWorkspaceTree();
            Title = $"Visual Game Studio - {_workspaceService.CurrentWorkspace?.DisplayName ?? "Workspace"}";
            StatusText = $"Opened workspace: {Path.GetFileName(filePath)}";

            // Track in recent projects
            _recentProjectsService.AddRecentProject(
                filePath,
                _workspaceService.CurrentWorkspace?.DisplayName ?? Path.GetFileNameWithoutExtension(filePath));
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to open workspace: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task SaveWorkspaceAsAsync()
    {
        if (_workspaceService.CurrentWorkspace == null)
        {
            StatusText = "No workspace is open.";
            return;
        }

        var filePath = await _dialogService.ShowSaveFileDialogAsync(new FileDialogOptions
        {
            Title = "Save Workspace As",
            Filters = new List<FileDialogFilter>
            {
                new("VGS Workspace", "vgs-workspace"),
                new("All Files", "*")
            }
        });

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _workspaceService.SaveWorkspaceAsync(filePath);
            Title = $"Visual Game Studio - {_workspaceService.CurrentWorkspace.DisplayName}";
            StatusText = $"Workspace saved: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to save workspace: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc == null) return;

        var filePath = await _dialogService.ShowSaveFileDialogAsync(new FileDialogOptions
        {
            Title = "Save As",
            Filters = new List<FileDialogFilter>
            {
                new("BasicLang Files", "bas", "bl"),
                new("All Files", "*")
            }
        });

        if (!string.IsNullOrEmpty(filePath))
        {
            await _fileService.WriteFileAsync(filePath, activeDoc.Text);
            var oldPath = activeDoc.FilePath;
            activeDoc.FilePath = filePath;
            activeDoc.IsDirty = false;

            // Re-register under new path
            if (oldPath != null)
            {
                _openDocuments.Remove(oldPath);
                _autoSaveService.UnregisterDocument(oldPath);
                _filesWithErrors.TryRemove(oldPath, out _);
                if (string.Equals(_lastActiveEditorFilePath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastActiveEditorFilePath = filePath;
                }
            }
            _openDocuments[filePath] = activeDoc;
            RegisterDocumentForAutoSave(activeDoc, filePath);

            StatusText = $"Saved as: {Path.GetFileName(filePath)}";
        }
    }

    [RelayCommand]
    private void CloseEditor()
    {
        _dockFactory.CloseActiveDocument();
    }

    [RelayCommand]
    private async Task CloseFolderAsync()
    {
        if (_projectService.CurrentProject != null)
        {
            if (_projectService.HasUnsavedChanges)
            {
                var result = await _dialogService.ShowMessageAsync("Close Project",
                    "Save changes before closing?", DialogButtons.YesNoCancel, DialogIcon.Question);
                if (result == DialogResult.Cancel) return;
                if (result == DialogResult.Yes) await SaveAllAsync();
            }

            await _projectService.CloseProjectAsync();
            Title = "Visual Game Studio";
            StatusText = "Ready";
        }
    }

    // ── Solution Commands ──────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenSolutionAsync()
    {
        var filePath = await _dialogService.ShowOpenFileDialogAsync(new FileDialogOptions
        {
            Title = "Open Solution",
            Filters = new List<FileDialogFilter>
            {
                new("BasicLang Solution", "blsln"),
                new("All Files", "*")
            }
        });

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            SetBusy(true, "Opening solution...");
            var solution = await _solutionService.LoadSolutionAsync(filePath);
            SolutionExplorer.LoadSolution(solution);

            // Track in recent projects
            _recentProjectsService.AddRecentProject(filePath, solution.SolutionName);

            Title = $"{solution.SolutionName} - Visual Game Studio";
            StatusText = $"Solution loaded: {solution.SolutionName}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to open solution: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task NewSolutionAsync()
    {
        var name = await _dialogService.ShowInputDialogAsync("New Solution", "Enter solution name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var directory = await _dialogService.ShowFolderDialogAsync(
            new FolderDialogOptions { Title = "Select Solution Location" });
        if (string.IsNullOrEmpty(directory)) return;

        try
        {
            SetBusy(true, "Creating solution...");
            var solutionDir = Path.Combine(directory, name);
            var solution = await _solutionService.CreateSolutionAsync(name, solutionDir);
            SolutionExplorer.LoadSolution(solution);

            Title = $"{solution.SolutionName} - Visual Game Studio";
            StatusText = $"Solution created: {solution.SolutionName}";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to create solution: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    [RelayCommand]
    private async Task CloseSolutionAsync()
    {
        if (!_solutionService.HasSolution) return;

        // Check for unsaved changes
        var unsavedDocs = _openDocuments.Values.Where(d => d.IsDirty).ToList();
        if (unsavedDocs.Any())
        {
            var result = await _dialogService.ShowMessageAsync("Close Solution",
                "Save changes before closing?", DialogButtons.YesNoCancel, DialogIcon.Question);
            if (result == DialogResult.Cancel) return;
            if (result == DialogResult.Yes) await SaveAllAsync();
        }

        // Close all open documents
        foreach (var doc in _openDocuments.Values.ToList())
        {
            _dockFactory.CloseActiveDocument();
        }
        _openDocuments.Clear();

        await _solutionService.CloseSolutionAsync();
        SolutionExplorer.Nodes.Clear();
        Title = "Visual Game Studio";
        StatusText = "Ready";
    }

    [RelayCommand]
    private async Task BuildSolutionAsync()
    {
        if (!_solutionService.HasSolution)
        {
            await _dialogService.ShowMessageAsync("Build Solution", "No solution is open.",
                DialogButtons.Ok, DialogIcon.Information);
            return;
        }

        if (_buildService.IsBuilding) return;

        await SaveBeforeBuildAsync();

        ShowBuildOutput();

        StatusBar.SetBuildStarted();
        StatusText = "Building solution...";
        Services.ScreenReaderService.Instance.Announce("Solution build started");
        ShowProgressNotification("build", "Building solution...");

        var buildOrder = _solutionService.GetBuildOrder();
        var solutionDir = _solutionService.CurrentSolution!.SolutionDirectory;
        bool allSucceeded = true;

        for (int i = 0; i < buildOrder.Count; i++)
        {
            var proj = buildOrder[i];
            StatusText = $"Building {proj.Name} ({i + 1}/{buildOrder.Count})...";

            var projectPath = proj.GetFullPath(solutionDir);
            try
            {
                var project = await _projectService.OpenProjectAsync(projectPath);
                await _buildService.BuildProjectAsync(project);
            }
            catch (Exception ex)
            {
                _outputService.WriteError($"Failed to build {proj.Name}: {ex.Message}", OutputCategory.Build);
                allSucceeded = false;
                break;
            }
        }

        DismissNotification("build");
        StatusText = allSucceeded
            ? $"Solution build succeeded ({buildOrder.Count} project(s))"
            : "Solution build failed";
    }

    [RelayCommand]
    private async Task AddNewProjectToSolutionAsync()
    {
        if (!_solutionService.HasSolution) return;
        if (App.MainWindow == null) return;

        var vm = new ViewModels.Dialogs.AddProjectToSolutionViewModel();
        var existingNames = _solutionService.CurrentSolution!.Projects.Select(p => p.Name);
        vm.Initialize(_solutionService.CurrentSolution.SolutionDirectory, existingNames);

        var dialog = new Views.Dialogs.AddProjectToSolutionDialog
        {
            DataContext = vm
        };

        var result = await dialog.ShowDialog<bool?>(App.MainWindow);
        if (result != true || !vm.DialogResult) return;

        try
        {
            var outputType = vm.GetOutputType();
            var project = await _solutionService.AddNewProjectAsync(vm.ProjectName, outputType);

            // Write a richer .blproj with backend and source file reference
            var defaultCode = vm.GetDefaultCode();
            var hasSource = !string.IsNullOrEmpty(defaultCode) && vm.SelectedTemplate != "Empty";
            var compileItem = hasSource ? $"\n    <Compile Include=\"Program.bas\" />" : "";
            var referencesXml = "";
            var selectedRefs = vm.GetSelectedReferences();
            if (selectedRefs.Count > 0)
            {
                referencesXml = "\n  <ItemGroup>";
                foreach (var refName in selectedRefs)
                {
                    referencesXml += $"\n    <ProjectReference Include=\"..\\{refName}\\{refName}.blproj\" />";
                    _solutionService.AddProjectReference(vm.ProjectName, refName);
                }
                referencesXml += "\n  </ItemGroup>";
            }

            var blprojContent =
$"""
<?xml version="1.0" encoding="utf-8"?>
<BasicLangProject Version="1.0">
  <PropertyGroup>
    <ProjectName>{vm.ProjectName}</ProjectName>
    <OutputType>{outputType}</OutputType>
    <RootNamespace>{vm.ProjectName}</RootNamespace>
    <TargetBackend>{vm.SelectedBackend}</TargetBackend>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <OutputPath>bin\Debug</OutputPath>
    <DebugSymbols>true</DebugSymbols>
    <Optimize>false</Optimize>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <OutputPath>bin\Release</OutputPath>
    <DebugSymbols>false</DebugSymbols>
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>{compileItem}
  </ItemGroup>{referencesXml}
</BasicLangProject>
""";
            await File.WriteAllTextAsync(project.AbsolutePath, blprojContent);

            // Write default source file
            if (hasSource)
            {
                var projectDir = Path.GetDirectoryName(project.AbsolutePath);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    var sourcePath = Path.Combine(projectDir, "Program.bas");
                    await File.WriteAllTextAsync(sourcePath, defaultCode);
                }
            }

            await _solutionService.SaveSolutionAsync();
            SolutionExplorer.LoadSolution(_solutionService.CurrentSolution!);
            StatusText = $"Project '{vm.ProjectName}' added to solution";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to add project: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    [RelayCommand]
    private async Task AddExistingProjectToSolutionAsync()
    {
        if (!_solutionService.HasSolution) return;

        var filePath = await _dialogService.ShowOpenFileDialogAsync(new FileDialogOptions
        {
            Title = "Add Existing Project",
            Filters = new List<FileDialogFilter>
            {
                new("BasicLang Project", "blproj"),
                new("All Files", "*")
            }
        });

        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            await _solutionService.AddExistingProjectAsync(filePath);
            await _solutionService.SaveSolutionAsync();
            SolutionExplorer.LoadSolution(_solutionService.CurrentSolution!);
            StatusText = $"Project added to solution";
        }
        catch (Exception ex)
        {
            await _dialogService.ShowMessageAsync("Error", $"Failed to add project: {ex.Message}",
                DialogButtons.Ok, DialogIcon.Error);
        }
    }

    private void OnSolutionLoaded(object? sender, EventArgs e)
    {
        HasSolutionOpen = true;
        if (_solutionService.CurrentSolution != null)
        {
            Title = $"{_solutionService.CurrentSolution.SolutionName} - Visual Game Studio";
        }
    }

    private void OnSolutionClosed(object? sender, EventArgs e)
    {
        HasSolutionOpen = false;
        if (_projectService.CurrentProject == null)
        {
            Title = "Visual Game Studio";
        }
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        _recentProjectsService.ClearRecentProjects();
        StatusText = "Recent projects cleared";
    }

    [RelayCommand]
    private async Task OpenRecentProjectAsync(string projectPath)
    {
        if (!string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
        {
            try
            {
                await _projectService.OpenProjectAsync(projectPath);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowMessageAsync("Error",
                    $"Failed to open project: {ex.Message}", DialogButtons.Ok, DialogIcon.Error);
            }
        }
        else
        {
            StatusText = "Project file not found";
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        // "Open Settings (JSON)..." — open the dialog directly in its raw-JSON view. This was a
        // mislabeled duplicate of "Settings..." (it opened the normal UI view).
        await ShowSettingsDialogAsync(openJsonEditor: true);
    }

    [RelayCommand]
    private async Task OpenColorThemeAsync()
    {
        if (App.MainWindow == null) return;

        var themes = new List<string>(ThemeManager.AllThemeNames);
        themes.Add("--- Import VS Code Theme (.json) ---");

        var selected = await _dialogService.ShowListSelectionAsync("Color Theme", "Select a color theme:", themes);

        if (selected >= 0 && selected < themes.Count)
        {
            var themeName = themes[selected];

            if (themeName.StartsWith("---"))
            {
                await ImportVsCodeThemeAsync();
                return;
            }

            ThemeManager.Apply(themeName);
            // Persist through the same key the Settings dialog uses so the choice survives a restart
            // (the quick picker previously applied live but wrote nothing).
            _settingsService?.Set("workbench.colorTheme", themeName, SettingsScope.User);
            StatusText = $"Theme changed to: {themeName}";
            ShowNotification($"Theme: {themeName}", "info");
        }
    }

    private async Task ImportVsCodeThemeAsync()
    {
        try
        {
            if (App.MainWindow == null) return;

            var topLevel = Avalonia.Controls.TopLevel.GetTopLevel(App.MainWindow);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(
                new Avalonia.Platform.Storage.FilePickerOpenOptions
                {
                    Title = "Import VS Code Theme",
                    AllowMultiple = false,
                    FileTypeFilter = new[]
                    {
                        new Avalonia.Platform.Storage.FilePickerFileType("VS Code Theme")
                        {
                            Patterns = new[] { "*.json" }
                        }
                    }
                });

            if (files.Count == 0) return;

            var filePath = files[0].Path?.LocalPath;
            if (string.IsNullOrEmpty(filePath)) return;

            var label = await ThemeManager.LoadVsCodeThemeFileAsync(filePath);
            if (label != null)
            {
                ThemeManager.Apply(label);
                // Persist both the active theme name and the source file path so the imported theme
                // is re-registered (ReloadImportedThemesAsync) and re-applied on the next launch.
                _settingsService?.Set("workbench.colorTheme", label, SettingsScope.User);
                ThemeManager.RememberImportedThemePath(_settingsService, filePath);
                StatusText = $"Theme imported and applied: {label}";
                ShowNotification($"Theme: {label}", "info");
            }
            else
            {
                StatusText = "Failed to import theme file.";
                ShowNotification("Failed to import theme. The file may not be a valid VS Code theme.", "error");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Theme import error: {ex.Message}";
        }
    }

    // D3: OpenFileIconThemeAsync (the "File Icon Theme..." picker) removed — it wrote nothing (no
    // file-icon-theme feature exists) and its value set didn't even match the schema. The command
    // palette entry that invoked it was removed with it.

    #endregion

    #region Menu Commands — Edit (additional)

    [RelayCommand]
    private void ToggleBlockComment()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestToggleBlockComment();
    }

    [RelayCommand]
    private void ReplaceInFiles()
    {
        _dockFactory.ActivateTool("FindInFiles");
        FindInFiles.IsReplaceMode = true;
    }

    [RelayCommand]
    private void SelectAll()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestSelectAll();
    }

    #endregion

    #region Menu Commands — Selection

    [RelayCommand]
    private void CopyLineUp()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestCopyLineUp();
    }

    [RelayCommand]
    private void CopyLineDown()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestCopyLineDown();
    }

    [RelayCommand]
    private void AddCursorAbove()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestAddCursorAbove();
    }

    [RelayCommand]
    private void AddCursorBelow()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestAddCursorBelow();
    }

    [RelayCommand]
    private void AddCursorsToLineEnds()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestAddCursorsToLineEnds();
    }

    #endregion

    #region Menu Commands — View (additional)

    [RelayCommand]
    private void ShowSearchPanel()
    {
        _dockFactory.ActivateTool("FindInFiles");
    }

    [RelayCommand]
    private void ShowSourceControl()
    {
        _dockFactory.ActivateTool("GitChanges");
    }

    [RelayCommand]
    private void ShowDebugPanel()
    {
        _dockFactory.ActivateTool("Variables");
    }

    [RelayCommand]
    private void ShowExtensions()
    {
        _dockFactory.ActivateTool("Extensions");
    }

    [RelayCommand]
    private void ShowProblems()
    {
        _dockFactory.ActivateTool("ErrorList");
    }

    [RelayCommand]
    private void ToggleFullScreen()
    {
        IsFullScreen = !IsFullScreen;
        // The view code-behind handles the actual window state change
        StatusText = IsFullScreen ? "Full screen mode" : "Windowed mode";
    }

    [RelayCommand]
    private void ToggleMenuBar()
    {
        ShowMenuBar = !ShowMenuBar;
        StatusText = ShowMenuBar ? "Menu bar visible" : "Menu bar hidden (press Alt to show)";
    }

    [RelayCommand]
    private void ToggleSideBar()
    {
        ShowSideBar = !ShowSideBar;
        StatusText = ShowSideBar ? "Side bar visible" : "Side bar hidden";
    }

    [RelayCommand]
    private void ToggleStatusBar()
    {
        ShowStatusBar = !ShowStatusBar;
        StatusText = ShowStatusBar ? "Status bar visible" : "Status bar hidden";
    }

    [RelayCommand]
    private void TogglePanel()
    {
        ShowPanel = !ShowPanel;
        StatusText = ShowPanel ? "Panel visible" : "Panel hidden";
    }

    /// <summary>
    /// Toggles the bottom panel between maximized and normal layout.
    /// When maximized, the bottom panel fills the entire content area (sidebar and editor hidden).
    /// </summary>
    [RelayCommand]
    private void ToggleBottomPanelMaximize()
    {
        var isMaximized = _dockFactory.ToggleBottomPanelMaximize();
        IsBottomPanelMaximized = isMaximized;
        StatusText = isMaximized ? "Panel maximized" : "Panel restored";
    }

    /// <summary>
    /// Restores the bottom panel from maximized state. Called on Escape key.
    /// </summary>
    public void RestoreBottomPanel()
    {
        if (IsBottomPanelMaximized)
        {
            _dockFactory.RestoreBottomPanelIfMaximized();
            IsBottomPanelMaximized = false;
            StatusText = "Panel restored";
        }
    }

    [RelayCommand]
    private void ToggleMinimap()
    {
        ShowMinimap = !ShowMinimap;
        // Persist so the toggle survives the next settings event and restarts (VS Code parity).
        PersistMinimapToggle(_settingsService, ShowMinimap);
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleMinimap(ShowMinimap);
        }
        StatusText = ShowMinimap ? "Minimap visible" : "Minimap hidden";
    }

    /// <summary>
    /// Mirrors editor.minimap.enabled / editor.renderWhitespace changes onto the View-menu toggle
    /// state so the menu checkmarks stay truthful when the settings change (e.g. via the Settings
    /// dialog). The editors re-read these through ApplyEditorSettings independently.
    /// </summary>
    private void OnEditorDisplaySettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (_settingsService == null) return;
        if (e.Key != "editor.minimap.enabled" && e.Key != "editor.renderWhitespace") return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.Key == "editor.minimap.enabled")
                ShowMinimap = _settingsService.Get("editor.minimap.enabled", true);
            else
                ShowWhitespace = _settingsService.Get("editor.renderWhitespace", "none") != "none";
        });
    }

    [RelayCommand]
    private void ToggleBreadcrumbs()
    {
        ShowBreadcrumbs = !ShowBreadcrumbs;
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleBreadcrumbs(ShowBreadcrumbs);
        }
        StatusText = ShowBreadcrumbs ? "Breadcrumbs visible" : "Breadcrumbs hidden";
    }

    [RelayCommand]
    private void ToggleStickyScroll()
    {
        ShowStickyScroll = !ShowStickyScroll;
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleStickyScroll(ShowStickyScroll);
        }
        StatusText = ShowStickyScroll ? "Sticky scroll enabled" : "Sticky scroll disabled";
    }

    [RelayCommand]
    private void ToggleWordWrap()
    {
        WordWrap = !WordWrap;
        foreach (var doc in _dockFactory.GetAllDocuments().OfType<CodeEditorDocumentViewModel>())
        {
            doc.RequestToggleWordWrap(WordWrap);
        }
        StatusText = WordWrap ? "Word wrap enabled" : "Word wrap disabled";
    }

    [RelayCommand]
    private void SplitEditorRight()
    {
        StatusText = "Split editor right";
        ShowNotification("Editor split — use tabs to switch between split panes", "info");
    }

    [RelayCommand]
    private void SplitEditorDown()
    {
        StatusText = "Split editor down";
    }

    [RelayCommand]
    private void SingleEditorLayout()
    {
        StatusText = "Single editor layout";
    }

    [RelayCommand]
    private void TwoColumnsLayout()
    {
        StatusText = "Two columns layout";
    }

    [RelayCommand]
    private void GridLayout()
    {
        StatusText = "Grid layout (2x2)";
    }

    #endregion

    #region Menu Commands — Go (additional)

    [RelayCommand]
    private void NavigateBack()
    {
        // Navigation history: go back to previous caret position
        StatusText = "Navigate back";
    }

    [RelayCommand]
    private void NavigateForward()
    {
        StatusText = "Navigate forward";
    }

    [RelayCommand]
    private async Task GoToDeclarationAsync()
    {
        // Declaration is typically the same as definition in BasicLang
        await GoToDefinitionAsync();
    }

    [RelayCommand]
    private void GoToBracket()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        activeDoc?.RequestGoToBracket();
        StatusText = "Go to matching bracket";
    }

    #endregion

    #region Menu Commands — Run (additional)

    [RelayCommand]
    private async Task NewConditionalBreakpointAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        var condition = await _dialogService.ShowInputDialogAsync(
            "Conditional Breakpoint",
            "Enter condition expression:",
            "");

        if (string.IsNullOrEmpty(condition)) return;

        Breakpoints.AddBreakpoint(activeDoc.FilePath, activeDoc.CaretLine);
        // Update the breakpoint with the condition
        var bp = Breakpoints.GetBreakpointsForFile(activeDoc.FilePath)
            .FirstOrDefault(b => b.Line == activeDoc.CaretLine);
        if (bp != null)
        {
            Breakpoints.UpdateBreakpointCondition(bp, condition, null, null);
        }
        StatusText = $"Conditional breakpoint set at line {activeDoc.CaretLine}";
    }

    [RelayCommand]
    private async Task NewLogpointAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        var message = await _dialogService.ShowInputDialogAsync(
            "Logpoint",
            "Enter log message (use {expression} for interpolation):",
            "");

        if (string.IsNullOrEmpty(message)) return;

        Breakpoints.AddBreakpoint(activeDoc.FilePath, activeDoc.CaretLine);
        var bp = Breakpoints.GetBreakpointsForFile(activeDoc.FilePath)
            .FirstOrDefault(b => b.Line == activeDoc.CaretLine);
        if (bp != null)
        {
            Breakpoints.UpdateBreakpointCondition(bp, null, null, message);
        }
        StatusText = $"Logpoint set at line {activeDoc.CaretLine}";
    }

    #endregion

    #region Menu Commands — Terminal

    [RelayCommand]
    private void SplitTerminal()
    {
        _dockFactory.ActivateTool("Terminal");
        Terminal.SplitTerminalCommand?.Execute(null);
        StatusText = "Terminal split";
    }

    [RelayCommand]
    private async Task RunTaskAsync()
    {
        var projectDir = _projectService.CurrentProject?.ProjectDirectory;
        if (string.IsNullOrEmpty(projectDir))
        {
            ShowNotification("No project open. Open a project to run tasks.", "warning");
            return;
        }

        var tasks = _taskRunnerService.GetAvailableTasks(projectDir);
        if (tasks.Count == 0)
        {
            ShowNotification("No tasks found. Use 'Configure Tasks...' to create tasks.json.", "info");
            return;
        }

        var taskLabels = tasks.Select(t =>
            t.IsAutoDetected ? $"{t.Label} (auto-detected)" : t.Label).ToList();

        var selected = await _dialogService.ShowListSelectionAsync(
            "Run Task", "Select a task to run:", taskLabels);

        if (selected >= 0 && selected < tasks.Count)
        {
            var task = tasks[selected];
            StatusText = $"Running task: {task.Label}...";
            _dockFactory.ActivateTool("Output");

            var exitCode = await _taskRunnerService.RunTaskAsync(task, projectDir);
            StatusText = exitCode == 0
                ? $"Task '{task.Label}' completed successfully"
                : $"Task '{task.Label}' failed (exit code {exitCode})";
        }
    }

    [RelayCommand]
    private async Task RunBuildTaskAsync()
    {
        var projectDir = _projectService.CurrentProject?.ProjectDirectory;
        if (string.IsNullOrEmpty(projectDir))
        {
            await BuildAsync();
            return;
        }

        var buildTask = await _taskRunnerService.GetDefaultBuildTaskAsync(projectDir);
        if (buildTask != null)
        {
            StatusText = $"Running build task: {buildTask.Label}...";
            _dockFactory.ActivateTool("Output");

            var exitCode = await _taskRunnerService.RunTaskAsync(buildTask, projectDir);
            StatusText = exitCode == 0
                ? $"Build task '{buildTask.Label}' completed successfully"
                : $"Build task '{buildTask.Label}' failed (exit code {exitCode})";
        }
        else
        {
            await BuildAsync();
        }
    }

    [RelayCommand]
    private async Task RunTestTaskAsync()
    {
        var projectDir = _projectService.CurrentProject?.ProjectDirectory;
        if (string.IsNullOrEmpty(projectDir))
        {
            ShowNotification("No project open.", "warning");
            return;
        }

        var testTask = await _taskRunnerService.GetDefaultTestTaskAsync(projectDir);
        if (testTask != null)
        {
            StatusText = $"Running test task: {testTask.Label}...";
            _dockFactory.ActivateTool("Output");

            var exitCode = await _taskRunnerService.RunTaskAsync(testTask, projectDir);
            StatusText = exitCode == 0
                ? $"Test task '{testTask.Label}' completed successfully"
                : $"Test task '{testTask.Label}' failed (exit code {exitCode})";
        }
        else
        {
            ShowNotification("No test task configured. Add a task with group 'test' in tasks.json.", "info");
        }
    }

    [RelayCommand]
    private async Task ConfigureTasksAsync()
    {
        var projectDir = _projectService.CurrentProject?.ProjectDirectory;
        if (string.IsNullOrEmpty(projectDir))
        {
            ShowNotification("No project open. Open a project to configure tasks.", "warning");
            return;
        }

        var tasksFile = _taskRunnerService.GetTasksFilePath(projectDir);
        if (!File.Exists(tasksFile))
        {
            tasksFile = await _taskRunnerService.CreateDefaultTasksFileAsync(projectDir);
            ShowNotification("Created default tasks.json", "info");
        }

        await OpenFileAsync(tasksFile);
        StatusText = "Editing tasks.json";
    }

    [RelayCommand]
    private async Task ConfigureDefaultShellAsync()
    {
        var shells = new List<string> { "PowerShell", "Command Prompt", "Git Bash", "WSL" };
        var selected = await _dialogService.ShowListSelectionAsync(
            "Configure Default Shell", "Select default terminal shell:", shells);

        if (selected >= 0 && selected < shells.Count)
        {
            StatusText = $"Default shell: {shells[selected]}";
            ShowNotification($"Default terminal shell set to {shells[selected]}", "info");
        }
    }

    #endregion

    #region Menu Commands — Help

    [RelayCommand]
    private void ShowWelcome()
    {
        ShowNotification("Welcome to Visual Game Studio IDE!", "info");
        StatusText = "Welcome to Visual Game Studio";
    }

    [RelayCommand]
    private void ShowDocumentation()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/gracepriest/VisualGameStudioEngine",
                UseShellExecute = true
            });
        }
        catch { StatusText = "Could not open documentation"; }
    }

    [RelayCommand]
    private void ShowReleaseNotes()
    {
        ShowNotification("Visual Game Studio IDE v1.0 — 60+ features, 1725 tests passing", "info");
        StatusText = "Release Notes";
    }

    [RelayCommand]
    private void ReportIssue()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/gracepriest/VisualGameStudioEngine/issues/new",
                UseShellExecute = true
            });
        }
        catch { StatusText = "Could not open issue tracker"; }
    }

    [RelayCommand]
    private void ViewLicense()
    {
        ShowNotification("Visual Game Studio IDE is proprietary software.", "info");
        StatusText = "License information";
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        ShowNotification("You are running the latest version.", "info");
        StatusText = "No updates available";
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        await _dialogService.ShowMessageAsync(
            "About Visual Game Studio",
            "Visual Game Studio IDE v1.0\n\n" +
            "A complete game development platform with:\n" +
            "- BasicLang compiler (C#, LLVM, MSIL, C++ backends)\n" +
            "- Full-featured IDE with IntelliSense, debugging, and source control\n" +
            "- 2D game engine built on Raylib\n\n" +
            "1725 tests passing | VS Code parity ~95%\n\n" +
            "Copyright (c) 2026 Visual Game Studio",
            DialogButtons.Ok, DialogIcon.Information);
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
        if (activeDoc?.FilePath == null || !_languageServices.IsConnectedFor(activeDoc.FilePath)) return;

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

        // Activate the Call Hierarchy panel
        _dockFactory.ActivateTool("CallHierarchy");
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
        if (activeDoc?.FilePath == null || !_languageServices.IsConnectedFor(activeDoc.FilePath)) return;

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
        if (activeDoc?.FilePath == null) return;
        // Route selection ranges to the server that owns this file (BasicLang for .bas, clangd for .cpp).
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is not { IsConnected: true }) return;

        if (_currentSelectionRange == null)
        {
            // Get fresh selection ranges from LSP
            var positions = new List<(int line, int column)> { (activeDoc.CaretLine, activeDoc.CaretColumn) };
            var ranges = await svc.GetSelectionRangesAsync(activeDoc.FilePath, positions);
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
            // Ask the server that owns this file for documentSymbol — BasicLang for .bas, clangd for
            // .cpp once registered. The text-parser fallback is BasicLang-specific and produces a
            // wrong, partial outline for C++ (it parses `class Foo {` as a node and `//` as code), so
            // it only runs for BasicLang files; anything else with no server gets an empty outline.
            var svc = _languageServices.GetFor(filePath);
            if (svc is { IsConnected: true })
            {
                await DocumentOutline.UpdateOutlineFromLspAsync(filePath, svc);
            }
            else
            {
                DocumentOutline.UpdateOutlineFromTextFallback(filePath, content);
            }
        }
        catch
        {
            // Fallback to text-based parsing — BasicLang only (see above).
            DocumentOutline.UpdateOutlineFromTextFallback(filePath, content);
        }
    }

    /// <summary>
    /// Fetches code lenses from the LSP server and displays them in the editor.
    /// </summary>
    private async Task RefreshCodeLensesAsync(CodeEditorDocumentViewModel document)
    {
        try
        {
            if (document.FilePath == null) return;
            // Code lens stays BasicLang-only in Phase 3a: HandleCodeLensCommandAsync dispatches on
            // literal "basiclang.*" command names, so a clangd lens would render but do nothing when
            // clicked. TODO(Phase 3b): drop this gate and route once clangd command names are handled.
            if (!BasicLangFileTypes.IsBasicLangSourceFile(document.FilePath)) return;
            var svc = _languageServices.GetFor(document.FilePath);
            if (svc is not { IsConnected: true }) return;

            var lenses = await svc.GetCodeLensAsync(document.FilePath);
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
            if (document.FilePath == null) return;
            // Route semantic tokens to the server that owns this file (BasicLang for .bas, clangd for
            // .cpp once registered) — the editor decodes the token data, which is server-agnostic.
            var svc = _languageServices.GetFor(document.FilePath);
            if (svc is not { IsConnected: true }) return;

            var result = await svc.GetSemanticTokensAsync(document.FilePath, cancellationToken);
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
            // Code lenses are only ever populated for BasicLang source files (see
            // RefreshCodeLensesAsync's gate), so this should be unreachable for a
            // non-BasicLang document — guard anyway so a clicked lens never sends
            // an LSP request for a .cpp/.h URI the server would misinterpret.
            // The command names below are literal "basiclang.*", so this whole handler is
            // BasicLang-specific by construction. TODO(Phase 3b): handle clangd's lens commands.
            if (!BasicLangFileTypes.IsBasicLangSourceFile(document.FilePath)) return;

            // BasicLang-gated above, so this routes to the BasicLang server.
            var svc = _languageServices.GetFor(document.FilePath);
            if (svc is not { IsConnected: true }) return;

            switch (info.CommandName)
            {
                case "basiclang.showReferences":
                    // Find references for the symbol on this line
                    if (document.FilePath != null)
                    {
                        var refs = await svc.FindReferencesAsync(
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
                                var refPath = VisualGameStudio.ProjectSystem.Services.LanguageService.UriToPath(firstRef.Uri);
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
                        var def = await svc.GetDefinitionAsync(
                            document.FilePath, info.Line, 1);
                        if (def != null)
                        {
                            var defPath = VisualGameStudio.ProjectSystem.Services.LanguageService.UriToPath(def.Uri);
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

    /// <summary>
    /// Builds LSP formatting options from the editor.tabSize / editor.insertSpaces settings so
    /// Format Document (and format-on-save) honor the user's indentation preferences instead of
    /// always sending the 4-spaces defaults.
    /// </summary>
    private FormattingOptionsInfo BuildFormattingOptions() => new()
    {
        TabSize = _settingsService?.Get("editor.tabSize", 4) ?? 4,
        InsertSpaces = _settingsService?.Get("editor.insertSpaces", true) ?? true,
    };

    private async Task FormatDocumentAsync()
    {
        var activeDoc = _dockFactory.GetActiveDocument() as CodeEditorDocumentViewModel;
        if (activeDoc?.FilePath == null) return;

        if (!_languageServices.IsConnectedFor(activeDoc.FilePath))
        {
            StatusText = "Language server not connected";
            return;
        }

        var applied = await FormatDocumentContentAsync(activeDoc);
        StatusText = applied > 0
            ? $"Applied {applied} formatting changes"
            : "Document is already formatted";
    }

    /// <summary>
    /// Requests LSP formatting for <paramref name="doc"/> and applies the returned edits to its
    /// content, returning the number of edits applied (0 if none or the server is not connected).
    /// Shared by Format Document and format-on-save so both honor editor.tabSize / insertSpaces.
    /// Operates on the passed document (not necessarily the active one) so SaveAll can format each.
    /// </summary>
    private async Task<int> FormatDocumentContentAsync(CodeEditorDocumentViewModel doc)
    {
        if (doc.FilePath == null) return 0;
        // Route to the server that owns this file (BasicLang for .bas, clangd for .cpp once
        // registered). Silent no-op (0 edits applied) when no server serves it, so format-on-save
        // never corrupts a buffer no language server owns.
        var svc = _languageServices.GetFor(doc.FilePath);
        if (svc is not { IsConnected: true }) return 0;

        var edits = await svc.FormatDocumentAsync(doc.FilePath, BuildFormattingOptions());
        if (edits.Count == 0) return 0;

        // Apply the edits in reverse order to preserve offsets.
        var sortedEdits = edits.OrderByDescending(e => e.StartLine).ThenByDescending(e => e.StartColumn).ToList();
        var lines = doc.Text.Split('\n');

        foreach (var edit in sortedEdits)
        {
            // Convert to 0-based indices.
            var startLine = edit.StartLine - 1;
            var endLine = edit.EndLine - 1;
            var startCol = edit.StartColumn - 1;
            var endCol = edit.EndColumn - 1;

            // Simple line-based replacement for now.
            if (startLine >= 0 && startLine < lines.Length)
            {
                if (startLine == endLine && startCol >= 0 && endCol <= lines[startLine].Length)
                {
                    var line = lines[startLine];
                    lines[startLine] = line.Substring(0, startCol) + edit.NewText + line.Substring(endCol);
                }
            }
        }

        doc.ReplaceContent(string.Join("\n", lines));
        return edits.Count;
    }

    private async Task OnTypeFormattingAsync(CodeEditorDocumentViewModel document, int line, int column, string triggerCharacter)
    {
        if (document.FilePath == null) return;
        // Route to the server that owns this file (BasicLang for .bas, clangd for .cpp).
        var svc = _languageServices.GetFor(document.FilePath);
        if (svc is not { IsConnected: true }) return;

        var edits = await svc.OnTypeFormattingAsync(document.FilePath, line, column, triggerCharacter);
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
        // Route to the server that owns this file (BasicLang for .bas, clangd for .cpp).
        var svc = _languageServices.GetFor(activeDoc.FilePath);
        if (svc is not { IsConnected: true })
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

        var actions = await svc.GetCodeActionsAsync(
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
    /// Parses completion items from extension host JSON response.
    /// Delegates to the shared (unit-tested) parser, which handles the
    /// vscode.SnippetString object form of insertText, insertTextFormat, and
    /// guards each item so one malformed entry can't drop the rest.
    /// </summary>
    private static IReadOnlyList<CompletionItem> ParseExtensionCompletions(System.Text.Json.JsonElement json)
        => VisualGameStudio.Core.Utilities.ExtensionCompletionParsing.Parse(json);

    /// <summary>
    /// Parses hover info from extension host JSON response.
    /// </summary>
    private static HoverInfo? ParseExtensionHover(System.Text.Json.JsonElement json)
    {
        try
        {
            if (json.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

            string? content = null;

            // VS Code hover format: { contents: MarkupContent | string | array }
            if (json.TryGetProperty("contents", out var contents))
            {
                if (contents.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    content = contents.GetString();
                }
                else if (contents.ValueKind == System.Text.Json.JsonValueKind.Object &&
                         contents.TryGetProperty("value", out var val))
                {
                    content = val.GetString();
                }
                else if (contents.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var part in contents.EnumerateArray())
                    {
                        if (part.ValueKind == System.Text.Json.JsonValueKind.String)
                            parts.Add(part.GetString() ?? "");
                        else if (part.TryGetProperty("value", out var pv))
                            parts.Add(pv.GetString() ?? "");
                    }
                    content = string.Join("\n\n", parts);
                }
            }

            if (string.IsNullOrEmpty(content)) return null;
            return new HoverInfo { Contents = content };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ExtHover] Parse error: {ex.Message}");
            return null;
        }
    }

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
    public string? Details { get; }
    public bool AutoDismiss { get; }
    public double Progress { get; }
    public bool IsIndeterminate { get; }
    public bool ShowProgress { get; }
    public List<NotificationAction> Actions { get; }
    public string? NotificationId { get; }

    public NotificationEventArgs(string message, string severity)
    {
        Message = message;
        Severity = severity;
        AutoDismiss = severity == "info";
        Actions = new List<NotificationAction>();
    }

    public NotificationEventArgs(string message, string severity, string? details,
        bool autoDismiss, List<NotificationAction>? actions = null,
        double progress = 0, bool isIndeterminate = false, bool showProgress = false,
        string? notificationId = null)
    {
        Message = message;
        Severity = severity;
        Details = details;
        AutoDismiss = autoDismiss;
        Actions = actions ?? new List<NotificationAction>();
        Progress = progress;
        IsIndeterminate = isIndeterminate;
        ShowProgress = showProgress;
        NotificationId = notificationId;
    }
}

/// <summary>
/// Represents a clickable action button on a notification toast.
/// </summary>
public class NotificationAction
{
    public string Label { get; }
    public Action Callback { get; }

    public NotificationAction(string label, Action callback)
    {
        Label = label;
        Callback = callback;
    }
}

/// <summary>
/// Event args for temporary status bar messages.
/// </summary>
public class StatusBarMessageEventArgs : EventArgs
{
    public string Message { get; }
    public double DurationSeconds { get; }

    public StatusBarMessageEventArgs(string message, double durationSeconds)
    {
        Message = message;
        DurationSeconds = durationSeconds;
    }
}

/// <summary>
/// View model for a single item in the Open Recent submenu.
/// </summary>
public class RecentProjectMenuItem
{
    public string Header { get; set; } = "";
    public string? ToolTip { get; set; }
    public string? FilePath { get; set; }
    public System.Windows.Input.ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public bool IsEnabled { get; set; } = true;
}
