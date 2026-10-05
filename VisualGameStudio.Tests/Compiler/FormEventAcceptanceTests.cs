using System.Collections.Concurrent;
using System.Text.Json;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Serialization;
using VisualGameStudio.ProjectSystem.Services;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Property-grid slice 5 Task 8 — the Events tab's gestures, end to end, RUN on both targets through both build entry
/// points. A form made through the real document view model; every handler asked for through the grid's Events rows (the
/// <c>HandlerRequested</c> route the view raises — never a constructed plan): the Form's Load (which writes "loaded" into
/// the Label), the Button's Click and MouseDown, the TextBox's KeyPress. One line of user code per stub, then
/// <c>SaveAsync</c> writes the document and regenerates the regions. Then each target is built by the real CLI AND by
/// <c>BuildService.BuildProjectAsync</c> (the IDE's own route — it does not delegate to the CLI process), and RUN.
///
/// <para>The shared assertion is the ORDER and COUNT of the handlers and the Label's text after Load — never the KeyPress
/// character: <c>e</c> is a <c>KeyPressEventArgs</c> on WinForms and a <c>DomEvent</c> on the page, and <c>KeyChar</c>
/// mapping is piece 2's (ruling 1).</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormEventAcceptanceTests
{
    private const string FormName = "EvForm";
    private static readonly string[] Handlers = { "EvForm_Load", "Button1_Click", "Button1_MouseDown", "TextBox1_KeyPress" };

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-evaccept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Environment.GetEnvironmentVariable("BL_KEEP_ACCEPTANCE") == "1")
        {
            TestContext.Out.WriteLine($"[kept] {_dir}");
            return;
        }

        try { Directory.Delete(_dir, true); } catch { }
    }

    private static void Log(string what) => TestContext.Out.WriteLine(what);

    // ==================================================================
    // The designer half
    // ==================================================================

    /// <summary>
    /// Designs the form in <c>design/</c>: three controls placed, the four handlers asked for through the Events rows, one
    /// line per stub, saved. Returns the design directory.
    /// </summary>
    private async Task<string> DesignAsync(FormTarget target)
    {
        var design = Path.Combine(_dir, "design");
        Directory.CreateDirectory(design);
        var scaffold = target == FormTarget.Web
            ? FormScaffolder.Create(FormName, target, FormLayoutKind.Grid)
            : FormScaffolder.Create(FormName, target);
        File.WriteAllText(Path.Combine(design, scaffold.DocumentFileName), scaffold.DocumentText);
        File.WriteAllText(Path.Combine(design, scaffold.CodeFileName), scaffold.CodeText);

        var vm = new CodeEditorDocumentViewModel(new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path.Combine(design, scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer opens");

        foreach (var (kind, x, y) in new[] { ("Label", 24, 24), ("Button", 120, 72), ("TextBox", 120, 24) })
        {
            Assert.That(vm.PlaceControl(kind, x, y), Is.Null, $"placing a {kind}");
        }

        var controls = vm.DesignDocument!.Controls.ToDictionary(c => c.Id);
        Assert.That(controls.Keys, Is.EquivalentTo(new[] { "Label1", "Button1", "TextBox1" }), "precondition: the ids the code uses");

        vm.PropertyGrid.IsEventsMode = true;
        await RequestThroughTheGridAsync(vm, null, "Load");
        await RequestThroughTheGridAsync(vm, controls["Button1"], "Click");
        await RequestThroughTheGridAsync(vm, controls["Button1"], "MouseDown");
        await RequestThroughTheGridAsync(vm, controls["TextBox1"], "KeyPress");
        Log($"[1] {target}: four handlers through the Events rows");

        // One line of user code per stub, as the user types it; Load also writes the Label (null if Load ran before the
        // controls were fetched — on the page that is a TypeError).
        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        foreach (var handler in Handlers)
        {
            var signature = code.Split('\n').Single(l => l.Contains($"Sub {handler}("));
            var lines = handler == "EvForm_Load"
                ? (target == FormTarget.Web ? "        Label1.textContent = \"loaded\"\n" : "        Label1.Text = \"loaded\"\n")
                : "";
            code = code.Replace(signature, signature + "\n" + lines + $"        Console.WriteLine(\"{handler}\")");
        }

        File.WriteAllText(codePath, code);
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[2] saved: document written, regions regenerated");
        return design;
    }

    /// <summary>
    /// Selects the owner through the ONE selection store and asks the Events row for its handler — the double-click the
    /// view turns into <c>RequestHandler</c> — then waits for the host to bind it.
    /// </summary>
    private static async Task RequestThroughTheGridAsync(CodeEditorDocumentViewModel vm, FormControl? control, string eventName)
    {
        vm.Selection.Set(control);
        var row = vm.PropertyGrid.EventRows.Single(r => r.Name == eventName);
        row.RequestHandler();
        for (var i = 0; i < 500 && row.Handler.Length == 0; i++)   // up to 10 s: a loaded machine is slow, never wrong
        {
            await Task.Delay(20);
        }

        Assert.That(row.Handler, Is.Not.Empty, $"{control?.Id ?? "form"}.{eventName}: the host bound nothing");
    }

    /// <summary>Copies the designed pair into a route directory with a Main and the route's project file.</summary>
    private string Route(string design, string name, string projectXml)
    {
        var route = Path.Combine(_dir, name);
        Directory.CreateDirectory(route);
        foreach (var file in Directory.GetFiles(design))
        {
            File.Copy(file, Path.Combine(route, Path.GetFileName(file)));
        }

        File.WriteAllText(Path.Combine(route, "Main.bas"), "Sub Main()\nEnd Sub\n");
        File.WriteAllText(Path.Combine(route, "App.blproj"), projectXml);
        return route;
    }

    /// <summary>
    /// Builds with the real CLI and returns its output directory — FOUND (the directory under <c>bin</c> holding the built
    /// <paramref name="artifact"/>), not assumed, so a change of configuration or framework does not break the walkthrough.
    /// </summary>
    private static string CliBuild(string route, string artifact)
    {
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(route, "App.blproj") }, route, timeoutMs: 300_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");
        var built = Directory.GetFiles(Path.Combine(route, "bin"), artifact, SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(built, Is.Not.Null, $"the CLI built no {artifact} under bin/:\n{stdout}");
        return Path.GetDirectoryName(built!)!;
    }

    /// <summary>Skips the test when node cannot run — asked BEFORE any build, so a missing node costs nothing.</summary>
    private static void RequireNode()
    {
        try
        {
            var (exit, _, _) = CliTestHarness.RunProcess("node", new[] { "--version" }, Path.GetTempPath(), timeoutMs: 30_000);
            if (exit != 0)
            {
                Assert.Ignore("node is not runnable here — the web run tier needs it");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("node is not on PATH — the web run tier needs it");
        }
    }

    private static async Task<string> IdeBuild(string route)
    {
        var project = await new ProjectSerializer().LoadAsync(Path.Combine(route, "App.blproj"));
        var result = await new BuildService(new SilentOutput()).BuildProjectAsync(project);
        Assert.That(result.Success, Is.True, "the IDE build failed:\n" + string.Join("\n", result.Diagnostics.Select(d => d.Id + " " + d.Message)));
        return result.OutputPath!;
    }

    /// <summary>The shared assertion: Load once BEFORE the marker, then each handler in order (KeyPress twice), the Label loaded.</summary>
    private static void AssertTheRun(IReadOnlyList<string> log, string marker, string route)
    {
        var before = log.TakeWhile(l => l != marker).ToList();
        var after = log.SkipWhile(l => l != marker).Skip(1).ToList();
        var all = string.Join("\n", log);

        Assert.Multiple(() =>
        {
            Assert.That(log, Does.Contain(marker), $"{route}: the run never reached '{marker}':\n{all}");
            Assert.That(before.Count(l => l == "EvForm_Load"), Is.EqualTo(1), $"{route}: Load once, first:\n{all}");
            Assert.That(after.Where(l => Handlers.Contains(l)),
                Is.EqualTo(new[] { "Button1_Click", "Button1_MouseDown", "TextBox1_KeyPress", "TextBox1_KeyPress" }),
                $"{route}: each handler once, in order; KeyPress for 'x' and Enter, never Shift:\n{all}");
            Assert.That(after, Does.Contain("LBL loaded"), $"{route}: Load wrote the Label:\n{all}");
            Assert.That(log.Where(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase)), Is.Empty, $"{route}:\n{all}");
        });
    }

    // ==================================================================
    // The web
    // ==================================================================

    private const string WebCliProject = """
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>App</ProjectName>
            <OutputType>Exe</OutputType>
            <TargetBackend>JavaScript</TargetBackend>
            <StartupForm>EvForm</StartupForm>
          </PropertyGroup>
        </BasicLangProject>
        """;

    private const string WebIdeProject = """
        <Project>
          <PropertyGroup>
            <ProjectName>App</ProjectName>
            <TargetBackend>JavaScript</TargetBackend>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Main.bas" />
            <Compile Include="EvForm.bas" />
            <Compile Include="EvForm.blwebform" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// The page, built by the real CLI and by the IDE's BuildService, RUN under node: Load first (it writes the Label —
    /// a TypeError if it ran before the controls were fetched), then Click, MouseDown, and the KeyPress driven as
    /// <c>keydown</c> events carrying <c>key</c> (<c>x</c> and <c>Enter</c> raise it, <c>Shift</c> does not) — never a
    /// <c>keypress</c> event. Edge too, where present, on the CLI build.
    /// </summary>
    [Test]
    public async Task Web_TheEventsTabsGestures_BuildThroughBothRoutes_AndRun()
    {
        RequireNode();
        var design = await DesignAsync(FormTarget.Web);
        var form = FormDocumentReader.Read(Path.Combine(design, "EvForm.blwebform"),
            File.ReadAllText(Path.Combine(design, "EvForm.blwebform"))).Model;

        const string dispatch = """
            console.log("--- dispatch");
            dispatch(get("Button1"), "click");
            dispatch(get("Button1"), "mousedown");
            key("TextBox1", "x");
            key("TextBox1", "Enter");
            key("TextBox1", "Shift");
            console.log("LBL " + get("Label1").textContent);
            """;

        var cliOut = CliBuild(Route(design, "cli", WebCliProject), "EvForm.html");
        AssertTheRun(RunNode(form, cliOut, dispatch), "--- dispatch", "web/CLI");
        Log("[3] web/CLI ran under node");

        var ideOut = await IdeBuild(Route(design, "ide", WebIdeProject));
        AssertTheRun(RunNode(form, ideOut, dispatch), "--- dispatch", "web/IDE");
        Log("[4] web/IDE ran under node");

        var edge = VisualGameStudio.Tests.Compiler.PixelLayout.EdgeLayoutHarness.EdgePath();
        if (edge != null)
        {
            FormEventDef E(FormControlDef d, string name) => d.Events!.Single(e => e.Name == name);
            var button = FormControlCatalog.Find("Button")!;
            var keyPress = E(FormControlCatalog.Find("TextBox")!, "KeyPress");
            string Send(string id, FormEventDef evt, string? key = null) =>
                $"el('{id}').dispatchEvent(new {FormEvents.DomInterfaceOf(evt.WebEvent!)}(" +
                $"{JsonSerializer.Serialize(FormEvents.ListenType(evt))}, {{ bubbles: true{(key == null ? "" : ", key: " + JsonSerializer.Serialize(key))} }}));\n";
            var driver = "await turn(); await turn();\nmark('--- dispatch');\n" +
                         Send("Button1", E(button, "Click")) + Send("Button1", E(button, "MouseDown")) +
                         Send("TextBox1", keyPress, "x") + Send("TextBox1", keyPress, "Enter") + Send("TextBox1", keyPress, "Shift") +
                         "mark('LBL ' + el('Label1').textContent);\n";
            AssertTheRun(FormEventWebRunTests.RunInEdge(edge, cliOut, FormName, driver), "--- dispatch", "web/CLI in Edge");
            Log("[5] web/CLI ran in Edge");
        }
        else
        {
            Log("[5] Edge not present: the node tier only");
        }
    }

    private static List<string> RunNode(FormDocument form, string output, string dispatch)
    {
        // The PAGE too, not only the script: a route that builds the script but emits no page ships nothing a browser opens.
        Assert.That(File.Exists(Path.Combine(output, form.Name + ".html")), Is.True,
            $"no {form.Name}.html in {output}:\n" + string.Join("\n", Directory.GetFiles(output)));

        var script = File.Exists(Path.Combine(output, "App.js"))
            ? Path.Combine(output, "App.js")
            : Directory.GetFiles(output, "*.js").First(f => !Path.GetFileName(f).StartsWith("harness", StringComparison.Ordinal));
        File.Copy(script, Path.Combine(output, "app.mjs"), overwrite: true);
        File.WriteAllText(Path.Combine(output, "harness.mjs"), FormEventWebRunTests.Harness(form) + dispatch);

        try
        {
            var (exit, stdout, stderr) = CliTestHarness.RunProcess("node", new[] { "harness.mjs" }, output, timeoutMs: 30_000);
            Assert.That(exit, Is.Zero, $"node failed:\n{stdout}\n{stderr}");
            return stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("node is not on PATH — the web run tier needs it");
            throw;
        }
    }

    // ==================================================================
    // WinForms
    // ==================================================================

    private const string WinCliProject = """
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>App</ProjectName>
            <OutputType>Exe</OutputType>
            <TargetBackend>CSharp</TargetBackend>
            <UseWindowsForms>true</UseWindowsForms>
          </PropertyGroup>
        </BasicLangProject>
        """;

    private const string WinIdeProject = """
        <Project>
          <PropertyGroup>
            <ProjectName>App</ProjectName>
            <TargetBackend>CSharp</TargetBackend>
            <UseWindowsForms>true</UseWindowsForms>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Main.bas" />
            <Compile Include="EvForm.bas" />
            <Compile Include="EvForm.blform" />
          </ItemGroup>
        </Project>
        """;

    /// <summary>
    /// The window, built by the real CLI and by the IDE's BuildService, RUN: a driver loads the built App.dll, SHOWS the
    /// form (⚠ PerformClick-style raising needs it visible — CLAUDE.md), raises Click, MouseDown and KeyPress ('x', Enter)
    /// through the protected <c>On…</c> methods, and prints the Label. Windows only.
    /// </summary>
    [Test]
    [Platform(Include = "Win")]
    public async Task WinForms_TheEventsTabsGestures_BuildThroughBothRoutes_AndRun()
    {
        if (!CliTestHarness.DotnetOnPath())
        {
            Assert.Ignore("the dotnet SDK is not on PATH");
        }

        var design = await DesignAsync(FormTarget.WinForms);
        var driver = BuildDriver();

        var cliOut = CliBuild(Route(design, "cli", WinCliProject), "App.dll");
        AssertTheRun(RunDriver(driver, cliOut), "--- shown", "WinForms/CLI");
        Log("[3] WinForms/CLI ran");

        var ideOut = await IdeBuild(Route(design, "ide", WinIdeProject));
        AssertTheRun(RunDriver(driver, ideOut), "--- shown", "WinForms/IDE");
        Log("[4] WinForms/IDE ran");
    }

    /// <summary>The hand-written driver — the only part not the designer's or the compiler's. Builds once, returns its exe.</summary>
    private string BuildDriver()
    {
        var dir = Path.Combine(_dir, "driver");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "driver.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>EvDriver</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "Driver.cs"), """
            using System;
            using System.Linq;
            using System.Reflection;
            using System.Windows.Forms;

            internal static class Driver
            {
                [STAThread]
                private static int Main(string[] args)
                {
                    var assembly = Assembly.LoadFrom(args[0]);
                    var type = assembly.GetTypes().First(t => t.Name == "EvForm");
                    var form = (Form)Activator.CreateInstance(type);

                    // ⛔ SHOWN: Load is raised here, and raising input on a never-shown form proves nothing.
                    form.Show();
                    Application.DoEvents();
                    Console.WriteLine("--- shown");

                    Control Field(string name) =>
                        (Control)type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(form)
                        ?? throw new InvalidOperationException("no field " + name);
                    void Raise(Control c, string method, EventArgs e) =>
                        c.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { e.GetType() }, null)
                            .Invoke(c, new object[] { e });

                    Raise(Field("Button1"), "OnClick", EventArgs.Empty);
                    Raise(Field("Button1"), "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0));
                    Raise(Field("TextBox1"), "OnKeyPress", new KeyPressEventArgs('x'));
                    Raise(Field("TextBox1"), "OnKeyPress", new KeyPressEventArgs('\r'));
                    Application.DoEvents();

                    Console.WriteLine("LBL " + Field("Label1").Text);
                    form.Close();
                    return 0;
                }
            }
            """);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, dir, timeoutMs: 300_000);
        Assert.That(exit, Is.Zero, $"the driver did not build.\n{stdout}\n{stderr}");
        return Directory.GetFiles(dir, "EvDriver.exe", SearchOption.AllDirectories).First();
    }

    private static List<string> RunDriver(string driver, string output)
    {
        var app = Path.Combine(output, "App.dll");
        Assert.That(File.Exists(app), Is.True, $"no App.dll in {output}:\n" + string.Join("\n", Directory.GetFiles(output)));
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(driver, new[] { app }, output, timeoutMs: 120_000);
        Assert.That(exit, Is.Zero, $"the designed form crashed at run time.\n{stdout}\n{stderr}");
        return stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
    }

    private sealed class SilentOutput : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();
        public void WriteLine(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) => _lines.Enqueue(message);
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
