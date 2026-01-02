using NUnit.Framework;
using VisualGameStudio.Editor.MultiCursor;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class CursorPositionTests
{
    [Test]
    public void Constructor_WithOffset_SetsOffset()
    {
        var cursor = new CursorPosition(100);

        Assert.That(cursor.Offset, Is.EqualTo(100));
    }

    [Test]
    public void Constructor_WithOffset_SelectionIsNull()
    {
        var cursor = new CursorPosition(100);

        Assert.That(cursor.SelectionStart, Is.Null);
        Assert.That(cursor.SelectionEnd, Is.Null);
    }

    [Test]
    public void Constructor_WithSelection_SetsAllProperties()
    {
        var cursor = new CursorPosition(150, 100, 150);

        Assert.That(cursor.Offset, Is.EqualTo(150));
        Assert.That(cursor.SelectionStart, Is.EqualTo(100));
        Assert.That(cursor.SelectionEnd, Is.EqualTo(150));
    }

    [Test]
    public void HasSelection_WithNoSelection_ReturnsFalse()
    {
        var cursor = new CursorPosition(100);

        Assert.That(cursor.HasSelection, Is.False);
    }

    [Test]
    public void HasSelection_WithSelection_ReturnsTrue()
    {
        var cursor = new CursorPosition(150, 100, 150);

        Assert.That(cursor.HasSelection, Is.True);
    }

    [Test]
    public void HasSelection_WithSameStartAndEnd_ReturnsFalse()
    {
        var cursor = new CursorPosition(100, 100, 100);

        Assert.That(cursor.HasSelection, Is.False);
    }

    [Test]
    public void HasSelection_WithOnlyStart_ReturnsFalse()
    {
        var cursor = new CursorPosition(100)
        {
            SelectionStart = 50
        };

        Assert.That(cursor.HasSelection, Is.False);
    }

    [Test]
    public void HasSelection_WithOnlyEnd_ReturnsFalse()
    {
        var cursor = new CursorPosition(100)
        {
            SelectionEnd = 150
        };

        Assert.That(cursor.HasSelection, Is.False);
    }

    [Test]
    public void SelectionLength_WithNoSelection_ReturnsZero()
    {
        var cursor = new CursorPosition(100);

        Assert.That(cursor.SelectionLength, Is.EqualTo(0));
    }

    [Test]
    public void SelectionLength_WithSelection_ReturnsCorrectLength()
    {
        var cursor = new CursorPosition(150, 100, 150);

        Assert.That(cursor.SelectionLength, Is.EqualTo(50));
    }

    [Test]
    public void SelectionLength_WithReverseSelection_ReturnsAbsoluteLength()
    {
        var cursor = new CursorPosition(100, 150, 100);

        Assert.That(cursor.SelectionLength, Is.EqualTo(50));
    }

    [Test]
    public void SelectionLength_WithSameStartAndEnd_ReturnsZero()
    {
        var cursor = new CursorPosition(100, 100, 100);

        Assert.That(cursor.SelectionLength, Is.EqualTo(0));
    }

    [Test]
    public void Offset_CanBeModified()
    {
        var cursor = new CursorPosition(100);
        cursor.Offset = 200;

        Assert.That(cursor.Offset, Is.EqualTo(200));
    }

    [Test]
    public void SelectionStart_CanBeModified()
    {
        var cursor = new CursorPosition(100);
        cursor.SelectionStart = 50;

        Assert.That(cursor.SelectionStart, Is.EqualTo(50));
    }

    [Test]
    public void SelectionEnd_CanBeModified()
    {
        var cursor = new CursorPosition(100);
        cursor.SelectionEnd = 150;

        Assert.That(cursor.SelectionEnd, Is.EqualTo(150));
    }

    [Test]
    public void Clone_CreatesIndependentCopy()
    {
        var original = new CursorPosition(100, 50, 100);
        var clone = original.Clone();

        original.Offset = 200;
        original.SelectionStart = 150;
        original.SelectionEnd = 200;

        Assert.That(clone.Offset, Is.EqualTo(100));
        Assert.That(clone.SelectionStart, Is.EqualTo(50));
        Assert.That(clone.SelectionEnd, Is.EqualTo(100));
    }

    [Test]
    public void Clone_CopiesAllProperties()
    {
        var original = new CursorPosition(100, 50, 100);
        var clone = original.Clone();

        Assert.That(clone.Offset, Is.EqualTo(original.Offset));
        Assert.That(clone.SelectionStart, Is.EqualTo(original.SelectionStart));
        Assert.That(clone.SelectionEnd, Is.EqualTo(original.SelectionEnd));
    }

    [Test]
    public void Clone_PreservesNullSelection()
    {
        var original = new CursorPosition(100);
        var clone = original.Clone();

        Assert.That(clone.SelectionStart, Is.Null);
        Assert.That(clone.SelectionEnd, Is.Null);
    }

    [Test]
    public void Clone_CloningClone_WorksCorrectly()
    {
        var original = new CursorPosition(100, 50, 100);
        var clone1 = original.Clone();
        var clone2 = clone1.Clone();

        Assert.That(clone2.Offset, Is.EqualTo(100));
        Assert.That(clone2.SelectionStart, Is.EqualTo(50));
        Assert.That(clone2.SelectionEnd, Is.EqualTo(100));
    }

    [Test]
    public void ZeroOffset_IsValid()
    {
        var cursor = new CursorPosition(0);

        Assert.That(cursor.Offset, Is.EqualTo(0));
    }

    [Test]
    public void LargeOffset_IsValid()
    {
        var cursor = new CursorPosition(int.MaxValue);

        Assert.That(cursor.Offset, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void SelectionWithZeroStart_IsValid()
    {
        var cursor = new CursorPosition(50, 0, 50);

        Assert.That(cursor.HasSelection, Is.True);
        Assert.That(cursor.SelectionLength, Is.EqualTo(50));
    }

    [Test]
    public void MultipleModifications_WorkCorrectly()
    {
        var cursor = new CursorPosition(100);

        cursor.Offset = 200;
        cursor.SelectionStart = 150;
        cursor.SelectionEnd = 200;

        Assert.That(cursor.Offset, Is.EqualTo(200));
        Assert.That(cursor.SelectionStart, Is.EqualTo(150));
        Assert.That(cursor.SelectionEnd, Is.EqualTo(200));
        Assert.That(cursor.HasSelection, Is.True);
        Assert.That(cursor.SelectionLength, Is.EqualTo(50));
    }

    [Test]
    public void ClearingSelection_BySettingToNull()
    {
        var cursor = new CursorPosition(100, 50, 100);

        cursor.SelectionStart = null;
        cursor.SelectionEnd = null;

        Assert.That(cursor.HasSelection, Is.False);
        Assert.That(cursor.SelectionLength, Is.EqualTo(0));
    }
}
