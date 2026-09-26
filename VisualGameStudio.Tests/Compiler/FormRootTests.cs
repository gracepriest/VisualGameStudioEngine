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

    /// <summary>
    /// Every accessor of the one storage map, for every FormRoot row, on BOTH targets. ⚠ Get/Set/
    /// StorageAttributes are a STORAGE map and deliberately target-agnostic (the typed fields exist on
    /// every document); the target filter is <see cref="FormRootValues.RowForAttribute"/>'s, which must
    /// answer a row exactly on the targets it applies to.
    /// </summary>
    [Test]
    public void EveryFormRootRow_HasStorage_OnBothTargets()
    {
        var samples = new Dictionary<FormPropertyType, string>
        {
            [FormPropertyType.String] = "sample",
            [FormPropertyType.Size] = "75, 23",
        };

        Assert.Multiple(() =>
        {
            foreach (var target in new[] { FormTarget.WinForms, FormTarget.Web })
            {
                foreach (var row in FormControlCatalog.FormRoot.Properties)
                {
                    var form = new FormDocument { Target = target, Name = "F" };
                    var where = $"form.{row.Name} on {target}";

                    Assert.That(samples.ContainsKey(row.Type), Is.True, $"{where}: add a sample for {row.Type}");
                    Assert.That(FormRootValues.Get(form, row), Is.Null, $"{where}: an empty document carries no value");
                    Assert.That(FormRootValues.Set(form, row, samples[row.Type]), Is.True, $"{where}: Set must store the sample");
                    Assert.That(FormRootValues.Get(form, row), Is.EqualTo(samples[row.Type]), $"{where}: Get must read back what Set stored");
                    Assert.That(FormRootValues.Set(form, row, null), Is.True, $"{where}: null = absent");
                    Assert.That(FormRootValues.Get(form, row), Is.Null, $"{where}: cleared");

                    var attributes = FormRootValues.StorageAttributes(row);
                    foreach (var attribute in attributes)
                    {
                        Assert.That(FormRootValues.RowForAttribute(attribute, target),
                            row.AppliesTo(target) ? Is.SameAs(row) : Is.Null,
                            $"{where}: attribute '{attribute}' belongs to the row exactly where the row applies");
                    }
                }
            }
        });
    }

    /// <summary>
    /// ⛔ A row this map does not know THROWS from every accessor — never a guess. A guessed storage
    /// attribute makes the reader call it "known" (so not an unknown attribute) while nothing models it,
    /// and the next save deletes it.
    /// </summary>
    [Test]
    public void AnUnmappedRow_Throws_FromEveryAccessor()
    {
        var bogus = new FormPropertyDef("FormBorderStyle", FormPropertyType.String);
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F" };

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => FormRootValues.Get(form, bogus));
            Assert.Throws<InvalidOperationException>(() => FormRootValues.Set(form, bogus, "x"));
            Assert.Throws<InvalidOperationException>(() => FormRootValues.StorageAttributes(bogus));
        });
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
    [TestCase("a<b&c", "a&lt;b&amp;c")]
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

    private const string WebWithRootBind = """
        <WebForm Name="F" Version="1">
          <Bind Event="load" Handler="F_Load"/>
          <Layout Kind="Grid" Cols="auto" Rows="auto"/>
          <Controls/>
        </WebForm>
        """;

    [Test]
    public void AWebRootBind_IsModelled_AndRoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blwebform", WebWithRootBind);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Binds.Single().Handler, Is.EqualTo("F_Load"));
            Assert.That(file.Model.UnknownChildren, Is.Empty);
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WebWithRootBind));
        });
    }

    /// <summary>Apply's insertion point, not Create's: a bind ADDED to an existing document.</summary>
    [Test]
    public void ARootBindAddedToAnExistingDocument_LandsBeforeTheControls()
    {
        const string text = """
            <Form Name="F" Version="1" Width="400" Height="300">
              <Controls/>
              <Components/>
            </Form>
            """;
        var file = FormDocumentReader.Read("F.blform", text);
        file.Model.Binds.Add(new FormBind { Event = "Load", Handler = "F_Load" });

        var written = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("<Bind Event=\"Load\" Handler=\"F_Load\" />")
                .Or.Contain("<Bind Event=\"Load\" Handler=\"F_Load\"/>"));
            Assert.That(written.IndexOf("<Bind", StringComparison.Ordinal),
                Is.GreaterThan(0).And.LessThan(written.IndexOf("<Controls", StringComparison.Ordinal)));
            Assert.That(FormDocumentReader.Read("F.blform", written).Model.Binds.Single().Handler, Is.EqualTo("F_Load"));
        });
    }

    /// <summary>The reason names only the attributes the document CARRIES — never an empty `Height=""`.</summary>
    [Test]
    public void TheDegradedClientSizeReason_NamesOnlyThePresentAttributes()
    {
        var file = FormDocumentReader.Read("F.blform", """
            <Form Name="F" Version="1" Width="12px">
              <Controls/>
            </Form>
            """);

        Assert.Multiple(() =>
        {
            Assert.That(file.DegradedReasonOfRoot("ClientSize"), Does.Contain("Width=\"12px\""));
            Assert.That(file.DegradedReasonOfRoot("ClientSize"), Does.Not.Contain("Height"));
        });
    }

    /// <summary>
    /// A size that is not POSITIVE is Degraded — the same rule <see cref="FormRootValues.Set"/> refuses it
    /// by, and the region writer emits nothing for it. Preserved exactly.
    /// </summary>
    [TestCase("0", "300")]
    [TestCase("400", "-5")]
    public void ANonPositiveClientSize_IsDegraded_AndPreserved(string width, string height)
    {
        var text = $"""
            <Form Name="F" Version="1" Width="{width}" Height="{height}">
              <Controls/>
            </Form>
            """;
        var file = FormDocumentReader.Read("F.blform", text);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("ClientSize"), Does.Contain("positive"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text));
        });
    }

    /// <summary>
    /// ⚠ Ordinal, like <see cref="FormRootValues.RowForAttribute"/>: a root row's name IS its XML
    /// attribute spelling, which is case-sensitive — `text="x"` is an unknown attribute to the reader,
    /// so the tier API must not call `text` Canon.
    /// </summary>
    [Test]
    public void TheRootTierLookup_IsOrdinal_LikeTheAttributeLookup()
    {
        var file = FormDocumentReader.Read("F.blform", WithRootBind);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("text"), Is.EqualTo(PropertyTier.Unknown));
            Assert.That(FormRootValues.RowForAttribute("text", FormTarget.WinForms), Is.Null);
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
