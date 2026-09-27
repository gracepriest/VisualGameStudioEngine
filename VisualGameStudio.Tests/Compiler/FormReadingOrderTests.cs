using BasicLang.Forms;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Spec 2026-09-27 §5 — the order controls stack in on a phone: ROWS by vertical overlap, top to bottom, left to
/// right within a row. Pure and per sibling list; the emitter recurses into containers (Task 10).
/// </summary>
[TestFixture]
public class FormReadingOrderTests
{
    private sealed record Box(string Id, int X, int Y, int W, int H);

    private static IReadOnlyList<string> Order(params Box[] boxes) =>
        FormReadingOrder.Order(boxes, b => new FormRect(b.X, b.Y, b.W, b.H)).Select(b => b.Id).ToList();

    [Test]
    public void Nothing_OrdersToNothing() => Assert.That(Order(), Is.Empty);

    [Test]
    public void TwoRows_TopToBottom_LeftToRight()
    {
        Assert.That(Order(
                new Box("button", 10, 50, 75, 23),
                new Box("text", 70, 10, 100, 20),
                new Box("label", 10, 10, 50, 20)),
            Is.EqualTo(new[] { "label", "text", "button" }));
    }

    [Test]
    public void ALabelSlightlyHigherThanItsBox_ShareTheBoxsRow_AndComeFirst()
    {
        Assert.That(Order(
                new Box("text", 70, 10, 100, 23),
                new Box("label", 10, 13, 50, 15)),
            Is.EqualTo(new[] { "label", "text" }));
    }

    [Test]
    public void ControlsThatOnlyTouch_AreDifferentRows()
    {
        Assert.That(Order(
                new Box("second", 5, 30, 50, 20),
                new Box("first", 100, 10, 50, 20)),
            Is.EqualTo(new[] { "first", "second" }), "second's top (30) is first's bottom: no overlap");
    }

    [Test]
    public void ATallListBesideAColumnOfFields_IsOneRow_ListThenFieldsTopToBottom()
    {
        Assert.That(Order(
                new Box("f3", 150, 70, 100, 20),
                new Box("list", 10, 10, 100, 200),
                new Box("f1", 150, 10, 100, 20),
                new Box("f2", 150, 40, 100, 20)),
            Is.EqualTo(new[] { "list", "f1", "f2", "f3" }));
    }

    [Test]
    public void IdenticalRects_KeepDocumentOrder()
    {
        Assert.That(Order(
                new Box("a", 10, 10, 50, 20),
                new Box("b", 10, 10, 50, 20),
                new Box("c", 10, 10, 50, 20)),
            Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void AZeroHeightControl_StillJoinsARow()
    {
        Assert.That(Order(
                new Box("right", 100, 10, 50, 0),
                new Box("left", 10, 10, 50, 0)),
            Is.EqualTo(new[] { "left", "right" }));
    }

    /// <summary>
    /// Added on the Task 8 mutation pass: every planned case gives the same answer if the row's bottom is the LAST
    /// control's bottom rather than the union, because each follower that joins sorts after it anyway. Here the
    /// short field joins the tall list's row and would end it at 32; the note (top 50) is still beside the list.
    /// </summary>
    [Test]
    public void TheRowsBottomIsTheUnionOfTheRow_NotTheLastControlsBottom()
    {
        Assert.That(Order(
                new Box("list", 100, 10, 100, 200),
                new Box("field", 250, 12, 100, 20),
                new Box("note", 10, 50, 80, 20)),
            Is.EqualTo(new[] { "note", "list", "field" }),
            "the note's top (50) is above the row's union bottom (210), so it is in the row, and leftmost");
    }

    /// <summary>
    /// Added on the Task 8 mutation pass: <see cref="AZeroHeightControl_StillJoinsARow"/> gives the same answer with
    /// or without the 1px rule (two one-control rows come out left, right too). Here a zero-height control OPENS
    /// the row: with 1px, the field at the same top joins it, and so do the controls overlapping the field. No member
    /// spans the row (each misses some top), so the row is plain left to right and the low control comes first.
    /// Without 1px the rule would be a row of its own, first. (Fixture rewritten for the spanning rule, owner
    /// decision 2026-09-27: the previous one had a spanning field, under which both variants agree.)
    /// </summary>
    [Test]
    public void AZeroHeightControl_OpensARowThatOthersJoin()
    {
        Assert.That(Order(
                new Box("rule", 100, 10, 50, 0),
                new Box("field", 200, 10, 50, 20),
                new Box("deep", 300, 25, 50, 30),
                new Box("low", 10, 40, 50, 20)),
            Is.EqualTo(new[] { "low", "rule", "field", "deep" }),
            "the rule counts as 10..11, so the field (top 10) joins it; deep (25 < 30) and low (40 < 55) join too");
    }

    /// <summary>
    /// Owner decision 2026-09-27: a tall sibling must not turn the controls beside it into columns. The logo's
    /// span holds every other top, so it is placed by X (first, it is leftmost) and the rest form their OWN rows.
    /// Before the rule: logo, userLabel, passLabel, userBox, passBox, ok.
    /// </summary>
    [Test]
    public void ATallLogoBesideLabelBoxPairs_KeepsEachPairTogether()
    {
        Assert.That(Order(
                new Box("logo", 10, 10, 100, 200),
                new Box("userLabel", 120, 22, 60, 18),
                new Box("userBox", 190, 20, 150, 23),
                new Box("passLabel", 120, 62, 60, 18),
                new Box("passBox", 190, 60, 150, 23),
                new Box("ok", 190, 100, 75, 23)),
            Is.EqualTo(new[] { "logo", "userLabel", "userBox", "passLabel", "passBox", "ok" }));
    }

    [Test]
    public void ATallListOnTheRight_ComesAfterThePairsBesideIt_ByX()
    {
        Assert.That(Order(
                new Box("list", 400, 10, 100, 200),
                new Box("userLabel", 10, 22, 60, 18),
                new Box("userBox", 80, 20, 150, 23),
                new Box("passLabel", 10, 62, 60, 18),
                new Box("passBox", 80, 60, 150, 23)),
            Is.EqualTo(new[] { "userLabel", "userBox", "passLabel", "passBox", "list" }));
    }

    [Test]
    public void AFullHeightLeftBandAtXZero_ComesFirst_ThenThePairsRowByRow()
    {
        Assert.That(Order(
                new Box("userBox", 180, 20, 150, 23),
                new Box("band", 0, 0, 100, 300),
                new Box("userLabel", 110, 22, 60, 18),
                new Box("passBox", 180, 60, 150, 23),
                new Box("passLabel", 110, 62, 60, 18)),
            Is.EqualTo(new[] { "band", "userLabel", "userBox", "passLabel", "passBox" }));
    }

    [Test]
    public void TwoTallMembersSideBySide_BothPlacedByX_BeforeThePairsToTheirRight()
    {
        Assert.That(Order(
                new Box("userLabel", 240, 22, 60, 18),
                new Box("userBox", 310, 20, 150, 23),
                new Box("tallB", 120, 10, 100, 200),
                new Box("passLabel", 240, 62, 60, 18),
                new Box("passBox", 310, 60, 150, 23),
                new Box("tallA", 10, 10, 100, 200)),
            Is.EqualTo(new[] { "tallA", "tallB", "userLabel", "userBox", "passLabel", "passBox" }));
    }

    [Test]
    public void TallMembersOnBothSides_SplitThePairsBetweenThemByX()
    {
        Assert.That(Order(
                new Box("right", 400, 10, 100, 200),
                new Box("box", 180, 20, 150, 23),
                new Box("left", 10, 10, 100, 200),
                new Box("label", 120, 22, 50, 18)),
            Is.EqualTo(new[] { "left", "label", "box", "right" }));
    }

    /// <summary>
    /// A row with NO spanning member (a's span misses low's top; nothing else holds a's) is plain X, then top, then
    /// document order — so of two controls at the same X, the higher comes first even when written second.
    /// </summary>
    [Test]
    public void ARowWithNoSpanningMember_AtEqualX_TheHigherComesFirst()
    {
        Assert.That(Order(
                new Box("low", 10, 40, 50, 20),
                new Box("mid", 100, 25, 50, 20),
                new Box("high", 10, 10, 50, 20)),
            Is.EqualTo(new[] { "high", "low", "mid" }));
    }

    [Test]
    public void AMemberAtTheSpanningMembersOwnX_ComesAfterIt()
    {
        Assert.That(Order(
                new Box("caption", 10, 120, 100, 20),
                new Box("picture", 10, 10, 200, 150)),
            Is.EqualTo(new[] { "picture", "caption" }),
            "the caption's top is inside the picture's span, at the same X: the tie goes AFTER the spanning member");
    }

    [Test]
    public void NegativeTops_OrderAboveZero() =>
        Assert.That(Order(new Box("below", 10, 0, 50, 20), new Box("above", 10, -20, 50, 20)),
            Is.EqualTo(new[] { "above", "below" }));

    [Test]
    public void AContainerStacksAsOneBlock_ItsChildrenOrderedTheSameWay()
    {
        var top = Order(
            new Box("panel", 10, 60, 300, 200),
            new Box("title", 10, 10, 200, 30));
        var inside = Order(
            new Box("ok", 200, 150, 75, 23),
            new Box("name", 10, 10, 150, 23),
            new Box("nameLabel", 170, 12, 60, 18));

        Assert.Multiple(() =>
        {
            Assert.That(top, Is.EqualTo(new[] { "title", "panel" }), "the panel is ordered by its own rectangle");
            Assert.That(inside, Is.EqualTo(new[] { "name", "nameLabel", "ok" }),
                "its children by theirs, relative to it — the same rule, one level down");
        });
    }
}
