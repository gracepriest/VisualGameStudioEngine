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

    // Line offset for .mod/.cls files whose source was wrapped during preprocessing.
    // Key = normalized file path, Value = number of wrapper lines added before original content.
    private readonly Dictionary<string, int> _lineOffsets = new(StringComparer.OrdinalIgnoreCase);

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
    /// Registers a line offset for a .mod or .cls file that was preprocessed with
    /// wrapper lines. When the user sets a breakpoint at line N in the original file,
    /// this offset is added to translate to the PDB line number. When the debugger
    /// reports a PDB line, the offset is subtracted to show the original line.
    /// </summary>
    public void RegisterLineOffset(string filePath, int offset)
    {
        if (offset <= 0) return;
        _lineOffsets[NormalizePath(filePath)] = offset;
    }

    /// <summary>
    /// Gets the line offset for a file, or 0 if none is registered.
    /// </summary>
    public int GetLineOffset(string filePath)
    {
        return _lineOffsets.TryGetValue(NormalizePath(filePath), out var offset) ? offset : 0;
    }

    /// <summary>
    /// Translates a user-facing line number to a PDB line number by adding the
    /// wrapper offset for .mod/.cls files.
    /// </summary>
    public int UserLineToPdbLine(string filePath, int userLine)
    {
        return userLine + GetLineOffset(filePath);
    }

    /// <summary>
    /// Translates a PDB line number to a user-facing line number by subtracting
    /// the wrapper offset for .mod/.cls files.
    /// </summary>
    public int PdbLineToUserLine(string filePath, int pdbLine)
    {
        return Math.Max(1, pdbLine - GetLineOffset(filePath));
    }

    /// <summary>
    /// Returns the IL offset within the given method for the closest sequence point
    /// at or after <paramref name="line"/> in <paramref name="basFilePath"/>.
    /// Returns <c>null</c> if no matching sequence point is found.
    /// </summary>
    public (int methodToken, int ilOffset)? GetILOffsetForLine(string basFilePath, int line)
    {
        basFilePath = NormalizePath(basFilePath);
        // Translate user line to PDB line for .mod/.cls files
        line = UserLineToPdbLine(basFilePath, line);
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
    /// Returns ALL (methodToken, ilOffset) pairs whose sequence points sit on the
    /// nearest executable line at or after <paramref name="line"/>.
    ///
    /// A single .bas line can have IL in SEVERAL methods: a line containing a
    /// lambda has sequence points both in the enclosing method and in the
    /// compiler-generated closure method (&lt;&gt;c__DisplayClass...), and async
    /// functions split lines between the kickoff method and the state-machine
    /// MoveNext. Callers that bind breakpoints should bind to every returned
    /// method so the breakpoint hits both when the line executes normally and
    /// when the lambda/continuation is invoked.
    /// For each method, the lowest IL offset on that line is returned.
    /// </summary>
    public IReadOnlyList<(int methodToken, int ilOffset)> GetAllILOffsetsForLine(string basFilePath, int line)
    {
        var result = new List<(int methodToken, int ilOffset)>();
        basFilePath = NormalizePath(basFilePath);
        line = UserLineToPdbLine(basFilePath, line);
        if (!_sequencePointsByFile.TryGetValue(basFilePath, out var entries))
            return result;

        // Find the nearest executable line at or after the requested line
        int? bestLine = null;
        foreach (var entry in entries)
        {
            if (entry.StartLine < line)
                continue;
            if (bestLine == null || entry.StartLine < bestLine.Value)
                bestLine = entry.StartLine;
        }
        if (bestLine == null)
            return result;

        // Collect the lowest IL offset per method on that line
        var perMethod = new Dictionary<int, int>();
        foreach (var entry in entries)
        {
            if (entry.StartLine != bestLine.Value)
                continue;
            if (!perMethod.TryGetValue(entry.MethodToken, out var existing) || entry.ILOffset < existing)
                perMethod[entry.MethodToken] = entry.ILOffset;
        }

        foreach (var kvp in perMethod.OrderBy(k => k.Key))
            result.Add((kvp.Key, kvp.Value));
        return result;
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

        if (best is null) return null;
        // Translate PDB line back to user-facing line for .mod/.cls files
        int userLine = PdbLineToUserLine(best.FilePath, best.StartLine);
        return (best.FilePath, userLine, best.StartColumn);
    }

    /// <summary>
    /// Returns the nearest executable source line at or after the requested line.
    /// Falls back to returning <paramref name="line"/> itself when no PDB data is loaded
    /// or no sequence point is found.
    /// </summary>
    public int FindNearestExecutableLine(string basFilePath, int line)
    {
        basFilePath = NormalizePath(basFilePath);
        // Translate user line to PDB line for .mod/.cls files
        int pdbLine = UserLineToPdbLine(basFilePath, line);
        if (!_sequencePointsByFile.TryGetValue(basFilePath, out var entries))
            return line;

        int? nearest = null;
        foreach (var entry in entries)
        {
            if (entry.StartLine < pdbLine)
                continue;
            if (nearest == null || entry.StartLine < nearest.Value)
                nearest = entry.StartLine;
        }

        // Translate back to user-facing line
        return nearest.HasValue ? PdbLineToUserLine(basFilePath, nearest.Value) : line;
    }

    /// <summary>
    /// Get the next executable line AFTER the given line in the same method.
    /// Returns null if no next line exists (end of method).
    /// </summary>
    public (int methodToken, int ilOffset, int line)? GetNextExecutableLine(string basFilePath, int currentLine, int currentMethodToken)
    {
        basFilePath = NormalizePath(basFilePath);
        // Translate user line to PDB line for .mod/.cls files
        int pdbLine = UserLineToPdbLine(basFilePath, currentLine);
        if (!_sequencePointsByMethod.TryGetValue(currentMethodToken, out var sps))
            return null;

        // Find all lines in this method after the current line
        var nextEntry = sps
            .Where(sp => sp.StartLine > pdbLine &&
                   string.Equals(sp.FilePath, basFilePath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(sp => sp.StartLine)
            .FirstOrDefault();

        if (nextEntry == null) return null;

        // Translate back to user-facing line
        int userLine = PdbLineToUserLine(basFilePath, nextEntry.StartLine);
        return (nextEntry.MethodToken, nextEntry.ILOffset, userLine);
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

                    // Skip compiler-generated locals (e.g. "CS$<>8__locals0" closure
                    // containers, cached delegates). Omitting the slot (rather than
                    // renaming it) lets callers keep their own friendly name for the
                    // slot while positional slot->name mapping stays intact for the
                    // remaining user locals.
                    if (GeneratedNames.IsCompilerGeneratedLocalName(name))
                        continue;

                    // Only store if not already present (outer scope takes precedence)
                    if (!result.ContainsKey(slot))
                    {
                        result[slot] = GeneratedNames.DemangleMemberName(name);
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

/// <summary>
/// Helpers for recognizing and demangling C#-compiler-generated names produced
/// when BasicLang lambdas and async functions are compiled through the C#
/// backend:
///
///   Closure classes:        &lt;&gt;c__DisplayClass0_0, &lt;&gt;c (no captures)
///   Lambda methods:         &lt;Main&gt;b__0, &lt;Main&gt;b__0_0
///   Local functions:        &lt;Main&gt;g__Helper|0_0
///   Async state machines:   &lt;WorkAsync&gt;d__1 (method MoveNext)
///   Hoisted locals:         &lt;n&gt;5__2            → n
///   Hoisted 'this':         &lt;&gt;4__this          → Me
///   Plumbing fields:        &lt;&gt;1__state, &lt;&gt;t__builder, &lt;&gt;u__1
///   Closure locals:         CS$&lt;&gt;8__locals0 (slot in enclosing method)
/// </summary>
public static class GeneratedNames
{
    /// <summary>True for async/iterator state machine type names like "&lt;WorkAsync&gt;d__1".</summary>
    public static bool IsStateMachineType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        string simple = SimpleTypeName(typeName);
        if (simple.Length < 4 || simple[0] != '<') return false;
        int close = simple.IndexOf('>');
        if (close <= 1) return false;
        // "<Name>d__N"
        return close + 1 < simple.Length && simple[close + 1] == 'd';
    }

    /// <summary>True for closure display class names like "&lt;&gt;c__DisplayClass0_0" or "&lt;&gt;c".</summary>
    public static bool IsDisplayClassType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        string simple = SimpleTypeName(typeName);
        return simple.StartsWith("<>c", StringComparison.Ordinal);
    }

    /// <summary>True if the frame's declaring type is any compiler-generated nested type.</summary>
    public static bool IsCompilerGeneratedType(string typeName) =>
        IsStateMachineType(typeName) || IsDisplayClassType(typeName);

    /// <summary>
    /// Extracts the user-facing method name from a state machine type name:
    /// "&lt;WorkAsync&gt;d__1" → "WorkAsync". Returns null when the pattern does not match.
    /// </summary>
    public static string GetStateMachineMethodName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;
        string simple = SimpleTypeName(typeName);
        if (simple.Length < 4 || simple[0] != '<') return null;
        int close = simple.IndexOf('>');
        if (close <= 1) return null;
        return simple.Substring(1, close - 1);
    }

    /// <summary>
    /// Extracts the containing method name from a generated lambda/local-function
    /// method name: "&lt;Main&gt;b__0_0" → "Main". Returns null when not a generated name.
    /// </summary>
    public static string GetContainingMethodName(string methodName)
    {
        if (string.IsNullOrEmpty(methodName) || methodName[0] != '<') return null;
        int close = methodName.IndexOf('>');
        if (close <= 1) return null;
        return methodName.Substring(1, close - 1);
    }

    /// <summary>
    /// True for state-machine/closure plumbing fields that should be hidden from
    /// the user: &lt;&gt;1__state, &lt;&gt;t__builder, &lt;&gt;u__1 (awaiters), cached delegates
    /// (&lt;&gt;9__...), nested closure references, etc. The hoisted-this field
    /// (&lt;&gt;4__this) is NOT plumbing — it is surfaced as "Me".
    /// </summary>
    public static bool IsPlumbingFieldName(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return true;
        if (!fieldName.StartsWith("<>", StringComparison.Ordinal)) return false;
        if (fieldName.StartsWith("<>4__this", StringComparison.Ordinal)) return false;
        return true; // <>1__state, <>t__builder, <>u__N, <>9, <>9__N, <>8__N (nested closures kept out)
    }

    /// <summary>
    /// Demangles a hoisted/captured member name for display:
    ///   "&lt;n&gt;5__2"   → "n"      (hoisted local in a state machine)
    ///   "&lt;&gt;4__this" → "Me"     (hoisted enclosing instance)
    ///   "captured"      → "captured" (closure fields keep their original name)
    /// </summary>
    public static string DemangleMemberName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.StartsWith("<>4__this", StringComparison.Ordinal)) return "Me";
        if (name[0] == '<')
        {
            int close = name.IndexOf('>');
            if (close > 1)
                return name.Substring(1, close - 1);
        }
        return name;
    }

    /// <summary>
    /// True for compiler-generated LOCAL variable names in the enclosing method,
    /// e.g. "CS$&lt;&gt;8__locals0" (closure container) or other CS$ temps.
    /// </summary>
    public static bool IsCompilerGeneratedLocalName(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        return name.StartsWith("CS$", StringComparison.Ordinal) ||
               name.StartsWith("<>", StringComparison.Ordinal) ||
               name.StartsWith("<", StringComparison.Ordinal) && name.Contains('>') && name.Contains("__");
    }

    /// <summary>
    /// Maps a compiler-generated "Type.Method" frame name back to a user-facing
    /// name:
    ///   "&lt;&gt;c__DisplayClass0_0.&lt;Main&gt;b__0" → "Main.&lt;lambda&gt;"
    ///   "&lt;&gt;c.&lt;Main&gt;b__0_0"              → "Main.&lt;lambda&gt;"
    ///   "&lt;WorkAsync&gt;d__1.MoveNext"           → "WorkAsync"
    ///   "Program.&lt;Main&gt;g__Helper|0_0"        → "Main.Helper"
    /// Returns null when the name is not a recognized generated pattern
    /// (callers should then fall back to the raw name).
    /// </summary>
    public static string DemangleFrameName(string typeDotMethod)
    {
        if (string.IsNullOrEmpty(typeDotMethod)) return null;

        int lastDot = typeDotMethod.LastIndexOf('.');
        string typePart = lastDot > 0 ? typeDotMethod.Substring(0, lastDot) : string.Empty;
        string methodPart = lastDot >= 0 ? typeDotMethod.Substring(lastDot + 1) : typeDotMethod;

        // Async/iterator state machine: <WorkAsync>d__1.MoveNext → WorkAsync
        if (IsStateMachineType(typePart) &&
            (methodPart == "MoveNext" || methodPart == "MoveNextAsync"))
        {
            return GetStateMachineMethodName(typePart);
        }

        // Closure class: <>c__DisplayClass0_0.<Main>b__0 → Main.<lambda>
        if (IsDisplayClassType(typePart))
        {
            string containing = GetContainingMethodName(methodPart);
            return containing != null ? containing + ".<lambda>" : "<lambda>";
        }

        // Local function on a normal type: Program.<Main>g__Helper|0_0 → Main.Helper
        if (methodPart.Length > 2 && methodPart[0] == '<')
        {
            int close = methodPart.IndexOf('>');
            if (close > 1 && close + 2 < methodPart.Length && methodPart[close + 1] == 'g')
            {
                string containing = methodPart.Substring(1, close - 1);
                int start = methodPart.IndexOf("__", close, StringComparison.Ordinal);
                if (start > 0)
                {
                    int bar = methodPart.IndexOf('|', start);
                    string local = bar > start + 2
                        ? methodPart.Substring(start + 2, bar - start - 2)
                        : methodPart.Substring(start + 2);
                    return containing + "." + local;
                }
            }
            // Lambda emitted directly on the containing type: Program.<Main>b__0_0
            if (close > 1 && close + 1 < methodPart.Length && methodPart[close + 1] == 'b')
            {
                return methodPart.Substring(1, close - 1) + ".<lambda>";
            }
        }

        return null;
    }

    private static string SimpleTypeName(string typeName)
    {
        // Strip namespace and enclosing type prefixes ("Ns.Outer+<Foo>d__1")
        int plus = typeName.LastIndexOf('+');
        int dot = typeName.LastIndexOf('.');
        int cut = Math.Max(plus, dot);
        return cut >= 0 && cut + 1 < typeName.Length ? typeName.Substring(cut + 1) : typeName;
    }
}
