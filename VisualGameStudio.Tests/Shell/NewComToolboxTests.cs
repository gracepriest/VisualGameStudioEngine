using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Designer;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Installed NewCOM controls reach the designer: manifest → scan → toolbox entry → drop → the
/// <c>.blform</c> carries the clsid → the code-behind constructs <c>NewComControlHost("&lt;clsid&gt;")</c>.
///
/// <para>⛔ Before this, <c>NewComCatalogScanner.Scan</c> had no caller at all, and had it run, every
/// manifest became the same catalog kind (so only the first registered) with its clsid thrown away.
/// These tests drive the real view-model command the canvas binds and the real startup line, not the
/// scanner alone — a scanner nobody calls passes every test that only constructs it.</para>
/// </summary>
[TestFixture]
public class NewComToolboxTests
{
    private const string Dir = "/proj/";
    private const string WidgetClsid = "87654321-4321-4321-fedc-ba9876543210";
    private const string GaugeClsid = "11111111-2222-3333-4444-555555555555";

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "bl-newcom-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        NewComCatalogScanner.Scan(Array.Empty<string>());   // the snapshot is static: leave it empty for the next fixture
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Manifest(string folder, string json)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "newcom.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string Control(string name, string clsid, string extra = "") =>
        $$"""{ "name": "{{name}}", "kind": "control", "clsid": "{{clsid}}", "library": "x.dll", "factory": "X_Create"{{extra}} }""";

    // ------------------------------------------------------------------ manifest reading

    [Test]
    public void AControlManifest_IsRead_WithItsClsidAndSize()
    {
        var path = Manifest("widget", Control("MyWidget", WidgetClsid.ToUpperInvariant(), """, "defaultSize": [160, 48]"""));

        var manifest = NewComCatalogScanner.TryRead(path);

        Assert.That(manifest, Is.EqualTo(new NewComControlManifest("MyWidget", WidgetClsid, 160, 48, path)),
            "clsid normalised to lower-case D form; defaultSize honoured");
    }

    [Test]
    public void WithoutDefaultSize_TheHostRowsDefaultsApply()
    {
        var manifest = NewComCatalogScanner.TryRead(Manifest("widget", Control("MyWidget", WidgetClsid)))!;
        var row = FormControlCatalog.Find(NewComCatalogScanner.HostKind)!;

        Assert.That((manifest.DefaultWidth, manifest.DefaultHeight), Is.EqualTo((row.DefaultWidth, row.DefaultHeight)));
    }

    [TestCase("""{ "name": "Lib", "clsid": "87654321-4321-4321-fedc-ba9876543210" }""", TestName = "NotAControl_NoKind")]
    [TestCase("""{ "name": "Lib", "kind": "component", "clsid": "87654321-4321-4321-fedc-ba9876543210" }""", TestName = "NotAControl_OtherKind")]
    [TestCase("""{ "kind": "control", "clsid": "87654321-4321-4321-fedc-ba9876543210" }""", TestName = "NoName")]
    [TestCase("""{ "name": "W", "kind": "control" }""", TestName = "NoClsid")]
    [TestCase("""{ "name": "W", "kind": "control", "clsid": "x\") : Kill(\"y" }""", TestName = "ClsidThatIsNotAGuid_NeverReachesGeneratedCode")]
    [TestCase("""{ not json""", TestName = "NotJson")]
    [TestCase("""[1, 2]""", TestName = "NotAnObject")]
    public void AnUnusableManifest_IsSkipped(string json)
    {
        Assert.That(NewComCatalogScanner.TryRead(Manifest("bad", json)), Is.Null);
    }

    [Test]
    public void Scan_FindsEveryControl_AndTheFirstInstallOfAClsidWins()
    {
        // Two DIFFERENT controls: the old scanner gave both the same catalog kind and kept only one.
        var projectRoot = Path.Combine(_root, "project");
        var userRoot = Path.Combine(_root, "user");
        Manifest(Path.Combine("project", "widget"), Control("ProjectWidget", WidgetClsid));
        Manifest(Path.Combine("user", "widget"), Control("UserWidget", WidgetClsid));
        Manifest(Path.Combine("user", "gauge"), Control("Gauge", GaugeClsid));
        Manifest(Path.Combine("user", "lib"), """{ "name": "Lib", "clsid": "99999999-2222-3333-4444-555555555555" }""");

        var found = NewComCatalogScanner.Scan(new[] { projectRoot, userRoot, Path.Combine(_root, "absent") });

        Assert.Multiple(() =>
        {
            Assert.That(found.Select(m => m.Name), Is.EqualTo(new[] { "ProjectWidget", "Gauge" }),
                "one entry per clsid, search-path order; the non-control manifest is not offered");
            Assert.That(NewComCatalogScanner.Installed, Is.SameAs(found), "the scan replaces the snapshot the toolbox reads");
        });
    }

    // ------------------------------------------------------------------ toolbox

    [Test]
    public void TheToolbox_OffersEachInstalledControl_ByName_InItsOwnGroupLast()
    {
        Manifest("widget", Control("MyWidget", WidgetClsid));
        Manifest("gauge", Control("Gauge", GaugeClsid));
        NewComCatalogScanner.Scan(new[] { _root });

        var items = new FormToolboxViewModel { Target = FormTarget.WinForms }.Items;
        var newCom = items.Where(i => i.NewCom != null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(newCom.Select(i => i.Label), Is.EqualTo(new[] { "Gauge", "MyWidget" }));
            Assert.That(newCom.Select(i => i.Kind), Has.All.EqualTo(NewComCatalogScanner.HostKind));
            Assert.That(newCom.Select(i => i.Category), Has.All.EqualTo("NewCOM Controls"));
            Assert.That(newCom.Select(i => i.StartsCategory), Is.EqualTo(new[] { true, false }), "one group header");
            Assert.That(items.Skip(items.Count - 2), Is.EqualTo(newCom), "the group comes last");
            Assert.That(items.Where(i => i.Kind == NewComCatalogScanner.HostKind && i.NewCom == null), Is.Empty,
                "the bare host row is never offered: placed bare it does not compile");
        });
    }

    [Test]
    public void WithNothingInstalled_TheToolboxOffersNoNewComEntry()
    {
        NewComCatalogScanner.Scan(new[] { _root });

        var items = new FormToolboxViewModel { Target = FormTarget.WinForms }.Items;

        Assert.That(items.Where(i => i.Kind == NewComCatalogScanner.HostKind), Is.Empty);
    }

    [Test]
    public void OnAWebPage_AnInstalledControl_IsBadgedDesktopOnly_LikeEveryOtherDesktopRow()
    {
        Manifest("widget", Control("MyWidget", WidgetClsid));
        NewComCatalogScanner.Scan(new[] { _root });
        var host = FormControlCatalog.Find(NewComCatalogScanner.HostKind)!;

        var item = new FormToolboxViewModel { Target = FormTarget.Web }.Items.Single(i => i.NewCom != null);

        Assert.That(item.DesktopOnly, Is.EqualTo(!host.SupportsTarget(FormTarget.Web)));
    }

    [Test]
    public void TheToolboxRow_ShowsTheLabel_NotTheKind()
    {
        // Every NewCOM entry has the same Kind, so a row bound to Kind would list N identical names.
        var axaml = File.ReadAllText(RepoFile("VisualGameStudio.Shell", "Views", "Documents", "CodeEditorDocumentView.axaml"));

        Assert.Multiple(() =>
        {
            Assert.That(axaml, Does.Contain("Text=\"{Binding Label}\""));
            Assert.That(axaml, Does.Not.Contain("Text=\"{Binding Kind}\""));
        });
    }

    // ------------------------------------------------------------------ the drop, through the bound command

    private static (CodeEditorDocumentViewModel Vm, Dictionary<string, string> Files) Open()
    {
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.WinForms);
        var contents = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Dir + scaffold.DocumentFileName] = scaffold.DocumentText,
            [Dir + scaffold.CodeFileName] = scaffold.CodeText
        };

        var files = new Mock<IFileService>();
        files.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) => Task.FromResult(contents[p]));
        files.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
            .Returns((string p) => Task.FromResult(contents.ContainsKey(p)));
        files.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string text, CancellationToken _) =>
            {
                contents[p] = text;
                return Task.CompletedTask;
            });

        var vm = new CodeEditorDocumentViewModel(files.Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = Dir + scaffold.DocumentFileName
        };
        vm.Text = contents[vm.FilePath];
        return (vm, contents);
    }

    [Test]
    public async Task DroppingAnInstalledControl_WritesItsClsid_AndTheCodeBehindConstructsTheHost()
    {
        Manifest("widget", Control("MyWidget", WidgetClsid, """, "defaultSize": [160, 48]"""));
        NewComCatalogScanner.Scan(new[] { _root });
        var item = new FormToolboxViewModel { Target = FormTarget.WinForms }.Items.Single(i => i.NewCom != null);
        var (vm, files) = Open();

        // The request exactly as FormCanvasControl.OnDrop builds it from the toolbox drag.
        vm.PlaceDroppedControlCommand.Execute(new FormControlDropRequest(item.Kind, 40, 40, item.NewCom!.Clsid));
        Assert.That(await vm.SaveAsync(), Is.True);

        var control = vm.DesignDocument!.AllControls().Single();
        var code = files[Dir + "LoginForm.bas"];
        Assert.Multiple(() =>
        {
            Assert.That(vm.Text, Does.Contain($"Clsid=\"{WidgetClsid}\""), "the .blform persists which control");
            Assert.That(control.Geometry, Is.TypeOf<PixelGeometry>()
                .With.Property(nameof(PixelGeometry.Width)).EqualTo(160)
                .And.Property(nameof(PixelGeometry.Height)).EqualTo(48), "the manifest's default size");
            Assert.That(code, Does.Contain($"New NewCom.Runtime.NewComControlHost(\"{WidgetClsid}\")"));
        });
    }

    [Test]
    public void TheBareHostKind_IsRefused_WithAReason()
    {
        var (vm, _) = Open();
        var before = vm.Text;

        var refusal = vm.PlaceControl(NewComCatalogScanner.HostKind, 40, 40);

        Assert.Multiple(() =>
        {
            Assert.That(refusal, Does.Contain("NewCOM Controls"));
            Assert.That(vm.Text, Is.EqualTo(before));
        });
    }

    [Test]
    public void AClsidNoLongerInstalled_IsRefused_WithAReason()
    {
        NewComCatalogScanner.Scan(new[] { _root });
        var (vm, _) = Open();

        var refusal = vm.PlaceControl(NewComCatalogScanner.HostKind, 40, 40, GaugeClsid);

        Assert.That(refusal, Does.Contain(GaugeClsid));
    }

    [Test]
    public void AClsidOnAnotherKind_IsRefused()
    {
        Manifest("widget", Control("MyWidget", WidgetClsid));
        NewComCatalogScanner.Scan(new[] { _root });
        var (vm, _) = Open();

        Assert.That(vm.PlaceControl("Button", 40, 40, WidgetClsid), Does.Contain("not a NewCOM control"));
    }

    // ------------------------------------------------------------------ who calls the scanner

    [Test]
    public void TheIdeStartup_ScansForInstalledControls()
    {
        // ⛔ The shipping caller. The scanner existed, complete and unreachable, until this line did.
        var app = File.ReadAllText(RepoFile("VisualGameStudio.Shell", "App.axaml.cs"));

        Assert.That(app, Does.Contain("NewComCatalogScanner.Scan();"));
    }

    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
        }

        Assert.Ignore($"{Path.Combine(parts)} not found above the test base directory.");
        return null!;
    }
}
