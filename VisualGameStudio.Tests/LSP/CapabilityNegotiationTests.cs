using System.Text.Json;
using System.Text.Json.Nodes;
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
            Assert.That(caps.HasCompletionResolveProvider, Is.True);
            Assert.That(caps.HasHoverProvider, Is.True);
            Assert.That(caps.HasDefinitionProvider, Is.True);
            Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
        });
    }

    // THE GUARD. A response with no `result` member reaches us as a default/Undefined
    // JsonElement (ProcessMessage: tcs.SetResult(default)), whose GetRawText() throws
    // InvalidOperationException — which a `catch (JsonException)` would NOT catch. The
    // parser takes the element precisely so no call site has to remember this.
    [Test]
    public void ParseServerCapabilities_UndefinedElement_ReturnsEmptyCapabilities_NotThrow()
    {
        ServerCapabilities caps = null!;

        Assert.DoesNotThrow(() => caps = LanguageService.ParseServerCapabilities(default(JsonElement)));
        Assert.Multiple(() =>
        {
            Assert.That(caps.HasCompletionProvider, Is.False);
            Assert.That(caps.HasHoverProvider, Is.False);
            Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
        });
    }

    [Test]
    public void ParseServerCapabilities_NullProvider_IsFalse()
    {
        var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{"hoverProvider":null}}""");

        Assert.That(caps.HasHoverProvider, Is.False);
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
            Assert.That(caps.HasCompletionResolveProvider, Is.False);
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
    //
    // Asserts the PATH, not just the presence of the substring: `positionEncodings` is
    // only meaningful to a server under `general`. Sitting anywhere else (e.g. moved
    // under `textDocument`) would still contain "utf-16" while negotiating nothing at
    // all — the encoding pin would read as green and be entirely inert.
    //
    // The "utf-16" literal is hardcoded deliberately. Referencing ServerCapabilities.Utf16
    // would make this test flip together with the code and assert nothing.
    [Test]
    public void ClientCapabilities_AdvertisePositionEncoding_Utf16_Only()
    {
        var caps = SerializeClientCapabilities();

        var encodings = caps["general"]?["positionEncodings"]?.AsArray();
        Assert.That(encodings, Is.Not.Null,
            "positionEncodings must be advertised at `general.positionEncodings` — that is the " +
            "only place a server reads it. LSP positions are UTF-16 code units and AvaloniaEdit's " +
            "Caret.Column matches ONLY utf-16.");
        Assert.That(encodings!.Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "utf-16" }),
            "utf-16 and ONLY utf-16. If this ever offers utf-8, every position on every " +
            "non-ASCII line shifts silently, with no error anywhere.");
    }

    [Test]
    public void ClientCapabilities_DoNotAdvertisePositionEncoding_AnywhereElse()
    {
        var caps = SerializeClientCapabilities();

        Assert.That(caps["textDocument"]?["positionEncodings"], Is.Null,
            "positionEncodings under textDocument is inert — servers only read general.positionEncodings");
        Assert.That(JsonSerializer.Serialize(caps), Does.Not.Contain("utf-8"));
    }

    private static JsonNode SerializeClientCapabilities() =>
        JsonSerializer.SerializeToNode(LanguageService.BuildClientCapabilities())!;

    // ---- Workspace root ----------------------------------------------------

    private static JsonObject SerializeInitializeParams(string? workspaceRoot) =>
        JsonSerializer.SerializeToNode(LanguageService.BuildInitializeParams(workspaceRoot))!.AsObject();

    // THE ROOT. The client used to send `rootUri: null` and nothing else. A server with
    // no workspace root has no project: clangd cannot locate compile_commands.json and
    // answers with garbage diagnostics for every translation unit — silently, with no
    // error on either side.
    //
    // Asserts the PATH of each member rather than the presence of a substring: `rootUri`
    // sitting under the wrong parent is exactly as invisible to a server as `rootUri`
    // absent, and both read as green to a `Does.Contain` assertion.
    //
    // The expected URI is written out literally rather than calling PathToUri. Deriving
    // it from the code under test would make this flip together with the code and pin
    // nothing at all.
    [Test]
    public void InitializeParams_WithWorkspaceRoot_SendsRootUriRootPathAndWorkspaceFolders()
    {
        const string root = @"C:\projects\My Game";
        const string expectedUri = "file:///C:/projects/My%20Game";

        var parms = SerializeInitializeParams(root);

        Assert.Multiple(() =>
        {
            Assert.That(parms["rootUri"]?.GetValue<string>(), Is.EqualTo(expectedUri),
                "rootUri must be a percent-encoded file:// URI at the top level of initialize params");
            Assert.That(parms["rootPath"]?.GetValue<string>(), Is.EqualTo(root),
                "rootPath is the deprecated-but-still-widely-read sibling of rootUri; it carries a PATH, not a URI");

            var folders = parms["workspaceFolders"]?.AsArray();
            Assert.That(folders, Is.Not.Null, "workspaceFolders must be sent");
            Assert.That(folders!.Count, Is.EqualTo(1));
            Assert.That(folders[0]?["uri"]?.GetValue<string>(), Is.EqualTo(expectedUri),
                "a workspaceFolders entry is {uri, name} — bare strings are malformed and worse than sending none");
            Assert.That(folders[0]?["name"]?.GetValue<string>(), Is.EqualTo("My Game"));
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void InitializeParams_NoWorkspaceRoot_OmitsRootMembers_RatherThanSendingNull(string? root)
    {
        var parms = SerializeInitializeParams(root);

        // ContainsKey, NOT `parms["rootUri"] is null`: an absent member and a member whose
        // value is JSON `null` both read back as a null JsonNode. "This client has no
        // workspace" and "rootUri: null" are different messages, and only omission is
        // correct. Omission must also be structural — it cannot depend on the caller
        // happening to serialize with DefaultIgnoreCondition.WhenWritingNull.
        Assert.Multiple(() =>
        {
            Assert.That(parms.ContainsKey("rootUri"), Is.False);
            Assert.That(parms.ContainsKey("rootPath"), Is.False);
            Assert.That(parms.ContainsKey("workspaceFolders"), Is.False);
        });
    }

    // A workspaceFolders entry's `name` is a display label derived from the directory
    // name. Path.GetFileName alone returns "" for a path with a trailing separator —
    // an empty label is exactly the malformed entry this guards against.
    [TestCase(@"C:\projects\My Game", "My Game")]
    [TestCase(@"C:\projects\My Game\", "My Game")]
    [TestCase(@"C:\projects\Game", "Game")]
    [TestCase(@"\\build-server\shared\Game", "Game")]
    [TestCase(@"C:\projets\Jeu Vidéo", "Jeu Vidéo")]
    // A drive root has no name component to take; the documented fallback is the path
    // itself. Pins that a root can never produce the empty label this method guards against.
    [TestCase(@"C:\", @"C:\")]
    public void InitializeParams_WorkspaceFolderName_IsDirectoryName_EvenWithTrailingSeparator(
        string root, string expectedName)
    {
        var parms = SerializeInitializeParams(root);

        Assert.That(parms["workspaceFolders"]?.AsArray()[0]?["name"]?.GetValue<string>(),
            Is.EqualTo(expectedName));
    }

    // ---- Working directory -------------------------------------------------

    // Process.Start THROWS on a non-existent WorkingDirectory, so an unusable root must
    // cost us the cwd and not the whole language server. The existence check is injected
    // (mirroring ResolveLspPathOverride) — this assembly has no InternalsVisibleTo for
    // the test project, so a public static is the only reachable seam.
    [Test]
    public void ResolveWorkingDirectory_ExistingDirectory_IsUsed()
    {
        var resolved = LanguageService.ResolveWorkingDirectory(@"C:\projects\Game", _ => true);

        Assert.That(resolved, Is.EqualTo(@"C:\projects\Game"));
    }

    [Test]
    public void ResolveWorkingDirectory_MissingDirectory_IsNull_SoProcessStartCannotThrow()
    {
        var resolved = LanguageService.ResolveWorkingDirectory(@"C:\projects\Deleted", _ => false);

        Assert.That(resolved, Is.Null,
            "a root that names no directory must yield null (inherit the IDE cwd), never a " +
            "WorkingDirectory that makes Process.Start throw and take the server down with it");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ResolveWorkingDirectory_NoRoot_IsNull_WithoutTouchingTheFilesystem(string? root)
    {
        var probed = false;

        var resolved = LanguageService.ResolveWorkingDirectory(root, _ => { probed = true; return true; });

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.Null);
            Assert.That(probed, Is.False, "no root means nothing to probe for");
        });
    }

    // THE ASYMMETRY. The wire root and the working directory used to trim independently:
    // BuildInitializeParams trimmed, StartCoreAsync did not — so a padded root reached
    // `initialize` as C:\proj while the working directory was silently skipped, because
    // Directory.Exists("  C:\proj  ") is false. Both now normalize through one rule, so
    // they cannot disagree about what the root IS.
    [Test]
    public void PaddedRoot_ReachesWireAndWorkingDirectory_Identically()
    {
        const string padded = "  C:\\projects\\Game  ";
        const string trimmed = @"C:\projects\Game";

        var probedWith = (string?)null;
        var workingDirectory = LanguageService.ResolveWorkingDirectory(padded, p => { probedWith = p; return true; });
        var wireRoot = SerializeInitializeParams(padded)["rootPath"]?.GetValue<string>();

        Assert.Multiple(() =>
        {
            Assert.That(probedWith, Is.EqualTo(trimmed), "the existence check must probe the TRIMMED root");
            Assert.That(workingDirectory, Is.EqualTo(trimmed));
            Assert.That(wireRoot, Is.EqualTo(trimmed));
            Assert.That(workingDirectory, Is.EqualTo(wireRoot),
                "the working directory and the root on the wire must be the same string");
        });
    }

    [TestCase(null)]
    [TestCase(@"C:\projects\Game")]
    public void InitializeParams_AlwaysCarryProcessIdAndCapabilities(string? root)
    {
        // Adding the workspace root must not displace what initialize already carried.
        var parms = SerializeInitializeParams(root);

        Assert.Multiple(() =>
        {
            Assert.That(parms["processId"]?.GetValue<int>(), Is.EqualTo(Environment.ProcessId));
            Assert.That(parms["capabilities"]?["general"]?["positionEncodings"], Is.Not.Null,
                "the negotiated position encoding must survive alongside the workspace root");
        });
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
            Assert.That(caps.HasCompletionResolveProvider, Is.False);
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

    // ---- clangd's non-standard offsetEncoding ------------------------------

    // Probed against REAL clangd 22.1.6 (Task 12, Step 0). Its initialize result carries
    // BOTH `capabilities.positionEncoding` (the standard 3.17 field) and a TOP-LEVEL
    // `offsetEncoding` — clangd's own, which predates the spec. Both answered "utf-16",
    // but only BECAUSE this client advertises general.positionEncodings:["utf-16"]:
    // clangd's own default is utf-8. The pin wins the negotiation; it is not luck.
    //
    // Reading it is the difference between "safe because we asked nicely" and "safe
    // because we checked" — see DescribeEncodingMismatch below.
    [Test]
    public void ParseServerCapabilities_ReadsClangdsTopLevelOffsetEncoding()
    {
        // The shape real clangd replies with, trimmed to the members this client reads.
        var json = """
        {"capabilities":{"positionEncoding":"utf-16","hoverProvider":true},
         "offsetEncoding":"utf-16",
         "serverInfo":{"name":"clangd","version":"22.1.6"}}
        """;

        var caps = LanguageService.ParseServerCapabilities(json);

        Assert.Multiple(() =>
        {
            Assert.That(caps.PositionEncoding, Is.EqualTo("utf-16"));
            Assert.That(caps.OffsetEncoding, Is.EqualTo("utf-16"));
            Assert.That(caps.HasHoverProvider, Is.True, "the sibling member must not displace the capabilities parse");
        });
    }

    // Null means "the server never said", which is NOT the claim "the server said utf-16".
    // Only the latter is a verified answer; conflating them would make the guard vacuous
    // for every server that omits the field.
    [Test]
    public void ParseServerCapabilities_NoOffsetEncoding_IsNull_NotUtf16()
    {
        var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{}}""");

        Assert.That(caps.OffsetEncoding, Is.Null);
    }

    // THE PARENT. offsetEncoding is a SIBLING of capabilities, not a member of it. Read
    // from the wrong parent it would answer null forever and the guard would never fire —
    // green, and completely inert. (Same failure mode as positionEncodings under the wrong
    // parent, above.)
    [Test]
    public void ParseServerCapabilities_OffsetEncodingNestedInsideCapabilities_IsNotRead()
    {
        var caps = LanguageService.ParseServerCapabilities("""{"capabilities":{"offsetEncoding":"utf-8"}}""");

        Assert.That(caps.OffsetEncoding, Is.Null,
            "a value nested inside capabilities is not clangd's top-level offsetEncoding");
    }

    // A top-level offsetEncoding must survive a result this parser otherwise treats as
    // non-conforming — otherwise a reply with no `capabilities` member could smuggle utf-8
    // past the guard.
    [Test]
    public void ParseServerCapabilities_OffsetEncodingSurvivesAMissingCapabilitiesMember()
    {
        var caps = LanguageService.ParseServerCapabilities("""{"offsetEncoding":"utf-8"}""");

        Assert.That(caps.OffsetEncoding, Is.EqualTo("utf-8"));
    }

    [TestCase("""{"capabilities":{},"offsetEncoding":123}""")]
    [TestCase("""{"capabilities":{},"offsetEncoding":null}""")]
    [TestCase("""{"capabilities":{},"offsetEncoding":{"value":"utf-8"}}""")]
    public void ParseServerCapabilities_NonStringOffsetEncoding_IsNull_NotThrow(string json)
    {
        ServerCapabilities caps = null!;

        Assert.DoesNotThrow(() => caps = LanguageService.ParseServerCapabilities(json));
        Assert.That(caps.OffsetEncoding, Is.Null);
    }

    // ---- The encoding guard ------------------------------------------------
    //
    // THE ONE THING IN PHASE 3A THAT CAN SILENTLY CORRUPT EVERY POSITION WE SEND.
    // Positions convert as `character = column - 1` against AvaloniaEdit's Caret.Column
    // (1-based UTF-16 code units) at 12+ call sites. A server answering utf-8 shifts every
    // position on every line containing a non-ASCII character — with no error on either
    // side, and a server-supplied textEdit would then land in the wrong place.

    [Test]
    public void DescribeEncodingMismatch_ServerAgreedUtf16_IsNull()
    {
        var caps = new ServerCapabilities { PositionEncoding = "utf-16", OffsetEncoding = "utf-16" };

        Assert.That(LanguageService.DescribeEncodingMismatch(caps), Is.Null);
    }

    // A silent server is safe: LSP 3.17 defaults positionEncoding to utf-16, and a server
    // that never sends clangd's field has not claimed anything to contradict it. BasicLang's
    // own server is this case — the guard must not break it.
    [Test]
    public void DescribeEncodingMismatch_ServerSaidNothing_IsNull_PerTheLspDefault()
    {
        Assert.That(LanguageService.DescribeEncodingMismatch(new ServerCapabilities()), Is.Null);
    }

    [TestCase("utf-8")]
    [TestCase("utf-32")]
    public void DescribeEncodingMismatch_PositionEncodingIsNotUtf16_IsReported(string encoding)
    {
        var caps = new ServerCapabilities { PositionEncoding = encoding };

        Assert.That(LanguageService.DescribeEncodingMismatch(caps), Does.Contain(encoding));
    }

    // THE DIVERGENCE. A reply whose standard field reads utf-16 while clangd's own field says
    // utf-8 must still be refused — offsetEncoding is what clangd's semantics are defined
    // against, so it wins where they disagree.
    //
    // ⚠ This shape is SYNTHETIC, deliberately. Real clangd 22.1.6 moves both fields together
    // (measured: `--offset-encoding=utf-8` produced utf-8 in BOTH), so no clangd shipping today
    // is known to answer like this — do not read this test as evidence that one does. It pins
    // that the second read is a real check rather than decoration, for a future version or a
    // server that borrows the extension without the standard field.
    [Test]
    public void DescribeEncodingMismatch_ClangdOffsetEncodingIsUtf8_IsReported_EvenWhenPositionEncodingLooksFine()
    {
        var caps = new ServerCapabilities { PositionEncoding = "utf-16", OffsetEncoding = "utf-8" };

        var mismatch = LanguageService.DescribeEncodingMismatch(caps);

        Assert.That(mismatch, Is.Not.Null,
            "reading only the standard field is how a utf-8 clangd stays invisible");
        Assert.That(mismatch, Does.Contain("utf-8"));
    }

    // Casing is not the contract — an "UTF-16" is still utf-16 and must not take C++
    // IntelliSense down over a spelling difference.
    [TestCase("UTF-16")]
    [TestCase("Utf-16")]
    public void DescribeEncodingMismatch_Utf16InAnyCasing_IsAccepted(string encoding)
    {
        var caps = new ServerCapabilities { PositionEncoding = encoding, OffsetEncoding = encoding };

        Assert.That(LanguageService.DescribeEncodingMismatch(caps), Is.Null);
    }

    [TestCase("synchronization")]
    [TestCase("completion")]
    [TestCase("hover")]
    [TestCase("signatureHelp")]
    [TestCase("definition")]
    [TestCase("references")]
    [TestCase("documentSymbol")]
    [TestCase("publishDiagnostics")]
    public void BuildClientCapabilities_KeepsExistingAdvertisedSurface(string member)
    {
        // The blob was extracted verbatim; this task adds `general.positionEncodings`
        // and must not otherwise change what the server is told. Asserts each member
        // sits under `textDocument` rather than merely appearing somewhere in the JSON.
        var caps = SerializeClientCapabilities();

        Assert.That(caps["textDocument"]?[member], Is.Not.Null,
            $"textDocument.{member} must still be advertised");
    }
}
