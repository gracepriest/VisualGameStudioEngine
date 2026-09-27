using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.3/§5 — a Canvas page's phone breakpoint: a FormRoot row stored on &lt;Layout&gt;,
/// default 600, 0 = never stack, anything else unusable Degraded and preserved byte-for-byte.
/// </summary>
[TestFixture]
public class FormMobileBreakpointTests
{
    private static FormPropertyDef Row => FormControlCatalog.FormRoot.Property("MobileBreakpoint")!;

    private static string Page(string breakpoint) => $"""
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas" MobileBreakpoint="{breakpoint}"/>
          <Controls/>
        </WebForm>
        """;

    [Test]
    public void TheRow_IsAnIntDefaultingToTheLayoutsDefault_OnACanvasPageOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Row.Type, Is.EqualTo(FormPropertyType.Int));
            Assert.That(Row.Default, Is.EqualTo(FormLayout.DefaultMobileBreakpoint.ToString()), "one constant, not two");
            Assert.That(FormRootValues.Applies(Row, FormTarget.Web, FormLayoutKind.Canvas), Is.True);
            Assert.That(FormRootValues.Applies(Row, FormTarget.Web, FormLayoutKind.Grid), Is.False);
            Assert.That(FormRootValues.Applies(Row, FormTarget.Web, FormLayoutKind.Flow), Is.False);
            Assert.That(FormRootValues.Applies(Row, FormTarget.WinForms, null), Is.False);
            Assert.That(FormRootValues.StorageAttributes(Row), Is.Empty, "stored on <Layout>, like Cols/Rows/Gap");
        });
    }

    [Test]
    public void AValue_IsReadRoundTripsAndIsCanon()
    {
        var file = FormDocumentReader.Read("F.blwebform", Page("480"));

        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.Get(file.Model, Row), Is.EqualTo("480"));
            Assert.That(file.Model.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(480));
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(Page("480")));
        });
    }

    [TestCase("-5")]
    [TestCase("abc")]
    [TestCase("48px")]
    public void AnUnusableValue_IsDegraded_AndPreservedThroughAnUnrelatedEdit(string raw)
    {
        var file = FormDocumentReader.Read("F.blwebform", Page(raw));

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("MobileBreakpoint"), Does.Contain(raw).And.Contain("preserved"));
            Assert.That(file.Model.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(FormLayout.DefaultMobileBreakpoint),
                "a Degraded value never reaches the page; the default applies");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(Page(raw)));
        });

        file.Model.Text = "Edited";

        Assert.That(FormDocumentWriter.Write(file), Does.Contain($"MobileBreakpoint=\"{raw}\""),
            "⛔ the model holds the RAW text, so nothing can remove it on a save (spec §2.3)");
    }

    /// <summary>
    /// ⛔ The catalog's culture-free parser, not <c>int.TryParse</c>: the current-culture overload accepts a tab
    /// and other whitespace the designer's other Int rows refuse. ⚠ The two non-ASCII inputs are built from
    /// code points — a raw character pasted through an editor tool has been stored wrongly before.
    /// </summary>
    [TestCase("+600", true, 600)]
    [TestCase("0600", true, 600)]
    [TestCase("0", true, 0)]
    [TestCase("\t600", false, FormLayout.DefaultMobileBreakpoint)]
    [TestCase("600.0", false, FormLayout.DefaultMobileBreakpoint)]
    [TestCase("1e3", false, FormLayout.DefaultMobileBreakpoint)]
    [TestCase("99999999999", false, FormLayout.DefaultMobileBreakpoint)]
    [TestCase("arabic-indic 600", false, FormLayout.DefaultMobileBreakpoint)]
    [TestCase("U+2212 minus 5", false, FormLayout.DefaultMobileBreakpoint)]
    public void TryParseMobileBreakpoint_IsTheCatalogsCultureFreeParser(string input, bool usable, int pixels)
    {
        var text = input switch
        {
            "arabic-indic 600" => new string(new[] { (char)0x0666, (char)0x0660, (char)0x0660 }),
            "U+2212 minus 5" => ((char)0x2212) + "5",
            _ => input
        };

        Assert.Multiple(() =>
        {
            Assert.That(FormLayout.TryParseMobileBreakpoint(text, out var parsed), Is.EqualTo(usable));
            Assert.That(parsed, Is.EqualTo(pixels));
        });
    }

    [Test]
    public void AnOverflowingValue_IsDegraded_AndTheReasonNamesTheRange()
    {
        var file = FormDocumentReader.Read("F.blwebform", Page("99999999999"));

        Assert.That(file.DegradedReasonOfRoot("MobileBreakpoint"),
            Does.Contain("from 0 to 2147483647").And.Contain("0 means never stack").And.Not.Contain("positive"));
    }

    [Test]
    public void Set_AcceptsZeroAndPositive_RefusesNegativeAndText_AndNullRemoves()
    {
        var file = FormDocumentReader.Read("F.blwebform", Page("480"));

        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.Set(file.Model, Row, "0"), Is.True, "0 = never stack");
            Assert.That(file.Model.Layout!.EffectiveMobileBreakpoint, Is.EqualTo(0));
            Assert.That(FormRootValues.Set(file.Model, Row, "-1"), Is.False);
            Assert.That(FormRootValues.Set(file.Model, Row, "wide"), Is.False);
            Assert.That(FormRootValues.Get(file.Model, Row), Is.EqualTo("0"), "a refused value changes nothing");
            Assert.That(FormRootValues.Set(file.Model, Row, "0720"), Is.True);
            Assert.That(FormRootValues.Get(file.Model, Row), Is.EqualTo("720"), "stored as its invariant number");
            Assert.That(FormRootValues.CanReset(Row), Is.True);
            Assert.That(FormRootValues.Set(file.Model, Row, null), Is.True);
            Assert.That(file.Model.Layout.EffectiveMobileBreakpoint, Is.EqualTo(FormLayout.DefaultMobileBreakpoint));
            Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("MobileBreakpoint"));
        });
    }

    /// <summary>
    /// ⛔ The writer's APPLY path on a Canvas page (carried from Task 2's review: ApplyLayout on a Canvas page
    /// was only weakly pinned). An edit updates the existing attribute in place, a Reset removes it, and an
    /// edit on a page whose &lt;Layout&gt; never carried one adds it — each a byte-exact expectation, so a
    /// writer that skipped ApplyLayout for a pixel page fails here.
    /// </summary>
    [Test]
    public void EditingIt_OnACanvasPage_UpdatesAddsAndRemovesTheLayoutAttribute()
    {
        var edited = FormDocumentReader.Read("F.blwebform", Page("480"));
        Assert.That(FormRootValues.Set(edited.Model, Row, "720"), Is.True);
        var updated = FormDocumentWriter.Write(edited);

        Assert.That(FormRootValues.Set(edited.Model, Row, null), Is.True);
        var removed = FormDocumentWriter.Write(edited);

        var absent = FormDocumentReader.Read("F.blwebform", """
            <WebForm Name="F" Version="1" Width="640" Height="480">
              <Layout Kind="Canvas"/>
              <Controls/>
            </WebForm>
            """);
        Assert.That(FormRootValues.Set(absent.Model, Row, "0"), Is.True);
        var added = FormDocumentWriter.Write(absent);

        Assert.Multiple(() =>
        {
            // ⚠ A changed document is re-serialized (` />`), so the expectations are the writer's spelling.
            Assert.That(updated, Is.EqualTo("""
                <WebForm Name="F" Version="1" Width="640" Height="480">
                  <Layout Kind="Canvas" MobileBreakpoint="720" />
                  <Controls />
                </WebForm>
                """), "updated in place, nothing else touched");
            Assert.That(removed, Is.EqualTo("""
                <WebForm Name="F" Version="1" Width="640" Height="480">
                  <Layout Kind="Canvas" />
                  <Controls />
                </WebForm>
                """), "Reset (null) removes the attribute — null means absent in this storage (scope call S1)");
            Assert.That(added, Does.Contain("<Layout Kind=\"Canvas\" MobileBreakpoint=\"0\" />"));
        });
    }

    /// <summary>⚠ <see cref="FormLayout.Clone"/> has no production caller today; this keeps its copy complete.</summary>
    [Test]
    public void Clone_CarriesIt()
    {
        var layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = "-5" };

        Assert.That(layout.Clone().MobileBreakpoint, Is.EqualTo("-5"), "raw text, unusable values included");
    }

    [Test]
    public void CreatingACanvasPage_WritesItOnTheLayout()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "F", Width = 800, Height = 450,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas, MobileBreakpoint = "600" }
        };

        Assert.That(FormDocumentWriter.Create(page), Does.Contain("<Layout Kind=\"Canvas\" MobileBreakpoint=\"600\" />"));
    }

    /// <summary>Create from a model EDITED through the row (not hand-built): the edit and the Reset both reach it.</summary>
    [Test]
    public void CreatingACanvasPage_AfterAnEdit_AndAfterAReset()
    {
        var model = FormDocumentReader.Read("F.blwebform", Page("480")).Model;

        FormRootValues.Set(model, Row, "360");
        var edited = FormDocumentWriter.Create(model);
        FormRootValues.Set(model, Row, null);
        var reset = FormDocumentWriter.Create(model);

        Assert.Multiple(() =>
        {
            Assert.That(edited, Does.Contain("<Layout Kind=\"Canvas\" MobileBreakpoint=\"360\" />"));
            Assert.That(reset, Does.Contain("<Layout Kind=\"Canvas\" />").And.Not.Contain("MobileBreakpoint"));
        });
    }

    [Test]
    public void OnAGridPage_ItRoundTripsUntouched_AndIsNotARowThere()
    {
        const string text = """
            <WebForm Name="F" Version="1">
              <Layout Kind="Grid" Cols="auto" Rows="auto" MobileBreakpoint="-9"/>
              <Controls/>
            </WebForm>
            """;
        var file = FormDocumentReader.Read("F.blwebform", text);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("MobileBreakpoint"), Is.EqualTo(PropertyTier.Unknown));
            Assert.That(file.DegradedRoot, Is.Empty, "only a Canvas page judges it");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text));
        });
    }

    /// <summary>
    /// ⛔ "The reader judges MobileBreakpoint" and "the MobileBreakpoint row exists" must be ONE fact: a
    /// Degraded finding on a document with no row would freeze nothing and name a property the grid does not
    /// show; a row with no judgement would show an unusable value as Canon. The reader asks
    /// <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>; this sweep holds it
    /// there for every (target, layout) that can carry the attribute.
    /// </summary>
    [Test]
    public void AnUnusableValue_IsJudged_ExactlyWhereTheRowApplies()
    {
        var documents = new (string File, FormTarget Target, FormLayoutKind? Layout, string Text)[]
        {
            ("F.blform", FormTarget.WinForms, null, """
                <Form Name="F" Version="1"><Layout Kind="Canvas" MobileBreakpoint="-1"/><Controls/></Form>
                """),
            ("F.blwebform", FormTarget.Web, FormLayoutKind.Grid, """
                <WebForm Name="F" Version="1"><Layout MobileBreakpoint="-1"/><Controls/></WebForm>
                """),
        }.Concat(Enum.GetValues<FormLayoutKind>().Select(kind => ("F.blwebform", FormTarget.Web, (FormLayoutKind?)kind, $"""
                <WebForm Name="F" Version="1"><Layout Kind="{kind}" MobileBreakpoint="-1"/><Controls/></WebForm>
                """)));

        Assert.Multiple(() =>
        {
            foreach (var (fileName, target, layout, text) in documents)
            {
                var file = FormDocumentReader.Read(fileName, text);
                var where = $"{target}/{layout?.ToString() ?? "no layout"}";

                Assert.That(file.IsRefused, Is.False, where);
                Assert.That(file.DegradedReasonOfRoot("MobileBreakpoint") != null,
                    Is.EqualTo(FormRootValues.Applies(Row, target, layout)), where);
                Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text), $"{where}: round-trips untouched");
            }
        });
    }

    [Test]
    public void TheGrid_OnACanvasPage_ShowsItWithItsDefault_AndFreezesAnUnusableOne()
    {
        var absent = FormDocumentReader.Read("F.blwebform", """
            <WebForm Name="F" Version="1" Width="640" Height="480">
              <Layout Kind="Canvas"/>
              <Controls/>
            </WebForm>
            """);
        var grid = new FormPropertyGridViewModel();
        grid.Load(absent);
        grid.SelectedControl = null;

        var degraded = FormDocumentReader.Read("F.blwebform", Page("abc"));
        var frozenGrid = new FormPropertyGridViewModel();
        frozenGrid.Load(degraded);
        frozenGrid.SelectedControl = null;

        Assert.Multiple(() =>
        {
            Assert.That(grid.Rows.Single(r => r.Name == "MobileBreakpoint").StringValue, Is.EqualTo("600"));
            Assert.That(frozenGrid.Rows.Single(r => r.Name == "MobileBreakpoint").IsFrozen, Is.True);
        });
    }
}
