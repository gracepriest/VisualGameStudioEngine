# Task 32 preflight: portable page rendering and client areas

Plan: `2026-09-29-portable-control-library.md`, Task 32. Spec: §7, §10.4.
Checked against `feat/portable-controls` at `ef67f690` (clean tree).
This preflight wins where the task outline is stale. No owner decisions needed.

## Anchors and corrections

| Claim | Current anchor | Decision |
|---|---|---|
| Page generation knows the style | `FormAssetEmitter.Emit/Html/Css` take only a document; `SourcePath` is retained by the reader | Emit reads the paired `.bas` through `FormCodeStyle`; pure Html/Css helpers accept explicit style, default DOM |
| Both build routes emit pages | `Program` and `BuildService` call `LoadWebForms`, then `JavaScriptEmitter.Emit` -> `FormAssetEmitter.Emit` | put the marker read at that shared emitter, not a second reader per route |
| Catalog rendering facets exist | `FormControlDef` has tag, input type and WebCss but no caption/multiline/client/enable facets | add optional row facets, never kind switches in the emitter |
| DOM metadata exists | `FormPropertyDef` has HtmlAttribute/CssProperty, no DomMember | add live DOM mapping metadata; per-kind Text/Checked mappings override shared rows |
| Client sizes are correct | `FormDockLayout.Walk` uses outer size; `ResolveSiblings` starts at 0,0 | add FormClientArea (origin inset plus separate dock padding), consume once in the resolver |
| Canvas origins are correct | `FormCanvasTransform.Layout/ContainerAt` use container bounds.TopLeft | add only the origin inset; dock padding applies only to docking, never positioned children |
| Page anchoring uses client sizes | `CanvasPlacement` asks designer.ClientSizeOf | keep that seam; the resolver now subtracts borders for portable pages; DOM pages explicitly retain legacy mode |
| Reflow matches C# | `FormDockScript.Core`, DataJson, PageScript mirror ResolveSiblings/Walk | carry client metrics from FormClientArea into node data; update C#/JS together; retain byte-identical legacy script text/data |
| WinForms sizes use the resolver | `RegionWriter.GenerateInitLines` resolves desktop docked bounds once | corrected child/container dock sizes flow through the existing seam; no new writer algebra |
| Library geometry exists | Task 33 owns it; Task 31 has DOM-backed controls and style seam | emit design-client/inset/padding metadata and add library accessors; do not create a second anchor/reflow engine here |
| Existing pixel tests use old rendering | `PixelPageLayoutTests` records missing captions and UA fieldset borders; pixel reference records model gaps | keep legacy-page records explicit; update desktop/model expectations only for the specified inset correction |

## Decisions

1. Only Portable pages get wrappers, textarea/password markup, legend/frame, panel borders, hidden and enable metadata. Legacy DOM Html/Css/script bytes are pinned BEFORE product changes.
2. Rendering facets: caption wrapper/tag, multiline tag, password property, native/ARIA enable target, client-area rule and portable-only kind CSS. The same catalog owns DomMember metadata. Existing WebCss/CssProperty behavior remains unchanged for DOM pages.
3. FormClientArea.Of(control) reads the row's border-inset rule (Panel 0/1/2) and dock padding (GroupBox 3/19/3/3). Invalid/degraded borders use None. Client size is max(0, outer - 2*inset). Bounds remain relative to the client origin; only docked children get dock padding.
4. FormDockLayout defaults to corrected client areas for desktop/canvas callers; its explicit legacy switch is used by DOM CSS/script emission. Public sibling resolution gets optional dock padding. The JS core receives the same resolved metrics, not another kind table.
5. The form area gains a stable portable-only id for default radio grouping. Wrapper id/classes/geometry/style/hidden remain on the label; checked/name/readonly/disabled go on the inner input. Enabled state is retained per control in data-vgs-enabled and propagated to descendants without losing each child's own choice.
6. Portable Visible=False is hidden, not a display:none rule, on Canvas/Grid/Flow. The hidden rule is emitted for all portable layouts. GroupBox has zero border/padding/margin and a non-hit-testing frame pseudo-element; the legend is not a control or docked sibling.
7. Emit anchor/dock/requested-size and design client/inset/padding metadata from the existing deciders. Task 33 owns live runtime geometry and always-on reflow. No IDE forms drop; DOM declarations, if changed, stay lock-step.
8. Security: caption/legend/value/attribute escaping remains mandatory. No innerHTML. A style problem in marker selection is BL8012, not silent fallback to a different style.

## Implementation and lean gates

1. Pin legacy first-kind/nested/docked page hashes on ef67f690; add client-rule tests.
2. Catalog + FormClientArea; resolver + canvas origins + script lock-step; add literal bounds and client-size tests.
3. Portable markup/CSS/style selection and metadata; real CLI and IDE build-route tests.
4. Library metadata consumption; rendering/client-area tests; spec review then quality review and targeted mutations.
5. Build tests, then named gate: PortablePageRenderingTests, FormClientAreaTests, FormAssetEmitterTests, FormDockLayoutTests, FormDockScriptTests, FormCanvasTransformTests, FormCanvasRenderTests, FormRegionWriterTests, FormPortableCodegenTests, FormBuildEmissionTests, PortableFirstKindsNodeTests, DomCoreLockStepTests; named pixel-layout integration fixtures as affected. Then broad Form*/PixelLayout* fast fixtures (exclude Formatting), then full fast subset.

Compare failures by name with Task 31's six known baseline failures. Record all intentional desktop/model expectation changes. Stage by name, commit via git commit -F. No stash, push or IDE forms drop.
