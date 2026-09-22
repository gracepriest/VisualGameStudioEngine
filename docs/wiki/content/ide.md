title: IDE overview
lede: An Avalonia shell that composes an editor library, a service layer, and external language servers into a VS Code-class environment.
---
`VisualGameStudio.Shell` is the application. `VisualGameStudio.Editor` is a library it
consumes. Building `.Editor` does not give you an IDE — this is the single most common
first mistake in the repo.

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
IDE/VisualGameStudio.exe     # or the prebuilt drop
```

> [note] After any `.axaml` change, `dotnet clean` before building. Stale Avalonia build
> cache causes crashes that look nothing like a markup error.

## Composition

<div class="pipe">
<div class="pipe-step"><span class="n">UI</span><span class="s">Shell</span><span class="f">views + view models + docking</span></div>
<div class="pipe-step"><span class="n">CTRL</span><span class="s">Editor</span><span class="f">the code editor control</span></div>
<div class="pipe-step"><span class="n">IMPL</span><span class="s">ProjectSystem</span><span class="f">~60 services</span></div>
<div class="pipe-step"><span class="n">API</span><span class="s">Core</span><span class="f">46 interfaces + models</span></div>
</div>

The Shell resolves services through `VisualGameStudio.Core` interfaces, so a panel talks
to `IBuildService`, never to a concrete build implementation. That seam is what lets the
test suite exercise view models without an Avalonia window — and it is also the reason
some UI paths (the new-project wizard, for one) can only be verified by a human clicking
them.

## Editing

| Capability | Notes |
|---|---|
| IntelliSense | Completion, parameter hints, quick info, signature help, inlay hints |
| Semantic highlighting | LSP semantic tokens layered *over* the lexer colouring — `SemanticTokenHighlighter` overrides the `.xshd` / TextMate result rather than replacing it, so colouring degrades to the regex grammar when no server is attached |
| Folding | LSP folding ranges when a server answers, with a regex fallback (`BasicLangFoldingStrategy`) so folding still works unattached |
| Bracket matching | With auto-closing and surrounding pairs — select text, type a bracket, it wraps |
| Multi-cursor | Multiple simultaneous carets |
| Smart indentation | Regex-based indent/outdent matching VS Code behaviour |
| Indentation guides | Vertical lines at tab stops, active guide highlighted |
| Code lens | Reference counts on Subs, Functions and Classes (`basiclang.showReferences`), `Run` / `Debug` above `Main`, and `Inherits <Base>` on derived classes |
| Snippets | 57 built-in snippets in 11 categories — Control Flow, Declarations, Properties, Variables, Async, Patterns, Game Development, Output, Statements, Expressions, Comments (`sub`, `func`, `if`, `for`, `class`, `prop`, `try`, …) — plus user snippets loaded per language |
| Inline find/replace | `Ctrl+F` / `Ctrl+H` with incremental search and match highlighting |
| Command palette | `Ctrl+Shift+P` (`Ctrl+P` = quick open), 178 built-in commands across 15 categories plus extension-contributed ones, fuzzy matching with MRU; prefixes switch mode — `>` commands, `:` go to line, `@` document symbols, `#` workspace symbols, `?` help |
| Bookmarks | Navigate by marked locations |
| Minimap | Document overview alongside the scrollbar, toggled from View → Toggle Minimap |
| Sticky scroll | Enclosing Sub/Function/Class pinned at the top of the viewport, click to jump (`StickyScrollEnabled`, on by default) |
| Breadcrumbs | Symbol path bar above the document (`BreadcrumbControl`) |
| Bracket pair colorization | Nesting-depth colours on top of match highlighting (`BracketPairColorizer`) |
| Inline colours | Colour literals get a swatch and a picker popup (`InlineColorRenderer`, `ColorPickerPopup`) |
| Occurrence highlighting | Other instances of the selection highlighted as you select (`SelectionOccurrenceHighlighter`) |
| Git gutter | Added/changed/removed marks in the margin, plus inline blame (`GitGutterMargin`, `InlineBlameRenderer`) |

## Navigating

Go to definition and type definition, find all references, document outline, go to line,
Go to definition and type definition, find all references, document outline, go to symbol,
peek definition, call hierarchy and type hierarchy are all LSP backed, so they work
identically for BasicLang and for C++. Go to line (`Ctrl+G`) and quick open (`Ctrl+P`) are
not — go to line is a dialog over the active document, and quick open lists the project's
`Compile`/`Content` items plus any `.bas` / `.bl` / `.blproj` found under the project
directory — so both keep working with no language server attached.
backed, so they work identically for BasicLang and for C++.

## C++ as a peer language

You can open, edit, build and debug `.cpp` / `.cc` / `.cxx` / `.h` / `.hpp` / `.hh` /
`.hxx` / `.inl` files alongside `.bas` sources in the same project. clangd provides
IntelliSense from a generated `compile_commands.json`; `lldb-dap` provides native
debugging. Neither ships with the IDE. Each is located by the same chain — the
`cpp.clangd.path` / `cpp.lldbDap.path` override → the IDE's own install under
`~/.vgs/tools` → `PATH` → conventional LLVM install directories → nothing
(`ClangdLocator`, `LldbDapLocator`; null is a real answer).

> [trap] The in-IDE download is **not automatic and not universal**. It is a one-click
> "Download C++ tools" action (Tools menu, or the missing-clangd toast) run by
> `ClangdDownloadFlow` / `LldbDapDownloadFlow`, and the pinned assets are
> **Windows-only** — on any other platform the flow refuses with a toast. clangd is
> pinned <span class="pill ok">22.1.6</span>, and a mid-session install needs an IDE
> restart before DI picks it up. `lldb-dap` is <span class="pill warn">not pinned
> yet</span>: `LldbDapInstaller.ExpectedSha256` is still `REPLACE-AT-RELEASE-TIME`, so
> `IsReleasePinned` is false and the download is gated off — until the release runbook
> fills the pins, point `cpp.lldbDap.path` at an existing `lldb-dap` yourself.

## Session persistence

Per-project window layout is saved and restored on reopen (**View → Reset Layout** to
start over), along with the open documents and each one's caret position. Design:
`docs/superpowers/specs/2026-07-08-per-project-layout-persistence-design.md`.

| What | Owner | Where it lands |
|---|---|---|
| Dock layout, window size, open documents + carets, active document | `WorkspaceStateStore.cs` | `~/.vgs/workspaceStorage/<sha256-of-project-path>/state.json` — user-local |
| Unsaved buffers across restarts (hot exit) | `HotExitService.cs` | `~/.vgs/backups/`, keyed by a hash of the file path |
| Auto-save (`Off` / `AfterDelay` / `OnFocusChange` / `OnWindowChange`) | `AutoSaveService.cs` | writes the file itself |
| Breakpoints | `BreakpointsViewModel.cs` | `<projectDir>/.vgs/breakpoints.json` |
| Bookmarks | `BookmarkService.cs` | `<projectDir>/.vgs/bookmarks.json` |

> [note] Breakpoints and bookmarks are **not** part of the workspace state store. They
> live in the project folder, so they travel with the repo; the layout is user-local and
> keyed by a hash of the project path, so personal layout stays out of git.
start over), along with open documents, breakpoints and bookmarks. Design:
`docs/superpowers/specs/2026-07-08-per-project-layout-persistence-design.md`.
`WorkspaceStateStore.cs` and `HotExitService.cs` implement it; `AutoSaveService.cs`
handles unsaved work.

## Where to look next

- [Panels and dialogs](#/ide-panels) — the tool windows and the refactoring surface.
- [Editor internals](#/ide-editor) — how the editing control is built.
- [Services](#/ide-services) — the 46 interfaces and what implements them.
- [Extension host](#/ide-extensions) — running VS Code extensions.
- [Debugging](#/debugging) — the DAP side.

## Parity tracking

The IDE was built against explicit comparisons with VS Code rather than by feel. The
scorecards and research are in the repo:

`docs/comparisons/` — editor, IntelliSense, project system, UI/UX, and an overall
parity scorecard.
`docs/research/` — VS Code's build system, editor core, micro-features, error handling,
git, IntelliSense, project system, settings, terminal, UI/UX.
