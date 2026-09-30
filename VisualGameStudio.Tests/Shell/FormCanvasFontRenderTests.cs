using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Documents;
using VisualGameStudio.Shell.Views.Documents;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// ⛔ Owner click-through D1 (2026-09-30): the designer CANVAS ignored Font. Changing a control's size / Bold / Italic,
/// or a Form Font the controls inherit, worked in the running app while the canvas kept drawing the old caption. The
/// canvas now draws each caption with the control's EFFECTIVE font — its own, else the nearest container's, else the
/// Form's (<see cref="FormAmbient.Inherited"/>, the one rule the property grid's Font parts use) — in points at the
/// canvas zoom. Inherited ForeColor and the Form's BackColor were ignored the same way and are drawn too.
///
/// <para>⛔ CLAUDE.md's canvas rules hold here: every compared document has the SAME control id and caption, so two
/// frames can differ only by what is under test (a font, a colour); every render is at a zoom ≠ 1.0 (asserted); pixels
/// come from the real control through Skia under <c>[AvaloniaTest]</c>.</para>
/// </summary>
[TestFixture]
public class FormCanvasFontRenderTests
{
    private const string SharedId = "X";
    private const string SharedText = "Caption";
    private const int FormWidth = 200;
    private const int FormHeight = 100;

    /// <summary>The zoom every render here is drawn at — ≠ 1.0 (Fit never zooms a form above 1:1, so below it).</summary>
    private const double Zoom = 0.75;

    /// <summary>Fit's margin is 24 a side: this viewport fits the 200×100 form at exactly <see cref="Zoom"/>.</summary>
    private static readonly Size Viewport = new((FormWidth * Zoom) + 48, (FormHeight * Zoom) + 48);

    /// <summary>Points to canvas pixels at 96 DPI, before the zoom.</summary>
    private static double Px(double points) => points * 96 / 72;

    private static FormDocument Doc(string? labelFont = null, string? formFont = null)
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "T", Width = FormWidth, Height = FormHeight };
        var label = new FormControl
        {
            Kind = "Label", Id = SharedId, TabIndex = 0,
            Geometry = new PixelGeometry { X = 10, Y = 10, Width = 180, Height = 60 }
        };
        label.Properties["Text"] = SharedText;
        if (labelFont != null) label.Properties["Font"] = labelFont;
        if (formFont != null) doc.Properties["Font"] = formFont;
        doc.Controls.Add(label);
        return doc;
    }

    private static FormCanvasControl.DocumentCaptions Render(FormDocument doc)
    {
        var frame = FormCanvasControl.RenderDocumentForTest(doc, selected: null, typeHereHost: null, Viewport);
        Assert.That(frame.Zoom, Is.EqualTo(Zoom).Within(1e-9), "the fixture must render at a zoom other than 1.0");
        return frame;
    }

    private static FormCanvasControl.CaptionDraw CaptionOf(FormDocument doc, string id = SharedId) =>
        Render(doc).Captions.Single(c => c.Control?.Id == id).Drawn
        ?? throw new InvalidOperationException($"'{id}' drew no caption");

    /// <summary>The canvas's pixels alone (a canvas rendered to its own bitmap), hashed.</summary>
    private static string Hash(FormDocument doc)
    {
        var canvas = new FormCanvasControl { Document = doc };
        return HashOf(canvas, Viewport);
    }

    private static string HashOf(FormCanvasControl canvas, Size size)
    {
        canvas.Measure(size);
        canvas.Arrange(new Rect(canvas.Bounds.Position, size));
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96));
        bitmap.Render(canvas);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    [AvaloniaTest]
    public void ABoldLabel_DrawsDifferentlyFromARegularOne()
    {
        var regular = Doc(labelFont: "Arial, 10pt");
        var bold = Doc(labelFont: "Arial, 10pt, style=Bold");

        Assert.That(Hash(bold), Is.Not.EqualTo(Hash(regular)), "same id, same caption: only the Bold can differ");
    }

    [AvaloniaTest]
    public void AnItalicAndAnUnderlinedLabel_DrawDifferentlyFromARegularOne()
    {
        var regular = Hash(Doc(labelFont: "Arial, 10pt"));

        Assert.Multiple(() =>
        {
            Assert.That(Hash(Doc(labelFont: "Arial, 10pt, style=Italic")), Is.Not.EqualTo(regular));
            Assert.That(Hash(Doc(labelFont: "Arial, 10pt, style=Underline")), Is.Not.EqualTo(regular));
            Assert.That(Hash(Doc(labelFont: "Arial, 10pt, style=Strikeout")), Is.Not.EqualTo(regular));
        });
    }

    /// <summary>The caption is shaped at the font's POINT size, in canvas pixels, times the canvas zoom.</summary>
    [AvaloniaTest]
    public void ALabelsCaption_IsShapedAtItsPointSize_AtTheCanvasZoom()
    {
        var drawn = CaptionOf(Doc(labelFont: "Arial, 18pt"));

        Assert.That(drawn.FontSize, Is.EqualTo(Px(18) * Zoom).Within(1e-9));
    }

    /// <summary>A Form Font the label inherits changes the LABEL's frame — and its caption is shaped at that size.</summary>
    [AvaloniaTest]
    public void AnInheritedFormFont_ChangesTheChildsFrame()
    {
        var plain = Doc();
        var inherited = Doc(formFont: "Arial, 16pt, style=Bold");

        Assert.Multiple(() =>
        {
            Assert.That(Hash(inherited), Is.Not.EqualTo(Hash(plain)));
            Assert.That(CaptionOf(inherited).FontSize, Is.EqualTo(Px(16) * Zoom).Within(1e-9));
        });
    }

    /// <summary>The nearest container's font wins over the Form's — the same rule as the grid's Font parts.</summary>
    [AvaloniaTest]
    public void TheNearestContainersFont_WinsOverTheForms()
    {
        var doc = new FormDocument { Target = FormTarget.WinForms, Name = "T", Width = FormWidth, Height = FormHeight };
        doc.Properties["Font"] = "Arial, 8pt";
        var group = new FormControl
        {
            Kind = "GroupBox", Id = "G", TabIndex = 0,
            Geometry = new PixelGeometry { X = 5, Y = 5, Width = 190, Height = 90 }
        };
        group.Properties["Font"] = "Arial, 20pt";
        var button = new FormControl
        {
            Kind = "Button", Id = SharedId, TabIndex = 0,
            Geometry = new PixelGeometry { X = 10, Y = 30, Width = 160, Height = 50 }
        };
        button.Properties["Text"] = SharedText;
        group.Children.Add(button);
        doc.Controls.Add(group);

        Assert.That(CaptionOf(doc).FontSize, Is.EqualTo(Px(20) * Zoom).Within(1e-9));
    }

    /// <summary>
    /// With no Font anywhere up the chain, a caption is drawn exactly as before — the fixed schematic size — so the
    /// pinned caption arithmetic of every existing form is unchanged.
    /// </summary>
    [AvaloniaTest]
    public void WithNoFontAnywhere_ACaptionKeepsTheSchematicSize()
    {
        Assert.That(CaptionOf(Doc()).FontSize, Is.EqualTo(FormCanvasTransform.CaptionFontSize).Within(1e-9));
    }

    /// <summary>A Form ForeColor inks a child's caption (ForeColor is ambient on a Label), as the running form does.</summary>
    [AvaloniaTest]
    public void AnInheritedFormForeColor_InksTheChildsCaption()
    {
        var plain = Doc();
        var red = Doc();
        red.Properties["ForeColor"] = "#C00000";

        Assert.That(Hash(red), Is.Not.EqualTo(Hash(plain)));
    }

    /// <summary>The Form's BackColor fills the form surface.</summary>
    [AvaloniaTest]
    public void AFormBackColor_FillsTheFormSurface()
    {
        var plain = Doc();
        var yellow = Doc();
        yellow.Properties["BackColor"] = "#FFFF00";

        Assert.That(Hash(yellow), Is.Not.EqualTo(Hash(plain)));
    }

    // ==================================================================
    // Through the REAL view: an edit in the real grid redraws the real canvas
    // ==================================================================

    private const string LabelDoc = """
        <Form Name="F" Version="1" Width="1600" Height="1200" Text="F">
          <Controls>
            <Label Id="X" Text="Caption" X="16" Y="16" Width="200" Height="40" TabIndex="0"/>
          </Controls>
        </Form>
        """;

    /// <summary>
    /// ⛔ The owner's exact steps, headless: the real <see cref="CodeEditorDocumentView"/> on a real view model, the Form
    /// Font set through the real grid (nothing selected), then the Label's Bold PART. After each edit the canvas the view
    /// hosts is re-rendered and must differ — and its <c>ModelRevision</c> must have moved, which is what repaints it live.
    /// </summary>
    [AvaloniaTest]
    public void EditingFontsThroughTheRealGrid_RedrawsTheRealCanvas()
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, new Mock<IEventAggregator>().Object)
        {
            FilePath = "/proj/F.blform"
        };
        vm.SetContent(LabelDoc);
        Assert.That(vm.EnterDesignModeForFormDocument(), Is.True);

        var view = new CodeEditorDocumentView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        var canvas = view.FindControl<FormCanvasControl>("DesignCanvas")
                     ?? throw new InvalidOperationException("DesignCanvas not found");
        var size = canvas.Bounds.Size;
        Assert.That(FormCanvasControl.Fit(vm.DesignDocument!, size).Zoom, Is.Not.EqualTo(1.0).Within(1e-6),
            "the real view must render at a zoom other than 1.0");

        string Frame()
        {
            Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(
                new PixelSize((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height)), new Vector(96, 96));
            bitmap.Render(canvas);
            using var stream = new MemoryStream();
            bitmap.Save(stream);
            return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
        }

        var before = Frame();
        var revision = canvas.ModelRevision;

        vm.Selection.Set(null);
        vm.PropertyGrid.Rows.Single(r => r.Name == "Font").StringValue = "Arial, 14pt";
        var afterFormFont = Frame();

        var label = vm.DesignDocument!.FindById("X")!;
        vm.Selection.Set(label);
        vm.PropertyGrid.Rows.Single(r => r.Name == "Font").Children.Single(c => c.Name == "Bold").BoolValue = true;
        var afterBold = Frame();

        Assert.Multiple(() =>
        {
            Assert.That(canvas.ModelRevision, Is.Not.EqualTo(revision), "an edit must move the revision that repaints the canvas");
            Assert.That(afterFormFont, Is.Not.EqualTo(before), "the Form Font the Label inherits must reach the canvas");
            Assert.That(afterBold, Is.Not.EqualTo(afterFormFont), "the Label's Bold must reach the canvas");
            Assert.That(label.Properties["Font"], Is.EqualTo("Arial, 14pt, style=Bold"), "the part started from the inherited font");
        });
    }
}
