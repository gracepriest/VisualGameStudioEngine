using System.Text.RegularExpressions;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// ⛔ Slice 4 Task 6 / ADR 0020, RUN on both targets — "it compiles" was the ceiling here once and hid two defects
/// (CLAUDE.md). A ComboBox whose items contain a comma, an ampersand and a less-than is built through the REAL designer
/// (the document view model, the grid's Items row, SaveAsync writing the document and the regions), compiled by the real
/// CLI, and run: WinForms as a real window that prints its live <c>Items</c>; the web as the CLI-built page, its script
/// executed under node and its <c>&lt;option&gt;</c>s read from the page the browser would load.
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class FormItemsAcceptanceTests
{
    private static readonly string[] Expected = { "Smith, John", "A & B", "x < y" };

    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    /// <summary>The designer half: a new form, a ComboBox, its Items set through the real grid row, saved.</summary>
    private async Task DesignAsync(FormTarget target)
    {
        var scaffold = FormScaffolder.Create("ItemsForm", target, FormLayoutKind.Grid);
        File.WriteAllText(P(scaffold.DocumentFileName), scaffold.DocumentText);
        File.WriteAllText(P(scaffold.CodeFileName), scaffold.CodeText);

        var vm = new CodeEditorDocumentViewModel(new FormDesignerAcceptanceTests.DiskFiles(), new Mock<IEventAggregator>().Object)
        {
            FilePath = P(scaffold.DocumentFileName)
        };
        vm.SetContent(scaffold.DocumentText);
        Assert.That(vm.PlaceControl("ComboBox", 24, 24), Is.Null, "placing the ComboBox");
        var combo = vm.DesignDocument!.Controls.Single();
        vm.Selection.Set(combo);
        var items = vm.PropertyGrid.Rows.Single(r => r.Name == "Items");
        items.ApplyItems(FormItems.Join(Expected)); // what the String Collection Editor's OK does
        Assert.That(await vm.SaveAsync(), Is.True, "the save failed");

        var document = File.ReadAllText(P(scaffold.DocumentFileName));
        Assert.That(document, Does.Contain("<Item>Smith, John</Item>").And.Contain("<Item>A &amp; B</Item>"),
            "precondition: the document stores one <Item> each");
    }

    [Test]
    public async Task WinForms_TheItemsReachTheLiveComboBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Ignore("a WinForms window can only be run on Windows");
        }

        await DesignAsync(FormTarget.WinForms);
        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { P("ItemsForm.bas"), "--target=csharp" }, _dir, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused the designer's .bas.\n{stdout}\n{stderr}");

        var app = Path.Combine(_dir, "app");
        Directory.CreateDirectory(app);
        File.Copy(P("ItemsForm.cs"), Path.Combine(app, "ItemsForm.cs"));
        File.WriteAllText(Path.Combine(app, "app.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net8.0-windows</TargetFramework>
                <UseWindowsForms>true</UseWindowsForms>
                <Nullable>disable</Nullable>
                <AssemblyName>ItemsApp</AssemblyName>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(app, "Driver.cs"), """
            using System;
            using System.Windows.Forms;
            using GeneratedCode;

            internal static class Driver
            {
                [STAThread]
                private static void Main()
                {
                    var form = new ItemsForm();
                    form.Show();
                    Application.DoEvents();
                    foreach (Control c in form.Controls)
                    {
                        if (c is ComboBox combo)
                        {
                            Console.WriteLine("COUNT " + combo.Items.Count);
                            foreach (var item in combo.Items) Console.WriteLine("ITEM [" + item + "]");
                        }
                    }
                    form.Close();
                    Console.WriteLine("DONE");
                }
            }
            """);

        var (buildExit, buildOut, buildErr) = CliTestHarness.RunProcess(
            "dotnet", new[] { "build", "-c", "Release", "--nologo" }, app, timeoutMs: 300_000);
        Assert.That(buildExit, Is.Zero, $"the WinForms app did not build.\n{buildOut}\n{buildErr}");

        var exe = Directory.GetFiles(app, "ItemsApp.exe", SearchOption.AllDirectories).Single();
        var (runExit, runOut, runErr) = CliTestHarness.RunProcess(exe, Array.Empty<string>(), app, timeoutMs: 120_000);
        TestContext.WriteLine(runOut);

        Assert.Multiple(() =>
        {
            Assert.That(runExit, Is.Zero, $"the form crashed at run time.\n{runOut}\n{runErr}");
            Assert.That(runOut, Does.Contain("COUNT 3"), "three items in the live ComboBox — a comma never splits one");
            foreach (var item in Expected)
            {
                Assert.That(runOut, Does.Contain("ITEM [" + item + "]"));
            }
            Assert.That(runOut, Does.Contain("DONE"));
        });
    }

    [Test]
    public async Task Web_ThePageHasOneOptionPerItem_AndItsScriptRuns()
    {
        await DesignAsync(FormTarget.Web);
        File.WriteAllText(P("App.blproj"), """
            <BasicLangProject Version="1.0">
              <PropertyGroup>
                <ProjectName>App</ProjectName>
                <OutputType>Exe</OutputType>
                <TargetBackend>JavaScript</TargetBackend>
                <StartupForm>ItemsForm</StartupForm>
              </PropertyGroup>
            </BasicLangProject>
            """);
        File.WriteAllText(P("Main.bas"), "Sub Main()\n    Console.WriteLine(\"App loaded\")\nEnd Sub\n");

        var (exit, stdout, stderr) = CliTestHarness.RunProcess(
            CliTestHarness.CliPath(), new[] { "build", P("App.blproj") }, _dir, timeoutMs: 180_000);
        Assert.That(exit, Is.Zero, $"the real CLI refused the web project.\n{stdout}\n{stderr}");

        var outDir = Path.Combine(_dir, "bin", "Debug", "net8.0");
        var html = File.ReadAllText(Path.Combine(outDir, "ItemsForm.html"));
        var options = Regex.Matches(html, "<option[^>]*>(.*?)</option>").Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

        var ran = FormDesignerAcceptanceTests.RunPageUnderNode(outDir, formName: "ItemsForm");
        if (ran == null)
        {
            Assert.Ignore("node is not on PATH, so the emitted page cannot be executed here");
        }

        Assert.Multiple(() =>
        {
            Assert.That(options, Is.EqualTo(Expected), "one <option> per item, decoded back to the item exactly");
            Assert.That(html, Does.Contain("<option>A &amp; B</option>").And.Contain("<option>x &lt; y</option>"), "escaped in the page");
            Assert.That(ran, Does.Not.Contain("ReferenceError").And.Not.Contain("LOAD ERROR"), "the page's script runs");
            Assert.That(ran, Does.Contain("App loaded"));
        });
    }
}
