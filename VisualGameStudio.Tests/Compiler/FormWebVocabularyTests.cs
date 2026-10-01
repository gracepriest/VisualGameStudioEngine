using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 3 Task 5 (plan 3.4, spec D2 — one vocabulary): a row with a clean CSS/HTML meaning reaches the page through the
/// catalog; a row with none is <c>Targets: WinForms</c> and NEVER reaches a page; the web-only extras <c>CssClass</c> and
/// <c>Style</c> exist on every web element kind.
///
/// <para>⚠ Directional, as the pre-flight records: the reverse ("every web row reaches the page") is not asserted —
/// pre-existing rows (TextBox.Multiline, TextBox.PasswordChar, Panel.BorderStyle, PictureBox.SizeMode) fail it today and
/// are piece 2's portable library.</para>
/// </summary>
[TestFixture]
public class FormWebVocabularyTests
{
    private static string Sample(FormPropertyDef p) => p.Type switch
    {
        FormPropertyType.Int => "7",
        FormPropertyType.Bool => p.Default == "true" ? "false" : "true",
        FormPropertyType.Color => "#123456",
        FormPropertyType.Enum => p.AllowedValues!.First(v => !string.Equals(v, p.Default, StringComparison.OrdinalIgnoreCase)),
        FormPropertyType.Size => "75, 23",
        FormPropertyType.Font => "Arial, 11pt, style=Bold",
        FormPropertyType.Padding => "4",
        FormPropertyType.Cursor => "Hand",
        FormPropertyType.Fraction => "0.5",
        FormPropertyType.Reference => "btnOther",
        _ => "sample"
    };

    private static IEnumerable<TestCaseData> EveryWebElementKind() =>
        FormControlCatalog.For(FormTarget.Web)
            .Where(d => d.HtmlTag != null)
            .Select(d => new TestCaseData(d.Kind).SetName("{m}(" + d.Kind + ")"));

    private static (string Html, string Css) Page(FormControlDef definition, Action<FormControl> customise)
    {
        var form = new FormDocument { Target = FormTarget.Web, Name = "F", Layout = new FormLayout() };
        var control = FormCatalogShapes.Canonical(form, definition, "c");
        customise(control);
        return (FormAssetEmitter.Html(form, "app.js"), FormAssetEmitter.Css(form));
    }

    /// <summary>
    /// ⛔ D2: every WinForms-only row of every web kind, set to a non-default value, changes NOTHING on the page — the
    /// markup and the stylesheet are byte-identical to the control without them. Catalog-driven: a new WinForms-only row
    /// that leaks (a CssProperty on it, an emitter reading it by name) fails here the day it is added.
    /// </summary>
    [TestCaseSource(nameof(EveryWebElementKind))]
    public void NoWinFormsOnlyRow_ReachesThePage(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;
        var winFormsOnly = definition.Properties.Where(p => !p.AppliesTo(FormTarget.Web)).ToList();

        var without = Page(definition, _ => { });
        var with = Page(definition, c =>
        {
            foreach (var row in winFormsOnly)
            {
                c.Properties[row.Name] = Sample(row);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(with.Html, Is.EqualTo(without.Html),
                $"{kind}: a WinForms-only row reached the markup ({string.Join(", ", winFormsOnly.Select(r => r.Name))})");
            Assert.That(with.Css, Is.EqualTo(without.Css), $"{kind}: a WinForms-only row reached the stylesheet");
        });
    }

    [TestCaseSource(nameof(EveryWebElementKind))]
    public void EveryWebElementKind_OffersCssClassAndStyle_OnTheWebOnly(string kind)
    {
        var definition = FormControlCatalog.Find(kind)!;

        Assert.Multiple(() =>
        {
            foreach (var name in new[] { "CssClass", "Style" })
            {
                var row = definition.Property(name);
                Assert.That(row, Is.Not.Null, $"{kind}.{name}");

                // ⚠ The one exception: ProgressBar's own WinForms "Style" (ProgressBarStyle) holds the name.
                if (kind == "ProgressBar" && name == "Style")
                {
                    Assert.That(row?.Type, Is.EqualTo(FormPropertyType.Enum), "ProgressBar keeps WinForms' Style");
                    continue;
                }

                Assert.That(row?.AppliesTo(FormTarget.Web), Is.True, $"{kind}.{name} is a web row");
                Assert.That(row?.AppliesTo(FormTarget.WinForms), Is.False, $"{kind}.{name} does not exist on WinForms (D2)");
            }
        });
    }

    /// <summary>A row is found by NAME (never by target), so no kind may carry two rows of one name.</summary>
    [Test]
    public void NoKind_HasTwoRowsOfOneName()
    {
        var duplicated = FormControlCatalog.All.Append(FormControlCatalog.FormRoot)
            .SelectMany(d => d.Properties.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1).Select(g => $"{d.Kind}.{g.Key}"))
            .ToList();

        Assert.That(duplicated, Is.Empty);
    }

    [Test]
    public void AComponentOrAWinFormsOnlyKind_HasNoCssClass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormControlCatalog.Find("Timer")!.Property("CssClass"), Is.Null, "a Timer is not an element");
            Assert.That(FormControlCatalog.Find("DataGridView")!.Property("CssClass"), Is.Null, "no web row at all");
        });
    }

    [Test]
    public void CssClass_JoinsTheElementsOwnClass()
    {
        var (html, _) = Page(FormControlCatalog.Find("Button")!, c => c.Properties["CssClass"] = "primary big-one");

        Assert.That(html, Does.Contain("class=\"vgs-Button primary big-one\""));
    }

    [TestCase("a\" onclick=\"x")]
    [TestCase("x;y")]
    [TestCase("1abc")]
    [TestCase("a<b")]
    public void AnUnusableCssClass_IsDegraded_AndLeftOutOfThePage(string value)
    {
        var row = FormControlCatalog.Find("Button")!.Property("CssClass")!;
        var (html, _) = Page(FormControlCatalog.Find("Button")!, c => c.Properties["CssClass"] = value);

        Assert.Multiple(() =>
        {
            Assert.That(row.Accepts(value, FormTarget.Web), Is.False);
            Assert.That(html, Does.Contain("class=\"vgs-Button\""), "the element keeps only its own class");
            Assert.That(html, Does.Not.Contain("onclick"));
        });
    }

    [Test]
    public void Style_IsTheElementsStyleAttribute_Escaped()
    {
        var (html, _) = Page(FormControlCatalog.Find("Label")!, c => c.Properties["Style"] = "color: red; content: \"x\"");

        Assert.That(html, Does.Contain("style=\"color: red; content: &quot;x&quot;\""));
    }

    // ==================================================================
    // The web-mapped D1 rows (plan 3.4)
    // ==================================================================

    [Test]
    public void ATextBoxsPlaceholderText_IsItsPlaceholder()
    {
        var (html, _) = Page(FormControlCatalog.Find("TextBox")!, c => c.Properties["PlaceholderText"] = "User name");

        Assert.That(html, Does.Contain("placeholder=\"User name\""));
    }

    [TestCase("TextBox")]
    [TestCase("NumericUpDown")]
    public void AHorizontalTextAlign_IsTextAlign(string kind)
    {
        var (_, css) = Page(FormControlCatalog.Find(kind)!, c => c.Properties["TextAlign"] = "Right");

        Assert.That(css, Does.Contain("text-align: right"));
    }

    [Test]
    public void APanelsAutoScroll_IsOverflowAuto_AndOffSaysNothing()
    {
        var (_, on) = Page(FormControlCatalog.Find("Panel")!, c => c.Properties["AutoScroll"] = "true");
        var (_, off) = Page(FormControlCatalog.Find("Panel")!, c => c.Properties["AutoScroll"] = "false");

        Assert.Multiple(() =>
        {
            Assert.That(on, Does.Contain("overflow: auto"));
            Assert.That(off, Does.Not.Contain("overflow"));
        });
    }

    [Test]
    public void ANumericUpDownsReadOnly_IsReadonly()
    {
        var (html, _) = Page(FormControlCatalog.Find("NumericUpDown")!, c => c.Properties["ReadOnly"] = "true");

        Assert.That(html, Does.Contain(" readonly"));
    }

    /// <summary>
    /// Found by the D2 sweep's construction: the emitter's boolean flags read <c>Enabled</c>/<c>Checked</c> by NAME, so a
    /// MenuStrip's WinForms-only Enabled=false put <c>disabled</c> on its &lt;nav&gt; and a menu item's WinForms-only Checked
    /// put <c>checked</c> on an &lt;li&gt;. A flag is honoured only through a row that exists on the web.
    /// </summary>
    [TestCase("MenuStrip", "Enabled", "false", " disabled")]
    [TestCase("ToolStripMenuItem", "Checked", "true", " checked")]
    public void AFlagOnAWinFormsOnlyRow_NeverReachesTheMarkup(string kind, string row, string value, string attribute)
    {
        var (html, _) = Page(FormControlCatalog.Find(kind)!, c => c.Properties[row] = value);

        Assert.That(html, Does.Not.Contain(attribute));
    }
}
