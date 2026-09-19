using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 25 — what the region writer, the handler stub and the markup emitter do with a component.
///
/// <para>The shape is the brief's: declare field → construct → set properties → wire handlers,
/// with the same trap list as a control. The differences are all subtractions — no geometry, no
/// <c>Controls.Add</c>, no <c>getElementById</c> — plus one addition on the web: a Timer is a
/// <c>setInterval</c> handle, so its construct line is a template from the catalog and its handler
/// is parameterless. <c>WinFormsCatalogSweepTests</c> puts every row through csc; this fixture pins
/// the SHAPE, which csc cannot see (it would accept <c>Controls.Add</c> of a Button just as well).</para>
/// </summary>
[TestFixture]
public class FormComponentEmissionTests
{
    private static FormDocument WinFormsWith(params FormControl[] components)
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300, Text = "F" };
        doc.Controls.Add(new FormControl
        {
            Kind = "Button", Id = "btn", Geometry = new PixelGeometry { X = 8, Y = 8, Width = 75, Height = 23 }
        });
        doc.Components.AddRange(components);
        return doc;
    }

    private static FormDocument WebWith(params FormControl[] components)
    {
        var doc = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        doc.Controls.Add(new FormControl { Kind = "Button", Id = "btn", Geometry = new GridGeometry() });
        doc.Components.AddRange(components);
        return doc;
    }

    private static string Emit(FormDocument doc)
    {
        var scaffold = FormScaffolder.Create("F", doc.Target).CodeText;
        var written = RegionWriter.Write("F.bas", scaffold, doc, "F" + doc.FileExtension);
        Assert.That(written.Refused, Is.False, string.Join("; ", written.Diagnostics.Select(d => d.Format())));
        return written.Text;
    }

    private static FormControl Timer(string id, string? handler = null, params (string Name, string Value)[] properties)
    {
        var tmr = new FormControl { Kind = "Timer", Id = id };
        foreach (var (name, value) in properties)
        {
            tmr.Properties[name] = value;
        }

        if (handler != null)
        {
            tmr.Binds.Add(new FormBind { Event = "Tick", Handler = handler });
        }

        return tmr;
    }

    // ==================================================================
    // WinForms
    // ==================================================================

    [Test]
    public void WinForms_AComponent_IsDeclaredConstructedSetAndWired_BeforeTheControls_AndNeverAdded()
    {
        var code = Emit(WinFormsWith(Timer("tmr", "tmr_Tick", ("Interval", "500"), ("Enabled", "true"))));

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private tmr As System.Windows.Forms.Timer"), "qualified — spec M10/M13");
            Assert.That(code, Does.Contain("tmr = New System.Windows.Forms.Timer()"));
            Assert.That(code, Does.Contain("tmr.Interval = 500"));
            Assert.That(code, Does.Contain("tmr.Enabled = True"));
            Assert.That(code, Does.Contain("AddHandler tmr.Tick, AddressOf tmr_Tick"));
            Assert.That(code, Does.Not.Contain("Controls.Add(tmr)"), "a component is not a control");
            Assert.That(code, Does.Not.Contain("tmr.Location").And.Not.Contain("tmr.TabIndex").And.Not.Contain("tmr.Size"));
            Assert.That(code.IndexOf("Private tmr As", StringComparison.Ordinal),
                Is.LessThan(code.IndexOf("Private btn As", StringComparison.Ordinal)), "VS's order: components first");
            Assert.That(code.IndexOf("tmr = New", StringComparison.Ordinal),
                Is.LessThan(code.IndexOf("btn = New", StringComparison.Ordinal)));
            Assert.That(code, Does.Not.Contain("With "), "never With");
            Assert.That(code, Does.Not.Contain("Handles "), "never Handles");
        });
    }

    [Test]
    public void WinForms_EveryComponentKind_ConstructsWithItsQualifiedType()
    {
        // A BackgroundWorker lives in System.ComponentModel, which the scaffold never imports;
        // measured CS0246 with a bare name (spec M11).
        var code = Emit(WinFormsWith(
            new FormControl { Kind = "BackgroundWorker", Id = "bw" },
            new FormControl { Kind = "ErrorProvider", Id = "err" },
            new FormControl { Kind = "ToolTip", Id = "tip" }));

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private bw As System.ComponentModel.BackgroundWorker"));
            Assert.That(code, Does.Contain("bw = New System.ComponentModel.BackgroundWorker()"));
            Assert.That(code, Does.Contain("err = New System.Windows.Forms.ErrorProvider()"));
            Assert.That(code, Does.Contain("tip = New System.Windows.Forms.ToolTip()"));
        });
    }

    [Test]
    public void WinForms_AComponentsEnumProperty_IsQualifiedByTheCatalog_LikeAControls()
    {
        var err = new FormControl { Kind = "ErrorProvider", Id = "err" };
        err.Properties["BlinkStyle"] = "NeverBlink";

        Assert.That(Emit(WinFormsWith(err)), Does.Contain("err.BlinkStyle = ErrorBlinkStyle.NeverBlink"));
    }

    [Test]
    public void TheHandlerStub_TakesTheRowsEventArgsType()
    {
        var doc = WinFormsWith(new FormControl { Kind = "BackgroundWorker", Id = "bw" });

        var plan = FormHandlers.PlanDefault(doc, doc.Components[0], FormScaffolder.Create("F", FormTarget.WinForms).CodeText);

        Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created), plan.Refusal);
        Assert.That(plan.CodeText,
            Does.Contain("Private Sub bw_DoWork(sender As Object, e As System.ComponentModel.DoWorkEventArgs)"),
            "the typed stub is the useful one — e.Argument is unreachable from EventArgs");
    }

    [Test]
    public void TheHandlerStub_StillTakesEventArgs_ForAComponentWhoseRowSaysNothing()
    {
        var doc = WinFormsWith(Timer("tmr"));

        var plan = FormHandlers.PlanDefault(doc, doc.Components[0], FormScaffolder.Create("F", FormTarget.WinForms).CodeText);

        Assert.That(plan.CodeText, Does.Contain("Private Sub tmr_Tick(sender As Object, e As EventArgs)"));
    }

    // ==================================================================
    // Web
    // ==================================================================

    [Test]
    public void Web_ATimerWithATickHandler_IsASetIntervalHandle_OverTheTypedWindow()
    {
        var tmr = Timer("tmr", null, ("Interval", "250"));
        tmr.Binds.Add(new FormBind { Event = "tick", Handler = "tmr_Tick" });

        var code = Emit(WebWith(tmr));

        Assert.Multiple(() =>
        {
            Assert.That(code, Does.Contain("Private tmr As Integer"), "the handle");
            Assert.That(code, Does.Contain("Dim w As Window = ::window"), "the typed Window, declared once");
            Assert.That(code, Does.Contain("tmr = w.setInterval(AddressOf tmr_Tick, 250)"));
            Assert.That(code, Does.Not.Contain("getElementById(\"tmr\")"), "a Timer is not an element");
            Assert.That(code, Does.Not.Contain("tmr.addEventListener"), "an Integer has no addEventListener");
            Assert.That(code, Does.Contain("btn = doc.getElementById(\"btn\")"), "the controls are untouched");
        });
    }

    [Test]
    public void Web_ATimerWithNoHandler_EmitsOnlyItsField()
    {
        // The field, unconditionally (a user's clearInterval compiles before the handler is wired);
        // no construct line, because setInterval needs a callback. The `Dim w As Window` local IS
        // still declared — it is keyed on "a script component exists", not on "one is wired".
        var code = Emit(WebWith(Timer("tmr")));

        Assert.That(code, Does.Contain("Private tmr As Integer").And.Not.Contain("setInterval"));
    }

    [Test]
    public void Web_TheTemplateUsesTheCatalogDefault_WhenThePropertyIsUnset()
    {
        var tmr = Timer("tmr");
        tmr.Binds.Add(new FormBind { Event = "tick", Handler = "tmr_Tick" });

        Assert.That(Emit(WebWith(tmr)), Does.Contain("w.setInterval(AddressOf tmr_Tick, 100)"));
    }

    [Test]
    public void Web_AFormWithNoScriptComponent_DeclaresNoWindow()
    {
        Assert.That(Emit(WebWith()), Does.Not.Contain("As Window"), "byte-for-byte what today's region is");
    }

    [Test]
    public void Web_TheTimerStub_IsParameterless_BecauseSetIntervalTakesAnAction()
    {
        var doc = WebWith(Timer("tmr"));

        var plan = FormHandlers.PlanDefault(doc, doc.Components[0], FormScaffolder.Create("F", FormTarget.Web).CodeText);

        Assert.That(plan.Outcome, Is.EqualTo(HandlerOutcome.Created), plan.Refusal);
        Assert.That(plan.CodeText, Does.Contain("Private Sub tmr_Tick()"));
        Assert.That(plan.CodeText, Does.Not.Contain("tmr_Tick(e As DomEvent)"),
            "the typed Window.setInterval refuses Action(Of DomEvent) — spec M7");
    }

    [Test]
    public void AComponentPropertyTheTargetLacks_IsSkippedAndWarned_LikeAControls()
    {
        var doc = WebWith(Timer("tmr", null, ("Enabled", "true")));

        var written = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.Web).CodeText, doc, "F.blwebform");

        Assert.That(written.Diagnostics.Select(d => d.Code), Does.Contain(DesignCodes.PropertyNotOnTarget));
    }

    [Test]
    public void Web_AHandlerDeclaredBelowTheRegion_IsRefused_ForAComponentToo()
    {
        // BL8013 runs over components as well; a parameterless callback is not bitten by the
        // erasure (spec M8), so the check is stricter than the compiler there — the safe side.
        var tmr = Timer("tmr");
        tmr.Binds.Add(new FormBind { Event = "tick", Handler = "tmr_Tick" });
        var doc = WebWith(tmr);

        var scaffold = FormScaffolder.Create("F", FormTarget.Web).CodeText;
        var below = scaffold.Replace("End Class", "    Private Sub tmr_Tick()\n    End Sub\nEnd Class");

        var written = RegionWriter.Write("F.bas", below, doc, "F.blwebform");

        Assert.That(written.Refused, Is.True);
        Assert.That(written.Diagnostics.Single().Code, Is.EqualTo(DesignCodes.HandlerDeclaredAfterWiring));
    }

    /// <summary>Spec §2's "no" row for the markup emitter, pinned: a component has no element.</summary>
    [Test]
    public void TheWebPage_CarriesNoMarkupForAComponent()
    {
        var html = FormAssetEmitter.Html(WebWith(Timer("tmr")), "App.js");

        Assert.That(html, Does.Contain("id=\"btn\"").And.Not.Contain("tmr"));
    }
}
