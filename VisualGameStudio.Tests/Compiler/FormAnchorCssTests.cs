using System.Globalization;
using System.Text.RegularExpressions;
using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §4 — Anchor and Dock → CSS, as a pure function the page emitter only consumes. The
/// WinForms window is the resize reference (Task 12/13); this pins our reading of it, table by table.
/// </summary>
[TestFixture]
public class FormAnchorCssTests
{
    private static PixelGeometry At(string? anchor, int x = 10, int y = 20, int width = 75, int height = 23) =>
        new() { X = x, Y = y, Width = width, Height = height, Anchor = anchor };

    private static List<(string Property, string Value)> Css(PixelGeometry geometry, int w = 400, int h = 300) =>
        FormAnchorCss.Positioned(geometry, w, h).ToList();

    // ==================================================================
    // The parser (shared with RegionWriter)
    // ==================================================================

    [TestCase(null, FormAnchorEdges.Top | FormAnchorEdges.Left)]
    [TestCase("", FormAnchorEdges.Top | FormAnchorEdges.Left)]
    [TestCase("None", FormAnchorEdges.None)]
    [TestCase("Top, Right", FormAnchorEdges.Top | FormAnchorEdges.Right)]
    [TestCase("left,LEFT", FormAnchorEdges.Left)]
    public void Parse_ReadsTheEdges_AbsentMeansTopLeft(string? anchor, FormAnchorEdges expected)
    {
        Assert.That(FormAnchor.Parse(anchor, out var unknown), Is.EqualTo(expected));
        Assert.That(unknown, Is.Empty);
    }

    [Test]
    public void Parse_NamesAnUnknownEdge_AndKeepsTheKnownOnes()
    {
        Assert.That(FormAnchor.Parse("Top,Middle", out var unknown), Is.EqualTo(FormAnchorEdges.Top));
        Assert.That(unknown, Is.EqualTo(new[] { "Middle" }));
    }

    [Test]
    public void TheEdgeValues_AreAnchorStyles()
    {
        // ⛔ RegionWriter emits CType(n, AnchorStyles) from these numbers. DockStyle numbers differently.
        Assert.That(((int)FormAnchorEdges.Top, (int)FormAnchorEdges.Bottom, (int)FormAnchorEdges.Left, (int)FormAnchorEdges.Right),
            Is.EqualTo((1, 2, 4, 8)));
    }

    // ==================================================================
    // Positioned controls — the table
    // ==================================================================

    [Test]
    public void TopLeft_TheDefault_IsItsDesignedBox()
    {
        Assert.That(Css(At(null)), Is.EqualTo(new List<(string, string)>
        {
            ("left", "10px"), ("width", "75px"), ("top", "20px"), ("height", "23px")
        }));
    }

    [Test]
    public void Right_NotLeft_KeepsItsDistanceFromTheRightEdge()
    {
        Assert.That(Css(At("Top,Right")).Take(2), Is.EqualTo(new List<(string, string)> { ("right", "315px"), ("width", "75px") }));
    }

    [Test]
    public void LeftAndRight_StretchesTheWidth()
    {
        Assert.That(Css(At("Top,Left,Right")).Take(2), Is.EqualTo(new List<(string, string)> { ("left", "10px"), ("right", "315px") }));
    }

    [Test]
    public void Bottom_NotTop_KeepsItsDistanceFromTheBottomEdge()
    {
        Assert.That(Css(At("Bottom,Left")).Skip(2), Is.EqualTo(new List<(string, string)> { ("bottom", "257px"), ("height", "23px") }));
    }

    [Test]
    public void TopAndBottom_StretchesTheHeight()
    {
        Assert.That(Css(At("Top,Bottom,Left")).Skip(2), Is.EqualTo(new List<(string, string)> { ("top", "20px"), ("bottom", "257px") }));
    }

    [Test]
    public void NoneOnEitherAxis_IsCentredRelativeToItsOriginalOffset()
    {
        Assert.That(Css(At("None")), Is.EqualTo(new List<(string, string)>
        {
            ("left", "calc(50% - 190px)"), ("width", "75px"), ("top", "calc(50% - 130px)"), ("height", "23px")
        }));
    }

    private static IEnumerable<TestCaseData> EveryAnchorCombination()
    {
        for (var bits = 0; bits < 16; bits++)
        {
            var edges = (FormAnchorEdges)bits;
            var anchor = edges == FormAnchorEdges.None ? "None" : edges.ToString();
            yield return new TestCaseData(anchor).SetName($"{{m}}({anchor})");
        }
    }

    private static string[] Axis(bool near, bool far, string nearName, string farName, string size) => (near, far) switch
    {
        (true, true) => new[] { nearName, farName },
        (false, true) => new[] { farName, size },
        _ => new[] { nearName, size }
    };

    [TestCaseSource(nameof(EveryAnchorCombination))]
    public void EveryAnchorCombination_MapsEachAxisByItsTwoEdges(string anchor)
    {
        var edges = FormAnchor.Parse(anchor, out _);
        var css = Css(At(anchor));
        var left = edges.HasFlag(FormAnchorEdges.Left);
        var right = edges.HasFlag(FormAnchorEdges.Right);
        var top = edges.HasFlag(FormAnchorEdges.Top);
        var bottom = edges.HasFlag(FormAnchorEdges.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(css.Select(d => d.Property),
                Is.EqualTo(Axis(left, right, "left", "right", "width").Concat(Axis(top, bottom, "top", "bottom", "height"))));

            if (!left && !right)
            {
                Assert.That(css.First(d => d.Property == "left").Value, Does.StartWith("calc(50%"));
            }

            if (!top && !bottom)
            {
                Assert.That(css.First(d => d.Property == "top").Value, Does.StartWith("calc(50%"));
            }
        });
    }

    [TestCase(10, 400, 600)]
    [TestCase(10, 401, 900)]
    [TestCase(300, 400, 1000)]
    [TestCase(0, 400, 400)]
    public void TheCentringFormula_MovesByHalfTheGrowth_AsWinFormsDoes(int x, int designWidth, int newWidth)
    {
        var value = FormAnchorCss.Centred(x, designWidth);
        var match = Regex.Match(value, @"^calc\(50% ([+-]) ([0-9.]+)px\)$");

        Assert.That(match.Success, Is.True, value);

        var shift = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * (match.Groups[1].Value == "-" ? -1 : 1);

        Assert.That(0.5 * newWidth + shift, Is.EqualTo(x + (newWidth - designWidth) / 2.0).Within(0.001),
            "new left = X + (W′−W)/2 (spec §4)");
    }

    [Test]
    public void ANonPositiveSize_WritesNoSize_AndCountsAsZero()
    {
        var css = Css(At("Top,Right", width: 0, height: -4));

        Assert.Multiple(() =>
        {
            Assert.That(css.Select(d => d.Property), Is.EqualTo(new[] { "right", "top" }),
                "spec §3: the emitter writes no size for a non-positive one");
            Assert.That(css[0].Value, Is.EqualTo("390px"), "400 − 10 − 0");
        });
    }

    [Test]
    public void ANegativeSize_CountsAsZero_InTheFarInset_OnBothAxes()
    {
        // Width 0 cannot tell "counts as zero" from "counts as itself"; −4 can (a −4 would push the inset to 394).
        var css = Css(At("Bottom,Right", width: -4, height: -4));

        Assert.That(css, Is.EqualTo(new List<(string, string)> { ("right", "390px"), ("bottom", "280px") }));
    }

    [Test]
    [SetCulture("sv-SE")]
    public void Numbers_AreInvariant_UnderAUnicodeMinusCulture()
    {
        UnicodeMinusCulture.Require();
        var css = Css(At(null, x: -5), w: 400, h: 300);

        Assert.That(css[0].Value, Is.EqualTo("-5px"));
        Assert.That(string.Concat(css.Select(d => d.Value)), Does.Not.Contain(((char)0x2212).ToString()));
    }

    [Test]
    [SetCulture("sv-SE")]
    public void TheCentredOffset_AndDockedInsets_AreInvariant_UnderACommaDecimalCulture()
    {
        // sv-SE writes 190.5 as "190,5" and −3 with U+2212; CSS reads neither. The fractional half comes from
        // an odd container size (401 / 2), the negative from a docked rect past the far edge (S9 overflow).
        UnicodeMinusCulture.Require();
        var centred = FormAnchorCss.Centred(10, 401);
        var docked = FormAnchorCss.Docked(Docked(FormDockEdge.Bottom, new FormRect(0, 280, 400, 23)));

        Assert.Multiple(() =>
        {
            Assert.That(centred, Is.EqualTo("calc(50% - 190.5px)"));
            Assert.That(docked.Single(d => d.Property == "bottom").Value, Is.EqualTo("-3px"));
        });
    }

    // ==================================================================
    // Docked — resolved once at the design size into fixed insets
    // ==================================================================

    private static FormDockedBounds Docked(FormDockEdge edge, FormRect bounds) =>
        new(new FormControl { Kind = "Panel", Id = "p" }, edge, bounds, 400, 300);

    [TestCase(FormDockEdge.Top, "left:0px right:0px top:24px height:50px")]
    [TestCase(FormDockEdge.Bottom, "left:0px right:0px bottom:22px height:50px")]
    [TestCase(FormDockEdge.Left, "left:0px width:50px top:24px bottom:22px")]
    [TestCase(FormDockEdge.Right, "right:0px width:50px top:24px bottom:22px")]
    [TestCase(FormDockEdge.Fill, "left:0px right:0px top:24px bottom:22px")]
    public void EachDockEdge_BecomesFixedInsets(FormDockEdge edge, string expected)
    {
        var bounds = edge switch
        {
            FormDockEdge.Top => new FormRect(0, 24, 400, 50),
            FormDockEdge.Bottom => new FormRect(0, 228, 400, 50),
            FormDockEdge.Left => new FormRect(0, 24, 50, 254),
            FormDockEdge.Right => new FormRect(350, 24, 50, 254),
            _ => new FormRect(0, 24, 400, 254)
        };

        var css = FormAnchorCss.Docked(Docked(edge, bounds));

        Assert.That(string.Join(" ", css.Select(d => $"{d.Property}:{d.Value}")), Is.EqualTo(expected));
    }
}
