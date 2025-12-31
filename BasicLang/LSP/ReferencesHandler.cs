using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Handles find all references requests
    /// </summary>
    public class ReferencesHandler : ReferencesHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public ReferencesHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<LocationContainer> Handle(ReferenceParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<LocationContainer>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<LocationContainer>(null);
            }

            var locations = new List<Location>();

            // Check if this is a public symbol to determine search scope
            bool isPublicSymbol = IsPublicSymbol(state, word);

            // Get all documents to search (all if public, otherwise just current)
            var documentsToSearch = isPublicSymbol
                ? _documentManager.GetAllDocuments()
                : new[] { state };

            // Search through all relevant documents for references
            foreach (var docState in documentsToSearch)
            {
                if (docState?.Tokens == null) continue;

                foreach (var token in docState.Tokens)
                {
                    if (token.Type == TokenType.Identifier &&
                        string.Equals(token.Lexeme, word, StringComparison.OrdinalIgnoreCase))
                    {
                        locations.Add(new Location
                        {
                            Uri = docState.Uri,
                            Range = new LspRange(
                                new Position(token.Line - 1, token.Column - 1),
                                new Position(token.Line - 1, token.Column - 1 + token.Lexeme.Length))
                        });
                    }
                }

                // If includeDeclaration is true, also search for declarations
                if (request.Context?.IncludeDeclaration == true && docState.AST != null)
                {
                    var declLocation = FindDeclarationLocation(docState, word);
                    if (declLocation != null && !locations.Any(l =>
                        l.Uri == declLocation.Uri &&
                        l.Range.Start.Line == declLocation.Range.Start.Line &&
                        l.Range.Start.Character == declLocation.Range.Start.Character))
                    {
                        locations.Insert(0, declLocation);
                    }
                }
            }

            return Task.FromResult(new LocationContainer(locations));
        }

        private bool IsPublicSymbol(DocumentState state, string name)
        {
            if (state?.AST == null) return false;

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

        private Location FindDeclarationLocation(DocumentState state, string word)
        {
            foreach (var decl in state.AST.Declarations)
            {
                var location = FindDeclarationInNode(state, decl, word);
                if (location != null)
                    return location;
            }
            return null;
        }

        private Location FindDeclarationInNode(DocumentState state, ASTNode node, string word)
        {
            int line = -1;
            int column = -1;
            int length = word.Length;

            switch (node)
            {
                case FunctionNode func when func.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    line = func.Line;
                    column = func.Column;
                    break;

                case SubroutineNode sub when sub.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    line = sub.Line;
                    column = sub.Column;
                    break;

                case ClassNode cls when cls.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    line = cls.Line;
                    column = cls.Column;
                    break;

                case VariableDeclarationNode varDecl when varDecl.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    line = varDecl.Line;
                    column = varDecl.Column;
                    break;

                case ConstantDeclarationNode constDecl when constDecl.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    line = constDecl.Line;
                    column = constDecl.Column;
                    break;

                case PropertyNode prop when prop.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    line = prop.Line;
                    column = prop.Column;
                    break;
            }

            if (line > 0)
            {
                return new Location
                {
                    Uri = state.Uri,
                    Range = new LspRange(
                        new Position(line - 1, column - 1),
                        new Position(line - 1, column - 1 + length))
                };
            }

            // Search nested declarations
            switch (node)
            {
                case ClassNode cls:
                    foreach (var member in cls.Members)
                    {
                        var memberLoc = FindDeclarationInNode(state, member, word);
                        if (memberLoc != null)
                            return memberLoc;
                    }
                    break;

                case FunctionNode func:
                    foreach (var param in func.Parameters)
                    {
                        if (param.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            return new Location
                            {
                                Uri = state.Uri,
                                Range = new LspRange(
                                    new Position(param.Line - 1, param.Column - 1),
                                    new Position(param.Line - 1, param.Column - 1 + param.Name.Length))
                            };
                        }
                    }
                    break;

                case SubroutineNode sub:
                    foreach (var param in sub.Parameters)
                    {
                        if (param.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            return new Location
                            {
                                Uri = state.Uri,
                                Range = new LspRange(
                                    new Position(param.Line - 1, param.Column - 1),
                                    new Position(param.Line - 1, param.Column - 1 + param.Name.Length))
                            };
                        }
                    }
                    break;
            }

            return null;
        }

        protected override ReferenceRegistrationOptions CreateRegistrationOptions(
            ReferenceCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new ReferenceRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
