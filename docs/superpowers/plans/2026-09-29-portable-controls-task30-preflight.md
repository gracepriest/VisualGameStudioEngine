# Task 30 pre-flight: the portable code-generation style and its opt-in command

Plan: `docs/superpowers/plans/2026-09-29-portable-control-library.md`, "### Task 30".
Spec: `docs/superpowers/specs/2026-09-29-portable-control-library-design.md` §6.1-§6.3, review C2/C3/I1/I5, O5.
Checked against `feat/portable-controls` @ **`d11d4738`** (Task 29 implementation landed; clean tree).
Read-only pre-flight; the only file created by this step is this document.

Follow this document **instead of** the plan's Task 30 section where the two disagree.
**OWNER decisions needed: none.**

---

## A. Anchors (claim -> today's file:line at d11d4738 -> verdict)

| # | Claim | Today | Verdict |
|---|---|---|---|
| 1 | `RegionMarkers` has a strict marker regex and `FormatOpen` | `RegionMarkers.cs:60-69`: `region=... form=... hash=...`, no style; `FormRegion` has no style field | extend both; keep the no-style path byte-identical |
| 2 | A style reader exists | no `BasicLang/Forms/FormCodeStyle.cs` | **create it; it is the only style reader** |
| 3 | The writer's init is target-driven | `RegionWriter.Write` scans markers at `:41`; `GenerateInit` branches on `form.Target` at `:708`; `AppendSiblings` branches at `:870`; `AppendControlInit` branches at `:912`; `AppendComponentInit` branches at `:949`; `AppendBinds` branches at `:1107`; `DeclaredType` branches at `:1332` | switch code generation on the recorded style, not the document target |
| 4 | WinForms controls never emit `Name` | no `Name =` line in `AppendControlInit` (`:918-922`) | add `id.Name = "id"` immediately after `New` on WinForms **and portable**; DOM unchanged |
| 5 | `CheckHandlerOrdering` is target-driven | `RegionWriter.cs:578-617` returns for non-web and diagnoses BL8013; no style input | pass the style and return for portable; BL8013 stays for DOM web |
| 6 | Handler stubs branch on target | `FormHandlers.cs:217-242`: signature and insertion side both use `form.Target`; the landed slice-3 rework is present (`PlanBind`, `DefaultEventDefOn`, `NameOn`) | make the ONE signature/placement site branch on style: portable uses the WinForms signature and WinForms (below-init) placement |
| 7 | The scaffold has only target and web layout | `FormScaffolder.Create` `:105-158`; `CodeBehind` `:195-254`; `AppendInitRegion` `:187-193` | add an explicit portable opt-in; portable web gets the three `Using` lines, `Inherits Form`, WinForms region order, and `style="portable"` markers |
| 8 | The command lives in `MainWindowViewModel` | the real "Add New Form" command is `SolutionExplorerViewModel.AddNewFormAsync` at `:1300-1388`; its menu item is `SolutionExplorerView.axaml:68`; the plan's `MainWindowViewModel`/`AddNewFormAsync` claim is stale | add the opt-in command to `SolutionExplorerViewModel`, adjacent to the existing `[RelayCommand]`, and bind it in the same Add submenu |
| 9 | `RegionWriter.Write` is the only writer of a new scaffold | `FormScaffolder.WithGeneratedRegions` calls `RegionWriter.Write` at `:175` | keep the scaffold's markers authoritative: the scaffold writes the style into the empty markers, then the writer reads it |
| 10 | DOM-style files are a byte-stable surface | existing DOM branches and expected outputs are green at d11d4738 | do not touch the DOM strings; only WinForms gains `Name`, and portable is new |

---

## B. Decisions

1. **The style is recorded, not guessed.** `FormCodeStyle` (new) is the one reader. Its result carries a `FormStyle` (`Dom`, `Portable`, `WinForms`, `Dual`), the scanned regions, and an optional malformed-style problem. `RegionWriter` calls it once; `FormHandlers` calls it for its code text. A marker without `style` means the target's historical style (`WinForms` for `.blform`, `Dom` for `.blwebform`); a portable marker means portable. The raw style is preserved when a region is rewritten.
2. **The marker grammar is strict.** `style` is accepted only between `form` and `hash`. `portable` is legal only on `.blwebform`; `dual` is legal only on `.blform` (the piece-3 contract). An unknown value, a style illegal for the extension, or the two regions disagreeing is `RegionMarkersMalformed` (`BL8012`) on both extensions. The hash covers content only; the marker style is not hashed. The old no-style marker is pinned as unchanged.
3. **The writer branches on the style, not `FormTarget`.** Portable is a web-target document whose code-behind uses the WinForms walk. It emits `New <WinFormsType>()`, `id.Name = "id"`, `AddHandler`, `Me.Controls.Add` in reverse, and WinForms field types. It emits no `Dim doc`, FormRoot rows, `Me.<FormProperty>`/reference rows, geometry, or control property lines. Tray components are the exception: they construct and keep their property lines; controls do not. DOM remains exactly today's `doc.getElementById`/`addEventListener` walk.
4. **The init is tagged at generation time.** `RegionWriter.GenerateInitLines` returns `InitLine(Text, Kind)` (internal, visible to the test project): `Geometry`, `Property`, `ComponentProperty`, `RootProperty`, `Reference`, and `Other`. WinForms emits everything; portable emits everything except `Geometry`/`Property`/`RootProperty`/`Reference` (`ComponentProperty` is retained); DOM uses its own DOM lines. This is the piece-3 seam, not a text-shape heuristic.
5. **`Name` is a WinForms/portable line.** It is generated once, immediately after the `New` line, before geometry/properties. It is not generated for a DOM element fetched by id. This intentionally rehashes every existing WinForms region on its next save, and no DOM region.
6. **Handler style follows the marker.** Portable uses `(sender As Object, e As {WinFormsArgs ?? "EventArgs"})` and inserts BELOW the init region even on a web document; DOM uses the existing DOM signature and inserts ABOVE the init region. `BL8013` is skipped for portable only. Existing event names stay as slice 3 chose them; portable `AddHandler` writes the WinForms event spelling, mapped through the catalog rather than trusting a DOM `click` string.
7. **The scaffold receives the style explicitly.** `FormScaffolder.Create(..., bool portable = false)` is the opt-in (the plan's larger style parameter is not needed). It writes `style="portable"` into both empty markers before calling `RegionWriter.Write`; therefore a brand-new form with no controls is portable by its marker, not by detecting content. The portable scaffold emits three `Using` lines, `Inherits Form`, the init region after `New()`, and the WinForms handler comment. DOM scaffold output is byte-identical.
8. **The command is real and reachable.** `AddWebFormPortableCommand` is generated from a new `[RelayCommand]` directly above `AddWebFormPortableAsync` in `SolutionExplorerViewModel` (the plan's `MainWindowViewModel` route is stale). It forces a web document with `portable: true`, writes and lists the pair through the existing path, and has a menu item in the same Add submenu. `SolutionExplorerNewFormTests` drives the generated command and reads `SolutionExplorerView.axaml` for the binding.
9. **No IDE forms drop in this task.** Only `IDE/lib/js/dom-core.bli` remains lock-step from Task 28/29. The IDE's form library is deliberately not copied until the Task 36 drop. `FormCodeStyle`, the writer, and the scaffold are source-tree changes.

---

## C. Implementation order

1. `FormCodeStyle.cs` + `RegionMarkers` style field/format overloading; fast marker tests, including `dual` on `.blform`, unknown on both extensions, disagreement, and no-style legacy.
2. `RegionWriter`: read style, tag init lines, portable filtering, `Name` on WinForms/portable, portable controls/components/binds, BL8013 skip, style-preserving rewrites. Fast writer tests and the tagged subset test.
3. `FormHandlers`: style-aware signature and placement; portable defaults/non-defaults; BL8013 is writer-side.
4. `FormScaffolder`: portable scaffold and explicit style markers; no-control portability test.
5. `SolutionExplorerViewModel` command + AXAML item; entry-point test through the real generated command.
6. Re-run all `Form*` fast fixtures and `WinFormsCatalogSweepTests`; update only the `Name`-line expectations and any intentional hashes. No DOM expected text changes.

---

## D. Tests (the lean gate)

- **New fast:** `FormCodeStyleTests` (style parsing/refusal), `FormPortableCodegenTests` (tagged subset, portable bytes, `Name` on both targets, no-control form, DOM byte stability, BL8013 skip).
- **Existing fast:** all `Form*` fixtures that touch writer/scaffolder/handlers, `WinFormsCatalogSweepTests`, and the shell `SolutionExplorerNewFormTests`.
- **Entry point:** generated `AddWebFormPortableCommand` invoked through the real `SolutionExplorerViewModel`; AXAML must contain `AddWebFormPortableCommand` and a portable-labelled header.
- **Not run here:** `IdeDropLibraryMatchesSourceTests` (expected to fail between drops), `WinFormsCatalogSweepTests`' csc leg if the WindowsDesktop pack is absent (SKIP, never a false pass).

### Named gate command

```
dotnet test VisualGameStudio.Tests\VisualGameStudio.Tests.csproj -c Release --no-build --nologo \
  --filter "FullyQualifiedName~FormCodeStyleTests|FullyQualifiedName~FormPortableCodegenTests|FullyQualifiedName~FormRegionWriterTests|FullyQualifiedName~FormScaffolderTests|FullyQualifiedName~FormHandlerPlanTests|FullyQualifiedName~SolutionExplorerNewFormTests|FullyQualifiedName~WinFormsCatalogSweepTests"
```

Then the fast subset (`TestCategory!=Integration`) with the Task 29 baseline: six known failures only.

---

## E. Risks and traps

- **Every WinForms region rehashes once** because `Name` is new. The content hash is recalculated by `ReplaceRegion`; no marker hash hand-edit is needed. Change only tests that assert exact WinForms output or hashes; DOM tests must remain untouched.
- **The WinForms walk is G4, not an approximation.** Containers must remain fully populated before their own `Controls.Add`; sibling adds stay reversed; host items stay in document order.
- **Portable event spelling.** A web document stores `click`; the portable code-behind needs `AddHandler btn.Click`. Map through `FormEvents.NameOn(..., FormTarget.WinForms)` and do not emit the DOM string.
- **Components are not controls.** A Timer has no `Controls.Add`, and must not get a `Name` line that csc cannot resolve. Keep component construction/property/bind lines, tag property lines `ComponentProperty`, and retain them in portable.
- **BL8013 must skip only portable.** A DOM web file keeps the existing above-init refusal; a portable file is allowed the WinForms below-init placement.
- **No style for `.blform` except `dual`.** Do not accept `portable` there and do not accept `dual` on `.blwebform`; both are BL8012.
- **No whole-file rewrites.** Use targeted edits; stage by name; commit via `git commit -F`; no push.

---

## F. Out of scope

Task 31+ kind implementations, style-rule `<style>`/runtime geometry, catalog rows, conversion, the IDE forms drop, and the default flip. This task produces the style contract, portable init/scaffold, handler style, and the opt-in entry point only.
