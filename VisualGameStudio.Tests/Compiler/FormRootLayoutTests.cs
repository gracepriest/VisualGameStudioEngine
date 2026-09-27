using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.2/§2.3 — which FORM rows a document has is decided by (target, layout) through ONE
/// predicate, <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/>; the reader
/// pre-scans the root's &lt;Layout&gt; so it can read the root in the right vocabulary; a Canvas page stores
/// and edits a design size exactly as a .blform does.
/// </summary>
[TestFixture]
public class FormRootLayoutTests
{
    private const string CanvasPage = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls/>
        </WebForm>
        """;

    private const string CanvasPageLayoutLast = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Controls/>
          <Layout Kind="Canvas"/>
        </WebForm>
        """;

    private const string GridPageWithAWidth = """
        <WebForm Name="F" Version="1" Width="640">
          <Layout Kind="Grid" Cols="auto" Rows="auto"/>
          <Controls/>
        </WebForm>
        """;

    private static FormPropertyDef Row(string name) => FormControlCatalog.FormRoot.Property(name)!;

    // ⚠ These expected sets are the SPEC's decisions (§2.3), not a list of catalog kinds. ⚠ RE-CHECK IN
    // TASK 4: MobileBreakpoint joins the Canvas row.
    private static IEnumerable<TestCaseData> Documents()
    {
        yield return new TestCaseData(FormTarget.WinForms, null, new[] { "Text", "ClientSize" })
            .SetName("{m}(WinForms)");
        yield return new TestCaseData(FormTarget.Web, null, new[] { "Text", "Cols", "Rows", "Gap" })
            .SetName("{m}(Web, no Layout)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Grid, new[] { "Text", "Cols", "Rows", "Gap" })
            .SetName("{m}(Web Grid)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Flow, new[] { "Text", "Gap" })
            .SetName("{m}(Web Flow)");
        yield return new TestCaseData(FormTarget.Web, FormLayoutKind.Canvas, new[] { "Text", "ClientSize" })
            .SetName("{m}(Web Canvas)");
    }

    [TestCaseSource(nameof(Documents))]
    public void TheRootRowsThatApply_FollowTheTargetAndTheLayout(
        FormTarget target, FormLayoutKind? layout, string[] expected) =>
        Assert.That(
            FormControlCatalog.FormRoot.Properties
                .Where(r => FormRootValues.Applies(r, target, layout))
                .Select(r => r.Name),
            Is.EqualTo(expected));

    [Test]
    public void TheDocumentOverload_AsksTheSamePredicate()
    {
        var documents = new[]
        {
            new FormDocument { Target = FormTarget.WinForms, Name = "W" },
            new FormDocument { Target = FormTarget.Web, Name = "P" },
            new FormDocument { Target = FormTarget.Web, Name = "C", Layout = new FormLayout { Kind = FormLayoutKind.Canvas } },
            new FormDocument { Target = FormTarget.Web, Name = "L", Layout = new FormLayout { Kind = FormLayoutKind.Flow } },
        };

        Assert.Multiple(() =>
        {
            foreach (var document in documents)
            {
                foreach (var row in FormControlCatalog.FormRoot.Properties)
                {
                    Assert.That(FormRootValues.Applies(row, document),
                        Is.EqualTo(FormRootValues.Applies(row, document.Target, FormVocabulary.LayoutOf(document))),
                        $"form.{row.Name} on {document.Name}");
                }
            }
        });
    }

    /// <summary>
    /// ⛔ Only <see cref="FormRootValues.Applies(FormPropertyDef, FormTarget, FormLayoutKind?)"/> reads
    /// WebLayouts. On a control row nothing would ever read it — a facet that silently does nothing.
    /// </summary>
    [Test]
    public void WebLayouts_IsDeclaredOnlyOnFormRootRows_ThatExistOnTheWeb()
    {
        var onControls = FormControlCatalog.All
            .SelectMany(d => d.Properties.Where(p => p.WebLayouts != null).Select(p => $"{d.Kind}.{p.Name}"))
            .ToList();
        var inert = FormControlCatalog.FormRoot.Properties
            .Where(p => p.WebLayouts != null && !p.AppliesTo(FormTarget.Web))
            .Select(p => p.Name)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(onControls, Is.Empty, "no control consumer reads WebLayouts");
            Assert.That(inert, Is.Empty, "a web layout list on a row that does not exist on the web is never consulted");
        });
    }

    [Test]
    public void RowForAttribute_AsksThePredicate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.Web, FormLayoutKind.Canvas)?.Name,
                Is.EqualTo("ClientSize"));
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.Web, FormLayoutKind.Grid), Is.Null);
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.Web, null), Is.Null, "no <Layout> is Grid");
            Assert.That(FormRootValues.RowForAttribute("Width", FormTarget.WinForms, null)?.Name, Is.EqualTo("ClientSize"));
        });
    }

    [Test]
    public void ACanvasPage_ModelsItsDesignSize_AndRoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        Assert.Multiple(() =>
        {
            Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Message)));
            Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((640, 480)));
            Assert.That(file.Model.UnknownAttributes, Does.Not.ContainKey("Width").And.Not.ContainKey("Height"),
                "modelled, so it is never also written back as an unknown attribute");
            Assert.That(FormRootValues.Get(file.Model, Row("ClientSize")), Is.EqualTo("640, 480"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(CanvasPage), "a no-op is byte-identical");
        });
    }

    [Test]
    public void TheLayoutIsPreScanned_WhenItFollowsTheControls()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPageLayoutLast);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Layout?.Kind, Is.EqualTo(FormLayoutKind.Canvas));
            Assert.That(file.Model.Width, Is.EqualTo(640),
                "the root is read in the Canvas vocabulary although <Layout> comes after <Controls>");
            Assert.That(file.Model.UnknownAttributes, Does.Not.ContainKey("Width"));
            Assert.That(file.Model.UnknownChildren, Is.Empty, "<Layout> is read once, never kept as an unknown element");
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(CanvasPageLayoutLast));
        });
    }

    [Test]
    public void AGridPagesWidth_StaysAnUnknownAttribute_AndRoundTrips()
    {
        var file = FormDocumentReader.Read("F.blwebform", GridPageWithAWidth);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.Width, Is.Null, "a Grid page has no design size (D3)");
            Assert.That(file.Model.UnknownAttributes["Width"], Is.EqualTo("640"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(GridPageWithAWidth));
        });
    }

    [Test]
    public void AnUnparseableCanvasWidth_IsADegradedClientSize_AndPreserved()
    {
        const string text = """
            <WebForm Name="F" Version="1" Width="12px" Height="480">
              <Layout Kind="Canvas"/>
              <Controls/>
            </WebForm>
            """;
        var file = FormDocumentReader.Read("F.blwebform", text);

        Assert.Multiple(() =>
        {
            Assert.That(file.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Degraded));
            Assert.That(file.DegradedReasonOfRoot("ClientSize"), Does.Contain("12px"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text), "frozen AND preserved");
        });

        file.Model.Text = "Edited";
        Assert.That(FormDocumentWriter.Write(file), Does.Contain("Width=\"12px\""),
            "an unrelated edit never touches text the reader could not parse");
    }

    [Test]
    public void EditingACanvasPagesClientSize_WritesTheRootWidthAndHeight()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        Assert.That(FormRootValues.Set(file.Model, Row("ClientSize"), "1024, 600"), Is.True);
        var written = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("Width=\"1024\"").And.Contain("Height=\"600\""));
            Assert.That(written, Does.Contain("<Layout Kind=\"Canvas\""), "the layout is untouched");
            Assert.That(FormDocumentReader.Read("F.blwebform", written).Model.Width, Is.EqualTo(1024));
        });
    }

    [Test]
    public void CreatingACanvasPage_WritesTheSizeAndTheLayout()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "F", Width = 800, Height = 450,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };

        var created = FormDocumentWriter.Create(page);
        var reread = FormDocumentReader.Read("F.blwebform", created);

        Assert.Multiple(() =>
        {
            Assert.That(created, Does.Contain("Width=\"800\"").And.Contain("Height=\"450\"").And.Contain("Kind=\"Canvas\""));
            Assert.That(reread.IsRefused, Is.False);
            Assert.That((reread.Model.Width, reread.Model.Height), Is.EqualTo((800, 450)));
        });
    }

    [Test]
    public void TheClientSizeTier_IsCanonOnACanvasPage_AndUnknownOnAGridPage()
    {
        var canvas = FormDocumentReader.Read("F.blwebform", CanvasPage);
        var grid = FormDocumentReader.Read("F.blwebform", GridPageWithAWidth);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Canon));
            Assert.That(grid.TierOfRoot("ClientSize"), Is.EqualTo(PropertyTier.Unknown));
            Assert.That(canvas.TierOfRoot("Cols"), Is.EqualTo(PropertyTier.Unknown), "a Grid-only row on a Canvas page");
        });
    }

    [Test]
    public void TheRegionWriter_EmitsNoClientSize_ForACanvasPage()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        var result = RegionWriter.Write(
            "F.bas", FormScaffolder.Create("F", FormTarget.Web).CodeText, file.Model, "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(result.Refused, Is.False);
            Assert.That(result.Text, Does.Not.Contain("ClientSize"),
                "the web code-behind emits no geometry (spec §2.3; D4 keeps handler code unchanged)");
        });
    }

    [Test]
    public void TheGrid_OnACanvasPage_ShowsClientSize_AndNoTracks()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = null;

        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("ClientSize"));
            Assert.That(names, Does.Not.Contain("Cols").And.Not.Contains("Rows").And.Not.Contains("Gap"));
            Assert.That(grid.Rows.Single(r => r.Name == "ClientSize").StringValue, Is.EqualTo("640, 480"));
        });

        grid.Rows.Single(r => r.Name == "ClientSize").StringValue = "800, 600";

        Assert.That((file.Model.Width, file.Model.Height), Is.EqualTo((800, 600)));
    }

    [Test]
    public void TheGrid_OnAFlowPage_ShowsGap_AndNoTracksOrSize()
    {
        var file = FormDocumentReader.Read("F.blwebform", """
            <WebForm Name="F" Version="1">
              <Layout Kind="Flow" Dir="Vertical" Gap="4px"/>
              <Controls/>
            </WebForm>
            """);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = null;

        var names = grid.Rows.Select(r => r.Name).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain("Gap"), "the emitter writes gap for Flow too (FormAssetEmitter.cs:427-430)");
            Assert.That(names, Does.Not.Contain("Cols").And.Not.Contains("Rows").And.Not.Contains("ClientSize"));
        });
    }
}
