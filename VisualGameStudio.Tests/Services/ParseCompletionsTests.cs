using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class ParseCompletionsTests
{
    private static JsonElement Json(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    [Test]
    public void ParseCompletions_ArrayResult_ParsesItems()
    {
        var result = Json("""[{"label":"WriteLine","kind":2}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Label, Is.EqualTo("WriteLine"));
        Assert.That(items[0].Kind, Is.EqualTo(CompletionItemKind.Method));
    }

    [Test]
    public void ParseCompletions_CompletionListResult_ParsesItems()
    {
        var result = Json("""{"isIncomplete":false,"items":[{"label":"Trim","kind":2}]}""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Label, Is.EqualTo("Trim"));
    }

    [Test]
    public void ParseCompletions_InsertTextFormatSnippet_IsParsed()
    {
        var result = Json("""[{"label":"Sub","kind":14,"insertText":"Sub ${1:Name}()\n\t$0\nEnd Sub","insertTextFormat":2}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].InsertTextFormat, Is.EqualTo(InsertTextFormat.Snippet));
        Assert.That(items[0].InsertText, Is.EqualTo("Sub ${1:Name}()\n\t$0\nEnd Sub"));
    }

    [Test]
    public void ParseCompletions_InsertTextFormatPlainText_IsParsed()
    {
        var result = Json("""[{"label":"x","kind":6,"insertTextFormat":1}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].InsertTextFormat, Is.EqualTo(InsertTextFormat.PlainText));
    }

    [Test]
    public void ParseCompletions_MissingInsertTextFormat_DefaultsToPlainText()
    {
        var result = Json("""[{"label":"x","kind":6}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].InsertTextFormat, Is.EqualTo(InsertTextFormat.PlainText));
    }

    [Test]
    public void ParseCompletions_Preselect_IsParsed()
    {
        var result = Json("""[{"label":"best","preselect":true},{"label":"other"}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].Preselect, Is.True);
        Assert.That(items[1].Preselect, Is.False);
    }

    [Test]
    public void ParseCompletions_DocumentationString_IsParsed()
    {
        var result = Json("""[{"label":"Trim","documentation":"Removes whitespace."}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].Documentation, Is.EqualTo("Removes whitespace."));
    }

    [Test]
    public void ParseCompletions_DocumentationMarkupContent_IsParsed()
    {
        var result = Json("""[{"label":"Trim","documentation":{"kind":"markdown","value":"**Removes** whitespace."}}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].Documentation, Is.EqualTo("**Removes** whitespace."));
    }

    [Test]
    public void ParseCompletions_MalformedDocumentation_IsTreatedAsAbsent_NotThrow()
    {
        // Malformed per LSP, but a server bug must not throw away the WHOLE completion
        // reply: a MarkupContent whose "value" is not a string, a documentation that is
        // neither string nor object, and a value-less MarkupContent all parse as "no
        // documentation".
        var result = Json("""
            [{"label":"a","documentation":{"kind":"markdown","value":42}},
             {"label":"b","documentation":123},
             {"label":"c","documentation":{"kind":"markdown"}}]
            """);

        IReadOnlyList<CompletionItem> items = null!;
        Assert.DoesNotThrow(() => items = LanguageService.ParseCompletions(result));

        Assert.That(items, Has.Count.EqualTo(3));
        Assert.That(items[0].Documentation, Is.Null, "non-string MarkupContent value → absent");
        Assert.That(items[1].Documentation, Is.Null, "non-string, non-object documentation → absent");
        Assert.That(items[2].Documentation, Is.Null, "value-less MarkupContent → absent");
    }

    [Test]
    public void ParseCompletions_SortTextAndFilterText_AreParsed()
    {
        var result = Json("""[{"label":"Trim","sortText":"08904_Trim","filterText":"trim"}]""");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items[0].SortText, Is.EqualTo("08904_Trim"));
        Assert.That(items[0].FilterText, Is.EqualTo("trim"));
    }

    [Test]
    public void ParseCompletions_NullResult_ReturnsEmpty()
    {
        var result = Json("null");

        var items = LanguageService.ParseCompletions(result);

        Assert.That(items, Is.Empty);
    }
}
