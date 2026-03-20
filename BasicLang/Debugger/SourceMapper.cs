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
        basFilePath = NormalizePath(basFilePath);
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
        basFilePath = NormalizePath(basFilePath);
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
    /// Get the next executable line AFTER the given line in the same method.
    /// Returns null if no next line exists (end of method).
    /// </summary>
    public (int methodToken, int ilOffset, int line)? GetNextExecutableLine(string basFilePath, int currentLine, int currentMethodToken)
    {
        basFilePath = NormalizePath(basFilePath);
        if (!_sequencePointsByMethod.TryGetValue(currentMethodToken, out var sps))
            return null;

        // Find all lines in this method after the current line
        var nextEntry = sps
            .Where(sp => sp.StartLine > currentLine &&
                   string.Equals(sp.FilePath, basFilePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(sp => sp.StartLine)
            .FirstOrDefault();

        if (nextEntry == null) return null;

        return (nextEntry.MethodToken, nextEntry.ILOffset, nextEntry.StartLine);
    }

    /// <summary>
    /// Get all executable lines for a method (for step-over via temporary breakpoints)
    /// </summary>
    public IReadOnlyList<(int line, int ilOffset)> GetMethodLines(int methodToken)
    {
        if (!_sequencePointsByMethod.TryGetValue(methodToken, out var sps))
            return Array.Empty<(int, int)>();

        return sps
            .OrderBy(sp => sp.ILOffset)
            .Select(sp => (sp.StartLine, sp.ILOffset))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Finds the entry point (method token + first IL offset) for a Sub or Function
    /// by scanning the source files referenced in the PDB for a declaration line matching
    /// "Sub {name}" or "Function {name}".
    /// Returns null if no match is found.
    /// </summary>
    public (int methodToken, int ilOffset, string filePath, int line)? FindMethodEntryByName(string functionName)
    {
        foreach (var kvp in _sequencePointsByFile)
        {
            string filePath = kvp.Key;
            if (!File.Exists(filePath))
                continue;

            try
            {
                var lines = File.ReadAllLines(filePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string trimmed = lines[i].Trim();
                    // Match patterns like: Sub Main, Function Foo, Public Sub Main, Private Function Bar(...)
                    // We look for "Sub <name>" or "Function <name>" as a word boundary match
                    if (IsMethodDeclaration(trimmed, functionName))
                    {
                        // Found the declaration line (1-based)
                        int declLine = i + 1;
                        // Get the IL offset for the next executable line at or after the declaration
                        var ilInfo = GetILOffsetForLine(filePath, declLine);
                        if (ilInfo.HasValue)
                        {
                            return (ilInfo.Value.methodToken, ilInfo.Value.ilOffset, filePath, declLine);
                        }
                    }
                }
            }
            catch
            {
                // Skip files that can't be read
            }
        }

        return null;
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

        // Find all entries on this line
        var onLine = entries.Where(e => e.StartLine == line)
                            .OrderBy(e => e.ILOffset)
                            .ToList();
        if (onLine.Count == 0)
            return null;

        int startOffset = onLine[0].ILOffset;

        // End offset = start of the first sequence point on a DIFFERENT line (after this one)
        var allSorted = entries.OrderBy(e => e.ILOffset).ToList();
        int endOffset = startOffset + 1; // fallback

        // Find the first sequence point after our line's last entry that's on a different line
        var lastOnLine = onLine[onLine.Count - 1];
        foreach (var sp in allSorted)
        {
            if (sp.ILOffset > lastOnLine.ILOffset && sp.StartLine != line)
            {
                endOffset = sp.ILOffset;
                break;
            }
        }

        return (startOffset, endOffset);
    }

    /// <summary>
    /// Returns the local variable names for the given method, ordered by slot index.
    /// Uses the PDB's LocalScope and LocalVariable tables.
    /// If no PDB data is available, returns an empty dictionary.
    /// Key = slot index, Value = variable name.
    /// </summary>
    public Dictionary<int, string> GetLocalVariableNames(int methodToken)
    {
        var result = new Dictionary<int, string>();
        if (_pdbReader is null) return result;

        try
        {
            // Convert method token to MethodDefinitionHandle
            int rowNumber = MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(methodToken));
            var methodDefHandle = MetadataTokens.MethodDefinitionHandle(rowNumber);

            foreach (var scopeHandle in _pdbReader.GetLocalScopes(methodDefHandle))
            {
                var scope = _pdbReader.GetLocalScope(scopeHandle);
                foreach (var varHandle in scope.GetLocalVariables())
                {
                    var localVar = _pdbReader.GetLocalVariable(varHandle);
                    string name = _pdbReader.GetString(localVar.Name);
                    int slot = localVar.Index;
                    // Only store if not already present (outer scope takes precedence)
                    if (!result.ContainsKey(slot))
                    {
                        result[slot] = name;
                    }
                }
            }
        }
        catch
        {
            // PDB may not have local variable info for all methods
        }

        return result;
    }

    /// <summary>
    /// Returns the parameter names for the given method token by reading the PE metadata.
    /// This requires the assembly metadata reader, not the PDB reader.
    /// Returns an empty dictionary if metadata is not available.
    /// Key = parameter index (0-based), Value = parameter name.
    /// </summary>
    public Dictionary<int, string> GetParameterNames(int methodToken, MetadataReader? assemblyReader)
    {
        var result = new Dictionary<int, string>();
        if (assemblyReader == null) return result;

        try
        {
            int rowNumber = MetadataTokens.GetRowNumber(MetadataTokens.EntityHandle(methodToken));
            var methodDefHandle = MetadataTokens.MethodDefinitionHandle(rowNumber);
            var methodDef = assemblyReader.GetMethodDefinition(methodDefHandle);

            int index = 0;
            foreach (var paramHandle in methodDef.GetParameters())
            {
                var param = assemblyReader.GetParameter(paramHandle);
                string name = assemblyReader.GetString(param.Name);
                if (!string.IsNullOrEmpty(name))
                {
                    result[param.SequenceNumber - 1] = name; // SequenceNumber is 1-based
                }
                index++;
            }
        }
        catch
        {
            // Metadata may not be available
        }

        return result;
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

                // Normalize the file path for consistent matching
                filePath = NormalizePath(filePath);

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

    /// <summary>
    /// Checks whether a trimmed source line declares a Sub or Function with the given name.
    /// Matches patterns like "Sub Main", "Public Function Foo()", "Private Sub Bar(x As Integer)".
    /// </summary>
    private static bool IsMethodDeclaration(string trimmedLine, string functionName)
    {
        // Remove leading access modifiers and other keywords
        // Common prefixes: Public, Private, Protected, Friend, Shared, Overrides, Overloads, Static, Async
        string line = trimmedLine;

        // Look for "Sub <name>" or "Function <name>" anywhere in the line
        int subIdx = FindKeywordIndex(line, "Sub");
        if (subIdx >= 0)
        {
            string afterSub = line.Substring(subIdx + 3).TrimStart();
            if (NameMatches(afterSub, functionName))
                return true;
        }

        int funcIdx = FindKeywordIndex(line, "Function");
        if (funcIdx >= 0)
        {
            string afterFunc = line.Substring(funcIdx + 8).TrimStart();
            if (NameMatches(afterFunc, functionName))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the index of a keyword in the line, ensuring it's a whole word (not part of "EndSub" etc.).
    /// </summary>
    private static int FindKeywordIndex(string line, string keyword)
    {
        int idx = 0;
        while (idx <= line.Length - keyword.Length)
        {
            int pos = line.IndexOf(keyword, idx, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
                return -1;

            // Check that it's a whole word
            bool startOk = pos == 0 || !char.IsLetterOrDigit(line[pos - 1]);
            bool endOk = pos + keyword.Length >= line.Length || !char.IsLetterOrDigit(line[pos + keyword.Length]);
            if (startOk && endOk)
                return pos;

            idx = pos + 1;
        }
        return -1;
    }

    /// <summary>
    /// Checks whether the text starts with the function name (case-insensitive),
    /// followed by end-of-string, '(', or whitespace.
    /// </summary>
    private static bool NameMatches(string text, string functionName)
    {
        if (text.Length < functionName.Length)
            return false;
        if (!text.StartsWith(functionName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.Length == functionName.Length)
            return true;
        char next = text[functionName.Length];
        return next == '(' || char.IsWhiteSpace(next);
    }

    /// <summary>
    /// Normalizes a file path by converting forward slashes to backslashes (on Windows)
    /// and using the full path form. This ensures consistent matching between #line
    /// directive paths and PDB document paths.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        // Normalize slashes to the OS convention
        path = path.Replace('/', Path.DirectorySeparatorChar);
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

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
