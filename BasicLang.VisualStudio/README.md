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

## Requirements

- Visual Studio 2022 (17.0 or later)
- .NET 8.0 SDK
- BasicLang compiler (BasicLang.exe)

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
# Clone the repository
git clone https://github.com/visualgamestudio/basiclang.git
cd basiclang/BasicLang.VisualStudio

# Build the SDK
dotnet pack src/BasicLang.SDK -c Release

# Build the VSIX
dotnet build src/BasicLang.VisualStudio -c Release
```

The VSIX will be output to `src/BasicLang.VisualStudio/bin/Release/BasicLang.VisualStudio.vsix`.

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
