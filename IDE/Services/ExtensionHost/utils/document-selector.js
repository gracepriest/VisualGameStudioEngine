'use strict';

const path = require('path');

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * Score a document against a VS Code DocumentSelector.
 *
 * @param {string|object|Array} selector
 *   - string           : matches languageId (score 10)
 *   - DocumentFilter   : { language?, scheme?, pattern?, notebookType? }
 *   - Array of above   : returns max score of any element
 * @param {string} uri         Document URI (e.g. "file:///c:/foo/bar.js")
 * @param {string} languageId  Language identifier (e.g. "javascript")
 * @returns {number} >0 if matches, 0 if no match. Higher = better.
 */
function score(selector, uri, languageId) {
    if (!selector) {
        return 0;
    }

    // Array — take the maximum score across all elements.
    if (Array.isArray(selector)) {
        let best = 0;
        for (const item of selector) {
            const s = score(item, uri, languageId);
            if (s > best) {
                best = s;
            }
        }
        return best;
    }

    // Plain string — shorthand for { language: selector }.
    if (typeof selector === 'string') {
        return selector === languageId ? 10 : 0;
    }

    // DocumentFilter object.
    if (typeof selector === 'object') {
        return scoreFilter(selector, uri, languageId);
    }

    return 0;
}

// ---------------------------------------------------------------------------
// Internal helpers
// ---------------------------------------------------------------------------

/**
 * Score a single DocumentFilter against a document.
 *
 * Scoring rules (additive):
 *   language exact match  = +10
 *   language '*'          = +5
 *   language mismatch     = 0  (entire filter fails)
 *   scheme exact match    = +10
 *   scheme '*'            = +5
 *   scheme mismatch       = 0  (entire filter fails)
 *   pattern match (glob)  = +10
 *   pattern mismatch      = 0  (entire filter fails)
 *   No constraints at all = 1  (weak match)
 *
 * @param {object} filter  DocumentFilter
 * @param {string} uri     Document URI
 * @param {string} languageId
 * @returns {number}
 */
function scoreFilter(filter, uri, languageId) {
    let result = 0;
    let hasConstraint = false;

    // --- language ---
    if (filter.language !== undefined && filter.language !== null) {
        hasConstraint = true;
        if (filter.language === '*') {
            result += 5;
        } else if (filter.language === languageId) {
            result += 10;
        } else {
            return 0; // hard mismatch
        }
    }

    // --- scheme ---
    if (filter.scheme !== undefined && filter.scheme !== null) {
        hasConstraint = true;
        const docScheme = extractScheme(uri);
        if (filter.scheme === '*') {
            result += 5;
        } else if (filter.scheme === docScheme) {
            result += 10;
        } else {
            return 0;
        }
    }

    // --- pattern (glob) ---
    if (filter.pattern !== undefined && filter.pattern !== null) {
        hasConstraint = true;
        const filePath = uriToFilePath(uri);
        if (matchGlob(filter.pattern, filePath)) {
            result += 10;
        } else {
            return 0;
        }
    }

    // If no constraints were specified the filter is extremely broad — score 1.
    if (!hasConstraint) {
        return 1;
    }

    return result;
}

/**
 * Extract the scheme portion from a URI string ("file", "untitled", etc.).
 * @param {string} uri
 * @returns {string}
 */
function extractScheme(uri) {
    if (!uri) return '';
    const idx = uri.indexOf('://');
    if (idx > 0) {
        return uri.substring(0, idx);
    }
    // No scheme — assume file.
    return 'file';
}

/**
 * Convert a URI to a file-system path for glob matching.
 * Handles file:///c:/... style URIs and plain paths.
 * @param {string} uri
 * @returns {string}
 */
function uriToFilePath(uri) {
    if (!uri) return '';
    if (uri.startsWith('file:///')) {
        let p = uri.substring(7); // strip file:///
        p = decodeURIComponent(p);
        // Normalise to forward slashes for consistent glob matching.
        return p.replace(/\\/g, '/');
    }
    if (uri.startsWith('file://')) {
        let p = uri.substring(7);
        p = decodeURIComponent(p);
        return p.replace(/\\/g, '/');
    }
    // Already a path or other scheme — return as-is with forward slashes.
    return uri.replace(/\\/g, '/');
}

// ---------------------------------------------------------------------------
// Glob matching
// ---------------------------------------------------------------------------

/**
 * Test whether a file path matches a glob pattern.
 *
 * Supported syntax:
 *   **   — matches any number of path segments (including zero)
 *   *    — matches any characters within a single path segment
 *   ?    — matches exactly one character (not separator)
 *   {a,b} — matches any of the comma-separated alternatives
 *
 * Matching is case-insensitive on Windows (process.platform === 'win32').
 *
 * @param {string} pattern  Glob pattern (may use / or \ separators).
 * @param {string} filePath File path to test.
 * @returns {boolean}
 */
function matchGlob(pattern, filePath) {
    if (!pattern || !filePath) return false;

    // Normalise separators.
    const normPattern = pattern.replace(/\\/g, '/');
    const normPath = filePath.replace(/\\/g, '/');

    const regex = globToRegex(normPattern);
    return regex.test(normPath);
}

/**
 * Convert a glob pattern to a RegExp.
 *
 * @param {string} glob  Normalised glob (forward slashes only).
 * @returns {RegExp}
 */
function globToRegex(glob) {
    let reStr = '';
    let i = 0;
    const len = glob.length;

    while (i < len) {
        const ch = glob[i];

        if (ch === '*') {
            if (i + 1 < len && glob[i + 1] === '*') {
                // ** — match across path segments.
                // Consume any trailing slash: **/ or just **
                i += 2;
                if (i < len && glob[i] === '/') {
                    i++;
                }
                reStr += '(?:.+/)?'; // zero or more path segments
            } else {
                // * — match within a single segment (no /).
                reStr += '[^/]*';
                i++;
            }
        } else if (ch === '?') {
            reStr += '[^/]';
            i++;
        } else if (ch === '{') {
            // Brace expansion: {a,b,c}
            const close = glob.indexOf('}', i);
            if (close === -1) {
                // Malformed — treat literally.
                reStr += escapeRegex(ch);
                i++;
            } else {
                const alternatives = glob.substring(i + 1, close).split(',');
                reStr += '(?:' + alternatives.map(a => globPartToRegex(a)).join('|') + ')';
                i = close + 1;
            }
        } else if (ch === '[') {
            // Character class — pass through mostly verbatim.
            const close = glob.indexOf(']', i);
            if (close === -1) {
                reStr += escapeRegex(ch);
                i++;
            } else {
                reStr += glob.substring(i, close + 1);
                i = close + 1;
            }
        } else {
            reStr += escapeRegex(ch);
            i++;
        }
    }

    const flags = process.platform === 'win32' ? 'i' : '';
    return new RegExp('^' + reStr + '$', flags);
}

/**
 * Convert a sub-expression inside brace expansion to regex.
 * Handles *, ?, and literal characters.
 * @param {string} part
 * @returns {string}
 */
function globPartToRegex(part) {
    let out = '';
    for (let i = 0; i < part.length; i++) {
        const ch = part[i];
        if (ch === '*') {
            out += '[^/]*';
        } else if (ch === '?') {
            out += '[^/]';
        } else {
            out += escapeRegex(ch);
        }
    }
    return out;
}

/**
 * Escape a character for use in a regular expression.
 * @param {string} ch
 * @returns {string}
 */
function escapeRegex(ch) {
    return ch.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

module.exports = {
    score,
    matchGlob,
};
