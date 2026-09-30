using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Tests.Compiler.PixelLayout;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 3 Task 7 (plan 3.6, spec §8 "Run, not compile"): a form designed with slice 3's properties — the Form's
/// FormBorderStyle/StartPosition/BackColor/Font/Opacity/TopMost/AcceptButton, a TextBox's PlaceholderText/TextAlign, a
/// Button's Cursor/Padding, a CheckBox's CheckAlign, a Font set through a composite PART — built through the real
/// designer view model and the real property grid, compiled by the real CLI, and RUN.
///
/// <para>⛔⛔ "It compiles" was this feature's ceiling once and hid two defects (CLAUDE.md). The WinForms driver prints what
/// the LIVE window reports and presses Enter through the form's own dialog-key processing, so the AcceptButton wiring
/// is proven by the handler firing; the web page runs under node (the click handler and a GroupBox's Enter — its
/// <c>focusin</c> — fire), its stylesheet is asserted, and, where Edge is installed, the browser's COMPUTED styles are
/// read back.</para>
///
/// <para>⚠ Windows-only (WinForms, Edge) and node-less machines SKIP, never fail.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class FormPropertyBatchAcceptanceTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-batch-" + Guid.NewGuid().ToString("N"));
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

    private void Write(string name, string content) => File.WriteAllText(Path_(name), content);

    private static void Log(string what) => TestContext.Out.WriteLine(what);

    private CodeEditorDocumentViewModel NewForm(FormTarget target)
    {
        var scaffold = FormScaffolder.Create("BatchForm", target);
        Write(scaffold.DocumentFileName, scaffold.DocumentText);
        Write(scaffold.CodeFileName, scaffold.CodeText);

        var vm = new CodeEditorDocumentViewModel(new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = Path_(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);
        Assert.That(vm.DesignDocument, Is.Not.Null, "the new form did not open in the designer");
        return vm;
    }

    private static FormControl Place(CodeEditorDocumentViewModel vm, string kind, int x, int y)
    {
        var before = vm.DesignDocument!.AllControls().ToList();
        Assert.That(vm.PlaceControl(kind, x, y), Is.Null, $"placing a {kind} was refused");
        return vm.DesignDocument!.AllControls().Except(before).Single();
    }

    /// <summary>
    /// Sets a row of the REAL grid, the owner selected through the ONE selection store (null = the form). A top-level row
    /// by name, else a composite's part (<c>Font</c> → <c>Italic</c> is looked up as the part).
    /// </summary>
    private static void SetThroughGrid(CodeEditorDocumentViewModel vm, FormControl? owner, string name, string value)
    {
        vm.Selection.Set(owner);
        Assert.That(vm.PropertyGrid.SelectedControl, Is.SameAs(owner), "the grid follows the selection store");

        var row = vm.PropertyGrid.Rows.SingleOrDefault(r => r.Name == name)
                  ?? vm.PropertyGrid.AllRows().SingleOrDefault(r => r.Parent != null && r.Name == name);
        Assert.That(row, Is.Not.Null, $"the grid has no '{name}' row for {owner?.Id ?? "the form"}");

        if (row!.TypeName == nameof(FormPropertyType.Bool))
        {
            row.BoolValue = bool.Parse(value);
        }
        else
        {
            row.StringValue = value;
        }

        Assert.That(row.Refusal, Is.Null, $"'{name}' = '{value}' was refused: {row.Refusal}");
    }

    private static async Task<string> DoubleClick(CodeEditorDocumentViewModel vm, FormControl control, string printed)
    {
        await vm.ActivateControlCommand.ExecuteAsync(control);
        var bind = control.Binds.Single();
        var codePath = FormCodeBehind.PathFor(vm.FilePath!);
        var code = File.ReadAllText(codePath);
        var signature = code.Split('\n').First(l => l.Contains($"Sub {bind.Handler}"));
        File.WriteAllText(codePath, code.Replace(signature, signature + $"\n        Console.WriteLine(\"{printed}\")"));
        return bind.Event;
    }

    // ==================================================================
    // WinForms: the live window reports what the designer set
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task WinForms_TheSliceThreeProperties_ReachTheLiveWindow_AndEnterPressesTheAcceptButton()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("a WinForms window can only be run on Windows");
        }

        var vm = NewForm(FormTarget.WinForms);
        var label = Place(vm, "Label", 24, 24);
        var textBox = Place(vm, "TextBox", 120, 24);
        var button = Place(vm, "Button", 120, 72);
        var check = Place(vm, "CheckBox", 24, 110);

        // The Form's own rows (nothing selected).
        SetThroughGrid(vm, null, "FormBorderStyle", "FixedDialog");
        SetThroughGrid(vm, null, "StartPosition", "CenterScreen");
        SetThroughGrid(vm, null, "BackColor", "LightYellow");
        SetThroughGrid(vm, null, "Font", "Segoe UI, 10pt, style=Bold");
        SetThroughGrid(vm, null, "Opacity", "90%");   // typed as VS types it (owner decision 2026-09-29)
        SetThroughGrid(vm, null, "TopMost", "true");
        SetThroughGrid(vm, null, "AcceptButton", button.Id);

        SetThroughGrid(vm, textBox, "PlaceholderText", "Your name");
        SetThroughGrid(vm, textBox, "TextAlign", "Center");
        SetThroughGrid(vm, button, "Text", "OK");
        SetThroughGrid(vm, button, "Cursor", "Hand");
        SetThroughGrid(vm, button, "Padding", "4, 2, 4, 2");
        SetThroughGrid(vm, check, "Text", "Remember");
        SetThroughGrid(vm, check, "CheckAlign", "MiddleRight");
        SetThroughGrid(vm, label, "Text", "Name");
        SetThroughGrid(vm, label, "Italic", "true");   // a composite PART: Font → Italic

        await DoubleClick(vm, button, "HANDLER FIRED");
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        Log("[saved]\n" + File.ReadAllText(vm.FilePath!));

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { Path_("BatchForm.bas"), "--target=csharp" }, _dir, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's .bas.\n{stdout}\n{stderr}");

        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(app);
        File.Copy(Path_("BatchForm.cs"), Path.Combine(app, "BatchForm.cs"));
        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>BatchApp</AssemblyName>
                <RootNamespace>BatchApp</RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        // ⚠ The DRIVER is the only hand-written part. It reads the LIVE window's properties, then presses Enter through
        // the form's own ProcessDialogKey — the path a real Enter takes to the AcceptButton.
        File.WriteAllText(Path.Combine(app, "Driver.cs"), """
            using System;
            using System.Globalization;
            using System.Reflection;
            using System.Windows.Forms;
            using GeneratedCode;

            internal static class Driver
            {
                [STAThread]
                private static void Main()
                {
                    var form = new BatchForm();
                    form.Show();
                    Application.DoEvents();

                    Console.WriteLine("FORM border=" + form.FormBorderStyle + " start=" + form.StartPosition +
                        " back=" + form.BackColor.Name + " bold=" + form.Font.Bold +
                        " size=" + form.Font.SizeInPoints.ToString(CultureInfo.InvariantCulture) +
                        " opacity=" + form.Opacity.ToString(CultureInfo.InvariantCulture) + " topmost=" + form.TopMost +
                        " accept=" + ((Button)form.AcceptButton).Text);

                    foreach (Control c in form.Controls)
                    {
                        var line = "CONTROL " + c.GetType().Name + " text='" + c.Text + "'" +
                                   " italic=" + c.Font.Italic + " bold=" + c.Font.Bold +
                                   " cursor=" + (c.Cursor == Cursors.Hand ? "Hand" : c.Cursor == Cursors.IBeam ? "IBeam" : "Other") +
                                   " padding=" + c.Padding.Left + "," + c.Padding.Top + "," + c.Padding.Right + "," + c.Padding.Bottom;
                        if (c is TextBox t) line += " placeholder='" + t.PlaceholderText + "' align=" + t.TextAlign;
                        if (c is CheckBox k) line += " checkalign=" + k.CheckAlign;
                        Console.WriteLine(line);
                    }

                    var dialogKey = typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.NonPublic | BindingFlags.Instance);
                    Console.WriteLine("ENTER handled=" + dialogKey.Invoke(form, new object[] { Keys.Enter }));
                    Application.DoEvents();

                    form.Close();
                    Console.WriteLine("DONE");
                }
            }
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
        Assert.That(buildExit, Is.Zero, $"the WinForms app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory.GetFiles(app, "BatchApp.exe", SearchOption.AllDirectories).FirstOrDefault();
        Assert.That(exe, Is.Not.Null, "no executable was produced");

        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe!, Array.Empty<string>(), app, timeoutMs: 120_000);
        Log("[runtime]\n" + runOut + runErr);
        Assert.That(runExit, Is.Zero, $"the designed form crashed at run time.\n{runOut}\n{runErr}");

        Assert.Multiple(() =>
        {
            Assert.That(runOut, Does.Contain("FORM border=FixedDialog start=CenterScreen back=LightYellow bold=True size=10 opacity=0.9 topmost=True accept=OK"),
                "the Form's slice-3 rows as the live window reports them");
            // (A dropped TextBox carries its id as its Text — placement's caption rule.)
            Assert.That(runOut, Does.Contain("CONTROL TextBox text='TextBox1'").And.Contain("placeholder='Your name' align=Center"));
            Assert.That(runOut, Does.Contain("CONTROL Button text='OK'").And.Contain("cursor=Hand padding=4,2,4,2"));
            Assert.That(runOut, Does.Contain("checkalign=MiddleRight"));
            Assert.That(runOut, Does.Contain("CONTROL Label text='Name' italic=True bold=True"),
                "the Font PART wrote a whole font starting from the one the Label INHERITS (the Form's 10pt Bold) — " +
                "VS writes '…10pt, style=Bold, Italic' (code review I1)");
            Assert.That(runOut, Does.Contain("ENTER handled=True"), "Enter reached the AcceptButton");
            Assert.That(runOut, Does.Contain("HANDLER FIRED"), "the AcceptButton's Click handler ran — AcceptButton was wired to a live button");
            Assert.That(runOut, Does.Contain("DONE"));
        });
    }

    // ==================================================================
    // The web: the page's CSS, its handlers under node, and Edge's computed styles
    // ==================================================================

    [Test]
    [Category("Integration")]
    public async Task Web_TheSliceThreeProperties_ReachThePage_AndItsHandlersRun()
    {
        var vm = NewForm(FormTarget.Web);   // the default web scaffold: a Canvas page
        var textBox = Place(vm, "TextBox", 120, 24);
        var button = Place(vm, "Button", 120, 72);
        var group = Place(vm, "GroupBox", 300, 120);

        SetThroughGrid(vm, null, "BackColor", "#FFFFE0");
        SetThroughGrid(vm, null, "ForeColor", "#202020");
        SetThroughGrid(vm, null, "Font", "Segoe UI, 10pt, style=Bold");
        SetThroughGrid(vm, textBox, "PlaceholderText", "Your name");
        SetThroughGrid(vm, textBox, "TextAlign", "Center");
        SetThroughGrid(vm, button, "Text", "OK");
        SetThroughGrid(vm, button, "Cursor", "Hand");
        SetThroughGrid(vm, button, "Padding", "4, 2, 4, 2");
        SetThroughGrid(vm, button, "CssClass", "primary");

        await DoubleClick(vm, button, "HANDLER FIRED");
        var groupEvent = await DoubleClick(vm, group, "ENTER FIRED");
        Assert.That(groupEvent, Is.EqualTo("focusin"), "a GroupBox double-click wires Enter — the page's focusin (owner decision O3)");
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");
        var saved = FormDocumentReader.Read(vm.FilePath!, File.ReadAllText(vm.FilePath!)).Model;
        Log("[saved]\n" + File.ReadAllText(vm.FilePath!));

        Write("App.blproj", """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>BatchForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        Write("Main.bas", "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", Path_("App.blproj") }, _dir, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's output.\n{stdout}\n{stderr}");

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        var html = File.ReadAllText(Path.Combine(outDir, "BatchForm.html"));
        var css = File.ReadAllText(Path.Combine(outDir, "BatchForm.css"));
        Log("[css]\n" + css);

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("body { background-color: #FFFFE0; color: #202020; font-family: \"Segoe UI\"; font-size: 10pt; font-weight: bold; }"),
                "the Form's web rows are ONE body rule");
            Assert.That(css, Does.Contain("cursor: pointer").And.Contain("padding: 2px 4px 2px 4px"), "the Button's cursor and padding");
            Assert.That(css, Does.Contain("text-align: center"), "the TextBox's alignment");
            Assert.That(html, Does.Contain("placeholder=\"Your name\""));
            Assert.That(html, Does.Contain($"id=\"{button.Id}\" class=\"vgs-Button primary\""));
        });

        // ⛔ RUN it — before Edge writes its own script into the directory.
        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(outDir, formName: "BatchForm", clickId: button.Id,
            dispatch: (group.Id, "focusin"));
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Log("[runtime]\n" + ran);
        Assert.Multiple(() =>
        {
            Assert.That(ran, Does.Not.Contain("LOAD ERROR").And.Not.Contain("ReferenceError"), "a green build is not a running page");
            Assert.That(ran, Does.Contain("HANDLER FIRED"), "the Button's click handler ran");
            Assert.That(ran, Does.Contain("ENTER FIRED"), "the GroupBox's Enter (focusin) handler ran");
        });

        // Where Edge is installed: what the BROWSER resolved, not what the stylesheet says.
        if (EdgeLayoutHarness.EdgePath() == null)
        {
            Log("[edge] not installed here — computed styles not measured");
            return;
        }

        var edge = EdgeLayoutHarness.Measure(outDir, new[]
        {
            EdgeCase.Of(saved, 800, 450,
                EdgeStep.Style("bodyBack", "", "background-color"),
                EdgeStep.Style("bodyWeight", "", "font-weight"),
                EdgeStep.Style("buttonCursor", button.Id, "cursor"),
                EdgeStep.Style("buttonPaddingTop", button.Id, "padding-top"),
                EdgeStep.Style("buttonWeight", button.Id, "font-weight"),
                EdgeStep.Style("textAlign", textBox.Id, "text-align"))
        });
        var probes = edge.Results["BatchForm@800x450"].Probes;
        Log("[edge] " + string.Join(", ", probes.Select(p => $"{p.Key}={p.Value}")));

        Assert.Multiple(() =>
        {
            Assert.That(probes["bodyBack"], Is.EqualTo("rgb(255, 255, 224)"));
            Assert.That(probes["bodyWeight"], Is.EqualTo("700"));
            Assert.That(probes["buttonCursor"], Is.EqualTo("pointer"));
            Assert.That(probes["buttonPaddingTop"], Is.EqualTo("2px"));
            // ⛔ The first run of this test measured 400 here: a browser does not inherit body's font into a <button>.
            Assert.That(probes["buttonWeight"], Is.EqualTo("700"), "the Form's bold Font reaches the button, as WinForms' ambient Font does");
            Assert.That(probes["textAlign"], Is.EqualTo("center"));
        });
    }
}
