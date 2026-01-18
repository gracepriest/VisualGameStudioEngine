using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        private IRModule _currentModule;
        private IRFunction _currentFunction;

        // Name mapping and declared identifier tracking
        private readonly Dictionary<IRValue, string> _valueNames;
        private readonly Dictionary<string, string> _variableNameMap; // logical name -> sanitized C# name
        private readonly HashSet<string> _declaredIdentifiers;         // logical names (locals/params/globals)
        private readonly Dictionary<string, IRValue> _tempDefsByName;  // tempName -> defining IRValue (only for non-declared names)

        // Use counts help decide whether to emit calls as statements or inline them into expressions
        private readonly Dictionary<IRValue, int> _useCounts;

        // For structured control flow generation
        private HashSet<BasicBlock> _processedBlocks;

        // Stack of loop end blocks for break detection
        private Stack<BasicBlock> _loopEndBlocks;

        // Standard library provider for built-in functions
        private readonly CSharpStdLibProvider _stdLib;
        private readonly FrameworkStdLibProvider _frameworkStdLib;

        public string GeneratedCode => _output.ToString();

        // Helper methods to check both stdlib providers (Framework first, then CSharp)
        private bool StdLibCanHandle(string functionName)
        {
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
                { "Object", "object" }
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

            _output.Clear();
            _indentLevel = 0;
            _usings.Clear();

            // Add default usings
            _usings.Add("System");
            _usings.Add("System.Collections.Generic");
            _usings.Add("System.Threading.Tasks");
            _usings.Add("System.Collections");
            _usings.Add("System.Runtime.InteropServices");
            _usings.Add("System.Text");
            _usings.Add("System.IO");
            _usings.Add("System.Linq");
            _usings.Add("System.Net");
            _usings.Add("System.Net.Http");
            _usings.Add("System.Net.Sockets");
            _usings.Add("System.Text.Json");
            _usings.Add("System.Text.Json.Nodes");
            _usings.Add("System.Text.RegularExpressions");
            _usings.Add("System.Security.Cryptography");
            _usings.Add("System.Diagnostics");
            _usings.Add("System.Threading");

            // Add .NET usings from the source code
            foreach (var netUsing in module.NetUsings)
            {
                _usings.Add(netUsing.Namespace);
            }

            // Pre-scan for stdlib function calls to collect required imports
            CollectStdLibImports(module);

            // Emit using directives
            foreach (var usingDirective in _usings.OrderBy(u => u))
                WriteLine($"using {usingDirective};");

            // Emit aliased usings separately
            foreach (var netUsing in module.NetUsings.Where(u => !string.IsNullOrEmpty(u.Alias)))
            {
                WriteLine($"using {netUsing.Alias} = {netUsing.Namespace};");
            }

            WriteLine();

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
                                    var accessMod = MapAccessModifier(constVar.Access);
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
                                    var accessMod = MapAccessModifier(globalVar.Access);
                                    if (globalVar.InitialValue != null)
                                    {
                                        var initVal = EmitExpression(globalVar.InitialValue);
                                        WriteLine($"{accessMod} static {type} {name} = {initVal};");
                                    }
                                    else
                                    {
                                        WriteLine($"{accessMod} static {type} {name};");
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
            return _output.ToString();
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

                    // Declare locals
                    var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var localVar in method.DefaultImplementation.LocalVariables)
                    {
                        var varName = GetValueName(localVar);
                        if (declared.Add(varName))
                        {
                            var csharpType = MapType(localVar.Type);
                            var defaultValue = GetDefaultValue(localVar.Type);
                            WriteLine($"{csharpType} {varName} = {defaultValue};");
                        }
                    }

                    if (method.DefaultImplementation.LocalVariables.Count > 0)
                        WriteLine();

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
                var value = member.Value != null ? $" = {member.Value}" : "";
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

            // Type and name
            parts.Add(MapTypeName(param.TypeName));
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
            if (value is IRConstant constant)
            {
                if (constant.Value is string s)
                    return $"\"{s}\"";
                if (constant.Value is bool b)
                    return b ? "true" : "false";
                if (constant.Value is null)
                    return "null";
                return constant.Value.ToString();
            }
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
        private void GenerateClass(IRClass irClass)
        {
            // Class declaration with generic parameters
            var className = SanitizeName(irClass.Name);
            var genericParams = "";
            if (irClass.GenericParameters != null && irClass.GenericParameters.Count > 0)
            {
                genericParams = "<" + string.Join(", ", irClass.GenericParameters) + ">";
            }

            var abstractMod = irClass.IsAbstract ? "abstract " : "";
            var classDecl = $"public {abstractMod}class {className}{genericParams}";
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
                WriteLine($"{access} {staticMod}{type} {name};");
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

            WriteLine($"{access} {staticMod}{type} {name}");
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
                _processedBlocks = new HashSet<BasicBlock>();
                _loopEndBlocks = new Stack<BasicBlock>();
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
                _processedBlocks = new HashSet<BasicBlock>();
                _loopEndBlocks = new Stack<BasicBlock>();
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
            var delegateType = SanitizeName(evt.DelegateType);
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
                WriteLine($"{access} {staticMod}{sealedMod}{overrideMod}{abstractMod}{virtualMod}{returnType} {name}{genericParams}({paramList})");
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

                // Declare locals
                var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var localVar in method.Implementation.LocalVariables)
                {
                    var varName = GetValueName(localVar);
                    if (declared.Add(varName))
                    {
                        var csharpType = MapType(localVar.Type);
                        var defaultValue = GetDefaultValue(localVar.Type);
                        WriteLine($"{csharpType} {varName} = {defaultValue};");
                    }
                }

                if (method.Implementation.LocalVariables.Count > 0)
                    WriteLine();

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
        }

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
                // Wrap return type in Task<T> or use Task for void
                if (returnType == "void")
                    actualReturnType = "Task";
                else
                    actualReturnType = $"Task<{returnType}>";
            }
            else if (function.IsIterator)
            {
                // Wrap return type in IEnumerable<T>
                if (returnType != "void")
                    actualReturnType = $"IEnumerable<{returnType}>";
            }

            // Generate constraint clauses for generic type parameters
            var constraints = GenerateConstraintClauses(function.GenericTypeParams);

            // Use the function's access modifier if set, otherwise use the default
            var accessMod = MapAccessModifier(function.Access);

            WriteLine($"{accessMod} static {asyncModifier}{actualReturnType} {functionName}{genericParams}({parameters}){constraints}");

            WriteLine("{");
            Indent();

            // Declare locals (ONLY real locals; no compiler temps)
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var localVar in function.LocalVariables)
            {
                var varName = GetValueName(localVar);
                if (declared.Add(varName))
                {
                    var csharpType = MapType(localVar.Type);
                    string defaultValue;

                    // Check if this is an array with a specific size
                    if (localVar.Type?.Kind == TypeKind.Array && localVar.Type.ArraySize > 0)
                    {
                        var elementType = MapType(localVar.Type.ElementType);
                        defaultValue = $"new {elementType}[{localVar.Type.ArraySize}]";
                    }
                    else
                    {
                        defaultValue = GetDefaultValue(localVar.Type);
                    }

                    WriteLine($"{csharpType} {varName} = {defaultValue};");
                }
            }

            if (function.LocalVariables.Count > 0)
                WriteLine();

            // Body - use structured control flow generation
            _processedBlocks = new HashSet<BasicBlock>();
            _loopEndBlocks = new Stack<BasicBlock>();
            if (function.EntryBlock != null)
                GenerateStructuredBlock(function.EntryBlock);

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
                // Check if it's a simple expression lambda (single return statement)
                var instructions = lambdaFunc.EntryBlock.Instructions;
                if (instructions.Count == 1 && instructions[0] is IRReturn ret && ret.Value != null)
                {
                    // Single expression lambda: x => x * 2
                    sb.Append(EmitExpression(ret.Value));
                }
                else
                {
                    // Statement lambda with block: x => { statements; }
                    sb.Append("{\n");
                    var oldIndent = _indentLevel;
                    _indentLevel++;

                    foreach (var instr in instructions)
                    {
                        if (instr is IRReturn retStmt)
                        {
                            if (retStmt.Value != null)
                            {
                                sb.Append($"{new string(' ', _indentLevel * 4)}return {EmitExpression(retStmt.Value)};\n");
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
                    var argExprs = call.Arguments.Select(a => EmitExpression(a)).ToArray();
                    var fn = SanitizeName(call.FunctionName);
                    var args = string.Join(", ", argExprs);
                    return $"{fn}({args})";
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

                    if (_declaredIdentifiers.Contains(v.Name))
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

            // Declare locals
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var localVar in function.LocalVariables)
            {
                var varName = GetValueName(localVar);
                if (declared.Add(varName))
                {
                    var csharpType = MapType(localVar.Type);
                    var defaultValue = GetDefaultValue(localVar.Type);
                    WriteLine($"{csharpType} {varName} = {defaultValue};");
                }
            }

            if (function.LocalVariables.Count > 0)
                WriteLine("");

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

            var defaultTerminator = defaultBlock.Instructions.LastOrDefault();
            if (defaultTerminator is IRReturn)
            {
                // Return already emitted
            }
            else
            {
                WriteLine("break;");
            }
            Unindent();

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

            // Check if block ends with a return (no break needed)
            var terminator = block.Instructions.LastOrDefault();
            if (terminator is IRReturn)
            {
                // Return already emitted
            }
            else if (terminator is IRBranch br && br.Target.Name.Contains("switch.end"))
            {
                // Jump to switch end - emit break
                WriteLine("break;");
            }
            else if (terminator is IRBranch branch)
            {
                // Process the branch target (might have more code)
                HandleUnconditionalBranch(branch);
                WriteLine("break;");
            }
            else
            {
                // Default: add break
                WriteLine("break;");
            }

            Unindent();
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

            return null;
        }

        private void GenerateLoop(string condition, BasicBlock bodyBlock, BasicBlock endBlock, BasicBlock incBlock, string loopType)
        {
            // Push the loop end block so inner code can emit 'break' when targeting it
            if (endBlock != null)
                _loopEndBlocks.Push(endBlock);

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

            // Always generate increment if it exists
            if (incBlock != null && !_processedBlocks.Contains(incBlock))
            {
                _processedBlocks.Add(incBlock);
                EmitBlockInstructions(incBlock);
            }

            Unindent();
            WriteLine("}");

            // Pop the loop end block
            if (endBlock != null)
                _loopEndBlocks.Pop();

            // Continue after the loop
            if (endBlock != null && !_processedBlocks.Contains(endBlock))
            {
                _processedBlocks.Add(endBlock);
                EmitBlockInstructions(endBlock);

                // Handle end block's terminator
                var endTerminator = endBlock.Instructions.LastOrDefault();
                if (endTerminator is IRConditionalBranch endCond)
                    HandleConditionalBranch(endCond);
                else if (endTerminator is IRBranch endBranch)
                    HandleUnconditionalBranch(endBranch);
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
            WriteLine($"if ({condition})");
            WriteLine("{");
            Indent();

            _processedBlocks.Add(thenBlock);
            EmitBlockInstructions(thenBlock);

            // Handle then block's terminator (might have nested control flow, return, or break)
            var thenTerminator = thenBlock.Instructions.LastOrDefault();
            if (thenTerminator is IRConditionalBranch thenCond)
                HandleConditionalBranch(thenCond);
            else if (thenTerminator is IRBranch thenBranch)
            {
                if (IsLoopEndBlock(thenBranch.Target))
                    WriteLine("break;");
                else if (!_processedBlocks.Contains(thenBranch.Target))
                    HandleUnconditionalBranch(thenBranch);
            }

            Unindent();
            WriteLine("}");
            WriteLine("else");
            WriteLine("{");
            Indent();

            _processedBlocks.Add(elseBlock);
            EmitBlockInstructions(elseBlock);

            // Handle else block's terminator
            var elseTerminator = elseBlock.Instructions.LastOrDefault();
            if (elseTerminator is IRConditionalBranch elseCond)
                HandleConditionalBranch(elseCond);
            else if (elseTerminator is IRBranch elseBranch)
            {
                if (IsLoopEndBlock(elseBranch.Target))
                    WriteLine("break;");
                else if (!_processedBlocks.Contains(elseBranch.Target))
                    HandleUnconditionalBranch(elseBranch);
            }

            Unindent();
            WriteLine("}");

            // Continue after merge
            if (mergeBlock != null && !_processedBlocks.Contains(mergeBlock))
            {
                _processedBlocks.Add(mergeBlock);
                EmitBlockInstructions(mergeBlock);

                var mergeTerminator = mergeBlock.Instructions.LastOrDefault();
                if (mergeTerminator is IRConditionalBranch mergeCond)
                    HandleConditionalBranch(mergeCond);
                else if (mergeTerminator is IRBranch mergeBranch)
                    HandleUnconditionalBranch(mergeBranch);
            }
        }

        private void GenerateIfThen(string condition, BasicBlock thenBlock, BasicBlock mergeBlock)
        {
            WriteLine($"if ({condition})");
            WriteLine("{");
            Indent();

            _processedBlocks.Add(thenBlock);
            EmitBlockInstructions(thenBlock);

            // Handle then block's terminator
            var thenTerminator = thenBlock.Instructions.LastOrDefault();
            if (thenTerminator is IRConditionalBranch thenCond)
                HandleConditionalBranch(thenCond);
            else if (thenTerminator is IRBranch thenBranch)
            {
                // Check if this is a break (branch to loop end)
                if (IsLoopEndBlock(thenBranch.Target))
                    WriteLine("break;");
                else if (!_processedBlocks.Contains(thenBranch.Target))
                    HandleUnconditionalBranch(thenBranch);
            }

            Unindent();
            WriteLine("}");

            // Continue after merge
            if (mergeBlock != null && !_processedBlocks.Contains(mergeBlock))
            {
                _processedBlocks.Add(mergeBlock);
                EmitBlockInstructions(mergeBlock);

                var mergeTerminator = mergeBlock.Instructions.LastOrDefault();
                if (mergeTerminator is IRConditionalBranch mergeCond)
                    HandleConditionalBranch(mergeCond);
                else if (mergeTerminator is IRBranch mergeBranch)
                    HandleUnconditionalBranch(mergeBranch);
            }
        }

        private bool IsLoopEndBlock(BasicBlock block)
        {
            if (block == null || _loopEndBlocks.Count == 0)
                return false;
            return _loopEndBlocks.Contains(block);
        }

        private bool ShouldEmitInstruction(IRInstruction instruction)
        {
            // Non-values are usually control-flow or statements and should be emitted
            if (instruction is IRReturn or IRBranch or IRConditionalBranch or IRSwitch or IRLabel)
                return true;

            if (instruction is IRComment)
                return _options.GenerateComments;

            if (instruction is IRStore or IRAssignment)
                return true;

            if (instruction is IRAlloca or IRPhi or IRAwait)
                return false;

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

        private bool IsNamedDestination(IRValue value)
        {
            if (value == null) return false;
            if (string.IsNullOrEmpty(value.Name)) return false;
            return _declaredIdentifiers.Contains(value.Name);
        }

        private string GetValueName(IRValue value)
        {
            if (value is IRConstant constant)
                return EmitConstant(constant);

            if (_valueNames.TryGetValue(value, out var name))
                return name;

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

                        // If it's a real variable, use its name; if it's a temp "register",
                        // try to inline its defining value.
                        if (_declaredIdentifiers.Contains(v.Name) || v.IsParameter || v.IsGlobal)
                        {
                            var varName = GetValueName(v);

                            // Check if this global is from a different module and needs qualification
                            if (v.IsGlobal)
                            {
                                if (!string.IsNullOrEmpty(v.ModuleName) && _currentFunction != null)
                                {
                                    var currentModuleName = _currentFunction.ModuleName;
                                    if (!string.Equals(v.ModuleName, currentModuleName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // Qualify with module name
                                        // Note: "Main" module gets renamed to "Program" in C# output
                                        var qualifyingModule = v.ModuleName;
                                        if (qualifyingModule.Equals("Main", StringComparison.OrdinalIgnoreCase))
                                        {
                                            qualifyingModule = "Program";
                                        }
                                        return $"{qualifyingModule}.{varName}";
                                    }
                                }
                            }
                            return varName;
                        }

                        if (!string.IsNullOrEmpty(v.Name) && _tempDefsByName.TryGetValue(v.Name, out var def))
                            return EmitExpression(def, stack, needsParens);

                        return GetValueName(v);

                    case IRBinaryOp bin:
                    {
                        // Sub-expressions need parens to preserve precedence
                        var left = EmitExpression(bin.Left, stack, true);
                        var right = EmitExpression(bin.Right, stack, true);
                        var op = MapBinaryOperator(bin.Operation);
                        var expr = $"{left} {op} {right}";
                        return needsParens ? $"({expr})" : expr;
                    }

                    case IRUnaryOp un:
                    {
                        var operand = EmitExpression(un.Operand, stack, true);
                        var op = MapUnaryOperator(un.Operation);
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
                        var fn = call.FunctionName.Contains(".")
                            ? string.Join(".", call.FunctionName.Split('.').Select(SanitizeName))
                            : SanitizeName(call.FunctionName);
                        var args = string.Join(", ", argExprs);
                        return $"{fn}({args})";
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
                        var target = MapType(cast.Type);
                        var expr = EmitExpression(cast.Value, stack, false);
                        return $"({target}){expr}";
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
                                var fn = SanitizeName(call.FunctionName);
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
                        return $"{obj}.{methodName}({args})";
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
                        return $"{obj}.{fieldName}";
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
                    return call.Arguments;
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
                    return new[] { sw.Value };
                case IRGetElementPtr gep:
                    var ops = new List<IRValue> { gep.BasePointer };
                    ops.AddRange(gep.Indices);
                    return ops;
                case IRPhi phi:
                    return phi.Operands.Select(i => i.Value).ToList();
                case IRTupleElement tupleElem:
                    return new[] { tupleElem.Tuple };
                default:
                    return Array.Empty<IRValue>();
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
            var right = EmitExpression(binaryOp.Right, new HashSet<IRValue>(), needsParens: true);
            var op = MapBinaryOperator(binaryOp.Operation);

            var target = GetValueName(binaryOp);
            WriteLine($"{target} = {left} {op} {right};");
        }

        public void Visit(IRUnaryOp unaryOp)
        {
            if (!IsNamedDestination(unaryOp))
                return;

            // Use needsParens=true for sub-expressions to preserve operator precedence
            var operand = EmitExpression(unaryOp.Operand, new HashSet<IRValue>(), needsParens: true);
            var op = MapUnaryOperator(unaryOp.Operation);

            var target = GetValueName(unaryOp);
            WriteLine($"{target} = {op}{operand};");
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

            // Format arguments, adding 'ref' prefix for ByRef parameters
            var argExprs = call.Arguments.Select((arg, i) =>
            {
                var expr = EmitExpression(arg);
                bool isByRef = call.ByRefArguments != null && i < call.ByRefArguments.Count && call.ByRefArguments[i];
                return isByRef ? $"ref {expr}" : expr;
            }).ToArray();

            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

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
            var sanitizedName = functionName.Contains(".")
                ? string.Join(".", functionName.Split('.').Select(SanitizeName))
                : SanitizeName(functionName);

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

                bool isLastBlock = ret.ParentBlock?.Successors?.Count == 0;

                if (isVoidFunction && isLastBlock)
                {
                    // Don't emit unnecessary return at end of void method
                    return;
                }

                WriteLine("return;");
            }
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
            var targetType = MapType(cast.Type);
            var target = GetValueName(cast);

            WriteLine($"{target} = ({targetType}){value};");
        }

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
            var arrayName = GetValueName(arrayStore.Array);
            var indexVal = arrayStore.Index is IRConstant c ? c.Value.ToString() : GetValueName(arrayStore.Index);
            var valueVal = arrayStore.Value is IRConstant vc ? EmitConstant(vc) : GetValueName(arrayStore.Value);
            WriteLine($"{arrayName}[{indexVal}] = {valueVal};");
        }

        public void Visit(IRAwait awaitInst)
        {
            string exprVal;

            // If the expression is an IRCall, emit the call inline with await
            if (awaitInst.Expression is IRCall call)
            {
                var argExprs = call.Arguments.Select(EmitExpression).ToArray();
                var argsStr = string.Join(", ", argExprs);
                exprVal = $"{SanitizeName(call.FunctionName)}({argsStr})";
            }
            else
            {
                exprVal = awaitInst.Expression is IRConstant c ? EmitConstant(c) : GetValueName(awaitInst.Expression);
            }

            var resultType = MapType(awaitInst.Type);
            WriteLine($"{resultType} {awaitInst.Name} = await {exprVal};");
        }

        public void Visit(IRYield yieldInst)
        {
            if (yieldInst.IsBreak)
            {
                WriteLine("yield break;");
            }
            else
            {
                var valueVal = yieldInst.Value is IRConstant c ? EmitConstant(c) : GetValueName(yieldInst.Value);
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
            var args = string.Join(", ", methodCall.Arguments.Select(EmitExpression));

            // Emit as statement (no assignment) if:
            // - Type is null or Void
            // - Name is null (no temp var was assigned)
            // - Result is unused (GetUseCount == 0)
            // This handles .NET method calls where we don't know the actual return type
            if (methodCall.Type == null || methodCall.Type.Name == "Void" ||
                string.IsNullOrEmpty(methodCall.Name) || GetUseCount(methodCall) == 0)
            {
                WriteLine($"{obj}.{methodName}({args});");
            }
            else
            {
                var resultType = MapType(methodCall.Type);
                WriteLine($"{resultType} {methodCall.Name} = {obj}.{methodName}({args});");
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
            WriteLine($"{type} {fieldAccess.Name} = {obj}.{fieldName};");
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

            // Handle try block's terminator (may have nested control flow)
            var tryTerminator = tryCatch.TryBlock.Instructions.LastOrDefault();
            if (tryTerminator is IRConditionalBranch tryCond)
            {
                HandleConditionalBranch(tryCond);
            }
            else if (tryTerminator is IRBranch tryBranch &&
                     tryBranch.Target != tryCatch.EndBlock &&
                     !_processedBlocks.Contains(tryBranch.Target))
            {
                HandleUnconditionalBranch(tryBranch);
            }

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

                // Handle catch block's terminator
                var catchTerminator = catchClause.Block.Instructions.LastOrDefault();
                if (catchTerminator is IRConditionalBranch catchCond)
                {
                    HandleConditionalBranch(catchCond);
                }
                else if (catchTerminator is IRBranch catchBranch &&
                         catchBranch.Target != tryCatch.EndBlock &&
                         !_processedBlocks.Contains(catchBranch.Target))
                {
                    HandleUnconditionalBranch(catchBranch);
                }

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

                Unindent();
                WriteLine("}");
            }

            // Continue with end block
            if (tryCatch.EndBlock != null && !_processedBlocks.Contains(tryCatch.EndBlock))
            {
                _processedBlocks.Add(tryCatch.EndBlock);
                EmitBlockInstructions(tryCatch.EndBlock);

                var endTerminator = tryCatch.EndBlock.Instructions.LastOrDefault();
                if (endTerminator is IRConditionalBranch endCond)
                    HandleConditionalBranch(endCond);
                else if (endTerminator is IRBranch endBranch)
                    HandleUnconditionalBranch(endBranch);
            }
        }

        public void Visit(IRForEach forEach)
        {
            var elemType = MapType(forEach.ElementType);
            var varName = SanitizeName(forEach.VariableName);
            var collectionExpr = GetValueName(forEach.Collection);

            WriteLine($"foreach ({elemType} {varName} in {collectionExpr})");
            WriteLine("{");
            Indent();

            // Generate body block
            _processedBlocks.Add(forEach.BodyBlock);
            EmitBlockInstructions(forEach.BodyBlock);

            // Handle body block's terminator
            var bodyTerminator = forEach.BodyBlock.Instructions.LastOrDefault();
            if (bodyTerminator is IRConditionalBranch bodyCond)
            {
                HandleConditionalBranch(bodyCond);
            }
            else if (bodyTerminator is IRBranch bodyBranch &&
                     bodyBranch.Target != forEach.EndBlock &&
                     !_processedBlocks.Contains(bodyBranch.Target))
            {
                HandleUnconditionalBranch(bodyBranch);
            }

            Unindent();
            WriteLine("}");

            // Continue with end block
            if (forEach.EndBlock != null && !_processedBlocks.Contains(forEach.EndBlock))
            {
                _processedBlocks.Add(forEach.EndBlock);
                EmitBlockInstructions(forEach.EndBlock);

                var endTerminator = forEach.EndBlock.Instructions.LastOrDefault();
                if (endTerminator is IRConditionalBranch endCond)
                    HandleConditionalBranch(endCond);
                else if (endTerminator is IRBranch endBranch)
                    HandleUnconditionalBranch(endBranch);
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

            if (constant.Value is float f)
                return $"{f}f";

            return constant.Value.ToString();
        }

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
                return $"{elementType}[]";
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

        private string MapBinaryOperator(BinaryOpKind op) => op switch
        {
            BinaryOpKind.Add => "+",
            BinaryOpKind.Sub => "-",
            BinaryOpKind.Mul => "*",
            BinaryOpKind.Div => "/",
            BinaryOpKind.Mod => "%",
            BinaryOpKind.IntDiv => "/",
            BinaryOpKind.And => "&&",
            BinaryOpKind.Or => "||",
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
