# BasicLang Visual Studio 2022 Extension — Build & Troubleshooting Notes

Hard-won details for `BasicLang.VisualStudio` (the CPS-based VS 2022 VSIX). Extracted
from the project changelog so the root `CLAUDE.md` can stay a durable operating guide.
This is reference material — consult it when working on the extension, not every session.

> The legacy `VS.BasicLang` VSIX (MEF-based, SDK-style workaround) has been removed from
> the repo. Everything below refers to the current CPS extension, `BasicLang.VisualStudio`.

## What it is

A CPS (Common Project System) VS 2022 extension modeled on RemObjects Elements: project
system integration, LSP-based IntelliSense (launches `BasicLang.exe --lsp`), a BasicLang
menu (Build / Run / Change Backend / Restart Server), Options pages, and project/item
templates. Source: `BasicLang.VisualStudio/src/BasicLang.VisualStudio/`.

Not implemented: a CPS debug launch provider (debug APIs aren't public), and `BasicLang.exe`
is not bundled in the SDK `tools/` folder.

## Build

```bash
# VSIX — requires VS 2022 MSBuild, NOT `dotnet build`
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" \
  BasicLang.VisualStudio/src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj -p:Configuration=Release

# SDK NuGet package
dotnet pack BasicLang.VisualStudio/src/BasicLang.SDK -c Release
```

Outputs: `.../BasicLang.VisualStudio.vsix` and `.../BasicLang.SDK/bin/Release/BasicLang.SDK.1.0.0.nupkg`.

The build uses a manual pkgdef (`GeneratePkgDefFile=false`) plus custom MSBuild targets
(`CreateTemplateZips`, `AddTemplatesToVsix`) because VSSDK template processing does not
work with SDK-style projects.

## Gotchas that cost real time

**1. `Dependencies` vs `Prerequisites` in the vsixmanifest.** Never put a VS Setup
component ID (e.g. `Microsoft.VisualStudio.Component.CoreEditor`) in `<Dependencies>` — the
installer treats it as a reference to another VSIX and fails with `MissingReferencesException`.
- `Dependencies` = other VSIX extensions or .NET Framework (`Microsoft.Framework.NDP`, `[4.8,)`).
- `Prerequisites` = VS Setup components (CoreEditor `[17.0,)`, Roslyn, etc.).

**2. Manual pkgdef must mirror every `[ProvideX]` attribute.** With
`GeneratePkgDefFile=false`, nothing is auto-generated — each package attribute needs a
hand-written pkgdef section:
- `[ProvideMenuResource]` → `[$RootKey$\Menus]` (missing this = **BasicLang menu never appears**)
- `[ProvideAutoLoad]` → `[$RootKey$\AutoLoadPackages\{context-guid}]`
- `[ProvideProjectFactory]` → `[$RootKey$\Projects\{guid}]`
- Project templates → `[$RootKey$\Projects\{guid}]` + `[$RootKey$\NewProjectTemplates\TemplateDirs\...]`

**3. Templates need `.vstman` manifests (VS 2017+).** Template scanning is no longer
automatic; ship `BasicLang.ProjectTemplates.vstman` / `BasicLang.ItemTemplates.vstman` and
register `[$RootKey$\TemplateEngine\Templates\...]` in the pkgdef. In a `.vstman`,
`TemplateFileName` references the `.vstemplate` **inside** the zip, not the `.zip` name.

**4. Template `<ProjectType>` — VERIFY before trusting either source.** VS only shows
custom project types in New Project once the pkgdef project-factory + template-dir
registration is present. Two records in this repo disagree on the value:
- The old root changelog said the working templates used `<ProjectType>BasicLang</ProjectType>`.
- `VSExtensionAgent/CLAUDE.md` says it must be `VisualBasic` (VS ignores unknown types).

Check the actual `.vstemplate` files before relying on either. If templates don't appear:
`devenv.exe /updateConfiguration`.

**5. Templates use `Microsoft.NET.Sdk`** with a `ProjectTypeGuids` + `<IsBasicLangProject>true</IsBasicLangProject>`
marker and `<BasicLangCompile Include="**\*.bas" />` — not a custom `BasicLang.SDK` reference.

## Clean reinstall (when the extension misbehaves)

1. Close all VS instances.
2. Delete the old extension from `%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\Extensions\`.
3. Clear the MEF cache: `rd /s /q "%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\ComponentModelCache"`.
4. Install the VSIX, then `devenv.exe /updateConfiguration`.
5. On stubborn failures: `devenv.exe /log`, then read `%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml`.
