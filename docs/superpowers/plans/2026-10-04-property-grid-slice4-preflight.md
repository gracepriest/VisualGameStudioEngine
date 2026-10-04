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
