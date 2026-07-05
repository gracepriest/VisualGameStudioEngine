# C++ Backend Overhaul Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the C++ backend honest (hard errors for unsupported features) and then actually support generics, reference semantics, exceptions, lambdas, and async/yield — so BasicLang programs behave the same on the C++ backend as on the C# backend.

**Architecture:** A new `CppCapabilityChecker` pass runs at the top of `CppCodeGenerator.Generate()` and throws on unsupported IR constructs; each subsequent task implements one feature and deletes its diagnostic. Feature lowering follows the locked decisions in `docs/superpowers/specs/2026-07-05-cpp-backend-overhaul-decisions.md`: real C++ templates for generics, `std::shared_ptr` for class reference semantics, `try/catch(...)` + duplicated body for `Finally`, inline C++ lambdas mirroring the C# backend's `__lambda_N` inlining, a synchronous `Task<T>` struct for async and a C++20 coroutine `Generator<T>` for yield.

**Tech Stack:** C# (.NET, `BasicLang/` compiler project), NUnit (`VisualGameStudio.Tests`), generated C++ targets `-std=c++20`.

---

## Context for a zero-context engineer

- **Compile pipeline:** BasicLang source → `Lexer` → `Parser` → `SemanticAnalyzer` → `IRBuilder` → backend. Backends are `IIRVisitor` implementations; IR instructions dispatch via `inst.Accept(this)`.
- **The C++ backend** is `BasicLang/CppCodeGenerator.cs` (`class CppCodeGenerator : CodeGeneratorBase`, namespace `BasicLang.Compiler.CodeGen.CPlusPlus`). `CodeGeneratorBase` lives in `BasicLang/ICodeGenerator.cs:80` and declares one abstract `Visit(...)` per IR node type. Type mapping goes through `CppTypeMapper` in `BasicLang/TypeMapper.cs:201` (base `TypeMapperBase.MapType` at `TypeMapper.cs:36`).
- **IR nodes** are in `BasicLang/IRNodes.cs`. `TypeInfo` (in `BasicLang/SymbolTable.cs:19`) has `Name`, `Kind` (`TypeKind.Class/Structure/Interface/TypeParameter/...`), `ElementType`, and `GenericArguments` (populated for `Stack(Of Integer)`-style instantiations — see `SymbolTable.cs:527`).
- **Several call sites construct the C++ generator:** `Program.cs:1051`, `Program.cs:3552-3558`, `MultiTargetCompiler.cs:48-54`, `BackendRegistry.cs:36` and `:42`. We therefore put the capability check *inside* `CppCodeGenerator.Generate()`, not in any one driver.
- **Tests:** NUnit. Model helper: `CompileToCSharp` in `VisualGameStudio.Tests/Compiler/CompilationTests.cs:21`. No existing test touches the C++ backend, so new hard errors cannot regress the suite.
- **Commands:**
  - Build compiler: `dotnet build BasicLang/BasicLang.csproj -c Release`
  - Run one fixture: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppBackendTests"`
  - Full suite (gate before every commit): `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
- **Commit style:** one commit per green task, message `feat(cpp-backend): <what>`, body notes the task number. Follow @superpowers:test-driven-development and @superpowers:verification-before-completion.

## File structure (whole overhaul)

- Create: `BasicLang/CppCapabilityChecker.cs` — standalone IR-walk pass returning diagnostics (Task 1)
- Create: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs` — all new tests live here
- Modify: `BasicLang/CppCodeGenerator.cs` — every task
- Modify: `BasicLang/TypeMapper.cs` — Tasks 2, 3, 6 (`CppTypeMapper`)
- Modify: `BasicLang/IRNodes.cs`, `BasicLang/ICodeGenerator.cs`, `BasicLang/IRBuilder.cs`, `BasicLang/CSharpBackend.cs` — Task 4 (`IRThrow`)
- Modify: `docs/`, `CLAUDE.md`, `IDE/` binaries — Task 7

---

### Task 1: Capability diagnostics — make every gap a compile error

**Files:**
- Create: `BasicLang/CppCapabilityChecker.cs`
- Modify: `BasicLang/CppCodeGenerator.cs` (top of `Generate`, line ~39)
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs` (new)

- [ ] **Step 1.1: Create the test file with the CompileToCpp helper and failing diagnostics tests**

Create `VisualGameStudio.Tests/Compiler/CppBackendTests.cs`. The helper mirrors `CompileToCSharp` (`CompilationTests.cs:21`) but ends with `new CppCodeGenerator(...)`:

```csharp
using NUnit.Framework;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// C++ backend tests: BasicLang source -> ... -> IRBuilder -> CppCodeGenerator -> C++ output.
/// </summary>
[TestFixture]
public class CppBackendTests
{
    /// <summary>Compile BasicLang source to C++ output. Throws are allowed to propagate
    /// (capability diagnostics are exceptions); pipeline-stage errors go to the list.</summary>
    private string CompileToCpp(string source, out List<string> errors)
    {
        errors = new List<string>();
        var lexer = new Lexer(source);
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        ProgramNode ast;
        try { ast = parser.Parse(); }
        catch (Exception ex) { errors.Add($"Parse error: {ex.Message}"); return null; }

        var analyzer = new SemanticAnalyzer();
        if (!analyzer.Analyze(ast))
        {
            foreach (var err in analyzer.Errors) errors.Add($"Semantic error: {err.Message}");
            return null;
        }

        var irBuilder = new IRBuilder(analyzer);
        var irModule = irBuilder.Build(ast, "TestModule");

        var gen = new CppCodeGenerator(new CppCodeGenOptions { GenerateComments = false });
        return gen.Generate(irModule);
    }

    // ---- Task 1: capability diagnostics ----

    [Test]
    public void Cpp_AsyncFunction_ThrowsCapabilityError()
    {
        var source = @"
Async Function GetValue() As Integer
    Return 42
End Function";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
        Assert.That(ex.Message, Does.Contain("Async"));
    }

    [Test]
    public void Cpp_IteratorYield_ThrowsCapabilityError()
    {
        var source = @"
Iterator Function Numbers() As IEnumerable(Of Integer)
    Yield 1
End Function";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
        Assert.That(ex.Message, Does.Contain("Yield").Or.Contain("Iterator"));
    }

    [Test]
    public void Cpp_TryFinally_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Try
        Dim x As Integer = 1
    Finally
        Dim y As Integer = 2
    End Try
End Sub";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
        Assert.That(ex.Message, Does.Contain("Finally"));
    }

    [Test]
    public void Cpp_Lambda_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Dim f = Function(x As Integer) x * 2
End Sub";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
        Assert.That(ex.Message, Does.Contain("Lambda").IgnoreCase);
    }

    [Test]
    public void Cpp_GenericClass_ThrowsCapabilityError()
    {
        var source = @"
Class Stack(Of T)
    Private _top As T
End Class";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
        Assert.That(ex.Message, Does.Contain("generic").IgnoreCase);
    }

    [Test]
    public void Cpp_UnmappedNetType_ThrowsCapabilityError()
    {
        var source = @"
Sub Main()
    Dim items As List(Of Integer)
End Sub";
        var ex = Assert.Throws<CppCapabilityException>(() => CompileToCpp(source, out _));
        Assert.That(ex.Message, Does.Contain("List"));
    }

    [Test]
    public void Cpp_PlainProceduralCode_StillCompiles()
    {
        var source = @"
Function Add(a As Integer, b As Integer) As Integer
    Return a + b
End Function";
        var output = CompileToCpp(source, out var errors);
        Assert.That(errors, Is.Empty, string.Join("; ", errors));
        Assert.That(output, Does.Contain("int32_t Add(int32_t a, int32_t b)"));
    }
}
```

Adjust the diagnostics-message assertions to the exact wording you implement — but keep them asserting on the *feature name*, so messages stay actionable.

- [ ] **Step 1.2: Run the fixture to verify the new tests fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppBackendTests"`
Expected: FAIL — `CppCapabilityException` does not exist yet (compile error). Comment nothing out; the compile error *is* the failing state for 1.1. If the BasicLang syntax in any test fails to parse (e.g. `Iterator Function`), check how `AsyncFunctionTests.cs` / existing tests spell it and adjust the source snippets, not the goal.

- [ ] **Step 1.3: Implement `CppCapabilityChecker`**

Create `BasicLang/CppCapabilityChecker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>Thrown when an IRModule uses features the C++ backend cannot lower.</summary>
    public class CppCapabilityException : Exception
    {
        public IReadOnlyList<string> Diagnostics { get; }
        public CppCapabilityException(List<string> diagnostics)
            : base("C++ backend: unsupported feature(s):\n  " + string.Join("\n  ", diagnostics))
        {
            Diagnostics = diagnostics;
        }
    }

    /// <summary>
    /// Walks an IRModule and reports constructs the C++ backend cannot emit correct code for.
    /// Each check is deleted when the corresponding feature lands (see plan tasks 2-6).
    /// The unmapped-type and Object checks are permanent.
    /// </summary>
    public class CppCapabilityChecker
    {
        // Value types CppTypeMapper can map (keep in sync with TypeMapper.cs InitializeTypeMappings)
        private static readonly HashSet<string> MappedTypeNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Integer", "Long", "Single", "Double", "String", "Boolean", "Char", "Void",
            "Byte", "Short", "UByte", "UShort", "UInteger", "ULong"
        };

        public List<string> Check(IRModule module)
        {
            var diags = new List<string>();

            foreach (var func in module.Functions)
            {
                if (func.IsAsync)
                    diags.Add($"Async function '{func.Name}' — Async/Await is not yet supported by the C++ backend");
                if (func.IsIterator)
                    diags.Add($"Iterator function '{func.Name}' — Yield is not yet supported by the C++ backend");
                if (func.IsLambda)
                    diags.Add($"Lambda '{func.Name}' — lambdas are not yet supported by the C++ backend");
                if (func.GenericParameters is { Count: > 0 })
                    diags.Add($"Function '{func.Name}' — generic functions are not yet supported by the C++ backend");

                CheckType(func.ReturnType, $"return type of '{func.Name}'", module, diags);
                foreach (var p in func.Parameters)
                    CheckType(p.Type, $"parameter '{p.Name}' of '{func.Name}'", module, diags);
                foreach (var lv in func.LocalVariables)
                    CheckType(lv.Type, $"local '{lv.Name}' in '{func.Name}'", module, diags);

                foreach (var block in func.Blocks)
                    foreach (var inst in block.Instructions)
                        CheckInstruction(inst, func.Name, module, diags);
            }

            foreach (var irClass in module.Classes.Values)
            {
                if (irClass.GenericParameters is { Count: > 0 })
                    diags.Add($"Class '{irClass.Name}' — generic classes are not yet supported by the C++ backend");
            }

            return diags.Distinct().ToList();
        }

        private void CheckInstruction(IRInstruction inst, string funcName, IRModule module, List<string> diags)
        {
            switch (inst)
            {
                case IRTryCatch tc:
                    if (tc.FinallyBlock != null)
                        diags.Add($"Try/Finally in '{funcName}' — Finally blocks are not yet supported by the C++ backend");
                    foreach (var i in tc.TryBlock.Instructions) CheckInstruction(i, funcName, module, diags);
                    foreach (var cc in tc.CatchClauses)
                        foreach (var i in cc.Block.Instructions) CheckInstruction(i, funcName, module, diags);
                    break;
                case IRAwait:
                    diags.Add($"Await in '{funcName}' — Async/Await is not yet supported by the C++ backend");
                    break;
                case IRYield:
                    diags.Add($"Yield in '{funcName}' — Yield is not yet supported by the C++ backend");
                    break;
            }
        }

        /// <summary>PERMANENT check: class-kind types must be user-defined in this module or
        /// have a known C++ mapping; everything else (List, Dictionary, DateTime, Object...)
        /// is an error until a .NET-surface design exists.</summary>
        private void CheckType(TypeInfo type, string where, IRModule module, List<string> diags)
        {
            if (type == null) return;
            if (type.Kind == TypeKind.Array) { CheckType(type.ElementType, where, module, diags); return; }
            if (type.Kind == TypeKind.TypeParameter) return; // handled by the generics diagnostics above
            foreach (var ga in type.GenericArguments) CheckType(ga, where, module, diags);

            var name = type.Name;
            if (string.IsNullOrEmpty(name) || MappedTypeNames.Contains(name)) return;
            if (name.Equals("Object", StringComparison.OrdinalIgnoreCase))
            {
                diags.Add($"'Object' ({where}) — 'Object' has no C++ mapping");
                return;
            }
            if (type.Kind is TypeKind.Class or TypeKind.Interface or TypeKind.Structure)
            {
                bool userDefined = module.Classes.ContainsKey(name)
                    || module.Interfaces.ContainsKey(name)
                    || module.Enums.ContainsKey(name)
                    || module.Delegates.ContainsKey(name);
                if (!userDefined)
                    diags.Add($".NET type '{name}' ({where}) — no C++ mapping exists for this type");
            }
        }
    }
}
```

**Adaptation notes for the implementer (verify, don't assume):**
- `IRModule` member names (`Classes`, `Interfaces`, `Enums`, `Delegates`, `Functions`) — confirm in `IRNodes.cs` (they are `Dictionary<string, ...>` keyed by name; adjust `ContainsKey` calls if the shapes differ).
- `IRFunction.IsAsync/IsIterator/IsLambda/GenericParameters` are at `IRNodes.cs:998-1004`.
- If `TypeKind` has no `Array` member, check how `TypeMapperBase.MapType` (TypeMapper.cs:41) detects arrays and mirror it.

- [ ] **Step 1.4: Wire the checker into `CppCodeGenerator.Generate`**

In `BasicLang/CppCodeGenerator.cs`, at the very top of `Generate(IRModule module)` (line ~41, before `_module = module;`):

```csharp
var capabilityDiags = new CppCapabilityChecker().Check(module);
if (capabilityDiags.Count > 0)
    throw new CppCapabilityException(capabilityDiags);
```

- [ ] **Step 1.5: Run fixture, then full suite**

Run: `dotnet test ... --filter "FullyQualifiedName~CppBackendTests"` → all Task-1 tests PASS.
Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release` → full suite green (baseline 1,918 + new).

- [ ] **Step 1.6: Commit**

```bash
git add BasicLang/CppCapabilityChecker.cs BasicLang/CppCodeGenerator.cs VisualGameStudio.Tests/Compiler/CppBackendTests.cs docs/superpowers/specs/2026-07-05-cpp-backend-overhaul-decisions.md docs/superpowers/plans/2026-07-05-cpp-backend-overhaul.md
git commit -m "feat(cpp-backend): capability diagnostics - hard errors for unsupported features (task 1)"
```

---

### Task 2: Generics → real C++ templates

**Files:**
- Modify: `BasicLang/CppCodeGenerator.cs` — `Generate` forward-decl loop (~53-58), `GenerateClass` (348), `GenerateConstructor` param rule (478), `GenerateMethod` (692), `GenerateFunctionDeclaration` (813), `GenerateFunction` (824), `Visit(IRNewObject)` (1551)
- Modify: `BasicLang/TypeMapper.cs` — `CppTypeMapper.MapType` override + `GetDefaultValue`
- Modify: `BasicLang/CppCapabilityChecker.cs` — delete the two generics diagnostics
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs`

- [ ] **Step 2.1: Write failing tests**

```csharp
[Test]
public void Cpp_TemplateFunction_EmitsCppTemplate()
{
    var source = @"
Template Function Max(Of T)(a As T, b As T) As T
    If a > b Then
        Return a
    End If
    Return b
End Function";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("template <typename T>"));
    Assert.That(output, Does.Match(@"T\s+Max\(const T& a, const T& b\)"));
}

[Test]
public void Cpp_GenericClass_EmitsCppTemplate()
{
    var source = @"
Class Pair(Of T)
    Public First As T
    Public Second As T
End Class";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("template <typename T>"));
    Assert.That(output, Does.Contain("class Pair"));
    Assert.That(output, Does.Contain("T First;"));
}

[Test]
public void Cpp_GenericInstantiation_EmitsTemplateArguments()
{
    var source = @"
Class Pair(Of T)
    Public First As T
End Class

Sub Main()
    Dim p As New Pair(Of Integer)()
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("Pair<int32_t>"));
}
```

Replace the Task-1 test `Cpp_GenericClass_ThrowsCapabilityError` with `Cpp_GenericClass_EmitsCppTemplate` (the diagnostic is being removed by design — update, don't accumulate contradictions).

- [ ] **Step 2.2: Run fixture — new tests FAIL** (generics diagnostic throws)

- [ ] **Step 2.3: Implement**

1. **Delete** the two `GenericParameters` diagnostics from `CppCapabilityChecker.Check` (function + class loops).
2. `CppCodeGenerator` — add helper:
```csharp
private string TemplatePrefix(List<string> genericParams) =>
    (genericParams == null || genericParams.Count == 0)
        ? null
        : "template <" + string.Join(", ", genericParams.Select(p => $"typename {SanitizeName(p)}")) + ">";
```
3. Emit the prefix (call `WriteLine(prefix)` when non-null) immediately before the signature line in: forward-decl loop (`template <typename T>` + `class Pair;`), `GenerateClass`, `GenerateFunctionDeclaration`, `GenerateFunction`, `GenerateMethod` (methods use `method.GenericParameters` — confirm the property on `IRMethod` at IRNodes.cs:~1375). If constraints exist (`GenericTypeParams` with constraints), emit a comment line `// constraints dropped: T As IComparable` (locked decision 2).
4. `CppTypeMapper.MapType` override (in `TypeMapper.cs`, inside `CppTypeMapper`):
```csharp
public override string MapType(TypeInfo type)
{
    if (type == null) return GetDefaultType();
    if (type.Kind == TypeKind.TypeParameter) return type.Name;   // T stays T
    if (type.GenericArguments != null && type.GenericArguments.Count > 0
        && type.Kind != TypeKind.Array)
    {
        var args = string.Join(", ", type.GenericArguments.Select(MapType));
        return $"{base.MapType(StripGenericArgs(type))}<{args}>";
    }
    return base.MapType(type);
}
```
   where `StripGenericArgs` maps the bare name (simplest: `_typeMap.TryGetValue(type.Name, out var m) ? m : type.Name` — do NOT recurse into base with the same TypeInfo or you loop). Note `CppCodeGenerator.MapTypeName(string)` (CppCodeGenerator.cs:266) duplicates primitive mapping — route any generic-name usage through the type mapper instead where you touch it.
5. `CppTypeMapper.GetDefaultValue`: if `type.Kind == TypeKind.TypeParameter` return `"{}"` (value-init — works for int, string, anything).
6. Const-ref parameter rule (`GenerateConstructor` line 478 and the same lambda in `GenerateMethod`/`GenerateFunction` if present): add `|| p.Type.Kind == TypeKind.TypeParameter` to the const-ref branch.
7. `Visit(IRNewObject)` (1551): when `newObj.Type?.GenericArguments?.Count > 0`, use `MapType(newObj.Type)` as the constructor name instead of `SanitizeName(newObj.ClassName)`:
```csharp
var ctorName = (newObj.Type?.GenericArguments is { Count: > 0 })
    ? MapType(newObj.Type)
    : SanitizeName(newObj.ClassName);
WriteLine($"{type} {newObj.Name} = {ctorName}({args});");
```
   If the instantiation test fails because `newObj.Type.GenericArguments` is empty, the semantic analyzer isn't populating the instantiated TypeInfo — debug via `_semanticAnalyzer.GetNodeType` at IRBuilder.cs:3281 before adding IR plumbing (see @superpowers:systematic-debugging).

- [ ] **Step 2.4: Run fixture → PASS; run full suite → green**

- [ ] **Step 2.5: Commit** — `feat(cpp-backend): emit real C++ templates for generics (task 2)`

---

### Task 3: Reference semantics — `std::shared_ptr` for classes

**Files:**
- Modify: `BasicLang/TypeMapper.cs` (`CppTypeMapper.MapType`)
- Modify: `BasicLang/CppCodeGenerator.cs` — `Visit(IRNewObject)` (1551), `Visit(IRInstanceMethodCall)` (1559), `Visit(IRFieldAccess)` (1604), `Visit(IRFieldStore)` (1612), const-ref rule (478), `Visit(IRCast)` (1495)
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs`

- [ ] **Step 3.1: Write failing tests**

```csharp
[Test]
public void Cpp_ClassInstance_UsesSharedPtr()
{
    var source = @"
Class Person
    Public Name As String
End Class

Sub Main()
    Dim p As New Person()
    p.Name = ""Alice""
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("std::shared_ptr<Person>"));
    Assert.That(output, Does.Contain("std::make_shared<Person>("));
    Assert.That(output, Does.Contain("->Name"));
}

[Test]
public void Cpp_ClassMethodCall_UsesArrow()
{
    var source = @"
Class Counter
    Private _n As Integer
    Public Sub Increment()
        _n = _n + 1
    End Sub
End Class

Sub Main()
    Dim c As New Counter()
    c.Increment()
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("c->Increment()"));
}

[Test]
public void Cpp_Structure_StaysValueType()
{
    var source = @"
Structure Point
    Public X As Integer
    Public Y As Integer
End Structure

Sub Main()
    Dim p As Point
    p.X = 1
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Not.Contain("shared_ptr<Point>"));
    Assert.That(output, Does.Contain("p.X"));
}
```

- [ ] **Step 3.2: Run fixture — FAIL**

- [ ] **Step 3.3: Implement**

1. `CppTypeMapper.MapType`: after primitive/`_typeMap` lookup misses, wrap class/interface kinds:
```csharp
if (type.Kind is TypeKind.Class or TypeKind.Interface)
    return $"std::shared_ptr<{bareName}>";   // bareName includes template args from Task 2
```
   `TypeKind.Structure` and `TypeKind.TypeParameter` stay bare. IMPORTANT: `String` maps via `_typeMap` *before* this branch — order matters. The class *definition* itself (`class Person { ... }`) uses `SanitizeName(irClass.Name)`, not MapType, so definitions are unaffected.
2. `Visit(IRNewObject)`: `WriteLine($"{type} {newObj.Name} = std::make_shared<{bareClassName}>({args});")` where `bareClassName` is the Task-2 `ctorName` (WITHOUT shared_ptr wrapper — add a `MapBareClassName(TypeInfo)` helper on the generator that returns the unwrapped mapped name; structures keep the old value-construction path).
3. Member access: add helper `private bool IsSharedPtr(IRValue obj)` → true when `obj.Type?.Kind is TypeKind.Class or TypeKind.Interface` and the mapped type isn't a `_typeMap` primitive, EXCEPT when the object is `this` (raw pointer inside methods — detect `obj is IRVariable v && v.Name == "this"`, which also uses `->`). Then in `Visit(IRInstanceMethodCall)`, `Visit(IRFieldAccess)`, `Visit(IRFieldStore)` choose `->` vs `.` accordingly (`this` → `->`; shared_ptr → `->`; structures/std::string → `.`).
4. Const-ref rule (478): `std::shared_ptr` params pass by value — change the `TypeKind.Class` condition so classes now hit the by-value branch (shared_ptr copy is the intended semantics), keep `std::string` and structures as `const&`.
5. `Visit(IRCast)` (1495): when both source and target are class-kind, emit `std::static_pointer_cast<Target>(value)` instead of `static_cast`.
6. `GetDefaultValue` for class-kind → `nullptr` (verify existing behavior; `Nothing` already lowers to a null constant — confirm `EmitConstant` emits `nullptr`).

- [ ] **Step 3.4: Run fixture → PASS; full suite → green.** Pay attention to Task-2 template tests — `Pair<int32_t>` instantiation now becomes `std::shared_ptr<Pair<int32_t>>`; update those assertions deliberately (they should assert `std::make_shared<Pair<int32_t>>`).

- [ ] **Step 3.5: Commit** — `feat(cpp-backend): shared_ptr reference semantics for classes (task 3)`

---

### Task 4: Throw + Finally + exception type mapping

**Files:**
- Modify: `BasicLang/IRNodes.cs` — new `IRThrow` node + `IIRVisitor` default method (find `interface IIRVisitor` via grep; implementors: `IRPrettyPrinter`, `ImprovedCSharpCodeGenerator`, `ICodeGenerator`)
- Modify: `BasicLang/ICodeGenerator.cs` — `CodeGeneratorBase` gets `public virtual void Visit(IRThrow throwInst) { }` (virtual no-op → LLVM/MSIL unaffected)
- Modify: `BasicLang/IRBuilder.cs:2677` — emit `IRThrow` instead of comment-only
- Modify: `BasicLang/CSharpBackend.cs` — `ImprovedCSharpCodeGenerator.Visit(IRThrow)` emits real `throw`
- Modify: `BasicLang/CppCodeGenerator.cs` — `Visit(IRThrow)`, `Visit(IRTryCatch)` (1628) finally lowering + exception-name mapping (1644)
- Modify: `BasicLang/CppCapabilityChecker.cs` — remove Finally diagnostic; add exception class names (`Exception`, `ArgumentException`, `InvalidOperationException`, `ApplicationException`, `SystemException`, `NotImplementedException`) to the known-types allowlist
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs` + one C# regression test in `CompilationTests.cs`

- [ ] **Step 4.1: Write failing tests**

```csharp
[Test]
public void Cpp_ThrowStatement_EmitsCppThrow()
{
    var source = @"
Sub Fail()
    Throw New Exception(""boom"")
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("throw std::runtime_error("));
    Assert.That(output, Does.Contain("boom"));
}

[Test]
public void Cpp_TryCatchTyped_MapsExceptionType()
{
    var source = @"
Sub Main()
    Try
        Dim x As Integer = 1
    Catch ex As Exception
        Dim y As Integer = 2
    End Try
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("catch (const std::exception& ex)"));
}

[Test]
public void Cpp_TryFinally_EmitsFinallyBodyTwice()
{
    var source = @"
Sub Main()
    Dim n As Integer = 0
    Try
        n = 1
    Finally
        n = 2
    End Try
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    // exceptional path: catch(...) { finally; throw; }
    Assert.That(output, Does.Contain("catch (...)"));
    Assert.That(output, Does.Contain("throw;"));
    // normal path: finally body after the try/catch
    Assert.That(CountOccurrences(output, "n = 2"), Is.EqualTo(2));
}
```
(add `private static int CountOccurrences(string haystack, string needle)` helper to the fixture)

And in `CompilationTests.cs` (C# backend regression — Throw is broken there today too):

```csharp
[Test]
public void Compile_ThrowStatement_EmitsThrow()
{
    var source = @"
Sub Fail()
    Throw New Exception(""boom"")
End Sub";
    var output = CompileToCSharp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("throw"));
}
```

- [ ] **Step 4.2: Run both fixtures — new tests FAIL** (no `throw` in output; Finally diagnostic throws)

- [ ] **Step 4.3: Implement**

1. `IRNodes.cs` — add after `IRTryCatch` (~line 900):
```csharp
/// <summary>Throw statement: throw Exception (null = rethrow).</summary>
public class IRThrow : IRInstruction
{
    public IRValue Exception { get; set; }
    public IRThrow(IRValue exception) { Exception = exception; }
    public override void Accept(IIRVisitor visitor) => visitor.Visit(this);
    public override string ToString() => Exception == null ? "rethrow" : $"throw {Exception.Name}";
}
```
2. `IIRVisitor` — add a **default interface method** so `IRPrettyPrinter` keeps compiling untouched:
```csharp
void Visit(IRThrow throwInst) { }
```
   (If the project targets < netcoreapp3.0 and default interface methods fail to compile, fall back to adding empty `Visit(IRThrow)` implementations to `IRPrettyPrinter` and any other implementor the compiler flags.)
3. `CodeGeneratorBase` (ICodeGenerator.cs, after line 158): `public virtual void Visit(IRThrow throwInst) { }` — LLVM/MSIL inherit the no-op.
4. `IRBuilder.Visit(ThrowStatementNode)` (2677): after `node.Exception.Accept(this)`, capture the produced value (the `_expressionResult` field — see how `Visit(ReturnStatementNode)` at IRBuilder.cs:2695 obtains the evaluated operand) and `EmitInstruction(new IRThrow(value));` for throw, `EmitInstruction(new IRThrow(null))` for bare rethrow.
5. `ImprovedCSharpCodeGenerator` — `public void Visit(IRThrow t)` emitting `throw <expr>;` / `throw;` (use the class's `EmitExpression`/`GetValueName` conventions).
6. `CppCodeGenerator.Visit(IRThrow)`:
```csharp
public override void Visit(IRThrow throwInst)
{
    if (throwInst.Exception == null) { WriteLine("throw;"); return; }
    // New Exception("msg") lowers to IRNewObject of an exception class: unwrap the message
    if (throwInst.Exception is IRNewObject ex && IsExceptionClassName(ex.ClassName))
    {
        var msg = ex.Arguments.Count > 0 ? GetValueName(ex.Arguments[0]) : "\"exception\"";
        WriteLine($"throw std::runtime_error({msg});");
        return;
    }
    WriteLine($"throw std::runtime_error({GetValueName(throwInst.Exception)});");
}
```
   `IsExceptionClassName`: static set of the .NET exception names listed above. NOTE: the `IRNewObject` for the exception may be emitted as its own instruction *before* `IRThrow` — if so, `Visit(IRNewObject)` will already have emitted a `std::make_shared<Exception>` line that the checker/type-mapper can't map. Handle by mapping exception class names in `CppTypeMapper` to `std::runtime_error` AND detecting the newobj-feeding-throw pattern (skip the standalone newobj emission when its only use is the throw — check `IRValue` use counts, or emit `std::runtime_error t0(msg);` for exception-class IRNewObject). Let the test drive which is needed.
7. `Visit(IRTryCatch)` (1628): map catch types via a name map (`Exception`/base → `std::exception`, specific .NET exceptions → `std::runtime_error`); then finally lowering:
```csharp
if (tryCatch.FinallyBlock != null)
{
    WriteLine("catch (...)");
    WriteLine("{"); Indent(); WriteLine("{"); Indent();
    EmitBlockInstructions(tryCatch.FinallyBlock);   // extract the existing inst-loop into a helper
    Unindent(); WriteLine("}");
    WriteLine("throw;");
    Unindent(); WriteLine("}");
    // normal path: braces give the duplicated body its own scope (avoids duplicate declarations)
    WriteLine("{"); Indent();
    EmitBlockInstructions(tryCatch.FinallyBlock);
    Unindent(); WriteLine("}");
}
```
8. Remove the Finally diagnostic from `CppCapabilityChecker`; add exception class names to its allowlist.
9. Document the limitation in the generated header comment and plan notes: `Return` inside `Try` bypasses the finally body (known, accepted — spec decision 5).

- [ ] **Step 4.4: Run fixtures → PASS; full suite → green** (watch LLVM/MSIL tests — must be untouched by the no-op).

- [ ] **Step 4.5: Commit** — `feat(compiler): IRThrow node; cpp-backend throw/finally/exception mapping (task 4)`

---

### Task 5: Lambdas → C++ lambdas

**Files:**
- Modify: `BasicLang/ICodeGenerator.cs` — make `GetValueName` `protected virtual` (line 176)
- Modify: `BasicLang/CppCodeGenerator.cs` — skip `IsLambda` at top level (mirror CSharpBackend.cs:1358), override `GetValueName` to inline `__lambda_N` refs (mirror CSharpBackend.cs:2659), new `GenerateLambdaExpression`
- Modify: `BasicLang/CppCapabilityChecker.cs` — remove lambda diagnostic
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs`

- [ ] **Step 5.1: Read the C# blueprint first**

Read `CSharpBackend.cs` `GenerateLambdaExpression` (search for it; the `__lambda_` reference hook is at 2659) to learn how the lambda `IRFunction`'s body is rendered as an expression. Lambdas exist in `module.Functions` named `__lambda_N` with `IsLambda == true`; use sites reference them as `IRVariable`s with that name.

- [ ] **Step 5.2: Write failing tests**

```csharp
[Test]
public void Cpp_LambdaAssignedToVariable_EmitsCppLambda()
{
    var source = @"
Sub Main()
    Dim f = Function(x As Integer) x * 2
    Dim result As Integer = f(5)
End Sub";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("[=](int32_t x)"));
    Assert.That(output, Does.Not.Contain("__lambda_"));  // no dangling references
    Assert.That(output, Does.Contain("f(5)"));
}
```
Replace `Cpp_Lambda_ThrowsCapabilityError` with this test.

- [ ] **Step 5.3: Run fixture — FAIL**

- [ ] **Step 5.4: Implement**

1. `CodeGeneratorBase.GetValueName` (ICodeGenerator.cs:176): change `protected string` → `protected virtual string`.
2. `CppCodeGenerator`: in the top-level function loop inside `Generate`, skip `IsLambda` functions (same guard as CSharpBackend.cs:1358) — both in the declarations pass and the definitions pass.
3. Override `GetValueName`:
```csharp
protected override string GetValueName(IRValue value)
{
    if (value is IRVariable v && v.Name != null && v.Name.StartsWith("__lambda_"))
    {
        var lambdaFunc = _module?.Functions.FirstOrDefault(f => f.Name == v.Name && f.IsLambda);
        if (lambdaFunc != null) return GenerateLambdaExpression(lambdaFunc);
    }
    return base.GetValueName(value);
}
```
4. `GenerateLambdaExpression(IRFunction lambda)` — renders `[=](params) -> ret { body }` **into a separate buffer**: the generator writes through `_output` (`private readonly StringBuilder`); remove `readonly`, then:
```csharp
private string GenerateLambdaExpression(IRFunction lambda)
{
    var saved = (_output, _currentFunction, _indentLevel);
    _output = new StringBuilder(); _indentLevel = 0;
    var savedNames = new Dictionary<IRValue, string>(_valueNames);

    var ps = string.Join(", ", lambda.Parameters.Select(p => $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
    var ret = MapType(lambda.ReturnType);
    _output.Append($"[=]({ps}) -> {ret} {{ ");
    _currentFunction = lambda;
    if (lambda.EntryBlock != null) GenerateBlock(lambda.EntryBlock, new HashSet<BasicBlock>());
    _output.Append(" }");

    var text = _output.ToString().Replace("\r\n", " ").Replace("\n", " ");
    (_output, _currentFunction, _indentLevel) = saved;
    _valueNames.Clear(); foreach (var kv in savedNames) _valueNames[kv.Key] = kv.Value;
    return text;
}
```
   Single-line flattening is crude but correct C++; refine only if a test forces it. Do NOT call `InitializeFunctionContext` here (it clears state we must restore); declare the lambda's locals inline via `DeclareLocalsAndTemporaries` only if the body needs temps — let the test drive it.
5. The variable holding the lambda (`Dim f = ...`) needs a callable type: `auto` works when the declaration is emitted with an initializer. Check how `DeclareLocalsAndTemporaries` declares `f` (its TypeInfo is a delegate/function type) — if it pre-declares `f` without initializer, map function-typed locals to `std::function<ret(args)>` (the delegate → `std::function` alias machinery at CppCodeGenerator.cs:263 shows the formatting).
6. Calling `f(5)`: check what IR a call-through-variable produces (likely `IRCall` with the variable as callee or an `IRInstanceMethodCall` of `Invoke`). Map `Invoke` on function-typed values to plain `(...)` call syntax.
7. Remove the lambda diagnostic from `CppCapabilityChecker`.

- [ ] **Step 5.5: Run fixture → PASS; full suite → green** (the `GetValueName` virtualization must not change any other backend — no overrides exist elsewhere).

- [ ] **Step 5.6: Commit** — `feat(cpp-backend): inline C++ lambdas for BasicLang lambda expressions (task 5)`

---

### Task 6: Async (`Task<T>` emulation) + Yield (C++20 coroutines)

**Files:**
- Modify: `BasicLang/CppCodeGenerator.cs` — runtime preamble in `GenerateHeader`, `Visit(IRAwait)` (1536), `Visit(IRYield)` (1542), async/iterator signature handling in `GenerateFunction`/`GenerateFunctionDeclaration`, `Visit(IRReturn)` inside async
- Modify: `BasicLang/TypeMapper.cs` — map `Task`/`Task(Of T)` and `IEnumerable(Of T)` (iterator returns) in `CppTypeMapper`
- Modify: `BasicLang/CppCapabilityChecker.cs` — remove Async/Await/Yield diagnostics; allow `Task`/`IEnumerable` names
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs`

- [ ] **Step 6.1: Write failing tests**

```csharp
[Test]
public void Cpp_AsyncFunction_EmitsTaskEmulation()
{
    var source = @"
Async Function GetValue() As Task(Of Integer)
    Return 42
End Function";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    .That(output, Does.Contain("BasicLang::Task<int32_t> GetValue()"));
    Assert.That(output, Does.Not.Contain("#warning"));
}

[Test]
public void Cpp_IteratorYield_EmitsCoroutine()
{
    var source = @"
Iterator Function Numbers() As IEnumerable(Of Integer)
    Yield 1
    Yield 2
End Function";
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));
    Assert.That(output, Does.Contain("BasicLang::Generator<int32_t> Numbers()"));
    Assert.That(output, Does.Contain("co_yield 1"));
}
```
Replace the two Task-1 diagnostics tests (`Cpp_AsyncFunction_ThrowsCapabilityError`, `Cpp_IteratorYield_ThrowsCapabilityError`). Fix the typo `.That` → `Assert.That` when copying. Match the exact BasicLang async/iterator syntax used by `AsyncFunctionTests.cs` / `TaskResultTests.cs`.

- [ ] **Step 6.2: Run fixture — FAIL**

- [ ] **Step 6.3: Implement**

1. Runtime preamble (emit from `GenerateHeader` only when `module.Functions.Any(f => f.IsAsync || f.IsIterator)`; add `<coroutine>` include only for iterators; note `// requires -std=c++20`):
```cpp
namespace BasicLang {
    // Synchronous Task<T> emulation: type-correct, no scheduler (spec decision 3)
    template <typename T> struct Task {
        T Value;
        T get() const { return Value; }
    };
    template <> struct Task<void> { };

    template <typename T> struct Generator {
        struct promise_type {
            T current;
            Generator get_return_object() { return Generator{ std::coroutine_handle<promise_type>::from_promise(*this) }; }
            std::suspend_always initial_suspend() noexcept { return {}; }
            std::suspend_always final_suspend() noexcept { return {}; }
            std::suspend_always yield_value(T v) { current = v; return {}; }
            void return_void() {}
            void unhandled_exception() { std::terminate(); }
        };
        std::coroutine_handle<promise_type> h;
        explicit Generator(std::coroutine_handle<promise_type> h) : h(h) {}
        Generator(Generator&& o) noexcept : h(o.h) { o.h = nullptr; }
        ~Generator() { if (h) h.destroy(); }
        struct iterator {
            std::coroutine_handle<promise_type> h;
            iterator& operator++() { h.resume(); return *this; }
            T operator*() const { return h.promise().current; }
            bool operator!=(std::default_sentinel_t) const { return !h.done(); }
        };
        iterator begin() { h.resume(); return iterator{ h }; }
        std::default_sentinel_t end() { return {}; }
    };
}
```
2. `CppTypeMapper`: `Task` with one generic arg → `BasicLang::Task<arg>`; bare `Task` → `BasicLang::Task<void>`; in iterator return position `IEnumerable(Of T)` → `BasicLang::Generator<T>` (do this in the generator's signature emission by checking `function.IsIterator`, not globally in the mapper — `IEnumerable` elsewhere stays a permanent capability error).
3. `Visit(IRAwait)` (1536): `WriteLine($"auto {awaitInst.Name} = {GetValueName(awaitInst.Expression)}.get();");` (verify `IRAwait` member names in IRNodes.cs; result-name conventions per neighboring visitors).
4. `Visit(IRYield)` (1542): `co_yield <value>;` / `co_return;` for `IsBreak`.
5. `Visit(IRReturn)` inside an `IsAsync` function: wrap — `return BasicLang::Task<T>{ <value> };` (detect via `_currentFunction.IsAsync`; `T` from the mapped return type's generic arg).
6. Remove Async/Await/Yield diagnostics from checker; allow `Task` name (and `IEnumerable` only on iterator returns — easiest: skip `CheckType` for the return type when `func.IsIterator`).

- [ ] **Step 6.4: Run fixture → PASS; full suite → green**

- [ ] **Step 6.5: Commit** — `feat(cpp-backend): Task<T> async emulation + C++20 coroutine yield (task 6)`

---

### Task 7: End-to-end validation, docs, IDE binaries

**Files:**
- Test: `VisualGameStudio.Tests/Compiler/CppBackendTests.cs` (E2E smoke test)
- Modify: `CLAUDE.md` (C++ backend status), `docs/superpowers/specs/2026-07-05-cpp-backend-overhaul-decisions.md` (mark shipped)
- Modify: `IDE/` — refresh `BasicLang.dll`/`BasicLang.exe` from build output (repo convention after compiler changes)

- [ ] **Step 7.1: Write the E2E smoke test**

One representative program exercising all six features (template class + shared_ptr instances + try/finally + throw/catch + lambda + iterator), compiled through `CompileToCpp`, then — if a real C++ compiler is on PATH — syntax-checked:

```csharp
[Test]
public void Cpp_EndToEnd_GeneratedCodeIsValidCpp()
{
    var source = /* representative program - compose from the passing feature tests */;
    var output = CompileToCpp(source, out var errors);
    Assert.That(errors, Is.Empty, string.Join("; ", errors));

    var compiler = FindCppCompiler(); // probe: "clang++", "g++" via Process("--version"); "cl.exe" via where
    if (compiler == null) Assert.Ignore("No C++ compiler on PATH - structural assertions only");

    var tmp = Path.Combine(Path.GetTempPath(), $"blcpp_{Guid.NewGuid():N}.cpp");
    File.WriteAllText(tmp, output);
    try
    {
        var (exitCode, stderr) = RunProcess(compiler,
            compiler.Contains("cl") ? $"/std:c++20 /Zs \"{tmp}\"" : $"-std=c++20 -fsyntax-only \"{tmp}\"");
        Assert.That(exitCode, Is.EqualTo(0), $"generated C++ failed to compile:\n{stderr}\n---\n{output}");
    }
    finally { File.Delete(tmp); }
}
```

- [ ] **Step 7.2: Run it; fix whatever the real compiler rejects** (this is the step that catches include-ordering, header, and small syntax slips across all six features — debug with @superpowers:systematic-debugging, add a focused regression test per fix)

- [ ] **Step 7.3: Update docs** — CLAUDE.md "C++ backend" note (supported subset, `-std=c++20` requirement, Return-in-Try finally limitation, permanent .NET-type diagnostics); mark the spec shipped.

- [ ] **Step 7.4: Refresh IDE binaries** — build Release, copy BasicLang outputs to `IDE/` (match the file set refreshed in commit `deb98ef`).

- [ ] **Step 7.5: Full suite green; commit** — `feat(cpp-backend): E2E C++ compile validation, docs, IDE binary refresh (task 7)`

---

## Execution notes

- Tasks are strictly ordered (2 depends on 1's test file; 3 reshapes 2's output; 4-6 each delete a Task-1 diagnostic; 7 validates all).
- When a later task changes earlier assertions (noted in 3.4, 5.2, 6.1), update the earlier test *deliberately in the same task* — never leave the suite red between commits.
- Anywhere the plan says "verify/confirm X", do it before writing code; line numbers drift as tasks land.
- If `newObj.Type.GenericArguments` turns out empty in Task 2 (semantic analyzer gap), STOP and investigate the analyzer before adding IR plumbing — that is the one place this plan can grow upstream scope (flagged in the side-chat estimate as the two-day risk).
