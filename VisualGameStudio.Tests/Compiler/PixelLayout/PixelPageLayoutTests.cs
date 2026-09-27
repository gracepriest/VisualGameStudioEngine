using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// ⛔⛔ Task 13 — the Canvas PAGE, laid out by a real browser (spec 2026-09-27 §7). Every fixture is built into ONE
/// JavaScript project by the real CLI, served from loopback and measured by Microsoft Edge headless in ONE run
/// (<see cref="EdgeLayoutHarness"/>); the same documents are retargeted and measured in ONE WinForms run
/// (<see cref="WinFormsReferenceHarness"/>). Each test then compares Edge with the model
/// (<see cref="PixelLayoutModel.Rects"/>) and with the WinForms window, at ±1px.
///
/// <para>⚠ The shared fixtures are 400 wide, below the default 600px breakpoint, so their WEB copies carry
/// <c>MobileBreakpoint="200"</c> (pre-flight B1); phone stacking has its own 640×480 fixture.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
[NonParallelizable]
public class PixelPageLayoutTests
{
    private const string Breakpoint = "200";

    private string _dir = "";
    private string _timing = "";
    private EdgeRun _edge = null!;
    private IReadOnlyDictionary<string, WinFormsReference> _winForms = new Dictionary<string, WinFormsReference>();

    /// <summary>The web copy of a shared fixture: desktop down to 200px, so every size measured here is desktop.</summary>
    private static FormDocument Web(FormDocument document)
    {
        document.Layout!.MobileBreakpoint = Breakpoint;
        return document;
    }

    private static FormDocument PhoneWeb()
    {
        var phone = PixelLayoutFixtures.Phone();
        phone.Literal = "<p>Terms apply.</p>";
        return phone;
    }

    private static FormDocument LiteralWeb()
    {
        var page = Web(PixelLayoutFixtures.Literal("LiteralP"));
        page.Literal = "<p>Use your work account.</p>";
        return page;
    }

    [OneTimeSetUp]
    public void MeasureEverything()
    {
        if (EdgeLayoutHarness.EdgePath() == null)
        {
            TestSkip.IgnoreEvenInsideMultiple("Microsoft Edge is not installed (or this is not Windows), so the page cannot be laid out");
        }

        _dir = Path.Combine(Path.GetTempPath(), "bl-edge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var png = EdgeLayoutHarness.TinyPng();
        var started = DateTime.UtcNow;

        var documents = new[]
        {
            Web(PixelLayoutFixtures.SelfTest()), Web(PixelLayoutFixtures.Anchors()), Web(PixelLayoutFixtures.DockStrips()),
            Web(PixelLayoutFixtures.TopBeforeMenu()), Web(PixelLayoutFixtures.OverflowV()), Web(PixelLayoutFixtures.OverflowH()),
            Web(PixelLayoutFixtures.HiddenDock()), Web(PixelLayoutFixtures.Bordered()), Web(PixelLayoutFixtures.HiddenBox()),
            Web(PixelLayoutFixtures.DockedBox()), Web(PixelLayoutFixtures.DockedAnchor()),
            Web(PixelLayoutFixtures.HiddenSiblingAnchor()), Web(PixelLayoutFixtures.NestedDock()),
            Web(PixelLayoutFixtures.Strips("StripsPinned")), Web(PixelLayoutFixtures.Picture()),
            Web(PixelLayoutFixtures.MenuOver()), LiteralWeb(), PhoneWeb()
        };
        var site = EdgeLayoutHarness.BuildSite(_dir, documents, new Dictionary<string, byte[]> { ["pic.png"] = png });
        var byName = documents.ToDictionary(d => d.Name);
        EdgeCase At(string form, int w, int h, params EdgeStep[] steps) => EdgeCase.Of(byName[form], w, h, steps);

        var built = DateTime.UtcNow;
        _edge = EdgeLayoutHarness.Measure(site, new[]
        {
            At("SelfTest", 400, 300), At("SelfTest", 600, 300),
            At("Anchors", 400, 300), At("Anchors", 601, 401), At("Anchors", 341, 251),
            At("DockStrips", 400, 300), At("DockStrips", 600, 400),
            At("TopBeforeMenu", 400, 300), At("OverflowV", 400, 300), At("OverflowH", 400, 300),
            At("HiddenBox", 400, 300), At("StripsPinned", 400, 300), At("Bordered", 400, 300),
            At("HiddenDock", 400, 300,
                EdgeStep.Display("showA", "pnlA", "block"), EdgeStep.Display("hideA", "pnlA", "none"),
                EdgeStep.Hidden("hideB", "pnlB", true), EdgeStep.Hidden("showB", "pnlB", false),
                EdgeStep.Class("hideBc", "pnlB", true), EdgeStep.Class("showBc", "pnlB", false),
                EdgeStep.StyleCost("cost", "pnlC", 20)),
            At("HiddenSiblingAnchor", 400, 300, EdgeStep.Display("showHid", "hid", "block")),
            At("DockedBox", 400, 300), At("DockedBox", 500, 360),
            At("DockedAnchor", 400, 300), At("DockedAnchor", 500, 360),
            At("NestedDock", 400, 300), At("NestedDock", 500, 360),
            At("Picture", 400, 300), At("Picture", 600, 400),
            At("MenuOver", 400, 300, EdgeStep.HitTest("closed", 20, 40), EdgeStep.OpenMenu("open", "mnuFile"), EdgeStep.HitTest("hit", 20, 40)),
            At("LiteralP", 400, 300),
            At("Phone", 640, 480),
            At("Phone", 400, 900, EdgeStep.HasText("caption", "Remember me"), EdgeStep.HasText("label", "Password"))
        });
        var measured = DateTime.UtcNow;

        // The WinForms side: the UNMODIFIED documents (the breakpoint and the Literal are web-only). pic.png goes
        // where the driver runs: Image.FromFile is relative to its working directory (pre-flight B5).
        var winDir = Path.Combine(_dir, "winforms");
        Directory.CreateDirectory(Path.Combine(winDir, "reference-app"));
        File.WriteAllBytes(Path.Combine(winDir, "reference-app", "pic.png"), png);
        _winForms = WinFormsReferenceHarness.Measure(winDir,
            new ReferenceFixture(PixelLayoutFixtures.SelfTest(), new ResizeStep("wide", 600, 300)),
            new ReferenceFixture(PixelLayoutFixtures.Anchors(), new ResizeStep("grow", 601, 401)),
            new ReferenceFixture(PixelLayoutFixtures.DockStrips(), new ResizeStep("grow", 600, 400)),
            new ReferenceFixture(PixelLayoutFixtures.TopBeforeMenu()),
            new ReferenceFixture(PixelLayoutFixtures.OverflowV()),
            new ReferenceFixture(PixelLayoutFixtures.OverflowH()),
            new ReferenceFixture(PixelLayoutFixtures.HiddenDock(),
                new VisibilityStep("showA", "pnlA", true), new VisibilityStep("hideA", "pnlA", false),
                new VisibilityStep("hideB", "pnlB", false), new VisibilityStep("showB", "pnlB", true)),
            new ReferenceFixture(PixelLayoutFixtures.Bordered()),
            new ReferenceFixture(PixelLayoutFixtures.HiddenBox()),
            new ReferenceFixture(PixelLayoutFixtures.DockedBox(), new ResizeStep("grow", 500, 360)),
            new ReferenceFixture(PixelLayoutFixtures.DockedAnchor(), new ResizeStep("grow", 500, 360)),
            new ReferenceFixture(PixelLayoutFixtures.HiddenSiblingAnchor(), new VisibilityStep("showHid", "hid", true)),
            new ReferenceFixture(PixelLayoutFixtures.NestedDock(), new ResizeStep("grow", 500, 360)),
            new ReferenceFixture(PixelLayoutFixtures.Strips("StripsPinned")),
            new ReferenceFixture(PixelLayoutFixtures.Picture(), new ResizeStep("grow", 600, 400)),
            new ReferenceFixture(PixelLayoutFixtures.MenuOver()),
            new ReferenceFixture(PixelLayoutFixtures.Literal("LiteralPlain")));
        var done = DateTime.UtcNow;

        _timing = $"[timing] CLI build {(built - started).TotalSeconds:0.0} s, Edge {(measured - built).TotalSeconds:0.0} s " +
                  $"(process {_edge.Took.TotalSeconds:0.0} s), WinForms {(done - measured).TotalSeconds:0.0} s";
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

    // ================================================================== helpers

    private EdgeCaseResult Edge(string form, int width, int height) => _edge.Results[$"{form}@{width}x{height}"];

    private static void Record(string title, LayoutSnapshot snapshot)
    {
        TestContext.Out.WriteLine($"[{title}] {snapshot.Label} viewport {snapshot.ClientWidth}x{snapshot.ClientHeight}");
        foreach (var (id, box) in snapshot.Controls.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            TestContext.Out.WriteLine($"    {id,-12} {LayoutComparison.Show(box)}");
        }
    }

    /// <summary>Edge against an expectation at ±1px, with every rectangle printed.</summary>
    private static void AssertAgrees(string what, LayoutSnapshot edge, IReadOnlyDictionary<string, LayoutBox> expected)
    {
        Record(what, edge);
        var differences = EdgeLayoutHarness.Differences(expected, edge.Controls);
        Assert.That(differences, Is.Empty, $"{what} '{edge.Label}': Edge disagrees:\n  " + string.Join("\n  ", differences));
    }

    /// <summary>Edge at a viewport against the model at that client size AND the WinForms window's snapshot.</summary>
    private void AssertMatches(string form, int width, int height, FormDocument model, string winFormsLabel, string edgeLabel = "initial")
    {
        var edge = Edge(form, width, height)[edgeLabel];
        Assert.Multiple(() =>
        {
            AssertAgrees($"{form} vs model", edge, PixelLayoutModel.Rects(model, FormDockMode.Runtime, (width, height)));
            AssertAgrees($"{form} vs WinForms", edge, _winForms[form][winFormsLabel].Controls);
        });
    }

    // ================================================================== the harness proves itself

    [Test]
    public void EveryCase_RanAtScaleOne_InItsOwnViewport_WithoutAPageError()
    {
        var first = _edge.Results.Values.First();
        TestContext.Out.WriteLine($"[edge] {first.UserAgent}; {_edge.Results.Count} cases in {_edge.Took.TotalSeconds:0.0} s");
        TestContext.Out.WriteLine(_timing);

        Assert.Multiple(() =>
        {
            foreach (var result in _edge.Results.Values)
            {
                Assert.That(result.Errors, Is.Empty, $"{result.Case.Name}: the page threw");
                Assert.That(result.DevicePixelRatio, Is.EqualTo(1), result.Case.Name);
                Assert.That((result.ViewportWidth, result.ViewportHeight), Is.EqualTo((result.Case.Width, result.Case.Height)), result.Case.Name);
            }

            // M-6: the media range syntax needs Chromium ≥ 104; record what measured it.
            Assert.That(first.UserAgent, Does.Contain("Edg/"));
        });
    }

    [Test]
    public void TheRun_LeftNoProfileBehind()
    {
        Assert.That(_edge.ProfileDeleted, Is.True, $"the throw-away Edge profile is still there: {_edge.ProfileDirectory}");
    }

    [Test]
    public void SelfTest_AtTheDesignSize_IsTheStoredGeometry()
    {
        AssertMatches("SelfTest", 400, 300, PixelLayoutFixtures.SelfTest(), "design");
    }

    [Test]
    public void SelfTest_TwoHundredWider_TheRightAnchoredControlFollows()
    {
        AssertMatches("SelfTest", 600, 300, PixelLayoutFixtures.SelfTest(), "wide");
        Assert.That(Edge("SelfTest", 600, 300)["initial"].Controls["p2"], Is.EqualTo(new LayoutBox(480, 20, 100, 50)));
    }

    /// <summary>
    /// ⛔ The form area FILLS the browser from its top-left (spec §4) on every desktop case — and this is the check
    /// that can see a form area pushed down by a collapsing margin (pre-flight B3): every control rect is measured from
    /// the form area's own origin, so the controls alone look unchanged when the whole area moves.
    /// </summary>
    [Test]
    public void TheFormArea_StartsAtTheViewportsTopLeft_OnEveryDesktopCase()
    {
        Assert.Multiple(() =>
        {
            foreach (var result in _edge.Results.Values.Where(r => r.Case.Name != "Phone@400x900"))
            {
                Assert.That((result.FormArea.X, result.FormArea.Y), Is.EqualTo((0.0, 0.0)), $"{result.Case.Name}: the form area moved");
                Assert.That(result.FormClientOrigin, Is.EqualTo((0.0, 0.0)), $"{result.Case.Name}: the form area has a border");
            }
        });
    }

    // ================================================================== anchors

    [TestCase(400, 300, "design")]
    [TestCase(601, 401, "grow")]
    public void EveryAnchorCombination_MatchesTheModelAndTheWindow(int width, int height, string label)
    {
        AssertMatches("Anchors", width, height, PixelLayoutFixtures.Anchors(), label);
    }

    /// <summary>
    /// ⚠ RECORDED (Task 12 measured WinForms; this measures Edge): a centred axis on an ODD growth lands on a half
    /// pixel. The page's <c>calc(50% …)</c> KEEPS it (the spec's value); WinForms FLOORS it. Inside ±1, and pinned
    /// here exactly so a change on either side is visible.
    /// </summary>
    [Test]
    public void TheCentredHalfPixel_EdgeKeepsIt_WinFormsFloorsIt()
    {
        var edge = Edge("Anchors", 601, 401)["initial"].Controls;
        var window = _winForms["Anchors"]["grow"].Controls;

        Assert.Multiple(() =>
        {
            for (var n = 0; n < 16; n++)
            {
                var edges = PixelLayoutFixtures.AnchorFlags(n);
                var (x, y, w, h) = PixelLayoutFixtures.AnchorCell(n);
                var across = PixelLayoutModel.AnchorAxis(x, w, 201, edges.HasFlag(FormAnchorEdges.Left), edges.HasFlag(FormAnchorEdges.Right));
                var down = PixelLayoutModel.AnchorAxis(y, h, 101, edges.HasFlag(FormAnchorEdges.Top), edges.HasFlag(FormAnchorEdges.Bottom));
                if (across.Centred)
                {
                    TestContext.Out.WriteLine($"    a{n} X: Edge {edge[$"a{n}"].X}, WinForms {window[$"a{n}"].X}, spec {across.SpecOffset}");
                    Assert.That(edge[$"a{n}"].X, Is.EqualTo(across.SpecOffset), $"a{n} X on the page");
                    Assert.That(window[$"a{n}"].X, Is.EqualTo(across.WinFormsOffset), $"a{n} X in the window");
                }

                if (down.Centred)
                {
                    TestContext.Out.WriteLine($"    a{n} Y: Edge {edge[$"a{n}"].Y}, WinForms {window[$"a{n}"].Y}, spec {down.SpecOffset}");
                    Assert.That(edge[$"a{n}"].Y, Is.EqualTo(down.SpecOffset), $"a{n} Y on the page");
                    Assert.That(window[$"a{n}"].Y, Is.EqualTo(down.WinFormsOffset), $"a{n} Y in the window");
                }
            }
        });
    }

    /// <summary>
    /// Spec §4: narrower than the design, the form keeps its design size and the PAGE scrolls — never squashed. (Not
    /// compared with WinForms' shrink, which squashes by design — pre-flight B2.)
    /// </summary>
    [Test]
    public void NarrowerThanTheDesign_TheFormKeepsItsSize_AndThePageScrolls()
    {
        var narrow = Edge("Anchors", 341, 251);

        Assert.Multiple(() =>
        {
            Assert.That((narrow.FormArea.Width, narrow.FormArea.Height), Is.EqualTo((400.0, 300.0)), "the form area was squashed");
            Assert.That(narrow.ScrollWidth, Is.GreaterThanOrEqualTo(400), "the page does not scroll sideways");
            Assert.That(narrow.ScrollHeight, Is.GreaterThanOrEqualTo(300), "the page does not scroll down");
            AssertAgrees("Anchors narrow vs design", narrow["initial"], PixelLayoutModel.Rects(PixelLayoutFixtures.Anchors()));
        });
    }

    // ================================================================== docking

    [TestCase(400, 300, "design")]
    [TestCase(600, 400, "grow")]
    public void AFill_BetweenAMenuAndAStatusStrip(int width, int height, string label)
    {
        AssertMatches("DockStrips", width, height, PixelLayoutFixtures.DockStrips(), label);
    }

    [TestCase("TopBeforeMenu")]
    [TestCase("OverflowV")]
    [TestCase("OverflowH")]
    [TestCase("HiddenBox")]
    [TestCase("StripsPinned")]
    public void AtTheDesignSize_TheDockedFormsMatch(string form)
    {
        var model = form switch
        {
            "TopBeforeMenu" => PixelLayoutFixtures.TopBeforeMenu(),
            "OverflowV" => PixelLayoutFixtures.OverflowV(),
            "OverflowH" => PixelLayoutFixtures.OverflowH(),
            "HiddenBox" => PixelLayoutFixtures.HiddenBox(),
            _ => PixelLayoutFixtures.Strips("StripsPinned")
        };

        AssertMatches(form, 400, 300, model, "design");
    }

    [Test]
    public void EveryStrip_IsItsCatalogHeight_Exactly()
    {
        var strips = Edge("StripsPinned", 400, 300)["initial"].Controls;

        Assert.Multiple(() =>
        {
            Assert.That(strips["menu"].Height, Is.EqualTo(24));
            Assert.That(strips["tools"].Height, Is.EqualTo(25));
            Assert.That(strips["status"].Height, Is.EqualTo(22));
        });
    }

    [TestCase("DockedBox", 400, 300, "design")]
    [TestCase("DockedBox", 500, 360, "grow")]
    [TestCase("DockedAnchor", 400, 300, "design")]
    [TestCase("DockedAnchor", 500, 360, "grow")]
    [TestCase("NestedDock", 400, 300, "design")]
    [TestCase("NestedDock", 500, 360, "grow")]
    public void ADockedContainersChildren(string form, int width, int height, string label)
    {
        var model = form switch
        {
            "DockedBox" => PixelLayoutFixtures.DockedBox(),
            "DockedAnchor" => PixelLayoutFixtures.DockedAnchor(),
            _ => PixelLayoutFixtures.NestedDock()
        };

        AssertMatches(form, width, height, model, label);
    }

    [Test]
    public void AHiddenDockedSibling_MovesTheContainersAnchoredChildren()
    {
        AssertMatches("HiddenSiblingAnchor", 400, 300, PixelLayoutFixtures.HiddenSiblingAnchor(hidVisible: false), "design");
    }

    /// <summary>The REAL reflow script, in a real browser: showing the hidden sibling re-docks the container.</summary>
    [Test]
    public void ShowingTheSibling_ReDocksTheContainer_AndPutsItsChildrenBack()
    {
        AssertMatches("HiddenSiblingAnchor", 400, 300, PixelLayoutFixtures.HiddenSiblingAnchor(hidVisible: true), "showHid", "showHid");
    }

    [Test]
    public void ADockedControl_HiddenAtStartup_DoesNotDock()
    {
        AssertMatches("HiddenDock", 400, 300, PixelLayoutFixtures.HiddenDock(pnlAVisible: false), "design");
    }

    /// <summary>
    /// ⛔ Owner decision 2026-09-27: a run-time Visible toggle re-docks, through the page's reflow script — here the
    /// REAL script in a real browser (the node glue test runs it against a stub DOM). Three spellings user code has
    /// today — <c>style.display</c>, <c>hidden</c>, a class — each compared with the WinForms window's toggle.
    /// </summary>
    [TestCase("showA", "showA")]
    [TestCase("hideA", "hideA")]
    [TestCase("hideB", "hideB")]
    [TestCase("showB", "showB")]
    [TestCase("hideBc", "hideB")]
    [TestCase("showBc", "showB")]
    public void ARunTimeVisibilityToggle_ReDocksItsSiblings_AsTheWindowDoes(string edgeLabel, string winFormsLabel)
    {
        AssertAgrees($"HiddenDock {edgeLabel} vs WinForms {winFormsLabel}",
            Edge("HiddenDock", 400, 300)[edgeLabel], _winForms["HiddenDock"][winFormsLabel].Controls);
    }

    /// <summary>
    /// ⚠ RECORDED, not decided (Task 10 review M-5): the reflow script re-walks the dock tree on ANY observed
    /// attribute write under the form area — here a colour change on an UNDOCKED Panel. Measured: 4 getComputedStyle
    /// calls per write on this page, i.e. ONE full walk per write (pnlA and pnlB are each asked twice: as docked
    /// siblings, then as containers). The walk writes nothing when the CSS is unchanged. Whether to filter the
    /// observer to docked ids is the coordinator's call.
    /// </summary>
    [Test]
    public void M5_AnUnrelatedStyleWrite_ReWalksTheDockTree_Recorded()
    {
        var perWrite = Edge("HiddenDock", 400, 300).Probes["cost"];
        TestContext.Out.WriteLine($"[M-5] getComputedStyle calls per colour write on pnlC: {perWrite}");
        Assert.That(perWrite, Is.EqualTo("4"));
    }

    // ================================================================== Task 10's defects, in a real browser

    /// <summary>
    /// ⛔ Task 10 review I-1: an <c>&lt;img&gt;</c> with a LOADED src ignores left+right (top+bottom) without an
    /// explicit size. Every image here is loaded (an unloaded one stretches and hides the defect).
    /// </summary>
    [TestCase(400, 300, "design")]
    [TestCase(600, 400, "grow")]
    public void APictureShowingARealImage_StretchesWithItsAnchorsAndItsDock(int width, int height, string label)
    {
        var page = Edge("Picture", width, height);
        Assert.Multiple(() =>
        {
            foreach (var id in new[] { "picLR", "picTB", "picFill" })
            {
                Assert.That(page.Images[id].Complete && page.Images[id].NaturalWidth > 0, Is.True, $"{id}'s image did not load");
            }

            AssertMatches("Picture", width, height, PixelLayoutFixtures.Picture(), label);
        });
    }

    /// <summary>⛔ Task 10 review N-1: a Literal starting with a <c>&lt;p&gt;</c> moves nothing (pre-flight B3, B6).</summary>
    [Test]
    public void ALiteralStartingWithAParagraph_MovesNoControl()
    {
        var page = Edge("LiteralP", 400, 300);
        Assert.Multiple(() =>
        {
            Assert.That(page.Literal, Is.Not.Null, "the page carries its Literal");
            Assert.That((page.FormArea.X, page.FormArea.Y), Is.EqualTo((0.0, 0.0)), "the paragraph's margin moved the form area");
            AssertAgrees("LiteralP vs model", page["initial"], PixelLayoutModel.Rects(PixelLayoutFixtures.Literal("LiteralPlain")));
            AssertAgrees("LiteralP vs WinForms", page["initial"], _winForms["LiteralPlain"]["design"].Controls);
        });
    }

    /// <summary>
    /// ⛔ Task 10 B4: an OPEN dropdown is on top of the control after its strip (WinForms opens it as its own window).
    /// Before it opens, the same point is the Panel — the probe can see both answers.
    /// </summary>
    [Test]
    public void AnOpenMenuDropdown_IsAboveTheControlAfterTheStrip()
    {
        var page = Edge("MenuOver", 400, 300);
        TestContext.Out.WriteLine($"[menu] closed: {page.Probes["closed"]}, open: {page.Probes["hit"]}");

        Assert.Multiple(() =>
        {
            AssertMatches("MenuOver", 400, 300, PixelLayoutFixtures.MenuOver(), "design");
            Assert.That(page.Probes["closed"], Is.EqualTo("under"), "non-vacuity: closed, the point is over the Panel");
            Assert.That(page.Probes["hit"], Is.EqualTo("mnuOpen"), "the open dropdown is under the Panel");
        });
    }

    // ================================================================== phones (spec §5)

    [Test]
    public void ThePhonePage_AtItsDesignSize_IsTheModel()
    {
        AssertAgrees("Phone vs model", Edge("Phone", 640, 480)["initial"], PixelLayoutModel.Rects(PixelLayoutFixtures.Phone()));
    }

    /// <summary>
    /// Below the breakpoint: ONE column in <see cref="FormReadingOrder"/>'s order, top strips first and bottom strips
    /// last, inputs across the column, small controls at their size, the hidden control still hidden, the Literal last.
    /// </summary>
    [Test]
    public void BelowTheBreakpoint_OneColumn_InReadingOrder()
    {
        var page = Edge("Phone", 400, 900);
        var controls = page["initial"].Controls;
        Record("Phone 400", page["initial"]);

        var document = PixelLayoutFixtures.Phone();
        var designer = FormDockLayout.Resolve(document, FormDockMode.Designer);
        FormRect RectOf(FormControl c) => designer.TryGet(c, out var d) ? d.Bounds
            : c.Geometry is PixelGeometry p ? new FormRect(p.X, p.Y, p.Width, p.Height) : default;
        var positioned = document.Controls.Where(c => c.Definition?.Place == FormPlace.Positioned).ToList();
        var expected = new[] { "menu" }
            .Concat(FormReadingOrder.Order(positioned, RectOf).Select(c => c.Id))
            .Concat(new[] { "status" })
            .Where(id => id != "btnHidden")
            .ToList();

        var actual = controls.Where(c => c.Value.Visible).OrderBy(c => c.Value.Y).ThenBy(c => c.Value.X).Select(c => c.Key).ToList();
        TestContext.Out.WriteLine($"[phone] expected {string.Join(", ", expected)}\n[phone] measured {string.Join(", ", actual)}");

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(expected), "the column is not in reading order");
            Assert.That(controls["btnHidden"].Visible, Is.False, "a hidden control stays hidden on a phone");
            foreach (var id in actual)
            {
                Assert.That(controls[id].X, Is.EqualTo(0).Within(EdgeLayoutHarness.Tolerance), $"{id} is not in the one column");
            }

            foreach (var id in new[] { "txtUser", "txtPass", "lstRecent", "menu", "status" })
            {
                Assert.That(controls[id].Width, Is.EqualTo(400).Within(EdgeLayoutHarness.Tolerance), $"{id} does not span the column");
            }

            Assert.That(controls["btnGo"].Width, Is.EqualTo(75), "a small control keeps its designed width");
            Assert.That(page.Literal, Is.Not.Null);
            Assert.That(page.Literal!.Value.Y, Is.GreaterThanOrEqualTo(controls.Values.Where(b => b.Visible).Max(b => b.Y + b.Height)),
                "the Literal comes after every control");
        });
    }

    // ================================================================== recorded gaps

    /// <summary>
    /// ⚠ RECORDED GAP (Task 10 M-7, not fixed): a CheckBox is a bare <c>&lt;input&gt;</c>, which has no content, so
    /// its caption is not on the page at all. A Label's text is (non-vacuity).
    /// </summary>
    [Test]
    public void TheCheckBoxCaption_IsNotOnThePage_Recorded()
    {
        var page = Edge("Phone", 400, 900);
        Assert.Multiple(() =>
        {
            Assert.That(page.Probes["label"], Is.EqualTo("true"), "non-vacuity: a Label's text is rendered");
            Assert.That(page.Probes["caption"], Is.EqualTo("false"), "the CheckBox caption now renders — update the record (and the follow-up)");
        });
    }

    /// <summary>
    /// ⚠ RECORDED GAP (plan spec-claims #11, Task 10 C3/M-3; measured, NOT fixed). The model takes a container's
    /// client area to be its bounds. On the page:
    /// <list type="bullet">
    ///   <item><description>A Panel's <c>BorderStyle</c> has no CSS, so a FixedSingle or Fixed3D Panel draws no border
    ///   and its children sit exactly where the model puts them — WinForms puts them 1px (FixedSingle) / 2px (Fixed3D)
    ///   inside (Task 12's record).</description></item>
    ///   <item><description>A GroupBox is a <c>&lt;fieldset&gt;</c> with the UA's 2px groove border: EVERY child is 2px
    ///   in on each side — Dock=Top (202, 132, 176 wide), positioned (212, 172). WinForms docks the Dock=Top child in its
    ///   DisplayRectangle (203, 149, 174 wide: the caption) and does NOT inset the positioned one (210, 170). The caption
    ///   is a bare text node, not a <c>&lt;legend&gt;</c>, and the fieldset's min-inline-size did not widen it (180).</description></item>
    /// </list>
    /// So Edge vs the window: pSingle's children −1/−1 (and the Dock=Top +2 wide), p3D's −2/−2 (+4 wide), grpTop −1/−17
    /// (+2 wide), grpSub +2/+2. Held EXACTLY: a change on the page is a change in the emitter or in Edge, never noise.
    /// </summary>
    [Test]
    public void BorderedContainers_OnThePage_Recorded()
    {
        var edge = Edge("Bordered", 400, 300)["initial"];
        Record("Bordered Edge", edge);
        Record("Bordered WinForms", _winForms["Bordered"]["design"]);

        var recorded = new Dictionary<string, LayoutBox>(PixelLayoutModel.Rects(PixelLayoutFixtures.Bordered()))
        {
            ["grpTop"] = new(202, 132, 176, 20),
            ["grpSub"] = new(212, 172, 50, 30),
        };

        var differences = LayoutComparison.Differences(recorded, edge.Controls, 0);
        Assert.That(differences, Is.Empty, "the bordered containers no longer lay out as recorded:\n  " + string.Join("\n  ", differences));
    }
}
