using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

/// <summary>
/// The IDE identifies documents by RAW WINDOWS PATH; the extension host identifies them by URI.
/// Nothing reconciled the two, so a document stored under one key was looked up under the other and
/// never found — a permanent miss on every provider request.
///
/// <para>The store path: <c>main.js</c> parses the incoming string with <c>utils/uri</c>, whose
/// scheme regex is lazy, so <c>C:\proj\a.js</c> yields scheme <c>"C"</c>; the backslash
/// normalisation is gated on <c>scheme === 'file'</c> and never runs; the key ends up
/// percent-encoded as <c>C:%5Cproj%5Ca.js</c>. The lookup path passes the raw string straight
/// through. The two can never meet.</para>
///
/// <para>Converting once, on the way out of the IDE, also makes <c>{ scheme: 'file' }</c> selectors
/// score — with a raw Windows path the derived scheme is <c>"C"</c>, so the most common VS Code
/// selector shape matches nothing.</para>
///
/// <para>⚠ This changes every uri string every extension sees. That is a correction toward the real
/// VS Code contract — extensions expect <c>file:///</c> URIs, not Windows paths — but it is a
/// behaviour change, not a pure fix.</para>
/// </summary>
[TestFixture]
public class ExtensionHostDocumentUriTests
{
    private static string HostSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "VisualGameStudio.ProjectSystem", "Services", "ExtensionHost.cs");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        Assert.Fail("could not locate ExtensionHost.cs");
        return "";
    }

    [Test]
    public void AWindowsPathBecomesAFileUri()
    {
        Assert.That(ExtensionHost.ToDocumentUri(@"C:\proj\a.js"),
            Is.EqualTo("file:///C:/proj/a.js").IgnoreCase,
            "the host keys documents by URI; a raw Windows path is stored under a "
            + "percent-encoded key that no lookup can reproduce");
    }

    /// <summary>Converting twice must not corrupt an already-correct value.</summary>
    [Test]
    public void AnExistingFileUriIsLeftAlone()
    {
        const string uri = "file:///C:/proj/a.js";
        Assert.That(ExtensionHost.ToDocumentUri(uri), Is.EqualTo(uri));
    }

    /// <summary>
    /// VS Code uses non-file schemes for unsaved buffers and virtual documents. Rewriting those
    /// would break them, so anything already carrying a scheme passes through untouched.
    /// </summary>
    [TestCase("untitled:Untitled-1")]
    [TestCase("vscode-userdata:/settings.json")]
    [TestCase("https://example.com/a.js")]
    public void NonFileSchemesArePreserved(string uri)
    {
        Assert.That(ExtensionHost.ToDocumentUri(uri), Is.EqualTo(uri));
    }

    /// <summary>A malformed path must degrade to itself, never throw on the notification path.</summary>
    [TestCase("")]
    [TestCase("   ")]
    public void GarbageDegradesInsteadOfThrowing(string input)
    {
        Assert.That(() => ExtensionHost.ToDocumentUri(input), Throws.Nothing);
    }

    /// <summary>
    /// The guard that keeps this fixed. Every outbound payload carrying a document identity must
    /// route through the converter — one wrapper that forgets is one silently dead provider, and
    /// there are eighteen such sites.
    /// </summary>
    [Test]
    public void EveryOutboundUriIsConverted()
    {
        var offenders = HostSource()
            .Split('\n')
            .Select((line, i) => (line: line.Trim(), no: i + 1))
            .Where(x => x.line.Contains("new { uri") || x.line.Contains(", uri,") || x.line.Contains(", uri }"))
            .Where(x => !x.line.Contains("ToDocumentUri"))
            .Select(x => $"  line {x.no}: {x.line}")
            .ToList();

        Assert.That(offenders, Is.Empty,
            "these sites send a document identity to the host without normalising it, so the host "
            + "stores or looks it up under a key the other side cannot produce:\n"
            + string.Join("\n", offenders));
    }
}
