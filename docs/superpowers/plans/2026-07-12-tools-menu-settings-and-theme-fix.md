# Tools Menu / Settings Dialog / Theme Fix Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every option reachable from Tools → Settings actually work end-to-end, and eliminate the Light / High-Contrast "white lines around buttons" rendering bugs.

**Architecture:** Three root causes drive ~100 audited defects: (1) the settings service never loads its store at startup and a second legacy store splits the truth; (2) ~25 settings persist but have no consumer; (3) theme resources flip with the Avalonia theme variant while most Shell chrome hardcodes dark hex colors, and the High-Contrast style block is dead code due to Avalonia resource-lookup and style-ordering rules. Fix the keystones first (tiny diffs, huge blast radius), then the theme system, then wire consumers group by group.

**Tech Stack:** C# / Avalonia 11.3, CommunityToolkit.Mvvm, System.Text.Json, NUnit (VisualGameStudio.Tests).

**Audit provenance:** 121-agent audit (13 auditors + adversarial verifiers), 2026-07-12. 102 confirmed defects, 15 verified-working, 3 works-claims overturned. Live repro screenshots captured for Light and High Contrast. Items marked *(unverified)* below were confirmed by one auditor but their adversarial re-check was cut off by a session limit — re-verify the cited lines before coding against them.

**Repo conventions that apply to every task here (from CLAUDE.md):**
- After ANY `.axaml` change: `dotnet clean` before building, or the stale cache crashes the IDE.
- Never round-trip repo files through PowerShell `Get-Content`/`Set-Content` (BOM-less UTF-8 corruption). Use Edit/Write tools.
- Full suite: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`. Tests are NUnit.
- Verify through the running IDE (`IDE/VisualGameStudio.exe` after refreshing binaries), not just the suite.

---

## Decision points (defaults chosen; change before executing if you disagree)

| # | Decision | Options | **Default in this plan** |
|---|----------|---------|--------------------------|
| D1 | Settings dialog commit model | (a) Live-apply + snapshot-revert on Cancel; (b) buffer everything until OK; (c) VS Code model: all-live, remove OK/Cancel | **(a)** — smallest diff, keeps current live-apply feel, makes Cancel honest |
| D2 | High-Contrast palette mechanism | (a) Custom `ThemeVariant("HighContrast")` + third ThemeDictionary; (b) push/pop brushes in `Application.Current.Resources` at Apply-time (like VS Code themes) | **(a)** — declarative, no leak risk, same pattern as Dark/Light |
| D3 | Settings with no backing feature (`editor.cursorBlinking`, `workbench.iconTheme`, `terminal.integrated.cursorStyle`, `editor.minimap.side`) | (a) Implement the feature; (b) remove from dialog until it exists | **(b)** — honest UI now; features become separate specs |
| D4 | Settings→Keyboard editable grid | (a) Make read-only reference generated from real KeyBindings; (b) build a real keymap pipeline (persist + rebind live) | **(a)** now; (b) is its own spec |

---

## Phase 0 — Keystone persistence fixes (do these first; they unblock everything)

### Task 0.1: Load user settings at startup (`SettingsService.LoadAsync` has zero callers)

The single worst bug: `~/.vgs/settings.json` is **never read** at launch, so every `Get()` returns schema defaults, the dialog shows defaults, and pressing OK clobbers the user's saved values.

**Files:**
- Modify: `VisualGameStudio.Shell/App.axaml.cs` (after DI container build in `OnFrameworkInitializationCompleted`)
- Test: `VisualGameStudio.Tests/SettingsServiceStartupTests.cs` (new)

- [ ] **Step 1: Write the failing test** — construct `SettingsService` pointed at a temp home dir containing a settings.json with `"editor.fontSize": 99`, call the same startup path App uses, assert `Get<int>("editor.fontSize") == 99`. (Give `SettingsService` a testable home-dir override or factory if it hardcodes the user profile — check `SettingsService.cs:49-51`.)
- [ ] **Step 2: Run it, verify it fails** (value comes back 14, the schema default).
- [ ] **Step 3: Implement** — in `App.OnFrameworkInitializationCompleted`, right after `Services` is built: resolve `ISettingsService` and block on `LoadAsync()` (it is a fast local file read; `GetAwaiter().GetResult()` is acceptable pre-UI). Alternative: make the `SettingsService` constructor load synchronously.
- [ ] **Step 4: Run test → PASS; run full suite.**
- [ ] **Step 5: Commit** `fix: load user settings.json at startup (SettingsService.LoadAsync was never called)`

### Task 0.2: Atomic, corruption-proof settings writes

Observed live during the audit: `~/.vgs/settings.json` ended up with **duplicate keys** (`workbench.colorTheme` twice, later `git.autoFetch` + `editor.insertSpaces` twice) — concurrent non-atomic `File.WriteAllTextAsync` calls (two IDE instances + watcher-triggered re-saves) interleaving.

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/SettingsService.cs:669-688` (`SaveToFileAsync`)
- Test: `VisualGameStudio.Tests/SettingsServicePersistenceTests.cs` (new)

- [ ] **Step 1: Failing tests** — (a) save writes via temp file + `File.Replace`/`File.Move(overwrite:true)` so a reader never sees a torn file; (b) `LoadFromFileAsync` tolerates duplicate keys (last-wins) — feed it a file with a duplicated key, assert load succeeds with the last value.
- [ ] **Step 2: Implement** — write to `settings.json.tmp` then atomic replace; keep the existing `_saveLock`. Add a `FileShare.Read`-safe read with one retry on `IOException` for the watcher path.
- [ ] **Step 3: Suite green. Commit** `fix: atomic settings writes; tolerate duplicate keys on load`

### Task 0.3: Kill the split-brain store (legacy `%APPDATA%\VisualGameStudio\settings.json`)

Today: dialog live-writes `~/.vgs`; editors and startup theme read the **legacy** file, which only OK updates. Result: "applies on OK only", "reverts on restart", and the two stores diverge (directly observed: legacy said `Light` while `~/.vgs` said `Dark`).

**Files:**
- Modify: `VisualGameStudio.Shell/ThemeManager.cs:76-81` (`ApplyFromSettings`)
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs:1204-1217` (`Save`), `1461-1505` (`SaveSettings`), `1507-1521` (`LoadCurrentSettings`)
- Modify: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml.cs:1878-1913` (`ApplyEditorSettings`)
- Test: extend `SettingsServiceStartupTests.cs`

- [ ] **Step 1: Failing test** — theme round-trip through the single store: set `workbench.colorTheme` = "Light" via `ISettingsService`, simulate restart (new service + the new theme-name reader), assert the startup theme resolver returns "Light" **without** any `%APPDATA%` file present.
- [ ] **Step 2: `ThemeManager.ApplyFromSettings`** — read `~/.vgs/settings.json` key `workbench.colorTheme` directly (it runs pre-DI; do a minimal `JsonDocument` parse with the same comment-stripping helper, or move Apply-from-settings to right after DI build now that Task 0.1 loads the store). One-time migration: if `~/.vgs` has no theme but the legacy file exists, migrate `SelectedTheme` → `workbench.colorTheme`.
- [ ] **Step 3: `ApplyEditorSettings`** — replace `SettingsViewModel.LoadCurrentSettings()` with reads from `ISettingsService` effective values (resolve via `App.Services`). This alone converts ~8 editor settings from "OK-only" to live (the static `SettingsChanged` re-apply subscription already exists at `CodeEditorDocumentView.axaml.cs:386-387, 1869-1872`).
- [ ] **Step 4: `Save()`** — delete the `SaveSettings()` legacy write; keep `LoadCurrentSettings` only for the migration shim (mark `[Obsolete]`).
- [ ] **Step 5: Suite green; manual smoke: change font size → takes effect immediately without OK; restart → survives. Commit** `fix: single settings store — retire legacy %APPDATA% settings.json (theme + editor settings now live + restart-safe)`

### Task 0.4: Make Cancel honest (D1: snapshot-revert)

`Cancel()` (`SettingsViewModel.cs:1219-1224`) closes the dialog leaving every live-applied change **applied and persisted**, theme included.

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs`
- Modify: `VisualGameStudio.Shell/Views/Dialogs/SettingsDialog.axaml.cs` (window `Closing` hook)
- Test: `VisualGameStudio.Tests/SettingsDialogCancelTests.cs` (new)

- [ ] **Step 1: Failing tests** — open VM, change `FontSize` and `SelectedTheme`, call `CancelCommand`: assert service values AND `ThemeManager.CurrentTheme` are restored to the snapshot; OK path asserts values stick.
- [ ] **Step 2: Implement** — in the VM constructor snapshot `(key, effective value, scope)` for all settings keys plus the current theme name; `Cancel()` writes the snapshot back through the service (only keys that changed) and calls `ThemeManager.Apply(snapshotTheme)`. Treat window-X-close as Cancel: in `SettingsDialog.axaml.cs` handle `Closing` → if `DialogResult != true`, invoke the same revert.
- [ ] **Step 3: Suite green. Commit** `fix: Settings Cancel/X reverts live-applied changes (incl. theme)`

### Task 0.5: OK stops flooding scopes; Reset All gets a confirmation

`Save()` → `SaveToService()` dumps **all ~45 keys** into the active scope (workspace tab active → all 45 keys into the project's `.vgs/settings.json`). `ResetToDefaults` wipes user + workspace stores with no prompt and skips `ThemeManager.Apply`.

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs:1355+` (`SaveToService`), `1226-1280` (`ResetToDefaults`)

- [ ] **Step 1:** `SaveToService` writes only keys whose value differs from the effective value outside the dialog edit (or delete it entirely — values were already live-saved by `AutoSaveSettingToService`; verify nothing else depends on it).
- [ ] **Step 2:** `ResetToDefaults`: confirm via `IDialogService` first; restrict to the **active** scope; route `SelectedTheme` through `ThemeManager.Apply`; also clear the legacy file if the migration shim still exists.
- [ ] **Step 3: Suite green. Commit** `fix: settings OK writes only modified keys; Reset All confirms, respects scope, applies theme`

---

## Phase 1 — Theme fixes (the reported bug)

### Background facts (verified during audit — trust these, they are counter-intuitive)

1. **Avalonia has no CSS specificity.** Class selectors and type selectors both produce style frames; among equal priority, **the LAST-declared style wins per property**. The HC override block sits at the TOP of `AppStyles.axaml` (lines 62-361) so the base class styles BELOW it (`Button.toolbarButton:438`, `Button.primary:493`, `Border.statusBar:397`…) silently kill the HC variants.
2. **`Style.Resources` are NOT scoped to the selector.** The 22 HC brushes inside `<Style Selector="Window.highContrast">` (`AppStyles.axaml:65-92`) are dead: theme-dictionary lookup wins first, so HC currently renders with the ordinary **Dark** palette. *(unverified — re-check against Avalonia 11.3.13 `StyleBase.TryGetResource` before relying on it)*
3. **The `highContrast` class is only stamped on windows open at Apply-time** (`ThemeManager.cs:299-316`), and `ApplyFromSettings` runs in `App.Initialize()` **before MainWindow exists** — so after a restart in HC, even the main window is unclassed.
4. **Light theme's white lines are seams**: theme-following resources (button hover fills, `IdeSecondaryBorder`→`#C8C8C8`, `IdeHeaderBg`→`#DDDDE1`, Fluent default button chrome) rendering on ~76 views that hardcode dark hexes. The Fluent *Light* default button face is translucent **black** (`#33000000`) with a transparent border *(verify against the installed Avalonia Fluent theme resources before relying on it for the smoke checklist)* — the "white" comes from *our* light-resolving brushes and light default chrome on dark hardcoded strips, e.g. the 7 status-bar buttons that have **no base style at all** (`Classes="statusBarButton"` is only styled under `Window.highContrast`).

### Task 1.1: Real High-Contrast palette (D2: custom ThemeVariant)

**Files:**
- Modify: `VisualGameStudio.Shell/Resources/Styles/AppStyles.axaml:7-92`
- Modify: `VisualGameStudio.Shell/ThemeManager.cs:100-137`
- Test: manual 3-theme screenshot smoke (script pattern below) + suite

- [ ] **Step 1:** Add a third ThemeDictionary: `<ResourceDictionary x:Key="{x:Static local:AppThemes.HighContrast}">` — define `public static class AppThemes { public static readonly ThemeVariant HighContrast = new("HighContrast", ThemeVariant.Dark); }` in Shell. Move the 22 HC brush values from the dead `Style.Resources` block into it; delete the dead block. *(verify: write a 3-line spike first confirming variant fallback — keys absent from the HC dictionary must resolve from Dark via the inheritance parent.)*
- [ ] **Step 2:** `ThemeManager.Apply("High Contrast")` sets `RequestedThemeVariant = AppThemes.HighContrast` (inheritance falls back to Dark for undefined keys).
- [ ] **Step 3:** `dotnet clean` + build + screenshot smoke in HC: menus/tab strips/panels must now be black, not dark-gray.
- [ ] **Step 4: Commit** `fix: High Contrast gets a real ThemeVariant palette (Style.Resources block was dead code)`

### Task 1.2: Stamp `highContrast` class on every window, forever

**Files:**
- Modify: `VisualGameStudio.Shell/ThemeManager.cs` (keep `ApplyHighContrastClass` for already-open windows)
- Modify: `VisualGameStudio.Shell/App.axaml.cs`

- [ ] **Step 1:** Add `ThemeManager.Register(Window)` — if `ThemeManager.IsHighContrast`, add the class; call it from a shared dialog-show helper (or each `ShowDialog` site's owner-window factory), `MainWindow`'s constructor, and the DockFactory `HostWindowLocator` lambda. (Note: Avalonia has no WPF-style `EventManager` global class handlers and `Window.Opened` is a plain CLR event — per-creation-site registration is the reliable route. If you find `TopLevel`/lifetime hooks that fire per-window in Avalonia 11.3, you may centralize, but verify first.)
- [ ] **Step 2:** After MainWindow is constructed at startup, re-run `ApplyHighContrastClass()` (fixes the restart-in-HC unclassed main window).
- [ ] **Step 3:** Manual smoke: switch to HC, open Settings dialog + a refactoring dialog + float a panel — all must render HC. Commit `fix: highContrast class applied to windows opened after theme change and at startup`

### Task 1.3: Reorder HC overrides + fix the class-style kills

**Files:**
- Modify: `VisualGameStudio.Shell/Resources/Styles/AppStyles.axaml` (move HC section from lines 62-361 to the END of the file)

- [ ] **Step 1:** Move the entire HC override section below all base styles. Declaration order is the only specificity — this revives `Window.highContrast Button.toolbarButton / Button.primary / Border.statusBar / Button.statusBarButton` etc.
- [ ] **Step 2:** While there, scope the blanket `Window.highContrast Button` border so tiny icon buttons don't get stray 1px boxes: keep the border for content buttons, add explicit HC styles for dock chrome (`idc|ToolChromeControl` part buttons), tab close buttons, and panel-header icon buttons (consistent border + yellow focus, or `BorderThickness=0` + hover/focus ring only — pick one treatment and apply it to all icon buttons).
- [ ] **Step 3:** `dotnet clean`, build, HC screenshot smoke: status bar must go black with its designed white top border (see Task 1.4 for the binding that also fights it); no stray white line across a blue bar. Commit `fix: HC style ordering — overrides now beat base class styles`

### Task 1.4: Status bar — the single worst Light-theme offender

7 buttons `Classes="statusBarButton"` have **no base style** → raw Fluent chrome boxes on the hardcoded `#007ACC` bar (translucent-black boxes in Light, translucent-white in Dark — the archetypal "white lines"). The bar's `Background` **binds** `BoolToBrushConverter.DebugStatusBarInstance` (local value → beats any style, including HC black).

**Files:**
- Modify: `VisualGameStudio.Shell/Resources/Styles/AppStyles.axaml` (new base style)
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml:446-616` (status bar area)
- Modify: the `BoolToBrushConverter` (Shell converters file — locate `DebugStatusBarInstance`)

- [ ] **Step 1:** Add base style mirroring `statusItem` from `Views/Controls/StatusBarControl.axaml:14-29` (note: `StatusBarControl` itself is DEAD CODE — never instantiated; the live status bar is inline in `MainWindow.axaml:446-616` — copy the style values, don't wire the control): `Button.statusBarButton { Background=Transparent; BorderThickness=0; CornerRadius=0; Padding=6,2 }` + `:pointerover` `#FFFFFF20`, `:pressed` `#FFFFFF30`.
- [ ] **Step 2:** Add `IdeStatusBarBg` / `IdeStatusBarDebugBg` keys to all three ThemeDictionaries (Dark/Light: `#007ACC`/`#CC6800`; HC: `#000000`/`#000000`). Change the converter to resolve them via `Application.Current.FindResource` at convert time (no cached brushes). **Converter re-evaluation gotcha:** the binding only re-converts when `IsDebugging` changes, so a live theme switch would leave the old color — subscribe the status bar (or MainWindowViewModel) to `ThemeManager.ThemeChanged` and re-raise the bound property (or replace the converter binding with a style-class swap `statusBar`/`statusBarDebug` whose Backgrounds use the themed keys, which re-resolve automatically).
- [ ] **Step 3:** `dotnet clean`, build, screenshot smoke Light + HC + Dark. Commit `fix: status bar buttons get real styles; bar background theme-aware (was hardcoded via converter)`
- [ ] **Step 4:** Same pattern for `Button.skipNavLink` (also HC-only today). Commit with it or separately.

### Task 1.5: Toolbar + panel chrome sweep (Light mixed-render)

**Files:**
- Modify: `VisualGameStudio.Shell/Views/MainWindow.axaml:358` (toolbar Border + 3 separator Borders at ~378/392/421)
- Modify: `VisualGameStudio.Shell/Views/Panels/OutputPanelView.axaml`, `TerminalView.axaml` (headers/tab strip), panel-header button classes across `Views/Panels/*`
- Modify: `VisualGameStudio.Shell/Resources/Styles/AppStyles.axaml` (add `Button.toolbarButton:pressed` override)

- [ ] **Step 1:** Toolbar Border → `Background={DynamicResource IdeMenuBg}` `BorderBrush={DynamicResource IdeBorder}`; separators → `IdeBorder`. The build-config ComboBox then matches automatically.
- [ ] **Step 2:** Class the five Output filter buttons (Build/Debug/LSP/General/Clear) + Send + terminal tab buttons as `toolbarButton` (or a new `panelHeaderButton` style with explicit rest/hover/pressed chrome); panel bodies that stay hardcoded dark are handled in Task 1.6.
- [ ] **Step 3:** `dotnet clean`, build, Light screenshot smoke: no dark toolbar strip, no white ComboBox island, no light hover boxes on dark bars. Commit `fix: toolbar + panel header chrome follows theme (Light mixed-render)`

### Task 1.6: Hardcoded-dark surface migration (staged sweep)

~76 views hardcode `#1E1E1E/#252526/#2D2D2D/#3C3C3C/#CCCCCC/#808080`. Migrate to `Ide*` DynamicResources, adding these **new keys** to all three ThemeDictionaries first:

| New key | Dark | Light | HC |
|---|---|---|---|
| `IdeEditorBg` | `#1E1E1E` | `#FFFFFF` | `#000000` |
| `IdeAccent` | `#3794FF` | `#0066BF` | `#FFFF00` |
| `IdeFgMuted` | `#808080` | `#616161` | `#FFFFFF` |
| `IdeFgSubtle` | `#606060` | `#8E8E8E` | `#C0C0C0` |
| `IdeOverlayBg` | `#E8252526` | `#E8F6F6F6` | `#000000` |
| `IdeActivityBarBg` / `Fg` | `#333333`/`#CCCCCC` | `#2C2C2C`/`#FFFFFF` | `#000000`/`#FFFFFF` |
| `IdeWarningBg/Border/Fg`, `IdeSuccessBg/Border/Fg`, `IdeErrorFg`, `IdeDiffAddedBg`, `IdeDiffRemovedBg` | (current dark values) | (light equivalents) | black bg + white/yellow |

Order by user impact, one commit per bullet, `dotnet clean` + Light/HC screenshot after each:

- [ ] **1.6a** Dialogs with `Button.secondary` white-outline bug (`AttachToProcessDialog`, `ExceptionSettingsDialog`): drop the local dark restyles, window `Background={DynamicResource IdeBg}`; Cancel buttons get `Classes="secondary"`.
- [ ] **1.6b** Refactoring dialog family (~28 dialogs, cookie-cutter): window → `IdeBg`, panes → `IdePanelBg`/`IdeBorder`, labels → `IdeFg`/`IdeFgMuted`, bare Cancel buttons → `Classes="secondary"`, warning/success boxes → semantic keys.
- [ ] **1.6c** SettingsDialog itself: code-behind converters (`ScopeTabFg` returns `Colors.White`, `CategoryBg` returns `#37373D`) → resolve `IdeFg`/`IdeListSelected` via `FindResource` at convert time; `#3794FF` accents → `IdeAccent`; scope badge → `IdeListSelected` + `IdeFg`.
- [ ] **1.6d** Document tab strip (`TabStripControl`), Welcome page (67 literals), Command Palette + Quick Open (`IdeOverlayBg`), status-bar picker popups + notification flyout (`IdeContextBg`/`IdeContextBorder`).
- [ ] **1.6e** Panels: Git family + FindInFiles + Timeline + DiffViewer (diff colors → semantic keys), Problems (delete DataGrid Background/RowBackground locals — global style already themed), Extensions, Debug family, FindReplace widget, PeekDefinition popup, editor splitters (delete locals; global GridSplitter style already uses `IdeBorder`).
- [ ] **1.6f** Activity bar → `IdeActivityBarBg`/`Fg` keys (stays dark in Light by design, goes true black in HC).
- [ ] Add every new key to `ThemeManager.ClearResourceOverrides` (`ThemeManager.cs:275-297`) so VS Code extension themes can retint and cleanly release them.

### Task 1.7: Theme verification harness

- [ ] Write `scratchpad`-style capture script into `docs/superpowers/plans/assets/` or a test utility: launch `IDE/VisualGameStudio.exe` with `workbench.colorTheme` pre-set (Dark / Light / High Contrast), screenshot main window + Settings dialog + one refactoring dialog, eyeball seams. (Precedent script from this audit: Win32 `GetWindowRect` + `Graphics.CopyFromScreen`.)
- [ ] Commit any fixes found; refresh `IDE/` prebuilt binaries per repo convention when the batch lands.

---

## Phase 2 — Wire the dead settings (grouped)

Every task below follows the same TDD shape: **(1)** failing NUnit test (or, where UI-bound, a focused manual verification step), **(2)** minimal wiring, **(3)** suite green, **(4)** commit. All reads go through `ISettingsService` effective values; live updates ride the existing `SettingsChanged`/`SettingChanged` events.

**⚠ Line-ref staleness:** the line numbers below were captured *before* Phase 0/1, which rewrite several of the same files (`SettingsViewModel.cs`, `ApplyEditorSettings`, `AppStyles.axaml`). After Phase 0/1 land, re-locate by **symbol name**, not line number.

### Task 2.0: Consumer registry (prerequisite for the Phase 3 contract test)

- [ ] Add `SettingsConsumerRegistry` (Shell or Core): `static void RegisterConsumer(string key, string consumerDescription)` + `static IReadOnlyDictionary<string,string> Consumers`. Every wiring bullet in Tasks 2.1-2.7 must call `RegisterConsumer` at its consumer's initialization (one line each). The Phase 3 contract test asserts every dialog key is registered.

### Task 2.1: Editor group (unlocked by Task 0.3)

**Files:** `CodeEditorDocumentView.axaml.cs:1878-1913` (`ApplyEditorSettings`), `CodeEditorControl.axaml.cs`, `SettingsViewModel.cs:1104-1132`

- [ ] `editor.tabSize` + `editor.insertSpaces` → `_textEditor.Options.IndentationSize` / `ConvertTabsToSpaces` (currently hardcoded 4/true) **and** build `FormattingOptionsInfo` from them for LSP `FormatDocumentAsync` calls (MainWindowViewModel currently sends defaults).
- [ ] `editor.highlightCurrentLine` → `Options.HighlightCurrentLine` (hardcoded true at init).
- [ ] `editor.stickyScroll.enabled` → the already-existing `CodeEditorControl.StickyScrollEnabledProperty` (one line).
- [ ] `editor.bracketPairColorization` → existing `BracketPairColorization` property (one line).
- [ ] `editor.autoClosingBrackets` → existing `AutoCloseBrackets` property; **also move `SettingsChanged?.Invoke` above the special-case early returns in `AutoSaveSettingToService` (SettingsViewModel.cs:1114-1122)** — today ShowLineNumbers/AutoCloseBrackets edits skip the live event.
- [ ] `editor.smoothScrolling` → existing `SmoothScrolling` property (one line).
- [ ] `editor.minimap.enabled` → seed `MainWindowViewModel.ShowMinimap` from the setting at startup + on change; fix the broken AXAML binding in `VisualGameStudio.Editor/CodeEditorControl.axaml` (`{Binding ShowMinimap, FallbackValue=True}` resolves against `CodeEditorDocumentViewModel`, which lacks the property, so the fallback `True` always wins — locate with Grep for `ShowMinimap` if moved).
- [ ] `editor.wordWrap` → read the string directly in `ApplyEditorSettings` (any non-`"off"` = wrap); document `wordWrapColumn` as unsupported (or drop the third combo choice, D3 spirit).
- [ ] `editor.renderWhitespace` → map `!= "none"` to `SetWhitespaceVisible`; seed the View-menu toggle state from it. Document `boundary`/`selection` as unsupported or remove the choices.
- [ ] `editor.formatOnSave` → in `SaveDocumentCoreAsync` (MainWindowViewModel, near the trim hook at ~:2450), run the formatter when true.
- [ ] `editor.cursorBlinking` → **remove from dialog** (D3), leave key in schema.

### Task 2.2: Terminal group

**Files:** `Views/Panels/TerminalView.axaml(.cs)`, `TerminalViewModel`

- [ ] `terminal.integrated.fontFamily`/`fontSize` → bind output `SelectableTextBlock` + input `TextBox` to VM properties fed from the service (fallback: editor font; note the hardcoded 13 doesn't even match the schema default 14). Live-update on `SettingChanged`.
- [ ] `terminal.integrated.defaultProfile` → `TerminalViewModel.LoadDefaultShellPreference` currently reads **raw `%APPDATA%` JSON** (`TerminalViewModel:401-404, 554`) — switch to `ISettingsService`, re-resolve on change (new sessions only), keep the platform-specific `.windows` key taking precedence.
- [ ] `terminal.integrated.cursorStyle` → **remove from dialog** (D3; no cursor-rendering surface exists).

### Task 2.3: IntelliSense group

**Files:** `MainWindowViewModel` (onCompletion/onHover/onSigHelp handlers), `CodeEditorControl.axaml.cs`

- [ ] `intellisense.autoComplete` → gate the completion auto-trigger / request handler.
- [ ] `intellisense.quickInfo` → early-return in the hover handler.
- [ ] `intellisense.signatureHelp` → gate the `(`/`,` trigger.
- [ ] `intellisense.delay` → make `CompletionDebounceMilliseconds` (hard 120) a settable property; push the setting.

### Task 2.4: Build + BasicLang group

**Files:** `MainWindowViewModel` (BuildAsync/RebuildAsync, ctor `:531`), `BuildService.cs:500,749-753`, `LanguageService` ctor

- [ ] `build.saveBeforeBuild` → wrap the unconditional `SaveAllAsync`.
- [ ] `build.showOutput` → guard the Output-panel activation.
- [ ] `build.defaultConfiguration` → initialize `CurrentConfiguration` from it (toolbar combo still wins during the session).
- [ ] `basiclang.compiler.backend` → use as default `TargetBackend` for new projects / `.blproj` files that omit it (per-project value still wins). **Test both entry points (CLI + IDE) per repo convention.**
- [ ] `basiclang.lsp.path` → `LanguageService` uses it as `_compilerPath` override when non-empty + exists, else auto-probe.
- [ ] `basiclang.lsp.autoStart` → guard the unconditional `StartAsync()`; add a manual "Start Language Server" command for the off case.

### Task 2.5: Git group

**Files:** `GitChangesViewModel` (:448 pull, :483 push), `StatusBarViewModel:508`, new `GitAutoFetchService` (or timer in `GitChangesViewModel`)

- [ ] `git.confirmSync` → confirmation dialog on pull/push/sync; **also subscribe `MainWindowViewModel` to `StatusBarViewModel.SyncRequested` — the status-bar Sync button currently does nothing at all** (zero subscribers).
- [ ] `git.autoFetch` + `git.autoFetchInterval` → periodic `IGitService.FetchAsync` timer honoring both, restart timer on change.

### Task 2.6: Workbench group

**Files:** `DockFactory.CreateLayout`, `MainWindowViewModel` startup path

- [ ] `workbench.startupEditor` → honor `welcomePage`/`newUntitledFile`/`none` when creating the initial document dock (respect per-project layout restore); wire the welcome page's own dead "Show welcome on startup" checkbox to the same key.
- [ ] `workbench.sideBar.location` → order root dock children / ToolDock alignment from it (restart-scope is acceptable; document it).
- [ ] `workbench.iconTheme` → **remove from dialog** (D3) and remove/fix the unrelated stub `OpenFileIconThemeAsync` picker (different value set, writes nothing).

### Task 2.7: Theme-flow leftovers

**Files:** `SettingsViewModel.cs:1094-1097, 1226+`, `MainWindowViewModel.cs:6401-6425`, `ThemeManager.cs`

- [ ] Quick picker (`Preferences > Color Theme`, `OpenColorThemeAsync`) → persist the choice through the same path as the dialog.
- [ ] JSON-editor edits of the theme → route through `ThemeManager.Apply` (today only the combo's `SetStringSetting` path applies live). *(`ResetToDefaults`' theme apply is already covered by Task 0.5 — don't duplicate.)*
- [ ] Extension themes: persist imported theme file paths (e.g. `workbench.importedThemes`), reload via `LoadVsCodeThemeFileAsync` at startup **before** applying the saved theme; in `Apply()`, notify + fall back gracefully when the saved name is missing instead of silently rendering Dark.

### Task 2.8: Settings dialog chrome

**Files:** `SettingsViewModel.cs`, `SettingsDialog.axaml`

- [ ] Scope switch (User↔Workspace) → after `LoadFromService` also call `UpdateCategorySettings(...)` + re-run the search filter; today the visible controls keep the old scope's values while writes go to the new scope.
- [ ] JSON editor Save → make `SetRawJsonAsync` return success/errors, await it, surface parse failures in `JsonValidationErrors` (today malformed JSON is silently discarded while the UI implies success).
- [ ] JSON view overlap → gate the search-results ScrollViewer on `IsSearchActive AND !IsJsonEditorActive`; flush pending debounced saves before `GetRawJson`.
- [ ] Command palette "Open Settings (JSON)..." → actually open the JSON view (`vm.IsJsonEditorActive = true` before ShowDialog); today it's a mislabeled duplicate of "Settings...".
- [ ] Help → About: add `Command="{Binding ShowAboutCommand}"` (`MainWindow.axaml:353`; command exists at `MainWindowViewModel.cs:7001`).

### Task 2.9: Keyboard shortcuts surfaces (D4: honest read-only)

**Files:** `KeyboardShortcutsViewModel.cs:48-146`, `SettingsDialog.axaml:313-329`, `SettingsViewModel.cs:1183-1201`

- [ ] Generate the F1 dialog's list from `MainWindow`'s actual `Window.KeyBindings` (gesture + command name) merged with palette metadata — the hand-maintained list has ≥4 wrong entries and omits ~15 real bindings (e.g. shows Ctrl+Shift+F5 = "Run in External Console" when it's actually Restart Debugging).
- [ ] Settings → Keyboard grid: set the Shortcut column `IsReadOnly="True"` and feed it the same generated list (editing is placebo today: resets on reopen, rebinds nothing).

---

## Phase 3 — Regression net + ship

- [ ] Add a **settings-consumer contract test**: for every key in the dialog inventory, assert there is a registered consumer (a simple registry/annotation the wiring tasks populate) — prevents future "persists-but-dead" settings.
- [ ] Full suite green: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`.
- [ ] Manual matrix: Dark/Light/HC × {main window, Settings dialog, one refactoring dialog, floated panel} screenshots; theme change → Cancel → restart cycles.
- [ ] Rebuild + refresh `IDE/` prebuilt binaries; commit per repo convention.

## Explicitly verified working (don't touch)

`editor.trimTrailingWhitespaceOnSave`; DataGrid theme include; Modified filter; JSON Validate / Back to UI; search + `@modified` + clear; per-item Reset; all SettingsDialog static converters exist; Tools menu wiring for both items; command-palette Settings/Keyboard entries.
