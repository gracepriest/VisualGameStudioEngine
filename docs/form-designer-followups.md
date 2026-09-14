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
- a **qualified module call** emits a reference to a container JS does not have → `ReferenceError`;
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

Nothing in that message names the real cause, and it is not specific to generated code. The same
`Sub` inside `Public Module VgsForms` resolves and lowers correctly, so the generator emits it that
way and `Main()` calls `VgsForms.VgsDispatchForm()`.

**Two consequences to decide on:** the spec's D7 text and `BL8031`'s reserved purpose both now refer
to a module member rather than a top-level name; and the cross-file top-level `Sub` limitation is a
**compiler** gap worth its own investigation — it is not a form-designer problem and it will bite
anyone splitting procedural code across files on the JavaScript backend.

### 13. Nothing makes `Main()` call the dispatch for you
`BL8018` warns when a project has form pages and no source calls `VgsForms.VgsDispatchForm()`, which
is the honest minimum: the backend emits exactly one invocation, for `Main`, so an uncalled helper
is a page that loads a script and does nothing.

But a warning is not the same as it working. The options are to have **File → New Form** offer to
insert the call into the project's `Main()` the first time a form is added, or to make the web
project template ship a `Main()` that already dispatches. Both edit the user's code, which is why
neither was done unilaterally.
