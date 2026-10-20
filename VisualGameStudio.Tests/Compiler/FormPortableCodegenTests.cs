using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 30: the portable code-behind is the WinForms walk without the desktop-only lines, selected
/// by the marker's recorded style rather than by sniffing the document target.
/// </summary>
[TestFixture]
public class FormPortableCodegenTests
{
    private static FormDocument FormWithControls(FormTarget target, string buttonEvent = "Click")
    {
        var form = new FormDocument
        {
            Target = target,
            Name = "LoginForm",
            Width = 800,
            Height = 450,
            Text = "Sign in"
        };

        var button = new FormControl
        {
            Kind = "Button",
            Id = "btnLogin",
            TabIndex = 0,
            Geometry = new PixelGeometry { X = 20, Y = 30, Width = 100, Height = 30 }
        };
        button.Properties["Text"] = "Sign in";
        button.Binds.Add(new FormBind { Event = buttonEvent, Handler = "btnLogin_Click" });

        var panel = new FormControl
        {
            Kind = "Panel",
            Id = "pnlBody",
            TabIndex = 1,
            Geometry = new PixelGeometry { X = 10, Y = 20, Width = 200, Height = 100 }
        };
        panel.Children.Add(button);
        form.Controls.Add(panel);

        var timer = new FormControl { Kind = "Timer", Id = "tmrRefresh" };
        timer.Properties["Interval"] = "250";
        timer.Binds.Add(new FormBind { Event = "Tick", Handler = "tmrRefresh_Tick" });
        form.Components.Add(timer);

        // A reference row: mobile/portable must not emit it, and it must be tagged as such so a
        // conversion cannot accidentally retain it.
        form.Properties["AcceptButton"] = "btnLogin";

        return form;
    }

    private static FormScaffold PortableScaffold() =>
        FormScaffolder.Create("LoginForm", FormTarget.Web, portable: true);

    private static IEnumerable<TestCaseData> EveryWinFormsKind() =>
        FormControlCatalog.All
            .Where(c => c.SupportsTarget(FormTarget.WinForms))
            .Select(c => new TestCaseData(c.Kind).SetName("{m}(" + c.Kind + ")"));

    private static string InitText(string source)
    {
        var init = RegionMarkers.Find(RegionMarkers.Scan(source), RegionMarkers.Init);
        Assert.That(init, Is.Not.Null, "the fixture must carry an init region");
        return source.Substring(init!.StartOffset, init.EndOffset - init.StartOffset);
    }

    [TestCaseSource(nameof(EveryWinFormsKind))]
    public void PortableSubset_ForEveryCanonicalKind_IsWinFormsMinusTheTaggedLines(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var form = new FormDocument
        {
            Target = FormTarget.Web,
            Name = "SweepForm",
            Width = 800,
            Height = 450,
            Text = "Sweep"
        };
        var control = FormCatalogShapes.Canonical(form, definition, "ctl");

        foreach (var property in definition.Properties.Where(p => p.Default != null))
        {
            control.Properties[property.Name] = property.Default!;
        }

        var diagnostics = new List<DesignDiagnostic>();
        var winForms = RegionWriter.GenerateInitLines(
            form, FormStyle.WinForms, "", "SweepForm.bas", diagnostics);
        var portable = RegionWriter.GenerateInitLines(
            form, FormStyle.Portable, "", "SweepForm.bas", diagnostics);

        var expected = winForms
            .Where(l => l.Kind is not (InitLineKind.Geometry or InitLineKind.Property or
                InitLineKind.RootProperty or InitLineKind.Reference))
            .Select(l => l.Text);
        var actual = portable
            .Where(l => l.Kind is InitLineKind.Other or InitLineKind.ComponentProperty)
            .Select(l => l.Text);

        Assert.That(actual, Is.EqualTo(expected),
            $"the portable init for '{kind}' must be the WinForms walk minus its tagged desktop-only lines");
    }

    [Test]
    public void PortableScaffold_RecordsTheStyleAndIsAlreadyCanon()
    {
        var scaffold = PortableScaffold();
        var regions = RegionMarkers.Scan(scaffold.CodeText);

        Assert.Multiple(() =>
        {
            Assert.That(regions, Has.All.Property(nameof(FormRegion.Style)).EqualTo("portable"));
            Assert.That(regions, Has.All.Property(nameof(FormRegion.State)).EqualTo(RegionState.Canon));
            Assert.That(scaffold.CodeText, Does.Contain("Using System.Windows.Forms"));
            Assert.That(scaffold.CodeText, Does.Contain("Inherits Form"));
            Assert.That(scaffold.CodeText, Does.Not.Contain("Dim doc"));
            Assert.That(InitText(scaffold.CodeText), Does.Contain("Private Sub InitializeComponent()"));
        });
    }

    [Test]
    public void PortableScaffold_IsTheOptInOnly_AndRefusesAWinFormsTarget()
    {
        Assert.Throws<ArgumentException>(() =>
            FormScaffolder.Create("LoginForm", FormTarget.WinForms, portable: true));
    }

    [Test]
    public void PortableInit_IsTheWinFormsLinesWithoutGeometryPropertiesRootsOrReferences()
    {
        var diagnostics = new List<DesignDiagnostic>();
        var winForms = RegionWriter.GenerateInitLines(
            FormWithControls(FormTarget.WinForms), FormStyle.WinForms, "", "LoginForm.bas", diagnostics);
        var portable = RegionWriter.GenerateInitLines(
            FormWithControls(FormTarget.Web), FormStyle.Portable, "", "LoginForm.bas", diagnostics);

        var emittedPortable = portable
            .Where(l => l.Kind is InitLineKind.Other or InitLineKind.ComponentProperty)
            .Select(l => l.Text);
        var expected = winForms
            .Where(l => l.Kind is not (InitLineKind.Geometry or InitLineKind.Property or
                InitLineKind.RootProperty or InitLineKind.Reference))
            .Select(l => l.Text);

        Assert.Multiple(() =>
        {
            Assert.That(emittedPortable, Is.EqualTo(expected),
                "portable is the WinForms walk minus the tagged desktop-only lines");
            Assert.That(portable.Any(l => l.Kind == InitLineKind.RootProperty), Is.True);
            Assert.That(winForms.Any(l => l.Kind == InitLineKind.Geometry), Is.True);
            Assert.That(winForms.Any(l => l.Kind == InitLineKind.Property), Is.True);
            Assert.That(winForms.Any(l => l.Kind == InitLineKind.RootProperty), Is.True);
            Assert.That(winForms.Any(l => l.Kind == InitLineKind.Reference), Is.True);
            Assert.That(portable.Any(l => l.Kind == InitLineKind.ComponentProperty), Is.True,
                "tray component properties are the deliberate exception");
        });
    }

    [Test]
    public void PortableWriter_UsesNewWinFormsTypesAndAddHandler_AndDropsDesktopOnlyLines()
    {
        var scaffold = PortableScaffold();
        var result = RegionWriter.Write(
            scaffold.CodeFileName, scaffold.CodeText, FormWithControls(FormTarget.Web, "click"),
            scaffold.DocumentFileName);

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False,
                string.Join("; ", result.Diagnostics.Select(d => d.Message)));
            Assert.That(result.Text, Does.Not.Contain("Dim doc"));
            Assert.That(result.Text, Does.Contain("Private pnlBody As Panel"));
            Assert.That(result.Text, Does.Contain("Private tmrRefresh As System.Windows.Forms.Timer"));
            Assert.That(result.Text, Does.Contain("pnlBody = New Panel()"));
            Assert.That(result.Text, Does.Contain("btnLogin = New Button()"));
            Assert.That(result.Text, Does.Contain("btnLogin.Name = \"btnLogin\""));
            Assert.That(result.Text, Does.Contain("AddHandler btnLogin.Click, AddressOf btnLogin_Click"));
            Assert.That(result.Text, Does.Contain("pnlBody.Controls.Add(btnLogin)"));
            Assert.That(result.Text, Does.Contain("Me.Controls.Add(pnlBody)"));
            Assert.That(result.Text, Does.Contain("tmrRefresh = New System.Windows.Forms.Timer()"));
            Assert.That(result.Text, Does.Contain("tmrRefresh.Interval = 250"));
            Assert.That(result.Text, Does.Contain("AddHandler tmrRefresh.Tick, AddressOf tmrRefresh_Tick"));

            Assert.That(result.Text, Does.Not.Contain("doc.getElementById"));
            Assert.That(result.Text, Does.Not.Contain("btnLogin.Location"));
            Assert.That(result.Text, Does.Not.Contain("btnLogin.Text ="));
            Assert.That(result.Text, Does.Not.Contain("Me.ClientSize"));
            Assert.That(result.Text, Does.Not.Contain("Me.AcceptButton"));
            Assert.That(result.Text, Does.Not.Contain("addEventListener"));
        });
    }

    [Test]
    public void WinFormsWriter_EmitsNameImmediatelyAfterNew_ThenGeometry()
    {
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.WinForms);
        var result = RegionWriter.Write(
            scaffold.CodeFileName, scaffold.CodeText, FormWithControls(FormTarget.WinForms),
            scaffold.DocumentFileName);

        Assert.That(result.Refused, Is.False,
            string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        var construct = result.Text.IndexOf("btnLogin = New Button()", StringComparison.Ordinal);
        var name = result.Text.IndexOf("btnLogin.Name = \"btnLogin\"", StringComparison.Ordinal);
        var location = result.Text.IndexOf("btnLogin.Location", StringComparison.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(construct, Is.GreaterThanOrEqualTo(0));
            Assert.That(name, Is.GreaterThan(construct), "Name follows New on WinForms");
            Assert.That(location, Is.GreaterThan(name), "Name precedes the desktop geometry");
            Assert.That(result.Text, Does.Contain("btnLogin.Text = \"Sign in\""),
                "the desktop still emits control properties");
        });
    }

    [Test]
    public void DomWriter_KeepsItsElementHandleAndDoesNotEmitNameOrGeometry()
    {
        var scaffold = FormScaffolder.Create("LoginForm", FormTarget.Web);
        var result = RegionWriter.Write(
            scaffold.CodeFileName, scaffold.CodeText, FormWithControls(FormTarget.Web),
            scaffold.DocumentFileName);

        Assert.That(result.Refused, Is.False,
            string.Join("; ", result.Diagnostics.Select(d => d.Message)));

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Contain("btnLogin = doc.getElementById(\"btnLogin\")"));
            Assert.That(result.Text, Does.Contain("btnLogin.addEventListener(\"click\", AddressOf btnLogin_Click)"));
            Assert.That(result.Text, Does.Not.Contain("btnLogin.Name"));
            Assert.That(result.Text, Does.Not.Contain("btnLogin.Location"));
            Assert.That(result.Text, Does.Contain("Dim doc As Document = ::document"));
        });
    }

    [Test]
    public void PortableHandler_UsesWinFormsSignatureAndLandsBelowTheInitRegion()
    {
        var scaffold = PortableScaffold();
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        form.Controls.Add(button);

        var plan = FormHandlers.PlanDefault(form, button, scaffold.CodeText);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created));
            Assert.That(plan.CodeText, Does.Contain("Private Sub btnLogin_Click(sender As Object, e As EventArgs)"));
            Assert.That(plan.CodeText, Does.Not.Contain("e As DomEvent"));
        });

        var init = RegionMarkers.Find(RegionMarkers.Scan(plan.CodeText), RegionMarkers.Init)!;
        var handler = plan.CodeText.IndexOf("Private Sub btnLogin_Click", StringComparison.Ordinal);
        Assert.That(handler, Is.GreaterThan(init.EndOffset),
            "portable uses AddHandler and the WinForms below-init placement");
    }

    [Test]
    public void PortablePlanBind_AcceptsADomEventSpellingButStillWritesTheWinFormsSignature()
    {
        var scaffold = PortableScaffold();
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        form.Controls.Add(button);

        var plan = FormHandlers.PlanBind(
            form, button, new FormBind { Event = "click", Handler = "btnLogin_Click" }, scaffold.CodeText);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created));
            Assert.That(plan.CodeText, Does.Contain("Private Sub btnLogin_Click(sender As Object, e As EventArgs)"));
        });
    }

    [Test]
    public void PortableWriter_DoesNotApplyTheDomHandlerOrderingRefusal()
    {
        var scaffold = PortableScaffold();
        var form = new FormDocument { Target = FormTarget.Web, Name = "LoginForm" };
        var button = new FormControl { Kind = "Button", Id = "btnLogin" };
        button.Binds.Add(new FormBind { Event = "click", Handler = "btnLogin_Click" });
        form.Controls.Add(button);

        var plan = FormHandlers.PlanDefault(form, button, scaffold.CodeText);
        var result = RegionWriter.Write(
            "LoginForm.bas", plan.CodeText, form, "LoginForm.blwebform");

        Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.HandlerDeclaredAfterWiring), Is.False,
            "BL8013 is a DOM-only refusal; portable's AddHandler tolerates the later declaration");
        Assert.That(result.Refused, Is.False,
            string.Join("; ", result.Diagnostics.Select(d => d.Message)));
    }
}
