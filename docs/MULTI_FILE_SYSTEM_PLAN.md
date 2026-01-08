# Multi-File System Implementation Plan

## Overview

Implement a complete multi-file compilation system for BasicLang with:
- **Automatic visibility** of Public symbols across project files
- **Import** for shorthand access and external libraries
- **#Include** for header files (.bh)
- **Using** for .NET namespaces (already implemented)

---

## File Types

| Extension | Purpose | Default Visibility | Prefix Required |
|-----------|---------|-------------------|-----------------|
| `.bas`/`.bl` | Code file | Private | `ModuleName.Symbol` |
| `.mod` | Module (globals) | Public | `ModuleName.Symbol` |
| `.cls` | Class file | Class-scoped | `ClassName` |
| `.bh` | Header file | Copied/Extern | N/A |
| `.frm` | Form + code-behind | N/A | N/A |

---

## Syntax Summary

```basic
' .NET namespaces
Using System.IO
Using System.Collections.Generic

' Shorthand for project modules (optional)
Import Utils                    ' Can now use PrintMessage() instead of Utils.PrintMessage()

' External libraries (required)
Import ThirdPartyLib            ' Must import to use external libs

' Header files (preprocessor)
#Include "Constants.bh"
#Include "GameTypes.bh"
```

---

## Implementation Phases

### Phase 1: Multi-File Symbol Resolution
**Goal:** All Public symbols visible project-wide with ModuleName prefix

### Phase 2: Import for Shorthand
**Goal:** Import brings symbols into local scope (no prefix needed)

### Phase 3: Header File System (#Include)
**Goal:** Preprocessor-style header inclusion for .bh files

### Phase 4: External Library Support
**Goal:** Import external compiled libraries

---

## Phase 1: Multi-File Symbol Resolution

### 1.1 Update BuildService to Combine Files

**File:** `VisualGameStudio.ProjectSystem/Services/BuildService.cs`

```csharp
// Current: Compiles each file separately, only keeps last output
// New: Combine all files into single compilation

private async Task<BuildResult> CompileWithBasicLangApiAsync(
    BasicLangProject project,
    List<ProjectItem> sourceFiles,
    CancellationToken cancellationToken)
{
    var result = new BuildResult();
    var allModules = new List<IRModule>();
    var globalSymbolTable = new SymbolTable();

    // Phase 1: Parse all files and collect symbols
    foreach (var sourceFile in sourceFiles)
    {
        var parseResult = ParseFile(sourceFile);
        CollectPublicSymbols(parseResult, globalSymbolTable);
    }

    // Phase 2: Semantic analysis with shared symbol table
    foreach (var sourceFile in sourceFiles)
    {
        var module = AnalyzeAndBuildIR(sourceFile, globalSymbolTable);
        allModules.Add(module);
    }

    // Phase 3: Merge all modules into single output
    var mergedModule = MergeModules(allModules);

    // Phase 4: Generate code
    result.GeneratedCode = GenerateCode(mergedModule);
}
```

### 1.2 Create Project Symbol Table

**File:** `BasicLang/ProjectSymbolTable.cs` (NEW)

```csharp
public class ProjectSymbolTable
{
    // Module name -> Symbol table for that module
    private Dictionary<string, ModuleSymbols> _modules = new();

    public void RegisterModule(string moduleName, ModuleSymbols symbols)
    {
        _modules[moduleName] = symbols;
    }

    // Lookup symbol with module prefix: "Utils.PrintMessage"
    public Symbol LookupQualified(string moduleName, string symbolName)
    {
        if (_modules.TryGetValue(moduleName, out var module))
        {
            return module.GetPublicSymbol(symbolName);
        }
        return null;
    }

    // Get all public symbols from a module (for Import shorthand)
    public IEnumerable<Symbol> GetPublicSymbols(string moduleName)
    {
        if (_modules.TryGetValue(moduleName, out var module))
        {
            return module.GetAllPublicSymbols();
        }
        return Enumerable.Empty<Symbol>();
    }
}

public class ModuleSymbols
{
    public string ModuleName { get; set; }
    public string FilePath { get; set; }

    private Dictionary<string, Symbol> _publicSymbols = new();
    private Dictionary<string, Symbol> _privateSymbols = new();

    public void AddSymbol(Symbol symbol)
    {
        if (symbol.IsPublic)
            _publicSymbols[symbol.Name] = symbol;
        else
            _privateSymbols[symbol.Name] = symbol;
    }

    public Symbol GetPublicSymbol(string name) =>
        _publicSymbols.TryGetValue(name, out var s) ? s : null;

    public IEnumerable<Symbol> GetAllPublicSymbols() => _publicSymbols.Values;
}
```

### 1.3 Update SemanticAnalyzer for Qualified Names

**File:** `BasicLang/SemanticAnalyzer.cs`

```csharp
// Add project symbol table
private ProjectSymbolTable _projectSymbols;

public void SetProjectSymbols(ProjectSymbolTable projectSymbols)
{
    _projectSymbols = projectSymbols;
}

// Update symbol lookup to check qualified names
private Symbol LookupSymbol(string name)
{
    // Check if it's a qualified name (ModuleName.Symbol)
    if (name.Contains('.'))
    {
        var parts = name.Split('.', 2);
        var moduleName = parts[0];
        var symbolName = parts[1];

        var symbol = _projectSymbols?.LookupQualified(moduleName, symbolName);
        if (symbol != null) return symbol;
    }

    // Check local scope
    var localSymbol = _currentScope.Lookup(name);
    if (localSymbol != null) return localSymbol;

    // Check imported symbols (Phase 2)
    var importedSymbol = _importedSymbols.TryGetValue(name, out var s) ? s : null;
    if (importedSymbol != null) return importedSymbol;

    return null;
}
```

### 1.4 Add Public/Private Keywords to Parser

**File:** `BasicLang/Parser.cs`

```basic
' Syntax to support:
Public Sub DoSomething()        ' Visible to other files
Private Sub Helper()            ' Only visible in this file (default)
Friend Sub InternalFunc()       ' Visible within project only

Public Dim SharedVar As Integer ' Public variable
Private Dim localVar As Integer ' Private variable (default)
```

```csharp
private FunctionNode ParseFunction()
{
    var visibility = Visibility.Private; // Default

    if (Match(TokenType.Public))
        visibility = Visibility.Public;
    else if (Match(TokenType.Private))
        visibility = Visibility.Private;
    else if (Match(TokenType.Friend))
        visibility = Visibility.Friend;

    // ... rest of parsing

    node.Visibility = visibility;
    return node;
}
```

### 1.5 Update Code Generator to Merge Modules

**File:** `BasicLang/CSharpBackend.cs`

```csharp
public string Generate(List<IRModule> modules, string projectNamespace)
{
    var sb = new StringBuilder();

    // Using statements
    sb.AppendLine("using System;");
    // ... other usings

    sb.AppendLine($"namespace {projectNamespace}");
    sb.AppendLine("{");

    // Generate each module as a static class
    foreach (var module in modules)
    {
        GenerateModuleAsClass(sb, module);
    }

    sb.AppendLine("}");
    return sb.ToString();
}

private void GenerateModuleAsClass(StringBuilder sb, IRModule module)
{
    var visibility = module.IsPublicModule ? "public" : "internal";
    sb.AppendLine($"    {visibility} static class {module.Name}");
    sb.AppendLine("    {");

    // Generate constants, variables, functions...
    foreach (var func in module.Functions)
    {
        var funcVisibility = func.IsPublic ? "public" : "private";
        sb.AppendLine($"        {funcVisibility} static ...");
    }

    sb.AppendLine("    }");
}
```

---

## Phase 2: Import for Shorthand

### 2.1 Update Import Directive Handling

**File:** `BasicLang/SemanticAnalyzer.cs`

```csharp
private Dictionary<string, Symbol> _importedSymbols = new();

public void Visit(ImportDirectiveNode node)
{
    // Check if it's a project module
    if (_projectSymbols.HasModule(node.Module))
    {
        // Import all public symbols into local scope (shorthand)
        foreach (var symbol in _projectSymbols.GetPublicSymbols(node.Module))
        {
            if (_importedSymbols.ContainsKey(symbol.Name))
            {
                Warning($"Symbol '{symbol.Name}' already imported, using version from {node.Module}",
                    node.Line, node.Column);
            }
            _importedSymbols[symbol.Name] = symbol;
        }
    }
    else
    {
        // External library - handle separately (Phase 4)
        ImportExternalLibrary(node.Module);
    }
}
```

### 2.2 Update Symbol Resolution Priority

```csharp
private Symbol LookupSymbol(string name)
{
    // Priority:
    // 1. Local scope (variables in current function)
    // 2. Module scope (module-level variables/functions)
    // 3. Imported symbols (from Import statements)
    // 4. Qualified lookup (ModuleName.Symbol)

    // 1. Local scope
    var local = _currentScope.LookupLocal(name);
    if (local != null) return local;

    // 2. Module scope
    var moduleLevel = _currentModuleScope.Lookup(name);
    if (moduleLevel != null) return moduleLevel;

    // 3. Imported symbols
    if (_importedSymbols.TryGetValue(name, out var imported))
        return imported;

    // 4. Not found - caller should try qualified lookup
    return null;
}
```

---

## Phase 3: Header File System (#Include)

### 3.1 Add Preprocessor

**File:** `BasicLang/Preprocessor.cs` (NEW)

```csharp
public class Preprocessor
{
    private HashSet<string> _includedFiles = new();  // Prevent double-include
    private string _baseDirectory;

    public string Process(string source, string filePath)
    {
        _baseDirectory = Path.GetDirectoryName(filePath);
        var lines = source.Split('\n');
        var result = new StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("#Include"))
            {
                var includePath = ParseIncludePath(trimmed);
                var fullPath = ResolvePath(includePath);

                if (_includedFiles.Contains(fullPath))
                {
                    // Already included - skip (include guard)
                    result.AppendLine($"' [Already included: {includePath}]");
                    continue;
                }

                _includedFiles.Add(fullPath);

                // Read and recursively process the header
                var headerContent = File.ReadAllText(fullPath);
                var processedHeader = Process(headerContent, fullPath);

                result.AppendLine($"' [Begin: {includePath}]");
                result.AppendLine(processedHeader);
                result.AppendLine($"' [End: {includePath}]");
            }
            else
            {
                result.AppendLine(line);
            }
        }

        return result.ToString();
    }

    private string ParseIncludePath(string line)
    {
        // #Include "path/to/file.bh"
        var match = Regex.Match(line, @"#Include\s+""([^""]+)""");
        if (match.Success)
            return match.Groups[1].Value;

        throw new PreprocessorException($"Invalid #Include syntax: {line}");
    }

    private string ResolvePath(string includePath)
    {
        // Try relative to current file first
        var relative = Path.Combine(_baseDirectory, includePath);
        if (File.Exists(relative))
            return Path.GetFullPath(relative);

        // Try project include paths
        foreach (var searchPath in _searchPaths)
        {
            var candidate = Path.Combine(searchPath, includePath);
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        throw new PreprocessorException($"Include file not found: {includePath}");
    }
}
```

### 3.2 Header File Syntax

**File:** `GameTypes.bh`

```basic
' ══════════════════════════════════════════════════════════
' GameTypes.bh - Common types and constants
' ══════════════════════════════════════════════════════════
#Once    ' Only include once (optional - automatic)

' Constants (copied to each including file)
Const MAX_ENTITIES As Integer = 1000
Const TILE_SIZE As Integer = 32

' Type definitions (copied to each including file)
Type Vector2
    X As Single
    Y As Single
End Type

Type Rectangle
    X As Single
    Y As Single
    Width As Single
    Height As Single
End Type

' Extern declarations (shared, defined elsewhere)
Extern Dim GameScore As Integer
Extern Dim PlayerHealth As Integer

' Function declarations (implementation in .bas file)
Declare Sub InitGame(width As Integer, height As Integer)
Declare Function CreateEntity(x As Single, y As Single) As Integer

' Inline functions (expanded at include site)
Inline Function Vec2Add(a As Vector2, b As Vector2) As Vector2
    Dim result As Vector2
    result.X = a.X + b.X
    result.Y = a.Y + b.Y
    Return result
End Function
```

### 3.3 Add Header-Specific Tokens

**File:** `BasicLang/BasicLangLexer.cs`

```csharp
// New tokens for headers
{ "#Include", TokenType.PreprocessorInclude },
{ "#Once", TokenType.PreprocessorOnce },
{ "#If", TokenType.PreprocessorIf },
{ "#Else", TokenType.PreprocessorElse },
{ "#EndIf", TokenType.PreprocessorEndIf },
{ "#Define", TokenType.PreprocessorDefine },
{ "Extern", TokenType.Extern },
{ "Declare", TokenType.Declare },
{ "Inline", TokenType.Inline },
```

### 3.4 Parse Header-Specific Constructs

**File:** `BasicLang/Parser.cs`

```csharp
private DeclareNode ParseDeclare()
{
    Consume(TokenType.Declare, "Expected 'Declare'");

    var node = new DeclareNode(Previous().Line, Previous().Column);

    if (Match(TokenType.Sub))
    {
        node.Kind = DeclarationKind.Sub;
    }
    else if (Match(TokenType.Function))
    {
        node.Kind = DeclarationKind.Function;
    }

    node.Name = Consume(TokenType.Identifier, "Expected name").Lexeme;
    node.Parameters = ParseParameterList();

    if (node.Kind == DeclarationKind.Function)
    {
        Consume(TokenType.As, "Expected 'As'");
        node.ReturnType = ParseTypeReference();
    }

    return node;
}

private ExternVariableNode ParseExternVariable()
{
    Consume(TokenType.Extern, "Expected 'Extern'");
    Consume(TokenType.Dim, "Expected 'Dim'");

    var node = new ExternVariableNode(Previous().Line, Previous().Column);
    node.Name = Consume(TokenType.Identifier, "Expected variable name").Lexeme;
    Consume(TokenType.As, "Expected 'As'");
    node.Type = ParseTypeReference();

    return node;
}

private InlineFunctionNode ParseInlineFunction()
{
    Consume(TokenType.Inline, "Expected 'Inline'");

    // Parse like a normal function but mark as inline
    var func = ParseFunction();
    return new InlineFunctionNode(func) { IsInline = true };
}
```

---

## Phase 4: External Library Support

### 4.1 Library Manifest

**File:** `mylib.blpkg` (JSON manifest)

```json
{
    "name": "MyLibrary",
    "version": "1.0.0",
    "exports": [
        {
            "name": "HelperFunction",
            "type": "function",
            "returnType": "Integer",
            "parameters": [
                { "name": "x", "type": "Integer" }
            ]
        }
    ],
    "dependencies": [],
    "assembly": "MyLibrary.dll"
}
```

### 4.2 External Import Resolution

```csharp
private void ImportExternalLibrary(string libraryName)
{
    // Look for library in:
    // 1. Project's lib/ folder
    // 2. Global BasicLang packages folder
    // 3. NuGet packages (for .NET interop)

    var manifest = FindLibraryManifest(libraryName);
    if (manifest == null)
    {
        Error($"External library not found: {libraryName}");
        return;
    }

    // Load symbols from manifest
    foreach (var export in manifest.Exports)
    {
        var symbol = CreateSymbolFromExport(export);
        _importedSymbols[symbol.Name] = symbol;
    }

    // Track assembly reference for code generation
    _requiredAssemblies.Add(manifest.Assembly);
}
```

---

## File Changes Summary

| File | Changes |
|------|---------|
| `BuildService.cs` | Multi-file compilation, symbol collection |
| `ProjectSymbolTable.cs` | NEW - Project-wide symbol management |
| `SemanticAnalyzer.cs` | Qualified name lookup, import handling |
| `Parser.cs` | Public/Private/Friend, Declare, Extern, Inline |
| `BasicLangLexer.cs` | New tokens for preprocessor/headers |
| `Preprocessor.cs` | NEW - #Include processing |
| `CSharpBackend.cs` | Multi-module code generation |
| `CppBackend.cs` | Header file generation |
| `IRNodes.cs` | New IR nodes for declarations |
| `ASTNodes.cs` | New AST nodes for header constructs |

---

## Testing Plan

### Test 1: Basic Multi-File
```basic
' Utils.bas
Public Sub PrintHello()
    Console.WriteLine("Hello")
End Sub

' Main.bas
Sub Main()
    Utils.PrintHello()   ' Should work without Import
End Sub
```

### Test 2: Import Shorthand
```basic
' Main.bas
Import Utils

Sub Main()
    PrintHello()         ' Should work with Import
End Sub
```

### Test 3: Header Include
```basic
' Constants.bh
Const MAX_SIZE As Integer = 100

' Main.bas
#Include "Constants.bh"

Sub Main()
    Dim arr(MAX_SIZE) As Integer   ' Should see MAX_SIZE
End Sub
```

### Test 4: Visibility
```basic
' Utils.bas
Public Sub PublicFunc()
End Sub

Private Sub PrivateFunc()
End Sub

' Main.bas
Sub Main()
    Utils.PublicFunc()    ' OK
    Utils.PrivateFunc()   ' ERROR: Private
End Sub
```

---

## Implementation Order

1. **Phase 1A**: Add Public/Private keywords to parser
2. **Phase 1B**: Create ProjectSymbolTable
3. **Phase 1C**: Update BuildService for multi-file
4. **Phase 1D**: Update SemanticAnalyzer for qualified names
5. **Phase 1E**: Update CSharpBackend for merged output
6. **Phase 2A**: Implement Import shorthand in SemanticAnalyzer
7. **Phase 3A**: Create Preprocessor for #Include
8. **Phase 3B**: Add header-specific parsing
9. **Phase 4**: External library support (future)

---

## Estimated Scope

| Phase | Files to Change | Complexity |
|-------|-----------------|------------|
| 1 | 5 files | Medium |
| 2 | 2 files | Low |
| 3 | 4 files (2 new) | Medium |
| 4 | 3 files | High |

**Recommended start:** Phase 1 - Get multi-file working first, then add Import shorthand and headers.
