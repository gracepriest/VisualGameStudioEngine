# Task 33 preflight: run-time geometry, Anchor, live docking

Plan: `2026-09-29-portable-control-library.md`, Task 33. Spec: §5.8–§5.10, §11b notes 1–2, 4.
Checked against `feat/portable-controls` at `e0d962b6` (clean tree).
This preflight wins where the task outline is stale. No owner decisions needed.

## Anchors and corrections

| Claim | Current anchor | Decision |
|---|---|---|
| `VgsSetStyle` writes inline style | `Control.bas:391-414` writes `el.style.*` directly | replace it with the ONE `<style>` rule writer; every property setter keeps calling `VgsSetStyle` so no setter changes |
| The DOM declarations suffice | `Document.head` is NOT declared; `getBoundingClientRect`, `parentElement`, `offsetWidth/Height`, `appendChild`, `getAttribute`/`setAttribute`, `textContent` are | add `Document.head` to both byte-identical `dom-core.bli` copies; reuse the rest; no library-only members in the declaration file |
| Geometry members exist | none on `Control`; Task 31 added only `VgsClientInset`, `VgsDesignClient*`, `VgsDockPadding` | add `Left/Top/Width/Height/Location/Size/Right/Bottom/Anchor` to `Control.bas` (the class is not partial, so no new file); never duplicate them in the DOM declaration copies |
| The page hook is declared | `lib/js/forms/vgs-page.bli` already declares `VgsDockHook.request/reflow` | consume it lazily through `::window.vgsDock`; do not add a second declaration |
| Portable pages always carry the script | `FormDockScript.PageScript` returns null when `Docks(root)` is false; `AppendCanvasBody` emits it only on Canvas | portable path emits unconditionally on Canvas and Grid/Flow; DOM path keeps the null-when-nothing-docks rule and its byte-identical text |
| The script's data is complete | `Nodes` prunes controls that neither dock nor hold docked children; `DataJson` is design-time only | portable live walk enumerates live child elements each reflow; JSON only seeds the first reflow |
| Requested bounds are emitted | Task 32 emits `data-vgs-x/y/w/h`, `data-vgs-anchor`, `data-vgs-dock`, `data-vgs-inset`, `data-vgs-pad`, `data-vgs-cw/ch` | these are the live inputs. Keep these names; do NOT add `data-vgs-client` or a second alias set. The spec's names are superseded by the shipped Task 32 metadata |
| `data-vgs-w/h` is the docking input | `form.OwnSizeOf` is server-side only | the live walk and the library read/update `data-vgs-w/h`; never measure a docked control's rendered box |
| `FormAnchorCss` is the axis rule | `Positioned(...)` and `Docked(...)` exist; `Axis` is private | expose the axis decision through `Positioned` only; the library mirrors it and a node lock-step gate owns the 16-combination table |
| `vgsDockCss` mirrors `Docked` | both write only the declarations for their edge | live rules gain a completion step writing all six geometry properties, `auto` for unused ones. Design-time CSS stays byte-identical |
| The cascade rule is testable | library inline style currently beats the dock rule | after the rule writer both `<style>` elements are equal-specificity `#id` rules; live dock wins by document order or by `auto` completion. Test both creation orders |
| Visible/Dock/size writes ask for one reflow | no hook caller | `VgsRequestReflow` handles a missing hook loudly; the observer calls `request()`, so a `Visible` write schedules one microtask, never two |
| `Anchor` is page metadata | Task 32 writes `data-vgs-anchor` for every pixel-geometry control, default included | keep "every control" (a read needs the default too); note the superset in the record |
| Breakpoint 0 is unwrapped | `Bootstrap` already omits the media query when `breakpoint <= 0` | reuse it; do not add a second breakpoint reader |
| Container design sizes exist for Canvas | Task 32 emits `data-vgs-cw/ch` only when the designer resolver knows the control | Grid/Flow containers and controls have no pixel geometry, so geometry there stays CSS-flow based; Task 33 does not invent bounds for them |

## Decisions

1. `VgsSetStyle` keeps its signature but writes into one lazily created `<style id="vgs-live">` rule `#<id>{…}`, wrapped in `@media (width >= B)` unless B is 0. The reader is the same rule text; inline `style` attributes are never mutated by the library.
2. Geometry reads use the element's live rectangle in the parent container's client coordinates at desktop width. Below the breakpoint they return the requested `data-vgs-x/y/w/h`; writes there use the container's design client size (`data-vgs-cw/ch`) and land in the media-guarded rule.
3. Docked controls (`data-vgs-dock` other than None) never receive geometry declarations from the library (R2). A size write updates `data-vgs-w/h` and requests a reflow.
4. `Dock = None` restores requested bounds through the library rule; docking asks the script for the live rule. `BringToFront` appends the element to its parent's child list and requests a reflow.
5. All names come from the Task 32 metadata. There is no kind switch in the library or the script.
6. Blanket rule: a container is any element with `data-vgs-cw/ch`; its child elements with `data-vgs-control` are its positioned/docked children. Text nodes and the `<legend>` are not walked.
7. The JS and C# change in one commit: `FormDockLayout` ↔ the script core and `FormAnchorCss.Docked` ↔ `vgsDockCss`. The lock-step fixtures gain the nine live cases, each on a plain Panel, a bordered Panel and a GroupBox.
8. DOM declarations (both copies) gain only what the library calls; they stay byte-identical. No IDE forms drop.

## Implementation and lean gates

1. Preflight, then the rule writer + `Document.head` in both declaration copies + node assertions (one style element, one rule, media guard, values readable).
2. Geometry + Anchor + the axis lock-step fixture; library writes at desktop/phone.
3. Live script: always emit on portable; live walk; `window.vgsDock`; one scheduler; six-property completion; dock/none/bring-to-front.
4. Add the nine live dock fixtures and the R2 creation-order test; CLI-built page with nothing docked still carries the script and hook.
5. Named gate: `FormDockScriptTests`, `PortableAnchorLockStepTests`, `PortableGeometryNodeTests`, `FormAnchorCssTests`, `FormDockLayoutTests`, `FormBuildEmissionTests`, `PixelLayout*` fast, `FormCanvasTransformTests`, `PortableFirstKindsNodeTests`. Then the full fast subset. Integration/Edge assertions stay in Task 35 and are not claimed here.

Compare failures by name with Task 32's six known baseline failures. Stage by name, commit via `git commit -F`. No stash, push or IDE forms drop.
