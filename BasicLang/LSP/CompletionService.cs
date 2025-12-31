using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Service that provides completion items.
    /// Queries the compiler core (SemanticAnalyzer/TypeRegistry) for IntelliSense.
    /// </summary>
    public class CompletionService
    {
        /// <summary>
        /// Get completion items based on context
        /// </summary>
        public List<CompletionItem> GetCompletions(DocumentState state, int line, int character)
        {
            var completions = new List<CompletionItem>();

            // Check if we're completing after a dot (member access)
            var triggerContext = GetTriggerContext(state, line, character);
            if (triggerContext.IsMemberAccess)
            {
                // Get member completions for the object type
                completions.AddRange(GetMemberCompletions(state, triggerContext.ObjectName));
                return completions;
            }

            // Add keywords
            completions.AddRange(GetKeywordCompletions());

            // Add built-in functions
            completions.AddRange(GetBuiltInFunctionCompletions());

            // Add built-in types
            completions.AddRange(GetTypeCompletions());

            // Add symbols from the current document (via SemanticAnalyzer)
            if (state?.SemanticAnalyzer != null)
            {
                completions.AddRange(GetSymbolCompletions(state));

                // Add .NET types from the TypeRegistry
                completions.AddRange(GetNetTypeCompletions(state));
            }

            return completions;
        }

        /// <summary>
        /// Determine if we're completing after a dot and get the object name
        /// </summary>
        private (bool IsMemberAccess, string ObjectName) GetTriggerContext(DocumentState state, int line, int character)
        {
            if (state?.SourceCode == null || line < 0)
                return (false, null);

            var lines = state.SourceCode.Split('\n');
            if (line >= lines.Length)
                return (false, null);

            var currentLine = lines[line];
            if (character <= 0 || character > currentLine.Length)
                return (false, null);

            // Look for a dot before the cursor
            var beforeCursor = currentLine.Substring(0, character).TrimEnd();
            if (!beforeCursor.EndsWith("."))
                return (false, null);

            // Get the object name before the dot
            var withoutDot = beforeCursor.Substring(0, beforeCursor.Length - 1).TrimEnd();
            var objectName = ExtractLastIdentifier(withoutDot);

            return (true, objectName);
        }

        /// <summary>
        /// Extract the last identifier from a string (handles expressions like "obj.Method().Property")
        /// </summary>
        private string ExtractLastIdentifier(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            // Simple case: just an identifier
            var result = new System.Text.StringBuilder();
            for (int i = text.Length - 1; i >= 0; i--)
            {
                var c = text[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    result.Insert(0, c);
                }
                else
                {
                    break;
                }
            }

            return result.Length > 0 ? result.ToString() : null;
        }

        /// <summary>
        /// Get member completions for a type
        /// </summary>
        private IEnumerable<CompletionItem> GetMemberCompletions(DocumentState state, string objectName)
        {
            if (string.IsNullOrEmpty(objectName) || state?.SemanticAnalyzer == null)
                yield break;

            // First, check if it's a .NET type (for static members)
            var netType = state.SemanticAnalyzer.GetNetType(objectName);
            if (netType != null)
            {
                foreach (var member in netType.Members)
                {
                    // For static types, only show static members
                    if (netType.IsStatic && !member.IsStatic)
                        continue;

                    yield return CreateMemberCompletionItem(member);
                }
                yield break;
            }

            // Check if it's a variable with a known type
            var symbol = state.SemanticAnalyzer.GlobalScope?.Resolve(objectName);
            if (symbol != null && symbol.Type != null)
            {
                // Try to get .NET type members
                var typeName = symbol.Type.Name;
                netType = state.SemanticAnalyzer.GetNetType(typeName);
                if (netType != null)
                {
                    foreach (var member in netType.Members)
                    {
                        // For instance variables, show instance members
                        if (member.IsStatic)
                            continue;

                        yield return CreateMemberCompletionItem(member);
                    }
                }

                // Also check user-defined type members
                if (symbol.Type.Members != null)
                {
                    foreach (var memberKvp in symbol.Type.Members)
                    {
                        yield return new CompletionItem
                        {
                            Label = memberKvp.Key,
                            Kind = GetCompletionKind(memberKvp.Value.Kind),
                            Detail = $"{memberKvp.Value.Name} As {memberKvp.Value.Type?.Name ?? "Object"}",
                            InsertText = memberKvp.Key
                        };
                    }
                }
            }
        }

        private CompletionItem CreateMemberCompletionItem(NetMemberInfo member)
        {
            var kind = member.Kind switch
            {
                NetMemberKind.Method => CompletionItemKind.Method,
                NetMemberKind.Property => CompletionItemKind.Property,
                NetMemberKind.Field => CompletionItemKind.Field,
                NetMemberKind.Event => CompletionItemKind.Event,
                NetMemberKind.EnumValue => CompletionItemKind.EnumMember,
                _ => CompletionItemKind.Text
            };

            var detail = member.GetSignature();
            var insertText = member.Name;

            // Add parentheses for methods
            if (member.Kind == NetMemberKind.Method)
            {
                if (member.Parameters.Count == 0)
                {
                    insertText = $"{member.Name}()";
                }
                else
                {
                    // Create snippet with parameter placeholders
                    var paramSnippets = member.Parameters.Select((p, i) => $"${{{i + 1}:{p.Name}}}");
                    insertText = $"{member.Name}({string.Join(", ", paramSnippets)})";
                }
            }

            return new CompletionItem
            {
                Label = member.Name,
                Kind = kind,
                Detail = detail,
                InsertTextFormat = member.Kind == NetMemberKind.Method ? InsertTextFormat.Snippet : InsertTextFormat.PlainText,
                InsertText = insertText
            };
        }

        private CompletionItemKind GetCompletionKind(SemanticAnalysis.SymbolKind kind)
        {
            return kind switch
            {
                SemanticAnalysis.SymbolKind.Function => CompletionItemKind.Function,
                SemanticAnalysis.SymbolKind.Subroutine => CompletionItemKind.Function,
                SemanticAnalysis.SymbolKind.Variable => CompletionItemKind.Variable,
                SemanticAnalysis.SymbolKind.Constant => CompletionItemKind.Constant,
                SemanticAnalysis.SymbolKind.Parameter => CompletionItemKind.Variable,
                SemanticAnalysis.SymbolKind.Class => CompletionItemKind.Class,
                SemanticAnalysis.SymbolKind.Interface => CompletionItemKind.Interface,
                SemanticAnalysis.SymbolKind.Structure => CompletionItemKind.Struct,
                SemanticAnalysis.SymbolKind.Property => CompletionItemKind.Property,
                SemanticAnalysis.SymbolKind.Event => CompletionItemKind.Event,
                _ => CompletionItemKind.Text
            };
        }

        /// <summary>
        /// Get .NET type completions from the TypeRegistry
        /// </summary>
        private IEnumerable<CompletionItem> GetNetTypeCompletions(DocumentState state)
        {
            if (state?.SemanticAnalyzer?.TypeRegistry == null)
                yield break;

            foreach (var netType in state.SemanticAnalyzer.GetLoadedNetTypes())
            {
                var kind = netType.IsInterface ? CompletionItemKind.Interface :
                           netType.IsEnum ? CompletionItemKind.Enum :
                           netType.IsStruct ? CompletionItemKind.Struct :
                           CompletionItemKind.Class;

                var detail = netType.IsStatic ? $"Static Class {netType.FullName}" : $"Class {netType.FullName}";

                yield return new CompletionItem
                {
                    Label = netType.Name,
                    Kind = kind,
                    Detail = detail,
                    InsertText = netType.Name
                };
            }
        }

        private IEnumerable<CompletionItem> GetKeywordCompletions()
        {
            var keywords = new[]
            {
                ("Sub", "Subroutine declaration", "Sub ${1:Name}()\n\t$0\nEnd Sub"),
                ("Function", "Function declaration", "Function ${1:Name}() As ${2:Integer}\n\t$0\nEnd Function"),
                ("If", "If statement", "If ${1:condition} Then\n\t$0\nEnd If"),
                ("If...Else", "If-Else statement", "If ${1:condition} Then\n\t$2\nElse\n\t$0\nEnd If"),
                ("For", "For loop", "For ${1:i} = ${2:1} To ${3:10}\n\t$0\nNext"),
                ("While", "While loop", "While ${1:condition}\n\t$0\nWend"),
                ("Do While", "Do While loop", "Do While ${1:condition}\n\t$0\nLoop"),
                ("Do Until", "Do Until loop", "Do Until ${1:condition}\n\t$0\nLoop"),
                ("Select Case", "Select Case statement", "Select Case ${1:expression}\n\tCase ${2:value}\n\t\t$0\n\tCase Else\n\t\t\nEnd Select"),
                ("Class", "Class declaration", "Class ${1:Name}\n\t$0\nEnd Class"),
                ("Dim", "Variable declaration", "Dim ${1:name} As ${2:Integer}"),
                ("Const", "Constant declaration", "Const ${1:NAME} As ${2:Integer} = ${3:0}"),
                ("Return", "Return statement", "Return ${1:value}"),
                ("Exit", "Exit statement", "Exit ${1|For,While,Do,Sub,Function|}"),
                ("Try", "Try-Catch block", "Try\n\t$0\nCatch ex As Exception\n\t\nEnd Try"),
                ("Property", "Property declaration", "Property ${1:Name} As ${2:Integer}\n\tGet\n\t\tReturn ${3:_value}\n\tEnd Get\n\tSet(value As ${2:Integer})\n\t\t${3:_value} = value\n\tEnd Set\nEnd Property"),

                // Additional common patterns
                ("For Each", "For Each loop", "For Each ${1:item} In ${2:collection}\n\t$0\nNext"),
                ("Interface", "Interface declaration", "Interface ${1:IName}\n\t$0\nEnd Interface"),
                ("Enum", "Enumeration declaration", "Enum ${1:Name}\n\t${2:Value1}\n\t${3:Value2}\nEnd Enum"),
                ("Structure", "Structure declaration", "Structure ${1:Name}\n\tPublic ${2:field} As ${3:Integer}\nEnd Structure"),
                ("With", "With block", "With ${1:object}\n\t$0\nEnd With"),
                ("Using", "Using statement", "Using ${1:resource} = ${2:New Resource()}\n\t$0\nEnd Using"),
                ("If...ElseIf", "If-ElseIf-Else statement", "If ${1:condition1} Then\n\t$2\nElseIf ${3:condition2} Then\n\t$4\nElse\n\t$0\nEnd If"),
                ("Function Async", "Async function declaration", "Async Function ${1:Name}() As Task(Of ${2:Integer})\n\t$0\nEnd Function"),
                ("Sub Async", "Async subroutine declaration", "Async Sub ${1:Name}()\n\t$0\nEnd Sub"),
                ("Lambda", "Lambda expression", "Function(${1:x}) ${2:x + 1}"),
                ("Event", "Event declaration", "Event ${1:OnEvent}(sender As Object, e As EventArgs)"),
                ("RaiseEvent", "Raise event", "RaiseEvent ${1:OnEvent}(Me, EventArgs.Empty)"),
                ("Implements Method", "Method implementing interface", "Public Function ${1:MethodName}(${2:param} As ${3:Integer}) As ${4:Integer} Implements ${5:IInterface}.${1:MethodName}\n\t$0\nEnd Function"),
                ("Overrides Method", "Method overriding base", "Public Overrides Function ${1:MethodName}(${2:param} As ${3:Integer}) As ${4:Integer}\n\t$0\nEnd Function"),
                ("Constructor", "Class constructor", "Public Sub New(${1:param} As ${2:Integer})\n\t$0\nEnd Sub"),
                ("Singleton", "Singleton pattern", "Private Shared _instance As ${1:ClassName}\nPrivate Sub New()\nEnd Sub\n\nPublic Shared ReadOnly Property Instance As ${1:ClassName}\n\tGet\n\t\tIf _instance Is Nothing Then\n\t\t\t_instance = New ${1:ClassName}()\n\t\tEnd If\n\t\tReturn _instance\n\tEnd Get\nEnd Property"),
                ("IDisposable", "IDisposable implementation", "Private _disposed As Boolean = False\n\nProtected Overridable Sub Dispose(disposing As Boolean)\n\tIf Not _disposed Then\n\t\tIf disposing Then\n\t\t\t' Dispose managed resources\n\t\t\t$0\n\t\tEnd If\n\t\t_disposed = True\n\tEnd If\nEnd Sub\n\nPublic Sub Dispose()\n\tDispose(True)\nEnd Sub"),
            };

            foreach (var (label, detail, snippet) in keywords)
            {
                yield return new CompletionItem
                {
                    Label = label,
                    Kind = CompletionItemKind.Keyword,
                    Detail = detail,
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = snippet
                };
            }

            // Simple keywords without snippets
            var simpleKeywords = new[]
            {
                "And", "Or", "Not", "Xor", "Mod",
                "True", "False", "Nothing",
                "Public", "Private", "Protected",
                "Shared", "Overridable", "Overrides",
                "Inherits", "Implements",
                "Me", "MyBase",
                "New", "As", "Of",
                "Then", "Else", "ElseIf",
                "End", "Next", "Wend", "Loop",
                "To", "Step", "Each", "In"
            };

            foreach (var keyword in simpleKeywords)
            {
                yield return new CompletionItem
                {
                    Label = keyword,
                    Kind = CompletionItemKind.Keyword,
                    InsertText = keyword
                };
            }
        }

        private IEnumerable<CompletionItem> GetBuiltInFunctionCompletions()
        {
            var functions = new[]
            {
                // Console I/O
                ("PrintLine", "Prints a line to the console", "PrintLine(${1:text})", "Sub"),
                ("Print", "Prints to the console without newline", "Print(${1:text})", "Sub"),
                ("ReadLine", "Reads a line from the console", "ReadLine()", "String"),
                ("ReadKey", "Reads a key press", "ReadKey()", "String"),

                // String functions
                ("Len", "Returns the length of a string", "Len(${1:str})", "Integer"),
                ("Left", "Returns leftmost characters", "Left(${1:str}, ${2:count})", "String"),
                ("Right", "Returns rightmost characters", "Right(${1:str}, ${2:count})", "String"),
                ("Mid", "Returns a substring", "Mid(${1:str}, ${2:start}, ${3:length})", "String"),
                ("UCase", "Converts to uppercase", "UCase(${1:str})", "String"),
                ("LCase", "Converts to lowercase", "LCase(${1:str})", "String"),
                ("Trim", "Removes leading/trailing whitespace", "Trim(${1:str})", "String"),
                ("LTrim", "Removes leading whitespace", "LTrim(${1:str})", "String"),
                ("RTrim", "Removes trailing whitespace", "RTrim(${1:str})", "String"),
                ("InStr", "Finds substring position", "InStr(${1:str}, ${2:search})", "Integer"),
                ("Replace", "Replaces occurrences in string", "Replace(${1:str}, ${2:old}, ${3:new})", "String"),
                ("Split", "Splits string into array", "Split(${1:str}, ${2:delimiter})", "String()"),
                ("Join", "Joins array into string", "Join(${1:arr}, ${2:delimiter})", "String"),

                // Math functions
                ("Abs", "Returns absolute value", "Abs(${1:num})", "Double"),
                ("Sqrt", "Returns square root", "Sqrt(${1:num})", "Double"),
                ("Pow", "Returns power", "Pow(${1:base}, ${2:exponent})", "Double"),
                ("Sin", "Returns sine", "Sin(${1:radians})", "Double"),
                ("Cos", "Returns cosine", "Cos(${1:radians})", "Double"),
                ("Tan", "Returns tangent", "Tan(${1:radians})", "Double"),
                ("Log", "Returns natural logarithm", "Log(${1:num})", "Double"),
                ("Log10", "Returns base-10 logarithm", "Log10(${1:num})", "Double"),
                ("Exp", "Returns e raised to power", "Exp(${1:num})", "Double"),
                ("Floor", "Rounds down", "Floor(${1:num})", "Double"),
                ("Ceiling", "Rounds up", "Ceiling(${1:num})", "Double"),
                ("Round", "Rounds to nearest", "Round(${1:num})", "Double"),
                ("Min", "Returns minimum", "Min(${1:a}, ${2:b})", "Double"),
                ("Max", "Returns maximum", "Max(${1:a}, ${2:b})", "Double"),
                ("Rnd", "Returns random number 0-1", "Rnd()", "Double"),
                ("Randomize", "Seeds random generator", "Randomize(${1:seed})", "Sub"),

                // Type conversion
                ("CInt", "Converts to Integer", "CInt(${1:value})", "Integer"),
                ("CLng", "Converts to Long", "CLng(${1:value})", "Long"),
                ("CDbl", "Converts to Double", "CDbl(${1:value})", "Double"),
                ("CSng", "Converts to Single", "CSng(${1:value})", "Single"),
                ("CStr", "Converts to String", "CStr(${1:value})", "String"),
                ("CBool", "Converts to Boolean", "CBool(${1:value})", "Boolean"),
                ("Chr", "Converts ASCII to character", "Chr(${1:code})", "Char"),
                ("Asc", "Converts character to ASCII", "Asc(${1:char})", "Integer"),
                ("Val", "Converts string to number", "Val(${1:str})", "Double"),

                // Array functions
                ("UBound", "Returns upper bound of array", "UBound(${1:arr})", "Integer"),
                ("LBound", "Returns lower bound of array", "LBound(${1:arr})", "Integer"),
                ("Array", "Creates an array", "Array(${1:values})", "Variant()"),

                // Collection functions
                ("CreateList", "Creates a new List", "CreateList()", "List"),
                ("ListAdd", "Adds item to list", "ListAdd(${1:list}, ${2:item})", "Sub"),
                ("ListGet", "Gets item from list", "ListGet(${1:list}, ${2:index})", "Object"),
                ("ListSet", "Sets item in list", "ListSet(${1:list}, ${2:index}, ${3:value})", "Sub"),
                ("ListCount", "Gets list count", "ListCount(${1:list})", "Integer"),
                ("ListRemove", "Removes item from list", "ListRemove(${1:list}, ${2:index})", "Sub"),
                ("ListContains", "Checks if list contains item", "ListContains(${1:list}, ${2:item})", "Boolean"),
                ("CreateDictionary", "Creates a new Dictionary", "CreateDictionary()", "Dictionary"),
                ("DictSet", "Sets key-value in dictionary", "DictSet(${1:dict}, ${2:key}, ${3:value})", "Sub"),
                ("DictGet", "Gets value from dictionary", "DictGet(${1:dict}, ${2:key})", "Object"),
                ("DictRemove", "Removes key from dictionary", "DictRemove(${1:dict}, ${2:key})", "Sub"),
                ("DictContainsKey", "Checks if dictionary has key", "DictContainsKey(${1:dict}, ${2:key})", "Boolean"),
                ("CreateHashSet", "Creates a new HashSet", "CreateHashSet()", "HashSet"),
                ("SetAdd", "Adds item to set", "SetAdd(${1:set}, ${2:item})", "Boolean"),
                ("SetContains", "Checks if set contains item", "SetContains(${1:set}, ${2:item})", "Boolean"),
                ("SetRemove", "Removes item from set", "SetRemove(${1:set}, ${2:item})", "Boolean"),

                // LINQ-style functions
                ("Where", "Filters collection", "Where(${1:collection}, Function(x) ${2:x > 0})", "IEnumerable"),
                ("Select", "Projects collection", "Select(${1:collection}, Function(x) ${2:x * 2})", "IEnumerable"),
                ("OrderBy", "Sorts collection", "OrderBy(${1:collection}, Function(x) ${2:x})", "IEnumerable"),
                ("FirstOrDefault", "Gets first or default", "FirstOrDefault(${1:collection})", "Object"),
                ("LastOrDefault", "Gets last or default", "LastOrDefault(${1:collection})", "Object"),
                ("ToList", "Converts to list", "ToList(${1:collection})", "List"),
                ("ToArray", "Converts to array", "ToArray(${1:collection})", "Array"),
            };

            foreach (var (name, detail, snippet, returnType) in functions)
            {
                yield return new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Function,
                    Detail = $"{detail} -> {returnType}",
                    InsertTextFormat = InsertTextFormat.Snippet,
                    InsertText = snippet
                };
            }
        }

        private IEnumerable<CompletionItem> GetTypeCompletions()
        {
            var types = new[]
            {
                ("Integer", "32-bit signed integer"),
                ("Long", "64-bit signed integer"),
                ("Single", "32-bit floating point"),
                ("Double", "64-bit floating point"),
                ("String", "Text string"),
                ("Boolean", "True or False"),
                ("Char", "Single character"),
                ("Byte", "8-bit unsigned integer"),
                ("Object", "Base type for all objects"),
                ("Variant", "Can hold any type"),
            };

            foreach (var (name, detail) in types)
            {
                yield return new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Class,
                    Detail = detail,
                    InsertText = name
                };
            }
        }

        private IEnumerable<CompletionItem> GetSymbolCompletions(DocumentState state)
        {
            if (state.AST == null) yield break;

            // Add declared functions and subs
            foreach (var decl in state.AST.Declarations)
            {
                if (decl is BasicLang.Compiler.AST.FunctionNode func)
                {
                    yield return new CompletionItem
                    {
                        Label = func.Name,
                        Kind = CompletionItemKind.Function,
                        Detail = $"Function {func.Name}() As {func.ReturnType?.Name ?? "Void"}",
                        InsertText = func.Name
                    };
                }
                else if (decl is BasicLang.Compiler.AST.SubroutineNode sub)
                {
                    yield return new CompletionItem
                    {
                        Label = sub.Name,
                        Kind = CompletionItemKind.Function,
                        Detail = $"Sub {sub.Name}()",
                        InsertText = sub.Name
                    };
                }
                else if (decl is BasicLang.Compiler.AST.ClassNode cls)
                {
                    yield return new CompletionItem
                    {
                        Label = cls.Name,
                        Kind = CompletionItemKind.Class,
                        Detail = $"Class {cls.Name}",
                        InsertText = cls.Name
                    };
                }
                else if (decl is BasicLang.Compiler.AST.VariableDeclarationNode varDecl)
                {
                    yield return new CompletionItem
                    {
                        Label = varDecl.Name,
                        Kind = CompletionItemKind.Variable,
                        Detail = $"{varDecl.Name} As {varDecl.Type?.Name ?? "Variant"}",
                        InsertText = varDecl.Name
                    };
                }
            }
        }
    }
}
