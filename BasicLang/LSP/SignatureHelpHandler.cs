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

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Handles signature help requests (parameter hints)
    /// </summary>
    public class SignatureHelpHandler : SignatureHelpHandlerBase
    {
        private readonly DocumentManager _documentManager;

        // Built-in function signatures
        private static readonly Dictionary<string, (string signature, string[] paramDocs)> BuiltInSignatures =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["PrintLine"] = ("PrintLine(text As String)", new[] { "The text to print to the console" }),
            ["Print"] = ("Print(text As String)", new[] { "The text to print without newline" }),
            ["ReadLine"] = ("ReadLine() As String", Array.Empty<string>()),
            ["Len"] = ("Len(str As String) As Integer", new[] { "The string to measure" }),
            ["Left"] = ("Left(str As String, count As Integer) As String", new[] { "The source string", "Number of characters to take" }),
            ["Right"] = ("Right(str As String, count As Integer) As String", new[] { "The source string", "Number of characters to take" }),
            ["Mid"] = ("Mid(str As String, start As Integer, length As Integer) As String", new[] { "The source string", "Starting position (1-based)", "Number of characters" }),
            ["UCase"] = ("UCase(str As String) As String", new[] { "The string to convert" }),
            ["LCase"] = ("LCase(str As String) As String", new[] { "The string to convert" }),
            ["Trim"] = ("Trim(str As String) As String", new[] { "The string to trim" }),
            ["InStr"] = ("InStr(str As String, search As String) As Integer", new[] { "The string to search in", "The substring to find" }),
            ["Replace"] = ("Replace(str As String, old As String, new As String) As String", new[] { "The source string", "The substring to find", "The replacement text" }),
            ["Abs"] = ("Abs(num As Double) As Double", new[] { "The number" }),
            ["Sqrt"] = ("Sqrt(num As Double) As Double", new[] { "The number to get the square root of" }),
            ["Pow"] = ("Pow(base As Double, exponent As Double) As Double", new[] { "The base number", "The exponent" }),
            ["Sin"] = ("Sin(radians As Double) As Double", new[] { "The angle in radians" }),
            ["Cos"] = ("Cos(radians As Double) As Double", new[] { "The angle in radians" }),
            ["Tan"] = ("Tan(radians As Double) As Double", new[] { "The angle in radians" }),
            ["Floor"] = ("Floor(num As Double) As Double", new[] { "The number to round down" }),
            ["Ceiling"] = ("Ceiling(num As Double) As Double", new[] { "The number to round up" }),
            ["Round"] = ("Round(num As Double) As Double", new[] { "The number to round" }),
            ["Min"] = ("Min(a As Double, b As Double) As Double", new[] { "First value", "Second value" }),
            ["Max"] = ("Max(a As Double, b As Double) As Double", new[] { "First value", "Second value" }),
            ["Rnd"] = ("Rnd() As Double", Array.Empty<string>()),
            ["CInt"] = ("CInt(value) As Integer", new[] { "The value to convert" }),
            ["CDbl"] = ("CDbl(value) As Double", new[] { "The value to convert" }),
            ["CStr"] = ("CStr(value) As String", new[] { "The value to convert" }),
            ["CBool"] = ("CBool(value) As Boolean", new[] { "The value to convert" }),
            ["UBound"] = ("UBound(arr As Array) As Integer", new[] { "The array" }),
            ["LBound"] = ("LBound(arr As Array) As Integer", new[] { "The array" }),
            ["Chr"] = ("Chr(code As Integer) As Char", new[] { "The character code" }),
            ["Asc"] = ("Asc(char As Char) As Integer", new[] { "The character" }),
        };

        public SignatureHelpHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<SignatureHelp> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
        {
            var state = _documentManager.GetDocument(request.TextDocument.Uri);
            if (state == null)
            {
                return Task.FromResult<SignatureHelp>(null);
            }

            // Find the function name by looking backwards from the cursor
            var line = request.Position.Line;
            var character = request.Position.Character;

            if (line >= state.Lines.Length)
            {
                return Task.FromResult<SignatureHelp>(null);
            }

            var lineText = state.Lines[line];
            if (character > lineText.Length)
            {
                character = lineText.Length;
            }

            // Find the opening parenthesis and function name
            var (functionName, activeParameter) = FindFunctionContext(lineText, character);
            if (string.IsNullOrEmpty(functionName))
            {
                return Task.FromResult<SignatureHelp>(null);
            }

            // Try built-in functions first
            if (BuiltInSignatures.TryGetValue(functionName, out var builtIn))
            {
                var sig = CreateSignatureInfo(functionName, builtIn.signature, builtIn.paramDocs);
                return Task.FromResult(new SignatureHelp
                {
                    Signatures = new Container<SignatureInformation>(sig),
                    ActiveSignature = 0,
                    ActiveParameter = activeParameter
                });
            }

            // Try user-defined functions
            if (state.AST != null)
            {
                var userSig = FindUserFunctionSignature(state.AST, functionName);
                if (userSig != null)
                {
                    return Task.FromResult(new SignatureHelp
                    {
                        Signatures = new Container<SignatureInformation>(userSig),
                        ActiveSignature = 0,
                        ActiveParameter = activeParameter
                    });
                }
            }

            return Task.FromResult<SignatureHelp>(null);
        }

        private (string functionName, int activeParameter) FindFunctionContext(string lineText, int position)
        {
            int parenDepth = 0;
            int commaCount = 0;
            int functionStart = -1;
            int functionEnd = -1;

            // Scan backwards from the cursor to find the function context
            for (int i = position - 1; i >= 0; i--)
            {
                char c = lineText[i];

                if (c == ')')
                {
                    parenDepth++;
                }
                else if (c == '(')
                {
                    if (parenDepth == 0)
                    {
                        functionEnd = i;
                        // Find the function name
                        functionStart = i - 1;
                        while (functionStart >= 0 && (char.IsLetterOrDigit(lineText[functionStart]) || lineText[functionStart] == '_'))
                        {
                            functionStart--;
                        }
                        functionStart++;
                        break;
                    }
                    parenDepth--;
                }
                else if (c == ',' && parenDepth == 0)
                {
                    commaCount++;
                }
            }

            if (functionStart < 0 || functionEnd < 0 || functionStart >= functionEnd)
            {
                return (null, 0);
            }

            var functionName = lineText.Substring(functionStart, functionEnd - functionStart);
            return (functionName.Trim(), commaCount);
        }

        private SignatureInformation CreateSignatureInfo(string name, string signature, string[] paramDocs)
        {
            var parameters = new List<ParameterInformation>();

            // Parse parameters from signature
            var parenStart = signature.IndexOf('(');
            var parenEnd = signature.LastIndexOf(')');
            if (parenStart >= 0 && parenEnd > parenStart)
            {
                var paramStr = signature.Substring(parenStart + 1, parenEnd - parenStart - 1);
                var paramParts = paramStr.Split(',');

                for (int i = 0; i < paramParts.Length; i++)
                {
                    var param = paramParts[i].Trim();
                    if (!string.IsNullOrEmpty(param))
                    {
                        parameters.Add(new ParameterInformation
                        {
                            Label = param,
                            Documentation = i < paramDocs.Length ? paramDocs[i] : null
                        });
                    }
                }
            }

            return new SignatureInformation
            {
                Label = signature,
                Documentation = $"Built-in function: {name}",
                Parameters = new Container<ParameterInformation>(parameters)
            };
        }

        private SignatureInformation FindUserFunctionSignature(ProgramNode ast, string name)
        {
            foreach (var decl in ast.Declarations)
            {
                switch (decl)
                {
                    case FunctionNode func when func.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                        return CreateUserFunctionSignature(func);

                    case SubroutineNode sub when sub.Name.Equals(name, StringComparison.OrdinalIgnoreCase):
                        return CreateUserSubroutineSignature(sub);

                    case ClassNode cls:
                        foreach (var member in cls.Members)
                        {
                            if (member is FunctionNode mFunc && mFunc.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                return CreateUserFunctionSignature(mFunc);
                            if (member is SubroutineNode mSub && mSub.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                                return CreateUserSubroutineSignature(mSub);
                        }
                        break;
                }
            }
            return null;
        }

        private SignatureInformation CreateUserFunctionSignature(FunctionNode func)
        {
            var paramStrs = func.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}");
            var signature = $"{func.Name}({string.Join(", ", paramStrs)}) As {func.ReturnType?.Name ?? "Void"}";

            var parameters = func.Parameters.Select(p => new ParameterInformation
            {
                Label = $"{p.Name} As {p.Type?.Name ?? "Variant"}",
                Documentation = $"Parameter: {p.Name}"
            }).ToList();

            return new SignatureInformation
            {
                Label = signature,
                Documentation = "User-defined function",
                Parameters = new Container<ParameterInformation>(parameters)
            };
        }

        private SignatureInformation CreateUserSubroutineSignature(SubroutineNode sub)
        {
            var paramStrs = sub.Parameters.Select(p => $"{p.Name} As {p.Type?.Name ?? "Variant"}");
            var signature = $"{sub.Name}({string.Join(", ", paramStrs)})";

            var parameters = sub.Parameters.Select(p => new ParameterInformation
            {
                Label = $"{p.Name} As {p.Type?.Name ?? "Variant"}",
                Documentation = $"Parameter: {p.Name}"
            }).ToList();

            return new SignatureInformation
            {
                Label = signature,
                Documentation = "User-defined subroutine",
                Parameters = new Container<ParameterInformation>(parameters)
            };
        }

        protected override SignatureHelpRegistrationOptions CreateRegistrationOptions(
            SignatureHelpCapability capability,
            ClientCapabilities clientCapabilities)
        {
            return new SignatureHelpRegistrationOptions
            {
                DocumentSelector = TextDocumentSelector.ForLanguage("basiclang"),
                TriggerCharacters = new Container<string>("(", ","),
                RetriggerCharacters = new Container<string>(",")
            };
        }
    }
}
