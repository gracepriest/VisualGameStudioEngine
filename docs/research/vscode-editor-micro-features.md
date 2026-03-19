# VS Code Editor Micro-Features -- VGS IDE Recommendations

Research date: 2026-03-18

This document catalogs small VS Code editor features that make a big UX difference
and assesses what VGS IDE already has versus what it could add with moderate effort.

---

## Current VGS IDE Editor Capabilities (Already Implemented)

| Feature | Implementation |
|---------|---------------|
| Bracket matching highlight | `BracketHighlighter.cs` -- highlights matching `()[]{}` with gold foreground on dark background |
| Indentation guides | `IndentationGuideRenderer.cs` -- vertical lines at tab stops, active indent level highlighted brighter |
| Minimap | `MinimapControl.axaml.cs` -- code overview on right side with viewport indicator and click-to-navigate |
| Multi-cursor editing | `MultiCursorManager.cs`, `MultiCursorRenderer.cs`, `MultiCursorInputHandler.cs` -- add cursors above/below, next occurrence, select all occurrences |
| Multi-cursor blink timer | `MultiCursorRenderer.cs` has `MultiCursorBlinkTimer` class |
| Auto-close brackets | `CodeEditorControl.axaml.cs` -- auto-inserts closing `)}]` and skip-over on typing closing bracket |
| Configurable font size/family | `EditorFontSize` and `EditorFontFamily` styled properties |
| Settings keys defined | `ISettingsService.cs` defines `CursorBlinking` and `SmoothScrolling` constants (but NOT wired up) |
| Breadcrumb navigation | `BreadcrumbControl.axaml.cs` |
| Code Lens | `CodeLensRenderer.cs` |
| Inlay hints | `InlayHintRenderer.cs` |
| Inline debug values | `InlineDebugValueRenderer.cs` |
| Semantic token highlighting | `SemanticTokenHighlighter.cs` |
| Inline find/replace | `InlineFindReplaceControl.axaml.cs` |
| Text markers | `TextMarkerService.cs` |
| Breakpoint margin | `BreakpointMargin.cs` |
| Bookmark margin | `BookmarkMargin.cs` |

---

## Top 10 Micro-Features to Add

### 1. Sticky Scroll (Scope Headers Pinned at Top)

**What VS Code does**: As you scroll down, the opening lines of enclosing scopes
(class, function, loop, if-block) stick to the top of the editor. Clicking a sticky
line jumps to that scope's start. Configurable max lines (default 5).

**VGS status**: Not implemented.

**Implementation approach**: Create a `StickyScrollPanel` overlay above the text editor
that listens to scroll events. Use the existing syntax tree (available from LSP or
local parser) to determine enclosing scopes at the current scroll position. Render
scope-opening lines as clickable text blocks pinned to the editor top.

**Effort**: Medium. Requires scope detection and an overlay panel. AvaloniaEdit does not
have this built in, so it would be a custom control layered on top of `TextEditor`.

**Impact**: High -- prevents getting lost in large files, one of VS Code's most
praised features.

---

### 2. Smooth Cursor Caret Animation

**What VS Code does**: `editor.cursorSmoothCaretAnimation` causes the cursor to glide
smoothly to its new position instead of jumping instantly. Makes it easier to track
the cursor after arrow-key moves, Home/End, or clicking.

**VGS status**: Setting key `Editor.CursorBlinking` is defined in `ISettingsService.cs`
but not wired to any behavior. AvaloniaEdit's default caret has no smooth animation.

**Implementation approach**: Override the caret rendering in AvaloniaEdit's `TextArea`.
Use Avalonia's `Animation` framework to animate `Caret.ScreenPosition` with a short
(80-120ms) easing transition. The multi-cursor blink timer already exists and shows
the pattern.

**Effort**: Low-Medium. Avalonia has animation primitives; the main work is intercepting
caret position changes and animating the visual.

**Impact**: Medium -- subtle polish that makes the editor feel premium.

---

### 3. Smooth Scrolling

**What VS Code does**: `editor.smoothScrolling` adds a short animation to vertical
scrolling instead of instant jumps, making scroll behavior less jarring.

**VGS status**: Setting key `Editor.SmoothScrolling` is defined but not implemented.

**Implementation approach**: Intercept scroll events on the `TextEditor.TextArea` and
use `Avalonia.Animation` to animate `ScrollOffset` changes over ~100ms. Apply easing
function (e.g., `CubicEaseOut`).

**Effort**: Low. AvaloniaEdit exposes `ScrollOffset`; wrapping changes in an animation
is straightforward.

**Impact**: Medium -- noticeable improvement in scroll feel.

---

### 4. Cursor Blinking Styles (blink, smooth, phase, expand, solid)

**What VS Code does**: Five cursor blink modes:
- `blink` -- standard on/off blink
- `smooth` -- fade in/out
- `phase` -- sinusoidal brightness oscillation
- `expand` -- grows from thin line to full width
- `solid` -- no blinking

**VGS status**: Setting key defined, not wired. Default AvaloniaEdit blink only.

**Implementation approach**: Subclass or patch the caret renderer. Use a timer + opacity
animation for smooth/phase modes, width animation for expand. The
`MultiCursorBlinkTimer` already demonstrates the timer pattern.

**Effort**: Low. Mostly animation work on the existing caret visual.

**Impact**: Low-Medium -- personalization option that users appreciate.

---

### 5. Bracket Pair Colorization

**What VS Code does**: Nested brackets are colored differently by depth level (e.g.,
gold, violet, cyan cycling). Built into the editor core since VS Code 1.60 with
sub-millisecond performance.

**VGS status**: `BracketHighlighter.cs` highlights the *matching pair* at the cursor
(gold foreground, dark background) but does NOT colorize all brackets by nesting depth.

**Implementation approach**: Extend `BracketHighlighter` (or create a new
`BracketPairColorizationTransformer`) that walks visible lines and assigns colors
based on nesting depth. Use a cycling palette of 3-6 colors. The existing bracket
scanner logic in `FindMatchingBracket` can be adapted to track depth across the
document.

**Effort**: Medium. Need a single-pass bracket depth scanner that runs on visible lines.
Performance matters for large files -- cache depth per line and invalidate on edit.

**Impact**: High -- one of the most popular VS Code features, dramatically improves
readability of nested code.

---

### 6. Git Gutter / Modified Line Indicators

**What VS Code does**: Small colored bars in the editor gutter show which lines were
added (green), modified (yellow), or deleted (red) compared to the last git commit.
Clicking shows an inline diff. Controlled by `scm.diffDecorations`.

**VGS status**: Not implemented. The editor has `BreakpointMargin` and `BookmarkMargin`
in the gutter but no git change indicators.

**Implementation approach**: Create a `GitGutterMargin` that runs `git diff` on the
current file and parses the unified diff output to determine changed line ranges.
Render colored bars (3px wide) in the gutter margin. Re-run diff on file save or
after a debounced delay on text changes. Use `LibGit2Sharp` NuGet or shell out to
`git`.

**Effort**: Medium. Diff parsing is well-understood; the margin rendering follows the
same pattern as `BreakpointMargin`.

**Impact**: High -- essential for version-controlled projects, gives immediate visual
feedback about what changed.

---

### 7. Font Ligature Support

**What VS Code does**: `editor.fontLigatures` enables programming ligatures when using
fonts like Fira Code or Cascadia Code. Operators like `>=`, `!=`, `=>`, `&&` render
as single combined glyphs.

**VGS status**: The editor already uses `Cascadia Code` as a font (seen in signature
help popup). But no explicit ligature enable/disable setting exists.

**Implementation approach**: In Avalonia, font ligatures are controlled by
`TextRunProperties` and the `Typeface` options. Set
`TextFormattingMode`/`TextRenderingMode` appropriately and ensure the font shaping
engine is not disabled. Add a boolean setting `Editor.FontLigatures` and apply it to
the text editor's `TextArea.TextView`.

**Effort**: Low. Avalonia's text rendering respects OpenType ligature tables by default
if the font supports them. May just need to verify it is not accidentally disabled.

**Impact**: Low-Medium -- appreciated by developers who use ligature fonts.

---

### 8. Inline Color Picker / Color Decorators

**What VS Code does**: Color values in CSS/code (e.g., `#FF5733`, `rgb(255,87,51)`)
show a small colored square inline. Clicking it opens a color picker to edit the
value. Works in any file type.

**VGS status**: Not implemented.

**Implementation approach**: Create a `ColorDecoratorTransformer` that scans visible
lines for color patterns (`#RRGGBB`, `Color.FromArgb(...)`, `Color.Parse("...")`).
Render a small colored rectangle inline before the color text. On click, open a
color picker popup (Avalonia has community color picker controls). This is especially
relevant since BasicLang game code frequently uses color values for the Raylib engine.

**Effort**: Medium. Pattern matching for colors is simple; the inline decoration and
popup picker need custom UI work.

**Impact**: Medium -- very useful for game development where color values are common.

---

### 9. Minimap: Highlight Current Line and Search Matches

**What VS Code does**: The minimap shows the current cursor line highlighted and all
search result matches as colored markers, making it easy to see where matches are
distributed across the file.

**VGS status**: `MinimapControl.axaml.cs` exists with basic code rendering and viewport
indicator, but does NOT highlight the current line position or search matches.

**Implementation approach**: In `MinimapControl.UpdateMinimap()`, add:
1. A horizontal line/bar at the current caret's proportional Y position
2. Small colored rectangles at Y positions corresponding to search match lines
   (get match positions from `InlineFindReplaceControl`)
3. Small colored rectangles for diagnostic error/warning lines

**Effort**: Low. The minimap already renders; adding overlaid markers is simple drawing.

**Impact**: Medium -- makes the minimap genuinely useful for navigation rather than
just decorative.

---

### 10. Auto-Closing Pairs for Quotes and Backticks

**What VS Code does**: In addition to brackets, VS Code auto-closes `""`, `''`,
and backtick pairs. Typing `"` inserts `""` and places the cursor between them.
If text is selected, wrapping it in quotes/brackets is also supported (surround
selection).

**VGS status**: `CodeEditorControl.axaml.cs` auto-closes `()`, `[]`, `{}` but the
auto-close logic does NOT handle `""` or surround-selection.

**Implementation approach**: Extend the existing auto-close bracket logic in
`HandleAutoCloseBrackets` to include `"` and `'` pairs. Add surround-selection:
when text is selected and the user types `(`, `[`, `{`, or `"`, wrap the selection
instead of replacing it.

**Effort**: Low. The bracket auto-close infrastructure already exists; adding quotes
and surround behavior is a small extension.

**Impact**: Medium -- reduces keystrokes and matches expected behavior from VS Code.

---

## Priority Matrix

| # | Feature | Effort | Impact | Priority |
|---|---------|--------|--------|----------|
| 5 | Bracket pair colorization | Medium | High | **P1** |
| 6 | Git gutter indicators | Medium | High | **P1** |
| 1 | Sticky scroll | Medium | High | **P1** |
| 10 | Auto-close quotes + surround | Low | Medium | **P2** |
| 9 | Minimap enhancements | Low | Medium | **P2** |
| 3 | Smooth scrolling | Low | Medium | **P2** |
| 2 | Smooth cursor animation | Low-Med | Medium | **P2** |
| 8 | Inline color picker | Medium | Medium | **P3** |
| 4 | Cursor blink styles | Low | Low-Med | **P3** |
| 7 | Font ligature toggle | Low | Low-Med | **P3** |

---

## Sources

- [VS Code Sticky Scroll -- DEV Community](https://dev.to/bytehide/visual-studio-codes-new-editor-sticky-scroll-feature-never-get-lost-in-the-code-again-1dob)
- [Sticky Scroll Preview -- Visual Studio Blog](https://devblogs.microsoft.com/visualstudio/sticky-scroll-now-in-preview/)
- [Smooth Cursor in VSCode -- Codu](https://www.codu.co/articles/smooth-cursor-in-vscode-cmcuuimz)
- [Enable Smooth Typing and Cursor Animation -- DEV Community](https://dev.to/trishiraj/enable-smooth-typing-and-cursor-animation-in-vscode-318d)
- [Change Cursor Color, Style and Animation -- bobbyhadz](https://bobbyhadz.com/blog/vscode-change-cursor-color-style-and-animation)
- [Font Ligatures in VS Code -- Fira Code Wiki](https://github.com/tonsky/FiraCode/wiki/VS-Code-Instructions)
- [Enable Font Ligatures -- bobbyhadz](https://bobbyhadz.com/blog/enable-font-ligatures-in-vscode)
- [Git Gutter Indicators -- tutorialpedia.org](https://www.tutorialpedia.org/blog/vs-code-highlight-modified-lines/)
- [VS Code Color Picker -- bobbyhadz](https://bobbyhadz.com/blog/vscode-color-picker)
- [Bracket Pair Colorization 10,000x Faster -- VS Code Blog](https://code.visualstudio.com/blogs/2021/09/29/bracket-pair-colorization)
- [VS Code Bracket Guides -- mostviertel.tech](https://www.mostviertel.tech/blog/2022/vscode-brackets)
- [VS Code User Interface Docs](https://code.visualstudio.com/docs/getstarted/userinterface)
- [VS Code v1.78 Release Notes (Color Picker)](https://code.visualstudio.com/updates/v1_78)
