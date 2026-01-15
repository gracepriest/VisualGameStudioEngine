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

        public List<PreprocessorError> Errors => _errors;

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
