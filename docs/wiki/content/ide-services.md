title: Services and abstractions
lede: 42 public interfaces in `Core/Abstractions/Services`, 63 implementation files in ProjectSystem — the IDE's whole contract surface.
---
`VisualGameStudio.Core/Abstractions/Services/` declares what the IDE can do;
`VisualGameStudio.ProjectSystem/Services/` does it. The Shell only ever sees the
interfaces for most services, which is why most of the IDE is testable without a window. Nine
ProjectSystem types are registered by concrete class rather than behind an interface —
`ClangdInstaller`, `LldbDapInstaller`, `WebPreviewServer`, `RegenOnSaveCoordinator`,
`GitAutoFetchService`, `CppToolchainOverrides`, `OpenVsxClient`, `VsixInstaller`,
`FileSearchService` — and `IDialogService` is implemented in the Shell, not ProjectSystem.

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
| `ISearchService` | `SearchService.cs` — declared but unused; `FileSearchService.cs` is the live Find-in-Files engine and implements no interface |
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

Protocol types are in `VisualGameStudio.Core/Abstractions/Services/DapProtocol.cs`;
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
| `ISettingsService` | `SettingsService.cs` (+ `SettingsConsumerRegistry.cs`, which lives in Core next to the interfaces) |
| `IKeybindingService` | `KeybindingService.cs` |
| `ICommandService` | <span class="pill warn">no implementation</span> — declared only; injected as an optional `ICommandService?` into `ExtensionService` and `KeybindingService`, always null |
| `IOutputService` | `OutputService.cs` |
| `ITerminalService` | `TerminalService.cs` (+ `ShellProfileDetector.cs`) |
| `IGitService` | `GitService.cs` (+ `GitAutoFetchService.cs`) |
| `ITaskListService` | `TaskListService.cs` — comment scanning for TODO / FIXME / HACK / BUG / NOTE / UNDONE / XXX, each with a type and priority |
| `IDialogService` | `Shell/Services/DialogService.cs` — modal dialog hosting; the only Core service interface whose production implementation lives in the Shell rather than ProjectSystem |

## Extensions

| Interface | Implementation |
|---|---|
| `IExtensionService` | `ExtensionService.cs` (`ExtensionManager.cs` implements the separate `Core.Extensions.IExtensionManager` and is currently unreferenced) |
| `IMarketplaceService` | `MarketplaceService.cs`, `OpenVsxClient.cs` |
| `IContributionService` | <span class="pill warn">no implementation</span> — the contribution-point contract (commands, menus, keybindings, themes, snippets, languages, configuration) is declared in Core but nothing implements or consumes it yet |
| — | `ExtensionHost.cs` + `ExtensionHost/`, `ExtensionHostMain.js` |
| — | `VsixInstaller.cs`, `ExtensionFileSystem.cs`, `ExtensionWorkspace.cs` |

See [Extension host](#/ide-extensions).

## Unlisted helpers

Eight files in `ProjectSystem/Services/` back no interface of their own and appear in no
table above.

| File | Role |
|---|---|
| `WebPreviewServer.cs` | Loopback static-file server for previewing a built JavaScript project (`file://` is an opaque origin, so ES modules and source maps never load) |
| `RegenOnSaveCoordinator.cs` | `FileSavedEvent` (and external `.blproj` edits) → debounce → `IIntelliSenseEmissionService.RequestEmit`, so native C++ IntelliSense refreshes on save |
| `BlprojReferenceWriter.cs` | Idempotent in-place `<ProjectReference>` insertion into a `.blproj`; writes BOM-less UTF-8 |
| `SolutionWizardMapper.cs` | Pure translation of New Project wizard options to solution-creation shapes; no I/O |
| `VsCodeThemeLoader.cs` | Loads VS Code JSON colour themes (tokenColors, colors, semanticTokenColors) |
| `TextMateRegistrar.cs` | Registers `.tmLanguage(.json)` grammars from installed extensions with `TextMateService` |
| `LspFrameWriter.cs` | Serializes LSP frames (Content-Length + JSON body), one at a time, all waits cancellable |
| `RestartPolicy.cs` | Bounded language-server auto-restart budget (3 attempts, `2^(n-1)`s backoff, 60s stability window before refund) |

## Adding a service

1. Declare the interface in `Core/Abstractions/Services/`.
2. Implement it in `ProjectSystem/Services/`.
3. Register it where the Shell composes its container.
4. Consume it from a view model by interface only.
5. Test the view model against a fake — that is the whole point of the seam.
