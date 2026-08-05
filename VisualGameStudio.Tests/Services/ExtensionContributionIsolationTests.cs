using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// The residual net behind the shape fixes in <see cref="ExtensionManifestShapeTests"/>.
///
/// <para>Retyping the DTO to VS Code's real shapes fixes the shapes we know about. It cannot fix
/// the ones we do not: the DTO models a fraction of VS Code's schema, and every unmodelled corner
/// is another way for one cosmetic field to abort the whole manifest bind — which, because the
/// caller catches per-extension, deletes that extension's grammars, themes, commands and
/// activation along with it.</para>
///
/// <para>These tests pin the containment property: a section that cannot bind costs THAT SECTION,
/// and the extension plus its other sections survive.</para>
/// </summary>
[TestFixture]
public class ExtensionContributionIsolationTests
{
    private static ExtensionManifest? Bind(string json) =>
        JsonSerializer.Deserialize<ExtensionManifest>(json, ExtensionService.ManifestJsonOptions);

    /// <summary>
    /// A manifest whose <c>commands</c> is a string — not a shape VS Code allows, which is exactly
    /// the point: this stands in for the unmodelled corner we have not met yet. Themes and grammars
    /// on either side of it are well-formed and must still arrive.
    /// </summary>
    private const string CommandsIsGarbage = """
        {
          "name": "sample",
          "version": "1.0.0",
          "publisher": "acme",
          "contributes": {
            "themes": [ { "label": "Sample Dark", "uiTheme": "vs-dark", "path": "./t.json" } ],
            "commands": "this is not an array",
            "grammars": [ { "language": "sample", "scopeName": "source.sample", "path": "./s.json" } ]
          }
        }
        """;

    [Test]
    public void AnUnbindableSection_DoesNotTakeTheExtensionDown()
    {
        ExtensionManifest? manifest = null;

        Assert.DoesNotThrow(() => manifest = Bind(CommandsIsGarbage),
            "one unbindable section must not abort the manifest bind — the caller catches at "
            + "whole-extension scope, so throwing here deletes the entire extension");

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("sample"), "identity must survive");
    }

    [Test]
    public void AnUnbindableSection_CostsOnlyItself()
    {
        var manifest = Bind(CommandsIsGarbage);
        var contributes = manifest!.Contributes!;

        Assert.That(contributes.Commands, Is.Empty, "the broken section degrades to its default");

        // Values, never counts: a converter that loses the naming policy yields the right count
        // with every field blank, which would pass a count-only assertion.
        Assert.That(contributes.Themes, Has.Count.EqualTo(1));
        Assert.That(contributes.Themes[0].Label, Is.EqualTo("Sample Dark"));
        Assert.That(contributes.Grammars, Has.Count.EqualTo(1));
        Assert.That(contributes.Grammars[0].ScopeName, Is.EqualTo("source.sample"));
    }

    [Test]
    public void AnUnbindableSection_IsReported()
    {
        var contributes = Bind(CommandsIsGarbage)!.Contributes!;

        Assert.That(contributes.LoadErrors, Has.Count.EqualTo(1),
            "a silently dropped section is worse than a loud failure — it must be reported");
        Assert.That(contributes.LoadErrors[0].Section, Is.EqualTo("commands"));
        Assert.That(contributes.LoadErrors[0].Message, Is.Not.Empty);
    }

    /// <summary>
    /// Real and hand-edited manifests carry all of these. Without a token-type guard as the first
    /// statement of the converter, the converter itself becomes a NEW whole-extension kill path —
    /// the opposite of its purpose.
    /// </summary>
    [TestCase("null", TestName = "contributes is null")]
    [TestCase("[]", TestName = "contributes is an array")]
    [TestCase("\"nonsense\"", TestName = "contributes is a string")]
    [TestCase("5", TestName = "contributes is a number")]
    public void ADegenerateContributesValue_LeavesTheExtensionIntact(string contributesLiteral)
    {
        var json = $$"""
            { "name": "sample", "version": "1.0.0", "publisher": "acme",
              "contributes": {{contributesLiteral}} }
            """;

        ExtensionManifest? manifest = null;
        Assert.DoesNotThrow(() => manifest = Bind(json));
        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("sample"));
    }

    /// <summary>
    /// A custom converter owns serialization as well as deserialization, and the natural Write()
    /// implementation — handing the value straight back to JsonSerializer.Serialize — recurses into
    /// a StackOverflowException. That is uncatchable in .NET, so it terminates the IDE rather than
    /// failing one extension. This round-trip is the only thing that catches it before a caller does.
    /// </summary>
    [Test]
    public void AManifestSurvivesARoundTrip()
    {
        var original = Bind("""
            {
              "name": "sample", "version": "1.0.0", "publisher": "acme",
              "repository": "https://github.com/acme/sample",
              "contributes": {
                "commands": [ { "command": "sample.run", "title": "Run" } ],
                "themes": [ { "label": "Sample Dark", "uiTheme": "vs-dark", "path": "./t.json" } ]
              }
            }
            """);

        string json = null!;
        Assert.DoesNotThrow(
            () => json = JsonSerializer.Serialize(original, ExtensionService.ManifestJsonOptions),
            "serializing must not recurse through the contributions converter");

        var round = JsonSerializer.Deserialize<ExtensionManifest>(json, ExtensionService.ManifestJsonOptions);

        Assert.That(round!.Name, Is.EqualTo("sample"));
        Assert.That(round.Contributes!.Commands[0].Command, Is.EqualTo("sample.run"));
        Assert.That(round.Contributes.Themes[0].Label, Is.EqualTo("Sample Dark"));
        Assert.That(json, Does.Not.Contain("loadErrors"),
            "LoadErrors is diagnostic state, not part of the manifest format");
    }

    /// <summary>
    /// The converter matches section names by hand, so a section added to
    /// <see cref="ExtensionContributions"/> without a matching arm binds to nothing — silently, with
    /// no exception and no LoadError. This fails the moment a 14th section is added unprotected.
    ///
    /// <para>Guards a specific near-miss: the property is <c>ViewsContainers</c> while its element
    /// type is <c>ViewContainerContribution</c>, so a hand-written <c>"viewContainers"</c> arm is a
    /// silent, otherwise-test-passing no-op.</para>
    /// </summary>
    [Test]
    public void EverySectionOnTheDtoHasAConverterArm()
    {
        var declared = typeof(ExtensionContributions)
            .GetProperties()
            .Where(p => p.Name != nameof(ExtensionContributions.LoadErrors))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        var handled = ExtensionContributionsConverter.KnownSections
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.That(handled, Is.EqualTo(declared),
            "every contribution section must have a converter arm; an unmatched section binds to "
            + "nothing with no error at all");
    }
}
