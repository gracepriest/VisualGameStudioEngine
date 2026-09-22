title: Editor integrations
lede: BasicLang outside its own IDE — the VS 2022 CPS extension and the VS Code extension.
---
Both integrations exist for the same reason: `BasicLang.exe --lsp` is a standard language
server, so any LSP-capable editor can get IntelliSense with a thin client.

## Visual Studio 2022 — `BasicLang.VisualStudio` v2.4.0

A CPS (Common Project System) extension modelled on RemObjects Elements. Source:
`BasicLang.VisualStudio/src/BasicLang.VisualStudio/`.

**What it provides**

- A full project system through CPS, with `.blproj` support.
- LSP-based IntelliSense — it launches `BasicLang.exe --lsp`.
- A TextMate grammar for highlighting.
- A **BasicLang** submenu under **Tools**: Build · Run · Change Backend · Restart Server
  (Build is bound to `Ctrl+Shift+Alt+B`). *Change Backend* is informational only — it shows a
  message box listing the four backends and points you at Tools > Options > BasicLang > Compiler.
- Two editor context-menu commands: Go to Definition and Find All References (deliberately
  left unbound — editor-scoped F12/Shift+F12 would hijack VS's own commands).
- General and Compiler options pages.
- 4 project templates and 3 item templates.

**What it does not provide**

- A CPS debug launch provider — the debug APIs are not public.
- A bundled `BasicLang.exe` in the SDK `tools/` folder.
- A way to select the JavaScript backend. `CompilerBackend` offers only CSharp / MSIL / LLVM /
  CPlusPlus, and Build always passes one of those as `--target`, while the CLI takes
  `--target=csharp|cpp|javascript|llvm|msil` — five targets, four exposed.

**Building it**

```powershell
# VSIX — requires VS 2022 MSBuild, NOT dotnet build
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" `
  BasicLang.VisualStudio/src/BasicLang.VisualStudio/BasicLang.VisualStudio.csproj -p:Configuration=Release

# SDK NuGet package
dotnet pack BasicLang.VisualStudio/src/BasicLang.SDK -c Release

# or both in one step — packs the SDK, restores, then finds MSBuild via vswhere
BasicLang.VisualStudio/build.ps1 -Configuration Release
```

The build uses a manual pkgdef (`GeneratePkgDefFile=false`) plus custom MSBuild targets
(`CreateTemplateZips`, `AddTemplateZipsToVsix`, and `InjectTemplateZipsAsOpcParts` after packaging), because VSSDK template processing does not
work with SDK-style projects.

### Gotchas that cost real time

> [trap] **`Dependencies` vs `Prerequisites` in the vsixmanifest.** Never put a VS Setup
> component ID (e.g. `Microsoft.VisualStudio.Component.CoreEditor`) in `<Dependencies>` —
> the installer treats it as a reference to another VSIX and fails with
> `MissingReferencesException`. `Dependencies` = other VSIX extensions or .NET Framework.
> `Prerequisites` = VS Setup components.

> [trap] **A manual pkgdef must mirror every `[ProvideX]` attribute.** With
> `GeneratePkgDefFile=false` nothing is generated for you. `[ProvideMenuResource]` →
> `[$RootKey$\Menus]` — omit it and **the BasicLang menu never appears**.
> `[ProvideAutoLoad]` → `[$RootKey$\AutoLoadPackages\{context-guid}]`.
> `[ProvideProjectFactory]` → `[$RootKey$\Projects\{guid}]`.

> [trap] **Templates need `.vstman` manifests (VS 2017+).** Template scanning is no longer
> automatic. Ship `BasicLang.ProjectTemplates.vstman` / `BasicLang.ItemTemplates.vstman`
> and register them in the pkgdef. Inside a `.vstman`, `TemplateFileName` references the
> `.vstemplate` **inside** the zip, not the `.zip` name.

> [trap] **VsixUtil drops the template zips out of the finished VSIX.** VSSDK 17.9's
> `VsixUtil.exe` silently discards `*.zip` under `ProjectTemplates/` / `ItemTemplates/` even
> when they are emitted as `VSIXSourceItem`/`Content` with `IncludeInVSIX` and listed in
> `files.json`. The `InjectTemplateZipsAsOpcParts` target re-adds them **after**
> `CreateVsixContainer` by running `BuildSystem/InjectTemplateZips.ps1`, which uses
> `System.IO.Packaging` (not raw zip appending) so `[Content_Types].xml` and `manifest.json`
> stay consistent and the installer actually installs the zips.

> [trap] **Template `<ProjectType>` is `VisualBasic`, not `BasicLang`.** VS ignores unknown
> project-type values in the New Project dialog, so templates categorize under Visual Basic.
> This is settled. BasicLang identity is carried by the project-type **GUID**
> (`{95a8f3e1-1234-4567-8903-abcdef123456}`) plus pkgdef registration. If templates still
> do not appear: `devenv.exe /updateConfiguration`.

### Clean reinstall

1. Close all VS instances.
2. Delete the extension from `%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\Extensions\`.
3. Clear the MEF cache: `rd /s /q "%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\ComponentModelCache"`.
4. Install the VSIX, then `devenv.exe /updateConfiguration`.
5. Still broken: `devenv.exe /log`, then read
   `%APPDATA%\Microsoft\VisualStudio\17.0_*\ActivityLog.xml`.

Full notes: `docs/vs-extension-notes.md`. Research behind the approach:
`docs/vs2022-language-support-research.md`.

## Visual Studio Code — `vscode-basiclang`

Not in the `.sln`. TypeScript, packaged as a `.vsix`.

```text
vscode-basiclang/
├─ package.json                      manifest: language, grammar, snippets, commands, debugger, tasks
├─ src/extension.ts                  LSP client + debug-adapter factory + build/run (425 lines)
├─ syntaxes/basiclang.tmLanguage.json  TextMate grammar
├─ snippets/basiclang.json           snippets
├─ language-configuration.json       brackets, comments, auto-closing
├─ blproj-language-configuration.json
└─ basiclang-1.0.0.vsix              packaged build
```

It registers the BasicLang language, contributes the grammar and snippets, and starts
`BasicLang.exe --lsp` as its server. One setting, `basiclang.languageServerPath`, points at
`BasicLang.exe`; the rest are `enableSemanticHighlighting`, `enableInlayHints`,
`enableCodeLens`, `format.tabSize` and `format.insertSpaces` — there is **no** backend
setting. Recognised extensions: `.bl`, `.bas`, `.cls`, `.mod`, `.bli`, plus `.blproj`.

It also ships debugging, which the VS 2022 extension does not: `contributes.debuggers`
registers type `basiclang` with a launch configuration (`program`, `stopOnEntry`, `args`,
`cwd`) and breakpoints for the language, and `registerDebugAdapterDescriptorFactory` runs
`BasicLang.exe --debug-adapter`. Four commands (`restartServer`, `showOutput`, `build`,
`run`), two `basiclang` problem matchers and a `basiclang` task type round it out.

Documentation: `docs/IDE-Extensions.md`.

## Removed — do not resurrect

`VS.BasicLang`, the legacy MEF-based VSIX with an SDK-style workaround, has been deleted
from the repo. Old documentation still references it. The current VS extension is
`BasicLang.VisualStudio`.

## Automated maintenance agents

Four Claude Agent SDK applications (Python) maintain these areas, each with its own
`CLAUDE.md`, a set of `--profile` task profiles and an in-process MCP tool server — 28
profiles and 23 tools between them:

| Agent | Scope |
|---|---|
| `BasicLangAgent/` | Compiler testing and review |
| `IDEAgent/` | IDE feature development and testing |
| `EngineAgent/` | C++ / VB.NET engine sync maintenance |
| `VSExtensionAgent/` | VSIX build and validation |
