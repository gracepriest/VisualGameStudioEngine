using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.AST;
using BasicLang.Compiler;

namespace BasicLang.Compiler.SemanticAnalysis
{
    /// <summary>
    /// Semantic analyzer for BasicLang - performs type checking and scope resolution
    /// </summary>
    public class SemanticAnalyzer : IASTVisitor
    {
        private readonly TypeManager _typeManager;
        private Scope _currentScope;
        private readonly List<SemanticError> _errors;
        private readonly Dictionary<ASTNode, TypeInfo> _nodeTypes;
        private readonly Dictionary<ASTNode, Symbol> _nodeSymbols;
        private bool _inStaticContext;
        private readonly ErrorContext _errorContext;

        // Module system fields
        private ModuleRegistry _moduleRegistry;
        private ModuleResolver _moduleResolver;
        private CompilationUnit _currentUnit;

        // Project-wide symbol table for multi-file compilation
        private ProjectSymbolTable _projectSymbols;
        private string _currentModuleName;

        // Imported modules for shorthand access (no prefix needed)
        private readonly HashSet<string> _importedModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // .NET namespace tracking
        private readonly HashSet<string> _netNamespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Type registry for .NET assemblies (lazy loading)
        private TypeRegistry _typeRegistry;

        // External library loader for Import statements
        private ExternalLibraryLoader _libraryLoader;

        // Loaded external libraries for symbol resolution
        private readonly Dictionary<string, ExternalLibrary> _externalLibraries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Get the TypeRegistry for .NET type lookups
        /// </summary>
        public TypeRegistry TypeRegistry => _typeRegistry;

        // Common .NET types that should be recognized
        private static readonly HashSet<string> CommonNetTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // System types
            "Console", "Math", "Environment", "DateTime", "TimeSpan", "Guid", "Random",
            "Convert", "BitConverter", "Buffer", "Array", "Enum", "Type", "Activator",
            "Object", "String", "Int32", "Int64", "Double", "Single", "Boolean", "Char", "Byte",
            "Int16", "UInt16", "UInt32", "UInt64", "Decimal", "SByte",
            // System enums
            "ConsoleColor", "ConsoleKey", "DayOfWeek", "DateTimeKind", "StringComparison",
            "StringSplitOptions", "TypeCode", "MidpointRounding",
            // System.Text
            "StringBuilder", "Encoding", "UTF8Encoding", "ASCIIEncoding",
            // System.Collections.Generic
            "List", "Dictionary", "HashSet", "Queue", "Stack", "LinkedList",
            "SortedList", "SortedDictionary", "SortedSet", "KeyValuePair",
            // System.IO
            "File", "Directory", "Path", "Stream", "FileStream", "MemoryStream",
            "StreamReader", "StreamWriter", "BinaryReader", "BinaryWriter",
            "TextReader", "TextWriter", "StringReader", "StringWriter",
            "FileMode", "FileAccess", "FileShare", "SearchOption",
            // System.Threading
            "Thread", "Task", "Mutex", "Semaphore", "Monitor", "Interlocked",
            "CancellationToken", "CancellationTokenSource",
            // System.Net
            "WebClient", "HttpClient", "WebRequest", "WebResponse",
            // System.Linq
            "Enumerable", "Queryable",
            // System.Diagnostics
            "Process", "Stopwatch", "Debug", "Trace"
        };

        public List<SemanticError> Errors => _errors;
        public Scope GlobalScope { get; private set; }
        public ErrorContext ErrorContext => _errorContext;

        public SemanticAnalyzer()
        {
            _typeManager = new TypeManager();
            _errors = new List<SemanticError>();
            _nodeTypes = new Dictionary<ASTNode, TypeInfo>();
            _nodeSymbols = new Dictionary<ASTNode, Symbol>();
            _inStaticContext = false;
            _errorContext = new ErrorContext();
        }

        /// <summary>
        /// Configure the module system for multi-file compilation
        /// </summary>
        public void ConfigureModuleSystem(ModuleRegistry registry, ModuleResolver resolver, CompilationUnit currentUnit)
        {
            _moduleRegistry = registry;
            _moduleResolver = resolver;
            _currentUnit = currentUnit;
        }

        /// <summary>
        /// Configure the project-wide symbol table for cross-file resolution
        /// </summary>
        public void ConfigureProjectSymbols(ProjectSymbolTable projectSymbols, string currentModuleName)
        {
            _projectSymbols = projectSymbols;
            _currentModuleName = currentModuleName;
        }

        /// <summary>
        /// Get the current compilation unit
        /// </summary>
        public CompilationUnit CurrentUnit => _currentUnit;

        /// <summary>
        /// Resolve a qualified name (ModuleName.Symbol) from the project symbol table or external libraries
        /// </summary>
        private Symbol ResolveQualifiedName(string name)
        {
            // Check if name contains a dot (qualified name like ModuleName.Symbol)
            var dotIndex = name.IndexOf('.');
            if (dotIndex > 0)
            {
                var moduleName = name.Substring(0, dotIndex);
                var symbolName = name.Substring(dotIndex + 1);

                // First check project symbols
                if (_projectSymbols != null)
                {
                    var symbol = _projectSymbols.LookupQualified(moduleName, symbolName);
                    if (symbol != null)
                        return symbol;
                }

                // Then check external libraries
                if (_externalLibraries.TryGetValue(moduleName, out var library))
                {
                    return library.GetSymbol(symbolName);
                }
            }

            // Unqualified name - first check imported modules (from "Import ModuleName" statements)
            // These have priority for shorthand access
            foreach (var importedModule in _importedModules)
            {
                // Check project symbols
                if (_projectSymbols != null)
                {
                    var symbol = _projectSymbols.LookupQualified(importedModule, name);
                    if (symbol != null)
                    {
                        // Mark as imported so IRBuilder can qualify the call
                        symbol.IsImported = true;
                        symbol.SourceModule = importedModule;
                        return symbol;
                    }
                }

                // Check external libraries
                if (_externalLibraries.TryGetValue(importedModule, out var library))
                {
                    var symbol = library.GetSymbol(name);
                    if (symbol != null && symbol.IsPublic)
                    {
                        symbol.IsImported = true;
                        symbol.SourceModule = importedModule;
                        return symbol;
                    }
                }
            }

            // Not found in imports - symbol not accessible without qualification
            // (Public symbols require either "Import" or "ModuleName.Symbol" access)
            return null;
        }

        /// <summary>
        /// Configure the type registry for .NET assembly loading
        /// </summary>
        public void ConfigureTypeRegistry(TypeRegistry registry)
        {
            _typeRegistry = registry;
        }

        /// <summary>
        /// Configure the external library loader
        /// </summary>
        public void ConfigureExternalLibraryLoader(ExternalLibraryLoader loader)
        {
            _libraryLoader = loader;
        }

        /// <summary>
        /// Get the external library loader
        /// </summary>
        public ExternalLibraryLoader LibraryLoader => _libraryLoader;

        /// <summary>
        /// Resolve a symbol from external libraries
        /// </summary>
        private Symbol ResolveFromExternalLibraries(string name)
        {
            // Check if name contains a dot (LibraryName.Symbol)
            var dotIndex = name.IndexOf('.');
            if (dotIndex > 0)
            {
                var libraryName = name.Substring(0, dotIndex);
                var symbolName = name.Substring(dotIndex + 1);

                if (_externalLibraries.TryGetValue(libraryName, out var library))
                {
                    return library.GetSymbol(symbolName);
                }
            }

            // Check all imported external libraries for unqualified access
            foreach (var kvp in _externalLibraries)
            {
                var symbol = kvp.Value.GetSymbol(name);
                if (symbol != null && symbol.IsPublic)
                    return symbol;
            }

            return null;
        }

        /// <summary>
        /// Find a compilation unit by module name
        /// </summary>
        private CompilationUnit FindModuleByName(string moduleName)
        {
            if (_moduleRegistry == null) return null;

            foreach (var unit in _moduleRegistry.Modules)
            {
                if (unit.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                {
                    return unit;
                }
            }
            return null;
        }

        /// <summary>
        /// Get all loaded .NET types for IntelliSense
        /// </summary>
        public IEnumerable<NetTypeInfo> GetLoadedNetTypes()
        {
            if (_typeRegistry == null)
                return Enumerable.Empty<NetTypeInfo>();

            return _typeRegistry.GetAllLoadedTypes();
        }

        /// <summary>
        /// Get members of a .NET type for IntelliSense
        /// </summary>
        public IEnumerable<NetMemberInfo> GetNetTypeMembers(string typeName)
        {
            if (_typeRegistry == null)
                return Enumerable.Empty<NetMemberInfo>();

            return _typeRegistry.GetTypeMembers(typeName);
        }

        /// <summary>
        /// Get .NET type info by name
        /// </summary>
        public NetTypeInfo GetNetType(string typeName)
        {
            return _typeRegistry?.GetType(typeName);
        }

        public bool Analyze(ProgramNode program)
        {
            _errors.Clear();
            _nodeTypes.Clear();
            _nodeSymbols.Clear();
            _netNamespaces.Clear();

            GlobalScope = new Scope("Global", ScopeKind.Global, null);
            _currentScope = GlobalScope;

            // Register standard library functions
            RegisterStdLibFunctions();

            try
            {
                program.Accept(this);
                return _errors.Count == 0;
            }
            catch (Exception ex)
            {
                _errors.Add(new SemanticError($"Internal error: {ex.Message}\n{ex.StackTrace}", 0, 0));
                return false;
            }
        }

        public TypeInfo GetNodeType(ASTNode node)
        {
            _nodeTypes.TryGetValue(node, out var type);
            return type;
        }

        public Symbol GetNodeSymbol(ASTNode node)
        {
            _nodeSymbols.TryGetValue(node, out var symbol);
            return symbol;
        }

        private void SetNodeType(ASTNode node, TypeInfo type)
        {
            _nodeTypes[node] = type;
        }

        private void SetNodeSymbol(ASTNode node, Symbol symbol)
        {
            _nodeSymbols[node] = symbol;
        }

        /// <summary>
        /// Register standard library functions in the global scope
        /// </summary>
        private void RegisterStdLibFunctions()
        {
            // I/O Functions
            RegisterStdLibFunction("Print", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("PrintLine", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("Input", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("prompt", _typeManager.GetType("String")) });

            RegisterStdLibFunction("ReadLine", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            // String Functions
            RegisterStdLibFunction("Len", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("str", _typeManager.GetType("String")) });

            RegisterStdLibFunction("Mid", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")), ("start", _typeManager.GetType("Integer")), ("length", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("Left", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")), ("length", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("Right", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")), ("length", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("UCase", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")) });

            RegisterStdLibFunction("LCase", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")) });

            RegisterStdLibFunction("Trim", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")) });

            RegisterStdLibFunction("InStr", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("str", _typeManager.GetType("String")), ("search", _typeManager.GetType("String")) });

            RegisterStdLibFunction("Replace", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("str", _typeManager.GetType("String")), ("find", _typeManager.GetType("String")), ("replaceWith", _typeManager.GetType("String")) });

            // Math Functions
            RegisterStdLibFunction("Abs", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Sqrt", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Pow", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("baseVal", _typeManager.GetType("Double")), ("exponent", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Sin", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Cos", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Tan", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Log", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Exp", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Floor", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Ceiling", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Round", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Min", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("a", _typeManager.GetType("Double")), ("b", _typeManager.GetType("Double")) });

            RegisterStdLibFunction("Max", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("a", _typeManager.GetType("Double")), ("b", _typeManager.GetType("Double")) });

            // Random Functions
            RegisterStdLibFunction("Rnd", SymbolKind.Function, _typeManager.GetType("Double"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("Randomize", SymbolKind.Subroutine, _typeManager.VoidType,
                Array.Empty<(string, TypeInfo)>());

            // Conversion Functions
            RegisterStdLibFunction("CInt", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("CLng", SymbolKind.Function, _typeManager.GetType("Long"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("CDbl", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("CSng", SymbolKind.Function, _typeManager.GetType("Single"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("CStr", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("CBool", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("value", _typeManager.GetType("Object")) });

            // Array Functions
            RegisterStdLibFunction("UBound", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("array", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("LBound", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("array", _typeManager.GetType("Object")) });

            // Additional Conversion Functions
            RegisterStdLibFunction("Str", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("Val", SymbolKind.Function, _typeManager.GetType("Double"),
                new[] { ("str", _typeManager.GetType("String")) });

            // File I/O Functions
            RegisterStdLibFunction("FileOpen", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("filename", _typeManager.GetType("String")), ("mode", _typeManager.GetType("String")) });

            RegisterStdLibFunction("FileClose", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("handle", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("FileRead", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("handle", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("FileReadLine", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("handle", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("FileReadAll", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("handle", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("FileWrite", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("handle", _typeManager.GetType("Integer")), ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("FileWriteLine", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("handle", _typeManager.GetType("Integer")), ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("FileExists", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("filename", _typeManager.GetType("String")) });

            RegisterStdLibFunction("FileDelete", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("filename", _typeManager.GetType("String")) });

            RegisterStdLibFunction("FileCopy", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("source", _typeManager.GetType("String")), ("dest", _typeManager.GetType("String")) });

            RegisterStdLibFunction("FileEof", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("handle", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("FileSeek", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("handle", _typeManager.GetType("Integer")), ("position", _typeManager.GetType("Long")) });

            RegisterStdLibFunction("FileTell", SymbolKind.Function, _typeManager.GetType("Long"),
                new[] { ("handle", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("FileLen", SymbolKind.Function, _typeManager.GetType("Long"),
                new[] { ("filename", _typeManager.GetType("String")) });

            // Directory Functions
            RegisterStdLibFunction("DirExists", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("path", _typeManager.GetType("String")) });

            RegisterStdLibFunction("DirCreate", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("path", _typeManager.GetType("String")) });

            RegisterStdLibFunction("DirDelete", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("path", _typeManager.GetType("String")) });

            RegisterStdLibFunction("GetCurrentDir", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("SetCurrentDir", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("path", _typeManager.GetType("String")) });

            // Networking - TCP Client
            RegisterStdLibFunction("TcpConnect", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("host", _typeManager.GetType("String")), ("port", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("TcpSend", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("socket", _typeManager.GetType("Object")), ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("TcpReceive", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("socket", _typeManager.GetType("Object")), ("bufferSize", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("TcpReceiveLine", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("socket", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("TcpClose", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("socket", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("TcpIsConnected", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("socket", _typeManager.GetType("Object")) });

            // Networking - TCP Server
            RegisterStdLibFunction("TcpListen", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("port", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("TcpAccept", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("listener", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("TcpStopListener", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("listener", _typeManager.GetType("Object")) });

            // Networking - UDP
            RegisterStdLibFunction("UdpCreate", SymbolKind.Function, _typeManager.GetType("Object"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("UdpBind", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("socket", _typeManager.GetType("Object")), ("port", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("UdpSend", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("socket", _typeManager.GetType("Object")), ("host", _typeManager.GetType("String")), ("port", _typeManager.GetType("Integer")), ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("UdpReceive", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("socket", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("UdpClose", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("socket", _typeManager.GetType("Object")) });

            // Networking - HTTP
            RegisterStdLibFunction("HttpGet", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("url", _typeManager.GetType("String")) });

            RegisterStdLibFunction("HttpPost", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("url", _typeManager.GetType("String")), ("data", _typeManager.GetType("String")), ("contentType", _typeManager.GetType("String")) });

            RegisterStdLibFunction("HttpDownload", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("url", _typeManager.GetType("String")), ("filePath", _typeManager.GetType("String")) });

            // JSON Functions
            RegisterStdLibFunction("JsonParse", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("jsonString", _typeManager.GetType("String")) });

            RegisterStdLibFunction("JsonStringify", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("obj", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("JsonGet", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("json", _typeManager.GetType("Object")), ("path", _typeManager.GetType("String")) });

            RegisterStdLibFunction("JsonSet", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("json", _typeManager.GetType("Object")), ("path", _typeManager.GetType("String")), ("value", _typeManager.GetType("Object")) });

            RegisterStdLibFunction("JsonIsValid", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("jsonString", _typeManager.GetType("String")) });

            // Regex Functions
            RegisterStdLibFunction("RegexMatch", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("input", _typeManager.GetType("String")), ("pattern", _typeManager.GetType("String")) });

            RegisterStdLibFunction("RegexMatches", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("input", _typeManager.GetType("String")), ("pattern", _typeManager.GetType("String")) });

            RegisterStdLibFunction("RegexReplace", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("input", _typeManager.GetType("String")), ("pattern", _typeManager.GetType("String")), ("replacement", _typeManager.GetType("String")) });

            RegisterStdLibFunction("RegexSplit", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("input", _typeManager.GetType("String")), ("pattern", _typeManager.GetType("String")) });

            RegisterStdLibFunction("IsMatch", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("input", _typeManager.GetType("String")), ("pattern", _typeManager.GetType("String")) });

            // Environment Functions
            RegisterStdLibFunction("GetEnv", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("name", _typeManager.GetType("String")) });

            RegisterStdLibFunction("SetEnv", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("name", _typeManager.GetType("String")), ("value", _typeManager.GetType("String")) });

            RegisterStdLibFunction("GetArgs", SymbolKind.Function, _typeManager.GetType("Object"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetExePath", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetMachineName", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetUserName", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetOSVersion", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetTempPath", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("Exit", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("code", _typeManager.GetType("Integer")) });

            // Console Functions
            RegisterStdLibFunction("Cls", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("ClearScreen", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("SetForeColor", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("color", _typeManager.GetType("String")) });

            RegisterStdLibFunction("SetBackColor", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("color", _typeManager.GetType("String")) });

            RegisterStdLibFunction("ResetColor", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("SetCursorPos", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("x", _typeManager.GetType("Integer")), ("y", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("GetCursorX", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetCursorY", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("Beep", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("ReadKey", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("KeyAvailable", SymbolKind.Function, _typeManager.GetType("Boolean"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("SetTitle", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("title", _typeManager.GetType("String")) });

            RegisterStdLibFunction("GetWindowWidth", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetWindowHeight", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            // Process Functions
            RegisterStdLibFunction("Shell", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("command", _typeManager.GetType("String")) });

            RegisterStdLibFunction("ShellAsync", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("command", _typeManager.GetType("String")) });

            RegisterStdLibFunction("Run", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("exePath", _typeManager.GetType("String")), ("arguments", _typeManager.GetType("String")) });

            RegisterStdLibFunction("RunHidden", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("exePath", _typeManager.GetType("String")), ("arguments", _typeManager.GetType("String")) });

            RegisterStdLibFunction("KillProcess", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("processId", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("GetProcessId", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("GetProcesses", SymbolKind.Function, _typeManager.GetType("Object"),
                Array.Empty<(string, TypeInfo)>());

            RegisterStdLibFunction("Sleep", SymbolKind.Subroutine, _typeManager.GetType("Void"),
                new[] { ("milliseconds", _typeManager.GetType("Integer")) });

            // Crypto Functions
            RegisterStdLibFunction("MD5", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("SHA1", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("SHA256", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("Base64Encode", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("Base64Decode", SymbolKind.Function, _typeManager.GetType("String"),
                new[] { ("data", _typeManager.GetType("String")) });

            RegisterStdLibFunction("RandomBytes", SymbolKind.Function, _typeManager.GetType("Object"),
                new[] { ("count", _typeManager.GetType("Integer")) });

            RegisterStdLibFunction("NewGuid", SymbolKind.Function, _typeManager.GetType("String"),
                Array.Empty<(string, TypeInfo)>());

            // ==================== GAME FRAMEWORK FUNCTIONS ====================
            // Core
            RegisterStdLibFunction("GameInit", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("width", _typeManager.GetType("Integer")), ("height", _typeManager.GetType("Integer")), ("title", _typeManager.GetType("String")) });
            RegisterStdLibFunction("GameShutdown", SymbolKind.Subroutine, _typeManager.VoidType,
                Array.Empty<(string, TypeInfo)>());
            RegisterStdLibFunction("GameBeginFrame", SymbolKind.Subroutine, _typeManager.VoidType,
                Array.Empty<(string, TypeInfo)>());
            RegisterStdLibFunction("GameEndFrame", SymbolKind.Subroutine, _typeManager.VoidType,
                Array.Empty<(string, TypeInfo)>());
            RegisterStdLibFunction("GameShouldClose", SymbolKind.Function, _typeManager.GetType("Boolean"),
                Array.Empty<(string, TypeInfo)>());
            RegisterStdLibFunction("GameGetDeltaTime", SymbolKind.Function, _typeManager.GetType("Single"),
                Array.Empty<(string, TypeInfo)>());
            RegisterStdLibFunction("GameGetFPS", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            // Input
            RegisterStdLibFunction("IsKeyPressed", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("key", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("IsKeyDown", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("key", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("IsKeyReleased", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("key", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("IsMouseButtonPressed", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("button", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("IsMouseButtonDown", SymbolKind.Function, _typeManager.GetType("Boolean"),
                new[] { ("button", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("GetMouseX", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());
            RegisterStdLibFunction("GetMouseY", SymbolKind.Function, _typeManager.GetType("Integer"),
                Array.Empty<(string, TypeInfo)>());

            // Drawing
            RegisterStdLibFunction("ClearBackground", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("r", _typeManager.GetType("Integer")), ("g", _typeManager.GetType("Integer")), ("b", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("DrawRectangle", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("x", _typeManager.GetType("Single")), ("y", _typeManager.GetType("Single")), ("w", _typeManager.GetType("Single")), ("h", _typeManager.GetType("Single")),
                        ("r", _typeManager.GetType("Integer")), ("g", _typeManager.GetType("Integer")), ("b", _typeManager.GetType("Integer")), ("a", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("DrawCircle", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("x", _typeManager.GetType("Single")), ("y", _typeManager.GetType("Single")), ("radius", _typeManager.GetType("Single")),
                        ("r", _typeManager.GetType("Integer")), ("g", _typeManager.GetType("Integer")), ("b", _typeManager.GetType("Integer")), ("a", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("DrawLine", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("x1", _typeManager.GetType("Single")), ("y1", _typeManager.GetType("Single")), ("x2", _typeManager.GetType("Single")), ("y2", _typeManager.GetType("Single")),
                        ("r", _typeManager.GetType("Integer")), ("g", _typeManager.GetType("Integer")), ("b", _typeManager.GetType("Integer")), ("a", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("DrawText", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("text", _typeManager.GetType("String")), ("x", _typeManager.GetType("Single")), ("y", _typeManager.GetType("Single")), ("fontSize", _typeManager.GetType("Integer")),
                        ("r", _typeManager.GetType("Integer")), ("g", _typeManager.GetType("Integer")), ("b", _typeManager.GetType("Integer")), ("a", _typeManager.GetType("Integer")) });

            // Textures
            RegisterStdLibFunction("LoadTexture", SymbolKind.Function, _typeManager.GetType("Integer"),
                new[] { ("path", _typeManager.GetType("String")) });
            RegisterStdLibFunction("UnloadTexture", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("handle", _typeManager.GetType("Integer")) });
            RegisterStdLibFunction("DrawTexture", SymbolKind.Subroutine, _typeManager.VoidType,
                new[] { ("handle", _typeManager.GetType("Integer")), ("x", _typeManager.GetType("Single")), ("y", _typeManager.GetType("Single")),
                        ("r", _typeManager.GetType("Integer")), ("g", _typeManager.GetType("Integer")), ("b", _typeManager.GetType("Integer")), ("a", _typeManager.GetType("Integer")) });
        }

        private void RegisterStdLibFunction(string name, SymbolKind kind, TypeInfo returnType, (string name, TypeInfo type)[] parameters)
        {
            var symbol = new Symbol(name, kind, returnType, 0, 0)
            {
                ReturnType = returnType,
                IsDefined = true
            };

            foreach (var param in parameters)
            {
                symbol.Parameters.Add(new Symbol(param.name, SymbolKind.Parameter, param.type, 0, 0));
            }

            GlobalScope.Define(symbol);
        }

        private void Error(string message, int line, int column)
        {
            // Add context to error message if available
            var contextualMessage = _errorContext.FormatErrorWithContext(message);
            _errors.Add(new SemanticError(contextualMessage, line, column));
        }

        private void ErrorWithPoisoning(string message, int line, int column, string symbolName)
        {
            // Record the error and poison the symbol to prevent cascade errors
            Error(message, line, column);
            if (!string.IsNullOrEmpty(symbolName))
            {
                _errorContext.PoisonSymbol(symbolName);
            }
        }

        private bool IsSymbolPoisoned(string symbolName)
        {
            return _errorContext.IsSymbolPoisoned(symbolName);
        }

        private void Warning(string message, int line, int column)
        {
            _errors.Add(new SemanticError(message, line, column, ErrorSeverity.Warning));
        }

        /// <summary>
        /// Get grouped errors for better error reporting
        /// </summary>
        public List<ErrorGroup> GetGroupedErrors()
        {
            return ErrorGrouper.GroupErrors(_errors);
        }

        private Scope EnterScope(string name, ScopeKind kind)
        {
            var newScope = new Scope(name, kind, _currentScope);
            _currentScope = newScope;
            return newScope;
        }

        private void ExitScope()
        {
            if (_currentScope.Parent != null)
            {
                _currentScope = _currentScope.Parent;
            }
        }

        private TypeInfo ResolveTypeReference(TypeReference typeRef)
        {
            if (typeRef == null)
                return _typeManager.VoidType;

            // Handle pointer types
            if (typeRef.IsPointer)
            {
                var baseType = ResolveTypeReference(new TypeReference(typeRef.Name));
                return _typeManager.CreatePointerType(baseType);
            }

            // Handle array types
            if (typeRef.IsArray)
            {
                var elementType = ResolveTypeName(typeRef.Name);
                if (elementType == null)
                {
                    Error($"Unknown type '{typeRef.Name}'", 0, 0);
                    return _typeManager.ObjectType;
                }
                // Get the array size from the first dimension (for 1D arrays)
                int arraySize = typeRef.ArrayDimensions.Count > 0 ? typeRef.ArrayDimensions[0] : 0;
                return _typeManager.CreateArrayType(elementType, typeRef.ArrayDimensions.Count, arraySize);
            }

            // Handle generic types
            if (typeRef.GenericArguments.Count > 0)
            {
                var typeArgs = typeRef.GenericArguments
                    .Select(t => ResolveTypeReference(t))
                    .ToList();
                var genericType = _typeManager.CreateGenericType(typeRef.Name, typeArgs);

                // If CreateGenericType returns null (base type not registered),
                // create a synthetic generic type for .NET types like List, Dictionary, etc.
                if (genericType == null && IsNetType(typeRef.Name))
                {
                    genericType = new TypeInfo(typeRef.Name, TypeKind.Class);
                    genericType.GenericArguments.AddRange(typeArgs);
                }

                return genericType ?? _typeManager.ObjectType;
            }

            // Regular type lookup (check scope for type parameters first, then type manager)
            var type = ResolveTypeName(typeRef.Name);
            if (type == null)
            {
                Error($"Unknown type '{typeRef.Name}'", 0, 0);
                return _typeManager.ObjectType;
            }

            // Handle fixed-length strings
            if (typeRef.IsFixedLengthString)
            {
                var fixedStringType = new TypeInfo($"String*{typeRef.FixedStringLength}", TypeKind.Primitive);
                fixedStringType.IsFixedLengthString = true;
                fixedStringType.FixedStringLength = typeRef.FixedStringLength;
                return fixedStringType;
            }

            // Handle nullable types
            if (typeRef.IsNullable)
            {
                var nullableType = new TypeInfo($"{type.Name}?", TypeKind.Nullable);
                nullableType.IsNullable = true;
                nullableType.UnderlyingType = type;
                return nullableType;
            }

            return type;
        }

        /// <summary>
        /// Resolves a type name by checking the current scope for type parameters first,
        /// then falling back to the type manager, and finally checking for .NET types.
        /// </summary>
        private TypeInfo ResolveTypeName(string name)
        {
            // First, check if it's a type parameter in the current scope
            var symbol = _currentScope.Resolve(name);
            if (symbol != null && symbol.Kind == SymbolKind.TypeParameter)
            {
                return symbol.Type;
            }

            // Fall back to the type manager
            var type = _typeManager.GetType(name);
            if (type != null)
            {
                return type;
            }

            // Check if this could be a .NET type
            if (IsNetType(name))
            {
                // Create a synthetic type for .NET types
                return new TypeInfo(name, TypeKind.Class);
            }

            return null;
        }

        /// <summary>
        /// Checks if a type name could be a .NET type
        /// </summary>
        private bool IsNetType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // Fully qualified .NET type (e.g., System.IO.File)
            if (name.Contains('.'))
            {
                var parts = name.Split('.');
                var rootNamespace = parts[0];
                // Check if it starts with a known .NET root namespace
                if (rootNamespace.Equals("System", StringComparison.OrdinalIgnoreCase) ||
                    rootNamespace.Equals("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                    rootNamespace.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // Check if it's a commonly known .NET type
            if (CommonNetTypes.Contains(name))
            {
                return true;
            }

            // Be VERY permissive: any PascalCase identifier could be a .NET type
            // The C# compiler will validate the actual type existence
            if (char.IsUpper(name[0]) && name.Length > 1 && !name.Contains('_'))
            {
                return true;
            }

            return false;
        }

        // ====================================================================
        // Program Structure
        // ====================================================================

        public void Visit(ProgramNode node)
        {
            foreach (var declaration in node.Declarations)
            {
                declaration.Accept(this);
            }
        }

        // ====================================================================
        // Declarations
        // ====================================================================

        public void Visit(NamespaceNode node)
        {
            EnterScope(node.Name, ScopeKind.Namespace);

            var symbol = new Symbol(node.Name, SymbolKind.Namespace, null, node.Line, node.Column);
            _currentScope.Parent.Define(symbol);

            foreach (var member in node.Members)
            {
                member.Accept(this);
            }

            ExitScope();
        }

        public void Visit(ModuleNode node)
        {
            EnterScope(node.Name, ScopeKind.Module);

            var symbol = new Symbol(node.Name, SymbolKind.Module, null, node.Line, node.Column);
            _currentScope.Parent.Define(symbol);

            foreach (var member in node.Members)
            {
                member.Accept(this);
            }

            ExitScope();
        }

        public void Visit(ClassNode node)
        {
            // Define the class type
            var classType = _typeManager.DefineType(node.Name, TypeKind.Class);
            if (classType == null)
            {
                Error($"Class '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            // Set abstract flag
            classType.IsAbstract = node.IsAbstract;

            // Set base class
            if (node.BaseClass != null)
            {
                var baseType = _typeManager.GetType(node.BaseClass);
                if (baseType == null)
                {
                    Error($"Unknown base class '{node.BaseClass}'", node.Line, node.Column);
                }
                else if (baseType.Kind != TypeKind.Class)
                {
                    Error($"'{node.BaseClass}' is not a class", node.Line, node.Column);
                }
                else
                {
                    classType.BaseType = baseType;
                }
            }

            // Add interfaces
            foreach (var interfaceName in node.Interfaces)
            {
                var interfaceType = _typeManager.GetType(interfaceName);
                if (interfaceType == null)
                {
                    Error($"Unknown interface '{interfaceName}'", node.Line, node.Column);
                }
                else if (interfaceType.Kind != TypeKind.Interface)
                {
                    Error($"'{interfaceName}' is not an interface", node.Line, node.Column);
                }
                else
                {
                    classType.Interfaces.Add(interfaceType);
                }
            }

            // Define class symbol
            var symbol = new Symbol(node.Name, SymbolKind.Class, classType, node.Line, node.Column);
            if (!_currentScope.Define(symbol))
            {
                Error($"Symbol '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            // Enter class scope
            var classScope = EnterScope(node.Name, ScopeKind.Class);
            classScope.ClassType = classType;

            // Register generic type parameters in the class scope
            foreach (var genericParam in node.GenericParameters)
            {
                // Create a type parameter type
                var typeParamType = new TypeInfo(genericParam, TypeKind.TypeParameter);

                // Define type parameter as a symbol in the class scope (not global type manager)
                var typeParamSymbol = new Symbol(genericParam, SymbolKind.TypeParameter, typeParamType, node.Line, node.Column);
                classScope.Define(typeParamSymbol);

                // Track it in the class symbol
                symbol.GenericParameters.Add(typeParamType);
            }

            // Process members
            bool hasAbstractMembers = false;
            foreach (var member in node.Members)
            {
                member.Accept(this);

                // Check for abstract methods
                if (member is FunctionNode func)
                {
                    if (func.IsAbstract)
                    {
                        hasAbstractMembers = true;
                        // Validate: abstract methods can't have a body
                        if (func.Body != null && func.Body.Statements.Count > 0)
                        {
                            Error($"Abstract method '{func.Name}' cannot have a body", func.Line, func.Column);
                        }
                        // Validate: abstract methods must be in abstract classes
                        if (!node.IsAbstract)
                        {
                            Error($"Abstract method '{func.Name}' can only be declared in an abstract class. Mark class '{node.Name}' as MustInherit", func.Line, func.Column);
                        }
                    }
                    if (_nodeSymbols.TryGetValue(func, out var funcSymbol))
                    {
                        classType.Members[func.Name] = funcSymbol;
                    }
                }
                else if (member is SubroutineNode sub)
                {
                    if (sub.IsAbstract)
                    {
                        hasAbstractMembers = true;
                        // Validate: abstract methods can't have a body
                        if (sub.Body != null && sub.Body.Statements.Count > 0)
                        {
                            Error($"Abstract method '{sub.Name}' cannot have a body", sub.Line, sub.Column);
                        }
                        // Validate: abstract methods must be in abstract classes
                        if (!node.IsAbstract)
                        {
                            Error($"Abstract method '{sub.Name}' can only be declared in an abstract class. Mark class '{node.Name}' as MustInherit", sub.Line, sub.Column);
                        }
                    }
                    if (_nodeSymbols.TryGetValue(sub, out var subSymbol))
                    {
                        classType.Members[sub.Name] = subSymbol;
                    }
                }
                else if (member is VariableDeclarationNode varDecl && _nodeSymbols.TryGetValue(varDecl, out var varSymbol))
                {
                    classType.Members[varDecl.Name] = varSymbol;
                }
                else if (member is PropertyNode prop && _nodeSymbols.TryGetValue(prop, out var propSymbol))
                {
                    classType.Members[prop.Name] = propSymbol;
                }
            }

            // Validate: abstract classes should have at least one abstract member (warning, not error)
            // This is not enforced in VB.NET, but it's a good practice

            ExitScope();
        }

        public void Visit(InterfaceNode node)
        {
            var interfaceType = _typeManager.DefineType(node.Name, TypeKind.Interface);
            if (interfaceType == null)
            {
                Error($"Interface '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            var symbol = new Symbol(node.Name, SymbolKind.Interface, interfaceType, node.Line, node.Column);
            if (!_currentScope.Define(symbol))
            {
                Error($"Symbol '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            EnterScope(node.Name, ScopeKind.Interface);

            foreach (var method in node.Methods)
            {
                method.Accept(this);

                // Validate default implementations
                if (!method.IsAbstract && method.Body != null)
                {
                    // Default implementation - validate it
                    // For now, we allow any implementation - the C# compiler will catch issues
                    // Future: Add more strict validation for default interface methods
                }
            }

            ExitScope();
        }

        public void Visit(EnumNode node)
        {
            var enumType = _typeManager.DefineType(node.Name, TypeKind.Enum);
            if (enumType == null)
            {
                Error($"Enum '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            var symbol = new Symbol(node.Name, SymbolKind.Type, enumType, node.Line, node.Column);
            if (!_currentScope.Define(symbol))
            {
                Error($"Symbol '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            // Define enum members as constants in current scope
            foreach (var member in node.Members)
            {
                var memberSymbol = new Symbol(
                    $"{node.Name}.{member.Name}",
                    SymbolKind.Constant,
                    enumType,
                    member.Line,
                    member.Column
                );
                _currentScope.Define(memberSymbol);

                // If member has explicit value, analyze it
                if (member.Value != null)
                {
                    member.Value.Accept(this);
                }
            }
        }

        public void Visit(EnumMemberNode node)
        {
            // Enum members are processed in EnumNode visitor
        }

        public void Visit(TypeNode node)
        {
            var type = _typeManager.DefineType(node.Name, TypeKind.UserDefinedType);
            if (type == null)
            {
                Error($"Type '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            var symbol = new Symbol(node.Name, SymbolKind.Type, type, node.Line, node.Column);
            if (!_currentScope.Define(symbol))
            {
                Error($"Symbol '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            // Process members
            foreach (var member in node.Members)
            {
                var memberType = ResolveTypeReference(member.Type);
                var memberSymbol = new Symbol(member.Name, SymbolKind.Variable, memberType, member.Line, member.Column);
                type.Members[member.Name] = memberSymbol;
            }
        }

        public void Visit(StructureNode node)
        {
            var type = _typeManager.DefineType(node.Name, TypeKind.Structure);
            if (type == null)
            {
                Error($"Structure '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            var symbol = new Symbol(node.Name, SymbolKind.Structure, type, node.Line, node.Column);
            if (!_currentScope.Define(symbol))
            {
                Error($"Symbol '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            // Process members
            foreach (var member in node.Members)
            {
                var memberType = ResolveTypeReference(member.Type);
                var memberSymbol = new Symbol(member.Name, SymbolKind.Variable, memberType, member.Line, member.Column);
                type.Members[member.Name] = memberSymbol;
            }
        }

        public void Visit(UnionNode node)
        {
            var type = _typeManager.DefineType(node.Name, TypeKind.Union);
            if (type == null)
            {
                Error($"Union '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            var symbol = new Symbol(node.Name, SymbolKind.Type, type, node.Line, node.Column);
            if (!_currentScope.Define(symbol))
            {
                Error($"Symbol '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            // Process members - in a union, all members share the same memory location
            foreach (var member in node.Members)
            {
                var memberType = ResolveTypeReference(member.Type);
                var memberSymbol = new Symbol(member.Name, SymbolKind.Variable, memberType, member.Line, member.Column);
                type.Members[member.Name] = memberSymbol;
            }
        }

        public void Visit(FunctionNode node)
        {
            var symbol = new Symbol(node.Name, SymbolKind.Function, null, node.Line, node.Column);
            symbol.Access = node.Access;

            // Check if there's an existing symbol (could be stdlib function)
            var existing = _currentScope.ResolveLocal(node.Name);
            if (existing != null)
            {
                // Allow overriding stdlib functions (line 0 means stdlib)
                if (existing.Line == 0 && existing.Column == 0)
                {
                    // Remove the stdlib definition to allow user override
                    _currentScope.Symbols.Remove(node.Name);
                }
                else if (!_currentScope.Define(symbol))
                {
                    Error($"Function '{node.Name}' is already defined in this scope", node.Line, node.Column);
                    // Continue processing even with error
                }
            }

            _currentScope.Define(symbol);

            // Enter function scope
            var functionScope = EnterScope(node.Name, ScopeKind.Function);

            // Register generic type parameters as symbols in the function scope BEFORE resolving return type
            var genericTypeParams = new List<TypeInfo>();
            foreach (var genericParam in node.GenericParameters)
            {
                var typeParamType = new TypeInfo(genericParam, TypeKind.TypeParameter);
                var typeParamSymbol = new Symbol(genericParam, SymbolKind.TypeParameter, typeParamType, node.Line, node.Column);
                functionScope.Define(typeParamSymbol);
                genericTypeParams.Add(typeParamType);
            }
            symbol.GenericParameters.AddRange(genericTypeParams);

            // Now resolve return type (type parameters are in scope)
            var returnType = ResolveTypeReference(node.ReturnType);
            symbol.ReturnType = returnType;
            symbol.Type = returnType;
            functionScope.ReturnType = returnType;

            SetNodeSymbol(node, symbol);
            SetNodeType(node, returnType);

            // Process parameters
            foreach (var param in node.Parameters)
            {
                param.Accept(this);
                if (_nodeSymbols.TryGetValue(param, out var paramSymbol))
                {
                    symbol.Parameters.Add(paramSymbol);
                }
            }

            // Set static context if this is a static function
            var wasInStaticContext = _inStaticContext;
            if (node.IsStatic)
            {
                _inStaticContext = true;
            }

            // Process body
            if (node.Body != null && !node.IsAbstract)
            {
                node.Body.Accept(this);
            }

            // Restore static context
            _inStaticContext = wasInStaticContext;

            ExitScope();
        }

        public void Visit(SubroutineNode node)
        {
            var symbol = new Symbol(node.Name, SymbolKind.Subroutine, _typeManager.VoidType, node.Line, node.Column);
            symbol.ReturnType = _typeManager.VoidType;
            symbol.Access = node.Access;

            // Check if there's an existing symbol (could be stdlib function)
            var existing = _currentScope.ResolveLocal(node.Name);
            if (existing != null)
            {
                // Allow overriding stdlib functions (line 0 means stdlib)
                if (existing.Line == 0 && existing.Column == 0)
                {
                    _currentScope.Symbols.Remove(node.Name);
                }
                else if (!_currentScope.Define(symbol))
                {
                    Error($"Subroutine '{node.Name}' is already defined in this scope", node.Line, node.Column);
                }
            }

            _currentScope.Define(symbol);
            SetNodeSymbol(node, symbol);

            // Enter subroutine scope
            var subScope = EnterScope(node.Name, ScopeKind.Subroutine);
            subScope.ReturnType = _typeManager.VoidType;

            // Register generic type parameters as symbols in the subroutine scope
            var genericTypeParams = new List<TypeInfo>();
            foreach (var genericParam in node.GenericParameters)
            {
                var typeParamType = new TypeInfo(genericParam, TypeKind.TypeParameter);
                var typeParamSymbol = new Symbol(genericParam, SymbolKind.TypeParameter, typeParamType, node.Line, node.Column);
                subScope.Define(typeParamSymbol);
                genericTypeParams.Add(typeParamType);
            }
            symbol.GenericParameters.AddRange(genericTypeParams);

            // Process parameters
            foreach (var param in node.Parameters)
            {
                param.Accept(this);
                if (_nodeSymbols.TryGetValue(param, out var paramSymbol))
                {
                    symbol.Parameters.Add(paramSymbol);
                }
            }

            // Set static context if this is a static subroutine
            var wasInStaticContext = _inStaticContext;
            if (node.IsStatic)
            {
                _inStaticContext = true;
            }

            // Process body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Restore static context
            _inStaticContext = wasInStaticContext;

            ExitScope();
        }

        public void Visit(ParameterNode node)
        {
            var paramType = ResolveTypeReference(node.Type);
            var symbol = new Symbol(node.Name, SymbolKind.Parameter, paramType, node.Line, node.Column)
            {
                IsOptional = node.IsOptional,
                IsParamArray = node.IsParamArray,
                IsByRef = node.IsByRef
            };

            if (!_currentScope.Define(symbol))
            {
                Error($"Parameter '{node.Name}' is already defined", node.Line, node.Column);
            }

            SetNodeSymbol(node, symbol);
            SetNodeType(node, paramType);

            // Check default value type
            if (node.DefaultValue != null)
            {
                node.DefaultValue.Accept(this);
                var defaultType = GetNodeType(node.DefaultValue);

                if (!paramType.IsAssignableFrom(defaultType))
                {
                    Error($"Default value type '{defaultType}' is not compatible with parameter type '{paramType}'",
                          node.Line, node.Column);
                }
            }
        }

        public void Visit(VariableDeclarationNode node)
        {
            TypeInfo varType;

            if (node.IsAuto)
            {
                // Type inference from initializer
                if (node.Initializer == null)
                {
                    Error($"Auto variable '{node.Name}' must have an initializer", node.Line, node.Column);
                    varType = _typeManager.ObjectType;
                }
                else
                {
                    node.Initializer.Accept(this);
                    varType = GetNodeType(node.Initializer);
                    if (varType == null)
                    {
                        Error($"Cannot infer type for variable '{node.Name}'", node.Line, node.Column);
                        varType = _typeManager.ObjectType;
                    }
                }
            }
            else
            {
                varType = ResolveTypeReference(node.Type);
                if (varType == null)
                {
                    Error($"Unknown type '{node.Type?.Name ?? "null"}' for variable '{node.Name}'", node.Line, node.Column);
                    varType = _typeManager.ObjectType;
                }
            }

            var symbol = new Symbol(node.Name, SymbolKind.Variable, varType, node.Line, node.Column);
            symbol.Access = node.Access;

            if (!_currentScope.Define(symbol))
            {
                Error($"Variable '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            SetNodeSymbol(node, symbol);
            SetNodeType(node, varType);

            // Check initializer type
            if (node.Initializer != null && !node.IsAuto)
            {
                node.Initializer.Accept(this);
                var initType = GetNodeType(node.Initializer);

                if (initType != null && !varType.IsAssignableFrom(initType))
                {
                    Error($"Cannot assign value of type '{initType}' to variable of type '{varType}'",
                          node.Line, node.Column);
                }
            }
        }

        public void Visit(TupleDeconstructionNode node)
        {
            // Analyze the initializer
            node.Initializer.Accept(this);
            var initType = GetNodeType(node.Initializer);

            // Define each variable in the tuple
            for (int i = 0; i < node.Variables.Count; i++)
            {
                var (varName, varTypeRef) = node.Variables[i];

                TypeInfo varType;
                if (varTypeRef != null)
                {
                    varType = ResolveTypeReference(varTypeRef);
                }
                else if (initType?.TupleElementTypes != null && i < initType.TupleElementTypes.Count)
                {
                    varType = initType.TupleElementTypes[i];
                }
                else
                {
                    varType = _typeManager.ObjectType;
                }

                var symbol = new Symbol(varName, SymbolKind.Variable, varType, node.Line, node.Column);

                if (!_currentScope.Define(symbol))
                {
                    Error($"Variable '{varName}' is already defined in this scope", node.Line, node.Column);
                }
            }
        }

        public void Visit(ConstantDeclarationNode node)
        {
            var constType = ResolveTypeReference(node.Type);

            if (node.Value == null)
            {
                Error($"Constant '{node.Name}' must have a value", node.Line, node.Column);
            }
            else
            {
                node.Value.Accept(this);
                var valueType = GetNodeType(node.Value);

                if (!constType.IsAssignableFrom(valueType))
                {
                    Error($"Constant value type '{valueType}' is not compatible with declared type '{constType}'",
                          node.Line, node.Column);
                }
            }

            var symbol = new Symbol(node.Name, SymbolKind.Constant, constType, node.Line, node.Column);
            symbol.IsConstant = true;

            if (!_currentScope.Define(symbol))
            {
                Error($"Constant '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            SetNodeSymbol(node, symbol);
            SetNodeType(node, constType);
        }

        public void Visit(TypeDefineNode node)
        {
            var baseType = ResolveTypeReference(node.BaseType);

            // Create alias
            var aliasType = new TypeInfo(node.AliasName, baseType.Kind)
            {
                BaseType = baseType
            };

            if (_typeManager.DefineType(node.AliasName, baseType.Kind) == null)
            {
                Error($"Type alias '{node.AliasName}' is already defined", node.Line, node.Column);
            }
        }

        public void Visit(TemplateDeclarationNode node)
        {
            // TODO: Implement generic type parameters
            if (node.Declaration != null)
            {
                node.Declaration.Accept(this);
            }
        }

        public void Visit(DelegateDeclarationNode node)
        {
            var delegateType = _typeManager.DefineType(node.Name, TypeKind.Delegate);
            if (delegateType == null)
            {
                Error($"Delegate '{node.Name}' is already defined", node.Line, node.Column);
                return;
            }

            var returnType = ResolveTypeReference(node.ReturnType);
            var symbol = new Symbol(node.Name, SymbolKind.Class, delegateType, node.Line, node.Column);
            symbol.ReturnType = returnType;

            foreach (var param in node.Parameters)
            {
                param.Accept(this);
                if (_nodeSymbols.TryGetValue(param, out var paramSymbol))
                {
                    symbol.Parameters.Add(paramSymbol);
                }
            }

            if (!_currentScope.Define(symbol))
            {
                Error($"Delegate '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }
        }

        public void Visit(ExtensionMethodNode node)
        {
            // Verify extended type exists
            var extendedType = _typeManager.GetType(node.ExtendedType);
            if (extendedType == null)
            {
                Error($"Cannot extend unknown type '{node.ExtendedType}'", node.Line, node.Column);
            }

            if (node.Method != null)
            {
                node.Method.Accept(this);
            }
        }

        public void Visit(ExternDeclarationNode node)
        {
            // Validate extern declaration
            if (string.IsNullOrEmpty(node.Name))
            {
                Error("Extern declaration must have a name", node.Line, node.Column);
                return;
            }

            // Validate parameters
            foreach (var param in node.Parameters)
            {
                param.Accept(this);
            }

            // Determine return type
            var returnType = _typeManager.GetType("Void");
            if (node.IsFunction && node.ReturnType != null)
            {
                returnType = _typeManager.GetType(node.ReturnType.Name);
                if (returnType == null)
                {
                    Error($"Unknown return type '{node.ReturnType.Name}' for extern '{node.Name}'", node.Line, node.Column);
                    returnType = _typeManager.GetType("Void");
                }
            }

            // Register extern as a function/sub in the symbol table
            var symbolKind = node.IsFunction ? SymbolKind.Function : SymbolKind.Subroutine;
            var externSymbol = new Symbol(node.Name, symbolKind, returnType, node.Line, node.Column)
            {
                ReturnType = returnType,
                IsExtern = true,
                ExternImplementations = new Dictionary<string, string>(node.PlatformImplementations)
            };

            // Add parameters to the symbol
            foreach (var param in node.Parameters)
            {
                var paramType = _typeManager.GetType(param.Type?.Name ?? "Object");
                var paramSymbol = new Symbol(param.Name, SymbolKind.Parameter, paramType, param.Line, param.Column);
                externSymbol.Parameters.Add(paramSymbol);
            }

            if (!_currentScope.Define(externSymbol))
            {
                Error($"Extern '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }
        }

        public void Visit(UsingDirectiveNode node)
        {
            // Track .NET namespaces for type resolution
            if (node.IsNetNamespace)
            {
                _netNamespaces.Add(node.Namespace);

                // Load types from the TypeRegistry if configured
                if (_typeRegistry != null)
                {
                    _typeRegistry.LoadNamespace(node.Namespace);
                }
            }

            // Track the using directive for module resolution
            if (_moduleRegistry != null && _currentUnit != null)
            {
                var usingInfo = new UsingInfo(node.Namespace, node.Line, node.Column, node.IsNetNamespace, node.Alias);
                _currentUnit.Usings.Add(usingInfo);

                // If this is a .NET namespace, don't try to resolve as BasicLang files
                if (node.IsNetNamespace)
                {
                    // .NET namespaces are passed through to the backend
                    // No file resolution needed
                    return;
                }

                // Resolve namespace and import symbols (for BasicLang modules)
                var files = _moduleResolver?.ResolveNamespace(node.Namespace, _currentUnit.FilePath);
                if (files != null && files.Count > 0)
                {
                    usingInfo.ResolvedPaths.AddRange(files);

                    // Import symbols from each file
                    foreach (var file in files)
                    {
                        ImportSymbolsFromFile(file, node.Line, node.Column);
                    }
                }
            }
        }

        public void Visit(ImportDirectiveNode node)
        {
            // Check if this is an external library import
            if (node.IsExternalLibrary)
            {
                LoadExternalLibrary(node);
                return;
            }

            // Track the imported module for shorthand access (no prefix needed)
            _importedModules.Add(node.Module);

            // Verify the module exists in the project symbol table
            if (_projectSymbols != null && !_projectSymbols.HasModule(node.Module))
            {
                // Module not found in project - might be an external library or not compiled yet
                // Don't error here, let the module registry handle it if available
            }

            // Track the import directive for module resolution (legacy support)
            if (_moduleRegistry != null && _currentUnit != null)
            {
                var importInfo = new ImportInfo(node.Module, node.Line, node.Column);
                _currentUnit.Imports.Add(importInfo);

                // Resolve the module path
                var resolvedPath = _moduleResolver?.ResolveModule(node.Module, _currentUnit.FilePath);
                if (resolvedPath != null)
                {
                    importInfo.ResolvedPath = resolvedPath;
                    _currentUnit.Dependencies.Add(ModuleResolver.GetModuleId(resolvedPath));

                    // Import symbols from the module
                    ImportSymbolsFromFile(resolvedPath, node.Line, node.Column);
                }
                else if (_projectSymbols == null || !_projectSymbols.HasModule(node.Module))
                {
                    // Only error if the module wasn't found in project symbols either
                    Error($"Cannot resolve import '{node.Module}'", node.Line, node.Column);
                }
            }
        }

        /// <summary>
        /// Load an external library and make its symbols available
        /// </summary>
        private void LoadExternalLibrary(ImportDirectiveNode node)
        {
            if (_libraryLoader == null)
            {
                // Create a default loader if not configured
                _libraryLoader = new ExternalLibraryLoader();
            }

            // Resolve the library path relative to current file if needed
            var libraryPath = node.LibraryPath;
            if (!System.IO.Path.IsPathRooted(libraryPath) && _currentUnit != null)
            {
                var currentDir = System.IO.Path.GetDirectoryName(_currentUnit.FilePath);
                if (!string.IsNullOrEmpty(currentDir))
                {
                    var relativePath = System.IO.Path.Combine(currentDir, libraryPath);
                    if (System.IO.File.Exists(relativePath))
                    {
                        libraryPath = relativePath;
                    }
                }
            }

            // Load the library
            var library = _libraryLoader.LoadLibrary(libraryPath);
            if (library == null)
            {
                foreach (var error in _libraryLoader.Errors)
                {
                    Error(error, node.Line, node.Column);
                }
                return;
            }

            // Store the library for symbol resolution
            _externalLibraries[node.Module] = library;
            _importedModules.Add(node.Module);

            // Register symbols in the global scope for immediate access
            foreach (var kvp in library.Symbols)
            {
                var symbol = kvp.Value;
                if (symbol.IsPublic)
                {
                    // Mark as imported
                    symbol.IsImported = true;
                    symbol.SourceModule = node.Module;

                    // Register in global scope if not already defined
                    if (_currentScope.Resolve(symbol.Name) == null)
                    {
                        _currentScope.Define(symbol);
                    }
                }
            }
        }

        /// <summary>
        /// Import public symbols from a compiled module
        /// </summary>
        private void ImportSymbolsFromFile(string filePath, int line, int column)
        {
            if (_moduleRegistry == null) return;

            var moduleId = ModuleResolver.GetModuleId(filePath);
            var unit = _moduleRegistry.Get(moduleId);

            if (unit == null || !unit.IsComplete)
            {
                // Module hasn't been compiled yet - this will be handled by the Compiler
                return;
            }

            // Import exported symbols into current scope
            foreach (var symbol in unit.ExportedSymbols)
            {
                // Only import if not already defined
                if (_currentScope.Resolve(symbol.Name) == null)
                {
                    // Clone the symbol for the current scope
                    var importedSymbol = new Symbol(
                        symbol.Name,
                        symbol.Kind,
                        symbol.Type,
                        line,
                        column)
                    {
                        IsImported = true,
                        SourceModule = unit.ModuleName,
                        // Copy function-related properties
                        Parameters = symbol.Parameters,
                        ReturnType = symbol.ReturnType,
                        Access = symbol.Access,
                        IsExtern = symbol.IsExtern
                    };

                    _currentScope.Define(importedSymbol);
                }
            }
        }

        public void Visit(ConstructorNode node)
        {
            // Enter constructor scope
            EnterScope("Constructor", ScopeKind.Function);

            // Register parameters
            foreach (var param in node.Parameters)
            {
                param.Accept(this);
            }

            // Validate base constructor call if present
            if (node.BaseConstructorArgs.Count > 0)
            {
                foreach (var arg in node.BaseConstructorArgs)
                {
                    arg.Accept(this);
                }
                // TODO: Validate base constructor exists and arguments match
            }

            // Analyze body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            ExitScope();
        }

        public void Visit(PropertyNode node)
        {
            // Validate property type
            TypeInfo propertyType = null;
            if (node.PropertyType != null)
            {
                propertyType = ResolveTypeReference(node.PropertyType);
                if (propertyType == null)
                {
                    Error($"Unknown property type '{node.PropertyType.Name}'", node.Line, node.Column);
                    propertyType = _typeManager.ObjectType;
                }
            }
            else
            {
                // Default to Object type if not specified
                propertyType = _typeManager.ObjectType;
            }

            // Validate ReadOnly/WriteOnly combinations
            if (node.IsReadOnly && node.IsWriteOnly)
            {
                Error($"Property '{node.Name}' cannot be both ReadOnly and WriteOnly", node.Line, node.Column);
            }

            // Validate that ReadOnly properties have only getter
            if (node.IsReadOnly && node.Setter != null)
            {
                Error($"ReadOnly property '{node.Name}' cannot have a setter", node.Line, node.Column);
            }

            // Validate that WriteOnly properties have only setter
            if (node.IsWriteOnly && node.Getter != null)
            {
                Error($"WriteOnly property '{node.Name}' cannot have a getter", node.Line, node.Column);
            }

            // Ensure property has at least one accessor
            if (node.Getter == null && node.Setter == null)
            {
                Error($"Property '{node.Name}' must have at least one accessor (Get or Set)", node.Line, node.Column);
            }

            // Create a symbol for the property
            var symbol = new Symbol(node.Name, SymbolKind.Property, propertyType, node.Line, node.Column);
            symbol.Access = node.Access;

            // Try to define in current scope
            if (!_currentScope.Define(symbol))
            {
                Error($"Property '{node.Name}' is already defined in this scope", node.Line, node.Column);
            }

            SetNodeSymbol(node, symbol);
            SetNodeType(node, propertyType);

            // Set static context if this is a static property
            var wasInStaticContext = _inStaticContext;
            if (node.IsStatic)
            {
                _inStaticContext = true;
            }

            // Analyze getter
            if (node.Getter != null)
            {
                var getterScope = EnterScope($"get_{node.Name}", ScopeKind.Function);
                getterScope.ReturnType = propertyType;

                // Analyze getter body
                node.Getter.Accept(this);

                // Validate that getter returns the correct type
                // Check if all return statements in getter return the property type
                ValidateGetterReturns(node.Getter, propertyType, node.Name, node.Line, node.Column);

                ExitScope();
            }

            // Analyze setter
            if (node.Setter != null)
            {
                var setterScope = EnterScope($"set_{node.Name}", ScopeKind.Function);
                setterScope.ReturnType = _typeManager.VoidType;

                // Register setter parameter (value)
                if (node.SetterParameter != null)
                {
                    node.SetterParameter.Accept(this);

                    // Validate setter parameter type matches property type
                    var paramType = GetNodeType(node.SetterParameter);
                    if (paramType != null && !propertyType.IsAssignableFrom(paramType))
                    {
                        Error($"Setter parameter type '{paramType}' does not match property type '{propertyType}'",
                              node.SetterParameter.Line, node.SetterParameter.Column);
                    }
                }
                else
                {
                    // Create implicit 'value' parameter with the property's type
                    var valueSymbol = new Symbol("value", SymbolKind.Parameter, propertyType, node.Line, node.Column);
                    _currentScope.Define(valueSymbol);
                }

                node.Setter.Accept(this);
                ExitScope();
            }

            // Restore static context
            _inStaticContext = wasInStaticContext;
        }

        /// <summary>
        /// Validate that all return statements in a getter return the correct type
        /// </summary>
        private void ValidateGetterReturns(BlockNode getterBlock, TypeInfo expectedType, string propertyName, int line, int column)
        {
            if (getterBlock == null) return;

            foreach (var statement in getterBlock.Statements)
            {
                if (statement is ReturnStatementNode returnStmt && returnStmt.Value != null)
                {
                    var returnType = GetNodeType(returnStmt.Value);
                    if (returnType != null && !expectedType.IsAssignableFrom(returnType))
                    {
                        Error($"Property '{propertyName}' getter must return type '{expectedType}', but returns '{returnType}'",
                              returnStmt.Line, returnStmt.Column);
                    }
                }
                else if (statement is BlockNode nestedBlock)
                {
                    ValidateGetterReturns(nestedBlock, expectedType, propertyName, line, column);
                }
                else if (statement is IfStatementNode ifStmt)
                {
                    ValidateGetterReturns(ifStmt.ThenBlock, expectedType, propertyName, line, column);
                    if (ifStmt.ElseBlock != null)
                        ValidateGetterReturns(ifStmt.ElseBlock, expectedType, propertyName, line, column);
                }
            }
        }

        public void Visit(MyBaseExpressionNode node)
        {
            // MyBase refers to the base class
            // Validate we're inside a class that has a base class
            var classScope = _currentScope.GetClassScope();
            if (classScope == null)
            {
                Error("MyBase can only be used within a class", node.Line, node.Column);
                return;
            }

            var classType = classScope.ClassType;
            if (classType == null || classType.BaseType == null)
            {
                Error("MyBase can only be used in a class that inherits from another class", node.Line, node.Column);
            }

            // Set node type to base class type
            if (classType?.BaseType != null)
            {
                _nodeTypes[node] = classType.BaseType;
            }
        }

        public void Visit(LambdaExpressionNode node)
        {
            // Track captured variables for closure generation
            var capturedVariables = new List<Symbol>();
            var lambdaScope = EnterScope("Lambda", ScopeKind.Function);

            // Register parameters in the lambda scope and analyze their types
            foreach (var param in node.Parameters)
            {
                var paramType = param.Type != null
                    ? ResolveTypeReference(param.Type)
                    : _typeManager.GetType("Object");
                var paramSymbol = new Symbol(param.Name, SymbolKind.Parameter, paramType, param.Line, param.Column);
                _currentScope.Define(paramSymbol);
                SetNodeSymbol(param, paramSymbol);
                SetNodeType(param, paramType);
            }

            // Analyze the body to discover captured variables
            TypeInfo bodyType = null;
            if (node.Body != null)
            {
                node.Body.Accept(this);
                bodyType = GetNodeType(node.Body) ?? _typeManager.GetType("Object");
            }
            else if (node.StatementBody != null)
            {
                lambdaScope.ReturnType = node.ReturnType != null
                    ? ResolveTypeReference(node.ReturnType)
                    : _typeManager.GetType("Void");
                node.StatementBody.Accept(this);
                bodyType = lambdaScope.ReturnType;
            }

            // Find captured variables (variables from outer scopes used in lambda)
            // This happens automatically through scope resolution - variables resolved
            // from parent scopes are captured variables

            // Determine the delegate type
            TypeInfo delegateType;
            if (node.IsFunction)
            {
                // Function lambda - returns a value
                var returnType = node.ReturnType != null
                    ? ResolveTypeReference(node.ReturnType)
                    : bodyType ?? _typeManager.GetType("Object");

                if (node.Parameters.Count == 0)
                {
                    delegateType = new TypeInfo($"Func<{returnType.Name}>", TypeKind.Delegate);
                }
                else
                {
                    var paramTypes = string.Join(", ", node.Parameters.Select(p =>
                        p.Type != null ? ResolveTypeReference(p.Type).Name : "object"));
                    delegateType = new TypeInfo($"Func<{paramTypes}, {returnType.Name}>", TypeKind.Delegate);
                }
            }
            else
            {
                // Sub lambda - returns void
                if (node.Parameters.Count == 0)
                {
                    delegateType = new TypeInfo("Action", TypeKind.Delegate);
                }
                else
                {
                    var paramTypes = string.Join(", ", node.Parameters.Select(p =>
                        p.Type != null ? ResolveTypeReference(p.Type).Name : "object"));
                    delegateType = new TypeInfo($"Action<{paramTypes}>", TypeKind.Delegate);
                }
            }

            SetNodeType(node, delegateType);
            ExitScope();
        }

        public void Visit(CollectionInitializerNode node)
        {
            TypeInfo commonType = null;

            // Analyze each element
            foreach (var element in node.Elements)
            {
                element.Accept(this);
                var elementType = GetNodeType(element);

                if (elementType != null)
                {
                    if (commonType == null)
                    {
                        commonType = elementType;
                    }
                    else if (!commonType.Equals(elementType))
                    {
                        // For mixed types, use Object
                        Warning($"Collection contains mixed types: {commonType.Name} and {elementType.Name}", node.Line, node.Column);
                        commonType = _typeManager.GetType("Object");
                    }
                }
            }

            // Set the type to array of the common element type
            var arrayType = new TypeInfo($"{(commonType?.Name ?? "Object")}[]", TypeKind.Array);
            arrayType.ElementType = commonType ?? _typeManager.GetType("Object");
            arrayType.ArrayRank = 1;  // Collection initializers create 1D arrays
            SetNodeType(node, arrayType);
        }

        public void Visit(TupleLiteralNode node)
        {
            var elementTypes = new List<TypeInfo>();

            // Analyze each element
            foreach (var element in node.Elements)
            {
                element.Accept(this);
                var elementType = GetNodeType(element) ?? _typeManager.GetType("Object");
                elementTypes.Add(elementType);
            }

            // Create tuple type
            var typeNames = string.Join(", ", elementTypes.Select(t => t.Name));
            var tupleType = new TypeInfo($"({typeNames})", TypeKind.Tuple);
            tupleType.TupleElementTypes = elementTypes;
            tupleType.TupleElementNames = node.ElementNames.ToList();
            SetNodeType(node, tupleType);
        }

        public void Visit(OperatorDeclarationNode node)
        {
            // Operators must be Shared/Static
            if (!node.IsShared)
            {
                Warning("Operators must be Shared (static)", node.Line, node.Column);
            }

            // Resolve the return type
            var returnType = ResolveTypeReference(node.ReturnType);

            // Create a scope for the operator body
            var operatorScope = EnterScope($"Operator {node.OperatorSymbol}", ScopeKind.Function);
            operatorScope.ReturnType = returnType;

            // Register parameters
            foreach (var param in node.Parameters)
            {
                var paramType = ResolveTypeReference(param.Type);
                var paramSymbol = new Symbol(param.Name, SymbolKind.Parameter, paramType, param.Line, param.Column);
                _currentScope.Define(paramSymbol);
            }

            // Analyze the body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            ExitScope();
        }

        public void Visit(EventDeclarationNode node)
        {
            // Resolve the event type (delegate type)
            var eventType = node.EventType != null ? ResolveTypeReference(node.EventType) : _typeManager.GetType("EventHandler");

            // Register the event as a symbol
            var eventSymbol = new Symbol(node.Name, SymbolKind.Event, eventType, node.Line, node.Column);
            _currentScope.Define(eventSymbol);
        }

        public void Visit(RaiseEventStatementNode node)
        {
            // Look up the event
            var eventSymbol = _currentScope.Resolve(node.EventName);
            if (eventSymbol == null)
            {
                Error($"Event '{node.EventName}' is not defined", node.Line, node.Column);
            }

            // Analyze arguments
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
            }
        }

        public void Visit(AddHandlerStatementNode node)
        {
            // Analyze the event expression
            node.EventExpression?.Accept(this);

            // Analyze the handler expression
            node.HandlerExpression?.Accept(this);
        }

        public void Visit(RemoveHandlerStatementNode node)
        {
            // Analyze the event expression
            node.EventExpression?.Accept(this);

            // Analyze the handler expression
            node.HandlerExpression?.Accept(this);
        }

        public void Visit(TypePatternNode node)
        {
            // Resolve the match type
            if (node.MatchType != null)
            {
                var resolvedType = ResolveTypeReference(node.MatchType);
                if (resolvedType == null)
                {
                    Error($"Unknown type '{node.MatchType.Name}' in type pattern", node.Line, node.Column);
                }
            }

            // If there's a variable binding, add it to scope
            if (!string.IsNullOrEmpty(node.VariableName) && node.MatchType != null)
            {
                var resolvedType = ResolveTypeReference(node.MatchType);
                if (resolvedType != null)
                {
                    var symbol = new Symbol(node.VariableName, SymbolKind.Variable, resolvedType, node.Line, node.Column);
                    _currentScope.Define(symbol);
                }
            }

            // Analyze When guard if present
            node.WhenGuard?.Accept(this);
        }

        public void Visit(ConstantPatternNode node)
        {
            // Analyze the constant value
            node.Value?.Accept(this);

            // Analyze When guard if present
            node.WhenGuard?.Accept(this);
        }

        public void Visit(RangePatternNode node)
        {
            // Analyze bounds
            node.LowerBound?.Accept(this);
            node.UpperBound?.Accept(this);

            // Verify bounds are comparable
            var lowerType = GetNodeType(node.LowerBound);
            var upperType = GetNodeType(node.UpperBound);

            if (lowerType != null && upperType != null)
            {
                if (!lowerType.IsNumeric() || !upperType.IsNumeric())
                {
                    Warning("Range pattern bounds should be numeric types", node.Line, node.Column);
                }
            }

            // Analyze When guard if present
            node.WhenGuard?.Accept(this);
        }

        public void Visit(ComparisonPatternNode node)
        {
            // Analyze the comparison value
            node.Value?.Accept(this);

            // Validate operator
            var validOperators = new[] { ">", "<", ">=", "<=", "=", "<>" };
            if (!validOperators.Contains(node.Operator))
            {
                Error($"Invalid comparison operator '{node.Operator}' in pattern", node.Line, node.Column);
            }

            // Analyze When guard if present
            node.WhenGuard?.Accept(this);
        }

        public void Visit(AwaitExpressionNode node)
        {
            // Check that we're inside an async function
            var funcScope = _currentScope.GetFunctionScope();
            // Note: In a full implementation, we would check if the function has IsAsync set
            // For now, just analyze the expression

            node.Expression?.Accept(this);

            // The expression should return a Task-like type
            // For now, we'll trust the programmer
        }

        public void Visit(YieldStatementNode node)
        {
            // Check that we're inside an iterator function
            var funcScope = _currentScope.GetFunctionScope();
            if (funcScope == null)
            {
                Error("Yield statement must be inside a function", node.Line, node.Column);
            }

            // Analyze the value if not a yield break
            if (!node.IsBreak && node.Value != null)
            {
                node.Value.Accept(this);
            }
        }

        public void Visit(LinqQueryExpressionNode node)
        {
            // Create a new scope for LINQ query to manage range variables
            EnterScope("LinqQuery", ScopeKind.Block);

            TypeInfo currentResultType = _typeManager.ObjectType;

            // Analyze each clause
            foreach (var clause in node.Clauses)
            {
                switch (clause)
                {
                    case FromClause from:
                        from.Collection?.Accept(this);
                        var collectionType = GetNodeType(from.Collection);

                        // Try to infer element type from collection
                        TypeInfo elementType = _typeManager.ObjectType;
                        if (collectionType?.Kind == TypeKind.Array)
                        {
                            elementType = collectionType.ElementType ?? _typeManager.ObjectType;
                        }

                        // Define the range variable with inferred type
                        var symbol = new Symbol(from.VariableName, SymbolKind.Variable, elementType, from.Line, from.Column);
                        _currentScope.Define(symbol);
                        currentResultType = elementType;
                        break;

                    case WhereClause where:
                        where.Condition?.Accept(this);
                        var condType = GetNodeType(where.Condition);
                        if (condType != null && !condType.Equals(_typeManager.BooleanType))
                        {
                            Warning($"Where condition should be Boolean, got '{condType}'", where.Line, where.Column);
                        }
                        break;

                    case SelectClause select:
                        select.Selector?.Accept(this);
                        currentResultType = GetNodeType(select.Selector) ?? _typeManager.ObjectType;
                        break;

                    case OrderByClause orderBy:
                        orderBy.KeySelector?.Accept(this);
                        break;

                    case GroupByClause groupBy:
                        groupBy.KeySelector?.Accept(this);
                        if (groupBy.ElementSelector != null)
                        {
                            groupBy.ElementSelector.Accept(this);
                        }

                        // If there's an Into variable, define it in scope
                        if (!string.IsNullOrEmpty(groupBy.IntoVariable))
                        {
                            // The into variable represents IGrouping<TKey, TElement>
                            var groupingType = _typeManager.ObjectType;  // Simplified
                            var intoSymbol = new Symbol(groupBy.IntoVariable, SymbolKind.Variable, groupingType, groupBy.Line, groupBy.Column);
                            _currentScope.Define(intoSymbol);
                        }
                        break;

                    case JoinClause join:
                        join.Collection?.Accept(this);
                        join.OuterKeySelector?.Accept(this);
                        join.InnerKeySelector?.Accept(this);

                        // Define the join range variable
                        var joinCollectionType = GetNodeType(join.Collection);
                        TypeInfo joinElementType = _typeManager.ObjectType;
                        if (joinCollectionType?.Kind == TypeKind.Array)
                        {
                            joinElementType = joinCollectionType.ElementType ?? _typeManager.ObjectType;
                        }

                        var joinSymbol = new Symbol(join.VariableName, SymbolKind.Variable, joinElementType, join.Line, join.Column);
                        _currentScope.Define(joinSymbol);

                        // Validate key types match
                        var outerKeyType = GetNodeType(join.OuterKeySelector);
                        var innerKeyType = GetNodeType(join.InnerKeySelector);
                        if (outerKeyType != null && innerKeyType != null && !_typeManager.AreCompatible(outerKeyType, innerKeyType))
                        {
                            Warning($"Join key types '{outerKeyType}' and '{innerKeyType}' are not compatible", join.Line, join.Column);
                        }

                        // If there's an Into variable, it's a group join
                        if (!string.IsNullOrEmpty(join.IntoVariable))
                        {
                            var groupJoinType = _typeManager.ObjectType;  // Simplified: should be IEnumerable<joinElementType>
                            var intoSymbol = new Symbol(join.IntoVariable, SymbolKind.Variable, groupJoinType, join.Line, join.Column);
                            _currentScope.Define(intoSymbol);
                        }
                        break;

                    case AggregateClause aggregate:
                        aggregate.Collection?.Accept(this);
                        if (aggregate.Selector != null)
                        {
                            aggregate.Selector.Accept(this);
                        }

                        var aggCollectionType = GetNodeType(aggregate.Collection);
                        TypeInfo aggElementType = _typeManager.ObjectType;
                        if (aggCollectionType?.Kind == TypeKind.Array)
                        {
                            aggElementType = aggCollectionType.ElementType ?? _typeManager.ObjectType;
                        }

                        var aggSymbol = new Symbol(aggregate.VariableName, SymbolKind.Variable, aggElementType, aggregate.Line, aggregate.Column);
                        _currentScope.Define(aggSymbol);

                        if (!string.IsNullOrEmpty(aggregate.IntoVariable))
                        {
                            var intoSymbol = new Symbol(aggregate.IntoVariable, SymbolKind.Variable, _typeManager.ObjectType, aggregate.Line, aggregate.Column);
                            _currentScope.Define(intoSymbol);
                        }
                        break;

                    case LetClause let:
                        let.Value?.Accept(this);
                        var letType = GetNodeType(let.Value) ?? _typeManager.ObjectType;
                        var letSymbol = new Symbol(let.VariableName, SymbolKind.Variable, letType, let.Line, let.Column);
                        _currentScope.Define(letSymbol);
                        break;

                    case TakeClause take:
                        take.Count?.Accept(this);
                        var takeCountType = GetNodeType(take.Count);
                        if (takeCountType != null && !takeCountType.IsIntegral())
                        {
                            Error($"Take count must be an integer type, got '{takeCountType}'", take.Line, take.Column);
                        }
                        break;

                    case SkipClause skip:
                        skip.Count?.Accept(this);
                        var skipCountType = GetNodeType(skip.Count);
                        if (skipCountType != null && !skipCountType.IsIntegral())
                        {
                            Error($"Skip count must be an integer type, got '{skipCountType}'", skip.Line, skip.Column);
                        }
                        break;

                    case DistinctClause distinct:
                        // No additional validation needed
                        break;
                }
            }

            // Set the result type of the query expression (usually IEnumerable<T>)
            // For simplification, we use an array type
            var resultArrayType = new TypeInfo($"{currentResultType.Name}[]", TypeKind.Array);
            resultArrayType.ElementType = currentResultType;
            SetNodeType(node, resultArrayType);

            ExitScope();
        }

        public void Visit(InlineCodeNode node)
        {
            // Validate the language is one of the supported targets
            var supportedLanguages = new[] { "csharp", "cpp", "llvm", "msil" };
            if (!supportedLanguages.Contains(node.Language.ToLower()))
            {
                Error($"Unsupported inline code language '{node.Language}'. Supported languages: {string.Join(", ", supportedLanguages)}", node.Line, node.Column);
            }

            // Note: The actual code content is not validated during semantic analysis.
            // It will be passed through to the appropriate code generator.
            // The code generator may perform additional validation or simply emit the code as-is.
        }

        // ====================================================================
        // Preprocessor Directives
        // ====================================================================

        public void Visit(PreprocessorDefineNode node)
        {
            // Register the preprocessor symbol in the preprocessor context
            // This is typically handled during a preprocessing phase before parsing
            // For now, just validate the name
            if (string.IsNullOrEmpty(node.Name))
            {
                Error("Preprocessor #Define requires a symbol name", node.Line, node.Column);
            }
        }

        public void Visit(PreprocessorUndefineNode node)
        {
            if (string.IsNullOrEmpty(node.Name))
            {
                Error("Preprocessor #Undefine requires a symbol name", node.Line, node.Column);
            }
        }

        public void Visit(PreprocessorIfNode node)
        {
            // Evaluate the condition at compile time
            // The condition should reference defined preprocessor symbols
            node.Condition?.Accept(this);

            // Analyze the then body
            foreach (var stmt in node.ThenBody)
            {
                stmt.Accept(this);
            }

            // Analyze else-if clauses
            foreach (var elseIf in node.ElseIfClauses)
            {
                elseIf.Condition?.Accept(this);
                foreach (var stmt in elseIf.Body)
                {
                    stmt.Accept(this);
                }
            }

            // Analyze else body
            foreach (var stmt in node.ElseBody)
            {
                stmt.Accept(this);
            }
        }

        public void Visit(PreprocessorIncludeNode node)
        {
            if (string.IsNullOrEmpty(node.FilePath))
            {
                Error("Preprocessor #Include requires a file path", node.Line, node.Column);
            }
        }

        public void Visit(PreprocessorConstNode node)
        {
            if (string.IsNullOrEmpty(node.Name))
            {
                Error("Preprocessor #Const requires a constant name", node.Line, node.Column);
            }

            node.Value?.Accept(this);

            // The constant should evaluate to a compile-time constant value
        }

        public void Visit(PreprocessorRegionNode node)
        {
            // Regions are purely organizational - just analyze the body
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
        }

        public void Visit(DeclareNode node)
        {
            // Validate the declare statement
            if (string.IsNullOrEmpty(node.Name))
            {
                Error("Declare statement requires a function/sub name", node.Line, node.Column);
                return;
            }

            if (string.IsNullOrEmpty(node.LibraryName))
            {
                Error("Declare statement requires a Lib clause specifying the library name", node.Line, node.Column);
            }

            // Resolve parameter types
            foreach (var param in node.Parameters)
            {
                param.Accept(this);
            }

            // Resolve return type
            TypeInfo returnType = _typeManager.VoidType;
            if (node.IsFunction && node.ReturnType != null)
            {
                returnType = ResolveTypeReference(node.ReturnType);
            }

            // Register the declared function in the current scope
            var symbol = new Symbol(node.Name, node.IsFunction ? SymbolKind.Function : SymbolKind.Subroutine, returnType, node.Line, node.Column)
            {
                IsExtern = true,
                ReturnType = returnType
            };

            // Add parameters to the symbol
            foreach (var param in node.Parameters)
            {
                var paramType = ResolveTypeReference(param.Type);
                var paramSymbol = new Symbol(param.Name, SymbolKind.Parameter, paramType, param.Line, param.Column)
                {
                    IsByRef = param.IsByRef,
                    IsOptional = param.IsOptional
                };
                symbol.Parameters.Add(paramSymbol);
            }

            if (!_currentScope.Define(symbol))
            {
                Error($"Duplicate declaration of '{node.Name}'", node.Line, node.Column);
            }
        }

        // ====================================================================
        // Statements
        // ====================================================================

        public void Visit(BlockNode node)
        {
            EnterScope("Block", ScopeKind.Block);

            foreach (var statement in node.Statements)
            {
                statement.Accept(this);
            }

            ExitScope();
        }

        public void Visit(IfStatementNode node)
        {
            // Check condition
            node.Condition.Accept(this);
            var condType = GetNodeType(node.Condition);

            if (condType != null && !condType.Equals(_typeManager.BooleanType))
            {
                Warning($"Condition should be Boolean, got '{condType}'", node.Line, node.Column);
            }

            // Check branches
            node.ThenBlock.Accept(this);

            foreach (var (condition, block) in node.ElseIfClauses)
            {
                condition.Accept(this);
                var elseIfCondType = GetNodeType(condition);
                if (elseIfCondType != null && !elseIfCondType.Equals(_typeManager.BooleanType))
                {
                    Warning($"ElseIf condition should be Boolean, got '{elseIfCondType}'", node.Line, node.Column);
                }
                block.Accept(this);
            }

            if (node.ElseBlock != null)
            {
                node.ElseBlock.Accept(this);
            }
        }

        public void Visit(SelectStatementNode node)
        {
            node.Expression.Accept(this);
            var exprType = GetNodeType(node.Expression);

            foreach (var caseClause in node.Cases)
            {
                caseClause.Accept(this);

                // Check case values are compatible with expression
                foreach (var value in caseClause.Values)
                {
                    var valueType = GetNodeType(value);
                    if (valueType != null && exprType != null &&
                        !_typeManager.AreCompatible(exprType, valueType))
                    {
                        Error($"Case value type '{valueType}' is not compatible with select expression type '{exprType}'",
                              value.Line, value.Column);
                    }
                }
            }
        }

        public void Visit(CaseClauseNode node)
        {
            foreach (var value in node.Values)
            {
                value.Accept(this);
            }

            node.Body.Accept(this);
        }

        public void Visit(ForLoopNode node)
        {
            // Define loop variable
            var loopVarSymbol = new Symbol(node.Variable, SymbolKind.Variable,
                                          _typeManager.IntegerType, node.Line, node.Column);

            EnterScope("ForLoop", ScopeKind.Loop);

            if (!_currentScope.Define(loopVarSymbol))
            {
                Error($"Loop variable '{node.Variable}' conflicts with existing symbol", node.Line, node.Column);
            }

            // Check start, end, and step expressions
            node.Start.Accept(this);
            var startType = GetNodeType(node.Start);
            if (startType != null && !startType.IsNumeric())
            {
                Error($"For loop start value must be numeric, got '{startType}'", node.Line, node.Column);
            }

            node.End.Accept(this);
            var endType = GetNodeType(node.End);
            if (endType != null && !endType.IsNumeric())
            {
                Error($"For loop end value must be numeric, got '{endType}'", node.Line, node.Column);
            }

            if (node.Step != null)
            {
                node.Step.Accept(this);
                var stepType = GetNodeType(node.Step);
                if (stepType != null && !stepType.IsNumeric())
                {
                    Error($"For loop step value must be numeric, got '{stepType}'", node.Line, node.Column);
                }
            }

            node.Body.Accept(this);

            ExitScope();
        }

        public void Visit(ForEachLoopNode node)
        {
            // Check collection
            node.Collection.Accept(this);
            var collectionType = GetNodeType(node.Collection);

            TypeInfo elementType = ResolveTypeReference(node.VariableType);

            if (collectionType != null)
            {
                if (collectionType.Kind == TypeKind.Array)
                {
                    if (!elementType.IsAssignableFrom(collectionType.ElementType))
                    {
                        Error($"Cannot assign array element type '{collectionType.ElementType}' to loop variable of type '{elementType}'",
                              node.Line, node.Column);
                    }
                }
                else
                {
                    // Check if type is enumerable (has appropriate members)
                    // For now, just check if it's an array
                    Warning($"For Each requires an array or enumerable collection", node.Line, node.Column);
                }
            }

            EnterScope("ForEachLoop", ScopeKind.Loop);

            var loopVarSymbol = new Symbol(node.Variable, SymbolKind.Variable, elementType, node.Line, node.Column);
            if (!_currentScope.Define(loopVarSymbol))
            {
                Error($"Loop variable '{node.Variable}' conflicts with existing symbol", node.Line, node.Column);
            }

            node.Body.Accept(this);

            ExitScope();
        }

        // Stack of With object types for nested With blocks
        private Stack<TypeInfo> _withObjectTypes = new Stack<TypeInfo>();

        public void Visit(WithStatementNode node)
        {
            // Analyze the object expression
            node.Object.Accept(this);
            var objectType = GetNodeType(node.Object);

            // Create a scope for the With block where member access can be implicit
            EnterScope("WithBlock", ScopeKind.Block);

            // Push the With object type for implicit member access resolution
            _withObjectTypes.Push(objectType);

            node.Body.Accept(this);

            _withObjectTypes.Pop();
            ExitScope();
        }

        public void Visit(ImplicitWithMemberNode node)
        {
            if (_withObjectTypes.Count == 0)
            {
                Error("Implicit member access (.) is only valid inside a With block", node.Line, node.Column);
                SetNodeType(node, _typeManager.ObjectType);
                return;
            }

            var withType = _withObjectTypes.Peek();
            if (withType == null)
            {
                SetNodeType(node, _typeManager.ObjectType);
                return;
            }

            // Look up the member in the With object type
            if (withType.Members.TryGetValue(node.MemberName, out var memberSymbol))
            {
                SetNodeType(node, memberSymbol.Type ?? memberSymbol.ReturnType ?? _typeManager.ObjectType);
            }
            else
            {
                Error($"Type '{withType.Name}' does not have a member '{node.MemberName}'", node.Line, node.Column);
                SetNodeType(node, _typeManager.ObjectType);
            }
        }

        public void Visit(WhileLoopNode node)
        {
            node.Condition.Accept(this);
            var condType = GetNodeType(node.Condition);

            if (condType != null && !condType.Equals(_typeManager.BooleanType))
            {
                Warning($"While condition should be Boolean, got '{condType}'", node.Line, node.Column);
            }

            EnterScope("WhileLoop", ScopeKind.Loop);
            node.Body.Accept(this);
            ExitScope();
        }

        public void Visit(DoLoopNode node)
        {
            EnterScope("DoLoop", ScopeKind.Loop);
            node.Body.Accept(this);
            ExitScope();

            if (node.Condition != null)
            {
                node.Condition.Accept(this);
                var condType = GetNodeType(node.Condition);

                if (condType != null && !condType.Equals(_typeManager.BooleanType))
                {
                    Warning($"Loop condition should be Boolean, got '{condType}'", node.Line, node.Column);
                }
            }
        }

        public void Visit(TryStatementNode node)
        {
            node.TryBlock.Accept(this);

            foreach (var catchClause in node.CatchClauses)
            {
                catchClause.Accept(this);
            }
        }

        public void Visit(CatchClauseNode node)
        {
            var exceptionType = ResolveTypeReference(node.ExceptionType);

            EnterScope("Catch", ScopeKind.Block);

            if (!string.IsNullOrEmpty(node.ExceptionVariable))
            {
                var exSymbol = new Symbol(node.ExceptionVariable, SymbolKind.Variable,
                                         exceptionType, node.Line, node.Column);
                _currentScope.Define(exSymbol);
            }

            node.Body.Accept(this);

            ExitScope();
        }

        public void Visit(ThrowStatementNode node)
        {
            if (node.Exception != null)
            {
                node.Exception.Accept(this);
            }
        }

        public void Visit(ReturnStatementNode node)
        {
            var functionScope = _currentScope.GetFunctionScope();
            if (functionScope == null)
            {
                Error("Return statement outside of function", node.Line, node.Column);
                return;
            }

            if (node.Value != null)
            {
                node.Value.Accept(this);
                var returnType = GetNodeType(node.Value);

                if (functionScope.ReturnType.Equals(_typeManager.VoidType))
                {
                    Error("Cannot return a value from a subroutine", node.Line, node.Column);
                }
                // Allow returning any type when the expected type is a type parameter (generics)
                else if (functionScope.ReturnType.Kind == TypeKind.TypeParameter ||
                         returnType?.Kind == TypeKind.TypeParameter)
                {
                    // Type parameters are checked at instantiation time
                }
                else if (!functionScope.ReturnType.IsAssignableFrom(returnType))
                {
                    Error($"Cannot return type '{returnType}' from function expecting '{functionScope.ReturnType}'",
                          node.Line, node.Column);
                }
            }
            else
            {
                if (!functionScope.ReturnType.Equals(_typeManager.VoidType))
                {
                    Error($"Function must return a value of type '{functionScope.ReturnType}'",
                          node.Line, node.Column);
                }
            }
        }

        public void Visit(ExitStatementNode node)
        {
            // Exit Sub/Function should be inside a sub/function
            if (node.Kind == ExitKind.Sub || node.Kind == ExitKind.Function)
            {
                var functionScope = _currentScope.GetFunctionScope();
                if (functionScope == null)
                {
                    Error($"Exit {node.Kind} outside of {node.Kind.ToString().ToLower()}", node.Line, node.Column);
                }
            }
            // Exit For/Do/While should be inside a loop - we'll validate at IR generation or let C# handle it
        }

        public void Visit(AssignmentStatementNode node)
        {
            node.Target.Accept(this);
            node.Value.Accept(this);

            var targetType = GetNodeType(node.Target);
            var valueType = GetNodeType(node.Value);

            if (targetType == null || valueType == null)
                return;

            // Check if target is assignable
            if (node.Target is IdentifierExpressionNode idExpr)
            {
                var symbol = _currentScope.Resolve(idExpr.Name);
                if (symbol != null && symbol.IsConstant)
                {
                    Error($"Cannot assign to constant '{idExpr.Name}'", node.Line, node.Column);
                }
            }

            // Check type compatibility
            if (node.Operator == "=")
            {
                // Allow assignment when either type is a type parameter (generics)
                if (targetType.Kind == TypeKind.TypeParameter || valueType.Kind == TypeKind.TypeParameter)
                {
                    // Type checking deferred to instantiation time
                }
                else if (!targetType.IsAssignableFrom(valueType))
                {
                    Error($"Cannot assign value of type '{valueType}' to '{targetType}'",
                          node.Line, node.Column);
                }
            }
            else // Compound assignment
            {
                // Operators like +=, -= require numeric types (allow type parameters)
                if (!targetType.IsNumeric() && !valueType.IsNumeric() &&
                    targetType.Kind != TypeKind.TypeParameter && valueType.Kind != TypeKind.TypeParameter)
                {
                    Error($"Operator '{node.Operator}' requires numeric operands", node.Line, node.Column);
                }
            }
        }

        public void Visit(ExpressionStatementNode node)
        {
            node.Expression.Accept(this);
        }

        // ====================================================================
        // Expressions
        // ====================================================================

        public void Visit(BinaryExpressionNode node)
        {
            node.Left.Accept(this);
            node.Right.Accept(this);

            var leftType = GetNodeType(node.Left);
            var rightType = GetNodeType(node.Right);

            if (leftType == null || rightType == null)
            {
                SetNodeType(node, _typeManager.ObjectType);
                return;
            }

            TypeInfo resultType;

            switch (node.Operator)
            {
                case "+":
                case "-":
                case "*":
                case "/":
                case "%":
                    // Arithmetic operators
                    if (!leftType.IsNumeric() || !rightType.IsNumeric())
                    {
                        Error($"Arithmetic operator '{node.Operator}' requires numeric operands",
                              node.Line, node.Column);
                        resultType = _typeManager.IntegerType;
                    }
                    else
                    {
                        resultType = _typeManager.GetCommonType(leftType, rightType);
                    }
                    break;

                case "\\":
                    // Integer division
                    if (!leftType.IsIntegral() || !rightType.IsIntegral())
                    {
                        Error($"Integer division requires integral operands", node.Line, node.Column);
                    }
                    resultType = _typeManager.IntegerType;
                    break;

                case "&":
                    // String concatenation
                    if (leftType.Name == "String" || rightType.Name == "String")
                    {
                        resultType = _typeManager.StringType;
                    }
                    else
                    {
                        Error($"Operator '&' requires at least one string operand", node.Line, node.Column);
                        resultType = _typeManager.StringType;
                    }
                    break;

                case "<":
                case "<=":
                case ">":
                case ">=":
                    // Comparison operators - allow type parameters (generics)
                    if (!leftType.IsNumeric() && !rightType.IsNumeric() &&
                        leftType.Kind != TypeKind.TypeParameter && rightType.Kind != TypeKind.TypeParameter)
                    {
                        Error($"Comparison operator '{node.Operator}' requires numeric operands",
                              node.Line, node.Column);
                    }
                    resultType = _typeManager.BooleanType;
                    break;

                case "=":
                case "<>":
                case "!=":
                case "==":
                case "IsEqual":
                    // Equality operators
                    if (!_typeManager.AreCompatible(leftType, rightType) &&
                        !_typeManager.AreCompatible(rightType, leftType))
                    {
                        Warning($"Comparing incompatible types '{leftType}' and '{rightType}'",
                               node.Line, node.Column);
                    }
                    resultType = _typeManager.BooleanType;
                    break;

                case "And":
                case "&&":
                case "Or":
                case "||":
                    // Logical operators
                    if (!leftType.Equals(_typeManager.BooleanType) ||
                        !rightType.Equals(_typeManager.BooleanType))
                    {
                        Error($"Logical operator '{node.Operator}' requires Boolean operands",
                              node.Line, node.Column);
                    }
                    resultType = _typeManager.BooleanType;
                    break;

                default:
                    Error($"Unknown binary operator '{node.Operator}'", node.Line, node.Column);
                    resultType = _typeManager.ObjectType;
                    break;
            }

            SetNodeType(node, resultType);
        }

        public void Visit(UnaryExpressionNode node)
        {
            node.Operand.Accept(this);
            var operandType = GetNodeType(node.Operand);

            if (operandType == null)
            {
                SetNodeType(node, _typeManager.ObjectType);
                return;
            }

            TypeInfo resultType;

            switch (node.Operator)
            {
                case "-":
                case "+":
                    if (!operandType.IsNumeric())
                    {
                        Error($"Unary '{node.Operator}' requires numeric operand", node.Line, node.Column);
                    }
                    resultType = operandType;
                    break;

                case "Not":
                case "!":
                    if (!operandType.Equals(_typeManager.BooleanType))
                    {
                        Error($"Logical NOT requires Boolean operand", node.Line, node.Column);
                    }
                    resultType = _typeManager.BooleanType;
                    break;

                case "++":
                case "--":
                    if (!operandType.IsNumeric())
                    {
                        Error($"Operator '{node.Operator}' requires numeric operand", node.Line, node.Column);
                    }
                    resultType = operandType;
                    break;

                case "^":
                    // Pointer dereference
                    if (!operandType.IsPointer)
                    {
                        Error($"Cannot dereference non-pointer type '{operandType}'", node.Line, node.Column);
                        resultType = _typeManager.ObjectType;
                    }
                    else
                    {
                        resultType = operandType; // Should be base type, but simplified here
                    }
                    break;

                case "AddressOf":
                    resultType = _typeManager.CreatePointerType(operandType);
                    break;

                case "Deref":
                    // Dereference pointer
                    if (!operandType.IsPointer)
                    {
                        Error($"Cannot dereference non-pointer type '{operandType}'", node.Line, node.Column);
                        resultType = _typeManager.ObjectType;
                    }
                    else
                    {
                        // For pointer types, return the element type
                        resultType = operandType.ElementType ?? _typeManager.ObjectType;
                    }
                    break;

                default:
                    Error($"Unknown unary operator '{node.Operator}'", node.Line, node.Column);
                    resultType = _typeManager.ObjectType;
                    break;
            }

            SetNodeType(node, resultType);
        }

        public void Visit(LiteralExpressionNode node)
        {
            TypeInfo type;

            switch (node.LiteralType)
            {
                case TokenType.IntegerLiteral:
                    type = _typeManager.IntegerType;
                    break;
                case TokenType.LongLiteral:
                    type = _typeManager.LongType;
                    break;
                case TokenType.SingleLiteral:
                    type = _typeManager.SingleType;
                    break;
                case TokenType.DoubleLiteral:
                    type = _typeManager.DoubleType;
                    break;
                case TokenType.StringLiteral:
                    type = _typeManager.StringType;
                    break;
                case TokenType.CharLiteral:
                    type = _typeManager.CharType;
                    break;
                case TokenType.BooleanLiteral:
                    type = _typeManager.BooleanType;
                    break;
                default:
                    type = _typeManager.ObjectType;
                    break;
            }

            SetNodeType(node, type);
        }

        public void Visit(InterpolatedStringNode node)
        {
            // Analyze each expression part
            foreach (var part in node.Parts)
            {
                if (part is ExpressionNode expr)
                {
                    expr.Accept(this);
                }
            }

            // Interpolated strings always result in String type
            SetNodeType(node, _typeManager.StringType);
        }

        public void Visit(IdentifierExpressionNode node)
        {
            // Handle 'Me' keyword - refers to current class instance
            if (node.Name.Equals("Me", StringComparison.OrdinalIgnoreCase))
            {
                var classScope = _currentScope.GetClassScope();
                if (classScope == null)
                {
                    Error("'Me' can only be used within a class", node.Line, node.Column);
                    SetNodeType(node, _typeManager.ObjectType);
                }
                else if (_inStaticContext)
                {
                    Error("'Me' cannot be used in a static (Shared) context", node.Line, node.Column);
                    SetNodeType(node, _typeManager.ObjectType);
                }
                else
                {
                    var classType = classScope.ClassType;
                    if (classType != null)
                    {
                        SetNodeType(node, classType);
                    }
                    else
                    {
                        SetNodeType(node, _typeManager.ObjectType);
                    }
                }
                return;
            }

            var symbol = _currentScope.Resolve(node.Name);

            if (symbol == null)
            {
                // Try project symbol table for cross-module references (ModuleName.Symbol or imported symbols)
                symbol = ResolveQualifiedName(node.Name);
            }

            if (symbol == null)
            {
                // Check if this could be a .NET static class (e.g., Console, Math, File)
                if (IsNetType(node.Name))
                {
                    // Treat as a .NET type reference - create synthetic type
                    SetNodeType(node, new TypeInfo(node.Name, TypeKind.Class));
                }
                else
                {
                    // Build detailed error message for debugging
                    var details = new System.Text.StringBuilder();
                    details.Append($"Undefined identifier '{node.Name}'");

                    // Show imported modules
                    if (_importedModules.Count > 0)
                    {
                        details.Append($" [Imported: {string.Join(", ", _importedModules)}]");
                    }
                    else
                    {
                        details.Append(" [No imports]");
                    }

                    // Show available modules in project symbol table
                    if (_projectSymbols != null)
                    {
                        var modules = _projectSymbols.GetModuleNames().ToList();
                        if (modules.Count > 0)
                        {
                            details.Append($" [Available modules: {string.Join(", ", modules)}]");
                        }
                        else
                        {
                            details.Append(" [No modules in project]");
                        }
                    }
                    else
                    {
                        details.Append(" [No project symbol table]");
                    }

                    // Show current module
                    details.Append($" [Current module: {_currentModuleName ?? "none"}]");

                    Error(details.ToString(), node.Line, node.Column);
                    SetNodeType(node, _typeManager.ObjectType);
                }
            }
            else
            {
                SetNodeSymbol(node, symbol);
                SetNodeType(node, symbol.Type);
            }
        }

        public void Visit(MemberAccessExpressionNode node)
        {
            // First, check if this is a module reference (ModuleName.Symbol)
            if (node.Object is IdentifierExpressionNode objId)
            {
                var moduleName = objId.Name;

                // Check ProjectSymbolTable (used by IDE)
                if (_projectSymbols != null && _projectSymbols.HasModule(moduleName))
                {
                    var symbol = _projectSymbols.LookupQualified(moduleName, node.MemberName);
                    if (symbol != null)
                    {
                        SetNodeSymbol(node, symbol);
                        SetNodeType(node, symbol.Type);
                        return;
                    }
                    else
                    {
                        Error($"Module '{moduleName}' does not have a public member '{node.MemberName}'",
                              node.Line, node.Column);
                        SetNodeType(node, _typeManager.ObjectType);
                        return;
                    }
                }

                // Check ModuleRegistry (used by CLI)
                if (_moduleRegistry != null)
                {
                    var unit = FindModuleByName(moduleName);
                    if (unit != null && unit.IsComplete)
                    {
                        var symbol = unit.ExportedSymbols.FirstOrDefault(s =>
                            s.Name.Equals(node.MemberName, StringComparison.OrdinalIgnoreCase));
                        if (symbol != null)
                        {
                            SetNodeSymbol(node, symbol);
                            SetNodeType(node, symbol.Type);
                            return;
                        }
                        else
                        {
                            Error($"Module '{moduleName}' does not have a public member '{node.MemberName}'",
                                  node.Line, node.Column);
                            SetNodeType(node, _typeManager.ObjectType);
                            return;
                        }
                    }
                }

                // Check external libraries
                if (_externalLibraries.TryGetValue(moduleName, out var library))
                {
                    var symbol = library.GetSymbol(node.MemberName);
                    if (symbol != null)
                    {
                        SetNodeSymbol(node, symbol);
                        SetNodeType(node, symbol.Type);
                        return;
                    }
                }
            }

            node.Object.Accept(this);
            var objectType = GetNodeType(node.Object);

            if (objectType == null)
            {
                SetNodeType(node, _typeManager.ObjectType);
                return;
            }

            // Look up member in type
            if (objectType.Members.TryGetValue(node.MemberName, out var memberSymbol))
            {
                SetNodeSymbol(node, memberSymbol);
                SetNodeType(node, memberSymbol.Type);
            }
            else if (IsNetType(objectType.Name))
            {
                // For .NET types, we don't validate member access - trust the user
                // The C# compiler will catch any errors
                SetNodeType(node, _typeManager.ObjectType);
            }
            else
            {
                Error($"Type '{objectType.Name}' does not have a member '{node.MemberName}'",
                      node.Line, node.Column);
                SetNodeType(node, _typeManager.ObjectType);
            }
        }

        public void Visit(CallExpressionNode node)
        {
            node.Callee.Accept(this);

            var calleeType = GetNodeType(node.Callee);
            Symbol calleeSymbol = null;

            if (node.Callee is IdentifierExpressionNode idExpr)
            {
                calleeSymbol = _currentScope.Resolve(idExpr.Name);
                // If not found in local scope, check imported modules from project symbol table
                if (calleeSymbol == null)
                {
                    calleeSymbol = ResolveQualifiedName(idExpr.Name);
                }
            }
            else if (node.Callee is MemberAccessExpressionNode memberExpr)
            {
                calleeSymbol = GetNodeSymbol(memberExpr);
            }

            if (calleeSymbol != null &&
                (calleeSymbol.Kind == SymbolKind.Function || calleeSymbol.Kind == SymbolKind.Subroutine))
            {
                // Count required parameters and check for ParamArray
                int requiredCount = calleeSymbol.Parameters.Count(p => !p.IsOptional && !p.IsParamArray);
                bool hasParamArray = calleeSymbol.Parameters.Any(p => p.IsParamArray);
                int totalParams = calleeSymbol.Parameters.Count;

                // Check argument count
                bool validArgCount = node.Arguments.Count >= requiredCount &&
                                     (hasParamArray || node.Arguments.Count <= totalParams);

                if (!validArgCount)
                {
                    if (hasParamArray)
                    {
                        Error($"Function '{calleeSymbol.Name}' requires at least {requiredCount} arguments, got {node.Arguments.Count}",
                              node.Line, node.Column);
                    }
                    else if (requiredCount == totalParams)
                    {
                        Error($"Function '{calleeSymbol.Name}' expects {totalParams} arguments, got {node.Arguments.Count}",
                              node.Line, node.Column);
                    }
                    else
                    {
                        Error($"Function '{calleeSymbol.Name}' expects {requiredCount} to {totalParams} arguments, got {node.Arguments.Count}",
                              node.Line, node.Column);
                    }
                }
                else
                {
                    // Check argument types
                    for (int i = 0; i < node.Arguments.Count; i++)
                    {
                        node.Arguments[i].Accept(this);
                        var argType = GetNodeType(node.Arguments[i]);

                        TypeInfo paramType;
                        if (i < calleeSymbol.Parameters.Count)
                        {
                            var param = calleeSymbol.Parameters[i];
                            if (param.IsParamArray)
                            {
                                // ParamArray element type (array element type)
                                paramType = param.Type?.ElementType ?? param.Type;
                            }
                            else
                            {
                                paramType = param.Type;
                            }
                        }
                        else if (hasParamArray)
                        {
                            // Extra args go to ParamArray - use element type
                            var paramArrayParam = calleeSymbol.Parameters.Last();
                            paramType = paramArrayParam.Type?.ElementType ?? paramArrayParam.Type;
                        }
                        else
                        {
                            continue;
                        }

                        // Allow any type when the parameter is a type parameter (generics)
                        if (paramType?.Kind == TypeKind.TypeParameter || argType?.Kind == TypeKind.TypeParameter)
                        {
                            // Type checking deferred to instantiation time
                            continue;
                        }

                        if (argType != null && paramType != null && !paramType.IsAssignableFrom(argType))
                        {
                            Error($"Argument {i + 1}: cannot convert from '{argType}' to '{paramType}'",
                                  node.Arguments[i].Line, node.Arguments[i].Column);
                        }
                    }
                }

                SetNodeType(node, calleeSymbol.ReturnType ?? _typeManager.VoidType);
            }
            else
            {
                // Process arguments anyway
                foreach (var arg in node.Arguments)
                {
                    arg.Accept(this);
                }

                SetNodeType(node, _typeManager.ObjectType);
            }
        }

        public void Visit(ArrayAccessExpressionNode node)
        {
            node.Array.Accept(this);
            var arrayType = GetNodeType(node.Array);

            if (arrayType == null)
            {
                SetNodeType(node, _typeManager.ObjectType);
                return;
            }

            // Check indices
            foreach (var index in node.Indices)
            {
                index.Accept(this);
            }

            // Handle actual arrays
            if (arrayType.Kind == TypeKind.Array)
            {
                // Validate integer indices for arrays
                foreach (var index in node.Indices)
                {
                    var indexType = GetNodeType(index);
                    if (indexType != null && !indexType.IsIntegral())
                    {
                        Error($"Array index must be an integer type, got '{indexType}'",
                              index.Line, index.Column);
                    }
                }

                if (node.Indices.Count != arrayType.ArrayRank)
                {
                    Error($"Array expects {arrayType.ArrayRank} indices, got {node.Indices.Count}",
                          node.Line, node.Column);
                }

                SetNodeType(node, arrayType.ElementType ?? _typeManager.ObjectType);
                return;
            }

            // Allow indexing on .NET collection types (List, Dictionary, etc.)
            // These have indexers that the C# compiler will validate
            if (IsNetType(arrayType.Name) || IsIndexableNetType(arrayType.Name))
            {
                // For generic types like List<String>, try to infer the element type
                if (arrayType.GenericArguments != null && arrayType.GenericArguments.Count > 0)
                {
                    // List<T> returns T, Dictionary<K,V> returns V
                    var elementType = arrayType.GenericArguments[arrayType.GenericArguments.Count - 1];
                    SetNodeType(node, elementType);
                }
                else
                {
                    SetNodeType(node, _typeManager.ObjectType);
                }
                return;
            }

            // Not an array or known indexable type
            Error($"Cannot index non-array type '{arrayType}'", node.Line, node.Column);
            SetNodeType(node, _typeManager.ObjectType);
        }

        /// <summary>
        /// Check if a type name is a known .NET collection type that supports indexing
        /// </summary>
        private bool IsIndexableNetType(string typeName)
        {
            // Extract base type name (handle generics like "List<String>" -> "List")
            var baseName = typeName;
            var genericStart = typeName.IndexOf('<');
            if (genericStart > 0)
                baseName = typeName.Substring(0, genericStart);

            // Also handle VB-style generics "List(Of String)" -> "List"
            var ofIndex = typeName.IndexOf("(Of", StringComparison.OrdinalIgnoreCase);
            if (ofIndex > 0)
                baseName = typeName.Substring(0, ofIndex).Trim();

            return baseName switch
            {
                "List" => true,
                "Dictionary" => true,
                "ArrayList" => true,
                "Hashtable" => true,
                "SortedList" => true,
                "SortedDictionary" => true,
                "Collection" => true,
                "ObservableCollection" => true,
                "IList" => true,
                "IDictionary" => true,
                "ICollection" => true,
                _ => false
            };
        }

        public void Visit(NewExpressionNode node)
        {
            var type = ResolveTypeReference(node.Type);

            // Validate: cannot instantiate abstract classes
            if (type != null && type.IsAbstract)
            {
                Error($"Cannot create an instance of abstract class '{type.Name}'", node.Line, node.Column);
            }

            // TODO: Check constructor arguments
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
            }

            SetNodeType(node, type);
        }

        public void Visit(CastExpressionNode node)
        {
            node.Expression.Accept(this);
            var targetType = ResolveTypeReference(node.TargetType);

            SetNodeType(node, targetType);
        }
    }
}