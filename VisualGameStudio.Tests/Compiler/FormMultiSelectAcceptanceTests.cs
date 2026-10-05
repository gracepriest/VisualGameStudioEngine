using System.Collections.Concurrent;
using System.Text.RegularExpressions;
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
/// Property-grid slice 6 Task 7 — RUN, not compile: a MULTI-SELECTION edit reaches a running program on both targets through
/// both build entry points. The form is made through the real document view model on a real temp folder; two Buttons are
/// selected TOGETHER through the one store (<c>Selection.SetRange</c>, the marquee's route) and edited through the grid's
/// MERGED rows — Text, BackColor, the Font's Bold PART and the Size's Width PART — then the merged Events row's
/// double-click asks for ONE handler bound on both. <c>SaveAsync</c>, then the real CLI and <c>BuildService</c>, then RUN:
/// the two Buttons must come out identical, and a click on EACH runs the one handler.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormMultiSelectAcceptanceTests
{
    private const string FormName = "MultiForm";

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-multiaccept-" + Guid.NewGuid().ToString("N"));
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
    // The designer half: a multi-selection edited through the merged rows
    // ==================================================================

    private async Task<string> DesignAsync(FormTarget target)
    {
        var design = Path.Combine(_dir, "design");
        Directory.CreateDirectory(design);
        var scaffold = FormScaffolder.Create(FormName, target); // web: a Canvas page — pixel geometry, so Width exists
        File.WriteAllText(Path.Combine(design, scaffold.DocumentFileName), scaffold.DocumentText);
        File.WriteAllText(Path.Combine(design, scaffold.CodeFileName), scaffold.CodeText);

        var vm = new CodeEditorDocumentViewModel(new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path.Combine(design, scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True, "precondition: the designer opens");

        foreach (var (kind, x, y) in new[] { ("Label", 24, 24), ("Button", 24, 72), ("Button", 160, 72) })
        {
            Assert.That(vm.PlaceControl(kind, x, y), Is.Null, $"placing a {kind}");
        }

        var controls = vm.DesignDocument!.Controls.ToDictionary(c => c.Id);
        Assert.That(controls.Keys, Is.EquivalentTo(new[] { "Label1", "Button1", "Button2" }), "precondition: the ids the code uses");

        // Selected TOGETHER through the one store (the marquee's route); Button2 is the primary.
        vm.Selection.SetRange(new[] { controls["Button1"], controls["Button2"] });
        var grid = vm.PropertyGrid;
        Assert.That(grid.IsMultiSelection, Is.True, "precondition: the grid shows the set");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        grid.Rows.Single(r => r.Name == "Text").StringValue = "Go";
        grid.Rows.Single(r => r.Name == "BackColor").ApplyColor("Red");
        grid.AllRows().Single(r => r.Name == "Bold").StringValue = "True";
        grid.AllRows().Single(r => r.Name == "Width").StringValue = "90";
        Assert.That(edits, Is.EqualTo(4), "ONE Edited per merged edit (never one per member)");
        Log($"[1] {target}: four merged edits");

        // The merged Events row's double-click: ONE handler for the primary, bound on both.
        grid.IsEventsMode = true;
        var click = grid.EventRows.Single(r => r.Name == "Click");
        click.RequestHandler();
        for (var i = 0; i < 500 && click.Handler.Length == 0; i++)
        {
            await Task.Delay(20);
        }

        Assert.Multiple(() =>
        {
            Assert.That(click.Handler, Is.EqualTo("Button2_Click"), "the shared handler, named after the primary");
            Assert.That(vm.Selection.Controls.Count, Is.EqualTo(2), "the gesture kept the selection");
        });

        // One line of user code: count each run, and mark the Label.
        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        var signature = code.Split('\n').Single(l => l.Contains("Sub Button2_Click("));
        var body = target == FormTarget.Web ? "        Label1.textContent = \"clicked\"\n" : "        Label1.Text = \"clicked\"\n";
        code = code.Replace(signature, signature + "\n" + body + "        Console.WriteLine(\"Button2_Click\")");
        File.WriteAllText(codePath, code);

        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[2] saved");
        var saved = File.ReadAllText(Path.Combine(design, scaffold.DocumentFileName));
        Assert.That(Regex.Matches(saved, "Handler=\"Button2_Click\"").Count, Is.EqualTo(2), "both Buttons carry the bind:\n" + saved);
        return design;
    }

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

    private static string CliBuild(string route, string artifact)
    {
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path.Combine(route, "App.blproj") }, route, timeoutMs: 300_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");
        var built = Directory.GetFiles(Path.Combine(route, "bin"), artifact, SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(built, Is.Not.Null, $"the CLI built no {artifact} under bin/:\n{stdout}");
        return Path.GetDirectoryName(built!)!;
    }

    private static async Task<string> IdeBuild(string route)
    {
        var project = await new ProjectSerializer().LoadAsync(Path.Combine(route, "App.blproj"));
        var result = await new BuildService(new SilentOutput()).BuildProjectAsync(project);
        Assert.That(result.Success, Is.True, "the IDE build failed:\n" + string.Join("\n", result.Diagnostics.Select(d => d.Id + " " + d.Message)));
        return result.OutputPath!;
    }

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

    // ==================================================================
    // The web: both elements styled by the page's CSS, a click on each runs the one handler
    // ==================================================================

    private const string WebCliProject = """
        <BasicLangProject Version="1.0">
          <PropertyGroup>
            <ProjectName>App</ProjectName>
            <OutputType>Exe</OutputType>
            <TargetBackend>JavaScript</TargetBackend>
            <StartupForm>MultiForm</StartupForm>
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
            <Compile Include="MultiForm.bas" />
            <Compile Include="MultiForm.blwebform" />
          </ItemGroup>
        </Project>
        """;

    [Test]
    public async Task Web_AMultiSelectionEdit_BuildsThroughBothRoutes_AndBothButtonsRunIt()
    {
        RequireNode();
        var design = await DesignAsync(FormTarget.Web);
        var form = FormDocumentReader.Read(Path.Combine(design, FormName + ".blwebform"),
            File.ReadAllText(Path.Combine(design, FormName + ".blwebform"))).Model;

        const string dispatch = """
            console.log("--- dispatch");
            dispatch(get("Button1"), "click");
            dispatch(get("Button2"), "click");
            console.log("LBL " + get("Label1").textContent);
            """;

        foreach (var (name, output) in new[]
                 {
                     ("web/CLI", CliBuild(Route(design, "cli", WebCliProject), FormName + ".html")),
                     ("web/IDE", await IdeBuild(Route(design, "ide", WebIdeProject)))
                 })
        {
            AssertTheCss(output, name);
            AssertTheRun(RunNode(form, output, dispatch), "--- dispatch", name);
            Log($"[3] {name} ran under node");
        }
    }

    /// <summary>Both Buttons' rules in the page's stylesheet carry the merged edits — identical, as the run asserts too.</summary>
    private static void AssertTheCss(string output, string route)
    {
        var cssPath = Path.Combine(output, FormName + ".css");
        Assert.That(File.Exists(cssPath), Is.True, $"{route}: no {FormName}.css:\n" + string.Join("\n", Directory.GetFiles(output)));
        var css = File.ReadAllText(cssPath);
        var html = File.ReadAllText(Path.Combine(output, FormName + ".html"));
        Log($"[{route} css]\n{css}");

        string Rules(string id) => string.Join(" ", Regex.Matches(css, "#" + id + @"\b[^{]*\{([^}]*)\}").Select(m => m.Groups[1].Value));
        Assert.Multiple(() =>
        {
            foreach (var id in new[] { "Button1", "Button2" })
            {
                var rules = Rules(id);
                Assert.That(rules, Does.Contain("background-color: red").IgnoreCase.Or.Contain("#FF0000").IgnoreCase,
                    $"{route} {id}: BackColor");
                Assert.That(rules, Does.Contain("font-weight: bold"), $"{route} {id}: the Bold part");
                Assert.That(rules, Does.Contain("width: 90px"), $"{route} {id}: the Width part");
                Assert.That(html, Does.Match($"id=\"{id}\"[^>]*>Go<"), $"{route} {id}: the Text");
            }
        });
    }

    private static List<string> RunNode(FormDocument form, string output, string dispatch)
    {
        var script = File.Exists(Path.Combine(output, "App.js"))
            ? Path.Combine(output, "App.js")
            : Directory.GetFiles(output, "*.js").First(f => !Path.GetFileName(f).StartsWith("harness", StringComparison.Ordinal));
        File.Copy(script, Path.Combine(output, "app.mjs"), overwrite: true);
        File.WriteAllText(Path.Combine(output, "harness.mjs"), FormEventWebRunTests.Harness(form) + dispatch);

        try
        {
            var (exit, stdout, stderr) = CliTestHarness.RunProcess("node", new[] { "harness.mjs" }, output, timeoutMs: 30_000);
            Assert.That(exit, Is.Zero, $"node failed:\n{stdout}\n{stderr}");
            return stdout.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Ignore("node is not on PATH — the web run tier needs it");
            throw;
        }
    }

    /// <summary>The shared assertion: after the marker, the ONE handler ran TWICE (a click on each Button), the Label marked.</summary>
    private static void AssertTheRun(IReadOnlyList<string> log, string marker, string route)
    {
        var after = log.SkipWhile(l => l != marker).Skip(1).ToList();
        var all = string.Join("\n", log);
        Assert.Multiple(() =>
        {
            Assert.That(log, Does.Contain(marker), $"{route}: the run never reached '{marker}':\n{all}");
            Assert.That(after.Count(l => l == "Button2_Click"), Is.EqualTo(2), $"{route}: a click on EACH Button runs the handler:\n{all}");
            Assert.That(after, Does.Contain("LBL clicked"), $"{route}: the handler wrote the Label:\n{all}");
            Assert.That(log.Where(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase)), Is.Empty, $"{route}:\n{all}");
        });
    }

    // ==================================================================
    // WinForms: the two Buttons identical, a click on each runs the one handler
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
            <Compile Include="MultiForm.bas" />
            <Compile Include="MultiForm.blform" />
          </ItemGroup>
        </Project>
        """;

    [Test]
    [Platform(Include = "Win")]
    public async Task WinForms_AMultiSelectionEdit_BuildsThroughBothRoutes_AndBothButtonsRunIt()
    {
        if (!CliTestHarness.DotnetOnPath())
        {
            Assert.Ignore("the dotnet SDK is not on PATH");
        }

        var design = await DesignAsync(FormTarget.WinForms);
        var driver = BuildDriver();

        foreach (var (name, output) in new[]
                 {
                     ("WinForms/CLI", CliBuild(Route(design, "cli", WinCliProject), "App.dll")),
                     ("WinForms/IDE", await IdeBuild(Route(design, "ide", WinIdeProject)))
                 })
        {
            var log = RunDriver(driver, output);
            var all = string.Join("\n", log);
            Assert.Multiple(() =>
            {
                Assert.That(log, Does.Contain("Button1 Go|Red|True|90"), $"{name}: Button1 carries every merged edit:\n{all}");
                Assert.That(log, Does.Contain("Button2 Go|Red|True|90"), $"{name}: Button2 identical:\n{all}");
            });
            AssertTheRun(log, "--- clicks", name);
            Log($"[3] {name} ran");
        }
    }

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
                <AssemblyName>MultiDriver</AssemblyName>
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
                    var type = assembly.GetTypes().First(t => t.Name == "MultiForm");
                    var form = (Form)Activator.CreateInstance(type);

                    // ⛔ SHOWN: raising input on a never-shown form proves nothing (CLAUDE.md).
                    form.Show();
                    Application.DoEvents();

                    Control Field(string name) =>
                        (Control)type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(form)
                        ?? throw new InvalidOperationException("no field " + name);

                    foreach (var id in new[] { "Button1", "Button2" })
                    {
                        var b = Field(id);
                        Console.WriteLine($"{id} {b.Text}|{b.BackColor.Name}|{b.Font.Bold}|{b.Width}");
                    }

                    Console.WriteLine("--- clicks");
                    foreach (var id in new[] { "Button1", "Button2" })
                    {
                        var c = Field(id);
                        c.GetType().GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(EventArgs) }, null)
                            .Invoke(c, new object[] { EventArgs.Empty });
                    }

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
        return Directory.GetFiles(dir, "MultiDriver.exe", SearchOption.AllDirectories).First();
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
