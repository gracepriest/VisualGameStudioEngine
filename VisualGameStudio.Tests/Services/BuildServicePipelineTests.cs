using System.Collections.Concurrent;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// End-to-end pins for the IDE's IN-PROCESS build pipeline (BuildService).
///
/// BuildService historically re-implemented the compiler (own lexer/parser/
/// analyzer/IR merge) and drifted from what `BasicLang.exe build` does. These
/// tests build REAL template projects through BuildService and require the
/// same outcomes the CLI produces:
///
///   1. game-app (dotnet)     — .mod/.cls implicit wrapping must be applied
///                              (drift symptom: BL2001 parse errors in the IDE).
///   2. console-app (dotnet)  — cross-module calls (Main.bas → Helpers.mod)
///                              must link into one program (drift: CS0103).
///   3. console-app (Cpp)     — TargetBackend=Cpp must produce a .cpp, not a .cs
///                              (drift: "Backend: Cpp" printed, .cs generated).
///   4. semantic error        — diagnostics must carry file path + line so the
///                              Error List can navigate.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BuildServicePipelineTests
{
    private string _rootDir = null!;
    private ProjectTemplateService _templates = null!;

    [SetUp]
    public void SetUp()
    {
        _templates = new ProjectTemplateService();
        _rootDir = Path.Combine(Path.GetTempPath(), "bl-ide-build-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_rootDir);
    }

    [TearDown]
    public void TearDown()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (Directory.Exists(_rootDir))
                    Directory.Delete(_rootDir, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(200);
            }
        }
    }

    // ------------------------------------------------------------------
    // 1. game-app template (dotnet): .mod/.cls preprocessing parity
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_GameAppTemplate_DotNet_Succeeds()
    {
        var project = await CreateTemplateProjectAsync("game-app", SolutionTypes.DotNet, "PipelineGame");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True,
            "game-app template failed to build in-process (CLI builds it fine — " +
            ".mod/.cls implicit Module/Class wrapping was not applied?).\n" + Describe(result, output));

        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "game-app build succeeded but reported no executable.\n" + Describe(result, output));
        Assert.That(File.Exists(result.ExecutablePath), Is.True,
            $"Reported executable does not exist: {result.ExecutablePath}");

        // The stock template uses the game engine, so the managed wrapper must be
        // deployed next to the built game (reference is hint-pathed; msbuild copies it).
        var outputDir = Path.GetDirectoryName(result.ExecutablePath!)!;
        Assert.That(File.Exists(Path.Combine(outputDir, "RaylibWrapper.dll")), Is.True,
            "RaylibWrapper.dll was not deployed next to the built game.\n" + Describe(result, output));
    }

    // ------------------------------------------------------------------
    // 2. console-app template (dotnet): cross-module linkage parity
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_ConsoleAppTemplate_DotNet_SucceedsAndProducesAssembly()
    {
        var project = await CreateTemplateProjectAsync("console-app", SolutionTypes.DotNet, "PipelineConsole");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True,
            "console-app template failed to build in-process (Main.bas calls " +
            "PrintHeader/FormatMessage from Helpers.mod — cross-module linkage lost?).\n" +
            Describe(result, output));

        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "console-app build succeeded but reported no executable.\n" + Describe(result, output));
        Assert.That(File.Exists(result.ExecutablePath), Is.True,
            $"Reported executable does not exist: {result.ExecutablePath}");
    }

    // ------------------------------------------------------------------
    // 3. console-app template (native/Cpp): backend honesty
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_ConsoleAppTemplate_Cpp_ProducesCppSource_NotCs()
    {
        var project = await CreateTemplateProjectAsync("console-app", SolutionTypes.Native, "PipelineNative");
        Assert.That(project.TargetBackend, Is.EqualTo(TargetBackend.Cpp),
            "native solution template did not set TargetBackend=Cpp — test setup is wrong");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True,
            "console-app (Cpp backend) failed to build in-process.\n" + Describe(result, output));

        Assert.That(result.GeneratedFileName, Does.EndWith(".cpp"),
            $"Cpp backend reported generated file '{result.GeneratedFileName}' — expected a .cpp.\n" +
            Describe(result, output));

        Assert.That(result.OutputPath, Is.Not.Null.And.Not.Empty, "no output path reported");
        var cppFiles = Directory.GetFiles(result.OutputPath!, "*.cpp");
        Assert.That(cppFiles, Is.Not.Empty,
            "Cpp backend claimed success but wrote no .cpp file.\n" + Describe(result, output));

        var csFiles = Directory.GetFiles(result.OutputPath!, "*.cs");
        Assert.That(csFiles, Is.Empty,
            "Cpp backend generated C# output (.cs) — 'Backend: Cpp' is lying:\n" +
            string.Join("\n", csFiles) + "\n" + Describe(result, output));

        // A C++ build produces source for an external toolchain — no .NET exe.
        Assert.That(result.ExecutablePath, Is.Null.Or.Empty,
            "Cpp backend claimed a .NET executable was produced.");
    }

    // ------------------------------------------------------------------
    // 3b. CLI-parity paths that were missing from the IDE pipeline:
    //     NuGet restore (avalonia has PackageReferences) and the
    //     UseWindowsForms → net*-windows TFM flow (winforms).
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_AvaloniaTemplate_DotNet_RestoresPackagesAndBuilds()
    {
        var project = await CreateTemplateProjectAsync("avalonia-app", SolutionTypes.DotNet, "PipelineAvalonia");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True,
            "avalonia-app template failed to build in-process (PackageReference restore " +
            "or csproj package flow broken?).\n" + Describe(result, output));
        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "avalonia-app build succeeded but reported no executable.\n" + Describe(result, output));
    }

    [Test]
    public async Task Build_WinFormsTemplate_DotNet_Builds()
    {
        var project = await CreateTemplateProjectAsync("winforms-app", SolutionTypes.DotNet, "PipelineWinForms");

        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True,
            "winforms-app template failed to build in-process (UseWindowsForms / " +
            "net*-windows TFM not flowed into the generated csproj?).\n" + Describe(result, output));
        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "winforms-app build succeeded but reported no executable.\n" + Describe(result, output));
    }

    // ------------------------------------------------------------------
    // 4. semantic error: Error List navigation (file + line + column)
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_SemanticError_DiagnosticCarriesFilePathAndLine()
    {
        // Hand-rolled two-file project with a known-bad call on a known line.
        var projectDir = Path.Combine(_rootDir, "BrokenApp");
        Directory.CreateDirectory(projectDir);

        const string mainSource =
@"Sub Main()
    Dim x As Integer = missingFunction(1)
End Sub
";
        const int expectedErrorLine = 2;

        const string helperSource =
@"Public Function Twice(a As Integer) As Integer
    Return a * 2
End Function
";

        const string projectXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>BrokenApp</ProjectName>
    <OutputType>Exe</OutputType>
    <RootNamespace>BrokenApp</RootNamespace>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
    <Compile Include=""Helper.bas"" />
  </ItemGroup>
</BasicLangProject>
";

        var projectFile = Path.Combine(projectDir, "BrokenApp.blproj");
        File.WriteAllText(projectFile, projectXml);
        File.WriteAllText(Path.Combine(projectDir, "Main.bas"), mainSource);
        File.WriteAllText(Path.Combine(projectDir, "Helper.bas"), helperSource);

        var project = await new ProjectSerializer().LoadAsync(projectFile);
        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.False,
            "Build of a semantically-broken project succeeded.\n" + Describe(result, output));
        Assert.That(result.ErrorCount, Is.GreaterThan(0),
            "Failed build reported no error diagnostics.\n" + Describe(result, output));

        var error = result.Errors.FirstOrDefault(e =>
            e.FilePath != null &&
            e.FilePath.EndsWith("Main.bas", StringComparison.OrdinalIgnoreCase));
        Assert.That(error, Is.Not.Null,
            "No error diagnostic carried the offending source file path — the Error List " +
            "cannot navigate.\n" + Describe(result, output));

        Assert.That(error!.Line, Is.EqualTo(expectedErrorLine),
            $"Diagnostic line mismatch: {error.FilePath}({error.Line},{error.Column}): {error.Message}");
        Assert.That(error.Column, Is.GreaterThan(0),
            $"Diagnostic column missing: {error.FilePath}({error.Line},{error.Column}): {error.Message}");
    }

    // ------------------------------------------------------------------
    // 5. hostile project names: MSBuild/XML special characters
    // ------------------------------------------------------------------

    [Test]
    public async Task Build_ProjectNameWithMsBuildSpecialChars_Builds()
    {
        // Regression: a project named ";k;lk;lkl;k;l" failed with MSB4094 —
        // the generated csproj embedded the raw name into <AssemblyName> and
        // <Compile Include>, and MSBuild split the derived obj\Debug\<name>.dll
        // path on the ';' list separator (multiple items into Csc's single-item
        // OutputAssembly). '&' additionally breaks the XML layer, '%' and the
        // apostrophe are MSBuild escape/quote characters. All are legal Windows
        // file-name characters, so the build layer must escape them.
        const string name = "A;B & C's 100%";

        var projectDir = Path.Combine(_rootDir, name);
        Directory.CreateDirectory(projectDir);

        const string mainSource =
@"Sub Main()
    PrintLine(""hostile name ok"")
End Sub
";
        // NOTE: '&' must be XML-escaped inside the .blproj itself.
        var projectXml =
@"<?xml version=""1.0"" encoding=""utf-8""?>
<BasicLangProject Version=""1.0"">
  <PropertyGroup>
    <ProjectName>" + System.Security.SecurityElement.Escape(name) + @"</ProjectName>
    <OutputType>Exe</OutputType>
    <TargetBackend>CSharp</TargetBackend>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""Main.bas"" />
  </ItemGroup>
</BasicLangProject>
";

        var projectFile = Path.Combine(projectDir, name + ".blproj");
        File.WriteAllText(projectFile, projectXml);
        File.WriteAllText(Path.Combine(projectDir, "Main.bas"), mainSource);

        var project = await new ProjectSerializer().LoadAsync(projectFile);
        var (result, output) = await BuildAsync(project);

        Assert.That(result.Success, Is.True,
            "project with MSBuild/XML-special characters in its name failed to build " +
            "(raw name leaked into the generated csproj?).\n" + Describe(result, output));
        Assert.That(result.ExecutablePath, Is.Not.Null.And.Not.Empty,
            "build succeeded but reported no executable.\n" + Describe(result, output));
        Assert.That(File.Exists(result.ExecutablePath), Is.True,
            $"Reported executable does not exist: {result.ExecutablePath}");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<BasicLangProject> CreateTemplateProjectAsync(
        string templateId, SolutionType solutionType, string name)
    {
        var template = ProjectTemplates.All.Single(t => t.Id == templateId);
        var options = new CreateProjectOptions
        {
            Name = name,
            Location = _rootDir,
            Template = template,
            SolutionType = solutionType,
            CreateSolutionFolder = true,
            CreateGitRepository = false
        };

        var creation = await _templates.CreateProjectAsync(options);
        Assert.That(creation.Success, Is.True, $"project creation failed: {creation.Error}");
        Assert.That(creation.ProjectPath, Is.Not.Null.And.Not.Empty);
        Assert.That(File.Exists(creation.ProjectPath), Is.True, "project file missing on disk");

        return await new ProjectSerializer().LoadAsync(creation.ProjectPath!);
    }

    private static async Task<(BuildResult Result, RecordingOutput Output)> BuildAsync(BasicLangProject project)
    {
        var output = new RecordingOutput();
        var buildService = new BuildService(output);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await buildService.BuildProjectAsync(project, cts.Token);
        return (result, output);
    }

    private static string Describe(BuildResult result, RecordingOutput output)
    {
        return "Diagnostics:\n" +
               string.Join("\n", result.Diagnostics.Select(d =>
                   $"  {d.FilePath}({d.Line},{d.Column}): {d.Severity} {d.Id}: {d.Message}")) +
               "\n\nBuild output:\n" + output.Dump();
    }

    /// <summary>Thread-safe recording IOutputService so failures show real build output.</summary>
    private sealed class RecordingOutput : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue("ERROR: " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => _lines.ToArray();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }
}
