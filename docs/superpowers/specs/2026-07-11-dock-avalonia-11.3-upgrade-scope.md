# Scoping: Avalonia 11.1 → 11.3 + Dock.Avalonia 11.3 upgrade

**Status:** EXECUTED 2026-07-12 — merged to master (branch `avalonia-dock-11.3-upgrade`). The spike's
predictions held exactly (2 AXAML breaks, 0 C# errors). One additional runtime regression found and
fixed: Dock 11.3 collapses empty docks more aggressively, flattening MainArea away when every panel
closes — `DockFactory.EnsureMainArea()` now rebuilds it around the live DocumentDock. Suite
2459/2459; manual UI pass verified (editor, docking/float/re-dock, resize, reopen, debug).
Deprecated drag-drop API migration (§3.4) deferred — still warnings only.
**Motivation:** Get the newer Dock's smoother drag-dock / floating-adorner behavior so panels
dock reliably on any drop (not only when dropped on the compass arrows), removing the 11.1.0.2
workarounds. See [[dock-drag-float-behavior]] in auto-memory for the 11.1.0.2 constraints.

## Verdict

**Feasible, and more tractable than first assumed.** A throwaway spike build against 11.3 showed the
**compile blast radius is small and contained** (2 Dock AXAML fixes + deprecation cleanup; our C#
compiled with **zero errors**). The real cost is **manual runtime verification across the whole IDE**,
which cannot be automated (the test suite is logic-only; UI drag/render can't be driven headlessly).

Net: a well-scoped ~**1-day** dedicated branch task, dominated by hands-on testing. **Not urgent** —
docking already works acceptably on 11.1.0.2. Do it when touching the UI anyway or to stay current.

## 1. Feasibility — version matrix (verified on nuget.org)

| Package | Current | Target | Notes |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter, Avalonia.Controls.DataGrid | 11.1.0 | **11.3.13** | all present as 11.3.13 |
| Avalonia.Diagnostics (Debug-only) | 11.1.0 | 11.3.13 | conditional ref, Debug config only |
| Dock.Avalonia, Dock.Model.Mvvm | 11.1.0.2 | **11.3.12.1** | depends on Avalonia ≥ 11.3.12 ✓ |
| Avalonia.AvaloniaEdit | 11.1.0 | **11.3.0** | dep floor is Avalonia **11.0.0** → does NOT block 11.3 (was the biggest worry) |

No version conflicts. Referenced only in `VisualGameStudio.Shell`, `VisualGameStudio.Editor`,
`VisualGameStudio.Tests` (Core/ProjectSystem/BasicLang don't reference Avalonia directly).
Avalonia 12.x exists but is explicitly **out of scope** (larger jump).

## 2. Spike results (real `dotnet build` of Shell against 11.3)

- **C# compiled clean — 0 errors.** `DockFactory` (WireFactory, reopen/region-rebuild, overrides),
  and the editor renderer C# are all API-compatible with Dock 11.3.12.1 / Avalonia 11.3.13. No C#
  rewrite needed — the key unknown, and it came back green.
- **2 AXAML errors, both Dock-specific** (AVLN2000):
  1. `App.axaml:18` — `avares://Dock.Avalonia/Themes/DockFluentTheme.axaml` no longer resolves
     (Dock 11.3 reorganized its themes → update the StyleInclude path/name).
  2. `Resources/Styles/AppStyles.axaml:488` — `ProportionalStackPanelSplitter` moved/renamed in
     Dock 11.3 (this is the splitter min-size style from the 11.1 shrink fix → update the type; and
     re-confirm the `MinimumProportionSize` attached property still exists / same owner).
  - These halt the XAML compiler, so they likely **mask a small tail** of further AXAML fixes that
    only surface once #1/#2 resolve. Pattern is clearly Dock theme/control wiring, not a rewrite.
- **24 `CS0618` deprecation warnings** (warnings, not errors): Avalonia 11.3 deprecated drag-drop APIs
  used by the editor — `DragEventArgs.Data` → `DataTransfer`, `DataFormats.Files/.Text` →
  `DataFormat.File/.Text` in `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (~L5188+).
  Compiles today; should be migrated.
- Other warnings (CA1416 ×64, CS0169 ×12, VSTHRD ×4) are **pre-existing**, not upgrade-related.

## 3. Migration work items

1. Fix the Dock theme StyleInclude in `App.axaml` (new 11.3 path/name). *(small)*
2. Rename `ProportionalStackPanelSplitter` in `AppStyles.axaml` to its 11.3 equivalent; re-verify
   `MinimumProportionSize`. *(small)*
3. Rebuild; fix any masked AXAML errors that surface after #1/#2. *(small–medium, unknown tail)*
4. Migrate the 24 deprecated drag-drop calls in the editor. *(medium; optional but recommended)*
5. Re-verify Dock 11.3 docking behavior and enable its improved drag-adorner setting (the reason for
   the upgrade). Some 11.1 workarounds may become unnecessary — re-check whether `WireFactory`
   (Factory-on-every-dockable) and the tab-bar `IsDockTarget` styles are still needed. *(small config,
   medium testing)*
6. Full manual UI verification (see risk table). *(the bulk of the effort)*

## 4. Risk

| Area | Risk | Why |
|---|---|---|
| Package resolution | 🟢 Low | Clean matrix, confirmed |
| C# compile | 🟢 Low | Spike: 0 CS errors |
| AXAML compile | 🟡 Medium | 2 known breaks + a likely small masked tail |
| **Runtime / visual** | 🔴 **The real risk** | Editor renderers (color swatches / inlay hints / execution line / folding / minimap / IntelliSense popups), docking UX, popups/adorners, every panel & dialog. Only manual testing catches these — the 2459-test suite is logic-only, and the agent cannot drive desktop drag/render. |

## 5. Recommended approach

Dedicated branch session, **not** bundled into feature work:

1. Branch; bump all packages to the matrix in §1.
2. Fix the 2 AXAML breaks (§3.1–2); rebuild until XAML compiles clean.
3. `dotnet test` (2459) — catches logic regressions.
4. **Manual UI pass**: editor (type/scroll/fold/color-swatch/inlay/debug-exec-line), docking
   (drag/dock via arrows, float pop-out + re-dock, resize, reopen after mass-close), debugging,
   every panel and dialog.
5. Wire the new Dock drag-adorner setting; prune now-unnecessary 11.1 workarounds if any.
6. Migrate the deprecated drag-drop APIs (§3.4).
7. Deploy prebuilt IDE binaries; commit (feature + binary-refresh, per repo convention).

Budget ~1 day, most of it hands-on verification. Gate merge on the manual pass.

## Appendix — how the spike was run (reproduce)

`dotnet add <Shell/Editor.csproj> package <id> --version <target>` for the §1 matrix (skip the
Debug-only Diagnostics ref for a Release spike), then `dotnet build Shell -c Release`, capture errors,
then `git checkout -- *.csproj` to revert. Nothing was committed.
