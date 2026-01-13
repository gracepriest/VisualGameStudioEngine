using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles document formatting requests
    /// </summary>
    public class FormattingHandler : DocumentFormattingHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public FormattingHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult(new TextEditContainer());
            }

            var formatted = FormatDocument(state.Content, request.Options);

            // Create a single edit that replaces the entire document
            var edit = new TextEdit
            {
                Range = new LspRange(
                    new Position(0, 0),
                    new Position(state.Lines.Length, 0)),
                NewText = formatted
            };

            return Task.FromResult(new TextEditContainer(edit));
        }

        private string FormatDocument(string content, FormattingOptions options)
        {
            var lines = content.Split('\n');
            var result = new StringBuilder();
            int indentLevel = 0;
            var indentString = options.InsertSpaces
                ? new string(' ', (int)options.TabSize)
                : "\t";

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.Trim();

                // Skip empty lines
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    result.AppendLine();
                    continue;
                }

                // Decrease indent before these keywords
                if (IsDecreaseIndentBefore(trimmed))
                {
                    indentLevel = System.Math.Max(0, indentLevel - 1);
                }

                // Handle Else, ElseIf, Case - same level as If/Select
                if (IsMiddleKeyword(trimmed))
                {
                    // Temporarily decrease for this line only
                    var tempIndent = System.Math.Max(0, indentLevel - 1);
                    result.AppendLine(GetIndent(tempIndent, indentString) + FormatLine(trimmed));
                    continue;
                }

                // Write the formatted line
                result.AppendLine(GetIndent(indentLevel, indentString) + FormatLine(trimmed));

                // Increase indent after these keywords
                if (IsIncreaseIndentAfter(trimmed))
                {
                    indentLevel++;
                }
            }

            return result.ToString();
        }

        private string FormatLine(string line)
        {
            // Normalize spacing around operators
            var result = line;

            // Add space after commas
            result = System.Text.RegularExpressions.Regex.Replace(result, @",(?!\s)", ", ");

            // Normalize spaces around = (but not == or <=, >=, <>)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<![<>=!])=(?!=)", " = ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+=\s+", " = ");

            // Normalize spaces around comparison operators
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<>", " <> ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<=", " <= ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @">=", " >= ");

            // Fix multiple spaces
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ");

            // Proper casing for keywords
            result = NormalizeKeywordCasing(result);

            return result.Trim();
        }

        private string NormalizeKeywordCasing(string line)
        {
            // Keywords to normalize (VB style - proper case)
            var keywords = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                // Control flow
                { "if", "If" }, { "then", "Then" }, { "else", "Else" }, { "elseif", "ElseIf" },
                { "end if", "End If" }, { "endif", "End If" },
                { "for", "For" }, { "to", "To" }, { "step", "Step" }, { "next", "Next" },
                { "while", "While" }, { "wend", "Wend" }, { "end while", "End While" },
                { "do", "Do" }, { "loop", "Loop" }, { "until", "Until" },
                { "select", "Select" }, { "case", "Case" }, { "end select", "End Select" },

                // Declarations
                { "dim", "Dim" }, { "const", "Const" }, { "as", "As" },
                { "sub", "Sub" }, { "end sub", "End Sub" },
                { "function", "Function" }, { "end function", "End Function" },
                { "class", "Class" }, { "end class", "End Class" },
                { "property", "Property" }, { "end property", "End Property" },
                { "get", "Get" }, { "end get", "End Get" },
                { "set", "Set" }, { "end set", "End Set" },
                { "inherits", "Inherits" }, { "implements", "Implements" },

                // Modifiers
                { "public", "Public" }, { "private", "Private" }, { "protected", "Protected" },
                { "shared", "Shared" }, { "static", "Static" },
                { "overridable", "Overridable" }, { "overrides", "Overrides" },
                { "mustoverride", "MustOverride" }, { "notoverridable", "NotOverridable" },
                { "optional", "Optional" }, { "byval", "ByVal" }, { "byref", "ByRef" },
                { "paramarray", "ParamArray" },

                // Types
                { "integer", "Integer" }, { "string", "String" }, { "boolean", "Boolean" },
                { "double", "Double" }, { "single", "Single" }, { "long", "Long" },
                { "object", "Object" }, { "variant", "Variant" },

                // Operators
                { "and", "And" }, { "or", "Or" }, { "not", "Not" }, { "xor", "Xor" },
                { "mod", "Mod" }, { "is", "Is" }, { "isnot", "IsNot" },
                { "andalso", "AndAlso" }, { "orelse", "OrElse" },

                // Literals
                { "true", "True" }, { "false", "False" }, { "nothing", "Nothing" },

                // Statements
                { "return", "Return" }, { "exit", "Exit" }, { "continue", "Continue" },
                { "throw", "Throw" }, { "try", "Try" }, { "catch", "Catch" }, { "finally", "Finally" },
                { "end try", "End Try" },
                { "with", "With" }, { "end with", "End With" },
                { "using", "Using" }, { "end using", "End Using" },

                // Other
                { "new", "New" }, { "me", "Me" }, { "mybase", "MyBase" }, { "myclass", "MyClass" },
                { "imports", "Imports" }, { "option", "Option" }, { "explicit", "Explicit" },
            };

            var result = line;

            // Apply keyword casing using word boundaries
            foreach (var kv in keywords)
            {
                var pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(kv.Key)}\b";
                result = System.Text.RegularExpressions.Regex.Replace(
                    result,
                    pattern,
                    kv.Value,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            return result;
        }

        private bool IsIncreaseIndentAfter(string line)
        {
            var upper = line.ToUpperInvariant();

            // Block starters
            if (upper.StartsWith("IF ") && upper.EndsWith(" THEN")) return true;
            if (upper.StartsWith("ELSEIF ") && upper.EndsWith(" THEN")) return true;
            if (upper == "ELSE") return true;
            if (upper.StartsWith("FOR ")) return true;
            if (upper.StartsWith("WHILE ")) return true;
            if (upper.StartsWith("DO WHILE ") || upper.StartsWith("DO UNTIL ") || upper == "DO") return true;
            if (upper.StartsWith("SELECT CASE ")) return true;
            if (upper.StartsWith("CASE ") || upper == "CASE ELSE") return true;
            if (upper.StartsWith("SUB ")) return true;
            if (upper.StartsWith("FUNCTION ")) return true;
            if (upper.StartsWith("CLASS ")) return true;
            if (upper.StartsWith("PROPERTY ")) return true;
            if (upper == "GET" || upper.StartsWith("GET(")) return true;
            if (upper == "SET" || upper.StartsWith("SET(")) return true;
            if (upper.StartsWith("TRY")) return true;
            if (upper.StartsWith("CATCH")) return true;
            if (upper.StartsWith("FINALLY")) return true;
            if (upper.StartsWith("WITH ")) return true;
            if (upper.StartsWith("USING ")) return true;

            return false;
        }

        private bool IsDecreaseIndentBefore(string line)
        {
            var upper = line.ToUpperInvariant();

            // Block enders
            if (upper.StartsWith("END IF") || upper == "ENDIF") return true;
            if (upper == "NEXT" || upper.StartsWith("NEXT ")) return true;
            if (upper == "WEND" || upper.StartsWith("END WHILE")) return true;
            if (upper == "LOOP" || upper.StartsWith("LOOP WHILE") || upper.StartsWith("LOOP UNTIL")) return true;
            if (upper.StartsWith("END SELECT")) return true;
            if (upper.StartsWith("END SUB")) return true;
            if (upper.StartsWith("END FUNCTION")) return true;
            if (upper.StartsWith("END CLASS")) return true;
            if (upper.StartsWith("END PROPERTY")) return true;
            if (upper.StartsWith("END GET")) return true;
            if (upper.StartsWith("END SET")) return true;
            if (upper.StartsWith("END TRY")) return true;
            if (upper.StartsWith("END WITH")) return true;
            if (upper.StartsWith("END USING")) return true;

            return false;
        }

        private bool IsMiddleKeyword(string line)
        {
            var upper = line.ToUpperInvariant();

            if (upper == "ELSE") return true;
            if (upper.StartsWith("ELSEIF ")) return true;
            if (upper.StartsWith("CASE ") || upper == "CASE ELSE") return true;
            if (upper.StartsWith("CATCH")) return true;
            if (upper.StartsWith("FINALLY")) return true;

            return false;
        }

        private string GetIndent(int level, string indentString)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < level; i++)
            {
                sb.Append(indentString);
            }
            return sb.ToString();
        }

        protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(
            DocumentFormattingCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DocumentFormattingRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }

    /// <summary>
    /// Handles range formatting requests (format selection)
    /// </summary>
    public class RangeFormattingHandler : DocumentRangeFormattingHandlerBase
    {
        private readonly DocumentManager _documentManager;

        public RangeFormattingHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<TextEditContainer> Handle(DocumentRangeFormattingParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult(new TextEditContainer());
            }

            var startLine = (int)request.Range.Start.Line;
            var endLine = (int)request.Range.End.Line;

            // Ensure valid range
            if (startLine < 0 || startLine >= state.Lines.Length || endLine < startLine)
            {
                return Task.FromResult(new TextEditContainer());
            }

            endLine = System.Math.Min(endLine, state.Lines.Length - 1);

            // Calculate initial indent level by analyzing lines before selection
            int baseIndentLevel = CalculateIndentLevel(state.Lines, startLine, request.Options);

            // Format only the selected range
            var edits = new List<TextEdit>();
            var indentString = request.Options.InsertSpaces
                ? new string(' ', (int)request.Options.TabSize)
                : "\t";

            int currentIndent = baseIndentLevel;

            for (int i = startLine; i <= endLine; i++)
            {
                var line = state.Lines[i].TrimEnd('\r');
                var trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue; // Skip empty lines
                }

                // Check indent adjustments
                if (IsDecreaseIndentBefore(trimmed))
                {
                    currentIndent = System.Math.Max(0, currentIndent - 1);
                }

                string formattedLine;
                if (IsMiddleKeyword(trimmed))
                {
                    var tempIndent = System.Math.Max(0, currentIndent - 1);
                    formattedLine = GetIndent(tempIndent, indentString) + FormatLine(trimmed);
                }
                else
                {
                    formattedLine = GetIndent(currentIndent, indentString) + FormatLine(trimmed);
                }

                // Only add edit if line changed
                if (line != formattedLine)
                {
                    edits.Add(new TextEdit
                    {
                        Range = new LspRange(new Position(i, 0), new Position(i, line.Length)),
                        NewText = formattedLine
                    });
                }

                if (IsIncreaseIndentAfter(trimmed))
                {
                    currentIndent++;
                }
            }

            return Task.FromResult(new TextEditContainer(edits));
        }

        private int CalculateIndentLevel(string[] lines, int targetLine, FormattingOptions options)
        {
            int indent = 0;

            for (int i = 0; i < targetLine && i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                if (IsDecreaseIndentBefore(trimmed))
                {
                    indent = System.Math.Max(0, indent - 1);
                }

                if (IsIncreaseIndentAfter(trimmed))
                {
                    indent++;
                }
            }

            return indent;
        }

        private string FormatLine(string line)
        {
            var result = line;
            result = System.Text.RegularExpressions.Regex.Replace(result, @",(?!\s)", ", ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<![<>=!])=(?!=)", " = ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s+=\s+", " = ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<>", " <> ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<=", " <= ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @">=", " >= ");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ");
            return result.Trim();
        }

        private bool IsIncreaseIndentAfter(string line)
        {
            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("IF ") && upper.EndsWith(" THEN")) return true;
            if (upper.StartsWith("ELSEIF ") && upper.EndsWith(" THEN")) return true;
            if (upper == "ELSE") return true;
            if (upper.StartsWith("FOR ")) return true;
            if (upper.StartsWith("WHILE ")) return true;
            if (upper.StartsWith("DO WHILE ") || upper.StartsWith("DO UNTIL ") || upper == "DO") return true;
            if (upper.StartsWith("SELECT CASE ")) return true;
            if (upper.StartsWith("CASE ") || upper == "CASE ELSE") return true;
            if (upper.StartsWith("SUB ")) return true;
            if (upper.StartsWith("FUNCTION ")) return true;
            if (upper.StartsWith("CLASS ")) return true;
            if (upper.StartsWith("PROPERTY ")) return true;
            if (upper == "GET" || upper.StartsWith("GET(")) return true;
            if (upper == "SET" || upper.StartsWith("SET(")) return true;
            if (upper.StartsWith("TRY")) return true;
            if (upper.StartsWith("CATCH")) return true;
            if (upper.StartsWith("FINALLY")) return true;
            if (upper.StartsWith("WITH ")) return true;
            if (upper.StartsWith("USING ")) return true;
            return false;
        }

        private bool IsDecreaseIndentBefore(string line)
        {
            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("END IF") || upper == "ENDIF") return true;
            if (upper == "NEXT" || upper.StartsWith("NEXT ")) return true;
            if (upper == "WEND" || upper.StartsWith("END WHILE")) return true;
            if (upper == "LOOP" || upper.StartsWith("LOOP WHILE") || upper.StartsWith("LOOP UNTIL")) return true;
            if (upper.StartsWith("END SELECT")) return true;
            if (upper.StartsWith("END SUB")) return true;
            if (upper.StartsWith("END FUNCTION")) return true;
            if (upper.StartsWith("END CLASS")) return true;
            if (upper.StartsWith("END PROPERTY")) return true;
            if (upper.StartsWith("END GET")) return true;
            if (upper.StartsWith("END SET")) return true;
            if (upper.StartsWith("END TRY")) return true;
            if (upper.StartsWith("END WITH")) return true;
            if (upper.StartsWith("END USING")) return true;
            return false;
        }

        private bool IsMiddleKeyword(string line)
        {
            var upper = line.ToUpperInvariant();
            if (upper == "ELSE") return true;
            if (upper.StartsWith("ELSEIF ")) return true;
            if (upper.StartsWith("CASE ") || upper == "CASE ELSE") return true;
            if (upper.StartsWith("CATCH")) return true;
            if (upper.StartsWith("FINALLY")) return true;
            return false;
        }

        private string GetIndent(int level, string indentString)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < level; i++)
            {
                sb.Append(indentString);
            }
            return sb.ToString();
        }

        protected override DocumentRangeFormattingRegistrationOptions CreateRegistrationOptions(
            DocumentRangeFormattingCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new DocumentRangeFormattingRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang")
            };
        }
    }
}
