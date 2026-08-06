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

    // Keyed by (collection, file): one extension's publish must not disturb another's, and neither
    // must disturb the LSP's. File comparison stays case-insensitive to match the other keyspaces.
    private readonly Dictionary<(string Collection, string FilePath), List<DiagnosticItem>> _extensionDiagnostics =
        new(ValueTupleComparer.OrdinalIgnoreCaseOnFilePath);

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

    /// <summary>
    /// Replaces one extension DiagnosticCollection's diagnostics for a single file. An empty or
    /// null payload removes that pair — VS Code's "this collection is clean for this file" signal.
    /// </summary>
    /// <remarks>
    /// Keyed by (collection, file) rather than file, because several extensions reporting on the
    /// same file is the ordinary case: ESLint and a spell checker both flagging app.js. Keying by
    /// file alone would make each publish erase the previous extension's findings, and sharing the
    /// LSP keyspace would let any LSP publish erase all of them at once. Build diagnostics already
    /// set the precedent of a separate keyspace for the same reason.
    /// </remarks>
    public void SetExtensionDiagnostics(string collection, string filePath, IEnumerable<DiagnosticItem>? diagnostics)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var key = (collection ?? "", filePath);
        var list = diagnostics?.ToList();

        lock (_lock)
        {
            if (list == null || list.Count == 0)
            {
                _extensionDiagnostics.Remove(key);
            }
            else
            {
                StampFilePath(list, filePath);
                _extensionDiagnostics[key] = list;
            }
        }
    }

    /// <summary>Removes all LSP, build and extension diagnostics.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lspDiagnostics.Clear();
            _buildDiagnostics.Clear();
            _extensionDiagnostics.Clear();
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
                .Concat(_extensionDiagnostics.Values)
                .SelectMany(items => items)
                .OrderBy(d => d.FilePath ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d.Line)
                .ThenBy(d => d.Column)
                .ToList();
        }
    }

    /// <summary>
    /// Compares (collection, file) keys, matching the file part case-insensitively so the extension
    /// keyspace agrees with the LSP and build ones about what counts as the same file. The
    /// collection name stays ordinal — it is an extension-chosen identifier, not a path.
    /// </summary>
    private sealed class ValueTupleComparer : IEqualityComparer<(string Collection, string FilePath)>
    {
        public static readonly ValueTupleComparer OrdinalIgnoreCaseOnFilePath = new();

        public bool Equals((string Collection, string FilePath) x, (string Collection, string FilePath) y) =>
            string.Equals(x.Collection, y.Collection, StringComparison.Ordinal)
            && string.Equals(x.FilePath, y.FilePath, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Collection, string FilePath) obj) =>
            HashCode.Combine(
                obj.Collection,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FilePath ?? ""));
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
