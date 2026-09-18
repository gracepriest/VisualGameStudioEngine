using BasicLang.Forms;
using NUnit.Framework;
using VisualGameStudio.Shell.ViewModels.Designer;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 20: the selection algebra, behind a plain class so it is testable without a window.
///
/// <para>⚠ <b>Primary is the concept that makes the rest of the task work.</b> Align and
/// make-same-size need a REFERENCE control — "align left" means "align to which left?" — and VS's
/// answer is the one selected last, drawn with distinct handles. Without a primary, align has to
/// invent a rule (leftmost? first in document order?) and the result moves controls the user did not
/// expect to move.</para>
/// </summary>
[TestFixture]
public class FormSelectionTests
{
    private static FormControl Control(string id) => new() { Kind = "Button", Id = id };

    private static FormSelection Selection(out FormControl a, out FormControl b, out FormControl c)
    {
        a = Control("a");
        b = Control("b");
        c = Control("c");
        return new FormSelection();
    }

    [Test]
    public void AFreshSelectionIsEmpty()
    {
        var selection = new FormSelection();

        Assert.Multiple(() =>
        {
            Assert.That(selection.IsEmpty, Is.True);
            Assert.That(selection.Controls, Is.Empty);
            Assert.That(selection.Primary, Is.Null);
        });
    }

    [Test]
    public void SetReplacesWhateverWasSelected()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);

        selection.Set(b);

        Assert.Multiple(() =>
        {
            Assert.That(selection.Controls, Is.EqualTo(new[] { b }));
            Assert.That(selection.Primary, Is.SameAs(b));
        });
    }

    [Test]
    public void SetNullClearsIt()
    {
        var selection = Selection(out var a, out _, out _);
        selection.Set(a);

        selection.Set(null);

        Assert.That(selection.IsEmpty, Is.True);
    }

    // ==================================================================
    // Extending
    // ==================================================================

    [Test]
    public void ToggleAddsAControlThatIsNotSelected()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);

        selection.Toggle(b);

        Assert.That(selection.Controls, Is.EquivalentTo(new[] { a, b }));
    }

    [Test]
    public void ToggleRemovesAControlThatIsAlreadySelected()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);
        selection.Toggle(b);

        selection.Toggle(a);

        Assert.That(selection.Controls, Is.EqualTo(new[] { b }));
    }

    [Test]
    public void TheSameControlIsNeverSelectedTwice()
    {
        var selection = Selection(out var a, out _, out _);
        selection.Set(a);

        selection.Add(a);

        Assert.That(selection.Controls, Has.Count.EqualTo(1));
    }

    // ==================================================================
    // Primary
    // ==================================================================

    [Test]
    public void ThePrimaryIsTheOneSelectedLast()
    {
        var selection = Selection(out var a, out var b, out var c);
        selection.Set(a);
        selection.Toggle(b);
        selection.Toggle(c);

        Assert.That(selection.Primary, Is.SameAs(c));
    }

    /// <summary>
    /// ⛔ Ctrl-clicking the primary away must promote another, not leave a dangling reference.
    /// Align reads Primary; a stale one aligns everything to a control that is no longer selected.
    /// </summary>
    [Test]
    public void RemovingThePrimaryPromotesAnotherControl()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);
        selection.Toggle(b);

        selection.Toggle(b);

        Assert.Multiple(() =>
        {
            Assert.That(selection.Primary, Is.SameAs(a));
            Assert.That(selection.Controls, Is.EqualTo(new[] { a }));
        });
    }

    [Test]
    public void ClearingLeavesNoPrimary()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);
        selection.Toggle(b);

        selection.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(selection.Primary, Is.Null);
            Assert.That(selection.IsEmpty, Is.True);
        });
    }

    // ==================================================================
    // The marquee's result
    // ==================================================================

    [Test]
    public void SetRangeReplacesTheWholeSelection()
    {
        var selection = Selection(out var a, out var b, out var c);
        selection.Set(a);

        selection.SetRange(new[] { b, c });

        Assert.Multiple(() =>
        {
            Assert.That(selection.Controls, Is.EqualTo(new[] { b, c }));
            Assert.That(selection.Primary, Is.SameAs(c), "the last of the range is the anchor");
        });
    }

    [Test]
    public void AnEmptyRangeClears()
    {
        var selection = Selection(out var a, out _, out _);
        selection.Set(a);

        selection.SetRange(Array.Empty<FormControl>());

        Assert.That(selection.IsEmpty, Is.True);
    }

    // ==================================================================
    // Change notification
    // ==================================================================

    /// <summary>
    /// ⚠ The canvas repaints on this. Raising it when nothing actually changed turns every
    /// pointer-move over an already-selected control into a full redraw of the form.
    /// </summary>
    [Test]
    public void ReSelectingTheSameSingleControlRaisesNoChange()
    {
        var selection = Selection(out var a, out _, out _);
        selection.Set(a);

        var changes = 0;
        selection.Changed += (_, _) => changes++;
        selection.Set(a);

        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void ARealChangeRaisesExactlyOnce()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);

        var changes = 0;
        selection.Changed += (_, _) => changes++;
        selection.Toggle(b);

        Assert.That(changes, Is.EqualTo(1));
    }

    [Test]
    public void ClearingAnAlreadyEmptySelectionRaisesNoChange()
    {
        var selection = new FormSelection();

        var changes = 0;
        selection.Changed += (_, _) => changes++;
        selection.Clear();

        Assert.That(changes, Is.Zero);
    }

    [Test]
    public void ContainsReportsMembership()
    {
        var selection = Selection(out var a, out var b, out _);
        selection.Set(a);

        Assert.Multiple(() =>
        {
            Assert.That(selection.Contains(a), Is.True);
            Assert.That(selection.Contains(b), Is.False);
        });
    }

    /// <summary>
    /// ⛔ Identity, not equality. Two controls can carry the same id while a paste is mid-flight, and
    /// a selection that matched on id would light up the wrong box on the canvas.
    /// </summary>
    [Test]
    public void MembershipIsByIdentityNotByValue()
    {
        var selection = new FormSelection();
        var a = Control("same");
        var twin = Control("same");
        selection.Set(a);

        Assert.That(selection.Contains(twin), Is.False);
    }
}
