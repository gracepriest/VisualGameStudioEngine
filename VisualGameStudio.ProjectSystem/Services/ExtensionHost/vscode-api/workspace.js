'use strict';

const path = require('path');
const { Disposable, EventEmitter } = require('./event');

/**
 * Creates the vscode.workspace namespace API.
 *
 * @param {object} rpc             - JSON-RPC module with sendRequest / sendNotification.
 * @param {object} documentManager - Manages open TextDocument instances.
 * @returns {object} workspace namespace
 */
function createWorkspaceApi(rpc, documentManager) {
    /** @type {Array<{uri: object, name: string, index: number}>} */
    let _workspaceFolders = [];

    /** @type {object} Flat settings object pushed from IDE */
    let _settings = {};

    /** @type {Map<string, object>} scheme -> provider */
    const _contentProviders = new Map();

    /** @type {Map<number, object>} watcherId -> watcher */
    const _fileWatchers = new Map();
    let _nextWatcherId = 1;

    /** @type {number} */
    let _nextDiagCollectionId = 1;

    // Event emitters
    const _onDidChangeWorkspaceFolders = new EventEmitter();
    const _onDidChangeConfiguration = new EventEmitter();

    // -----------------------------------------------------------------------
    // Configuration proxy
    // -----------------------------------------------------------------------

    /**
     * Navigate a nested object by dot-separated key path.
     * @param {object} obj
     * @param {string} keyPath
     * @returns {{ found: boolean, value: any }}
     */
    function _resolve(obj, keyPath) {
        if (!keyPath) return { found: true, value: obj };
        const parts = keyPath.split('.');
        let current = obj;
        for (const part of parts) {
            if (current == null || typeof current !== 'object') {
                return { found: false, value: undefined };
            }
            if (!(part in current)) {
                return { found: false, value: undefined };
            }
            current = current[part];
        }
        return { found: true, value: current };
    }

    /**
     * Build a Configuration proxy for a given section.
     * @param {string|undefined} section
     * @param {object|undefined} _scope - unused for now, kept for API compat
     * @returns {object}
     */
    function _makeConfiguration(section, _scope) {
        // The "root" for this configuration is _settings[section] (or _settings if no section).
        function _getRoot() {
            if (!section) return _settings;
            const r = _resolve(_settings, section);
            return r.found ? r.value : {};
        }

        const config = {
            get(key, defaultValue) {
                const root = _getRoot();
                if (key === undefined) return root;
                const r = _resolve(root, key);
                return r.found && r.value !== undefined ? r.value : defaultValue;
            },

            has(key) {
                const root = _getRoot();
                return _resolve(root, key).found;
            },

            update(key, value, configTarget, overrideInLanguage) {
                const fullKey = section ? `${section}.${key}` : key;
                return rpc.sendRequest('configuration/update', {
                    key: fullKey,
                    value,
                    configTarget,
                    overrideInLanguage,
                });
            },

            inspect(key) {
                const root = _getRoot();
                const r = _resolve(root, key);
                const fullKey = section ? `${section}.${key}` : key;
                return {
                    key: fullKey,
                    defaultValue: undefined,
                    globalValue: r.found ? r.value : undefined,
                    workspaceValue: undefined,
                    workspaceFolderValue: undefined,
                };
            },
        };

        return config;
    }

    // -----------------------------------------------------------------------
    // File system proxy
    // -----------------------------------------------------------------------

    function _uriToString(uri) {
        if (!uri) return '';
        if (typeof uri === 'string') return uri;
        if (typeof uri.toString === 'function') return uri.toString();
        return String(uri);
    }

    const fs = {
        async readFile(uri) {
            const result = await rpc.sendRequest('workspace/fs/readFile', {
                uri: _uriToString(uri),
            });
            // result is base64-encoded string
            const buffer = Buffer.from(result, 'base64');
            return new Uint8Array(buffer.buffer, buffer.byteOffset, buffer.byteLength);
        },

        async writeFile(uri, content) {
            const buffer = Buffer.from(content);
            const base64 = buffer.toString('base64');
            return rpc.sendRequest('workspace/fs/writeFile', {
                uri: _uriToString(uri),
                content: base64,
            });
        },

        async stat(uri) {
            return rpc.sendRequest('workspace/fs/stat', {
                uri: _uriToString(uri),
            });
        },

        async readDirectory(uri) {
            return rpc.sendRequest('workspace/fs/readDirectory', {
                uri: _uriToString(uri),
            });
        },

        async createDirectory(uri) {
            return rpc.sendRequest('workspace/fs/createDirectory', {
                uri: _uriToString(uri),
            });
        },

        async delete(uri, options) {
            return rpc.sendRequest('workspace/fs/delete', {
                uri: _uriToString(uri),
                options,
            });
        },

        async rename(source, target, options) {
            return rpc.sendRequest('workspace/fs/rename', {
                source: _uriToString(source),
                target: _uriToString(target),
                options,
            });
        },

        async copy(source, target, options) {
            return rpc.sendRequest('workspace/fs/copy', {
                source: _uriToString(source),
                target: _uriToString(target),
                options,
            });
        },
    };

    // -----------------------------------------------------------------------
    // Workspace API
    // -----------------------------------------------------------------------

    return {
        // -------------------------------------------------------------------
        // Document access
        // -------------------------------------------------------------------

        /** @returns {object[]} All known text documents. */
        get textDocuments() {
            return documentManager.allDocuments;
        },

        /** Notebook documents — empty array, no notebook support yet. */
        get notebookDocuments() {
            return [];
        },

        /** Notebook events — stubs */
        onDidOpenNotebookDocument: _onDidChangeConfiguration.event,
        onDidCloseNotebookDocument: _onDidChangeConfiguration.event,
        onDidChangeNotebookDocument: _onDidChangeConfiguration.event,
        onDidSaveNotebookDocument: _onDidChangeConfiguration.event,

        /**
         * Open a text document.
         * @param {string|object} uriOrOptions - URI string, Uri object, or {content, language}.
         * @returns {Promise<object>}
         */
        async openTextDocument(uriOrOptions) {
            if (uriOrOptions && typeof uriOrOptions === 'object' && !uriOrOptions.scheme) {
                // {content, language} — create a virtual document
                return rpc.sendRequest('workspace/openTextDocument', {
                    content: uriOrOptions.content || '',
                    language: uriOrOptions.language || 'plaintext',
                    isVirtual: true,
                });
            }
            const uri = _uriToString(uriOrOptions);
            return rpc.sendRequest('workspace/openTextDocument', { uri });
        },

        onDidOpenTextDocument: documentManager.onDidOpen,
        onDidChangeTextDocument: documentManager.onDidChange,
        onDidCloseTextDocument: documentManager.onDidClose,
        onDidSaveTextDocument: documentManager.onDidSave,

        // -------------------------------------------------------------------
        // Workspace folders
        // -------------------------------------------------------------------

        /** @returns {Array|undefined} */
        get workspaceFolders() {
            return _workspaceFolders.length > 0 ? _workspaceFolders : undefined;
        },

        /** @returns {string|undefined} */
        get name() {
            return _workspaceFolders.length > 0 ? _workspaceFolders[0].name : undefined;
        },

        /** @returns {string|undefined} @deprecated */
        get rootPath() {
            if (_workspaceFolders.length === 0) return undefined;
            const f = _workspaceFolders[0];
            const uri = _uriToString(f.uri);
            // Strip file:/// prefix for a local path
            if (uri.startsWith('file:///')) {
                return decodeURIComponent(uri.substring(8));
            }
            return uri;
        },

        /** @returns {undefined} */
        get workspaceFile() {
            return undefined;
        },

        onDidChangeWorkspaceFolders: _onDidChangeWorkspaceFolders.event,

        /**
         * Find the workspace folder containing a given URI.
         * @param {string|object} uri
         * @returns {object|undefined}
         */
        getWorkspaceFolder(uri) {
            const uriStr = _uriToString(uri).replace(/\\/g, '/');
            let best = undefined;
            let bestLen = 0;
            for (const folder of _workspaceFolders) {
                const folderStr = _uriToString(folder.uri).replace(/\\/g, '/');
                if (uriStr.startsWith(folderStr) && folderStr.length > bestLen) {
                    best = folder;
                    bestLen = folderStr.length;
                }
            }
            return best;
        },

        /**
         * Make a path relative to the workspace.
         * @param {string|object} pathOrUri
         * @param {boolean} [includeWorkspaceFolder]
         * @returns {string}
         */
        asRelativePath(pathOrUri, includeWorkspaceFolder) {
            const uriStr = _uriToString(pathOrUri).replace(/\\/g, '/');
            let filePath = uriStr;
            if (filePath.startsWith('file:///')) {
                filePath = decodeURIComponent(filePath.substring(8));
            }

            for (const folder of _workspaceFolders) {
                let folderPath = _uriToString(folder.uri).replace(/\\/g, '/');
                if (folderPath.startsWith('file:///')) {
                    folderPath = decodeURIComponent(folderPath.substring(8));
                }
                if (!folderPath.endsWith('/')) folderPath += '/';

                const normFile = filePath.toLowerCase();
                const normFolder = folderPath.toLowerCase();

                if (normFile.startsWith(normFolder)) {
                    const relative = filePath.substring(folderPath.length);
                    if (includeWorkspaceFolder && _workspaceFolders.length > 1) {
                        return folder.name + '/' + relative;
                    }
                    return relative;
                }
            }

            return filePath;
        },

        // -------------------------------------------------------------------
        // Configuration
        // -------------------------------------------------------------------

        /**
         * Get a configuration object.
         * @param {string} [section]
         * @param {object} [scope]
         * @returns {object} Configuration proxy
         */
        getConfiguration(section, scope) {
            return _makeConfiguration(section, scope);
        },

        onDidChangeConfiguration: _onDidChangeConfiguration.event,

        /**
         * Internal: push settings from IDE.
         * @param {object} settings
         */
        _pushConfiguration(settings) {
            _settings = settings || {};
            _onDidChangeConfiguration.fire({ affectsConfiguration: () => true });
        },

        // -------------------------------------------------------------------
        // File operations
        // -------------------------------------------------------------------

        /**
         * Find files in the workspace.
         * @param {string} include - Glob pattern
         * @param {string} [exclude] - Glob pattern
         * @param {number} [maxResults]
         * @param {object} [token] - CancellationToken
         * @returns {Promise<object[]>}
         */
        findFiles(include, exclude, maxResults, token) {
            return rpc.sendRequest('workspace/findFiles', {
                include: typeof include === 'string' ? include : (include && include.pattern) || '',
                exclude: typeof exclude === 'string' ? exclude : (exclude && exclude.pattern) || undefined,
                maxResults,
            });
        },

        /**
         * Save all dirty documents.
         * @param {boolean} [includeUntitled]
         * @returns {Promise<boolean>}
         */
        saveAll(includeUntitled) {
            return rpc.sendRequest('workspace/saveAll', { includeUntitled });
        },

        /**
         * Apply a workspace edit.
         * @param {object} edit - WorkspaceEdit
         * @returns {Promise<boolean>}
         */
        applyEdit(edit) {
            // Serialize the WorkspaceEdit for transport
            const serialized = {};
            if (edit && typeof edit.entries === 'function') {
                serialized.entries = edit.entries().map(([uri, edits]) => ({
                    uri: _uriToString(uri),
                    edits: edits.map(e => ({
                        range: e.range,
                        newText: e.newText,
                    })),
                }));
            } else if (edit && edit._edits) {
                // Internal representation
                serialized.entries = [];
                for (const [uriStr, edits] of edit._edits) {
                    serialized.entries.push({
                        uri: uriStr,
                        edits: edits.map(e => ({
                            range: e.range,
                            newText: e.newText,
                        })),
                    });
                }
            }
            return rpc.sendRequest('workspace/applyEdit', { edit: serialized });
        },

        // -------------------------------------------------------------------
        // File system
        // -------------------------------------------------------------------

        fs,

        // -------------------------------------------------------------------
        // File watchers
        // -------------------------------------------------------------------

        /**
         * Create a file system watcher.
         * @param {string} glob
         * @param {boolean} [ignoreCreateEvents]
         * @param {boolean} [ignoreChangeEvents]
         * @param {boolean} [ignoreDeleteEvents]
         * @returns {object}
         */
        createFileSystemWatcher(glob, ignoreCreateEvents, ignoreChangeEvents, ignoreDeleteEvents) {
            const watcherId = _nextWatcherId++;
            const onDidCreate = new EventEmitter();
            const onDidChange = new EventEmitter();
            const onDidDelete = new EventEmitter();

            const watcher = {
                onDidCreate: onDidCreate.event,
                onDidChange: onDidChange.event,
                onDidDelete: onDidDelete.event,

                /** @internal */
                _onDidCreate: onDidCreate,
                _onDidChange: onDidChange,
                _onDidDelete: onDidDelete,
                _id: watcherId,

                dispose() {
                    _fileWatchers.delete(watcherId);
                    onDidCreate.dispose();
                    onDidChange.dispose();
                    onDidDelete.dispose();
                    rpc.sendNotification('workspace/unwatchFiles', { watcherId });
                },
            };

            _fileWatchers.set(watcherId, watcher);

            rpc.sendNotification('workspace/watchFiles', {
                watcherId,
                glob: typeof glob === 'string' ? glob : (glob && glob.pattern) || '',
                ignoreCreateEvents: !!ignoreCreateEvents,
                ignoreChangeEvents: !!ignoreChangeEvents,
                ignoreDeleteEvents: !!ignoreDeleteEvents,
            });

            return watcher;
        },

        // -------------------------------------------------------------------
        // Content providers & task providers
        // -------------------------------------------------------------------

        /**
         * Register a text document content provider for a URI scheme.
         * @param {string} scheme
         * @param {object} provider
         * @returns {Disposable}
         */
        registerTextDocumentContentProvider(scheme, provider) {
            _contentProviders.set(scheme, provider);
            rpc.sendNotification('workspace/registerContentProvider', { scheme });
            return new Disposable(() => {
                _contentProviders.delete(scheme);
                rpc.sendNotification('workspace/unregisterContentProvider', { scheme });
            });
        },

        /**
         * Register a task provider.
         * @param {string} type
         * @param {object} provider
         * @returns {Disposable}
         */
        registerTaskProvider(type, provider) {
            // Stub — tasks namespace handles the heavy lifting when available
            rpc.sendNotification('workspace/registerTaskProvider', { type });
            return new Disposable(() => {
                rpc.sendNotification('workspace/unregisterTaskProvider', { type });
            });
        },

        // -------------------------------------------------------------------
        // Internal methods — called by extension host / IDE notifications
        // -------------------------------------------------------------------

        /**
         * Update workspace folders from IDE.
         * @param {Array} folders
         */
        _setWorkspaceFolders(folders) {
            const old = _workspaceFolders;
            _workspaceFolders = (folders || []).map((f, i) => ({
                uri: f.uri,
                name: f.name || path.basename(_uriToString(f.uri)),
                index: i,
            }));
            _onDidChangeWorkspaceFolders.fire({
                added: _workspaceFolders.filter(
                    f => !old.some(o => _uriToString(o.uri) === _uriToString(f.uri))
                ),
                removed: old.filter(
                    o => !_workspaceFolders.some(f => _uriToString(f.uri) === _uriToString(o.uri))
                ),
            });
        },

        /**
         * Notify a file watcher of a file system event.
         * @param {number} watcherId
         * @param {string} type - 'create' | 'change' | 'delete'
         * @param {string} uri
         */
        _onFileWatcherEvent(watcherId, type, uri) {
            const watcher = _fileWatchers.get(watcherId);
            if (!watcher) return;
            if (type === 'create') watcher._onDidCreate.fire({ uri });
            else if (type === 'change') watcher._onDidChange.fire({ uri });
            else if (type === 'delete') watcher._onDidDelete.fire({ uri });
        },

        /**
         * Retrieve a content provider by scheme (used by extension host to serve content).
         * @param {string} scheme
         * @returns {object|undefined}
         */
        _getContentProvider(scheme) {
            return _contentProviders.get(scheme);
        },
    };
}

module.exports = { createWorkspaceApi };
