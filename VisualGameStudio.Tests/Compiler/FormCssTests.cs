using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §2.1/§2.8 — a row's web meaning comes from the CATALOG. ⛔ The four hard-coded rules in
/// FormAssetEmitter.AppendControlCss (ForeColor, BackColor, TextAlign, Visible) MOVE onto the walk in
/// the same commit: two sources would emit duplicate or conflicting declarations.
/// </summary>
[TestFixture]
public class FormCssTests
{
    private static FormDocument PageWith(string kind, params (string Name, string Value)[] properties)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var control = new FormControl { Kind = kind, Id = "c", Geometry = new GridGeometry() };
        foreach (var (name, value) in properties)
        {
            control.Properties[name] = value;
        }

        form.Controls.Add(control);
        return form;
    }

    private static string RuleFor(FormDocument form) =>
        FormAssetEmitter.Css(form).Split('\n').Single(l => l.StartsWith("#c ", StringComparison.Ordinal));

    [Test]
    public void TheFourMovedRules_EachComeFromTheCatalog()
    {
        var rule = RuleFor(PageWith("Label",
            ("ForeColor", "Red"), ("BackColor", "#00ff00"), ("TextAlign", "MiddleRight"), ("Visible", "false")));

        Assert.Multiple(() =>
        {
            Assert.That(rule, Does.Contain("color: Red"));
            Assert.That(rule, Does.Contain("background-color: #00ff00"));
            Assert.That(rule, Does.Contain("text-align: right"));
            Assert.That(rule, Does.Contain("display: none"));
        });
    }

    [TestCase("TopLeft", "left")]
    [TestCase("MiddleCenter", "center")]
    [TestCase("BottomRight", "right")]
    [TestCase("Center", "center")]   // the legacy alias
    [TestCase("left", "left")]       // legacy, lower-case
    public void TextAlign_EmitsOnlyTheHorizontalPart(string value, string css)
    {
        var rule = RuleFor(PageWith("Label", ("TextAlign", value)));

        Assert.Multiple(() =>
        {
            Assert.That(rule, Does.Contain($"text-align: {css}"));
            Assert.That(rule, Does.Not.Contain("middle").And.Not.Contain("top").And.Not.Contain("bottom"),
                "the vertical part has no web meaning; `middleleft` is not CSS (spec §2.8)");
        });
    }

    [Test]
    public void AnEightDigitHexColour_IsArgb_AndBecomesRgba()
    {
        // #80FF0000 in the document is WinForms ARGB: alpha 0x80, red. CSS would read 8 digits as RRGGBBAA.
        Assert.That(RuleFor(PageWith("Label", ("ForeColor", "#80FF0000"))), Does.Contain("color: rgba(255, 0, 0, 0.502)"));
    }

    [Test]
    public void ASystemColour_BecomesItsCssSystemColour()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RuleFor(PageWith("Label", ("BackColor", "Control"))), Does.Contain("background-color: ButtonFace"));
            // Carried from Task 4's review: the same table on the OTHER colour row — `color: Control` is not CSS.
            Assert.That(RuleFor(PageWith("Label", ("ForeColor", "Control"))), Does.Contain("color: ButtonFace"));
        });
    }

    [Test]
    public void ADegradedValue_EmitsNoDeclaration()
    {
        // ⚠ BackColor="ActiveCaption" is the web-ONLY refusal (carried from Task 4's review): VALID on
        // WinForms (SystemColors.ActiveCaption), Degraded on the web where CSS has no equivalent. The
        // other two values are refused on every target, so without it a gate on Accepts(value) — not
        // Accepts(value, Web) — would pass this test and still write `background-color: ActiveCaption`.
        var css = FormAssetEmitter.Css(PageWith("Label",
            ("ForeColor", "12345"), ("TextAlign", "Bogus"), ("BackColor", "ActiveCaption")));

        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Not.Contain("color: 12345").And.Not.Contain("text-align"));
            Assert.That(css, Does.Not.Contain("ActiveCaption").IgnoreCase.And.Not.Contain("background-color"));
        });
    }

    [Test]
    public void NoDeclarationIsEmittedTwice()
    {
        var rule = RuleFor(PageWith("Label", ("ForeColor", "Red")));

        Assert.That(rule.Split("color: Red").Length - 1, Is.EqualTo(1),
            "the hard-coded rule and the catalog walk must not both run (spec §2.1 ⛔)");
    }

    private static IEnumerable<TestCaseData> EveryWebKindWithAVisibleRow() =>
        FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.Web) && d.Property("Visible") is { } p && p.AppliesTo(FormTarget.Web))
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    /// <summary>Catalog-driven: a new row with a Visible property is covered the day it is added.</summary>
    [TestCaseSource(nameof(EveryWebKindWithAVisibleRow))]
    public void VisibleFalse_HidesTheElement_ForEveryWebKind(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var form = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var control = FormCatalogShapes.Canonical(form, definition, "c");
        control.Properties["Visible"] = "false";

        Assert.That(FormAssetEmitter.Css(form), Does.Contain("#c {").And.Contain("display: none"),
            $"'{kind}.Visible' must carry its CSS mapping — the element stays (getElementById needs it) and is hidden");
    }
}
