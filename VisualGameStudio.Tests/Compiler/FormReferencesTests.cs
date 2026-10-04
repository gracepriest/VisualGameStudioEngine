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

    /// <summary>
    /// Slice 4 review follow-up — VS behaviour, the reference FOLLOWS its object: removing a control through
    /// <see cref="FormDocument.RemoveControl"/> (the ONE model path the designer's Delete and Cut both take) drops every
    /// Reference row naming it — or naming any control inside it (a Panel holding the AcceptButton). A reference to a
    /// control still on the form stays.
    /// </summary>
    [Test]
    public void RemovingAControl_DropsTheReferencesToIt_AndToEverythingInsideIt()
    {
        var form = Form();
        form.Properties["AcceptButton"] = "btnInner";
        form.Properties["CancelButton"] = "btnCancel";

        var removed = form.RemoveControl(form.FindById("pnl")!);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(form.FindById("pnl"), Is.Null);
            Assert.That(form.Properties.ContainsKey("AcceptButton"), Is.False, "it named a Button inside the removed Panel");
            Assert.That(form.Properties["CancelButton"], Is.EqualTo("btnCancel"), "a reference to a control still here stays");
        });
    }

    /// <summary>
    /// Only what THIS removal took is forgotten: a reference already dangling (<c>btnGone</c>, BL8034) is the user's to
    /// fix, and removing an unrelated control leaves it alone. A control that is not in the form removes nothing.
    /// </summary>
    [Test]
    public void RemovingAControl_LeavesAnUnrelatedDanglingReference_AndAStrangerRemovesNothing()
    {
        var form = Form();
        form.Properties["AcceptButton"] = "btnGone";
        form.Properties["CancelButton"] = "btnOk";

        var stranger = form.RemoveControl(Positioned("Button", "btnOk", 9));
        var label = form.RemoveControl(form.FindById("lblTitle")!);

        Assert.Multiple(() =>
        {
            Assert.That(stranger, Is.False, "a control that is not in the form");
            Assert.That(form.FindById("btnOk"), Is.Not.Null, "…removed nothing, even sharing an Id");
            Assert.That(form.Properties["CancelButton"], Is.EqualTo("btnOk"), "…and forgot nothing");
            Assert.That(label, Is.True);
            Assert.That(form.Properties["AcceptButton"], Is.EqualTo("btnGone"), "an unrelated dangling Id is left alone");
        });
    }

    /// <summary>A tray component is removed through the same path (the tray's Delete).</summary>
    [Test]
    public void RemovingATrayComponent_GoesThroughTheSamePath()
    {
        var form = Form();
        Assert.Multiple(() =>
        {
            Assert.That(form.RemoveControl(form.FindById("tmr")!), Is.True);
            Assert.That(form.Components, Is.Empty);
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
