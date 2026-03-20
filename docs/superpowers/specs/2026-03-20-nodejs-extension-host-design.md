# Node.js Extension Host — Full VS Code Extension Runtime

**Date:** 2026-03-20
**Status:** Approved

## Problem

The IDE has a Node.js extension host (`ExtensionHostMain.js`) that can load VS Code extensions, but the VS Code API shim is mostly stubs. Extensions like HTML, CSS, JSON, and TypeScript that spawn their own language servers via `vscode-languageclient` don't actually work — provider registrations are no-ops, document events never fire, and the host lacks a synchronized document model.

## Goal

Make the Node.js extension host a **full VS Code extension runtime** so that real VS Code extensions (language servers, TreeView, WebView, debug adapters, task providers) work out of the box. Extensions bring their own dependencies (including `vscode-languageclient`) and can spawn child processes freely.

## Architecture

### Process Model

Single Node.js process (matching VS Code's local extension host model). All extensions run in-process. Crash recovery auto-restarts the host and re-activates extensions with exponential backoff (2s → 4s → 8s → max 30s).

### Communication

JSON-RPC over stdin/stdout between C# IDE and Node.js host, using Content-Length headers (LSP-style framing). The C# side uses `StreamJsonRpc` via `HeaderDelimitedMessageHandler`.

### Data Flow

```
User types in editor
  → C# IDE sends textDocument/didChange to Node.js host
  → Host updates internal TextDocument model
  → Host fires workspace.onDidChangeTextDocument to extensions
  → Extensions with registered providers are ready for requests

User triggers completion (Ctrl+Space)
  → C# IDE sends textDocument/completion request to host
  → Host matches document against registered provider selectors
  → Host calls provider.provideCompletionItems(document, position, token)
  → Provider (e.g., vscode-languageclient) forwards to child language server
  → Language server responds with completions
  → Host serializes and returns to IDE via JSON-RPC
  → IDE displays completions in editor
```

## File Structure

```
VisualGameStudio.ProjectSystem/Services/ExtensionHost/
├── main.js                    # Entry point, RPC setup, extension lifecycle
├── rpc.js                     # JSON-RPC protocol (send/receive/routing)
├── document-manager.js        # TextDocument model + sync
├── provider-registry.js       # All provider registrations + request dispatch
├── vscode-api/
│   ├── index.js               # Creates and returns the vscode module
│   ├── types.js               # Position, Range, Uri, Selection, TextEdit, etc.
│   ├── event.js               # EventEmitter, CancellationToken, Disposable
│   ├── commands.js            # commands namespace
│   ├── window.js              # window namespace
│   ├── workspace.js           # workspace namespace
│   ├── languages.js           # languages namespace (all providers, diagnostics)
│   ├── debug.js               # debug namespace
│   ├── tasks.js               # tasks namespace
│   └── env.js                 # env namespace
└── utils/
    ├── document-selector.js   # Match documents against selectors
    └── uri.js                 # Full VS Code Uri implementation
```

## Component Details

### 1. RPC Router (`rpc.js`)

Handles JSON-RPC protocol with Content-Length framing. Supports:
- Requests (with id, expects response)
- Notifications (no id, fire-and-forget)
- Responses (to pending requests)
- Bidirectional: both IDE→Host requests and Host→IDE requests

### 2. Document Manager (`document-manager.js`)

Maintains synchronized TextDocument objects matching the IDE's open editors.

**TextDocument API:**
- `uri`, `fileName`, `languageId`, `version`
- `getText(range?)` — full text or range
- `lineAt(line | position)` — returns TextLine
- `positionAt(offset)` — offset to Position
- `offsetAt(position)` — Position to offset
- `lineCount`, `isDirty`, `isClosed`
- `getWordRangeAtPosition(position, regex?)`

**Sync protocol:**
- `textDocument/didOpen` — creates TextDocument with full content
- `textDocument/didChange` — applies incremental or full changes, bumps version
- `textDocument/didClose` — marks document closed
- `textDocument/didSave` — marks document saved

**Events fired:**
- `workspace.onDidOpenTextDocument`
- `workspace.onDidChangeTextDocument` (with `TextDocumentChangeEvent`)
- `workspace.onDidCloseTextDocument`
- `workspace.onDidSaveTextDocument`

### 3. Provider Registry (`provider-registry.js`)

Central registry for all language feature providers. When an extension registers a provider, it's stored with its document selector. When the IDE requests a language feature, the registry finds matching providers and invokes them.

**Supported provider types (20+):**
- CompletionItemProvider
- HoverProvider
- DefinitionProvider
- TypeDefinitionProvider
- ImplementationProvider
- ReferenceProvider
- DocumentHighlightProvider
- DocumentSymbolProvider
- WorkspaceSymbolProvider
- CodeActionProvider
- CodeLensProvider
- DocumentFormattingEditProvider
- DocumentRangeFormattingEditProvider
- OnTypeFormattingEditProvider
- RenameProvider
- SignatureHelpProvider
- DocumentLinkProvider
- ColorProvider
- FoldingRangeProvider
- SelectionRangeProvider
- InlayHintsProvider
- LinkedEditingRangeProvider
- DeclarationProvider
- CallHierarchyProvider
- TypeHierarchyProvider
- SemanticTokensProvider
- DocumentDropEditProvider

**Document selector matching** (`utils/document-selector.js`):
- Matches by `language`, `scheme`, `pattern` (glob), `notebookType`
- String selectors treated as language ID
- Array selectors match if any element matches

### 4. VS Code API Modules

#### `types.js` — Complete type implementations
- `Position` — line/character, with `translate()`, `compareTo()`, `isEqual()`
- `Range` — start/end Position, with `contains()`, `intersection()`, `union()`, `isEmpty`, `isSingleLine`
- `Selection` — extends Range with `anchor`/`active`/`isReversed`
- `Uri` — full implementation with `scheme`, `authority`, `path`, `query`, `fragment`, `fsPath`, `toString()`, `with()`, static `file()`, `parse()`, `from()`, `joinPath()`
- `Location`, `LocationLink`
- `Diagnostic`, `DiagnosticSeverity`, `DiagnosticRelatedInformation`
- `CompletionItem`, `CompletionItemKind`, `CompletionList`
- `Hover`, `MarkdownString`
- `TextEdit`, `WorkspaceEdit`, `SnippetTextEdit`
- `CodeAction`, `CodeActionKind`
- `CodeLens`
- `DocumentLink`
- `Color`, `ColorInformation`, `ColorPresentation`
- `FoldingRange`, `FoldingRangeKind`
- `SelectionRange`
- `InlayHint`, `InlayHintKind`
- `SignatureHelp`, `SignatureInformation`, `ParameterInformation`
- `SymbolInformation`, `DocumentSymbol`, `SymbolKind`
- `TreeItem`, `TreeItemCollapsibleState`
- `ThemeIcon`, `ThemeColor`
- `EventEmitter` (proper VS Code EventEmitter, not Node.js)
- `Disposable`, `CancellationTokenSource`, `CancellationToken`
- All enums: `ViewColumn`, `StatusBarAlignment`, `TextEditorRevealType`, `EndOfLine`, `IndentAction`, etc.

#### `event.js` — Event infrastructure
- `EventEmitter<T>` with `.event` property, `.fire()`, `.dispose()`
- `CancellationTokenSource` with `.token`, `.cancel()`, `.dispose()`
- `CancellationToken` with `.isCancellationRequested`, `.onCancellationRequested`
- `Disposable` with static `from(...disposables)`

#### `commands.js` — Command registry
- `registerCommand(id, handler)` → Disposable
- `registerTextEditorCommand(id, handler)` → Disposable
- `executeCommand(id, ...args)` → Promise
- `getCommands(filterInternal?)` → Promise<string[]>
- Built-in commands: `vscode.open`, `vscode.diff`, `editor.action.showReferences`, etc.
- Forward unknown commands to IDE via RPC

#### `window.js` — Window API
- `showInformationMessage/showWarningMessage/showErrorMessage` — proxy to IDE
- `showQuickPick` — proxy to IDE with full QuickPickOptions
- `showInputBox` — proxy to IDE with InputBoxOptions
- `showOpenDialog/showSaveDialog` — proxy to IDE
- `createOutputChannel(name)` — real OutputChannel with append/appendLine/clear/show/hide/dispose
- `createStatusBarItem(alignment, priority)` — real StatusBarItem with text/tooltip/command/show/hide
- `activeTextEditor` — synced from IDE, real TextEditor object
- `visibleTextEditors` — synced from IDE
- `onDidChangeActiveTextEditor` — fires on IDE editor switch
- `onDidChangeVisibleTextEditors` — fires on editor visibility changes
- `showTextDocument(uri, options)` — request IDE to open document
- `createTreeView(viewId, options)` — TreeView support (data stored in host, IDE renders)
- `registerTreeDataProvider(viewId, provider)` — shorthand for createTreeView
- `createWebviewPanel(viewType, title, column, options)` — WebView support (HTML sent to IDE for rendering)
- `withProgress(options, task)` — progress notifications to IDE
- `setStatusBarMessage(text, timeout?)` — temporary status bar message
- `createTerminal(options)` — request IDE to create terminal

#### `workspace.js` — Workspace API
- `textDocuments` — all open TextDocument objects (from DocumentManager)
- `workspaceFolders` — synced from IDE
- `rootPath` / `name` — workspace root
- `getConfiguration(section)` — returns ConfigurationSection proxy
  - `get(key, defaultValue)` — reads from IDE-synced settings
  - `has(key)`, `update(key, value, target)`, `inspect(key)`
  - Settings pushed from IDE via `workspace/configuration` notification
- `openTextDocument(uri)` — request IDE to open, returns TextDocument
- `findFiles(include, exclude, maxResults, token)` — proxy to IDE (glob search)
- `createFileSystemWatcher(glob, ignoreCreate, ignoreChange, ignoreDelete)` — real watchers via IDE
- `applyEdit(workspaceEdit)` — send edit to IDE for application
- `saveAll(includeUntitled?)` — request IDE save all
- `fs` — FileSystem API: `readFile`, `writeFile`, `stat`, `readDirectory`, `createDirectory`, `delete`, `rename`, `copy`
- All document events (from DocumentManager)
- `onDidChangeConfiguration` — fires when settings change
- `onDidChangeWorkspaceFolders` — fires on folder add/remove
- `registerTextDocumentContentProvider(scheme, provider)` — virtual documents

#### `languages.js` — Languages API
- All 20+ `registerXxxProvider` methods — store in ProviderRegistry, notify IDE
- `createDiagnosticCollection(name)` — real collection that publishes to IDE via `languages/publishDiagnostics`
- `getDiagnostics(resource?)` — returns diagnostics from all collections
- `setTextDocumentLanguage(document, languageId)` — change document language
- `getLanguages()` — returns known language IDs
- `match(selector, document)` — score a document against a selector
- `registerDocumentSemanticTokensProvider(selector, provider, legend)` — semantic tokens
- `onDidChangeDiagnostics` — event

#### `debug.js` — Debug API
- `registerDebugAdapterDescriptorFactory(type, factory)` — register debug adapter
- `registerDebugConfigurationProvider(type, provider)` — register config provider
- `startDebugging(folder, config, options)` — request IDE to start debug session
- `activeDebugSession` — current session
- `breakpoints` — current breakpoints
- `onDidStartDebugSession`, `onDidTerminateDebugSession`
- `onDidChangeActiveDebugSession`
- `onDidChangeBreakpoints`

#### `tasks.js` — Tasks API
- `registerTaskProvider(type, provider)` — register task provider
- `fetchTasks(filter?)` — get all tasks
- `executeTask(task)` — run a task
- `onDidStartTask`, `onDidEndTask`
- `taskExecutions` — running tasks

#### `env.js` — Environment API
- `appName` — "Visual Game Studio"
- `appRoot` — IDE installation path
- `language` — UI language
- `machineId` — anonymous machine identifier
- `sessionId` — current session ID
- `uriScheme` — "vscode" (for compat)
- `clipboard` — read/write text
- `openExternal(uri)` — open URL in browser
- `asExternalUri(uri)` — resolve external URI
- `shell` — default shell path

### 5. C# Side Changes

#### `ExtensionHost.cs`
- **Crash recovery**: auto-restart with backoff on unexpected exit
- **Provider requests**: new RPC methods for each provider type (`textDocument/completion`, `textDocument/hover`, etc.)
- **Document sync**: send didOpen/didChange/didClose/didSave with full content
- **TreeView handlers**: `treeView/getChildren`, `treeView/getTreeItem` callbacks
- **WebView handlers**: `webview/create`, `webview/setHtml`, `webview/postMessage` bidirectional
- **Diagnostics handler**: receive `languages/publishDiagnostics` and forward to IDE diagnostic system
- **Configuration push**: send workspace/user settings to host on startup and on change
- **Workspace folder sync**: notify host of folder changes

#### `ExtensionService.cs`
- **Provider integration**: when user triggers completion/hover/etc., check if extension host has registered providers for the language, forward request
- **Document lifecycle hooks**: on editor open/change/close/save, notify extension host
- **TreeView rendering**: create Avalonia TreeView controls from host data
- **WebView rendering**: create Avalonia WebView controls from host HTML
- **Diagnostic integration**: merge extension diagnostics with LSP diagnostics in Problems panel

#### `IExtensionService.cs`
- Add `RequestCompletionAsync`, `RequestHoverAsync`, `RequestDefinitionAsync`, etc.
- Add TreeView/WebView data models
- Add diagnostic collection events

### 6. JSON-RPC Protocol

#### IDE → Host (Requests)

| Method | Params | Response |
|--------|--------|----------|
| `initialize` | `{workspaceFolders, configuration}` | capabilities |
| `activateExtension` | `{extensionPath, extensionId}` | `{activated, hasMain}` |
| `deactivateExtension` | `{extensionId}` | `{deactivated}` |
| `executeCommand` | `{command, args}` | result |
| `shutdown` | — | `{ok}` |
| `textDocument/completion` | `{uri, position, context}` | CompletionList |
| `textDocument/hover` | `{uri, position}` | Hover |
| `textDocument/definition` | `{uri, position}` | Location[] |
| `textDocument/references` | `{uri, position, context}` | Location[] |
| `textDocument/formatting` | `{uri, options}` | TextEdit[] |
| `textDocument/rangeFormatting` | `{uri, range, options}` | TextEdit[] |
| `textDocument/codeAction` | `{uri, range, context}` | CodeAction[] |
| `textDocument/codeLens` | `{uri}` | CodeLens[] |
| `textDocument/documentSymbol` | `{uri}` | DocumentSymbol[] |
| `textDocument/signatureHelp` | `{uri, position, context}` | SignatureHelp |
| `textDocument/rename` | `{uri, position, newName}` | WorkspaceEdit |
| `textDocument/foldingRange` | `{uri}` | FoldingRange[] |
| `textDocument/documentLink` | `{uri}` | DocumentLink[] |
| `textDocument/selectionRange` | `{uri, positions}` | SelectionRange[] |
| `textDocument/inlayHint` | `{uri, range}` | InlayHint[] |
| `textDocument/semanticTokens` | `{uri}` | SemanticTokens |
| `textDocument/documentHighlight` | `{uri, position}` | DocumentHighlight[] |
| `textDocument/linkedEditingRange` | `{uri, position}` | LinkedEditingRanges |
| `textDocument/prepareCallHierarchy` | `{uri, position}` | CallHierarchyItem[] |
| `textDocument/prepareTypeHierarchy` | `{uri, position}` | TypeHierarchyItem[] |
| `workspace/symbol` | `{query}` | SymbolInformation[] |
| `treeView/getChildren` | `{viewId, element?}` | TreeItem[] |
| `webview/postMessage` | `{panelId, message}` | — |

#### IDE → Host (Notifications)

| Method | Params |
|--------|--------|
| `textDocument/didOpen` | `{uri, languageId, version, text}` |
| `textDocument/didChange` | `{uri, version, contentChanges}` |
| `textDocument/didClose` | `{uri}` |
| `textDocument/didSave` | `{uri, text?}` |
| `workspace/didChangeConfiguration` | `{settings}` |
| `workspace/didChangeWorkspaceFolders` | `{added, removed}` |
| `activeEditor/didChange` | `{uri, languageId, selections}` |

#### Host → IDE (Notifications)

| Method | Params |
|--------|--------|
| `registerCommand` | `{command, extensionId}` |
| `languages/registerProvider` | `{type, extensionId, selector, metadata}` |
| `languages/publishDiagnostics` | `{uri, diagnostics, collectionName}` |
| `window/showMessage` | `{type, message, items}` |
| `outputChannel/create` | `{name}` |
| `outputChannel/append` | `{name, text}` |
| `statusBar/update` | `{extensionId, text, tooltip, command, visible}` |
| `treeView/create` | `{viewId, title, options}` |
| `treeView/refresh` | `{viewId, element?}` |
| `webview/create` | `{panelId, viewType, title, column, options}` |
| `webview/setHtml` | `{panelId, html}` |
| `webview/postMessage` | `{panelId, message}` |
| `debug/registerAdapter` | `{type, extensionId}` |
| `tasks/register` | `{type, extensionId}` |
| `workspace/applyEdit` | `{edit}` |
| `window/withProgress` | `{id, title, message, increment}` |
| `extensionActivated` | `{extensionId}` |
| `log` | `{level, message}` |
| `ready` | — |

## Implementation Priority

### Phase 1 — Language Extensions Work (Tier 1)
1. `rpc.js` — protocol layer
2. `types.js` — all VS Code types
3. `event.js` — EventEmitter, CancellationToken, Disposable
4. `document-manager.js` — TextDocument model + sync
5. `utils/uri.js` — full Uri implementation
6. `utils/document-selector.js` — selector matching
7. `workspace.js` — document events, configuration, workspaceFolders, fs
8. `languages.js` — all provider registrations + request dispatch
9. `provider-registry.js` — central provider store + matching
10. `window.js` — OutputChannel, StatusBarItem, messages, activeTextEditor
11. `commands.js` — command registry
12. `main.js` — entry point, extension lifecycle, require('vscode') hook
13. `ExtensionHost.cs` — expanded RPC, document sync, provider requests, crash recovery
14. `ExtensionService.cs` — wire providers into editor, document event forwarding

### Phase 2 — Broader Extension Compat (Tier 2)
15. TreeView support (window.js + C# rendering)
16. WebView support (window.js + C# rendering)
17. `workspace.fs` — FileSystem API
18. `tasks.js` — task provider support
19. `debug.js` — debug adapter support

### Phase 3 — Polish (Tier 3)
20. Terminal creation
21. Authentication providers
22. Comment controllers
23. Custom editors

## Testing Strategy

- **Unit tests**: Each `vscode-api/` module tested with mock RPC
- **Integration test**: Install real `vscode-html` extension from Open VSX, activate, verify completions return real HTML tag suggestions
- **C# tests**: Provider request/response serialization round-trip tests
- **Smoke test**: Open HTML file in IDE, verify syntax highlighting + completions + hover + formatting all work

## Migration from Current Implementation

This is a **full replacement** of the current `ExtensionHostMain.js` (523 lines). The new modular `ExtensionHost/` directory replaces it entirely.

### RPC Method Name Changes

The current implementation uses ad-hoc method names. The new implementation uses LSP-style naming for consistency:

| Current (old) | New (spec) | Direction |
|---|---|---|
| `documentOpened` | `textDocument/didOpen` | IDE → Host |
| `documentChanged` | `textDocument/didChange` | IDE → Host |
| `documentClosed` | `textDocument/didClose` | IDE → Host |
| `provideCompletions` | `textDocument/completion` | IDE → Host |
| `provideHover` | `textDocument/hover` | IDE → Host |
| `host/registerCommand` | `registerCommand` | Host → IDE |
| `host/showMessage` | `window/showMessage` | Host → IDE |
| `host/log` | `log` | Host → IDE |

Both `ExtensionHost.cs` (C# RPC handlers) and the Node.js host are updated simultaneously — no backward compatibility needed since both sides are in-tree.

### Critical Bugs Fixed

1. **`broadcastToExtensions()` is empty** — The current implementation receives `textDocument/didOpen` etc. but never fires the corresponding `workspace.onDidOpenTextDocument` events to extensions. The new DocumentManager fixes this.
2. **`require('vscode')` shared across extensions** — The current code sets `require.cache['vscode']` globally, meaning the last-activated extension's API facade overwrites all others. The new implementation creates per-extension API instances using a module resolution hook that returns the correct facade based on the calling extension's path.
3. **Missing `heartbeat` handler** — The C# side sends heartbeats every 30s but the JS side has no handler. The new implementation adds a heartbeat handler that responds immediately.

## Per-Extension API Isolation

Each extension gets its own `vscode` API facade created by `vscode-api/index.js`. The `require('vscode')` hook uses `module.parent.filename` to determine which extension is requiring it and returns the correct facade. This ensures:
- Each extension's `ExtensionContext` is isolated
- `commands.registerCommand` tracks which extension registered each command
- `subscriptions` arrays are per-extension for proper cleanup on deactivate

## Provider Merging and Priority

When multiple extensions register providers for the same language/document:
- **Completion**: Results from all matching providers are merged into a single CompletionList
- **Hover**: First provider that returns a non-null result wins
- **Definition/References/etc.**: Results from all providers are concatenated
- **Formatting**: First registered provider wins (only one formatter active)
- **Code Actions**: Results from all providers are merged

Provider timeouts: 5 seconds per provider. If a provider exceeds this, its result is skipped and other providers' results are returned.

## Cancellation Propagation

Uses the LSP `$/cancelRequest` pattern:
1. C# side sends a request with an `id`
2. If the user cancels (e.g., types more during completion), C# sends `$/cancelRequest` with that `id`
3. Node.js host receives the cancellation, sets `CancellationToken.isCancellationRequested = true`
4. Provider can check the token and abort early
5. Host sends error response with code `-32800` (RequestCancelled)

## Serialization

All VS Code types are serialized as plain JSON objects matching LSP type shapes:
- `Position` → `{line: number, character: number}`
- `Range` → `{start: Position, end: Position}`
- `Location` → `{uri: string, range: Range}`
- `CompletionItem` → matches LSP `CompletionItem`
- `Diagnostic` → matches LSP `Diagnostic`

This ensures compatibility with `vscode-languageclient` which expects LSP-shaped data.

## Document Sync Mode

Full-sync in v1: `textDocument/didChange` sends `{uri, version, text}` (complete document text), not incremental `contentChanges`. The DocumentManager replaces the full text on each change. This simplifies implementation at the cost of bandwidth for large files.

## Node.js Requirements

Minimum Node.js 16.x (for stable ESM interop and `AbortController`). Recommended 18.x+.

## Key Constraints

- Extensions bring their own `vscode-languageclient` — we don't bundle or shim it
- Extensions can spawn child processes freely (language servers, etc.)
- The `vscode` API shim must be complete enough that `vscode-languageclient` works against it
- All provider requests are async and support cancellation tokens
- A malicious extension can crash the shared process — accepted risk, mitigated by crash recovery
