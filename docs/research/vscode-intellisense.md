# VS Code IntelliSense and Language Server Protocol Features

> Research compiled March 2026. Based on VS Code documentation and LSP 3.17 specification.

---

## Table of Contents

1. [Completion (IntelliSense)](#1-completion-intellisense)
2. [Signature Help / Parameter Hints](#2-signature-help--parameter-hints)
3. [Hover Information](#3-hover-information)
4. [Go to Definition / Type Definition / Implementation](#4-go-to-definition--type-definition--implementation)
5. [Find All References / Peek References](#5-find-all-references--peek-references)
6. [Rename Symbol](#6-rename-symbol)
7. [Code Actions (Quick Fix, Refactoring)](#7-code-actions-quick-fix-refactoring)
8. [Code Lens](#8-code-lens)
9. [Document Formatting / Range Formatting / On-Type Formatting](#9-document-formatting--range-formatting--on-type-formatting)
10. [Document Symbols / Outline](#10-document-symbols--outline)
11. [Workspace Symbols](#11-workspace-symbols)
12. [Inlay Hints](#12-inlay-hints)
13. [Linked Editing (Rename Tag Pairs)](#13-linked-editing-rename-tag-pairs)
14. [Selection Range (Expand/Shrink Selection)](#14-selection-range-expandshrink-selection)
15. [Call Hierarchy / Type Hierarchy](#15-call-hierarchy--type-hierarchy)
16. [Semantic Tokens](#16-semantic-tokens)

---

## 1. Completion (IntelliSense)

### Overview

VS Code IntelliSense provides intelligent code completions based on language semantics, source code analysis, and extension-provided data. It offers different types of completions: language server suggestions, snippets, and simple word-based textual completions.

### Trigger Characters

- **Manual trigger**: `Ctrl+Space` (Windows/Linux) or `Cmd+Space` (macOS).
- **Automatic triggers**: Language-specific characters that automatically invoke completions. Common examples:
  - `.` (dot) -- member access in most languages
  - `:` -- scope resolution (C++, Lua)
  - `<` -- generic type parameters, JSX tags
  - `"` or `'` -- string attribute values (HTML)
  - `/` -- file path completions
  - `@` -- decorators, annotations
- Each `CompletionItemProvider` registers its own set of `triggerCharacters`. When a trigger character is typed, only providers that registered that character are queried.
- Setting: `editor.suggestOnTriggerCharacters` (default: `true`) controls whether typing trigger characters activates suggestions.

### Filtering

- **Prefix matching**: The current prefix (text left of the cursor) is used to filter and sort suggestions.
- **CamelCase filtering**: Typing uppercase letters matches CamelCase segments. For example, typing `cra` matches `createApplication`.
- **Fuzzy matching**: VS Code uses fuzzy substring matching, not just prefix matching. Typing `fb` can match `fooBar`.
- **filterText**: Each `CompletionItem` can provide a custom `filterText` property that overrides the label for filtering purposes.
- Settings for hiding specific kinds:
  - `editor.suggest.showMethods`
  - `editor.suggest.showFunctions`
  - `editor.suggest.showConstructors`
  - `editor.suggest.showFields`
  - `editor.suggest.showVariables`
  - `editor.suggest.showClasses`
  - `editor.suggest.showStructs`
  - `editor.suggest.showInterfaces`
  - `editor.suggest.showModules`
  - `editor.suggest.showProperties`
  - `editor.suggest.showEvents`
  - `editor.suggest.showOperators`
  - `editor.suggest.showUnits`
  - `editor.suggest.showValues`
  - `editor.suggest.showConstants`
  - `editor.suggest.showEnumMembers`
  - `editor.suggest.showKeywords`
  - `editor.suggest.showWords`
  - `editor.suggest.showColors`
  - `editor.suggest.showFiles`
  - `editor.suggest.showReferences`
  - `editor.suggest.showCustomcolors`
  - `editor.suggest.showFolders`
  - `editor.suggest.showTypeParameters`
  - `editor.suggest.showSnippets`
  - `editor.suggest.showUsers`
  - `editor.suggest.showIssues`

### Sorting

The suggestion list is sorted by multiple factors:

1. **Match quality**: How well the item matches the typed prefix (exact prefix > fuzzy match).
2. **sortText**: Each `CompletionItem` can supply a `sortText` string. Items are sorted lexicographically by `sortText` when match quality is equal.
3. **Locality bonus**: `editor.suggest.localityBonus` (default: `false`). When enabled, items closer to the cursor position in the same scope are boosted.
4. **Recency**: `editor.suggestSelection` controls how previously selected items influence ordering:
   - `"first"` -- always select the first item.
   - `"recentlyUsed"` -- select the most recently chosen item if it appears in the list.
   - `"recentlyUsedByPrefix"` -- remembers which item was selected for a specific typed prefix (e.g., selecting `console` after typing `co` means `console` is pre-selected next time `co` is typed).
5. **Kind grouping**: Items can be grouped by kind (methods, variables, keywords, snippets) depending on settings.

### Preselect

- A `CompletionItem` can set `preselect: true` to indicate it should be selected by default when the completion list opens.
- Only one item should be preselected. If multiple items have `preselect: true`, the editor picks the first one.
- Useful for suggesting the most likely completion (e.g., the inferred type in a type annotation context).

### Commit Characters

- Each `CompletionItem` can define `commitCharacters` -- an array of characters that, when typed while the item is selected, accept the completion and then insert the typed character.
- Example: In JavaScript, `;` and `.` are common commit characters. Typing `.` after selecting `document` accepts the completion and immediately triggers member completions.
- Global setting: `editor.acceptSuggestionOnCommitCharacter` (default: `true`).
- Setting: `editor.acceptSuggestionOnEnter` controls whether Enter also accepts suggestions (`"on"`, `"off"`, `"smart"`).

### CompletionItem Properties (LSP)

| Property | Type | Description |
|----------|------|-------------|
| `label` | `string` or `CompletionItemLabelDetails` | Display text in the completion list |
| `labelDetails` | `{ detail?: string, description?: string }` | Additional label parts (rendered after the label) |
| `kind` | `CompletionItemKind` | Icon/category of the item (see below) |
| `tags` | `CompletionItemTag[]` | e.g., `Deprecated` (renders with strikethrough) |
| `detail` | `string` | Short description shown beside the label (e.g., type signature) |
| `documentation` | `string` or `MarkupContent` | Full documentation shown in the details pane (supports Markdown) |
| `deprecated` | `boolean` | Marks item as deprecated (prefer `tags` instead) |
| `preselect` | `boolean` | Select this item when showing the list |
| `sortText` | `string` | String used for sorting (defaults to `label`) |
| `filterText` | `string` | String used for filtering (defaults to `label`) |
| `insertText` | `string` | Text to insert (defaults to `label`) |
| `insertTextFormat` | `1` (PlainText) or `2` (Snippet) | Whether `insertText` is a snippet with placeholders |
| `insertTextMode` | `1` (asIs) or `2` (adjustIndentation) | How whitespace/indentation is handled |
| `textEdit` | `TextEdit` or `InsertReplaceEdit` | Precise edit to apply (overrides `insertText`) |
| `textEditText` | `string` | Text of the edit (when using default ranges) |
| `additionalTextEdits` | `TextEdit[]` | Additional edits (e.g., auto-import statements) |
| `commitCharacters` | `string[]` | Characters that accept this item when typed |
| `command` | `Command` | Command executed after accepting the item |
| `data` | `any` | Data preserved for `completionItem/resolve` |

### CompletionItemKind Values

| Value | Kind | Icon |
|-------|------|------|
| 1 | Text | Abc |
| 2 | Method | Box with M |
| 3 | Function | Box with f |
| 4 | Constructor | Wrench |
| 5 | Field | Box with field icon |
| 6 | Variable | Box with x |
| 7 | Class | Box with C |
| 8 | Interface | Box with I |
| 9 | Module | Box with module icon |
| 10 | Property | Wrench |
| 11 | Unit | Ruler |
| 12 | Value | Box with value |
| 13 | Enum | Box with E |
| 14 | Keyword | Box with key icon |
| 15 | Snippet | Box with snippet icon |
| 16 | Color | Color swatch |
| 17 | File | File icon |
| 18 | Reference | Reference icon |
| 19 | Folder | Folder icon |
| 20 | EnumMember | Box with member icon |
| 21 | Constant | Box with constant icon |
| 22 | Struct | Box with S |
| 23 | Event | Lightning bolt |
| 24 | Operator | Box with operator icon |
| 25 | TypeParameter | Box with T |

### Completion Resolve

- The `completionItem/resolve` request allows lazy-loading of expensive completion item details.
- When the user selects an item in the list, the client sends `completionItem/resolve` to the server.
- The server fills in missing properties like `documentation`, `detail`, `additionalTextEdits`, and `command`.
- This avoids computing documentation for all items upfront.

### Snippet Completions

- `insertTextFormat: 2` marks the `insertText` as a snippet.
- Snippet syntax: `$1`, `$2` for tab stops; `${1:default}` for placeholders; `${1|option1,option2|}` for choice.
- Setting: `editor.snippetSuggestions` controls snippet position: `"top"`, `"bottom"`, `"inline"` (default), or `"none"`.
- Setting: `editor.suggest.snippetsPreventQuickSuggestions` -- when `true`, typing inside a snippet placeholder does not trigger quick suggestions.

---

## 2. Signature Help / Parameter Hints

### Overview

Signature help displays function/method signatures as an overlay while typing arguments. It highlights the current parameter and shows documentation for each parameter.

### LSP Method

- `textDocument/signatureHelp`

### Trigger Characters

- **Trigger characters**: Characters that open signature help even when it is not already showing. Typically `(` for function calls.
- **Retrigger characters**: Characters that update signature help only when it is already visible. Typically `,` (to advance to the next parameter) and `)`.
- Providers register both sets independently via `SignatureHelpRegistrationOptions`.

### Manual Trigger

- `Ctrl+Shift+Space` (Windows/Linux) or `Cmd+Shift+Space` (macOS).

### SignatureHelp Response

```typescript
interface SignatureHelp {
    signatures: SignatureInformation[];
    activeSignature?: number;     // Index of the active signature
    activeParameter?: number;     // Index of the active parameter
}

interface SignatureInformation {
    label: string;                         // Full signature string
    documentation?: string | MarkupContent; // Signature documentation
    parameters?: ParameterInformation[];
    activeParameter?: number;              // Override for this specific signature
}

interface ParameterInformation {
    label: string | [number, number];      // Name or offset range in signature label
    documentation?: string | MarkupContent; // Parameter documentation
}
```

### Context Information

The request includes `SignatureHelpContext`:
- `triggerKind`: `Invoked` (manual), `TriggerCharacter`, or `ContentChange`.
- `triggerCharacter`: The actual character that triggered the request.
- `isRetrigger`: Whether signature help was already active.
- `activeSignatureHelp`: The currently active signature help (for retrigger).

### Settings

- `editor.parameterHints.enabled` (default: `true`) -- enable/disable parameter hints.
- `editor.parameterHints.cycle` (default: `false`) -- cycle through overloads when pressing up/down at the boundaries.

---

## 3. Hover Information

### Overview

Hovering over a symbol displays information about it in a tooltip. Typically shows the type, documentation, and source location.

### LSP Method

- `textDocument/hover`

### Hover Response

```typescript
interface Hover {
    contents: MarkedString | MarkedString[] | MarkupContent;
    range?: Range;  // Range to highlight while hover is shown
}
```

- `contents` supports Markdown for rich formatting (code blocks, links, bold/italic).
- Multiple `MarkedString` values are concatenated with separators.
- `range` defines which source range to highlight while the hover is displayed.

### Behavior

- Triggered by mouse hover or `Ctrl+K Ctrl+I` keyboard shortcut.
- Multiple hover providers can contribute content; results are merged.
- The hover tooltip is dismissable by moving the mouse away or pressing `Escape`.
- Setting: `editor.hover.enabled` (default: `true`).
- Setting: `editor.hover.delay` (default: `300` ms) -- delay before showing hover.
- Setting: `editor.hover.sticky` (default: `true`) -- hover stays visible when mouse moves into it (allows clicking links).

---

## 4. Go to Definition / Type Definition / Implementation

### Go to Definition

- **LSP method**: `textDocument/definition`
- **Shortcut**: `F12` or `Ctrl+Click`
- **Peek**: `Alt+F12` opens an inline peek editor showing the definition.
- If multiple definitions exist, VS Code shows a peek view with all locations.
- Returns `Location | Location[] | LocationLink[]`.

### Go to Type Definition

- **LSP method**: `textDocument/typeDefinition`
- **Shortcut**: No default keybinding (available via command palette or right-click menu).
- Navigates to the type of a variable/expression rather than its declaration.
- Example: For `let x = new Foo()`, Go to Definition goes to `x`'s declaration, while Go to Type Definition goes to `class Foo`.

### Go to Implementation

- **LSP method**: `textDocument/implementation`
- **Shortcut**: `Ctrl+F12`
- Navigates to implementations of interfaces or abstract methods.
- Shows all concrete implementations when invoked on an interface method.

### Go to Declaration

- **LSP method**: `textDocument/declaration`
- Navigates to the declaration of a symbol (as opposed to its definition).
- Distinction matters in languages like C/C++ where declaration and definition differ.

### LocationLink

```typescript
interface LocationLink {
    originSelectionRange?: Range;  // Range of the origin (for highlighting)
    targetUri: DocumentUri;
    targetRange: Range;            // Full range of the target (e.g., enclosing function)
    targetSelectionRange: Range;   // Precise range to select/highlight
}
```

---

## 5. Find All References / Peek References

### Find All References

- **LSP method**: `textDocument/references`
- **Shortcut**: `Shift+F12` (peek) or `Shift+Alt+F12` (side panel)
- Shows all locations where a symbol is referenced across the workspace.

### Request Parameters

```typescript
interface ReferenceParams {
    textDocument: TextDocumentIdentifier;
    position: Position;
    context: ReferenceContext;
}

interface ReferenceContext {
    includeDeclaration: boolean;  // Include the declaration in results
}
```

### Peek References

- `Shift+F12` opens an inline peek view with a list of all references.
- References are shown with file name, line number, and surrounding context.
- Users can click to navigate to any reference.
- The References panel (`Shift+Alt+F12`) shows references in the sidebar for persistent navigation.

---

## 6. Rename Symbol

### Overview

Renames a symbol and all its references across the workspace in a single operation.

### LSP Methods

- `textDocument/rename` -- performs the rename.
- `textDocument/prepareRename` -- validates whether rename is possible at the position and returns the range/placeholder.

### Shortcut

- `F2` on a symbol.

### Workflow

1. User presses `F2` on a symbol.
2. VS Code sends `textDocument/prepareRename` to validate and get the current name/range.
3. An inline text input appears with the current name pre-filled.
4. User types the new name and presses Enter.
5. VS Code sends `textDocument/rename` with the new name.
6. Server returns a `WorkspaceEdit` with all text changes across files.
7. VS Code applies all changes atomically (can be undone in one step).

### prepareRename Response

```typescript
// One of:
Range                                    // Range of the symbol to rename
{ range: Range, placeholder: string }    // Range + suggested name
{ defaultBehavior: boolean }             // Use client's default behavior
```

### WorkspaceEdit

The rename response is a `WorkspaceEdit` that can contain:
- `changes`: Map of URI to TextEdit arrays.
- `documentChanges`: Array of `TextDocumentEdit`, `CreateFile`, `RenameFile`, or `DeleteFile` operations.

---

## 7. Code Actions (Quick Fix, Refactoring)

### Overview

Code actions provide automated fixes and refactorings. They appear as a light bulb icon in the editor gutter or can be triggered manually.

### LSP Method

- `textDocument/codeAction`
- `codeAction/resolve` -- lazily resolve a code action's edit/command.

### Trigger

- **Light bulb**: Appears automatically when code actions are available at the cursor position.
- **Quick Fix**: `Ctrl+.` (Windows/Linux) or `Cmd+.` (macOS).
- **Refactor menu**: `Ctrl+Shift+R` -- shows only refactoring actions.
- **Source actions**: Triggered via command palette or keybinding.

### CodeActionKind Hierarchy

Code action kinds form a hierarchy separated by dots:

| Kind | Description |
|------|-------------|
| `""` (empty) | All code actions |
| `quickfix` | Fixes for diagnostics (errors, warnings) |
| `refactor` | All refactoring actions |
| `refactor.extract` | Extract to function/variable/constant/method |
| `refactor.extract.function` | Extract selection to a new function |
| `refactor.extract.variable` | Extract expression to a new variable |
| `refactor.extract.constant` | Extract expression to a new constant |
| `refactor.inline` | Inline a variable/function/method |
| `refactor.inline.variable` | Inline a variable |
| `refactor.move` | Move code to another location |
| `refactor.rewrite` | Rewrite code (e.g., convert to arrow function) |
| `source` | Whole-file actions (not shown in light bulb menu) |
| `source.organizeImports` | Sort and remove unused imports |
| `source.fixAll` | Auto-fix all fixable errors in the file |
| `source.fixAll.eslint` | Fix all ESLint errors (extension-specific) |

### Preferred Code Actions

- A code action can be marked as `isPreferred: true`.
- Preferred quick fixes address the underlying error directly.
- Preferred refactorings are the most common choice.
- The command `editor.action.quickFix` with `"preferred": true` runs only the preferred action.

### Code Actions on Save

```json
"editor.codeActionsOnSave": {
    "source.organizeImports": "explicit",
    "source.fixAll": "explicit"
}
```

Values: `"explicit"` (only when explicitly saved), `"always"` (including auto-save), `"never"`.

### Disabled Code Actions

- A code action can include `disabled: { reason: string }` to explain why it is unavailable.
- Disabled actions appear grayed out in the menu with the reason shown.

---

## 8. Code Lens

### Overview

Code Lens shows actionable, contextual information interspersed with source code, displayed as inline annotations above code lines.

### LSP Methods

- `textDocument/codeLens` -- compute code lenses for a document.
- `codeLens/resolve` -- resolve a code lens (fill in the command).

### Common Uses

- **Reference count**: "3 references" above a function definition.
- **Test status**: "Run Test | Debug Test" above test functions.
- **Git blame**: "John Doe, 2 days ago" showing last author.
- **Implementations**: "2 implementations" above an interface method.

### CodeLens Structure

```typescript
interface CodeLens {
    range: Range;       // Position where the lens is shown
    command?: Command;  // Clickable command (title + command ID + arguments)
    data?: any;         // Data for resolve
}
```

### Settings

- `editor.codeLens` (default: `true`) -- show/hide code lenses.
- `editor.codeLensFontFamily` -- font family for code lenses.
- `editor.codeLensFontSize` -- font size (default: 90% of editor font).

---

## 9. Document Formatting / Range Formatting / On-Type Formatting

### Document Formatting

- **LSP method**: `textDocument/formatting`
- **Shortcut**: `Shift+Alt+F` (Windows), `Shift+Option+F` (macOS)
- Formats the entire document according to the formatter's rules.
- Only one formatter can be active per language. If multiple are available, VS Code prompts to choose a default.

### Range Formatting

- **LSP method**: `textDocument/rangeFormatting`
- **Shortcut**: `Ctrl+K Ctrl+F` (formats selection)
- Formats only the selected range of text.
- Setting: `editor.formatOnPaste` (default: `false`) -- format pasted content using range formatting.

### On-Type Formatting

- **LSP method**: `textDocument/onTypeFormatting`
- Formats code automatically as the user types specific trigger characters.
- Common trigger characters: `;`, `}`, `\n` (newline).
- The provider registers `firstTriggerCharacter` and optional `moreTriggerCharacter` values.
- Setting: `editor.formatOnType` (default: `false`) -- enable/disable on-type formatting.

### Format on Save

- Setting: `editor.formatOnSave` (default: `false`) -- format the document when saving.
- Setting: `editor.formatOnSaveMode`: `"file"` (whole file), `"modifications"` (only changed lines), or `"modificationsIfAvailable"`.

### Formatting Options

```typescript
interface FormattingOptions {
    tabSize: number;
    insertSpaces: boolean;
    trimTrailingWhitespace?: boolean;
    insertFinalNewline?: boolean;
    trimFinalNewlines?: boolean;
}
```

### Best Practices

- Formatters should return the smallest possible text edits to preserve markers like diagnostic positions and breakpoints.
- Range formatting should respect the provided range and not modify code outside it.

---

## 10. Document Symbols / Outline

### Overview

Document symbols provide the structure of a document, displayed in the Outline view (sidebar) and Breadcrumbs (top of editor). Supports hierarchical symbol trees.

### LSP Method

- `textDocument/documentSymbol`

### Response Formats

**Hierarchical (preferred)**:
```typescript
interface DocumentSymbol {
    name: string;
    detail?: string;
    kind: SymbolKind;
    tags?: SymbolTag[];          // e.g., Deprecated
    range: Range;                 // Full range including body
    selectionRange: Range;        // Range of the name
    children?: DocumentSymbol[];  // Nested symbols
}
```

**Flat (legacy)**:
```typescript
interface SymbolInformation {
    name: string;
    kind: SymbolKind;
    tags?: SymbolTag[];
    location: Location;
    containerName?: string;
}
```

### SymbolKind Values

| Value | Kind | Value | Kind |
|-------|------|-------|------|
| 1 | File | 14 | Constant |
| 2 | Module | 15 | String |
| 3 | Namespace | 16 | Number |
| 4 | Package | 17 | Boolean |
| 5 | Class | 18 | Array |
| 6 | Method | 19 | Object |
| 7 | Property | 20 | Key |
| 8 | Field | 21 | Null |
| 9 | Constructor | 22 | EnumMember |
| 10 | Enum | 23 | Struct |
| 11 | Interface | 24 | Event |
| 12 | Function | 25 | Operator |
| 13 | Variable | 26 | TypeParameter |

### Features

- **Outline view**: Sidebar panel showing the document's symbol tree.
- **Breadcrumbs**: Navigation bar showing the current location in the symbol hierarchy.
- **Go to Symbol**: `Ctrl+Shift+O` opens a quick picker for document symbols. Typing `:` groups by kind.

---

## 11. Workspace Symbols

### Overview

Allows searching for symbols across all files in the workspace, not just the current document.

### LSP Methods

- `workspace/symbol` -- search for symbols matching a query string.
- `workspaceSymbol/resolve` -- resolve additional details for a workspace symbol.

### Shortcut

- `Ctrl+T` -- opens the workspace symbol search.

### Behavior

- The user types a query and the server returns matching symbols from across the workspace.
- Results include the symbol name, kind, location (file + range), and optional container name.
- Supports partial/fuzzy matching depending on the server implementation.

---

## 12. Inlay Hints

### Overview

Inlay hints display additional inline information directly in the source code, rendered as subtle annotations that are not part of the actual text.

### LSP Methods

- `textDocument/inlayHint` -- compute inlay hints for a range.
- `inlayHint/resolve` -- resolve additional details (e.g., tooltip, text edits).

### Types of Inlay Hints

| Type | Example | Description |
|------|---------|-------------|
| Parameter names | `foo(/*name:*/ "Alice", /*age:*/ 30)` | Shows parameter names at call sites |
| Variable types | `let x/*: number*/ = 42` | Shows inferred types for variables |
| Return types | `function foo()/*: string*/ {` | Shows inferred return types |
| Chained method types | `.filter(...)/*: string[]*/.map(...)` | Shows intermediate types in chains |

### InlayHint Structure

```typescript
interface InlayHint {
    position: Position;
    label: string | InlayHintLabelPart[];
    kind?: InlayHintKind;      // 1 = Type, 2 = Parameter
    textEdits?: TextEdit[];     // Applied when accepting the hint (e.g., to add explicit type)
    tooltip?: string | MarkupContent;
    paddingLeft?: boolean;
    paddingRight?: boolean;
    data?: any;
}

interface InlayHintLabelPart {
    value: string;
    tooltip?: string | MarkupContent;
    location?: Location;        // Clickable -- navigates to this location
    command?: Command;           // Clickable -- executes this command
}
```

### Settings

- `editor.inlayHints.enabled`: `"on"` (default), `"off"`, `"onUnlessPressed"` (hide when holding `Ctrl+Alt`), `"offUnlessPressed"` (show only when holding `Ctrl+Alt`).
- `editor.inlayHints.fontSize` -- font size for hints.
- `editor.inlayHints.fontFamily` -- font family for hints.
- `editor.inlayHints.padding` -- add padding around hints.
- Language-specific settings (e.g., TypeScript):
  - `typescript.inlayHints.parameterNames.enabled`
  - `typescript.inlayHints.variableTypes.enabled`
  - `typescript.inlayHints.functionLikeReturnTypes.enabled`
  - `typescript.inlayHints.propertyDeclarationTypes.enabled`

---

## 13. Linked Editing (Rename Tag Pairs)

### Overview

Linked editing allows simultaneous editing of related ranges. The primary use case is HTML/XML tag pairs: editing the opening tag name automatically updates the closing tag.

### LSP Method

- `textDocument/linkedEditingRange`

### Response

```typescript
interface LinkedEditingRanges {
    ranges: Range[];             // All linked ranges
    wordPattern?: string;        // Regex constraining valid edits
}
```

### Behavior

- When the cursor is on one of the linked ranges and the user types, all linked ranges are updated simultaneously.
- The `wordPattern` restricts what the user can type (e.g., valid tag names).
- Setting: `editor.linkedEditing` (default: `false`) -- must be explicitly enabled.

### Supported Scenarios

- HTML/XML tag pair renaming.
- JSX tag pair renaming.
- Any paired constructs a language server defines.

---

## 14. Selection Range (Expand/Shrink Selection)

### Overview

Selection range provides smart expand/shrink selection based on the semantic structure of the code, rather than simple syntactic boundaries.

### LSP Method

- `textDocument/selectionRange`

### Shortcuts

- **Expand**: `Shift+Alt+Right` (Windows/Linux) or `Ctrl+Shift+Cmd+Right` (macOS).
- **Shrink**: `Shift+Alt+Left` (Windows/Linux) or `Ctrl+Shift+Cmd+Left` (macOS).

### Response

```typescript
interface SelectionRange {
    range: Range;
    parent?: SelectionRange;    // Enclosing selection range (linked list)
}
```

### Behavior

- Returns a linked list of increasingly larger ranges.
- Example expansion sequence: `identifier` -> `expression` -> `statement` -> `block` -> `function body` -> `function` -> `class body` -> `class` -> `file`.
- Each expansion step corresponds to a meaningful semantic boundary.

---

## 15. Call Hierarchy / Type Hierarchy

### Call Hierarchy

Shows all calls from or to a function, allowing drill-down through the call graph.

**LSP Methods**:
- `textDocument/prepareCallHierarchy` -- find the call hierarchy item at a position.
- `callHierarchy/incomingCalls` -- who calls this function?
- `callHierarchy/outgoingCalls` -- what does this function call?

**Shortcut**: `Shift+Alt+H` (Show Call Hierarchy).

**CallHierarchyItem**:
```typescript
interface CallHierarchyItem {
    name: string;
    kind: SymbolKind;
    tags?: SymbolTag[];
    detail?: string;
    uri: DocumentUri;
    range: Range;
    selectionRange: Range;
    data?: any;
}
```

**Incoming/Outgoing Calls**:
```typescript
interface CallHierarchyIncomingCall {
    from: CallHierarchyItem;
    fromRanges: Range[];        // Ranges within `from` that make the call
}

interface CallHierarchyOutgoingCall {
    to: CallHierarchyItem;
    fromRanges: Range[];        // Ranges in the current item that make the call
}
```

### Type Hierarchy

Shows the inheritance hierarchy of types (supertypes and subtypes).

**LSP Methods**:
- `textDocument/prepareTypeHierarchy` -- find the type hierarchy item at a position.
- `typeHierarchy/supertypes` -- parent types (base classes, implemented interfaces).
- `typeHierarchy/subtypes` -- child types (derived classes, implementors).

**TypeHierarchyItem**: Same structure as `CallHierarchyItem`.

---

## 16. Semantic Tokens

### Overview

Semantic tokens provide token-level type and modifier information based on the language server's semantic understanding of the code. This enables richer syntax highlighting than TextMate grammars alone, distinguishing between (for example) a local variable and a parameter, or a function call and a function declaration.

### LSP Methods

- `textDocument/semanticTokens/full` -- get all semantic tokens for a document.
- `textDocument/semanticTokens/full/delta` -- get only changed tokens since the last request.
- `textDocument/semanticTokens/range` -- get tokens for a specific range.

### Token Encoding

Tokens are encoded as a flat array of integers, with every 5 integers representing one token:

| Offset | Meaning |
|--------|---------|
| 0 | Delta line (relative to previous token) |
| 1 | Delta start character (relative to previous token on same line, or absolute if on a new line) |
| 2 | Length |
| 3 | Token type index (into the legend) |
| 4 | Token modifiers (bitmask) |

### Semantic Token Types (Predefined)

The LSP 3.17 specification defines 23 standard token types:

| Index | Token Type | Description |
|-------|------------|-------------|
| 0 | `namespace` | Namespace identifiers |
| 1 | `type` | Type names (when not more specific) |
| 2 | `class` | Class names |
| 3 | `enum` | Enum type names |
| 4 | `interface` | Interface names |
| 5 | `struct` | Struct names |
| 6 | `typeParameter` | Generic type parameters (e.g., `T`) |
| 7 | `parameter` | Function/method parameters |
| 8 | `variable` | Variable identifiers |
| 9 | `property` | Object/class property names |
| 10 | `enumMember` | Enum member values |
| 11 | `event` | Event identifiers |
| 12 | `function` | Function names |
| 13 | `method` | Method names |
| 14 | `macro` | Macro names |
| 15 | `keyword` | Language keywords |
| 16 | `modifier` | Language modifiers (e.g., `public`, `async`) |
| 17 | `comment` | Comment text |
| 18 | `string` | String literals |
| 19 | `number` | Numeric literals |
| 20 | `regexp` | Regular expression literals |
| 21 | `operator` | Operators |
| 22 | `decorator` | Decorators / annotations |

### Semantic Token Modifiers (Predefined)

The LSP 3.17 specification defines 10 standard modifiers, encoded as a bitmask:

| Bit | Modifier | Description |
|-----|----------|-------------|
| 0 | `declaration` | Symbol is being declared |
| 1 | `definition` | Symbol is being defined |
| 2 | `readonly` | Symbol is read-only / constant |
| 3 | `static` | Symbol is static |
| 4 | `deprecated` | Symbol is deprecated |
| 5 | `abstract` | Symbol is abstract |
| 6 | `async` | Symbol is async |
| 7 | `modification` | Symbol is being modified (written to) |
| 8 | `documentation` | Symbol is in a documentation context |
| 9 | `defaultLibrary` | Symbol is from the standard library |

### Semantic Token Legend

The server declares its token types and modifiers in `SemanticTokensLegend` during initialization:

```typescript
interface SemanticTokensLegend {
    tokenTypes: string[];       // e.g., ["namespace", "type", "class", ...]
    tokenModifiers: string[];   // e.g., ["declaration", "readonly", ...]
}
```

### VS Code Theme Integration

Semantic tokens integrate with VS Code themes via `semanticTokenColors` in color themes:

```json
{
    "semanticTokenColors": {
        "variable.readonly": "#4FC1FF",
        "parameter": "#9CDCFE",
        "property.declaration": "#DCDCAA",
        "function.defaultLibrary": "#DCDCAA",
        "class": "#4EC9B0"
    }
}
```

### Settings

- `editor.semanticHighlighting.enabled`: `true` (default for themes that support it), `false`, or `"configuredByTheme"`.

---

## Summary: LSP Methods Quick Reference

| Feature | LSP Method(s) | VS Code Shortcut |
|---------|---------------|-----------------|
| Completion | `textDocument/completion`, `completionItem/resolve` | `Ctrl+Space` |
| Signature Help | `textDocument/signatureHelp` | `Ctrl+Shift+Space` |
| Hover | `textDocument/hover` | Mouse hover, `Ctrl+K Ctrl+I` |
| Go to Definition | `textDocument/definition` | `F12`, `Ctrl+Click` |
| Go to Type Definition | `textDocument/typeDefinition` | (no default) |
| Go to Implementation | `textDocument/implementation` | `Ctrl+F12` |
| Go to Declaration | `textDocument/declaration` | (no default) |
| Find References | `textDocument/references` | `Shift+F12` |
| Rename | `textDocument/rename`, `textDocument/prepareRename` | `F2` |
| Code Actions | `textDocument/codeAction`, `codeAction/resolve` | `Ctrl+.` |
| Code Lens | `textDocument/codeLens`, `codeLens/resolve` | (inline) |
| Document Formatting | `textDocument/formatting` | `Shift+Alt+F` |
| Range Formatting | `textDocument/rangeFormatting` | `Ctrl+K Ctrl+F` |
| On-Type Formatting | `textDocument/onTypeFormatting` | (automatic) |
| Document Symbols | `textDocument/documentSymbol` | `Ctrl+Shift+O` |
| Workspace Symbols | `workspace/symbol`, `workspaceSymbol/resolve` | `Ctrl+T` |
| Inlay Hints | `textDocument/inlayHint`, `inlayHint/resolve` | (inline) |
| Linked Editing | `textDocument/linkedEditingRange` | (automatic) |
| Selection Range | `textDocument/selectionRange` | `Shift+Alt+Right/Left` |
| Call Hierarchy | `textDocument/prepareCallHierarchy`, `callHierarchy/incomingCalls`, `callHierarchy/outgoingCalls` | `Shift+Alt+H` |
| Type Hierarchy | `textDocument/prepareTypeHierarchy`, `typeHierarchy/supertypes`, `typeHierarchy/subtypes` | (no default) |
| Semantic Tokens | `textDocument/semanticTokens/full`, `.../delta`, `.../range` | (automatic) |
| Document Highlight | `textDocument/documentHighlight` | (automatic on cursor) |
| Folding Range | `textDocument/foldingRange` | `Ctrl+Shift+[` / `]` |
| Document Link | `textDocument/documentLink`, `documentLink/resolve` | (clickable links) |
| Document Color | `textDocument/documentColor`, `textDocument/colorPresentation` | (color swatches) |
| Diagnostics | `textDocument/publishDiagnostics` (push) or `textDocument/diagnostic` (pull) | (automatic) |

---

## Sources

- [VS Code IntelliSense Documentation](https://code.visualstudio.com/docs/editing/intellisense)
- [VS Code Code Navigation](https://code.visualstudio.com/docs/editing/editingevolved)
- [VS Code Refactoring](https://code.visualstudio.com/docs/editing/refactoring)
- [VS Code Programmatic Language Features](https://code.visualstudio.com/api/language-extensions/programmatic-language-features)
- [VS Code API Reference](https://code.visualstudio.com/api/references/vscode-api)
- [LSP 3.17 Specification](https://microsoft.github.io/language-server-protocol/specifications/lsp/3.17/specification/)
- [VS Code Language Server Extension Guide](https://code.visualstudio.com/api/language-extensions/language-server-extension-guide)
- [VS Code TypeScript Editing](https://code.visualstudio.com/docs/typescript/typescript-editing)
- [VS Code Basic Editing](https://code.visualstudio.com/docs/editing/codebasics)
- [VS Code Default Settings Reference](https://code.visualstudio.com/docs/reference/default-settings)
