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
    /// Handles selection range requests for smart selection expansion
    /// </summary>
    public class SelectionRangeHandler : ISelectionRangeHandler
    {
        private readonly DocumentManager _documentManager;
        private SelectionRangeCapability _capability;

        public SelectionRangeHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Guid Id { get; } = Guid.NewGuid();

        public void SetCapability(SelectionRangeCapability capability, ClientCapabilities clientCapabilities)
        {
            _capability = capability;
        }

        public Task<Container<SelectionRange>> Handle(SelectionRangeParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult<Container<SelectionRange>>(null);
            }

            var selectionRanges = new List<SelectionRange>();

            foreach (var position in request.Positions)
            {
                var selectionRange = BuildSelectionRange(state, position);
                if (selectionRange != null)
                {
                    selectionRanges.Add(selectionRange);
                }
            }

            if (selectionRanges.Count == 0)
            {
                return Task.FromResult<Container<SelectionRange>>(null);
            }

            return Task.FromResult(new Container<SelectionRange>(selectionRanges));
        }

        private SelectionRange BuildSelectionRange(DocumentState state, Position position)
        {
            int line = (int)position.Line;
            int character = (int)position.Character;

            if (line < 0 || line >= state.Lines.Length)
                return null;

            var lineText = state.Lines[line];
            if (character < 0 || character > lineText.Length)
                return null;

            // Build a hierarchy of selection ranges from innermost to outermost
            var ranges = new List<LspRange>();

            // 1. Current word/identifier
            var wordRange = GetWordRange(lineText, line, character);
            if (wordRange != null)
                ranges.Add(wordRange);

            // 2. Expression on the line (simple heuristic)
            var expressionRange = GetExpressionRange(lineText, line, character);
            if (expressionRange != null && !ranges.Contains(expressionRange))
                ranges.Add(expressionRange);

            // 3. Statement line
            var statementRange = GetStatementRange(state, line);
            if (statementRange != null && !ranges.Contains(statementRange))
                ranges.Add(statementRange);

            // 4. Block (If/For/While/Function/Sub/Class body)
            var blockRange = GetBlockRange(state, line);
            if (blockRange != null && !ranges.Contains(blockRange))
                ranges.Add(blockRange);

            // 5. Function/Sub/Class declaration
            var declarationRange = GetDeclarationRange(state, line);
            if (declarationRange != null && !ranges.Contains(declarationRange))
                ranges.Add(declarationRange);

            // 6. Entire document
            var documentRange = new LspRange(
                new Position(0, 0),
                new Position(state.Lines.Length - 1, state.Lines[state.Lines.Length - 1].Length));
            ranges.Add(documentRange);

            // Build nested SelectionRange structure from outermost to innermost
            SelectionRange current = null;
            for (int i = ranges.Count - 1; i >= 0; i--)
            {
                current = new SelectionRange
                {
                    Range = ranges[i],
                    Parent = current
                };
            }

            return current;
        }

        private LspRange GetWordRange(string lineText, int line, int character)
        {
            if (string.IsNullOrEmpty(lineText) || character >= lineText.Length)
                return null;

            int start = character;
            int end = character;

            // Expand left
            while (start > 0 && IsIdentifierChar(lineText[start - 1]))
                start--;

            // Expand right
            while (end < lineText.Length && IsIdentifierChar(lineText[end]))
                end++;

            if (start == end)
                return null;

            return new LspRange(
                new Position(line, start),
                new Position(line, end));
        }

        private LspRange GetExpressionRange(string lineText, int line, int character)
        {
            if (string.IsNullOrEmpty(lineText))
                return null;

            // Find the bounds of an expression (simple heuristic: between operators or delimiters)
            int start = character;
            int end = character;

            var delimiters = new HashSet<char> { ',', ';', '(', ')', '\n', '\r' };

            // Expand left until delimiter
            while (start > 0 && !delimiters.Contains(lineText[start - 1]))
                start--;

            // Expand right until delimiter
            while (end < lineText.Length && !delimiters.Contains(lineText[end]))
                end++;

            // Trim whitespace
            while (start < end && char.IsWhiteSpace(lineText[start]))
                start++;
            while (end > start && char.IsWhiteSpace(lineText[end - 1]))
                end--;

            if (start >= end)
                return null;

            return new LspRange(
                new Position(line, start),
                new Position(line, end));
        }

        private LspRange GetStatementRange(DocumentState state, int line)
        {
            if (line < 0 || line >= state.Lines.Length)
                return null;

            var lineText = state.Lines[line].TrimEnd();
            if (string.IsNullOrWhiteSpace(lineText))
                return null;

            return new LspRange(
                new Position(line, 0),
                new Position(line, lineText.Length));
        }

        private LspRange GetBlockRange(DocumentState state, int targetLine)
        {
            // Find the containing block (If/For/While/Sub/Function)
            var keywords = new[] { "If", "For", "While", "Do", "Sub", "Function", "Class", "Select" };
            var endKeywords = new[] { "End If", "Next", "Wend", "Loop", "End Sub", "End Function", "End Class", "End Select" };

            int startLine = -1;
            int endLine = -1;
            int depth = 0;

            // Search backwards for block start
            for (int i = targetLine; i >= 0; i--)
            {
                var line = state.Lines[i].Trim();

                // Check for end keywords (we're going backwards, so these increase depth)
                if (endKeywords.Any(k => line.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
                {
                    depth++;
                }
                // Check for start keywords
                else if (keywords.Any(k => line.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase) ||
                                           line.Equals(k, StringComparison.OrdinalIgnoreCase)))
                {
                    if (depth == 0)
                    {
                        startLine = i;
                        break;
                    }
                    else
                    {
                        depth--;
                    }
                }
            }

            if (startLine == -1)
                return null;

            // Search forward for matching end
            depth = 0;
            for (int i = startLine; i < state.Lines.Length; i++)
            {
                var line = state.Lines[i].Trim();

                // Check for start keywords (increase depth)
                if (keywords.Any(k => line.StartsWith(k + " ", StringComparison.OrdinalIgnoreCase) ||
                                      line.Equals(k, StringComparison.OrdinalIgnoreCase)))
                {
                    if (i != startLine)
                        depth++;
                }
                // Check for end keywords
                else if (endKeywords.Any(k => line.StartsWith(k, StringComparison.OrdinalIgnoreCase)))
                {
                    if (depth == 0)
                    {
                        endLine = i;
                        break;
                    }
                    else
                    {
                        depth--;
                    }
                }
            }

            if (endLine == -1)
                return null;

            return new LspRange(
                new Position(startLine, 0),
                new Position(endLine, state.Lines[endLine].Length));
        }

        private LspRange GetDeclarationRange(DocumentState state, int targetLine)
        {
            if (state.AST == null)
                return null;

            // Find the declaration containing this line
            foreach (var declaration in state.AST.Declarations)
            {
                var range = GetDeclarationRangeForNode(state, declaration, targetLine);
                if (range != null)
                    return range;
            }

            return null;
        }

        private LspRange GetDeclarationRangeForNode(DocumentState state, ASTNode node, int targetLine)
        {
            switch (node)
            {
                case FunctionNode func:
                    if (func.Line - 1 <= targetLine && IsLineInBody(func.Body, targetLine))
                    {
                        int endLine = FindEndLine(state, func.Line - 1, "End Function");
                        if (endLine != -1)
                        {
                            return new LspRange(
                                new Position(func.Line - 1, 0),
                                new Position(endLine, state.Lines[endLine].Length));
                        }
                    }
                    break;

                case SubroutineNode sub:
                    if (sub.Line - 1 <= targetLine && IsLineInBody(sub.Body, targetLine))
                    {
                        int endLine = FindEndLine(state, sub.Line - 1, "End Sub");
                        if (endLine != -1)
                        {
                            return new LspRange(
                                new Position(sub.Line - 1, 0),
                                new Position(endLine, state.Lines[endLine].Length));
                        }
                    }
                    break;

                case ClassNode cls:
                    if (cls.Line - 1 <= targetLine)
                    {
                        int endLine = FindEndLine(state, cls.Line - 1, "End Class");
                        if (endLine != -1 && targetLine <= endLine)
                        {
                            return new LspRange(
                                new Position(cls.Line - 1, 0),
                                new Position(endLine, state.Lines[endLine].Length));
                        }
                    }
                    break;
            }

            return null;
        }

        private bool IsLineInBody(BlockNode body, int targetLine)
        {
            if (body == null)
                return false;

            foreach (var statement in body.Statements)
            {
                if (statement.Line - 1 <= targetLine)
                    return true;
            }

            return false;
        }

        private int FindEndLine(DocumentState state, int startLine, string endKeyword)
        {
            for (int i = startLine + 1; i < state.Lines.Length; i++)
            {
                var line = state.Lines[i].Trim();
                if (line.StartsWith(endKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private bool IsIdentifierChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        public SelectionRangeRegistrationOptions GetRegistrationOptions(
            SelectionRangeCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new SelectionRangeRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
