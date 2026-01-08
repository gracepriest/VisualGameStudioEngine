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

        public List<PreprocessorError> Errors => _errors;

        public Preprocessor()
        {
            _includedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _includePaths = new List<string>();
            _errors = new List<PreprocessorError>();
            _definedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                // Check for #IfDef / #IfNDef / #EndIf (simple conditional compilation)
                else if (trimmedLine.StartsWith("#IfDef", StringComparison.OrdinalIgnoreCase) ||
                         trimmedLine.StartsWith("#IfNDef", StringComparison.OrdinalIgnoreCase) ||
                         trimmedLine.StartsWith("#EndIf", StringComparison.OrdinalIgnoreCase) ||
                         trimmedLine.StartsWith("#Else", StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: Implement conditional compilation in a future phase
                    result.AppendLine(line);
                }
                else
                {
                    result.AppendLine(line);
                }
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
