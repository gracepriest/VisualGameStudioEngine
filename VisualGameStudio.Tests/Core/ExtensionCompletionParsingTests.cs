using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

/// <summary>
/// Verifies parsing of extension-host (vscode API-shaped) completion items,
/// especially the SnippetString object form of insertText that previously
/// threw and truncated the whole list.
/// </summary>
[TestFixture]
public class ExtensionCompletionParsingTests
{
    private static JsonElement Json(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    [Test]
    public void Parse_ArrayOfSimpleItems_ReturnsAll()
    {
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":"alpha","kind":1,"detail":"d1","insertText":"alpha()"},{"label":"beta"}]"""));

        Assert.That(result, Has.Count.EqualTo(2));
        Assert.That(result[0].Label, Is.EqualTo("alpha"));
        Assert.That(result[0].InsertText, Is.EqualTo("alpha()"));
        Assert.That(result[0].Detail, Is.EqualTo("d1"));
        Assert.That(result[0].Kind, Is.EqualTo(CompletionItemKind.Method));
        Assert.That(result[1].InsertText, Is.EqualTo("beta"), "insertText defaults to the label");
    }

    [Test]
    public void Parse_CompletionListFormat_ReadsItems()
    {
        var result = ExtensionCompletionParsing.Parse(Json(
            """{"items":[{"label":"alpha"}]}"""));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Label, Is.EqualTo("alpha"));
    }

    [Test]
    public void Parse_SnippetStringInsertText_ObjectForm_ParsesValueAndMarksSnippet()
    {
        // vscode.SnippetString serializes as {"value": "..."}; GetString() on
        // that object used to throw InvalidOperationException.
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":"for","insertText":{"value":"for ${1:i}"}}]"""));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InsertText, Is.EqualTo("for ${1:i}"));
        Assert.That(result[0].InsertTextFormat, Is.EqualTo(InsertTextFormat.Snippet),
            "a SnippetString is snippet-format by definition — raw ${1:...} markers must never be inserted literally");
    }

    [Test]
    public void Parse_SnippetStringItem_DoesNotTruncateRemainingItems()
    {
        // The old single try/catch around the whole foreach dropped every item
        // AFTER the first SnippetString one.
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":"first"},{"label":"snippet","insertText":{"value":"s $0"}},{"label":"last"}]"""));

        Assert.That(result.Select(c => c.Label), Is.EqualTo(new[] { "first", "snippet", "last" }));
    }

    [Test]
    public void Parse_MalformedItem_IsSkipped_RestSurvives()
    {
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":"good1"},{"label":123},{"kind":1},{"label":"good2","insertText":42}]"""));

        // label:123 and missing-label items are skipped; numeric insertText
        // falls back to the label instead of aborting.
        Assert.That(result.Select(c => c.Label), Is.EqualTo(new[] { "good1", "good2" }));
        Assert.That(result[1].InsertText, Is.EqualTo("good2"));
    }

    [Test]
    public void Parse_LspShapedItem_InsertTextFormat2_MarksSnippet()
    {
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":"sub","insertText":"Sub ${1:Name}()\n\t$0\nEnd Sub","insertTextFormat":2}]"""));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].InsertTextFormat, Is.EqualTo(InsertTextFormat.Snippet));
    }

    [Test]
    public void Parse_StringInsertText_WithoutFormat_StaysPlainText()
    {
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":"x","insertText":"x"}]"""));

        Assert.That(result[0].InsertTextFormat, Is.EqualTo(InsertTextFormat.PlainText));
    }

    [Test]
    public void Parse_ObjectFormLabel_ReadsInnerLabel()
    {
        var result = ExtensionCompletionParsing.Parse(Json(
            """[{"label":{"label":"styled","detail":" (ext)"}}]"""));

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Label, Is.EqualTo("styled"));
    }

    [Test]
    public void Parse_NonListJson_ReturnsEmpty()
    {
        Assert.That(ExtensionCompletionParsing.Parse(Json("null")), Is.Empty);
        Assert.That(ExtensionCompletionParsing.Parse(Json("42")), Is.Empty);
        Assert.That(ExtensionCompletionParsing.Parse(Json("{\"noItems\":true}")), Is.Empty);
    }
}
