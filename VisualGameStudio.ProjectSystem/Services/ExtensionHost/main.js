'use strict';

// ---------------------------------------------------------------------------
// main.js — Extension Host entry point
//
// Replaces the monolithic ExtensionHostMain.js. Launches a JSON-RPC server
// that manages VS Code-compatible extensions for the Avalonia IDE.
// ---------------------------------------------------------------------------

const path = require('path');
const fs = require('fs');
const Module = require('module');

const rpc = require('./rpc');
const { DocumentManager } = require('./document-manager');
const { createVscodeApi, createMemento } = require('./vscode-api/index');
const { EventEmitter, CancellationTokenSource } = require('./vscode-api/event');
const Uri = require('./utils/uri');

// Optional: provider-registry may not exist yet
let ProviderRegistry = null;
try {
    const mod = require('./provider-registry');
    ProviderRegistry = mod.ProviderRegistry || mod;
} catch (_) { /* provider-registry.js not yet created */ }

// Optional: types module
let types = {};
try { types = require('./vscode-api/types'); } catch (_) { /* types.js not yet created */ }

// ---------------------------------------------------------------------------
// Global state
// ---------------------------------------------------------------------------

const documentManager = new DocumentManager();
const providerRegistry = ProviderRegistry ? new ProviderRegistry() : null;

/** Map<extensionId, { path, isActive, exports, module, context, facade, pkg }> */
const loadedExtensions = new Map();

/** Map<extensionId, vscode API facade> */
const extensionFacades = new Map();

/** Set during activation so the require('vscode') hook returns the right facade */
let currentActivatingExtensionId = null;

/** Workspace folders received from IDE */
let workspaceFolders = [];

/** Global configuration snapshot */
let globalConfig = null;

// ---------------------------------------------------------------------------
// require('vscode') hook
//
// When an extension does `const vscode = require('vscode')`, Node resolves
// the module name to the literal string 'vscode' and then hits the cache
// proxy, which returns the correct per-extension facade.
// ---------------------------------------------------------------------------

const originalResolve = Module._resolveFilename;

Module._resolveFilename = function (request, parent) {
    if (request === 'vscode') {
        return 'vscode';
    }
    return originalResolve.apply(this, arguments);
};

// Proxy the module cache so that require.cache['vscode'] returns the
// correct facade for whichever extension is currently being activated or
// is requiring it.
const realCache = Module._cache;
let _cachedCacheProxy = null;

Object.defineProperty(Module, '_cache', {
    get() {
        if (!_cachedCacheProxy) {
            _cachedCacheProxy = new Proxy(realCache, {
            get(target, prop) {
                if (prop === 'vscode') {
                    // During activation we know exactly which extension is loading
                    if (currentActivatingExtensionId && extensionFacades.has(currentActivatingExtensionId)) {
                        return {
                            id: 'vscode',
                            filename: 'vscode',
                            loaded: true,
                            exports: extensionFacades.get(currentActivatingExtensionId),
                        };
                    }
                    // Fallback: return the last-activated extension's facade
                    const lastId = [...extensionFacades.keys()].pop();
                    if (lastId) {
                        return {
                            id: 'vscode',
                            filename: 'vscode',
                            loaded: true,
                            exports: extensionFacades.get(lastId),
                        };
                    }
                    return undefined;
                }
                return target[prop];
            },
            set(target, prop, value) {
                target[prop] = value;
                return true;
            },
            has(target, prop) {
                return prop === 'vscode' || prop in target;
            },
        });
        }
        return _cachedCacheProxy;
    },
});

// ---------------------------------------------------------------------------
// Extension lifecycle
// ---------------------------------------------------------------------------

/**
 * Activate an extension by path and id.
 * @param {string} extensionPath  Absolute path to the extension folder
 * @param {string} extensionId    Unique extension identifier
 * @returns {Promise<object>}
 */
async function activateExtension(extensionPath, extensionId) {
    try {
        // 1. Locate package.json (root or extension/ subfolder for extracted VSIX)
        let pkgPath = path.join(extensionPath, 'package.json');
        if (!fs.existsSync(pkgPath)) {
            const altPath = path.join(extensionPath, 'extension', 'package.json');
            if (fs.existsSync(altPath)) {
                extensionPath = path.join(extensionPath, 'extension');
                pkgPath = altPath;
            } else {
                throw new Error(`package.json not found in ${extensionPath}`);
            }
        }

        const pkg = JSON.parse(fs.readFileSync(pkgPath, 'utf8'));

        // 2. If no main field → static-only extension (theme, snippet, grammar)
        const mainField = pkg.main || pkg.browser;
        if (!mainField) {
            loadedExtensions.set(extensionId, {
                path: extensionPath,
                isActive: true,
                exports: {},
                module: null,
                context: null,
                facade: null,
                pkg,
            });
            return { activated: true, hasMain: false };
        }

        const mainPath = path.resolve(extensionPath, mainField);
        if (!fs.existsSync(mainPath) && !fs.existsSync(mainPath + '.js')) {
            // Entry point referenced but file missing — treat as static
            loadedExtensions.set(extensionId, {
                path: extensionPath,
                isActive: true,
                exports: {},
                module: null,
                context: null,
                facade: null,
                pkg,
            });
            return { activated: true, hasMain: false };
        }

        // 3. Create per-extension VS Code API facade
        const facade = createVscodeApi(
            extensionId,
            extensionPath,
            rpc,
            documentManager,
            providerRegistry,
            globalConfig
        );

        // 4. Set workspace folders on the facade
        if (workspaceFolders.length > 0) {
            facade.workspace._setWorkspaceFolders(workspaceFolders);
        }

        // 5. Wire extensions.getExtension so this extension can query others
        wireExtensionsApi(facade);

        // 6. Store facade in map before require so nested requires see it
        extensionFacades.set(extensionId, facade);
        currentActivatingExtensionId = extensionId;

        // 7. Require the extension module
        let extensionModule;
        try {
            extensionModule = require(mainPath);
        } catch (requireErr) {
            extensionFacades.delete(extensionId);
            currentActivatingExtensionId = null;
            throw requireErr;
        }

        // 8. Create ExtensionContext
        const subscriptions = [];
        const context = createExtensionContext(extensionId, extensionPath, subscriptions, facade, pkg);

        // 9. Call activate()
        let exports = {};
        if (typeof extensionModule.activate === 'function') {
            exports = (await Promise.resolve(extensionModule.activate(context))) || {};
        }

        // 10. Store everything
        loadedExtensions.set(extensionId, {
            path: extensionPath,
            isActive: true,
            exports,
            module: extensionModule,
            context,
            facade,
            pkg,
        });

        // 11. Notify IDE
        rpc.sendNotification('extensionActivated', { extensionId });

        // 12. Clean up
        currentActivatingExtensionId = null;

        return { activated: true, hasMain: true };
    } catch (err) {
        currentActivatingExtensionId = null;
        extensionFacades.delete(extensionId);
        rpc.sendNotification('log', {
            level: 'error',
            message: `Failed to activate ${extensionId}: ${err.message}\n${err.stack || ''}`,
        });
        return { activated: false, error: err.message };
    }
}

/**
 * Deactivate a single extension.
 */
async function deactivateExtension(extensionId) {
    const ext = loadedExtensions.get(extensionId);
    if (!ext) {
        return { deactivated: false, error: 'Extension not loaded' };
    }

    try {
        // Call deactivate() if provided
        if (ext.module && typeof ext.module.deactivate === 'function') {
            await Promise.resolve(ext.module.deactivate());
        }

        // Dispose all subscriptions
        if (ext.context && ext.context.subscriptions) {
            for (const sub of ext.context.subscriptions) {
                try { sub.dispose(); } catch (_) { /* best effort */ }
            }
        }

        // Clean up maps
        loadedExtensions.delete(extensionId);
        extensionFacades.delete(extensionId);

        return { deactivated: true };
    } catch (err) {
        return { deactivated: false, error: err.message };
    }
}

/**
 * Deactivate all loaded extensions.
 */
async function deactivateAll() {
    const ids = [...loadedExtensions.keys()];
    for (const id of ids) {
        await deactivateExtension(id);
    }
}

// ---------------------------------------------------------------------------
// ExtensionContext factory
// ---------------------------------------------------------------------------

function createExtensionContext(extensionId, extensionPath, subscriptions, facade, pkg) {
    const extensionUri = Uri.file(extensionPath);
    const storagePath = path.join(extensionPath, '.storage');
    const globalStoragePath = path.join(extensionPath, '.global-storage');
    const logPath = path.join(extensionPath, '.logs');

    return {
        subscriptions,
        extensionPath,
        extensionUri,
        storagePath,
        storageUri: Uri.file(storagePath),
        globalStoragePath,
        globalStorageUri: Uri.file(globalStoragePath),
        logPath,
        logUri: Uri.file(logPath),
        extensionMode: 1, // Production
        workspaceState: createMemento(),
        globalState: createMemento(),

        asAbsolutePath(relativePath) {
            return path.join(extensionPath, relativePath);
        },

        get extension() {
            return {
                id: extensionId,
                extensionUri,
                extensionPath,
                isActive: true,
                packageJSON: pkg,
                extensionKind: 1, // UI
                exports: loadedExtensions.has(extensionId)
                    ? loadedExtensions.get(extensionId).exports
                    : {},
            };
        },

        // Secrets — proxy to IDE via RPC
        secrets: {
            get(key) {
                return rpc.sendRequest('secrets/get', { extensionId, key });
            },
            store(key, value) {
                return rpc.sendRequest('secrets/store', { extensionId, key, value });
            },
            delete(key) {
                return rpc.sendRequest('secrets/delete', { extensionId, key });
            },
            onDidChange: new EventEmitter().event,
        },

        // Environment variables for the extension
        environmentVariableCollection: {
            persistent: true,
            replace() {},
            append() {},
            prepend() {},
            get() { return undefined; },
            forEach() {},
            delete() {},
            clear() {},
            [Symbol.iterator]() { return [][Symbol.iterator](); },
        },
    };
}

// ---------------------------------------------------------------------------
// Wire extensions.getExtension / extensions.all on a facade
// ---------------------------------------------------------------------------

function wireExtensionsApi(facade) {
    facade.extensions.getExtension = function (id) {
        const ext = loadedExtensions.get(id);
        if (!ext) return undefined;
        return {
            id,
            extensionPath: ext.path,
            extensionUri: Uri.file(ext.path),
            isActive: ext.isActive,
            exports: ext.exports,
            packageJSON: ext.pkg,
            extensionKind: 1,
            activate() {
                // Already active
                return Promise.resolve(ext.exports);
            },
        };
    };

    facade.extensions.all = new Proxy([], {
        get(target, prop) {
            if (prop === 'length') return loadedExtensions.size;
            if (prop === Symbol.iterator) {
                return function* () {
                    for (const [id, ext] of loadedExtensions) {
                        yield {
                            id,
                            extensionPath: ext.path,
                            extensionUri: Uri.file(ext.path),
                            isActive: ext.isActive,
                            exports: ext.exports,
                            packageJSON: ext.pkg,
                            extensionKind: 1,
                        };
                    }
                };
            }
            if (typeof prop === 'string' && /^\d+$/.test(prop)) {
                const idx = parseInt(prop, 10);
                const entries = [...loadedExtensions.entries()];
                if (idx >= 0 && idx < entries.length) {
                    const [id, ext] = entries[idx];
                    return {
                        id,
                        extensionPath: ext.path,
                        extensionUri: Uri.file(ext.path),
                        isActive: ext.isActive,
                        exports: ext.exports,
                        packageJSON: ext.pkg,
                        extensionKind: 1,
                    };
                }
                return undefined;
            }
            // Array methods like forEach, map, filter
            if (typeof Array.prototype[prop] === 'function') {
                const arr = [];
                for (const [id, ext] of loadedExtensions) {
                    arr.push({
                        id,
                        extensionPath: ext.path,
                        extensionUri: Uri.file(ext.path),
                        isActive: ext.isActive,
                        exports: ext.exports,
                        packageJSON: ext.pkg,
                        extensionKind: 1,
                    });
                }
                return arr[prop].bind(arr);
            }
            return target[prop];
        },
    });
}

// ---------------------------------------------------------------------------
// RPC Request Handlers (IDE → Host)
// ---------------------------------------------------------------------------

rpc.onRequest('initialize', (params) => {
    if (params.workspaceFolders) {
        workspaceFolders = params.workspaceFolders;
        // Push to already-loaded extensions
        for (const [, ext] of loadedExtensions) {
            if (ext.facade) ext.facade.workspace._setWorkspaceFolders(workspaceFolders);
        }
    }
    if (params.configuration) {
        globalConfig = params.configuration;
        for (const [, ext] of loadedExtensions) {
            if (ext.facade) ext.facade.workspace._pushConfiguration(params.configuration);
        }
    }
    return {
        capabilities: {
            commands: true,
            providers: true,
            treeViews: true,
            webviews: true,
        },
    };
});

rpc.onRequest('activateExtension', async (params) => {
    return await activateExtension(params.extensionPath, params.extensionId);
});

rpc.onRequest('deactivateExtension', async (params) => {
    return await deactivateExtension(params.extensionId);
});

rpc.onRequest('executeCommand', async (params) => {
    const command = params.command;
    const args = params.args || [];

    // Try each loaded extension's local command registry
    for (const [, ext] of loadedExtensions) {
        if (!ext.facade) continue;
        try {
            const result = await ext.facade.commands._executeLocal(command, args);
            return result;
        } catch (err) {
            if (err && err.message && err.message.startsWith('Command not found:')) continue;
            throw err;
        }
    }
    throw new Error(`Command not found: ${command}`);
});

rpc.onRequest('fireActivationEvent', async (params) => {
    const event = params.event;
    const results = [];
    for (const [id, ext] of loadedExtensions) {
        if (!ext.pkg || !ext.pkg.activationEvents) continue;
        if (ext.pkg.activationEvents.includes(event) || ext.pkg.activationEvents.includes('*')) {
            results.push(id);
        }
    }
    return { notified: results };
});

rpc.onRequest('heartbeat', () => {
    return true;
});

rpc.onRequest('shutdown', async () => {
    await deactivateAll();
    process.exit(0);
});

// ---------------------------------------------------------------------------
// Provider request handlers (textDocument/* → providerRegistry)
// ---------------------------------------------------------------------------

const providerRequestTypes = [
    ['textDocument/completion', 'completion', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
        context: p.context,
    })],
    ['textDocument/hover', 'hover', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
    })],
    ['textDocument/definition', 'definition', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
    })],
    ['textDocument/references', 'references', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
        context: p.context || { includeDeclaration: true },
    })],
    ['textDocument/formatting', 'documentFormatting', (p, doc) => ({
        document: doc,
        options: p.options || {},
    })],
    ['textDocument/rangeFormatting', 'documentRangeFormatting', (p, doc) => ({
        document: doc,
        range: makeRange(p.range),
        options: p.options || {},
    })],
    ['textDocument/codeAction', 'codeAction', (p, doc) => ({
        document: doc,
        range: makeRange(p.range),
        context: p.context || { diagnostics: [] },
    })],
    ['textDocument/codeLens', 'codeLens', (p, doc) => ({
        document: doc,
    })],
    ['textDocument/documentSymbol', 'documentSymbol', (p, doc) => ({
        document: doc,
    })],
    ['textDocument/signatureHelp', 'signatureHelp', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
        context: p.context,
    })],
    ['textDocument/rename', 'rename', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
        newName: p.newName,
    })],
    ['textDocument/foldingRange', 'foldingRange', (p, doc) => ({
        document: doc,
    })],
    ['textDocument/documentLink', 'documentLink', (p, doc) => ({
        document: doc,
    })],
    ['textDocument/selectionRange', 'selectionRange', (p, doc) => ({
        document: doc,
        positions: (p.positions || []).map(makePosition),
    })],
    ['textDocument/inlayHint', 'inlayHint', (p, doc) => ({
        document: doc,
        range: makeRange(p.range),
    })],
    ['textDocument/semanticTokens', 'semanticTokens', (p, doc) => ({
        document: doc,
    })],
    ['textDocument/documentHighlight', 'documentHighlight', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
    })],
    ['textDocument/linkedEditingRange', 'linkedEditingRange', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
    })],
    ['textDocument/prepareCallHierarchy', 'callHierarchy', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
    })],
    ['textDocument/prepareTypeHierarchy', 'typeHierarchy', (p, doc) => ({
        document: doc,
        position: makePosition(p.position),
    })],
    ['workspace/symbol', 'workspaceSymbol', (p) => ({
        query: p.query,
    })],
];

// Helper: create a Position from { line, character }
function makePosition(pos) {
    if (!pos) return { line: 0, character: 0 };
    // Use types.Position if available, otherwise plain object
    if (types.Position) return new types.Position(pos.line, pos.character);
    return { line: pos.line, character: pos.character };
}

// Helper: create a Range from { start, end }
function makeRange(range) {
    if (!range) return { start: { line: 0, character: 0 }, end: { line: 0, character: 0 } };
    if (types.Range) {
        return new types.Range(
            range.start.line, range.start.character,
            range.end.line, range.end.character
        );
    }
    return {
        start: { line: range.start.line, character: range.start.character },
        end: { line: range.end.line, character: range.end.character },
    };
}

// Register all provider request handlers
for (const [method, type, paramMapper] of providerRequestTypes) {
    rpc.onRequest(method, async (params, requestId) => {
        // If no provider registry, return null
        if (!providerRegistry || typeof providerRegistry.dispatchRequest !== 'function') {
            return null;
        }

        const isWorkspaceRequest = type === 'workspaceSymbol';
        const doc = isWorkspaceRequest ? null : documentManager.getDocument(params.uri);
        if (!doc && !isWorkspaceRequest) return null;

        const cts = new CancellationTokenSource();
        if (requestId !== undefined) {
            rpc.registerCancelCallback(requestId, () => cts.cancel());
        }

        try {
            const mapped = paramMapper(params, doc);
            return await providerRegistry.dispatchRequest(type, doc, mapped, cts.token);
        } finally {
            if (requestId !== undefined) {
                rpc.unregisterCancelCallback(requestId);
            }
            cts.dispose();
        }
    });
}

// ---------------------------------------------------------------------------
// TreeView request handlers
// ---------------------------------------------------------------------------

rpc.onRequest('treeView/getChildren', async (params) => {
    const viewId = params.viewId;
    const element = params.element; // null for root

    // Search all extensions for a registered TreeView with this ID
    for (const [, ext] of loadedExtensions) {
        if (!ext.facade || !ext.facade.window._treeViews) continue;
        const treeView = ext.facade.window._treeViews.get(viewId);
        if (!treeView || !treeView._provider) continue;

        const provider = treeView._provider;
        if (typeof provider.getChildren === 'function') {
            const children = await Promise.resolve(provider.getChildren(element || undefined));
            if (!children) return [];

            // Resolve each child to a TreeItem
            const items = [];
            for (const child of children) {
                let treeItem = child;
                if (typeof provider.getTreeItem === 'function') {
                    treeItem = await Promise.resolve(provider.getTreeItem(child));
                }
                items.push({
                    label: typeof treeItem.label === 'string' ? treeItem.label : (treeItem.label && treeItem.label.label) || '',
                    description: treeItem.description || '',
                    tooltip: treeItem.tooltip || '',
                    collapsibleState: treeItem.collapsibleState || 0,
                    command: treeItem.command || null,
                    iconPath: treeItem.iconPath || null,
                    contextValue: treeItem.contextValue || '',
                    id: treeItem.id || undefined,
                    _element: child, // keep reference for subsequent getChildren calls
                });
            }
            return items;
        }
    }
    return [];
});

rpc.onRequest('treeView/getTreeItem', async (params) => {
    const viewId = params.viewId;
    const element = params.element;

    for (const [, ext] of loadedExtensions) {
        if (!ext.facade || !ext.facade.window._treeViews) continue;
        const treeView = ext.facade.window._treeViews.get(viewId);
        if (!treeView || !treeView._provider) continue;

        const provider = treeView._provider;
        if (typeof provider.getTreeItem === 'function') {
            const item = await Promise.resolve(provider.getTreeItem(element));
            if (!item) return null;
            return {
                label: typeof item.label === 'string' ? item.label : (item.label && item.label.label) || '',
                description: item.description || '',
                tooltip: item.tooltip || '',
                collapsibleState: item.collapsibleState || 0,
                command: item.command || null,
                iconPath: item.iconPath || null,
                contextValue: item.contextValue || '',
                id: item.id || undefined,
            };
        }
    }
    return null;
});

// ---------------------------------------------------------------------------
// WebView message forwarding
// ---------------------------------------------------------------------------

rpc.onRequest('webview/postMessage', (params) => {
    const panelId = params.panelId;
    const message = params.message;

    for (const [, ext] of loadedExtensions) {
        if (!ext.facade || !ext.facade.window._webviewPanels) continue;
        const panel = ext.facade.window._webviewPanels.get(panelId);
        if (panel && panel.webview && panel.webview._onDidReceiveMessage) {
            panel.webview._onDidReceiveMessage.fire(message);
            return true;
        }
    }
    return false;
});

// ---------------------------------------------------------------------------
// RPC Notification Handlers (IDE → Host)
// ---------------------------------------------------------------------------

rpc.onNotification('textDocument/didOpen', (params) => {
    documentManager.openDocument(
        Uri.parse(params.uri),
        params.languageId,
        params.version,
        params.text
    );
});

rpc.onNotification('textDocument/didChange', (params) => {
    documentManager.changeDocument(params.uri, params.version, params.text);
});

rpc.onNotification('textDocument/didClose', (params) => {
    documentManager.closeDocument(params.uri);
});

rpc.onNotification('textDocument/didSave', (params) => {
    documentManager.saveDocument(params.uri, params.text);
});

rpc.onNotification('workspace/didChangeConfiguration', (params) => {
    globalConfig = params.settings;
    for (const [, ext] of loadedExtensions) {
        if (ext.facade) {
            ext.facade.workspace._pushConfiguration(params.settings);
        }
    }
});

rpc.onNotification('workspace/didChangeWorkspaceFolders', (params) => {
    if (params.added) {
        workspaceFolders = workspaceFolders.concat(params.added);
    }
    if (params.removed) {
        const removedUris = new Set((params.removed || []).map(f => f.uri || f));
        workspaceFolders = workspaceFolders.filter(f => !removedUris.has(f.uri || f));
    }
    for (const [, ext] of loadedExtensions) {
        if (ext.facade) {
            ext.facade.workspace._setWorkspaceFolders(workspaceFolders);
        }
    }
});

rpc.onNotification('activeEditor/didChange', (params) => {
    for (const [, ext] of loadedExtensions) {
        if (ext.facade) {
            ext.facade.window._setActiveEditor(params);
        }
    }
});

// ---------------------------------------------------------------------------
// Start the RPC reader and announce readiness
// ---------------------------------------------------------------------------

rpc.startReading();

rpc.sendNotification('log', { level: 'info', message: 'Extension host started' });
rpc.sendNotification('ready', {});
