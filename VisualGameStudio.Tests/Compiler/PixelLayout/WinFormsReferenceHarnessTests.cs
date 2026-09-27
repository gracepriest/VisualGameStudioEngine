using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// ⛔⛔ Task 12 — the real WinForms window, measured (spec 2026-09-27 §7). Every fixture of
/// <see cref="PixelLayoutFixtures"/> is retargeted, built into ONE WinForms program and run ONCE in
/// <see cref="MeasureEverything"/>; each test then reads its form's snapshots.
///
/// <para>The WinForms window is the ARBITER of <see cref="FormDockLayout"/>'s reading of WinForms (plan scope call
/// S9: "as read, not yet run"). The docking tests hold the window to the resolver with ZERO tolerance.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class WinFormsReferenceHarnessTests
{
    private string _dir = "";
    private IReadOnlyDictionary<string, WinFormsReference> _measured = new Dictionary<string, WinFormsReference>();

    [OneTimeSetUp]
    public void MeasureEverything()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bl-wfref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _measured = WinFormsReferenceHarness.Measure(_dir,
            new ReferenceFixture(PixelLayoutFixtures.SelfTest(), new ResizeStep("wide", 600, 300)),
            new ReferenceFixture(PixelLayoutFixtures.Anchors(),
                new ResizeStep("grow", 601, 401), new ResizeStep("shrink", 341, 251)),
            new ReferenceFixture(PixelLayoutFixtures.DockStrips(), new ResizeStep("grow", 600, 400)),
            new ReferenceFixture(PixelLayoutFixtures.TopBeforeMenu()),
            new ReferenceFixture(PixelLayoutFixtures.OverflowV()),
            new ReferenceFixture(PixelLayoutFixtures.OverflowH()),
            new ReferenceFixture(PixelLayoutFixtures.HiddenDock(),
                new VisibilityStep("showA", "pnlA", true), new VisibilityStep("hideA", "pnlA", false)),
            new ReferenceFixture(PixelLayoutFixtures.Bordered()),
            new ReferenceFixture(PixelLayoutFixtures.HiddenBox()),
            new ReferenceFixture(PixelLayoutFixtures.DockedBox(), new ResizeStep("grow", 500, 360)),
            new ReferenceFixture(PixelLayoutFixtures.DockedAnchor(), new ResizeStep("grow", 500, 360)),
            new ReferenceFixture(PixelLayoutFixtures.Strips("StripsPinned")),
            new ReferenceFixture(PixelLayoutFixtures.Strips("StripsAuto"), Array.Empty<ReferenceStep>(), PinStrips: false));
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        if (Environment.GetEnvironmentVariable("BL_KEEP_ACCEPTANCE") == "1")
        {
            TestContext.Out.WriteLine($"[kept] {_dir}");
            return;
        }

        try { Directory.Delete(_dir, true); } catch { }
    }

    private LayoutSnapshot Snap(string form, string label) => _measured[form][label];

    private static void Record(string title, LayoutSnapshot snapshot)
    {
        TestContext.Out.WriteLine($"[{title}] {snapshot.Label} client {snapshot.ClientWidth}x{snapshot.ClientHeight}");
        foreach (var (id, box) in snapshot.Controls.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"    {id,-12} {LayoutComparison.Show(box)}");
        }
    }

    /// <summary>
    /// The window against the model, exactly. <paramref name="clientSize"/> null: the model at its own design size;
    /// otherwise re-docked and re-anchored at that size (<see cref="PixelLayoutModel.Rects"/>).
    /// </summary>
    private void AssertMatchesModel(string form, string label, FormDocument model, (int Width, int Height)? clientSize = null)
    {
        var snapshot = Snap(form, label);
        Record(form, snapshot);
        var differences = LayoutComparison.Differences(
            PixelLayoutModel.Rects(model, FormDockMode.Runtime, clientSize), snapshot.Controls, 0);
        Assert.That(differences, Is.Empty,
            $"{form} '{label}': the WinForms window disagrees with the model:\n  " + string.Join("\n  ", differences));
    }

    // ================================================================== the harness proves itself

    [Test]
    public void SelfTest_TheDesignSize_IsTheStoredGeometry_ToThePixel()
    {
        AssertMatchesModel("SelfTest", "design", PixelLayoutFixtures.SelfTest());
    }

    [Test]
    public void SelfTest_TwoHundredWider_TheRightAnchoredControlMovesTwoHundred_TheOtherStays()
    {
        var wide = Snap("SelfTest", "wide");
        Record("SelfTest", wide);

        Assert.Multiple(() =>
        {
            Assert.That(wide.Controls["p2"], Is.EqualTo(new LayoutBox(480, 20, 100, 50)));
            Assert.That(wide.Controls["p1"], Is.EqualTo(new LayoutBox(20, 20, 100, 50)));
        });
    }

    [Test]
    public void TheWindow_RanUnaware_At96Dpi_AndTheMachinesScaleIsRecorded()
    {
        var reference = _measured["SelfTest"];
        TestContext.Out.WriteLine($"[dpi] DeviceDpi {reference.DeviceDpi}, display scale {reference.DisplayScale:0.###}");

        Assert.That(reference.DeviceDpi, Is.EqualTo(96));
    }

    // ================================================================== anchors (spec §4's table)

    /// <summary>
    /// Every combination, held at 0px to the model re-anchored at that size — spec §4's table through
    /// <see cref="PixelLayoutModel.AnchorAxis"/>. ⚠ MEASURED: a centred axis lands on a half pixel for an odd growth
    /// and WinForms FLOORS it (−59 → −30, not −29); the page's calc(50% …) keeps the half, so the two differ by
    /// exactly 0.5px there — inside Task 13's ±1. Each such row is printed.
    /// </summary>
    [TestCase("design", 400, 300)]
    [TestCase("grow", 601, 401)]
    [TestCase("shrink", 341, 251)]
    public void EveryAnchorCombination_FollowsTheSpecTable(string label, int width, int height)
    {
        AssertMatchesModel("Anchors", label, PixelLayoutFixtures.Anchors(), (width, height));

        for (var n = 0; n < 16; n++)
        {
            var edges = PixelLayoutFixtures.AnchorFlags(n);
            var (x, y, w, h) = PixelLayoutFixtures.AnchorCell(n);
            var across = PixelLayoutModel.AnchorAxis(x, w, width - 400, edges.HasFlag(FormAnchorEdges.Left), edges.HasFlag(FormAnchorEdges.Right));
            var down = PixelLayoutModel.AnchorAxis(y, h, height - 300, edges.HasFlag(FormAnchorEdges.Top), edges.HasFlag(FormAnchorEdges.Bottom));
            foreach (var (what, axis) in new[] { ("X", across), ("Y", down) })
            {
                if (axis.Centred && axis.SpecOffset != axis.WinFormsOffset)
                {
                    TestContext.Out.WriteLine(
                        $"    [centring] a{n} ({PixelLayoutFixtures.AnchorText(n)}) {what}: spec {axis.SpecOffset}, WinForms {axis.WinFormsOffset}");
                }
            }
        }
    }

    // ================================================================== docking (FormDockLayout is on trial)

    [Test]
    public void AFill_BetweenAMenuAndAStatusStrip_IsTheResolversRect()
    {
        AssertMatchesModel("DockStrips", "design", PixelLayoutFixtures.DockStrips());
    }

    [Test]
    public void AFill_BetweenStrips_ReDocksAtALargerSize()
    {
        AssertMatchesModel("DockStrips", "grow", PixelLayoutFixtures.DockStrips(600, 400));
    }

    [Test]
    public void ADockTopPanel_BeforeTheMenuStrip_TakesTheTopEdge()
    {
        AssertMatchesModel("TopBeforeMenu", "design", PixelLayoutFixtures.TopBeforeMenu());
    }

    [Test]
    public void S9_AnOverflowingTop_ThenABottom_ThenAFill_IsTheResolversRect()
    {
        AssertMatchesModel("OverflowV", "design", PixelLayoutFixtures.OverflowV());
    }

    [Test]
    public void S9_AnOverflowingLeft_ThenARight_ThenAFill_IsTheResolversRect()
    {
        AssertMatchesModel("OverflowH", "design", PixelLayoutFixtures.OverflowH());
    }

    [Test]
    public void ADockedControl_HiddenAtStartup_DoesNotDock()
    {
        AssertMatchesModel("HiddenDock", "design", PixelLayoutFixtures.HiddenDock(pnlAVisible: false));
    }

    [Test]
    public void ShowingAHiddenDockedControl_ReDocksItsSiblings_AndTheAnchoredOnesStay()
    {
        AssertMatchesModel("HiddenDock", "showA", PixelLayoutFixtures.HiddenDock(pnlAVisible: true));
    }

    [Test]
    public void HidingItAgain_ClosesTheGap()
    {
        AssertMatchesModel("HiddenDock", "hideA", PixelLayoutFixtures.HiddenDock(pnlAVisible: false));
    }

    [Test]
    public void AHiddenContainer_HidesItsChildren()
    {
        AssertMatchesModel("HiddenBox", "design", PixelLayoutFixtures.HiddenBox());
    }

    [Test]
    public void ADockedContainersChildren_AreOffsetByWhereItDocks_NotItsStoredPosition()
    {
        AssertMatchesModel("DockedBox", "design", PixelLayoutFixtures.DockedBox());
    }

    [Test]
    public void ADockedContainersChildren_ReDockAndReAnchor_WhenTheFormGrows()
    {
        AssertMatchesModel("DockedBox", "grow", PixelLayoutFixtures.DockedBox(), (500, 360));
    }

    /// <summary>
    /// ⚠ OPEN — WinForms DISAGREES with the model here (see <see cref="PixelLayoutFixtures.DockedAnchor"/>). Pinned
    /// to the MEASURED window, with the model's answer beside it, until the coordinator decides which side moves
    /// (the region writer emitting a docked container's resolved size, or the model/page anchoring against the
    /// stored size). Not a pass-around: the assertion fails the day either the window or the model changes.
    /// </summary>
    [Test]
    public void OPEN_AnAnchoredChildOfADockedContainer_IsAnchoredToTheContainersStoredSize()
    {
        var design = Snap("DockedAnchor", "design");
        var grow = Snap("DockedAnchor", "grow");
        Record("DockedAnchor", design);
        Record("DockedAnchor", grow);

        Assert.Multiple(() =>
        {
            Assert.That(design.Controls["side"], Is.EqualTo(new LayoutBox(0, 0, 120, 300)));
            Assert.That(design.Controls["sideBR"], Is.EqualTo(new LayoutBox(60, 500, 50, 30)), "measured WinForms");
            Assert.That(grow.Controls["sideBR"], Is.EqualTo(new LayoutBox(60, 560, 50, 30)), "measured WinForms");
            Assert.That(PixelLayoutModel.Rects(PixelLayoutFixtures.DockedAnchor())["sideBR"],
                Is.EqualTo(new LayoutBox(60, 250, 50, 30)), "the model's answer (anchored to the docked 300)");
        });
    }

    [Test]
    public void TheModel_ReDocksAtAnotherSize_AsTheDocumentRebuiltAtThatSizeDoes()
    {
        // The two ways of asking for a resized expectation must agree (re-anchoring aside, which only the
        // clientSize form can do): the rebuilt document, and the design document laid out at that size.
        var differences = LayoutComparison.Differences(
            PixelLayoutModel.Rects(PixelLayoutFixtures.DockStrips(600, 400)),
            PixelLayoutModel.Rects(PixelLayoutFixtures.DockStrips(), FormDockMode.Runtime, (600, 400)), 0);
        Assert.That(differences, Is.Empty, string.Join("\n", differences));
    }

    /// <summary>
    /// Review I-4: an exception inside a WINDOW PROCEDURE (here a Resize handler, raised from inside the
    /// <c>ClientSize</c> setter's SetWindowPos) must come back as the driver's ERROR line, fast. Without
    /// <c>SetUnhandledExceptionMode(ThrowException)</c> WinForms shows a modal ThreadExceptionDialog and the run
    /// hangs to the 120 s timeout. The handler is user code patched into the retargeted pair — no designer
    /// document can express it (a Panel's only catalog event is Click and the form root takes no binds).
    /// ⚠ Its own build and run (~30 s).
    /// </summary>
    [Test]
    public void AnExceptionInAWindowProcedure_IsTheDriversError_NotAHang()
    {
        var dir = Path.Combine(_dir, "boom");
        Directory.CreateDirectory(dir);

        static string Boom(string code)
        {
            const string init = "Me.InitializeComponent()";
            var at = code.IndexOf(init, StringComparison.Ordinal);
            Assert.That(at, Is.GreaterThanOrEqualTo(0), "the scaffold no longer calls Me.InitializeComponent()");
            code = code.Insert(at + init.Length, "\n        AddHandler Me.Resize, AddressOf ThrowOnResize");

            var end = code.LastIndexOf("End Class", StringComparison.Ordinal);
            return code.Insert(end,
                "    Private Sub ThrowOnResize(sender As Object, e As EventArgs)\n" +
                "        Throw New InvalidOperationException(\"boom from a Resize handler\")\n" +
                "    End Sub\n");
        }

        var started = DateTime.UtcNow;
        var error = Assert.Throws<InvalidDataException>(() => WinFormsReferenceHarness.Measure(dir,
            new ReferenceFixture(PixelLayoutFixtures.Read("Boom", 400, 300, """<Panel Id="p" X="10" Y="10" Width="50" Height="50"/>"""),
                new ReferenceStep[] { new ResizeStep("grow", 500, 300) }, EditCode: Boom)));
        var took = DateTime.UtcNow - started;
        TestContext.Out.WriteLine($"[boom] {took.TotalSeconds:0.#} s: {error!.Message}");

        Assert.That(error.Message, Does.Contain("boom from a Resize handler"));
    }

    // ================================================================== recorded gaps

    /// <summary>
    /// ⚠ RECORDED GAP (plan spec-claims #11; Task 12 measured it, nothing is fixed here): the model takes a
    /// container's client area to be its bounds. WinForms does not, for three of the four:
    /// <list type="bullet">
    ///   <item><description>FixedSingle Panel: children 1px in on each side (client 178×98).</description></item>
    ///   <item><description>Fixed3D Panel: children 2px in on each side (client 176×96).</description></item>
    ///   <item><description>GroupBox: a POSITIONED child is NOT inset (its Location is in the GroupBox's
    ///   window, which is its client area); a DOCKED child docks in the DisplayRectangle, 3px in at the sides and
    ///   19px down at the top (the caption) — Dock=Top gives (3, 19, 174 wide).</description></item>
    /// </list>
    /// The borderless Panel is exactly the model. These literals are what the Edge page is compared against
    /// (Task 13); a change here is a change in WinForms or in the harness, never noise.
    /// </summary>
    [Test]
    public void BorderedContainers_TheModelsInsetGap_IsMeasuredAndRecorded()
    {
        var snapshot = Snap("Bordered", "design");
        Record("Bordered", snapshot);

        var model = PixelLayoutModel.Rects(PixelLayoutFixtures.Bordered());
        var recorded = new Dictionary<string, LayoutBox>(model)
        {
            ["pSingleTop"] = new(201, 11, 178, 20),
            ["pSingleSub"] = new(211, 51, 50, 30),
            ["p3DTop"] = new(12, 132, 176, 20),
            ["p3DSub"] = new(22, 172, 50, 30),
            ["grpTop"] = new(203, 149, 174, 20),
        };

        var differences = LayoutComparison.Differences(recorded, snapshot.Controls, 0);
        Assert.That(differences, Is.Empty,
            "the bordered containers no longer measure as recorded:\n  " + string.Join("\n  ", differences));
    }

    [Test]
    public void PinnedStrips_AreTheirCatalogHeights()
    {
        AssertMatchesModel("StripsPinned", "design", PixelLayoutFixtures.Strips("StripsPinned"));
    }

    /// <summary>
    /// ⚠ Measured, at 96 DPI with .NET 8's default font (Segoe UI 9pt) and one item each: an AUTO-SIZED MenuStrip,
    /// ToolStrip and StatusStrip are exactly the catalog's 24/25/22 — so the driver's pin changes nothing on this
    /// machine (a different font or DPI would; the pin stays so the reference does not depend on it).
    /// </summary>
    [Test]
    public void AutoSizedStrips_At96Dpi_AreAlreadyTheCatalogHeights()
    {
        AssertMatchesModel("StripsAuto", "design", PixelLayoutFixtures.Strips("StripsAuto"));
    }
}
