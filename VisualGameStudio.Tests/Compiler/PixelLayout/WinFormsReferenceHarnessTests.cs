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

    /// <summary>The window against the model at one size, exactly.</summary>
    private void AssertMatchesModel(string form, string label, FormDocument modelAtThatSize)
    {
        var snapshot = Snap(form, label);
        Record(form, snapshot);
        var differences = LayoutComparison.Differences(PixelLayoutModel.Rects(modelAtThatSize), snapshot.Controls, 0);
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
    /// Spec §4, per axis with d = the container's growth: near only → offset; far only → offset + d; both → offset,
    /// size + d; neither → offset + d/2 (a HALF pixel for an odd d, which WinForms must round one way — recorded).
    /// </summary>
    private static (double Offset, double Size, bool Centred) SpecAxis(int offset, int size, int d, bool near, bool far) =>
        (near, far) switch
        {
            (true, true) => (offset, size + d, false),
            (false, true) => (offset + d, size, false),
            (true, false) => (offset, size, false),
            _ => (offset + d / 2.0, size, true)
        };

    [TestCase("design", 400, 300)]
    [TestCase("grow", 601, 401)]
    [TestCase("shrink", 341, 251)]
    public void EveryAnchorCombination_FollowsTheSpecTable(string label, int width, int height)
    {
        var snapshot = Snap("Anchors", label);
        Record("Anchors", snapshot);

        var problems = new List<string>();
        for (var n = 0; n < 16; n++)
        {
            var edges = PixelLayoutFixtures.AnchorFlags(n);
            var (x, y, w, h) = PixelLayoutFixtures.AnchorCell(n);
            var across = SpecAxis(x, w, width - 400, edges.HasFlag(FormAnchorEdges.Left), edges.HasFlag(FormAnchorEdges.Right));
            var down = SpecAxis(y, h, height - 300, edges.HasFlag(FormAnchorEdges.Top), edges.HasFlag(FormAnchorEdges.Bottom));
            var box = snapshot.Controls[$"a{n}"];

            // ⚠ MEASURED: a centred axis lands on a half pixel for an odd growth, and WinForms FLOORS it —
            // offset + floor(d/2), toward −∞ on a shrink too (−59 → −30, not −29). The page's calc(50% …) keeps the
            // half, so the two differ by exactly 0.5px there: inside Task 13's ±1, and recorded here exactly.
            void Check(string what, double want, double got, bool centred)
            {
                var winForms = centred ? Math.Floor(want) : want;
                if (got != winForms)
                {
                    problems.Add($"a{n} ({PixelLayoutFixtures.AnchorText(n)}) {what}: spec {want}, expected WinForms {winForms}, measured {got}");
                }
                else if (centred && want != got)
                {
                    TestContext.Out.WriteLine($"    [centring] a{n} ({PixelLayoutFixtures.AnchorText(n)}) {what}: spec {want}, WinForms {got}");
                }
            }

            Check("X", across.Offset, box.X, across.Centred);
            Check("Width", across.Size, box.Width, false);
            Check("Y", down.Offset, box.Y, down.Centred);
            Check("Height", down.Size, box.Height, false);
        }

        Assert.That(problems, Is.Empty, "WinForms disagrees with spec §4's anchor table:\n  " + string.Join("\n  ", problems));
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
