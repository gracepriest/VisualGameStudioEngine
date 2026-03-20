# Node.js Extension Host Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the stub Node.js extension host with a full VS Code extension runtime so real extensions (HTML, CSS, JSON, TypeScript) work out of the box.

**Architecture:** Modular Node.js extension host communicating with C# IDE via JSON-RPC over stdin/stdout. Extensions run in a single process, bring their own dependencies (including `vscode-languageclient`), and can spawn child language servers. The VS Code API shim is split into focused modules mirroring VS Code's namespace structure.

**Tech Stack:** Node.js (16+), JSON-RPC (Content-Length framing), C# with StreamJsonRpc, Avalonia UI

**Spec:** `docs/superpowers/specs/2026-03-20-nodejs-extension-host-design.md`

---

## File Map

### New Files (Node.js — `VisualGameStudio.ProjectSystem/Services/ExtensionHost/`)

| File | Responsibility |
|------|---------------|
| `main.js` | Entry point, extension lifecycle, `require('vscode')` hook |
| `rpc.js` | JSON-RPC protocol: send/receive/route with Content-Length framing |
| `document-manager.js` | TextDocument model, document sync, fires document events |
| `provider-registry.js` | Stores all registered providers, matches selectors, dispatches requests |
| `vscode-api/index.js` | Factory: creates per-extension vscode API facade |
| `vscode-api/types.js` | Position, Range, Uri, Selection, TextEdit, Location, Diagnostic, CompletionItem, etc. |
| `vscode-api/event.js` | EventEmitter, CancellationTokenSource, CancellationToken, Disposable |
| `vscode-api/commands.js` | `vscode.commands` namespace |
| `vscode-api/window.js` | `vscode.window` namespace (messages, output, statusbar, TreeView, WebView) |
| `vscode-api/workspace.js` | `vscode.workspace` namespace (documents, config, fs, watchers) |
| `vscode-api/languages.js` | `vscode.languages` namespace (20+ provider registrations, diagnostics) |
| `vscode-api/debug.js` | `vscode.debug` namespace |
| `vscode-api/tasks.js` | `vscode.tasks` namespace |
| `vscode-api/env.js` | `vscode.env` namespace |
| `utils/document-selector.js` | Match documents against DocumentSelector |
| `utils/uri.js` | Full VS Code Uri implementation |

### Modified Files (C#)

| File | Changes |
|------|---------|
| `VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs` | Crash recovery, expanded RPC (provider requests, document sync, diagnostics, TreeView, WebView), new method names |
| `VisualGameStudio.ProjectSystem/Services/ExtensionService.cs` | Wire provider requests into IDE, forward document events to host, merge extension diagnostics |
| `VisualGameStudio.Core/Abstractions/Services/IExtensionService.cs` | Add provider request methods, TreeView/WebView models |
| `VisualGameStudio.Shell/Configuration/ServiceConfiguration.cs` | Update ExtensionService constructor (if needed) |

### Deleted Files

| File | Reason |
|------|--------|
| `VisualGameStudio.ProjectSystem/Services/ExtensionHostMain.js` | Replaced by `ExtensionHost/main.js` |

---

## Task 1: RPC Protocol Layer (`rpc.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/rpc.js`

This is the foundation everything else builds on. Extracted from the current monolithic `ExtensionHostMain.js` lines 18-104, but with proper module exports and cancellation support.

- [ ] **Step 1: Create `rpc.js` with JSON-RPC protocol**

```javascript
'use strict';

let messageId = 1;
const pendingRequests = new Map();
const methodHandlers = new Map();
const notificationHandlers = new Map();

function sendMessage(msg) {
    const json = JSON.stringify(msg);
    const header = `Content-Length: ${Buffer.byteLength(json)}\r\n\r\n`;
    process.stdout.write(header + json);
}

function sendRequest(method, params) {
    const id = messageId++;
    return new Promise((resolve, reject) => {
        pendingRequests.set(id, { resolve, reject });
        sendMessage({ jsonrpc: '2.0', id, method, params });
    });
}

function sendNotification(method, params) {
    sendMessage({ jsonrpc: '2.0', method, params });
}

function sendResponse(id, result, error) {
    if (error) {
        sendMessage({ jsonrpc: '2.0', id, error: { code: error.code || -1, message: String(error.message || error) } });
    } else {
        sendMessage({ jsonrpc: '2.0', id, result: result !== undefined ? result : null });
    }
}

function onRequest(method, handler) {
    methodHandlers.set(method, handler);
}

function onNotification(method, handler) {
    notificationHandlers.set(method, handler);
}

function handleMessage(msg) {
    if (msg.id !== undefined && msg.method) {
        // Request from IDE
        const handler = methodHandlers.get(msg.method);
        if (handler) {
            Promise.resolve(handler(msg.params || {}, msg.id))
                .then(result => sendResponse(msg.id, result))
                .catch(err => sendResponse(msg.id, null, err));
        } else {
            sendResponse(msg.id, null, { code: -32601, message: `Unknown method: ${msg.method}` });
        }
    } else if (msg.id !== undefined) {
        // Response to our request
        const pending = pendingRequests.get(msg.id);
        if (pending) {
            pendingRequests.delete(msg.id);
            if (msg.error) pending.reject(new Error(msg.error.message));
            else pending.resolve(msg.result);
        }
    } else if (msg.method) {
        // Notification from IDE
        if (msg.method === '$/cancelRequest' && msg.params) {
            // Handle cancellation
            const cancelId = msg.params.id;
            const pending = pendingRequests.get(cancelId);
            if (pending) {
                pending.reject(new Error('Request cancelled'));
                pendingRequests.delete(cancelId);
            }
            // Also notify any active provider request
            cancelCallbacks.forEach((cb, id) => {
                if (id === cancelId) { cb(); cancelCallbacks.delete(id); }
            });
            return;
        }
        const handler = notificationHandlers.get(msg.method);
        if (handler) {
            try { handler(msg.params || {}); } catch (e) {
                sendNotification('log', { level: 'error', message: `Notification handler error: ${e.message}` });
            }
        }
    }
}

const cancelCallbacks = new Map();

function registerCancelCallback(requestId, callback) {
    cancelCallbacks.set(requestId, callback);
}

function unregisterCancelCallback(requestId) {
    cancelCallbacks.delete(requestId);
}

// Message reader — reads Content-Length framed JSON-RPC from stdin
function startReading() {
    let buffer = '';
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', (chunk) => {
        buffer += chunk;
        while (true) {
            const headerEnd = buffer.indexOf('\r\n\r\n');
            if (headerEnd === -1) break;
            const header = buffer.substring(0, headerEnd);
            const match = header.match(/Content-Length:\s*(\d+)/i);
            if (!match) { buffer = buffer.substring(headerEnd + 4); continue; }
            const contentLength = parseInt(match[1], 10);
            const contentStart = headerEnd + 4;
            if (buffer.length < contentStart + contentLength) break;
            const content = buffer.substring(contentStart, contentStart + contentLength);
            buffer = buffer.substring(contentStart + contentLength);
            try {
                handleMessage(JSON.parse(content));
            } catch (e) {
                sendNotification('log', { level: 'error', message: `Parse error: ${e.message}` });
            }
        }
    });
}

module.exports = {
    sendRequest, sendNotification, sendResponse,
    onRequest, onNotification,
    registerCancelCallback, unregisterCancelCallback,
    startReading
};
```

- [ ] **Step 2: Verify file is syntactically valid**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/rpc.js"`
Expected: No output (no syntax errors)

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/rpc.js
git commit -m "feat(ext-host): add JSON-RPC protocol layer"
```

---

## Task 2: VS Code Types (`vscode-api/types.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/types.js`

Complete implementations of all VS Code API types. These must match LSP serialization shapes so `vscode-languageclient` works.

- [ ] **Step 1: Create `types.js` with core types**

Implement these classes/enums (each with full methods matching VS Code API):

**Classes to implement:**
- `Position` — constructor(line, character), translate(), with(), compareTo(), isEqual(), isBefore(), isAfter(), isBeforeOrEqual(), isAfterOrEqual()
- `Range` — constructor(start, end) or (sL, sC, eL, eC), isEmpty, isSingleLine, contains(), intersection(), union(), with()
- `Selection` — extends Range, anchor, active, isReversed
- `Uri` — scheme, authority, path, query, fragment, fsPath, toString(), with(), toJSON(), static file(), parse(), from(), joinPath()
- `Location` — uri, range
- `LocationLink` — originSelectionRange, targetUri, targetRange, targetSelectionRange
- `Diagnostic` — range, message, severity, source, code, relatedInformation, tags
- `DiagnosticRelatedInformation` — location, message
- `CompletionItem` — label, kind, detail, documentation, sortText, filterText, insertText, range, command, additionalTextEdits, commitCharacters
- `CompletionList` — isIncomplete, items
- `Hover` — contents, range
- `MarkdownString` — value, isTrusted, supportThemeIcons, appendText(), appendMarkdown(), appendCodeblock()
- `TextEdit` — static replace(), insert(), delete(), setEndOfLine(); range, newText
- `SnippetTextEdit` — static replace(), insert(); range, snippet
- `WorkspaceEdit` — has(), set(), get(), entries(), createFile(), deleteFile(), renameFile(), size
- `CodeAction` — title, kind, diagnostics, edit, command, isPreferred
- `CodeActionKind` — static Empty, QuickFix, Refactor, RefactorExtract, RefactorInline, RefactorMove, RefactorRewrite, Source, SourceOrganizeImports, SourceFixAll; append(), intersects(), contains()
- `CodeLens` — range, command, isResolved
- `DocumentLink` — range, target, tooltip
- `Color`, `ColorInformation`, `ColorPresentation`
- `FoldingRange` — start, end, kind
- `SelectionRange` — range, parent
- `InlayHint` — position, label, kind, tooltip, paddingLeft, paddingRight
- `SignatureHelp` — signatures, activeSignature, activeParameter
- `SignatureInformation` — label, documentation, parameters, activeParameter
- `ParameterInformation` — label, documentation
- `SymbolInformation` — name, kind, location, containerName
- `DocumentSymbol` — name, detail, kind, range, selectionRange, children
- `TreeItem` — label, id, iconPath, description, tooltip, command, collapsibleState, contextValue, resourceUri
- `ThemeIcon` — static File, Folder; id, color
- `ThemeColor` — id
- `SnippetString` — value, appendText(), appendTabstop(), appendPlaceholder(), appendChoice(), appendVariable()
- `SemanticTokensLegend` — tokenTypes, tokenModifiers
- `SemanticTokens` — resultId, data
- `SemanticTokensBuilder` — push(), build()
- `Task` — definition, scope, name, source, execution, isBackground, presentationOptions, problemMatchers, runOptions, group
- `ShellExecution`, `ProcessExecution`
- `TaskGroup` — static Clean, Build, Rebuild, Test

**Enums to implement (as frozen objects):**
- `DiagnosticSeverity` — Error=0, Warning=1, Information=2, Hint=3
- `DiagnosticTag` — Unnecessary=1, Deprecated=2
- `CompletionItemKind` — Text=0 through TypeParameter=24
- `CompletionItemTag` — Deprecated=1
- `CompletionTriggerKind` — Invoke=0, TriggerCharacter=1, TriggerForIncompleteCompletions=2
- `SymbolKind` — File=0 through TypeParameter=25
- `SymbolTag` — Deprecated=1
- `IndentAction` — None=0, Indent=1, IndentOutdent=2, Outdent=3
- `FoldingRangeKind` — Comment=1, Imports=2, Region=3
- `InlayHintKind` — Type=1, Parameter=2
- `StatusBarAlignment` — Left=1, Right=2
- `ViewColumn` — Active=-1, Beside=-2, One=1...Nine=9
- `TextEditorRevealType` — Default=0, InCenter=1, InCenterIfOutsideViewport=2, AtTop=3
- `EndOfLine` — LF=1, CRLF=2
- `TreeItemCollapsibleState` — None=0, Collapsed=1, Expanded=2
- `FileType` — Unknown=0, File=1, Directory=2, SymbolicLink=64
- `TextDocumentSaveReason` — Manual=1, AfterDelay=2, FocusOut=3
- `ConfigurationTarget` — Global=1, Workspace=2, WorkspaceFolder=3
- `ProgressLocation` — SourceControl=1, Window=10, Notification=15
- `DecorationRangeBehavior` — OpenOpen=0, ClosedClosed=1, OpenClosed=2, ClosedOpen=3
- `OverviewRulerLane` — Left=1, Center=2, Right=4, Full=7

This file will be large (~800-1000 lines). That's acceptable since it's purely type definitions.

**Reference:** Implement each class to match the VS Code API exactly. Use https://code.visualstudio.com/api/references/vscode-api as the authoritative reference. All types must serialize to plain JSON matching LSP shapes (e.g., `Position` → `{line, character}`, `Range` → `{start, end}`).

**Key implementation notes for tricky types:**
- `Position.translate(lineDelta, charDelta)` → returns new Position(line+lineDelta, character+charDelta)
- `Range.intersection(other)` → returns overlapping range or undefined
- `Range.union(other)` → returns smallest range containing both
- `WorkspaceEdit` stores changes internally as `Map<string, TextEdit[]>` (uri string → edits)
- `CodeActionKind.append(parts)` → returns new kind with `.parts` appended (e.g., `QuickFix.append('extract')` → `'quickfix.extract'`)
- `SemanticTokensBuilder.push(line, char, length, tokenType, tokenModifiers)` → stores in internal array, `build()` → returns delta-encoded `SemanticTokens`
- `Uri` — use the full implementation from Task 4 (`utils/uri.js`), re-export it here

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/types.js"`
Expected: No errors

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/types.js
git commit -m "feat(ext-host): add VS Code type implementations"
```

---

## Task 3: Event Infrastructure (`vscode-api/event.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/event.js`

VS Code's EventEmitter is different from Node's — it has an `.event` property that returns the subscribe function, and `.fire()` to emit.

- [ ] **Step 1: Create `event.js`**

```javascript
'use strict';

class Disposable {
    constructor(callOnDispose) {
        this._callOnDispose = callOnDispose;
        this._isDisposed = false;
    }
    static from(...disposables) {
        return new Disposable(() => {
            for (const d of disposables) {
                if (d && typeof d.dispose === 'function') d.dispose();
            }
        });
    }
    dispose() {
        if (!this._isDisposed) {
            this._isDisposed = true;
            if (this._callOnDispose) this._callOnDispose();
        }
    }
}

class EventEmitter {
    constructor() {
        this._listeners = [];
        this._event = null;
    }
    get event() {
        if (!this._event) {
            this._event = (listener, thisArgs, disposables) => {
                const bound = thisArgs ? listener.bind(thisArgs) : listener;
                this._listeners.push(bound);
                const disposable = new Disposable(() => {
                    const idx = this._listeners.indexOf(bound);
                    if (idx >= 0) this._listeners.splice(idx, 1);
                });
                if (disposables) disposables.push(disposable);
                return disposable;
            };
        }
        return this._event;
    }
    fire(data) {
        for (const listener of [...this._listeners]) {
            try { listener(data); } catch (e) { /* swallow */ }
        }
    }
    dispose() {
        this._listeners.length = 0;
        this._event = null;
    }
}

class CancellationToken {
    constructor() {
        this._isCancellationRequested = false;
        this._emitter = new EventEmitter();
    }
    get isCancellationRequested() { return this._isCancellationRequested; }
    get onCancellationRequested() { return this._emitter.event; }
    _cancel() {
        if (!this._isCancellationRequested) {
            this._isCancellationRequested = true;
            this._emitter.fire(undefined);
        }
    }
}

CancellationToken.None = Object.freeze((() => {
    const t = new CancellationToken(); return t;
})());

CancellationToken.Cancelled = Object.freeze((() => {
    const t = new CancellationToken(); t._isCancellationRequested = true; return t;
})());

class CancellationTokenSource {
    constructor(parent) {
        this._token = new CancellationToken();
        this._parentListener = null;
        if (parent) {
            this._parentListener = parent.onCancellationRequested(() => this.cancel());
        }
    }
    get token() { return this._token; }
    cancel() { this._token._cancel(); }
    dispose() {
        if (this._parentListener) this._parentListener.dispose();
    }
}

module.exports = { Disposable, EventEmitter, CancellationToken, CancellationTokenSource };
```

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/event.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/event.js
git commit -m "feat(ext-host): add EventEmitter, CancellationToken, Disposable"
```

---

## Task 4: Uri Implementation (`utils/uri.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/utils/uri.js`

Full VS Code Uri implementation. Critical because `vscode-languageclient` constructs URIs everywhere.

- [ ] **Step 1: Create `uri.js`**

Implement `Uri` class with:
- Constructor: `(scheme, authority, path, query, fragment)`
- Getters: `scheme`, `authority`, `path`, `query`, `fragment`, `fsPath`
- `fsPath` — converts URI path to OS file path (handles drive letters on Windows: `/c:/foo` → `c:\foo`)
- `toString(skipEncoding?)` — serializes to string form `scheme://authority/path?query#fragment`
- `toJSON()` — returns `{ $mid: 1, scheme, authority, path, query, fragment, fsPath }`
- `with(change)` — returns new Uri with changed components
- Static `file(path)` — creates `file://` URI from filesystem path
- Static `parse(value, strict?)` — parses URI string
- Static `from(components)` — creates from `{scheme, authority, path, query, fragment}`
- Static `joinPath(base, ...pathSegments)` — joins path segments onto a base URI

Handle Windows paths: `file:///c%3A/Users/...` ↔ `c:\Users\...`

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/utils/uri.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/utils/uri.js
git commit -m "feat(ext-host): add full VS Code Uri implementation"
```

---

## Task 5: Document Selector Matching (`utils/document-selector.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/utils/document-selector.js`

- [ ] **Step 1: Create `document-selector.js`**

```javascript
'use strict';

const path = require('path');

/**
 * Scores a document against a DocumentSelector.
 * Returns >0 if matches, 0 if no match.
 * Higher score = better match.
 *
 * Selector can be:
 * - string: matches languageId
 * - DocumentFilter: { language?, scheme?, pattern?, notebookType? }
 * - Array of string|DocumentFilter: matches if any element matches
 */
function score(selector, uri, languageId) {
    if (!selector) return 0;
    if (typeof selector === 'string') {
        return selector === languageId ? 10 : 0;
    }
    if (Array.isArray(selector)) {
        let best = 0;
        for (const item of selector) {
            const s = score(item, uri, languageId);
            if (s > best) best = s;
        }
        return best;
    }
    // DocumentFilter
    return scoreFilter(selector, uri, languageId);
}

function scoreFilter(filter, uri, languageId) {
    let result = 0;
    if (filter.language) {
        if (filter.language === languageId) result += 10;
        else if (filter.language === '*') result += 5;
        else return 0;
    }
    if (filter.scheme) {
        if (filter.scheme === uri.scheme) result += 10;
        else if (filter.scheme === '*') result += 5;
        else return 0;
    }
    if (filter.pattern) {
        if (matchGlob(filter.pattern, uri.fsPath || uri.path)) result += 10;
        else return 0;
    }
    // If no constraints specified, match everything weakly
    return result || 1;
}

function matchGlob(pattern, filePath) {
    // Simple glob matching: ** matches any path, * matches segment, ? matches char
    const normalized = filePath.replace(/\\/g, '/');
    const regex = globToRegex(pattern);
    return regex.test(normalized);
}

function globToRegex(glob) {
    let regex = '';
    let i = 0;
    while (i < glob.length) {
        const c = glob[i];
        if (c === '*') {
            if (glob[i + 1] === '*') {
                if (glob[i + 2] === '/') { regex += '(?:.+/)?'; i += 3; }
                else { regex += '.*'; i += 2; }
            } else {
                regex += '[^/]*'; i++;
            }
        } else if (c === '?') { regex += '[^/]'; i++; }
        else if (c === '{') {
            const end = glob.indexOf('}', i);
            if (end > -1) {
                const choices = glob.substring(i + 1, end).split(',').map(s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
                regex += '(?:' + choices.join('|') + ')';
                i = end + 1;
            } else { regex += '\\{'; i++; }
        } else {
            regex += c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
            i++;
        }
    }
    return new RegExp('^' + regex + '$', 'i');
}

module.exports = { score, matchGlob };
```

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/utils/document-selector.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/utils/document-selector.js
git commit -m "feat(ext-host): add DocumentSelector matching"
```

---

## Task 6: Document Manager (`document-manager.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/document-manager.js`

Maintains synchronized TextDocument objects. Fires events to extensions.

- [ ] **Step 1: Create `document-manager.js`**

Implement:
- `TextDocument` class: `uri`, `fileName`, `languageId`, `version`, `lineCount`, `isDirty`, `isClosed`, `eol`
  - `getText(range?)` — returns full text or text within range
  - `lineAt(lineOrPosition)` — returns `TextLine` object with `text`, `range`, `rangeIncludingLineBreak`, `firstNonWhitespaceCharacterIndex`, `isEmptyOrWhitespace`
  - `positionAt(offset)` — converts offset to Position
  - `offsetAt(position)` — converts Position to offset
  - `getWordRangeAtPosition(position, regex?)` — finds word boundaries
  - `validateRange(range)` / `validatePosition(position)` — clamp to document bounds
- `TextLine` class: `lineNumber`, `text`, `range`, `rangeIncludingLineBreak`, `firstNonWhitespaceCharacterIndex`, `isEmptyOrWhitespace`
- `DocumentManager` class:
  - `openDocument(uri, languageId, version, text)` — creates TextDocument, fires `onDidOpen`
  - `changeDocument(uri, version, text)` — updates content, fires `onDidChange` with `TextDocumentChangeEvent`
  - `closeDocument(uri)` — marks closed, fires `onDidClose`
  - `saveDocument(uri, text?)` — fires `onDidSave`
  - `getDocument(uri)` — returns TextDocument or undefined
  - `allDocuments` — returns array of all open documents
  - Events: `onDidOpen`, `onDidChange`, `onDidClose`, `onDidSave` (all EventEmitter instances)

Use the `EventEmitter` from `event.js` and `Position`/`Range` from `types.js`.

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/document-manager.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/document-manager.js
git commit -m "feat(ext-host): add TextDocument model and DocumentManager"
```

---

## Task 7: Provider Registry (`provider-registry.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/provider-registry.js`

Central store for all language feature providers registered by extensions.

- [ ] **Step 1: Create `provider-registry.js`**

Implement:
- `ProviderRegistry` class:
  - `register(type, selector, provider, metadata?)` — returns Disposable
  - `getProviders(type, uri, languageId)` — returns matching providers sorted by score
  - `hasProviders(type, uri, languageId)` — boolean check
  - `getRegistrations()` — all registrations (for notifying IDE)

Provider types (string constants):
```javascript
const PROVIDER_TYPES = {
    COMPLETION: 'completion',
    HOVER: 'hover',
    DEFINITION: 'definition',
    TYPE_DEFINITION: 'typeDefinition',
    IMPLEMENTATION: 'implementation',
    REFERENCES: 'references',
    DOCUMENT_HIGHLIGHT: 'documentHighlight',
    DOCUMENT_SYMBOL: 'documentSymbol',
    WORKSPACE_SYMBOL: 'workspaceSymbol',
    CODE_ACTION: 'codeAction',
    CODE_LENS: 'codeLens',
    DOCUMENT_FORMATTING: 'documentFormatting',
    DOCUMENT_RANGE_FORMATTING: 'documentRangeFormatting',
    ON_TYPE_FORMATTING: 'onTypeFormatting',
    RENAME: 'rename',
    SIGNATURE_HELP: 'signatureHelp',
    DOCUMENT_LINK: 'documentLink',
    COLOR: 'color',
    FOLDING_RANGE: 'foldingRange',
    SELECTION_RANGE: 'selectionRange',
    INLAY_HINT: 'inlayHint',
    LINKED_EDITING_RANGE: 'linkedEditingRange',
    DECLARATION: 'declaration',
    CALL_HIERARCHY: 'callHierarchy',
    TYPE_HIERARCHY: 'typeHierarchy',
    SEMANTIC_TOKENS: 'semanticTokens',
    DOCUMENT_DROP: 'documentDrop',
};
```

Request dispatching:
- `dispatchRequest(type, document, position, context, token)` — finds matching providers, calls them with timeout (5s), merges results per spec rules:
  - Completion: merge all results into single CompletionList
  - Hover: first non-null wins
  - Definition/References/etc.: concatenate all results
  - Formatting: first registered wins

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/provider-registry.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/provider-registry.js
git commit -m "feat(ext-host): add provider registry with selector matching"
```

---

## Task 8: Commands Namespace (`vscode-api/commands.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/commands.js`

- [ ] **Step 1: Create `commands.js`**

```javascript
'use strict';

const { Disposable } = require('./event');

function createCommandsApi(rpc, extensionId) {
    const localCommands = new Map(); // Commands registered in this host

    return {
        registerCommand(command, callback, thisArg) {
            const wrapped = thisArg ? callback.bind(thisArg) : callback;
            localCommands.set(command, wrapped);
            rpc.sendNotification('registerCommand', { command, extensionId });
            return new Disposable(() => {
                localCommands.delete(command);
            });
        },

        registerTextEditorCommand(command, callback, thisArg) {
            // Wraps callback with active editor injection
            return this.registerCommand(command, (...args) => {
                // activeTextEditor injected at call time
                const editor = require('./window')._getActiveTextEditor();
                if (editor) {
                    return callback.call(thisArg, editor, editor._edit, ...args);
                }
            });
        },

        async executeCommand(command, ...args) {
            // Check local first
            const local = localCommands.get(command);
            if (local) {
                return Promise.resolve(local(...args));
            }
            // Forward to IDE
            return rpc.sendRequest('executeCommand', { command, args });
        },

        async getCommands(filterInternal) {
            const local = [...localCommands.keys()];
            if (filterInternal) {
                return local.filter(c => !c.startsWith('_'));
            }
            return local;
        },

        // Internal: execute from IDE request
        _executeLocal(command, args) {
            const local = localCommands.get(command);
            if (local) return Promise.resolve(local(...(args || [])));
            return Promise.reject(new Error(`Command not found: ${command}`));
        }
    };
}

module.exports = { createCommandsApi };
```

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/commands.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/commands.js
git commit -m "feat(ext-host): add commands namespace"
```

---

## Task 9: Window Namespace (`vscode-api/window.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/window.js`

- [ ] **Step 1: Create `window.js`**

Implement `createWindowApi(rpc, extensionId)` returning:

- `showInformationMessage(message, ...itemsOrOptions)` — proxy to IDE via `window/showMessage`
- `showWarningMessage(message, ...itemsOrOptions)` — proxy to IDE
- `showErrorMessage(message, ...itemsOrOptions)` — proxy to IDE
- `showQuickPick(items, options, token?)` — proxy to IDE via `window/showQuickPick`
- `showInputBox(options?, token?)` — proxy to IDE via `window/showInputBox`
- `showOpenDialog(options)` — proxy to IDE
- `showSaveDialog(options)` — proxy to IDE
- `createOutputChannel(name, options?)` — real OutputChannel with append/appendLine/clear/show/hide/dispose; sends notifications to IDE
- `createStatusBarItem(alignmentOrOptions, priority?)` — real StatusBarItem with text/tooltip/command/color/show/hide; sends updates to IDE
- `showTextDocument(documentOrUri, columnOrOptions?, preserveFocus?)` — proxy to IDE
- `activeTextEditor` — getter, synced from IDE notifications
- `visibleTextEditors` — getter, synced from IDE
- `onDidChangeActiveTextEditor` — EventEmitter, fired by IDE notifications
- `onDidChangeVisibleTextEditors` — EventEmitter
- `onDidChangeTextEditorSelection` — EventEmitter
- `onDidChangeTextEditorVisibleRanges` — EventEmitter
- `createTreeView(viewId, options)` — creates TreeView proxy (stores provider, notifies IDE of creation, handles getChildren/getTreeItem requests)
- `registerTreeDataProvider(viewId, provider)` — shorthand for createTreeView
- `createWebviewPanel(viewType, title, showOptions, options?)` — creates WebView proxy (sends HTML to IDE, handles message passing)
- `withProgress(options, task)` — sends progress notifications to IDE, calls task with progress reporter
- `setStatusBarMessage(text, timeoutOrThenable?)` — temporary message
- `createTerminal(nameOrOptions?)` — proxy to IDE for terminal creation
- `registerWebviewViewProvider(viewId, provider, options?)` — for sidebar webviews

Internal methods for state sync:
- `_setActiveEditor(editorData)` — called by main.js on IDE notification
- `_setVisibleEditors(editorsData)` — called by main.js
- `_getActiveTextEditor()` — returns current TextEditor

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/window.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/window.js
git commit -m "feat(ext-host): add window namespace (messages, output, statusbar, TreeView, WebView)"
```

---

## Task 10: Workspace Namespace (`vscode-api/workspace.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/workspace.js`

- [ ] **Step 1: Create `workspace.js`**

Implement `createWorkspaceApi(rpc, documentManager)` returning:

**Document access:**
- `textDocuments` — getter returning `documentManager.allDocuments`
- `openTextDocument(uriOrOptions)` — if string/Uri, request IDE to open via `workspace/openTextDocument`; if options with content/language, create virtual document
- `onDidOpenTextDocument` — delegates to `documentManager.onDidOpen.event`
- `onDidChangeTextDocument` — delegates to `documentManager.onDidChange.event`
- `onDidCloseTextDocument` — delegates to `documentManager.onDidClose.event`
- `onDidSaveTextDocument` — delegates to `documentManager.onDidSave.event`

**Workspace:**
- `workspaceFolders` — getter, updated by IDE notifications
- `name` — getter, first workspace folder name
- `rootPath` — getter, deprecated but needed for compat, first folder path
- `workspaceFile` — getter
- `onDidChangeWorkspaceFolders` — EventEmitter
- `getWorkspaceFolder(uri)` — finds containing workspace folder
- `asRelativePath(pathOrUri, includeWorkspaceFolder?)` — relative path within workspace

**Configuration:**
- `getConfiguration(section?, scope?)` — returns Configuration proxy
  - Reads from in-memory settings store (pushed from IDE)
  - `get(key, defaultValue)` — synchronous read
  - `has(key)` — check existence
  - `update(key, value, configTarget?, overrideInLanguage?)` — async, sends to IDE
  - `inspect(key)` — returns { defaultValue, globalValue, workspaceValue, workspaceFolderValue }
- `onDidChangeConfiguration` — EventEmitter, fired when IDE pushes settings
- `_pushConfiguration(settings)` — internal, called on IDE notification

**File operations:**
- `findFiles(include, exclude?, maxResults?, token?)` — proxy to IDE via `workspace/findFiles`
- `saveAll(includeUntitled?)` — proxy to IDE
- `applyEdit(edit)` — sends WorkspaceEdit to IDE via `workspace/applyEdit`

**File system:**
- `fs` object:
  - `readFile(uri)` — proxy to IDE
  - `writeFile(uri, content)` — proxy to IDE
  - `stat(uri)` — proxy to IDE
  - `readDirectory(uri)` — proxy to IDE
  - `createDirectory(uri)` — proxy to IDE
  - `delete(uri, options?)` — proxy to IDE
  - `rename(source, target, options?)` — proxy to IDE
  - `copy(source, target, options?)` — proxy to IDE

**File watchers:**
- `createFileSystemWatcher(glob, ignoreCreate?, ignoreChange?, ignoreDelete?)` — creates watcher, registers with IDE, fires onDidCreate/onChange/onDidDelete events when IDE notifies

**Other:**
- `registerTextDocumentContentProvider(scheme, provider)` — virtual document support
- `registerFileSystemProvider(scheme, provider, options?)` — custom filesystem
- `registerTaskProvider(type, provider)` — delegates to tasks namespace

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/workspace.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/workspace.js
git commit -m "feat(ext-host): add workspace namespace (documents, config, fs, watchers)"
```

---

## Task 11: Languages Namespace (`vscode-api/languages.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/languages.js`

- [ ] **Step 1: Create `languages.js`**

Implement `createLanguagesApi(rpc, providerRegistry, extensionId)` returning:

**Provider registration (each returns Disposable, stores in registry, notifies IDE):**
- `registerCompletionItemProvider(selector, provider, ...triggerCharacters)`
- `registerHoverProvider(selector, provider)`
- `registerDefinitionProvider(selector, provider)`
- `registerTypeDefinitionProvider(selector, provider)`
- `registerImplementationProvider(selector, provider)`
- `registerReferenceProvider(selector, provider)`
- `registerDocumentHighlightProvider(selector, provider)`
- `registerDocumentSymbolProvider(selector, provider, metadata?)`
- `registerWorkspaceSymbolProvider(provider)`
- `registerCodeActionsProvider(selector, provider, metadata?)`
- `registerCodeLensProvider(selector, provider)`
- `registerDocumentFormattingEditProvider(selector, provider)`
- `registerDocumentRangeFormattingEditProvider(selector, provider)`
- `registerOnTypeFormattingEditProvider(selector, provider, firstTriggerCharacter, ...moreTriggerCharacter)`
- `registerRenameProvider(selector, provider)`
- `registerSignatureHelpProvider(selector, provider, ...triggerCharactersOrMetadata)` — handle both old (chars) and new (metadata) signatures
- `registerDocumentLinkProvider(selector, provider)`
- `registerColorProvider(selector, provider)`
- `registerFoldingRangeProvider(selector, provider)`
- `registerSelectionRangeProvider(selector, provider)`
- `registerInlayHintsProvider(selector, provider)`
- `registerLinkedEditingRangeProvider(selector, provider)`
- `registerDeclarationProvider(selector, provider)`
- `registerCallHierarchyProvider(selector, provider)`
- `registerTypeHierarchyProvider(selector, provider)`
- `registerDocumentSemanticTokensProvider(selector, provider, legend)`
- `registerDocumentRangeSemanticTokensProvider(selector, provider, legend)`
- `registerDocumentDropEditProvider(selector, provider)`
- `registerEvaluatableExpressionProvider(selector, provider)`
- `registerInlineValuesProvider(selector, provider)`

**Diagnostics:**
- `createDiagnosticCollection(name?)` — creates collection that publishes to IDE via `languages/publishDiagnostics`
  - `.set(uri, diagnostics)` — set diagnostics for uri
  - `.delete(uri)` — clear diagnostics for uri
  - `.clear()` — clear all
  - `.forEach(callback)` — iterate entries
  - `.get(uri)` — get diagnostics for uri
  - `.has(uri)` — check if uri has diagnostics
  - `.dispose()` — clear and notify IDE
- `getDiagnostics(resource?)` — returns diagnostics from all collections
- `onDidChangeDiagnostics` — EventEmitter

**Utilities:**
- `match(selector, document)` — delegates to `documentSelector.score()`
- `getLanguages()` — returns known language IDs
- `setTextDocumentLanguage(document, languageId)` — change language ID

Each `registerXxxProvider` call should:
1. Store provider in `providerRegistry` with selector and extension ID
2. Send `languages/registerProvider` notification to IDE with type, selector, and metadata (e.g., trigger characters)
3. Return Disposable that removes from registry

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/languages.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/languages.js
git commit -m "feat(ext-host): add languages namespace (20+ providers, diagnostics)"
```

---

## Task 12: Debug, Tasks, and Env Namespaces

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/debug.js`
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/tasks.js`
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/env.js`

- [ ] **Step 1: Create `debug.js`**

Implement `createDebugApi(rpc, extensionId)`:
- `registerDebugAdapterDescriptorFactory(debugType, factory)` — stores factory, notifies IDE
- `registerDebugConfigurationProvider(debugType, provider, triggerKind?)` — stores provider
- `startDebugging(folder, nameOrConfiguration, parentSessionOrOptions?)` — proxy to IDE
- `stopDebugging(session?)` — proxy to IDE
- `activeDebugSession` — getter
- `breakpoints` — getter
- `addBreakpoints(breakpoints)` / `removeBreakpoints(breakpoints)`
- `onDidStartDebugSession`, `onDidTerminateDebugSession`, `onDidChangeActiveDebugSession`, `onDidReceiveDebugSessionCustomEvent`, `onDidChangeBreakpoints` — EventEmitters
- Internal handlers for IDE to request debug adapter descriptors

- [ ] **Step 2: Create `tasks.js`**

Implement `createTasksApi(rpc, extensionId)`:
- `registerTaskProvider(type, provider)` — stores provider, notifies IDE
- `fetchTasks(filter?)` — collects from all providers
- `executeTask(task)` — proxy to IDE
- `taskExecutions` — getter
- `onDidStartTask`, `onDidEndTask`, `onDidStartTaskProcess`, `onDidEndTaskProcess` — EventEmitters
- Internal handlers for IDE to request task resolution

- [ ] **Step 3: Create `env.js`**

Implement `createEnvApi(rpc)`:
- `appName` — `'Visual Game Studio'`
- `appRoot` — from initialization data
- `appHost` — `'desktop'`
- `language` — from `process.env.VSCODE_NLS_CONFIG` or `'en'`
- `machineId` — generate stable hash from hostname
- `sessionId` — random UUID
- `isNewAppInstall` — `false`
- `isTelemetryEnabled` — `false`
- `uriScheme` — `'vscode'`
- `clipboard` — `{ readText: () => rpc.sendRequest('env/clipboardRead'), writeText: (text) => rpc.sendRequest('env/clipboardWrite', { text }) }`
- `openExternal(uri)` — proxy to IDE
- `asExternalUri(uri)` — return as-is for local
- `shell` — `process.env.SHELL || process.env.COMSPEC || '/bin/sh'`
- `remoteName` — `undefined`
- `logLevel` — `3` (Info)
- `onDidChangeLogLevel` — EventEmitter

- [ ] **Step 4: Verify all three**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/debug.js" && node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/tasks.js" && node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/env.js"`

- [ ] **Step 5: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/debug.js VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/tasks.js VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/env.js
git commit -m "feat(ext-host): add debug, tasks, and env namespaces"
```

---

## Task 13: VS Code API Factory (`vscode-api/index.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/index.js`

Creates per-extension API facades. This is the module returned when extensions `require('vscode')`.

- [ ] **Step 1: Create `index.js`**

```javascript
'use strict';

const types = require('./types');
const { EventEmitter, Disposable, CancellationTokenSource, CancellationToken } = require('./event');
const { createCommandsApi } = require('./commands');
const { createWindowApi } = require('./window');
const { createWorkspaceApi } = require('./workspace');
const { createLanguagesApi } = require('./languages');
const { createDebugApi } = require('./debug');
const { createTasksApi } = require('./tasks');
const { createEnvApi } = require('./env');

/**
 * Creates a per-extension VS Code API facade.
 * Each extension gets its own instance so subscriptions/context are isolated.
 */
function createVscodeApi(extensionId, extensionPath, rpc, documentManager, providerRegistry, config) {
    const commands = createCommandsApi(rpc, extensionId);
    const window = createWindowApi(rpc, extensionId);
    const workspace = createWorkspaceApi(rpc, documentManager);
    const languages = createLanguagesApi(rpc, providerRegistry, extensionId);
    const debug = createDebugApi(rpc, extensionId);
    const tasks = createTasksApi(rpc, extensionId);
    const env = createEnvApi(rpc);

    // Push initial config
    if (config) workspace._pushConfiguration(config);

    const vscode = {
        // Namespaces
        commands,
        window,
        workspace,
        languages,
        debug,
        tasks,
        env,

        // Extensions API
        extensions: {
            getExtension: null, // set by main.js after all facades created
            all: [],
            onDidChange: new EventEmitter().event,
        },

        // All types and enums spread at top level
        ...types,

        // Event infrastructure
        EventEmitter,
        Disposable,
        CancellationTokenSource,
        CancellationToken,

        // Extension context class
        ExtensionContext: createExtensionContextClass(extensionPath, rpc),

        // Extension mode
        ExtensionMode: { Production: 1, Development: 2, Test: 3 },
    };

    return vscode;
}

function createExtensionContextClass(extensionPath, rpc) {
    const path = require('path');
    const Uri = require('../utils/uri');

    return class ExtensionContext {
        constructor(extId, extPath, globalStoragePath, logPath) {
            this.subscriptions = [];
            this.extensionPath = extPath;
            this.extensionUri = Uri.file(extPath);
            this.globalStoragePath = globalStoragePath || path.join(extPath, '.storage');
            this.globalStorageUri = Uri.file(this.globalStoragePath);
            this.storagePath = path.join(extPath, '.storage');
            this.storageUri = Uri.file(this.storagePath);
            this.logPath = logPath || path.join(extPath, '.logs');
            this.logUri = Uri.file(this.logPath);
            this.extensionMode = 1; // Production

            // Memento implementations
            this.workspaceState = createMemento();
            this.globalState = createMemento();

            // Secrets API
            this.secrets = {
                get: (key) => rpc.sendRequest('secrets/get', { key }),
                store: (key, value) => rpc.sendRequest('secrets/store', { key, value }),
                delete: (key) => rpc.sendRequest('secrets/delete', { key }),
                onDidChange: new (require('./event').EventEmitter)().event,
            };

            // Environment
            this.environmentVariableCollection = {
                persistent: true,
                description: '',
                replace: () => {},
                append: () => {},
                prepend: () => {},
                get: () => undefined,
                forEach: () => {},
                delete: () => {},
                clear: () => {},
                [Symbol.iterator]: function*() {},
            };

            // Extension object
            this.extension = {
                id: extId,
                extensionUri: this.extensionUri,
                extensionPath: extPath,
                isActive: true,
                packageJSON: {},
                extensionKind: 1, // UI
                exports: undefined,
            };
        }
        asAbsolutePath(relativePath) {
            return path.join(this.extensionPath, relativePath);
        }
    };
}

function createMemento() {
    const store = new Map();
    return {
        keys() { return [...store.keys()]; },
        get(key, defaultValue) { return store.has(key) ? store.get(key) : defaultValue; },
        update(key, value) { store.set(key, value); return Promise.resolve(); },
    };
}

module.exports = { createVscodeApi };
```

- [ ] **Step 2: Verify syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/index.js"`

- [ ] **Step 3: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/vscode-api/index.js
git commit -m "feat(ext-host): add VS Code API factory with per-extension isolation"
```

---

## Task 14: Main Entry Point (`main.js`)

**Files:**
- Create: `VisualGameStudio.ProjectSystem/Services/ExtensionHost/main.js`
- Delete: `VisualGameStudio.ProjectSystem/Services/ExtensionHostMain.js`

The entry point that ties everything together: RPC setup, extension lifecycle, `require('vscode')` hook, request routing.

- [ ] **Step 1: Create `main.js`**

Implement:

**Initialization:**
- Create RPC instance, DocumentManager, ProviderRegistry
- Call `rpc.startReading()` to begin listening
- Register all RPC request/notification handlers

**`require('vscode')` Hook:**
- Use `Module._resolveFilename` to intercept `require('vscode')`
- Track which extension is currently being loaded (stack-based for nested requires)
- Return per-extension API facade based on calling extension's path
- Use `module.parent.filename` to determine caller if stack tracking fails

**RPC Request Handlers (IDE → Host):**
- `initialize` → store workspace folders, settings; respond with capabilities
- `activateExtension` → load extension module, create API facade, call `activate(context)`
- `deactivateExtension` → call `deactivate()`, dispose subscriptions
- `executeCommand` → delegate to commands._executeLocal()
- `fireActivationEvent` → notify loaded extensions
- `shutdown` → deactivate all, process.exit(0)
- `heartbeat` → respond immediately with `true`
- `textDocument/completion` → get document from manager, create Position, create CancellationToken, call `providerRegistry.dispatchRequest('completion', ...)`
- `textDocument/hover` → same pattern
- `textDocument/definition` → same pattern
- `textDocument/references` → same pattern
- (all 20+ provider request types follow the same pattern)
- `treeView/getChildren` → call stored TreeView provider's `getChildren(element)`
- `treeView/getTreeItem` → call stored TreeView provider's `getTreeItem(element)`
- `webview/postMessage` → forward to stored WebView panel's onDidReceiveMessage

**RPC Notification Handlers (IDE → Host):**
- `textDocument/didOpen` → `documentManager.openDocument(uri, languageId, version, text)`
- `textDocument/didChange` → `documentManager.changeDocument(uri, version, text)`
- `textDocument/didClose` → `documentManager.closeDocument(uri)`
- `textDocument/didSave` → `documentManager.saveDocument(uri, text)`
- `workspace/didChangeConfiguration` → push to all workspace API instances
- `workspace/didChangeWorkspaceFolders` → update workspace folders
- `activeEditor/didChange` → update window.activeTextEditor

**Extension Lifecycle:**
- `activateExtension(extensionPath, extensionId)`:
  1. Read `package.json` from extension path (try root, then `extension/` subfolder)
  2. If no `main` field → static-only extension, mark as active, return
  3. Create per-extension vscode API facade via `createVscodeApi()`
  4. Set up `require('vscode')` to return this facade
  5. `require(mainPath)` to load extension
  6. Call `module.activate(context)` if exists
  7. Store module, context, facade in loaded extensions map
  8. Send `extensionActivated` notification to IDE
- `deactivateExtension(extensionId)`:
  1. Call `module.deactivate()` if exists
  2. Dispose all `context.subscriptions`
  3. Remove from loaded extensions map

**Wire `vscode.extensions` API:**
- After all extensions are activated, wire `extensions.getExtension(id)` on each facade to look up from the loaded extensions map
- `extensions.all` getter returns array of `{ id, extensionPath, isActive, exports, packageJSON }` from all loaded extensions
- `extensions.onDidChange` fires when extensions are activated/deactivated

**Startup:**
- Send `log` notification: "Extension host started"
- Send `ready` notification

- [ ] **Step 2: Delete old `ExtensionHostMain.js`**

Remove `VisualGameStudio.ProjectSystem/Services/ExtensionHostMain.js`

- [ ] **Step 3: Verify `main.js` syntax**

Run: `node -c "VisualGameStudio.ProjectSystem/Services/ExtensionHost/main.js"`

- [ ] **Step 4: Commit**

```bash
git rm VisualGameStudio.ProjectSystem/Services/ExtensionHostMain.js
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost/
git commit -m "feat(ext-host): add main entry point, replace monolithic ExtensionHostMain.js"
```

---

## Task 15: Update C# `ExtensionHost.cs` — RPC + Crash Recovery

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs`

Update the C# side to use the new directory structure, new method names, and add crash recovery + provider request methods + document sync.

- [ ] **Step 1: Update script path**

The `_extensionHostScriptPath` currently points to `ExtensionHostMain.js`. Update `ExtensionService.cs` (or wherever the path is constructed) to point to `Services/ExtensionHost/main.js` instead.

Check `ExtensionService.cs:158` area for where `ExtensionHost` is constructed with the script path.

- [ ] **Step 2: Add crash recovery**

Add to `ExtensionHost.cs`:
- `_restartAttempts` counter field
- `_maxRestartDelay` = 30 seconds
- `_activeExtensions` list — track which extensions were activated (for re-activation after restart)
- In `OnHostProcessExited`:
  - If not intentional shutdown, auto-restart with exponential backoff: `delay = Math.Min(2^_restartAttempts * 2000, 30000)`
  - After restart, re-activate all previously active extensions
  - Reset `_restartAttempts` on successful heartbeat

- [ ] **Step 3: Update RPC method names**

Change all existing RPC method names in `ExtensionHost.cs` to match the new spec:
- `_rpc.AddLocalRpcMethod("host/registerCommand", ...)` → `_rpc.AddLocalRpcMethod("registerCommand", ...)`
- `_rpc.AddLocalRpcMethod("host/showMessage", ...)` → `_rpc.AddLocalRpcMethod("window/showMessage", ...)`
- `_rpc.AddLocalRpcMethod("host/createOutputChannel", ...)` → `_rpc.AddLocalRpcMethod("outputChannel/create", ...)`
- `_rpc.AddLocalRpcMethod("host/outputChannelAppend", ...)` → `_rpc.AddLocalRpcMethod("outputChannel/append", ...)`
- `_rpc.AddLocalRpcMethod("host/outputChannelAppendLine", ...)` → remove (merged into append)
- `_rpc.AddLocalRpcMethod("host/setStatusBarItem", ...)` → `_rpc.AddLocalRpcMethod("statusBar/update", ...)`
- `_rpc.AddLocalRpcMethod("host/registerCompletionProvider", ...)` → `_rpc.AddLocalRpcMethod("languages/registerProvider", ...)`
- `_rpc.AddLocalRpcMethod("host/registerHoverProvider", ...)` → remove (merged into languages/registerProvider)
- `_rpc.AddLocalRpcMethod("host/log", ...)` → `_rpc.AddLocalRpcMethod("log", ...)`

Add new RPC handlers:
- `_rpc.AddLocalRpcMethod("languages/publishDiagnostics", ...)` — receive diagnostics from extensions
- `_rpc.AddLocalRpcMethod("treeView/create", ...)` — extension created a tree view
- `_rpc.AddLocalRpcMethod("treeView/refresh", ...)` — tree view data changed
- `_rpc.AddLocalRpcMethod("webview/create", ...)` — extension created a webview panel
- `_rpc.AddLocalRpcMethod("webview/setHtml", ...)` — webview HTML updated
- `_rpc.AddLocalRpcMethod("webview/postMessage", ...)` — webview message from extension
- `_rpc.AddLocalRpcMethod("workspace/applyEdit", ...)` — extension wants to apply edits
- `_rpc.AddLocalRpcMethod("window/withProgress", ...)` — progress updates
- `_rpc.AddLocalRpcMethod("extensionActivated", ...)` — extension activated notification

- [ ] **Step 4: Add document sync methods**

Add to `ExtensionHost.cs`:
```csharp
public async Task NotifyDocumentOpenedAsync(string uri, string languageId, int version, string text, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return;
    try { await _rpc.NotifyAsync("textDocument/didOpen", new { uri, languageId, version, text }); }
    catch { }
}

public async Task NotifyDocumentChangedAsync(string uri, int version, string text, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return;
    try { await _rpc.NotifyAsync("textDocument/didChange", new { uri, version, text }); }
    catch { }
}

public async Task NotifyDocumentClosedAsync(string uri, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return;
    try { await _rpc.NotifyAsync("textDocument/didClose", new { uri }); }
    catch { }
}

public async Task NotifyDocumentSavedAsync(string uri, string? text = null, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return;
    try { await _rpc.NotifyAsync("textDocument/didSave", new { uri, text }); }
    catch { }
}

public async Task NotifyConfigurationChangedAsync(object settings, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return;
    try { await _rpc.NotifyAsync("workspace/didChangeConfiguration", new { settings }); }
    catch { }
}

public async Task NotifyActiveEditorChangedAsync(string? uri, string? languageId, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return;
    try { await _rpc.NotifyAsync("activeEditor/didChange", new { uri, languageId }); }
    catch { }
}
```

- [ ] **Step 5: Add provider request methods**

Add generic provider request method and specific shortcuts:
```csharp
public async Task<JsonElement?> RequestProviderAsync(string method, object parameters, CancellationToken ct = default)
{
    if (!IsRunning || _rpc == null) return null;
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        return await _rpc.InvokeWithCancellationAsync<JsonElement?>(method, new[] { parameters }, cts.Token);
    }
    catch { return null; }
}

// Shortcuts for common providers
public Task<JsonElement?> RequestCompletionAsync(string uri, int line, int character, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/completion", new { uri, position = new { line, character } }, ct);

public Task<JsonElement?> RequestHoverAsync(string uri, int line, int character, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/hover", new { uri, position = new { line, character } }, ct);

public Task<JsonElement?> RequestDefinitionAsync(string uri, int line, int character, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/definition", new { uri, position = new { line, character } }, ct);

public Task<JsonElement?> RequestReferencesAsync(string uri, int line, int character, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/references", new { uri, position = new { line, character } }, ct);

public Task<JsonElement?> RequestFormattingAsync(string uri, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/formatting", new { uri }, ct);

public Task<JsonElement?> RequestCodeActionsAsync(string uri, int startLine, int startChar, int endLine, int endChar, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/codeAction", new { uri, range = new { start = new { line = startLine, character = startChar }, end = new { line = endLine, character = endChar } } }, ct);

public Task<JsonElement?> RequestDocumentSymbolsAsync(string uri, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/documentSymbol", new { uri }, ct);

public Task<JsonElement?> RequestSignatureHelpAsync(string uri, int line, int character, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/signatureHelp", new { uri, position = new { line, character } }, ct);

public Task<JsonElement?> RequestRenameAsync(string uri, int line, int character, string newName, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/rename", new { uri, position = new { line, character }, newName }, ct);

public Task<JsonElement?> RequestFoldingRangesAsync(string uri, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/foldingRange", new { uri }, ct);

public Task<JsonElement?> RequestInlayHintsAsync(string uri, int startLine, int startChar, int endLine, int endChar, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/inlayHint", new { uri, range = new { start = new { line = startLine, character = startChar }, end = new { line = endLine, character = endChar } } }, ct);

public Task<JsonElement?> RequestSemanticTokensAsync(string uri, CancellationToken ct = default)
    => RequestProviderAsync("textDocument/semanticTokens", new { uri }, ct);
```

- [ ] **Step 6: Add diagnostics event**

```csharp
public event EventHandler<ExtensionDiagnosticsEventArgs>? DiagnosticsReceived;

// In StartAsync, register handler:
_rpc.AddLocalRpcMethod("languages/publishDiagnostics", new Action<string, JsonElement, string?>(OnPublishDiagnostics));

private void OnPublishDiagnostics(string uri, JsonElement diagnostics, string? collectionName)
{
    DiagnosticsReceived?.Invoke(this, new ExtensionDiagnosticsEventArgs
    {
        Uri = uri,
        Diagnostics = diagnostics,
        CollectionName = collectionName ?? ""
    });
}
```

Add event args class:
```csharp
public class ExtensionDiagnosticsEventArgs : EventArgs
{
    public string Uri { get; set; } = "";
    public JsonElement Diagnostics { get; set; }
    public string CollectionName { get; set; } = "";
}
```

- [ ] **Step 7: Commit**

```bash
git add VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs
git commit -m "feat(ext-host): update C# ExtensionHost with new RPC, crash recovery, provider requests"
```

---

## Task 16: Update C# `ExtensionService.cs` — Wire Providers Into IDE

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/ExtensionService.cs`
- Modify: `VisualGameStudio.Core/Abstractions/Services/IExtensionService.cs`

- [ ] **Step 1: Add provider request methods to `IExtensionService.cs`**

After the existing `ExecuteExtensionCommandAsync` method (around line 111), add:
```csharp
/// <summary>Request completion items from extension providers.</summary>
Task<JsonElement?> RequestCompletionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

/// <summary>Request hover info from extension providers.</summary>
Task<JsonElement?> RequestHoverAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

/// <summary>Request go-to-definition from extension providers.</summary>
Task<JsonElement?> RequestDefinitionAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

/// <summary>Request find-references from extension providers.</summary>
Task<JsonElement?> RequestReferencesAsync(string uri, int line, int character, CancellationToken cancellationToken = default);

/// <summary>Request document formatting from extension providers.</summary>
Task<JsonElement?> RequestFormattingAsync(string uri, CancellationToken cancellationToken = default);

/// <summary>Request document symbols from extension providers.</summary>
Task<JsonElement?> RequestDocumentSymbolsAsync(string uri, CancellationToken cancellationToken = default);

/// <summary>Check if extension host has providers for a language.</summary>
bool HasExtensionProviders(string languageId);

/// <summary>Notify extension host of document open.</summary>
Task NotifyDocumentOpenedAsync(string uri, string languageId, int version, string text, CancellationToken cancellationToken = default);

/// <summary>Notify extension host of document change.</summary>
Task NotifyDocumentChangedAsync(string uri, int version, string text, CancellationToken cancellationToken = default);

/// <summary>Notify extension host of document close.</summary>
Task NotifyDocumentClosedAsync(string uri, CancellationToken cancellationToken = default);

/// <summary>Notify extension host of document save.</summary>
Task NotifyDocumentSavedAsync(string uri, string? text = null, CancellationToken cancellationToken = default);

/// <summary>Extension-published diagnostics.</summary>
event EventHandler<ExtensionDiagnosticsEventArgs>? ExtensionDiagnosticsReceived;
```

- [ ] **Step 2: Implement in `ExtensionService.cs`**

Add implementations that delegate to `_extensionHost`:
```csharp
// Track which languages have extension providers
private readonly HashSet<string> _extensionProviderLanguages = new();

public bool HasExtensionProviders(string languageId)
    => _extensionProviderLanguages.Contains(languageId);

public async Task<JsonElement?> RequestCompletionAsync(string uri, int line, int character, CancellationToken ct = default)
{
    if (_extensionHost == null || !_extensionHost.IsRunning) return null;
    return await _extensionHost.RequestCompletionAsync(uri, line, character, ct);
}

// (Same pattern for RequestHoverAsync, RequestDefinitionAsync, etc.)

public async Task NotifyDocumentOpenedAsync(string uri, string languageId, int version, string text, CancellationToken ct = default)
{
    if (_extensionHost == null || !_extensionHost.IsRunning) return;
    await _extensionHost.NotifyDocumentOpenedAsync(uri, languageId, version, text, ct);
    // Also trigger onLanguage activation event
    await NotifyLanguageOpenedAsync(languageId);
}

// (Same pattern for other Notify methods)

public event EventHandler<ExtensionDiagnosticsEventArgs>? ExtensionDiagnosticsReceived;
```

- [ ] **Step 3: Wire diagnostics from ExtensionHost to ExtensionService**

In `StartExtensionHostAsync`, subscribe to `_extensionHost.DiagnosticsReceived`:
```csharp
_extensionHost.DiagnosticsReceived += (s, e) =>
{
    ExtensionDiagnosticsReceived?.Invoke(this, e);
};
```

- [ ] **Step 4: Wire provider registrations**

In the `languages/registerProvider` handler, track which languages have providers:
```csharp
_extensionHost.ProviderRegistered += (s, e) =>
{
    if (e.Selector != null)
    {
        // Parse selector for language IDs and add to tracking set
        foreach (var lang in ExtractLanguageIds(e.Selector))
        {
            _extensionProviderLanguages.Add(lang);
        }
    }
};
```

- [ ] **Step 5: Update script path for new directory structure**

In `StartExtensionHostAsync` (line ~158), update the path construction to point to `ExtensionHost/main.js` instead of `ExtensionHostMain.js`.

- [ ] **Step 6: Commit**

```bash
git add VisualGameStudio.Core/Abstractions/Services/IExtensionService.cs VisualGameStudio.ProjectSystem/Services/ExtensionService.cs
git commit -m "feat(ext-host): wire extension providers into IDE via IExtensionService"
```

---

## Task 17: Wire Document Events from Editor to Extension Host

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs`

The IDE must notify the extension host when documents are opened, changed, closed, and saved.

- [ ] **Step 1: Add document open notification**

In `CodeEditorDocumentViewModel`, when a document is loaded/opened, call:
```csharp
await _extensionService.NotifyDocumentOpenedAsync(
    fileUri,
    languageId,
    version: 1,
    text: editor.Text);
```

Find the document initialization point (where text is first loaded into the editor) and add this call.

- [ ] **Step 2: Add document change notification**

Subscribe to the editor's `TextChanged` event and forward to extension host:
```csharp
private int _documentVersion = 1;

private async void OnEditorTextChanged(object? sender, EventArgs e)
{
    _documentVersion++;
    if (_extensionService.HasExtensionProviders(LanguageId))
    {
        await _extensionService.NotifyDocumentChangedAsync(
            FileUri, _documentVersion, Editor.Text);
    }
}
```

Throttle to avoid flooding: only send after 100ms debounce.

- [ ] **Step 3: Add document close notification**

In the document close/dispose handler:
```csharp
await _extensionService.NotifyDocumentClosedAsync(FileUri);
```

- [ ] **Step 4: Add document save notification**

In the save handler:
```csharp
await _extensionService.NotifyDocumentSavedAsync(FileUri, Editor.Text);
```

- [ ] **Step 5: Commit**

```bash
git add VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs
git commit -m "feat(ext-host): wire document events from editor to extension host"
```

---

## Task 18: Wire Completion/Hover from Extension Host to Editor

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs`

When the built-in LSP doesn't have a language server for a file type, fall back to extension providers.

- [ ] **Step 1: Add extension completion fallback**

In the completion request handler (around line 704-720), after the built-in LSP completion:
```csharp
// If built-in LSP returned nothing and extension host has providers, try extension
if ((completions == null || completions.Count == 0) && _extensionService.HasExtensionProviders(LanguageId))
{
    var extResult = await _extensionService.RequestCompletionAsync(FileUri, line, column);
    if (extResult.HasValue)
    {
        // Parse JSON completion items into IDE completion model
        completions = ParseExtensionCompletions(extResult.Value);
    }
}
```

Implement `ParseExtensionCompletions(JsonElement)` to convert LSP-shaped CompletionItem JSON to the IDE's completion model.

- [ ] **Step 2: Add extension hover fallback**

Similarly in the hover/data tip handler:
```csharp
if (hoverResult == null && _extensionService.HasExtensionProviders(LanguageId))
{
    var extResult = await _extensionService.RequestHoverAsync(FileUri, line, column);
    if (extResult.HasValue)
    {
        hoverText = ParseExtensionHover(extResult.Value);
    }
}
```

- [ ] **Step 3: Add extension definition fallback**

In the go-to-definition handler, add extension fallback.

- [ ] **Step 4: Commit**

```bash
git add VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs
git commit -m "feat(ext-host): wire extension completions and hover into editor"
```

---

## Task 19: Build and Smoke Test

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/VisualGameStudio.ProjectSystem.csproj` (ensure new JS files are copied to output)

- [ ] **Step 1: Ensure JS files are included in build output**

Add to the `.csproj`:
```xml
<ItemGroup>
  <None Include="Services\ExtensionHost\**\*.js" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Remove old reference if it exists:
```xml
<!-- Remove this if present -->
<None Include="Services\ExtensionHostMain.js" ... />
```

- [ ] **Step 2: Build the IDE**

Run: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release`
Expected: Build succeeds with no errors

- [ ] **Step 3: Verify Node.js host starts**

Run: `node "VisualGameStudio.ProjectSystem/Services/ExtensionHost/main.js"` with a test initialization message piped to stdin. Verify it responds with `ready` notification.

- [ ] **Step 4: Copy to IDE folder**

Copy build output to `IDE/` folder so the IDE binary uses the new extension host.

- [ ] **Step 5: Commit**

```bash
git add VisualGameStudio.ProjectSystem/VisualGameStudio.ProjectSystem.csproj
git commit -m "feat(ext-host): include Node.js extension host files in build output"
```

---

## Task 20: Integration Test with HTML Extension

- [ ] **Step 1: Download HTML extension**

Download the `vscode.html-language-features` extension from Open VSX:
```bash
# Or use the IDE's extension manager
curl -L "https://open-vsx.org/api/vscode/html-language-features/1.85.0/file/vscode.html-language-features-1.85.0.vsix" -o html-ext.vsix
```

- [ ] **Step 2: Install in IDE extensions directory**

Extract to `~/.vgs/extensions/vscode.html-language-features-1.85.0/`

- [ ] **Step 3: Test activation**

Launch IDE, open an HTML file. Verify in Output panel:
- Extension host starts
- HTML extension activates
- Language server spawns as child process
- Completion works (type `<` and see HTML tag suggestions)
- Hover works (hover over an HTML tag and see documentation)

- [ ] **Step 4: Commit any fixes**

```bash
git add -A
git commit -m "fix(ext-host): integration test fixes for HTML extension"
```

---

## Summary

| Task | Component | Est. Lines |
|------|-----------|-----------|
| 1 | rpc.js | ~120 |
| 2 | types.js | ~900 |
| 3 | event.js | ~100 |
| 4 | uri.js | ~200 |
| 5 | document-selector.js | ~80 |
| 6 | document-manager.js | ~250 |
| 7 | provider-registry.js | ~200 |
| 8 | commands.js | ~60 |
| 9 | window.js | ~350 |
| 10 | workspace.js | ~300 |
| 11 | languages.js | ~250 |
| 12 | debug.js + tasks.js + env.js | ~300 |
| 13 | vscode-api/index.js | ~150 |
| 14 | main.js | ~350 |
| 15 | ExtensionHost.cs updates | ~200 |
| 16 | ExtensionService.cs + IExtensionService.cs | ~150 |
| 17 | Document event wiring | ~80 |
| 18 | Completion/hover wiring | ~100 |
| 19 | Build config | ~20 |
| 20 | Integration test | ~0 (manual) |
| **Total** | | **~3,660** |

**Parallelization:** Tasks 1-13 (Node.js files) can be **written** in parallel by separate agents — each agent gets full code/spec for its file. Runtime cross-`require()` dependencies exist (Tasks 6-13 import from Tasks 2, 3, 5) but `node -c` syntax checks work without dependencies present. All Node.js files must be present before integration testing. Tasks 15-16 (C# changes) can also run in parallel with Node.js tasks since they only need the RPC method names (defined in spec). Tasks 17-18 depend on Tasks 15-16. Tasks 19-20 are sequential integration steps.

**Agent assignment for 10 parallel agents:**
- Agent 1: Task 1 (rpc.js)
- Agent 2: Task 2 (types.js) — largest file, full agent
- Agent 3: Task 3 (event.js) + Task 5 (document-selector.js)
- Agent 4: Task 4 (uri.js)
- Agent 5: Task 6 (document-manager.js) + Task 7 (provider-registry.js)
- Agent 6: Task 8 (commands.js) + Task 12 (debug.js, tasks.js, env.js)
- Agent 7: Task 9 (window.js)
- Agent 8: Task 10 (workspace.js) + Task 11 (languages.js)
- Agent 9: Task 13 (index.js) + Task 14 (main.js)
- Agent 10: Task 15 (ExtensionHost.cs) + Task 16 (ExtensionService.cs + IExtensionService.cs)
