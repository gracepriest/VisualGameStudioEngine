using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Core.Utilities;

/// <summary>
/// Thread-safe per-file diagnostics store backing the Error List panel.
///
/// LSP publishDiagnostics is a per-document protocol: each notification carries
/// the complete set of diagnostics for ONE file, and an empty list means "this
/// file is now clean". Rendering each payload directly therefore wipes every
/// other file's errors — the aggregator instead keeps a uri -&gt; diagnostics map
/// and exposes a flattened, stably ordered snapshot of the union.
///
/// Build results live in a separate keyspace so a new build replaces only the
/// previous build's entries, and LSP + build diagnostics coexist instead of
/// clobbering each other.
/// </summary>
public class DiagnosticsAggregator
{
    private readonly object _lock = new();

    private readonly Dictionary<string, List<DiagnosticItem>> _lspDiagnostics =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, List<DiagnosticItem>> _buildDiagnostics =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the LSP diagnostics for a single file. An empty or null payload
    /// removes the file's entry (the LSP "file is now clean" signal). Items
    /// without a FilePath are stamped with <paramref name="filePath"/> so the
    /// Error List file column and double-click navigation work.
    /// </summary>
    public void SetFileDiagnostics(string filePath, IEnumerable<DiagnosticItem>? diagnostics)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var list = diagnostics?.ToList();
        lock (_lock)
        {
            if (list == null || list.Count == 0)
            {
                _lspDiagnostics.Remove(filePath);
            }
            else
            {
                StampFilePath(list, filePath);
                _lspDiagnostics[filePath] = list;
            }
        }
    }

    /// <summary>
    /// Replaces ALL build diagnostics with the given batch (a new build's
    /// results supersede the previous build's). LSP entries are untouched.
    /// Items without a FilePath (project-level errors) are kept under an
    /// empty key so they still appear in the snapshot.
    /// </summary>
    public void SetBuildDiagnostics(IEnumerable<DiagnosticItem>? diagnostics)
    {
        var list = diagnostics?.ToList() ?? new List<DiagnosticItem>();
        lock (_lock)
        {
            _buildDiagnostics.Clear();
            foreach (var group in list.GroupBy(d => d.FilePath ?? "", StringComparer.OrdinalIgnoreCase))
            {
                _buildDiagnostics[group.Key] = group.ToList();
            }
        }
    }

    /// <summary>Removes all LSP and build diagnostics.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lspDiagnostics.Clear();
            _buildDiagnostics.Clear();
        }
    }

    /// <summary>
    /// Flattened union of all LSP and build diagnostics, ordered by file path
    /// (case-insensitive), then line, then column. LINQ OrderBy is stable, so
    /// equal keys keep their publish order.
    /// </summary>
    public IReadOnlyList<DiagnosticItem> GetSnapshot()
    {
        lock (_lock)
        {
            return _lspDiagnostics.Values
                .Concat(_buildDiagnostics.Values)
                .SelectMany(items => items)
                .OrderBy(d => d.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Line)
                .ThenBy(d => d.Column)
                .ToList();
        }
    }

    private static void StampFilePath(List<DiagnosticItem> items, string filePath)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.FilePath))
            {
                item.FilePath = filePath;
            }
        }
    }
}
