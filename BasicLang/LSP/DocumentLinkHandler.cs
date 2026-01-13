using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles document link requests (clickable links for imports, includes, file paths, and URLs)
    /// </summary>
    public class DocumentLinkHandler : IDocumentLinkHandler
    {
        private readonly DocumentManager _documentManager;

        // Regex patterns for link detection
        private static readonly Regex UrlPattern = new Regex(
            @"https?://[^\s\)<>""']+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex FilePathPattern = new Regex(
            @"""([A-Za-z]:[\\\/](?:[^""\\]|\\.)+|\.{1,2}[\\\/](?:[^""\\]|\\.)+)""",
            RegexOptions.Compiled);

        public DocumentLinkHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Task<DocumentLinkContainer?> Handle(DocumentLinkParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult(new DocumentLinkContainer());
            }

            var links = new List<DocumentLink>();

            // Get the directory of the current document for resolving relative paths
            var documentPath = GetDocumentPath(request.TextDocument.Uri);
            var documentDirectory = !string.IsNullOrEmpty(documentPath) ? Path.GetDirectoryName(documentPath) : null;

            // Scan through all lines
            for (int lineNum = 0; lineNum < state.Lines.Length; lineNum++)
            {
                var line = state.Lines[lineNum];

                // 1. Check for Import/Imports statements
                AddImportLinks(line, lineNum, documentDirectory, links);

                // 2. Check for file path strings
                AddFilePathLinks(line, lineNum, documentDirectory, links);

                // 3. Check for URLs in comments
                if (IsCommentLine(line))
                {
                    AddUrlLinks(line, lineNum, links);
                }
            }

            // Also check AST for Import directives
            AddAstImportLinks(state, documentDirectory, links);

            return Task.FromResult(new DocumentLinkContainer(links));
        }

        /// <summary>
        /// Scan for Import/Imports statements in the line
        /// </summary>
        private void AddImportLinks(string line, int lineNum, string documentDirectory, List<DocumentLink> links)
        {
            // Pattern: Import "filename.bas" or Imports "filename.bas"
            var importPattern = @"\b(?:Import|Imports)\s+""([^""]+)""";
            var matches = Regex.Matches(line, importPattern, RegexOptions.IgnoreCase);

            foreach (Match match in matches)
            {
                var fileName = match.Groups[1].Value;
                var uri = ResolveFileUri(fileName, documentDirectory);

                if (uri != null)
                {
                    var startColumn = match.Groups[1].Index - 1; // -1 for the opening quote
                    var endColumn = startColumn + fileName.Length + 2; // +2 for both quotes

                    links.Add(new DocumentLink
                    {
                        Range = new LspRange(
                            new Position(lineNum, startColumn),
                            new Position(lineNum, endColumn)),
                        Target = uri,
                        Tooltip = $"Open {fileName}"
                    });
                }
            }
        }

        /// <summary>
        /// Scan for file path strings in the line
        /// </summary>
        private void AddFilePathLinks(string line, int lineNum, string documentDirectory, List<DocumentLink> links)
        {
            var matches = FilePathPattern.Matches(line);

            foreach (Match match in matches)
            {
                var filePath = match.Groups[1].Value;

                // Skip if this is part of an Import statement (already handled)
                if (IsPartOfImportStatement(line, match.Index))
                {
                    continue;
                }

                var uri = ResolveFileUri(filePath, documentDirectory);

                if (uri != null)
                {
                    var startColumn = match.Index;
                    var endColumn = startColumn + match.Length;

                    links.Add(new DocumentLink
                    {
                        Range = new LspRange(
                            new Position(lineNum, startColumn),
                            new Position(lineNum, endColumn)),
                        Target = uri,
                        Tooltip = $"Open {filePath}"
                    });
                }
            }
        }

        /// <summary>
        /// Scan for URLs in comments
        /// </summary>
        private void AddUrlLinks(string line, int lineNum, List<DocumentLink> links)
        {
            var matches = UrlPattern.Matches(line);

            foreach (Match match in matches)
            {
                var url = match.Value;
                var startColumn = match.Index;
                var endColumn = startColumn + url.Length;

                try
                {
                    links.Add(new DocumentLink
                    {
                        Range = new LspRange(
                            new Position(lineNum, startColumn),
                            new Position(lineNum, endColumn)),
                        Target = DocumentUri.From(url),
                        Tooltip = $"Open {url}"
                    });
                }
                catch
                {
                    // Invalid URL, skip
                }
            }
        }

        /// <summary>
        /// Add links from AST Import directives
        /// </summary>
        private void AddAstImportLinks(DocumentState state, string documentDirectory, List<DocumentLink> links)
        {
            if (state?.AST == null) return;

            foreach (var decl in state.AST.Declarations)
            {
                if (decl is BasicLang.Compiler.AST.ImportDirectiveNode importNode)
                {
                    var fileName = importNode.Module;

                    // Try to add .bas extension if not present
                    if (!Path.HasExtension(fileName))
                    {
                        fileName += ".bas";
                    }

                    var uri = ResolveFileUri(fileName, documentDirectory);

                    if (uri != null)
                    {
                        // Note: Line/Column in AST are 1-based, LSP uses 0-based
                        var line = Math.Max(0, importNode.Line - 1);
                        var column = Math.Max(0, importNode.Column - 1);

                        // Find the module name in the line to get accurate range
                        if (line < state.Lines.Length)
                        {
                            var lineText = state.Lines[line];
                            var moduleIndex = lineText.IndexOf(importNode.Module, StringComparison.OrdinalIgnoreCase);

                            if (moduleIndex >= 0)
                            {
                                links.Add(new DocumentLink
                                {
                                    Range = new LspRange(
                                        new Position(line, moduleIndex),
                                        new Position(line, moduleIndex + importNode.Module.Length)),
                                    Target = uri,
                                    Tooltip = $"Open {fileName}"
                                });
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolve a file path to a DocumentUri
        /// </summary>
        private DocumentUri ResolveFileUri(string filePath, string documentDirectory)
        {
            try
            {
                string fullPath = null;

                // Handle absolute paths
                if (Path.IsPathRooted(filePath))
                {
                    fullPath = filePath;
                }
                // Handle relative paths
                else if (documentDirectory != null)
                {
                    fullPath = Path.GetFullPath(Path.Combine(documentDirectory, filePath));
                }

                // Check if file exists
                if (fullPath != null && File.Exists(fullPath))
                {
                    return DocumentUri.FromFileSystemPath(fullPath);
                }
            }
            catch
            {
                // Invalid path, return null
            }

            return null;
        }

        /// <summary>
        /// Get the file system path from a DocumentUri
        /// </summary>
        private string GetDocumentPath(DocumentUri uri)
        {
            try
            {
                var uriString = uri.ToString();

                // Handle file:// URIs
                if (uriString.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    var path = Uri.UnescapeDataString(uriString.Substring(7));

                    // Handle Windows paths (file:///C:/path)
                    if (path.StartsWith("/") && path.Length > 2 && path[2] == ':')
                    {
                        path = path.Substring(1);
                    }

                    return path.Replace('/', Path.DirectorySeparatorChar);
                }
            }
            catch
            {
                // Invalid URI, return null
            }

            return null;
        }

        /// <summary>
        /// Check if a line is a comment
        /// </summary>
        private bool IsCommentLine(string line)
        {
            var trimmed = line.TrimStart();
            return trimmed.StartsWith("'");
        }

        /// <summary>
        /// Check if a position in the line is part of an Import statement
        /// </summary>
        private bool IsPartOfImportStatement(string line, int position)
        {
            // Look backwards from position to see if we find "Import" or "Imports"
            var beforeMatch = line.Substring(0, position);
            return Regex.IsMatch(beforeMatch, @"\b(?:Import|Imports)\s+$", RegexOptions.IgnoreCase);
        }

        public DocumentLinkRegistrationOptions GetRegistrationOptions(
            DocumentLinkCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DocumentLinkRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                ResolveProvider = false
            };
        }
    }
}
