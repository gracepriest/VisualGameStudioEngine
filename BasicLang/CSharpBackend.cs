using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BasicLang.Net;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.StdLib;
using BasicLang.Compiler.StdLib.CSharp;
using BasicLang.Compiler.StdLib.Framework;

namespace BasicLang.Compiler.CodeGen.CSharp
{
    /// <summary>
    /// Improved C# code generator.
    ///
    /// IMPORTANT: This generator intentionally avoids emitting compiler-temporary locals (t0, t1, ...)
    /// by inlining SSA IR values into C# expressions whenever it is safe/possible.
    ///
    /// Rule of thumb:
    /// - If an IR value has a Name that matches a real declared variable (local/param/global), we emit a statement assignment.
    /// - Otherwise, we treat it as an expression-only value and inline it where referenced (return, if-condition, RHS, etc.).
    /// - Calls are emitted as statements if their result is assigned to a declared variable or if the result is otherwise unused.
    /// </summary>
    public class ImprovedCSharpCodeGenerator : IIRVisitor
    {
        private readonly StringBuilder _output;
        private readonly CodeGenOptions _options;
        private int _indentLevel;

        private readonly Dictionary<string, string> _typeMap;
        private readonly HashSet<string> _usings;
        private readonly HashSet<string> _usedNamespaces;

        private IRModule _currentModule;
        private IRFunction _currentFunction;

        // Name mapping and declared identifier tracking
        private readonly Dictionary<IRValue, string> _valueNames;
        private readonly Dictionary<string, string> _variableNameMap; // logical name -> sanitized C# name
        private readonly HashSet<string> _declaredIdentifiers;         // logical names (locals/params/globals)
        private readonly Dictionary<string, IRValue> _tempDefsByName;  // tempName -> defining IRValue (only for non-declared names)

        // Use counts help decide whether to emit calls as statements or inline them into expressions
        private readonly Dictionary<IRValue, int> _useCounts;

        /// <summary>
        /// ADR-0001's declared-local temps for the function being emitted: values used more than
        /// once whose definition is not replicable. See <see cref="ComputeMaterialisedTemps"/>.
        /// </summary>
        private readonly HashSet<IRValue> _materialised = new HashSet<IRValue>();

        /// <summary>The materialised values whose DEFINING text is being written right now.</summary>
        private readonly HashSet<IRValue> _emittingDefinition = new HashSet<IRValue>();

        // For structured control flow generation
        private HashSet<BasicBlock> _processedBlocks;

        /// <summary>
        /// The merge blocks of the Ifs being generated right now. An <c>ElseIf</c> clause is lowered
        /// as a nested conditional (<c>ifN.elseifK.then</c> / <c>ifN.elseifK.else</c>) that branches
        /// to the OUTER If's <c>ifN.end</c>, so its reconstruction finds the same merge block. Only
        /// the outermost If — the first to claim the block — may emit it, after its own braces.
        /// ⛔ MEASURED before: the inner one emitted it inside the outer Else, so every statement
        /// after an ElseIf chain was lost whenever the first branch was taken.
        /// </summary>
        private readonly HashSet<BasicBlock> _pendingIfMerges = new HashSet<BasicBlock>();

        // Stack of loop end blocks for break detection
        private Stack<BasicBlock> _loopEndBlocks;

        /// <summary>
        /// The end blocks of the <c>For Each</c> loops currently OPEN around the text being
        /// written — <see cref="_loopEndBlocks"/>'s counterpart for the one loop shape that is
        /// not emitted through <see cref="GenerateLoop"/>.
        ///
        /// <para>⛔ It cannot be folded into <see cref="_loopEndBlocks"/>, because the two are
        /// asked DIFFERENT questions. A <c>while</c>-shaped loop's body ends by branching to its
        /// CONDITION block, so any branch to its <c>.end</c> is necessarily an exit. A
        /// <c>For Each</c>'s does not: <c>IRBuilder.Visit(ForEachLoopNode)</c> gives the loop
        /// <c>LoopContext(endBlock, endBlock)</c> and then ends the body with
        /// <c>IRBranch(endBlock)</c> — byte-identical to the <c>Exit For</c> branch except for
        /// <c>IRBranch.IsLoopExit</c>. Treating every branch to a <c>For Each</c>'s end as a
        /// <c>break</c> makes an ordinary iteration exit the loop; treating none of them as one
        /// is what this backend did, and <c>Exit For</c> was a silent NO-OP.</para>
        /// </summary>
        private HashSet<BasicBlock> _forEachEndBlocks;

        /// <summary>
        /// Loop-variable renames in force while a <c>For Each</c> body is being emitted: BasicLang
        /// name → the C# name its <c>foreach</c> declared. See <see cref="Visit(IRForEach)"/>.
        /// </summary>
        private readonly Dictionary<string, string> _forEachRenames = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The <c>For Each</c> variables whose bodies are open, outermost first (BasicLang names).</summary>
        private readonly List<string> _openForEachVariables = new();

        /// <summary>Fresh loop-variable names already issued in <see cref="_forEachNamesOwner"/>'s body.</summary>
        private readonly HashSet<string> _issuedForEachNames = new(StringComparer.OrdinalIgnoreCase);
        private IRFunction _forEachNamesOwner;

        /// <summary>How many C# <c>switch</c> statements are open around the text being written.</summary>
        private int _switchDepth;

        /// <summary><see cref="_switchDepth"/> as it stood when each enclosing loop opened.</summary>
        private Stack<int> _loopSwitchDepths;

        /// <summary>
        /// Loop end blocks a <c>goto</c> was emitted for, so the matching label is written after
        /// that loop closes — and ONLY then, since an unreferenced label is CS0164.
        /// </summary>
        private HashSet<BasicBlock> _labelledLoopEnds;

        // Standard library provider for built-in functions
        private readonly CSharpStdLibProvider _stdLib;
        private readonly FrameworkStdLibProvider _frameworkStdLib;

        // #line directive tracking for source-level debugging
        private int _lastEmittedSourceLine = -1;
        private string _lastEmittedSourceFile = null;

        public string GeneratedCode => _output.ToString();

        /// <summary>
        /// Test-only view of the candidate <c>using</c> set THIS INSTANCE actually built —
        /// read after <see cref="Generate"/> has run. Exists so
        /// <c>NetAmbientNamespaceTests.GenerateSeedsEveryAmbientNamespaceIntoItsCandidateUsings</c>
        /// can hold spec §12.4's "the ambient set used by NetTypeResolver ≡ the one used by
        /// CSharpBackend" against what <see cref="Generate"/> DID rather than against the
        /// constant it was supposed to read.
        ///
        /// <para><b>Why it is an instance view and not a static alias.</b> This member used to be
        /// <c>static … =&gt; NetAmbientNamespaces.All</c>, which made the drift test compare the
        /// shared constant with itself: it could not fail under ANY edit to the seeding loop in
        /// <see cref="Generate"/>, including deleting the loop outright. Exposing <c>_usings</c>
        /// instead puts the assertion on the generator's own state, so "the backend reads the
        /// shared constant" becomes a measurable fact.</para>
        ///
        /// <para>The set is a SUPERSET of <see cref="NetAmbientNamespaces.All"/> by design:
        /// <see cref="Generate"/> also adds the program's own <c>Using</c> directives and every
        /// stdlib-required import. Nothing removes from it, and emission filters a separate
        /// <c>_usedNamespaces</c> set — so reading this changes no output. NOTE: despite the file
        /// name (<c>CSharpBackend.cs</c>), the class is <c>ImprovedCSharpCodeGenerator</c> — there
        /// is no type literally named <c>CSharpBackend</c> anywhere in the codebase.</para>
        /// </summary>
        internal IReadOnlyCollection<string> CandidateUsingsForTest => _usings;

        // Names of functions/subs defined by the user program. A user definition
        // shadows a stdlib builtin of the same name (e.g. a user "Run" must call
        // the user's method, not emit the Process.Start builtin).
        private readonly HashSet<string> _userFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void CollectUserFunctionNames(IRModule module)
        {
            _userFunctionNames.Clear();
            foreach (var func in module.Functions)
            {
                if (string.IsNullOrEmpty(func.Name)) continue;
                _userFunctionNames.Add(func.Name);
                // Also record the unqualified last segment (e.g. "M.Run" -> "Run").
                var dot = func.Name.LastIndexOf('.');
                if (dot >= 0 && dot < func.Name.Length - 1)
                    _userFunctionNames.Add(func.Name.Substring(dot + 1));
            }
        }

        // Helper methods to check both stdlib providers (Framework first, then CSharp)
        private bool StdLibCanHandle(string functionName)
        {
            // A user-defined function of the same name shadows the builtin.
            if (functionName != null && _userFunctionNames.Contains(functionName))
                return false;
            return _frameworkStdLib.CanHandle(functionName) || _stdLib.CanHandle(functionName);
        }

        private string StdLibEmitCall(string functionName, string[] arguments)
        {
            if (_frameworkStdLib.CanHandle(functionName))
                return _frameworkStdLib.EmitCall(functionName, arguments);
            return _stdLib.EmitCall(functionName, arguments);
        }

        private IEnumerable<string> StdLibGetRequiredImports(string functionName)
        {
            if (_frameworkStdLib.CanHandle(functionName))
                return _frameworkStdLib.GetRequiredImports(functionName);
            return _stdLib.GetRequiredImports(functionName);
        }

        public ImprovedCSharpCodeGenerator(CodeGenOptions options = null)
        {
            _output = new StringBuilder();
            _options = options ?? new CodeGenOptions();
            _indentLevel = 0;

            _usings = new HashSet<string>();
            _usedNamespaces = new HashSet<string>();
            _valueNames = new Dictionary<IRValue, string>();
            _variableNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _declaredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _tempDefsByName = new Dictionary<string, IRValue>(StringComparer.OrdinalIgnoreCase);
            _useCounts = new Dictionary<IRValue, int>();

            // Initialize standard library providers (Framework first, then CSharp fallback)
            _frameworkStdLib = new FrameworkStdLibProvider();
            _stdLib = new CSharpStdLibProvider();

            // Initialize type mapping
            _typeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Integer", "int" },
                { "Long", "long" },
                { "Single", "float" },
                { "Double", "double" },
                { "String", "string" },
                { "Boolean", "bool" },
                { "Char", "char" },
                { "Void", "void" },
                { "Object", "object" },
                { "Byte", "byte" },
                { "Short", "short" },
                { "SByte", "sbyte" },
                { "UByte", "byte" },
                { "UShort", "ushort" },
                { "UInteger", "uint" },
                { "ULong", "ulong" },
                { "Decimal", "decimal" }
            };

            // Default using
            _usings.Add("System");
        }

        /// <summary>
        /// Generate C# code from IR module
        /// </summary>
        public string Generate(IRModule module)
        {
            _currentModule = module;

            // Backend honesty (spec decision 12): the C# backend rejects the
            // C++-only passthrough features (#CppInclude / :: foreign types / cpp{}
            // inline blocks) but supports collections natively (rejectCollections:
            // false). Its own inline language is "csharp" — a csharp{} block is fine.
            //
            // ⛔ allowForeignIdentifiers stays at its default FALSE. That flag exists for the
            // JavaScript backend, where `::name` is a raw JS global; C# has no such reading, and
            // turning it on here would let a `::` name reach SanitizeName and emit `mathlibfreeAdd`
            // — a compile error at best, and from a build that reported success.
            ForeignFeatureChecker.Check(module, "C#", rejectCollections: false, ownInlineLanguage: "csharp");

            _output.Clear();
            _indentLevel = 0;
            _usings.Clear();

            CollectUserFunctionNames(module);

            // Build the candidate usings set. Shared with NetTypeResolver via
            // NetAmbientNamespaces (spec §6.5) so the C# backend and the native resolver cannot
            // drift apart — see NetAmbientNamespaceTests.CSharpBackendAndResolverShareOneAmbientSet.
            foreach (var ambientNamespace in NetAmbientNamespaces.All)
                _usings.Add(ambientNamespace);

            // Add .NET usings from the source code
            foreach (var netUsing in module.NetUsings)
            {
                _usings.Add(netUsing.Namespace);
            }

            // Pre-scan for stdlib function calls to collect required imports
            CollectStdLibImports(module);

            // Track which namespaces are actually used during code generation
            _usedNamespaces.Clear();
            // System is always needed (basic types, Console, etc.)
            _usedNamespaces.Add("System");
            // Namespaces explicitly imported in source code are always emitted
            foreach (var netUsing in module.NetUsings)
            {
                _usedNamespaces.Add(netUsing.Namespace);
            }

            // Placeholder for usings - will be replaced after code generation
            var usingsPlaceholder = "<<USINGS_PLACEHOLDER>>";
            EmitLineHidden();
            _output.Append(usingsPlaceholder);

            // Group types by namespace
            var classesByNamespace = module.Classes.Values
                .GroupBy(c => c.Namespace ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            var interfacesByNamespace = module.Interfaces.Values
                .GroupBy(i => i.Namespace ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            var enumsByNamespace = module.Enums.Values
                .GroupBy(e => e.Namespace ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            var delegatesByNamespace = module.Delegates.Values
                .GroupBy(d => d.Namespace ?? "")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Get all unique namespaces (source-defined + default)
            var allNamespaces = new HashSet<string> { "" };  // Empty string for default namespace
            foreach (var ns in classesByNamespace.Keys) if (!string.IsNullOrEmpty(ns)) allNamespaces.Add(ns);
            foreach (var ns in interfacesByNamespace.Keys) if (!string.IsNullOrEmpty(ns)) allNamespaces.Add(ns);
            foreach (var ns in enumsByNamespace.Keys) if (!string.IsNullOrEmpty(ns)) allNamespaces.Add(ns);
            foreach (var ns in delegatesByNamespace.Keys) if (!string.IsNullOrEmpty(ns)) allNamespaces.Add(ns);

            // Get standalone functions
            var standaloneFunctions = module.Functions
                .Where(f => !f.IsExternal && !IsClassMethod(f, module))
                .ToList();

            // Generate each namespace block
            foreach (var ns in allNamespaces.OrderBy(n => n))
            {
                // Determine the effective namespace name
                var effectiveNamespace = string.IsNullOrEmpty(ns)
                    ? _options.Namespace
                    : ns;

                WriteLine($"namespace {effectiveNamespace}");
                WriteLine("{");
                Indent();

                // Generate interfaces in this namespace
                if (interfacesByNamespace.TryGetValue(ns, out var interfacesInNs))
                {
                    foreach (var irInterface in interfacesInNs)
                    {
                        GenerateInterface(irInterface);
                        WriteLine();
                    }
                }

                // Generate enums in this namespace
                if (enumsByNamespace.TryGetValue(ns, out var enumsInNs))
                {
                    foreach (var irEnum in enumsInNs)
                    {
                        GenerateEnum(irEnum);
                        WriteLine();
                    }
                }

                // Generate delegates in this namespace
                if (delegatesByNamespace.TryGetValue(ns, out var delegatesInNs))
                {
                    foreach (var irDelegate in delegatesInNs)
                    {
                        GenerateDelegate(irDelegate);
                        WriteLine();
                    }
                }

                // Generate classes in this namespace
                if (classesByNamespace.TryGetValue(ns, out var classesInNs))
                {
                    foreach (var irClass in classesInNs)
                    {
                        GenerateClass(irClass);
                        WriteLine();
                    }
                }

                // Generate module classes for standalone functions (only in default namespace)
                if (string.IsNullOrEmpty(ns) && (standaloneFunctions.Count > 0 || module.GlobalVariables.Count > 0))
                {
                    // Group functions by their source module
                    var functionsByModule = standaloneFunctions
                        .GroupBy(f => f.ModuleName ?? _options.ClassName)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    // Group globals by their source module
                    var globalsByModule = module.GlobalVariables.Values
                        .GroupBy(g => g.ModuleName ?? _options.ClassName)
                        .ToDictionary(g => g.Key, g => g.ToList());

                    // Get all module names
                    var allModules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var m in functionsByModule.Keys) allModules.Add(m);
                    foreach (var m in globalsByModule.Keys) allModules.Add(m);

                    bool hasUserMain = false;

                    // Generate a static class for each module
                    foreach (var moduleName in allModules.OrderBy(m => m))
                    {
                        _currentModuleClass = moduleName;
                        var className = SanitizeName(moduleName);
                        // C# doesn't allow a method with the same name as its enclosing class
                        if (className.Equals("Main", StringComparison.OrdinalIgnoreCase))
                        {
                            className = "Program";
                        }
                        WriteLine($"{_options.ClassAccessModifier} static class {className}");
                        WriteLine("{");
                        Indent();

                        // Constants for this module
                        if (globalsByModule.TryGetValue(moduleName, out var moduleGlobals))
                        {
                            var constants = moduleGlobals.Where(v => v.IsConst).ToList();
                            if (constants.Count > 0)
                            {
                                WriteLine("// Constants");
                                foreach (var constVar in constants)
                                {
                                    var type = MapType(constVar.Type);
                                    var name = SanitizeName(constVar.Name);
                                    var accessMod = ModuleMemberAccess(constVar.Access);
                                    var value = constVar.InitialValue != null ? EmitExpression(constVar.InitialValue) : "default";
                                    WriteLine($"{accessMod} const {type} {name} = {value};");
                                }
                                WriteLine();
                            }

                            // Globals (non-const) for this module
                            var globals = moduleGlobals.Where(v => !v.IsConst).ToList();
                            if (globals.Count > 0)
                            {
                                WriteLine("// Global variables");
                                foreach (var globalVar in globals)
                                {
                                    var type = MapType(globalVar.Type);
                                    var name = SanitizeName(globalVar.Name);
                                    var accessMod = ModuleMemberAccess(globalVar.Access);
                                    if (globalVar.InitialValue != null)
                                    {
                                        var initVal = EmitExpression(globalVar.InitialValue);
                                        WriteLine($"{accessMod} static {type} {name} = {initVal};");
                                    }
                                    else
                                    {
                                        // A module-level fixed-size array allocates here for the
                                        // same reason a local does — left bare it is a null
                                        // reference, and the first `g(0) = …` throws.
                                        var sized = SizedArrayInitializer(globalVar.Type);
                                        WriteLine(sized != null
                                            ? $"{accessMod} static {type} {name} = {sized};"
                                            : $"{accessMod} static {type} {name};");
                                    }
                                }
                                WriteLine();
                            }
                        }

                        // Extern declarations (P/Invoke) - put in first/main module
                        if (moduleName == allModules.First() && module.ExternDeclarations.Count > 0)
                        {
                            WriteLine("// P/Invoke declarations");
                            foreach (var externDecl in module.ExternDeclarations.Values)
                            {
                                GenerateExternDeclaration(externDecl);
                            }
                            WriteLine();
                        }

                        // Functions for this module
                        if (functionsByModule.TryGetValue(moduleName, out var moduleFunctions))
                        {
                            foreach (var function in moduleFunctions)
                            {
                                GenerateFunction(function);
                                WriteLine();

                                if (function.Name.Equals("Main", StringComparison.OrdinalIgnoreCase))
                                    hasUserMain = true;
                            }
                        }

                        Unindent();
                        WriteLine("}");
                        WriteLine();
                        _currentModuleClass = null;
                    }

                    // Optional default Main - generate in a Program class
                    if (_options.GenerateMainMethod && !hasUserMain)
                    {
                        WriteLine($"{_options.ClassAccessModifier} class Program");
                        WriteLine("{");
                        Indent();
                        GenerateMainMethod();
                        Unindent();
                        WriteLine("}");
                    }
                }

                Unindent();
                WriteLine("}");
                WriteLine();
            }

            _currentModule = null;

            // Determine which namespaces are actually used by scanning generated code
            var generatedBody = _output.ToString();
            DetectUsedNamespaces(generatedBody);

            // Build the usings header with only used namespaces
            var usingsBuilder = new StringBuilder();
            foreach (var usingDirective in _usings.Where(u => _usedNamespaces.Contains(u)).OrderBy(u => u))
                usingsBuilder.AppendLine($"using {usingDirective};");

            // Emit aliased usings separately
            foreach (var netUsing in module.NetUsings.Where(u => !string.IsNullOrEmpty(u.Alias)))
            {
                usingsBuilder.AppendLine($"using {netUsing.Alias} = {netUsing.Namespace};");
            }

            usingsBuilder.AppendLine();

            // Replace the placeholder with actual usings
            return generatedBody.Replace("<<USINGS_PLACEHOLDER>>", usingsBuilder.ToString());
        }

        /// <summary>
        /// Scan generated code to detect which namespaces are actually referenced
        /// </summary>
        private void DetectUsedNamespaces(string code)
        {
            // Map of type/keyword patterns to the namespace they require
            var namespaceIndicators = new Dictionary<string, string[]>
            {
                { "List<", new[] { "System.Collections.Generic" } },
                { "Dictionary<", new[] { "System.Collections.Generic" } },
                { "HashSet<", new[] { "System.Collections.Generic" } },
                { "Queue<", new[] { "System.Collections.Generic" } },
                { "Stack<", new[] { "System.Collections.Generic" } },
                { "KeyValuePair<", new[] { "System.Collections.Generic" } },
                { "IEnumerable<", new[] { "System.Collections.Generic" } },
                { "IList<", new[] { "System.Collections.Generic" } },
                { "IDictionary<", new[] { "System.Collections.Generic" } },
                { "ICollection<", new[] { "System.Collections.Generic" } },
                { "IEnumerable", new[] { "System.Collections" } },
                { "ICollection", new[] { "System.Collections" } },
                { "ArrayList", new[] { "System.Collections" } },
                { "Hashtable", new[] { "System.Collections" } },
                { "Task", new[] { "System.Threading.Tasks" } },
                { "async ", new[] { "System.Threading.Tasks" } },
                { "await ", new[] { "System.Threading.Tasks" } },
                { "StringBuilder", new[] { "System.Text" } },
                { "Encoding", new[] { "System.Text" } },
                { "File.", new[] { "System.IO" } },
                { "Directory.", new[] { "System.IO" } },
                { "Path.", new[] { "System.IO" } },
                { "StreamReader", new[] { "System.IO" } },
                { "StreamWriter", new[] { "System.IO" } },
                { ".Select(", new[] { "System.Linq" } },
                { ".Where(", new[] { "System.Linq" } },
                { ".OrderBy(", new[] { "System.Linq" } },
                { ".ToList(", new[] { "System.Linq" } },
                { ".ToArray(", new[] { "System.Linq" } },
                { ".First(", new[] { "System.Linq" } },
                { ".Any(", new[] { "System.Linq" } },
                { ".Count(", new[] { "System.Linq" } },
                { "HttpClient", new[] { "System.Net.Http" } },
                { "HttpResponseMessage", new[] { "System.Net.Http" } },
                { "IPAddress", new[] { "System.Net" } },
                { "Dns.", new[] { "System.Net" } },
                { "TcpClient", new[] { "System.Net.Sockets" } },
                { "TcpListener", new[] { "System.Net.Sockets" } },
                { "Socket", new[] { "System.Net.Sockets" } },
                { "JsonSerializer", new[] { "System.Text.Json" } },
                { "JsonDocument", new[] { "System.Text.Json" } },
                { "JsonNode", new[] { "System.Text.Json.Nodes" } },
                { "JsonObject", new[] { "System.Text.Json.Nodes" } },
                { "JsonArray", new[] { "System.Text.Json.Nodes" } },
                { "Regex", new[] { "System.Text.RegularExpressions" } },
                { "DllImport", new[] { "System.Runtime.InteropServices" } },
                { "Marshal", new[] { "System.Runtime.InteropServices" } },
                { "StructLayout", new[] { "System.Runtime.InteropServices" } },
                { "Process", new[] { "System.Diagnostics" } },
                { "Stopwatch", new[] { "System.Diagnostics" } },
                { "Debug.", new[] { "System.Diagnostics" } },
                { "Thread", new[] { "System.Threading" } },
                { "Monitor", new[] { "System.Threading" } },
                { "Mutex", new[] { "System.Threading" } },
                { "Semaphore", new[] { "System.Threading" } },
                { "FrameworkWrapper.", new[] { "RaylibWrapper" } },
                { "SHA256", new[] { "System.Security.Cryptography" } },
                { "SHA512", new[] { "System.Security.Cryptography" } },
                { "MD5", new[] { "System.Security.Cryptography" } },
                { "Aes", new[] { "System.Security.Cryptography" } },
            };

            foreach (var (indicator, namespaces) in namespaceIndicators)
            {
                if (code.Contains(indicator))
                {
                    foreach (var ns in namespaces)
                        _usedNamespaces.Add(ns);
                }
            }
        }

        /// <summary>
        /// Check if a function belongs to a class
        /// </summary>
        private bool IsClassMethod(IRFunction function, IRModule module)
        {
            foreach (var irClass in module.Classes.Values)
            {
                if (irClass.Methods.Any(m => m.Implementation == function))
                    return true;
                if (irClass.Constructors.Any(c => c.Implementation == function))
                    return true;
                if (irClass.Properties.Any(p => p.Getter == function || p.Setter == function))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Pre-scan the module for stdlib function calls and collect required imports
        /// </summary>
        private void CollectStdLibImports(IRModule module)
        {
            // Scan all functions
            foreach (var function in module.Functions)
            {
                CollectStdLibImportsFromFunction(function);
            }

            // Scan class methods
            foreach (var irClass in module.Classes.Values)
            {
                foreach (var method in irClass.Methods)
                {
                    if (method.Implementation != null)
                        CollectStdLibImportsFromFunction(method.Implementation);
                }
                foreach (var ctor in irClass.Constructors)
                {
                    if (ctor.Implementation != null)
                        CollectStdLibImportsFromFunction(ctor.Implementation);
                }
            }
        }

        private void CollectStdLibImportsFromFunction(IRFunction function)
        {
            foreach (var block in function.Blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    CollectStdLibImportsFromInstruction(instr);
                }
            }
        }

        private void CollectStdLibImportsFromInstruction(IRInstruction instr)
        {
            if (instr is IRCall call && StdLibCanHandle(call.FunctionName))
            {
                foreach (var import in StdLibGetRequiredImports(call.FunctionName))
                {
                    _usings.Add(import);
                }
            }
            else if (instr is IRAssignment assign)
            {
                CollectStdLibImportsFromExpression(assign.Value);
            }
            else if (instr is IRReturn ret && ret.Value != null)
            {
                CollectStdLibImportsFromExpression(ret.Value);
            }
            else if (instr is IRConditionalBranch condBranch)
            {
                CollectStdLibImportsFromExpression(condBranch.Condition);
            }
        }

        private void CollectStdLibImportsFromExpression(IRValue expr)
        {
            if (expr is IRCall call && StdLibCanHandle(call.FunctionName))
            {
                foreach (var import in StdLibGetRequiredImports(call.FunctionName))
                {
                    _usings.Add(import);
                }
                // Also check arguments
                foreach (var arg in call.Arguments)
                {
                    CollectStdLibImportsFromExpression(arg);
                }
            }
            else if (expr is IRBinaryOp binOp)
            {
                CollectStdLibImportsFromExpression(binOp.Left);
                CollectStdLibImportsFromExpression(binOp.Right);
            }
            else if (expr is IRUnaryOp unaryOp)
            {
                CollectStdLibImportsFromExpression(unaryOp.Operand);
            }
        }

        /// <summary>
        /// Generate a C# interface from IRInterface
        /// </summary>
        private void GenerateInterface(IRInterface irInterface)
        {
            var interfaceName = SanitizeName(irInterface.Name);

            // Interface declaration with base interfaces
            var baseList = "";
            if (irInterface.BaseInterfaces.Count > 0)
            {
                baseList = " : " + string.Join(", ", irInterface.BaseInterfaces.Select(SanitizeName));
            }

            WriteLine($"public interface {interfaceName}{baseList}");
            WriteLine("{");
            Indent();

            // Generate method signatures and default implementations
            foreach (var method in irInterface.Methods)
            {
                var returnType = MapType(method.ReturnType);
                var methodName = SanitizeName(method.Name);
                var paramList = string.Join(", ", method.Parameters.Select(FormatIRParameter));

                if (method.HasDefaultImplementation && method.DefaultImplementation != null)
                {
                    // Generate default implementation (C# 8.0+)
                    WriteLine($"{returnType} {methodName}({paramList})");
                    WriteLine("{");
                    Indent();

                    // Generate the default implementation body from IR
                    _currentFunction = method.DefaultImplementation;
                    InitializeFunctionContext(method.DefaultImplementation);
                    _processedBlocks = new HashSet<BasicBlock>();
                    _loopEndBlocks = new Stack<BasicBlock>();
                    ResetLoopExitState();

                    // Declare locals — and any temp materialised under ADR-0001 (see DeclareLocals)
                    DeclareLocals(method.DefaultImplementation, sizedArrays: false);

                    if (method.DefaultImplementation.EntryBlock != null)
                        GenerateStructuredBlock(method.DefaultImplementation.EntryBlock);

                    _currentFunction = null;

                    Unindent();
                    WriteLine("}");
                }
                else
                {
                    // Abstract method signature only
                    WriteLine($"{returnType} {methodName}({paramList});");
                }
            }

            // Generate property signatures
            foreach (var prop in irInterface.Properties)
            {
                var propType = MapType(prop.Type);
                var propName = SanitizeName(prop.Name);
                var accessors = "";
                if (prop.HasGetter) accessors += " get;";
                if (prop.HasSetter) accessors += " set;";
                WriteLine($"{propType} {propName} {{{accessors} }}");
            }

            Unindent();
            WriteLine("}");
        }

        /// <summary>
        /// Generate a C# enum from IREnum
        /// </summary>
        private void GenerateEnum(IREnum irEnum)
        {
            var enumName = SanitizeName(irEnum.Name);

            // Underlying type
            var underlyingType = "";
            if (irEnum.UnderlyingType != null && irEnum.UnderlyingType.Name != "Int32")
            {
                underlyingType = " : " + MapType(irEnum.UnderlyingType);
            }

            WriteLine($"public enum {enumName}{underlyingType}");
            WriteLine("{");
            Indent();

            for (int i = 0; i < irEnum.Members.Count; i++)
            {
                var member = irEnum.Members[i];
                var comma = i < irEnum.Members.Count - 1 ? "," : "";
                // Invariant: IRBuilder stores a Long, and a NEGATIVE one under sv-SE rendered
                // `Back = −1` (U+2212 minus) — CS1056.
                var value = member.Value != null
                    ? " = " + Convert.ToString(member.Value, CultureInfo.InvariantCulture)
                    : "";
                WriteLine($"{SanitizeName(member.Name)}{value}{comma}");
            }

            Unindent();
            WriteLine("}");
        }

        /// <summary>
        /// Generate a C# delegate from IRDelegate
        /// </summary>
        private void GenerateDelegate(IRDelegate irDelegate)
        {
            var delegateName = SanitizeName(irDelegate.Name);
            var returnType = MapType(irDelegate.ReturnType);
            var paramList = string.Join(", ", irDelegate.Parameters.Select(FormatIRParameter));

            WriteLine($"public delegate {returnType} {delegateName}({paramList});");
        }

        /// <summary>
        /// Map a type name string to C# type
        /// </summary>
        private string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "object";
            return typeName.ToLowerInvariant() switch
            {
                "integer" => "int",
                "long" => "long",
                "single" => "float",
                "double" => "double",
                "string" => "string",
                "boolean" => "bool",
                "byte" => "byte",
                "short" => "short",
                "object" => "object",
                "void" => "void",
                _ => SanitizeName(typeName)
            };
        }

        /// <summary>
        /// Wrap an async method's return type in Task/Task&lt;T&gt;.
        /// A declared Task(Of T)/Task return type is kept as-is (no double wrapping).
        /// </summary>
        private static string WrapAsyncReturnType(string returnType)
        {
            if (returnType == "void")
                return "Task";
            if (returnType == "Task" || returnType.StartsWith("Task<"))
                return returnType;
            return $"Task<{returnType}>";
        }

        /// <summary>
        /// Map BasicLang access modifiers to C# access modifiers
        /// </summary>
        private string MapAccessModifier(AST.AccessModifier access)
        {
            return access switch
            {
                AST.AccessModifier.Public => "public",
                AST.AccessModifier.Private => "private",
                AST.AccessModifier.Protected => "protected",
                AST.AccessModifier.Friend => "internal",           // Friend is like C# internal
                AST.AccessModifier.ProtectedFriend => "protected internal",
                _ => "private"
            };
        }

        /// <summary>
        /// Format a single parameter for C# output
        /// </summary>
        private string FormatParameter(IRVariable param, bool isFirstExtensionParam = false)
        {
            var parts = new List<string>();

            // Extension method 'this' modifier
            if (isFirstExtensionParam)
                parts.Add("this");

            // params for ParamArray
            if (param.IsParamArray)
                parts.Add("params");

            // ref for ByRef
            if (param.IsByRef)
                parts.Add("ref");

            // Type and name
            parts.Add(MapType(param.Type));
            parts.Add(GetValueName(param));

            var result = string.Join(" ", parts);

            // Default value for optional
            if (param.IsOptional && param.DefaultValue != null)
            {
                result += " = " + FormatDefaultValue(param.DefaultValue);
            }
            else if (param.IsOptional)
            {
                // Default value based on type
                result += " = " + GetDefaultValueLiteral(param.Type);
            }

            return result;
        }

        /// <summary>
        /// Format a single parameter from IRParameter
        /// </summary>
        private string FormatIRParameter(IRParameter param)
        {
            var parts = new List<string>();

            // params for ParamArray
            if (param.IsParamArray)
                parts.Add("params");

            // ref for ByRef
            if (param.IsByRef)
                parts.Add("ref");

            // Type and name. Prefer the fully-resolved Type (carries generic arguments, e.g.
            // Dictionary<string, int>) when present; fall back to the bare TypeName string.
            parts.Add(param.Type != null ? MapType(param.Type) : MapTypeName(param.TypeName));
            parts.Add(SanitizeName(param.Name));

            var result = string.Join(" ", parts);

            // Default value for optional
            if (param.IsOptional && param.DefaultValue != null)
            {
                result += " = " + FormatDefaultValue(param.DefaultValue);
            }
            else if (param.IsOptional)
            {
                // Use default for the type
                result += " = default";
            }

            return result;
        }

        /// <summary>
        /// Format a default value expression for C#
        /// </summary>
        private string FormatDefaultValue(IRValue value)
        {
            // One literal renderer for defaults and expressions. ⛔ This kept its own copy of the
            // rules and drifted: it had no Char arm (`Optional c As Char = "a"c` emitted
            // `char c = a` — CS0103 in EVERY culture), left String defaults unescaped (a default
            // containing a quote emitted `"a"b"` — CS1003/CS1010), had no Single arm until
            // CSharpFloatingLiteral was bolted on, and fell back to CurrentCulture ToString()
            // (sv-SE `int n = −5`, U+2212 — CS1056). EmitConstant handles all of them.
            if (value is IRConstant constant)
                return EmitConstant(constant);
            return "default";
        }

        /// <summary>
        /// Get default value literal for a type
        /// </summary>
        private string GetDefaultValueLiteral(TypeInfo type)
        {
            if (type == null) return "default";
            var typeName = type.Name?.ToLowerInvariant() ?? "";
            return typeName switch
            {
                "integer" or "int" => "0",
                "long" => "0L",
                "single" or "float" => "0.0f",
                "double" => "0.0",
                "boolean" or "bool" => "false",
                "string" => "\"\"",
                "char" => "'\\0'",
                _ => "default"
            };
        }

        /// <summary>
        /// Generate a C# class from IRClass
        /// </summary>
        /// <summary>
        /// Fields and properties of the class being generated, and of its bases — the names an
        /// unqualified identifier inside one of its methods can resolve to.
        ///
        /// <para>⛔ MEASURED without this: <c>Total = Total + x</c> inside a method emitted an
        /// EMPTY body. IRBuilder lowers that assignment as an IRBinaryOp RENAMED to
        /// <c>Total</c> (no IRAssignment), and <see cref="IsNamedDestination"/> knew only
        /// parameters, locals and globals — so a field-named result looked like an unused SSA
        /// temp and was skipped, from a build that reported success. C++ and JavaScript both
        /// emitted the statement.</para>
        /// </summary>
        private HashSet<string> _currentClassMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private HashSet<string> CollectMemberNames(IRClass irClass)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var current = irClass;
            while (current != null && seen.Add(current.Name))
            {
                foreach (var f in current.Fields ?? new List<IRField>())
                    if (f?.Name != null) names.Add(f.Name);
                foreach (var p in current.Properties ?? new List<IRProperty>())
                    if (p?.Name != null) names.Add(p.Name);

                if (string.IsNullOrEmpty(current.BaseClass) || _currentModule?.Classes == null) break;
                _currentModule.Classes.TryGetValue(current.BaseClass, out current);
            }

            return names;
        }

        /// <summary>The events of the class being generated, by name — what a <c>raise_X</c> call resolves against.</summary>
        private Dictionary<string, IREvent> _currentClassEvents = new Dictionary<string, IREvent>(StringComparer.OrdinalIgnoreCase);

        private void GenerateClass(IRClass irClass)
        {
            _currentClassMemberNames = CollectMemberNames(irClass);
            _currentClassEvents = new Dictionary<string, IREvent>(StringComparer.OrdinalIgnoreCase);
            foreach (var evt in irClass.Events ?? new List<IREvent>())
                if (evt?.Name != null) _currentClassEvents[evt.Name] = evt;
            try
            {
                GenerateClassBody(irClass);
            }
            finally
            {
                _currentClassMemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _currentClassEvents = new Dictionary<string, IREvent>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void GenerateClassBody(IRClass irClass)
        {
            // Class declaration with generic parameters
            var className = SanitizeName(irClass.Name);
            var genericParams = "";
            if (irClass.GenericParameters != null && irClass.GenericParameters.Count > 0)
            {
                genericParams = "<" + string.Join(", ", irClass.GenericParameters) + ">";
            }

            var abstractMod = irClass.IsAbstract ? "abstract " : "";
            var kindKeyword = irClass.IsStruct ? "struct" : "class";
            var classDecl = $"public {abstractMod}{kindKeyword} {className}{genericParams}";
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                classDecl += $" : {SanitizeName(irClass.BaseClass)}";
                if (irClass.Interfaces.Count > 0)
                {
                    classDecl += ", " + string.Join(", ", irClass.Interfaces.Select(SanitizeName));
                }
            }
            else if (irClass.Interfaces.Count > 0)
            {
                classDecl += " : " + string.Join(", ", irClass.Interfaces.Select(SanitizeName));
            }

            // Generate constraint clauses for generic type parameters
            var constraints = GenerateConstraintClauses(irClass.GenericTypeParams);
            if (!string.IsNullOrEmpty(constraints))
            {
                classDecl += constraints;
            }

            WriteLine(classDecl);
            WriteLine("{");
            Indent();

            // Fields
            foreach (var field in irClass.Fields)
            {
                var access = MapAccessModifier(field.Access);
                var staticMod = field.IsStatic ? "static " : "";
                var type = MapType(field.Type);
                var name = SanitizeName(field.Name);
                // An explicit initializer wins; otherwise a fixed-size array field allocates,
                // like every other declaration site (`Public Cells(9) As Integer`).
                var init = field.Initializer is IRConstant c
                    ? $" = {EmitConstant(c)}"
                    : SizedArrayInitializer(field.Type) is string sized ? $" = {sized}" : "";
                WriteLine($"{access} {staticMod}{type} {name}{init};");
            }

            if (irClass.Fields.Count > 0)
                WriteLine();

            // Constructors
            foreach (var ctor in irClass.Constructors)
            {
                GenerateConstructor(irClass, ctor);
                WriteLine();
            }

            // Properties
            foreach (var prop in irClass.Properties)
            {
                GenerateProperty(irClass, prop);
                WriteLine();
            }

            // Events
            foreach (var evt in irClass.Events)
            {
                GenerateEvent(evt);
            }

            if (irClass.Events.Count > 0)
                WriteLine();

            // Methods
            foreach (var method in irClass.Methods)
            {
                GenerateMethod(irClass, method);
                WriteLine();
            }

            Unindent();
            WriteLine("}");
        }

        /// <summary>
        /// Generate a constructor
        /// </summary>
        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var access = MapAccessModifier(ctor.Access);
            var className = SanitizeName(irClass.Name);

            // Generate parameter list from implementation
            var paramList = "";
            if (ctor.Implementation != null)
            {
                paramList = string.Join(", ", ctor.Implementation.Parameters.Select(p =>
                    FormatParameter(p)));
            }

            // Base constructor call
            var baseCtor = "";
            if (!string.IsNullOrEmpty(irClass.BaseClass) && ctor.BaseConstructorArgs.Count > 0)
            {
                var baseArgs = string.Join(", ", ctor.BaseConstructorArgs.Select(a =>
                    a is IRConstant c ? EmitConstant(c) : SanitizeName(a.Name)));
                baseCtor = $" : base({baseArgs})";
            }

            WriteLine($"{access} {className}({paramList}){baseCtor}");
            WriteLine("{");
            Indent();

            // Generate body from implementation
            if (ctor.Implementation?.EntryBlock != null)
            {
                _currentFunction = ctor.Implementation;
                InitializeFunctionContext(ctor.Implementation);
                _processedBlocks = new HashSet<BasicBlock>();
                _loopEndBlocks = new Stack<BasicBlock>();
                ResetLoopExitState();

                // Declare locals — and any temp materialised under ADR-0001 (see DeclareLocals)
                DeclareLocals(ctor.Implementation, sizedArrays: false);

                GenerateStructuredBlock(ctor.Implementation.EntryBlock);
                _currentFunction = null;
            }

            Unindent();
            WriteLine("}");
        }

        /// <summary>
        /// Generate a property
        /// </summary>
        private void GenerateProperty(IRClass irClass, IRProperty prop)
        {
            var access = MapAccessModifier(prop.Access);
            var staticMod = prop.IsStatic ? "static " : "";
            var type = MapType(prop.Type);
            var name = SanitizeName(prop.Name);

            // ⛔ WITHOUT THIS THE EMITTED PROPERTY WAS PLAIN ON BOTH CLASSES, and C# hiding
            // without `new` is only a WARNING — so the file compiled and reading an overridden
            // property through a base-typed variable returned the BASE's value. Measured. The
            // two modifiers are mutually exclusive in C# (an override is already virtual), which
            // is why this is an if/else and not two concatenated strings, exactly as
            // GenerateMethod spells it.
            // ⚠ A Shared property is never virtual, and the guard is not theoretical: the front
            // end ACCEPTS `Public Shared Overridable Property` (VB itself refuses it, BC30503 —
            // a separate front-end gap, not decided here), and without this the emitted
            // `public static virtual int N` does not compile at all, where before it was a plain
            // static property that did. MSILBackend.GenerateProperty makes the same choice, for
            // the same reason: `static virtual` will not assemble either.
            var virtualMod = prop.IsStatic ? ""
                : prop.IsOverride ? "override "
                : prop.IsVirtual ? "virtual "
                : "";

            // AUTO-PROPERTY: `Public Property V As Integer` with no Get/Set body reaches the
            // IR with both accessors null. Falling through would emit `public int V { }` — a
            // property with no accessors, which does not compile. C# has real auto-property
            // syntax, so emit that and let the C# compiler own the backing field.
            if (prop.Getter == null && prop.Setter == null)
            {
                // WriteOnly has no auto form: C# requires a get accessor on an
                // auto-property (CS8051), so `{ set; }` would not compile. Emit an explicit
                // backing field instead of silently producing a broken file.
                if (prop.IsWriteOnly)
                {
                    var backing = SanitizeName("__" + prop.Name);
                    WriteLine($"private {staticMod}{type} {backing};");
                    WriteLine($"{access} {staticMod}{virtualMod}{type} {name} {{ set {{ {backing} = value; }} }}");
                    return;
                }

                WriteLine($"{access} {staticMod}{virtualMod}{type} {name} {{ {(prop.IsReadOnly ? "get;" : "get; set;")} }}");
                return;
            }

            WriteLine($"{access} {staticMod}{virtualMod}{type} {name}");
            WriteLine("{");
            Indent();

            // Getter
            if (prop.Getter != null && !prop.IsWriteOnly)
            {
                WriteLine("get");
                WriteLine("{");
                Indent();
                _currentFunction = prop.Getter;
                InitializeFunctionContext(prop.Getter);
                // ⛔ An accessor declared NO locals at all: a Get whose whole body was
                // `Dim sum As Integer = 5` / `Return sum + 1` was CS0103 on `sum`, loop or not.
                DeclareLocals(prop.Getter, sizedArrays: false);
                _processedBlocks = new HashSet<BasicBlock>();
                _loopEndBlocks = new Stack<BasicBlock>();
                ResetLoopExitState();
                if (prop.Getter.EntryBlock != null)
                    GenerateStructuredBlock(prop.Getter.EntryBlock);
                _currentFunction = null;
                Unindent();
                WriteLine("}");
            }

            // Setter
            if (prop.Setter != null && !prop.IsReadOnly)
            {
                WriteLine("set");
                WriteLine("{");
                Indent();
                _currentFunction = prop.Setter;
                InitializeFunctionContext(prop.Setter);
                DeclareLocals(prop.Setter, sizedArrays: false);
                _processedBlocks = new HashSet<BasicBlock>();
                _loopEndBlocks = new Stack<BasicBlock>();
                ResetLoopExitState();
                if (prop.Setter.EntryBlock != null)
                    GenerateStructuredBlock(prop.Setter.EntryBlock);
                _currentFunction = null;
                Unindent();
                WriteLine("}");
            }

            Unindent();
            WriteLine("}");
        }

        /// <summary>
        /// Generate an event
        /// </summary>
        private void GenerateEvent(IREvent evt)
        {
            var access = MapAccessModifier(evt.Access);
            var staticMod = evt.IsStatic ? "static " : "";
            // The full TypeInfo when the IR carries it — `Action<int>`, not `Action`.
            var delegateType = evt.Type != null ? MapType(evt.Type) : SanitizeName(evt.DelegateType);
            var name = SanitizeName(evt.Name);
            WriteLine($"{access} {staticMod}event {delegateType} {name};");
        }

        /// <summary>
        /// Generate a method
        /// </summary>
        private void GenerateMethod(IRClass irClass, IRMethod method)
        {
            var access = MapAccessModifier(method.Access);
            var staticMod = method.IsStatic ? "static " : "";
            var abstractMod = method.IsAbstract ? "abstract " : "";
            var virtualMod = method.IsVirtual && !method.IsOverride && !method.IsAbstract ? "virtual " : "";
            var overrideMod = method.IsOverride ? "override " : "";
            var sealedMod = method.IsSealed && method.IsOverride ? "sealed " : "";
            var returnType = MapType(method.ReturnType);
            var name = SanitizeName(method.Name);

            // Async methods return Task/Task<T> (abstract methods have no implementation to check)
            var asyncMod = "";
            if (method.Implementation?.IsAsync == true && !method.IsAbstract)
            {
                asyncMod = "async ";
                returnType = WrapAsyncReturnType(returnType);
            }

            // Add generic parameters
            var genericParams = "";
            if (method.GenericParameters != null && method.GenericParameters.Count > 0)
            {
                genericParams = "<" + string.Join(", ", method.GenericParameters) + ">";
            }

            // Generate parameter list
            var paramList = "";
            if (method.Implementation != null)
            {
                paramList = string.Join(", ", method.Implementation.Parameters.Select(p =>
                    FormatParameter(p)));
            }

            // Check if this is an operator overload
            if (method.Name.StartsWith("op_"))
            {
                var opName = method.Name.Substring(3);  // Remove "op_" prefix
                var opSymbol = opName switch
                {
                    "Addition" => "+",
                    "Subtraction" => "-",
                    "Multiply" => "*",
                    "Division" => "/",
                    "Modulus" => "%",
                    "Equality" => "==",
                    "Inequality" => "!=",
                    "LessThan" => "<",
                    "GreaterThan" => ">",
                    "LessThanOrEqual" => "<=",
                    "GreaterThanOrEqual" => ">=",
                    "BitwiseAnd" => "&",
                    "BitwiseOr" => "|",
                    "ExclusiveOr" => "^",
                    "LeftShift" => "<<",
                    "RightShift" => ">>",
                    "UnaryNegation" => "-",
                    "UnaryPlus" => "+",
                    "LogicalNot" => "!",
                    "OnesComplement" => "~",
                    "Increment" => "++",
                    "Decrement" => "--",
                    "Implicit" => "implicit operator",
                    "Explicit" => "explicit operator",
                    _ => opName
                };

                // Check if this is a conversion operator
                if (opSymbol == "implicit operator" || opSymbol == "explicit operator")
                {
                    WriteLine($"public static {opSymbol} {returnType}({paramList})");
                }
                else
                {
                    WriteLine($"public static {returnType} operator {opSymbol}({paramList})");
                }
            }
            else
            {
                WriteLine($"{access} {staticMod}{sealedMod}{overrideMod}{abstractMod}{virtualMod}{asyncMod}{returnType} {name}{genericParams}({paramList})");
            }

            // Abstract methods don't have a body
            if (method.IsAbstract)
            {
                WriteLine(";");
                return;
            }

            WriteLine("{");
            Indent();

            // Generate body
            if (method.Implementation != null)
            {
                _currentFunction = method.Implementation;
                InitializeFunctionContext(method.Implementation);
                _processedBlocks = new HashSet<BasicBlock>();
                _loopEndBlocks = new Stack<BasicBlock>();
                ResetLoopExitState();

                // Declare locals — and any temp materialised under ADR-0001 (see DeclareLocals)
                DeclareLocals(method.Implementation, sizedArrays: false);

                if (method.Implementation.EntryBlock != null)
                    GenerateStructuredBlock(method.Implementation.EntryBlock);

                _currentFunction = null;
            }

            Unindent();
            WriteLine("}");
        }

        /// <summary>
        /// Initialize function context for code generation
        /// </summary>
        private void InitializeFunctionContext(IRFunction function)
        {
            _valueNames.Clear();
            _variableNameMap.Clear();
            _declaredIdentifiers.Clear();
            _tempDefsByName.Clear();
            _useCounts.Clear();
            _lastEmittedSourceLine = -1;
            _lastEmittedSourceFile = null;

            // Track declared identifiers
            foreach (var param in function.Parameters)
                _declaredIdentifiers.Add(param.Name);

            foreach (var local in function.LocalVariables)
                _declaredIdentifiers.Add(local.Name);

            if (_currentModule != null)
            {
                foreach (var g in _currentModule.GlobalVariables.Values)
                    _declaredIdentifiers.Add(g.Name);
            }

            // Map parameters and locals
            foreach (var param in function.Parameters)
            {
                var sanitized = SanitizeName(param.Name);
                _valueNames[param] = sanitized;
                _variableNameMap[param.Name] = sanitized;
            }

            foreach (var localVar in function.LocalVariables)
            {
                var sanitized = SanitizeName(localVar.Name);
                _valueNames[localVar] = sanitized;
                _variableNameMap[localVar.Name] = sanitized;
            }

            AnalyzeUseCounts(function);
            BuildTempDefinitions(function);
            ComputeMaterialisedTemps(function);
        }

        /// <summary>
        /// The C# access of a member of a MODULE's static class — a Module's or a file-scope
        /// procedure, global or constant: <c>public</c> when declared Public, else <c>internal</c>.
        ///
        /// <para>⛔ A file-scope Private is private to its FILE, and a Module's Private to its
        /// Module; the front end enforces both. Here every module is its own static class, so
        /// C#'s <c>private</c> ALSO hid the member from the classes and Module blocks of the same
        /// file, which the front end had let through: <c>Program.Total</c> from a class body, once
        /// it was qualified, was CS0122 — measured, on this backend alone. MSIL maps the same way
        /// (<c>assembly</c>); the flattening backends have no such boundary. Class members keep
        /// <see cref="MapAccessModifier(IR.AccessModifier)"/>: a class IS the C# class.</para>
        /// </summary>
        private static string ModuleMemberAccess(IR.AccessModifier access) =>
            access == IR.AccessModifier.Public ? "public" : "internal";

        /// <summary>
        /// Map IR access modifier to C# string
        /// </summary>
        private string MapAccessModifier(IR.AccessModifier access)
        {
            return access switch
            {
                IR.AccessModifier.Public => "public",
                IR.AccessModifier.Private => "private",
                IR.AccessModifier.Protected => "protected",
                IR.AccessModifier.Friend => "internal",
                _ => "private"
            };
        }

        private void GenerateMainMethod()
        {
            WriteLine("static void Main(string[] args)");
            WriteLine("{");
            Indent();
            WriteLine("Console.WriteLine(\"No Main function found\");");
            Unindent();
            WriteLine("}");
        }

        private void GenerateFunction(IRFunction function)
        {
            _currentFunction = function;

            _valueNames.Clear();
            _variableNameMap.Clear();
            _declaredIdentifiers.Clear();
            _tempDefsByName.Clear();
            _useCounts.Clear();
            _lastEmittedSourceLine = -1;
            _lastEmittedSourceFile = null;

            // Track declared identifiers (params, locals, globals)
            foreach (var param in function.Parameters)
                _declaredIdentifiers.Add(param.Name);

            foreach (var local in function.LocalVariables)
                _declaredIdentifiers.Add(local.Name);

            if (_currentModule != null)
            {
                foreach (var g in _currentModule.GlobalVariables.Values)
                    _declaredIdentifiers.Add(g.Name);
            }

            // Map parameters and locals to sanitized names
            foreach (var param in function.Parameters)
            {
                var sanitized = SanitizeName(param.Name);
                _valueNames[param] = sanitized;
                _variableNameMap[param.Name] = sanitized;
            }

            foreach (var localVar in function.LocalVariables)
            {
                var sanitized = SanitizeName(localVar.Name);
                _valueNames[localVar] = sanitized;
                _variableNameMap[localVar.Name] = sanitized;
            }

            AnalyzeUseCounts(function);
            BuildTempDefinitions(function);
            ComputeMaterialisedTemps(function);

            // Check if this is a lambda - lambdas are generated inline, not as separate functions
            if (function.IsLambda)
            {
                // Lambdas are generated inline where they're used, skip here
                return;
            }

            // Check if this is an operator overload (generated with op_ prefix)
            // Function names can be "op_Addition" or "ClassName.op_Addition"
            // Check before sanitizing since sanitization removes dots
            if (function.Name.Contains(".op_") || function.Name.StartsWith("op_"))
            {
                GenerateOperator(function);
                return;
            }

            // Signature
            var returnType = MapType(function.ReturnType);
            var functionName = SanitizeName(function.Name);

            // Add generic type parameters if any
            var genericParams = "";
            if (function.GenericParameters != null && function.GenericParameters.Count > 0)
            {
                genericParams = "<" + string.Join(", ", function.GenericParameters) + ">";
            }

            // Generate parameters, with 'this' modifier for extension methods
            var paramList = new List<string>();
            for (int i = 0; i < function.Parameters.Count; i++)
            {
                var p = function.Parameters[i];
                var isFirstExtensionParam = function.IsExtension && i == 0;
                paramList.Add(FormatParameter(p, isFirstExtensionParam));
            }
            var parameters = string.Join(", ", paramList);

            // Handle async and iterator modifiers
            var asyncModifier = function.IsAsync ? "async " : "";
            var actualReturnType = returnType;

            if (function.IsAsync)
            {
                actualReturnType = WrapAsyncReturnType(returnType);
            }
            else if (function.IsIterator)
            {
                // Wrap return type in IEnumerable<T>
                if (returnType != "void")
                    actualReturnType = $"IEnumerable<{returnType}>";
            }

            // Generate constraint clauses for generic type parameters
            var constraints = GenerateConstraintClauses(function.GenericTypeParams);

            // A module's procedure, so its declared access maps like a module's global does.
            var accessMod = ModuleMemberAccess(function.Access);

            // WinForms/WPF require an STA entry point; harmless for console apps.
            // Not valid on async Main (compiler ignores it with a warning there).
            if (functionName.Equals("Main", StringComparison.OrdinalIgnoreCase) && !function.IsAsync)
                WriteLine("[STAThread]");

            WriteLine($"{accessMod} static {asyncModifier}{actualReturnType} {functionName}{genericParams}({parameters}){constraints}");

            WriteLine("{");
            Indent();

            // Declare locals — and any temp materialised under ADR-0001 (see DeclareLocals).
            // Use #line hidden so the PDB doesn't map these to the temp .cs file
            var hasDeclarations = function.LocalVariables.Count > 0 || MaterialisedTempsOf(function).Any();
            if (hasDeclarations)
                EmitLineHidden();
            DeclareLocals(function, sizedArrays: true, blankLineAfter: false);

            if (hasDeclarations)
            {
                _output.AppendLine("#line default");
                _lastEmittedSourceLine = -1;
                _lastEmittedSourceFile = null;
                WriteLine();
            }

            // Body - use structured control flow generation
            _processedBlocks = new HashSet<BasicBlock>();
            _loopEndBlocks = new Stack<BasicBlock>();
            ResetLoopExitState();
            if (function.EntryBlock != null)
                GenerateStructuredBlock(function.EntryBlock);

            EmitLineHidden();
            Unindent();
            WriteLine("}");

            _currentFunction = null;
        }

        private string GenerateLambdaExpression(IRFunction lambdaFunc)
        {
            var sb = new StringBuilder();

            // Generate parameters
            var paramList = new List<string>();
            foreach (var param in lambdaFunc.Parameters)
            {
                var paramName = SanitizeName(param.Name);
                if (param.Type != null)
                {
                    // Explicitly typed lambda parameter
                    var paramType = MapType(param.Type);
                    paramList.Add($"{paramType} {paramName}");
                }
                else
                {
                    // Inferred type
                    paramList.Add(paramName);
                }
            }

            var parameters = string.Join(", ", paramList);

            // Handle zero parameters
            if (paramList.Count == 0)
            {
                sb.Append("() => ");
            }
            // Single parameter without explicit type can omit parentheses
            else if (paramList.Count == 1 && !paramList[0].Contains(' '))
            {
                sb.Append($"{parameters} => ");
            }
            else
            {
                sb.Append($"({parameters}) => ");
            }

            // Generate body
            if (lambdaFunc.EntryBlock != null && lambdaFunc.EntryBlock.Instructions.Count > 0)
            {
                // Check if it's a simple expression lambda: single block ending in a
                // return whose value inlines all preceding pure value computations
                var instructions = lambdaFunc.EntryBlock.Instructions;
                bool isExpressionLambda = lambdaFunc.Blocks.Count == 1 &&
                    instructions.Count > 0 &&
                    instructions[instructions.Count - 1] is IRReturn lastRet &&
                    lastRet.Value != null &&
                    instructions.Take(instructions.Count - 1)
                        .All(i => i is IRValue && !(i is IRCall) && !(i is IRStore) && !(i is IRAlloca));

                if (isExpressionLambda)
                {
                    // Single expression lambda: x => x * 2
                    sb.Append(EmitExpression(((IRReturn)instructions[instructions.Count - 1]).Value));
                }
                else
                {
                    // Statement lambda with block: x => { statements; }
                    sb.Append("{\n");
                    var oldIndent = _indentLevel;
                    _indentLevel++;

                    for (int i = 0; i < instructions.Count; i++)
                    {
                        var instr = instructions[i];

                        // A CALL renamed after the variable it assigns — `Total = Add(Total, x)`
                        // — is a store, not a statement call. Found by review: the branch below
                        // excludes IRCall, so it fell to GenerateInlineStatement and only the
                        // call was emitted; the accumulator never changed.
                        if (instr is IRCall namedCall && IsNamedDestination(namedCall))
                        {
                            sb.Append($"{new string(' ', _indentLevel * 4)}{GetValueName(namedCall)} = {EmitExpression(namedCall)};\n");
                            continue;
                        }

                        // Skip pure value computations (temps) - they get inlined
                        // into the expressions that consume them.
                        //
                        // ⛔ Unless the result is NAMED AFTER A VARIABLE — IRBuilder lowers
                        // `total = total + x` as an IRBinaryOp renamed to `total`, with no
                        // IRAssignment. Skipping that "temp" emitted an EMPTY lambda body, and
                        // a Sub lambda accumulating into a captured local (or a field) silently
                        // did nothing. Measured: the same program summed to 6 on C++ and JS
                        // and printed 0 here.
                        if (instr is IRValue namedValue && !(instr is IRCall) && !(instr is IRStore) &&
                            !(instr is IRAlloca) && !(instr is IRAssignment))
                        {
                            if (IsNamedDestination(namedValue))
                            {
                                sb.Append($"{new string(' ', _indentLevel * 4)}{GetValueName(namedValue)} = {EmitExpression(namedValue)};\n");
                            }
                            continue;
                        }

                        if (instr is IRReturn retStmt)
                        {
                            if (retStmt.Value != null)
                            {
                                sb.Append($"{new string(' ', _indentLevel * 4)}return {EmitExpression(retStmt.Value)};\n");
                            }
                            else if (i == instructions.Count - 1)
                            {
                                // Trailing "return;" is implicit in a statement lambda
                            }
                            else
                            {
                                sb.Append($"{new string(' ', _indentLevel * 4)}return;\n");
                            }
                        }
                        else
                        {
                            // Handle other statements
                            sb.Append($"{new string(' ', _indentLevel * 4)}{GenerateInlineStatement(instr)};\n");
                        }
                    }

                    _indentLevel = oldIndent;
                    sb.Append($"{new string(' ', _indentLevel * 4)}}}");
                }
            }
            else
            {
                // Empty lambda body
                sb.Append("{ }");
            }

            return sb.ToString();
        }

        private string GenerateInlineStatement(IRInstruction instr)
        {
            // Generate statement inline for lambda bodies
            switch (instr)
            {
                case IRStore store:
                    return $"{EmitExpression(store.Address)} = {EmitExpression(store.Value)}";
                case IRCall call:
                    // EmitExpression maps standard library calls (e.g. Print -> Console.Write)
                    return EmitExpression(call);
                default:
                    return EmitExpression(instr as IRValue);
            }
        }

        private void AnalyzeUseCounts(IRFunction function)
        {
            foreach (var block in function.Blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    foreach (var op in GetOperands(instr))
                    {
                        if (op == null) continue;
                        _useCounts.TryGetValue(op, out var c);
                        _useCounts[op] = c + 1;
                    }
                }
            }
        }

        private void BuildTempDefinitions(IRFunction function)
        {
            // Only map "temp-like" names (i.e., not declared locals/params/globals).
            foreach (var block in function.Blocks)
            {
                foreach (var instr in block.Instructions)
                {
                    if (instr is not IRValue v) continue;
                    if (string.IsNullOrEmpty(v.Name)) continue;

                    // A result named after a variable OR (when IRBuilder renamed it) a class
                    // member is an assignment, not a temp — registering it here would inline
                    // `Total + x` into every later read of `Total`.
                    if (_declaredIdentifiers.Contains(v.Name) ||
                        (v.NamedAfterVariable && _currentClassMemberNames.Contains(v.Name)))
                        continue;

                    // first definition wins (good enough for simple SSA-style temp regs)
                    if (!_tempDefsByName.ContainsKey(v.Name))
                        _tempDefsByName[v.Name] = v;
                }
            }
        }

        private void GenerateOperator(IRFunction function)
        {
            var returnType = MapType(function.ReturnType);

            // Extract operator name - function name can be "op_Addition" or "ClassName.op_Addition"
            var funcName = function.Name;
            var opIndex = funcName.IndexOf("op_");
            var opName = opIndex >= 0 ? funcName.Substring(opIndex + 3) : funcName;

            // Map operator method names to C# operator symbols
            var opSymbol = opName switch
            {
                "Addition" => "+",
                "Subtraction" => "-",
                "Multiply" => "*",
                "Division" => "/",
                "Modulus" => "%",
                "BitwiseAnd" => "&",
                "BitwiseOr" => "|",
                "ExclusiveOr" => "^",
                "LeftShift" => "<<",
                "RightShift" => ">>",
                "Equality" => "==",
                "Inequality" => "!=",
                "LessThan" => "<",
                "GreaterThan" => ">",
                "LessThanOrEqual" => "<=",
                "GreaterThanOrEqual" => ">=",
                "UnaryNegation" => "-",
                "UnaryPlus" => "+",
                "LogicalNot" => "!",
                "OnesComplement" => "~",
                "True" => "true",
                "False" => "false",
                "Increment" => "++",
                "Decrement" => "--",
                "Implicit" => "implicit operator",
                "Explicit" => "explicit operator",
                _ => opName  // Fallback to method name
            };

            var parameters = string.Join(", ", function.Parameters.Select(p =>
                $"{MapType(p.Type)} {GetValueName(p)}"));

            // Check if this is a conversion operator
            if (opSymbol == "implicit operator" || opSymbol == "explicit operator")
            {
                WriteLine($"public static {opSymbol} {returnType}({parameters})");
            }
            else
            {
                WriteLine($"public static {returnType} operator {opSymbol}({parameters})");
            }

            WriteLine("{");
            Indent();

            // Declare locals — and any temp materialised under ADR-0001 (see DeclareLocals)
            DeclareLocals(function, sizedArrays: false);

            // Generate body
            _processedBlocks.Clear();
            GenerateStructuredBlock(function.EntryBlock);

            Unindent();
            WriteLine("}");
            WriteLine("");
        }

        private void GenerateBlock(BasicBlock block, HashSet<BasicBlock> visited)
        {
            GenerateStructuredBlock(block);
        }

        /// <summary>
        /// Generate structured C# code from a basic block, recognizing control flow patterns.
        /// </summary>
        private void GenerateStructuredBlock(BasicBlock block)
        {
            if (block == null || _processedBlocks.Contains(block))
                return;

            _processedBlocks.Add(block);

            // Emit non-control-flow instructions
            EmitBlockInstructions(block);

            // Handle the terminator instruction with structured control flow
            var terminator = block.Instructions.LastOrDefault();

            // Emit #line for the terminator (control-flow statements like If, While, Select)
            if (terminator is IRInstruction termInstr && termInstr.SourceLine > 0)
            {
                EmitLineDirective(termInstr.SourceLine, _currentFunction?.SourceFilePath);
            }

            if (terminator is IRConditionalBranch condBranch)
            {
                HandleConditionalBranch(condBranch);
            }
            else if (terminator is IRBranch branch)
            {
                HandleUnconditionalBranch(branch);
            }
            else if (terminator is IRReturn ret)
            {
                // Return is handled by Visit(IRReturn)
            }
            else if (terminator is IRSwitch switchInst)
            {
                HandleSwitchStatement(switchInst);
            }
            // For other terminators, the Visit method handles them
        }

        private void EmitLineDirective(int sourceLine, string sourceFile)
        {
            if (sourceLine <= 0 || string.IsNullOrEmpty(sourceFile)) return;
            // Normalize the path to use consistent separators (backslash on Windows)
            sourceFile = sourceFile.Replace('/', Path.DirectorySeparatorChar);
            if (sourceLine == _lastEmittedSourceLine && string.Equals(sourceFile, _lastEmittedSourceFile, StringComparison.OrdinalIgnoreCase)) return;

            _lastEmittedSourceLine = sourceLine;
            _lastEmittedSourceFile = sourceFile;

            _output.AppendLine($"#line {sourceLine} \"{sourceFile}\"");
        }

        private void EmitLineHidden()
        {
            _lastEmittedSourceLine = -1;
            _lastEmittedSourceFile = null;
            _output.AppendLine("#line hidden");
        }

        private void EmitBlockInstructions(BasicBlock block)
        {
            var instructions = block.Instructions.ToList();
            var emittedTupleGroups = new HashSet<int>();

            for (int i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];

                // Skip control flow - we handle it structurally
                if (instruction is IRBranch or IRConditionalBranch or IRSwitch)
                    continue;

                if (!ShouldEmitInstruction(instruction))
                    continue;

                // A materialised temp is written ONCE, here, into the local DeclareLocals declared;
                // every use reads that local (EmitExpression). Not through Visit: Visit(IRCall)
                // deliberately emits nothing for a call whose result is used.
                if (instruction is IRValue materialisedValue && _materialised.Contains(materialisedValue))
                {
                    if (materialisedValue.SourceLine > 0)
                        EmitLineDirective(materialisedValue.SourceLine, _currentFunction?.SourceFilePath);
                    EmitMaterialisedDefinition(materialisedValue);
                    continue;
                }

                // Skip tuple elements that were already emitted as part of a group
                if (emittedTupleGroups.Contains(i))
                    continue;

                // Handle consecutive tuple element accesses as a single deconstruction
                if (instruction is IRTupleElement tupleElem)
                {
                    // Find all consecutive IRTupleElement instructions with the same source tuple
                    var group = new List<IRTupleElement> { tupleElem };
                    for (int j = i + 1; j < instructions.Count; j++)
                    {
                        if (instructions[j] is IRTupleElement nextElem &&
                            ReferenceEquals(nextElem.Tuple, tupleElem.Tuple))
                        {
                            group.Add(nextElem);
                            emittedTupleGroups.Add(j);
                        }
                        else if (instructions[j] is not IRBranch and not IRConditionalBranch and not IRSwitch)
                        {
                            break;
                        }
                    }

                    if (group.Count > 1)
                    {
                        // Emit as C# tuple deconstruction: (x, y, z) = tuple;
                        EmitTupleDeconstruction(group);
                        continue;
                    }
                }

                // Emit #line directive for source-level debugging
                if (instruction is IRInstruction irInstr && irInstr.SourceLine > 0)
                {
                    EmitLineDirective(irInstr.SourceLine, _currentFunction?.SourceFilePath);
                }

                instruction.Accept(this);
            }
        }

        /// <summary>
        /// Emit a group of tuple element accesses as a single C# deconstruction statement
        /// </summary>
        private void EmitTupleDeconstruction(List<IRTupleElement> elements)
        {
            // Sort by index to ensure correct order
            elements = elements.OrderBy(e => e.Index).ToList();

            var tupleExpr = EmitExpression(elements[0].Tuple);
            var varNames = elements.Select(e => SanitizeName(e.Name)).ToList();

            // Use C# tuple deconstruction syntax with assignment (variables already declared):
            // (x, y, z) = tuple;
            WriteLine($"({string.Join(", ", varNames)}) = {tupleExpr};");
        }

        private void HandleConditionalBranch(IRConditionalBranch condBranch)
        {
            var condition = EmitExpression(condBranch.Condition);
            var trueBlock = condBranch.TrueTarget;
            var falseBlock = condBranch.FalseTarget;

            // Detect loop patterns
            if (IsLoopHeader(trueBlock, falseBlock, out var loopBody, out var loopEnd, out var loopInc, out var loopType, out var negateCondition))
            {
                // For Until loops, negate the condition
                var loopCondition = negateCondition ? $"!({condition})" : condition;
                GenerateLoop(loopCondition, loopBody, loopEnd, loopInc, loopType);
                return;
            }

            // Detect if-then-else pattern
            if (IsIfThenElse(trueBlock, falseBlock, out var thenBlock, out var elseBlock, out var mergeBlock))
            {
                GenerateIfThenElse(condition, thenBlock, elseBlock, mergeBlock);
                return;
            }

            // Detect simple if-then pattern (no else)
            if (IsIfThen(trueBlock, falseBlock, out thenBlock, out mergeBlock))
            {
                GenerateIfThen(condition, thenBlock, mergeBlock);
                return;
            }

            // Fallback: emit goto-style code
            WriteLine($"if ({condition})");
            WriteLine("{");
            Indent();
            GenerateStructuredBlock(trueBlock);
            Unindent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();
            GenerateStructuredBlock(falseBlock);
            Unindent();
            WriteLine("}");
        }

        private void HandleUnconditionalBranch(IRBranch branch)
        {
            var target = branch.Target;

            // ⛔ FIRST, and before the _processedBlocks / ".end" tests below: an `Exit For` out of
            // a For Each targets a block those two tests both discard, which is exactly how
            // `Exit For` became a silent no-op on this backend (measured: a loop over 1,2,3,4
            // exiting at 3 totalled 10).
            if (TryEmitLoopExit(branch))
                return;

            // If the target is already processed or is a loop back-edge, skip
            // (the loop structure handles continuation)
            if (_processedBlocks.Contains(target))
                return;

            // If target is a merge block or loop end, we've already handled it
            if (target.Name.EndsWith(".end"))
                return;

            // Continue with the next block
            GenerateStructuredBlock(target);
        }

        private void HandleSwitchStatement(IRSwitch switchInst)
        {
            var value = EmitExpression(switchInst.Value);

            WriteLine($"switch ({value})");
            WriteLine("{");
            Indent();
            // Inside these braces `break` means the SWITCH — see EmitLoopExit.
            _switchDepth++;

            // Group value cases by their target block
            var casesByBlock = new Dictionary<BasicBlock, List<IRValue>>();
            foreach (var (caseValue, target) in switchInst.Cases)
            {
                if (!casesByBlock.ContainsKey(target))
                    casesByBlock[target] = new List<IRValue>();
                casesByBlock[target].Add(caseValue);
            }

            // Group pattern cases by their target block
            var patternsByBlock = new Dictionary<BasicBlock, List<IRPatternCase>>();
            foreach (var patternCase in switchInst.PatternCases)
            {
                if (!patternsByBlock.ContainsKey(patternCase.Target))
                    patternsByBlock[patternCase.Target] = new List<IRPatternCase>();
                patternsByBlock[patternCase.Target].Add(patternCase);
            }

            // Emit each case group with inline body
            foreach (var (block, caseValues) in casesByBlock)
            {
                // Emit value case labels
                foreach (var caseValue in caseValues)
                {
                    var caseExpr = EmitExpression(caseValue);
                    WriteLine($"case {caseExpr}:");
                }

                // Also emit pattern cases for this block
                if (patternsByBlock.TryGetValue(block, out var patterns))
                {
                    foreach (var pattern in patterns)
                    {
                        EmitPatternCase(pattern);
                    }
                    patternsByBlock.Remove(block);
                }

                EmitCaseBody(block);
            }

            // Emit remaining pattern-only cases (blocks with patterns but no value cases)
            foreach (var (block, patterns) in patternsByBlock)
            {
                foreach (var pattern in patterns)
                {
                    EmitPatternCase(pattern);
                }
                EmitCaseBody(block);
            }

            // Emit default case
            var defaultBlock = switchInst.DefaultTarget;
            WriteLine("default:");
            _processedBlocks.Add(defaultBlock);
            Indent();
            EmitBlockInstructions(defaultBlock);
            EmitCaseTerminator(defaultBlock);
            Unindent();

            _switchDepth--;
            Unindent();
            WriteLine("}");

            // Process the switch.end block
            var endBlock = switchInst.EndBlock;
            if (endBlock != null && !_processedBlocks.Contains(endBlock))
            {
                GenerateStructuredBlock(endBlock);
            }
        }

        private void EmitPatternCase(IRPatternCase pattern)
        {
            var whenClause = pattern.WhenGuard != null ? $" when {EmitExpression(pattern.WhenGuard)}" : "";

            switch (pattern)
            {
                case IRTypePatternCase typePattern:
                    var typeName = MapTypeName(typePattern.TypeName);
                    if (!string.IsNullOrEmpty(typePattern.BindingVariable))
                    {
                        WriteLine($"case {typeName} {typePattern.BindingVariable}{whenClause}:");
                    }
                    else
                    {
                        WriteLine($"case {typeName}{whenClause}:");
                    }
                    break;

                case IRRangePatternCase rangePattern:
                    var lower = EmitExpression(rangePattern.LowerBound);
                    var upper = EmitExpression(rangePattern.UpperBound);
                    // C# 9+ relational pattern: >= lower and <= upper
                    WriteLine($"case >= {lower} and <= {upper}{whenClause}:");
                    break;

                case IRComparisonPatternCase compPattern:
                    var compValue = EmitExpression(compPattern.CompareValue);
                    var op = compPattern.Operator switch
                    {
                        ">" => ">",
                        "<" => "<",
                        ">=" => ">=",
                        "<=" => "<=",
                        "=" => "==",  // Note: not supported directly, needs workaround
                        "<>" => "!=",
                        _ => compPattern.Operator
                    };
                    // C# 9+ relational pattern
                    if (op == "==" || op == "!=")
                    {
                        // For equality, use when clause
                        if (string.IsNullOrEmpty(whenClause))
                        {
                            WriteLine($"case var _temp when _temp {op} {compValue}:");
                        }
                        else
                        {
                            WriteLine($"case var _temp when _temp {op} {compValue} && {EmitExpression(pattern.WhenGuard)}:");
                        }
                    }
                    else
                    {
                        WriteLine($"case {op} {compValue}{whenClause}:");
                    }
                    break;

                case IRConstantPatternCase constPattern:
                    var constValue = EmitExpression(constPattern.Value);
                    WriteLine($"case {constValue}{whenClause}:");
                    break;

                case IRNothingPatternCase:
                    // Null pattern
                    WriteLine($"case null{whenClause}:");
                    break;

                case IROrPatternCase orPattern:
                    // Or pattern: case 1 or 2 or 3
                    var alternatives = new List<string>();
                    foreach (var alt in orPattern.Alternatives)
                    {
                        alternatives.Add(GetPatternExpression(alt));
                    }
                    WriteLine($"case {string.Join(" or ", alternatives)}{whenClause}:");
                    break;

                case IRTuplePatternCase tuplePattern:
                    // Tuple deconstruction pattern: case (x, y, z)
                    var elements = new List<string>();
                    foreach (var elem in tuplePattern.Elements)
                    {
                        elements.Add(GetPatternExpression(elem));
                    }
                    WriteLine($"case ({string.Join(", ", elements)}){whenClause}:");
                    break;

                case IRBindingPatternCase bindingPattern:
                    // Binding pattern: var x when condition
                    // Uses var pattern to capture the value with a binding variable
                    if (!string.IsNullOrEmpty(bindingPattern.BindingVariable))
                    {
                        WriteLine($"case var {bindingPattern.BindingVariable}{whenClause}:");
                    }
                    else
                    {
                        // Fallback to default case if no binding variable
                        WriteLine($"default:");
                    }
                    break;
            }
        }

        /// <summary>
        /// Get the C# pattern expression for an IR pattern (used for or/tuple patterns)
        /// </summary>
        private string GetPatternExpression(IRPatternCase pattern)
        {
            switch (pattern)
            {
                case IRTypePatternCase typePattern:
                    var typeName = MapTypeName(typePattern.TypeName);
                    return !string.IsNullOrEmpty(typePattern.BindingVariable)
                        ? $"{typeName} {typePattern.BindingVariable}"
                        : typeName;

                case IRRangePatternCase rangePattern:
                    var lower = EmitExpression(rangePattern.LowerBound);
                    var upper = EmitExpression(rangePattern.UpperBound);
                    return $">= {lower} and <= {upper}";

                case IRComparisonPatternCase compPattern:
                    var compValue = EmitExpression(compPattern.CompareValue);
                    var op = compPattern.Operator switch
                    {
                        ">" => ">",
                        "<" => "<",
                        ">=" => ">=",
                        "<=" => "<=",
                        _ => compPattern.Operator
                    };
                    return $"{op} {compValue}";

                case IRConstantPatternCase constPattern:
                    return EmitExpression(constPattern.Value);

                case IRNothingPatternCase:
                    return "null";

                case IROrPatternCase orPattern:
                    var alternatives = orPattern.Alternatives.Select(GetPatternExpression);
                    return string.Join(" or ", alternatives);

                case IRTuplePatternCase tuplePattern:
                    var elements = tuplePattern.Elements.Select(GetPatternExpression);
                    return $"({string.Join(", ", elements)})";

                case IRBindingPatternCase bindingPattern:
                    return !string.IsNullOrEmpty(bindingPattern.BindingVariable)
                        ? $"var {bindingPattern.BindingVariable}"
                        : "_";

                default:
                    return "_";  // Discard pattern as fallback
            }
        }

        private void EmitCaseBody(BasicBlock block)
        {
            // Mark block as processed so it's not emitted again
            _processedBlocks.Add(block);

            // Emit the case body with indentation
            Indent();
            EmitBlockInstructions(block);
            EmitCaseTerminator(block);
            Unindent();
        }

        /// <summary>
        /// The terminator of a case section's first block — a <c>Case</c> or the <c>Case Else</c> —
        /// followed by the <c>break;</c> that closes the section.
        ///
        /// <para>⛔ The conditional-branch and IRSwitch arms were missing, so a case body holding an
        /// <c>If</c> or a nested <c>Select</c> became a bare <c>break;</c>: the If, and every
        /// statement after it in that case, was DROPPED with a green build. MEASURED: a
        /// <c>Case 1</c> of <c>If m = 1 … Else … End If</c> then a WriteLine compiled to
        /// <c>case 1: break;</c>. The If's own merge block carries the rest of the body and ends
        /// with the branch to the switch's end, which emits nothing.</para>
        /// </summary>
        private void EmitCaseTerminator(BasicBlock block)
        {
            var terminator = block.Instructions.LastOrDefault();
            if (terminator is IRReturn)
                return; // Return already emitted

            // ⛔ An `Exit For` written inside a `Select Case` arm. TryEmitLoopExit spells it as a
            // `goto` precisely because a `break` here would leave the SWITCH; adding the usual
            // trailing `break;` after it would be unreachable code (CS0162).
            if (terminator is IRBranch loopExit && TryEmitLoopExit(loopExit))
                return;

            if (terminator is IRConditionalBranch cond)
                HandleConditionalBranch(cond);
            else if (terminator is IRSwitch switchInst)
                HandleSwitchStatement(switchInst);
            else if (terminator is IRBranch branch)
                HandleUnconditionalBranch(branch); // a branch to the switch's end emits nothing

            WriteLine("break;");
        }

        private bool IsLoopHeader(BasicBlock trueBlock, BasicBlock falseBlock,
            out BasicBlock loopBody, out BasicBlock loopEnd, out BasicBlock loopInc, out string loopType, out bool negateCondition)
        {
            loopBody = null;
            loopEnd = null;
            loopInc = null;
            loopType = null;
            negateCondition = false;

            // Standard pattern: condition block branches to body (true) and end (false)
            if (trueBlock.Name.Contains(".body") && falseBlock.Name.Contains(".end"))
            {
                loopBody = trueBlock;
                loopEnd = falseBlock;
                negateCondition = false;

                // Find increment block through body block's terminator (more reliable than name matching)
                loopInc = FindIncrementBlock(trueBlock);

                if (trueBlock.Name.StartsWith("for.") || trueBlock.Name.StartsWith("foreach."))
                    loopType = "for";
                else if (trueBlock.Name.StartsWith("while."))
                    loopType = "while";
                else if (trueBlock.Name.StartsWith("do."))
                    loopType = "do";
                else
                    loopType = "while";

                return true;
            }

            // Until pattern: branches are swapped (end on true, body on false)
            if (trueBlock.Name.Contains(".end") && falseBlock.Name.Contains(".body"))
            {
                loopBody = falseBlock;
                loopEnd = trueBlock;
                negateCondition = true;  // Need to negate condition for Until loops

                // Find increment block through body block's terminator
                loopInc = FindIncrementBlock(falseBlock);

                if (falseBlock.Name.StartsWith("for.") || falseBlock.Name.StartsWith("foreach."))
                    loopType = "for";
                else if (falseBlock.Name.StartsWith("while."))
                    loopType = "while";
                else if (falseBlock.Name.StartsWith("do."))
                    loopType = "do";
                else
                    loopType = "while";

                return true;
            }

            return false;
        }

        /// <summary>
        /// Find the increment block by following the body block's branch target.
        /// This is more reliable than name matching when there are multiple loops.
        /// </summary>
        private BasicBlock FindIncrementBlock(BasicBlock bodyBlock)
        {
            // The body block should end with a branch to the increment block
            var terminator = bodyBlock.Instructions.LastOrDefault();
            if (terminator is IRBranch branch && branch.Target.Name.Contains(".inc"))
            {
                return branch.Target;
            }

            // If body has nested control flow, we need to trace through to find the inc block
            // Check all blocks that the body might branch to
            foreach (var instruction in bodyBlock.Instructions)
            {
                if (instruction is IRBranch br && br.Target.Name.Contains(".inc"))
                {
                    return br.Target;
                }
            }

            // ⛔ NO BY-NAME LAST RESORT HERE, and one was measured and removed rather than never
            // tried. The tempting rule is "if neither scan found it, take the block named
            // `{bodyPrefix}.inc`", on the theory that a body ending in `Exit For` never branches
            // to its own increment and so loses it. It cannot help, in either direction:
            //
            //   • When the `.inc` IS reachable, the structured walk out of the body's terminator
            //     (an If's merge block, a Select Case's `switch.end`, a nested loop's `.end`)
            //     emits it BEFORE GenerateLoop reaches the `incBlock` check, so the name lookup
            //     only ever hands back a block already in _processedBlocks. Measured over 207
            //     programs through the CLI — 66 shapes written to make the body terminate in
            //     every non-falling-through way, plus every loop program in this test project:
            //     the lookup reached its one emission site 51 times, ALREADY PROCESSED all 51.
            //   • When the `.inc` is NOT reachable — a body ending in an unconditional
            //     `Exit For` — it does not survive to be found: the IR optimizer deletes it, and
            //     every shipping route runs the optimizer unconditionally.
            //
            // So on the shipping path it is inert (all 207 emissions byte-identical with and
            // without it), and on the UNOPTIMIZED path some fixtures use it emits `i = i + 1`
            // after the `break` — CS0162, unreachable code. What actually stops
            // `For i = 1 To 4 / t = t + 1 / Exit For / Next` from looping forever is the `break`,
            // from TryEmitLoopExit; with the break suppressed the program hangs whether this
            // lookup is here or not.
            return null;
        }

        private void GenerateLoop(string condition, BasicBlock bodyBlock, BasicBlock endBlock, BasicBlock incBlock, string loopType)
        {
            // Push the loop end block so inner code can emit 'break' when targeting it
            if (endBlock != null)
                _loopEndBlocks.Push(endBlock);
            _loopSwitchDepths.Push(_switchDepth);

            WriteLine($"while ({condition})");
            WriteLine("{");
            Indent();

            // Generate body
            _processedBlocks.Add(bodyBlock);
            EmitBlockInstructions(bodyBlock);

            // Handle body's terminator
            var bodyTerminator = bodyBlock.Instructions.LastOrDefault();
            if (bodyTerminator is IRConditionalBranch innerCond)
            {
                // Nested control flow in loop body (if statements or inner loop conditions)
                HandleConditionalBranch(innerCond);
            }
            else if (bodyTerminator is IRSwitch switchInst)
            {
                // Switch statement inside loop body
                HandleSwitchStatement(switchInst);
            }
            else if (bodyTerminator is IRBranch innerBranch)
            {
                // ⛔ THE EXIT TEST GOES FIRST. `target != endBlock` below discards exactly the
                // branch an `Exit While`/`Exit For` written as the body's LAST statement
                // produces — measured: a While over 0..3 exiting on the first pass printed 4
                // instead of 1, because that branch was dropped and the loop ran to completion.
                if (!TryEmitLoopExit(innerBranch))
                {
                    // Nested loop: body branches unconditionally to inner loop's condition block
                    // Don't follow branches to increment or end blocks - those are handled below
                    var target = innerBranch.Target;
                    if (!_processedBlocks.Contains(target) &&
                        target != incBlock &&
                        target != endBlock &&
                        !target.Name.EndsWith(".inc") &&
                        !target.Name.EndsWith(".end"))
                    {
                        HandleUnconditionalBranch(innerBranch);
                    }
                }
            }

            // Always generate increment if it exists
            if (incBlock != null && !_processedBlocks.Contains(incBlock))
            {
                _processedBlocks.Add(incBlock);
                EmitBlockInstructions(incBlock);
            }

            Unindent();
            WriteLine("}");
            EmitLoopExitLabelIfNeeded(endBlock);

            // Pop the loop end block
            if (endBlock != null)
                _loopEndBlocks.Pop();
            _loopSwitchDepths.Pop();

            // Continue after the loop
            if (endBlock != null && !_processedBlocks.Contains(endBlock))
            {
                _processedBlocks.Add(endBlock);
                EmitBlockInstructions(endBlock);

                // Handle end block's terminator
                EmitContinuationTerminator(endBlock);
            }
        }

        private bool IsIfThenElse(BasicBlock trueBlock, BasicBlock falseBlock,
            out BasicBlock thenBlock, out BasicBlock elseBlock, out BasicBlock mergeBlock)
        {
            thenBlock = null;
            elseBlock = null;
            mergeBlock = null;

            // Pattern: true -> ifN.then, false -> ifN.else, both merge at ifN.end
            if (trueBlock.Name.Contains(".then") && falseBlock.Name.Contains(".else"))
            {
                thenBlock = trueBlock;
                elseBlock = falseBlock;

                // Extract the prefix (e.g., "if0" from "if0.then")
                var dotIndex = trueBlock.Name.IndexOf('.');
                if (dotIndex > 0)
                {
                    var prefix = trueBlock.Name.Substring(0, dotIndex);
                    // Find merge block with matching prefix
                    mergeBlock = _currentFunction.Blocks.FirstOrDefault(b =>
                        b.Name == $"{prefix}.end");
                }

                return mergeBlock != null;
            }

            return false;
        }

        private bool IsIfThen(BasicBlock trueBlock, BasicBlock falseBlock,
            out BasicBlock thenBlock, out BasicBlock mergeBlock)
        {
            thenBlock = null;
            mergeBlock = null;

            // Pattern: true -> ifN.then, false -> ifN.end (no else)
            if (trueBlock.Name.Contains(".then") && falseBlock.Name.Contains(".end"))
            {
                // Extract prefix from both blocks and verify they match
                var trueDot = trueBlock.Name.IndexOf('.');
                var falseDot = falseBlock.Name.IndexOf('.');
                if (trueDot > 0 && falseDot > 0)
                {
                    var truePrefix = trueBlock.Name.Substring(0, trueDot);
                    var falsePrefix = falseBlock.Name.Substring(0, falseDot);

                    // Only match if prefixes match (same if statement)
                    if (truePrefix == falsePrefix)
                    {
                        thenBlock = trueBlock;
                        mergeBlock = falseBlock;
                        return true;
                    }
                }
            }

            return false;
        }

        private void GenerateIfThenElse(string condition, BasicBlock thenBlock, BasicBlock elseBlock, BasicBlock mergeBlock)
        {
            // An ElseIf's nested conditional shares the outer If's merge block — leave it to the owner.
            bool ownsMerge = mergeBlock != null && _pendingIfMerges.Add(mergeBlock);

            WriteLine($"if ({condition})");
            WriteLine("{");
            Indent();

            _processedBlocks.Add(thenBlock);
            EmitBlockInstructions(thenBlock);

            // Handle then block's terminator (might have nested control flow, return, or break)
            EmitIfArmTerminator(thenBlock);

            Unindent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();

            _processedBlocks.Add(elseBlock);
            EmitBlockInstructions(elseBlock);

            // Handle else block's terminator
            EmitIfArmTerminator(elseBlock);

            Unindent();
            WriteLine("}");

            if (!ownsMerge)
                return;
            _pendingIfMerges.Remove(mergeBlock);

            // Continue after merge
            if (!_processedBlocks.Contains(mergeBlock))
            {
                _processedBlocks.Add(mergeBlock);
                EmitBlockInstructions(mergeBlock);

                EmitContinuationTerminator(mergeBlock);
            }
        }

        private void GenerateIfThen(string condition, BasicBlock thenBlock, BasicBlock mergeBlock)
        {
            bool ownsMerge = mergeBlock != null && _pendingIfMerges.Add(mergeBlock);

            WriteLine($"if ({condition})");
            WriteLine("{");
            Indent();

            _processedBlocks.Add(thenBlock);
            EmitBlockInstructions(thenBlock);

            // Handle then block's terminator
            EmitIfArmTerminator(thenBlock);

            Unindent();
            WriteLine("}");

            if (!ownsMerge)
                return;
            _pendingIfMerges.Remove(mergeBlock);

            // Continue after merge
            if (!_processedBlocks.Contains(mergeBlock))
            {
                _processedBlocks.Add(mergeBlock);
                EmitBlockInstructions(mergeBlock);

                EmitContinuationTerminator(mergeBlock);
            }
        }

        /// <summary>
        /// The terminator of an If arm's first block, emitted inside the arm's braces.
        ///
        /// <para>⛔ The IRSwitch arm was missing here and in every continuation below (see
        /// <see cref="EmitContinuationTerminator"/>), so a <c>Select Case</c> ending an If arm
        /// was DROPPED — the switch, its case bodies, and the code after it — with a green build.
        /// MEASURED: <c>If n &gt; 0 Then … Select Case n …</c> printed only the arm's first
        /// line. C++ and JavaScript were correct; they do not reconstruct structure from the
        /// CFG this way.</para>
        /// </summary>
        private void EmitIfArmTerminator(BasicBlock armBlock)
        {
            var terminator = armBlock.Instructions.LastOrDefault();
            if (terminator is IRConditionalBranch cond)
                HandleConditionalBranch(cond);
            else if (terminator is IRSwitch switchInst)
                HandleSwitchStatement(switchInst);
            else if (terminator is IRBranch branch)
            {
                // Check if this is a break (branch to loop end)
                if (!TryEmitLoopExit(branch) && !_processedBlocks.Contains(branch.Target))
                    HandleUnconditionalBranch(branch);
            }
        }

        /// <summary>
        /// The terminator of the block that CONTINUES after a structured construct — an If's
        /// merge block, a loop's, Try's or For Each's end block.
        ///
        /// <para>⛔ Without the IRSwitch arm, a <c>Select Case</c> placed AFTER any of those
        /// constructs was dropped with everything following it. MEASURED on master: an
        /// <c>If … End If</c> followed by <c>Select Case n</c> compiled to the If alone — in a
        /// plain Sub, no Try involved — and the same after For, While, Try and For Each.</para>
        /// </summary>
        private void EmitContinuationTerminator(BasicBlock continuationBlock)
        {
            var terminator = continuationBlock.Instructions.LastOrDefault();
            if (terminator is IRConditionalBranch cond)
                HandleConditionalBranch(cond);
            else if (terminator is IRSwitch switchInst)
                HandleSwitchStatement(switchInst);
            else if (terminator is IRBranch branch)
                HandleUnconditionalBranch(branch);
        }

        private bool IsLoopEndBlock(BasicBlock block)
        {
            if (block == null || _loopEndBlocks.Count == 0)
                return false;
            return _loopEndBlocks.Contains(block);
        }

        // ====================================================================
        // LEAVING A LOOP. Every `Exit For` / `Exit While` / `Exit Do` in the language arrives
        // here as an IRBranch to the enclosing loop's end block, and there are only three
        // questions: is this branch an exit at all, is `break` the right C# for it, and where
        // does the label go when it is not.
        // ====================================================================

        /// <summary>Fresh loop-exit bookkeeping for one method/accessor body.</summary>
        private void ResetLoopExitState()
        {
            _forEachEndBlocks = new HashSet<BasicBlock>();
            _loopSwitchDepths = new Stack<int>();
            _labelledLoopEnds = new HashSet<BasicBlock>();
            _switchDepth = 0;
        }

        /// <summary>
        /// The C# label placed after a loop whose exit could not be spelled <c>break</c>.
        /// Derived from the IR block name, which <c>IRBuilder</c> already makes unique per
        /// function (<c>foreach0.end</c>, <c>for1.end</c>, …).
        /// </summary>
        private static string LoopExitLabel(BasicBlock endBlock) =>
            "__exit_" + new string((endBlock.Name ?? "loop")
                .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

        /// <summary>
        /// Emit the jump that LEAVES the loop <paramref name="endBlock"/> ends.
        ///
        /// <para>⛔ <c>break</c> is not always it. Inside a C# <c>switch</c>, <c>break</c> leaves
        /// the SWITCH — measured on the pre-existing counted-<c>For</c> path, where
        /// <c>Exit For</c> in a <c>Select Case</c> over 1..4 totalled <b>7</b> instead of
        /// <b>3</b> from a program that compiled, ran and exited 0. A <c>goto</c> to a label
        /// after the loop is the only spelling C# has that means "leave the loop" from inside a
        /// switch, and it is emitted ONLY in that case so the ordinary loops keep reading as
        /// ordinary loops.</para>
        /// </summary>
        private void EmitLoopExit(BasicBlock endBlock)
        {
            if (_loopSwitchDepths.Count > 0 && _switchDepth > _loopSwitchDepths.Peek())
            {
                _labelledLoopEnds.Add(endBlock);
                WriteLine($"goto {LoopExitLabel(endBlock)};");
                return;
            }

            WriteLine("break;");
        }

        /// <summary>
        /// Handle <paramref name="branch"/> if it targets an enclosing loop's end block, and say
        /// whether it did — so every caller that owns an <c>IRBranch</c> terminator can ask the
        /// one question before falling back to its own fall-through handling.
        ///
        /// <para>⛔ A <c>For Each</c>'s end block is the target of BOTH its <c>Exit For</c> and
        /// its ordinary end-of-iteration branch, so this returns <c>true</c> (handled) for both
        /// and emits nothing for the second. <c>IRBranch.IsLoopExit</c> is the only thing that
        /// separates them — see <see cref="_forEachEndBlocks"/>.</para>
        /// </summary>
        private bool TryEmitLoopExit(IRBranch branch)
        {
            var target = branch?.Target;
            if (target == null) return false;

            if (_forEachEndBlocks.Contains(target))
            {
                if (branch.IsLoopExit) EmitLoopExit(target);
                return true;
            }

            // A while-shaped loop's body ends by branching to its CONDITION block, so a branch
            // to its end block is always a real exit and needs no flag to prove it.
            if (IsLoopEndBlock(target))
            {
                EmitLoopExit(target);
                return true;
            }

            return false;
        }

        /// <summary>Write the <c>goto</c> target for a loop that needed one, after its closing brace.</summary>
        private void EmitLoopExitLabelIfNeeded(BasicBlock endBlock)
        {
            if (endBlock == null || !_labelledLoopEnds.Remove(endBlock)) return;
            // A label must be followed by a statement; the empty one is the smallest.
            WriteLine($"{LoopExitLabel(endBlock)}: ;");
        }

        private bool ShouldEmitInstruction(IRInstruction instruction)
        {
            // ADR-0001's arm: GetUseCount(v) > 1 && !IsReplicable(v) -> a declared local. Decided
            // once per function in ComputeMaterialisedTemps; first, because every arm below would
            // otherwise inline it (or, for a used call, emit nothing) and let each use evaluate it.
            if (instruction is IRValue materialisedValue && _materialised.Contains(materialisedValue))
                return true;

            // Non-values are usually control-flow or statements and should be emitted
            if (instruction is IRReturn or IRBranch or IRConditionalBranch or IRSwitch or IRLabel)
                return true;

            if (instruction is IRComment)
                return _options.GenerateComments;

            if (instruction is IRStore or IRAssignment)
                return true;

            if (instruction is IRAlloca or IRPhi)
                return false;

            // Awaits whose result is consumed are inlined into the consuming expression;
            // awaits assigned to a declared variable or with an unused result must be
            // emitted as statements for their side effects
            if (instruction is IRAwait awaitValue)
                return IsNamedDestination(awaitValue) || GetUseCount(awaitValue) == 0;

            // IRArrayAlloc must always be emitted because IRArrayStore depends on it
            if (instruction is IRArrayAlloc)
                return true;

            if (instruction is IRCall call)
            {
                // void calls are statements
                var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

                // If the IRCall is explicitly named as a declared variable destination, emit assignment statement
                if (IsNamedDestination(call))
                    return true;

                // If the result is unused, emit as a statement call (for side effects)
                if (!hasReturn || GetUseCount(call) == 0)
                    return true;

                // Otherwise, we inline it into expressions (no temp locals)
                return false;
            }

            if (instruction is IRInstanceMethodCall methodCall)
            {
                // void method calls are statements
                var hasReturn = methodCall.Type != null && !methodCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

                // If the result is unused, emit as a statement call (for side effects)
                if (!hasReturn || GetUseCount(methodCall) == 0)
                    return true;

                // Otherwise, we inline it into expressions (no temp locals)
                return false;
            }

            if (instruction is IRValue v)
            {
                // Only emit expression-producing values when they represent an assignment
                // to a real declared variable (local/param/global). Otherwise inline.
                return IsNamedDestination(v);
            }

            return true;
        }

        private int GetUseCount(IRValue value) => _useCounts.TryGetValue(value, out var c) ? c : 0;

        /// <summary>
        /// ADR-0001's materialisation decision for <paramref name="function"/>: every value
        /// instruction with <c>GetUseCount(v) &gt; 1 &amp;&amp; !IsReplicable(v)</c> becomes a declared
        /// local, written once and read by name. Use count &lt;= 1, or replicable, keeps today's
        /// inlining — that is the whole truth table, and it must not be widened (ADR-0001's trap:
        /// C# re-emitting a replicable expression is what keeps it right across a bad CSE merge).
        ///
        /// <para>Not candidates, each for a stated reason:</para>
        /// <list type="bullet">
        /// <item>A value named after a DECLARED variable. Materialising it would not create a
        /// temp: its "temp" name IS the variable, so every use would read the variable's CURRENT
        /// value rather than the value computed. CSE produces exactly that use pattern — it merges
        /// later copies of an expression onto a binop named after a variable, and does not kill
        /// the entry when that variable is reassigned (task #125). With a NON-replicable operand
        /// (a non-Const global, or a ByRef parameter) such a binop is otherwise a candidate:
        /// <c>Dim a = g + q</c> / <c>a = z</c> / <c>l(0) = g + q</c> / <c>l(1) = g + q</c> (g a
        /// module global) prints <c>3,3,0</c> with this exclusion and <c>0,0,0</c> without it, on
        /// all three entry points. Re-emitting the expression is what keeps the C# oracle right
        /// there (C++, JS and MSIL print 0,0,0 regardless — that is the CSE defect). C4
        /// (<c>p + q</c>, two locals) does NOT exercise this: its value is replicable and never
        /// a candidate.
        /// <para>⚠ This test is on SPELLING: an IR temp that happens to be spelled like a user
        /// local (<c>Dim t0</c> — task #126) is also excluded, and is then both assigned to the
        /// user's variable and inlined at every use. ADR-0004 D3's reservation removes that case;
        /// until then T2 stays wrong either way.</para></item>
        /// <item>A value defined in a LOOP-CONDITION block. That block's instructions are written
        /// once, before the <c>while</c>, and its condition is re-emitted as text each iteration —
        /// a local written once would freeze the condition. Such a value stays inlined, so a
        /// multi-use non-replicable one there is still evaluated per use; the optimizer gate on
        /// the rewrite that creates it (ADR-0004 D4, step 3) is what closes that case.</item>
        /// <item>No name, no type, or a void type — nothing to declare.</item>
        /// <item>Kinds that already declare themselves or are never emitted as values
        /// (<see cref="IRArrayAlloc"/>, <see cref="IRAlloca"/>, <see cref="IRPhi"/>,
        /// <see cref="IRVariable"/>, <see cref="IRConstant"/>) and <see cref="IRTupleElement"/>,
        /// whose consecutive runs are emitted as one deconstruction.</item>
        /// </list>
        /// <para>⚠ Temp NAMES are the IR's own (<c>t0</c>, …). A user local of the same spelling
        /// collides; ADR-0004 D3 fixes that in the IR by reservation, not here.</para>
        /// </summary>
        private void ComputeMaterialisedTemps(IRFunction function)
        {
            _materialised.Clear();
            _emittingDefinition.Clear();

            var loopConditionBlocks = new HashSet<BasicBlock>();
            foreach (var block in function.Blocks)
            {
                if (block.Instructions.LastOrDefault() is IRConditionalBranch cb
                    && cb.TrueTarget != null && cb.FalseTarget != null
                    && IsLoopHeader(cb.TrueTarget, cb.FalseTarget, out _, out _, out _, out _, out _))
                    loopConditionBlocks.Add(block);
            }

            foreach (var block in function.Blocks)
            {
                if (loopConditionBlocks.Contains(block)) continue;

                foreach (var instr in block.Instructions)
                {
                    if (instr is not IRValue v) continue;
                    if (v is IRArrayAlloc or IRAlloca or IRPhi or IRVariable or IRConstant or IRTupleElement) continue;
                    if (string.IsNullOrEmpty(v.Name) || v.Type == null
                        || v.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsNamedDestination(v)) continue;
                    if (GetUseCount(v) <= 1) continue;
                    if (IsReplicableHere(v)) continue;
                    _materialised.Add(v);
                }
            }
        }

        /// <summary>
        /// <see cref="IRReplicability.IsReplicable(IRValue, Func{IRValue, bool?})"/> as this backend
        /// sees an operand: an already-materialised value is a local (replicable), and an
        /// <see cref="IRVariable"/> that merely NAMES a temp is judged by the temp's definition,
        /// because <see cref="EmitExpression(IRValue)"/> inlines that definition in its place.
        /// </summary>
        private bool IsReplicableHere(IRValue value)
        {
            var visiting = new HashSet<IRValue>();
            bool? Override(IRValue v)
            {
                if (_materialised.Contains(v)) return true;
                if (v is IRVariable tempRef && !string.IsNullOrEmpty(tempRef.Name)
                    && !_declaredIdentifiers.Contains(tempRef.Name) && !tempRef.IsParameter && !tempRef.IsGlobal
                    && !_currentClassMemberNames.Contains(tempRef.Name)
                    && _tempDefsByName.TryGetValue(tempRef.Name, out var def) && !ReferenceEquals(def, v))
                {
                    if (!visiting.Add(def)) return false; // a cycle is not provably anything
                    return IRReplicability.IsReplicable(def, Override);
                }
                return null;
            }
            return IRReplicability.IsReplicable(value, Override);
        }

        /// <summary>The materialised temps defined in <paramref name="function"/>'s own blocks, in order.</summary>
        private IEnumerable<IRValue> MaterialisedTempsOf(IRFunction function) =>
            function.Blocks.SelectMany(b => b.Instructions).OfType<IRValue>().Where(_materialised.Contains);

        /// <summary>
        /// The ONE place a function body's locals are declared: the function's own locals, then
        /// every temp <see cref="ComputeMaterialisedTemps"/> decided to materialise, each typed
        /// from the IR. Replaces five hand-copied loops, and is also what a property accessor
        /// calls — it used to declare nothing.
        /// </summary>
        private void DeclareLocals(IRFunction function, bool sizedArrays, bool blankLineAfter = true)
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var any = false;

            foreach (var localVar in function.LocalVariables)
            {
                var varName = GetValueName(localVar);
                any = true;
                if (declared.Add(varName))
                {
                    var csharpType = MapType(localVar.Type);
                    var defaultValue = (sizedArrays ? SizedArrayInitializer(localVar.Type) : null)
                                       ?? GetDefaultValue(localVar.Type);
                    WriteLine($"{csharpType} {varName} = {defaultValue};");
                }
            }

            foreach (var temp in MaterialisedTempsOf(function))
            {
                var tempName = GetValueName(temp);
                any = true;
                if (declared.Add(tempName))
                    WriteLine($"{MapType(temp.Type)} {tempName} = {GetDefaultValue(temp.Type)};");
            }

            if (any && blankLineAfter)
                WriteLine();
        }

        /// <summary>
        /// Writes <c>tN = &lt;definition&gt;;</c> for a materialised temp — the single place its
        /// defining expression text is emitted.
        /// </summary>
        private void EmitMaterialisedDefinition(IRValue value)
        {
            _emittingDefinition.Add(value);
            try
            {
                WriteLine($"{GetValueName(value)} = {EmitExpression(value)};");
            }
            finally
            {
                _emittingDefinition.Remove(value);
            }
        }

        /// <summary>Render explicit generic type arguments as "&lt;T1, T2&gt;", or "" if none.</summary>
        private string FormatGenericArgs(List<TypeInfo> genericArgs)
        {
            if (genericArgs == null || genericArgs.Count == 0) return "";
            return "<" + string.Join(", ", genericArgs.Select(MapType)) + ">";
        }

        private bool IsNamedDestination(IRValue value)
        {
            if (value == null) return false;
            if (string.IsNullOrEmpty(value.Name)) return false;
            if (_declaredIdentifiers.Contains(value.Name)) return true;

            // A class member is a destination ONLY for a value IRBuilder actually RENAMED after
            // it. Found by review: matching every value by name made an ordinary temp `t0` in
            // a class with a field `t0` a write to that field, and a call temp was emitted as
            // `t0 = Bump();` and then inlined AGAIN at its use. A local or parameter of the same
            // name shadows a member in C# exactly as it does in BasicLang.
            return value.NamedAfterVariable && _currentClassMemberNames.Contains(value.Name);
        }

        /// <summary>
        /// The module whose static class is being emitted, or null outside every module class:
        /// inside a class body (its methods, constructors, accessors and field initializers),
        /// an interface, a struct. Set around each module class in <see cref="Generate"/>.
        ///
        /// <para>⛔ This is what decides whether a module member is spelled bare or qualified,
        /// and it used to be decided by comparing MODULE NAMES — the member's against the
        /// emitting function's. A class's methods carry the file module's name too, so a
        /// file-scope <c>Twice</c> or <c>Total</c> used from a class body compared equal and went
        /// out bare, inside a C# class that has no such member: CS0103, measured for a method,
        /// a constructor, a property getter, a Shared method, a lambda in a method, and a
        /// <c>Module</c> block's procedure (a different static class, the same bare spelling).
        /// A bare name resolves only inside the static class that declares it; nothing about
        /// the function says whether that is where it is being written.</para>
        /// </summary>
        private string _currentModuleClass;

        /// <summary>Whether the text being written lands inside <paramref name="module"/>'s static class.</summary>
        private bool EmittedInsideModuleClass(string module) =>
            !string.IsNullOrEmpty(_currentModuleClass)
            && string.Equals(module, _currentModuleClass, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// <c>Module.Name</c> for a global unless it is being written inside its own module's
        /// static class, where the bare spelling is the only one that resolves a same-named
        /// local first. "Main" is spelled "Program", as the class is.
        /// </summary>
        private string QualifyCrossModuleGlobal(IRVariable global, string spelled)
        {
            if (global == null || string.IsNullOrEmpty(global.ModuleName)) return spelled;
            if (EmittedInsideModuleClass(global.ModuleName)) return spelled;
            return $"{ModuleClassName(global.ModuleName)}.{spelled}";
        }

        /// <summary>
        /// The global a NAMED DESTINATION refers to — a value IRBuilder renamed after its
        /// assignment target (<c>Helpers.Value = Helpers.Value + 1</c> is an IRBinaryOp named
        /// after the global) — or null when the name is not a global's.
        ///
        /// <para>⛔ Such a destination carries only the NAME, not the IRVariable, so the
        /// cross-module qualification in EmitExpression's IRVariable arm never saw it: the write
        /// was spelled bare, <c>Value = Value + 1</c>, inside a class that has no <c>Value</c> —
        /// CS0103 from a build the front end had accepted. Reachable only since a qualified
        /// module member resolves at all; before that the front end refused the shape.</para>
        /// </summary>
        private IRVariable GlobalNamedBy(string irName)
        {
            if (string.IsNullOrEmpty(irName) || _currentModule?.GlobalVariables == null) return null;
            return _currentModule.GlobalVariables.Values.FirstOrDefault(g =>
                g?.IsGlobal == true && string.Equals(g.Name, irName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The static class a Module's members are emitted into ("Main" is "Program").</summary>
        private string ModuleClassName(string moduleName) =>
            moduleName.Equals("Main", StringComparison.OrdinalIgnoreCase) ? "Program" : SanitizeName(moduleName);

        /// <summary>
        /// The C# spelling of a call's target: qualified by the callee's module class unless
        /// the call is being written inside that very class.
        ///
        /// <para>⛔ This backend qualified cross-module VARIABLES and never CALLS, so a bare
        /// <c>Twice(4)</c> from Module M to Helpers' <c>Twice</c> was emitted bare inside
        /// <c>static class M</c>: CS0103, on this backend alone — the three flattening backends
        /// ran it. The owner now arrives on <see cref="IRCall.CalleeModule"/>; the dotted
        /// spelling is still honoured for the names that still carry one (.NET statics).</para>
        /// </summary>
        private string UserCallTarget(IRCall call)
        {
            var name = call.FunctionName ?? string.Empty;
            var spelled = name.Contains(".")
                ? string.Join(".", name.Split('.').Select(SanitizeName))
                : SanitizeName(name);

            if (string.IsNullOrEmpty(call.CalleeModule) || EmittedInsideModuleClass(call.CalleeModule)) return spelled;
            return $"{ModuleClassName(call.CalleeModule)}.{spelled}";
        }

        private string GetValueName(IRValue value)
        {
            if (value is IRConstant constant)
                return EmitConstant(constant);

            // Inside a For Each body whose variable had to be renamed, that name means the LOOP
            // variable — checked before both caches, which map it to the outer local.
            if (_forEachRenames.Count > 0 && value is IRVariable loopRead && loopRead.Name != null
                && _forEachRenames.TryGetValue(loopRead.Name, out var loopName))
                return loopName;

            if (_valueNames.TryGetValue(value, out var name))
                return name;

            // A renamed destination that IS a global of another module: spelled qualified, and
            // cached on THIS instance only — never in _variableNameMap, which the IRVariable arm
            // below shares and which EmitExpression qualifies itself (a shared entry would be
            // qualified twice).
            if (value is not IRVariable && value.NamedAfterVariable && !string.IsNullOrEmpty(value.Name)
                && GlobalNamedBy(value.Name) is IRVariable destinationGlobal)
            {
                var qualified = QualifyCrossModuleGlobal(destinationGlobal, SanitizeName(value.Name));
                if (!string.Equals(qualified, SanitizeName(value.Name), StringComparison.Ordinal))
                {
                    _valueNames[value] = qualified;
                    return qualified;
                }
            }

            if (value is IRVariable variable)
            {
                if (_variableNameMap.TryGetValue(variable.Name, out var mapped))
                {
                    _valueNames[value] = mapped;
                    return mapped;
                }

                name = SanitizeName(variable.Name);
                _variableNameMap[variable.Name] = name;
                _valueNames[value] = name;
                return name;
            }

            if (!string.IsNullOrEmpty(value.Name))
            {
                // Named value: sanitize and cache (this includes IRBinaryOp renamed to a real variable, etc.)
                name = SanitizeName(value.Name);

                if (_variableNameMap.TryGetValue(value.Name, out var mapped))
                    name = mapped;
                else
                    _variableNameMap[value.Name] = name;

                _valueNames[value] = name;
                return name;
            }

            // Unnamed / compiler-temp values should not become locals; but if we end up here,
            // fall back to a stable-ish name to avoid nulls.
            name = "_tmp";
            _valueNames[value] = name;
            return name;
        }

        private string EmitExpression(IRValue value) => EmitExpression(value, new HashSet<IRValue>(), false);


        /// <summary>
        /// Emit an expression, optionally wrapping in parentheses if it's a compound expression used as a sub-expression.
        /// </summary>
        private string EmitExpression(IRValue value, HashSet<IRValue> stack, bool needsParens = false)
        {
            if (value == null) return string.Empty;

            // A materialised temp is READ by name everywhere except the one place its definition
            // is written (EmitMaterialisedDefinition). ADR-0001 E1: its text appears once.
            if (_materialised.Contains(value) && !_emittingDefinition.Contains(value))
                return GetValueName(value);

            // Prevent infinite recursion on weird cyclic graphs
            if (!stack.Add(value))
                return GetValueName(value);

            try
            {
                switch (value)
                {
                    case IRConstant c:
                        return EmitConstant(c);

                    case IRVariable v:
                        // Check if this is a lambda reference
                        if (v.Name != null && v.Name.StartsWith("__lambda_"))
                        {
                            // Find the lambda function in the module
                            var lambdaFunc = _currentModule?.Functions.FirstOrDefault(f => f.Name == v.Name);
                            if (lambdaFunc != null && lambdaFunc.IsLambda)
                            {
                                return GenerateLambdaExpression(lambdaFunc);
                            }
                        }

                        // If it's a real variable (or a member of the class being generated),
                        // use its name; if it's a temp "register", try to inline its defining value.
                        if (_declaredIdentifiers.Contains(v.Name) || v.IsParameter || v.IsGlobal ||
                            _currentClassMemberNames.Contains(v.Name))
                        {
                            var varName = GetValueName(v);

                            // Check if this global is from a different module and needs qualification
                            if (v.IsGlobal)
                                return QualifyCrossModuleGlobal(v, varName);
                            return varName;
                        }

                        if (!string.IsNullOrEmpty(v.Name) && _tempDefsByName.TryGetValue(v.Name, out var def))
                            return EmitExpression(def, stack, needsParens);

                        return GetValueName(v);

                    case IRBinaryOp bin:
                    {
                        // Sub-expressions need parens to preserve precedence
                        var left = EmitExpression(bin.Left, stack, true);
                        var right = EmitDivisor(bin, EmitExpression(bin.Right, stack, true));
                        var op = MapBinaryOperator(bin.Operation);
                        var narrowed = NarrowArithmetic(bin, $"{left} {op} {right}");
                        if (narrowed != null) return narrowed;
                        var expr = $"{left} {op} {right}";
                        return needsParens ? $"({expr})" : expr;
                    }

                    case IRUnaryOp un:
                    {
                        var operand = EmitExpression(un.Operand, stack, true);
                        var op = MapUnaryOperator(un.Operation);
                        var narrowed = NarrowUnary(un, $"{op}{operand}");
                        if (narrowed != null) return narrowed;
                        var expr = $"{op}{operand}";
                        return needsParens ? $"({expr})" : expr;
                    }

                    case IRCompare cmp:
                    {
                        var left = EmitExpression(cmp.Left, stack, true);
                        var right = EmitExpression(cmp.Right, stack, true);
                        var op = MapCompareOperator(cmp.Comparison);
                        var expr = $"{left} {op} {right}";
                        return needsParens ? $"({expr})" : expr;
                    }

                    case IRCall call:
                    {
                        var argExprs = call.Arguments.Select(a => EmitExpression(a, stack, false)).ToArray();

                        // Invoke a delegate value directly: (calleeExpr)(args), e.g. f(a)(b)
                        if (call.CalleeValue != null)
                        {
                            var calleeExpr = EmitExpression(call.CalleeValue, stack, true);
                            return $"{calleeExpr}({string.Join(", ", argExprs)})";
                        }

                        // Check if this is a standard library function
                        if (StdLibCanHandle(call.FunctionName))
                        {
                            // Add required imports
                            foreach (var import in StdLibGetRequiredImports(call.FunctionName))
                            {
                                _usings.Add(import);
                            }
                            return StdLibEmitCall(call.FunctionName, argExprs);
                        }

                        // Handle qualified names (e.g., "ClassName.MethodName") by sanitizing each part
                        var fn = UserCallTarget(call);
                        var args = string.Join(", ", argExprs);
                        return $"{fn}{FormatGenericArgs(call.GenericArguments)}({args})";
                    }

                    case IRLoad load:
                        return EmitExpression(load.Address, stack, needsParens);

                    case IRGetElementPtr gep:
                    {
                        var baseExpr = EmitExpression(gep.BasePointer, stack, false);
                        var indices = string.Join(", ", gep.Indices.Select(i => EmitExpression(i, stack, false)));
                        return $"{baseExpr}[{indices}]";
                    }

                    case IRIndexerAccess indexer:
                    {
                        var collectionExpr = EmitExpression(indexer.Collection, stack, false);
                        var indices = string.Join(", ", indexer.Indices.Select(i => EmitExpression(i, stack, false)));
                        return $"{collectionExpr}[{indices}]";
                    }

                    case IRCast cast:
                    {
                        var expr = EmitExpression(cast.Value, stack, false);
                        return EmitCastText(cast, expr);
                    }

                    case IRAwait awaitVal:
                    {
                        // Emit the await expression inline
                        string innerExpr;
                        if (awaitVal.Expression is IRCall call)
                        {
                            var argExprs = call.Arguments.Select(a => EmitExpression(a, stack, false)).ToArray();

                            // Check if this is a standard library function
                            if (StdLibCanHandle(call.FunctionName))
                            {
                                foreach (var import in StdLibGetRequiredImports(call.FunctionName))
                                {
                                    _usings.Add(import);
                                }
                                innerExpr = StdLibEmitCall(call.FunctionName, argExprs);
                            }
                            else
                            {
                                // Preserve dots in qualified names like Task.Delay
                                var fn = string.Join(".", call.FunctionName.Split('.').Select(SanitizeName));
                                var args = string.Join(", ", argExprs);
                                innerExpr = $"{fn}({args})";
                            }
                        }
                        else
                        {
                            innerExpr = EmitExpression(awaitVal.Expression, stack, false);
                        }
                        return $"await {innerExpr}";
                    }

                    case IRNewObject newObj:
                    {
                        // Use MapType to get the full type including generic arguments
                        var typeName = MapType(newObj.Type);
                        var argExprs = newObj.Arguments.Select(a => EmitExpression(a, stack, false)).ToArray();
                        var args = string.Join(", ", argExprs);
                        return $"new {typeName}({args})";
                    }

                    case IRInstanceMethodCall methodCall:
                    {
                        var obj = EmitExpression(methodCall.Object, stack, false);
                        var methodName = SanitizeName(methodCall.MethodName);
                        var argExprs = methodCall.Arguments.Select(a => EmitExpression(a, stack, false)).ToArray();
                        var args = string.Join(", ", argExprs);
                        return $"{obj}.{methodName}{FormatGenericArgs(methodCall.GenericArguments)}({args})";
                    }

                    case IRBaseMethodCall baseCall:
                    {
                        var methodName = SanitizeName(baseCall.MethodName);
                        var argExprs = baseCall.Arguments.Select(a => EmitExpression(a, stack, false)).ToArray();
                        var args = string.Join(", ", argExprs);
                        return $"base.{methodName}({args})";
                    }

                    case IRFieldAccess fieldAccess:
                    {
                        var obj = EmitExpression(fieldAccess.Object, stack, false);
                        var fieldName = SanitizeName(fieldAccess.FieldName);
                        return RequiresNativeBclIntCast(fieldAccess)
                            ? $"(int)({obj}.{fieldName})"
                            : $"{obj}.{fieldName}";
                    }

                    case IRTupleElement tupleElem:
                    {
                        var tuple = EmitExpression(tupleElem.Tuple, stack, false);
                        // Access tuple element using Item1, Item2, etc. (1-based indexing)
                        return $"{tuple}.Item{tupleElem.Index + 1}";
                    }

                    case IRAlloca alloca:
                    {
                        // IRBuilder sometimes uses <name>_addr as an address placeholder.
                        // In C#, treat it as just <name>.
                        if (!string.IsNullOrEmpty(alloca.Name) &&
                            alloca.Name.EndsWith("_addr", StringComparison.OrdinalIgnoreCase))
                        {
                            var baseName = alloca.Name.Substring(0, alloca.Name.Length - "_addr".Length);
                            return SanitizeName(baseName);
                        }
                        return SanitizeName(alloca.Name);
                    }

                    default:
                        // If it's a named destination, use it; otherwise try defs-by-name.
                        if (!string.IsNullOrEmpty(value.Name) && !_declaredIdentifiers.Contains(value.Name) &&
                            _tempDefsByName.TryGetValue(value.Name, out var def2) && !ReferenceEquals(def2, value))
                        {
                            return EmitExpression(def2, stack, needsParens);
                        }
                        return GetValueName(value);
                }
            }
            finally
            {
                stack.Remove(value);
            }
        }

        /// <summary>
        /// Every <see cref="IRValue"/> an instruction READS — the use-count walker behind
        /// <see cref="AnalyzeUseCounts"/>.
        ///
        /// <para>⛔ TOTAL OVER IR NODE KINDS, and the default arm THROWS (ADR-0001). A missing arm
        /// is not an absent feature: it is a silently-zero use count, and a zero-use call is
        /// emitted as a statement by <see cref="ShouldEmitInstruction"/> AND inlined again by the
        /// consumer that reads it. Measured before the arms below existed: <c>b.V = Tag()</c>
        /// (IRFieldStore) and <c>Throw MakeEx()</c> (IRThrow) each called their function TWICE on
        /// the reference backend, and a <c>For Each</c> over a call had the same shape waiting
        /// behind its CS0103. A new node kind must get an arm here, even an empty one.</para>
        /// </summary>
        private IEnumerable<IRValue> GetOperands(IRInstruction instr)
        {
            switch (instr)
            {
                case IRBinaryOp bin:
                    return new[] { bin.Left, bin.Right };
                case IRUnaryOp un:
                    return new[] { un.Operand };
                case IRCompare cmp:
                    return new[] { cmp.Left, cmp.Right };
                case IRCast cast:
                    return new[] { cast.Value };
                case IRCall call:
                    return call.CalleeValue != null
                        ? call.Arguments.Concat(new[] { call.CalleeValue })
                        : call.Arguments;
                case IRInstanceMethodCall methodCall:
                    // The receiver (Object) is a use — chain intermediates like
                    // a.B() feeding .C() must count so they aren't also emitted
                    // as standalone (double-executing) statements.
                    return new[] { methodCall.Object }.Concat(methodCall.Arguments);
                case IRBaseMethodCall baseMethodCall:
                    return baseMethodCall.Arguments;
                case IRNewObject newObject:
                    return newObject.Arguments;
                case IRIndexerAccess indexerAccess:
                    return new[] { indexerAccess.Collection }.Concat(indexerAccess.Indices);
                case IRIndexerStore indexerStore:
                    return new[] { indexerStore.Collection }
                        .Concat(indexerStore.Indices)
                        .Append(indexerStore.Value);
                case IRFieldAccess fieldAccess:
                    return new[] { fieldAccess.Object };
                case IRAssignment asg:
                    return new[] { asg.Value, asg.Target };
                case IRLoad load:
                    return new[] { load.Address };
                case IRStore store:
                    return new[] { store.Address, store.Value };
                case IRReturn ret:
                    return ret.Value != null ? new[] { ret.Value } : Array.Empty<IRValue>();
                case IRConditionalBranch br:
                    return new[] { br.Condition };
                case IRSwitch sw:
                {
                    var switchOps = new List<IRValue> { sw.Value };
                    if (sw.Cases != null)
                        switchOps.AddRange(sw.Cases.Select(c => c.CaseValue));
                    if (sw.PatternCases != null)
                        foreach (var patternCase in sw.PatternCases)
                            AddPatternOperands(patternCase, switchOps);
                    return switchOps;
                }
                case IRGetElementPtr gep:
                    var ops = new List<IRValue> { gep.BasePointer };
                    ops.AddRange(gep.Indices);
                    return ops;
                case IRPhi phi:
                    return phi.Operands.Select(i => i.Value).ToList();
                case IRTupleElement tupleElem:
                    return new[] { tupleElem.Tuple };
                case IRAwait awaited:
                    return awaited.Expression != null ? new[] { awaited.Expression } : Array.Empty<IRValue>();
                case IRArrayStore arrayStore:
                    // Task 24a. An array-literal element's use must be COUNTED here, or a call
                    // element has use-count 0, ShouldEmitInstruction emits it as a bare statement
                    // and Visit(IRArrayStore) re-renders it inline — measured: a green build that
                    // ran `Bump()` four times for `{Bump(), Bump()}`.
                    return new[] { arrayStore.Array, arrayStore.Index, arrayStore.Value };
                case IRFieldStore fieldStore:
                    return new[] { fieldStore.Object, fieldStore.Value };
                case IRForEach forEach:
                    return new[] { forEach.Collection };
                case IRThrow throwInst:
                    return throwInst.Exception != null ? new[] { throwInst.Exception } : Array.Empty<IRValue>();
                case IRYield yieldInst:
                    return yieldInst.Value != null ? new[] { yieldInst.Value } : Array.Empty<IRValue>();

                // Kinds that read no IRValue. Listed, not defaulted, so the default can throw.
                // ⚠ IRVariable.DefaultValue / InitialValue are DECLARATION data (a parameter's
                // optional default, a module-scope initializer), evaluated outside every function
                // body — excluded exactly as OptimizationPass.ReplaceUses excludes them.
                case IRConstant:
                case IRVariable:
                case IRAlloca:
                case IRArrayAlloc:
                case IRBranch:
                case IRLabel:
                case IRComment:
                case IRInlineCode:
                case IRTryCatch:
                    return Array.Empty<IRValue>();

                default:
                    throw new InvalidOperationException(
                        $"CSharpBackend.GetOperands has no arm for IR node kind '{instr?.GetType().Name ?? "null"}'. "
                        + "Every kind needs one (ADR-0001): a missing arm is a silently-zero use count, which "
                        + "makes a call both a statement and an inlined expression — evaluated twice.");
            }
        }

        /// <summary>The values a Select Case pattern reads — mirrors OptimizationPass.ReplaceUsesInPattern.</summary>
        private static void AddPatternOperands(IRPatternCase patternCase, List<IRValue> operands)
        {
            if (patternCase == null) return;
            if (patternCase.WhenGuard != null) operands.Add(patternCase.WhenGuard);

            switch (patternCase)
            {
                case IRRangePatternCase range:
                    operands.Add(range.LowerBound);
                    operands.Add(range.UpperBound);
                    break;
                case IRComparisonPatternCase comparison:
                    operands.Add(comparison.CompareValue);
                    break;
                case IRConstantPatternCase constant:
                    operands.Add(constant.Value);
                    break;
                case IROrPatternCase or:
                    if (or.Alternatives != null)
                        foreach (var alternative in or.Alternatives)
                            AddPatternOperands(alternative, operands);
                    break;
                case IRTuplePatternCase tuple:
                    if (tuple.Elements != null)
                        foreach (var element in tuple.Elements)
                            AddPatternOperands(element, operands);
                    break;
            }
        }

        // ====================================================================
        // IR Visitor Methods
        // ====================================================================

        public void Visit(IRFunction function) { }
        public void Visit(BasicBlock block) { }
        public void Visit(IRConstant constant) { }
        public void Visit(IRVariable variable) { }

        public void Visit(IRBinaryOp binaryOp)
        {
            if (!IsNamedDestination(binaryOp))
                return;

            // Use needsParens=true for sub-expressions to preserve operator precedence
            var left = EmitExpression(binaryOp.Left, new HashSet<IRValue>(), needsParens: true);
            var right = EmitDivisor(binaryOp, EmitExpression(binaryOp.Right, new HashSet<IRValue>(), needsParens: true));
            var op = MapBinaryOperator(binaryOp.Operation);

            var target = GetValueName(binaryOp);
            WriteLine($"{target} = {NarrowArithmetic(binaryOp, $"{left} {op} {right}") ?? $"{left} {op} {right}"};");
        }

        public void Visit(IRUnaryOp unaryOp)
        {
            if (!IsNamedDestination(unaryOp))
                return;

            // Use needsParens=true for sub-expressions to preserve operator precedence
            var operand = EmitExpression(unaryOp.Operand, new HashSet<IRValue>(), needsParens: true);
            var op = MapUnaryOperator(unaryOp.Operation);

            var target = GetValueName(unaryOp);
            WriteLine($"{target} = {NarrowUnary(unaryOp, $"{op}{operand}") ?? $"{op}{operand}"};");
        }

        public void Visit(IRCompare compare)
        {
            if (!IsNamedDestination(compare))
                return;

            // Use needsParens=true for sub-expressions to preserve operator precedence
            var left = EmitExpression(compare.Left, new HashSet<IRValue>(), needsParens: true);
            var right = EmitExpression(compare.Right, new HashSet<IRValue>(), needsParens: true);
            var op = MapCompareOperator(compare.Comparison);

            var target = GetValueName(compare);
            WriteLine($"{target} = {left} {op} {right};");
        }

        public void Visit(IRAssignment assignment)
        {
            var value = EmitExpression(assignment.Value);
            // Use EmitExpression for target to handle module qualification for imported globals
            var target = EmitExpression(assignment.Target);
            WriteLine($"{target} = {value};");
        }

        public void Visit(IRLoad load)
        {
            if (!IsNamedDestination(load))
                return;

            var address = EmitExpression(load.Address);
            var target = GetValueName(load);
            WriteLine($"{target} = {address};");
        }

        public void Visit(IRStore store)
        {
            var value = EmitExpression(store.Value);

            // Array element store
            if (store.Address is IRGetElementPtr gep)
            {
                var baseExpr = EmitExpression(gep.BasePointer);
                var indices = string.Join(", ", gep.Indices.Select(EmitExpression));
                WriteLine($"{baseExpr}[{indices}] = {value};");
                return;
            }

            var address = EmitExpression(store.Address);
            WriteLine($"{address} = {value};");
        }

        public void Visit(IRCall call)
        {
            var functionName = call.FunctionName;

            // RaiseEvent X(args) arrives as a call named raise_X (IRBuilder's convention).
            // MEASURED before this arm: emitted verbatim, CS0103 — nothing defined raise_X, so
            // no event had ever been raised on this backend. The event IS the delegate here:
            // invoke it if anyone subscribed. Only for an event of the class being generated,
            // so a user function that happens to be called raise_Foo is left alone.
            if (functionName != null && functionName.StartsWith("raise_", StringComparison.Ordinal) &&
                _currentClassEvents.TryGetValue(functionName.Substring("raise_".Length), out var raisedEvent))
            {
                var raiseArgs = string.Join(", ", call.Arguments.Select(a => EmitExpression(a)));
                WriteLine($"{SanitizeName(raisedEvent.Name)}?.Invoke({raiseArgs});");
                return;
            }

            // Handle event subscription: Delegate.Combine -> +=
            if (functionName == "Delegate.Combine" && call.Arguments.Count >= 2)
            {
                var eventExpr = EmitExpression(call.Arguments[0]);
                var handlerExpr = EmitExpression(call.Arguments[1]);
                WriteLine($"{eventExpr} += {handlerExpr};");
                return;
            }

            // Handle event unsubscription: Delegate.Remove -> -=
            if (functionName == "Delegate.Remove" && call.Arguments.Count >= 2)
            {
                var eventExpr = EmitExpression(call.Arguments[0]);
                var handlerExpr = EmitExpression(call.Arguments[1]);
                WriteLine($"{eventExpr} -= {handlerExpr};");
                return;
            }

            // Format arguments, adding the by-reference modifier for ByRef parameters.
            // P2a-2 Task 9 (Task-8 review I5): a resolved .NET target records the CLR ref-KIND
            // in NetArgumentRefKinds, because `ByRefArguments` is a List<bool> and cannot tell
            // `ref` from `out` — emitting `ref x` for an `out` parameter is CS1620. A VB ByRef
            // argument records nothing there and keeps `ref`, which is VB's only form. `in` /
            // `RefReadOnly` need no modifier at a C# call site.
            var argExprs = call.Arguments.Select((arg, i) =>
            {
                var expr = EmitExpression(arg);
                bool isByRef = call.ByRefArguments != null && i < call.ByRefArguments.Count && call.ByRefArguments[i];
                if (!isByRef) return expr;

                var refKind = call.NetArgumentRefKinds != null && i < call.NetArgumentRefKinds.Count
                    ? call.NetArgumentRefKinds[i]
                    : BasicLang.Net.NetRefKind.Ref;
                return refKind switch
                {
                    BasicLang.Net.NetRefKind.Out => $"out {expr}",
                    BasicLang.Net.NetRefKind.In or BasicLang.Net.NetRefKind.RefReadOnly => expr,
                    _ => $"ref {expr}",
                };
            }).ToArray();

            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Invoke a delegate value directly: (calleeExpr)(args), e.g. f(a)(b)
            if (call.CalleeValue != null)
            {
                var calleeExpr = EmitExpression(call.CalleeValue, new HashSet<IRValue>(), true);
                var invocation = $"{calleeExpr}({string.Join(", ", argExprs)})";
                if (hasReturn && IsNamedDestination(call))
                {
                    WriteLine($"{GetValueName(call)} = {invocation};");
                }
                else if (!hasReturn || GetUseCount(call) == 0)
                {
                    WriteLine($"{invocation};");
                }
                return;
            }

            // Check if this is an extern function call
            if (_currentModule != null && _currentModule.IsExtern(functionName))
            {
                var externDecl = _currentModule.GetExtern(functionName);
                if (externDecl != null && externDecl.HasImplementation("CSharp"))
                {
                    var impl = externDecl.GetImplementation("CSharp");
                    var argsStr = string.Join(", ", argExprs);

                    // Format: implementation string may contain {0}, {1} placeholders
                    // Or it may be a direct method call like "System.IO.File.ReadAllText"
                    string externCall;
                    if (impl.Contains("{"))
                    {
                        externCall = string.Format(impl, argExprs);
                    }
                    else
                    {
                        externCall = $"{impl}({argsStr})";
                    }

                    if (hasReturn && IsNamedDestination(call))
                    {
                        var target = GetValueName(call);
                        WriteLine($"{target} = {externCall};");
                        return;
                    }

                    WriteLine($"{externCall};");
                    return;
                }
            }

            // Check if this is a standard library function
            if (StdLibCanHandle(functionName))
            {
                var stdLibCall = StdLibEmitCall(functionName, argExprs);

                // Add required imports
                foreach (var import in StdLibGetRequiredImports(functionName))
                {
                    _usings.Add(import);
                }

                if (hasReturn && IsNamedDestination(call))
                {
                    var target = GetValueName(call);
                    WriteLine($"{target} = {stdLibCall};");
                    return;
                }

                // Emit as statement (for void functions like Print)
                WriteLine($"{stdLibCall};");
                return;
            }

            // Regular function call
            var args = string.Join(", ", argExprs);
            // Handle qualified names (e.g., "ClassName.MethodName") by sanitizing each part
            var sanitizedName = UserCallTarget(call);
            sanitizedName += FormatGenericArgs(call.GenericArguments);

            // If this call is explicitly targeted at a declared variable, emit assignment.
            if (hasReturn && IsNamedDestination(call))
            {
                var target = GetValueName(call);
                WriteLine($"{target} = {sanitizedName}({args});");
                return;
            }

            // Otherwise emit as statement when result unused / void
            if (!hasReturn || GetUseCount(call) == 0)
            {
                WriteLine($"{sanitizedName}({args});");
                return;
            }

            // If we got here, this call should have been inlined by EmitExpression.
            // Do nothing to avoid creating temps.
        }

        public void Visit(IRReturn ret)
        {
            // Iterator functions should not have return statements - they use yield break/yield return
            if (_currentFunction?.IsIterator == true)
            {
                // Skip returns in iterator functions - they end naturally or via yield break
                return;
            }

            if (ret.Value != null)
            {
                var value = EmitExpression(ret.Value);
                WriteLine($"return {value};");
            }
            else
            {
                // Skip unnecessary "return;" at end of void methods
                // Check if this is the last instruction in a void function
                bool isVoidFunction = _currentFunction?.ReturnType == null ||
                    _currentFunction.ReturnType.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

                // ⛔ THIS TEST USED TO BE `ret.ParentBlock?.Successors?.Count == 0`, WHICH IS
                // TRUE OF EVERY RETURN. A return terminates its block, so NO return block has
                // successors — early or not — and the suppression therefore swallowed EVERY
                // void return. `Exit Sub` was a complete no-op: measured, a Sub that prints "5"
                // and exits, then prints "-1", printed BOTH on C# where C++, JavaScript and
                // MSIL all print "5".
                //
                // Suppressing the ONE trailing return of a void method is still worth having,
                // and the question it really asks is "will anything else be emitted after this
                // point?". ⚠ It is NOT "is this the last block in IRFunction.Blocks": creation
                // order is not emission order — an `If` inside a `For Each` body creates
                // `if0.then`/`if0.end` AFTER `foreach0.end`, so the block carrying the trailing
                // return is followed in the list by two blocks emitted long before it. The set
                // of blocks ALREADY EMITTED answers the real question directly.
                //
                // ⚠ INVARIANT RELIED ON: a return is the LAST instruction of its block, so
                // suppressing it cannot strand statements behind it. Measured over 45 programs,
                // optimized AND unoptimized, including `Exit Sub` with a statement written after
                // it (the front end drops the unreachable statement and the block still ends at
                // the return). A guard for it was written and then removed: nothing can reach it,
                // and an unreachable guard is an unkillable mutant, not insurance.
                bool nothingLeftToEmit = isVoidFunction
                    && _currentFunction?.Blocks != null
                    && _currentFunction.Blocks.All(b => _processedBlocks.Contains(b));

                if (nothingLeftToEmit)
                {
                    // Don't emit unnecessary return at end of void method
                    return;
                }

                WriteLine("return;");
            }
        }

        public void Visit(IRThrow throwInst)
        {
            if (throwInst.Exception == null)
            {
                WriteLine("throw;");
                return;
            }

            var exception = EmitExpression(throwInst.Exception);
            WriteLine($"throw {exception};");
        }

        public void Visit(IRBranch branch)
        {
            // Handled structurally in GenerateStructuredBlock - no direct goto emission
        }

        public void Visit(IRConditionalBranch condBranch)
        {
            // Handled structurally in HandleConditionalBranch - no direct goto emission
        }

        public void Visit(IRSwitch switchInst)
        {
            var value = EmitExpression(switchInst.Value);

            WriteLine($"switch ({value})");
            WriteLine("{");
            Indent();

            foreach (var (caseValue, target) in switchInst.Cases)
            {
                // Case labels must be compile-time constants in C#. We still stringify defensively.
                var caseExpr = EmitExpression(caseValue);
                WriteLine($"case {caseExpr}: goto {target.Name};");
            }

            WriteLine($"default: goto {switchInst.DefaultTarget.Name};");

            Unindent();
            WriteLine("}");
        }

        public void Visit(IRPhi phi)
        {
            // Phi nodes are SSA merge artifacts; in imperative C# emission they should be lowered earlier.
            WriteLine($"// Phi node: {phi.Name}");
        }

        public void Visit(IRAlloca alloca)
        {
            // No-op for C# (locals are declared from LocalVariables; arrays are references already)
        }

        public void Visit(IRGetElementPtr gep)
        {
            if (!IsNamedDestination(gep))
                return;

            var baseExpr = EmitExpression(gep.BasePointer);
            var indices = string.Join(", ", gep.Indices.Select(EmitExpression));
            var target = GetValueName(gep);

            WriteLine($"{target} = {baseExpr}[{indices}];");
        }

        public void Visit(IRCast cast)
        {
            if (!IsNamedDestination(cast))
                return;

            var value = EmitExpression(cast.Value);
            var target = GetValueName(cast);

            WriteLine($"{target} = {EmitCastText(cast, value)};");
        }

        /// <summary>
        /// Render a cast as C# text. TryCast becomes the 'as' operator; String
        /// conversions to/from other types use Convert.*; everything else is a C# cast.
        /// </summary>
        private string EmitCastText(IRCast cast, string valueExpr)
        {
            var targetType = MapType(cast.Type);

            if (cast.IsTryCast)
                return $"({valueExpr} as {targetType})";

            var sourceName = cast.SourceType?.Name;
            var targetName = cast.Type?.Name;

            // String -> numeric/bool/char: a C# cast is invalid, use Convert.*
            if (sourceName == "String" && targetName != null &&
                ConvertMethodForType(targetName) is string convertMethod)
            {
                return $"Convert.{convertMethod}({valueExpr})";
            }

            // Non-String, non-Object -> String: a C# cast is invalid, use Convert.ToString
            if (targetName == "String" && sourceName != null &&
                sourceName != "String" && sourceName != "Object")
            {
                return $"Convert.ToString({valueExpr})";
            }

            // ⛔ A FLOATING -> INTEGRAL narrowing ROUNDS HALF-TO-EVEN, and a plain C# cast does
            // NOT — it truncates. `Dim i As Integer = 7.5` answered 7 on all four backends while
            // `CInt(7.5)` answers 8, so one language gave two answers depending on which syntax
            // reached the same narrowing. VB rounds both. Convert.ToXxx is exactly
            // Math.Round(x, MidpointRounding.ToEven), which is what CInt already lowers to here.
            if (IsFloatingTypeName(sourceName) && targetName != null
                && IsIntegralTypeName(targetName)
                && ConvertMethodForType(targetName) is string narrowing)
            {
                return $"Convert.{narrowing}({valueExpr})";
            }

            return $"({targetType})({valueExpr})";
        }

        /// <summary>A BasicLang floating type, i.e. one a narrowing has something to round from.</summary>
        private static bool IsFloatingTypeName(string typeName) =>
            typeName == "Double" || typeName == "Single";

        /// <summary>A BasicLang integral type, i.e. a narrowing target that rounds.</summary>
        private static bool IsIntegralTypeName(string typeName) => typeName switch
        {
            "Integer" or "UInteger" or "Long" or "ULong" or "Short" or "UShort"
                or "Byte" or "SByte" or "UByte" => true,
            _ => false,
        };

        /// <summary>Convert.* method name for a BasicLang primitive target type, or null.</summary>
        private static string ConvertMethodForType(string typeName) => typeName switch
        {
            "Integer" => "ToInt32",
            "UInteger" => "ToUInt32",
            "Long" => "ToInt64",
            "ULong" => "ToUInt64",
            "Short" => "ToInt16",
            "UShort" => "ToUInt16",
            "Byte" => "ToByte",
            "SByte" => "ToSByte",
            "UByte" => "ToByte",
            "Single" => "ToSingle",
            "Double" => "ToDouble",
            "Boolean" => "ToBoolean",
            "Char" => "ToChar",
            _ => null
        };

        public void Visit(IRLabel label)
        {
            Unindent();
            WriteLine($"{label.Name}:");
            Indent();
        }

        public void Visit(IRComment comment)
        {
            if (_options.GenerateComments)
                WriteLine($"// {comment.Text}");
        }

        public void Visit(IRInlineCode inlineCode)
        {
            if (inlineCode.Language.ToLower() == "csharp")
            {
                // Emit the C# code directly
                WriteLine("// Inline C# code");
                foreach (var line in inlineCode.Code.Split('\n'))
                {
                    WriteLine(line.TrimEnd());
                }
            }
            else
            {
                // For non-C# inline code, emit a comment indicating it's not supported
                WriteLine($"// WARNING: Inline {inlineCode.Language} code not supported in C# backend");
                WriteLine($"// Original code ({inlineCode.Code.Length} chars) was skipped");
            }
        }

        public void Visit(IRArrayAlloc arrayAlloc)
        {
            var elementType = MapType(arrayAlloc.ElementType);
            WriteLine($"var {arrayAlloc.Name} = new {elementType}[{arrayAlloc.Size}];");
        }

        public void Visit(IRArrayStore arrayStore)
        {
            // ⛔ EmitExpression, never GetValueName (see Visit(IRIndexerStore) below). Task 24a's typed
            // array literal (`New Double() {1, i}`) wraps every NON-literal element in an IRCast
            // (CoerceToDeclaredType re-types a literal in place and builds no cast), and a by-name
            // render emitted `t1[1] = t0;` with t0 declared nowhere — CS0103. Measured through the
            // CLI for both a Sub parameter and a local `i`: a local does not hide it, only a literal.
            var arrayName = EmitExpression(arrayStore.Array);
            var indexVal = arrayStore.Index is IRConstant c ? c.Value.ToString() : EmitExpression(arrayStore.Index);
            var valueVal = arrayStore.Value is IRConstant vc ? EmitConstant(vc) : EmitExpression(arrayStore.Value);
            WriteLine($"{arrayName}[{indexVal}] = {valueVal};");
        }

        public void Visit(IRIndexerStore indexerStore)
        {
            // In C#, both List<T> and Dictionary<K,V> writes are `collection[index] = value`
            // (Dictionary's indexer setter inserts-or-updates), so a single form is faithful.
            //
            // ⛔ EmitExpression, never GetValueName, for every operand here and in the IRArrayStore,
            // IRYield and IRForEach visitors (ADR-0001 E2): GetValueName is valid only for a value
            // already declared as a local, and an inlined temp has no declaration — `l(i) = l(i) * 10`,
            // `For Each n In Make()`, `Yield Tag()` and `{Tag(), 2}` all emitted a bare `tN` (CS0103).
            // Inlining is sound only because GetOperands counts these operands; without its arms the
            // call was ALSO emitted as a statement and ran twice. A value named after a declared
            // variable is re-emitted as its expression, exactly as every other consumer (a call
            // argument, an IRStore) does; honouring the name instead made these four sites read a
            // variable that CSE had merged onto and the program had since REASSIGNED (measured:
            // `a = p + q` / `a = Seed(0)` / `l(0) = p + q` stored 0, not 3 — a CSE defect).
            var collection = EmitExpression(indexerStore.Collection);
            var indices = string.Join(", ", indexerStore.Indices.Select(i =>
                i is IRConstant ic ? EmitConstant(ic) : EmitExpression(i)));
            var value = indexerStore.Value is IRConstant vc ? EmitConstant(vc) : EmitExpression(indexerStore.Value);
            WriteLine($"{collection}[{indices}] = {value};");
        }

        public void Visit(IRAwait awaitInst)
        {
            // Only reached for statement-level awaits - see ShouldEmitInstruction.
            // EmitExpression handles the IRAwait case, emitting "await <expr>" inline.
            if (IsNamedDestination(awaitInst))
            {
                // Await assigned directly to a declared variable (Dim r = Await f())
                WriteLine($"{GetValueName(awaitInst)} = {EmitExpression(awaitInst)};");
            }
            else
            {
                WriteLine($"{EmitExpression(awaitInst)};");
            }
        }

        public void Visit(IRYield yieldInst)
        {
            if (yieldInst.IsBreak)
            {
                WriteLine("yield break;");
            }
            else
            {
                var valueVal = yieldInst.Value is IRConstant c ? EmitConstant(c) : EmitExpression(yieldInst.Value);
                WriteLine($"yield return {valueVal};");
            }
        }

        public void Visit(IRNewObject newObj)
        {
            var args = string.Join(", ", newObj.Arguments.Select(EmitExpression));
            var type = MapType(newObj.Type);
            // Use the full type (including generic arguments) for the constructor
            WriteLine($"{type} {newObj.Name} = new {type}({args});");
        }

        public void Visit(IRInstanceMethodCall methodCall)
        {
            var obj = EmitExpression(methodCall.Object);
            var methodName = SanitizeName(methodCall.MethodName);
            var generics = FormatGenericArgs(methodCall.GenericArguments);
            // A ByRef parameter needs `ref` at the call site too, not only on the declaration —
            // without it csc rejects the call outright (CS1620).
            var args = string.Join(", ", methodCall.Arguments.Select((arg, i) =>
            {
                var expr = EmitExpression(arg);
                bool isByRef = methodCall.ByRefArguments != null
                               && i < methodCall.ByRefArguments.Count
                               && methodCall.ByRefArguments[i];
                return isByRef ? $"ref {expr}" : expr;
            }));

            // Emit as statement (no assignment) if:
            // - Type is null or Void
            // - Name is null (no temp var was assigned)
            // - Result is unused (GetUseCount == 0)
            // This handles .NET method calls where we don't know the actual return type
            if (methodCall.Type == null || methodCall.Type.Name == "Void" ||
                string.IsNullOrEmpty(methodCall.Name) || GetUseCount(methodCall) == 0)
            {
                WriteLine($"{obj}.{methodName}{generics}({args});");
            }
            else
            {
                var resultType = MapType(methodCall.Type);
                WriteLine($"{resultType} {methodCall.Name} = {obj}.{methodName}{generics}({args});");
            }
        }

        public void Visit(IRBaseMethodCall baseCall)
        {
            var methodName = SanitizeName(baseCall.MethodName);
            var args = string.Join(", ", baseCall.Arguments.Select(EmitExpression));

            if (baseCall.Type == null || baseCall.Type.Name == "Void")
            {
                WriteLine($"base.{methodName}({args});");
            }
            else
            {
                var resultType = MapType(baseCall.Type);
                WriteLine($"{resultType} {baseCall.Name} = base.{methodName}({args});");
            }
        }

        public void Visit(IRFieldAccess fieldAccess)
        {
            var obj = EmitExpression(fieldAccess.Object);
            var fieldName = SanitizeName(fieldAccess.FieldName);
            var type = MapType(fieldAccess.Type);
            var access = RequiresNativeBclIntCast(fieldAccess)
                ? $"(int)({obj}.{fieldName})"
                : $"{obj}.{fieldName}";
            WriteLine($"{type} {fieldAccess.Name} = {access};");
        }

        /// <summary>
        /// True for P1 native-BCL surface members whose v1 BL type diverges
        /// from the real .NET member type (exactly DateTime.DayOfWeek and
        /// DateTime.Kind, flagged RequiresCSharpIntCast in NativeBclSurface):
        /// the analyzer types them Integer, so the raw enum access must be
        /// wrapped in an explicit (int) cast — otherwise csc fails CS0266 on
        /// the typed temp, and an inlined WriteLine would print the enum name
        /// ("Sunday"), a parity diff vs the C++ backend's numeric value.
        /// Keyed by the RECEIVER's type name plus the flag — never by member
        /// name matching — so a user field that happens to be called
        /// DayOfWeek on a non-DateTime receiver is untouched.
        /// </summary>
        private static bool RequiresNativeBclIntCast(IRFieldAccess fieldAccess)
        {
            var receiverTypeName = fieldAccess.Object?.Type?.Name;
            return receiverTypeName != null &&
                   NativeBclSurface.RequiresCSharpIntCast(receiverTypeName, fieldAccess.FieldName);
        }

        public void Visit(IRFieldStore fieldStore)
        {
            var obj = EmitExpression(fieldStore.Object);
            var fieldName = SanitizeName(fieldStore.FieldName);
            var value = EmitExpression(fieldStore.Value);
            WriteLine($"{obj}.{fieldName} = {value};");
        }

        public void Visit(IRTupleElement tupleElement)
        {
            var tuple = EmitExpression(tupleElement.Tuple);
            var varName = SanitizeName(tupleElement.Name);
            var type = MapType(tupleElement.Type);
            // Access tuple element using Item1, Item2, etc. (1-based indexing)
            WriteLine($"{type} {varName} = {tuple}.Item{tupleElement.Index + 1};");
        }

        public void Visit(IRTryCatch tryCatch)
        {
            WriteLine("try");
            WriteLine("{");
            Indent();

            // Generate try block body
            _processedBlocks.Add(tryCatch.TryBlock);
            EmitBlockInstructions(tryCatch.TryBlock);
            EmitTryRegionTerminator(tryCatch.TryBlock, tryCatch.EndBlock);

            Unindent();
            WriteLine("}");

            // Generate catch clauses
            foreach (var catchClause in tryCatch.CatchClauses)
            {
                var exType = catchClause.ExceptionType?.Name ?? "Exception";
                var varName = !string.IsNullOrEmpty(catchClause.VariableName)
                    ? SanitizeName(catchClause.VariableName)
                    : "ex";

                WriteLine($"catch ({exType} {varName})");
                WriteLine("{");
                Indent();

                _processedBlocks.Add(catchClause.Block);
                EmitBlockInstructions(catchClause.Block);
                EmitTryRegionTerminator(catchClause.Block, tryCatch.EndBlock);

                Unindent();
                WriteLine("}");
            }

            // Generate finally block if present
            if (tryCatch.FinallyBlock != null)
            {
                WriteLine("finally");
                WriteLine("{");
                Indent();

                _processedBlocks.Add(tryCatch.FinallyBlock);
                EmitBlockInstructions(tryCatch.FinallyBlock);
                EmitTryRegionTerminator(tryCatch.FinallyBlock, tryCatch.EndBlock, allowLoopExit: false);

                Unindent();
                WriteLine("}");
            }

            // Continue with end block
            if (tryCatch.EndBlock != null && !_processedBlocks.Contains(tryCatch.EndBlock))
            {
                _processedBlocks.Add(tryCatch.EndBlock);
                EmitBlockInstructions(tryCatch.EndBlock);

                EmitContinuationTerminator(tryCatch.EndBlock);
            }
        }

        /// <summary>
        /// The control flow that ENDS a try, catch or finally region's first block, emitted inside
        /// that region's braces.
        ///
        /// <para>⛔ The IRSwitch arm was missing, so a <c>Select Case</c> anywhere in a Try was
        /// DROPPED WITHOUT A TRACE — its switch, every case body, and everything after it up to the
        /// End Try. MEASURED: <c>Try : Select Case n ... : Catch</c> emitted an empty
        /// <c>try { }</c>, and the program printed nothing, with a green build. Loop bodies had
        /// already needed the same arm (see GenerateLoop). A Finally handled no terminator at all,
        /// so the same held there for an If.</para>
        ///
        /// <para>Continuing into the region's successors stops at the Try's end block: its name
        /// ends in ".end", which HandleUnconditionalBranch never follows, and the direct branch is
        /// refused below — so code after End Try is never pulled inside the braces.</para>
        ///
        /// <para>An <c>Exit For</c> as a try or catch body's last statement targets the LOOP's end,
        /// not the Try's, so the end-block guard below would drop it — TryEmitLoopExit emits it
        /// as a <c>break</c>/<c>goto</c> first. Not from a Finally: C# forbids leaving a finally
        /// block that way (CS0157).</para>
        /// </summary>
        private void EmitTryRegionTerminator(BasicBlock regionBlock, BasicBlock tryEndBlock, bool allowLoopExit = true)
        {
            var terminator = regionBlock.Instructions.LastOrDefault();
            if (terminator is IRConditionalBranch cond)
            {
                HandleConditionalBranch(cond);
            }
            else if (terminator is IRSwitch switchInst)
            {
                HandleSwitchStatement(switchInst);
            }
            else if (terminator is IRBranch branch)
            {
                if (allowLoopExit && TryEmitLoopExit(branch)) return;
                if (branch.Target != tryEndBlock && !_processedBlocks.Contains(branch.Target))
                    HandleUnconditionalBranch(branch);
            }
        }

        public void Visit(IRForEach forEach)
        {
            var elemType = MapType(forEach.ElementType);
            var varName = SanitizeName(forEach.VariableName);
            // Evaluated BEFORE the rename below: the collection is read in the enclosing scope,
            // where the name still has its outer meaning.
            var collectionExpr = EmitExpression(forEach.Collection);

            // ⛔ THE LOOP VARIABLE MAY NOT REUSE AN OUTER NAME. BasicLang scopes a For Each variable
            // to its body and lets it shadow a local, a parameter or an enclosing loop's variable;
            // C# refuses that (CS0136). And BasicLang is case-INSENSITIVE where C# is not, so a
            // local `N` beside `For Each n` compiled — and the body read the OUTER `N` through the
            // case-insensitive name map: a silent wrong answer (68 where 43 was right, measured).
            // A colliding variable gets a fresh name for its body only.
            var hadOuterRename = false;
            string outerRename = null;
            var renamed = forEach.VariableName != null && ForEachVariableCollides(forEach.VariableName);
            if (renamed)
            {
                hadOuterRename = _forEachRenames.TryGetValue(forEach.VariableName, out outerRename);
                varName = FreshForEachVariableName(varName);
                _forEachRenames[forEach.VariableName] = varName;
            }
            _openForEachVariables.Add(forEach.VariableName ?? string.Empty);

            WriteLine($"foreach ({elemType} {varName} in {collectionExpr})");
            WriteLine("{");
            Indent();

            // ⛔ REGISTER THE END BLOCK FIRST. Everything emitted between here and the closing
            // brace — the body, an If's arms, a Try, a Select Case — asks TryEmitLoopExit
            // whether a branch is an `Exit For`, and the answer is "only for a block in this
            // set, and only when IRBranch.IsLoopExit". Without the registration `Exit For`
            // emitted NOTHING and the loop ran to completion (measured: 10 for a loop over
            // 1,2,3,4 that must total 3).
            var registeredEnd = forEach.EndBlock != null && _forEachEndBlocks.Add(forEach.EndBlock);
            _loopSwitchDepths.Push(_switchDepth);

            // Generate body block
            _processedBlocks.Add(forEach.BodyBlock);
            EmitBlockInstructions(forEach.BodyBlock);

            // Handle body block's terminator
            var bodyTerminator = forEach.BodyBlock.Instructions.LastOrDefault();
            if (bodyTerminator is IRConditionalBranch bodyCond)
            {
                HandleConditionalBranch(bodyCond);
            }
            else if (bodyTerminator is IRSwitch bodySwitch)
            {
                // ⛔ THIS ARM DID NOT EXIST, and EmitBlockInstructions skips every IRSwitch
                // because control flow is emitted structurally — so a `Select Case` that was
                // the whole body of a For Each was DROPPED. Measured: a loop summing 1,2,3
                // through a Select Case printed 0 from a program that compiled and ran.
                HandleSwitchStatement(bodySwitch);
            }
            else if (bodyTerminator is IRBranch bodyBranch)
            {
                // The exit test owns `Target == forEach.EndBlock`: that is BOTH the end of an
                // ordinary iteration (emit nothing) and a bare `Exit For` written as the body's
                // last statement (emit the jump), told apart only by IRBranch.IsLoopExit.
                if (!TryEmitLoopExit(bodyBranch) && !_processedBlocks.Contains(bodyBranch.Target))
                {
                    HandleUnconditionalBranch(bodyBranch);
                }
            }

            Unindent();
            WriteLine("}");

            // The body is closed: the name resolves back to whatever it meant outside it.
            _openForEachVariables.RemoveAt(_openForEachVariables.Count - 1);
            if (renamed)
            {
                if (hadOuterRename) _forEachRenames[forEach.VariableName] = outerRename;
                else _forEachRenames.Remove(forEach.VariableName);
            }

            _loopSwitchDepths.Pop();
            if (registeredEnd) _forEachEndBlocks.Remove(forEach.EndBlock);
            EmitLoopExitLabelIfNeeded(forEach.EndBlock);

            // Continue with end block
            if (forEach.EndBlock != null && !_processedBlocks.Contains(forEach.EndBlock))
            {
                _processedBlocks.Add(forEach.EndBlock);
                EmitBlockInstructions(forEach.EndBlock);

                EmitContinuationTerminator(forEach.EndBlock);
            }
        }

        /// <summary>
        /// Whether a <c>For Each</c> variable's name is already taken in the C# method body it lands
        /// in: a local or parameter of the function (compared case-insensitively, as BasicLang
        /// does), or the variable of a <c>For Each</c> whose body is still open. A module global or
        /// a class member is NOT a collision — a C# local may shadow a field.
        /// </summary>
        private bool ForEachVariableCollides(string name)
        {
            if (_openForEachVariables.Contains(name, StringComparer.OrdinalIgnoreCase)) return true;
            if (_currentFunction == null) return false;
            return _currentFunction.LocalVariables.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))
                || _currentFunction.Parameters.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// <c>{name}_{k}</c> for the smallest k not already a name in this body — a local,
        /// parameter, global, class member, open loop variable, or an earlier fresh name —
        /// all compared case-insensitively. A user may spell <c>n_1</c> too; that is why the
        /// candidate is checked rather than assumed free.
        ///
        /// <para>Locals, parameters and globals come from <see cref="_declaredIdentifiers"/>
        /// (case-insensitive, filled from the same function whenever <see cref="_currentFunction"/>
        /// is set). An active rename is always an earlier fresh name of this same body, so
        /// <see cref="_issuedForEachNames"/> covers it.</para>
        /// </summary>
        private string FreshForEachVariableName(string baseName)
        {
            if (!ReferenceEquals(_forEachNamesOwner, _currentFunction))
            {
                _issuedForEachNames.Clear();
                _forEachNamesOwner = _currentFunction;
            }

            bool Taken(string candidate) =>
                _declaredIdentifiers.Contains(candidate)
                || _currentClassMemberNames.Contains(candidate)
                || _issuedForEachNames.Contains(candidate)
                || _openForEachVariables.Contains(candidate, StringComparer.OrdinalIgnoreCase);

            var trimmed = baseName.TrimStart('@');
            for (var k = 1; ; k++)
            {
                var candidate = $"{trimmed}_{k}";
                if (Taken(candidate)) continue;
                _issuedForEachNames.Add(candidate);
                return candidate;
            }
        }

        public void Visit(IRIndexerAccess indexer)
        {
            // This is handled by GetValueName - we don't emit a separate statement
            // The indexer access expression is generated inline where it's used
        }

        // ====================================================================
        // Helper Methods
        // ====================================================================

        private string EmitConstant(IRConstant constant)
        {
            if (constant.Value == null)
                return "null";

            if (constant.Value is string str)
                return $"\"{EscapeString(str)}\"";

            if (constant.Value is char ch)
                return $"'{EscapeChar(ch)}'";

            if (constant.Value is bool b)
                return b ? "true" : "false";

            if (constant.Value is float or double)
                return CSharpFloatingLiteral(constant.Value);

            // System.Decimal constant (spec 6.1: a literal converted from its
            // source text in a Decimal context) — the m suffix keeps the C#
            // compiler in decimal space, and decimal.ToString round-trips the
            // exact value including scale ('1.50' stays 1.50m).
            if (constant.Value is decimal dm)
                return dm.ToString(CultureInfo.InvariantCulture) + "m";

            // What still reaches here is integral (Integer, Long, Short, Byte, …). Invariant
            // because a NEGATIVE one is culture-sensitive: sv-SE's NegativeSign is U+2212, which
            // emitted `public int NegI = −7;` — CS1056, not a C# token.
            return Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// A <c>Single</c> or <c>Double</c> constant as C# source: a FLOATING literal,
        /// culture-invariant, that parses back to the same value (sign of zero included), or the
        /// <c>float.</c>/<c>double.</c> named constant for NaN and ±Infinity. The C# twin of
        /// <c>CppCodeGenerator.CppDoubleLiteral</c>.
        ///
        /// <para>⛔ A Double used to fall through to a bare CurrentCulture <c>ToString()</c>, and a
        /// Single to <c>$"{f}f"</c>. MEASURED through the CLI:
        /// (1) an integral Double lost its point, so the constant became an INT literal —
        /// <c>1.0 / 0.0</c>, which constant folding leaves alone, emitted <c>1 / 0</c>: CS0020,
        /// integer division by constant zero;
        /// (2) de-DE emitted <c>h = 2,5;</c> and a parameter default <c>double r = 0,25</c> —
        /// neither compiles;
        /// (3) <c>-0.0</c> would emit <c>-0</c>, an int, i.e. +0.0;
        /// (4) NaN and ∞ would emit <c>NaN</c> and <c>∞</c>, which are not C#.</para>
        ///
        /// <para>"R" is the shortest round-tripping string; ".0" is appended only when it has
        /// neither a point nor an exponent ("1E+20" is already a C# double literal). A Single keeps
        /// its <c>f</c> suffix. <c>-0.0</c> is unary minus on the constant <c>0.0</c>, which C#
        /// folds to −0.0.</para>
        /// </summary>
        internal static string CSharpFloatingLiteral(object value)
        {
            if (value is float f)
            {
                if (float.IsNaN(f)) return "float.NaN";
                if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
                if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
                return WithFloatingPoint(f.ToString("R", CultureInfo.InvariantCulture)) + "f";
            }

            var d = (double)value;
            if (double.IsNaN(d)) return "double.NaN";
            if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
            if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
            return WithFloatingPoint(d.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string WithFloatingPoint(string roundTrip) =>
            roundTrip.IndexOfAny(FloatingLiteralMarks) >= 0 ? roundTrip : roundTrip + ".0";

        private static readonly char[] FloatingLiteralMarks = { '.', 'E', 'e' };

        /// <summary>
        /// Generate C# where clauses for generic type parameter constraints
        /// </summary>
        private string GenerateConstraintClauses(List<GenericTypeParameter> typeParams)
        {
            if (typeParams == null || typeParams.Count == 0)
                return "";

            var clauses = new List<string>();

            foreach (var param in typeParams)
            {
                if (param.Constraints == null || param.Constraints.Count == 0)
                    continue;

                var constraints = new List<string>();

                // Class/struct constraints must come first
                foreach (var c in param.Constraints)
                {
                    if (c.Kind == GenericConstraintKind.Class)
                        constraints.Insert(0, "class");
                    else if (c.Kind == GenericConstraintKind.Structure)
                        constraints.Insert(0, "struct");
                }

                // Then type constraints (interfaces, base classes)
                foreach (var c in param.Constraints)
                {
                    if (c.Kind == GenericConstraintKind.Type && !string.IsNullOrEmpty(c.TypeName))
                        constraints.Add(c.TypeName);
                }

                // new() constraint must come last
                foreach (var c in param.Constraints)
                {
                    if (c.Kind == GenericConstraintKind.New)
                        constraints.Add("new()");
                }

                if (constraints.Count > 0)
                {
                    clauses.Add($"where {param.Name} : {string.Join(", ", constraints)}");
                }
            }

            if (clauses.Count == 0)
                return "";

            return " " + string.Join(" ", clauses);
        }

        private string MapType(TypeInfo type)
        {
            if (type == null)
                return "object";

            // Handle tuple types
            if (type.Kind == TypeKind.Tuple && type.TupleElementTypes.Count > 0)
            {
                var elements = new List<string>();
                for (int i = 0; i < type.TupleElementTypes.Count; i++)
                {
                    var elementType = MapType(type.TupleElementTypes[i]);
                    if (i < type.TupleElementNames.Count && !string.IsNullOrEmpty(type.TupleElementNames[i]))
                    {
                        elements.Add($"{elementType} {type.TupleElementNames[i]}");
                    }
                    else
                    {
                        elements.Add(elementType);
                    }
                }
                return $"({string.Join(", ", elements)})";
            }

            // Handle nullable types
            if (type.IsNullable && type.UnderlyingType != null)
            {
                var underlyingType = MapType(type.UnderlyingType);
                return $"{underlyingType}?";
            }

            if (_typeMap.TryGetValue(type.Name, out var csharpType))
            {
                if (type.IsNullable)
                    return $"{csharpType}?";
                return csharpType;
            }

            if (type.Kind == TypeKind.Array && type.ElementType != null)
            {
                var elementType = MapType(type.ElementType);
                // A MULTI-DIMENSIONAL array is spelled rectangularly — `int[,]` for rank 2 —
                // which is what EmitExpression already emits for a multi-index
                // IRGetElementPtr (`a[i, j]`), and what SizedArrayInitializer allocates
                // (`new int[4, 3]`). Rank is clamped to at least 1 because many synthesized
                // array TypeInfos carry rank 0 and have always meant one dimension.
                var commas = new string(',', Math.Max(1, type.ArrayRank) - 1);
                return $"{elementType}[{commas}]";
            }

            // Handle generic types with type arguments
            if (type.GenericArguments != null && type.GenericArguments.Count > 0)
            {
                var typeName = MapTypeName(type.Name);
                var typeArgs = string.Join(", ", type.GenericArguments.Select(MapType));
                var result = $"{typeName}<{typeArgs}>";
                return type.IsNullable ? $"{result}?" : result;
            }

            return type.IsNullable ? $"{type.Name}?" : type.Name;
        }

        /// <summary>
        /// The storage initializer for a FIXED-SIZE array declaration (<c>new int[3]</c>,
        /// <c>new int[4, 3]</c>), or <c>null</c> when the declaration is not one — callers then
        /// fall back to their own default.
        ///
        /// <para><b>Shared by locals, module-level globals AND class fields deliberately.</b>
        /// Only the LOCALS loop used to size arrays, so a module-level <c>Dim g(4) As Integer</c>
        /// or a field <c>Public Cells(9) As Integer</c> emitted a bare <c>int[] g;</c> — null at
        /// run time, NullReferenceException on the first index. The C++ backend hit the identical
        /// split and fixed it by routing every declaration site through one helper
        /// (<c>CppCodeGenerator.SizedArrayInitializer</c>); this is its counterpart, and the two
        /// read the same <see cref="TypeInfo.ArrayDimensionSizes"/> so the backends cannot
        /// disagree about the element count.</para>
        ///
        /// <para>Every dimension must have a known size. A partially-sized declaration has no
        /// meaning to allocate, so it falls through to the default rather than guessing.</para>
        /// </summary>
        private string SizedArrayInitializer(TypeInfo type)
        {
            if (type?.Kind != TypeKind.Array || type.ElementType == null) return null;

            var sizes = type.ArrayDimensionSizes;
            if (sizes.Count == 0) return null;
            foreach (var size in sizes)
                if (size <= 0) return null;

            // C# spells a multi-dimensional array rectangularly — `new int[4, 3]` indexed
            // `a[i, j]`, which is exactly what EmitExpression already emits for a multi-index
            // IRGetElementPtr.
            return $"new {MapType(type.ElementType)}[{string.Join(", ", sizes)}]";
        }

        private string GetDefaultValue(TypeInfo type)
        {
            if (type == null)
                return "default";

            var typeName = type.Name?.ToLower() ?? "";

            return typeName switch
            {
                "integer" => "0",
                "long" => "0L",
                "single" => "0.0f",
                "double" => "0.0",
                "boolean" => "false",
                "char" => "'\\0'",
                "string" => "\"\"",
                _ when type.Kind == TypeKind.Array => "default!",
                _ when type.Kind == TypeKind.Pointer => "default",
                _ when type.Kind == TypeKind.TypeParameter => "default!",  // Generic type parameter T
                _ when type.Kind == TypeKind.Structure => "default",       // Value types
                _ when type.Kind == TypeKind.Union => "default",           // Union types (all members share same memory)
                _ when type.Kind == TypeKind.Class => "default!",          // Reference types
                _ => "default!"  // Use default for unknown types (safe for both value and reference types)
            };
        }

        /// <summary>
        /// The rendered divisor of <paramref name="bin"/>, made NON-CONSTANT to Roslyn when it is a
        /// constant zero of an integral or Decimal type, or a constant -1 of a signed integral type.
        ///
        /// <para>⛔ A constant over a constant zero is CS0020 ("Division by constant zero") — a
        /// COMPILE error, where .NET/VB throws DivideByZeroException at RUN time and a Try can
        /// catch it. Roslyn raises it only when BOTH operands are constants (it is a constant-
        /// folding error): `n / 0` compiles and throws. But the IR hands us two constants far more
        /// often than a literal `5 \ 0`, because copy propagation substitutes locals. MEASURED, each
        /// emitted as a constant over a constant and rejected: `Dim z = 0 : 5 \ z` → `5 / 0`,
        /// `Dim a = 5 : a \ 0` → `5 / 0`, `5 Mod 0` → `5 % 0`, Decimal `d / 0` → `5m / 0m`. The IR
        /// constant folder correctly refuses these (it catches DivideByZeroException), so the
        /// emitter is the one place that knows the operands became C# constants.</para>
        ///
        /// <para>An array element read is not a constant expression, keeps the literal's own type
        /// (int, long, decimal, byte...) with no type name to spell, and has no side effect. It is
        /// only ever reached on a path that throws, so its allocation costs nothing that matters.
        /// Floating divisors are left alone: `5.0 / 0.0` is legal C# (Infinity), as in .NET.</para>
        ///
        /// <para>⛔ A constant -1 is the SAME trap: a constant signed minimum over it is CS0220 ("The
        /// operation overflows at compile time in checked mode"), where .NET throws
        /// OverflowException at run time. MEASURED: `(-2147483647 - 1) \ -1` emitted
        /// `-2147483648 / -1` and did not build. A variable over -1 is unaffected either way; the
        /// rewrite only costs a -1 divisor, which nobody writes in a hot loop.</para>
        /// </summary>
        /// <summary>
        /// <paramref name="expr"/> cast back to the NARROW integral type of <paramref name="bin"/>
        /// (Short, UShort, Byte, SByte), or null when no cast is needed.
        ///
        /// <para>⛔ C# promotes a <c>short</c>, <c>sbyte</c>, <c>byte</c> or <c>ushort</c> operand to
        /// <c>int</c>, so <c>a + b</c> on two Shorts is an <c>int</c>, and assigning it to the Short
        /// the IR declared was CS0266 ("Cannot implicitly convert type 'int' to 'short'"). MEASURED
        /// on master <c>7a62bdea</c>: every Short or SByte <c>+ - * \ Mod</c> assigned back to its
        /// own type failed the C# build. The cast is UNCHECKED — the width the Integer arithmetic
        /// of this backend already wraps at — except for <c>\</c>: there the promoted quotient
        /// only fails to fit for <c>MinValue \ -1</c>, which .NET reports as an OverflowException
        /// (Integer and Long trap the same way), so that cast is CHECKED. A narrow <c>Mod</c> by
        /// -1 is 0, which always fits.</para>
        /// </summary>
        private string NarrowArithmetic(IRBinaryOp bin, string expr)
        {
            if (bin.Type?.Name is not ("Short" or "UShort" or "Byte" or "SByte" or "UByte")) return null;
            if (bin.Operation is not (BinaryOpKind.Add or BinaryOpKind.Sub or BinaryOpKind.Mul
                or BinaryOpKind.IntDiv or BinaryOpKind.Mod or BinaryOpKind.And or BinaryOpKind.Or
                or BinaryOpKind.BitwiseAnd or BinaryOpKind.BitwiseOr or BinaryOpKind.Xor
                or BinaryOpKind.Shl or BinaryOpKind.Shr))
                return null;
            var type = MapType(bin.Type);
            return bin.Operation == BinaryOpKind.IntDiv
                ? $"checked(({type})({expr}))"
                : $"unchecked(({type})({expr}))";
        }

        /// <summary>
        /// A Short/UShort/Byte/SByte negation or bitwise Not cast back to its narrow type, or
        /// null. ⛔ C# promotes the operand to <c>int</c>, so <c>Dim lo As Short = -s</c> was
        /// CS0266 — MEASURED on master <c>e69ec64e</c>, the unary twin of
        /// <see cref="NarrowArithmetic"/>. Unchecked: <c>-(-32768)</c> wraps to -32768, as this
        /// backend's Integer negation does.
        /// </summary>
        private string NarrowUnary(IRUnaryOp un, string expr)
        {
            if (un.Type?.Name is not ("Short" or "UShort" or "Byte" or "SByte" or "UByte")) return null;
            if (un.Operation is not (UnaryOpKind.Neg or UnaryOpKind.BitwiseNot)) return null;
            return $"unchecked(({MapType(un.Type)})({expr}))";
        }

        private static string EmitDivisor(IRBinaryOp bin, string renderedRight)
        {
            if (bin.Operation is not (BinaryOpKind.Div or BinaryOpKind.IntDiv or BinaryOpKind.Mod))
                return renderedRight;
            return IsConstantDivisorTrap(bin.Right) ? $"new[] {{ {renderedRight} }}[0]" : renderedRight;
        }

        private static bool IsConstantDivisorTrap(IRValue value)
        {
            // The OUTERMOST type decides: `(double)(0)` is a floating divisor even though the
            // constant inside it is an int.
            if (value?.Type?.IsFloatingPoint() == true) return false;

            var inner = value;
            while (inner is IRCast cast) inner = cast.Value;
            if (inner is not IRConstant { Value: not null } constant) return false;

            return constant.Value switch
            {
                double or float => value.Type != null && !value.Type.IsFloatingPoint() && Convert.ToDouble(constant.Value) == 0.0,
                int or long or short or sbyte
                    => Convert.ToDecimal(constant.Value) is 0m or -1m,
                byte or ushort or uint or ulong or decimal
                    => Convert.ToDecimal(constant.Value) == 0m,
                _ => false
            };
        }

        private string MapBinaryOperator(BinaryOpKind op) => op switch
        {
            BinaryOpKind.Add => "+",
            BinaryOpKind.Sub => "-",
            BinaryOpKind.Mul => "*",
            BinaryOpKind.Div => "/",
            BinaryOpKind.Mod => "%",
            BinaryOpKind.IntDiv => "/",
            // VB `And`/`Or` evaluate BOTH operands; C# `&`/`|` on bool do exactly that.
            // These were `&&`/`||`, which SKIPPED a side effect VB guarantees.
            BinaryOpKind.And => "&",
            BinaryOpKind.Or => "|",
            BinaryOpKind.AndAlso => "&&",
            BinaryOpKind.OrElse => "||",
            BinaryOpKind.BitwiseAnd => "&",
            BinaryOpKind.BitwiseOr => "|",
            BinaryOpKind.Xor => "^",
            BinaryOpKind.Shl => "<<",
            BinaryOpKind.Shr => ">>",
            BinaryOpKind.Concat => "+",
            _ => "?"
        };

        private string MapUnaryOperator(UnaryOpKind op) => op switch
        {
            UnaryOpKind.Neg => "-",
            UnaryOpKind.Not => "!",
            UnaryOpKind.BitwiseNot => "~",
            UnaryOpKind.Inc => "++",
            UnaryOpKind.Dec => "--",
            UnaryOpKind.AddressOf => "",  // In C#, method reference is just the method name
            _ => "?"
        };

        private string MapCompareOperator(CompareKind cmp) => cmp switch
        {
            CompareKind.Eq => "==",
            CompareKind.Ne => "!=",
            CompareKind.Lt => "<",
            CompareKind.Le => "<=",
            CompareKind.Gt => ">",
            CompareKind.Ge => ">=",
            _ => "?"
        };

        private string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "_unnamed";

            // Convert VB.NET's "Me" to C#'s "this"
            if (name.Equals("Me", StringComparison.OrdinalIgnoreCase))
                return "this";

            var sanitized = new StringBuilder();

            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_')
                    sanitized.Append(ch);
            }

            var result = sanitized.ToString();

            if (result.Length > 0 && char.IsDigit(result[0]))
                result = "_" + result;

            if (IsCSharpKeyword(result))
                result = "@" + result;

            return result.Length > 0 ? result : "_unnamed";
        }

        private bool IsCSharpKeyword(string name)
        {
            var keywords = new HashSet<string>
            {
                "abstract", "as", "base", "bool", "break", "byte", "case", "catch",
                "char", "class", "const", "continue", "default", "do", "double",
                "else", "false", "finally", "for", "foreach", "goto", "if", "int",
                "null", "object", "return", "string", "switch", "this", "true",
                "try", "void", "while"
            };

            return keywords.Contains((name ?? "").ToLowerInvariant());
        }

        private string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
        }

        private string EscapeChar(char ch)
        {
            if (ch == '\'') return "\\'";
            if (ch == '\\') return "\\\\";
            if (ch == '\n') return "\\n";
            if (ch == '\r') return "\\r";
            if (ch == '\t') return "\\t";
            return ch.ToString();
        }

        /// <summary>
        /// Generate P/Invoke declaration for C library interop
        /// </summary>
        private void GenerateExternDeclaration(IRExternDeclaration externDecl)
        {
            // Skip if this is a platform-specific extern (not a C library interop)
            if (string.IsNullOrEmpty(externDecl.LibraryName) && externDecl.PlatformImplementations.Count > 0)
            {
                // This is a platform-specific extern, handle differently
                if (externDecl.PlatformImplementations.TryGetValue("CSharp", out var impl))
                {
                    // Emit the raw C# implementation
                    WriteLine(impl);
                }
                return;
            }

            // Build the DllImport attribute
            var dllImportParts = new List<string>();
            dllImportParts.Add($"\"{externDecl.LibraryName}\"");

            // Add entry point if alias is specified
            if (!string.IsNullOrEmpty(externDecl.AliasName))
            {
                dllImportParts.Add($"EntryPoint = \"{externDecl.AliasName}\"");
            }

            // Add calling convention
            if (!string.IsNullOrEmpty(externDecl.CallingConvention) && externDecl.CallingConvention != "Default")
            {
                var ccName = externDecl.CallingConvention switch
                {
                    "CDecl" => "CallingConvention.Cdecl",
                    "StdCall" => "CallingConvention.StdCall",
                    "FastCall" => "CallingConvention.FastCall",
                    "ThisCall" => "CallingConvention.ThisCall",
                    _ => "CallingConvention.Winapi"
                };
                dllImportParts.Add($"CallingConvention = {ccName}");
            }

            WriteLine($"[DllImport({string.Join(", ", dllImportParts)})]");

            // Build the method signature
            var returnType = externDecl.IsFunction
                ? MapType(externDecl.ReturnType)
                : "void";

            var paramList = new List<string>();
            foreach (var param in externDecl.Parameters)
            {
                var paramType = MapType(param.Type);
                var paramName = SanitizeName(param.Name);

                // Handle ByRef parameters
                if (param.IsByRef)
                {
                    paramList.Add($"ref {paramType} {paramName}");
                }
                else
                {
                    paramList.Add($"{paramType} {paramName}");
                }
            }

            var methodName = SanitizeName(externDecl.Name);
            WriteLine($"public static extern {returnType} {methodName}({string.Join(", ", paramList)});");
        }

        private void Write(string text) => _output.Append(text);

        private void WriteLine(string text = "")
        {
            if (!string.IsNullOrEmpty(text))
            {
                _output.Append(new string(' ', _indentLevel * _options.IndentSize));
                _output.AppendLine(text);
            }
            else
            {
                _output.AppendLine();
            }
        }

        private void Indent() => _indentLevel++;
        private void Unindent() => _indentLevel = Math.Max(0, _indentLevel - 1);
    }
}
