using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
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
            // I/O Functions
            ["PrintLine"] = ("PrintLine(text As String)", new[] { "The text to print to the console" }),
            ["Print"] = ("Print(text As String)", new[] { "The text to print without newline" }),
            ["WriteLine"] = ("WriteLine(text As String)", new[] { "The text to write with newline" }),
            ["Write"] = ("Write(text As String)", new[] { "The text to write without newline" }),
            ["ReadLine"] = ("ReadLine() As String", Array.Empty<string>()),
            ["ReadKey"] = ("ReadKey() As String", Array.Empty<string>()),
            ["Input"] = ("Input(prompt As String) As String", new[] { "The prompt to display" }),

            // File I/O
            ["FileRead"] = ("FileRead(path As String) As String", new[] { "The file path to read" }),
            ["FileWrite"] = ("FileWrite(path As String, content As String)", new[] { "The file path", "The content to write" }),
            ["FileAppend"] = ("FileAppend(path As String, content As String)", new[] { "The file path", "The content to append" }),
            ["FileExists"] = ("FileExists(path As String) As Boolean", new[] { "The file path to check" }),
            ["FileDelete"] = ("FileDelete(path As String)", new[] { "The file path to delete" }),
            ["FileCopy"] = ("FileCopy(source As String, destination As String)", new[] { "The source path", "The destination path" }),
            ["FileReadLines"] = ("FileReadLines(path As String) As String()", new[] { "The file path to read" }),

            // Directory Functions
            ["DirExists"] = ("DirExists(path As String) As Boolean", new[] { "The directory path" }),
            ["DirCreate"] = ("DirCreate(path As String)", new[] { "The directory path to create" }),
            ["DirGetFiles"] = ("DirGetFiles(path As String) As String()", new[] { "The directory path" }),

            // Path Functions
            ["PathCombine"] = ("PathCombine(path1 As String, path2 As String) As String", new[] { "First path", "Second path" }),
            ["PathGetFileName"] = ("PathGetFileName(path As String) As String", new[] { "The file path" }),
            ["PathGetDirectory"] = ("PathGetDirectory(path As String) As String", new[] { "The file path" }),
            ["PathGetExtension"] = ("PathGetExtension(path As String) As String", new[] { "The file path" }),

            // String Functions
            ["Len"] = ("Len(str As String) As Integer", new[] { "The string to measure" }),
            ["Left"] = ("Left(str As String, count As Integer) As String", new[] { "The source string", "Number of characters to take" }),
            ["Right"] = ("Right(str As String, count As Integer) As String", new[] { "The source string", "Number of characters to take" }),
            ["Mid"] = ("Mid(str As String, start As Integer, length As Integer) As String", new[] { "The source string", "Starting position (1-based)", "Number of characters" }),
            ["UCase"] = ("UCase(str As String) As String", new[] { "The string to convert" }),
            ["LCase"] = ("LCase(str As String) As String", new[] { "The string to convert" }),
            ["Trim"] = ("Trim(str As String) As String", new[] { "The string to trim" }),
            ["LTrim"] = ("LTrim(str As String) As String", new[] { "The string to trim" }),
            ["RTrim"] = ("RTrim(str As String) As String", new[] { "The string to trim" }),
            ["InStr"] = ("InStr(str As String, search As String) As Integer", new[] { "The string to search in", "The substring to find" }),
            ["InStrRev"] = ("InStrRev(str As String, search As String) As Integer", new[] { "The string to search in", "The substring to find" }),
            ["Replace"] = ("Replace(str As String, old As String, new As String) As String", new[] { "The source string", "The substring to find", "The replacement text" }),
            ["Split"] = ("Split(str As String, delimiter As String) As String()", new[] { "The string to split", "The delimiter" }),
            ["Join"] = ("Join(arr As String(), delimiter As String) As String", new[] { "The array to join", "The delimiter" }),
            ["Format"] = ("Format(value, formatString As String) As String", new[] { "The value to format", "The format string" }),
            ["StartsWith"] = ("StartsWith(str As String, value As String) As Boolean", new[] { "The string to check", "The value to find" }),
            ["EndsWith"] = ("EndsWith(str As String, value As String) As Boolean", new[] { "The string to check", "The value to find" }),
            ["Contains"] = ("Contains(str As String, value As String) As Boolean", new[] { "The string to check", "The value to find" }),
            ["Substring"] = ("Substring(str As String, startIndex As Integer) As String", new[] { "The source string", "The starting index (0-based)" }),
            ["IsNullOrEmpty"] = ("IsNullOrEmpty(str As String) As Boolean", new[] { "The string to check" }),

            // Math Functions
            ["Abs"] = ("Abs(num As Double) As Double", new[] { "The number" }),
            ["Sqrt"] = ("Sqrt(num As Double) As Double", new[] { "The number to get the square root of" }),
            ["Pow"] = ("Pow(base As Double, exponent As Double) As Double", new[] { "The base number", "The exponent" }),
            ["Sin"] = ("Sin(radians As Double) As Double", new[] { "The angle in radians" }),
            ["Cos"] = ("Cos(radians As Double) As Double", new[] { "The angle in radians" }),
            ["Tan"] = ("Tan(radians As Double) As Double", new[] { "The angle in radians" }),
            ["Asin"] = ("Asin(value As Double) As Double", new[] { "The value" }),
            ["Acos"] = ("Acos(value As Double) As Double", new[] { "The value" }),
            ["Atan"] = ("Atan(value As Double) As Double", new[] { "The value" }),
            ["Atan2"] = ("Atan2(y As Double, x As Double) As Double", new[] { "The y coordinate", "The x coordinate" }),
            ["Log"] = ("Log(value As Double) As Double", new[] { "The value" }),
            ["Log10"] = ("Log10(value As Double) As Double", new[] { "The value" }),
            ["Exp"] = ("Exp(value As Double) As Double", new[] { "The exponent" }),
            ["Floor"] = ("Floor(num As Double) As Double", new[] { "The number to round down" }),
            ["Ceiling"] = ("Ceiling(num As Double) As Double", new[] { "The number to round up" }),
            ["Round"] = ("Round(num As Double) As Double", new[] { "The number to round" }),
            ["RoundTo"] = ("RoundTo(num As Double, decimals As Integer) As Double", new[] { "The number", "The decimal places" }),
            ["Min"] = ("Min(a As Double, b As Double) As Double", new[] { "First value", "Second value" }),
            ["Max"] = ("Max(a As Double, b As Double) As Double", new[] { "First value", "Second value" }),
            ["Clamp"] = ("Clamp(value As Double, min As Double, max As Double) As Double", new[] { "The value", "Minimum", "Maximum" }),
            ["Sign"] = ("Sign(value As Double) As Integer", new[] { "The value" }),
            ["Rnd"] = ("Rnd() As Double", Array.Empty<string>()),
            ["RandomInt"] = ("RandomInt(min As Integer, max As Integer) As Integer", new[] { "Minimum value", "Maximum value" }),

            // Type Conversion
            ["CInt"] = ("CInt(value) As Integer", new[] { "The value to convert" }),
            ["CLng"] = ("CLng(value) As Long", new[] { "The value to convert" }),
            ["CDbl"] = ("CDbl(value) As Double", new[] { "The value to convert" }),
            ["CSng"] = ("CSng(value) As Single", new[] { "The value to convert" }),
            ["CStr"] = ("CStr(value) As String", new[] { "The value to convert" }),
            ["CBool"] = ("CBool(value) As Boolean", new[] { "The value to convert" }),
            ["CByte"] = ("CByte(value) As Byte", new[] { "The value to convert" }),
            ["CDate"] = ("CDate(value) As DateTime", new[] { "The value to convert" }),
            ["Val"] = ("Val(str As String) As Double", new[] { "The string to parse" }),

            // DateTime Functions
            ["Now"] = ("Now() As DateTime", Array.Empty<string>()),
            ["Today"] = ("Today() As DateTime", Array.Empty<string>()),
            ["Year"] = ("Year(date As DateTime) As Integer", new[] { "The date" }),
            ["Month"] = ("Month(date As DateTime) As Integer", new[] { "The date" }),
            ["Day"] = ("Day(date As DateTime) As Integer", new[] { "The date" }),
            ["Hour"] = ("Hour(date As DateTime) As Integer", new[] { "The date/time" }),
            ["Minute"] = ("Minute(date As DateTime) As Integer", new[] { "The date/time" }),
            ["Second"] = ("Second(date As DateTime) As Integer", new[] { "The date/time" }),
            ["DateAdd"] = ("DateAdd(date As DateTime, interval As String, number As Integer) As DateTime", new[] { "The date", "The interval (d, m, y, h, n, s)", "The amount" }),
            ["DateDiff"] = ("DateDiff(date1 As DateTime, date2 As DateTime, interval As String) As Long", new[] { "First date", "Second date", "The interval" }),
            ["FormatDate"] = ("FormatDate(date As DateTime, format As String) As String", new[] { "The date", "The format string" }),

            // Array Functions
            ["UBound"] = ("UBound(arr As Array) As Integer", new[] { "The array" }),
            ["LBound"] = ("LBound(arr As Array) As Integer", new[] { "The array" }),
            ["ArrayLength"] = ("ArrayLength(arr As Array) As Integer", new[] { "The array" }),
            ["ArrayIndexOf"] = ("ArrayIndexOf(arr As Array, value) As Integer", new[] { "The array", "The value to find" }),
            ["ArrayContains"] = ("ArrayContains(arr As Array, value) As Boolean", new[] { "The array", "The value to find" }),
            ["ArraySort"] = ("ArraySort(arr As Array)", new[] { "The array to sort" }),
            ["ArrayReverse"] = ("ArrayReverse(arr As Array)", new[] { "The array to reverse" }),

            // Collection Functions
            ["ListAdd"] = ("ListAdd(list As List, item)", new[] { "The list", "The item to add" }),
            ["ListRemove"] = ("ListRemove(list As List, item) As Boolean", new[] { "The list", "The item to remove" }),
            ["ListGet"] = ("ListGet(list As List, index As Integer)", new[] { "The list", "The index" }),
            ["ListCount"] = ("ListCount(list As List) As Integer", new[] { "The list" }),
            ["DictAdd"] = ("DictAdd(dict As Dictionary, key, value)", new[] { "The dictionary", "The key", "The value" }),
            ["DictGet"] = ("DictGet(dict As Dictionary, key)", new[] { "The dictionary", "The key" }),
            ["DictContainsKey"] = ("DictContainsKey(dict As Dictionary, key) As Boolean", new[] { "The dictionary", "The key" }),

            // Character Functions
            ["Chr"] = ("Chr(code As Integer) As Char", new[] { "The character code" }),
            ["Asc"] = ("Asc(char As Char) As Integer", new[] { "The character" }),

            // Environment Functions
            ["Environ"] = ("Environ(name As String) As String", new[] { "The environment variable name" }),
            ["Shell"] = ("Shell(command As String) As Integer", new[] { "The command to execute" }),
            ["Sleep"] = ("Sleep(milliseconds As Integer)", new[] { "The time to sleep in milliseconds" }),

            // Information Functions
            ["TypeName"] = ("TypeName(value) As String", new[] { "The value" }),
            ["IsArray"] = ("IsArray(value) As Boolean", new[] { "The value to check" }),
            ["IsDate"] = ("IsDate(value) As Boolean", new[] { "The value to check" }),
            ["IsNumeric"] = ("IsNumeric(value) As Boolean", new[] { "The value to check" }),
            ["IsNothing"] = ("IsNothing(value) As Boolean", new[] { "The value to check" }),
        };

        public SignatureHelpHandler(DocumentManager documentManager)
        {
            _documentManager = documentManager;
        }

        public override Task<SignatureHelp?> Handle(SignatureHelpParams request, CancellationToken cancellationToken)
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

            // Check if this is a method call on an object (contains a dot)
            if (functionName.Contains('.'))
            {
                var netSig = FindNetMethodSignature(state, functionName);
                if (netSig != null)
                {
                    return Task.FromResult(new SignatureHelp
                    {
                        Signatures = new Container<SignatureInformation>(netSig),
                        ActiveSignature = 0,
                        ActiveParameter = activeParameter
                    });
                }
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
                        // Find the function name (including object.method)
                        functionStart = i - 1;
                        while (functionStart >= 0 && (char.IsLetterOrDigit(lineText[functionStart]) || lineText[functionStart] == '_' || lineText[functionStart] == '.'))
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

        /// <summary>
        /// Find signature for .NET method calls like Console.WriteLine or myList.Add
        /// </summary>
        private SignatureInformation FindNetMethodSignature(DocumentState state, string fullMethodName)
        {
            var dotIndex = fullMethodName.LastIndexOf('.');
            if (dotIndex < 0) return null;

            var typeName = fullMethodName.Substring(0, dotIndex);
            var methodName = fullMethodName.Substring(dotIndex + 1);

            // Try to get type from TypeRegistry
            NetTypeInfo netType = null;

            if (state?.TypeRegistry != null)
            {
                netType = state.TypeRegistry.GetType(typeName);
            }

            if (netType == null && state?.SemanticAnalyzer != null)
            {
                netType = state.SemanticAnalyzer.GetNetType(typeName);
            }

            if (netType == null) return null;

            // Find the method (case-insensitive)
            var method = netType.Members.FirstOrDefault(m =>
                m.Kind == NetMemberKind.Method &&
                m.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase));

            if (method == null) return null;

            // Build signature
            var paramStrs = method.Parameters.Select(p => $"{p.Name} As {p.Type}");
            var returnType = method.ReturnType ?? "Void";
            var signature = $"{typeName}.{method.Name}({string.Join(", ", paramStrs)})" +
                           (returnType != "Void" ? $" As {returnType}" : "");

            var parameters = method.Parameters.Select(p => new ParameterInformation
            {
                Label = $"{p.Name} As {p.Type}",
                Documentation = $"Parameter: {p.Name}"
            }).ToList();

            return new SignatureInformation
            {
                Label = signature,
                Documentation = $".NET method from {netType.FullName}",
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
