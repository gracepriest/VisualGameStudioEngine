using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.AST;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles document highlight requests - highlights all occurrences of a symbol in the current document
    /// </summary>
    public class DocumentHighlightHandler : DocumentHighlightHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public DocumentHighlightHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<DocumentHighlightContainer?> Handle(
            DocumentHighlightParams request,
            CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<DocumentHighlightContainer>(null);
            }

            // Get the word at the cursor position
            var word = state.GetWordAtPosition(request.Position.Line, request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<DocumentHighlightContainer>(null);
            }

            var highlights = new List<DocumentHighlight>();

            // Find the declaration location to mark it differently
            var declarationLocation = FindDeclarationLocation(state, word);

            // Find all occurrences in tokens
            if (state.Tokens != null)
            {
                foreach (var token in state.Tokens)
                {
                    if (token.Type == TokenType.Identifier &&
                        string.Equals(token.Lexeme, word, StringComparison.OrdinalIgnoreCase))
                    {
                        var range = new LspRange(
                            new Position(token.Line - 1, token.Column - 1),
                            new Position(token.Line - 1, token.Column - 1 + token.Lexeme.Length));

                        // Determine if this is a write or read reference
                        var kind = DetermineHighlightKind(state, token, word, declarationLocation);

                        highlights.Add(new DocumentHighlight
                        {
                            Range = range,
                            Kind = kind
                        });
                    }
                }
            }

            // Also check keywords and built-in functions
            if (IsKeywordOrBuiltIn(word))
            {
                // For keywords, highlight all occurrences
                if (state.Tokens != null)
                {
                    foreach (var token in state.Tokens)
                    {
                        if (string.Equals(token.Lexeme, word, StringComparison.OrdinalIgnoreCase))
                        {
                            var range = new LspRange(
                                new Position(token.Line - 1, token.Column - 1),
                                new Position(token.Line - 1, token.Column - 1 + token.Lexeme.Length));

                            highlights.Add(new DocumentHighlight
                            {
                                Range = range,
                                Kind = DocumentHighlightKind.Text
                            });
                        }
                    }
                }
            }

            return Task.FromResult(new DocumentHighlightContainer(highlights));
        }

        private DocumentHighlightKind DetermineHighlightKind(
            DocumentState state,
            Token token,
            string word,
            (int line, int column)? declarationLocation)
        {
            // Check if this is the declaration
            if (declarationLocation.HasValue &&
                token.Line == declarationLocation.Value.line &&
                token.Column == declarationLocation.Value.column)
            {
                return DocumentHighlightKind.Write; // Declaration is a "write"
            }

            // Check if this is an assignment target
            if (IsAssignmentTarget(state, token))
            {
                return DocumentHighlightKind.Write;
            }

            // Otherwise it's a read
            return DocumentHighlightKind.Read;
        }

        private bool IsAssignmentTarget(DocumentState state, Token token)
        {
            if (state.Tokens == null) return false;

            // Find the token index
            int tokenIndex = -1;
            for (int i = 0; i < state.Tokens.Count; i++)
            {
                if (state.Tokens[i] == token)
                {
                    tokenIndex = i;
                    break;
                }
            }

            if (tokenIndex < 0) return false;

            // Check if the next non-whitespace token is an assignment operator
            for (int i = tokenIndex + 1; i < state.Tokens.Count; i++)
            {
                var nextToken = state.Tokens[i];
                if (nextToken.Type == TokenType.Newline) break;

                if (nextToken.Type == TokenType.Equals ||
                    nextToken.Type == TokenType.PlusAssign ||
                    nextToken.Type == TokenType.MinusAssign ||
                    nextToken.Type == TokenType.MultiplyAssign ||
                    nextToken.Type == TokenType.DivideAssign)
                {
                    return true;
                }

                // If we hit another token (not a continuation of the identifier), stop
                break;
            }

            return false;
        }

        private (int line, int column)? FindDeclarationLocation(DocumentState state, string word)
        {
            if (state?.AST == null) return null;

            foreach (var decl in state.AST.Declarations)
            {
                var location = FindDeclarationInNode(decl, word);
                if (location.HasValue) return location;
            }

            return null;
        }

        private (int line, int column)? FindDeclarationInNode(ASTNode node, string word)
        {
            switch (node)
            {
                case FunctionNode func when func.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return (func.Line, func.Column);

                case SubroutineNode sub when sub.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return (sub.Line, sub.Column);

                case ClassNode cls when cls.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return (cls.Line, cls.Column);

                case VariableDeclarationNode varDecl when varDecl.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return (varDecl.Line, varDecl.Column);

                case ConstantDeclarationNode constDecl when constDecl.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return (constDecl.Line, constDecl.Column);

                case PropertyNode prop when prop.Name.Equals(word, StringComparison.OrdinalIgnoreCase):
                    return (prop.Line, prop.Column);

                case ClassNode cls:
                    // Search members
                    foreach (var member in cls.Members)
                    {
                        var location = FindDeclarationInNode(member, word);
                        if (location.HasValue) return location;
                    }
                    break;

                case FunctionNode func:
                    // Search parameters
                    foreach (var param in func.Parameters)
                    {
                        if (param.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            return (param.Line, param.Column);
                        }
                    }
                    break;

                case SubroutineNode sub:
                    // Search parameters
                    foreach (var param in sub.Parameters)
                    {
                        if (param.Name.Equals(word, StringComparison.OrdinalIgnoreCase))
                        {
                            return (param.Line, param.Column);
                        }
                    }
                    break;
            }

            return null;
        }

        private bool IsKeywordOrBuiltIn(string word)
        {
            var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Dim", "Const", "Function", "Sub", "End", "If", "Then", "Else", "ElseIf",
                "For", "To", "Step", "Next", "While", "Wend", "Do", "Loop", "Until",
                "Select", "Case", "Class", "Inherits", "Implements", "Public", "Private",
                "Protected", "Friend", "Static", "Shared", "Return", "Exit", "Continue",
                "Try", "Catch", "Finally", "Throw", "New", "Me", "MyBase", "MyClass",
                "Nothing", "True", "False", "And", "Or", "Not", "Xor", "Mod", "Like",
                "Is", "IsNot", "TypeOf", "As", "ByVal", "ByRef", "Optional", "ParamArray"
            };

            var builtInFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Print", "PrintLine", "Input", "Len", "Mid", "Left", "Right", "Trim",
                "LTrim", "RTrim", "UCase", "LCase", "Str", "Val", "CInt", "CDbl",
                "CBool", "CStr", "Abs", "Sqrt", "Sin", "Cos", "Tan", "Log", "Exp",
                "Rnd", "Int", "Fix", "Sgn", "Asc", "Chr", "InStr", "Replace",
                "Split", "Join", "UBound", "LBound", "Array", "CreateList",
                "CreateDictionary", "CreateHashSet"
            };

            return keywords.Contains(word) || builtInFunctions.Contains(word);
        }

        protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(
            DocumentHighlightCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DocumentHighlightRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
