# IntelliSense Comparison: VS Code vs VGS IDE

**Date**: 2026-03-18
**LSP Server**: `BasicLang.exe --lsp` (OmniSharp-based, registered in `BasicLangLanguageServer.cs`)
**VS Code Client**: `vscode-basiclang` extension using `vscode-languageclient` v9.x
**VGS IDE Client**: `LanguageService.cs` -- custom LSP client over stdin/stdout JSON-RPC

Both editors connect to the same BasicLang LSP server. Differences arise from which LSP methods each client actually calls and how the UI surfaces the results.

---

## Feature Comparison

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| **Completion** | Full | Full | None |
| **Signature help** | Full | Full | None |
| **Hover** | Full | Full | None |
| **Go to definition** | Full | Full | None |
| **Go to type definition** | Full | Full | None |
| **Go to implementation** | Full | Not wired | IDE missing |
| **Find references** | Full | Full | None |
| **Rename symbol** | Full (prepare + execute) | Full (prepare + execute) | None |
| **Code actions** | Full | Full | None |
| **Code lens** | Full | Full | None |
| **Document formatting** | Full | Full | None |
| **Range formatting** | Full | Full (client method exists) | None |
| **On-type formatting** | Full | Not wired | IDE missing |
| **Document symbols / outline** | Full | Full | None |
| **Workspace symbols** | Full (VS Code native) | Partial (manual file scan) | IDE uses fallback |
| **Inlay hints** | Full | Full | None |
| **Semantic tokens** | Full | Full | None |
| **Call hierarchy** | Full (incoming + outgoing) | Full (incoming + outgoing) | None |
| **Type hierarchy** | Full (supertypes + subtypes) | Full (supertypes + subtypes) | None |
| **Selection range** | Full | Full | None |
| **Document links** | Full | Full | None |
| **Linked editing** | Full | Not wired | IDE missing |
| **Document highlights** | Full | Full | None |
| **Folding ranges** | Full | Not wired (editor has own folding) | IDE uses local folding |
| **Diagnostics** | Full (publishDiagnostics) | Full (publishDiagnostics) | None |
| **Execute command** | Full | Partial (code lens commands) | Minor |

---

## Detailed Feature Notes

### 1. Completion

**Server** (`CompletionHandler.cs` + `CompletionService.cs`):
- Trigger characters: `.`, `(`, ` ` (space)
- Resolve provider: enabled (though resolve currently returns item unchanged)
- Sources: keywords, built-in functions (~80), user-defined symbols from AST, .NET type members via `TypeRegistry`/`SemanticAnalyzer`, engine API functions, snippet-style completions
- Filtering: done client-side by both editors

**VS Code**: Relies on `vscode-languageclient` which automatically handles `textDocument/completion` and `completionItem/resolve`. Trigger characters, filtering, and commit behavior are all handled by VS Code's built-in completion widget.

**VGS IDE** (`LanguageService.GetCompletionsAsync`): Sends `textDocument/completion`, parses `label`, `detail`, `documentation`, `kind`, `insertText`, `filterText`, `sortText`. IDE advertises `snippetSupport: true` in capabilities. Completion popup is rendered by `CodeEditorControl`.

**Gap**: None. Both get the same data. VS Code may have slightly better fuzzy filtering out of the box.

---

### 2. Signature Help

**Server** (`SignatureHelpHandler.cs`):
- Trigger characters: `(`, `,`
- Retrigger characters: `,`
- Coverage: ~80 built-in functions with full parameter documentation, user-defined Function/Sub from AST, .NET method signatures via `TypeRegistry`
- Active parameter tracking: counts commas between parentheses at cursor position

**VS Code**: Automatic via language client. Shows parameter info tooltip on `(` and `,`.

**VGS IDE** (`LanguageService.GetSignatureHelpAsync`): Calls `textDocument/signatureHelp`, parses signatures with parameters. Wired to `SignatureHelpRequested` event from the editor.

**Gap**: None.

---

### 3. Hover

**Server** (`HoverHandler.cs` + `SymbolService.GetHoverInfo`):
- Returns Markdown-formatted content
- Resolves: built-in functions, user-defined symbols, .NET types, keywords

**VS Code**: Displays hover in native tooltip with Markdown rendering.

**VGS IDE** (`LanguageService.GetHoverAsync`): Parses `contents` (string, MarkupContent, or array). Wired to `HoverRequested` event.

**Gap**: None.

---

### 4. Go to Definition

**Server** (`DefinitionHandler.cs`):
- Uses `SymbolService.FindDefinition` to locate functions, subs, classes, variables, constants, properties in AST
- Returns `LocationOrLocationLinks`

**VS Code**: F12 / Ctrl+Click, handled natively.

**VGS IDE** (`LanguageService.GetDefinitionAsync`): Parses location (uri + range). Wired to F12 / Ctrl+Click in editor.

**Gap**: None.

---

### 5. Go to Type Definition

**Server**: Not a separate handler -- falls back to definition handler behavior.

**VGS IDE** (`LanguageService.GetTypeDefinitionAsync`): Sends `textDocument/typeDefinition` request. If server returns null, falls back to definition.

**Gap**: None (graceful fallback in both).

---

### 6. Go to Implementation

**Server** (`ImplementationHandler.cs`):
- Finds interface implementations (classes implementing an interface)
- Finds class inheritors (subclasses)
- Finds method overrides (virtual/abstract method implementations)
- Searches across all open documents

**VS Code**: Handled automatically. Ctrl+F12 triggers `textDocument/implementation`.

**VGS IDE**: `LanguageService` has no `GetImplementationAsync` method. The `textDocument/implementation` request is never sent.

**Gap**: **VGS IDE is missing Go to Implementation.** The server fully supports it, but the IDE client does not call the endpoint.

---

### 7. Find References

**Server** (`ReferencesHandler.cs`):
- Scans tokens across all open documents for identifier matches (case-insensitive)
- Public symbols: searched in all open documents; private symbols: current document only
- Supports `includeDeclaration` flag

**VS Code**: Shift+F12 triggers references panel.

**VGS IDE** (`LanguageService.FindReferencesAsync`): Sends `textDocument/references` with `includeDeclaration: true`. Has a fallback `FindReferencesFallbackAsync` that does text-based search when LSP is unavailable.

**Gap**: None.

---

### 8. Rename Symbol

**Server** (`RenameHandler.cs` + `PrepareRenameHandler.cs`):
- Prepare rename: validates the cursor is on an identifier token, returns range + placeholder
- Execute rename: finds all identifier tokens matching the word across relevant documents (public = all docs, private = current doc), returns `WorkspaceEdit` with text edits
- `PrepareProvider: true` advertised

**VS Code**: F2 triggers rename with prepare step.

**VGS IDE** (`LanguageService.RenameAsync`): Sends `textDocument/rename`. Also has a `RefactoringService`-based fallback for non-LSP rename.

**Gap**: None.

---

### 9. Code Actions

**Server** (`CodeActionHandler.cs`):
- Quick fixes for diagnostics: undefined variable (suggest `Dim`), missing return type, unused variable removal, typo suggestions
- Refactoring: extract function, organize imports, generate property from field, add null check, convert to ternary, encapsulate field
- Source actions: add missing imports

**VS Code**: Lightbulb icon, Ctrl+. triggers code actions.

**VGS IDE** (`LanguageService.GetCodeActionsAsync`): Sends `textDocument/codeAction` with range and diagnostics. Parses `quickfix`, `refactor`, `refactor.extract`, `refactor.inline`, `refactor.rewrite`, `source`, `source.organizeImports`, `source.fixAll` kinds.

**Gap**: None.

---

### 10. Code Lens

**Server** (`CodeLensHandler.cs`):
- Reference counts on functions, subs, classes
- "Run" and "Debug" buttons on `Main` function/sub
- "Inherits BaseClass" on classes with base classes
- Commands: `basiclang.showReferences`, `basiclang.run`, `basiclang.debug`, `basiclang.goToDefinition`
- `ResolveProvider: false`

**VS Code**: Displays code lenses inline above declarations. Commands routed to extension.

**VGS IDE** (`LanguageService.GetCodeLensAsync`): Sends `textDocument/codeLens`, parses lenses with command info. `RefreshCodeLensesAsync` called on text change. `HandleCodeLensCommandAsync` processes `showReferences` and `goToDefinition` commands.

**Gap**: None.

---

### 11. Formatting

**Server** (`FormattingHandler.cs` + `RangeFormattingHandler` + `OnTypeFormattingHandler.cs`):

| Sub-feature | Server | VS Code | VGS IDE |
|-------------|--------|---------|---------|
| Document formatting | Full (indent normalization, keyword casing, operator spacing) | Full | Full |
| Range formatting | Full (format selection with context-aware indentation) | Full | Full (client method exists) |
| On-type formatting | Full (auto-inserts closing keywords on Enter after block openers) | Full | **Not wired** |

On-type formatting trigger character: `\n` (Enter). When the user presses Enter after `Sub MyFunc`, the server auto-inserts `End Sub` with correct indentation.

**Gap**: **VGS IDE does not invoke `textDocument/onTypeFormatting`.** The server supports it, but the IDE does not send the request. The IDE may have its own local auto-close logic in the editor control.

---

### 12. Document Symbols / Outline

**Server** (`DocumentSymbolHandler.cs` + `SymbolService.GetDocumentSymbols`):
- Returns hierarchical `DocumentSymbol` tree
- Covers: functions, subs, classes (with nested members), variables, constants, properties, interfaces, modules

**VS Code**: Populates the Outline panel and breadcrumb bar.

**VGS IDE** (`LanguageService.GetDocumentSymbolsAsync`): Parses hierarchical symbols with children. Used in the symbol navigator / outline panel.

**Gap**: None.

---

### 13. Workspace Symbols

**Server** (`WorkspaceSymbolHandler.cs`):
- Searches all open documents for matching symbols
- Case-insensitive substring matching on symbol names
- Returns functions, subs, classes, variables, constants, properties, class members with container names

**VS Code**: Ctrl+T triggers workspace symbol search. `vscode-languageclient` automatically sends `workspace/symbol`.

**VGS IDE**: No `GetWorkspaceSymbolsAsync` method in `LanguageService`. The `GoToWorkspaceSymbolAsync` method in `MainWindowViewModel` manually iterates open files and calls `GetDocumentSymbolsAsync` per file.

**Gap**: **VGS IDE does not use `workspace/symbol`.** It falls back to iterating documents client-side. This means: (1) only open files are searched rather than all workspace files, (2) no server-side filtering, (3) potentially slower for large projects.

---

### 14. Inlay Hints

**Server** (`InlayHintsHandler.cs`):
- Parameter name hints on function calls (e.g., `PrintLine(value: "Hello")`)
- Covers ~30 built-in functions and all user-defined functions/subs from AST
- Skips hints when argument name matches parameter name or uses named arguments (`:=`)
- `ResolveProvider: false`

**VS Code**: Displays inline gray text before arguments.

**VGS IDE** (`LanguageService.GetInlayHintsAsync`): Sends `textDocument/inlayHint` with range. Parses position, label, kind, paddingRight. Configurable via settings.

**Gap**: None.

---

### 15. Semantic Tokens

**Server** (`SemanticTokensHandler.cs`):
- Full document + range support, no delta
- 19 token types: namespace, type, class, enum, interface, struct, typeParameter, parameter, variable, property, enumMember, function, method, keyword, modifier, comment, string, number, operator
- 10 token modifiers: declaration, definition, readonly, static, deprecated, abstract, async, modification, documentation, defaultLibrary
- Two-pass: collects declarations from AST, then classifies each token
- Built-in functions get `defaultLibrary` modifier

**VS Code**: Uses semantic tokens for enhanced highlighting on top of TextMate grammar.

**VGS IDE** (`LanguageService.GetSemanticTokensAsync`): Sends `textDocument/semanticTokens/full`. Parses the encoded integer array.

**Gap**: None.

---

### 16. Call Hierarchy

**Server** (`CallHierarchyHandler.cs`):
- Prepare: finds function/sub at cursor, returns `CallHierarchyItem` with name, kind, detail, range
- Incoming calls: searches all documents for callers, walks AST bodies (statements + expressions) to find `CallExpressionNode` references
- Outgoing calls: finds all calls within a function body, groups by target name

**VS Code**: Right-click > "Show Call Hierarchy". Displays tree view.

**VGS IDE**: `LanguageService` has `GetIncomingCallsAsync` and `GetOutgoingCallsAsync`. `CallHierarchyViewModel` provides UI. Wired via `ShowCallHierarchyAsync` in MainWindowViewModel.

**Gap**: None.

---

### 17. Type Hierarchy

**Server** (`TypeHierarchyHandler.cs`):
- Prepare: finds class/interface/structure at cursor
- Supertypes: extracts base class name from detail string, searches all documents
- Subtypes: searches all documents for classes whose `BaseClass` matches

**VS Code**: Right-click > "Show Type Hierarchy".

**VGS IDE**: `LanguageService` has `GetSupertypesAsync` and `GetSubtypesAsync`. `TypeHierarchyViewModel` provides UI. Wired via `ShowTypeHierarchyAsync`.

**Gap**: None.

---

### 18. Selection Range

**Server** (`SelectionRangeHandler.cs`):
- Builds a nested hierarchy of selection ranges for smart expand/shrink selection
- Levels: word -> expression -> statement -> block -> declaration -> document
- Block detection uses keyword matching (If/For/While/Sub/Function/Class)

**VS Code**: Shift+Alt+Right/Left to expand/shrink selection.

**VGS IDE** (`LanguageService.GetSelectionRangesAsync`): Sends `textDocument/selectionRange`. `_currentSelectionRange` tracks state. `SmartSelectExpandAsync` walks up the parent chain.

**Gap**: None.

---

### 19. Document Links

**Server** (`DocumentLinkHandler.cs`):
- Detects: `Import "filename.bas"` directives, file path strings, URLs in comments
- Resolves relative paths against the current document directory
- Only links to files that exist on disk
- `ResolveProvider: false`

**VS Code**: Shows clickable links in editor.

**VGS IDE** (`LanguageService.GetDocumentLinksAsync`): Sends `textDocument/documentLink`. Parses range, target URI, tooltip.

**Gap**: None.

---

### 20. Linked Editing

**Server** (`LinkedEditingRangeHandler.cs`):
- Provides synchronized rename for local variables, parameters, function/sub names
- Determines scope (function/sub body, class) and finds all identifier occurrences within scope
- Requires at least 2 occurrences for linked editing to be useful
- Returns word pattern: `[a-zA-Z_][a-zA-Z0-9_]*`

**VS Code**: Automatically links matching identifiers for simultaneous editing when cursor is on one.

**VGS IDE**: `LanguageService` has no `GetLinkedEditingRangesAsync` method. The feature is not wired.

**Gap**: **VGS IDE does not support linked editing ranges.** The server fully implements scope-aware linked editing, but the IDE client never sends the request.

---

### 21. Document Highlights

**Server** (`DocumentHighlightHandler.cs`):
- Finds all occurrences of a symbol in the current document
- Distinguishes Write (declaration, assignment target) vs Read references
- Also highlights keywords and built-in function names

**VS Code**: Highlights all occurrences of selected symbol in the editor.

**VGS IDE** (`LanguageService.GetDocumentHighlightsAsync`): Called on cursor position change. Parses highlight kind (Read/Write/Text).

**Gap**: None.

---

### 22. Folding Ranges

**Server** (`FoldingRangeHandler.cs`): Registered as handler in the LSP server.

**VS Code**: Automatically uses folding ranges from the server.

**VGS IDE**: `LanguageService` has no `GetFoldingRangesAsync` method. The editor control likely implements its own local folding based on indentation or keyword matching.

**Gap**: **Minor.** The VGS IDE editor has its own folding mechanism that may not exactly match the LSP server's folding logic, but functionally the feature is present.

---

### 23. On-Type Formatting

**Server** (`OnTypeFormattingHandler.cs`):
- Trigger: `\n` (Enter key)
- Auto-inserts closing keywords (e.g., `End Sub`, `End Function`, `End If`, `Next`, `End While`, etc.)
- Checks if matching close already exists within next 20 lines to avoid duplicates
- Inserts with correct indentation

**VS Code**: Handled automatically by language client on Enter.

**VGS IDE**: Not wired. The IDE may have its own bracket/keyword auto-close in the editor control.

**Gap**: **VGS IDE does not use `textDocument/onTypeFormatting`.** The editor may have equivalent local behavior.

---

## Summary of Gaps

| # | Missing Feature in VGS IDE | Server Support | Difficulty to Wire |
|---|---------------------------|----------------|-------------------|
| 1 | Go to Implementation | Full (`ImplementationHandler`) | Low -- add `GetImplementationAsync` to `LanguageService`, call on Ctrl+F12 |
| 2 | On-type formatting | Full (`OnTypeFormattingHandler`) | Low -- add `GetOnTypeFormattingAsync`, call on Enter key |
| 3 | Workspace symbols (native) | Full (`WorkspaceSymbolHandler`) | Low -- add `GetWorkspaceSymbolsAsync`, replace manual file iteration |
| 4 | Linked editing ranges | Full (`LinkedEditingRangeHandler`) | Medium -- add `GetLinkedEditingRangesAsync`, integrate with editor multi-cursor |
| 5 | Folding ranges (LSP-based) | Full (`FoldingRangeHandler`) | Low -- add `GetFoldingRangesAsync`, but editor already has folding |

### Feature Coverage Score

- **LSP Server**: 22/22 features implemented (100%)
- **VS Code**: 22/22 features consumed (100%) -- `vscode-languageclient` auto-wires all registered handlers
- **VGS IDE**: 17/22 features consumed (77%) -- 5 features not wired in the client

### Architecture Difference

**VS Code** uses `vscode-languageclient` which automatically discovers and uses all server capabilities declared during initialization. Any handler registered in `BasicLangLanguageServer.cs` is automatically available.

**VGS IDE** uses a hand-rolled LSP client (`LanguageService.cs`) that manually implements each LSP method. Features must be explicitly coded: add a `SendRequestAsync` call, write a parser for the response, and wire it into the UI. This is why 5 features are missing -- the server supports them, but the client code was never written.

Additionally, the VGS IDE `InitializeAsync` only declares basic capabilities (completion, hover, signatureHelp, definition, references, documentSymbol, publishDiagnostics) in its `initialize` request. The server registers handlers regardless, but a fully standards-compliant server could skip advertising features the client doesn't declare support for.
