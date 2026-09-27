# Web forms laid out in pixels (piece 1) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give web forms the WinForms way of being designed. A web form can be laid out in pixels (`<Layout Kind="Canvas">`), and new web forms use it by default. It stores what a `.blform` stores (a design size, and per control `X`/`Y`/`Width`/`Height`/`Anchor`/`Dock`). The canvas, placement and property grid treat it like a WinForms form. Its generated page matches the design to the pixel at the design size, follows each control's Anchor/Dock when the browser is resized, scrolls rather than squashes below the design size, and stacks into one column below a phone breakpoint. The real WinForms window is the reference for resize behaviour.
**Architecture:** Five things each get exactly one answer. (1) `FormVocabulary.IsPixel(target, layout)` decides whether a document speaks pixels, and every site that used to decide by target alone now asks it. (2) `FormRootValues.Applies(row, target, layout)` decides which Form rows exist on a document. It reads a new per-row `WebLayouts` facet and replaces every `AppliesTo` over `FormRoot` rows. (3) `FormDocument.DesignSize` decides how big the form is, and the Shell's `SurfaceSize` becomes a thin adapter over it. (4) `FormDockLayout.Resolve` decides where every DOCKED thing sits: strips and docked controls, in one document-ordered sequence. The canvas's `Bands`/`BoundsOf` and the page emitter both call it. (5) Pure functions own the page's CSS decisions: `FormAnchorCss` (Anchor/Dock → CSS, including the WinForms centring formula) and `FormReadingOrder` (phone stacking order). The emitter and canvas only consume them. Canvas → WinForms retarget produces the WinForms program that the resize tests compare against. Edge headless loads the generated site from a loopback server and is the layout oracle.
**Tech Stack:** C# / .NET 8, Avalonia 11.3.13, NUnit + Avalonia.Headless (Skia), BasicLang compiler (WinForms via csc; JavaScript backend via node), Microsoft Edge headless (Windows-only layout oracle)
---

## How to read this plan

**Granularity decision (the plan owner's). It is stated here so nobody re-litigates it.**

- **Foundation tasks** (Tasks 1–8): full TDD step granularity with complete code. They are:
  - the vocabulary helper and every site that moves onto it;
  - the root-row layout predicate, ClientSize on Canvas and `MobileBreakpoint`;
  - the scaffold default;
  - the pure dock resolver;
  - the pure Anchor → CSS mapping;
  - the pure reading-order grouping.
- **Integration tasks** (Tasks 9–16): written at TASK granularity: files, responsibilities, the tests each must add, risks, gate. They are:
  - the canvas and placement;
  - the emitter's Canvas page;
  - the retarget;
  - the WinForms reference harness;
  - the Edge harness;
  - the acceptance twin;
  - mutation checks;
  - the gate.
- **Each integration task is expanded into full steps just before it starts, after a pre-flight against the tree as the foundation tasks leave it.**

The reason: anchors drift once earlier tasks land. The property-grid plan's later slices needed a pre-flight (`docs/superpowers/plans/2026-09-26-property-grid-slice2-preflight.md`), and that pre-flight found **4 blockers**. Each was a planned replacement that would have undone a rule an earlier slice had just put in place. An integration task written today would be written against a tree that Tasks 1–8 are about to change: `FormCanvasTransform.Layout`, `Bands` and `BoundsOf` all change their inputs in Task 6.

Every `file:line` anchor below was verified by reading the file on `feat/web-pixel-layout` at **`47797002`**. **Re-verify each anchor before editing.** If a line has moved, find the code by its quoted text, never by the number.

Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` (cited as "spec §N"; the programme decisions are §0, the review notes §7a).

### How to build and run tests (used by every task)

PowerShell, from the repo root. `$sp` is your session scratchpad directory.

```powershell
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~X`" > `"$sp\run.txt`" 2>&1"
```

Then Read `$sp\run.txt`.
- ⛔ Both streams are captured (`2>&1` inside `cmd`, never on a native exe in PowerShell 5.1).
- ⛔ A "Passed!" line is not the verdict. Read the counts and the failure names.
- ⛔ A passing test prints NOTHING at normal verbosity. To see that a named test RAN, re-run it alone with `--no-build --filter` and check the count.
- ⛔ The Bash tool is banned (the hook blocks it, and it opens wsl.exe). Use Read/Grep/Glob/Edit/Write, and PowerShell only for build/test/git.

### Commits (every task)

Write the message with the Write tool to `$sp\taskN-commit.txt`. End it with:

```
Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

Stage **by name** (`git add <each file>`, never `git add -A`, never `csc.dll`), run `git status`, then `git commit -F "$sp\taskN-commit.txt"`. Never round-trip a repo file through `Get-Content`/`Set-Content`.

### Scope calls made while anchoring (deviations from the spec's wording; each is deliberate)

| # | Spec says | This plan does | Why |
|---|---|---|---|
| S1 | `MobileBreakpoint` is an Int, and an unparseable value "keeps its RAW text and the writer NEVER removes the attribute because the model holds null" (§2.3) | `FormLayout.MobileBreakpoint` is a **`string?` holding the raw text**, like `Cols`/`Rows`/`Gap`. Parsed on demand by `FormLayout.TryParseMobileBreakpoint` / `EffectiveMobileBreakpoint`. The Degraded tier comes from the reader, as ClientSize's does. | With raw storage, "present but unusable" is never null in the model, so `SetAttributeIfChanged`'s remove-on-null cannot delete it **by construction**. There is no guard to forget. It also lets the row be Reset, since null really does mean absent. ClientSize needed its guard only because its storage is two ints. |
| S2 | "Add a per-row layout applicability evaluated together with the target through ONE predicate" (§2.3) | `FormPropertyDef` gains `WebLayouts` (the web layouts a row applies to; null = all). ClientSize's `Targets` become `{WinForms, Web}` + `WebLayouts {Canvas}`. Cols/Rows `{Grid}`, Gap `{Grid, Flow}`. **Every** reader of a `FormRoot` row's applicability moves to `FormRootValues.Applies`. | Once ClientSize targets the web, a bare `row.AppliesTo(Web)` is **true for ClientSize on a Grid page**, and every such caller is wrong. The spec lists the reader, writer, region writer, grid and retarget. Anchoring found two more (`FormFile.TierOfRoot`, `FormRetarget.cs:215`) and one test that goes red (spec-claims #3). Task 2 moves all of them in one commit. A catalog gate refuses `WebLayouts` on a control row, where nothing would ever read it. |
| S3 | `SurfaceSize` "Unaffected (verified)" (§2.4) | The 400×300 fallback moves to **`FormDocument.DesignSize`** (BasicLang). `FormCanvasTransform.SurfaceSize` returns it as an Avalonia `Size`. | The emitter (anchors, minimum size) and `FormDockLayout` live in BasicLang and cannot call a Shell method. Without the move, the page would carry a second copy of "how big is the form", which is the exact mirrored-pair scar `SurfaceSize` exists to prevent. Behaviour is unchanged. |
| S4 | §2.1 (vocabulary) then §2.3 (root rows) | Task order: the vocabulary helper (1) → **the root predicate + reader pre-scan + root size on Canvas** (2) → control vocabulary + clipboard (3) → MobileBreakpoint (4). | The reader's "is this root attribute known?" test is `RowForAttribute`, which needs the layout. So the §2.2 pre-scan and the §2.3 predicate must land together, or a Canvas page's `Width` is both modelled AND kept as an unknown attribute for a commit. |
| S5 | New web forms are Canvas; existing tests that assume Grid "pin Grid explicitly" (§2.5) | `FormScaffolder.Create(name, target = Web, webLayout = Canvas)`. A test pins Grid with one argument. Flow has no scaffold (refused). | One argument per pinned test. The production caller (`SolutionExplorerViewModel.cs:1335`) takes the default, so the parameter is not a thing with no caller. |
| S6 | "A paste between documents of DIFFERENT layout is REFUSED with a message naming both layouts" (§2.1) | New `FormClipboard.Paste(xml, target, layout, isTaken)` returns `FormPasteResult(Controls, Refusal)`. It refuses across VOCABULARY (`FormVocabulary.IsPixel` differs: Canvas ↔ Grid/Flow), not across layout name. ⚠ Changed on Task 3 review: Grid ↔ Flow stays allowed (both are cells, lossless, accepted before). Fragments record `Layout=` for the web. The 3-argument `DeserializeSubtree` stays as a shim (13 existing call sites in tests). The VM reports the refusal through its existing `ReportPlacementRefusal`. A cross-TARGET paste, silently empty today, is now reported too. | The refusal needs a way back to the user. `DeserializeSubtree` returns only a list, and an empty list already means "malformed fragment". `FormDesignerCommandTests.PastingAWinFormsSubtreeIntoAWebFormIsRefused` stays green: it asserts nothing landed and the text is unchanged. |
| S7 | The tests to pin to Grid are `FormDesignerLayoutRealViewTests`, `FormCodeBehindWriteTests`, `FormHandlerGestureTests`, and drop-into-cell acceptance paths (§2.5) | Pinned: `FormDesignerAcceptanceTests.cs:114`, `FormComponentAcceptanceTests.cs:58`, `FormMenuAcceptanceTests.cs:54`, `FormDesignerCommandTests.cs:512`. | Measured (spec-claims #6). The three named files already carry explicit Grid documents and take only `CodeText`/file names from the scaffold. The four above build their web page FROM the scaffold. The first drops Label/TextBox/Button, which `PlaceOnWeb` refuses on Canvas until Task 9. The other three are web acceptance paths whose page would otherwise go through the half-built Canvas emitter arm. |
| S8 | Reading order "grouped into ROWS by vertical overlap; left to right within a row" (§5) | A control joins the current row when its top is above the row's running bottom (the union of the row so far). A zero- (or negative-) height control counts as 1px tall. **Refined, owner decision 2026-09-27 (greedy rule, after re-review):** within a row, remove members TALLEST first (equal heights: document order) until the members left fall into two or more rows under the same union rule; the removed members are the row's SPANNING members. If the members left never split, there is none. (Not "its span contains every top": a logo 4px below the first box, or a heading above the fields, defeated that. Not "removing it alone splits the rest": two tall members side by side defeat that.) With no spanning member, the row is X, then Y, then document order. With spanning members, those are sorted by X (then Y, document order); every other member goes in the gap its X falls in (an equal X goes AFTER the spanning member); each gap is ordered by the whole function again (its own rows), emitted gap 0, spanning 1, gap 1, … . `FormReadingOrder`'s class summary is the exact statement. | This had to be pinned exactly for the table tests. The union rule keeps a label slightly higher than its box in the box's row. The spanning refinement stops a tall sibling turning the controls beside it into COLUMNS: a logo beside label/box pairs gave `logo, userLabel, passLabel, userBox, passBox` before it, and gives `logo, userLabel, userBox, passLabel, passBox` now. A tall control on the right comes after the pairs, by X. |
| S9 | "exactly as WinForms docks them" (§4) | `Fill` takes the remaining rectangle and does **not** consume it, as WinForms' `DefaultLayout` does. ⚠ Changed on Task 6 review: the remaining rectangle is **not** clamped. Each docked control's height/width is subtracted from it with no floor (`remainingBounds.Height -= element.Bounds.Height`), so after an overflowing Top a Bottom still sits on the container's real bottom edge (300px form, Top 400, Bottom 50 → Bottom at Y=250), and likewise Left then Right. Only a size **handed to a control** is clamped at 0: a Fill's two sizes, and the across-axis size of a Top/Bottom/Left/Right. A position may go negative. `FormDockMode.Runtime` skips a `Visible=false` control (`ParticipatesInLayout`); `Designer` docks it. | This follows WinForms' `DefaultLayout` **as read in its source, not yet run**. **The §7 reference harness (Task 12/13) is the arbiter.** It must include "an overflowing Top, then a Bottom" and "an overflowing Left, then a Right". If it disagrees, the harness wins and the Task 6 table row changes (Traps). |
| S10 | (not in spec) | `FormAnchor.Parse`/`Split` (Task 7) becomes the ONE anchor parser. `RegionWriter`'s private `AnchorFlags`/`SplitAnchor` move onto it. | The page reads Anchor too, and two parsers of one attribute is a mirrored pair. Side effect: `"Left,Left"` now ORs to Left (4). The old sum (8) was AnchorStyles.Right, a latent defect. |
| S11 | (not in spec) | `FormDockLayout.EdgeOf` reads `PixelGeometry.Dock` trimmed and case-insensitively. | The region writer emits `DockStyle.{Dock.Trim()}` verbatim, so a lowercase value is a pre-existing csc failure on WinForms (Traps). The page and canvas are both served by the one resolver, so they cannot disagree with each other. |
| S12 | Stacking order: emit in document order (§3) | Strips are emitted in document order too (WinForms z-order: a strip first in the document is at the back). | The canvas paints bands LAST, on top (`FormCanvasTransform.cs:375-376`). For a control overlapping a strip, the canvas and the page/WinForms therefore disagree. This is pre-existing canvas behaviour and is recorded (Risks, Task 10), not changed here. |

**No question here is architecture-altitude** (backends, IR, C++ std, engine sync, IDE layering). Every call above is feature-internal and reversible. There is no architect consultation; the assumptions are recorded here. `docs/superpowers/decisions/` holds nothing on form layout.

### Spec claims found FALSE or incomplete while anchoring (recorded, not worked around silently)

1. **§6 "root `Width`/`Height` handling in `ConvertRoot` is target-gated".** It is not in `ConvertRoot`. `ConvertRoot` (`FormRetarget.cs:189-257`) handles only unknown root attributes, through `RowForAttribute`. The window size is derived in `ToPixels` (`:629-630`, `Math.Max(800, …)`) and reported lost in `ToCells` (`:565-566`). Task 11 changes `ToPixels`.
2. **§2.3 "Needs a WinForms-oracle exemption (web-only row) in the parity test".** False: none is needed. The parity test compares only WinForms rows (`WinFormsCatalogParityTests.cs:71`, `:108`, `:155` all filter `AppliesTo(FormTarget.WinForms)`), so a web-only `MobileBreakpoint` is never compared. An `OracleExemption` on it would be dead text: `EveryOracleExemption_StillSuppressesAFinding` filters it out at `:155`, so nothing could ever prove it stale.
3. **§2.3's list of predicate callers is incomplete.**
   - `FormFile.TierOfRoot` (`FormFile.cs:111-112`) and `FormRetarget.cs:215` (`fromRow.AppliesTo(_to)`) also filter `FormRoot` rows by target, and are not listed.
   - Moving ClientSize onto the web turns **`FormRootRetargetTests.EveryFormRootRow_CrossesOrIsNamed_ExercisesBothArmsItHasRowsFor` (`:87`) RED** as written: ClientSize is then a layout-edge row that `AppliesTo(Web)`.
   - Task 2 moves all of them and rewrites the guard.
4. **§2.1 "`FormClipboard`'s mirrored `ReadGeometry` (… keyed on `Target` at `:308`)".** `:308` is the whole-target refusal in `DeserializeSubtree`. The vocabulary choice inside `ReadGeometry` is `FormDocument.cs:546`. Both change in Task 3.
5. **§2.4 "Unaffected (verified): `SurfaceSize`".** True of today's code, but the page emitter and `FormDockLayout` (BasicLang) need the same answer and cannot reach a Shell method. See scope call S3: the fallback moves, and `SurfaceSize` delegates.
6. **§2.5's list of tests that assume a Grid scaffold is wrong in both directions.**
   - `FormCodeBehindWriteTests` (`:148`), `FormHandlerGestureTests` (`:80-83`) and `FormDesignerLayoutRealViewTests` (`:51`) already carry explicit Grid documents and need no change.
   - The tests that really build their page from the web scaffold are `FormDesignerAcceptanceTests.cs:114`, `FormComponentAcceptanceTests.cs:58`, `FormMenuAcceptanceTests.cs:54` and `FormDesignerCommandTests.cs:512` (scope call S7).
7. **§2.2 (pre-scan) and a pre-existing divergence.** The reader keeps the **LAST** `<Layout>` (the element loop overwrote `model.Layout` for each one, `FormDocumentReader.cs:162-164`). The writer edits the **FIRST** (`ApplyLayout` uses `root.Element("Layout")`, `FormDocumentWriter.cs:262`).
   - The pre-scan keeps LAST, so reading is unchanged.
   - The writer's FIRST is out of scope and recorded here.
8. **§7a "existing `FormCanvasTransformTests` / `FormCanvasRenderTests` expectations for docked fixtures change INTENTIONALLY".** At `47797002`, no canvas test fixture carries a positioned `Dock`. Grep of every test for `Dock="Top|Bottom|Left|Right|Fill"` and `"Fill"` finds only strips' `Dock` properties and non-canvas files (`FormAnchorEmissionTests.cs:132`, `FormAnchorDockPickerTests.cs:260/291`). The expected-change list is **empty today**. Task 9's pre-flight re-greps.
9. **§7 item 2 "a recursive walk … for every control".** The generated WinForms code **never sets `Control.Name`** (open chip `task_fa51e644`). So the reference driver cannot identify controls by `Name` the way `FormDesignerAcceptanceTests.cs:436` prints them. Task 12's driver finds each control through its generated FIELD, by reflection on the form's private fields named by `Id`.
10. **§3 stacking order: incomplete.** Document order is right for the DOM against WinForms. But the canvas paints strip bands LAST, on top (`FormCanvasTransform.cs:375-379`), so a control overlapping a strip is drawn differently on the canvas than on the page. This is pre-existing and not addressed by the spec (scope call S12).
11. **§3/§4 "A container is its own positioned box; its children use coordinates relative to it", and a docked child docks inside it: incomplete.** A WinForms `GroupBox` (and `TabControl`/`SplitContainer` pages) lays children out in its `DisplayRectangle`, which is inset by the caption and border. The resolver and the page treat a container's client area as its bounds. Fixtures use `Panel` (no border). GroupBox docking is a recorded gap for the harness to measure (Risks, Task 12).

---

## Task 0: Pre-flight and baseline

**Files:** none modified.

- [ ] **Step 1: Confirm the base.** `git -C C:\Users\melvi\source\repos\VisualGameStudioEngine log --oneline -1` must print `47797002 …`. `git status` must show only `?? csc.dll` (never stage it).
- [ ] **Step 2: Build and record the baseline.**

```powershell
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter TestCategory!=Integration > `"$sp\baseline-fast.txt`" 2>&1"
cmd /c "dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --filter `"FullyQualifiedName~FormDesignerAcceptanceTests|FullyQualifiedName~FormComponentAcceptanceTests|FullyQualifiedName~FormMenuAcceptanceTests|FullyQualifiedName~FormBuildEmissionTests|FullyQualifiedName~FormAnchorEmissionTests|FullyQualifiedName~WinFormsCatalogSweepTests|FullyQualifiedName~FormRetargetPairTests`" > `"$sp\baseline-int.txt`" 2>&1"
```

Record, **with the base (`feat/web-pixel-layout` @ `47797002`)**, for both runs: total / passed / failed / skipped, and the SORTED failure NAMES.

The known machine failures in the fast subset are:
- `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`
- `Emit_ReplacesAScriptThatAnotherHandleHasMapped`
- `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind` (intermittent)
- `SearchSnippets_EmptyQuery_ReturnsAll`
- `SearchSnippets_WhitespaceQuery_ReturnsAll`

Any other name is new at this base and goes in the record too. ⚠ The JS web-build Integration rows may fail deterministically on this machine with `ERROR_USER_MAPPED_FILE` (auto-memory, operational rules). If they do, their names go into `baseline-int.txt`'s record, so every later gate compares by NAME.

- [ ] **Step 3: Re-verify every anchor Tasks 1–8 cite** (search by the quoted code). If one moved, note it in that task's checkbox before starting.

---

## Task 1: `FormVocabulary`, the one "does this document speak pixels?" answer

Spec §2.1. A pure helper that takes VALUES, because the reader calls it before any model exists, plus a document convenience.

**Files:**
- Create: `BasicLang/Forms/FormVocabulary.cs`
- Create: `VisualGameStudio.Tests/Compiler/FormVocabularyTests.cs`

- [ ] **Step 1: Write the failing test.** `VisualGameStudio.Tests/Compiler/FormVocabularyTests.cs`:

```csharp
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.1 — a document's layout VOCABULARY (pixels, or cells) is decided by
/// (target, layout) through ONE helper. Every site that used to decide by target alone asks it.
/// </summary>
[TestFixture]
public class FormVocabularyTests
{
    [TestCase(FormTarget.WinForms, null, true)]
    [TestCase(FormTarget.WinForms, FormLayoutKind.Grid, true)]
    [TestCase(FormTarget.Web, null, false)]
    [TestCase(FormTarget.Web, FormLayoutKind.Grid, false)]
    [TestCase(FormTarget.Web, FormLayoutKind.Flow, false)]
    [TestCase(FormTarget.Web, FormLayoutKind.Canvas, true)]
    public void IsPixel_IsWinForms_OrAWebCanvas(FormTarget target, FormLayoutKind? layout, bool expected) =>
        Assert.That(FormVocabulary.IsPixel(target, layout), Is.EqualTo(expected));

    [Test]
    public void LayoutOf_AWebDocumentWithNoLayout_IsGrid_AsFormLayoutDefaults()
    {
        var page = new FormDocument { Target = FormTarget.Web, Name = "P" };

        Assert.Multiple(() =>
        {
            Assert.That(FormVocabulary.LayoutOf(page), Is.EqualTo(FormLayoutKind.Grid));
            Assert.That(new FormLayout().Kind, Is.EqualTo(FormLayoutKind.Grid),
                "the two defaults are one fact — a page with no <Layout> is a Grid page");
            Assert.That(FormVocabulary.IsPixel(page), Is.False);
        });
    }

    [Test]
    public void LayoutOf_ACanvasPage_IsCanvas_AndThePageSpeaksPixels()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "P", Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        Assert.Multiple(() =>
        {
            Assert.That(FormVocabulary.LayoutOf(page), Is.EqualTo(FormLayoutKind.Canvas));
            Assert.That(FormVocabulary.IsPixel(page), Is.True);
        });
    }

    [Test]
    public void LayoutOf_AWinFormsDocument_IsNull_EvenWithAStrayLayout()
    {
        var window = new FormDocument
        {
            Target = FormTarget.WinForms, Name = "W", Layout = new FormLayout { Kind = FormLayoutKind.Grid }
        };

        Assert.Multiple(() =>
        {
            Assert.That(FormVocabulary.LayoutOf(window), Is.Null, "a window has no <Layout> (D3)");
            Assert.That(FormVocabulary.IsPixel(window), Is.True);
        });
    }
}
```

- [ ] **Step 2: Build. Expect a BUILD failure.** `dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release` fails with `CS0103: The name 'FormVocabulary' does not exist`. That is the right reason: the helper does not exist yet.

- [ ] **Step 3: Implement.** `BasicLang/Forms/FormVocabulary.cs`:

```csharp
namespace BasicLang.Forms;

/// <summary>
/// ⛔⛔ THE one answer to "is this document laid out in PIXELS or in CELLS?" (spec 2026-09-27 §2.1).
///
/// <para>A <c>.blform</c> always speaks pixels. A <c>.blwebform</c> speaks pixels when its
/// <c>&lt;Layout Kind="Canvas"&gt;</c> says so, and cells (Grid) or document order (Flow) otherwise.
/// Every site that decides a vocabulary asks HERE: the reader's root size and geometry, the writer,
/// <see cref="FormControlCatalog.IsStructural"/>, the clipboard, the canvas and placement. Before
/// this existed, each of them asked <c>Target == WinForms</c>, which silently routed every web
/// document down the Grid path.</para>
///
/// <para>⚠ It takes VALUES, not a document: the reader decides the vocabulary of the root
/// attributes before any model exists (§2.2's pre-scan hands it the layout).</para>
/// </summary>
public static class FormVocabulary
{
    /// <summary>True for WinForms, or for a web document laid out <see cref="FormLayoutKind.Canvas"/>.</summary>
    /// <param name="layout">The web document's layout; ignored for WinForms. Null on the web means Grid.</param>
    public static bool IsPixel(FormTarget target, FormLayoutKind? layout) =>
        target == FormTarget.WinForms || layout == FormLayoutKind.Canvas;

    /// <summary>
    /// The document's layout in the sense <see cref="IsPixel(FormTarget, FormLayoutKind?)"/> and
    /// <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> take it: null for
    /// WinForms (a window has no <c>&lt;Layout&gt;</c>), and on the web the document's own kind — Grid when
    /// it carries none, exactly as <see cref="FormLayout.Kind"/> defaults.
    /// </summary>
    public static FormLayoutKind? LayoutOf(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Target == FormTarget.Web ? document.Layout?.Kind ?? FormLayoutKind.Grid : null;
    }

    /// <summary><see cref="IsPixel(FormTarget, FormLayoutKind?)"/> for a document.</summary>
    public static bool IsPixel(FormDocument document) => IsPixel(document.Target, LayoutOf(document));
}
```

- [ ] **Step 4: Run it. Expect green.** Build, then filter `FullyQualifiedName~FormVocabularyTests`. Expect 9 passed (six `TestCase` rows and three tests).

- [ ] **Step 5: Commit.** Message `feat(forms): FormVocabulary — one answer to "pixels or cells" by (target, layout) (spec 2026-09-27 §2.1)`. Stage `BasicLang/Forms/FormVocabulary.cs` and `VisualGameStudio.Tests/Compiler/FormVocabularyTests.cs`.

---

## Task 2: The root rows by (target, layout), the `<Layout>` pre-scan, and a Canvas page's design size

Spec §2.1 (root `Width`/`Height`, known-root filtering, writer root size), §2.2 (pre-scan), §2.3 (the one predicate, ClientSize on Canvas, Gap on Grid+Flow). Scope calls S2, S4.

**Files:**
- Modify: `BasicLang/Forms/FormControlCatalog.cs`: the `FormPropertyDef` record (`:146-162`, parameter docs above at `:100-145`); the `FormRoot` rows (`:1953-1969`)
- Modify: `BasicLang/Forms/FormRootValues.cs`: class doc (`:3-17`); `RowForAttribute` (`:111-120`); new `Applies` overloads
- Modify: `BasicLang/Forms/Serialization/FormDocumentReader.cs`: `:110-154` (Text, root size, root attributes); `:160-164` (the `Layout` case); `IsKnownRootAttribute` (`:284-309`)
- Modify: `BasicLang/Forms/Serialization/FormDocumentWriter.cs`: `Create` `:89-101`; `ApplyToDocument` `:183-193`
- Modify: `BasicLang/Forms/Serialization/FormFile.cs`: `TierOfRoot` `:111-114`
- Modify: `BasicLang/Forms/RegionWriter.cs`: `:637`
- Modify: `BasicLang/Forms/FormRetarget.cs`: `Conversion` fields/ctor `:160-179`; `ConvertRoot` `:202-219`
- Modify: `BasicLang/Forms/FormDocument.cs`: the `Width`/`Height` doc comments `:49-53`
- Modify: `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs`: `:479`
- Modify (tests): `VisualGameStudio.Tests/Compiler/FormRootTests.cs` (`:58-64`, `:281`); `VisualGameStudio.Tests/Compiler/FormRootRetargetTests.cs` (`:28-90`); `VisualGameStudio.Tests/Compiler/FormPropertyGridTests.cs` (`:466`, Step 9's tighten)
- Create: `VisualGameStudio.Tests/Compiler/FormRootLayoutTests.cs`

- [ ] **Step 1: Write the failing test file.** `VisualGameStudio.Tests/Compiler/FormRootLayoutTests.cs`:

```csharp
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.2/§2.3 — which FORM rows a document has is decided by (target, layout) through ONE
/// predicate, <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>; the reader
/// pre-scans the root's &lt;Layout&gt; so it can read the root in the right vocabulary; a Canvas page stores
/// and edits a design size exactly as a .blform does.
/// </summary>
[TestFixture]
public class FormRootLayoutTests
{
    private const string CanvasPage = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls/>
        </WebForm>
        """;

    private const string CanvasPageLayoutLast = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Controls/>
          <Layout Kind="Canvas"/>
        </WebForm>
        """;

    private const string GridPageWithAWidth = """
        <WebForm Name="F" Version="1" Width="640">
          <Layout Kind="Grid" Cols="auto" Rows="auto"/>
          <Controls/>
        </WebForm>
        """;

    private static FormPropertyDef Row(string name) => FormControlCatalog.FormRoot.Property(name)!;

    // ⚠ These expected sets are the SPEC's decisions (§2.3), not a list of catalog kinds. ⚠ RE-CHECK IN
    // TASK 4: MobileBreakpoint joins the Canvas row.
    private static IEnumerable<TestCaseData> Documents()
    {
        yield return new TestCaseData(FormTarget.WinForms, null, new[] { "Text", "ClientSize" })
            .SetName("{m}(WinForms)");
        yield return new TestCaseData(FormTarget.Web, null, new[] { "Text", "Cols", "Rows", "Gap" })
            .SetName("{m}(Web, no Layout)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Grid, new[] { "Text", "Cols", "Rows", "Gap" })
            .SetName("{m}(Web Grid)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Flow, new[] { "Text", "Gap" })
            .SetName("{m}(Web Flow)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Canvas, new[] { "Text", "ClientSize" })
            .SetName("{m}(Web Canvas)");
    }

    [TestCaseSource(nameof(Documents))]
    public void TheRootRowsThatApply_FollowTheTargetAndTheLayout(
        FormTarget target, FormLayoutKind? layout, string[] expected) =>
        Assert.That(
            FormControlCatalog.FormRoot.Properties
                .Where(r => FormRootValues.Applies(r, target, layout))
                .Select(r => r.Name),
            Is.EqualTo(expected));

    [Test]
    public void TheDocumentOverload_AsksTheSamePredicate()
    {
        var documents = new[]
        {
            new FormDocument { Target = FormTarget.WinForms, Name = "W" },
            new FormDocument { Target = FormTarget.Web, Name = "P" },
            new FormDocument { Target = FormTarget.Web, Name = "C", Layout = new FormLayout { Kind = FormLayoutKind.Canvas } },
            new FormDocument { Target = FormTarget.Web, Name = "L", Layout = new FormLayout { Kind = FormLayoutKind.Flow } },
        };

        Assert.Multiple(() =>
        {
            foreach (var document in documents)
            {
                foreach (var row in FormControlCatalog.FormRoot.Properties)
                {
                    Assert.That(FormRootValues.Applies(row, document),
                        Is.EqualTo(FormRootValues.Applies(row, document.Target, FormVocabulary.LayoutOf(document))),
                        $"form.{row.Name} on {document.Name}");
                }
            }
        });
    }

    /// <summary>
    /// ⛔ Only <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> reads
    /// WebLayouts. On a control row nothing would ever read it — a facet that silently does nothing.
    /// </summary>
    [Test]
    public void WebLayouts_IsDeclaredOnlyOnFormRootRows_ThatExistOnTheWeb()
    {
        var onControls = FormControlCatalog.All
            .SelectMany(d => d.Properties.Where(p => p.WebLayouts != null).Select(p => $"{d.Kind}.{p.Name}"))
            .ToList();
        var inert = FormControlCatalog.FormRoot.Properties
            .Where(p => p.WebLayouts != null && !p.AppliesTo(FormTarget.Web))
            .Select(p => p.Name)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(onControls, Is.Empty, "no control consumer reads WebLayouts");
            Assert.That(inert, Is.Empty, "a web layout list on a row that does not exist on the web is never consulted");
        });
    }

    [Test]
    public void RowForAttribute_AsksThePredicate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.Web, FormLayoutKind.Canvas)?.Name,
                Is.EqualTo("ClientSize"));
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.Web, FormLayoutKind.Grid), Is.Null);
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.Web, null), Is.Null, "no <Layout> is Grid");
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.WinForms, null)?.Name, Is.EqualTo("ClientSize"));
        });
    }

    [Test]
    public void ACanvasPage_ModelsItsDesignSize_AndRoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Message)));
            Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((640, 480)));
            Assert.That(file.Model.UnknownAttributes, Does.Not.ContainKey("Width").And.Not.ContainKey("Height"),
                "modelled, so it is never also written back as an unknown attribute");
            Assert.That(FormRootValues.Get(file.Model, Row("ClientSize")), Is.EqualTo("640, 480"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(CanvasPage), "a no-op is byte-identical");
        });
    }

    [Test]
    public void TheLayoutIsPreScanned_WhenItFollowsTheControls()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPageLayoutLast);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Layout?.Kind, Is.EqualTo(FormLayoutKind.Canvas));
            Assert.That(file.Model.Width, Is.EqualTo(640),
                "the root is read in the Canvas vocabulary although <Layout> comes after <Controls>");
            Assert.That(file.Model.UnknownAttributes, Does.Not.ContainKey("Width"));
            Assert.That(file.Model.UnknownChildren, Is.Empty, "<Layout> is read once, never kept as an unknown element");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(CanvasPageLayoutLast));
        });
    }

    [Test]
    public void AGridPagesWidth_StaysAnUnknownAttribute_AndRoundTrips()
    {
        var file = FormDocumentReader.Read("F.blwebform", GridPageWithAWidth);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Width, Is.Null, "a Grid page has no design size (D3)");
            Assert.That(file.Model.UnknownAttributes["Width"], Is.EqualTo("640"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(GridPageWithAWidth));
        });
    }

    [Test]
    public void AnUnparseableCanvasWidth_IsADegradedClientSize_AndPreserved()
    {
        const string text = """
            <WebForm Name="F" Version="1" Width="12px" Height="480">
              <Layout Kind="Canvas"/>
              <Controls/>
            </WebForm>
            """;
        var file = FormDocumentReader.Read("F.blwebform", text);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("ClientSize"), Does.Contain("12px"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text), "frozen AND preserved");
        });

        file.Model.Text = "Edited";
        Assert.That(FormDocumentWriter.Write(file), Does.Contain("Width=\"12px\""),
            "an unrelated edit never touches text the reader could not parse");
    }

    [Test]
    public void EditingACanvasPagesClientSize_WritesTheRootWidthAndHeight()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        Assert.That(FormRootValues.Set(file.Model, Row("ClientSize"), "1024, 600"), Is.True);
        var written = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("Width=\"1024\"").And.Contain("Height=\"600\""));
            Assert.That(written, Does.Contain("<Layout Kind=\"Canvas\""), "the layout is untouched");
            Assert.That(FormDocumentReader.Read("F.blwebform", written).Model.Width, Is.EqualTo(1024));
        });
    }

    [Test]
    public void CreatingACanvasPage_WritesTheSizeAndTheLayout()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "F", Width = 800, Height = 450,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        var created = FormDocumentWriter.Create(page);
        var reread = FormDocumentReader.Read("F.blwebform", created);

        Assert.Multiple(() =>
        {
            Assert.That(created, Does.Contain("Width=\"800\"").And.Contain("Height=\"450\"").And.Contain("Kind=\"Canvas\""));
            Assert.That(reread.IsRefused, Is.False);
            Assert.That((reread.Model.Width, reread.Model.Height), Is.EqualTo((800, 450)));
        });
    }

    [Test]
    public void TheClientSizeTier_IsCanonOnACanvasPage_AndUnknownOnAGridPage()
    {
        var canvas = FormDocumentReader.Read("F.blwebform", CanvasPage);
        var grid = FormDocumentReader.Read("F.blwebform", GridPageWithAWidth);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(grid.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Unknown));
            Assert.That(canvas.TierOfRoot("Cols"), Is.EqualTo(PropertyTier.Unknown), "a Grid-only row on a Canvas page");
        });
    }

    [Test]
    public void TheRegionWriter_EmitsNoClientSize_ForACanvasPage()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        var result = RegionWriter.Write(
            "F.bas", FormScaffolder.Create("F", FormTarget.Web).CodeText, file.Model, "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False);
            Assert.That(result.Text, Does.Not.Contain("ClientSize"),
                "the web code-behind emits no geometry (spec §2.3; D4 keeps handler code unchanged)");
        });
    }

    [Test]
    public void TheGrid_OnACanvasPage_ShowsClientSize_AndNoTracks()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = null;

        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("ClientSize"));
            Assert.That(names, Does.Not.Contain("Cols").And.Not.Contains("Rows").And.Not.Contains("Gap"));
            Assert.That(grid.Rows.Single(r => r.Name == "ClientSize").StringValue, Is.EqualTo("640, 480"));
        });

        grid.Rows.Single(r => r.Name == "ClientSize").StringValue = "800, 600";

        Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((800, 600)));
    }

    [Test]
    public void TheGrid_OnAFlowPage_ShowsGap_AndNoTracksOrSize()
    {
        var file = FormDocumentReader.Read("F.blwebform", """
            <WebForm Name="F" Version="1">
              <Layout Kind="Flow" Dir="Vertical" Gap="4px"/>
              <Controls/>
            </WebForm>
            """);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = null;

        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Gap"), "the emitter writes gap for Flow too (FormAssetEmitter.cs:427-430)");
            Assert.That(names, Does.Not.Contain("Cols").And.Not.Contains("Rows").And.Not.Contains("ClientSize"));
        });
    }
}
```

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS0117: 'FormRootValues' does not contain a definition for 'Applies'`, a `RowForAttribute` overload error (no 3-argument overload), and `CS1061: 'FormPropertyDef' does not contain a definition for 'WebLayouts'`. Right reason: none of the three exists.

- [ ] **Step 3: Add the `WebLayouts` facet to `FormPropertyDef`.** In `FormControlCatalog.cs`, directly after the `OracleExemption` `<param>` doc (ends at `:145`), add:

```csharp
/// <param name="WebLayouts">
/// On the web, the page layouts this row exists on (spec 2026-09-27 §2.3); null = every layout. Read by
/// <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> and by NOTHING else —
/// ⛔ a FormRoot row's applicability is never <see cref="AppliesTo"/> alone: ClientSize targets the web (a
/// Canvas page has a design size) and does not exist on a Grid page. A catalog gate
/// (<c>FormRootLayoutTests.WebLayouts_IsDeclaredOnlyOnFormRootRows_ThatExistOnTheWeb</c>) refuses it on a
/// control row, where no consumer reads it.
/// </param>
```

and change the record's last parameter from

```csharp
    IReadOnlyDictionary<string, string>? Aliases = null,
    string? OracleExemption = null)
```

to

```csharp
    IReadOnlyDictionary<string, string>? Aliases = null,
    string? OracleExemption = null,
    IReadOnlyList<FormLayoutKind>? WebLayouts = null)
```

- [ ] **Step 4: Move the `FormRoot` rows onto (target, layout).** Replace `:1951-1969`, from `// ⚠ CLIENT size, emitted` through the Gap row, with:

```csharp
            // ⚠ CLIENT size, emitted `Me.ClientSize = New Size(w, h)` exactly as before — the form shows
            // ClientSize, never a second Size (spec §2.3). ⛔ Also a Canvas PAGE's design size (spec 2026-09-27
            // D2): stored as the root's Width/Height exactly as a .blform stores them. The web code-behind
            // emits no geometry (RegionWriter emits root rows on the WinForms branch only).
            new("ClientSize", FormPropertyType.Size, Targets: new[] { FormTarget.WinForms, FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The size of the client area of the form, in pixels.",
                OracleExemption: "ClientSize is not browsable in WinForms — VS shows Size. The designer " +
                                 "shows the CLIENT size because that is what its surface draws and what the " +
                                 "region writer emits (spec §2.3).",
                WebLayouts: new[] { FormLayoutKind.Canvas }),

            // Web only — kept verbatim as CSS track lists; the browser is the renderer (FormGridLayout).
            new("Cols", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The page's column tracks, as a comma-separated CSS grid track list (e.g. 120px,1fr).",
                WebLayouts: new[] { FormLayoutKind.Grid }),
            new("Rows", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The page's row tracks, as a comma-separated CSS grid track list (e.g. auto,auto).",
                WebLayouts: new[] { FormLayoutKind.Grid }),
            // ⚠ Grid AND Flow: the emitter writes `gap` for both (FormAssetEmitter.Css).
            new("Gap", FormPropertyType.String, Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "The space between the page's grid cells or flow items, as a CSS length (e.g. 8px).",
                WebLayouts: new[] { FormLayoutKind.Grid, FormLayoutKind.Flow }),
```

- [ ] **Step 5: The one predicate.** In `FormRootValues.cs`, append to the class summary's second paragraph: `The row's APPLICABILITY is Applies — the one predicate the reader, writer, region writer, grid, tier and retarget all call (spec 2026-09-27 §2.3).` Replace `RowForAttribute` (`:111-120`) with:

```csharp
    /// <summary>
    /// ⛔⛔ Whether <paramref name="row"/> exists on a document of <paramref name="target"/> laid out
    /// <paramref name="layout"/> (spec 2026-09-27 §2.3) — THE one predicate for a FormRoot row. Never call
    /// <see cref="FormPropertyDef.AppliesTo"/> on a root row directly: ClientSize targets the web (a Canvas
    /// page's design size) and does not exist on a Grid page, and <c>AppliesTo(Web)</c> alone says it does.
    /// </summary>
    /// <param name="layout">
    /// The web document's layout; ignored for WinForms. Null on the web means Grid — the default a page
    /// with no <c>&lt;Layout&gt;</c> has (<see cref="FormVocabulary.LayoutOf"/>).
    /// </param>
    public static bool Applies(FormPropertyDef row, FormTarget target, FormLayoutKind? layout)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!row.AppliesTo(target))
        {
            return false;
        }

        return target != FormTarget.Web || row.WebLayouts == null ||
               row.WebLayouts.Contains(layout ?? FormLayoutKind.Grid);
    }

    /// <summary><see cref="Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> for a document.</summary>
    public static bool Applies(FormPropertyDef row, FormDocument form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return Applies(row, form.Target, FormVocabulary.LayoutOf(form));
    }

    /// <summary>
    /// The FormRoot row a root attribute belongs to on a document of (<paramref name="target"/>,
    /// <paramref name="layout"/>), or null — through <see cref="Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>,
    /// never a copy of it. ⚠ Ordinal: XML attribute names are case-sensitive and the reader has always matched
    /// them exactly — and <see cref="Serialization.FormFile.TierOfRoot"/> uses the same comparison, so the
    /// tier API and the reader cannot disagree about whether <c>text</c> is the Text row.
    /// </summary>
    public static FormPropertyDef? RowForAttribute(string attribute, FormTarget target, FormLayoutKind? layout) =>
        FormControlCatalog.FormRoot.Properties
            .Where(r => Applies(r, target, layout))
            .FirstOrDefault(r => StorageAttributes(r).Contains(attribute, StringComparer.Ordinal));
```

- [ ] **Step 6: The reader: pre-scan, root size by vocabulary, known-root by (target, layout).** In `FormDocumentReader.cs`, replace from `// Text is ONE vocabulary on both targets` (`:110`) through the end of the root-attribute loop (`:154`) with:

```csharp
        // Text is ONE vocabulary on both targets (D2, spec §2.3): the window caption, the page title.
        model.Text = (string?)root.Attribute("Text");

        // ⛔ PRE-SCAN (spec 2026-09-27 §2.2). The root attributes and every control are read in the
        // vocabulary the LAYOUT picks — a Canvas page stores Width/Height and X/Y exactly as a .blform does —
        // but <Layout> is an ordinary child element and may come AFTER <Controls>. So it is read here,
        // before anything that depends on it. ⚠ The LAST one, exactly as the element loop below always
        // behaved (each <Layout> overwrote the one before); that loop now skips it.
        if (target == FormTarget.Web &&
            root.Elements().LastOrDefault(e => e.Name.LocalName == "Layout") is { } layoutElement)
        {
            model.Layout = ReadLayout(layoutElement);
        }

        var layout = FormVocabulary.LayoutOf(model);

        if (FormVocabulary.IsPixel(target.Value, layout))
        {
            // The form's own client size: a window's, or a Canvas page's design size (spec 2026-09-27 D2).
            // A Grid/Flow page has none — D3 gives it a <Layout> instead.
            model.Width = IntAttribute(root, "Width");
            model.Height = IntAttribute(root, "Height");

            // ⚠ An unparseable Width/Height is left null here and still falls through to
            // UnknownAttributes below — that round trip is what preserves the text byte-for-byte. What
            // changed (spec §2.3) is the TIER: the ClientSize row is Degraded — frozen, explained — not
            // Unknown. The storage stayed where it was on purpose: the writer's Width/Height guard
            // exists to never overwrite text it could not parse.
            //
            // ⚠ A size that parses but is not POSITIVE is Degraded too: FormRootValues.Set refuses it and
            // the region writer emits nothing for it, so showing it Canon would promise a size the
            // program never gets. A parsed value stays modelled (and so round-trips through the model).
            var rawWidth = (string?)root.Attribute("Width");
            var rawHeight = (string?)root.Attribute("Height");
            var unusable =
                (rawWidth != null && model.Width is null or <= 0) ||
                (rawHeight != null && model.Height is null or <= 0);
            if (unusable)
            {
                // Name only the attributes the document CARRIES — never an invented `Height=""`.
                var present = new List<string>();
                if (rawWidth != null) present.Add($"Width=\"{rawWidth}\"");
                if (rawHeight != null) present.Add($"Height=\"{rawHeight}\"");

                degradedRoot.Add(new DegradedProperty("", "ClientSize",
                    string.Join(" ", present),
                    $"the form's client size could not be used — {string.Join(" and ", present)} " +
                    (present.Count == 1 ? "must be a positive whole number" : "must both be positive whole numbers") +
                    ". The attributes are preserved exactly as written."));
            }
        }

        foreach (var attribute in root.Attributes())
        {
            if (!IsKnownRootAttribute(attribute.Name.LocalName, target.Value, layout, root))
            {
                model.UnknownAttributes[attribute.Name.LocalName] = attribute.Value;
            }
        }
```

Replace the `Layout` case (`:160-164`):

```csharp
                // Web only (D3), and already READ by the pre-scan above — skipped here so it is neither
                // read twice nor mistaken for an unknown element. On a .blform it is not a layout — it is
                // an element this designer does not model, and it round-trips untouched like any other.
                case "Layout" when target == FormTarget.Web:
                    break;
```

Replace `IsKnownRootAttribute` (`:295-309`) with (update its doc's first `⛔` paragraph to say "asks `RowForAttribute` by (target, layout)"):

```csharp
    private static bool IsKnownRootAttribute(string name, FormTarget target, FormLayoutKind? layout, XElement root)
    {
        if (name is "Name" or "Version")
        {
            return true;
        }

        var row = FormRootValues.RowForAttribute(name, target, layout);
        if (row == null)
        {
            return false;
        }

        return row.Type == FormPropertyType.Size ? IntAttribute(root, name) != null : true;
    }
```

- [ ] **Step 7: The writer: root size by vocabulary.** In `FormDocumentWriter.cs`, `Create` (`:92-101`), replace:

```csharp
        if (model.Target == FormTarget.WinForms)
        {
            // The window's client size: D3's divergence, at the root.
            root.SetAttributeValue("Width", model.Width);
            root.SetAttributeValue("Height", model.Height);
        }
        else if (model.Layout != null)
        {
            root.Add(LayoutElement(model.Layout));
        }
```

with:

```csharp
        // The client size of a PIXEL document — a window, or a Canvas page (spec 2026-09-27 D2). ⛔ Asked of
        // FormVocabulary, never of the target: a Canvas page has a size AND a <Layout>.
        if (FormVocabulary.IsPixel(model))
        {
            root.SetAttributeValue("Width", model.Width);
            root.SetAttributeValue("Height", model.Height);
        }

        if (model.Target == FormTarget.Web && model.Layout != null)
        {
            root.Add(LayoutElement(model.Layout));
        }
```

In `ApplyToDocument` (`:183-193`), replace:

```csharp
        if (model.Target == FormTarget.WinForms)
        {
            ApplyFormAttributes(root, model);
        }
        else
        {
            ApplyLayout(root, model);
        }
```

with:

```csharp
        // ⛔ A Canvas page has BOTH: a size (pixel vocabulary, FormVocabulary) and a <Layout> (web). A .blform
        // never gets a <Layout> — its own reader would read it as an unknown element on the next open.
        if (FormVocabulary.IsPixel(model))
        {
            ApplyFormAttributes(root, model);
        }

        if (model.Target == FormTarget.Web)
        {
            ApplyLayout(root, model);
        }
```

Update the comment above it (`:183-185`) and `ApplyFormAttributes`' summary to say "the pixel root's `Width`/`Height` — a `.blform`'s or a Canvas page's".

- [ ] **Step 8: The other readers of a root row's applicability.**
  - `FormFile.cs:111-114`: replace `is { } row && row.AppliesTo(Model.Target)` with `is { } row && FormRootValues.Applies(row, Model)`.
  - `RegionWriter.cs:637`: replace `FormControlCatalog.FormRoot.Properties.Where(p => p.AppliesTo(FormTarget.WinForms))` with `FormControlCatalog.FormRoot.Properties.Where(p => FormRootValues.Applies(p, FormTarget.WinForms, null))`.
  - `FormPropertyGridViewModel.cs:479`: replace `.Where(p => p.AppliesTo(form.Target))` with `.Where(p => FormRootValues.Applies(p, form))`.
  - `FormRetarget.cs`: in `Conversion` add a field after `_to` (`:164`):

```csharp
        /// <summary>
        /// The layout the DESTINATION document gets: WinForms → web produces Grid (ToCells; spec 2026-09-27 §6 —
        /// unchanged in piece 1, piece 4 replaces the retarget); a window has none.
        /// </summary>
        private readonly FormLayoutKind? _toLayout;
```

    Set it in the constructor: `_toLayout = to == FormTarget.Web ? FormLayoutKind.Grid : null;`. Then in `ConvertRoot` replace `:204-205` with:

```csharp
                var toRow = FormRootValues.RowForAttribute(name, _to, _toLayout);
                var fromRow = FormRootValues.RowForAttribute(name, _from, FormVocabulary.LayoutOf(_source));
```

    and `:215` `(fromRow.AppliesTo(_to)` with `(FormRootValues.Applies(fromRow, _to, _toLayout)`.
  - `FormDocument.cs:49-53`: change the section header comment to `// --- pixel documents: .blform, and a web Canvas page (spec 2026-09-27 D2) ---`. Change the summary to `The form's client size — a window's, or a Canvas page's design size. Null on a Grid/Flow page.`

- [ ] **Step 9: Existing tests move onto the predicate.**
  - `FormRootTests.cs:58-64`: replace the inner `foreach (var attribute in attributes)` block with:

```csharp
                    foreach (var attribute in attributes)
                    {
                        foreach (var layout in target == FormTarget.WinForms
                                     ? new FormLayoutKind?[] { null }
                                     : new FormLayoutKind?[] { null, FormLayoutKind.Grid, FormLayoutKind.Flow, FormLayoutKind.Canvas })
                        {
                            Assert.That(FormRootValues.RowForAttribute(attribute, target, layout),
                                FormRootValues.Applies(row, target, layout) ? Is.SameAs(row) : Is.Null,
                                $"{where} ({layout?.ToString() ?? "no layout"}): attribute '{attribute}' belongs to the row exactly where the row applies");
                        }
                    }
```

  - Update that test's summary: "the target filter is `RowForAttribute`'s" becomes "the (target, layout) filter is `FormRootValues.Applies`, which `RowForAttribute` asks".
  - `FormRootTests.cs:281`: `FormRootValues.RowForAttribute("text", FormTarget.WinForms)` becomes `FormRootValues.RowForAttribute("text", FormTarget.WinForms, null)`.
  - `FormRootRetargetTests.cs`: replace the `EveryFormRootRow_CrossesOrIsNamed` test's attribute line and header (`:28-40`), and its applicability test at `:47`:

```csharp
    /// <summary>
    /// The sweep's source documents. ⚠ RE-CHECK IN TASK 11: Web Canvas joins then (spec 2026-09-27 §6) —
    /// until the Canvas → WinForms retarget copies the design size, ClientSize would not cross exactly and
    /// MobileBreakpoint (Task 4) would not be named.
    /// </summary>
    private static IEnumerable<TestCaseData> Sources()
    {
        yield return new TestCaseData(FormTarget.WinForms, null).SetName("{m}(WinForms)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Grid).SetName("{m}(Web Grid)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Flow).SetName("{m}(Web Flow)");
    }

    [TestCaseSource(nameof(Sources))]
    public void EveryFormRootRow_CrossesOrIsNamed(FormTarget from, FormLayoutKind? layout)
    {
        var to = Other(from);
        FormLayoutKind? toLayout = to == FormTarget.Web ? FormLayoutKind.Grid : null;   // what the retarget produces
        var source = new FormDocument
        {
            Target = from, Name = "Sweep",
            Layout = layout is { } kind ? new FormLayout { Kind = kind } : null
        };
        var rows = FormControlCatalog.FormRoot.Properties.Where(r => FormRootValues.Applies(r, from, layout)).ToList();
```

    and inside the loop `if (row.AppliesTo(to))` becomes `if (FormRootValues.Applies(row, to, toLayout))`. Replace the guard test's body (`:84-89`) with:

```csharp
        Assert.Multiple(() =>
        {
            Assert.That(rows.Any(r => FormRootValues.Applies(r, FormTarget.WinForms, null) &&
                                      FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Grid)), Is.True);
            Assert.That(rows.Any(r => IsLayoutEdge(r) && FormRootValues.Applies(r, FormTarget.WinForms, null) &&
                                      !FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Grid)), Is.True);
            Assert.That(rows.Any(r => IsLayoutEdge(r) && FormRootValues.Applies(r, FormTarget.Web, FormLayoutKind.Grid) &&
                                      !FormRootValues.Applies(r, FormTarget.WinForms, null)), Is.True);
        });
```

  - While here, tighten `FormPropertyGridTests.AWebPagesFormRows_AreItsGridTracks_NotAClientSize` (`:466`) to also assert `Does.Not.Contain("ClientSize")`. Its name has always claimed that, and ClientSize now targets the web.

- [ ] **Step 10: Grep gate: no raw applicability test on a root row survives.** Grep `FormRoot.Properties` in `BasicLang/`, `VisualGameStudio.Shell/` and `VisualGameStudio.Tests/`. Every hit that filters must go through `FormRootValues.Applies`. The exceptions are a WinForms-only filter kept for the parity/csc sweeps (`WinFormsCatalogParityTests`, `WinFormsCatalogSweepTests.cs:502`), where `AppliesTo(WinForms)` equals `Applies(r, WinForms, null)` because `WebLayouts` is web-only. List each hit and its verdict in the commit message.

- [ ] **Step 11: Run.** Build, then filter `FullyQualifiedName~FormRootLayoutTests|FullyQualifiedName~FormRootTests|FullyQualifiedName~FormRootRetargetTests|FullyQualifiedName~FormPropertyGrid|FullyQualifiedName~WinFormsCatalogParityTests|FullyQualifiedName~FormRetargetTests|FullyQualifiedName~FormDocument|FullyQualifiedName~FormRegionWriterTests|FullyQualifiedName~BlFormRoundTripTests|FullyQualifiedName~FormScaffolderTests`. All green. ⚠ `FormRootRetargetTests.EveryFormRootRow_CrossesOrIsNamed` now has three rows named `(WinForms)`, `(Web Grid)` and `(Web Flow)`. The old `[Values]` names disappear. This is expected; record it for the gate's name comparison.

- [ ] **Step 12: Commit.** Message `feat(forms): the Form's rows by (target, layout) through one predicate; <Layout> pre-scan; a Canvas page's design size (spec 2026-09-27 §2.1-2.3)`. Include the grep verdicts from Step 10. Stage the source files and the 4 test files (FormRootTests, FormRootRetargetTests, FormRootLayoutTests, FormPropertyGridTests) listed above by name — count them against the Files list; do not trust a number here. (Plan review note: `TheRegionWriter_EmitsNoClientSize_ForACanvasPage` cannot fail from this task's change — RegionWriter's web branch never emits root rows — keep it as documentation, not a guard.)

---

## Task 3: A Canvas page's controls speak pixels, in the reader, writer, catalog and clipboard

Spec §2.1 (`IsStructural`, `ReadGeometry`, the clipboard's mirrored `ReadGeometry`, cross-layout paste refusal), §2.5 (foreign vocabulary round-trips; doc-comment corrections). Scope call S6.

> ⚠ Plan-review note: the cross-layout paste-refusal test goes through the STATIC `_designerClipboard` (`CodeEditorDocumentViewModel.cs:504`). It is correct only while these tests run serially — put a comment on the test saying so, and clear the clipboard in its setup so an earlier test's copy cannot satisfy it.

**Files:**
- Modify: `BasicLang/Forms/FormControlCatalog.cs`: `IsStructural(name, target)` (`:2021-2041`)
- Modify: `BasicLang/Forms/Serialization/FormDocumentReader.cs`: the three `ReadControl` call sites (the Controls loop, the Components loop, the nested recursion near `:565`); `ReadControl`'s signature (`:344-348`); `:457` (`ReadGeometry`); `:505-507` (`IsStructural`); `ReadGeometry` (`:635-677`)
- Modify: `BasicLang/Forms/FormDocument.cs`: `FormClipboard` (`:256-586`); add `FormPasteResult`
- Modify: `BasicLang/Forms/FormRetarget.cs`: `:422`
- Modify: `BasicLang/Forms/FormGeometry.cs`: doc comments `:13-17`, `:33-39`, `:58-66`
- Modify: `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs`: `CopyControls` `:572-573`; `PasteControls` `:625-631`
- Create: `VisualGameStudio.Tests/Compiler/FormPixelVocabularyTests.cs`

- [ ] **Step 1: Write the failing test file.** `VisualGameStudio.Tests/Compiler/FormPixelVocabularyTests.cs`:

```csharp
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.1 / §2.5 — a Canvas page's CONTROLS are read, written, judged structural and pasted in
/// the pixel vocabulary; a paste between layouts is refused and SAID.
/// </summary>
[TestFixture]
public class FormPixelVocabularyTests
{
    private const string CanvasPage = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls>
            <Panel Id="pnl" X="16" Y="40" Width="300" Height="200" Anchor="Top,Left,Right" TabIndex="0">
              <Button Id="btn" X="8" Y="8" Width="75" Height="23" Dock="Bottom" TabIndex="1" Text="Go"/>
            </Panel>
            <TextBox Id="txt" X="330" Y="40" Width="120" Height="23" TabIndex="2" Col="3"/>
          </Controls>
        </WebForm>
        """;

    private const string CanvasPageLayoutLast = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Controls>
            <Button Id="btn" X="8" Y="8" Width="75" Height="23" TabIndex="0"/>
          </Controls>
          <Layout Kind="Canvas"/>
        </WebForm>
        """;

    private const string GridPage = """
        <WebForm Name="F" Version="1">
          <Layout Kind="Grid" Cols="auto,1fr" Rows="auto"/>
          <Controls>
            <Button Id="btn" Col="1" Row="0" X="190" TabIndex="0"/>
          </Controls>
        </WebForm>
        """;

    // ==================================================================
    // IsStructural by (target, layout)
    // ==================================================================

    [TestCase("X", FormTarget.Web, FormLayoutKind.Canvas, true)]
    [TestCase("Dock", FormTarget.Web, FormLayoutKind.Canvas, true)]
    [TestCase("Col", FormTarget.Web, FormLayoutKind.Canvas, false)]
    [TestCase("X", FormTarget.Web, FormLayoutKind.Grid, false)]
    [TestCase("Col", FormTarget.Web, FormLayoutKind.Grid, true)]
    [TestCase("X", FormTarget.Web, null, false)]
    [TestCase("X", FormTarget.WinForms, null, true)]
    [TestCase("TabIndex", FormTarget.Web, FormLayoutKind.Canvas, true)]
    [TestCase("Id", FormTarget.Web, FormLayoutKind.Flow, true)]
    public void IsStructural_AsksTheLayout(string attribute, FormTarget target, FormLayoutKind? layout, bool expected) =>
        Assert.That(FormControlCatalog.IsStructural(attribute, target, layout), Is.EqualTo(expected));

    // ==================================================================
    // The reader and the writer
    // ==================================================================

    [Test]
    public void ACanvasPagesControls_ReadPixelGeometry_AtEveryDepth()
    {
        var model = FormDocumentReader.Read("F.blwebform", CanvasPage).Model;
        var panel = (PixelGeometry)model.FindById("pnl")!.Geometry!;
        var button = (PixelGeometry)model.FindById("btn")!.Geometry!;

        Assert.Multiple(() =>
        {
            Assert.That((panel.X, panel.Y, panel.Width, panel.Height), Is.EqualTo((16, 40, 300, 200)));
            Assert.That(panel.Anchor, Is.EqualTo("Top,Left,Right"));
            Assert.That((button.X, button.Y, button.Width, button.Height), Is.EqualTo((8, 8, 75, 23)));
            Assert.That(button.Dock, Is.EqualTo("Bottom"));
            Assert.That(model.FindById("pnl")!.UnknownAttributes, Is.Empty, "X/Width/Anchor are geometry here, not unknown");
        });
    }

    [Test]
    public void ACellAttributeOnACanvasControl_IsAnUnknownAttribute_AndRoundTrips()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);
        var text = file.Model.FindById("txt")!;

        Assert.Multiple(() =>
        {
            Assert.That(text.Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(text.UnknownAttributes["Col"], Is.EqualTo("3"), "foreign vocabulary round-trips, no new diagnostic (§2.5)");
            Assert.That(file.Diagnostics, Is.Empty);
        });

        ((PixelGeometry)file.Model.FindById("btn")!.Geometry!).X = 10;
        var written = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("X=\"10\""));
            Assert.That(written, Does.Contain("Col=\"3\""), "an unrelated edit keeps the foreign attribute");
        });
    }

    [Test]
    public void ACanvasPage_RoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(CanvasPage));
    }

    [Test]
    public void ControlsAreReadAsPixels_EvenWhenTheLayoutComesLast()
    {
        var model = FormDocumentReader.Read("F.blwebform", CanvasPageLayoutLast).Model;

        Assert.That(model.FindById("btn")!.Geometry, Is.TypeOf<PixelGeometry>(),
            "the §2.2 pre-scan decides the vocabulary before any control is read");
    }

    [Test]
    public void AGridPagesControl_StillReadsItsCell_AndKeepsAStrayXAsUnknown()
    {
        var file = FormDocumentReader.Read("F.blwebform", GridPage);
        var button = file.Model.FindById("btn")!;

        Assert.Multiple(() =>
        {
            Assert.That(button.Geometry, Is.TypeOf<GridGeometry>());
            Assert.That(button.UnknownAttributes["X"], Is.EqualTo("190"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(GridPage), "Grid pages are byte-for-byte unchanged");
        });
    }

    [Test]
    public void CreatingACanvasPage_WritesPixelGeometry()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "F", Width = 640, Height = 480,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        page.Controls.Add(new FormControl
        {
            Kind = "Button", Id = "btn", TabIndex = 0,
            Geometry = new PixelGeometry { X = 24, Y = 32, Width = 75, Height = 23, Anchor = "Top,Right" }
        });

        var reread = FormDocumentReader.Read("F.blwebform", FormDocumentWriter.Create(page)).Model;
        var geometry = (PixelGeometry)reread.FindById("btn")!.Geometry!;

        Assert.That((geometry.X, geometry.Y, geometry.Anchor), Is.EqualTo((24, 32, "Top,Right")));
    }

    // ==================================================================
    // The clipboard
    // ==================================================================

    private static FormControl PixelButton(string id) => new()
    {
        Kind = "Button", Id = id, TabIndex = 0,
        Geometry = new PixelGeometry { X = 40, Y = 40, Width = 75, Height = 23 }
    };

    private static FormControl CellButton(string id) => new()
    {
        Kind = "Button", Id = id, TabIndex = 0, Geometry = new GridGeometry { Col = 1, Row = 0 }
    };

    [Test]
    public void ACanvasFragment_RecordsItsLayout_AndPastesAsPixels()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { PixelButton("btn") }, FormLayoutKind.Canvas);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.Contain("Layout=\"Canvas\""));
            Assert.That(paste.Refusal, Is.Null);
            Assert.That(paste.Controls.Single().Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(((PixelGeometry)paste.Controls.Single().Geometry!).X, Is.EqualTo(40));
        });
    }

    [Test]
    public void AGridFragment_IntoACanvasPage_IsRefused_NamingBothLayouts()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { CellButton("btn") }, FormLayoutKind.Grid);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Does.Contain("Grid").And.Contain("Canvas"));
        });
    }

    [Test]
    public void ACanvasFragment_IntoAGridPage_IsRefused()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { PixelButton("btn") }, FormLayoutKind.Canvas);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Grid, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Does.Contain("Canvas").And.Contain("Grid"));
        });
    }

    [Test]
    public void AFragmentWrittenBeforeLayoutsWereRecorded_IsAGridFragment()
    {
        const string legacy = """
            <FormSubtree Target="Web" Version="1">
              <Button Id="btn" Col="1" Row="0" TabIndex="0"/>
            </FormSubtree>
            """;

        Assert.Multiple(() =>
        {
            Assert.That(FormClipboard.Paste(legacy, FormTarget.Web, FormLayoutKind.Grid, _ => false).Controls, Has.Count.EqualTo(1));
            Assert.That(FormClipboard.Paste(legacy, FormTarget.Web, FormLayoutKind.Canvas, _ => false).Refusal, Is.Not.Null);
        });
    }

    [Test]
    public void AWinFormsFragment_IntoACanvasPage_IsRefused_AndSaysWhy()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { PixelButton("btn") });

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty, "the two targets have different catalogs");
            Assert.That(paste.Refusal, Does.Contain("WinForms"));
        });
    }

    [Test]
    public void ANumericLayoutName_IsNotALayout()
    {
        // ⛔ Enum.TryParse accepts "2" as Canvas. A fragment is unvetted text; a number is not a layout name.
        const string xml = """<FormSubtree Target="Web" Layout="2" Version="1"><Button Id="b" TabIndex="0"/></FormSubtree>""";

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Is.Null, "a malformed fragment is not ours to explain");
        });
    }

    [Test]
    public void TheOldEntryPoint_StillPastesIntoAGridPage()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { CellButton("btn") });

        Assert.That(FormClipboard.DeserializeSubtree(xml, FormTarget.Web, _ => false).Single().Geometry,
            Is.TypeOf<GridGeometry>());
    }

    // ==================================================================
    // Through the real command (who calls it in a shipping build: the Paste command)
    // ==================================================================

    private const string GridPageDoc = """
        <WebForm Name="GridPage" Version="1">
          <Layout Kind="Grid" Cols="auto,1fr" Rows="auto" Gap="8px"/>
          <Controls>
            <Button Id="btnGrid" Col="0" Row="0" TabIndex="0" Text="Grid"/>
          </Controls>
        </WebForm>
        """;

    private const string PixelPageDoc = """
        <WebForm Name="PixelPage" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls>
            <Button Id="btnPixel" X="40" Y="40" Width="75" Height="23" TabIndex="0" Text="Pixel"/>
          </Controls>
        </WebForm>
        """;

    private static (CodeEditorDocumentViewModel Vm, List<DesignerDiagnosticsEvent> Published) OpenVm(string name, string text)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/proj/" + name + ".blwebform"] = text,
            ["/proj/" + name + ".bas"] = FormScaffolder.Create(name, FormTarget.Web).CodeText
        };

        var fs = new Mock<IFileService>();
        fs.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) => Task.FromResult(files[p]));
        fs.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
            .Returns((string p) => Task.FromResult(files.ContainsKey(p)));
        fs.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string t, CancellationToken _) =>
            {
                files[p] = t;
                return Task.CompletedTask;
            });

        var published = new List<DesignerDiagnosticsEvent>();
        var events = new Mock<IEventAggregator>();
        events.Setup(e => e.Publish(It.IsAny<DesignerDiagnosticsEvent>()))
            .Callback((DesignerDiagnosticsEvent e) => published.Add(e));

        var vm = new CodeEditorDocumentViewModel(fs.Object, events.Object) { FilePath = "/proj/" + name + ".blwebform" };
        vm.SetContent(text);
        return (vm, published);
    }

    [Test]
    public void PastingAGridPagesControl_IntoACanvasPage_IsRefusedAndSaid_AndChangesNothing()
    {
        var (grid, _) = OpenVm("GridPage", GridPageDoc);
        grid.Selection.Set(grid.DesignDocument!.FindById("btnGrid")!);
        grid.CopyControlsCommand.Execute(null);

        var (canvas, published) = OpenVm("PixelPage", PixelPageDoc);
        var before = canvas.Text;

        canvas.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.DesignDocument!.Controls.Select(c => c.Id), Is.EqualTo(new[] { "btnPixel" }));
            Assert.That(canvas.Text, Is.EqualTo(before));
            Assert.That(published.SelectMany(e => e.Diagnostics).Any(d =>
                    d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Grid") && d.Message.Contains("Canvas")),
                Is.True, "a refused paste is SAID — a silent no-op is the failure this designer exists to remove");
        });
    }

    [Test]
    public void PastingWithinACanvasPage_LandsInPixels_OffsetSoItIsVisible()
    {
        var (canvas, _) = OpenVm("PixelPage", PixelPageDoc);
        canvas.Selection.Set(canvas.DesignDocument!.FindById("btnPixel")!);
        canvas.CopyControlsCommand.Execute(null);

        canvas.PasteControlsCommand.Execute(null);

        var copy = canvas.DesignDocument!.Controls.Single(c => c.Id != "btnPixel");
        Assert.That(((PixelGeometry)copy.Geometry!).X, Is.EqualTo(48));
    }
}
```

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS1501: No overload for method 'IsStructural' takes 3 arguments`, `CS0117: 'FormClipboard' does not contain a definition for 'Paste'`, and a `SerializeSubtree` 3-argument overload error. Right reason: none exists.

- [ ] **Step 3: `IsStructural` by (target, layout).** Replace `FormControlCatalog.cs:2021-2041` (the 2-argument overload and its doc) with:

```csharp
    /// <summary>
    /// Structural in the vocabulary a document of (<paramref name="target"/>, <paramref name="layout"/>)
    /// speaks (spec 2026-09-27 §2.1).
    ///
    /// <para>⛔ The two vocabularies overlap in spelling and not in meaning, so "structural" is not a
    /// property of the attribute name alone. <c>Width</c> is a pixel control's width and is read into its
    /// geometry; on a Grid page's control nothing reads it, so calling it structural there would make it
    /// neither a property nor an unknown attribute — absent from the model entirely, and silently dropped
    /// by any path that rebuilds the document from the model.</para>
    ///
    /// <para>⛔ By LAYOUT too, through <see cref="FormVocabulary.IsPixel(FormTarget, FormLayoutKind?)"/>: a
    /// Canvas page's control is laid out in pixels exactly as a .blform's is. Asking by target alone put its
    /// X/Width in UnknownAttributes, and the control had a position nothing read.</para>
    /// </summary>
    public static bool IsStructural(string attributeName, FormTarget target, FormLayoutKind? layout)
    {
        if (string.Equals(attributeName, "Id", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(attributeName, "TabIndex", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var vocabulary = FormVocabulary.IsPixel(target, layout) ? PixelAttributes : GridAttributes;
        return vocabulary.Any(a => string.Equals(a, attributeName, StringComparison.OrdinalIgnoreCase));
    }
```

  (The 1-argument `IsStructural(string)` at `:2018` stays; it has no caller at this base. Leave it rather than widen this commit.)

- [ ] **Step 4: The reader threads the layout into every control.**
  - `ReadControl`'s signature (`:344-348`): insert `FormLayoutKind? layout,` after `FormTarget target,`. Add a `<param name="layout">` doc: `The document's layout (the §2.2 pre-scan's) — with the target, it picks the geometry vocabulary.`
  - The Controls loop call becomes `ReadControl(child, target.Value, layout, filePath, diagnostics, degraded, positions)`.
  - The Components loop call becomes `ReadControl(child, target.Value, layout, filePath, diagnostics, degraded, positions, isComponent: true)`.
  - The nested call (`:565-566`) becomes `ReadControl(child, target, layout, filePath, diagnostics, degraded, positions, parent: control)`.
  - `:457`: `ReadGeometry(element, target)` becomes `ReadGeometry(element, target, layout)`.
  - `:506`: `FormControlCatalog.IsStructural(name, target)` becomes `FormControlCatalog.IsStructural(name, target, layout)`.
  - Update the comment above `:505` from "Target-aware" to "(target, layout)-aware".
  - `ReadGeometry` (`:643`): signature `ReadGeometry(XElement element, FormTarget target, FormLayoutKind? layout)`. Its first line becomes `if (FormVocabulary.IsPixel(target, layout))`. Doc: "Selected by (target, layout) — `FormVocabulary.IsPixel` — not by sniffing which attributes are present".

- [ ] **Step 5: The clipboard.** In `FormDocument.cs`:
  - Add, directly above `public static class FormClipboard`:

```csharp
/// <summary>
/// What a paste produced: the controls to add (already renamed), or why nothing was pasted.
/// </summary>
/// <param name="Refusal">
/// A reason a user can act on, or null. ⚠ Null with no controls means a fragment that is not ours or is
/// malformed — nothing to explain; a refusal is a fragment we understood and cannot honour here.
/// </param>
public sealed record FormPasteResult(IReadOnlyList<FormControl> Controls, string? Refusal)
{
    internal static readonly FormPasteResult Nothing = new(Array.Empty<FormControl>(), null);
}
```

  - Replace `SerializeSubtree` (`:260-277`) with:

```csharp
    /// <summary>
    /// Serializes controls (with their descendants) to a self-describing XML fragment. The target — and,
    /// for a web form, its LAYOUT (spec 2026-09-27 §2.1) — is recorded so a paste into a document of another
    /// vocabulary can be refused rather than producing a WinForms control on a page, or a cell on a pixel page.
    /// </summary>
    /// <param name="layout">The source web document's layout; null means Grid. Ignored for WinForms.</param>
    public static string SerializeSubtree(
        FormTarget target, IEnumerable<FormControl> controls, FormLayoutKind? layout = null)
    {
        var root = new XElement(ClipboardRoot,
            new XAttribute("Target", target.ToString()),
            new XAttribute("Version", 1));

        if (target == FormTarget.Web)
        {
            root.SetAttributeValue("Layout", (layout ?? FormLayoutKind.Grid).ToString());
        }

        foreach (var control in controls)
        {
            root.Add(ToElement(control));
        }

        return root.ToString();
    }
```

  - Replace `DeserializeSubtree` (`:279-329`) with the shim plus `Paste`:

```csharp
    /// <summary>
    /// The pre-layout entry point, kept for its callers: a web destination is taken to be Grid, which every
    /// web form was when this signature was written. New code calls <see cref="Paste"/>.
    /// </summary>
    public static IReadOnlyList<FormControl> DeserializeSubtree(
        string xml, FormTarget target, Func<string, bool> isTaken) =>
        Paste(xml, target, target == FormTarget.Web ? FormLayoutKind.Grid : null, isTaken).Controls;

    /// <summary>
    /// Reads a fragment produced by <see cref="SerializeSubtree"/> into a document of (<paramref name="target"/>,
    /// <paramref name="layout"/>), renaming every control whose id is already taken and retargeting the binds
    /// that named it — or REFUSES, with a reason, a fragment from another target or another layout.
    ///
    /// <para>⛔ A paste between layouts is refused, never converted (spec 2026-09-27 §2.1): a grid cell has no
    /// pixel position and a pixel position has no cell, so every pasted control would land somewhere nobody
    /// designed. Converting is piece 4's job.</para>
    /// </summary>
    /// <param name="layout">The destination web document's layout; null means Grid. Ignored for WinForms.</param>
    /// <param name="isTaken">
    /// Whether an id is in use. The caller passes the destination document's lookup; ids minted
    /// during this paste are added to it internally, so two pasted siblings cannot collide.
    /// </param>
    public static FormPasteResult Paste(
        string xml, FormTarget target, FormLayoutKind? layout, Func<string, bool> isTaken)
    {
        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            return FormPasteResult.Nothing;
        }

        if (root.Name.LocalName != ClipboardRoot ||
            !Enum.TryParse<FormTarget>((string?)root.Attribute("Target"), out var sourceTarget))
        {
            return FormPasteResult.Nothing;
        }

        // A web fragment written before layouts were recorded names none — and every web form then was Grid.
        FormLayoutKind? sourceLayout = null;
        if (sourceTarget == FormTarget.Web)
        {
            var recorded = (string?)root.Attribute("Layout");
            if (recorded == null)
            {
                sourceLayout = FormLayoutKind.Grid;
            }
            else if (TryLayoutName(recorded, out var kind))
            {
                sourceLayout = kind;
            }
            else
            {
                return FormPasteResult.Nothing;
            }
        }

        FormLayoutKind? destination = target == FormTarget.Web ? layout ?? FormLayoutKind.Grid : null;

        // ⚠ Changed on review (Task 3): by VOCABULARY, not layout name — Grid ↔ Flow stays allowed.
        if (sourceTarget != target ||
            FormVocabulary.IsPixel(sourceTarget, sourceLayout) != FormVocabulary.IsPixel(target, destination))
        {
            return new FormPasteResult(Array.Empty<FormControl>(),
                $"These controls were copied from a {Describe(sourceTarget, sourceLayout)} and this is a " +
                $"{Describe(target, destination)}, so nothing was pasted. " +
                (sourceTarget != target
                    ? "The two targets have different control catalogs."
                    : "A grid cell has no pixel position and a pixel position has no cell — every pasted " +
                      "control would have landed somewhere it was not designed."));
        }

        var minted = new HashSet<string>(StringComparer.Ordinal);
        bool Taken(string id) => minted.Contains(id) || isTaken(id);

        var result = new List<FormControl>();
        foreach (var element in root.Elements())
        {
            var control = FromElement(element, target, destination);
            if (control != null)
            {
                Rename(control, Taken, minted);
                result.Add(control);
            }
        }

        return new FormPasteResult(result, null);
    }

    /// <summary>
    /// ⛔ By NAME only. <c>Enum.TryParse</c> accepts a numeric string ("2" is Canvas), and a fragment is
    /// unvetted text — a number is not a layout name.
    /// </summary>
    private static bool TryLayoutName(string text, out FormLayoutKind kind)
    {
        foreach (var candidate in Enum.GetValues<FormLayoutKind>())
        {
            if (string.Equals(candidate.ToString(), text, StringComparison.Ordinal))
            {
                kind = candidate;
                return true;
            }
        }

        kind = default;
        return false;
    }

    private static string Describe(FormTarget target, FormLayoutKind? layout) => target == FormTarget.WinForms
        ? "WinForms form"
        : layout switch
        {
            FormLayoutKind.Canvas => "pixel-layout (Canvas) web form",
            FormLayoutKind.Flow => "Flow-layout web form",
            _ => "Grid-layout web form"
        };
```

  - `FromElement` (`:447`): signature `FromElement(XElement element, FormTarget target, FormLayoutKind? layout)`. Update the doc param: "the pasted subtree's (target, layout)". Inside, `ReadGeometry(element, target)` becomes `ReadGeometry(element, target, layout)`, `FormControlCatalog.IsStructural(name, target)` becomes `FormControlCatalog.IsStructural(name, target, layout)`, and the nested `FromElement(child, target)` becomes `FromElement(child, target, layout)`.
  - `ReadGeometry` (`:539`): signature `ReadGeometry(XElement element, FormTarget target, FormLayoutKind? layout)`, and `if (target == FormTarget.WinForms)` becomes `if (FormVocabulary.IsPixel(target, layout))`. Update its summary: "Selected by (target, layout) — the same `FormVocabulary.IsPixel` `FormDocumentReader.ReadGeometry` asks — never by sniffing".

- [ ] **Step 6: The real commands carry the layout, and say a refusal.** In `CodeEditorDocumentViewModel.cs`, `CopyControls` (`:572-573`):

```csharp
        _designerClipboard = BasicLang.Forms.FormClipboard.SerializeSubtree(
            file.Model.Target, Selection.Controls, BasicLang.Forms.FormVocabulary.LayoutOf(file.Model));
```

`PasteControls` (`:625-631`), replacing the `DeserializeSubtree` call and the `pasted.Count == 0` return:

```csharp
        var paste = BasicLang.Forms.FormClipboard.Paste(
            _designerClipboard, file.Model.Target, BasicLang.Forms.FormVocabulary.LayoutOf(file.Model),
            id => taken.Contains(id));

        // ⛔ A refused paste is SAID (spec 2026-09-27 §2.1) — between layouts, and between targets, which used
        // to come back empty and silent.
        if (paste.Refusal != null)
        {
            ReportPlacementRefusal(paste.Refusal);
            return;
        }

        var pasted = paste.Controls;
        if (pasted.Count == 0)
        {
            return;
        }
```

- [ ] **Step 7: The retarget's stray-layout check.** `FormRetarget.cs:422`: `FormControlCatalog.IsStructural(name, _to)` becomes `FormControlCatalog.IsStructural(name, _to, _toLayout)`.

- [ ] **Step 8: Doc comments that are now false (spec §2.5).** In `FormGeometry.cs`:
  - `FormTarget.Web` (`:16`) becomes `A .blwebform — browser page, Grid/Flow cells or (Canvas) pixels (D3; spec 2026-09-27 D1).`
  - The `PixelGeometry` summary (`:33-36`) becomes `Pixel geometry: absolute X/Y/Width/Height plus Anchor/Dock — a .blform's, and a Canvas page's (spec 2026-09-27 D2). The WinForms idiom, and what the shipped template writes.`
  - Above `PixelGeometry.Target` (`:39`), add `/// <summary>The target whose NATIVE vocabulary this is. ⚠ A Canvas page borrows it — never decide a document's vocabulary from this; ask FormVocabulary.IsPixel.</summary>`
  - In the `GridGeometry` summary, replace the `⚠ Absolute pixels are NOT the web default and are not modeled here…` paragraph with `⚠ A cell. A web page laid out in pixels is a Canvas page, and its controls carry PixelGeometry (spec 2026-09-27 D1) — pixels are a property of the DOCUMENT's layout, never of one control.`

- [ ] **Step 9: Run.** Build, then filter `FullyQualifiedName~FormPixelVocabularyTests|FullyQualifiedName~FormDocumentTests|FullyQualifiedName~FormStripDocumentTests|FullyQualifiedName~FormDesignerCommandTests|FullyQualifiedName~FormRetargetTests|FullyQualifiedName~FormDocumentReader|FullyQualifiedName~BlFormRoundTripTests|FullyQualifiedName~FormRootLayoutTests`. All green. ⚠ `FormDesignerCommandTests.PastingAWinFormsSubtreeIntoAWebFormIsRefused` must stay green. It asserts nothing landed and the text is unchanged, and now the refusal is also reported.

- [ ] **Step 10: Commit.** Message `feat(forms): a Canvas page's controls in the pixel vocabulary — reader, IsStructural, clipboard; a paste between layouts is refused and said (spec 2026-09-27 §2.1, §2.5)`. Stage `FormControlCatalog.cs`, `FormDocumentReader.cs`, `FormDocument.cs`, `FormRetarget.cs`, `FormGeometry.cs`, `CodeEditorDocumentViewModel.cs` and the new test file.

---

## Task 4: `MobileBreakpoint`, a Canvas-only root row stored on `<Layout>` as raw text

Spec §2.3 (new row, default 600, 0 = never, negative/unparseable → Degraded, stored on `<Layout>`, raw text preserved). Scope call S1. Spec-claims #2: no parity exemption.

**Files:**
- Modify: `BasicLang/Forms/FormGeometry.cs`: `FormLayout` (`:100-128`)
- Modify: `BasicLang/Forms/Serialization/FormDocumentReader.cs`: `ReadLayout` (`:315-331`); `Read` (after Task 2's `var layout = …`)
- Modify: `BasicLang/Forms/Serialization/FormDocumentWriter.cs`: `ApplyLayout` (`:280-284`); `LayoutElement` (`:555-563`)
- Modify: `BasicLang/Forms/FormControlCatalog.cs`: `FormRoot` (after the Gap row)
- Modify: `BasicLang/Forms/FormRootValues.cs`: `Get`, `Set`, `StorageAttributes`
- Modify (tests): `VisualGameStudio.Tests/Compiler/FormRootTests.cs` (`:36-40` samples); `VisualGameStudio.Tests/Compiler/FormRootLayoutTests.cs` (the Canvas expected set)
- Create: `VisualGameStudio.Tests/Compiler/FormMobileBreakpointTests.cs`

- [ ] **Step 1: Write the failing test file.** `VisualGameStudio.Tests/Compiler/FormMobileBreakpointTests.cs`:

```csharp
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.3/§5 — a Canvas page's phone breakpoint: a FormRoot row stored on &lt;Layout&gt;,
/// default 600, 0 = never stack, anything else unusable Degraded and preserved byte-for-byte.
/// </summary>
[TestFixture]
public class FormMobileBreakpointTests
{
    private static FormPropertyDef Row => FormControlCatalog.FormRoot.Property("MobileBreakpoint")!;

    private static string Page(string breakpoint) => $"""
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas" MobileBreakpoint="{breakpoint}"/>
          <Controls/>
        </WebForm>
        """;

    [Test]
    public void TheRow_IsAnIntDefaultingToTheLayoutsDefault_OnACanvasPageOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row.Type, Is.EqualTo(FormPropertyType.Int));
            Assert.That(Row.Default, Is.EqualTo(FormLayout.DefaultMobileBreakpoint.ToString()), "one constant, not two");
            Assert.That(FormRootValues.Applies(Row, FormTarget.Web, FormLayoutKind.Canvas), Is.True);
            Assert.That(FormRootValues.Applies(Row, FormTarget.Web, FormLayoutKind.Grid), Is.False);
            Assert.That(FormRootValues.Applies(Row, FormTarget.Web, FormLayoutKind.Flow), Is.False);
            Assert.That(FormRootValues.Applies(Row, FormTarget.WinForms, null), Is.False);
            Assert.That(FormRootValues.StorageAttributes(Row), Is.Empty, "stored on <Layout>, like Cols/Rows/Gap");
        });
    }

    [Test]
    public void AValue_IsReadRoundTripsAndIsCanon()
    {
        var file = FormDocumentReader.Read("F.blwebform", Page("480"));

        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.Get(file.Model, Row), Is.EqualTo("480"));
            Assert.That(file.Model.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(480));
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(Page("480")));
        });
    }

    [TestCase("-5")]
    [TestCase("abc")]
    [TestCase("48px")]
    public void AnUnusableValue_IsDegraded_AndPreservedThroughAnUnrelatedEdit(string raw)
    {
        var file = FormDocumentReader.Read("F.blwebform", Page(raw));

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("MobileBreakpoint"), Does.Contain(raw).And.Contain("preserved"));
            Assert.That(file.Model.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(FormLayout.DefaultMobileBreakpoint),
                "a Degraded value never reaches the page; the default applies");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(Page(raw)));
        });

        file.Model.Text = "Edited";

        Assert.That(FormDocumentWriter.Write(file), Does.Contain($"MobileBreakpoint=\"{raw}\""),
            "⛔ the model holds the RAW text, so nothing can remove it on a save (spec §2.3)");
    }

    [Test]
    public void Set_AcceptsZeroAndPositive_RefusesNegativeAndText_AndNullRemoves()
    {
        var file = FormDocumentReader.Read("F.blwebform", Page("480"));

        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.Set(file.Model, Row, "0"), Is.True, "0 = never stack");
            Assert.That(file.Model.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(0));
            Assert.That(FormRootValues.Set(file.Model, Row, "-1"), Is.False);
            Assert.That(FormRootValues.Set(file.Model, Row, "wide"), Is.False);
            Assert.That(FormRootValues.Get(file.Model, Row), Is.EqualTo("0"), "a refused value changes nothing");
            Assert.That(FormRootValues.Set(file.Model, Row, "0720"), Is.True);
            Assert.That(FormRootValues.Get(file.Model, Row), Is.EqualTo("720"), "stored as its invariant number");
            Assert.That(FormRootValues.CanReset(Row), Is.True);
            Assert.That(FormRootValues.Set(file.Model, Row, null), Is.True);
            Assert.That(file.Model.Layout.EffectiveMobileBreakpoint, Is.EqualTo(FormLayout.DefaultMobileBreakpoint));
            Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("MobileBreakpoint"));
        });
    }

    [Test]
    public void CreatingACanvasPage_WritesItOnTheLayout()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "F", Width = 800, Height = 450,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = "600" }
        };

        Assert.That(FormDocumentWriter.Create(page), Does.Contain("<Layout Kind=\"Canvas\" MobileBreakpoint=\"600\" />"));
    }

    [Test]
    public void OnAGridPage_ItRoundTripsUntouched_AndIsNotARowThere()
    {
        const string text = """
            <WebForm Name="F" Version="1">
              <Layout Kind="Grid" Cols="auto" Rows="auto" MobileBreakpoint="-9"/>
              <Controls/>
            </WebForm>
            """;
        var file = FormDocumentReader.Read("F.blwebform", text);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Unknown));
            Assert.That(file.DegradedRoot, Is.Empty, "only a Canvas page judges it");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text));
        });
    }

    [Test]
    public void TheGrid_OnACanvasPage_ShowsItWithItsDefault_AndFreezesAnUnusableOne()
    {
        var absent = FormDocumentReader.Read("F.blwebform", """
            <WebForm Name="F" Version="1" Width="640" Height="480">
              <Layout Kind="Canvas"/>
              <Controls/>
            </WebForm>
            """);
        var grid = new FormPropertyGridViewModel();
        grid.Load(absent);
        grid.SelectedControl = null;

        var degraded = FormDocumentReader.Read("F.blwebform", Page("abc"));
        var frozenGrid = new FormPropertyGridViewModel();
        frozenGrid.Load(degraded);
        frozenGrid.SelectedControl = null;

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Single(r => r.Name == "MobileBreakpoint").StringValue, Is.EqualTo("600"));
            Assert.That(frozenGrid.Rows.Single(r => r.Name == "MobileBreakpoint").IsFrozen, Is.True);
        });
    }
}
```

  (⚠ Confirm the frozen-row property's name on `FormPropertyRow` before running. The slice-2 pre-flight uses `IsFrozen`, `FormPropertyRow.cs:192-196`. If it moved, use the current name.)

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS0117: 'FormLayout' does not contain a definition for 'DefaultMobileBreakpoint'`, plus errors for `EffectiveMobileBreakpoint` and `MobileBreakpoint`. Right reason.

- [ ] **Step 3: `FormLayout` holds the raw text.** In `FormGeometry.cs`, add inside `FormLayout` after `Dir`:

```csharp
    /// <summary>The page width, in pixels, below which a Canvas page stacks into one column when none is set.</summary>
    public const int DefaultMobileBreakpoint = 600;

    /// <summary>
    /// A Canvas page's phone breakpoint (spec 2026-09-27 §2.3, §5), as the document's RAW text — e.g.
    /// <c>"600"</c>; <c>"0"</c> never stacks. Null = absent (the default applies). Canvas only.
    ///
    /// <para>⛔ Raw text, like <see cref="Cols"/>, never a parsed int: a value that cannot be used is then
    /// never null in the model, so no save can remove it (the writer removes an attribute whose model value
    /// is null). Its Degraded tier comes from the reader; its number from
    /// <see cref="EffectiveMobileBreakpoint"/>.</para>
    /// </summary>
    public string? MobileBreakpoint { get; set; }

    /// <summary>
    /// The breakpoint the page uses: the document's value when usable, else <see cref="DefaultMobileBreakpoint"/>
    /// — a Degraded value never reaches generated output, exactly as a Degraded control property does not.
    /// </summary>
    public int EffectiveMobileBreakpoint =>
        TryParseMobileBreakpoint(MobileBreakpoint, out var pixels) ? pixels : DefaultMobileBreakpoint;

    /// <summary>
    /// A usable breakpoint: 0 or a positive whole number, read culture-free with the catalog's parser
    /// (<see cref="FormPropertyDef.TryParseInt"/>). ⛔ The ONE rule — the reader's Degraded check,
    /// <see cref="FormRootValues.Set"/> and <see cref="EffectiveMobileBreakpoint"/> all ask it.
    /// </summary>
    public static bool TryParseMobileBreakpoint(string? text, out int pixels)
    {
        if (text != null && FormPropertyDef.TryParseInt(text, out pixels) && pixels >= 0)
        {
            return true;
        }

        pixels = DefaultMobileBreakpoint;
        return false;
    }
```

  `Clone` (`:124-127`) becomes `new() { Kind = Kind, Cols = Cols, Rows = Rows, Gap = Gap, Dir = Dir, MobileBreakpoint = MobileBreakpoint };`. Also update the `FormLayout` summary's example to `<Layout Kind="Canvas" MobileBreakpoint="600"/>`.

- [ ] **Step 4: The reader.** In `ReadLayout` (`:317-323`), add `MobileBreakpoint = (string?)element.Attribute("MobileBreakpoint")` to the initializer. In `Read`, directly after `var layout = FormVocabulary.LayoutOf(model);` (added in Task 2), add:

```csharp
        // The Canvas page's phone breakpoint (spec 2026-09-27 §2.3). Stored as RAW text, so an unusable value
        // is preserved by construction; the tier is what says it cannot be used. ⚠ Judged on a Canvas page
        // only — on a Grid/Flow page it is not a row, and it round-trips untouched.
        if (layout == FormLayoutKind.Canvas &&
            model.Layout?.MobileBreakpoint is { } rawBreakpoint &&
            !FormLayout.TryParseMobileBreakpoint(rawBreakpoint, out _))
        {
            degradedRoot.Add(new DegradedProperty("", "MobileBreakpoint", rawBreakpoint,
                $"the page's phone breakpoint could not be used — MobileBreakpoint=\"{rawBreakpoint}\" must be 0 " +
                "(never stack) or a positive whole number of pixels, so the page stacks below the default " +
                $"{FormLayout.DefaultMobileBreakpoint}px. The attribute is preserved exactly as written."));
        }
```

- [ ] **Step 5: The writer.** `LayoutElement`: add `element.SetAttributeValue("MobileBreakpoint", layout.MobileBreakpoint);` after the `Dir` line. `ApplyLayout`: add `SetAttributeIfChanged(element, "MobileBreakpoint", model.Layout.MobileBreakpoint);` after the `Dir` line. (⛔ Remove-on-null is correct here. Null means absent in this storage (S1); an unusable value is non-null raw text.)

- [ ] **Step 6: The row.** In `FormControlCatalog.cs`, after the Gap row added in Task 2:

```csharp
            // ⛔ Web CANVAS only (spec 2026-09-27 §2.3, §5): below this page width the controls stack into one
            // column. 0 = never stack. Stored on <Layout> as raw text (FormLayout.MobileBreakpoint);
            // FormRootValues.StorageAttributes says so. ⚠ The default is FormLayout's constant, never a second copy.
            new("MobileBreakpoint", FormPropertyType.Int,
                FormLayout.DefaultMobileBreakpoint.ToString(CultureInfo.InvariantCulture),
                Targets: new[] { FormTarget.Web },
                Category: FormPropertyCategory.Layout,
                Description: "Below this page width, in pixels, the controls stack into one column for phones. 0 never stacks.",
                WebLayouts: new[] { FormLayoutKind.Canvas }),
```

- [ ] **Step 7: The storage map.** In `FormRootValues.cs`:
  - `Get`: add `"MobileBreakpoint" => form.Layout?.MobileBreakpoint,` before the throwing arm.
  - `StorageAttributes`: `"Cols" or "Rows" or "Gap" => Array.Empty<string>(),` becomes `"Cols" or "Rows" or "Gap" or "MobileBreakpoint" => Array.Empty<string>(),`.
  - `Set`: add before `default:`

```csharp
            case "MobileBreakpoint":
                if (value == null)
                {
                    if (form.Layout != null)
                    {
                        form.Layout.MobileBreakpoint = null;
                    }

                    return true;
                }

                // ⛔ Refused, never coerced (spec §7): the grid cannot manufacture a Degraded value of its own.
                if (!FormLayout.TryParseMobileBreakpoint(value, out var pixels))
                {
                    return false;
                }

                (form.Layout ??= new FormLayout()).MobileBreakpoint =
                    pixels.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return true;
```

- [ ] **Step 8: Existing tests.**
  - `FormRootTests.cs:36-40`: add `[FormPropertyType.Int] = "600",` to the samples dictionary.
  - `FormRootLayoutTests`: the `(Web Canvas)` row's expected set becomes `new[] { "Text", "ClientSize", "MobileBreakpoint" }`. Delete the "RE-CHECK IN TASK 4" line.

- [ ] **Step 9: Run.** Build, then filter `FullyQualifiedName~FormMobileBreakpointTests|FullyQualifiedName~FormRootTests|FullyQualifiedName~FormRootLayoutTests|FullyQualifiedName~FormRootRetargetTests|FullyQualifiedName~WinFormsCatalogParityTests|FullyQualifiedName~FormCssTests|FullyQualifiedName~FormPropertyGrid`. All green. `WinFormsCatalogParityTests.EveryRowAndEvent_DeclaresACategoryAndADescription` covers the new row, and it has both. The parity comparison skips it (web-only, spec-claims #2).

- [ ] **Step 10: Commit.** Message `feat(forms): MobileBreakpoint — a Canvas-only Form row on <Layout>, raw text preserved, Degraded when unusable (spec 2026-09-27 §2.3)`. Stage `FormGeometry.cs`, `FormDocumentReader.cs`, `FormDocumentWriter.cs`, `FormControlCatalog.cs`, `FormRootValues.cs`, `FormRootTests.cs`, `FormRootLayoutTests.cs` and the new test file.

---

## Task 5: New web forms are Canvas pages; tests that build from a Grid scaffold pin Grid

Spec §2.5, D1 ("It is the default for NEW web forms"). Scope calls S5, S7.

⚠ **From this commit until Task 9, a NEW web form opened in the IDE cannot take a drop.** `PlaceOnWeb` refuses non-Grid. The branch is internal and nothing is dropped into `IDE\` before Task 16 (Traps).

**Files:**
- Modify: `BasicLang/Forms/FormScaffolder.cs`: `using`s (`:1`); `Create` (`:68-126`)
- Modify: `BasicLang/Forms/FormGeometry.cs`: `FormLayoutKind.Canvas`'s doc (`:93-97`)
- Modify (tests): `VisualGameStudio.Tests/Compiler/FormScaffolderTests.cs` (add tests after `:116`); `VisualGameStudio.Tests/Compiler/FormDesignerAcceptanceTests.cs:114`; `VisualGameStudio.Tests/Compiler/FormComponentAcceptanceTests.cs:58`; `VisualGameStudio.Tests/Compiler/FormMenuAcceptanceTests.cs:54`; `VisualGameStudio.Tests/Compiler/FormDesignerCommandTests.cs:512`

- [ ] **Step 1: Write the failing tests** by Edit into `FormScaffolderTests.cs`, after `Create_ProducesADocumentThatReadsBackClean` (`:116`):

```csharp
    /// <summary>
    /// ⛔ Spec 2026-09-27 D1: a NEW web form is a Canvas page, designed like a WinForms form — the WinForms
    /// scaffold's design size and the default phone breakpoint.
    /// </summary>
    [Test]
    public void Create_Web_IsACanvasPage_OfTheWinFormsDesignSize()
    {
        var scaffold = FormScaffolder.Create("LoginForm");
        var file = FormDocumentReader.Read("LoginForm.blwebform", scaffold.DocumentText);
        var window = FormDocumentReader.Read("LoginForm.blform", FormScaffolder.Create("LoginForm", FormTarget.WinForms).DocumentText);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Format())));
            Assert.That(file.Model.Layout!.Kind, Is.EqualTo(FormLayoutKind.Canvas));
            Assert.That(file.Model.Layout.MobileBreakpoint, Is.EqualTo(FormLayout.DefaultMobileBreakpoint.ToString()));
            Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((window.Model.Width, window.Model.Height)),
                "the WinForms scaffold's design size (spec §2.5)");
            Assert.That(file.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(scaffold.DocumentText, Does.Contain("<Layout Kind=\"Canvas\" MobileBreakpoint=\"600\" />"));
        });
    }

    [Test]
    public void Create_Web_Grid_IsTheGridScaffold_Unchanged()
    {
        var file = FormDocumentReader.Read(
            "LoginForm.blwebform", FormScaffolder.Create("LoginForm", FormTarget.Web, FormLayoutKind.Grid).DocumentText);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Layout!.Kind, Is.EqualTo(FormLayoutKind.Grid));
            Assert.That(file.Model.Layout.Cols, Is.EqualTo("auto,1fr"));
            Assert.That(file.Model.Layout.Rows, Is.EqualTo("auto"));
            Assert.That(file.Model.Layout.Gap, Is.EqualTo("8px"));
            Assert.That(file.Model.Width, Is.Null, "a Grid page has no design size");
        });
    }

    [Test]
    public void Create_Web_Flow_IsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FormScaffolder.Create("LoginForm", FormTarget.Web, FormLayoutKind.Flow));
    }
```

  (Add `using BasicLang.Forms.Serialization;` at the top of `FormScaffolderTests.cs` if `PropertyTier` is not already imported.)

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS1501: No overload for method 'Create' takes 3 arguments`. Right reason.

- [ ] **Step 3: Implement.** In `FormScaffolder.cs`, add `using System.Globalization;`. Add before `Create`:

```csharp
    /// <summary>
    /// 800x450 is the size <c>dotnet new winforms</c> gives a new form, so a form created here and one created
    /// by the shipped template open the same size. ⛔ A new Canvas PAGE takes the same design size (spec
    /// 2026-09-27 §2.5): one number for "a new form's size", on both targets.
    /// </summary>
    private const int DesignWidth = 800;
    private const int DesignHeight = 450;
```

Change the signature to `public static FormScaffold Create(string formName, FormTarget target = FormTarget.Web, FormLayoutKind webLayout = FormLayoutKind.Canvas)`. Add a `<param name="webLayout">` doc: `A new web form's layout: Canvas (spec 2026-09-27 D1, the default — designed like a WinForms form) or Grid (the pre-piece-1 scaffold, which tests that build their page from a Grid scaffold pin). There is no Flow scaffold. Ignored for WinForms.`. Replace the body's `if (target == FormTarget.Web) { … } else { … }` (`:101-117`) with:

```csharp
        if (target == FormTarget.Web)
        {
            document.Layout = webLayout switch
            {
                FormLayoutKind.Canvas => new FormLayout
                {
                    Kind = FormLayoutKind.Canvas,
                    MobileBreakpoint = FormLayout.DefaultMobileBreakpoint.ToString(CultureInfo.InvariantCulture)
                },
                FormLayoutKind.Grid => new FormLayout
                {
                    Kind = FormLayoutKind.Grid, Cols = "auto,1fr", Rows = "auto", Gap = "8px"
                },
                _ => throw new ArgumentOutOfRangeException(nameof(webLayout), webLayout,
                    "a new web form is a Canvas page (the default) or a Grid page; there is no Flow scaffold.")
            };

            if (webLayout == FormLayoutKind.Canvas)
            {
                document.Width = DesignWidth;
                document.Height = DesignHeight;
            }
        }
        else
        {
            // D3's other half: a window has a size and a caption where a page has a layout.
            document.Width = DesignWidth;
            document.Height = DesignHeight;
            document.Text = formName;
        }
```

- [ ] **Step 4: The enum's doc is now false.** `FormGeometry.cs:93-97` (`FormLayoutKind.Canvas`) becomes:

```csharp
    /// <summary>
    /// Absolute pixels, designed like a WinForms form (spec 2026-09-27 D1): the page stores a design size and
    /// per-control X/Y/Width/Height/Anchor/Dock, is exact at the design size, follows Anchor/Dock on resize and
    /// stacks below <see cref="FormLayout.MobileBreakpoint"/>. The DEFAULT for a new web form. The IDE may
    /// label it "Pixel"; the XML spelling stays <c>Canvas</c>.
    /// </summary>
```

- [ ] **Step 5: Pin Grid where a test builds its page from the web scaffold** (S7, spec-claims #6). Edit each call to pass `FormLayoutKind.Grid`, with the one-line comment `// ⚠ Pinned to Grid (spec 2026-09-27 §2.5): this path was written against the Grid scaffold; the Canvas twin is Task 14.`:
  - `FormDesignerAcceptanceTests.cs:114`: `FormScaffolder.Create("LoginForm", target)` becomes `FormScaffolder.Create("LoginForm", target, FormLayoutKind.Grid)`. (It drops Label/TextBox/Button; `PlaceOnWeb` refuses a Canvas page until Task 9.)
  - `FormComponentAcceptanceTests.cs:58`: the same change.
  - `FormMenuAcceptanceTests.cs:54`: the same change.
  - `FormDesignerCommandTests.cs:512`: `FormScaffolder.Create("WebForm", FormTarget.Web)` becomes `FormScaffolder.Create("WebForm", FormTarget.Web, FormLayoutKind.Grid)`.

  Then grep `FormScaffolder.Create(` across `VisualGameStudio.Tests`. Confirm every OTHER web call uses only `CodeText`, the file names, or an explicit document (as listed in spec-claims #6), and record the verdict per file in the commit message.

- [ ] **Step 6: Run.** Build, then:
  - Fast: filter `FullyQualifiedName~FormScaffolderTests|FullyQualifiedName~FormDesignerCommandTests|FullyQualifiedName~FormCanvasDropTests|FullyQualifiedName~FormCodeBehindWriteTests|FullyQualifiedName~FormHandlerGestureTests|FullyQualifiedName~FormRetargetPairTests|FullyQualifiedName~FormRetargetTests`.
  - Integration, because the pins touch them: filter `FullyQualifiedName~FormDesignerAcceptanceTests|FullyQualifiedName~FormComponentAcceptanceTests|FullyQualifiedName~FormMenuAcceptanceTests`.
  - Compare failure NAMES with `baseline-int.txt`. Zero new names.

- [ ] **Step 7: Commit.** Message `feat(forms): a new web form is a Canvas page of the WinForms design size; Grid-scaffold paths pinned (spec 2026-09-27 §2.5, D1)`. Stage `FormScaffolder.cs`, `FormGeometry.cs` and the five test files.

---

## Task 6: `FormDocument.DesignSize` and the pure `FormDockLayout` resolver

Spec §3 (ONE shared layout function; band height = the strip row's `DefaultHeight`), §4 (Dock resolved once, strips + docked controls as ONE document-ordered sequence; document order is docking order), §7 (table tests). Scope calls S3, S9, S11. **Pure: no consumer changes here.** The canvas moves onto it in Task 9 and the emitter in Task 10.

**Files:**
- Modify: `BasicLang/Forms/FormDocument.cs`: add `DesignSize` next to `Width`/`Height` (`:49-53`)
- Modify: `VisualGameStudio.Shell/Controls/FormCanvasTransform.cs`: `SurfaceSize` (`:195-211`)
- Create: `BasicLang/Forms/FormDockLayout.cs`
- Create: `VisualGameStudio.Tests/Compiler/FormDockLayoutTests.cs`

- [ ] **Step 1: Write the failing test file.** `VisualGameStudio.Tests/Compiler/FormDockLayoutTests.cs`:

```csharp
using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §3/§4/§7 — the ONE resolver of where every DOCKED thing sits: strips (their Dock property)
/// and docked controls (PixelGeometry.Dock), as ONE sequence in DOCUMENT order, which is WinForms' docking
/// order (back-most docks first). Pure; the canvas (Task 9) and the page (Task 10) both call it.
/// ⚠ The WinForms reference harness (Task 12/13) is the arbiter of every row here; where it disagrees, it wins.
/// </summary>
[TestFixture]
public class FormDockLayoutTests
{
    private static FormControl Strip(string kind, string id, string? dock = null)
    {
        var strip = new FormControl { Kind = kind, Id = id };
        if (dock != null)
        {
            strip.Properties["Dock"] = dock;
        }

        return strip;
    }

    private static FormControl Box(string id, int width, int height, string? dock, int x = 0, int y = 0) => new()
    {
        Kind = "Panel", Id = id,
        Geometry = new PixelGeometry { X = x, Y = y, Width = width, Height = height, Dock = dock }
    };

    private static FormDocument Window(params FormControl[] controls)
    {
        var document = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        document.Controls.AddRange(controls);
        return document;
    }

    private static int Band(string kind) => FormControlCatalog.Find(kind)!.DefaultHeight;

    private static FormRect At(FormDocument document, string id) =>
        FormDockLayout.Resolve(document).All.Single(d => d.Control.Id == id).Bounds;

    [Test]
    public void TheBandHeights_AreTheHeightsTheCanvasDraws()
    {
        Assert.That((Band("MenuStrip"), Band("ToolStrip"), Band("StatusStrip")), Is.EqualTo((24, 25, 22)),
            "spec §3: MenuStrip 24, ToolStrip 25, StatusStrip 22 (FormControlCatalog.cs:1808/1833/1854)");
    }

    [TestCase("Top", 0, 0, 400, 50)]
    [TestCase("Bottom", 0, 250, 400, 50)]
    [TestCase("Left", 0, 0, 80, 300)]
    [TestCase("Right", 320, 0, 80, 300)]
    [TestCase("Fill", 0, 0, 400, 300)]
    [TestCase("fill", 0, 0, 400, 300)]
    public void EachDockValue_Alone(string dock, int x, int y, int width, int height)
    {
        var document = Window(Box("p", 80, 50, dock));

        Assert.That(At(document, "p"), Is.EqualTo(new FormRect(x, y, width, height)));
    }

    [Test]
    public void Strips_StackFromTheirEdge_InDocumentOrder_AtTheirBandHeights()
    {
        var document = Window(
            Strip("MenuStrip", "menu"), Strip("ToolStrip", "tools"),
            Strip("StatusStrip", "status"), Strip("StatusStrip", "status2"));

        Assert.Multiple(() =>
        {
            Assert.That(At(document, "menu"), Is.EqualTo(new FormRect(0, 0, 400, 24)));
            Assert.That(At(document, "tools"), Is.EqualTo(new FormRect(0, 24, 400, 25)));
            Assert.That(At(document, "status"), Is.EqualTo(new FormRect(0, 278, 400, 22)),
                "the FIRST-documented bottom strip docks first, so it is ON the edge");
            Assert.That(At(document, "status2"), Is.EqualTo(new FormRect(0, 256, 400, 22)));
        });
    }

    [Test]
    public void AStripWithNoDockAttribute_TakesItsRowsDefaultEdge()
    {
        var document = Window(Strip("StatusStrip", "status"));

        Assert.That(At(document, "status").Y, Is.EqualTo(300 - Band("StatusStrip")), "StatusStrip's row default is Bottom");
    }

    [Test]
    public void AFill_BetweenAMenuAndAStatusStrip_TakesWhatIsLeft()
    {
        var document = Window(Strip("MenuStrip", "menu"), Strip("StatusStrip", "status"), Box("fill", 10, 10, "Fill"));

        Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(0, 24, 400, 300 - 24 - 22)),
            "spec §4's example: top:24; left:0; right:0; bottom:22");
    }

    [Test]
    public void ADockTopPanel_BeforeTheMenuStrip_TakesTheTopEdge_AndTheMenuSitsBelowIt()
    {
        var document = Window(Box("p", 10, 50, "Top"), Strip("MenuStrip", "menu"));

        Assert.Multiple(() =>
        {
            Assert.That(At(document, "p"), Is.EqualTo(new FormRect(0, 0, 400, 50)));
            Assert.That(At(document, "menu"), Is.EqualTo(new FormRect(0, 50, 400, 24)),
                "document order IS docking order — on the canvas, the page and in WinForms alike (spec §4)");
        });
    }

    [Test]
    public void ALeftDock_ThenATopDock_TheTopStartsRightOfTheLeft()
    {
        var document = Window(Box("left", 80, 10, "Left"), Box("top", 10, 40, "Top"));

        Assert.That(At(document, "top"), Is.EqualTo(new FormRect(80, 0, 320, 40)));
    }

    [Test]
    public void AFill_DoesNotConsume_SoALaterTopOverlapsIt()
    {
        // ⚠ WinForms' DefaultLayout: Fill takes the remaining rectangle and leaves it unchanged (S9).
        var document = Window(Box("fill", 10, 10, "Fill"), Box("top", 10, 40, "Top"));

        Assert.Multiple(() =>
        {
            Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(0, 0, 400, 300)));
            Assert.That(At(document, "top"), Is.EqualTo(new FormRect(0, 0, 400, 40)));
        });
    }

    [Test]
    public void UndockedControls_AreNotResolved_AndConsumeNothing()
    {
        var document = Window(Box("free", 100, 100, null, x: 5, y: 5), Box("none", 10, 10, "None"),
            Box("bogus", 10, 10, "Middle"), Box("top", 10, 40, "Top"));
        var ids = FormDockLayout.Resolve(document).All.Select(d => d.Control.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.EqualTo(new[] { "top" }));
            Assert.That(At(document, "top").Y, Is.Zero);
        });
    }

    [Test]
    public void Overflow_ClampsWhatIsLeft_AtZero()
    {
        var document = Window(Box("tall", 10, 400, "Top"), Box("fill", 10, 10, "Fill"));

        Assert.That(At(document, "fill").Height, Is.Zero);
    }

    [Test]
    public void ADockedChild_DocksInsideItsContainer_AtTheContainersResolvedSize()
    {
        var panel = Box("pnl", 10, 10, "Fill");
        panel.Children.Add(new FormControl
        {
            Kind = "Button", Id = "ok",
            Geometry = new PixelGeometry { X = 3, Y = 3, Width = 75, Height = 30, Dock = "Bottom" }
        });
        var document = Window(Strip("MenuStrip", "menu"), panel);
        var ok = FormDockLayout.Resolve(document).All.Single(d => d.Control.Id == "ok");

        Assert.Multiple(() =>
        {
            Assert.That(ok.Bounds, Is.EqualTo(new FormRect(0, 276 - 30, 400, 30)), "relative to the panel's client origin");
            Assert.That((ok.ContainerWidth, ok.ContainerHeight), Is.EqualTo((400, 276)),
                "the panel's own resolved Fill size, not its stored 10x10");
        });
    }

    [Test]
    public void TryGet_FindsExactlyTheDockedControls()
    {
        var document = Window(Box("free", 10, 10, null), Box("top", 10, 40, "Top"));
        var layout = FormDockLayout.Resolve(document);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(document.Controls[1], out var top), Is.True);
            Assert.That(top.Edge, Is.EqualTo(FormDockEdge.Top));
            Assert.That(layout.TryGet(document.Controls[0], out _), Is.False);
        });
    }

    [Test]
    public void EdgeOf_IsTheOneAnswer_ForAStripAndForADockedControl()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormDockLayout.EdgeOf(Strip("MenuStrip", "m")), Is.EqualTo(FormDockEdge.Top));
            Assert.That(FormDockLayout.EdgeOf(Strip("MenuStrip", "m", "Bottom")), Is.EqualTo(FormDockEdge.Bottom));
            Assert.That(FormDockLayout.EdgeOf(Box("p", 1, 1, " Right ")), Is.EqualTo(FormDockEdge.Right));
            Assert.That(FormDockLayout.EdgeOf(Box("p", 1, 1, null)), Is.Null);
            Assert.That(FormDockLayout.EdgeOf(new FormControl { Kind = "Timer", Id = "t" }), Is.Null, "a component has no place");
        });
    }

    [Test]
    public void TheDesignSize_IsWhatTheCanvasSurfaceSizeReports()
    {
        var documents = new[]
        {
            new FormDocument { Target = FormTarget.WinForms, Name = "A", Width = 640, Height = 480 },
            new FormDocument { Target = FormTarget.Web, Name = "B" },
            new FormDocument { Target = FormTarget.WinForms, Name = "C", Width = 0, Height = -5 },
        };

        Assert.Multiple(() =>
        {
            foreach (var document in documents)
            {
                var surface = FormCanvasTransform.SurfaceSize(document);
                Assert.That(((int)surface.Width, (int)surface.Height), Is.EqualTo(document.DesignSize),
                    $"{document.Name}: ONE answer to how big the form is (spec §2.4)");
            }

            Assert.That(documents[1].DesignSize, Is.EqualTo((FormDocument.DefaultDesignWidth, FormDocument.DefaultDesignHeight)));
        });
    }

    [Test]
    public void ResolvingAPageWithNoSize_UsesTheDesignSizeFallback()
    {
        var document = new FormDocument
        {
            Target = FormTarget.Web, Name = "P", Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        document.Controls.Add(Box("fill", 1, 1, "Fill"));

        Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(0, 0, FormDocument.DefaultDesignWidth, FormDocument.DefaultDesignHeight)));
    }
}
```

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS0103: The name 'FormDockLayout' does not exist`, plus errors for `FormRect` and `DesignSize`. Right reason.

- [ ] **Step 3: `DesignSize`.** In `FormDocument.cs`, after `Height` (`:53`):

```csharp
    /// <summary>The design size a pixel document is given when it carries none (or a non-positive one).</summary>
    public const int DefaultDesignWidth = 400;
    public const int DefaultDesignHeight = 300;

    /// <summary>
    /// ⛔⛔ THE one answer to "how big is the form" (spec 2026-09-27 §2.4, scope call S3): the client size, or
    /// <see cref="DefaultDesignWidth"/>×<see cref="DefaultDesignHeight"/>. The canvas surface
    /// (<c>FormCanvasTransform.SurfaceSize</c>), the dock resolver and the page emitter all read it — a second
    /// copy of the fallback puts the page's anchors somewhere other than the surface the user designed on.
    /// </summary>
    public (int Width, int Height) DesignSize =>
        (Width is > 0 ? Width.Value : DefaultDesignWidth, Height is > 0 ? Height.Value : DefaultDesignHeight);
```

  In `FormCanvasTransform.cs`, `SurfaceSize` (`:205-211`) body becomes:

```csharp
        ArgumentNullException.ThrowIfNull(document);

        // ⛔ Adapts FormDocument.DesignSize — the answer now lives in BasicLang, where the page emitter and
        // FormDockLayout can reach it too. Never re-derive the fallback here.
        var (width, height) = document.DesignSize;
        return new Size(width, height);
```

  Add to its summary: `⚠ Since spec 2026-09-27 this adapts FormDocument.DesignSize (the page emitter needs the same number and cannot call a Shell method).`

- [ ] **Step 4: The resolver.** `BasicLang/Forms/FormDockLayout.cs`:

```csharp
namespace BasicLang.Forms;

/// <summary>A rectangle in form pixels, relative to its container's client origin.</summary>
public readonly record struct FormRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>The edge a docked thing takes — WinForms' DockStyle, minus None.</summary>
public enum FormDockEdge
{
    Top,
    Bottom,
    Left,
    Right,
    Fill
}

/// <summary>One docked thing, resolved.</summary>
/// <param name="Bounds">Where it sits, relative to its container's client origin.</param>
/// <param name="ContainerWidth">The client width it was docked within — the reference for its CSS insets.</param>
/// <param name="ContainerHeight">The client height it was docked within.</param>
public readonly record struct FormDockedBounds(
    FormControl Control, FormDockEdge Edge, FormRect Bounds, int ContainerWidth, int ContainerHeight);

/// <summary>Every docked thing in a document, and a lookup by control.</summary>
public sealed class FormDockLayoutResult
{
    private readonly Dictionary<FormControl, FormDockedBounds> _byControl;

    internal FormDockLayoutResult(List<FormDockedBounds> all)
    {
        All = all;
        _byControl = new Dictionary<FormControl, FormDockedBounds>(ReferenceEqualityComparer.Instance);
        foreach (var docked in all)
        {
            _byControl[docked.Control] = docked;
        }
    }

    /// <summary>Per sibling list in document order; a container's docked children after its siblings.</summary>
    public IReadOnlyList<FormDockedBounds> All { get; }

    public bool TryGet(FormControl control, out FormDockedBounds docked) => _byControl.TryGetValue(control, out docked);
}

/// <summary>
/// ⛔⛔ THE one answer to "where does every DOCKED thing sit" (spec 2026-09-27 §3, §4): the strips (a
/// <see cref="FormPlace.Docked"/> row's Dock PROPERTY) and the docked controls (<see cref="PixelGeometry.Dock"/>),
/// resolved as ONE sequence. The canvas (<c>FormCanvasTransform.Bands</c>/<c>BoundsOf</c>) and the page emitter
/// both call it — two copies of this stacking algebra is the <c>Tracks</c>/<c>ParseTracks</c> scar a third time.
///
/// <para>⛔ Docking order, in MODEL terms, stated this way on purpose given this repo's z-order history:
/// WinForms docks back-most first, and the model's DOCUMENT order is back-to-front ("last in the list is in
/// front", <see cref="FormDocument.BringToFront"/>) — so the FIRST in the document docks FIRST and takes the
/// outermost edge. A Dock=Top Panel that precedes the MenuStrip sits above it, on the canvas, on the page and in
/// WinForms alike.</para>
///
/// <para>⚠ A strip's band height is its row's <see cref="FormControlDef.DefaultHeight"/> (24/25/22), not a
/// measured content height (spec §3). ⚠ Fill takes what is left and does NOT consume it (WinForms'
/// DefaultLayout); what is left never goes negative. ⚠ A container's client area is taken to be its bounds —
/// a GroupBox's caption inset is a recorded gap (plan spec-claims #11). The WinForms reference harness is the
/// arbiter of all three.</para>
/// </summary>
public static class FormDockLayout
{
    private static readonly FormDockEdge[] Edges = Enum.GetValues<FormDockEdge>();

    /// <summary>Every docked thing in <paramref name="document"/>, at every depth, at its design size.</summary>
    public static FormDockLayoutResult Resolve(FormDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var (width, height) = document.DesignSize;
        var all = new List<FormDockedBounds>();
        Walk(document.Controls, width, height, all);
        return new FormDockLayoutResult(all);
    }

    /// <summary>
    /// The docked siblings in <paramref name="siblings"/>, in document order, inside a client area of
    /// <paramref name="width"/>×<paramref name="height"/>. Undocked siblings are skipped and consume nothing.
    /// </summary>
    public static IReadOnlyList<FormDockedBounds> ResolveSiblings(
        IReadOnlyList<FormControl> siblings, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(siblings);

        var clientWidth = Math.Max(0, width);
        var clientHeight = Math.Max(0, height);
        var remaining = new FormRect(0, 0, clientWidth, clientHeight);
        var placed = new List<FormDockedBounds>();

        foreach (var control in siblings)
        {
            if (EdgeOf(control) is not { } edge)
            {
                continue;
            }

            var (ownWidth, ownHeight) = OwnSize(control);
            FormRect bounds;

            switch (edge)
            {
                case FormDockEdge.Top:
                    bounds = new FormRect(remaining.X, remaining.Y, remaining.Width, ownHeight);
                    remaining = new FormRect(
                        remaining.X, remaining.Y + ownHeight, remaining.Width, Math.Max(0, remaining.Height - ownHeight));
                    break;

                case FormDockEdge.Bottom:
                    bounds = new FormRect(remaining.X, remaining.Bottom - ownHeight, remaining.Width, ownHeight);
                    remaining = remaining with { Height = Math.Max(0, remaining.Height - ownHeight) };
                    break;

                case FormDockEdge.Left:
                    bounds = new FormRect(remaining.X, remaining.Y, ownWidth, remaining.Height);
                    remaining = new FormRect(
                        remaining.X + ownWidth, remaining.Y, Math.Max(0, remaining.Width - ownWidth), remaining.Height);
                    break;

                case FormDockEdge.Right:
                    bounds = new FormRect(remaining.Right - ownWidth, remaining.Y, ownWidth, remaining.Height);
                    remaining = remaining with { Width = Math.Max(0, remaining.Width - ownWidth) };
                    break;

                default:
                    // Fill: what is left, left as it is (WinForms' DefaultLayout does not consume it).
                    bounds = remaining;
                    break;
            }

            placed.Add(new FormDockedBounds(control, edge, bounds, clientWidth, clientHeight));
        }

        return placed;
    }

    /// <summary>
    /// The edge <paramref name="control"/> docks to, or null when it does not dock. ⛔ The one answer: a strip
    /// through <see cref="FormControl.IsDockedToBottom"/> (its Dock PROPERTY, row default included); a
    /// positioned control through <see cref="PixelGeometry.Dock"/> — trimmed, case-insensitive; "None" and an
    /// unknown name do not dock. Tray components and items never do.
    /// </summary>
    public static FormDockEdge? EdgeOf(FormControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        switch (control.Definition?.Place ?? FormPlace.Positioned)
        {
            case FormPlace.Docked:
                return control.IsDockedToBottom ? FormDockEdge.Bottom : FormDockEdge.Top;

            case FormPlace.Positioned when control.Geometry is PixelGeometry { Dock: { } dock }:
                var name = dock.Trim();
                foreach (var edge in Edges)
                {
                    if (string.Equals(edge.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return edge;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static void Walk(IReadOnlyList<FormControl> siblings, int width, int height, List<FormDockedBounds> all)
    {
        var docked = ResolveSiblings(siblings, width, height);
        all.AddRange(docked);

        foreach (var control in siblings)
        {
            // Only a POSITIONED container holds controls: a strip's children are items, placed by the canvas's
            // band layout and by the page's markup, never docked.
            if (control.Definition?.Place is not (null or FormPlace.Positioned) || control.Children.Count == 0)
            {
                continue;
            }

            var own = docked.FirstOrDefault(d => ReferenceEquals(d.Control, control));
            var (innerWidth, innerHeight) = own.Control != null
                ? (own.Bounds.Width, own.Bounds.Height)
                : OwnSize(control);

            Walk(control.Children, innerWidth, innerHeight, all);
        }
    }

    /// <summary>A strip's band height (its row's DefaultHeight — spec §3), or a control's stored size.</summary>
    private static (int Width, int Height) OwnSize(FormControl control) =>
        control.Definition?.Place == FormPlace.Docked
            ? (0, control.Definition.DefaultHeight)
            : control.Geometry is PixelGeometry pixel
                ? (Math.Max(0, pixel.Width), Math.Max(0, pixel.Height))
                : (0, 0);
}
```

- [ ] **Step 5: Run.** Build, then filter `FullyQualifiedName~FormDockLayoutTests|FullyQualifiedName~FormCanvasTransformTests|FullyQualifiedName~FormPlacementTests|FullyQualifiedName~FormGridLayoutTests`. All green. `SurfaceSize`'s existing tests prove the delegation changed nothing.

- [ ] **Step 6: Commit.** Message `feat(forms): FormDockLayout — one resolver for strips and docked controls in document order; FormDocument.DesignSize is the one form size (spec 2026-09-27 §3, §4)`. Stage `FormDocument.cs`, `FormCanvasTransform.cs`, `FormDockLayout.cs` and the new test file.

---

## Task 7: `FormAnchor` (one parser) and the pure `FormAnchorCss` mapping

Spec §4 (the Anchor table, the WinForms centring formula, Dock → fixed insets), §7 ("every combination, including the centring formula"). Scope call S10.

**Files:**
- Create: `BasicLang/Forms/FormAnchorCss.cs` (`FormAnchorEdges`, `FormAnchor`, `FormAnchorCss`)
- Modify: `BasicLang/Forms/RegionWriter.cs`: `AnchorFlags` (`:154-164`); `CheckAnchors` (`:202`); `SplitAnchor` (`:215-216`); `AnchorExpression` (`:1063-1075`)
- Create: `VisualGameStudio.Tests/Compiler/FormAnchorCssTests.cs`

- [ ] **Step 1: Write the failing test file.** `VisualGameStudio.Tests/Compiler/FormAnchorCssTests.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §4 — Anchor and Dock → CSS, as a pure function the page emitter only consumes. The
/// WinForms window is the resize reference (Task 12/13); this pins our reading of it, table by table.
/// </summary>
[TestFixture]
public class FormAnchorCssTests
{
    private static PixelGeometry At(string? anchor, int x = 10, int y = 20, int width = 75, int height = 23) =>
        new() { X = x, Y = y, Width = width, Height = height, Anchor = anchor };

    private static List<(string Property, string Value)> Css(PixelGeometry geometry, int w = 400, int h = 300) =>
        FormAnchorCss.Positioned(geometry, w, h).ToList();

    // ==================================================================
    // The parser (shared with RegionWriter)
    // ==================================================================

    [TestCase(null, FormAnchorEdges.Top | FormAnchorEdges.Left)]
    [TestCase("", FormAnchorEdges.Top | FormAnchorEdges.Left)]
    [TestCase("None", FormAnchorEdges.None)]
    [TestCase("Top, Right", FormAnchorEdges.Top | FormAnchorEdges.Right)]
    [TestCase("left,LEFT", FormAnchorEdges.Left)]
    public void Parse_ReadsTheEdges_AbsentMeansTopLeft(string? anchor, FormAnchorEdges expected)
    {
        Assert.That(FormAnchor.Parse(anchor, out var unknown), Is.EqualTo(expected));
        Assert.That(unknown, Is.Empty);
    }

    [Test]
    public void Parse_NamesAnUnknownEdge_AndKeepsTheKnownOnes()
    {
        Assert.That(FormAnchor.Parse("Top,Middle", out var unknown), Is.EqualTo(FormAnchorEdges.Top));
        Assert.That(unknown, Is.EqualTo(new[] { "Middle" }));
    }

    [Test]
    public void TheEdgeValues_AreAnchorStyles()
    {
        // ⛔ RegionWriter emits CType(n, AnchorStyles) from these numbers. DockStyle numbers differently.
        Assert.That(((int)FormAnchorEdges.Top, (int)FormAnchorEdges.Bottom, (int)FormAnchorEdges.Left, (int)FormAnchorEdges.Right),
            Is.EqualTo((1, 2, 4, 8)));
    }

    // ==================================================================
    // Positioned controls — the table
    // ==================================================================

    [Test]
    public void TopLeft_TheDefault_IsItsDesignedBox()
    {
        Assert.That(Css(At(null)), Is.EqualTo(new List<(string, string)>
        {
            ("left", "10px"), ("width", "75px"), ("top", "20px"), ("height", "23px")
        }));
    }

    [Test]
    public void Right_NotLeft_KeepsItsDistanceFromTheRightEdge()
    {
        Assert.That(Css(At("Top,Right")).Take(2), Is.EqualTo(new List<(string, string)> { ("right", "315px"), ("width", "75px") }));
    }

    [Test]
    public void LeftAndRight_StretchesTheWidth()
    {
        Assert.That(Css(At("Top,Left,Right")).Take(2), Is.EqualTo(new List<(string, string)> { ("left", "10px"), ("right", "315px") }));
    }

    [Test]
    public void Bottom_NotTop_KeepsItsDistanceFromTheBottomEdge()
    {
        Assert.That(Css(At("Bottom,Left")).Skip(2), Is.EqualTo(new List<(string, string)> { ("bottom", "257px"), ("height", "23px") }));
    }

    [Test]
    public void TopAndBottom_StretchesTheHeight()
    {
        Assert.That(Css(At("Top,Bottom,Left")).Skip(2), Is.EqualTo(new List<(string, string)> { ("top", "20px"), ("bottom", "257px") }));
    }

    [Test]
    public void NoneOnEitherAxis_IsCentredRelativeToItsOriginalOffset()
    {
        Assert.That(Css(At("None")), Is.EqualTo(new List<(string, string)>
        {
            ("left", "calc(50% - 190px)"), ("width", "75px"), ("top", "calc(50% - 130px)"), ("height", "23px")
        }));
    }

    private static IEnumerable<TestCaseData> EveryAnchorCombination()
    {
        for (var bits = 0; bits < 16; bits++)
        {
            var edges = (FormAnchorEdges)bits;
            var anchor = edges == FormAnchorEdges.None ? "None" : edges.ToString();
            yield return new TestCaseData(anchor).SetName($"{{m}}({anchor})");
        }
    }

    private static string[] Axis(bool near, bool far, string nearName, string farName, string size) => (near, far) switch
    {
        (true, true) => new[] { nearName, farName },
        (false, true) => new[] { farName, size },
        _ => new[] { nearName, size }
    };

    [TestCaseSource(nameof(EveryAnchorCombination))]
    public void EveryAnchorCombination_MapsEachAxisByItsTwoEdges(string anchor)
    {
        var edges = FormAnchor.Parse(anchor, out _);
        var css = Css(At(anchor));
        var left = edges.HasFlag(FormAnchorEdges.Left);
        var right = edges.HasFlag(FormAnchorEdges.Right);
        var top = edges.HasFlag(FormAnchorEdges.Top);
        var bottom = edges.HasFlag(FormAnchorEdges.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(css.Select(d => d.Property),
                Is.EqualTo(Axis(left, right, "left", "right", "width").Concat(Axis(top, bottom, "top", "bottom", "height"))));

            if (!left && !right)
            {
                Assert.That(css.First(d => d.Property == "left").Value, Does.StartWith("calc(50%"));
            }

            if (!top && !bottom)
            {
                Assert.That(css.First(d => d.Property == "top").Value, Does.StartWith("calc(50%"));
            }
        });
    }

    [TestCase(10, 400, 600)]
    [TestCase(10, 401, 900)]
    [TestCase(300, 400, 1000)]
    [TestCase(0, 400, 400)]
    public void TheCentringFormula_MovesByHalfTheGrowth_AsWinFormsDoes(int x, int designWidth, int newWidth)
    {
        var value = FormAnchorCss.Centred(x, designWidth);
        var match = Regex.Match(value, @"^calc\(50% ([+-]) ([0-9.]+)px\)$");

        Assert.That(match.Success, Is.True, value);

        var shift = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * (match.Groups[1].Value == "-" ? -1 : 1);

        Assert.That(0.5 * newWidth + shift, Is.EqualTo(x + (newWidth - designWidth) / 2.0).Within(0.001),
            "new left = X + (W′−W)/2 (spec §4)");
    }

    [Test]
    public void ANonPositiveSize_WritesNoSize_AndCountsAsZero()
    {
        var css = Css(At("Top,Right", width: 0, height: -4));

        Assert.Multiple(() =>
        {
            Assert.That(css.Select(d => d.Property), Is.EqualTo(new[] { "right", "top" }),
                "spec §3: the emitter writes no size for a non-positive one");
            Assert.That(css[0].Value, Is.EqualTo("390px"), "400 − 10 − 0");
        });
    }

    [Test]
    [SetCulture("sv-SE")]
    public void Numbers_AreInvariant_UnderAUnicodeMinusCulture()
    {
        UnicodeMinusCulture.Require();
        var css = Css(At(null, x: -5), w: 400, h: 300);

        Assert.That(css[0].Value, Is.EqualTo("-5px"));
        Assert.That(string.Concat(css.Select(d => d.Value)), Does.Not.Contain(((char)0x2212).ToString()));
    }

    // ==================================================================
    // Docked — resolved once at the design size into fixed insets
    // ==================================================================

    private static FormDockedBounds Docked(FormDockEdge edge, FormRect bounds) =>
        new(new FormControl { Kind = "Panel", Id = "p" }, edge, bounds, 400, 300);

    [TestCase(FormDockEdge.Top, "left:0px right:0px top:24px height:50px")]
    [TestCase(FormDockEdge.Bottom, "left:0px right:0px bottom:22px height:50px")]
    [TestCase(FormDockEdge.Left, "left:0px width:50px top:24px bottom:22px")]
    [TestCase(FormDockEdge.Right, "right:0px width:50px top:24px bottom:22px")]
    [TestCase(FormDockEdge.Fill, "left:0px right:0px top:24px bottom:22px")]
    public void EachDockEdge_BecomesFixedInsets(FormDockEdge edge, string expected)
    {
        var bounds = edge switch
        {
            FormDockEdge.Top => new FormRect(0, 24, 400, 50),
            FormDockEdge.Bottom => new FormRect(0, 228, 400, 50),
            FormDockEdge.Left => new FormRect(0, 24, 50, 254),
            FormDockEdge.Right => new FormRect(350, 24, 50, 254),
            _ => new FormRect(0, 24, 400, 254)
        };

        var css = FormAnchorCss.Docked(Docked(edge, bounds));

        Assert.That(string.Join(" ", css.Select(d => $"{d.Property}:{d.Value}")), Is.EqualTo(expected));
    }
}
```

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS0103: The name 'FormAnchor' does not exist`, plus errors for `FormAnchorCss` and `FormAnchorEdges`. Right reason.

- [ ] **Step 3: Implement.** `BasicLang/Forms/FormAnchorCss.cs`:

```csharp
using System.Globalization;

namespace BasicLang.Forms;

/// <summary>
/// WinForms' <c>AnchorStyles</c> edges, with its numbers — verified against the official enum documentation.
/// ⛔ <c>DockStyle</c> numbers DIFFERENTLY (its Left is 3) and is not a flags enum; the two never share a
/// conversion.
/// </summary>
[Flags]
public enum FormAnchorEdges
{
    None = 0,
    Top = 1,
    Bottom = 2,
    Left = 4,
    Right = 8
}

/// <summary>
/// ⛔ THE one reader of an <c>Anchor</c> attribute (plan scope call S10) — the region writer's refusal and
/// emission and the page's CSS all parse it here. Two parsers of one attribute is a mirrored pair.
/// </summary>
public static class FormAnchor
{
    /// <summary>What an absent Anchor means — WinForms' default.</summary>
    public const FormAnchorEdges Default = FormAnchorEdges.Top | FormAnchorEdges.Left;

    /// <summary>The edge names as written, trimmed.</summary>
    public static string[] Split(string anchor) =>
        anchor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The edges <paramref name="anchor"/> names, case-insensitively; null/blank is <see cref="Default"/>.
    /// A name that is no edge is returned in <paramref name="unknown"/> and contributes nothing — the region
    /// writer refuses it (BL8006-family <c>AnchorNotExpressible</c>) rather than emit a control anchored to less
    /// than the user wrote.
    /// </summary>
    public static FormAnchorEdges Parse(string? anchor, out IReadOnlyList<string> unknown)
    {
        if (string.IsNullOrWhiteSpace(anchor))
        {
            unknown = Array.Empty<string>();
            return Default;
        }

        var edges = FormAnchorEdges.None;
        var bad = new List<string>();

        foreach (var name in Split(anchor))
        {
            var match = Enum.GetValues<FormAnchorEdges>()
                .Where(e => string.Equals(e.ToString(), name, StringComparison.OrdinalIgnoreCase))
                .Select(e => (FormAnchorEdges?)e)
                .FirstOrDefault();

            if (match is { } edge)
            {
                edges |= edge;
            }
            else
            {
                bad.Add(name);
            }
        }

        unknown = bad;
        return edges;
    }
}

/// <summary>
/// Anchor and Dock → CSS for a Canvas page (spec 2026-09-27 §4) — a PURE function; the emitter only consumes it.
///
/// <para>Per axis, with W the container's design size: near edge only → <c>near:offset; size</c>; far edge only
/// → <c>far:(W−offset−size); size</c>; both → <c>near:offset; far:(W−offset−size)</c> (the size follows);
/// neither → centred relative to its original offset, as WinForms does:
/// new offset = offset + (W′−W)/2 → <c>calc(50% ± (offset − W/2)px)</c>.</para>
///
/// <para>A docked control's rectangle was resolved ONCE at the design size by <see cref="FormDockLayout"/>;
/// here it becomes fixed insets on the edges it follows.</para>
/// </summary>
public static class FormAnchorCss
{
    /// <summary>
    /// The declarations for an undocked positioned control, horizontal axis first. ⚠ A non-positive size writes
    /// no size declaration (spec §3) and counts as 0 in the far-edge inset.
    /// </summary>
    public static IReadOnlyList<(string Property, string Value)> Positioned(
        PixelGeometry geometry, int containerWidth, int containerHeight)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        var edges = FormAnchor.Parse(geometry.Anchor, out _);
        var declarations = new List<(string Property, string Value)>();

        Axis(declarations, "left", "right", "width", geometry.X, geometry.Width, containerWidth,
            edges.HasFlag(FormAnchorEdges.Left), edges.HasFlag(FormAnchorEdges.Right));
        Axis(declarations, "top", "bottom", "height", geometry.Y, geometry.Height, containerHeight,
            edges.HasFlag(FormAnchorEdges.Top), edges.HasFlag(FormAnchorEdges.Bottom));

        return declarations;
    }

    /// <summary>The fixed insets for a docked thing, from its resolved rectangle and container.</summary>
    public static IReadOnlyList<(string Property, string Value)> Docked(FormDockedBounds docked)
    {
        var b = docked.Bounds;
        var right = Px(docked.ContainerWidth - b.Right);
        var bottom = Px(docked.ContainerHeight - b.Bottom);

        return docked.Edge switch
        {
            FormDockEdge.Top => new[] { ("left", Px(b.X)), ("right", right), ("top", Px(b.Y)), ("height", Px(b.Height)) },
            FormDockEdge.Bottom => new[] { ("left", Px(b.X)), ("right", right), ("bottom", bottom), ("height", Px(b.Height)) },
            FormDockEdge.Left => new[] { ("left", Px(b.X)), ("width", Px(b.Width)), ("top", Px(b.Y)), ("bottom", bottom) },
            FormDockEdge.Right => new[] { ("right", right), ("width", Px(b.Width)), ("top", Px(b.Y)), ("bottom", bottom) },
            _ => new[] { ("left", Px(b.X)), ("right", right), ("top", Px(b.Y)), ("bottom", bottom) }
        };
    }

    /// <summary>
    /// The centred offset: <c>offset + (W′−W)/2</c> is <c>50%·W′ + (offset − W/2)</c>. Written with the sign
    /// outside the length (<c>calc(50% - 190px)</c>) because CSS requires spaces around a binary +/−.
    /// </summary>
    public static string Centred(int offset, int container)
    {
        var shift = offset - (container / 2.0);
        return shift < 0
            ? $"calc(50% - {Number(-shift)}px)"
            : $"calc(50% + {Number(shift)}px)";
    }

    private static void Axis(
        List<(string Property, string Value)> declarations, string near, string far, string size,
        int offset, int extent, int container, bool toNear, bool toFar)
    {
        var used = Math.Max(0, extent);
        var farInset = container - offset - used;

        if (toNear && toFar)
        {
            declarations.Add((near, Px(offset)));
            declarations.Add((far, Px(farInset)));
            return;
        }

        if (toFar)
        {
            declarations.Add((far, Px(farInset)));
        }
        else if (toNear)
        {
            declarations.Add((near, Px(offset)));
        }
        else
        {
            declarations.Add((near, Centred(offset, container)));
        }

        if (extent > 0)
        {
            declarations.Add((size, Px(extent)));
        }
    }

    /// <summary>⛔ Invariant: sv-SE spells a negative with U+2212, which CSS does not read as a minus.</summary>
    private static string Px(int value) => value.ToString(CultureInfo.InvariantCulture) + "px";

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 4: `RegionWriter` onto the one parser.**
  - Delete `AnchorFlags` (`:154-164`). Move its `DockStyle` warning paragraph into `FormAnchorEdges`' doc; it is already there above.
  - `CheckAnchors` `:202`: `var unknown = SplitAnchor(anchor).Where(e => !AnchorFlags.ContainsKey(e)).ToList();` becomes `FormAnchor.Parse(anchor, out var unknown);`. `unknown.Count` and `string.Join(", ", unknown)` keep working.
  - Delete `SplitAnchor` (`:215-216`).
  - `AnchorExpression` (`:1063-1075`) body becomes:

```csharp
        var edges = FormAnchor.Split(anchor);
        if (edges.Length == 1)
        {
            return $"AnchorStyles.{edges[0]}";
        }

        // Unknown names are refused by CheckAnchors before this runs. ⚠ Flags are OR-ed, not summed: the old
        // sum turned "Left,Left" into 8 — AnchorStyles.Right (plan scope call S10).
        var value = (int)FormAnchor.Parse(anchor, out _);
        return $"CType({value}, AnchorStyles)   ' {string.Join(", ", edges)}";
```

- [ ] **Step 5: Run.** Build, then filter `FullyQualifiedName~FormAnchorCssTests|FullyQualifiedName~FormAnchorEmissionTests|FullyQualifiedName~FormAnchorDockPickerTests|FullyQualifiedName~FormRegionWriterTests|FullyQualifiedName~FormDesignDiagnosticTests`. `FormAnchorEmissionTests` has `[Category("Integration")]` rows that compile with csc. Run them by name here, because RegionWriter's emission changed shape internally. All green. Compare names with `baseline-int.txt`.

- [ ] **Step 6: Commit.** Message `feat(forms): FormAnchor is the one Anchor parser; FormAnchorCss maps Anchor/Dock to CSS incl. the WinForms centring formula (spec 2026-09-27 §4)`. Stage `FormAnchorCss.cs`, `RegionWriter.cs` and the new test file.

---

## Task 8: The pure reading-order grouping for phone stacking

Spec §5 (rows by vertical overlap; rows top to bottom; left to right; a container stacks as one block with its children ordered the same way), §7 (table tests: overlapping rows, a label slightly higher than its box, nested containers, ties). Scope call S8.

**Files:**
- Create: `BasicLang/Forms/FormReadingOrder.cs`
- Create: `VisualGameStudio.Tests/Compiler/FormReadingOrderTests.cs`

- [ ] **Step 1: Write the failing test file.** `VisualGameStudio.Tests/Compiler/FormReadingOrderTests.cs`:

```csharp
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §5 — the order controls stack in on a phone: ROWS by vertical overlap, top to bottom, left to
/// right within a row. Pure and per sibling list; the emitter recurses into containers (Task 10).
/// </summary>
[TestFixture]
public class FormReadingOrderTests
{
    private sealed record Box(string Id, int X, int Y, int W, int H);

    private static IReadOnlyList<string> Order(params Box[] boxes) =>
        FormReadingOrder.Order(boxes, b => new FormRect(b.X, b.Y, b.W, b.H)).Select(b => b.Id).ToList();

    [Test]
    public void Nothing_OrdersToNothing() => Assert.That(Order(), Is.Empty);

    [Test]
    public void TwoRows_TopToBottom_LeftToRight()
    {
        Assert.That(Order(
                new Box("button", 10, 50, 75, 23),
                new Box("text", 70, 10, 100, 20),
                new Box("label", 10, 10, 50, 20)),
            Is.EqualTo(new[] { "label", "text", "button" }));
    }

    [Test]
    public void ALabelSlightlyHigherThanItsBox_ShareTheBoxsRow_AndComeFirst()
    {
        Assert.That(Order(
                new Box("text", 70, 10, 100, 23),
                new Box("label", 10, 13, 50, 15)),
            Is.EqualTo(new[] { "label", "text" }));
    }

    [Test]
    public void ControlsThatOnlyTouch_AreDifferentRows()
    {
        Assert.That(Order(
                new Box("second", 5, 30, 50, 20),
                new Box("first", 100, 10, 50, 20)),
            Is.EqualTo(new[] { "first", "second" }), "second's top (30) is first's bottom: no overlap");
    }

    [Test]
    public void ATallListBesideAColumnOfFields_IsOneRow_ListThenFieldsTopToBottom()
    {
        Assert.That(Order(
                new Box("f3", 150, 70, 100, 20),
                new Box("list", 10, 10, 100, 200),
                new Box("f1", 150, 10, 100, 20),
                new Box("f2", 150, 40, 100, 20)),
            Is.EqualTo(new[] { "list", "f1", "f2", "f3" }));
    }

    [Test]
    public void IdenticalRects_KeepDocumentOrder()
    {
        Assert.That(Order(
                new Box("a", 10, 10, 50, 20),
                new Box("b", 10, 10, 50, 20),
                new Box("c", 10, 10, 50, 20)),
            Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void AZeroHeightControl_StillJoinsARow()
    {
        Assert.That(Order(
                new Box("right", 100, 10, 50, 0),
                new Box("left", 10, 10, 50, 0)),
            Is.EqualTo(new[] { "left", "right" }));
    }

    [Test]
    public void AContainerStacksAsOneBlock_ItsChildrenOrderedTheSameWay()
    {
        var top = Order(
            new Box("panel", 10, 60, 300, 200),
            new Box("title", 10, 10, 200, 30));
        var inside = Order(
            new Box("ok", 200, 150, 75, 23),
            new Box("name", 10, 10, 150, 23),
            new Box("nameLabel", 170, 12, 60, 18));

        Assert.Multiple(() =>
        {
            Assert.That(top, Is.EqualTo(new[] { "title", "panel" }), "the panel is ordered by its own rectangle");
            Assert.That(inside, Is.EqualTo(new[] { "name", "nameLabel", "ok" }),
                "its children by theirs, relative to it — the same rule, one level down");
        });
    }
}
```

- [ ] **Step 2: Build. Expect a BUILD failure.** `CS0103: The name 'FormReadingOrder' does not exist`. Right reason.

- [ ] **Step 3: Implement.** `BasicLang/Forms/FormReadingOrder.cs`:

```csharp
namespace BasicLang.Forms;

/// <summary>
/// The order a Canvas page's controls stack in below its phone breakpoint (spec 2026-09-27 §5) — PURE, computed
/// at build time and applied with CSS <c>order</c> inside the media query (the HTML order is unchanged).
///
/// <para>The rule (plan scope call S8), stated exactly because the table tests pin it: sort by top, then left,
/// then document order; a control joins the current ROW when its top is above the row's running bottom (the
/// union of the row so far), otherwise it starts a new row; within a row, left to right, then top, then document
/// order. A zero-height control counts as 1px tall, so it can still share a row.</para>
///
/// <para>⚠ Per sibling list. A container is ordered among its siblings by its own rectangle and stacks as one
/// block; the caller orders its children with this same function, in the container's coordinates.</para>
/// </summary>
public static class FormReadingOrder
{
    public static IReadOnlyList<T> Order<T>(IReadOnlyList<T> items, Func<T, FormRect> boundsOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(boundsOf);

        var entries = items
            .Select((item, index) => (Item: item, Bounds: boundsOf(item), Index: index))
            .OrderBy(e => e.Bounds.Y)
            .ThenBy(e => e.Bounds.X)
            .ThenBy(e => e.Index)
            .ToList();

        var ordered = new List<T>(entries.Count);
        var row = new List<(T Item, FormRect Bounds, int Index)>();
        var rowBottom = 0;

        foreach (var entry in entries)
        {
            if (row.Count > 0 && entry.Bounds.Y >= rowBottom)
            {
                Flush(row, ordered);
            }

            rowBottom = row.Count == 0 ? Extent(entry.Bounds) : Math.Max(rowBottom, Extent(entry.Bounds));
            row.Add(entry);
        }

        Flush(row, ordered);
        return ordered;
    }

    private static int Extent(FormRect bounds) => Math.Max(bounds.Bottom, bounds.Y + 1);

    private static void Flush<T>(List<(T Item, FormRect Bounds, int Index)> row, List<T> ordered)
    {
        ordered.AddRange(row
            .OrderBy(e => e.Bounds.X)
            .ThenBy(e => e.Bounds.Y)
            .ThenBy(e => e.Index)
            .Select(e => e.Item));
        row.Clear();
    }
}
```

- [ ] **Step 4: Run.** Build, then filter `FullyQualifiedName~FormReadingOrderTests`. 8 passed.

- [ ] **Step 5: Foundation checkpoint.** Run the fast subset into `$sp\foundation-fast.txt` (both streams). Compare the sorted failure NAMES with `baseline-fast.txt`. **Zero new names.** Record the count **with its base** (`feat/web-pixel-layout` @ this commit's parent) in the commit message.

- [ ] **Step 6: Commit.** Message `feat(forms): FormReadingOrder — the phone stacking order by overlapping rows (spec 2026-09-27 §5)`. Include the checkpoint line. Stage `FormReadingOrder.cs` and the new test file.

---

# Integration tasks (TASK granularity)

> ⛔ **Each task below is expanded into full steps just before it starts, after a pre-flight against the tree as Tasks 1–8 leave it.** The pre-flight re-reads every file it names, re-verifies every anchor by quoted text, and checks every "replace X with Y" against the rules earlier tasks put in place (the slice-2 blocker class). It writes its corrections as a sibling document (`docs/superpowers/plans/2026-09-27-web-pixel-layout-<task>-preflight.md`) that wins over this plan. Anchors below are at `47797002`.

## Task 9: The canvas and placement become layout-aware, and a docked control is drawn where it runs

Spec §2.4, §7a (BoundsOf through `FormDockLayout`; a docked control cannot be dragged or resized), §7 "Canvas (headless, real IDE view)".

> ⚠ Plan-review note: a negative `MobileBreakpoint` typed in the grid is Degraded (Task 4) and snaps back via the two-step echo — check in a real-view test whether a refusal REASON is shown. If not, either surface it (the carried-refusal path from property-grid slice 2) or record it as an accepted gap in the task's report; do not leave it silent by accident.

**Files and responsibilities:**
- `VisualGameStudio.Shell/Controls/FormCanvasTransform.cs`:
  - `Layout` (`:371-380`) routes on `FormVocabulary.IsPixel(document)`, not `Target == Web`. A Canvas page uses the pixel path.
  - `WebLayout` (`:832-863`) stays Grid/Flow only; fix its summary's "Canvas is the unimplemented pixel escape hatch".
  - `Bands` (`:412-514`) takes each strip's band rectangle from `FormDockLayout.Resolve`: one call per layout pass, shared with `BoundsOf`. It never re-derives the stacking. It still yields cells and Type Here slots from `band.Y/Height` (its `⛔⛔` rule stands).
  - `BoundsOf` (`:344-358`) gains the resolved layout (`FormDockLayoutResult`) and the container's client size. A docked control's rect comes from it, not its stale X/Y.
  - Its callers change with it: the private `Layout(controls, origin)` (`:865-893`), `ContainerAt` (`:297-335`), and `FormCanvasControl.FormBoundsOf`.
  - Update the summaries that say `BoundsOf` "ignores Dock" or that `Bands` "uniquely owns where each band SITS". The resolver now owns that, and `Bands` consumes it.
- `VisualGameStudio.Shell/ViewModels/Designer/FormPlacement.cs`:
  - `Place` (`:99-104`) calls `PlaceOnWeb` only when `!FormVocabulary.IsPixel(document)`. A Canvas page takes the pixel path (containment, clamping to `SurfaceOf`, TabIndex, Text).
  - `PlaceOnWeb`'s refusal text (`:167-173`, "A Canvas layout positions controls by document order") becomes Flow-only wording.
- `VisualGameStudio.Shell/Controls/FormCanvasControl.cs`:
  - Form grips: press `:883` and drawing `:1566` gate on `IsPixel`.
  - `DrawSurface` `:1779-1811`: `isWindow` (title bar, bevel, caption, window buttons) stays **WinForms only**. The alignment grid is drawn for `IsPixel`. A Canvas page gets grid + grips, no window frame (spec §2.4).
  - Cell guides `:1816` stay Grid only (already).
  - **Docked controls** (`FormDockLayout.EdgeOf(control) != null` on a Positioned control): `HandleUnder` yields no handles; `OnPointerPressed`'s pixel branch (`:983-1008`) arms no drag, so a press selects only; group drags skip them (`_dragStarts`); keyboard nudge (check `FormCanvasKeyboardTests`' route) does nothing for them.
  - Draw them at their resolved rect.
- `FormGeometryEdit` / the VM's move/resize commit: confirm nothing writes X/Y for a docked control (pre-flight greps `MoveToForm`/`MoveTo`).

**Tests each must add:**
- `FormCanvasTransformTests` (Edit):
  - `Layout` of a Canvas page yields pixel rects.
  - For a document mixing strips, a `Dock=Top` Panel before the MenuStrip, and a nested docked Button: every `Band` entry's rect equals `FormDockLayout.Resolve(...)`'s, and every docked control's `Control` entry equals its resolved rect offset by its container. This is the "one answer, two consumers" test: `Bands`/`BoundsOf` agree with the resolver.
  - `HitTest` finds the docked control where it is drawn; `ContainerAt` on a Canvas page.
- `FormPlacementTests` (Edit):
  - a drop on a Canvas page yields `PixelGeometry` at the point, clamped;
  - a drop into a Panel is container-relative;
  - Flow still refuses with the Flow wording.
- `FormCanvasDropTests` (Edit): a VM `PlaceControl` on a new (Canvas) scaffold writes `X=`/`Y=` into the text.
- A new real-view file `VisualGameStudio.Tests/Shell/FormPixelPageRealViewTests.cs`:
  - It follows the rig in `FormPropertyGridRealViewTests.cs`: the real `CodeEditorDocumentView`, `AppStyles.axaml`, `[AvaloniaTest]` + Skia via `DesignerHeadlessApp`, every `Window` closed, repeat clicks offset to dodge the double-click trap, and every test at two window sizes / zoom ≠ 1.
  - On a Canvas page it checks: controls drawn at their pixels; a toolbox drop lands at the pointer; drag moves; a resize handle resizes; grid edits of X/Y/Width/Height/Anchor/Dock redraw; the alignment grid and form grips are present; no title bar (pixel sample above the surface equals the background); a docked Panel shows no handles and a drag starting on it moves nothing and selects it.
  - The grid shows ClientSize and MobileBreakpoint on a Canvas page, and Cols/Rows/Gap only on a Grid page.
- `FormCanvasRenderTests`: re-run. Spec-claims #8: no positioned-Dock fixture exists at `47797002`. If the pre-flight finds one, record each changed expectation as INTENDED in the commit, one line per test.

**Risks:**
- ⛔ `Bands` computing its own stacking again (the mirrored pair). The agreement test is the guard, and mutation M5 targets it.
- ⚠ `Bands` must not assume strips exist only at the ROOT. `FormDockLayout.ResolveSiblings` resolves a strip in any sibling list (inside a Panel too), so the canvas reads bands from the result at every depth, relative to their container's client origin.
- ⚠ The canvas asks for `FormDockMode.Designer` (the default: hidden controls are shown and docked). The page (Task 10) asks for `FormDockMode.Runtime`. A container's client size for anchoring is `FormDockLayoutResult.ClientSizeOf`, never a re-derived "resolved bounds or stored size".
- The docked control's stored X/Y is stale by design. Anything that reads `PixelGeometry` directly for drawing (selection outline, the Type Here overlay, marquee `ControlsIn` at `:168-193`) must go through the resolved bounds.
- Test at more than zoom 1.0 (`ToForm` is the identity at 1.0).
- Never change a bound property inside `Render`.
- `dotnet clean` if any AXAML changes.
- The ONE selection store: never set `PropertyGrid.SelectedControl` from the VM.

**Gate:** fast subset (names vs the Task 8 checkpoint); named fixtures `FormCanvas*`, `FormPlacementTests`, `FormDesigner*RealViewTests`, `FormPropertyGridRealViewTests`, `FormStrip*`, `FormTrayViewTests`, `FormDesignModeTests`, `FormAnchorDockPickerTests`, `FormPixelPageRealViewTests`.

## Task 10: The emitter's Canvas page, with strips inside, band heights, positions, anchors and the phone query

Spec §3, §4, §5. Scope call S12.

**Files and responsibilities:**
- `BasicLang/Forms/FormAssetEmitter.cs`:
  - `Html` (`:101-181`). For a Canvas page, strips are emitted INSIDE `<div class="vgs-form">`, in document order (S12). Everything else is emitted in document order. Top/bottom chrome outside the div stays for Grid/Flow only; update the `⛔` comment at `:116-119` to say Grid/Flow.
  - `<Literal>` flows at the form area's top-left.
  - `Css` (`:391-453`), Canvas arm (`:422-424`):
    - `.vgs-form { position: relative; width: 100%; min-width: Wpx; height: 100vh; min-height: Hpx; box-sizing: border-box; }` with W×H = `DesignSize`, plus `body { margin: 0; }` on Canvas pages only.
    - Per control (recursing with each container's design size): `position: absolute; box-sizing: border-box` and either `FormAnchorCss.Docked(resolved)` (from ONE `FormDockLayout.Resolve(form)`) or `FormAnchorCss.Positioned(geometry, W, H)`.
    - Strips: `FormAnchorCss.Docked` with the band height. The id rule outranks the row's `WebCss` class rules (`FormControlCatalog.cs:1813/1838/1859`), so the height holds; `box-sizing: border-box` absorbs the ToolStrip's padding and the StatusStrip's border.
    - A non-positive size writes none.
    - Catalog CSS (`FormCss`) unchanged; tray components emit nothing.
  - The phone query: `@media (width < {EffectiveMobileBreakpoint}px)`. It is absent when the breakpoint is 0, and the default applies when the value is Degraded. Inside it:
    - the form area becomes `display:flex; flex-direction:column; gap:8px; height:auto; min-width:0; min-height:0`;
    - every control gets `position: static; order: N`, with N from `FormReadingOrder` per sibling list, recursing;
    - top strips come first, bottom strips last, and docked controls join the grouping at their resolved rect (§7a);
    - containers get `height:auto` and flex column;
    - stretch comes from the new catalog facet, else `align-self:flex-start`.
    - ⛔ Never write `display` for a control whose own catalog CSS hides it (Visible=false → `display:none`). A later `display:flex` would un-hide it on phones.
  - `AppendControlCss` (`:455-502`): its grid-only rule stays; the Canvas rules come from the functions above, never re-derived.
- `BasicLang/Forms/FormControlCatalog.cs`:
  - `FormControlDef` gains `bool StretchesWhenStacked = false` (a new positional parameter at the end, with a `<param>` doc).
  - It is set on TextBox, ComboBox, ListBox and PictureBox (spec §5 "multi-line text" is the TextBox row).
  - ⛔ Never a `control.Kind` switch in the emitter.
- `BasicLang/Forms/RegionWriter.cs`: `CheckAnchors` (`:186-213`) gates on `FormVocabulary.IsPixel(form)`, not `Target == WinForms`. A Canvas page's unknown anchor edge is refused as a `.blform`'s is, because the page would otherwise anchor it to less than written.

**Tests each must add:**
- `FormAssetEmitterTests` (Edit), Canvas cases:
  - strips inside the form div, in document order;
  - each control's rule equals `FormAnchorCss`'s output for it (compare to the function, never a re-derivation);
  - band heights 24/25/22 via the catalog;
  - minimum size = `DesignSize`;
  - nested children use the container's size;
  - media query present with the breakpoint, absent for 0, default for Degraded;
  - `order` values equal `FormReadingOrder`'s;
  - stretch only on flagged rows (catalog-driven `TestCaseSource` over `FormControlCatalog.All`);
  - a hidden container keeps `display:none` on phones.
- Grid/Flow outputs: the existing pinned strings stay byte-identical (a named re-run, no edits).
- A catalog gate: `StretchesWhenStacked` only on Positioned rows that support the web.
- `FormBuildEmissionTests` (Edit): a Canvas page built through the real CLI and RUN under node, with no `LOAD ERROR` and the handler firing. ⛔ A green build is not a running page.
- `FormRegionWriterTests`/`FormDesignDiagnosticTests`: a Canvas page with `Anchor="Middle"` is refused.

**Risks:**
- The DOM's containing block for absolute children is the container's padding box. A catalog CSS border (BorderStyle) shifts children by the border width, and WinForms insets `DisplayRectangle` too. The Task 12/13 harness measures it; fixtures avoid borders.
- The strip's `WebCss` `li:hover>ul` dropdown needs `overflow: visible` on the band.
- `100vh` inside the Edge iframe is the iframe's height (intended).
- S12: overlap drawn differently on the canvas and the page (recorded).
- Every number goes through invariant formatting.
- ⛔ **Toggling `Visible` at run time (decision DEFERRED to this task's pre-flight, to be put to the owner).** WinForms re-lays out docked controls whenever user code toggles `Visible`: the next docked control closes the gap, and reopens it when the control is shown again. The static page's fixed insets cannot do that. `FormDockMode.Runtime` gives a hidden docked control, and every child of a hidden container, NO bounds. So this task must STATE the rule for their CSS, knowing that one state is wrong either way:
  - **Designer-mode insets** for everything: correct once shown, but a gap while hidden;
  - **Runtime-mode insets**: correct while hidden, but overlap and stale stored X/Y once shown.
  Choose the rule, write it in the emitter's comment and the commit, and test the chosen state.

**Gate:** fast subset; `FormAssetEmitterTests`, `FormCssTests`, `FormBuildEmissionTests` (Integration), `FormDesignerAcceptanceTests`/`FormMenuAcceptanceTests`/`FormComponentAcceptanceTests` (Integration, Grid-pinned; must be unchanged by name).

## Task 11: Canvas web → WinForms retarget copies geometry and the design size exactly

Spec §6 (lossless; no layout warning; BL8024 for `MobileBreakpoint`; the retarget and root sweeps cover the new row). Spec-claims #1.

> ⚠ Plan-review note: `FormRootRetargetTests.Sample` (`:16-20`) has no Int arm, so the root sweep cannot produce a value for `MobileBreakpoint` — add one (e.g. `"480"`) before relying on the sweep to cover the new row.

**Files and responsibilities:**
- `BasicLang/Forms/FormRetarget.cs`:
  - `ToPixels` (`:624-649`). When `FormVocabulary.LayoutOf(_source) == Canvas`:
    - every control's `_sourceGeometry` (already a `PixelGeometry` clone, `:321`) becomes its geometry at every depth, as is (`Place` is skipped);
    - `Document.Width/Height = _source.Width/Height` exactly (no 800×450 minimum; a null stays null);
    - `Text ??= Name` as today;
    - **no** `RetargetLayoutCrossed` finding;
    - `MobileBreakpoint` present → `RetargetPropertyLost` naming `'form.MobileBreakpoint'` and its value (dropped-and-named);
    - a `<Literal>` is still named (a loss, not layout);
    - Anchor/Dock cross in the geometry.
  - `ConvertRoot`'s source/destination rows use `_toLayout`/`LayoutOf(_source)` from Task 2. Confirm a Canvas source's Width/Height are modelled, not unknown, so nothing is double-named.
- WinForms → web keeps producing Grid (unchanged).

**Tests each must add:**
- `FormRootRetargetTests` (Edit): add `(Web Canvas)` to `Sources()`. ClientSize crosses exactly; MobileBreakpoint is named with `RetargetPropertyLost 'form.MobileBreakpoint'`. The sweep's RetargetPropertyLost arm becomes non-vacuous; delete its "⚠ RE-CHECK" line.
- `FormRetargetTests` (Edit):
  - the catalog sweep (`EveryCatalogKind_Retargets_…`) gets a Canvas-source variant;
  - `CanvasToWinForms_IsLossless`: every control's X/Y/Width/Height/Anchor/Dock at every depth, and the design size, are identical; zero `RetargetLayoutCrossed`.
- `FormRetargetPairTests` (Edit): `ConvertToPair` of a Canvas fixture produces C# that `WinFormsCompile.AssertCompiles` accepts (Integration, csc).

**Risks:**
- `Hoist` (`:347-368`) translates children of a removed kind by the container's X/Y. For a Canvas source with a docked container, it must translate by the RESOLVED rect, or a hoisted child lands offset (ask `FormDockLayout`).
- The destination's region writer must emit `Dock` before or after `Location/Size` consistently with today's order (`RegionWriter.cs:1024-1044`); verify by running.

**Gate:** fast subset; `FormRetarget*`, `FormRootRetargetTests`, `FormRetargetPairTests` (Integration).

## Task 12: The WinForms reference harness (spec §7 items 1–6)

**Files and responsibilities:**
- New `VisualGameStudio.Tests/Compiler/PixelLayout/WinFormsReferenceHarness.cs`. Given a Canvas `FormDocument` and a list of client sizes, it returns every control's rectangle in FORM-CLIENT coordinates per size. It:
  1. gets the WinForms program from `FormRetarget.ConvertToPair(document, WinForms)` (Task 11);
  2. compiles the `.bas` with the real CLI (`--target=csharp`), as `FormDesignerAcceptanceTests.cs:373-386` does;
  3. writes a driver `Driver.cs` + `app.csproj` (net8.0-windows, UseWindowsForms). Its `Main` calls `Application.SetHighDpiMode(HighDpiMode.DpiUnaware)` before any window (item 4). It shows the form, `Application.DoEvents()`, then:
     - pins every `ToolStrip`-derived control `AutoSize = false` with its catalog `DefaultHeight`, **before any resize** (item 5, §7a), and runs `PerformLayout`;
     - walks EVERY control at every depth through the form's private FIELDS by reflection, keyed by `Id` (spec-claims #9: `Control.Name` is never set);
     - prints `RECT <size> <id> x y w h` with `form.PointToClient(c.PointToScreen(Point.Empty))` + `Size` (item 2);
     - for each extra size: `form.ClientSize = new Size(w, h)`, `PerformLayout()`, `DoEvents()`, print again (item 3). The size is clamped inside `Screen.PrimaryScreen.WorkingArea`, and the harness asserts the clamp did not bite;
  4. builds with `dotnet build -c Release` and runs it through `CliTestHarness.RunProcess`, which already kills its own process tree on timeout (`CliTestHarness.cs:85`);
  5. parses the lines.
- Gates (item 6): `[Category("Integration")]` + `if (!OperatingSystem.IsWindows()) Assert.Ignore("a WinForms window can only be run on Windows")`. Inside `Assert.Multiple` use `TestSkip.IgnoreEvenInsideMultiple`.

**Tests each must add:**
- `WinFormsReferenceHarnessTests` (self-test, Integration): a two-control fixture (one Top|Left, one Right-anchored) at the design size reports the stored geometry within 0px, and at W+200 the Right-anchored control moved by 200. This proves the harness before anything is compared against it.
- A Panel `Dock=Fill` between a MenuStrip and a StatusStrip reports the resolver's rect.
- **Overflow at the far edges (S9):** an overflowing `Dock=Top` Panel, then a `Dock=Bottom` Panel; and an overflowing `Dock=Left`, then a `Dock=Right`. Both must report `FormDockLayout.Resolve`'s rects (the far-edge dock on the real edge, unclamped remainder).
- A `Visible=false` docked Panel before a second one reports `Resolve(…, FormDockMode.Runtime)`'s rects (the second closes the gap).

**Risks:**
- DPI virtualisation on the owner's scaled display.
- Windows clamps an oversized window.
- A `GroupBox` insets its children (spec-claims #11). Fixtures use Panel; a GroupBox fixture is added only to MEASURE and record the inset, never to pass.
- A `Panel` with `BorderStyle` `FixedSingle`/`Fixed3D` also loses 1–2px of client area, like a GroupBox. The resolver takes a container's client area as its bounds. Measure it, and record it; never make a test pass around it.
- Strip heights are the catalog's `DefaultHeight` at 96 DPI (24/25/22), but real strips `AutoSize`. The driver pins `AutoSize = false`; the harness should also MEASURE an auto-sized strip once and record the difference.
- A hand-edited strip `Dock="Left"`/`"Fill"`/`"None"` silently resolves to Top (`FormControl.IsDockedToBottom` is Bottom-or-else-Top). Flag it for a later diagnostic; it is not implemented.
- The generated form is in `namespace GeneratedCode`.
- `PerformClick` needs a shown form (not used here).
- **The harness is the arbiter of Task 6's table (S9).** If Fill-then-Top or overflow disagrees, fix `FormDockLayout` and its table, and say so in the commit.

**Gate:** the harness self-test on Windows; Linux skips with the reason.

## Task 13: The Edge headless harness, served from localhost, and the layout assertions

Spec §7 (Edge check), §7a (loopback server).

**Files and responsibilities:**
- New `VisualGameStudio.Tests/Compiler/PixelLayout/EdgeLayoutHarness.cs`:
  - Locates `msedge.exe` (`C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe`, then `Program Files`). If it is absent, **skip with a reason** (Linux/cloud), never pass by absence.
  - Serves the built site directory from an `HttpListener` on `http://localhost:{port}/`. The port comes from probing a free one (`TcpListener` on port 0 → read → stop → register); retry on `HttpListenerException`. ⛔ Not `127.0.0.1` (a non-admin prefix can be refused without a URL ACL) and not `file://` (opaque origins).
  - Writes a served COPY of the form page with a measuring script appended.
  - Writes `harness.html`, which iframes the copy at each width (design W, W+200, W−100 above the breakpoint, 400 below it) and each height. On load it reads every control's `getBoundingClientRect()` minus the form area's rect (same origin), and writes the results into a `<pre id="out">`.
  - Launches `msedge --headless=new --user-data-dir=<fresh temp dir> --hide-scrollbars --force-device-scale-factor=1 --virtual-time-budget=<ms> --dump-dom http://localhost:{port}/harness.html`. It captures stdout, parses `#out`, and on timeout `Process.Kill(entireProcessTree: true)` on **its own PID** (⛔ never by name: the owner's Edge is running). Afterwards it disposes the listener and deletes the temp profile.
- New `VisualGameStudio.Tests/Compiler/PixelLayout/PixelPageLayoutTests.cs` (Integration; Windows + Edge gates):
  - at the design size, every UNDOCKED control is within 1px of its stored X/Y/Width/Height, and every strip is at its `DefaultHeight`;
  - docked controls, and every control at the wider size, match the Task 12 WinForms reference within 1px;
  - narrower than design: the form keeps its size and the page scrolls (document scroll width ≥ W);
  - below the breakpoint: controls in `FormReadingOrder`'s order, stretch rows span the iframe width, top strips first and bottom strips last, hidden controls stay hidden.
- Fixtures cover:
  - every Anchor combination on one axis each;
  - the no-anchor centring;
  - a `Dock=Top` Panel before a MenuStrip;
  - a Fill between strips;
  - a nested Panel with a docked child;
  - an overflowing `Dock=Top` then a `Dock=Bottom`, and an overflowing `Dock=Left` then a `Dock=Right` (S9, far-edge docking; compared with the Task 12 reference);
  - a `Visible=false` docked control before another (the page is `FormDockMode.Runtime`).

**Risks:**
- The generated page's module script loads and runs BasicLang's JS. A script error must not abort measurement, so the harness measures on `load`, independent of the script.
- `--virtual-time-budget` too small gives empty rects: assert non-empty before comparing.
- Firewall prompts do not apply to localhost.
- `100vh` is the iframe height.

**Gate:** Windows with Edge; skips elsewhere with the reason.

## Task 14: The Canvas acceptance twin

Spec §7 "End to end".

**Files and responsibilities:**
- `VisualGameStudio.Tests/Compiler/FormDesignerAcceptanceTests.cs` (Edit): add `Web_Canvas_DesignedForm_BuildsRunsAndLaysOut`. It:
  - takes a NEW (Canvas, default) scaffold;
  - drops Label/TextBox/Button at pixel points through `vm.PlaceControl`;
  - sets properties through the real grid;
  - sets an Anchor (Right) and a Dock (a Panel `Fill` under a MenuStrip placed through `PlaceControl`);
  - double-clicks to wire;
  - adds one line of user code and runs `SaveAsync`;
  - runs a real CLI `build` of a JavaScript project;
  - RUNS the page under node with `RunPageUnderNode` (handler fires, no `LOAD ERROR`);
  - lays it out in Edge with the Task 13 harness, applying the §7 checks.
- The Grid walkthrough stays pinned (Task 5).

**Risks:**
- The static clipboard and shared temp dirs: use the fixture's own `_dir`.
- `BL8018` must be absent (dispatch call present).
- The JS web-build rows may fail with `ERROR_USER_MAPPED_FILE` on this machine. A/B on the base commit before calling a failure a regression.

**Gate:** the twin plus the existing web/WinForms walkthroughs, by name.

## Task 15: Mutation checks, on the load-bearing rules

Use the Edit tool to mutate, rebuild, and see red. Then Edit to revert, rebuild, and see green. ⛔ Never `Copy-Item` (MSBuild keeps the mutant DLL on an old mtime); never `git checkout --`. Record each red test name in the gate commit message.

| # | Rule | Mutant | Must go red (at least) |
|---|---|---|---|
| M1 | Vocabulary by (target, layout) | `FormVocabulary.IsPixel` returns `target == FormTarget.WinForms` | `FormVocabularyTests.IsPixel_…(Web, Canvas)`, `FormRootLayoutTests.ACanvasPage_ModelsItsDesignSize…`, `FormPixelVocabularyTests.ACanvasPagesControls_ReadPixelGeometry…` |
| M2 | The layout predicate | `Applies` ignores `WebLayouts` (return after `AppliesTo`) | `FormRootLayoutTests.TheRootRowsThatApply…(Web Grid)` and `(Web Flow)`, `TheGrid_OnAFlowPage_ShowsGap…` |
| M3 | The `<Layout>` pre-scan | move the read back into the element loop (delete the pre-scan block, restore the loop's assignment) | `FormRootLayoutTests.TheLayoutIsPreScanned…`, `FormPixelVocabularyTests.ControlsAreReadAsPixels_EvenWhenTheLayoutComesLast` |
| M4a | Anchor far edge | `farInset = container - offset` (drop `- used`) | `FormAnchorCssTests.Right_NotLeft…`, `LeftAndRight…`, `Bottom_NotTop…` |
| M4b | The centring formula | `shift = offset + container / 2.0` | `TheCentringFormula_…` (all rows), `NoneOnEitherAxis…` |
| M5 | Docking order | iterate `siblings` reversed in `ResolveSiblings` | `FormDockLayoutTests.ADockTopPanel_BeforeTheMenuStrip…`, `Strips_StackFromTheirEdge…`; after Task 9 also the canvas agreement test |
| M6 | Strips inside the form area | emit Canvas strips outside `.vgs-form` | Task 10's strips-inside test; Task 13's design-size check |
| M7 | Reading-order grouping | `entry.Bounds.Y >= rowBottom` → `entry.Bounds.Y > rowBottom` | `FormReadingOrderTests.ControlsThatOnlyTouch_AreDifferentRows` |
| M8 | The breakpoint query | emit the query for breakpoint 0 / drop it | Task 10's query tests |
| M9 | Cross-layout paste refusal | delete `\|\| sourceLayout != destination` | `FormPixelVocabularyTests.AGridFragment_IntoACanvasPage_IsRefused…`, `PastingAGridPagesControl_IntoACanvasPage…` |
| M10 | Band height = DefaultHeight | `OwnSize` returns `(0, 20)` for a strip | `FormDockLayoutTests.Strips_StackFromTheirEdge…`, `AFill_BetweenAMenuAndAStatusStrip…` |
| M11 | Raw breakpoint storage | `ReadLayout` stops reading `MobileBreakpoint` | `FormMobileBreakpointTests.AValue_IsReadRoundTripsAndIsCanon`, `AnUnusableValue…` |

A mutant that turns nothing red is a finding: add the missing test, or record why the rule is covered elsewhere (as slice 2's M6 did).

## Task 16: Gate, IDE drop, and the owner's click-through

- **Step 1: Clean build.** Run `dotnet clean` on both projects (AXAML may have changed in Task 9), then build the Shell and the test project in Release.
- **Step 2: Fast subset** into `$sp\gate-fast.txt` (both streams). Compare the sorted failure NAMES against `baseline-fast.txt` (Task 0): zero new names. State the base with every number.
- **Step 3: Integration.**
  - Every fixture named in Tasks 5, 7, 9–14, plus `WinFormsCatalogSweepTests`, `FormDesignerAcceptanceTests`, `FormMenuAcceptanceTests`, `FormComponentAcceptanceTests`, `FormBuildEmissionTests`, `FormAnchorEmissionTests`, `FormRetargetPairTests`, `WinFormsReferenceHarnessTests`, `PixelPageLayoutTests`.
  - Compare names with `baseline-int.txt`.
  - The renamed `FormRootRetargetTests` rows (Task 2) are new names, not regressions.
- **Step 4: Records.** Update `docs/HANDOFF.md` ("NEWEST"), because auto-memory does not travel, and the auto-memory index line.
- **Step 5: Commit** the gate with the mutation table's red names and both gates' numbers with their base.
- **Step 6: IDE drop.** On Windows run `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E`. ⛔ Never `/MIR`; keep `IDE\lib\js\dom-core.bli`. Stage by name.
- **Step 7: The owner's click-through.** The owner runs `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe` (or the refreshed drop). The script:
  1. New web form: the surface shows the alignment grid and form grips, no title bar. The grid shows ClientSize 800,450 and MobileBreakpoint 600.
  2. Drop a Label, TextBox and Button. Drag one; resize one. Set the Button's Anchor to Right.
  3. Add a MenuStrip; drop a Panel and set its Dock to Fill. The Panel sits under the menu and shows no handles; a drag selects it only.
  4. Open an existing Grid `.blwebform`: unchanged cells.
  5. Copy from it and paste into the Canvas form: refused, with the message in the Error List.
  6. Build the web project and open the page in Edge. At the design size it matches the canvas. Widen the window: the Button follows the right edge and the Panel fills. Narrow below 600: one column in reading order.
  7. Retarget Form… Canvas → WinForms: identical positions, and one BL8024 for MobileBreakpoint.
  8. Record the findings. Each reported defect gets a failing real-view test before its fix.

---

## Traps (the repo-specific ones that apply here)

- ⛔⛔ **A `FormRoot` row's applicability is `FormRootValues.Applies`, never `row.AppliesTo(target)`.** ClientSize targets the web from Task 2; a bare `AppliesTo(Web)` says it exists on a Grid page. The only `AppliesTo` over root rows that stays is a WinForms-only filter in the parity/csc sweeps, where the two agree.
- ⛔ **`FormVocabulary.IsPixel` is the one vocabulary test.** Never `Target == WinForms` for a geometry, root-size or chrome decision. Task 9's `isWindow` (the title bar) is the one place that legitimately stays target-only: it is about being a WINDOW, not about pixels.
- ⛔ **The reader's pre-scan keeps the LAST `<Layout>`**, as the element loop always did. The writer's `ApplyLayout` edits the FIRST (pre-existing, spec-claims #7). Do not "fix" one side in passing.
- ⛔ **`MobileBreakpoint` is raw text in the model.** Never parse it into the model. Read its number through `EffectiveMobileBreakpoint`/`TryParseMobileBreakpoint`, the one rule.
- ⛔ **`FormDocument.DesignSize` is the one form size**; `SurfaceSize` adapts it. The emitter reads `DesignSize`, never `Width ?? 400`.
- ⛔ **`FormDockLayout` is the one dock resolver; `FormAnchor` is the one anchor parser; `FormAnchorCss` and `FormReadingOrder` are the only CSS deciders.** The canvas and emitter consume them. A second stacking loop, anchor split or order computation is the `Tracks`/`ParseTracks` scar again. `FormControl.IsDockedToBottom` stays the strip-edge lookup, called from inside the resolver.
- ⛔ **The catalog is the single source of truth.** The phone stretch flag is a row facet (`StretchesWhenStacked`), `WebLayouts` is a row facet, band heights are `DefaultHeight`. Never a hand-written `[TestCase]` kind list beside the catalog, and never a `control.Kind` switch in the canvas or emitter. (Task 2's expected-row table pins spec DECISIONS per layout, not kinds.)
- ⛔ **Headless:** `[AvaloniaTest]`; Skia via `DesignerHeadlessApp` (`UseHeadlessDrawing = false`); host the REAL `CodeEditorDocumentView` with `AppStyles.axaml` loaded (rig: `FormPropertyGridRealViewTests.cs`); close every Window; offset repeat clicks (a second click at the same point within the double-click time is a double-click); test at more than one window size and zoom ≠ 1.0; give fixture controls stable ids.
- ⛔ **Never change a bound property inside `Render`.** The binding swallows the exception.
- ⛔ **`dotnet clean` after any AXAML change.**
- ⛔ **Generated code:** one statement per composite (`New Point`/`New Size`); never emit `With`; the JS backend needs `Me.`-qualified self-calls; generated C# is in `namespace GeneratedCode`.
- ⛔ **D9 tiers go through `Accepts` and the reader's Degraded checks.** A Degraded value never reaches generated output: a Degraded `MobileBreakpoint` gives the page the default.
- ⛔ **The ONE selection store:** `SelectInDesigner` writes `Selection` and the grid together. Never set `PropertyGrid.SelectedControl` from the VM.
- ⛔ **A Windows-only prerequisite (WinForms, Edge, csc) SKIPS with a reason.** Never pass by absence. Inside `Assert.Multiple` use `TestSkip.IgnoreEvenInsideMultiple`, because `Assert.Ignore` FAILS there.
- ⛔ **Kill spawned processes by their own PID/process tree, never by name.** The owner's Edge is running. Edge also needs a fresh `--user-data-dir`, or it hands off to the running instance and exits.
- ⛔ **Capture both streams** (`cmd /c "… > file 2>&1"`). A "Passed!" line is not the verdict; a passing test prints nothing.
- ⛔ **A green build is not a running page.** Every emitter change is RUN under node (and laid out in Edge from Task 13).
- ⛔ **Mutation reverts use Edit + rebuild**, never `Copy-Item` or `git checkout --`.
- ⛔ **A subagent that adds tests uses Edit on an existing test file.** Write is only for the files this plan creates (new, untracked). A Write over an untracked file once destroyed four tests with nothing going red.
- ⛔ **Stage by name; never `git add -A`; never `csc.dll`; commit via `-F`**; never round-trip a repo file through `Get-Content`/`Set-Content`; never type a backslash-u escape into code (build characters from code points, e.g. `(char)0x2212`).
- ⛔ **No IDE drop before Task 16.** From Task 5 a new web form is Canvas, but the canvas cannot place on Canvas until Task 9.
- ⚠ **`Control.Name` is never set by the generated WinForms code** (chip `task_fa51e644`). Identify controls by their generated field.
- ⚠ **WinForms semantics this plan assumes, for the harness to confirm:** Fill does not consume; the remaining rect is NOT clamped (only sizes handed to controls are — S9, changed on Task 6 review); a hidden control does not dock at run time (`FormDockMode.Runtime`); containers' client area = bounds (GroupBox is the known exception); the region writer emits `Anchor`/`Dock` names verbatim, so a lowercase value is a csc failure the canvas/page tolerate (S11).
- ⚠ **The JS web-build Integration rows may fail with `ERROR_USER_MAPPED_FILE` on this machine.** Compare by NAME against the baseline, and A/B on the base commit before calling one a regression.
- ⚠ **PowerShell 5.1:** no `&&`; `2>&1` on a native exe wraps stderr in ErrorRecords (use `cmd /c`). The Bash tool is banned (hook; wsl.exe).

## Tests to re-check when later tasks land (absence tests that may be vacuous today)

| Test | Why it may be vacuous / change | Re-check in |
|---|---|---|
| `FormRootLayoutTests.TheRootRowsThatApply_FollowTheTargetAndTheLayout` (expected sets) | Canvas gains `MobileBreakpoint` | Task 4 (edit) |
| `FormRootRetargetTests.EveryFormRootRow_CrossesOrIsNamed` (`Sources`) | No Canvas source until the retarget copies the design size; its RetargetPropertyLost arm has no row until MobileBreakpoint is in a source | Task 11 |
| `FormPlacement.PlaceOnWeb`'s "Canvas" refusal text | Unreachable for Canvas after Task 9; the wording is Flow-only | Task 9 |
| `FormCanvasTransformTests` / `FormCanvasRenderTests` docked expectations | No positioned-Dock fixture at `47797002` (spec-claims #8); any found will move intentionally | Task 9 pre-flight |
| The Grid pins in `FormDesignerAcceptanceTests`, `FormComponentAcceptanceTests`, `FormMenuAcceptanceTests`, `FormDesignerCommandTests.OpenWeb` | Grid scaffold kept only for these paths | Piece 4 (retire Grid/Flow) |
| `FormDesignerCommandTests.PastingAWinFormsSubtreeIntoAWebFormIsRefused` | Asserts silence-equivalent outcomes; the refusal is now also REPORTED (S6), so consider asserting the report | Task 3 (optional tighten) |
| `FormPropertyGridTests.AWebPagesFormRows_AreItsGridTracks_NotAClientSize` | Its name claimed "no ClientSize" but it asserted Width/Height only | Task 2 (tightened) |
| `FormAssetEmitterTests` Grid/Flow pinned strings | Must stay byte-identical while the Canvas arm grows | Task 10 |
| `FormAnchorEmissionTests` | RegionWriter's anchor parsing moved to `FormAnchor` | Task 7 (Integration rows by name) |
| `FormDockLayoutTests.AFill_DoesNotConsume…`, `Overflow_ClampsWhatIsLeft_AtZero` | Pin assumed WinForms semantics | Task 12/13 (the harness decides) |
| `RegionWriter.CheckAnchors` | WinForms-only until Task 10 gates it on `IsPixel` | Task 10 |
| `WinFormsCatalogParityTests` | `MobileBreakpoint` is web-only and never compared, by design (spec-claims #2) | Every task adding a root row |
| `FormScaffolderTests.Create_ProducesADocumentThatReadsBackClean` | Asserts only `Layout != null`; true for Grid and Canvas alike | Task 5 (the new Canvas test carries the weight) |

## File map (every file created or modified)

| File | Task | Responsibility |
|---|---|---|
| `BasicLang/Forms/FormVocabulary.cs` | 1 (new) | `IsPixel(target, layout)`, `LayoutOf`, the document overload |
| `BasicLang/Forms/FormControlCatalog.cs` | 2, 3, 4, 10 | `WebLayouts` facet; FormRoot rows by layout; `IsStructural(name, target, layout)`; the MobileBreakpoint row; `StretchesWhenStacked` |
| `BasicLang/Forms/FormRootValues.cs` | 2, 4 | `Applies` (the one predicate); `RowForAttribute(…, layout)`; MobileBreakpoint storage |
| `BasicLang/Forms/Serialization/FormDocumentReader.cs` | 2, 3, 4 | `<Layout>` pre-scan; root size by vocabulary; controls by (target, layout); MobileBreakpoint Degraded |
| `BasicLang/Forms/Serialization/FormDocumentWriter.cs` | 2, 4 | Root size by vocabulary (Create/Apply); MobileBreakpoint on `<Layout>` |
| `BasicLang/Forms/Serialization/FormFile.cs` | 2 | `TierOfRoot` through `Applies` |
| `BasicLang/Forms/FormDocument.cs` | 2, 3, 6 | Width/Height docs; `FormClipboard.Paste` + `FormPasteResult` + layout in fragments; `DesignSize` |
| `BasicLang/Forms/FormGeometry.cs` | 3, 4, 5 | Doc corrections; `FormLayout.MobileBreakpoint` + default + parser; `Canvas` is the default |
| `BasicLang/Forms/RegionWriter.cs` | 2, 7, 10 | Root rows via `Applies`; `FormAnchor` parser; `CheckAnchors` on `IsPixel` |
| `BasicLang/Forms/FormRetarget.cs` | 2, 3, 11 | `_toLayout`; root rows via `Applies`; `IsStructural(…, _toLayout)`; Canvas → WinForms lossless |
| `BasicLang/Forms/FormScaffolder.cs` | 5 | New web forms are Canvas; `webLayout` parameter |
| `BasicLang/Forms/FormDockLayout.cs` | 6 (new) | `FormRect`, `FormDockEdge`, `FormDockedBounds`, `FormDockLayoutResult`, the resolver |
| `BasicLang/Forms/FormAnchorCss.cs` | 7 (new) | `FormAnchorEdges`, `FormAnchor` (the one parser), `FormAnchorCss` |
| `BasicLang/Forms/FormReadingOrder.cs` | 8 (new) | Phone stacking order |
| `BasicLang/Forms/FormAssetEmitter.cs` | 10 | The Canvas page: strips inside, positions, anchors, band heights, phone query |
| `VisualGameStudio.Shell/Controls/FormCanvasTransform.cs` | 6, 9 | `SurfaceSize` adapts `DesignSize`; pixel layout for Canvas; `Bands`/`BoundsOf` through `FormDockLayout` |
| `VisualGameStudio.Shell/Controls/FormCanvasControl.cs` | 9 | Grips/alignment grid for `IsPixel`; no window chrome on a page; docked controls not draggable |
| `VisualGameStudio.Shell/ViewModels/Designer/FormPlacement.cs` | 9 | Canvas drops in pixels; Flow-only refusal wording |
| `VisualGameStudio.Shell/ViewModels/Designer/FormPropertyGridViewModel.cs` | 2 | Form rows via `Applies` |
| `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs` | 3 | Copy records the layout; paste refusal reported |
| `VisualGameStudio.Tests/Compiler/FormVocabularyTests.cs` | 1 (new) | The vocabulary table |
| `VisualGameStudio.Tests/Compiler/FormRootLayoutTests.cs` | 2 (new), 4 | Root rows by (target, layout); pre-scan; Canvas design size; grid rows |
| `VisualGameStudio.Tests/Compiler/FormPixelVocabularyTests.cs` | 3 (new) | IsStructural/reader/writer by layout; clipboard; paste refusal through the real command |
| `VisualGameStudio.Tests/Compiler/FormMobileBreakpointTests.cs` | 4 (new) | Row, storage, Degraded, raw preservation, grid |
| `VisualGameStudio.Tests/Compiler/FormDockLayoutTests.cs` | 6 (new) | The resolver table; DesignSize = SurfaceSize |
| `VisualGameStudio.Tests/Compiler/FormAnchorCssTests.cs` | 7 (new) | Parser; every anchor combination; centring formula; docked insets; invariance |
| `VisualGameStudio.Tests/Compiler/FormReadingOrderTests.cs` | 8 (new) | Reading-order table |
| `VisualGameStudio.Tests/Compiler/FormRootTests.cs` | 2, 4 | `RowForAttribute` by layout; Int sample |
| `VisualGameStudio.Tests/Compiler/FormRootRetargetTests.cs` | 2, 11 | Sweep by (target, layout); guard via `Applies`; Canvas source |
| `VisualGameStudio.Tests/Compiler/FormPropertyGridTests.cs` | 2 | Tightened "no ClientSize on a Grid page" |
| `VisualGameStudio.Tests/Compiler/FormScaffolderTests.cs` | 5 | Canvas default; Grid pin; Flow refused |
| `VisualGameStudio.Tests/Compiler/FormDesignerAcceptanceTests.cs` | 5, 14 | Grid pin; the Canvas twin |
| `VisualGameStudio.Tests/Compiler/FormComponentAcceptanceTests.cs` | 5 | Grid pin |
| `VisualGameStudio.Tests/Compiler/FormMenuAcceptanceTests.cs` | 5 | Grid pin |
| `VisualGameStudio.Tests/Compiler/FormDesignerCommandTests.cs` | 5 | Grid pin (`OpenWeb`) |
| `VisualGameStudio.Tests/Compiler/FormCanvasTransformTests.cs` | 9 | Canvas layout; `Bands`/`BoundsOf` agree with `FormDockLayout` |
| `VisualGameStudio.Tests/Compiler/FormPlacementTests.cs` | 9 | Canvas drops |
| `VisualGameStudio.Tests/Compiler/FormCanvasDropTests.cs` | 9 | VM drop on a Canvas scaffold |
| `VisualGameStudio.Tests/Shell/FormPixelPageRealViewTests.cs` | 9 (new) | The real document view on a Canvas page |
| `VisualGameStudio.Tests/Compiler/FormAssetEmitterTests.cs` | 10 | The Canvas page |
| `VisualGameStudio.Tests/Compiler/FormBuildEmissionTests.cs` | 10 | A Canvas page RUN under node |
| `VisualGameStudio.Tests/Compiler/FormRetargetTests.cs`, `FormRetargetPairTests.cs` | 11 | Canvas → WinForms lossless; csc |
| `VisualGameStudio.Tests/Compiler/PixelLayout/WinFormsReferenceHarness.cs` (+ `WinFormsReferenceHarnessTests.cs`) | 12 (new) | The WinForms window as the resize reference |
| `VisualGameStudio.Tests/Compiler/PixelLayout/EdgeLayoutHarness.cs`, `PixelPageLayoutTests.cs` | 13 (new) | Edge served from localhost; the layout assertions |
| `docs/HANDOFF.md` | 16 | The record |
| `IDE\` (drop) | 16 | `robocopy … /E`, never `/MIR` |
