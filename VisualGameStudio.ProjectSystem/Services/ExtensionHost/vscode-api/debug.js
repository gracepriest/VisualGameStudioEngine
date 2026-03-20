'use strict';

const { Disposable, EventEmitter } = require('./event');

/**
 * Creates the vscode.debug namespace API.
 *
 * @param {object} rpc   - JSON-RPC module with sendRequest / sendNotification.
 * @param {string} extensionId - The owning extension identifier.
 * @returns {object} debug namespace
 */
function createDebugApi(rpc, extensionId) {
    /** @type {Map<string, object>} debugType -> factory */
    const adapterFactories = new Map();
    /** @type {Map<string, object>} debugType -> provider */
    const configProviders = new Map();

    let _activeSession = null;
    let _breakpoints = [];

    // Event emitters
    const _onDidStartDebugSession = new EventEmitter();
    const _onDidTerminateDebugSession = new EventEmitter();
    const _onDidChangeActiveDebugSession = new EventEmitter();
    const _onDidReceiveDebugSessionCustomEvent = new EventEmitter();
    const _onDidChangeBreakpoints = new EventEmitter();

    return {
        // -----------------------------------------------------------------
        // Registration
        // -----------------------------------------------------------------

        /**
         * Register a debug adapter descriptor factory for a given debug type.
         * @param {string} debugType
         * @param {object} factory - Must implement createDebugAdapterDescriptor(session, executable).
         * @returns {Disposable}
         */
        registerDebugAdapterDescriptorFactory(debugType, factory) {
            adapterFactories.set(debugType, factory);
            rpc.sendNotification('debug/registerAdapter', { debugType, extensionId });
            return new Disposable(() => {
                adapterFactories.delete(debugType);
                rpc.sendNotification('debug/unregisterAdapter', { debugType, extensionId });
            });
        },

        /**
         * Register a debug configuration provider.
         * @param {string} debugType
         * @param {object} provider - May implement resolveDebugConfiguration / provideDebugConfigurations.
         * @param {number} [triggerKind] - When the provider should be invoked (initial=1, dynamic=2).
         * @returns {Disposable}
         */
        registerDebugConfigurationProvider(debugType, provider, triggerKind) {
            const key = triggerKind ? `${debugType}:${triggerKind}` : debugType;
            configProviders.set(key, provider);
            rpc.sendNotification('debug/registerConfigProvider', { debugType, extensionId, triggerKind });
            return new Disposable(() => {
                configProviders.delete(key);
            });
        },

        // -----------------------------------------------------------------
        // Session control
        // -----------------------------------------------------------------

        /**
         * Start a debug session.
         * @param {object|undefined} folder - Workspace folder or undefined.
         * @param {string|object} nameOrConfiguration - Launch config name or inline config.
         * @param {object} [parentSessionOrOptions] - Parent session or options bag.
         * @returns {Promise<boolean>}
         */
        startDebugging(folder, nameOrConfiguration, parentSessionOrOptions) {
            const folderUri = folder && folder.uri ? (folder.uri.toString ? folder.uri.toString() : String(folder.uri)) : undefined;
            return rpc.sendRequest('debug/startDebugging', {
                folder: folderUri,
                nameOrConfiguration,
                parentSessionOrOptions,
            });
        },

        /**
         * Stop a debug session (or the active session when omitted).
         * @param {object} [session]
         * @returns {Promise<void>}
         */
        stopDebugging(session) {
            return rpc.sendRequest('debug/stopDebugging', {
                sessionId: session ? session.id : undefined,
            });
        },

        // -----------------------------------------------------------------
        // Breakpoints
        // -----------------------------------------------------------------

        /**
         * Add breakpoints.
         * @param {object[]} breakpoints
         */
        addBreakpoints(breakpoints) {
            const serialised = breakpoints.map(bp => ({
                id: bp.id,
                enabled: bp.enabled,
                condition: bp.condition,
                hitCondition: bp.hitCondition,
                logMessage: bp.logMessage,
                location: bp.location,
            }));
            _breakpoints.push(...breakpoints);
            rpc.sendNotification('debug/addBreakpoints', { breakpoints: serialised });
            _onDidChangeBreakpoints.fire({ added: breakpoints, removed: [], changed: [] });
        },

        /**
         * Remove breakpoints.
         * @param {object[]} breakpoints
         */
        removeBreakpoints(breakpoints) {
            const ids = new Set(breakpoints.map(bp => bp.id));
            _breakpoints = _breakpoints.filter(bp => !ids.has(bp.id));
            const serialised = breakpoints.map(bp => ({ id: bp.id }));
            rpc.sendNotification('debug/removeBreakpoints', { breakpoints: serialised });
            _onDidChangeBreakpoints.fire({ added: [], removed: breakpoints, changed: [] });
        },

        // -----------------------------------------------------------------
        // Getters
        // -----------------------------------------------------------------

        /** @returns {object|null} The currently active debug session. */
        get activeDebugSession() {
            return _activeSession;
        },

        /** @returns {object[]} All current breakpoints. */
        get breakpoints() {
            return [..._breakpoints];
        },

        // -----------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------

        onDidStartDebugSession: _onDidStartDebugSession.event,
        onDidTerminateDebugSession: _onDidTerminateDebugSession.event,
        onDidChangeActiveDebugSession: _onDidChangeActiveDebugSession.event,
        onDidReceiveDebugSessionCustomEvent: _onDidReceiveDebugSessionCustomEvent.event,
        onDidChangeBreakpoints: _onDidChangeBreakpoints.event,

        // -----------------------------------------------------------------
        // Internal — called by the extension host when the IDE sends
        // debug-related notifications.
        // -----------------------------------------------------------------

        /**
         * @param {object} session
         */
        _onSessionStarted(session) {
            _activeSession = session;
            _onDidStartDebugSession.fire(session);
            _onDidChangeActiveDebugSession.fire(session);
        },

        /**
         * @param {object} session
         */
        _onSessionTerminated(session) {
            if (_activeSession && _activeSession.id === session.id) {
                _activeSession = null;
                _onDidChangeActiveDebugSession.fire(null);
            }
            _onDidTerminateDebugSession.fire(session);
        },

        /**
         * @param {object} event - { session, event, body }
         */
        _onCustomEvent(event) {
            _onDidReceiveDebugSessionCustomEvent.fire(event);
        },

        /**
         * @param {object} event - { added, removed, changed }
         */
        _onBreakpointsChanged(event) {
            if (event.added) _breakpoints.push(...event.added);
            if (event.removed) {
                const ids = new Set(event.removed.map(bp => bp.id));
                _breakpoints = _breakpoints.filter(bp => !ids.has(bp.id));
            }
            _onDidChangeBreakpoints.fire(event);
        },

        /**
         * Retrieve a registered adapter factory.
         * @param {string} debugType
         * @returns {object|undefined}
         */
        _getAdapterFactory(debugType) {
            return adapterFactories.get(debugType);
        },

        /**
         * Retrieve a registered config provider.
         * @param {string} debugType
         * @param {number} [triggerKind]
         * @returns {object|undefined}
         */
        _getConfigProvider(debugType, triggerKind) {
            const key = triggerKind ? `${debugType}:${triggerKind}` : debugType;
            return configProviders.get(key) || configProviders.get(debugType);
        },
    };
}

module.exports = { createDebugApi };
