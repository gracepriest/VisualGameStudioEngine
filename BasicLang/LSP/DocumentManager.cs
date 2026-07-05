using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Manages open documents and their parsed state with caching support
    /// </summary>
    public class DocumentManager
    {
        private readonly ConcurrentDictionary<DocumentUri, DocumentState> _documents;
        private readonly ConcurrentDictionary<string, CachedParseResult> _parseCache;
        private readonly LspProjectContextProvider _projectContextProvider;
        private readonly int _maxCacheEntries;
        private readonly object _cacheLock = new object();

        /// <summary>
        /// Shared TypeRegistry for .NET assembly loading
        /// </summary>
        public TypeRegistry TypeRegistry { get; private set; }

        public DocumentManager(int maxCacheEntries = 100)
        {
            _documents = new ConcurrentDictionary<DocumentUri, DocumentState>();
            _parseCache = new ConcurrentDictionary<string, CachedParseResult>();
            _projectContextProvider = new LspProjectContextProvider();
            _maxCacheEntries = maxCacheEntries;

            // Initialize TypeRegistry with auto-detected .NET SDK path
            InitializeTypeRegistry();
        }

        /// <summary>
        /// Initialize the TypeRegistry with assembly search paths
        /// </summary>
        private void InitializeTypeRegistry()
        {
            TypeRegistry = new TypeRegistry();

            // Preload core .NET types from the running runtime so member
            // completion for Console/String/List/... works even when no SDK
            // reference assemblies are found on disk (finding [15]).
            TypeRegistry.PreloadCoreTypes();

            // Try to load cached index first
            if (!TypeRegistry.LoadIndexFromCache())
            {
                // Auto-detect .NET SDK reference assemblies
                var sdkPath = TypeRegistry.DetectDotNetSdkPath();
                if (!string.IsNullOrEmpty(sdkPath))
                {
                    TypeRegistry.AddSearchPath(sdkPath);
                }

                // Build the index (runs in background for large assembly sets)
                try
                {
                    TypeRegistry.BuildIndex();
                }
                catch
                {
                    // Ignore index build failures
                }
            }
        }

        /// <summary>
        /// Add additional assembly search paths (e.g., user-defined libraries)
        /// </summary>
        public void AddAssemblySearchPath(string path)
        {
            TypeRegistry?.AddSearchPath(path);
            // Rebuild index to include new path
            try
            {
                TypeRegistry?.BuildIndex();
            }
            catch
            {
                // Ignore rebuild failures
            }
        }

        /// <summary>
        /// Open or update a document with caching support
        /// </summary>
        public DocumentState UpdateDocument(DocumentUri uri, string content)
        {
            // Resolve the document's project (nearest .blproj or sibling files)
            // so semantic analysis can see cross-file symbols.
            var projectContext = GetProjectContext(uri, content);

            // Check if document exists and content hasn't changed
            if (_documents.TryGetValue(uri, out var existingState))
            {
                if (existingState.ContentHash == ComputeHash(content))
                {
                    if (projectContext?.Stamp == existingState.ProjectStamp)
                    {
                        // Content and project both unchanged, return cached state
                        return existingState;
                    }

                    // A sibling project file changed: keep the parse, re-run
                    // semantic analysis against the fresh project symbol table.
                    existingState.SetProjectContext(projectContext);
                    existingState.ReRunSemanticAnalysis();
                    return existingState;
                }
            }

            // Create new state and try to use cached parse results
            var state = new DocumentState(uri, content);
            state.TypeRegistry = TypeRegistry; // Share TypeRegistry across documents
            state.SetProjectContext(projectContext);
            var contentHash = state.ContentHash;

            // Check parse cache
            if (_parseCache.TryGetValue(contentHash, out var cachedResult))
            {
                // Use cached parse result
                state.ApplyCachedResult(cachedResult);
            }
            else
            {
                // Parse and cache the result
                state.Parse();

                // Cache the result if parsing was successful
                if (state.ParseSuccessful)
                {
                    CacheParseResult(contentHash, state);
                }
            }

            _documents[uri] = state;
            return state;
        }

        private void CacheParseResult(string contentHash, DocumentState state)
        {
            lock (_cacheLock)
            {
                // Evict old entries if cache is full
                if (_parseCache.Count >= _maxCacheEntries)
                {
                    // Remove oldest entries (simple FIFO eviction)
                    var keysToRemove = new List<string>();
                    int removeCount = _parseCache.Count / 4; // Remove 25%
                    int count = 0;
                    foreach (var key in _parseCache.Keys)
                    {
                        if (count++ < removeCount)
                            keysToRemove.Add(key);
                        else
                            break;
                    }
                    foreach (var key in keysToRemove)
                    {
                        _parseCache.TryRemove(key, out _);
                    }
                }

                _parseCache[contentHash] = new CachedParseResult
                {
                    Tokens = state.Tokens,
                    AST = state.AST,
                    CachedAt = DateTime.UtcNow
                };
            }
        }

        private static string ComputeHash(string content)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(content);
                var hashBytes = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hashBytes);
            }
        }

        /// <summary>
        /// Clear the parse cache
        /// </summary>
        public void ClearCache()
        {
            _parseCache.Clear();
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public (int documentCount, int cacheEntries) GetCacheStats()
        {
            return (_documents.Count, _parseCache.Count);
        }

        /// <summary>
        /// Get a document's state
        /// </summary>
        public DocumentState GetDocument(DocumentUri uri)
        {
            _documents.TryGetValue(uri, out var state);
            return state;
        }

        /// <summary>
        /// Close a document
        /// </summary>
        public void CloseDocument(DocumentUri uri)
        {
            _documents.TryRemove(uri, out _);
        }

        /// <summary>
        /// Get all open documents
        /// </summary>
        public IEnumerable<DocumentState> GetAllDocuments()
        {
            return _documents.Values;
        }

        /// <summary>
        /// Resolve the project context for a document. Returns null for
        /// documents that don't map to a file on disk (unsaved/virtual docs).
        /// </summary>
        private LspProjectContext GetProjectContext(DocumentUri uri, string content)
        {
            try
            {
                var filePath = DocumentState.TryGetFileSystemPath(uri);
                if (filePath == null)
                    return null;

                return _projectContextProvider.GetContext(filePath, content, GetOpenDocumentContent);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Return the open-editor content for a file path, or null if the file
        /// is not open (the project provider then reads it from disk).
        /// </summary>
        private string GetOpenDocumentContent(string filePath)
        {
            foreach (var doc in _documents.Values)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, filePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    return doc.Content;
                }
            }
            return null;
        }

        /// <summary>
        /// After a document changed, re-run semantic analysis for the OTHER
        /// open documents in the same project so their diagnostics reflect the
        /// new cross-file symbols. Returns the refreshed documents (so callers
        /// can republish diagnostics).
        /// </summary>
        public List<DocumentState> RefreshOpenProjectSiblings(DocumentUri changedUri)
        {
            var refreshed = new List<DocumentState>();

            var changed = GetDocument(changedUri);
            var context = changed?.ProjectContext;
            if (context == null)
                return refreshed;

            foreach (var other in _documents.Values)
            {
                if (other.Uri == changedUri)
                    continue;
                if (other.FilePath == null || !context.ContainsFile(other.FilePath))
                    continue;
                if (other.ProjectStamp == context.Stamp)
                    continue;

                other.SetProjectContext(context);
                other.ReRunSemanticAnalysis();
                refreshed.Add(other);
            }

            return refreshed;
        }
    }

    /// <summary>
    /// Cached parse result for reuse
    /// </summary>
    public class CachedParseResult
    {
        public List<Token> Tokens { get; set; }
        public ProgramNode AST { get; set; }
        public DateTime CachedAt { get; set; }
    }

    /// <summary>
    /// Represents the state of a single document
    /// </summary>
    public class DocumentState
    {
        public DocumentUri Uri { get; }
        public string Content { get; private set; }
        public string ContentHash { get; private set; }
        public string[] Lines { get; private set; }
        public List<Token> Tokens { get; private set; }
        public ProgramNode AST { get; private set; }
        public SemanticAnalyzer SemanticAnalyzer { get; private set; }
        public List<Diagnostic> Diagnostics { get; private set; }
        public bool ParseSuccessful { get; private set; }
        public bool SemanticSuccessful { get; private set; }
        public bool FromCache { get; private set; }
        public DateTime ParsedAt { get; private set; }

        /// <summary>
        /// TypeRegistry for .NET assembly loading (shared across documents)
        /// </summary>
        public TypeRegistry TypeRegistry { get; set; }

        /// <summary>
        /// File-system path of the document (null for unsaved/virtual docs)
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Module name derived from the file name (BasicLang module semantics)
        /// </summary>
        public string ModuleName { get; }

        /// <summary>
        /// Project the document belongs to (cross-file symbols), or null for
        /// single-file analysis.
        /// </summary>
        public LspProjectContext ProjectContext { get; private set; }

        /// <summary>
        /// Stamp of the project context the last semantic analysis ran against.
        /// </summary>
        public string ProjectStamp { get; private set; }

        /// <summary>
        /// Alias for Content property (for CompletionService compatibility)
        /// </summary>
        public string SourceCode => Content;

        public DocumentState(DocumentUri uri, string content)
        {
            Uri = uri;
            Content = content;
            ContentHash = ComputeContentHash(content);
            Lines = content.Split('\n');
            Tokens = new List<Token>();
            Diagnostics = new List<Diagnostic>();
            ParsedAt = DateTime.UtcNow;
            FilePath = TryGetFileSystemPath(uri);
            ModuleName = FilePath != null
                ? System.IO.Path.GetFileNameWithoutExtension(FilePath)
                : null;
        }

        /// <summary>
        /// Convert a document URI to a local file path, or null when the URI
        /// doesn't refer to the file system.
        /// </summary>
        internal static string TryGetFileSystemPath(DocumentUri uri)
        {
            try
            {
                if (uri == null)
                    return null;
                if (!string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase))
                    return null;
                var path = uri.GetFileSystemPath();
                return string.IsNullOrEmpty(path) ? null : System.IO.Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Attach the project context used by the next semantic analysis run.
        /// </summary>
        public void SetProjectContext(LspProjectContext context)
        {
            ProjectContext = context;
            ProjectStamp = context?.Stamp;
        }

        /// <summary>
        /// Re-run semantic analysis (e.g. after a sibling project file changed)
        /// keeping the existing parse results.
        /// </summary>
        public void ReRunSemanticAnalysis()
        {
            if (AST == null || !ParseSuccessful)
                return;

            // All diagnostics on a successfully parsed document come from
            // semantic analysis; RunSemanticAnalysis publishes a fresh list by
            // reference swap, so a list concurrently enumerated by another LSP
            // thread (hover/completion/publish) is never mutated under it.
            RunSemanticAnalysis();
        }

        private static string ComputeContentHash(string content)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                var hashBytes = sha256.ComputeHash(bytes);
                return Convert.ToBase64String(hashBytes);
            }
        }

        /// <summary>
        /// Apply a cached parse result (avoids re-lexing and re-parsing)
        /// </summary>
        public void ApplyCachedResult(CachedParseResult cached)
        {
            Tokens = cached.Tokens;
            AST = cached.AST;
            ParseSuccessful = true;
            FromCache = true;

            // Still need to run semantic analysis as it may depend on other documents
            RunSemanticAnalysis();
        }

        /// <summary>
        /// Parse the document and perform semantic analysis
        /// </summary>
        public void Parse()
        {
            Diagnostics.Clear();
            ParseSuccessful = false;
            SemanticSuccessful = false;
            FromCache = false;
            ParsedAt = DateTime.UtcNow;

            try
            {
                // Lexical analysis
                var lexer = new Lexer(Content);
                Tokens = lexer.Tokenize();

                // Parsing
                var parser = new Parser(Tokens);
                AST = parser.Parse();
                ParseSuccessful = true;

                // Semantic analysis
                RunSemanticAnalysis();
            }
            catch (ParseException ex)
            {
                Diagnostics.Add(new Diagnostic
                {
                    Message = ex.Message,
                    Severity = DiagnosticSeverity.Error,
                    Line = ex.Token?.Line ?? 1,
                    Column = ex.Token?.Column ?? 1
                });
            }
            catch (Exception ex)
            {
                Diagnostics.Add(new Diagnostic
                {
                    Message = $"Internal error: {ex.Message}",
                    Severity = DiagnosticSeverity.Error,
                    Line = 1,
                    Column = 1
                });
            }
        }

        /// <summary>
        /// Run semantic analysis on the parsed AST.
        /// Results (analyzer + diagnostics) are built on locals and published
        /// by reference swap only when complete, so concurrent readers on other
        /// LSP threads see either the previous snapshot or the new one — never
        /// a half-built analyzer or a list mutated mid-enumeration.
        /// </summary>
        private void RunSemanticAnalysis()
        {
            if (AST == null) return;

            var diagnostics = new List<Diagnostic>();
            var analyzer = new SemanticAnalyzer();
            try
            {
                // Configure TypeRegistry for .NET IntelliSense
                if (TypeRegistry != null)
                {
                    analyzer.ConfigureTypeRegistry(TypeRegistry);
                }

                // Cross-file symbols: analyze against the project-wide symbol
                // table so Import directives and sibling-file references resolve.
                if (ProjectContext?.Symbols != null && !string.IsNullOrEmpty(ModuleName))
                {
                    analyzer.ConfigureProjectSymbols(ProjectContext.Symbols, ModuleName,
                        ProjectContext.IndeterminateImports);
                }

                var succeeded = analyzer.Analyze(AST);

                // Note: Even if analysis failed, the SemanticAnalyzer still
                // contains useful scope/symbol information for IntelliSense.
                // We keep the analyzer instance to provide partial completions.

                // Collect semantic errors
                foreach (var error in analyzer.Errors)
                {
                    var diag = new Diagnostic
                    {
                        Message = error.Message,
                        Severity = error.Severity == BasicLang.Compiler.SemanticAnalysis.ErrorSeverity.Warning
                            ? DiagnosticSeverity.Warning
                            : DiagnosticSeverity.Error,
                        Line = error.Line,
                        Column = error.Column
                    };

                    // Tag warnings about unused/unreferenced symbols as Unnecessary
                    if (error.Severity == BasicLang.Compiler.SemanticAnalysis.ErrorSeverity.Warning)
                    {
                        var msgLower = error.Message.ToLowerInvariant();
                        if (msgLower.Contains("unused") || msgLower.Contains("not used") || msgLower.Contains("never used") || msgLower.Contains("unreferenced"))
                        {
                            diag.Tags.Add(DiagnosticTag.Unnecessary);
                        }
                        if (msgLower.Contains("deprecated") || msgLower.Contains("obsolete"))
                        {
                            diag.Tags.Add(DiagnosticTag.Deprecated);
                        }
                    }

                    diagnostics.Add(diag);
                }

                // Detect unused local variables by scanning symbol table scopes
                DetectUnusedSymbols(analyzer, diagnostics);

                SemanticSuccessful = succeeded;
            }
            catch (Exception ex)
            {
                // Even on exception, keep the partial semantic info gathered so far
                diagnostics.Add(new Diagnostic
                {
                    Message = $"Semantic analysis error: {ex.Message}",
                    Severity = DiagnosticSeverity.Error,
                    Line = 1,
                    Column = 1
                });
            }

            // Publish complete results with single reference swaps
            SemanticAnalyzer = analyzer;
            Diagnostics = diagnostics;
        }

        /// <summary>
        /// Get the word at a specific position
        /// </summary>
        public string GetWordAtPosition(int line, int character)
        {
            if (line < 0 || line >= Lines.Length)
                return null;

            var lineText = Lines[line];
            if (character < 0 || character >= lineText.Length)
                return null;

            // Find word boundaries
            int start = character;
            int end = character;

            while (start > 0 && IsIdentifierChar(lineText[start - 1]))
                start--;

            while (end < lineText.Length && IsIdentifierChar(lineText[end]))
                end++;

            if (start == end)
                return null;

            return lineText.Substring(start, end - start);
        }

        /// <summary>
        /// Get the token at a specific position
        /// </summary>
        public Token GetTokenAtPosition(int line, int character)
        {
            // Lines in LSP are 0-based, but our tokens are 1-based
            int targetLine = line + 1;

            foreach (var token in Tokens)
            {
                if (token.Line == targetLine)
                {
                    int tokenEnd = token.Column + (token.Lexeme?.Length ?? 0);
                    if (character >= token.Column - 1 && character < tokenEnd - 1)
                    {
                        return token;
                    }
                }
            }

            return null;
        }

        private bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        /// <summary>
        /// Detect unused local variables and imports by scanning symbol scopes
        /// and checking if names appear in source beyond their declaration.
        /// Operates on the in-flight analyzer/diagnostics locals so results are
        /// only published when the whole analysis pass completes.
        /// </summary>
        private void DetectUnusedSymbols(SemanticAnalyzer analyzer, List<Diagnostic> diagnostics)
        {
            if (analyzer?.GlobalScope == null || Content == null)
                return;

            try
            {
                // Collect local variables from all scopes (skip global-level functions, classes, etc.)
                var localVariables = new List<Symbol>();
                CollectLocalVariables(analyzer.GlobalScope, localVariables);

                foreach (var variable in localVariables)
                {
                    if (string.IsNullOrEmpty(variable.Name) || variable.Line <= 0)
                        continue;

                    // Skip loop variables, parameters, and compiler-generated symbols
                    if (variable.Kind == SymbolKind.Parameter)
                        continue;
                    if (variable.Name.StartsWith("_") && variable.Name.Length == 1)
                        continue;

                    // Count how many times the identifier appears in tokens (excluding the declaration itself)
                    int referenceCount = 0;
                    foreach (var token in Tokens)
                    {
                        if (token.Lexeme == variable.Name &&
                            token.Type == TokenType.Identifier &&
                            !(token.Line == variable.Line && token.Column == variable.Column))
                        {
                            referenceCount++;
                        }
                    }

                    if (referenceCount == 0)
                    {
                        // Check if we already have a diagnostic for this variable at the same location
                        bool alreadyReported = diagnostics.Any(d =>
                            d.Line == variable.Line &&
                            d.Column == variable.Column &&
                            d.Tags.Contains(DiagnosticTag.Unnecessary));

                        if (!alreadyReported)
                        {
                            diagnostics.Add(new Diagnostic
                            {
                                Message = $"Variable '{variable.Name}' is declared but never used",
                                Severity = DiagnosticSeverity.Hint,
                                Line = variable.Line,
                                Column = variable.Column,
                                EndLine = variable.Line,
                                EndColumn = variable.Column + variable.Name.Length,
                                Tags = new List<DiagnosticTag> { DiagnosticTag.Unnecessary }
                            });
                        }
                    }
                }

                // Detect unused imports by checking Using/Import declarations in the AST
                if (AST?.Declarations != null)
                {
                    foreach (var decl in AST.Declarations)
                    {
                        if (decl is UsingDirectiveNode usingDir && !string.IsNullOrEmpty(usingDir.Namespace))
                        {
                            // Check if any part of the namespace is referenced in the source
                            var nsParts = usingDir.Namespace.Split('.');
                            var lastPart = nsParts.Last();

                            // Check if the imported namespace/type name appears elsewhere in tokens
                            bool isUsed = Tokens.Any(t =>
                                t.Type == TokenType.Identifier &&
                                t.Lexeme == lastPart &&
                                t.Line != usingDir.Line);

                            if (!isUsed)
                            {
                                diagnostics.Add(new Diagnostic
                                {
                                    Message = $"Import '{usingDir.Namespace}' is not used",
                                    Severity = DiagnosticSeverity.Hint,
                                    Line = usingDir.Line > 0 ? usingDir.Line : 1,
                                    Column = 1,
                                    Tags = new List<DiagnosticTag> { DiagnosticTag.Unnecessary }
                                });
                            }
                        }
                    }
                }
            }
            catch
            {
                // Don't let unused detection failures break the diagnostics pipeline
            }
        }

        /// <summary>
        /// Recursively collect local variable symbols from scopes
        /// </summary>
        private void CollectLocalVariables(Scope scope, List<Symbol> variables)
        {
            if (scope == null)
                return;

            foreach (var symbol in scope.Symbols.Values)
            {
                if (symbol.Kind == SymbolKind.Variable && !symbol.IsImported)
                {
                    variables.Add(symbol);
                }
            }

            if (scope.Children != null)
            {
                foreach (var child in scope.Children)
                {
                    CollectLocalVariables(child, variables);
                }
            }
        }
    }

    /// <summary>
    /// Simple diagnostic class for internal use
    /// </summary>
    public class Diagnostic
    {
        public string Message { get; set; }
        public DiagnosticSeverity Severity { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public int EndLine { get; set; }
        public int EndColumn { get; set; }
        public List<DiagnosticTag> Tags { get; set; } = new List<DiagnosticTag>();
    }

    public enum DiagnosticSeverity
    {
        Error = 1,
        Warning = 2,
        Information = 3,
        Hint = 4
    }

    public enum DiagnosticTag
    {
        Unnecessary = 1,
        Deprecated = 2
    }
}
