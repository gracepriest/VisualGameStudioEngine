using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Preprocessor for BasicLang - handles #Include directives and include guards
    /// </summary>
    public class Preprocessor
    {
        private readonly HashSet<string> _includedFiles;
        private readonly List<string> _includePaths;
        private readonly List<PreprocessorError> _errors;
        private readonly HashSet<string> _definedSymbols;
        private readonly Stack<ConditionalState> _conditionalStack;
        private readonly List<string> _cppIncludes = new List<string>();
        private readonly List<BasicLang.Compiler.IR.JsImportDirective> _jsImports =
            new List<BasicLang.Compiler.IR.JsImportDirective>();

        public List<PreprocessorError> Errors => _errors;

        /// <summary>
        /// C++ headers collected from #CppInclude directives, as fully delimited tokens
        /// (e.g. "&lt;mutex&gt;" or "\"grid.h\""). These are passed through to the C++
        /// backend as real #include lines. Distinct from #Include (source-file splicing).
        /// Accumulates across all files processed by this instance; never cleared.
        /// </summary>
        public IReadOnlyList<string> CppIncludes => _cppIncludes;

        /// <summary>
        /// JavaScript imports collected from #JsImport directives — the module specifier
        /// (without quotes) plus any binding clause, already normalised to JavaScript spelling.
        /// The JavaScript backend re-quotes the specifier when it emits the ES `import`.
        /// Unlike <see cref="CppIncludes"/> there is no delimiter to preserve: JavaScript has
        /// no angle-bracket module form, so a specifier is always a quoted string and the
        /// quotes carry no meaning worth round-tripping.
        /// Accumulates across all files processed by this instance; never cleared.
        /// </summary>
        public IReadOnlyList<BasicLang.Compiler.IR.JsImportDirective> JsImports => _jsImports;

        /// <summary>A JavaScript identifier — what may appear either side of an <c>As</c>.</summary>
        private const string JsIdent = @"[A-Za-z_$][A-Za-z0-9_$]*";

        /// <summary>
        /// Parses one <c>#JsImport</c> line into a <see cref="JsImportDirective"/>, or records a
        /// diagnostic. Four forms, tried MOST SPECIFIC FIRST — the default-import pattern
        /// (<c>name From "…"</c>) would otherwise swallow <c>* As lib From "…"</c>'s tail.
        ///
        /// <para><b>Everything here is validated at parse time.</b> This is the last point where
        /// a line number is still available; after it, a malformed clause could only be emitted
        /// verbatim and left for the browser to reject.</para>
        /// </summary>
        private void ParseJsImport(string line, int lineNumber)
        {
            // The specifier is common to all four forms and is checked ONCE, at the end.
            string Fail(string message)
            {
                _errors.Add(new PreprocessorError { Line = lineNumber, Message = message });
                return null;
            }

            var namespaceForm = Regex.Match(line,
                $@"^#JsImport\s+\*\s+As\s+({JsIdent})\s+From\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase);
            var namedForm = Regex.Match(line,
                @"^#JsImport\s+\{([^}]*)\}\s+From\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase);
            var defaultForm = Regex.Match(line,
                $@"^#JsImport\s+({JsIdent})\s+From\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase);
            var bareForm = Regex.Match(line,
                @"^#JsImport\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase);

            string specifier, clause = null;
            var bound = new List<string>();

            if (namespaceForm.Success)
            {
                specifier = namespaceForm.Groups[2].Value;
                clause = "* as " + namespaceForm.Groups[1].Value;
                bound.Add(namespaceForm.Groups[1].Value);
            }
            else if (namedForm.Success)
            {
                specifier = namedForm.Groups[2].Value;

                var rendered = new List<string>();
                foreach (var raw in namedForm.Groups[1].Value.Split(','))
                {
                    var entry = raw.Trim();
                    if (entry.Length == 0) continue;   // a trailing comma is legal in ES

                    // `name` or `name As alias`. The alias form is not decoration: it is how a
                    // user dodges the BL7010 collision this backend raises when an imported name
                    // clashes with one their own program declares.
                    var alias = Regex.Match(entry, $@"^({JsIdent})\s+As\s+({JsIdent})$", RegexOptions.IgnoreCase);
                    var plain = Regex.Match(entry, $@"^({JsIdent})$");

                    if (alias.Success)
                    {
                        rendered.Add($"{alias.Groups[1].Value} as {alias.Groups[2].Value}");
                        bound.Add(alias.Groups[2].Value);
                    }
                    else if (plain.Success)
                    {
                        rendered.Add(plain.Groups[1].Value);
                        bound.Add(plain.Groups[1].Value);
                    }
                    else
                    {
                        Fail($"Invalid #JsImport binding '{entry}': expected a JavaScript name, " +
                             $"optionally followed by 'As alias'. In: {line}");
                        return;
                    }
                }

                if (rendered.Count == 0)
                {
                    Fail($"Invalid #JsImport syntax: '{{ }}' imports no names. Use " +
                         $"#JsImport \"{specifier}\" if the module is wanted only for its side " +
                         $"effects. In: {line}");
                    return;
                }

                clause = "{ " + string.Join(", ", rendered) + " }";
            }
            else if (defaultForm.Success)
            {
                specifier = defaultForm.Groups[2].Value;
                clause = defaultForm.Groups[1].Value;
                bound.Add(defaultForm.Groups[1].Value);
            }
            else if (bareForm.Success)
            {
                specifier = bareForm.Groups[1].Value;
            }
            else
            {
                // Quoted specifiers only. JavaScript has no angle-bracket module form, so
                // <./a.js> is not an alternate spelling to accept — it is a mistake, and so is
                // a bare unquoted path.
                Fail($"Invalid #JsImport syntax (expected a quoted module specifier, optionally " +
                     $"preceded by a binding clause such as '{{ greet }} From'): {line}");
                return;
            }

            // ⛔ A backslash path is the natural thing for a Windows user to type, and it is the
            // one bad specifier that produces NO diagnostic anywhere downstream: it collects, it
            // escapes cleanly into the emitted ES `import`, and no module loader — browser or
            // Node — resolves it. The result is a clean build and a 404 at run time. Module
            // specifiers are URLs, not OS paths: forward slashes on every platform.
            if (specifier.Contains('\\'))
            {
                Fail($"Invalid #JsImport specifier \"{specifier}\": JavaScript module specifiers " +
                     "use forward slashes, not backslashes (a backslash path resolves in no " +
                     "module loader)");
                return;
            }

            _jsImports.Add(new BasicLang.Compiler.IR.JsImportDirective(specifier, clause, bound));
        }

        public Preprocessor()
        {
            _includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _includePaths = new List<string>();
            _errors = new List<PreprocessorError>();
            _definedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _conditionalStack = new Stack<ConditionalState>();
        }

        /// <summary>
        /// State for conditional compilation blocks
        /// </summary>
        private class ConditionalState
        {
            public bool ParentActive { get; set; }    // was the enclosing block active when this one opened?
            public bool BranchActive { get; set; }    // is the current branch the one being compiled?
            public bool AnyBranchTaken { get; set; }  // has an earlier branch of this block been taken?
            public bool SeenElse { get; set; }        // has #Else been seen (no #ElseIf may follow)?
            public bool IsIfBlock { get; set; }       // opened by #If (true) or by #IfDef/#IfNDef (false)
        }

        private static readonly Regex IfDirective = new(@"^#If\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ElseIfDirective = new(@"^#ElseIf\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ElseDirective = new(@"^#Else\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex EndIfDirective = new(@"^#End\s*If\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // VB accepts "#If(B) Then": whitespace OR an opening parenthesis follows the keyword.
        private static readonly Regex IfForm = new(@"^#(?:Else)?If(?:\s+|(?=\())(?<cond>.*?)\s+Then\s*(?:'.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // #Else and #End If take nothing but whitespace or a trailing ' comment.
        private static readonly Regex BareElse = new(@"^#Else\s*(?:'.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ElseIfAsTwoWords = new(@"^#Else\s+If\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BareEndIf = new(@"^#End\s*If\s*(?:'.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Add a path to search for include files
        /// </summary>
        public void AddIncludePath(string path)
        {
            if (!_includePaths.Contains(path))
                _includePaths.Add(path);
        }

        /// <summary>
        /// Define a preprocessor symbol
        /// </summary>
        public void Define(string symbol)
        {
            _definedSymbols.Add(symbol);
        }

        /// <summary>
        /// Process a source file and handle all #Include directives
        /// </summary>
        public string Process(string source, string filePath)
        {
            _errors.Clear();
            _conditionalStack.Clear();

            // Track this file to prevent circular includes
            var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
            _includedFiles.Add(normalizedPath);

            return ProcessCore(source, filePath);
        }

        /// <summary>
        /// The line loop and the unclosed-block check. Clears nothing: an <c>#Include</c> calls it with the
        /// includer's errors kept and its conditional blocks set aside (see <see cref="ProcessInclude"/>).
        /// </summary>
        private string ProcessCore(string source, string filePath)
        {
            var result = new StringBuilder();
            var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var lineNumber = 0;

            foreach (var line in lines)
            {
                lineNumber++;
                var trimmedLine = line.TrimStart();

                // Check for #Include directive. Gated: an #Include in an inactive branch is not spliced
                // (nor resolved — a missing file there is no error, exactly as any other skipped line).
                if (trimmedLine.StartsWith("#Include", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsConditionalActive())
                    {
                        var includeContent = ProcessInclude(trimmedLine, filePath, lineNumber);
                        if (includeContent != null)
                        {
                            result.AppendLine(includeContent);
                        }
                        else
                        {
                            // Keep the original line if include failed (error already recorded)
                            result.AppendLine($"' Error: Failed to include - {line}");
                        }
                    }
                    else
                    {
                        result.AppendLine($"' [IFDEF SKIP] {line}");
                    }
                }
                // Check for #Define directive. Gated like #CppInclude: a #Define in an inactive branch defines
                // nothing. Its line is ALWAYS commented out, never removed, so every later line keeps its number.
                else if (trimmedLine.StartsWith("#Define", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsConditionalActive())
                        ProcessDefine(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
                // ⛔ ORDER IS LOAD-BEARING. #IfDef/#IfNDef before #If (\b stops "#If" matching "#IfDef" anyway);
                // #ElseIf before #Else (\b: "#Else" + "If" has no word boundary, so ElseDirective cannot take it).
                // Each directive line (conditional or #Define) is commented out, never removed, so line numbers
                // survive.
                else if (trimmedLine.StartsWith("#IfDef", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessIfDef(trimmedLine, lineNumber, false);
                    result.AppendLine($"' {line}");
                }
                else if (trimmedLine.StartsWith("#IfNDef", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessIfDef(trimmedLine, lineNumber, true);
                    result.AppendLine($"' {line}");
                }
                else if (ElseIfDirective.IsMatch(trimmedLine))
                {
                    ProcessElseIf(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
                else if (ElseDirective.IsMatch(trimmedLine))
                {
                    ProcessElse(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
                else if (EndIfDirective.IsMatch(trimmedLine))
                {
                    ProcessEndIf(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
                else if (IfDirective.IsMatch(trimmedLine))
                {
                    ProcessIf(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}");
                }
                // Check for #CppInclude directive (C++ std passthrough - emits a real
                // C++ #include; distinct from #Include which splices BasicLang source).
                else if (trimmedLine.StartsWith("#CppInclude", StringComparison.OrdinalIgnoreCase))
                {
                    // Only collect the header when inside an active conditional block, so
                    // platform-gated headers (e.g. #IfNDef WINDOWS ... #CppInclude <unistd.h>)
                    // are skipped when the guard is inactive. The directive line is always
                    // commented out (regardless of conditional state) to preserve line numbers.
                    if (IsConditionalActive())
                    {
                        var angle = Regex.Match(trimmedLine, @"#CppInclude\s+<([^>]+)>", RegexOptions.IgnoreCase);
                        var quote = Regex.Match(trimmedLine, "#CppInclude\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        if (angle.Success)
                            _cppIncludes.Add("<" + angle.Groups[1].Value + ">");
                        else if (quote.Success)
                            _cppIncludes.Add("\"" + quote.Groups[1].Value + "\"");
                        else
                            _errors.Add(new PreprocessorError { Line = lineNumber, Message = $"Invalid #CppInclude syntax: {trimmedLine}" });
                    }
                    result.AppendLine($"' {line}"); // Comment the directive out of the BasicLang source
                }
                // Check for #JsImport directive (JavaScript interop - becomes a real ES
                // `import` statement; the JS-backend sibling of #CppInclude).
                else if (trimmedLine.StartsWith("#JsImport", StringComparison.OrdinalIgnoreCase))
                {
                    // Same two behaviours as #CppInclude above: collect only inside an active
                    // conditional so a gated import is skipped, but ALWAYS comment the line out
                    // regardless of conditional state, because removing it would shift every
                    // subsequent line number and silently skew the JS source map.
                    if (IsConditionalActive())
                        ParseJsImport(trimmedLine, lineNumber);
                    result.AppendLine($"' {line}"); // Comment the directive out of the BasicLang source
                }
                else
                {
                    // Only include line if we're in an active conditional block
                    if (IsConditionalActive())
                    {
                        result.AppendLine(line);
                    }
                    else
                    {
                        // Comment out the line when in inactive block
                        result.AppendLine($"' [IFDEF SKIP] {line}");
                    }
                }
            }

            // Check for unclosed conditional blocks
            if (_conditionalStack.Count > 0)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = $"Unclosed conditional block: {_conditionalStack.Count} #End If missing"
                });
            }

            return result.ToString();
        }

        /// <summary>
        /// Process an #Include directive
        /// </summary>
        private string ProcessInclude(string line, string currentFile, int lineNumber)
        {
            // Pattern: #Include "file.bh" or #Include <file.bh>
            var quoteMatch = Regex.Match(line, @"#Include\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var angleMatch = Regex.Match(line, @"#Include\s+<([^>]+)>", RegexOptions.IgnoreCase);

            string includePath = null;
            bool isSystemInclude = false;

            if (quoteMatch.Success)
            {
                includePath = quoteMatch.Groups[1].Value;
            }
            else if (angleMatch.Success)
            {
                includePath = angleMatch.Groups[1].Value;
                isSystemInclude = true;
            }
            else
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = $"Invalid #Include syntax: {line}"
                });
                return null;
            }

            // Resolve the include path
            var resolvedPath = ResolveIncludePath(includePath, currentFile, isSystemInclude);

            if (resolvedPath == null)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = $"Cannot find include file: {includePath}"
                });
                return null;
            }

            // Check for circular include
            var normalizedResolved = Path.GetFullPath(resolvedPath).ToLowerInvariant();
            if (_includedFiles.Contains(normalizedResolved))
            {
                // File already included (include guard) - skip silently
                return $"' Already included: {includePath}";
            }

            // Mark as included
            _includedFiles.Add(normalizedResolved);

            // Read and process the included file
            try
            {
                var includeContent = File.ReadAllText(resolvedPath);

                // Add markers for source location tracking
                var result = new StringBuilder();
                result.AppendLine($"' Begin include: {includePath}");
                // ⛔ An included file has its OWN conditional blocks, and must not see or disturb the
                // includer's. Before, the recursive public Process() cleared _errors and _conditionalStack.
                var parentBlocks = _conditionalStack.ToArray();   // top first
                _conditionalStack.Clear();
                result.Append(ProcessCore(includeContent, resolvedPath));
                _conditionalStack.Clear();
                for (var i = parentBlocks.Length - 1; i >= 0; i--) _conditionalStack.Push(parentBlocks[i]);
                result.AppendLine($"' End include: {includePath}");

                return result.ToString();
            }
            catch (Exception ex)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = $"Error reading include file '{includePath}': {ex.Message}"
                });
                return null;
            }
        }

        /// <summary>
        /// Resolve the path of an include file
        /// </summary>
        private string ResolveIncludePath(string includePath, string currentFile, bool isSystemInclude)
        {
            // For quoted includes, first check relative to current file
            if (!isSystemInclude)
            {
                var currentDir = Path.GetDirectoryName(currentFile);
                var relativePath = Path.Combine(currentDir, includePath);
                if (File.Exists(relativePath))
                    return relativePath;
            }

            // Search in include paths
            foreach (var searchPath in _includePaths)
            {
                var fullPath = Path.Combine(searchPath, includePath);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // Try absolute path
            if (Path.IsPathRooted(includePath) && File.Exists(includePath))
                return includePath;

            return null;
        }

        /// <summary>
        /// Process a #Define directive
        /// </summary>
        private void ProcessDefine(string line, int lineNumber)
        {
            // Pattern: #Define SYMBOL or #Define SYMBOL value
            var match = Regex.Match(line, @"#Define\s+(\w+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var symbol = match.Groups[1].Value;
                _definedSymbols.Add(symbol);
            }
            else
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = $"Invalid #Define syntax: {line}"
                });
            }
        }

        /// <summary>
        /// Check if a symbol is defined
        /// </summary>
        public bool IsDefined(string symbol)
        {
            return _definedSymbols.Contains(symbol);
        }

        /// <summary>
        /// Process #IfDef or #IfNDef directive
        /// </summary>
        private void ProcessIfDef(string line, int lineNumber, bool isNegated)
        {
            var directiveName = isNegated ? "#IfNDef" : "#IfDef";
            var match = Regex.Match(line, directiveName + @"\s+(\w+)", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = $"Invalid {directiveName} syntax: expected symbol name"
                });
                // Push a default state to keep stack balanced. ParentActive is read BEFORE the push.
                _conditionalStack.Push(new ConditionalState
                {
                    ParentActive = IsConditionalActive(), BranchActive = false, AnyBranchTaken = false
                });
                return;
            }

            var symbol = match.Groups[1].Value;
            var isDefined = _definedSymbols.Contains(symbol);
            var conditionTrue = isNegated ? !isDefined : isDefined;

            _conditionalStack.Push(new ConditionalState
            {
                ParentActive = IsConditionalActive(), BranchActive = conditionTrue, AnyBranchTaken = conditionTrue
            });
        }

        /// <summary>Process an <c>#If … Then</c> directive (spec 2026-09-29 §4.1).</summary>
        private void ProcessIf(string line, int lineNumber)
        {
            var parentActive = IsConditionalActive();
            var taken = EvaluateIfForm(line, "#If", lineNumber);
            _conditionalStack.Push(new ConditionalState
            {
                ParentActive = parentActive, BranchActive = taken, AnyBranchTaken = taken, IsIfBlock = true
            });
        }

        /// <summary>Process an <c>#ElseIf … Then</c> directive: taken only if no earlier branch was.</summary>
        private void ProcessElseIf(string line, int lineNumber)
        {
            if (_conditionalStack.Count == 0) { Fail(lineNumber, "#ElseIf without matching #If"); return; }
            var state = _conditionalStack.Peek();
            if (!state.IsIfBlock) { Fail(lineNumber, "#ElseIf is only valid inside #If … #End If, not #IfDef/#IfNDef"); return; }
            if (state.SeenElse) { Fail(lineNumber, "#ElseIf after #Else"); return; }
            var taken = EvaluateIfForm(line, "#ElseIf", lineNumber);
            state.BranchActive = !state.AnyBranchTaken && taken;
            state.AnyBranchTaken |= state.BranchActive;
        }

        /// <summary>The condition of an #If/#ElseIf line; a malformed one is reported and counts as false.</summary>
        private bool EvaluateIfForm(string line, string directive, int lineNumber)
        {
            var match = IfForm.Match(line);
            if (!match.Success || match.Groups["cond"].Value.Trim().Length == 0)
            {
                Fail(lineNumber, $"{directive} requires a condition followed by 'Then'");
                return false;
            }
            var condition = match.Groups["cond"].Value;
            if (!PreprocessorCondition.TryEvaluate(condition, IsDefined, out var value, out var error))
            {
                Fail(lineNumber, $"Invalid {directive} condition '{condition}': {error}");
                return false;
            }
            return value;
        }

        private void Fail(int lineNumber, string message) =>
            _errors.Add(new PreprocessorError { Line = lineNumber, Message = message });

        /// <summary>
        /// Process #Else directive. Only whitespace or a ' comment may follow it.
        /// </summary>
        private void ProcessElse(string line, int lineNumber)
        {
            // ⛔ "#Else If B Then" is a slip for "#ElseIf". Taken as a plain #Else it would compile its branch
            // UNCONDITIONALLY with a green build; taken as #ElseIf it would bless a spelling VB refuses. It is an
            // error, and the branch it opens is never taken (no SeenElse: it was not an #Else).
            if (ElseIfAsTwoWords.IsMatch(line))
            {
                Fail(lineNumber, "'#Else If' is not a directive: write #ElseIf (one word)");
                if (_conditionalStack.Count > 0) _conditionalStack.Peek().BranchActive = false;
                return;
            }

            if (!BareElse.IsMatch(line))
                Fail(lineNumber, $"Unexpected text after #Else: {line}");   // reported, then read as #Else

            if (_conditionalStack.Count == 0)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = "#Else without matching #If, #IfDef or #IfNDef"
                });
                return;
            }

            var state = _conditionalStack.Peek();
            if (state.SeenElse)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = "Duplicate #Else in conditional block"
                });
                return;
            }

            state.BranchActive = !state.AnyBranchTaken;
            state.AnyBranchTaken = true;
            state.SeenElse = true;
        }

        /// <summary>
        /// Process #EndIf / #End If directive. Only whitespace or a ' comment may follow it.
        /// </summary>
        private void ProcessEndIf(string line, int lineNumber)
        {
            if (!BareEndIf.IsMatch(line))
                Fail(lineNumber, $"Unexpected text after #End If: {line}");   // reported, then read as #End If

            if (_conditionalStack.Count == 0)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = "#End If without matching #If, #IfDef or #IfNDef"
                });
                return;
            }

            _conditionalStack.Pop();
        }

        /// <summary>
        /// Check if the current conditional block is active (code should be included)
        /// </summary>
        private bool IsConditionalActive()
        {
            if (_conditionalStack.Count == 0)
                return true; // No conditional block, everything is active

            // Active only when the enclosing block was active AND this block's current branch is the taken one.
            var s = _conditionalStack.Peek();
            return s.ParentActive && s.BranchActive;
        }

        /// <summary>
        /// Clear the list of included files (for reprocessing)
        /// </summary>
        public void ClearIncludedFiles()
        {
            _includedFiles.Clear();
        }
    }

    /// <summary>
    /// Represents a preprocessor error
    /// </summary>
    public class PreprocessorError
    {
        public int Line { get; set; }
        public int Column { get; set; }
        public string Message { get; set; }

        public override string ToString()
        {
            return $"Line {Line}: {Message}";
        }
    }
}
