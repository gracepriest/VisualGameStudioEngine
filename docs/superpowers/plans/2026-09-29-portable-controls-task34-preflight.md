# Task 34 preflight: the build errors and editor awareness

Plan: `2026-09-29-portable-control-library.md`, Task 34. Spec §5.11, O3, O11, §11b note 3.
Base: `feat/portable-controls` @ `3f0f2c87`, working tree clean. Every anchor below was re-read on
this tree; the plan's line numbers had drifted.

## What already exists

| Fact | Where |
|---|---|
| The method/property manifest is real and gated | `BasicLang/Forms/PortableMembers.cs` (15 rows); `PortableMembersManifestTests` |
| The library is included by a token scan, JavaScript only, `.bas` + `.bli` | `BasicLang/Compiler.cs:623-704` (`WithWebFormsLibrary`, `HasPortableNamespaceReference`) |
| A program class colliding with a library class is already a named error | `BasicLang/Compiler.cs:712-745` (`PortableLibraryNameCollision`) |
| The DOM declarations are the only LSP web declaration file | `BasicLang/LSP/LspProjectContext.cs:457-476`; its own comment says the portable library joins here |
| The analyzer already records the target | `SemanticAnalyzer.ConfigureTarget` → `_isJavaScriptTarget` |
| The missing-member final arm is the user-class path | `SemanticAnalyzer.cs:12864`; the `With` arm is `:10654` |
| The library declares `.Element` as a public property | `BasicLang/lib/js/forms/Control.bas:55` |
| Design-region parser exposes the raw marker style and line | `RegionMarkers.Scan` → `FormRegion.Style`, `.Line`, `.Content` |
| LSP web tests already drive a real `.blproj` and `DocumentManager` | `VisualGameStudio.Tests/LSP/LspConditionalCompilationTests.cs` |

## Measured today (probes against the built compiler)

| Shape | Current result | Task 34 target |
|---|---|---|
| `btn.ShowDialog()` on a web library `Button` | `Type 'Button' does not have a member 'ShowDialog'. Available members: TextAlign, PerformClick` | `WebUnavailableMember`: `'Button.ShowDialog' is a WinForms member that is not available on the web. Put desktop-only code inside #If DESKTOP Then ... #End If.` |
| `btn.Nope = 1` | same user-class wording | same `WebUnavailableMember` wording |
| `MessageBoxButtons.RetryCancel` | argument conversion from `Object` to `MessageBoxButtons` | `WebUnavailableMember` naming `MessageBoxButtons.RetryCancel`, with no follow-on conversion error |
| `Dim g As DataGridView` on JavaScript | generic `BL7007 ... no .NET base class library` | `WebUnavailableMember` naming `DataGridView`, suggesting `#If DESKTOP` |
| `New Button()` in `Sub Main` | compiles | `RuntimeControlCreation`: `Creating a Button at run time is not supported on the web yet.` |
| desktop C# `Private b As Button` + `b.Element` | compiles green | `ElementOnDesktop`: `'.Element' is web-only; wrap it in #If WEB Then ... #End If.` |
| web DOM-style designer regions + `Using System.Windows.Forms` | compiles | `MixedCodeStyle` |
| web `.Element` on a library control | compiles | stays legal |
| `New Button()` inside the designer init region | compiles | stays legal |

Baseline warning, unrelated and not touched: a web build with the library prints
`BL6017: .NET type 'System.Object' has no accessible member named 'vgsDock'` from the library's
`::window.vgsDock` hook. It is a warning, present before this task, and it does not decide anything
here.

## Decisions (implementation choices, not owner decisions)

1. **One type seam.** New `BasicLang/Forms/PortableLibraryTypes.cs`:
   - `IsControlKind(name)` — `FormControlCatalog.Find(name) != null`, plus `Form`/`Control`.
   - `IsLibraryClass(name)` — control kinds, `PortableMembers.Manifest` declaring types, and the
     support/receiver classes the library declares (`ControlCollection`, `MessageBox`, `Padding`,
     `Cursor`, `Cursors`, `ObjectCollection`).
   - `IsLibraryReceiver(TypeInfo)` — the simple name is a library class, or its base chain names one
     (`Me` inside `Class LoginForm Inherits Form`).
   - `IsKnownWinFormsKind(name)` — a catalog `Kind`. A catalog kind reaching BL7007 is by definition
     a kind the web library does not ship (today `DataGridView`, `TabControl`, ...).

2. **`WebUnavailableMember` is a rewording, not a second analysis.** On a JavaScript build, in the
   analyzer's final missing-member arm and the `With` arm, a receiver whose type name is a library
   class reports the web message. A method is available exactly when the library declares it (the
   manifest gates that); a property/event exactly when the catalog does (the library declares those).
   The existing suggestion list is dropped for this arm because it lists library members, not the
   WinForms member the user typed. `MessageBoxButtons` beyond the three is caught in the source
   check (below) so the useless conversion error never appears.

3. **BL7007 keeps its family but gets the web wording.** `JsCapabilityChecker` still raises it, but
   when the type name is a known catalog kind that the web library does not declare, the message
   names the type and suggests `#If DESKTOP`. Non-WinForms types (`Stream`, ...) keep the generic
   message.

4. **`ElementOnDesktop` is checked in `BindMemberAccess`**, before the missing-member arm, only on
   non-JavaScript builds, only for `.Element`, and only when `IsLibraryReceiver(objectType)` is true.
   A receiver typed `Object` is left to csc (spec's recorded K14 limit). It reports and returns
   without a second “has no member” error.

5. **Source-level checks run once, after parsing and before semantic analysis**, in
   `BasicLang/Forms/PortableWebSourceChecks.cs`, from `CompileProjectFiles` (the single-file route
   delegates here). They are JavaScript-only and skip the library's own units:
   - `RuntimeControlCreation`: token `New` followed by a control class name outside the file's
     `init` region lines. The init region is found with `RegionMarkers.Scan`; a file with no init
     region has no allowed creation site. The check runs before analysis, so the user sees only the
     named error.
   - `MixedCodeStyle`: a file carrying DOM-style designer markers (a marker with no `style`)
     whose tokens contain `Using System.Windows.Forms`, `Using System.Drawing`, or
     `Inherits Form`. Tokens, not text: comments and string literals are filtered first (§11b note 3).
   - `MessageBoxButtons` beyond `OK`/`OKCancel`/`YesNo`: token `MessageBoxButtons` + `.` + a name not
     in the three. Reports `WebUnavailableMember` and stops before the conversion error.
   Errors carry the user's line/column. The named prefix is the stable contract, matching
   `PortableLibraryNameCollision`; no BL number is invented in this task.

6. **The LSP joins the build's trigger, not a second list.** `WithWebDeclarations` passes its source
   set to `WebDeclarationFiles(files)`. That always yields `dom-core.bli`, and when any source file
   references the two portable namespaces (the same lexer scan as the build, made `internal` on
   `BasicCompiler` so there is one trigger), yields every `.bas`/`.bli` in
   `BasicCompiler.WebFormsLibraryDirectory`. Desktop projects still see neither.

7. **No IDE-forms drop change.** The library drop copies are already byte-identical and the
   refresher is Task 36's; this task adds no form assets.

## Tests

New `VisualGameStudio.Tests/Compiler/PortableBuildErrorTests.cs`, CLI route
(`BasicCompiler.CompileProjectFiles`), one row per error plus the positive controls:

- `WebUnavailableMember`: `Button.ShowDialog` (method), `Button.Nope` (property),
  `MessageBoxButtons.RetryCancel`, `DataGridView` (BL7007 reword). Assert the message names the
  member/type and `#If DESKTOP`; assert the old wording/`Available members` is gone.
- `RuntimeControlCreation`: `New Button()` in `Sub Main`; `New LoginForm()` outside init; allowed
  `New Button()` on a line inside an `init` marker pair; allowed on a desktop build.
- `MixedCodeStyle`: DOM markers + `Using System.Windows.Forms`; DOM markers + `Inherits Form`;
  a comment/string naming the namespace does not trigger; no markers does not trigger; desktop
  does not trigger.
- `ElementOnDesktop`: `b.Element` with `Private b As Button`; `Me.Element` in `Inherits Form`;
  `b.Element` under `#If WEB` is blanked on a desktop build and stays legal; web build legal.

LSP, in `LspConditionalCompilationTests` (the existing real-`.blproj` helper):

- a web project's completion after `btn.` lists a library member (e.g. `Text` / `PerformClick`) —
  proving the library reached the project symbol table, not just the DOM declarations;
- a desktop project flags `btn.Element` with `ElementOnDesktop`;
- a web project does not flag it.

## Gate

1. `PortableBuildErrorTests`, `PortableMembersManifestTests`, `WebFormsLibraryInclusionTests`,
   `LspConditionalCompilationTests`, `CompletionServiceTests`, `CrossFileAnalysisTests`,
   `LspMixedProjectTests`, `ModClsDocumentTests`, `BaseConstructorCallDiagnosticsTests`.
2. Fast subset `TestCategory!=Integration`.
3. Commit by name; no push (Tasks 29–34).

Known fast-subset baseline: six failures (`EveryTextRoute_...`, three `Emit_Replaces...`,
two `SearchSnippets_...`). They are not Task 34 regressions.
