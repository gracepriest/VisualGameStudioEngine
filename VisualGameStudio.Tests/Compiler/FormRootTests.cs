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
    /// every document); the (target, layout) filter is <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>,
    /// which <see cref="FormRootValues.RowForAttribute"/> asks, and which must answer a row exactly on the
    /// (target, layout) pairs it applies to.
    /// </summary>
    [Test]
    public void EveryFormRootRow_HasStorage_OnBothTargets()
    {
        var samples = new Dictionary<FormPropertyType, string>
        {
            [FormPropertyType.String] = "sample",
            [FormPropertyType.Size] = "75, 23",
            [FormPropertyType.Int] = "600",
            // Slice 3's Properties-stored rows (the store keeps the document's text — the tiers judge it).
            [FormPropertyType.Bool] = "true",
            [FormPropertyType.Enum] = "sample",
            [FormPropertyType.Color] = "Red",
            [FormPropertyType.Font] = "Arial, 10pt",
            [FormPropertyType.Fraction] = "0.5",
            [FormPropertyType.Reference] = "btnOk",
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
                        foreach (var layout in target == FormTarget.WinForms
                                     ? new FormLayoutKind?[] { null }
                                     : new FormLayoutKind?[] { null, FormLayoutKind.Grid, FormLayoutKind.Flow, FormLayoutKind.Canvas })
                        {
                            Assert.That(FormRootValues.RowForAttribute(attribute, target, layout),
                                FormRootValues.Applies(row, target, layout) ? Is.SameAs(row) : Is.Null,
                                $"{where} ({layout?.ToString() ?? "no layout"}): attribute '{attribute}' belongs to the row exactly where the row applies");
                        }
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

    /// <summary>
    /// Slice 3 backlog (3): the root size's no-op check compared TEXT, so a save that changed nothing rewrote
    /// <c>Width="0400"</c> as <c>"400"</c>. It compares the PARSED number, as every control coordinate does
    /// (SetIntAttributeIfChanged) — the spelling is the user's until the number moves.
    /// </summary>
    [TestCase("F.blform", "<Form Name=\"F\" Version=\"1\" Width=\"0400\" Height=\" 300 \"><Controls/></Form>")]
    [TestCase("F.blwebform", "<WebForm Name=\"F\" Version=\"1\" Width=\"+800\" Height=\"0450\"><Layout Kind=\"Canvas\"/><Controls/></WebForm>")]
    public void ARootSizeSpelledDifferently_SurvivesANoOpSave_AndMovesOnlyWhenTheNumberDoes(string name, string xml)
    {
        var file = FormDocumentReader.Read(name, xml);

        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(xml), "a no-op save is byte-identical");

        file.Model.Width = file.Model.Width + 1;
        var moved = FormDocumentWriter.Write(file);

        Assert.That(moved, Does.Contain($"Width=\"{file.Model.Width}\""), "a real edit is written in canonical form");
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
            Assert.That(FormRootValues.RowForAttribute("text", FormTarget.WinForms, null), Is.Null);
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

    [TestCase("0, 300", true)]
    [TestCase("640, -1", true)]
    [TestCase("640, 0", true)]
    [TestCase("abc", true)]
    [TestCase("640, 480", false)]
    public void RefusalOf_ClientSize_IsExactlyWhatSetRefuses(string value, bool refused)
    {
        var row = FormControlCatalog.FormRoot.Property("ClientSize")!;
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };

        var reason = FormRootValues.RefusalOf(row, value);

        Assert.Multiple(() =>
        {
            Assert.That(reason != null, Is.EqualTo(refused));
            Assert.That(FormRootValues.Set(form, row, value), Is.EqualTo(!refused), "ONE rule: Set asks RefusalOf");
            if (refused)
            {
                Assert.That(reason, Does.Contain($"'{value}'").And.EndWith("It was not applied; ClientSize is unchanged."));
            }
        });
    }

    [Test]
    public void RefusalOf_AnswersForEveryFormRootRow_AndNeverRefusesFreeText()
    {
        Assert.Multiple(() =>
        {
            foreach (var row in FormControlCatalog.FormRoot.Properties)
            {
                Assert.DoesNotThrow(() => FormRootValues.RefusalOf(row, "1"), $"{row.Name} is mapped in RefusalOf");
            }

            foreach (var name in new[] { "Text", "Cols", "Rows", "Gap" })
            {
                Assert.That(FormRootValues.RefusalOf(FormControlCatalog.FormRoot.Property(name)!, "anything at all"), Is.Null, name);
            }
        });
    }

    // ==================================================================
    // Slice 3 Task 4 (plan 3.2 + 3.3) — Properties-stored root rows: the Form's D1 set
    // ==================================================================

    private const string WinFormWithRootProperties = """
        <Form Name="F" Version="1" Text="Hi" Width="400" Height="300" FormBorderStyle="FixedDialog" TopMost="true" Opacity="0.85">
          <Controls/>
        </Form>
        """;

    /// <summary>
    /// A catalog-only root attribute lives in <see cref="FormDocument.Properties"/> (spec §2.3) — read, modelled, written
    /// back byte-identically when nothing changed, and never also kept as an unknown attribute.
    /// </summary>
    [Test]
    public void APropertiesStoredRootRow_IsModelled_AndRoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blform", WinFormWithRootProperties);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Properties["FormBorderStyle"], Is.EqualTo("FixedDialog"));
            Assert.That(file.Model.Properties["TopMost"], Is.EqualTo("true"));
            Assert.That(file.Model.UnknownAttributes, Does.Not.ContainKey("FormBorderStyle"));
            Assert.That(FormRootValues.Get(file.Model, FormControlCatalog.FormRoot.Property("Opacity")!), Is.EqualTo("0.85"));
            Assert.That(file.TierOfRoot("FormBorderStyle"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(WinFormWithRootProperties), "a no-op is byte-identical");
        });
    }

    /// <summary>
    /// ⛔ Owner decision (2026-09-29): Opacity is shown and typed as a PERCENTAGE, like VS, and stored the way WinForms
    /// stores it — the 0–1 Double. Through the REAL grid row: <c>0.85</c> shows as <c>85%</c>, typing <c>80%</c> stores
    /// <c>0.8</c>, and a refused <c>150</c> is never written — the row keeps showing <c>80%</c> with the reason.
    /// </summary>
    [Test]
    public void OpacityThroughTheGrid_IsAPercentage_StoredAsTheFraction_AndARefusalSnapsBack()
    {
        var file = FormDocumentReader.Read("F.blform", WinFormWithRootProperties);
        var grid = new VisualGameStudio.Shell.ViewModels.Designer.FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = null;
        var row = grid.Rows.Single(r => r.Name == "Opacity");

        Assert.That(row.StringValue, Is.EqualTo("85%"), "shown as VS shows it");

        row.StringValue = "80%";
        Assert.Multiple(() =>
        {
            Assert.That(row.Refusal, Is.Null);
            Assert.That(file.Model.Properties["Opacity"], Is.EqualTo("0.8"), "stored as WinForms' Double, never '80%'");
            Assert.That(row.StringValue, Is.EqualTo("80%"));
        });

        row.StringValue = "150";
        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Properties["Opacity"], Is.EqualTo("0.8"), "a refused value is never written");
            Assert.That(row.Refusal, Does.Contain("100%"), "and the reason says what is accepted");
            Assert.That(row.StringValue, Is.EqualTo("80%"), "the editor snaps back to what the document holds");
            Assert.That(FormDocumentWriter.Write(file), Does.Contain("Opacity=\"0.8\""));
        });
    }

    [Test]
    public void AnEditedRootProperty_IsWritten_AndAResetOneLeavesTheDocument()
    {
        var file = FormDocumentReader.Read("F.blform", WinFormWithRootProperties);
        var border = FormControlCatalog.FormRoot.Property("FormBorderStyle")!;
        var topMost = FormControlCatalog.FormRoot.Property("TopMost")!;

        Assert.That(FormRootValues.Set(file.Model, border, "None"), Is.True);
        Assert.That(FormRootValues.Set(file.Model, topMost, null), Is.True);
        Assert.That(FormRootValues.CanReset(topMost), Is.True, "only ClientSize cannot be removed");

        var written = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("FormBorderStyle=\"None\""));
            Assert.That(written, Does.Not.Contain("TopMost"), "Reset REMOVES the attribute (spec §2.7)");
        });
    }

    [Test]
    public void Create_WritesRootProperties_InCatalogOrder()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        form.Properties["TopMost"] = "true";
        form.Properties["FormBorderStyle"] = "FixedSingle";

        var created = FormDocumentWriter.Create(form);

        Assert.That(created.IndexOf("FormBorderStyle=", StringComparison.Ordinal),
            Is.GreaterThan(created.IndexOf("Height=", StringComparison.Ordinal))
              .And.LessThan(created.IndexOf("TopMost=", StringComparison.Ordinal)),
            "catalog order after the typed attributes: " + created);
    }

    /// <summary>A WinForms-only root row on a web page is not a row there: it stays an unknown attribute, untouched.</summary>
    [Test]
    public void AWinFormsOnlyRootAttributeOnAPage_IsKeptAsUnknown()
    {
        const string xml = """
            <WebForm Name="F" Version="1" FormBorderStyle="FixedDialog"><Controls/></WebForm>
            """;
        var file = FormDocumentReader.Read("F.blwebform", xml);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Properties, Does.Not.ContainKey("FormBorderStyle"));
            Assert.That(file.Model.UnknownAttributes["FormBorderStyle"], Is.EqualTo("FixedDialog"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(xml));
        });
    }

    /// <summary>
    /// D9 at the root: a Properties-stored value the row cannot use is Degraded — frozen with the catalog's reason,
    /// preserved on save — and (the branch GenerateInit carried as UNREACHABLE since slice 1, plan :6100) it never reaches
    /// the generated code: BL8009 names it instead.
    /// </summary>
    [Test]
    public void ADegradedRootProperty_IsFrozen_Preserved_AndNeverEmitted()
    {
        const string xml = """
            <Form Name="F" Version="1" Width="400" Height="300" FormBorderStyle="Bogus"><Controls/></Form>
            """;
        var file = FormDocumentReader.Read("F.blform", xml);
        var row = FormControlCatalog.FormRoot.Property("FormBorderStyle")!;

        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, file.Model, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("FormBorderStyle"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("FormBorderStyle"), Is.EqualTo(row.DescribeRefusal("Bogus", FormTarget.WinForms)));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(xml));
            Assert.That(result.Text, Does.Not.Contain("Me.FormBorderStyle"));
            Assert.That(result.Diagnostics.Single(d => d.Code == DesignCodes.DegradedProperty).Message,
                Does.Contain("'form.FormBorderStyle'").And.Contain("Bogus"));
        });
    }

    [Test]
    public void TheRegionWriter_EmitsARootPropertyAsOneMeStatement()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        form.Properties["FormBorderStyle"] = "FixedDialog";
        form.Properties["Font"] = "Segoe UI, 10pt, style=Bold";
        form.Properties["Opacity"] = "0.85";
        form.Properties["MinimumSize"] = "200, 100";

        var text = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform").Text;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Me.FormBorderStyle = FormBorderStyle.FixedDialog"));
            Assert.That(text, Does.Contain("Me.Font = New Font(\"Segoe UI\", 10F, FontStyle.Bold)"));
            Assert.That(text, Does.Contain("Me.Opacity = 0.85"));
            Assert.That(text, Does.Contain("Me.MinimumSize = New Size(200, 100)"));
        });
    }

    // ==================================================================
    // AcceptButton / CancelButton — a reference to a control (pre-flight §3, B4)
    // ==================================================================

    private static FormDocument FormWithButton(string buttonKind = "Button")
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        form.Controls.Add(new FormControl
        {
            Kind = buttonKind, Id = "btnOk", TabIndex = 0,
            Geometry = new PixelGeometry { X = 8, Y = 8, Width = 75, Height = 23 }
        });
        return form;
    }

    /// <summary>
    /// ⛔ B4: emitted AFTER the controls are constructed and added. Before them, <c>btnOk</c> is still Nothing, so the
    /// form would compile and have no accept button.
    /// </summary>
    [Test]
    public void AcceptButton_IsEmittedAfterTheControls()
    {
        var form = FormWithButton();
        form.Properties["AcceptButton"] = "btnOk";
        form.Properties["CancelButton"] = "btnOk";

        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");
        var text = result.Text;

        Assert.Multiple(() =>
        {
            Assert.That(result.Diagnostics.Where(d => !d.IsWarning), Is.Empty);
            Assert.That(text.IndexOf("Me.AcceptButton = btnOk", StringComparison.Ordinal),
                Is.GreaterThan(text.IndexOf("Me.Controls.Add(btnOk)", StringComparison.Ordinal)));
            Assert.That(text.IndexOf("Me.CancelButton = btnOk", StringComparison.Ordinal),
                Is.GreaterThan(text.IndexOf("Me.Controls.Add(btnOk)", StringComparison.Ordinal)));
            Assert.That(text.IndexOf("btnOk = New Button()", StringComparison.Ordinal), Is.GreaterThan(0));
        });
    }

    /// <summary>
    /// A reference that names no control of the kinds its row allows (renamed, deleted, or a Label) is WARNED — BL8034 —
    /// and not emitted: <c>Me.AcceptButton = lblTitle</c> is CS0029 at csc, <c>= btnGone</c> CS0103, BasicLang silent.
    /// </summary>
    [TestCase("btnGone", "Button")]
    [TestCase("btnOk", "Label")]
    public void ADanglingReference_IsWarned_AndNotEmitted(string reference, string kindOfBtnOk)
    {
        var form = FormWithButton(kindOfBtnOk);
        form.Properties["AcceptButton"] = reference;

        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False, "a warning, never a refusal — the document is preserved");
            Assert.That(result.Text, Does.Not.Contain("Me.AcceptButton"));
            var warning = result.Diagnostics.Single(d => d.Code == DesignCodes.ReferenceNotFound);
            Assert.That(warning.IsWarning, Is.True);
            Assert.That(warning.Message, Does.Contain("'form.AcceptButton'").And.Contain(reference).And.Contain("Button"));
        });
    }

    /// <summary>
    /// Slice 4 D-7: the BL8034 check is <see cref="FormReferences.IsAllowed"/>, the same function the grid's drop-down
    /// lists from — a Button inside a Panel is offered there, so it must be emitted here, and a case-different Id is
    /// refused in both.
    /// </summary>
    [TestCase("btnInner", true)]
    [TestCase("BTNINNER", false)]
    public void TheReferenceCheck_IsTheSharedPredicate(string reference, bool emitted)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        var panel = new FormControl
        {
            Kind = "Panel", Id = "pnl", TabIndex = 0, Geometry = new PixelGeometry { X = 8, Y = 8, Width = 200, Height = 100 }
        };
        panel.Children.Add(new FormControl
        {
            Kind = "Button", Id = "btnInner", TabIndex = 0, Geometry = new PixelGeometry { X = 8, Y = 8, Width = 75, Height = 23 }
        });
        form.Controls.Add(panel);
        form.Properties["AcceptButton"] = reference;

        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text.Contains($"Me.AcceptButton = {reference}"), Is.EqualTo(emitted));
            Assert.That(result.Diagnostics.Any(d => d.Code == DesignCodes.ReferenceNotFound), Is.EqualTo(!emitted));
            Assert.That(FormReferences.IsAllowed(form, FormControlCatalog.FormRoot.Properties.Single(p => p.Name == "AcceptButton"),
                reference), Is.EqualTo(emitted), "the grid's list and the writer agree");
        });
    }

    [Test]
    public void TheReferenceCode_IsBL8034() =>
        Assert.That(DesignCodes.ReferenceNotFound, Is.EqualTo("BL8034"));

    // ==================================================================
    // The Form on the web: BackColor, ForeColor, Font on body (spec §2.3)
    // ==================================================================

    [TestCase(FormLayoutKind.Grid)]
    [TestCase(FormLayoutKind.Canvas)]
    public void TheFormsWebRows_AreTheBodysCss_OnEveryLayout(FormLayoutKind layout)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout { Kind = layout } };
        form.Properties["BackColor"] = "#102030";
        form.Properties["ForeColor"] = "Control";
        form.Properties["Font"] = "Segoe UI, 10pt, style=Italic";

        var css = FormAssetEmitter.Css(form);
        var body = css.Split('\n').Single(l => l.StartsWith("body {", StringComparison.Ordinal) && l.Contains("background-color"));

        Assert.Multiple(() =>
        {
            Assert.That(body, Does.Contain("background-color: #102030"));
            Assert.That(body, Does.Contain("color: ButtonFace"), "system colours map through the ONE table");
            Assert.That(body, Does.Contain("font-family: \"Segoe UI\"").And.Contain("font-size: 10pt").And.Contain("font-style: italic"));
        });
    }

    /// <summary>
    /// ⛔ Found by the RUN (task 7, Edge's computed style): a browser does NOT inherit the body's font into
    /// &lt;button&gt;/&lt;input&gt;/&lt;select&gt;/&lt;textarea&gt; — their user-agent sheet sets their own — so a Form
    /// Font on body left every button regular while the WinForms window made it bold (Font is ambient on every
    /// control). With a Form Font the page tells its form controls to inherit it; a Form ForeColor reaches a button the
    /// same way (Button's ForeColor is ambient; a TextBox's is WindowText in WinForms, so inputs keep their own).
    /// Without either row nothing changes (the pixel harness's measured pages stay byte-identical).
    /// </summary>
    [Test]
    public void AFormFont_ReachesThePagesFormControls_AsWinFormsAmbientFontDoes()
    {
        var withFont = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout { Kind = FormLayoutKind.Canvas } };
        withFont.Properties["Font"] = "Segoe UI, 10pt";
        withFont.Properties["ForeColor"] = "Red";
        var plain = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout { Kind = FormLayoutKind.Canvas } };

        Assert.Multiple(() =>
        {
            Assert.That(FormAssetEmitter.Css(withFont), Does.Contain("button, input, select, textarea { font: inherit; }"));
            Assert.That(FormAssetEmitter.Css(withFont), Does.Contain("button { color: inherit; }"));
            Assert.That(FormAssetEmitter.Css(plain), Does.Not.Contain("inherit"));
        });
    }

    /// <summary>
    /// ⛔ Code review I2 (2026-09-29): a CONTAINER's Font is ambient too — a Button in a GroupBox with a bold Font is bold
    /// in WinForms — so the inherit rules are written when a Font (resp. ForeColor) is present ANYWHERE on the page, not
    /// only on the Form. A Grid or Flow page with none anywhere is byte-identical to before.
    /// </summary>
    [TestCase(FormLayoutKind.Canvas)]
    [TestCase(FormLayoutKind.Grid)]
    [TestCase(FormLayoutKind.Flow)]
    public void AContainersFontOrForeColor_ReachesThePagesFormControls_AsWinFormsAmbientPropertiesDo(FormLayoutKind kind)
    {
        FormDocument Page(string? font, string? fore)
        {
            var page = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout { Kind = kind } };
            var group = new FormControl { Kind = "GroupBox", Id = "grp", TabIndex = 0, Geometry = new GridGeometry() };
            if (font != null) group.Properties["Font"] = font;
            if (fore != null) group.Properties["ForeColor"] = fore;
            group.Children.Add(new FormControl { Kind = "Button", Id = "btn", TabIndex = 0, Geometry = new GridGeometry() });
            page.Controls.Add(group);
            return page;
        }

        var withFont = FormAssetEmitter.Css(Page("Segoe UI, 10pt, style=Bold", null));
        var withFore = FormAssetEmitter.Css(Page(null, "#C00000"));
        var plain = FormAssetEmitter.Css(Page(null, null));
        var unreadable = FormAssetEmitter.Css(Page("Arial", null));

        Assert.Multiple(() =>
        {
            Assert.That(withFont, Does.Contain("button, input, select, textarea { font: inherit; }"));
            Assert.That(withFont, Does.Not.Contain("button { color: inherit; }"), "each rule only for its own property");
            Assert.That(withFore, Does.Contain("button { color: inherit; }"));
            Assert.That(withFore, Does.Not.Contain("font: inherit"));
            Assert.That(plain, Does.Not.Contain("inherit"), "a page with no Font or ForeColor anywhere is unchanged");
            Assert.That(unreadable, Does.Not.Contain("inherit"), "a Degraded font is never emitted, so nothing inherits it");
        });
    }

    [Test]
    public void APageWithNoFormRows_HasNoExtraBodyRule_AndAWinFormsOnlyRowNeverReachesIt()
    {
        var plain = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var withForeignRow = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        withForeignRow.Properties["TopMost"] = "true";   // an in-memory model only — the reader keeps it unknown

        Assert.Multiple(() =>
        {
            Assert.That(FormAssetEmitter.Css(plain), Does.Not.Contain("body {"), "a Grid page's stylesheet is unchanged");
            Assert.That(FormAssetEmitter.Css(withForeignRow), Is.EqualTo(FormAssetEmitter.Css(plain)));
        });
    }
}
