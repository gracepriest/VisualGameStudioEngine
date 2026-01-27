# BasicLang Visual Studio 2022 Extension

Full Visual Studio 2022 support for the BasicLang programming language with CPS project system integration, LSP-based IntelliSense, debugging, and MSBuild integration.

## Features

- **CPS Project System**: Native project system integration for `.blproj` files
- **LSP IntelliSense**: Full language server protocol support for:
  - Code completion
  - Go to definition
  - Find all references
  - Hover information
  - Diagnostics (errors, warnings)
  - Semantic highlighting
  - Inlay hints
  - CodeLens
- **Project Templates**:
  - Console Application
  - Class Library
  - Windows Forms Application
  - WPF Application
- **Item Templates**:
  - Class
  - Module
  - Interface
- **Debugging**: F5 debugging with Debug Adapter Protocol
- **Multiple Backends**: CSharp, MSIL, LLVM, C++

## Current Status (January 2026)

| Feature | Status | Notes |
|---------|--------|-------|
| CPS Project System | ✅ Implemented | Simplified for public NuGet APIs |
| LSP Client | ✅ Implemented | Connects to `BasicLang.exe --lsp` |
| Syntax Highlighting | ✅ Implemented | TextMate grammar from vscode-basiclang |
| Menu Commands | ✅ Implemented | Build, Run, Change Backend, Restart Server |
| Options Pages | ✅ Implemented | General and Compiler settings |
| Project Templates | ⚠️ Created | Not yet wired into VSIX |
| Item Templates | ⚠️ Created | Not yet wired into VSIX |
| Debug Launch Provider | ❌ Not implemented | CPS debug APIs not publicly available |
| Bundled Compiler | ❌ Not included | SDK tools/ folder is empty |

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
- Semantic highlighting
- Inlay hints
- CodeLens
- Diagnostics

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

| Command | Shortcut | Description |
|---------|----------|-------------|
| Build Project | Ctrl+Shift+B | Build the current BasicLang project |
| Run | F5 | Run with debugging |
| Run without Debugging | Ctrl+F5 | Run without debugging |
| Go to Definition | F12 | Navigate to symbol definition |
| Find All References | Shift+F12 | Find all symbol references |
| Change Backend | - | Change compiler backend |
| Restart Language Server | - | Restart the LSP server |

## License

MIT License - see LICENSE.txt for details.
