using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 23: a web property becomes an HTML attribute because the CATALOG says so.
///
/// <para>⛔⛔ Before this the emitter carried a hand-written <c>if</c> per property —
/// <c>MaxLength</c>, <c>GroupName</c>, <c>Image</c> — which is a second list beside
/// <see cref="FormControlCatalog"/> and goes stale the moment a row is added. Widening the catalog
/// to twenty-three kinds on top of that would have meant editing two files per property and
/// discovering the misses only by opening a page. A row now declares its <c>HtmlAttribute</c> or it
/// does not reach the page at all.</para>
///
/// <para>⚠ The WinForms name and the HTML name genuinely differ — <c>Minimum</c> is <c>min</c> — and
/// nothing but the catalog can know that.</para>
/// </summary>
[TestFixture]
public class FormHtmlAttributeTests
{
    private static string Html(string kind, params (string Name, string Value)[] properties)
    {
        var form = new FormDocument
        {
            Target = FormTarget.Web,
            Name = "W",
            Layout = new FormLayout { Kind = FormLayoutKind.Grid, Cols = "1fr", Rows = "auto" }
        };

        var control = new FormControl
        {
            Kind = kind, Id = "ctl", TabIndex = 0, Geometry = new GridGeometry { Col = 0, Row = 0 }
        };

        foreach (var (name, value) in properties)
        {
            control.Properties[name] = value;
        }

        form.Controls.Add(control);
        return FormAssetEmitter.Html(form, "Site.js");
    }

    [Test]
    public void AWinFormsNameBecomesItsHtmlSpelling()
    {
        var html = Html("NumericUpDown", ("Minimum", "5"), ("Maximum", "50"), ("Value", "7"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("min=\"5\""));
            Assert.That(html, Does.Contain("max=\"50\""));
            Assert.That(html, Does.Contain("value=\"7\""));
            Assert.That(html, Does.Not.Contain("Minimum="),
                "the WinForms spelling is not an HTML attribute");
        });
    }

    /// <summary>
    /// ⛔ A property the catalog marks WinForms-only must not reach the page. <c>DecimalPlaces</c>
    /// is not something <c>&lt;input type="number"&gt;</c> has ever heard of, and emitting it would
    /// put an attribute in the markup that describes a behaviour the page cannot have.
    /// </summary>
    [Test]
    public void AWinFormsOnlyPropertyDoesNotLeakIntoTheMarkup()
    {
        var html = Html("NumericUpDown", ("Minimum", "0"), ("DecimalPlaces", "2"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("min=\"0\""));
            Assert.That(html, Does.Not.Contain("DecimalPlaces"));
            Assert.That(html, Does.Not.Contain("decimalplaces"));
        });
    }

    /// <summary>The regression this refactor could most easily have caused, silently.</summary>
    [Test]
    public void MaxLengthStillEmits_NowFromTheCatalogRatherThanAHandWrittenLine()
    {
        Assert.That(Html("TextBox", ("MaxLength", "12")), Does.Contain("maxlength=\"12\""));
    }

    /// <summary>
    /// MaxLength's WinForms Default is 32767 and its WebDefault is "none" — both are DISPLAY defaults.
    /// An absent MaxLength must emit no attribute: an <c>&lt;input&gt;</c> with no maxlength is
    /// unlimited, and a written 32767 would cap the page at a WinForms limit nobody set.
    /// </summary>
    [Test]
    public void ATextBoxWithNoMaxLength_EmitsNoMaxlengthAttribute()
    {
        var html = Html("TextBox", ("Text", "hello"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("id=\"ctl\""), "the control was emitted");
            Assert.That(html, Does.Not.Contain("maxlength"));
        });
    }

    [Test]
    public void ATrackBarBecomesARangeInputCarryingItsBounds()
    {
        var html = Html("TrackBar", ("Minimum", "1"), ("Maximum", "9"), ("Value", "4"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("type=\"range\""));
            Assert.That(html, Does.Contain("min=\"1\""));
            Assert.That(html, Does.Contain("max=\"9\""));
        });
    }

    [Test]
    public void AProgressBarBecomesAProgressElement()
    {
        var html = Html("ProgressBar", ("Maximum", "200"), ("Value", "50"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<progress"));
            Assert.That(html, Does.Contain("max=\"200\""));
            Assert.That(html, Does.Contain("value=\"50\""));
        });
    }

    /// <summary>
    /// ⚠ <c>ProgressBar.Minimum</c> is WinForms-only on purpose: <c>&lt;progress&gt;</c> has no
    /// minimum, it is always zero-based. Declaring it web-capable would emit an attribute browsers
    /// ignore, which reads as working and is not.
    /// </summary>
    [Test]
    public void AProgressBarsMinimumIsNotEmittedBecauseProgressHasNone()
    {
        Assert.That(Html("ProgressBar", ("Minimum", "10")), Does.Not.Contain("min=\"10\""));
    }

    [Test]
    public void AnEmptyValueEmitsNoAttributeAtAll()
    {
        Assert.That(Html("NumericUpDown", ("Minimum", "")), Does.Not.Contain("min="));
    }
}
