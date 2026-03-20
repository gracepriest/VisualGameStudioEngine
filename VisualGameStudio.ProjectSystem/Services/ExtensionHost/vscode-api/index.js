'use strict';

// ---------------------------------------------------------------------------
// vscode-api/index.js — Factory that creates per-extension VS Code API facades
//
// Each extension gets its own isolated instance so subscriptions, commands,
// and configuration are scoped to that extension.
// ---------------------------------------------------------------------------

const path = require('path');
const { EventEmitter, Disposable, CancellationTokenSource, CancellationToken } = require('./event');
const { createCommandsApi } = require('./commands');
const { createWindowApi } = require('./window');
const { createWorkspaceApi } = require('./workspace');
const { createDebugApi } = require('./debug');
const { createTasksApi } = require('./tasks');
const { createEnvApi } = require('./env');

// Optional modules — may not exist yet; provide safe fallbacks
let types = {};
try { types = require('./types'); } catch (_) { /* types.js not yet created */ }

let createLanguagesApi = null;
try { createLanguagesApi = require('./languages').createLanguagesApi; } catch (_) { /* languages.js not yet created */ }

// ---------------------------------------------------------------------------
// Enum constants that VS Code exposes at the top level
// ---------------------------------------------------------------------------

const ExtensionMode = Object.freeze({
    Production: 1,
    Development: 2,
    Test: 3,
});

const StatusBarAlignment = Object.freeze({ Left: 1, Right: 2 });

const DiagnosticSeverity = Object.freeze({
    Error: 0,
    Warning: 1,
    Information: 2,
    Hint: 3,
});

const CompletionItemKind = Object.freeze({
    Text: 0, Method: 1, Function: 2, Constructor: 3, Field: 4,
    Variable: 5, Class: 6, Interface: 7, Module: 8, Property: 9,
    Unit: 10, Value: 11, Enum: 12, Keyword: 13, Snippet: 14,
    Color: 15, File: 16, Reference: 17, Folder: 18, EnumMember: 19,
    Constant: 20, Struct: 21, Event: 22, Operator: 23, TypeParameter: 24,
});

const CompletionItemTag = Object.freeze({ Deprecated: 1 });

const SymbolKind = Object.freeze({
    File: 0, Module: 1, Namespace: 2, Package: 3, Class: 4,
    Method: 5, Property: 6, Field: 7, Constructor: 8, Enum: 9,
    Interface: 10, Function: 11, Variable: 12, Constant: 13, String: 14,
    Number: 15, Boolean: 16, Array: 17, Object: 18, Key: 19,
    Null: 20, EnumMember: 21, Struct: 22, Event: 23, Operator: 24,
    TypeParameter: 25,
});

const TreeItemCollapsibleState = Object.freeze({
    None: 0,
    Collapsed: 1,
    Expanded: 2,
});

const ViewColumn = Object.freeze({
    Active: -1, Beside: -2,
    One: 1, Two: 2, Three: 3, Four: 4, Five: 5,
    Six: 6, Seven: 7, Eight: 8, Nine: 9,
});

const TextEditorRevealType = Object.freeze({
    Default: 0,
    InCenter: 1,
    InCenterIfOutsideViewport: 2,
    AtTop: 3,
});

const OverviewRulerLane = Object.freeze({
    Left: 1, Center: 2, Right: 4, Full: 7,
});

const DecorationRangeBehavior = Object.freeze({
    OpenOpen: 0, ClosedClosed: 1, OpenClosed: 2, ClosedOpen: 3,
});

const EndOfLine = Object.freeze({ LF: 1, CRLF: 2 });

const FoldingRangeKind = Object.freeze({
    Comment: 1, Imports: 2, Region: 3,
});

const IndentAction = Object.freeze({
    None: 0, Indent: 1, IndentOutdent: 2, Outdent: 3,
});

const CodeActionKind = Object.freeze({
    Empty: '',
    QuickFix: 'quickfix',
    Refactor: 'refactor',
    RefactorExtract: 'refactor.extract',
    RefactorInline: 'refactor.inline',
    RefactorRewrite: 'refactor.rewrite',
    Source: 'source',
    SourceOrganizeImports: 'source.organizeImports',
    SourceFixAll: 'source.fixAll',
});

const InlayHintKind = Object.freeze({ Type: 1, Parameter: 2 });

const SemanticTokensLegend = class {
    constructor(tokenTypes, tokenModifiers) {
        this.tokenTypes = tokenTypes || [];
        this.tokenModifiers = tokenModifiers || [];
    }
};

const SemanticTokensBuilder = class {
    constructor(legend) {
        this._legend = legend;
        this._data = [];
        this._prevLine = 0;
        this._prevChar = 0;
    }
    push(line, char, length, tokenType, tokenModifiers) {
        const deltaLine = line - this._prevLine;
        const deltaChar = deltaLine === 0 ? char - this._prevChar : char;
        this._data.push(deltaLine, deltaChar, length, tokenType, tokenModifiers || 0);
        this._prevLine = line;
        this._prevChar = char;
    }
    build() {
        return { data: new Uint32Array(this._data) };
    }
};

// Fallback enums — types.js may provide richer versions
const builtinEnums = {
    StatusBarAlignment,
    DiagnosticSeverity,
    CompletionItemKind,
    CompletionItemTag,
    SymbolKind,
    TreeItemCollapsibleState,
    ViewColumn,
    TextEditorRevealType,
    OverviewRulerLane,
    DecorationRangeBehavior,
    EndOfLine,
    FoldingRangeKind,
    IndentAction,
    CodeActionKind,
    InlayHintKind,
    SemanticTokensLegend,
    SemanticTokensBuilder,
    ExtensionMode,
};

// ---------------------------------------------------------------------------
// createVscodeApi — builds an isolated API facade for one extension
// ---------------------------------------------------------------------------

/**
 * @param {string} extensionId     Unique extension identifier (publisher.name)
 * @param {string} extensionPath   Absolute filesystem path to the extension root
 * @param {object} rpc             RPC module (sendRequest, sendNotification, etc.)
 * @param {object} documentManager DocumentManager instance
 * @param {object|null} providerRegistry ProviderRegistry instance (may be null)
 * @param {object|null} config     Initial workspace configuration snapshot
 * @returns {object} A VS Code-compatible API object
 */
function createVscodeApi(extensionId, extensionPath, rpc, documentManager, providerRegistry, config) {
    const commands = createCommandsApi(rpc, extensionId);
    const window = createWindowApi(rpc, extensionId);
    const workspace = createWorkspaceApi(rpc, documentManager);
    const debug = createDebugApi(rpc, extensionId);
    const tasks = createTasksApi(rpc, extensionId);
    const env = createEnvApi(rpc);

    // Languages API is optional — depends on languages.js + provider-registry.js
    let languages = null;
    if (createLanguagesApi && providerRegistry) {
        languages = createLanguagesApi(rpc, providerRegistry, extensionId);
    } else {
        // Minimal stub so extensions that touch vscode.languages don't crash
        languages = {
            registerCompletionItemProvider() { return Disposable.NONE; },
            registerHoverProvider() { return Disposable.NONE; },
            registerDefinitionProvider() { return Disposable.NONE; },
            registerReferenceProvider() { return Disposable.NONE; },
            registerDocumentFormattingEditProvider() { return Disposable.NONE; },
            registerDocumentRangeFormattingEditProvider() { return Disposable.NONE; },
            registerCodeActionsProvider() { return Disposable.NONE; },
            registerCodeLensProvider() { return Disposable.NONE; },
            registerDocumentSymbolProvider() { return Disposable.NONE; },
            registerWorkspaceSymbolProvider() { return Disposable.NONE; },
            registerSignatureHelpProvider() { return Disposable.NONE; },
            registerRenameProvider() { return Disposable.NONE; },
            registerFoldingRangeProvider() { return Disposable.NONE; },
            registerDocumentLinkProvider() { return Disposable.NONE; },
            registerSelectionRangeProvider() { return Disposable.NONE; },
            registerInlayHintsProvider() { return Disposable.NONE; },
            registerDocumentSemanticTokensProvider() { return Disposable.NONE; },
            registerDocumentHighlightProvider() { return Disposable.NONE; },
            registerLinkedEditingRangeProvider() { return Disposable.NONE; },
            registerCallHierarchyProvider() { return Disposable.NONE; },
            registerTypeHierarchyProvider() { return Disposable.NONE; },
            createDiagnosticCollection(name) {
                return {
                    name: name || '',
                    set() {},
                    delete() {},
                    clear() {},
                    forEach() {},
                    get() { return []; },
                    has() { return false; },
                    dispose() {},
                    [Symbol.iterator]() { return [][Symbol.iterator](); },
                };
            },
            getDiagnostics() { return []; },
            getLanguages() { return Promise.resolve([]); },
            setLanguageConfiguration() { return Disposable.NONE; },
            match() { return 0; },
        };
    }

    // Push initial configuration if available
    if (config) {
        workspace._pushConfiguration(config);
    }

    // Build the extensions sub-API — getExtension is wired later by main.js
    const extensionsChangeEmitter = new EventEmitter();
    const extensions = {
        getExtension: null,   // Will be wired by main.js after all facades are created
        all: [],              // Will be replaced with a Proxy by main.js
        allAcrossExtensionHosts: [],  // VS Code internal API used by some extensions
        onDidChange: extensionsChangeEmitter.event,
        _fireDidChange() { extensionsChangeEmitter.fire(undefined); },
    };

    // Assemble the final facade
    const vscode = {
        // Sub-APIs
        commands,
        window,
        workspace,
        languages,
        debug,
        tasks,
        env,
        extensions,

        // Core event/disposal classes
        EventEmitter,
        Disposable,
        CancellationTokenSource,
        CancellationToken,

        // Enums & constants (builtins first, then override with types.js if present)
        ...builtinEnums,
        ...types,

        // ExtensionMode is always present
        ExtensionMode,

        // Version — vscode-languageclient validates this
        version: '1.95.0',

        // Localization API (vscode.l10n)
        l10n: {
            t(messageOrOptions, ...args) {
                // Simple passthrough — returns the message as-is (no translation)
                let message = typeof messageOrOptions === 'string'
                    ? messageOrOptions
                    : messageOrOptions.message;
                // Simple {0}, {1} substitution
                if (args.length > 0) {
                    for (let i = 0; i < args.length; i++) {
                        message = message.replace(`{${i}}`, String(args[i]));
                    }
                } else if (typeof messageOrOptions === 'object' && messageOrOptions.args) {
                    const a = messageOrOptions.args;
                    if (Array.isArray(a)) {
                        for (let i = 0; i < a.length; i++) {
                            message = message.replace(`{${i}}`, String(a[i]));
                        }
                    } else {
                        for (const [key, val] of Object.entries(a)) {
                            message = message.replace(`{${key}}`, String(val));
                        }
                    }
                }
                return message;
            },
            bundle: undefined,
            uri: undefined,
        },
    };

    return vscode;
}

// ---------------------------------------------------------------------------
// Memento — simple in-memory key/value store for ExtensionContext state
// ---------------------------------------------------------------------------

function createMemento() {
    const store = new Map();
    return {
        keys() {
            return [...store.keys()];
        },
        get(key, defaultValue) {
            return store.has(key) ? store.get(key) : defaultValue;
        },
        update(key, value) {
            if (value === undefined) {
                store.delete(key);
            } else {
                store.set(key, value);
            }
            return Promise.resolve();
        },
    };
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

module.exports = { createVscodeApi, createMemento };
