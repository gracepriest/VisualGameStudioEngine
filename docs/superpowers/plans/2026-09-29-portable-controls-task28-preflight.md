# Task 28 pre-flight: library skeleton, inclusion hook, DOM surface

Plan: `docs/superpowers/plans/2026-09-29-portable-control-library.md`, "### Task 28".
Spec: `docs/superpowers/specs/2026-09-29-portable-control-library-design.md` §5.1, §5.2, §5.7, §5.10, §11b note 3.
Checked against `feat/portable-controls` @ **`ef61c7cb`** (Task 27's 2.0b gate landed; clean tree).
Read-only pre-flight; the only file created by this step is this document.

Follow this document **instead of** the plan's Task 28 section where the two disagree.
**OWNER decisions needed: none.**

---

## A. Anchors (claim -> today's file:line at ef61c7cb -> verdict)

| # | Claim | Today | Verdict |
|---|---|---|---|
| 1 | The library source directory exists | `BasicLang/lib/js/forms/` does not exist; `BasicLang/lib/js/` contains only `dom-core.bli` | **create the first files** |
| 2 | The compiler ships `dom-core.bli` beside itself | `BasicLang/BasicLang.csproj:39-44`: `None Include="lib\js\dom-core.bli" CopyToOutputDirectory="PreserveNewest"` | **extend with a forms glob**; preserve relative paths |
| 3 | The project route adds the DOM declarations | `BasicLang/Compiler.cs:391` calls `WithJavaScriptDeclarations(sourceFiles)` inside `CompileProjectFiles` | **pass through the forms hook here** |
| 4 | The single-file route uses the same route | `BasicLang/Compiler.cs:274-275`: a JavaScript single file returns `CompileProjectFiles(new[] { filePath })` | **no separate hook needed**; both routes meet at :391 |
| 5 | `WithJavaScriptDeclarations` is the model and is backend-gated | `BasicLang/Compiler.cs:575-590` (`DomDeclarationsPath`, `IsJavaScriptTarget`, `WithJavaScriptDeclarations`) | **put `WithWebFormsLibrary` beside it**; reuse `IsJavaScriptTarget` |
| 6 | The plan's class-merge anchor is `Compiler.cs:986-991` | That range now handles exported namespace symbols. The actual first-wins class merge is `BasicLang/Compiler.cs:1077-1083` (`if (!combined.Classes.ContainsKey(cls.Key)) ...`) | **anchor corrected**; collision must be refused before this merge |
| 7 | The inclusion test can reuse the `.bli` fixture's output check | `VisualGameStudio.Tests/Compiler/BliDeclarationFileTests.cs:119-121` checks `BasicCompiler.DomDeclarationsPath`; `:53` constructs a JavaScript compiler | **use `BasicCompiler`, not `JsTestSupport`** |
| 8 | `JsTestSupport` exercises the inclusion hook | It builds the lexer/parser/analyzer/IR directly and bypasses `BasicCompiler` | **wrong tool for Task 28**; it cannot see auto-included units |
| 9 | The trigger must skip comments and string literals | Spec §11b note 3. `BasicLangLexer` emits `TokenType.Comment`, `TokenType.StringLiteral`, `TokenType.CharLiteral`, and a `Using` token; whitespace is skipped | **token scan, not a regex**; ignore string/char/comment tokens |
| 10 | BasicLang spells the directive `Using` | `BasicLang/Parser.cs:697` consumes `TokenType.Using`; the lexer has `Import` (a DLL/module import), not `Imports` | **trigger on `Using` and qualified names**; do not treat `Import` as a namespace directive |
| 11 | The DOM declarations lack the Task 28 members | `BasicLang/lib/js/dom-core.bli` has no rect/client metrics, `closest`/`contains`, positional style, event `button`/`buttons`/`detail`/`relatedTarget`/`code`/modifiers, `getComputedStyle`, `styleSheets`, `CSSStyleSheet`, or `CSSMediaRule` | **extend the declaration surface** |
| 12 | The IDE DOM copy must stay byte-identical | `IDE/lib/js/dom-core.bli` exists beside the compiler copy | **update both copies in one commit**; no IDE forms drop |
| 13 | `PortableLibraryNameCollision` has an assigned BL code | No occurrence in the repo; Task 28 assigns none and no BL7004 path is involved | **emit a named `SemanticError`**; do not invent a BL number |
| 14 | Library classes are compiled program units | `.bas` is source and entry-like, but a unit without `Main` is not required to be an entry point; existing cross-file class tests prove simple-name IR keys | **`.bas` shells compile and merge**; no stub `Main` |
| 15 | `vgs-page.bli` belongs to the library | Spec §5.10: `Extern Class VgsDockHook` with `request`/`reflow` | **add it; include it only under the hook** |
| 16 | The plan requires `BasicLang/lib/js/forms/*.bas` | Task 28 says library skeleton; Task 31 says "one `.bas` per kind" | **create one shell per kind now**; behaviour lands in Tasks 29-31+ |

## B. Blockers

None. The required Task 15 namespace behavior, `.bli` support, and Task 27 compiler state are
already on this branch. The only new integration point is the compiler's source-set hook.

## C. Corrections and decisions taken

1. **Old anchor corrected.** The first-wins merge is `Compiler.cs:1077-1083`, not `:986-991`.
   A duplicate library/program class would silently keep the first IR class there, so the
   collision check runs immediately after phase-1 parsing and before any analysis/merge.
2. **`Imports` is not a BasicLang keyword.** Detect `Using System.Windows.Forms`,
   `Using System.Drawing`, and any qualified `System.Windows.Forms.` / `System.Drawing.`
   token sequence. `Import` (the DLL/module directive) is not a trigger.
3. **Token scan includes comments and strings as tokens.** We filter out `Comment`,
   `StringLiteral`, `CharLiteral`, and `InterpolatedStringLiteral` before matching. A comment or
   a string that merely mentions the namespaces therefore cannot pull the library in.
4. **The forms hook runs after the DOM hook and scans the original source set** (plus the DOM
   declarations, which contain no trigger). It cannot scan the forms files it is about to add,
   or the library would include itself.
5. **A library file is a `.bas` or `.bli`** under `BasicCompiler.WebFormsLibraryDirectory`.
   Both are copied by one csproj glob. Sorting by ordinal path makes the source set deterministic.
6. **The skeleton is intentionally behaviour-free.** Task 28 creates the namespaces, enums,
   class shells and inheritance, not WinForms behaviour. `Dim x As New Button()` already works
   with an implicit parameterless constructor (see `JavaScriptProgramNamespaceTests`).
7. **Kinds are one file per class** (`Button.bas`, `Label.bas`, ...), matching Task 31's shape.
   `Control`, `Form`, collections, `Padding`, `Cursor`/`Cursors`, event args, `MessageBox`,
   and the shared enums live in `Core.bas`/`Enums.bas`; strips and items get their own files.
8. **No IDE forms drop.** Only `IDE/lib/js/dom-core.bli` is updated, byte-for-byte. The
   load-bearing `IDE/lib/js/forms/` refresh is the later IDE drop, not Task 28.
9. **No BL number for the collision.** The diagnostic's stable, assertable name is
   `PortableLibraryNameCollision`; the message names the program class and the library class
   (including its namespace). Task 34 owns numbered web-build diagnostics.
10. **No hard-coded `bc29391e` golden.** That commit predates Task 15's namespace work, so its
    literal output is not reproducible here. The no-trigger test asserts both the existing
    output's essential shape and the absence of every library unit; the trigger tests assert
    inclusion and successful JS emission.

## D. Implementation steps

1. Add `BasicCompiler.WebFormsLibraryDirectory` and `WithWebFormsLibrary` beside
   `DomDeclarationsPath`/`WithJavaScriptDeclarations`; wire the project route through both.
2. Add token scanning, with comments/strings filtered, and include every `.bas`/`.bli` under the
   library directory only on JavaScript when a trigger is present.
3. Add the collision walk after phase-1 parsing; collect library class simple names and full
   names from the AST, walk program unit namespaces/classes recursively, and add one
   `PortableLibraryNameCollision` error per colliding program class. Return before phase 2 if any.
4. Create the library source skeleton:
   - `BasicLang/lib/js/forms/System.bas`: `System.EventArgs`.
   - `Drawing.bas`: `System.Drawing.Color`, `Point`, `Size`, `Image`, `Font`, `FontStyle`.
   - `Core.bas`: `Control`, `Form`, `ControlCollection`, `ObjectCollection`, `Padding`,
     `Cursor`, `Cursors`, `MessageBox`, event-argument classes.
   - `Enums.bas`: `Keys`, `MouseButtons`, `DockStyle`, `AnchorStyles`, `BorderStyle`,
     `ContentAlignment`, `PictureBoxSizeMode`, `DialogResult`, `MessageBoxButtons`.
   - One `.bas` per Callisto kind: the 15 positioned kinds, `ToolStrip`, `MenuStrip`,
     `StatusStrip`, `ToolStripItem`, `ToolStripMenuItem`, `ToolStripButton`,
     `ToolStripStatusLabel`, `ToolStripSeparator`, and `Timer`.
   - `vgs-page.bli`: `Extern Class VgsDockHook` with `request`/`reflow`.
5. Extend both `dom-core.bli` copies with `DOMRect`, client/offset metrics,
   `getBoundingClientRect`, `closest`, `contains`, positional style properties, event fields,
   `getComputedStyle`, `styleSheets`, `CSSStyleSheet`, `CSSStyleRule`/`CSSMediaRule`/`CSSRuleList`.
6. Add the csproj glob for `lib\js\forms\**\*`.
7. Add `WebFormsLibraryInclusionTests` (no-trigger, Using, qualified-only, comment/string,
   C# gate, ship-beside, skeleton type coverage, collision, collision-free cases) and
   `DomCoreLockStepTests` (byte equality).
8. Build, run the named tests, then the Task 28 gate, and commit both DOM copies together.
   No push and no IDE drop.
