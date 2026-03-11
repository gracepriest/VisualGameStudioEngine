using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles on-type formatting requests. When the user presses Enter after a block-opening
    /// keyword, this handler auto-inserts the matching closing keyword.
    /// </summary>
    public class OnTypeFormattingHandler : DocumentOnTypeFormattingHandlerBase
    {
        private readonly DocumentManager _documentManager;

        /// <summary>
        /// Maps a block-opening pattern to its closing keyword.
        /// Each entry is (startCheck, endCheck, closingKeyword).
        /// </summary>
        private static readonly (Func<string, bool> Matches, string ClosingKeyword, Func<string, bool> AlreadyClosed)[] BlockRules =
        {
            (line => line.StartsWith("SUB "),          "End Sub",    line => line.StartsWith("END SUB")),
            (line => line.StartsWith("FUNCTION "),     "End Function", line => line.StartsWith("END FUNCTION")),
            (line => line.StartsWith("CLASS "),         "End Class",  line => line.StartsWith("END CLASS")),
            (line => line.StartsWith("IF ") && line.EndsWith(" THEN"), "End If", line => line.StartsWith("END IF") || line == "ENDIF"),
            (line => line.StartsWith("FOR "),           "Next",       line => line == "NEXT" || line.StartsWith("NEXT ")),
            (line => line.StartsWith("WHILE "),         "End While",  line => line.StartsWith("END WHILE")),
            (line => line.StartsWith("SELECT CASE "),   "End Select", line => line.StartsWith("END SELECT")),
            (line => line.StartsWith("PROPERTY "),      "End Property", line => line.StartsWith("END PROPERTY")),
            (line => line == "TRY" || line.StartsWith("TRY "), "End Try", line => line.StartsWith("END TRY")),
            (line => line == "DO" || line.StartsWith("DO WHILE") || line.StartsWith("DO UNTIL"), "Loop", line => line == "LOOP" || line.StartsWith("LOOP WHILE") || line.StartsWith("LOOP UNTIL")),
            (line => line.StartsWith("WITH "),          "End With",   line => line.StartsWith("END WITH")),
        };

        public OnTypeFormattingHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<TextEditContainer?> Handle(DocumentOnTypeFormattingParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult<TextEditContainer?>(new TextEditContainer());
            }

            var lines = state.Lines;
            var cursorLine = (int)request.Position.Line;

            // We want the line above the cursor (the line the user just typed Enter on)
            var previousLineIndex = cursorLine - 1;
            if (previousLineIndex < 0 || previousLineIndex >= lines.Length)
            {
                return Task.FromResult<TextEditContainer?>(new TextEditContainer());
            }

            var previousLine = lines[previousLineIndex].TrimEnd('\r');
            var trimmedUpper = previousLine.Trim().ToUpperInvariant();
            var indent = GetLineIndentation(previousLine);

            // Check each block rule
            foreach (var rule in BlockRules)
            {
                if (!rule.Matches(trimmedUpper))
                    continue;

                // Check if a matching closing keyword already exists within the next 20 lines
                if (HasMatchingClose(lines, cursorLine, rule.AlreadyClosed, 20))
                {
                    return Task.FromResult<TextEditContainer?>(new TextEditContainer());
                }

                // Build the text edits:
                // 1. Insert an indented blank line at the cursor position (for the user's code)
                // 2. Insert the closing keyword at the same indent level as the opening line
                var innerIndent = indent + GetIndentUnit(request.Options);
                var closingLine = indent + rule.ClosingKeyword;

                // Insert at the cursor position: inner indent + newline + closing keyword + newline
                var insertText = innerIndent + "\n" + closingLine + "\n";

                var edit = new TextEdit
                {
                    Range = new LspRange(
                        new Position(cursorLine, 0),
                        new Position(cursorLine, 0)),
                    NewText = insertText
                };

                return Task.FromResult<TextEditContainer?>(new TextEditContainer(edit));
            }

            return Task.FromResult<TextEditContainer?>(new TextEditContainer());
        }

        /// <summary>
        /// Checks whether a matching closing keyword exists within the next N lines.
        /// </summary>
        private bool HasMatchingClose(string[] lines, int startLine, Func<string, bool> isClosing, int maxLinesToSearch)
        {
            int nestingDepth = 1;
            int endLine = Math.Min(startLine + maxLinesToSearch, lines.Length);

            for (int i = startLine; i < endLine; i++)
            {
                var trimmedUpper = lines[i].Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(trimmedUpper))
                    continue;

                if (isClosing(trimmedUpper))
                {
                    nestingDepth--;
                    if (nestingDepth == 0)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extracts the leading whitespace from a line.
        /// </summary>
        private string GetLineIndentation(string line)
        {
            int i = 0;
            while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            {
                i++;
            }
            return line.Substring(0, i);
        }

        /// <summary>
        /// Returns a single indent unit based on formatting options.
        /// </summary>
        private string GetIndentUnit(FormattingOptions options)
        {
            if (options.InsertSpaces)
            {
                return new string(' ', (int)options.TabSize);
            }
            return "\t";
        }

        protected override DocumentOnTypeFormattingRegistrationOptions CreateRegistrationOptions(
            DocumentOnTypeFormattingCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DocumentOnTypeFormattingRegistrationOptions
            {
                FirstTriggerCharacter = "\n",
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
