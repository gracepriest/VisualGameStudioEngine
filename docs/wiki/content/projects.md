title: Projects and templates
lede: Solution types, the `.blproj` format, the template catalogue, and the wizard that writes them.
---
## Solution types

A *solution type* fixes the backend and the shape of the build. The canonical list is
`SolutionTypes.All` in
`VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs`.

| Id | Name | What it produces |
|---|---|---|
| `dotnet` | .NET (C#) | .NET assemblies through the C# backend; full .NET ecosystem |
| `native` | BasicLang (Native) | Native code via C++ transpilation; no .NET runtime required. Can also reach .NET classes via `<NetProxy>` — a Native-AOT shim DLL is published and deployed beside the exe (P2a-2, **complete**) |
| `javascript` | JavaScript (Web) | A static web site — `index.html`, the script, its source map; `F5` previews it |
| `cpp` | C++ | User-authored C++ built directly with clang++/g++/MSVC — no BasicLang involved |
| `msil` | MSIL | <span class="pill ok">Maintained</span> Textual IL (`.il`) for `ilasm`; the build stops at the `.il` and says so |
| `llvm` | LLVM | <span class="pill mute">Unmaintained</span> |

All of them use `.blproj` for projects and `.blsln` for solutions. Source extension is
`.bas` except for the pure-C++ type, which is `.cpp`.

> [note] **MSIL is no longer unmaintained; LLVM still is.** `BasicLang/MSILBackend.cs` went
> from 2,136 to 5,356 lines (+3,492 / -272) across 24 commits, gaining properties, field
> initializers, arrays, `Try`/`Catch`, `Select Case`, instance methods (`Me`), `Shared`
> members, module-level variables, the .NET `Console` surface and native `List`/`Dictionary`.
> `VisualGameStudio.Tests/Msil/` (6 files, 4,279 lines, ~145 cases) is entirely new: it runs the
> emitted IL through a real `ilasm` and executes the result. `BasicLang/LLVMBackend.cs`, by
> contrast, has had zero commits and still has no test file of its own — the only tests naming
> `LLVMCodeGenerator` are two rejection guards in `ForeignFeatureGuardTests`.

> [trap] **Every id in `SolutionTypes.All` needs an explicit `TargetBackend` arm in
> `ProjectTemplateService.GenerateProjectFileContent`, and at least one template naming
> it in `SupportedSolutionTypes`.** A missing arm used to default to C#, which would have
> written `<TargetBackend>CSharp</TargetBackend>` into a JavaScript project. The default
> now throws, and `ProjectTemplateBackendMappingTests` pins both halves.

## The `.blproj` format

`.blproj` is XML. A generated project looks roughly like this:

```xml
<?xml version="1.0" encoding="utf-8"?>
<BasicLangProject Version="1.0">
  <PropertyGroup>
    <ProjectName>MyGame</ProjectName>
    <OutputType>Exe</OutputType>          <!-- Exe | Library | WinExe -->
    <RootNamespace>MyGame</RootNamespace>
    <TargetBackend>CSharp</TargetBackend> <!-- CSharp | MSIL | Cpp | LLVM | JavaScript -->
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Debug'">
    <OutputPath>bin\Debug</OutputPath>
    <DebugSymbols>true</DebugSymbols>
    <Optimize>false</Optimize>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <OutputPath>bin\Release</OutputPath>
    <DebugSymbols>false</DebugSymbols>
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Main.bas" />
    <Compile Include="Helpers.mod" />
  </ItemGroup>
</BasicLangProject>
```

Type-specific properties layer on top:

| Property | When |
|---|---|
| `<Language>Cpp</Language>`, `<CppStandard>`, `<CppToolchain>` | Pure C++ projects |
| `<UseWindowsForms>true</UseWindowsForms>` | `winforms-app` template |
| `<UseWPF>true</UseWPF>` | `wpf-app` template |
| `<NetProxy Include="System.Console" />` (in an `<ItemGroup>`) | Native projects that call .NET types — declares a type whose public surface is collected and rendered into `blnet_proxies.g.hpp` / `blnet_facade.g.hpp`. See [.NET interop](#/net-interop) |
| `<Reference Include="…">` with an optional `<HintPath>`, `<PackageReference Include="…" Version="…" />`, `<ProjectReference Include="…" />` | Assembly, NuGet and project-to-project references, all in `<ItemGroup>` |
| `<NetProxy Include="Full.Type.Name" />` | Native projects reaching a .NET class — the P2a-2 item group `NetSurfaceCollector` reads |
| `<NativeLib Include="VisualGameStudioEngine.lib" />` | Written automatically for the `cpp-game-app` template; `<IncludeDir>` and `<Define>` are the sibling C++ item elements |
| `<PackageReference Include="…" Version="…" />` | NuGet packages; the `avalonia-app` template writes `Avalonia.Desktop` and `Avalonia.Themes.Fluent` at `11.1.0` |
| `<ProjectReference Include="…" />`, `<Reference Include="…"><HintPath>…</HintPath></Reference>` | Project-to-project and raw assembly references |
| `<DefineConstants>` | Inside a configuration `PropertyGroup`, for `#If` |

> [note] A project **name** is user text and a `.blproj` is XML. `&` is a legal filename
> character, so an unescaped name once produced a malformed project file whose very first
> build died with *"'<' is an unexpected token."* Names are XML-escaped in the project
> file and left raw in generated source — a `.bas` is not XML. Pinned by
> `TemplateProjectNameEscapingTests`.

## Templates

The CLI catalogue lives in `BasicLang/ProjectSystem/TemplateEngine.cs`; the IDE's in
`ProjectTemplates.All` in `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs`
(`ProjectTemplateService.cs` holds no catalogue — it only generates the files for one).
List the CLI's with:

```powershell
IDE/BasicLang.exe new --list      # same listing as: IDE/BasicLang.exe list templates
```

> [note] The CLI catalogue is extensible. `TemplateEngine`'s constructor also loads every
> directory containing a `template.json` from `<app dir>/templates` and
> `~/.basiclang/templates`, keyed by the template's `ShortName` — so a user template sharing a
> built-in's short name **replaces** it, and a `--list` that disagrees with this page is the
> first thing to check. The IDE has the matching seam in `IProjectTemplateService.RegisterTemplate`.

| Template | Solution type | Produces |
|---|---|---|
| `console` (IDE `console-app`) | dotnet / msil / native / llvm | A console application (`Main.bas` + `Helpers.mod`) |
| `game` (IDE `game-app`) | dotnet / msil / native / llvm | A game project wired to the engine (`Main.bas`, `GameState.mod`, `Player.cls`) |
| `web` (IDE `web-site`) | javascript | One `Main.bas` typed against the DOM (`::document`, `Document`/`Element`/`DomEvent`); the **build** writes `index.html`, the script and its source map under `bin\` |
| `cpp-console` (IDE `cpp-console-app`) | cpp | A native console app (`main.cpp`) |
| `cpp-library` | cpp | A native C++ **static** library (`mathutils.cpp`/`.h`), archived to a `.lib`/`.a`; creates no solution file |
| `cpp-game` (IDE `cpp-game-app`) | cpp | A native game linking the engine import library via its C ABI (`Framework_*`) |
| `classlib` (IDE `class-library`) | dotnet / msil / native / llvm | A reusable library (`Library.mod` + `Types.cls`); creates no solution file |
| `webapi` (IDE `web-api`) | dotnet | A REST/HTTP API service |
| `test` (IDE `unit-test`) | dotnet / msil | A unit-test project; creates no solution file |
| `empty` | CLI only | A bare `.blproj` with no source files |
| `sln` | CLI only | A standalone `.blsln` stub, in the legacy plain-text format |
| IDE `winforms-app` | dotnet / msil | A Windows Forms desktop app (`<UseWindowsForms>`; `Main.bas` + `UIHelpers.bas`) |
| IDE `wpf-app` | dotnet / msil | A WPF desktop app (`<UseWPF>`) |
| IDE `avalonia-app` | dotnet / msil | A cross-platform Avalonia desktop app; pulls `Avalonia.Desktop` and `Avalonia.Themes.Fluent` 11.1.0 |

The VS 2022 extension ships its own set — 4 project templates and 3 item templates.
Those use `Microsoft.NET.Sdk` with a `ProjectTypeGuids` marker rather than this
`.blproj` shape; see [Editor integrations](#/editors).

## Creating projects

### From the CLI

```powershell
basiclang new console -n MyApp
basiclang new game -n MyGame
basiclang new web -n MySite
basiclang new classlib --name MyLibrary --output ./libs
```

> [trap] **`new` cannot choose a backend.** Its only options are `-n`/`--name` and
> `-o`/`--output`. `console`, `classlib`, `game`, `empty`, `webapi` and `test` all ship a
> hardcoded `<TargetBackend>CSharp</TargetBackend>`; `web` ships `JavaScript` and the three
> `cpp-*` templates ship `Cpp`. To get a native or MSIL project from a CLI template, edit
> `<TargetBackend>` afterwards — or create it from the IDE wizard, which does offer the choice.

### From the IDE

**File → New Project** opens a two-window wizard. The first window (`NewProjectSelectView`)
picks a **language** (BasicLang or C++), a **backend**, an optional platform filter and the
**template**; the second (`NewProjectConfigureView`) takes the name, location, target
framework, the C++ standard and options such as a custom namespace. For BasicLang the backend
list is C# (.NET), MSIL, Native C++, JavaScript (Web) and LLVM, each mapping to a real
`SolutionType`; for C++ it is a toolchain choice (clang++ / g++ / MSVC) over the single `cpp`
solution type, greyed out with "(not installed)" when `ICppToolchainProbe` finds it missing.
`NewProjectWizardViewModel.cs` drives it;
a template, name and location. `NewProjectWizardViewModel.cs` drives it;
`NewSolutionViewModel.cs` handles solutions; `SolutionWizardMapper.cs` maps wizard choices
onto template-service options. The design is recorded in
`docs/superpowers/plans/2026-07-13-new-project-wizard-two-window.md`.

## Solutions

`.blsln` is XML rooted at `<BasicLangSolution Version="1.0">` — a `PropertyGroup` carrying
`SolutionName` and `DefaultProject`, then a `<Projects>` list of
`<Project Name= Path= Type=>` entries whose `<ProjectReference>` children name other projects
by name, plus an optional `<Folders>` section. `SolutionSerializer.cs` reads and writes it and
throws on any other root element; `SolutionService.cs` loads and saves them;
`BlprojReferenceWriter.cs` writes project-to-project references;
`AddProjectReferenceViewModel.cs` is the dialog. Closing a solution and the new-solution
flow are specified in
`docs/superpowers/specs/2026-07-23-solution-ux-close-project-and-new-solution-wizard-design.md`.

> [trap] **There are two `.blsln` formats and only the IDE's is XML.** The CLI's `sln`
> template still emits the legacy plain text (`BasicLang Solution File, Format Version 1.0`
> with `#` comment lines), which `SolutionSerializer.LoadAsync` cannot parse — `XDocument.Parse`
> throws before the root-element check. This is why `TemplateEngine.IsXmlFile` keys on `.blproj`
> alone and deliberately excludes `.blsln`: escaping a plain-text file would put a literal
> `&amp;` in it. A solution to open in the IDE must come from the IDE, not from `basiclang new sln`.

## Build configuration

Debug and Release are written into every generated project. In the IDE, the build
configuration dialog (`BuildConfigurationDialogViewModel.cs`) edits them, and
`LaunchConfigurationService.cs` plus `LaunchConfigurationDialogViewModel.cs` own run
configurations — which executable, which arguments, which working directory.

## Packages

NuGet packages are declared as `<PackageReference>` in the `.blproj` and restored by
`BasicLang/ProjectSystem/PackageManager.cs`. They are consumed on **two** build paths: the
managed C# one (`BuildService` phase 1, matching `basiclang build`), and the **native** one,
where `CppProjectBuilder.RestorePackagesForClosure` hands the restored assemblies to the .NET
proxy closure. MSIL, LLVM and JavaScript builds stop at generated source, so a package
restored for them reaches nothing. Commands:
`BasicLang/ProjectSystem/PackageManager.cs`:

```powershell
basiclang add package Newtonsoft.Json        # see the trap below — --version is unreachable
basiclang remove package Newtonsoft.Json
basiclang list packages
basiclang restore
basiclang search json
```
