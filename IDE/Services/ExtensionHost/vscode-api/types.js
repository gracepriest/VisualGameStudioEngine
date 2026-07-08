'use strict';

// VS Code API type implementations for the Node.js extension host.
// All types serialize to plain JSON matching LSP shapes so that
// vscode-languageclient works out of the box.
//
// CommonJS module, targets Node.js 16+.

// ────────────────────────────────────────────────────────────────
// Enums (frozen objects with numeric values)
// ────────────────────────────────────────────────────────────────

const DiagnosticSeverity = Object.freeze({
    Error: 0,
    Warning: 1,
    Information: 2,
    Hint: 3,
});

const DiagnosticTag = Object.freeze({
    Unnecessary: 1,
    Deprecated: 2,
});

const CompletionItemKind = Object.freeze({
    Text: 0,
    Method: 1,
    Function: 2,
    Constructor: 3,
    Field: 4,
    Variable: 5,
    Class: 6,
    Interface: 7,
    Module: 8,
    Property: 9,
    Unit: 10,
    Value: 11,
    Enum: 12,
    Keyword: 13,
    Snippet: 14,
    Color: 15,
    File: 16,
    Reference: 17,
    Folder: 18,
    EnumMember: 19,
    Constant: 20,
    Struct: 21,
    Event: 22,
    Operator: 23,
    TypeParameter: 24,
});

const CompletionItemTag = Object.freeze({
    Deprecated: 1,
});

const CompletionTriggerKind = Object.freeze({
    Invoke: 0,
    TriggerCharacter: 1,
    TriggerForIncompleteCompletions: 2,
});

const SymbolKind = Object.freeze({
    File: 0,
    Module: 1,
    Namespace: 2,
    Package: 3,
    Class: 4,
    Method: 5,
    Property: 6,
    Field: 7,
    Constructor: 8,
    Enum: 9,
    Interface: 10,
    Function: 11,
    Variable: 12,
    Constant: 13,
    String: 14,
    Number: 15,
    Boolean: 16,
    Array: 17,
    Object: 18,
    Key: 19,
    Null: 20,
    EnumMember: 21,
    Struct: 22,
    Event: 23,
    Operator: 24,
    TypeParameter: 25,
});

const SymbolTag = Object.freeze({
    Deprecated: 1,
});

const IndentAction = Object.freeze({
    None: 0,
    Indent: 1,
    IndentOutdent: 2,
    Outdent: 3,
});

const FoldingRangeKind = Object.freeze({
    Comment: 1,
    Imports: 2,
    Region: 3,
});

const InlayHintKind = Object.freeze({
    Type: 1,
    Parameter: 2,
});

const StatusBarAlignment = Object.freeze({
    Left: 1,
    Right: 2,
});

const ViewColumn = Object.freeze({
    Active: -1,
    Beside: -2,
    One: 1,
    Two: 2,
    Three: 3,
    Four: 4,
    Five: 5,
    Six: 6,
    Seven: 7,
    Eight: 8,
    Nine: 9,
});

const TextEditorRevealType = Object.freeze({
    Default: 0,
    InCenter: 1,
    InCenterIfOutsideViewport: 2,
    AtTop: 3,
});

const EndOfLine = Object.freeze({
    LF: 1,
    CRLF: 2,
});

const TreeItemCollapsibleState = Object.freeze({
    None: 0,
    Collapsed: 1,
    Expanded: 2,
});

const FileType = Object.freeze({
    Unknown: 0,
    File: 1,
    Directory: 2,
    SymbolicLink: 64,
});

const TextDocumentSaveReason = Object.freeze({
    Manual: 1,
    AfterDelay: 2,
    FocusOut: 3,
});

const ConfigurationTarget = Object.freeze({
    Global: 1,
    Workspace: 2,
    WorkspaceFolder: 3,
});

const ProgressLocation = Object.freeze({
    SourceControl: 1,
    Window: 10,
    Notification: 15,
});

const DecorationRangeBehavior = Object.freeze({
    OpenOpen: 0,
    ClosedClosed: 1,
    OpenClosed: 2,
    ClosedOpen: 3,
});

const OverviewRulerLane = Object.freeze({
    Left: 1,
    Center: 2,
    Right: 4,
    Full: 7,
});

const DocumentHighlightKind = Object.freeze({
    Text: 0,
    Read: 1,
    Write: 2,
});

const SignatureHelpTriggerKind = Object.freeze({
    Invoke: 1,
    TriggerCharacter: 2,
    ContentChange: 3,
});

const CodeActionTriggerKind = Object.freeze({
    Invoke: 1,
    Automatic: 2,
});

const TaskRevealKind = Object.freeze({
    Always: 1,
    Silent: 2,
    Never: 3,
});

const TaskPanelKind = Object.freeze({
    Shared: 1,
    Dedicated: 2,
    New: 3,
});

const TaskScope = Object.freeze({
    Global: 1,
    Workspace: 2,
});

const ExtensionKind = Object.freeze({
    UI: 1,
    Workspace: 2,
});

// ────────────────────────────────────────────────────────────────
// Uri — minimal implementation matching VS Code's Uri shape
// ────────────────────────────────────────────────────────────────

class Uri {
    /**
     * @param {string} scheme
     * @param {string} authority
     * @param {string} path
     * @param {string} query
     * @param {string} fragment
     */
    constructor(scheme, authority, path, query, fragment) {
        this.scheme = scheme || '';
        this.authority = authority || '';
        this.path = path || '';
        this.query = query || '';
        this.fragment = fragment || '';
    }

    get fsPath() {
        if (this.scheme === 'file') {
            let p = this.path;
            const isWindows = process.platform === 'win32';
            if (isWindows) {
                // Handle Windows paths: /C:/foo -> C:\foo
                if (p.length >= 3 && p[0] === '/' && p[2] === ':') {
                    p = p.substring(1);
                }
                return p.replace(/\//g, '\\');
            }
            return p;
        }
        return this.path;
    }

    toString() {
        let result = '';
        if (this.scheme) result += this.scheme + '://';
        if (this.authority) result += this.authority;
        result += this.path;
        if (this.query) result += '?' + this.query;
        if (this.fragment) result += '#' + this.fragment;
        return result;
    }

    toJSON() { return this.toString(); }

    with(change) {
        return new Uri(
            change.scheme !== undefined ? change.scheme : this.scheme,
            change.authority !== undefined ? change.authority : this.authority,
            change.path !== undefined ? change.path : this.path,
            change.query !== undefined ? change.query : this.query,
            change.fragment !== undefined ? change.fragment : this.fragment
        );
    }

    /**
     * @param {string} str
     * @returns {Uri}
     */
    static parse(str) {
        const m = /^([a-zA-Z][a-zA-Z0-9+.-]*):\/\/([^/?#]*)([^?#]*)(\?[^#]*)?(#.*)?$/.exec(str);
        if (m) {
            return new Uri(
                m[1],
                m[2],
                m[3],
                (m[4] || '').replace(/^\?/, ''),
                (m[5] || '').replace(/^#/, '')
            );
        }
        // Fallback: treat as file path.
        return Uri.file(str);
    }

    /**
     * @param {string} fsPath
     * @returns {Uri}
     */
    static file(fsPath) {
        let p = fsPath.replace(/\\/g, '/');
        if (p[0] !== '/') p = '/' + p;
        return new Uri('file', '', p, '', '');
    }

    static from(components) {
        return new Uri(
            components.scheme,
            components.authority,
            components.path,
            components.query,
            components.fragment
        );
    }

    static joinPath(base, ...pathSegments) {
        let p = base.path;
        for (const seg of pathSegments) {
            if (p.endsWith('/')) {
                p += seg;
            } else {
                p += '/' + seg;
            }
        }
        return base.with({ path: p });
    }
}

// ────────────────────────────────────────────────────────────────
// Core types
// ────────────────────────────────────────────────────────────────

class Position {
    /**
     * @param {number} line   Zero-based line number.
     * @param {number} character  Zero-based character offset.
     */
    constructor(line, character) {
        this.line = line;
        this.character = character;
    }

    /**
     * Create a new position relative to this one.
     * @param {number|object} [lineDelta=0]
     * @param {number} [characterDelta=0]
     * @returns {Position}
     */
    translate(lineDelta, characterDelta) {
        if (typeof lineDelta === 'object') {
            characterDelta = lineDelta.characterDelta || 0;
            lineDelta = lineDelta.lineDelta || 0;
        }
        return new Position(
            this.line + (lineDelta || 0),
            this.character + (characterDelta || 0)
        );
    }

    /**
     * Create a copy with optional overrides.
     * @param {number|object} [line]
     * @param {number} [character]
     * @returns {Position}
     */
    with(line, character) {
        if (typeof line === 'object') {
            character = line.character !== undefined ? line.character : this.character;
            line = line.line !== undefined ? line.line : this.line;
        }
        return new Position(
            line !== undefined ? line : this.line,
            character !== undefined ? character : this.character
        );
    }

    /** @param {Position} other @returns {number} */
    compareTo(other) {
        if (this.line !== other.line) {
            return this.line - other.line;
        }
        return this.character - other.character;
    }

    /** @param {Position} other @returns {boolean} */
    isEqual(other) { return this.line === other.line && this.character === other.character; }

    /** @param {Position} other @returns {boolean} */
    isBefore(other) { return this.compareTo(other) < 0; }

    /** @param {Position} other @returns {boolean} */
    isAfter(other) { return this.compareTo(other) > 0; }

    /** @param {Position} other @returns {boolean} */
    isBeforeOrEqual(other) { return this.compareTo(other) <= 0; }

    /** @param {Position} other @returns {boolean} */
    isAfterOrEqual(other) { return this.compareTo(other) >= 0; }

    toJSON() { return { line: this.line, character: this.character }; }
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

    get isEmpty() { return this.start.isEqual(this.end); }
    get isSingleLine() { return this.start.line === this.end.line; }

    /**
     * @param {Position|Range} positionOrRange
     * @returns {boolean}
     */
    contains(positionOrRange) {
        if (positionOrRange instanceof Range) {
            return this.contains(positionOrRange.start) && this.contains(positionOrRange.end);
        }
        return positionOrRange.isAfterOrEqual(this.start) && positionOrRange.isBeforeOrEqual(this.end);
    }

    /** @param {Range} other @returns {Range|undefined} */
    intersection(other) {
        const start = this.start.isAfter(other.start) ? this.start : other.start;
        const end = this.end.isBefore(other.end) ? this.end : other.end;
        if (start.isAfter(end)) return undefined;
        return new Range(start, end);
    }

    /** @param {Range} other @returns {Range} */
    union(other) {
        const start = this.start.isBefore(other.start) ? this.start : other.start;
        const end = this.end.isAfter(other.end) ? this.end : other.end;
        return new Range(start, end);
    }

    /**
     * @param {Position|object} [start]
     * @param {Position} [end]
     * @returns {Range}
     */
    with(start, end) {
        if (typeof start === 'object' && !(start instanceof Position)) {
            end = start.end !== undefined ? start.end : this.end;
            start = start.start !== undefined ? start.start : this.start;
        }
        return new Range(
            start !== undefined ? start : this.start,
            end !== undefined ? end : this.end
        );
    }

    /** @param {Range} other @returns {boolean} */
    isEqual(other) {
        return this.start.isEqual(other.start) && this.end.isEqual(other.end);
    }

    toJSON() { return { start: this.start.toJSON(), end: this.end.toJSON() }; }
}

class Selection extends Range {
    /**
     * @param {Position|number} anchorOrAnchorLine
     * @param {Position|number} activeOrAnchorChar
     * @param {number} [activeLine]
     * @param {number} [activeChar]
     */
    constructor(anchorOrAnchorLine, activeOrAnchorChar, activeLine, activeChar) {
        if (typeof anchorOrAnchorLine === 'number') {
            const anchor = new Position(anchorOrAnchorLine, activeOrAnchorChar);
            const active = new Position(activeLine, activeChar);
            super(
                anchor.isBefore(active) ? anchor : active,
                anchor.isBefore(active) ? active : anchor
            );
            this.anchor = anchor;
            this.active = active;
        } else {
            const anchor = anchorOrAnchorLine;
            const active = activeOrAnchorChar;
            super(
                anchor.isBefore(active) ? anchor : active,
                anchor.isBefore(active) ? active : anchor
            );
            this.anchor = anchor;
            this.active = active;
        }
    }

    get isReversed() { return this.anchor.isAfter(this.active); }
}

class Location {
    /**
     * @param {Uri} uri
     * @param {Range|Position} rangeOrPosition
     */
    constructor(uri, rangeOrPosition) {
        this.uri = uri;
        this.range = rangeOrPosition instanceof Position
            ? new Range(rangeOrPosition, rangeOrPosition)
            : rangeOrPosition;
    }

    toJSON() {
        return { uri: this.uri.toString(), range: this.range.toJSON() };
    }
}

/**
 * LocationLink -- used by definition providers.
 */
class LocationLink {
    /**
     * @param {Uri} targetUri
     * @param {Range} targetRange
     * @param {Range} [targetSelectionRange]
     * @param {Range} [originSelectionRange]
     */
    constructor(targetUri, targetRange, targetSelectionRange, originSelectionRange) {
        this.originSelectionRange = originSelectionRange;
        this.targetUri = targetUri;
        this.targetRange = targetRange;
        this.targetSelectionRange = targetSelectionRange || targetRange;
    }
}

// ────────────────────────────────────────────────────────────────
// Text types
// ────────────────────────────────────────────────────────────────

class TextEdit {
    /**
     * @param {Range} range
     * @param {string} newText
     */
    constructor(range, newText) {
        this.range = range;
        this.newText = newText;
        this.newEol = undefined;
    }

    static replace(range, newText) { return new TextEdit(range, newText); }
    static insert(position, newText) { return new TextEdit(new Range(position, position), newText); }
    static delete(range) { return new TextEdit(range, ''); }
    static setEndOfLine(eol) {
        const e = new TextEdit(new Range(0, 0, 0, 0), '');
        e.newEol = eol;
        return e;
    }

    toJSON() {
        const obj = { range: this.range.toJSON(), newText: this.newText };
        if (this.newEol !== undefined) obj.newEol = this.newEol;
        return obj;
    }
}

class SnippetTextEdit {
    /**
     * @param {Range} range
     * @param {SnippetString} snippet
     */
    constructor(range, snippet) {
        this.range = range;
        this.snippet = snippet;
    }

    static replace(range, snippet) { return new SnippetTextEdit(range, snippet); }
    static insert(position, snippet) {
        return new SnippetTextEdit(new Range(position, position), snippet);
    }
}

class WorkspaceEdit {
    constructor() {
        /** @type {Map<string, TextEdit[]>} */
        this._edits = new Map();
        /** @type {Array<{type: string, args: any[]}>} */
        this._fileOps = [];
    }

    /**
     * @param {Uri} uri
     * @returns {boolean}
     */
    has(uri) { return this._edits.has(uri.toString()); }

    /**
     * @param {Uri} uri
     * @param {TextEdit[]} edits
     */
    set(uri, edits) { this._edits.set(uri.toString(), edits); }

    /**
     * @param {Uri} uri
     * @returns {TextEdit[]}
     */
    get(uri) { return this._edits.get(uri.toString()) || []; }

    /**
     * @returns {Array<[Uri, TextEdit[]]>}
     */
    entries() {
        const result = [];
        for (const [key, value] of this._edits) {
            result.push([Uri.parse(key), value]);
        }
        return result;
    }

    get size() { return this._edits.size; }

    createFile(uri, options) {
        this._fileOps.push({ type: 'create', uri, options });
    }

    deleteFile(uri, options) {
        this._fileOps.push({ type: 'delete', uri, options });
    }

    renameFile(oldUri, newUri, options) {
        this._fileOps.push({ type: 'rename', oldUri, newUri, options });
    }
}

class SnippetString {
    /** @param {string} [value] */
    constructor(value) {
        this.value = value || '';
    }

    /** @param {string} str @returns {SnippetString} */
    appendText(str) {
        // Escape snippet-special characters.
        this.value += str.replace(/\$|}|\\/g, '\\$&');
        return this;
    }

    /** @param {number} [num] @returns {SnippetString} */
    appendTabstop(num) {
        this.value += num != null ? ('$' + num) : '$0';
        return this;
    }

    /**
     * @param {string|Function} value
     * @param {number} [num]
     * @returns {SnippetString}
     */
    appendPlaceholder(value, num) {
        if (typeof value === 'function') {
            const nested = new SnippetString();
            value(nested);
            this.value += '${' + (num != null ? num : '') + ':' + nested.value + '}';
        } else {
            this.value += '${' + (num != null ? num : '') + ':' + value + '}';
        }
        return this;
    }

    /**
     * @param {string[]} values
     * @param {number} [num]
     * @returns {SnippetString}
     */
    appendChoice(values, num) {
        this.value += '${' + (num != null ? num : '') + '|' + values.join(',') + '|}';
        return this;
    }

    /**
     * @param {string} name
     * @param {string|Function} [defaultValue]
     * @returns {SnippetString}
     */
    appendVariable(name, defaultValue) {
        if (defaultValue !== undefined) {
            if (typeof defaultValue === 'function') {
                const nested = new SnippetString();
                defaultValue(nested);
                this.value += '${' + name + ':' + nested.value + '}';
            } else {
                this.value += '${' + name + ':' + defaultValue + '}';
            }
        } else {
            this.value += '${' + name + '}';
        }
        return this;
    }
}

// ────────────────────────────────────────────────────────────────
// Language types
// ────────────────────────────────────────────────────────────────

class Diagnostic {
    /**
     * @param {Range} range
     * @param {string} message
     * @param {number} [severity]
     */
    constructor(range, message, severity) {
        this.range = range;
        this.message = message;
        this.severity = severity !== undefined ? severity : DiagnosticSeverity.Error;
        this.source = undefined;
        this.code = undefined;
        this.relatedInformation = undefined;
        this.tags = undefined;
    }
}

class DiagnosticRelatedInformation {
    /**
     * @param {Location} location
     * @param {string} message
     */
    constructor(location, message) {
        this.location = location;
        this.message = message;
    }
}

class CompletionItem {
    /**
     * @param {string} label
     * @param {number} [kind]
     */
    constructor(label, kind) {
        this.label = label;
        this.kind = kind;
        this.detail = undefined;
        this.documentation = undefined;
        this.sortText = undefined;
        this.filterText = undefined;
        this.insertText = undefined;
        this.range = undefined;
        this.command = undefined;
        this.additionalTextEdits = undefined;
        this.commitCharacters = undefined;
        this.preselect = undefined;
        this.keepWhitespace = undefined;
        this.tags = undefined;
        this.textEdit = undefined;
    }
}

class CompletionList {
    /**
     * @param {CompletionItem[]} [items]
     * @param {boolean} [isIncomplete]
     */
    constructor(items, isIncomplete) {
        this.items = items || [];
        this.isIncomplete = isIncomplete || false;
    }
}

class MarkdownString {
    /**
     * @param {string} [value]
     * @param {boolean} [supportThemeIcons]
     */
    constructor(value, supportThemeIcons) {
        this.value = value || '';
        this.isTrusted = false;
        this.supportThemeIcons = supportThemeIcons || false;
        this.supportHtml = false;
        this.baseUri = undefined;
    }

    /** @param {string} value @returns {MarkdownString} */
    appendText(value) {
        // Escape markdown specials.
        this.value += value
            .replace(/[\\`*_{}[\]()#+\-.!]/g, '\\$&')
            .replace(/\n/g, '\n\n');
        return this;
    }

    /** @param {string} value @returns {MarkdownString} */
    appendMarkdown(value) {
        this.value += value;
        return this;
    }

    /**
     * @param {string} code
     * @param {string} [language]
     * @returns {MarkdownString}
     */
    appendCodeblock(code, language) {
        this.value += '\n```' + (language || '') + '\n' + code + '\n```\n';
        return this;
    }
}

class Hover {
    /**
     * @param {MarkdownString|string|Array<MarkdownString|string>} contents
     * @param {Range} [range]
     */
    constructor(contents, range) {
        this.contents = Array.isArray(contents) ? contents : [contents];
        this.range = range;
    }
}

class SignatureHelp {
    constructor() {
        this.signatures = [];
        this.activeSignature = 0;
        this.activeParameter = 0;
    }
}

class SignatureInformation {
    /**
     * @param {string} label
     * @param {string|MarkdownString} [documentation]
     */
    constructor(label, documentation) {
        this.label = label;
        this.documentation = documentation;
        this.parameters = [];
        this.activeParameter = undefined;
    }
}

class ParameterInformation {
    /**
     * @param {string|[number,number]} label
     * @param {string|MarkdownString} [documentation]
     */
    constructor(label, documentation) {
        this.label = label;
        this.documentation = documentation;
    }
}

class CodeAction {
    /**
     * @param {string} title
     * @param {CodeActionKind} [kind]
     */
    constructor(title, kind) {
        this.title = title;
        this.kind = kind;
        this.diagnostics = undefined;
        this.edit = undefined;
        this.command = undefined;
        this.isPreferred = undefined;
        this.disabled = undefined;
    }
}

class CodeActionKind {
    /** @param {string} value */
    constructor(value) {
        this.value = value;
    }

    /** @param {string} parts @returns {CodeActionKind} */
    append(parts) {
        return new CodeActionKind(this.value ? this.value + '.' + parts : parts);
    }

    /** @param {CodeActionKind} other @returns {boolean} */
    intersects(other) {
        return this.value === other.value
            || other.value.startsWith(this.value + '.')
            || this.value.startsWith(other.value + '.');
    }

    /** @param {CodeActionKind} other @returns {boolean} */
    contains(other) {
        return this.value === other.value || other.value.startsWith(this.value + '.');
    }
}

CodeActionKind.Empty = new CodeActionKind('');
CodeActionKind.QuickFix = new CodeActionKind('quickfix');
CodeActionKind.Refactor = new CodeActionKind('refactor');
CodeActionKind.RefactorExtract = new CodeActionKind('refactor.extract');
CodeActionKind.RefactorInline = new CodeActionKind('refactor.inline');
CodeActionKind.RefactorMove = new CodeActionKind('refactor.move');
CodeActionKind.RefactorRewrite = new CodeActionKind('refactor.rewrite');
CodeActionKind.Source = new CodeActionKind('source');
CodeActionKind.SourceOrganizeImports = new CodeActionKind('source.organizeImports');
CodeActionKind.SourceFixAll = new CodeActionKind('source.fixAll');

class CodeLens {
    /**
     * @param {Range} range
     * @param {object} [command]
     */
    constructor(range, command) {
        this.range = range;
        this.command = command;
    }

    get isResolved() { return this.command !== undefined; }
}

class DocumentLink {
    /**
     * @param {Range} range
     * @param {Uri} [target]
     */
    constructor(range, target) {
        this.range = range;
        this.target = target;
        this.tooltip = undefined;
    }
}

class Color {
    /**
     * @param {number} red   0..1
     * @param {number} green 0..1
     * @param {number} blue  0..1
     * @param {number} alpha 0..1
     */
    constructor(red, green, blue, alpha) {
        this.red = red;
        this.green = green;
        this.blue = blue;
        this.alpha = alpha;
    }
}

class ColorInformation {
    /**
     * @param {Range} range
     * @param {Color} color
     */
    constructor(range, color) {
        this.range = range;
        this.color = color;
    }
}

class ColorPresentation {
    /** @param {string} label */
    constructor(label) {
        this.label = label;
        this.textEdit = undefined;
        this.additionalTextEdits = undefined;
    }
}

class FoldingRange {
    /**
     * @param {number} start  Zero-based start line.
     * @param {number} end    Zero-based end line.
     * @param {number} [kind]
     */
    constructor(start, end, kind) {
        this.start = start;
        this.end = end;
        this.kind = kind;
    }
}

class SelectionRange {
    /**
     * @param {Range} range
     * @param {SelectionRange} [parent]
     */
    constructor(range, parent) {
        this.range = range;
        this.parent = parent;
    }
}

class InlayHint {
    /**
     * @param {Position} position
     * @param {string|Array} label
     * @param {number} [kind]
     */
    constructor(position, label, kind) {
        this.position = position;
        this.label = label;
        this.kind = kind;
        this.tooltip = undefined;
        this.textEdits = undefined;
        this.paddingLeft = undefined;
        this.paddingRight = undefined;
    }
}

class SymbolInformation {
    /**
     * @param {string} name
     * @param {number} kind
     * @param {string|Range} rangeOrContainerName
     * @param {Location|Uri} [locationOrUri]
     */
    constructor(name, kind, rangeOrContainerName, locationOrUri) {
        this.name = name;
        this.kind = kind;
        this.tags = undefined;

        if (typeof rangeOrContainerName === 'string') {
            // (name, kind, containerName, location)
            this.containerName = rangeOrContainerName;
            this.location = locationOrUri instanceof Location
                ? locationOrUri
                : new Location(locationOrUri, new Range(0, 0, 0, 0));
        } else {
            // (name, kind, range, uri) -- deprecated overload
            this.containerName = '';
            this.location = locationOrUri
                ? new Location(locationOrUri, rangeOrContainerName)
                : new Location(undefined, rangeOrContainerName);
        }
    }
}

class DocumentSymbol {
    /**
     * @param {string} name
     * @param {string} detail
     * @param {number} kind
     * @param {Range} range
     * @param {Range} selectionRange
     */
    constructor(name, detail, kind, range, selectionRange) {
        this.name = name;
        this.detail = detail;
        this.kind = kind;
        this.range = range;
        this.selectionRange = selectionRange;
        this.children = [];
        this.tags = undefined;
    }
}

// ────────────────────────────────────────────────────────────────
// UI types
// ────────────────────────────────────────────────────────────────

class ThemeColor {
    /** @param {string} id */
    constructor(id) {
        this.id = id;
    }
}

class ThemeIcon {
    /**
     * @param {string} id
     * @param {ThemeColor} [color]
     */
    constructor(id, color) {
        this.id = id;
        this.color = color;
    }
}

ThemeIcon.File = new ThemeIcon('file');
ThemeIcon.Folder = new ThemeIcon('folder');

class TreeItem {
    /**
     * @param {string|Uri} labelOrResourceUri
     * @param {number} [collapsibleState]
     */
    constructor(labelOrResourceUri, collapsibleState) {
        if (typeof labelOrResourceUri === 'string') {
            this.label = labelOrResourceUri;
            this.resourceUri = undefined;
        } else {
            this.label = undefined;
            this.resourceUri = labelOrResourceUri;
        }
        this.collapsibleState = collapsibleState !== undefined
            ? collapsibleState
            : TreeItemCollapsibleState.None;
        this.id = undefined;
        this.iconPath = undefined;
        this.description = undefined;
        this.tooltip = undefined;
        this.command = undefined;
        this.contextValue = undefined;
        this.checkboxState = undefined;
    }
}

// ────────────────────────────────────────────────────────────────
// Semantic tokens
// ────────────────────────────────────────────────────────────────

class SemanticTokensLegend {
    /**
     * @param {string[]} tokenTypes
     * @param {string[]} [tokenModifiers]
     */
    constructor(tokenTypes, tokenModifiers) {
        this.tokenTypes = tokenTypes;
        this.tokenModifiers = tokenModifiers || [];
    }
}

class SemanticTokens {
    /**
     * @param {Uint32Array} data
     * @param {string} [resultId]
     */
    constructor(data, resultId) {
        this.data = data;
        this.resultId = resultId;
    }
}

class SemanticTokensBuilder {
    /** @param {SemanticTokensLegend} [legend] */
    constructor(legend) {
        this._legend = legend;
        /** @type {Array<{line:number, char:number, length:number, tokenType:number, tokenModifiers:number}>} */
        this._tokens = [];
    }

    /**
     * Push a token. Accepts either:
     *   push(line, char, length, tokenType, tokenModifiers?)
     *   push(range, tokenType, tokenModifiers?)
     * @param {number|Range} lineOrRange
     * @param {number} charOrTokenType
     * @param {number} [lengthOrTokenModifiers]
     * @param {number} [tokenType]
     * @param {number} [tokenModifiers]
     */
    push(lineOrRange, charOrTokenType, lengthOrTokenModifiers, tokenType, tokenModifiers) {
        if (typeof lineOrRange === 'object') {
            // (range, tokenType, tokenModifiers?) overload
            const range = lineOrRange;
            this._tokens.push({
                line: range.start.line,
                char: range.start.character,
                length: range.end.character - range.start.character,
                tokenType: charOrTokenType,
                tokenModifiers: lengthOrTokenModifiers || 0,
            });
        } else {
            this._tokens.push({
                line: lineOrRange,
                char: charOrTokenType,
                length: lengthOrTokenModifiers,
                tokenType: tokenType,
                tokenModifiers: tokenModifiers || 0,
            });
        }
    }

    /**
     * Delta-encode all tokens and return SemanticTokens.
     * @returns {SemanticTokens}
     */
    build() {
        // Sort by line then char.
        this._tokens.sort((a, b) => a.line - b.line || a.char - b.char);

        const data = new Uint32Array(this._tokens.length * 5);
        let prevLine = 0;
        let prevChar = 0;
        for (let i = 0; i < this._tokens.length; i++) {
            const t = this._tokens[i];
            const deltaLine = t.line - prevLine;
            const deltaChar = deltaLine === 0 ? t.char - prevChar : t.char;
            const offset = i * 5;
            data[offset] = deltaLine;
            data[offset + 1] = deltaChar;
            data[offset + 2] = t.length;
            data[offset + 3] = t.tokenType;
            data[offset + 4] = t.tokenModifiers;
            prevLine = t.line;
            prevChar = t.char;
        }
        return new SemanticTokens(data);
    }
}

// ────────────────────────────────────────────────────────────────
// Task types
// ────────────────────────────────────────────────────────────────

class ShellExecution {
    /**
     * @param {string} commandLineOrCommand
     * @param {string[]|object} [argsOrOptions]
     * @param {object} [options]
     */
    constructor(commandLineOrCommand, argsOrOptions, options) {
        if (Array.isArray(argsOrOptions)) {
            this.command = commandLineOrCommand;
            this.args = argsOrOptions;
            this.options = options;
        } else {
            this.command = commandLineOrCommand;
            this.args = [];
            this.options = argsOrOptions;
        }
    }
}

class ProcessExecution {
    /**
     * @param {string} process
     * @param {string[]|object} [argsOrOptions]
     * @param {object} [options]
     */
    constructor(process, argsOrOptions, options) {
        if (Array.isArray(argsOrOptions)) {
            this.process = process;
            this.args = argsOrOptions;
            this.options = options;
        } else {
            this.process = process;
            this.args = [];
            this.options = argsOrOptions;
        }
    }
}

class TaskGroup {
    /** @param {string} id @param {string} label */
    constructor(id, label) {
        this._id = id;
        this._label = label;
    }
}

TaskGroup.Clean = new TaskGroup('clean', 'Clean');
TaskGroup.Build = new TaskGroup('build', 'Build');
TaskGroup.Rebuild = new TaskGroup('rebuild', 'Rebuild');
TaskGroup.Test = new TaskGroup('test', 'Test');

class Task {
    /**
     * @param {object} definition
     * @param {number|object} scope
     * @param {string} name
     * @param {string} source
     * @param {ShellExecution|ProcessExecution} [execution]
     * @param {string|string[]} [problemMatchers]
     */
    constructor(definition, scope, name, source, execution, problemMatchers) {
        this.definition = definition;
        this.scope = scope;
        this.name = name;
        this.source = source;
        this.execution = execution;
        this.problemMatchers = problemMatchers;
        this.group = undefined;
        this.presentationOptions = undefined;
        this.isBackground = false;
        this.detail = undefined;
    }
}

// ────────────────────────────────────────────────────────────────
// Missing types needed by vscode-languageclient
// ────────────────────────────────────────────────────────────────

class CancellationError extends Error {
    constructor() {
        super('Cancelled');
        this.name = 'CancellationError';
    }
}

class CallHierarchyItem {
    constructor(kind, name, detail, uri, range, selectionRange) {
        this.kind = kind;
        this.name = name;
        this.detail = detail;
        this.uri = uri;
        this.range = range;
        this.selectionRange = selectionRange;
        this.tags = undefined;
    }
}

class TypeHierarchyItem {
    constructor(kind, name, detail, uri, range, selectionRange) {
        this.kind = kind;
        this.name = name;
        this.detail = detail;
        this.uri = uri;
        this.range = range;
        this.selectionRange = selectionRange;
        this.tags = undefined;
    }
}

class DocumentHighlight {
    constructor(range, kind) {
        this.range = range;
        this.kind = kind || DocumentHighlightKind.Text;
    }
}

class LinkedEditingRanges {
    constructor(ranges, wordPattern) {
        this.ranges = ranges;
        this.wordPattern = wordPattern;
    }
}

class EvaluatableExpression {
    constructor(range, expression) {
        this.range = range;
        this.expression = expression;
    }
}

class InlineValueText {
    constructor(range, text) { this.range = range; this.text = text; }
}

class InlineValueVariableLookup {
    constructor(range, variableName, caseSensitiveLookup) {
        this.range = range;
        this.variableName = variableName;
        this.caseSensitiveLookup = caseSensitiveLookup !== false;
    }
}

class InlineValueEvaluatableExpression {
    constructor(range, expression) { this.range = range; this.expression = expression; }
}

// ────────────────────────────────────────────────────────────────
// Exports
// ────────────────────────────────────────────────────────────────

module.exports = {
    // Enums
    DiagnosticSeverity,
    DiagnosticTag,
    CompletionItemKind,
    CompletionItemTag,
    CompletionTriggerKind,
    SymbolKind,
    SymbolTag,
    IndentAction,
    FoldingRangeKind,
    InlayHintKind,
    StatusBarAlignment,
    ViewColumn,
    TextEditorRevealType,
    EndOfLine,
    TreeItemCollapsibleState,
    FileType,
    TextDocumentSaveReason,
    ConfigurationTarget,
    ProgressLocation,
    DecorationRangeBehavior,
    OverviewRulerLane,
    DocumentHighlightKind,
    SignatureHelpTriggerKind,
    CodeActionTriggerKind,
    TaskRevealKind,
    TaskPanelKind,
    TaskScope,
    ExtensionKind,

    // Core types
    Position,
    Range,
    Selection,
    Location,
    LocationLink,
    Uri,

    // Text types
    TextEdit,
    SnippetTextEdit,
    WorkspaceEdit,
    SnippetString,

    // Language types
    Diagnostic,
    DiagnosticRelatedInformation,
    CompletionItem,
    CompletionList,
    MarkdownString,
    Hover,
    SignatureHelp,
    SignatureInformation,
    ParameterInformation,
    CodeAction,
    CodeActionKind,
    CodeLens,
    DocumentLink,
    Color,
    ColorInformation,
    ColorPresentation,
    FoldingRange,
    SelectionRange,
    InlayHint,
    SymbolInformation,
    DocumentSymbol,

    // UI types
    ThemeColor,
    ThemeIcon,
    TreeItem,

    // Semantic tokens
    SemanticTokensLegend,
    SemanticTokens,
    SemanticTokensBuilder,

    // Task types
    ShellExecution,
    ProcessExecution,
    TaskGroup,
    Task,

    // Types needed by vscode-languageclient
    CancellationError,
    CallHierarchyItem,
    TypeHierarchyItem,
    DocumentHighlight,
    LinkedEditingRanges,
    EvaluatableExpression,
    InlineValueText,
    InlineValueVariableLookup,
    InlineValueEvaluatableExpression,
};
