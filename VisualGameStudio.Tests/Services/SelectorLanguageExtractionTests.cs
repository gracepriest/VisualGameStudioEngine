using System.Linq;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Extracts the language ids from a VS Code document selector, which is what decides whether the
/// IDE will ever ASK an extension for hover or completion.
///
/// <para>The original inline version tested <c>RootElement.TryGetProperty("language", ...)</c>
/// FIRST and only then checked for an array. <c>JsonElement.TryGetProperty</c> does not return
/// false for a non-object — it THROWS <c>InvalidOperationException</c> — so on an array selector it
/// threw immediately, landed in a bare <c>catch { }</c>, and the array-handling branch sitting
/// right below it could never execute.</para>
///
/// <para>Arrays are the normal case: <c>vscode-api/languages.js</c> runs every selector through
/// <c>_normaliseSelector</c>, which wraps objects and bare strings alike into an array. So this
/// silently discarded essentially every real selector, leaving
/// <c>HasExtensionProviders</c> permanently false — observed as
/// "Provider registered: hover" immediately followed by "extension providers: False".</para>
/// </summary>
[TestFixture]
public class SelectorLanguageExtractionTests
{
    /// <summary>The shape languages.js actually sends — always an array after normalisation.</summary>
    [Test]
    public void AnArraySelectorYieldsItsLanguages()
    {
        var languages = ExtensionService.ExtractSelectorLanguages(
            """[{ "scheme": "file", "language": "javascript" }]""");

        Assert.That(languages, Is.EquivalentTo(new[] { "javascript" }),
            "this is the exact selector the hover probe registers, and the shape every VS Code "
            + "extension produces once normalised");
    }

    [Test]
    public void AMultiEntrySelectorYieldsEveryLanguage()
    {
        var languages = ExtensionService.ExtractSelectorLanguages(
            """[{ "language": "javascript" }, { "language": "typescript" }]""");

        Assert.That(languages, Is.EquivalentTo(new[] { "javascript", "typescript" }));
    }

    /// <summary>A single object is legal too, and used to be the only shape that worked.</summary>
    [Test]
    public void AnObjectSelectorYieldsItsLanguage()
    {
        Assert.That(
            ExtensionService.ExtractSelectorLanguages("""{ "scheme": "file", "language": "python" }"""),
            Is.EquivalentTo(new[] { "python" }));
    }

    /// <summary>
    /// VS Code allows a bare language string inside a selector array. languages.js normalises those
    /// away, but an extension can call the RPC directly, and a string element would throw from
    /// <c>TryGetProperty</c> exactly as the array itself did.
    /// </summary>
    [Test]
    public void BareStringEntriesAreTolerated()
    {
        Assert.That(
            ExtensionService.ExtractSelectorLanguages("""["javascript", { "language": "css" }]"""),
            Is.EquivalentTo(new[] { "javascript", "css" }));
    }

    /// <summary>
    /// A selector with no language — scheme- or pattern-only — is legal and simply contributes no
    /// language. It must not throw, and must not poison the entries beside it.
    /// </summary>
    [Test]
    public void SelectorsWithoutALanguageAreSkipped()
    {
        Assert.That(
            ExtensionService.ExtractSelectorLanguages("""[{ "scheme": "file" }, { "language": "go" }]"""),
            Is.EquivalentTo(new[] { "go" }));
    }

    /// <summary>
    /// Pins the mechanism, because the fix looks like a stylistic reordering and is not.
    ///
    /// <para><c>TryGetProperty</c> reads as a safe probe — the <c>Try</c> prefix promises a bool.
    /// On a non-object it THROWS instead, so testing it before checking <c>ValueKind</c> is not
    /// defensive, it is the bug. This test fails the day someone "tidies" the guard away.</para>
    /// </summary>
    [Test]
    public void TryGetProperty_ThrowsOnAnArray_WhichIsWhyTheValueKindCheckComesFirst()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("""[{ "language": "javascript" }]""");

        Assert.That(() => doc.RootElement.TryGetProperty("language", out _),
            Throws.InstanceOf<System.InvalidOperationException>(),
            "a Try* method that throws is why the array branch below it was unreachable, and why "
            + "the bare catch made it invisible");
    }

    [TestCase(null, TestName = "null selector")]
    [TestCase("", TestName = "empty selector")]
    [TestCase("null", TestName = "JSON null selector")]
    [TestCase("not json at all", TestName = "malformed selector")]
    [TestCase("42", TestName = "numeric selector")]
    public void DegenerateSelectorsYieldNothingAndDoNotThrow(string? selectorJson)
    {
        Assert.That(() => ExtensionService.ExtractSelectorLanguages(selectorJson).ToList(),
            Throws.Nothing);
        Assert.That(ExtensionService.ExtractSelectorLanguages(selectorJson), Is.Empty);
    }
}
