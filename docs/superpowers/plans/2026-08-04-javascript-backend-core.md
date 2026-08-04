# JavaScript Backend — Core (Plan 1 of 2) Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `TargetPlatform.JavaScript` backend that compiles a real BasicLang program to a runnable ES module, refuses everything that cannot lower cleanly, and opens the result in a browser from the IDE.

**Architecture:** A new `IIRVisitor` code generator modeled on `ImprovedCSharpCodeGenerator` (not `CodeGeneratorBase` — see Task 2), fronted by a `JsCapabilityChecker` that runs after semantic analysis and before codegen. All types erase: generics, interfaces, and every numeric become untyped JS. Output is one ES module plus a Source Map v3 file plus a generated `index.html`, served over a local `HttpListener` and opened in the system browser.

**Tech Stack:** C# / .NET 8 (compiler), NUnit (tests), Node.js (test execution tier), JavaScript ES2020+ (output).

**Spec:** `docs/superpowers/specs/2026-08-04-javascript-backend-design.md`

---

## Scope

**In this plan (Plan 1):** backend skeleton, Node execution test harness, capability checker (`BL7001`–`BL7007`), core language codegen, stdlib, output shape, source maps, dev server, IDE F5.

**Deferred to Plan 2:** `#JsImport` and `::` interop, the `Extern` language marker, `.bli` declaration files, the `lib.dom.d.ts` generator, and the DOM surface batches.

**Not in either plan** (spec non-goals): page models 2–4, WASM/Emscripten, server-side web apps, static-site generation, bundling, npm.

## File Structure

| File | Responsibility |
|---|---|
| `BasicLang/ICodeGenerator.cs` | **Modify** — add `TargetPlatform.JavaScript` |
| `BasicLang/BackendRegistry.cs` | **Modify** — register the backend under `"JavaScript"` and `"JS"` |
| `BasicLang/JavaScriptBackend.cs` | **Create** — `JavaScriptCodeGenerator`, the `IIRVisitor`. The bulk of the work. |
| `BasicLang/JavaScriptTypeMapper.cs` | **Create** — `ITypeMapper`; near-trivial under erasure |
| `BasicLang/JsCapabilityChecker.cs` | **Create** — `BL7001`–`BL7007` rejections |
| `BasicLang/StdLib/JavaScriptStdLib.cs` | **Create** — `IStdLibProvider` for Console/Math/String/Random/DateTime/Regex |
| `BasicLang/JavaScriptSourceMap.cs` | **Create** — Source Map v3 emitter (VLQ encoding) |
| `BasicLang/JavaScriptEmitter.cs` | **Create** — writes `.js` + `.js.map` + `index.html` to an output dir |
| `BasicLang/Runtime/NodeLocator.cs` | **Create** — extracted from `ExtensionHost`; see Task 3 |
| `VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs:768` | **Modify** — delegate to `NodeLocator` |
| `VisualGameStudio.ProjectSystem/Services/WebPreviewServer.cs` | **Create** — `HttpListener` static server |
| `VisualGameStudio.Tests/Compiler/JavaScript*Tests.cs` | **Create** — one fixture per concern |

Each generator concern stays in its own file rather than growing `JavaScriptBackend.cs` past the ~4,000-line shape `CppCodeGenerator.cs` reached.

---

## Phase 0 — Walking skeleton

### Task 1: Register the backend

**Files:**
- Modify: `BasicLang/ICodeGenerator.cs:11-17`
- Modify: `BasicLang/BackendRegistry.cs:32-70`
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptBackendRegistrationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using NUnit.Framework;
using BasicLang.Compiler.CodeGen;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class JavaScriptBackendRegistrationTests
{
    [Test]
    public void Registry_ResolvesJavaScriptByBothNames()
    {
        Assert.That(BackendRegistry.GetTarget("JavaScript"), Is.EqualTo(TargetPlatform.JavaScript));
        Assert.That(BackendRegistry.GetTarget("JS"), Is.EqualTo(TargetPlatform.JavaScript));
    }

    [Test]
    public void Registry_CreatesJavaScriptGenerator()
    {
        var gen = BackendRegistry.Create(TargetPlatform.JavaScript);
        Assert.That(gen.Target, Is.EqualTo(TargetPlatform.JavaScript));
        Assert.That(gen.BackendName, Is.EqualTo("JavaScript"));
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptBackendRegistrationTests"
```

Expected: compile error — `TargetPlatform` has no member `JavaScript`.

- [ ] **Step 3: Add the enum member**

In `BasicLang/ICodeGenerator.cs`, append to `TargetPlatform`:

```csharp
public enum TargetPlatform
{
    CSharp,
    Cpp,
    LLVM,
    MSIL,
    JavaScript
}
```

Append, never insert — these values are persisted in `.blproj` files.

- [ ] **Step 4: Add a stub generator so the registry can construct one**

Create `BasicLang/JavaScriptBackend.cs`:

```csharp
using System;
using System.Text;
using BasicLang.Compiler.IR;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Emits ES-module JavaScript from BasicLang IR.
    ///
    /// Implements <see cref="IIRVisitor"/> directly rather than extending
    /// <see cref="CodeGeneratorBase"/>, matching ImprovedCSharpCodeGenerator: the base
    /// class names every SSA temp (t0, t1, ...), which produces output nobody can read
    /// in devtools. Readable output is a requirement here, not a nicety — it is half of
    /// what source maps are for.
    /// </summary>
    public class JavaScriptCodeGenerator : ICodeGenerator
    {
        private readonly StringBuilder _output = new StringBuilder();
        private readonly CodeGenOptions _options;

        public string BackendName => "JavaScript";
        public TargetPlatform Target => TargetPlatform.JavaScript;
        public ITypeMapper TypeMapper { get; }

        public JavaScriptCodeGenerator(CodeGenOptions options = null)
        {
            _options = options ?? new CodeGenOptions();
            TypeMapper = new JavaScriptTypeMapper();
        }

        public string Generate(IRModule module) => throw new NotImplementedException();

        // IIRVisitor members are added in Task 2 onward.
    }
}
```

Create `BasicLang/JavaScriptTypeMapper.cs`:

```csharp
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Type mapping under erasure (spec D2). JS has no type annotations, so MapType
    /// exists only to satisfy ITypeMapper and to answer "what default value does an
    /// uninitialised variable of this type get".
    /// </summary>
    public class JavaScriptTypeMapper : ITypeMapper
    {
        public string MapType(TypeInfo type) => "";   // erased

        public string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "null";
            switch (type.Name)
            {
                case "Integer":
                case "Single":
                case "Double": return "0";
                case "Boolean": return "false";
                case "String":  return "\"\"";
                default:        return "null";
            }
        }

        public string MapBinaryOperator(BinaryOpKind op) => op switch
        {
            BinaryOpKind.Add => "+",
            BinaryOpKind.Subtract => "-",
            BinaryOpKind.Multiply => "*",
            BinaryOpKind.Divide => "/",
            _ => "+"    // widened in Task 8
        };

        public string MapComparisonOperator(CompareKind op) => op switch
        {
            CompareKind.Equal => "===",
            CompareKind.NotEqual => "!==",
            CompareKind.LessThan => "<",
            CompareKind.LessThanOrEqual => "<=",
            CompareKind.GreaterThan => ">",
            CompareKind.GreaterThanOrEqual => ">=",
            _ => "==="
        };

        public string MapUnaryOperator(UnaryOpKind op) => op switch
        {
            UnaryOpKind.Negate => "-",
            UnaryOpKind.Not => "!",
            _ => "-"
        };
    }
}
```

Verify the actual member names of `BinaryOpKind`, `CompareKind`, and `UnaryOpKind` in `BasicLang/IRNodes.cs` before compiling — the names above are the expected shape, not a verified list.

- [ ] **Step 5: Register it**

In `BasicLang/BackendRegistry.cs`, add `using BasicLang.Compiler.CodeGen.JavaScript;` and inside `Initialize()`, after the MSIL block:

```csharp
// Register JavaScript backend
Register(TargetPlatform.JavaScript, "JavaScript", opts => new JavaScriptCodeGenerator(opts));
Register(TargetPlatform.JavaScript, "JS", opts => new JavaScriptCodeGenerator(opts));
```

- [ ] **Step 6: Run the tests and confirm they pass**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptBackendRegistrationTests"
```

Expected: 2 passed.

- [ ] **Step 7: Commit**

```bash
git add BasicLang/ICodeGenerator.cs BasicLang/BackendRegistry.cs BasicLang/JavaScriptBackend.cs BasicLang/JavaScriptTypeMapper.cs VisualGameStudio.Tests/Compiler/JavaScriptBackendRegistrationTests.cs
git commit -m "feat(js): register TargetPlatform.JavaScript and a stub generator"
```

---

### Task 2: Hello World — the first end-to-end emit

Proves the visitor wiring works before any feature is added.

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs`
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptCodeGenTests.cs`

- [ ] **Step 1: Write the failing test**

Copy `BuildModule`/`BuildModuleFromProcessed` from `VisualGameStudio.Tests/Compiler/ForeignFeatureGuardTests.cs:57-92` — same pipeline, same asserts.

```csharp
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class JavaScriptCodeGenTests
{
    // BuildModule(...) copied from ForeignFeatureGuardTests — see Step 1 note.

    [Test]
    public void Emits_HelloWorld()
    {
        var module = BuildModule(
            "Sub Main()\nConsole.WriteLine(\"Hello\")\nEnd Sub",
            runPreprocessor: false);

        var js = new JavaScriptCodeGenerator().Generate(module);

        Assert.That(js, Does.Contain("function Main()"));
        Assert.That(js, Does.Contain("console.log(\"Hello\")"));
        Assert.That(js, Does.Contain("Main();"));   // entry point invoked
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptCodeGenTests.Emits_HelloWorld"
```

Expected: FAIL — `NotImplementedException`.

- [ ] **Step 3: Implement `Generate` plus the minimum visitors**

Implement in `JavaScriptBackend.cs`: `Generate(IRModule)` walking `module.Functions`, `Visit(IRFunction)`, `Visit(BasicBlock)`, `Visit(IRCall)`, `Visit(IRConstant)`, `Visit(IRReturn)`. Every other `IIRVisitor` member throws `NotSupportedException($"JS backend: {nameof(X)} not implemented yet")` — a loud stub, never a silent no-op.

`Generate` emits, in order: a `"use strict";` line is **not** needed (modules are strict), the functions, then the entry-point call.

> **Do not copy `CodeGeneratorBase`'s virtual no-op visitors.** `Visit(IRThrow)` and `Visit(IRIndexerStore)` are `virtual {}` there, which is exactly how LLVM/MSIL came to silently drop collection writes (see the TODO at `ICodeGenerator.cs:167`). Every unimplemented visitor throws.

- [ ] **Step 4: Run it and confirm it passes**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptCodeGenTests.Emits_HelloWorld"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add BasicLang/JavaScriptBackend.cs VisualGameStudio.Tests/Compiler/JavaScriptCodeGenTests.cs
git commit -m "feat(js): emit a runnable Hello World"
```

---

### Task 3: Extract the Node locator, then build the execution harness

This is the highest-value task in the plan. Text assertions on generated JS prove nothing about behaviour — and every open C++ backend chip is a silent wrong-output bug that text assertions would have passed.

**Files:**
- Create: `BasicLang/Runtime/NodeLocator.cs`
- Modify: `VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs:768-830`
- Create: `VisualGameStudio.Tests/Compiler/JavaScriptExecutionTests.cs`

- [ ] **Step 1: Extract `FindNodeExecutable` into `BasicLang/Runtime/NodeLocator.cs`**

Move the body of `ExtensionHost.FindNodeExecutable()` (`ExtensionHost.cs:768`) verbatim into `public static string? Find()` on a new `NodeLocator`. Keep the whole probe chain — PATH, `C:\Program Files\nodejs`, `Program Files (x86)`, the `SpecialFolder.ProgramFiles` combine, and the nvm symlink.

- [ ] **Step 2: Make `ExtensionHost` delegate to it**

```csharp
private string? FindNodeExecutable() => BasicLang.Runtime.NodeLocator.Find();
```

> CLAUDE.md: *"Some resolver source is shared across consumers — change it once, not per-consumer."* Duplicating the probe chain would give the IDE and the compiler two Node discovery behaviours that drift.

- [ ] **Step 3: Run the existing extension-host tests to prove the extraction is behaviour-preserving**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration&FullyQualifiedName~Extension"
```

Expected: same pass count as before the change. If any test fails, the extraction was not verbatim — revert and redo.

- [ ] **Step 4: Write the failing execution test**

```csharp
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using BasicLang.Compiler.CodeGen.JavaScript;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
[Category("Integration")]   // spawns node
public class JavaScriptExecutionTests
{
    /// <summary>Compile BasicLang to JS, run it under Node, return stdout.</summary>
    private string RunJs(string basicLangSource)
    {
        var node = BasicLang.Runtime.NodeLocator.Find();
        if (node == null) Assert.Ignore("Node.js not installed — execution tier skipped.");

        var module = BuildModule(basicLangSource, runPreprocessor: false);
        var js = new JavaScriptCodeGenerator().Generate(module);

        var dir = Path.Combine(Path.GetTempPath(), "BasicLang_JsExec_" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "program.mjs");   // .mjs so Node treats it as a module
        File.WriteAllText(file, js);

        var p = Process.Start(new ProcessStartInfo(node!, $"\"{file}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        })!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();

        try { Directory.Delete(dir, true); } catch { }

        Assert.That(p.ExitCode, Is.Zero, $"node exited {p.ExitCode}. stderr:\n{stderr}\n\nGenerated JS:\n{js}");
        return stdout.Trim();
    }

    [Test]
    public void HelloWorld_ActuallyRuns()
    {
        Assert.That(RunJs("Sub Main()\nConsole.WriteLine(\"Hello\")\nEnd Sub"), Is.EqualTo("Hello"));
    }
}
```

> `Assert.Ignore` when Node is absent is deliberate — but note the false-green risk your memory already records for the raylib DLL staging. A skipped tier reads as green. Task 20 adds a roster guard so the whole tier cannot silently vanish.

- [ ] **Step 5: Run it and confirm it passes**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptExecutionTests"
```

Expected: PASS (or an explicit Ignore if Node is genuinely absent — install Node if so; this tier is load-bearing).

- [ ] **Step 6: Commit**

```bash
git add BasicLang/Runtime/NodeLocator.cs VisualGameStudio.ProjectSystem/Services/ExtensionHost.cs VisualGameStudio.Tests/Compiler/JavaScriptExecutionTests.cs
git commit -m "feat(js): Node execution test harness; extract NodeLocator to shared runtime"
```

---

### Task 4: CLI target wiring

**Files:**
- Modify: `BasicLang/Program.cs` (the `--target=` switch)
- Create: `BasicLang/JavaScriptEmitter.cs`
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptEmitterTests.cs`

- [ ] **Step 1: Write the failing test** — `BasicLang.exe prog.bas --target=javascript` writes `prog.js` next to the source, containing `console.log`.
- [ ] **Step 2: Run it, confirm it fails.**
- [ ] **Step 3: Implement** `JavaScriptEmitter.Emit(IRModule, outputDir, baseName)` writing `<baseName>.js`. Wire `--target=javascript` / `--target=js` through the existing switch, routing to the emitter rather than the csproj-plus-`dotnet build` path at `Program.cs:684` — JS has no build step.
- [ ] **Step 4: Run it, confirm it passes.**
- [ ] **Step 5: Commit** — `feat(js): --target=javascript emits a .js file`

---

## Phase 1 — The capability checker

Built before the generator grows, so unsupported constructs are refused rather than half-emitted.

### Task 5: `JsCapabilityChecker` skeleton + passthrough rejection

**Files:**
- Create: `BasicLang/JsCapabilityChecker.cs`
- Modify: `BasicLang/JavaScriptBackend.cs` (call it from `Generate`)
- Test: `VisualGameStudio.Tests/Compiler/JsCapabilityCheckerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void Js_ForeignType_ThrowsCleanError()
{
    var module = BuildModule("Sub Main()\nDim m As std::mutex\nEnd Sub", runPreprocessor: false);
    var ex = Assert.Throws<ForeignFeatureException>(
        () => new JavaScriptCodeGenerator().Generate(module));
    Assert.That(ex!.Message, Does.Contain("JavaScript"));
    Assert.That(ex.Message, Does.Contain("std::mutex"));
}

[Test]
public void Js_CppInclude_ThrowsCleanError()
{
    var module = BuildModule("#CppInclude <mutex>\nSub Main()\nEnd Sub", runPreprocessor: true);
    var ex = Assert.Throws<ForeignFeatureException>(
        () => new JavaScriptCodeGenerator().Generate(module));
    Assert.That(ex!.Message, Does.Contain("JavaScript"));
}

[Test]
public void Js_Collections_DoNotThrow()
{
    var module = BuildModule("Sub Main()\nDim l As New List(Of Integer)()\nEnd Sub", runPreprocessor: false);
    Assert.DoesNotThrow(() => new JavaScriptCodeGenerator().Generate(module));
}
```

- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement.** At the top of `Generate`, mirroring `CSharpBackend.cs:174`:

```csharp
ForeignFeatureChecker.Check(module, "JavaScript", rejectCollections: false, ownInlineLanguage: "javascript");
JsCapabilityChecker.Check(module);
```

`JsCapabilityChecker.Check` is an empty walk for now. Model its structure on `BasicLang/CppCapabilityChecker.cs`.

- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Update the honesty matrix** in the `ForeignFeatureGuardTests` doc comment (`ForeignFeatureGuardTests.cs:23-27`) to add a JavaScript column: passthrough ❌, foreign types ❌, collections ✅ native.
- [ ] **Step 6: Commit** — `feat(js): wire ForeignFeatureChecker and add JsCapabilityChecker skeleton`

---

### Tasks 6–12: One diagnostic per task

Each follows the identical five steps. Do them one at a time and commit each — they are independent, and a single commit per diagnostic keeps each revertable.

| Task | Code | Rejects | Test source | Message must suggest |
|---|---|---|---|---|
| 6 | `BL7001` | method overloading | two `Sub F` with different signatures | "rename one overload" |
| 7 | `BL7002` | `ByRef` parameters | `Sub Bump(ByRef x As Integer)` | "return a value instead" |
| 8 | `BL7003` | `Long` | `Dim n As Long` | "use Integer (Number, exact to 2^53)" |
| 9 | `BL7004` | `Char` | `Dim c As Char` | "use String" |
| 10 | `BL7005` | value `Structure` | `Structure P` … `End Structure` | "use a Class (reference semantics)" |
| 11 | `BL7006` | operator overloading | `Operator +` on a class | — |
| 12 | `BL7007` | .NET BCL types | `Dim s As Stream` | "no BCL in the browser" |

Template for each:

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void Js_ByRef_IsRejected()
{
    var module = BuildModule(
        "Sub Bump(ByRef x As Integer)\nx = x + 1\nEnd Sub\nSub Main()\nEnd Sub",
        runPreprocessor: false);

    var ex = Assert.Throws<ForeignFeatureException>(
        () => new JavaScriptCodeGenerator().Generate(module));
    Assert.That(ex!.Message, Does.Contain("BL7002"));
    Assert.That(ex.Message, Does.Contain("ByRef"));
}
```

- [ ] **Step 2: Run, confirm it fails** (currently it emits wrong JS silently — that is the bug being prevented).
- [ ] **Step 3: Add the detection arm** to `JsCapabilityChecker`, with the code, the source position, and the suggested alternative.
- [ ] **Step 4: Run, confirm it passes.**
- [ ] **Step 5: Commit** — e.g. `feat(js): BL7002 rejects ByRef parameters`

> `BL7xxx` was confirmed unused in-tree at spec time. Re-confirm with `rg "BL7\d{3}"` before Task 6 in case Plan 2 or another session has claimed part of the range.

---

## Phase 2 — Core language codegen

Every task in this phase adds **both** a codegen test and an execution test. The codegen test pins the shape; the execution test proves the behaviour. CLAUDE.md is explicit that a green unit suite has hidden bugs the CLI and optimizer expose.

### Task 13: The numeric model

**Files:** Modify `BasicLang/JavaScriptBackend.cs`, `BasicLang/JavaScriptTypeMapper.cs`. Test: `JavaScriptNumericTests.cs`.

- [ ] **Step 1: Write failing execution tests**

```csharp
[Test] public void IntegerDivision_Truncates()
    => Assert.That(RunJs("Sub Main()\nConsole.WriteLine(7 \\ 2)\nEnd Sub"), Is.EqualTo("3"));

[Test] public void IntegerDivision_TruncatesTowardZero_ForNegatives()
    => Assert.That(RunJs("Sub Main()\nConsole.WriteLine(-7 \\ 2)\nEnd Sub"), Is.EqualTo("-3"));

[Test] public void Mod_MatchesDotNetSign()
    => Assert.That(RunJs("Sub Main()\nConsole.WriteLine(-7 Mod 2)\nEnd Sub"), Is.EqualTo("-1"));

[Test] public void CInt_Truncates()
    => Assert.That(RunJs("Sub Main()\nConsole.WriteLine(CInt(3.7))\nEnd Sub"), Is.EqualTo("3"));
```

The negative cases are the ones that matter — `Math.floor` would give `-4` and `-1` respectively, and only the truncating form matches .NET.

- [ ] **Step 2: Run, confirm failure.**
- [ ] **Step 3: Implement** `\` → `Math.trunc(a / b)`, `Mod` → `%`, `CInt`/`CLng` → `Math.trunc`, `CDbl`/`CSng` → identity. No `|0` masking anywhere — overflow is a documented limit (spec D2).
- [ ] **Step 4: Run, confirm pass.**
- [ ] **Step 5: Commit** — `feat(js): numeric model — truncating integer division, Mod, conversions`

### Tasks 14–22: Language features

Same five-step TDD shape per task, each with a codegen assertion **and** an execution assertion, each committed separately.

| Task | Feature | Lowering | Execution test must prove |
|---|---|---|---|
| 14 | Control flow | `If`/`While`/`For`/`Do` → native; `Select Case` → `if`-chain (guards make `switch` wrong) | a `For` loop sums 1..10 = 55 |
| 15 | Strings | JS `string`; both immutable | concat, `Len`, `Mid`, `UCase` round-trip |
| 16 | Arrays + `For Each` | `Array`, `for…of` | element write is visible after the loop |
| 17 | Classes | `class` / `extends`; interfaces erase | a virtual call dispatches to the override |
| 18 | Collections | `List`→`Array`, `Dictionary`→`Map`, **reference semantics** | mutating through a second alias is visible through the first |
| 19 | `Try/Catch/Finally` | native | **`Return` inside `Try` still runs `Finally`** — the known C++ break must not exist here |
| 20 | Lambdas/closures | arrow functions | a counter closure retains state across calls |
| 21 | `Async`/`Await` | native `async`/`await` | awaited values arrive in order |
| 22 | Iterators/`Yield` | native `function*` | a generator yields 3 values lazily |

> Task 18's aliasing test is the one that catches the whole class of bug your C++ backend hit when value-wrapper collections diverged from .NET. Write it as an execution test, not a text assertion.

> Task 19's test is a regression guard against inheriting a known defect, not a new feature. Keep it even though it passes on first write.

### Task 23: Generics erasure and LINQ

- [ ] Generic functions and classes emit with type parameters dropped; `List(Of Integer)` and `List(Of String)` produce the same JS.
- [ ] LINQ lowers to `.map`/`.filter`/`.reduce`. Execution test: filter-then-project over 5 elements gives the expected array.
- [ ] Commit.

---

## Phase 3 — Standard library

### Task 24: `JavaScriptStdLib`

**Files:** Create `BasicLang/StdLib/JavaScriptStdLib.cs` implementing `IStdLibProvider` (`BasicLang/StdLib/IStdLib.cs:44`). Test: `JavaScriptStdLibTests.cs`.

- [ ] **Step 1: Write failing execution tests** for one function per category:

| Category | BasicLang | JavaScript |
|---|---|---|
| Console | `Console.WriteLine` | `console.log` |
| Math | `Sqr(x)`, `Abs(x)` | `Math.sqrt`, `Math.abs` |
| String | `Len`, `Mid`, `UCase` | `.length`, `.substring`, `.toUpperCase` |
| Random | `Rnd()` | `Math.random()` |
| DateTime | `Now()` | `new Date()` |
| Regex | `RegexMatch(s, p)` | `new RegExp(p).test(s)` |

- [ ] **Step 2–4:** implement, following the `CSharpStdLibProvider` shape at `BasicLang/StdLib/CSharpStdLib.cs`, and confirm pass.
- [ ] **Step 5:** Confirm the **networking** category (`HttpGet`/`HttpPost`/`HttpDownload`, `CSharpStdLib.cs:168-170`) is **not** implemented — `fetch` is async and cannot back a synchronous signature. `CanHandle` must return false so it surfaces as an unsupported-function error rather than emitting something that returns a Promise where a String is expected.
- [ ] **Step 6: Commit** — `feat(js): JavaScriptStdLib for Console/Math/String/Random/DateTime/Regex`

---

## Phase 4 — Output, source maps, and running it

### Task 25: ES module output + `index.html` harness

- [ ] Failing test: `JavaScriptEmitter.Emit` writes `app.js` **and** an `index.html` containing `<script type="module" src="app.js"></script>`.
- [ ] Existing `index.html` in the project directory is **never** overwritten — generate only when absent.
- [ ] Commit.

### Task 26: Source maps

**Files:** Create `BasicLang/JavaScriptSourceMap.cs`. Test: `JavaScriptSourceMapTests.cs`.

- [ ] **Step 1: Failing test** — emitting a 3-line `.bas` produces `app.js.map` with `version: 3`, a `sources` array naming the `.bas` file, and a decodable `mappings` string; `app.js` ends with `//# sourceMappingURL=app.js.map`.
- [ ] **Step 2–4:** implement Source Map v3 with Base64 VLQ segment encoding. Track `(sourceLine, sourceColumn)` per emitted statement — the generator already threads source positions for the `#line` work on the C# backend (`CSharpBackend.cs:60-62`), so reuse that plumbing rather than adding a second position channel.
- [ ] **Step 5:** Round-trip test — decode the `mappings` string and assert a known generated line maps back to the correct `.bas` line.
- [ ] **Step 6: Commit** — `feat(js): Source Map v3 emission`

### Task 27: Local static dev server

**Files:** Create `VisualGameStudio.ProjectSystem/Services/WebPreviewServer.cs`.

- [ ] **Step 1: Failing test** — start on an ephemeral port, `GET /index.html` returns 200 with `Content-Type: text/html`; `GET /app.js` returns `text/javascript`; `GET /missing` returns 404.
- [ ] **Step 2–4:** implement over `HttpListener`. Serve only from the project output directory; **reject any path that escapes it after normalisation** (`..` traversal). Bind `127.0.0.1` only, never `+` or `*`.
- [ ] **Step 5:** MIME map must include `.wasm` → `application/wasm` — C1a/C1b reuse this server and a wrong MIME type breaks WASM streaming instantiation.
- [ ] **Step 6: Commit** — `feat(ide): local static preview server`

### Task 28: IDE F5

- [ ] Wire the Run path so a `<TargetBackend>JavaScript</TargetBackend>` project builds, starts `WebPreviewServer`, and opens the system browser at the served URL.
- [ ] Server stops on debug-end, alongside the existing `RestorePreDebugPanels` teardown.
- [ ] Manual verification: create a JS project from a template, press F5, see output in the browser and a breakpoint land on a `.bas` line in devtools.
- [ ] Commit.

---

## Phase 5 — Gates

### Task 29: Both entry points and the optimizer

CLAUDE.md requires this explicitly; a fix verified through one entry point can break the other.

- [ ] **Step 1:** Add a test compiling a JS project through the **CLI** path (`BasicLang.exe build proj.blproj`) and asserting the emitted `.js` runs under Node.
- [ ] **Step 2:** Add the equivalent through the **IDE** build path (`CompileProjectFiles`).
- [ ] **Step 3:** Add an optimizer-running variant of the Phase 2 execution tests, mirroring `CompileToCppOptimized` in `CppCollectionTests.cs`. Assert identical stdout optimized and unoptimized.
- [ ] **Step 4:** Commit.

### Task 30: Roster guard and full-suite gate

- [ ] **Step 1:** Add a guard test asserting the JS execution tier contains at least N tests and that Node was actually found — so the tier cannot silently skip its way to green (the false-green failure mode your memory records for raylib DLL staging).
- [ ] **Step 2:** Run the fast subset:

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```

Expected: baseline `4125/0/1` plus the new tests, zero failures.

- [ ] **Step 3:** Run the full suite (~39 min), redirecting to a file — it exceeds tool truncation.
- [ ] **Step 4:** Update `docs/superpowers/specs/2026-08-04-javascript-backend-design.md` status to reflect what shipped.
- [ ] **Step 5:** Commit.

---

## Notes for the implementer

- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it corrupts the BOM-less UTF-8 files here. Use Edit/Write. Multi-line commit messages go through a file plus `git commit -F`.
- **Build at project level, not the `.sln`**, if working in a worktree.
- **The class in `CSharpBackend.cs` is `ImprovedCSharpCodeGenerator`.** There is no type named `CSharpBackend` anywhere. Do not grep for one.
- **Another session may be active on `master`** (the VS Code extensions feature). Check `git log` before assuming your branch point.
- Verify enum member names in `BasicLang/IRNodes.cs` before writing any `switch` over `BinaryOpKind`/`CompareKind`/`UnaryOpKind`; the names in Task 1 are the expected shape, not a verified list.
