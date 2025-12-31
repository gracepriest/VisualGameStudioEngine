using System;
using System.Collections.Generic;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Status of a compilation unit
    /// </summary>
    public enum CompilationStatus
    {
        /// <summary>Unit has not been processed</summary>
        Pending,
        /// <summary>Source is being lexed and parsed</summary>
        Parsing,
        /// <summary>Semantic analysis in progress</summary>
        Analyzing,
        /// <summary>IR generation in progress</summary>
        GeneratingIR,
        /// <summary>Compilation completed successfully</summary>
        Complete,
        /// <summary>Compilation failed with errors</summary>
        Error
    }

    /// <summary>
    /// Represents a single source file being compiled
    /// </summary>
    public class CompilationUnit
    {
        /// <summary>
        /// Unique identifier for this unit (normalized file path)
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Absolute path to the source file
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Module name (derived from file name)
        /// </summary>
        public string ModuleName { get; }

        /// <summary>
        /// Source code content
        /// </summary>
        public string SourceCode { get; set; }

        /// <summary>
        /// Parsed AST
        /// </summary>
        public ProgramNode AST { get; set; }

        /// <summary>
        /// Symbol table after semantic analysis
        /// </summary>
        public Scope Symbols { get; set; }

        /// <summary>
        /// Generated IR module
        /// </summary>
        public IRModule IR { get; set; }

        /// <summary>
        /// Current compilation status
        /// </summary>
        public CompilationStatus Status { get; set; }

        /// <summary>
        /// Compilation errors
        /// </summary>
        public List<SemanticError> Errors { get; }

        /// <summary>
        /// Files this unit depends on (imports)
        /// </summary>
        public List<string> Dependencies { get; }

        /// <summary>
        /// Symbols exported by this unit (public members)
        /// </summary>
        public List<Symbol> ExportedSymbols { get; }

        /// <summary>
        /// Import directives in this file
        /// </summary>
        public List<ImportInfo> Imports { get; }

        /// <summary>
        /// Using directives in this file
        /// </summary>
        public List<UsingInfo> Usings { get; }

        /// <summary>
        /// Time when compilation completed
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Source file last modified time (for incremental compilation)
        /// </summary>
        public DateTime LastModified { get; set; }

        public CompilationUnit(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath));

            FilePath = System.IO.Path.GetFullPath(filePath);
            Id = ModuleResolver.GetModuleId(filePath);
            ModuleName = ModuleResolver.GetModuleName(filePath);
            Status = CompilationStatus.Pending;
            Errors = new List<SemanticError>();
            Dependencies = new List<string>();
            ExportedSymbols = new List<Symbol>();
            Imports = new List<ImportInfo>();
            Usings = new List<UsingInfo>();
        }

        /// <summary>
        /// Check if this unit needs recompilation
        /// </summary>
        public bool NeedsRecompilation()
        {
            if (Status == CompilationStatus.Pending)
                return true;

            if (Status == CompilationStatus.Error)
                return true;

            // Check if source file has been modified
            if (System.IO.File.Exists(FilePath))
            {
                var currentModified = System.IO.File.GetLastWriteTimeUtc(FilePath);
                if (currentModified > LastModified)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Reset unit for recompilation
        /// </summary>
        public void Reset()
        {
            Status = CompilationStatus.Pending;
            AST = null;
            Symbols = null;
            IR = null;
            Errors.Clear();
            Dependencies.Clear();
            ExportedSymbols.Clear();
            Imports.Clear();
            Usings.Clear();
            CompletedAt = null;
        }

        /// <summary>
        /// Check if unit has errors
        /// </summary>
        public bool HasErrors => Errors.Count > 0 || Status == CompilationStatus.Error;

        /// <summary>
        /// Check if unit is fully compiled
        /// </summary>
        public bool IsComplete => Status == CompilationStatus.Complete;

        public override string ToString()
        {
            return $"{ModuleName} ({Status})";
        }
    }

    /// <summary>
    /// Information about an import directive
    /// </summary>
    public class ImportInfo
    {
        /// <summary>
        /// The import reference as written in source (module name or path)
        /// </summary>
        public string Reference { get; set; }

        /// <summary>
        /// Resolved file path (null if unresolved)
        /// </summary>
        public string ResolvedPath { get; set; }

        /// <summary>
        /// Line number in source
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Column number in source
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// Whether this import uses path syntax (quoted)
        /// </summary>
        public bool IsPathImport { get; set; }

        public ImportInfo(string reference, int line, int column)
        {
            Reference = reference;
            Line = line;
            Column = column;
            IsPathImport = reference.StartsWith("\"") || reference.StartsWith("'");
        }
    }

    /// <summary>
    /// Information about a using directive
    /// </summary>
    public class UsingInfo
    {
        /// <summary>
        /// The namespace name
        /// </summary>
        public string Namespace { get; set; }

        /// <summary>
        /// Line number in source
        /// </summary>
        public int Line { get; set; }

        /// <summary>
        /// Column number in source
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// Resolved file paths that provide this namespace
        /// </summary>
        public List<string> ResolvedPaths { get; set; }

        /// <summary>
        /// If true, this is a .NET Framework/BCL namespace (e.g., System.IO)
        /// </summary>
        public bool IsNetNamespace { get; set; }

        /// <summary>
        /// Optional alias for the namespace (e.g., Using IO = System.IO)
        /// </summary>
        public string Alias { get; set; }

        public UsingInfo(string namespaceName, int line, int column, bool isNetNamespace = false, string alias = null)
        {
            Namespace = namespaceName;
            Line = line;
            Column = column;
            ResolvedPaths = new List<string>();
            IsNetNamespace = isNetNamespace;
            Alias = alias;
        }
    }
}
