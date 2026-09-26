using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec §2.7/§3 — an ABSENT property displays the TARGET's default (greyed, not bold); BOLD is present
/// AND different from it; RESET removes it from the document. Before this an unset Enabled — default
/// true — displayed as False: a live display lie.
/// </summary>
[TestFixture]
public class FormPropertyRowDefaultTests
{
    private static (FormFile File, FormPropertyGridViewModel Grid) Open(string controlXml, string id, string file = "F.blform")
    {
        var text = file.EndsWith(".blform", StringComparison.Ordinal)
            ? $"<Form Name=\"F\" Version=\"1\" Width=\"400\" Height=\"300\"><Controls>{controlXml}</Controls></Form>"
            : $"<WebForm Name=\"F\" Version=\"1\"><Controls>{controlXml}</Controls></WebForm>";
        var form = FormDocumentReader.Read(file, text);
        var grid = new FormPropertyGridViewModel();
        grid.Load(form);
        grid.SelectedControl = form.Model.FindById(id);
        return (form, grid);
    }

    private static FormPropertyRow Row(FormPropertyGridViewModel grid, string name) =>
        grid.Rows.Single(r => r.Name == name);

    [Test]
    public void AnAbsentBoolRow_DisplaysTheTargetsDefault_NotFalse()
    {
        var (_, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>", "lbl");
        var enabled = Row(grid, "Enabled");

        Assert.Multiple(() =>
        {
            Assert.That(enabled.BoolValue, Is.True, "an unset Enabled is enabled — it was displayed as False");
            Assert.That(enabled.IsPresent, Is.False);
            Assert.That(enabled.IsDefaultShown, Is.True, "greyed");
            Assert.That(enabled.IsBold, Is.False);
            Assert.That(enabled.CanReset, Is.False, "nothing to reset");
        });
    }

    [Test]
    public void APresentValueEqualToTheDefault_IsNotBold()
    {
        var (_, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\" Enabled=\"true\"/>", "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(Row(grid, "Enabled").IsBold, Is.False, "bold = present AND different from the default");
            Assert.That(Row(grid, "Enabled").CanReset, Is.True, "present, so Reset is offered");
        });
    }

    [Test]
    public void APresentValueDifferentFromTheDefault_IsBold()
    {
        var (_, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\" Enabled=\"false\"/>", "lbl");

        Assert.That(Row(grid, "Enabled").IsBold, Is.True);
    }

    [Test]
    public void APresentTextOverANullDefault_IsBold_ButAnEmptyTextIsNot()
    {
        var (_, set) = Open("<Label Id=\"lbl\" TabIndex=\"0\" Text=\"Hi\"/>", "lbl");
        var (_, empty) = Open("<Label Id=\"lbl\" TabIndex=\"0\" Text=\"\"/>", "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(Row(set, "Text").IsBold, Is.True);
            Assert.That(Row(empty, "Text").IsBold, Is.False, "the displayed default of a null default is empty");
        });
    }

    [Test]
    public void Reset_RemovesThePropertyFromTheDocument_AndRaisesEditedOnce()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\" Enabled=\"false\"/>", "lbl");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Row(grid, "Enabled").ResetCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("lbl")!.Properties.ContainsKey("Enabled"), Is.False,
                "Reset REMOVES — it never writes the default value");
            Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("Enabled="));
            Assert.That(edits, Is.EqualTo(1));
            Assert.That(Row(grid, "Enabled").BoolValue, Is.True, "and the row now shows the default");
        });
    }

    /// <summary>
    /// ⛔ The editor pushes its displayed value back (a TextBox on LostFocus, a combo on render). For an
    /// absent row that value is the DEFAULT, and writing it would make every selection-and-tab-away add
    /// attributes the user never set.
    /// </summary>
    [Test]
    public void PushingTheDisplayedDefaultBack_WritesNothing()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\"/>", "lbl");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Row(grid, "Enabled").BoolValue = true;
        Row(grid, "TextAlign").StringValue = "TopLeft";

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That(file.Model.FindById("lbl")!.Properties, Is.Empty);
        });
    }

    /// <summary>
    /// Spec §7: "invalid typed value → refused in the editor, never written". Owned HERE (slice 2), because
    /// Commit is the one door every editor goes through. Before this a typed "maybe" in a Bool row's text
    /// was written and the row froze Degraded on the next open — the designer manufacturing its own D9 case.
    /// </summary>
    [Test]
    public void AnInvalidTypedValue_IsRefused_NeverWritten_AndTheEditorSnapsBack()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>", "lbl");
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        var align = Row(grid, "TextAlign");
        var refreshed = new List<string?>();
        align.PropertyChanged += (_, e) => refreshed.Add(e.PropertyName);

        Row(grid, "Enabled").StringValue = "maybe";
        align.StringValue = "Bogus";
        Row(grid, "ForeColor").StringValue = "12345";

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That(file.Model.FindById("lbl")!.Properties, Is.Empty, "nothing invalid reaches the document");
            Assert.That(refreshed, Does.Contain(nameof(FormPropertyRow.StringValue)),
                "the editor is told to re-read, so it shows the value the document holds, not the refused text");
        });
    }

    /// <summary>
    /// Clearing a typed row's editor is RESET, not a refusal (spec §7): a present BackColor emptied by the
    /// user leaves the document, and the row shows the default again.
    /// </summary>
    [Test]
    public void ClearingAPresentTypedValue_ResetsIt()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\" BackColor=\"Red\"/>", "lbl");
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        var back = Row(grid, "BackColor");

        back.StringValue = "";

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("lbl")!.Properties.ContainsKey("BackColor"), Is.False, "removed, like Reset");
            Assert.That(FormDocumentWriter.Write(file), Does.Not.Contain("BackColor="));
            Assert.That(back.IsPresent, Is.False);
            Assert.That(back.IsDefaultShown, Is.True, "greyed: the default is showing again");
            Assert.That(back.IsBold, Is.False);
            Assert.That(edits, Is.EqualTo(1));
        });
    }

    [Test]
    public void ClearingAStringValue_WritesTheEmptyString()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\" Text=\"Hi\"/>", "lbl");

        Row(grid, "Text").StringValue = "";

        Assert.That(file.Model.FindById("lbl")!.Properties["Text"], Is.EqualTo(""),
            "an empty caption is a real String value, not a reset");
    }

    [Test]
    public void AValueUnusableOnThisTarget_IsRefused()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\"/>", "lbl", "F.blwebform");

        Row(grid, "ForeColor").StringValue = "ActiveCaption"; // a system colour with no CSS equivalent

        Assert.That(file.Model.FindById("lbl")!.Properties.ContainsKey("ForeColor"), Is.False);
    }

    [Test]
    public void AnAliasValue_DisplaysItsCanonicalMember_AndIsNotBoldWhenItIsTheDefault()
    {
        var (_, grid) = Open("<Button Id=\"btn\" TabIndex=\"0\" TextAlign=\"Center\"/>", "btn");
        var align = Row(grid, "TextAlign");

        Assert.Multiple(() =>
        {
            Assert.That(align.StringValue, Is.EqualTo("MiddleCenter"));
            Assert.That(align.IsBold, Is.False, "Center is MiddleCenter, a Button's WinForms default (spec §2.8)");
        });
    }

    [Test]
    public void OnTheWeb_TheRowShowsTheWebDefault()
    {
        var control = new FormControl { Kind = "Label", Id = "lbl" };
        var def = new FormPropertyDef("Interval", FormPropertyType.Int, "100", WebDefault: "250",
            Category: FormPropertyCategory.Behavior, Description: "x");

        var row = new FormPropertyRow(control, def, FormTarget.Web, null, () => { });

        Assert.That(row.IntValue, Is.EqualTo(250));
    }

    /// <summary>
    /// ⛔ A web TextBox's MaxLength has NO default (the browser imposes no limit), so the absent row
    /// displays "" — which the numeric editor renders as 0 and pushes straight back. Writing that echo
    /// produced MaxLength="0": a text box that accepts no input, for a selection click.
    ///
    /// <para>⚠ The absent state still SHOWS 0: ITypedValueRow.IntValue is an int and the
    /// NumericUpDown cannot render "empty". It is greyed (IsDefaultShown), and the echo is inert.</para>
    /// </summary>
    [Test]
    public void AnAbsentIntRowWithNoDefault_PushedBackWhatItShows_WritesNothing_ButARealEditDoes()
    {
        var (file, grid) = Open("<TextBox Id=\"txt\" TabIndex=\"0\"/>", "txt", "F.blwebform");
        var edits = 0;
        grid.Edited += (_, _) => edits++;
        var row = Row(grid, "MaxLength");

        row.IntValue = row.IntValue;

        Assert.Multiple(() =>
        {
            Assert.That(row.IntValue, Is.Zero, "what the numeric editor can show for 'no limit'");
            Assert.That(row.IsDefaultShown, Is.True);
            Assert.That(file.Model.FindById("txt")!.Properties.ContainsKey("MaxLength"), Is.False,
                "the editor's echo of the absent state is not an edit");
            Assert.That(edits, Is.Zero);
        });

        row.IntValue = 10;

        Assert.Multiple(() =>
        {
            Assert.That(file.Model.FindById("txt")!.Properties["MaxLength"], Is.EqualTo("10"));
            Assert.That(edits, Is.EqualTo(1));
        });
    }

    /// <summary>
    /// The view repaints from notifications alone: a write, a clear-reset and a Reset must each tell the
    /// row's bold/greyed/Reset bindings and the editors to re-read, and re-evaluate the Reset command.
    /// </summary>
    [Test]
    public void EveryValueChange_NotifiesTheBindingsThatDependOnIt()
    {
        var (_, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\" BackColor=\"Red\"/>", "lbl");
        var enabled = Row(grid, "Enabled");
        var back = Row(grid, "BackColor");

        (HashSet<string?> Names, int CanExecute) Record(FormPropertyRow row, Action act)
        {
            var names = new HashSet<string?>();
            var canExecute = 0;
            void OnChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e) => names.Add(e.PropertyName);
            void OnCan(object? s, EventArgs e) => canExecute++;
            row.PropertyChanged += OnChanged;
            row.ResetCommand.CanExecuteChanged += OnCan;
            act();
            row.PropertyChanged -= OnChanged;
            row.ResetCommand.CanExecuteChanged -= OnCan;
            return (names, canExecute);
        }

        var expected = new[]
        {
            nameof(FormPropertyRow.IsBold), nameof(FormPropertyRow.IsPresent), nameof(FormPropertyRow.IsDefaultShown),
            nameof(FormPropertyRow.CanReset), nameof(FormPropertyRow.StringValue), nameof(FormPropertyRow.BoolValue),
            nameof(FormPropertyRow.IntValue), nameof(FormPropertyRow.DisplayValue), nameof(FormPropertyRow.RawValue)
        };

        var write = Record(enabled, () => enabled.BoolValue = false);
        var clear = Record(back, () => back.StringValue = "");
        var reset = Record(enabled, () => enabled.ResetCommand.Execute(null));

        Assert.Multiple(() =>
        {
            Assert.That(write.Names, Is.SupersetOf(expected), "a write");
            Assert.That(write.CanExecute, Is.GreaterThanOrEqualTo(1), "a write makes Reset available");
            Assert.That(clear.Names, Is.SupersetOf(expected), "a clear-reset");
            Assert.That(clear.CanExecute, Is.GreaterThanOrEqualTo(1));
            Assert.That(reset.Names, Is.SupersetOf(expected), "a Reset");
            Assert.That(reset.CanExecute, Is.GreaterThanOrEqualTo(1), "a Reset makes Reset unavailable");
        });
    }

    /// <summary>
    /// ⚠ RelayCommand.Execute does not consult CanExecute, so a caller (a context menu, a test, a key
    /// binding) can run Reset on an absent row; that must not report an edit.
    /// </summary>
    [Test]
    public void ResetOnAnAbsentRow_DoesNothing_AndRaisesNoEdit()
    {
        var (file, grid) = Open("<Label Id=\"lbl\" TabIndex=\"0\"/>", "lbl");
        var edits = 0;
        grid.Edited += (_, _) => edits++;

        Row(grid, "Enabled").ResetCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(edits, Is.Zero);
            Assert.That(file.Model.FindById("lbl")!.Properties, Is.Empty);
        });
    }

    [Test]
    public void TheRowCarriesItsCatalogCategoryAndDescription()
    {
        // ⚠ Geometry is REQUIRED here: without X/Y/Width/Height the control has no PixelGeometry, the
        // grid builds no X row, and Row(grid, "X") throws whatever the implementation does.
        var (_, grid) = Open("<Label Id=\"lbl\" X=\"0\" Y=\"0\" Width=\"10\" Height=\"10\" TabIndex=\"0\"/>", "lbl");

        Assert.Multiple(() =>
        {
            Assert.That(Row(grid, "Enabled").Category, Is.EqualTo("Behavior"));
            Assert.That(Row(grid, "Enabled").Description, Is.EqualTo("Indicates whether the control is enabled."));
            Assert.That(Row(grid, "TabIndex").Category, Is.EqualTo("Behavior"), "intrinsic rows are categorised too");
            Assert.That(Row(grid, "X").Category, Is.EqualTo("Layout"));
        });
    }
}
