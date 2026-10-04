using Avalonia.Media;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 4 D-1: what the colour drop-down offers and what it writes. ⛔ Every list is the catalog's (D-1d) — the expected
/// values here are DERIVED from <see cref="FormSystemColors"/> / <see cref="FormKnownColors"/>, never a hand list.
/// </summary>
[TestFixture]
public class FormColorChoicesTests
{
    private static FormPropertyDef BackColor =>
        FormControlCatalog.Find("Label")!.Properties.Single(p => p.Name == "BackColor");

    [Test]
    public void TheSystemTab_OnWinForms_IsAll33SystemColours()
    {
        Assert.That(FormColorChoices.System(BackColor, FormTarget.WinForms).Select(c => c.Name),
            Is.EqualTo(FormSystemColors.Names));
        Assert.That(FormSystemColors.Names, Has.Count.EqualTo(33), "precondition: the table's size");
    }

    [Test]
    public void TheSystemTab_OnTheWeb_IsOnlyTheColoursWithACssMeaning()
    {
        var withCss = FormSystemColors.Names.Where(n => FormSystemColors.CssFor(n) != null).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(FormColorChoices.System(BackColor, FormTarget.Web).Select(c => c.Name), Is.EqualTo(withCss));
            Assert.That(withCss, Has.Count.EqualTo(8), "precondition: eight system colours have CSS");
            Assert.That(withCss, Does.Not.Contain("ActiveCaption").And.Contain("Window"));
        });
    }

    [TestCase(FormTarget.WinForms)]
    [TestCase(FormTarget.Web)]
    public void TheWebTab_IsTheNamedColours(FormTarget target)
    {
        Assert.That(FormColorChoices.Web(BackColor, target).Select(c => c.Name), Is.EqualTo(FormKnownColors.Names));
    }

    [Test]
    public void EveryOfferedEntry_HasASwatch_OnWinForms()
    {
        var all = FormColorChoices.Web(BackColor, FormTarget.WinForms).Concat(FormColorChoices.System(BackColor, FormTarget.WinForms));
        Assert.That(all.Where(c => c.Swatch == null).Select(c => c.Name), Is.Empty, "every WinForms name previews");
    }

    /// <summary>D-1c: <c>#RRGGBB</c> when opaque, <c>#AARRGGBB</c> otherwise — both the catalog's canonical, accepted on both targets.</summary>
    [TestCase(255, 255, 0, 0, "#FF0000")]
    [TestCase(128, 255, 0, 0, "#80FF0000")]
    [TestCase(255, 1, 2, 171, "#0102AB")]
    [TestCase(0, 0, 0, 0, "#00000000")]
    public void ACustomColour_IsWrittenAsCanonicalHex(int a, int r, int g, int b, string expected)
    {
        var text = FormColorChoices.ToDocumentText(Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b));

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(BackColor.Accepts(text, FormTarget.WinForms), Is.True);
            Assert.That(BackColor.Accepts(text, FormTarget.Web), Is.True);
            Assert.That(BackColor.Canonical(text), Is.EqualTo(text), "already the canonical spelling");
        });
    }

    [TestCase("#F00", 255, 255, 0, 0)]
    [TestCase("#00FF00", 255, 0, 255, 0)]
    [TestCase("#800000FF", 128, 0, 0, 255)]
    [TestCase("Red", 255, 255, 0, 0)]
    [TestCase("red", 255, 255, 0, 0)]
    public void TheSwatch_ResolvesHexAndNames(string value, int a, int r, int g, int b)
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormColorChoices.TryResolve(value, out var color), Is.True);
            Assert.That(color, Is.EqualTo(Color.FromArgb((byte)a, (byte)r, (byte)g, (byte)b)));
        });
    }

    [TestCase("")]
    [TestCase("Bogus")]
    [TestCase("#12")]
    [TestCase("#GGHHII")]
    [TestCase("RebeccaPurple", Description = "a CSS name the web ACCEPTS but neither System.Drawing nor Avalonia knows: '?', never a wrong colour")]
    public void AnUnknownValue_HasNoSwatch(string value)
    {
        Assert.That(FormColorChoices.TryResolve(value, out _), Is.False);
    }

    /// <summary>The drop-down opens on the tab holding the current value, as VS's does.</summary>
    [TestCase("Control", FormColorTab.System)]
    [TestCase("window", FormColorTab.System)]
    [TestCase("Red", FormColorTab.Web)]
    [TestCase("#FF0000", FormColorTab.Custom)]
    [TestCase("", FormColorTab.Custom)]
    [TestCase(null, FormColorTab.Custom)]
    public void TheDropDown_OpensOnTheTabHoldingTheValue(string? value, FormColorTab expected)
    {
        Assert.That(FormColorChoices.TabFor(value), Is.EqualTo(expected));
    }

    [Test]
    public void ASystemColour_Previews_OnThisMachine()
    {
        Assert.That(FormColorChoices.TryResolve("Control", out _), Is.True);
    }

    // ==================================================================
    // The row (FormPropertyRow): IsColor, its lists, its swatch, ApplyColor
    // ==================================================================

    private static (FormFile File, FormPropertyGridViewModel Grid) Open(string controlXml, string file = "F.blform")
    {
        var text = file.EndsWith(".blform", StringComparison.Ordinal)
            ? $"<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\"><Controls>{controlXml}</Controls></Form>"
            : $"<WebForm Name=\"F\" Version=\"1\"><Controls>{controlXml}</Controls></WebForm>";
        var form = FormDocumentReader.Read(file, text);
        var grid = new FormPropertyGridViewModel();
        grid.Load(form);
        grid.SelectedControl = form.Model.FindById("lbl");
        return (form, grid);
    }

    [Test]
    public void AColourRow_OffersTheCatalogsLists_ForItsTarget()
    {
        var (_, win) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>");
        var (_, web) = Open("<Label Id=\"lbl\" TabIndex=\"0\"/>", "F.blwebform");
        var winRow = win.Rows.Single(r => r.Name == "BackColor");
        var webRow = web.Rows.Single(r => r.Name == "BackColor");

        Assert.Multiple(() =>
        {
            Assert.That(winRow.IsColor, Is.True);
            Assert.That(winRow.SystemColorChoices, Has.Count.EqualTo(33));
            Assert.That(webRow.SystemColorChoices.Select(c => c.Name),
                Is.EqualTo(FormSystemColors.Names.Where(n => FormSystemColors.CssFor(n) != null)));
            Assert.That(webRow.WebColorChoices, Has.Count.EqualTo(FormKnownColors.Names.Count));
        });
    }

    [Test]
    public void ApplyColor_WritesOnce_AndTheSameColourAgainIsANoOp()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>");
        var row = grid.Rows.Single(r => r.Name == "BackColor");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        row.ApplyColor("Red");
        row.ApplyColor("Red");

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("lbl")!.Properties["BackColor"], Is.EqualTo("Red"));
            Assert.That(edits, Is.EqualTo(1), "one write; the same colour again changes nothing");
            Assert.That(row.SwatchColor, Is.EqualTo(Color.FromRgb(255, 0, 0)), "the swatch follows");
            Assert.That(row.HasUnknownSwatch, Is.False);
        });
    }

    /// <summary>"?" means "a value this machine cannot preview" — never "no value": an absent, default-less BackColor is an empty swatch.</summary>
    [Test]
    public void TheQuestionMarkSwatch_IsForAnUnpreviewableValue_NotForAnEmptyOne()
    {
        var (_, absent) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>");
        var (_, css) = Open("<Label Id=\"lbl\" TabIndex=\"0\" BackColor=\"RebeccaPurple\"/>", "F.blwebform");

        Assert.Multiple(() =>
        {
            Assert.That(absent.Rows.Single(r => r.Name == "BackColor").HasUnknownSwatch, Is.False, "empty: no '?'");
            Assert.That(css.Rows.Single(r => r.Name == "BackColor").HasUnknownSwatch, Is.True, "a CSS-only name: '?'");
        });
    }

    [Test]
    public void AFrozenColour_HasNoDropDown()
    {
        var (_, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\" BackColor=\"#12\"/>");
        var row = grid.Rows.Single(r => r.Name == "BackColor");

        Assert.Multiple(() =>
        {
            Assert.That(row.IsFrozen, Is.True, "precondition: a Degraded colour");
            Assert.That(row.IsColor, Is.False, "no drop-down over a frozen value");
            Assert.That(row.SwatchColor, Is.Null);
        });
    }
}
