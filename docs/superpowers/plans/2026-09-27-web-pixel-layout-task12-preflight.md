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
