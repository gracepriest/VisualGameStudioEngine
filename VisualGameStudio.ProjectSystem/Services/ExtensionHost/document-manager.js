'use strict';

// TextDocument model and DocumentManager for the extension host.
// Maintains synchronized document state matching the IDE's open editors.
//
// CommonJS module, targets Node.js 16+.

const { EventEmitter, Disposable } = require('./vscode-api/event');

// ---------------------------------------------------------------------------
// EndOfLine enum
// ---------------------------------------------------------------------------

const EndOfLine = Object.freeze({
    LF: 1,
    CRLF: 2,
});

// ---------------------------------------------------------------------------
// Minimal Position / Range / Uri (inline, no external dependency)
// ---------------------------------------------------------------------------
// These are lightweight value types used internally by TextDocument.
// If a full vscode-api/types module is introduced later, swap these out.

class Position {
    /**
     * @param {number} line 0-based line number
     * @param {number} character 0-based character offset
     */
    constructor(line, character) {
        this.line = line;
        this.character = character;
    }

    isEqual(other) {
        return this.line === other.line && this.character === other.character;
    }

    isBefore(other) {
        return this.line < other.line || (this.line === other.line && this.character < other.character);
    }

    isAfter(other) {
        return this.line > other.line || (this.line === other.line && this.character > other.character);
    }

    compareTo(other) {
        if (this.isBefore(other)) return -1;
        if (this.isAfter(other)) return 1;
        return 0;
    }

    translate(lineDelta, charDelta) {
        return new Position(this.line + (lineDelta || 0), this.character + (charDelta || 0));
    }

    with(line, character) {
        return new Position(
            typeof line === 'number' ? line : this.line,
            typeof character === 'number' ? character : this.character,
        );
    }
}

class Range {
    /**
     * @param {Position|number} startOrStartLine
     * @param {Position|number} endOrStartChar
     * @param {number} [endLine]
     * @param {number} [endChar]
     */
    constructor(startOrStartLine, endOrStartChar, endLine, endChar) {
        if (typeof startOrStartLine === 'number') {
            this.start = new Position(startOrStartLine, endOrStartChar);
            this.end = new Position(endLine, endChar);
        } else {
            this.start = startOrStartLine;
            this.end = endOrStartChar;
        }
    }

    get isEmpty() {
        return this.start.isEqual(this.end);
    }

    get isSingleLine() {
        return this.start.line === this.end.line;
    }

    contains(positionOrRange) {
        if (positionOrRange instanceof Range) {
            return this.contains(positionOrRange.start) && this.contains(positionOrRange.end);
        }
        return !positionOrRange.isBefore(this.start) && !positionOrRange.isAfter(this.end);
    }

    isEqual(other) {
        return this.start.isEqual(other.start) && this.end.isEqual(other.end);
    }

    intersection(other) {
        const start = this.start.isAfter(other.start) ? this.start : other.start;
        const end = this.end.isBefore(other.end) ? this.end : other.end;
        if (start.isAfter(end)) return undefined;
        return new Range(start, end);
    }

    union(other) {
        const start = this.start.isBefore(other.start) ? this.start : other.start;
        const end = this.end.isAfter(other.end) ? this.end : other.end;
        return new Range(start, end);
    }

    with(start, end) {
        return new Range(start || this.start, end || this.end);
    }
}

class Uri {
    /**
     * @param {string} scheme
     * @param {string} authority
     * @param {string} path
     * @param {string} query
     * @param {string} fragment
     */
    constructor(scheme, authority, path, query, fragment) {
        this.scheme = scheme || 'file';
        this.authority = authority || '';
        this.path = path || '';
        this.query = query || '';
        this.fragment = fragment || '';
    }

    get fsPath() {
        if (this.scheme !== 'file') return this.path;
        // Convert /C:/foo to C:\foo on Windows-style paths
        let p = this.path;
        if (p.length >= 3 && p[0] === '/' && p[2] === ':') {
            p = p.substring(1);
        }
        const pathMod = require('path');
        return p.replace(/\//g, pathMod.sep);
    }

    toString() {
        if (this.scheme === 'file') {
            return 'file://' + this.authority + this.path;
        }
        let result = this.scheme + '://';
        if (this.authority) result += this.authority;
        result += this.path;
        if (this.query) result += '?' + this.query;
        if (this.fragment) result += '#' + this.fragment;
        return result;
    }

    static parse(value) {
        if (typeof value !== 'string') return value;
        try {
            const url = new URL(value);
            return new Uri(
                url.protocol.replace(/:$/, ''),
                url.hostname,
                decodeURIComponent(url.pathname),
                url.search.replace(/^\?/, ''),
                url.hash.replace(/^#/, ''),
            );
        } catch (_e) {
            // Treat as file path
            return Uri.file(value);
        }
    }

    static file(fsPath) {
        let p = fsPath.replace(/\\/g, '/');
        if (p[0] !== '/') p = '/' + p;
        return new Uri('file', '', p, '', '');
    }
}

// ---------------------------------------------------------------------------
// TextLine
// ---------------------------------------------------------------------------

class TextLine {
    /**
     * @param {number} lineNumber 0-based
     * @param {string} text       line text WITHOUT line ending
     * @param {string} lineBreak  the actual line break string ('\n', '\r\n', or '')
     */
    constructor(lineNumber, text, lineBreak) {
        this._lineNumber = lineNumber;
        this._text = text;
        this._lineBreak = lineBreak;
    }

    /** 0-based line index. */
    get lineNumber() {
        return this._lineNumber;
    }

    /** Line text without the line ending. */
    get text() {
        return this._text;
    }

    /** Range of the line text (excludes line ending). */
    get range() {
        return new Range(
            new Position(this._lineNumber, 0),
            new Position(this._lineNumber, this._text.length),
        );
    }

    /** Range including the line ending. */
    get rangeIncludingLineBreak() {
        if (this._lineBreak) {
            // Line break leads to start of next line
            return new Range(
                new Position(this._lineNumber, 0),
                new Position(this._lineNumber + 1, 0),
            );
        }
        // Last line — same as range
        return this.range;
    }

    /** Index of the first non-whitespace character, or length of text if all whitespace. */
    get firstNonWhitespaceCharacterIndex() {
        const match = this._text.search(/\S/);
        return match >= 0 ? match : this._text.length;
    }

    /** Whether the line is empty or contains only whitespace. */
    get isEmptyOrWhitespace() {
        return this.firstNonWhitespaceCharacterIndex === this._text.length;
    }
}

// ---------------------------------------------------------------------------
// TextDocument
// ---------------------------------------------------------------------------

/** Default word pattern — matches VS Code's definition. */
const DEFAULT_WORD_PATTERN =
    /(-?\d*\.\d\w*)|([^\`\~\!\@\#\%\^\&\*\(\)\-\=\+\[\{\]\}\\\|\;\:\'\"\,\.\<\>\/\?\s]+)/g;

class TextDocument {
    /**
     * @param {Uri}    uri
     * @param {string} languageId
     * @param {number} version
     * @param {string} content
     */
    constructor(uri, languageId, version, content) {
        this._uri = uri instanceof Uri ? uri : Uri.parse(uri);
        this._languageId = languageId;
        this._version = version;
        this._content = content;
        this._isDirty = false;
        this._isClosed = false;

        /** @type {TextLine[]|null} Lazily computed lines cache. */
        this._lines = null;
    }

    // -- Properties ----------------------------------------------------------

    get uri() { return this._uri; }
    get fileName() { return this._uri.fsPath; }
    get languageId() { return this._languageId; }
    get version() { return this._version; }
    get isDirty() { return this._isDirty; }
    get isClosed() { return this._isClosed; }

    get eol() {
        return this._content.indexOf('\r\n') >= 0 ? EndOfLine.CRLF : EndOfLine.LF;
    }

    get lineCount() {
        return this._getLines().length;
    }

    // -- Internal line cache -------------------------------------------------

    /** @returns {TextLine[]} */
    _getLines() {
        if (!this._lines) {
            this._lines = [];
            const raw = this._content;
            let lineStart = 0;
            for (let i = 0; i < raw.length; i++) {
                const ch = raw[i];
                if (ch === '\r' && raw[i + 1] === '\n') {
                    this._lines.push(new TextLine(this._lines.length, raw.substring(lineStart, i), '\r\n'));
                    i++; // skip \n
                    lineStart = i + 1;
                } else if (ch === '\n') {
                    this._lines.push(new TextLine(this._lines.length, raw.substring(lineStart, i), '\n'));
                    lineStart = i + 1;
                }
            }
            // Last line (or the only line if no newline)
            this._lines.push(new TextLine(this._lines.length, raw.substring(lineStart), ''));
        }
        return this._lines;
    }

    /** Invalidate the lazy line cache (called after content changes). */
    _invalidateLines() {
        this._lines = null;
    }

    // -- Methods -------------------------------------------------------------

    /**
     * Get the text of the document. If a range is given, returns only the text
     * inside that range.
     * @param {Range} [range]
     * @returns {string}
     */
    getText(range) {
        if (!range) return this._content;
        const start = this.offsetAt(range.start);
        const end = this.offsetAt(range.end);
        return this._content.substring(start, end);
    }

    /**
     * Returns the TextLine at the given line number or Position.
     * @param {number|Position} lineOrPosition
     * @returns {TextLine}
     */
    lineAt(lineOrPosition) {
        const lineNum = typeof lineOrPosition === 'number'
            ? lineOrPosition
            : lineOrPosition.line;
        const lines = this._getLines();
        if (lineNum < 0 || lineNum >= lines.length) {
            throw new Error('Illegal line number: ' + lineNum + ', max: ' + (lines.length - 1));
        }
        return lines[lineNum];
    }

    /**
     * Converts a zero-based offset to a Position.
     * @param {number} offset
     * @returns {Position}
     */
    positionAt(offset) {
        offset = Math.max(0, Math.min(offset, this._content.length));
        const lines = this._getLines();
        let remaining = offset;
        for (let i = 0; i < lines.length; i++) {
            const lineLen = lines[i].text.length + lines[i]._lineBreak.length;
            if (remaining <= lines[i].text.length || i === lines.length - 1) {
                return new Position(i, Math.min(remaining, lines[i].text.length));
            }
            remaining -= lineLen;
        }
        // Fallback — end of document
        const last = lines.length - 1;
        return new Position(last, lines[last].text.length);
    }

    /**
     * Converts a Position to a zero-based offset.
     * @param {Position} position
     * @returns {number}
     */
    offsetAt(position) {
        const lines = this._getLines();
        const line = Math.max(0, Math.min(position.line, lines.length - 1));
        let offset = 0;
        for (let i = 0; i < line; i++) {
            offset += lines[i].text.length + lines[i]._lineBreak.length;
        }
        offset += Math.max(0, Math.min(position.character, lines[line].text.length));
        return offset;
    }

    /**
     * Get the word range at the given position.
     * @param {Position} position
     * @param {RegExp}   [regex]
     * @returns {Range|undefined}
     */
    getWordRangeAtPosition(position, regex) {
        const pat = regex || DEFAULT_WORD_PATTERN;
        // Ensure global flag
        const flags = pat.flags.includes('g') ? pat.flags : pat.flags + 'g';
        const re = new RegExp(pat.source, flags);

        const lines = this._getLines();
        if (position.line < 0 || position.line >= lines.length) return undefined;

        const lineText = lines[position.line].text;
        let match;
        while ((match = re.exec(lineText)) !== null) {
            const start = match.index;
            const end = start + match[0].length;
            if (start <= position.character && position.character <= end) {
                return new Range(
                    new Position(position.line, start),
                    new Position(position.line, end),
                );
            }
            // Safety: avoid infinite loop on zero-length match
            if (match[0].length === 0) break;
        }
        return undefined;
    }

    /**
     * Clamp a Range to the document bounds.
     * @param {Range} range
     * @returns {Range}
     */
    validateRange(range) {
        const start = this.validatePosition(range.start);
        const end = this.validatePosition(range.end);
        return new Range(start, end);
    }

    /**
     * Clamp a Position to the document bounds.
     * @param {Position} position
     * @returns {Position}
     */
    validatePosition(position) {
        const lines = this._getLines();
        let line = Math.max(0, Math.min(position.line, lines.length - 1));
        let character = Math.max(0, Math.min(position.character, lines[line].text.length));
        return new Position(line, character);
    }
}

// ---------------------------------------------------------------------------
// DocumentManager
// ---------------------------------------------------------------------------

class DocumentManager {
    constructor() {
        /** @type {Map<string, TextDocument>} keyed by uri.toString() */
        this._documents = new Map();

        this._onDidOpen = new EventEmitter();
        this._onDidChange = new EventEmitter();
        this._onDidClose = new EventEmitter();
        this._onDidSave = new EventEmitter();

        this.onDidOpen = this._onDidOpen.event;
        this.onDidChange = this._onDidChange.event;
        this.onDidClose = this._onDidClose.event;
        this.onDidSave = this._onDidSave.event;
    }

    /**
     * @param {Uri|string} uri
     * @returns {string}
     */
    _key(uri) {
        if (typeof uri === 'string') return uri;
        if (uri && typeof uri.toString === 'function') return uri.toString();
        return String(uri);
    }

    /**
     * Open a document — creates a TextDocument and fires onDidOpen.
     * @param {Uri|string} uri
     * @param {string} languageId
     * @param {number} version
     * @param {string} text
     * @returns {TextDocument}
     */
    openDocument(uri, languageId, version, text) {
        const parsedUri = uri instanceof Uri ? uri : Uri.parse(uri);
        const doc = new TextDocument(parsedUri, languageId, version, text);
        this._documents.set(this._key(parsedUri), doc);
        this._onDidOpen.fire(doc);
        return doc;
    }

    /**
     * Update an already-open document's content and version.
     * @param {Uri|string} uri
     * @param {number} version
     * @param {string} text
     */
    changeDocument(uri, version, text) {
        const key = this._key(uri);
        const doc = this._documents.get(key);
        if (!doc) return;

        const fullRange = new Range(
            new Position(0, 0),
            new Position(doc.lineCount - 1, doc.lineAt(doc.lineCount - 1).text.length),
        );

        doc._content = text;
        doc._version = version;
        doc._isDirty = true;
        doc._invalidateLines();

        this._onDidChange.fire({
            document: doc,
            contentChanges: [{ range: fullRange, text }],
        });
    }

    /**
     * Close a document.
     * @param {Uri|string} uri
     */
    closeDocument(uri) {
        const key = this._key(uri);
        const doc = this._documents.get(key);
        if (!doc) return;
        doc._isClosed = true;
        this._documents.delete(key);
        this._onDidClose.fire(doc);
    }

    /**
     * Save a document. Optionally update content.
     * @param {Uri|string} uri
     * @param {string} [text]
     */
    saveDocument(uri, text) {
        const key = this._key(uri);
        const doc = this._documents.get(key);
        if (!doc) return;
        if (text !== undefined) {
            doc._content = text;
            doc._invalidateLines();
        }
        doc._isDirty = false;
        this._onDidSave.fire(doc);
    }

    /**
     * Get a document by URI.
     * @param {Uri|string} uri
     * @returns {TextDocument|undefined}
     */
    getDocument(uri) {
        return this._documents.get(this._key(uri));
    }

    /**
     * All currently open (not closed) documents.
     * @returns {TextDocument[]}
     */
    get allDocuments() {
        return Array.from(this._documents.values()).filter(d => !d._isClosed);
    }

    dispose() {
        this._onDidOpen.dispose();
        this._onDidChange.dispose();
        this._onDidClose.dispose();
        this._onDidSave.dispose();
        this._documents.clear();
    }
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

module.exports = {
    EndOfLine,
    Position,
    Range,
    Uri,
    TextLine,
    TextDocument,
    DocumentManager,
};
