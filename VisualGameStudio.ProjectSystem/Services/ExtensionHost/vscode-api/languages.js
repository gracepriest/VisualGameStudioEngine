'use strict';

const { Disposable, EventEmitter } = require('./event');

/**
 * Creates the vscode.languages namespace API.
 *
 * @param {object} rpc              - JSON-RPC module with sendRequest / sendNotification.
 * @param {object} providerRegistry - Central registry for language providers.
 * @param {string} extensionId      - The owning extension identifier.
 * @returns {object} languages namespace
 */
function createLanguagesApi(rpc, providerRegistry, extensionId) {
    /** @type {Map<string, {name: string, entries: Map<string, object[]>}>} */
    const _diagnosticCollections = new Map();
    let _nextCollectionId = 1;

    const _onDidChangeDiagnostics = new EventEmitter();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /**
     * Normalise a document selector to a serialisable form.
     * @param {string|object|Array} selector
     * @returns {Array}
     */
    function _normaliseSelector(selector) {
        if (!selector) return [];
        if (typeof selector === 'string') return [{ language: selector }];
        if (Array.isArray(selector)) return selector;
        return [selector];
    }

    function _uriToString(uri) {
        if (!uri) return '';
        if (typeof uri === 'string') return uri;
        if (typeof uri.toString === 'function') return uri.toString();
        return String(uri);
    }

    /**
     * Common provider registration pattern.
     * @param {string} type           - Provider type name (e.g. 'completion', 'hover')
     * @param {*}      selector       - Document selector (null for workspace-wide providers)
     * @param {object} provider       - The provider object
     * @param {object} [metadata]     - Extra metadata to send to IDE
     * @returns {Disposable}
     */
    function _registerProvider(type, selector, provider, metadata) {
        const id = providerRegistry.add(type, selector, provider, extensionId);
        rpc.sendNotification('languages/registerProvider', {
            type,
            id,
            extensionId,
            selector: selector ? _normaliseSelector(selector) : null,
            metadata: metadata || undefined,
        });
        return new Disposable(() => {
            providerRegistry.remove(id);
            rpc.sendNotification('languages/unregisterProvider', { type, id });
        });
    }

    // -----------------------------------------------------------------------
    // Languages API
    // -----------------------------------------------------------------------

    return {
        // -------------------------------------------------------------------
        // Provider registrations
        // -------------------------------------------------------------------

        registerCompletionItemProvider(selector, provider, ...triggerCharacters) {
            return _registerProvider('completion', selector, provider, { triggerCharacters });
        },

        registerHoverProvider(selector, provider) {
            return _registerProvider('hover', selector, provider);
        },

        registerDefinitionProvider(selector, provider) {
            return _registerProvider('definition', selector, provider);
        },

        registerTypeDefinitionProvider(selector, provider) {
            return _registerProvider('typeDefinition', selector, provider);
        },

        registerImplementationProvider(selector, provider) {
            return _registerProvider('implementation', selector, provider);
        },

        registerReferenceProvider(selector, provider) {
            return _registerProvider('references', selector, provider);
        },

        registerDocumentHighlightProvider(selector, provider) {
            return _registerProvider('documentHighlight', selector, provider);
        },

        registerDocumentSymbolProvider(selector, provider, metadata) {
            return _registerProvider('documentSymbol', selector, provider,
                metadata ? { label: metadata.label } : undefined);
        },

        registerWorkspaceSymbolProvider(provider) {
            return _registerProvider('workspaceSymbol', null, provider);
        },

        registerCodeActionsProvider(selector, provider, metadata) {
            return _registerProvider('codeAction', selector, provider,
                metadata ? { providedCodeActionKinds: metadata.providedCodeActionKinds } : undefined);
        },

        registerCodeLensProvider(selector, provider) {
            return _registerProvider('codeLens', selector, provider);
        },

        registerDocumentFormattingEditProvider(selector, provider) {
            return _registerProvider('documentFormatting', selector, provider);
        },

        registerDocumentRangeFormattingEditProvider(selector, provider) {
            return _registerProvider('documentRangeFormatting', selector, provider);
        },

        registerOnTypeFormattingEditProvider(selector, provider, firstTriggerChar, ...moreTriggerChars) {
            const triggerCharacters = [firstTriggerChar, ...moreTriggerChars];
            return _registerProvider('onTypeFormatting', selector, provider, { triggerCharacters });
        },

        registerRenameProvider(selector, provider) {
            return _registerProvider('rename', selector, provider);
        },

        registerSignatureHelpProvider(selector, provider, ...triggerCharsOrMetadata) {
            let metadata;
            const last = triggerCharsOrMetadata[triggerCharsOrMetadata.length - 1];
            if (last && typeof last === 'object' && !Array.isArray(last) &&
                (last.triggerCharacters || last.retriggerCharacters)) {
                // Last argument is a metadata object
                metadata = {
                    triggerCharacters: last.triggerCharacters || [],
                    retriggerCharacters: last.retriggerCharacters || [],
                };
            } else {
                // All arguments are trigger characters
                metadata = { triggerCharacters: triggerCharsOrMetadata };
            }
            return _registerProvider('signatureHelp', selector, provider, metadata);
        },

        registerDocumentLinkProvider(selector, provider) {
            return _registerProvider('documentLink', selector, provider);
        },

        registerColorProvider(selector, provider) {
            return _registerProvider('color', selector, provider);
        },

        registerFoldingRangeProvider(selector, provider) {
            return _registerProvider('foldingRange', selector, provider);
        },

        registerSelectionRangeProvider(selector, provider) {
            return _registerProvider('selectionRange', selector, provider);
        },

        registerInlayHintsProvider(selector, provider) {
            return _registerProvider('inlayHints', selector, provider);
        },

        registerLinkedEditingRangeProvider(selector, provider) {
            return _registerProvider('linkedEditingRange', selector, provider);
        },

        registerDeclarationProvider(selector, provider) {
            return _registerProvider('declaration', selector, provider);
        },

        registerCallHierarchyProvider(selector, provider) {
            return _registerProvider('callHierarchy', selector, provider);
        },

        registerTypeHierarchyProvider(selector, provider) {
            return _registerProvider('typeHierarchy', selector, provider);
        },

        registerDocumentSemanticTokensProvider(selector, provider, legend) {
            return _registerProvider('documentSemanticTokens', selector, provider, { legend });
        },

        registerDocumentRangeSemanticTokensProvider(selector, provider, legend) {
            return _registerProvider('documentRangeSemanticTokens', selector, provider, { legend });
        },

        registerEvaluatableExpressionProvider(selector, provider) {
            return _registerProvider('evaluatableExpression', selector, provider);
        },

        registerInlineValuesProvider(selector, provider) {
            return _registerProvider('inlineValues', selector, provider);
        },

        // -------------------------------------------------------------------
        // Diagnostics
        // -------------------------------------------------------------------

        /**
         * Create a diagnostic collection.
         * @param {string} [name]
         * @returns {object} DiagnosticCollection
         */
        createDiagnosticCollection(name) {
            const collectionName = name || `__diag_${_nextCollectionId++}`;

            /** @type {Map<string, object[]>} uri string -> Diagnostic[] */
            const entries = new Map();

            function _publishUri(uriStr) {
                const diagnostics = entries.get(uriStr) || [];
                rpc.sendNotification('languages/publishDiagnostics', {
                    uri: uriStr,
                    diagnostics,
                    collection: collectionName,
                });
                _onDidChangeDiagnostics.fire({ uris: [uriStr] });
            }

            const collection = {
                get name() {
                    return collectionName;
                },

                set(uriOrEntries, diagnostics) {
                    if (Array.isArray(uriOrEntries)) {
                        // Array of [uri, diagnostics[]]
                        const changedUris = [];
                        for (const [uri, diags] of uriOrEntries) {
                            const uriStr = _uriToString(uri);
                            if (diags && diags.length > 0) {
                                entries.set(uriStr, [...diags]);
                            } else {
                                entries.delete(uriStr);
                            }
                            changedUris.push(uriStr);
                        }
                        for (const uriStr of changedUris) {
                            _publishUri(uriStr);
                        }
                    } else {
                        // Single uri + diagnostics
                        const uriStr = _uriToString(uriOrEntries);
                        if (diagnostics && diagnostics.length > 0) {
                            entries.set(uriStr, [...diagnostics]);
                        } else {
                            entries.delete(uriStr);
                        }
                        _publishUri(uriStr);
                    }
                },

                delete(uri) {
                    const uriStr = _uriToString(uri);
                    entries.delete(uriStr);
                    _publishUri(uriStr);
                },

                clear() {
                    const uris = [...entries.keys()];
                    entries.clear();
                    for (const uriStr of uris) {
                        _publishUri(uriStr);
                    }
                },

                forEach(callback, thisArg) {
                    for (const [uriStr, diags] of entries) {
                        callback.call(thisArg, uriStr, diags, collection);
                    }
                },

                get(uri) {
                    const uriStr = _uriToString(uri);
                    return entries.get(uriStr) || [];
                },

                has(uri) {
                    const uriStr = _uriToString(uri);
                    return entries.has(uriStr);
                },

                dispose() {
                    collection.clear();
                    _diagnosticCollections.delete(collectionName);
                    rpc.sendNotification('languages/disposeDiagnosticCollection', {
                        collection: collectionName,
                    });
                },
            };

            _diagnosticCollections.set(collectionName, collection);
            return collection;
        },

        /**
         * Get diagnostics for a resource or all diagnostics.
         * @param {string|object} [resource]
         * @returns {object[]|Array<[string, object[]]>}
         */
        getDiagnostics(resource) {
            if (resource) {
                const uriStr = _uriToString(resource);
                const all = [];
                for (const coll of _diagnosticCollections.values()) {
                    const diags = coll.get(uriStr);
                    if (diags && diags.length > 0) {
                        all.push(...diags);
                    }
                }
                return all;
            }
            // Return all as [uri, diagnostics[]][]
            const merged = new Map();
            for (const coll of _diagnosticCollections.values()) {
                coll.forEach((uriStr, diags) => {
                    const existing = merged.get(uriStr) || [];
                    existing.push(...diags);
                    merged.set(uriStr, existing);
                });
            }
            return [...merged.entries()];
        },

        onDidChangeDiagnostics: _onDidChangeDiagnostics.event,

        // -------------------------------------------------------------------
        // Utilities
        // -------------------------------------------------------------------

        /**
         * Score a document against a selector.
         * @param {*} selector
         * @param {object} document
         * @returns {number}
         */
        match(selector, document) {
            const { score } = require('../utils/document-selector');
            return score(selector, _uriToString(document.uri), document.languageId);
        },

        /**
         * Get all known language identifiers.
         * @returns {Promise<string[]>}
         */
        getLanguages() {
            return rpc.sendRequest('languages/getLanguages');
        },

        /**
         * Change the language of a text document.
         * @param {object} document
         * @param {string} languageId
         * @returns {Promise<object>}
         */
        setTextDocumentLanguage(document, languageId) {
            return rpc.sendRequest('languages/setLanguage', {
                uri: _uriToString(document.uri),
                languageId,
            });
        },
    };
}

module.exports = { createLanguagesApi };
