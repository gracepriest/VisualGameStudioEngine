using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 25's acceptance: a Timer dropped in the designer TICKS — built by the real CLI, on both
/// targets, and RUN.
///
/// <para>⛔⛔ Everything upstream stops at "it compiles". The csc sweep proves every component
/// property and event name is real; the emission tests pin the shape; neither can see whether a
/// Timer the designer wired actually fires. This repo has shipped a green build whose every page
/// died on load, and a designer whose finished pieces had no caller. So: the designer's own
/// commands, the real region writer, the real compiler, and a process that prints TICK.</para>
///
/// <para>⚠ What is simulated: no pointer gestures (those have their own headless tests). What is
/// not: the build, the window, the page, the tick.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class FormComponentAcceptanceTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-tray-accept-" + Guid.NewGuid().ToString("N"));
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

    private string Path_(string name) => Path.Combine(_dir, name);

    private static void Log(string what) => TestContext.Out.WriteLine(what);

    /// <summary>
    /// The designer half: new form, a Timer from the toolbox into the tray, its properties through
    /// the real grid, a double-click for the handler, one line of user code, save.
    /// </summary>
    private async Task<CodeEditorDocumentViewModel> BuildTimerFormInDesignerAsync(FormTarget target)
    {
        // ⚠ Named LoginForm on both targets: the node harness hard-codes data-form="LoginForm".
        var scaffold = FormScaffolder.Create("LoginForm", target);
        File.WriteAllText(Path_(scaffold.DocumentFileName), scaffold.DocumentText);
        File.WriteAllText(Path_(scaffold.CodeFileName), scaffold.CodeText);
        Log($"[1] new form      -> {scaffold.DocumentFileName} + {scaffold.CodeFileName}");

        var vm = new CodeEditorDocumentViewModel(
            new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path_(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);

        // --- a Timer from the toolbox: it lands in the tray, wherever it was dropped ---
        var refusal = vm.PlaceControl("Timer", 200, 200);
        Assert.That(refusal, Is.Null, $"placing a Timer was refused: {refusal}");
        var timer = vm.DesignDocument!.FindById("Timer1");
        Assert.That(timer, Is.Not.Null);
        Assert.That(vm.Tray.Items.Single().Id, Is.EqualTo("Timer1"));
        Log("[2] placed        -> Timer1, in the tray");

        // --- properties, through the real grid (Enabled is WinForms-only and offered only there) ---
        vm.PropertyGrid.SelectedControl = timer;
        SetRow(vm, "Interval", "1");
        if (target == FormTarget.WinForms)
        {
            SetRow(vm, "Enabled", "true");
        }

        Assert.That(vm.PropertyGrid.Rows.Any(r => r.Name == "TabIndex"), Is.False, "a component has no tab order");
        Log("[3] properties    -> Interval=1" + (target == FormTarget.WinForms ? ", Enabled=true" : ""));

        // --- double-click: creates the handler and wires it ---
        await vm.ActivateControlCommand.ExecuteAsync(timer);
        var bind = timer!.Binds.SingleOrDefault();
        Assert.That(bind, Is.Not.Null, "the double-click did not wire the timer");
        Log($"[4] double-click  -> {bind!.Event} => {bind.Handler}");

        // --- one line of user code in the stub ---
        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        var signature = code.Split('\n').First(l => l.Contains($"Sub {bind.Handler}"));
        var body = target == FormTarget.WinForms
            ? "        Console.WriteLine(\"TICK\")\n        Timer1.Enabled = False"
            : "        Console.WriteLine(\"TICK\")";
        File.WriteAllText(codePath, code.Replace(signature, signature + "\n" + body));
        Log($"[5] user code     -> Console.WriteLine in {bind.Handler}");

        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[6] saved         -> document written, designer regions regenerated");

        var regenerated = File.ReadAllText(codePath);
        Assert.That(regenerated, Does.Not.Contain("Controls.Add(Timer1)"), "a component is not a control");
        return vm;
    }

    private static void SetRow(CodeEditorDocumentViewModel vm, string name, string value)
    {
        var row = vm.PropertyGrid.Rows.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        Assert.That(row, Is.Not.Null, $"the property grid has no '{name}' row for the timer");
        row!.StringValue = value;
    }

    // ==================================================================
    // WinForms — the Timer ticks in a real window
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task WinForms_ATimerFromTheTray_TicksInARealWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("a WinForms window can only be run on Windows");
        }

        await BuildTimerFormInDesignerAsync(FormTarget.WinForms);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(),
            new[] { Path_("LoginForm.bas"), "--target=csharp" }, _dir, timeoutMs: 180_000);
        Log($"[7] compile exit  -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's .bas.\n{stdout}\n{stderr}");

        var generated = Path_("LoginForm.cs");
        Assert.That(File.Exists(generated), Is.True, $"no C# emitted.\n{stdout}");
        var csharp = File.ReadAllText(generated);
        Assert.That(csharp, Does.Contain("new System.Windows.Forms.Timer()"), "the qualified construct reached the C#");

        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(app);
        File.Copy(generated, Path.Combine(app, "LoginForm.cs"));
        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>TrayApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        // ⚠ The DRIVER is the only hand-written part: show the form and pump the message loop
        // until the designer's Timer has had every chance to fire. The form, its Timer, its
        // InitializeComponent and its handler are entirely the designer's and the compiler's.
        File.WriteAllText(Path.Combine(app, "Driver.cs"), """
            using System;
            using System.Windows.Forms;
            using GeneratedCode;

            internal static class Driver
            {
                [STAThread]
                private static void Main()
                {
                    var form = new LoginForm();
                    form.Show();
                    var until = DateTime.UtcNow.AddSeconds(3);
                    while (DateTime.UtcNow < until)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(5);
                    }
                    form.Close();
                    Console.WriteLine("DONE");
                }
            }
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
        Log($"[8] dotnet build  -> {buildExit}");
        Assert.That(buildExit, Is.Zero, $"the WinForms app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory.GetFiles(app, "TrayApp.exe", SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(exe, Is.Not.Null, "no executable was produced");

        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 60_000);
        Log($"[9] run           -> exit {runExit}\n{runOut.Trim()}");

        Assert.Multiple(() =>
        {
            Assert.That(runExit, Is.Zero, runErr);
            Assert.That(runOut, Does.Contain("TICK"), "the Timer the designer wired never fired");
            Assert.That(runOut, Does.Contain("DONE"));
        });
    }

    // ==================================================================
    // Web — the Timer is a setInterval handle, and it fires
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task Web_ATimerFromTheTray_TicksUnderNode()
    {
        await BuildTimerFormInDesignerAsync(FormTarget.Web);

        var code = File.ReadAllText(Path_("LoginForm.bas"));
        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private Sub Timer1_Tick()"), "parameterless: Window.setInterval takes an Action");
            Assert.That(code, Does.Contain("Timer1 = w.setInterval(AddressOf Timer1_Tick, 1)"), "the typed call");
        });

        File.WriteAllText(Path_("App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>LoginForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path_("Main.bas"),
            "Sub Main()\n    Console.WriteLine(\"App loaded\")\n    VgsForms.VgsDispatchForm()\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path_("App.blproj") }, _dir, timeoutMs: 180_000);
        Log($"[7] build exit    -> {exit}");
        Assert.That(exit, Is.Zero,
            "the real CLI refused the designer's output — with the TYPED setInterval a wrong stub " +
            $"signature fails HERE.\n{stdout}\n{stderr}");
        Assert.That(stdout + stderr, Does.Not.Contain("BL8018"));

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        Assert.That(File.Exists(Path.Combine(outDir, "LoginForm.html")), Is.True, $"no page was emitted.\n{stdout}");
        Assert.That(File.ReadAllText(Path.Combine(outDir, "LoginForm.html")), Does.Not.Contain("Timer1"),
            "a component has no markup");

        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(outDir);
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Log($"[8] runtime       -> {ran!.Trim()}");
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("ReferenceError").And.Not.Contain("TypeError"),
                "the page threw on load — a green build is not a running page");
            Assert.That(ran, Does.Contain("setInterval 1"), "the Timer was never started");
            Assert.That(ran, Does.Contain("TICK"), "the tick handler did not fire");
        });
    }
}
