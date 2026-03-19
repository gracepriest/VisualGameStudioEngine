# VS Code Error Handling vs VGS IDE - Research & Recommendations

## Date: March 2026

---

## 1. VS Code Error Handling Features (Complete Inventory)

### 1.1 Problems Panel
- Centralized list of all errors, warnings, and info messages (Ctrl+Shift+M)
- Filterable by severity (errors/warnings/info toggle buttons)
- Sortable and groupable by file, severity, or source
- Shows error code, message, file path, line, column
- Click-to-navigate to source location
- Badge count on panel tab showing total problems
- Auto-reveal on build when new problems appear
- "Copy Message" context menu on individual items
- Filter by text search within the panel
- Group by File / Severity / Source

### 1.2 Squiggly Underlines (Editor Decorations)
- Red squiggles for errors
- Yellow/amber squiggles for warnings
- Blue squiggles for info/hints
- Customizable colors via `editorError.foreground`, `editorWarning.foreground`, `editorInfo.foreground`
- Squiggles span the exact error range (not just a fixed width)
- Hover over squiggle shows tooltip with error message, code, and source

### 1.3 Overview Ruler (Scrollbar Markers)
- Colored markers in the right-side scrollbar gutter showing error positions
- Red ticks for errors, yellow for warnings, blue for info
- Provides at-a-glance view of all issues in a long file
- Clicking a marker scrolls to that position

### 1.4 Gutter Icons
- Red circle/dot in the line number gutter for lines with errors
- Yellow triangle for warnings
- Blue "i" for info messages
- Lightbulb icon when code actions are available

### 1.5 Quick Fix Lightbulb (Code Actions)
- Yellow lightbulb appears in gutter when cursor is on a line with available fixes
- Ctrl+. (or Cmd+.) to invoke code actions
- Shows dropdown menu of available fixes
- "Preferred" fix can be applied directly
- Supports workspace-wide edits (rename, extract, etc.)

### 1.6 Error Navigation (F8 / Shift+F8)
- F8: Go to next error/warning in the file
- Shift+F8: Go to previous error/warning
- Opens inline peek zone showing the error details
- Loops through all diagnostics in the current file
- Peek zone shows severity icon, message, and source

### 1.7 Inline Peek Error Zone
- When navigating with F8, an inline zone appears below the error line
- Shows the error message with severity icon
- Shows source attribution (e.g., "ts(2322)")
- Can cycle through multiple errors on the same line
- Dismissable with Escape

### 1.8 Error Lens (Popular Extension)
- Inline diagnostic text at end of the offending line
- Entire line background highlighted in error/warning color
- Message template customization ($message, $severity, $source, $code)
- Follow-cursor mode: only show inline message on active line
- Configurable delay before showing messages
- Line highlighting intensity configurable

### 1.9 Problem Matchers (Build Integration)
- Regex-based patterns to extract errors from build output
- Maps to file, line, column, severity, message, code
- Built-in matchers for tsc, eslint, go, jshint, etc.
- Custom matchers definable in tasks.json

### 1.10 Status Bar Error Count
- Bottom status bar shows error/warning counts with icons
- Clickable to open Problems panel
- Color changes based on severity (red background if errors exist)

---

## 2. VGS IDE Current Implementation

### 2.1 Error List Panel (ErrorListViewModel + ErrorListView)
**What exists:**
- DataGrid with columns: Severity, Code, Description, File, Line, Column
- Filter toggles for Errors/Warnings/Messages with counts
- Double-click to navigate to error location
- Severity counts displayed in header

**Files:**
- `VisualGameStudio.Shell/ViewModels/Panels/ErrorListViewModel.cs`
- `VisualGameStudio.Shell/Views/Panels/ErrorListView.axaml`
- `VisualGameStudio.Shell/Views/Panels/ErrorListView.axaml.cs`

### 2.2 Squiggly Underlines (TextMarkerService)
**What exists:**
- Custom `TextMarkerService` implementing `DocumentColorizingTransformer` + `IBackgroundRenderer`
- Draws squiggly lines under error ranges with severity-based colors:
  - Error: `#E51400` (red)
  - Warning: `#CCA700` (amber)
  - Info: `#3794FF` (blue)
- Text foreground color also changes for error/warning markers
- Markers store start offset, length, type, and message

**Files:**
- `VisualGameStudio.Editor/TextMarkers/TextMarkerService.cs`

### 2.3 Hover Tooltips for Errors
**What exists:**
- `ShowErrorTooltip()` method creates a popup when hovering over marked text
- Shows error message in a styled border popup
- Popup positioned near the hover location
- Uses `GetMarkersAtOffset()` to find relevant markers

**File:**
- `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (lines ~2455-2493)

### 2.4 Code Actions (Quick Fix)
**What exists:**
- Ctrl+. triggers `CodeActionsRequested` event
- `ShowCodeActionsAsync()` in MainWindowViewModel calls LSP `GetCodeActionsAsync()`
- Shows available actions in a list selection dialog
- Can apply workspace edits from selected action

**File:**
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (lines ~4331-4384)

### 2.5 Minimap
**What exists:**
- Basic minimap showing code structure with keyword/comment/string colors
- Viewport indicator for scroll position
- Click-to-scroll navigation

**What's missing:** No diagnostic markers in the minimap.

### 2.6 Build Diagnostics
**What exists:**
- `BuildService.cs` parses compiler output into `DiagnosticItem` objects
- Build results flow into Error List panel
- LSP `DiagnosticsReceived` event updates both Error List and editor markers

---

## 3. Gap Analysis

| Feature | VS Code | VGS IDE | Gap |
|---------|---------|---------|-----|
| Problems panel with filters | Yes | Yes | Partial - no text search, no grouping |
| Severity toggle buttons | Yes | Yes | Done |
| Click-to-navigate | Yes | Yes (double-click) | Single-click missing |
| Squiggly underlines | Yes | Yes | Done |
| Severity-based colors | Yes | Yes | Done |
| Hover tooltip on squiggle | Yes | Yes | Partial - always shows error color, no severity icon |
| Overview ruler / scrollbar markers | Yes | No | **Missing** |
| Gutter error/warning icons | Yes | No | **Missing** |
| Lightbulb gutter icon | Yes | No | **Missing** |
| F8 next/prev error navigation | Yes | No | **Missing** |
| Inline peek error zone | Yes | No | **Missing** |
| Error Lens inline messages | Extension | No | **Missing** |
| Status bar error count | Yes | No | **Missing** |
| Problem matchers (build) | Yes | Partial | Build errors parsed, no configurable matchers |
| Copy error message | Yes | No | **Missing** |
| Group by file/severity | Yes | No | **Missing** |
| Panel text filter | Yes | No | **Missing** |
| Diagnostic markers in minimap | Yes | No | **Missing** |
| Code actions via Ctrl+. | Yes | Yes | Done, but no lightbulb |
| Badge count on panel tab | Yes | No | **Missing** |

---

## 4. Improvement Recommendations (Prioritized)

### Priority 1: Easy Wins (1-2 hours each)

#### 4.1 Status Bar Error/Warning Count
**Effort:** Low
**Impact:** High - constant visibility of project health

Add error/warning counts to the IDE status bar (already exists for other info). Display red error icon + count and yellow warning icon + count. Click opens/focuses the Error List panel.

**Where to implement:**
- `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` - add `ErrorCount`/`WarningCount` properties
- Status bar AXAML - add bound TextBlocks with colored icons

#### 4.2 Severity Icons in Error List
**Effort:** Low
**Impact:** Medium - visual scanning is faster with icons

Replace the text "Error"/"Warning"/"Info" in the Severity column with colored icons (red circle, yellow triangle, blue "i"). Use a `DataGridTemplateColumn` with an icon + text.

**Where to implement:**
- `VisualGameStudio.Shell/Views/Panels/ErrorListView.axaml` - replace `DataGridTextColumn` for Severity

#### 4.3 Copy Error Message Context Menu
**Effort:** Low
**Impact:** Medium - useful for searching/reporting errors

Add right-click context menu to Error List with "Copy Message", "Copy Line", "Copy All".

**Where to implement:**
- `ErrorListView.axaml` - add `ContextMenu` to DataGrid
- `ErrorListViewModel.cs` - add `CopyMessageCommand`

#### 4.4 Hover Tooltip Severity Coloring
**Effort:** Low
**Impact:** Low - current tooltip always uses error color

The `ShowErrorTooltip()` method always uses `#F48771` (error color). Pass severity through and color-code: red for error, amber for warning, blue for info. Also show the error code in the tooltip.

**Where to implement:**
- `CodeEditorControl.axaml.cs` - modify `ShowErrorTooltip()` to accept severity

### Priority 2: Medium Effort (2-4 hours each)

#### 4.5 F8 / Shift+F8 Error Navigation
**Effort:** Medium
**Impact:** High - very common workflow in VS Code

Add keyboard handling for F8 (next error) and Shift+F8 (previous error). Track current diagnostic index. Navigate caret to the error location and optionally show the error message inline.

**Where to implement:**
- `CodeEditorControl.axaml.cs` - add F8/Shift+F8 key handling
- `MainWindowViewModel.cs` - add `GoToNextError()`/`GoToPreviousError()` commands
- Use `ErrorListViewModel.Diagnostics` sorted by file+line as the navigation list

#### 4.6 Gutter Error/Warning Icons
**Effort:** Medium
**Impact:** High - immediate visual indicator of problem lines

Create a custom margin for AvaloniaEdit that renders small colored icons (red dot, yellow triangle) in the gutter for lines with diagnostics. The `TextMarkerService` already tracks marker positions.

**Where to implement:**
- New file: `VisualGameStudio.Editor/Margins/DiagnosticGutterMargin.cs`
- Extend `AbstractMargin` from AvaloniaEdit
- Register in `CodeEditorControl.axaml.cs` during editor setup

#### 4.7 Overview Ruler / Scrollbar Diagnostic Markers
**Effort:** Medium
**Impact:** High - at-a-glance view of all issues in file

Draw colored ticks on the right edge of the editor (or overlay on scrollbar) showing diagnostic positions relative to document length. Red for errors, yellow for warnings.

**Where to implement:**
- New file: `VisualGameStudio.Editor/Controls/OverviewRulerControl.cs`
- Or integrate into existing minimap by adding colored markers for diagnostic lines

#### 4.8 Diagnostic Markers in Minimap
**Effort:** Medium
**Impact:** Medium - leverages existing minimap

Extend `MinimapControl` to accept a list of diagnostics and render colored horizontal lines/blocks at the corresponding line positions. Red for errors, yellow for warnings.

**Where to implement:**
- `VisualGameStudio.Editor/Controls/MinimapControl.axaml.cs` - add `UpdateDiagnostics()` method
- `CodeEditorControl.axaml.cs` - pass diagnostics to minimap when they update

#### 4.9 Error List Text Filter
**Effort:** Medium
**Impact:** Medium - helpful for large projects

Add a text filter box at the top of the Error List panel. Filter diagnostics by message content, file name, or error code.

**Where to implement:**
- `ErrorListView.axaml` - add TextBox above DataGrid
- `ErrorListViewModel.cs` - add `FilterText` property, update `RefreshFilteredDiagnostics()`

#### 4.10 Lightbulb Icon for Code Actions
**Effort:** Medium
**Impact:** Medium - discoverability of quick fixes

When the cursor is on a line with diagnostics, query the LSP for available code actions. If any exist, show a lightbulb icon in the gutter. Clicking invokes the code action menu.

**Where to implement:**
- Integrate with `DiagnosticGutterMargin` (from 4.6)
- Async query to LSP on cursor position change (debounced)

### Priority 3: Larger Features (4-8 hours each)

#### 4.11 Inline Peek Error Zone (F8 style)
**Effort:** High
**Impact:** Medium - nice to have, VS Code power-user feature

When navigating with F8, show an inline expandable zone below the error line (similar to peek definition). Show severity icon, message, source, and code. Allow cycling through multiple errors.

**Where to implement:**
- New custom AvaloniaEdit visual line element
- Complex rendering within text view

#### 4.12 Error Lens Inline Messages
**Effort:** High
**Impact:** Medium - popular VS Code extension feature

Render diagnostic messages as faded text at the end of each error line. Optionally highlight the full line background. Make it toggleable in settings.

**Where to implement:**
- Extend `TextMarkerService` or create new `InlineDiagnosticRenderer`
- Use AvaloniaEdit's `InlineObjectElement` or custom `VisualLineElementGenerator`

#### 4.13 Group By File/Severity in Error List
**Effort:** Medium-High
**Impact:** Low-Medium - useful for large projects

Add grouping support to the Error List DataGrid. Group diagnostics by file path or severity level with collapsible group headers.

**Where to implement:**
- `ErrorListViewModel.cs` - add grouping logic with `CollectionViewSource`
- `ErrorListView.axaml` - use `DataGrid.GroupStyle` or custom grouped ItemsControl

---

## 5. Recommended Implementation Order

1. **Status bar error count** (4.1) - highest visibility, lowest effort
2. **Severity icons in Error List** (4.2) - quick visual improvement
3. **F8/Shift+F8 navigation** (4.5) - essential developer workflow
4. **Gutter error icons** (4.6) - high-impact visual cue
5. **Copy error message** (4.3) - small quality-of-life fix
6. **Hover tooltip severity colors** (4.4) - trivial fix
7. **Minimap diagnostic markers** (4.8) - extends existing feature
8. **Overview ruler markers** (4.7) - important for long files
9. **Error list text filter** (4.9) - useful for larger projects
10. **Lightbulb icon** (4.10) - code action discoverability
11. **Error Lens inline messages** (4.12) - optional power feature
12. **Inline peek zone** (4.11) - complex, lower priority
13. **Group by file/severity** (4.13) - nice to have

---

## 6. Sources

- [Mastering Error Identification in VS Code](https://dev.to/pexlkeys/mastering-error-identification-how-to-show-errors-in-visual-studio-code-2oe5)
- [VS Code Error Lens Extension](https://github.com/usernamehw/vscode-error-lens)
- [A Better Way to See Errors with Error Lens](https://blog.openreplay.com/vscode-error-lens-errors/)
- [Customize VS Code Error Squiggle Colors](https://daveceddia.com/vscode-change-error-squiggles-color/)
- [VS Code Theme Color Reference](https://code.visualstudio.com/api/references/theme-color)
- [VS Code Code Navigation](https://code.visualstudio.com/docs/editing/editingevolved)
- [VS Code Tasks and Problem Matchers](https://code.visualstudio.com/docs/debugtest/tasks)
- [Understanding Problem Matchers](https://dev.to/collinskesuibai/understanding-problem-matchers-in-visual-studio-code-70b)
