# Visual Game Studio Engine - Project Memory

This file contains persistent context for Claude Code sessions.

## Project Overview

A complete game development platform with a custom programming language (BasicLang), a full-featured IDE, and a 2D game engine built on Raylib.

## Solution Structure

### Core Projects (In Solution)

| Project | Language | Description |
|---------|----------|-------------|
| **BasicLang** | C# (~45K lines) | Full compiler for VB-like language with lexer, parser, semantic analysis, LSP server, and multiple backends (C#, LLVM, MSIL, C++) |
| **VisualGameStudio.Core** | C# | Core abstractions, interfaces, and models for the IDE |
| **VisualGameStudio.Editor** | C# | Avalonia-based code editor with syntax highlighting, IntelliSense, folding |
| **VisualGameStudio.ProjectSystem** | C# | Project/solution management, build service, LSP client, debugging |
| **VisualGameStudio.Shell** | C# | Main IDE application shell, windows, panels, menus |
| **VisualGameStudio.Tests** | C# | Unit test suite (800+ tests) |
| **RaylibWrapper** | VB.NET | P/Invoke bindings to the C++ game engine DLL |
| **VisualGameStudioEngine** | C++ | Core 2D game engine DLL built on Raylib |
| **CPPengineTest** | C++ | Native engine test project |
| **TestVbDLL** | VB.NET | Sample VB.NET game using the wrapper |
| **VisualGameStudio** | VB.NET | Legacy IDE (superseded by Avalonia version) |

### Extension Projects (Not in Solution)

| Project | Type | Description |
|---------|------|-------------|
| **BasicLang.VisualStudio** | VSIX | **NEW** CPS-based VS 2022 extension with full project system, LSP, templates |
| **VS.BasicLang** | VSIX | Legacy VS 2022 extension (MEF-based, SDK-style project workaround) |
| **vscode-basiclang** | VS Code Extension | VS Code extension with syntax highlighting and snippets |

### Test/Sample Projects

| Folder | Purpose |
|--------|---------|
| **SampleGames/** | Sample games (Pong, SpaceShooter) |
| **TestWinForms/** | WinForms application example in BasicLang |
| **TestMultiFile/** | Multi-file compilation tests |
| **TestGame/** | Game development test project |

### Output Folders

| Folder | Contents |
|--------|----------|
| **IDE/** | Pre-built IDE binaries and dependencies |
| **bin/**, **obj/** | Build output |
| **docs/** | Documentation |

## Architecture

```
┌─────────────────────┐    ┌─────────────────────┐
│  BasicLang Compiler │    │ VisualGameStudioEngine│
│   (C# - 45K LOC)    │    │      (C++ DLL)       │
│  - Lexer/Parser     │    │  - Raylib wrapper    │
│  - Semantic Analysis│    │  - 2D rendering      │
│  - IR Generation    │    │  - Input/Audio       │
│  - Code Backends    │    │  - ECS (partial)     │
│  - LSP Server       │    └──────────┬──────────┘
└─────────┬───────────┘               │
          │                           │
          ▼                           ▼
┌─────────────────────┐    ┌─────────────────────┐
│  Visual Game Studio │    │   RaylibWrapper     │
│   IDE (Avalonia)    │    │     (VB.NET)        │
│  - Code Editor      │    │  - P/Invoke layer   │
│  - IntelliSense     │    │  - Type-safe API    │
│  - Project System   │    └─────────────────────┘
│  - Debugger         │
└─────────────────────┘
```

## Key File Locations

- **Compiler**: `BasicLang/` - Lexer.cs, Parser.cs, SemanticAnalyzer.cs, IRBuilder.cs, CSharpBackend.cs
- **LSP Server**: `BasicLang/LSP/` - BasicLangLanguageServer.cs, CompletionService.cs
- **IDE Core**: `VisualGameStudio.Core/Abstractions/` - Service interfaces
- **Editor**: `VisualGameStudio.Editor/Controls/` - CodeEditorControl.axaml.cs
- **Shell**: `VisualGameStudio.Shell/` - MainWindow, ViewModels
- **Game Engine**: `VisualGameStudioEngine/` - C++ source, `RaylibWrapper/` - VB.NET bindings
- **IDE Binaries**: `IDE/` - Ready-to-run IDE executables

## Build Commands

```bash
# Build BasicLang compiler
dotnet build BasicLang/BasicLang.csproj -c Release

# Build entire IDE
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release

# Run IDE
./IDE/VisualGameStudio.exe

# Compile a BasicLang file
./IDE/BasicLang.exe compile MyFile.bas --target=csharp

# Build a BasicLang project
./IDE/BasicLang.exe build MyProject.blproj
```

## BasicLang Language Features

- VB-like syntax with modern features
- Pattern matching with When guards
- LINQ support
- Async/Await
- Classes, interfaces, modules
- Generics/templates
- Multiple backends: C#, LLVM, MSIL, C++
- .NET interop via `Using` directive
- Multi-file projects with Import/Using

## Common Tasks

1. **Fix compiler bugs**: Edit files in `BasicLang/`
2. **Add IDE features**: Edit `VisualGameStudio.Editor/` or `VisualGameStudio.Shell/`
3. **Improve IntelliSense**: Edit `BasicLang/LSP/CompletionService.cs`
4. **Add game engine features**: Edit C++ in `VisualGameStudioEngine/`, update `RaylibWrapper/`
5. **Update IDE binaries**: Copy from build output to `IDE/` folder

## Recent Bug Fixes (January 2026)

### For Loop Improvements
- **Inline type declaration**: `For i As Integer = 1 To 10` now supported (Parser.cs, ASTNodes.cs)
- **Negative Step fix**: Descending loops (`Step -2`) now use `>=` comparison instead of `<=` (IRBuilder.cs)

### VB-Style Array Support
- **Array declaration**: `Dim arr() As Integer = {1, 2, 3}` now supported alongside C#-style `arr[]` (Parser.cs)
- **Array indexing**: `arr(i)` correctly generates `arr[i]` in C# output (IRBuilder.cs, SemanticAnalyzer.cs)

### Forward Reference Support
- **Two-pass semantic analysis**: Functions/Subs can now be called before their definition
- Added `RegisterDeclarations()` pass that pre-registers all function signatures (SemanticAnalyzer.cs)

### .NET Type Method Chaining
- **Method chaining**: `s.Trim().ToUpper()` now correctly returns `String` instead of `Object`
- Added `LookupNetTypeMember()` to resolve .NET method return types (SemanticAnalyzer.cs)
- Added `GetStringMethodReturnType()` with known return types for String methods
- Added `GetCommonMethodReturnType()` for StringBuilder and collection methods

### For Each Loop Improvements
- **Optional type declaration**: `For Each n In numbers` now works (type inferred from collection)
- Both `For Each n As Integer In arr` (explicit) and `For Each n In arr` (inferred) are supported
- Element type is inferred from array element type or generic collection type argument

### Generic Collection Indexer Fix
- **Bracket syntax**: `dict("key")` now generates proper C# `dict["key"]` instead of invalid `dict("key")`
- Added `IRIndexerAccess` class to IRNodes.cs for collection indexing
- Added `IsIndexableGenericType()` helper to detect List<T>, Dictionary<K,V>, etc.
- Updated IRBuilder.cs to emit `IRIndexerAccess` for generic collections
- Updated CSharpBackend.cs to generate `collection[index]` syntax

### For Each Native IR Generation
- **Native foreach**: For Each loops now emit `IRForEach` instruction instead of manual index loops
- Added `IRForEach` class to IRNodes.cs
- CSharpBackend generates native C# `foreach` statement
- Loop variable is declared by foreach, not separately

### Constructor Argument Validation
- **Argument count validation**: `New Person("Alice")` when constructor takes 2 args now errors
- **Type validation**: Argument types are checked against parameter types
- Constructor symbols registered in class type's Members dictionary as `.ctor{N}` (N = param count)
- Helpful error messages show available constructors

### Key Implementation Details
- `IsNegativeStep()` helper in IRBuilder.cs detects negative loop steps
- `GetTypeInfoFromName()` helper resolves type names for inline declarations
- Array access detection in `Visit(CallExpressionNode)` distinguishes `func()` from `arr()`
- Symbol.ReturnType must be explicitly set for pre-registered functions
- `ResolveNetTypeName()` maps .NET type names to BasicLang TypeInfo
- Constructor symbols stored as `.ctor0`, `.ctor1`, `.ctor2` etc. to support overloading

### Base Constructor Validation
- **MyBase.New() validation**: Base constructor calls now validated for argument count and types
- Error messages show available base constructors when mismatch detected
- Validates both argument count and type compatibility

### MSIL Backend Base Class Context
- **Base class method calls**: `MyBase.Method()` now correctly uses actual base class name
- Added `_currentClass` tracking field to MSILBackend
- Base calls no longer hardcoded to `System.Object`

### Conditional Compilation Preprocessor
- **#IfDef / #IfNDef / #Else / #EndIf**: Full conditional compilation support
- Preprocessor runs before lexer/parser in Compiler.cs
- Stack-based implementation supports nested conditionals
- Inactive code blocks are commented out in preprocessed output
- Validates unclosed conditional blocks and duplicate #Else

### LLVM Backend Exception Handling
- **Try/Catch blocks**: Implemented using LLVM `invoke` and `landingpad` pattern
- Uses personality function for C++ exception handling ABI
- Supports catch-all exceptions with `catch i8* null`
- Exception pointer extracted from landing pad for catch block

### ForEach in LLVM/MSIL Backends
- **LLVM ForEach**: Index-based loop with `__get_collection_length` and `__get_collection_element` helper functions
- **MSIL ForEach**: Proper `GetEnumerator` pattern using `IEnumerable` interface
- Loop variable loaded at each iteration from collection element

### Indexer Access in LLVM/MSIL Backends
- **LLVM Indexer**: Uses `getelementptr` instruction for array/collection access
- **MSIL Indexer**: Uses `get_Item` method call for collection indexers
- Both single and multi-dimensional indexing supported
- Results stored in `_llvmNames` dictionary for subsequent use

### LLVM Event Handlers
- **AddHandler/RemoveHandler**: Implemented delegate combine/remove pattern
- Uses `@llvm.delegate.combine` and `@llvm.delegate.remove` intrinsics
- Event delegate pointers stored for later invocation

### Generic Type Parameters (TemplateDeclarationNode)
- **Template declarations**: `Visit(TemplateDeclarationNode)` now properly implements generic type parameters
- Type parameters registered in current scope before processing inner declaration
- Injects type parameters into ClassNode, FunctionNode, SubroutineNode GenericParameters
- Type parameters resolved via `ResolveTypeName()` which checks scope for `SymbolKind.TypeParameter`

### Bitwise Operators
- **Full bitwise support**: `BitwiseAnd (&)`, `BitwiseOr (|)`, `BitwiseNot (~)`, `Xor (^)`, `Shl (<<)`, `Shr (>>)`
- Separated logical (short-circuit) operators from bitwise in IRNodes.cs
- CSharpBackend generates proper `&` and `|` for bitwise operations
- IRBuilder maps both VB-style (`And`, `Or`, `Xor`, `Shl`, `Shr`) and C-style (`&&`, `||`, `^`, `<<`, `>>`) syntax

### Compound Assignment Operators
- **All compound assignments**: `+=`, `-=`, `*=`, `/=`, `\=`, `%=`, `&=`, `And=`, `Or=`, `Xor=`, `<<=`, `>>=`
- Integer division assignment (`\=`) and modulo assignment (`%=` / `Mod=`)
- String concatenation assignment (`&=`)
- Bitwise compound assignments (`And=`, `Or=`, `Xor=`)
- Shift compound assignments (`<<=`, `>>=`)

### Multi-Dimensional Array Support
- **UBound/LBound overloads**: Added overloads accepting `Array` type for multi-dimensional arrays
- VB uses 1-based dimension indexing, mapped to .NET 0-based internally
- Single-dimensional arrays throw if dimension != 1
- Multi-dimensional arrays validate dimension range (1 to array.Rank)

### IDE Test Infrastructure Fix
- **DebugService.Dispose() deadlock**: Fixed thread pool starvation when running 1000+ tests in parallel
- Removed blocking `Task.Run(...).Wait()` pattern that caused deadlocks under high load
- Replaced with synchronous cleanup: `_cts?.Cancel()`, `CleanupProcesses()`, state update
- Tests now pass individually; full suite runs without hanging

### VS.BasicLang Project Templates (Revised Approach)
- **SDK-style project**: Template uses `.csproj` with `Microsoft.NET.Sdk` for VS 2022 native handling
- Template creates BasicLang projects that VS can manage natively (no custom project factory needed)
- Template files in `VS.BasicLang/Templates/Projects/BasicLangConsoleApp/`:
  - `BasicLangConsoleApp.csproj` - SDK-style project with BasicLang markers
  - `BasicLangConsoleApp.vstemplate` - Template metadata with `<ProjectType>CSharp</ProjectType>`
  - `Program.bas` - BasicLang source file
  - `__TemplateIcon.png` - Template icon
- Build creates VSIX manually using `ZipDirectory` (VSSDK tools not available from dotnet CLI)
- Build command: `dotnet build VS.BasicLang -c Release`
- VSIX output: `VS.BasicLang/VS.BasicLang.vsix`
- Template appears in VS 2022 under C# project templates (VS 2022 limitation for custom languages)
- **Version 1.3.0**: Current extension version

### VS.BasicLang Build Process
The build uses custom MSBuild targets since VSSDK tools aren't available from `dotnet build`:
1. `CreateTemplateZip` target - Zips template source files to `ProjectTemplates/BasicLang/BasicLangConsoleApp.zip`
2. `CreateVsixManual` target - Creates VSIX by:
   - Copying DLL, pkgdef, template zip to build directory
   - Creating `[Content_Types].xml` for VSIX package
   - Zipping to `VS.BasicLang.vsix`

### VS 2022 Custom Language Limitations
- VS 2022 only recognizes built-in languages for New Project dialog filters: `csharp`, `visualbasic`, `fsharp`, `cpp`, etc.
- Custom language names like "BasicLang" won't appear in the language dropdown
- Solution: Use `<ProjectType>CSharp</ProjectType>` to make template appear under C# (practical workaround)
- Full custom language support would require implementing a complete CPS (Common Project System) extension

### VS.BasicLang Key Files Modified (January 2026)
- **VS.BasicLang.csproj**: Added `VSToolsPath` property, `CreateTemplateZip` and `CreateVsixManual` MSBuild targets
- **BasicLangPackage.cs**: Removed `[ProvideProjectFactory]` attribute and factory registration (not needed for SDK-style projects)
- **BasicLang.pkgdef**: Simplified - removed project factory registrations, kept language service registrations
- **source.extension.vsixmanifest**: Version 1.3.0, updated description
- **Templates/Projects/BasicLangConsoleApp/BasicLangConsoleApp.vstemplate**: Uses `<ProjectType>CSharp</ProjectType>`, `<LanguageTag>csharp</LanguageTag>`
- **Templates/Projects/BasicLangConsoleApp/BasicLangConsoleApp.csproj**: New SDK-style project file with `<IsBasicLangProject>true</IsBasicLangProject>` marker
- Removed old `ConsoleApp.blproj` template file

### BasicLang.VisualStudio - CPS Extension (January 2026)

A complete CPS (Common Project System) based Visual Studio 2022 extension for BasicLang, following the RemObjects Elements model.

#### Original Implementation Plan Prompt

The following detailed plan was provided to implement the extension:

```
Create a complete CPS (Common Project System) based Visual Studio 2022 extension for BasicLang,
following the RemObjects Elements model. The extension will provide proper project system integration,
LSP-based IntelliSense, debugging support, and MSBuild integration.

Architecture:
BasicLang.VisualStudio/
├── src/
│   ├── BasicLang.VisualStudio/     # Main VSIX (CPS-based)
│   └── BasicLang.SDK/              # MSBuild SDK NuGet package
└── tests/

Implementation Phases:
1. Solution Setup - Create project structure with VSIX and SDK projects
2. CPS Project System - Guids, ProjectCapability, UnconfiguredProject, ConfiguredProject, TreeProvider
3. MSBuild SDK - Sdk.props/Sdk.targets for BasicLang compilation
4. LSP Client - ILanguageClient connecting to BasicLang.exe --lsp
5. Templates - Console, Library, WinForms, WPF project templates + Class/Module/Interface items
6. Commands & Options - Build, Run, Change Backend, Restart Server + Options pages

Key NuGet Packages:
- Microsoft.VisualStudio.SDK 17.9.37000
- Microsoft.VisualStudio.ProjectSystem 17.9.380
- Microsoft.VisualStudio.LanguageServer.Client 17.10.124
```

#### What Was Implemented

**Completed:**
- ✅ Solution structure with `BasicLang.VisualStudio.sln`
- ✅ CPS project system files (simplified for public NuGet APIs):
  - `Package/Guids.cs` - All GUIDs for package, commands, project type
  - `Package/BasicLangPackage.cs` - VS Package with auto-load
  - `ProjectSystem/BasicLangProjectCapability.cs` - CPS capability export
  - `ProjectSystem/BasicLangUnconfiguredProject.cs` - Project registration
  - `ProjectSystem/BasicLangConfiguredProject.cs` - Configuration services
  - `ProjectSystem/BasicLangProjectTreeProvider.cs` - Solution Explorer icons
  - `ProjectSystem/BasicLangProjectFactory.cs` - Flavored project factory
- ✅ LSP Client (`LanguageService/BasicLangLanguageClient.cs`):
  - Implements `ILanguageClient`, `ILanguageClientCustomMessage2`
  - Server discovery in extension dir → PATH → common install paths
  - Launches `BasicLang.exe --lsp` on stdin/stdout
  - `RestartServerAsync()` for manual restart
- ✅ TextMate grammar (`LanguageService/BasicLangGrammar.json`) - copied from vscode-basiclang
- ✅ Content type definitions (`LanguageService/BasicLangContentType.cs`)
- ✅ Commands (`Commands/BasicLangCommands.vsct`, `Commands/CommandHandlers.cs`):
  - Build Project, Run, Change Backend, Restart Server, Go To Definition, Find References
- ✅ Options pages:
  - `Options/GeneralOptionsPage.cs` - LSP path, auto-start, semantic highlighting
  - `Options/CompilerOptionsPage.cs` - Backend, framework, warnings, optimizations
- ✅ Project templates (created but not yet wired into VSIX):
  - ConsoleApp, ClassLibrary, WinFormsApp, WpfApp
- ✅ Item templates:
  - Class, Module, Interface
- ✅ MSBuild SDK (`BasicLang.SDK`):
  - `Sdk/Sdk.props` - Properties for BasicLang projects
  - `Sdk/Sdk.targets` - Build targets invoking BasicLang.exe
  - NuGet package: `BasicLang.SDK.1.0.0.nupkg`
- ✅ VSIX builds successfully with MSBuild
- ✅ Manual pkgdef file created (auto-generation failed with SDK-style project)

**Not Yet Implemented:**
- ❌ Templates not wired into VSIX (disabled due to SDK-style project complexities)
- ❌ Debug launch provider (CPS debug APIs not publicly available)
- ❌ BasicLang.exe not bundled in SDK tools/ folder

#### Build Commands

```bash
# Build VSIX (requires VS 2022 MSBuild)
cd BasicLang.VisualStudio/src/BasicLang.VisualStudio
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" BasicLang.VisualStudio.csproj -p:Configuration=Release

# Build SDK NuGet package
cd BasicLang.VisualStudio/src/BasicLang.SDK
dotnet pack -c Release
```

#### Output Files
- **VSIX**: `BasicLang.VisualStudio/src/BasicLang.VisualStudio/BasicLang.VisualStudio.vsix`
- **SDK NuGet**: `BasicLang.VisualStudio/src/BasicLang.SDK/bin/Release/BasicLang.SDK.1.0.0.nupkg`

#### Key Technical Challenges Solved

1. **SDK-style project + VSSDK**: Added explicit import of `Microsoft.VsSDK.targets` and custom `_CreateVsixAfterBuild` target
2. **Template manifest generation**: Overrode `GenerateTemplatesManifest` and `CreateTemplateManifests` targets to prevent errors
3. **Pkgdef generation**: Created manual `BasicLang.VisualStudio.pkgdef` since auto-generation failed
4. **VSIX manifest placeholders**: Changed `|%CurrentProject%;PkgdefProjectOutputGroup|` to explicit paths
5. **ProductArchitecture**: Added required `<ProductArchitecture>amd64</ProductArchitecture>` to installation targets
6. **NuGet package versions**: Resolved version conflicts (StreamJsonRpc 2.18.37→2.18.44, etc.)

#### Extension Features When Installed

- **Syntax Highlighting**: TextMate grammar for `.bas`, `.bl`, `.blproj` files
- **IntelliSense**: LSP-based completions, hover, diagnostics via `BasicLang.exe --lsp`
- **Commands**: BasicLang menu with Build, Run, Change Backend, Restart Server
- **Options**: Tools → Options → BasicLang (General, Compiler settings)
- **File Icons**: VB-style icons for BasicLang files in Solution Explorer
