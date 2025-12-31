using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles code action requests (quick fixes, refactorings)
    /// </summary>
    public class CodeActionHandler : ICodeActionHandler
    {
        private readonly DocumentManager _documentManager;

        public CodeActionHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public Task<CommandOrCodeActionContainer> Handle(CodeActionParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult(new CommandOrCodeActionContainer());
            }

            var actions = new List<CommandOrCodeAction>();

            // Get diagnostics in the requested range
            var diagnosticsInRange = request.Context.Diagnostics
                .Where(d => RangesOverlap(d.Range, request.Range))
                .ToList();

            // Generate quick fixes for each diagnostic
            foreach (var diagnostic in diagnosticsInRange)
            {
                var fixes = GetQuickFixesForDiagnostic(state, diagnostic, request.TextDocument.Uri);
                actions.AddRange(fixes);
            }

            // Add refactoring actions based on selection
            var refactorings = GetRefactoringActions(state, request.Range, request.TextDocument.Uri);
            actions.AddRange(refactorings);

            return Task.FromResult(new CommandOrCodeActionContainer(actions));
        }

        private List<CommandOrCodeAction> GetQuickFixesForDiagnostic(
            DocumentState state,
            OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic diagnostic,
            DocumentUri uri)
        {
            var fixes = new List<CommandOrCodeAction>();
            var message = diagnostic.Message ?? "";

            // Fix: Expected 'End If'
            if (message.Contains("Expected 'End If'") || message.Contains("Expected 'End If"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'End If'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "End If\n",
                    diagnostic));
            }

            // Fix: Expected 'End Sub'
            if (message.Contains("Expected 'End Sub'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'End Sub'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "End Sub\n",
                    diagnostic));
            }

            // Fix: Expected 'End Function'
            if (message.Contains("Expected 'End Function'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'End Function'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "End Function\n",
                    diagnostic));
            }

            // Fix: Expected 'End Class'
            if (message.Contains("Expected 'End Class'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'End Class'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "End Class\n",
                    diagnostic));
            }

            // Fix: Expected 'Wend' or 'End While'
            if (message.Contains("Expected 'Wend'") || message.Contains("Expected 'End While'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'Wend'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "Wend\n",
                    diagnostic));
            }

            // Fix: Expected 'Next'
            if (message.Contains("Expected 'Next'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'Next'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "Next\n",
                    diagnostic));
            }

            // Fix: Expected 'Loop'
            if (message.Contains("Expected 'Loop'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'Loop'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "Loop\n",
                    diagnostic));
            }

            // Fix: Undefined variable - suggest adding Dim
            if (message.Contains("Undefined variable") || message.Contains("is not defined"))
            {
                var varName = ExtractVariableName(message);
                if (!string.IsNullOrEmpty(varName))
                {
                    int insertLine = FindDeclarationInsertLine(state, (int)diagnostic.Range.Start.Line);
                    fixes.Add(CreateInsertFix(
                        $"Declare variable '{varName}'",
                        uri,
                        new Position(insertLine, 0),
                        $"    Dim {varName} As Object\n",
                        diagnostic));
                }
            }

            // Fix: Missing 'Then' after If condition
            if (message.Contains("Expected 'Then'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'Then'",
                    uri,
                    diagnostic.Range.End,
                    " Then",
                    diagnostic));
            }

            // Fix: Missing 'End Try'
            if (message.Contains("Expected 'End Try'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'End Try'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "End Try\n",
                    diagnostic));
            }

            // Fix: Missing 'End Select'
            if (message.Contains("Expected 'End Select'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add 'End Select'",
                    uri,
                    new Position(diagnostic.Range.End.Line + 1, 0),
                    "End Select\n",
                    diagnostic));
            }

            // Fix: Type mismatch - suggest type conversion
            if (message.Contains("Type mismatch") || message.Contains("Cannot convert"))
            {
                if (message.Contains("Integer") || message.Contains("Int"))
                {
                    fixes.Add(CreateWrappingFix(
                        "Convert to Integer with CInt()",
                        uri,
                        diagnostic.Range,
                        "CInt(",
                        ")",
                        diagnostic));
                }
                if (message.Contains("String"))
                {
                    fixes.Add(CreateWrappingFix(
                        "Convert to String with CStr()",
                        uri,
                        diagnostic.Range,
                        "CStr(",
                        ")",
                        diagnostic));
                }
                if (message.Contains("Double") || message.Contains("Single"))
                {
                    fixes.Add(CreateWrappingFix(
                        "Convert to Double with CDbl()",
                        uri,
                        diagnostic.Range,
                        "CDbl(",
                        ")",
                        diagnostic));
                }
                if (message.Contains("Boolean"))
                {
                    fixes.Add(CreateWrappingFix(
                        "Convert to Boolean with CBool()",
                        uri,
                        diagnostic.Range,
                        "CBool(",
                        ")",
                        diagnostic));
                }
            }

            // Fix: Missing 'As' clause in declaration
            if (message.Contains("Missing type") || message.Contains("Expected 'As'"))
            {
                fixes.Add(CreateInsertFix(
                    "Add type declaration 'As Integer'",
                    uri,
                    diagnostic.Range.End,
                    " As Integer",
                    diagnostic));
                fixes.Add(CreateInsertFix(
                    "Add type declaration 'As String'",
                    uri,
                    diagnostic.Range.End,
                    " As String",
                    diagnostic));
                fixes.Add(CreateInsertFix(
                    "Add type declaration 'As Object'",
                    uri,
                    diagnostic.Range.End,
                    " As Object",
                    diagnostic));
            }

            // Fix: Missing return type
            if (message.Contains("Missing return type") || message.Contains("Expected return type"))
            {
                fixes.Add(CreateInsertFix(
                    "Add return type 'As Integer'",
                    uri,
                    diagnostic.Range.End,
                    " As Integer",
                    diagnostic));
                fixes.Add(CreateInsertFix(
                    "Add return type 'As String'",
                    uri,
                    diagnostic.Range.End,
                    " As String",
                    diagnostic));
            }

            // Fix: Unused variable - remove it
            if (message.Contains("Unused variable") || message.Contains("is declared but never used"))
            {
                fixes.Add(CreateRemoveLineFix(
                    "Remove unused variable",
                    uri,
                    (int)diagnostic.Range.Start.Line,
                    state,
                    diagnostic));
            }

            return fixes;
        }

        private CommandOrCodeAction CreateWrappingFix(
            string title,
            DocumentUri uri,
            LspRange range,
            string prefix,
            string suffix,
            OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic diagnostic)
        {
            // We need to get the text at the range - for now, just add prefix/suffix
            var edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [uri] = new[]
                    {
                        new TextEdit
                        {
                            Range = new LspRange(range.Start, range.Start),
                            NewText = prefix
                        },
                        new TextEdit
                        {
                            Range = new LspRange(range.End, range.End),
                            NewText = suffix
                        }
                    }
                }
            };

            return new CommandOrCodeAction(new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diagnostic),
                Edit = edit
            });
        }

        private CommandOrCodeAction CreateRemoveLineFix(
            string title,
            DocumentUri uri,
            int line,
            DocumentState state,
            OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic diagnostic)
        {
            var endLine = line < state.Lines.Length - 1 ? line + 1 : line;
            var endCol = line < state.Lines.Length - 1 ? 0 : state.Lines[line].Length;

            var edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [uri] = new[]
                    {
                        new TextEdit
                        {
                            Range = new LspRange(
                                new Position(line, 0),
                                new Position(endLine, endCol)),
                            NewText = ""
                        }
                    }
                }
            };

            return new CommandOrCodeAction(new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diagnostic),
                Edit = edit
            });
        }

        private List<CommandOrCodeAction> GetRefactoringActions(
            DocumentState state,
            LspRange range,
            DocumentUri uri)
        {
            var actions = new List<CommandOrCodeAction>();

            if (state?.Lines == null) return actions;

            int startLine = (int)range.Start.Line;
            if (startLine < 0 || startLine >= state.Lines.Length) return actions;

            var lineText = state.Lines[startLine].Trim();
            var originalLine = state.Lines[startLine];

            // Convert Sub to Function
            if (lineText.StartsWith("Sub ") && !lineText.Contains("Sub New"))
            {
                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(
                                    new Position(startLine, 0),
                                    new Position(startLine, originalLine.Length)),
                                NewText = originalLine.Replace("Sub ", "Function ") + " As Object"
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Convert to Function",
                    Kind = CodeActionKind.RefactorRewrite,
                    Edit = edit
                }));
            }

            // Convert Function to Sub
            if (lineText.StartsWith("Function "))
            {
                var asIndex = originalLine.LastIndexOf(" As ");
                var newLine = asIndex > 0
                    ? originalLine.Substring(0, asIndex).Replace("Function ", "Sub ")
                    : originalLine.Replace("Function ", "Sub ");

                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(
                                    new Position(startLine, 0),
                                    new Position(startLine, originalLine.Length)),
                                NewText = newLine
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Convert to Sub",
                    Kind = CodeActionKind.RefactorRewrite,
                    Edit = edit
                }));
            }

            // Add Option Explicit
            if (startLine == 0 && !state.Content.TrimStart().StartsWith("Option"))
            {
                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(new Position(0, 0), new Position(0, 0)),
                                NewText = "Option Explicit\n\n"
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Add 'Option Explicit'",
                    Kind = CodeActionKind.Source,
                    Edit = edit
                }));
            }

            // Extract to variable
            if (range.Start.Line == range.End.Line && range.Start.Character != range.End.Character)
            {
                int startChar = (int)range.Start.Character;
                int endChar = (int)range.End.Character;
                if (startLine < state.Lines.Length && startChar < state.Lines[startLine].Length)
                {
                    var selectedText = state.Lines[startLine].Substring(
                        startChar,
                        Math.Min(endChar - startChar, state.Lines[startLine].Length - startChar));

                    if (!string.IsNullOrWhiteSpace(selectedText))
                    {
                        int insertLine = FindDeclarationInsertLine(state, startLine);
                        var edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [uri] = new[]
                                {
                                    new TextEdit
                                    {
                                        Range = new LspRange(new Position(insertLine, 0), new Position(insertLine, 0)),
                                        NewText = $"    Dim extracted As Object = {selectedText}\n"
                                    },
                                    new TextEdit
                                    {
                                        Range = range,
                                        NewText = "extracted"
                                    }
                                }
                            }
                        };

                        actions.Add(new CommandOrCodeAction(new CodeAction
                        {
                            Title = "Extract to variable",
                            Kind = CodeActionKind.RefactorExtract,
                            Edit = edit
                        }));
                    }
                }
            }

            // Toggle access modifier
            if (lineText.StartsWith("Public "))
            {
                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(
                                    new Position(startLine, 0),
                                    new Position(startLine, originalLine.Length)),
                                NewText = originalLine.Replace("Public ", "Private ")
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Change to Private",
                    Kind = CodeActionKind.RefactorRewrite,
                    Edit = edit
                }));
            }
            else if (lineText.StartsWith("Private "))
            {
                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(
                                    new Position(startLine, 0),
                                    new Position(startLine, originalLine.Length)),
                                NewText = originalLine.Replace("Private ", "Public ")
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Change to Public",
                    Kind = CodeActionKind.RefactorRewrite,
                    Edit = edit
                }));
            }

            // Add Try-Catch around statement
            if (!lineText.StartsWith("Try") && !lineText.StartsWith("Dim") &&
                !lineText.StartsWith("If") && !lineText.StartsWith("For") &&
                !lineText.StartsWith("While") && !lineText.StartsWith("'"))
            {
                var indent = originalLine.Length - originalLine.TrimStart().Length;
                var indentStr = new string(' ', indent);
                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(
                                    new Position(startLine, 0),
                                    new Position(startLine, originalLine.Length)),
                                NewText = $"{indentStr}Try\n{indentStr}    {originalLine.TrimStart()}\n{indentStr}Catch ex As Exception\n{indentStr}    ' Handle error\n{indentStr}End Try"
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Surround with Try-Catch",
                    Kind = CodeActionKind.Refactor,
                    Edit = edit
                }));
            }

            // Add region
            if (!lineText.StartsWith("#Region"))
            {
                // Find the end of the current block
                int endBlockLine = FindEndOfBlock(state, startLine);
                var indent = originalLine.Length - originalLine.TrimStart().Length;
                var indentStr = new string(' ', indent);
                var edit = new WorkspaceEdit
                {
                    Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                    {
                        [uri] = new[]
                        {
                            new TextEdit
                            {
                                Range = new LspRange(new Position(startLine, 0), new Position(startLine, 0)),
                                NewText = $"{indentStr}#Region \"Region Name\"\n"
                            },
                            new TextEdit
                            {
                                Range = new LspRange(new Position(endBlockLine + 1, 0), new Position(endBlockLine + 1, 0)),
                                NewText = $"{indentStr}#End Region\n"
                            }
                        }
                    }
                };

                actions.Add(new CommandOrCodeAction(new CodeAction
                {
                    Title = "Surround with Region",
                    Kind = CodeActionKind.Refactor,
                    Edit = edit
                }));
            }

            // Extract Method (when selecting multiple lines)
            if (range.Start.Line != range.End.Line)
            {
                int startLineNum = (int)range.Start.Line;
                int endLineNum = (int)range.End.Line;

                if (startLineNum < state.Lines.Length && endLineNum < state.Lines.Length)
                {
                    var selectedLines = new List<string>();
                    for (int i = startLineNum; i <= endLineNum; i++)
                    {
                        selectedLines.Add(state.Lines[i]);
                    }

                    var extractedCode = string.Join("\n", selectedLines.Select(l => "    " + l.TrimStart()));
                    var indent = state.Lines[startLineNum].Length - state.Lines[startLineNum].TrimStart().Length;
                    var indentStr = new string(' ', indent);

                    // Find end of containing Sub/Function
                    int insertPoint = FindEndOfContainingMethod(state, startLineNum);
                    if (insertPoint > 0)
                    {
                        var edit = new WorkspaceEdit
                        {
                            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                            {
                                [uri] = new[]
                                {
                                    // Replace selected code with method call
                                    new TextEdit
                                    {
                                        Range = new LspRange(
                                            new Position(startLineNum, 0),
                                            new Position(endLineNum, state.Lines[endLineNum].Length)),
                                        NewText = $"{indentStr}ExtractedMethod()"
                                    },
                                    // Insert new method after containing method
                                    new TextEdit
                                    {
                                        Range = new LspRange(new Position(insertPoint + 1, 0), new Position(insertPoint + 1, 0)),
                                        NewText = $"\nPrivate Sub ExtractedMethod()\n{extractedCode}\nEnd Sub\n"
                                    }
                                }
                            }
                        };

                        actions.Add(new CommandOrCodeAction(new CodeAction
                        {
                            Title = "Extract Method",
                            Kind = CodeActionKind.RefactorExtract,
                            Edit = edit
                        }));
                    }
                }
            }

            // Generate property from field
            if (lineText.StartsWith("Private ") && lineText.Contains(" As "))
            {
                var match = System.Text.RegularExpressions.Regex.Match(lineText, @"Private\s+(_?\w+)\s+As\s+(\w+)");
                if (match.Success)
                {
                    var fieldName = match.Groups[1].Value;
                    var fieldType = match.Groups[2].Value;
                    var propName = fieldName.StartsWith("_") ? fieldName.Substring(1) : fieldName + "Value";
                    propName = char.ToUpper(propName[0]) + propName.Substring(1);

                    var indent = originalLine.Length - originalLine.TrimStart().Length;
                    var indentStr = new string(' ', indent);

                    var edit = new WorkspaceEdit
                    {
                        Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                        {
                            [uri] = new[]
                            {
                                new TextEdit
                                {
                                    Range = new LspRange(new Position(startLine + 1, 0), new Position(startLine + 1, 0)),
                                    NewText = $"\n{indentStr}Public Property {propName} As {fieldType}\n{indentStr}    Get\n{indentStr}        Return {fieldName}\n{indentStr}    End Get\n{indentStr}    Set(value As {fieldType})\n{indentStr}        {fieldName} = value\n{indentStr}    End Set\n{indentStr}End Property\n"
                                }
                            }
                        }
                    };

                    actions.Add(new CommandOrCodeAction(new CodeAction
                    {
                        Title = $"Generate property '{propName}'",
                        Kind = CodeActionKind.Refactor,
                        Edit = edit
                    }));
                }
            }

            // Add Using statement for unresolved type
            if (lineText.Contains("New ") || lineText.Contains(" As "))
            {
                var typeMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"(?:New\s+|As\s+)(\w+)");
                if (typeMatch.Success)
                {
                    var typeName = typeMatch.Groups[1].Value;
                    // Common .NET type mappings
                    var usingMappings = new Dictionary<string, string>
                    {
                        { "List", "System.Collections.Generic" },
                        { "Dictionary", "System.Collections.Generic" },
                        { "StringBuilder", "System.Text" },
                        { "Regex", "System.Text.RegularExpressions" },
                        { "File", "System.IO" },
                        { "Directory", "System.IO" },
                        { "Path", "System.IO" },
                        { "Stream", "System.IO" },
                        { "HttpClient", "System.Net.Http" },
                        { "Task", "System.Threading.Tasks" },
                        { "Thread", "System.Threading" },
                        { "JsonSerializer", "System.Text.Json" }
                    };

                    if (usingMappings.TryGetValue(typeName, out var usingStatement))
                    {
                        // Check if Using already exists
                        if (!state.Content.Contains($"Using {usingStatement}"))
                        {
                            int insertLine = FindUsingInsertLine(state);
                            var edit = new WorkspaceEdit
                            {
                                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                                {
                                    [uri] = new[]
                                    {
                                        new TextEdit
                                        {
                                            Range = new LspRange(new Position(insertLine, 0), new Position(insertLine, 0)),
                                            NewText = $"Using {usingStatement}\n"
                                        }
                                    }
                                }
                            };

                            actions.Add(new CommandOrCodeAction(new CodeAction
                            {
                                Title = $"Add 'Using {usingStatement}'",
                                Kind = CodeActionKind.QuickFix,
                                Edit = edit
                            }));
                        }
                    }
                }
            }

            // Convert to auto-implemented property
            if (lineText.StartsWith("Public Property ") && !lineText.Contains(" = "))
            {
                // Check if it has a full Get/Set block below
                if (startLine + 1 < state.Lines.Length && state.Lines[startLine + 1].Trim().StartsWith("Get"))
                {
                    // Find End Property
                    int endPropLine = -1;
                    for (int i = startLine + 1; i < state.Lines.Length; i++)
                    {
                        if (state.Lines[i].Trim().StartsWith("End Property"))
                        {
                            endPropLine = i;
                            break;
                        }
                    }

                    if (endPropLine > 0)
                    {
                        var propMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"Property\s+(\w+)\s+As\s+(\w+)");
                        if (propMatch.Success)
                        {
                            var propName = propMatch.Groups[1].Value;
                            var propType = propMatch.Groups[2].Value;
                            var indent = originalLine.Length - originalLine.TrimStart().Length;
                            var indentStr = new string(' ', indent);

                            var edit = new WorkspaceEdit
                            {
                                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                                {
                                    [uri] = new[]
                                    {
                                        new TextEdit
                                        {
                                            Range = new LspRange(
                                                new Position(startLine, 0),
                                                new Position(endPropLine, state.Lines[endPropLine].Length)),
                                            NewText = $"{indentStr}Public Property {propName} As {propType}"
                                        }
                                    }
                                }
                            };

                            actions.Add(new CommandOrCodeAction(new CodeAction
                            {
                                Title = "Convert to auto-implemented property",
                                Kind = CodeActionKind.RefactorRewrite,
                                Edit = edit
                            }));
                        }
                    }
                }
            }

            return actions;
        }

        private int FindEndOfContainingMethod(DocumentState state, int currentLine)
        {
            // Find the start of the containing method
            int methodStart = -1;
            for (int i = currentLine; i >= 0; i--)
            {
                var line = state.Lines[i].Trim();
                if (line.StartsWith("Sub ") || line.StartsWith("Function ") ||
                    line.StartsWith("Public Sub") || line.StartsWith("Public Function") ||
                    line.StartsWith("Private Sub") || line.StartsWith("Private Function"))
                {
                    methodStart = i;
                    break;
                }
            }

            if (methodStart < 0) return -1;

            // Find the end of this method
            for (int i = methodStart + 1; i < state.Lines.Length; i++)
            {
                var line = state.Lines[i].Trim();
                if (line.StartsWith("End Sub") || line.StartsWith("End Function"))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindUsingInsertLine(DocumentState state)
        {
            // Find where to insert Using statements (after Option statements, before other code)
            for (int i = 0; i < state.Lines.Length; i++)
            {
                var line = state.Lines[i].Trim();
                if (line.StartsWith("Using ") || line.StartsWith("Imports "))
                {
                    // Find the last Using line
                    for (int j = i; j < state.Lines.Length; j++)
                    {
                        var nextLine = state.Lines[j].Trim();
                        if (!nextLine.StartsWith("Using ") && !nextLine.StartsWith("Imports ") && !string.IsNullOrEmpty(nextLine))
                        {
                            return j;
                        }
                    }
                }
                else if (!line.StartsWith("Option") && !line.StartsWith("'") && !string.IsNullOrEmpty(line))
                {
                    return i;
                }
            }
            return 0;
        }

        private int FindEndOfBlock(DocumentState state, int startLine)
        {
            if (state?.Lines == null || startLine >= state.Lines.Length)
                return startLine;

            var line = state.Lines[startLine].Trim();
            int depth = 0;

            // Count opening keywords
            if (line.StartsWith("Sub ") || line.StartsWith("Function ") ||
                line.StartsWith("Public Sub") || line.StartsWith("Public Function") ||
                line.StartsWith("Private Sub") || line.StartsWith("Private Function"))
            {
                depth = 1;
                for (int i = startLine + 1; i < state.Lines.Length; i++)
                {
                    var l = state.Lines[i].Trim();
                    if (l.StartsWith("End Sub") || l.StartsWith("End Function"))
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                    else if (l.StartsWith("Sub ") || l.StartsWith("Function "))
                    {
                        depth++;
                    }
                }
            }
            else if (line.StartsWith("Class "))
            {
                depth = 1;
                for (int i = startLine + 1; i < state.Lines.Length; i++)
                {
                    var l = state.Lines[i].Trim();
                    if (l.StartsWith("End Class"))
                    {
                        depth--;
                        if (depth == 0) return i;
                    }
                    else if (l.StartsWith("Class "))
                    {
                        depth++;
                    }
                }
            }

            return startLine;
        }

        private CommandOrCodeAction CreateInsertFix(
            string title,
            DocumentUri uri,
            Position position,
            string text,
            OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic diagnostic)
        {
            var edit = new WorkspaceEdit
            {
                Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>>
                {
                    [uri] = new[]
                    {
                        new TextEdit
                        {
                            Range = new LspRange(position, position),
                            NewText = text
                        }
                    }
                }
            };

            return new CommandOrCodeAction(new CodeAction
            {
                Title = title,
                Kind = CodeActionKind.QuickFix,
                Diagnostics = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic>(diagnostic),
                Edit = edit,
                IsPreferred = true
            });
        }

        private string ExtractVariableName(string message)
        {
            var quoteStart = message.IndexOf('\'');
            if (quoteStart >= 0)
            {
                var quoteEnd = message.IndexOf('\'', quoteStart + 1);
                if (quoteEnd > quoteStart)
                {
                    return message.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                }
            }
            return null;
        }

        private int FindDeclarationInsertLine(DocumentState state, int currentLine)
        {
            if (state?.Lines == null) return currentLine;

            for (int i = currentLine; i >= 0; i--)
            {
                var line = state.Lines[i].Trim();
                if (line.StartsWith("Sub ") || line.StartsWith("Function ") ||
                    line.StartsWith("Public Sub") || line.StartsWith("Public Function") ||
                    line.StartsWith("Private Sub") || line.StartsWith("Private Function"))
                {
                    return i + 1;
                }
            }
            return currentLine;
        }

        private bool RangesOverlap(LspRange a, LspRange b)
        {
            if (a.End.Line < b.Start.Line) return false;
            if (b.End.Line < a.Start.Line) return false;
            if (a.End.Line == b.Start.Line && a.End.Character < b.Start.Character) return false;
            if (b.End.Line == a.Start.Line && b.End.Character < a.Start.Character) return false;
            return true;
        }

        public CodeActionRegistrationOptions GetRegistrationOptions(
            CodeActionCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new CodeActionRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                CodeActionKinds = new Container<CodeActionKind>(
                    CodeActionKind.QuickFix,
                    CodeActionKind.Refactor,
                    CodeActionKind.RefactorRewrite,
                    CodeActionKind.Source
                ),
                ResolveProvider = false
            };
        }
    }
}
