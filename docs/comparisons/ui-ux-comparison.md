# UI/UX Comparison: Visual Game Studio IDE vs VS Code

This document compares the Visual Game Studio (VGS) IDE's user interface and interaction patterns against Visual Studio Code (VS Code 1.96+), identifying feature parity, differences, and gaps.

---

## Summary Scorecard

| Feature | VS Code | VGS IDE | Parity |
|---------|---------|---------|--------|
| Layout (sidebar, panels, editor) | Full | Partial | 70% |
| Tab management | Full | Basic | 50% |
| Command palette | Full | Implemented | 75% |
| Quick open (Ctrl+P) | Full | Implemented | 65% |
| Status bar | Full | Implemented | 80% |
| Breadcrumbs | Full | Implemented | 60% |
| Notifications | Full | Missing | 5% |
| Settings UI | Full (JSON + GUI) | GUI only | 55% |
| Theme support | Full (marketplace) | Minimal | 20% |
| Keyboard shortcuts | Full (keybindings.json) | Partial | 60% |
| Menu system | Full | Full | 90% |
| Context menus | Full | Full | 85% |
| Drag and drop | Full | Missing | 0% |
| Zen mode | Full | Missing | 0% |

**Overall UI/UX Parity: ~51%**

---

## 1. Layout (Sidebar, Panels, Editor Area)

### VS Code
- **Activity Bar** (far left): Icon-only vertical bar for switching between sidebar views (Explorer, Search, Source Control, Debug, Extensions).
- **Primary Sidebar** (left): Collapsible panel showing the active view (file tree, search results, git changes, etc.). Can be moved to the right.
- **Secondary Sidebar** (right, optional): A second sidebar for additional views (added in VS Code 1.64).
- **Editor Group**: Central area with tabbed editors, supports split into unlimited groups horizontally/vertically.
- **Panel** (bottom): Collapsible area for Terminal, Output, Problems, Debug Console. Can be moved to side.
- **Minimap**: Scrollbar-adjacent code overview in every editor.

### VGS IDE
- **No Activity Bar**: There is no icon-based activity bar. Sidebar views are accessed through tabs within the left dock.
- **Left Dock (20% width)**: Single ToolDock containing Solution Explorer, Git Changes, Git Branches, Git Stash, Git Blame, Document Outline, and Bookmarks as tabs. Functions like a combined sidebar.
- **Document Area (60%)**: Central document dock with tabbed editors. Supports split view (horizontal and vertical) within a single document, but not editor group splitting across documents.
- **Bottom Dock (35%)**: Split into two ToolDock groups side by side:
  - Left group: Output, Error List, Terminal, Find in Files
  - Right group: Call Stack, Variables, Breakpoints, Watch, Immediate Window
- **No Minimap**: Not implemented.
- **Dock.Avalonia**: Uses the Dock.Avalonia library for docking, which provides tab dragging and rearranging within dock groups.

### Gaps
- No Activity Bar for quick view switching; must click tabs in the left dock.
- No secondary sidebar.
- No minimap.
- Split view is per-document only (horizontal/vertical within one file), not multi-document side-by-side editor groups.
- Panel cannot be moved to the side.
- No panel maximize/minimize toggle.

---

## 2. Tab Management

### VS Code
- Tabs for open editors with modified indicator (dot).
- Tab reordering via drag and drop.
- Tab pinning (pinned tabs stay left, show with a pin icon).
- Tab previews (single-click opens preview tab, double-click opens persistent tab).
- Tab wrapping (multiple rows) or scrolling when many tabs are open.
- Split editor from tab context menu.
- Close all / close others / close to the right.
- Tab groups (editor groups) for side-by-side editing.

### VGS IDE
- Tabs managed by Dock.Avalonia's DocumentDock.
- Modified indicator tracked by `CodeEditorDocumentViewModel` (Title shows asterisk).
- Tab switching via click.
- Close tab supported via DockFactory's `CloseDockable`.
- Document title updates on save/modify.

### Gaps
- No tab pinning.
- No preview tabs (every open is persistent).
- No tab reordering via drag and drop (Dock.Avalonia may support basic reordering within a dock group, but not across groups).
- No close others / close to the right / close all from tab context menu.
- No tab wrapping or scrolling controls.
- No split editor from tab (split is per-document only).

---

## 3. Command Palette

### VS Code
- **Ctrl+Shift+P** opens Command Palette.
- Prefixes: `>` for commands (default), `@` for symbols in file, `#` for workspace symbols, `:` for go to line, no prefix for file search.
- Recently used commands bubble to top.
- Extensions can register commands.
- Context-aware command availability (disabled commands hidden or grayed).

### VGS IDE
- **Ctrl+Shift+P** opens Command Palette (`CommandPaletteDialog`).
- Overlay dialog with acrylic blur, rounded corners, VS Code-like styling.
- `>` prefix shown in search box (visual only, not functional as mode switch).
- Fuzzy matching with scoring: exact substring match scores 100+, sequential character match with consecutive bonuses.
- Commands organized by category: File, Edit, View, Build, Debug, Bookmarks, Selection.
- 50+ registered commands with keyboard shortcut display.
- Hint bar showing navigation instructions (Up/Down, Enter, Esc).
- Limited to 50 results displayed.

### Gaps
- No prefix-based mode switching within the palette (typing `@` does not switch to symbol search, typing `:` does not go to line). These modes exist in QuickOpen instead but are separate dialogs.
- No recently-used command sorting/pinning.
- No extension/plugin command registration.
- No context-aware command enable/disable (all commands shown regardless of state).
- Command Palette and Quick Open are separate dialogs rather than unified like VS Code's.

---

## 4. Quick Open (Ctrl+P)

### VS Code
- **Ctrl+P** opens Quick Open for file search.
- Prefix `@` for symbols, `#` for workspace symbols, `:` for go to line, `>` for commands.
- MRU (most recently used) files shown by default.
- File path fuzzy matching.
- Preview on hover / selection.

### VGS IDE
- **QuickOpenDialog** implemented with modes: Files, Symbols, Commands, All.
- Supports `@` prefix to switch to symbol mode, `>` prefix to switch to command mode.
- Fuzzy matching with scoring (same algorithm as Command Palette).
- File icons: emoji-based (document, package icons).
- Symbol parsing: regex-based extraction of Module, Class, Sub, Function, Property from `.bas` files.
- Results limited to 50 items.

### Gaps
- No `:` prefix for go-to-line (separate GoToLineDialog exists).
- No MRU file list on empty search.
- No file preview on selection.
- Symbol parsing is basic regex, not LSP-based (does not use `workspace/symbol` request).
- No keyboard shortcut assigned by default (QuickOpen is not bound to Ctrl+P in MainWindow.axaml key bindings).

---

## 5. Status Bar

### VS Code
- Left: Branch name, sync status, errors/warnings count.
- Right: Line/column, spaces/tabs, encoding, line ending (CRLF/LF), language mode, feedback, notifications.
- Clickable elements open pickers or relevant panels.
- Color changes based on context (blue = normal, orange = debugging, purple = remote).
- Extensions can add status bar items.

### VGS IDE
- **Left**: Status message text (build status, "Ready", etc.).
- **Right (left to right)**: Build configuration (Debug/Release), Ln/Col position, indentation indicator (clickable, cycles through Spaces 2/4/8 and Tabs), encoding indicator (clickable, cycles through UTF-8/UTF-16/ASCII), line ending indicator (clickable, toggles CRLF/LF), language mode (clickable), Ctrl+Shift+P hint.
- Blue background (`#007ACC`) matching VS Code's default.
- `StatusBarViewModel` handles file-based language detection, line ending detection, encoding cycling, indentation cycling.
- Clickable indicators with cursor change and tooltips.

### Gaps
- No git branch name in status bar.
- No error/warning count display.
- No color change for debugging state (always blue).
- No extension/plugin status bar items.
- No sync/remote status indicators.
- Encoding and line ending changes are cycle-based (click to advance) rather than picker-based.

---

## 6. Breadcrumbs

### VS Code
- Navigation bar above each editor showing file path and symbol hierarchy.
- Click any segment to open a dropdown for navigation.
- Shows file path segments and code symbols (class > method > block).
- Can be configured to show file path, symbols, or both.

### VGS IDE
- `BreadcrumbControl` implemented from `VisualGameStudio.Editor.Controls`.
- Displayed between Find/Replace bar and editor content (Grid.Row=1 in CodeEditorDocumentView).
- Supports `ItemClicked` event for navigation.

### Gaps
- Implementation details of BreadcrumbControl would need further investigation, but it exists as a custom control in the Editor project.
- Likely simpler than VS Code's (no dropdown pickers for each segment, no symbol hierarchy beyond basic parsing).
- No configuration options for what to display.

---

## 7. Notifications

### VS Code
- Toast notifications in bottom-right corner.
- Notification center (bell icon) collects all notifications.
- Types: information, warning, error.
- Notifications can have action buttons.
- Extension notifications (update available, recommendation, etc.).
- Progress notifications (build progress, download progress).

### VGS IDE
- **Not implemented**. The word "notification" appears only in code comments about LSP text change notifications, not UI notifications.
- Status changes are communicated via the status bar text and the Output panel.
- Build results show in status bar and Error List panel.
- No toast notifications, no notification center, no progress indicators outside the status bar.

### Gaps
- No toast notification system.
- No notification center / history.
- No progress notification overlays.
- No action buttons on notifications.
- Critical gap: users cannot receive passive alerts about background events (LSP errors, extension updates, etc.).

---

## 8. Settings UI

### VS Code
- **JSON-based settings** (`settings.json`): Primary settings mechanism with IntelliSense.
- **Settings GUI** (Ctrl+,): Searchable graphical settings editor.
- User settings vs Workspace settings (scoping).
- Settings sync across devices.
- Extension settings automatically integrated.
- Default settings reference viewer.

### VGS IDE
- **GUI dialog** (`SettingsDialog`): Tabbed window with 5 tabs:
  - **Editor**: Font family (8 monospace fonts), font size, tab size, spaces/tabs, display options (line numbers, current line highlight, whitespace, word wrap), behavior (auto indent, bracket matching, auto close brackets).
  - **IntelliSense**: Auto complete toggle, quick info, signature help, delay (ms).
  - **Build**: Save before build, show build output, default configuration.
  - **Appearance**: Theme selection (Dark, Light, High Contrast).
  - **Keyboard**: DataGrid showing action, current shortcut, default shortcut (editable).
- Persisted as JSON at `%APPDATA%\VisualGameStudio\settings.json`.
- Reset to Defaults button.
- `SettingsChanged` static event for live updates.

### Gaps
- No raw JSON editor for settings (cannot edit settings.json directly with IntelliSense).
- No search/filter within settings UI.
- No workspace-level settings (only global).
- No settings sync.
- No extension settings integration.
- No default settings reference.
- Settings dialog is modal (blocks interaction with IDE while open).

---

## 9. Theme Support

### VS Code
- 15+ built-in color themes (dark, light, high contrast).
- Thousands of marketplace themes.
- Full token-level customization via `editor.tokenColorCustomizations`.
- Workbench color customization (`workbench.colorCustomizations`).
- File icon themes (Seti, Material Icons, etc.).
- Product icon themes.

### VGS IDE
- **3 themes available**: Dark, Light, High Contrast (in SettingsViewModel).
- **Only Dark theme actually implemented** in `AppStyles.axaml`: hardcoded hex colors matching VS Code Dark+ theme.
  - Background: `#1E1E1E`, Panels: `#252526`, Menu: `#2D2D2D`, Status bar: `#007ACC`
  - Selection: `#094771`, Hover: `#2A2D2E`
- No Light or High Contrast theme stylesheets exist (selecting them in settings likely has no visual effect).
- No token-level color customization.
- Emoji-based icons (no icon theme system).

### Gaps
- Only 1 functional theme despite 3 listed.
- No light theme implementation.
- No marketplace / installable themes.
- No token color customization.
- No workbench color customization.
- No file icon themes (uses emoji characters for all icons).
- No product icon theme.

---

## 10. Keyboard Shortcuts

### VS Code
- `keybindings.json` for full customization.
- Keyboard Shortcuts editor (Ctrl+K Ctrl+S) with search, record shortcut, and when-clause conditions.
- Context-based shortcuts (different behavior in editor vs terminal vs sidebar).
- Chord shortcuts (Ctrl+K Ctrl+C for comment).
- Extension-provided shortcuts.

### VGS IDE
- **63 keybindings** defined in `MainWindow.axaml` covering:
  - Debugging: F5, Ctrl+F5, Ctrl+Shift+F5, Shift+F5, F9, F10, F11, Shift+F11, Ctrl+F10, Ctrl+Shift+F10, Ctrl+Shift+F9
  - Editing: Ctrl+S, Ctrl+Shift+S, Ctrl+G, Ctrl+T, Ctrl+F, Ctrl+H, Ctrl+Shift+F, Ctrl+K (bookmark), F2/Shift+F2 (bookmarks)
  - Navigation: F12, Shift+F12
  - Refactoring: Ctrl+R, Ctrl+Shift+M/I/V/S/E/T/X/G, Ctrl+.
  - Panels: Ctrl+Alt+B/O/E/C/V/W/I/X
  - Command Palette: Ctrl+Shift+P
- Settings dialog has a **Keyboard tab** with DataGrid for rebinding (16 shortcuts editable).
- Shortcuts stored in `settings.json`.

### Gaps
- No chord shortcuts (Ctrl+K, Ctrl+C is not supported; Ctrl+K is used for toggle bookmark).
- No keyboard shortcut search/record UI (must type in DataGrid cell).
- No when-clause / context-based shortcuts.
- Only 16 shortcuts are editable in settings (the 63 AXAML keybindings are hardcoded).
- No extension-provided shortcuts.
- Several VGS shortcuts conflict with VS Code conventions (Ctrl+D is duplicate line in VGS vs add selection to next find match in VS Code).

---

## 11. Menu System

### VS Code
- Standard menu bar: File, Edit, Selection, View, Go, Run, Terminal, Help.
- Platform-native menus (Windows/macOS).
- Menu items show keyboard shortcuts.
- Some menus are context-dependent (e.g., Run menu changes during debugging).

### VGS IDE
- **7 top-level menus**: File, Edit, View, Tools, Build, Debug, Help.
- Menu bar with dark theme styling (`#2D2D2D` background).
- All menu items show `InputGesture` shortcuts.
- **File menu**: New Project, Open Project, Open File, Save, Save All, Exit.
- **Edit menu**: Undo/Redo, Cut/Copy/Paste, Find/Replace/Find in Files, Go to Definition, Find References, 12 refactoring commands, Go to Line/Symbol, Bookmarks submenu, Toggle Comment, Line operations (duplicate, move, delete).
- **View menu**: Command Palette, Solution Explorer, Output, Error List, Terminal, Find Results, Full Screen.
- **Build menu**: Build, Rebuild, Clean, Cancel Build.
- **Debug menu**: Full debug controls, breakpoint management, debug windows submenu.
- **Debug menu is context-aware**: `IsEnabled` bindings for `IsDebugging`, `IsPaused` states.

### Gaps
- No Selection menu (VS Code has expand/shrink selection, multi-cursor operations; VGS has these in Command Palette but not as a menu).
- No Go menu (VGS puts navigation in Edit menu).
- No Terminal menu (VGS puts Terminal in View menu).
- Help > About is a stub (no command bound).
- Full Screen menu item has no command binding (InputGesture defined but no handler).
- No dynamic menu items based on installed extensions.

---

## 12. Context Menus

### VS Code
- Editor context menu: Cut, Copy, Paste, Command Palette, Peek/Go to Definition, Find References, Rename Symbol, Format Document, refactoring actions (via Code Actions).
- Explorer context menu: Open, Open to the Side, Reveal in File Explorer, Copy Path, Rename, Delete.
- Tab context menu: Close, Close Others, Close to the Right, Pin, Split Editor.
- Panel context menus vary by panel type.

### VGS IDE
- **Editor context menu** (CodeEditorDocumentView.axaml): 30+ items including:
  - Cut, Copy, Paste
  - Add to Watch
  - Go to Definition, Peek Definition, Find All References
  - Surround With
  - 20+ refactoring operations (Rename, Extract Method, Inline Method, Introduce Variable, Extract Constant, Inline Constant, Change Signature, Encapsulate Field, Move Type to File, Extract Interface, Generate Constructor, Implement Interface, Override Method, Add/Remove/Reorder/Rename Parameter, etc.)
  - Toggle Comment
- **Solution Explorer context menu**: Add (New File, New Folder, Existing File), Open, Rename (F2), Delete, Open in Explorer, Copy Path.
- Styled context menus matching dark theme.

### Gaps
- No tab context menu (close others, close to right, pin, split).
- Editor context menu is excessively long (30+ items always visible vs VS Code's shorter menu with Code Actions for refactoring).
- No Code Actions grouping (VS Code groups refactoring suggestions dynamically based on cursor position).
- No "Open to the Side" in Solution Explorer.
- No output/terminal panel context menus.

---

## 13. Drag and Drop

### VS Code
- Drag tabs to reorder or split into editor groups.
- Drag files from Explorer into editor to open.
- Drag files from OS file manager into VS Code.
- Drag text selections within/between editors.
- Drag sidebar views between sidebar and panel.

### VGS IDE
- **Not implemented**. No `DragDrop`, `AllowDrop`, `DragOver`, or `DragEnter` handlers found in any Shell view files.
- Dock.Avalonia may provide basic drag support for dock tabs (rearranging tools within a dock group), but this is library-level, not application-level.
- No file drop from OS.
- No text drag and drop.
- No tab dragging to create split editors.

### Gaps
- No file drop support from OS file manager.
- No tab drag to reorder or split.
- No text selection drag and drop.
- No cross-panel drag and drop.
- Complete feature gap.

---

## 14. Zen Mode

### VS Code
- **Ctrl+K Z**: Enters Zen Mode.
- Hides all UI chrome: sidebar, panel, status bar, activity bar, tabs.
- Centers the editor with padding.
- Full screen optional.
- Configurable: which elements to hide, centering width.
- Toggle back with Esc (double press) or same shortcut.

### VGS IDE
- **Not implemented**. No Zen Mode, distraction-free mode, or focus mode found.
- View > Full Screen menu item exists but has no command binding.
- No centering layout mode.

### Gaps
- Complete feature gap.
- Full Screen is declared in the menu but not functional.
- No focus mode or distraction-free editing.

---

## Additional Observations

### Toolbar
VGS IDE has a **toolbar** (`Border DockPanel.Dock="Top"`) that VS Code does not have by default:
- New, Open, Save, Save All buttons
- Configuration dropdown (Debug/Release)
- Debug controls (Start/Continue, Pause, Stop, Restart, Step Over/Into/Out) with visibility bindings for debug state
- Debug status text
- Uses unicode text symbols for icons rather than SVG/image icons

This toolbar is more like Visual Studio than VS Code. VS Code relies on keyboard shortcuts and the Command Palette for these actions instead.

### Split Editor
VGS IDE supports **per-document split view** (horizontal or vertical) with toggle buttons in the top-right corner of each editor. VS Code's split is between editor groups (different documents or same document in different groups), which is more flexible.

### Git Integration
VGS IDE has 4 git-related panels (Changes, Branches, Stash, Blame) in the left dock, compared to VS Code's Source Control sidebar view. The VGS approach gives more dedicated screen space to git features, though the actual git operation depth varies.

### Debug Panel Layout
VGS IDE splits bottom panels into two groups (general tools and debug tools side by side), which is unique. VS Code shows all bottom panels as a single tab strip. The VGS approach keeps debug tools visible alongside output during debugging sessions.

### Dialog-Heavy Approach
VGS IDE uses many modal dialogs (68+ dialog files found) for refactoring operations, settings, project creation, and breakpoint configuration. VS Code uses inline UI (peek windows, quick picks, input boxes) for most of these operations, which keeps the user in flow.

---

## Priority Recommendations

### High Priority (fundamental UX gaps)
1. **Notifications system**: Add toast notifications for background events.
2. **Drag and drop**: Enable file drop and tab reordering.
3. **Functional themes**: Implement Light and High Contrast themes.

### Medium Priority (workflow improvements)
4. **Unified Quick Open + Command Palette**: Merge into a single dialog with prefix switching (like VS Code's Ctrl+P).
5. **Tab management**: Add close others, close to right, pin tabs.
6. **Activity Bar**: Add icon-based sidebar switcher for faster view navigation.
7. **Status bar improvements**: Add git branch, error/warning count, debug state color.

### Lower Priority (polish)
8. **Zen mode**: Implement distraction-free editing.
9. **Minimap**: Add scrollbar code preview.
10. **Editor context menu cleanup**: Group refactoring items into a submenu or use Code Actions.
11. **Settings search**: Add filtering within the Settings dialog.
12. **Full keyboard shortcut editor**: Make all 63+ shortcuts editable, add search/record.

---

## Key Files Referenced

- Layout: `VisualGameStudio.Shell/Views/MainWindow.axaml`
- Dock: `VisualGameStudio.Shell/Dock/DockFactory.cs`
- ViewModel: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs`
- Command Palette: `VisualGameStudio.Shell/ViewModels/Dialogs/CommandPaletteViewModel.cs`, `Views/Dialogs/CommandPaletteDialog.axaml`
- Quick Open: `VisualGameStudio.Shell/ViewModels/Dialogs/QuickOpenViewModel.cs`, `Views/Dialogs/QuickOpenDialog.axaml`
- Settings: `VisualGameStudio.Shell/ViewModels/Dialogs/SettingsViewModel.cs`, `Views/Dialogs/SettingsDialog.axaml`
- Status Bar: `VisualGameStudio.Shell/ViewModels/StatusBarViewModel.cs`
- Styles: `VisualGameStudio.Shell/Resources/Styles/AppStyles.axaml`
- Editor: `VisualGameStudio.Shell/Views/Documents/CodeEditorDocumentView.axaml`
- Solution Explorer: `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs`, `Views/Panels/SolutionExplorerView.axaml`
- Terminal: `VisualGameStudio.Shell/ViewModels/Panels/TerminalViewModel.cs`
