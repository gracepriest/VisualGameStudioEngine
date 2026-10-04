using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 4 D-2: the Font dialog's view model. ⛔ The families come from a seam (the installed list differs per machine —
/// pre-flight M6), so these tests never depend on what this machine has.
/// </summary>
[TestFixture]
public class FormFontDialogViewModelTests
{
    private static readonly FormFontValue SegoeUi9 = new("Segoe UI", 9m, false, false, false, false);

    [Test]
    public void AFamilyTheCatalogWouldRefuse_IsNeverOffered()
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial", "Font & Co", "Verdana", "Semi;Colon" }, SegoeUi9, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(vm.AllFamilies, Is.EqualTo(new[] { "Arial", "Segoe UI", "Verdana" }),
                "sorted; the two the parser refuses are gone; Segoe UI (WinForms' default) is always there");
            Assert.That(vm.AllFamilies.All(f => FormFontValue.TryParse(f + ", 9pt", out _)), Is.True);
        });
    }

    [Test]
    public void TheCurrentValuesFamily_IsOffered_EvenWhenNotInstalled_AndSelected()
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial" }, SegoeUi9 with { Family = "Tahoma" }, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(vm.AllFamilies, Does.Contain("Tahoma"), "a Linux IDE must still show the form's own font");
            Assert.That(vm.SelectedFamily, Is.EqualTo("Tahoma"));
            Assert.That(vm.SizeText, Is.EqualTo("9"));
        });
    }

    [Test]
    public void BoldAndItalic_AcceptAsOneCanonicalValue()
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial" }, SegoeUi9, FormTarget.WinForms)
        {
            SelectedFamily = "Arial",
            SizeText = "10",
            Bold = true,
            Italic = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(vm.Accept(), Is.EqualTo("Arial, 10pt, style=Bold, Italic"));
            Assert.That(vm.Result, Is.EqualTo("Arial, 10pt, style=Bold, Italic"));
        });
    }

    [Test]
    public void Cancel_ProducesNoResult()
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial" }, SegoeUi9, FormTarget.WinForms) { Bold = true };

        vm.Cancel();

        Assert.That(vm.Result, Is.Null);
    }

    [TestCase("9.5", true)]
    [TestCase("72", true)]
    [TestCase("0", false)]
    [TestCase("9.125", false)]
    [TestCase("big", false)]
    public void ASizeIsAcceptedOnlyWhenTheParserAcceptsIt(string size, bool accepted)
    {
        var vm = new FormFontDialogViewModel(Array.Empty<string>(), SegoeUi9, FormTarget.WinForms) { SizeText = size };

        Assert.Multiple(() =>
        {
            Assert.That(vm.CanAccept, Is.EqualTo(accepted));
            Assert.That(vm.Accept() != null, Is.EqualTo(accepted), "OK never produces a value the catalog refuses");
        });
    }

    /// <summary>Part C review: the size box speaks the user's culture (sv-SE types <c>9,75</c>); the document stays invariant.</summary>
    [TestCase("sv-SE", "9,75", "Arial, 9.75pt")]
    [TestCase("sv-SE", "9.75", "Arial, 9.75pt")]
    [TestCase("de-DE", "10,5", "Arial, 10.5pt")]
    [TestCase("en-US", "9.75", "Arial, 9.75pt")]
    [TestCase("en-US", "9,75", null)]
    public void TheSizeBox_AcceptsTheCulturesDecimalSeparator(string culture, string typed, string? expected)
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial" }, SegoeUi9 with { Family = "Arial" }, FormTarget.WinForms,
            new System.Globalization.CultureInfo(culture)) { SizeText = typed };

        Assert.That(vm.Accept(), Is.EqualTo(expected));
    }

    [Test]
    public void TheSizeBox_ShowsTheStartSizeInTheCulture()
    {
        var vm = new FormFontDialogViewModel(Array.Empty<string>(), SegoeUi9 with { Size = 9.75m }, FormTarget.WinForms,
            new System.Globalization.CultureInfo("sv-SE"));

        Assert.That(vm.SizeText, Is.EqualTo("9,75"));
    }

    /// <summary>
    /// Part C review: the document's family spelling wins over an installed family that differs only in case — so an
    /// untouched OK writes back exactly what the document holds (<c>arial</c>), never a rewrite to <c>Arial</c>.
    /// </summary>
    [Test]
    public void TheDocumentsFamilySpelling_IsKept_OverAnInstalledFamilyDifferingInCase()
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial", "Verdana" }, SegoeUi9 with { Family = "arial" }, FormTarget.WinForms);

        Assert.Multiple(() =>
        {
            Assert.That(vm.AllFamilies, Does.Contain("arial").And.Not.Contain("Arial"));
            Assert.That(vm.SelectedFamily, Is.EqualTo("arial"));
            Assert.That(vm.Accept(), Is.EqualTo("arial, 9pt"));
        });
    }

    [Test]
    public void AnUntouchedOk_OnALowerCaseFamily_ChangesNothingInTheDocument()
    {
        var text = BoldGroupBox.Replace("<Label Id=\"lbl\" X=\"8\" Y=\"20\" Width=\"80\" Height=\"20\" TabIndex=\"0\"/>",
            "<Label Id=\"lbl\" X=\"8\" Y=\"20\" Width=\"80\" Height=\"20\" TabIndex=\"0\" Font=\"arial, 9pt\"/>");
        var file = FormDocumentReader.Read("F.blform", text);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("lbl");
        var font = grid.Rows.Single(r => r.Name == "Font");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        var vm = new FormFontDialogViewModel(new[] { "Arial" }, font.EffectiveFont!, font.Target);
        font.ApplyFont(vm.Accept()!);

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That(FormDocumentWriter.Write(file), Is.EqualTo(text), "byte-identical");
        });
    }

    [Test]
    public void TheFilter_NarrowsTheFamilies()
    {
        var vm = new FormFontDialogViewModel(new[] { "Arial", "Arial Black", "Verdana" }, SegoeUi9, FormTarget.WinForms)
        {
            FilterText = "arial"
        };

        Assert.That(vm.Families, Is.EqualTo(new[] { "Arial", "Arial Black" }));
    }

    [Test]
    public void TheWebHint_ShowsOnlyOnAWebForm()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new FormFontDialogViewModel(Array.Empty<string>(), SegoeUi9, FormTarget.Web).IsWeb, Is.True);
            Assert.That(new FormFontDialogViewModel(Array.Empty<string>(), SegoeUi9, FormTarget.WinForms).IsWeb, Is.False);
        });
    }

    // ==================================================================
    // The row: HasEllipsis, and the start value — the font the control INHERITS when it sets none
    // ==================================================================

    private const string BoldGroupBox = """
        <Form Name="F" Version="1" Width="400" Height="300">
          <Controls>
            <GroupBox Id="grp" X="8" Y="8" Width="200" Height="150" TabIndex="0" Font="Tahoma, 10pt, style=Bold">
              <Label Id="lbl" X="8" Y="20" Width="80" Height="20" TabIndex="0"/>
            </GroupBox>
          </Controls>
        </Form>
        """;

    /// <summary>
    /// ⛔ An absent ambient Font on a Label inside a BOLD GroupBox starts the dialog Bold, at the GroupBox's family and size
    /// — what the control actually shows (<c>FormAmbient</c>), the rule the Font parts already follow (slice-3 review I1).
    /// </summary>
    [Test]
    public void AnAbsentAmbientFont_StartsFromWhatTheControlInherits()
    {
        var file = FormDocumentReader.Read("F.blform", BoldGroupBox);
        Assert.That(file.IsRefused, Is.False, string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("lbl");
        var font = grid.Rows.Single(r => r.Name == "Font");

        var vm = new FormFontDialogViewModel(Array.Empty<string>(), font.EffectiveFont!, font.Target);

        Assert.Multiple(() =>
        {
            Assert.That(font.HasEllipsis, Is.True);
            Assert.That(font.IsPresent, Is.False, "precondition: the Label sets no Font");
            Assert.That(font.EffectiveFont?.Canonical, Is.EqualTo("Tahoma, 10pt, style=Bold"));
            Assert.That(vm.Bold, Is.True, "the dialog starts Bold");
            Assert.That(vm.SelectedFamily, Is.EqualTo("Tahoma"));
        });
    }

    [Test]
    public void OnlyAnEditableFontRow_HasTheEllipsis()
    {
        var file = FormDocumentReader.Read("F.blform", BoldGroupBox);
        var grid = new FormPropertyGridViewModel();
        grid.Load(file);
        grid.SelectedControl = file.Model.FindById("lbl");

        Assert.That(grid.Rows.Where(r => r.HasEllipsis).Select(r => r.Name), Is.EqualTo(new[] { "Font" }));
    }
}
