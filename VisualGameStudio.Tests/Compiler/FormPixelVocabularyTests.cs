using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §2.1 / §2.5 — a Canvas page's CONTROLS are read, written, judged structural and pasted in
/// the pixel vocabulary; a paste between VOCABULARIES (pixels vs cells) is refused and SAID.
///
/// <para>⚠ NonParallelizable: the command tests share the view model's STATIC designer clipboard.</para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class FormPixelVocabularyTests
{
    private const string CanvasPage = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls>
            <Panel Id="pnl" X="16" Y="40" Width="300" Height="200" Anchor="Top,Left,Right" TabIndex="0">
              <Button Id="btn" X="8" Y="8" Width="75" Height="23" Dock="Bottom" TabIndex="1" Text="Go"/>
            </Panel>
            <TextBox Id="txt" X="330" Y="40" Width="120" Height="23" TabIndex="2" Col="3"/>
          </Controls>
        </WebForm>
        """;

    private const string CanvasPageLayoutLast = """
        <WebForm Name="F" Version="1" Width="640" Height="480">
          <Controls>
            <Button Id="btn" X="8" Y="8" Width="75" Height="23" TabIndex="0"/>
          </Controls>
          <Layout Kind="Canvas"/>
        </WebForm>
        """;

    private const string GridPage = """
        <WebForm Name="F" Version="1">
          <Layout Kind="Grid" Cols="auto,1fr" Rows="auto"/>
          <Controls>
            <Button Id="btn" Col="1" Row="0" X="190" TabIndex="0"/>
          </Controls>
        </WebForm>
        """;

    // ==================================================================
    // IsStructural by (target, layout)
    // ==================================================================

    [TestCase("X", FormTarget.Web, FormLayoutKind.Canvas, true)]
    [TestCase("Dock", FormTarget.Web, FormLayoutKind.Canvas, true)]
    [TestCase("Col", FormTarget.Web, FormLayoutKind.Canvas, false)]
    [TestCase("X", FormTarget.Web, FormLayoutKind.Grid, false)]
    [TestCase("Col", FormTarget.Web, FormLayoutKind.Grid, true)]
    [TestCase("X", FormTarget.Web, null, false)]
    [TestCase("X", FormTarget.WinForms, null, true)]
    [TestCase("TabIndex", FormTarget.Web, FormLayoutKind.Canvas, true)]
    [TestCase("Id", FormTarget.Web, FormLayoutKind.Flow, true)]
    public void IsStructural_AsksTheLayout(string attribute, FormTarget target, FormLayoutKind? layout, bool expected) =>
        Assert.That(FormControlCatalog.IsStructural(attribute, target, layout), Is.EqualTo(expected));

    // ==================================================================
    // The reader and the writer
    // ==================================================================

    [Test]
    public void ACanvasPagesControls_ReadPixelGeometry_AtEveryDepth()
    {
        var model = FormDocumentReader.Read("F.blwebform", CanvasPage).Model;
        var panel = (PixelGeometry)model.FindById("pnl")!.Geometry!;
        var button = (PixelGeometry)model.FindById("btn")!.Geometry!;

        Assert.Multiple(() =>
        {
            Assert.That((panel.X, panel.Y, panel.Width, panel.Height), Is.EqualTo((16, 40, 300, 200)));
            Assert.That(panel.Anchor, Is.EqualTo("Top,Left,Right"));
            Assert.That((button.X, button.Y, button.Width, button.Height), Is.EqualTo((8, 8, 75, 23)));
            Assert.That(button.Dock, Is.EqualTo("Bottom"));
            Assert.That(model.FindById("pnl")!.UnknownAttributes, Is.Empty, "X/Width/Anchor are geometry here, not unknown");
        });
    }

    [Test]
    public void ACellAttributeOnACanvasControl_IsAnUnknownAttribute_AndRoundTrips()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);
        var text = file.Model.FindById("txt")!;

        Assert.Multiple(() =>
        {
            Assert.That(text.Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(text.UnknownAttributes["Col"], Is.EqualTo("3"), "foreign vocabulary round-trips, no new diagnostic (§2.5)");
            Assert.That(file.Diagnostics, Is.Empty);
        });

        ((PixelGeometry)file.Model.FindById("btn")!.Geometry!).X = 10;
        var written = FormDocumentWriter.Write(file);

        Assert.Multiple(() =>
        {
            Assert.That(written, Does.Contain("X=\"10\""));
            Assert.That(written, Does.Contain("Col=\"3\""), "an unrelated edit keeps the foreign attribute");
        });
    }

    [Test]
    public void ACanvasPage_RoundTripsByteIdentical()
    {
        var file = FormDocumentReader.Read("F.blwebform", CanvasPage);

        Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(CanvasPage));
    }

    [Test]
    public void ControlsAreReadAsPixels_EvenWhenTheLayoutComesLast()
    {
        var model = FormDocumentReader.Read("F.blwebform", CanvasPageLayoutLast).Model;

        Assert.That(model.FindById("btn")!.Geometry, Is.TypeOf<PixelGeometry>(),
            "the §2.2 pre-scan decides the vocabulary before any control is read");
    }

    [Test]
    public void AGridPagesControl_StillReadsItsCell_AndKeepsAStrayXAsUnknown()
    {
        var file = FormDocumentReader.Read("F.blwebform", GridPage);
        var button = file.Model.FindById("btn")!;

        Assert.Multiple(() =>
        {
            Assert.That(button.Geometry, Is.TypeOf<GridGeometry>());
            Assert.That(button.UnknownAttributes["X"], Is.EqualTo("190"));
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(GridPage), "Grid pages are byte-for-byte unchanged");
        });
    }

    [Test]
    public void CreatingACanvasPage_WritesPixelGeometry()
    {
        var page = new FormDocument
        {
            Target = FormTarget.Web, Name = "F", Width = 640, Height = 480,
            Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        page.Controls.Add(new FormControl
        {
            Kind = "Button", Id = "btn", TabIndex = 0,
            Geometry = new PixelGeometry { X = 24, Y = 32, Width = 75, Height = 23, Anchor = "Top,Right" }
        });

        var reread = FormDocumentReader.Read("F.blwebform", FormDocumentWriter.Create(page)).Model;
        var geometry = (PixelGeometry)reread.FindById("btn")!.Geometry!;

        Assert.That((geometry.X, geometry.Y, geometry.Anchor), Is.EqualTo((24, 32, "Top,Right")));
    }

    // ==================================================================
    // The clipboard
    // ==================================================================

    private static FormControl PixelButton(string id) => new()
    {
        Kind = "Button", Id = id, TabIndex = 0,
        Geometry = new PixelGeometry { X = 40, Y = 40, Width = 75, Height = 23 }
    };

    private static FormControl CellButton(string id) => new()
    {
        Kind = "Button", Id = id, TabIndex = 0, Geometry = new GridGeometry { Col = 1, Row = 0 }
    };

    [Test]
    public void ACanvasFragment_RecordsItsLayout_AndPastesAsPixels()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { PixelButton("btn") }, FormLayoutKind.Canvas);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(xml, Does.Contain("Layout=\"Canvas\""));
            Assert.That(paste.Refusal, Is.Null);
            Assert.That(paste.Controls.Single().Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(((PixelGeometry)paste.Controls.Single().Geometry!).X, Is.EqualTo(40));
        });
    }

    [Test]
    public void AGridFragment_IntoACanvasPage_IsRefused_NamingBothLayouts()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { CellButton("btn") }, FormLayoutKind.Grid);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Does.Contain("Grid").And.Contain("Canvas"));
        });
    }

    [Test]
    public void ACanvasFragment_IntoAGridPage_IsRefused()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { PixelButton("btn") }, FormLayoutKind.Canvas);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Grid, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Does.Contain("Canvas").And.Contain("Grid"));
        });
    }

    [Test]
    public void AFragmentWrittenBeforeLayoutsWereRecorded_IsAGridFragment()
    {
        const string legacy = """
            <FormSubtree Target="Web" Version="1">
              <Button Id="btn" Col="1" Row="0" TabIndex="0"/>
            </FormSubtree>
            """;

        Assert.Multiple(() =>
        {
            Assert.That(FormClipboard.Paste(legacy, FormTarget.Web, FormLayoutKind.Grid, _ => false).Controls, Has.Count.EqualTo(1));
            Assert.That(FormClipboard.Paste(legacy, FormTarget.Web, FormLayoutKind.Canvas, _ => false).Refusal, Is.Not.Null);
        });
    }

    [Test]
    public void AWinFormsFragment_IntoACanvasPage_IsRefused_AndSaysWhy()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.WinForms, new[] { PixelButton("btn") });

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty, "the two targets have different catalogs");
            Assert.That(paste.Refusal, Does.Contain("WinForms"));
        });
    }

    [Test]
    public void ANumericLayoutName_IsNotALayout()
    {
        // ⛔ Enum.TryParse accepts "2" as Canvas. A fragment is unvetted text; a number is not a layout name.
        const string xml = """<FormSubtree Target="Web" Layout="2" Version="1"><Button Id="b" TabIndex="0"/></FormSubtree>""";

        var paste = FormClipboard.Paste(xml, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Is.Null, "a malformed fragment is not ours to explain");
        });
    }

    [Test]
    public void TheOldEntryPoint_StillPastesIntoAGridPage()
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { CellButton("btn") });

        Assert.That(FormClipboard.DeserializeSubtree(xml, FormTarget.Web, _ => false).Single().Geometry,
            Is.TypeOf<GridGeometry>());
    }

    // ⛔ The refusal is by VOCABULARY, not by layout name (review of Task 3): Grid and Flow both read
    // Col/Row into a GridGeometry, so a paste between them is lossless and was accepted before layouts
    // were recorded. Refusing it would be a regression with a false reason.
    [TestCase(FormLayoutKind.Grid, FormLayoutKind.Flow)]
    [TestCase(FormLayoutKind.Flow, FormLayoutKind.Grid)]
    public void APasteBetweenTwoCellLayouts_Lands_AndKeepsItsCell(FormLayoutKind from, FormLayoutKind into)
    {
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { CellButton("btn") }, from);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, into, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Refusal, Is.Null);
            Assert.That(paste.Controls, Has.Count.EqualTo(1));
            var cell = paste.Controls.Single().Geometry as GridGeometry;
            Assert.That(cell, Is.Not.Null);
            Assert.That((cell!.Col, cell.Row), Is.EqualTo((1, 0)));
        });
    }

    [TestCase(FormLayoutKind.Canvas, FormLayoutKind.Flow)]
    [TestCase(FormLayoutKind.Flow, FormLayoutKind.Canvas)]
    public void APasteBetweenCanvasAndFlow_IsRefused_NamingBoth(FormLayoutKind from, FormLayoutKind into)
    {
        var control = from == FormLayoutKind.Canvas ? PixelButton("btn") : CellButton("btn");
        var xml = FormClipboard.SerializeSubtree(FormTarget.Web, new[] { control }, from);

        var paste = FormClipboard.Paste(xml, FormTarget.Web, into, _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(paste.Controls, Is.Empty);
            Assert.That(paste.Refusal, Does.Contain("Canvas").And.Contain("Flow"));
        });
    }

    private const string NestedCanvasFragment = """
        <FormSubtree Target="Web" Version="1" Layout="Canvas">
          <Panel Id="pnl" TabIndex="0" X="16" Y="40" Width="300" Height="200" Anchor="Top,Left">
            <Button Id="btn" TabIndex="1" X="8" Y="8" Width="75" Height="23" Dock="Bottom" Text="Go" />
          </Panel>
        </FormSubtree>
        """;

    [Test]
    public void ANestedCanvasFragment_PastesPixelsAtEveryDepth_WithNothingLeftOverAsUnknown()
    {
        var paste = FormClipboard.Paste(NestedCanvasFragment, FormTarget.Web, FormLayoutKind.Canvas, _ => false);

        var panel = paste.Controls.Single();
        var button = panel.Children.Single();

        Assert.Multiple(() =>
        {
            Assert.That(paste.Refusal, Is.Null);
            Assert.That(panel.Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(button.Geometry, Is.TypeOf<PixelGeometry>(), "the layout reaches the NESTED control too");
            Assert.That(((PixelGeometry?)button.Geometry)?.Dock, Is.EqualTo("Bottom"));
            Assert.That(panel.UnknownAttributes, Is.Empty, "X/Width/Anchor are geometry on a Canvas paste");
            Assert.That(button.UnknownAttributes, Is.Empty, "X/Width/Dock are geometry on a Canvas paste");
        });
    }

    // ==================================================================
    // Through the real command (who calls it in a shipping build: the Paste command)
    // ==================================================================

    private const string GridPageDoc = """
        <WebForm Name="GridPage" Version="1">
          <Layout Kind="Grid" Cols="auto,1fr" Rows="auto" Gap="8px"/>
          <Controls>
            <Button Id="btnGrid" Col="0" Row="0" TabIndex="0" Text="Grid"/>
          </Controls>
        </WebForm>
        """;

    private const string PixelPageDoc = """
        <WebForm Name="PixelPage" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls>
            <Button Id="btnPixel" X="40" Y="40" Width="75" Height="23" TabIndex="0" Text="Pixel"/>
          </Controls>
        </WebForm>
        """;

    /// <summary>
    /// ⚠ The designer's copy buffer is a STATIC field shared by every view model (and so by every test in this
    /// assembly). The command tests below are correct only while tests run SERIALLY, which is how this suite
    /// runs. Each starts from an EMPTY buffer so a copy left by an earlier test cannot satisfy it.
    /// </summary>
    private static readonly System.Reflection.FieldInfo DesignerClipboard =
        typeof(CodeEditorDocumentViewModel).GetField("_designerClipboard",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
        ?? throw new InvalidOperationException("CodeEditorDocumentViewModel._designerClipboard was renamed");

    [SetUp]
    public void EmptyTheDesignerClipboard() => DesignerClipboard.SetValue(null, null);

    private static (CodeEditorDocumentViewModel Vm, List<DesignerDiagnosticsEvent> Published) OpenVm(string name, string text)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/proj/" + name + ".blwebform"] = text,
            ["/proj/" + name + ".bas"] = FormScaffolder.Create(name, FormTarget.Web).CodeText
        };

        var fs = new Mock<IFileService>();
        fs.Setup(f => f.ReadFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, CancellationToken _) => Task.FromResult(files[p]));
        fs.Setup(f => f.FileExistsAsync(It.IsAny<string>()))
            .Returns((string p) => Task.FromResult(files.ContainsKey(p)));
        fs.Setup(f => f.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string p, string t, CancellationToken _) =>
            {
                files[p] = t;
                return Task.CompletedTask;
            });

        var published = new List<DesignerDiagnosticsEvent>();
        var events = new Mock<IEventAggregator>();
        events.Setup(e => e.Publish(It.IsAny<DesignerDiagnosticsEvent>()))
            .Callback((DesignerDiagnosticsEvent e) => published.Add(e));

        var vm = new CodeEditorDocumentViewModel(fs.Object, events.Object) { FilePath = "/proj/" + name + ".blwebform" };
        vm.SetContent(text);
        return (vm, published);
    }

    // ⚠ Relies on serial execution: goes through the static designer clipboard (see DesignerClipboard).
    [Test]
    public void PastingAGridPagesControl_IntoACanvasPage_IsRefusedAndSaid_AndChangesNothing()
    {
        var (grid, _) = OpenVm("GridPage", GridPageDoc);
        grid.Selection.Set(grid.DesignDocument!.FindById("btnGrid")!);
        grid.CopyControlsCommand.Execute(null);

        Assert.That((string?)DesignerClipboard.GetValue(null), Does.Contain("Layout=\"Grid\""),
            "precondition: THIS test's copy is what is on the clipboard");

        var (canvas, published) = OpenVm("PixelPage", PixelPageDoc);
        var before = canvas.Text;

        canvas.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.DesignDocument!.Controls.Select(c => c.Id), Is.EqualTo(new[] { "btnPixel" }));
            Assert.That(canvas.Text, Is.EqualTo(before));
            Assert.That(published.SelectMany(e => e.Diagnostics).Any(d =>
                    d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Grid") && d.Message.Contains("Canvas")),
                Is.True, "a refused paste is SAID — a silent no-op is the failure this designer exists to remove");
        });
    }

    // ⚠ Relies on serial execution: goes through the static designer clipboard (see DesignerClipboard).
    [Test]
    public void PastingWithinACanvasPage_LandsInPixels_OffsetSoItIsVisible()
    {
        var (canvas, _) = OpenVm("PixelPage", PixelPageDoc);
        canvas.Selection.Set(canvas.DesignDocument!.FindById("btnPixel")!);
        canvas.CopyControlsCommand.Execute(null);

        canvas.PasteControlsCommand.Execute(null);

        var copy = canvas.DesignDocument!.Controls.Single(c => c.Id != "btnPixel");
        Assert.That(((PixelGeometry)copy.Geometry!).X, Is.EqualTo(48));
    }

    private const string NestedPixelPageDoc = """
        <WebForm Name="PixelPage" Version="1" Width="640" Height="480">
          <Layout Kind="Canvas"/>
          <Controls>
            <Panel Id="pnl" X="16" Y="40" Width="300" Height="200" TabIndex="0">
              <Button Id="btn" X="8" Y="8" Width="75" Height="23" TabIndex="1" Text="Go"/>
            </Panel>
          </Controls>
        </WebForm>
        """;

    // ⚠ Relies on serial execution: goes through the static designer clipboard (see DesignerClipboard).
    [Test]
    public void PastingANestedPanelWithinACanvasPage_WritesTheOffsetPosition_AndPixelsAtEveryDepth()
    {
        var (canvas, _) = OpenVm("PixelPage", NestedPixelPageDoc);
        canvas.Selection.Set(canvas.DesignDocument!.FindById("pnl")!);
        canvas.CopyControlsCommand.Execute(null);

        canvas.PasteControlsCommand.Execute(null);

        // What reached the FILE: the pasted panel's element, read as raw XML so a stale X carried as an
        // unknown attribute (and written over the offset one) is visible.
        var written = System.Xml.Linq.XDocument.Parse(canvas.Text);
        var pastedPanel = written.Descendants("Panel").Single(e => (string?)e.Attribute("Id") != "pnl");
        var pastedButton = pastedPanel.Elements("Button").Single();
        var copy = canvas.DesignDocument!.Controls.Single(c => c.Id != "pnl");

        Assert.Multiple(() =>
        {
            Assert.That((string?)pastedPanel.Attribute("X"), Is.EqualTo("24"), "16 + the paste offset of 8, not the stale 16");
            Assert.That((string?)pastedPanel.Attribute("Y"), Is.EqualTo("48"), "40 + the paste offset of 8, not the stale 40");
            Assert.That((string?)pastedButton.Attribute("X"), Is.EqualTo("8"), "a child keeps its position in its parent");
            Assert.That(copy.Children.Single().Geometry, Is.TypeOf<PixelGeometry>());
            Assert.That(copy.UnknownAttributes, Is.Empty);
            Assert.That(copy.Children.Single().UnknownAttributes, Is.Empty);
        });
    }
}
