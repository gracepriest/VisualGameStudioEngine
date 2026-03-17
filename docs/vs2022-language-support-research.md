# Visual Studio 2022 Language Support Research Report

**Date:** March 17, 2026
**Goal:** Make BasicLang a first-class language in Visual Studio 2022
**Current Extension Version:** BasicLang.VisualStudio v2.4.0

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [Component 1: Syntax Highlighting (TextMate + Language Configuration)](#2-syntax-highlighting)
3. [Component 2: Language Server Protocol (LSP) Client](#3-lsp-client)
4. [Component 3: Project System (CPS)](#4-project-system-cps)
5. [Component 4: Project Templates](#5-project-templates)
6. [Component 5: Debug Launch Provider](#6-debug-launch-provider)
7. [Component 6: VSIX Packaging Best Practices](#7-vsix-packaging)
8. [Known Limitations and Workarounds](#8-known-limitations)
9. [How Other Languages Do It](#9-how-other-languages-do-it)
10. [Recommendations for BasicLang](#10-recommendations-for-basiclang)
11. [Sources](#11-sources)

---

## 1. Architecture Overview

A first-class language in VS 2022 requires these components:

```
┌──────────────────────────────────────────────────────────────┐
│                    VSIX Extension Package                     │
│                                                              │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────────┐   │
│  │  TextMate    │  │ Language     │  │ Language           │   │
│  │  Grammar     │  │ Configuration│  │ Configuration      │   │
│  │  (.json)     │  │ (.json)      │  │ (.pkgdef)          │   │
│  └──────┬───────┘  └──────┬───────┘  └────────┬──────────┘   │
│         └─────────────────┴───────────────────┘              │
│                    Syntax Highlighting                        │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  ILanguageClient (or LanguageServerProvider)         │    │
│  │  - Content type definition                           │    │
│  │  - Server lifecycle management                       │    │
│  │  - LSP capability negotiation                        │    │
│  └──────────────────────────────────────────────────────┘    │
│                    IntelliSense / Language Features           │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  CPS Project System                                  │    │
│  │  - Project capabilities                              │    │
│  │  - MSBuild targets                                   │    │
│  │  - IDebugLaunchProvider                              │    │
│  │  - Solution Explorer tree provider                   │    │
│  └──────────────────────────────────────────────────────┘    │
│                    Project/Build/Debug                        │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐    │
│  │  Project & Item Templates                            │    │
│  │  - .vstemplate files with LanguageTag                │    │
│  │  - .vstman manifest files                            │    │
│  │  - Template directory registration in pkgdef         │    │
│  └──────────────────────────────────────────────────────┘    │
│                    New Project Dialog                         │
└──────────────────────────────────────────────────────────────┘
```

There are **two extensibility models** to choose from:

| Approach | Model | Pros | Cons |
|----------|-------|------|------|
| **VSSDK (Legacy)** | In-process MEF/COM | Full API access, mature, documented | Complex, crash risks, requires restart |
| **VisualStudio.Extensibility** | Out-of-process | Modern, crash-safe, no restart | Limited API surface, newer, less documented |

**Recommendation for BasicLang:** Continue using VSSDK (in-process) model. The new VisualStudio.Extensibility model supports `LanguageServerProvider` but does not yet have CPS project system integration, debug launch providers, or custom project factories. These are all needed for first-class language support.

---

## 2. Syntax Highlighting

### Two Approaches

| Approach | When to Use |
|----------|-------------|
| **TextMate Grammar** | File-based syntax coloring; no code needed; shared with VS Code |
| **MEF Classifier** | Programmatic classification; more control; harder to maintain |

**TextMate is the recommended approach** for VS 2022. It allows sharing grammar definitions between VS Code and VS 2022.

### How to Register a TextMate Grammar

**Step 1: Create grammar file** (`Grammars/basiclang.tmLanguage.json`)

The grammar file uses standard TextMate grammar format (same as VS Code). BasicLang already has this at `LanguageService/BasicLangGrammar.json`.

**Step 2: Register in pkgdef**

```pkgdef
; Point VS to the grammar repository folder
[$RootKey$\TextMate\Repositories]
"BasicLang"="$PackageFolder$\Grammars"

; Map TextMate scope to language configuration
[$RootKey$\TextMate\LanguageConfiguration\GrammarMapping]
"source.basiclang"="$PackageFolder$\basiclang-language-configuration.json"

; Map content type to language configuration
[$RootKey$\TextMate\LanguageConfiguration\ContentTypeMapping]
"basiclang"="$PackageFolder$\basiclang-language-configuration.json"
```

Reference: [Microsoft VSSDK-Extensibility-Samples TextmateGrammar](https://github.com/microsoft/VSSDK-Extensibility-Samples/blob/master/TextmateGrammar/src/languages.pkgdef)

**Step 3: Add a Language Configuration file** (`basiclang-language-configuration.json`)

This supplements the TextMate grammar with local (non-LSP) editor behavior:

```json
{
  "comments": {
    "lineComment": "'",
    "blockComment": ["/*", "*/"]
  },
  "brackets": [
    ["(", ")"],
    ["[", "]"],
    ["{", "}"]
  ],
  "autoClosingPairs": [
    { "open": "(", "close": ")" },
    { "open": "[", "close": "]" },
    { "open": "{", "close": "}" },
    { "open": "\"", "close": "\"", "notIn": ["string"] }
  ],
  "surroundingPairs": [
    ["(", ")"],
    ["[", "]"],
    ["{", "}"],
    ["\"", "\""]
  ],
  "indentationRules": {
    "increaseIndentPattern": "^\\s*(Sub|Function|If|For|While|Class|Module|Interface|Enum|Select|Try|With|Do|Namespace|Property|Get|Set|Structure)\\b",
    "decreaseIndentPattern": "^\\s*(End Sub|End Function|End If|Next|Wend|End Class|End Module|End Interface|End Enum|End Select|Catch|Finally|End Try|End With|Loop|End Namespace|End Property|End Get|End Set|End Structure|Else|ElseIf|Case)\\b"
  },
  "wordPattern": "(-?\\d*\\.\\d\\w*)|([^\\`\\~\\!\\@\\#\\%\\^\\&\\*\\(\\)\\-\\=\\+\\[\\{\\]\\}\\\\\\|\\;\\:\\'\\\"\\,\\.\\<\\>\\/\\?\\s]+)"
}
```

**Important property settings for all TextMate files:**
- Build Action = `Content`
- Include in VSIX = `True`
- Copy to output = `Copy always`

Reference: [Language Configuration documentation](https://learn.microsoft.com/en-us/visualstudio/extensibility/language-configuration?view=vs-2022)

### Current State in BasicLang Extension

The extension has `BasicLangGrammar.json` but is **missing**:
- Language Configuration file (`language-configuration.json`) -- would provide bracket matching, auto-closing, comment toggling, and indentation rules without LSP round-trips
- Proper TextMate repository registration in pkgdef (currently only has content type registration)

---

## 3. LSP Client

### Two Approaches

| Approach | NuGet Package | Model |
|----------|--------------|-------|
| **ILanguageClient** (VSSDK) | `Microsoft.VisualStudio.LanguageServer.Client` | In-process MEF export |
| **LanguageServerProvider** (New) | `Microsoft.VisualStudio.Extensibility` | Out-of-process |

### ILanguageClient (VSSDK) -- Current and Recommended Approach

**Required NuGet Package:** `Microsoft.VisualStudio.LanguageServer.Client`

> **Warning:** Do not update the transitive `Newtonsoft.Json` and `StreamJsonRpc` packages to versions newer than what ships with the target VS version, or the extension will break.

**Core Implementation Pattern:**

```csharp
// 1. Content Type Definition (required for LSP activation)
public class BasicLangContentDefinition
{
    [Export]
    [Name("basiclang")]
    [BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition BasicLangContentType;

    [Export]
    [FileExtension(".bas")]
    [ContentType("basiclang")]
    internal static FileExtensionToContentTypeDefinition BasFileExtension;
}

// 2. Language Client
[ContentType("basiclang")]
[Export(typeof(ILanguageClient))]
public class BasicLangLanguageClient : ILanguageClient, ILanguageClientCustomMessage2
{
    public string Name => "BasicLang Language Server";

    // Configuration sections for workspace settings
    public IEnumerable<string> ConfigurationSections => new[] { "basiclang" };

    // Initialization options sent to server in "initialize" request
    public object InitializationOptions => new { ... };

    // File patterns to watch
    public IEnumerable<string> FilesToWatch => new[] { "**/*.bas", "**/*.blproj" };

    public event AsyncEventHandler<EventArgs> StartAsync;
    public event AsyncEventHandler<EventArgs> StopAsync;

    public async Task<Connection> ActivateAsync(CancellationToken token)
    {
        // Start the language server process
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = serverPath,
            Arguments = "--lsp",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        return new Connection(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
    }

    public async Task OnLoadedAsync()
    {
        // Must call StartAsync to activate the server
        await StartAsync.InvokeAsync(this, EventArgs.Empty);
    }
}
```

**Key content type base definition:** The content type MUST derive from `CodeRemoteContentDefinition.CodeRemoteContentTypeName` for LSP to work. This is already correct in the BasicLang extension.

### LSP Features Supported by VS 2022

| Feature | Supported |
|---------|-----------|
| textDocument/completion | Yes |
| textDocument/hover | Yes |
| textDocument/signatureHelp | Yes |
| textDocument/definition | Yes |
| textDocument/references | Yes |
| textDocument/documentHighlight | Yes |
| textDocument/documentSymbol | Yes |
| textDocument/formatting | Yes |
| textDocument/rangeFormatting | Yes |
| textDocument/rename | Yes |
| textDocument/codeAction | Yes |
| textDocument/publishDiagnostics | Yes |
| workspace/symbol | Yes |
| workspace/executeCommand | Yes |
| workspace/applyEdit | Yes |
| textDocument/codeLens | **No** |
| textDocument/documentLink | **No** |
| textDocument/onTypeFormatting | **No** |

### Server Distribution Options

1. **Embed in VSIX as content files** (recommended for BasicLang)
2. Create an MSI installer
3. Provide instructions on Marketplace

### Server Lifecycle

- VS calls `OnLoadedAsync()` when extension loads
- Extension calls `StartAsync` delegate to signal server should start
- VS calls `ActivateAsync()` to actually start the server
- VS calls `OnServerInitializedAsync()` after handshake completes
- Server is stopped on solution close, solution switch, or VS shutdown
- Server lifecycle is tied to the workspace/editor session

### Middle Layer (Message Interception)

Implement `ILanguageClientMiddleLayer` to intercept/modify LSP messages:

```csharp
public class DiagnosticsFilterMiddleLayer : ILanguageClientMiddleLayer
{
    public bool CanHandle(string methodName)
    {
        return methodName == "textDocument/publishDiagnostics";
    }

    public async Task HandleNotificationAsync(string methodName, JToken methodParam,
        Func<JToken, Task> sendNotification)
    {
        // Filter or modify notifications before they reach VS
        await sendNotification(methodParam);
    }

    public async Task<JToken> HandleRequestAsync(string methodName, JToken methodParam,
        Func<JToken, Task<JToken>> sendRequest)
    {
        // Filter or modify requests/responses
        return await sendRequest(methodParam);
    }
}
```

### Diagnostic Tracing

LSP trace logs are written to `%temp%\VisualStudio\LSP\[LanguageClientName]-[Datetime].log` when tracing is enabled via workspace settings:

```json
{
    "basiclang.trace.server": "Verbose"
}
```

### New VisualStudio.Extensibility Model (Alternative Future Approach)

Starting with VS 2022 17.9, there is a new out-of-process model. Key differences:

```csharp
[VisualStudioContribution]
public class BasicLangServerProvider : LanguageServerProvider
{
    // Define custom document type
    [VisualStudioContribution]
    internal static DocumentTypeConfiguration BasicLangDocumentType => new("basiclang")
    {
        FileExtensions = new[] { ".bas", ".bl" },
        BaseDocumentType = LanguageServerBaseDocumentType,
    };

    public override LanguageServerProviderConfiguration LanguageServerProviderConfiguration =>
        new("BasicLang Language Server", new[]
        {
            DocumentFilter.FromDocumentType(BasicLangDocumentType),
        });

    public override Task<IDuplexPipe?> CreateServerConnectionAsync(CancellationToken cancellationToken)
    {
        (Stream PipeToServer, Stream PipeToVS) = FullDuplexStream.CreatePair();
        // Connect PipeToServer to the language server process
        return Task.FromResult<IDuplexPipe?>(
            new DuplexPipe(PipeToVS.UsePipeReader(), PipeToVS.UsePipeWriter()));
    }
}
```

Benefits: out-of-process (crash isolation), no restart needed for install. Not recommended yet for BasicLang because it lacks project system and debug provider APIs.

Reference: [LanguageServerProvider documentation](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/language-server-provider/language-server-provider?view=vs-2022)

### Current State in BasicLang Extension

The `BasicLangLanguageClient.cs` implementation is well-structured and follows best practices. **Improvements needed:**

1. **Bundle BasicLang.exe in the VSIX** -- currently searches multiple paths; embedding it would ensure it is always available
2. **Add settings support** -- provide a `BasicLangSettings.json` file with defaults, registered via pkgdef under `[$RootKey$\OpenFolder\Settings\VSWorkspaceSettings\BasicLang]`
3. **Improve restart logic** -- current `RestartServerAsync()` uses `Task.Delay(500)` which is fragile; consider using the server process exit event
4. **Add LSP tracing support** -- expose the trace.server setting for debugging

---

## 4. Project System (CPS)

### Background

The Common Project System (CPS) is the extensible project system that ships with VS 2022. It is used by C#, VB, F#, C++, and many other project types. It uses MEF for extensibility.

**Key Repository:** [microsoft/VSProjectSystem](https://github.com/microsoft/VSProjectSystem)

### Required Components for a CPS-Based Language

#### 4.1 Project Capabilities

Capabilities drive CPS behavior. They are declared in MSBuild and consumed by MEF exports.

**In your MSBuild targets file** (`BasicLang.targets`):

```xml
<Project>
  <ItemGroup>
    <ProjectCapability Include="BasicLang" />
    <ProjectCapability Include="DependenciesTree" />
    <ProjectCapability Include="LaunchProfiles" />
  </ItemGroup>
</Project>
```

**In your MEF extension**, use `[AppliesTo("BasicLang")]`:

```csharp
[Export]
[AppliesTo("BasicLang")]
[ProjectTypeRegistration(
    BasicLangGuids.ProjectTypeGuidString,
    "BasicLang",
    "#1",
    BasicLangConstants.ProjectExtension,
    BasicLangConstants.ContentTypeName,
    BasicLangGuids.PackageGuidString)]
internal class BasicLangUnconfiguredProject
{
    [ImportingConstructor]
    public BasicLangUnconfiguredProject(UnconfiguredProject unconfiguredProject)
    {
        UnconfiguredProject = unconfiguredProject;
    }

    [Import]
    internal UnconfiguredProject UnconfiguredProject { get; }
}
```

#### 4.2 MSBuild Integration

The build targets file defines how BasicLang projects are compiled:

```xml
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <!-- Import the standard .NET SDK props first -->
  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" Condition="'$(UsingMicrosoftNETSdk)' != 'true'" />

  <PropertyGroup>
    <IsBasicLangProject>true</IsBasicLangProject>
    <BasicLangBackend Condition="'$(BasicLangBackend)' == ''">CSharp</BasicLangBackend>
    <Language>BasicLang</Language>
  </PropertyGroup>

  <!-- Define BasicLang-specific item types -->
  <ItemGroup>
    <AvailableItemName Include="BasicLangCompile" />
  </ItemGroup>

  <!-- Custom compile target -->
  <Target Name="BasicLangCompile"
          BeforeTargets="CoreCompile"
          Inputs="@(BasicLangCompile)"
          Outputs="$(IntermediateOutputPath)%(BasicLangCompile.Filename).cs">
    <Exec Command="BasicLang.exe compile &quot;%(BasicLangCompile.FullPath)&quot; --target=$(BasicLangBackend) --output=&quot;$(IntermediateOutputPath)&quot;" />
  </Target>

  <!-- Import standard targets last -->
  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" Condition="'$(UsingMicrosoftNETSdk)' != 'true'" />
</Project>
```

Register in pkgdef:

```pkgdef
[$RootKey$\MSBuild\SafeImports]
"BasicLang"="$PackageFolder$\BuildSystem\BasicLang.targets"
```

#### 4.3 Key NuGet Packages for CPS

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.VisualStudio.ProjectSystem` | 17.9+ | CPS core APIs |
| `Microsoft.VisualStudio.ProjectSystem.Managed` | 17.9+ | Managed project support |
| `Microsoft.VisualStudio.SDK` | 17.9+ | VS SDK APIs |
| `Microsoft.VisualStudio.LanguageServer.Client` | 17.10+ | LSP client |
| `Microsoft.VSSDK.BuildTools` | 17.9+ | VSIX build tools |

**Important note:** Many CPS APIs (especially in `Microsoft.VisualStudio.ProjectSystem`) are marked as internal and are not available in the public NuGet packages. The public surface is limited. This is a known pain point. The VSProjectSystem GitHub repo documents what is available.

Reference: [VSProjectSystem Introduction](https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/intro.md)

### Current State in BasicLang Extension

The extension has CPS project system files but they rely on simplified patterns because full CPS APIs are not publicly available. The key issue is that `BasicLangProjectFactory` needs to properly implement `IVsProjectFactory` to handle `.blproj` files.

---

## 5. Project Templates

### Template Discovery in VS 2017+

Starting with VS 2017, template scanning is **not automatic**. Extensions must provide `.vstman` (Visual Studio Template Manifest) files.

### Required Components

#### 5.1 The .vstemplate File

```xml
<?xml version="1.0" encoding="utf-8"?>
<VSTemplate Version="3.0.0" Type="Project"
    xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
  <TemplateData>
    <Name>BasicLang Console Application</Name>
    <Description>A console application using BasicLang.</Description>
    <ProjectType>VisualBasic</ProjectType>
    <LanguageTag>visualbasic</LanguageTag>
    <PlatformTag>windows</PlatformTag>
    <PlatformTag>linux</PlatformTag>
    <ProjectTypeTag>console</ProjectTypeTag>
    <SortOrder>1000</SortOrder>
    <TemplateID>BasicLang.ConsoleApp</TemplateID>
    <CreateNewFolder>true</CreateNewFolder>
    <DefaultName>BasicLangApp</DefaultName>
    <ProvideDefaultName>true</ProvideDefaultName>
  </TemplateData>
  <TemplateContent>
    <Project File="Project.blproj" ReplaceParameters="true">
      <ProjectItem ReplaceParameters="true" TargetFileName="Program.bas">Program.bas</ProjectItem>
    </Project>
  </TemplateContent>
</VSTemplate>
```

#### 5.2 Template Tags -- The Language Filter Problem

The New Project dialog in VS 2022 has three filter dropdowns: Language, Platform, and Project Type. These are populated from `<LanguageTag>`, `<PlatformTag>`, and `<ProjectTypeTag>` elements.

**Built-in Language Tags** (these get localized display names):

| Tag Value | Display Name |
|-----------|-------------|
| `cpp` | C++ |
| `csharp` | C# |
| `fsharp` | F# |
| `java` | Java |
| `javascript` | JavaScript |
| `python` | Python |
| `typescript` | TypeScript |
| `visualbasic` | Visual Basic |
| `xaml` | XAML |

**Custom Language Tags:** VS 2022 **does support custom values**. If you use `<LanguageTag>BasicLang</LanguageTag>`, VS will:
- Add "BasicLang" to the Language dropdown filter
- The text appears as-is (no localization)
- Templates with this tag will appear when the filter is selected

**However**, there is a critical coupling: `<ProjectType>` in the vstemplate must match either a built-in project type OR the `LanguageVsTemplate` property of a registered `ProvideProjectFactory` attribute. For custom languages, the `<ProjectType>` element must match the value registered via `ProvideProjectFactory.LanguageVsTemplate`.

#### 5.3 The ProjectType/LanguageVsTemplate Connection

```csharp
// In your package class:
[ProvideProjectFactory(
    typeof(BasicLangProjectFactory),
    null,
    "BasicLang Project Files (*.blproj);*.blproj",
    "blproj",
    "blproj",
    @".\NullPath",
    LanguageVsTemplate = "BasicLang")]
public sealed class BasicLangPackage : AsyncPackage { }
```

Then in the vstemplate:
```xml
<ProjectType>BasicLang</ProjectType>
```

This pairing is what connects the template to your project factory.

#### 5.4 The vstman Manifest File

```xml
<VSTemplateManifest Version="1.0" Locale="1033"
    xmlns="http://schemas.microsoft.com/developer/vstemplatemanifest/2015">
  <VSTemplateContainer TemplateType="Project">
    <RelativePathOnDisk>BasicLang\ConsoleApp</RelativePathOnDisk>
    <TemplateFileName>ConsoleApp.vstemplate</TemplateFileName>
    <VSTemplateHeader>
      <TemplateData xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
        <Name>BasicLang Console Application</Name>
        <Description>A console application using BasicLang.</Description>
        <ProjectType>BasicLang</ProjectType>
        <LanguageTag>BasicLang</LanguageTag>
        <PlatformTag>windows</PlatformTag>
        <ProjectTypeTag>console</ProjectTypeTag>
      </TemplateData>
    </VSTemplateHeader>
  </VSTemplateContainer>
</VSTemplateManifest>
```

#### 5.5 Template Registration in pkgdef

```pkgdef
; TemplateEngine tells VS where to scan for template manifests
[$RootKey$\TemplateEngine\Templates\BasicLang.VisualStudio\2.4.0]
"InstalledPath"="$PackageFolder$\ProjectTemplates"

[$RootKey$\TemplateEngine\Templates\BasicLang.VisualStudio.Items\2.4.0]
"InstalledPath"="$PackageFolder$\ItemTemplates"
```

Reference: [Template Tags documentation](https://learn.microsoft.com/en-us/visualstudio/ide/template-tags?view=vs-2022), [vstman Schema Reference](https://learn.microsoft.com/en-us/visualstudio/extensibility/visual-studio-template-manifest-schema-reference?view=vs-2022)

### Current State in BasicLang Extension

The extension uses `<ProjectType>VisualBasic</ProjectType>` and `<LanguageTag>visualbasic</LanguageTag>` as a workaround to appear under VB templates. This works but is misleading to users.

**Recommended Fix:** Switch to using a custom `LanguageVsTemplate` ("BasicLang") in the `ProvideProjectFactory` attribute, and update all vstemplates to use `<ProjectType>BasicLang</ProjectType>` and `<LanguageTag>BasicLang</LanguageTag>`. This will create a custom "BasicLang" entry in the language filter dropdown.

---

## 6. Debug Launch Provider

### IDebugLaunchProvider (CPS)

This is the CPS mechanism for launching debuggers from custom project types.

**Required NuGet:** `Microsoft.VisualStudio.ProjectSystem`

```csharp
[ExportDebugger("BasicLangDebugger")]
[AppliesTo("BasicLang")]
public class BasicLangDebugLaunchProvider : DebugLaunchProviderBase
{
    [ImportingConstructor]
    public BasicLangDebugLaunchProvider(ConfiguredProject configuredProject)
        : base(configuredProject)
    {
    }

    public override Task<bool> CanLaunchAsync(DebugLaunchOptions launchOptions)
    {
        return Task.FromResult(true);
    }

    public override async Task<IReadOnlyList<IDebugLaunchSettings>> QueryDebugTargetsAsync(
        DebugLaunchOptions launchOptions)
    {
        var settings = new DebugLaunchSettings(launchOptions)
        {
            Executable = outputPath,     // Path to compiled executable
            LaunchOperation = DebugLaunchOperation.CreateProcess,
            LaunchDebugEngineGuid = VSConstants.DebugEnginesGuids.ManagedAndNative_guid,
            CurrentDirectory = projectDir
        };

        return new[] { settings };
    }
}
```

**Optional XAML Rule** for debug configuration UI:

```xml
<!-- BasicLangDebugger.xaml -->
<Rule Name="BasicLangDebugger"
      DisplayName="BasicLang Debugger"
      PageTemplate="debugger"
      xmlns="http://schemas.microsoft.com/build/2009/properties">
  <Rule.DataSource>
    <DataSource Persistence="UserFile" />
  </Rule.DataSource>

  <StringProperty Name="BasicLangDebuggerCommand"
                  DisplayName="Command"
                  Description="The command to execute on debug" />
  <StringProperty Name="BasicLangDebuggerArguments"
                  DisplayName="Arguments"
                  Description="Command line arguments" />
</Rule>
```

Reference: [IDebugLaunchProvider documentation](https://github.com/microsoft/VSProjectSystem/blob/master/doc/extensibility/IDebugLaunchProvider.md), [ScriptDebuggerLaunchProvider example](https://github.com/microsoft/VSProjectSystem/blob/master/samples/WindowsScript/WindowsScript/WindowsScript.ProjectType/ScriptDebuggerLaunchProvider.cs)

### Current State in BasicLang Extension

Debug launch provider is **not yet implemented**. This is critical for F5 debugging support.

---

## 7. VSIX Packaging Best Practices

### VSIX Manifest Structure

```xml
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
  <Metadata>
    <Identity Id="BasicLang.VisualStudio" Version="2.4.0" Language="en-US"
              Publisher="Your Publisher" />
    <DisplayName>BasicLang for Visual Studio 2022</DisplayName>
    <Description>Full language support for BasicLang</Description>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Pro" Version="[17.0,18.0)">
      <ProductArchitecture>amd64</ProductArchitecture>
    </InstallationTarget>
  </Installation>
  <Dependencies>
    <Dependency Id="Microsoft.Framework.NDP"
                DisplayName="Microsoft .NET Framework"
                d:Source="Manual" Version="[4.8,)" />
  </Dependencies>
  <Prerequisites>
    <Prerequisite Id="Microsoft.VisualStudio.Component.CoreEditor"
                  Version="[17.0,)" DisplayName="Visual Studio core editor" />
  </Prerequisites>
  <Assets>
    <Asset Type="Microsoft.VisualStudio.VsPackage" d:Source="Project"
           d:ProjectName="%CurrentProject%" Path="|%CurrentProject%;PkgdefProjectOutputGroup|" />
    <Asset Type="Microsoft.VisualStudio.MefComponent" d:Source="Project"
           d:ProjectName="%CurrentProject%" Path="|%CurrentProject%|" />
    <Asset Type="Microsoft.VisualStudio.ProjectTemplate"
           Path="ProjectTemplates" />
    <Asset Type="Microsoft.VisualStudio.ItemTemplate"
           Path="ItemTemplates" />
  </Assets>
</PackageManifest>
```

**Key rules:**
- `Dependencies` = other VSIX extensions or .NET Framework requirements
- `Prerequisites` = VS Setup components (CoreEditor, etc.)
- **Never put VS Setup component IDs in Dependencies** -- this causes `MissingReferencesException`
- Include `<ProductArchitecture>amd64</ProductArchitecture>` for VS 2022

### VSIX Contents Checklist for a Language Extension

```
BasicLang.VisualStudio.dll          -- Extension assembly
BasicLang.VisualStudio.pkgdef       -- Registry entries
extension.vsixmanifest              -- VSIX manifest
[Content_Types].xml                 -- Content type mappings

Grammars/
  basiclang.tmLanguage.json         -- TextMate grammar

LanguageService/
  basiclang-language-configuration.json  -- Language configuration

BuildSystem/
  BasicLang.targets                 -- MSBuild targets

Tools/
  BasicLang.exe                     -- Language server (bundled)
  BasicLang.dll
  (other dependencies)

ProjectTemplates/
  BasicLang/
    ConsoleApp.zip
    ClassLibrary.zip
    ...
  BasicLang.ProjectTemplates.vstman

ItemTemplates/
  BasicLang/
    Class.zip
    Module.zip
    ...
  BasicLang.ItemTemplates.vstman

Menus.ctmenu                       -- Command table (compiled)
```

---

## 8. Known Limitations and Workarounds

### 8.1 New Project Dialog Language Filter

**Limitation:** The language filter dropdown in the New Project dialog was designed primarily for built-in languages. Custom language names are supported but appear as unlocalized free-form text.

**Workaround options:**
1. **Use `visualbasic` ProjectType** (current approach) -- templates appear under VB. Pragmatic but misleading.
2. **Use custom `BasicLang` ProjectType** with proper `ProvideProjectFactory` registration -- creates a custom entry. This is the correct approach but requires more work to set up properly.
3. **Use `<LanguageTag>BasicLang</LanguageTag>`** -- custom values ARE supported and will appear in the dropdown.

**Recommendation:** Option 2 + 3 combined. Use `LanguageVsTemplate = "BasicLang"` in `ProvideProjectFactory` and `<LanguageTag>BasicLang</LanguageTag>` in vstemplates.

Developer Community feedback item: [Allow custom project templates for custom language](https://developercommunity.visualstudio.com/idea/422666/allow-custom-project-templates-for-custom-language.html)

### 8.2 CPS API Accessibility

**Limitation:** Many CPS APIs in `Microsoft.VisualStudio.ProjectSystem` are internal. The public API surface is limited.

**Workaround:** Use the publicly documented patterns from [microsoft/VSProjectSystem](https://github.com/microsoft/VSProjectSystem). Focus on what is exposed: `UnconfiguredProject`, `ConfiguredProject`, `IProjectTreeProvider`, `IDebugLaunchProvider`.

### 8.3 CodeLens and DocumentLink Not Supported via LSP

**Limitation:** VS 2022's built-in LSP client does not support `textDocument/codeLens` or `textDocument/documentLink`.

**Workaround:** Implement CodeLens via the VS-native `ICodeLensDataPoint` / `ICodeLensCallbackService` MEF APIs instead of LSP.

### 8.4 Debug Adapter Protocol (DAP)

**Limitation:** VS 2022 does not have native DAP support for custom languages the way VS Code does. There is no built-in `IDebugAdapterHost` for custom languages.

**Workaround:** Use `IDebugLaunchProvider` with the managed debug engine (`DebugEnginesGuids.ManagedAndNative_guid`) since BasicLang compiles to .NET IL or C#. For a custom debug engine, you would need to implement `IDebugEngine2` (very complex, not recommended).

### 8.5 Template Discovery After Install

**Limitation:** Templates sometimes do not appear immediately after VSIX installation.

**Workaround:** Run `devenv.exe /updateConfiguration` after install. Clear the ComponentModelCache: `rd /s /q "%LOCALAPPDATA%\Microsoft\VisualStudio\17.0_*\ComponentModelCache"`.

---

## 9. How Other Languages Do It

### Rust (rust-analyzer.vs)

- **Repository:** [kitamstudios/rust-analyzer.vs](https://github.com/kitamstudios/rust-analyzer.vs)
- **Approach:** Uses "Open Folder" experience (no custom CPS project system)
- **LSP:** ILanguageClient connecting to `rust-analyzer.exe`
- **Build:** Invokes `cargo` through VS tasks
- **Debug:** Uses standard debug engines
- **Key insight:** No custom project factory; relies on folder-based experience for full language support

### PHP (DEVSENSE PHP Tools)

- **Approach:** Full CPS project system with custom project factory
- **LSP:** Custom PHP language server
- **Templates:** Custom project templates under a "PHP" language filter
- **Key insight:** Comprehensive implementation with proprietary code; one of the few extensions to achieve true first-class language status

### RemObjects Elements (Oxygene/Swift/Java)

- **Approach:** Full custom project system, custom compiler front-ends
- **Key insight:** Uses a single compiler with multiple language front-ends; project files are unified across languages
- **Integration:** "Seamless alongside Microsoft's Visual C# and Visual Basic"

### Svelte (SvelteVisualStudio)

- **Repository:** [jasonlyu123/SvelteVisualStudio](https://github.com/jasonlyu123/SvelteVisualStudio)
- **Approach:** LSP client only (no project system)
- **Key insight:** Simple extension, just connects LSP server; relies on existing project types (web projects)

### Common Pattern

Most successful VS 2022 language extensions follow this pattern:
1. TextMate grammar for syntax highlighting
2. ILanguageClient for LSP integration
3. Either piggyback on existing project systems (C#, .NET SDK) or use Open Folder
4. Only the most ambitious extensions (PHP Tools, RemObjects) implement full CPS project systems

---

## 10. Recommendations for BasicLang

### Priority 1: Quick Wins (Immediate Value)

#### 1A. Add Language Configuration File
Create `basiclang-language-configuration.json` with bracket matching, auto-closing pairs, comment toggling, and indentation rules. This provides immediate local editor improvements without LSP round-trips.

#### 1B. Fix TextMate Grammar Registration
Add proper `[$RootKey$\TextMate\Repositories]` and `[$RootKey$\TextMate\LanguageConfiguration\GrammarMapping]` entries to the pkgdef file. Move the grammar file to a `Grammars/` folder.

#### 1C. Bundle BasicLang.exe in the VSIX
Include the language server and its dependencies in a `Tools/` folder within the VSIX. Update `FindLanguageServer()` to look there first. This eliminates the "language server not found" problem.

### Priority 2: Template Fixes (User-Facing)

#### 2A. Switch to Custom LanguageTag
Change from `<LanguageTag>visualbasic</LanguageTag>` to `<LanguageTag>BasicLang</LanguageTag>` in all vstemplates. This will create a "BasicLang" entry in the New Project dialog's language dropdown.

#### 2B. Fix ProjectType Registration
Use `LanguageVsTemplate = "BasicLang"` in `ProvideProjectFactory` and set `<ProjectType>BasicLang</ProjectType>` in vstemplates. This correctly associates templates with the BasicLang project factory.

#### 2C. Ensure vstman Files Are Correct
Verify all vstman entries match the vstemplate data exactly. The vstman header TemplateData must mirror the vstemplate TemplateData.

### Priority 3: Debug Support (Critical for Developer Experience)

#### 3A. Implement IDebugLaunchProvider
Create a `BasicLangDebugLaunchProvider` class that:
- Exports with `[ExportDebugger("BasicLangDebugger")]` and `[AppliesTo("BasicLang")]`
- Compiles the BasicLang project to .NET IL or C#
- Launches the compiled output with the managed debug engine
- This enables F5 debugging

#### 3B. Add Debug Configuration XAML Rule
Provide a XAML rule for debug settings (command, arguments, working directory, etc.)

### Priority 4: Build System Improvements

#### 4A. Improve BasicLang.targets
The MSBuild targets should:
- Properly define the compile pipeline (BasicLang -> C# -> .NET assembly)
- Support incremental builds (inputs/outputs tracking)
- Integrate with VS's error list (parse compiler output)
- Support clean/rebuild targets

#### 4B. Package BasicLang.SDK as NuGet
Distribute the MSBuild SDK as a NuGet package so projects can reference it: `<Project Sdk="BasicLang.SDK/1.0.0">`. This is the modern pattern for custom language SDKs.

### Priority 5: Future / Nice-to-Have

#### 5A. Consider VisualStudio.Extensibility Model
When the new extensibility model gains project system support, consider migrating the LSP client to `LanguageServerProvider` for out-of-process crash isolation. Keep the CPS components in VSSDK.

#### 5B. Custom File Icons in Solution Explorer
Implement `IProjectTreeProvider` to show custom icons for `.bas` files in Solution Explorer. Use the `[AppliesTo("BasicLang")]` MEF pattern.

#### 5C. LSP Settings Page
Create a Tools > Options page that configures BasicLang LSP settings and stores them in VS settings, forwarded to the language server via `workspace/didChangeConfiguration`.

### Implementation Checklist

```
[ ] Create basiclang-language-configuration.json
[ ] Update pkgdef with TextMate repository and grammar mapping
[ ] Bundle BasicLang.exe + dependencies in VSIX Tools/ folder
[ ] Update FindLanguageServer() to check VSIX-bundled path first
[ ] Change LanguageTag from "visualbasic" to "BasicLang" in all vstemplates
[ ] Change ProjectType from "VisualBasic" to "BasicLang" in all vstemplates
[ ] Update ProvideProjectFactory with LanguageVsTemplate = "BasicLang"
[ ] Update vstman files to match vstemplate changes
[ ] Implement BasicLangDebugLaunchProvider with IDebugLaunchProvider
[ ] Add debug configuration XAML rule
[ ] Improve BasicLang.targets with incremental build support
[ ] Add LSP workspace settings support
[ ] Add LSP tracing configuration
[ ] Test template discovery after clean install
[ ] Test F5 debugging end-to-end
```

---

## 11. Sources

### Official Microsoft Documentation
- [Adding a Language Server Protocol extension](https://learn.microsoft.com/en-us/visualstudio/extensibility/adding-an-lsp-extension?view=vs-2022)
- [Create an Extensible Language Server Provider (VisualStudio.Extensibility)](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/language-server-provider/language-server-provider?view=vs-2022)
- [Add language-specific syntax support using Language Configuration](https://learn.microsoft.com/en-us/visualstudio/extensibility/language-configuration?view=vs-2022)
- [Add or edit tags on project templates](https://learn.microsoft.com/en-us/visualstudio/ide/template-tags?view=vs-2022)
- [Visual Studio Template Manifest Schema Reference](https://learn.microsoft.com/en-us/visualstudio/extensibility/visual-studio-template-manifest-schema-reference?view=vs-2022)
- [Creating a Basic Project System, Part 2](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-a-basic-project-system-part-2?view=vs-2022)
- [Creating Custom Project and Item Templates](https://learn.microsoft.com/en-us/visualstudio/extensibility/creating-custom-project-and-item-templates?view=vs-2022)
- [ProvideProjectFactoryAttribute.LanguageVsTemplate Property](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.shell.provideprojectfactoryattribute.languagevstemplate?view=visualstudiosdk-2022)
- [ILanguageClient Interface](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.languageserver.client.ilanguageclient?view=visualstudiosdk-2022)
- [VisualStudio.Extensibility Overview](https://learn.microsoft.com/en-us/visualstudio/extensibility/visualstudio.extensibility/visualstudio-extensibility?view=vs-2022)
- [Language Server Protocol Overview](https://learn.microsoft.com/en-us/visualstudio/extensibility/language-server-protocol?view=vs-2022)
- [Add editor support for other languages](https://learn.microsoft.com/en-us/visualstudio/ide/adding-visual-studio-editor-support-for-other-languages?view=vs-2022)
- [Extend and customize the build process](https://learn.microsoft.com/en-us/visualstudio/msbuild/how-to-extend-the-visual-studio-build-process?view=vs-2022)
- [Visual Studio Integration (MSBuild)](https://learn.microsoft.com/en-us/visualstudio/msbuild/visual-studio-integration-msbuild?view=vs-2022)
- [Build Visual Studio templates with tags](https://devblogs.microsoft.com/visualstudio/build-visual-studio-templates-with-tags-for-efficient-user-search-and-grouping/)
- [Custom debug engines in Visual Studio 2022](https://learn.microsoft.com/en-us/answers/questions/5645702/custom-debug-engines-in-visual-studio-2022)

### GitHub Repositories
- [microsoft/VSProjectSystem](https://github.com/microsoft/VSProjectSystem) -- CPS documentation and samples
- [microsoft/VSProjectSystem - IDebugLaunchProvider](https://github.com/microsoft/VSProjectSystem/blob/master/doc/extensibility/IDebugLaunchProvider.md)
- [microsoft/VSProjectSystem - MEF](https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/mef.md)
- [microsoft/VSProjectSystem - Project Capabilities](https://github.com/microsoft/VSProjectSystem/blob/master/doc/overview/about_project_capabilities.md)
- [microsoft/VSProjectSystem - ScriptDebuggerLaunchProvider sample](https://github.com/microsoft/VSProjectSystem/blob/master/samples/WindowsScript/WindowsScript/WindowsScript.ProjectType/ScriptDebuggerLaunchProvider.cs)
- [microsoft/VSSDK-Extensibility-Samples](https://github.com/microsoft/VSSDK-Extensibility-Samples) -- Official extensibility samples
- [microsoft/VSSDK-Extensibility-Samples - TextmateGrammar](https://github.com/microsoft/VSSDK-Extensibility-Samples/blob/master/TextmateGrammar/src/languages.pkgdef)
- [microsoft/VSSDK-Extensibility-Samples - LanguageServerProtocol](https://github.com/microsoft/VSSDK-Extensibility-Samples/tree/master/LanguageServerProtocol)
- [microsoft/VSExtensibility](https://github.com/microsoft/VSExtensibility/) -- New extensibility model
- [madskristensen/TextmateSample](https://github.com/madskristensen/TextmateSample) -- TextMate grammar VS extension sample
- [kitamstudios/rust-analyzer.vs](https://github.com/kitamstudios/rust-analyzer.vs) -- Rust language support for VS 2022
- [jasonlyu123/SvelteVisualStudio](https://github.com/jasonlyu123/SvelteVisualStudio) -- Svelte LSP client for VS 2022

### Developer Community / Forums
- [Allow custom project templates for custom language in 2019](https://developercommunity.visualstudio.com/idea/422666/allow-custom-project-templates-for-custom-language.html)
- [Custom project templates placed under the language](https://developercommunity.visualstudio.com/t/custom-project-templates-placed-under-the-language/1578537)
- [Custom project templates not showing in Create New](https://developercommunity.visualstudio.com/t/custom-project-templates-not-showing-in-create-new/1289638)

### Other References
- [Debug Adapter Protocol](https://microsoft.github.io/debug-adapter-protocol/)
- [Language Server Protocol](https://microsoft.github.io/language-server-protocol/)
- [RemObjects Elements](https://www.remobjects.com/elements/)
- [VSIX Cookbook - Walkthrough: Create Custom Language Editor](https://www.vsixcookbook.com/recipes/Walkthrough-Create-Language-Editor.html)
- [Visual Studio Extensibility Cookbook](https://www.vsixcookbook.com/getting-started/useful-resources.html)
- [The Future of Visual Studio Extensibility is Here!](https://devblogs.microsoft.com/visualstudio/the-future-of-visual-studio-extensibility-is-here/)
- [Introducing the Project System Extensibility Preview](https://devblogs.microsoft.com/visualstudio/introducing-the-project-system-extensibility-preview/)
