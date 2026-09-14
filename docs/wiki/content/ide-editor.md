title: Editor internals
lede: `VisualGameStudio.Editor` is a reusable Avalonia control — highlighting, folding, margins, markers, multi-cursor.
---
This project is a **library**, not an application. It provides the code-editing control
the Shell hosts. Everything here is UI mechanics; language knowledge arrives over LSP.

## Directory map

| Directory | Responsibility |
|---|---|
| `Controls/` | The editor control and its composed pieces |
| `Highlighting/` | Syntax and semantic highlighting |
| `Completion/` | The completion list UI and its interaction model |
| `Folding/` | Fold region model and gutter interaction |
| `Formatting/` | Indentation, on-type formatting, format-document plumbing |
| `Margins/` | Line numbers, breakpoint gutter, fold markers, change bars |
| `MultiCursor/` | Multiple caret and selection model |
| `Rendering/` | Custom text rendering layers |
| `TextMarkers/` | Squiggles, occurrence highlights, search matches, bookmarks |
| `Services/` | Editor-scoped services |
| `Utils/` | Shared helpers |

## Highlighting: two systems, one surface

**TextMate grammars** give immediate, tokenizer-level colour the moment a file opens —
before any language server has started. Grammar loading is in
`VisualGameStudio.Core/TextMate/` and `VisualGameStudio.ProjectSystem/TextMate/`
(`TextMateService.cs`, `TextMateRegistrar.cs`). `.xshd` files in the repo cover the
AvaloniaEdit-style definitions.

**Semantic tokens** from the LSP then refine it — distinguishing a type from a variable,
a parameter from a field. This is the layer that makes BasicLang highlighting correct
rather than merely plausible.

`VsCodeThemeLoader.cs` loads VS Code themes, so colour schemes carry over.

## Text markers

Markers are the general mechanism behind everything drawn *over* text:

- Diagnostic squiggles, with LSP diagnostic tags (deprecated, unnecessary) changing the
  rendering.
- Occurrence highlights from `textDocument/documentHighlight`.
- Find/replace match highlighting, updated incrementally as you type.
- Bookmark and breakpoint indicators in the margin.

## Multi-cursor

The multi-cursor model keeps a primary caret plus a set of secondary carets, each with
its own selection. Edits apply to all carets; undo collapses the group into a single
transaction so `Ctrl+Z` behaves the way it does elsewhere.

## Formatting

Three distinct paths:

1. **Smart indentation** — regex-based indent/outdent rules matching VS Code's behaviour,
   applied as you type newlines.
2. **On-type formatting** — LSP `onTypeFormatting`, triggered by specific characters.
3. **Format document** — LSP `formatting`, applied as a single edit that **preserves
   undo**, so formatting does not destroy your history.

## Surrounding pairs and auto-closing

Typing an opening bracket or quote with a selection wraps the selection rather than
replacing it; typing one without a selection inserts the pair and places the caret
inside. Both behaviours match VS Code, which was the explicit design target.

## Snippets

30+ snippets shipped to match VS Code's BASIC-family set. `SnippetService.cs`
(ProjectSystem) and `VisualGameStudio.Core/Snippets/` define the model;
`vscode-basiclang/snippets/basiclang.json` mirrors them for the VS Code extension.

> [note] Two `SearchSnippets_*` tests are known-failing baseline. They show up in the fast
> subset and are **not yours** — see [Building and testing](#/build-test).

## Where the design came from

`docs/comparisons/editor-comparison.md` and `docs/research/vscode-editor-core.md` /
`vscode-editor-micro-features.md` record what was compared and what was deliberately
skipped.
