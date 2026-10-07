using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using BasicLang.Compiler.ProjectSystem;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Panels;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task #223 — a BasicLang diagnostic prints its code ONCE on every build route that prints a
/// code field: <c>path(line,col): error BC30526: Property 'P' is 'ReadOnly'.</c>
///
/// <para>The front end puts a VB-coded error's code at the head of its message on purpose
/// (<c>SemanticAnalyzer.VbCodedError</c>: the CLI's <c>{label}: {Message}</c> print sites carry no
/// code field). The native project builder copied that message into a <see cref="CppDiagnostic"/>
/// whose <see cref="CppDiagnostic.Code"/> <see cref="CppDiagnosticsParser.FormatNormalized"/> also
/// prints, so the CLI and the IDE printed <c>error BC30526: BC30526: …</c>; the IDE's own C#-route
/// line (<c>BuildService.AddCompilerDiagnostic</c>) did the same. Both now take the code off the
/// head where the error is converted (<see cref="CppDiagnosticsParser.MessageWithoutCode"/>), and
/// a toolchain line, which never goes through that conversion, is printed exactly as before.</para>
///
/// <para>The CLI leg (the real <c>BasicLang build X.blproj</c> on a C++ target) is
/// <c>PropertyAccessExecutionTests.CppReleaseBuild_PrintsTheDiagnosticCodeOnce_Task223</c>
/// (Integration). Every case here is fast: a BasicLang native build reports a front-end error
/// before it asks for any toolchain, and the routes below are given a toolchain resolver that finds
/// nothing.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class DiagnosticCodeOnceTests
{
    /// <summary>A VB-coded error (BC30526, line 27) and a BL-coded one (BL4004, line 22), both
    /// with the code at the head of the analyzer's message.</summary>
    private const string Program = """
        Class C
            Public ReadOnly Property P As Integer
                Get
                    Return 1
                End Get
            End Property
            Public Property Q As Integer
                Get
                    Return 2
                End Get
                Set(value As Integer)
                End Set
            End Property
        End Class
        Class B
            Public Sub New(ByRef x As Integer)
            End Sub
        End Class
        Class D
            Inherits B
            Public Sub New(c As C)
                MyBase.New(c.Q)
            End Sub
        End Class
        Sub Main()
            Dim o As New C()
            o.P = 99
        End Sub
        """;

    private const string ReadOnlyTail = "Main.bas(27,5): error BC30526: Property 'P' is 'ReadOnly'.";

    private string _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-t223-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private string WriteProject(string backend)
    {
        File.WriteAllText(Path.Combine(_dir, "Main.bas"), Program + "\n");
        var blproj = Path.Combine(_dir, "App.blproj");
        File.WriteAllText(blproj, $"""
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>{backend}</TargetBackend>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="Main.bas" />
              </ItemGroup>
            </BasicLangProject>
            """);
        return blproj;
    }

    /// <summary><c>CppProjectBuilder.Build</c> — what the CLI's <c>build</c> and the IDE's native
    /// route both call — with no toolchain: the front end refuses the program first.</summary>
    private List<CppDiagnostic> BuildNative()
    {
        var result = CppProjectBuilder.Build(ProjectFile.Load(WriteProject("Cpp")), "Debug",
            resolveById: _ => null, probeAvailability: () => default, resolveToolchain: () => null);
        Assert.That(result.Success, Is.False, "the front end must refuse this program");
        return result.Diagnostics;
    }

    private static int Occurrences(string text, string code) => Regex.Matches(text, Regex.Escape(code)).Count;

    // =====================================================================================
    // The conversion: a BasicLang diagnostic, rendered the way the CLI and the IDE render it.
    // =====================================================================================

    [Test]
    public void ANativeProjectBuild_PrintsABasicLangCodeOnce_Task223()
    {
        var diagnostics = BuildNative();
        var all = string.Join("\n", diagnostics.Select(CppDiagnosticsParser.FormatNormalized));

        var readOnly = diagnostics.SingleOrDefault(d => d.Code == "BC30526");
        var byRef = diagnostics.SingleOrDefault(d => d.Code == "BL4004");
        Assert.That(readOnly, Is.Not.Null, all);
        Assert.That(byRef, Is.Not.Null, all);
        Assert.Multiple(() =>
        {
            Assert.That(CppDiagnosticsParser.FormatNormalized(readOnly!), Does.EndWith(ReadOnlyTail),
                "the MSBuild shape with the code ONCE (was 'error BC30526: BC30526: …')");
            Assert.That(readOnly!.Message, Is.EqualTo("Property 'P' is 'ReadOnly'."),
                "the code lives in Code; the IDE's error list shows Code and Message side by side");
            Assert.That(CppDiagnosticsParser.FormatNormalized(byRef!),
                Does.EndWith("Main.bas(22,20): error BL4004: Property 'Q' cannot be passed ByRef inside a 'MyBase.New' call. "
                    + "VB writes a ByRef property back through its setter after the call, and that write-back cannot run "
                    + "inside a base-constructor call. Copy the value to a local first (in the caller, passing it in as a "
                    + "parameter) and pass the local."),
                "a BL-coded message is split the same way");
            Assert.That(Occurrences(all, "BC30526"), Is.EqualTo(1), all);
            Assert.That(Occurrences(all, "BL4004"), Is.EqualTo(1), all);
        });
    }

    // =====================================================================================
    // A toolchain line never goes through that conversion, so it prints as it always did.
    // =====================================================================================

    [Test]
    public void AToolchainDiagnostic_IsPrintedExactlyAsBefore_Task223()
    {
        // Rooted, forward-slash paths: IsPathRooted on every OS, so Absolutize keeps them as written.
        var output = "/proj/main.cpp(5,3): error C2065: 'x': undeclared identifier\n"
                   + "/proj/util.cpp(7): warning C4189: 'y': local variable is initialized but not referenced\n"
                   + "/proj/main.cpp:12:5: error: use of undeclared identifier 'foo'\n"
                   + "/proj/main.obj : error LNK2019: unresolved external symbol Add referenced in function main\n";

        var printed = CppDiagnosticsParser.Parse(output, "/proj").Select(CppDiagnosticsParser.FormatNormalized);

        Assert.That(printed, Is.EqualTo(new[]
        {
            "/proj/main.cpp(5,3): error C2065: 'x': undeclared identifier",
            "/proj/util.cpp(7): warning C4189: 'y': local variable is initialized but not referenced",
            "/proj/main.cpp(12,5): error CPP1001: use of undeclared identifier 'foo'",
            "/proj/main.obj: error LNK2019: unresolved external symbol Add referenced in function main",
        }));
    }

    // =====================================================================================
    // The IDE: its Output panel makes the produced line clickable, and both of its build routes
    // (native through CppProjectBuilder, C# through its own AddCompilerDiagnostic) print it once.
    // =====================================================================================

    [AvaloniaTest]
    public void TheOutputPanel_StillNavigatesFromTheProducedLine_Task223()
    {
        var readOnly = BuildNative().Single(d => d.Code == "BC30526");
        // Exactly what BuildService echoes into the Build pane for a native build.
        var line = "  " + CppDiagnosticsParser.FormatNormalized(readOnly);

        var panel = new OutputPanelViewModel(new Mock<IOutputService>().Object, new Mock<IDebugService>().Object);
        panel.AppendOutput(line + "\n");
        Dispatcher.UIThread.RunJobs();

        Assert.That(panel.OutputLines, Has.Count.EqualTo(1), line);
        var parsed = panel.OutputLines[0];
        Assert.Multiple(() =>
        {
            Assert.That(parsed.IsClickable, Is.True, "the click-to-navigate regex must match: " + line);
            Assert.That(parsed.FilePath, Is.EqualTo(readOnly.FilePath));
            Assert.That(parsed.Line, Is.EqualTo(27));
            Assert.That(parsed.Column, Is.EqualTo(5));
            Assert.That(parsed.Severity, Is.EqualTo(OutputLineSeverity.Error));
        });
    }

    [TestCase("Cpp")]
    [TestCase("CSharp")]
    public async Task TheIdeBuild_PrintsTheCodeOnce_InTheBuildPaneAndTheErrorList_Task223(string backend)
    {
        var blproj = WriteProject(backend);
        var lines = new ConcurrentQueue<string>();
        var output = new Mock<IOutputService>();
        output.Setup(o => o.WriteLine(It.IsAny<string>(), It.IsAny<OutputCategory>()))
            .Callback<string, OutputCategory>((message, _) => lines.Enqueue(message));
        output.Setup(o => o.WriteError(It.IsAny<string>(), It.IsAny<OutputCategory>()))
            .Callback<string, OutputCategory>((message, _) => lines.Enqueue(message));
        var service = new BuildService(output.Object, new ProjectSerializer(),
            new CppToolchainOverrides(null), pathResolve: _ => null);
        var project = await new ProjectSerializer().LoadAsync(blproj);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var result = await service.BuildProjectAsync(project, cts.Token);

        var pane = string.Join("\n", lines);
        Assert.That(result.Success, Is.False, pane);
        var item = result.Diagnostics.SingleOrDefault(d => d.Id == "BC30526");
        Assert.That(item, Is.Not.Null, pane);
        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Some.EndWith(ReadOnlyTail),
                "the MSBuild shape with the code ONCE (was 'error BC30526: BC30526: …')\n" + pane);
            Assert.That(Occurrences(pane, "BC30526"), Is.EqualTo(1), pane);
            Assert.That(item!.Message, Is.EqualTo("Property 'P' is 'ReadOnly'."),
                "the error list shows Id beside Message — the code belongs in Id only");
        });
    }
}
