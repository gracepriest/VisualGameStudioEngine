using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Slice 4 D-7: which controls a Reference row (the Form's AcceptButton / CancelButton) may name. ONE function answers
/// both the property grid's drop-down (<see cref="FormReferences.Candidates"/>) and the region writer's BL8034 check
/// (<see cref="FormReferences.IsAllowed"/>), so the list never offers an Id the writer would then refuse to emit.
/// </summary>
[TestFixture]
public class FormReferencesTests
{
    private static FormPropertyDef AcceptButton =>
        FormControlCatalog.FormRoot.Properties.Single(p => p.Name == "AcceptButton");

    private static FormControl Positioned(string kind, string id, int tab) => new()
    {
        Kind = kind, Id = id, TabIndex = tab, Geometry = new PixelGeometry { X = 8, Y = 8 + tab * 30, Width = 75, Height = 23 }
    };

    /// <summary>
    /// A form with Buttons at the top level and inside a Panel, a Label between them, and a Timer in the tray — in that
    /// document order.
    /// </summary>
    private static FormDocument Form()
    {
        var form = new FormDocument { Target = FormTarget.WinForms, Name = "F", Width = 400, Height = 300, Text = "F" };
        form.Controls.Add(Positioned("Button", "btnOk", 0));
        form.Controls.Add(Positioned("Label", "lblTitle", 1));
        var panel = Positioned("Panel", "pnl", 2);
        panel.Children.Add(Positioned("Button", "btnInner", 0));
        form.Controls.Add(panel);
        form.Controls.Add(Positioned("Button", "btnCancel", 3));
        form.Components.Add(new FormControl { Kind = "Timer", Id = "tmr" });
        return form;
    }

    [Test]
    public void Candidates_AreTheButtons_NestedIncluded_InDocumentOrder()
    {
        Assert.That(FormReferences.Candidates(Form(), AcceptButton), Is.EqualTo(new[] { "btnOk", "btnInner", "btnCancel" }));
    }

    /// <summary>
    /// ⛔ A tray component is never a candidate, even for a row whose kinds would include it: a component has no place
    /// on the form (CLAUDE.md, "a tray component is a FormControl with no place"), and every walker chooses its lists
    /// explicitly. Probed with a row whose kinds name Timer, so the exclusion is the walk's, not the kind filter's.
    /// </summary>
    [Test]
    public void Candidates_NeverIncludeATrayComponent()
    {
        var timerReference = new FormPropertyDef("X", FormPropertyType.Reference, ReferenceKinds: new[] { "Timer" });

        Assert.Multiple(() =>
        {
            Assert.That(FormReferences.Candidates(Form(), timerReference), Is.Empty);
            Assert.That(FormReferences.IsAllowed(Form(), timerReference, "tmr"), Is.False);
        });
    }

    [TestCase("btnOk", true)]
    [TestCase("btnInner", true, Description = "a Button inside a Panel is still a Button on this form")]
    [TestCase("lblTitle", false, Description = "a control of a kind the row does not allow")]
    [TestCase("btnGone", false, Description = "no control has the Id")]
    [TestCase("BTNOK", false, Description = "Ids are ordinal, as FindById and the generated field are")]
    public void IsAllowed_IsMembershipOfTheCandidates(string id, bool allowed)
    {
        var form = Form();

        Assert.Multiple(() =>
        {
            Assert.That(FormReferences.IsAllowed(form, AcceptButton, id), Is.EqualTo(allowed));
            Assert.That(FormReferences.Candidates(form, AcceptButton).Contains(id), Is.EqualTo(allowed),
                "the drop-down offers exactly what the writer emits");
        });
    }

    /// <summary>A row with no kinds allows nothing — never "anything".</summary>
    [Test]
    public void ARowWithNoKinds_AllowsNothing()
    {
        var bare = new FormPropertyDef("X", FormPropertyType.Reference);

        Assert.Multiple(() =>
        {
            Assert.That(FormReferences.Candidates(Form(), bare), Is.Empty);
            Assert.That(FormReferences.IsAllowed(Form(), bare, "btnOk"), Is.False);
        });
    }
}
