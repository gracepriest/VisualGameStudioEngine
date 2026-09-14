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
| `native` | BasicLang (Native) | Native code via C++ transpilation; no runtime required |
| `javascript` | JavaScript (Web) | A static web site — `index.html`, the script, its source map; `F5` previews it |
| `cpp` | C++ | User-authored C++ built directly with clang++/g++/MSVC — no BasicLang involved |
| `msil` | MSIL | <span class="pill mute">Unmaintained</span> |
| `llvm` | LLVM | <span class="pill mute">Unmaintained</span> |

All of them use `.blproj` for projects and `.blsln` for solutions. Source extension is
`.bas` except for the pure-C++ type, which is `.cpp`.

> [trap] **Every id in `SolutionTypes.All` needs an explicit `TargetBackend` arm in
> `ProjectTemplateService.GenerateProjectFileContent`, and at least one template naming
> it in `SupportedSolutionTypes`.** A missing arm used to default to C#, which would have
> written `<TargetBackend>CSharp</TargetBackend>` into a JavaScript project. The default
> now throws, and `ProjectTemplateBackendMappingTests` pins both halves.

## The `.blproj` format

`.blproj` is XML. A generated project looks roughly like this:

```xml
<Project>
  <PropertyGroup>
    <ProjectName>MyGame</ProjectName>
    <OutputType>Exe</OutputType>          <!-- Exe | Library | WinExe -->
    <RootNamespace>MyGame</RootNamespace>
    <TargetBackend>CSharp</TargetBackend> <!-- CSharp | Cpp | JavaScript | … -->
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
    <Compile Include="Program.bas" />
  </ItemGroup>
</Project>
```

Type-specific properties layer on top:

| Property | When |
|---|---|
| `<Language>Cpp</Language>`, `<CppStandard>`, `<CppToolchain>` | Pure C++ projects |
| `<UseWindowsForms>true</UseWindowsForms>` | `winforms-app` template |
| `<UseWPF>true</UseWPF>` | `wpf-app` template |

> [note] A project **name** is user text and a `.blproj` is XML. `&` is a legal filename
> character, so an unescaped name once produced a malformed project file whose very first
> build died with *"'<' is an unexpected token."* Names are XML-escaped in the project
> file and left raw in generated source — a `.bas` is not XML. Pinned by
> `TemplateProjectNameEscapingTests`.

## Templates

The CLI catalogue lives in `BasicLang/ProjectSystem/TemplateEngine.cs`; the IDE's in
`ProjectTemplateService.cs`. List them with:

```powershell
IDE/BasicLang.exe new --list
```

| Template | Solution type | Produces |
|---|---|---|
| `console` | dotnet / native | A console application |
| `game` | dotnet / native | A game project wired to the engine |
| `web` (`web-site`) | javascript | A static site with an HTML page and an entry module |
| C++ console | cpp | A native console app |
| C++ library | cpp | A native library |
| C++ game | cpp | A native game linking the engine |

The VS 2022 extension ships its own set — 4 project templates and 3 item templates.
Those use `Microsoft.NET.Sdk` with a `ProjectTypeGuids` marker rather than this
`.blproj` shape; see [Editor integrations](#/editors).

## Creating projects

### From the CLI

```powershell
basiclang new console -n MyApp
basiclang new game -n MyGame
basiclang new web -n MySite
```

### From the IDE

**File → New Project** opens a two-window wizard: pick a solution type and backend, then
a template, name and location. `NewProjectWizardViewModel.cs` drives it;
`NewSolutionViewModel.cs` handles solutions; `SolutionWizardMapper.cs` maps wizard choices
onto template-service options. The design is recorded in
`docs/superpowers/plans/2026-07-13-new-project-wizard-two-window.md`.

## Solutions

`.blsln` groups projects. `SolutionService.cs` loads and saves them;
`BlprojReferenceWriter.cs` writes project-to-project references;
`AddProjectReferenceViewModel.cs` is the dialog. Closing a solution and the new-solution
flow are specified in
`docs/superpowers/specs/2026-07-23-solution-ux-close-project-and-new-solution-wizard-design.md`.

## Build configuration

Debug and Release are written into every generated project. In the IDE, the build
configuration dialog (`BuildConfigurationDialogViewModel.cs`) edits them, and
`LaunchConfigurationService.cs` plus `LaunchConfigurationDialogViewModel.cs` own run
configurations — which executable, which arguments, which working directory.

## Packages

NuGet packages are available on the managed backend, through
`BasicLang/ProjectSystem/PackageManager.cs`:

```powershell
basiclang add package Newtonsoft.Json --version 13.0.3
basiclang remove package Newtonsoft.Json
basiclang list packages
basiclang restore
basiclang search json
```
