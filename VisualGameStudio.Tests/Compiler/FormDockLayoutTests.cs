using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.Controls;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §3/§4/§7 — the ONE resolver of where every DOCKED thing sits: strips (their Dock property)
/// and docked controls (PixelGeometry.Dock), as ONE sequence in DOCUMENT order, which is WinForms' docking
/// order (back-most docks first). Pure; the canvas (Task 9) and the page (Task 10) both call it.
/// ⚠ The WinForms reference harness (Task 12/13) is the arbiter of every row here; where it disagrees, it wins.
/// </summary>
[TestFixture]
public class FormDockLayoutTests
{
    private static FormControl Strip(string kind, string id, string? dock = null)
    {
        var strip = new FormControl { Kind = kind, Id = id };
        if (dock != null)
        {
            strip.Properties["Dock"] = dock;
        }

        return strip;
    }

    private static FormControl Box(string id, int width, int height, string? dock, int x = 0, int y = 0) => new()
    {
        Kind = "Panel", Id = id,
        Geometry = new PixelGeometry { X = x, Y = y, Width = width, Height = height, Dock = dock }
    };

    private static FormDocument Window(params FormControl[] controls)
    {
        var document = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300 };
        document.Controls.AddRange(controls);
        return document;
    }

    private static int Band(string kind) => FormControlCatalog.Find(kind)!.DefaultHeight;

    private static FormRect At(FormDocument document, string id) =>
        FormDockLayout.Resolve(document).All.Single(d => d.Control.Id == id).Bounds;

    [Test]
    public void TheBandHeights_AreTheHeightsTheCanvasDraws()
    {
        Assert.That((Band("MenuStrip"), Band("ToolStrip"), Band("StatusStrip")), Is.EqualTo((24, 25, 22)),
            "spec §3: MenuStrip 24, ToolStrip 25, StatusStrip 22 (FormControlCatalog.cs:1808/1833/1854)");
    }

    [TestCase("Top", 0, 0, 400, 50)]
    [TestCase("Bottom", 0, 250, 400, 50)]
    [TestCase("Left", 0, 0, 80, 300)]
    [TestCase("Right", 320, 0, 80, 300)]
    [TestCase("Fill", 0, 0, 400, 300)]
    [TestCase("fill", 0, 0, 400, 300)]
    public void EachDockValue_Alone(string dock, int x, int y, int width, int height)
    {
        var document = Window(Box("p", 80, 50, dock));

        Assert.That(At(document, "p"), Is.EqualTo(new FormRect(x, y, width, height)));
    }

    [Test]
    public void Strips_StackFromTheirEdge_InDocumentOrder_AtTheirBandHeights()
    {
        var document = Window(
            Strip("MenuStrip", "menu"), Strip("ToolStrip", "tools"),
            Strip("StatusStrip", "status"), Strip("StatusStrip", "status2"));

        Assert.Multiple(() =>
        {
            Assert.That(At(document, "menu"), Is.EqualTo(new FormRect(0, 0, 400, 24)));
            Assert.That(At(document, "tools"), Is.EqualTo(new FormRect(0, 24, 400, 25)));
            Assert.That(At(document, "status"), Is.EqualTo(new FormRect(0, 278, 400, 22)),
                "the FIRST-documented bottom strip docks first, so it is ON the edge");
            Assert.That(At(document, "status2"), Is.EqualTo(new FormRect(0, 256, 400, 22)));
        });
    }

    [Test]
    public void AStripWithNoDockAttribute_TakesItsRowsDefaultEdge()
    {
        var document = Window(Strip("StatusStrip", "status"));

        Assert.That(At(document, "status").Y, Is.EqualTo(300 - Band("StatusStrip")), "StatusStrip's row default is Bottom");
    }

    [Test]
    public void AFill_BetweenAMenuAndAStatusStrip_TakesWhatIsLeft()
    {
        var document = Window(Strip("MenuStrip", "menu"), Strip("StatusStrip", "status"), Box("fill", 10, 10, "Fill"));

        Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(0, 24, 400, 300 - 24 - 22)),
            "spec §4's example: top:24; left:0; right:0; bottom:22");
    }

    [Test]
    public void ADockTopPanel_BeforeTheMenuStrip_TakesTheTopEdge_AndTheMenuSitsBelowIt()
    {
        var document = Window(Box("p", 10, 50, "Top"), Strip("MenuStrip", "menu"));

        Assert.Multiple(() =>
        {
            Assert.That(At(document, "p"), Is.EqualTo(new FormRect(0, 0, 400, 50)));
            Assert.That(At(document, "menu"), Is.EqualTo(new FormRect(0, 50, 400, 24)),
                "document order IS docking order — on the canvas, the page and in WinForms alike (spec §4)");
        });
    }

    [Test]
    public void ALeftDock_ThenATopDock_TheTopStartsRightOfTheLeft()
    {
        var document = Window(Box("left", 80, 10, "Left"), Box("top", 10, 40, "Top"));

        Assert.That(At(document, "top"), Is.EqualTo(new FormRect(80, 0, 320, 40)));
    }

    [Test]
    public void AFill_DoesNotConsume_SoALaterTopOverlapsIt()
    {
        // ⚠ WinForms' DefaultLayout: Fill takes the remaining rectangle and leaves it unchanged (S9).
        var document = Window(Box("fill", 10, 10, "Fill"), Box("top", 10, 40, "Top"));

        Assert.Multiple(() =>
        {
            Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(0, 0, 400, 300)));
            Assert.That(At(document, "top"), Is.EqualTo(new FormRect(0, 0, 400, 40)));
        });
    }

    [Test]
    public void UndockedControls_AreNotResolved_AndConsumeNothing()
    {
        var document = Window(Box("free", 100, 100, null, x: 5, y: 5), Box("none", 10, 10, "None"),
            Box("bogus", 10, 10, "Middle"), Box("top", 10, 40, "Top"));
        var ids = FormDockLayout.Resolve(document).All.Select(d => d.Control.Id).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.EqualTo(new[] { "top" }));
            Assert.That(At(document, "top").Y, Is.Zero);
        });
    }

    [Test]
    public void Overflow_ClampsWhatIsLeft_AtZero()
    {
        var document = Window(Box("tall", 10, 400, "Top"), Box("fill", 10, 10, "Fill"));

        Assert.That(At(document, "fill").Height, Is.Zero);
    }

    [Test]
    public void ADockedChild_DocksInsideItsContainer_AtTheContainersResolvedSize()
    {
        var panel = Box("pnl", 10, 10, "Fill");
        panel.Children.Add(new FormControl
        {
            Kind = "Button", Id = "ok",
            Geometry = new PixelGeometry { X = 3, Y = 3, Width = 75, Height = 30, Dock = "Bottom" }
        });
        var document = Window(Strip("MenuStrip", "menu"), panel);
        var ok = FormDockLayout.Resolve(document).All.Single(d => d.Control.Id == "ok");

        Assert.Multiple(() =>
        {
            Assert.That(ok.Bounds, Is.EqualTo(new FormRect(0, 276 - 30, 400, 30)), "relative to the panel's client origin");
            Assert.That((ok.ContainerWidth, ok.ContainerHeight), Is.EqualTo((400, 276)),
                "the panel's own resolved Fill size, not its stored 10x10");
        });
    }

    [Test]
    public void TryGet_FindsExactlyTheDockedControls()
    {
        var document = Window(Box("free", 10, 10, null), Box("top", 10, 40, "Top"));
        var layout = FormDockLayout.Resolve(document);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(document.Controls[1], out var top), Is.True);
            Assert.That(top.Edge, Is.EqualTo(FormDockEdge.Top));
            Assert.That(layout.TryGet(document.Controls[0], out _), Is.False);
        });
    }

    [Test]
    public void EdgeOf_IsTheOneAnswer_ForAStripAndForADockedControl()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormDockLayout.EdgeOf(Strip("MenuStrip", "m")), Is.EqualTo(FormDockEdge.Top));
            Assert.That(FormDockLayout.EdgeOf(Strip("MenuStrip", "m", "Bottom")), Is.EqualTo(FormDockEdge.Bottom));
            Assert.That(FormDockLayout.EdgeOf(Box("p", 1, 1, " Right ")), Is.EqualTo(FormDockEdge.Right));
            Assert.That(FormDockLayout.EdgeOf(Box("p", 1, 1, null)), Is.Null);
            Assert.That(FormDockLayout.EdgeOf(new FormControl { Kind = "Timer", Id = "t" }), Is.Null, "a component has no place");
        });
    }

    [Test]
    public void TheDesignSize_IsWhatTheCanvasSurfaceSizeReports()
    {
        var documents = new[]
        {
            new FormDocument { Target = FormTarget.WinForms, Name = "A", Width = 640, Height = 480 },
            new FormDocument { Target = FormTarget.Web, Name = "B" },
            new FormDocument { Target = FormTarget.WinForms, Name = "C", Width = 0, Height = -5 },
        };

        Assert.Multiple(() =>
        {
            foreach (var document in documents)
            {
                var surface = FormCanvasTransform.SurfaceSize(document);
                Assert.That(((int)surface.Width, (int)surface.Height), Is.EqualTo(document.DesignSize),
                    $"{document.Name}: ONE answer to how big the form is (spec §2.4)");
            }

            Assert.That(documents[1].DesignSize, Is.EqualTo((FormDocument.DefaultDesignWidth, FormDocument.DefaultDesignHeight)));
        });
    }

    [Test]
    public void ResolvingAPageWithNoSize_UsesTheDesignSizeFallback()
    {
        var document = new FormDocument
        {
            Target = FormTarget.Web, Name = "P", Layout = new FormLayout { Kind = FormLayoutKind.Canvas }
        };
        document.Controls.Add(Box("fill", 1, 1, "Fill"));

        Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(0, 0, FormDocument.DefaultDesignWidth, FormDocument.DefaultDesignHeight)));
    }
}
