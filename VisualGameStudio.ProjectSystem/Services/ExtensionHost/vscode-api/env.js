'use strict';

const crypto = require('crypto');
const os = require('os');
const { EventEmitter } = require('./event');

/**
 * Creates the vscode.env namespace API.
 *
 * @param {object} rpc - JSON-RPC module with sendRequest / sendNotification.
 * @returns {object} env namespace
 */
function createEnvApi(rpc) {
    const _machineId = crypto.createHash('sha256').update(os.hostname()).digest('hex');
    const _sessionId = crypto.randomUUID
        ? crypto.randomUUID()
        : crypto.randomBytes(16).toString('hex');

    let _logLevel = 3; // LogLevel.Info

    // These emitters exist for API compat but may never fire.
    const _onDidChangeTelemetryEnabled = new EventEmitter();
    const _onDidChangeLogLevel = new EventEmitter();

    /** @enum {number} */
    const UIKind = Object.freeze({ Desktop: 1, Web: 2 });

    /** @enum {number} */
    const LogLevel = Object.freeze({
        Off: 0,
        Trace: 1,
        Debug: 2,
        Info: 3,
        Warning: 4,
        Error: 5,
    });

    return {
        /** @type {string} Human-readable application name. */
        appName: 'Visual Game Studio',

        /** @type {string} Root path of the application. */
        appRoot: process.cwd(),

        /** @type {string} Host identifier. */
        appHost: 'desktop',

        /** @type {string} Display language (IETF tag). */
        language: (() => {
            try {
                const nlsConfig = process.env.VSCODE_NLS_CONFIG;
                if (nlsConfig) {
                    return JSON.parse(nlsConfig).locale || 'en';
                }
            } catch (_e) { /* ignore */ }
            return 'en';
        })(),

        /** @type {string} SHA-256 of hostname (stable per-machine). */
        machineId: _machineId,

        /** @type {string} Unique session identifier. */
        sessionId: _sessionId,

        /** @type {boolean} Whether this is the first launch after install. */
        isNewAppInstall: false,

        /** @type {boolean} Whether telemetry is enabled. */
        isTelemetryEnabled: false,

        /** Subscribe to telemetry-enabled changes. */
        onDidChangeTelemetryEnabled: _onDidChangeTelemetryEnabled.event,

        /** @type {string} URI scheme for the application. */
        uriScheme: 'vscode',

        /** Clipboard access. */
        clipboard: {
            /**
             * Read text from the system clipboard.
             * @returns {Promise<string>}
             */
            readText() {
                return rpc.sendRequest('env/clipboardRead');
            },

            /**
             * Write text to the system clipboard.
             * @param {string} text
             * @returns {Promise<void>}
             */
            writeText(text) {
                return rpc.sendRequest('env/clipboardWrite', { text });
            },
        },

        /**
         * Open a URI in the default external application.
         * @param {object|string} uri
         * @returns {Promise<boolean>}
         */
        openExternal(uri) {
            const uriStr = uri && typeof uri.toString === 'function' ? uri.toString() : String(uri);
            return rpc.sendRequest('env/openExternal', { uri: uriStr });
        },

        /**
         * Resolve an external URI (identity transform for desktop).
         * @param {object} uri
         * @returns {Promise<object>}
         */
        asExternalUri(uri) {
            return Promise.resolve(uri);
        },

        /** @type {string} Default shell path. */
        shell: process.env.SHELL || process.env.COMSPEC || '/bin/sh',

        /** @type {string|undefined} Name of the remote (undefined for local). */
        remoteName: undefined,

        /** @type {number} Current log level. */
        get logLevel() {
            return _logLevel;
        },
        set logLevel(value) {
            if (_logLevel !== value) {
                _logLevel = value;
                _onDidChangeLogLevel.fire(value);
            }
        },

        /** Subscribe to log-level changes. */
        onDidChangeLogLevel: _onDidChangeLogLevel.event,

        /** @type {number} Desktop = 1. */
        uiKind: UIKind.Desktop,

        /** Enum for UI kind. */
        UIKind,

        /** Enum for log level. */
        LogLevel,

        /**
         * Creates a telemetry logger — stub that discards all telemetry.
         */
        createTelemetryLogger(sender, options) {
            const noop = () => {};
            const _onDidChangeEnableStates = new EventEmitter();
            return {
                logUsage: noop,
                logError: noop,
                logEvent: noop,
                isUsageEnabled: false,
                isErrorsEnabled: false,
                onDidChangeEnableStates: _onDidChangeEnableStates.event,
                dispose: noop,
            };
        },
    };
}

module.exports = { createEnvApi };
