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

## What `defaultKind` means

| kind | meaning | the catalog's `Default` must be |
|---|---|---|
| attribute | a `[DefaultValue]` exists | equal to it |
| reset | no attribute; the fresh instance holds what Reset gives back | equal to it |
| ambient | inherited from the parent (measured by parenting) | null |
| volatile | two fresh instances disagree (a clock read) | null |
| serialized | no attribute and a fresh instance would be written | null |
| collection | a collection | null |

A read that depends on the object being PARENTED or SHOWN (an unparented ToolStripItem reports
`Visible=False`; a Form starts hidden) is wrong as a default; the catalog row says so with
`OracleExemption: "<reason>"` rather than the test carrying a list.

Not in `VisualGameStudioEngine.sln`, deliberately.
