# VS Code UI/UX Patterns and Layout Research

> Research date: March 2026
> Purpose: Reference for Visual Game Studio IDE development

---

## 1. Main Layout Architecture

VS Code's workbench is organized into five primary regions, from outside in:

```
+---+-------------------------+---+
| A |       Editor Area       | A |
| c |  (tabs + breadcrumbs +  | u |
| t |   editor groups)        | x |
| i |                         |   |
| v | Primary    | Secondary  | S |
| i | Sidebar    | Sidebar    | i |
| t |            |            | d |
| y |            |            | e |
|   |            |            | b |
| B |            |            | a |
| a |            |            | r |
| r |            |            |   |
+---+---+--------+-----------+---+
|       Panel (Terminal, Output)  |
+--+------------------------------+
|          Status Bar              |
+---------------------------------+
```

### 1.1 Activity Bar

- **Position**: Far left by default; can be moved to top, bottom, or hidden entirely via `View > Appearance > Activity Bar Position`.
- **Purpose**: Primary navigation between view containers (Explorer, Search, Source Control, Run/Debug, Extensions).
- **Behavior**:
  - Clicking an icon opens/toggles the corresponding Primary Sidebar view container.
  - Icons can be reordered via drag-and-drop.
  - Right-click context menu lets you show/hide individual views.
  - When too many icons are present, overflow items are collapsed into a `...` ellipsis menu.
  - Badge counts (blue/red circles with numbers) appear over icons to indicate actionable items (e.g., pending source control changes, extension updates).
- **Sizes**: Default (large) and Compact mode (`workbench.activityBar.compact`).
- **Special items**: Account and Manage (gear) buttons are pinned at the bottom. When Activity Bar is positioned at top/bottom, these buttons move to the right side of the title bar.

### 1.2 Primary Sidebar

- **Position**: Left of the editor area by default; can be toggled to the right.
- **Toggle**: `Ctrl+B` to show/hide.
- **Contents**: View containers contributed by the Activity Bar items -- Explorer, Search, Source Control, Run and Debug, Extensions, plus any extension-contributed containers.
- **View management**: Individual views within a container can be collapsed, reordered, or dragged to the Panel or Secondary Sidebar.
- **Width**: User-resizable via drag handle.

### 1.3 Secondary Sidebar (Auxiliary Bar)

- **Position**: Always opposite the Primary Sidebar (if Primary is left, Secondary is right).
- **Toggle**: `Ctrl+Alt+B` or via View menu.
- **Purpose**: Provides a second surface for views -- users can drag Terminal, Problems, Outline, or any other view here.
- **Default visibility**: Shown by default when opening a folder/workspace; hidden in empty windows. Configurable via `workbench.secondarySideBar.defaultVisibility`.

### 1.4 Editor Area

- **Central region**: Occupies the main workspace area.
- **Contains**: One or more Editor Groups, each with its own tab bar, breadcrumbs, and editor content.
- **Supports**: Text editors, custom editors (contributed by extensions), webview panels, diff editors, settings UI, keyboard shortcuts editor.

### 1.5 Panel

- **Position**: Below the editor area by default; can be moved to left, right, or bottom via `View > Appearance > Panel Position`.
- **Toggle**: `` Ctrl+` `` (for Terminal) or `Ctrl+J` (for Panel).
- **Default contents**: Terminal, Problems, Output, Debug Console.
- **Behavior**: Views can be dragged between Panel, Primary Sidebar, and Secondary Sidebar.
- **Tabs**: Panel views appear as tabs; only one is visible at a time within a tab group, but multiple tab groups can be created side by side within the Panel.

### 1.6 Status Bar

- **Position**: Bottom of the window, full width.
- **Organization**: Two logical groups:
  - **Left side**: Workspace-scoped items -- Git branch, sync status, errors/warnings count, running tasks.
  - **Right side**: Active-file-scoped items -- cursor position (Ln/Col), indentation (spaces/tabs), encoding (UTF-8), end-of-line sequence (LF/CRLF), language mode, notifications bell, feedback.
- **Colors**: Background color changes contextually (e.g., blue/purple for a workspace, orange during debugging, no-folder mode has different color).
- **Interactivity** (see Section 6 for details): Nearly every segment is clickable.

### 1.7 Layout Persistence

- VS Code remembers view positions, panel arrangement, sidebar widths, and editor group layout across sessions.
- `View: Reset View Locations` command restores default layout.

---

## 2. Tab Management

### 2.1 Editor Groups

- The editor area can contain multiple **Editor Groups** arranged in a grid layout.
- Split actions: `Ctrl+\` splits the active editor into a new group to the right. Context menu offers split left, right, up, down.
- Drag-and-drop: Tabs can be dragged between groups; dragging to an edge of the editor area creates a new group.
- Tabs can also be dragged out of groups entirely or merged.
- Groups can be resized by dragging the separator between them.

### 2.2 Tab Types

| Tab State | Visual Indicator | Behavior |
|-----------|-----------------|----------|
| **Preview** | Italic title text | Single-clicking a file in Explorer opens in preview mode; the tab is reused when another file is previewed. Double-click or editing the file promotes it to a kept tab. |
| **Kept (Open)** | Normal title text | Persists until explicitly closed. |
| **Pinned** | Shows only icon (or first letter if no icon); stays at left edge of tab bar | `Ctrl+K Shift+Enter` to pin. `Ctrl+W` skips pinned tabs. Can show on a separate row via `workbench.editor.pinnedTabsOnSeparateRow`. |
| **Dirty (Modified)** | Dot indicator on the close button (or highlighted tab if `workbench.editor.highlightModifiedTabs` is true) | Indicates unsaved changes. |

### 2.3 Tab Sizing Modes

- `workbench.editor.tabSizing`:
  - **fit** -- Tabs grow to fit their label; scrollbar appears when tabs overflow.
  - **shrink** -- Tabs shrink to show partial labels when space is limited.
  - **fixed** -- All tabs have the same width.
- Pinned tab sizing (`workbench.editor.pinnedTabSizing`):
  - **normal** -- Same as other tabs.
  - **shrink** -- Smaller, showing partial label.
  - **compact** -- Icon-only (or first letter).

### 2.4 Tab Overflow

- When tabs exceed available width, a horizontal scrollbar appears between the tab row and editor content.
- `workbench.editor.titleScrollbarSizing` can be set to `large` for easier dragging.
- The `...` menu at the right end of the tab bar lists all open editors.
- **Open Editors** section in the Explorer sidebar provides an alternative view of all open files.

### 2.5 Wrapped Tabs

- `workbench.editor.wrapTabs: true` causes tabs to wrap into multiple rows instead of scrolling.
- Pinned tabs can be placed on their own row above wrapped tabs.

### 2.6 Tab Close Behavior

- `workbench.editor.tabCloseButton` -- Position: left, right, or off.
- `Ctrl+W` closes the active tab (skips pinned tabs unless using `closeActivePinnedEditor` command).
- `Ctrl+K Ctrl+W` closes all tabs in the active group.
- Middle-click on a tab closes it.

---

## 3. Command Palette

### 3.1 Activation

- **Shortcut**: `Ctrl+Shift+P` (or `F1`).
- Opens a dropdown input at the top center of the editor area.
- Prefixed with `>` to indicate command mode.

### 3.2 Search Behavior

- **Fuzzy matching**: Typing fragments of a command name matches commands containing those fragments in order (e.g., "tog mini" matches "View: Toggle Minimap").
- **Sorting**: Results are sorted by name to keep the list stable and memorable, rather than by fuzzy-match relevance score. Recently used commands are boosted to the top.
- **Filtering**: Only matching commands are shown as you type.

### 3.3 Entry Display

Each entry in the Command Palette shows:
- **Command name** (e.g., "View: Toggle Minimap")
- **Keybinding** (right-aligned, e.g., `Ctrl+Shift+M`) if one is assigned
- **Category prefix** before the colon (e.g., "View:", "File:", "Editor:")

### 3.4 Prefix Modes

The Command Palette input supports special prefixes that change its behavior:

| Prefix | Mode | Example |
|--------|------|---------|
| `>` | Command search (default when opened with `Ctrl+Shift+P`) | `> toggle minimap` |
| (none) | File search / Quick Open (default when opened with `Ctrl+P`) | `main.ts` |
| `@` | Go to Symbol in current file | `@myFunction` |
| `@:` | Go to Symbol by category | `@:function` |
| `#` | Go to Symbol in workspace | `#MyClass` |
| `:` | Go to Line | `:42` |
| `?` | Help / list available prefixes | `?` |

### 3.5 Recent Commands

- Most recently used commands appear at the top of the list when the palette is first opened.
- The list updates dynamically as you use commands.

---

## 4. Quick Open (Ctrl+P)

### 4.1 Activation

- **Shortcut**: `Ctrl+P`.
- Opens the same dropdown input as Command Palette but without the `>` prefix.

### 4.2 File Search

- Fuzzy, case-insensitive matching against all file paths in the workspace.
- Results update as you type with the best matches ranked at the top.
- Recently opened files appear first when the input is empty.

### 4.3 Navigation

- `Enter` opens the selected file (replaces current preview tab, or opens in a new tab if preview is disabled).
- `Right Arrow` opens the file in the background and keeps Quick Open open for further selection.
- `Ctrl+P` pressed repeatedly cycles through recently opened files.
- `workbench.editor.enablePreviewFromQuickOpen` controls whether files open in preview or kept mode.

### 4.4 Extended Quick Open

- Typing a prefix character (`>`, `@`, `#`, `:`) switches mode (see Section 3.4).
- Extensions can contribute custom Quick Open providers.

---

## 5. Breadcrumbs Navigation

### 5.1 Location

- Displayed as a horizontal bar between the tab row and the editor content.
- Shows the path from workspace root to the current file, plus the symbol hierarchy at the cursor position.

### 5.2 Structure

```
workspace-root > folder > subfolder > filename.ext > Class > Method
 \_____________ file path ___________/ \____ symbol path ____/
```

### 5.3 Interaction

- **Click a segment**: Opens a dropdown showing siblings at that level (folders, files, or symbols).
- **Dropdown navigation**: Type to filter; best match is highlighted. Arrow keys navigate; Enter selects.
- **Keyboard activation**: `Ctrl+Shift+.` focuses breadcrumbs. `Ctrl+Shift+;` focuses the last (symbol) segment.
- Arrow keys navigate between breadcrumb segments while focused.

### 5.4 Configuration

| Setting | Options | Description |
|---------|---------|-------------|
| `breadcrumbs.enabled` | true/false | Show/hide breadcrumbs |
| `breadcrumbs.filePath` | on/off/last | Show full path, none, or only last segment |
| `breadcrumbs.symbolPath` | on/off/last | Show full symbol path, none, or only last |
| `breadcrumbs.icons` | true/false | Show file/symbol icons |
| `breadcrumbs.symbolSortOrder` | name/type/position | Order of symbols in dropdown |

### 5.5 Symbol Source

- Symbols shown in breadcrumbs come from the same provider as the **Outline view** and **Go to Symbol** (`Ctrl+Shift+O`).
- Requires a language extension that provides `DocumentSymbolProvider`.

---

## 6. Status Bar Segments and Click Actions

### 6.1 Left-Side Items (Workspace Scope)

| Segment | Click Action |
|---------|-------------|
| **Git branch name** | Opens branch picker / checkout dialog |
| **Sync indicator** (arrows) | Triggers Git sync (pull/push) |
| **Errors and Warnings** (icons + counts) | Opens the Problems panel (`Ctrl+Shift+M`) |
| **Running tasks** | Shows task output |

### 6.2 Right-Side Items (File Scope)

| Segment | Click Action |
|---------|-------------|
| **Line:Column** (e.g., `Ln 42, Col 15`) | Opens "Go to Line" dialog (`Ctrl+G`) |
| **Spaces/Tab Size** (e.g., `Spaces: 4`) | Opens indentation options (change indentation, convert tabs/spaces) |
| **Encoding** (e.g., `UTF-8`) | Opens encoding picker (reopen or save with different encoding) |
| **EOL** (e.g., `LF`) | Toggles between LF and CRLF |
| **Language Mode** (e.g., `TypeScript`) | Opens language mode picker to change syntax highlighting |
| **Feedback** (smiley icon) | Opens feedback/tweet dialog |
| **Notifications** (bell icon) | Opens Notification Center |

### 6.3 Extension-Contributed Items

- Extensions can add custom status bar items with `StatusBarItem` API.
- Items specify alignment (left/right) and priority (controls ordering).
- Items can have tooltips, click commands, background colors, and icons.
- Background color can be set to `statusBarItem.errorBackground` or `statusBarItem.warningBackground` for urgency.

### 6.4 Contextual Color Changes

| Context | Status Bar Color |
|---------|-----------------|
| Normal workspace | Default theme color (typically blue/purple) |
| No folder open | Purple/magenta tint |
| Debugging active | Orange |
| Remote connection | Green |

---

## 7. Notifications and Toasts

### 7.1 Toast Notifications

- Appear in the **bottom-right corner** of the window (can be configured to top-right).
- Auto-dismiss after a timeout for informational messages without action buttons.
- Notifications with action buttons persist until dismissed or acted upon.

### 7.2 Severity Levels

| Level | API Method | Visual Style |
|-------|-----------|-------------|
| Information | `showInformationMessage()` | Blue/default icon |
| Warning | `showWarningMessage()` | Yellow/warning icon |
| Error | `showErrorMessage()` | Red/error icon |

### 7.3 Progress Notifications

- Used for long-running operations (e.g., indexing, installing extensions).
- Show a progress bar within the notification toast.
- Can be cancellable (shows a cancel button).
- Recommended as a last resort -- prefer inline progress in views or editors.
- API: `window.withProgress()` with `ProgressLocation.Notification`.

### 7.4 Notification Center

- Accessed via the **bell icon** in the Status Bar.
- Lists all past notifications (including dismissed ones) for the current session.
- **Do Not Disturb mode**: Toggle via the bell icon context menu. Hides all non-error toast pop-ups; errors still appear. Notifications remain accessible in the Notification Center.
- Per-extension notification control: Can selectively disable notifications from specific extensions.

### 7.5 Source Identification

- Each notification can show its source (e.g., the extension name that generated it).
- Users can click the source to manage that extension.

---

## 8. Settings UI

### 8.1 Opening

- **Shortcut**: `Ctrl+,`
- **Command**: `Preferences: Open Settings (UI)`
- Opens as an editor tab (not a dialog).

### 8.2 Scope Tabs

Tabs along the top of the Settings editor:

| Tab | Scope | Storage |
|-----|-------|---------|
| **User** | Global (all workspaces) | `%APPDATA%/Code/User/settings.json` |
| **Workspace** | Current workspace only | `.vscode/settings.json` |
| **Folder** (multi-root only) | Specific folder in workspace | `.vscode/settings.json` in that folder |

### 8.3 Search

- Full-text search bar at the top.
- Filters results in real time as you type.
- The table of contents (left-side category tree) filters to show only categories with matches during search.
- Clicking a category in the filtered tree scrolls to and isolates those results.

### 8.4 Search Filters

Special filter tokens (accessible via the funnel icon):

| Filter | Purpose |
|--------|---------|
| `@modified` | Shows only settings that differ from defaults |
| `@tag:language` | Language-specific settings |
| `@tag:preview` | Preview/experimental features |
| `@ext:<extension-id>` | Settings from a specific extension |
| `@feature:<feature>` | Settings for a specific feature area |
| `@id:<setting-id>` | Find a setting by its exact ID |

### 8.5 Modified Indicator

- Settings that have been changed from their default value show a **colored bar on the left edge** (similar to modified-line indicators in the editor gutter).
- The `@modified` filter collects all such settings.

### 8.6 Setting Controls

Settings render as appropriate UI controls:

| Setting Type | Control |
|-------------|---------|
| Boolean | Checkbox |
| String | Text input |
| Enum | Dropdown select |
| Number | Number input |
| Array | Editable list with Add/Remove |
| Object | JSON editor or structured form |

### 8.7 Gear Icon (Per-Setting)

Each setting has a gear icon that opens a context menu with:
- **Reset Setting** -- Restore to default value
- **Copy Setting ID** -- e.g., `editor.fontSize`
- **Copy Setting as JSON** -- e.g., `"editor.fontSize": 14`
- **Copy Settings URL** -- Deep link to the setting

### 8.8 JSON Editing

- `Preferences: Open Settings (JSON)` opens the raw `settings.json` file.
- A link in the Settings UI header switches to JSON mode.
- JSON mode provides IntelliSense for setting names and values.

---

## 9. Keyboard Shortcut Editor

### 9.1 Opening

- **Shortcut**: `Ctrl+K Ctrl+S`
- **Command**: `Preferences: Open Keyboard Shortcuts`
- Opens as an editor tab with a searchable table.

### 9.2 Table Columns

| Column | Content |
|--------|---------|
| **Command** | Human-readable command name |
| **Keybinding** | Current key combination (editable) |
| **When** | Context condition (when clause) for the binding |
| **Source** | Where binding is defined (Default, User, Extension) |

### 9.3 Search

- Search by command name (text) or by key combination.
- **Record Keys** mode: Click the keyboard icon in the search bar, then press keys to search for bindings that match that combination.

### 9.4 Editing

- **Double-click** a keybinding cell to record a new shortcut.
- **Right-click** context menu options:
  - **Change Keybinding** -- Record a new key combination
  - **Change When Expression** -- Edit the context condition
  - **Remove Keybinding** -- Delete the custom binding
  - **Reset Keybinding** -- Restore to default
  - **Copy Command ID** -- Copy the internal command identifier
  - **Copy Command Title** -- Copy the display name
- Conflicts are shown when two commands share the same keybinding.

### 9.5 When Clauses

- Boolean expressions that control when a keybinding is active.
- Common contexts: `editorTextFocus`, `editorHasSelection`, `terminalFocus`, `inDebugMode`, `resourceExtname == '.bas'`.
- Supports operators: `==`, `!=`, `&&`, `||`, `!`, `=~` (regex), `in`.

### 9.6 JSON Editing

- `Preferences: Open Keyboard Shortcuts (JSON)` opens `keybindings.json`.
- Provides IntelliSense for command IDs and when-clause contexts.

---

## 10. Theme System

### 10.1 Color Themes

- **Purpose**: Control the colors of the entire UI and editor syntax highlighting.
- **Categories**: Light, Dark, High Contrast Light, High Contrast Dark.
- **Activation**: `Ctrl+K Ctrl+T` opens the theme picker; preview on hover before committing.
- **Structure**: A color theme is a JSON mapping from:
  - **Workbench color IDs** (e.g., `activityBar.background`, `statusBar.foreground`) -- 700+ customizable color keys.
  - **TextMate token scopes** (e.g., `keyword.control`, `string.quoted`) to foreground color, font style.
  - **Semantic token types** (e.g., `function.declaration`, `variable.readonly`) for language-aware coloring.
- **User overrides**: `workbench.colorCustomizations` and `editor.tokenColorCustomizations` in `settings.json` allow per-theme overrides without creating a new theme.

### 10.2 File Icon Themes

- **Purpose**: Assign icons to file types, folder types, and specific file names in Explorer, tabs, and Quick Open.
- **Activation**: `File > Preferences > File Icon Theme` or via Command Palette.
- **Built-in**: Seti (default), Minimal; many community themes available (Material Icon Theme, vscode-icons, etc.).
- **Structure**: JSON mapping from file extensions, file names, folder names, and language IDs to icon SVG/PNG files.

### 10.3 Product Icon Themes

- **Purpose**: Replace the icons used throughout the VS Code UI itself (Activity Bar icons, status bar icons, editor gutter icons, etc.) -- distinct from file icons.
- **Activation**: `Preferences: Product Icon Theme` command.
- **Structure**: Maps product icon IDs (from the [Product Icon Reference](https://code.visualstudio.com/api/references/icons-in-labels)) to custom font glyphs.
- **Default**: Codicon font (Microsoft's icon font for VS Code).

### 10.4 Theme Scope Summary

| Theme Type | What It Controls | File Format |
|-----------|-----------------|-------------|
| Color Theme | UI colors + syntax colors | JSON (`*.color-theme.json`) |
| File Icon Theme | File/folder icons | JSON (`*.icon-theme.json`) |
| Product Icon Theme | UI chrome icons | JSON (`*.product-icon-theme.json`) |

---

## 11. Zen Mode and Focus Mode

### 11.1 Zen Mode

- **Activation**: `Ctrl+K Z` or `View > Appearance > Zen Mode`.
- **Exit**: Double-press `Esc`.
- **What it does**:
  - Enters full screen.
  - Hides Activity Bar, Sidebar, Panel, Status Bar, and tab bar.
  - Centers the editor (Centered Layout).
  - Suppresses non-error notifications.
- **Configurable settings**:

| Setting | Default | Description |
|---------|---------|-------------|
| `zenMode.fullScreen` | true | Enter full screen |
| `zenMode.centerLayout` | true | Center the editor |
| `zenMode.hideActivityBar` | true | Hide Activity Bar |
| `zenMode.hideStatusBar` | true | Hide Status Bar |
| `zenMode.hideLineNumbers` | true | Hide line numbers |
| `zenMode.showTabs` | none | Show multiple, single, or no tabs |
| `zenMode.silentNotifications` | true | Suppress non-error notifications |
| `zenMode.restore` | true | Restore Zen Mode on next launch if active when closed |

### 11.2 Centered Layout

- **Activation**: `View: Toggle Centered Layout` command (can be used independently of Zen Mode).
- Centers the editor content in the viewport with margins on both sides.
- Margins are resizable by dragging the sash handles.
- Useful on wide monitors when working with a single file.

### 11.3 Focus Mode (Focused View)

- Individual views (e.g., Explorer, Terminal) can be maximized to fill the entire workbench area.
- Right-click a view title and select "Focus View" to expand it.
- Escape returns to normal layout.

---

## 12. Minimap

### 12.1 Position and Display

- Shown on the **right edge** of the editor by default.
- Can be moved to the left via `editor.minimap.side: "left"`.
- Disabled entirely with `editor.minimap.enabled: false`.

### 12.2 Rendering Modes

| Setting | Value | Description |
|---------|-------|-------------|
| `editor.minimap.renderCharacters` | true | Renders actual (tiny) characters |
| `editor.minimap.renderCharacters` | false | Renders colored blocks representing code density |

### 12.3 Size and Scale

| Setting | Options | Description |
|---------|---------|-------------|
| `editor.minimap.scale` | 1, 2, 3 | Scale factor for minimap content |
| `editor.minimap.size` | proportional / fill / fit | How minimap height relates to editor height |
| `editor.minimap.maxColumn` | number | Maximum columns rendered (limits width) |

- **proportional**: Minimap height is proportional to file length (may scroll independently).
- **fill**: Minimap stretches to fill editor height.
- **fit**: Minimap shrinks to fit within editor height (no minimap scrolling).

### 12.4 Autohide

- `editor.minimap.autohide: true` hides the minimap until the mouse hovers over the minimap area.
- Saves horizontal space when not actively navigating.

### 12.5 Interaction

- **Click**: Clicking on the minimap scrolls the editor to that position.
- **Drag**: Dragging the translucent viewport slider scrolls the editor proportionally.
- **Viewport highlight**: A translucent overlay shows the currently visible portion of the file. Visible when hovering or interacting with the minimap.
- **Slider visibility**: `editor.minimap.showSlider` -- always / mouseover.

### 12.6 Decorations

- Search highlights, errors, warnings, and git changes are shown as colored indicators in the minimap.
- Folding region markers (e.g., `//#region`) display their names in the minimap.
- Selection highlights are rendered in the minimap to show where selected text appears in the document.

---

## Summary Table: Key Shortcuts

| Feature | Shortcut (Windows/Linux) |
|---------|-------------------------|
| Command Palette | `Ctrl+Shift+P` or `F1` |
| Quick Open (file) | `Ctrl+P` |
| Go to Symbol (file) | `Ctrl+Shift+O` |
| Go to Symbol (workspace) | `Ctrl+T` |
| Go to Line | `Ctrl+G` |
| Toggle Sidebar | `Ctrl+B` |
| Toggle Secondary Sidebar | `Ctrl+Alt+B` |
| Toggle Panel | `Ctrl+J` |
| Toggle Terminal | `` Ctrl+` `` |
| Split Editor | `Ctrl+\` |
| Zen Mode | `Ctrl+K Z` |
| Settings | `Ctrl+,` |
| Keyboard Shortcuts | `Ctrl+K Ctrl+S` |
| Theme Picker | `Ctrl+K Ctrl+T` |
| Breadcrumbs Focus | `Ctrl+Shift+.` |
| Pin Tab | `Ctrl+K Shift+Enter` |

---

## Sources

- [VS Code User Interface](https://code.visualstudio.com/docs/getstarted/userinterface)
- [VS Code Custom Layout](https://code.visualstudio.com/docs/configure/custom-layout)
- [VS Code UX Guidelines Overview](https://code.visualstudio.com/api/ux-guidelines/overview)
- [Activity Bar UX Guidelines](https://code.visualstudio.com/api/ux-guidelines/activity-bar)
- [Sidebars UX Guidelines](https://code.visualstudio.com/api/ux-guidelines/sidebars)
- [Status Bar UX Guidelines](https://code.visualstudio.com/api/ux-guidelines/status-bar)
- [Notifications UX Guidelines](https://code.visualstudio.com/api/ux-guidelines/notifications)
- [Extending Workbench](https://code.visualstudio.com/api/extension-capabilities/extending-workbench)
- [VS Code Code Navigation](https://code.visualstudio.com/docs/editing/editingevolved)
- [VS Code Keyboard Shortcuts](https://code.visualstudio.com/docs/configure/keybindings)
- [VS Code Settings](https://code.visualstudio.com/docs/getstarted/settings)
- [VS Code Themes](https://code.visualstudio.com/docs/configure/themes)
- [VS Code Theming API](https://code.visualstudio.com/api/extension-capabilities/theming)
- [Theme Color Reference](https://code.visualstudio.com/api/references/theme-color)
- [Product Icon Theme Guide](https://code.visualstudio.com/api/extension-guides/product-icon-theme)
- [Color Theme Guide](https://code.visualstudio.com/api/extension-guides/color-theme)
- [When Clause Contexts](https://code.visualstudio.com/api/references/when-clause-contexts)
- [VS Code Tips and Tricks](https://code.visualstudio.com/docs/getstarted/tips-and-tricks)
- [VS Code Breadcrumbs Announcement (v1.26)](https://code.visualstudio.com/updates/v1_26)
- [VS Code Settings Editor UI (v1.27)](https://code.visualstudio.com/updates/v1_27)
