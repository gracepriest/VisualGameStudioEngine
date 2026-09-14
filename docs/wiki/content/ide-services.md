title: Services and abstractions
lede: 44 interfaces in Core, ~60 implementations in ProjectSystem — the IDE's whole contract surface.
---
`VisualGameStudio.Core/Abstractions/Services/` declares what the IDE can do;
`VisualGameStudio.ProjectSystem/Services/` does it. The Shell only ever sees the
interfaces, which is why most of the IDE is testable without a window.

## Project and build

| Interface | Implementation | Role |
|---|---|---|
| `IProjectService` | `ProjectService.cs` | Load, save, modify projects |
| `ISolutionService` | `SolutionService.cs` | Solutions and project membership |
| `IProjectTemplateService` | `ProjectTemplateService.cs` | Solution types, templates, `.blproj` generation |
| `IBuildService` | `BuildService.cs` | Build orchestration; delegates to the CLI engine |
| `ITaskRunnerService` | `TaskRunnerService.cs` | User-defined tasks |
| `IWorkspaceService` | `WorkspaceService.cs` | Workspace-level operations |
| `IWorkspaceStateStore` | `WorkspaceStateStore.cs` | Per-project persisted state |
| `IRecentProjectsService` | `RecentProjectsService.cs` | Recent list on the welcome page |

> [trap] The IDE build **delegates to the CLI engine** (`CompileProjectFiles`). A compiler
> fix verified only through a unit-test helper can still break through the IDE or the CLI.
> Exercise both entry points.

## Language and editing

| Interface | Implementation | Role |
|---|---|---|
| `ILanguageService` | `LanguageService.cs` | LSP client |
| `ILanguageServiceRegistry` | `LanguageServiceRegistry.cs` | Which server serves which language |
| `ICodeFormattingService` | `CodeFormattingService.cs` | Formatting entry points |
| `ICodeAnalysisService` | `CodeAnalysisService.cs` | Code smells, security issues, complexity |
| `ICodeMetricsService` | `CodeMetricsService.cs` | Metrics |
| `IRefactoringService` | `RefactoringService.cs` | The refactoring catalogue |
| `ISymbolSearchService` | `SymbolSearchService.cs` | Symbol lookup |
| `ISnippetService` | `SnippetService.cs` | Snippet expansion |
| `ITextMateService` | `TextMateService.cs` | Grammar-based highlighting |
| `IIntelliSenseEmissionService` | `IntelliSenseEmissionService.cs` | Emits artefacts IntelliSense needs |

## Files, search, navigation

| Interface | Implementation |
|---|---|
| `IFileService` | `FileService.cs` |
| `IFileWatcherService` | `FileWatcherService.cs` |
| `ISearchService` | `SearchService.cs`, `FileSearchService.cs` |
| `IFindReplaceService` | `FindReplaceService.cs` |
| `INavigationService` | `NavigationService.cs` |
| `IBookmarkService` | `BookmarkService.cs` |
| `IAutoSaveService` | `AutoSaveService.cs` |
| `IHotExitService` | `HotExitService.cs` |

## Debugging

| Interface | Implementation |
|---|---|
| `IDebugService` | `DebugService.cs` |
| `IDebugAdapterRegistry` | `DebugAdapterRegistry.cs` |
| `ILaunchConfigurationService` | `LaunchConfigurationService.cs` |
| — | `DapSession.cs` — the protocol session |

Protocol types are in `VisualGameStudio.Core/DAP/DapProtocol.cs`;
`DebugAdapterDescriptor.cs` and `LanguageServerDescriptor.cs` describe registrable
servers and adapters.

## Native toolchain

| Interface | Implementation | Role |
|---|---|---|
| `ICppToolchainProbe` | `CppToolchainProbeService.cs` | Discover clang / gcc / MSVC |
| — | `CppToolchainOverrides.cs` | Per-backend pinned compiler and debugger paths |
| — | `ToolchainPathValidator.cs` | Validates a pinned path, fails loudly on a bad one |
| — | `ClangdInstaller.cs`, `ClangdLocator.cs` | On-demand clangd |
| — | `LldbDapInstaller.cs`, `LldbDapLocator.cs` | On-demand lldb-dap |
| — | `FileDownloader.cs`, `SafeZip` (compiler side) | Acquisition plumbing |

## Environment and platform

| Interface | Implementation |
|---|---|
| `ISettingsService` | `SettingsService.cs` (+ `SettingsConsumerRegistry.cs`) |
| `IKeybindingService` | `KeybindingService.cs` |
| `ICommandService` | Command registration for the palette |
| `IOutputService` | `OutputService.cs` |
| `ITerminalService` | `TerminalService.cs` (+ `ShellProfileDetector.cs`) |
| `IGitService` | `GitService.cs` (+ `GitAutoFetchService.cs`) |
| `ITaskListService` | `TaskListService.cs` — TODO/HACK comment scanning |
| `IDialogService` | Modal dialog hosting |

## Extensions

| Interface | Implementation |
|---|---|
| `IExtensionService` | `ExtensionService.cs`, `ExtensionManager.cs` |
| `IMarketplaceService` | `MarketplaceService.cs`, `OpenVsxClient.cs` |
| `IContributionService` | Extension contribution points |
| — | `ExtensionHost.cs` + `ExtensionHost/`, `ExtensionHostMain.js` |
| — | `VsixInstaller.cs`, `ExtensionFileSystem.cs`, `ExtensionWorkspace.cs` |

See [Extension host](#/ide-extensions).

## Adding a service

1. Declare the interface in `Core/Abstractions/Services/`.
2. Implement it in `ProjectSystem/Services/`.
3. Register it where the Shell composes its container.
4. Consume it from a view model by interface only.
5. Test the view model against a fake — that is the whole point of the seam.
