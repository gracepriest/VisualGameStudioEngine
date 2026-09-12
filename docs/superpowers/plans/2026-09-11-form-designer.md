# Dual-target Visual Form Designer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A visual form designer in the Visual Game Studio IDE that produces both a browser page (JavaScript/DHTML backend) and a .NET WinForms desktop app (C# backend) from one `.blform` document.

**Architecture:** The document is the truth (`.blform` XML, structure-preserving writer). A `FormLowerer` **partitions** `.blform` out of the compile set inside `CompileProjectFiles` and **injects** generated BasicLang source back in — one seam, four build routes. Lowering targets BasicLang, never C# or JavaScript, so the five existing backends stay the only code that knows a target language. Generated code is a `Protected`-membered base class in `obj/gen/forms/` that the user's class `Inherits`.

**Tech Stack:** C# (BasicLang compiler + Avalonia IDE), NUnit, `System.Xml.Linq`, Avalonia 11.3.13 custom-draw (`Render(DrawingContext)`), .NET 8.

**Spec:** `docs/superpowers/specs/2026-09-11-form-designer-design.md` — the plan argues from the spec; executors read both. §2 (six corrections to the build prompt) changes two tasks and is not optional reading.

## Global Constraints

Every task's requirements implicitly include this section. Values are copied verbatim from the spec and from measured facts below.

- **Target framework:** `net8.0`. Avalonia **11.3.13**, Dock.Avalonia **11.3.12.1**. No WebView package, no `PropertyGrid`, no `ColorPicker`, no font dialog — Avalonia 11.3 base ships none of them.
- **Full-suite baseline:** 5826 tests, **4 known failures** (`docs/HANDOFF.md`). A run that does not match this is a regression until proven otherwise.
- **Generated type names never contain `_`.** The PascalCase heuristic at `SemanticAnalyzer.cs:2397` excludes any identifier containing an underscore, which turns `Me.Text` into a hard semantic error. Generated bases are `<Name>Base`, **never** `<Name>_Base`.
- **Generated base members are `Protected` or `Public`, never `Private`.** `PopulateSiblingClassMembers` (`SemanticAnalyzer.cs:448-478`) guards every arm with `when member.Access != AccessModifier.Private`, so a private member of a sibling base is invisible to the subclass.
- **Event wiring is `AddHandler <ctl>.<Event>, AddressOf <handler>`.** `Handles` is lexed (`BasicLangLexer.cs:192,540`) but **never parsed** — `Parser.cs` contains zero `TokenType.Handles`.
- **One statement per property.** There is no object-initializer and no `With` syntax.
- **`.blform` must never appear** in `ProjectFile.BasicLangSourceExtensions` or `ModuleResolver.SupportedExtensions`. It goes in `FileExtensions.SourceExtensions` only.
- **Both entry points, every time.** `BasicLang.exe build X.blproj` *and* the IDE `BuildService` path (`BuildService.cs:651`). A fix verified through only one still breaks the other.
- **stdout is the only valid oracle.** Every shipping route runs the IR optimizer; the unit-test helper does not. Validate through the real CLI or an optimizer-running helper.
- **"Passed!" does not mean the suite passed.** A crashed host still prints a per-assembly summary and sends the abort to stderr. Capture both streams and check the total against the baseline.
- **A missing backend switch arm does not fail — it silently builds C#.** Four dispatch maps have already defaulted that way. Grep every map keyed on a backend or solution type; make new defaults throw.
- **After AXAML changes, `dotnet clean` before building.** Stale build cache causes crashes.
- **Never round-trip repo files through PowerShell `Get-Content`/`Set-Content`** — it corrupts the BOM-less UTF-8 files here. Multi-line commit messages go through a file and `git commit -F`.
- **`IDE/` is a hand-committed xcopy drop.** Refresh with `robocopy <Shell bin> IDE /E` — **never `/MIR`**. Verify against the deployed binary (`IDE/BasicLang.exe new --list`), never timestamps.
- **Iterate with `--filter "TestCategory!=Integration"`** (~2 min). The full suite (~39 min) is the gate.

---

## ⛔ STATUS: NOT STARTED. Nothing below has been built or run.

**No task in this plan has been implemented, and no gate in it has been executed.** The environment this plan was written in has **no .NET SDK** (`dotnet: command not found`) — the same constraint that produced the unbuilt patch Task 1 exists to gate. Every "Expected:" line is a prediction, not a record.

**Owner sign-off: 4 of 5 answered; every question that shapes the design is closed.**

- **Q1 — ✅ yes, WinForms also.** Slice 3 (Tasks 18–20) un-gated; its catalog CI gate is mandatory infrastructure — the only correctness check that target has. Task 2 is on the critical path for the same reason.
- **Q2 — ✅ Grid/Flow persisted, free pixel-drag with snap resolution** (spec D2 + **D2a**). Adds Task 8a, which must precede Task 15.
- **Q3 — ✅ the designer subsumes page models 2 and 3.** Forced **D10a**; models 2 and 3 become acceptance obligations (Task 17).
- **Q4 — ✅ fix cross-file `Implements` in slice 0.** Task 3 un-gated; Tasks 1 and 3 gate **separately**.
- **Q5 — ⬜ open, and it blocks execution rather than design:** nobody has run a gate yet. Slice 0 needs an SDK and ~39 minutes per full-suite run, twice.

⚠ **Execution granularity.** Slices 0 and 1 (Tasks 1–8) are expanded to executable TDD steps with real code. **Slices 2 and 3 (Tasks 8a–20) are at design granularity** — files, interfaces, gates and the traps are specified, but their steps are not yet broken into write-test/run/implement/run/commit cycles with literal code. **Expand a slice-2+ task to that granularity before dispatching a subagent at it.** This is stated rather than hidden: a subagent handed a design-level step will improvise, and Task 11 and Task 19 are the two places where improvisation is most expensive.

---

## Measured facts

Every row was re-verified against `origin/master` at `d6b57b6` while writing the spec, on Linux in
a cloud session. **Re-run them rather than trusting them** — line numbers drift, and six rows here
contradict the build prompt (spec §2). A row marked ⚠ is one the prompt got wrong.

| # | Fact | How it was measured | Result |
|---|---|---|---|
| 1 | No partial classes anywhere | `grep -c Partial BasicLang/Parser.cs BasicLang/ASTNodes.cs BasicLang/SemanticAnalyzer.cs` | `0 / 0 / 0` |
| 2 | `Handles` lexed, never parsed | `grep -n "TokenType.Handles" BasicLang/Parser.cs` | no output; token at `BasicLangLexer.cs:192,540` |
| 3 | `Inherits` resolves via `_typeManager` only | read `SemanticAnalyzer.cs:4569` | opaque arm `:4573-4578`, hard error `:4582` |
| 4 ⚠ | `Symbol.Access` defaults to `Public` **twice** | read `SymbolTable.cs:76`; `AccessModifier` enum | `Access = AccessModifier.Public;` in ctor, **and** `Public` is the enum's zero value (`ASTNodes.cs:366-373`) |
| 5 ⚠ | Interfaces **do** pass the export filter | read `Compiler.cs:815-832` | first arm is `symbol.Access == Public`; only the `IsClassFile` short-circuit drops them |
| 6 | Cross-unit symbols reach `GlobalScope`, never `_typeManager` | read `SemanticAnalyzer.cs:287-328` | `ImportImplicitProjectSymbols` calls `GlobalScope.Define` only |
| 7 | Pending-sibling pass shells `ClassNode` only | read `SemanticAnalyzer.cs:349-368` | no `InterfaceNode` arm |
| 8 | A sibling base's `Private` members are invisible | read `SemanticAnalyzer.cs:448-478` | every arm guarded `when member.Access != AccessModifier.Private` |
| 9 | WinForms is un-armed for .NET resolution | read `Compiler.cs:145-146` | early `return` on `UseWindowsForms \|\| UseWpf` |
| 10 | WinForms types come from a heuristic | read `SemanticAnalyzer.cs:2397` | `char.IsUpper(name[0]) && !name.Contains('_')`, under "Be VERY permissive" |
| 11 | `CommonNetTypes` has zero WinForms names | `sed -n '203,232p' … \| grep -cE '"(Form\|Button\|Point\|Size\|Label\|TextBox)"'` | `0` |
| 12 ⚠ | `ProjectSerializer.Save` **destroys** unknown properties | read `ProjectSerializer.cs:245-273` | `new XDocument(...)` rebuild; emits 7 property elements |
| 13 ⚠ | `dom-core.bli` is 119 lines, with no positioning | `wc -l`; `grep -c "position\|zIndex"` | `119`; `0` |
| 14 | `GetSourceFiles` explicit branch is unfiltered | read `ProjectFile.cs` | bare `Directory.GetFiles(dir, filePattern)` |
| 15 ⚠ | **BL6014 is a C-family allowlist** — it cannot catch `.blform` | read `Compiler.cs:351-381` | filters on `CFamilySourceExtensions` only; loop at `:383` does `File.ReadAllText` on everything else |
| 16 | The seam reaches every build route | `grep -rn "CompileProjectFiles"` | `Compiler.cs:334`, called from `Program.cs:532`, `Compiler.cs:227`, `BuildService.cs:651`, `CppProjectBuilder` |
| 17 | The harness is never overwritten | read `JavaScriptEmitter.cs:105-115` | `⛔ NEVER overwrite the harness` |
| 18 ⚠ | The Shell's `WebView` is a **source viewer** | read `WebViewDocumentView.axaml` | its own banner: "full rendering requires a browser component (e.g., CefNet or WebView2)" |
| 19 | Dropping `"msil"` from `winforms-app` is safe | read `IProjectTemplateService.cs:350,368,432,461` | `console-app`, `game-app`, `class-library`, `unit-test` all carry it |
| 20 | One extension→`ItemType` decision point | read `ProjectService.cs:246` | `FileExtensions.IsSourceFile(path) ? Compile : Content` |
| 21 | `obj/` is already gitignored | `grep -n "\[Oo\]bj/" .gitignore` | `.gitignore:37` — the generated-base location needs no new rule |
| 22 | The prior lexer-pollution incident | read `LspMixedProjectTests.cs:13-40` | a `.cpp` from `GetSourceFiles()` **unfiltered** was "lexed/parsed AS BASICLANG", and "the pollution is invisible in diagnostics" |

### Commands used throughout

```powershell
# fast loop while iterating (~2 min) — skips the compile/run/spawn tests
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"

# THE GATE (~39 min). Capture BOTH streams: a crashed host still prints a per-assembly "Passed!"
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release 1> suite.out 2> suite.err

# the compiler alone, and the IDE
dotnet build BasicLang/BasicLang.csproj -c Release
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release   # dotnet clean first after AXAML

# the CLI — stdout is the only valid oracle, because every shipping route runs the IR optimizer
IDE/BasicLang.exe MyFile.bas --target=csharp
IDE/BasicLang.exe build MyProject.blproj
IDE/BasicLang.exe design --check LoginForm.blform     # Task 7
IDE/BasicLang.exe design --import MainForm.bas        # Task 16

# refresh the hand-committed xcopy drop — NEVER /MIR
robocopy VisualGameStudio.Shell/bin/Release/net8.0 IDE /E
IDE/BasicLang.exe new --list                          # verify the deployed binary, not timestamps
```

## File structure

New files this plan creates, in one place. Everything else is a modification to an existing file
named in its task.

```
VisualGameStudio.Core/Forms/
    FormDocument.cs  FormControl.cs  FormControlCatalog.cs    Task 5
    BlformReader.cs  BlformWriter.cs  Schema/                 Task 9
BasicLang/Forms/
    FormLowerer.cs                                            Task 11
    FormIR.cs  WebFormLowerer.cs                              Task 12
    WinFormsLowerer.cs                                        Task 18
VisualGameStudio.Editor/Controls/
    FormCanvasControl.cs                                      Task 8, writes in Task 15
VisualGameStudio.Shell/Views/Panels/
    PropertyGridView.axaml(.cs)  ToolboxView.axaml(.cs)       Task 14
VisualGameStudio.Tests/
    Forms/BlformAlgebraTests.cs                               Task 9
    Compiler/FormLoweringSeamTests.cs                         Task 11
    Forms/WebLoweringTests.cs                                 Task 12
    Forms/FormEventWiringTests.cs                             Task 13
    Forms/WinFormsLoweringTests.cs                            Task 18
    ProjectSerializerRoundTripTests.cs                        Task 2

obj/gen/forms/            generated .bas — gitignored (.gitignore:37), never a <Compile> item
```

## Risks

| Risk | Why it is real here | Mitigation |
|---|---|---|
| **The `Inherits` patch does not compile, or its tests do not discriminate.** | It was authored with no SDK and has never been built or run, in either direction. | Task 1 Step 2 runs the tests against *reverted* code first. Any test green on unpatched source gets rewritten, not accepted. |
| **A `.blform` reaches the BasicLang lexer.** | BL6014 is an allowlist (fact 15) and the parser recovers silently — this exact shape has already hit the repo twice (fact 22). | Tasks 10+11 in one push; the guard test asserts via the **symbol table**, not `result.Success`. |
| **The WinForms catalog is wrong and nothing notices.** | No type metadata at any layer (facts 9–11); member access degrades to `Object` with no diagnostic. | Task 19 generates every control with every property and requires the real CLI **and** `dotnet build` to exit 0. It is the type system. |
| **A backend dispatch map silently defaults to C#.** | Four maps have already done this. | Task 7 and Task 16 both grep every map keyed on backend or solution type; new defaults throw; extend `ProjectTemplateBackendMappingTests`. |
| **The snap rule gets improvised at the keyboard.** | Task 15 is where a designer feels good or bad, and the temptation is to tune it live. | Task 8a is a written, signed-off rule with no code, and Task 15 cannot start before it. |
| **Convention wiring creeps back in as a "helpful" second mechanism.** | It looks like a small kindness and double-fires every handler (spec D10a). | Task 13 Step 4 asserts a conventionally-named handler with no `<Bind>` is **not** wired and raises `BL8008`. |
| **A green suite that is not green.** | A crashed host prints a per-assembly "Passed!" and sends the abort to stderr. | Capture both streams; check the total against the recorded baseline (5826 / 4 known failures). |
| **The IDE and the CLI diverge.** | They are separate entry points into the same seam. | Every task that touches the build path asserts both, in that task, not later. |

---
## Slice 0 — prerequisites. Non-negotiable. (~2 days + two full suite runs)

Nothing in slice 1 depends on Tasks 2–4, but everything from slice 2 on does, and Task 1 gates the whole generated-base-class design. Do slice 0 first: it is the smallest set of changes that makes the ground under this feature true.

### Task 1: Gate the unbuilt cross-file `Inherits` patch

`11419b7` on branch `claude/modest-gauss-0ki0b7` carries a +22-line insert-only change to `SemanticAnalyzer.cs` and a 250-line `CrossFileInheritanceTests.cs`. Its own commit message says it plainly: **no .NET SDK was available; it has never been built, and not one of its tests has ever been executed, in either direction.** This task does not write the fix — it establishes whether the existing one is real.

**Files:**
- Verify: `BasicLang/SemanticAnalyzer.cs:4567-4589` (the inserted `GlobalScope` fallback)
- Verify: `VisualGameStudio.Tests/Compiler/CrossFileInheritanceTests.cs`

**Interfaces:**
- Consumes: nothing. First task.
- Produces: **cross-file `Inherits` resolves a sibling base to a `TypeInfo` with a populated `Members` dictionary.** Task 12 and Task 18 depend on this: a generated `<Name>Base` in `obj/gen/forms/` is a sibling unit, and without it every inherited member degrades to `Object` with no diagnostic.

- [ ] **Step 1: Bring the patch onto the working branch**

```bash
git cherry-pick 11419b7
```

- [ ] **Step 2: Build it. This has never been demonstrated to compile.**

Run: `dotnet build BasicLang/BasicLang.csproj -c Release`
Expected: build succeeds. If it does not, the patch is wrong on its face — fix the compile error before going further, and note that nothing about it was ever verified.

- [ ] **Step 3: Prove the tests discriminate — revert the fix, keep the tests**

```bash
git show 11419b7 -- BasicLang/SemanticAnalyzer.cs | git apply --reverse
dotnet build BasicLang/BasicLang.csproj -c Release
```

- [ ] **Step 4: Run the tests against unpatched code**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CrossFileInheritanceTests"`

Expected: every test FAILS **except** `Probe_CrossFileImplements_StillUnresolved`, which passes (it pins a defect this patch does not fix).

Expected failure reasons, read them — do not accept a red that fails for an unrelated reason:
- `CrossFileInherits_ResolvesSiblingBase` → `Unknown base class 'Greeter'`
- `CrossFileInherits_DerivedListedFirst_StillResolves` → same
- `WithUsingDirective_PrefersSiblingBase_OverOpaqueNetType` → **not** a failure to compile; it fails on the `Members` assertion, because the old behaviour was a *green build* with an empty `Members` dictionary
- `SelfInheritance_IsNotResolvedFromGlobalScope` → may pass before and after; if so it is not discriminating, and say so

⛔ **Any test green here is not discriminating and must be rewritten, not accepted.** `WithUsingDirective_PrefersSiblingBase_OverOpaqueNetType` is the one that matters most: a `Success`-only assertion would pass both before and after the fix and would prove nothing.

- [ ] **Step 5: Restore the fix**

```bash
git checkout BasicLang/SemanticAnalyzer.cs
dotnet build BasicLang/BasicLang.csproj -c Release
```

- [ ] **Step 6: Run the tests against patched code**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CrossFileInheritanceTests"`
Expected: ALL PASS, `Probe_CrossFileImplements_StillUnresolved` included.

- [ ] **Step 7: Confirm the narrowing did not become a closing**

`GenuineNetBase_StillResolvesOpaquely` must pass — `Inherits Form` keeps the opaque-.NET path. The fix **narrows** that arm; it must not close it, or every WinForms form in the repo stops compiling.

- [ ] **Step 8: THE GATE — full suite**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release 1> suite.out 2> suite.err
```

Expected: 5826 tests, 4 known failures. Check **both** streams and compare the total against the baseline — a crashed host still prints a per-assembly "Passed!".

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "test(semantic): gate the cross-file Inherits patch on a full suite run"
```

### Task 2: Make `ProjectSerializer` round-trip project properties (spec D14)

⚠ **Sharper than the build prompt states, and sharper than "Save rebuilds the document".** The IDE's model literally cannot hold these values: `VisualGameStudio.Core/Models/BasicLangProject.cs` has **no** `UseWindowsForms`, `UseWpf` or `TargetFramework` property, while the CLI's `BasicLang/ProjectSystem/ProjectFile.cs` reads all three (`:18`, `:85-86`, `:131`, `:151-155`). So an IDE load drops them on the floor and the save writes a document without them. A designer adds files → the IDE saves → the next `BasicLang.exe build` fails CS0246 on `Form`.

**Files:**
- Modify: `VisualGameStudio.Core/Models/BasicLangProject.cs` (add the three properties)
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` (`LoadAsync` ~`:36-50`, `SaveAsync` `:245-273`)
- Test: `VisualGameStudio.Tests/ProjectSystem/ProjectSerializerRoundTripTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `BasicLangProject.UseWindowsForms : bool`, `.UseWpf : bool`, `.TargetFramework : string` (default `"net8.0"`), and `.UnknownProperties : Dictionary<string,string>` preserving any `PropertyGroup` child the model does not model. Task 10 relies on `<Compile>` items surviving the same save.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.ProjectSystem;

/// <summary>
/// The IDE must not destroy project properties it does not itself model.
///
/// <para>Before this fix, BasicLangProject carried no UseWindowsForms/UseWpf/TargetFramework at
/// all, and ProjectSerializer.SaveAsync rebuilt the document from scratch with seven property
/// elements. So an IDE save DELETED them, and the next CLI build failed CS0246 on Form. The
/// invented property is the load-bearing case: fixing only the three known names leaves the
/// defect in place for the fourth one anyone adds.</para>
/// </summary>
[TestFixture]
public class ProjectSerializerRoundTripTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "BLProjRoundTrip_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private const string WinFormsProject = @"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>MyApp</ProjectName>
    <OutputType>Exe</OutputType>
    <RootNamespace>MyApp</RootNamespace>
    <TargetBackend>CSharp</TargetBackend>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <SomeFutureProperty>keep-me</SomeFutureProperty>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""MainForm.bas"" />
  </ItemGroup>
</BasicLangProject>";

    [Test]
    public async Task Save_PreservesWinFormsProperties_AndUnknownOnes()
    {
        var path = Path.Combine(_dir, "MyApp.blproj");
        File.WriteAllText(path, WinFormsProject);

        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        Assert.That(project.UseWindowsForms, Is.True, "UseWindowsForms must survive the load");
        Assert.That(project.TargetFramework, Is.EqualTo("net8.0-windows"));

        await serializer.SaveAsync(project);
        var reloaded = await serializer.LoadAsync(path);

        Assert.That(reloaded.UseWindowsForms, Is.True, "UseWindowsForms must survive a save");
        Assert.That(reloaded.TargetFramework, Is.EqualTo("net8.0-windows"));

        // The one that stops this recurring for the FOURTH property someone adds.
        var xml = File.ReadAllText(path);
        Assert.That(xml, Does.Contain("SomeFutureProperty"),
            "a PropertyGroup child the model does not model must be preserved verbatim");
        Assert.That(xml, Does.Contain("keep-me"));

        Assert.That(reloaded.Items.Exists(i => i.Include == "MainForm.bas"), Is.True,
            "Compile items must still round-trip");
    }
}
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectSerializerRoundTripTests"`
Expected: FAIL to **compile** — `BasicLangProject` has no `UseWindowsForms`. That compile error *is* the defect; record it.

- [ ] **Step 3: Add the properties to the model**

```csharp
// VisualGameStudio.Core/Models/BasicLangProject.cs
public bool UseWindowsForms { get; set; }
public bool UseWpf { get; set; }
public string TargetFramework { get; set; } = "net8.0";

/// <summary>
/// PropertyGroup children this model does not model, kept verbatim so a save never destroys
/// what a hand-edit or a future Claude Code version put there. Key = element name.
/// </summary>
public Dictionary<string, string> UnknownProperties { get; set; } = new();
```

- [ ] **Step 4: Read them in `LoadAsync`**

In the `PropertyGroup` loop (around `:37-50`), alongside the existing `OutputType` read:

```csharp
var tfm = propertyGroup.Element("TargetFramework")?.Value;
if (!string.IsNullOrEmpty(tfm)) project.TargetFramework = tfm;

if (bool.TryParse(propertyGroup.Element("UseWindowsForms")?.Value, out var uwf)) project.UseWindowsForms = uwf;
if (bool.TryParse(propertyGroup.Element("UseWPF")?.Value, out var uwp)) project.UseWpf = uwp;

// Anything this model does not model, kept so SaveAsync can put it back.
foreach (var el in propertyGroup.Elements())
{
    if (!KnownPropertyNames.Contains(el.Name.LocalName))
        project.UnknownProperties[el.Name.LocalName] = el.Value;
}
```

Add the companion set next to it:

```csharp
private static readonly HashSet<string> KnownPropertyNames = new()
{
    "ProjectName", "OutputType", "RootNamespace", "TargetBackend", "Language",
    "CppStandard", "CppToolchain", "TargetFramework", "UseWindowsForms", "UseWPF",
    "OutputPath", "DebugSymbols", "Optimize", "DefineConstants"
};
```

- [ ] **Step 5: Write them in `SaveAsync`**

In the global `PropertyGroup` literal at `:251-271`, after `CppToolchain`:

```csharp
new XElement("TargetFramework", project.TargetFramework),
project.UseWindowsForms ? new XElement("UseWindowsForms", "true") : null,
project.UseWpf ? new XElement("UseWPF", "true") : null
```

Then, after the `XDocument` is constructed and `root` is taken:

```csharp
// ⛔ Unknown properties are RE-ADDED, not merged in place: SaveAsync rebuilds the document
// from scratch, so anything not explicitly written here is destroyed.
var globalGroup = root.Element("PropertyGroup")!;
foreach (var (name, value) in project.UnknownProperties)
{
    if (globalGroup.Element(name) is null)
        globalGroup.Add(new XElement(name, value));
}
```

- [ ] **Step 6: Run the test and confirm it passes**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectSerializerRoundTripTests"`
Expected: PASS.

- [ ] **Step 7: Assert the CLI agrees — both entry points**

Build the saved project through the real CLI, not only the IDE model:

Run: `IDE/BasicLang.exe build <tmp>/MyApp.blproj`
Expected: no CS0246 on `Form`. Before this task, the post-save project fails exactly there.

- [ ] **Step 8: Gate and commit**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "TestCategory!=Integration"`

```bash
git add -A
git commit -m "fix(projectsystem): round-trip TargetFramework/UseWindowsForms and unknown properties"
```

### Task 3: Fix cross-file `Implements`

⚠ **This task's shape changed from the build prompt.** The prompt defers this because it "touches `Compiler.CollectExportedSymbols`, which is shared machinery". Spec §2.1 found that wrong: `AccessModifier.Public` is the enum's zero value (`ASTNodes.cs:366-373`) *and* is assigned explicitly in the `Symbol` constructor (`SymbolTable.cs:76`), while `Visit(InterfaceNode)` (`SemanticAnalyzer.cs:4712`) never sets `Access` — so every interface already satisfies the export filter's first arm and exports normally from a `.bas`. **`CollectExportedSymbols` is not touched.**

⛔ **Do not start until Task 1 has landed and passed its own full-suite gate.** Two unproven `SemanticAnalyzer` changes under one gate leave a red suite with two candidate causes.

**Files:**
- Modify: `BasicLang/SemanticAnalyzer.cs:349-368` (`RegisterPendingSiblingSignatures` pass 1 — shell `InterfaceNode`)
- Modify: `BasicLang/SemanticAnalyzer.cs` (the interface arm of `Visit(ClassNode)`, just below the base-class arm)
- Modify: `VisualGameStudio.Tests/Compiler/CrossFileInheritanceTests.cs` (flip the probe)
- **Do NOT modify:** `BasicLang/Compiler.cs` `CollectExportedSymbols`

**Interfaces:**
- Consumes: Task 1's `GlobalScope` fallback pattern in `Visit(ClassNode)` — this mirrors it for interfaces.
- Produces: cross-file `Implements` resolves. Not consumed by any later task in this plan; it is here because the owner chose it (Q4) and because a generated base may later want to implement a designer interface.

- [ ] **Step 1: Flip the pinning probe into a positive assertion**

`Probe_CrossFileImplements_StillUnresolved` exists to fail loudly when someone fixes this. Replace it:

```csharp
/// <summary>
/// Cross-file <c>Implements</c>. Broken by the same root cause as the base-class arm — cross-unit
/// symbols reach GlobalScope (ImportImplicitProjectSymbols) while Visit(ClassNode) queried
/// _typeManager, which holds only the current unit — plus one extra: pass 1 of
/// RegisterPendingSiblingSignatures shelled only ClassNode, so a PENDING sibling's interface was
/// never registered either. Unlike the base-class arm this has no opaque-.NET escape hatch, so it
/// was a hard error on every backend.
/// </summary>
[TestCase("javascript")]
[TestCase("csharp")]
public void CrossFileImplements_ResolvesSiblingInterface(string backend)
{
    var iface = Write("IGreeter.bas", @"
Interface IGreeter
    Sub Greet()
End Interface
");
    var impl = Write("Greeter.bas", @"
Class Greeter
    Implements IGreeter
    Public Sub Greet()
        Console.WriteLine(""hello"")
    End Sub
End Class

Sub Main()
    Dim g As New Greeter()
    g.Greet()
End Sub
");

    var result = For(backend).CompileProjectFiles(new[] { iface, impl });

    Assert.That(result.AllErrors.Select(e => e.Message),
        Has.None.Contains("Unknown interface"),
        $"[{backend}] the interface is declared in a sibling unit and must resolve from GlobalScope");
    Assert.That(result.Success, Is.True,
        $"[{backend}] errors: {string.Join(" | ", result.AllErrors.Select(e => e.Message))}");
}

/// <summary>
/// Order-independence, the pending-sibling half: with the interface listed SECOND it is a pending
/// unit when Greeter is visited, so it must come from RegisterPendingSiblingSignatures' pass-1
/// shell rather than from a completed unit's exports.
/// </summary>
[Test]
public void CrossFileImplements_InterfaceListedSecond_StillResolves()
{
    var iface = Write("IGreeter.bas", "Interface IGreeter\n    Sub Greet()\nEnd Interface\n");
    var impl = Write("Greeter.bas",
        "Class Greeter\n    Implements IGreeter\n    Public Sub Greet()\n    End Sub\nEnd Class\n\nSub Main()\nEnd Sub\n");

    var result = For("javascript").CompileProjectFiles(new[] { impl, iface });

    Assert.That(result.Success, Is.True,
        "errors: " + string.Join(" | ", result.AllErrors.Select(e => e.Message)));
}
```

- [ ] **Step 2: Run and confirm both fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CrossFileImplements"`
Expected: FAIL, `Unknown interface 'IGreeter'` on every backend. No opaque escape hatch means no green-build variant here.

- [ ] **Step 3: Shell `InterfaceNode` in pass 1**

In `RegisterPendingSiblingSignatures` (`:349-368`), the pass-1 loop currently handles only `ClassNode`. Add the interface arm beside it:

```csharp
foreach (var decl in EnumerateSiblingTopLevelDeclarations(unit))
{
    if (decl is ClassNode classNode)
    {
        var classType = RegisterSiblingClassShell(classNode, unit);
        if (classType != null) pendingClasses.Add((classNode, classType));
    }
    // ⛔ Pass 1 shelled ClassNode ONLY, so a pending sibling's interface was invisible even
    // after the GlobalScope lookup below. An interface shell needs no second pass: Implements
    // checks identity and kind, not members.
    else if (decl is InterfaceNode interfaceNode)
    {
        RegisterSiblingInterfaceShell(interfaceNode, unit);
    }
}
```

- [ ] **Step 4: Add the shell registrar**

Beside `RegisterSiblingClassShell`:

```csharp
/// <summary>
/// Register a pending sibling's interface in GlobalScope as a shell. Mirrors
/// RegisterSiblingClassShell; no member population pass is needed because Implements resolution
/// checks kind and identity only.
/// </summary>
private TypeInfo RegisterSiblingInterfaceShell(InterfaceNode node, CompilationUnit unit)
{
    if (GlobalScope.Resolve(node.Name) != null) return null;

    var type = new TypeInfo(node.Name, TypeKind.Interface);
    GlobalScope.Define(new Symbol(node.Name, SymbolKind.Interface, type, 0, 0)
    {
        IsImported = true,
        IsSiblingSignature = true,
        SourceModule = unit.ModuleName
    });
    return type;
}
```

- [ ] **Step 5: Consult `GlobalScope` in the interface arm**

In `Visit(ClassNode)`, the `foreach (var interfaceName in node.Interfaces)` loop resolves through `_typeManager.GetType(interfaceName)`. Add the fallback **before** the error, mirroring Task 1's base-class shape:

```csharp
var interfaceType = _typeManager.GetType(interfaceName);

// ⛔ Same root cause as the base-class arm above: _typeManager holds only THIS unit's
// declarations, while a sibling's interface reaches GlobalScope via
// ImportImplicitProjectSymbols (completed units) or RegisterPendingSiblingSignatures
// (pending ones). Unlike Inherits there is no opaque-.NET arm to fall through to, so a miss
// here is a hard error on every backend.
if (interfaceType == null)
{
    var sibling = GlobalScope.Resolve(interfaceName);
    if (sibling != null && sibling.Kind == SymbolKind.Interface && sibling.Type != null)
        interfaceType = sibling.Type;
}
```

- [ ] **Step 6: Run and confirm both pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CrossFileImplements"`
Expected: PASS on both backends and both orderings.

- [ ] **Step 7: Pin the residual gap rather than leaving it silent**

An interface declared in a **`.cls`/`.class`** file is still dropped, by the `unit.IsClassFile` short-circuit at `Compiler.cs:815-823` which exports only `SymbolKind.Class`. That one *would* need `CollectExportedSymbols`. Out of scope — record it:

```csharp
/// <summary>
/// KNOWN GAP, deliberately not fixed here. Compiler.CollectExportedSymbols short-circuits a
/// .cls/.class unit to SymbolKind.Class only (Compiler.cs:815-823), so an interface declared in a
/// class file never leaves it. Fixing that means widening shared export machinery, which this
/// task deliberately avoids. A designer interface lives in a .bas, so v1 is unaffected.
/// </summary>
[Test]
public void Probe_InterfaceInClassFile_StillUnexported()
{
    var iface = Write("IGreeter.cls", "Interface IGreeter\n    Sub Greet()\nEnd Interface\n");
    var impl = Write("Greeter.bas",
        "Class Greeter\n    Implements IGreeter\n    Public Sub Greet()\n    End Sub\nEnd Class\n\nSub Main()\nEnd Sub\n");

    var result = For("javascript").CompileProjectFiles(new[] { iface, impl });

    Assert.That(result.Success, Is.False,
        "if this now passes, the .cls export gap was fixed — delete this probe and say so");
}
```

- [ ] **Step 8: THE GATE — full suite, separately from Task 1**

```bash
dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release 1> suite.out 2> suite.err
```
Expected: 5826 tests, 4 known failures.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "fix(semantic): resolve a cross-file Implements interface from GlobalScope"
```

### Task 4: Define DPI behaviour and drop the dead MSIL route

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs:305-309`
- Modify: `VisualGameStudio.ProjectSystem/Services/BuildService.cs:1119-1124`
- Modify: `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs:386`
- Test: `VisualGameStudio.Tests/Services/ProjectTemplateBackendMappingTests.cs` (extend)

**Interfaces:**
- Consumes: nothing.
- Produces: both csproj generators emit `<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>`. Task 20 asserts the WinForms window scales predictably; without this the behaviour is undefined rather than wrong.

- [ ] **Step 1: Write the failing test**

```csharp
[Test]
public void BothCsprojGenerators_EmitHighDpiMode()
{
    // Two generators, one behaviour. A change to only one is the recurring shape of bugs here:
    // the IDE build path and the template path emit csproj independently.
    foreach (var generated in new[] { GenerateViaTemplateService(), GenerateViaBuildService() })
    {
        Assert.That(generated, Does.Contain("<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>"),
            "scaling behaviour must be defined, not left to the default");
    }
}

[Test]
public void WinFormsTemplate_DoesNotOfferMsil()
{
    var winforms = ProjectTemplates.All.Single(t => t.Id == "winforms-app");
    Assert.That(winforms.SupportedSolutionTypes, Does.Not.Contain("msil"),
        "the MSIL pipeline stops at a .il file — offering it is a dead end in the wizard");
}
```

- [ ] **Step 2: Run and confirm both fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectTemplateBackendMappingTests"`
Expected: FAIL on both.

- [ ] **Step 3: Emit the DPI property in both generators**

In `ProjectTemplateService.cs` beside `<UseWindowsForms>` at `:305`, and in `BuildService.cs` beside the same at `:1119`:

```csharp
sb.AppendLine("    <ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>");
```

- [ ] **Step 4: Drop `"msil"` from `winforms-app`**

`IProjectTemplateService.cs:386`:

```csharp
SupportedSolutionTypes = new List<string> { "dotnet" },
```

⚠ **Verified safe, not assumed:** `EverySolutionType_HasAtLeastOneTemplate` (`ProjectTemplateBackendMappingTests.cs:86-95`) still passes because `console-app` (`:350`), `game-app` (`:368`), `class-library` (`:432`) and `unit-test` (`:461`) all carry `"msil"`.

- [ ] **Step 5: Run and confirm they pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~ProjectTemplateBackendMappingTests"`
Expected: PASS, and `EverySolutionType_HasAtLeastOneTemplate` still green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(projectsystem): define PerMonitorV2 DPI and drop the dead MSIL WinForms route"
```

---

## Slice 1 — a read-only canvas over the two templates that already ship (~1 week to demo)

**Zero writes, zero new file type, zero project-system change, zero build change, zero shell refactor.** The whole slice is produced by a *reader*.

**Slice 1 must NOT:** widen `_openDocuments`, add a document type, add a dock region, add a file extension, touch `JavaScriptEmitter`, or write one byte into a user's file.

### Task 5: The form model

**Files:**
- Create: `VisualGameStudio.Core/Forms/FormDocument.cs`
- Create: `VisualGameStudio.Core/Forms/FormControl.cs`
- Create: `VisualGameStudio.Core/Forms/FormControlCatalog.cs`
- Test: `VisualGameStudio.Tests/Forms/FormModelTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces — **every later task depends on these exact names:**
  - `FormDocument { string Name; FormControl Root; List<FormControl> AllControls(); }`
  - `FormControl { string Name; string Kind; FormControl? Parent; List<FormControl> Children; Dictionary<string,PropertyValue> Properties; List<Facet> Facets; }`
  - `PropertyValue { string Raw; ReadState State; string? Reason; }`
  - `enum ReadState { Canon, Degraded, Refused }`
  - `Facet { string Target; FacetKind Kind; string Name; string Value; }` with `enum FacetKind { Literal, Expression, Property, Attribute, Raw }`
  - `FormControlCatalog.Kinds : IReadOnlyList<string>` — the 10 v1 kinds plus the three container kinds
  - `FormDocument.SerializeSubtree(FormControl) : string` and `FormDocument.DeserializeSubtree(string xml, string renameTo) : FormControl`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Forms;

namespace VisualGameStudio.Tests.Forms;

[TestFixture]
public class FormModelTests
{
    /// <summary>
    /// Degraded is PER PROPERTY, not per form (spec D11). One unparseable value freezes exactly
    /// one property-grid row and leaves every other row editable. A per-form flag would make a
    /// single bad attribute lock the whole designer, which is the behaviour this rejects.
    /// </summary>
    [Test]
    public void DegradedState_IsPerProperty_NotPerForm()
    {
        var ctl = new FormControl { Name = "btnLogin", Kind = "Button" };
        ctl.Properties["Text"] = new PropertyValue { Raw = "Sign in", State = ReadState.Canon };
        ctl.Properties["Width"] = new PropertyValue
        {
            Raw = "<<not a number>>", State = ReadState.Degraded, Reason = "not an integer"
        };

        Assert.That(ctl.Properties["Text"].State, Is.EqualTo(ReadState.Canon),
            "a bad Width must not freeze Text");
        Assert.That(ctl.Properties["Width"].Reason, Is.Not.Null.And.Not.Empty,
            "a Degraded row must carry a reason the property grid can show");
    }

    /// <summary>
    /// A facet aimed at the OTHER target is preserved byte-for-byte, never dropped (spec D3).
    /// This is the only mechanism that lets one document serve two vocabularies losslessly.
    /// </summary>
    [Test]
    public void ForeignTargetFacet_IsRetainedOnTheModel()
    {
        var ctl = new FormControl { Name = "btnLogin", Kind = "Button" };
        ctl.Facets.Add(new Facet
        {
            Target = "winforms", Kind = FacetKind.Property, Name = "FlatStyle", Value = "Flat"
        });

        var kept = ctl.Facets.Single(f => f.Target == "winforms");
        Assert.That(kept.Value, Is.EqualTo("Flat"));
    }

    /// <summary>
    /// Required in slice 1 even though no UI calls it (spec §8): nearly free alongside the model,
    /// very expensive to bolt on later. It is what Ctrl+C, duplicate and templating all become.
    /// </summary>
    [Test]
    public void SerializeSubtree_RoundTripsWithRename()
    {
        var doc = new FormDocument { Name = "LoginForm" };
        var panel = new FormControl { Name = "pnlMain", Kind = "Grid" };
        panel.Children.Add(new FormControl { Name = "btnLogin", Kind = "Button" });
        doc.Root = panel;

        var xml = doc.SerializeSubtree(panel);
        var clone = FormDocument.DeserializeSubtree(xml, renameTo: "pnlCopy");

        Assert.That(clone.Name, Is.EqualTo("pnlCopy"));
        Assert.That(clone.Children.Single().Name, Is.EqualTo("btnLogin"),
            "children keep their names; only the subtree root is renamed");
    }

    /// <summary>
    /// The catalog must model the three container kinds from day one (spec D2/D2a), because the
    /// recognizer in Task 6 has to produce a Canvas model — that is what BOTH shipped templates
    /// actually are.
    /// </summary>
    [Test]
    public void Catalog_CarriesTheThreeContainerKinds()
    {
        Assert.That(FormControlCatalog.Kinds, Contains.Item("Grid"));
        Assert.That(FormControlCatalog.Kinds, Contains.Item("Flow"));
        Assert.That(FormControlCatalog.Kinds, Contains.Item("Canvas"));
    }
}
```

- [ ] **Step 2: Run and confirm failure**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~FormModelTests"`
Expected: FAIL to compile — none of these types exist.

- [ ] **Step 3: Write the model**

```csharp
// VisualGameStudio.Core/Forms/FormControl.cs
namespace VisualGameStudio.Core.Forms;

public enum ReadState { Canon, Degraded, Refused }

public sealed class PropertyValue
{
    public string Raw { get; set; } = "";
    public ReadState State { get; set; } = ReadState.Canon;
    /// <summary>Why this ONE row is frozen. Shown in the property grid; null when Canon.</summary>
    public string? Reason { get; set; }
}

public enum FacetKind { Literal, Expression, Property, Attribute, Raw }

public sealed class Facet
{
    /// <summary>"web" or "winforms". A facet for the other target is preserved, greyed, never dropped.</summary>
    public string Target { get; set; } = "";
    public FacetKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class FormControl
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public FormControl? Parent { get; set; }
    public List<FormControl> Children { get; } = new();
    public Dictionary<string, PropertyValue> Properties { get; } = new();
    public List<Facet> Facets { get; } = new();
}
```

```csharp
// VisualGameStudio.Core/Forms/FormControlCatalog.cs
namespace VisualGameStudio.Core.Forms;

public static class FormControlCatalog
{
    /// <summary>
    /// v1 kinds. Grid/Flow/Canvas are containers (spec D2/D2a): Grid and Flow are the default
    /// vocabulary, Canvas is the explicitly-marked absolute escape hatch and the only kind whose
    /// children may carry X/Y and Anchor.
    /// </summary>
    public static readonly IReadOnlyList<string> Kinds = new[]
    {
        "Grid", "Flow", "Canvas",
        "Button", "Label", "TextBox", "CheckBox", "RadioButton",
        "ListBox", "ComboBox", "Panel", "PictureBox", "ProgressBar"
    };

    public static bool IsContainer(string kind) => kind is "Grid" or "Flow" or "Canvas";
}
```

`FormDocument.cs` carries `Name`, `Root`, `AllControls()` (depth-first), and the two subtree methods. `SerializeSubtree` writes the same element shape the Task 9 writer will read, so the two cannot drift.

- [ ] **Step 4: Run and confirm all four pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~FormModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(forms): form document model with per-property read state and facets"
```

### Task 6: The `InitializeComponent` recognizer, two dialects

A token-stream reader using a `SourceIndex` line/column→offset mapper. **No lexer change.**

**Files:**
- Create: `VisualGameStudio.Core/Forms/InitializeComponentRecognizer.cs`
- Create: `VisualGameStudio.Core/Forms/SourceIndex.cs`
- Test: `VisualGameStudio.Tests/Forms/RecognizerTests.cs` (create)

**Interfaces:**
- Consumes: Task 5's `FormDocument`, `FormControl`, `PropertyValue`, `ReadState`.
- Produces: `InitializeComponentRecognizer.Read(string source, RecognizerDialect dialect) : FormDocument` with `enum RecognizerDialect { WinForms, Dom }`. Task 7 (`design --check`) and Task 8 (canvas) both call this. Task 16 (`design --import`) reuses it unchanged.

**The WinForms grammar target is the shipped template verbatim** (`BasicLang.VisualStudio/src/BasicLang.VisualStudio/Templates/Projects/WinFormsApp/MainForm.bas`):

```basiclang
Me.Text = "$safeprojectname$"
Me.Size = New Size(400, 300)
Me.StartPosition = FormStartPosition.CenterScreen
lblMessage = New Label()
lblMessage.Text = "Hello, BasicLang!"
lblMessage.Location = New Point(20, 20)
Me.Controls.Add(lblMessage)
AddHandler btnClick.Click, AddressOf btnClick_Click
```

- [ ] **Step 1: Write the failing tests, fixtures first**

```csharp
[Test]
public void WinFormsDialect_RecoversTheShippedTemplate()
{
    var doc = InitializeComponentRecognizer.Read(ShippedMainForm, RecognizerDialect.WinForms);

    Assert.That(doc.Name, Is.EqualTo("MainForm"));
    Assert.That(doc.Root.Kind, Is.EqualTo("Canvas"),
        "absolute Location/Size is a Canvas in the model — this is what the template IS");

    var label = doc.AllControls().Single(c => c.Name == "lblMessage");
    Assert.That(label.Kind, Is.EqualTo("Label"));
    Assert.That(label.Properties["X"].Raw, Is.EqualTo("20"), "New Point(20, 20) fans out to X/Y");
    Assert.That(label.Properties["Y"].Raw, Is.EqualTo("20"));
    Assert.That(label.Properties["Text"].Raw, Is.EqualTo("Hello, BasicLang!"));
}

/// <summary>
/// An unrecognized STATEMENT degrades exactly one property and leaves the rest readable.
/// An unrecognized CONTROL KIND is Refused — the file is never written.
/// </summary>
[Test]
public void UnrecognizedStatement_DegradesOnePropertyOnly()
{
    var doc = InitializeComponentRecognizer.Read(@"
Public Class MainForm
    Inherits Form
    Private btn As Button
    Private Sub InitializeComponent()
        btn = New Button()
        btn.Text = ""OK""
        btn.Size = ComputeSize(3 * 7)
    End Sub
End Class", RecognizerDialect.WinForms);

    var btn = doc.AllControls().Single(c => c.Name == "btn");
    Assert.That(btn.Properties["Text"].State, Is.EqualTo(ReadState.Canon));
    Assert.That(btn.Properties["Width"].State, Is.EqualTo(ReadState.Degraded));
    Assert.That(btn.Properties["Width"].Reason, Does.Contain("ComputeSize"));
}

[Test]
public void EventWiring_IsRecovered()
{
    var doc = InitializeComponentRecognizer.Read(ShippedMainForm, RecognizerDialect.WinForms);
    var btn = doc.AllControls().Single(c => c.Name == "btnClick");
    Assert.That(btn.Properties.ContainsKey("@Click"), Is.True,
        "AddHandler btnClick.Click, AddressOf btnClick_Click");
    Assert.That(btn.Properties["@Click"].Raw, Is.EqualTo("btnClick_Click"));
}
```

- [ ] **Step 2: Run and confirm failure.** Expected: FAIL to compile.

- [ ] **Step 3: Implement `SourceIndex`** — a line/column→offset mapper built once per source string, so every diagnostic can carry `file(line,col)` without re-scanning.

- [ ] **Step 4: Implement the recognizer.** Statement patterns per dialect; anything unmatched sets that one property `Degraded` with the offending text as the reason; an unknown control kind sets the document `Refused`.

- [ ] **Step 5: Run and confirm all three pass.**

- [ ] **Step 6: Assert against the second shipped template** (the `web-site` one) in the `Dom` dialect, so both dialects are exercised by files that already build.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(forms): InitializeComponent recognizer for the WinForms and DOM dialects"
```

### Task 7: `BasicLang.exe design --check <file>`

Headless, scriptable, useful in CI on day one — and it proves the grammar before a single pixel exists.

**Files:**
- Modify: `BasicLang/Program.cs` (add the `design` verb)
- Test: `VisualGameStudio.Tests/Forms/DesignCheckCliTests.cs` (create)

**Interfaces:**
- Consumes: Task 6's `InitializeComponentRecognizer.Read`.
- Produces: the `design` verb with a `--check <file>` switch. Exit 0 clean, **exit 1 on refusal**. Task 16 adds `--import` to the same verb.

- [ ] **Step 1: Write the failing test** — a refused file exits 1 and names `BL8002` with `file(line,col)`; a clean file exits 0 and prints nothing to stderr.

- [ ] **Step 2: Run and confirm failure.** Expected: unknown verb.

- [ ] **Step 3: ⚠ Before adding the verb, grep every backend/solution-type dispatch map.**

```bash
grep -rn "switch.*[Bb]ackend\|case \"csharp\"\|case \"javascript\"" --include="*.cs" BasicLang/ | grep -v /obj/
```

A missing arm does not fail — it silently builds C#. Four maps have already defaulted that way. Make any new default **throw**.

- [ ] **Step 4: Implement the verb.**

- [ ] **Step 5: Run the real CLI, not just the test helper.**

Run: `IDE/BasicLang.exe design --check <fixture>`
Expected: exit 1, `BL8002` on stderr with `file(line,col)`. **stdout is the only valid oracle.**

- [ ] **Step 6: Extend `ProjectTemplateBackendMappingTests`** so a future backend cannot be added without an arm here.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(cli): design --check validates a form before any UI exists"
```

### Task 8: The canvas

**Files:**
- Create: `VisualGameStudio.Editor/Controls/FormCanvasControl.cs`
- Modify: `VisualGameStudio.Shell/ViewModels/Documents/CodeEditorDocumentViewModel.cs` (a `Design | Code` segmented toggle bound to the **same** `TextDocument`)

**Interfaces:**
- Consumes: Task 6's recognizer, Task 5's model.
- Produces: `FormCanvasControl : Control` with `FormDocument? Document` and `FormControl? Selection`. Task 15 adds the write path to this same control — it adds no new control.

- [ ] **Step 1: `FormCanvasControl : Control` overriding `Render(DrawingContext)`**, following `MinimapControl.axaml.cs:417`. Six margin controls in `VisualGameStudio.Editor/Margins/` are the same pattern.

- [ ] **Step 2: Schematic draw, selection, hit-testing.** **Schematic, not WYSIWYG** — the IDE has no rendering browser (spec §2.4: the `WebViewDocumentView` that exists is an HTML *source* viewer by its own banner) and no property grid or toolbox. Design for that honestly rather than apologising for it.

- [ ] **Step 3: Render the model recovered from the persisted artifact**, never from an in-memory delta (spec D11), so a writer/reader disagreement surfaces in one tick instead of as slow corruption.

- [ ] **Step 4: ⚠ `dotnet clean` before building** — AXAML plus a stale cache crashes.

⛔ **No drag in slice 1.** Selection and hit-testing only. Dragging a control *is* a write, and slice 1's whole contract is zero writes; the gesture arrives in Task 15, behind Task 8a. The temptation is real, because a canvas that highlights on click feels one small step from a canvas that moves things. It is not — the step is the entire snap-resolution design.

**The demo:** open the shipped `winforms-app` template and the shipped `web-site` template — files that already exist and already build — and see the form.

- [ ] **Step 5: Gate.** Full suite if anything reached `VisualGameStudio.Editor`; otherwise the fast subset plus a manual `IDE/VisualGameStudio.exe` run.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(editor): read-only schematic form canvas with selection"
```

---

## Slice 2 — the web designer writes (the first shipping designer)

Ordered so **the first writing, shipping, owner-facing designer is the web one** — it has the owner
mandate, `dom-core.bli` is machine-readable ground truth a catalog can be pinned against, and F5
already reaches a real renderer (`MainWindowViewModel.cs:4168`, `_webPreviewServer.Start(...)`).

⚠ **Tasks 10 and 11 must land in the same push.** Between them there is a window in which a
`.blform` listed as `<Compile>` is handed to the BasicLang lexer. Do not split them across commits,
and do not leave that window open overnight.

### Task 8a: Design the snap-resolution rule (no code)

The owner's Q2 answer (spec D2a) is only real if this is decided before a drag gesture exists.
Output is a short spec section appended to the design doc, not source.

**Interfaces:**
- Consumes: spec D2a's five sub-decisions.
- Produces: a written rule appended to the spec, and the signature Task 15 implements against —
  `SnapResolver.Resolve(Point dropPoint, FormControl container) : Placement`, where
  `Placement { int Col; int Row; Alignment HAlign; Alignment VAlign; bool ExtendsGrid; string? RefusalReason; }`.
  No code in this task; the signature is the deliverable's shape.

- [ ] **Step 1:** Which cell wins when a drop straddles a boundary. **Recommendation: the pointer's
      own position**, not the control's centroid — the pointer is what the user is looking at.
- [ ] **Step 2:** What a drop *outside every existing cell* does — extend the grid (add a row or
      column) or refuse with a visible reason. ⚠ **Silently clamping into the nearest existing cell
      is what users report as "it moved my button somewhere else".** This is the sub-decision that
      ships badly if it is improvised.
- [ ] **Step 3:** Within-cell placement — stretch, or an alignment (start/center/end) picked from
      where in the cell the drop landed. Stretch is the default only for a single occupant.
- [ ] **Step 4:** Undo restores the prior **constraint**, not the prior pixels, and the undo entry
      names the constraint.
- [ ] **Step 5:** `<Canvas>` children keep the raw, unresolved gesture — drag sets `X`/`Y` and
      snaplines behave exactly as a WinForms user expects. This is where the fidelity argument is
      honoured in full.
- [ ] **Step 6:** State the invariant explicitly: **the rule is UI behaviour, not persistence.** It
      must never produce a document the property grid could not have produced, so it inherits D11's
      `Read∘Apply == Apply∘Read` obligation.

**Gate:** owner or reviewer sign-off on the written rule. No build.

### Task 9: `.blform` schema and the structure-preserving writer

**Files:**
- Create: `VisualGameStudio.Core/Forms/BlformReader.cs`, `BlformWriter.cs`
- Create: `VisualGameStudio.Core/Forms/Schema/` (element and attribute names as constants)
- Test: `VisualGameStudio.Tests/Forms/BlformAlgebraTests.cs`

**Interfaces:**
- Consumes: Task 5's `FormDocument`, `FormControl`, `PropertyValue`, `ReadState`, `Facet`.
- Produces: `BlformReader.Read(string path) : FormDocument` (retains unknown elements, attributes and
  comments on the model), `BlformWriter.Write(FormDocument doc, string path) : void` (deterministic
  attribute order, children in z-order), and `BlformSchema` name constants. Tasks 10, 11, 15 and 16
  all call these two methods and nothing else.

- [ ] **Step 1: Write the algebra tests first, property-based over generated documents** — not three
      hand-written cases. Three laws: a no-op patch writes **nothing** (assert bytes, not a dirty
      flag); round-trip is **byte-identical**; `Read∘Apply == Apply∘Read`.
- [ ] **Step 2:** Reader on `XDocument.Load` with `LoadOptions.SetLineInfo` so every diagnostic
      carries `file(line,col)`. Unknown elements, unknown attributes and comments are retained on
      the model, not discarded.
- [ ] **Step 3:** Writer with deterministic attribute order and children in z-order.
- [ ] **Step 4:** Reserve the four day-one sections even though v1 writes none of them:
      `<Components>`, `<Resources>`, `<Bind Property= Source= Path=>`, and an explicit `TabIndex`
      on every control (spec D1). Each is an hour now and a format break later.
- [ ] **Step 5:** `BL8001` (not well-formed), `BL8002` (unknown control kind → Refused, file never
      written), `BL8003` (unparseable value → that **one property** Degraded), `BL8007` (duplicate
      id).
- [ ] **Step 6:** Wire the reader into `design --check` from Task 7 so the CLI validates `.blform`
      as well as recovered source.

**Gate:** `--filter "TestCategory!=Integration"`. ⛔ **A failing algebra law is a hard stop, not a
known issue** — every later task assumes these three hold.

### Task 10: `.blform` enters the project system

**Files:**
- Modify: `VisualGameStudio.Core/Constants/FileExtensions.cs` (add `BasicLangForm = ".blform"` and
  put it in `SourceExtensions`)
- Verify unchanged: `ProjectFile.BasicLangSourceExtensions`, `ModuleResolver.SupportedExtensions` —
  `.blform` must **not** appear in either
- Modify: `VisualGameStudio.ProjectSystem/Services/ProjectService.cs:246` — the single
  extension→`ProjectItemType` decision point, so an added `.blform` becomes `Compile`, not `Content`

**Interfaces:**
- Consumes: Task 9's reader/writer.
- Produces: `FileExtensions.BasicLangForm = ".blform"`, present in `FileExtensions.SourceExtensions`
  and **absent** from `ProjectFile.BasicLangSourceExtensions` and `ModuleResolver.SupportedExtensions`.
  `ProjectService.cs:246` therefore classifies an added `.blform` as `ProjectItemType.Compile`.
  Task 11 depends on `.blform` reaching the compile set — that is the whole point, and the trap.

- [ ] **Step 1:** Add the extension and confirm `<Compile Include="LoginForm.blform" />` round-trips
      through `ProjectSerializer` (it already does — verified spec §4 — so this is a regression
      test, not new code).
- [ ] **Step 2:** ⚠ **Assert the two exclusions.** A test that fails if `.blform` ever appears in
      `ProjectFile.BasicLangSourceExtensions` or `ModuleResolver.SupportedExtensions`. Both are
      "obviously wrong to add" right up until someone adds them to fix a different bug.
- [ ] **Step 3:** Solution Explorer shows `.blform` with its own icon and opens it in the designer.
- [ ] **Step 4:** ⛔ **Go straight to Task 11 in the same push.**

**Gate:** `--filter "TestCategory!=Integration"` + `SolutionExplorerViewModelTests`.

### Task 11: The `FormLowerer` seam — partition, then inject

Spec §2.2 and D5. **This is the task the build prompt got wrong**, so read §2.2 before starting.

**Files:**
- Modify: `BasicLang/Compiler.cs` — the BL6014 guard region (`:351-381`), after the
  `files.Count == 0` check and **before** the registration loop at `:383`
- Create: `BasicLang/Forms/FormLowerer.cs`
- Test: `VisualGameStudio.Tests/Compiler/FormLoweringSeamTests.cs`

**Interfaces:**
- Consumes: Task 9's `BlformReader`, Task 10's extension registration, Task 1's working cross-file
  `Inherits` (the generated base is a sibling unit).
- Produces: `FormLowerer.Partition(List<string> files) : (List<string> sources, List<string> forms)`
  and `FormLowerer.Lower(IEnumerable<string> formPaths, string objGenDir) : IEnumerable<string>`
  returning generated `.bas` paths. Called from `CompileProjectFiles` only. Tasks 12 and 18 supply
  the per-target lowering this dispatches to.

- [ ] **Step 1: Write the failing guard test first** — a project with a `.blform` `<Compile>` item
      compiles, and the `.blform` **never reaches the lexer**. ⚠ **Assert via the symbol table /
      module registry directly.** `LspMixedProjectTests.cs:13-40` records why: the error-recovering
      parser does not throw, it registers a junk module under the file's basename, and *"the
      pollution is invisible in diagnostics"*. A test that only checks `result.Success` passes while
      the symbol table is corrupt.
- [ ] **Step 2:** Confirm BL6014 does **not** catch it today — `CFamilySourceExtensions` is an
      allowlist. Run the test and watch XML reach the lexer. That is the bug, reproduced.
- [ ] **Step 3:** Partition `.blform` out of `files`; hand them to the lowerer; inject the generated
      `.bas` back in. One seam, four build routes (`Program.cs:532`, `Compiler.cs:227`,
      `BuildService.cs:651`, `CppProjectBuilder`).
- [ ] **Step 4:** Generated sources land in `obj/gen/forms/` — gitignored already (`.gitignore:37`,
      `[Oo]bj/`), never a `<Compile>` item, never in Solution Explorer (spec D4).
- [ ] **Step 5: Both entry points.** `BasicLang.exe build X.blproj` **and** the IDE `BuildService`
      path. Assert both, in this task, not later.

**Gate: FULL SUITE.** This edits `Compiler.CompileProjectFiles`, which every build route runs
through.

### Task 12: FormIR and the web lowerer

**Files:**
- Create: `BasicLang/Forms/FormIR.cs`, `BasicLang/Forms/WebFormLowerer.cs`
- Test: `VisualGameStudio.Tests/Forms/WebLoweringTests.cs`

**Interfaces:**
- Consumes: Task 11's `FormLowerer` dispatch, Task 5's model.
- Produces: `FormIR` (the single intermediate both targets lower from) and
  `WebFormLowerer.Lower(FormDocument doc) : LoweredForm`, where
  `LoweredForm { string Html; string Css; string BasSource; }`. Task 13 extends `BasSource` with
  event wiring; Task 18 implements the same `Lower` shape for WinForms off the same `FormIR`.

- [ ] **Step 1:** `FormIR` as the single intermediate both targets lower from (spec D6). ⚠ **Lower
      to BasicLang source, never to JavaScript.** Emitting `.js` directly would bypass source maps,
      `JsCapabilityChecker`, semantic type-checking, and the ability for handler bodies to call
      generated declarations with real types.
- [ ] **Step 2:** Emit three artifacts per form: an HTML fragment with stable ids, a `.css`, and a
      `.g.bas` that touches only `getElementById` and `addEventListener` (both already declared —
      `dom-core.bli:24` and `:57`). **Zero `.bli` additions.**
- [ ] **Step 3: Layout is CSS text, not typed member access.** `dom-core.bli` has no
      `position`/`left`/`top`/`zIndex` (verified: `grep -c` returns 0). Geometry **fans out** on the
      web — `X="96" Y="80"` becomes two independent declarations (spec D7).
- [ ] **Step 4:** Generated markup **always overwrites**, shipped from an asset root, so
      `JavaScriptEmitter.cs:105-115`'s never-overwrite guard keeps protecting only a hand-authored
      `index.html` that never entered that root. Test both halves: generated markup is replaced on
      rebuild; a hand-authored `index.html` is not.
- [ ] **Step 5:** One `.js` per project (spec D8). `<body data-form="LoginForm">`, `Main()`
      dispatching on `doc.body.getAttribute("data-form")`. ⛔ **Do not add multi-entry-point support
      to `JavaScriptBackend` — that is a backend change, not a designer change.**
- [ ] **Step 5a: Ownership marking — `runat`-style (spec D9). THIS IS HOW PAGE MODEL 2 IS
      DELIVERED**, and the owner's Q3 answer makes it an acceptance obligation rather than a
      courtesy. Literal markup passes through **untouched**; only marked elements become
      designer-owned objects. Two rules, both testable, both asserted in Task 17:
      **(a)** marking is **opt-in** — a page with zero designer-owned elements must still build and
      still support code-behind, or model 2 is reachable only by first adopting the designer;
      **(b)** unowned markup is preserved **byte-for-byte** across a designer save. A near-miss on
      (b) is silent data loss in someone's hand-written HTML. It also gives the Task 8 canvas a
      principled way to show non-owned content as read-only instead of clobbering it.

- [ ] **Step 6:** Generated base class members are `Protected`, never `Private`
      (`SemanticAnalyzer.cs:448-478` — a private member of a sibling base is invisible to the
      subclass), and the type is `<Name>Base`, never `<Name>_Base` (the PascalCase heuristic at
      `:2397` excludes identifiers containing `_`).

**Gate:** the real CLI. **stdout is the only valid oracle** — every shipping route runs the IR
optimizer and the unit-test helper does not.

### Task 13: Events — `<Bind>` wires, convention names

Spec D10 and **D10a**, which exists because the owner's Q3 answer made auto-wiring the designer's
job.

**Files:**
- Modify: `BasicLang/Forms/WebFormLowerer.cs` (DOM thunk), `FormIR.cs`
- Test: `VisualGameStudio.Tests/Forms/FormEventWiringTests.cs`

**Interfaces:**
- Consumes: Task 12's `FormIR` and `LoweredForm`.
- Produces: `FormEvent { string ControlName; string EventName; string HandlerName; }` on `FormIR`,
  per-target thunk emission, and diagnostics `BL8005` (bound handler missing — an **error**) and
  `BL8008` (conventionally-named handler exists but is not bound — informational). Task 14's
  double-click writes the `<Bind>` that this reads.

- [ ] **Step 1:** `FormEvent` plus a per-target thunk — an inline lambda on DOM, a private
      `(sender, EventArgs)` sub on WinForms. This dodges the `Action` vs `Action(Of DomEvent)` arity
      wall and BL7007's ban on `EventArgs` together.
- [ ] **Step 2:** Wiring is `AddHandler <ctl>.<Event>, AddressOf <handler>` — `Handles` is lexed but
      **never parsed** (`Parser.cs` has zero `TokenType.Handles`), so it is not available.
- [ ] **Step 3: A missing handler is an error** (`BL8005`), not a warning. The analyzer has no
      override validation, so a renamed or deleted handler otherwise yields a green build with a
      wired, dead button.
- [ ] **Step 4: Exactly one wiring mechanism** (D10a). `<Bind>` wires; the naming convention only
      *suggests* the name. ⚠ **Test that a conventionally-named handler with no `<Bind>` is NOT
      wired** and raises `BL8008`. That assertion is what stops convention wiring creeping back in
      as a "helpful" second mechanism and double-firing every handler.

**Gate:** `--filter "TestCategory!=Integration"` + a CLI run proving a real button fires once.

### Task 14: The property grid and toolbox

**Files:**
- Create: `VisualGameStudio.Shell/Views/Panels/PropertyGridView.axaml(.cs)`, `ToolboxView.axaml(.cs)`
  and their view models
- Modify: `VisualGameStudio.Shell/Dock/DockFactory.cs` (two new dock regions)

**Interfaces:**
- Consumes: Task 5's model (`PropertyValue.State`/`Reason` drive the frozen rows, `Facet.Target`
  drives the greyed ones), Task 9's writer.
- Produces: `PropertyGridViewModel` and `ToolboxViewModel`, plus
  `FormEditingService.CreateHandler(FormControl ctl, string eventName) : void` — the ONE atomic edit
  that writes both the `Sub` and the `<Bind>` (spec D10a).

- [ ] **Step 1:** Both are hand-built. Avalonia 11.3 base ships **no** `PropertyGrid`, **no**
      `ColorPicker` and **no** font dialog — price every type editor as bespoke.
- [ ] **Step 2:** Edits commit on **focus-loss/Enter**, not per keystroke (spec D13) — otherwise
      typing "Sign in" is eight undo entries.
- [ ] **Step 3:** Degraded rows are **per property** (spec D11): one unparseable value freezes
      exactly one row, shows its reason, and leaves every other row editable.
- [ ] **Step 4:** Foreign-target facets render greyed with a reason and are **never dropped on save**
      (spec D3) — the round-trip test from Task 9 covers the persistence half; this is the UI half.
- [ ] **Step 5: Double-click-to-create-handler is ONE atomic edit** (D10a): it writes
      `Sub btnSave_Click()` **and** the matching `<Bind>` together. ⚠ Shipping the sub without the
      bind looks like model 3 working and is a dead button.
- [ ] **Step 6:** ⚠ **`dotnet clean` before building** — AXAML plus a stale cache crashes.

**Gate:** full suite (this reaches `VisualGameStudio.Shell`) + a manual `IDE/VisualGameStudio.exe`
run.

### Task 15: The canvas writes — drag with snap resolution

This is where slice 1's read-only canvas gains a write path, and where Task 8a's rule is
implemented. Not before.

**Files:**
- Modify: `VisualGameStudio.Editor/Controls/FormCanvasControl.cs`

**Interfaces:**
- Consumes: Task 8a's `SnapResolver.Resolve`/`Placement` signature, Task 8's `FormCanvasControl`,
  Task 9's writer.
- Produces: the implemented `SnapResolver`, and drag/drop/delete/reorder on the existing
  `FormCanvasControl`. Adds no new control — it is the write path on the slice-1 one.

- [ ] **Step 1:** Drag, drop from toolbox, delete, reorder — each resolved through Task 8a's rule.
- [ ] **Step 2:** Snaplines while dragging, and the status-bar readout naming the constraint the drop
      will write ("col 1, row 0 — stretch") **before** the mouse is released.
- [ ] **Step 3:** Undo/redo restores the prior **constraint**, not the prior pixels.
- [ ] **Step 4: The canvas keeps rendering the model recovered from the persisted artifact**, never
      from the in-memory delta (spec D11), so a writer/reader disagreement surfaces within one tick
      instead of as slow corruption.
- [ ] **Step 5:** Re-run Task 9's algebra tests against documents produced by dragging, not only by
      the property grid. The gesture must not be able to produce a document the grid could not.

**Gate:** full suite + manual IDE run.

### Task 16: `design --import` — the recognizer as an importer (spec D12)

**Files:**
- Modify: `BasicLang/Program.cs` (the `design` verb)

**Interfaces:**
- Consumes: Task 6's `InitializeComponentRecognizer.Read` unchanged, Task 9's `BlformWriter.Write`.
- Produces: `design --import <file>` on the Task 7 verb, emitting a `.blform` plus a trimmed
  code-behind. Refuses rather than guesses.

- [ ] **Step 1:** Convert a hand-written form (Task 6's recognizer output) into a `.blform` plus a
      trimmed code-behind. This is the migration path for existing forms, including the shipped VSIX
      template.
- [ ] **Step 2:** Refuses rather than guesses — anything the recognizer marks Refused stops the
      import with a named diagnostic and writes nothing.
- [ ] **Step 3:** ⚠ **Grep every backend/solution-type dispatch map again** before adding the
      subcommand. A missing switch arm does not fail, it silently builds C#; four maps have already
      defaulted that way.

**Gate:** the real CLI, against both shipped templates.

### Task 17: Web end-to-end — the shipping milestone

**Interfaces:**
- Consumes: every slice-2 task.
- Produces: the page-model 2 and 3 acceptance tests (spec §7, §10 Q3). These are the only place
  those staged capabilities get proven, since the designer subsumes them.

- [ ] **Step 1:** New project → design a form on the canvas → F5 → the form renders in the system
      browser and a button fires exactly once.
- [ ] **Step 2:** **Model 2 acceptance** (spec §7, §10 Q3): a page with **zero** designer-owned
      elements still builds and still supports code-behind; a mixed page's unowned markup is
      **byte-identical** after a designer save. A near-miss on the second is silent data loss in
      someone's hand-written HTML.
- [ ] **Step 3:** **Model 3 acceptance:** double-click produces a conventionally-named handler that
      is **actually wired** — assert the `<Bind>` was written, not merely that the sub exists.
      Asserting the sub alone would pass under the convention-only design that D10a rejected, and so
      proves nothing.

**Gate: FULL SUITE**, and this is the point at which the web designer is claimable as shipped.

---

## Slice 3 — WinForms

Confirmed in scope by the owner (spec §10 Q1). Everything here rests on Task 1 and Task 2 having
landed, and on the fact that **this target has no type metadata at any layer**.

### Task 18: The WinForms lowerer

**Files:**
- Create: `BasicLang/Forms/WinFormsLowerer.cs`
- Test: `VisualGameStudio.Tests/Forms/WinFormsLoweringTests.cs`

**Interfaces:**
- Consumes: Task 12's `FormIR`, Task 11's dispatch, Task 2's project-property round-trip (without it
  the built project loses `<UseWindowsForms>` and fails CS0246 on `Form`).
- Produces: `WinFormsLowerer.Lower(FormDocument doc) : LoweredForm` with `Html`/`Css` empty and
  `BasSource` matching the shipped template's shape. Task 19 generates against this.

- [ ] **Step 1:** Match the shipped template's shape exactly
      (`BasicLang.VisualStudio/.../WinFormsApp/MainForm.bas`) — it is already swept by
      `TemplateBuildSweepTests` and `BuildServicePipelineTests.Build_WinFormsTemplate_DotNet_Builds`.
- [ ] **Step 2: Geometry fans IN here** (spec D7). `X="96" Y="80"` is **one** statement —
      `btnLogin.Location = New Point(96, 80)`. ⚠ It cannot be emitted as two: `Location` returns a
      `Point` **struct**, so `btnLogin.Location.X = 96` is CS1612 — and BasicLang will not catch it,
      because WinForms types come from the PascalCase heuristic and member access degrades to
      `Object` with no diagnostic. It compiles clean until `csc`.
- [ ] **Step 3:** One statement per property — there is no object-initializer or `With` syntax
      (`Parser.cs` `New` parses a type reference and optional positional args, then returns).
- [ ] **Step 4:** Grid/Flow containers lower to `TableLayoutPanel`/`FlowLayoutPanel`; `<Canvas>`
      children lower to `Location`/`Size` with `Anchor`.

**Gate:** the real CLI **and** `dotnet build` on the emitted project — `csc` is the only thing that
actually type-checks this target.

### Task 19: The catalog CI gate — the stand-in for the type system

⛔ **This is not a nicety. It is the only correctness check WinForms has.** `EnableNetResolution`
returns early for `UseWindowsForms` (`Compiler.cs:145-146`), the resolver closure is
`Microsoft.NETCore.App` only, and `CommonNetTypes` contains **zero** WinForms names — so the catalog
is otherwise unfalsifiable hand-written data.

**Interfaces:**
- Consumes: Task 18's `WinFormsLowerer`, Task 5's `FormControlCatalog.Kinds`.
- Produces: the generated every-control-every-property project and the CI gate that requires the real
  CLI **and** `dotnet build` to exit 0. There is no other correctness check for this target.

- [ ] **Step 1:** Generate a project containing **every catalog control with every property set**.
- [ ] **Step 2:** Require the **real CLI** to exit 0, then `dotnet build` to exit 0.
- [ ] **Step 3:** Run it on every catalog change. A control added without passing this gate is a
      control nobody has established exists.
- [ ] **Step 4:** ⚠ Assert no generated identifier contains `_` — the heuristic excludes such names
      and `Me.Text` becomes a hard semantic error.

**Gate:** the gate *is* the test. Wire it into the suite as `[Category("Integration")]`.

### Task 20: WinForms end-to-end

**Interfaces:**
- Consumes: Tasks 18 and 19, plus Task 2 end-to-end.
- Produces: the shipping proof for the WinForms half.

- [ ] **Step 1:** New WinForms project → design a form → build → run → a window appears and a button
      fires once.
- [ ] **Step 2:** Confirm Task 2's fix holds end-to-end: add a file through the IDE (which triggers a
      project save), then build **from the CLI**. Before Task 2 this fails CS0246 on `Form`.
- [ ] **Step 3:** `design --import` on the shipped VSIX template produces a `.blform` that builds to
      the same window.

**Gate: FULL SUITE.**

---

## Definition of done

The feature is shippable when all of these hold at once:

- [ ] Slice 0's four tasks landed, each full-suite gated where it touches `SemanticAnalyzer`.
- [ ] Task 9's three algebra laws hold — no-op writes nothing, round-trip byte-identical,
      `Read∘Apply == Apply∘Read` — against documents produced by **both** the property grid and the
      drag gesture.
- [ ] A `.blform` never reaches the BasicLang lexer, asserted via the symbol table.
- [ ] Both entry points build every form: `BasicLang.exe build X.blproj` and the IDE.
- [ ] Page model 2 and model 3 acceptance tests pass (Task 17) — they are the only place those
      staged capabilities get proven.
- [ ] The WinForms catalog gate passes with every control and every property.
- [ ] `IDE/` refreshed with `robocopy <Shell bin> IDE /E` — **never `/MIR`** — and verified against
      the deployed binary (`IDE/BasicLang.exe new --list`), not timestamps.
- [ ] The full suite matches the recorded baseline, with stdout **and stderr** captured and the
      total checked against the expected count.

---
## Standing house rules

Moved to **Global Constraints** at the top of this plan, where an executor reads them before
picking up a task rather than after finishing one.
