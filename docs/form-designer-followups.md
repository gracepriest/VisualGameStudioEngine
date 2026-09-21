# Form designer — follow-ups to file separately

**Task 19 of `docs/superpowers/plans/2026-09-11-visual-form-designer.md`** asks for these to be
filed as chips rather than fixed in that plan. Each one is written so it can be filed verbatim: what
it is, where it lives, and the evidence.

⚠ Everything marked **measured 2026-09-13** was reproduced in this repo on Linux with .NET 8.0.131,
using the real CLI and, where the output is C#, `csc` against the WinForms reference assemblies.
Dates on the other entries are the plan's own.

---

## From the plan's own list

### 1. Cross-file `Inherits` / `Implements` are both broken
`SemanticAnalyzer.cs:4569` and `:4598`, same cause. *Unknown base class* / *Unknown interface*,
exit 1. Out of the designer plan by owner decision 1. The spec's *Cross-file resolution* section
carries the diagnosis and the fix shape: route through `ResolveTypeName`, add the `InterfaceNode`
arm, and do **not** touch `CollectExportedSymbols`.

⚠ Affects any user with a multi-file class hierarchy — not a designer-only problem.

### 2. `With … End With` silently drops its assignments
`IRBuilder.cs:3356` and `:2866`; `With` over a .NET receiver at `SemanticAnalyzer.cs:6939`. The IR
builder's assignment dispatch has no `ImplicitWithMemberNode` arm and no final else, so every
`.Prop = value` inside a `With` block is discarded — **with a green build**.

⛔ This is why the designer's region writer must never emit `With`. That rule is enforced by a
comment in `RegionWriter.AppendControlInit`; nothing in the compiler stops anyone else.

### 3. Four JavaScript-backend defects, each from a green build
Measured 2026-09-11:
- a lambda calling an unqualified method of the enclosing class emits a **bare identifier** →
  `ReferenceError` at run time;
- `Me.Method()` inside a lambda hard-errors — class members are not populated
  (*"Available members: btn, .ctor0"*);
- a **qualified module call** emits a reference to a container JS does not have → `ReferenceError`
  — ⛔ **see entry 14**, which is this same defect, reproduced and measured, after it shipped in
  the designer's own dispatch three days after this line was written;
- **chained access through a declared `Property`** loses its type
  (`doc.body.getAttribute(…)` → `Object`).

The first and third are runtime failures from clean builds — the highest-severity shape this repo
tracks. The fourth is why `FormAssetEmitter.DispatchSource` reads `data-form` in two steps.

### 4. `AddressOf` to a later-declared `Sub` erases parameter types
`DelegateTypeOf`, `SemanticAnalyzer.cs:7503-7518`.

✅ **Independently reproduced, and the boundary is now exact** (measured 2026-09-13):

| Shape, handler declared AFTER the wiring | Result |
|---|---|
| Web: `addEventListener("click", AddressOf H)` | **FAILS** — *cannot convert from `Action(Of Object)` to `Action(Of DomEvent)`* |
| WinForms: `AddHandler btn.Click, AddressOf H` | **Compiles**, through BasicLang *and* csc, binding with full parameter types |
| Module-level `Sub` into a declared `Action(Of Integer)` | **Compiles** |

So the erasure is real but only bites where the call site declares the delegate's parameter type.
The designer's `BL8013` check was narrowed to the web target on that evidence; the underlying
compiler behaviour is unchanged and still worth fixing.

### 5. `Overrides` is never validated
Zero `Overrid` matches in `SemanticAnalyzer.cs`.

### 6. `ClearIncludedFiles` has zero call sites
`Preprocessor.cs:568`.

### ⛔ Do NOT file: the C++ `Friend`-field gap
`IRBuilder.MapAccessModifier` (`:948-957`) collapses `Friend` to `Private` before any `IRField` is
built, so it is unreachable and the chip would not reproduce.

---

## Found while building the designer — measured 2026-09-13

### 7. A call on an assignment's right-hand side is emitted TWICE (C# backend)
```basic
pic.Image = Image.FromFile("logo.png")
```
emits:
```csharp
Image.FromFile("logo.png");                    // ← the call, again
pic.Image = Image.FromFile("logo.png");
```
Clean build; `csc` accepts it. **The call runs twice**, so anything with side effects happens twice
— here the file is opened twice and an `Image` is leaked. Task 17's catalog sweep does not catch it,
and correctly so: that gate asks whether the output COMPILES, not what it does.

### 8. Multi-flag `Anchor` cannot be expressed in BasicLang at all
**✅ RESOLVED 2026-09-18** — the premise below was incomplete: csc had never been asked, and
`CType(7, AnchorStyles)` was refused by `SemanticAnalyzer.RejectImpossibleConversion`, not by the
parser. One arm of that check now exempts scalar → *unresolvable* .NET type, and multi-edge anchors
are emitted (`FormAnchorEmissionTests`). Kept for the measurements; the decision is closed.

`Anchor="Left,Top,Right"` — an ordinary WinForms thing. Three routes, all refused:

| Attempt | Result |
|---|---|
| `AnchorStyles.Left Or AnchorStyles.Top` | *Logical operator 'Or' requires Boolean operands* |
| `CType(7, AnchorStyles)` | *Cannot convert 'Integer' to 'AnchorStyles': no such conversion exists* — the enum is an unresolvable .NET type |
| `AnchorStyles.Left \| AnchorStyles.Top` | `\|` **lexes** (`TokenType.BitwiseOr`, `BasicLangLexer.cs:754`) but the parser never consumes it: *Unexpected token in expression* |

The designer refuses such a document (`BL8015`) rather than emitting one flag (geometry the running
program will not reproduce) or all of them (does not compile).

**This is an open product decision**, not just a bug: teach the parser a bitwise `Or`/`|` — which
also needs the semantic analyzer to stop demanding Boolean operands for an unresolvable enum type,
and carries a full-suite blast radius — or keep single-edge anchors plus `Dock`.

### 9. `BasicLangLexer` emits a `BitwiseOr` token the parser never consumes
`BasicLangLexer.cs:754` produces `TokenType.BitwiseOr` for `|`; no parser path accepts it, so every
use is *Unexpected token in expression*. Either wire it up (see 8) or stop lexing it, because a
token that only ever produces a parse error is worse than no token.

### 10. `Avalonia.Controls.ColorPicker` 11.3.13 IS available
The designer plan prices colour editors as hand-built on the grounds that "Avalonia 11.3 base ships
no ColorPicker". True of the base package — but the first-party `Avalonia.Controls.ColorPicker`
package restores fine, and `IDE/Avalonia.Controls.ColorPicker.dll` is already in the committed drop.

The property grid's colour rows are text for now. Adopting the real control needs its theme included
in `App.axaml`, and a missing style include renders a blank control — which is not something a
headless run or a build will tell you. **Do this on a machine where you can see it.**

### 11. `FormClipboard` is complete and nothing calls it
`FormClipboard.SerializeSubtree` / `DeserializeSubtree` (`FormDocument.cs`) do the whole job —
target-matched fragments, id collision renaming, handler retargeting by convention — and the only
callers are tests. The canvas has no Copy/Cut/Paste commands, so a user cannot reach any of it.

This is the same shape as the two dead generators fixed in `49a9bda` (`RegionWriter.Write` and
`DispatchSource`), and it is the one that is left. Wiring it is a canvas feature — keyboard bindings,
a selection-to-fragment path, a paste-at-cursor offset rule — not a fix, so it is filed rather than
bolted on. **Until it is wired, treat the clipboard tests as design notes, not as coverage of
anything a user can do.**

### 12. D7 describes a dispatch shape that does not build
The spec says the dispatch helper is "the only generated **top-level** name". A top-level `Sub` in
one `.bas` is **not callable from another** — measured 2026-09-14 with two hand-written files and a
real `BasicLang build`:

```
Sub Helper() ... End Sub     ' Helper.bas
Sub Main()  Helper()  End Sub ' Main.bas
→ JavaScript backend: no lowering for 'Helper.Helper'. It is neither declared by this
  program nor supported by JavaScriptStdLib.
```

Nothing in that message names the real cause, and it is not specific to generated code — two
hand-written files do it too.

⛔ **This entry first said the fix was `Public Module VgsForms`. That was wrong**, and the correction
is entry 14 below: a module COMPILES and then throws `ReferenceError` at run time, because the
backend flattens its members to bare globals while emitting the call site qualified. The generator
emits `Public Class` + `Public Shared Sub`, which is the only shape measured to both compile and run
across files on this backend.

**Two consequences to decide on:** the spec's D7 text and `BL8031`'s reserved purpose both now refer
to a shared method on a class rather than a top-level name; and the cross-file top-level `Sub`
limitation is a **compiler** gap worth its own investigation — it is not a form-designer problem and
it will bite anyone splitting procedural code across files on the JavaScript backend.

### 13. Nothing makes `Main()` call the dispatch for you
`BL8018` warns when a project has form pages and no source calls `VgsForms.VgsDispatchForm()`, which
is the honest minimum: the backend emits exactly one invocation, for `Main`, so an uncalled helper
is a page that loads a script and does nothing.

But a warning is not the same as it working. The options are to have **File → New Form** offer to
insert the call into the project's `Main()` the first time a form is added, or to make the web
project template ship a `Main()` that already dispatches. Both edit the user's code, which is why
neither was done unilaterally.

### 14. ⛔⛔ The JavaScript backend emits calls to module objects it never defines
**This is a runtime failure from a clean, green build**, and it is a COMPILER bug.

⛔⛔ **File this as the same issue as the third bullet of entry 3, not as a new one.** That bullet
— *"a qualified module call emits a reference to a container JS does not have → `ReferenceError`"*,
measured **2026-09-11** — is this exact defect, written down in this exact document three days
before the designer's dispatch was generated as a module and shipped broken. Nobody read it,
including the person who wrote it. The entry below is the reproduction and the blast radius that
bullet never had; treat it as evidence attached to a known bug, and treat the near-miss as the
reason a one-line entry in a long list is not the same as a filed issue.

#### Minimal reproduction — 8 lines, ONE file, no form designer

```basic
Public Module M
    Public Sub Go()
        Console.WriteLine("go")
    End Sub
End Module

Sub Main()
    M.Go()
End Sub
```

Builds clean. Emits:

```js
function Go() { console.log("go"); return; }   // the module is FLATTENED to a bare global
function Main() { M.Go(); return; }            // but the call site stays QUALIFIED
Main();
```

`M` is defined nowhere. Running it: **`ReferenceError: M is not defined`.**

#### ⛔ It is NOT cross-file only — the blast radius is wider than first written

Measured 2026-09-14, all three with a real `BasicLang build` and the output executed under node:

| Shape | Result |
|---|---|
| `M.Go()` from **another file** | compiles → **`ReferenceError` at run time** |
| `M.Go()` in the **SAME file** | compiles → **identical broken output**, same `ReferenceError` |
| `Go()` unqualified, same file | ✅ emits `Go()` and runs correctly |

So the trigger is **qualifying a module member at all**, not crossing a file boundary. Anyone who
writes `MyModule.Helper()` — ordinary, correct BasicLang, and the shape most people reach for
precisely because it is explicit — gets a clean build and a dead program.

#### Root cause

`IRBuilder.Visit(ModuleNode)` (`BasicLang/IRBuilder.cs:385`) treats a module as purely
organizational: *"Modules are organizational - process members"*. It visits the members directly, so
each `Sub` becomes a **top-level `IRFunction`** carrying only a `ModuleName` string — there is no
container in the IR for the backend to emit. The call site, meanwhile, keeps the qualifier. Nothing
downstream reconciles the two.

Two candidate fixes, neither attempted here:
- **Backend-only:** when lowering a qualified call whose receiver matches a known module name and
  whose member is a top-level function with that `ModuleName`, emit the bare name. Surgical, and
  only touches calls that are broken today.
- **Emit the container:** after the top-level functions, emit `const M = { Go };` per module. Makes
  the qualified form real, but introduces a global that can collide with a class or variable.

#### ⛔⛔ CHECKED, 2026-09-14: all three backends are broken, in DIFFERENT directions

Same eight-line program, each backend generated and then actually COMPILED and RUN:

| Backend | `M.Go()` qualified | `Go()` unqualified |
|---|---|---|
| **JavaScript** | ❌ builds clean, **`ReferenceError: M is not defined` at RUN time** | ✅ works |
| **C#** | ✅ works — emits `public static class M`, runs | ❌ **`CS0103: The name 'Go' does not exist in the current context`** |
| **C++** | ❌ clang: **`error: use of undeclared identifier 'M'`** | ✅ compiles and runs |

⛔⛔ **There is no spelling of a module-member call that works on all three backends.** JavaScript
and C++ require the unqualified form; C# requires the qualified one. Any program that calls a module
member is therefore locked to a subset of targets, silently — and the language is VB-like, where
module members are supposed to be reachable BOTH ways.

Two distinct root causes, one shared origin:

- **JavaScript and C++** hoist module members to bare top-level functions (`IRBuilder.Visit(ModuleNode)`)
  and then emit the call site qualified anyway. JS ships the broken program; C++ is caught by clang,
  so it cannot ship — but the error names a generated identifier the user never wrote.
- **C#** does the opposite and gets the container right: it groups members into a per-module
  `public static class`. Its bug is the UNQUALIFIED call, which it emits bare from a sibling class
  that cannot see the member.

⚠ C++ additionally binds the call's result (`void* t0 = {}; t0 = M.Go();`) for a `Sub` that returns
nothing. That is a symptom of the qualified path being lowered as an instance call, not a separate
defect — the unqualified C++ output has no such binding and compiles clean.

✅ gracepriest/VisualGameStudioEngine#6 fixes the JavaScript half, and after it JS is the only backend on which
**both** spellings work. **C# and C++ are untouched by that PR** and each still needs its own fix:
C++ the same container-or-rewrite decision as JS, C# the reverse one (resolve an unqualified call to
the module class that declares it).

#### Why it was not fixed on the form-designer branch

The designer works around it by generating `Public Class VgsForms` with a `Public Shared Sub`, which
emits a real `class VgsForms { static ... }`. That is a generator-side workaround: **the underlying
bug is untouched.** Fixing it means changing call lowering for every JavaScript program, which does
not belong stacked onto a 37-commit feature branch awaiting review — it wants its own branch, its
own PR, and its own full-suite gate.

⚠ Related and also unfixed: a bare top-level `Sub` in one `.bas` cannot be called from another at
all (*"no lowering for `Helper.Helper`"*) — follow-up 12. **These are two DIFFERENT paths**: that one
fails at COMPILE time through `JavaScriptBackend.CallTarget`'s dotted-name arm
(`JavaScriptBackend.cs:1296`), this one slips past it and fails at RUN time. Between them, a class is
the only shape that works across files on this backend.

### 15. The default source glob walked `bin/` and `obj/`
`ProjectFile.GetSourceFiles()`'s glob branch recursed the whole project directory with no build-output
exclusion, while `GetCppTranslationUnits()` two methods below has always excluded them. Once the build
started writing a generated `VgsFormDispatch.g.bas` into `obj/`, the **second** build of a glob-shaped
project swept its own output back in and compiled the file twice — from a build that reported success.

Fixed here by giving the glob the same two guards the C++ one documents (build-output exclusion and
an exact-extension check). **Worth knowing that this was latent for every generated source, not just
this one** — anything a future build step writes under `obj/` would have been compiled on the next run.

⚠ **The actionable residue is a WINDOWS-ONLY defect that predates this branch and is still
unverified.** Win32 globbing over-matches three-character patterns, so `*.bas` matches `.basic` and
`*.cls` matches `.class`: every glob-shaped project has been yielding — and compiling — those files
**twice**. The exact-extension check fixes it, but it cannot be reproduced on Linux, so nobody has
watched it happen. **Confirm it on the Windows run**, and if it reproduces, this is worth filing on
its own account rather than as a footnote to the form designer.

⚠ Its mirror, `ProjectGlobSafety.MaterialiseGlobbedSources`, needed the same guard and did not get
it for one commit — see the fourth-pass notes in `docs/HANDOFF.md`. The two are mirrored by
construction and not shared; anything added to one belongs in the other.


### 16. A CSS track function with a comma cannot be expressed in `Cols`/`Rows` at all

⛔ **Pre-existing, ships today, and independent of the designer canvas.** `<Layout Cols="…">` is a
**comma-separated** list, and `FormAssetEmitter.Tracks` renders it by splitting on those commas and
rejoining with spaces:

```csharp
private static string Tracks(string commaSeparated) =>
    string.Join(" ", commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
```

So every CSS track function that takes arguments — `minmax(100px, 1fr)`, `repeat(2, 1fr)`,
`clamp(…)`, `fit-content(…)` — is **torn in half**. `Cols="minmax(100px, 1fr),1fr"` reaches the page
as `grid-template-columns: minmax(100px 1fr) 1fr`, which is invalid CSS: the browser drops the whole
declaration and the page falls back to a single implicit column. No diagnostic anywhere, and the
`.blwebform` round-trips perfectly — the damage is only in the emitted stylesheet.

**Measured, not assumed:** `FormGridLayout` on the canvas splits on exactly the same commas, on
purpose, so the schematic shows the same torn tracks the page will get rather than a grid the page
does not have. `ATrackFunctionContainingACommaSplitsApart_MatchingWhatTheEmitterDoes` pins that
agreement and names this entry.

**The fix is a format decision, which is why it is filed rather than made.** Three options, in
increasing cost:

| Option | Cost | Loses |
|---|---|---|
| Split on commas **not inside parentheses** | A dozen lines in `Tracks` and `ParseTracks`, both of which must change together (they are mirrored by construction) | Nothing — `Cols="minmax(100px, 1fr),1fr"` starts working |
| Store the track list **verbatim** and emit it unchanged | Simplest emitter, but the attribute is then CSS rather than a list | The reader/writer can no longer count tracks without a CSS parser — and the canvas needs that count to draw cells |
| Separate tracks with **spaces**, not commas | Matches CSS exactly | Breaks every existing `.blwebform`, including the shipped template |

⚠ Whichever is chosen, **`FormAssetEmitter.Tracks` and `FormGridLayout.ParseTracks` must change in
the same commit.** They are a mirrored pair by design: the canvas draws the grid the emitter
produces, and a canvas that split differently would put the cells somewhere the page does not have
them — with nothing on screen looking wrong.

### 17. There is no "View Code" for a form document

**Add ▸ New Form** writes the pair — `LoginForm.blform` and `LoginForm.bas` — and opens the
DOCUMENT, because the design view is a mode on the document's own editor and opening the
code-behind instead is how "I can't see the form designer" happened. Both files are listed in the
project and both are visible in the tree, so nothing is lost; but the IDE has no command that
crosses between them the way `F7` / *View Code* does in Visual Studio, and no command that goes back
the other way from a `.bas` to its form.

Raising `FileOpenRequested` twice is NOT the fix: `MainWindowViewModel.OnFileOpenRequested` is
`async void`, so two raises race on their file reads and whichever finishes last takes the active
tab. The designer would be the front tab only by luck.

**Shape of the fix:** a command on the document tab (and the tree's context menu) that opens the
sibling, found by swapping the extension — `.blform`/`.blwebform` ↔ `.bas` — and only offered when
the sibling exists. Small, and it wants a caller test that drives the command and an AXAML guard
for the binding, like every other UI seam on this branch.

### 18. A retarget can only carry a kind's DEFAULT event across — found 2026-09-19

`FormControlDef` names one measured event per target per kind (`WinFormsEvent` / `WebEvent`), and
that is the whole cross-target event vocabulary. `FormRetarget` therefore maps `Click` ⇄ `click`,
`TextChanged` ⇄ `input` and so on through the catalog, and DROPS any other bind with a `BL8026`
naming the handler — `MouseEnter` on a Button, say, which a human would map to `mouseenter`
without thinking.

Carrying the WinForms spelling into the web document instead was measured as the worse failure:
`addEventListener("MouseEnter", …)` registers cleanly and never fires, and nothing in the page says
so. Dropped-and-named is honest; mapped would be better.

**Shape of the fix:** a per-kind event table on the catalog row — `Events: { (WinForms:"MouseEnter",
Web:"mouseenter"), … }` — driven through the same csc / node gates as the default event
(`WinFormsCatalogSweepTests.TheDefaultEvent_…`, `FormRetargetPairTests`), because an event name is
exactly as unfalsifiable as a property name and by the same mechanism. That table is also what an
Events tab in the property grid would read, so the two features want the same row.

### 19. Extender properties: a ToolTip's text and an ErrorProvider's error live on OTHER controls — found 2026-09-19

The tray (Task 25) holds a `ToolTip` and an `ErrorProvider` with their OWN properties
(`InitialDelay`, `BlinkStyle`, …), which is what VS gives you before you set a tooltip on anything.
What VS gives you next — "ToolTip on toolTip1" and "Error on errorProvider1" as rows on EVERY
control, emitted as `toolTip1.SetToolTip(btn, "…")` / `errorProvider1.SetError(btn, "…")` — has no
shape in `FormPropertyDef`: every property is emitted as `{Id}.{Name} = value` on its own control.
Measured 2026-09-19: `SetToolTip` before `Show()` works and `GetToolTip` returns it (spec M2), and
on the web the honest equivalent of a tooltip is the `title` attribute, settable through
`Element.setAttribute` (M9) — so the extender is the one place a ToolTip HAS a web form.

**Shape of the fix:** a `FormPropertyDef` facet naming the component it emits AGAINST (`Extender:
"ToolTip"`, `ExtenderMethod: "SetToolTip"`, `HtmlAttribute: "title"`), a row the property grid adds
to every control while a matching component exists, and a csc sweep row that emits it. Until then
the two components are present, inert, and callable from code.

### 20. Two compiler gaps user code meets the moment it touches a component — measured 2026-09-19

1. **`Container` is not known to implement `IContainer`.** VS's idiom `Private components As
   System.ComponentModel.IContainer` / `components = New System.ComponentModel.Container()` is
   refused: *Cannot assign value of type 'Container' to 'IContainer'* — qualified or with the
   `Using`, both spellings (spec M5). A field typed `Container` compiles and runs (M14). The tray
   emits parameterless constructors and no container, which leaks nothing observable in a form's
   lifetime; the analyzer's assignability check for resolved .NET types is the gap.
2. **A component's extender value cannot be read back into a typed variable.** `Dim s As String =
   tip.GetToolTip(btn)` and `Return err.GetError(btn)` from a `Function … As String` are refused —
   *Cannot assign value of type 'Object' to variable of type 'String'* (M15) — because the WinForms
   member types as `Object` (the same degradation that makes a catalog row unfalsifiable without
   csc). Declaring the variable `As Object` compiles. Neither is a designer defect, and both will be
   blamed on the tray.

### 21. `FormClipboard.FromElement` accepts a component nested in a container — latent, unreachable

Raised by the 2026-09-19 review and refuted as unreachable, recorded so it is not re-found:
`FormDocument.cs` guards only the component-as-PARENT direction, so a hand-built
`<Panel><Timer/></Panel>` fragment fed to `DeserializeSubtree` builds a Timer into the Panel's
children, which the writer would then emit with `TabIndex` and `p.Controls.Add(t)` and the reader
would refuse on the next open (BL8020). No product path produces that fragment: the only writer of
the designer clipboard is `SerializeSubtree` over `Selection.Controls`, and every writer of
`Children` — the reader, `FormPlacement.Place`, `MoveToForm`, `Clone` — is closed to a component. If
the clipboard ever reads the OS clipboard (spec: it does not, by design), mirror the reader's
BL8020 check in `FromElement` first.

### 22. `IROptimizer.CloneInstruction` has no `IRArrayAlloc` arm — found building commit 24a, PRE-EXISTING

`IROptimizer.cs:2642-2689` has clone arms for IRAssignment/IRBinaryOp/IRUnaryOp/IRStore/IRLoad and
`default: return inst;` (`:2687-2688`). An unrolled loop body that carries an array literal reuses
the SAME `IRArrayAlloc` INSTANCE N times, so the JavaScript backend's new `Visit(IRArrayAlloc) =>
Bind(...)` emits `const t0 = new Array(N);` N times in one scope — a SyntaxError — where the C#
backend instead emits CS0128 (a redeclared local). **Aggressive pipeline only, and PRE-EXISTING**:
before commit 24a the JS side threw `NotYet` for the same shape, so 24a newly EXPOSES this rather
than causing it.

The honest fix is larger than adding clone arms: `CloneVariable` (`:2692-2699`) already renames
`total` to `_u0_total`, a name NEITHER backend declares, so that pass already mangles any unrolled
loop with a local assignment — adding an `IRArrayAlloc` arm to `CloneInstruction` alone would still
collide with that renaming gap.

Repro: aggressive pipeline, under node —
```basic
For i = 0 To 7
    Dim a() As Integer = New Integer() {i, i}
    s = s + a(1)
Next
```
expecting `28`.

### 23. The C# backend is CS0103 for an array literal inside a `When` guard, including the EMPTY one — PRE-EXISTING

`New Integer() {}` inside a `Select Case … When` guard emits `Total(t0)` with `t0` declared
nowhere — the same IRBuilder suppression path that entry 22 and commit 24a's JavaScript
`Size > 0` throw both come from, but the C# backend has no arm that renders an unbound
`IRArrayAlloc` at all, literal or not. After commit 24a the JavaScript backend accepts and
correctly RUNS that exact program (the `Size == 0` carve-out — spec §10), so the two backends now
diverge on this shape: JS runs it, C# fails the build. Pre-existing in the same suppression path,
untouched by 24a.

### 24. Two front-end gaps that block a test commit 24a wanted — found 2026-09-20

A user `Function` cannot declare an array return type in either spelling — `As Integer()` and `As
Integer[]` are both parse errors — and `s.Split(",")` parses as an array index ("Array index must
be an integer type"), not a method call. Consequence: no array-typed local can currently take an
`IRCall`/`IRAwait` initializer, which is the ONLY reason the JavaScript `IRStore`-skip added in
commit 24a (`JavaScriptBackend.cs:2225-2256` — skipping a store whose address is an `IRAlloca`) is
safe today: `TryRenameToVariable` renames a non-foreign `IRCall`/`IRAwait` initializer, and nothing
in the current language surface can construct an array-typed one to exercise that path. If either
gap is fixed, the `IRStore`-skip must be re-derived from scratch — the comment at that location
says so.
