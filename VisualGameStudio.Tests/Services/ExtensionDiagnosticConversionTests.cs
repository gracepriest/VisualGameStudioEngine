using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Converts a VS Code diagnostic payload into the IDE's model.
///
/// <para>⛔ The severity scales differ by one and the mistake is silent.
/// <b>LSP</b> is Error=1, Warning=2, Information=3, Hint=4.
/// <b>VS Code</b> — which is what an extension sends — is Error=0, Warning=1, Information=2,
/// Hint=3. Reusing the existing LSP converter would render every extension ERROR as a warning and
/// every warning as information: no crash, no log, just quietly wrong severities in the Problems
/// panel.</para>
///
/// <para>Positions are 0-based on the wire and 1-based in the IDE, matching what the LSP path
/// already does.</para>
/// </summary>
[TestFixture]
public class ExtensionDiagnosticConversionTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Test]
    public void AFullDiagnosticConvertsEveryField()
    {
        var items = ExtensionService.ConvertExtensionDiagnostics(Json("""
            [{
              "range": { "start": { "line": 4, "character": 8 }, "end": { "line": 4, "character": 20 } },
              "message": "'unusedThing' is assigned a value but never used.",
              "severity": 1,
              "source": "eslint",
              "code": "no-unused-vars"
            }]
            """), @"C:\p\a.js").ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        var item = items[0];

        Assert.That(item.Message, Is.EqualTo("'unusedThing' is assigned a value but never used."));
        Assert.That(item.FilePath, Is.EqualTo(@"C:\p\a.js"));
        Assert.That(item.Line, Is.EqualTo(5), "wire is 0-based, the IDE is 1-based");
        Assert.That(item.Column, Is.EqualTo(9));
        Assert.That(item.Severity, Is.EqualTo(DiagnosticSeverity.Warning),
            "VS Code severity 1 is WARNING; under the LSP scale 1 would be Error");
    }

    /// <summary>
    /// The whole point of a separate converter. Getting this wrong mislabels every diagnostic an
    /// extension ever produces, and nothing fails visibly.
    /// </summary>
    [TestCase(0, DiagnosticSeverity.Error)]
    [TestCase(1, DiagnosticSeverity.Warning)]
    [TestCase(2, DiagnosticSeverity.Info)]
    // VS Code's Hint maps to Info rather than Hidden: Hidden is the IDE's "do not surface" level,
    // and silently dropping extension output is precisely this subsystem's recurring failure.
    [TestCase(3, DiagnosticSeverity.Info)]
    public void SeverityUsesTheVsCodeScaleNotTheLspOne(int wireSeverity, DiagnosticSeverity expected)
    {
        var items = ExtensionService.ConvertExtensionDiagnostics(Json($$"""
            [{ "message": "m", "severity": {{wireSeverity}} }]
            """), @"C:\p\a.js").ToList();

        Assert.That(items.Single().Severity, Is.EqualTo(expected));
    }

    /// <summary>VS Code treats a missing severity as Error, the most severe reading.</summary>
    [Test]
    public void AMissingSeverityDefaultsToError()
    {
        var items = ExtensionService.ConvertExtensionDiagnostics(
            Json("""[{ "message": "m" }]"""), @"C:\p\a.js").ToList();

        Assert.That(items.Single().Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    /// <summary>A diagnostic with no range still belongs in the list — it just has no position.</summary>
    [Test]
    public void AMissingRangeDoesNotDropTheDiagnostic()
    {
        var items = ExtensionService.ConvertExtensionDiagnostics(
            Json("""[{ "message": "whole-file problem" }]"""), @"C:\p\a.js").ToList();

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items.Single().Message, Is.EqualTo("whole-file problem"));
    }

    /// <summary>`code` is a string OR a number in VS Code, and either must survive.</summary>
    [TestCase("\"no-unused-vars\"", "no-unused-vars")]
    [TestCase("2304", "2304")]
    public void CodeAcceptsBothStringAndNumber(string rawCode, string expected)
    {
        var items = ExtensionService.ConvertExtensionDiagnostics(Json($$"""
            [{ "message": "m", "code": {{rawCode}} }]
            """), @"C:\p\a.js").ToList();

        Assert.That(items.Single().Id, Is.EqualTo(expected));
    }

    /// <summary>An empty publish is the "clean" signal and must convert to an empty list, not null.</summary>
    [Test]
    public void AnEmptyArrayConvertsToNoItems()
    {
        Assert.That(ExtensionService.ConvertExtensionDiagnostics(Json("[]"), @"C:\p\a.js"), Is.Empty);
    }

    /// <summary>Anything unexpected on this wire must degrade, never throw into a notification path.</summary>
    [TestCase("""{ "not": "an array" }""")]
    [TestCase("\"a string\"")]
    [TestCase("42")]
    [TestCase("null")]
    public void NonArrayPayloadsDegradeInsteadOfThrowing(string raw)
    {
        Assert.That(() => ExtensionService.ConvertExtensionDiagnostics(Json(raw), @"C:\p\a.js").ToList(),
            Throws.Nothing);
        Assert.That(ExtensionService.ConvertExtensionDiagnostics(Json(raw), @"C:\p\a.js"), Is.Empty);
    }

    /// <summary>One malformed entry must not discard the well-formed ones beside it.</summary>
    [Test]
    public void AMalformedEntryDoesNotPoisonItsNeighbours()
    {
        var items = ExtensionService.ConvertExtensionDiagnostics(Json("""
            [ "garbage", { "message": "real problem", "severity": 0 }, 42 ]
            """), @"C:\p\a.js").ToList();

        Assert.That(items.Select(i => i.Message), Is.EquivalentTo(new[] { "real problem" }));
    }
}
