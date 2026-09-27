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

    // One row per edge: the first docked box overflows what is left on its axis, and the Fill after it must be
    // handed a 0-size rectangle on that axis, never a negative one (S9: the remainder itself stays unclamped, so
    // a later far-edge dock measures from the true edge; only the size HANDED to a control clamps at 0).
    [TestCase("Top", 10, 400, 0, 400, 400, 0)]
    [TestCase("Bottom", 10, 400, 0, 0, 400, 0)]
    [TestCase("Left", 500, 10, 500, 0, 0, 300)]
    [TestCase("Right", 500, 10, 0, 0, 0, 300)]
    public void Overflow_OnEachEdge_LeavesAZeroRemainder_NeverANegativeOne(
        string dock, int width, int height, int x, int y, int fillWidth, int fillHeight)
    {
        var document = Window(Box("big", width, height, dock), Box("fill", 10, 10, "Fill"));

        Assert.That(At(document, "fill"), Is.EqualTo(new FormRect(x, y, fillWidth, fillHeight)),
            $"a Dock={dock} box larger than the form leaves nothing — clamped at 0 on its axis");
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

    // The fallback's VALUE, per axis and independently: the comparison above agrees by construction (SurfaceSize
    // delegates), so it cannot see a changed fallback on its own.
    [TestCase(null, 200, 400, 200)]
    [TestCase(0, 200, 400, 200)]
    [TestCase(-5, 200, 400, 200)]
    [TestCase(200, null, 200, 300)]
    [TestCase(200, 0, 200, 300)]
    [TestCase(200, -5, 200, 300)]
    [TestCase(null, null, 400, 300)]
    [TestCase(1, 1, 1, 1)]
    public void TheDesignSize_FallsBackTo400By300_OnlyOnTheAxisThatIsMissingOrNotPositive(
        int? width, int? height, int expectedWidth, int expectedHeight)
    {
        var document = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = width, Height = height };
        var surface = FormCanvasTransform.SurfaceSize(document);

        Assert.Multiple(() =>
        {
            Assert.That(document.DesignSize, Is.EqualTo((expectedWidth, expectedHeight)));
            Assert.That((surface.Width, surface.Height), Is.EqualTo(((double)expectedWidth, (double)expectedHeight)),
                "the canvas surface reports the same number");
        });
    }

    // ---- Task 6 review: far-edge docking (the remainder is NOT clamped; only a size handed out is) ----

    [Test]
    public void AnOverflowingTop_ThenABottom_TheBottomMeasuresFromTheTrueFarEdge()
    {
        // WinForms' DefaultLayout: remainingBounds.Height -= element.Bounds.Height, unclamped — so the Bottom
        // is placed from the form's real bottom edge (300 - 50), not from where the overflowing Top ended.
        var document = Window(Box("tall", 10, 400, "Top"), Box("foot", 10, 50, "Bottom"));

        Assert.That(At(document, "foot"), Is.EqualTo(new FormRect(0, 250, 400, 50)));
    }

    [Test]
    public void AnOverflowingLeft_ThenARight_TheRightMeasuresFromTheTrueFarEdge()
    {
        var document = Window(Box("wide", 500, 10, "Left"), Box("side", 50, 10, "Right"));

        Assert.That(At(document, "side"), Is.EqualTo(new FormRect(350, 0, 50, 300)));
    }

    // A second far-edge dock after an overflowing one keeps measuring from the unclamped remainder: its
    // position may go negative (off the form), its size never does.
    [TestCase("Bottom", 10, 400, 0, -140, 400, 40)]
    [TestCase("Right", 500, 10, -150, 0, 50, 300)]
    public void ASecondFarEdgeDock_AfterAnOverflowingOne_MeasuresFromTheUnclampedRemainder(
        string dock, int firstWidth, int firstHeight, int x, int y, int width, int height)
    {
        var document = Window(Box("big", firstWidth, firstHeight, dock), Box("next", 50, 40, dock));

        Assert.That(At(document, "next"), Is.EqualTo(new FormRect(x, y, width, height)));
    }

    [TestCase("Left", 500, 10, "Top", 500, 0, 0, 40)]
    [TestCase("Right", 500, 10, "Bottom", 0, 260, 0, 40)]
    [TestCase("Top", 10, 400, "Left", 0, 400, 50, 0)]
    [TestCase("Bottom", 10, 400, "Right", 350, 0, 50, 0)]
    public void AfterAnOverflowOnOneAxis_TheSizeHandedAcrossIt_IsZero_NeverNegative(
        string firstDock, int firstWidth, int firstHeight, string secondDock, int x, int y, int width, int height)
    {
        var document = Window(Box("big", firstWidth, firstHeight, firstDock), Box("next", 50, 40, secondDock));

        Assert.That(At(document, "next"), Is.EqualTo(new FormRect(x, y, width, height)));
    }

    // ---- Task 6 review: Visible=false in Runtime mode ----

    private static FormControl Hidden(FormControl control)
    {
        control.Properties["Visible"] = "False";
        return control;
    }

    [Test]
    public void InDesignerMode_AHiddenTop_IsStillDocked_AndTheNextTopSitsBelowIt()
    {
        var document = Window(Hidden(Box("ghost", 10, 40, "Top")), Box("top", 10, 30, "Top"));
        var layout = FormDockLayout.Resolve(document);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(document.Controls[0], out var ghost), Is.True, "the canvas shows hidden controls");
            Assert.That(ghost.Bounds, Is.EqualTo(new FormRect(0, 0, 400, 40)));
            Assert.That(At(document, "top"), Is.EqualTo(new FormRect(0, 40, 400, 30)));
        });
    }

    [Test]
    public void InRuntimeMode_AHiddenTop_IsSkipped_AndTheNextTopTakesItsPlace()
    {
        var document = Window(Hidden(Box("ghost", 10, 40, "Top")), Box("top", 10, 30, "Top"));
        var layout = FormDockLayout.Resolve(document, FormDockMode.Runtime);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(document.Controls[0], out _), Is.False,
                "WinForms skips a hidden control when docking (ParticipatesInLayout); the page emits display:none");
            Assert.That(layout.TryGet(document.Controls[1], out var top), Is.True);
            Assert.That(top.Bounds, Is.EqualTo(new FormRect(0, 0, 400, 30)));
        });
    }

    [Test]
    public void InRuntimeMode_AHiddenStrip_IsSkipped_AndConsumesNothing()
    {
        var document = Window(Hidden(Strip("MenuStrip", "menu")), Box("fill", 10, 10, "Fill"));
        var layout = FormDockLayout.Resolve(document, FormDockMode.Runtime);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(document.Controls[0], out _), Is.False);
            Assert.That(layout.TryGet(document.Controls[1], out var fill), Is.True);
            Assert.That(fill.Bounds, Is.EqualTo(new FormRect(0, 0, 400, 300)));
        });
    }

    [Test]
    public void InRuntimeMode_AHiddenContainersChildren_AreNotResolved()
    {
        var panel = Hidden(Box("pnl", 200, 100, null));
        var child = Box("c", 10, 20, "Top");
        panel.Children.Add(child);
        var layout = FormDockLayout.Resolve(Window(panel), FormDockMode.Runtime);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(child, out _), Is.False, "an invisible container's children are invisible too");
            Assert.That(layout.TryGetClientSize(panel, out _), Is.False);
        });
    }

    [Test]
    public void InRuntimeMode_AHiddenTop_InsideAVisiblePanel_IsSkipped_AndTheNextTopTakesItsPlace()
    {
        // The mode reaches every depth, not only the root's sibling list.
        var panel = Box("pnl", 200, 100, null, x: 10, y: 10);
        var ghost = Hidden(Box("ghost", 10, 40, "Top"));
        var top = Box("top", 10, 30, "Top");
        panel.Children.Add(ghost);
        panel.Children.Add(top);
        var layout = FormDockLayout.Resolve(Window(panel), FormDockMode.Runtime);

        Assert.Multiple(() =>
        {
            Assert.That(layout.TryGet(ghost, out _), Is.False);
            Assert.That(layout.TryGet(top, out var resolved), Is.True);
            Assert.That(resolved.Bounds, Is.EqualTo(new FormRect(0, 0, 200, 30)));
        });
    }

    [Test]
    public void AVisibleTrue_OrAnUnparseableVisible_IsNotHidden_InRuntimeMode()
    {
        var shown = Box("shown", 10, 10, "Top");
        shown.Properties["Visible"] = "True";
        var junk = Box("junk", 10, 10, "Top");
        junk.Properties["Visible"] = "nope";
        var layout = FormDockLayout.Resolve(Window(shown, junk), FormDockMode.Runtime);

        Assert.That(layout.All.Select(d => d.Control.Id), Is.EqualTo(new[] { "shown", "junk" }),
            "the same Bool rule the page's display:none uses — only a parsed false hides");
    }

    // ---- Task 6 review: a container's client size, one source ----

    [Test]
    public void ClientSizeOf_ADockedPanel_IsItsResolvedSize()
    {
        var panel = Box("pnl", 10, 10, "Fill");
        panel.Children.Add(Box("c", 10, 10, null));
        var layout = FormDockLayout.Resolve(Window(Strip("MenuStrip", "menu"), panel));

        Assert.That(layout.ClientSizeOf(panel), Is.EqualTo((400, 276)));
    }

    [Test]
    public void ClientSizeOf_AnUndockedPanel_IsItsStoredSize()
    {
        var panel = Box("pnl", 200, 100, null, x: 10, y: 10);
        panel.Children.Add(Box("c", 10, 10, null));
        var layout = FormDockLayout.Resolve(Window(panel));

        Assert.That(layout.ClientSizeOf(panel), Is.EqualTo((200, 100)));
    }

    [Test]
    public void TheRootsClientSize_IsTheDesignSize()
    {
        var document = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 640, Height = null };

        Assert.That(FormDockLayout.Resolve(document).RootClientSize, Is.EqualTo(document.DesignSize));
    }

    // ---- Task 6 review: the common shape, order of All, the public entry's client clamp ----

    [Test]
    public void ADockedChild_InsideAnUndockedPanel_DocksAgainstThePanelsStoredSize()
    {
        var panel = Box("pnl", 200, 100, null, x: 20, y: 20);
        panel.Children.Add(new FormControl
        {
            Kind = "Button", Id = "ok",
            Geometry = new PixelGeometry { X = 3, Y = 3, Width = 75, Height = 30, Dock = "Bottom" }
        });
        var ok = FormDockLayout.Resolve(Window(panel)).All.Single(d => d.Control.Id == "ok");

        Assert.Multiple(() =>
        {
            Assert.That(ok.Bounds, Is.EqualTo(new FormRect(0, 70, 200, 30)));
            Assert.That((ok.ContainerWidth, ok.ContainerHeight), Is.EqualTo((200, 100)));
        });
    }

    [Test]
    public void All_ListsASiblingListsDockedControls_BeforeAnyContainersChildren()
    {
        var panel = Box("pnl", 10, 50, "Top");
        panel.Children.Add(Box("child", 10, 10, "Bottom"));
        var document = Window(panel, Box("second", 10, 20, "Top"));

        Assert.That(FormDockLayout.Resolve(document).All.Select(d => d.Control.Id),
            Is.EqualTo(new[] { "pnl", "second", "child" }));
    }

    [Test]
    public void ResolveSiblings_ANegativeClientSize_IsTreatedAsZero()
    {
        var fill = FormDockLayout.ResolveSiblings(new[] { Box("fill", 10, 10, "Fill") }, -10, -20).Single();

        Assert.Multiple(() =>
        {
            Assert.That(fill.Bounds, Is.EqualTo(new FormRect(0, 0, 0, 0)));
            Assert.That((fill.ContainerWidth, fill.ContainerHeight), Is.EqualTo((0, 0)));
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
