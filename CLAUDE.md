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
| **VS.BasicLang** | VSIX | Visual Studio 2022 extension for BasicLang syntax highlighting |
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
