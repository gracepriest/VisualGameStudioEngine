title: JavaScript backend
lede: BasicLang in the browser — ES modules, a typed DOM, `#JsImport`, and a capability checker that refuses rather than guesses.
---
The JavaScript backend compiles a BasicLang project into ES modules plus an HTML page,
runnable in a browser and previewable with `F5` in the IDE. It is the newest maintained
target and the most opinionated about what it will *not* do.

## Emitting a site

```powershell
IDE/BasicLang.exe new web -n MySite        # scaffold
IDE/BasicLang.exe build                    # emit modules + page
IDE/BasicLang.exe program.bas --target=javascript
```

> [note] The output uses ES modules, so a browser will not load it from a `file://` page.
> Serve the directory over HTTP — or press `F5` in the IDE, which starts `WebPreviewServer`
> for you. The CLI prints this reminder after a successful build.

Implementation: `JavaScriptBackend.cs` (~3,100 lines) with `JavaScriptEmitter.cs`,
`JavaScriptTypeMapper.cs`, `JavaScriptSourceMap.cs` (source maps, so the browser debugger
shows BasicLang), and `JsExceptionTypes.cs`.

## The typed DOM

`BasicLang/lib/js/dom-core.bli` declares the DOM as `Extern Class` types. An
`Extern Class` **emits nothing** — the browser already provides the object — and its
members carry the exact JavaScript names, so a BasicLang call is the real call. Member
casing does not matter at the use site: `el.TextContent` reaches `textContent`.

```vb
Dim d As Document = ::document
Dim w As Window = ::window
Dim el As Element = d.getElementById("out")
el.textContent = "hello"
el.addEventListener("click", Sub(e As DomEvent) Console.WriteLine(e.target.id))
```

`::name` is the untyped hatch to any global. Anything not declared in `dom-core.bli` is
still reachable that way, or verbatim through a `javascript{ }` block.

`console` is deliberately *absent* from the declarations: `Console.WriteLine` already
lowers to `console.log`, and a declared `console` would collide with that surface — the
collision the checker reports as `BL7011`.

> [trap] `IDE/lib/js/dom-core.bli` is **load-bearing in the deployed drop**. The deployed
> compiler auto-includes it for every JavaScript build; without it the typed DOM does not
> resolve. Any refresh of `IDE/` must keep it.

## `#JsImport`

Pull in real JavaScript modules with a directive that mirrors ES import syntax:

| Directive | Emits |
|---|---|
| `#JsImport "./m.js"` | `import "./m.js";` |
| `#JsImport { greet, other } From "./m.js"` | `import { greet, other } from "./m.js";` |
| `#JsImport { greet As hi } From "./m.js"` | `import { greet as hi } from "./m.js";` |
| `#JsImport lib From "./m.js"` | `import lib from "./m.js";` (default) |
| `#JsImport * As lib From "./m.js"` | `import * as lib from "./m.js";` (namespace) |

## What the backend refuses

`JsCapabilityChecker.cs` rejects constructs that cannot be lowered faithfully. The
principle is the same as on the native backend: **a diagnostic, never plausible-looking
wrong code.**

| Code | Rejected |
|---|---|
| `BL7002` | `ByRef` parameters |
| `BL7003` | `Long` — no 64-bit integer with matching semantics |
| `BL7004` | `Char` |
| `BL7005` | Value `Structure` — JavaScript has no value semantics for objects |
| `BL7006` | Operator overloads |
| `BL7007` | BCL types the backend does not provide (e.g. `System.IO.Stream`) |
| `BL7010` | A `#JsImport` that cannot be lowered |
| `BL7011` | A declared type colliding with a provided global (e.g. `console`) |
| `BL7012` | A non-provided exception type |

## Control flow, once more with feeling

The JavaScript backend derives an `If`'s merge block through `FindMergeBlock`, which
means **the true branch target must never itself be the merge block**. This is a
different constraint from the C# backend's name matching and from the C++ backend's
goto-based emission, and a CFG change has to satisfy all three.

## In the IDE

The IDE gained a JavaScript project type after the compiler could already emit one. The
pieces:

| Piece | Where |
|---|---|
| `SolutionTypes.JavaScript` (id `javascript`) and the `web-site` template | `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs` |
| `.blproj` writer and page generator | `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs` |
| Wizard "JavaScript (Web)" backend option | `VisualGameStudio.Shell/ViewModels/Dialogs/NewProjectWizardViewModel.cs` |
| CLI `basiclang new web` | `BasicLang/ProjectSystem/TemplateEngine.cs` |
| `F5` preview server | `VisualGameStudio.ProjectSystem/Services/WebPreviewServer.cs` |

> [note] **Open gap:** the `lib.dom.d.ts` → `.bli` generator was never built, so
> `dom-core.bli` is hand-curated and covers a deliberately small core. The wizard's
> JavaScript path is covered by view-model and template-service tests but has not been
> clicked through by a human — nothing in the suite can drive the Avalonia window.
