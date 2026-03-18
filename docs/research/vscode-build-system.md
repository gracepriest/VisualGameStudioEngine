# VS Code Build System Research

Research into how Visual Studio Code handles building, compiling, error parsing, and build output presentation. This covers the task system, problem matchers, the Problems panel, and extension APIs for custom build tool integration.

---

## 1. Build Tasks (tasks.json)

VS Code uses a task system configured via `.vscode/tasks.json` in the workspace root. Tasks integrate external build tools (compilers, bundlers, linters) into the editor.

### Task Types

| Type | Behavior |
|------|----------|
| `"shell"` | Runs the command inside a shell (bash, cmd, PowerShell). The command string is interpreted by the shell, so pipes, redirects, and shell builtins work. |
| `"process"` | Launches the program as a standalone process without a shell. More efficient but no shell features. |

### Minimal Example

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "build",
      "type": "shell",
      "command": "dotnet build",
      "group": {
        "kind": "build",
        "isDefault": true
      },
      "problemMatcher": "$msCompile"
    }
  ]
}
```

### Core Task Properties

| Property | Description |
|----------|-------------|
| `label` | Human-readable name shown in the task picker and referenced by `preLaunchTask` |
| `type` | `"shell"` or `"process"` |
| `command` | The command to execute |
| `args` | Array of arguments passed to the command |
| `group` | Categorizes the task as `"build"` or `"test"` with optional `isDefault` |
| `problemMatcher` | Defines how to parse build output for errors/warnings |
| `options` | Override `cwd`, `env`, or `shell` for this task |
| `presentation` | Controls terminal behavior (reveal, panel sharing, focus, clear) |
| `runOptions` | Controls when the task runs (e.g., on folder open) |
| `dependsOn` | Array of task labels that must run before this task |
| `dependsOrder` | `"parallel"` (default), `"sequence"`, or `"any"` |
| `isBackground` | If `true`, task runs indefinitely (watch mode) |

### Running Tasks

- **Ctrl+Shift+B** (Cmd+Shift+B on Mac) -- runs the default build task
- **Ctrl+Shift+P > "Tasks: Run Task"** -- shows all available tasks
- **Ctrl+Shift+P > "Tasks: Run Build Task"** -- shows tasks in the `build` group
- **Ctrl+Shift+P > "Tasks: Run Test Task"** -- shows tasks in the `test` group

### Task Groups

Tasks can be assigned to the `build` or `test` group. One task per group can be marked as the default:

```json
"group": {
  "kind": "build",
  "isDefault": true
}
```

The default build task is what runs when you press Ctrl+Shift+B.

---

## 2. Problem Matchers

Problem matchers are regex-based parsers that scan task output and extract structured error/warning information for the Problems panel.

### How They Work

1. Each line of task output is matched against the regex `pattern`
2. Capture groups extract: `file`, `line`, `column`, `severity`, `message`, `code`
3. Matched problems appear in the Problems panel and as inline squiggles in the editor

### Built-in Problem Matchers

VS Code ships with these predefined matchers:

| Matcher | Language/Tool | File Location Assumption |
|---------|---------------|--------------------------|
| `$tsc` | TypeScript compiler | Relative to workspace |
| `$tsc-watch` | TypeScript in watch mode | Relative to workspace |
| `$jshint` | JSHint | Absolute paths |
| `$jshint-stylish` | JSHint (stylish reporter) | Absolute paths |
| `$eslint-compact` | ESLint (compact format) | Relative to workspace |
| `$eslint-stylish` | ESLint (stylish format) | Relative to workspace |
| `$go` | Go compiler | Relative to workspace |
| `$gcc` | GCC/Clang compiler | Relative to workspace |
| `$msCompile` | C#/VB .NET compiler (MSBuild) | Absolute paths |

### Custom Problem Matcher Definition

```json
"problemMatcher": {
  "owner": "basiclang",
  "fileLocation": ["relative", "${workspaceFolder}"],
  "severity": "error",
  "pattern": {
    "regexp": "^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+(BL\\d+):\\s+(.+)$",
    "file": 1,
    "line": 2,
    "column": 3,
    "severity": 4,
    "code": 5,
    "message": 6
  }
}
```

### Problem Matcher Properties

| Property | Description |
|----------|-------------|
| `owner` | Identifier for the source of problems. Use a language service ID to merge with LSP diagnostics, or `"external"` (default). |
| `fileLocation` | How filenames are interpreted: `"absolute"`, `"relative"` (default, relative to cwd), or `["relative", "path"]` |
| `severity` | Default severity if the pattern doesn't capture it: `"error"`, `"warning"`, or `"info"` |
| `source` | Human-readable string identifying the source (shown in Problems panel) |
| `pattern` | Regex pattern with named capture group indices |

### Multi-line Problem Matchers

For compilers that spread errors across multiple lines, use an array of patterns:

```json
"pattern": [
  {
    "regexp": "^([^\\s].*)$",
    "file": 1
  },
  {
    "regexp": "^\\s+(\\d+):(\\d+)\\s+(error|warning):\\s+(.*)$",
    "line": 1,
    "column": 2,
    "severity": 3,
    "message": 4,
    "loop": true
  }
]
```

The first pattern captures the filename. The second pattern (with `"loop": true`) captures multiple errors for that file.

### Background Problem Matchers (Watch Mode)

For tasks that run continuously, the problem matcher needs `beginsPattern` and `endsPattern` to detect compilation cycles:

```json
"problemMatcher": {
  "owner": "basiclang",
  "pattern": { ... },
  "background": {
    "activeOnStart": true,
    "beginsPattern": "^File change detected\\.",
    "endsPattern": "^Compilation complete\\."
  }
}
```

---

## 3. Problems Panel

The Problems panel (Ctrl+Shift+M / Cmd+Shift+M) aggregates all detected issues from:
- Problem matchers (task output parsing)
- Language servers (LSP diagnostics)
- Linter extensions

### Features

| Feature | Description |
|---------|-------------|
| **Severity icons** | Errors (red circle), Warnings (yellow triangle), Info (blue circle) |
| **Quick filter buttons** | Top-right buttons to show/hide Errors, Warnings, Info |
| **Text filter** | Search bar to filter by text, file name, or diagnostic code |
| **Group by** | Organize by File (default), Severity, or Type |
| **Sort by** | Click column headers to sort by Severity, File, Position |
| **Error counts** | Status bar shows total error/warning counts |
| **File badges** | Explorer shows error/warning counts on affected files |

### Navigation

- **Double-click** a problem to jump to the exact source location (file, line, column)
- **Right-click context menu** provides: Go to Problem, Copy Message, Filter to This File
- **F8 / Shift+F8** cycles through problems in the active file
- **Ctrl+Shift+M** toggles the Problems panel visibility

### Problem Sources

Each problem has a `source` label (shown in parentheses) indicating its origin:
- `"ts"` -- TypeScript language service
- `"eslint"` -- ESLint extension
- `"basiclang"` -- Custom problem matcher with `"owner": "basiclang"`

Problems from different sources with the same `owner` as a language service are merged with that service's diagnostics.

---

## 4. Pre-Launch Build Tasks

The `preLaunchTask` property in `.vscode/launch.json` runs a task before starting a debug session.

### Configuration

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch Program",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/bin/Debug/net8.0/MyApp.dll",
      "preLaunchTask": "build",
      "postDebugTask": "cleanup"
    }
  ]
}
```

### Behavior

1. When you press F5 (Start Debugging), VS Code finds the task with a `label` matching the `preLaunchTask` value
2. The task runs to completion
3. If the task exits with a non-zero code, VS Code asks whether to continue debugging or abort
4. If problem matchers detect errors, VS Code may show a warning before launching
5. After debugging ends, the `postDebugTask` runs (if specified)

### Special Values

- `"preLaunchTask": "${defaultBuildTask}"` -- uses whatever task is marked as the default build task
- Background tasks used as `preLaunchTask` require a problem matcher with `beginsPattern`/`endsPattern` so VS Code knows when the task has finished its initial work

---

## 5. Auto-Detected Tasks

VS Code and extensions can automatically detect build tasks without requiring manual `tasks.json` configuration.

### Built-in Auto-Detection

VS Code natively auto-detects tasks for:
- **npm** -- detects `package.json` scripts
- **Gulp** -- detects `gulpfile.js` tasks
- **Grunt** -- detects `Gruntfile.js` tasks
- **Jake** -- detects `Jakefile` tasks
- **TypeScript** -- detects `tsconfig.json` and provides `tsc: build` / `tsc: watch`

### Extension-Provided Auto-Detection

- **C# Dev Kit** -- registers `dotnet: build`, `dotnet: clean`, `dotnet: restore` tasks when it detects a `.sln` or `.csproj`
- **CMake Tools** -- detects `CMakeLists.txt` and provides build/configure tasks
- **Makefile Tools** -- detects Makefiles and provides build tasks
- **Python** -- provides run/test tasks

### How Auto-Detection Works

1. Extensions register a `TaskProvider` for a task type
2. VS Code calls `provideTasks()` when the user opens the task picker
3. The provider scans the workspace for configuration files (e.g., `package.json`, `tsconfig.json`)
4. Detected tasks appear alongside manually defined tasks in the picker

### Configuring Auto-Detected Tasks

You can customize an auto-detected task by adding it to `tasks.json`. VS Code merges your overrides with the detected configuration:

```json
{
  "type": "npm",
  "script": "build",
  "group": {
    "kind": "build",
    "isDefault": true
  },
  "problemMatcher": "$tsc"
}
```

---

## 6. Custom Task Providers (Extension API)

Extensions can contribute tasks programmatically via the `TaskProvider` API.

### Registration

```typescript
// In extension activation
const provider = vscode.tasks.registerTaskProvider('basiclang', {
  provideTasks(): vscode.Task[] {
    // Scan workspace and return available tasks
    const buildTask = new vscode.Task(
      { type: 'basiclang', action: 'build' },  // task definition
      vscode.TaskScope.Workspace,
      'Build',                                   // label
      'BasicLang',                               // source
      new vscode.ShellExecution('basiclang build ${file}'),
      '$msCompile'                               // problem matcher
    );
    buildTask.group = vscode.TaskGroup.Build;
    return [buildTask];
  },

  resolveTask(task: vscode.Task): vscode.Task | undefined {
    // Fill in execution details for tasks loaded from tasks.json
    return task;
  }
});
```

### Key Concepts

| Concept | Description |
|---------|-------------|
| `provideTasks()` | Called when VS Code needs to list available tasks. Returns an array of `Task` objects. |
| `resolveTask()` | Called for tasks defined in `tasks.json` that match this provider's type. Fills in missing execution details. |
| `TaskDefinition` | Schema declared in `package.json` under `contributes.taskDefinitions`. Defines the properties unique to this task type. |

### Task Execution Types

| Execution Type | Use Case |
|----------------|----------|
| `ShellExecution` | Run a command in the shell. Simplest option. |
| `ProcessExecution` | Launch a process directly. More control, no shell overhead. |
| `CustomExecution` | Full control via a `Pseudoterminal`. Use for tasks requiring state, complex output handling, or no external process. |

### package.json Contribution

```json
{
  "contributes": {
    "taskDefinitions": [
      {
        "type": "basiclang",
        "required": ["action"],
        "properties": {
          "action": {
            "type": "string",
            "description": "The build action (build, clean, run)"
          },
          "target": {
            "type": "string",
            "description": "The compilation target backend"
          }
        }
      }
    ]
  }
}
```

### Problem Matchers from Extensions

Extensions can also contribute named problem matchers in `package.json`:

```json
{
  "contributes": {
    "problemMatchers": [
      {
        "name": "basiclang",
        "owner": "basiclang",
        "fileLocation": ["relative", "${workspaceFolder}"],
        "pattern": {
          "regexp": "^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+(BL\\d+):\\s+(.+)$",
          "file": 1,
          "line": 2,
          "column": 3,
          "severity": 4,
          "code": 5,
          "message": 6
        }
      }
    ]
  }
}
```

This makes the matcher available as `"$basiclang"` in any `tasks.json`.

---

## 7. Build Output: Terminal vs. Output Panel

### Terminal Panel (Default for Tasks)

- Tasks run in the **Integrated Terminal** panel by default
- Each task gets a terminal instance
- Full ANSI color support, interactive input possible
- Build output appears as it would in a regular terminal

### Presentation Control

The `presentation` property controls terminal behavior:

```json
"presentation": {
  "reveal": "always",
  "panel": "shared",
  "focus": false,
  "clear": false,
  "echo": true,
  "showReuseMessage": true,
  "close": false,
  "group": "build"
}
```

| Property | Values | Description |
|----------|--------|-------------|
| `reveal` | `"always"`, `"silent"`, `"never"` | When to show the terminal. `"silent"` only shows on errors. |
| `panel` | `"shared"`, `"dedicated"`, `"new"` | Whether to reuse terminal instances. |
| `focus` | `true/false` | Whether the terminal receives keyboard focus |
| `clear` | `true/false` | Clear the terminal before running |
| `echo` | `true/false` | Echo the command being run |
| `close` | `true/false` | Close the terminal when the task completes |
| `group` | string | Group related task terminals into split panes |

### Output Panel (for Extensions)

The Output panel (`View > Output`) is used by extensions via `vscode.window.createOutputChannel()`. It does not support ANSI colors (in standard channels) and is read-only. Language servers and some extensions write logs here.

### Typical Pattern

- **Build tasks** write to the Terminal panel (full color, interactive)
- **Problem matchers** parse that terminal output and populate the Problems panel
- **Language servers** write diagnostics directly to the Problems panel and logs to the Output panel

---

## 8. Error Navigation

### From Problems Panel

- **Double-click** any problem entry to open the file at the exact line and column
- **F8** -- go to next problem in the current file
- **Shift+F8** -- go to previous problem in the current file
- **Right-click > Go to Problem** -- navigate to the source location

### From Terminal Output

- Terminal output supports link detection. File paths with line numbers (e.g., `src/main.bas(10,5)`) are often clickable if the terminal detects them as file links.
- Ctrl+Click on file paths in the terminal opens the file in the editor.

### Inline Editor Indicators

- Red/yellow/blue squiggly underlines appear under problematic code
- Hover over a squiggle to see the error message
- The minimap (right gutter) shows colored markers for errors/warnings
- The scrollbar shows error positions as colored highlights

### Status Bar

The bottom-left status bar shows aggregate error/warning counts. Clicking it opens the Problems panel.

---

## 9. Watch Mode / Incremental Builds

### Background Tasks

Watch-mode tasks that run indefinitely must be configured as background tasks:

```json
{
  "label": "watch",
  "type": "shell",
  "command": "dotnet watch build",
  "isBackground": true,
  "problemMatcher": {
    "owner": "dotnet",
    "pattern": {
      "regexp": "^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+(\\w+):\\s+(.+)$",
      "file": 1,
      "line": 2,
      "column": 3,
      "severity": 4,
      "code": 5,
      "message": 6
    },
    "background": {
      "activeOnStart": true,
      "beginsPattern": "^\\s*dotnet watch .+ Started",
      "endsPattern": "^\\s*dotnet watch .+ (Exited|Waiting)"
    }
  }
}
```

### Key Requirements for Watch Mode

1. **`isBackground: true`** -- tells VS Code the task won't terminate
2. **`background.beginsPattern`** -- regex matching output when a new build cycle starts
3. **`background.endsPattern`** -- regex matching output when a build cycle completes
4. **`background.activeOnStart`** -- if `true`, the task is considered active immediately on launch

### Watch as preLaunchTask

When a background task is used as a `preLaunchTask`:
1. VS Code starts the task
2. It waits for the first `endsPattern` match (initial build complete)
3. The debugger launches
4. The task continues running in the background, detecting file changes

### Common Watch Tools

| Tool | Command | beginsPattern | endsPattern |
|------|---------|---------------|-------------|
| TypeScript | `tsc --watch` | `File change detected` | `Found 0 errors\|Watching for file changes` |
| .NET | `dotnet watch` | `dotnet watch .* Started` | `dotnet watch .* (Exited\|Waiting)` |
| Webpack | `webpack --watch` | `webpack is watching` | `compiled (successfully\|with warnings)` |

---

## 10. Multi-Step Build Pipelines

### dependsOn (Compound Tasks)

Tasks can depend on other tasks, creating build pipelines:

```json
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "clean",
      "type": "shell",
      "command": "dotnet clean"
    },
    {
      "label": "restore",
      "type": "shell",
      "command": "dotnet restore"
    },
    {
      "label": "compile",
      "type": "shell",
      "command": "dotnet build --no-restore",
      "problemMatcher": "$msCompile"
    },
    {
      "label": "test",
      "type": "shell",
      "command": "dotnet test --no-build",
      "problemMatcher": "$msCompile"
    },
    {
      "label": "full-build",
      "dependsOn": ["clean", "restore", "compile", "test"],
      "dependsOrder": "sequence",
      "group": {
        "kind": "build",
        "isDefault": true
      }
    }
  ]
}
```

### dependsOrder Values

| Value | Behavior |
|-------|----------|
| `"parallel"` | (Default) All dependent tasks start simultaneously |
| `"sequence"` | Tasks run one at a time in array order; each must complete before the next starts |

### Compound Task (No Command)

A task with `dependsOn` but no `command` acts purely as an orchestrator. It has no output of its own.

### Automatic Tasks on Folder Open

Tasks can run automatically when a workspace is opened:

```json
{
  "label": "auto-restore",
  "type": "shell",
  "command": "dotnet restore",
  "runOptions": {
    "runOn": "folderOpen"
  }
}
```

VS Code prompts the user for permission the first time a folder-open task is encountered.

---

## Summary Table

| Feature | Mechanism | Key File |
|---------|-----------|----------|
| Build tasks | `tasks.json` shell/process tasks | `.vscode/tasks.json` |
| Error parsing | Problem matchers (regex) | `tasks.json` or `package.json` |
| Problems panel | Aggregates diagnostics | Built-in UI (Ctrl+Shift+M) |
| Pre-launch builds | `preLaunchTask` | `.vscode/launch.json` |
| Auto-detected tasks | `TaskProvider` API | Extension `package.json` |
| Custom task providers | `vscode.tasks.registerTaskProvider()` | Extension TypeScript source |
| Build output | Integrated Terminal | Terminal panel |
| Error navigation | Double-click / F8 | Problems panel |
| Watch mode | `isBackground` + background matcher | `tasks.json` |
| Build pipelines | `dependsOn` + `dependsOrder` | `tasks.json` |

---

## Relevance to BasicLang / VGS IDE

For the Visual Game Studio IDE build system, the following VS Code patterns are most applicable:

1. **Problem matcher pattern**: BasicLang compiler output should follow a parseable format like `file(line,col): severity code: message` so it works with `$msCompile` or a custom matcher.

2. **Task auto-detection**: The vscode-basiclang extension could register a `TaskProvider` that detects `.blproj` files and provides build/run tasks automatically.

3. **Pre-launch builds**: The extension's `launch.json` templates should use `preLaunchTask` to compile before debugging.

4. **Watch mode**: If BasicLang supports file watching, a background task with `beginsPattern`/`endsPattern` would enable incremental compilation.

5. **Problems panel integration**: The LSP server (`BasicLang.exe --lsp`) already provides diagnostics directly; a problem matcher would add build-time error reporting on top of that.

6. **Build pipelines**: Multi-step builds (clean, restore, compile, run) can be composed with `dependsOn` and `dependsOrder: "sequence"`.

---

## Sources

- [VS Code Tasks Documentation](https://code.visualstudio.com/docs/debugtest/tasks)
- [VS Code Tasks Appendix](https://code.visualstudio.com/docs/reference/tasks-appendix)
- [VS Code Task Provider Extension Guide](https://code.visualstudio.com/api/extension-guides/task-provider)
- [VS Code Debugging Configuration](https://code.visualstudio.com/docs/debugtest/debugging-configuration)
- [VS Code C# Build Tools](https://code.visualstudio.com/docs/csharp/build-tools)
- [VS Code User Interface](https://code.visualstudio.com/docs/getstarted/userinterface)
- [Understanding Problem Matchers (DEV Community)](https://dev.to/collinskesuibai/understanding-problem-matchers-in-visual-studio-code-70b)
- [VSCode Tasks Problem Matchers (Allison Thackston)](https://www.allisonthackston.com/articles/vscode-tasks-problemmatcher.html)
- [Built-in Problem Matchers (Steve Kinney)](https://stevekinney.com/courses/visual-studio-code/built-in-problem-matchers)
- [Compound Tasks in VS Code (Steve Kinney)](https://stevekinney.com/courses/visual-studio-code/compound-tasks-vscode)
- [VS Code Extension Samples - Task Provider](https://github.com/microsoft/vscode-extension-samples/tree/main/task-provider-sample)
- [Getting Started with Problem Matchers (michaelheap.com)](https://michaelheap.com/getting-started-problem-matchers/)
- [preLaunchTask and postDebugTask (Medium)](https://medium.com/@mi_zhang/simplify-your-development-workflow-with-vscodes-prelaunchtask-in-launch-json-82809a996741)
