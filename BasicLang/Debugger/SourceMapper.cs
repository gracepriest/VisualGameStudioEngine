using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace BasicLang.Debugger;

/// <summary>
/// Reads portable PDB files to map between .bas source lines and IL offsets.
/// Used for setting breakpoints and reading stack traces.
/// </summary>
public sealed class SourceMapper : IDisposable
{
    private MetadataReaderProvider? _pdbProvider;
    private MetadataReader? _pdbReader;

    // Keyed by normalized file path (lower-case on case-insensitive file systems)
    private readonly Dictionary<string, List<SequencePointEntry>> _sequencePointsByFile =
        new(StringComparer.OrdinalIgnoreCase);

    // Keyed by method token (raw int32 from MetadataToken)
    private readonly Dictionary<int, List<SequencePointEntry>> _sequencePointsByMethod = new();

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Loads a portable PDB file and indexes all sequence points.
    /// Returns <c>true</c> on success, <c>false</c> if the file does not exist or cannot be read.
    /// </summary>
    public bool LoadPdb(string pdbPath)
    {
        if (!File.Exists(pdbPath))
            return false;

        try
        {
            var stream = File.OpenRead(pdbPath);
            _pdbProvider = MetadataReaderProvider.FromPortablePdbStream(stream);
            _pdbReader = _pdbProvider.GetMetadataReader();

            IndexSequencePoints();
            return true;
        }
        catch
        {
            _pdbProvider?.Dispose();
            _pdbProvider = null;
            _pdbReader = null;
            return false;
        }
    }

    /// <summary>
    /// Returns the IL offset within the given method for the closest sequence point
    /// at or after <paramref name="line"/> in <paramref name="basFilePath"/>.
    /// Returns <c>null</c> if no matching sequence point is found.
    /// </summary>
    public (int methodToken, int ilOffset)? GetILOffsetForLine(string basFilePath, int line)
    {
        if (!_sequencePointsByFile.TryGetValue(basFilePath, out var entries))
            return null;

        SequencePointEntry? best = null;
        foreach (var entry in entries)
        {
            if (entry.StartLine < line)
                continue;
            if (best == null || entry.StartLine < best.StartLine ||
                (entry.StartLine == best.StartLine && entry.ILOffset < best.ILOffset))
            {
                best = entry;
            }
        }

        return best is null ? null : (best.MethodToken, best.ILOffset);
    }

    /// <summary>
    /// Returns the source location (file, line, column) for a given method token and IL offset.
    /// Finds the sequence point whose IL offset is closest to (but not exceeding) the given offset.
    /// Returns <c>null</c> if no match is found.
    /// </summary>
    public (string file, int line, int column)? GetSourceLocation(int methodToken, int ilOffset)
    {
        if (!_sequencePointsByMethod.TryGetValue(methodToken, out var entries))
            return null;

        SequencePointEntry? best = null;
        foreach (var entry in entries)
        {
            if (entry.ILOffset > ilOffset)
                continue;
            if (best == null || entry.ILOffset > best.ILOffset)
                best = entry;
        }

        return best is null ? null : (best.FilePath, best.StartLine, best.StartColumn);
    }

    /// <summary>
    /// Returns the nearest executable source line at or after the requested line.
    /// Falls back to returning <paramref name="line"/> itself when no PDB data is loaded
    /// or no sequence point is found.
    /// </summary>
    public int FindNearestExecutableLine(string basFilePath, int line)
    {
        if (!_sequencePointsByFile.TryGetValue(basFilePath, out var entries))
            return line;

        int? nearest = null;
        foreach (var entry in entries)
        {
            if (entry.StartLine < line)
                continue;
            if (nearest == null || entry.StartLine < nearest.Value)
                nearest = entry.StartLine;
        }

        return nearest ?? line;
    }

    /// <summary>
    /// Returns all source file paths referenced in the loaded PDB.
    /// </summary>
    public IReadOnlyList<string> GetSourceDocuments()
    {
        return _sequencePointsByFile.Keys.ToList();
    }

    /// <summary>
    /// Returns the IL byte range [startOffset, endOffset) for the sequence point at
    /// <paramref name="line"/> within the given method.  Useful for computing step ranges.
    /// Returns <c>null</c> if no match is found.
    /// </summary>
    public (int startOffset, int endOffset)? GetILRangeForLine(int methodToken, int line)
    {
        if (!_sequencePointsByMethod.TryGetValue(methodToken, out var entries))
            return null;

        // Find the entry whose StartLine matches exactly
        var sorted = entries.Where(e => e.StartLine == line)
                            .OrderBy(e => e.ILOffset)
                            .ToList();
        if (sorted.Count == 0)
            return null;

        var first = sorted[0];

        // The end offset is the start of the next sequence point (or first.ILOffset + 1 as fallback)
        var allSorted = entries.OrderBy(e => e.ILOffset).ToList();
        int idx = allSorted.FindIndex(e => e.ILOffset == first.ILOffset);
        int endOffset = idx >= 0 && idx + 1 < allSorted.Count
            ? allSorted[idx + 1].ILOffset
            : first.ILOffset + 1;

        return (first.ILOffset, endOffset);
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        _pdbProvider?.Dispose();
        _pdbProvider = null;
        _pdbReader = null;
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------

    private void IndexSequencePoints()
    {
        if (_pdbReader is null)
            return;

        foreach (var methodDebugHandle in _pdbReader.MethodDebugInformation)
        {
            var methodDebugInfo = _pdbReader.GetMethodDebugInformation(methodDebugHandle);

            // Derive the method definition token (MethodDef table row)
            // MethodDebugInformationHandle row index == MethodDefinitionHandle row index
            int rowNumber = MetadataTokens.GetRowNumber(methodDebugHandle);
            var methodDefHandle = MetadataTokens.MethodDefinitionHandle(rowNumber);
            int methodToken = MetadataTokens.GetToken(methodDefHandle);

            if (methodDebugInfo.Document.IsNil)
                continue;

            var document = _pdbReader.GetDocument(methodDebugInfo.Document);
            string docPath = _pdbReader.GetString(document.Name);

            foreach (var sp in methodDebugInfo.GetSequencePoints())
            {
                // Hidden sequence points have StartLine == SequencePoint.HiddenLine
                if (sp.IsHidden)
                    continue;

                // Resolve the document for this specific sequence point (may differ per-sp)
                string filePath = docPath;
                if (!sp.Document.IsNil)
                {
                    var spDoc = _pdbReader.GetDocument(sp.Document);
                    filePath = _pdbReader.GetString(spDoc.Name);
                }

                var entry = new SequencePointEntry
                {
                    FilePath = filePath,
                    StartLine = sp.StartLine,
                    EndLine = sp.EndLine,
                    StartColumn = sp.StartColumn,
                    EndColumn = sp.EndColumn,
                    ILOffset = sp.Offset,
                    MethodToken = methodToken
                };

                // Index by file
                if (!_sequencePointsByFile.TryGetValue(filePath, out var fileList))
                {
                    fileList = new List<SequencePointEntry>();
                    _sequencePointsByFile[filePath] = fileList;
                }
                fileList.Add(entry);

                // Index by method
                if (!_sequencePointsByMethod.TryGetValue(methodToken, out var methodList))
                {
                    methodList = new List<SequencePointEntry>();
                    _sequencePointsByMethod[methodToken] = methodList;
                }
                methodList.Add(entry);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Private nested type
    // -----------------------------------------------------------------------

    private sealed class SequencePointEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int StartColumn { get; set; }
        public int EndColumn { get; set; }
        public int ILOffset { get; set; }
        public int MethodToken { get; set; }
    }
}
