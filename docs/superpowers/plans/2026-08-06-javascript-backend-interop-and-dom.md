# JavaScript Backend Plan 2 — The Interop Escape Hatch

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a BasicLang program reach the page and call any JavaScript library — untyped, but working — so that "BasicLang produces web sites" becomes true.

**Architecture:** Three passthrough channels, each independent. `#JsImport` emits real ES `import` statements. `javascript{ … }` emits a verbatim block — the **universal** hatch, since it bypasses type checking entirely. `::` passes a raw JavaScript identifier through unmangled, which is ergonomic sugar but **CALL-ONLY** (see below). Together these need no type system.

### ⛔ What `::` can and cannot do — MEASURED, not assumed

An earlier draft of this plan proposed `::document.getElementById("out").textContent = "hi"` as its headline milestone. **That program does not compile**, and no task in this plan makes it compile. Compiled against this worktree:

| Form | Result |
|---|---|
| `::console.log("hi")` — a CALL | Reaches `RejectInlineForeign` — ✅ **this plan's Task 3 enables it** |
| `::document.getElementById("x").textContent = "hi"` — member ASSIGNMENT | ❌ `Cannot assign value of type 'String' to '::document::getElementById::textContent'` — a **SemanticAnalyzer** error, raised before any backend. Task 3 relaxes `ForeignFeatureChecker` and does **not** touch this. |
| `Dim el = ::document.getElementById("x")` — storing a `::` value | ❌ rejected by `CheckType`'s `TypeKind.Foreign` arm. The local's INFERRED type is Foreign, so the declared-type walk catches it (documented at `ForeignFeatureGuardTests.cs:498-507`). |

So after this plan: **you can call raw JavaScript, but you cannot assign to a JS property through `::`, and you cannot store a `::` result in a variable.**

That is why `javascript{ … }` (Task 4) is the universal hatch and carries the milestone — it has no such limits. Task 7 is **optional** and lifts both `::` restrictions if the user wants the sugar to be complete.

**Tech Stack:** C# (BasicLang compiler), NUnit, Node (execution tier), the existing `WebPreviewServer`.

---

## Scope: why this plan is only the escape hatch

The design spec's D5 (a typed DOM declared in `.bli` files, generated from `lib.dom.d.ts`) is a **different feature** — a type system for foreign declarations — with its own extension-list, parser and codegen work. It is outlined as **plan 2b** at the end, with both of its design decisions now settled and one hard prerequisite identified. Bundling it here would delay four tasks of ready, shippable work behind a much larger piece.

**This plan ships alone.** After Task 6 a user can write a real web page — via `javascript{ … }` for anything stateful, `::` for calls, and `#JsImport` for libraries. The typed surface becomes **plan 2b**, outlined at the end with its decisions surfaced.

Explicitly out of scope: `.bli` files, `Extern Class`, the `lib.dom.d.ts` generator, DOM batches, page models 2/3/4, WASM.

## Status of plan 1

Plan 1 (`2026-08-04-javascript-backend-core.md`, tasks 1–30) is complete. Two follow-up fixes landed since:

- `f301240` — `Visit(IRConstant)` emits nothing (optimized IR puts constants in `block.Instructions`).
- `cd4f04d` — `And`/`Or` lower to `&&`/`||`; `AndAlso`/`OrElse` parse; word operators are case-insensitive.

Gate at the time of writing: **392/392** JavaScript + operator tests (191 under Node); fast subset **4438 passed / 2 failed / 1 skipped**, where the 2 failures are the pre-existing `SearchSnippets_*` (chip `task_b9620d48`) and unrelated to any of this work.

---

## Measured facts

Verified against the worktree. **Re-check any row you are about to depend on** — a previous draft of this plan had two wrong rows and they would have sent an engineer down a dead end.

| Fact | Evidence |
|---|---|
| `#JsImport` does not exist. `#CppInclude` is the precedent. | `Preprocessor.cs:133-151`. Two behaviours to copy: collect only when `IsConditionalActive()` (`:139`), and **comment the directive line out rather than remove it** (`:150`) so line numbers survive for source maps. |
| `IRModule.CppIncludes` is threaded onto the combined module at **two** sites. | `Compiler.cs:263` (`CompileFile`) and `:425` (`CompileProjectFiles`). Miss one and the project route silently drops imports. |
| ⛔ **`JsTestSupport` hand-copies preprocessor output too.** | `JsTestSupport.cs:70` — `module.CppIncludes.AddRange(cppIncludes);`. Without a sibling line, Task 1's test cannot pass no matter how correct the product code is. |
| `::` syntax exists end to end — lexer, parser, IR. | `TokenType.ScopeResolution` (`BasicLangLexer.cs:646`), `IsForeignQualified` (`ASTNodes.cs:1486`), stitched verbatim (`Parser.cs:4281`, dispatch `:4286`), survives on `IRVariable.Name` / `IRCall.FunctionName` (`IROperandWalker.cs:41-42`). |
| ⛔ **`::` is rejected at THREE sites, not one.** | `CheckType` (`ForeignFeatureChecker.cs:197-202`, the `TypeKind.Foreign` arm — hit by DECLARED positions), the `IRNewObject` arm (`:127-130`), and `RejectInlineForeign` (`:169-190`, called at `:116`). All three hardcode "foreign C++ type … only available on the C++ backend". |
| ⛔ **An existing test asserts the behaviour this plan inverts.** | `JsCapabilityCheckerTests.cs:21-31` — `Js_ForeignType_ThrowsCleanError` compiles `Dim m As std::mutex` and expects `ForeignFeatureException`. Task 3 must update it deliberately, not discover it. Doc comments at `ForeignFeatureGuardTests.cs:26` and `JsCapabilityCheckerTests.cs:11-12` go stale too. |
| `ownInlineLanguage` is a **required** parameter today, not optional. | `ForeignFeatureChecker.cs:59`. |
| ⛔ **`js{}` DOES NOT EXIST.** Inline blocks are lexer keywords, and there are exactly four. | `BasicLangLexer.cs:216-219` (TokenTypes) and `:563-566` (keyword map): `csharp`, `cpp`, `llvm`, `msil`. `js{ … }` lexes as identifier + `{` and dies in the parser. |
| ⛔ The inline tag is lowercased from the keyword. | `BasicLangLexer.cs:1299` — `language = identifier.ToLower()`. So a `js` keyword yields tag `"js"`, which does **not** match the `ownInlineLanguage: "javascript"` passed at `JavaScriptBackend.cs:129-130`, and `ForeignFeatureChecker.cs:137` would then reject it. |
| Generated JS is a flat script with no `import`/`export`, ending in `Main();`. | `JavaScriptBackend.cs:381`. |
| `Line()` maintains `_generatedLine`, which `RecordMapping` reads. | `JavaScriptBackend.cs:70-78`, `:116`. Emitting imports via `Line()` keeps source maps correct automatically. |
| `SanitizeName` **drops** non-alphanumerics — it does not substitute `_`. | `JavaScriptBackend.cs:653-665`. So `::window.alert` currently mangles to `windowalert`. Any test asserting `Not.Contain("window_alert")` is false-green. |
| The execution-tier roster reads **type-level** `[Category]` and discovers by NAME PREFIX `JavaScript`/`Js`. | `JsExecutionTierRosterTests.cs:108-120`, `:152-154`. A fixture mixing fast and Integration tests cannot carry a class-level category — split it, as `BooleanOperatorTests` / `BooleanOperatorExecutionTests` do (`BooleanOperatorTests.cs:156-162`). |
| The built CLI used by tests lives next to the test binaries. | `CliTestHarness.CliPath()` → `AppContext.BaseDirectory\BasicLang.exe`, i.e. `VisualGameStudio.Tests\bin\Release\net8.0\BasicLang.exe`. ⛔ `IDE\BasicLang.exe` is a **stale separate copy** — never use it to verify a change. |

### Commands used throughout

```bash
dotnet build BasicLang\BasicLang.csproj -c Release
```
```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptInterop"
```
```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```

---

## File structure

| File | Responsibility |
|---|---|
| `BasicLang/Preprocessor.cs` (modify) | Collect `#JsImport` into `JsImports`, mirroring the `#CppInclude` arm |
| `BasicLang/IRNodes.cs` (modify) | `IRModule.JsImports` — sibling of `CppIncludes` |
| `BasicLang/Compiler.cs` (modify) | Thread `JsImports` at **both** combine sites |
| `VisualGameStudio.Tests/Compiler/JsTestSupport.cs` (modify) | Thread `JsImports` in the test helper too |
| `BasicLang/ForeignFeatureChecker.cs` (modify) | Make all three `::` rejections backend-aware |
| `BasicLang/BasicLangLexer.cs` (modify) | `javascript{ … }` token, keyword, and scan arm |
| `BasicLang/JavaScriptBackend.cs` (modify) | Emit imports; emit `::` verbatim; implement `Visit(IRInlineCode)` |
| `VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs` (create) | Codegen tests — **no** class-level `[Category]` |
| `VisualGameStudio.Tests/Compiler/JavaScriptInteropExecutionTests.cs` (create) | Node-executing tests — class-level `[Category("Integration")]`, added to the roster |

Two test files, not one: the roster guard reads type-level categories, so a mixed fixture cannot be rostered.

---

## Task 1: `#JsImport` collects onto the module

**Files:**
- Modify: `BasicLang/IRNodes.cs` (`IRModule`, beside `CppIncludes`)
- Modify: `BasicLang/Preprocessor.cs` (mirror `:133-151`)
- Modify: `BasicLang/Compiler.cs:263` and `:425`
- Modify: `VisualGameStudio.Tests/Compiler/JsTestSupport.cs:38,46,70`
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class JavaScriptInteropTests
{
    [Test]
    public void JsImport_IsCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

        Assert.That(module.JsImports, Is.EqualTo(new[] { "./chart.js" }));
    }

    /// <summary>
    /// An import gated behind an inactive conditional must not be collected.
    ///
    /// ⛔ #IfDef, NOT #If. The Preprocessor implements #IfDef/#IfNDef/#Else/#EndIf
    /// (Preprocessor.cs:89-151); `#If` is a LEXER/parser construct (TokenType.PreprocessorIf)
    /// and never reaches the directive collector, so a test written with #If fails through
    /// JsTestSupport's parse guard no matter how correct the implementation is.
    /// </summary>
    [Test]
    public void JsImport_InsideInactiveConditional_IsNotCollected()
    {
        var module = JsTestSupport.BuildModule(
            "#IfDef NEVER_DEFINED\n#JsImport \"./nope.js\"\n#EndIf\nSub Main()\nEnd Sub",
            runPreprocessor: true);

        Assert.That(module.JsImports, Is.Empty);
    }

    /// <summary>
    /// The directive must be COMMENTED OUT, not removed. A source map built from
    /// IRInstruction.SourceLine is off by one for the whole file otherwise — and that is
    /// exactly the .mod/.cls off-by-one class of bug plan 1 already had to fix once.
    /// </summary>
    [Test]
    public void JsImport_PreservesLineNumbers()
    {
        var module = JsTestSupport.BuildModule(
            "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
            runPreprocessor: true, sourceFilePath: "prog.bas");

        var lines = module.Functions.Single(f => f.Name == "Main")
            .Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.SourceLine > 0).Select(i => i.SourceLine).ToList();

        Assert.That(lines, Does.Contain(3), "Console.WriteLine is on source line 3");
    }

    /// <summary>There are no angle-bracket module specifiers in JavaScript.</summary>
    [Test]
    public void JsImport_WithoutQuotes_IsAnError()
        => Assert.That(() => JsTestSupport.BuildModule(
                "#JsImport ./a.js\nSub Main()\nEnd Sub", runPreprocessor: true),
            Throws.Exception);
}
```

- [ ] **Step 2: Run and confirm failure**

```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~JavaScriptInteropTests"
```

Expected: compile error, `'IRModule' does not contain a definition for 'JsImports'`.

- [ ] **Step 3: Add `JsImports` to `IRModule`**

In `IRNodes.cs`, beside `CppIncludes`, and initialise in the constructor:

```csharp
/// <summary>
/// Module specifiers from #JsImport, in source order and as written, emitted as real ES
/// `import` statements by the JavaScript backend. Sibling of <see cref="CppIncludes"/>:
/// same collection shape, different target language.
/// </summary>
public List<string> JsImports { get; set; }
```

- [ ] **Step 4: Collect the directive in `Preprocessor.cs`**

Add a `_jsImports` list and a `JsImports` property mirroring `:29`, then an arm beside the `#CppInclude` one. Quotes only:

```csharp
else if (trimmedLine.StartsWith("#JsImport", StringComparison.OrdinalIgnoreCase))
{
    if (IsConditionalActive())
    {
        var quote = Regex.Match(trimmedLine, "#JsImport\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
        if (quote.Success) _jsImports.Add(quote.Groups[1].Value);
        else Errors.Add(/* "#JsImport requires a quoted module specifier" — match the
                           surrounding error-construction style exactly */);
    }
    result.AppendLine($"' {line}");   // comment out, never remove — preserves line numbers
}
```

- [ ] **Step 5: Thread it at both combine sites**

`Compiler.cs:263` and `:425`, beside the existing `CppIncludes.AddRange` lines.

- [ ] **Step 6: Thread it through the test helper**

`JsTestSupport.cs` — mirror `cppIncludes` at `:38`, `:46` and `:70`. **Step 2's failure cannot clear without this.**

- [ ] **Step 7: Run and confirm all four pass**

- [ ] **Step 8: Commit**

```bash
git add BasicLang/IRNodes.cs BasicLang/Preprocessor.cs BasicLang/Compiler.cs VisualGameStudio.Tests/Compiler/JsTestSupport.cs VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs
git commit -m "feat(js): #JsImport collects module specifiers onto IRModule"
```

---

## Task 1b: `#JsImport` must be REFUSED on backends that cannot honour it

**Found by the Task 1 code-quality review. Not in the original plan, and it would have been lost.**

The `#CppInclude` precedent has THREE parts and Task 1 copied two: collect, thread, **and reject
on backends that cannot honour it**. `ForeignFeatureChecker.cs:64` throws when a non-C++ backend
sees a non-empty `module.CppIncludes`, and `JsCapabilityCheckerTests.cs:34`
(`Js_CppInclude_ThrowsCleanError`) pins the mirror direction.

Nothing rejects `JsImports`. `#JsImport "./chart.js"` in a **C#-backend** program preprocesses
clean, lands on `CombinedIR`, and is **silently dropped** — precisely the "a refusal beats a half
implementation" line this backend is built on, violated in the other direction.

**Files:** `BasicLang/ForeignFeatureChecker.cs` (the arm at `:64` and the matrix comment at
`:25-31`); test in `JsCapabilityCheckerTests.cs` beside `Js_CppInclude_ThrowsCleanError`.

- [ ] **Step 1: Failing tests** — a `#JsImport` program compiled for the C# backend throws
  `ForeignFeatureException` naming the directive; the same program on JavaScript does not.
- [ ] **Step 2:** Run, confirm the C# case currently passes silently (that IS the bug).
- [ ] **Step 3:** Add the symmetric arm. ⚠ The checker is SHARED and the JS backend calls it with
  `backendName: "JavaScript"` — a naive `JsImports.Count > 0 → throw` breaks JavaScript itself.
  Gate it the same way Task 3 gates `::`, with an explicit parameter rather than a name test.
  ⚠ The C++ backend does not route through this checker at all and needs its own guard.
  Scope per project direction: **C# and C++ only**; LLVM/MSIL are out of scope.
- [ ] **Step 4:** Add a `#JsImport` row to the matrix comment. Run, commit.

---

## Task 2: `#JsImport` emits real `import` statements

**Files:**
- Modify: `BasicLang/JavaScriptBackend.cs` (`Generate`, after the reset block at `:134-138`)
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void JsImport_EmitsAnImportStatement()
    => Assert.That(
        JsTestSupport.Compile("#JsImport \"./chart.js\"\nSub Main()\nEnd Sub", runPreprocessor: true),
        Does.Contain("import \"./chart.js\";"));

/// <summary>
/// Imports go first. ESM hoists them, so this is convention and readability rather than a
/// correctness requirement — but the output is meant to be READ in devtools, and an import
/// buried under function declarations reads as generated sludge.
/// </summary>
[Test]
public void JsImport_ImportsPrecedeDeclarations()
{
    var lines = JsTestSupport
        .Compile("#JsImport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true)
        .Replace("\r\n", "\n").Split('\n')
        .Where(l => l.Trim().Length > 0).ToList();

    var firstImport = lines.FindIndex(l => l.TrimStart().StartsWith("import "));
    var firstFunction = lines.FindIndex(l => l.TrimStart().StartsWith("function "));

    Assert.That(firstImport, Is.GreaterThanOrEqualTo(0), "no import emitted");
    Assert.That(firstImport, Is.LessThan(firstFunction));
}

/// <summary>Two files in one project may import the same module.</summary>
[Test]
public void JsImport_DeduplicatesRepeatedSpecifiers()
{
    var js = JsTestSupport.Compile(
        "#JsImport \"./a.js\"\n#JsImport \"./a.js\"\nSub Main()\nEnd Sub", runPreprocessor: true);

    Assert.That(System.Text.RegularExpressions.Regex.Matches(js, @"import ""\./a\.js""").Count,
        Is.EqualTo(1));
}

/// <summary>
/// A source map must still point at the right .bas lines with imports above the code —
/// which it does only because Line() maintains _generatedLine.
/// </summary>
[Test]
public void JsImport_DoesNotShiftSourceMapPositions()
{
    var module = JsTestSupport.BuildModule(
        "#JsImport \"./a.js\"\nSub Main()\nConsole.WriteLine(1)\nEnd Sub",
        runPreprocessor: true, sourceFilePath: "prog.bas");
    var generator = new BasicLang.Compiler.CodeGen.JavaScript.JavaScriptCodeGenerator();
    var js = generator.Generate(module).Replace("\r\n", "\n").Split('\n');

    var generatedLine = System.Array.FindIndex(js, l => l.Contains("console.log(1)"));
    Assert.That(generatedLine, Is.GreaterThanOrEqualTo(0), "no console.log emitted");

    // Reuse the existing decoder rather than asserting on the raw mappings string —
    // Does.Contain("mappings") passes on ANY source map and proves nothing.
    var pairs = JavaScriptGeneratorSourceMapTests.Decode(generator.SourceMap.ToJson("app.js"));
    var mapped = pairs.Where(p => p.generated == generatedLine).Select(p => p.source).ToList();

    Assert.That(mapped, Does.Contain(2),
        "Console.WriteLine is on source line 3 (0-based 2); imports above it must not shift it");
}
```

⚠ `JavaScriptGeneratorSourceMapTests.Decode` is currently `private static`. Promote it to `internal static` (same assembly) in the same commit, rather than copying the decoder — a second copy will drift from the first.

```csharp
```

- [ ] **Step 2: Run and confirm failure**

- [ ] **Step 3: Emit the imports**

In `Generate`, immediately after `_generatedLine = 0;` and **before** the comment banner:

```csharp
// De-duplicated because two files in one project may import the same module. Emitted via
// Line() rather than appending to _output, so _generatedLine stays accurate and source-map
// positions do not shift by the number of imports.
var seenImports = new HashSet<string>(StringComparer.Ordinal);
foreach (var specifier in module.JsImports ?? new List<string>())
{
    if (!seenImports.Add(specifier)) continue;
    Line($"import \"{EscapeJsString(specifier)}\";");
}
if (seenImports.Count > 0) Line();
```

- [ ] **Step 4: Run and confirm pass**

- [ ] **Step 5: Commit**

```bash
git commit -am "feat(js): #JsImport emits real ES import statements"
```

---

## Task 3: `::` passes raw JavaScript through in EXPRESSION position

**Files:**
- Modify: `BasicLang/ForeignFeatureChecker.cs` (three sites: `:197-202`, `:127-130`, `:169-190`)
- Modify: `BasicLang/JavaScriptBackend.cs`
- Modify: `VisualGameStudio.Tests/Compiler/JsCapabilityCheckerTests.cs:21-31` (asserts the old behaviour)
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptInteropTests.cs`

**The highest-risk task in this plan.** `ForeignFeatureChecker` is shared by five backends.

### The expression/type split — decide before writing code

`::` means two different things depending on position, and only one of them lowers to JavaScript:

- **Expression position** — `::console.log(x)`, `::window.alert(...)`. This is a raw JS identifier. It lowers: emit it verbatim.
- **Type position** — `Dim m As std::mutex`. This is an opaque C++ type. It does **not** lower to JavaScript, and emitting `stdmutex` would be exactly the silent-miscompile class the capability line exists to prevent.

**So `CheckType` (`:197-202`) keeps rejecting for JavaScript.** Only the two VALUE sites relax. Its message should stop saying "C++" for a JS build, but the rejection stays.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void ForeignIdentifier_EmitsVerbatim()
    => Assert.That(JsTestSupport.Compile("Sub Main()\n::console.log(\"hi\")\nEnd Sub"),
        Does.Contain("console.log(\"hi\")"));

/// <summary>
/// ⚠ SanitizeName DROPS non-alphanumerics (JavaScriptBackend.cs:653-665) — it does not
/// substitute underscores. The mangled form is `windowalert`, so that is what must be absent.
/// </summary>
[Test]
public void ForeignIdentifier_IsNotMangled()
{
    var js = JsTestSupport.Compile("Sub Main()\n::window.alert(\"hi\")\nEnd Sub");

    Assert.That(js, Does.Contain("window.alert"));
    Assert.That(js, Does.Not.Contain("windowalert"));
}

/// <summary>A `::` TYPE is still a C++ passthrough type and still does not lower.</summary>
[Test]
public void ForeignType_IsStillRejected()
    => Assert.That(() => JsTestSupport.Compile("Sub Main()\nDim m As std::mutex\nEnd Sub"),
        Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());

// ---- The two limitations this plan does NOT lift. Pinned so they are known, not
// discovered — and so Task 7 (optional) has a red test to turn green.

/// <summary>
/// ⚠ KNOWN LIMITATION. Assignment to a `::` member is a SEMANTIC ANALYZER error, raised
/// before any backend, so relaxing ForeignFeatureChecker cannot reach it. Use
/// javascript{ … } for stateful DOM work, or take Task 7.
/// </summary>
[Test]
public void ForeignMemberAssignment_IsStillRejected_KNOWN()
    => Assert.That(() => JsTestSupport.Compile(
            "Sub Main()\n::document.title = \"hi\"\nEnd Sub"),
        Throws.Exception.With.Message.Contains("Cannot assign"));

/// <summary>
/// ⚠ KNOWN LIMITATION. An inferred local from a `::` expression gets a Foreign type, which
/// CheckType rejects — so a `::` value cannot be stored and reused.
/// </summary>
[Test]
public void ForeignValueInALocal_IsStillRejected_KNOWN()
    => Assert.That(() => JsTestSupport.Compile(
            "Sub Main()\nDim el = ::document.getElementById(\"out\")\nEnd Sub"),
        Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());

/// <summary>REGRESSION GUARD: the other backends keep refusing `::` values entirely.</summary>
[TestCase("csharp")]
[TestCase("llvm")]
[TestCase("msil")]
public void ForeignIdentifier_IsStillRejectedOnOtherBackends(string backend)
{
    var module = JsTestSupport.BuildModule("Sub Main()\n::console.log(\"hi\")\nEnd Sub");

    var ex = Assert.Throws<BasicLang.Compiler.CodeGen.ForeignFeatureException>(
        () => BasicLang.Compiler.Driver.Program.GenerateCode(module, backend));
    Assert.That(ex!.Message, Does.Contain("::"));
}
```

- [ ] **Step 2: Run; confirm the first two fail and the last two pass**

- [ ] **Step 3: Update the test that asserts the OLD behaviour**

`JsCapabilityCheckerTests.cs:21-31` (`Js_ForeignType_ThrowsCleanError`) uses a `::` TYPE, so under the split above it **still passes** — but its doc comment at `:11-12` claims JS rejects `::` wholesale and is now wrong. Fix the comment, and add a sibling test naming the new expression-position behaviour so the pair reads as a deliberate distinction. Also update `ForeignFeatureGuardTests.cs:26`.

- [ ] **Step 4: Make the two VALUE sites backend-aware**

Add an explicit parameter — do **not** sniff the backend name:

```csharp
public static void Check(IRModule module, string backendName, bool rejectCollections,
    string ownInlineLanguage, bool allowForeignIdentifiers = false)
```

⚠ `ownInlineLanguage` is currently **required** (`:59`); keep it required. C++ and JavaScript pass `allowForeignIdentifiers: true`; C#, LLVM and MSIL keep the default and their existing message text, which is asserted by existing tests. The `#CppInclude` rejection at `:64-69` is unrelated and must stay — a `#CppInclude` in a JavaScript program is still an error.

- [ ] **Step 5: Emit `::` names verbatim**

One helper, routed through by `SanitizeName`'s callers — `VariableRef`, `CallTarget`, and `InstanceCall`'s receiver. Do not special-case at each site.

```csharp
/// <summary>
/// A `::`-qualified name is RAW JAVASCRIPT and must reach the output untouched —
/// SanitizeName would drop the dots and turn `console.log` into `consolelog`.
///
/// ⛔ Only a LEADING `::` is a JS passthrough. An INTERIOR one (`mathlib::freeAdd`) is a C++
/// namespace qualification with no JavaScript meaning: stripping it yields `mathlibfreeAdd`,
/// an undefined identifier that reaches the browser from a green build. Refuse it.
///
/// Named ForeignName rather than TryForeignName because it THROWS — a `Try*` that can throw
/// breaks the convention every caller relies on.
/// </summary>
private static bool ForeignName(string name, out string raw)
{
    raw = null;
    if (name == null || !name.StartsWith("::", StringComparison.Ordinal)) return false;

    raw = name.Substring(2);
    if (raw.Contains("::"))
        throw JsCapabilityChecker.ForeignNamespaceRejection(name);   // new BL7009
    return true;
}
```

⚠ **Ordering matters in `CallTarget` (`JavaScriptBackend.cs:796`).** That method routes dotted names through the stdlib mapping and throws on anything unmapped, so the foreign check must run **first** — before the stdlib lookup, not merely in place of `SanitizeName`. The three call sites are `CallTarget:796`, `VariableRef:1783`, and `InstanceCall:1800`.

`ForeignNamespaceRejection` follows the established shape — `JsCapabilityChecker` already exposes `public static ForeignFeatureException` factories for generator-raised codes (`BannedConstantRejection:185`, `ByRefArgumentRejection:491`, `LinqRejection:527`). BL7009 is free (7001 is deliberately unused; 7002–7008 are in use).

- [ ] **Step 6: Add the interior-`::` test**

```csharp
[Test]
public void ForeignIdentifier_WithInteriorNamespace_IsRejected()
    => Assert.That(() => JsTestSupport.Compile("Sub Main()\n::mathlib::freeAdd(1, 2)\nEnd Sub"),
        Throws.Exception.With.Message.Contains("BL7009"));
```

- [ ] **Step 7: Run everything, then the fast subset — this is the blast-radius check**

```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```

Expected: no new failures beyond the 2 pre-existing `SearchSnippets_*`.

- [ ] **Step 8: Commit**

```bash
git commit -am "feat(js): :: passes raw JavaScript identifiers through unmangled"
```

---

## Task 4: `javascript{ … }` inline blocks

**Files:**
- Modify: `BasicLang/BasicLangLexer.cs:216-219` (TokenType), `:563-566` (keyword), and the scan arm near `:1291`
- Modify: `BasicLang/JavaScriptBackend.cs` (`Visit(IRInlineCode)` — currently `throw NotYet`)
- Test: both interop test files

⛔ There is **no `js{}` today** — inline blocks are lexer keywords and there are exactly four. And the tag is lowercased at `:1299`, so the keyword spelling must match the `ownInlineLanguage` string passed at `JavaScriptBackend.cs:129-130`. **Use the keyword `javascript`**, matching that string, rather than adding `js` and then having to normalise two spellings in the checker.

### ⛔ Two things this task's plan got wrong — MEASURED during execution

1. **There is a FIFTH list, in the SemanticAnalyzer.** `Visit(InlineCodeNode)`
   (`SemanticAnalyzer.cs:6397`) carries its own `{ "csharp", "cpp", "llvm", "msil" }` allow-list
   and errors with *"Unsupported inline code language"*. The lexer changes alone make
   `javascript{ }` **lex** and then **fail analysis** — every codegen test dies in
   `JsTestSupport`'s parse/analyze guard, not at `NotYet(IRInlineCode)` as the plan predicted.
   Same "N independent lists, add an arm to N−1" shape the repo has paid for repeatedly; this
   one at least fails loudly.
2. **The C++ backend SILENTLY DROPS a foreign inline block**, and always has.
   `CppCodeGenerator.Visit(IRInlineCode)` emits `// WARNING: Inline <lang> code not supported`
   into the generated C++ and continues — a do-nothing program from a build that reported
   success. C#/LLVM/MSIL refuse this through `ForeignFeatureChecker`'s inline arm; C++ does not
   run that checker and never got the guard. Fixed the same way Task 1b fixed `#JsImport`: an
   arm in `CppCapabilityChecker.CheckInstruction`, which covers `Generate` **and**
   `GenerateSplit`. It was already true for `csharp{ }`/`llvm{ }`/`msil{ }` — the honesty
   matrix's last dishonest cell, found only because the `javascript{ }` mirror test drove it.

Also settled here rather than deferred to a release note: the scan arm now falls back to
`Identifier` when no `{` follows, so all five tags are CONTEXTUAL keywords. `Dim javascript As
Integer` stays legal — see the risk row at the end of this plan.

- [ ] **Step 1: Write the failing tests**

```csharp
// JavaScriptInteropTests.cs (codegen)
[Test]
public void InlineJavaScriptBlock_EmitsVerbatim()
    => Assert.That(JsTestSupport.Compile("Sub Main()\njavascript{ console.log(\"inline\"); }\nEnd Sub"),
        Does.Contain("console.log(\"inline\");"));

/// <summary>A block tagged for another backend must still be refused here.</summary>
[Test]
public void InlineCppBlock_IsStillRejectedOnJavaScript()
    => Assert.That(() => JsTestSupport.Compile("Sub Main()\ncpp{ int x = 1; }\nEnd Sub"),
        Throws.TypeOf<BasicLang.Compiler.CodeGen.ForeignFeatureException>());
```

```csharp
// JavaScriptInteropExecutionTests.cs
[Test]
public void InlineJavaScriptBlock_Executes()
    => Assert.That(JavaScriptExecutionTests.RunJs(
        "Sub Main()\njavascript{ console.log(\"inline\"); }\nEnd Sub"),
        Is.EqualTo("inline"));
```

- [ ] **Step 2: Run and confirm failure**

Expected: `JsTestSupport`'s parse guard (`JsTestSupport.cs:62`) throws `InvalidOperationException` — **not** `NotYet(IRInlineCode)`. The block does not lex, so it never reaches the generator.

- [ ] **Step 3: Add the lexer support**

`TokenType.InlineJavaScript` beside the other four; `{ "javascript", TokenType.InlineJavaScript }` in the keyword map; and the scan arm that forms an `InlineCode` token. Read `:1291-1299` and follow it exactly.

- [ ] **Step 4: Implement `Visit(IRInlineCode)`**

Emit the block's text one output line per source line, via `Line()`, so `_generatedLine` stays accurate. A block whose language is not `"javascript"` must throw — the checker admits only that tag.

- [ ] **Step 5: Run both fixtures and confirm pass**

- [ ] **Step 6: Commit**

```bash
git commit -am "feat(js): javascript{} inline blocks emit verbatim"
```

---

## Task 5: `#JsImport` targets reach the output directory

**Files:**
- Modify: `BasicLang/JavaScriptEmitter.cs` (signature + copy logic — **do it here, once**)
- Callers to check: `Program.cs:593-604` (CLI project), `Program.cs:1150-1157` (CLI single file), **`VisualGameStudio.ProjectSystem/Services/BuildService.cs:922` (IDE)**
- Test: `VisualGameStudio.Tests/Compiler/JavaScriptInteropExecutionTests.cs`

Without this, the headline claim is false for the shipping routes: the project route writes `.js` into `bin/<config>/<tfm>/` while a user's `./chart.js` stays in the project directory, so the emitted `import "./chart.js"` 404s in the browser. An execution test that hand-writes the helper module into the temp directory **hides** this.

⛔ **THREE emit call sites, not two.** `BuildService.cs:922` is the IDE route — and it is the one this task's own test drives, since `result.OutputPath` comes from `BuildProjectAsync`. Putting the copy inside `JavaScriptEmitter.Emit` covers all three at once; patching call sites would reproduce the repo's documented "three independent maps, a missing arm is silent" failure.

### ⛔ Measured during execution — two more rules, and one real limitation

4. **Containment, not just self-copy.** `../shared/util.js` is a legal ES specifier that resolves
   ABOVE the output directory. Copying it writes outside the build output, and one more `..`
   reaches the project directory and overwrites a source file. Refused with a warning, reusing
   `SafeZip.IsWithin` — the repo's one containment predicate — rather than growing a second.
5. **Node needs `package.json` `{"type":"module"}`.** Node parses `.js` as CommonJS unless told
   otherwise; automatic detection only became the default in **22.7**, so whether the emitted
   site runs under Node depends on the reader's version. Written into the output directory
   **only when the program has imports** (so a plain program leaves the user's directory alone —
   the single-file route emits NEXT TO THE SOURCE) and **only when absent** (same rule as
   `index.html`). This settles the ESM hazard Task 6 Step 6 flagged as open; browsers were never
   affected.

~~⚠ **KNOWN LIMITATION, found by running it.** `#JsImport` emits a **side-effect-only**
`import "./m.js";` — no binding clause, so **no names are bound**.~~
✅ **CLOSED** by the binding forms below. The bare form still binds nothing, which is correct ES
(a side-effect import), and is now pinned as such by
`ProjectRoute_BareJsImport_RunsTheModuleButBindsNoNames` rather than as a gap.

## Follow-up (shipped): `#JsImport` binding forms

**The syntax mirrors ES exactly**, because the person writing one is reading MDN or a package
README, not a BasicLang manual — the same reasoning that settled plan 2b's decision 2. It costs
nothing structurally: the preprocessor comments the directive line out BEFORE lexing, so `From`
and `As` never reach the keyword table, the parser, or any other backend.

| BasicLang | JavaScript |
|---|---|
| `#JsImport "./m.js"` | `import "./m.js";` |
| `#JsImport { greet, other } From "./m.js"` | `import { greet, other } from "./m.js";` |
| `#JsImport { greet As hi } From "./m.js"` | `import { greet as hi } from "./m.js";` |
| `#JsImport lib From "./m.js"` | `import lib from "./m.js";` (default) |
| `#JsImport * As lib From "./m.js"` | `import * as lib from "./m.js";` (namespace) |

- **`IRModule.JsImports` became `List<JsImportDirective>`** — an import has two independent
  parts, and only one of them is a file path. `JavaScriptEmitter` copies by `Specifier` while the
  backend emits by `Clause`; one string could serve either, never both.
- **De-dup keys on clause AND specifier.** `{ a } From "./m.js"` and `{ b } From "./m.js"` are two
  imports of one module — collapsing on the specifier alone drops `b`, and it surfaces as
  `b is not defined` at run time. The COPY still de-dupes on specifier alone: two clauses, one file.
- **Keyword casing is normalised, imported NAMES are not.** BasicLang is case-insensitive, so
  `AS`/`as`/`As` all arrive — but `import { a AS b }` is a SyntaxError. The names are the opposite
  case: `{ Greet }` must stay `Greet`, since ES named imports fail at LINK time and a "helpfully
  corrected" name renders a blank page.
- **BL7010 — an import binding that collides with a program declaration.** They share one JS
  module scope and redeclaring an import is a SyntaxError: the module fails to PARSE, so nothing
  runs at all. Compared on the EMITTED name (via the generator's own `SanitizeName`) and
  case-SENSITIVELY, because `greet` and `Greet` genuinely coexist in JavaScript. The fix the
  message suggests is the alias form.
  - ⛔ Two bugs in the first cut of this check, both caught by boundary tests: class methods
    **do** appear in `module.Functions` (flattened, unqualified), so it flagged a legal
    `Class Widget` with a `render` method — fixed by using `CollectMemberImplementations()`, the
    generator's own predicate; and the same directive written twice de-dupes to one import, so it
    is not a collision — fixed by walking the de-duplicated list.
- `::greet(...)` reaches an imported name with no extra work — **measured**, not assumed
  (`ProjectRoute_ImportedName_IsCallableThroughForeignSyntax`).
- Milestone re-verified in a browser with an ordinary `export function greet()` and
  `#JsImport { greet } From "./greet.js"`: renders "Hello from BasicLang", console clean.

Gate: JS suite **523/523** · fast subset **4679 / 2 pre-existing / 1 skipped**.

⚠ **Test placement deviates from the plan.** These tests live in `JavaScriptCliProcessTests`,
not `JavaScriptInteropExecutionTests`: the question is an ENTRY-POINT one — what lands in each
route's output directory — and that fixture already owns both CLI routes, the IDE route, the
Node harness and a `SilentOutput`. Duplicating that scaffolding to honour the plan's filename
would have been the drift this repo keeps paying for.

**Three things to specify before writing code:**

1. **Resolution base.** `IRModule.JsImports` is a flat `List<string>` with no per-file origin, so `./helper.js` has no unambiguous base in a multi-file project. Simplest defensible rule: resolve against the **project directory** (single-file route: the source file's directory), and say so in the diagnostic when a file is not found. If per-file resolution is wanted later, `JsImports` needs to carry the importing file — a schema change, not part of this task.
2. **Self-copy.** The single-file route writes output NEXT TO THE SOURCE (`Program.cs:1140`), so source dir == output dir and `File.Copy(src, dst)` with `src == dst` throws `IOException`. Guard on the normalised full paths being equal.
3. **`Emit`'s signature must change** — it currently takes `(outputDirectory, scriptFileName, javaScript, title, sourceMapJson)` (`JavaScriptEmitter.cs:42-47`) and has no way to receive the import list or a base directory. Add both as optional parameters so existing callers keep compiling.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
[Category("Integration")]
public async Task JsImport_RelativeTarget_IsCopiedToTheOutputDirectory()
{
    // Project dir: Main.bas (imports "./helper.js") + helper.js
    await File.WriteAllTextAsync(Path.Combine(_dir, "helper.js"),
        "export function greet() { return \"hi\"; }\n");
    await File.WriteAllTextAsync(Path.Combine(_dir, "Main.bas"),
        "#JsImport \"./helper.js\"\nSub Main()\njavascript{ console.log(\"ran\"); }\nEnd Sub\n");
    await WriteProjectOnly();   // <TargetBackend>JavaScript</TargetBackend>

    var (exit, stdout, stderr) = await CliTestHarness.RunCli(
        _dir, "build", Path.Combine(_dir, "Site.blproj"));
    Assert.That(exit, Is.Zero, $"{stdout}\n{stderr}");

    var scripts = Directory.GetFiles(Path.Combine(_dir, "bin"), "*.js", SearchOption.AllDirectories);
    var siteDir = Path.GetDirectoryName(scripts.First(s => !s.EndsWith("helper.js")))!;

    Assert.That(File.Exists(Path.Combine(siteDir, "helper.js")), Is.True,
        "an imported relative module must be copied beside the emitted script, or the " +
        "browser 404s on it");
    Assert.That(RunNodeFile(Path.Combine(siteDir, "Site.js")), Is.EqualTo("ran"));
}

/// <summary>Bare specifiers and URLs are the user's concern — package management is a
/// stated non-goal — so they must be left completely alone.</summary>
[Test]
public void JsImport_BareSpecifier_IsNotCopiedAndIsNotAnError()
{
    // #JsImport "lodash" compiles clean, emits import "lodash";, copies nothing.
}

/// <summary>The spec says a missing module is not a compile error.</summary>
[Test]
public void JsImport_MissingRelativeTarget_WarnsRatherThanFails()
{
    // #JsImport "./absent.js" — exit 0, a warning naming the file, no copy.
}
```

- [ ] **Step 2: Run and confirm failure**

- [ ] **Step 3: Implement in `JavaScriptEmitter.Emit`** — per the three rules above.

- [ ] **Step 4: Verify all three routes.** The test above covers the CLI project route; add the single-file route (self-copy guard) and one through `BuildService`.

- [ ] **Step 5: Run and confirm pass. Step 6: Commit.**

---

## Task 6: Gate, and the end-to-end proof

- [ ] **Step 1: Full JavaScript suite**

```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~Compiler.JavaScript|FullyQualifiedName~Compiler.Js"
```

- [ ] **Step 2: Fast subset** — blast radius for `ForeignFeatureChecker` and the lexer, both shared with every backend.

- [ ] **Step 3: Optimizer parity**

**Every shipping route optimizes unconditionally and the test helper does not** — that is how the `Visit(IRConstant)` bug survived 351 green tests. Add these three to `JavaScriptOptimizedExecutionTests`, using its existing `AssertSameOutput` oracle:

```csharp
[Test]
public void Optimized_ForeignCallStillWorks()
    => AssertSameOutput("Sub Main()\n::console.log(\"hi\")\nEnd Sub");

/// <summary>An inline block is opaque to the optimizer — confirm it is not dropped as dead.</summary>
[Test]
public void Optimized_InlineJavaScriptSurvives()
    => AssertSameOutput("Sub Main()\njavascript{ console.log(\"inline\"); }\nEnd Sub");

/// <summary>A foreign call inside a folded-constant context must still be emitted once.</summary>
[Test]
public void Optimized_ForeignCallBesideFoldedConstants()
    => AssertSameOutput(
        "Sub Main()\nDim x As Integer = 2 + 3 * 4\n::console.log(\"hi\")\nConsole.WriteLine(x)\nEnd Sub");
```

`#JsImport` needs no optimizer test — it is a module-level directive the optimizer never sees.

- [ ] **Step 4: Both entry points through the real binary**

Add cases to `JavaScriptCliProcessTests` using `VisualGameStudio.Tests\bin\Release\net8.0\BasicLang.exe` via `CliTestHarness`. ⛔ Not `IDE\BasicLang.exe` — that is a stale copy.

- [ ] **Step 5: Roster**

Add `JavaScriptInteropExecutionTests` to `JsExecutionTierRosterTests.ExecutionTier` and bump `RosterIsPinned` from 17 to 18. The name prefix `JavaScript` means discovery finds it; the class-level `[Category("Integration")]` means `EveryFixtureIsStillCategorisedIntegration` passes. `JavaScriptInteropTests` (codegen) must **not** be rostered and must **not** carry a class-level category.

- [x] **Step 6: THE MILESTONE — a real web page. ✅ DONE 2026-08-06.**

**It works.** Compiled through the real
`VisualGameStudio.Tests\bin\Release\net8.0\BasicLang.exe`, served over HTTP, loaded in a browser:
the page renders **"Hello from BasicLang"**, with no console errors.

Verified, in order:

| | Result |
|---|---|
| `BasicLang.exe page.bas --target=javascript` | ✅ `page.js`, `index.html`, `page.js.map`, **`package.json`** |
| Emitted script | `import "./greet.js";` at the top, then `function Main() { document.getElementById("out").textContent = greet("BasicLang"); }` |
| Page over `http://127.0.0.1:…` (**not** `file://` — ES modules are CORS-gated and a `file://` origin is opaque) | ✅ renders **"Hello from BasicLang"**, console clean |
| Source map | `sources: ["page.bas"]`, **`sourcesContent` inlined**, generated line 5 → **page.bas line 4** — the `javascript{ … }` line, which is exactly what a devtools breakpoint binds to |

⚠ `greet.js` publishes through `globalThis`, not `export` — forced by the side-effect-only import
(see the Task 5 limitation above). That is the shape a user has to write today.

The original checklist, kept for reproduction:

⛔ The milestone uses `javascript{ … }`, **not** `::`. A `::` member assignment does not compile (see the limitations table at the top of this plan) — an earlier draft of this plan used one and it was wrong. Add a codegen assertion for this exact program in `JavaScriptInteropTests` first, so the headline example is guarded by a test rather than only by a checklist.

```
1. Create a folder with page.bas:

       #JsImport "./greet.js"

       Sub Main()
           javascript{ document.getElementById("out").textContent = greet("BasicLang"); }
       End Sub

2. Beside it, greet.js:

       export function greet(who) { return "Hello from " + who; }

   (`export`/named import shape depends on how Task 2 emits the import — if it emits a
   side-effect-only `import "./greet.js";`, use a global instead of an export.)

3. Compile:  VisualGameStudio.Tests\bin\Release\net8.0\BasicLang.exe page.bas --target=javascript
4. Edit the generated index.html, adding  <div id="out"></div>  inside <body>.
5. Serve it — the IDE's F5, or any local server. NOT file://: ES modules are CORS-gated and
   a file:// origin is opaque, so the page silently loads nothing.
6. Confirm the page shows "Hello from BasicLang".
7. Set a devtools breakpoint on the javascript{} line; confirm it lands on the .bas line.
```

✅ **The Node/ESM hazard is SETTLED** (Task 5): the emitter writes a `package.json` containing
`{"type":"module"}` into the output directory, but only when the program has imports and only
when absent. Node parses `.js` as CommonJS unless told otherwise and automatic detection only
became the default in 22.7, so without it whether the emitted site runs under Node depended on
the reader's version. Browsers were never affected — `type="module"` on the script tag settles
it there.

- [ ] **Step 7: Update the spec's status line**

`docs/superpowers/specs/2026-08-04-javascript-backend-design.md:4` still reads "**Status:** Design — not started" after 20+ shipped commits.

- [ ] **Step 8: Commit**

---

## Task 7 (OPTIONAL) — Make `::` complete

**Only take this if the user wants `::` to be more than call-only sugar.** Tasks 1–6 ship without it; `javascript{ … }` already covers everything this unlocks, less ergonomically.

Two independent restrictions, in two different components:

- [ ] **Step 1: Member assignment.** `::document.title = "hi"` fails in the **SemanticAnalyzer** with "Cannot assign value of type 'String' to '::document::title'" — nothing to do with `ForeignFeatureChecker`. Find the assignment type-check and let a Foreign-typed target accept any value: a foreign member has no knowable type, so the check has nothing to check. Pin with the `_KNOWN` test from Task 3, inverted.

- [ ] **Step 2: Inferred locals.** `Dim el = ::document.getElementById("x")` fails in `CheckType`'s `TypeKind.Foreign` arm (`ForeignFeatureChecker.cs:197-202`) because the local's INFERRED type is Foreign. Distinguish an **annotated** `Dim m As std::mutex` — which must stay rejected, since a C++ type genuinely does not lower — from an **inferred** one. ⚠ `ForeignFeatureGuardTests.cs:498-507` documents this exact behaviour for the other backends and must keep passing for them.

- [ ] **Step 3:** Execution tests for both, through Node with a DOM shim. Then the milestone in Task 6 Step 6 can be rewritten in `::` form, which is what a user would reach for first.

- [ ] **Step 4: Commit.**

---

## Risks

| Risk | Mitigation |
|---|---|
| ⛔ **`::` is call-only**, so the obvious DOM idioms (property assignment, storing an element) do not compile. A user will hit this in their first five minutes. | Measured and tabled at the top of this plan; pinned by two `_KNOWN` tests; `javascript{ … }` carries the milestone; Task 7 lifts it if wanted. **Say this in the user-facing notes** — discovering it by trial is a bad first experience. |
| **`ForeignFeatureChecker` is shared by five backends**, and `::` is rejected at three sites with C++-specific messages. | Explicit `allowForeignIdentifiers` parameter defaulted `false`; only the two VALUE sites relax; regression tests assert C#/LLVM/MSIL still refuse. Fast subset after Task 3. |
| **Interior `::` silently miscompiles.** `mathlib::freeAdd` → `mathlibfreeAdd`, an undefined identifier from a green build. | Only a LEADING `::` is a passthrough; interior occurrences raise BL7009 (Task 3 Step 5). |
| **`::` in type position has no JS meaning.** | Deliberately still rejected — `CheckType` does not relax. Pinned by `ForeignType_IsStillRejected`. |
| **The inline-block tag is lowercased**, so a `js` keyword would not match `ownInlineLanguage: "javascript"` and the checker would reject what the lexer just accepted. | Use the keyword `javascript`, matching the existing string exactly. |
| **`#JsImport` targets never reaching the output** would make the feature useless in a real project while every test passes. | Task 5 exists for this, and its test builds through the project route rather than hand-placing the file. |
| **The optimizer runs on every shipping route and not in the test helper.** | Task 6 Step 3. Not hypothetical — this is exactly how the `IRConstant` bug reached a green suite. |
| **Roster mechanics**: a mixed fast/Integration fixture cannot be rostered. | Two test files from the start; Task 6 Step 5 spells out which is which. |
| **`::` and `javascript{}` make it possible to write unchecked JavaScript**, bypassing the capability line entirely. | That is the POINT of an escape hatch, and the spec (D4) chose it — but such code has no type checking and no BL70xx protection. Document it; do not try to type-check it. |
| **Adding `javascript` as a lexer keyword makes it unusable as an identifier** — `Dim javascript As Integer` becomes a parse error, because the scan arm emits the keyword token even when no `{` follows (`BasicLangLexer.cs:1291-1303`). | Pre-existing for `csharp`/`cpp`/`llvm`/`msil`, so this is consistent rather than novel — but `javascript` is a plausible variable name on this backend specifically. One line in the release note, or make the scan arm fall back to `Identifier` when `{` does not follow (which would improve all five). |
| **ESM-in-`.js` under Node** — the emitted file gains `import` but is still named `.js`, which older Node parses as CommonJS. | Settled explicitly in Task 6 Step 6. Browsers are unaffected (`type="module"` on the tag). |

---

# Plan 2b (deferred) — The typed DOM

**Not part of this plan.** It is a separate feature — a type system for foreign declarations. Both of its blocking decisions are now **SETTLED** (user, 2026-08-06), so 2b is ready to be written as a full plan whenever wanted.

### Decision 1 — `Extern` marks a declaration-only type ✅ SETTLED

```basiclang
Public Extern Class Element
    ...
End Class
```

`Extern` is overloaded deliberately. It already parses as per-backend inline implementations — `Extern Function F() … CSharp: "…" End Extern` (`ParseExtern`, `Parser.cs:1752`), producing a function-shaped `IRExternDeclaration` (`IRNodes.cs:1529-1547`) with no members. The unifying reading is **"defined outside BasicLang"**: `Extern Function` with a body says *how* to define it per backend; `Extern Class` with no body says *it already exists in the target runtime*. No new keyword, no new concept for the user.

### Decision 2 — declare members with their EXACT JavaScript names ✅ SETTLED

```basiclang
Public Extern Class Element
    Public Property textContent As String
    Public Property innerHTML As String
    Public Function querySelector(sel As String) As Element
    Public Sub addEventListener(evt As String, handler As Action)
End Class
```

No conversion rule and no `Alias` syntax. The declaration carries the real JS name and codegen emits **the declared name**, verbatim.

**Why this over implicit camelCase or explicit aliases:**

- **Exact by construction.** Implicit "lowercase the first letter" handles `TextContent`→`textContent` and `GetElementById`→`getElementById`, but mangles `document.URL` into `uRL`. Every such member then needs an override anyway, so the implicit rule buys nothing and costs a second mechanism plus a rule for when it is wrong.
- **The plan-3 generator becomes a COPY, not a transform.** `lib.dom.d.ts` already holds the exact names. Copying cannot be wrong; transforming has to detect its own failures.
- **IntelliSense matches MDN.** Completion shows `textContent`, which is what the user just read on the docs page. No mental translation.
- **camelCase at the call site is informative** — it signals "this is a foreign type", the same way `::` does.

The cost — `.bli` files not following BasicLang's PascalCase convention — is accepted: these are declarations *of foreign types*, and looking foreign is honest.

### ⛔ Decision 2 has a hard prerequisite: chip `task_8f4dcdb2`

**Member access currently emits the USE-SITE casing, not the declared name.** Measured:

```basiclang
Class Box
    Public TextContent As String
End Class
Sub Main()
    Dim b As New Box()
    b.textContent = "hi"          ' lowercase t — accepted, BasicLang is case-insensitive
    Console.WriteLine(b.TextContent)
End Sub
```

emits two DIFFERENT JS properties and prints nothing:

```js
class Box { TextContent = ""; }
b.textContent = "hi";        // writes one
const t2 = b.TextContent;    // reads another
```

On the C# backend the same program emits `b.textContent` against `public string TextContent` and fails late in `csc` (CS1061). On JavaScript it is silently wrong from a green build.

So a user writing `element.TextContent` out of BasicLang habit would emit `.TextContent` and get `undefined`. **Extern member resolution must canonicalise to the declared name** — which is required no matter which mapping was chosen, since an `Alias` mechanism would have to do exactly the same thing. Fixing `task_8f4dcdb2` at IR-build time makes every backend inherit it and turns Decision 2 into "emit what the declaration says", with no extra machinery.

This is the same shape as the operator bug fixed in `cd4f04d`: case-insensitive front end, case-sensitive back end.

### Work 2b must cover that this plan does not

- **⛔ FIRST: chip `task_8f4dcdb2`** (declared-name canonicalisation), per Decision 2 above. Everything else in 2b is silently wrong without it.
- **`Extern Class` codegen emits NOTHING for the type itself.** An extern class exists in the runtime; emitting `class Element { }` would SHADOW the real one. See the `EmitClass` note below.
- **`.bli` in THREE independent extension lists**, not one: `ModuleResolver.SupportedExtensions` (`:15`), `ProjectFile.BasicLangSourceExtensions` (`:81-82`), and `VisualGameStudio.Core/Constants/FileExtensions.SourceExtensions` (`:15`). Consequences of missing any:
  - `ProjectFile.GetSourceFiles()` (`:415`) globs by the second list — miss it and **the project route never compiles the DOM lib**.
  - `DocumentManager.cs:183`, `TextDocumentSyncHandler.cs:41`, `LspProjectContext.cs:343` and `:394` all key off the second list — miss it and there is **no completion, no hover, no diagnostics** on a `.bli`, which defeats spec D5's entire "zero LSP changes" rationale.
  - `ProjectService.cs:246` uses the third — miss it and the IDE files `.bli` as Content, not Compile.
  - Roughly ten further hardcoded literal lists (`RefactoringService.cs` ×5, `MainWindowViewModel.cs:5196/5340`, `FindInFilesViewModel.cs:94`, `SolutionExplorerViewModel.cs:1297`, `CommandPaletteViewModel.cs:509`) will silently exclude `.bli`. `LspMixedProjectTests.cs:186-190` documents that this exact drift already happened once with `.basic`/`.class`.
- **`Program.cs:161` gating** — `IsSourceFile` makes `BasicLang.exe dom-core.bli` a valid compile target. A declaration-only file must not be an entry point.
- **`Public Extern Class` needs TWO parser edits.** `ParseTopLevelDeclaration` handles bare `Extern` at `:106-107`, but the modifier path is a separate block at `:125-236` with no `Extern` arm and throws at `:233-235`.
- **`JsCapabilityChecker` likely needs NO change** — `BuildAllowedTypeNames` (`:262-263`) already admits every declared class and interface. Verify rather than assume work.
- **`EmitClass` must skip extern types.** `Generate` iterates `module.Classes.Values` (`:163-167`) unfiltered, so an auto-loaded `Element` would emit `class Element { }` and **shadow the real DOM type** — a runtime failure with no compile error. Also decide what happens to extern members flattened by `CollectMemberImplementations()` (`:152`), which have no `Implementation`.
- **`console` collides with `Console`, and Decision 2 does NOT save you from it.** `Console` is already the stdlib surface (`JsCapabilityChecker.cs:259`, `StdLibRegistry.cs:37`). Declaring `Extern Class console` in a `.bli` looks distinct under Decision 2's exact-JS-name rule, but `IRModule.Classes` is keyed `OrdinalIgnoreCase`, so the two ARE the same key. Leave `console` out of the DOM declarations entirely — the stdlib already lowers `Console.WriteLine` to `console.log`.
- **BL7007's exception-by-suffix rule** (`JsCapabilityChecker.cs:293`) admits anything ending in `Exception` — so a generated `DOMException` passes the allow-list even if its declaration is missing. A false green waiting for plan 3.
- **DOM auto-loading must be backend-gated** (`project.Backend`, `Program.cs:566`) — `Element` would collide with a user type on the C# backend — **and must cover the single-file route**, which has no `.blproj` and therefore no `TargetBackend`.
