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

    // ==================================================================
    // Review fixes — the walk is generic, so its guards must be too
    // ==================================================================

    /// <summary>
    /// ⛔ <see cref="FormCssConverter.None"/> copies the value verbatim, and a String row accepts ANY
    /// text — the first String row given a CssProperty would write <c>;</c>, <c>}</c> or
    /// <c>&lt;/style&gt;</c> straight into the stylesheet. Only a type whose accepted values are
    /// themselves safe may use it.
    ///
    /// <para>⚠ TASK 6: when <c>FormControlCatalog.FormRoot</c> exists, its rows must be added to this
    /// source — the root has a stylesheet rule too.</para>
    /// </summary>
    [Test]
    public void EveryVerbatimCssRow_IsIntOrEnum()
    {
        var offenders = FormControlCatalog.All
            .SelectMany(d => d.Properties.Select(p => (d.Kind, Property: p)))
            .Where(x => x.Property.CssProperty != null && x.Property.CssConverter == FormCssConverter.None &&
                        x.Property.Type is not (FormPropertyType.Int or FormPropertyType.Enum))
            .Select(x => $"{x.Kind}.{x.Property.Name} ({x.Property.Type})")
            .ToList();

        Assert.That(offenders, Is.Empty, "a verbatim CSS row must have a type whose values cannot break out of a declaration");
    }

    [TestCase(";")]
    [TestCase("{")]
    [TestCase("}")]
    [TestCase("<")]
    [TestCase(">")]
    [TestCase("\"")]
    [TestCase("'")]
    [TestCase("\\")]
    [TestCase("\n")]
    [TestCase("\r")]
    public void AVerbatimValue_ThatCouldBreakOutOfItsDeclaration_IsRefused(string character)
    {
        // Belt and braces for the row-type rule above: even a String row that slipped past it cannot
        // write a character that ends the declaration, the rule, or the <style> element.
        var def = new FormPropertyDef("X", FormPropertyType.String, CssProperty: "content");

        Assert.Multiple(() =>
        {
            Assert.That(FormCss.Declaration(def, "a" + character + "b"), Is.Null);
            Assert.That(FormCss.Declaration(def, "ab"), Is.EqualTo(("content", "ab")),
                "a safe value still passes — the refusal is about the characters, not the row");
        });
    }

    /// <summary>
    /// One accepted sample per converter. ⛔ A converter added to the enum without a sample here fails
    /// this test (the switch below throws), and without an arm in FormCss it throws there — never a
    /// silent "no declaration".
    /// </summary>
    [TestCaseSource(nameof(EveryConverter))]
    public void EveryConverter_ProducesADeclaration_ForAnAcceptedValue(FormCssConverter converter)
    {
        var (def, value) = converter switch
        {
            FormCssConverter.None => (new FormPropertyDef("X", FormPropertyType.Int, CssProperty: "z-index"), "5"),
            FormCssConverter.Color => (new FormPropertyDef("X", FormPropertyType.Color, CssProperty: "color",
                CssConverter: converter), "Red"),
            FormCssConverter.ContentAlignmentHorizontal => (FormControlCatalog.Find("Label")!.Property("TextAlign")!, "MiddleLeft"),
            FormCssConverter.VisibleToDisplay => (new FormPropertyDef("X", FormPropertyType.Bool, "true",
                CssProperty: "display", CssConverter: converter), "false"),
            _ => throw new ArgumentOutOfRangeException(nameof(converter), converter, "add a sample for the new converter")
        };

        Assert.Multiple(() =>
        {
            Assert.That(def.CssConverter, Is.EqualTo(converter), "the sample must exercise the converter it names");
            Assert.That(FormCss.Declaration(def, value), Is.Not.Null);
        });
    }

    private static IEnumerable<FormCssConverter> EveryConverter() => Enum.GetValues<FormCssConverter>();

    [Test]
    public void AnUnknownConverter_Throws_RatherThanEmittingNothing()
    {
        var def = new FormPropertyDef("X", FormPropertyType.Int, CssProperty: "z-index",
            CssConverter: (FormCssConverter)999);

        Assert.Throws<ArgumentOutOfRangeException>(() => FormCss.Declaration(def, "5"));
    }

    /// <summary>
    /// ⛔ The gate is <c>Accepts(value, Web)</c>. With the Color converter nothing tells the two apart
    /// (it has no CSS name for ActiveCaption either), so this uses None, which masks nothing:
    /// weakened to <c>Accepts(value)</c>, the declaration comes back.
    /// </summary>
    [Test]
    public void AWebRefusedValue_IsRefusedByTheGate_NotOnlyByItsConverter()
    {
        var def = new FormPropertyDef("X", FormPropertyType.Color, CssProperty: "color", CssConverter: FormCssConverter.None);

        Assert.That(FormCss.Declaration(def, "ActiveCaption"), Is.Null);
    }

    private static IEnumerable<TestCaseData> EveryWebKind() =>
        FormControlCatalog.All
            .Where(d => d.SupportsTarget(FormTarget.Web))
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    [TestCaseSource(nameof(EveryWebKind))]
    public void NoWebKind_HasTwoRowsWritingTheSameCssProperty(string kind)
    {
        var duplicated = FormControlCatalog.Find(kind)!.Properties
            .Where(p => p.AppliesTo(FormTarget.Web) && p.CssProperty != null)
            .GroupBy(p => p.CssProperty, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(p => p.Name))}")
            .ToList();

        Assert.That(duplicated, Is.Empty, "two rows writing one CSS property emit conflicting declarations");
    }
}
