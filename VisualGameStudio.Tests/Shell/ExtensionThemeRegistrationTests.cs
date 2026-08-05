using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell;

namespace VisualGameStudio.Tests.Shell;

/// <summary>
/// Guards the last link in the extension theme pipeline. Extensions' themes were parsed and
/// counted but never registered: ExtensionService.LoadThemesFromExtension called
/// TextMateService.LoadThemeFromJson, set the returned theme's Type, incremented a counter for
/// the Output log, and let the object go out of scope. The counter therefore reported work
/// ATTEMPTED as work COMPLETED — "1 theme(s)" in the log while nothing became selectable.
///
/// The registry (ThemeManager) lives in the Shell and the parser in ProjectSystem, which the
/// Shell references — so ExtensionService cannot call ThemeManager directly. The fix surfaces
/// each theme file's path on the existing (previously unconsumed) ContributionsLoaded event and
/// registers them Shell-side, keeping the dependency direction intact.
/// </summary>
[TestFixture]
public class ExtensionThemeRegistrationTests
{
    private static string? FindRepoFile(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>
    /// The event must be able to carry the paths at all, and must never hand a null list to a
    /// subscriber that will foreach over it.
    /// </summary>
    [Test]
    public void ContributionsLoadedEventArgs_ExposesThemeContributions()
    {
        var args = new ExtensionContributionsLoadedEventArgs(new Extension { Id = "test.ext" });

        Assert.That(args.ThemeContributions, Is.Not.Null, "subscribers enumerate this without a null check");
        Assert.That(args.ThemeContributions, Is.Empty);

        args.ThemeContributions.Add(new ExtensionThemeContribution
        {
            Path = "C:/x/theme.json",
            Label = "My Theme",
            UiTheme = "vs-dark"
        });

        Assert.That(args.ThemeContributions, Has.Count.EqualTo(1));
        Assert.That(args.ThemeContributions[0].Label, Is.EqualTo("My Theme"),
            "the manifest label must survive the hop to the Shell — it is the registry key");
    }

    /// <summary>
    /// Regression for the Dracula case: an extension's theme files may share an internal "name"
    /// ("Dracula" for both dracula.json and dracula-soft.json), so registering by file-derived
    /// name collapses the variants onto one key and the second overwrites the first. The
    /// manifest's distinct labels must be what reaches the registry.
    /// </summary>
    [Test]
    public async Task ThemesWithIdenticalInternalNames_RegisterSeparatelyUnderManifestLabels()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var sharedInternalName = $"Shared_{suffix}";
        var labelA = $"Variant A {suffix}";
        var labelB = $"Variant B {suffix}";

        var pathA = Path.Combine(Path.GetTempPath(), $"themeA_{suffix}.json");
        var pathB = Path.Combine(Path.GetTempPath(), $"themeB_{suffix}.json");
        var body = $"{{ \"name\": \"{sharedInternalName}\", \"type\": \"dark\", \"colors\": {{}} }}";
        await File.WriteAllTextAsync(pathA, body);
        await File.WriteAllTextAsync(pathB, body);

        try
        {
            var a = await ThemeManager.LoadVsCodeThemeFileAsync(pathA, labelA, "vs-dark", "test.ext");
            var b = await ThemeManager.LoadVsCodeThemeFileAsync(pathB, labelB, "vs-dark", "test.ext");

            Assert.That(a, Is.EqualTo(labelA));
            Assert.That(b, Is.EqualTo(labelB));

            Assert.That(ThemeManager.ExtensionThemeNames, Does.Contain(labelA));
            Assert.That(ThemeManager.ExtensionThemeNames, Does.Contain(labelB),
                "both variants must be selectable; registering by the shared internal name would " +
                "leave only one.");
            Assert.That(ThemeManager.ExtensionThemeNames, Does.Not.Contain(sharedInternalName),
                "the file's internal name must not be used as the registry key for manifest themes");
        }
        finally
        {
            try { File.Delete(pathA); } catch { /* best effort */ }
            try { File.Delete(pathB); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// A JSON Schema "type" may be a string or an array of strings. The array form threw, and
    /// because manifest parsing is caught per-EXTENSION, that one field aborted loading the whole
    /// extension — vscode.html-language-features never loaded at all because of
    /// <c>"html.format.unformatted": { "type": ["string", "null"] }</c>.
    /// </summary>
    [TestCase("\"string\"", "string", TestName = "SchemaType_StringForm")]
    [TestCase("[\"string\", \"null\"]", "string", TestName = "SchemaType_ArrayForm_TakesFirstNonNull")]
    [TestCase("[\"null\", \"boolean\"]", "boolean", TestName = "SchemaType_ArrayForm_SkipsLeadingNull")]
    [TestCase("[\"null\"]", "null", TestName = "SchemaType_ArrayForm_AllNull")]
    [TestCase("123", "string", TestName = "SchemaType_UnexpectedShape_FallsBack")]
    public void ConfigurationPropertyType_AcceptsStringAndArrayForms(string typeJson, string expected)
    {
        var json = $"{{ \"type\": {typeJson}, \"scope\": \"resource\" }}";

        var prop = System.Text.Json.JsonSerializer.Deserialize<ConfigurationProperty>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(prop, Is.Not.Null);
        Assert.That(prop!.Type, Is.EqualTo(expected));
        Assert.That(prop.Scope, Is.EqualTo("resource"),
            "parsing must continue past the type field rather than consuming the rest of the object");
    }

    /// <summary>
    /// Behavioural: the registration mechanism the fix depends on genuinely makes a theme
    /// selectable. ThemeManager.LoadVsCodeThemeFileAsync is the same call the file-picker import
    /// command uses, and is headless-safe (only Apply touches Application.Current).
    /// </summary>
    [Test]
    public async Task LoadVsCodeThemeFile_MakesTheThemeSelectable()
    {
        var themeName = $"ExtThemeTest_{Guid.NewGuid():N}";
        var path = Path.Combine(Path.GetTempPath(), $"{themeName}.json");
        await File.WriteAllTextAsync(path,
            $"{{ \"name\": \"{themeName}\", \"type\": \"dark\", \"colors\": {{}} }}");

        try
        {
            var label = await ThemeManager.LoadVsCodeThemeFileAsync(path);

            Assert.That(label, Is.Not.Null, "a valid VS Code theme file must load");
            Assert.That(ThemeManager.ExtensionThemeNames, Does.Contain(themeName),
                "a registered theme must appear among the selectable theme names — this is the step " +
                "the extension path was missing entirely.");
            Assert.That(ThemeManager.IsKnownTheme(themeName), Is.True,
                "and it must resolve as a known theme so a saved selection survives a restart.");
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// A grammar contribution's embeddedLanguages/tokenTypes are OBJECTS (scope -> id), not
    /// arrays. Typed as List&lt;string&gt; they threw, and because manifest parsing is caught at the
    /// whole-extension level that took all of vscode.html down — grammar, language and snippets.
    /// Note the orphaned VSCodeExtension model in Core/Extensions already had these right; only
    /// the wired one was wrong, which is this subsystem's recurring shape.
    /// </summary>
    [Test]
    public void GrammarContribution_ParsesEmbeddedLanguagesAndTokenTypesAsObjects()
    {
        // Shape taken verbatim from vscode.html 1.95.3's package.json.
        const string json = """
        {
          "scopeName": "text.html.basic",
          "path": "./syntaxes/html.tmLanguage.json",
          "embeddedLanguages": { "text.html": "html", "source.css": "css", "source.js": "javascript" },
          "tokenTypes": { "meta.embedded.block.html": "other" }
        }
        """;

        var grammar = System.Text.Json.JsonSerializer.Deserialize<GrammarContribution>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(grammar, Is.Not.Null);
        Assert.That(grammar!.ScopeName, Is.EqualTo("text.html.basic"));
        Assert.That(grammar.EmbeddedLanguages, Has.Count.EqualTo(3));
        Assert.That(grammar.EmbeddedLanguages["source.css"], Is.EqualTo("css"));
        Assert.That(grammar.TokenTypes["meta.embedded.block.html"], Is.EqualTo("other"));
    }

    /// <summary>
    /// Source guard: ExtensionService must hand the collected paths out on the event. Its
    /// extensions directory is hard-coded to ~/.vgs/extensions, so a behavioural test at that
    /// layer would write into the developer's real profile.
    /// </summary>
    [Test]
    public void ExtensionService_SurfacesThemeFilePathsOnTheEvent()
    {
        var path = FindRepoFile("VisualGameStudio.ProjectSystem", "Services", "ExtensionService.cs");
        if (path == null)
        {
            Assert.Ignore("ExtensionService.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        Assert.That(src, Does.Contain("LoadThemesFromExtension(extension, stats.ThemeContributions)"),
            "the theme loader must record each resolved theme on the event args, otherwise the " +
            "Shell has nothing to register and themes are silently dropped again.");
        Assert.That(src, Does.Contain("Label = label"),
            "the manifest label must be carried across — deriving identity from the theme file " +
            "collapses same-named variants onto one registry entry.");
    }

    /// <summary>
    /// Source guard: the Shell must actually consume the event. ContributionsLoaded existed and
    /// was fired for a long time with no subscriber at all — declaring it is not wiring it.
    /// </summary>
    [Test]
    public void MainWindowViewModel_SubscribesAndRegistersExtensionThemes()
    {
        var path = FindRepoFile("VisualGameStudio.Shell", "ViewModels", "MainWindowViewModel.cs");
        if (path == null)
        {
            Assert.Ignore("MainWindowViewModel.cs not found from the test base directory — skipping source guard.");
            return;
        }

        var src = File.ReadAllText(path);

        Assert.That(src, Does.Contain("ContributionsLoaded += OnExtensionContributionsLoaded"),
            "the Shell must subscribe to ContributionsLoaded — the event was fired with no listener before.");
        Assert.That(src, Does.Contain("ThemeManager.LoadVsCodeThemeFileAsync("),
            "and the handler must register each contributed theme with ThemeManager.");
    }
}
