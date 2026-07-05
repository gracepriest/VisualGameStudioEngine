using NUnit.Framework;
using BasicLang.Compiler.LSP;
using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Finding [18]: completion items must carry Documentation (the doc strings
/// exist server-side), and the completionItem/resolve handler (advertised via
/// ResolveProvider=true) must attach documentation lazily via
/// CompletionItem.Data instead of being a no-op.
/// </summary>
[TestFixture]
public class CompletionResolveTests
{
    private CompletionService _completionService = null!;
    private DocumentManager _documentManager = null!;
    private CompletionHandler _completionHandler = null!;

    [SetUp]
    public void SetUp()
    {
        _completionService = new CompletionService();
        _documentManager = new DocumentManager();
        _completionHandler = new CompletionHandler(_documentManager, _completionService);
    }

    private DocumentState CreateManagedState(string sourceCode, string fileName = "test.bas")
    {
        var uri = DocumentUri.From($"file:///{fileName}");
        return _documentManager.UpdateDocument(uri, sourceCode);
    }

    private static string DocText(StringOrMarkupContent? doc)
    {
        if (doc == null) return null!;
        return doc.HasMarkupContent ? doc.MarkupContent!.Value : doc.String!;
    }

    [Test]
    public void BuiltInFunctions_CarryDocumentation()
    {
        var state = CreateManagedState("Sub Main()\nEnd Sub");

        var result = _completionService.GetCompletions(state, 1, 0);

        var printLine = result.FirstOrDefault(c => c.Label == "PrintLine");
        Assert.That(printLine, Is.Not.Null);
        Assert.That(DocText(printLine!.Documentation), Is.Not.Null.And.Not.Empty,
            "built-in function items must carry Documentation");
    }

    [Test]
    public void KeywordSnippets_CarryDocumentation()
    {
        var state = CreateManagedState("Sub Main()\nEnd Sub");

        var result = _completionService.GetCompletions(state, 1, 0);

        var subKeyword = result.FirstOrDefault(c => c.Label == "Sub" && c.Kind == CompletionItemKind.Keyword);
        Assert.That(subKeyword, Is.Not.Null);
        Assert.That(DocText(subKeyword!.Documentation), Is.Not.Null.And.Not.Empty,
            "keyword snippet items must carry Documentation");
    }

    [Test]
    public void NetMemberItems_CarryResolveData()
    {
        var source = "Sub Main()\n    Console.\nEnd Sub";
        var state = CreateManagedState(source);

        var result = _completionService.GetCompletions(state, 1, "    Console.".Length);

        var writeLine = result.FirstOrDefault(c => c.Label == "WriteLine");
        Assert.That(writeLine, Is.Not.Null);
        Assert.That(writeLine!.Data, Is.Not.Null, "member items must stash type+member in Data for lazy resolve");

        var data = writeLine.Data as JObject;
        Assert.That(data, Is.Not.Null);
        Assert.That(data!["member"]?.ToString(), Is.EqualTo("WriteLine"));
        Assert.That(data["type"]?.ToString(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task ResolveHandler_AttachesDocumentation()
    {
        var source = "Sub Main()\n    Console.\nEnd Sub";
        var state = CreateManagedState(source);
        var result = _completionService.GetCompletions(state, 1, "    Console.".Length);

        var writeLine = result.FirstOrDefault(c => c.Label == "WriteLine");
        Assert.That(writeLine, Is.Not.Null);

        var resolved = await _completionHandler.Handle(writeLine!, CancellationToken.None);

        Assert.That(resolved, Is.Not.Null);
        var docText = DocText(resolved.Documentation);
        Assert.That(docText, Is.Not.Null.And.Not.Empty,
            "the resolve handler must attach Documentation from the stashed Data");
        Assert.That(docText, Does.Contain("WriteLine"), "documentation should describe the member");
    }

    [Test]
    public async Task ResolveHandler_FillsMissingDocumentationFromData()
    {
        // An item that arrives with Data but no Documentation (the lazy path)
        var item = new CompletionItem
        {
            Label = "WriteLine",
            Kind = CompletionItemKind.Method,
            Data = new JObject { ["type"] = "Console", ["member"] = "WriteLine" }
        };

        var resolved = await _completionHandler.Handle(item, CancellationToken.None);

        var docText = DocText(resolved.Documentation);
        Assert.That(docText, Is.Not.Null.And.Not.Empty,
            "resolve must fill Documentation from Data when it is missing");
        Assert.That(docText, Does.Contain("WriteLine"));
    }

    [Test]
    public async Task ResolveHandler_WithoutData_EchoesItemBack()
    {
        var item = new CompletionItem { Label = "Whatever", Kind = CompletionItemKind.Text };

        var resolved = await _completionHandler.Handle(item, CancellationToken.None);

        Assert.That(resolved, Is.Not.Null);
        Assert.That(resolved.Label, Is.EqualTo("Whatever"));
    }
}
