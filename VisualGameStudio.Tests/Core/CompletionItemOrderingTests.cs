using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

[TestFixture]
public class CompletionItemOrderingTests
{
    private static CompletionItem Item(string label, string? sortText = null) =>
        new() { Label = label, SortText = sortText };

    [Test]
    public void OrderByServerRank_UsesSortText()
    {
        var items = new[]
        {
            Item("Zebra", "00002_Zebra"),
            Item("Apple", "00003_Apple"),
            Item("Trim", "00001_Trim")
        };

        var ordered = CompletionItemOrdering.OrderByServerRank(items);

        Assert.That(ordered.Select(i => i.Label), Is.EqualTo(new[] { "Trim", "Zebra", "Apple" }));
    }

    [Test]
    public void OrderByServerRank_FallsBackToLabelWhenSortTextMissing()
    {
        var items = new[]
        {
            Item("beta"),
            Item("alpha")
        };

        var ordered = CompletionItemOrdering.OrderByServerRank(items);

        Assert.That(ordered.Select(i => i.Label), Is.EqualTo(new[] { "alpha", "beta" }));
    }

    [Test]
    public void OrderByServerRank_IsStableForEqualKeys()
    {
        var items = new[]
        {
            Item("first", "same"),
            Item("second", "same"),
            Item("third", "same")
        };

        var ordered = CompletionItemOrdering.OrderByServerRank(items);

        Assert.That(ordered.Select(i => i.Label), Is.EqualTo(new[] { "first", "second", "third" }));
    }

    [Test]
    public void OrderByServerRank_OrdinalComparison_DigitsBeforeLetters()
    {
        var items = new[]
        {
            Item("b", "b"),
            Item("digit", "1"),
            Item("upper", "B")
        };

        var ordered = CompletionItemOrdering.OrderByServerRank(items);

        // Ordinal: '1' (0x31) < 'B' (0x42) < 'b' (0x62)
        Assert.That(ordered.Select(i => i.Label), Is.EqualTo(new[] { "digit", "upper", "b" }));
    }

    [Test]
    public void OrderByServerRank_EmptyInput_ReturnsEmpty()
    {
        var ordered = CompletionItemOrdering.OrderByServerRank(Array.Empty<CompletionItem>());
        Assert.That(ordered, Is.Empty);
    }
}
