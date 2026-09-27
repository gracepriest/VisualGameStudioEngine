using System.Net;
using System.Text.Json;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// Task 13 (spec 2026-09-27 §7) — the Edge harness's parser, fed synthetic dumps, no browser. ⛔ Every refusal here is
/// a way the real run could otherwise report a pass it did not measure: an unfinished run, a case that never
/// reported, a control that was not on the page, an iframe whose size did not take, a device scale that is not 1.
/// </summary>
[TestFixture]
public class EdgeLayoutParsingTests
{
    private static readonly EdgeCase Case = new("SelfTest@400x300", "SelfTest", 400, 300, new[] { "p1", "p2" },
        new[] { EdgeStep.Display("showP2", "p2", "block") });

    private static Dictionary<string, object?> Good(
        string name = "SelfTest@400x300", int vw = 400, int vh = 300, double dpr = 1, string readyState = "complete")
    {
        object Box(double x, double y, double w, double h, bool v = true) => new { x, y, w, h, v };
        return new Dictionary<string, object?>
        {
            ["name"] = name, ["rs"] = readyState, ["vw"] = vw, ["vh"] = vh, ["dpr"] = dpr, ["ua"] = "Edg/154.0.0.0",
            ["form"] = new { x = 0, y = 0, w = 400, h = 300, cl = 0, ct = 0 },
            ["sw"] = 400, ["sh"] = 300,
            ["images"] = new Dictionary<string, object>(), ["literal"] = null,
            ["probes"] = new Dictionary<string, string> { ["hit"] = "p1" },
            ["errors"] = Array.Empty<string>(),
            ["snaps"] = new object[]
            {
                new { label = "initial", controls = new Dictionary<string, object> { ["p1"] = Box(20, 20, 100, 50), ["p2"] = Box(280.5, 20, 100, 50) } },
                new { label = "showP2", controls = new Dictionary<string, object> { ["p1"] = Box(20, 20, 100, 50), ["p2"] = Box(0, 0, 0, 0, false) } }
            }
        };
    }

    private static string Dump(object results, bool done = true) =>
        "<!DOCTYPE html>\n<html><head></head><body><pre id=\"out\"" + (done ? " data-done=\"1\"" : "") + ">" +
        WebUtility.HtmlEncode(JsonSerializer.Serialize(results)) + "</pre>\n</body></html>";

    [Test]
    public void AGoodDump_ParsesIntoFormClientSnapshots()
    {
        var result = EdgeLayoutHarness.Parse(Dump(new[] { Good() }), new[] { Case })[Case.Name];

        Assert.Multiple(() =>
        {
            Assert.That(result["initial"].Controls["p2"], Is.EqualTo(new LayoutBox(280.5, 20, 100, 50)));
            Assert.That(result["showP2"].Controls["p2"].Visible, Is.False);
            Assert.That(result["initial"].ClientWidth, Is.EqualTo(400));
            Assert.That(result.Probes["hit"], Is.EqualTo("p1"));
            Assert.That(result.FormArea, Is.EqualTo(new LayoutBox(0, 0, 400, 300)));
            Assert.That(result.UserAgent, Does.Contain("Edg/"));
        });
    }

    [Test]
    public void ADumpWithNoOutput_IsRefused()
    {
        Assert.That(() => EdgeLayoutHarness.Parse("<html><body></body></html>", new[] { Case }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("no #out"));
    }

    [Test]
    public void AnUnfinishedRun_IsRefused_NeverReadAsWhatItGotThrough()
    {
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { Good() }, done: false), new[] { Case }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("did not finish"));
    }

    [Test]
    public void ACaseThatNeverReported_IsRefused()
    {
        var other = Case with { Name = "SelfTest@600x300", Width = 600 };
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { Good() }), new[] { Case, other }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("SelfTest@600x300"));
    }

    [Test]
    public void AControlThePageDidNotHave_IsRefused_NeverSkipped()
    {
        var withMissing = Case with { Ids = new[] { "p1", "p2", "p3" } };
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { Good() }), new[] { withMissing }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("'p3'"));
    }

    [Test]
    public void AnIframeWhoseSizeDidNotTake_IsRefused()
    {
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { Good(vw: 300, vh: 150) }), new[] { Case }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("300x150"));
    }

    [TestCase("loading")]
    [TestCase("interactive")]
    public void AMeasurementTakenBeforeLoad_IsRefused(string readyState)
    {
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { Good(readyState: readyState) }), new[] { Case }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("before the page's load event"));
    }

    [Test]
    public void ADeviceScaleOtherThanOne_IsRefused()
    {
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { Good(dpr: 2) }), new[] { Case }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("devicePixelRatio 2"));
    }

    [Test]
    public void ASnapshotAStepNeverTook_IsRefused()
    {
        var good = Good();
        good["snaps"] = ((object[])good["snaps"]!).Take(1).ToArray();
        Assert.That(() => EdgeLayoutHarness.Parse(Dump(new[] { good }), new[] { Case }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("'showP2'"));
    }

    [Test]
    public void PageErrors_AreCarried_ForTheTestToAssert()
    {
        var good = Good();
        good["errors"] = new[] { "ReferenceError: x is not defined" };

        var result = EdgeLayoutHarness.Parse(Dump(new[] { good }), new[] { Case })[Case.Name];

        Assert.That(result.Errors, Is.EqualTo(new[] { "ReferenceError: x is not defined" }));
    }

    /// <summary>
    /// The one tolerance every Edge comparison uses — ±1px (spec §7). A 1.5px difference is a difference; a 0.5px one
    /// (the centring half pixel WinForms floors) is not.
    /// </summary>
    [Test]
    public void TheEdgeTolerance_IsOnePixel()
    {
        var expected = new Dictionary<string, LayoutBox> { ["a"] = new(10, 10, 50, 50) };

        Assert.Multiple(() =>
        {
            Assert.That(EdgeLayoutHarness.Differences(expected,
                new Dictionary<string, LayoutBox> { ["a"] = new(10.5, 10, 50, 50) }), Is.Empty);
            Assert.That(EdgeLayoutHarness.Differences(expected,
                new Dictionary<string, LayoutBox> { ["a"] = new(11.5, 10, 50, 50) }), Has.Count.EqualTo(1));
        });
    }
}
