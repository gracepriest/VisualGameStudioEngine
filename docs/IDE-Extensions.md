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

- `.bl` - BasicLang source files
- `.bas` - BasicLang source files (alternative)
- `.blproj` - BasicLang project files

---

## Visual Studio 2022 Extension

The Visual Studio extension provides language support for BasicLang in VS 2022.

### Installation

1. Build the extension using MSBuild with VSSDK targets
2. Install the VSIX: `VS.BasicLang.vsix`
3. Restart Visual Studio

### Features

- **Language Service**: LSP client for IntelliSense and diagnostics
- **Syntax Highlighting**: Via pkgdef registration
- **Build Commands**: Tools menu integration for build/run
- **File Associations**: Auto-detection of .bl/.bas/.blproj files

### Build Requirements

- Visual Studio 2022 (17.0+)
- .NET Framework 4.8
- Microsoft.VSSDK.BuildTools 17.9+

### Menu Commands

Access via Tools > BasicLang:
- **Build Project** - Build the current file/project
- **Run Project** - Run the current project
- **Restart Language Server** - Restart the LSP connection

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

```csharp
var service = new TextMateService();
var grammar = await service.LoadGrammarAsync("path/to/grammar.json");
var theme = await service.LoadThemeAsync("path/to/theme.json");
```

### Snippet Service

Load and expand code snippets from VS Code format.

```csharp
var service = new SnippetService();
service.LoadSnippets("path/to/snippets.json");
var expanded = service.ExpandSnippet("if", values);
```

---

## Building Extensions

### VS Code Extension

```bash
cd vscode-basiclang
npm install
npm run compile
npm run package
```

Output: `basiclang-1.0.0.vsix`

### Visual Studio Extension

```bash
cd VS.BasicLang
dotnet restore
msbuild VS.BasicLang.csproj -p:Configuration=Release -p:VSToolsPath="path/to/VSSDK" -t:Rebuild,CreateVsixContainer
```

Output: `VS.BasicLang.vsix`
