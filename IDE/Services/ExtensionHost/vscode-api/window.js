'use strict';

const { Disposable, EventEmitter } = require('./event');

let _nextChannelId = 1;
let _nextStatusBarId = 1;
let _nextTerminalId = 1;
let _nextWebviewId = 1;

/**
 * Creates the vscode.window namespace API.
 *
 * @param {object} rpc         - JSON-RPC module with sendRequest / sendNotification.
 * @param {string} extensionId - The owning extension identifier.
 * @returns {object} window namespace
 */
function createWindowApi(rpc, extensionId) {

    // -----------------------------------------------------------------
    // Text editor state
    // -----------------------------------------------------------------

    let _activeTextEditor = undefined;
    let _visibleTextEditors = [];

    const _onDidChangeActiveTextEditor = new EventEmitter();
    const _onDidChangeVisibleTextEditors = new EventEmitter();
    const _onDidChangeTextEditorSelection = new EventEmitter();
    const _onDidChangeTextEditorVisibleRanges = new EventEmitter();

    function _makeTextEditor(data) {
        if (!data) return undefined;
        return {
            document: data.document || {},
            selection: data.selection || { start: { line: 0, character: 0 }, end: { line: 0, character: 0 } },
            selections: data.selections || [],
            visibleRanges: data.visibleRanges || [],
            options: data.options || {},
            viewColumn: data.viewColumn,
            _edit: data._edit || {},
        };
    }

    // -----------------------------------------------------------------
    // Terminal events
    // -----------------------------------------------------------------

    const _onDidOpenTerminal = new EventEmitter();
    const _onDidCloseTerminal = new EventEmitter();

    // -----------------------------------------------------------------
    // TreeView storage (for main.js callbacks)
    // -----------------------------------------------------------------

    /** @type {Map<string, object>} viewId -> treeView instance */
    const _treeViews = new Map();

    // -----------------------------------------------------------------
    // Message dialogs
    // -----------------------------------------------------------------

    function _showMessage(type, message, ...optionsOrItems) {
        let options = {};
        let items = optionsOrItems;
        if (optionsOrItems.length > 0 && optionsOrItems[0] !== null &&
            typeof optionsOrItems[0] === 'object' && !Array.isArray(optionsOrItems[0]) &&
            typeof optionsOrItems[0].title === 'undefined' && !('isCloseAffordance' in optionsOrItems[0])) {
            // First arg looks like MessageOptions (has modal property, no title)
            if ('modal' in optionsOrItems[0] || 'detail' in optionsOrItems[0]) {
                options = optionsOrItems[0];
                items = optionsOrItems.slice(1);
            }
        }
        // Flatten if items is nested array
        if (items.length === 1 && Array.isArray(items[0])) {
            items = items[0];
        }
        const normalizedItems = items.map(item =>
            typeof item === 'string' ? { title: item } : item
        );
        return rpc.sendRequest('window/showMessage', {
            type,
            message,
            options,
            items: normalizedItems,
            extensionId,
        });
    }

    // -----------------------------------------------------------------
    // Output channels
    // -----------------------------------------------------------------

    function _createOutputChannel(name, options) {
        const channelId = `${extensionId}-output-${_nextChannelId++}`;
        const isLog = !!(options && options.log);

        rpc.sendNotification('outputChannel/create', { channelId, name, extensionId, log: isLog });

        const channel = {
            get name() { return name; },

            append(text) {
                rpc.sendNotification('outputChannel/append', { channelId, text });
            },
            appendLine(text) {
                rpc.sendNotification('outputChannel/append', { channelId, text: text + '\n' });
            },
            clear() {
                rpc.sendNotification('outputChannel/clear', { channelId });
            },
            show(preserveFocus) {
                rpc.sendNotification('outputChannel/show', { channelId, preserveFocus: !!preserveFocus });
            },
            hide() {
                rpc.sendNotification('outputChannel/hide', { channelId });
            },
            replace(text) {
                rpc.sendNotification('outputChannel/clear', { channelId });
                rpc.sendNotification('outputChannel/append', { channelId, text });
            },
            dispose() {
                rpc.sendNotification('outputChannel/dispose', { channelId });
            },
        };

        if (isLog) {
            const _logLevel = (level, args) => {
                const text = args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ');
                channel.appendLine(`[${level}] ${text}`);
            };
            channel.trace = (...args) => _logLevel('trace', args);
            channel.debug = (...args) => _logLevel('debug', args);
            channel.info = (...args) => _logLevel('info', args);
            channel.warn = (...args) => _logLevel('warn', args);
            channel.error = (...args) => _logLevel('error', args);
        }

        return channel;
    }

    // -----------------------------------------------------------------
    // Status bar
    // -----------------------------------------------------------------

    function _createStatusBarItem(alignmentOrId, priorityOrAlignment, priority) {
        let id, alignment, prio;

        if (typeof alignmentOrId === 'string') {
            // New signature: (id, alignment, priority)
            id = alignmentOrId;
            alignment = priorityOrAlignment || 1; // Left=1, Right=2
            prio = priority || 0;
        } else {
            // Old signature: (alignment, priority)
            id = `${extensionId}-statusbar-${_nextStatusBarId++}`;
            alignment = alignmentOrId || 1;
            prio = priorityOrAlignment || 0;
        }

        let _text = '';
        let _tooltip = '';
        let _color = undefined;
        let _backgroundColor = undefined;
        let _command = undefined;
        let _name = '';
        let _accessibilityInformation = undefined;
        let _visible = false;

        function _update() {
            if (!_visible) return;
            rpc.sendNotification('statusBar/update', {
                id, text: _text, tooltip: _tooltip, color: _color,
                backgroundColor: _backgroundColor, command: _command,
                alignment, priority: prio, name: _name,
                accessibilityInformation: _accessibilityInformation,
                extensionId,
            });
        }

        const item = {
            get id() { return id; },
            get alignment() { return alignment; },
            get priority() { return prio; },

            get text() { return _text; },
            set text(v) { _text = v; _update(); },

            get tooltip() { return _tooltip; },
            set tooltip(v) { _tooltip = v; _update(); },

            get color() { return _color; },
            set color(v) { _color = v; _update(); },

            get backgroundColor() { return _backgroundColor; },
            set backgroundColor(v) { _backgroundColor = v; _update(); },

            get command() { return _command; },
            set command(v) { _command = v; _update(); },

            get name() { return _name; },
            set name(v) { _name = v; _update(); },

            get accessibilityInformation() { return _accessibilityInformation; },
            set accessibilityInformation(v) { _accessibilityInformation = v; _update(); },

            show() {
                _visible = true;
                _update();
            },
            hide() {
                _visible = false;
                rpc.sendNotification('statusBar/update', {
                    id, visible: false, extensionId,
                });
            },
            dispose() {
                rpc.sendNotification('statusBar/dispose', { id, extensionId });
            },
        };

        return item;
    }

    function _setStatusBarMessage(text, timeoutOrThenable) {
        const item = _createStatusBarItem(1, -1000);
        item.text = text;
        item.show();

        const dispose = () => item.dispose();

        if (typeof timeoutOrThenable === 'number') {
            setTimeout(dispose, timeoutOrThenable);
        } else if (timeoutOrThenable && typeof timeoutOrThenable.then === 'function') {
            timeoutOrThenable.then(dispose, dispose);
        }

        return new Disposable(dispose);
    }

    // -----------------------------------------------------------------
    // TreeView
    // -----------------------------------------------------------------

    function _createTreeView(viewId, options) {
        const provider = options.treeDataProvider;

        const _onDidExpandElement = new EventEmitter();
        const _onDidCollapseElement = new EventEmitter();
        const _onDidChangeSelection = new EventEmitter();
        const _onDidChangeVisibility = new EventEmitter();

        let _visible = true;
        let _title = viewId;
        let _description = '';
        let _message = '';

        rpc.sendNotification('treeView/create', { viewId, extensionId });

        const treeView = {
            get visible() { return _visible; },
            get title() { return _title; },
            set title(v) { _title = v; },
            get description() { return _description; },
            set description(v) { _description = v; },
            get message() { return _message; },
            set message(v) { _message = v; },

            onDidExpandElement: _onDidExpandElement.event,
            onDidCollapseElement: _onDidCollapseElement.event,
            onDidChangeSelection: _onDidChangeSelection.event,
            onDidChangeVisibility: _onDidChangeVisibility.event,

            reveal(element, options) {
                return rpc.sendRequest('treeView/reveal', { viewId, element, options });
            },
            dispose() {
                rpc.sendNotification('treeView/dispose', { viewId });
                _onDidExpandElement.dispose();
                _onDidCollapseElement.dispose();
                _onDidChangeSelection.dispose();
                _onDidChangeVisibility.dispose();
                _treeViews.delete(viewId);
            },

            // Internal methods for main.js to call when IDE requests tree data
            _getChildren(element) {
                if (provider.getChildren) {
                    return Promise.resolve(provider.getChildren(element));
                }
                return Promise.resolve([]);
            },
            _getTreeItem(element) {
                if (provider.getTreeItem) {
                    return Promise.resolve(provider.getTreeItem(element));
                }
                return Promise.resolve(element);
            },

            // Internal: fire events from main.js
            _fireExpand(element) { _onDidExpandElement.fire({ element }); },
            _fireCollapse(element) { _onDidCollapseElement.fire({ element }); },
            _fireSelectionChange(selection) { _onDidChangeSelection.fire({ selection }); },
            _fireVisibilityChange(visible) {
                _visible = visible;
                _onDidChangeVisibility.fire({ visible });
            },
        };

        _treeViews.set(viewId, treeView);
        return treeView;
    }

    // -----------------------------------------------------------------
    // WebView
    // -----------------------------------------------------------------

    function _createWebviewPanel(viewType, title, showOptions, options) {
        const webviewId = `${extensionId}-webview-${_nextWebviewId++}`;
        const column = typeof showOptions === 'number' ? showOptions : (showOptions && showOptions.viewColumn) || 1;
        const preserveFocus = (showOptions && showOptions.preserveFocus) || false;

        const _onDidDispose = new EventEmitter();
        const _onDidChangeViewState = new EventEmitter();
        const _onDidReceiveMessage = new EventEmitter();

        let _html = '';
        let _active = true;
        let _visible = true;
        let _viewColumn = column;

        rpc.sendNotification('webview/create', {
            webviewId, viewType, title, column, preserveFocus,
            options: options || {}, extensionId,
        });

        const webview = {
            get html() { return _html; },
            set html(value) {
                _html = value;
                rpc.sendNotification('webview/setHtml', { webviewId, html: value });
            },
            postMessage(message) {
                return rpc.sendRequest('webview/postMessage', { webviewId, message });
            },
            onDidReceiveMessage: _onDidReceiveMessage.event,
            get options() { return options || {}; },
            get cspSource() { return ''; },
        };

        const panel = {
            get viewType() { return viewType; },
            get title() { return title; },
            get webview() { return webview; },
            get active() { return _active; },
            get visible() { return _visible; },
            get viewColumn() { return _viewColumn; },

            onDidDispose: _onDidDispose.event,
            onDidChangeViewState: _onDidChangeViewState.event,

            reveal(viewColumn, preserveFocus) {
                rpc.sendNotification('webview/reveal', { webviewId, viewColumn, preserveFocus });
            },
            dispose() {
                rpc.sendNotification('webview/dispose', { webviewId });
                _onDidDispose.fire();
                _onDidDispose.dispose();
                _onDidChangeViewState.dispose();
                _onDidReceiveMessage.dispose();
            },

            // Internal: receive message from IDE
            _onMessage(msg) { _onDidReceiveMessage.fire(msg); },
            _onViewStateChange(state) {
                _active = state.active;
                _visible = state.visible;
                _viewColumn = state.viewColumn || _viewColumn;
                _onDidChangeViewState.fire({ webviewPanel: panel });
            },
            // Internal: _onDidReceiveMessage emitter for main.js webview/postMessage handler
            _onDidReceiveMessage,
        };

        _webviewPanels.set(webviewId, panel);
        return panel;
    }

    /** @type {Map<string, object>} panelId -> webview panel */
    const _webviewPanels = new Map();

    /** @type {Map<string, object>} viewId -> provider registration */
    const _webviewViewProviders = new Map();

    // -----------------------------------------------------------------
    // Progress
    // -----------------------------------------------------------------

    function _withProgress(options, task) {
        const progressId = `${extensionId}-progress-${Date.now()}`;
        rpc.sendNotification('window/withProgress', {
            progressId,
            title: options.title || '',
            location: options.location,
            cancellable: !!options.cancellable,
            extensionId,
        });

        const progress = {
            report(value) {
                rpc.sendNotification('window/progressReport', {
                    progressId,
                    message: value.message,
                    increment: value.increment,
                });
            },
        };

        const cts = { isCancellationRequested: false, onCancellationRequested: new EventEmitter().event };

        return Promise.resolve(task(progress, cts)).then(
            result => {
                rpc.sendNotification('window/progressEnd', { progressId });
                return result;
            },
            err => {
                rpc.sendNotification('window/progressEnd', { progressId });
                throw err;
            }
        );
    }

    // -----------------------------------------------------------------
    // Terminal
    // -----------------------------------------------------------------

    function _createTerminal(nameOrOptions) {
        const terminalId = `${extensionId}-terminal-${_nextTerminalId++}`;
        let name, shellPath, shellArgs, cwd, env;

        if (typeof nameOrOptions === 'string') {
            name = nameOrOptions;
        } else if (nameOrOptions && typeof nameOrOptions === 'object') {
            name = nameOrOptions.name;
            shellPath = nameOrOptions.shellPath;
            shellArgs = nameOrOptions.shellArgs;
            cwd = nameOrOptions.cwd;
            env = nameOrOptions.env;
        } else {
            name = 'Terminal';
        }

        rpc.sendNotification('terminal/create', {
            terminalId, name, shellPath, shellArgs, cwd, env, extensionId,
        });

        const terminal = {
            get name() { return name; },
            sendText(text, addNewLine) {
                rpc.sendNotification('terminal/sendText', {
                    terminalId, text, addNewLine: addNewLine !== false,
                });
            },
            show(preserveFocus) {
                rpc.sendNotification('terminal/show', { terminalId, preserveFocus: !!preserveFocus });
            },
            dispose() {
                rpc.sendNotification('terminal/dispose', { terminalId });
            },
        };

        _onDidOpenTerminal.fire(terminal);
        return terminal;
    }

    // -----------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------

    return {
        // Message dialogs
        showInformationMessage(message, ...optionsOrItems) {
            return _showMessage('info', message, ...optionsOrItems);
        },
        showWarningMessage(message, ...optionsOrItems) {
            return _showMessage('warning', message, ...optionsOrItems);
        },
        showErrorMessage(message, ...optionsOrItems) {
            return _showMessage('error', message, ...optionsOrItems);
        },

        // Pickers
        showQuickPick(items, options, token) {
            return rpc.sendRequest('window/showQuickPick', { items, options, token, extensionId });
        },
        showInputBox(options, token) {
            return rpc.sendRequest('window/showInputBox', { options, token, extensionId });
        },
        showOpenDialog(options) {
            return rpc.sendRequest('window/showOpenDialog', { options, extensionId });
        },
        showSaveDialog(options) {
            return rpc.sendRequest('window/showSaveDialog', { options, extensionId });
        },

        // Output channels
        createOutputChannel: _createOutputChannel,

        // Status bar
        createStatusBarItem: _createStatusBarItem,
        setStatusBarMessage: _setStatusBarMessage,

        // Text editors
        get activeTextEditor() { return _activeTextEditor; },
        get visibleTextEditors() { return _visibleTextEditors.slice(); },
        onDidChangeActiveTextEditor: _onDidChangeActiveTextEditor.event,
        onDidChangeVisibleTextEditors: _onDidChangeVisibleTextEditors.event,
        onDidChangeTextEditorSelection: _onDidChangeTextEditorSelection.event,
        onDidChangeTextEditorVisibleRanges: _onDidChangeTextEditorVisibleRanges.event,

        showTextDocument(documentOrUri, columnOrOptions, preserveFocus) {
            const uri = documentOrUri.uri ? documentOrUri.uri.toString() : documentOrUri.toString();
            let options = {};
            if (typeof columnOrOptions === 'number') {
                options.viewColumn = columnOrOptions;
                options.preserveFocus = !!preserveFocus;
            } else if (columnOrOptions && typeof columnOrOptions === 'object') {
                options = columnOrOptions;
            }
            return rpc.sendRequest('window/showTextDocument', { uri, options, extensionId });
        },

        // TreeView
        createTreeView: _createTreeView,
        registerTreeDataProvider(viewId, provider) {
            return _createTreeView(viewId, { treeDataProvider: provider });
        },

        // WebView
        createWebviewPanel: _createWebviewPanel,
        registerWebviewViewProvider(viewId, provider, options) {
            _webviewViewProviders.set(viewId, { provider, options });
            rpc.sendNotification('webviewView/register', { viewId, extensionId });
            return new Disposable(() => {
                _webviewViewProviders.delete(viewId);
                rpc.sendNotification('webviewView/unregister', { viewId, extensionId });
            });
        },

        // Progress
        withProgress: _withProgress,

        // Terminal
        createTerminal: _createTerminal,
        onDidOpenTerminal: _onDidOpenTerminal.event,
        onDidCloseTerminal: _onDidCloseTerminal.event,

        // -----------------------------------------------------------------
        // Internal state methods (called by main.js)
        // -----------------------------------------------------------------

        _treeViews,
        _webviewPanels,

        _setActiveEditor(editorData) {
            _activeTextEditor = _makeTextEditor(editorData);
            _onDidChangeActiveTextEditor.fire(_activeTextEditor);
        },

        _setVisibleEditors(editorsData) {
            _visibleTextEditors = (editorsData || []).map(_makeTextEditor);
            _onDidChangeVisibleTextEditors.fire(_visibleTextEditors);
        },

        _getActiveTextEditor() {
            return _activeTextEditor;
        },

        _getTreeView(viewId) {
            return _treeViews.get(viewId);
        },

        _getWebviewViewProvider(viewId) {
            const reg = _webviewViewProviders.get(viewId);
            return reg ? reg.provider : undefined;
        },

        // Tab groups API — minimal stub for extensions that check open tabs
        tabGroups: {
            all: [],
            activeTabGroup: { tabs: [], isActive: true, viewColumn: 1 },
            onDidChangeTabGroups: _onDidChangeActiveTextEditor.event,
            onDidChangeTabs: _onDidChangeActiveTextEditor.event,
            close: () => Promise.resolve(true),
        },
    };
}

module.exports = { createWindowApi };
