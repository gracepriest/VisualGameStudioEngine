# VS Code Project / Workspace System Research

> Research date: 2026-03-18

This document covers the architecture and capabilities of VS Code's project and workspace system, including settings hierarchy, build tasks, debugging, extensions, source control, terminal integration, and file watchers.

---

## 1. Workspace Model

VS Code has three workspace models: **single-folder**, **multi-root**, and **virtual**.

### 1.1 Single-Folder Workspace

The simplest and most common model. Opening a single folder creates an implicit workspace. Workspace-specific configuration is stored in a `.vscode/` directory at the folder root:

```
my-project/
  .vscode/
    settings.json
    tasks.json
    launch.json
    extensions.json
  src/
  package.json
```

No explicit workspace file is needed. The folder *is* the workspace.

### 1.2 Multi-Root Workspace

A multi-root workspace aggregates two or more folders into a single VS Code window. It is defined by a `.code-workspace` file:

```jsonc
// project.code-workspace
{
  "folders": [
    { "path": "frontend" },
    { "path": "backend" },
    { "path": "../shared-lib" }    // relative paths supported
  ],
  "settings": {
    // workspace-level settings go here
  },
  "extensions": {
    "recommendations": ["ms-python.python"]
  }
}
```

Key characteristics:
- **Creation**: Drag multiple folders into the editor, use `File > Add Folder to Workspace`, or open multiple folders from the CLI (`code folder1 folder2`).
- **Settings scoping**: Each root folder can have its own `.vscode/settings.json`. Only *resource-scoped* settings (e.g., `files.exclude`) apply per-folder; *window-scoped* settings are ignored at the folder level to prevent conflicts.
- **Search**: Global search spans all root folders; results are grouped by folder. You can scope a search to one folder with `./` prefix.
- **Debug**: VS Code discovers `launch.json` files from every root folder and displays them with a folder-name suffix. Compound launch configurations can be defined in the `.code-workspace` file.
- **Tasks**: `tasks.json` from each root folder is discovered; tasks show with a folder suffix.

### 1.3 Virtual Workspace

A virtual workspace is backed by a `FileSystemProvider` extension rather than the local disk. The canonical example is the **GitHub Repositories** extension, which uses a `vscode-vfs` URI scheme:

```
vscode-vfs://github/microsoft/vscode/package.json
```

Characteristics:
- Files exist in memory or on a remote server; no local clone is needed.
- Some features (terminal, tasks, native debuggers) may be unavailable or limited.
- Extensions must declare `virtualWorkspaces` capability in `package.json` to opt in.
- Enables lightweight browsing and editing of remote repos without cloning.

---

## 2. Explorer / File Tree

The **Explorer** view in the Side Bar is the primary file navigation surface.

### 2.1 Structure

- Renders the workspace folder(s) as a rooted tree.
- In multi-root workspaces, each root folder appears as a top-level node.
- Supports drag-and-drop for moving/copying files and for adding folders to the workspace.

### 2.2 File and Folder Icons

- VS Code ships with a minimal icon set and the **Seti** icon theme.
- Icons are controlled via `File > Preferences > File Icon Theme`.
- Extensions can contribute custom icon themes via the `iconThemes` contribution point.
- Icon themes are JSON files that map file extensions, file names, folder names, and language IDs to SVG/PNG icon definitions.

### 2.3 Filtering and Finding

- Press `Ctrl+Alt+F` (Windows/Linux) to open an inline **Find** control in the Explorer.
- Two modes:
  - **Highlight mode** (default): Matching files/folders are highlighted; folders show a badge with match count.
  - **Filter mode**: Non-matching items are hidden from the tree.
- Fuzzy matching is supported.

### 2.4 File Exclusions

- `files.exclude`: Glob patterns for files/folders to hide from the Explorer (and from search by default).
- `search.exclude`: Additional patterns excluded only from search (not from the tree).

```jsonc
{
  "files.exclude": {
    "**/.git": true,
    "**/node_modules": true,
    "**/bin": true,
    "**/obj": true
  }
}
```

### 2.5 File Nesting (VS Code 1.67+)

The `explorer.fileNesting` settings allow grouping related files under a parent:

```jsonc
{
  "explorer.fileNesting.enabled": true,
  "explorer.fileNesting.patterns": {
    "*.ts": "${capture}.js, ${capture}.d.ts, ${capture}.js.map",
    "package.json": "package-lock.json, .npmrc, yarn.lock"
  }
}
```

### 2.6 Sort Order

- `explorer.sortOrder`: `default` (alphabetical, folders first), `filesFirst`, `type`, `modified`, `foldersNestsFiles`.

---

## 3. Settings Hierarchy

VS Code settings cascade through multiple scopes. **Later scopes override earlier ones** (with exceptions for language-specific settings):

| Priority | Scope | Location | Applies To |
|----------|-------|----------|------------|
| 1 (lowest) | **Default** | Built into VS Code | All instances |
| 2 | **User** | `%APPDATA%\Code\User\settings.json` (Windows) | All instances for this user |
| 3 | **Remote** | On the remote machine | Remote sessions |
| 4 | **Workspace** | `.vscode/settings.json` or `.code-workspace` `"settings"` | Current workspace |
| 5 (highest) | **Workspace Folder** | `.vscode/settings.json` per root folder | That folder only (multi-root) |

### 3.1 Merge Behavior

- **Primitive and array values**: Higher-priority scope fully replaces lower-priority value.
- **Object values**: Merged (keys from both scopes are combined; conflicts go to higher-priority scope).

### 3.2 Language-Specific Settings

Language-specific settings (e.g., `"[python]": { "editor.tabSize": 4 }`) have special precedence:

- Language-specific user settings **override** non-language-specific workspace settings.
- If the same language has settings in both user and workspace scope, workspace wins for that language.

### 3.3 Settings Categories

Settings have a **scope** that controls where they can be applied:

| Scope | Description |
|-------|-------------|
| `application` | User settings only (not in workspace) |
| `machine` | User or remote settings (not in workspace) |
| `machine-overridable` | Like `machine` but can be overridden per workspace |
| `window` | User or workspace (not per-folder in multi-root) |
| `resource` | Can be set at any level including per-folder |
| `language-overridable` | Can be overridden per language |

---

## 4. Tasks System

VS Code tasks integrate external tools (compilers, linters, bundlers) into the editor workflow via `.vscode/tasks.json`.

### 4.1 Task Schema

```jsonc
{
  "version": "2.0.0",
  "tasks": [
    {
      "label": "Build",
      "type": "shell",             // "shell" or "process"
      "command": "dotnet",
      "args": ["build", "-c", "Release"],
      "group": {
        "kind": "build",
        "isDefault": true          // Ctrl+Shift+B runs this
      },
      "presentation": {
        "echo": true,
        "reveal": "always",        // "always", "never", "silent"
        "focus": false,
        "panel": "shared",         // "shared", "dedicated", "new"
        "showReuseMessage": true,
        "clear": false
      },
      "problemMatcher": "$msCompile",
      "options": {
        "cwd": "${workspaceFolder}",
        "env": { "NODE_ENV": "production" },
        "shell": {
          "executable": "bash",
          "args": ["-c"]
        }
      },
      "dependsOn": ["PreBuild"],   // run other tasks first
      "dependsOrder": "sequence",  // or "parallel"
      "runOptions": {
        "runOn": "folderOpen"      // auto-run on workspace open
      }
    }
  ]
}
```

### 4.2 Task Types

| Type | Behavior |
|------|----------|
| `shell` | Command is interpreted by the shell (bash, cmd, PowerShell). Supports pipes, redirection, etc. |
| `process` | Command is spawned directly as a process. No shell interpretation. |

Extensions can register custom task types via the `taskDefinitions` contribution point (e.g., `npm`, `gulp`, `msbuild`).

### 4.3 Problem Matchers

Problem matchers parse build/lint output and surface errors in the **Problems** panel with file, line, column, severity, and message.

**Built-in problem matchers:**

| Matcher | Tool |
|---------|------|
| `$tsc` | TypeScript compiler |
| `$tsc-watch` | TypeScript compiler (watch mode, background) |
| `$msCompile` | Microsoft C/C++ compiler (cl.exe), MSBuild, C# |
| `$gcc` | GCC / Clang |
| `$eslint-compact` | ESLint compact output |
| `$eslint-stylish` | ESLint stylish output |
| `$jshint` | JSHint |
| `$jshint-stylish` | JSHint stylish output |
| `$go` | Go compiler |

**Custom problem matcher example:**

```jsonc
{
  "problemMatcher": {
    "owner": "basiclang",
    "fileLocation": ["relative", "${workspaceFolder}"],
    "pattern": {
      "regexp": "^(.+)\\((\\d+),(\\d+)\\):\\s+(error|warning)\\s+(.+)$",
      "file": 1,
      "line": 2,
      "column": 3,
      "severity": 4,
      "message": 5
    }
  }
}
```

**Background problem matchers** support long-running tasks (e.g., file watchers) by defining `beginsPattern` and `endsPattern` to delimit build cycles.

### 4.4 Task Auto-Detection

Extensions can provide `TaskProvider` implementations that auto-detect tasks. VS Code ships with auto-detection for npm, gulp, grunt, Jake, and TypeScript.

### 4.5 Compound Tasks

Tasks can declare `dependsOn` to chain tasks. `dependsOrder` controls sequential vs. parallel execution.

---

## 5. Launch Configurations (Debugging)

Debug configurations live in `.vscode/launch.json`.

### 5.1 Schema

```jsonc
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch Program",
      "type": "coreclr",           // debug adapter type
      "request": "launch",         // "launch" or "attach"
      "program": "${workspaceFolder}/bin/Debug/net8.0/MyApp.dll",
      "args": ["--verbose"],
      "cwd": "${workspaceFolder}",
      "env": { "ASPNETCORE_ENVIRONMENT": "Development" },
      "console": "integratedTerminal",  // "internalConsole", "integratedTerminal", "externalTerminal"
      "stopAtEntry": false,
      "preLaunchTask": "Build",    // task label to run first
      "postDebugTask": "Cleanup",
      "serverReadyAction": {
        "pattern": "Now listening on:\\s+(https?://\\S+)",
        "uriFormat": "%s",
        "action": "openExternally"
      }
    }
  ],
  "compounds": [
    {
      "name": "Full Stack",
      "configurations": ["Launch Backend", "Launch Frontend"],
      "stopAll": true
    }
  ]
}
```

### 5.2 Core Concepts

| Concept | Description |
|---------|-------------|
| **Launch** | VS Code starts the program and attaches the debugger |
| **Attach** | VS Code connects to an already-running process |
| **Compound** | Launches multiple configurations simultaneously |
| **preLaunchTask** | Runs a task (from tasks.json) before debugging starts |
| **serverReadyAction** | Opens a URL when the debug output matches a pattern |

### 5.3 Debug Adapter Protocol (DAP)

VS Code uses the [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/) to communicate with debuggers. Extensions provide debug adapters via the `debuggers` contribution point in `package.json`. The protocol is language-agnostic and transport-agnostic (stdin/stdout, sockets, named pipes).

### 5.4 Variables

Launch configurations support variable substitution:

| Variable | Value |
|----------|-------|
| `${workspaceFolder}` | Root path of the workspace folder |
| `${workspaceFolderBasename}` | Folder name without path |
| `${file}` | Current open file |
| `${fileBasename}` | File name without path |
| `${fileDirname}` | Directory of current file |
| `${lineNumber}` | Current cursor line number |
| `${selectedText}` | Selected text in editor |
| `${env:VARNAME}` | Environment variable |
| `${input:id}` | User input prompt (defined in `inputs` array) |
| `${command:commandId}` | Result of a VS Code command |

### 5.5 Multi-Root Debug

In multi-root workspaces, `launch.json` files from all root folders are discovered. Configurations appear in the debug dropdown with a folder suffix. The `.code-workspace` file can also define workspace-level launch configurations and compounds.

---

## 6. Extensions Model

### 6.1 Extension Anatomy

Every extension is an npm package with a `package.json` manifest that declares:

| Field | Purpose |
|-------|---------|
| `name`, `version`, `publisher` | Identity |
| `engines.vscode` | Minimum VS Code version |
| `main` | Entry point (JS module) |
| `activationEvents` | Events that trigger extension loading |
| `contributes` | Static JSON declarations (commands, settings, languages, etc.) |

### 6.2 Activation Events

Extensions are lazy-loaded. They activate when a matching event occurs:

| Event | Trigger |
|-------|---------|
| `onLanguage:basiclang` | A file with the language ID is opened |
| `onCommand:myExt.doThing` | A declared command is invoked (auto since VS Code 1.74) |
| `onDebug` | Debug session starts |
| `onDebugResolve:type` | Debug config of the given type is resolved |
| `workspaceContains:**/*.bas` | Workspace contains matching files |
| `onFileSystem:scheme` | Files from the URI scheme are opened |
| `onView:viewId` | A declared view is opened |
| `onUri` | Extension's system-wide URI handler is invoked |
| `onStartupFinished` | After VS Code startup completes |
| `*` | Always activate (discouraged) |

### 6.3 Contribution Points

Extensions register capabilities statically in `package.json` under `contributes`:

- `languages` -- Register language IDs, extensions, aliases, configuration
- `grammars` -- TextMate grammars for syntax highlighting
- `themes`, `iconThemes`, `productIconThemes` -- UI theming
- `commands` -- Command palette entries
- `menus` -- Context menus, editor title bar, SCM title bar, etc.
- `keybindings` -- Keyboard shortcuts
- `configuration` -- Settings (appear in Settings editor)
- `configurationDefaults` -- Default setting values
- `taskDefinitions` -- Custom task types
- `problemMatchers`, `problemPatterns` -- Build output parsers
- `debuggers` -- Debug adapter registrations
- `breakpoints` -- Language IDs that support breakpoints
- `views`, `viewsContainers` -- Side bar / panel views (Tree View, Webview)
- `snippets` -- Code snippets per language
- `jsonValidation` -- JSON schema associations
- `customEditors` -- Custom editor providers
- `terminal` -- Terminal profiles

### 6.4 Extension API Surface

The `vscode` module exposes APIs for:

- **Workspace**: `workspace.fs`, `workspace.onDidChangeTextDocument`, `workspace.findFiles`, `workspace.createFileSystemWatcher`
- **Window**: `window.showInformationMessage`, `window.createTerminal`, `window.createTreeView`, `window.registerWebviewViewProvider`
- **Languages**: `languages.registerCompletionItemProvider`, `languages.registerHoverProvider`, `languages.registerDefinitionProvider`, `languages.createDiagnosticCollection`
- **Debug**: `debug.registerDebugAdapterDescriptorFactory`, `debug.startDebugging`
- **Commands**: `commands.registerCommand`, `commands.executeCommand`
- **SCM**: `scm.createSourceControl`
- **Authentication**: `authentication.getSession`
- **Language Model (AI)**: `lm.selectChatModels` (VS Code 1.90+)

### 6.5 Extension Hosts

- **Node.js extension host**: Full Node.js APIs, file system access, child processes.
- **Web extension host**: Runs in the browser (vscode.dev). Limited to browser-compatible APIs. Declared via `"browser"` field in `package.json`.
- **Remote extension host**: Runs on a remote machine (SSH, WSL, container).

### 6.6 Extension Recommendations

`.vscode/extensions.json` lists recommended and unwanted extensions for a workspace:

```jsonc
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "basiclang.vscode-basiclang"
  ],
  "unwantedRecommendations": [
    "some.conflicting-extension"
  ]
}
```

---

## 7. Source Control Integration (Git)

### 7.1 Built-in Git Extension

VS Code ships with a built-in Git extension that provides:

- **Source Control view**: Shows changed, staged, and untracked files grouped by status.
- **Inline diff**: Click a changed file to see a side-by-side diff.
- **Staging**: Stage/unstage individual files or hunks.
- **Commit**: Commit message input with Ctrl+Enter shortcut.
- **Branching**: Create, switch, delete, rename branches from the status bar or Command Palette.
- **Merge conflicts**: Inline conflict markers with Accept Current/Incoming/Both actions.
- **Remote operations**: Push, pull, fetch, sync.
- **Git blame**: Inline annotations showing last commit per line.
- **Timeline view**: File history with commit-level navigation.
- **Gutter indicators**: Color-coded markers in the editor gutter for added (green), modified (blue), and deleted (red) lines.

### 7.2 SCM API

The Source Control API (`vscode.scm`) allows extensions to register custom SCM providers. Core abstractions:

| Interface | Purpose |
|-----------|---------|
| `SourceControl` | Represents one SCM provider instance (e.g., a Git repo) |
| `SourceControlResourceGroup` | Groups resources by state (staged, working tree, merge conflicts) |
| `SourceControlResourceState` | One file's state (URI, decorations, command on click) |

Multiple SCM providers can coexist (e.g., Git + SVN in the same workspace).

### 7.3 Key Settings

```jsonc
{
  "git.autofetch": true,            // Auto-fetch periodically
  "git.confirmSync": false,         // Don't ask before sync
  "git.enableSmartCommit": true,    // Stage all if nothing staged
  "git.defaultCloneDirectory": "~/repos",
  "scm.defaultViewMode": "tree"     // "tree" or "list" for SCM view
}
```

---

## 8. Terminal Integration

### 8.1 Terminal Profiles

Terminal profiles define shell configurations per platform:

```jsonc
{
  "terminal.integrated.profiles.windows": {
    "PowerShell": {
      "source": "PowerShell",       // auto-detect
      "icon": "terminal-powershell"
    },
    "Command Prompt": {
      "path": "C:\\Windows\\System32\\cmd.exe",
      "args": [],
      "icon": "terminal-cmd"
    },
    "Git Bash": {
      "source": "Git Bash"
    },
    "WSL": {
      "path": "wsl.exe",
      "args": ["-d", "Ubuntu"]
    }
  },
  "terminal.integrated.defaultProfile.windows": "PowerShell"
}
```

Profiles specify either a `source` (auto-detected, Windows only: `PowerShell` or `Git Bash`) or a `path` (explicit executable path) plus optional `args`, `icon`, `color`, and `env`.

### 8.2 Shell Integration

VS Code's shell integration injects escape sequences into supported shells (bash, zsh, PowerShell, fish) to enable:

- **Command detection**: VS Code knows where each command starts/ends.
- **Command decorations**: Success/failure icons in the gutter next to commands.
- **Run Recent Command**: Quick pick of recently executed commands (`Ctrl+Alt+R`).
- **Go to Recent Directory**: Navigate terminal `cwd` history.
- **Command navigation**: `Ctrl+Up/Down` to jump between commands.
- **Sticky scroll**: Current command stays visible when scrolling.

### 8.3 Task Terminal Integration

Tasks use the integrated terminal by default. Per-task overrides:

```jsonc
{
  "label": "Build",
  "type": "shell",
  "options": {
    "shell": {
      "executable": "bash",
      "args": ["-c"]
    },
    "cwd": "${workspaceFolder}/src"
  }
}
```

### 8.4 Key Settings

| Setting | Purpose |
|---------|---------|
| `terminal.integrated.cwd` | Default working directory |
| `terminal.integrated.env.*` | Environment variable overrides per platform |
| `terminal.integrated.fontSize` | Terminal font size |
| `terminal.integrated.scrollback` | Max scrollback lines (default 1000) |
| `terminal.integrated.sendKeybindingsToShell` | Let shell handle certain keybindings |

---

## 9. File Watchers and Hot Reload

### 9.1 Built-in File Watcher

VS Code uses a file system watcher to detect changes to files on disk (made outside the editor). This powers:

- **Explorer refresh**: New/deleted/renamed files appear automatically.
- **Dirty indicator update**: If an open file is changed externally, VS Code prompts or auto-reloads.
- **Search index update**: Search results reflect file system changes.
- **Git status update**: SCM decorations update when files change.

### 9.2 Watcher Configuration

```jsonc
{
  // Exclude from file watching (reduces OS handles, improves perf)
  "files.watcherExclude": {
    "**/.git/objects/**": true,
    "**/.git/subtree-cache/**": true,
    "**/node_modules/*/**": true,
    "**/bin/**": true,
    "**/obj/**": true
  }
}
```

- `files.watcherExclude`: Glob patterns for directories excluded from the OS file watcher entirely. Reduces inotify/ReadDirectoryChanges handle usage. Critical on Linux where the default inotify limit is low.
- Separate from `files.exclude` (which only hides files in Explorer/search but still watches them).

### 9.3 Extension File Watcher API

Extensions can create their own file watchers:

```typescript
// Watch for .bas file changes in the workspace
const watcher = vscode.workspace.createFileSystemWatcher('**/*.bas');

watcher.onDidCreate(uri => { /* file created */ });
watcher.onDidChange(uri => { /* file modified */ });
watcher.onDidDelete(uri => { /* file deleted */ });

// Dispose when no longer needed
watcher.dispose();
```

The `FileSystemWatcher` interface supports:
- `onDidCreate` -- File/folder created
- `onDidChange` -- File/folder modified
- `onDidDelete` -- File/folder deleted
- Glob pattern filtering at creation time

### 9.4 FileSystemProvider

For virtual file systems, extensions implement `FileSystemProvider`:

```typescript
export interface FileSystemProvider {
  onDidChangeFile: Event<FileChangeEvent[]>;
  watch(uri: Uri, options: { recursive: boolean; excludes: string[] }): Disposable;
  stat(uri: Uri): FileStat;
  readDirectory(uri: Uri): [string, FileType][];
  readFile(uri: Uri): Uint8Array;
  writeFile(uri: Uri, content: Uint8Array, options: { create: boolean; overwrite: boolean }): void;
  delete(uri: Uri, options: { recursive: boolean }): void;
  rename(oldUri: Uri, newUri: Uri, options: { overwrite: boolean }): void;
  createDirectory(uri: Uri): void;
}
```

### 9.5 Hot Reload Considerations

VS Code itself does not have a built-in "hot reload" feature. Hot reload is typically provided by:

- **Extension-specific watchers**: e.g., the Live Server extension watches HTML files.
- **Task-based watchers**: Background tasks with `isBackground: true` and a background `problemMatcher` (e.g., `tsc --watch`, `webpack --watch`).
- **Debug adapter support**: Some debug adapters (e.g., .NET Hot Reload, React Fast Refresh) support hot reload during debug sessions.
- **`files.autoSave`**: Auto-save triggers external watchers (e.g., `nodemon`, `dotnet watch`).

```jsonc
{
  "files.autoSave": "afterDelay",
  "files.autoSaveDelay": 1000
}
```

---

## 10. Summary Table: Key Configuration Files

| File | Location | Purpose |
|------|----------|---------|
| `settings.json` | `~/.config/Code/User/` (Linux) or `%APPDATA%\Code\User\` (Win) | User-global settings |
| `.vscode/settings.json` | Workspace root | Workspace/folder settings |
| `.code-workspace` | Anywhere | Multi-root workspace definition + settings |
| `.vscode/tasks.json` | Workspace root | Build/test task definitions |
| `.vscode/launch.json` | Workspace root | Debug configurations |
| `.vscode/extensions.json` | Workspace root | Recommended extensions |
| `package.json` | Extension root | Extension manifest (activation, contributions) |

---

## Sources

- [What is a VS Code workspace?](https://code.visualstudio.com/docs/editing/workspaces/workspaces)
- [Multi-root Workspaces](https://code.visualstudio.com/docs/editing/workspaces/multi-root-workspaces)
- [User and workspace settings](https://code.visualstudio.com/docs/configure/settings)
- [Integrate with External Tools via Tasks](https://code.visualstudio.com/docs/debugtest/tasks)
- [Tasks Appendix](https://code.visualstudio.com/docs/reference/tasks-appendix)
- [Debug code with Visual Studio Code](https://code.visualstudio.com/docs/debugtest/debugging)
- [Visual Studio Code debug configuration](https://code.visualstudio.com/docs/debugtest/debugging-configuration)
- [Extension Anatomy](https://code.visualstudio.com/api/get-started/extension-anatomy)
- [Activation Events](https://code.visualstudio.com/api/references/activation-events)
- [Extension Manifest](https://code.visualstudio.com/api/references/extension-manifest)
- [Contribution Points](https://code.visualstudio.com/api/references/contribution-points)
- [VS Code API](https://code.visualstudio.com/api/references/vscode-api)
- [Source Control in VS Code](https://code.visualstudio.com/docs/sourcecontrol/overview)
- [Source Control API](https://code.visualstudio.com/api/extension-guides/scm-provider)
- [Terminal Profiles](https://code.visualstudio.com/docs/terminal/profiles)
- [Terminal Shell Integration](https://code.visualstudio.com/docs/terminal/shell-integration)
- [Terminal Basics](https://code.visualstudio.com/docs/terminal/basics)
- [File and Folder Icons](https://code.visualstudio.com/blogs/2016/09/08/icon-themes)
- [File Icon Theme API](https://code.visualstudio.com/api/extension-guides/file-icon-theme)
- [Tree View API](https://code.visualstudio.com/api/extension-guides/tree-view)
- [Virtual Workspaces](https://code.visualstudio.com/api/extension-guides/virtual-workspaces)
- [Remote Repositories](https://code.visualstudio.com/blogs/2021/06/10/remote-repositories)
- [Task Provider API](https://code.visualstudio.com/api/extension-guides/task-provider)
- [Debugger Extension API](https://code.visualstudio.com/api/extension-guides/debugger-extension)
- [Variables Reference](https://code.visualstudio.com/docs/reference/variables-reference)
