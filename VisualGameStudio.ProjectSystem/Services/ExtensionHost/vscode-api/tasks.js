'use strict';

const { Disposable, EventEmitter, CancellationToken } = require('./event');

/**
 * Creates the vscode.tasks namespace API.
 *
 * @param {object} rpc   - JSON-RPC module with sendRequest / sendNotification.
 * @param {string} extensionId - The owning extension identifier.
 * @returns {object} tasks namespace
 */
function createTasksApi(rpc, extensionId) {
    /** @type {Map<string, object>} type -> provider */
    const taskProviders = new Map();

    /** @type {object[]} Currently active task executions. */
    let _taskExecutions = [];

    // Event emitters
    const _onDidStartTask = new EventEmitter();
    const _onDidEndTask = new EventEmitter();
    const _onDidStartTaskProcess = new EventEmitter();
    const _onDidEndTaskProcess = new EventEmitter();

    return {
        // -----------------------------------------------------------------
        // Registration
        // -----------------------------------------------------------------

        /**
         * Register a task provider for the given task type.
         * @param {string} type  - The task type (e.g. 'npm', 'grunt').
         * @param {object} provider - Must implement provideTasks(token) and optionally resolveTask(task, token).
         * @returns {Disposable}
         */
        registerTaskProvider(type, provider) {
            taskProviders.set(type, provider);
            rpc.sendNotification('tasks/register', { type, extensionId });
            return new Disposable(() => {
                taskProviders.delete(type);
                rpc.sendNotification('tasks/unregister', { type, extensionId });
            });
        },

        // -----------------------------------------------------------------
        // Fetch / Execute
        // -----------------------------------------------------------------

        /**
         * Collect tasks from all registered providers, optionally filtered by type.
         * @param {{ type?: string }} [filter]
         * @returns {Promise<object[]>}
         */
        async fetchTasks(filter) {
            const tasks = [];
            for (const [type, provider] of taskProviders) {
                if (filter && filter.type && filter.type !== type) {
                    continue;
                }
                try {
                    const provided = await provider.provideTasks(CancellationToken.None);
                    if (Array.isArray(provided)) {
                        tasks.push(...provided);
                    }
                } catch (_e) {
                    // Individual provider failures should not break collection.
                }
            }
            return tasks;
        },

        /**
         * Execute a task.
         * @param {object} task - The task to execute.
         * @returns {Promise<object>} A TaskExecution.
         */
        executeTask(task) {
            const serialised = {
                name: task.name,
                type: task.definition ? task.definition.type : undefined,
                definition: task.definition,
                source: task.source,
                scope: task.scope,
                detail: task.detail,
                group: task.group,
                presentationOptions: task.presentationOptions,
                problemMatchers: task.problemMatchers,
                isBackground: task.isBackground,
                execution: task.execution ? {
                    commandLine: task.execution.commandLine,
                    process: task.execution.process,
                    args: task.execution.args,
                    options: task.execution.options,
                } : undefined,
            };
            return rpc.sendRequest('tasks/executeTask', { task: serialised });
        },

        // -----------------------------------------------------------------
        // Getters
        // -----------------------------------------------------------------

        /** @returns {object[]} Currently active task executions. */
        get taskExecutions() {
            return [..._taskExecutions];
        },

        // -----------------------------------------------------------------
        // Events
        // -----------------------------------------------------------------

        onDidStartTask: _onDidStartTask.event,
        onDidEndTask: _onDidEndTask.event,
        onDidStartTaskProcess: _onDidStartTaskProcess.event,
        onDidEndTaskProcess: _onDidEndTaskProcess.event,

        // -----------------------------------------------------------------
        // Internal — called by the extension host on IDE notifications.
        // -----------------------------------------------------------------

        /**
         * @param {object} execution - { task, ... }
         */
        _onTaskStarted(execution) {
            _taskExecutions.push(execution);
            _onDidStartTask.fire({ execution });
        },

        /**
         * @param {object} execution
         */
        _onTaskEnded(execution) {
            _taskExecutions = _taskExecutions.filter(e => e !== execution);
            _onDidEndTask.fire({ execution });
        },

        /**
         * @param {object} event - { execution, processId }
         */
        _onTaskProcessStarted(event) {
            _onDidStartTaskProcess.fire(event);
        },

        /**
         * @param {object} event - { execution, exitCode }
         */
        _onTaskProcessEnded(event) {
            _onDidEndTaskProcess.fire(event);
        },

        /**
         * Retrieve a registered task provider.
         * @param {string} type
         * @returns {object|undefined}
         */
        _getProvider(type) {
            return taskProviders.get(type);
        },
    };
}

module.exports = { createTasksApi };
