# Extension manifest binding — shape fixes, then per-section error isolation

**Status:** design, approved in restructured form 2026-08-05.
**Companion:** [`2026-08-05-extensions-recovery-ledger.md`](2026-08-05-extensions-recovery-ledger.md).

---

## 1. The problem, measured

`ExtensionManifest` ([`IExtensionService.cs:421`](../../../VisualGameStudio.Core/Abstractions/Services/IExtensionService.cs))
holds identity (`Name`, `Version`, `Publisher`, `Main`, `ActivationEvents`) and `Contributes` in one
strongly-typed graph. `JsonSerializer.Deserialize<ExtensionManifest>`
([`ExtensionService.cs:859`](../../../VisualGameStudio.ProjectSystem/Services/ExtensionService.cs))
is eager and all-or-nothing, and the catch is at **whole-extension scope**
([`:151`](../../../VisualGameStudio.ProjectSystem/Services/ExtensionService.cs)). One shape mismatch
anywhere and the extension vanishes entirely.

This was believed to be a two-instance problem (`fbcdce5`, `3449378`). It is not. Probed against the
real `VisualGameStudio.Core.dll` using a byte-copy of the production `JsonOptions`:

> **Of 16 real VS Code manifest shapes, 2 load and 14 kill the whole extension.**

Killed: `menus` as object map · `views` · `viewsContainers` · `configuration` as array ·
`commands` as single object · `keybindings` as single object · `commands[].icon={light,dark}` ·
`languages[].firstLine` as string · `languages[].icon` as object ·
`debuggers[].configurationAttributes` as object · `debuggers[].initialConfigurations` as string ·
`problemMatchers[].fileLocation` as array · `problemMatchers[].pattern` as array or string.
An ESLint-shaped manifest (commands + menus + configuration) dies whole.

The three extensions currently installed load **only because none of them contributes commands,
menus or views**. This is not a latent edge case; it is the common case.

### 1.1 Three sections could never have worked

`contributes.menus`, `views` and `viewsContainers` are **object maps** in VS Code's schema —
keyed by menu id, container id and location respectively. There is no array form. The DTO types all
three as `List<T>` ([`:551`, `:557`, `:558`](../../../VisualGameStudio.Core/Abstractions/Services/IExtensionService.cs)).
`MenuContribution.MenuId` and `ViewContainerContribution.Location` correspond to no JSON field at
all — they are the map **key**. The repo already knew: `ExtensionService.cs:1558` carries the comment
`// menus is { "editor/context": [...], ... }`.

---

## 2. Why isolation alone is the wrong fix

The intuitive fix — isolate each contribution section so one bad section can't kill the rest — is
**insufficient and actively harmful on its own**:

- For the object-map sections it converts a **loud failure into permanent silent data loss**. The
  section is dropped every time, forever, and nothing surfaces beyond one warning line.
- `commands[].icon = {light,dark}` (used by `vscode.git` and most icon-bearing extensions) throws
  *inside* the commands array. Isolation drops the **entire `commands` section** — one of only two
  sections with a live consumer — leaving an empty command palette and a dead `onCommand`
  activation path, silently.

Measured comparison on the 16-shape fixture:

| Approach | Extensions loading | Commands preserved |
|---|---|---|
| Today | 2 / 16 | 2 |
| Isolation only | 16 / 16 | 14 |
| Shape fixes only | 14 / 16 | 14 |

Isolation loads more manifests, but the ones it "saves" are saved **empty**. Shape fixes recover the
actual content. They are complementary, and the order matters: **shape fixes are the fix; isolation
is the residual net.**

### 2.1 The real argument for isolation

Only **two** of thirteen sections are read from the DTO: `Commands`
([`:999`](../../../VisualGameStudio.ProjectSystem/Services/ExtensionService.cs), `:1499`) and
`Keybindings` (`:1023`, `:1520`). Themes, snippets, menus, grammars and languages are re-parsed from
raw JSON on paths that **already have their own per-section `try/catch`**. Six sections have no
readers at all.

So bugs 7 and 9 never killed anything by *losing data*. They killed by **throwing before the
extension was added at `:148`** — which is why the raw loaders never ran either.

**The converter's value is that it makes the existing raw-JSON isolation reachable.** That is the
strongest argument for it, and it is the one to lead with.

---

## 3. Design

Two sequenced workstreams, one spec.

### W1 — Shape fixes (first; delivers the measurable win)

Retype the DTO to the shapes VS Code actually emits. No new machinery.

| Property | Now | Becomes |
|---|---|---|
| `Menus` | `List<MenuContribution>` | `Dictionary<string, List<MenuContribution>>` (key populates `MenuId`) |
| `Views` | `List<ViewContribution>` | `Dictionary<string, List<ViewContribution>>` |
| `ViewsContainers` | `List<ViewContainerContribution>` | `Dictionary<string, List<ViewContainerContribution>>` |
| `Configuration` | single object | object-or-array tolerant |
| `CommandContribution.Icon` (`:573`) | `string` | `JsonElement?` (string or `{light,dark}`) |
| `LanguageContribution.Icon` (`:612`) | | `JsonElement?` |
| `LanguageContribution.FirstLine` (`:610`) | | `string?` |
| `DebuggerContribution.ConfigurationAttributes` (`:804`) | | `JsonElement?` |
| `DebuggerContribution.InitialConfigurations` (`:805`) | | `JsonElement?` |
| `ProblemMatcherContribution.FileLocation` (`:837`) | | string-or-array |
| `ProblemMatcherContribution.Pattern` (`:838`) | | `JsonElement?` |
| `ExtensionManifest.Repository` (`:439`) | `ExtensionRepository?` | string-or-object |

`Repository` is **identity-level, outside `contributes`** — `"repository": "https://github.com/o/r"`
is valid npm shorthand and kills the extension where no contribution converter can see it.
`VsixInstaller.cs:673` already types it `JsonElement?`.

> Several correct shapes already exist in `Core/Extensions/VSCodeExtension.cs` — `:113`
> (`string? FirstLine`), `:195` (`JsonElement? ConfigurationAttributes`). Fourth instance of the
> subsystem's rule: **when two implementations exist, the wired one is the broken one.**

### W2 — `ExtensionContributionsConverter` (second; the residual net)

A `sealed JsonConverter<ExtensionContributions>` applied via `[JsonConverter]` **on the class**,
beside `JsonSchemaTypeConverter`. `Read()` walks `contributes` property by property and binds each
recognised section independently; a failing section is left at its default and recorded on
`ExtensionContributions.LoadErrors`. The caller drains `LoadErrors` to `_outputService`.

The class attribute is the **default** binding: it covers both deserialize sites
([`:316`](../../../VisualGameStudio.ProjectSystem/Services/ExtensionService.cs) and `:859`)
without a registration step. It is not unforgettable — STJ resolves
property-attribute > `options.Converters` > type-attribute, so `options.Converters` remains a
deliberate override seam (useful for a strict-parsing test).

---

## 4. Mandatory implementation constraints

**These are not review comments. Each was reproduced by running code, and in each case the natural
implementation reproduces the bug being fixed — or something worse.**

1. **Forward `options` to every nested `Deserialize`.** Omitting it silently binds every string
   property to `""` with the correct element count and **zero recorded errors** — strictly worse
   than the bug being fixed. Cause: the no-arg overload uses `JsonSerializerOptions.Default`, which
   is case-sensitive and has no `CamelCase` policy.

2. **`JsonElement.ParseValue(ref reader)` OUTSIDE the per-section try.** A syntactically broken
   subtree strands the reader; the next iteration then throws `InvalidOperationException`, which
   isn't `JsonException`, escapes the converter and kills the whole extension. Only
   `element.Deserialize<T>(options)` goes inside the try. Prefer `JsonElement.ParseValue` over
   `JsonDocument.ParseValue` — identical positioning, detached, no `IDisposable` lifetime rule for a
   maintainer to get wrong.

3. **Hand-write `Write()`; never recurse.** A class-level attribute binds serialization too, and
   `Write` is abstract. The natural body `JsonSerializer.Serialize(writer, value, options)` dies with
   **`Stack overflow`, exit 253** — uncatchable in .NET Core, so it kills the IDE, not the extension.
   Write per section: `WritePropertyName("grammars")` then serialize the *element list* (element
   types carry no converter). Skip empty sections, never emit `LoadErrors`.
   **Never pass `ExtensionContributions` as the type argument to `Serialize`/`Deserialize` from
   inside its own converter** — say so in a code comment.

4. **Guard a non-object `contributes` as the first statement of `Read()`.** Real manifests carry
   `"contributes": []`, a string, or a number. Without the guard the converter itself becomes a new
   whole-extension kill path. Use `reader.TrySkip()`, not `Skip()` — `Skip` throws on partial JSON,
   latent today but live the moment anyone switches to `DeserializeAsync(Stream)`.
   `"contributes": null` never calls `Read` (`HandleNull` is false for reference types) and is
   already null-checked at `:894`.

5. **Match section names with `StringComparer.OrdinalIgnoreCase` against a hard-coded 13-name
   table.** `PropertyNameCaseInsensitive = true` ([`:20`](../../../VisualGameStudio.ProjectSystem/Services/ExtensionService.cs))
   means `"Commands"` binds today; an ordinal match loses it with **zero exception and zero
   LoadError**. `PropertyNamingPolicy` does **not** apply inside a converter's own token walk.
   Verified: camelCase(CLR name) equals the real VS Code key for all 13 — no divergences.
   ⚠ Trap: the property is `ViewsContainers` but the element type is `ViewContainerContribution`; a
   hand-written `case "viewContainers":` is a silent, test-passing no-op.

6. **Broaden the catch filter:**
   `catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or ArgumentException)`.
   A nested custom converter that throws non-`JsonException` surfaces unwrapped, and this codebase's
   stated remedy for shape drift is *"add a tolerant converter"*. `JsonElement` inspection throws
   `InvalidOperationException` — which is exactly why `:1127` and `:1197` are bare catches today.
   **Do not catch bare `Exception`** — that swallows `OperationCanceledException` and
   `OutOfMemoryException`.

7. **Skip unknown sections silently**, recording nothing. `LoadErrors` gets a non-null initializer
   and `[JsonIgnore]`; `ContributionLoadError` is a **string-only record** — no `Exception` field.

---

## 5. Error surfacing

**Decision: Output channel only.** One warning per dropped section, naming the extension, the
section, and the JSON error, and stating that the rest loaded.

Drained where `manifest.Contributes` is assigned
([`:894`](../../../VisualGameStudio.ProjectSystem/Services/ExtensionService.cs)) — **deliberately
outside the `if (total > 0)` gate at `:942`**. An extension whose only contribution was dropped has
`total == 0`, which is precisely the case where silence is worst.

**Severity must reflect the consumer map**, or eleven cost-free drops warn at the same volume as a
real loss:

| Sections | Consumer | Severity |
|---|---|---|
| `Commands`, `Keybindings` | read from the DTO | **warning — real functional loss** |
| `Themes`, `Grammars`, `Snippets`, `Menus`, `Languages` | re-parsed from raw JSON | informational — the raw path still runs |
| `Views`, `ViewsContainers`, `Configuration`, `Debuggers`, `TaskDefinitions`, `ProblemMatchers` | unread | informational |

> Accepted with eyes open: Output is the surface that already failed twice this year — both the
> `total > 0` gate and the attempt-vs-completion counter hid real failures there. Revisit if a third
> instance appears.

---

## 6. Testing

**Every test is seen red before it is seen green.** The fix is trivially reversible (comment out the
attribute; revert one DTO property), so there is no excuse. Three of last session's tests were
written after their fix and have never been observed failing — that is an argument, not evidence,
and it is not repeated here.

- **Prerequisite:** a public seam for `JsonOptions`. Tests must bind under the **real** options;
  a local copy that drifts proves nothing.
- **The 16-shape fixture**, table-driven, at `ExtensionManifest` scope. Asserts identity survives,
  **values** bind, and `LoadErrors` matches expectation.
- **Assert values, not counts.** `grammars[0].ScopeName == "text.html.basic"`, never
  `grammars.Count == 1` — a count assertion passes while every string is empty (constraint 1).
- **Degenerate `contributes`:** `null`, `[]`, `"s"`, `5` — each must survive with the extension intact.
- **Serialize round-trip** of a full `ExtensionManifest`. The only thing that catches the `Write()`
  stack overflow before a future caller does.
- **Name-table pin:** assert the converter's key set equals the camelCased names from
  `typeof(ExtensionContributions).GetProperties()`. Fails when a 14th section is added unprotected.
- **Real manifests** from `~/.vgs/extensions` — no regression.
- Message **wording is not asserted**; only that a warning is raised naming the section.

---

## 7. Scope

**In:** `ExtensionManifest` / `ExtensionContributions` in
`Core/Abstractions/Services/IExtensionService.cs`, bound via `ExtensionService.JsonOptions`.

**Out, deliberately:**
- The other three manifest models (`VsixInstaller.VsixManifest`, `ExtensionManager.packageJson`,
  `Core/Extensions/VSCodeExtension.cs`) — they belong to the Open VSX consolidation.
- `TextMateRegistrar`'s shared languages+grammars `try` — a separate boundary.
- Install pre-flight diagnostics at `:316` are dropped deliberately; that path only validates
  `Name`.

**Aggravated but deferred — must be stated, not left implicit.** `DiscoverExtensionsAsync` clears
only `_extensions` at `:111`. The five derived indexes (`_activationEventIndex`,
`_activatedLanguages`, `_contributedCommands`, `_contributedKeybindings`, `_contributedMenuItems`)
are never cleared, `:1526` appends unconditionally, and `:148` is a bare `Add` with no dedupe by Id.
Discovery runs after **every** install. More extensions parsing successfully therefore means more
duplicate keybindings and stale commands per refresh — and re-drained `LoadErrors` duplicate too.
**This fix will be blamed for a leak it merely exposes.** Either clear the five indexes and dedupe
at `:148` in the same change, or file it as a named chip before starting.
