# C++ Projects Phase 1 — Build & Run Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A user can create a pure C++ project in VGS Studio (CLI `BasicLang.exe new cpp-console` or the IDE New Project dialog), edit `.cpp`/`.h` files with C++ syntax highlighting, build it with the existing clang++/g++/MSVC toolchain from both the CLI and the IDE, see per-file/per-line compiler errors in the Error List, get a `compile_commands.json`, and Run the produced exe — per Phase 1 of `docs/superpowers/specs/2026-07-11-cpp-language-support-design.md`.

**Architecture:** A new `<Language>Cpp</Language>` axis on `.blproj`. All native-project build logic lives in **one shared class** (`CppProjectBuilder` in `BasicLang/ProjectSystem/`) consumed by both the CLI (`Program.cs HandleBuildCommand`) and the IDE (`BuildService`), preserving the repo's no-drift invariant. `CppToolchain` grows a multi-TU, flag-aware compile API whose command-line construction also feeds a `compile_commands.json` writer. Compiler stderr is parsed by a new `CppDiagnosticsParser` (clang/gcc + MSVC formats) into the existing `DiagnosticItem` flow.

**Tech Stack:** C# / .NET 8, NUnit 4 (constraint model), AvaloniaEdit XSHD highlighting, external clang++/g++/MSVC toolchains.

**Critical recon facts (verified against HEAD, do not re-derive):**
- `BasicCompiler.CompileProjectFiles` (BasicLang/Compiler.cs:199) compiles **BasicLang sources only**. The real `.blproj` orchestrators are CLI `HandleBuildCommand` (BasicLang/Program.cs:406, cpp branch 733–798) and IDE `BuildService.CompileWithBasicLangApiAsync` (VisualGameStudio.ProjectSystem/Services/BuildService.cs:411). Pure-C++ routing branches **around** them.
- **Three** `.blproj` parsers must learn `<Language>`: `BasicLang/ProjectSystem/ProjectFile.cs` (Load :70 / Save :211), `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` (:35–48, write :152), and the two template generators. IDE `ProjectSerializer.SaveAsync` rebuilds XML from the model — anything not modeled is **silently stripped on save**.
- CLI reads `<Backend> ?? <TargetBackend>`; IDE reads **only** `<TargetBackend>` (case-insensitive enum, rejects `c++`). Templates must write `<TargetBackend>Cpp</TargetBackend>` (never `<Backend>`).
- `CppToolchain.CompileToExecutable` (BasicLang/ProjectSystem/CppToolchain.cs:95) is single-TU, no `-I`/defines/opt flags. MSVC runs via `cmd.exe /s /c ""<vcvars64.bat>" >nul && cl ..."` — the double-double-quote pattern is load-bearing.
- No C++ diagnostic parsing exists anywhere. IDE currently emits ONE blob `DiagnosticItem` BL6004 pinned to the `.blproj`. Diagnostic IDs BL6001–BL6004 are taken.
- OutputPanel's clickable-error regex (VisualGameStudio.Shell/ViewModels/Panels/OutputPanelViewModel.cs:63) matches only `file(line[,col]): error CODE: msg` — echo C++ errors normalized to that format.
- Live New Project dialog = `CreateProjectView` / `CreateProjectViewModel` (constructed at MainWindowViewModel.cs:1651). `NewProjectDialog`/`NewProjectViewModel` are dead — do not touch.
- `SetHighlightingForFile` (VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs:406) already handles `.cpp` via built-in fallback but is **only called on theme change**; every editor gets BasicLang highlighting in `OnInitialized` (:495–503). AvaloniaEdit 11.3.0's built-in "C++" definition has hard-coded light colors (unreadable on dark) — a repo-authored XSHD registered as "C++" shadows it.
- LSP leak: `CodeEditorControl.UpdateFoldings` (:1884) sends foldingRange for ANY file; `GoToDefinitionAsync` (MainWindowViewModel.cs:4223) is ungated. The BasicLang server answers unknown URIs as BasicLang.
- `framework.h` includes `pch.h` (not consumable by user code); engine declarations must be declared `extern "C"` inline (import-lib linking needs no `__declspec(dllimport)` for functions). Verified exports: `bool Framework_Initialize(int,int,const char*)`, `void Framework_Update()`, `bool Framework_ShouldClose()`, `void Framework_Shutdown()`, `void Framework_BeginDrawing()`, `void Framework_EndDrawing()`, `void Framework_ClearBackground(u8,u8,u8,u8)`, `void Framework_DrawText(const char*,int,int,int,u8,u8,u8,u8)` (framework.h:280–299).
- `VisualGameStudioEngine.lib`/`.dll` live next to the shipped binaries (`IDE/`); CLI-side lookup is `EngineDeployment.GetImportLibPath(AppContext.BaseDirectory)` only — dev trees need broader probing (IDE already probes `x64/{Release,Debug}` privately in BuildService).
- Test conventions: NUnit 4 constraint asserts; `[NonParallelizable]` on process-spawning fixtures; temp dirs `Path.GetTempPath()/"<prefix>-"+Guid`; teardown retries 3× with 200 ms sleep; toolchain skip = `if (CppToolchain.Find() == null) Assert.Ignore(...)`; run one fixture: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~<Fixture>"`.
- Repo rules: use Edit/Write tools (never PS `Get-Content`/`Set-Content` round-trips); `dotnet clean` after AXAML changes; test through BOTH the CLI and IDE entry points.

**New diagnostic IDs introduced by this plan:**
| Id | Severity | Meaning |
|---|---|---|
| BL6005 | Error | No C++ toolchain found for a `Language=Cpp` project (hard fail — unlike BL6002's soft warning for the transpile backend) |
| BL6006 | Error | Native compile failed but no diagnostics could be parsed (raw output fallback) |
| BL6007 | Error | `Language=Cpp` project contains no C++ translation units |
| BL6008 | Error | `Language=Cpp` project contains BasicLang sources (mixed projects arrive in Phase 2) |
| BL6009 | Error | A `<NativeLib>` could not be resolved |
| CPP1001 / CPP1002 | Error / Warning | Parsed clang/gcc diagnostic with no compiler code (MSVC codes like `C2065`/`LNK2019` are kept verbatim) |

**File structure (new files):**
| File | Responsibility |
|---|---|
| `BasicLang/ProjectSystem/CppDiagnosticsParser.cs` | Parse clang/gcc/MSVC/linker output → `List<CppDiagnostic>` |
| `BasicLang/ProjectSystem/CompileCommandsWriter.cs` | Emit `obj/compile_commands.json` from the toolchain's own command lines |
| `BasicLang/ProjectSystem/CppProjectBuilder.cs` | The single shared native-project build orchestrator (CLI + IDE) |
| `VisualGameStudio.Editor/Highlighting/Cpp.xshd`, `CppLight.xshd` | Themed C++ syntax highlighting |
| `VisualGameStudio.Tests/Compiler/CppDiagnosticsParserTests.cs` | Parser unit tests |
| `VisualGameStudio.Tests/Compiler/CppProjectFileTests.cs` | `.blproj` Language/C++-items round-trip + TU-glob tests |
| `VisualGameStudio.Tests/Compiler/CompileCommandsWriterTests.cs` | compile_commands.json content tests |
| `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs` | CLI e2e (multi-file build, errors, templates) |
| `VisualGameStudio.Tests/Serialization/ProjectSerializerCppTests.cs` | IDE serializer round-trip tests |
| `VisualGameStudio.Tests/Editor/CppHighlightingTests.cs` | Highlighting registration tests |

Modified: `ProjectFile.cs`, `CppToolchain.cs`, `EngineDeployment.cs`, `Program.cs`, `TemplateEngine.cs` (compiler side); `BasicLangProject.cs`, `ProjectSerializer.cs`, `BuildService.cs`, `IProjectTemplateService.cs`, `ProjectTemplateService.cs` (IDE project system); `SolutionExplorerViewModel.cs`, `MainWindowViewModel.cs` (shell); `HighlightingLoader.cs`, `CodeEditorControl.axaml.cs`, `VisualGameStudio.Editor.csproj` (editor); `TemplateBuildSweepTests.cs`, `BuildServicePipelineTests.cs` (tests).

Skills to apply throughout: @superpowers:test-driven-development (every task below is RED→GREEN→commit), @superpowers:verification-before-completion (final task).

---

### Task 1: `<Language>` + C++ properties in the compiler-side `ProjectFile`

**Files:**
- Modify: `BasicLang/ProjectSystem/ProjectFile.cs` (property block ~:39, `Load` ~:94–96 and ItemGroup loop ~:112, `Save` ~:227, new method after `GetSourceFiles` ~:335)
- Test: `VisualGameStudio.Tests/Compiler/CppProjectFileTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppProjectFileTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cppproj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    private string WriteProject(string xml)
    {
        var path = Path.Combine(_dir, "Test.blproj");
        File.WriteAllText(path, xml);
        return path;
    }

    [Test]
    public void Load_ParsesLanguageCppStandardAndCppItems()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>Test</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <CppStandard>c++17</CppStandard>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <IncludeDir Include="vendor/include" />
                <NativeLib Include="VisualGameStudioEngine.lib" />
                <Define Include="MY_FLAG" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var project = ProjectFile.Load(path);

        Assert.That(project.Language, Is.EqualTo("Cpp"));
        Assert.That(project.IsCppProject, Is.True);
        Assert.That(project.CppStandard, Is.EqualTo("c++17"));
        Assert.That(project.IncludeDirs, Is.EqualTo(new[] { "vendor/include" }));
        Assert.That(project.NativeLibs, Is.EqualTo(new[] { "VisualGameStudioEngine.lib" }));
        Assert.That(project.Defines, Is.EqualTo(new[] { "MY_FLAG" }));
    }

    [Test]
    public void Load_DefaultsLanguageToBasicLang()
    {
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);
        Assert.That(project.Language, Is.EqualTo("BasicLang"));
        Assert.That(project.IsCppProject, Is.False);
        Assert.That(project.CppStandard, Is.EqualTo("c++20"), "default standard");
    }

    [Test]
    public void SaveThenLoad_RoundTripsLanguageAndCppItems()
    {
        var project = new ProjectFile
        {
            FilePath = Path.Combine(_dir, "RT.blproj"),
            ProjectName = "RT",
            Language = "Cpp",
            CppStandard = "c++20",
        };
        project.SourceFiles.Add("main.cpp");
        project.IncludeDirs.Add("inc");
        project.NativeLibs.Add("foo.lib");
        project.Defines.Add("A_DEFINE");

        project.Save();
        var reloaded = ProjectFile.Load(project.FilePath);

        Assert.That(reloaded.Language, Is.EqualTo("Cpp"));
        Assert.That(reloaded.CppStandard, Is.EqualTo("c++20"));
        Assert.That(reloaded.IncludeDirs, Is.EqualTo(new[] { "inc" }));
        Assert.That(reloaded.NativeLibs, Is.EqualTo(new[] { "foo.lib" }));
        Assert.That(reloaded.Defines, Is.EqualTo(new[] { "A_DEFINE" }));
    }

    [Test]
    public void Save_BasicLangProject_DoesNotEmitLanguageOrCppElements()
    {
        var project = new ProjectFile { FilePath = Path.Combine(_dir, "BL.blproj"), ProjectName = "BL" };
        project.Save();
        var text = File.ReadAllText(project.FilePath);
        Assert.That(text, Does.Not.Contain("<Language>"), "old-format files must stay untouched");
        Assert.That(text, Does.Not.Contain("<CppStandard>"));
    }

    [Test]
    public void GetCppTranslationUnits_DefaultGlob_FindsTUsExcludingBinObjAndHeaders()
    {
        File.WriteAllText(Path.Combine(_dir, "main.cpp"), "// tu");
        File.WriteAllText(Path.Combine(_dir, "util.cc"), "// tu");
        File.WriteAllText(Path.Combine(_dir, "util.h"), "// header, not a TU");
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "src", "extra.cpp"), "// tu");
        Directory.CreateDirectory(Path.Combine(_dir, "bin", "Debug"));
        File.WriteAllText(Path.Combine(_dir, "bin", "Debug", "generated.cpp"), "// must be excluded");
        Directory.CreateDirectory(Path.Combine(_dir, "obj"));
        File.WriteAllText(Path.Combine(_dir, "obj", "stale.cpp"), "// must be excluded");

        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><Language>Cpp</Language></PropertyGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);

        var tus = project.GetCppTranslationUnits().Select(Path.GetFileName).ToList();

        Assert.That(tus, Is.EquivalentTo(new[] { "main.cpp", "util.cc", "extra.cpp" }));
    }

    [Test]
    public void GetCppTranslationUnits_ExplicitCompileItems_FiltersToTuExtensions()
    {
        File.WriteAllText(Path.Combine(_dir, "main.cpp"), "// tu");
        File.WriteAllText(Path.Combine(_dir, "util.h"), "// header");
        var path = WriteProject("""
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Test</ProjectName><Language>Cpp</Language></PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <Compile Include="util.h" />
              </ItemGroup>
            </BasicLangProject>
            """);
        var project = ProjectFile.Load(path);
        var tus = project.GetCppTranslationUnits().Select(Path.GetFileName).ToList();
        Assert.That(tus, Is.EqualTo(new[] { "main.cpp" }), "headers ride along as Compile items but are not compiled");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppProjectFileTests"`
Expected: FAIL — `ProjectFile` has no `Language`/`CppStandard`/`IncludeDirs`/`NativeLibs`/`Defines`/`GetCppTranslationUnits` members (compile errors).

- [ ] **Step 3: Implement in `ProjectFile.cs`**

3a. Property block (after `public string Backend ... = "CSharp";` at :39):

```csharp
        // Project language: "BasicLang" (default) or "Cpp" (user-authored C++
        // sources compiled directly by the native toolchain — no transpile).
        public string Language { get; set; } = "BasicLang";

        public bool IsCppProject =>
            string.Equals(Language, "Cpp", StringComparison.OrdinalIgnoreCase);

        // C++-only settings (ignored for BasicLang projects)
        public string CppStandard { get; set; } = "c++20";
        public List<string> IncludeDirs { get; set; } = new List<string>();
        public List<string> NativeLibs { get; set; } = new List<string>();
        public List<string> Defines { get; set; } = new List<string>();

        /// <summary>File extensions treated as C++ translation units (headers are not compiled).</summary>
        public static readonly string[] CppTranslationUnitExtensions = { ".cpp", ".cc", ".cxx", ".c" };
```

3b. In `Load`, after the Backend lines (:94–96):

```csharp
                project.Language = propertyGroup.Element("Language")?.Value ?? "BasicLang";
                project.CppStandard = propertyGroup.Element("CppStandard")?.Value ?? "c++20";
```

3c. In the `foreach (var itemGroup in root.Elements("ItemGroup"))` loop (after the `<Compile>` handling at :115–120), add:

```csharp
                foreach (var inc in itemGroup.Elements("IncludeDir"))
                {
                    var include = inc.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include)) project.IncludeDirs.Add(include);
                }
                foreach (var lib in itemGroup.Elements("NativeLib"))
                {
                    var include = lib.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include)) project.NativeLibs.Add(include);
                }
                foreach (var def in itemGroup.Elements("Define"))
                {
                    var include = def.Attribute("Include")?.Value;
                    if (!string.IsNullOrEmpty(include)) project.Defines.Add(include);
                }
```

3d. In `Save` (main PropertyGroup construction at :217–233), after the `TargetBackend` element add (conditional so BasicLang files are untouched):

```csharp
            IsCppProject ? new XElement("Language", Language) : null,
            IsCppProject ? new XElement("CppStandard", CppStandard) : null,
```

and after the existing Compile/Reference ItemGroup emission add a C++ ItemGroup (only when any list is non-empty):

```csharp
            if (IncludeDirs.Count > 0 || NativeLibs.Count > 0 || Defines.Count > 0)
            {
                var cppItems = new XElement("ItemGroup");
                foreach (var d in IncludeDirs) cppItems.Add(new XElement("IncludeDir", new XAttribute("Include", d)));
                foreach (var l in NativeLibs) cppItems.Add(new XElement("NativeLib", new XAttribute("Include", l)));
                foreach (var d in Defines) cppItems.Add(new XElement("Define", new XAttribute("Include", d)));
                doc.Root.Add(cppItems);
            }
```

(Adapt to `Save`'s actual construction style — it builds the `XDocument` inline; append the ItemGroup to the root element after construction, before `doc.Save`.)

3e. New method after `GetSourceFiles()` (:335):

```csharp
        /// <summary>
        /// C++ translation units for a Language=Cpp project. Default (no explicit
        /// Compile items): recursive glob of TU extensions excluding bin/ and obj/
        /// (build outputs live under the project dir). Explicit Compile items are
        /// resolved by GetSourceFiles' existing rules, then filtered to TU
        /// extensions so headers can be listed without being compiled.
        /// </summary>
        public IEnumerable<string> GetCppTranslationUnits()
        {
            var projectDir = Path.GetDirectoryName(FilePath) ?? ".";

            if (SourceFiles.Count == 0)
            {
                foreach (var ext in CppTranslationUnitExtensions)
                    foreach (var file in Directory.GetFiles(projectDir, "*" + ext, SearchOption.AllDirectories))
                        // Exact-extension check: Win32 globbing lets "*.c" match
                        // longer extensions that merely start with "c".
                        if (string.Equals(Path.GetExtension(file), ext, StringComparison.OrdinalIgnoreCase)
                            && !IsInBuildOutputDir(projectDir, file))
                            yield return file;
            }
            else
            {
                foreach (var file in GetSourceFiles())
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.IndexOf(CppTranslationUnitExtensions, ext) >= 0)
                        yield return file;
                }
            }
        }

        internal static bool IsInBuildOutputDir(string projectDir, string file)
        {
            var rel = Path.GetRelativePath(projectDir, file);
            return rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppProjectFileTests"`
Expected: all 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add BasicLang/ProjectSystem/ProjectFile.cs VisualGameStudio.Tests/Compiler/CppProjectFileTests.cs
git commit -m "feat(cpp): Language axis + C++ items and TU gathering in ProjectFile"
```

---

### Task 2: `CppDiagnosticsParser`

**Files:**
- Create: `BasicLang/ProjectSystem/CppDiagnosticsParser.cs`
- Test: `VisualGameStudio.Tests/Compiler/CppDiagnosticsParserTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppDiagnosticsParserTests
{
    [Test]
    public void Parses_ClangError_WithColumn()
    {
        var output = @"C:\proj\main.cpp:12:5: error: use of undeclared identifier 'foo'";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].FilePath, Is.EqualTo(@"C:\proj\main.cpp"));
        Assert.That(diags[0].Line, Is.EqualTo(12));
        Assert.That(diags[0].Column, Is.EqualTo(5));
        Assert.That(diags[0].IsWarning, Is.False);
        Assert.That(diags[0].Code, Is.EqualTo("CPP1001"));
        Assert.That(diags[0].Message, Is.EqualTo("use of undeclared identifier 'foo'"));
    }

    [Test]
    public void Parses_GccWarning_AndFatalError()
    {
        var output = "util.cc:3:10: warning: unused variable 'x' [-Wunused-variable]\n"
                   + "main.cpp:1:10: fatal error: missing.h: No such file or directory";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(2));
        Assert.That(diags[0].IsWarning, Is.True);
        Assert.That(diags[0].Code, Is.EqualTo("CPP1002"));
        Assert.That(diags[0].FilePath, Is.EqualTo(Path.Combine(@"C:\proj", "util.cc")), "relative paths resolve against the working dir");
        Assert.That(diags[1].IsWarning, Is.False);
        Assert.That(diags[1].Message, Does.Contain("missing.h"));
    }

    [Test]
    public void Parses_MsvcError_LineOnly_KeepsCompilerCode()
    {
        var output = @"C:\proj\main.cpp(5): error C2065: 'x': undeclared identifier";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].Line, Is.EqualTo(5));
        Assert.That(diags[0].Column, Is.EqualTo(0));
        Assert.That(diags[0].Code, Is.EqualTo("C2065"));
    }

    [Test]
    public void Parses_MsvcWarning_WithLineAndColumn()
    {
        var output = @"main.cpp(7,12): warning C4189: 'y': local variable is initialized but not referenced";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].IsWarning, Is.True);
        Assert.That(diags[0].Line, Is.EqualTo(7));
        Assert.That(diags[0].Column, Is.EqualTo(12));
    }

    [Test]
    public void Parses_LinkerError_WithoutLine()
    {
        var output = @"main.obj : error LNK2019: unresolved external symbol Framework_Initialize referenced in function main";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1));
        Assert.That(diags[0].Code, Is.EqualTo("LNK2019"));
        Assert.That(diags[0].Line, Is.EqualTo(0));
    }

    [Test]
    public void Ignores_Notes_CaretLines_AndChatter()
    {
        var output = "main.cpp:12:5: error: no matching function for call to 'f'\n"
                   + "main.cpp:3:6: note: candidate function not viable\n"
                   + "    f(1, 2);\n"
                   + "    ^\n"
                   + "1 error generated.\n"
                   + "Microsoft (R) C/C++ Optimizing Compiler";
        var diags = CppDiagnosticsParser.Parse(output, @"C:\proj");
        Assert.That(diags, Has.Count.EqualTo(1), "only the error line is a diagnostic");
    }

    [Test]
    public void EmptyOrNullOutput_ReturnsEmptyList()
    {
        Assert.That(CppDiagnosticsParser.Parse(null, @"C:\proj"), Is.Empty);
        Assert.That(CppDiagnosticsParser.Parse("", @"C:\proj"), Is.Empty);
    }

    [Test]
    public void FormatNormalized_EmitsMsBuildStyle()
    {
        var d = new CppDiagnostic
        {
            FilePath = @"C:\proj\main.cpp", Line = 12, Column = 5,
            IsWarning = false, Code = "CPP1001", Message = "boom"
        };
        Assert.That(CppDiagnosticsParser.FormatNormalized(d),
            Is.EqualTo(@"C:\proj\main.cpp(12,5): error CPP1001: boom"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppDiagnosticsParserTests"`
Expected: FAIL (types don't exist).

- [ ] **Step 3: Implement `CppDiagnosticsParser.cs` (complete file)**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>One parsed C++ compiler/linker diagnostic.</summary>
    public sealed class CppDiagnostic
    {
        public string FilePath { get; set; }
        public int Line { get; set; }          // 1-based; 0 = no line (linker)
        public int Column { get; set; }        // 1-based; 0 = no column
        public bool IsWarning { get; set; }
        public string Code { get; set; }       // MSVC/linker code, or CPP1001/CPP1002
        public string Message { get; set; }
    }

    /// <summary>
    /// Parses clang/gcc (`file:line:col: severity: msg`), MSVC
    /// (`file(line[,col]): severity CODE: msg`) and MSVC linker
    /// (`file : error LNKnnnn: msg`) diagnostics out of raw toolchain output.
    /// Notes, caret/source-echo lines and banner chatter are skipped.
    /// </summary>
    public static class CppDiagnosticsParser
    {
        public const string GenericErrorCode = "CPP1001";
        public const string GenericWarningCode = "CPP1002";

        // clang/gcc: C:\p\main.cpp:12:5: error: msg   (drive colon is safe: the
        // line/col groups anchor the last two ':'-separated numeric fields)
        private static readonly Regex GccClang = new Regex(
            @"^(?<file>.+?):(?<line>\d+):(?<col>\d+):\s+(?:fatal\s+)?(?<sev>error|warning):\s+(?<msg>.*)$",
            RegexOptions.Compiled);

        // MSVC: main.cpp(5): error C2065: msg   |   main.cpp(7,12): warning C4189: msg
        private static readonly Regex Msvc = new Regex(
            @"^(?<file>.+?)\((?<line>\d+)(?:,(?<col>\d+))?\)\s*:\s*(?:fatal\s+)?(?<sev>error|warning)\s+(?<code>[A-Z]+\d+)\s*:\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        // MSVC linker: main.obj : error LNK2019: msg
        private static readonly Regex Linker = new Regex(
            @"^(?<file>[^:(]+?)\s*:\s*(?:fatal\s+)?error\s+(?<code>LNK\d+)\s*:\s*(?<msg>.*)$",
            RegexOptions.Compiled);

        public static List<CppDiagnostic> Parse(string toolchainOutput, string workingDirectory)
        {
            var result = new List<CppDiagnostic>();
            if (string.IsNullOrEmpty(toolchainOutput))
                return result;

            foreach (var raw in toolchainOutput.Split('\n'))
            {
                var line = raw.TrimEnd('\r');

                var m = Msvc.Match(line);
                if (m.Success)
                {
                    result.Add(new CppDiagnostic
                    {
                        FilePath = Absolutize(m.Groups["file"].Value.Trim(), workingDirectory),
                        Line = int.Parse(m.Groups["line"].Value),
                        Column = m.Groups["col"].Success ? int.Parse(m.Groups["col"].Value) : 0,
                        IsWarning = m.Groups["sev"].Value == "warning",
                        Code = m.Groups["code"].Value,
                        Message = m.Groups["msg"].Value.Trim()
                    });
                    continue;
                }

                m = GccClang.Match(line);
                if (m.Success)
                {
                    var isWarning = m.Groups["sev"].Value == "warning";
                    result.Add(new CppDiagnostic
                    {
                        FilePath = Absolutize(m.Groups["file"].Value.Trim(), workingDirectory),
                        Line = int.Parse(m.Groups["line"].Value),
                        Column = int.Parse(m.Groups["col"].Value),
                        IsWarning = isWarning,
                        Code = isWarning ? GenericWarningCode : GenericErrorCode,
                        Message = m.Groups["msg"].Value.Trim()
                    });
                    continue;
                }

                m = Linker.Match(line);
                if (m.Success)
                {
                    result.Add(new CppDiagnostic
                    {
                        FilePath = Absolutize(m.Groups["file"].Value.Trim(), workingDirectory),
                        Line = 0,
                        Column = 0,
                        IsWarning = false,
                        Code = m.Groups["code"].Value,
                        Message = m.Groups["msg"].Value.Trim()
                    });
                }
            }

            return result;
        }

        /// <summary>
        /// MSBuild-style single line: `path(line,col): error CODE: message` —
        /// the format the IDE Output panel's click-to-navigate regex matches.
        /// </summary>
        public static string FormatNormalized(CppDiagnostic d)
        {
            var kind = d.IsWarning ? "warning" : "error";
            var location = d.Line > 0
                ? (d.Column > 0 ? $"{d.FilePath}({d.Line},{d.Column})" : $"{d.FilePath}({d.Line})")
                : d.FilePath;
            return $"{location}: {kind} {d.Code}: {d.Message}";
        }

        private static string Absolutize(string file, string workingDirectory)
        {
            try
            {
                if (!Path.IsPathRooted(file) && !string.IsNullOrEmpty(workingDirectory))
                    return Path.Combine(workingDirectory, file);
            }
            catch (ArgumentException) { /* illegal chars — keep as-is */ }
            return file;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release --filter "FullyQualifiedName~CppDiagnosticsParserTests"`
Expected: all 8 PASS. (If `Ignores_Notes...` fails because the linker regex matches the banner line `Microsoft (R) ...`: the `[^:(]+?` file group excludes `(`, which the banner contains — verify the regex is transcribed exactly.)

- [ ] **Step 5: Commit**

```bash
git add BasicLang/ProjectSystem/CppDiagnosticsParser.cs VisualGameStudio.Tests/Compiler/CppDiagnosticsParserTests.cs
git commit -m "feat(cpp): clang/gcc/MSVC diagnostics parser with MSBuild-style normalization"
```

---

### Task 3: Multi-TU, flag-aware `CppToolchain` API

**Files:**
- Modify: `BasicLang/ProjectSystem/CppToolchain.cs`
- Test: extend `VisualGameStudio.Tests/Compiler/CppDiagnosticsParserTests.cs`? No — create `VisualGameStudio.Tests/Compiler/CppToolchainArgsTests.cs`

The existing `CompileToExecutable` stays untouched (the transpile-backend path keeps using it). New surface:

- [ ] **Step 1: Write the failing tests (command-line construction is pure — no compiler needed)**

```csharp
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CppToolchainArgsTests
{
    private static CppCompileRequest Request()
    {
        var r = new CppCompileRequest
        {
            OutputPath = @"C:\proj\bin\Debug\App.exe",
            CppStandard = "c++20",
            WorkingDirectory = @"C:\proj\bin\Debug",
            DebugSymbols = true,
            Optimize = false,
        };
        r.SourceFiles.Add(@"C:\proj\main.cpp");
        r.SourceFiles.Add(@"C:\proj\util.cpp");
        r.IncludeDirs.Add(@"C:\proj\vendor\include");
        r.Defines.Add("MY_FLAG");
        r.Libraries.Add(@"C:\tools\VisualGameStudioEngine.lib");
        return r;
    }

    [Test]
    public void ClangLike_PerTuArguments_ContainStdIncludeDefine()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.ClangLike, "clang++", Request(), @"C:\proj\main.cpp");
        Assert.That(args[0], Is.EqualTo("clang++"));
        Assert.That(args, Does.Contain("-std=c++20"));
        Assert.That(args, Does.Contain(@"-IC:\proj\vendor\include"));
        Assert.That(args, Does.Contain("-DMY_FLAG"));
        Assert.That(args, Does.Contain(@"C:\proj\main.cpp"));
        Assert.That(args, Does.Not.Contain(@"C:\proj\util.cpp"), "per-TU entry lists only its own file");
    }

    [Test]
    public void Msvc_PerTuArguments_UseSlashFlags()
    {
        var args = CppToolchain.BuildCompileCommandArguments(
            CppToolchainKind.Msvc, "cl", Request(), @"C:\proj\main.cpp");
        Assert.That(args[0], Is.EqualTo("cl"));
        Assert.That(args, Does.Contain("/std:c++20"));
        Assert.That(args, Does.Contain("/EHsc"));
        Assert.That(args, Does.Contain(@"/IC:\proj\vendor\include"));
        Assert.That(args, Does.Contain("/DMY_FLAG"));
    }
}
```

- [ ] **Step 2: Run to verify failure** (`--filter "FullyQualifiedName~CppToolchainArgsTests"` → compile error)

- [ ] **Step 3: Implement.** Add to `CppToolchain.cs` (same namespace/file, above the class):

```csharp
    public enum CppToolchainKind { ClangLike, Msvc }

    /// <summary>Inputs for a multi-TU native compile (exe or static library).</summary>
    public sealed class CppCompileRequest
    {
        public List<string> SourceFiles { get; } = new List<string>();
        public string OutputPath { get; set; }               // .exe, or .lib/.a for libraries
        public bool LinkExecutable { get; set; } = true;     // false = static library
        public List<string> IncludeDirs { get; } = new List<string>();
        public List<string> Defines { get; } = new List<string>();
        public List<string> Libraries { get; } = new List<string>();
        public string CppStandard { get; set; } = "c++20";
        public string WorkingDirectory { get; set; }
        public bool DebugSymbols { get; set; }
        public bool Optimize { get; set; }
    }
```

Inside `CppToolchain` add:

```csharp
        public CppToolchainKind Kind => _vcvarsPath != null ? CppToolchainKind.Msvc : CppToolchainKind.ClangLike;

        /// <summary>Compiler driver name for compile_commands.json ("clang++", "g++", "cl").</summary>
        public string DriverName => _vcvarsPath != null ? "cl" : _executable;

        /// <summary>
        /// Per-TU compile command (argv, driver first) — single source of truth
        /// shared by the real compile and compile_commands.json emission.
        /// Static + kind-keyed so it is unit-testable without an installed toolchain.
        /// </summary>
        public static List<string> BuildCompileCommandArguments(
            CppToolchainKind kind, string driver, CppCompileRequest request, string sourceFile)
        {
            var args = new List<string> { driver };
            if (kind == CppToolchainKind.Msvc)
            {
                args.Add("/nologo");
                args.Add("/std:" + request.CppStandard);
                args.Add("/EHsc");
                args.Add(request.Optimize ? "/O2" : "/Od");
                if (request.DebugSymbols) args.Add("/Zi");
                foreach (var inc in request.IncludeDirs) args.Add("/I" + inc);
                foreach (var def in request.Defines) args.Add("/D" + def);
                args.Add(sourceFile);
            }
            else
            {
                args.Add("-std=" + request.CppStandard);
                args.Add(request.Optimize ? "-O2" : "-O0");
                if (request.DebugSymbols) args.Add("-g");
                foreach (var inc in request.IncludeDirs) args.Add("-I" + inc);
                foreach (var def in request.Defines) args.Add("-D" + def);
                args.Add(sourceFile);
            }
            return args;
        }

        /// <summary>
        /// Compile a whole project in one toolchain invocation (all TUs on one
        /// command line; Phase 1 has no incremental builds). Executables compile
        /// and link in one step; libraries compile to objects then archive
        /// (llvm-ar/ar for clang/g++, lib.exe inside the vcvars environment).
        /// Known limitation: very large projects could exceed cmd.exe's 8191-char
        /// limit on the MSVC path — acceptable for Phase 1, response files later.
        /// </summary>
        public (bool Success, string Output) Compile(CppCompileRequest request)
        {
            var quotedSources = string.Join(" ", request.SourceFiles.Select(s => "\"" + s + "\""));
            var libs = string.Join(" ", request.Libraries.Select(l => "\"" + l + "\""));
            string arguments;

            if (_vcvarsPath != null)
            {
                var flags = "/nologo /std:" + request.CppStandard + " /EHsc "
                          + (request.Optimize ? "/O2" : "/Od")
                          + (request.DebugSymbols ? " /Zi" : "")
                          + string.Concat(request.IncludeDirs.Select(i => " /I\"" + i + "\""))
                          + string.Concat(request.Defines.Select(d => " /D" + d));
                if (request.LinkExecutable)
                {
                    arguments = "/s /c \"\"" + _vcvarsPath + "\" >nul && cl " + flags + " "
                              + quotedSources + (libs.Length > 0 ? " " + libs : "")
                              + " /Fe:\"" + request.OutputPath + "\"\"";
                }
                else
                {
                    // cl /c into the working dir, then lib.exe archives the .obj files.
                    var objs = string.Join(" ", request.SourceFiles.Select(s =>
                        "\"" + Path.GetFileNameWithoutExtension(s) + ".obj\""));
                    arguments = "/s /c \"\"" + _vcvarsPath + "\" >nul && cl /c " + flags + " "
                              + quotedSources + " && lib /nologo /OUT:\"" + request.OutputPath + "\" " + objs + "\"";
                }
                return RunProcess(_executable, arguments, request.WorkingDirectory, request.OutputPath);
            }

            var gnuFlags = "-std=" + request.CppStandard + " "
                         + (request.Optimize ? "-O2" : "-O0")
                         + (request.DebugSymbols ? " -g" : "")
                         + string.Concat(request.IncludeDirs.Select(i => " -I\"" + i + "\""))
                         + string.Concat(request.Defines.Select(d => " -D" + d));

            if (request.LinkExecutable)
            {
                arguments = gnuFlags + " " + quotedSources
                          + (libs.Length > 0 ? " " + libs : "")
                          + " -o \"" + request.OutputPath + "\"";
                return RunProcess(_executable, arguments, request.WorkingDirectory, request.OutputPath);
            }

            // Library: compile to objects, then archive.
            var compile = RunProcess(_executable, gnuFlags + " -c " + quotedSources,
                request.WorkingDirectory, expectedOutput: null);
            if (!compile.Success) return compile;

            var objNames = string.Join(" ", request.SourceFiles.Select(s =>
                "\"" + Path.GetFileNameWithoutExtension(s) + ".o\""));
            var archiver = FindArchiver();
            if (archiver == null)
                return (false, compile.Output + "\nerror: no archiver (llvm-ar/ar) found on PATH for static library output");
            var archive = RunProcess(archiver, "rcs \"" + request.OutputPath + "\" " + objNames,
                request.WorkingDirectory, request.OutputPath);
            return (archive.Success, compile.Output + archive.Output);
        }

        private static string FindArchiver()
        {
            foreach (var exe in new[] { "llvm-ar", "ar" })
            {
                try
                {
                    using var probe = Process.Start(new ProcessStartInfo(exe, "--version")
                    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true });
                    probe.WaitForExit(10000);
                    if (probe.ExitCode == 0) return exe;
                }
                catch { }
            }
            return null;
        }

        private (bool Success, string Output) RunProcess(
            string executable, string arguments, string workingDirectory, string expectedOutput)
        {
            // NOTE: the existing CompileToExecutable body (:114-147) uses SYNC
            // ReadToEnd() — the deadlock-prone pattern; do NOT copy it. This
            // helper drains stdout/stderr via async reads (compilers overflow
            // the ~4KB pipe buffer with error dumps and deadlock naive code).
            // Success = exit 0 (+ output file exists when expected).
            var psi = new ProcessStartInfo(executable, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workingDirectory)) psi.WorkingDirectory = workingDirectory;

            using var proc = Process.Start(psi);
            var stdOutTask = proc.StandardOutput.ReadToEndAsync();
            var stdErrTask = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(CompileTimeoutMs))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return (false, "error: C++ compile timed out after " + (CompileTimeoutMs / 1000) + "s");
            }
            var output = (stdOutTask.Result + "\n" + stdErrTask.Result).Trim();
            var ok = proc.ExitCode == 0 && (expectedOutput == null || File.Exists(expectedOutput));
            return (ok, output);
        }
```

(If the existing file's `CompileToExecutable` body already has a private run helper, reuse/extend it instead of duplicating — read the remaining lines 114–147 first. Keep the async-drain pattern: compilers overflow the ~4 KB pipe buffer and deadlock naive code.)

- [ ] **Step 4: Run tests** — `CppToolchainArgsTests` PASS; also run `--filter "FullyQualifiedName~CppBackendTests"` to confirm nothing existing broke.

- [ ] **Step 5: Commit**

```bash
git add BasicLang/ProjectSystem/CppToolchain.cs VisualGameStudio.Tests/Compiler/CppToolchainArgsTests.cs
git commit -m "feat(cpp): multi-TU flag-aware CppToolchain.Compile + shared per-TU command construction"
```

---

### Task 4: `CompileCommandsWriter`

**Files:**
- Create: `BasicLang/ProjectSystem/CompileCommandsWriter.cs`
- Test: `VisualGameStudio.Tests/Compiler/CompileCommandsWriterTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Text.Json;
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class CompileCommandsWriterTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Test]
    public void Write_EmitsOneEntryPerTu_WithDirectoryFileArguments()
    {
        var request = new CppCompileRequest
        {
            OutputPath = Path.Combine(_dir, "bin", "App.exe"),
            CppStandard = "c++20",
            WorkingDirectory = _dir,
        };
        request.SourceFiles.Add(Path.Combine(_dir, "main.cpp"));
        request.SourceFiles.Add(Path.Combine(_dir, "util.cpp"));
        request.IncludeDirs.Add(Path.Combine(_dir, "inc"));
        request.Defines.Add("FLAG");

        var path = CompileCommandsWriter.Write(
            _dir, CppToolchainKind.ClangLike, "clang++", request);

        Assert.That(path, Is.EqualTo(Path.Combine(_dir, "obj", "compile_commands.json")));
        Assert.That(File.Exists(path), Is.True);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = doc.RootElement.EnumerateArray().ToList();
        Assert.That(entries, Has.Count.EqualTo(2));
        foreach (var e in entries)
        {
            Assert.That(e.GetProperty("directory").GetString(), Is.EqualTo(_dir));
            var args = e.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToList();
            Assert.That(args[0], Is.EqualTo("clang++"));
            Assert.That(args, Does.Contain("-std=c++20"));
            Assert.That(args, Does.Contain("-I" + Path.Combine(_dir, "inc")));
            Assert.That(args, Does.Contain("-DFLAG"));
        }
        Assert.That(entries.Select(e => e.GetProperty("file").GetString()),
            Is.EquivalentTo(request.SourceFiles));
    }
}
```

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement `CompileCommandsWriter.cs` (complete file)**

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Writes the clangd compilation database (obj/compile_commands.json) from
    /// the same per-TU command lines the toolchain compiles with, so the editor
    /// and the build can never disagree about flags.
    /// </summary>
    public static class CompileCommandsWriter
    {
        public static string Write(
            string projectDir, CppToolchainKind kind, string driver, CppCompileRequest request)
        {
            var objDir = Path.Combine(projectDir, "obj");
            Directory.CreateDirectory(objDir);

            var entries = new List<object>();
            foreach (var tu in request.SourceFiles)
            {
                entries.Add(new
                {
                    directory = projectDir,
                    file = tu,
                    arguments = CppToolchain.BuildCompileCommandArguments(kind, driver, request, tu),
                });
            }

            var path = Path.Combine(objDir, "compile_commands.json");
            File.WriteAllText(path, JsonSerializer.Serialize(entries,
                new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }
    }
}
```

- [ ] **Step 4: Run tests** — PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(cpp): compile_commands.json writer fed by toolchain command construction"`

---

### Task 5: Engine artifact lookup for user C++ (`EngineDeployment.LocateImportLib`)

**Files:**
- Modify: `BasicLang/ProjectSystem/EngineDeployment.cs`

Dev trees don't have `VisualGameStudioEngine.lib` next to `BasicLang/bin/.../BasicLang.exe`; the IDE privately probes `x64/{Release,Debug}` (BuildService.cs:1127–1138). Give the shared layer the same reach.

- [ ] **Step 1: Add to `EngineDeployment` (test comes via Task 6's builder tests + Task 7's game template e2e):**

```csharp
        /// <summary>
        /// Locate the engine import library: next to the running binaries first
        /// (installed layout — IDE/ ships the .lib), then walking up from the
        /// base directory looking for a dev-tree x64/{Release,Debug} build.
        /// Null when not found anywhere.
        /// </summary>
        public static string LocateImportLib()
        {
            var direct = GetImportLibPath(AppContext.BaseDirectory);
            if (direct != null) return direct;

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
            {
                foreach (var config in new[] { "Release", "Debug" })
                {
                    var candidate = Path.Combine(dir.FullName, "x64", config, EngineImportLibName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        /// <summary>Native DLLs matching wherever the import lib was found (same dir), falling back to the base-directory set.</summary>
        public static List<string> LocateNativeDlls(string importLibPath)
        {
            if (importLibPath != null)
            {
                var dir = Path.GetDirectoryName(importLibPath);
                var fromLibDir = GetNativeDllPaths(dir);
                if (fromLibDir.Count > 0) return fromLibDir;
            }
            return GetNativeDllPaths(AppContext.BaseDirectory);
        }
```

(Match the file's existing style; `GetImportLibPath`/`GetNativeDllPaths`/`EngineImportLibName` already exist at :76/:130/:38.)

- [ ] **Step 2: Build** — `dotnet build BasicLang/BasicLang.csproj -c Release` → 0 errors.
- [ ] **Step 3: Commit** — `git commit -m "feat(cpp): dev-tree-aware engine import-lib lookup in EngineDeployment"`

---

### Task 6: `CppProjectBuilder` — the shared native build orchestrator

**Files:**
- Create: `BasicLang/ProjectSystem/CppProjectBuilder.cs`
- Test: `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs` (created here with builder-level tests; CLI-spawning tests added in Task 7)

- [ ] **Step 1: Write failing builder-level tests (in-process, toolchain-guarded)**

```csharp
using BasicLang.Compiler.ProjectSystem;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
[NonParallelizable]
public class CppProjectCliBuildTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-cppbuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        for (var i = 0; i < 3; i++)
        {
            try { Directory.Delete(_dir, recursive: true); return; }
            catch { Thread.Sleep(200); }
        }
    }

    private ProjectFile MakeCppProject(params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
        {
            var full = Path.Combine(_dir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        var blproj = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(blproj, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
            </BasicLangProject>
            """);
        return ProjectFile.Load(blproj);
    }

    [Test]
    public void Build_MultiFileProject_ProducesRunnableExe_AndCompileCommands()
    {
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");

        var project = MakeCppProject(
            ("main.cpp", """
                #include <iostream>
                #include "util.h"
                int main() { std::cout << "sum=" << Add(2, 3) << std::endl; return 0; }
                """),
            ("util.cpp", """
                #include "util.h"
                int Add(int a, int b) { return a + b; }
                """),
            ("util.h", "int Add(int a, int b);\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.True, "build failed:\n" + result.RawToolchainOutput
            + "\n" + string.Join("\n", result.Diagnostics.Select(CppDiagnosticsParser.FormatNormalized)));
        Assert.That(result.ExecutablePath, Does.EndWith("App.exe"));
        Assert.That(File.Exists(result.ExecutablePath), Is.True);
        Assert.That(File.Exists(Path.Combine(_dir, "obj", "compile_commands.json")), Is.True);

        var psi = new System.Diagnostics.ProcessStartInfo(result.ExecutablePath!)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.That(stdout, Does.Contain("sum=5"));
    }

    [Test]
    public void Build_CompileError_YieldsFileLineDiagnostic()
    {
        if (CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");

        var project = MakeCppProject(("main.cpp", "int main() { undeclared_symbol; return 0; }\n"));

        var result = CppProjectBuilder.Build(project, "Debug");

        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics, Is.Not.Empty, "raw output:\n" + result.RawToolchainOutput);
        var d = result.Diagnostics.First(x => !x.IsWarning);
        Assert.That(d.FilePath, Does.EndWith("main.cpp"));
        Assert.That(d.Line, Is.EqualTo(1));
    }

    [Test]
    public void Build_NoCppSources_FailsWithBL6007()
    {
        var project = MakeCppProject(); // no source files at all
        var result = CppProjectBuilder.Build(project, "Debug");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6007"));
    }

    [Test]
    public void Build_BasicLangSourcesPresent_FailsWithBL6008()
    {
        var project = MakeCppProject(("main.cpp", "int main() { return 0; }\n"),
                                     ("logic.bas", "Module M\nEnd Module\n"));
        var result = CppProjectBuilder.Build(project, "Debug");
        Assert.That(result.Success, Is.False);
        Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6008"));
        Assert.That(result.Diagnostics.First(d => d.Code == "BL6008").Message, Does.Contain("logic.bas"));
    }

    [Test]
    public void Build_NoToolchain_FailsWithBL6005()
    {
        // Only assertable on machines without a toolchain; on machines with one,
        // assert the inverse (a toolchain build never emits BL6005).
        var project = MakeCppProject(("main.cpp", "int main() { return 0; }\n"));
        var result = CppProjectBuilder.Build(project, "Debug");
        if (CppToolchain.Find() == null)
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain("BL6005"));
            Assert.That(result.Diagnostics.First(d => d.Code == "BL6005").Message,
                Does.Contain("clang").And.Contain("g++").And.Contain("MSVC"));
        }
        else
        {
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Not.Contain("BL6005"));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure** (`--filter "FullyQualifiedName~CppProjectCliBuildTests"`).

- [ ] **Step 3: Implement `CppProjectBuilder.cs` (complete file)**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BasicLang.Compiler.ProjectSystem
{
    public sealed class CppProjectBuildResult
    {
        public bool Success { get; set; }
        public string ExecutablePath { get; set; }    // set for OutputType=Exe on success
        public string OutputPath { get; set; }        // output dir
        public List<CppDiagnostic> Diagnostics { get; } = new List<CppDiagnostic>();
        public List<string> Messages { get; } = new List<string>();   // progress lines for CLI/IDE output
        public string RawToolchainOutput { get; set; } = "";
    }

    /// <summary>
    /// Builds a Language=Cpp project: gathers translation units, resolves
    /// includes/libs, emits compile_commands.json, drives CppToolchain, parses
    /// diagnostics, deploys engine DLLs. The ONLY native-project build path —
    /// shared verbatim by the CLI (Program.cs) and the IDE (BuildService) so the
    /// two entry points cannot drift.
    /// </summary>
    public static class CppProjectBuilder
    {
        public static CppProjectBuildResult Build(ProjectFile project, string configuration)
        {
            var result = new CppProjectBuildResult();
            var projectDir = Path.GetDirectoryName(project.FilePath) ?? ".";
            var outputName = project.AssemblyName ?? project.ProjectName ?? "Program";
            var isExe = !string.Equals(project.OutputType, "Library", StringComparison.OrdinalIgnoreCase);

            // Native projects use bin/<config> in BOTH entry points (no TFM — it
            // is meaningless for native output).
            var outputDir = Path.Combine(projectDir, "bin", configuration);
            Directory.CreateDirectory(outputDir);
            result.OutputPath = outputDir;

            // ---- Phase 1 guard: no BasicLang sources in a C++ project ----
            var strayBas = project.GetSourceFiles()
                .Where(f => !ProjectFile.IsInBuildOutputDir(projectDir, f))
                .Where(f => new[] { ".bas", ".bl", ".basic", ".mod", ".cls", ".class" }
                    .Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (strayBas.Count > 0)
            {
                Fail(result, "BL6008",
                    "BasicLang sources in a C++ project are not supported yet (mixed projects arrive in a later phase): "
                    + string.Join(", ", strayBas.Select(Path.GetFileName)), project.FilePath);
                return result;
            }

            // ---- Translation units ----
            var tus = project.GetCppTranslationUnits().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tus.Count == 0)
            {
                Fail(result, "BL6007",
                    "No C++ source files found (looked for " + string.Join("/", ProjectFile.CppTranslationUnitExtensions)
                    + " under " + projectDir + ", excluding bin/ and obj/).", project.FilePath);
                return result;
            }

            // ---- Toolchain (hard requirement for Language=Cpp) ----
            var toolchain = CppToolchain.Find();
            if (toolchain == null)
            {
                Fail(result, "BL6005",
                    "No C++ toolchain found. Probed: clang++ (PATH), g++ (PATH), MSVC (vswhere). "
                    + "Install LLVM/clang (https://releases.llvm.org), MinGW-w64, or Visual Studio Build Tools.",
                    project.FilePath);
                return result;
            }
            result.Messages.Add($"Compiling C++ with {toolchain.DisplayName}...");

            // ---- Request ----
            var request = new CppCompileRequest
            {
                OutputPath = Path.Combine(outputDir, outputName + (isExe ? ".exe"
                    : toolchain.Kind == CppToolchainKind.Msvc ? ".lib" : ".a")),
                LinkExecutable = isExe,
                CppStandard = project.CppStandard,
                WorkingDirectory = outputDir,
                DebugSymbols = true,
                Optimize = false,
            };
            if (project.Configurations.TryGetValue(configuration, out var config))
            {
                request.Optimize = config.OptimizationsEnabled;
                request.DebugSymbols = config.DebugSymbols;
            }
            request.SourceFiles.AddRange(tus);

            request.IncludeDirs.Add(projectDir);
            foreach (var inc in project.IncludeDirs)
                request.IncludeDirs.Add(Path.IsPathRooted(inc) ? inc : Path.Combine(projectDir, inc));
            request.Defines.AddRange(project.Defines);

            // ---- Native libs (engine lib resolves via EngineDeployment) ----
            string engineLib = null;
            foreach (var lib in project.NativeLibs)
            {
                var local = Path.IsPathRooted(lib) ? lib : Path.Combine(projectDir, lib);
                if (File.Exists(local)) { request.Libraries.Add(local); continue; }

                if (string.Equals(Path.GetFileName(lib), EngineDeployment.EngineImportLibName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    engineLib = EngineDeployment.LocateImportLib();
                    if (engineLib != null) { request.Libraries.Add(engineLib); continue; }
                }
                Fail(result, "BL6009", $"Native library not found: {lib}", project.FilePath);
                return result;
            }

            // ---- compile_commands.json (before compiling — clangd wants it even on failure) ----
            var ccPath = CompileCommandsWriter.Write(projectDir, toolchain.Kind, toolchain.DriverName, request);
            result.Messages.Add($"Compilation database: {ccPath}");

            // ---- Compile ----
            var (ok, output) = toolchain.Compile(request);
            result.RawToolchainOutput = output;
            result.Diagnostics.AddRange(CppDiagnosticsParser.Parse(output, outputDir));

            if (!ok)
            {
                if (!result.Diagnostics.Any(d => !d.IsWarning))
                {
                    // Nothing parseable — surface the raw blob so the failure is never silent.
                    Fail(result, "BL6006", "C++ compilation failed: " + output, project.FilePath);
                }
                result.Success = false;
                return result;
            }

            result.Success = true;
            if (isExe) result.ExecutablePath = request.OutputPath;
            result.Messages.Add($"Output: {request.OutputPath}");

            // ---- Engine DLL deploy ----
            if (engineLib != null)
            {
                foreach (var dll in EngineDeployment.LocateNativeDlls(engineLib))
                {
                    var dest = Path.Combine(outputDir, Path.GetFileName(dll));
                    File.Copy(dll, dest, overwrite: true);
                    result.Messages.Add($"Deployed {Path.GetFileName(dll)}");
                }
            }
            return result;
        }

        private static void Fail(CppProjectBuildResult result, string code, string message, string filePath)
        {
            result.Success = false;
            result.Diagnostics.Add(new CppDiagnostic
            { FilePath = filePath, Line = 0, Column = 0, IsWarning = false, Code = code, Message = message });
        }
    }
}
```

- [ ] **Step 4: Run tests** — all 5 PASS (2 skip on toolchain-less machines).
- [ ] **Step 5: Commit** — `git commit -m "feat(cpp): shared CppProjectBuilder (TU gather, toolchain, diagnostics, compile_commands, engine deploy)"`

---

### Task 7: CLI wiring — `build` routes to the builder, `run` finds native exes, templates

**Files:**
- Modify: `BasicLang/Program.cs` (`HandleBuildCommand` :406, `HandleRunCommand` :820–884)
- Modify: `BasicLang/ProjectSystem/TemplateEngine.cs` (`RegisterBuiltInTemplates` :40–430)
- Test: extend `VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs`

- [ ] **Step 1: Write the failing CLI e2e tests (append to `CppProjectCliBuildTests`)**

```csharp
    private static string CliPath()
    {
        var cliPath = Path.Combine(AppContext.BaseDirectory, "BasicLang.exe");
        Assert.That(File.Exists(cliPath), Is.True,
            "BasicLang.exe not deployed next to the tests — project reference output changed?");
        return cliPath;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCli(
        string workingDir, params string[] args)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = CliPath(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
            }
        };
        foreach (var a in args) process.StartInfo.ArgumentList.Add(a);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token);
        return (process.ExitCode, await stdout, await stderr);
    }

    [Test]
    public async Task Cli_Build_CppProject_ProducesExe()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var project = MakeCppProject(("main.cpp", "#include <iostream>\nint main(){ std::cout << \"hi\"; return 0; }\n"));

        var (exit, stdout, stderr) = await RunCli(_dir, "build", project.FilePath);

        Assert.That(exit, Is.EqualTo(0), $"STDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.That(Directory.GetFiles(_dir, "App.exe", SearchOption.AllDirectories), Is.Not.Empty);
    }

    [Test]
    public async Task Cli_Build_CppCompileError_PrintsNormalizedDiagnostic()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var project = MakeCppProject(("main.cpp", "int main() { undeclared_symbol; return 0; }\n"));

        var (exit, stdout, stderr) = await RunCli(_dir, "build", project.FilePath);

        Assert.That(exit, Is.Not.EqualTo(0));
        // Normalized MSBuild-style location: main.cpp(1,...): error ...
        Assert.That(stdout + stderr, Does.Match(@"main\.cpp\(1[,)]"),
            $"expected a normalized file(line[,col]) diagnostic.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
    }

    [Test]
    public async Task Cli_New_CppConsole_Builds_And_Runs()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");

        var (exitNew, so, se) = await RunCli(_dir, "new", "cpp-console", "-n", "HelloCpp", "-o",
            Path.Combine(_dir, "HelloCpp"));
        Assert.That(exitNew, Is.EqualTo(0), $"new failed:\n{so}\n{se}");
        var blproj = Path.Combine(_dir, "HelloCpp", "HelloCpp.blproj");
        Assert.That(File.Exists(blproj), Is.True);
        Assert.That(File.ReadAllText(blproj), Does.Contain("<Language>Cpp</Language>"));

        var (exitBuild, so2, se2) = await RunCli(Path.Combine(_dir, "HelloCpp"), "build", blproj);
        Assert.That(exitBuild, Is.EqualTo(0), $"build failed:\n{so2}\n{se2}");

        var exe = Directory.GetFiles(Path.Combine(_dir, "HelloCpp"), "HelloCpp.exe", SearchOption.AllDirectories).Single();
        var psi = new System.Diagnostics.ProcessStartInfo(exe)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(30000);
        Assert.That(output, Does.Contain("Hello from HelloCpp"));
    }

    [Test]
    public async Task Cli_New_CppLibrary_Builds()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        var (exitNew, _, _) = await RunCli(_dir, "new", "cpp-library", "-n", "MathLib", "-o",
            Path.Combine(_dir, "MathLib"));
        Assert.That(exitNew, Is.EqualTo(0));
        var blproj = Path.Combine(_dir, "MathLib", "MathLib.blproj");
        var (exitBuild, so, se) = await RunCli(Path.Combine(_dir, "MathLib"), "build", blproj);
        Assert.That(exitBuild, Is.EqualTo(0), $"library build failed:\n{so}\n{se}");
        Assert.That(Directory.GetFiles(Path.Combine(_dir, "MathLib"), "MathLib.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".a") || f.EndsWith(".lib")), Is.Not.Empty);
    }

    [Test]
    public async Task Cli_New_CppGame_Builds_WhenEngineLibAvailable()
    {
        if (CppToolchain.Find() == null) Assert.Ignore("No C++ toolchain available");
        if (BasicLang.Compiler.ProjectSystem.EngineDeployment.LocateImportLib() == null)
            Assert.Ignore("VisualGameStudioEngine.lib not found (engine not built on this machine)");

        var (exitNew, _, _) = await RunCli(_dir, "new", "cpp-game", "-n", "MyGame", "-o",
            Path.Combine(_dir, "MyGame"));
        Assert.That(exitNew, Is.EqualTo(0));
        var blproj = Path.Combine(_dir, "MyGame", "MyGame.blproj");
        var (exitBuild, so, se) = await RunCli(Path.Combine(_dir, "MyGame"), "build", blproj);
        Assert.That(exitBuild, Is.EqualTo(0), $"game build failed:\n{so}\n{se}");
        var exeDir = Path.GetDirectoryName(Directory.GetFiles(
            Path.Combine(_dir, "MyGame"), "MyGame.exe", SearchOption.AllDirectories).Single())!;
        Assert.That(File.Exists(Path.Combine(exeDir, "VisualGameStudioEngine.dll")), Is.True,
            "engine DLL must be deployed next to the game exe");
        // Do NOT run the game exe — it opens a window.
    }
```

- [ ] **Step 2: Run to verify failure** (build routes into the BasicLang pipeline and fails, `new cpp-console` says unknown template).

- [ ] **Step 3a: Route `HandleBuildCommand`.** In `BasicLang/Program.cs`, the NuGet restore (:419–427) currently runs **before** configuration resolution (:430–437). First **move the restore block below the configuration resolution**, then insert this branch between configuration resolution and the (now-later) restore — C++ projects skip restore entirely:

```csharp
            // ---------- Language=Cpp: user-authored C++, no BasicLang pipeline ----------
            if (project.IsCppProject)
            {
                var cppResult = ProjectSystem.CppProjectBuilder.Build(project, configuration);
                foreach (var msg in cppResult.Messages)
                    Console.WriteLine("  " + msg);
                foreach (var diag in cppResult.Diagnostics)
                {
                    var line = "  " + ProjectSystem.CppDiagnosticsParser.FormatNormalized(diag);
                    if (diag.IsWarning) Console.WriteLine(line);
                    else Console.Error.WriteLine(line);
                }
                if (!cppResult.Success)
                {
                    Console.Error.WriteLine("  Build failed.");
                    return 1;
                }
                Console.WriteLine("  Build succeeded.");
                return 0;
            }
```

(Move the restore call after this branch if it currently precedes the insertion point; keep the existing behavior for non-C++ projects byte-identical.)

- [ ] **Step 3b: Fix `HandleRunCommand`** (:837–847). Extend `possiblePaths` so native exes are found and run directly (not via `dotnet`):

```csharp
            // NOTE: outputDir here is ALREADY projectDir/bin/<config>/<TFM>
            // (Program.cs:835) — base the native entries on projectDir, or the
            // paths double up (bin/Debug/net8.0/bin/Debug/...).
            var possiblePaths = new[]
            {
                Path.Combine(outputDir, $"{exeName}.dll"),
                Path.Combine(outputDir, "bin", configuration, project.TargetFramework, $"{exeName}.dll"),
                Path.Combine(projectDir, "bin", configuration, $"{exeName}.exe"),   // native layout (CppProjectBuilder)
                Path.Combine(outputDir, $"{exeName}.exe"),
            };
```

and where the found path is launched, branch: `.exe` → `Process.Start(exePath)` directly with redirected output; `.dll` → existing `dotnet` launch. (Read the surrounding launch code at :850–884 first and mirror its output handling.)

- [ ] **Step 3c: Register CLI templates.** In `TemplateEngine.RegisterBuiltInTemplates`, add three entries following the existing embedded-string pattern (`[\"{{ProjectName}}.blproj\"] = @"..."`). Exact template files:

**`cpp-console`** (`Name`: "C++ Console Application", `ShortName`: "cpp-console", `DefaultProjectName`: "MyCppApp", Tags: cpp, console):
- `{{ProjectName}}.blproj`:
```xml
<BasicLangProject Version="1.0">
  <PropertyGroup>
    <ProjectName>{{ProjectName}}</ProjectName>
    <OutputType>Exe</OutputType>
    <Language>Cpp</Language>
    <CppStandard>c++20</CppStandard>
    <TargetBackend>Cpp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="main.cpp" />
  </ItemGroup>
</BasicLangProject>
```
- `main.cpp`:
```cpp
#include <iostream>

int main()
{
    std::cout << "Hello from {{ProjectName}}!" << std::endl;
    return 0;
}
```
- `.gitignore`: `bin/\nobj/\n`

**`cpp-library`** ("C++ Static Library", ShortName "cpp-library"):
- `.blproj` as above but `<OutputType>Library</OutputType>` and Compile items `mathutils.cpp`;
- `mathutils.h`: `#pragma once\nint Add(int a, int b);\n`
- `mathutils.cpp`: `#include "mathutils.h"\nint Add(int a, int b) { return a + b; }\n`

**`cpp-game`** ("C++ Game (VGS Engine)", ShortName "cpp-game"):
- `.blproj` as cpp-console plus `<NativeLib Include="VisualGameStudioEngine.lib" />` in the ItemGroup;
- `main.cpp` (declarations verified against `VisualGameStudioEngine/framework.h:280–299` — plain `extern "C"` is sufficient for import-lib linking; `framework.h` itself is not consumable because it includes `pch.h`):
```cpp
// Engine C-ABI declarations (see VisualGameStudioEngine/framework.h).
extern "C" {
    bool Framework_Initialize(int width, int height, const char* title);
    void Framework_Update();
    bool Framework_ShouldClose();
    void Framework_Shutdown();
    void Framework_BeginDrawing();
    void Framework_EndDrawing();
    void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a);
    void Framework_DrawText(const char* text, int x, int y, int fontSize,
                            unsigned char r, unsigned char g, unsigned char b, unsigned char a);
}

int main()
{
    if (!Framework_Initialize(800, 450, "{{ProjectName}}"))
        return 1;

    while (!Framework_ShouldClose())
    {
        Framework_Update();
        Framework_BeginDrawing();
        Framework_ClearBackground(30, 30, 46, 255);
        Framework_DrawText("Hello from {{ProjectName}}!", 260, 200, 24, 205, 214, 244, 255);
        Framework_EndDrawing();
    }

    Framework_Shutdown();
    return 0;
}
```

- [ ] **Step 4: Run tests** — `--filter "FullyQualifiedName~CppProjectCliBuildTests"` → all PASS (skips where guarded). Then the neighboring fixtures: `--filter "FullyQualifiedName~CliBuildTests|FullyQualifiedName~TemplateBuildSweepTests"` → still green (no regressions in the BasicLang path).

- [ ] **Step 5: Commit**

```bash
git add BasicLang/Program.cs BasicLang/ProjectSystem/TemplateEngine.cs VisualGameStudio.Tests/Compiler/CppProjectCliBuildTests.cs
git commit -m "feat(cpp): CLI build/run/new support for Language=Cpp projects (console, library, game templates)"
```

---

### Task 8: IDE model + serializer — `Language` and C++ settings survive the IDE

**Files:**
- Modify: `VisualGameStudio.Core/Models/BasicLangProject.cs` (enum at :47, properties near :10)
- Modify: `VisualGameStudio.ProjectSystem/Serialization/ProjectSerializer.cs` (parse :35–48 area + item loop; write :140–160 area)
- Test: `VisualGameStudio.Tests/Serialization/ProjectSerializerCppTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;

namespace VisualGameStudio.Tests.Serialization;

[TestFixture]
public class ProjectSerializerCppTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-ideser-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown() { try { Directory.Delete(_dir, true); } catch { } }

    [Test]
    public async Task Load_ParsesLanguageAndCppSettings()
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <CppStandard>c++20</CppStandard>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <IncludeDir Include="inc" />
                <NativeLib Include="VisualGameStudioEngine.lib" />
                <Define Include="FLAG" />
              </ItemGroup>
            </BasicLangProject>
            """);

        var project = await new ProjectSerializer().LoadAsync(path);

        Assert.That(project.Language, Is.EqualTo(ProjectLanguage.Cpp));
        Assert.That(project.CppSettings, Is.Not.Null);
        Assert.That(project.CppSettings!.CppStandard, Is.EqualTo("c++20"));
        Assert.That(project.CppSettings.IncludeDirs, Is.EqualTo(new[] { "inc" }));
        Assert.That(project.CppSettings.NativeLibs, Is.EqualTo(new[] { "VisualGameStudioEngine.lib" }));
        Assert.That(project.CppSettings.Defines, Is.EqualTo(new[] { "FLAG" }));
    }

    [Test]
    public async Task SaveThenLoad_DoesNotStripLanguageOrCppItems()
    {
        var path = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
                <IncludeDir Include="inc" />
              </ItemGroup>
            </BasicLangProject>
            """);
        var serializer = new ProjectSerializer();
        var project = await serializer.LoadAsync(path);

        await serializer.SaveAsync(project);   // the IDE save path that used to strip unknown elements
        var reloaded = await serializer.LoadAsync(path);

        Assert.That(reloaded.Language, Is.EqualTo(ProjectLanguage.Cpp),
            "an IDE save must not strip <Language> from a C++ project");
        Assert.That(reloaded.CppSettings!.IncludeDirs, Is.EqualTo(new[] { "inc" }));
    }

    [Test]
    public async Task Load_DefaultsToBasicLang_ForOldProjects()
    {
        var path = Path.Combine(_dir, "Old.blproj");
        File.WriteAllText(path, """
            <BasicLangProject Version="1.0">
              <PropertyGroup><ProjectName>Old</ProjectName></PropertyGroup>
            </BasicLangProject>
            """);
        var project = await new ProjectSerializer().LoadAsync(path);
        Assert.That(project.Language, Is.EqualTo(ProjectLanguage.BasicLang));
        Assert.That(project.CppSettings, Is.Null);
    }
}
```

(Adjust `SaveAsync` call to its actual signature — read ProjectSerializer.cs:140 first; it may take `(project, path)`.)

- [ ] **Step 2: Run to verify failure.**

- [ ] **Step 3: Implement.**

3a. `BasicLangProject.cs` — add next to `TargetBackend`:

```csharp
    public ProjectLanguage Language { get; set; } = ProjectLanguage.BasicLang;

    /// <summary>C++-only settings; null for BasicLang projects. Modeled so IDE saves round-trip them.</summary>
    public CppProjectSettings? CppSettings { get; set; }
```

and below the `TargetBackend` enum:

```csharp
public enum ProjectLanguage
{
    BasicLang,
    Cpp
}

public class CppProjectSettings
{
    public string CppStandard { get; set; } = "c++20";
    public List<string> IncludeDirs { get; set; } = new();
    public List<string> NativeLibs { get; set; } = new();
    public List<string> Defines { get; set; } = new();
}
```

3b. `ProjectSerializer.cs` — in the PropertyGroup parse (next to the TargetBackend parse at :44–48):

```csharp
            var language = propertyGroup.Element("Language")?.Value;
            if (!string.IsNullOrEmpty(language) &&
                Enum.TryParse<ProjectLanguage>(language, true, out var lang))
            {
                project.Language = lang;
            }
            if (project.Language == ProjectLanguage.Cpp)
            {
                project.CppSettings = new CppProjectSettings
                {
                    CppStandard = propertyGroup.Element("CppStandard")?.Value ?? "c++20"
                };
            }
```

In the ItemGroup parse loop (near Compile items :73–80): collect `IncludeDir`/`NativeLib`/`Define` Include attributes into `project.CppSettings` (create it lazily if items appear without `<Language>`).

In `SaveAsync` (:140+): after writing `TargetBackend` (:152), when `project.Language == ProjectLanguage.Cpp` write `<Language>Cpp</Language>` + `<CppStandard>`; after the Compile/Content item emission, emit an ItemGroup with the `CppSettings` items when non-empty (same shapes as Task 1's Save).

- [ ] **Step 4: Run tests** — 3 PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(cpp): IDE project model + serializer round-trip Language and C++ settings"`

---

### Task 9: IDE build routing — `BuildService` → shared `CppProjectBuilder`

**Files:**
- Modify: `VisualGameStudio.ProjectSystem/Services/BuildService.cs` (branch in `BuildInternalAsync` before the empty-source check at :332–347; new private method)
- Test: extend `VisualGameStudio.Tests/Services/BuildServicePipelineTests.cs`

- [ ] **Step 1: Write the failing tests (append to `BuildServicePipelineTests`)**

```csharp
    private async Task<BasicLangProject> CreateCppProjectOnDisk(string name, string mainCpp)
    {
        var dir = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "main.cpp"), mainCpp);
        var blproj = Path.Combine(dir, name + ".blproj");
        File.WriteAllText(blproj, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>{name}</ProjectName>
                <OutputType>Exe</OutputType>
                <Language>Cpp</Language>
                <TargetBackend>Cpp</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="main.cpp" />
              </ItemGroup>
            </BasicLangProject>
            """);
        return await new ProjectSerializer().LoadAsync(blproj);
    }

    [Test]
    public async Task Build_CppLanguageProject_ProducesRunnableExe()
    {
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");

        var project = await CreateCppProjectOnDisk("IdeCppRun",
            "#include <iostream>\nint main(){ std::cout << \"ide-cpp-ok\"; return 0; }\n");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True, Describe(result, output));
        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty, Describe(result, output));
        Assert.That(File.Exists(result.ExecutablePath), Is.True);

        var psi = new System.Diagnostics.ProcessStartInfo(result.ExecutablePath!)
        { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        proc.WaitForExit(30000);
        Assert.That(stdout, Does.Contain("ide-cpp-ok"));
    }

    [Test]
    public async Task Build_CppLanguageProject_CompileError_DiagnosticHasFileLineColumn()
    {
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");

        var project = await CreateCppProjectOnDisk("IdeCppErr",
            "int main() { undeclared_symbol; return 0; }\n");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.False);
        var d = result.Diagnostics.FirstOrDefault(x =>
            x.Severity == DiagnosticSeverity.Error && x.FilePath?.EndsWith("main.cpp") == true);
        Assert.That(d, Is.Not.Null, "expected a per-file diagnostic, got:\n" + Describe(result, output));
        Assert.That(d!.Line, Is.EqualTo(1));
    }

    [Test]
    public async Task Build_CppLanguageProject_NoToolchain_IsHardErrorNotSourceOnlySuccess()
    {
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() != null)
            Assert.Ignore("Toolchain installed — the no-toolchain contract is only assertable without one");

        var project = await CreateCppProjectOnDisk("IdeCppNoTc", "int main(){ return 0; }\n");
        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.False,
            "Language=Cpp must hard-fail without a toolchain (unlike the transpile backend's BL6002 soft warning)");
        Assert.That(result.Diagnostics.Select(x => x.Id), Does.Contain("BL6005"));
    }
```

- [ ] **Step 2: Run to verify failure** — the first test fails with BL0001 or the BasicLang pipeline choking on `.cpp`.

- [ ] **Step 3: Implement.** In `BuildInternalAsync`: the `BuildCompleted` event fires only when execution **falls through** to the shared finalization code after the compile call (:369) — and `MainWindowViewModel` publishes to the Error List *solely* via `BuildCompleted` (subscribed :482, handler :1274). **Do NOT `return` early** or C++ diagnostics silently never reach the Error List. Structure the branch as if/else around the existing sourceFiles + `CompileWithBasicLangApiAsync` block (:332–347) so both paths fall through to the same logging + `BuildCompleted` finalization:

```csharp
            // ---------- Language=Cpp: route to the shared native builder ----------
            if (project.Language == ProjectLanguage.Cpp)
            {
                result = await Task.Run(() => BuildCppProject(project), _buildCts.Token);
            }
            else
            {
                // ... existing sourceFiles check + CompileWithBasicLangApiAsync call,
                // unchanged, moved into this else block ...
            }
            // execution falls through to the existing duration/success logging and
            // BuildCompleted?.Invoke — shared by both languages.
```

New private method (place near `CompileGeneratedCpp` :1021):

```csharp
        /// <summary>
        /// Native C++ project build. Delegates to the SAME CppProjectBuilder the
        /// CLI uses (BasicLang.Compiler.ProjectSystem) — no IDE-side reimplementation.
        /// </summary>
        private BuildResult BuildCppProject(BasicLangProject project)
        {
            var result = new BuildResult();
            var configuration = CurrentConfiguration?.Name ?? "Debug";

            _outputService.WriteLine($"Building C++ project {project.Name} [{configuration}]...", OutputCategory.Build);

            var projectFile = BasicLang.Compiler.ProjectSystem.ProjectFile.Load(project.FilePath);
            var cpp = BasicLang.Compiler.ProjectSystem.CppProjectBuilder.Build(projectFile, configuration);

            foreach (var msg in cpp.Messages)
                _outputService.WriteLine(msg, OutputCategory.Build);

            foreach (var diag in cpp.Diagnostics)
            {
                result.Diagnostics.Add(new DiagnosticItem
                {
                    Id = diag.Code,
                    Message = diag.Message,
                    FilePath = diag.FilePath,
                    Line = diag.Line,
                    Column = diag.Column,
                    Severity = diag.IsWarning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    Source = "cpp"
                });
                // Echo in the OutputPanel's clickable format.
                _outputService.WriteLine("  " +
                    BasicLang.Compiler.ProjectSystem.CppDiagnosticsParser.FormatNormalized(diag),
                    OutputCategory.Build);
            }

            result.Success = cpp.Success;
            result.ExecutablePath = cpp.ExecutablePath;
            result.OutputPath = cpp.OutputPath;
            return result;
        }
```

(Check how `BuildInternalAsync` finalizes results — BuildCompleted firing, IsBuilding flags — and return through the same path so the Error List publication at MainWindowViewModel.OnBuildCompleted still runs. Loading via the compiler-side `ProjectFile.Load` is deliberate: the CLI parser owns C++ item semantics; the IDE model's `Language` is only the routing key.)

- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~BuildServicePipelineTests"` → new tests pass, existing 6+ tests still green.
- [ ] **Step 5: Commit** — `git commit -m "feat(cpp): IDE BuildService routes Language=Cpp through shared CppProjectBuilder with per-line diagnostics"`

---

### Task 10: IDE templates + New Project language picker

**Files:**
- Modify: `VisualGameStudio.Core/Abstractions/Services/IProjectTemplateService.cs` (SolutionTypes :105–167, ProjectTemplates :304–450)
- Modify: `VisualGameStudio.ProjectSystem/Services/ProjectTemplateService.cs` (`GenerateProjectFileContent` :257, `GetOutputType` :339, `GetCompileItems` :355, `GenerateSourceFilesAsync` :371)
- Test: extend `VisualGameStudio.Tests/Services/TemplateBuildSweepTests.cs`

- [ ] **Step 1: Write the failing sweep tests.** In `TemplateBuildSweepTests`, add three `[TestCase]`s and make the solution type per-case:

```csharp
    [TestCase("cpp-console-app")]
    [TestCase("cpp-library")]
    [TestCase("cpp-game-app")]
    public async Task CppTemplate_CreatesProject_ThatCompilerBuilds(string templateId)
    {
        var compiler = FindCompiler();
        if (compiler == null)
            Assert.Inconclusive("BasicLang.exe not built; run 'dotnet build BasicLang -c Release' first.");
        if (BasicLang.Compiler.ProjectSystem.CppToolchain.Find() == null)
            Assert.Ignore("No C++ toolchain available (clang++/g++/MSVC)");
        if (templateId == "cpp-game-app" &&
            BasicLang.Compiler.ProjectSystem.EngineDeployment.LocateImportLib() == null)
            Assert.Ignore("VisualGameStudioEngine.lib not found (engine not built)");

        var template = ProjectTemplates.All.Single(t => t.Id == templateId);
        var name = "Sweep" + string.Concat(templateId.Split('-').Select(
            p => char.ToUpperInvariant(p[0]) + p.Substring(1)));

        var options = new CreateProjectOptions
        {
            Name = name,
            Location = _rootDir,
            Template = template,
            SolutionType = SolutionTypes.Cpp,
            CreateSolutionFolder = true,
            CreateGitRepository = false
        };
        var result = await _service.CreateProjectAsync(options);
        Assert.That(result.Success, Is.True, $"project creation failed: {result.Error}");
        Assert.That(File.ReadAllText(result.ProjectPath!), Does.Contain("<Language>Cpp</Language>"));

        var (exitCode, output) = RunCompilerBuild(compiler, result.ProjectPath!);
        Assert.That(exitCode, Is.EqualTo(0),
            $"'{templateId}' project failed to build.\n--- compiler output ---\n{output}");
    }
```

- [ ] **Step 2: Run to verify failure** (`SolutionTypes.Cpp` doesn't exist).

- [ ] **Step 3: Implement.**

3a. `IProjectTemplateService.cs` — add to `SolutionTypes` (and to `All`):

```csharp
    public static readonly SolutionType Cpp = new()
    {
        Id = "cpp",
        Name = "C++",
        Description = "Native C++ project compiled directly with clang++, g++, or MSVC. No transpilation, no runtime.",
        Icon = "cpp",
        ProjectExtension = ".blproj",
        SolutionExtension = ".blsln",
        SourceExtension = ".cpp"
    };

    public static IReadOnlyList<SolutionType> All => new[] { DotNet, Msil, Native, Llvm, Cpp };
```

Disambiguate the existing transpile entry (Id stays `"native"` — only the display name changes):

```csharp
        Name = "BasicLang (Native)",
        Description = "Compile BasicLang to native code via C++ transpilation. High performance, no runtime required.",
```

Add to `ProjectTemplates.All` three entries following the existing shape (`Id`, `Name`, `Description`, `Icon`, `Order`, `SupportedSolutionTypes = new List<string> { "cpp" }` — the property is `List<string>` (IProjectTemplateService.cs:216), an array literal is a CS0029): `cpp-console-app` ("C++ Console App"), `cpp-library` ("C++ Static Library"), `cpp-game-app` ("C++ Game (VGS Engine)"). Existing templates keep their `SupportedSolutionTypes` (they do not list `"cpp"`, so the dialog's template list filters correctly with zero UI changes — the Solution Type ComboBox IS the language picker).

3b. `ProjectTemplateService.cs`:
- `GenerateProjectFileContent` (:257): when `options.SolutionType.Id == "cpp"`, emit `<Language>Cpp</Language>` and `<CppStandard>c++20</CppStandard>` after `<TargetBackend>Cpp</TargetBackend>` (the existing switch maps nothing for "cpp" — add `"cpp" => "Cpp"` to the targetBackend switch), and for `cpp-game-app` also emit the `<NativeLib Include="VisualGameStudioEngine.lib" />` ItemGroup.
- `GetOutputType` (:339): `cpp-console-app`/`cpp-game-app` → "exe", `cpp-library` → "library".
- `GetCompileItems` (:355): `cpp-console-app`/`cpp-game-app` → `["main.cpp"]`; `cpp-library` → `["mathutils.cpp"]` (header listed as Content or omitted — match Task 7's CLI templates).
- `GenerateSourceFilesAsync` (:371): write the same file contents as Task 7's CLI templates (keep the two template systems content-identical; add a comment in each pointing at the other — they are known to drift). **CAUTION:** this method today writes `Main{SolutionType.SourceExtension}` with BasicLang content **unconditionally** for every template except `class-library` (:371–382) — with `SourceExtension=".cpp"` that produces a bogus BasicLang-content `Main.cpp` (persisting on disk for `cpp-library`, masked for the others only by case-insensitive overwrite). Gate the generic Main-file write to non-`"cpp"` solution types before adding the cpp branches.

- [ ] **Step 4: Run** — `--filter "FullyQualifiedName~TemplateBuildSweepTests"` → 8 old + 3 new pass/skip. `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` → clean.
- [ ] **Step 5: Commit** — `git commit -m "feat(cpp): C++ solution type + IDE templates; New Project dialog gains C++ via SolutionTypes"`

---

### Task 11: Solution Explorer + file-dialog C++ awareness

**Files:**
- Modify: `VisualGameStudio.Shell/ViewModels/Panels/SolutionExplorerViewModel.cs` (allowlist :338–348, `GetItemTypeForExtension` :1062–1071, add-file dialog filter :1200)
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (Open File dialog filter :1713)

No good headless test seam exists for these view-models (they are UI-bound); this task is small mechanical edits verified by build + the final IDE smoke (Task 14). Keep each edit minimal:

- [ ] **Step 1:** Solution-view allowlist (:338–348): add `".cpp", ".h", ".hpp", ".c", ".cc", ".cxx"` to the `sourceExtensions` set.
- [ ] **Step 2:** `GetItemTypeForExtension` (:1062): extend the Compile arm:

```csharp
            ".bas" or ".bl" or ".mod" or ".cls" or ".class"
                or ".cpp" or ".cc" or ".cxx" or ".c" or ".h" or ".hpp" => ProjectItemType.Compile,
```

(Headers as Compile items are harmless: `CppProjectBuilder` filters to TU extensions; listing headers keeps them visible in the project tree.)
- [ ] **Step 3:** `AddExistingFileAsync` dialog (:1200): add a filter entry `new("C++ Files", new[] { "*.cpp", "*.h", "*.hpp", "*.c", "*.cc", "*.cxx" })` (match the existing FilePickerFileType construction style).
- [ ] **Step 4:** Open File dialog (MainWindowViewModel.cs:1713): this dialog uses a DIFFERENT filter type than Step 3 — `FileDialogFilter(string name, params string[] extensions)` with **bare** extensions (no `*.` prefix). Add: `new("C++ Files", "cpp", "h", "hpp", "c", "cc", "cxx")`.
- [ ] **Step 5:** Build: `dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release` → 0 errors. Run the full existing suite filter for the Shell-adjacent fixtures if any exist; otherwise rely on Task 14.
- [ ] **Step 6: Commit** — `git commit -m "feat(cpp): Solution Explorer and file dialogs recognize C++ files"`

---

### Task 12: C++ syntax highlighting (themed XSHD + apply-at-open)

**Files:**
- Create: `VisualGameStudio.Editor/Highlighting/Cpp.xshd` (dark), `VisualGameStudio.Editor/Highlighting/CppLight.xshd`
- Modify: `VisualGameStudio.Editor/VisualGameStudio.Editor.csproj` (:22–26 EmbeddedResource group)
- Modify: `VisualGameStudio.Editor/Highlighting/HighlightingLoader.cs` (`RegisterHighlighting` :33–46, `UpdateForTheme` :83–94)
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (`OnInitialized` :495–503, `SetLanguageService` :394)
- Test: `VisualGameStudio.Tests/Editor/CppHighlightingTests.cs` (create)

**IMPORTANT: author the .xshd files with the Write tool only** (PS round-trips corrupt BOM-less UTF-8 in this repo).

- [ ] **Step 1: Write the failing tests**

```csharp
using AvaloniaEdit.Highlighting;
using NUnit.Framework;
using VisualGameStudio.Editor.Highlighting;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class CppHighlightingTests
{
    [Test]
    public void RegisterHighlighting_RegistersThemedCppDefinition()
    {
        HighlightingLoader.RegisterHighlighting();

        var byName = HighlightingManager.Instance.GetDefinition("C++");
        Assert.That(byName, Is.Not.Null);

        // Ours (VS Code dark palette) shadows AvaloniaEdit's light-only built-in:
        // the built-in's Comment color is Green (#FF008000); ours is #6A9955.
        var comment = byName!.GetNamedColor("Comment");
        Assert.That(comment, Is.Not.Null, "themed definition must define a Comment color");
        Assert.That(comment!.Foreground!.ToString(), Does.Contain("6A9955").IgnoreCase);
    }

    [TestCase(".cpp")]
    [TestCase(".h")]
    [TestCase(".hpp")]
    [TestCase(".cc")]
    [TestCase(".cxx")]
    public void ExtensionLookup_ResolvesCpp(string ext)
    {
        HighlightingLoader.RegisterHighlighting();
        var def = HighlightingManager.Instance.GetDefinitionByExtension(ext);
        Assert.That(def, Is.Not.Null);
        Assert.That(def!.Name, Is.EqualTo("C++"));
    }
}
```

- [ ] **Step 2: Run to verify failure** (`--filter "FullyQualifiedName~CppHighlightingTests"`): the built-in resolves but its Comment color is Green → first test fails.

- [ ] **Step 3: Author `Cpp.xshd`** (complete file — VS Code dark palette, same hexes as BasicLang.xshd):

```xml
<?xml version="1.0"?>
<SyntaxDefinition name="C++" extensions=".cpp;.h;.hpp;.c;.cc;.cxx;.hh;.hxx;.inl"
                  xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">

  <Color name="Comment" foreground="#6A9955" fontStyle="italic" />
  <Color name="String" foreground="#CE9178" />
  <Color name="Char" foreground="#CE9178" />
  <Color name="Number" foreground="#B5CEA8" />
  <Color name="Preprocessor" foreground="#C586C0" />
  <Color name="Keyword" foreground="#569CD6" fontWeight="bold" />
  <Color name="ControlKeyword" foreground="#C586C0" fontWeight="bold" />
  <Color name="TypeKeyword" foreground="#4EC9B0" />
  <Color name="Constant" foreground="#4FC1FF" />
  <Color name="Operator" foreground="#D4D4D4" />

  <RuleSet>
    <Span color="Comment" begin="//" />
    <Span color="Comment" multiline="true" begin="/\*" end="\*/" />

    <Span color="String" begin="&quot;" end="&quot;">
      <RuleSet>
        <Span begin="\\" end="." />
      </RuleSet>
    </Span>
    <Span color="Char" begin="'" end="'">
      <RuleSet>
        <Span begin="\\" end="." />
      </RuleSet>
    </Span>

    <Span color="Preprocessor" begin="^\s*\#" />

    <Keywords color="ControlKeyword">
      <Word>if</Word><Word>else</Word><Word>switch</Word><Word>case</Word><Word>default</Word>
      <Word>for</Word><Word>while</Word><Word>do</Word><Word>break</Word><Word>continue</Word>
      <Word>return</Word><Word>goto</Word><Word>try</Word><Word>catch</Word><Word>throw</Word>
      <Word>co_await</Word><Word>co_yield</Word><Word>co_return</Word>
    </Keywords>

    <Keywords color="Keyword">
      <Word>class</Word><Word>struct</Word><Word>union</Word><Word>enum</Word><Word>namespace</Word>
      <Word>template</Word><Word>typename</Word><Word>using</Word><Word>typedef</Word>
      <Word>public</Word><Word>private</Word><Word>protected</Word><Word>friend</Word>
      <Word>virtual</Word><Word>override</Word><Word>final</Word><Word>explicit</Word><Word>inline</Word>
      <Word>static</Word><Word>extern</Word><Word>const</Word><Word>constexpr</Word><Word>consteval</Word>
      <Word>constinit</Word><Word>mutable</Word><Word>volatile</Word><Word>register</Word>
      <Word>new</Word><Word>delete</Word><Word>operator</Word><Word>sizeof</Word><Word>alignof</Word>
      <Word>decltype</Word><Word>noexcept</Word><Word>static_assert</Word><Word>static_cast</Word>
      <Word>dynamic_cast</Word><Word>const_cast</Word><Word>reinterpret_cast</Word>
      <Word>concept</Word><Word>requires</Word><Word>export</Word><Word>import</Word><Word>module</Word>
    </Keywords>

    <Keywords color="TypeKeyword">
      <Word>void</Word><Word>bool</Word><Word>char</Word><Word>wchar_t</Word><Word>char8_t</Word>
      <Word>char16_t</Word><Word>char32_t</Word><Word>short</Word><Word>int</Word><Word>long</Word>
      <Word>float</Word><Word>double</Word><Word>signed</Word><Word>unsigned</Word><Word>auto</Word>
      <Word>size_t</Word><Word>this</Word>
    </Keywords>

    <Keywords color="Constant">
      <Word>true</Word><Word>false</Word><Word>nullptr</Word><Word>NULL</Word>
    </Keywords>

    <Rule color="Number">
      \b0[xX][0-9a-fA-F']+\b|\b\d[\d']*(\.[\d']+)?([eE][+-]?\d+)?[fFlLuU]*\b
    </Rule>
  </RuleSet>
</SyntaxDefinition>
```

`CppLight.xshd`: identical structure; mirror the color hexes from `BasicLangLight.xshd` (open it and copy its palette for the same color names; Preprocessor/ControlKeyword use its ControlKeyword hex, TypeKeyword uses its Type hex).

- [ ] **Step 4:** `VisualGameStudio.Editor.csproj` — add to the EmbeddedResource ItemGroup:

```xml
  <EmbeddedResource Include="Highlighting\Cpp.xshd" />
  <EmbeddedResource Include="Highlighting\CppLight.xshd" />
```

- [ ] **Step 5:** `HighlightingLoader.cs`:
- In `RegisterHighlighting()` after the BasicLang registration, load + register C++ the same way (reuse the existing private loader with the FULL manifest resource name `VisualGameStudio.Editor.Highlighting.Cpp.xshd`):

```csharp
        // CAUTION: LoadDefinitionFromResource takes the FULL manifest resource
        // name (see the BasicLang call at :288) — a bare "Cpp.xshd" returns null
        // and the if-guard would silently leave AvaloniaEdit's light-only
        // built-in active (the exact failure the new tests catch).
        var cpp = LoadDefinitionFromResource("VisualGameStudio.Editor.Highlighting.Cpp.xshd");
        if (cpp != null)
        {
            HighlightingManager.Instance.RegisterHighlighting(
                "C++",
                new[] { ".cpp", ".h", ".hpp", ".c", ".cc", ".cxx", ".hh", ".hxx", ".inl" },
                cpp);
        }
```

(Adapt the loader-call signature to the actual private helper at :291–313.)
- In `UpdateForTheme(bool isDark, bool isHighContrast)` re-register "C++" with `VisualGameStudio.Editor.Highlighting.Cpp.xshd` (dark/HC) or `VisualGameStudio.Editor.Highlighting.CppLight.xshd` (light), mirroring the BasicLang re-registration.

- [ ] **Step 6:** `CodeEditorControl.axaml.cs`:
- `OnInitialized` (:495–503): replace the unconditional BasicLang application with:

```csharp
            HighlightingLoader.RegisterHighlighting();
            if (!string.IsNullOrEmpty(_documentFilePath))
            {
                SetHighlightingForFile(_documentFilePath);
            }
            else
            {
                var highlighting = HighlightingManager.Instance.GetDefinition("BasicLang");
                if (highlighting != null) _textEditor.SyntaxHighlighting = highlighting;
            }
```

- `SetLanguageService` (:394): after `_documentFilePath` is stored, add `SetHighlightingForFile(filePath);` (the method already no-ops when `_textEditor` is null — whichever of the two hooks fires last wins, both orders end correct).

- [ ] **Step 7: Run** — `--filter "FullyQualifiedName~CppHighlightingTests"` → PASS; also run the existing Editor fixture(s) (`--filter "FullyQualifiedName~VisualGameStudio.Tests.Editor"`) → no regressions.
- [ ] **Step 8: Commit** — `git commit -m "feat(cpp): themed C++ syntax highlighting applied at document open"`

---

### Task 13: LSP gating — keep C++ files away from the BasicLang server

**Files:**
- Modify: `VisualGameStudio.Editor/Controls/CodeEditorControl.axaml.cs` (`UpdateFoldings` :1884)
- Modify: `VisualGameStudio.Shell/ViewModels/MainWindowViewModel.cs` (`GoToDefinitionAsync` :4223)

The BasicLang LSP answers unknown URIs as if they were BasicLang (documented at MainWindowViewModel.cs:2082–2086), so ungated paths produce actively wrong results for `.cpp`.

- [ ] **Step 1:** In `UpdateFoldings` add the gate to the LSP branch condition:

```csharp
                && VisualGameStudio.Core.Utilities.BasicLangFileTypes.IsBasicLangSourceFile(_documentFilePath)
```

(non-BasicLang files fall through to `ApplyFoldings(null)` — the regex strategy produces nothing for C++, which is correct-and-quiet for Phase 1).
- [ ] **Step 2:** In `GoToDefinitionAsync`, add at the top of the LSP attempt: `if (!BasicLangFileTypes.IsBasicLangSourceFile(activeDoc.FilePath)) return;` — the in-scope variable is `activeDoc` (a `CodeEditorDocumentViewModel`), not `document`; mirror the completion gate idiom at :2087.
- [ ] **Step 3:** Build Shell + Editor; run `--filter "FullyQualifiedName~BasicLangFileTypesTests"` (existing) → green.
- [ ] **Step 4: Commit** — `git commit -m "fix(cpp): gate folding and go-to-definition so C++ files never query the BasicLang LSP"`

---

### Task 14: Full verification

Apply @superpowers:verification-before-completion — run everything, then drive the real product surfaces.

- [ ] **Step 1: Full suite**

Run: `dotnet test VisualGameStudio.Tests/VisualGameStudio.Tests.csproj -c Release`
Expected: everything green (≈2,400 existing + ~30 new; toolchain-dependent tests skip only on machines without a compiler — this machine has one, so they must RUN).

- [ ] **Step 2: CLI smoke (real binary, real toolchain)**

```powershell
dotnet build BasicLang/BasicLang.csproj -c Release
BasicLang/bin/Release/net8.0/BasicLang.exe new cpp-console -n SmokeCpp -o $env:TEMP\SmokeCpp
BasicLang/bin/Release/net8.0/BasicLang.exe build $env:TEMP\SmokeCpp\SmokeCpp.blproj
& "$env:TEMP\SmokeCpp\bin\Debug\SmokeCpp.exe"        # prints "Hello from SmokeCpp!"
Get-Content $env:TEMP\SmokeCpp\obj\compile_commands.json   # valid JSON, 1 entry
BasicLang/bin/Release/net8.0/BasicLang.exe run $env:TEMP\SmokeCpp\SmokeCpp.blproj   # runs the exe
```

- [ ] **Step 3: IDE smoke (manual, through the real Shell)**

```powershell
dotnet clean VisualGameStudio.Shell/VisualGameStudio.Shell.csproj   # AXAML touched in Tasks 10-12
dotnet build VisualGameStudio.Shell/VisualGameStudio.Shell.csproj -c Release
VisualGameStudio.Shell/bin/Release/net8.0/VisualGameStudio.exe
```

Verify by hand: (1) File → New Project shows "C++" in Solution Type with the three templates (and "BasicLang (Native)" renamed); (2) create a C++ Console project — `main.cpp` opens with C++ colors on the dark theme; (3) Build → succeeds, Output shows toolchain name; (4) introduce an error → Error List shows `main.cpp(line,col)` entry, double-click navigates; (5) fix, Ctrl+F5 → program output appears; (6) add a `.h` via Solution Explorer → visible in tree; (7) open a `.bas` project → everything BasicLang still works (highlighting, completion, folding).

- [ ] **Step 4: Update memory/docs + final commit.** Update the auto-memory active-work entry for Phase 1 status. Do NOT add changelog entries to CLAUDE.md.

```bash
git add -A
git commit -m "feat(cpp): Phase 1 complete - C++ projects build & run from CLI and IDE"
```

---

## Out of scope for this plan (later phases, per spec §6)

- Mixed BasicLang+C++ projects, per-module split emission, entry-point counting (Phase 2)
- Spec §9's ".cpp files present in a C#/MSIL/LLVM-targeting project → clear error": mixing-related, deferred to Phase 2 alongside the rest of the mixing rules (Phase 1 covers only the reverse guard, BL6008)
- clangd IntelliSense, LSP registry, downloads, `compile_commands.json` on project open (Phase 3 — Phase 1 only emits it on build)
- Native debugging via lldb-dap/gdb (Phase 4)
- Incremental `.o` caching, response files for long MSVC command lines, C++ Toggle-Comment/`//` support, C-family indentation strategy, Find-in-Files/Command-Palette filter extensions, CMake import (Phase 5 backlog)
- Known Phase 1 edge: adding .cpp/.h files to a BASICLANG project via Solution Explorer creates Compile items that the BasicLang pipeline will lex and fail on loudly — mixing guards are Phase 2 (BL6008 covers only the reverse direction)
