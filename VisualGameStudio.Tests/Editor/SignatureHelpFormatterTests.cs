using NUnit.Framework;
using VisualGameStudio.Editor.Completion;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class SignatureHelpFormatterTests
{
    [Test]
    public void GetActiveParameterRange_FirstParameter_IsFound()
    {
        var range = SignatureHelpFormatter.GetActiveParameterRange(
            "Sub Print(text As String, count As Integer)",
            new[] { "text As String", "count As Integer" },
            activeParameter: 0);

        Assert.That(range, Is.Not.Null);
        Assert.That(range!.Value.Start, Is.EqualTo("Sub Print(".Length));
        Assert.That(range.Value.Length, Is.EqualTo("text As String".Length));
    }

    [Test]
    public void GetActiveParameterRange_SecondParameter_IsFound()
    {
        var sig = "Sub Print(text As String, count As Integer)";
        var range = SignatureHelpFormatter.GetActiveParameterRange(
            sig,
            new[] { "text As String", "count As Integer" },
            activeParameter: 1);

        Assert.That(range, Is.Not.Null);
        Assert.That(sig.Substring(range!.Value.Start, range.Value.Length), Is.EqualTo("count As Integer"));
    }

    [Test]
    public void GetActiveParameterRange_IdenticalParameterLabels_ResolveInOrder()
    {
        var sig = "Sub F(x As Integer, x As Integer)";
        var labels = new[] { "x As Integer", "x As Integer" };

        var first = SignatureHelpFormatter.GetActiveParameterRange(sig, labels, 0);
        var second = SignatureHelpFormatter.GetActiveParameterRange(sig, labels, 1);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Value.Start, Is.GreaterThan(first!.Value.Start));
    }

    [Test]
    public void GetActiveParameterRange_ActiveParameterOutOfRange_ReturnsNull()
    {
        var range = SignatureHelpFormatter.GetActiveParameterRange(
            "Sub F(x As Integer)", new[] { "x As Integer" }, activeParameter: 5);

        Assert.That(range, Is.Null);
    }

    [Test]
    public void GetActiveParameterRange_NegativeActiveParameter_ReturnsNull()
    {
        var range = SignatureHelpFormatter.GetActiveParameterRange(
            "Sub F(x As Integer)", new[] { "x As Integer" }, activeParameter: -1);

        Assert.That(range, Is.Null);
    }

    [Test]
    public void GetActiveParameterRange_LabelNotInSignature_ReturnsNull()
    {
        var range = SignatureHelpFormatter.GetActiveParameterRange(
            "Sub F(x As Integer)", new[] { "y As String" }, activeParameter: 0);

        Assert.That(range, Is.Null);
    }

    [Test]
    public void GetActiveParameterRange_EmptySignature_ReturnsNull()
    {
        var range = SignatureHelpFormatter.GetActiveParameterRange(
            "", new[] { "x" }, activeParameter: 0);

        Assert.That(range, Is.Null);
    }

    [Test]
    public void GetActiveParameterRange_ParameterNameMatchingMethodName_SearchesAfterParen()
    {
        // Parameter label "Print" also appears in the method name — the match
        // must be inside the parameter list, not the method name.
        var sig = "Sub Print(Print As String)";
        var range = SignatureHelpFormatter.GetActiveParameterRange(sig, new[] { "Print As String" }, 0);

        Assert.That(range, Is.Not.Null);
        Assert.That(range!.Value.Start, Is.GreaterThanOrEqualTo(sig.IndexOf('(')));
    }
}
