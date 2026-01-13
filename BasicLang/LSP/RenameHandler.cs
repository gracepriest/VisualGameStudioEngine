using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.AST;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles rename symbol requests
    /// </summary>
    public class RenameHandler : RenameHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public RenameHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<WorkspaceEdit>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<WorkspaceEdit>(null);
            }

            var newName = request.NewName;
            var allChanges = new Dictionary<DocumentUri, IEnumerable<TextEdit>>();

            // Check if this is a public/exported symbol (class, function, module-level variable)
            bool isPublicSymbol = IsPublicSymbol(state, word);

            // Get all open documents if it's a public symbol, otherwise just the current document
            var documentsToSearch = isPublicSymbol
                ? _documentManager.GetAllDocuments()
                : new[] { state };

            // Find all references across all relevant documents
            foreach (var docState in documentsToSearch)
            {
                if (docState?.Tokens == null) continue;

                var edits = new List<TextEdit>();

                foreach (var token in docState.Tokens)
                {
                    if (token.Type == TokenType.Identifier &&
                        string.Equals(token.Lexeme, word, StringComparison.OrdinalIgnoreCase))
                    {
                        edits.Add(new TextEdit
                        {
                            Range = new LspRange(
                                new Position(token.Line - 1, token.Column - 1),
                                new Position(token.Line - 1, token.Column - 1 + token.Lexeme.Length)),
                            NewText = newName
                        });
                    }
                }

                if (edits.Count > 0)
                {
                    allChanges[docState.Uri] = edits;
                }
            }

            if (allChanges.Count == 0)
            {
                return Task.FromResult<WorkspaceEdit>(null);
            }

            var workspaceEdit = new WorkspaceEdit
            {
                Changes = allChanges
            };

            return Task.FromResult(workspaceEdit);
        }

        private bool IsPublicSymbol(DocumentState state, string name)
        {
            if (state?.AST == null) return false;

            // Check if the symbol is defined as a public class, function, or subroutine
            foreach (var decl in state.AST.Declarations)
            {
                switch (decl)
                {
                    case ClassNode cls when cls.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                        return true; // Classes are typically public/accessible
                    case FunctionNode func when func.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                        return func.Access == AccessModifier.Public;
                    case SubroutineNode sub when sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                        return sub.Access == AccessModifier.Public;
                }
            }
            return false;
        }

        protected override RenameRegistrationOptions CreateRegistrationOptions(
            RenameCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new RenameRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                PrepareProvider = true
            };
        }
    }

    /// <summary>
    /// Handles prepare rename requests (validates if rename is possible)
    /// </summary>
    public class PrepareRenameHandler : PrepareRenameHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public PrepareRenameHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<RangeOrPlaceholderRange>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<RangeOrPlaceholderRange>(null);
            }

            // Find the token at this position
            var token = state.GetTokenAtPosition(request.Position.Line, request.Position.Character);
            if (token == null || token.Type != TokenType.Identifier)
            {
                return Task.FromResult<RangeOrPlaceholderRange>(null);
            }

            // Return the range and placeholder
            var range = new LspRange(
                new Position(token.Line - 1, token.Column - 1),
                new Position(token.Line - 1, token.Column - 1 + token.Lexeme.Length));

            return Task.FromResult<RangeOrPlaceholderRange>(new RangeOrPlaceholderRange(
                new PlaceholderRange
                {
                    Range = range,
                    Placeholder = word
                }));
        }

        protected override RenameRegistrationOptions CreateRegistrationOptions(
            RenameCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new RenameRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                PrepareProvider = true
            };
        }
    }
}
