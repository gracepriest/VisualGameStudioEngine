'use strict';

// Central provider registry for language feature providers registered by extensions.
// When the IDE requests a language feature, the registry finds matching providers
// and dispatches to them with timeout and result merging.
//
// CommonJS module, targets Node.js 16+.

const { Disposable } = require('./vscode-api/event');

// ---------------------------------------------------------------------------
// Provider type constants
// ---------------------------------------------------------------------------

const PROVIDER_TYPES = Object.freeze({
    COMPLETION: 'completion',
    HOVER: 'hover',
    DEFINITION: 'definition',
    TYPE_DEFINITION: 'typeDefinition',
    IMPLEMENTATION: 'implementation',
    REFERENCES: 'references',
    DOCUMENT_HIGHLIGHT: 'documentHighlight',
    DOCUMENT_SYMBOL: 'documentSymbol',
    WORKSPACE_SYMBOL: 'workspaceSymbol',
    CODE_ACTION: 'codeAction',
    CODE_LENS: 'codeLens',
    DOCUMENT_FORMATTING: 'documentFormatting',
    DOCUMENT_RANGE_FORMATTING: 'documentRangeFormatting',
    ON_TYPE_FORMATTING: 'onTypeFormatting',
    RENAME: 'rename',
    SIGNATURE_HELP: 'signatureHelp',
    DOCUMENT_LINK: 'documentLink',
    COLOR: 'color',
    FOLDING_RANGE: 'foldingRange',
    SELECTION_RANGE: 'selectionRange',
    INLAY_HINT: 'inlayHint',
    LINKED_EDITING_RANGE: 'linkedEditingRange',
    DECLARATION: 'declaration',
    CALL_HIERARCHY: 'callHierarchy',
    TYPE_HIERARCHY: 'typeHierarchy',
    SEMANTIC_TOKENS: 'semanticTokens',
    DOCUMENT_DROP: 'documentDrop',
});

// Provider types whose results should be concatenated when multiple providers match.
const CONCAT_TYPES = new Set([
    PROVIDER_TYPES.DEFINITION,
    PROVIDER_TYPES.REFERENCES,
    PROVIDER_TYPES.DOCUMENT_HIGHLIGHT,
    PROVIDER_TYPES.DOCUMENT_SYMBOL,
    PROVIDER_TYPES.CODE_ACTION,
    PROVIDER_TYPES.CODE_LENS,
    PROVIDER_TYPES.DOCUMENT_LINK,
    PROVIDER_TYPES.FOLDING_RANGE,
    PROVIDER_TYPES.SELECTION_RANGE,
    PROVIDER_TYPES.INLAY_HINT,
    PROVIDER_TYPES.DECLARATION,
    PROVIDER_TYPES.WORKSPACE_SYMBOL,
]);

// Provider types where first registered provider wins (no merging).
const FIRST_WINS_TYPES = new Set([
    PROVIDER_TYPES.DOCUMENT_FORMATTING,
    PROVIDER_TYPES.DOCUMENT_RANGE_FORMATTING,
    PROVIDER_TYPES.ON_TYPE_FORMATTING,
]);

// Provider types where first non-null result wins.
const FIRST_NONNULL_TYPES = new Set([
    PROVIDER_TYPES.HOVER,
    PROVIDER_TYPES.RENAME,
    PROVIDER_TYPES.SIGNATURE_HELP,
]);

// ---------------------------------------------------------------------------
// Document selector scoring (lazy-loaded)
// ---------------------------------------------------------------------------

/**
 * Score a document against a selector. Higher = better match.
 * Uses ./utils/document-selector if available, otherwise falls back to
 * a simple language-id matcher.
 */
let _scoreFn = null;

function scoreSelector(selector, uri, languageId) {
    if (!_scoreFn) {
        try {
            const ds = require('./utils/document-selector');
            if (typeof ds.score === 'function') {
                _scoreFn = ds.score;
            }
        } catch (_e) {
            // Module not available yet — use fallback
        }
        if (!_scoreFn) {
            _scoreFn = _fallbackScore;
        }
    }
    return _scoreFn(selector, uri, languageId);
}

/**
 * Fallback selector scoring when document-selector module is unavailable.
 * Returns 10 for exact language match, 5 for wildcard, 0 for no match.
 */
function _fallbackScore(selector, uri, languageId) {
    if (!selector) return 0;

    const uriStr = typeof uri === 'string' ? uri : (uri && typeof uri.toString === 'function' ? uri.toString() : '');

    // Selector can be a string (languageId), an object, or an array
    if (typeof selector === 'string') {
        return selector === languageId ? 10 : (selector === '*' ? 5 : 0);
    }

    if (Array.isArray(selector)) {
        let best = 0;
        for (const item of selector) {
            best = Math.max(best, _fallbackScore(item, uri, languageId));
        }
        return best;
    }

    // Object: { language?, scheme?, pattern? }
    let score = 0;
    if (selector.language) {
        if (selector.language === languageId) {
            score += 10;
        } else if (selector.language === '*') {
            score += 5;
        } else {
            return 0; // Language mismatch — no match
        }
    }
    if (selector.scheme) {
        if (uriStr.startsWith(selector.scheme + '://') || uriStr.startsWith(selector.scheme + ':')) {
            score += 1;
        } else if (selector.scheme !== '*') {
            return 0; // Scheme mismatch
        }
    }
    // pattern matching would require glob — skip in fallback, treat as +1 if present
    if (selector.pattern) {
        score += 1;
    }
    return score || 1; // If object had no specific fields, give minimal score
}

// ---------------------------------------------------------------------------
// Timeout helper
// ---------------------------------------------------------------------------

const PROVIDER_TIMEOUT_MS = 5000;

/**
 * Run a function with a timeout. Resolves with the result or undefined on timeout.
 * @param {Function} fn
 * @param {number} timeoutMs
 * @returns {Promise<any>}
 */
function withTimeout(fn, timeoutMs) {
    return new Promise((resolve) => {
        let settled = false;
        const timer = setTimeout(() => {
            if (!settled) {
                settled = true;
                resolve(undefined);
            }
        }, timeoutMs);

        Promise.resolve().then(() => fn()).then(
            (result) => {
                if (!settled) {
                    settled = true;
                    clearTimeout(timer);
                    resolve(result);
                }
            },
            (_err) => {
                if (!settled) {
                    settled = true;
                    clearTimeout(timer);
                    resolve(undefined); // Skip failed providers
                }
            },
        );
    });
}

// ---------------------------------------------------------------------------
// ProviderRegistry
// ---------------------------------------------------------------------------

let _nextId = 1;

class ProviderRegistry {
    constructor() {
        /** @type {Map<string, Array<{id: number, type: string, selector: any, provider: any, extensionId: string|undefined, metadata: any}>>} */
        this._registrations = new Map();
    }

    /**
     * Register a language feature provider.
     * @param {string} type       One of PROVIDER_TYPES values
     * @param {any}    selector   Document selector (string, object, or array)
     * @param {any}    provider   The provider object (has provideXxx methods)
     * @param {any}    [metadata] Optional metadata (e.g., trigger characters)
     * @returns {Disposable}
     */
    register(type, selector, provider, metadata) {
        const id = _nextId++;
        const registration = {
            id,
            type,
            selector,
            provider,
            extensionId: metadata && metadata.extensionId || undefined,
            metadata: metadata || undefined,
        };

        if (!this._registrations.has(type)) {
            this._registrations.set(type, []);
        }
        this._registrations.get(type).push(registration);

        return new Disposable(() => {
            const list = this._registrations.get(type);
            if (list) {
                const idx = list.findIndex(r => r.id === id);
                if (idx >= 0) {
                    list.splice(idx, 1);
                }
            }
        });
    }

    /**
     * Convenience: add a provider and return its numeric id.
     * Used by languages.js which manages its own Disposable.
     */
    add(type, selector, provider, extensionId) {
        const id = _nextId++;
        const registration = { id, type, selector, provider, extensionId, metadata: undefined };
        if (!this._registrations.has(type)) this._registrations.set(type, []);
        this._registrations.get(type).push(registration);
        return id;
    }

    /**
     * Remove a provider by its numeric id.
     */
    remove(id) {
        for (const [type, list] of this._registrations) {
            const idx = list.findIndex(r => r.id === id);
            if (idx >= 0) { list.splice(idx, 1); return; }
        }
    }

    /**
     * Get all providers matching the given type, URI, and language ID,
     * sorted by selector score (highest first).
     * @param {string} type
     * @param {any}    uri
     * @param {string} languageId
     * @returns {Array<{provider: any, registration: object, score: number}>}
     */
    getProviders(type, uri, languageId) {
        const list = this._registrations.get(type);
        if (!list || list.length === 0) return [];

        const scored = [];
        for (const reg of list) {
            const s = scoreSelector(reg.selector, uri, languageId);
            if (s > 0) {
                scored.push({ provider: reg.provider, registration: reg, score: s });
            }
        }

        // Sort descending by score
        scored.sort((a, b) => b.score - a.score);
        return scored;
    }

    /**
     * Whether any provider is registered for the given type + document.
     * @param {string} type
     * @param {any}    uri
     * @param {string} languageId
     * @returns {boolean}
     */
    hasProviders(type, uri, languageId) {
        return this.getProviders(type, uri, languageId).length > 0;
    }

    /**
     * Get all registrations across all types (for IDE notifications).
     * @returns {Array<object>}
     */
    getRegistrations() {
        const all = [];
        for (const list of this._registrations.values()) {
            all.push(...list);
        }
        return all;
    }

    /**
     * Dispatch a request to matching providers, with timeout and result merging.
     * @param {string} type
     * @param {object} document    TextDocument
     * @param {object} params      Request-specific params (position, range, etc.)
     * @param {object} [token]     CancellationToken
     * @returns {Promise<any>}
     */
    async dispatchRequest(type, document, params, token) {
        const uri = document && document.uri;
        const languageId = document && document.languageId;
        const matches = this.getProviders(type, uri, languageId);

        if (matches.length === 0) return null;

        // -- Completion: merge all items into a single CompletionList --------
        if (type === PROVIDER_TYPES.COMPLETION) {
            return this._dispatchCompletion(matches, document, params, token);
        }

        // -- Concat types: gather all results into one array -----------------
        if (CONCAT_TYPES.has(type)) {
            return this._dispatchConcat(matches, document, params, token);
        }

        // -- First-wins types: use first registered provider -----------------
        if (FIRST_WINS_TYPES.has(type)) {
            return this._dispatchFirstWins(matches, document, params, token);
        }

        // -- First non-null types --------------------------------------------
        if (FIRST_NONNULL_TYPES.has(type)) {
            return this._dispatchFirstNonNull(matches, document, params, token);
        }

        // -- Default: first non-null -----------------------------------------
        return this._dispatchFirstNonNull(matches, document, params, token);
    }

    // -- Dispatch strategies -------------------------------------------------

    /**
     * Completion: merge all provider results into a single CompletionList.
     */
    async _dispatchCompletion(matches, document, params, token) {
        const allItems = [];
        let isIncomplete = false;

        const promises = matches.map(m =>
            withTimeout(() => this._invokeProvider(m.provider, PROVIDER_TYPES.COMPLETION, document, params, token), PROVIDER_TIMEOUT_MS),
        );
        const results = await Promise.all(promises);

        for (const result of results) {
            if (!result) continue;
            if (Array.isArray(result)) {
                allItems.push(...result);
            } else if (result.items) {
                allItems.push(...result.items);
                if (result.isIncomplete) isIncomplete = true;
            }
        }

        return { items: allItems, isIncomplete };
    }

    /**
     * Concatenate all provider results into a single array.
     */
    async _dispatchConcat(matches, document, params, token) {
        const allResults = [];

        const promises = matches.map(m =>
            withTimeout(() => this._invokeProvider(m.provider, m.registration.type, document, params, token), PROVIDER_TIMEOUT_MS),
        );
        const results = await Promise.all(promises);

        for (const result of results) {
            if (!result) continue;
            if (Array.isArray(result)) {
                allResults.push(...result);
            } else {
                allResults.push(result);
            }
        }

        return allResults;
    }

    /**
     * First registered provider wins (no fallback to others).
     */
    async _dispatchFirstWins(matches, document, params, token) {
        if (matches.length === 0) return null;
        return withTimeout(
            () => this._invokeProvider(matches[0].provider, matches[0].registration.type, document, params, token),
            PROVIDER_TIMEOUT_MS,
        );
    }

    /**
     * Try providers in order, return first non-null result.
     */
    async _dispatchFirstNonNull(matches, document, params, token) {
        for (const m of matches) {
            const result = await withTimeout(
                () => this._invokeProvider(m.provider, m.registration.type, document, params, token),
                PROVIDER_TIMEOUT_MS,
            );
            if (result != null) return result;
        }
        return null;
    }

    /**
     * Invoke a provider's method for the given type.
     * Maps PROVIDER_TYPES to the standard VS Code provider method names.
     */
    _invokeProvider(provider, type, document, params, token) {
        const methodName = PROVIDER_METHOD_MAP[type];
        if (methodName && typeof provider[methodName] === 'function') {
            return provider[methodName](document, params, token);
        }
        // Try generic 'provide' method
        if (typeof provider.provide === 'function') {
            return provider.provide(document, params, token);
        }
        return Promise.resolve(null);
    }
}

// ---------------------------------------------------------------------------
// Provider type → method name mapping
// ---------------------------------------------------------------------------

const PROVIDER_METHOD_MAP = {
    [PROVIDER_TYPES.COMPLETION]: 'provideCompletionItems',
    [PROVIDER_TYPES.HOVER]: 'provideHover',
    [PROVIDER_TYPES.DEFINITION]: 'provideDefinition',
    [PROVIDER_TYPES.TYPE_DEFINITION]: 'provideTypeDefinition',
    [PROVIDER_TYPES.IMPLEMENTATION]: 'provideImplementation',
    [PROVIDER_TYPES.REFERENCES]: 'provideReferences',
    [PROVIDER_TYPES.DOCUMENT_HIGHLIGHT]: 'provideDocumentHighlights',
    [PROVIDER_TYPES.DOCUMENT_SYMBOL]: 'provideDocumentSymbols',
    [PROVIDER_TYPES.WORKSPACE_SYMBOL]: 'provideWorkspaceSymbols',
    [PROVIDER_TYPES.CODE_ACTION]: 'provideCodeActions',
    [PROVIDER_TYPES.CODE_LENS]: 'provideCodeLenses',
    [PROVIDER_TYPES.DOCUMENT_FORMATTING]: 'provideDocumentFormattingEdits',
    [PROVIDER_TYPES.DOCUMENT_RANGE_FORMATTING]: 'provideDocumentRangeFormattingEdits',
    [PROVIDER_TYPES.ON_TYPE_FORMATTING]: 'provideOnTypeFormattingEdits',
    [PROVIDER_TYPES.RENAME]: 'provideRenameEdits',
    [PROVIDER_TYPES.SIGNATURE_HELP]: 'provideSignatureHelp',
    [PROVIDER_TYPES.DOCUMENT_LINK]: 'provideDocumentLinks',
    [PROVIDER_TYPES.COLOR]: 'provideDocumentColors',
    [PROVIDER_TYPES.FOLDING_RANGE]: 'provideFoldingRanges',
    [PROVIDER_TYPES.SELECTION_RANGE]: 'provideSelectionRanges',
    [PROVIDER_TYPES.INLAY_HINT]: 'provideInlayHints',
    [PROVIDER_TYPES.LINKED_EDITING_RANGE]: 'provideLinkedEditingRanges',
    [PROVIDER_TYPES.DECLARATION]: 'provideDeclaration',
    [PROVIDER_TYPES.CALL_HIERARCHY]: 'prepareCallHierarchy',
    [PROVIDER_TYPES.TYPE_HIERARCHY]: 'prepareTypeHierarchy',
    [PROVIDER_TYPES.SEMANTIC_TOKENS]: 'provideDocumentSemanticTokens',
    [PROVIDER_TYPES.DOCUMENT_DROP]: 'provideDocumentDropEdits',
};

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

module.exports = {
    ProviderRegistry,
    PROVIDER_TYPES,
};
