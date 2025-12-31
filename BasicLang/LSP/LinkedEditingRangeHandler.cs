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
    /// Handles linked editing range requests for synchronized variable renaming
    /// </summary>
    public class LinkedEditingRangeHandler : LinkedEditingRangeHandlerBase
    {
        private readonly DocumentManager _documentManager;
        private DocumentState _currentState;

        public LinkedEditingRangeHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        protected override LinkedEditingRangeRegistrationOptions CreateRegistrationOptions(
            LinkedEditingRangeClientCapabilities capability,
            ClientCapabilities clientCapabilities)
        {
            return new LinkedEditingRangeRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }

        public override Task<LinkedEditingRanges> Handle(LinkedEditingRangeParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<LinkedEditingRanges>(null);
            }
            _currentState = state;

            // Get the word at the cursor position
            var word = state.GetWordAtPosition((int)request.Position.Line, (int)request.Position.Character);
            if (string.IsNullOrEmpty(word))
            {
                return Task.FromResult<LinkedEditingRanges>(null);
            }

            // Get the token at the cursor to determine context
            var token = state.GetTokenAtPosition((int)request.Position.Line, (int)request.Position.Character);
            if (token == null || token.Type != TokenType.Identifier)
            {
                return Task.FromResult<LinkedEditingRanges>(null);
            }

            // Check if this is a local variable, parameter, or function/sub name
            var scope = DetermineScope(state, word, (int)request.Position.Line);
            if (scope == null)
            {
                return Task.FromResult<LinkedEditingRanges>(null);
            }

            // Find all occurrences of the identifier within the scope
            var ranges = FindOccurrencesInScope(state, word, scope);
            if (ranges.Count < 2) // Need at least 2 occurrences to make linked editing useful
            {
                return Task.FromResult<LinkedEditingRanges>(null);
            }

            return Task.FromResult(new LinkedEditingRanges
            {
                Ranges = ranges.ToArray(),
                WordPattern = @"[a-zA-Z_][a-zA-Z0-9_]*" // Identifier pattern
            });
        }

        private ScopeInfo DetermineScope(DocumentState state, string identifier, int line)
        {
            if (state.AST == null)
                return null;

            // Search through declarations to find the scope
            foreach (var declaration in state.AST.Declarations)
            {
                var scope = FindScopeInNode(declaration, identifier, line);
                if (scope != null)
                    return scope;
            }

            return null;
        }

        private ScopeInfo FindScopeInNode(ASTNode node, string identifier, int targetLine)
        {
            switch (node)
            {
                case FunctionNode func:
                    // Check if identifier is the function name
                    if (func.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ScopeInfo
                        {
                            StartLine = func.Line - 1,
                            EndLine = FindEndLine(func.Line - 1, "End Function"),
                            ScopeType = ScopeType.Function,
                            IncludeDeclaration = true
                        };
                    }

                    // Check if identifier is a parameter
                    if (func.Parameters.Exists(p => p.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new ScopeInfo
                        {
                            StartLine = func.Line - 1,
                            EndLine = FindEndLine(func.Line - 1, "End Function"),
                            ScopeType = ScopeType.Function,
                            IncludeDeclaration = true
                        };
                    }

                    // Check if identifier is a local variable within this function
                    if (func.Body != null && IsVariableDeclaredInBlock(func.Body, identifier))
                    {
                        return new ScopeInfo
                        {
                            StartLine = func.Line - 1,
                            EndLine = FindEndLine(func.Line - 1, "End Function"),
                            ScopeType = ScopeType.Function,
                            IncludeDeclaration = true
                        };
                    }
                    break;

                case SubroutineNode sub:
                    // Check if identifier is the subroutine name
                    if (sub.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ScopeInfo
                        {
                            StartLine = sub.Line - 1,
                            EndLine = FindEndLine(sub.Line - 1, "End Sub"),
                            ScopeType = ScopeType.Subroutine,
                            IncludeDeclaration = true
                        };
                    }

                    // Check if identifier is a parameter
                    if (sub.Parameters.Exists(p => p.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new ScopeInfo
                        {
                            StartLine = sub.Line - 1,
                            EndLine = FindEndLine(sub.Line - 1, "End Sub"),
                            ScopeType = ScopeType.Subroutine,
                            IncludeDeclaration = true
                        };
                    }

                    // Check if identifier is a local variable within this subroutine
                    if (sub.Body != null && IsVariableDeclaredInBlock(sub.Body, identifier))
                    {
                        return new ScopeInfo
                        {
                            StartLine = sub.Line - 1,
                            EndLine = FindEndLine(sub.Line - 1, "End Sub"),
                            ScopeType = ScopeType.Subroutine,
                            IncludeDeclaration = true
                        };
                    }
                    break;

                case ClassNode cls:
                    // Check members
                    foreach (var member in cls.Members)
                    {
                        var scope = FindScopeInNode(member, identifier, targetLine);
                        if (scope != null)
                            return scope;
                    }

                    // Check if it's a property
                    if (cls.Members.Exists(m =>
                        m is PropertyNode prop && prop.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase)))
                    {
                        return new ScopeInfo
                        {
                            StartLine = cls.Line - 1,
                            EndLine = FindEndLine(cls.Line - 1, "End Class"),
                            ScopeType = ScopeType.Class,
                            IncludeDeclaration = true
                        };
                    }
                    break;

                case ModuleNode module:
                    // Check members
                    foreach (var member in module.Members)
                    {
                        var scope = FindScopeInNode(member, identifier, targetLine);
                        if (scope != null)
                            return scope;
                    }
                    break;
            }

            return null;
        }

        private bool IsVariableDeclaredInBlock(BlockNode block, string identifier)
        {
            if (block == null)
                return false;

            foreach (var statement in block.Statements)
            {
                if (statement is VariableDeclarationNode varDecl &&
                    varDecl.Name.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Check nested blocks
                if (statement is IfStatementNode ifStmt)
                {
                    if (IsVariableDeclaredInBlock(ifStmt.ThenBlock, identifier))
                        return true;
                    if (IsVariableDeclaredInBlock(ifStmt.ElseBlock, identifier))
                        return true;
                }
                else if (statement is ForLoopNode forLoop)
                {
                    if (forLoop.Variable.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                else if (statement is WhileLoopNode whileLoop)
                {
                    if (IsVariableDeclaredInBlock(whileLoop.Body, identifier))
                        return true;
                }
                else if (statement is DoLoopNode doLoop)
                {
                    if (IsVariableDeclaredInBlock(doLoop.Body, identifier))
                        return true;
                }
            }

            return false;
        }

        private List<LspRange> FindOccurrencesInScope(DocumentState state, string identifier, ScopeInfo scope)
        {
            var ranges = new List<LspRange>();

            if (state?.Tokens == null || scope == null)
                return ranges;

            foreach (var token in state.Tokens)
            {
                if (token.Type == TokenType.Identifier &&
                    string.Equals(token.Lexeme, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    int tokenLine = token.Line - 1; // Convert to 0-based

                    // Check if token is within the scope
                    if (tokenLine >= scope.StartLine && tokenLine <= scope.EndLine)
                    {
                        ranges.Add(new LspRange(
                            new Position(tokenLine, token.Column - 1),
                            new Position(tokenLine, token.Column - 1 + token.Lexeme.Length)));
                    }
                }
            }

            return ranges;
        }

        private int FindEndLine(int startLine, string endKeyword)
        {
            if (_currentState?.Tokens == null)
                return startLine + 100;

            // Map end keyword to token types
            TokenType? startType = null;
            TokenType? endType = null;

            switch (endKeyword.ToLower().Replace(" ", ""))
            {
                case "endfunction":
                    startType = TokenType.Function;
                    endType = TokenType.EndFunction;
                    break;
                case "endsub":
                    startType = TokenType.Sub;
                    endType = TokenType.EndSub;
                    break;
                case "endclass":
                    startType = TokenType.Class;
                    endType = TokenType.EndClass;
                    break;
                case "endif":
                    startType = TokenType.If;
                    endType = TokenType.EndIf;
                    break;
                case "endselect":
                    startType = TokenType.Select;
                    endType = TokenType.EndSelect;
                    break;
            }

            if (!startType.HasValue || !endType.HasValue)
                return startLine + 100;

            // Track nesting level for proper matching
            int nestingLevel = 1;

            foreach (var token in _currentState.Tokens)
            {
                int tokenLine = token.Line - 1; // Convert to 0-based

                if (tokenLine <= startLine)
                    continue;

                // Check for nested start keywords
                if (token.Type == startType.Value)
                {
                    nestingLevel++;
                }

                // Check for end keyword
                if (token.Type == endType.Value)
                {
                    nestingLevel--;
                    if (nestingLevel == 0)
                    {
                        return tokenLine;
                    }
                }
            }

            // Fallback to document end if not found
            return _currentState.Lines?.Length > 0 ? _currentState.Lines.Length - 1 : startLine + 100;
        }

        private class ScopeInfo
        {
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public ScopeType ScopeType { get; set; }
            public bool IncludeDeclaration { get; set; }
        }

        private enum ScopeType
        {
            Function,
            Subroutine,
            Class,
            Module,
            Block
        }
    }
}
