# Code Editor Comparison: VS Code (Monaco) vs VGS IDE (AvaloniaEdit)

**Date**: 2026-03-18
**VGS IDE Editor**: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (built on AvaloniaEdit)
**VS Code Editor**: Monaco Editor (web-based, TypeScript)

---

## Feature-by-Feature Comparison

| # | Feature | VS Code (Monaco) | VGS IDE (AvaloniaEdit) | Gap |
|---|---------|-------------------|------------------------|-----|
| 1 | **Syntax highlighting** | TextMate grammars + semantic tokens; 100+ languages built-in | AvaloniaEdit `.xshd` highlighting definition (`HighlightingLoader.cs`); BasicLang-specific rules registered at startup | **Minor** -- VGS has full keyword/string/comment/operator highlighting for BasicLang; lacks semantic token coloring from LSP (tokens are lexer-based only) |
| 2 | **Code folding** | Language-aware folding (indentation, markers, brackets); Ctrl+Shift+[ / ] | `BasicLangFoldingStrategy` with debounced updates (500 ms timer); fold/unfold all; Ctrl+M toggle; click fold margin; preserves fold state across edits | **None** -- Feature-complete for BasicLang; fold margin click handling is robust with re-entrancy guards |
| 3 | **Auto-close brackets/quotes** | Configurable per language; auto-close `()`, `[]`, `{}`, `""`, `''`; skip-over closing chars; delete empty pairs | Full implementation: `AutoClosePairs` dict covers `()`, `[]`, `{}`, `""`, `''`; skip-over closing brackets/quotes; `DeleteEmptyPair()` on Backspace; suppresses inside strings/comments; `SurroundingPairs` wraps selections | **None** -- Parity with VS Code including surround-with-pair on selection and smart suppression |
| 4 | **Multi-cursor editing** | Alt+Click to add cursor; Ctrl+Alt+Up/Down; Ctrl+D next occurrence; Ctrl+Shift+L select all occurrences | `MultiCursorManager` + `MultiCursorRenderer` + `MultiCursorInputHandler`: Alt+Click, add above/below, `AddNextOccurrence()` (Ctrl+D equivalent), `SelectAllOccurrences()` (Ctrl+Shift+L equivalent); type/backspace/delete at all cursors | **Minor** -- Core multi-cursor works; cursor positions clear after each edit operation (simplified offset tracking); no Ctrl+U to undo last cursor add |
| 5 | **Find/Replace (regex)** | Inline find bar (Ctrl+F / Ctrl+H); match case, whole word, regex; F3/Shift+F3 navigation; incremental highlight; replace single/all | `InlineFindReplaceControl` overlay at top-right; match case (Alt+C), whole word (Alt+W), regex (Alt+R) toggles; incremental search with 150 ms debounce; `SearchHighlightRenderer` highlights all matches with current-match distinction; replace single and replace all; Enter/Shift+Enter/F3 navigation; match count display ("3 of 12") | **None** -- Full parity including regex, incremental highlighting, and keyboard shortcuts |
| 6 | **Minimap** | Right-side minimap showing document overview; click/drag to scroll; syntax-colored; viewport indicator | `MinimapControl` on right side; renders lines as colored rectangles (keywords = blue, comments = green, strings = orange, code = gray); click/drag scrolls editor; viewport indicator border; updates on text change and scroll | **Minor** -- VGS minimap uses simplified colored rectangles (not character-level rendering); no hover preview; functional but less detailed than Monaco's glyph-based minimap |
| 7 | **Bracket matching highlight** | Highlights matching brackets on cursor adjacency; customizable colors | `BracketHighlighter` (DocumentColorizingTransformer): matches `()`, `[]`, `{}`; scans up to 10,000 chars; skips strings (`"..."`) and comments (`'...`); highlights with gold foreground on dark background | **None** -- Full parity; smart scanning avoids false matches inside strings/comments |
| 8 | **Indentation guides** | Vertical lines at each indent level; active indent highlighted | `IndentationGuideRenderer` (IBackgroundRenderer): draws vertical lines at each tab stop; active indent level (at caret line) uses brighter pen; handles tabs and spaces; smart context for blank lines (checks 5 lines above/below) | **None** -- Full parity including active-indent highlighting |
| 9 | **Word wrap** | Alt+Z toggle; configurable (off, on, wordWrapColumn, bounded) | `WordWrap` styled property; Alt+Z keyboard toggle; AvaloniaEdit native word wrap | **Minor** -- VGS has on/off toggle only; no column-bounded wrap mode |
| 10 | **Line numbers** | Always on by default; configurable (on, off, relative, interval) | `ShowLineNumbers` styled property (default true); styled with `#858585` foreground | **Minor** -- No relative line number mode |
| 11 | **Whitespace rendering** | Configurable: none, boundary, selection, trailing, all | AvaloniaEdit options exist (`ShowTabs`, `ShowSpaces`, `ShowEndOfLine`) but hardcoded to `false` in `ConfigureEditor()`; no runtime toggle exposed | **Moderate** -- Infrastructure exists but no UI toggle to enable whitespace rendering at runtime |
| 12 | **Go to line (Ctrl+G)** | Quick input dialog for line number | `GoToLineRequested` event fires on Ctrl+G; `NavigateTo(line, column)` scrolls and positions caret | **Minor** -- Event plumbing is complete; depends on shell providing the input dialog (not in editor control itself) |
| 13 | **Column selection (Alt+Shift+Click)** | Alt+Shift+Click or Alt+Shift+Arrow for rectangular/box selection | Not implemented -- no `RectangularSelection` or box selection support found in codebase | **Significant** -- AvaloniaEdit supports rectangular selection natively but VGS does not wire it up |
| 14 | **Drag-and-drop text** | Drag selected text to new position | `EnableTextDragDrop = true` in `ConfigureEditor()`; uses AvaloniaEdit built-in drag-drop | **None** -- Enabled via AvaloniaEdit option |
| 15 | **Snippets (Tab expansion)** | Extensive snippet system with tab stops, placeholders, choices, transforms | `SnippetProvider` with 35+ built-in snippets (func, sub, if, for, class, try, etc.); `TryExpandSnippet()` triggers on Tab; `SnippetDefinition.Expand()` handles `${N:default}` and `$0` cursor markers; `SnippetCompletionData` shows snippets in completion list | **Minor** -- VGS handles `$0` final cursor and `${N:default}` placeholders; does not support tab-stop cycling (Tab between `$1`, `$2`, etc.), choice placeholders, or snippet transforms |
| 16 | **Surround with brackets** | Type `(` with selection to wrap it in `(selection)` | `SurroundingPairs` in `OnTextAreaTextEntering`: typing `(`, `[`, `{`, or `"` with active selection wraps it; inner text remains selected after wrap | **None** -- Full parity for bracket/quote surround |
| 17 | **Smart indentation** | Language-specific indentation rules (increase/decrease patterns, onEnterRules) | `BasicLangIndentationStrategy` + `SmartIndentHandler`: regex-based increase/decrease patterns matching VS Code's `language-configuration.json`; auto-indent after Sub/Function/If/For/etc.; auto-outdent on End/Else/Next/Loop; indent correction on Enter | **None** -- Direct port of VS Code extension's indentation rules |
| 18 | **Code lens rendering** | Inline annotations above lines (references count, run/debug); clickable | `CodeLensRenderer` (IBackgroundRenderer): renders cornflower-blue text above target lines; groups multiple lenses per line with `|` separator; hit-region click detection fires `CodeLensClicked` event; supports reference counts and custom commands | **Minor** -- VGS code lens works but does not add extra line spacing (draws in existing background space); may overlap with code on tight layouts |

---

## Additional Features Present in VGS IDE (Not in Comparison List)

| Feature | Implementation |
|---------|---------------|
| **Breakpoint margin** | `BreakpointMargin` with 4 breakpoint types (normal, conditional, hit-count, logpoint); enabled/disabled/unverified states; execution arrow; DPI-aware scaling |
| **Bookmark margin** | `BookmarkMargin` for line bookmarks per file |
| **Inline debug values** | `InlineDebugValueRenderer` shows variable values next to code lines during debugging |
| **Inlay hints** | `InlayHintRenderer` renders parameter names and type annotations inline |
| **Error squiggles** | `TextMarkerService` with error (red), warning (yellow), info (blue), hint marker types; hover tooltip shows diagnostic message |
| **Signature help popup** | Popup above caret showing function signature, active parameter, and documentation |
| **Search highlight** | `SearchHighlightRenderer` paints translucent gold rectangles for all search matches with bright gold for current match |
| **Document links** | Ctrl+Click navigation on LSP-provided document links |
| **Split view** | Horizontal/vertical split editor view |
| **Undo preservation** | `TextDocument` sharing across tab switches preserves full undo history |
| **Line operations** | Duplicate line (Ctrl+D), move line up/down (Alt+Up/Down), delete line (Ctrl+Shift+K), insert line above/below (Ctrl+Shift+Enter / Ctrl+Enter) |
| **Toggle comment** | Ctrl+/ toggles `'` line comments for selection or current line |
| **Code completion** | Auto-trigger on `.`, `(`, `,` and after 2+ identifier characters; Ctrl+Space manual trigger |
| **Go to definition** | F12 and Ctrl+Click via LSP |
| **Find all references** | Shift+F12 via LSP |
| **Rename symbol** | F2 via LSP |
| **Code actions** | Ctrl+. for quick fixes via LSP |
| **Format document** | Ctrl+Shift+F via LSP |
| **Expand/shrink selection** | Selection range requests via LSP |

---

## Gap Summary

| Gap Level | Count | Features |
|-----------|-------|----------|
| **None** (full parity) | 9 | Code folding, auto-close brackets, find/replace, bracket matching, indentation guides, drag-and-drop, surround with brackets, smart indentation, whitespace rendering (infrastructure) |
| **Minor** (functional but less polished) | 7 | Syntax highlighting (no semantic tokens), multi-cursor (simplified offset tracking), minimap (rectangle-based), word wrap (no column mode), line numbers (no relative mode), snippets (no tab-stop cycling), code lens (no extra spacing) |
| **Moderate** (partially missing) | 1 | Whitespace rendering (no runtime toggle UI) |
| **Significant** (missing) | 1 | Column/rectangular selection |

---

## Architecture Comparison

| Aspect | VS Code (Monaco) | VGS IDE (AvaloniaEdit) |
|--------|-------------------|------------------------|
| **Rendering engine** | HTML Canvas / DOM-based | Avalonia UI (hardware-accelerated, cross-platform) |
| **Text buffer** | Piece table with line tokens | AvaloniaEdit `TextDocument` (gap buffer) |
| **Extension model** | JSON contributions + TypeScript API | C# classes implementing AvaloniaEdit interfaces (`IBackgroundRenderer`, `DocumentColorizingTransformer`, `IIndentationStrategy`) |
| **Highlighting** | TextMate grammars (JSON) + semantic tokens (LSP) | `.xshd` XML definitions + `DocumentColorizingTransformer` |
| **Folding** | Provider-based (language server or indentation) | `IFoldingStrategy` implementation |
| **Completion** | `CompletionItemProvider` interface | `ICompletionData` via AvaloniaEdit `CompletionWindow` |
| **Undo** | Piece table operations | AvaloniaEdit `UndoStack` (preserved across tab switches via shared `TextDocument`) |

---

## Recommendations for Closing Remaining Gaps

1. **Column/rectangular selection** -- AvaloniaEdit supports `RectangleSelection` natively. Wire `Alt+Shift+Click` and `Alt+Shift+Arrow` to enable it in `CodeEditorControl`.

2. **Whitespace rendering toggle** -- Add a menu item or toolbar button that sets `_textEditor.Options.ShowSpaces = true` and `ShowTabs = true` at runtime. The AvaloniaEdit infrastructure already supports this.

3. **Snippet tab-stop cycling** -- Implement a `SnippetSession` class that tracks `$1`, `$2`, etc. positions after expansion and cycles through them on Tab/Shift+Tab.

4. **Semantic token highlighting** -- Wire the LSP `textDocument/semanticTokens` response to apply token-level coloring via a `DocumentColorizingTransformer`.

5. **Relative line numbers** -- Add a mode toggle that overrides the line number margin to display relative offsets from the caret line.
