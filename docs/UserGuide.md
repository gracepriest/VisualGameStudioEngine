# VisualGameStudio IDE User Guide

## Table of Contents
1. [Getting Started](#getting-started)
2. [Editor Features](#editor-features)
3. [Project Management](#project-management)
4. [Building and Running](#building-and-running)
5. [Debugging](#debugging)
6. [Code Analysis](#code-analysis)
7. [Find and Replace](#find-and-replace)
8. [Task List](#task-list)
9. [Terminal](#terminal)
10. [Version Control](#version-control)

---

## Getting Started

### Opening a Project
1. Use `File > Open Project` to open an existing BasicLang project
2. Navigate to the `.blproj` file and select it
3. The project tree will populate with all project files

### Creating a New Project
1. Use `File > New Project`
2. Choose a project template
3. Specify the project name and location
4. The IDE creates the project structure automatically

### IDE Layout
- **Left Panel**: Project Explorer, showing files and folders
- **Center**: Code Editor with syntax highlighting
- **Bottom Panel**: Output, Errors, Terminal, Task List
- **Right Panel**: Properties, Outline

---

## Editor Features

### Syntax Highlighting
The editor provides full syntax highlighting for BasicLang:
- Keywords (Function, If, For, etc.) in blue
- Strings in brown
- Comments in green
- Numbers in dark cyan

### Code Completion
Press `Ctrl+Space` to trigger code completion:
- Shows available functions, variables, and keywords
- Filter suggestions by typing
- Press `Enter` or `Tab` to accept

### Code Folding
- Click the fold markers in the gutter to collapse/expand code regions
- Use `Ctrl+Shift+[` to fold current region
- Use `Ctrl+Shift+]` to unfold current region

### Bracket Matching
- Matching brackets are highlighted when cursor is near one
- Supports `()`, `[]`, and `{}`

### Multi-Cursor Editing
- Hold `Ctrl` and click to add cursors
- Use `Ctrl+D` to select next occurrence
- All cursors edit simultaneously

### Go to Definition
- Press `F12` or `Ctrl+Click` on a symbol
- Jumps to the definition of functions, variables, types

### Find All References
- Press `Shift+F12` on a symbol
- Shows all locations where the symbol is used

---

## Project Management

### Project Structure
```
MyProject/
├── MyProject.blproj    # Project file
├── Program.bl          # Main entry point
├── Modules/
│   └── Utils.bl        # Module files
└── Assets/
    └── data.json       # Resource files
```

### Adding Files
1. Right-click in Project Explorer
2. Select `Add > New File` or `Add > Existing File`
3. Choose the file type and name

### Build Configurations
- **Debug**: Full debugging support, no optimization
- **Release**: Optimized code, minimal debug info

Switch configurations in the toolbar dropdown.

---

## Building and Running

### Building
- Press `Ctrl+B` or use `Build > Build Solution`
- Build output appears in the Output panel
- Errors appear in the Errors panel

### Running
- Press `F5` to run with debugging
- Press `Ctrl+F5` to run without debugging
- Output appears in the Output panel

### Clean Build
- Use `Build > Clean Solution` to remove build artifacts
- Use `Build > Rebuild Solution` for a fresh build

---

## Debugging

### Breakpoints
- Click in the gutter to toggle a breakpoint
- Press `F9` to toggle at current line
- Right-click breakpoint for conditions

### Debug Controls
- `F5` - Continue
- `F10` - Step Over
- `F11` - Step Into
- `Shift+F11` - Step Out

### Watch Window
- Add expressions to monitor during debugging
- Right-click variable and select "Add Watch"

### Call Stack
- View the current call stack during debugging
- Double-click to navigate to a frame

---

## Code Analysis

### Running Analysis
The IDE automatically analyzes code as you type. You can also:
- Use `Analyze > Run Code Analysis` for full analysis
- View results in the Errors panel

### Issue Types

#### Code Smells
- Long functions (exceed line limit)
- Too many parameters
- Deep nesting
- Magic numbers
- Empty catch blocks

#### Security Issues
- Hardcoded credentials
- Command injection risks
- Path traversal vulnerabilities

#### Complexity Metrics
- Cyclomatic complexity per function
- Cognitive complexity
- Nesting depth

### Quick Fixes
- Click the lightbulb icon for suggested fixes
- Press `Ctrl+.` to show quick actions

---

## Find and Replace

### Quick Find
- Press `Ctrl+F` to open Find
- Type search text and press `Enter`
- Use `F3` / `Shift+F3` for next/previous

### Find and Replace
- Press `Ctrl+H` to open Find and Replace
- Enter search and replacement text
- Click Replace or Replace All

### Find Options
- **Case Sensitive**: Match exact case
- **Whole Word**: Match complete words only
- **Regex**: Use regular expressions
- **Preserve Case**: Match case pattern in replacement

### Find in Files
- Press `Ctrl+Shift+F` for project-wide search
- Specify file patterns to include/exclude
- Results show in Find Results panel

---

## Task List

### Task Comments
Add comments with special tokens:
```basic
' TODO: Implement this feature
' FIXME: This needs to be fixed
' HACK: Temporary workaround
' BUG: Known bug here
' NOTE: Important note
```

### Viewing Tasks
- Open `View > Task List`
- Tasks are extracted from all project files
- Click a task to navigate to its location

### Filtering Tasks
- Filter by type (TODO, FIXME, BUG, etc.)
- Filter by priority
- Filter by file
- Search by text

### Task Priorities
- **High**: FIXME, BUG, XXX
- **Normal**: TODO, HACK, UNDONE
- **Low**: NOTE

---

## Terminal

### Opening Terminal
- Use `View > Terminal` or `` Ctrl+` ``
- A new terminal session opens

### Creating Sessions
- Click the `+` button to create new sessions
- Each session is independent
- Switch between sessions using tabs

### Running Commands
- Type commands and press `Enter`
- Use Up/Down arrows for command history
- Use `Tab` for auto-completion

### Clear Terminal
- Type `clear` or press `Ctrl+L`
- Clears visible output, history preserved

---

## Version Control

### Git Integration
The IDE integrates with Git for version control:

### Status
- Modified files show in Project Explorer
- View changes in the Source Control panel

### Committing
1. Stage files by clicking `+` or use `Stage All`
2. Enter commit message
3. Click `Commit`

### Branching
- View current branch in status bar
- Use Source Control panel to switch branches
- Create new branches from current state

### History
- View file history with `Right-click > View History`
- Compare versions side-by-side

---

## Keyboard Shortcuts

### General
| Shortcut | Action |
|----------|--------|
| Ctrl+N | New File |
| Ctrl+O | Open File |
| Ctrl+S | Save |
| Ctrl+Shift+S | Save All |
| Ctrl+W | Close File |

### Editing
| Shortcut | Action |
|----------|--------|
| Ctrl+Z | Undo |
| Ctrl+Y | Redo |
| Ctrl+X | Cut |
| Ctrl+C | Copy |
| Ctrl+V | Paste |
| Ctrl+A | Select All |
| Ctrl+D | Duplicate Line |

### Navigation
| Shortcut | Action |
|----------|--------|
| Ctrl+G | Go to Line |
| F12 | Go to Definition |
| Ctrl+- | Navigate Back |
| Ctrl+Shift+- | Navigate Forward |

### Search
| Shortcut | Action |
|----------|--------|
| Ctrl+F | Find |
| Ctrl+H | Find and Replace |
| Ctrl+Shift+F | Find in Files |
| F3 | Find Next |
| Shift+F3 | Find Previous |

### Build and Debug
| Shortcut | Action |
|----------|--------|
| Ctrl+B | Build |
| F5 | Run with Debugging |
| Ctrl+F5 | Run without Debugging |
| F9 | Toggle Breakpoint |
| F10 | Step Over |
| F11 | Step Into |

---

## Troubleshooting

### Build Errors
1. Check the Errors panel for details
2. Double-click error to go to location
3. Review the error message and fix code

### Performance Issues
1. Close unused files and panels
2. Disable real-time analysis for large projects
3. Use `Build > Clean` to clear caches

### Plugin Issues
1. Disable suspect plugins in `Tools > Extensions`
2. Check plugin compatibility
3. Reinstall problematic plugins

---

## Getting Help

- Press `F1` for context-sensitive help
- Visit documentation at project repository
- Report issues on GitHub issues page
