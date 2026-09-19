title: Editor internals
lede: `VisualGameStudio.Editor` is a reusable Avalonia control — highlighting, folding, margins, markers, multi-cursor.
---
This project is a **library**, not an application. It provides the code-editing control
the Shell hosts. Most of it is UI mechanics — but not all. BasicLang keyword knowledge is baked into the library:
`Folding/BasicLangFoldingStrategy.cs`, `Completion/SmartIndentHandler.cs`,
`Completion/BasicLangIndentationStrategy.cs`, `Completion/SnippetProvider.cs` and the five
embedded `.xshd` grammars all carry their own regexes and keyword lists. Symbol-level knowledge —
completion items, diagnostics, semantic tokens — is what arrives over LSP.

## Directory map

| Directory | Responsibility |
|---|---|
| `Controls/` | The editor control and its composed pieces |
| `Highlighting/` | Syntax and semantic highlighting |
| `Completion/` | Completion list UI and commit rules, signature help, **and** the indentation strategies (`SmartIndentHandler.cs`, `BasicLangIndentationStrategy.cs`, `CStyleIndentationStrategy.cs`) plus the 39-entry built-in snippet table (`SnippetProvider.cs`) |
| `Folding/` | Fold region model and gutter interaction |
| `Formatting/` | One file — `LspTextEditApplier.cs`, which applies LSP `TextEdit`s to a live document. Indentation lives in `Completion/`; on-type and format-document triggering lives in the Shell |
| `Margins/` | `CurrentLineNumberMargin`, `BreakpointMargin`, `BookmarkMargin`, `DiagnosticMargin`, `GitGutterMargin` (change bars). The fold margin is AvaloniaEdit's own `FoldingMargin`, installed from `Controls/CodeEditorControl.axaml.cs` |
| `MultiCursor/` | Multiple caret and selection model |
| `Rendering/` | Custom text rendering layers |
| `TextMarkers/` | 15 files of over-text rendering: squiggles (`TextMarkerService.cs`), occurrence and search highlights, bracket matching and pair colorizing, indent guides, inlay hints, CodeLens models, inline colour / blame / debug values, merge-conflict and execution-line rendering. Bookmarks are **not** here — `BookmarkMargin.cs` is in `Margins/` |
| `Services/` | Editor-scoped services |
| `Utils/` | One file — `MarkdownLite.cs`, the hover / doc-comment renderer |
| `EditorTheme.cs` (root) | Static theme palette and the `ThemeChanged` event the margins and renderers read |

## Highlighting: two systems, one surface

**TextMate grammars** give immediate colour the moment a file opens — before any language
server has started. There is no TextMate tokenizer in the editor: nothing in the tree
references `TextMateSharp` or `AvaloniaEdit.TextMate`. `Highlighting/TextMateToAvalonEditConverter.cs`
(823 lines) instead performs an *approximate*, one-shot conversion of a grammar into an
AvalonEdit `IHighlightingDefinition`, pulling keywords, strings, comments and numbers out of
the grammar's patterns — so the result is rule-based colouring, not real TextMate scope
tokenization. Grammar loading is in
`VisualGameStudio.ProjectSystem/Services/`
(`TextMateService.cs`, `TextMateRegistrar.cs`). Five `.xshd` files — `BasicLang`, `BasicLangLight`, `BasicLangHighContrast`, `Cpp`, `CppLight` —
embedded as resources by `VisualGameStudio.Editor.csproj`, cover the
AvaloniaEdit-style definitions.

> [trap] There are two parallel TextMate stacks, and two parallel snippet stacks. The
> TextMate service the IDE runs is `ProjectSystem/Services/TextMateService.cs` +
> `TextMateRegistrar.cs`, registered in `Shell/Configuration/ServiceConfiguration.cs` against
> `Core/Abstractions/Services/ITextMateService.cs`; the grammar model the editor converts
> (`TextMateGrammarInfo`) lives in the *other* file, `Core/TextMate/ITextMateService.cs`.
> `ProjectSystem/TextMate/TextMateService.cs` is an unreferenced duplicate — no
> `using VisualGameStudio.ProjectSystem.TextMate` exists anywhere in the tree. Snippets have
> the same shape: `ProjectSystem/Snippets/SnippetService.cs` (against the dead
> `Core/Snippets/ISnippetService.cs`) sits unused beside the registered
> `ProjectSystem/Services/SnippetService.cs`.

**Semantic tokens** from the LSP then refine it — distinguishing a type from a variable,
a parameter from an enum member. The legend is 19 token types (`Namespace` … `Operator` — there
is no `Field`) plus 10 modifier bits, fixed in `BasicLang/LSP/SemanticTokensHandler.cs` and
mirrored index-for-index by `VisualGameStudio.Core/Utilities/SemanticTokenLegendMap.cs`, which
remaps clangd's tokens into the same slots. Reorder the legend and remapped tokens silently
shift colour. This is the layer that makes BasicLang highlighting correct
rather than merely plausible.

`VisualGameStudio.ProjectSystem/Services/VsCodeThemeLoader.cs` loads VS Code themes (through
`Shell/ThemeManager.cs`), so colour schemes carry over to the `.xshd` / TextMate layer.

> [trap] They do **not** carry over to semantic tokens. `Highlighting/SemanticTokenHighlighter.cs`
> hard-codes its 14 brushes to VS Code Dark+ values and never reads `EditorTheme` or a loaded
> theme, so semantic colours stay dark-theme in the light and high-contrast schemes.

## Text markers

Markers are the general mechanism behind everything drawn *over* text:

- Diagnostic squiggles, coloured by **severity only** — `TextMarkerType` is
  `Error`/`Warning`/`Info`/`Hint`/`Highlight` and `DiagnosticItem` carries no tags. The LSP
  server does emit `DiagnosticTag.Unnecessary` / `Deprecated`
  (`BasicLang/LSP/DocumentManager.cs`), but no client code reads them; the strikethrough on a
  deprecated symbol comes from semantic token *modifier* bit 4 instead
  (`Highlighting/SemanticTokenHighlighter.cs`), and nothing renders `Unnecessary`.
- Occurrence highlights from `textDocument/documentHighlight`.
- Find/replace match highlighting, updated incrementally as you type.
- Bookmark and breakpoint indicators in the margin.

## Multi-cursor

The multi-cursor model keeps a primary caret plus a set of secondary carets, each with
its own selection — `MultiCursorManager` stores only the secondaries, so `CursorCount` is
`_cursors.Count + 1`. `MultiCursorInputHandler` binds `Alt`+click to drop a caret anywhere,
`Ctrl+Alt+Up` / `Ctrl+Alt+Down` to add a caret above or below, `Ctrl+D` to add the next
occurrence, `Ctrl+Shift+L` to select all occurrences, and `Esc` to collapse back to a single
caret. Edits apply to all carets; undo collapses the group into a single
transaction so `Ctrl+Z` behaves the way it does elsewhere.

## Formatting

Three distinct paths:

1. **Smart indentation** — regex-based indent/outdent rules matching VS Code's behaviour,
   applied as you type newlines.
2. **On-type formatting** — LSP `onTypeFormatting`. The server registers a single trigger
   character (`FirstTriggerCharacter = "\n"` in `BasicLang/LSP/OnTypeFormattingHandler.cs`);
   the Shell (`Views/Documents/CodeEditorDocumentView.axaml.cs`) fires it on Enter, and also on
   a line whose entire trimmed text is one of `End Sub`, `End If`, `End Function`, `End While`,
   `End Module`, `End Class` — whole keywords, not single characters.
3. **Format document** — LSP `formatting`, applied by `Formatting/LspTextEditApplier.cs` as
   **several targeted replaces in reverse document order**, wrapped in one
   `BeginUpdate`/`EndUpdate` so they collapse into a single undo unit. The reason the class
   exists is the **caret**, not undo: the old whole-buffer `Replace(0, TextLength, …)` made
   AvaloniaEdit map the caret to the end of the new text and yanked it past the last brace.
   The same path also runs on save when `editor.formatOnSave` is on.

## Surrounding pairs and auto-closing

Typing `(`, `[`, `{` or `"` with a selection wraps the selection rather than replacing it, and
leaves the inner text selected. Typing `(`, `[`, `{`, `"` or `'` without a selection inserts the
pair and places the caret inside; typing `)`, `]` or `}` when it is already the next character
skips over it instead of doubling it. Both tables are hard-coded in
`Controls/CodeEditorControl.axaml.cs` rather than read from `language-configuration.json`, and
they differ: `'` auto-closes but never surrounds, because it opens a comment in BasicLang.

> [trap] Auto-close obeys the `editor.autoClosingBrackets` setting; surround-with does not.
> Surround is suppressed while a snippet placeholder is active — otherwise typing `(` over a
> placeholder yields `WriteLine((value))`. In C/C++ files `'` never auto-closes (character
> literals), and typing `}` on an otherwise-blank line dedents it one level.

## Snippets

Three snippet tables, and they are not the same table. `Completion/SnippetProvider.cs` in this
project holds **39** built-in entries (45 prefixes) for Tab expansion and completion;
`ProjectSystem/Services/SnippetService.cs` holds **58** `AddBuiltIn` calls (57 distinct
prefixes) behind `Core/Abstractions/Services/ISnippetService.cs`, with the model in
`Core/Models/Snippet.cs` — `Core/Snippets/` holds only a dead parallel `ISnippetService.cs`,
used by the equally dead `ProjectSystem/Snippets/SnippetService.cs`.
`vscode-basiclang/snippets/basiclang.json` has 40 entries and mirrors the **editor's** table:
every one of the editor's 45 prefixes appears in it, plus `header`. It does not mirror
`SnippetService`, which adds 20 prefixes the editor lacks (`game`, `gameloop`, `drawrect`,
`drawtext`, `ctor`, `await`, `print`, `singleton`, `observer`, `todo`, …) and itself lacks 8
the editor has (`dimv`, `fore`, `function`, `ife`, `readline`, `subroutine`, `switch`,
`writeline`).

> [note] Two `SearchSnippets_*` tests are known-failing baseline. They show up in the fast
> subset and are **not yours** — see [Building and testing](#/build-test).

## Where the design came from

`docs/comparisons/editor-comparison.md` and `docs/research/vscode-editor-core.md` /
`vscode-editor-micro-features.md` record what was compared and what was deliberately
skipped.
