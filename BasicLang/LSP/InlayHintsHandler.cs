using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles inlay hints requests (inline parameter names, type hints)
    /// </summary>
    public class InlayHintsHandler : IInlayHintsHandler
    {
        private readonly DocumentManager _documentManager;
        private readonly Dictionary<string, List<string>> _functionParams;

        public InlayHintsHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
            _functionParams = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
            AddBuiltInParams();
        }

        private void AddBuiltInParams()
        {
            // Common built-in functions with their parameter names
            _functionParams["PrintLine"] = new List<string> { "value" };
            _functionParams["Print"] = new List<string> { "value" };
            _functionParams["ReadLine"] = new List<string>();
            _functionParams["Input"] = new List<string> { "prompt" };
            _functionParams["Len"] = new List<string> { "str" };
            _functionParams["Left"] = new List<string> { "str", "count" };
            _functionParams["Right"] = new List<string> { "str", "count" };
            _functionParams["Mid"] = new List<string> { "str", "start", "length" };
            _functionParams["UCase"] = new List<string> { "str" };
            _functionParams["LCase"] = new List<string> { "str" };
            _functionParams["Trim"] = new List<string> { "str" };
            _functionParams["InStr"] = new List<string> { "str", "search" };
            _functionParams["Replace"] = new List<string> { "str", "oldValue", "newValue" };
            _functionParams["Abs"] = new List<string> { "value" };
            _functionParams["Sqrt"] = new List<string> { "value" };
            _functionParams["Pow"] = new List<string> { "base", "exponent" };
            _functionParams["Floor"] = new List<string> { "value" };
            _functionParams["Ceiling"] = new List<string> { "value" };
            _functionParams["Round"] = new List<string> { "value" };
            _functionParams["Sin"] = new List<string> { "radians" };
            _functionParams["Cos"] = new List<string> { "radians" };
            _functionParams["Tan"] = new List<string> { "radians" };
            _functionParams["Log"] = new List<string> { "value" };
            _functionParams["Exp"] = new List<string> { "value" };
            _functionParams["Min"] = new List<string> { "a", "b" };
            _functionParams["Max"] = new List<string> { "a", "b" };
            _functionParams["CInt"] = new List<string> { "value" };
            _functionParams["CLng"] = new List<string> { "value" };
            _functionParams["CDbl"] = new List<string> { "value" };
            _functionParams["CSng"] = new List<string> { "value" };
            _functionParams["CStr"] = new List<string> { "value" };
            _functionParams["CBool"] = new List<string> { "value" };
        }

        public Task<InlayHintContainer> Handle(InlayHintParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null || state.Lines == null)
            {
                return Task.FromResult(new InlayHintContainer());
            }

            var hints = new List<InlayHint>();

            // Collect user-defined function parameters from AST
            var userFuncParams = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
            CollectUserFunctionParams(state, userFuncParams);

            // Combined function params lookup
            var allFuncParams = new Dictionary<string, List<string>>(_functionParams, System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in userFuncParams)
            {
                allFuncParams[kv.Key] = kv.Value;
            }

            // Scan lines for function calls
            int startLine = (int)request.Range.Start.Line;
            int endLine = (int)request.Range.End.Line;

            for (int lineNum = startLine; lineNum <= endLine && lineNum < state.Lines.Length; lineNum++)
            {
                var line = state.Lines[lineNum];
                AddHintsForLine(line, lineNum, allFuncParams, hints);
            }

            return Task.FromResult(new InlayHintContainer(hints));
        }

        private void CollectUserFunctionParams(DocumentState state, Dictionary<string, List<string>> funcParams)
        {
            if (state?.AST == null) return;

            foreach (var decl in state.AST.Declarations)
            {
                if (decl is BasicLang.Compiler.AST.FunctionNode func)
                {
                    funcParams[func.Name] = func.Parameters.Select(p => p.Name).ToList();
                }
                else if (decl is BasicLang.Compiler.AST.SubroutineNode sub)
                {
                    funcParams[sub.Name] = sub.Parameters.Select(p => p.Name).ToList();
                }
                else if (decl is BasicLang.Compiler.AST.ClassNode classNode)
                {
                    foreach (var member in classNode.Members)
                    {
                        if (member is BasicLang.Compiler.AST.FunctionNode memberFunc)
                        {
                            funcParams[$"{classNode.Name}.{memberFunc.Name}"] = memberFunc.Parameters.Select(p => p.Name).ToList();
                            funcParams[memberFunc.Name] = memberFunc.Parameters.Select(p => p.Name).ToList();
                        }
                        else if (member is BasicLang.Compiler.AST.SubroutineNode memberSub)
                        {
                            funcParams[$"{classNode.Name}.{memberSub.Name}"] = memberSub.Parameters.Select(p => p.Name).ToList();
                            funcParams[memberSub.Name] = memberSub.Parameters.Select(p => p.Name).ToList();
                        }
                    }
                }
            }
        }

        private void AddHintsForLine(
            string line,
            int lineNum,
            Dictionary<string, List<string>> funcParams,
            List<InlayHint> hints)
        {
            // Find function calls in the line using regex
            // Pattern: identifier followed by (
            var pattern = @"\b([A-Za-z_][A-Za-z0-9_]*)\s*\(";
            var matches = Regex.Matches(line, pattern);

            foreach (Match match in matches)
            {
                var funcName = match.Groups[1].Value;

                // Skip keywords that look like function calls
                if (IsKeyword(funcName)) continue;

                if (funcParams.TryGetValue(funcName, out var paramNames) && paramNames.Count > 0)
                {
                    // Find the arguments
                    int parenStart = match.Index + match.Length - 1; // Position of (
                    var args = ExtractArguments(line, parenStart);

                    for (int i = 0; i < args.Count && i < paramNames.Count; i++)
                    {
                        var arg = args[i];

                        // Skip if argument is the same as parameter name
                        if (string.Equals(arg.Text.Trim(), paramNames[i], System.StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // Skip if it's a named argument (contains :=)
                        if (arg.Text.Contains(":=")) continue;

                        hints.Add(new InlayHint
                        {
                            Position = new Position(lineNum, arg.StartColumn),
                            Label = new StringOrInlayHintLabelParts($"{paramNames[i]}:"),
                            Kind = InlayHintKind.Parameter,
                            PaddingRight = true
                        });
                    }
                }
            }
        }

        private List<(string Text, int StartColumn)> ExtractArguments(string line, int parenStart)
        {
            var args = new List<(string Text, int StartColumn)>();

            if (parenStart >= line.Length || line[parenStart] != '(')
                return args;

            int depth = 0;
            int argStart = parenStart + 1;
            int i = parenStart;

            while (i < line.Length)
            {
                char c = line[i];

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        // End of arguments
                        var argText = line.Substring(argStart, i - argStart).Trim();
                        if (!string.IsNullOrEmpty(argText))
                        {
                            args.Add((argText, argStart));
                        }
                        break;
                    }
                }
                else if (c == ',' && depth == 1)
                {
                    // Argument separator at top level
                    var argText = line.Substring(argStart, i - argStart).Trim();
                    args.Add((argText, argStart));
                    argStart = i + 1;
                }
                else if (c == '"')
                {
                    // Skip string literals
                    i++;
                    while (i < line.Length && line[i] != '"')
                    {
                        if (line[i] == '\\' && i + 1 < line.Length)
                            i++;
                        i++;
                    }
                }

                i++;
            }

            return args;
        }

        private bool IsKeyword(string name)
        {
            var keywords = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                "If", "Then", "Else", "ElseIf", "End", "For", "Next", "While", "Wend",
                "Do", "Loop", "Until", "Select", "Case", "Sub", "Function", "Class",
                "Property", "Get", "Set", "Return", "Dim", "Const", "As", "New",
                "True", "False", "Nothing", "And", "Or", "Not", "Mod", "Is",
                "Try", "Catch", "Finally", "Throw", "With", "Using"
            };

            return keywords.Contains(name);
        }

        public InlayHintRegistrationOptions GetRegistrationOptions(
            InlayHintClientCapabilities capability,
            ClientCapabilities clientCapabilities)
        {
            return new InlayHintRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                ResolveProvider = false
            };
        }
    }
}
