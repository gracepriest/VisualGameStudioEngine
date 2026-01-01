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
    private DocumentDock? _documentDock;
    private IRootDock? _rootDock;

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
        BookmarksViewModel? bookmarks = null)
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

        // Left tool dock (Solution Explorer, Git panels, Outline, Bookmarks)
        var leftDock = new ProportionalDock
        {
            Proportion = 0.2,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                new ToolDock
                {
                    VisibleDockables = CreateList<IDockable>(solutionExplorerTool, gitChangesTool, gitBranchesTool, gitStashTool, gitBlameTool, documentOutlineTool, bookmarksTool),
                    ActiveDockable = solutionExplorerTool,
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible
                }
            )
        };

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

        // Bottom tool dock - split into two groups for better tab visibility
        // Left group: General tools (Output, Error List, Terminal, Find)
        var bottomLeftTools = new ToolDock
        {
            Id = "BottomLeftTools",
            Title = "Output",
            Proportion = 0.5,
            VisibleDockables = CreateList<IDockable>(outputTool, errorListTool, terminalTool, findInFilesTool),
            ActiveDockable = outputTool,
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible
        };

        // Right group: Debug tools (Call Stack, Variables, Breakpoints, Watch, Immediate)
        var bottomRightTools = new ToolDock
        {
            Id = "BottomRightTools",
            Title = "Debug",
            Proportion = 0.5,
            VisibleDockables = CreateList<IDockable>(callStackTool, variablesTool, breakpointsTool, watchTool, immediateWindowTool),
            ActiveDockable = breakpointsTool,
            Alignment = Alignment.Bottom,
            GripMode = GripMode.Visible
        };

        var bottomDock = new ProportionalDock
        {
            Proportion = 0.35,
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                bottomLeftTools,
                new ProportionalDockSplitter(),
                bottomRightTools
            )
        };

        // Main content area (documents + bottom tools)
        var mainArea = new ProportionalDock
        {
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(
                _documentDock,
                new ProportionalDockSplitter(),
                bottomDock
            )
        };

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
            ["Bookmarks"] = () => _bookmarks
        };

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            ["Root"] = () => _rootDock,
            ["DocumentDock"] = () => _documentDock
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new HostWindow()
        };

        base.InitLayout(layout);
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

    public object? GetActiveDocument()
    {
        return (_documentDock?.ActiveDockable as CodeEditorDocument)?.ViewModel;
    }

    public void ActivateTool(string toolId)
    {
        if (_rootDock == null) return;

        var (tool, parent) = FindDockableWithParent(_rootDock, toolId, null);
        if (tool != null)
        {
            // Set as active in parent dock first (this switches the tab)
            if (parent is IDock parentDock)
            {
                parentDock.ActiveDockable = tool;
            }
            SetActiveDockable(tool);
            SetFocusedDockable(_rootDock, tool);
        }
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

public class HostWindow : IHostWindow
{
    public IDockWindow? Window { get; set; }
    public IDockManager? DockManager { get; set; }
    public IHostWindowState? HostWindowState { get; set; }
    public bool IsTracked { get; set; }

    public void Present(bool isDialog) { }
    public void Exit() { }
    public void SetPosition(double x, double y) { }
    public void GetPosition(out double x, out double y) { x = 0; y = 0; }
    public void SetSize(double width, double height) { }
    public void GetSize(out double width, out double height) { width = 800; height = 600; }
    public void SetTitle(string title) { }
    public void SetLayout(IDock layout) { }
    public void SetTopmost(bool topmost) { }
}
