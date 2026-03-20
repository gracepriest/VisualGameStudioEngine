'use strict';

const { Disposable } = require('./event');

/**
 * Creates the vscode.commands namespace API.
 *
 * @param {object} rpc   - JSON-RPC module with sendRequest / sendNotification.
 * @param {string} extensionId - The owning extension identifier.
 * @returns {object} commands namespace
 */
function createCommandsApi(rpc, extensionId) {
    /** @type {Map<string, Function>} */
    const localCommands = new Map();

    return {
        /**
         * Register a command that can be invoked via executeCommand.
         * @param {string} command   - Unique command identifier.
         * @param {Function} callback - Handler function.
         * @param {*} [thisArg]      - The `this` context for the callback.
         * @returns {Disposable}
         */
        registerCommand(command, callback, thisArg) {
            const wrapped = thisArg ? callback.bind(thisArg) : callback;
            localCommands.set(command, wrapped);
            rpc.sendNotification('registerCommand', { command, extensionId });
            return new Disposable(() => {
                localCommands.delete(command);
                rpc.sendNotification('unregisterCommand', { command, extensionId });
            });
        },

        /**
         * Register a text-editor command.  The active editor and an edit-builder
         * are passed as the first two arguments when the command is invoked.
         * @param {string} command
         * @param {Function} callback - (editor, edit, ...args) => any
         * @param {*} [thisArg]
         * @returns {Disposable}
         */
        registerTextEditorCommand(command, callback, thisArg) {
            return this.registerCommand(command, (...args) => {
                // Lazy require to avoid circular dependency with window.js
                let editor;
                try {
                    const { _getActiveTextEditor } = require('./window');
                    editor = _getActiveTextEditor();
                } catch (_e) {
                    // window module may not exist yet
                }
                if (editor) {
                    return callback.call(thisArg, editor, editor._edit, ...args);
                }
            });
        },

        /**
         * Execute a command by identifier.  Local commands are preferred;
         * if not found the request is forwarded to the IDE host.
         * @param {string} command
         * @param  {...any} args
         * @returns {Promise<any>}
         */
        async executeCommand(command, ...args) {
            const local = localCommands.get(command);
            if (local) {
                return Promise.resolve(local(...args));
            }
            return rpc.sendRequest('executeCommand', { command, args });
        },

        /**
         * Retrieve the list of all available commands.
         * @param {boolean} [filterInternal] - When true, commands starting with '_' are excluded.
         * @returns {Promise<string[]>}
         */
        async getCommands(filterInternal) {
            const local = [...localCommands.keys()];
            try {
                const remote = await rpc.sendRequest('getCommands', { filterInternal });
                if (Array.isArray(remote)) {
                    const merged = [...new Set([...local, ...remote])];
                    return filterInternal ? merged.filter(c => !c.startsWith('_')) : merged;
                }
            } catch (_e) {
                // IDE may not support this request yet
            }
            return filterInternal ? local.filter(c => !c.startsWith('_')) : local;
        },

        /**
         * Internal: execute a locally-registered command (called by the IDE
         * host when it needs an extension-side command to run).
         * @param {string} command
         * @param {any[]} [args]
         * @returns {Promise<any>}
         */
        _executeLocal(command, args) {
            const local = localCommands.get(command);
            if (local) {
                return Promise.resolve(local(...(args || [])));
            }
            return Promise.reject(new Error(`Command not found: ${command}`));
        },
    };
}

module.exports = { createCommandsApi };
