/**
 * Visual Game Studio - Extension Host
 *
 * This Node.js process hosts VS Code-compatible extensions.
 * It communicates with the IDE via JSON-RPC over stdin/stdout.
 *
 * Protocol:
 *   IDE → Host: JSON-RPC requests/notifications
 *   Host → IDE: JSON-RPC responses/notifications
 */

'use strict';

const path = require('path');
const fs = require('fs');
const readline = require('readline');

// ─── JSON-RPC Protocol ──────────────────────────────────────────

let messageId = 1;
const pendingRequests = new Map();
const registeredCommands = new Map();
const outputChannels = new Map();
const loadedExtensions = new Map();

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
        sendMessage({ jsonrpc: '2.0', id, error: { code: -1, message: String(error) } });
    } else {
        sendMessage({ jsonrpc: '2.0', id, result: result ?? null });
    }
}

// ─── Message Reader ─────────────────────────────────────────────

let buffer = '';

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
    buffer += chunk;
    while (true) {
        const headerEnd = buffer.indexOf('\r\n\r\n');
        if (headerEnd === -1) break;

        const header = buffer.substring(0, headerEnd);
        const match = header.match(/Content-Length:\s*(\d+)/i);
        if (!match) {
            buffer = buffer.substring(headerEnd + 4);
            continue;
        }

        const contentLength = parseInt(match[1], 10);
        const contentStart = headerEnd + 4;
        if (buffer.length < contentStart + contentLength) break;

        const content = buffer.substring(contentStart, contentStart + contentLength);
        buffer = buffer.substring(contentStart + contentLength);

        try {
            const msg = JSON.parse(content);
            handleMessage(msg);
        } catch (e) {
            sendNotification('log', { level: 'error', message: `Parse error: ${e.message}` });
        }
    }
});

// ─── Message Handler ────────────────────────────────────────────

function handleMessage(msg) {
    if (msg.id !== undefined && msg.method) {
        // Request
        handleRequest(msg.id, msg.method, msg.params || {});
    } else if (msg.id !== undefined) {
        // Response
        const pending = pendingRequests.get(msg.id);
        if (pending) {
            pendingRequests.delete(msg.id);
            if (msg.error) pending.reject(new Error(msg.error.message));
            else pending.resolve(msg.result);
        }
    } else if (msg.method) {
        // Notification
        handleNotification(msg.method, msg.params || {});
    }
}

function handleRequest(id, method, params) {
    switch (method) {
        case 'initialize':
            sendResponse(id, { capabilities: { commands: true, themes: true, snippets: true } });
            break;

        case 'activateExtension':
            activateExtension(params.extensionPath, params.extensionId)
                .then(result => sendResponse(id, result))
                .catch(err => sendResponse(id, null, err.message));
            break;

        case 'deactivateExtension':
            deactivateExtension(params.extensionId)
                .then(result => sendResponse(id, result))
                .catch(err => sendResponse(id, null, err.message));
            break;

        case 'executeCommand':
            executeCommand(params.command, params.args)
                .then(result => sendResponse(id, result))
                .catch(err => sendResponse(id, null, err.message));
            break;

        case 'shutdown':
            deactivateAll().then(() => {
                sendResponse(id, { ok: true });
                process.exit(0);
            });
            break;

        default:
            sendResponse(id, null, `Unknown method: ${method}`);
    }
}

function handleNotification(method, params) {
    switch (method) {
        case 'textDocument/didOpen':
        case 'textDocument/didChange':
        case 'textDocument/didClose':
            // Forward to extensions that registered document listeners
            broadcastToExtensions(method, params);
            break;
    }
}

// ─── VS Code API Shim ──────────────────────────────────────────

function createVscodeApi(extensionId, extensionPath) {
    const subscriptions = [];

    const vscode = {
        // Core enums
        StatusBarAlignment: { Left: 1, Right: 2 },
        DiagnosticSeverity: { Error: 0, Warning: 1, Information: 2, Hint: 3 },
        CompletionItemKind: { Text: 0, Method: 1, Function: 2, Constructor: 3, Field: 4, Variable: 5, Class: 6, Interface: 7, Module: 8, Property: 9, Unit: 10, Value: 11, Enum: 12, Keyword: 13, Snippet: 14, Color: 15, File: 16, Reference: 17, Folder: 18 },
        TreeItemCollapsibleState: { None: 0, Collapsed: 1, Expanded: 2 },
        ViewColumn: { Active: -1, Beside: -2, One: 1, Two: 2, Three: 3 },

        // Position and Range
        Position: class Position {
            constructor(line, character) { this.line = line; this.character = character; }
        },
        Range: class Range {
            constructor(startLine, startChar, endLine, endChar) {
                if (typeof startLine === 'object') {
                    this.start = startLine; this.end = startChar;
                } else {
                    this.start = new vscode.Position(startLine, startChar);
                    this.end = new vscode.Position(endLine, endChar);
                }
            }
        },
        Uri: {
            file: (p) => ({ scheme: 'file', path: p, fsPath: p, toString: () => `file://${p}` }),
            parse: (s) => ({ scheme: 'file', path: s, fsPath: s, toString: () => s })
        },

        // Commands API
        commands: {
            registerCommand(command, callback) {
                registeredCommands.set(command, { callback, extensionId });
                sendNotification('registerCommand', { command, extensionId });
                const disposable = { dispose() { registeredCommands.delete(command); } };
                subscriptions.push(disposable);
                return disposable;
            },
            executeCommand(command, ...args) {
                return executeCommand(command, args);
            },
            getCommands() {
                return Promise.resolve([...registeredCommands.keys()]);
            }
        },

        // Window API
        window: {
            showInformationMessage(message, ...items) {
                return sendRequest('window/showMessage', { type: 'info', message, items });
            },
            showWarningMessage(message, ...items) {
                return sendRequest('window/showMessage', { type: 'warning', message, items });
            },
            showErrorMessage(message, ...items) {
                return sendRequest('window/showMessage', { type: 'error', message, items });
            },
            showQuickPick(items, options) {
                return sendRequest('window/showQuickPick', { items, options });
            },
            showInputBox(options) {
                return sendRequest('window/showInputBox', { options });
            },
            createOutputChannel(name) {
                const channel = {
                    name,
                    _content: [],
                    append(text) { this._content.push(text); sendNotification('outputChannel/append', { name, text }); },
                    appendLine(text) { this.append(text + '\n'); },
                    clear() { this._content = []; sendNotification('outputChannel/clear', { name }); },
                    show() { sendNotification('outputChannel/show', { name }); },
                    hide() { sendNotification('outputChannel/hide', { name }); },
                    dispose() { outputChannels.delete(name); }
                };
                outputChannels.set(name, channel);
                sendNotification('outputChannel/create', { name });
                return channel;
            },
            createStatusBarItem(alignment, priority) {
                let _text = '', _tooltip = '', _command = '', _visible = false;
                const item = {
                    get text() { return _text; },
                    set text(v) { _text = v; this._update(); },
                    get tooltip() { return _tooltip; },
                    set tooltip(v) { _tooltip = v; this._update(); },
                    get command() { return _command; },
                    set command(v) { _command = v; this._update(); },
                    show() { _visible = true; this._update(); },
                    hide() { _visible = false; this._update(); },
                    dispose() { sendNotification('statusBar/dispose', { extensionId }); },
                    _update() {
                        sendNotification('statusBar/update', {
                            extensionId, text: _text, tooltip: _tooltip,
                            command: _command, visible: _visible,
                            alignment: alignment || 1, priority: priority || 0
                        });
                    }
                };
                return item;
            },
            activeTextEditor: null,
            visibleTextEditors: [],
            onDidChangeActiveTextEditor: createEventEmitter(),
            onDidChangeVisibleTextEditors: createEventEmitter(),
            showTextDocument(uri, options) {
                return sendRequest('window/showTextDocument', { uri: uri.toString(), options });
            }
        },

        // Workspace API
        workspace: {
            rootPath: null,
            workspaceFolders: [],
            getConfiguration(section) {
                return {
                    get(key, defaultValue) {
                        // Synchronous — return default for now, async would break API contract
                        return defaultValue;
                    },
                    has(key) { return false; },
                    update(key, value, global) {
                        return sendRequest('configuration/update', { section, key, value, global });
                    }
                };
            },
            openTextDocument(uri) {
                return sendRequest('workspace/openTextDocument', { uri: typeof uri === 'string' ? uri : uri.toString() });
            },
            findFiles(include, exclude, maxResults) {
                return sendRequest('workspace/findFiles', { include, exclude, maxResults });
            },
            createFileSystemWatcher(glob) {
                return {
                    onDidCreate: createEventEmitter(),
                    onDidChange: createEventEmitter(),
                    onDidDelete: createEventEmitter(),
                    dispose() {}
                };
            },
            onDidOpenTextDocument: createEventEmitter(),
            onDidCloseTextDocument: createEventEmitter(),
            onDidChangeTextDocument: createEventEmitter(),
            onDidSaveTextDocument: createEventEmitter()
        },

        // Languages API
        languages: {
            registerCompletionItemProvider(selector, provider, ...triggerChars) {
                const id = `completion_${extensionId}_${messageId++}`;
                sendNotification('languages/registerProvider', {
                    type: 'completion', id, extensionId, selector, triggerChars
                });
                return { dispose() { sendNotification('languages/unregisterProvider', { id }); } };
            },
            registerHoverProvider(selector, provider) {
                const id = `hover_${extensionId}_${messageId++}`;
                sendNotification('languages/registerProvider', {
                    type: 'hover', id, extensionId, selector
                });
                return { dispose() { sendNotification('languages/unregisterProvider', { id }); } };
            },
            createDiagnosticCollection(name) {
                return {
                    name,
                    set(uri, diagnostics) {
                        sendNotification('diagnostics/set', { uri: uri.toString(), diagnostics, name });
                    },
                    delete(uri) {
                        sendNotification('diagnostics/delete', { uri: uri.toString(), name });
                    },
                    clear() {
                        sendNotification('diagnostics/clear', { name });
                    },
                    dispose() {}
                };
            }
        },

        // Extensions API
        extensions: {
            getExtension(id) {
                const ext = loadedExtensions.get(id);
                return ext ? { id, extensionPath: ext.path, isActive: ext.isActive, exports: ext.exports } : undefined;
            },
            get all() {
                return [...loadedExtensions.entries()].map(([id, ext]) => ({
                    id, extensionPath: ext.path, isActive: ext.isActive, exports: ext.exports
                }));
            },
            onDidChange: createEventEmitter()
        },

        // Extension context
        ExtensionContext: class ExtensionContext {
            constructor(extId, extPath) {
                this.subscriptions = subscriptions;
                this.extensionPath = extPath;
                this.globalStoragePath = path.join(extPath, '.storage');
                this.workspaceState = createMemento();
                this.globalState = createMemento();
                this.extensionUri = vscode.Uri.file(extPath);
                this.storagePath = path.join(extPath, '.storage');
                this.logPath = path.join(extPath, '.logs');
                this.extensionMode = 1; // Production
            }
            asAbsolutePath(relativePath) {
                return path.join(this.extensionPath, relativePath);
            }
        }
    };

    return vscode;
}

// ─── Helpers ────────────────────────────────────────────────────

function createEventEmitter() {
    const listeners = [];
    const emitter = function(listener) {
        listeners.push(listener);
        return { dispose() { const i = listeners.indexOf(listener); if (i >= 0) listeners.splice(i, 1); } };
    };
    emitter.fire = function(data) {
        listeners.forEach(l => { try { l(data); } catch(e) { /* ignore */ } });
    };
    return emitter;
}

function createMemento() {
    const store = new Map();
    return {
        get(key, defaultValue) { return store.has(key) ? store.get(key) : defaultValue; },
        update(key, value) { store.set(key, value); return Promise.resolve(); },
        keys() { return [...store.keys()]; }
    };
}

// ─── Extension Lifecycle ────────────────────────────────────────

async function activateExtension(extensionPath, extensionId) {
    try {
        const pkgPath = path.join(extensionPath, 'package.json');
        if (!fs.existsSync(pkgPath)) {
            // Try extension/ subfolder (VSIX extracted format)
            const altPath = path.join(extensionPath, 'extension', 'package.json');
            if (fs.existsSync(altPath)) {
                extensionPath = path.join(extensionPath, 'extension');
            } else {
                throw new Error(`package.json not found in ${extensionPath}`);
            }
        }

        const pkg = JSON.parse(fs.readFileSync(path.join(extensionPath, 'package.json'), 'utf8'));
        const mainFile = pkg.main || './extension.js';
        const mainPath = path.resolve(extensionPath, mainFile);

        if (!fs.existsSync(mainPath)) {
            // Extension has no JS entry point (theme/snippet only)
            loadedExtensions.set(extensionId, { path: extensionPath, isActive: true, exports: {}, pkg });
            return { activated: true, hasMain: false };
        }

        // Create vscode API shim for this extension
        const vscodeApi = createVscodeApi(extensionId, extensionPath);

        // Intercept require('vscode')
        const Module = require('module');
        const originalResolve = Module._resolveFilename;
        Module._resolveFilename = function(request, parent) {
            if (request === 'vscode') return 'vscode';
            return originalResolve.apply(this, arguments);
        };
        const originalLoad = Module._cache;
        require.cache['vscode'] = { id: 'vscode', filename: 'vscode', loaded: true, exports: vscodeApi };

        // Load and activate extension
        const extensionModule = require(mainPath);
        const context = new vscodeApi.ExtensionContext(extensionId, extensionPath);

        let exports = {};
        if (typeof extensionModule.activate === 'function') {
            exports = await Promise.resolve(extensionModule.activate(context)) || {};
        }

        loadedExtensions.set(extensionId, {
            path: extensionPath,
            isActive: true,
            exports,
            module: extensionModule,
            context,
            pkg
        });

        sendNotification('extensionActivated', { extensionId });
        return { activated: true, hasMain: true };

    } catch (err) {
        sendNotification('log', { level: 'error', message: `Failed to activate ${extensionId}: ${err.message}` });
        return { activated: false, error: err.message };
    }
}

async function deactivateExtension(extensionId) {
    const ext = loadedExtensions.get(extensionId);
    if (!ext) return { deactivated: false, error: 'Extension not loaded' };

    try {
        if (ext.module && typeof ext.module.deactivate === 'function') {
            await Promise.resolve(ext.module.deactivate());
        }
        if (ext.context && ext.context.subscriptions) {
            ext.context.subscriptions.forEach(s => { try { s.dispose(); } catch(e) {} });
        }
        loadedExtensions.delete(extensionId);
        return { deactivated: true };
    } catch (err) {
        return { deactivated: false, error: err.message };
    }
}

async function deactivateAll() {
    for (const [id] of loadedExtensions) {
        await deactivateExtension(id);
    }
}

async function executeCommand(command, args) {
    const cmd = registeredCommands.get(command);
    if (!cmd) {
        // Forward to IDE
        return sendRequest('executeCommand', { command, args });
    }
    return Promise.resolve(cmd.callback(...(args || [])));
}

function broadcastToExtensions(method, params) {
    // Extensions can register for document events through the vscode API shim
    // The event emitters will fire when we receive these notifications
}

// ─── Startup ────────────────────────────────────────────────────

sendNotification('log', { level: 'info', message: 'Extension host started' });
sendNotification('ready', {});
