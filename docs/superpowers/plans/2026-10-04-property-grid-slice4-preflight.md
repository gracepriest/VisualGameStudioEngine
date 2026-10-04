# Slice 4 pre-flight: the editors, expanded and corrected against today's tree

Plan: `docs/superpowers/plans/2026-09-25-property-grid-vs-parity.md`, section "## Slice 4 — The editors (TASK
granularity)" (Tasks 4.1–4.7), plus its Traps and "Tests to re-check" sections. Spec:
`docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md` §2.2 (Image row), §4 (editors), §7, §8.
Shape copied from `2026-09-29-property-grid-slice3-preflight.md`.

Made against **origin/master @ `0f6d2ced`** (slices 1–3 merged). Branch `feat/property-grid-slice4`, worktree
`scratchpad\wt-pg4`. Follow this document **alongside** the plan. Where the two disagree, **this document wins**.

Every file:line below was read on `0f6d2ced`. Line numbers drift: re-anchor by the quoted symbol, not the number.

---

## 0. Measured before writing (scratch probes, nothing in the repo)

| # | What | Result | How |
|---|---|---|---|
| M1 | `Avalonia.Controls.ColorPicker` **11.3.13** restores and matches the repo's Avalonia | ✅ Shell pins Avalonia/Desktop/Fluent/Fonts.Inter/DataGrid all at 11.3.13 (`VisualGameStudio.Shell.csproj:17-26`); the 11.3.13 package is already in the local NuGet cache | scratch NUnit project, same packages as `VisualGameStudio.Tests.csproj:28-44` |
| M2 | Headless Skia renders `ColorView` | ✅ **with** the theme include: 99 visual descendants, 684 distinct colours sampled from `CaptureRenderedFrame`. ⛔ **without** it: **0** visuals, a one-colour frame. The control is template-less without its theme, and nothing throws | `DesignerHeadlessApp`'s exact builder (`UseSkia` + `UseHeadlessDrawing = false` + `FluentTheme`) |
| M3 | Theme include URI | `avares://Avalonia.Controls.ColorPicker/Themes/Fluent/Fluent.xaml` loads as an `Avalonia.Styling.Styles`. ⚠ The first attempt also got **0 visuals with the include present**: the include was added to a window whose `Content` was already set. It rendered when the include was in scope before the control attached and a second layout pass ran | probe |
| M4 | `ShowDialog` under the headless platform | ✅ `owner.OwnedWindows.Count == 1`, the `Task<string?>` completes with the `Close(result)` value. A multi-line `TextBox` gets **`\r\n`** for Enter on Windows (`"a, b\r\nc"`) | probe |
| M5 | `Flyout.ShowAt` under the headless platform | ✅ `IsOpen == true`, with a `ColorView` as content | probe |
| M6 | Installed fonts under headless Skia | `FontManager.Current.SystemFonts` returned **187** families on this machine (Arial, Bahnschrift, Calibri…). The list depends on the machine, so no test may assert on it. Inject the source | probe |
| M7 | The fully qualified shape through BasicLang → C#: `System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources/logo.png"))` and `New System.Drawing.Icon(…)` | Emitted verbatim (`pic.Image = System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources/logo.png"));`, `this.Icon = new System.Drawing.Icon(…)`). ⚠ Measured on the **prebuilt `IDE\BasicLang.exe`** (a stale drop), single-file route. That route prints BL6017/BL6016 warnings (System.Object has no member…; `System.Drawing.Icon` not found). A UseWindowsForms project skips .NET resolution (`EnableNetResolution` returns early), so the project route should be silent. **Re-measure on the branch, through the project route, and through csc**, before Task 8 relies on it; a warning per image row on the project route would be noise to fix | CLI `--target=csharp` |
| M8 | Where a relative image path resolves, under BOTH launch shapes the shipping tools use: the exe directly (the IDE's `ExecutablePath`), and `dotnet "<App>.dll"` with an inherited working directory (`BasicLang.exe run`: `HandleRunCommand` prefers `<Name>.dll` and starts `dotnet "<dll>"` with no `WorkingDirectory`, `Program.cs:1266-1295`) | Both launched from a working directory that is NOT the output folder: `Application.StartupPath` = `AppContext.BaseDirectory` = the output folder, and `Image.FromFile(Path.Combine(<either>, "Resources/logo.png"))` loaded the 3×2 png in **both** shapes. `Image.FromFile` against the working directory threw `FileNotFoundException` in **both** shapes. Both exit codes 0 | a scratch `net8.0-windows` WinForms exe (`scratchpad\startprobe`), started with `Start-Process -WorkingDirectory <other dir>` as `StartProbe.exe` and as `dotnet StartProbe.dll` |

## 1. Re-anchored facts (every claim the slice-4 plan text makes)

| Plan claim | Where it is on `0f6d2ced` | Holds? |
|---|---|---|
| Colour rows are a text box | `FormPropertyRow.IsTextBox` includes `Color` (`FormPropertyRow.cs:331-334`). `IsColor` exists (`:342-348`, its comment says "Colour rows are TEXT for now") but **no AXAML binds it**, so it is an unreachable flag. Only `FormPropertyGridTests.cs:565` reads it | ✅. The flag has no caller (see the CLAUDE.md "no caller" rule) |
| "Replaces the Color text row" | — | ⚠ **Corrected (D-1a):** the text stays. VS has both typed text and a drop-down, and §3 says the parent accepts typed text. The swatch drop-down sits BESIDE the text box |
| Package `Avalonia.Controls.ColorPicker` 11.3.13 + "its theme include in `App.axaml`" | `App.axaml:11-21` includes Fluent, DataGrid, AvaloniaEdit, Dock, AppStyles | ❌ **Corrected (D-1b):** `App.axaml` is the wrong home. The headless test app is a bare `Application` + `FluentTheme` (`DesignerHeadlessApp.cs:28-32`). The real-view rig adds ONLY `AppStyles.axaml` (`FormPropertyGridRealViewTests.cs:195-202`). An include in `App.axaml` is never loaded by any test, and M2 shows the result: a ColorView that draws nothing while the test passes |
| Bool is a ToggleSwitch | `TypedValueEditor.axaml:37-39`. ⚠ This is the **SHARED** editor (its comment `:32-35`). `SettingsDialog.axaml` uses it too | ⚠ **Corrected (D-3):** replacing it there changes the Settings dialog. The grid maps Bool onto the shared editor's EXISTING ComboBox arm instead |
| `AnAbsentEnabled_ShowsAsOnAndGreyed_…` finds a ToggleSwitch | Real name `AnAbsentEnabled_ShowsOnAndGreyed_AndAPresentText_IsBold` (`FormPropertyGridRealViewTests.cs:685-703`, `OfType<ToggleSwitch>().Single()` at `:693`) | ✅, and it is not the only one: `ExpandingAFont_AndSwitchingBold_WritesTheWholeFont_AtTwoSizes` (`:594-626`, ToggleSwitch at `:613`, the Font's Bold PART) and `FormPropertyGridTests.cs:563,566` (`IsCheckBox` true) break too |
| Enum/Cursor drop-downs (4.4) | **Already exist.** `IsComboBox => Typed && _type is Enum or Cursor` (`FormPropertyRow.cs:323`). `Choices` comes from `FormPropertyDef.Choices` (`FormControlCatalog.cs:388`) | ⚠ 4.4 shrinks to: Anchor/Dock as pop-ups, plus slice-3 review **M5** (the web Cursor list offers members the web refuses) |
| Anchor/Dock "today's visual pickers" | INTRINSIC rows, pixel geometry only (`FormPropertyGridViewModel.cs:392-408`). Drawn INLINE as 46×46 boxes in every row template (`FormPropertyGridView.axaml:189-246`) | ✅ |
| `FormPropertyDef.SplitItems` splits on commas | `FormControlCatalog.cs:607-609`: `Split(',', RemoveEmptyEntries \| TrimEntries)` | ✅. Consumers: `RegionWriter.cs:1081-1090` (`Items.Add` per item), `FormAssetEmitter.cs:413-438` (`<option>` per item). A THIRD serializer copies the raw attribute: `FormClipboard.ToElement`/`FromElement` (`FormDocument.cs:498`, `:586`, attribute copy `:609-625`). Items rows: ComboBox `:1789`, ListBox `:1808`, CheckedListBox `:2047` (`String`, `IsItemCollection: true`) |
| PictureBox `Image` is `String` + `WinFormsFactory` | `FormControlCatalog.cs:1870-1873` (`WinFormsFactory: "Image.FromFile"`). The literal is built at `:496-499`. Parity keys on the factory name (`CatalogParity.cs:132`) | ✅ |
| Nothing copies the image | Correct, and worse than the spec says: `Image.FromFile("x")` resolves against the **working directory**, not the exe (M8). `PixelPageLayoutTests.cs:101-105` already works around it ("pic.png goes where the driver runs: Image.FromFile is relative to its working directory") | ⚠ **Corrected (D-5b):** copying the file beside the exe is not enough. The emitted path must be anchored to `System.AppContext.BaseDirectory` (M8) |
| Web image = `src` | `FormAssetEmitter.cs:393-397` writes `src="{Attr(image)}"` raw, with no tier check and no URL encoding | ✅. Fixed in Task 8 |
| `FormSystemColors` | `BasicLang/Forms/FormSystemColors.cs:14-29`: **33** members, **8** with a CSS mapping (ButtonFace, Control, ControlText, GrayText, Highlight, HighlightText, Window, WindowText). `Names` `:36`, `CssFor` `:52` | ✅. The web refusal is `IsSystemColourRefusedOn` (`FormControlCatalog.cs:878-880`) |
| Web named colours | `FormKnownColors.cs:15-35` (141 `System.Drawing.Color` names, all valid CSS names). `Names` `:46` | ✅ (not named in the plan) |
| AcceptButton storage | FormRoot `Reference` row, `Targets: WinForms`, `ReferenceKinds: Button` (`FormControlCatalog.cs:2550-2557`). Properties-stored through `FormRootValues`' default arm (`FormRootValues.cs:62`). Emitted after the controls. BL8034 check `RegionWriter.cs:807-827` (`NamesAnAllowedControl`, private) | ✅. Today it is a text box (`IsTextBox` includes `Reference`) |
| `DesignCodes` next free number | `DesignDiagnostic.cs:83`: "the next claim starts at BL8036". BL8001 is a literal in Program.cs/Compiler.cs, BL8031 is reserved, BL8033–BL8035 are taken | ✅ **BL8036** is free on master. ⚠ `feat/portable-controls` (piece 2, in flight) may claim it too. Re-check at merge |
| Form `Icon` "waits for slice 4" | Comment `FormControlCatalog.cs:2509`. Oracle `winforms-metadata.json:466-477`: type `Icon`, category `Window Style`, `defaultKind: reset`, `default: null` | ✅. `Default: null` matches |
| Build-copy files "anchored at slice 4 expansion" | CLI project build `BasicLang/Program.cs:793-965` (outputDir `:794` = `bin/<config>/<tfm>`; web forms `:804-833`; JS emit `:947-965`; C# csproj/`dotnet build` `:967-1157`). IDE `BuildService.CompileWithBasicLangApiAsync` (`:533`; outputDir `:615` = `config.OutputPath`, e.g. `bin\Debug`; web forms `:623-651`; JS `:801-826`; C# `:828-915`; `DeployNativeEngine` `:1509`) | ⚠ **The IDE does NOT delegate to the CLI process.** It runs its own in-process pipeline that mirrors the CLI step for step, and the two output directories differ. A copy written into one route is a mirrored pair → **one shared helper**, called from both (D-5c) |
| Precedent for "copy beside the output" | `JavaScriptEmitter.CopyImportedModules` (`JavaScriptEmitter.cs:213-269`): `SafeZip.IsWithin` containment, the missing file is a `warn`, and it goes through `ReplaceFile` (temp + rename, against `ERROR_USER_MAPPED_FILE`) | ✅ reuse these three rules |

## 2. DECISIONS (owner delegated: "go with your recommendations"; each states what lost, so it can be overruled)

### D-6 (plan 4.6) — Items stored form. ✅ DECIDED (accepted by the coordinator under the owner's delegation, plan review of `88a74491`). It changes the file format and is the hardest call here to reverse; Task 6 records it as an ADR in `docs/superpowers/decisions/` first.

**Decided: child elements, one per item; the model keeps ONE in-memory encoding (LF-joined).**
```xml
<ComboBox Id="cmb" X="16" Y="16" Width="121" Height="23" TabIndex="0">
  <Item>Smith, John</Item>
  <Item>Beta</Item>
</ComboBox>
```
- **Reader** (`BasicLang/Forms/Serialization/FormDocumentReader.cs`, `ReadControl`: attribute loop `:544-609`, child loop `:611-636`): for an
  `IsItemCollection` row, `<Item>` children are read as text in document order, verbatim (no trim). A **legacy**
  `Items="a, b"` attribute is converted with the OLD rule (comma split, trim, drop empties), so every existing
  file loads with the meaning it had. Both are stored in the model as `Properties["Items"] = "Smith, John\nBeta"`.
- **Degraded** (D9, never coerced): an `<Item>` whose text holds CR/LF (the model separator), or a control
  carrying BOTH the attribute and `<Item>` children. The row is frozen with a reason, and `Items` is left OUT of
  `Properties`, so nothing is emitted. **Where the raw content lives:** the reader puts the raw `<Item>` elements
  into `control.UnknownChildren`, and the attribute (when present) into `control.UnknownAttributes`, so every
  path that already carries unknown content carries them. Those paths are: Apply (unknown children are never
  touched), Create (`FormDocumentWriter.cs:700-713` writes both), `FormControl` clone (`FormControl.cs:216-219`),
  the retarget (`FormRetarget.cs:415-417`), and the clipboard (`FormDocument.cs:555`, `:656`). The writer's Items
  path never touches a control whose Items is listed in `FormFile.Degraded`. Task 6 tests each of those five
  paths.
- **Blank items** (`<Item/>`, `<Item>  </Item>`): `Split` drops an empty or whitespace-only entry on every route,
  so a blank item is never emitted on either target. ⛔ The writer's "is it unchanged?" check compares the list the
  existing children READ AS (through the same `Split` rule) with the model's list, never element counts. So a
  document holding `<Item/>` reads as equal, is left alone, and a no-op save stays byte-identical. The blank
  element is dropped only when the user actually edits Items.
- **Writer** (`BasicLang/Forms/Serialization/FormDocumentWriter.cs`): Apply: a legacy attribute whose comma split
  equals the model's lines is LEFT ALONE, so a no-op save stays byte-identical, on a `.blform` AND a `.blwebform`,
  and old files are not rewritten for a selection click. Otherwise the writer removes the attribute and rewrites
  the `<Item>` run, placed first among the element's children, before `<Bind>` and nested controls. `<Item>`
  children that already read as the model's list are left alone. Items absent from the model removes both the
  attribute and the children. Create writes `<Item>` children and never the attribute. Item text goes through
  `XElement` (`&` and `<` escaped by XML, never by hand). The generic `foreach Properties → SetAttributeIfChanged`
  loop (`:535-538`) and the dropped-property sweep (`:540-552`) SKIP `IsItemCollection` rows. Both write sites ask
  one `FormItems.IsCollection(row)`.
- **`SplitItems`** becomes `value.Split('\n')`, with a trailing `\r` stripped and empty or whitespace-only entries
  dropped. Spaces around non-blank text are kept. A new `FormItems.Join` is its inverse. A new
  `FormItems.FromLegacy` holds the old comma rule and is called only by the reader.
- **FormClipboard** keeps copying the model string into an attribute. XML escapes the LF as `&#xA;` and reads it
  back exactly. A test pins that (Task 6).
- **Costs, recorded rather than hidden:**
  1. An EMPTY-string item (`Items.Add("")`, legal in WinForms) is unrepresentable: `Split` drops it.
  2. On the web, `<option>` text collapses leading, trailing and repeated spaces, so `"  padded "` shows as
     `padded` on the page while WinForms keeps the spaces. A web/WinForms divergence the model cannot prevent.
  3. An item containing a line break cannot be created in the editor, and a hand-written one is Degraded.

**Lost — an escaping scheme inside the attribute** (`Smith\, John`). Every hand-editor would have to learn the
escape. A legacy `C:\a,b` would change meaning. Items still could not keep leading or trailing spaces (the trim
stays).
**Lost — LF inside the attribute** (`Items="Smith, John&#xA;Beta"`). A one-item list containing a comma has no
LF, so it is indistinguishable from a legacy two-item list. It would need a sentinel trailing LF, and the file
would show `&#xA;` entities to anyone reading it.
**Lost — commas forever.** The plan's own trap: an item containing a comma cannot round-trip.

### D-1 (plan 4.1) — Colour editor
- **D-1a:** a swatch **button with a drop-down glyph** to the LEFT of the existing text box. It opens a
  `Flyout` (M5) holding a `TabControl` with **Custom** (`ColorView`), **Web** (`FormKnownColors.Names`, each with a
  swatch) and **System** (`FormSystemColors.Names`). The text box stays the row's own editor (typed `#hex` or
  names).
- **D-1b (revised per plan review):** package `Avalonia.Controls.ColorPicker` **11.3.13** in
  `VisualGameStudio.Shell.csproj` (M1). The theme `StyleInclude` has ONE home: **`FormPropertyGridView.axaml`'s
  `UserControl.Styles`** (beside the existing styles, `:13-56`). It is NOT in `App.axaml`, and NOT per drop-down
  instance (one copy per row would load the theme once per colour row). The real-view rig hosts the real
  `FormPropertyGridView`, so tests and the IDE load the same single copy, in scope before any row's template
  applies (M2/M3). ⚠ The `ColorView` lives inside a `Flyout`, whose content is a popup. Styles reach it through
  the logical tree (the flyout is logically parented to its target, as the row's Reset `ContextMenu` already
  is). That is asserted, not assumed: the Task 4 real-view test requires the open ColorView to have visual
  descendants (M2's trap). **Lost:** App.axaml plus a matching add in `DesignerHeadlessApp` and the rig. That is
  a mirrored pair, and its failure mode is a silent blank (M2). **Lost:** per-`FormColorDropDown` Styles
  (review: multiple homes).
- **D-1c:** Web and System **commit on pick** (one Edited). Custom commits **once, when the flyout closes or on
  OK**, never per `ColorChanged`. A drag across the spectrum would otherwise be hundreds of document writes and
  undo entries (the D13 rule `TypedValueEditor.axaml:54-58` states for text). Custom writes `#RRGGBB` at alpha
  255, else `#AARRGGBB` (both already Canon: `IsColor`, `FormControlCatalog.cs:958-975`).
- **D-1d:** which entries are offered is the CATALOG's answer: each list is filtered through
  `row.Definition.Accepts(name, target)`. That hides the 25 system colours with no CSS on a web form, and keeps
  every WinForms name. Never a hand list.
- **D-1e:** the swatch colour comes from `System.Drawing.Color.FromName(name)` / the hex digits (BasicLang
  targets net8.0, and `System.Drawing.Primitives` is in the shared framework on every OS; on Windows it reads the
  live system colour). It is preview only and never written. A Degraded value shows a hatched "?" swatch.

### D-2 (plan 4.2) — Font dialog
- A modal `Window` (`FormFontDialog`), opened from a `…` button on the Font row's value cell (M4: `ShowDialog`
  works headless). It has a family list with a filter box, size (a list of VS's sizes plus free decimal), Bold,
  Italic, Underline and Strikeout, and a live preview. OK writes ONE canonical Font value through the parent
  row's Commit (fan-in). Cancel writes nothing.
- **Family source (cross-platform):** `FontManager.Current.SystemFonts` (Avalonia; Windows plus Linux fontconfig;
  M6), behind an injectable `Func<IEnumerable<string>>` seam so tests are machine-independent. The list is
  **filtered to the families `FormFontValue.TryParse` accepts** (`FormFont.cs:94-95`: letters, digits, spaces,
  hyphens), so a family that would be refused is never offered. The **current value and WinForms' default
  "Segoe UI" are always in the list**, even when not installed (a Linux IDE must still show the form's font).
  **Lost:** a hard-coded family list (wrong on every machine but one). **Lost:** WinForms' `InstalledFontCollection`
  (Windows-only, and the IDE is Avalonia).
- **Start value:** the row's value, or for an ABSENT ambient Font what the control inherits (`FormAmbient.
  Inherited`, the slice-3 review I1 rule the parts already follow: `FormCompositeRows.Attach(row, inherited)` at
  `FormPropertyGridViewModel.cs:652`). Expose that inherited function on the row so the dialog and the parts
  share it.
- **Web:** the same dialog. A hint line in the dialog says the page uses the font only if the viewer has it
  installed. The emitter adds no generic fallback family (follow-up, out of scope).

### D-3 (plan 4.3) — Bool drop-down
- `FormPropertyRow` for a Bool returns `IsCheckBox = false`, `IsComboBox = true`, `Choices = ["True", "False"]`.
  `StringValue` shows `True`/`False` for a Bool (the combo's items must match exactly). `ToDocument` lower-cases a
  Bool (the document vocabulary, `BoolValue`'s comment `:455-457`). `Judge` already treats `True` and `true` as
  one value (`Canonical`, `FormControlCatalog.cs:294-297`). **Zero new AXAML**: the shared editor's ComboBox arm
  renders it, and the Settings dialog keeps its switch.
- ⛔ **Bool rows WITHOUT a catalog definition** — the Font composite's Bold/Italic/Underline PARTS (intrinsic rows
  built by `FormCompositeRows.Part`, `FormCompositeRows.cs:40-52`, read as `"true"`/`"false"` by `Style` at `:74`).
  Their no-op check is the intrinsic arm of `Commit` (`FormPropertyRow.cs:638-641`), an ORDINAL compare with
  `DisplayValue`. A combo pushing `"True"` against `"true"` would be a Write on every selection: a document
  rewrite and an undo entry for a click. **Rule:** in the intrinsic arm, a Bool row compares PARSED values
  (`bool.TryParse` on both sides; equal → NoOp), and an intrinsic Bool write passes the lower-case word. The rule
  lives in ONE helper used by both arms (catalog rows already get it through `Canonical`). The lower case matters
  because the part's `compose` (`:89-97`) `bool.TryParse`s and accepts either case.
- **Double-click toggles:** the container's `DoubleTapped` in `FormPropertyGridView.axaml.cs` calls a new
  `row.ToggleBool()` (VM-tested) when the row is an editable Bool. Enum cycling on double-click (VS does it) is a
  follow-up.
- **Lost:** replacing the ToggleSwitch in `TypedValueEditor`. That changes Settings, which is out of scope.

### D-4 (plan 4.4) — Anchor/Dock pop-ups + choice filtering
- Each row shows a compact summary (`Top, Left` / `Fill` / `None`) and a drop-down button. A `Flyout` holds
  TODAY's box (the same bindings, moved, not rewritten). The rows lose their 46px height.
- `FormPropertyRow.Choices` filters `_choices` through `Accepts(choice, _target)`. Generic, so it does M5 (the
  web Cursor list drops the 11 members with no CSS) for every row, and no Cursor special case exists.

### D-5 (plan 4.5) — Image and Icon
- **D-5a — two new types.** `FormPropertyType.Image` (PictureBox.Image) and `FormPropertyType.Icon` (Form.Icon).
  They are separate because their per-target value rules differ, and those rules are run-time crashes otherwise:
  - stored: a path relative to the **project directory**, with forward slashes (`Resources/logo.png`). The reader
    accepts backslashes, and every consumer normalises through one `FormAssetPaths`.
  - Canon on both targets: a relative path inside the project.
  - Rooted absolute (`C:\pics\a.png`): WinForms only, as a VALUE. It is emitted verbatim as today, keeps legacy
    documents working, and is not copied (BL8036 says so). On the web it is refused with a reason.
  - `http(s)://` URL: web only, as a value (`src` verbatim, not copied). Refused on WinForms
    (`Image.FromFile` reads files).
  - `..` escaping the project: Degraded on both (the `SafeZip.IsWithin` rule).
  - extension: WinForms `Image` refuses `.svg`/`.webp` (GDI+ cannot decode them; `Image.FromFile` throws at run
    time). WinForms `Icon` requires `.ico` (`New Icon` throws `ArgumentException` on a png). Web `Icon` accepts
    `.ico/.png/.svg/.gif`.
  - All of this goes in `IsRefusedOn`/`RefusalReason` (`FormControlCatalog.cs:810-891`), the same predicate as
    system colours. Never a second check.
- **D-5b (revised per plan review — coordinator's decision, confirmed by M8): WinForms emission anchored to the
  app's base directory, FULLY QUALIFIED:**
  `System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources/logo.png"))`
  and `Me.Icon = New System.Drawing.Icon(System.IO.Path.Combine(System.AppContext.BaseDirectory, "Resources/app.ico"))`
  (M7). Every type name is qualified because of CLAUDE.md's CS0104 rule: a user's `Using` can make a bare `Image`
  or `Icon` ambiguous, and BasicLang is silent. `WinFormsFactory: "Image.FromFile"` is removed from the PictureBox
  row, and the Image type owns its literal. M8 measured `AppContext.BaseDirectory` = the output folder under both
  launch shapes (exe direct; `dotnet App.dll` from another working directory, which is `BasicLang.exe run`'s shape).
  **Lost:** bare `Image.FromFile("Resources/logo.png")`. It failed in both shapes (M8). **Lost:**
  `Application.StartupPath`. It measured identical (M8), but it is WinForms-only API surface, while
  `AppContext.BaseDirectory` is the runtime's own answer (coordinator decision).
- **D-5c — where the build copies.** One helper, `BasicLang/Forms/FormAssetCopy.cs`:
  `Copy(IEnumerable<FormDocument> forms, string projectDir, string outputDir, Action<DesignDiagnostic> report)`.
  It walks every control and component (catalog rows of type Image/Icon) plus `FormRoot`'s Icon, and copies
  `projectDir/<path>` → `outputDir/<path>` (sub-path preserved, `ReplaceFile`-style temp + rename,
  `SafeZip.IsWithin` containment).
  - WinForms: `outputDir` = the directory `dotnet build` writes the exe to (CLI `bin/<config>/<tfm>`, IDE
    `config.OutputPath`), so the file lands beside the exe.
  - Web: the site folder (the same `outputDir` the pages are written to), so the page's relative `src` resolves.
  - Called from BOTH routes, for BOTH backends that carry forms: CLI `Program.cs` (after code generation, before
    `dotnet build` / beside `JavaScriptEmitter.Emit`) and IDE `BuildService` (same points).
  - `.blform` documents are loaded for a C# project through a generalised
    `FormDocumentLoader.Load(paths, FormTarget, string consequence)`. `LoadWebForms` stays as a wrapper, so its
    callers are unchanged. The refusal warning's consequence clause is a PARAMETER: today it is hard-coded
    `"'X' was not turned into a page"` (`FormDocumentLoader.cs:61-63`), which would be false on the `.blform`
    route. The web route passes `"was not turned into a page"`; the C# route passes `"its images and icons were
    not copied into the output"`. A test pins each text on its route.
  - **Lost:** hiding the copy inside `JavaScriptEmitter.Emit`. It covers the web for free but leaves WinForms
    needing call sites anyway: two homes for one rule.
- **D-5d — the warning: `DesignCodes.AssetNotCopied = "BL8036"`** (Warning), one code for "a referenced image or
  icon the build did not copy into the output". The message names why: the file is missing at `<abs path>`, it
  resolves outside the project, or it is an absolute path that the program will look for on the machine it
  runs on. The message names `'<Form>.<control>.<Property>'`. The CLI prints
  `  Warning: BL8036: …`. The IDE adds a `DiagnosticItem { Id = "BL8036", Severity = Warning, FilePath =
  <form document> }` and writes the output line, so it reaches the Error List, not only the Output pane. A
  missing file never fails the build (the `#JsImport` precedent). Update the band table in `DesignDiagnostic.cs:54-85`.
- **D-5e — the picker** (`…` on Image/Icon rows). It goes through `TopLevel.StorageProvider.OpenFilePickerAsync`
  behind a view seam (`FormPropertyGridView.PickFile`), because the headless platform has no storage provider.
  - A file inside the project is stored relative to it.
  - A file outside is **offered a copy** into `<project>/Resources/` (a confirm seam, `ConfirmCopy`). Identical
    bytes already there are reused, and a different file with the same name becomes `name (2).ext`.
  - Declining on WinForms stores the absolute path (legal, BL8036 at build). On the web, decline is not offered
    (an absolute path is refused there): Copy or Cancel.
  - The project directory is the nearest ancestor of the form document holding a `*.blproj`, else the
    document's own directory (`FormAssetPaths.ProjectRootFor`). The document view model does not know its
    project (grep: no project reference in `CodeEditorDocumentViewModel.cs`). The build knows it exactly, and the
    two agree whenever the `.blproj` is an ancestor (the normal shape; a test pins a form in a subfolder).
- **D-5f — web equivalents.** Image → `<img src="Resources/logo.png">`, relative to the site and percent-encoded
  per path segment (reuse `JavaScriptEmitter.UrlPath`'s rule; `Attr` alone lets a space or `#` break it). Icon →
  `<link rel="icon" href="…">` in the page head, after `<title>` (`FormAssetEmitter.cs:141`). That makes `Icon` a
  BOTH-targets row: D2 says every row with a clean HTML meaning gets it. The spec's §2.3 web list predates this
  row, and this is an addition, recorded here. A refused web value is not emitted; slice 3's web BL8009 loop in
  `RegionWriter` already names it.

### D-7 (plan 4.7) — Reference editor
- A Reference row is a ComboBox: `(none)` first, then the Ids of every control on the form whose kind is in
  `ReferenceKinds`, in document order. `(none)` = Reset (remove). The candidate list and the BL8034 check are ONE
  function: `RegionWriter.NamesAnAllowedControl` (`:825-827`) moves to a public
  `FormReferences.Candidates(form, row)` / `FormReferences.IsAllowed(form, row, id)` that the region writer and the
  grid both call.
- ⛔ `(none)` is a DISPLAY item, never a value: the row maps it to Reset BEFORE `Judge` is asked, so the text
  `(none)` can never reach `Judge` as a Write (it is a legal-looking string to nothing). Picking `(none)` on an
  absent row is a NoOp.
- **The list follows the document while the Form stays selected.** `Choices` for a Reference row is EVALUATED
  on read (a `Func`, never captured at row construction). The grid raises `PropertyChanged(Choices)` on the
  Reference rows whenever the document changes under it (the existing `Edited`/reload path). So a Button added
  by paste, an undo, or a drop that leaves the Form selected appears in the AcceptButton list without
  reselecting.
- A stored Id naming no candidate (dangling, BL8034) is still SHOWN: the combo gets that Id as an extra item marked
  "(missing)", so the row never silently shows `(none)` over a value the document holds.
- Rename keeping references in step (slice-3 review M6) stays moot: Name is frozen
  (`FormPropertyGridViewModel.cs:356-363`).

### D-8 — web equivalents, per editor (spec §7 "a web form never shows a WinForms-only property")

| Editor | WinForms | Web |
|---|---|---|
| Colour | Custom / Web (141) / System (33) | Custom / Web (141) / System (**8**, filtered by `Accepts`) |
| Font | dialog, installed families | same dialog + "the viewer needs the font" hint |
| Bool | True/False combo | same |
| Enum / Cursor | all members | Cursor: only the 17 CSS-mappable members (`Accepts`) |
| Anchor / Dock | pop-up (pixel geometry) | pop-up on a Canvas page; no rows on Grid/Flow (unchanged) |
| Image | file picker, `System.Drawing.Image.FromFile(System.AppContext.BaseDirectory…)` | file picker, `<img src>` relative to the site; URLs allowed |
| Icon (Form) | `.ico` only, `New System.Drawing.Icon(System.AppContext.BaseDirectory…)` | `<link rel="icon">`; ico/png/svg/gif |
| Items | `Items.Add` per line | `<option>` per line (unchanged emitter, new split) |
| Reference | combo of Buttons | row hidden (`Targets: WinForms`, unchanged) |

## 3. Escalations
None sent to the architect. D-6 (the file format) was the one decision that is expensive to reverse. The
coordinator ACCEPTED it under the owner's delegation (plan review of `88a74491`). Task 6 records it as an ADR in
`docs/superpowers/decisions/` before it writes code.

## 3a. Plan review of `88a74491` — dispositions (this revision)

| # | Item | Disposition |
|---|---|---|
| 1 | `Application.StartupPath` unverified under `dotnet App.dll` | Measured both launch shapes (M8): StartupPath and BaseDirectory are identical, and the working directory fails in both. Adopted `System.AppContext.BaseDirectory`, fully qualified (D-5b). Task 11 runs all three shapes |
| 2 | Image/Icon rows have no editor until Task 10 | `IsTextBox` gains Image and Icon in Task 8, with a test |
| 3 | Task 8 re-check additions | Added: `FormRootTests.EveryFormRootRow_HasStorage_OnBothTargets` sample; `FormWebVocabularyTests.Sample`; `RegionWriter.Literal`'s unreachable arm |
| 4 | PixelPageLayout fix needs the harness | Task 8 changes `WinFormsReferenceHarness.Measure` (an `assets` parameter → `CopyToOutputDirectory` items); file listed |
| 5 | Bool parts compare ordinally | D-3 intrinsic Bool no-op rule + Task 1 test "selecting changes nothing" |
| 6 | D-6 edge cases | Blank items, UnknownChildren paths, `&`/`<`, the `.blwebform` legacy no-op save, and the costs list are all in D-6 and Task 6 |
| 7 | One home for the ColorPicker include | `FormPropertyGridView.axaml` Styles (D-1b) |
| 8 | Task 1 re-checks | Added `FormPropertyGridTests.cs:692-697` and `FormPropertyRowDefaultTests.cs:417` |
| 9 | Vacuous `(none)` mutation; candidate list freshness | Replaced the mutation; `Choices` is evaluated on read and refreshed on Edited, with a test |
| 10 | Exact serializer paths | `BasicLang/Forms/Serialization/FormDocument{Reader,Writer}.cs` in D-6 and Task 6 |
| 11 | LoadWebForms refusal text | Parameterised per route (D-5c) |

## 4. The tasks (TDD, one commit per task)

Every task: red test(s) first, run them, and see them fail for the RIGHT reason. Implement, then go green.
Mutation-check each new branch: revert with the **Edit** tool, then **REBUILD** (a mutant survives a revert until
you rebuild); record killed or equivalent. Commit by name. Never `git add -A`, never `csc.dll`. Message via a
scratchpad file + `git commit -F`. Every real-view test: `[AvaloniaTest]`, the rig's `TwoZooms` (800×560 below
zoom 1.0, 1400×900 at 1.0), the fixture's stable ids (`lbl`/`btn`/`tmr`), and every window closed. **After any
AXAML change: `dotnet clean` before building.**

### Task 0 — Baseline
`dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`. Then the fast subset at
`0f6d2ced`, BOTH streams captured (`cmd /c "dotnet test … --filter ""TestCategory!=Integration"" > file 2>&1"`).
Record total/passed/failed/skipped and the **sorted failure names** with the base SHA. Expected names are only the
known machine rows (§5).

### Task 1 — Bool drop-down + catalog-filtered choices (plan 4.3 + M5)
Files: `FormPropertyRow.cs` (`IsCheckBox` false for the grid, `IsComboBox` adds Bool, `Choices` for Bool
`True`/`False`, `Choices` filtered by `Accepts`, Bool `StringValue` display, `ToggleBool()`); `FormControlCatalog.cs`
(`ToDocument` lower-cases a Bool); `FormPropertyGridView.axaml.cs` (`DoubleTapped` on a row container → `ToggleBool`).
Tests:
- `FormPropertyGridTests`: Bool rows are combo rows with exactly `True, False` (catalog-driven over every Bool
  row of every kind, `TestCaseSource` from `All`). Picking `False` writes `"false"`. A web Cursor row's `Choices`
  equal `FormCursors.Names.Where(n => def.Accepts(n, Web))`. A WinForms Cursor row offers all 28.
- `FormPropertyRowDefaultTests`: an absent `Enabled` shows `True` and picking `True` is a NoOp (no Edited).
- Real view (`FormPropertyGridRealViewTests`): REWRITE `AnAbsentEnabled_ShowsOnAndGreyed_…` (`:685-703`) to find
  the row's visible `ComboBox` with `SelectedItem == "True"`, greyed. REWRITE the Bold step of
  `ExpandingAFont_…` (`:613`) to pick `True` in the Bold part's ComboBox. New
  `DoubleClickingABoolRow_TogglesIt_AtTwoSizes` (a real double-click on the name cell; the file carries
  `Enabled="false"`, and a second double-click resets the value to absent or writes `true`, per Judge).
- `FormPropertyGridTests.cs:563,566`: `IsCheckBox` → `IsComboBox`.
- **The Font parts (D-3 intrinsic rule):** `FormCompositeRowTests`: setting a Bold part's `StringValue` to
  `"True"` when it reads `"true"` raises no Edited and leaves the document identical, and `"False"` writes
  `style=` without Bold. Real view: `SelectingALabel_AndExpandingItsFont_ChangesNothing_AtTwoSizes` (select
  `lbl`, expand Font so the three part combos realise and bind, then the `.blform` text is byte-identical and no
  Edited fired).
Mutations: Bool not mapped to the combo; `ToDocument` keeps `True`; `Choices` unfiltered; `DoubleTapped` not
wired; **the intrinsic Bool compare made ordinal again** (the selection test must go red).
RE-CHECK: `FormPropertyGridTests.cs:692-697` (a frozen Bool row asserts `IsCheckBox` false; add
`IsComboBox` false, since a frozen Bool must not render the new combo either; `:699` already asserts it, so keep
it and confirm it holds for a frozen BOOL row specifically); `FormPropertyRowDefaultTests.cs:417` (the
PropertyChanged name list includes `BoolValue`/`StringValue`; a Bool edit must still raise `StringValue`, which the
combo now reads); `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` (`:261-293`; Bool rows now count as ComboBox),
`FormCompositeRowTests` (Bold/Italic/Underline via `BoolValue`, unchanged), `FormCanvasFontRenderTests.cs:254`,
`FormPropertyBatchAcceptanceTests.cs:94` (`BoolValue`, unchanged), `SettingsEditorWiringTests` (Settings untouched,
must stay green).

### Task 2 — Anchor/Dock as drop-down pop-ups (plan 4.4)
Files: `FormPropertyGridView.axaml` (`:189-246`: summary text + drop-down `Button` with a `Flyout` holding the
existing boxes, unchanged bindings), `FormPropertyRow.cs` (`AnchorSummary`, `DockSummary`).
Tests: `FormPropertyGridViewTests.EveryBindingInTheGridView_ResolvesAgainstTheTypeInScope` (automatic; it must see
inside the Flyout content, so check the walker descends into `Button.Flyout` → `Flyout` content; extend the walker
if it does not), `TheToolbarSearchSelectorAndEdgeToggles_CarryAnAutomationName` (the new drop-down buttons carry
names). Real view: `OpeningTheAnchorPopUp_AndClickingTheBottomEdge_WritesAnchor_AtTwoSizes` (open via a real click,
click the edge in the flyout's popup, the `.blform` gains `Anchor="Top,Bottom,Left"`), the same for Dock → `Fill`.
`FormAnchorDockPickerTests` (VM, unchanged; the summary text added).
Mutations: the summary not refreshed after an edge toggle (needs `OnPropertyChanged(AnchorSummary)` in `SetEdge`);
the flyout not bound.
⚠ The Font expander is ALSO `Classes="edge"` (`FormPropertyGridView.axaml:160`), and
`ExpandingAFont_…` finds `.Single(edge && visible)`. Do not give the new drop-down buttons the `edge` class.

### Task 3 — Reference editor (plan 4.7)
Files: new `BasicLang/Forms/FormReferences.cs` (`Candidates`, `IsAllowed`); `RegionWriter.cs` (`AppendRootRow`
calls `FormReferences.IsAllowed`; the private copy is deleted); `FormPropertyRow.cs` (a Reference row is a combo;
choices are passed in by the grid; the `(none)` mapping; the "(missing)" item); `FormPropertyGridViewModel.cs`
(`AddFormRows` passes `FormReferences.Candidates(form, definition)` for Reference rows).
Tests: `FormReferencesTests` (new): Buttons only, nested Buttons included (a Button in a Panel), document order,
components excluded. `FormRegionWriterTests`: the BL8034 cases still pass through the shared predicate.
`FormPropertyGridTests`: AcceptButton offers `(none), btnOk, btnCancel`; picking `btnOk` stores it; `(none)`
removes it; a dangling `AcceptButton="btnGone"` shows `btnGone (missing)` selected.
Real view: a fixture form with two Buttons; pick through the real ComboBox; the file gains `AcceptButton="btn"`.
Freshness: `FormPropertyGridTests.ACandidateAddedWhileTheFormStaysSelected_AppearsInTheList`: the Form is
selected and a Button is added through the document VM (paste, then undo/redo) without changing the selection.
The AcceptButton row's `Choices` then contains the new Id, and a `PropertyChanged(Choices)` was raised.
Mutations: `Candidates` ignores `ReferenceKinds`; **the `(none)` → Reset mapping removed, so `(none)` reaches
`Judge`** (the test "picking `(none)` removes the attribute and never writes the text `(none)`" must go red); the
missing value is not shown; `Choices` captured at construction (the freshness test must go red).

### Task 4 — Colour editor (plan 4.1)
Files: `VisualGameStudio.Shell.csproj` (+ `Avalonia.Controls.ColorPicker` 11.3.13); `FormPropertyGridView.axaml`
(the ColorPicker `StyleInclude` in its `UserControl.Styles`, the ONE home, D-1b); new
`Views/Controls/FormColorDropDown.axaml` (+ `.axaml.cs`; no theme include of its own); new `ViewModels/Designer/FormColorChoices.cs` (Web/System lists filtered by
`Accepts`; `ToDocumentText(Color)`; swatch resolution D-1e); `FormPropertyGridView.axaml` (row template: a
`DockPanel` with the drop-down left of `TypedValueEditor`, visible on `IsColor`); `FormPropertyRow.cs` (IsColor's
comment rewritten; `ApplyColor(string)` commits once). **`dotnet clean`** after the csproj and AXAML changes.
Tests:
- `FormColorChoicesTests` (new, VM): System on WinForms = all 33, on Web = the 8 with CSS (derived from
  `FormSystemColors.CssFor`, never a hand list). Web = `FormKnownColors.Names`. `#FF0000` at alpha 255 → `#FF0000`,
  at alpha 128 → `#80FF0000`, and each passes `Accepts`.
- Binding reflection: extend `FormPropertyGridViewTests`' walker to a `TestCaseSource` over EVERY new AXAML file
  (FormColorDropDown, FormFontDialog, FormItemsDialog), each scoped by its root `x:DataType`.
- Real view (`FormPropertyGridEditorRealViewTests`, new file): open the BackColor drop-down on `lbl` (real click);
  the `ColorView` has a template, i.e. visual descendants > 0 (the M2 trap, asserted, not assumed); pick `Red` on
  the Web tab, and the `.blform` carries `BackColor="Red"`, ONE Edited. Pick on the System tab of a WEB fixture,
  where `ActiveCaption` is absent and `Window` present. Custom: set `ColorView.Color` and close the flyout, then
  exactly one write, and moving the colour three times before closing still makes one. A frame captured with the
  flyout open differs from one with it closed (a distinctness check, since the control draws; MEMORY: a
  distinctness gate alone lets the WRONG thing draw, so also assert the Custom tab's ColorView `Color` equals the
  row's value on open).
- `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…`: the swatch button joins the swept kinds.
Mutations: the Custom tab commits on every `ColorChanged`; the System list unfiltered on the web; the include
removed from `FormPropertyGridView.axaml`'s Styles (the real-view visual-count assertion must go red); `#AARRGGBB` written for
alpha 255.

### Task 5 — Font dialog (plan 4.2)
Files: new `Views/Dialogs/FormFontDialog.axaml` (+ `.axaml.cs`), new `ViewModels/Designer/FormFontDialogViewModel.cs`
(families from a `Func<IEnumerable<string>>`, filtered by `FormFontValue.TryParse`, plus the current value plus
`Segoe UI`; size; styles; `Result` = canonical text or null); `FormPropertyRow.cs` (`HasEllipsis` for Font rows;
`InheritedValue`); `FormPropertyGridView.axaml` (a `…` button docked right of the editor, visible on
`HasEllipsis`); `FormPropertyGridView.axaml.cs` (opens the dialog with the owner `TopLevel.GetTopLevel(this) as
Window`; a `FontFamilies` seam defaulting to `FontManager.Current.SystemFonts`).
Tests:
- `FormFontDialogViewModelTests` (new): a family `"Font & Co"` from the seam is NOT offered; the current value's
  family is offered even when the seam lacks it; Bold+Italic → `"Arial, 10pt, style=Bold, Italic"`; Cancel →
  null; an absent ambient Font on a control inside a bold GroupBox starts Bold (`FormAmbient`).
- Real view: click `…` on `lbl`'s Font; the dialog is in `rig.Window.OwnedWindows` (M4); pick a family from the
  seam's fake list, toggle Bold, click OK; the `.blform` has the canonical font; ONE Edited; Cancel leaves the file
  byte-identical. Both sizes.
Mutations: the family filter removed; OK writes per toggle; Cancel writes; the start value ignores the inherited
font.

### Task 6 — Items storage (plan 4.6, data half). D-6 decided; write its ADR first
Files: new `BasicLang/Forms/FormItems.cs` (`IsCollection`, `Split`, `Join`, `FromLegacy`); `FormControlCatalog.cs`
(`SplitItems` → `FormItems.Split`; the IsItemCollection doc comment `:153-157`);
`BasicLang/Forms/Serialization/FormDocumentReader.cs`; `BasicLang/Forms/Serialization/FormDocumentWriter.cs`
(Apply + Create, D-6); `RegionWriter.cs` / `FormAssetEmitter.cs` (unchanged call to the new split; verify).
First: the ADR for D-6 in `docs/superpowers/decisions/` (decided, per the plan review).
Tests (`FormItemsStorageTests`, new):
- A legacy `Items="Alpha, Beta"` loads as two items and a no-op save is BYTE-IDENTICAL, on a `.blform` AND on a
  `.blwebform`.
- An edit to `Smith, John` + `Beta` writes two `<Item>` children, drops the attribute, and reads back
  identically.
- Items `A & B` and `x < y` write as `&amp;` / `&lt;` and read back exactly. They emit as
  `Items.Add("A & B")` and `<option>A &amp; B</option>`.
- Spaces are kept (`"  padded "`).
- Empty and whitespace-only lines are dropped.
- A document with `<Item/>` and `<Item>  </Item>` beside real items: a no-op save is BYTE-IDENTICAL (the compare is
  list-as-read, D-6), nothing blank is emitted, and an edit drops the blanks.
- An `<Item>` with `&#10;` is Degraded, preserved byte-for-byte, and nothing emitted. The raw `<Item>`s are in
  `UnknownChildren` and survive: Apply, Create (`FormDocumentWriter.Create` of the model), `FormControl` clone, a
  retarget pair, and a clipboard copy/paste. That is five assertions, one per path.
- Attribute plus children is Degraded (the attribute lands in `UnknownAttributes`; same five paths).
- Removing Items removes both forms.
- Create puts `<Item>` before `<Bind>`.
- FormClipboard round-trips a comma item.
- `RegionWriter` emits `cmb.Items.Add("Smith, John")`.
- The page has `<option>Smith, John</option>`.
- A web retarget pair carries the items.
Also a csc compile of a ComboBox whose sample Items holds a comma item (Integration, alongside
`WinFormsCatalogSweepTests`).
RE-CHECK: `FormAssetEmitterTests.cs:419,446,467,487,562` set `Properties["Items"] = "Alpha, Beta[, Gamma]"`
directly. That is the MODEL now, so rewrite them as `"Alpha\nBeta\nGamma"` (keep one test that loads a legacy XML
fixture through the reader). `FormPropertyGridTests.cs:291` loads XML (still passes).
`CatalogParity.TypeFits` `:130` unchanged.
Mutations: no-op save rewrites legacy; `FromLegacy` used on `<Item>` text (would split a comma item); the
Degraded item emitted; the writer leaves the stale attribute beside new children; **the writer compares element
counts instead of the list-as-read** (the `<Item/>` byte-identity test must go red).

### Task 7 — Items editor (plan 4.6, UI half)
Files: new `Views/Dialogs/FormItemsDialog.axaml` (+ `.axaml.cs`; a multi-line `TextBox`, `AcceptsReturn`), new
`ViewModels/Designer/FormItemsDialogViewModel.cs` (lines ⇄ `FormItems`; split on `\r\n|\n|\r`, per M4);
`FormPropertyRow.cs` (`IsCollectionEditor`; `CollectionSummary` = `(Collection)`; an empty result = Reset);
the grid view (`…` button; a read-only `(Collection)` text in place of the text box).
Tests: VM: CRLF and LF give the same lists; an empty result resets. Real view: the ComboBox row shows
`(Collection)`; `…` opens the dialog; type `Smith, John⏎Beta` through real key input; OK; the file has two
`<Item>`s; Cancel writes nothing.
Mutations: split on `\n` only (a `\r` left on each item); empty → `""` written instead of Reset.

### Task 8 — Image and Icon types + emission (plan 4.5a)
Files: `FormControlCatalog.cs`:
- `FormPropertyType.Image` / `Icon`; `Accepts`; `IsRefusedOn` + `RefusalReason` (D-5a); `WinFormsLiteral` (D-5b);
  `Canonical` (slashes).
- PictureBox `Image` → `Image` type, its `WinFormsFactory` removed.
- FormRoot `Icon` row (Both targets, `Category: WindowStyle`, the snapshot's description, `Default: null`).

Other files:
- new `FormAssetPaths.cs` (normalise, IsUrl, IsRooted, contained, ProjectRootFor).
- `FormAssetEmitter.cs` (img `src` via the tier check + URL encoding; `<link rel="icon">`).
- `RegionWriter.cs` (Icon among root rows: verify it lands before the controls and that BL8009 covers refusals).
  `Literal`'s UNREACHABLE arm (`:1317-1318`, the throw for "a Degraded value reached the writer") gains `Image`
  and `Icon`, so an unparseable value can never fall through to `StringLiteral` (`:1322`) and be emitted as a
  quoted string. That would be CS0029 at csc for a `System.Drawing.Image` property, with BasicLang silent.
- `FormPropertyRow.cs`: **`IsTextBox` gains `Image` and `Icon`** (`:331-334`). Otherwise those rows have NO
  editor between this task and Task 10's picker; the typed path stays the parent's own editor after Task 10 too.
- `CatalogParity.cs` (`TypeFits` arms `Image` → `"Image"`, `Icon` → `"Icon"`; the `:132` factory arm deleted).
- `VisualGameStudio.Tests/Compiler/PixelLayout/WinFormsReferenceHarness.cs`: `Measure` (`:135-203`) gains an
  `IReadOnlyDictionary<string, byte[]>? assets` parameter. Each asset is written into the `reference-app`
  directory and declared in the generated `app.csproj` (`:169-180`) as
  `<None Include="…" CopyToOutputDirectory="PreserveNewest" />`. The file then lands beside `ReferenceDriver.exe`,
  wherever `dotnet build` puts it (the exe directory is known only after the build, `:187`).
  `PixelPageLayoutTests.cs:101-105` passes `pic.png` through it instead of writing into the working directory.

Tests:
- `FormPropertyDefTests` per type: each Accepts/refuse rule and reason, on each target.
- `FormPropertyGridTests`: a PictureBox's Image row and the Form's Icon row are `IsTextBox` (catalog-driven over
  every Image/Icon-typed row), and typing `Resources/a.png` writes it.
- Parity (automatic, Integration): `WinFormsCatalogParityTests` for PictureBox and FormRoot.
- `WinFormsCatalogSweepTests.SampleValue` arms `Image` → `"Resources/sample.png"`, `Icon` → `"Resources/app.ico"`;
  the root sweep compiles `Me.Icon = New Icon(…)`.
- `FormRetargetTests.Sample` / `FormRootRetargetTests.Sample` arms.
- `FormAssetEmitterTests`: `src="Resources/my%20logo.png"`, a URL kept, an absolute path not emitted on the web,
  `<link rel="icon" href="Resources/app.ico">` after the title.
- `FormRegionWriterTests`: the exact fully qualified shape
  (`System.Drawing.Image.FromFile(System.IO.Path.Combine(System.AppContext.BaseDirectory, "…"))`,
  `New System.Drawing.Icon(…)`), including under a user `Using` that would make a bare `Image` ambiguous;
  `Icon="logo.png"` on WinForms → BL8009 "requires .ico".
- `FormPropertyGridDisplayTests.TheFormsRows_ComeFromFormRoot` (the expected set gains Icon).

RE-CHECK:
- `FormAssetEmitterTests.cs:904-906` (`Image="logo.png"`, still Canon).
- `PixelLayoutFixtures.cs:204-207` (`Image="pic.png"`) and **`PixelPageLayoutTests.cs:101-105`**: through the
  harness's new `assets` parameter (above), or the Integration Picture rows go blank.
- **`FormRootTests.EveryFormRootRow_HasStorage_OnBothTargets`** (`FormRootTests.cs:37-49`): its sample dictionary
  has no `Icon` key. Add `[FormPropertyType.Icon] = "Resources/app.ico"` (and `Image` for symmetry), or the lookup
  throws for the new row.
- **`FormWebVocabularyTests.Sample`** (`FormWebVocabularyTests.cs:18-31`): its `_ => "sample"` default meets the
  Image row. `"sample"` is a relative path and Canon on both targets today, but an extension rule must not refuse
  it on the web. Add explicit `Image` → `"Resources/sample.png"` and `Icon` → `"Resources/app.ico"` arms so the
  D2 direction test samples a real value, never a refused one.
- `FormWebVocabularyTests` D2 direction: Icon is now a BOTH-targets row, so it must reach the page (the
  `<link rel="icon">`). The WinForms-only byte-identity check must not include it.

Mutations: Icon accepts `.png` on WinForms; **`System.AppContext.BaseDirectory` dropped** (bare relative path);
an unqualified `Image.FromFile` (the `Using` test must go red); URL accepted on WinForms; `..` not Degraded; the
web src not encoded; `Image` removed from `IsTextBox`; `Image` removed from `Literal`'s unreachable arm (a
Degraded-value-forced test must throw, never emit a quoted string).

### Task 9 — The build copy + BL8036, BOTH entry points (plan 4.5b)
Files: new `BasicLang/Forms/FormAssetCopy.cs` (D-5c); `FormDocumentLoader.cs` (`Load(paths, target, consequence)`,
`LoadWebForms` kept, its text unchanged; test: a refused `.blform` on the C# route warns "its images and icons were
not copied into the output", never "was not turned into a page"); `DesignDiagnostic.cs` (`AssetNotCopied = "BL8036"` + band table); `BasicLang/Program.cs`
(C# and JS branches call `FormAssetCopy.Copy`); `VisualGameStudio.ProjectSystem/Services/BuildService.cs` (same,
plus `DiagnosticItem`s).
Tests:
- `FormAssetCopyTests` (new, temp dirs, fast): copies with the sub-path preserved; a missing file → BL8036 naming
  `LoginForm.pic.Image` and the absolute path; `../x.png` → BL8036 "outside the project", nothing written outside
  `outputDir`; an absolute path → not copied + BL8036; a URL → nothing; the same file referenced twice is copied
  once; a rebuild over a mapped copy goes through temp + rename.
- **CLI** (Integration, extend `FormBuildEmissionTests`, real `BasicLang.exe build`): a web project with a
  PictureBox `Image="Resources/logo.png"` → `bin/Debug/net8.0/Resources/logo.png` exists; delete the source →
  stderr has `Warning: BL8036:` and exit 0. A WinForms project (Windows-only, `[Platform(Include = "Win")]` +
  the dotnet/WindowsDesktop gate) → the png sits beside `App.exe`.
- **IDE** (Integration, extend `Services/JavaScriptProjectBuildTests` + a WinForms twin in
  `BuildServicePipelineTests`): `BuildService.BuildProjectAsync` → the file is in `config.OutputPath`, and
  `result.Diagnostics` holds a `BL8036` Warning with the form's path.
Mutations: the copy call removed from the IDE route only (the IDE test must go red while the CLI stays green; this
is the "test both entry points" kill); containment check removed; the warning not raised for a missing file.

### Task 10 — The image picker + copy into Resources (plan 4.5c)
Files: new `ViewModels/Designer/FormAssetImport.cs` (relative-or-copy decision, collision naming, uses
`FormAssetPaths.ProjectRootFor`); `FormPropertyRow.cs` (`HasEllipsis` for Image/Icon); the grid view code-behind
(`PickFile`, `ConfirmCopy` seams; the `StorageProvider` default with image filters, `.ico` for Icon).
Tests: VM: inside the project → relative with forward slashes; a form in `Forms/` with the image in `Images/` →
`Images/x.png`; outside + accept → copied to `Resources/x.png`; identical existing → reused; different existing →
`x (2).png`; web + decline → nothing written. Real view: fake seams; click `…` on a PictureBox's Image; the file
gains `Image="Resources/x.png"` and the copy exists in the temp project.
Mutations: the collision overwrites; backslashes stored; decline on the web stores the absolute path.

### Task 11 — RUN, not compile (plan 4.5d; spec §8 "the image copy on both targets")
New Integration fixture `FormImageAcceptanceTests`. One form with a PictureBox (`Image="Resources/logo.png"`, a
real 3×2 PNG written by the test) and a Form `Icon="Resources/app.ico"` (a real 16×16 .ico). Built through the real
designer VM + grid rows, compiled by the real CLI (`build`), and RUN:
- WinForms: the program prints `pic.Image.Width,Height` and `Me.Icon.Width` from the live form → `3,2` and
  `16`. It is RUN in all THREE shapes, each from a **working directory that is NOT the output folder** (the D-5b
  kill, M8). (a) `<App>.exe` directly, the IDE's `ExecutablePath` shape. (b) `dotnet "<App>.dll"`. (c) **`BasicLang.exe run
  <App>.blproj`**, the real CLI route (`HandleRunCommand`, `Program.cs:1266-1295`: prefers the `.dll`, starts
  `dotnet "<dll>"` with no `WorkingDirectory`, inherits the harness's cwd, and pipes stdout back). All three must
  print `3,2 16`. Windows-only, SKIP elsewhere.
- Web: the page under node (the handler still fires, the D7 dispatch alive); `logo.png` and `app.ico` exist under
  the site folder at the paths the HTML names. With Edge installed, `naturalWidth` of the `<img>` read back by a
  new `EdgeStep.ImageSize` probe in `EdgeLayoutHarness` (`EdgeLayoutHarness.cs:23-46`; no image probe exists today)
  → `3`. Edge/node missing → SKIP, never fail.
Mutations: `System.AppContext.BaseDirectory` dropped (every one of the three runs must print a
`FileNotFoundException`, not `3,2`; M8 measured exactly that); the web copy skipped.

### Task 12 — Gate (§5), mutation ledger, IDE drop, click-through
One commit for any records. Mutations killed/equivalent are listed per task in the execution notes, as in the
slice-3 §5a.

## 5. Gate for the slice
- **Fast subset** (`--filter "TestCategory!=Integration"`, Release, both streams captured): compare the **sorted
  failure NAMES** with Task 0's, base SHA stated. The only acceptable failures are the known machine rows:
  `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`, `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
  `Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`
  (intermittent), `SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`,
  `ReadingAnMvidTakesNoLockOnTheFile`. A count alone proves nothing. A passing test prints nothing, so re-run named
  rows with `--no-build --filter`.
- **Named Integration set**, 0 skipped on Windows: `WinFormsCatalogParityTests`, `WinFormsCatalogSweepTests`,
  `FormDesignerAcceptanceTests`, `FormMenuAcceptanceTests`, `FormComponentAcceptanceTests`, `FormBuildEmissionTests`,
  `FormAnchorEmissionTests`, `FormPropertyBatchAcceptanceTests`, `PixelPageLayoutTests` (the Picture rows, Task 8
  re-check), `FormRetargetPairTests`, `WebMainStartupTests`, `JavaScriptProjectBuildTests`,
  `BuildServicePipelineTests`, the new `FormImageAcceptanceTests`. Any new failure: re-run it alone, then A/B on a
  `git worktree add --detach` of `origin/master` (never `git merge-tree`).
- `dotnet clean` after the csproj/AXAML changes in Tasks 2, 4, 5, 7 and 10, before the gate build.
- **Merge check**: a trial merge of `origin/master` in a detached worktree. Expect conflicts in `DesignDiagnostic.cs`
  (the band table; BL8036 may be claimed by piece 2) and `FormControlCatalog.cs` (piece 2 adds rows).
- **IDE drop** (Windows): `robocopy VisualGameStudio.Shell\bin\Release\net8.0 IDE /E`. Never `/MIR`. Keep
  `IDE\lib\js\dom-core.bli`. Run the IDE from `VisualGameStudio.Shell\bin\Release\net8.0\VisualGameStudio.exe` for
  the click-through.
- **Owner click-through**:
  1. On a WinForms form, Button.BackColor: the drop-down shows Custom/Web/System. Pick System→Control, Web→Red,
     and a Custom colour; each is one undo step.
  2. On a web form the System tab shows only 8 entries.
  3. Label Font `…`: pick a family, size and Bold; the preview follows; OK, then Ctrl+Z once restores it.
  4. Enabled shows True/False; double-click toggles it.
  5. A Cursor on a web form offers no UpArrow.
  6. Anchor and Dock open as pop-ups.
  7. Form AcceptButton lists the Buttons and `(none)`.
  8. ComboBox Items shows `(Collection)`; `…` opens the editor; enter `Smith, John` and `Beta`, F5, and the
     drop-down holds two items on WinForms AND on the web.
  9. PictureBox Image `…`: pick a png outside the project, accept the copy into Resources, F5, and the picture
     shows on both targets.
  10. Form Icon `.ico`: F5, and the title bar shows it; on the web, the tab icon.
  11. Delete the png and build: the Error List shows BL8036 and the build still succeeds.

## 6. Traps that apply (restated where slice 4 touches them)
- The catalog is the single source of truth: every list an editor offers is filtered through `Accepts`. There are
  no hand lists.
- A WinForms row is unfalsifiable without csc: the Image/Icon types go through the sweep, and Image/Icon also go
  through a RUN.
- Fan-in: the Font dialog writes ONE value. Never `With`.
- ONE selection store. The editors only write values; they never select.
- `[RelayCommand]` sits directly above its method. `[ObservableProperty]` is one per field.
- No compiled bindings: every new AXAML file joins the reflection walker.
- Never change a bound property inside `Render`.
- Edit/Write can store a backslash-u escape as the raw character, so build such characters from code points (the
  drop-down glyph, U+25BE).
- A mutant survives a revert until you REBUILD.
- Never round-trip repo files through `Get-Content`/`Set-Content`.
- A subagent told to "add tests" edits existing files and Writes only NEW ones.

## 7. Tests to re-check (consolidated)

| Test | Why | Task |
|---|---|---|
| `FormPropertyGridRealViewTests.AnAbsentEnabled_ShowsOnAndGreyed_AndAPresentText_IsBold` (`:685-703`) | finds a ToggleSwitch | 1 (rewrite) |
| `FormPropertyGridRealViewTests.ExpandingAFont_AndSwitchingBold_…` (`:594-626`, `:613`) | Bold part's ToggleSwitch | 1 (rewrite) |
| `FormPropertyGridTests.cs:563,566` | `IsCheckBox` true for Bool | 1 |
| `FormPropertyGridTests.cs:565` | `IsColor` (the flag gains a caller in Task 4) | 4 |
| `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` (`:261-293`) | new editor kinds (swatch, `…`) must join the sweep | 1, 4, 5, 7, 10 |
| `FormPropertyGridViewTests.EveryBindingInTheGridView_…` / `TheToolbar…AutomationName` | Flyout content and new AXAML files | 2, 4, 5, 7 |
| `FormPropertyGridViewTests.TheDocumentView_NoLongerCarriesTheGridsOwnBindings` | must stay green | every task |
| `FormAssetEmitterTests.cs:419,446,467,487,562` | comma Items set directly on the MODEL | 6 |
| `FormPropertyGridDisplayTests.TheFormsRows_ComeFromFormRoot` | the Form gains Icon | 8 |
| `WinFormsCatalogSweepTests.SampleValue` (`:612`), `FormRetargetTests.Sample` (`:1823`), `FormRootRetargetTests.Sample` (`:16`) | arms for Image/Icon | 8 |
| `CatalogParity.TypeFits` (`:128-153`) | the factory arm `:132` replaced by type arms | 8 |
| `PixelPageLayoutTests.cs:101-105` + `PixelLayoutFixtures.cs:204-207` + `WinFormsReferenceHarness.Measure` (`:135-203`) | pic.png placed in the working dir; now passed as a harness asset copied beside the exe | 8 |
| `FormRootTests.EveryFormRootRow_HasStorage_OnBothTargets` (`:37-49`) | sample dictionary lacks Icon | 8 |
| `FormWebVocabularyTests.Sample` (`:18-31`) | default `"sample"` meets Image/Icon; Icon now reaches the page | 8 |
| `RegionWriter.Literal` unreachable arm (`:1317-1318`) | must include Image/Icon | 8 |
| `FormPropertyGridTests.cs:692-697` | frozen Bool: assert `IsComboBox` false too | 1 |
| `FormPropertyRowDefaultTests.cs:417` | PropertyChanged list; a Bool edit must still raise `StringValue` | 1 |
| `WinFormsCatalogParityTests.TheSnapshot_Covers…` | no new KIND in slice 4 (Icon is a row) | none |

## 8. Execution notes

### Task 0 — Baseline (base `e0fc5915` = `0f6d2ced` + this document; Windows, Release)
`dotnet build VisualGameStudio.Tests` green. Fast subset (`TestCategory!=Integration`, both streams captured):
**Total 10629 · Passed 10604 · Failed 6 · Skipped 19.** Sorted failure names — all known machine rows (§5):
`Emit_ReplacesAScriptThatAnotherHandleHasMapped`, `Emit_ReplacesAnImportedModuleThatAnotherHandleHasMapped`,
`Emit_ReplacingAnImportedModule_LeavesNoTempFileBehind`, `EveryTextRoute_UsesTheFormatter_NeverToStringOrABareCout`,
`SearchSnippets_EmptyQuery_ReturnsAll`, `SearchSnippets_WhitespaceQuery_ReturnsAll`. (`ReadingAnMvidTakesNoLockOnTheFile`
passed on this run.)

### Task 1 — Bool drop-down + catalog-filtered choices
Done as written. Choices taken under the delegation:
- **One spelling helper**: `FormPropertyDef.BoolWord` (BasicLang), used by `Canonical`, `ToDocument` and the grid's
  intrinsic arm (`FormPropertyRow.IsSameIntrinsicValue` / `IntrinsicDocumentText`). So D-3's "ONE helper used by
  both arms" lives in the catalog, not in the Shell.
- **`IsCheckBox` is the constant `false`** for the grid's row (not "Bool and something"): the switch never renders there.
- **Choices are filtered once per row** (cached; the target and definition are fixed for a row's life). Task 3 makes the
  Reference row's list a live `Func`, which is a separate arm.
- **Double-click** is one `DoubleTapped` handler on the list. It ignores a double-click inside the row's
  `TypedValueEditor` (that gesture belongs to the drop-down). It toggles on the name cell or the row's chrome.
- From an explicit `Enabled="false"`, the first double-click writes `true` and the second writes `false` (Judge gives a
  Write both times, never a Reset). The plan's "resets to absent or writes true" was read as "whatever Judge says". The
  test pins what Judge says.

Tests: red first (stub `ToggleBool`; 169 red in the Task 1 filter, each for the expected reason), then green.
`FormPropertyGridTests` (`EveryBoolRow_IsADropDownOfExactlyTrueAndFalse` over every Bool row of every kind plus FormRoot,
on each target, 159 cases; `PickingFalse_…`; `AWebCursorRow_…`; `ToggleBool_…`; `EachPropertyTypeGetsItsOwnEditor`
rewritten; the frozen-Bool test asserts its type). `FormPropertyRowDefaultTests` (`AnAbsentEnabled_ShowsTrue_AndPickingTrue_
IsANoOp`; the notification test gains a combo write). `FormCompositeRowTests` (`ABoldPart_PushedItsOwnShownItem_…_OnAnAbsentFont`,
`ABoldPart_TrueOverTrue_…`). Real view (`DoubleClickingABoolRow_TogglesIt_AtTwoSizes`,
`SelectingALabel_AndExpandingItsFont_ChangesNothing_AtTwoSizes`, the two ToggleSwitch tests rewritten).
`FormDesignerLayoutRealViewTests` also asserts that no ToggleSwitch is swept (ComboBox count 4→6).

⚠ **Deviation (the plan's own case is not discriminating).** "`True` over a part reading `true`" passes even with the
ordinal mutant: the part composes the same font, and the PARENT's Judge then NoOps. The discriminating case is an ABSENT
Font, where `False` over `false` composed the inherited font and the parent stored it. Both tests are kept. The absent one
is the kill.

⚠ **Finding, then FIXED in the review follow-up commit: a drop-down opened inside the grid could scroll the grid and close
itself.** The second drop-down opened in a window (or the first one after a Font expand) closed as it opened. The
pre-existing TextAlign Enum combo did the same. The reviewer measured the root cause:
1. Opening a ComboBox runs `PopupOpened → TryFocusSelectedItem → ComboBoxItem.BringIntoView()`.
2. The request bubbles out of the popup to the property list's `ScrollContentPresenter.BringDescendantIntoView`.
3. Headless popups use an `OverlayPopupHost` in the SAME window, so `TransformToVisual` succeeds and the list scrolls
   (27→0, 209→0).
4. `VirtualizingStackPanel` then runs `RecycleAllElements`, which detaches the combo, and the combo closes.

Real Win32 popups are separate roots, so the IDE is likely unaffected.

Fix (coordinator decision): `FormPropertyGridView` marks a bring-into-view request Handled when its target is inside a
popup (an `OverlayPopupHost`/`PopupRoot` ancestor, or a different top level).
- ⚠ The guard sits on each ROW CONTAINER, not on the ListBox. The event is bubble-only, and the presenter lives inside the
  ListBox's template. A ListBox-level guard was measured to change nothing.
- `PickInCombo` and the Task 1 real-view tests now pick with REAL clicks: open the drop-down, then click the item in its
  popup. The new `TwoDropDownsInOneWindow_BothPickThroughTheirRealPopups_AtTwoSizes` covers Enabled, then TextAlign.
- Red before the guard: that test, plus `ExpandingAFont_…` (its first pick follows an expand). Mutation: guard removed →
  both red again.

RE-CHECK: `FormPropertyGridTests` frozen Bool (`IsComboBox` false holds for the frozen BOOL row, type asserted);
`FormPropertyRowDefaultTests` PropertyChanged list (a combo write raises `StringValue`); `EveryRowsEditor_Fits…` (Bools now
ComboBoxes, all fit); `FormCompositeRowTests` (via `BoolValue`), `FormCanvasFontRenderTests`, `FormPropertyBatchAcceptanceTests`,
`SettingsEditorWiringTests`, `FormPropertyGridViewTests` — all green (774/774 in the filter).

Mutations (each applied with Edit and REBUILT):
| Mutant | Result |
|---|---|
| Bool not mapped to the combo | killed (164: the BoolRow cases, editor test, 4 real-view tests) |
| `ToDocument` keeps `True` | killed (`PickingFalse_…`) |
| `Choices` unfiltered | killed (`AWebCursorRow_…`) |
| `DoubleTapped` not wired | killed (`DoubleClickingABoolRow_…`) |
| intrinsic Bool compare ordinal again | killed by the VM test `ABoldPart_PushedItsOwnShownItem_…`. ⚠ The real-view `SelectingALabel_…` stays GREEN under it: measured, the headless ComboBox does not push its item back on bind. That test guards the end-to-end no-write only, and says so |

Post-task fast subset (base `a058f301` + Task 1): **Total 10796 · Passed 10771 · Failed 6 · Skipped 19.** The failure
names are identical to Task 0's six. +167 tests.

### Task 2 — Anchor/Dock as drop-down pop-ups (base `124355cd`)
Done as written: each row is a one-line summary (`FormPropertyRow.AnchorSummary` — `Top, Left`, flag order, `None` for no
edges, read through `FormAnchor.Parse`; `DockSummary` — the region in `DockStyle`'s spelling, `None` when unset) plus a
drop-down `Button` (`AnchorDropDown` / `DockDropDown`, automation-named, class `dropDown`, never `edge`) whose
`Button.Flyout` holds Task 26's box, moved with its bindings unchanged. The reflection walker already descends property
elements, so the Flyout content is checked with no walker change (confirmed: `EveryBindingInTheGridView_…` green, and it
counts the moved bindings).

⚠ **Deviation (measured): the drop-down sits LEFT of the summary, not at the cell's right edge as in VS.** Docked right,
the button lay under the property list's Fluent overlay scrollbar, and a real click at its centre hit
`PART_LineDownButton` (800x560). The colour swatch (D-1a) is already planned on the left, so the grid's editor buttons
now all sit left. `OpenAndClick` asserts that a click at the button's centre reaches it, so a later layout cannot move it
back under the scrollbar silently.
- The Flyout's content inherits the row as its DataContext (asserted in the real-view helper, not assumed).
- Dock's pop-up stays open after a pick, as Anchor's does. Closing on a Dock pick (VS does) needs code-behind and is a
  follow-up. → DONE in the review follow-ups below.

Tests: red first with stub summaries (13 red: the 9 summary/refresh VM cases, the automation test, the new
`TheAnchorAndDockBoxes_LiveInsideTheirDropDownsFlyouts`, both real-view tests — each for the expected reason). Then green.
- `FormAnchorDockPickerTests`: `TheAnchorSummary_ReadsTheEdgesAsTheOneParserDoes` ×4, `TogglingAnEdge_RefreshesTheAnchorSummary`,
  `TheDockSummary_NamesTheRegion` ×3, `ChoosingARegion_RefreshesTheDockSummary`.
- `FormPropertyGridViewTests`: the automation test names both drop-downs; `TheAnchorAndDockBoxes_LiveInsideTheirDropDownsFlyouts`.
- Real view: `OpeningTheAnchorPopUp_AndClickingTheBottomEdge_WritesAnchor_AtTwoSizes`, `OpeningTheDockPopUp_AndClickingFill_WritesDock_AtTwoSizes`.
  Each also asserts the row is one line (< 40px). ⚠ The summary lookup excludes button captions: the old inline Dock
  `None` BUTTON shows the same word as the summary, and the first red run found the Dock summary "present" through it.

RE-CHECK: `EveryBindingInTheGridView_…`, `TheToolbar…AutomationName`, `TheDocumentView_NoLongerCarriesTheGridsOwnBindings`,
`FormAnchorDockPickerTests`, `EveryRowsEditor_Fits…` — all green (370/370 over FormPropertyGrid*, FormAnchorDockPicker*,
FormDesignerLayoutRealView*, FormCompositeRow*, FormPropertyRow*).

Mutations (each applied with Edit and REBUILT; AXAML ones after `dotnet clean`):
| Mutant | Result |
|---|---|
| `AnchorSummary` not raised in `SetEdge` | killed (`TogglingAnEdge_RefreshesTheAnchorSummary`, the Anchor real-view test) |
| `DockSummary` not raised in `SetDock` | killed (`ChoosingARegion_…`, the Dock real-view test) |
| Flyout not bound (`Button.Flyout` → `FlyoutBase.AttachedFlyout`) | killed (Anchor real-view test, `TheAnchorAndDockBoxes_…`) |
| Anchor drop-down docked Right again | killed (the hit assertion: "hit ContentPresenter") |

### Task 3 — Reference editor (base Task 2's commit)
Done as written: new `BasicLang/Forms/FormReferences.cs` (`Candidates` = the Ids of `AllControls()` whose kind is in
`ReferenceKinds`, document order; `IsAllowed` = ordinal membership of that list). `RegionWriter.AppendRootRow` asks
`IsAllowed`; its private `NamesAnAllowedControl` is deleted. The grid passes `referenceCandidates: () =>
FormReferences.Candidates(form, definition)` to `ForStoredValue`, so a Reference row is a combo (`IsComboBox`; it left
`IsTextBox`) whose `Choices` are `(none)`, the candidates, and a dangling stored Id marked `btnGone (missing)`.
`(none)` is mapped to Reset in `FormPropertyRow.CommitReference` before `Commit`/Judge.

Choices taken under the delegation:
- **Components are never candidates.** The old check used `FindById`, which includes the tray. No row's kinds name a
  component, so nothing changes in practice; `FormReferencesTests` pins the rule with a Timer-kinded probe row.
- **Freshness hook = the document view model's model revision** (`OnDesignModelRevisionChanged` →
  `PropertyGrid.RefreshReferenceChoices()`), the same revision the tray follows. Every designer write-back and every
  reload bumps it.
- **Picking a `(missing)` item puts that Id back** (the mark is stripped; Judge no-ops it when it is already stored).

⛔ **Finding (measured through the real ComboBox): a NEW `Choices` list during a pick undoes the pick.** The commit runs
INSIDE the combo's SelectedItem push and raises `Choices` (the row's own value-changed, then the revision hook). A new
list instance swaps the combo's ItemsSource mid-push, and the combo pushed its PREVIOUS item back. That was `(none)`, i.e.
Reset, so one click wrote `btn` and removed it (two Edited). Fix: `ReferenceChoices` returns the SAME instance while
its items are unchanged, and a missing Id stays listed for the row's life, so picking a real Button over a dangling Id
does not shrink the list mid-push either. Both are mutation-pinned by the real-view tests below.

⚠ **Deviation: the freshness test cannot add the Button "by paste, then undo/redo, without changing the selection".**
Measured: every shipping path that adds a control moves the selection (paste → `Selection.SetRange(added)`, drop →
`SelectInDesigner`) or re-parses and rebuilds the rows (undo/redo `AdoptDocumentText`, a Code-view edit). So no IDE path
reaches "candidate added, same row" today, and the hook is defensive. Both freshness tests add the Button to the model
directly. The VM test then calls `RefreshReferenceChoices`. The real-view test makes a real grid edit of the Form's Text,
which goes through the document view model's write-back and revision.

Tests: red first with stubs (`FormReferences` returning nothing, an empty `RefreshReferenceChoices`): 10 red, each for
the expected reason. (`PickingAButton_StoresItsId` and `PickingNone_OnAnAbsentRow_IsANoOp` were green on the old text
box too: a typed Id stored, and `(none)` was refused, which is no edit. Neither is a kill on its own.)
- `FormReferencesTests` (new): Buttons only, nested included, document order; components never; `IsAllowed` ≡
  membership, ordinal; a row with no kinds allows nothing.
- `FormRootTests.TheReferenceCheck_IsTheSharedPredicate` ×2: the BL8034 tests live in `FormRootTests`, not
  `FormRegionWriterTests` as the plan says. The existing `ADanglingReference_IsWarned_AndNotEmitted` cases pass unchanged.
- `FormPropertyGridTests`: `AcceptButton_IsADropDownOfNoneThenTheButtons_InDocumentOrder`, `PickingAButton_StoresItsId`,
  `PickingNone_RemovesTheAttribute_AndNeverWritesTheTextNone`, `PickingNone_OnAnAbsentRow_IsANoOp`,
  `ADanglingReference_ShowsAsMissing_Selected_AndPushingItBackChangesNothing` (plus the same-instance and put-back
  steps), `ACandidateAddedWhileTheFormStaysSelected_AppearsInTheList`.
- Real view: `PickingAButtonInTheAcceptButtonDropDown_WritesItIntoTheFile_AtTwoSizes` (ONE Edited),
  `ADanglingAcceptButton_ShowsMissing_AndPickingAButtonReplacesIt_AtTwoSizes`,
  `ACandidateAddedWhileTheFormStaysSelected_AppearsInTheList` (the document-view-model half).

RE-CHECK: 664/664 green over FormReferences*, FormRootTests, FormPropertyGrid*, FormRegionWriter*, FormPropertyRow*,
FormValueType*, FormDesignerLayoutRealView*, FormAnchorDockPicker*, FormCompositeRow*, FormTrayView*, and the
Integration `FormPropertyBatchAcceptanceTests` (it sets AcceptButton through the grid; 0 skipped).

Mutations (each applied with Edit and REBUILT):
| Mutant | Result |
|---|---|
| `Candidates` ignores `ReferenceKinds` | killed (8: FormReferences ×5, the grid's list and dangling tests, the BL8034 Label case, the real-view pick) |
| `(none)` → Reset mapping removed (`(none)` reaches Judge) | killed (`PickingNone_RemovesTheAttribute_AndNeverWritesTheTextNone`) |
| the missing value not shown (dangling displays `(none)`) | killed (the VM and real-view dangling tests) |
| `Choices` captured at construction | killed (both `ACandidateAdded…` tests) |
| the document view model's revision hook removed | killed (the real-view `ACandidateAdded…`) |
| a new list instance on every read | killed (both real-view picks and the VM same-instance step). This is the finding above, confirmed |
| the missing entry not sticky | killed (the VM and real-view dangling tests) |

Post-task fast subset (base `124355cd` + Tasks 2–3, both streams captured): **Total 10828 · Passed 10803 · Failed 6 ·
Skipped 19.** The failure names are identical to Task 0's six (`ReadingAnMvidTakesNoLockOnTheFile` passed again). +32
tests over Task 1's run. Every `FormPropertyGrid*` fixture: 299/299 green.

### Review follow-ups to Tasks 2–3 (base `e33239f1`; one commit)
Decisions under the delegation (VS behaviour where in doubt):
1. **Keyboard access to Anchor/Dock: no fix needed for the pop-up mechanics** — measured: Enter on the focused drop-down
   opens the flyout, focus moves into it, Space flips the focused toggle, Esc closes it and the flyout hands focus back to
   its button. `TheAnchorPopUp_WorksFromTheKeyboard_EnterSpaceEsc_AtTwoSizes` was GREEN on the red run: it pins Avalonia's
   behaviour and is not a kill of anything written here.
2. **Dock closes on a pick; Anchor stays open** (VS). Every Dock choice (five regions + None) carries
   `Click="OnDockRegionPicked"`, which POSTS `Flyout.Hide()` — measured as a mutant: hiding synchronously inside Click runs
   before the button's Command and nothing is written. An explicit `Focus()` on the drop-down after Hide was an EQUIVALENT
   mutant (the flyout returns focus to its target itself) and was removed.
3. Dock's `None` has `AutomationProperties.Name="Not docked"` = its tooltip; the automation test counts it (10 choices).
4. **The reference follows its object on DELETE and CUT**: new `FormDocument.RemoveControl` (the one model path; `DeleteControl`
   and `CutControls` both call it) removes the control, then `FormReferences.ForgetRemoved` drops every FormRoot Reference
   row naming it or anything inside it (a Panel holding the AcceptButton). A reference that was already dangling is left
   for BL8034. One `WriteDesignerEditBack` → one undo restores the Button and the references (byte-identical, asserted).
   ⚠ **Rename is RECORDED, not fixed** (followups 35): no route renames an Id (Name is frozen; F2 edits a strip item's Text;
   the clipboard renames only pasted copies), so a rename helper would have no caller.
5. A hand-written `AcceptButton="x (missing)"` / `"btnOk (missing)"` / `"(none)"` is already Degraded (not a legal Id):
   frozen, shown verbatim, never mapped by the display marks, byte-identical on save, not emitted. Pinned by
   `AHandWrittenDisplayMark_IsDegraded_NeverMappedByTheDropDown` ×3 (green on the red run — a pin, not a kill).
6. **`UpdateTextFromEditor` now calls `SyncDesignerPanels`** (unless the designer is writing its own edit back), as the
   `OnTextChanged` Code-view route does, so the grid's rows read the new parse. `ReferenceChoices` asks the candidates once
   per read (the display takes the same list).

Tests: red first (7 red, each for the expected reason: `RemovingAControl_DropsTheReferences…`, both real-view delete/cut
tests, `AnEditorEdit_ThatAddsAButton_ReachesTheGridsRows`, the Dock real-view test's new "closes" step, the Dock keyboard
test's close/focus steps, the automation test). Then green: 22/22; every non-Integration `Form*`/`CodeEditor*` test
2851/2853 (1 skipped; the one failure is the known `EveryTextRoute_…`).

Mutations (Edit + REBUILD):
| Mutant | Result |
|---|---|
| `ForgetRemoved` forgets nothing (the red-step stub) | killed (FormReferences, both real-view delete/cut) |
| only the removed control's own Id forgotten, not its subtree | killed (`RemovingAControl_DropsTheReferences…`) |
| `removed.Contains` dropped (forget every dangling reference) | killed (`…LeavesAnUnrelatedDanglingReference…`) |
| `FindById == null` guard dropped | EQUIVALENT while Ids are unique (kept as the defensive half) |
| Dock pick does not hide | killed (both Dock real-view tests) |
| Dock hide synchronous, not posted | killed (both: nothing written) |
| explicit focus-return after Hide | EQUIVALENT → removed |
| `SyncDesignerPanels` absent from `UpdateTextFromEditor` | killed (red step) |
| None's automation name absent | killed (red step) |

### Task 4 — Colour editor (base `47c31434`)
Done as written: `Avalonia.Controls.ColorPicker` 11.3.13 in the Shell csproj; the theme `StyleInclude` in
`FormPropertyGridView.axaml`'s Styles ONLY; new `Views/Controls/FormColorDropDown.axaml(.cs)` (swatch button + glyph, a
`Flyout` with Custom `ColorView` + OK, Web, System); new `ViewModels/Designer/FormColorChoices.cs` (`Web`/`System` filtered by
`Accepts`, `ToDocumentText`, `TryResolve` for the preview, `TabFor`); the row template wraps the shared editor in a
`DockPanel` with the drop-down LEFT of it, visible on `IsColor`; `FormPropertyRow` gains `WebColorChoices`,
`SystemColorChoices`, `SwatchColor`, `Swatch`, `HasUnknownSwatch`, `ApplyColor`, and `IsColor` now requires the typed editor
(its "text for now" comment is gone; the flag finally has a caller).

Choices taken under the delegation (VS where in doubt):
- **The pop-up opens on the tab that holds the value** (VS): System for a system colour, Web for a named one, Custom
  otherwise (`FormColorChoices.TabFor`). Found by the reopen step: a TabControl otherwise keeps the last tab.
- **A Web/System pick closes the pop-up**; the Custom tab writes once on close (OK closes), only if the user moved it.
- **"?" means "cannot preview", never "empty"**: a default-less Label BackColor draws an empty swatch; a CSS-only web name
  (`RebeccaPurple`, which neither `System.Drawing` nor Avalonia's table knows) draws "?" — never a guessed colour.
- ⚠ **Deviation: the real-view tests live in `FormPropertyGridEditorRealViewTests.cs` as a second file of the SAME partial
  fixture** (`FormPropertyGridRealViewTests` is now `partial`), so the rig is shared rather than copied. The rig's `Open`
  gained a `target` parameter for the web fixture.

Tests: red first with stubbed logic (18 red, each for the expected reason: empty lists, `#FFFF0000` for opaque, no
swatch, the Custom close not writing). Then green.
- `FormColorChoicesTests` (new, 30): System = 33 on WinForms / the 8 with CSS on the web (derived from `CssFor`); Web = the
  141 names on both; every WinForms entry previews; canonical hex ×4 (each accepted on both targets, already canonical);
  preview resolution ×5 and refusals ×5; `TabFor` ×6; the row's lists per target; `ApplyColor` writes once and the same
  colour again is a no-op; the "?" rule; a frozen colour has no drop-down.
- `FormPropertyGridViewTests.EveryBindingInAnEditorView_ResolvesAgainstItsRootDataType` (`TestCaseSource`, the root's
  `x:DataType`; FormColorDropDown now, the Font and Items dialogs join it).
- Real view: `TheColourDropDown_HasATemplatedColorView_AndPickingRedOnTheWebTab_WritesItOnce_AtTwoSizes` (visual children >
  0; open frame ≠ closed; real clicks on the Web tab and on Red; ONE Edited; closes; reopened on Web with the Custom view at
  Red), `TheColourDropDown_OpensOnTheTabHoldingTheValue_AtTwoSizes`, `OnAWebForm_TheSystemTab_OffersOnlyCssColours_…`,
  `TheCustomColour_IsWrittenOnceOnClose_NeverPerChange_AtTwoSizes` (open/close = no write; three moves + OK = one write of
  `#0000FF`).
- `FormDesignerLayoutRealViewTests.EveryRowsEditor_Fits…` sweeps `FormColorDropDown` and requires it was seen.

RE-CHECK: `FormPropertyGridTests` (`IsColor`), `EveryRowsEditor_Fits…`, `EveryBindingInTheGridView_…`,
`TheDocumentView_NoLongerCarriesTheGridsOwnBindings` — green; every non-Integration `Form*`/`CodeEditor*` test 2886/2888
(1 skipped; the one failure is the known `EveryTextRoute_…`).

Mutations (Edit + REBUILD; the AXAML one after `dotnet clean`):
| Mutant | Result |
|---|---|
| Custom commits on every `ColorChanged` | killed (the Custom test, the Red test) |
| System list unfiltered on the web (`Accepts(name)` without the target) | killed (2 VM, the web real-view test) |
| the theme include removed from `FormPropertyGridView.axaml` | killed — "ColorView has a template: expected > 0, was 0" |
| `#AARRGGBB` for alpha 255 | killed (the red-step stub: hex ×2) |
| `TabFor` not applied on open | killed (`…OpensOnTheTabHoldingTheValue…`). ⚠ The reopen step alone was VACUOUS for it (the tab control keeps its last tab, which was Web), so the tab test was added |
| a "seeding" guard around the Custom seed | EQUIVALENT (the reset after the seed covers it) → removed |

### Task 5 — Font dialog (base `b97ac2bc`)
Done as written: new `Views/Dialogs/FormFontDialog.axaml(.cs)` (filter box + family list, size box + VS's size list,
Bold/Italic/Underline/Strikeout, a live preview, the web hint, OK/Cancel) and `ViewModels/Designer/FormFontDialogViewModel.cs`
(families from the seam, filtered by `FormFontValue.TryParse`, plus the current family and `Segoe UI`; `Preview` /
`CanAccept` through the parser; `Accept()` = canonical text, `Cancel()` = null). `FormPropertyRow` gains `HasEllipsis`,
`EffectiveFont`, `Target`, `ApplyFont` and an internal `Inherited`; `FormCompositeRows.Attach` stores the inherited function
on the row, and the parts and the dialog both read it through `FormCompositeRows.EffectiveFont` (ONE rule). The grid
view's code-behind opens the dialog with `ShowDialog<string?>(owner)` and commits a non-null result; its `FontFamilies`
seam defaults to `FontManager.Current.SystemFonts`.

Choices taken under the delegation:
- ⚠ **Deviation: the `…` sits LEFT of the text box**, not docked right as the plan says (VS puts it right): the list's
  overlay scrollbar covers the cell's right edge — Task 2's measured reason, which the swatch follows too. The real-view
  test asserts a click at its centre reaches it.
- The family and size lists bind their selection ONE way and write back only a real pick: a typed `9.5` is in no list and
  a filter can hide the chosen family, and a TwoWay `SelectedItem` would push null over the value in both cases.
- OK is disabled while the choices make no font (a size the parser refuses: 0, three decimals, text) — OK never produces a
  value the catalog would refuse.

Tests: red first with stubs (unfiltered families, `Accept` → null, the start ignoring the inherited font): 6 red for the
expected reasons. ⚠ The ambient test was ALSO red for a wrong reason on that run — its fixture wrapped the GroupBox's child in
`<Controls>`, which the reader does not nest; fixed (children sit directly in the container element), then the inherited-start
mutant was re-run against the corrected fixture (below). Then green.
- `FormFontDialogViewModelTests` (new, 16): refused families never offered (`Font & Co`, `Semi;Colon`); the current family
  offered and selected when not installed; Bold+Italic → `Arial, 10pt, style=Bold, Italic`; Cancel → null; size acceptance
  ×5; the filter; the web hint; an absent ambient Font inside a bold GroupBox starts at `Tahoma, 10pt, style=Bold`; only the
  Font row has the ellipsis.
- `EveryBindingInAnEditorView_…(FormFontDialog)` joins the walker list.
- Real view: `TheFontDialog_PicksAFamilyAndBold_AndOkWritesOneCanonicalFont_CancelWritesNothing_AtTwoSizes` — the dialog is in
  `OwnedWindows`, offers the seam's families minus the refused one, starts at the inherited `Segoe UI`; real clicks on Arial,
  Bold, OK → `Font="Arial, 9pt, style=Bold"`, ONE Edited, nothing written while open; reopened Bold, Italic + Cancel →
  byte-identical, no edit.
- `EveryRowsEditor_Fits…` sweeps the `FontEllipsis` button and requires it was seen.

RE-CHECK: every non-Integration `Form*`/`CodeEditor*` test 2901/2903 (1 skipped; the one failure is the known
`EveryTextRoute_…`), including `FormCompositeRowTests` (the parts now read the row's stored inherited function).

Mutations (Edit + REBUILD):
| Mutant | Result |
|---|---|
| the family filter removed | killed (red step: VM + real view) |
| OK writes per toggle (a commit on every dialog change) | killed (real view: "nothing written while open", 2 edits) |
| Cancel writes (`Close(Accept())`) | killed (real view: Italic written, 2 edits) |
| the start value ignores the inherited font | killed (`AnAbsentAmbientFont_…`, on the corrected fixture) |

### Part C — review of `47c31434` / `b97ac2bc` / `8eb882be` (base `8eb882be`; one commit)
1. **Panel syncs.** `UpdateTextFromEditor` syncs only in Design view (Code view: hidden panels; `ToggleDesignMode` re-syncs on
   entry). Undo/redo run INSIDE `AdoptDocumentText(rewind)` under `_adoptingDocumentText`, which stands down the editor route
   and `OnTextChanged`, so one undo syncs exactly once (was twice). Tests count grid rebuilds (one `Rows` Reset per Load).
2. **Translucent BackColor — MEASURED.** `WinFormsTranslucentBackColorRunTests` (Integration; dotnet build + run) constructs
   every catalog kind with a WinForms BackColor row, plus the Form, and sets `Color.FromArgb(128, …)` and `Color.Transparent`
   by reflection. Throw (both values): **TextBox, ComboBox, ListBox, NumericUpDown, TrackBar, ProgressBar, CheckedListBox,
   ListView, TreeView, Form**. Accept: Label, Button, CheckBox, RadioButton, Panel, GroupBox, PictureBox, LinkLabel,
   SplitContainer, FlowLayoutPanel, TableLayoutPanel. New `FormPropertyDef.OpaqueOnWinForms` (on `WindowBackColor` — all seven
   users throw — a new `OpaqueBackColor` for TrackBar/ProgressBar, and the FormRoot row); `IsTranslucentRefusedOn` joins
   `IsRefusedOn` with its reason ("… throws ArgumentException when the form is created"), so the editor refuses, a document
   value is Degraded and never emitted, the Web tab drops `Transparent`, and `FormPropertyRow.AllowsAlpha`
   (`AcceptsTranslucentOn`) turns the Custom tab's `IsAlphaEnabled` off. The web keeps alpha. The run test asserts, per kind,
   throws ⇔ the catalog refuses, so the table cannot drift. Red before: the run test failed for the 10 throwing kinds.
3. **Custom tab: Esc cancels** (a tunnel KeyDown on the control and the pop-up content drops the pending move before
   `Closed`); a click away (light dismiss) or OK commits once. The alpha channel is forced off on close where the row refuses it.
4. **Font dialog:** the `async void` handler catches and logs (`Trace.TraceError`); a non-`Window` top level shows the dialog
   modeless and awaits its close (same result rule). The size box reads the CULTURE's decimal separator (sv-SE `9,75` →
   `9.75pt`; `.` always accepted; both marks refused); the start size shows in the culture. The document's family spelling
   wins over an installed family differing in case (it is listed first). Tests: Esc and the close box write nothing; OK on an
   inherited font stores it explicitly (VS) — pinned.
5. The posted Dock Hide's stale-row case is documented (ContainerFromItem → null).

Mutations (Edit + REBUILD): IsDesignMode guard removed → killed (keystroke test); undo flag not set → killed (2 rebuilds);
WindowBackColor flag off → killed (run test + 6 VM); Transparent not translucent → killed; IsAlphaEnabled not set → killed;
Esc handler off → killed; culture separator ignored → killed (sv-SE, de-DE); family order reverted → killed by the VM spelling
test. ⚠ `AnUntouchedOk_OnALowerCaseFamily_…` stays green under that mutant (Judge treats the family case-insensitively) — an
end-to-end pin, not a kill.

### Task 6 — Items storage (base Part C's commit) — ADR 0020 first
ADR `docs/superpowers/decisions/0020-form-items-as-child-elements.md` (index updated; ⚠ re-check the number at merge). New
`BasicLang/Forms/FormItems.cs` (`IsCollection`, `Split`, `Join`, `FromLegacy`, `HoldsLineBreak`); `SplitItems` delegates to
`FormItems.Split`. Reader: a legacy attribute through `FromLegacy`, `<Item>` text verbatim; Degraded (line break, or both
forms) → nothing in `Properties`, raw elements to `UnknownChildren`, the attribute to `UnknownAttributes`, a reason in
`Degraded`. Writer: the generic set loop and the dropped-property sweep skip the row; `ApplyItems` leaves an equal legacy
attribute or equal-as-read children ALONE, else rewrites the run first among the children (a self-closing element gets one
item per line at the document's indent); a Degraded control is never touched. Create writes `<Item>`s before `<Bind>`.
⛔ **Found (fixed): the clipboard handed a Degraded list's raw comma attribute to the MODEL on paste** (it would have been
emitted as one item). `FormClipboard.FromElement` now keeps an items attribute UNKNOWN when the fragment also carries raw
`<Item>` children — the only shape a Degraded list travels in (the model's list travels as the LF attribute).
Tests: `FormItemsStorageTests` (new, 17: legacy byte-identical on `.blform` AND `.blwebform`; comma, `&`, `<`, spaces, blank
lines, `<Item/>` byte-identity, both Degraded shapes × {frozen/preserved/not emitted, the five paths}, removal, Create order,
clipboard, retarget + `<option>`s, the region writer). RE-CHECK: `FormAssetEmitterTests` ×5 now set the MODEL as LF text;
`FormPropertyGridTests.cs:291` (legacy XML) green. Red first: 18 red on the old reader/writer for the expected reasons
(`TheClipboard_RoundTripsACommaItem` was green — a pin, not a kill).

### Task 7 — the Items editor
`FormItemsDialog` (VS's "String Collection Editor": a multi-line box, OK/Cancel; OK is NOT IsDefault, Enter is a new line)
and `FormItemsDialogViewModel` (splits on CRLF/LF/CR; OK → the model value, "" for none). The row: `IsCollectionEditor`
(and out of `IsTextBox`), `CollectionSummary` = `(Collection)`, `ApplyItems` (an empty list RESETS). The grid shows
`(Collection)` and a `…` (`OnItemsEllipsisClick`, caught and logged, the shared `ShowDialogAsync`).
⚠ Deviation: the editor's code was written before its tests ran; the red evidence for it is the mutation table below.
⚠ **Emission stays `Items.Add` per item** — the pre-flight's "unchanged emitter". The coordinator's note said
`Items.AddRange`; switching shape would add an array-literal emission through BasicLang for no behavioural gain, and the
run test below proves the items reach the live control.
Tests: `FormItemsDialogViewModelTests` (new, 8), the editor walker case, real view
`TheItemsEditor_TypedThroughRealKeys_WritesOneItemEach_AndCancelWritesNothing_AtTwoSizes` (real `KeyTextInput`/Enter, ONE
Edited, two `<Item>`s; Cancel byte-identical). **RUN** (`FormItemsAcceptanceTests`, Integration, real designer VM + SaveAsync +
real CLI): WinForms window prints `COUNT 3` and each item (`Smith, John`, `A & B`, `x < y`); web: the CLI-built page has one
`<option>` per item (decoded exactly) and its script runs under node. ⚠ The web half reads the page's `<option>`s — a static
part of the page — rather than a browser's rendering; the node run proves the page's script loads.

Mutations (Edit + REBUILD) — all killed: no-op save rewrites legacy; `FromLegacy` on `<Item>` text; the Degraded list
modelled/emitted; stale attribute left beside new children; element-count compare instead of list-as-read (the `<Item/>`
byte-identity test); the clipboard guard removed (red step of its own fix); the dialog splitting without the lone-CR rule;
empty → "" written instead of Reset; the `…` click unwired (real view).
