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

    // ==================================================================
    // Part C review — a translucent BackColor where WinForms throws on one (pinned by WinFormsTranslucentBackColorRunTests)
    // ==================================================================

    private static FormPropertyDef BackColorOf(string kind) =>
        FormControlCatalog.Find(kind)!.Properties.Single(p => p.Name == "BackColor");

    [TestCase("#80FF0000")]
    [TestCase("#00FFFFFF")]
    [TestCase("Transparent")]
    [TestCase("transparent")]
    public void ATranslucentBackColor_IsRefusedOnAWinFormsTextBox_WithTheReason(string value)
    {
        var textBox = BackColorOf("TextBox");

        Assert.Multiple(() =>
        {
            Assert.That(textBox.Judge(value, null, FormTarget.WinForms), Is.EqualTo(FormEditVerdict.Refuse));
            Assert.That(textBox.DescribeRefusedEdit(value, FormTarget.WinForms),
                Does.Contain("transparent").And.Contain("ArgumentException"));
            Assert.That(textBox.Accepts(value, FormTarget.Web), Is.True, "the web keeps alpha (rgba)");
            Assert.That(BackColorOf("Label").Accepts(value, FormTarget.WinForms), Is.True, "a Label supports it (measured)");
        });
    }

    [TestCase("#FFFF0000")]
    [TestCase("#ff0000")]
    [TestCase("Red")]
    [TestCase("Window")]
    public void AnOpaqueBackColor_StaysAcceptedOnAWinFormsTextBox(string value)
    {
        Assert.That(BackColorOf("TextBox").Accepts(value, FormTarget.WinForms), Is.True);
    }

    [Test]
    public void TheCustomTabsAlpha_AndTheWebTabsTransparent_FollowTheCatalog()
    {
        var (_, textBox) = Open("<TextBox Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>");
        var (_, label) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>");
        var (_, webTextBox) = Open("<TextBox Id=\"lbl\" TabIndex=\"0\"/>", "F.blwebform");
        FormPropertyRow Back(FormPropertyGridViewModel g) => g.Rows.Single(r => r.Name == "BackColor");

        Assert.Multiple(() =>
        {
            Assert.That(Back(textBox).AllowsAlpha, Is.False, "a WinForms TextBox throws on alpha: no alpha channel");
            Assert.That(Back(label).AllowsAlpha, Is.True);
            Assert.That(Back(webTextBox).AllowsAlpha, Is.True, "the web keeps alpha");
            Assert.That(Back(textBox).WebColorChoices.Select(c => c.Name), Does.Not.Contain("Transparent"));
            Assert.That(Back(label).WebColorChoices.Select(c => c.Name), Does.Contain("Transparent"));
        });
    }

    private static string EmitFor(string kind, string backColor)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300, Text = "F" };
        var control = new FormControl
        {
            Kind = kind, Id = "ctl", TabIndex = 0, Geometry = new PixelGeometry { X = 0, Y = 0, Width = 10, Height = 10 }
        };
        control.Properties["BackColor"] = backColor;
        form.Controls.Add(control);
        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");
        return result.Text + "\n" + string.Join("\n", result.Diagnostics.Select(d => d.Message));
    }

    /// <summary>
    /// Part E: a SOURCE-form colour (written as WinForms source) obeys the same refusal — it used to skip the Accepts gate,
    /// so <c>Color.Transparent</c> on a TextBox was emitted and threw when the form was created.
    /// </summary>
    [TestCase("Color.Transparent")]
    [TestCase("Color.FromArgb(128, 255, 0, 0)")]
    [TestCase("Color.FromArgb(0, 0, 0, 0)")]
    public void ATranslucentSourceForm_IsNotEmittedOnATextBox_ButIsOnALabel(string source)
    {
        var textBox = EmitFor("TextBox", source);
        var label = EmitFor("Label", source);

        Assert.Multiple(() =>
        {
            Assert.That(textBox, Does.Not.Contain("ctl.BackColor ="), "never emitted where WinForms throws");
            Assert.That(textBox, Does.Contain("transparent"), "and the warning says why");
            Assert.That(label, Does.Contain("ctl.BackColor = " + source), "a Label supports it (measured)");
        });
    }

    [Test]
    public void AnOpaqueFromArgb_IsStillEmittedOnATextBox_AndAnUnparseableOneNever()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EmitFor("TextBox", "Color.FromArgb(255, 1, 2, 3)"), Does.Contain("ctl.BackColor = Color.FromArgb(255, 1, 2, 3)"));
            Assert.That(EmitFor("TextBox", "Color.FromArgb(x, 1, 2, 3)"), Does.Not.Contain("ctl.BackColor ="));
            Assert.That(EmitFor("Label", "Color.FromArgb(300, 1, 2, 3)"), Does.Not.Contain("ctl.BackColor ="));
        });
    }

    /// <summary>Part E: the Form's refusal points at Opacity — a Form has no transparent background, it has see-through.</summary>
    [TestCase("#80FF0000")]
    [TestCase("Color.Transparent")]
    public void TheFormsTranslucentBackColor_IsRefused_PointingToOpacity(string value)
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300, Text = "F" };
        form.Properties["BackColor"] = value;
        var result = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, form, "F.blform");
        var row = FormControlCatalog.FormRoot.Properties.Single(p => p.Name == "BackColor");

        Assert.Multiple(() =>
        {
            Assert.That(result.Text, Does.Not.Contain("Me.BackColor ="));
            Assert.That(row.DescribeRefusedEdit("#80FF0000", FormTarget.WinForms), Does.Contain("Opacity"));
            Assert.That(string.Join("\n", result.Diagnostics.Select(d => d.Message)), Does.Contain("Opacity"));
        });
    }

    /// <summary>
    /// Part E: a WEB form's translucent colour retargeted to WinForms, on a kind that throws on one, is NAMED with the
    /// catalog's reason (BL8024 RetargetPropertyLost). By the retarget's rule a value the destination refuses crosses
    /// PRESERVED (Degraded there) — and therefore never reaches the window's generated code.
    /// </summary>
    [Test]
    public void AWebTranslucentColour_RetargetedToATextBox_IsNamedWithTheCatalogsReason_AndNeverEmitted()
    {
        var web = FormDocumentReader.Read("F.blwebform",
            "<WebForm Name=\"F\" Version=\"1\"><Controls><TextBox Id=\"txt\" Col=\"0\" Row=\"0\" TabIndex=\"0\" BackColor=\"#80FF0000\"/></Controls></WebForm>");
        Assert.That(web.IsRefused, Is.False);

        var converted = FormRetarget.Convert(web.Model, FormTarget.WinForms);
        var lost = converted.Diagnostics.Where(d => d.Code == DesignCodes.RetargetPropertyLost).Select(d => d.Message).ToList();
        var code = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, converted.Document, "F.blform").Text;

        Assert.Multiple(() =>
        {
            Assert.That(lost, Has.Some.Contains("txt.BackColor"));
            Assert.That(lost.Single(m => m.Contains("txt.BackColor")), Does.Contain("transparent").And.Contain("ArgumentException"));
            Assert.That(code, Does.Not.Contain("txt.BackColor ="), "never emitted into the window");
        });
    }

    /// <summary>An existing document carrying one is Degraded — shown, preserved, never reaching source.</summary>
    [Test]
    public void ADocumentsTranslucentTextBoxBackColor_IsDegraded_AndNeverEmitted()
    {
        var (file, grid) = Open("<TextBox Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\" BackColor=\"#80FF0000\"/>");
        var row = grid.Rows.Single(r => r.Name == "BackColor");
        var code = RegionWriter.Write("F.bas", FormScaffolder.Create("F", FormTarget.WinForms).CodeText, file.Model, "F.blform").Text;

        Assert.Multiple(() =>
        {
            Assert.That(row.IsFrozen, Is.True);
            Assert.That(row.RawValue, Is.EqualTo("#80FF0000"));
            Assert.That(code, Does.Not.Contain("BackColor"), "never reaches the generated source");
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
