# WinFormsMetadataDump

Regenerates `VisualGameStudio.Tests/Data/winforms-metadata.json`, the TEST ORACLE that
`WinFormsCatalogParityTests` compares `BasicLang/Forms/FormControlCatalog.cs` against
(spec `docs/superpowers/specs/2026-09-25-property-grid-vs-parity-design.md` §2.6, decision D4).

**The oracle is never the author.** Nothing reads the JSON at build time and no code is generated
from it. The catalog stays hand-written; the parity test reports where it disagrees with WinForms.

## Run (Windows only — it needs the WindowsDesktop runtime)

From the repo root:

    dotnet run --project tools/WinFormsMetadataDump/WinFormsMetadataDump.csproj -c Release -- VisualGameStudio.Tests/Data/winforms-metadata.json

Then run the parity fixture:

    dotnet build VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release
    dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~WinFormsCatalogParityTests"

## When to regenerate

- A catalog row is added for a KIND not yet in `Program.cs`'s `Types` table (the parity fixture's
  completeness test fails and names it). Add the kind there first.
- The WindowsDesktop runtime version changes.

The JSON records `framework` and `windowsForms` so a diff shows which runtime produced it.

## Culture

The tool pins `CurrentCulture` and `CurrentUICulture` to the invariant culture before it reads
anything. `Category` and `Description` are LOCALISED through `CurrentUICulture`, and the
WindowsDesktop runtime ships German, French, Japanese, … resources, so without the pin a
regeneration on a non-English Windows would write `"Verhalten"` where the catalog says `Behavior`.
Invariant resolves to the neutral (English) resources. Every value's text form is invariant as well
(`800, 450`, never `800; 450`), so the file is byte-identical whatever machine regenerates it.

## The Form is measured top-level

Every control is PARENTED (into a `Panel`) to find out whether it inherits `Font`, `ForeColor`,
`Cursor` or `RightToLeft` from its parent (`ambient`). The Form is not: the designer's root Form is
top-level, which is what Visual Studio's Properties window shows, and a top-level form has no
parent to inherit from — its values are its own and classify as `attribute`/`reset`/`serialized`.

## What `defaultKind` means

| kind | meaning | the catalog's `Default` must be |
|---|---|---|
| attribute | a `[DefaultValue]` exists | equal to it |
| reset | no attribute; `ShouldSerializeValue` is false on a fresh instance, so the designer would not write it — its current value is the type's default (`ResetValue` is never called) | equal to it |
| ambient | inherited from the parent (measured by parenting) | null |
| volatile | two fresh instances disagree (a clock read) | null |
| serialized | no attribute and a fresh instance would be written | null |
| collection | a collection | null |
| unreadable | the getter or `ShouldSerializeValue` threw on a fresh instance: nothing was measured | not judged — the row needs an `OracleExemption` or a look |

A value the tool has no text form for (a complex object such as `FlatAppearance`) is recorded as
`null`, never as its type name.

A read that depends on the object being PARENTED or SHOWN (an unparented ToolStripItem reports
`Visible=False`; a Form starts hidden) is wrong as a default; the catalog row says so with
`OracleExemption: "<reason>"` rather than the test carrying a list.

Not in `VisualGameStudioEngine.sln`, deliberately.
