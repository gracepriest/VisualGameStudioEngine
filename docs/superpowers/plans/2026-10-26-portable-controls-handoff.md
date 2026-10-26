# Portable control library — handoff and memory (2026-10-26)

Branch: `feat/portable-controls`
Last pushed commit: `11573f3a` (`test: complete Task 47 2d gate`)

## What is complete

- Tasks 37–47 have implementation commits on this branch.
- Key commits:
  - `831228a2` — Tasks 37–39: value kinds, portable Timer, strips/items
  - `59c3b4bd` — Task 40 value surfaces; Task 41 portable default
  - `596b3438`, `38a0c3a3`, `ce0b601f` — Task 43 conversion function, CLI, checks
  - `25814709` — Task 44 conversion offer
  - `7d1e32a4` — Task 46 desktop-only badges/refusal
  - `11573f3a` — Task 47 gate/test updates
- Fast subset after Task 47: **10,773 passed, 6 known baseline failures, 19 skipped**.
- The six failures are pre-existing and unrelated:
  `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
  `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
  `Emit_ReplacesAScriptThatAnotherHandleHasMapped`,
  `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`,
  `SearchSnippets_EmptyQuery_ReturnsAll`,
  `SearchSnippets_WhitespaceQuery_ReturnsAll`.

## Important implementation facts

- Portable Timer constructs from its properties and honors `Enabled`; legacy DOM Timer keeps
  `w.setInterval` and `BL8027` “wired means running”.
- Task 39 strip/item library files are `ControlStrip.bas` and `ControlStripItem.bas`
  (renamed for alphabetical base-type ordering). IDE drop mirrors them.
- `FormCodeConversion.Convert(document, text)` is pure; it restores Timer `Enabled` after
  generation. It adds the three portable `Using` lines in canonical order, `Inherits Form`,
  converts markers to `style="portable"`, rewrites simple `DomEvent` handler signatures, and
  reports DOM uses as `BL8036` / `DomUsageFinding`. Non-simple handlers are `BL8037`.
- `design --convert <form.blwebform>` writes the converted code-behind through the same function.
- Task 44 offers Convert / Not now on opening a DOM-style web form; once per open; refused
  documents are not offered; Not now writes nothing.
- Task 46 lists desktop-only kinds on the web toolbox with `DesktopOnly`/`Badge == "desktop"`,
  allows the drop, exposes `HeaderBadge` on the property grid, and `FormDocumentLoader` refuses
  page generation with `BL8038` / `DesktopOnlyKind` (currently a warning/skip, not a hard error).
- Task 40 style getters cache the values set through the public properties; they do not read
  computed style.

## Remaining validation / known gaps

- Manual click-throughs for Tasks 36, 42, 45, 47 were not performed.
- Task 44 has no full headless IDE-route/notification test suite.
- Task 46 `BL8038` should be a build error, not only a warning.
- Task 43 lacks a CLI-bytes-equals-function-bytes test and a `MixedCodeStyle` regression test.
- Task 40 getters are cached, not computed-style readback.

## Main-branch merge status

`master` is currently far ahead (`origin/master` has 238 commits not in this branch). A direct
merge was attempted and aborted because it conflicts in:

- `BasicLang/Forms/FormControlCatalog.cs` (master has property-grid slices 4–6; branch has
  portable rows)
- `BasicLang/Forms/FormHandlers.cs`, `RegionWriter.cs`, `FormDocumentLoader.cs`
- `BasicLang/Program.cs`
- `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs`
- `VisualGameStudio.Tests/Compiler/{ForEachOverStringTests,FourBackends,JsExecutionTierRosterTests}.cs`
- `docs/HANDOFF.md`
- binary `IDE/*.dll`/`*.exe`

Do **not** merge with `-X ours` or `-X theirs`: either side discards real work. Resolve
`FormControlCatalog` semantically (keep master’s property-grid slice rows AND the portable
`DomMember`/events/Timer rows), then build and rerun the portable gates.

The branch is backed up at `origin/feat/portable-controls` (`11573f3a`). `master` was not
force-pushed.
