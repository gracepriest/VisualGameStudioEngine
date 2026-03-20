'use strict';

// JSON-RPC 2.0 protocol layer with Content-Length framing (LSP-style).
// Handles bidirectional communication between the C# IDE and this Node.js
// extension host over stdin/stdout.
//
// CommonJS module, targets Node.js 16+.

const HEADER_DELIMITER = '\r\n\r\n';
const CONTENT_LENGTH_RE = /Content-Length:\s*(\d+)/i;

// --- State ---

let _nextId = 1;

/** @type {Map<number, { resolve: Function, reject: Function }>} */
const _pendingRequests = new Map();

/** @type {Map<string, Function>} */
const _requestHandlers = new Map();

/** @type {Map<string, Function>} */
const _notificationHandlers = new Map();

/** @type {Map<number, Function>} */
const _cancelCallbacks = new Map();

// --- Writing (stdout) ---

/**
 * Send a raw JSON-RPC message with Content-Length framing to stdout.
 * @param {object} msg
 */
function _send(msg) {
    const body = JSON.stringify(msg);
    const byteLength = Buffer.byteLength(body, 'utf8');
    const header = `Content-Length: ${byteLength}${HEADER_DELIMITER}`;
    process.stdout.write(header + body);
}

/**
 * Send a JSON-RPC request and return a Promise for the response.
 * @param {string} method
 * @param {any} [params]
 * @returns {Promise<any>}
 */
function sendRequest(method, params) {
    const id = _nextId++;
    const msg = { jsonrpc: '2.0', id, method };
    if (params !== undefined) {
        msg.params = params;
    }
    return new Promise((resolve, reject) => {
        _pendingRequests.set(id, { resolve, reject });
        _send(msg);
    });
}

/**
 * Send a JSON-RPC notification (fire-and-forget, no ID).
 * @param {string} method
 * @param {any} [params]
 */
function sendNotification(method, params) {
    const msg = { jsonrpc: '2.0', method };
    if (params !== undefined) {
        msg.params = params;
    }
    _send(msg);
}

/**
 * Send a JSON-RPC response to an incoming request.
 * @param {number|string} id
 * @param {any} [result]
 * @param {{ code: number, message: string, data?: any }} [error]
 */
function sendResponse(id, result, error) {
    const msg = { jsonrpc: '2.0', id };
    if (error) {
        msg.error = { code: error.code, message: error.message };
        if (error.data !== undefined) {
            msg.error.data = error.data;
        }
    } else {
        msg.result = result !== undefined ? result : null;
    }
    _send(msg);
}

// --- Handler registration ---

/**
 * Register a handler for incoming requests from the IDE.
 * Handler receives (params) and should return a result or a Promise.
 * @param {string} method
 * @param {(params: any) => any | Promise<any>} handler
 */
function onRequest(method, handler) {
    _requestHandlers.set(method, handler);
}

/**
 * Register a handler for incoming notifications from the IDE.
 * @param {string} method
 * @param {(params: any) => void} handler
 */
function onNotification(method, handler) {
    _notificationHandlers.set(method, handler);
}

// --- Cancel callback management ---

/**
 * Register a cancellation callback for a given request ID.
 * Called when the IDE sends $/cancelRequest for that ID.
 * @param {number|string} requestId
 * @param {Function} callback
 */
function registerCancelCallback(requestId, callback) {
    _cancelCallbacks.set(requestId, callback);
}

/**
 * Unregister a cancellation callback.
 * @param {number|string} requestId
 */
function unregisterCancelCallback(requestId) {
    _cancelCallbacks.delete(requestId);
}

// --- Message routing ---

/**
 * Route an incoming JSON-RPC message.
 * @param {object} msg  Parsed JSON-RPC message
 */
function handleMessage(msg) {
    // --- $/cancelRequest notification ---
    if (msg.method === '$/cancelRequest') {
        const cancelId = msg.params && msg.params.id;
        if (cancelId != null) {
            // If we have a pending outgoing request with this id, reject it.
            const pending = _pendingRequests.get(cancelId);
            if (pending) {
                _pendingRequests.delete(cancelId);
                pending.reject(new RpcError(-32800, 'Request cancelled'));
            }
            // If there is a registered cancel callback, invoke it.
            const cb = _cancelCallbacks.get(cancelId);
            if (cb) {
                _cancelCallbacks.delete(cancelId);
                try { cb(); } catch (_) { /* swallow */ }
            }
        }
        return;
    }

    // --- Response to a request we sent ---
    if (msg.id != null && !msg.method) {
        const pending = _pendingRequests.get(msg.id);
        if (pending) {
            _pendingRequests.delete(msg.id);
            if (msg.error) {
                pending.reject(new RpcError(msg.error.code, msg.error.message, msg.error.data));
            } else {
                pending.resolve(msg.result);
            }
        }
        return;
    }

    // --- Incoming request (has id + method) ---
    if (msg.id != null && msg.method) {
        const handler = _requestHandlers.get(msg.method);
        if (!handler) {
            sendResponse(msg.id, undefined, {
                code: -32601,
                message: `Method not found: ${msg.method}`
            });
            return;
        }
        // Call handler, handle sync or async results.
        Promise.resolve()
            .then(() => handler(msg.params))
            .then(
                (result) => sendResponse(msg.id, result),
                (err) => {
                    const code = (err && err.code) || -32603;
                    const message = (err && err.message) || 'Internal error';
                    sendResponse(msg.id, undefined, { code, message });
                }
            );
        return;
    }

    // --- Incoming notification (has method, no id) ---
    if (msg.method) {
        const handler = _notificationHandlers.get(msg.method);
        if (handler) {
            try { handler(msg.params); } catch (_) { /* swallow */ }
        }
        return;
    }
}

// --- Reading (stdin) ---

/**
 * Start reading Content-Length framed JSON-RPC messages from stdin.
 * Call once at startup.
 */
function startReading() {
    let buffer = Buffer.alloc(0);
    let contentLength = -1;

    process.stdin.on('data', (chunk) => {
        buffer = Buffer.concat([buffer, chunk]);

        // eslint-disable-next-line no-constant-condition
        while (true) {
            // If we don't know the content length yet, look for the header.
            if (contentLength < 0) {
                const headerEnd = buffer.indexOf(HEADER_DELIMITER);
                if (headerEnd < 0) {
                    break; // Need more data for the header.
                }
                const header = buffer.slice(0, headerEnd).toString('ascii');
                const match = CONTENT_LENGTH_RE.exec(header);
                if (!match) {
                    // Malformed header -- skip past delimiter and retry.
                    buffer = buffer.slice(headerEnd + HEADER_DELIMITER.length);
                    continue;
                }
                contentLength = parseInt(match[1], 10);
                buffer = buffer.slice(headerEnd + HEADER_DELIMITER.length);
            }

            // We know the content length -- wait until we have enough bytes.
            if (buffer.length < contentLength) {
                break;
            }

            const body = buffer.slice(0, contentLength).toString('utf8');
            buffer = buffer.slice(contentLength);
            contentLength = -1;

            let msg;
            try {
                msg = JSON.parse(body);
            } catch (_) {
                // Malformed JSON -- discard and continue.
                continue;
            }

            handleMessage(msg);
        }
    });
}

// --- Error class ---

class RpcError extends Error {
    /**
     * @param {number} code
     * @param {string} message
     * @param {any} [data]
     */
    constructor(code, message, data) {
        super(message);
        this.code = code;
        if (data !== undefined) {
            this.data = data;
        }
    }
}

// --- Exports ---

module.exports = {
    sendRequest,
    sendNotification,
    sendResponse,
    onRequest,
    onNotification,
    handleMessage,
    registerCancelCallback,
    unregisterCancelCallback,
    startReading,
    RpcError,
};
