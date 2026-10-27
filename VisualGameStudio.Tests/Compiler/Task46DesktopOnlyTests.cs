using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

[TestFixture]
public class Task46DesktopOnlyTests
{
    [Test]
    public void WebToolbox_ListsDesktopOnlyKindsWithBadge()
    {
        var toolbox = new FormToolboxViewModel { Target = FormTarget.Web };
        var dataGrid = toolbox.Items.Single(i => i.Kind == "DataGridView");
        Assert.That(dataGrid.DesktopOnly, Is.True);
        Assert.That(dataGrid.Badge, Is.EqualTo("desktop"));
        Assert.That(toolbox.Items.Single(i => i.Kind == "Button").DesktopOnly, Is.False);
    }

    [Test]
    public void LoadWebForms_RefusesADesktopOnlyKindByCode()
    {
        var document = new FormDocument { Target = FormTarget.Web, Name = "DesktopOnly" };
        document.Controls.Add(new FormControl { Kind = "DataGridView", Id = "grid" });
        var dir = Path.Combine(Path.GetTempPath(), "bl-task46-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "DesktopOnly.blwebform");
        File.WriteAllText(path, FormDocumentWriter.Create(document));

        var warnings = new List<string>();
        var forms = FormDocumentLoader.LoadWebForms(new[] { path }, warnings.Add);

        Assert.That(forms, Is.Empty);
        Assert.That(warnings.Single(), Does.Contain(DesignCodes.DesktopOnlyKind));
        Assert.That(warnings.Single(), Does.Contain("DataGridView"));
        Directory.Delete(dir, recursive: true);
    }

    [Test]
    public void LoadWebForms_ReportsADesktopOnlyKindAsAnError_WhenTheBuildRouteAsksForOne()
    {
        var document = new FormDocument { Target = FormTarget.Web, Name = "DesktopOnly" };
        document.Controls.Add(new FormControl { Kind = "DataGridView", Id = "grid" });
        var dir = Path.Combine(Path.GetTempPath(), "bl-task46-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "DesktopOnly.blwebform");
        File.WriteAllText(path, FormDocumentWriter.Create(document));

        var warnings = new List<string>();
        var errors = new List<string>();
        var forms = FormDocumentLoader.LoadWebForms(new[] { path }, warnings.Add, errors.Add);

        Assert.That(forms, Is.Empty);
        Assert.That(warnings, Is.Empty);
        Assert.That(errors.Single(), Does.Contain(DesignCodes.DesktopOnlyKind));
        Assert.That(errors.Single(), Does.Contain("DataGridView"));
        Directory.Delete(dir, recursive: true);
    }

    /// <summary>
    /// Task 46's route gate: the CLI's project build must FAIL, not warn and skip the page. The
    /// failure happens before compilation, so this needs no toolchain beyond the deployed CLI.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task CliBuild_WithADesktopOnlyControl_FailsWithBL8042()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-task46-cli-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        try
        {
            var document = new FormDocument { Target = FormTarget.Web, Name = "DesktopOnly" };
            document.Controls.Add(new FormControl { Kind = "DataGridView", Id = "grid" });
            File.WriteAllText(Path.Combine(dir, "DesktopOnly.blwebform"), FormDocumentWriter.Create(document));
            File.WriteAllText(Path.Combine(dir, "Main.bas"), "Sub Main()\nEnd Sub\n");
            File.WriteAllText(Path.Combine(dir, "App.blproj"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <BasicLangProject Version="1.0">
                  <PropertyGroup>
                    <ProjectName>App</ProjectName>
                    <OutputType>Exe</OutputType>
                    <TargetBackend>JavaScript</TargetBackend>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="Main.bas" />
                    <Compile Include="DesktopOnly.blwebform" />
                  </ItemGroup>
                </BasicLangProject>
                """);

            var (exit, stdout, stderr) = await CliTestHarness.RunCli(
                dir, "build", Path.Combine(dir, "App.blproj"));

            Assert.Multiple(() =>
            {
                Assert.That(exit, Is.Not.Zero, $"a desktop-only control must fail the build.\n{stdout}\n{stderr}");
                Assert.That(stderr, Does.Contain(DesignCodes.DesktopOnlyKind));
                Assert.That(Directory.GetFiles(dir, "*.html", SearchOption.AllDirectories), Is.Empty,
                    "the build stopped before writing a page");
            });
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
