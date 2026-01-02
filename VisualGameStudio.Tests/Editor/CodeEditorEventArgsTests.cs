using NUnit.Framework;
using VisualGameStudio.Editor.Controls;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class CompletionRequestEventArgsTests
{
    [Test]
    public void Constructor_SetsAllProperties()
    {
        var args = new CompletionRequestEventArgs(10, 5, 100);

        Assert.That(args.Line, Is.EqualTo(10));
        Assert.That(args.Column, Is.EqualTo(5));
        Assert.That(args.Offset, Is.EqualTo(100));
    }

    [Test]
    public void Constructor_ZeroValues_AreValid()
    {
        var args = new CompletionRequestEventArgs(0, 0, 0);

        Assert.That(args.Line, Is.EqualTo(0));
        Assert.That(args.Column, Is.EqualTo(0));
        Assert.That(args.Offset, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_LargeValues_AreValid()
    {
        var args = new CompletionRequestEventArgs(10000, 500, 1000000);

        Assert.That(args.Line, Is.EqualTo(10000));
        Assert.That(args.Column, Is.EqualTo(500));
        Assert.That(args.Offset, Is.EqualTo(1000000));
    }

    [Test]
    public void InheritsFromEventArgs()
    {
        var args = new CompletionRequestEventArgs(1, 1, 0);

        Assert.That(args, Is.InstanceOf<EventArgs>());
    }
}

[TestFixture]
public class DataTipRequestEventArgsTests
{
    [Test]
    public void Constructor_SetsAllProperties()
    {
        var args = new DataTipRequestEventArgs("myVariable", 100.5, 200.5);

        Assert.That(args.Expression, Is.EqualTo("myVariable"));
        Assert.That(args.ScreenX, Is.EqualTo(100.5));
        Assert.That(args.ScreenY, Is.EqualTo(200.5));
    }

    [Test]
    public void Constructor_EmptyExpression_IsValid()
    {
        var args = new DataTipRequestEventArgs("", 0, 0);

        Assert.That(args.Expression, Is.EqualTo(""));
    }

    [Test]
    public void Constructor_ComplexExpression_IsValid()
    {
        var args = new DataTipRequestEventArgs("myObject.Property.SubProperty", 100, 200);

        Assert.That(args.Expression, Is.EqualTo("myObject.Property.SubProperty"));
    }

    [Test]
    public void Constructor_NegativeCoordinates_AreValid()
    {
        var args = new DataTipRequestEventArgs("test", -100, -200);

        Assert.That(args.ScreenX, Is.EqualTo(-100));
        Assert.That(args.ScreenY, Is.EqualTo(-200));
    }

    [Test]
    public void InheritsFromEventArgs()
    {
        var args = new DataTipRequestEventArgs("test", 0, 0);

        Assert.That(args, Is.InstanceOf<EventArgs>());
    }
}

[TestFixture]
public class SelectionInfoTests
{
    [Test]
    public void DefaultInfo_HasDefaultValues()
    {
        var info = new SelectionInfo();

        Assert.That(info.StartLine, Is.EqualTo(0));
        Assert.That(info.StartColumn, Is.EqualTo(0));
        Assert.That(info.EndLine, Is.EqualTo(0));
        Assert.That(info.EndColumn, Is.EqualTo(0));
        Assert.That(info.SelectedText, Is.EqualTo(""));
    }

    [Test]
    public void Info_CanSetAllProperties()
    {
        var info = new SelectionInfo
        {
            StartLine = 10,
            StartColumn = 5,
            EndLine = 15,
            EndColumn = 20,
            SelectedText = "Selected text here"
        };

        Assert.That(info.StartLine, Is.EqualTo(10));
        Assert.That(info.StartColumn, Is.EqualTo(5));
        Assert.That(info.EndLine, Is.EqualTo(15));
        Assert.That(info.EndColumn, Is.EqualTo(20));
        Assert.That(info.SelectedText, Is.EqualTo("Selected text here"));
    }

    [Test]
    public void IsMultiLine_SingleLine_ReturnsFalse()
    {
        var info = new SelectionInfo
        {
            StartLine = 10,
            EndLine = 10
        };

        Assert.That(info.IsMultiLine, Is.False);
    }

    [Test]
    public void IsMultiLine_MultipleLines_ReturnsTrue()
    {
        var info = new SelectionInfo
        {
            StartLine = 10,
            EndLine = 15
        };

        Assert.That(info.IsMultiLine, Is.True);
    }

    [Test]
    public void IsMultiLine_AdjacentLines_ReturnsTrue()
    {
        var info = new SelectionInfo
        {
            StartLine = 10,
            EndLine = 11
        };

        Assert.That(info.IsMultiLine, Is.True);
    }
}
