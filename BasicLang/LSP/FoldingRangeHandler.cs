using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles folding range requests (code folding/collapsing)
    /// </summary>
    public class FoldingRangeHandler : FoldingRangeHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public FoldingRangeHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<Container<FoldingRange>?> Handle(FoldingRangeRequestParam request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult(new Container<FoldingRange>());
            }

            var ranges = new List<FoldingRange>();

            // Track block starts with a stack
            var blockStack = new Stack<(int line, string kind, FoldingRangeKind? foldKind)>();

            for (int i = 0; i < state.Lines.Length; i++)
            {
                var line = state.Lines[i].Trim().ToUpperInvariant();

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Comments - find consecutive comment lines
                if (line.StartsWith("'") || line.StartsWith("REM "))
                {
                    int startLine = i;
                    while (i + 1 < state.Lines.Length)
                    {
                        var nextLine = state.Lines[i + 1].Trim();
                        if (nextLine.StartsWith("'") || nextLine.ToUpperInvariant().StartsWith("REM "))
                        {
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Only create fold if more than 1 line
                    if (i > startLine)
                    {
                        ranges.Add(new FoldingRange
                        {
                            StartLine = startLine,
                            EndLine = i,
                            Kind = FoldingRangeKind.Comment
                        });
                    }
                    continue;
                }

                // Region directives
                if (line.StartsWith("#REGION"))
                {
                    blockStack.Push((i, "REGION", FoldingRangeKind.Region));
                    continue;
                }

                if (line.StartsWith("#END REGION") || line.StartsWith("#ENDREGION"))
                {
                    if (blockStack.Count > 0 && blockStack.Peek().kind == "REGION")
                    {
                        var start = blockStack.Pop();
                        ranges.Add(new FoldingRange
                        {
                            StartLine = start.line,
                            EndLine = i,
                            Kind = FoldingRangeKind.Region
                        });
                    }
                    continue;
                }

                // Imports block
                if (line.StartsWith("IMPORTS "))
                {
                    int startLine = i;
                    while (i + 1 < state.Lines.Length)
                    {
                        var nextLine = state.Lines[i + 1].Trim().ToUpperInvariant();
                        if (nextLine.StartsWith("IMPORTS "))
                        {
                            i++;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (i > startLine)
                    {
                        ranges.Add(new FoldingRange
                        {
                            StartLine = startLine,
                            EndLine = i,
                            Kind = FoldingRangeKind.Imports
                        });
                    }
                    continue;
                }

                // Block statements
                if (line.StartsWith("CLASS "))
                {
                    blockStack.Push((i, "CLASS", null));
                }
                else if (line.StartsWith("END CLASS"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "CLASS");
                }
                else if (line.StartsWith("INTERFACE "))
                {
                    blockStack.Push((i, "INTERFACE", null));
                }
                else if (line.StartsWith("END INTERFACE"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "INTERFACE");
                }
                else if (line.StartsWith("ENUM "))
                {
                    blockStack.Push((i, "ENUM", null));
                }
                else if (line.StartsWith("END ENUM"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "ENUM");
                }
                else if (line.StartsWith("SUB "))
                {
                    blockStack.Push((i, "SUB", null));
                }
                else if (line.StartsWith("END SUB"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "SUB");
                }
                else if (line.StartsWith("FUNCTION "))
                {
                    blockStack.Push((i, "FUNCTION", null));
                }
                else if (line.StartsWith("END FUNCTION"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "FUNCTION");
                }
                else if (line.StartsWith("PROPERTY "))
                {
                    blockStack.Push((i, "PROPERTY", null));
                }
                else if (line.StartsWith("END PROPERTY"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "PROPERTY");
                }
                else if (line == "GET" || line.StartsWith("GET("))
                {
                    blockStack.Push((i, "GET", null));
                }
                else if (line.StartsWith("END GET"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "GET");
                }
                else if (line == "SET" || line.StartsWith("SET("))
                {
                    blockStack.Push((i, "SET", null));
                }
                else if (line.StartsWith("END SET"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "SET");
                }
                else if ((line.StartsWith("IF ") && line.EndsWith(" THEN")) ||
                         (line.StartsWith("IF ") && !line.Contains(" THEN ")))  // Multi-line If
                {
                    // Only track multi-line If (not single-line If...Then...End If)
                    if (!IsSingleLineIf(state.Lines[i]))
                    {
                        blockStack.Push((i, "IF", null));
                    }
                }
                else if (line.StartsWith("END IF") || line == "ENDIF")
                {
                    TryPopAndAddRange(blockStack, ranges, i, "IF");
                }
                else if (line.StartsWith("FOR "))
                {
                    blockStack.Push((i, "FOR", null));
                }
                else if (line.StartsWith("NEXT"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "FOR");
                }
                else if (line.StartsWith("WHILE "))
                {
                    blockStack.Push((i, "WHILE", null));
                }
                else if (line == "WEND" || line.StartsWith("END WHILE"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "WHILE");
                }
                else if (line.StartsWith("DO"))
                {
                    blockStack.Push((i, "DO", null));
                }
                else if (line.StartsWith("LOOP"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "DO");
                }
                else if (line.StartsWith("SELECT CASE ") || line.StartsWith("SELECT "))
                {
                    blockStack.Push((i, "SELECT", null));
                }
                else if (line.StartsWith("END SELECT"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "SELECT");
                }
                else if (line.StartsWith("TRY"))
                {
                    blockStack.Push((i, "TRY", null));
                }
                else if (line.StartsWith("END TRY"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "TRY");
                }
                else if (line.StartsWith("WITH "))
                {
                    blockStack.Push((i, "WITH", null));
                }
                else if (line.StartsWith("END WITH"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "WITH");
                }
                else if (line.StartsWith("USING "))
                {
                    blockStack.Push((i, "USING", null));
                }
                else if (line.StartsWith("END USING"))
                {
                    TryPopAndAddRange(blockStack, ranges, i, "USING");
                }
            }

            return Task.FromResult(new Container<FoldingRange>(ranges));
        }

        private void TryPopAndAddRange(
            Stack<(int line, string kind, FoldingRangeKind? foldKind)> stack,
            List<FoldingRange> ranges,
            int endLine,
            string expectedKind)
        {
            // Pop matching block from stack
            var tempStack = new Stack<(int line, string kind, FoldingRangeKind? foldKind)>();

            while (stack.Count > 0)
            {
                var item = stack.Pop();
                if (item.kind == expectedKind)
                {
                    // Found matching start
                    if (endLine > item.line)
                    {
                        ranges.Add(new FoldingRange
                        {
                            StartLine = item.line,
                            EndLine = endLine,
                            Kind = item.foldKind
                        });
                    }

                    // Restore temp stack
                    while (tempStack.Count > 0)
                    {
                        stack.Push(tempStack.Pop());
                    }
                    return;
                }
                else
                {
                    tempStack.Push(item);
                }
            }

            // Restore temp stack if no match found
            while (tempStack.Count > 0)
            {
                stack.Push(tempStack.Pop());
            }
        }

        private bool IsSingleLineIf(string line)
        {
            // Check if this is a single-line If statement
            // Single-line If: If condition Then statement [Else statement]
            var upper = line.Trim().ToUpperInvariant();

            if (!upper.StartsWith("IF ")) return false;

            // Find the first "Then" after "If"
            var thenIndex = upper.IndexOf(" THEN ");
            if (thenIndex < 0) return false;

            // If there's content after "Then " that's not just whitespace, it's single-line
            var afterThen = line.Substring(thenIndex + 6).Trim();
            return !string.IsNullOrWhiteSpace(afterThen);
        }

        protected override FoldingRangeRegistrationOptions CreateRegistrationOptions(
            FoldingRangeCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new FoldingRangeRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
