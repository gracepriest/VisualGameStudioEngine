# IDE User Guide

This guide covers all features of the Visual Game Studio IDE.

## Interface Overview

The IDE consists of several key areas:

1. **Menu Bar** - File, Edit, View, Build, Debug, Tools, Help
2. **Toolbar** - Quick access to common actions
3. **Code Editor** - Main editing area with syntax highlighting
4. **Solution Explorer** - Project and file navigation
5. **Output Panel** - Build output, debug messages
6. **Error List** - Compilation errors and warnings

## Code Editor Features

### Syntax Highlighting

The editor provides semantic highlighting for:
- Keywords (blue)
- Types (cyan)
- Strings (brown)
- Numbers (green)
- Comments (gray)
- Identifiers (default)

### IntelliSense

IntelliSense provides intelligent code completion:

- **Auto-completion** - Press `Ctrl+Space` to trigger
- **Parameter hints** - Shows function signatures
- **Quick info** - Hover over symbols for documentation
- **Member lists** - Type `.` after an object

### Code Navigation

| Shortcut | Action |
|----------|--------|
| `F12` | Go to Definition |
| `Shift+F12` | Find All References |
| `Ctrl+G` | Go to Line |
| `Ctrl+F` | Find |
| `Ctrl+H` | Find and Replace |
| `Ctrl+-` | Navigate Back |
| `Ctrl+Shift+-` | Navigate Forward |

### Code Folding

Click the `-` icons in the margin to collapse:
- Functions and Subroutines
- Classes and Modules
- Regions
- If/Select blocks

### Multi-Cursor Editing

- `Ctrl+Alt+Click` - Add cursor
- `Ctrl+Shift+L` - Add cursors to all occurrences
- `Escape` - Return to single cursor

### Bookmarks

| Shortcut | Action |
|----------|--------|
| `Ctrl+K, Ctrl+K` | Toggle Bookmark |
| `Ctrl+K, Ctrl+N` | Next Bookmark |
| `Ctrl+K, Ctrl+P` | Previous Bookmark |
| `Ctrl+K, Ctrl+L` | Clear All Bookmarks |

## Refactoring

### Rename Symbol

1. Place cursor on identifier
2. Press `F2` or right-click > Rename
3. Enter new name
4. Preview changes and confirm

### Quick Actions

Press `Ctrl+.` to access quick actions:
- Add missing `End If`, `End Sub`, etc.
- Generate variable declarations
- Extract to method
- Surround with Try-Catch
- Convert Sub to Function
- Change access modifiers

### Code Actions Available

- **Quick Fixes**
  - Add missing block endings
  - Declare undefined variables
  - Fix type mismatches
  - Add missing `Then`

- **Refactorings**
  - Extract variable
  - Extract method
  - Convert Sub/Function
  - Toggle Public/Private
  - Surround with Region
  - Generate property from field

## Building

### Build Commands

| Shortcut | Action |
|----------|--------|
| `Ctrl+Shift+B` | Build Solution |
| `Ctrl+B` | Build Current Project |
| `F7` | Build |

### Build Configurations

- **Debug** - Includes debug symbols, no optimization
- **Release** - Optimized, no debug info

## Debugging

### Starting Debug Session

| Shortcut | Action |
|----------|--------|
| `F5` | Start Debugging |
| `Ctrl+F5` | Start Without Debugging |
| `Shift+F5` | Stop Debugging |
| `Ctrl+Shift+F5` | Restart Debugging |

### Breakpoints

| Shortcut | Action |
|----------|--------|
| `F9` | Toggle Breakpoint |
| `Ctrl+Shift+F9` | Delete All Breakpoints |
| `Ctrl+F9` | Enable/Disable Breakpoint |

**Breakpoint Types:**
- **Line Breakpoint** - Break at specific line
- **Conditional Breakpoint** - Break when condition is true
- **Hit Count** - Break after N hits
- **Logpoint** - Print message without stopping

### Stepping

| Shortcut | Action |
|----------|--------|
| `F10` | Step Over |
| `F11` | Step Into |
| `Shift+F11` | Step Out |
| `Ctrl+Shift+F10` | Set Next Statement |

### Debug Windows

- **Locals** - View local variables
- **Watch** - Monitor expressions
- **Call Stack** - View call hierarchy
- **Immediate** - Execute expressions
- **Breakpoints** - Manage breakpoints

### Watch Window

Add expressions to monitor:
1. Open Watch window (`Debug > Windows > Watch`)
2. Click "Add Watch"
3. Enter expression
4. Value updates at each breakpoint

### Immediate Window

Execute code while paused:
```
?variableName          ' Print value
expression             ' Evaluate expression
clear                  ' Clear window
```

## Project Management

### Creating Projects

1. `File > New Project`
2. Select template
3. Configure options
4. Click Create

### Project Templates

- **Console Application** - Command-line program
- **Game Project** - 2D game with engine integration
- **Class Library** - Reusable code library

### Adding Files

- Right-click project > `Add > New Item`
- Or `Ctrl+Shift+A`

### Project Properties

Right-click project > Properties:
- **Application** - Output type, startup object
- **Build** - Compilation settings
- **Debug** - Debug configuration

## Settings

### Editor Settings

`Tools > Options > Editor`:
- Font and colors
- Tab size and indentation
- Word wrap
- Line numbers

### Keyboard Shortcuts

`Tools > Options > Keyboard`:
- View all shortcuts
- Customize bindings

## Tips and Tricks

### Productivity

1. Use `Ctrl+Space` frequently for completions
2. `Ctrl+.` for quick fixes
3. `F12` to explore code
4. `Ctrl+K, Ctrl+C` to comment selection
5. `Ctrl+K, Ctrl+U` to uncomment

### Error Resolution

1. Check Error List panel
2. Double-click error to navigate
3. Use `Ctrl+.` for suggested fixes
4. Hover for detailed messages

### Performance

1. Close unused files
2. Collapse large regions
3. Use `Ctrl+G` for quick navigation
4. Build specific projects, not entire solution

## Keyboard Shortcuts Reference

### General
| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | New File |
| `Ctrl+O` | Open File |
| `Ctrl+S` | Save |
| `Ctrl+Shift+S` | Save All |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |

### Editing
| Shortcut | Action |
|----------|--------|
| `Ctrl+X` | Cut |
| `Ctrl+C` | Copy |
| `Ctrl+V` | Paste |
| `Ctrl+A` | Select All |
| `Ctrl+D` | Duplicate Line |
| `Ctrl+L` | Delete Line |

### View
| Shortcut | Action |
|----------|--------|
| `Ctrl+E, E` | Solution Explorer |
| `Ctrl+E, O` | Output |
| `Ctrl+E, L` | Error List |

## Next Steps

- [Debugging Guide](debugging-guide.md) - Advanced debugging
- [BasicLang Guide](basiclang-guide.md) - Language reference
- [Game Engine Guide](engine-guide.md) - Building games
