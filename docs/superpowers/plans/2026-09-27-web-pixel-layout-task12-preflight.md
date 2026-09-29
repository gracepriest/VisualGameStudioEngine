# Task 12 pre-flight: the WinForms reference harness

Plan: `docs/superpowers/plans/2026-09-27-web-pixel-layout.md`, "## Task 12". Spec: `docs/superpowers/specs/2026-09-27-web-pixel-layout-design.md` §7 items 1–6, §7a.
The plan's anchors were taken at `47797002`. This check was made against `feat/web-pixel-layout` @ **`0c7657b9`** (Tasks 0–11 landed). Read-only; nothing but this file changed.

Follow this document **instead of** the plan's Task 12 section. Where they disagree, this document wins. Everything else in the plan (How to build/test, Commits, Traps) still applies.

**OWNER decisions needed: none up front.** ⛔ One conditional stop: if WinForms disagrees with `FormDockLayout` on any docked case, the numbers are reported to the coordinator BEFORE `FormDockLayout`/`FormDockScript` change (a design change; the plan's "the harness wins" still holds, but the change is approved first).

---

## A. Anchor verification (claim → today's file:line at 0c7657b9 → verdict)

| # | Plan / task claim | Today | Verdict |
|---|---|---|---|
| 1 | `ConvertToPair(document, WinForms)` (Task 11) | `BasicLang/Forms/FormRetarget.cs:109`; asks `Refusals` `:181` itself and throws `ArgumentException` on a refused source | holds |
| 2 | Compile the `.bas` with the real CLI as `FormDesignerAcceptanceTests.cs:373-386` | `:374-387` (`LoginForm.bas --target=csharp`, emits `LoginForm.cs`); the same shape is `WinFormsCatalogSweepTests.CompileToCSharp` `:572-598` | **moved** (+1), holds |
| 3 | Driver + `app.csproj` (net8.0-windows, UseWindowsForms) as the acceptance driver `:408-456` | `:389-457`; builds with `dotnet build -c Release --nologo` `:459-460`; finds the exe by name `:465-467` | holds |
| 4 | `CliTestHarness.RunProcess` kills its own tree on timeout `:85` | `VisualGameStudio.Tests/Compiler/CliTestHarness.cs:81-89` (`Kill(entireProcessTree: true)` on its own `Process`) | holds — never by name |
| 5 | Controls found through their generated FIELD by `Id` (spec-claims #9) | `RegionWriter.cs:589/594`: `Private {Id} As {Type}` for every control at every depth and every component | holds; reflection `BindingFlags.Instance \| NonPublic` |
| 6 | Generated C# is in `namespace GeneratedCode` | acceptance driver `:413-414` | holds |
| 7 | WinForms `Controls.Add` in REVERSE document order (so the first in the document is back-most and docks first) | `RegionWriter.cs:750-753`; the root `Me.ClientSize` is emitted BEFORE any control `:630-658`; no `SuspendLayout` | holds — the harness proves docking order |
| 8 | Strip heights `DefaultHeight` 24/25/22 | `FormControlCatalog.cs:1828` (MenuStrip 24), `:1853` (ToolStrip 25), `:1874` (StatusStrip 22) | holds |
| 9 | Bordered Panel | `FormControlCatalog.cs:1411-1417` `BorderStyle` None/FixedSingle/Fixed3D | holds |
| 10 | GroupBox is a container with `Text` | `FormControlCatalog.cs:1422` | holds |
| 11 | `FormDockLayout.Resolve(…, Runtime)` skips hidden controls | `BasicLang/Forms/FormDockLayout.cs:136`, `Participates` `:248-249`, `IsHidden` = `Visible` parses to false (`FormControl.cs:101`) | holds |
| 12 | `TestSkip.IgnoreEvenInsideMultiple` | `VisualGameStudio.Tests/TestSkip.cs:24` | holds |
| 13 | `VisualGameStudio.Tests/Compiler/PixelLayout/` | does not exist | new folder |
| 14 | The retarget's Canvas fixture | `FormRetargetTests.CanvasLogin` `:1281-1297` | exists; NOT reused (its `Anchor` mix is not a table) |

---

## B. Blockers / corrections (these win over the plan)

### B1: One app, several forms — not one build per fixture
- A WinForms `dotnet build` costs 20–40 s. The plan's shape (one app per document) makes a dozen fixtures cost minutes, and Task 13 will call the same harness again.
- **Correction.** `WinFormsReferenceHarness.Measure(workDir, fixtures…)` compiles every fixture's pair with the CLI (one `.bas` each — form names distinct), writes ONE `Driver.cs` that measures each form in turn, builds once and runs once. The test fixture measures everything in `[OneTimeSetUp]`.

### B2: The driver's parser and comparison are pure and unit-tested (fast)
- The mutations the coordinator asked for (a lookup that skips a missing control, a tolerance that swallows a real difference, a clamp that bites unseen, a DPI that is not 96) must turn something red without building WinForms. **Correction.** `WinFormsReferenceHarness.Parse(stdout, fixtures)` and `LayoutComparison.Differences(expected, actual, tolerance)` are pure; `WinFormsReferenceParsingTests` (not Integration) feeds them synthetic driver output.

### B3: Strips are pinned BEFORE `Show`, not after
- The plan: show, then pin, then `PerformLayout`. Anchor distances do not depend on strips (spec §7a) and the root `ClientSize` is emitted first (A7), so pinning between the constructor and `Show` gives the same final layout with one layout pass fewer and no visible re-dock. A fixture flag `PinStrips = false` measures the real AutoSize heights once (the plan's "measure an auto-sized strip once and record").

### B4: DPI — forced unaware, asserted, and the machine's scale recorded
- `Application.SetHighDpiMode(HighDpiMode.DpiUnaware)` first thing in `Main` (spec §7 item 4). Every number is then in 96-DPI logical pixels — the unit Edge's `--force-device-scale-factor=1` CSS px are in. **DPI-honest:** the driver prints `form.DeviceDpi` and the parser REFUSES anything but 96 (so a driver that lost the pin fails rather than reporting scaled numbers). The machine's real scale is recorded from `GetDeviceCaps(DESKTOPHORZRES) / GetDeviceCaps(HORZRES)` (physical/logical width, readable from an unaware process).

### B5: Clamp check is a parse-time assertion
- After every resize the driver prints the ACTUAL `ClientSize`; the parser refuses a snapshot whose actual size differs from the request ("Windows clamped the window"). The form is placed at the working area's top-left (`StartPosition = Manual`) so a size that fits is never pushed off-screen. Fixture sizes stay ≤ 601×401 client, which fits a 1280×672 logical working area (a 1920×1080 display at 150%).

### B6: Hidden controls
- A control whose effective `Visible` is false is reported `visible=0` with its PARENT-RELATIVE `Location`/`Size` (no handle is forced into existence to map it). Comparison of a hidden control is visibility only.

### B7: The window must not disturb the owner
- `ShowInTaskbar = false`, `Opacity = 0` (a layered window; layout is unaffected), placed on the working area. The window is closed and disposed after each fixture.

---

## C. The output Task 13 consumes

In-process C# (the Edge harness is in the same test assembly), plus a JSON copy per form for diagnosis:

```csharp
internal readonly record struct LayoutBox(double X, double Y, double Width, double Height, bool Visible = true);
internal sealed record LayoutSnapshot(string Label, int ClientWidth, int ClientHeight, IReadOnlyDictionary<string, LayoutBox> Controls);
internal sealed record WinFormsReference(string FormName, int DeviceDpi, double DisplayScale, IReadOnlyList<LayoutSnapshot> Snapshots)
{   public LayoutSnapshot this[string label] { get; }   public string ToJson(); }
```
- Coordinates are FORM-CLIENT (spec §7 item 2), every control at every depth that is not an item or a component, keyed by `Id`. Labels: `design` first, then one per step (`Resize(label, w, h)`, `SetVisible(label, id, visible)`).
- `Measure` writes `<workDir>/<Form>.reference.json` (`ToJson`) beside the build.
- `LayoutComparison.Differences(expected, actual, tolerance)` is the one comparison (missing id on either side, visibility, and each of X/Y/Width/Height with `|a − b| > tolerance`). Task 13 calls it with the Edge rects and `tolerance: 1`.
- `PixelLayoutFixtures` holds the fixture documents (XML builders parameterised by design size) so Task 13 lays out the SAME forms. `PixelLayoutModel.Rects(document, FormDockMode.Runtime)` is the model's form-client rect for every control at the document's design size (undocked: stored geometry; docked/strips: `FormDockLayout`), used by both harnesses.

---

## D. TDD steps

1. **Parser/comparison RED** (`WinFormsReferenceParsingTests`, fast): parse synthetic `SCREEN`/`FORM`/`SNAP`/`RECT`/`DONE` lines into snapshots; refuse DPI ≠ 96, a clamped `SNAP`, a `RECT` missing for a requested id, an `ERROR` line, no `DONE`; `Differences` reports a 2px difference at tolerance 1, not a 1px one, reports a missing id on each side and a visibility mismatch.
2. **Harness GREEN**: `WinFormsReferenceHarness.cs` (records, `Measure`, driver text, `Parse`), `PixelLayoutModel.cs` (`Rects`, `LayoutComparison`), `PixelLayoutFixtures.cs`.
3. **Self-test RED→GREEN** (`WinFormsReferenceHarnessTests`, Integration, Windows): `SelfTest` 400×300 — p1 Top|Left (20,20,100,50), p2 Right (280,20,100,50): `design` equals the model within 0; at 600×300 p2.X = 480, p1 unchanged.
4. **The carried cases** (each first asserted against the MODEL; a disagreement on docking = STOP and report):
   - `Anchors` 400×300: all 16 Anchor combinations (Panels), at 601×401 and 341×251 (odd deltas expose the centring's half pixel). Expected from spec §4's table; the centred axis is compared at ±0.5 and its measured value RECORDED.
   - `DockStrips`: MenuStrip, StatusStrip, Panel Fill; design and 600×400 against the model at each size.
   - `TopBeforeMenu`: Panel Dock=Top h40 then MenuStrip.
   - `OverflowV`: Top h400 then Bottom h50 (S9). `OverflowH`: Left w500 then Right w50 (S9).
   - `HiddenDock`: pnlA Top h40 `Visible=false`, pnlB Top h60, pnlC Top|Left, pnlD Bottom|Right. Steps `showA` (A visible → B re-docks to Y=40; C, D unmoved), `hideA` (back).
   - `Bordered`: Panel None / FixedSingle / Fixed3D and a GroupBox, each with a positioned child and a Dock=Top child. MEASURED and RECORDED: the insets become the assertion (a known gap, spec-claims #11), never a model change.
   - `StripsPinned` / `StripsAuto` (same document, `PinStrips` true/false): pinned = 24/25/22 exactly; auto heights RECORDED.
5. **Records**: the measured table goes in this file's execution notes and the commit message.

## E. Mutation list (apply, see red, restore)
1. Driver snapshots BEFORE applying a resize (swap the two lines) → self-test `wide` red.
2. Driver does not `Show` the form (layout without a handle) → record what goes red (docking may still run; if nothing goes red, record it as equivalent).
3. Harness id list = top-level controls only (drop nesting) → `Bordered`/parser "missing RECT" red.
4. Parser skips a requested id with no `RECT` (continue instead of throw) → parsing test red.
5. `Differences` ignores ids missing from `actual` → parsing test red.
6. `Differences` tolerance `>` → `> tolerance + 1` → parsing test red.
7. Parser drops the clamp check → parsing test red.
8. Parser drops the DPI check → parsing test red.
9. Driver stops pinning strips → `StripsPinned` red (if auto heights equal the catalog's, record it as equivalent on this machine).

## F. Gate
Fast subset vs `0c7657b9` (8292 / 8286 / 5 / 1; the five known names) + `WinFormsReferenceParsingTests`; Integration `WinFormsReferenceHarnessTests` (must RUN on this machine, 0 skipped) and `FormRetargetPairTests` by name.

---

### Execution notes (measured on the owner's Windows 11 machine, `feat/web-pixel-layout` @ `1bb8789f`)

**Machine:** `DeviceDpi` 96, display scale 1.0 (physical ÷ logical screen width) — the primary display runs at 100%, so the DpiUnaware pin could not be exercised against a scaled display here; the parser's DPI refusal is what guards a scaled machine. One build + one run of all 10 forms: ~17 s warm.

**B8 (correction found by running, wins over spec §7 item 2).** `form.PointToClient(c.PointToScreen(Point.Empty))` is the control's CLIENT origin. For a bordered Panel that is 1px (FixedSingle) / 2px (Fixed3D) inside its own box — the first run read a Panel at (200,10) as (201,11). The driver maps the control's `Location` from its PARENT's client area (`form.PointToClient(c.Parent.PointToScreen(c.Location))`), i.e. the window rectangle — what Edge's `getBoundingClientRect` (border box) reports. Task 13 must compare border boxes.

**FormDockLayout: AGREES with the real window on every docked case, at 0px.** S9 is now "as run". No FormDockLayout/FormDockScript change.

| Case | WinForms (form-client X, Y, W×H) | Model |
|---|---|---|
| Fill between MenuStrip + StatusStrip, 400×300 | menu (0,0,400×24), status (0,278,400×22), fill (0,24,400×254) | = |
| same at 600×400 | menu (0,0,600×24), status (0,378,600×22), fill (0,24,600×354) | = (Resolve at 600×400) |
| Dock=Top Panel h40 BEFORE the MenuStrip | band (0,0,400×40), menu (0,40,400×24) | = |
| S9 overflow: Top h400, Bottom h50, Fill | top (0,0,400×400), bottom (0,250,400×50), fill (0,400,400×0) | = |
| S9 overflow: Left w500, Right w50, Fill | left (0,0,500×300), right (350,0,50×300), fill (500,0,0×300) | = |
| Visible=false docked A (Top h40) before B (Top h60), startup | A hidden, B (0,0,400×60) | = (Runtime) |
| A shown at run time | A (0,0,400×40), B (0,40,400×60); anchored C (10,150) and D Bottom,Right (300,240) unmoved | = |
| A hidden again | B back at (0,0) | = |

**Anchors (spec §4 table), all 16 combinations, 80×60 Panels:** at 601×401 (+201,+101) and 341×251 (−59,−49) every near/far/stretch axis is exactly the table. A CENTRED axis (neither edge) is `offset + floor(d/2)` — floored toward −∞: +201 → +100, −59 → −30 (e.g. `a0` None: (110,60) grown, (−20,−15) shrunk; spec 110.5/60.5 and −19.5/−14.5). The page's `calc(50% …)` keeps the half: a 0.5px difference, inside Task 13's ±1, recorded exactly in the test. Stretched sizes shrink without clamping here (80−59 = 21, 60−49 = 11).

**Recorded gaps (measured, NOT fixed — the model takes a container's client area as its bounds):**

| Container at (x,y) 180×100 | Dock=Top child | Positioned child stored (10,40) |
|---|---|---|
| Panel, BorderStyle None (10,10) | (10,10,180×20) = model | (20,50) = model |
| Panel FixedSingle (200,10) | (201,11,178×20) — +1 each side | (211,51) — +1,+1 |
| Panel Fixed3D (10,130) | (12,132,176×20) — +2 each side | (22,172) — +2,+2 |
| GroupBox "Group" (200,130) | (203,149,174×20) — DisplayRectangle: +3 sides, +19 top | (210,170) — NOT inset |

**Strip heights at 96 DPI** (.NET 8 default font, one item each): auto-sized MenuStrip/ToolStrip/StatusStrip = 24/25/22 — exactly the catalog's `DefaultHeight`. Pinned: the same. The pin is kept (a different font or DPI would differ).

**Red/green.** Parser/comparison tests: 11 written with the harness (red shown by mutants 4–8 below, not by a pre-implementation run). Integration first run against the MODEL: 16 passed / 1 failed (Bordered: the B8 client-origin defect plus the real insets); after B8 and recording the insets: 28/28 (17 Integration + 11 fast), **0 skipped**.

**Mutations (apply, rebuild, run, restore):**

| # | Mutant | Result |
|---|---|---|
| 1 | Snapshot after `ClientSize =` but before `Settle()` | SURVIVED — equivalent: WinForms lays out synchronously inside the ClientSize setter |
| 1b | Snapshot BEFORE the resize | killed — 17 red (parser's size refusal in OneTimeSetUp); message now names both causes (clamp, or measured before the resize) |
| 2 | Driver never `Show`s the form | killed — 13 red; it exposed the anchor test passing with every control hidden → the anchor test now requires `Visible`; re-run: 16 red |
| 3 | Harness ids = top-level only | killed — 1 red (`BorderedContainers_…`) |
| 4 | Parser skips an id with no RECT | killed — 1 red (`AControlTheDriverDidNotReport_IsRefused_NeverSkipped`) |
| 5 | `Differences` ignores a control missing from `actual` | killed — 1 red (`Differences_AMissingControl_OnEitherSide_IsReported`) |
| 6 | Tolerance `> tolerance + 1` | killed — 1 red (`Differences_WithinTheTolerance_…`) |
| 7 | Parser drops the clamp check | killed — 1 red (`AClampedWindow_IsRefused`) |
| 8 | Parser drops the DPI check | killed — 1 red (`AScaledForm_IsRefused_…`) |
| 9 | Driver stops pinning strips | SURVIVED — equivalent on this machine (auto heights = catalog) |
| 11 | Measure with `c.PointToScreen(Point.Empty)` (spec §7's formula) | killed — 1 red (`BorderedContainers_…`) |
| 12 | Drop `SetHighDpiMode(DpiUnaware)` | SURVIVED — equivalent at display scale 1.0; mutant 8's refusal guards a scaled display |
| — | Driver skips a missing FIELD (`continue` instead of throw) | not run: every fixture control has its field; if one were skipped the driver prints no RECT for it and mutant 4's refusal fires |

### Review round 1 (Task 12 quality review)
- **I-1:** two fixtures joined the one run. `HiddenBox` (a `Visible=false` Panel holding a child) and `DockedBox` (a Dock=Left Panel stored at (200,100) 120×50, docked at (0,0) 120×300, holding a positioned and a Dock=Top child, also at 500×360) both match the model at 0px. The model mutants are killed: A (children inherit the container's `shown`, not its `visible`) 1 red (`AHiddenContainer_HidesItsChildren`); B (children offset by the stored X/Y) 3 red.
- ⛔ **OPEN, reported to the coordinator, nothing changed:** a Bottom,Right child in that docked container measures at **Y=500** (grown to 500×360: **560**), while the model and the page, which anchor against `ClientSizeOf` (the docked bounds), say **250** (310). WinForms captures the anchor distances when the child is ADDED. At that moment the container still has its STORED 120×50, because it is docked only when it is itself added to the form. Recorded in `DockedAnchor` / `OPEN_AnAnchoredChildOfADockedContainer_…`, which pins the measured and the model values. The fix is one of two: the region writer emits a docked container's resolved size as its `Size`, or the model/page anchor against the stored size.
- ✅ **DECIDED (coordinator), implemented:** the region writer writes every docked positioned control's `Size` as its `FormDockLayout.Resolve(…, Designer)` bounds. One rule; Location stays as stored. The page (`FormAssetEmitter.CanvasPlacement`) anchors undocked children against the DESIGNER client size, not Runtime-first. `PixelLayoutModel` takes its anchor bases from Designer too. New fixture `HiddenSiblingAnchor`: a hidden Dock=Top 40px sibling makes a Dock=Left container 120×260 at design and 120×300 at run time. WinForms measured `boxBR` Bottom,Right (60,240) (design Y 200 + 40), `boxTB` Top,Bottom,Left 30×190 (150 + 40), `boxC` None (70,120) (100 + floor(40/2)). With the sibling shown: box (0,40,120×260), children at their design positions (boxBR (60,240) = 40 + 200, boxC (70,140), boxTB (10,50) 30×150). Before the fix (stored 120×50): boxBR (60,450), boxTB 400 tall, boxC (70,225). `DockedAnchor` is now 250 (310 grown), matching the model at 0px. The reflow script needs no change: it writes rules only for the docked nodes it places, and an anchored child's distances are constant in WinForms too. No existing test expectation changed. Mutants: stored size emitted again, 5 red; page anchored to Runtime first, 1 red (`AnAnchoredChild_IsAnchoredAgainstItsContainersDesignerClientSize_NotItsRunTimeSize`); model anchored to Runtime, 1 red (`AHiddenDockedSibling_…`).
- **Re-review (N-1/N-2 + minors):**
  - **N-1:** `SetUnhandledExceptionMode(ThrowException)` CRASHED the driver on purpose every run (0xe0434352 → 0xc000041d), leaving a WER report and a crash dump each time. That crash also explains the zero-thread "exited" `ReferenceDriver` entries. It is replaced by an `Application.ThreadException` handler that prints the ERROR line and calls `Environment.Exit(3)`, attached before any window; the `AppDomain.UnhandledException` hook also exits cleanly now. The error test reports in 3.5 s. `%LOCALAPPDATA%\CrashDumps\ReferenceDriver*.dmp`: 10 files, newest 18:33:24, both before AND after a full PixelLayout run, so no new dump. Mutant (no handler): red. That run ended at 1 m 32 s with no exception reported, rather than at the 120 s timeout, which fits the modal dialog being dismissed with Continue (WinForms then swallows the exception and the driver prints DONE).
  - **N-2:** new fixture `NestedDock`. A Dock=Left outer (stored 120×50) holds a Dock=Bottom inner (stored 60×100), which holds a Bottom,Right child. Measured: inner (0,200,120×100) and child (60,270,40×20) at design; inner (0,260,120×100) and child (60,330) at 500×360. The model agrees at 0px. Mutant "resolved Size only for docked controls of the form root": 2 red.
  - Minors: the region writer resolves once per write (a parameter through `AppendSiblings`/`AppendControlInit`). A docked control resolved to 0×0 still writes `Size = New Size(0, 0)`: `Write_WinForms_ADockedControlResolvedToZero_StillWritesItsZeroSize`, mutant 1 red. The follow-up (the property grid shows the stored Size of a docked control) is recorded in plan Task 16.
- **I-2:** each FORM line now carries `Application.HighDpiMode`, and the parser refuses anything but DpiUnaware (`AnAwareProcess_IsRefused_EvenAt96Dpi` ×2). ⚠ Mutant 12 (drop the call) STILL survives, and must: a .NET 8 WinForms exe with no manifest and no `ApplicationHighDpiMode` is unaware by DEFAULT, and the FORM line printed `DpiUnaware` with the call removed (measured). The live risk is a driver that ends up AWARE. Mutant 12' (`SetHighDpiMode(PerMonitorV2)`) is killed with 23 red, on this 100% display.
- **I-3:** spec §7 item 2 and plan Task 12/13 are amended: the parent-mapped Location, and Edge's border box minus the form area's CLIENT origin.
- **I-4:** `SetUnhandledExceptionMode(ThrowException)` goes first in `Main`. ⚠ MEASURED: a rethrown window-procedure exception does NOT reach `Main`'s catch. It escapes through the native frames and ends the process ("Unhandled exception", ~8 s, no dialog). So an `AppDomain.UnhandledException` hook prints it as the ERROR line first. `Measure` parses before it checks the exit code, so the ERROR line is the reported reason. Test `AnExceptionInAWindowProcedure_IsTheDriversError_NotAHang`: a Resize handler patched into the pair throws. It is reported in ~8 s. Mutant (drop `ThrowException`): killed, with a 120 s timeout (the modal dialog). `ReferenceFixture.EditCode` is the hook for the patch.
- **M-1/M-3:** `PixelLayoutModel.AnchorAxis` is the anchor oracle (spec value and the measured floored WinForms value). `Rects(document, mode, clientSize)` re-docks through `ResolveSiblings` in each container's current client area and re-anchors against the container's growth. The anchor test and the grown DockedBox are held to it at 0px, and `TheModel_ReDocksAtAnotherSize_…` pins that it equals a document rebuilt at that size.
- ⚠ Process check: after this round, 10 `ReferenceDriver` entries remain with **0 threads and 0 handles**, parents gone, `HasExited` unreadable. They are exited processes whose kernel objects another process still holds open, not running programs, so there is nothing to kill. Their creation times pair up with this round's runs (main + boom).
