'use strict';

/**
 * VS Code-compatible Disposable, EventEmitter, CancellationToken, and CancellationTokenSource.
 *
 * VS Code's EventEmitter differs from Node's:
 *   - `.event` returns a subscribe function (listener, thisArgs?, disposables?) => Disposable
 *   - `.fire(data)` emits to all listeners
 */

// ---------------------------------------------------------------------------
// Disposable
// ---------------------------------------------------------------------------

class Disposable {
    /**
     * @param {Function} callOnDispose - Called exactly once when dispose() is invoked.
     */
    constructor(callOnDispose) {
        this._callOnDispose = callOnDispose;
        this._isDisposed = false;
    }

    /**
     * Creates a Disposable that disposes all provided disposables.
     * @param  {...Disposable} disposables
     * @returns {Disposable}
     */
    static from(...disposables) {
        return new Disposable(() => {
            for (const d of disposables) {
                if (d && typeof d.dispose === 'function') {
                    d.dispose();
                }
            }
        });
    }

    /**
     * Invoke the cleanup callback. Safe to call multiple times; only runs once.
     */
    dispose() {
        if (!this._isDisposed) {
            this._isDisposed = true;
            if (typeof this._callOnDispose === 'function') {
                this._callOnDispose();
            }
        }
    }
}

// ---------------------------------------------------------------------------
// EventEmitter
// ---------------------------------------------------------------------------

class EventEmitter {
    constructor() {
        /** @type {Array<{listener: Function, thisArgs: any}>} */
        this._listeners = [];
        /** @type {Function|null} */
        this._event = null;
    }

    /**
     * The event subscribers can register on.
     * Returns a function: (listener, thisArgs?, disposables?) => Disposable
     * @returns {Function}
     */
    get event() {
        if (!this._event) {
            this._event = (listener, thisArgs, disposables) => {
                const entry = { listener, thisArgs };
                this._listeners.push(entry);

                const disposable = new Disposable(() => {
                    const idx = this._listeners.indexOf(entry);
                    if (idx >= 0) {
                        this._listeners.splice(idx, 1);
                    }
                });

                if (Array.isArray(disposables)) {
                    disposables.push(disposable);
                }

                return disposable;
            };
        }
        return this._event;
    }

    /**
     * Fire the event — calls every listener, swallowing individual errors.
     * @param {*} data
     */
    fire(data) {
        // Snapshot listeners so mutations during fire are safe.
        const snapshot = this._listeners.slice();
        for (const { listener, thisArgs } of snapshot) {
            try {
                listener.call(thisArgs, data);
            } catch (_err) {
                // swallow — VS Code behaviour
            }
        }
    }

    /**
     * Dispose the emitter — clears all listeners.
     */
    dispose() {
        this._listeners.length = 0;
        this._event = null;
    }
}

// ---------------------------------------------------------------------------
// CancellationToken
// ---------------------------------------------------------------------------

class CancellationToken {
    /**
     * @param {boolean} cancelled  Initial cancelled state.
     * @param {EventEmitter} [emitter] Optional emitter to use for onCancellationRequested.
     */
    constructor(cancelled, emitter) {
        this._isCancelled = !!cancelled;
        this._emitter = emitter || new EventEmitter();
    }

    /** @returns {boolean} */
    get isCancellationRequested() {
        return this._isCancelled;
    }

    /** @returns {Function} Subscribe function for cancellation. */
    get onCancellationRequested() {
        return this._emitter.event;
    }

    /**
     * Internal — marks the token as cancelled and fires the event.
     */
    _cancel() {
        if (!this._isCancelled) {
            this._isCancelled = true;
            this._emitter.fire(undefined);
        }
    }
}

/** A token that is never cancelled. */
CancellationToken.None = Object.freeze(
    (() => {
        const t = new CancellationToken(false);
        // Prevent _cancel from having any effect.
        t._cancel = () => {};
        return t;
    })()
);

/** A token that is always cancelled. */
CancellationToken.Cancelled = Object.freeze(new CancellationToken(true));

// ---------------------------------------------------------------------------
// CancellationTokenSource
// ---------------------------------------------------------------------------

class CancellationTokenSource {
    /**
     * @param {CancellationToken} [parent] If provided, this source cancels when the parent does.
     */
    constructor(parent) {
        this._token = null;
        this._parentListener = null;

        if (parent && parent.isCancellationRequested) {
            // Already cancelled — create pre-cancelled token.
            this._token = new CancellationToken(true);
        } else if (parent) {
            this._parentListener = parent.onCancellationRequested(() => {
                this.cancel();
            });
        }
    }

    /** @returns {CancellationToken} */
    get token() {
        if (!this._token) {
            this._token = new CancellationToken(false);
        }
        return this._token;
    }

    /**
     * Signal cancellation.
     */
    cancel() {
        if (!this._token) {
            // Materialise a pre-cancelled token.
            this._token = new CancellationToken(true);
        } else {
            this._token._cancel();
        }
    }

    /**
     * Dispose — detach from parent and clean up.
     */
    dispose() {
        if (this._parentListener) {
            this._parentListener.dispose();
            this._parentListener = null;
        }
        if (this._token) {
            this._token._emitter.dispose();
        }
    }
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

module.exports = {
    Disposable,
    EventEmitter,
    CancellationToken,
    CancellationTokenSource,
};
