using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 28's acceptance: a menu (MenuStrip + ToolStrip + StatusStrip) built ENTIRELY through the
/// designer's own commands — <c>PlaceControl</c>, <c>BeginTypeHere</c>, <c>CommitTypeHere</c>,
/// <c>ActivateControlCommand</c> — compiles through the real CLI and RUNS on both targets.
///
/// <para>⛔⛔ Everything upstream of this stops at "it compiles". This repo has shipped green builds
/// whose pages died on load and designers whose finished pieces had no caller — so: the real view
/// model, the real region writer, the real compiler, a process that prints what actually happened.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class FormMenuAcceptanceTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-menu-accept-" + Guid.NewGuid().ToString("N"));
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
    /// The designer half, shared by both targets: a MenuStrip with File > Open.../-/Exit, a ToolStrip
    /// with one button, a StatusStrip with one label — every strip built through its OWN
    /// <c>BeginTypeHere</c>/<c>CommitTypeHere</c> pair, exactly as Task 23's leave-rule requires.
    /// </summary>
    private async Task<(CodeEditorDocumentViewModel Vm, FormControl OpenItem)> BuildMenuFormInDesignerAsync(FormTarget target)
    {
        var scaffold = FormScaffolder.Create("MenuForm", target);
        File.WriteAllText(Path_(scaffold.DocumentFileName), scaffold.DocumentText);
        File.WriteAllText(Path_(scaffold.CodeFileName), scaffold.CodeText);
        Log($"[1] new form      -> {scaffold.DocumentFileName} + {scaffold.CodeFileName}");

        var vm = new CodeEditorDocumentViewModel(
            new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path_(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);

        // --- MenuStrip: File > Open..., -, Exit ---
        var menuRefusal = vm.PlaceControl("MenuStrip", 0, 0);
        Assert.That(menuRefusal, Is.Null, $"placing a MenuStrip was refused: {menuRefusal}");
        var menuStrip = vm.DesignDocument!.FindById("MenuStrip1");
        Assert.That(menuStrip, Is.Not.Null, "the designer mints 'MenuStrip1' for the first strip of its kind");
        Log("[2] placed        -> MenuStrip1");

        vm.BeginTypeHere(menuStrip);
        vm.CommitTypeHere("&File");
        var file = vm.DesignDocument!.FindById("fileToolStripMenuItem");
        Assert.That(file, Is.Not.Null, "'&File' must mint 'fileToolStripMenuItem'");
        Log("[3] typed         -> &File -> fileToolStripMenuItem");

        vm.BeginTypeHere(file);
        vm.CommitTypeHere("&Open...");
        var open = vm.DesignDocument!.FindById("openToolStripMenuItem");
        Assert.That(open, Is.Not.Null, "'&Open...' must mint 'openToolStripMenuItem'");

        vm.CommitTypeHere("-");
        var sep = vm.DesignDocument!.FindById("toolStripSeparator1");
        Assert.That(sep, Is.Not.Null, "'-' must mint 'toolStripSeparator1'");

        vm.CommitTypeHere("E&xit");
        var exit = vm.DesignDocument!.FindById("exitToolStripMenuItem");
        Assert.That(exit, Is.Not.Null, "'E&xit' must mint 'exitToolStripMenuItem'");
        Log("[4] typed         -> &Open... , - , E&xit under fileToolStripMenuItem");

        // --- ToolStrip: one button ---
        var toolRefusal = vm.PlaceControl("ToolStrip", 0, 0);
        Assert.That(toolRefusal, Is.Null, $"placing a ToolStrip was refused: {toolRefusal}");
        var toolStrip = vm.DesignDocument!.FindById("ToolStrip1");
        Assert.That(toolStrip, Is.Not.Null, "the designer mints 'ToolStrip1'");
        vm.BeginTypeHere(toolStrip);
        vm.CommitTypeHere("Open");
        Log("[5] placed+typed  -> ToolStrip1 -> Open");

        // --- StatusStrip: one label ---
        var statusRefusal = vm.PlaceControl("StatusStrip", 0, 0);
        Assert.That(statusRefusal, Is.Null, $"placing a StatusStrip was refused: {statusRefusal}");
        var statusStrip = vm.DesignDocument!.FindById("StatusStrip1");
        Assert.That(statusStrip, Is.Not.Null, "the designer mints 'StatusStrip1'");
        vm.BeginTypeHere(statusStrip);
        vm.CommitTypeHere("Ready");
        Log("[6] placed+typed  -> StatusStrip1 -> Ready");

        // --- wire the Open item, exactly as a double-click would ---
        await vm.ActivateControlCommand.ExecuteAsync(open);
        var bind = open!.Binds.SingleOrDefault();
        Assert.That(bind, Is.Not.Null, "activating the Open item did not wire it");
        Log($"[7] activated     -> {bind!.Event} => {bind.Handler}");

        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        var signature = code.Split('\n').First(l => l.Contains($"Sub {bind.Handler}"));
        File.WriteAllText(codePath, code.Replace(signature, signature + "\n        Console.WriteLine(\"CLICK\")"));
        Log("[8] user code     -> Console.WriteLine(\"CLICK\") in " + bind.Handler);

        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[9] saved         -> document written, designer regions regenerated");

        return (vm, open);
    }

    // ==================================================================
    // WinForms — MENU / DROP / CLICK / STATUS / DONE, in order
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task WinForms_ADesignerBuiltMenu_RunsInARealWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("a WinForms window can only be run on Windows");
        }

        await BuildMenuFormInDesignerAsync(FormTarget.WinForms);

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { Path_("MenuForm.bas"), "--target=csharp" }, _dir, timeoutMs: 180_000);
        Log($"[10] compile exit -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's .bas.\n{stdout}\n{stderr}");

        var generated = Path_("MenuForm.cs");
        Assert.That(File.Exists(generated), Is.True, $"no C# emitted.\n{stdout}");
        var csharp = File.ReadAllText(generated);

        // ⚠ Measured, not assumed: the construct may be qualified like the Timer's
        // (`new System.Windows.Forms.ToolStripMenuItem()`) or bare (`new ToolStripMenuItem()`) — the
        // catalog's own field-declared type is bare ("ToolStripMenuItem"), and only a name that
        // collides with an auto-added `using` (Timer vs System.Threading) is forced qualified. Both
        // are accepted here; whichever is present is what this test pins going forward.
        Assert.That(csharp, Does.Match(@"openToolStripMenuItem\s*=\s*new\s+(System\.Windows\.Forms\.)?ToolStripMenuItem\(\)"),
            "openToolStripMenuItem must be constructed as a ToolStripMenuItem");
        Assert.That(csharp, Does.Contain("fileToolStripMenuItem.DropDownItems.Add(openToolStripMenuItem)"));

        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(app);
        File.Copy(generated, Path.Combine(app, "MenuForm.cs"));
        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>MenuApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);

        // ⚠ CORRECTION to the plan's Task 28 text: the OPEN item is the FILE menu's first drop-down
        // item, not menu-bar item 0 (menu-bar item 0 is File itself, which has no handler).
        File.WriteAllText(Path.Combine(app, "Driver.cs"), """
            using System;
            using System.Linq;
            using System.Windows.Forms;
            using GeneratedCode;

            internal static class Driver
            {
                [STAThread]
                private static void Main()
                {
                    var form = new MenuForm();
                    form.Show();

                    Console.WriteLine("MENU " + string.Join(",",
                        form.MainMenuStrip.Items.Cast<ToolStripItem>().Select(i => i.Text)));

                    var file = (ToolStripMenuItem)form.MainMenuStrip.Items[0];
                    Console.WriteLine("DROP " + string.Join(",",
                        file.DropDownItems.Cast<ToolStripItem>().Select(i => i.GetType().Name + ":" + i.Text)));

                    ((ToolStripMenuItem)file.DropDownItems[0]).PerformClick();

                    var status = form.Controls.OfType<StatusStrip>().Single();
                    Console.WriteLine("STATUS " + status.Items[0].Text);

                    form.Close();
                    Console.WriteLine("DONE");
                }
            }
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
        Log($"[11] dotnet build -> {buildExit}");
        Assert.That(buildExit, Is.Zero, $"the WinForms app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory.GetFiles(app, "MenuApp.exe", SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(exe, Is.Not.Null, "no executable was produced");

        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 60_000);
        Log($"[12] run          -> exit {runExit}\n{runOut.Trim()}");

        Assert.Multiple(() =>
        {
            Assert.That(runExit, Is.Zero, runErr);

            var menu = runOut.IndexOf("MENU &File", StringComparison.Ordinal);
            var drop = runOut.IndexOf("DROP ToolStripMenuItem:&Open...,ToolStripSeparator:,ToolStripMenuItem:E&xit", StringComparison.Ordinal);
            var click = runOut.IndexOf("CLICK", StringComparison.Ordinal);
            var status = runOut.IndexOf("STATUS Ready", StringComparison.Ordinal);
            var done = runOut.IndexOf("DONE", StringComparison.Ordinal);

            Assert.That(menu, Is.GreaterThanOrEqualTo(0), $"MENU line missing.\n{runOut}");
            Assert.That(drop, Is.GreaterThan(menu), $"DROP line missing or out of order.\n{runOut}");
            Assert.That(click, Is.GreaterThan(drop), $"CLICK missing or out of order — the Open item never fired.\n{runOut}");
            Assert.That(status, Is.GreaterThan(click), $"STATUS line missing or out of order.\n{runOut}");
            Assert.That(done, Is.GreaterThan(status), $"DONE missing or out of order.\n{runOut}");
        });
    }

    // ==================================================================
    // Web — the page IS the chrome, and the click reaches the handler under node
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task Web_ADesignerBuiltMenu_RunsUnderNode()
    {
        await BuildMenuFormInDesignerAsync(FormTarget.Web);

        File.WriteAllText(Path_("App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>MenuForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(Path_("Main.bas"),
            "Sub Main()\n    Console.WriteLine(\"App loaded\")\n    VgsForms.VgsDispatchForm()\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path_("App.blproj") }, _dir, timeoutMs: 180_000);
        Log($"[10] build exit   -> {exit}");
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");
        Assert.That(stdout + stderr, Does.Not.Contain("BL8018"));

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        var page = Path.Combine(outDir, "MenuForm.html");
        Assert.That(File.Exists(page), Is.True, $"no page was emitted.\n{stdout}");

        var html = File.ReadAllText(page);
        Log($"[11] page         -> MenuForm.html, {html.Length} bytes");

        var pos = -1;
        void ExpectNext(string fragment)
        {
            var idx = html.IndexOf(fragment, pos + 1, StringComparison.Ordinal);
            Assert.That(idx, Is.GreaterThan(pos), $"expected to find '{fragment}' after position {pos} in:\n{html}");
            pos = idx;
        }

        Assert.Multiple(() =>
        {
            ExpectNext("<nav id=\"MenuStrip1\"");
            ExpectNext("<div class=\"vgs-form\"");
        });

        // Reset and walk the nesting/ids inside the nav separately — the div comes before the deeper
        // menu structure in document order (the strip is emitted as page chrome, all at once), so this
        // is checked as its own ordered walk over the nav's own markup rather than continuing `pos`
        // past the div.
        var navStart = html.IndexOf("<nav id=\"MenuStrip1\"", StringComparison.Ordinal);
        pos = navStart - 1;
        Assert.Multiple(() =>
        {
            ExpectNext("<nav id=\"MenuStrip1\"");
            ExpectNext("<ul");
            ExpectNext("<li id=\"fileToolStripMenuItem\"");
            ExpectNext("<ul");
            ExpectNext("<li id=\"openToolStripMenuItem\"");
            ExpectNext("<li id=\"toolStripSeparator1\"");
        });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("role=\"separator\""), "the separator must carry its ARIA role");
            Assert.That(html, Does.Contain("File"), "the accelerator-stripped caption must be on the page");
            Assert.That(html, Does.Not.Contain("&amp;File"), "the raw '&File' must never reach the page");
        });

        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(outDir, "MenuForm", "openToolStripMenuItem");
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Log($"[12] runtime      -> {ran!.Trim()}");
        var clicks = System.Text.RegularExpressions.Regex.Matches(ran, "CLICK").Count;
        Assert.Multiple(() =>
        {
            Assert.That(clicks, Is.EqualTo(1), $"expected exactly one CLICK.\n{ran}");
            Assert.That(ran, Does.Not.Contain("LOAD ERROR"), $"the page threw on load.\n{ran}");
            Assert.That(ran, Does.Not.Contain("NO ELEMENT"), $"openToolStripMenuItem was never registered.\n{ran}");
            Assert.That(ran, Does.Not.Contain("ReferenceError").And.Not.Contain("TypeError"), $"the page threw.\n{ran}");
        });
    }
}
