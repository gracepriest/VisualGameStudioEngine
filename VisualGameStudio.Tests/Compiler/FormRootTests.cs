using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §2.3 — the Form's properties come from <see cref="FormControlCatalog.FormRoot"/>, which is kept
/// OUT of <see cref="FormControlCatalog.All"/>. Its values live in typed FormDocument fields, mapped in
/// ONE place (<see cref="FormRootValues"/>).
/// </summary>
[TestFixture]
public class FormRootTests
{
    [Test]
    public void FormRoot_IsNotInAll_AndFindCannotReachIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormControlCatalog.All, Has.No.Member(FormControlCatalog.FormRoot),
                "~10 consumers enumerate All and would mistake the Form for a control (spec §2.3)");
            Assert.That(FormControlCatalog.Find(FormControlCatalog.FormRoot.Kind), Is.Null,
                "the reader's `Find(elementName) != null` is its 'is this a control' test");
        });
    }

    [Test]
    public void EveryFormRootRow_HasStorage()
    {
        var empty = new FormDocument { Target = FormTarget.WinForms, Name = "F" };

        foreach (var row in FormControlCatalog.FormRoot.Properties)
        {
            Assert.DoesNotThrow(() => FormRootValues.Get(empty, row),
                $"form.{row.Name} is a FormRoot row FormRootValues cannot store — map it there");
        }
    }

    [Test]
    public void ClientSize_IsTheTypedWidthAndHeight()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 800, Height = 450 };
        var row = FormControlCatalog.FormRoot.Property("ClientSize")!;

        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.Get(form, row), Is.EqualTo("800, 450"));
            Assert.That(FormRootValues.Set(form, row, "640, 480"), Is.True);
            Assert.That((form.Width, form.Height), Is.EqualTo((640, 480)));
            Assert.That(FormRootValues.Set(form, row, "wide"), Is.False, "an invalid value is refused, never coerced");
            Assert.That((form.Width, form.Height), Is.EqualTo((640, 480)));
        });
    }

    private const string WebWithText = """
        <WebForm Name="F" Version="1" Text="Hello">
          <Layout Kind="Grid" Cols="auto" Rows="auto"/>
          <Controls/>
        </WebForm>
        """;

    /// <summary>⛔ A LIVE data-loss defect before this: a web form's caption edit was never written.</summary>
    [Test]
    public void AWebFormsText_IsReadWrittenAndRoundTrips()
    {
        var file = FormDocumentReader.Read("F.blwebform", WebWithText);

        Assert.That(file.Model.Text, Is.EqualTo("Hello"), "Text is ONE vocabulary on both targets (D2)");
        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WebWithText), "a no-op is byte-identical");

        file.Model.Text = "Bye";

        Assert.That(FormDocumentWriter.Write(file), Does.Contain("Text=\"Bye\""));
    }

    [Test]
    public void ClearingTheText_ToNull_RemovesTheAttribute()
    {
        var file = FormDocumentReader.Read("F.blwebform", WebWithText);

        file.Model.Text = null;

        Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("Text="));
    }

    [TestCase("Hello", "Hello")]
    [TestCase(null, "F")]
    public void ThePageTitle_IsTheText_FallingBackToTheName(string? text, string title)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F", Text = text };

        Assert.That(FormAssetEmitter.Html(form, "app.js"), Does.Contain($"<title>{title}</title>"));
    }

    private const string WithRootBind = """
        <Form Name="F" Version="1" Width="400" Height="300">
          <Bind Event="Load" Handler="F_Load"/>
          <Controls/>
        </Form>
        """;

    [Test]
    public void ARootBind_IsModelled_AndRoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blform", WithRootBind);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Binds.Single().Handler, Is.EqualTo("F_Load"));
            Assert.That(file.Model.UnknownChildren, Is.Empty, "a root <Bind> is modelled, not an unknown child");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WithRootBind));
        });
    }

    [Test]
    public void ARootBindEdit_IsWritten_AndCreateEmitsItBeforeTheControls()
    {
        var file = FormDocumentReader.Read("F.blform", WithRootBind);
        file.Model.Binds[0].Handler = "OnLoad";

        var created = FormDocumentWriter.Create(file.Model);

        Assert.Multiple(() =>
        {
            Assert.That(FormDocumentWriter.Write(file), Does.Contain("Handler=\"OnLoad\""));
            Assert.That(created.IndexOf("<Bind", StringComparison.Ordinal),
                Is.LessThan(created.IndexOf("<Controls", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void AnUnparseableClientSize_IsDegraded_NotUnknown_AndPreserved()
    {
        const string text = """
            <Form Name="F" Version="1" Width="12px" Height="300">
              <Controls/>
            </Form>
            """;
        var file = FormDocumentReader.Read("F.blform", text);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("ClientSize"), Does.Contain("12px"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text), "frozen AND preserved");
        });

        file.Model.Text = "Edited";
        Assert.That(FormDocumentWriter.Write(file), Does.Contain("Width=\"12px\""),
            "an unrelated edit must not touch the text the reader could not parse");
    }

    [Test]
    public void TheRootTier_IsCanonForARowOnThisTarget_AndUnknownOtherwise()
    {
        var file = FormDocumentReader.Read("F.blform", WithRootBind);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("Text"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(file.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(file.TierOfRoot("Cols"), Is.EqualTo(PropertyTier.Unknown), "a web-only row on a .blform");
        });
    }

    [Test]
    public void TheRegionWriter_EmitsTheRootRowsInCatalogOrder_AsMeStatements()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Text = "Hi", Width = 400, Height = 300 };

        var text = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform").Text;

        Assert.That(text.IndexOf("Me.Text = \"Hi\"", StringComparison.Ordinal),
            Is.GreaterThan(0).And.LessThan(text.IndexOf("Me.ClientSize = New Size(400, 300)", StringComparison.Ordinal)));
    }

    /// <summary>⚠ RE-CHECK IN SLICE 5: form events are wired then, and this warning is REPLACED by emission.</summary>
    [Test]
    public void ARootBind_IsWarned_NotEmitted_UntilFormEventsExist()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        form.Binds.Add(new FormBind { Event = "Load", Handler = "F_Load" });

        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False);
            Assert.That(result.Text, Does.Not.Contain("AddHandler Me.Load"));
            Assert.That(result.Diagnostics.Single(d => d.Code == DesignCodes.BindNotOnTarget).Message,
                Does.Contain("'form'").And.Contain("F_Load"));
        });
    }
}
