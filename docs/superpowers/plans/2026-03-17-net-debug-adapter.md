# .NET Debug Adapter Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the interpreter-based debug adapter with a full .NET debugger that launches compiled executables, supporting breakpoints, stepping, variable inspection, and call stacks mapped to `.bas` source files.

**Architecture:** The C# backend emits `#line` directives so PDBs reference `.bas` files. A new DAP server (`NetDebugAdapter`) launches the compiled .exe, attaches via ICorDebug (through `dbgshim.dll`), and handles all debug operations. The IDE side is unchanged — it already speaks DAP.

**Tech Stack:** C#, ICorDebug COM interop, dbgshim.dll P/Invoke, System.Reflection.Metadata (PDB reading), DAP JSON-RPC over stdin/stdout

**Spec:** `docs/superpowers/specs/2026-03-17-net-debug-adapter-design.md`

---

## File Map

### New Files
| File | Responsibility |
|------|---------------|
| `BasicLang/Debugger/CorDebugWrappers.cs` | COM `[ComImport]` interface declarations for ICorDebug, ICorDebugProcess, ICorDebugThread, ICorDebugValue hierarchy, ICorDebugStepper, ICorDebugBreakpoint, ICorDebugManagedCallback/2 |
| `BasicLang/Debugger/DbgShim.cs` | P/Invoke declarations for `dbgshim.dll` (RegisterForRuntimeStartup, CreateDebuggingInterfaceFromVersion3, etc.) |
| `BasicLang/Debugger/NetDebugProcess.cs` | Process launch, dbgshim bootstrap, ICorDebug attach, ManagedCallback implementation |
| `BasicLang/Debugger/SourceMapper.cs` | Read portable PDB via System.Reflection.Metadata — map .bas line ↔ IL offset |
| `BasicLang/Debugger/ClrBreakpointManager.cs` | Breakpoint state machine (pending → bound → verified), PDB-based binding |
| `BasicLang/Debugger/VariableInspector.cs` | Read ICorDebugValue hierarchy → DAP variable format |
| `BasicLang/Debugger/NetDebugAdapter.cs` | DAP server — command routing, connects all components |

### Modified Files
| File | Change |
|------|--------|
| `BasicLang/IRNodes.cs` | Add `SourceFilePath` property to `IRFunction` |
| `BasicLang/IRBuilder.cs` | Accept `sourceFilePath` parameter in `Build()`, set on each `IRFunction` |
| `BasicLang/Compiler.cs` | Pass `unit.FilePath` to `IRBuilder.Build()` |
| `BasicLang/CSharpBackend.cs` | Emit `#line N "path"` directives using `SourceLine` and `SourceFilePath` |
| `BasicLang/Program.cs` | Route `--debug-adapter` to `NetDebugAdapter` |
| `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` | Pass `.exe` path in debug launch config |

### Test Files
| File | Tests |
|------|-------|
| `VisualGameStudio.Tests/Compiler/LineDirectiveTests.cs` | `#line` directive emission, PDB verification |
| `VisualGameStudio.Tests/Debugger/SourceMapperTests.cs` | PDB sequence point reading, line ↔ IL mapping |
| `VisualGameStudio.Tests/Debugger/ClrBreakpointManagerTests.cs` | Breakpoint state transitions |

---

## Task 1: Add `SourceFilePath` to IRFunction and propagate from compiler

**Files:**
- Modify: `BasicLang/IRNodes.cs:973-1000` (IRFunction class)
- Modify: `BasicLang/IRBuilder.cs:56-68` (Build method signature)
- Modify: `BasicLang/Compiler.cs:400-401` (CompileUnit calls IRBuilder)

- [ ] **Step 1: Add `SourceFilePath` property to `IRFunction`**

In `BasicLang/IRNodes.cs`, after the `ModuleName` property (line ~994), add:

```csharp
/// <summary>
/// Absolute path to the source .bas file this function was compiled from
/// </summary>
public string SourceFilePath { get; set; }
```

- [ ] **Step 2: Update `IRBuilder.Build()` to accept and store file path**

In `BasicLang/IRBuilder.cs`, change the `Build` method signature (line ~59):

```csharp
public IRModule Build(ProgramNode program, string moduleName = "main", string sourceFilePath = null)
{
    _module = new IRModule(moduleName);
    _sourceFilePath = sourceFilePath;
    _currentFunction = null;
    _currentBlock = null;

    program.Accept(this);

    return _module;
}
```

Add field at the top of the class:
```csharp
private string _sourceFilePath;
```

Then in every place that sets `_currentFunction.ModuleName` (lines ~227, ~306, ~741, ~945, etc.), add immediately after:
```csharp
_currentFunction.SourceFilePath = _sourceFilePath;
```

- [ ] **Step 3: Pass `unit.FilePath` from Compiler to IRBuilder**

In `BasicLang/Compiler.cs` line ~401, change:
```csharp
unit.IR = irBuilder.Build(unit.AST, unit.ModuleName);
```
to:
```csharp
unit.IR = irBuilder.Build(unit.AST, unit.ModuleName, unit.FilePath);
```

- [ ] **Step 4: Build and verify no regressions**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Expected: Build succeeded, 0 errors

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q`
Expected: All 1651 tests pass

- [ ] **Step 5: Commit**

```bash
git add BasicLang/IRNodes.cs BasicLang/IRBuilder.cs BasicLang/Compiler.cs
git commit -m "feat: add SourceFilePath to IRFunction for #line directive support"
```

---

## Task 2: Emit `#line` directives in C# backend

**Files:**
- Modify: `BasicLang/CSharpBackend.cs` (multiple methods)
- Create: `VisualGameStudio.Tests/Compiler/LineDirectiveTests.cs`

- [ ] **Step 1: Write failing tests for `#line` directive emission**

Create `VisualGameStudio.Tests/Compiler/LineDirectiveTests.cs`:

```csharp
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.CSharp;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class LineDirectiveTests
{
    private string CompileToCS(string basSource, string fileName = "Test.bas")
    {
        var lexer = new Lexer(basSource);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(ast);
        var irBuilder = new IRBuilder(analyzer);
        var module = irBuilder.Build(ast, "Test", fileName);
        var generator = new ImprovedCSharpCodeGenerator();
        return generator.Generate(module);
    }

    [Test]
    public void Generate_SimpleSubMain_ContainsLineDirective()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 42
        PrintLine(x)
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        Assert.That(result, Does.Contain("#line"));
        Assert.That(result, Does.Contain("Test.bas"));
    }

    [Test]
    public void Generate_UsingStatements_MarkedAsHidden()
    {
        var source = @"Module Test
    Sub Main()
        PrintLine(""hello"")
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // Usings should be #line hidden
        Assert.That(result, Does.Contain("#line hidden"));
    }

    [Test]
    public void Generate_MultipleLines_CorrectLineNumbers()
    {
        var source = @"Module Test
    Sub Main()
        Dim x As Integer = 1
        Dim y As Integer = 2
        Dim z As Integer = x + y
    End Sub
End Module";

        var result = CompileToCS(source, @"C:\project\Test.bas");

        // Should contain line directives for executable lines
        Assert.That(result, Does.Contain("#line"));
        // Should reference the .bas file
        Assert.That(result, Does.Contain(@"Test.bas"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --filter "FullyQualifiedName~LineDirectiveTests" -v q`
Expected: FAIL — generated code does not contain `#line`

- [ ] **Step 3: Implement `#line` emission in CSharpBackend**

In `BasicLang/CSharpBackend.cs`, add a tracking field:
```csharp
private int _lastEmittedSourceLine = -1;
private string _lastEmittedSourceFile = null;
```

Add a helper method:
```csharp
private void EmitLineDirective(int sourceLine, string sourceFile)
{
    if (sourceLine <= 0 || string.IsNullOrEmpty(sourceFile)) return;
    if (sourceLine == _lastEmittedSourceLine && sourceFile == _lastEmittedSourceFile) return;

    _lastEmittedSourceLine = sourceLine;
    _lastEmittedSourceFile = sourceFile;

    // #line directives use verbatim paths — no double-escaping needed
    // The C# compiler reads the path as-is from the #line directive
    _output.AppendLine($"#line {sourceLine} \"{sourceFile}\"");
}

private void EmitLineHidden()
{
    _lastEmittedSourceLine = -1;
    _lastEmittedSourceFile = null;
    _output.AppendLine("#line hidden");
}
```

In the `Generate(IRModule module)` method:
- Before emitting `using` statements: call `EmitLineHidden()`
- Before emitting namespace/class declarations: call `EmitLineHidden()`
- In `ExecuteBlock` / `Visit(IRInstruction)` methods: before each instruction that has `SourceLine > 0`, call `EmitLineDirective(instruction.SourceLine, _currentFunction?.SourceFilePath)`

The key locations to add `EmitLineDirective` calls:
1. In the block execution loop where `instruction.SourceLine > 0` is checked
2. Before each `WriteLine()` call that emits executable C# code
3. Reset `_lastEmittedSourceLine` when entering a new function

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --filter "FullyQualifiedName~LineDirectiveTests" -v q`
Expected: PASS

- [ ] **Step 5: Run full test suite for regressions**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q`
Expected: All tests pass (some existing tests may need adjustment if they compare exact C# output)

- [ ] **Step 6: Commit**

```bash
git add BasicLang/CSharpBackend.cs VisualGameStudio.Tests/Compiler/LineDirectiveTests.cs
git commit -m "feat: emit #line directives in C# backend for source-level debugging"
```

---

## Task 3: COM interface declarations (CorDebugWrappers.cs)

**Files:**
- Create: `BasicLang/Debugger/CorDebugWrappers.cs`

- [ ] **Step 1: Create COM interface declarations**

Create `BasicLang/Debugger/CorDebugWrappers.cs` with all required ICorDebug COM interfaces. Each interface needs `[ComImport]`, `[Guid("...")]`, `[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]` and methods in exact vtable order.

Required interfaces (GUIDs from .NET runtime source `src/coreclr/inc/cordebug.idl`):

**Core:**
- `ICorDebug` — `{3D6F5F61-7538-11D3-8D5B-00104B35E7EF}`
- `ICorDebugProcess` — `{3D6F5F62-7538-11D3-8D5B-00104B35E7EF}`
- `ICorDebugAppDomain` — `{3D6F5F63-7538-11D3-8D5B-00104B35E7EF}`
- `ICorDebugThread` — `{938C6D66-7FB6-4F69-B389-425B8987329B}`
- `ICorDebugModule` — `{DBA2D8C1-E5C5-4069-8C13-10A7C6ABF43D}`
- `ICorDebugAssembly` — `{DF59507C-D47A-459E-BCE2-6427EAC8FD06}`
- `ICorDebugFunction` — `{CC7BCAF6-8A68-11D2-983C-0000F808342D}`
- `ICorDebugCode` — `{CC7BCAF4-8A68-11D2-983C-0000F808342D}`

**Frames:**
- `ICorDebugFrame` — `{CC7BCAEF-8A68-11D2-983C-0000F808342D}`
- `ICorDebugILFrame` — `{03E26311-4F76-11D3-88C6-006097945418}`

**Values:**
- `ICorDebugValue` — `{CC7BCAEC-8A68-11D2-983C-0000F808342D}`
- `ICorDebugGenericValue` — `{CC7BCAF8-8A68-11D2-983C-0000F808342D}`
- `ICorDebugStringValue` — `{CC7BCAFD-8A68-11D2-983C-0000F808342D}`
- `ICorDebugObjectValue` — `{18AD3D6E-B7D2-11D2-BD04-0000F80849BD}`
- `ICorDebugArrayValue` — `{0405B0DF-A660-11D2-BD02-0000F80849BD}`
- `ICorDebugReferenceValue` — `{CC7BCAF9-8A68-11D2-983C-0000F808342D}`
- `ICorDebugBoxedValue` — `{CC7BCAFC-8A68-11D2-983C-0000F808342D}`

**Control:**
- `ICorDebugStepper` — `{CC7BCAE7-8A68-11D2-983C-0000F808342D}`
- `ICorDebugBreakpoint` — `{CC7BCAE8-8A68-11D2-983C-0000F808342D}`
- `ICorDebugFunctionBreakpoint` — `{CC7BCAE9-8A68-11D2-983C-0000F808342D}`

**V2 interfaces (needed for JMC, advanced stepping):**
- `ICorDebugProcess2`, `ICorDebugThread2`, `ICorDebugILFrame2`
- `ICorDebugFunction2`, `ICorDebugModule2`
- `ICorDebugValue2`, `ICorDebugStepper2`

**Callbacks:**
- `ICorDebugManagedCallback` — `{3D6F5F60-7538-11D3-8D5B-00104B35E7EF}` (14 methods — ALL must be implemented, unused ones call Continue)
- `ICorDebugManagedCallback2` — `{250E5EEA-DB5C-4C76-B6F3-8C46F12E3203}` (7 methods — ALL must be implemented, unused ones call Continue)

**Enums and structs:**
- `COR_DEBUG_STEP_RANGE` struct
- `CorDebugStepReason` enum
- `CorDebugExceptionCallbackType` enum

Reference the GUIDs and method signatures from: https://github.com/dotnet/runtime/blob/main/src/coreclr/inc/cordebug.idl

- [ ] **Step 2: Build to verify COM declarations compile**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add BasicLang/Debugger/CorDebugWrappers.cs
git commit -m "feat: add ICorDebug COM interface declarations for .NET debugging"
```

---

## Task 4: dbgshim P/Invoke declarations (DbgShim.cs)

**Files:**
- Create: `BasicLang/Debugger/DbgShim.cs`

- [ ] **Step 1: Create dbgshim P/Invoke wrapper**

Create `BasicLang/Debugger/DbgShim.cs`:

```csharp
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BasicLang.Debugger
{
    /// <summary>
    /// P/Invoke declarations for dbgshim.dll (.NET Core debugging bootstrap)
    /// </summary>
    internal static class DbgShim
    {
        private static string _dbgshimPath;

        // Callback delegate for RegisterForRuntimeStartup
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate void RuntimeStartupCallback(
            [MarshalAs(UnmanagedType.Interface)] object pCordb,
            IntPtr parameter,
            int hresult);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int RegisterForRuntimeStartup(
            int processId,
            RuntimeStartupCallback callback,
            IntPtr parameter,
            out IntPtr unregisterToken);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int UnregisterForRuntimeStartup(IntPtr token);

        [DllImport("dbgshim.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
        public static extern int CreateDebuggingInterfaceFromVersion3(
            int iDebuggerVersion,
            string szDebuggeeVersion,
            string szApplicationGroupId,
            [MarshalAs(UnmanagedType.Interface)] out object ppCordb);

        /// <summary>
        /// Find and load dbgshim.dll. Returns true if found.
        /// </summary>
        public static bool TryLoad()
        {
            if (_dbgshimPath != null)
                return File.Exists(_dbgshimPath);

            var searchPaths = new[]
            {
                // Next to target exe (self-contained)
                Path.Combine(AppContext.BaseDirectory, "dbgshim.dll"),
                // DOTNET_ROOT
                Environment.GetEnvironmentVariable("DOTNET_ROOT") is string root
                    ? Path.Combine(root, "shared", "Microsoft.NETCore.App") : null,
                // Standard install
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "dotnet", "shared", "Microsoft.NETCore.App"),
            };

            foreach (var basePath in searchPaths)
            {
                if (string.IsNullOrEmpty(basePath)) continue;

                // If it's a directory with version subdirs, find latest
                if (Directory.Exists(basePath))
                {
                    var versionDirs = Directory.GetDirectories(basePath);
                    Array.Sort(versionDirs);
                    for (int i = versionDirs.Length - 1; i >= 0; i--)
                    {
                        var candidate = Path.Combine(versionDirs[i], "dbgshim.dll");
                        if (File.Exists(candidate))
                        {
                            _dbgshimPath = candidate;
                            NativeLibrary.Load(_dbgshimPath);
                            return true;
                        }
                    }
                }

                // Direct path check
                if (File.Exists(basePath))
                {
                    _dbgshimPath = basePath;
                    NativeLibrary.Load(_dbgshimPath);
                    return true;
                }
            }

            return false;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BasicLang/Debugger/DbgShim.cs
git commit -m "feat: add dbgshim.dll P/Invoke declarations for .NET Core debug bootstrap"
```

---

## Task 5: PDB Source Mapper (SourceMapper.cs)

**Files:**
- Create: `BasicLang/Debugger/SourceMapper.cs`
- Create: `VisualGameStudio.Tests/Debugger/SourceMapperTests.cs`

- [ ] **Step 1: Add System.Reflection.Metadata NuGet reference**

Check if already referenced in BasicLang.csproj. If not, add:
```xml
<PackageReference Include="System.Reflection.Metadata" Version="8.0.0" />
```

- [ ] **Step 2: Write failing tests for SourceMapper**

Create `VisualGameStudio.Tests/Debugger/SourceMapperTests.cs`:

```csharp
using NUnit.Framework;
using BasicLang.Debugger;

namespace VisualGameStudio.Tests.Debugger;

[TestFixture]
public class SourceMapperTests
{
    [Test]
    public void LoadPdb_NonExistentPath_ReturnsFalse()
    {
        var mapper = new SourceMapper();
        var result = mapper.LoadPdb("nonexistent.pdb");
        Assert.That(result, Is.False);
    }

    [Test]
    public void FindNearestExecutableLine_NoDataLoaded_ReturnsInputLine()
    {
        var mapper = new SourceMapper();
        var result = mapper.FindNearestExecutableLine("test.bas", 5);
        Assert.That(result, Is.EqualTo(5));
    }

    [Test]
    public void GetSourceDocuments_NoDataLoaded_ReturnsEmpty()
    {
        var mapper = new SourceMapper();
        var docs = mapper.GetSourceDocuments();
        Assert.That(docs, Is.Empty);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --filter "FullyQualifiedName~SourceMapperTests" -v q`
Expected: FAIL — SourceMapper class does not exist

- [ ] **Step 4: Implement SourceMapper**

Create `BasicLang/Debugger/SourceMapper.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace BasicLang.Debugger
{
    /// <summary>
    /// Reads portable PDB files to map between .bas source lines and IL offsets
    /// </summary>
    public class SourceMapper : IDisposable
    {
        private MetadataReaderProvider _pdbProvider;
        private MetadataReader _pdbReader;
        private readonly Dictionary<string, List<SequencePointEntry>> _sequencePointsByFile = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<SequencePointEntry>> _sequencePointsByMethod = new();

        public bool IsLoaded => _pdbReader != null;

        public bool LoadPdb(string pdbPath)
        {
            try
            {
                if (!File.Exists(pdbPath)) return false;

                var stream = File.OpenRead(pdbPath);
                _pdbProvider = MetadataReaderProvider.FromPortablePdbStream(stream);
                _pdbReader = _pdbProvider.GetMetadataReader();

                IndexSequencePoints();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void IndexSequencePoints()
        {
            foreach (var mdHandle in _pdbReader.MethodDebugInformation)
            {
                var debugInfo = _pdbReader.GetMethodDebugInformation(mdHandle);
                var methodToken = MetadataTokens.GetRowNumber(mdHandle);

                foreach (var sp in debugInfo.GetSequencePoints())
                {
                    if (sp.IsHidden) continue;

                    var doc = _pdbReader.GetDocument(sp.Document);
                    var docName = _pdbReader.GetString(doc.Name);

                    var entry = new SequencePointEntry
                    {
                        FilePath = docName,
                        StartLine = sp.StartLine,
                        EndLine = sp.EndLine,
                        StartColumn = sp.StartColumn,
                        EndColumn = sp.EndColumn,
                        ILOffset = sp.Offset,
                        MethodToken = methodToken
                    };

                    if (!_sequencePointsByFile.ContainsKey(docName))
                        _sequencePointsByFile[docName] = new List<SequencePointEntry>();
                    _sequencePointsByFile[docName].Add(entry);

                    if (!_sequencePointsByMethod.ContainsKey(methodToken))
                        _sequencePointsByMethod[methodToken] = new List<SequencePointEntry>();
                    _sequencePointsByMethod[methodToken].Add(entry);
                }
            }
        }

        /// <summary>
        /// Get the IL offset for a breakpoint at a specific .bas file and line
        /// </summary>
        public (int methodToken, int ilOffset)? GetILOffsetForLine(string basFilePath, int line)
        {
            if (!_sequencePointsByFile.TryGetValue(basFilePath, out var sps))
                return null;

            var match = sps.Where(sp => sp.StartLine <= line && sp.EndLine >= line)
                .OrderBy(sp => sp.StartLine)
                .FirstOrDefault();

            if (match == null) return null;

            return (match.MethodToken, match.ILOffset);
        }

        /// <summary>
        /// Get the source location for a given method token and IL offset (for stack traces)
        /// </summary>
        public (string file, int line, int column)? GetSourceLocation(int methodToken, int ilOffset)
        {
            if (!_sequencePointsByMethod.TryGetValue(methodToken, out var sps))
                return null;

            // Find the sequence point at or before this IL offset
            var match = sps.Where(sp => sp.ILOffset <= ilOffset)
                .OrderByDescending(sp => sp.ILOffset)
                .FirstOrDefault();

            if (match == null) return null;

            return (match.FilePath, match.StartLine, match.StartColumn);
        }

        /// <summary>
        /// Find the nearest line that has executable code
        /// </summary>
        public int FindNearestExecutableLine(string basFilePath, int line)
        {
            if (!_sequencePointsByFile.TryGetValue(basFilePath, out var sps) || sps.Count == 0)
                return line;

            // Find nearest line at or after the requested line
            var nearest = sps.Where(sp => sp.StartLine >= line)
                .OrderBy(sp => sp.StartLine)
                .FirstOrDefault();

            return nearest?.StartLine ?? line;
        }

        /// <summary>
        /// Get all source documents referenced in the PDB
        /// </summary>
        public IReadOnlyList<string> GetSourceDocuments()
        {
            return _sequencePointsByFile.Keys.ToList();
        }

        /// <summary>
        /// Get IL ranges for a specific source line (for step range filtering)
        /// </summary>
        public (int startOffset, int endOffset)? GetILRangeForLine(int methodToken, int line)
        {
            if (!_sequencePointsByMethod.TryGetValue(methodToken, out var sps))
                return null;

            var forLine = sps.Where(sp => sp.StartLine == line).ToList();
            if (forLine.Count == 0) return null;

            var start = forLine.Min(sp => sp.ILOffset);
            // End is the start of the next sequence point
            var allSorted = sps.OrderBy(sp => sp.ILOffset).ToList();
            var lastIdx = allSorted.FindLastIndex(sp => sp.StartLine == line);
            var end = lastIdx + 1 < allSorted.Count ? allSorted[lastIdx + 1].ILOffset : start + 1;

            return (start, end);
        }

        public void Dispose()
        {
            _pdbProvider?.Dispose();
        }

        private class SequencePointEntry
        {
            public string FilePath;
            public int StartLine, EndLine, StartColumn, EndColumn;
            public int ILOffset;
            public int MethodToken;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --filter "FullyQualifiedName~SourceMapperTests" -v q`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add BasicLang/Debugger/SourceMapper.cs VisualGameStudio.Tests/Debugger/SourceMapperTests.cs
git commit -m "feat: add SourceMapper for PDB sequence point reading"
```

---

## Task 6: Breakpoint state machine (ClrBreakpointManager.cs)

**Files:**
- Create: `BasicLang/Debugger/ClrBreakpointManager.cs`
- Create: `VisualGameStudio.Tests/Debugger/ClrBreakpointManagerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `VisualGameStudio.Tests/Debugger/ClrBreakpointManagerTests.cs`:

```csharp
using NUnit.Framework;
using BasicLang.Debugger;

namespace VisualGameStudio.Tests.Debugger;

[TestFixture]
public class ClrBreakpointManagerTests
{
    [Test]
    public void AddBreakpoint_NewBreakpoint_StatusIsPending()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Pending));
    }

    [Test]
    public void MarkBound_PendingBreakpoint_StatusChangesToBound()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.MarkBound(id, actualLine: 5);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Bound));
    }

    [Test]
    public void MarkInvalid_NoExecutableCode_StatusIsInvalid()
    {
        var mgr = new ClrBreakpointManager();
        var id = mgr.AddPendingBreakpoint("Main.bas", 1);
        mgr.MarkInvalid(id);
        var bp = mgr.GetBreakpoint(id);
        Assert.That(bp.Status, Is.EqualTo(ClrBreakpointStatus.Invalid));
    }

    [Test]
    public void GetPendingForFile_ReturnsOnlyPending()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("Main.bas", 10);
        var id3 = mgr.AddPendingBreakpoint("Main.bas", 15);
        mgr.MarkBound(id3, 15);

        var pending = mgr.GetPendingForFile("Main.bas");
        Assert.That(pending.Count, Is.EqualTo(2));
    }

    [Test]
    public void ClearFile_RemovesAllForFile()
    {
        var mgr = new ClrBreakpointManager();
        mgr.AddPendingBreakpoint("Main.bas", 5);
        mgr.AddPendingBreakpoint("func.bas", 10);
        mgr.ClearFile("Main.bas");

        Assert.That(mgr.GetPendingForFile("Main.bas"), Is.Empty);
        Assert.That(mgr.GetPendingForFile("func.bas"), Has.Count.EqualTo(1));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --filter "FullyQualifiedName~ClrBreakpointManagerTests" -v q`
Expected: FAIL

- [ ] **Step 3: Implement ClrBreakpointManager**

Create `BasicLang/Debugger/ClrBreakpointManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Debugger
{
    public enum ClrBreakpointStatus { Pending, Bound, Verified, Invalid }

    public class ClrBreakpointEntry
    {
        public int Id;
        public string FilePath;
        public int RequestedLine;
        public int ActualLine;
        public ClrBreakpointStatus Status;
        public string Condition;
        public string HitCondition;
        public string LogMessage;
        public object ClrBreakpoint; // ICorDebugFunctionBreakpoint when bound
    }

    public class ClrBreakpointManager
    {
        private readonly Dictionary<int, ClrBreakpointEntry> _breakpoints = new();
        private int _nextId = 1;

        public int AddPendingBreakpoint(string filePath, int line,
            string condition = null, string hitCondition = null, string logMessage = null)
        {
            var id = _nextId++;
            _breakpoints[id] = new ClrBreakpointEntry
            {
                Id = id,
                FilePath = filePath,
                RequestedLine = line,
                ActualLine = line,
                Status = ClrBreakpointStatus.Pending,
                Condition = condition,
                HitCondition = hitCondition,
                LogMessage = logMessage
            };
            return id;
        }

        public ClrBreakpointEntry GetBreakpoint(int id) =>
            _breakpoints.TryGetValue(id, out var bp) ? bp : null;

        public void MarkBound(int id, int actualLine, object clrBreakpoint = null)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
            {
                bp.Status = ClrBreakpointStatus.Bound;
                bp.ActualLine = actualLine;
                bp.ClrBreakpoint = clrBreakpoint;
            }
        }

        public void MarkVerified(int id)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                bp.Status = ClrBreakpointStatus.Verified;
        }

        public void MarkInvalid(int id)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                bp.Status = ClrBreakpointStatus.Invalid;
        }

        public IReadOnlyList<ClrBreakpointEntry> GetPendingForFile(string filePath)
        {
            return _breakpoints.Values
                .Where(bp => bp.Status == ClrBreakpointStatus.Pending &&
                    string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public IReadOnlyList<ClrBreakpointEntry> GetAllPending()
        {
            return _breakpoints.Values
                .Where(bp => bp.Status == ClrBreakpointStatus.Pending)
                .ToList();
        }

        public void ClearFile(string filePath)
        {
            var toRemove = _breakpoints.Values
                .Where(bp => string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .Select(bp => bp.Id)
                .ToList();
            foreach (var id in toRemove)
                _breakpoints.Remove(id);
        }

        public void ClearAll() => _breakpoints.Clear();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj --filter "FullyQualifiedName~ClrBreakpointManagerTests" -v q`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add BasicLang/Debugger/ClrBreakpointManager.cs VisualGameStudio.Tests/Debugger/ClrBreakpointManagerTests.cs
git commit -m "feat: add ClrBreakpointManager with pending/bound/verified state machine"
```

---

## Task 7: Variable inspector (VariableInspector.cs)

**Files:**
- Create: `BasicLang/Debugger/VariableInspector.cs`

- [ ] **Step 1: Create VariableInspector**

Create `BasicLang/Debugger/VariableInspector.cs` with the ICorDebugValue → DAP variable conversion logic. This file reads CLR values through the COM interfaces declared in CorDebugWrappers.cs.

Key methods:
- `InspectValue(ICorDebugValue value, string name)` → returns DAP variable dict
- `GetLocals(ICorDebugILFrame frame, SourceMapper mapper, int methodToken)` → list of local variables
- `GetArguments(ICorDebugILFrame frame)` → list of argument variables
- `ClearReferences()` — invalidate cached variable references on continue/step

The `Dictionary<int, object>` maps DAP `variablesReference` IDs to `ICorDebugValue` objects for expandable nodes (objects, arrays).

- [ ] **Step 2: Build to verify**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BasicLang/Debugger/VariableInspector.cs
git commit -m "feat: add VariableInspector for ICorDebugValue to DAP variable conversion"
```

---

## Task 8: Process launcher and ICorDebug bootstrap (NetDebugProcess.cs)

**Files:**
- Create: `BasicLang/Debugger/NetDebugProcess.cs`

- [ ] **Step 1: Create NetDebugProcess**

Create `BasicLang/Debugger/NetDebugProcess.cs` implementing:

1. `LaunchAsync(string exePath, string workingDir, string[] args)` — starts the process via `Process.Start()`, calls `DbgShim.RegisterForRuntimeStartup()`, waits for CLR callback
2. `OnRuntimeStartup(ICorDebug corDebug)` — callback from dbgshim: Initialize, SetManagedHandler, DebugActiveProcess
3. `ManagedCallbackHandler` — nested class implementing `ICorDebugManagedCallback` + `ICorDebugManagedCallback2` with all required methods (each unused method calls `Continue(false)`)
4. Events: `BreakpointHit`, `StepCompleted`, `ExceptionThrown`, `ModuleLoaded`, `ProcessExited`
5. Methods: `Continue()`, `Stop()`, `CreateStepper()`, `Detach()`

The callback handler must call `ICorDebugProcess.Stop(0)` before raising `BreakpointHit` / `StepCompleted` events to freeze all threads.

- [ ] **Step 2: Build to verify**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BasicLang/Debugger/NetDebugProcess.cs
git commit -m "feat: add NetDebugProcess for .NET Core process launch and ICorDebug attach"
```

---

## Task 9: DAP server (NetDebugAdapter.cs)

**Files:**
- Create: `BasicLang/Debugger/NetDebugAdapter.cs`

- [ ] **Step 1: Create NetDebugAdapter**

Create `BasicLang/Debugger/NetDebugAdapter.cs` — the main DAP server that wires all components together.

Structure mirrors existing `DebugSession.cs` (same DAP message parsing, same stdin/stdout protocol) but routes commands to the .NET debug components:

```
NetDebugAdapter
  ├── _process: NetDebugProcess        (launch, attach, continue, step)
  ├── _sourceMapper: SourceMapper      (PDB reading)
  ├── _breakpointMgr: ClrBreakpointManager  (breakpoint state)
  ├── _variableInspector: VariableInspector  (value reading)
  │
  ├── RunAsync()                       (main message loop — reuse from DebugSession)
  ├── HandleInitialize()               (report capabilities)
  ├── HandleLaunch()                   (launch .exe, attach debugger, load PDB)
  ├── HandleSetBreakpoints()           (store pending, bind if module loaded)
  ├── HandleConfigurationDone()        (resume process)
  ├── HandleStackTrace()               (walk CLR frames → .bas lines via PDB)
  ├── HandleScopes()                   (locals, arguments, globals)
  ├── HandleVariables()                (read ICorDebugValue via inspector)
  ├── HandleContinue/Next/StepIn/Out() (ICorDebugProcess/Stepper)
  ├── HandleEvaluate()                 (variable name lookup)
  ├── HandleThreads()                  (enumerate managed threads)
  ├── HandlePause()                    (ICorDebugProcess.Stop(0))
  ├── HandleSetExceptionBreakpoints()  (first-chance / unhandled filters)
  └── HandleDisconnect()               (detach, terminate)
```

Wire `NetDebugProcess` events to DAP events:
- `_process.BreakpointHit` → send DAP `stopped` reason=breakpoint
- `_process.StepCompleted` → send DAP `stopped` reason=step
- `_process.ExceptionThrown` → send DAP `stopped` reason=exception
- `_process.ModuleLoaded` → load PDB, bind pending breakpoints
- `_process.ProcessExited` → send DAP `exited` + `terminated`

The `HandleLaunch` method:
1. Receive `.exe` path from `program` argument
2. Find matching `.pdb` file (same name, same directory)
3. Load PDB into SourceMapper
4. Launch process via NetDebugProcess
5. Return success response

- [ ] **Step 2: Build to verify**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add BasicLang/Debugger/NetDebugAdapter.cs
git commit -m "feat: add NetDebugAdapter DAP server with full .NET debugging support"
```

---

## Task 10: Wire up entry point and IDE integration

**Files:**
- Modify: `BasicLang/Program.cs:40-44`
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs:1267-1342`

- [ ] **Step 1: Route `--debug-adapter` to NetDebugAdapter**

In `BasicLang/Program.cs`, change the `--debug-adapter` handler (line ~40):

```csharp
if (args.Contains("--debug-adapter") || args.Contains("--dap"))
{
    // Use .NET debug adapter for compiled .exe debugging
    // Falls back to interpreter-based DebugSession if --dap-legacy is specified
    if (args.Contains("--dap-legacy"))
    {
        var debugSession = new DebugSession(Console.OpenStandardInput(), Console.OpenStandardOutput());
        await debugSession.RunAsync();
    }
    else
    {
        var netAdapter = new NetDebugAdapter(Console.OpenStandardInput(), Console.OpenStandardOutput());
        await netAdapter.RunAsync();
    }
    return;
}
```

- [ ] **Step 2: Update MainWindowViewModel to pass .exe path**

In `MainWindowViewModel.cs`, update `StartDebuggingAsync()`. The game-framework detection we added earlier should be removed. Instead, ALL projects use the .NET debug adapter with the compiled .exe:

Change the debug config to pass the `.exe` path:
```csharp
var config = new DebugConfiguration
{
    Program = buildResult.ExecutablePath,  // Pass compiled .exe, not .bas
    WorkingDirectory = _projectService.CurrentProject.ProjectDirectory
};
```

Remove the game-framework detection block that launches the process directly (we added this as a workaround — the proper .NET adapter replaces it).

- [ ] **Step 3: Build IDE and compiler**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release --nologo -v q`
Run: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release --nologo -v q`
Expected: Both build successfully

- [ ] **Step 4: Run full test suite**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --nologo -v q`
Expected: All tests pass

- [ ] **Step 5: Commit**

```bash
git add BasicLang/Program.cs VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs
git commit -m "feat: wire NetDebugAdapter as default debug adapter, pass compiled .exe to launch"
```

---

## Task 11: Copy IDE binaries and end-to-end test

- [ ] **Step 1: Copy updated binaries to IDE folder**

```bash
cp BasicLang/bin/Release/net8.0/BasicLang.dll IDE/BasicLang.dll
cp BasicLang/bin/Release/net8.0/BasicLang.exe IDE/BasicLang.exe
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.dll IDE/VisualGameStudio.dll
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.exe IDE/VisualGameStudio.exe
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.ProjectSystem.dll IDE/VisualGameStudio.ProjectSystem.dll
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.Core.dll IDE/VisualGameStudio.Core.dll
cp VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.Editor.dll IDE/VisualGameStudio.Editor.dll
```

- [ ] **Step 2: Manual end-to-end test**

1. Start IDE from `IDE/VisualGameStudio.exe`
2. Open a game project (e.g., game6)
3. Set a breakpoint on a line inside the game loop (click left margin)
4. Press F5 (Start Debugging)
5. Verify: game window opens, breakpoint hits, call stack shows `.bas` file names and correct line numbers
6. Verify: Variables panel shows local variable values
7. Press F10 (Step Over) — execution advances one `.bas` line
8. Press F5 (Continue) — game resumes
9. Close game window — debug session terminates cleanly

- [ ] **Step 3: Final commit**

```bash
git add IDE/BasicLang.dll IDE/BasicLang.exe IDE/VisualGameStudio.dll IDE/VisualGameStudio.exe IDE/VisualGameStudio.ProjectSystem.dll IDE/VisualGameStudio.Core.dll IDE/VisualGameStudio.Editor.dll
git commit -m "chore: update IDE binaries with .NET debug adapter"
```
