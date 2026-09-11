using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// <c>workspace.findFiles</c> and <c>workspace.openTextDocument</c> — the next two requests after the
/// fs family, and the two an extension reaches for once it can read files at all.
///
/// <para>⛔ Both are REQUESTS, so an absent handler rejects into the extension rather than being a
/// silent no-op. See ExtensionHostRequestCoverageTests for why that split decides priority.</para>
/// </summary>
[TestFixture]
public class ExtensionWorkspaceTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "vgs-extws", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { Thread.Sleep(250); try { Directory.Delete(_root, recursive: true); } catch { } }
    }

    private void Make(string relative, string content = "x")
    {
        var full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>Results come back as file:// URIs; compare on the trailing relative part.</summary>
    private static string[] Names(System.Collections.Generic.List<string> uris) =>
        uris.Select(u => u.Substring(u.LastIndexOf('/') + 1)).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    // ------------------------------------------------------------------ glob semantics

    /// <summary>
    /// ⛔ THE CLASSIC GLOB BUG: <c>**/</c> must match ZERO segments, so <c>**\/*.json</c> has to find
    /// a file at the workspace ROOT as well as nested ones. Nearly every hand-rolled glob gets this
    /// wrong, and the symptom — an extension that works only for nested files — looks like a
    /// configuration problem rather than a matcher problem.
    /// </summary>
    [Test]
    public void FindFiles_DoubleStarMatchesZeroSegments()
    {
        Make("root.json");
        Make("sub/nested.json");
        Make("sub/deep/deeper.json");
        Make("ignored.txt");

        var found = ExtensionWorkspace.FindFiles(_root, "**/*.json", exclude: null, maxResults: null);

        Assert.That(Names(found), Is.EqualTo(new[] { "deeper.json", "nested.json", "root.json" }));
    }

    /// <summary>A single star must NOT cross a directory boundary.</summary>
    [Test]
    public void FindFiles_SingleStarStaysWithinOneSegment()
    {
        Make("root.json");
        Make("sub/nested.json");

        var found = ExtensionWorkspace.FindFiles(_root, "*.json", exclude: null, maxResults: null);

        Assert.That(Names(found), Is.EqualTo(new[] { "root.json" }),
            "*.json must not reach into sub/");
    }

    [Test]
    public void FindFiles_MatchesADirectoryPrefix()
    {
        Make("src/a.ts");
        Make("src/deep/b.ts");
        Make("other/c.ts");

        var found = ExtensionWorkspace.FindFiles(_root, "src/**", exclude: null, maxResults: null);

        Assert.That(Names(found), Is.EqualTo(new[] { "a.ts", "b.ts" }));
    }

    /// <summary>Brace alternation is common in real extension globs (<c>{**/*.ts,**/*.js}</c>).</summary>
    [Test]
    public void FindFiles_SupportsBraceAlternation()
    {
        Make("a.ts");
        Make("b.js");
        Make("c.css");

        var found = ExtensionWorkspace.FindFiles(_root, "{**/*.ts,**/*.js}", exclude: null, maxResults: null);

        Assert.That(Names(found), Is.EqualTo(new[] { "a.ts", "b.js" }));
    }

    /// <summary>
    /// ⛔ A regex metacharacter in the literal part of a glob must be ESCAPED, not interpreted.
    /// A file named <c>a.b.json</c> and a pattern <c>a.b.json</c> must match by literal dots —
    /// if the dots stayed regex-active, <c>axbxjson</c> would match too.
    /// </summary>
    [Test]
    public void FindFiles_TreatsDotsAsLiteral()
    {
        Make("a.b.json");
        Make("axbxjson");

        var found = ExtensionWorkspace.FindFiles(_root, "a.b.json", exclude: null, maxResults: null);

        Assert.That(Names(found), Is.EqualTo(new[] { "a.b.json" }));
    }

    // ------------------------------------------------------------------ exclude / maxResults

    [Test]
    public void FindFiles_AppliesTheExcludePattern()
    {
        Make("keep.ts");
        Make("node_modules/skip.ts");

        var found = ExtensionWorkspace.FindFiles(_root, "**/*.ts", exclude: "**/node_modules/**", maxResults: null);

        Assert.That(Names(found), Is.EqualTo(new[] { "keep.ts" }));
    }

    [Test]
    public void FindFiles_CapsAtMaxResults()
    {
        for (var i = 0; i < 10; i++) Make($"f{i}.ts");

        var found = ExtensionWorkspace.FindFiles(_root, "**/*.ts", exclude: null, maxResults: 3);

        Assert.That(found, Has.Count.EqualTo(3));
    }

    /// <summary>
    /// A maxResults of 0 means "no limit" in VS Code's API surface as commonly used — but an
    /// explicit 0 is indistinguishable from an omitted value once it crosses JSON, so the handler
    /// treats null as unlimited and a positive number as a cap. Pinned so the distinction is not
    /// quietly changed.
    /// </summary>
    [Test]
    public void FindFiles_TreatsNullMaxResultsAsUnlimited()
    {
        for (var i = 0; i < 10; i++) Make($"g{i}.ts");

        var found = ExtensionWorkspace.FindFiles(_root, "**/*.ts", exclude: null, maxResults: null);

        Assert.That(found, Has.Count.EqualTo(10));
    }

    // ------------------------------------------------------------------ result shape

    /// <summary>
    /// Results must be file:// URIs, because that is what every other uri on this wire is — the
    /// document keys, the diagnostics targets, the provider selectors. A bare Windows path here
    /// would not match a document the extension had already seen.
    /// </summary>
    [Test]
    public void FindFiles_ReturnsFileUris()
    {
        Make("h.ts");

        var found = ExtensionWorkspace.FindFiles(_root, "**/*.ts", exclude: null, maxResults: null);

        Assert.That(found, Has.Count.EqualTo(1));
        Assert.That(found[0], Does.StartWith("file:///"));
        Assert.That(found[0], Does.Not.Contain("\\"), "a URI uses forward slashes");
    }

    /// <summary>A missing workspace root is an empty result, not a crash — the IDE may have no folder open.</summary>
    [Test]
    public void FindFiles_ReturnsEmptyWhenTheRootDoesNotExist()
    {
        var found = ExtensionWorkspace.FindFiles(
            Path.Combine(_root, "no-such-root"), "**/*", exclude: null, maxResults: null);

        Assert.That(found, Is.Empty);
    }

    /// <summary>No workspace open at all — null root — must also be empty rather than throwing.</summary>
    [Test]
    public void FindFiles_ReturnsEmptyWhenThereIsNoWorkspace()
    {
        Assert.That(ExtensionWorkspace.FindFiles(null, "**/*", exclude: null, maxResults: null), Is.Empty);
    }

    // ------------------------------------------------------------------ openTextDocument

    /// <summary>
    /// The file form: <c>{ uri }</c>. The document must carry the text so the JS side can build a
    /// real TextDocument from it rather than handing the extension a bare JSON object.
    /// </summary>
    [Test]
    public void OpenTextDocument_ReadsAFileByUri()
    {
        Make("i.bas", "Print 1");
        var uri = ExtensionHost.ToDocumentUri(Path.Combine(_root, "i.bas"));

        var doc = ExtensionWorkspace.OpenTextDocument(uri, content: null, language: null, isVirtual: false);

        Assert.That(doc.Text, Is.EqualTo("Print 1"));
        Assert.That(doc.Uri, Is.EqualTo(uri));
        Assert.That(doc.Version, Is.EqualTo(1));
    }

    /// <summary>The languageId is derived from the extension when the caller does not state one.</summary>
    [Test]
    public void OpenTextDocument_DerivesTheLanguageFromTheFileExtension()
    {
        Make("j.bas", "");
        Make("k.json", "");

        var bas = ExtensionWorkspace.OpenTextDocument(
            ExtensionHost.ToDocumentUri(Path.Combine(_root, "j.bas")), null, null, false);
        var json = ExtensionWorkspace.OpenTextDocument(
            ExtensionHost.ToDocumentUri(Path.Combine(_root, "k.json")), null, null, false);

        Assert.That(bas.LanguageId, Is.EqualTo("basiclang"));
        Assert.That(json.LanguageId, Is.EqualTo("json"));
    }

    /// <summary>
    /// ⛔ THE SECOND SHAPE. workspace.js sends <c>{content, language, isVirtual}</c> with NO uri when
    /// the extension asks for an untitled document. One handler has to accept both shapes, because
    /// StreamJsonRpc rejects unknown keys and would fail the whole call otherwise.
    /// </summary>
    [Test]
    public void OpenTextDocument_CreatesAVirtualDocumentWithNoUri()
    {
        var doc = ExtensionWorkspace.OpenTextDocument(
            uri: null, content: "virtual body", language: "plaintext", isVirtual: true);

        Assert.That(doc.Text, Is.EqualTo("virtual body"));
        Assert.That(doc.LanguageId, Is.EqualTo("plaintext"));
        Assert.That(doc.Uri, Does.StartWith("untitled:"),
            "a virtual document is untitled: — it has no file backing it");
    }

    /// <summary>Two virtual documents must not collide on one untitled uri.</summary>
    [Test]
    public void OpenTextDocument_GivesEachVirtualDocumentItsOwnUri()
    {
        var a = ExtensionWorkspace.OpenTextDocument(null, "a", "plaintext", true);
        var b = ExtensionWorkspace.OpenTextDocument(null, "b", "plaintext", true);

        Assert.That(a.Uri, Is.Not.EqualTo(b.Uri));
    }

    /// <summary>A missing file must fail rather than answer an empty document.</summary>
    [Test]
    public void OpenTextDocument_ThrowsForAMissingFile()
    {
        var uri = ExtensionHost.ToDocumentUri(Path.Combine(_root, "absent.bas"));

        Assert.That(() => ExtensionWorkspace.OpenTextDocument(uri, null, null, false),
            Throws.TypeOf<FileNotFoundException>());
    }
}
