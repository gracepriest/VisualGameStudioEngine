using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Completion context types for context-aware completions
    /// </summary>
    public enum CompletionContextType
    {
        General,        // Default - show everything
        Import,         // After "Import " - show module names
        New,            // After "New " - show class types
        AsType,         // After "As " - show types
        MemberAccess,   // After "." - show members
        Implements,     // After "Implements " - show interfaces
        Inherits        // After "Inherits " - show classes
    }

    /// <summary>
    /// Extended trigger context with more information
    /// </summary>
    public class TriggerContext
    {
        public CompletionContextType ContextType { get; set; } = CompletionContextType.General;
        public bool IsMemberAccess => ContextType == CompletionContextType.MemberAccess;
        public string ObjectName { get; set; }
        public string FilterPrefix { get; set; }
    }

    /// <summary>
    /// Service that provides completion items.
    /// Queries the compiler core (SemanticAnalyzer/TypeRegistry) for IntelliSense.
    /// </summary>
    public class CompletionService
    {
        // Available modules for Import completion (can be set externally)
        private List<string> _availableModules = new List<string>();

        /// <summary>
        /// Set the available modules for Import statement completion
        /// </summary>
        public void SetAvailableModules(IEnumerable<string> modules)
        {
            _availableModules = modules?.ToList() ?? new List<string>();
        }

        /// <summary>
        /// Get completion items based on context
        /// </summary>
        public List<CompletionItem> GetCompletions(DocumentState? state, int line, int character)
        {
            var completions = new List<CompletionItem>();
            TriggerContext triggerContext = null;

            // Check context - only if we have a document
            if (state != null)
            {
                triggerContext = GetTriggerContext(state, line, character);

                // Handle context-specific completions
                switch (triggerContext.ContextType)
                {
                    case CompletionContextType.MemberAccess:
                        // Get member completions for the object type
                        var memberCompletions = GetMemberCompletions(state, triggerContext.ObjectName, line, character).ToList();
                        completions.AddRange(memberCompletions);
                        return ApplyFuzzyFilter(completions, triggerContext.FilterPrefix);

                    case CompletionContextType.Import:
                        // Show available modules for import
                        completions.AddRange(GetImportCompletions(state));
                        return ApplyFuzzyFilter(completions, triggerContext.FilterPrefix);

                    case CompletionContextType.New:
                        // Show only instantiable types (classes)
                        completions.AddRange(GetNewCompletions(state));
                        return ApplyFuzzyFilter(completions, triggerContext.FilterPrefix);

                    case CompletionContextType.AsType:
                        // Show all types for type annotations
                        completions.AddRange(GetAsTypeCompletions(state));
                        return ApplyFuzzyFilter(completions, triggerContext.FilterPrefix);

                    case CompletionContextType.Implements:
                        // Show interfaces
                        completions.AddRange(GetInterfaceCompletions(state));
                        return ApplyFuzzyFilter(completions, triggerContext.FilterPrefix);

                    case CompletionContextType.Inherits:
                        // Show classes for inheritance
                        completions.AddRange(GetClassCompletions(state));
                        return ApplyFuzzyFilter(completions, triggerContext.FilterPrefix);
                }
            }

            // General completions (default context)
            // Add keywords
            completions.AddRange(GetKeywordCompletions());

            // Add built-in functions
            completions.AddRange(GetBuiltInFunctionCompletions());

            // Add built-in types
            completions.AddRange(GetTypeCompletions());

            // Add symbols from the current document
            // Try AST-based extraction first (works even when semantic analysis fails)
            if (state?.AST != null)
            {
                completions.AddRange(GetSymbolCompletions(state, line, character));
            }

            // Add .NET types from the TypeRegistry if available
            if (state?.SemanticAnalyzer != null)
            {
                completions.AddRange(GetNetTypeCompletions(state));
            }

            // Apply fuzzy filter
            return ApplyFuzzyFilter(completions, triggerContext?.FilterPrefix);
        }

        /// <summary>
        /// Apply fuzzy filtering with scoring
        /// </summary>
        private List<CompletionItem> ApplyFuzzyFilter(List<CompletionItem> completions, string filterPrefix)
        {
            if (string.IsNullOrEmpty(filterPrefix))
                return completions;

            var scored = completions
                .Select(c => new { Item = c, Score = CalculateFuzzyScore(c.Label, filterPrefix) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Item.Label.Length)
                .Select(x =>
                {
                    // Create new item with sort text based on score (lower = better for VS Code sorting)
                    return new CompletionItem
                    {
                        Label = x.Item.Label,
                        Kind = x.Item.Kind,
                        Detail = x.Item.Detail,
                        Documentation = x.Item.Documentation,
                        InsertText = x.Item.InsertText,
                        InsertTextFormat = x.Item.InsertTextFormat,
                        SortText = $"{(10000 - x.Score):D5}_{x.Item.Label}",
                        FilterText = x.Item.FilterText,
                        Data = x.Item.Data
                    };
                })
                .ToList();

            return scored;
        }

        /// <summary>
        /// Calculate fuzzy match score (higher = better match)
        /// </summary>
        private int CalculateFuzzyScore(string label, string pattern)
        {
            if (string.IsNullOrEmpty(label) || string.IsNullOrEmpty(pattern))
                return 0;

            var labelLower = label.ToLowerInvariant();
            var patternLower = pattern.ToLowerInvariant();

            // Exact prefix match - highest score
            if (labelLower.StartsWith(patternLower))
                return 1000 + (100 - label.Length);

            // Case-sensitive prefix match - very high score
            if (label.StartsWith(pattern))
                return 900 + (100 - label.Length);

            // CamelCase match (e.g., "cra" matches "createApplication")
            var camelScore = CalculateCamelCaseScore(label, pattern);
            if (camelScore > 0)
                return 500 + camelScore;

            // Substring match
            var substringIndex = labelLower.IndexOf(patternLower);
            if (substringIndex >= 0)
                return 300 - substringIndex;

            // Fuzzy character match (all chars present in order)
            var fuzzyScore = CalculateSequentialCharScore(labelLower, patternLower);
            if (fuzzyScore > 0)
                return fuzzyScore;

            return 0;
        }

        /// <summary>
        /// Calculate CamelCase matching score
        /// </summary>
        private int CalculateCamelCaseScore(string label, string pattern)
        {
            var patternLower = pattern.ToLowerInvariant();
            int patternIndex = 0;
            int score = 0;
            bool lastWasMatch = false;

            for (int i = 0; i < label.Length && patternIndex < patternLower.Length; i++)
            {
                bool isUpperCase = char.IsUpper(label[i]);
                bool isWordStart = i == 0 || isUpperCase || (i > 0 && !char.IsLetterOrDigit(label[i - 1]));

                if (char.ToLowerInvariant(label[i]) == patternLower[patternIndex])
                {
                    patternIndex++;
                    // Bonus for matching at word boundaries
                    score += isWordStart ? 10 : (lastWasMatch ? 5 : 1);
                    lastWasMatch = true;
                }
                else
                {
                    lastWasMatch = false;
                }
            }

            return patternIndex == patternLower.Length ? score : 0;
        }

        /// <summary>
        /// Calculate score for sequential character matching
        /// </summary>
        private int CalculateSequentialCharScore(string label, string pattern)
        {
            int patternIndex = 0;
            int consecutiveBonus = 0;

            for (int i = 0; i < label.Length && patternIndex < pattern.Length; i++)
            {
                if (label[i] == pattern[patternIndex])
                {
                    patternIndex++;
                    consecutiveBonus++;
                }
                else
                {
                    consecutiveBonus = 0;
                }
            }

            return patternIndex == pattern.Length ? 100 + consecutiveBonus * 10 : 0;
        }

        /// <summary>
        /// Get completions for Import statements (module names)
        /// </summary>
        private IEnumerable<CompletionItem> GetImportCompletions(DocumentState state)
        {
            var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add modules set externally (from project)
            foreach (var mod in _availableModules)
            {
                modules.Add(mod);
            }

            // Add modules found in current file's directory (scan for .bas files)
            var filePath = state?.Uri?.GetFileSystemPath();
            if (!string.IsNullOrEmpty(filePath))
            {
                var dir = System.IO.Path.GetDirectoryName(filePath);
                if (System.IO.Directory.Exists(dir))
                {
                    foreach (var file in System.IO.Directory.GetFiles(dir, "*.bas"))
                    {
                        var moduleName = System.IO.Path.GetFileNameWithoutExtension(file);
                        if (!moduleName.Equals(System.IO.Path.GetFileNameWithoutExtension(filePath), StringComparison.OrdinalIgnoreCase))
                        {
                            modules.Add(moduleName);
                        }
                    }
                    foreach (var file in System.IO.Directory.GetFiles(dir, "*.mod"))
                    {
                        var moduleName = System.IO.Path.GetFileNameWithoutExtension(file);
                        modules.Add(moduleName);
                    }
                }
            }

            // Add common .NET namespaces as module suggestions
            var netModules = new[] { "System", "System.IO", "System.Collections", "System.Text", "System.Net" };
            foreach (var mod in netModules)
            {
                modules.Add(mod);
            }

            return modules.Select(m => new CompletionItem
            {
                Label = m,
                Kind = CompletionItemKind.Module,
                Detail = "Module",
                InsertText = m
            });
        }

        /// <summary>
        /// Get completions for "New" keyword (instantiable types)
        /// </summary>
        private IEnumerable<CompletionItem> GetNewCompletions(DocumentState state)
        {
            var types = new List<CompletionItem>();

            // Built-in collection types
            types.Add(CreateTypeCompletion("List", "List(Of T)", "Generic list collection", "List(Of ${1:Type})"));
            types.Add(CreateTypeCompletion("Dictionary", "Dictionary(Of TKey, TValue)", "Key-value dictionary", "Dictionary(Of ${1:KeyType}, ${2:ValueType})"));
            types.Add(CreateTypeCompletion("ArrayList", "ArrayList", "Non-generic list", "ArrayList()"));
            types.Add(CreateTypeCompletion("Hashtable", "Hashtable", "Non-generic dictionary", "Hashtable()"));
            types.Add(CreateTypeCompletion("StringBuilder", "StringBuilder", "Mutable string builder", "StringBuilder()"));
            types.Add(CreateTypeCompletion("StreamReader", "StreamReader", "Text file reader", "StreamReader(${1:path})"));
            types.Add(CreateTypeCompletion("StreamWriter", "StreamWriter", "Text file writer", "StreamWriter(${1:path})"));
            types.Add(CreateTypeCompletion("HttpClient", "HttpClient", "HTTP client for web requests", "HttpClient()"));
            types.Add(CreateTypeCompletion("Regex", "Regex", "Regular expression", "Regex(${1:pattern})"));
            types.Add(CreateTypeCompletion("Timer", "Timer", "Timer for periodic events", "Timer()"));
            types.Add(CreateTypeCompletion("Random", "Random", "Random number generator", "Random()"));
            types.Add(CreateTypeCompletion("Stopwatch", "Stopwatch", "High-resolution timer", "Stopwatch()"));

            // User-defined classes from AST
            if (state?.AST != null)
            {
                foreach (var decl in state.AST.Declarations)
                {
                    if (decl is ClassNode cls)
                    {
                        types.Add(new CompletionItem
                        {
                            Label = cls.Name,
                            Kind = CompletionItemKind.Class,
                            Detail = "Class",
                            InsertText = $"{cls.Name}()",
                            InsertTextFormat = InsertTextFormat.PlainText
                        });
                    }
                }
            }

            return types;
        }

        private CompletionItem CreateTypeCompletion(string name, string detail, string doc, string insertText)
        {
            return new CompletionItem
            {
                Label = name,
                Kind = CompletionItemKind.Class,
                Detail = detail,
                Documentation = doc,
                InsertText = insertText,
                InsertTextFormat = InsertTextFormat.Snippet
            };
        }

        /// <summary>
        /// Get completions for "As" type annotations
        /// </summary>
        private IEnumerable<CompletionItem> GetAsTypeCompletions(DocumentState state)
        {
            var types = new List<CompletionItem>();

            // Built-in types
            types.AddRange(GetTypeCompletions());

            // Generic types
            types.Add(new CompletionItem { Label = "List(Of", Kind = CompletionItemKind.Class, Detail = "Generic List", InsertText = "List(Of ${1:Type})", InsertTextFormat = InsertTextFormat.Snippet });
            types.Add(new CompletionItem { Label = "Dictionary(Of", Kind = CompletionItemKind.Class, Detail = "Generic Dictionary", InsertText = "Dictionary(Of ${1:KeyType}, ${2:ValueType})", InsertTextFormat = InsertTextFormat.Snippet });
            types.Add(new CompletionItem { Label = "IEnumerable(Of", Kind = CompletionItemKind.Interface, Detail = "Generic IEnumerable", InsertText = "IEnumerable(Of ${1:Type})", InsertTextFormat = InsertTextFormat.Snippet });

            // Tuple types
            types.Add(new CompletionItem { Label = "(", Kind = CompletionItemKind.Struct, Detail = "Tuple type", InsertText = "(${1:Type1}, ${2:Type2})", InsertTextFormat = InsertTextFormat.Snippet });
            types.Add(new CompletionItem { Label = "Tuple", Kind = CompletionItemKind.Struct, Detail = "Named tuple type", InsertText = "(${1:x} As ${2:Integer}, ${3:y} As ${4:Integer})", InsertTextFormat = InsertTextFormat.Snippet });

            // User-defined types from AST
            if (state?.AST != null)
            {
                foreach (var decl in state.AST.Declarations)
                {
                    switch (decl)
                    {
                        case ClassNode cls:
                            types.Add(new CompletionItem { Label = cls.Name, Kind = CompletionItemKind.Class, Detail = "Class" });
                            break;
                        case StructureNode str:
                            types.Add(new CompletionItem { Label = str.Name, Kind = CompletionItemKind.Struct, Detail = "Structure" });
                            break;
                        case EnumNode en:
                            types.Add(new CompletionItem { Label = en.Name, Kind = CompletionItemKind.Enum, Detail = "Enum" });
                            break;
                        case InterfaceNode iface:
                            types.Add(new CompletionItem { Label = iface.Name, Kind = CompletionItemKind.Interface, Detail = "Interface" });
                            break;
                    }
                }
            }

            return types;
        }

        /// <summary>
        /// Get interface completions for "Implements"
        /// </summary>
        private IEnumerable<CompletionItem> GetInterfaceCompletions(DocumentState state)
        {
            var interfaces = new List<CompletionItem>();

            // Common .NET interfaces
            interfaces.Add(new CompletionItem { Label = "IDisposable", Kind = CompletionItemKind.Interface, Detail = "Interface", Documentation = "Provides a mechanism for releasing unmanaged resources" });
            interfaces.Add(new CompletionItem { Label = "IComparable", Kind = CompletionItemKind.Interface, Detail = "Interface", Documentation = "Defines a comparison method" });
            interfaces.Add(new CompletionItem { Label = "IEnumerable", Kind = CompletionItemKind.Interface, Detail = "Interface", Documentation = "Exposes an enumerator for iteration" });
            interfaces.Add(new CompletionItem { Label = "ICloneable", Kind = CompletionItemKind.Interface, Detail = "Interface", Documentation = "Supports cloning" });
            interfaces.Add(new CompletionItem { Label = "IEquatable", Kind = CompletionItemKind.Interface, Detail = "Interface", Documentation = "Defines equality comparison" });

            // User-defined interfaces from AST
            if (state?.AST != null)
            {
                foreach (var decl in state.AST.Declarations)
                {
                    if (decl is InterfaceNode iface)
                    {
                        interfaces.Add(new CompletionItem
                        {
                            Label = iface.Name,
                            Kind = CompletionItemKind.Interface,
                            Detail = "Interface"
                        });
                    }
                }
            }

            return interfaces;
        }

        /// <summary>
        /// Get class completions for "Inherits"
        /// </summary>
        private IEnumerable<CompletionItem> GetClassCompletions(DocumentState state)
        {
            var classes = new List<CompletionItem>();

            // Common base classes
            classes.Add(new CompletionItem { Label = "Object", Kind = CompletionItemKind.Class, Detail = "Base class for all types" });
            classes.Add(new CompletionItem { Label = "Exception", Kind = CompletionItemKind.Class, Detail = "Base class for exceptions" });
            classes.Add(new CompletionItem { Label = "EventArgs", Kind = CompletionItemKind.Class, Detail = "Base class for event data" });
            classes.Add(new CompletionItem { Label = "Stream", Kind = CompletionItemKind.Class, Detail = "Abstract base class for streams" });

            // User-defined classes from AST
            if (state?.AST != null)
            {
                foreach (var decl in state.AST.Declarations)
                {
                    if (decl is ClassNode cls && !cls.IsAbstract)
                    {
                        classes.Add(new CompletionItem
                        {
                            Label = cls.Name,
                            Kind = CompletionItemKind.Class,
                            Detail = "Class"
                        });
                    }
                }
            }

            return classes;
        }

        /// <summary>
        /// Determine completion context based on cursor position
        /// </summary>
        private TriggerContext GetTriggerContext(DocumentState state, int line, int character)
        {
            var context = new TriggerContext();

            if (state?.SourceCode == null)
                return context;

            var lines = state.SourceCode.Split('\n');

            // Handle edge cases for line number
            if (line < 0) line = 0;
            if (line >= lines.Length)
            {
                line = Math.Max(0, lines.Length - 1);
            }

            var currentLine = lines[line];

            // Clamp character to valid range - allow 0 to Length (inclusive for end of line)
            character = Math.Max(0, Math.Min(character, currentLine.Length));

            if (currentLine.Length == 0)
            {
                return context;
            }

            // Get the text before the cursor
            var beforeCursor = currentLine.Substring(0, character);
            var trimmedBefore = beforeCursor.TrimEnd();

            // Extract any partial identifier the user is typing (for filtering)
            if (trimmedBefore.Length > 0 && !trimmedBefore.EndsWith(".") && !trimmedBefore.EndsWith(" "))
            {
                context.FilterPrefix = ExtractLastIdentifier(trimmedBefore);
            }

            // Check for context-specific keywords (case-insensitive)
            var beforeLower = trimmedBefore.ToLowerInvariant();

            // Check for "Import " context
            if (IsAfterKeyword(beforeLower, "import"))
            {
                context.ContextType = CompletionContextType.Import;
                context.FilterPrefix = ExtractAfterKeyword(trimmedBefore, "Import");
                return context;
            }

            // Check for "New " context
            if (IsAfterKeyword(beforeLower, "new"))
            {
                context.ContextType = CompletionContextType.New;
                context.FilterPrefix = ExtractAfterKeyword(trimmedBefore, "New");
                return context;
            }

            // Check for "As " context (type annotation)
            if (IsAfterKeyword(beforeLower, "as"))
            {
                context.ContextType = CompletionContextType.AsType;
                context.FilterPrefix = ExtractAfterKeyword(trimmedBefore, "As");
                return context;
            }

            // Check for "Implements " context
            if (IsAfterKeyword(beforeLower, "implements"))
            {
                context.ContextType = CompletionContextType.Implements;
                context.FilterPrefix = ExtractAfterKeyword(trimmedBefore, "Implements");
                return context;
            }

            // Check for "Inherits " context
            if (IsAfterKeyword(beforeLower, "inherits"))
            {
                context.ContextType = CompletionContextType.Inherits;
                context.FilterPrefix = ExtractAfterKeyword(trimmedBefore, "Inherits");
                return context;
            }

            // Check for "Of " context (generic type parameter)
            if (IsAfterKeyword(beforeLower, "of"))
            {
                context.ContextType = CompletionContextType.AsType;
                context.FilterPrefix = ExtractAfterKeyword(trimmedBefore, "Of");
                return context;
            }

            // Look for member access pattern: identifier followed by dot
            var dotIndex = trimmedBefore.LastIndexOf('.');
            if (dotIndex >= 0)
            {
                var afterDot = trimmedBefore.Substring(dotIndex + 1).Trim();
                var beforeDot = trimmedBefore.Substring(0, dotIndex).TrimEnd();

                var objectName = ExtractLastIdentifierOrCall(beforeDot);

                if (!string.IsNullOrEmpty(objectName))
                {
                    context.ContextType = CompletionContextType.MemberAccess;
                    context.ObjectName = objectName;
                    context.FilterPrefix = string.IsNullOrEmpty(afterDot) ? null : afterDot;
                    return context;
                }
            }

            return context;
        }

        /// <summary>
        /// Check if cursor is after a specific keyword
        /// </summary>
        private bool IsAfterKeyword(string lineLower, string keyword)
        {
            // Check for "keyword " at end or "keyword partial" where partial is what user is typing
            var keywordWithSpace = keyword + " ";
            var lastKeywordIndex = lineLower.LastIndexOf(keywordWithSpace);
            if (lastKeywordIndex >= 0)
            {
                // Make sure keyword is at a word boundary (start of line or after space/operator)
                if (lastKeywordIndex == 0 || !char.IsLetterOrDigit(lineLower[lastKeywordIndex - 1]))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Extract the text after a keyword (what user is typing)
        /// </summary>
        private string ExtractAfterKeyword(string line, string keyword)
        {
            var keywordWithSpace = keyword + " ";
            var lastIndex = line.LastIndexOf(keywordWithSpace, StringComparison.OrdinalIgnoreCase);
            if (lastIndex >= 0)
            {
                var afterKeyword = line.Substring(lastIndex + keywordWithSpace.Length).Trim();
                return string.IsNullOrEmpty(afterKeyword) ? null : afterKeyword;
            }
            return null;
        }

        /// <summary>
        /// Extract the last identifier from a string (simple identifier only)
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
        /// Extract the last identifier or method call from text
        /// Handles: "obj", "obj.Method()", "obj.Method().Property", "New List(Of String)"
        /// </summary>
        private string ExtractLastIdentifierOrCall(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            text = text.TrimEnd();

            // Handle method calls ending with ) - find the matching (
            if (text.EndsWith(")"))
            {
                int parenDepth = 1;
                int i = text.Length - 2;
                while (i >= 0 && parenDepth > 0)
                {
                    if (text[i] == ')') parenDepth++;
                    else if (text[i] == '(') parenDepth--;
                    i--;
                }

                // Now extract the identifier before the (
                if (i >= 0)
                {
                    var beforeParen = text.Substring(0, i + 1).TrimEnd();
                    return ExtractLastIdentifier(beforeParen);
                }
            }

            // Handle generic type instantiation: "New List(Of String)"
            if (text.EndsWith(")") || text.Contains("(Of "))
            {
                // Try to find the type name
                var newIndex = text.LastIndexOf("New ", StringComparison.OrdinalIgnoreCase);
                if (newIndex >= 0)
                {
                    var afterNew = text.Substring(newIndex + 4).Trim();
                    var parenIndex = afterNew.IndexOf('(');
                    if (parenIndex > 0)
                    {
                        return afterNew.Substring(0, parenIndex).Trim();
                    }
                    return ExtractLastIdentifier(afterNew);
                }
            }

            // Simple identifier
            return ExtractLastIdentifier(text);
        }

        /// <summary>
        /// Find a variable's type by searching the AST
        /// </summary>
        private string FindVariableTypeInAST(BasicLang.Compiler.AST.ProgramNode ast, string variableName, int beforeLine)
        {
            if (ast?.Declarations == null)
                return null;

            // Convert 0-based LSP line to 1-based AST line
            int astLine = beforeLine + 1;

            foreach (var decl in ast.Declarations)
            {
                // Check module-level variables
                if (decl is BasicLang.Compiler.AST.ModuleNode module)
                {
                    var type = FindVariableInModule(module, variableName, astLine);
                    if (type != null) return type;
                }
                // Check class members
                else if (decl is BasicLang.Compiler.AST.ClassNode classNode)
                {
                    var type = FindVariableInClass(classNode, variableName, astLine);
                    if (type != null) return type;
                }
                // Check function bodies
                else if (decl is BasicLang.Compiler.AST.FunctionNode func)
                {
                    var type = FindVariableInFunction(func, variableName, astLine);
                    if (type != null) return type;
                }
                else if (decl is BasicLang.Compiler.AST.SubroutineNode sub)
                {
                    var type = FindVariableInSub(sub, variableName, astLine);
                    if (type != null) return type;
                }
                // Check top-level variable declarations
                else if (decl is BasicLang.Compiler.AST.VariableDeclarationNode varDecl)
                {
                    if (varDecl.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase) && varDecl.Line <= astLine)
                    {
                        return GetTypeNameFromTypeRef(varDecl.Type);
                    }
                }
            }

            return null;
        }

        private string FindVariableInClass(BasicLang.Compiler.AST.ClassNode classNode, string variableName, int astLine)
        {
            if (classNode?.Members == null) return null;

            foreach (var member in classNode.Members)
            {
                if (member is BasicLang.Compiler.AST.VariableDeclarationNode varDecl)
                {
                    if (varDecl.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase) && varDecl.Line <= astLine)
                    {
                        return GetTypeNameFromTypeRef(varDecl.Type);
                    }
                }
                else if (member is BasicLang.Compiler.AST.FunctionNode func)
                {
                    var type = FindVariableInFunction(func, variableName, astLine);
                    if (type != null) return type;
                }
                else if (member is BasicLang.Compiler.AST.SubroutineNode sub)
                {
                    var type = FindVariableInSub(sub, variableName, astLine);
                    if (type != null) return type;
                }
            }
            return null;
        }

        private string FindVariableInModule(BasicLang.Compiler.AST.ModuleNode module, string variableName, int astLine)
        {
            if (module?.Members == null) return null;

            foreach (var member in module.Members)
            {
                if (member is BasicLang.Compiler.AST.VariableDeclarationNode varDecl)
                {
                    if (varDecl.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase) && varDecl.Line <= astLine)
                    {
                        return GetTypeNameFromTypeRef(varDecl.Type);
                    }
                }
                else if (member is BasicLang.Compiler.AST.FunctionNode func)
                {
                    var type = FindVariableInFunction(func, variableName, astLine);
                    if (type != null) return type;
                }
                else if (member is BasicLang.Compiler.AST.SubroutineNode sub)
                {
                    var type = FindVariableInSub(sub, variableName, astLine);
                    if (type != null) return type;
                }
            }
            return null;
        }

        private string FindVariableInFunction(BasicLang.Compiler.AST.FunctionNode func, string variableName, int astLine)
        {
            // Only search if cursor is inside this function
            if (astLine < func.Line) return null;

            // Check parameters
            if (func.Parameters != null)
            {
                foreach (var param in func.Parameters)
                {
                    if (param.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return GetTypeNameFromTypeRef(param.Type);
                    }
                }
            }

            // Check body
            if (func.Body != null)
            {
                return FindVariableInBlock(func.Body, variableName, astLine);
            }

            return null;
        }

        private string FindVariableInSub(BasicLang.Compiler.AST.SubroutineNode sub, string variableName, int astLine)
        {
            // Only search if cursor is inside this sub
            if (astLine < sub.Line)
                return null;

            // Check parameters
            if (sub.Parameters != null)
            {
                foreach (var param in sub.Parameters)
                {
                    if (param.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return GetTypeNameFromTypeRef(param.Type);
                    }
                }
            }

            // Check body
            if (sub.Body != null)
            {
                return FindVariableInBlock(sub.Body, variableName, astLine);
            }

            return null;
        }

        private string FindVariableInBlock(BasicLang.Compiler.AST.BlockNode block, string variableName, int astLine)
        {
            if (block?.Statements == null)
                return null;

            foreach (var stmt in block.Statements)
            {
                // Only consider declarations before the cursor
                if (stmt.Line > astLine)
                    continue;

                if (stmt is BasicLang.Compiler.AST.VariableDeclarationNode varDecl)
                {
                    if (varDecl.Name.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return GetTypeNameFromTypeRef(varDecl.Type);
                    }
                }
                // Recurse into nested blocks
                else if (stmt is BasicLang.Compiler.AST.ForLoopNode forNode && forNode.Body != null)
                {
                    // Check loop variable
                    if (forNode.Variable != null && forNode.Variable.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return "Integer"; // For loops typically use Integer
                    }
                    var type = FindVariableInBlock(forNode.Body, variableName, astLine);
                    if (type != null) return type;
                }
                else if (stmt is BasicLang.Compiler.AST.ForEachLoopNode forEachNode && forEachNode.Body != null)
                {
                    // Check loop variable
                    if (forEachNode.Variable != null && forEachNode.Variable.Equals(variableName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to infer element type from collection
                        return "Variant";
                    }
                    var type = FindVariableInBlock(forEachNode.Body, variableName, astLine);
                    if (type != null) return type;
                }
                else if (stmt is BasicLang.Compiler.AST.IfStatementNode ifNode)
                {
                    if (ifNode.ThenBlock != null)
                    {
                        var type = FindVariableInBlock(ifNode.ThenBlock, variableName, astLine);
                        if (type != null) return type;
                    }
                    if (ifNode.ElseBlock != null)
                    {
                        var type = FindVariableInBlock(ifNode.ElseBlock, variableName, astLine);
                        if (type != null) return type;
                    }
                }
                else if (stmt is BasicLang.Compiler.AST.WhileLoopNode whileNode && whileNode.Body != null)
                {
                    var type = FindVariableInBlock(whileNode.Body, variableName, astLine);
                    if (type != null) return type;
                }
            }

            return null;
        }

        private string GetTypeNameFromTypeRef(BasicLang.Compiler.AST.TypeReference typeRef)
        {
            if (typeRef == null) return "Variant";

            var name = typeRef.Name ?? "Variant";

            // Handle generic types
            if (typeRef.GenericArguments != null && typeRef.GenericArguments.Count > 0)
            {
                var args = string.Join(", ", typeRef.GenericArguments.Select(a => GetTypeNameFromTypeRef(a)));
                return $"{name}(Of {args})";
            }

            return name;
        }

        /// <summary>
        /// Generate possible .NET type names for a BasicLang type
        /// </summary>
        private IEnumerable<string> GetPossibleTypeNames(string baseTypeName, string fullTypeName)
        {
            // Count generic arguments from the full type name
            int genericCount = 0;
            if (fullTypeName != null)
            {
                // Count "(Of " occurrences and commas to determine generic arity
                var ofIndex = fullTypeName.IndexOf("(Of ", StringComparison.OrdinalIgnoreCase);
                if (ofIndex >= 0)
                {
                    var genericPart = fullTypeName.Substring(ofIndex);
                    // Count commas + 1 = number of type arguments
                    genericCount = genericPart.Count(c => c == ',') + 1;
                }
            }

            // Map common BasicLang type names to .NET type names
            var typeMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Integer"] = "Int32",
                ["Long"] = "Int64",
                ["Short"] = "Int16",
                ["Byte"] = "Byte",
                ["Single"] = "Single",
                ["Double"] = "Double",
                ["Decimal"] = "Decimal",
                ["Boolean"] = "Boolean",
                ["String"] = "String",
                ["Object"] = "Object",
                ["Date"] = "DateTime",
            };

            // Try the original name first
            yield return baseTypeName;

            // Try with generic arity suffix (e.g., Dictionary`2)
            if (genericCount > 0)
            {
                yield return $"{baseTypeName}`{genericCount}";
            }

            // Try mapped .NET name
            if (typeMapping.TryGetValue(baseTypeName, out var mappedName))
            {
                yield return mappedName;
            }

            // Try common namespaces
            var commonNamespaces = new[]
            {
                "System",
                "System.Collections.Generic",
                "System.Collections",
                "System.IO",
                "System.Text"
            };

            foreach (var ns in commonNamespaces)
            {
                yield return $"{ns}.{baseTypeName}";
                if (genericCount > 0)
                {
                    yield return $"{ns}.{baseTypeName}`{genericCount}";
                }
            }
        }

        // Common namespaces to try loading when a type isn't found
        private static readonly string[] CommonNamespaces = new[]
        {
            "System",
            "System.Collections.Generic",
            "System.IO",
            "System.Text",
            "System.Windows.Forms",
            "System.Drawing",
            "System.Linq",
            "System.Threading.Tasks"
        };

        // Hardcoded common .NET types for fallback when TypeRegistry can't load assemblies
        private static readonly Dictionary<string, List<(string Name, string Detail, CompletionItemKind Kind)>> WellKnownTypes =
            new Dictionary<string, List<(string, string, CompletionItemKind)>>(StringComparer.OrdinalIgnoreCase)
            {
                ["MessageBox"] = new List<(string, string, CompletionItemKind)>
                {
                    ("Show", "Shows a message box with text", CompletionItemKind.Method),
                    ("OK", "OK button", CompletionItemKind.EnumMember),
                    ("OKCancel", "OK and Cancel buttons", CompletionItemKind.EnumMember),
                    ("YesNo", "Yes and No buttons", CompletionItemKind.EnumMember),
                    ("YesNoCancel", "Yes, No, and Cancel buttons", CompletionItemKind.EnumMember),
                    ("RetryCancel", "Retry and Cancel buttons", CompletionItemKind.EnumMember),
                    ("AbortRetryIgnore", "Abort, Retry, and Ignore buttons", CompletionItemKind.EnumMember),
                },
                ["MessageBoxButtons"] = new List<(string, string, CompletionItemKind)>
                {
                    ("OK", "OK button only", CompletionItemKind.EnumMember),
                    ("OKCancel", "OK and Cancel buttons", CompletionItemKind.EnumMember),
                    ("YesNo", "Yes and No buttons", CompletionItemKind.EnumMember),
                    ("YesNoCancel", "Yes, No, and Cancel buttons", CompletionItemKind.EnumMember),
                    ("RetryCancel", "Retry and Cancel buttons", CompletionItemKind.EnumMember),
                    ("AbortRetryIgnore", "Abort, Retry, and Ignore buttons", CompletionItemKind.EnumMember),
                },
                ["MessageBoxIcon"] = new List<(string, string, CompletionItemKind)>
                {
                    ("None", "No icon", CompletionItemKind.EnumMember),
                    ("Information", "Information icon", CompletionItemKind.EnumMember),
                    ("Warning", "Warning icon", CompletionItemKind.EnumMember),
                    ("Error", "Error icon", CompletionItemKind.EnumMember),
                    ("Question", "Question icon", CompletionItemKind.EnumMember),
                    ("Asterisk", "Asterisk icon", CompletionItemKind.EnumMember),
                    ("Exclamation", "Exclamation icon", CompletionItemKind.EnumMember),
                    ("Hand", "Hand icon", CompletionItemKind.EnumMember),
                    ("Stop", "Stop icon", CompletionItemKind.EnumMember),
                },
                ["DialogResult"] = new List<(string, string, CompletionItemKind)>
                {
                    ("OK", "OK was clicked", CompletionItemKind.EnumMember),
                    ("Cancel", "Cancel was clicked", CompletionItemKind.EnumMember),
                    ("Yes", "Yes was clicked", CompletionItemKind.EnumMember),
                    ("No", "No was clicked", CompletionItemKind.EnumMember),
                    ("Abort", "Abort was clicked", CompletionItemKind.EnumMember),
                    ("Retry", "Retry was clicked", CompletionItemKind.EnumMember),
                    ("Ignore", "Ignore was clicked", CompletionItemKind.EnumMember),
                    ("None", "Nothing returned yet", CompletionItemKind.EnumMember),
                },
                ["Console"] = new List<(string, string, CompletionItemKind)>
                {
                    ("WriteLine", "Writes a line to the console", CompletionItemKind.Method),
                    ("Write", "Writes to the console", CompletionItemKind.Method),
                    ("ReadLine", "Reads a line from the console", CompletionItemKind.Method),
                    ("ReadKey", "Reads a key from the console", CompletionItemKind.Method),
                    ("Clear", "Clears the console", CompletionItemKind.Method),
                },
                ["Math"] = new List<(string, string, CompletionItemKind)>
                {
                    ("Abs", "Returns the absolute value", CompletionItemKind.Method),
                    ("Max", "Returns the maximum of two values", CompletionItemKind.Method),
                    ("Min", "Returns the minimum of two values", CompletionItemKind.Method),
                    ("Sqrt", "Returns the square root", CompletionItemKind.Method),
                    ("Pow", "Returns a number raised to a power", CompletionItemKind.Method),
                    ("Round", "Rounds a value", CompletionItemKind.Method),
                    ("Floor", "Returns the floor of a value", CompletionItemKind.Method),
                    ("Ceiling", "Returns the ceiling of a value", CompletionItemKind.Method),
                    ("Sin", "Returns the sine", CompletionItemKind.Method),
                    ("Cos", "Returns the cosine", CompletionItemKind.Method),
                    ("Tan", "Returns the tangent", CompletionItemKind.Method),
                    ("PI", "The value of PI", CompletionItemKind.Constant),
                    ("E", "The value of E", CompletionItemKind.Constant),
                },
                ["String"] = new List<(string, string, CompletionItemKind)>
                {
                    ("Length", "Gets the length of the string", CompletionItemKind.Property),
                    ("Substring", "Returns a substring", CompletionItemKind.Method),
                    ("ToUpper", "Converts to uppercase", CompletionItemKind.Method),
                    ("ToLower", "Converts to lowercase", CompletionItemKind.Method),
                    ("Trim", "Removes whitespace", CompletionItemKind.Method),
                    ("Contains", "Checks if string contains a value", CompletionItemKind.Method),
                    ("StartsWith", "Checks if string starts with a value", CompletionItemKind.Method),
                    ("EndsWith", "Checks if string ends with a value", CompletionItemKind.Method),
                    ("Replace", "Replaces occurrences", CompletionItemKind.Method),
                    ("Split", "Splits the string", CompletionItemKind.Method),
                    ("IndexOf", "Finds the index of a value", CompletionItemKind.Method),
                    ("IsNullOrEmpty", "Checks if null or empty", CompletionItemKind.Method),
                    ("Join", "Joins strings", CompletionItemKind.Method),
                    ("Format", "Formats a string", CompletionItemKind.Method),
                },
                ["List"] = new List<(string, string, CompletionItemKind)>
                {
                    ("Add", "Adds an item", CompletionItemKind.Method),
                    ("Remove", "Removes an item", CompletionItemKind.Method),
                    ("Clear", "Clears the list", CompletionItemKind.Method),
                    ("Count", "Gets the count", CompletionItemKind.Property),
                    ("Contains", "Checks if list contains an item", CompletionItemKind.Method),
                    ("IndexOf", "Finds the index of an item", CompletionItemKind.Method),
                    ("Insert", "Inserts an item at index", CompletionItemKind.Method),
                    ("RemoveAt", "Removes item at index", CompletionItemKind.Method),
                    ("Sort", "Sorts the list", CompletionItemKind.Method),
                    ("Reverse", "Reverses the list", CompletionItemKind.Method),
                    ("ToArray", "Converts to array", CompletionItemKind.Method),
                    ("First", "Gets first item", CompletionItemKind.Method),
                    ("Last", "Gets last item", CompletionItemKind.Method),
                    ("FirstOrDefault", "Gets first item or default", CompletionItemKind.Method),
                    ("LastOrDefault", "Gets last item or default", CompletionItemKind.Method),
                    ("Where", "Filters items", CompletionItemKind.Method),
                    ("Select", "Projects items", CompletionItemKind.Method),
                    ("OrderBy", "Orders items", CompletionItemKind.Method),
                    ("Any", "Checks if any items match", CompletionItemKind.Method),
                    ("All", "Checks if all items match", CompletionItemKind.Method),
                },
                ["Dictionary"] = new List<(string, string, CompletionItemKind)>
                {
                    ("Add", "Adds a key-value pair", CompletionItemKind.Method),
                    ("Remove", "Removes a key", CompletionItemKind.Method),
                    ("Clear", "Clears the dictionary", CompletionItemKind.Method),
                    ("Count", "Gets the count", CompletionItemKind.Property),
                    ("ContainsKey", "Checks if dictionary contains a key", CompletionItemKind.Method),
                    ("ContainsValue", "Checks if dictionary contains a value", CompletionItemKind.Method),
                    ("TryGetValue", "Tries to get value by key", CompletionItemKind.Method),
                    ("Keys", "Gets all keys", CompletionItemKind.Property),
                    ("Values", "Gets all values", CompletionItemKind.Property),
                },
                ["File"] = new List<(string, string, CompletionItemKind)>
                {
                    ("ReadAllText", "Reads all text from a file", CompletionItemKind.Method),
                    ("WriteAllText", "Writes all text to a file", CompletionItemKind.Method),
                    ("Exists", "Checks if file exists", CompletionItemKind.Method),
                    ("Delete", "Deletes a file", CompletionItemKind.Method),
                    ("Copy", "Copies a file", CompletionItemKind.Method),
                    ("Move", "Moves a file", CompletionItemKind.Method),
                    ("ReadAllLines", "Reads all lines from a file", CompletionItemKind.Method),
                    ("WriteAllLines", "Writes all lines to a file", CompletionItemKind.Method),
                },
            };

        /// <summary>
        /// Get member completions for a type
        /// </summary>
        private IEnumerable<CompletionItem> GetMemberCompletions(DocumentState state, string objectName, int line = -1, int character = -1)
        {
            if (string.IsNullOrEmpty(objectName))
                yield break;

            // Try to get type from SemanticAnalyzer first
            NetTypeInfo netType = null;
            if (state?.SemanticAnalyzer != null)
            {
                netType = state.SemanticAnalyzer.GetNetType(objectName);
            }

            // If not found via SemanticAnalyzer, try TypeRegistry directly
            // This handles cases where parsing failed (incomplete code like "MessageBox.")
            if (netType == null && state?.TypeRegistry != null)
            {
                // Try TypeRegistry directly - try both original case and PascalCase
                // Note: Namespaces are pre-loaded at initialization, not here (for performance)
                netType = state.TypeRegistry.GetType(objectName);
                if (netType == null)
                {
                    // Try PascalCase (first letter uppercase)
                    var pascalCase = char.ToUpper(objectName[0]) + objectName.Substring(1);
                    netType = state.TypeRegistry.GetType(pascalCase);
                }
            }

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
            else
            {
                // Fallback to well-known types if TypeRegistry lookup failed
                if (WellKnownTypes.TryGetValue(objectName, out var wellKnownMembers))
                {
                    foreach (var member in wellKnownMembers)
                    {
                        var insertText = member.Name;
                        if (member.Kind == CompletionItemKind.Method)
                        {
                            insertText = $"{member.Name}($0)";
                        }

                        yield return new CompletionItem
                        {
                            Label = member.Name,
                            Kind = member.Kind,
                            Detail = member.Detail,
                            InsertText = insertText,
                            InsertTextFormat = member.Kind == CompletionItemKind.Method ? InsertTextFormat.Snippet : InsertTextFormat.PlainText
                        };
                    }
                    yield break;
                }
            }

            // Try to look up variable type from AST if semantic analyzer is not available or failed
            string variableTypeName = null;

            // First, try AST-based lookup (works even when semantic analysis fails)
            if (state?.AST != null)
            {
                variableTypeName = FindVariableTypeInAST(state.AST, objectName, line);
            }

            // If we found a type from AST, try to get completions for it
            if (!string.IsNullOrEmpty(variableTypeName))
            {
                // Extract base type name (handle generics like "List(Of String)" -> "List")
                var baseTypeName = variableTypeName;
                var genericStart = variableTypeName.IndexOf('(');
                if (genericStart > 0)
                {
                    baseTypeName = variableTypeName.Substring(0, genericStart).Trim();
                }

                // Try TypeRegistry first - this gives us REAL members via reflection
                if (state?.TypeRegistry != null)
                {
                    // Try different name formats for generic types
                    // BasicLang: Dictionary -> .NET: Dictionary`2
                    var typeNamesToTry = GetPossibleTypeNames(baseTypeName, variableTypeName);

                    foreach (var tryName in typeNamesToTry)
                    {
                        netType = state.TypeRegistry.GetType(tryName);
                        if (netType != null)
                        {
                            foreach (var member in netType.Members)
                            {
                                if (member.IsStatic) continue; // Instance access, skip static members
                                yield return CreateMemberCompletionItem(member);
                            }
                            yield break;
                        }
                    }
                }

                // Fallback to well-known types (hardcoded)
                if (WellKnownTypes.TryGetValue(baseTypeName, out var wellKnownMembers))
                {
                    foreach (var member in wellKnownMembers)
                    {
                        var insertText = member.Name;
                        if (member.Kind == CompletionItemKind.Method)
                        {
                            insertText = $"{member.Name}($0)";
                        }

                        yield return new CompletionItem
                        {
                            Label = member.Name,
                            Kind = member.Kind,
                            Detail = member.Detail,
                            InsertText = insertText,
                            InsertTextFormat = member.Kind == CompletionItemKind.Method ? InsertTextFormat.Snippet : InsertTextFormat.PlainText
                        };
                    }
                    yield break;
                }
            }

            // If we don't have a SemanticAnalyzer, we're done
            if (state?.SemanticAnalyzer == null)
                yield break;

            // Find the scope at the current position and resolve the variable from there
            var currentScope = GetScopeAtPosition(state, line, character);

            // Check if it's a variable with a known type - search from current scope up the chain
            var symbol = currentScope?.Resolve(objectName) ?? state.SemanticAnalyzer.GlobalScope?.Resolve(objectName);

            if (symbol != null && symbol.Type != null)
            {
                // Try to get .NET type members
                var typeName = symbol.Type.Name;
                netType = state.SemanticAnalyzer.GetNetType(typeName);

                // Also try without generic parameters (e.g., "List" instead of "List<String>")
                if (netType == null && typeName.Contains("<"))
                {
                    var baseTypeName = typeName.Substring(0, typeName.IndexOf('<'));
                    netType = state.SemanticAnalyzer.GetNetType(baseTypeName);
                }

                // Try loading from TypeRegistry if still not found
                if (netType == null && state.TypeRegistry != null)
                {
                    netType = state.TypeRegistry.GetType(typeName);
                    if (netType == null)
                    {
                        // Try common type mappings
                        var mappedType = MapBasicTypeToNetType(typeName);
                        if (mappedType != null)
                        {
                            netType = state.TypeRegistry.GetType(mappedType);
                        }
                    }
                }

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
                else
                {
                    // Fallback to well-known types based on the variable's type
                    if (WellKnownTypes.TryGetValue(typeName, out var wellKnownMembers))
                    {
                        foreach (var member in wellKnownMembers)
                        {
                            var insertText = member.Name;
                            if (member.Kind == CompletionItemKind.Method)
                            {
                                insertText = $"{member.Name}($0)";
                            }

                            yield return new CompletionItem
                            {
                                Label = member.Name,
                                Kind = member.Kind,
                                Detail = member.Detail,
                                InsertText = insertText,
                                InsertTextFormat = member.Kind == CompletionItemKind.Method ? InsertTextFormat.Snippet : InsertTextFormat.PlainText
                            };
                        }
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

        /// <summary>
        /// Map BasicLang type names to .NET type names
        /// </summary>
        private string MapBasicTypeToNetType(string basicType)
        {
            return basicType?.ToLower() switch
            {
                "integer" => "Int32",
                "long" => "Int64",
                "single" => "Single",
                "double" => "Double",
                "string" => "String",
                "boolean" => "Boolean",
                "byte" => "Byte",
                "char" => "Char",
                "object" => "Object",
                "list" => "List",
                "dictionary" => "Dictionary",
                _ => null
            };
        }

        /// <summary>
        /// Get the scope at a specific line/character position
        /// </summary>
        private Scope GetScopeAtPosition(DocumentState state, int line, int character)
        {
            if (state?.SemanticAnalyzer?.GlobalScope == null || state.AST == null || line < 0)
                return state?.SemanticAnalyzer?.GlobalScope;

            // LSP uses 0-based line numbers, but AST uses 1-based line numbers
            int astLine = line + 1;

            // Search through AST to find the containing function/sub/class
            Scope bestScope = state.SemanticAnalyzer.GlobalScope;

            foreach (var decl in state.AST.Declarations)
            {
                var scope = FindScopeContainingPosition(state.SemanticAnalyzer.GlobalScope, decl, astLine);
                if (scope != null && scope != state.SemanticAnalyzer.GlobalScope)
                {
                    bestScope = scope;
                    break;
                }
            }

            return bestScope;
        }

        /// <summary>
        /// Find the scope that contains the given line position
        /// </summary>
        private Scope FindScopeContainingPosition(Scope parentScope, ASTNode node, int line)
        {
            // Check if this node spans the target line
            if (node is BasicLang.Compiler.AST.FunctionNode func)
            {
                // Check if line is within function bounds
                if (line >= func.Line && line <= GetEndLine(func))
                {
                    // Find the function's scope in the scope tree
                    foreach (var childScope in parentScope.Children)
                    {
                        if (childScope.Name == func.Name &&
                            (childScope.Kind == ScopeKind.Function || childScope.Kind == ScopeKind.Subroutine))
                        {
                            return childScope;
                        }
                    }
                }
            }
            else if (node is BasicLang.Compiler.AST.SubroutineNode sub)
            {
                if (line >= sub.Line && line <= GetEndLine(sub))
                {
                    foreach (var childScope in parentScope.Children)
                    {
                        if (childScope.Name == sub.Name &&
                            (childScope.Kind == ScopeKind.Function || childScope.Kind == ScopeKind.Subroutine))
                        {
                            return childScope;
                        }
                    }
                }
            }
            else if (node is BasicLang.Compiler.AST.ClassNode cls)
            {
                if (line >= cls.Line && line <= GetEndLine(cls))
                {
                    foreach (var childScope in parentScope.Children)
                    {
                        if (childScope.Name == cls.Name && childScope.Kind == ScopeKind.Class)
                        {
                            // Check if we're inside a method within the class
                            foreach (var member in cls.Members)
                            {
                                var innerScope = FindScopeContainingPosition(childScope, member, line);
                                if (innerScope != null && innerScope != childScope)
                                {
                                    return innerScope;
                                }
                            }
                            return childScope;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Estimate the end line of an AST node
        /// </summary>
        private int GetEndLine(ASTNode node)
        {
            // For now, estimate based on node type
            // A more accurate approach would require storing end positions in AST nodes
            if (node is BasicLang.Compiler.AST.FunctionNode func)
            {
                // Estimate: function line + number of statements + some buffer
                return func.Line + (func.Body?.Statements?.Count ?? 0) + 10;
            }
            else if (node is BasicLang.Compiler.AST.SubroutineNode sub)
            {
                return sub.Line + (sub.Body?.Statements?.Count ?? 0) + 10;
            }
            else if (node is BasicLang.Compiler.AST.ClassNode cls)
            {
                return cls.Line + (cls.Members?.Count ?? 0) * 10 + 10;
            }

            return node.Line + 100; // Default estimate
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
                // Import statement
                ("Import", "Import module", "Import ${1:ModuleName}"),

                ("Sub", "Subroutine declaration", "Sub ${1:Name}()\n\t$0\nEnd Sub"),
                ("Function", "Function declaration", "Function ${1:Name}() As ${2:Integer}\n\t$0\nEnd Function"),
                ("If", "If statement", "If ${1:condition} Then\n\t$0\nEnd If"),
                ("If...Else", "If-Else statement", "If ${1:condition} Then\n\t$2\nElse\n\t$0\nEnd If"),
                ("For", "For loop", "For ${1:i} = ${2:1} To ${3:10}\n\t$0\nNext"),
                ("While", "While loop", "While ${1:condition}\n\t$0\nWend"),
                ("Do While", "Do While loop", "Do While ${1:condition}\n\t$0\nLoop"),
                ("Do Until", "Do Until loop", "Do Until ${1:condition}\n\t$0\nLoop"),
                ("Select Case", "Select Case statement", "Select Case ${1:expression}\n\tCase ${2:value}\n\t\t$0\n\tCase Else\n\t\t\nEnd Select"),
                ("Select Case Type", "Type pattern matching", "Select Case ${1:value}\n\tCase Is ${2:Type1}\n\t\t$0\n\tCase Is ${3:Type2}\n\t\t\n\tCase Else\n\t\t\nEnd Select"),
                ("Select Case Guard", "Pattern matching with guards", "Select Case ${1:value}\n\tCase ${2:pattern} When ${3:condition}\n\t\t$0\n\tCase Else\n\t\t\nEnd Select"),
                ("Case Is", "Type pattern", "Case Is ${1:TypeName}"),
                ("Case When", "Pattern with guard", "Case ${1:pattern} When ${2:condition}"),
                ("Class", "Class declaration", "Class ${1:Name}\n\t$0\nEnd Class"),
                ("Dim", "Variable declaration", "Dim ${1:name} As ${2:Integer}"),
                ("Dim Tuple", "Tuple deconstruction", "Dim (${1:x}, ${2:y}) = ${3:GetPoint()}"),
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
                // ═══════════════════════════════════════════════════════════════
                // I/O Functions
                // ═══════════════════════════════════════════════════════════════
                ("Print", "Prints to the console without newline", "Print(${1:value})", "Void"),
                ("PrintLine", "Prints a line to the console", "PrintLine(${1:value})", "Void"),
                ("Input", "Prompts user for input with message", "Input(${1:prompt})", "String"),
                ("ReadLine", "Reads a line from the console", "ReadLine()", "String"),
                ("ReadKey", "Reads a key press from console", "ReadKey()", "String"),
                ("Write", "Writes to console without newline", "Write(${1:value})", "Void"),
                ("WriteLine", "Writes a line to console", "WriteLine(${1:value})", "Void"),

                // ═══════════════════════════════════════════════════════════════
                // File I/O Functions
                // ═══════════════════════════════════════════════════════════════
                ("FileRead", "Reads all text from a file", "FileRead(${1:path})", "String"),
                ("FileWrite", "Writes text to a file (overwrites)", "FileWrite(${1:path}, ${2:content})", "Void"),
                ("FileAppend", "Appends text to a file", "FileAppend(${1:path}, ${2:content})", "Void"),
                ("FileExists", "Checks if a file exists", "FileExists(${1:path})", "Boolean"),
                ("FileDelete", "Deletes a file", "FileDelete(${1:path})", "Void"),
                ("FileCopy", "Copies a file to new location", "FileCopy(${1:source}, ${2:destination})", "Void"),
                ("FileMove", "Moves a file to new location", "FileMove(${1:source}, ${2:destination})", "Void"),
                ("FileReadLines", "Reads all lines from a file", "FileReadLines(${1:path})", "String()"),
                ("FileWriteLines", "Writes lines to a file", "FileWriteLines(${1:path}, ${2:lines})", "Void"),
                ("FileReadBytes", "Reads all bytes from a file", "FileReadBytes(${1:path})", "Byte()"),
                ("FileWriteBytes", "Writes bytes to a file", "FileWriteBytes(${1:path}, ${2:bytes})", "Void"),
                ("FileGetSize", "Gets the size of a file in bytes", "FileGetSize(${1:path})", "Long"),
                ("FileGetCreationTime", "Gets file creation time", "FileGetCreationTime(${1:path})", "DateTime"),
                ("FileGetLastWriteTime", "Gets file last write time", "FileGetLastWriteTime(${1:path})", "DateTime"),

                // ═══════════════════════════════════════════════════════════════
                // Directory Functions
                // ═══════════════════════════════════════════════════════════════
                ("DirExists", "Checks if a directory exists", "DirExists(${1:path})", "Boolean"),
                ("DirCreate", "Creates a directory", "DirCreate(${1:path})", "Void"),
                ("DirDelete", "Deletes a directory", "DirDelete(${1:path})", "Void"),
                ("DirGetFiles", "Gets all files in a directory", "DirGetFiles(${1:path})", "String()"),
                ("DirGetDirs", "Gets all subdirectories", "DirGetDirs(${1:path})", "String()"),
                ("DirGetFilesPattern", "Gets files matching pattern", "DirGetFilesPattern(${1:path}, ${2:pattern})", "String()"),
                ("DirGetCurrentDirectory", "Gets current working directory", "DirGetCurrentDirectory()", "String"),
                ("DirSetCurrentDirectory", "Sets current working directory", "DirSetCurrentDirectory(${1:path})", "Void"),

                // ═══════════════════════════════════════════════════════════════
                // Path Functions
                // ═══════════════════════════════════════════════════════════════
                ("PathCombine", "Combines two path strings", "PathCombine(${1:path1}, ${2:path2})", "String"),
                ("PathGetFileName", "Gets filename from path", "PathGetFileName(${1:path})", "String"),
                ("PathGetDirectory", "Gets directory from path", "PathGetDirectory(${1:path})", "String"),
                ("PathGetExtension", "Gets file extension from path", "PathGetExtension(${1:path})", "String"),
                ("PathGetFileNameWithoutExtension", "Gets filename without extension", "PathGetFileNameWithoutExtension(${1:path})", "String"),
                ("PathChangeExtension", "Changes file extension", "PathChangeExtension(${1:path}, ${2:newExt})", "String"),
                ("PathGetFullPath", "Gets absolute path", "PathGetFullPath(${1:path})", "String"),
                ("PathGetTempPath", "Gets system temp directory", "PathGetTempPath()", "String"),
                ("PathGetTempFileName", "Creates a unique temp file", "PathGetTempFileName()", "String"),

                // ═══════════════════════════════════════════════════════════════
                // String Functions
                // ═══════════════════════════════════════════════════════════════
                ("Len", "Returns the length of a string", "Len(${1:str})", "Integer"),
                ("Mid", "Returns a substring", "Mid(${1:str}, ${2:start}, ${3:length})", "String"),
                ("Left", "Returns leftmost characters", "Left(${1:str}, ${2:count})", "String"),
                ("Right", "Returns rightmost characters", "Right(${1:str}, ${2:count})", "String"),
                ("UCase", "Converts string to uppercase", "UCase(${1:str})", "String"),
                ("LCase", "Converts string to lowercase", "LCase(${1:str})", "String"),
                ("Trim", "Removes leading and trailing whitespace", "Trim(${1:str})", "String"),
                ("LTrim", "Removes leading whitespace", "LTrim(${1:str})", "String"),
                ("RTrim", "Removes trailing whitespace", "RTrim(${1:str})", "String"),
                ("InStr", "Finds position of substring", "InStr(${1:str}, ${2:search})", "Integer"),
                ("InStrRev", "Finds position of substring from end", "InStrRev(${1:str}, ${2:search})", "Integer"),
                ("Replace", "Replaces occurrences in string", "Replace(${1:str}, ${2:oldValue}, ${3:newValue})", "String"),
                ("Split", "Splits string into array", "Split(${1:str}, ${2:delimiter})", "String()"),
                ("Join", "Joins array elements into string", "Join(${1:arr}, ${2:delimiter})", "String"),
                ("Chr", "Returns character from ASCII code", "Chr(${1:charCode})", "String"),
                ("Asc", "Returns ASCII code of character", "Asc(${1:char})", "Integer"),
                ("Space", "Returns string of spaces", "Space(${1:count})", "String"),
                ("String", "Returns repeated character string", "String(${1:count}, ${2:char})", "String"),
                ("StrReverse", "Reverses a string", "StrReverse(${1:str})", "String"),
                ("StrComp", "Compares two strings", "StrComp(${1:str1}, ${2:str2})", "Integer"),
                ("Format", "Formats a value as string", "Format(${1:value}, ${2:formatString})", "String"),
                ("StartsWith", "Checks if string starts with value", "StartsWith(${1:str}, ${2:value})", "Boolean"),
                ("EndsWith", "Checks if string ends with value", "EndsWith(${1:str}, ${2:value})", "Boolean"),
                ("Contains", "Checks if string contains value", "Contains(${1:str}, ${2:value})", "Boolean"),
                ("PadLeft", "Pads string on left to length", "PadLeft(${1:str}, ${2:totalWidth})", "String"),
                ("PadRight", "Pads string on right to length", "PadRight(${1:str}, ${2:totalWidth})", "String"),
                ("Substring", "Returns substring from start index", "Substring(${1:str}, ${2:startIndex})", "String"),
                ("ToCharArray", "Converts string to character array", "ToCharArray(${1:str})", "Char()"),
                ("IsNullOrEmpty", "Checks if string is null or empty", "IsNullOrEmpty(${1:str})", "Boolean"),
                ("IsNullOrWhiteSpace", "Checks if string is null or whitespace", "IsNullOrWhiteSpace(${1:str})", "Boolean"),

                // ═══════════════════════════════════════════════════════════════
                // Math Functions
                // ═══════════════════════════════════════════════════════════════
                ("Abs", "Returns absolute value", "Abs(${1:value})", "Double"),
                ("Sqrt", "Returns square root", "Sqrt(${1:value})", "Double"),
                ("Pow", "Returns value raised to power", "Pow(${1:base}, ${2:exponent})", "Double"),
                ("Sin", "Returns sine of angle (radians)", "Sin(${1:radians})", "Double"),
                ("Cos", "Returns cosine of angle (radians)", "Cos(${1:radians})", "Double"),
                ("Tan", "Returns tangent of angle (radians)", "Tan(${1:radians})", "Double"),
                ("Asin", "Returns arcsine (radians)", "Asin(${1:value})", "Double"),
                ("Acos", "Returns arccosine (radians)", "Acos(${1:value})", "Double"),
                ("Atan", "Returns arctangent (radians)", "Atan(${1:value})", "Double"),
                ("Atan2", "Returns arctangent of y/x", "Atan2(${1:y}, ${2:x})", "Double"),
                ("Sinh", "Returns hyperbolic sine", "Sinh(${1:value})", "Double"),
                ("Cosh", "Returns hyperbolic cosine", "Cosh(${1:value})", "Double"),
                ("Tanh", "Returns hyperbolic tangent", "Tanh(${1:value})", "Double"),
                ("Log", "Returns natural logarithm", "Log(${1:value})", "Double"),
                ("Log10", "Returns base-10 logarithm", "Log10(${1:value})", "Double"),
                ("Log2", "Returns base-2 logarithm", "Log2(${1:value})", "Double"),
                ("Exp", "Returns e raised to power", "Exp(${1:value})", "Double"),
                ("Floor", "Rounds down to integer", "Floor(${1:value})", "Double"),
                ("Ceiling", "Rounds up to integer", "Ceiling(${1:value})", "Double"),
                ("Round", "Rounds to nearest integer", "Round(${1:value})", "Double"),
                ("RoundTo", "Rounds to specified decimals", "RoundTo(${1:value}, ${2:decimals})", "Double"),
                ("Truncate", "Truncates decimal portion", "Truncate(${1:value})", "Double"),
                ("Min", "Returns minimum of two values", "Min(${1:a}, ${2:b})", "Double"),
                ("Max", "Returns maximum of two values", "Max(${1:a}, ${2:b})", "Double"),
                ("Clamp", "Clamps value between min and max", "Clamp(${1:value}, ${2:min}, ${3:max})", "Double"),
                ("Sign", "Returns sign of value (-1, 0, 1)", "Sign(${1:value})", "Integer"),
                ("Rnd", "Returns random number 0 to 1", "Rnd()", "Double"),
                ("Randomize", "Seeds random number generator", "Randomize()", "Void"),
                ("RandomInt", "Returns random integer in range", "RandomInt(${1:min}, ${2:max})", "Integer"),
                ("PI", "Returns value of PI", "PI()", "Double"),
                ("E", "Returns value of E", "E()", "Double"),
                ("DegreesToRadians", "Converts degrees to radians", "DegreesToRadians(${1:degrees})", "Double"),
                ("RadiansToDegrees", "Converts radians to degrees", "RadiansToDegrees(${1:radians})", "Double"),

                // ═══════════════════════════════════════════════════════════════
                // Type Conversion Functions
                // ═══════════════════════════════════════════════════════════════
                ("CInt", "Converts to Integer", "CInt(${1:value})", "Integer"),
                ("CLng", "Converts to Long", "CLng(${1:value})", "Long"),
                ("CDbl", "Converts to Double", "CDbl(${1:value})", "Double"),
                ("CSng", "Converts to Single", "CSng(${1:value})", "Single"),
                ("CStr", "Converts to String", "CStr(${1:value})", "String"),
                ("CBool", "Converts to Boolean", "CBool(${1:value})", "Boolean"),
                ("CByte", "Converts to Byte", "CByte(${1:value})", "Byte"),
                ("CChar", "Converts to Char", "CChar(${1:value})", "Char"),
                ("CDate", "Converts to DateTime", "CDate(${1:value})", "DateTime"),
                ("CDec", "Converts to Decimal", "CDec(${1:value})", "Decimal"),
                ("CObj", "Converts to Object", "CObj(${1:value})", "Object"),
                ("CType", "Converts to specified type", "CType(${1:value}, ${2:Type})", "Object"),
                ("DirectCast", "Direct cast to type", "DirectCast(${1:value}, ${2:Type})", "Object"),
                ("TryCast", "Try cast to type (returns Nothing on failure)", "TryCast(${1:value}, ${2:Type})", "Object"),
                ("Val", "Converts string to number", "Val(${1:str})", "Double"),
                ("Hex", "Converts number to hex string", "Hex(${1:value})", "String"),
                ("Oct", "Converts number to octal string", "Oct(${1:value})", "String"),
                ("Bin", "Converts number to binary string", "Bin(${1:value})", "String"),

                // ═══════════════════════════════════════════════════════════════
                // DateTime Functions
                // ═══════════════════════════════════════════════════════════════
                ("Now", "Returns current date and time", "Now()", "DateTime"),
                ("Today", "Returns current date (no time)", "Today()", "DateTime"),
                ("TimeOfDay", "Returns current time", "TimeOfDay()", "DateTime"),
                ("Year", "Gets year from date", "Year(${1:date})", "Integer"),
                ("Month", "Gets month from date (1-12)", "Month(${1:date})", "Integer"),
                ("Day", "Gets day from date (1-31)", "Day(${1:date})", "Integer"),
                ("Hour", "Gets hour from time (0-23)", "Hour(${1:date})", "Integer"),
                ("Minute", "Gets minute from time (0-59)", "Minute(${1:date})", "Integer"),
                ("Second", "Gets second from time (0-59)", "Second(${1:date})", "Integer"),
                ("Millisecond", "Gets millisecond (0-999)", "Millisecond(${1:date})", "Integer"),
                ("DayOfWeek", "Gets day of week (0=Sunday)", "DayOfWeek(${1:date})", "Integer"),
                ("DayOfYear", "Gets day of year (1-366)", "DayOfYear(${1:date})", "Integer"),
                ("WeekOfYear", "Gets week of year", "WeekOfYear(${1:date})", "Integer"),
                ("DaysInMonth", "Gets days in specified month", "DaysInMonth(${1:year}, ${2:month})", "Integer"),
                ("IsLeapYear", "Checks if year is leap year", "IsLeapYear(${1:year})", "Boolean"),
                ("DateAdd", "Adds interval to date", "DateAdd(${1:date}, ${2:interval}, ${3:number})", "DateTime"),
                ("DateDiff", "Gets difference between dates", "DateDiff(${1:date1}, ${2:date2}, ${3:interval})", "Long"),
                ("DatePart", "Gets part of date", "DatePart(${1:date}, ${2:interval})", "Integer"),
                ("DateSerial", "Creates date from parts", "DateSerial(${1:year}, ${2:month}, ${3:day})", "DateTime"),
                ("TimeSerial", "Creates time from parts", "TimeSerial(${1:hour}, ${2:minute}, ${3:second})", "DateTime"),
                ("FormatDate", "Formats date as string", "FormatDate(${1:date}, ${2:format})", "String"),
                ("ParseDate", "Parses string to date", "ParseDate(${1:str})", "DateTime"),
                ("DateValue", "Gets date portion of DateTime", "DateValue(${1:date})", "DateTime"),
                ("TimeValue", "Gets time portion of DateTime", "TimeValue(${1:date})", "DateTime"),
                ("Timer", "Gets seconds since midnight", "Timer()", "Double"),

                // ═══════════════════════════════════════════════════════════════
                // Array Functions
                // ═══════════════════════════════════════════════════════════════
                ("UBound", "Returns upper bound of array", "UBound(${1:arr})", "Integer"),
                ("LBound", "Returns lower bound of array", "LBound(${1:arr})", "Integer"),
                ("Array", "Creates array from values", "Array(${1:values})", "Object()"),
                ("ReDim", "Resizes an array", "ReDim ${1:arr}(${2:size})", "Void"),
                ("Erase", "Clears array contents", "Erase ${1:arr}", "Void"),
                ("ArrayLength", "Gets length of array", "ArrayLength(${1:arr})", "Integer"),
                ("ArrayResize", "Resizes array preserving data", "ArrayResize(${1:arr}, ${2:newSize})", "Void"),
                ("ArrayCopy", "Copies array elements", "ArrayCopy(${1:source}, ${2:dest}, ${3:length})", "Void"),
                ("ArrayIndexOf", "Finds index of element", "ArrayIndexOf(${1:arr}, ${2:value})", "Integer"),
                ("ArrayContains", "Checks if array contains value", "ArrayContains(${1:arr}, ${2:value})", "Boolean"),
                ("ArraySort", "Sorts array in place", "ArraySort(${1:arr})", "Void"),
                ("ArrayReverse", "Reverses array in place", "ArrayReverse(${1:arr})", "Void"),
                ("ArrayClear", "Clears array to default values", "ArrayClear(${1:arr})", "Void"),
                ("ArrayFill", "Fills array with value", "ArrayFill(${1:arr}, ${2:value})", "Void"),

                // ═══════════════════════════════════════════════════════════════
                // Collection Functions
                // ═══════════════════════════════════════════════════════════════
                ("CreateList", "Creates a new List", "CreateList(Of ${1:T})()", "List"),
                ("ListAdd", "Adds item to list", "ListAdd(${1:list}, ${2:item})", "Void"),
                ("ListRemove", "Removes item from list", "ListRemove(${1:list}, ${2:item})", "Boolean"),
                ("ListRemoveAt", "Removes item at index", "ListRemoveAt(${1:list}, ${2:index})", "Void"),
                ("ListInsert", "Inserts item at index", "ListInsert(${1:list}, ${2:index}, ${3:item})", "Void"),
                ("ListGet", "Gets item at index", "ListGet(${1:list}, ${2:index})", "Object"),
                ("ListSet", "Sets item at index", "ListSet(${1:list}, ${2:index}, ${3:value})", "Void"),
                ("ListCount", "Gets list count", "ListCount(${1:list})", "Integer"),
                ("ListClear", "Clears all items", "ListClear(${1:list})", "Void"),
                ("ListContains", "Checks if list contains item", "ListContains(${1:list}, ${2:item})", "Boolean"),
                ("ListIndexOf", "Gets index of item", "ListIndexOf(${1:list}, ${2:item})", "Integer"),
                ("ListSort", "Sorts list", "ListSort(${1:list})", "Void"),
                ("ListReverse", "Reverses list", "ListReverse(${1:list})", "Void"),
                ("ListToArray", "Converts list to array", "ListToArray(${1:list})", "Object()"),
                ("CreateDictionary", "Creates a new Dictionary", "CreateDictionary(Of ${1:TKey}, ${2:TValue})()", "Dictionary"),
                ("DictAdd", "Adds key-value pair", "DictAdd(${1:dict}, ${2:key}, ${3:value})", "Void"),
                ("DictRemove", "Removes key from dictionary", "DictRemove(${1:dict}, ${2:key})", "Boolean"),
                ("DictGet", "Gets value by key", "DictGet(${1:dict}, ${2:key})", "Object"),
                ("DictSet", "Sets value for key", "DictSet(${1:dict}, ${2:key}, ${3:value})", "Void"),
                ("DictCount", "Gets dictionary count", "DictCount(${1:dict})", "Integer"),
                ("DictClear", "Clears all entries", "DictClear(${1:dict})", "Void"),
                ("DictContainsKey", "Checks if key exists", "DictContainsKey(${1:dict}, ${2:key})", "Boolean"),
                ("DictContainsValue", "Checks if value exists", "DictContainsValue(${1:dict}, ${2:value})", "Boolean"),
                ("DictKeys", "Gets all keys", "DictKeys(${1:dict})", "Object()"),
                ("DictValues", "Gets all values", "DictValues(${1:dict})", "Object()"),
                ("DictTryGetValue", "Tries to get value", "DictTryGetValue(${1:dict}, ${2:key}, ${3:outValue})", "Boolean"),
                ("CreateHashSet", "Creates a new HashSet", "CreateHashSet(Of ${1:T})()", "HashSet"),
                ("SetAdd", "Adds item to set", "SetAdd(${1:set}, ${2:item})", "Boolean"),
                ("SetRemove", "Removes item from set", "SetRemove(${1:set}, ${2:item})", "Boolean"),
                ("SetContains", "Checks if set contains item", "SetContains(${1:set}, ${2:item})", "Boolean"),
                ("SetCount", "Gets set count", "SetCount(${1:set})", "Integer"),
                ("SetClear", "Clears all items", "SetClear(${1:set})", "Void"),
                ("CreateQueue", "Creates a new Queue", "CreateQueue(Of ${1:T})()", "Queue"),
                ("QueueEnqueue", "Adds item to queue", "QueueEnqueue(${1:queue}, ${2:item})", "Void"),
                ("QueueDequeue", "Removes and returns front item", "QueueDequeue(${1:queue})", "Object"),
                ("QueuePeek", "Returns front item without removing", "QueuePeek(${1:queue})", "Object"),
                ("QueueCount", "Gets queue count", "QueueCount(${1:queue})", "Integer"),
                ("CreateStack", "Creates a new Stack", "CreateStack(Of ${1:T})()", "Stack"),
                ("StackPush", "Pushes item onto stack", "StackPush(${1:stack}, ${2:item})", "Void"),
                ("StackPop", "Pops item from stack", "StackPop(${1:stack})", "Object"),
                ("StackPeek", "Returns top item without removing", "StackPeek(${1:stack})", "Object"),
                ("StackCount", "Gets stack count", "StackCount(${1:stack})", "Integer"),

                // ═══════════════════════════════════════════════════════════════
                // LINQ-Style Functions
                // ═══════════════════════════════════════════════════════════════
                ("Where", "Filters elements by condition", "Where(${1:collection}, Function(x) ${2:condition})", "IEnumerable"),
                ("Select", "Projects elements to new form", "Select(${1:collection}, Function(x) ${2:projection})", "IEnumerable"),
                ("SelectMany", "Projects and flattens collections", "SelectMany(${1:collection}, Function(x) ${2:selector})", "IEnumerable"),
                ("OrderBy", "Sorts elements ascending", "OrderBy(${1:collection}, Function(x) ${2:keySelector})", "IEnumerable"),
                ("OrderByDescending", "Sorts elements descending", "OrderByDescending(${1:collection}, Function(x) ${2:keySelector})", "IEnumerable"),
                ("ThenBy", "Secondary sort ascending", "ThenBy(${1:collection}, Function(x) ${2:keySelector})", "IEnumerable"),
                ("ThenByDescending", "Secondary sort descending", "ThenByDescending(${1:collection}, Function(x) ${2:keySelector})", "IEnumerable"),
                ("GroupBy", "Groups elements by key", "GroupBy(${1:collection}, Function(x) ${2:keySelector})", "IEnumerable"),
                ("Join", "Joins two collections", "Join(${1:outer}, ${2:inner}, Function(o) ${3:outerKey}, Function(i) ${4:innerKey}, Function(o, i) ${5:result})", "IEnumerable"),
                ("GroupJoin", "Groups and joins collections", "GroupJoin(${1:outer}, ${2:inner}, Function(o) ${3:outerKey}, Function(i) ${4:innerKey}, Function(o, g) ${5:result})", "IEnumerable"),
                ("First", "Returns first element", "First(${1:collection})", "Object"),
                ("FirstOrDefault", "Returns first or default", "FirstOrDefault(${1:collection})", "Object"),
                ("Last", "Returns last element", "Last(${1:collection})", "Object"),
                ("LastOrDefault", "Returns last or default", "LastOrDefault(${1:collection})", "Object"),
                ("Single", "Returns single element", "Single(${1:collection})", "Object"),
                ("SingleOrDefault", "Returns single or default", "SingleOrDefault(${1:collection})", "Object"),
                ("ElementAt", "Returns element at index", "ElementAt(${1:collection}, ${2:index})", "Object"),
                ("ElementAtOrDefault", "Returns element at index or default", "ElementAtOrDefault(${1:collection}, ${2:index})", "Object"),
                ("Take", "Takes first n elements", "Take(${1:collection}, ${2:count})", "IEnumerable"),
                ("TakeWhile", "Takes while condition true", "TakeWhile(${1:collection}, Function(x) ${2:condition})", "IEnumerable"),
                ("Skip", "Skips first n elements", "Skip(${1:collection}, ${2:count})", "IEnumerable"),
                ("SkipWhile", "Skips while condition true", "SkipWhile(${1:collection}, Function(x) ${2:condition})", "IEnumerable"),
                ("Distinct", "Returns distinct elements", "Distinct(${1:collection})", "IEnumerable"),
                ("Union", "Returns union of collections", "Union(${1:first}, ${2:second})", "IEnumerable"),
                ("Intersect", "Returns intersection", "Intersect(${1:first}, ${2:second})", "IEnumerable"),
                ("Except", "Returns elements not in second", "Except(${1:first}, ${2:second})", "IEnumerable"),
                ("Concat", "Concatenates collections", "Concat(${1:first}, ${2:second})", "IEnumerable"),
                ("Reverse", "Reverses collection", "Reverse(${1:collection})", "IEnumerable"),
                ("Zip", "Zips two collections", "Zip(${1:first}, ${2:second}, Function(a, b) ${3:result})", "IEnumerable"),
                ("Any", "Checks if any element matches", "Any(${1:collection}, Function(x) ${2:condition})", "Boolean"),
                ("All", "Checks if all elements match", "All(${1:collection}, Function(x) ${2:condition})", "Boolean"),
                ("Count", "Counts elements", "Count(${1:collection})", "Integer"),
                ("LongCount", "Counts elements (long)", "LongCount(${1:collection})", "Long"),
                ("Sum", "Sums elements", "Sum(${1:collection}, Function(x) ${2:selector})", "Double"),
                ("Average", "Averages elements", "Average(${1:collection}, Function(x) ${2:selector})", "Double"),
                ("Min", "Gets minimum value", "Min(${1:collection}, Function(x) ${2:selector})", "Object"),
                ("Max", "Gets maximum value", "Max(${1:collection}, Function(x) ${2:selector})", "Object"),
                ("Aggregate", "Accumulates elements", "Aggregate(${1:collection}, ${2:seed}, Function(acc, x) ${3:accumulator})", "Object"),
                ("ToList", "Converts to List", "ToList(${1:collection})", "List"),
                ("ToArray", "Converts to Array", "ToArray(${1:collection})", "Object()"),
                ("ToDictionary", "Converts to Dictionary", "ToDictionary(${1:collection}, Function(x) ${2:keySelector})", "Dictionary"),
                ("ToHashSet", "Converts to HashSet", "ToHashSet(${1:collection})", "HashSet"),
                ("AsEnumerable", "Casts to IEnumerable", "AsEnumerable(${1:collection})", "IEnumerable"),
                ("Cast", "Casts elements to type", "Cast(Of ${1:T})(${2:collection})", "IEnumerable"),
                ("OfType", "Filters by type", "OfType(Of ${1:T})(${2:collection})", "IEnumerable"),
                ("Range", "Generates range of integers", "Range(${1:start}, ${2:count})", "IEnumerable"),
                ("Repeat", "Generates repeated value", "Repeat(${1:value}, ${2:count})", "IEnumerable"),
                ("Empty", "Returns empty collection", "Empty(Of ${1:T})()", "IEnumerable"),
                ("DefaultIfEmpty", "Returns default if empty", "DefaultIfEmpty(${1:collection})", "IEnumerable"),
                ("SequenceEqual", "Checks sequence equality", "SequenceEqual(${1:first}, ${2:second})", "Boolean"),

                // ═══════════════════════════════════════════════════════════════
                // Memory/Pointer Functions
                // ═══════════════════════════════════════════════════════════════
                ("AddressOf", "Gets address of procedure", "AddressOf(${1:procedure})", "Pointer"),
                ("SizeOf", "Gets size of type in bytes", "SizeOf(${1:type})", "Integer"),
                ("AllocateMemory", "Allocates unmanaged memory", "AllocateMemory(${1:size})", "Pointer"),
                ("DeallocateMemory", "Frees unmanaged memory", "DeallocateMemory(${1:pointer})", "Void"),
                ("CopyMemory", "Copies memory block", "CopyMemory(${1:source}, ${2:dest}, ${3:size})", "Void"),
                ("ZeroMemory", "Fills memory with zeros", "ZeroMemory(${1:pointer}, ${2:size})", "Void"),

                // ═══════════════════════════════════════════════════════════════
                // Information Functions
                // ═══════════════════════════════════════════════════════════════
                ("TypeName", "Gets type name of object", "TypeName(${1:value})", "String"),
                ("VarType", "Gets variant type code", "VarType(${1:value})", "Integer"),
                ("IsArray", "Checks if value is array", "IsArray(${1:value})", "Boolean"),
                ("IsDate", "Checks if value is date", "IsDate(${1:value})", "Boolean"),
                ("IsNumeric", "Checks if value is numeric", "IsNumeric(${1:value})", "Boolean"),
                ("IsNothing", "Checks if value is Nothing", "IsNothing(${1:value})", "Boolean"),
                ("IsError", "Checks if value is error", "IsError(${1:value})", "Boolean"),
                ("GetType", "Gets Type object", "GetType(${1:typeName})", "Type"),

                // ═══════════════════════════════════════════════════════════════
                // Environment Functions
                // ═══════════════════════════════════════════════════════════════
                ("Environ", "Gets environment variable", "Environ(${1:name})", "String"),
                ("SetEnviron", "Sets environment variable", "SetEnviron(${1:name}, ${2:value})", "Void"),
                ("Command", "Gets command line arguments", "Command()", "String"),
                ("GetCommandLineArgs", "Gets command line args array", "GetCommandLineArgs()", "String()"),
                ("Shell", "Executes external command", "Shell(${1:command})", "Integer"),
                ("ShellExecute", "Executes with wait option", "ShellExecute(${1:command}, ${2:waitForExit})", "Integer"),
                ("Sleep", "Pauses execution", "Sleep(${1:milliseconds})", "Void"),
                ("Beep", "Plays system beep", "Beep()", "Void"),
                ("GetTickCount", "Gets system tick count", "GetTickCount()", "Long"),
                ("GetUserName", "Gets current user name", "GetUserName()", "String"),
                ("GetComputerName", "Gets computer name", "GetComputerName()", "String"),
                ("GetOSVersion", "Gets OS version", "GetOSVersion()", "String"),
                ("GetProcessorCount", "Gets processor count", "GetProcessorCount()", "Integer"),

                // ═══════════════════════════════════════════════════════════════
                // Error Handling Functions
                // ═══════════════════════════════════════════════════════════════
                ("Err", "Gets last error object", "Err()", "Exception"),
                ("ErrNumber", "Gets last error number", "ErrNumber()", "Integer"),
                ("ErrDescription", "Gets last error description", "ErrDescription()", "String"),
                ("ErrClear", "Clears error state", "ErrClear()", "Void"),
                ("ErrRaise", "Raises an error", "ErrRaise(${1:number}, ${2:description})", "Void"),

                // ═══════════════════════════════════════════════════════════════
                // Miscellaneous Functions
                // ═══════════════════════════════════════════════════════════════
                ("IIf", "Inline If expression", "IIf(${1:condition}, ${2:trueValue}, ${3:falseValue})", "Object"),
                ("Choose", "Selects value by index", "Choose(${1:index}, ${2:values})", "Object"),
                ("Switch", "Returns first true condition's value", "Switch(${1:conditions})", "Object"),
                ("CallByName", "Invokes member by name", "CallByName(${1:obj}, ${2:memberName}, ${3:callType})", "Object"),
                ("CreateObject", "Creates COM object", "CreateObject(${1:progId})", "Object"),
                ("GetObject", "Gets existing COM object", "GetObject(${1:progId})", "Object"),
                ("MsgBox", "Shows message box", "MsgBox(${1:message}, ${2:buttons}, ${3:title})", "Integer"),
                ("InputBox", "Shows input dialog", "InputBox(${1:prompt}, ${2:title}, ${3:default})", "String"),
                ("DoEvents", "Processes pending events", "DoEvents()", "Void"),
                ("Guid", "Generates new GUID", "Guid()", "String"),
                ("NewGuid", "Creates new Guid object", "NewGuid()", "Guid"),
                ("HashCode", "Gets hash code of object", "HashCode(${1:obj})", "Integer"),
                ("ReferenceEquals", "Checks reference equality", "ReferenceEquals(${1:obj1}, ${2:obj2})", "Boolean"),
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

        private IEnumerable<CompletionItem> GetSymbolCompletions(DocumentState state, int line = -1, int character = -1)
        {
            if (state.AST == null && state.SemanticAnalyzer?.GlobalScope == null) yield break;

            // Get the current scope based on cursor position
            var currentScope = GetScopeAtPosition(state, line, character);
            var addedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add symbols from the current scope and all parent scopes
            var scope = currentScope;
            while (scope != null)
            {
                foreach (var symbolKvp in scope.Symbols)
                {
                    var symbol = symbolKvp.Value;
                    if (addedSymbols.Contains(symbol.Name))
                        continue;

                    addedSymbols.Add(symbol.Name);

                    var completionItem = CreateSymbolCompletionItem(symbol);
                    if (completionItem != null)
                    {
                        yield return completionItem;
                    }
                }
                scope = scope.Parent;
            }

            // Also add top-level declarations from AST (in case semantic analysis didn't capture everything)
            if (state.AST != null)
            {
                // Convert LSP line (0-based) to AST line (1-based)
                int astLine = line + 1;

                foreach (var decl in state.AST.Declarations)
                {
                    if (decl is BasicLang.Compiler.AST.FunctionNode func)
                    {
                        if (!addedSymbols.Contains(func.Name))
                        {
                            addedSymbols.Add(func.Name);
                            yield return new CompletionItem
                            {
                                Label = func.Name,
                                Kind = CompletionItemKind.Function,
                                Detail = $"Function {func.Name}() As {func.ReturnType?.Name ?? "Void"}",
                                InsertText = func.Name
                            };
                        }

                        // If cursor is inside this function, extract local variables
                        if (astLine >= func.Line && astLine <= GetEndLine(func))
                        {
                            // Add function parameters
                            if (func.Parameters != null)
                            {
                                foreach (var param in func.Parameters)
                                {
                                    if (!addedSymbols.Contains(param.Name))
                                    {
                                        addedSymbols.Add(param.Name);
                                        yield return new CompletionItem
                                        {
                                            Label = param.Name,
                                            Kind = CompletionItemKind.Variable,
                                            Detail = $"Parameter {param.Name} As {param.Type?.Name ?? "Variant"}",
                                            InsertText = param.Name
                                        };
                                    }
                                }
                            }

                            // Extract local variables from function body
                            foreach (var localVar in ExtractLocalVariables(func.Body, astLine, addedSymbols))
                            {
                                yield return localVar;
                            }
                        }
                    }
                    else if (decl is BasicLang.Compiler.AST.SubroutineNode sub)
                    {
                        if (!addedSymbols.Contains(sub.Name))
                        {
                            addedSymbols.Add(sub.Name);
                            yield return new CompletionItem
                            {
                                Label = sub.Name,
                                Kind = CompletionItemKind.Function,
                                Detail = $"Sub {sub.Name}()",
                                InsertText = sub.Name
                            };
                        }

                        // If cursor is inside this subroutine, extract local variables
                        if (astLine >= sub.Line && astLine <= GetEndLine(sub))
                        {
                            // Add subroutine parameters
                            if (sub.Parameters != null)
                            {
                                foreach (var param in sub.Parameters)
                                {
                                    if (!addedSymbols.Contains(param.Name))
                                    {
                                        addedSymbols.Add(param.Name);
                                        yield return new CompletionItem
                                        {
                                            Label = param.Name,
                                            Kind = CompletionItemKind.Variable,
                                            Detail = $"Parameter {param.Name} As {param.Type?.Name ?? "Variant"}",
                                            InsertText = param.Name
                                        };
                                    }
                                }
                            }

                            // Extract local variables from subroutine body
                            foreach (var localVar in ExtractLocalVariables(sub.Body, astLine, addedSymbols))
                            {
                                yield return localVar;
                            }
                        }
                    }
                    else if (decl is BasicLang.Compiler.AST.ClassNode cls && !addedSymbols.Contains(cls.Name))
                    {
                        addedSymbols.Add(cls.Name);
                        yield return new CompletionItem
                        {
                            Label = cls.Name,
                            Kind = CompletionItemKind.Class,
                            Detail = $"Class {cls.Name}",
                            InsertText = cls.Name
                        };
                    }
                    else if (decl is BasicLang.Compiler.AST.VariableDeclarationNode varDecl && !addedSymbols.Contains(varDecl.Name))
                    {
                        addedSymbols.Add(varDecl.Name);
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

        /// <summary>
        /// Extract local variables from a block of statements (function/sub body)
        /// </summary>
        private IEnumerable<CompletionItem> ExtractLocalVariables(BasicLang.Compiler.AST.BlockNode body, int cursorLine, HashSet<string> addedSymbols)
        {
            if (body?.Statements == null) yield break;

            foreach (var stmt in body.Statements)
            {
                // Only include variables declared before or at the cursor position
                if (stmt.Line > cursorLine) continue;

                if (stmt is BasicLang.Compiler.AST.VariableDeclarationNode varDecl)
                {
                    if (!addedSymbols.Contains(varDecl.Name))
                    {
                        addedSymbols.Add(varDecl.Name);
                        yield return new CompletionItem
                        {
                            Label = varDecl.Name,
                            Kind = CompletionItemKind.Variable,
                            Detail = $"{varDecl.Name} As {varDecl.Type?.Name ?? "Variant"}",
                            InsertText = varDecl.Name
                        };
                    }
                }
                // Handle For loops - they declare loop variables
                else if (stmt is BasicLang.Compiler.AST.ForLoopNode forNode)
                {
                    if (!string.IsNullOrEmpty(forNode.Variable) && !addedSymbols.Contains(forNode.Variable))
                    {
                        addedSymbols.Add(forNode.Variable);
                        yield return new CompletionItem
                        {
                            Label = forNode.Variable,
                            Kind = CompletionItemKind.Variable,
                            Detail = $"{forNode.Variable} (loop variable)",
                            InsertText = forNode.Variable
                        };
                    }
                    // Also extract from for loop body
                    if (forNode.Body != null)
                    {
                        foreach (var innerVar in ExtractLocalVariables(forNode.Body, cursorLine, addedSymbols))
                        {
                            yield return innerVar;
                        }
                    }
                }
                // Handle For Each loops
                else if (stmt is BasicLang.Compiler.AST.ForEachLoopNode forEachNode)
                {
                    if (!string.IsNullOrEmpty(forEachNode.Variable) && !addedSymbols.Contains(forEachNode.Variable))
                    {
                        addedSymbols.Add(forEachNode.Variable);
                        yield return new CompletionItem
                        {
                            Label = forEachNode.Variable,
                            Kind = CompletionItemKind.Variable,
                            Detail = $"{forEachNode.Variable} (loop variable)",
                            InsertText = forEachNode.Variable
                        };
                    }
                    if (forEachNode.Body != null)
                    {
                        foreach (var innerVar in ExtractLocalVariables(forEachNode.Body, cursorLine, addedSymbols))
                        {
                            yield return innerVar;
                        }
                    }
                }
                // Handle If statements - recurse into body
                else if (stmt is BasicLang.Compiler.AST.IfStatementNode ifNode)
                {
                    if (ifNode.ThenBlock != null)
                    {
                        foreach (var innerVar in ExtractLocalVariables(ifNode.ThenBlock, cursorLine, addedSymbols))
                        {
                            yield return innerVar;
                        }
                    }
                    if (ifNode.ElseBlock != null)
                    {
                        foreach (var innerVar in ExtractLocalVariables(ifNode.ElseBlock, cursorLine, addedSymbols))
                        {
                            yield return innerVar;
                        }
                    }
                }
                // Handle While loops
                else if (stmt is BasicLang.Compiler.AST.WhileLoopNode whileNode)
                {
                    if (whileNode.Body != null)
                    {
                        foreach (var innerVar in ExtractLocalVariables(whileNode.Body, cursorLine, addedSymbols))
                        {
                            yield return innerVar;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Create a completion item from a symbol
        /// </summary>
        private CompletionItem CreateSymbolCompletionItem(Symbol symbol)
        {
            var kind = symbol.Kind switch
            {
                SemanticAnalysis.SymbolKind.Variable => CompletionItemKind.Variable,
                SemanticAnalysis.SymbolKind.Constant => CompletionItemKind.Constant,
                SemanticAnalysis.SymbolKind.Parameter => CompletionItemKind.Variable,
                SemanticAnalysis.SymbolKind.Function => CompletionItemKind.Function,
                SemanticAnalysis.SymbolKind.Subroutine => CompletionItemKind.Function,
                SemanticAnalysis.SymbolKind.Class => CompletionItemKind.Class,
                SemanticAnalysis.SymbolKind.Interface => CompletionItemKind.Interface,
                SemanticAnalysis.SymbolKind.Structure => CompletionItemKind.Struct,
                SemanticAnalysis.SymbolKind.Property => CompletionItemKind.Property,
                SemanticAnalysis.SymbolKind.Event => CompletionItemKind.Event,
                _ => CompletionItemKind.Text
            };

            var detail = symbol.Kind switch
            {
                SemanticAnalysis.SymbolKind.Variable => $"{symbol.Name} As {symbol.Type?.Name ?? "Variant"}",
                SemanticAnalysis.SymbolKind.Constant => $"Const {symbol.Name} As {symbol.Type?.Name ?? "Variant"}",
                SemanticAnalysis.SymbolKind.Parameter => $"Parameter {symbol.Name} As {symbol.Type?.Name ?? "Variant"}",
                SemanticAnalysis.SymbolKind.Function => $"Function {symbol.Name}() As {symbol.ReturnType?.Name ?? "Void"}",
                SemanticAnalysis.SymbolKind.Subroutine => $"Sub {symbol.Name}()",
                SemanticAnalysis.SymbolKind.Class => $"Class {symbol.Name}",
                SemanticAnalysis.SymbolKind.Interface => $"Interface {symbol.Name}",
                SemanticAnalysis.SymbolKind.Structure => $"Structure {symbol.Name}",
                SemanticAnalysis.SymbolKind.Property => $"Property {symbol.Name} As {symbol.Type?.Name ?? "Object"}",
                SemanticAnalysis.SymbolKind.Event => $"Event {symbol.Name}",
                _ => symbol.Name
            };

            return new CompletionItem
            {
                Label = symbol.Name,
                Kind = kind,
                Detail = detail,
                InsertText = symbol.Name
            };
        }
    }
}
