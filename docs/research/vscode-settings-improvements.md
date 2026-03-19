# VS Code Settings UI vs VGS IDE Settings -- Comparison and Improvements

## VS Code Settings Editor -- Key UX Features

### 1. Search and Filtering
- **Full-text search bar** at the top of the settings editor; searches setting names, descriptions, and values.
- **`@modified` filter**: Shows only settings whose value differs from the default. Useful for auditing what has been changed.
- **Other `@` filters**: `@tag:`, `@ext:`, `@feature:`, `@id:`, `@lang:` for scoped searching.
- **Search history**: The search bar remembers previous queries and supports undo/redo.
- **Clear button**: One-click button to clear the search input.

### 2. Table of Contents Sidebar
- A **left-hand Table of Contents (ToC)** lists all setting categories (Text Editor, Workbench, Extensions, etc.).
- The ToC **scrolls in sync** with the settings panel -- clicking a category scrolls to it; scrolling the panel highlights the active category.
- The ToC can be configured to **filter** during search (show only categories with matches) or to hide entirely.
- A **"Commonly Used"** group is pinned at the top, surfacing the most popular settings.

### 3. Modified Setting Indicator
- Each modified setting has a **colored bar (blue)** on its left edge, styled like modified-line indicators in the editor gutter.
- This gives an instant visual scan of everything the user has changed from defaults.

### 4. Per-Setting Gear Menu
- Hovering over any setting reveals a **gear icon** that opens a context menu with:
  - **Reset Setting** -- reverts to the default value.
  - **Copy Setting ID** -- e.g., `editor.fontSize`.
  - **Copy JSON Name/Value Pair** -- for pasting into `settings.json`.
  - **Copy Settings URL** -- deep link.

### 5. Setting Descriptions and Enumerations
- Every setting has a **description paragraph** explaining what it does and its impact.
- Enum settings show a **dropdown with labeled options** and descriptions for each value.
- Numeric settings show **input boxes with min/max validation**.
- Some settings render **inline links** to related settings or documentation.

### 6. Scope Tabs (User / Workspace / Folder)
- Tabs at the top switch between **User** (global), **Workspace** (project-level), and **Folder** (multi-root workspace) scopes.
- A setting can be overridden at each scope; the effective value is shown with an indicator of which scope it came from.

### 7. JSON Editing Escape Hatch
- An **"Open Settings (JSON)"** button lets power users edit the raw `settings.json` directly.
- Both UI and JSON views stay in sync.

### 8. Extension Settings
- Extensions contribute settings via `contributes.configuration` in their `package.json`.
- Extension settings appear under an **"Extensions"** category with the extension's display name as a sub-heading.
- The same search, filter, and gear-menu features apply to extension settings.

### 9. Settings Sync
- Built-in **Settings Sync** shares user settings, keybindings, extensions, and UI state across machines via a Microsoft or GitHub account.

---

## VGS IDE Settings Dialog -- Current State

### Architecture
- **ViewModel**: `SettingsViewModel.cs` -- 20 settings across 4 categories, plus 16 keyboard shortcuts.
- **View**: `SettingsDialog.axaml` -- Modal `Window` (800x600), uses `TabControl` with 5 tabs.
- **Storage**: `%APPDATA%/VisualGameStudio/settings.json` (single scope, user-level only).
- **Apply model**: OK/Cancel dialog -- changes are not live until the user clicks OK.

### Current Categories (Tabs)

| Tab | Settings |
|-----|----------|
| **Editor** | Font family, font size, tab size, convert tabs to spaces, show line numbers, highlight current line, show whitespace, word wrap, auto indent, bracket matching, auto close brackets |
| **IntelliSense** | Enable auto complete, show quick info, show signature help, auto complete delay |
| **Build** | Save before build, show build output, default configuration |
| **Appearance** | Color theme (Dark / Light / High Contrast) |
| **Keyboard** | 16 shortcuts in a DataGrid (Action, Current Binding, Default Binding) |

### Strengths
- Clean grouped layout with `Border` sections and bold sub-headings ("Font", "Tabs", "Display", "Behavior").
- `ResetToDefaults` command exists for a full reset.
- `KeyboardShortcut.IsModified` property already tracks per-shortcut modification state.
- Theme changes apply immediately on save via `ThemeManager.Apply()`.
- Static `SettingsChanged` event lets other components react.
- `SettingsData` class is cleanly serializable.

### Weaknesses (compared to VS Code)
- **No search/filter** -- users must manually browse tabs to find a setting.
- **No modified indicator** -- no visual cue showing which settings differ from defaults (except shortcuts have `IsModified` but it is not surfaced in the UI).
- **No per-setting reset** -- only a global "Reset to Defaults" button; no way to reset a single setting.
- **No setting descriptions** -- labels only; no explanatory text for what a setting does.
- **No scope support** -- single user-level scope; no workspace or project-level overrides.
- **Modal dialog** -- settings are in a blocking modal window, not an integrated editor tab.
- **No JSON escape hatch** -- no way to directly edit the raw `settings.json` from the dialog.
- **Limited categories** -- 5 tabs vs VS Code's deep hierarchical ToC with dozens of categories.
- **No "Commonly Used" group** -- new users must explore every tab.
- **No extension/plugin settings** -- no mechanism for plugins to register their own settings.

---

## Proposed Improvements (Prioritized)

### P0 -- High Impact, Moderate Effort

| # | Improvement | Description | Implementation Notes |
|---|-------------|-------------|---------------------|
| 1 | **Search bar** | Add a text input above the TabControl that filters visible settings by name/description match. Hide non-matching sections. | Add a `SearchText` property to ViewModel; each setting needs a `Name` + `Description` string. Filter with LINQ in a computed collection. |
| 2 | **Modified indicators** | Show a colored left-border on each setting that differs from its default. | Store defaults in a dictionary; compare current values on property change. Bind a `Border` left thickness/color to an `IsModified` computed property per setting. |
| 3 | **Per-setting reset** | Add a small "reset" button (or gear menu) next to each setting to revert it individually. | Add a `ResetCommand(string settingName)` relay command. Map setting names to default values. |
| 4 | **Setting descriptions** | Add a gray description `TextBlock` below each setting control explaining its behavior. | Pure XAML change -- add `TextBlock` elements with `Foreground="{DynamicResource IdeSubtleFg}"`. |

### P1 -- Medium Impact, Medium Effort

| # | Improvement | Description | Implementation Notes |
|---|-------------|-------------|---------------------|
| 5 | **`@modified` filter** | Special search token that shows only non-default settings. | Check `SearchText.StartsWith("@modified")` and filter to settings where current != default. |
| 6 | **Table of Contents sidebar** | Replace `TabControl` with a `ListBox` sidebar + `ScrollViewer` content. Sync scroll position with selected category. | Use a `SplitView` or two-column `Grid`. Attach scroll-changed handler to highlight active ToC item. |
| 7 | **Open settings.json button** | Add a toolbar button that opens the raw JSON file in the code editor. | Launch the file via the existing `OpenFile` infrastructure; path is already known (`SettingsPath`). |
| 8 | **Non-modal settings** | Open settings as a document tab instead of a modal dialog. | Create a `SettingsEditorControl` (UserControl) and open it as a tab in the main editor area via `IDocumentService`. |
| 9 | **Live preview** | Apply setting changes immediately as the user modifies them, with an undo on Cancel. | Snapshot current values on dialog open; restore snapshot on Cancel. Fire `SettingsChanged` on each property change. |

### P2 -- Lower Impact, Higher Effort

| # | Improvement | Description | Implementation Notes |
|---|-------------|-------------|---------------------|
| 10 | **Workspace-level settings** | Support a `.vgs/settings.json` in the project root that overrides user settings. | Add a `SettingsScope` enum and merge logic. Show scope tabs (User / Workspace) in the UI. |
| 11 | **Extension settings API** | Let plugins register settings via a `ISettingsContributor` interface. | Define an interface; collect contributions via MEF/DI. Render dynamically in an "Extensions" category. |
| 12 | **Settings sync** | Sync settings across machines via a cloud account or git-tracked file. | Significant infrastructure; defer until user accounts exist. |
| 13 | **Commonly Used group** | Pin the most frequently accessed settings at the top. | Track setting access frequency or curate a static list. Show as a virtual first category. |
| 14 | **Keybinding recorder** | Replace the text-edit DataGrid cell with a "Press a key" recorder widget. | Capture `KeyDown` events in a dedicated control; format as `Ctrl+Shift+X`. |
| 15 | **Import/Export settings** | Add buttons to export settings to a file and import from a file. | Serialize/deserialize `SettingsData` to a user-chosen path. |

---

## Implementation Roadmap

### Phase 1 -- Quick Wins (1-2 days)
- Add setting descriptions (XAML only, #4)
- Add modified indicators (#2)
- Add per-setting reset buttons (#3)

### Phase 2 -- Search (2-3 days)
- Refactor settings into a data-driven model (list of `SettingItem` objects with Name, Description, Category, DefaultValue, CurrentValue, Type)
- Implement search bar (#1) and `@modified` filter (#5)

### Phase 3 -- Layout Overhaul (3-5 days)
- Replace TabControl with ToC sidebar (#6)
- Move to non-modal tab (#8)
- Add live preview (#9)
- Add "Open JSON" button (#7)

### Phase 4 -- Advanced (future)
- Workspace settings (#10)
- Extension settings API (#11)
- Keybinding recorder (#14)
- Import/Export (#15)

---

## Summary

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| Search/filter | Full-text + `@` filters | None | Large |
| Modified indicator | Blue left bar | None (data exists for shortcuts) | Large |
| Per-setting reset | Gear menu > Reset | Global reset only | Medium |
| Setting descriptions | Every setting | None | Medium |
| ToC sidebar | Synced scroll, filterable | Tab strip (5 tabs) | Medium |
| Scope (User/Workspace) | 3 scopes | User only | Medium |
| JSON editing | Built-in toggle | None | Small |
| Non-modal | Editor tab | Modal dialog | Medium |
| Extension settings | Full API | None | Large |
| Settings sync | Built-in | None | Large |
| Commonly Used | Pinned group | None | Small |
| Keybinding UI | Recorder widget | DataGrid text edit | Small |

The VGS IDE settings dialog has a solid foundation with clean code and a reasonable category structure. The highest-value improvements are **search**, **modified indicators**, **per-setting reset**, and **descriptions** -- these four changes would close the largest UX gaps with moderate implementation effort.

---

## Sources

- [VS Code User and Workspace Settings](https://code.visualstudio.com/docs/getstarted/settings)
- [All-new VSCode Settings Editor UI - DEV Community](https://dev.to/vscode/all-new-vscode-settings-editor-ui-----3j48)
- [VS Code Settings UX Guidelines](https://code.visualstudio.com/api/ux-guidelines/settings)
- [Make "show modified" option more prominent - vscode#65214](https://github.com/microsoft/vscode/issues/65214)
- [VS Code August 2018 (v1.27) - Settings Editor](https://code.visualstudio.com/updates/v1_27)
- [VS Code Configure Settings docs](https://code.visualstudio.com/docs/configure/settings)
