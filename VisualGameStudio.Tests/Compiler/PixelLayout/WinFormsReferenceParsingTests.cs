using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// Task 12 — the WinForms reference harness's own parser and comparison, fed SYNTHETIC driver output. These are
/// pure, so the harness's failure modes (a control silently skipped, a tolerance that swallows a real difference, a
/// clamped window, a lost DPI pin) go red here without building a WinForms program.
/// </summary>
[TestFixture]
public class WinFormsReferenceParsingTests
{
    private static readonly DriverPlan Plan = new(
        "SelfTest",
        new[] { "p1", "p2" },
        new[] { ("design", 400, 300), ("wide", 600, 300) });

    private static string Output(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    private static readonly string[] Good =
    {
        "SCREEN 1280 1920 0 0 1280 672",
        "FORM SelfTest 96 DpiUnaware",
        "SNAP SelfTest design 400 300",
        "RECT SelfTest design p1 1 20 20 100 50",
        "RECT SelfTest design p2 1 280 20 100 50",
        "SNAP SelfTest wide 600 300",
        "RECT SelfTest wide p1 1 20 20 100 50",
        "RECT SelfTest wide p2 1 480 20 100 50",
        "DONE",
    };

    [Test]
    public void AGoodRun_ParsesEverySnapshot_InFormClientPixels()
    {
        var reference = WinFormsReferenceHarness.Parse(Output(Good), new[] { Plan })["SelfTest"];

        Assert.Multiple(() =>
        {
            Assert.That(reference.DeviceDpi, Is.EqualTo(96));
            Assert.That(reference.DisplayScale, Is.EqualTo(1.5));
            Assert.That(reference.Snapshots.Select(s => s.Label), Is.EqualTo(new[] { "design", "wide" }));
            Assert.That(reference["wide"].Controls["p2"], Is.EqualTo(new LayoutBox(480, 20, 100, 50)));
            Assert.That(reference["design"].ClientWidth, Is.EqualTo(400));
        });
    }

    [Test]
    public void AHiddenControl_IsParsedAsHidden()
    {
        var lines = Good.Select(l => l == "RECT SelfTest wide p1 1 20 20 100 50" ? "RECT SelfTest wide p1 0 20 20 100 50" : l).ToArray();

        var reference = WinFormsReferenceHarness.Parse(Output(lines), new[] { Plan })["SelfTest"];

        Assert.That(reference["wide"].Controls["p1"].Visible, Is.False);
    }

    [Test]
    public void AControlTheDriverDidNotReport_IsRefused_NeverSkipped()
    {
        var lines = Good.Where(l => l != "RECT SelfTest wide p2 1 480 20 100 50").ToArray();

        var error = Assert.Throws<InvalidDataException>(() => WinFormsReferenceHarness.Parse(Output(lines), new[] { Plan }));

        Assert.That(error!.Message, Does.Contain("p2").And.Contain("wide"));
    }

    [Test]
    public void ASnapshotTheDriverDidNotTake_IsRefused()
    {
        var lines = Good.Where(l => !l.Contains(" wide ")).ToArray();

        var error = Assert.Throws<InvalidDataException>(() => WinFormsReferenceHarness.Parse(Output(lines), new[] { Plan }));

        Assert.That(error!.Message, Does.Contain("wide"));
    }

    [Test]
    public void AClampedWindow_IsRefused()
    {
        // Windows clamps an oversized window: the driver asked for 600 wide and got 560.
        var lines = Good.Select(l => l == "SNAP SelfTest wide 600 300" ? "SNAP SelfTest wide 560 300" : l).ToArray();

        var error = Assert.Throws<InvalidDataException>(() => WinFormsReferenceHarness.Parse(Output(lines), new[] { Plan }));

        Assert.That(error!.Message, Does.Contain("clamp").And.Contain("560"));
    }

    [Test]
    public void AScaledForm_IsRefused_BecauseItsNumbersAreNotCssPixels()
    {
        var lines = Good.Select(l => l == "FORM SelfTest 96 DpiUnaware" ? "FORM SelfTest 144 DpiUnaware" : l).ToArray();

        var error = Assert.Throws<InvalidDataException>(() => WinFormsReferenceHarness.Parse(Output(lines), new[] { Plan }));

        Assert.That(error!.Message, Does.Contain("144"));
    }

    /// <summary>
    /// ⛔ On a 100% display a DPI-AWARE process also reports 96, so the DPI alone cannot see a lost pin there
    /// (review I-2: dropping SetHighDpiMode survived as mutant 12). The mode in force is refused too.
    /// </summary>
    [TestCase("SystemAware")]
    [TestCase("PerMonitorV2")]
    public void AnAwareProcess_IsRefused_EvenAt96Dpi(string mode)
    {
        var lines = Good.Select(l => l == "FORM SelfTest 96 DpiUnaware" ? "FORM SelfTest 96 " + mode : l).ToArray();

        var error = Assert.Throws<InvalidDataException>(() => WinFormsReferenceHarness.Parse(Output(lines), new[] { Plan }));

        Assert.That(error!.Message, Does.Contain(mode).And.Contain("DpiUnaware"));
    }

    [Test]
    public void ADriverError_OrAMissingDone_IsRefused()
    {
        var crashed = Good.Take(4).Append("ERROR System.MissingFieldException: p2").ToArray();
        var cut = Good.Take(Good.Length - 1).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(() => WinFormsReferenceHarness.Parse(Output(crashed), new[] { Plan }),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("MissingFieldException"));
            Assert.That(() => WinFormsReferenceHarness.Parse(Output(cut), new[] { Plan }),
                Throws.TypeOf<InvalidDataException>().With.Message.Contains("DONE"));
        });
    }

    [Test]
    public void TheJson_CarriesEverySnapshotAndRect()
    {
        var json = WinFormsReferenceHarness.Parse(Output(Good), new[] { Plan })["SelfTest"].ToJson();

        Assert.That(json, Does.Contain("\"wide\"").And.Contain("\"p2\"").And.Contain("480"));
    }

    // ------------------------------------------------------------------ the one comparison

    private static Dictionary<string, LayoutBox> Boxes(params (string Id, LayoutBox Box)[] boxes) =>
        boxes.ToDictionary(b => b.Id, b => b.Box);

    [Test]
    public void Differences_WithinTheTolerance_AreNone_AndOnePastIt_IsReported()
    {
        var expected = Boxes(("a", new LayoutBox(10, 10, 100, 50)));

        Assert.Multiple(() =>
        {
            Assert.That(LayoutComparison.Differences(expected, Boxes(("a", new LayoutBox(11, 9, 101, 49))), 1), Is.Empty);
            Assert.That(LayoutComparison.Differences(expected, Boxes(("a", new LayoutBox(10, 10, 100, 51.5))), 1),
                Has.Count.EqualTo(1).And.Some.Contains("a").And.Some.Contains("Height"));
            Assert.That(LayoutComparison.Differences(expected, Boxes(("a", new LayoutBox(12, 10, 100, 50))), 1),
                Has.Count.EqualTo(1).And.Some.Contains("X"));
            Assert.That(LayoutComparison.Differences(expected, Boxes(("a", new LayoutBox(10, 10, 100, 51))), 0),
                Has.Count.EqualTo(1), "tolerance 0 is exact");
        });
    }

    [Test]
    public void Differences_AMissingControl_OnEitherSide_IsReported()
    {
        var both = Boxes(("a", new LayoutBox(0, 0, 10, 10)), ("b", new LayoutBox(0, 0, 10, 10)));
        var onlyA = Boxes(("a", new LayoutBox(0, 0, 10, 10)));

        Assert.Multiple(() =>
        {
            Assert.That(LayoutComparison.Differences(both, onlyA, 1), Has.Count.EqualTo(1).And.Some.Contains("b"));
            Assert.That(LayoutComparison.Differences(onlyA, both, 1), Has.Count.EqualTo(1).And.Some.Contains("b"));
        });
    }

    [Test]
    public void Differences_Visibility_IsCompared_AndAHiddenPairComparesNothingElse()
    {
        var shown = Boxes(("a", new LayoutBox(0, 0, 10, 10)));
        var hidden = Boxes(("a", new LayoutBox(0, 0, 10, 10, Visible: false)));
        var hiddenElsewhere = Boxes(("a", new LayoutBox(90, 90, 1, 1, Visible: false)));

        Assert.Multiple(() =>
        {
            Assert.That(LayoutComparison.Differences(shown, hidden, 1), Has.Count.EqualTo(1).And.Some.Contains("visible"));
            Assert.That(LayoutComparison.Differences(hidden, hiddenElsewhere, 1), Is.Empty);
        });
    }
}
