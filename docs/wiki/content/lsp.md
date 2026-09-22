title: Language server
lede: `BasicLang.exe --lsp` — one handler per feature, shared with the compiler's own analysis.
---
The language server is not a separate product. It is the compiler binary in a different
mode, reusing the same parser and semantic analyzer, which is why an IntelliSense bug is
usually a compiler bug.

```powershell
IDE/BasicLang.exe --lsp                  # speaks LSP over stdio (alias: --language-server)
IDE/BasicLang.exe --lsp --lsp-simple     # minimal fallback server (SimpleLspServer)
```

Three clients drive it: the Visual Game Studio IDE, the VS 2022 extension, and the
VS Code extension.

## Layout

`BasicLang/LSP/` holds the server plus one file per feature.

| File | Feature |
|---|---|
| `BasicLangLanguageServer.cs` | Server host, capabilities, lifecycle |
| `SimpleLspServer.cs` | Standalone fallback server behind `--lsp-simple` (completion/hover/definition/diagnostics only); the real handlers reuse its `FindSymbolInScope` / `FormatSymbolSignature` helpers |
| `DocumentManager.cs` | Open-document store and version tracking |
| `TextDocumentSyncHandler.cs` | `didOpen` / `didChange` / `didClose` |
| `WorkspaceManager.cs`, `LspProjectContext.cs` | Workspace and project resolution |
| `DiagnosticsService.cs` | Publishes diagnostics |
| `SymbolService.cs` | Symbol model shared by several handlers |
| `CompletionHandler.cs`, `CompletionService.cs` | Completion (~3,530 lines in the service) |
| `SignatureHelpHandler.cs` | Parameter hints |
| `HoverHandler.cs` | Quick info |
| `DefinitionHandler.cs`, `ImplementationHandler.cs` | Go to definition / implementation |
| `ReferencesHandler.cs` | Find all references |
| `DocumentSymbolHandler.cs`, `WorkspaceSymbolHandler.cs` | Outline and workspace symbols |
| `SemanticTokensHandler.cs` | Semantic highlighting |
| `FoldingRangeHandler.cs` | Folding regions |
| `DocumentHighlightHandler.cs` | Occurrence highlighting |
| `CodeLensHandler.cs` | Code lens annotations |
| `CodeActionHandler.cs` | Quick fixes and refactorings (~1,180 lines) |
| `RenameHandler.cs` | Rename symbol |
| `FormattingHandler.cs`, `OnTypeFormattingHandler.cs` | Document and on-type formatting |
| `InlayHintsHandler.cs` | Inlay hints |
| `CallHierarchyHandler.cs`, `TypeHierarchyHandler.cs` | Call and type hierarchies |
| `SelectionRangeHandler.cs` | Smart expand selection |
| `LinkedEditingRangeHandler.cs` | Linked editing |
| `DocumentLinkHandler.cs` | Document links |
| `ExecuteCommandHandler.cs` | Server-side commands |
| `ImplicitContainer.cs` | Handles code outside an explicit module/class |

## Shared resolution

`ModuleResolver.cs` is the one place the `.cls` `Option Public` rule lives: the compiler resolves
modules and namespaces through it (`ResolveModule` / `ResolveNamespace`), and the LSP calls its
static `HasOptionPublicDirective` from `ImplicitContainer.cs`, so an implicit class is public in the
editor exactly when it is public in the build.

> [trap] Project-wide import resolution is **not** shared. `LspProjectContext.cs` runs its own
> top-directory-only scan and hand-mirrors the compiler's rule that `Import X` may resolve into a
> subdirectory named `X` (`AddSourceSubdirectories`), marking those imports indeterminate instead of
> unresolved. A resolution rule changed in `ModuleResolver.cs` has to be carried into that mirror, or
> the editor starts disagreeing with the build.

## C++ IntelliSense

C++ files are served by **clangd**, not by this server. Before anything has been built, the IDE
emits both artefacts clangd needs — the generated `obj/gen` headers for the project's BasicLang
sources and `obj/compile_commands.json` — through
`VisualGameStudio.ProjectSystem/Services/IntelliSenseEmissionService.cs` →
`BasicLang/ProjectSystem/IntelliSenseEmitter.cs` (the database itself is written by
`CompileCommandsWriter.cs`), so clangd
knows the flags for every translation unit, then routes completion, hover, diagnostics, go to definition
and semantic highlighting through it. clangd is downloaded on demand by
`ClangdInstaller.cs` / located by `ClangdLocator.cs`.

This is what "C++ is a peer language" means concretely: the IDE has no C++ knowledge of
its own, it just registers a second language server.

## The client side

In the IDE, `LanguageService.cs` and `LanguageServiceRegistry.cs`
(`VisualGameStudio.ProjectSystem/Services/`) own the client, with `LspFrameWriter.cs`
handling the wire framing and `RestartPolicy.cs` deciding what happens when a server
dies. There is no manual restart in the IDE: recovery is the bounded auto-restart — 3 attempts at 1s/2s/4s backoff, the budget refunded only after a connection survives 60s (`RestartPolicy.MaxAttempts` / `RestartPolicy.StabilityWindow`), after which the Output pane says to restart the IDE. `StatusBarViewModel.RestartLspCommand` exists but raises an event nothing subscribes to, so the live status bar shows each server's state without a clickable restart. A manual **Restart Language Server** command exists only in the other clients — the VS 2022 extension (**BasicLang → Restart Language Server**) and the VS Code extension.

`RegenOnSaveCoordinator.cs` keeps the **C++** side fresh: a saved `.bas`/`.mod`/`.cls` under the open
project — or an external edit of its `.blproj` — goes through a trailing-edge debounce into
`IIntelliSenseEmissionService.RequestEmit`, regenerating `obj/gen` and `obj/compile_commands.json`.
Without it clangd keeps resolving against the last build's generated headers. A `.cpp` save needs
nothing: clangd re-parses that file on `didChange`.

## Feature parity notes

The IDE's LSP feature coverage is tracked against VS Code in
`docs/comparisons/intellisense-comparison.md` and `docs/comparisons/ide-parity-scorecard.md`.
Research notes behind those decisions live in `docs/research/vscode-intellisense.md`.
