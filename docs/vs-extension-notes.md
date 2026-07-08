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

**4. Template `<ProjectType>` is `VisualBasic`, NOT `BasicLang`.** VS ignores unknown
project-type values in the New Project dialog, so the templates categorize under Visual
Basic. This is settled: all 4 project + 3 item `.vstemplate` files and both `.vstman`
manifests use `<ProjectType>VisualBasic</ProjectType>` + `<LanguageTag>visualbasic</LanguageTag>`.
The `<ProjectType>BasicLang</ProjectType>` recorded in the old root changelog was a
superseded v2.2.0 intermediate state that VS never surfaced. BasicLang identity is carried
instead by the project-type **GUID** — `<ProjectTypeGuids>{95a8f3e1-1234-4567-8903-abcdef123456}</ProjectTypeGuids>`
in each `Project.blproj` — plus the pkgdef project-factory / template-dir registration; the
`<ProjectType>` tag only controls which category the template appears under. If templates
still don't appear: `devenv.exe /updateConfiguration`.

**5. Templates use `Microsoft.NET.Sdk`** with a `ProjectTypeGuids` + `<IsBasicLangProject>true</IsBasicLangProject>`
marker and `<BasicLangCompile Include="**\*.bas" />` — not a custom `BasicLang.SDK` reference.

## Clean reinstall (when the extension misbehaves)

1. Close all VS instances.
2. Delete the old extension from `%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\Extensions\`.
3. Clear the MEF cache: `rd /s /q "%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\ComponentModelCache"`.
4. Install the VSIX, then `devenv.exe /updateConfiguration`.
5. On stubborn failures: `devenv.exe /log`, then read `%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml`.
