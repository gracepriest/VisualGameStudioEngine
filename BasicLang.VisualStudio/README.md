# BasicLang Visual Studio 2022 Extension

Visual Studio 2022 support for the BasicLang programming language: CPS-based project system integration, LSP-based IntelliSense, project/item templates, and MSBuild integration.

## Features

- **CPS Project System**: Project system integration for `.blproj` files
- **Syntax Highlighting**: TextMate grammar for `.bas`, `.bl`, and `.blproj` files
- **LSP IntelliSense** (via `BasicLang.exe --lsp`):
  - Code completion
  - Hover information
  - Diagnostics (errors)
  - Go to definition
- **BasicLang Menu Commands**: Build Project, Run, Change Backend, Restart Language Server, Go to Definition, Find All References
- **Options Pages**: Tools > Options > BasicLang (General and Compiler settings)
- **Project Templates**: Console Application, Class Library, Windows Forms Application, WPF Application
- **Item Templates**: Class, Module, Interface
- **Multiple Compiler Backends**: CSharp, MSIL, LLVM, C++ (selectable via the Change Backend command or Compiler options)

Not supported yet:

- **Debugging**: There is no debug launch support (no F5 debugging). The Run command builds and runs the program without a debugger attached.
- Find references, semantic highlighting, inlay hints, and CodeLens are not implemented by the language server yet. The related options and the Find All References menu command exist, but have no effect until the server supports them.

## Current Status

| Feature | Status | Notes |
|---------|--------|-------|
| CPS Project System | ✅ Implemented | Simplified for public NuGet APIs |
| LSP Client | ✅ Implemented | Connects to `BasicLang.exe --lsp` (completion, hover, diagnostics, go to definition) |
| Syntax Highlighting | ✅ Implemented | TextMate grammar from vscode-basiclang |
| Menu Commands | ✅ Implemented | Build, Run, Change Backend, Restart Server, Go to Definition, Find All References |
| Options Pages | ✅ Implemented | General and Compiler settings (some toggles depend on unimplemented server features) |
| Project Templates | ✅ Included in VSIX | 4 templates under ProjectTemplates/BasicLang |
| Item Templates | ✅ Included in VSIX | 3 templates under ItemTemplates/BasicLang |
| Debug Launch Provider | ❌ Not implemented | CPS debug APIs not publicly available |
| Bundled Compiler | ❌ Not included | BasicLang.exe must be installed separately (PATH or options) |

## Requirements

- Visual Studio 2022 (17.0 or later)
- .NET 8.0 SDK
- BasicLang compiler (BasicLang.exe) - must be in PATH or configured in options

## Installation

1. Download the `.vsix` file from the Releases page
2. Double-click to install in Visual Studio 2022
3. Restart Visual Studio

## Building from Source

### Prerequisites

- Visual Studio 2022 with:
  - .NET desktop development workload
  - Visual Studio extension development workload
- .NET 8.0 SDK

### Build Steps

```powershell
# Navigate to the extension directory
cd BasicLang.VisualStudio

# Build the SDK NuGet package
dotnet pack src/BasicLang.SDK -c Release

# Build the VSIX (requires VS 2022 MSBuild)
& "C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
    src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj -p:Configuration=Release
```

**Output files:**
- VSIX: `src/BasicLang.VisualStudio/BasicLang.VisualStudio.vsix`
- SDK NuGet: `src/BasicLang.SDK/bin/Release/BasicLang.SDK.1.0.0.nupkg`

> **Note:** The VSIX must be built with Visual Studio's MSBuild (not `dotnet build`) because it requires VSSDK targets for VSIX packaging.

## Project Structure

```
BasicLang.VisualStudio/
├── src/
│   ├── BasicLang.VisualStudio/     # Main VSIX project
│   │   ├── Package/                # VS package registration
│   │   ├── ProjectSystem/          # CPS project system
│   │   ├── LanguageService/        # LSP client
│   │   ├── Commands/               # Menu commands
│   │   ├── Options/                # Options pages
│   │   └── Templates/              # Project & item templates
│   └── BasicLang.SDK/              # MSBuild SDK NuGet
│       └── Sdk/                    # props/targets files
└── tests/
```

## Configuration

### Options

Access via **Tools > Options > BasicLang**:

**General:**
- Auto-start language server
- Language server path
- Semantic highlighting, inlay hints, CodeLens, diagnostics toggles (semantic highlighting, inlay hints, and CodeLens currently have no effect - the language server does not implement them yet)

**Compiler:**
- Default backend
- Treat warnings as errors
- Enable optimizations
- Debug info generation
- Target framework

### Project Properties

In `.blproj` files:

```xml
<Project Sdk="BasicLang.SDK/1.0.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Backend>CSharp</Backend>
  </PropertyGroup>
</Project>
```

## Commands

All commands live in the **BasicLang** top-level menu and are enabled when a `.bas`, `.bl`, or `.blproj` file is active. No keyboard shortcuts are bound by the extension.

| Command | Description |
|---------|-------------|
| Build Project | Build the current BasicLang project with BasicLang.exe |
| Run | Build and run the program (no debugger attached) |
| Change Backend... | Change the compiler backend (CSharp, MSIL, LLVM, C++) |
| Restart Language Server | Restart the LSP server |
| Go to Definition | Navigate to symbol definition (context menu) |
| Find All References | Present but non-functional until the language server implements references |

## License

MIT License - see LICENSE.txt for details.
