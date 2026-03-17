# BasicLang IDE Extensions

This document describes the IDE extensions available for BasicLang development.

## Visual Studio Code Extension

The VS Code extension provides language support for BasicLang in Visual Studio Code.

### Installation

1. Build the extension: `cd vscode-basiclang && npm install && npm run package`
2. Install the VSIX: `code --install-extension basiclang-1.0.0.vsix`

### Features

- **Syntax Highlighting**: Full TextMate grammar for BasicLang syntax
- **Code Snippets**: 40+ snippets for common code patterns
- **Language Server**: Connects to BasicLang LSP for IntelliSense
- **Debugging**: DAP integration for debugging support
- **Build Tasks**: Task definitions for build/run/clean

### Configuration

```json
{
  "basiclang.languageServerPath": "path/to/BasicLang.exe",
  "basiclang.enableSemanticHighlighting": true,
  "basiclang.enableInlayHints": true,
  "basiclang.enableCodeLens": true
}
```

### Supported File Extensions

- `.bas` - BasicLang source files (primary)
- `.blproj` - BasicLang project files

---

## Visual Studio 2022 Extension (BasicLang.VisualStudio v2.4.0)

The primary Visual Studio extension, built on CPS (Common Project System) for full project system integration.

### Installation

1. Build the VSIX:
   ```bash
   cd BasicLang.VisualStudio/src/BasicLang.VisualStudio
   "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" BasicLang.VisualStudio.csproj -p:Configuration=Release
   ```
2. Close all VS 2022 instances
3. Install `BasicLang.VisualStudio.vsix`
4. Run `devenv.exe /updateConfiguration`

### Features

- **CPS Project System**: Full project type with Solution Explorer integration
- **LSP IntelliSense**: Connects to `BasicLang.exe --lsp` for completions, hover, diagnostics, references
- **TextMate Syntax Highlighting**: Shared grammar with VS Code extension
- **Language Configuration**: Bracket matching, auto-closing pairs, comment toggling, indentation rules
- **4 Project Templates**: Console App, Class Library, WinForms App, WPF App
- **3 Item Templates**: Class, Module, Interface
- **Menu Commands**: Build, Run, Change Backend, Restart Server, Go To Definition, Find References
- **Options Pages**: General (LSP path, auto-start) + Compiler (backend, framework, warnings)
- **Custom Language Tag**: Templates appear under "BasicLang" in the New Project dialog language filter

### Template Tags

Templates use custom `<LanguageTag>BasicLang</LanguageTag>` which creates a dedicated "BasicLang" entry in the VS 2022 New Project dialog language dropdown.

### Build Requirements

- Visual Studio 2022 (17.0+) with VS SDK workload
- .NET Framework 4.8
- Microsoft.VSSDK.BuildTools 17.9+
- Must use MSBuild (not `dotnet build`)

### pkgdef Registration

The extension registers:
- TextMate grammar repository and language configuration
- Project factory with `Language(VsTemplate) = "BasicLang"`
- Template engine directories for project and item templates
- Menu resource for VSCT commands
- Auto-load triggers

### VSIX Contents

```
BasicLang.VisualStudio.dll
BasicLang.VisualStudio.pkgdef
LanguageService/BasicLangGrammar.json
LanguageService/basiclang-language-configuration.json
BuildSystem/BasicLang.targets
ProjectTemplates/BasicLang/*.zip (4 templates)
ProjectTemplates/BasicLang.ProjectTemplates.vstman
ItemTemplates/BasicLang/*.zip (3 templates)
ItemTemplates/BasicLang.ItemTemplates.vstman
Menus.ctmenu
```

---

## VS.BasicLang (Legacy Extension v1.3.0)

A simpler MEF-based extension. Superseded by BasicLang.VisualStudio but still functional.

### Build

```bash
dotnet build VS.BasicLang/VS.BasicLang.csproj -c Release
```

Output: `VS.BasicLang/VS.BasicLang.vsix` (~13KB)

### Features

- LSP client for IntelliSense
- TextMate grammar for syntax highlighting
- 1 project template (uses `<ProjectType>CSharp</ProjectType>` workaround)
- No menu commands or options pages

---

## Visual Game Studio IDE (Avalonia)

The full-featured Avalonia-based IDE with ~93% VS Code feature parity.

### Key Features

| Category | Features |
|----------|----------|
| **IntelliSense** | Completions, signature help, hover, inlay hints, code lens |
| **Navigation** | Go to definition, find references, document outline, type hierarchy, call hierarchy |
| **Editing** | Multi-cursor, auto-closing brackets, surrounding pairs, smart indentation, 30+ snippets |
| **Search** | Inline find/replace (Ctrl+F/H), command palette (Ctrl+Shift+P), workspace symbol search |
| **Debugging** | Breakpoints (persistent, data, exception), set next statement, restart, inline debug values |
| **UI** | Indentation guides, status bar (encoding, line ending, language mode), code folding |
| **Formatting** | Format document, on-type formatting, rename symbol |

### Build & Run

```bash
# Build
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release

# Run
./IDE/VisualGameStudio.exe

# Run tests (1,636 tests)
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
```

---

## Infrastructure Components

### LSP Client

Generic Language Server Protocol client for connecting to any LSP server.

```csharp
var client = new LspClient();
await client.StartAsync("path/to/language-server.exe", "--stdio");
var completions = await client.RequestCompletionAsync(uri, position);
```

### DAP Client

Debug Adapter Protocol client for debugging support.

```csharp
var client = new DapClient();
await client.StartAsync("path/to/debug-adapter.exe");
await client.SetBreakpointsAsync(source, breakpoints);
await client.LaunchAsync(program, args);
```

### TextMate Service

Parse and apply VS Code TextMate grammars and themes.

### Snippet Service

Load and expand code snippets from VS Code format.

---

## AI Agent SDK Applications

4 Claude Agent SDK applications automate maintenance across the project:

| Agent | Scope | Profiles | MCP Tools |
|-------|-------|----------|-----------|
| **BasicLangAgent** | Compiler (lexer, parser, backends, tests) | 6 | 4 |
| **IDEAgent** | IDE (editor, shell, debugger, project) | 7 | 6 |
| **EngineAgent** | C++ engine + VB.NET wrapper sync | 8 | 7 |
| **VSExtensionAgent** | VSIX (CPS, LSP, templates, pkgdef) | 7 | 6 |

### Usage

```bash
cd BasicLangAgent
pip install -r requirements.txt
python basiclang_agent.py --profile tests "Run all compiler tests and report results"
python basiclang_agent.py --list-profiles
```
