using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// Pins LSP capability negotiation: the <c>initialize</c> result must be PARSED
/// (it used to be discarded, making "server does not support X" indistinguishable
/// from "server works fine but the file is empty"), and the client must advertise
/// utf-16 position encoding — see <see cref="ClientCapabilities_AdvertisePositionEncoding_Utf16_Only"/>.
/// Tests the pure static parser, so no real server process is needed.
/// </summary>
[TestFixture]
public class CapabilityNegotiationTests
{
    [Test]
    public void ParseServerCapabilities_ReadsCompletionAndHoverProviders()
    {
        var json = """
        {"capabilities":{"completionProvider":{"resolveProvider":true},
                         "hoverProvider":true,"definitionProvider":true,
                         "positionEncoding":"utf-16"}}
        """;

        var caps = LanguageService.ParseServerCapabilities(json);

        Assert.Multiple(() =>
        {
            Assert.That(caps.HasCompletionProvider, Is.True);
            Assert.That(caps.CompletionResolveProvider, Is.True);   // clangd = true, BasicLang = false
            Assert.That(caps.HasHoverProvider, Is.True);
            Assert.That(caps.HasDefinitionProvider, Is.True);
            Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
        });
    }

    [Test]
    public void ParseServerCapabilities_MissingProviders_AreFalse_NotThrow()
    {
        var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{}}""");

        Assert.That(caps.HasCompletionProvider, Is.False);
    }

    [Test]
    public void ParseServerCapabilities_Malformed_ReturnsEmptyCapabilities_NotThrow()
    {
        ServerCapabilities caps = null!;

        Assert.DoesNotThrow(() => caps = LanguageService.ParseServerCapabilities("not json"));

        // The name promises "ReturnsEmptyCapabilities" as well as "NotThrow" — assert both.
        Assert.Multiple(() =>
        {
            Assert.That(caps.HasCompletionProvider, Is.False);
            Assert.That(caps.CompletionResolveProvider, Is.False);
            Assert.That(caps.HasHoverProvider, Is.False);
            Assert.That(caps.HasDefinitionProvider, Is.False);
            Assert.That(caps.HasReferencesProvider, Is.False);
            Assert.That(caps.HasDocumentSymbolProvider, Is.False);
            Assert.That(caps.HasSignatureHelpProvider, Is.False);
            Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
        });
    }

    // THE PIN. LSP positions are UTF-16 code units and this client converts them as
    // `character = column - 1` at 12+ call sites against AvaloniaEdit's Caret.Column.
    [Test]
    public void ClientCapabilities_AdvertisePositionEncoding_Utf16_Only()
    {
        var json = JsonSerializer.Serialize(LanguageService.BuildClientCapabilities());

        Assert.That(json, Does.Contain("utf-16"),
            "LSP positions are UTF-16 code units; AvaloniaEdit Caret.Column matches ONLY utf-16. " +
            "If this ever becomes utf-8, every position on every non-ASCII line shifts silently.");
        Assert.That(json, Does.Not.Contain("utf-8"));
    }

    // ---- Parser edge cases -------------------------------------------------

    [Test]
    public void ParseServerCapabilities_ProviderOptionsObject_CountsAsSupported()
    {
        // LSP types these as `boolean | XxxOptions` — a server that answers with the
        // options object DOES support the feature. Treating an object as "unsupported"
        // would silently disable features on spec-conformant servers.
        var json = """
        {"capabilities":{"hoverProvider":{"workDoneProgress":true},
                         "definitionProvider":{"workDoneProgress":false},
                         "referencesProvider":{},
                         "documentSymbolProvider":true,
                         "signatureHelpProvider":{"triggerCharacters":["("]}}}
        """;

        var caps = LanguageService.ParseServerCapabilities(json);

        Assert.Multiple(() =>
        {
            Assert.That(caps.HasHoverProvider, Is.True);
            Assert.That(caps.HasDefinitionProvider, Is.True);
            Assert.That(caps.HasReferencesProvider, Is.True);
            Assert.That(caps.HasDocumentSymbolProvider, Is.True);
            Assert.That(caps.HasSignatureHelpProvider, Is.True);
        });
    }

    [Test]
    public void ParseServerCapabilities_ExplicitlyFalseProviders_AreFalse()
    {
        var json = """
        {"capabilities":{"hoverProvider":false,"definitionProvider":false}}
        """;

        var caps = LanguageService.ParseServerCapabilities(json);

        Assert.Multiple(() =>
        {
            Assert.That(caps.HasHoverProvider, Is.False);
            Assert.That(caps.HasDefinitionProvider, Is.False);
        });
    }

    [Test]
    public void ParseServerCapabilities_CompletionWithoutResolve_ResolveIsFalse()
    {
        // BasicLang's shape: it offers completion but no completionItem/resolve.
        var json = """
        {"capabilities":{"completionProvider":{"triggerCharacters":["."]}}}
        """;

        var caps = LanguageService.ParseServerCapabilities(json);

        Assert.Multiple(() =>
        {
            Assert.That(caps.HasCompletionProvider, Is.True);
            Assert.That(caps.CompletionResolveProvider, Is.False);
        });
    }

    [Test]
    public void ParseServerCapabilities_MissingPositionEncoding_DefaultsToUtf16()
    {
        // LSP 3.17: `positionEncoding` is optional and defaults to utf-16.
        var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{}}""");

        Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
    }

    [Test]
    public void ParseServerCapabilities_ServerPickedUtf8_IsReportedVerbatim()
    {
        // We never ask for utf-8, but if a server ever answers with it we must be able
        // to SEE that rather than silently assume utf-16 and mis-place every position.
        var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{"positionEncoding":"utf-8"}}""");

        Assert.That(caps.PositionEncoding, Is.EqualTo("utf-8"));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("null")]
    [TestCase("[1,2,3]")]
    [TestCase("\"a string\"")]
    [TestCase("""{"no-capabilities-member":true}""")]
    [TestCase("""{"capabilities":null}""")]
    [TestCase("""{"capabilities":"not an object"}""")]
    public void ParseServerCapabilities_NonConformingResult_ReturnsEmptyCapabilities(string json)
    {
        ServerCapabilities caps = null!;

        Assert.DoesNotThrow(() => caps = LanguageService.ParseServerCapabilities(json));
        Assert.Multiple(() =>
        {
            Assert.That(caps.HasCompletionProvider, Is.False);
            Assert.That(caps.HasHoverProvider, Is.False);
            Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
        });
    }

    [Test]
    public void BuildClientCapabilities_KeepsExistingAdvertisedSurface()
    {
        // The blob is extracted verbatim; this task adds `general.positionEncodings`
        // and must not otherwise change what BasicLang is told.
        var json = JsonSerializer.Serialize(LanguageService.BuildClientCapabilities());

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("synchronization"));
            Assert.That(json, Does.Contain("completion"));
            Assert.That(json, Does.Contain("hover"));
            Assert.That(json, Does.Contain("signatureHelp"));
            Assert.That(json, Does.Contain("definition"));
            Assert.That(json, Does.Contain("references"));
            Assert.That(json, Does.Contain("documentSymbol"));
            Assert.That(json, Does.Contain("publishDiagnostics"));
            Assert.That(json, Does.Contain("positionEncodings"));
        });
    }
}
