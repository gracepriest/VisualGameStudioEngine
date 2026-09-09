# `Extern Class` — Typed Foreign Declarations Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a BasicLang program DECLARE a type that already exists in the JavaScript runtime, and use it with real type checking — closing the gap where every DOM idiom today is either a call-only `::` or an unchecked `javascript{ }` block.

**Architecture:** `Extern Class` is a declaration-only type: signatures, no bodies, no emission. It reuses the machinery `Interface` already has (body-less members parse identically) and adds one bit — `IsExtern` — that flows AST → semantic → IR → backends. The JavaScript backend emits **nothing** for the type itself and calls members by their **declared** name. Every other backend REFUSES it, because a type that exists in a JS runtime does not exist in .NET or C++.

**Tech Stack:** C# (BasicLang compiler), NUnit, Node (execution tier).

---

## ✅ STATUS: Tasks 1–9 COMPLETE. Milestone verified in a browser.

**Gate (full suite, because this touches `Parser.cs` and `SemanticAnalyzer.cs`): 5467 passed /
5 failed — all five MEASURED pre-existing.** The four known ones, plus
`CppFinallyExecutionTests.Finally_RunsAfterACaughtException`, which is NEW but **fails on
pristine `origin/master` too** (verified in a scratch worktree at `54eeda2`): the C++ program
prints `2` instead of `12`, i.e. the catch body runs and `Finally` is skipped. That is the other
session's in-flight `Finally` work — `a29d65b` fixed some paths and not this one — and is
unrelated to this plan. ⚠ **Worth telling that session: their gate did not cover this case.**

A hand-declared DOM slice, compiled through the real CLI and served over HTTP, renders
**"Typed DOM from BasicLang"** with a clean console — the first time the DOM is reached with
TYPE CHECKING rather than through call-only `::` or an unchecked `javascript{ }` block:

```basiclang
Extern Class Element
    Public Property textContent As String
End Class
Extern Class Document
    Public Function getElementById(id As String) As Element
End Class

Sub Main()
    Dim doc As Document
    javascript{ doc = document; }
    Dim out As Element
    out = doc.getElementById("out")
    out.textContent = "Typed DOM from BasicLang"
End Sub
```

### What execution found that the plan did not predict

1. **The emission bug was worse than "a redundant class".** MEASURED before the guard:
   `class Element { textContent = ""; querySelector(sel) { return null; } }`. Two failures in
   one — the declaration SHADOWS the real type, *and* every member got a SYNTHESIZED stub, so
   `el.querySelector("p")` answered **null**. An empty shadowing class might have happened to
   work; a null-returning stub cannot.
2. **Task 2 was a diagnostic fix, not a semantic check.** A member with a body was ALREADY
   refused — by the generic "Unexpected token in class" arm, whose suggestion lists Sub and
   Function as valid members. The user concluded the parser was confused rather than that
   bodies are the problem. A refusal that misdirects is barely better than none.
3. **The mirror test caught one for the FOURTH time on this backend.** C# emitted a real
   `public class Element` with a null-returning `querySelector` — a fake type that compiles,
   runs, and answers null.
4. **The roster guard was blind, and had been for two fixtures' whole lives.**
   `RosterCoversEveryJavaScriptIntegrationFixture` discovered by NAME PREFIX
   (`JavaScript`/`Js`). Widening it for `ExternClassExecutionTests` immediately surfaced
   `BooleanOperatorExecutionTests` and `MemberCasingExecutionTests` — both driving
   `JavaScriptExecutionTests.RunJs`, both never rostered, both uncounted by the floor. Pin
   18 → 21.
5. **The open question is answered: an extern type IS obtainable.** A `javascript{ }` block
   binds the local (the declaration emits `let api;` in the same scope) and members then reach
   the real object. No extra task needed.
6. ⚠ Recorded for 2c: BL7007 admits ANY name ending in `Exception`, so a generated declaration
   file could omit one and the omission would not be caught. Pinned by
   `Bl7007_AdmitsAnyExceptionSuffixedName_KNOWN`.

---

## Scope: this is plan 2b, narrowed

The plan-2 outline bundled four independent subsystems. **This plan is the first only** — the language feature, declared in ordinary `.bas` files. It ships working software on its own: a user hand-declares the DOM surface they touch and gets typed, checked access, with no `::` call-only limit and no untyped block.

Deferred to a later plan (**2c**), each for a stated reason:

| Deferred | Why not here |
|---|---|
| `.bli` declaration-only files | ~15 extension sites, ~10 of them hardcoded literal arrays. `LspMixedProjectTests.cs:186-190` documents this exact drift happening before with `.basic`/`.class`. Independent of the language feature. |
| `lib.dom.d.ts` → `.bli` generator | A separate tool with its own failure modes. Needs `.bli` first. |
| DOM auto-loading | Needs the generator's output to load. Must be backend-gated and must cover the single-file route (no `.blproj`, so no `TargetBackend`). |

A user can already get the whole value by writing `Extern Class Element` in their own `.bas`.

---

## ⛔ Recon corrections — the outline was wrong twice. Re-measure anything you depend on.

| Outline claim | MEASURED 2026-08-07 |
|---|---|
| "⛔ FIRST: chip `task_8f4dcdb2` (declared-name canonicalisation). **Everything else in 2b is silently wrong without it.**" | ❌ **ALREADY FIXED.** `IRBuilder.CanonicaliseMemberNames()` (`IRBuilder.cs:72`, `:99`) runs on every `Build()`. The outline's exact repro now emits `b.TextContent` at BOTH sites and prints `hi`; the outline documents it printing nothing. **There is no Task 0.** |
| "`.bli` in THREE independent extension lists, plus roughly ten further hardcoded lists" | ⚠ Undercounted, and it missed a structural one: `Compiler.cs:467-468` decides BY EXTENSION whether a file may hold `Main`. Irrelevant here (deferred), but do not trust the outline's list when 2c is written. |
| "`Public Extern Class` needs TWO parser edits" | ✅ **CONFIRMED.** `ParseTopLevelDeclaration` handles bare `Extern` at `Parser.cs:106-107`; the modifier block at `:125-156` has no `Extern` in its `Check(...)` set and no `Extern` arm after the modifier loop. |
| "`EmitClass` must skip extern types or it SHADOWS the real DOM type" | ✅ **CONFIRMED.** `JavaScriptBackend.cs:196` iterates `module.Classes.Values` unfiltered into `EmitClass`. |
| "`JsCapabilityChecker` likely needs NO change — verify rather than assume" | ✅ **CONFIRMED.** `BuildAllowedTypeNames` (`JsCapabilityChecker.cs:320`) admits everything the module declares, so a declared `Extern Class` is allowed for free. Task 6 pins that rather than leaving it to luck. |

### ⭐ The load-bearing discovery: an extern member IS an interface member

`ParseInterface` (`Parser.cs:1329-1380`) already parses exactly the shape an extern class needs — `ParseInterfaceFunction`, `ParseInterfaceSub`, and `ParseProperty`'s auto-property path for a body-less property. **Reuse it; do not write a second body-less member parser.** The two would drift, and the interface one already carries a hard-won fix (see its PROGRESS INVARIANT comment at `:1369` — the loop once spun forever on an unrecognised member, measured at 11.86s of CPU with output frozen).

---

## Measured facts

| Fact | Evidence |
|---|---|
| Member names are canonicalised to the DECLARED spelling at IR-build time. | `IRBuilder.CanonicaliseMemberNames()`, `IRBuilder.cs:72`/`:99`. This is what makes Decision 2 ("emit the declared name verbatim") free. |
| `IRModule.Classes` is keyed `OrdinalIgnoreCase`. | `IRNodes.cs:1423`. So `Extern Class console` and the stdlib `Console` ARE the same key — see Task 7. |
| JS class emission is unfiltered. | `JavaScriptBackend.cs:196`. |
| The generator's `SanitizeName` is `internal`. | `JavaScriptBackend.cs:747` (made internal for BL7010). Reusable by checkers. |
| BL7010 is taken; BL7011 is the next free code. | `JsCapabilityChecker.cs` — 7001 unused, 7002–7010 in use. |
| `ForeignFeatureChecker` is shared by 5 backends and takes explicit opt-in flags. | `ForeignFeatureChecker.cs:104-108` (`allowForeignIdentifiers`, `allowJsImports`). The same pattern is how `Extern Class` gets refused elsewhere. |
| The C++ backend does NOT run `ForeignFeatureChecker`. | Its guards live in `CppCapabilityChecker.Check`, which covers `Generate` AND `GenerateSplit`. Learned the hard way twice (`#JsImport`, foreign inline blocks). |

### Commands used throughout

```bash
dotnet build BasicLang\BasicLang.csproj -c Release
```
```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ExternClass"
```
```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"
```

⚠ The 2 standing fast-subset failures are `SearchSnippets_{Empty,Whitespace}Query_ReturnsAll` (chip `task_b9620d48`), pre-existing and unrelated.

---

## File structure

| File | Responsibility |
|---|---|
| `BasicLang/ASTNodes.cs` (modify) | `ClassNode.IsExtern` |
| `BasicLang/Parser.cs` (modify) | `Extern Class` after bare `Extern` (`:106`) and in the modifier block (`:125-156`); body-less members via the interface member parsers |
| `BasicLang/SemanticAnalyzer.cs` (modify) | Register the type; refuse a member WITH a body; refuse `New` on an extern class |
| `BasicLang/IRNodes.cs` (modify) | `IRClass.IsExtern` |
| `BasicLang/IRBuilder.cs` (modify) | Thread `IsExtern` onto `IRClass` |
| `BasicLang/JavaScriptBackend.cs` (modify) | Skip extern classes in `Generate`'s class loop |
| `BasicLang/JsCapabilityChecker.cs` (modify) | BL7011 — a name that would collide with the stdlib surface |
| `BasicLang/ForeignFeatureChecker.cs` (modify) | Refuse `Extern Class` on C#/LLVM/MSIL via an explicit opt-in flag |
| `BasicLang/CppCapabilityChecker.cs` (modify) | The C++ refusal, in `Check` so it covers `GenerateSplit` too |
| `VisualGameStudio.Tests/Compiler/ExternClassTests.cs` (create) | Parse/semantic/codegen — **no** class-level `[Category]` |
| `VisualGameStudio.Tests/Compiler/ExternClassExecutionTests.cs` (create) | Node-executing — class-level `[Category("Integration")]`, added to the roster |

Two test files, because `JsExecutionTierRosterTests` reads TYPE-level categories and a mixed fixture cannot be rostered.

⚠ The roster discovers by name prefix `JavaScript`/`Js` (`JsExecutionTierRosterTests.cs:152-153`). `ExternClassExecutionTests` matches NEITHER, so `RosterCoversEveryJavaScriptIntegrationFixture` will NOT catch its absence. Task 8 adds it explicitly and widens the discovery predicate.

---

## Task 1: `Extern Class` parses

**Files:**
- Modify: `BasicLang/ASTNodes.cs` (`ClassNode`)
- Modify: `BasicLang/Parser.cs:106-107` and `:125-156`
- Test: `VisualGameStudio.Tests/Compiler/ExternClassTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using NUnit.Framework;
using BasicLang.Compiler;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class ExternClassTests
{
    /// <summary>
    /// Parses only — no semantic analysis. Keeps a parse failure distinguishable from a
    /// semantic one, which matters because Parser.Parse() CATCHES ParseException internally,
    /// records it, and returns normally with zero declarations. Asserting on Errors is the
    /// only way to see it.
    /// </summary>
    private static ProgramNode Parse(string source)
    {
        var parser = new Parser(new Lexer(source).Tokenize());
        var ast = parser.Parse();
        Assert.That(parser.Errors.Select(e => e.ToString()), Is.Empty, "parse errors");
        return ast;
    }

    private const string ElementDecl =
        "Extern Class Element\n" +
        "Public Property textContent As String\n" +
        "Public Function querySelector(sel As String) As Element\n" +
        "Public Sub addEventListener(evt As String, handler As Action)\n" +
        "End Class\n";

    [Test]
    public void ExternClass_Parses()
    {
        var cls = Parse(ElementDecl + "Sub Main()\nEnd Sub").Declarations
            .OfType<ClassNode>().Single();

        Assert.That(cls.Name, Is.EqualTo("Element"));
        Assert.That(cls.IsExtern, Is.True);
    }

    /// <summary>
    /// ⛔ THE SECOND PARSER EDIT. `Public Extern Class` goes through a DIFFERENT block
    /// (Parser.cs:125-156), which has no Extern in its Check(...) set and no Extern arm after
    /// the modifier loop. Bare `Extern Class` passing proves nothing about this one.
    /// </summary>
    [Test]
    public void PublicExternClass_Parses()
    {
        var cls = Parse("Public " + ElementDecl + "Sub Main()\nEnd Sub").Declarations
            .OfType<ClassNode>().Single();

        Assert.That(cls.IsExtern, Is.True);
        Assert.That(cls.Access, Is.EqualTo(AccessModifier.Public));
    }

    /// <summary>Members are SIGNATURES: no bodies, no End Function/End Property.</summary>
    [Test]
    public void ExternClass_MembersAreSignaturesOnly()
    {
        var cls = Parse(ElementDecl + "Sub Main()\nEnd Sub").Declarations
            .OfType<ClassNode>().Single();

        Assert.That(cls.Properties.Select(p => p.Name), Is.EquivalentTo(new[] { "textContent" }));
        Assert.That(cls.Methods.Select(m => m.Name),
            Is.EquivalentTo(new[] { "querySelector", "addEventListener" }));
    }

    /// <summary>
    /// ⛔ Member names keep their EXACT JavaScript casing. This is the whole point of the
    /// feature: codegen emits the declared name verbatim, so a "corrected" PascalCase name
    /// would call a member the runtime does not have and yield undefined.
    /// </summary>
    [Test]
    public void ExternClass_MemberCasingIsPreservedExactly()
    {
        var cls = Parse("Extern Class Node\nPublic Property nodeValue As String\n" +
                        "Public Property URL As String\nEnd Class\nSub Main()\nEnd Sub")
            .Declarations.OfType<ClassNode>().Single();

        Assert.That(cls.Properties.Select(p => p.Name),
            Is.EquivalentTo(new[] { "nodeValue", "URL" }),
            "neither camelCase nor an all-caps acronym may be normalised");
    }

    /// <summary>An ordinary class is unaffected — IsExtern is opt-in.</summary>
    [Test]
    public void OrdinaryClass_IsNotExtern()
        => Assert.That(Parse("Class Box\nPublic X As Integer\nEnd Class\nSub Main()\nEnd Sub")
            .Declarations.OfType<ClassNode>().Single().IsExtern, Is.False);
}
```

- [ ] **Step 2: Run and confirm failure**

```bash
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ExternClassTests"
```

Expected: compile error, `'ClassNode' does not contain a definition for 'IsExtern'`.

- [ ] **Step 3: Add `ClassNode.IsExtern`**

Beside `IsAbstract` in `ASTNodes.cs`:

```csharp
/// <summary>
/// Declaration-only: the type already exists in the target runtime, so nothing is emitted
/// for it and its members have signatures rather than bodies. `Extern` is deliberately
/// overloaded — `Extern Function` with a body says HOW to define something per backend,
/// `Extern Class` with no body says it is ALREADY defined. Both read as "defined outside
/// BasicLang".
/// </summary>
public bool IsExtern { get; set; }
```

- [ ] **Step 4: Parse the bare form**

In `ParseExtern()` (`Parser.cs:1752`), after the `Extern Dim` arm and before the
`ExternDeclarationNode` is constructed:

```csharp
// Extern Class Name ... End Class — a declaration-only type. Delegates to ParseClass so
// there is ONE class parser; only the flag differs.
if (Check(TokenType.Class))
{
    var externClass = ParseClass();
    externClass.IsExtern = true;
    return externClass;
}
```

⚠ `ParseClass` must accept body-less members when `IsExtern` is set. Reuse the interface
member parsers (`ParseInterfaceFunction`/`ParseInterfaceSub`, and `ParseProperty`'s
auto-property path) — see the recon note above. Do **not** write a second body-less parser.

- [ ] **Step 5: Parse the modifier form**

In the modifier block (`Parser.cs:125-156`): add `TokenType.Extern` to BOTH `Check(...)`
sets, track `bool isExtern` in the modifier loop, and add an arm beside the `Class` one:

```csharp
if (isExtern && Check(TokenType.Class))
{
    var cls = ParseClass();
    cls.IsExtern = true;
    cls.Access = access;
    return cls;
}
```

⚠ `Extern` combined with `Async`/`Iterator`/`Inline`/`Shared` is meaningless. Report it
rather than ignoring it — a silently-dropped modifier is how a user learns the wrong model.

- [ ] **Step 6: Run and confirm all five pass. Step 7: Commit**

```bash
git commit -am "feat(lang): Extern Class parses as a declaration-only type"
```

---

## Task 2: A member WITH a body is refused

**Files:** `BasicLang/SemanticAnalyzer.cs`; test in `ExternClassTests.cs`

An extern member that carries a body is a contradiction: the body can never run, because
nothing is emitted for the type. Accepting it silently discards code the user wrote.

- [ ] **Step 1: Write the failing tests** — a `Public Sub click()` with statements inside an
  `Extern Class` produces a semantic error naming the member; the same body in an ordinary
  class still compiles.
- [ ] **Step 2:** Run, confirm the body is currently accepted and silently dropped (that IS the bug).
- [ ] **Step 3:** Add the check where `ClassNode` members are analyzed. Message names the class,
  the member, and says the body cannot run because an extern type emits nothing.
- [ ] **Step 4:** Run, commit.

---

## Task 3: `IsExtern` reaches the IR

**Files:** `BasicLang/IRNodes.cs` (`IRClass`), `BasicLang/IRBuilder.cs`; test in `ExternClassTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void ExternClass_IsExternSurvivesToTheIr()
{
    var module = JsTestSupport.BuildModule(ElementDecl + "Sub Main()\nEnd Sub");

    Assert.That(module.Classes["Element"].IsExtern, Is.True);
}
```

- [ ] **Step 2:** Run, confirm failure. **Step 3:** Add `IRClass.IsExtern`, thread it in
  `IRBuilder`'s `Visit(ClassNode)`. **Step 4:** Run, commit.

---

## Task 4: The JavaScript backend emits NOTHING for an extern class

**Files:** `BasicLang/JavaScriptBackend.cs:196`; tests in both fixtures

**The highest-value task in this plan.** Emitting `class Element { }` would SHADOW the real
DOM type — a runtime failure with no compile error, which is precisely the silent-miscompile
class this backend refuses everywhere else.

- [ ] **Step 1: Write the failing tests**

```csharp
[Test]
public void ExternClass_EmitsNoClassDeclaration()
{
    var js = JsTestSupport.Compile(ElementDecl + "Sub Main()\nEnd Sub");

    Assert.That(js, Does.Not.Contain("class Element"),
        "emitting the class would SHADOW the real runtime type — a runtime failure with " +
        "no compile error");
}

/// <summary>
/// ⛔ Members must not leak either. CollectMemberImplementations() drives what the class
/// loop skips; an extern member has NO Implementation, so confirm nothing tries to emit a
/// body that does not exist.
/// </summary>
[Test]
public void ExternClass_EmitsNoMemberBodies()
{
    var js = JsTestSupport.Compile(ElementDecl + "Sub Main()\nEnd Sub");

    Assert.That(js, Does.Not.Contain("querySelector"));
    Assert.That(js, Does.Not.Contain("textContent"));
}

/// <summary>A member CALL still emits, by the DECLARED name, verbatim.</summary>
[Test]
public void ExternClass_MemberCallEmitsTheDeclaredName()
{
    var js = JsTestSupport.Compile(
        ElementDecl +
        "Sub Main()\nDim e As Element\nConsole.WriteLine(e.textContent)\nEnd Sub");

    Assert.That(js, Does.Contain(".textContent"));
}

/// <summary>
/// ⭐ THE PAYOFF, and the reason chip task_8f4dcdb2 had to be fixed first: a BasicLang user
/// typing `.TextContent` out of PascalCase habit must still emit `.textContent`. Without
/// canonicalisation this emits the use-site casing and silently reads undefined.
/// </summary>
[Test]
public void ExternClass_UseSiteCasingIsCanonicalisedToTheDeclaredName()
{
    var js = JsTestSupport.Compile(
        ElementDecl +
        "Sub Main()\nDim e As Element\nConsole.WriteLine(e.TextContent)\nEnd Sub");

    Assert.That(js, Does.Contain(".textContent"));
    Assert.That(js, Does.Not.Contain(".TextContent"));
}
```

- [ ] **Step 2:** Run and confirm failure. **Step 3:** Filter the class loop:

```csharp
foreach (var irClass in module.Classes.Values)
{
    // An extern type ALREADY EXISTS in the runtime. Emitting a declaration for it would
    // shadow the real one — `class Element {}` shadows the DOM's Element — and the failure
    // is a runtime one from a build that reported success.
    if (irClass.IsExtern) continue;
    EmitClass(irClass, module);
    Line();
}
```

- [ ] **Step 4:** Run. **Step 5:** Also add the OPTIMIZED variants via
  `JsTestSupport.CompileOptimized` — every shipping route optimizes unconditionally and the
  plain helper does not. **Step 6:** Commit.

---

## Task 5: `New` on an extern class is refused

**Files:** `BasicLang/SemanticAnalyzer.cs`; test in `ExternClassTests.cs`

`New Element()` cannot work: nothing was emitted, so there is no constructor. The browser
would throw `Element is not a constructor` at runtime.

- [ ] **Step 1:** Failing test — `Dim e As New Element()` is refused, naming the type and
  saying an extern type is obtained from the runtime rather than constructed.
- [ ] **Step 2–4:** Run, implement, run, commit.

⚠ Check whether `IRNewObject` on an extern class can also arrive without a declared local
(`Console.WriteLine(New Element())`). If the semantic check misses that shape, the
JavaScript backend's `NewObject` renderer needs the same refusal — the same
declared-position-vs-expression-temporary split that `ForeignFeatureChecker` documents.

---

## Task 6: Every OTHER backend refuses `Extern Class`

**Files:** `BasicLang/ForeignFeatureChecker.cs`, `BasicLang/CppCapabilityChecker.cs`;
tests in `ExternClassTests.cs`

⛔ **This is the mirror test, and on this backend it has found a silently-dropped feature
every single time it has been written** — `#JsImport` on C#/C++, and foreign inline blocks
on C++ (broken since they existed). Write it BEFORE assuming the refusal exists.

An `Extern Class Element` names a type in a JavaScript runtime. On the C# backend there is no
such type; on C++ likewise. Emitting a C# `class Element` that the user never wrote, or
silently dropping the declaration, are both wrong.

- [ ] **Step 1: Write the failing tests**

```csharp
[TestCase("csharp")]
[TestCase("cpp")]
public void ExternClass_IsRejectedOnOtherBackends(string backend)
{
    var module = JsTestSupport.BuildModule(ElementDecl + "Sub Main()\nEnd Sub");

    Assert.That(() => BasicLang.Compiler.Driver.Program.GenerateCode(module, backend),
        Throws.Exception, $"the {backend} backend silently accepted an Extern Class");
}

[Test]
public void ExternClass_IsAcceptedOnJavaScript()
    => Assert.DoesNotThrow(() => JsTestSupport.Compile(ElementDecl + "Sub Main()\nEnd Sub"));
```

- [ ] **Step 2:** Run. Record which backends already refuse and which accept silently —
  that measurement is the finding, whichever way it goes.
- [ ] **Step 3:** Add an `allowExternClasses` opt-in to `ForeignFeatureChecker.Check`
  (default `false`; JavaScript passes `true`), matching `allowForeignIdentifiers` and
  `allowJsImports` exactly. ⛔ Do NOT sniff `backendName` — it is display text for a
  diagnostic, and a backend renamed for readability would silently change what it accepts.
- [ ] **Step 4:** Add the C++ arm to `CppCapabilityChecker.Check` — **not** to
  `CppCodeGenerator.Generate`, because the project route goes through `GenerateSplit` and a
  guard at the Generate site would leave the SHIPPING path unguarded.
- [ ] **Step 5:** Add a `Extern Class` row to the honesty-matrix comment
  (`ForeignFeatureChecker.cs:25-42`). **Step 6:** Run the fast subset — this file is shared by
  five backends. **Step 7:** Commit.

---

## Task 7: BL7011 — an extern name that collides with the stdlib surface

**Files:** `BasicLang/JsCapabilityChecker.cs`; test in `ExternClassTests.cs`

⛔ **`console` and `Console` are the SAME KEY.** `IRModule.Classes` is
`OrdinalIgnoreCase` (`IRNodes.cs:1423`), and `Console` is already the stdlib surface
(`JsCapabilityChecker.cs`, `StdLibRegistry.cs`). So `Extern Class console` does not merely
look confusing — it collides, and the exact-JS-name rule does not save you.

- [ ] **Step 1:** Failing tests — `Extern Class console` is refused with BL7011; an
  unrelated `Extern Class Element` is not.
- [ ] **Step 2–3:** Run; add the check beside the BL7010 collision walk, reusing
  `JavaScriptCodeGenerator.SanitizeName` so the comparison is on the EMITTED name.
- [ ] **Step 4:** Run, commit.

⚠ Also verify BL7007's exception-by-suffix rule (`JsCapabilityChecker.cs`, the
`EndsWith("Exception")` arm) does not admit an undeclared extern type by accident. Pin
whichever behaviour is correct; do not leave it unmeasured.

---

## Task 8: Execution tier + roster

**Files:** `VisualGameStudio.Tests/Compiler/ExternClassExecutionTests.cs` (create),
`VisualGameStudio.Tests/Compiler/JsExecutionTierRosterTests.cs` (modify)

- [ ] **Step 1:** Create the fixture with class-level `[Category("Integration")]`. Node has no
  DOM, so declare an extern class over something Node DOES have, and RUN it:

```csharp
/// <summary>
/// An extern class over a real runtime object — proving the declaration is erased and the
/// member call reaches the actual object. Node has no DOM, so JSON stands in for it; the
/// mechanism under test (declare, erase, call by declared name) is identical.
/// </summary>
[Test]
public void ExternClass_MemberCallReachesTheRuntimeObject()
    => Assert.That(JavaScriptExecutionTests.RunJs(
        "Extern Class JsonApi\nPublic Function stringify(v As Object) As String\nEnd Class\n" +
        "Sub Main()\njavascript{ globalThis.api = JSON; }\n" +
        "Dim api As JsonApi\n::console.log(api.stringify(42))\nEnd Sub"),
        Is.EqualTo("42"));
```

⚠ Verify this shape compiles before relying on it — `Dim api As JsonApi` with no
initialiser may need a `javascript{ }` assignment to bind, and the binding path is exactly
what Task 5's `New` refusal constrains. **If it does not, that is a finding: an extern type
that cannot be OBTAINED is useless, and this plan needs a task for it.**

- [ ] **Step 2:** Add `ExternClassExecutionTests` to `JsExecutionTierRosterTests.ExecutionTier`
  and bump `RosterIsPinned` from 18 to 19.
- [ ] **Step 3:** ⛔ Widen `RosterCoversEveryJavaScriptIntegrationFixture`'s discovery
  predicate (`JsExecutionTierRosterTests.cs:152-153`) — it matches name prefixes
  `JavaScript`/`Js` only, so `ExternClassExecutionTests` would NOT be caught if someone
  removed it from the roster. The guard is worth nothing if the next fixture escapes it.
- [ ] **Step 4:** Run both fixtures, commit.

---

## Task 9: Gate

- [ ] **Step 1:** Both new fixtures.
- [ ] **Step 2:** Full JavaScript suite.
- [ ] **Step 3:** Fast subset — blast radius for `Parser.cs`, `SemanticAnalyzer.cs` and
  `ForeignFeatureChecker.cs`, all shared by every backend. Expect no new failures beyond the
  2 pre-existing `SearchSnippets_*`.
- [ ] **Step 4:** ⛔ **FULL suite.** This plan touches the parser and the semantic analyzer —
  the fast subset is not a gate for that, and the record here is explicit: four codegen
  changes once gated green at 4593/2/1, then the first full run found 17 failures including 2
  real regressions already pushed.
- [ ] **Step 5:** THE PROOF — a real page. Hand-declare a small DOM surface, compile through
  `VisualGameStudio.Tests\bin\Release\net8.0\BasicLang.exe` (⛔ **not** `IDE\BasicLang.exe`,
  a stale copy), serve over HTTP (**not** `file://` — ES modules are CORS-gated and a
  `file://` origin is opaque), and confirm the page renders. This is the first time the DOM is
  reached with TYPE CHECKING rather than through an unchecked block.
- [ ] **Step 6:** Update this plan's status and commit.

---

## Risks

| Risk | Mitigation |
|---|---|
| **Emitting the extern type shadows the real runtime type** — a runtime failure from a green build. | Task 4, and it is the first thing tested. |
| **`ParseClass` must accept body-less members**, which it has never had to do. | Reuse the interface member parsers rather than writing a second one; they already carry the infinite-loop fix. |
| **A second body-less member parser drifts from the interface one.** | Stated explicitly in Task 1 Step 4. |
| **`Extern Class console` collides with the stdlib `Console`** because `IRModule.Classes` is case-INSENSITIVE, and Decision 2's exact-name rule hides it. | Task 7 / BL7011. |
| **Other backends silently accept it.** | Task 6 — the mirror test, which has found a real silent-drop every time it has been written on this backend. |
| **An extern type may be undeclarable-but-unobtainable** — declaring `Element` is useless if no expression can produce one. | Task 8 Step 1 flags this as a finding rather than assuming; if it bites, it earns its own task. |
| **The roster guard cannot see this fixture** (name prefix mismatch). | Task 8 Step 3 widens the predicate. |
| Parser/semantic changes are shared by all five backends. | Task 9 Step 4 — full suite, not the fast subset. |

---

## Deferred to plan 2c (with what recon already established)

- **`.bli` files** — ~15 extension sites. Canonical: `ModuleResolver.SupportedExtensions:15`,
  `ProjectFile.BasicLangSourceExtensions:81-82`,
  `VisualGameStudio.Core/Constants/FileExtensions.SourceExtensions:15`. Hardcoded literals
  found in recon: `TextMateService.cs:42`, `HighlightingLoader.cs:61` and `:120`,
  `LanguageFileTypes.cs:53` and `:161`, plus the RefactoringService/ViewModel lists the
  outline named. ⛔ **`Compiler.cs:467-468`** decides by extension whether a file may hold
  `Main` — a declaration-only file must not be an entry point, and the outline missed this.
- **`lib.dom.d.ts` → `.bli` generator** — a COPY, not a transform (Decision 2 makes the names
  exact by construction).
- **DOM auto-loading** — must be backend-gated (`project.Backend`) AND cover the single-file
  route, which has no `.blproj` and therefore no `TargetBackend`.
