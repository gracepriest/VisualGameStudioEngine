using System.Collections.Generic;
using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// Binds real VS Code <c>package.json</c> shapes through the production options and asserts the
/// extension survives.
///
/// <para>Why this fixture exists: <c>ExtensionManifest</c> holds identity and <c>contributes</c> in
/// one strongly-typed graph, <c>JsonSerializer.Deserialize&lt;ExtensionManifest&gt;</c> is eager and
/// all-or-nothing, and the caller's catch is at WHOLE-EXTENSION scope
/// (<c>ExtensionService.DiscoverExtensionsAsync</c>). So one shape mismatch anywhere in the manifest
/// does not degrade a feature — it makes the entire extension vanish, including its identity,
/// grammars, themes and activation events. Two such kills shipped to users before being found
/// individually (<c>fbcdce5</c> a JSON-Schema type array, <c>3449378</c> grammar embeddedLanguages).</para>
///
/// <para>Every case below is a shape VS Code itself documents and real extensions publish. The
/// assertion is deliberately weak — "the extension still binds and keeps its identity" — because
/// that is the property that was actually broken. Per-section value assertions live in
/// <see cref="ManifestShapes_PreserveTheirValues"/>; a case that binds with every field empty is
/// not a pass, so counts alone are never asserted.</para>
/// </summary>
[TestFixture]
public class ExtensionManifestShapeTests
{
    /// <summary>
    /// Wraps a <c>contributes</c> body in an otherwise minimal, valid manifest. Identity is
    /// constant across every case so that a failure isolates the shape under test.
    /// </summary>
    private static string ManifestWith(string contributesBody) => $$"""
        {
          "name": "sample",
          "displayName": "Sample",
          "version": "1.0.0",
          "publisher": "acme",
          "engines": { "vscode": "^1.75.0" },
          "contributes": {{contributesBody}}
        }
        """;

    /// <summary>
    /// Shapes that are legal in VS Code and appear in published extensions. The name is the test
    /// case label, so a red run names the offending shape directly.
    /// </summary>
    public static IEnumerable<TestCaseData> RealShapes()
    {
        // --- object maps: these have NO array form in VS Code's schema, so the List<T> the DTO
        //     declares could never have bound a real manifest at all.
        yield return new TestCaseData(ManifestWith("""
            { "menus": { "editor/context": [ { "command": "sample.run", "group": "navigation" } ] } }
            """)).SetName("menus is an object map keyed by menu id");

        yield return new TestCaseData(ManifestWith("""
            { "views": { "explorer": [ { "id": "sample.view", "name": "Sample" } ] } }
            """)).SetName("views is an object map keyed by container id");

        yield return new TestCaseData(ManifestWith("""
            { "viewsContainers": { "activitybar": [ { "id": "sample", "title": "Sample", "icon": "i.svg" } ] } }
            """)).SetName("viewsContainers is an object map keyed by location");

        // --- configuration: a single object OR an array of sections are both legal.
        yield return new TestCaseData(ManifestWith("""
            { "configuration": [ { "title": "Sample", "properties": { "sample.a": { "type": "string" } } } ] }
            """)).SetName("configuration is an array of sections");

        // --- oneOf leaves: a scalar where the DTO expects an object, or the reverse.
        yield return new TestCaseData(ManifestWith("""
            { "commands": [ { "command": "sample.run", "title": "Run", "icon": { "light": "l.svg", "dark": "d.svg" } } ] }
            """)).SetName("commands[].icon is a light/dark object");

        yield return new TestCaseData(ManifestWith("""
            { "languages": [ { "id": "sample", "firstLine": "^#!/.*\\\\bsample\\\\b" } ] }
            """)).SetName("languages[].firstLine is a string");

        yield return new TestCaseData(ManifestWith("""
            { "languages": [ { "id": "sample", "icon": { "light": "l.svg", "dark": "d.svg" } } ] }
            """)).SetName("languages[].icon is a light/dark object");

        yield return new TestCaseData(ManifestWith("""
            { "debuggers": [ { "type": "sample", "configurationAttributes": { "launch": { "required": [ "program" ] } } } ] }
            """)).SetName("debuggers[].configurationAttributes is an object");

        yield return new TestCaseData(ManifestWith("""
            { "problemMatchers": [ { "name": "sample", "fileLocation": [ "relative", "${workspaceFolder}" ] } ] }
            """)).SetName("problemMatchers[].fileLocation is an array");

        yield return new TestCaseData(ManifestWith("""
            { "problemMatchers": [ { "name": "sample", "pattern": [ { "regexp": "^(.*)$", "file": 1 } ] } ] }
            """)).SetName("problemMatchers[].pattern is an array of patterns");

        yield return new TestCaseData(ManifestWith("""
            { "problemMatchers": [ { "name": "sample", "pattern": "$samplePattern" } ] }
            """)).SetName("problemMatchers[].pattern is a named reference string");

        // --- an ESLint-shaped manifest: the combination real popular extensions actually publish.
        yield return new TestCaseData(ManifestWith("""
            {
              "commands": [ { "command": "sample.fix", "title": "Fix", "icon": { "light": "l.svg", "dark": "d.svg" } } ],
              "menus": { "commandPalette": [ { "command": "sample.fix", "when": "editorIsOpen" } ] },
              "configuration": [ { "title": "Sample", "properties": { "sample.enable": { "type": "boolean" } } } ]
            }
            """)).SetName("an ESLint-shaped manifest (commands + menus + configuration)");

        // --- identity level, OUTSIDE contributes: npm shorthand. No contribution-level fix can
        //     reach this one, which is why it belongs in the same workstream.
        yield return new TestCaseData("""
            {
              "name": "sample",
              "version": "1.0.0",
              "publisher": "acme",
              "repository": "https://github.com/acme/sample"
            }
            """).SetName("repository is npm shorthand string");

        // --- shapes that already bind today. Present so a regression in the fix is caught, and so
        //     the fixture is not exclusively made of failures.
        yield return new TestCaseData(ManifestWith("""
            { "commands": [ { "command": "sample.run", "title": "Run", "category": "Sample" } ] }
            """)).SetName("BASELINE commands array with a string-free icon");

        yield return new TestCaseData(ManifestWith("""
            { "grammars": [ { "language": "sample", "scopeName": "source.sample", "path": "./s.json",
              "embeddedLanguages": { "source.css": "css" } } ] }
            """)).SetName("BASELINE grammars with embeddedLanguages object");

        yield return new TestCaseData(ManifestWith("""
            { "themes": [ { "label": "Sample Dark", "uiTheme": "vs-dark", "path": "./t.json" } ] }
            """)).SetName("BASELINE themes array");
    }

    /// <summary>
    /// The property that was actually broken: a legal manifest shape must not take the whole
    /// extension down. Identity is asserted because identity is what the IDE loses — an extension
    /// that fails to bind is never added, so its grammars and themes never load either.
    /// </summary>
    [Test]
    [TestCaseSource(nameof(RealShapes))]
    public void RealVsCodeManifestShape_BindsWithoutKillingTheExtension(string manifestJson)
    {
        ExtensionManifest? manifest = null;

        Assert.DoesNotThrow(
            () => manifest = JsonSerializer.Deserialize<ExtensionManifest>(
                manifestJson, ExtensionService.ManifestJsonOptions),
            "A legal VS Code manifest shape threw during binding. The caller catches at "
            + "whole-extension scope, so this does not degrade one feature — the entire extension "
            + "disappears, identity and all.");

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("sample"), "identity must survive binding");
    }

    /// <summary>
    /// Binding without throwing is not sufficient: a converter that swallows a section, or a nested
    /// deserialize that loses the naming policy, produces the right element count with every value
    /// blank. These assert the values themselves.
    /// </summary>
    [Test]
    public void ManifestShapes_PreserveTheirValues()
    {
        var json = ManifestWith("""
            {
              "commands": [ { "command": "sample.run", "title": "Run",
                              "icon": { "light": "l.svg", "dark": "d.svg" } } ],
              "menus": { "editor/context": [ { "command": "sample.run", "group": "navigation" } ] }
            }
            """);

        var manifest = JsonSerializer.Deserialize<ExtensionManifest>(
            json, ExtensionService.ManifestJsonOptions);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Contributes, Is.Not.Null);

        // Commands is one of only two sections read from the DTO, so losing it is real
        // functional loss: an empty command palette and a dead onCommand activation path.
        Assert.That(manifest.Contributes!.Commands, Has.Count.EqualTo(1));
        Assert.That(manifest.Contributes.Commands[0].Command, Is.EqualTo("sample.run"),
            "a blank value here means the nested bind lost the naming policy");
        Assert.That(manifest.Contributes.Commands[0].Title, Is.EqualTo("Run"));
    }
}
