using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Models;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.LSP;

/// <summary>
/// The CLIENT side of <c>completionItem/resolve</c>: <see cref="LanguageService"/> acting as an
/// LSP client — preserving the server's opaque <c>data</c> token through
/// <see cref="LanguageService.ParseCompletions"/>, gating the request on
/// <see cref="ServerCapabilities.HasCompletionResolveProvider"/>, building the exact wire params,
/// and merging the reply's documentation/detail into the item.
///
/// <para>
/// ⚠ Not to be confused with <see cref="CompletionResolveTests"/>
/// (VisualGameStudio.Tests/LSP/CompletionResolveTests.cs) — that fixture is the SERVER side:
/// BasicLang's own <c>CompletionHandler</c> serving resolve to any client. This fixture is the
/// IDE consuming it. The two meet in
/// <see cref="Resolve_AgainstTheRealBasicLangServer_FillsLazyDocs"/>, where this client resolves
/// against that live server.
/// </para>
///
/// <para>
/// The pure seams (parse / predicate / builder / merge) are all public statics — this assembly
/// has no <c>InternalsVisibleTo</c> into ProjectSystem (cf. the <c>ParseCompletions</c> /
/// <c>ShouldRequestSemanticTokens</c> precedent). General <c>ParseCompletions</c> coverage lives
/// in <c>Services/ParseCompletionsTests.cs</c>; only the <c>data</c>-field behavior is pinned here.
/// </para>
/// </summary>
[TestFixture]
public class ClientCompletionResolveTests
{
    private static JsonElement Json(string json) =>
        JsonSerializer.Deserialize<JsonElement>(json);

    // ------------------------------------------------------------------
    // ParseCompletions: the data field round-trips
    // ------------------------------------------------------------------

    /// <summary>
    /// The server's <c>data</c> is an OPAQUE token the client must echo back verbatim on
    /// resolve — so it is compared by exact re-serialization, never by spot-checked members.
    /// The parse happens inside a <c>using JsonDocument</c> that is DISPOSED before the
    /// asserts: without the <c>Clone()</c> at the parse seam, reading the element afterwards
    /// throws <c>ObjectDisposedException</c> — the clone is what this test pins.
    /// </summary>
    [Test]
    public void ParseCompletions_PreservesTheDataField()
    {
        const string dataJson = """{"type":"Console","member":"WriteLine","index":3}""";

        IReadOnlyList<CompletionItem> items;
        using (var doc = JsonDocument.Parse(
            $$"""[{"label":"WriteLine","kind":2,"data":{{dataJson}}}]"""))
        {
            items = LanguageService.ParseCompletions(doc.RootElement);
        }
        // The document is disposed HERE — Data must have been cloned out of it to survive.

        Assert.That(items, Has.Count.EqualTo(1));
        Assert.That(items[0].Data, Is.Not.Null, "the data field must be captured at parse");
        Assert.That(items[0].Data!.Value.GetRawText(), Is.EqualTo(dataJson),
            "data is the server's opaque token — it must survive parsing byte-for-byte");
    }

    [Test]
    public void ParseCompletions_NoData_LeavesDataNull()
    {
        var absent = LanguageService.ParseCompletions(Json("""[{"label":"x","kind":6}]"""));
        var jsonNull = LanguageService.ParseCompletions(Json("""[{"label":"x","kind":6,"data":null}]"""));

        Assert.Multiple(() =>
        {
            Assert.That(absent[0].Data, Is.Null, "no data on the wire = nothing to resolve with");
            Assert.That(jsonNull[0].Data, Is.Null,
                "a JSON-null data carries nothing to resolve with either — normalized to absent " +
                "so the ShouldResolve gate has exactly one 'no data' shape to check");
        });
    }

    // ------------------------------------------------------------------
    // The gate: a pure predicate (mirrors ShouldRequestSemanticTokens)
    // ------------------------------------------------------------------

    // The negative wire claim ("no request actually leaves for a provider-less server") is
    // untestable here without a scripted server — none exists in this suite. This predicate
    // plus its single call site inside ResolveCompletionAsync is the coverage, the same
    // argument CapabilityNegotiationTests makes for the semantic-tokens gate; the live clangd
    // test below adds the genuine-article half.
    [Test]
    public void ShouldResolve_IsAPurePredicate()
    {
        var withData = new CompletionItem { Label = "x", Data = Json("""{"k":1}""") };
        var noData = new CompletionItem { Label = "x" };
        var providerTrue = new ServerCapabilities { HasCompletionResolveProvider = true };
        var providerFalse = new ServerCapabilities { HasCompletionResolveProvider = false };

        Assert.Multiple(() =>
        {
            Assert.That(LanguageService.ShouldResolve(null, withData), Is.False,
                "null capabilities = never connected or disconnected — nothing to ask");
            Assert.That(LanguageService.ShouldResolve(null, noData), Is.False);
            Assert.That(LanguageService.ShouldResolve(providerFalse, withData), Is.False,
                "a server that advertised resolveProvider false must never be sent the request " +
                "— real clangd 22 is exactly this server (measured, Phase 3b Step 0.3)");
            Assert.That(LanguageService.ShouldResolve(providerFalse, noData), Is.False);
            Assert.That(LanguageService.ShouldResolve(providerTrue, noData), Is.False,
                "without a data token there is nothing for the server to resolve against — " +
                "the reply could only describe a different item");
            Assert.That(LanguageService.ShouldResolve(providerTrue, withData), Is.True,
                "advertised provider AND a data token is the one and only green light");
        });
    }

    // ------------------------------------------------------------------
    // The request builder: exact wire shape
    // ------------------------------------------------------------------

    [Test]
    public void BuildResolveParams_EmitsTheExactJson()
    {
        var item = new CompletionItem
        {
            Label = "WriteLine",
            Kind = CompletionItemKind.Method,
            Data = Json("""{"type":"Console","member":"WriteLine"}""")
        };

        // Serialized with a mirror of the wire's options (LanguageService.JsonOptions is
        // private): camelCase + omit-nulls. Exact full-string compare, never Does.Contain —
        // JsonElement serialization preserves the data object's property order, so the
        // round-trip is deterministic.
        var wireOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        var json = JsonSerializer.Serialize(LanguageService.BuildResolveParams(item), wireOptions);

        Assert.That(json, Is.EqualTo(
            """{"label":"WriteLine","kind":2,"data":{"type":"Console","member":"WriteLine"}}"""),
            "the resolve params are the item itself: label, kind (wire int), and the server's " +
            "data token echoed back VERBATIM");
    }

    // ------------------------------------------------------------------
    // The merge: a pure function over the parsed reply
    // ------------------------------------------------------------------

    [Test]
    public void Resolve_MergesDocumentationAndDetail_FromAReplyElement()
    {
        var original = new CompletionItem
        {
            Label = "WriteLine",
            Kind = CompletionItemKind.Method,
            InsertText = "WriteLine",
            SortText = "0005",
            FilterText = "writeline",
            Preselect = true,
            InsertTextFormat = InsertTextFormat.Snippet,
            Data = Json("""{"type":"Console","member":"WriteLine"}""")
        };

        // (a) Plain-string documentation + detail — both merged, everything else preserved.
        var enriched = LanguageService.MergeResolvedCompletion(original, Json(
            """{"label":"WriteLine","documentation":"Writes a line.","detail":"Sub WriteLine(value As String)"}"""));
        Assert.Multiple(() =>
        {
            Assert.That(enriched.Documentation, Is.EqualTo("Writes a line."));
            Assert.That(enriched.Detail, Is.EqualTo("Sub WriteLine(value As String)"));
            Assert.That(enriched.Label, Is.EqualTo("WriteLine"));
            Assert.That(enriched.Kind, Is.EqualTo(CompletionItemKind.Method));
            Assert.That(enriched.InsertText, Is.EqualTo("WriteLine"));
            Assert.That(enriched.SortText, Is.EqualTo("0005"));
            Assert.That(enriched.FilterText, Is.EqualTo("writeline"));
            Assert.That(enriched.Preselect, Is.True);
            Assert.That(enriched.InsertTextFormat, Is.EqualTo(InsertTextFormat.Snippet));
            Assert.That(enriched.Data!.Value.GetRawText(),
                Is.EqualTo("""{"type":"Console","member":"WriteLine"}"""),
                "the data token rides along — a second resolve of the enriched item must still work");
            Assert.That(original.Documentation, Is.Null,
                "the merge returns a NEW item; the original is never mutated (it may still be " +
                "bound to the visible completion list)");
        });

        // (b) MarkupContent documentation — the object form is real LSP (BasicLang sends it).
        var markup = LanguageService.MergeResolvedCompletion(original, Json(
            """{"documentation":{"kind":"markdown","value":"**Writes** a line."}}"""));
        Assert.That(markup.Documentation, Is.EqualTo("**Writes** a line."),
            "MarkupContent documentation must be unwrapped to its value");

        // (c) Documentation only — an existing Detail survives.
        var hadDetail = new CompletionItem { Label = "x", Detail = "kept" };
        var docOnly = LanguageService.MergeResolvedCompletion(hadDetail, Json(
            """{"documentation":"docs"}"""));
        Assert.Multiple(() =>
        {
            Assert.That(docOnly.Documentation, Is.EqualTo("docs"));
            Assert.That(docOnly.Detail, Is.EqualTo("kept"),
                "fields the reply does not carry keep their original values");
        });

        // (d) Nothing mergeable — the ORIGINAL instance comes back, not a copy. Undefined is
        // the shape ProcessMessage hands back for a result-less response; per the Phase 3a
        // landmine its GetRawText() throws InvalidOperationException (NOT JsonException), so
        // the ValueKind check must sit outside any JsonException-shaped try — DoesNotThrow
        // pins that.
        Assert.Multiple(() =>
        {
            Assert.That(LanguageService.MergeResolvedCompletion(original, Json("null")),
                Is.SameAs(original), "a null reply enriches nothing");
            Assert.That(LanguageService.MergeResolvedCompletion(original, Json("""{"label":"WriteLine"}""")),
                Is.SameAs(original), "a reply without documentation or detail enriches nothing");
            Assert.That(LanguageService.MergeResolvedCompletion(original,
                    Json("""{"documentation":{"kind":"markdown"}}""")),
                Is.SameAs(original), "a value-less MarkupContent enriches nothing");
            CompletionItem undefinedResult = null!;
            Assert.DoesNotThrow(() =>
                undefinedResult = LanguageService.MergeResolvedCompletion(original, default));
            Assert.That(undefinedResult, Is.SameAs(original),
                "a result-less response (Undefined element) must fall through to the original");
        });
    }

    // ------------------------------------------------------------------
    // Live: the real BasicLang --lsp server serves lazy docs through this client
    // ------------------------------------------------------------------

    /// <summary>
    /// The end-to-end beneficiary: BasicLang's real <c>--lsp</c> server advertises
    /// <c>resolveProvider: true</c> and stashes type+member in <c>data</c> for .NET member
    /// items (see the server-side <see cref="CompletionResolveTests"/>); this client must
    /// carry that token through parse → gate → request → merge and come back with the docs.
    /// Runs unconditionally, like every live BasicLang test: the compiler build is deployed
    /// beside the test assembly (same auto-probe <see cref="ClangdE2ETests"/> relies on).
    /// </summary>
    [Test]
    [NonParallelizable] // spawns a `dotnet BasicLang.dll --lsp` child process
    public async Task Resolve_AgainstTheRealBasicLangServer_FillsLazyDocs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bl-client-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        // The same source the server-side fixture proves resolve against: a member access on
        // Console. Line 2 col 13 (1-based) = wire (1, 12), directly after the dot.
        const string source = "Sub Main()\n    Console.\nEnd Sub\n";
        var file = Path.Combine(dir, "Program.bas");
        File.WriteAllText(file, source);

        var output = new RecordingOutput();
        var lsp = new LanguageService(output);
        try
        {
            using (var startCts = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            {
                await lsp.StartAsync(dir, startCts.Token);
            }
            Assert.That(lsp.IsConnected, Is.True,
                "the BasicLang --lsp server did not connect.\n" + output.Dump());
            Assert.That(lsp.Capabilities!.HasCompletionResolveProvider, Is.True,
                "premise: the real --lsp server advertises resolveProvider true — if this " +
                "flipped, the lazy-docs path below would be gated off and prove nothing");

            await lsp.OpenDocumentAsync(file, source);

            // The server analyzes asynchronously after didOpen — poll for the member item
            // WITH its data token (the wire-level proof that ParseCompletions preserves it
            // through a REAL reply, not just fixture JSON).
            CompletionItem? writeLine = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (writeLine == null && DateTime.UtcNow < deadline)
            {
                var completions = await lsp.GetCompletionsAsync(file, line: 2, column: 13);
                writeLine = completions.FirstOrDefault(c => c.Label == "WriteLine" && c.Data is not null);
                if (writeLine == null) await Task.Delay(300);
            }
            Assert.That(writeLine, Is.Not.Null,
                "no 'WriteLine' completion carrying a data token arrived within 30s — either " +
                "member completion broke or the data field was dropped at parse.\n" + output.Dump());

            var resolved = await lsp.ResolveCompletionAsync(writeLine!);

            Assert.That(resolved.Documentation, Is.Not.Null.And.Not.Empty,
                "resolve must fill the lazy documentation from the server.\n" + output.Dump());
            Assert.That(resolved.Documentation, Does.Contain("WriteLine"),
                $"the documentation should describe the member; got: '{resolved.Documentation}'");
            Assert.That(resolved.Label, Is.EqualTo("WriteLine"),
                "enrichment must not change the item's identity");
        }
        finally
        {
            try { await lsp.StopAsync(); } catch { }
            try { lsp.Dispose(); } catch { }
            KillIfAlive(lsp.ServerProcessId);
            DeleteDirWithRetry(dir);
        }
    }

    // ------------------------------------------------------------------
    // Live: real clangd advertises resolveProvider FALSE — the gate-off path,
    // tested against the genuine article
    // ------------------------------------------------------------------

    /// <summary>
    /// S0.3 measured: real clangd 22 advertises <c>resolveProvider: false</c>, so the clangd
    /// path must gate off and hand back the ORIGINAL item — same reference, no copy, and no
    /// request on the wire (pinned via the error log: an off-gate request would draw a
    /// method-not-supported error from clangd, which the catch-all logs).
    /// Skips when clangd is not installed, via the shared <see cref="ClangdTestDiscovery"/>.
    /// </summary>
    [Test]
    [NonParallelizable] // spawns a clangd child process
    [Category("RequiresClangd")]
    public async Task Resolve_AgainstRealClangd_GatesOffAndReturnsTheOriginal()
    {
        var clangdPath = ClangdTestDiscovery.LocateClangd();
        ClangdTestDiscovery.RequireClangd(
            clangdPath,
            "clangd is not installed on this machine (not under ~\\.vgs\\tools\\clangd*\\bin, " +
            "not on PATH). This test pins the resolve gate-off against a real clangd; install " +
            "clangd to run it.");

        var dir = Path.Combine(Path.GetTempPath(), "bl-clangd-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        File.WriteAllText(Path.Combine(dir, "obj", "compile_commands.json"), "[]");

        var output = new RecordingOutput();
        using var service = new LanguageService(
            output, null, LanguageServerDescriptor.Clangd(clangdPath!));
        try
        {
            await service.StartAsync(dir);
            Assert.That(service.IsConnected, Is.True,
                "clangd did not connect.\n" + output.Dump());
            Assert.That(service.Capabilities!.HasCompletionResolveProvider, Is.False,
                "S0.3 measured real clangd 22 advertising resolveProvider FALSE — if a future " +
                "clangd flips this, the gate opens and this test must be revisited deliberately, " +
                "not patched green");

            // Data present DELIBERATELY: the capability, not a missing token, must be what
            // gates the clangd path off.
            var item = new CompletionItem
            {
                Label = "vector",
                Kind = CompletionItemKind.Class,
                Data = Json("""{"opaque":"token"}""")
            };

            var resolved = await service.ResolveCompletionAsync(item);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.SameAs(item),
                    "the gate-off path must return the ORIGINAL item — same instance, no copy");
                Assert.That(output.Dump(), Does.Not.Contain("Completion resolve error"),
                    "no resolve request may reach a server that never advertised the provider — " +
                    "an off-gate request draws an error reply from clangd, which the catch-all " +
                    "logs under exactly this marker");
            });
        }
        finally
        {
            try { await service.StopAsync(); } catch { }
            KillIfAlive(service.ServerProcessId);
            DeleteDirWithRetry(dir);
        }
    }

    // ------------------------------------------------------------------
    // Helpers (per-fixture on purpose — the suite's established idiom)
    // ------------------------------------------------------------------

    private static void KillIfAlive(int? pid)
    {
        if (pid is not int id) return;
        try
        {
            using var process = Process.GetProcessById(id);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // already gone — which is the desired state
        }
    }

    private static void DeleteDirWithRetry(string dir)
    {
        // One bounded retry: a just-stopped server can briefly hold the directory open.
        try { Directory.Delete(dir, recursive: true); }
        catch
        {
            Thread.Sleep(250);
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Thread-safe recording IOutputService so failures show the real LSP traffic instead of
    /// a bare assertion message (the ClangdE2ETests idiom).
    /// </summary>
    private sealed class RecordingOutput : IOutputService
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public string Dump() => string.Join(Environment.NewLine, _lines);

        public void WriteLine(string message, OutputCategory category = OutputCategory.General) =>
            _lines.Enqueue(message);
        public void Write(string message, OutputCategory category = OutputCategory.General) =>
            _lines.Enqueue(message);
        public void WriteError(string message, OutputCategory category = OutputCategory.General) =>
            _lines.Enqueue("[ERR] " + message);
        public void Clear(OutputCategory category) { }
        public void ClearAll() { }
        public void Activate(OutputCategory category) { }
        public IReadOnlyList<string> GetMessages(OutputCategory category) => Array.Empty<string>();
        public event EventHandler<OutputEventArgs>? OutputReceived { add { } remove { } }
        public IOutputChannel CreateChannel(string name) => throw new NotSupportedException();
        public IOutputChannel? GetChannel(string name) => null;
        public IReadOnlyList<IOutputChannel> Channels => Array.Empty<IOutputChannel>();
        public IOutputChannel? ActiveChannel { get; set; }
        public event EventHandler<string>? ChannelCreated { add { } remove { } }
        public event EventHandler<IOutputChannel?>? ActiveChannelChanged { add { } remove { } }
        public void ShowOutput() { }
    }
}
