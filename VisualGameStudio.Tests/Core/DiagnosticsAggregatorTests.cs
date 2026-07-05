using NUnit.Framework;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Core.Utilities;

namespace VisualGameStudio.Tests.Core;

/// <summary>
/// Tests for the per-file diagnostics aggregator that backs the Error List.
/// Regression coverage for the bug where every LSP publishDiagnostics payload
/// (one file's worth) replaced the whole Error List, so the last file to
/// publish (typically a clean file with an empty list) wiped all errors.
/// </summary>
[TestFixture]
public class DiagnosticsAggregatorTests
{
    private static DiagnosticItem Diag(string message, string? filePath = null, int line = 1,
        int column = 1, DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        return new DiagnosticItem
        {
            Message = message,
            FilePath = filePath,
            Line = line,
            Column = column,
            Severity = severity
        };
    }

    // ---- LSP per-file updates -------------------------------------------

    [Test]
    public void SetFileDiagnostics_SingleFile_SnapshotContainsThem()
    {
        var aggregator = new DiagnosticsAggregator();

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("e1"), Diag("e2") });

        var snapshot = aggregator.GetSnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(2));
        Assert.That(snapshot.Select(d => d.Message), Is.EquivalentTo(new[] { "e1", "e2" }));
    }

    [Test]
    public void SetFileDiagnostics_SecondFile_DoesNotWipeFirstFile()
    {
        // The original bug: publishing file B replaced file A's errors.
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("errorA") });

        aggregator.SetFileDiagnostics(@"C:\Proj\B.bas", new[] { Diag("errorB") });

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();
        Assert.That(messages, Is.EquivalentTo(new[] { "errorA", "errorB" }));
    }

    [Test]
    public void SetFileDiagnostics_CleanFilePublish_DoesNotWipeOtherFiles()
    {
        // The exact failure mode: a clean file publishes an empty list last
        // (e.g. reconnect re-sends didOpen for every open doc).
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\Broken.bas", new[] { Diag("kept") });

        aggregator.SetFileDiagnostics(@"C:\Proj\Clean.bas", Array.Empty<DiagnosticItem>());

        var snapshot = aggregator.GetSnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot[0].Message, Is.EqualTo("kept"));
    }

    [Test]
    public void SetFileDiagnostics_Republish_ReplacesOnlyThatFile()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("old1"), Diag("old2") });
        aggregator.SetFileDiagnostics(@"C:\Proj\B.bas", new[] { Diag("other") });

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("new1") });

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();
        Assert.That(messages, Is.EquivalentTo(new[] { "new1", "other" }));
    }

    [Test]
    public void SetFileDiagnostics_EmptyPayload_RemovesFileEntry()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("e1") });

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", Array.Empty<DiagnosticItem>());

        Assert.That(aggregator.GetSnapshot(), Is.Empty);
    }

    [Test]
    public void SetFileDiagnostics_NullPayload_RemovesFileEntry()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("e1") });

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", null);

        Assert.That(aggregator.GetSnapshot(), Is.Empty);
    }

    [Test]
    public void SetFileDiagnostics_EmptyPayloadForUnknownFile_IsNoOp()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("e1") });

        aggregator.SetFileDiagnostics(@"C:\Proj\NeverSeen.bas", Array.Empty<DiagnosticItem>());

        Assert.That(aggregator.GetSnapshot(), Has.Count.EqualTo(1));
    }

    [Test]
    public void SetFileDiagnostics_FileKeysAreCaseInsensitive()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("old") });

        aggregator.SetFileDiagnostics(@"c:\proj\a.BAS", new[] { Diag("new") });

        var snapshot = aggregator.GetSnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot[0].Message, Is.EqualTo("new"));
    }

    [Test]
    public void SetFileDiagnostics_NullOrEmptyFilePath_IsIgnored()
    {
        var aggregator = new DiagnosticsAggregator();

        aggregator.SetFileDiagnostics("", new[] { Diag("e1") });
        aggregator.SetFileDiagnostics(null!, new[] { Diag("e2") });

        Assert.That(aggregator.GetSnapshot(), Is.Empty);
    }

    // ---- FilePath stamping ----------------------------------------------

    [Test]
    public void SetFileDiagnostics_StampsFilePathOnItemsMissingIt()
    {
        var aggregator = new DiagnosticsAggregator();

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("e1", filePath: null) });

        Assert.That(aggregator.GetSnapshot()[0].FilePath, Is.EqualTo(@"C:\Proj\A.bas"));
    }

    [Test]
    public void SetFileDiagnostics_DoesNotOverwriteExistingFilePath()
    {
        var aggregator = new DiagnosticsAggregator();

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("e1", filePath: @"C:\Proj\Other.bas") });

        Assert.That(aggregator.GetSnapshot()[0].FilePath, Is.EqualTo(@"C:\Proj\Other.bas"));
    }

    // ---- Ordering --------------------------------------------------------

    [Test]
    public void GetSnapshot_OrdersByFileThenLineThenColumn()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\Zebra.bas", new[] { Diag("z1", line: 5) });
        aggregator.SetFileDiagnostics(@"C:\Proj\Alpha.bas", new[]
        {
            Diag("a-late", line: 10, column: 2),
            Diag("a-early", line: 2),
            Diag("a-late-col1", line: 10, column: 1)
        });

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();

        Assert.That(messages, Is.EqualTo(new[] { "a-early", "a-late-col1", "a-late", "z1" }));
    }

    // ---- Build diagnostics coexistence ------------------------------------

    [Test]
    public void SetBuildDiagnostics_CoexistsWithLspDiagnostics()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });

        aggregator.SetBuildDiagnostics(new[] { Diag("build", filePath: @"C:\Proj\B.bas") });

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();
        Assert.That(messages, Is.EquivalentTo(new[] { "lsp", "build" }));
    }

    [Test]
    public void SetBuildDiagnostics_SameFileAsLsp_BothArePresent()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });

        aggregator.SetBuildDiagnostics(new[] { Diag("build", filePath: @"C:\Proj\A.bas") });

        Assert.That(aggregator.GetSnapshot(), Has.Count.EqualTo(2));
    }

    [Test]
    public void SetBuildDiagnostics_NewBuild_ClearsPreviousBuildEntriesOnly()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });
        aggregator.SetBuildDiagnostics(new[] { Diag("build-old", filePath: @"C:\Proj\B.bas") });

        aggregator.SetBuildDiagnostics(new[] { Diag("build-new", filePath: @"C:\Proj\C.bas") });

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();
        Assert.That(messages, Is.EquivalentTo(new[] { "lsp", "build-new" }));
    }

    [Test]
    public void SetBuildDiagnostics_SuccessfulBuildEmptyList_ClearsBuildKeepsLsp()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });
        aggregator.SetBuildDiagnostics(new[] { Diag("build", filePath: @"C:\Proj\B.bas") });

        aggregator.SetBuildDiagnostics(Array.Empty<DiagnosticItem>());

        var snapshot = aggregator.GetSnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot[0].Message, Is.EqualTo("lsp"));
    }

    [Test]
    public void SetBuildDiagnostics_NullList_ClearsBuildKeepsLsp()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });
        aggregator.SetBuildDiagnostics(new[] { Diag("build", filePath: @"C:\Proj\B.bas") });

        aggregator.SetBuildDiagnostics(null);

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();
        Assert.That(messages, Is.EqualTo(new[] { "lsp" }));
    }

    [Test]
    public void SetBuildDiagnostics_ItemsWithoutFilePath_AreStillIncluded()
    {
        // Project-level build errors (bad .blproj etc.) have no file.
        var aggregator = new DiagnosticsAggregator();

        aggregator.SetBuildDiagnostics(new[] { Diag("project-level", filePath: null) });

        var snapshot = aggregator.GetSnapshot();
        Assert.That(snapshot, Has.Count.EqualTo(1));
        Assert.That(snapshot[0].Message, Is.EqualTo("project-level"));
    }

    [Test]
    public void SetFileDiagnostics_DoesNotDisturbBuildEntriesForOtherFiles()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetBuildDiagnostics(new[] { Diag("build", filePath: @"C:\Proj\B.bas") });

        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", Array.Empty<DiagnosticItem>());

        var messages = aggregator.GetSnapshot().Select(d => d.Message).ToList();
        Assert.That(messages, Is.EqualTo(new[] { "build" }));
    }

    // ---- Clear -------------------------------------------------------------

    [Test]
    public void Clear_RemovesLspAndBuildEntries()
    {
        var aggregator = new DiagnosticsAggregator();
        aggregator.SetFileDiagnostics(@"C:\Proj\A.bas", new[] { Diag("lsp") });
        aggregator.SetBuildDiagnostics(new[] { Diag("build", filePath: @"C:\Proj\B.bas") });

        aggregator.Clear();

        Assert.That(aggregator.GetSnapshot(), Is.Empty);
    }

    // ---- Thread safety -----------------------------------------------------

    [Test]
    public void ConcurrentUpdates_DoNotThrow_AndAllFilesLand()
    {
        var aggregator = new DiagnosticsAggregator();
        const int files = 32;
        const int iterations = 50;

        Parallel.For(0, files, i =>
        {
            var path = $@"C:\Proj\File{i}.bas";
            for (int n = 0; n < iterations; n++)
            {
                aggregator.SetFileDiagnostics(path, new[] { Diag($"e{i}", path, line: n + 1) });
                aggregator.GetSnapshot();
            }
            if (i % 2 == 0)
            {
                aggregator.SetBuildDiagnostics(new[] { Diag($"b{i}", path) });
            }
        });

        var snapshot = aggregator.GetSnapshot();
        // Every file publishes one final LSP diagnostic; exactly one build batch survives.
        var lspCount = snapshot.Count(d => d.Message.StartsWith("e"));
        var buildCount = snapshot.Count(d => d.Message.StartsWith("b"));
        Assert.That(lspCount, Is.EqualTo(files));
        Assert.That(buildCount, Is.EqualTo(1));
    }
}
