using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Shell.Dock;

public class DockFactory : Factory
{
    public event EventHandler<string>? DocumentClosed;

    private SolutionExplorerViewModel? _solutionExplorer;
    private OutputPanelViewModel? _outputPanel;
    private ErrorListViewModel? _errorList;
    private CallStackViewModel? _callStack;
    private VariablesViewModel? _variables;
    private BreakpointsViewModel? _breakpoints;
    private FindInFilesViewModel? _findInFiles;
    private TerminalViewModel? _terminal;
    private GitChangesViewModel? _gitChanges;
    private GitBranchesViewModel? _gitBranches;
    private GitStashViewModel? _gitStash;
    private GitBlameViewModel? _gitBlame;
    private WatchViewModel? _watch;
    private ImmediateWindowViewModel? _immediateWindow;
    private DocumentOutlineViewModel? _documentOutline;
    private BookmarksViewModel? _bookmarks;
    private ExtensionsViewModel? _extensions;
    private ProblemsViewModel? _problems;
    private DebugConsoleViewModel? _debugConsole;
    private ThreadsViewModel? _threads;
    private TimelineViewModel? _timeline;
    private CallHierarchyViewModel? _callHierarchy;
    private DocumentDock? _documentDock;
    private IRootDock? _rootDock;
    private ProportionalDock? _leftDock;
    private ProportionalDock? _mainArea;
    private ProportionalDock? _bottomDock;

    // Saved proportions for maximize/restore
    private double _savedLeftProportion;
    private double _savedDocumentProportion;
    private double _savedBottomProportion;
    private double _savedMainAreaProportion;
    private bool _isBottomPanelMaximized;

    public void SetViewModels(
        SolutionExplorerViewModel solutionExplorer,
        OutputPanelViewModel outputPanel,
        ErrorListViewModel errorList,
        CallStackViewModel? callStack = null,
        VariablesViewModel? variables = null,
        BreakpointsViewModel? breakpoints = null,
        FindInFilesViewModel? findInFiles = null,
        TerminalViewModel? terminal = null,
        GitChangesViewModel? gitChanges = null,
        GitBranchesViewModel? gitBranches = null,
        GitStashViewModel? gitStash = null,
        GitBlameViewModel? gitBlame = null,
        WatchViewModel? watch = null,
        ImmediateWindowViewModel? immediateWindow = null,
        DocumentOutlineViewModel? documentOutline = null,
        BookmarksViewModel? bookmarks = null,
        ExtensionsViewModel? extensions = null,
        ProblemsViewModel? problems = null,
        DebugConsoleViewModel? debugConsole = null,
        ThreadsViewModel? threads = null,
        TimelineViewModel? timeline = null,
        CallHierarchyViewModel? callHierarchy = null)
    {
        _solutionExplorer = solutionExplorer;
        _outputPanel = outputPanel;
        _errorList = errorList;
        _callStack = callStack;
        _variables = variables;
        _breakpoints = breakpoints;
        _findInFiles = findInFiles;
        _terminal = terminal;
        _gitChanges = gitChanges;
        _gitBranches = gitBranches;
        _gitStash = gitStash;
        _gitBlame = gitBlame;
        _watch = watch;
        _immediateWindow = immediateWindow;
        _documentOutline = documentOutline;
        _bookmarks = bookmarks;
        _extensions = extensions;
        _problems = problems;
        _debugConsole = debugConsole;
        _threads = threads;
        _timeline = timeline;
        _callHierarchy = callHierarchy;
    }

    public override IRootDock CreateLayout()
    {
        // Create tool panels
        var solutionExplorerTool = new SolutionExplorerTool
        {
            Id = "SolutionExplorer",
            Title = "Solution Explorer",
            ViewModel = _solutionExplorer
        };

        var outputTool = new OutputTool
        {
            Id = "Output",
            Title = "Output",
            ViewModel = _outputPanel
        };

        var errorListTool = new ErrorListTool
        {
            Id = "ErrorList",
            Title = "Error List",
            ViewModel = _errorList
        };

        // Welcome document
        var welcomeDocument = new WelcomeDocument
        {
            Id = "Welcome",
            Title = "Start Page"
        };

        // Git Changes tool (declared here to be used in left dock)
        var gitChangesTool = new GitChangesTool
        {
            Id = "GitChanges",
            Title = "Git Changes",
            ViewModel = _gitChanges
        };

        var gitBranchesTool = new GitBranchesTool
        {
            Id = "GitBranches",
            Title = "Branches",
            ViewModel = _gitBranches
        };

        var gitStashTool = new GitStashTool
        {
            Id = "GitStash",
            Title = "Stash",
            ViewModel = _gitStash
        };

        var gitBlameTool = new GitBlameTool
        {
            Id = "GitBlame",
            Title = "Blame",
            ViewModel = _gitBlame
        };

        var documentOutlineTool = new DocumentOutlineTool
        {
            Id = "DocumentOutline",
            Title = "Outline",
            ViewModel = _documentOutline
        };

        var bookmarksTool = new BookmarksTool
        {
            Id = "Bookmarks",
            Title = "Bookmarks",
            ViewModel = _bookmarks
        };

        var extensionsTool = new ExtensionsTool
        {
            Id = "Extensions",
            Title = "Extensions",
            ViewModel = _extensions
        };

        var timelineTool = new TimelineTool
        {
            Id = "Timeline",
            Title = "Timeline",
            ViewModel = _timeline
        };

        // Left tool dock (Solution Explorer, Git panels, Outline, Bookmarks, Timeline)
        _leftDock = new ProportionalDock
        {
            Id = "LeftDock",
            Proportion = 0.2,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                new ToolDock
                {
                    Id = "LeftTools",
                    VisibleDockables = CreateList<IDockable>(solutionExplorerTool, gitChangesTool, gitBranchesTool, gitStashTool, gitBlameTool, timelineTool, documentOutlineTool, bookmarksTool, extensionsTool),
                    ActiveDockable = solutionExplorerTool,
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible
                }
            )
        };
        var leftDock = _leftDock;

        // Document area
        _documentDock = new DocumentDock
        {
            Id = "DocumentDock",
            Title = "Documents",
            Proportion = 0.6,
            VisibleDockables = CreateList<IDockable>(welcomeDocument),
            ActiveDockable = welcomeDocument,
            CanCreateDocument = false,
            IsCollapsable = false
        };

        // Debug tools
        var callStackTool = new CallStackTool
        {
            Id = "CallStack",
            Title = "Call Stack",
            ViewModel = _callStack
        };

        var variablesTool = new VariablesTool
        {
            Id = "Variables",
            Title = "Variables",
            ViewModel = _variables
        };

        var breakpointsTool = new BreakpointsTool
        {
            Id = "Breakpoints",
            Title = "Breakpoints",
            ViewModel = _breakpoints
        };

        var findInFilesTool = new FindInFilesTool
        {
            Id = "FindInFiles",
            Title = "Find in Files",
            ViewModel = _findInFiles
        };

        var terminalTool = new TerminalTool
        {
            Id = "Terminal",
            Title = "Terminal",
            ViewModel = _terminal
        };

        var watchTool = new WatchTool
        {
            Id = "Watch",
            Title = "Watch",
            ViewModel = _watch
        };

        var immediateWindowTool = new ImmediateWindowTool
        {
            Id = "ImmediateWindow",
            Title = "Immediate",
            ViewModel = _immediateWindow
        };

        var problemsTool = new ProblemsTool
        {
            Id = "Problems",
            Title = "Problems",
            ViewModel = _problems
        };

        var debugConsoleTool = new DebugConsoleTool
        {
            Id = "DebugConsole",
            Title = "Debug Console",
            ViewModel = _debugConsole
        };

        var threadsTool = new ThreadsTool
        {
            Id = "Threads",
            Title = "Threads",
            ViewModel = _threads
        };

        var callHierarchyTool = new CallHierarchyTool
        {
            Id = "CallHierarchy",
            Title = "Call Hierarchy",
            ViewModel = _callHierarchy
        };

        // Bottom tool dock - split into two groups for better tab visibility
        // Left group: General tools (Output, Error List, Terminal, Find)
        var bottomLeftTools = new ToolDock
        {
            Id = "BottomLeftTools",
            Title = "Output",
            Proportion = 0.5,
            VisibleDockables = CreateList<IDockable>(outputTool, errorListTool, problemsTool, terminalTool, findInFilesTool, callHierarchyTool),
            ActiveDockable = outputTool,
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible
        };

        // Right group: Debug tools (Call Stack, Variables, Breakpoints, Watch, Immediate, Threads)
        var bottomRightTools = new ToolDock
        {
            Id = "BottomRightTools",
            Title = "Debug",
            Proportion = 0.5,
            VisibleDockables = CreateList<IDockable>(callStackTool, variablesTool, breakpointsTool, watchTool, immediateWindowTool, debugConsoleTool, threadsTool),
            ActiveDockable = breakpointsTool,
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible
        };

        _bottomDock = new ProportionalDock
        {
            Id = "BottomDock",
            Proportion = 0.35,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                bottomLeftTools,
                new ProportionalDockSplitter(),
                bottomRightTools
            )
        };
        var bottomDock = _bottomDock;

        // Main content area (documents + bottom tools)
        _mainArea = new ProportionalDock
        {
            Id = "MainArea",
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                _documentDock,
                new ProportionalDockSplitter(),
                bottomDock
            )
        };
        var mainArea = _mainArea;

        // Root layout
        var rootLayout = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter(),
                mainArea
            )
        };

        _rootDock = CreateRootDock();
        _rootDock.Id = "Root";
        _rootDock.Title = "Root";
        _rootDock.ActiveDockable = rootLayout;
        _rootDock.DefaultDockable = rootLayout;
        _rootDock.VisibleDockables = CreateList<IDockable>(rootLayout);

        return _rootDock;
    }

    public override void InitLayout(IDockable layout)
    {
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            ["SolutionExplorer"] = () => _solutionExplorer,
            ["Output"] = () => _outputPanel,
            ["ErrorList"] = () => _errorList,
            ["CallStack"] = () => _callStack,
            ["Variables"] = () => _variables,
            ["Breakpoints"] = () => _breakpoints,
            ["FindInFiles"] = () => _findInFiles,
            ["Terminal"] = () => _terminal,
            ["GitChanges"] = () => _gitChanges,
            ["GitBranches"] = () => _gitBranches,
            ["GitStash"] = () => _gitStash,
            ["GitBlame"] = () => _gitBlame,
            ["Watch"] = () => _watch,
            ["DocumentOutline"] = () => _documentOutline,
            ["Bookmarks"] = () => _bookmarks,
            ["Extensions"] = () => _extensions,
            ["Problems"] = () => _problems,
            ["DebugConsole"] = () => _debugConsole,
            ["Threads"] = () => _threads,
            ["Timeline"] = () => _timeline,
            ["CallHierarchy"] = () => _callHierarchy
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock,
            ["DocumentDock"] = () => _documentDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            // Real Avalonia floating-window host so a panel can be popped out into its own window and
            // dragged back / re-docked. (The old no-op stub extracted the panel into a non-presenting
            // window, making it vanish — that's the bug we hit before.) Docking works via the dock
            // adorner arrows; floating is the drop-onto-empty-space path.
            // Register with ThemeManager so a floated panel inherits the High-Contrast class before it
            // renders (the global Loaded hook also covers it, but this stamps it at creation time).
            [nameof(IDockWindow)] = () =>
            {
                var hostWindow = new global::Dock.Avalonia.Controls.HostWindow();
                VisualGameStudio.Shell.ThemeManager.Register(hostWindow);
                return hostWindow;
            }
        };

        base.InitLayout(layout);

        // base.InitLayout wires Owner on every nested dockable but leaves their Factory null.
        // Dock's drag/drop executes each move through sourceDockableOwner.Factory / targetDock.Factory,
        // so a null Factory makes every drop a silent no-op — panels can't be moved or repositioned.
        // Assign the factory across the whole tree so docking works.
        WireFactory(layout);
    }

    /// <summary>
    /// Sets <see cref="IDockable.Factory"/> to this factory on every dockable in the tree. Required
    /// for drag/drop reposition to work (see <see cref="InitLayout"/>).
    /// </summary>
    private void WireFactory(IDockable dockable)
    {
        dockable.Factory = this;
        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                WireFactory(child);
            }
        }
    }

    /// <summary>
    /// Toggles the bottom panel between maximized (fills entire content area) and normal layout.
    /// When maximized, the left sidebar and document area are collapsed; the bottom panel fills the space.
    /// </summary>
    /// <returns>True if the bottom panel is now maximized, false if restored.</returns>
    public bool ToggleBottomPanelMaximize()
    {
        if (_leftDock == null || _documentDock == null || _bottomDock == null || _mainArea == null)
            return false;

        if (_isBottomPanelMaximized)
        {
            // Restore saved proportions
            _leftDock.Proportion = _savedLeftProportion;
            _documentDock.Proportion = _savedDocumentProportion;
            _bottomDock.Proportion = _savedBottomProportion;
            _isBottomPanelMaximized = false;
        }
        else
        {
            // Save current proportions
            _savedLeftProportion = _leftDock.Proportion;
            _savedDocumentProportion = _documentDock.Proportion;
            _savedBottomProportion = _bottomDock.Proportion;

            // Maximize: collapse sidebar and documents, expand bottom panel
            _leftDock.Proportion = 0.001;
            _documentDock.Proportion = 0.001;
            _bottomDock.Proportion = 0.998;
            _isBottomPanelMaximized = true;
        }

        return _isBottomPanelMaximized;
    }

    /// <summary>
    /// Gets whether the bottom panel is currently maximized.
    /// </summary>
    public bool IsBottomPanelMaximized => _isBottomPanelMaximized;

    /// <summary>
    /// Restores the bottom panel from maximized state if it is currently maximized.
    /// </summary>
    public void RestoreBottomPanelIfMaximized()
    {
        if (_isBottomPanelMaximized)
        {
            ToggleBottomPanelMaximize();
        }
    }

    public override void CloseDockable(IDockable dockable)
    {
        // Fire DocumentClosed event before closing if it's a code editor document
        if (dockable is CodeEditorDocument editorDoc && editorDoc.ViewModel?.FilePath != null)
        {
            DocumentClosed?.Invoke(this, editorDoc.ViewModel.FilePath);
        }

        base.CloseDockable(dockable);
    }

    public void AddDocument(CodeEditorDocumentViewModel document)
    {
        if (_documentDock == null) return;

        var editorDocument = new CodeEditorDocument
        {
            Id = document.Id,
            Title = document.Title,
            ViewModel = document
        };

        AddDockable(_documentDock, editorDocument);
        SetActiveDockable(editorDocument);
        SetFocusedDockable(_documentDock, editorDocument);
    }

    public void ActivateDocument(CodeEditorDocumentViewModel document)
    {
        if (_documentDock?.VisibleDockables == null) return;

        var existingDoc = _documentDock.VisibleDockables
            .OfType<CodeEditorDocument>()
            .FirstOrDefault(d => d.ViewModel == document);

        if (existingDoc != null)
        {
            SetActiveDockable(existingDoc);
            SetFocusedDockable(_documentDock, existingDoc);
        }
    }

    public void AddWebViewDocument(WebViewDocumentViewModel viewModel)
    {
        if (_documentDock == null) return;

        var webViewDocument = new WebViewDocument
        {
            Id = viewModel.Id,
            Title = viewModel.Title,
            ViewModel = viewModel
        };

        AddDockable(_documentDock, webViewDocument);
        SetActiveDockable(webViewDocument);
        SetFocusedDockable(_documentDock, webViewDocument);
    }

    public void ActivateWebViewDocument(WebViewDocumentViewModel viewModel)
    {
        if (_documentDock?.VisibleDockables == null) return;

        var existingDoc = _documentDock.VisibleDockables
            .OfType<WebViewDocument>()
            .FirstOrDefault(d => d.ViewModel == viewModel);

        if (existingDoc != null)
        {
            SetActiveDockable(existingDoc);
            SetFocusedDockable(_documentDock, existingDoc);
        }
    }

    public object? GetActiveDocument()
    {
        return (_documentDock?.ActiveDockable as CodeEditorDocument)?.ViewModel;
    }

    public void CloseActiveDocument()
    {
        var activeDockable = _documentDock?.ActiveDockable;
        if (activeDockable != null)
        {
            CloseDockable(activeDockable);
        }
    }

    public IEnumerable<object> GetAllDocuments()
    {
        if (_documentDock?.VisibleDockables == null) return Enumerable.Empty<object>();

        return _documentDock.VisibleDockables
            .OfType<CodeEditorDocument>()
            .Select(d => d.ViewModel)
            .Where(vm => vm != null)!;
    }

    /// <summary>
    /// Maps each tool's stable Id to the Id of the <see cref="ToolDock"/> it lives in by default.
    /// Used to re-dock a tool that the user closed, so a View/Debug menu command can bring it back
    /// to a sensible place (mirrors the grouping in <see cref="CreateLayout"/>).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> _toolHomeDockId = new Dictionary<string, string>
    {
        // Left sidebar
        ["SolutionExplorer"] = "LeftTools",
        ["GitChanges"] = "LeftTools",
        ["GitBranches"] = "LeftTools",
        ["GitStash"] = "LeftTools",
        ["GitBlame"] = "LeftTools",
        ["Timeline"] = "LeftTools",
        ["DocumentOutline"] = "LeftTools",
        ["Bookmarks"] = "LeftTools",
        ["Extensions"] = "LeftTools",
        // Bottom-left group (general tools)
        ["Output"] = "BottomLeftTools",
        ["ErrorList"] = "BottomLeftTools",
        ["Problems"] = "BottomLeftTools",
        ["Terminal"] = "BottomLeftTools",
        ["FindInFiles"] = "BottomLeftTools",
        ["CallHierarchy"] = "BottomLeftTools",
        // Bottom-right group (debug tools)
        ["CallStack"] = "BottomRightTools",
        ["Variables"] = "BottomRightTools",
        ["Breakpoints"] = "BottomRightTools",
        ["Watch"] = "BottomRightTools",
        ["ImmediateWindow"] = "BottomRightTools",
        ["DebugConsole"] = "BottomRightTools",
        ["Threads"] = "BottomRightTools",
    };

    public void ActivateTool(string toolId)
    {
        if (_rootDock == null) return;

        var (tool, parent) = FindDockableWithParent(_rootDock, toolId, null);

        // If the tool isn't in the layout, the user closed it — recreate and re-dock it so the
        // menu command actually reopens the panel instead of silently doing nothing.
        if (tool == null)
        {
            (tool, parent) = ReopenClosedTool(toolId);
            if (tool == null) return;
        }

        // Set as active in parent dock first (this switches the tab)
        if (parent is IDock parentDock)
        {
            parentDock.ActiveDockable = tool;
        }
        SetActiveDockable(tool);
        SetFocusedDockable(_rootDock, tool);
    }

    /// <summary>
    /// Recreates a previously-closed tool from the factory map and docks it into its home
    /// <see cref="ToolDock"/> (rebuilding the home region if the user had closed it away). Returns
    /// the new tool and its parent dock, or (null, null) if the tool id is unknown.
    /// </summary>
    private (IDockable?, IDock?) ReopenClosedTool(string toolId)
    {
        if (_rootDock == null) return (null, null);
        if (!GetToolFactoryMap().TryGetValue(toolId, out var make)) return (null, null);

        var host = FindHomeToolDock(toolId);
        var tool = make();
        AddDockable(host, tool);
        return (tool, host);
    }

    /// <summary>
    /// Returns the <see cref="ToolDock"/> a reopened tool should go into: its mapped home dock if it
    /// still exists, otherwise the home dock and its parent region are recreated. Repeatedly closing
    /// panels empties and removes their ToolDocks — then their parent ProportionalDocks — so a reopen
    /// has to rebuild the missing region, or panels "disappear and won't come back".
    /// </summary>
    private IToolDock FindHomeToolDock(string toolId)
    {
        var homeId = _toolHomeDockId.TryGetValue(toolId, out var mapped) ? mapped : "BottomLeftTools";

        if (FindDockableById(_rootDock!, homeId) is IToolDock existing)
            return existing;

        return homeId == "LeftTools"
            ? CreateToolDockIn(EnsureLeftRegion(), homeId, Alignment.Left, insertFirst: false)
            : CreateToolDockIn(EnsureBottomRegion(), homeId, Alignment.Bottom, insertFirst: homeId == "BottomLeftTools");
    }

    /// <summary>The single ProportionalDock directly under the root (the horizontal Left|Main split).</summary>
    private ProportionalDock RootLayout()
    {
        if (_rootDock?.VisibleDockables != null)
        {
            foreach (var d in _rootDock.VisibleDockables)
                if (d is ProportionalDock p)
                    return p;
        }
        throw new InvalidOperationException("Root layout ProportionalDock is missing.");
    }

    /// <summary>Returns the left sidebar container, recreating and re-attaching it to the root if it was removed.</summary>
    private ProportionalDock EnsureLeftRegion()
    {
        if (FindDockableById(_rootDock!, "LeftDock") is ProportionalDock live)
            return _leftDock = live;

        var leftDock = new ProportionalDock
        {
            Id = "LeftDock",
            Proportion = 0.2,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>()
        };
        _leftDock = leftDock;

        var rootLayout = RootLayout();
        InsertDockable(rootLayout, leftDock, 0);
        InsertDockable(rootLayout, new ProportionalDockSplitter(), 1);
        return leftDock;
    }

    /// <summary>Returns the bottom panel container, recreating and re-attaching it to the main area if it was removed.</summary>
    private ProportionalDock EnsureBottomRegion()
    {
        if (FindDockableById(_rootDock!, "BottomDock") is ProportionalDock live)
            return _bottomDock = live;

        var bottomDock = new ProportionalDock
        {
            Id = "BottomDock",
            Proportion = 0.35,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>()
        };
        _bottomDock = bottomDock;

        var mainArea = EnsureMainArea();
        AddDockable(mainArea, new ProportionalDockSplitter());
        AddDockable(mainArea, bottomDock);
        return bottomDock;
    }

    /// <summary>
    /// Returns the live MainArea, rebuilding it if the dock collapse removed it. Dock 11.3 collapses
    /// empty docks more aggressively than 11.1: after every panel is closed even MainArea is flattened
    /// away and the (non-collapsable) DocumentDock is hoisted into the root layout. The old fallback
    /// to the stale <c>_mainArea</c> field then added the rebuilt region to a DETACHED dock — invisible.
    /// Instead, rebuild MainArea around the DocumentDock: wrap the live DocumentDock back in a fresh
    /// vertical MainArea in the same slot of its current parent.
    /// </summary>
    private ProportionalDock EnsureMainArea()
    {
        if (FindDockableById(_rootDock!, "MainArea") is ProportionalDock live)
            return _mainArea = live;

        // DocumentDock is IsCollapsable=false, so it always survives somewhere in the live tree.
        var (docDockable, parent) = FindDockableWithParent(_rootDock!, "DocumentDock", null);
        if (docDockable is not DocumentDock documentDock || parent is not IDock parentDock || parentDock.VisibleDockables == null)
            throw new InvalidOperationException("DocumentDock is missing from the layout tree.");

        var mainArea = new ProportionalDock
        {
            Id = "MainArea",
            Orientation = Orientation.Vertical,
            Proportion = documentDock.Proportion,
            VisibleDockables = CreateList<IDockable>()
        };
        mainArea.Factory = this;
        _mainArea = mainArea;

        // Swap MainArea into the DocumentDock's slot, then re-nest the DocumentDock inside it.
        var index = parentDock.VisibleDockables.IndexOf(documentDock);
        RemoveDockable(documentDock, false);
        InsertDockable(parentDock, mainArea, Math.Max(index, 0));
        AddDockable(mainArea, documentDock);
        documentDock.Proportion = 0.6; // its share within MainArea, as in CreateLayout
        return mainArea;
    }

    /// <summary>Creates a fresh empty ToolDock with the given id and adds it into <paramref name="parent"/>.</summary>
    private IToolDock CreateToolDockIn(ProportionalDock parent, string id, Alignment alignment, bool insertFirst)
    {
        var toolDock = new ToolDock
        {
            Id = id,
            Alignment = alignment,
            Proportion = alignment == Alignment.Bottom ? 0.5 : double.NaN,
            GripMode = GripMode.Visible,
            VisibleDockables = CreateList<IDockable>()
        };

        var hasSiblings = parent.VisibleDockables is { Count: > 0 };
        if (insertFirst && hasSiblings)
        {
            InsertDockable(parent, toolDock, 0);
            InsertDockable(parent, new ProportionalDockSplitter(), 1);
        }
        else
        {
            if (hasSiblings)
                AddDockable(parent, new ProportionalDockSplitter());
            AddDockable(parent, toolDock);
        }
        return toolDock;
    }

    private (IDockable?, IDock?) FindDockableWithParent(IDockable dockable, string id, IDock? parent)
    {
        if (dockable.Id == id) return (dockable, parent);

        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var (found, foundParent) = FindDockableWithParent(child, id, dock);
                if (found != null) return (found, foundParent);
            }
        }

        return (null, null);
    }

    private IDockable? FindDockableById(IDockable dockable, string id)
    {
        if (dockable.Id == id) return dockable;

        if (dockable is IDock dock && dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var found = FindDockableById(child, id);
                if (found != null) return found;
            }
        }

        return null;
    }

    /// <summary>The current root layout (for capturing/persisting state).</summary>
    public IRootDock? RootDock => _rootDock;

    /// <summary>
    /// Single source of truth mapping each tool's stable Id to a constructor that wires its
    /// view-model. Used both to rebuild a persisted layout (restoring each panel as its correct
    /// concrete subclass) and, indirectly, to keep restore in sync with CreateLayout's tool set.
    /// Keep this list aligned with the tools created in <see cref="CreateLayout"/>.
    /// </summary>
    public IReadOnlyDictionary<string, Func<IDockable>> GetToolFactoryMap()
    {
        return new Dictionary<string, Func<IDockable>>
        {
            ["SolutionExplorer"] = () => new SolutionExplorerTool { Id = "SolutionExplorer", Title = "Solution Explorer", ViewModel = _solutionExplorer },
            ["Output"] = () => new OutputTool { Id = "Output", Title = "Output", ViewModel = _outputPanel },
            ["ErrorList"] = () => new ErrorListTool { Id = "ErrorList", Title = "Error List", ViewModel = _errorList },
            ["GitChanges"] = () => new GitChangesTool { Id = "GitChanges", Title = "Git Changes", ViewModel = _gitChanges },
            ["GitBranches"] = () => new GitBranchesTool { Id = "GitBranches", Title = "Branches", ViewModel = _gitBranches },
            ["GitStash"] = () => new GitStashTool { Id = "GitStash", Title = "Stash", ViewModel = _gitStash },
            ["GitBlame"] = () => new GitBlameTool { Id = "GitBlame", Title = "Blame", ViewModel = _gitBlame },
            ["DocumentOutline"] = () => new DocumentOutlineTool { Id = "DocumentOutline", Title = "Outline", ViewModel = _documentOutline },
            ["Bookmarks"] = () => new BookmarksTool { Id = "Bookmarks", Title = "Bookmarks", ViewModel = _bookmarks },
            ["Extensions"] = () => new ExtensionsTool { Id = "Extensions", Title = "Extensions", ViewModel = _extensions },
            ["Timeline"] = () => new TimelineTool { Id = "Timeline", Title = "Timeline", ViewModel = _timeline },
            ["CallStack"] = () => new CallStackTool { Id = "CallStack", Title = "Call Stack", ViewModel = _callStack },
            ["Variables"] = () => new VariablesTool { Id = "Variables", Title = "Variables", ViewModel = _variables },
            ["Breakpoints"] = () => new BreakpointsTool { Id = "Breakpoints", Title = "Breakpoints", ViewModel = _breakpoints },
            ["FindInFiles"] = () => new FindInFilesTool { Id = "FindInFiles", Title = "Find in Files", ViewModel = _findInFiles },
            ["Terminal"] = () => new TerminalTool { Id = "Terminal", Title = "Terminal", ViewModel = _terminal },
            ["Watch"] = () => new WatchTool { Id = "Watch", Title = "Watch", ViewModel = _watch },
            ["ImmediateWindow"] = () => new ImmediateWindowTool { Id = "ImmediateWindow", Title = "Immediate", ViewModel = _immediateWindow },
            ["Problems"] = () => new ProblemsTool { Id = "Problems", Title = "Problems", ViewModel = _problems },
            ["DebugConsole"] = () => new DebugConsoleTool { Id = "DebugConsole", Title = "Debug Console", ViewModel = _debugConsole },
            ["Threads"] = () => new ThreadsTool { Id = "Threads", Title = "Threads", ViewModel = _threads },
            ["CallHierarchy"] = () => new CallHierarchyTool { Id = "CallHierarchy", Title = "Call Hierarchy", ViewModel = _callHierarchy }
        };
    }

    /// <summary>Captures the current layout tree as a serializable DTO (documents excluded).</summary>
    public Core.Models.DockNode? SerializeCurrentLayout()
    {
        return new DockLayoutSerializer().Capture(_rootDock);
    }

    /// <summary>
    /// Rebuilds the layout from a persisted DTO and swaps it in, re-wiring the internal dock
    /// references the rest of the factory depends on. Returns the new root, or null if the DTO
    /// couldn't be reconstructed (caller keeps the default layout).
    /// </summary>
    public IRootDock? TryApplyLayout(Core.Models.DockNode node)
    {
        var root = new DockLayoutSerializer().Rebuild(node, GetToolFactoryMap());
        if (root == null) return null;

        // A restored tree must contain the document area, or documents can't be opened.
        var documentDock = FindDockableById(root, "DocumentDock") as DocumentDock;
        if (documentDock == null) return null;

        _rootDock = root;
        _documentDock = documentDock;
        _leftDock = FindDockableById(root, "LeftDock") as ProportionalDock;
        _mainArea = FindDockableById(root, "MainArea") as ProportionalDock;
        _bottomDock = FindDockableById(root, "BottomDock") as ProportionalDock;
        _isBottomPanelMaximized = false;

        InitLayout(root);
        return root;
    }
}

// Tool and Document wrapper classes
public class SolutionExplorerTool : Tool
{
    public SolutionExplorerViewModel? ViewModel { get; set; }
}

public class OutputTool : Tool
{
    public OutputPanelViewModel? ViewModel { get; set; }
}

public class ErrorListTool : Tool
{
    public ErrorListViewModel? ViewModel { get; set; }
}

public class CodeEditorDocument : Document
{
    public CodeEditorDocumentViewModel? ViewModel { get; set; }
}

public class WelcomeDocument : Document
{
}

public class WebViewDocument : Document
{
    public WebViewDocumentViewModel? ViewModel { get; set; }
}

public class CallStackTool : Tool
{
    public CallStackViewModel? ViewModel { get; set; }
}

public class VariablesTool : Tool
{
    public VariablesViewModel? ViewModel { get; set; }
}

public class BreakpointsTool : Tool
{
    public BreakpointsViewModel? ViewModel { get; set; }
}

public class FindInFilesTool : Tool
{
    public FindInFilesViewModel? ViewModel { get; set; }
}

public class TerminalTool : Tool
{
    public TerminalViewModel? ViewModel { get; set; }
}

public class GitChangesTool : Tool
{
    public GitChangesViewModel? ViewModel { get; set; }
}

public class GitBranchesTool : Tool
{
    public GitBranchesViewModel? ViewModel { get; set; }
}

public class GitStashTool : Tool
{
    public GitStashViewModel? ViewModel { get; set; }
}

public class GitBlameTool : Tool
{
    public GitBlameViewModel? ViewModel { get; set; }
}

public class WatchTool : Tool
{
    public WatchViewModel? ViewModel { get; set; }
}

public class DocumentOutlineTool : Tool
{
    public DocumentOutlineViewModel? ViewModel { get; set; }
}

public class BookmarksTool : Tool
{
    public BookmarksViewModel? ViewModel { get; set; }
}

public class ImmediateWindowTool : Tool
{
    public ImmediateWindowViewModel? ViewModel { get; set; }
}

public class ExtensionsTool : Tool
{
    public ExtensionsViewModel? ViewModel { get; set; }
}

public class ProblemsTool : Tool
{
    public ProblemsViewModel? ViewModel { get; set; }
}

public class DebugConsoleTool : Tool
{
    public DebugConsoleViewModel? ViewModel { get; set; }
}

public class ThreadsTool : Tool
{
    public ThreadsViewModel? ViewModel { get; set; }
}

public class TimelineTool : Tool
{
    public TimelineViewModel? ViewModel { get; set; }
}

public class CallHierarchyTool : Tool
{
    public CallHierarchyViewModel? ViewModel { get; set; }
}

