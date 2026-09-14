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
<div class="pipe-step"><span class="n">API</span><span class="s">Core</span><span class="f">44 interfaces + models</span></div>
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
| Semantic highlighting | Driven by LSP semantic tokens, not a regex grammar |
| Folding | Collapsible regions from LSP folding ranges |
| Bracket matching | With auto-closing and surrounding pairs — select text, type a bracket, it wraps |
| Multi-cursor | Multiple simultaneous carets |
| Smart indentation | Regex-based indent/outdent matching VS Code behaviour |
| Indentation guides | Vertical lines at tab stops, active guide highlighted |
| Code lens | Clickable annotations above functions and classes |
| Snippets | 30+ snippets matching VS Code's set (`func`, `sub`, `if`, `for`, `class`, `try`, …) |
| Inline find/replace | `Ctrl+F` / `Ctrl+H` with incremental search and match highlighting |
| Command palette | `Ctrl+Shift+P`, ~50 commands, fuzzy search |
| Bookmarks | Navigate by marked locations |

## Navigating

Go to definition and type definition, find all references, document outline, go to line,
go to symbol, quick open, peek definition, call hierarchy and type hierarchy — all LSP
backed, so they work identically for BasicLang and for C++.

## C++ as a peer language

You can open, edit, build and debug `.cpp` / `.cc` / `.cxx` / `.h` / `.hpp` / `.hh` /
`.hxx` / `.inl` files alongside `.bas` sources in the same project. clangd provides
IntelliSense from a generated `compile_commands.json`; `lldb-dap` provides native
debugging. Neither requires setup — both are fetched on demand.

## Session persistence

Per-project window layout is saved and restored on reopen (**View → Reset Layout** to
start over), along with open documents, breakpoints and bookmarks. Design:
`docs/superpowers/specs/2026-07-08-per-project-layout-persistence-design.md`.
`WorkspaceStateStore.cs` and `HotExitService.cs` implement it; `AutoSaveService.cs`
handles unsaved work.

## Where to look next

- [Panels and dialogs](#/ide-panels) — the tool windows and the refactoring surface.
- [Editor internals](#/ide-editor) — how the editing control is built.
- [Services](#/ide-services) — the 44 interfaces and what implements them.
- [Extension host](#/ide-extensions) — running VS Code extensions.
- [Debugging](#/debugging) — the DAP side.

## Parity tracking

The IDE was built against explicit comparisons with VS Code rather than by feel. The
scorecards and research are in the repo:

`docs/comparisons/` — editor, IntelliSense, project system, UI/UX, and an overall
parity scorecard.
`docs/research/` — VS Code's build system, editor core, micro-features, error handling,
git, IntelliSense, project system, settings, terminal, UI/UX.
