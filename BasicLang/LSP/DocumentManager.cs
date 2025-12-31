using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            // Check if document exists and content hasn't changed
            if (_documents.TryGetValue(uri, out var existingState))
            {
                if (existingState.ContentHash == ComputeHash(content))
                {
                    // Content unchanged, return cached state
                    return existingState;
                }
            }

            // Create new state and try to use cached parse results
            var state = new DocumentState(uri, content);
            state.TypeRegistry = TypeRegistry; // Share TypeRegistry across documents
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
        /// Run semantic analysis on the parsed AST
        /// </summary>
        private void RunSemanticAnalysis()
        {
            if (AST == null) return;

            try
            {
                SemanticAnalyzer = new SemanticAnalyzer();

                // Configure TypeRegistry for .NET IntelliSense
                if (TypeRegistry != null)
                {
                    SemanticAnalyzer.ConfigureTypeRegistry(TypeRegistry);
                }

                SemanticSuccessful = SemanticAnalyzer.Analyze(AST);

                // Collect semantic errors
                foreach (var error in SemanticAnalyzer.Errors)
                {
                    Diagnostics.Add(new Diagnostic
                    {
                        Message = error.Message,
                        Severity = error.Severity == BasicLang.Compiler.SemanticAnalysis.ErrorSeverity.Warning
                            ? DiagnosticSeverity.Warning
                            : DiagnosticSeverity.Error,
                        Line = error.Line,
                        Column = error.Column
                    });
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Add(new Diagnostic
                {
                    Message = $"Semantic analysis error: {ex.Message}",
                    Severity = DiagnosticSeverity.Error,
                    Line = 1,
                    Column = 1
                });
            }
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
    }

    public enum DiagnosticSeverity
    {
        Error = 1,
        Warning = 2,
        Information = 3,
        Hint = 4
    }
}
