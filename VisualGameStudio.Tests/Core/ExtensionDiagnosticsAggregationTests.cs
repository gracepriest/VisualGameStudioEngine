using System.Linq;
using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

/// <summary>
/// Extension diagnostics need a keyspace of their own.
///
/// <para>In VS Code every extension owns one or more NAMED DiagnosticCollections, and several
/// extensions routinely report on the same file — ESLint and a spell checker both flagging
/// <c>app.js</c> is ordinary. Storing them in the LSP keyspace would make each publish replace the
/// file's whole entry, so the last writer would erase every other extension's findings, and any
/// LSP publish for that file would erase all of them at once.</para>
///
/// <para>So the key is (collection, file), not file. Build diagnostics already established the
/// precedent of a separate keyspace for exactly this reason.</para>
/// </summary>
[TestFixture]
public class ExtensionDiagnosticsAggregationTests
{
    private static DiagnosticItem Diag(string message, string file, int line = 1) => new()
    {
        Message = message,
        FilePath = file,
        Line = line,
        Column = 1,
        Severity = DiagnosticSeverity.Warning
    };

    [Test]
    public void ExtensionDiagnosticsAppearInTheSnapshot()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("no-unused-vars", @"C:\p\a.js") });

        Assert.That(aggregator.GetSnapshot().Select(d => d.Message), Is.EquivalentTo(new[] { "no-unused-vars" }));
    }

    /// <summary>Two extensions on one file must coexist — this is the ordinary case, not an edge case.</summary>
    [Test]
    public void TwoCollectionsOnTheSameFileCoexist()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("no-unused-vars", @"C:\p\a.js") });
        aggregator.SetExtensionDiagnostics("spell", @"C:\p\a.js", new[] { Diag("typo", @"C:\p\a.js") });

        Assert.That(aggregator.GetSnapshot().Select(d => d.Message),
            Is.EquivalentTo(new[] { "no-unused-vars", "typo" }),
            "keying by file alone would let the second extension erase the first");
    }

    /// <summary>An LSP publish must not disturb extension findings for the same file, or vice versa.</summary>
    [Test]
    public void ExtensionAndLspDiagnosticsCoexistOnOneFile()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("no-unused-vars", @"C:\p\a.js") });
        aggregator.SetFileDiagnostics(@"C:\p\a.js", new[] { Diag("lsp says hello", @"C:\p\a.js") });

        Assert.That(aggregator.GetSnapshot(), Has.Count.EqualTo(2));
    }

    /// <summary>
    /// An empty publish is VS Code's "this collection is now clean" signal — it must clear that
    /// collection's entry for the file and leave every other collection alone.
    /// </summary>
    [Test]
    public void AnEmptyPublishClearsOnlyThatCollection()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("no-unused-vars", @"C:\p\a.js") });
        aggregator.SetExtensionDiagnostics("spell", @"C:\p\a.js", new[] { Diag("typo", @"C:\p\a.js") });

        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", System.Array.Empty<DiagnosticItem>());

        Assert.That(aggregator.GetSnapshot().Select(d => d.Message), Is.EquivalentTo(new[] { "typo" }));
    }

    /// <summary>Republishing replaces rather than appends, or every keystroke would pile up.</summary>
    [Test]
    public void RepublishingReplacesTheCollectionsEntry()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("first", @"C:\p\a.js") });
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("second", @"C:\p\a.js") });

        Assert.That(aggregator.GetSnapshot().Select(d => d.Message), Is.EquivalentTo(new[] { "second" }));
    }

    /// <summary>Clear() is the project-close reset and must take extension findings with it.</summary>
    [Test]
    public void ClearRemovesExtensionDiagnosticsToo()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetExtensionDiagnostics("eslint", @"C:\p\a.js", new[] { Diag("no-unused-vars", @"C:\p\a.js") });

        aggregator.Clear();

        Assert.That(aggregator.GetSnapshot(), Is.Empty,
            "a stale extension diagnostic outliving its project points at a file that may not even "
            + "be open any more");
    }
}
