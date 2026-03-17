using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// C++ code generator - transpiles IR to C++
    /// Targets C++17 for modern language features
    /// </summary>
    public class CppCodeGenerator : CodeGeneratorBase
    {
        private readonly StringBuilder _output;
        private readonly CppCodeGenOptions _options;
        private readonly HashSet<IRValue> _allTemporaries;
        private readonly List<string> _headerIncludes;
        private readonly HashSet<string> _declaredIdentifiers;
        private bool _usesFramework;
        private readonly HashSet<string> _frameworkFunctionsUsed;
        private IRModule _module;

        public override string BackendName => "C++";
        public override TargetPlatform Target => TargetPlatform.Cpp;

        public CppCodeGenerator(CppCodeGenOptions options = null)
        {
            _output = new StringBuilder();
            _options = options ?? new CppCodeGenOptions();
            _allTemporaries = new HashSet<IRValue>();
            _headerIncludes = new List<string> { "iostream", "vector", "string", "memory" };
            _declaredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _frameworkFunctionsUsed = new HashSet<string>();
            _typeMapper = new CppTypeMapper();
        }
        
        public override string Generate(IRModule module)
        {
            _module = module;
            _output.Clear();
            _valueNames.Clear();
            _allTemporaries.Clear();
            _usesFramework = false;
            _frameworkFunctionsUsed.Clear();
            _tempCounter = 0;

            // Generate header
            GenerateHeader(module);

            // Generate forward declarations for classes
            if (module.Classes.Count > 0)
            {
                WriteLine("// Forward declarations");
                foreach (var irClass in module.Classes.Values)
                {
                    WriteLine($"class {SanitizeName(irClass.Name)};");
                }
                WriteLine();
            }

            // Generate enums
            if (module.Enums.Count > 0)
            {
                WriteLine("// Enums");
                foreach (var irEnum in module.Enums.Values)
                {
                    GenerateEnum(irEnum);
                    WriteLine();
                }
            }

            // Generate delegate types (using std::function)
            if (module.Delegates.Count > 0)
            {
                WriteLine("// Delegate types");
                foreach (var irDelegate in module.Delegates.Values)
                {
                    GenerateDelegate(irDelegate);
                }
                WriteLine();
            }

            // Generate interfaces (abstract classes)
            if (module.Interfaces.Count > 0)
            {
                WriteLine("// Interfaces (abstract classes)");
                foreach (var irInterface in module.Interfaces.Values)
                {
                    GenerateInterface(irInterface);
                    WriteLine();
                }
            }

            // Generate classes
            if (module.Classes.Count > 0)
            {
                WriteLine("// Classes");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateClass(irClass);
                    WriteLine();
                }

                // Generate static member initializations outside class
                WriteLine("// Static member initializations");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateStaticMemberInitializations(irClass);
                }
                if (module.Classes.Values.Any(c => c.Fields.Any(f => f.IsStatic)))
                    WriteLine();
            }

            // Generate global variables
            if (module.GlobalVariables.Count > 0)
            {
                WriteLine("// Global variables");
                foreach (var globalVar in module.GlobalVariables.Values)
                {
                    var type = MapType(globalVar.Type);
                    var name = SanitizeName(globalVar.Name);
                    WriteLine($"{type} {name} = {{}};");
                }
                WriteLine();
            }

            // Generate extern "C" declarations for C library interop
            if (module.ExternDeclarations.Count > 0)
            {
                WriteLine("// External C library declarations");
                WriteLine("extern \"C\" {");
                Indent();
                foreach (var externDecl in module.ExternDeclarations.Values)
                {
                    GenerateExternDeclaration(externDecl);
                }
                Unindent();
                WriteLine("}");
                WriteLine();
            }

            // Get standalone functions (not class methods)
            var standaloneFunctions = module.Functions
                .Where(f => !f.IsExternal && !IsClassMethod(f, module))
                .ToList();

            // Generate function declarations
            if (standaloneFunctions.Count > 0)
            {
                WriteLine("// Function declarations");
                foreach (var function in standaloneFunctions)
                {
                    GenerateFunctionDeclaration(function);
                }
                WriteLine();

                // Generate function implementations
                WriteLine("// Function implementations");
                foreach (var function in standaloneFunctions)
                {
                    GenerateFunction(function);
                    WriteLine();
                }
            }

            // Generate main function if requested
            if (_options.GenerateMainFunction)
            {
                GenerateMainFunction(module);
            }

            var result = _output.ToString();

            // Insert framework extern declarations if game framework functions were used
            if (_usesFramework)
            {
                var marker = "using namespace std;";
                var insertIdx = result.IndexOf(marker);
                if (insertIdx >= 0)
                {
                    // Find the end of the line containing "using namespace std;"
                    var lineEnd = result.IndexOf('\n', insertIdx);
                    if (lineEnd < 0) lineEnd = result.Length;
                    else lineEnd++; // include the \n
                    result = result.Insert(lineEnd, "\n" + GenerateFrameworkExternDeclarations() + "\n");
                }
            }

            return result;
        }

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
        
        private void GenerateHeader(IRModule module)
        {
            WriteLine("#pragma once");

            // Collect unique includes
            var includes = new HashSet<string> { "iostream", "vector", "string", "cstdint", "cmath", "algorithm", "cstdlib", "ctime", "functional" };
            foreach (var inc in _headerIncludes)
            {
                includes.Add(inc);
            }

            foreach (var include in includes)
            {
                WriteLine($"#include <{include}>");
            }

            WriteLine();
            WriteLine("using namespace std;");
            WriteLine();
        }

        private void GenerateEnum(IREnum irEnum)
        {
            var enumName = SanitizeName(irEnum.Name);

            // Use C++11 enum class for type safety
            var underlyingType = "";
            if (irEnum.UnderlyingType != null && irEnum.UnderlyingType.Name != "Int32")
            {
                underlyingType = " : " + MapType(irEnum.UnderlyingType);
            }

            WriteLine($"enum class {enumName}{underlyingType}");
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
            WriteLine("};");
        }

        private void GenerateDelegate(IRDelegate irDelegate)
        {
            var delegateName = SanitizeName(irDelegate.Name);
            var returnType = MapType(irDelegate.ReturnType);
            var paramTypes = string.Join(", ", irDelegate.Parameters.Select(p => MapTypeName(p.TypeName)));

            // Use std::function for delegate types
            WriteLine($"using {delegateName} = std::function<{returnType}({paramTypes})>;");
        }

        private string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "void*";
            return typeName.ToLowerInvariant() switch
            {
                "integer" => "int32_t",
                "long" => "int64_t",
                "single" => "float",
                "double" => "double",
                "string" => "std::string",
                "boolean" => "bool",
                "byte" => "uint8_t",
                "short" => "int16_t",
                "object" => "void*",
                "void" => "void",
                _ => SanitizeName(typeName)
            };
        }

        private void GenerateInterface(IRInterface irInterface)
        {
            var interfaceName = SanitizeName(irInterface.Name);

            // Interfaces become abstract classes in C++
            var baseList = "";
            if (irInterface.BaseInterfaces.Count > 0)
            {
                baseList = " : " + string.Join(", ", irInterface.BaseInterfaces.Select(b => $"public {SanitizeName(b)}"));
            }

            WriteLine($"class {interfaceName}{baseList}");
            WriteLine("{");
            WriteLine("public:");
            Indent();

            // Virtual destructor for proper cleanup
            WriteLine($"virtual ~{interfaceName}() = default;");
            WriteLine();

            // Generate pure virtual method declarations
            foreach (var method in irInterface.Methods)
            {
                var returnType = MapType(method.ReturnType);
                var methodName = SanitizeName(method.Name);
                var paramList = string.Join(", ", method.Parameters.Select(p =>
                    $"{MapTypeName(p.TypeName)} {SanitizeName(p.Name)}"));

                WriteLine($"virtual {returnType} {methodName}({paramList}) = 0;");
            }

            // Generate property getter/setter declarations
            foreach (var prop in irInterface.Properties)
            {
                var propType = MapType(prop.Type);
                var propName = SanitizeName(prop.Name);
                if (prop.HasGetter)
                    WriteLine($"virtual {propType} get_{propName}() = 0;");
                if (prop.HasSetter)
                    WriteLine($"virtual void set_{propName}({propType} value) = 0;");
            }

            Unindent();
            WriteLine("};");
        }

        private void GenerateStaticMemberInitializations(IRClass irClass)
        {
            var className = SanitizeName(irClass.Name);
            var staticFields = irClass.Fields.Where(f => f.IsStatic).ToList();

            foreach (var field in staticFields)
            {
                var type = MapType(field.Type);
                var name = SanitizeName(field.Name);
                var defaultValue = field.Initializer != null
                    ? (field.Initializer is IRConstant c ? EmitConstant(c) : GetValueName(field.Initializer))
                    : GetDefaultValue(field.Type);

                WriteLine($"{type} {className}::{name} = {defaultValue};");
            }
        }

        private void GenerateClass(IRClass irClass)
        {
            var className = SanitizeName(irClass.Name);

            // Build inheritance list
            var baseList = new List<string>();
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                baseList.Add($"public {SanitizeName(irClass.BaseClass)}");
            }
            foreach (var iface in irClass.Interfaces)
            {
                baseList.Add($"public {SanitizeName(iface)}");
            }

            var inheritance = baseList.Count > 0 ? " : " + string.Join(", ", baseList) : "";

            WriteLine($"class {className}{inheritance}");
            WriteLine("{");

            // Private members
            var privateFields = irClass.Fields.Where(f => f.Access == AccessModifier.Private).ToList();
            if (privateFields.Count > 0)
            {
                WriteLine("private:");
                Indent();
                foreach (var field in privateFields)
                {
                    var staticMod = field.IsStatic ? "static " : "";
                    var type = MapType(field.Type);
                    var name = SanitizeName(field.Name);
                    WriteLine($"{staticMod}{type} {name};");
                }
                Unindent();
                WriteLine();
            }

            // Protected members
            var protectedFields = irClass.Fields.Where(f => f.Access == AccessModifier.Protected).ToList();
            if (protectedFields.Count > 0)
            {
                WriteLine("protected:");
                Indent();
                foreach (var field in protectedFields)
                {
                    var staticMod = field.IsStatic ? "static " : "";
                    var type = MapType(field.Type);
                    var name = SanitizeName(field.Name);
                    WriteLine($"{staticMod}{type} {name};");
                }
                Unindent();
                WriteLine();
            }

            // Public members
            WriteLine("public:");
            Indent();

            // Public fields
            var publicFields = irClass.Fields.Where(f => f.Access == AccessModifier.Public).ToList();
            foreach (var field in publicFields)
            {
                var staticMod = field.IsStatic ? "static " : "";
                var type = MapType(field.Type);
                var name = SanitizeName(field.Name);
                WriteLine($"{staticMod}{type} {name};");
            }

            if (publicFields.Count > 0)
                WriteLine();

            // Constructors
            foreach (var ctor in irClass.Constructors)
            {
                GenerateConstructor(irClass, ctor);
            }

            // Destructor - virtual if has base class, interfaces, or virtual methods
            bool needsVirtualDestructor = !string.IsNullOrEmpty(irClass.BaseClass) ||
                                         irClass.Interfaces.Count > 0 ||
                                         irClass.Methods.Any(m => m.IsVirtual || m.IsOverride);

            if (needsVirtualDestructor)
            {
                WriteLine($"virtual ~{className}() {{}}");
                WriteLine();
            }
            else
            {
                WriteLine($"~{className}() = default;");
                WriteLine();
            }

            // Properties (as getter/setter methods)
            foreach (var prop in irClass.Properties)
            {
                GenerateProperty(irClass, prop);
            }

            // Generate simple inline getters/setters for private fields with public access pattern
            GenerateSimplePropertyAccessors(irClass);

            // Events
            foreach (var evt in irClass.Events)
            {
                GenerateEvent(evt);
            }

            // Methods
            foreach (var method in irClass.Methods)
            {
                GenerateMethod(irClass, method);
            }

            Unindent();
            WriteLine("};");
        }

        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var className = SanitizeName(irClass.Name);

            // Generate parameter list from implementation with const references for complex types
            var paramList = "";
            if (ctor.Implementation != null)
            {
                paramList = string.Join(", ", ctor.Implementation.Parameters.Select(p =>
                {
                    var paramType = MapType(p.Type);
                    // Use const reference for string and complex types
                    if (paramType == "std::string" || (p.Type != null && p.Type.Kind == TypeKind.Class))
                        return $"const {paramType}& {SanitizeName(p.Name)}";
                    return $"{paramType} {SanitizeName(p.Name)}";
                }));
            }

            // Build comprehensive initializer list
            var initItems = new List<string>();

            // Base constructor call first
            if (!string.IsNullOrEmpty(irClass.BaseClass) && ctor.BaseConstructorArgs.Count > 0)
            {
                var baseArgs = string.Join(", ", ctor.BaseConstructorArgs.Select(a =>
                    a is IRConstant c ? EmitConstant(c) : SanitizeName(a.Name)));
                initItems.Add($"{SanitizeName(irClass.BaseClass)}({baseArgs})");
            }
            else if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                // Default base constructor call
                initItems.Add($"{SanitizeName(irClass.BaseClass)}()");
            }

            // Field initializations - match constructor parameters to fields
            if (ctor.Implementation != null)
            {
                foreach (var param in ctor.Implementation.Parameters)
                {
                    // Find matching field (by name or by backing field pattern)
                    var field = irClass.Fields.FirstOrDefault(f =>
                        f.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase) ||
                        f.Name.Equals("_" + param.Name, StringComparison.OrdinalIgnoreCase));

                    if (field != null)
                    {
                        var fieldName = SanitizeName(field.Name);
                        var paramName = SanitizeName(param.Name);
                        initItems.Add($"{fieldName}({paramName})");
                    }
                }
            }

            var initList = initItems.Count > 0 ? " : " + string.Join(", ", initItems) : "";

            WriteLine($"{className}({paramList}){initList}");
            WriteLine("{");
            Indent();

            // Generate body from implementation
            if (ctor.Implementation != null)
            {
                _currentFunction = ctor.Implementation;
                InitializeFunctionContext(ctor.Implementation);
                DeclareLocalsAndTemporaries(ctor.Implementation);

                if (ctor.Implementation.EntryBlock != null)
                    GenerateBlock(ctor.Implementation.EntryBlock, new HashSet<BasicBlock>());

                _currentFunction = null;
            }

            Unindent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateSimplePropertyAccessors(IRClass irClass)
        {
            // Find private fields that could have simple getters/setters
            // Pattern: private field _name could have getName/setName methods
            var privateFields = irClass.Fields.Where(f =>
                f.Access == AccessModifier.Private &&
                !f.IsStatic &&
                f.Name.StartsWith("_")).ToList();

            // Only generate if they don't already have complex property implementations
            foreach (var field in privateFields)
            {
                var fieldName = SanitizeName(field.Name);
                var propName = fieldName.TrimStart('_');

                // Capitalize first letter for property name
                if (propName.Length > 0)
                    propName = char.ToUpper(propName[0]) + propName.Substring(1);

                // Check if there's already a property with this name
                var hasProperty = irClass.Properties.Any(p =>
                    p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));

                if (hasProperty)
                    continue; // Skip if already has explicit property

                var type = MapType(field.Type);
                var returnType = type;
                var paramType = type;

                // Use const reference for complex types
                if (type == "std::string" || (field.Type != null && field.Type.Kind == TypeKind.Class))
                {
                    returnType = $"const {type}&";
                    paramType = $"const {type}&";
                }

                // Generate simple inline getter
                WriteLine($"// Auto-generated getter for {fieldName}");
                WriteLine($"{returnType} get{propName}() const {{ return {fieldName}; }}");
                WriteLine();

                // Generate simple inline setter
                WriteLine($"// Auto-generated setter for {fieldName}");
                WriteLine($"void set{propName}({paramType} value) {{ {fieldName} = value; }}");
                WriteLine();
            }
        }

        private void GenerateProperty(IRClass irClass, IRProperty prop)
        {
            var propType = MapType(prop.Type);
            var propName = SanitizeName(prop.Name);
            var staticMod = prop.IsStatic ? "static " : "";

            // Determine if getter should be const (non-static, read-only access)
            var constQualifier = (!prop.IsStatic) ? " const" : "";

            // Use const reference for return type if it's a string or class type
            var returnType = propType;
            if (propType == "std::string" || (prop.Type != null && prop.Type.Kind == TypeKind.Class))
                returnType = $"const {propType}&";

            // Getter - returns const reference for complex types, const method for non-static
            if (prop.Getter != null && !prop.IsWriteOnly)
            {
                WriteLine($"{staticMod}{returnType} get_{propName}(){constQualifier}");
                WriteLine("{");
                Indent();

                _currentFunction = prop.Getter;
                InitializeFunctionContext(prop.Getter);
                DeclareLocalsAndTemporaries(prop.Getter);

                if (prop.Getter.EntryBlock != null)
                    GenerateBlock(prop.Getter.EntryBlock, new HashSet<BasicBlock>());

                _currentFunction = null;
                Unindent();
                WriteLine("}");
                WriteLine();
            }

            // Setter - takes const reference for complex types
            if (prop.Setter != null && !prop.IsReadOnly)
            {
                var paramType = propType;
                if (propType == "std::string" || (prop.Type != null && prop.Type.Kind == TypeKind.Class))
                    paramType = $"const {propType}&";

                WriteLine($"{staticMod}void set_{propName}({paramType} value)");
                WriteLine("{");
                Indent();

                _currentFunction = prop.Setter;
                InitializeFunctionContext(prop.Setter);
                DeclareLocalsAndTemporaries(prop.Setter);

                if (prop.Setter.EntryBlock != null)
                    GenerateBlock(prop.Setter.EntryBlock, new HashSet<BasicBlock>());

                _currentFunction = null;
                Unindent();
                WriteLine("}");
                WriteLine();
            }
        }

        private void GenerateEvent(IREvent evt)
        {
            var delegateType = SanitizeName(evt.DelegateType);
            var eventName = SanitizeName(evt.Name);
            var staticMod = evt.IsStatic ? "static " : "";

            // Events in C++ are typically implemented as vectors of callbacks
            WriteLine($"{staticMod}std::vector<{delegateType}> {eventName}_handlers;");
            WriteLine();

            // Add handler method
            WriteLine($"{staticMod}void add_{eventName}({delegateType} handler)");
            WriteLine("{");
            Indent();
            WriteLine($"{eventName}_handlers.push_back(handler);");
            Unindent();
            WriteLine("}");
            WriteLine();

            // Remove handler method (simplified - removes last matching)
            WriteLine($"{staticMod}void remove_{eventName}({delegateType} handler)");
            WriteLine("{");
            Indent();
            WriteLine($"// Note: Simplified removal - C++ function comparison is complex");
            WriteLine($"if (!{eventName}_handlers.empty()) {eventName}_handlers.pop_back();");
            Unindent();
            WriteLine("}");
            WriteLine();

            // Raise event method
            var paramList = "";
            // For simplicity, events with no params
            WriteLine($"{staticMod}void raise_{eventName}()");
            WriteLine("{");
            Indent();
            WriteLine($"for (auto& handler : {eventName}_handlers) {{ handler(); }}");
            Unindent();
            WriteLine("}");
            WriteLine();
        }

        private void GenerateMethod(IRClass irClass, IRMethod method)
        {
            var returnType = MapType(method.ReturnType);
            var methodName = SanitizeName(method.Name);
            var staticMod = method.IsStatic ? "static " : "";
            var virtualMod = method.IsVirtual && !method.IsOverride ? "virtual " : "";
            var overrideMod = method.IsOverride ? " override" : "";

            // Generate parameter list
            var paramList = "";
            if (method.Implementation != null)
            {
                paramList = string.Join(", ", method.Implementation.Parameters.Select(p =>
                    $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
            }

            WriteLine($"{virtualMod}{staticMod}{returnType} {methodName}({paramList}){overrideMod}");
            WriteLine("{");
            Indent();

            // Generate body
            if (method.Implementation != null)
            {
                _currentFunction = method.Implementation;
                InitializeFunctionContext(method.Implementation);
                DeclareLocalsAndTemporaries(method.Implementation);

                if (method.Implementation.EntryBlock != null)
                    GenerateBlock(method.Implementation.EntryBlock, new HashSet<BasicBlock>());

                _currentFunction = null;
            }

            Unindent();
            WriteLine("}");
            WriteLine();
        }

        private void InitializeFunctionContext(IRFunction function)
        {
            _valueNames.Clear();
            _declaredIdentifiers.Clear();
            _allTemporaries.Clear();
            _tempCounter = 0;

            // Track declared identifiers
            foreach (var param in function.Parameters)
                _declaredIdentifiers.Add(param.Name);

            foreach (var local in function.LocalVariables)
                _declaredIdentifiers.Add(local.Name);

            if (_module != null)
            {
                foreach (var g in _module.GlobalVariables.Values)
                    _declaredIdentifiers.Add(g.Name);
            }

            // Collect temporaries (values that aren't named destinations)
            foreach (var block in function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    if (instruction is IRValue value && !(value is IRConstant))
                    {
                        if (!IsNamedDestination(value))
                        {
                            _allTemporaries.Add(value);
                        }
                    }
                }
            }
        }

        private void DeclareLocalsAndTemporaries(IRFunction function)
        {
            // Declare local variables
            foreach (var local in function.LocalVariables)
            {
                var localType = MapType(local.Type);
                var localName = SanitizeName(local.Name);
                if (localType != "void")
                {
                    var defaultVal = GetDefaultValue(local.Type);
                    WriteLine($"{localType} {localName} = {defaultVal};");
                }
            }

            // Declare temporaries with proper typing (skip void types)
            var tempsByType = _allTemporaries
                .Where(t => t.Type?.Name != "Void" && MapType(t.Type) != "void")
                .GroupBy(t => MapType(t.Type))
                .ToList();

            foreach (var group in tempsByType)
            {
                var cppType = group.Key;
                var tempNames = group.Select(GetValueName).Distinct().ToList();

                foreach (var tempName in tempNames)
                {
                    WriteLine($"{cppType} {tempName} = {{}};");
                }
            }

            if (function.LocalVariables.Count > 0 || _allTemporaries.Count > 0)
                WriteLine();
        }

        private string MapAccessModifier(AccessModifier access)
        {
            return access switch
            {
                AccessModifier.Public => "public",
                AccessModifier.Private => "private",
                AccessModifier.Protected => "protected",
                AccessModifier.Friend => "public",  // C++ doesn't have internal
                _ => "private"
            };
        }

        private void GenerateFunctionDeclaration(IRFunction function)
        {
            var returnType = MapType(function.ReturnType);
            var functionName = SanitizeName(function.Name);
            
            var parameters = string.Join(", ",
                function.Parameters.Select(p => $"{MapType(p.Type)} {GetValueName(p)}"));
            
            WriteLine($"{returnType} {functionName}({parameters});");
        }
        
        private void GenerateFunction(IRFunction function)
        {
            _currentFunction = function;
            InitializeFunctionContext(function);

            // Generate signature
            var returnType = MapType(function.ReturnType);
            var functionName = SanitizeName(function.Name);
            var parameters = string.Join(", ",
                function.Parameters.Select(p => $"{MapType(p.Type)} {GetValueName(p)}"));

            WriteLine($"{returnType} {functionName}({parameters})");
            WriteLine("{");
            Indent();

            DeclareLocalsAndTemporaries(function);

            // Generate body
            if (function.EntryBlock != null)
            {
                GenerateBlock(function.EntryBlock, new HashSet<BasicBlock>());
            }

            Unindent();
            WriteLine("}");
        }
        
        private void GenerateBlock(BasicBlock block, HashSet<BasicBlock> visited)
        {
            if (visited.Contains(block)) return;
            visited.Add(block);
            
            // Label (if needed)
            if (block.Predecessors.Count > 1 || block != _currentFunction.EntryBlock)
            {
                Unindent();
                WriteLine($"{block.Name}:");
                Indent();
            }
            
            // Instructions
            foreach (var instruction in block.Instructions)
            {
                instruction.Accept(this);
            }
            
            // Process successors
            foreach (var successor in block.Successors.Where(s => !visited.Contains(s)))
            {
                GenerateBlock(successor, visited);
            }
        }
        
        private void GenerateMainFunction(IRModule module)
        {
            WriteLine("int main(int argc, char* argv[])");
            WriteLine("{");
            Indent();
            
            var mainFunc = module.Functions.FirstOrDefault(f =>
                f.Name.Equals("Main", StringComparison.OrdinalIgnoreCase));
            
            if (mainFunc != null)
            {
                var functionName = SanitizeName(mainFunc.Name);
                if (mainFunc.ReturnType.Name == "Void")
                {
                    WriteLine($"{functionName}();");
                }
                else
                {
                    WriteLine($"auto result = {functionName}();");
                    WriteLine("cout << result << endl;");
                }
            }
            else
            {
                WriteLine("cout << \"No Main function found\" << endl;");
            }
            
            WriteLine("return 0;");
            Unindent();
            WriteLine("}");
        }
        
        protected override void InitializeTypeMap()
        {
            base.InitializeTypeMap();
            
            // C++ specific mappings
            _typeMap["Integer"] = "int32_t";
            _typeMap["Long"] = "int64_t";
            _typeMap["Single"] = "float";
            _typeMap["Double"] = "double";
            _typeMap["String"] = "std::string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "void*";
            _typeMap["Byte"] = "uint8_t";
            _typeMap["Short"] = "int16_t";
            _typeMap["SByte"] = "int8_t";
            _typeMap["UByte"] = "uint8_t";
            _typeMap["UShort"] = "uint16_t";
            _typeMap["UInteger"] = "uint32_t";
            _typeMap["ULong"] = "uint64_t";
            _typeMap["Decimal"] = "long double";
        }
        
        #region Visitor Methods
        
        public override void Visit(IRFunction function) { }
        public override void Visit(BasicBlock block) { }
        public override void Visit(IRConstant constant) { }
        public override void Visit(IRVariable variable) { }
        
        public override void Visit(IRBinaryOp binaryOp)
        {
            var left = GetValueName(binaryOp.Left);
            var right = GetValueName(binaryOp.Right);
            var op = MapBinaryOperator(binaryOp.Operation);
            var result = GetValueName(binaryOp);
            
            WriteLine($"{result} = {left} {op} {right};");
        }
        
        public override void Visit(IRUnaryOp unaryOp)
        {
            var operand = GetValueName(unaryOp.Operand);
            var op = MapUnaryOperator(unaryOp.Operation);
            var result = GetValueName(unaryOp);
            
            if (unaryOp.Operation == UnaryOpKind.Inc || unaryOp.Operation == UnaryOpKind.Dec)
            {
                WriteLine($"{result}{op};");
            }
            else
            {
                WriteLine($"{result} = {op}{operand};");
            }
        }
        
        public override void Visit(IRCompare compare)
        {
            var left = GetValueName(compare.Left);
            var right = GetValueName(compare.Right);
            var op = MapCompareOperator(compare.Comparison);
            var result = GetValueName(compare);
            
            WriteLine($"{result} = {left} {op} {right};");
        }
        
        public override void Visit(IRAssignment assignment)
        {
            var value = GetValueName(assignment.Value);
            var target = GetValueName(assignment.Target);
            
            WriteLine($"{target} = {value};");
        }
        
        public override void Visit(IRLoad load)
        {
            var address = GetValueName(load.Address);
            var result = GetValueName(load);
            
            WriteLine($"{result} = *{address};");
        }
        
        public override void Visit(IRStore store)
        {
            var value = GetValueName(store.Value);

            // For IRVariable addresses (local variables), emit as simple assignment
            if (store.Address is IRVariable variable)
            {
                var varName = SanitizeName(variable.Name);
                WriteLine($"{varName} = {value};");
                return;
            }

            // Array element store
            if (store.Address is IRGetElementPtr gep)
            {
                var baseExpr = GetValueName(gep.BasePointer);
                var index = GetValueName(gep.Indices[0]);
                WriteLine($"{baseExpr}[{index}] = {value};");
                return;
            }

            var address = GetValueName(store.Address);

            // Check if this is a pointer store or a regular assignment
            if (store.Address.Type?.Kind == TypeKind.Pointer)
            {
                WriteLine($"*{address} = {value};");
            }
            else
            {
                // Regular variable assignment
                WriteLine($"{address} = {value};");
            }
        }
        
        public override void Visit(IRCall call)
        {
            var args = call.Arguments.Select(GetValueName).ToList();
            var functionName = call.FunctionName;
            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Check if this is an extern function call
            if (_module != null && _module.IsExtern(functionName))
            {
                var externDecl = _module.GetExtern(functionName);
                if (externDecl != null && externDecl.HasImplementation("Cpp"))
                {
                    var impl = externDecl.GetImplementation("Cpp");
                    var argsArr = args.ToArray();

                    string externCall;
                    if (impl.Contains("{"))
                    {
                        externCall = string.Format(impl, argsArr);
                    }
                    else
                    {
                        externCall = $"{impl}({string.Join(", ", args)})";
                    }

                    if (hasReturn && IsNamedDestination(call))
                    {
                        var target = SanitizeName(call.Name);
                        WriteLine($"{target} = {externCall};");
                    }
                    else
                    {
                        WriteLine($"{externCall};");
                    }
                    return;
                }
            }

            // Handle standard library calls
            var stdlibCall = EmitStdLibCall(functionName, args);
            if (stdlibCall != null)
            {
                if (hasReturn && IsNamedDestination(call))
                {
                    var target = SanitizeName(call.Name);
                    WriteLine($"{target} = {stdlibCall};");
                }
                else
                {
                    WriteLine($"{stdlibCall};");
                }
                return;
            }

            // Regular function call
            var sanitizedName = SanitizeName(functionName);
            var argsStr = string.Join(", ", args);

            // If this call result is assigned to a declared variable, emit directly
            if (hasReturn && IsNamedDestination(call))
            {
                var target = SanitizeName(call.Name);
                WriteLine($"{target} = {sanitizedName}({argsStr});");
            }
            else if (hasReturn && !string.IsNullOrEmpty(call.Name))
            {
                // Otherwise use temp variable
                var result = GetValueName(call);
                WriteLine($"{result} = {sanitizedName}({argsStr});");
            }
            else
            {
                // Void call
                WriteLine($"{sanitizedName}({argsStr});");
            }
        }

        private string GetArg(List<string> args, int index) => index < args.Count ? args[index] : "0";

        private string EmitStdLibCall(string functionName, List<string> args)
        {
            // Check game framework functions first (case-insensitive match)
            var frameworkCall = EmitFrameworkCall(functionName, args);
            if (frameworkCall != null) return frameworkCall;

            return functionName.ToLower() switch
            {
                "print" => $"cout << {args[0]}",
                "printline" => $"cout << {args[0]} << endl",
                "readline" => "([](){ string s; getline(cin, s); return s; })()",
                "len" => $"static_cast<int32_t>({args[0]}.length())",
                "left" => $"{args[0]}.substr(0, {args[1]})",
                "right" => $"{args[0]}.substr({args[0]}.length() - {args[1]})",
                "mid" => $"{args[0]}.substr({args[1]} - 1, {args[2]})",
                "trim" => $"([](string s){{ auto start = s.find_first_not_of(\" \\t\"); auto end = s.find_last_not_of(\" \\t\"); return start == string::npos ? \"\" : s.substr(start, end - start + 1); }})({args[0]})",
                "ucase" => $"([](string s){{ transform(s.begin(), s.end(), s.begin(), ::toupper); return s; }})({args[0]})",
                "lcase" => $"([](string s){{ transform(s.begin(), s.end(), s.begin(), ::tolower); return s; }})({args[0]})",
                "instr" => $"static_cast<int32_t>({args[0]}.find({args[1]}) + 1)",
                "replace" => $"([](string s, const string& from, const string& to){{ size_t pos = 0; while ((pos = s.find(from, pos)) != string::npos) {{ s.replace(pos, from.length(), to); pos += to.length(); }} return s; }})({args[0]}, {args[1]}, {args[2]})",
                "abs" => $"abs({args[0]})",
                "sqrt" => $"sqrt({args[0]})",
                "pow" => $"pow({args[0]}, {args[1]})",
                "sin" => $"sin({args[0]})",
                "cos" => $"cos({args[0]})",
                "tan" => $"tan({args[0]})",
                "log" => $"log({args[0]})",
                "exp" => $"exp({args[0]})",
                "floor" => $"floor({args[0]})",
                "ceiling" => $"ceil({args[0]})",
                "round" => $"round({args[0]})",
                "min" => $"min({args[0]}, {args[1]})",
                "max" => $"max({args[0]}, {args[1]})",
                "cint" => $"static_cast<int32_t>({args[0]})",
                "clng" => $"static_cast<int64_t>({args[0]})",
                "cdbl" => $"static_cast<double>({args[0]})",
                "csng" => $"static_cast<float>({args[0]})",
                "cstr" => $"to_string({args[0]})",
                "cbool" => $"static_cast<bool>({args[0]})",
                "ubound" => $"(static_cast<int32_t>({args[0]}.size()) - 1)",
                "lbound" => "0",
                "rnd" => "(static_cast<double>(rand()) / RAND_MAX)",
                "randomize" => "srand(time(nullptr))",
                _ => null
            };
        }

        /// <summary>
        /// Emit calls to the VisualGameStudioEngine C DLL functions.
        /// These map directly to Framework_* exports from framework.h.
        /// </summary>
        private string EmitFrameworkCall(string functionName, List<string> args)
        {
            var allArgs = string.Join(", ", args);

            string result = functionName switch
            {
                // Core
                "GameInit" => $"Framework_Initialize({allArgs})",
                "GameShutdown" => "Framework_Shutdown()",
                "GameBeginFrame" => "Framework_BeginDrawing()",
                "GameEndFrame" => "Framework_EndDrawing()",
                "GameShouldClose" => "Framework_ShouldClose()",
                "GameGetDeltaTime" => "Framework_GetDeltaTime()",
                "GameGetFPS" => "Framework_GetFPS()",

                // Input
                "IsKeyPressed" => $"Framework_IsKeyPressed({allArgs})",
                "IsKeyDown" => $"Framework_IsKeyDown({allArgs})",
                "IsKeyReleased" => $"Framework_IsKeyReleased({allArgs})",
                "IsMouseButtonPressed" => $"Framework_IsMouseButtonPressed({allArgs})",
                "IsMouseButtonDown" => $"Framework_IsMouseButtonDown({allArgs})",
                "GetMouseX" => "Framework_GetMouseX()",
                "GetMouseY" => "Framework_GetMouseY()",

                // Drawing
                "ClearBackground" => $"Framework_ClearBackground((uint8_t)({GetArg(args, 0)}), (uint8_t)({GetArg(args, 1)}), (uint8_t)({GetArg(args, 2)}), 255)",
                "DrawRectangle" => $"Framework_DrawRectangle({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawCircle" => $"Framework_DrawCircle({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, (uint8_t)({GetArg(args, 3)}), (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}))",
                "DrawLine" => $"Framework_DrawLine({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawText" => $"Framework_DrawText({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawRectangleLines" => $"Framework_DrawRectangleLines({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, {GetArg(args, 3)}, (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}), (uint8_t)({GetArg(args, 7)}))",
                "DrawCircleLines" => $"Framework_DrawCircleLines({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, (uint8_t)({GetArg(args, 3)}), (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}))",
                "DrawTriangle" => $"Framework_DrawTriangle({allArgs})",
                "DrawTriangleLines" => $"Framework_DrawTriangleLines({allArgs})",
                "DrawFPS" => $"Framework_DrawFPS({allArgs})",

                // Textures
                "LoadTexture" => $"Framework_LoadTexture({allArgs})",
                "UnloadTexture" => $"Framework_UnloadTexture({allArgs})",
                "DrawTexture" => $"Framework_DrawTextureSimple({GetArg(args, 0)}, {GetArg(args, 1)}, {GetArg(args, 2)}, (uint8_t)({GetArg(args, 3)}), (uint8_t)({GetArg(args, 4)}), (uint8_t)({GetArg(args, 5)}), (uint8_t)({GetArg(args, 6)}))",
                "DrawTextureEx" => $"Framework_DrawTextureEx({allArgs})",

                // Entities
                "CreateEntity" => "Framework_Entity_Create()",
                "DestroyEntity" => $"Framework_Entity_Destroy({allArgs})",
                "EntitySetPosition" => $"Framework_Entity_SetPosition({allArgs})",
                "EntityGetX" => $"Framework_Entity_GetPositionX({allArgs})",
                "EntityGetY" => $"Framework_Entity_GetPositionY({allArgs})",
                "EntitySetVelocity" => $"Framework_Entity_SetVelocity({allArgs})",
                "EntitySetSprite" => $"Framework_Entity_SetSprite({allArgs})",
                "EntitySetCollider" => $"Framework_Entity_SetColliderBox({allArgs})",
                "EntityIsActive" => $"Framework_Entity_IsActive({allArgs})",
                "EntitySetActive" => $"Framework_Entity_SetActive({allArgs})",

                // Audio
                "LoadSound" => $"Framework_LoadSound({allArgs})",
                "PlaySound" => $"Framework_PlaySound({allArgs})",
                "StopSound" => $"Framework_StopSound({allArgs})",
                "SetSoundVolume" => $"Framework_SetSoundVolume({allArgs})",
                "LoadMusic" => $"Framework_LoadMusic({allArgs})",
                "PlayMusic" => $"Framework_PlayMusic({allArgs})",
                "StopMusic" => $"Framework_StopMusic({allArgs})",

                // Audio Init
                "InitAudio" => "Framework_InitAudio()",
                "CloseAudio" => "Framework_CloseAudio()",
                "SetMasterVolume" => $"Framework_SetMasterVolume({allArgs})",

                // Time
                "SetTargetFPS" => $"Framework_SetTargetFPS({allArgs})",
                "GetFrameTime" => "Framework_GetFrameTime()",
                "GetTime" => "Framework_GetTime()",
                "SetTimeScale" => $"Framework_SetTimeScale({allArgs})",
                "GetTimeScale" => "Framework_GetTimeScale()",

                // Camera
                "CameraSetPosition" => $"Framework_Camera_SetPosition({allArgs})",
                "CameraSetZoom" => $"Framework_Camera_SetZoom({allArgs})",
                "CameraFollow" => $"Framework_Camera_FollowEntity({allArgs})",
                "CameraBeginMode" => "Framework_Camera_BeginMode()",
                "CameraEndMode" => "Framework_Camera_EndMode()",
                "CameraUpdate" => $"Framework_Camera_Update({allArgs})",
                "CameraReset" => "Framework_Camera_Reset()",
                "CameraSetTarget" => $"Framework_Camera_SetTarget({allArgs})",
                "CameraSetOffset" => $"Framework_Camera_SetOffset({allArgs})",
                "CameraSetRotation" => $"Framework_Camera_SetRotation({allArgs})",
                "CameraGetZoom" => "Framework_Camera_GetZoom()",
                "CameraGetRotation" => "Framework_Camera_GetRotation()",
                "CameraPanTo" => $"Framework_Camera_PanTo({allArgs})",
                "CameraZoomTo" => $"Framework_Camera_ZoomTo({allArgs})",
                "CameraSetBounds" => $"Framework_Camera_SetBounds({allArgs})",
                "CameraSetBoundsEnabled" => $"Framework_Camera_SetBoundsEnabled({allArgs})",
                "CameraSetFollowLerp" => $"Framework_Camera_SetFollowLerp({allArgs})",
                "CameraShake" => $"Framework_Camera_Shake({allArgs})",

                // Cursor
                "ShowCursor" => "Framework_ShowCursor()",
                "HideCursor" => "Framework_HideCursor()",
                "IsCursorHidden" => "Framework_IsCursorHidden()",
                "SetMousePosition" => $"Framework_SetMousePosition({allArgs})",
                "GetMouseWheelMove" => "Framework_GetMouseWheelMove()",

                // Update
                "GameUpdate" => "Framework_Update()",

                _ => null
            };

            if (result != null)
            {
                _usesFramework = true;
                // Extract the C function name from the result (everything before the first '(')
                var parenIdx = result.IndexOf('(');
                if (parenIdx > 0)
                    _frameworkFunctionsUsed.Add(result.Substring(0, parenIdx));
            }

            return result;
        }

        /// <summary>
        /// Generate extern "C" declarations for Framework_* functions used in the code.
        /// These match the signatures in VisualGameStudioEngine/framework.h.
        /// </summary>
        private string GenerateFrameworkExternDeclarations()
        {
            // Map of Framework_* function name to its C declaration (without __declspec)
            var signatures = new Dictionary<string, string>
            {
                // Core
                ["Framework_Initialize"] = "bool Framework_Initialize(int width, int height, const char* title)",
                ["Framework_Update"] = "void Framework_Update()",
                ["Framework_ShouldClose"] = "bool Framework_ShouldClose()",
                ["Framework_Shutdown"] = "void Framework_Shutdown()",
                ["Framework_BeginDrawing"] = "void Framework_BeginDrawing()",
                ["Framework_EndDrawing"] = "void Framework_EndDrawing()",
                ["Framework_ClearBackground"] = "void Framework_ClearBackground(unsigned char r, unsigned char g, unsigned char b, unsigned char a)",

                // Timing
                ["Framework_SetTargetFPS"] = "void Framework_SetTargetFPS(int fps)",
                ["Framework_GetFrameTime"] = "float Framework_GetFrameTime()",
                ["Framework_GetDeltaTime"] = "float Framework_GetDeltaTime()",
                ["Framework_GetTime"] = "double Framework_GetTime()",
                ["Framework_GetFPS"] = "int Framework_GetFPS()",

                // Input - Keyboard
                ["Framework_IsKeyPressed"] = "bool Framework_IsKeyPressed(int key)",
                ["Framework_IsKeyDown"] = "bool Framework_IsKeyDown(int key)",
                ["Framework_IsKeyReleased"] = "bool Framework_IsKeyReleased(int key)",

                // Input - Mouse
                ["Framework_IsMouseButtonPressed"] = "bool Framework_IsMouseButtonPressed(int button)",
                ["Framework_IsMouseButtonDown"] = "bool Framework_IsMouseButtonDown(int button)",
                ["Framework_GetMouseX"] = "int Framework_GetMouseX()",
                ["Framework_GetMouseY"] = "int Framework_GetMouseY()",
                ["Framework_GetMouseWheelMove"] = "float Framework_GetMouseWheelMove()",

                // Cursor
                ["Framework_ShowCursor"] = "void Framework_ShowCursor()",
                ["Framework_HideCursor"] = "void Framework_HideCursor()",

                // Drawing - Shapes
                ["Framework_DrawText"] = "void Framework_DrawText(const char* text, int x, int y, int fontSize, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawRectangle"] = "void Framework_DrawRectangle(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawRectangleLines"] = "void Framework_DrawRectangleLines(int x, int y, int width, int height, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawCircle"] = "void Framework_DrawCircle(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawCircleLines"] = "void Framework_DrawCircleLines(int centerX, int centerY, float radius, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawLine"] = "void Framework_DrawLine(int startX, int startY, int endX, int endY, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawTriangle"] = "void Framework_DrawTriangle(int x1, int y1, int x2, int y2, int x3, int y3, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawTriangleLines"] = "void Framework_DrawTriangleLines(int x1, int y1, int x2, int y2, int x3, int y3, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawFPS"] = "void Framework_DrawFPS(int x, int y)",

                // Textures
                ["Framework_LoadTexture"] = "int Framework_LoadTexture(const char* fileName)",
                ["Framework_UnloadTexture"] = "void Framework_UnloadTexture(int texture)",
                ["Framework_DrawTextureSimple"] = "void Framework_DrawTextureSimple(int texture, int posX, int posY, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",
                ["Framework_DrawTextureEx"] = "void Framework_DrawTextureEx(int texture, float posX, float posY, float rotation, float scale, unsigned char r, unsigned char g, unsigned char b, unsigned char a)",

                // Entities
                ["Framework_Entity_Create"] = "int Framework_Entity_Create()",
                ["Framework_Entity_Destroy"] = "void Framework_Entity_Destroy(int id)",
                ["Framework_Entity_SetPosition"] = "void Framework_Entity_SetPosition(int id, float x, float y)",
                ["Framework_Entity_GetPositionX"] = "float Framework_Entity_GetPositionX(int id)",
                ["Framework_Entity_GetPositionY"] = "float Framework_Entity_GetPositionY(int id)",
                ["Framework_Entity_SetVelocity"] = "void Framework_Entity_SetVelocity(int id, float vx, float vy)",
                ["Framework_Entity_SetSprite"] = "void Framework_Entity_SetSprite(int id, int texture)",
                ["Framework_Entity_SetColliderBox"] = "void Framework_Entity_SetColliderBox(int id, float w, float h)",
                ["Framework_Entity_IsActive"] = "bool Framework_Entity_IsActive(int id)",
                ["Framework_Entity_SetActive"] = "void Framework_Entity_SetActive(int id, bool active)",

                // Audio
                ["Framework_InitAudio"] = "bool Framework_InitAudio()",
                ["Framework_LoadSound"] = "int Framework_LoadSound(const char* file)",
                ["Framework_PlaySound"] = "void Framework_PlaySound(int handle)",
                ["Framework_StopSound"] = "void Framework_StopSound(int handle)",
                ["Framework_SetSoundVolume"] = "void Framework_SetSoundVolume(int handle, float volume)",
                ["Framework_LoadMusic"] = "int Framework_LoadMusic(const char* file)",
                ["Framework_PlayMusic"] = "void Framework_PlayMusic(int handle)",
                ["Framework_StopMusic"] = "void Framework_StopMusic(int handle)",

                // Audio extras
                ["Framework_CloseAudio"] = "void Framework_CloseAudio()",
                ["Framework_SetMasterVolume"] = "void Framework_SetMasterVolume(float volume)",

                // Camera
                ["Framework_Camera_SetPosition"] = "void Framework_Camera_SetPosition(float x, float y)",
                ["Framework_Camera_SetTarget"] = "void Framework_Camera_SetTarget(float x, float y)",
                ["Framework_Camera_SetRotation"] = "void Framework_Camera_SetRotation(float rotation)",
                ["Framework_Camera_SetZoom"] = "void Framework_Camera_SetZoom(float zoom)",
                ["Framework_Camera_SetOffset"] = "void Framework_Camera_SetOffset(float x, float y)",
                ["Framework_Camera_GetZoom"] = "float Framework_Camera_GetZoom()",
                ["Framework_Camera_GetRotation"] = "float Framework_Camera_GetRotation()",
                ["Framework_Camera_FollowEntity"] = "void Framework_Camera_FollowEntity(int entity)",
                ["Framework_Camera_BeginMode"] = "void Framework_Camera_BeginMode()",
                ["Framework_Camera_EndMode"] = "void Framework_Camera_EndMode()",
                ["Framework_Camera_Update"] = "void Framework_Camera_Update(float dt)",
                ["Framework_Camera_Reset"] = "void Framework_Camera_Reset()",
                ["Framework_Camera_PanTo"] = "void Framework_Camera_PanTo(float worldX, float worldY, float duration)",
                ["Framework_Camera_ZoomTo"] = "void Framework_Camera_ZoomTo(float targetZoom, float duration)",
                ["Framework_Camera_SetBounds"] = "void Framework_Camera_SetBounds(float minX, float minY, float maxX, float maxY)",
                ["Framework_Camera_SetBoundsEnabled"] = "void Framework_Camera_SetBoundsEnabled(bool enabled)",
                ["Framework_Camera_SetFollowLerp"] = "void Framework_Camera_SetFollowLerp(float lerpSpeed)",
                ["Framework_Camera_Shake"] = "void Framework_Camera_Shake(float intensity, float duration)",

                // Timing extras
                ["Framework_SetTimeScale"] = "void Framework_SetTimeScale(float scale)",
                ["Framework_GetTimeScale"] = "float Framework_GetTimeScale()",

                // Cursor extras
                ["Framework_IsCursorHidden"] = "bool Framework_IsCursorHidden()",
                ["Framework_SetMousePosition"] = "void Framework_SetMousePosition(int x, int y)",
            };

            var sb = new StringBuilder();
            sb.AppendLine("// VisualGameStudioEngine framework function declarations");
            sb.AppendLine("// Link with VisualGameStudioEngine.dll");
            sb.AppendLine("#ifdef _WIN32");
            sb.AppendLine("#define FRAMEWORK_API __declspec(dllimport)");
            sb.AppendLine("#else");
            sb.AppendLine("#define FRAMEWORK_API");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine("extern \"C\" {");

            foreach (var funcName in _frameworkFunctionsUsed.OrderBy(f => f))
            {
                if (signatures.TryGetValue(funcName, out var sig))
                {
                    sb.AppendLine($"    FRAMEWORK_API {sig};");
                }
                else
                {
                    // Unknown function — emit a compiler error so it's caught at build time
                    sb.AppendLine($"    #error \"Unknown framework function: {funcName} — declaration not found, check framework.h\"");
                }
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        public override void Visit(IRReturn ret)
        {
            if (ret.Value != null)
            {
                var value = GetValueName(ret.Value);
                WriteLine($"return {value};");
            }
            else
            {
                WriteLine("return;");
            }
        }
        
        public override void Visit(IRBranch branch)
        {
            WriteLine($"goto {branch.Target.Name};");
        }
        
        public override void Visit(IRConditionalBranch condBranch)
        {
            var condition = GetValueName(condBranch.Condition);
            
            WriteLine($"if ({condition}) {{");
            Indent();
            WriteLine($"goto {condBranch.TrueTarget.Name};");
            Unindent();
            WriteLine("}");
            WriteLine($"else {{");
            Indent();
            WriteLine($"goto {condBranch.FalseTarget.Name};");
            Unindent();
            WriteLine("}");
        }
        
        public override void Visit(IRSwitch switchInst)
        {
            var value = GetValueName(switchInst.Value);
            
            WriteLine($"switch ({value}) {{");
            Indent();
            
            foreach (var (caseValue, target) in switchInst.Cases)
            {
                var caseVal = GetValueName(caseValue);
                WriteLine($"case {caseVal}: goto {target.Name};");
            }
            
            WriteLine($"default: goto {switchInst.DefaultTarget.Name};");
            
            Unindent();
            WriteLine("}");
        }
        
        public override void Visit(IRPhi phi)
        {
            WriteLine($"// Phi node: {phi.Name}");
        }
        
        public override void Visit(IRAlloca alloca)
        {
            // Memory allocation handled in declarations
        }
        
        public override void Visit(IRGetElementPtr gep)
        {
            var basePtr = GetValueName(gep.BasePointer);
            var result = GetValueName(gep);
            
            if (gep.Indices.Count == 1)
            {
                var index = GetValueName(gep.Indices[0]);
                WriteLine($"{result} = &{basePtr}[{index}];");
            }
            else
            {
                var indices = string.Join("][", gep.Indices.Select(GetValueName));
                WriteLine($"{result} = &{basePtr}[{indices}];");
            }
        }
        
        public override void Visit(IRCast cast)
        {
            var value = GetValueName(cast.Value);
            var targetType = MapType(cast.Type);
            var result = GetValueName(cast);
            
            WriteLine($"{result} = static_cast<{targetType}>({value});");
        }
        
        public override void Visit(IRLabel label)
        {
            Unindent();
            WriteLine($"{label.Name}:");
            Indent();
        }
        
        public override void Visit(IRComment comment)
        {
            if (_options.GenerateComments)
            {
                WriteLine($"// {comment.Text}");
            }
        }

        public override void Visit(IRArrayAlloc arrayAlloc)
        {
            var elementType = MapType(arrayAlloc.ElementType);
            WriteLine($"{elementType} {arrayAlloc.Name}[{arrayAlloc.Size}];");
        }

        public override void Visit(IRArrayStore arrayStore)
        {
            var arrayName = arrayStore.Array.Name;
            var indexVal = arrayStore.Index is IRConstant c ? c.Value.ToString() : GetValueName(arrayStore.Index);
            var valueVal = arrayStore.Value is IRConstant vc ? EmitConstant(vc) : GetValueName(arrayStore.Value);
            WriteLine($"{arrayName}[{indexVal}] = {valueVal};");
        }

        public override void Visit(IRAwait awaitInst)
        {
            // C++ doesn't have native async/await - emit compiler warning
            WriteLine($"#warning \"await is not supported in C++ backend - expression '{awaitInst.Expression?.Name ?? "expression"}' will be evaluated synchronously\"");
        }

        public override void Visit(IRYield yieldInst)
        {
            // C++ doesn't have native yield - emit compiler warning
            if (yieldInst.IsBreak)
                WriteLine("#warning \"yield break is not supported in C++ backend - iterator will not function correctly\"");
            else
                WriteLine($"#warning \"yield return is not supported in C++ backend - value '{yieldInst.Value?.Name ?? "value"}' will not be yielded\"");
        }

        public override void Visit(IRNewObject newObj)
        {
            var className = SanitizeName(newObj.ClassName);
            var args = string.Join(", ", newObj.Arguments.Select(a => GetValueName(a)));
            var type = MapType(newObj.Type);
            WriteLine($"{type} {newObj.Name} = {className}({args});");
        }

        public override void Visit(IRInstanceMethodCall methodCall)
        {
            var obj = GetValueName(methodCall.Object);
            var methodName = SanitizeName(methodCall.MethodName);
            var args = string.Join(", ", methodCall.Arguments.Select(a => GetValueName(a)));
            if (methodCall.Type == null || methodCall.Type.Name == "Void")
                WriteLine($"{obj}.{methodName}({args});");
            else
                WriteLine($"auto {methodCall.Name} = {obj}.{methodName}({args});");
        }

        public override void Visit(IRBaseMethodCall baseCall)
        {
            var methodName = SanitizeName(baseCall.MethodName);
            var args = string.Join(", ", baseCall.Arguments.Select(a => GetValueName(a)));

            // Find the current class from the module to get base class name
            string baseClassName = "Base";
            if (_module != null && _currentFunction != null)
            {
                foreach (var irClass in _module.Classes.Values)
                {
                    if (irClass.Methods.Any(m => m.Implementation == _currentFunction) ||
                        irClass.Constructors.Any(c => c.Implementation == _currentFunction) ||
                        irClass.Properties.Any(p => p.Getter == _currentFunction || p.Setter == _currentFunction))
                    {
                        if (!string.IsNullOrEmpty(irClass.BaseClass))
                            baseClassName = SanitizeName(irClass.BaseClass);
                        break;
                    }
                }
            }

            // Generate base class method call
            if (baseCall.Type == null || baseCall.Type.Name == "Void")
            {
                WriteLine($"{baseClassName}::{methodName}({args});");
            }
            else
            {
                var result = GetValueName(baseCall);
                WriteLine($"{result} = {baseClassName}::{methodName}({args});");
            }
        }

        public override void Visit(IRFieldAccess fieldAccess)
        {
            var obj = GetValueName(fieldAccess.Object);
            var fieldName = SanitizeName(fieldAccess.FieldName);
            var type = MapType(fieldAccess.Type);
            WriteLine($"{type} {fieldAccess.Name} = {obj}.{fieldName};");
        }

        public override void Visit(IRFieldStore fieldStore)
        {
            var obj = GetValueName(fieldStore.Object);
            var fieldName = SanitizeName(fieldStore.FieldName);
            var value = GetValueName(fieldStore.Value);
            WriteLine($"{obj}.{fieldName} = {value};");
        }

        public override void Visit(IRTupleElement tupleElement)
        {
            var tuple = GetValueName(tupleElement.Tuple);
            var type = MapType(tupleElement.Type);
            // C++ uses std::get<index>(tuple)
            WriteLine($"{type} {SanitizeName(tupleElement.Name)} = std::get<{tupleElement.Index}>({tuple});");
        }

        public override void Visit(IRTryCatch tryCatch)
        {
            // C++ exception handling with try-catch
            WriteLine("try");
            WriteLine("{");
            Indent();
            foreach (var inst in tryCatch.TryBlock.Instructions)
            {
                if (inst is IRBranch or IRConditionalBranch) continue;
                inst.Accept(this);
            }
            Unindent();
            WriteLine("}");

            foreach (var catchClause in tryCatch.CatchClauses)
            {
                var exType = catchClause.ExceptionType?.Name ?? "std::exception";
                var varName = !string.IsNullOrEmpty(catchClause.VariableName)
                    ? SanitizeName(catchClause.VariableName)
                    : "ex";
                WriteLine($"catch (const {exType}& {varName})");
                WriteLine("{");
                Indent();
                foreach (var inst in catchClause.Block.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
                Unindent();
                WriteLine("}");
            }
        }

        public override void Visit(IRInlineCode inlineCode)
        {
            if (inlineCode.Language.ToLower() == "cpp")
            {
                // Emit the C++ code directly
                WriteLine("// Inline C++ code");
                foreach (var line in inlineCode.Code.Split('\n'))
                {
                    WriteLine(line.TrimEnd());
                }
            }
            else
            {
                // For non-C++ inline code, emit a comment indicating it's not supported
                WriteLine($"// WARNING: Inline {inlineCode.Language} code not supported in C++ backend");
                WriteLine($"// Original code ({inlineCode.Code.Length} chars) was skipped");
            }
        }

        public override void Visit(IRForEach forEach)
        {
            // C++ range-based for loop
            var elemType = MapType(forEach.ElementType);
            var varName = SanitizeName(forEach.VariableName);
            var collection = GetValueName(forEach.Collection);
            WriteLine($"for ({elemType} {varName} : {collection})");
            WriteLine("{");
            Indent();

            // Emit body block instructions
            if (forEach.BodyBlock != null)
            {
                foreach (var instruction in forEach.BodyBlock.Instructions)
                {
                    instruction.Accept(this);
                }
            }

            Unindent();
            WriteLine("}");
        }

        public override void Visit(IRIndexerAccess indexer)
        {
            var collection = GetValueName(indexer.Collection);
            var indices = string.Join("][", indexer.Indices.Select(i => GetValueName(i)));
            var result = GetValueName(indexer);

            WriteLine($"{result} = {collection}[{indices}];");
        }

        #endregion

        private string MapBinaryOperator(BinaryOpKind op) => op switch
        {
            BinaryOpKind.Add => "+",
            BinaryOpKind.Sub => "-",
            BinaryOpKind.Mul => "*",
            BinaryOpKind.Div => "/",
            BinaryOpKind.Mod => "%",
            BinaryOpKind.And => "&",
            BinaryOpKind.Or => "|",
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

        private bool IsNamedDestination(IRValue value)
        {
            if (value == null) return false;
            if (string.IsNullOrEmpty(value.Name)) return false;
            return _declaredIdentifiers.Contains(value.Name);
        }

        private string GetDefaultValue(TypeInfo type)
        {
            if (type == null) return "{}";

            var typeName = type.Name?.ToLower() ?? "";

            return typeName switch
            {
                "integer" or "int" => "0",
                "long" => "0LL",
                "single" or "float" => "0.0f",
                "double" => "0.0",
                "boolean" or "bool" => "false",
                "char" => "'\\0'",
                "string" => "\"\"",
                _ when type.Kind == TypeKind.Array => "{}",
                _ when type.Kind == TypeKind.Pointer => "nullptr",
                _ => "{}"
            };
        }

        protected override string EmitConstant(IRConstant constant)
        {
            if (constant.Value == null)
                return "nullptr";

            if (constant.Value is string str)
                return $"\"{EscapeString(str)}\"";

            if (constant.Value is char ch)
                return $"'{EscapeChar(ch)}'";

            if (constant.Value is bool b)
                return b ? "true" : "false";

            if (constant.Value is float f)
                return $"{f}f";

            if (constant.Value is long l)
                return $"{l}LL";

            return constant.Value.ToString();
        }

        protected new string EscapeString(string str)
        {
            return str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
        }

        protected new string EscapeChar(char ch)
        {
            if (ch == '\'') return "\\'";
            if (ch == '\\') return "\\\\";
            if (ch == '\n') return "\\n";
            if (ch == '\r') return "\\r";
            if (ch == '\t') return "\\t";
            return ch.ToString();
        }

        private void Write(string text) => _output.Append(text);
        
        /// <summary>
        /// Generate extern declaration for C library interop
        /// </summary>
        private void GenerateExternDeclaration(IRExternDeclaration externDecl)
        {
            // Skip if this is a platform-specific extern
            if (string.IsNullOrEmpty(externDecl.LibraryName) && externDecl.PlatformImplementations.Count > 0)
            {
                // This is a platform-specific extern, check for C++ implementation
                if (externDecl.PlatformImplementations.TryGetValue("Cpp", out var impl))
                {
                    WriteLine(impl);
                }
                return;
            }

            // Build the function declaration
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
                    paramList.Add($"{paramType}& {paramName}");
                }
                else
                {
                    paramList.Add($"{paramType} {paramName}");
                }
            }

            var methodName = SanitizeName(externDecl.Name);
            WriteLine($"{returnType} {methodName}({string.Join(", ", paramList)});");
        }

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
    }

    public class CppCodeGenOptions
    {
        public int IndentSize { get; set; } = 4;
        public bool GenerateComments { get; set; } = true;
        public bool GenerateMainFunction { get; set; } = true;
        public string Namespace { get; set; } = "BasicLang";
    }
}
