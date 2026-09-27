# Task 11 pre-flight: Canvas web → WinForms retarget copies geometry and the design size exactly

Plan: `docs/superpowers/plans/2026-09-27-web-pixel-layout.md`, "## Task 11". Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §6.
The plan's anchors were taken at `47797002`. This check was made against `feat/web-pixel-layout` @ **`cfdbe895`** (Tasks 1–10 landed). Read-only; nothing but this file changed.

Follow this document **instead of** the plan's Task 11 section. Where they disagree, this document wins. Everything else in the plan (How to build/test, Commits, Traps) still applies.

**OWNER decisions needed: none.** The reversible calls are listed under D.

---

## A. Anchor verification (claim → today's file:line at cfdbe895 → verdict)

| # | Plan / task claim | Today | Verdict |
|---|---|---|---|
| 1 | `ToPixels` `:624-649`; `Math.Max(800, …)` `:629-630` (spec-claims #1) | `BasicLang/Forms/FormRetarget.cs:631-656`; `Math.Max(MinimumWidth/Height, …)` `:636-637`; the layout finding `:642-648`; the Literal finding `:650-655` (code BL8025) | **moved** (+7), holds |
| 2 | `ToCells` reports the window size lost `:565-566` | `:572-573` | **moved**, holds; unchanged by this task |
| 3 | `DescribeLayout` ≈658-666 | `:658-666`; lists Cols/Rows/Gap/Dir only — a stray `MobileBreakpoint` on a Grid/Flow page's `<Layout>` (the reader reads it on ANY layout, `FormDocumentReader.cs:353`) is dropped with **no mention** | holds; gap confirmed, fixed in Step 5 |
| 4 | `_sourceGeometry` is a `PixelGeometry` clone `:321` | `:328` `_sourceGeometry[control] = Translate(source.Geometry, offset)`; `Translate` `:542-553` clones | **moved**, holds |
| 5 | `Hoist` `:347-368` translates by the container's stored X/Y | `:354-375` (`childOffset` `:356-358`) | **moved**; see B2 (unreachable from a Canvas page) |
| 6 | `ConvertRoot` uses `_toLayout` / `LayoutOf(_source)` (Task 2) | `:211-212`; `_toLayout` set `:184` (Grid for web, null for WinForms) | holds. A Canvas page's `Width`/`Height` are MODELLED (`FormDocumentReader.cs:145-146`), never unknown, so nothing is double-named; a degraded one (`Width="12px"`) stays unknown and is named once by the `fromRow` arm `:218-226` ("dropped rather than carried") |
| 7 | RegionWriter emits Dock/Anchor `:1024-1044` | `BasicLang/Forms/RegionWriter.cs:1015-1023` (Dock, then Anchor, after Location/Size) | **moved**, holds; order unchanged (the destination is an ordinary `.blform`) |
| 8 | `FormRootRetargetTests.Sample` `:16-20` has no Int arm | `VisualGameStudio.Tests/Compiler/FormRootRetargetTests.cs:16-20`; `Sources()` `:27-32` (no Canvas; its doc `:22-26` says Canvas joins in Task 11); the "⚠ RE-CHECK IN SLICE 3" line `:38` | holds |
| 9 | `EveryCatalogKind_Retargets_…` | `FormRetargetTests.cs:1283-1376`, `[Values] FormTarget from` | holds |
| 10 | `FormRetargetPairTests` compiles a pair with csc | `FormRetargetPairTests.cs:233-242` (`TheRetargetedWinFormsPair_CompilesThroughTheRealCompilerAndCsc`, Integration) | holds |
| 11 | (not in plan) CLI entry point | `BasicLang/Program.cs:546` → `ConvertToPair`; fixture `VisualGameStudio.Tests/Compiler/DesignRetargetCliTests.cs` (all Integration) | exists; used in Step 8 |
| 12 | (not in plan) IDE entry point | `SolutionExplorerViewModel.RetargetFormAsync` `:1408-1489` → `ConvertToPair` `:1456`; fixture `VisualGameStudio.Tests/Shell/SolutionExplorerRetargetTests.cs` drives the generated command (fast) | exists; used in Step 8 |
| 13 | (not in plan) `FormCatalogShapes.Canonical` | `VisualGameStudio.Tests/Compiler/FormCatalogShapes.cs:47-49` picks a Positioned control's geometry by `document.Target == Web` — a Canvas page gets a **GridGeometry** | **gap**, see B1 |
| 14 | Scaffold of a Canvas page carries `MobileBreakpoint` | `FormScaffolder.cs:118-127` writes `MobileBreakpoint="600"` explicitly | holds — so EVERY scaffolded Canvas page retargets with exactly one BL8024 |

---

## B. Blockers / corrections (these win over the plan)

### B1: The catalog-sweep fixture cannot produce a Canvas control
- **Evidence.** `FormCatalogShapes.Canonical` `:47-49` keys on `Target`, so a Canvas source gets `GridGeometry {0,0}`. A Canvas-source sweep built on it would retarget a control in the WRONG vocabulary (the reader would never produce that shape), and CLAUDE.md forbids hand-rolling a second shape.
- **Correction.** `Canonical` asks `FormVocabulary.IsPixel(document)`. No existing caller passes a Canvas document without an explicit `geometry:` (`FormAssetEmitterTests.cs:1178` passes one; `:893` is a strip), so nothing else moves.

### B2: The plan's Hoist risk is unreachable from a Canvas page today
- **Evidence.** Every web row in `FormControlCatalog.All` has a `WinFormsType` (no row is declared with a null one), so `definition.SupportsTarget(WinForms)` is true for every kind the reader produces; `Hoist` is reached only by an in-memory control with `Definition == null`.
- **Correction.** No dock-resolved offset is added. Recorded here; the day a web-only container row lands, `Hoist` must translate by `FormDockLayout`'s resolved rect for a docked container.

### B3: "a `<Literal>` is still named (a loss, not layout)" needs a code
- The spec's contract is "NO layout warning" for Canvas → WinForms. Today the Literal is named with BL8025 (`:650-655`).
- **Correction.** On a Canvas source it is named with **BL8024** (`RetargetPropertyLost`, "the page's <Literal> markup … was dropped"). A Grid/Flow source keeps BL8025 (unchanged, `ToWinForms_ReportsTheLayoutAndTheLiteral_AsCrossedLoss` pins 5).

### B4: A stray Grid/Flow attribute on a Canvas page's `<Layout>` would be dropped silently
- The reader reads `Cols`/`Rows`/`Gap`/`Dir` on ANY `<Layout>` (`FormDocumentReader.cs` `ReadLayout`). The Canvas arm skips `DescribeLayout`.
- **Correction.** One BL8024 naming them verbatim ("a Canvas page does not read them"). No BL8025.

---

## C. TDD steps

1. **Fixture (B1).** `FormCatalogShapes.Canonical`: `FormVocabulary.IsPixel(document) ? PixelGeometry{96,80,120,24,Top} : GridGeometry{0,0}`.
2. **Root sweep RED.** `FormRootRetargetTests`: `Sample` gains `FormPropertyType.Int => "480"`; `Sources()` gains `(Web Canvas)`; the doc's RE-CHECK text and `:38`'s line go; `…ExercisesBothArmsItHasRowsFor` gains "a non-layout-edge row on Web Canvas that does not apply to WinForms" (MobileBreakpoint). Expect RED: `(Web Canvas)` — ClientSize comes back `800, 450`, MobileBreakpoint not named.
3. **FormRetargetTests RED** (new region "Task 11 — a Canvas page crosses exactly"):
   - `CanvasToWinForms_IsLossless`: 640×400 page, `MobileBreakpoint="480"`; a Panel with `Dock="Top"` holding a TextBox `Anchor="Top,Left,Right"` and a Button `Anchor="Bottom,Right"`; a top-level Label; a MenuStrip strip. Every control's X/Y/Width/Height/Anchor/Dock at every depth identical; `(Width, Height) == (640, 400)`; `Text == Name`; zero BL8025; exactly one finding, BL8024 naming `'form.MobileBreakpoint'` and `480`; the strip has no geometry.
   - `CanvasToWinForms_ANullDesignSize_StaysNull`.
   - `CanvasToWinForms_TheLiteral_IsNamedAsALoss_NotAtTheLayoutEdge` (B3).
   - `CanvasToWinForms_AStrayGridAttributeOnTheLayout_IsNamed` (B4).
   - `CanvasToWinForms_ADegradedWidth_IsNamedOnce_AndNotCarried` (A6).
   - `CanvasToWinForms_AWiredWebTimer_ArrivesEnabled_AndSaysSo` (BL8027 both ways still applies).
   - `ToWinForms_AStrayMobileBreakpointOnAGridPage_IsNamedInTheLayoutFinding` (DescribeLayout).
   - `EveryCatalogKind_Retargets_FromACanvasPage_CopyingItsGeometryExactly`: the sweep body extracted to a helper taking `(from, layout)`; the Canvas variant also asserts each Positioned control's geometry equals the source's and there is no BL8025 at all.
4. **GREEN** — `ToPixels`:
   ```csharp
   if (FormVocabulary.LayoutOf(_source) == FormLayoutKind.Canvas) { CopyPixels(Document.Controls); Document.Width = _source.Width; Document.Height = _source.Height; Document.Text ??= _source.Name; NameCanvasLosses(); return; }
   ```
   `CopyPixels` walks every depth: a Positioned control takes `_sourceGeometry` as is (already a clone); a Docked/Item control gets null (the Task 26 rule, as `Place` does). `NameCanvasLosses` emits BL8024 for MobileBreakpoint (`'form.MobileBreakpoint' = "480"`), stray Grid/Flow attributes, and the Literal.
5. `DescribeLayout` adds `MobileBreakpoint="…"`.
6. **Pair (Integration).** `FormRetargetPairTests.TheRetargetedCanvasPair_CompilesThroughTheRealCompilerAndCsc` + fast `ACanvasPair_EmitsTheExactClientSizeAndGeometry` (`Me.ClientSize = New Size(640, 400)`, `DockStyle.Top`, `CType(13, AnchorStyles)`), and the Canvas pair added to `ThePairsRegions_AreCanon…`.
7. Nothing in WinForms → web changes (`_toLayout` stays Grid).
8. **Entry points.** CLI: `DesignRetargetCliTests.Cli_DesignRetarget_ACanvasPage_WritesABlformWithTheExactGeometry_AndNoLayoutWarning` (Integration). IDE: `SolutionExplorerRetargetTests.RetargetFormCommand_ACanvasPage_WritesTheExactGeometry_AndPublishesNoLayoutWarning` (fast).

## D. Decisions taken (reversible)
- B3 (Literal → BL8024 on Canvas), B4 (stray Grid attributes named), Docked/Item geometry forced null on the Canvas arm.

## E. Mutation list (apply, see red, restore)
1. Canvas detection in `ToPixels` → `false` (always Place).
2. Exact size → `Math.Max(MinimumWidth, _source.Width ?? 0)`.
3. Geometry copy → drop Anchor (copy with `Anchor = null`).
4. Geometry copy → drop Dock.
5. Geometry copy → top level only (no recursion).
6. MobileBreakpoint finding removed.
7. `DescribeLayout` MobileBreakpoint line removed.
8. Literal on Canvas back to BL8025.
9. Canonical back to Target-keyed.

## F. Gate
Fast subset vs cfdbe895 (8276 / 8270 / 5 / 1; the five known names); `FormRetargetTests`, `FormRootRetargetTests`, `SolutionExplorerRetargetTests` by name; Integration `FormRetargetPairTests`, `DesignRetargetCliTests`, `WinFormsCatalogSweepTests` (Canonical changed).
