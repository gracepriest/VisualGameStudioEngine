#!/usr/bin/env python3
"""
Visual Game Studio IDE Agent — Claude Agent SDK Application

An AI agent specialized for working with the Visual Game Studio IDE codebase.
Supports task profiles for different IDE subsystems: editor, shell, debugger,
project system, UI, refactoring, and code review.

Usage:
    python ide_agent.py "Fix the breakpoint margin rendering"
    python ide_agent.py --profile editor "Add word wrap toggle"
    python ide_agent.py --profile shell "Fix Solution Explorer refresh"
    python ide_agent.py --profile review
    python ide_agent.py --interactive
"""

import asyncio
import argparse
import os
import subprocess
import sys
from typing import Any

from claude_agent_sdk import (
    query,
    tool,
    create_sdk_mcp_server,
    ClaudeAgentOptions,
    AssistantMessage,
    ResultMessage,
    SystemMessage,
)

# ---------------------------------------------------------------------------
# Project paths
# ---------------------------------------------------------------------------
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CORE_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudio.Core")
EDITOR_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudio.Editor")
PROJECTSYSTEM_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudio.ProjectSystem")
SHELL_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudio.Shell")
TESTS_DIR = os.path.join(PROJECT_ROOT, "VisualGameStudio.Tests")
IDE_DIR = os.path.join(PROJECT_ROOT, "IDE")

# ---------------------------------------------------------------------------
# System prompt — deep IDE architecture knowledge
# ---------------------------------------------------------------------------
SYSTEM_PROMPT = """\
You are an expert developer working on the Visual Game Studio IDE, an Avalonia-based
code editor and project system for the BasicLang programming language.

## IDE Architecture — 4 Layers

### Layer 1: Core (VisualGameStudio.Core)
Service interfaces and models. NO implementations live here.
- 29 service interfaces in Abstractions/Services/:
  ILanguageService, IProjectService, IBuildService, IDebugService, IFileService,
  IRefactoringService, IFindReplaceService, ILspClientService, ISettingsService,
  IBookmarkService, IDialogService, IOutputService, ISnippetService,
  INavigationService, ICodeMetricsService, ICodeFormattingService,
  ISymbolSearchService, ITerminalService, ICodeAnalysisService, ITaskListService,
  ITextMateService, IDapClientService, IExtensionService, IMarketplaceService,
  IKeybindingService, IGitService, ISearchService, ICommandService,
  IProjectTemplateService
- ViewModels: ViewModelBase (CommunityToolkit.Mvvm), IDocumentViewModel
- Events: IEventAggregator (pub/sub), event records in Events.cs
- Models: DiagnosticItem, BasicLangProject, BasicLangSolution, BuildResult

### Layer 2: ProjectSystem (VisualGameStudio.ProjectSystem)
Concrete service implementations. 29 services in Services/:
- LanguageService: LSP client connecting to BasicLang.exe --lsp via stdin/stdout
- DebugService: DAP client connecting to BasicLang.exe debug via stdin/stdout JSON
- BuildService: Invokes BasicLang.exe build on .blproj files
- LspClientService: Generic multi-language LSP client infrastructure (12 languages)
- All other service implementations (FileService, GitService, etc.)

### Layer 3: Editor (VisualGameStudio.Editor)
Avalonia-based code editor built on AvaloniaEdit.
- Controls/CodeEditorControl.axaml.cs: Main editor control (~50 internal fields)
  Built on AvaloniaEdit TextEditor, adds folding, multi-cursor, completion, breakpoints
- Controls/InlineFindReplaceControl.axaml.cs: VS Code-style Ctrl+F/H bar
- Controls/MinimapControl.axaml.cs, BreadcrumbControl.axaml.cs
- TextMarkers/ (7 IBackgroundRenderer implementations):
  TextMarkerService, BracketHighlighter, CodeLensRenderer, InlayHintRenderer,
  InlineDebugValueRenderer, SearchHighlightRenderer, IndentationGuideRenderer
- Margins/: BookmarkMargin, BreakpointMargin (AbstractMargin subclasses)
- Folding/: BasicLangFoldingStrategy
- Completion/: completion data providers
- MultiCursor/: MultiCursorManager, MultiCursorRenderer, MultiCursorInputHandler

### Layer 4: Shell (VisualGameStudio.Shell)
Main application, MVVM ViewModels, Avalonia Views.
- MainWindowViewModel: orchestrator, injects services + panel VMs
- StatusBarViewModel: language, encoding, line endings, indentation indicators
- Documents/: CodeEditorDocumentViewModel, WelcomeDocumentViewModel
- Panels/ (17 VMs): SolutionExplorer, OutputPanel, ErrorList, CallStack, Variables,
  Breakpoints, FindInFiles, Terminal, Watch, ImmediateWindow, DocumentOutline,
  Bookmarks, CallHierarchy, TypeHierarchy, GitChanges, GitBranches, GitStash, GitBlame
- Dialogs/ (40+ VMs): refactoring, settings, project creation, debug config, etc.
- Views/: AXAML views for every ViewModel
- Configuration/ServiceConfiguration.cs: DI registration
- Dock/DockFactory: Dock.Avalonia panel layout

## Framework Knowledge
- UI: Avalonia 11.x (cross-platform .NET, NOT WPF)
- Editor: AvaloniaEdit (port of AvalonEdit)
- MVVM: CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand])
- DI: Microsoft.Extensions.DependencyInjection (singletons for services)
- Docking: Dock.Avalonia (dock panels, tool windows)
- Events: Custom IEventAggregator (pub/sub via Publish<T>/Subscribe<T>)
- LSP: JSON-RPC over stdin/stdout to BasicLang.exe --lsp
- DAP: JSON over stdin/stdout to BasicLang.exe debug adapter

## Key Patterns
- ViewModels use [ObservableProperty] for bindable props
- Commands use [RelayCommand] attribute
- Services registered as singletons in ServiceConfiguration.cs
- Events flow via IEventAggregator for cross-VM communication
- Renderers implement AvaloniaEdit's IBackgroundRenderer
- Margins implement AbstractMargin for gutter areas
- Views bind to VMs via DataContext in AXAML

## Build Commands
- Build IDE: dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
- Run tests: dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
- Run IDE: ./IDE/VisualGameStudio.exe

## Coding Standards
- Follow existing C# patterns in the codebase
- Service changes: interface in Core, implementation in ProjectSystem
- New panels: ViewModel in Shell/ViewModels/Panels, View in Shell/Views/Panels
- New dialogs: ViewModel in Shell/ViewModels/Dialogs, View in Shell/Views/Dialogs
- Register new services/VMs in ServiceConfiguration.cs
- Editor renderers go in Editor/TextMarkers/
- Run tests after changes: dotnet test
"""

# ---------------------------------------------------------------------------
# Task profiles — 7 domain-specific configurations
# ---------------------------------------------------------------------------
TASK_PROFILES: dict[str, dict[str, Any]] = {
    "editor": {
        "description": "Code editor: CodeEditorControl, renderers, margins, multi-cursor, folding",
        "system_prompt_suffix": """
Focus on the code editor subsystem in VisualGameStudio.Editor/:
- CodeEditorControl.axaml.cs: Main editor with ~50 internal fields
  Built on AvaloniaEdit TextEditor, adds folding, multi-cursor, completion, breakpoints
- TextMarkers/: 7 IBackgroundRenderer implementations:
  TextMarkerService (squiggly underlines), BracketHighlighter (matching brackets),
  CodeLensRenderer (clickable annotations), InlayHintRenderer (type/param hints),
  InlineDebugValueRenderer (debug variable values), SearchHighlightRenderer (search matches),
  IndentationGuideRenderer (vertical indent guides)
- Margins/: BookmarkMargin, BreakpointMargin (AbstractMargin subclasses)
- Controls/: MinimapControl, BreadcrumbControl, InlineFindReplaceControl
- Folding/: BasicLangFoldingStrategy
- Completion/: completion data providers for IntelliSense popup
- MultiCursor/: MultiCursorManager, MultiCursorRenderer, MultiCursorInputHandler

Renderers use AvaloniaEdit's Draw(TextView, DrawingContext) to paint behind/over text.
Margins use OnTextViewChanged/OnDocumentChanged lifecycle.
CodeEditorControl wires everything together in OnApplyTemplate/OnAttachedToVisualTree.
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Analyze the code editor for bugs, missing features, or rendering issues.",
    },
    "shell": {
        "description": "Shell: MainWindow, panel ViewModels, dialog ViewModels, MVVM patterns",
        "system_prompt_suffix": """
Focus on the IDE shell in VisualGameStudio.Shell/:
- MainWindowViewModel: orchestrator VM, injects services + panel VMs
  Manages document lifecycle, debug state, menu commands, event subscriptions
- StatusBarViewModel: language, encoding, line endings, indentation indicators
- Documents/: CodeEditorDocumentViewModel (text, caret, dirty state, 30+ events),
  WelcomeDocumentViewModel
- Panels/ (17 VMs): SolutionExplorer, OutputPanel, ErrorList, CallStack, Variables,
  Breakpoints, FindInFiles, Terminal, Watch, ImmediateWindow, DocumentOutline,
  Bookmarks, CallHierarchy, TypeHierarchy, GitChanges, GitBranches, GitStash, GitBlame
- Dialogs/ (40+ VMs): refactoring dialogs, settings, project creation, breakpoint
  conditions, exception settings, command palette, find/replace, etc.
- Views/: Corresponding .axaml for each VM
- Configuration/ServiceConfiguration.cs: DI registration
- Dock/DockFactory: panel layout system

Pattern: [ObservableProperty] for props, [RelayCommand] for commands,
IEventAggregator for cross-VM communication, IDialogService for dialog display.
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep", "Agent"],
        "default_prompt": "Review the shell architecture for MVVM violations or missing features.",
    },
    "debugger": {
        "description": "Debugging: DebugService, DAP protocol, debug panels (Variables, Watch, CallStack)",
        "system_prompt_suffix": """
Focus on the IDE debugging subsystem:
- IDebugService (Core): 20+ methods (Start/Stop/Step/Continue/Pause, breakpoints,
  variables, evaluate, data breakpoints, exception breakpoints, restart, set next statement)
- DebugService (ProjectSystem): DAP client over stdin/stdout JSON to BasicLang.exe
  States: NotStarted -> Initializing -> Running <-> Paused -> Stopped
  Events: StateChanged, Stopped, OutputReceived, BreakpointsChanged
- Debug Panel VMs (Shell):
  CallStackViewModel: stack frames display
  VariablesViewModel: locals/globals tree view
  WatchViewModel: user expressions evaluation
  BreakpointsViewModel: breakpoint list with conditions
  ImmediateWindowViewModel: REPL-style expression evaluation
- Debug-related dialogs:
  BreakpointConditionDialogViewModel, FunctionBreakpointDialogViewModel,
  ExceptionSettingsViewModel, LaunchConfigurationDialogViewModel
- Editor integration: InlineDebugValueRenderer (values next to variables),
  BreakpointMargin (gutter), DataTipPopup (hover evaluation)

DAP flow: IDE -> DebugService -> stdin/stdout JSON -> BasicLang.exe DebugSession -> DebuggableInterpreter
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Audit the debug subsystem for DAP protocol compliance and missing features.",
    },
    "project": {
        "description": "Project system: ProjectService, BuildService, solution management, templates",
        "system_prompt_suffix": """
Focus on the project system in VisualGameStudio.ProjectSystem/:
- IProjectService/ProjectService: open/close/create projects, manage files
- IBuildService/BuildService: invoke BasicLang.exe build, parse diagnostics
- IProjectTemplateService/ProjectTemplateService: project/item templates
- IFileService/FileService: file I/O operations
- SolutionExplorerViewModel: tree view of project files
- CreateProjectViewModel, NewProjectViewModel: project creation dialogs
- BuildConfigurationDialogViewModel: Debug/Release configuration

Project files: .blproj (XML format), .bas source files
Build: BasicLang.exe build MyProject.blproj -> diagnostics -> ErrorList
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review project system for robustness and missing project management features.",
    },
    "ui": {
        "description": "Avalonia UI: AXAML views, styling, theming, layout, controls",
        "system_prompt_suffix": """
Focus on the Avalonia UI layer:
- Views/MainWindow.axaml: main window layout with Dock panels
- Views/Documents/: CodeEditorDocumentView.axaml (editor tab)
- Views/Panels/: 17 panel views (SolutionExplorer, ErrorList, Terminal, etc.)
- Views/Dialogs/: 40+ dialog views (refactoring, settings, project creation)
- Views/Controls/: DataTipPopup, PeekDefinitionControl
- Editor controls in VisualGameStudio.Editor/Controls/

Avalonia specifics:
- Uses AXAML (like XAML but for Avalonia)
- StyledProperty/DirectProperty for custom controls
- DataTemplates for VM-to-View mapping
- Dock.Avalonia for panel layout (IRootDock, IDocumentDock, IToolDock)
- Themes: FluentTheme (light/dark)
- Key namespaces: xmlns:vm, xmlns:views, xmlns:controls
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Review UI views for layout issues, accessibility, and Avalonia best practices.",
    },
    "refactoring": {
        "description": "Refactoring: 28+ dialogs, code actions, LSP integration",
        "system_prompt_suffix": """
Focus on the refactoring subsystem:
- IRefactoringService/RefactoringService: code transformation operations
- ILanguageService code actions: GetCodeActionsAsync, RenameAsync, FormatDocumentAsync
- Refactoring dialogs (Shell/ViewModels/Dialogs/):
  RenameDialog, ExtractMethod, InlineMethod, IntroduceVariable, ExtractConstant,
  InlineConstant, ChangeSignature, EncapsulateField, InlineField,
  MoveTypeToFile, ExtractInterface, GenerateConstructor, ImplementInterface,
  OverrideMethod, SafeDelete, PullMembersUp, PushMembersDown,
  UseBaseType, ConvertToInterface, AddParameter, RemoveParameter,
  ReorderParameters, RenameParameter, ChangeParameterType,
  MakeParameterOptional, MakeParameterRequired, ConvertToNamedArguments,
  ConvertToPositionalArguments, InlineVariable

Each dialog VM provides preview, confirmation, and workspace edit application.
Connected to LSP via CodeActionInfo.Edit (WorkspaceEditInfo).
""",
        "allowed_tools": ["Read", "Edit", "Write", "Bash", "Glob", "Grep"],
        "default_prompt": "Audit refactoring dialogs for completeness and proper LSP integration.",
    },
    "review": {
        "description": "Code review: read-only analysis of IDE code quality",
        "system_prompt_suffix": """
Perform a thorough code review of the IDE codebase. Look for:
- MVVM violations (code-behind doing business logic, VMs accessing views)
- Memory leaks (event subscriptions not unsubscribed, IDisposable not disposed)
- Thread safety (accessing UI from background, shared state without locks)
- Null reference risks (missing null checks on service results)
- Resource leaks (processes, streams, LSP/DAP connections not cleaned up)
- Async issues (sync-over-async, missing ConfigureAwait, fire-and-forget)
- DI issues (incorrect lifetime, circular dependencies)
- Avalonia-specific: wrong thread for UI updates (use Dispatcher.UIThread)
Do NOT suggest style-only changes. Focus on real bugs.
""",
        "allowed_tools": ["Read", "Glob", "Grep"],
        "default_prompt": "Review the IDE for bugs, memory leaks, and architecture issues.",
    },
}

# ---------------------------------------------------------------------------
# Custom MCP tools — IDE-specific operations
# ---------------------------------------------------------------------------

@tool(
    "build_ide",
    "Build the Visual Game Studio IDE",
    {
        "type": "object",
        "properties": {
            "configuration": {
                "type": "string",
                "description": "Build configuration: Debug or Release (default: Release)",
            },
        },
    },
)
async def build_ide(args: dict[str, Any]) -> dict[str, Any]:
    """Build the IDE project."""
    config = args.get("configuration", "Release")
    csproj = os.path.join(SHELL_DIR, "VisualGameStudio.Shell.csproj")

    if not os.path.exists(csproj):
        return _text_result(f"Project not found at {csproj}")

    proc = await asyncio.create_subprocess_exec(
        "dotnet", "build", csproj, "-c", config,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await proc.communicate()
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "run_tests",
    "Run the IDE test suite (1636 xUnit tests)",
    {
        "type": "object",
        "properties": {
            "filter": {
                "type": "string",
                "description": "Optional test filter expression (e.g., 'FullyQualifiedName~Editor')",
            },
            "category": {
                "type": "string",
                "description": "Test category: all, editor, services, core, compiler, lsp, integration",
            },
        },
    },
)
async def run_tests(args: dict[str, Any]) -> dict[str, Any]:
    """Run the project test suite."""
    category_filters = {
        "editor": "FullyQualifiedName~Editor",
        "services": "FullyQualifiedName~Services",
        "core": "FullyQualifiedName~Core",
        "compiler": "FullyQualifiedName~Compiler",
        "lsp": "FullyQualifiedName~LSP",
        "integration": "FullyQualifiedName~Integration",
    }

    cmd = [
        "dotnet", "test",
        os.path.join(TESTS_DIR, "VisualGameStudio.Tests.csproj"),
        "-c", "Release", "--no-build", "--verbosity", "minimal",
    ]

    test_filter = args.get("filter", "")
    category = args.get("category", "all")

    if test_filter:
        cmd.extend(["--filter", test_filter])
    elif category and category != "all" and category in category_filters:
        cmd.extend(["--filter", category_filters[category]])

    proc = await asyncio.create_subprocess_exec(
        *cmd,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=PROJECT_ROOT,
    )
    stdout, stderr = await proc.communicate()
    output = stdout.decode("utf-8", errors="replace")
    errors = stderr.decode("utf-8", errors="replace")

    result = f"Exit code: {proc.returncode}\n{output}"
    if errors:
        result += f"\nErrors:\n{errors}"
    return _text_result(result)


@tool(
    "run_ide",
    "Launch the Visual Game Studio IDE executable",
    {
        "type": "object",
        "properties": {
            "wait": {
                "type": "boolean",
                "description": "Wait for IDE to exit (default: false)",
            },
        },
    },
)
async def run_ide(args: dict[str, Any]) -> dict[str, Any]:
    """Launch the IDE."""
    exe = os.path.join(IDE_DIR, "VisualGameStudio.exe")

    if not os.path.exists(exe):
        return _text_result(f"IDE executable not found at {exe}. Build the IDE first.")

    wait = args.get("wait", False)

    if wait:
        proc = await asyncio.create_subprocess_exec(
            exe,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            cwd=IDE_DIR,
        )
        stdout, stderr = await proc.communicate()
        output = stdout.decode("utf-8", errors="replace")
        return _text_result(f"IDE exited with code {proc.returncode}\n{output}")
    else:
        subprocess.Popen(
            [exe],
            cwd=IDE_DIR,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return _text_result(f"IDE launched: {exe}")


@tool(
    "list_services",
    "List all IDE service interfaces with their implementations",
    {"type": "object", "properties": {}},
)
async def list_services(args: dict[str, Any]) -> dict[str, Any]:
    """Return the complete service map."""
    return _text_result("""\
IDE Service Architecture (29 services):

Interface (Core)                  -> Implementation (ProjectSystem)  | Description
----------------------------------|----------------------------------|---------------------
ILanguageService                  -> LanguageService                 | LSP client (BasicLang.exe --lsp)
IProjectService                   -> ProjectService                  | Project/solution management
IBuildService                     -> BuildService                    | Compile via BasicLang.exe build
IDebugService                     -> DebugService                    | DAP client for debugging
IFileService                      -> FileService                     | File I/O operations
IRefactoringService               -> RefactoringService              | Code refactoring operations
IFindReplaceService               -> FindReplaceService              | Find/replace in files
ILspClientService                 -> LspClientService                | Generic multi-language LSP
IDapClientService                 -> DapClientService                | Generic DAP client
ISettingsService                  -> SettingsService                 | User preferences
IBookmarkService                  -> BookmarkService                 | Code bookmarks
IDialogService                    -> DialogService (Shell)           | Dialog display
IOutputService                    -> OutputService                   | Output panel logging
ISnippetService                   -> SnippetService                  | Code snippets
INavigationService                -> NavigationService               | File/symbol navigation
ICodeMetricsService               -> CodeMetricsService              | LOC, complexity metrics
ICodeFormattingService            -> CodeFormattingService            | Code formatting
ISymbolSearchService              -> SymbolSearchService             | Symbol search
ITerminalService                  -> TerminalService                 | Integrated terminal
ICodeAnalysisService              -> CodeAnalysisService             | Static analysis
ITaskListService                  -> TaskListService                 | TODO/FIXME tracking
ITextMateService                  -> TextMateService                 | TextMate grammars
IExtensionService                 -> ExtensionService                | Extension management
IMarketplaceService               -> MarketplaceService              | Extension marketplace
IKeybindingService                -> KeybindingService               | Keyboard shortcuts
IGitService                       -> GitService                      | Git integration
ISearchService                    -> SearchService                   | Workspace search
ICommandService                   -> (not yet implemented)           | Command system
IProjectTemplateService           -> ProjectTemplateService          | Project templates

DI Registration: Shell/Configuration/ServiceConfiguration.cs
""")


@tool(
    "get_ide_architecture",
    "Get a detailed overview of the IDE architecture",
    {"type": "object", "properties": {}},
)
async def get_ide_architecture(args: dict[str, Any]) -> dict[str, Any]:
    """Return the architecture overview."""
    return _text_result("""\
Visual Game Studio IDE -- Architecture Overview

+-----------------------------------------------------------+
|                    Layer 4: SHELL                          |
|  MainWindowViewModel (orchestrator)                       |
|  17 Panel VMs | 40+ Dialog VMs | 2 Document VMs          |
|  StatusBarViewModel | DockFactory                         |
|  App.axaml.cs -> ServiceConfiguration (DI)                |
|  Views/ (AXAML: MainWindow, Panels, Dialogs, Docs)       |
+-----------------------------------------------------------+
|                   Layer 3: EDITOR                         |
|  CodeEditorControl (AvaloniaEdit TextEditor)              |
|  7 Renderers: TextMarker, Bracket, CodeLens, InlayHint   |
|    InlineDebugValue, SearchHighlight, IndentationGuide    |
|  2 Margins: Bookmark, Breakpoint                         |
|  Multi-cursor | Folding | Completion | Minimap           |
+-----------------------------------------------------------+
|               Layer 2: PROJECT SYSTEM                     |
|  29 Service implementations                               |
|  LanguageService (LSP) | DebugService (DAP)               |
|  BuildService | ProjectService | GitService               |
|  LspClientService | DapClientService                      |
+-----------------------------------------------------------+
|                    Layer 1: CORE                          |
|  29 Service interfaces (ILanguageService, etc.)           |
|  Models (DiagnosticItem, CompletionItem, etc.)            |
|  Events (IEventAggregator, pub/sub)                       |
|  ViewModels (ViewModelBase, IDocumentViewModel)           |
+-----------------------------------------------------------+

Communication Patterns:
- Services: DI injection (constructor), all singletons
- Cross-VM: IEventAggregator.Publish<T>() / Subscribe<T>()
- LSP: LanguageService -> stdin/stdout -> BasicLang.exe --lsp
- DAP: DebugService -> stdin/stdout -> BasicLang.exe debug
- UI binding: [ObservableProperty] -> AXAML {Binding}
- Commands: [RelayCommand] -> AXAML Command={Binding}
""")


@tool(
    "list_viewmodels",
    "List all ViewModels with descriptions",
    {
        "type": "object",
        "properties": {
            "category": {
                "type": "string",
                "description": "Filter: panels, dialogs, documents, all (default: all)",
            },
        },
    },
)
async def list_viewmodels(args: dict[str, Any]) -> dict[str, Any]:
    """Return categorized ViewModel listing."""
    category = args.get("category", "all")

    panels = """\
Panel ViewModels (Shell/ViewModels/Panels/):
  SolutionExplorerViewModel    - Project tree view
  OutputPanelViewModel         - Build output
  ErrorListViewModel           - Diagnostics and errors
  CallStackViewModel           - Debug stack frames
  VariablesViewModel           - Debug locals/globals
  BreakpointsViewModel         - Breakpoint list with conditions
  FindInFilesViewModel         - Multi-file search results
  TerminalViewModel            - Embedded terminal
  WatchViewModel               - Debug watch expressions
  ImmediateWindowViewModel     - REPL debug console
  DocumentOutlineViewModel     - Document symbols
  BookmarksViewModel           - Code bookmarks
  CallHierarchyViewModel       - Call graph
  TypeHierarchyViewModel       - Type inheritance
  GitChangesViewModel          - Git staged/unstaged
  GitBranchesViewModel         - Git branch management
  GitStashViewModel            - Git stash entries
  GitBlameViewModel            - Line-by-line git blame
"""

    dialogs = """\
Dialog ViewModels (Shell/ViewModels/Dialogs/) -- grouped by function:

  Refactoring:
    RenameDialogViewModel, ExtractMethodDialogViewModel,
    InlineMethodDialogViewModel, IntroduceVariableDialogViewModel,
    ExtractConstantDialogViewModel, InlineConstantDialogViewModel,
    ChangeSignatureDialogViewModel, EncapsulateFieldDialogViewModel,
    InlineFieldDialogViewModel, MoveTypeToFileDialogViewModel,
    ExtractInterfaceDialogViewModel, SafeDeleteDialogViewModel,
    PullMembersUpDialogViewModel, PushMembersDownDialogViewModel,
    UseBaseTypeDialogViewModel, ConvertToInterfaceDialogViewModel,
    InlineVariableDialogViewModel

  Code Generation:
    GenerateConstructorDialogViewModel, ImplementInterfaceDialogViewModel,
    OverrideMethodDialogViewModel

  Parameters:
    AddParameterDialogViewModel, RemoveParameterDialogViewModel,
    ReorderParametersDialogViewModel, RenameParameterDialogViewModel,
    ChangeParameterTypeDialogViewModel, MakeParameterOptionalDialogViewModel,
    MakeParameterRequiredDialogViewModel, ConvertToNamedArgumentsDialogViewModel,
    ConvertToPositionalArgumentsDialogViewModel

  Navigation:
    GoToLineDialogViewModel, GoToSymbolDialogViewModel, QuickOpenDialogViewModel

  Debugging:
    BreakpointConditionDialogViewModel, FunctionBreakpointDialogViewModel,
    ExceptionSettingsViewModel, LaunchConfigurationDialogViewModel

  Settings:
    SettingsDialogViewModel, BuildConfigurationDialogViewModel

  Project:
    CreateProjectDialogViewModel, NewProjectViewModel

  Other:
    FindReplaceDialogViewModel, DiffViewerDialogViewModel,
    CommandPaletteViewModel
"""

    documents = """\
Document ViewModels (Shell/ViewModels/Documents/):
  CodeEditorDocumentViewModel  - Open file tab (text, caret, dirty state, 30+ events)
  WelcomeDocumentViewModel     - Welcome/start page tab
"""

    sections = {
        "panels": panels,
        "dialogs": dialogs,
        "documents": documents,
    }

    if category in sections:
        return _text_result(sections[category])

    return _text_result(panels + "\n" + dialogs + "\n" + documents)


def _text_result(text: str) -> dict[str, Any]:
    """Helper to create a text content result."""
    return {"content": [{"type": "text", "text": text}]}


# ---------------------------------------------------------------------------
# MCP server with all custom tools
# ---------------------------------------------------------------------------
ide_mcp_server = create_sdk_mcp_server(
    name="ide-tools",
    version="1.0.0",
    tools=[build_ide, run_tests, run_ide, list_services, get_ide_architecture, list_viewmodels],
)

# ---------------------------------------------------------------------------
# Agent runner
# ---------------------------------------------------------------------------

async def run_agent(
    prompt: str,
    profile_name: str | None = None,
    verbose: bool = False,
) -> str:
    """Run the IDE agent with the given prompt and optional task profile."""

    system = SYSTEM_PROMPT
    allowed_tools = ["Read", "Edit", "Write", "Bash", "Glob", "Grep"]

    if profile_name and profile_name in TASK_PROFILES:
        profile = TASK_PROFILES[profile_name]
        system += "\n" + profile["system_prompt_suffix"]
        allowed_tools = list(profile["allowed_tools"])
        if not prompt:
            prompt = profile["default_prompt"]
        if verbose:
            print(f"Using profile: {profile_name} -- {profile['description']}")

    custom_tool_names = [
        "mcp__ide-tools__build_ide",
        "mcp__ide-tools__run_tests",
        "mcp__ide-tools__run_ide",
        "mcp__ide-tools__list_services",
        "mcp__ide-tools__get_ide_architecture",
        "mcp__ide-tools__list_viewmodels",
    ]
    allowed_tools.extend(custom_tool_names)

    options = ClaudeAgentOptions(
        system_prompt=system,
        allowed_tools=allowed_tools,
        permission_mode="acceptEdits",
        cwd=PROJECT_ROOT,
        mcp_servers={
            "ide-tools": ide_mcp_server,
        },
    )

    result_text = ""

    async for message in query(prompt=prompt, options=options):
        if isinstance(message, AssistantMessage):
            for block in message.content:
                if getattr(block, "type", None) == "text":
                    if verbose:
                        print(f"Agent: {block.text}")
                elif getattr(block, "type", None) == "tool_use":
                    if verbose:
                        print(f"  [tool] {block.name}")

        elif isinstance(message, ResultMessage):
            if not message.is_error:
                result_text = message.result or ""
                if verbose:
                    print(f"\nResult:\n{result_text}")
            else:
                result_text = f"Error: {message.subtype}"
                print(f"Agent error: {message.subtype}", file=sys.stderr)

        elif isinstance(message, SystemMessage) and verbose:
            print(f"  [system] {message.subtype}: {message.data}")

    return result_text


# ---------------------------------------------------------------------------
# Interactive mode
# ---------------------------------------------------------------------------

async def interactive_mode(profile_name: str | None = None):
    """Run the agent in interactive loop mode."""
    print("Visual Game Studio IDE Agent -- Interactive Mode")
    print("Type 'quit' to exit, 'profile <name>' to switch profiles")
    print(f"Available profiles: {', '.join(TASK_PROFILES.keys())}")
    if profile_name:
        print(f"Active profile: {profile_name}")
    print("-" * 60)

    current_profile = profile_name

    while True:
        try:
            user_input = input("\n> ").strip()
        except (EOFError, KeyboardInterrupt):
            print("\nGoodbye!")
            break

        if not user_input:
            continue

        if user_input.lower() == "quit":
            break

        if user_input.lower().startswith("profile "):
            name = user_input.split(None, 1)[1].strip()
            if name in TASK_PROFILES:
                current_profile = name
                print(f"Switched to profile: {name}")
            else:
                print(f"Unknown profile. Available: {', '.join(TASK_PROFILES.keys())}")
            continue

        if user_input.lower() == "profiles":
            for name, info in TASK_PROFILES.items():
                marker = " *" if name == current_profile else ""
                print(f"  {name}{marker}: {info['description']}")
            continue

        await run_agent(user_input, profile_name=current_profile, verbose=True)


# ---------------------------------------------------------------------------
# CLI entry point
# ---------------------------------------------------------------------------

def main_cli():
    """CLI entry point."""
    parser = argparse.ArgumentParser(
        description="Visual Game Studio IDE Agent -- Claude Agent SDK",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Task Profiles:
  editor       Code editor: renderers, margins, multi-cursor, folding
  shell        Shell: MainWindow, panels, dialogs, MVVM patterns
  debugger     Debugging: DebugService, DAP, debug panels
  project      Project system: build, solution, templates
  ui           Avalonia UI: AXAML views, styling, theming
  refactoring  Refactoring: 28+ dialogs, code actions, LSP
  review       Code review: read-only quality analysis

Examples:
  %(prog)s "Fix the breakpoint margin rendering"
  %(prog)s --profile editor "Add word wrap toggle"
  %(prog)s --profile shell "Fix Solution Explorer refresh"
  %(prog)s --profile debugger "Fix step-over behavior"
  %(prog)s --profile review
  %(prog)s --interactive
  %(prog)s --interactive --profile editor
""",
    )

    parser.add_argument(
        "prompt",
        nargs="?",
        default="",
        help="Task description for the agent",
    )
    parser.add_argument(
        "--profile", "-p",
        choices=list(TASK_PROFILES.keys()),
        help="Task profile to use",
    )
    parser.add_argument(
        "--interactive", "-i",
        action="store_true",
        help="Run in interactive mode",
    )
    parser.add_argument(
        "--verbose", "-v",
        action="store_true",
        help="Show agent reasoning and tool calls",
    )
    parser.add_argument(
        "--list-profiles",
        action="store_true",
        help="List available task profiles",
    )

    args = parser.parse_args()

    if args.list_profiles:
        print("Available Task Profiles:\n")
        for name, info in TASK_PROFILES.items():
            print(f"  {name:12s}  {info['description']}")
        return

    if args.interactive:
        asyncio.run(interactive_mode(profile_name=args.profile))
        return

    if not args.prompt and not args.profile:
        parser.print_help()
        return

    result = asyncio.run(run_agent(
        args.prompt,
        profile_name=args.profile,
        verbose=args.verbose,
    ))

    if result and not args.verbose:
        print(result)


if __name__ == "__main__":
    main_cli()
