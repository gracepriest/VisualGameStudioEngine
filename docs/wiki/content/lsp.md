title: Language server
lede: `BasicLang.exe --lsp` — one handler per feature, shared with the compiler's own analysis.
---
The language server is not a separate product. It is the compiler binary in a different
mode, reusing the same parser and semantic analyzer, which is why an IntelliSense bug is
usually a compiler bug.

```powershell
IDE/BasicLang.exe --lsp        # speaks LSP over stdio
```

Three clients drive it: the Visual Game Studio IDE, the VS 2022 extension, and the
VS Code extension.

## Layout

`BasicLang/LSP/` holds the server plus one file per feature.

| File | Feature |
|---|---|
| `BasicLangLanguageServer.cs` | Server host, capabilities, lifecycle |
| `SimpleLspServer.cs` | Minimal transport/dispatch layer |
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

`ModuleResolver.cs` backs **both** the compiler and the LSP. Import and namespace
resolution behaviour must therefore change in one place — fixing it "for the LSP" in a
copy is how the two drift apart and an editor starts disagreeing with the build.

## C++ IntelliSense

C++ files are served by **clangd**, not by this server. The IDE generates a
`compile_commands.json` (`BasicLang/ProjectSystem/CompileCommandsWriter.cs`) so clangd
knows the flags for every translation unit, then routes completion, hover, diagnostics
and semantic highlighting through it. clangd is downloaded on demand by
`ClangdInstaller.cs` / located by `ClangdLocator.cs`.

This is what "C++ is a peer language" means concretely: the IDE has no C++ knowledge of
its own, it just registers a second language server.

## The client side

In the IDE, `LanguageService.cs` and `LanguageServiceRegistry.cs`
(`VisualGameStudio.ProjectSystem/Services/`) own the client, with `LspFrameWriter.cs`
handling the wire framing and `RestartPolicy.cs` deciding what happens when a server
dies. **Tools → Restart Language Server** is the manual escape hatch.

`RegenOnSaveCoordinator.cs` re-emits the artefacts IntelliSense depends on when relevant
files are saved, so completions do not go stale mid-edit.

## Feature parity notes

The IDE's LSP feature coverage is tracked against VS Code in
`docs/comparisons/intellisense-comparison.md` and `docs/comparisons/ide-parity-scorecard.md`.
Research notes behind those decisions live in `docs/research/vscode-intellisense.md`.
