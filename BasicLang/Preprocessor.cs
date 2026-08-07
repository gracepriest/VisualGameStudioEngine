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
            public bool ConditionWasTrue { get; set; }  // Was the #IfDef/#IfNDef condition true?
            public bool InElseBranch { get; set; }       // Are we in the #Else branch?
            public bool ParentActive { get; set; }       // Was the parent block active?
        }

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

            var result = new StringBuilder();
            var lines = source.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var lineNumber = 0;

            foreach (var line in lines)
            {
                lineNumber++;
                var trimmedLine = line.TrimStart();

                // Check for #Include directive
                if (trimmedLine.StartsWith("#Include", StringComparison.OrdinalIgnoreCase))
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
                // Check for #Define directive
                else if (trimmedLine.StartsWith("#Define", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessDefine(trimmedLine, lineNumber);
                }
                // Check for #IfDef directive
                else if (trimmedLine.StartsWith("#IfDef", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessIfDef(trimmedLine, lineNumber, false);
                    result.AppendLine($"' {line}"); // Comment out the directive
                }
                // Check for #IfNDef directive
                else if (trimmedLine.StartsWith("#IfNDef", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessIfDef(trimmedLine, lineNumber, true);
                    result.AppendLine($"' {line}"); // Comment out the directive
                }
                // Check for #Else directive
                else if (trimmedLine.StartsWith("#Else", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessElse(lineNumber);
                    result.AppendLine($"' {line}"); // Comment out the directive
                }
                // Check for #EndIf directive
                else if (trimmedLine.StartsWith("#EndIf", StringComparison.OrdinalIgnoreCase))
                {
                    ProcessEndIf(lineNumber);
                    result.AppendLine($"' {line}"); // Comment out the directive
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
                    Message = $"Unclosed conditional block: {_conditionalStack.Count} #EndIf missing"
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
                result.Append(Process(includeContent, resolvedPath)); // Recursive processing
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
                // Push a default state to keep stack balanced
                _conditionalStack.Push(new ConditionalState
                {
                    ConditionWasTrue = false,
                    InElseBranch = false,
                    ParentActive = IsConditionalActive()
                });
                return;
            }

            var symbol = match.Groups[1].Value;
            var isDefined = _definedSymbols.Contains(symbol);
            var conditionTrue = isNegated ? !isDefined : isDefined;

            _conditionalStack.Push(new ConditionalState
            {
                ConditionWasTrue = conditionTrue,
                InElseBranch = false,
                ParentActive = IsConditionalActive()
            });
        }

        /// <summary>
        /// Process #Else directive
        /// </summary>
        private void ProcessElse(int lineNumber)
        {
            if (_conditionalStack.Count == 0)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = "#Else without matching #IfDef or #IfNDef"
                });
                return;
            }

            var state = _conditionalStack.Peek();
            if (state.InElseBranch)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = "Duplicate #Else in conditional block"
                });
                return;
            }

            state.InElseBranch = true;
        }

        /// <summary>
        /// Process #EndIf directive
        /// </summary>
        private void ProcessEndIf(int lineNumber)
        {
            if (_conditionalStack.Count == 0)
            {
                _errors.Add(new PreprocessorError
                {
                    Line = lineNumber,
                    Message = "#EndIf without matching #IfDef or #IfNDef"
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

            var state = _conditionalStack.Peek();

            // If parent wasn't active, we're not active either
            if (!state.ParentActive)
                return false;

            // In the #If branch: active if condition was true
            // In the #Else branch: active if condition was false
            return state.InElseBranch ? !state.ConditionWasTrue : state.ConditionWasTrue;
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
