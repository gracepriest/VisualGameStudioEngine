using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.LLVM
{
    /// <summary>
    /// LLVM IR code generator - emits textual LLVM IR (.ll files)
    /// Can be compiled with llc or clang to native code
    /// </summary>
    public class LLVMCodeGenerator : CodeGeneratorBase
    {
        private readonly StringBuilder _output;
        private readonly LLVMCodeGenOptions _options;
        private readonly Dictionary<string, int> _stringConstants;
        private readonly HashSet<string> _declaredIdentifiers;
        private readonly Dictionary<IRValue, string> _llvmNames;
        private readonly Dictionary<string, List<string>> _classFieldNames;  // class -> field names in order
        private readonly Dictionary<string, Dictionary<string, int>> _classFieldIndices;  // class -> field -> index
        private readonly Dictionary<string, string> _classStructTypes;  // class -> LLVM struct type
        private readonly Dictionary<string, List<IRMethod>> _classVirtualMethods;  // class -> virtual methods
        private readonly Dictionary<string, string> _classVtableTypes;  // class -> vtable struct type
        private IRModule _module;
        private new int _tempCounter;
        private int _labelCounter;
        private int _stringCounter;

        public override string BackendName => "LLVM";
        public override TargetPlatform Target => TargetPlatform.LLVM;

        public LLVMCodeGenerator(LLVMCodeGenOptions options = null)
        {
            _output = new StringBuilder();
            _options = options ?? new LLVMCodeGenOptions();
            _stringConstants = new Dictionary<string, int>();
            _declaredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _llvmNames = new Dictionary<IRValue, string>();
            _classFieldNames = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            _classFieldIndices = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            _classStructTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _classVirtualMethods = new Dictionary<string, List<IRMethod>>(StringComparer.OrdinalIgnoreCase);
            _classVtableTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _typeMapper = new LLVMTypeMapper();
        }

        protected override void InitializeTypeMap()
        {
            // LLVM IR type mappings
            _typeMap["Integer"] = "i32";
            _typeMap["Long"] = "i64";
            _typeMap["Single"] = "float";
            _typeMap["Double"] = "double";
            _typeMap["String"] = "i8*";
            _typeMap["Boolean"] = "i1";
            _typeMap["Char"] = "i8";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "i8*";
            _typeMap["Byte"] = "i8";
            _typeMap["Short"] = "i16";
            _typeMap["UInteger"] = "i32";
            _typeMap["ULong"] = "i64";
        }

        /// <summary>
        /// Override MapType to handle generic type parameters and LLVM-specific types
        /// </summary>
        protected override string MapType(TypeInfo type)
        {
            if (type == null) return "i8*";

            // Check if this is a generic type parameter (single uppercase letter)
            if (type.Name.Length == 1 && char.IsUpper(type.Name[0]))
            {
                return "i8*";  // Generic type parameters are treated as pointers
            }

            // Check for array types
            if (type.Kind == TypeKind.Array)
            {
                var elemType = type.ElementType != null ? MapType(type.ElementType) : "i8*";
                return $"{elemType}*";  // Arrays are pointers in LLVM
            }

            // Check type map first
            if (_typeMap.TryGetValue(type.Name, out var mapped))
                return mapped;

            // Class types are pointers
            if (type.Kind == TypeKind.Class || type.Kind == TypeKind.Interface)
            {
                var className = SanitizeLLVMName(type.Name);
                if (_classStructTypes.ContainsKey(type.Name))
                {
                    return $"{_classStructTypes[type.Name]}*";
                }
                return $"%class.{className}*";
            }

            // Default to pointer type for unknown types
            return "i8*";
        }

        public override string Generate(IRModule module)
        {
            _module = module;
            _output.Clear();
            _stringConstants.Clear();
            _classFieldNames.Clear();
            _classFieldIndices.Clear();
            _classStructTypes.Clear();
            _classVirtualMethods.Clear();
            _classVtableTypes.Clear();
            _tempCounter = 0;
            _labelCounter = 0;
            _stringCounter = 0;

            // Collect all string constants first
            CollectStringConstants(module);

            // Generate module header
            GenerateHeader(module);

            // Generate enum constants
            if (module.Enums.Count > 0)
            {
                WriteLine("; Enum constants");
                foreach (var irEnum in module.Enums.Values)
                {
                    GenerateEnum(irEnum);
                }
                WriteLine();
            }

            // Generate class struct types
            if (module.Classes.Count > 0)
            {
                WriteLine("; Class struct types");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateClassStructType(irClass);
                }
                WriteLine();
            }

            // Generate vtable types and data
            if (module.Classes.Count > 0)
            {
                WriteLine("; Vtable types");
                foreach (var irClass in module.Classes.Values)
                {
                    if (_classVtableTypes.ContainsKey(irClass.Name))
                    {
                        GenerateVtableType(irClass);
                    }
                }
                WriteLine();

                WriteLine("; Vtable data");
                foreach (var irClass in module.Classes.Values)
                {
                    if (_classVtableTypes.ContainsKey(irClass.Name))
                    {
                        GenerateVtableData(irClass);
                    }
                }
                WriteLine();
            }

            // Generate delegate types (function pointer types)
            if (module.Delegates.Count > 0)
            {
                WriteLine("; Delegate types (function pointers)");
                foreach (var irDelegate in module.Delegates.Values)
                {
                    GenerateDelegate(irDelegate);
                }
                WriteLine();
            }

            // Generate interface vtable types
            if (module.Interfaces.Count > 0)
            {
                WriteLine("; Interface vtable types");
                foreach (var irInterface in module.Interfaces.Values)
                {
                    GenerateInterfaceVtable(irInterface);
                }
                WriteLine();
            }

            // Generate string constant declarations
            GenerateStringConstants();

            // Generate external function declarations
            GenerateExternals();

            // Generate class methods
            if (module.Classes.Count > 0)
            {
                WriteLine("; Class methods");
                foreach (var irClass in module.Classes.Values)
                {
                    GenerateClassMethods(irClass);
                }
            }

            // Generate standalone function definitions
            foreach (var function in module.Functions)
            {
                if (!function.IsExternal && !IsClassMethod(function, module))
                {
                    GenerateFunction(function);
                    WriteLine();
                }
            }

            return _output.ToString();
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

        private void GenerateEnum(IREnum irEnum)
        {
            var enumName = SanitizeLLVMName(irEnum.Name);
            foreach (var member in irEnum.Members)
            {
                var memberName = $"{enumName}_{SanitizeLLVMName(member.Name)}";
                var value = member.Value ?? 0;
                WriteLine($"@{memberName} = constant i32 {value}");
            }
        }

        private void GenerateClassStructType(IRClass irClass)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var fieldTypes = new List<string>();
            var fieldNames = new List<string>();
            var fieldIndices = new Dictionary<string, int>();

            // Comment for abstract classes
            if (irClass.IsAbstract)
            {
                WriteLine($"; Abstract class - cannot be instantiated directly");
            }

            int fieldIndex = 0;

            // Collect virtual methods
            var virtualMethods = new List<IRMethod>();
            foreach (var method in irClass.Methods)
            {
                if (method.IsVirtual || method.IsOverride)
                {
                    virtualMethods.Add(method);
                }
            }
            _classVirtualMethods[irClass.Name] = virtualMethods;

            // First field: vtable pointer (if class has virtual methods or inherits from class with virtuals)
            bool hasVtable = virtualMethods.Count > 0 ||
                            (!string.IsNullOrEmpty(irClass.BaseClass) && _classVirtualMethods.ContainsKey(irClass.BaseClass) && _classVirtualMethods[irClass.BaseClass].Count > 0);

            if (hasVtable)
            {
                var vtableTypeName = $"%vtable.{className}";
                _classVtableTypes[irClass.Name] = vtableTypeName;
                fieldTypes.Add($"{vtableTypeName}*");
                fieldNames.Add("__vtable_ptr");
                fieldIndices["__vtable_ptr"] = fieldIndex++;
            }

            // Inherit fields from base class if any
            if (!string.IsNullOrEmpty(irClass.BaseClass) && _classFieldNames.ContainsKey(irClass.BaseClass))
            {
                foreach (var baseField in _classFieldNames[irClass.BaseClass])
                {
                    if (baseField == "__vtable_ptr") continue; // Skip vtable ptr - already added
                    var baseFieldType = GetFieldType(irClass.BaseClass, baseField);
                    fieldTypes.Add(baseFieldType);
                    fieldNames.Add(baseField);
                    fieldIndices[baseField] = fieldIndex++;
                }
            }

            // Add own fields
            foreach (var field in irClass.Fields)
            {
                var fieldType = MapType(field.Type);
                fieldTypes.Add(fieldType);
                fieldNames.Add(field.Name);
                fieldIndices[field.Name] = fieldIndex++;
            }

            _classFieldNames[irClass.Name] = fieldNames;
            _classFieldIndices[irClass.Name] = fieldIndices;

            // Generate struct type
            var structBody = fieldTypes.Count > 0 ? string.Join(", ", fieldTypes) : "i8";
            var structType = $"{{ {structBody} }}";
            _classStructTypes[irClass.Name] = $"%class.{className}";

            // Add generic type parameters as comment/metadata
            var genericComment = "";
            if (irClass.GenericParameters != null && irClass.GenericParameters.Count > 0)
            {
                genericComment = $" ; generic<{string.Join(", ", irClass.GenericParameters)}>";
            }
            else if (irClass.GenericTypeParams != null && irClass.GenericTypeParams.Count > 0)
            {
                var genericParamStrings = irClass.GenericTypeParams.Select(p =>
                {
                    var constraints = p.Constraints?.Select(c => c.Kind.ToString()).ToList() ?? new List<string>();
                    return constraints.Count > 0 ? $"{p.Name}: {string.Join("+", constraints)}" : p.Name;
                });
                genericComment = $" ; generic<{string.Join(", ", genericParamStrings)}>";
            }

            WriteLine($"%class.{className} = type {structType}{genericComment}");
        }

        private void GenerateVtableType(IRClass irClass)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var vtableTypeName = _classVtableTypes[irClass.Name];
            var structType = _classStructTypes[irClass.Name];

            var virtualMethods = _classVirtualMethods[irClass.Name];
            var functionPtrTypes = new List<string>();

            foreach (var method in virtualMethods)
            {
                var returnType = MapType(method.ReturnType);
                var paramTypes = new List<string> { $"{structType}*" }; // this pointer

                if (method.Implementation != null)
                {
                    foreach (var param in method.Implementation.Parameters)
                    {
                        paramTypes.Add(MapType(param.Type));
                    }
                }

                var funcPtrType = $"{returnType} ({string.Join(", ", paramTypes)})*";
                functionPtrTypes.Add(funcPtrType);
            }

            var vtableBody = functionPtrTypes.Count > 0
                ? string.Join(", ", functionPtrTypes)
                : "i8*";  // Empty vtable has dummy pointer

            WriteLine($"{vtableTypeName} = type {{ {vtableBody} }}");
        }

        private void GenerateVtableData(IRClass irClass)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var vtableTypeName = _classVtableTypes[irClass.Name];
            var structType = _classStructTypes[irClass.Name];

            var virtualMethods = _classVirtualMethods[irClass.Name];
            var functionPtrs = new List<string>();

            foreach (var method in virtualMethods)
            {
                var returnType = MapType(method.ReturnType);
                var methodName = SanitizeLLVMName(method.Name);
                var paramTypes = new List<string> { $"{structType}*" }; // this pointer

                if (method.Implementation != null)
                {
                    foreach (var param in method.Implementation.Parameters)
                    {
                        paramTypes.Add(MapType(param.Type));
                    }
                }

                var funcPtrType = $"{returnType} ({string.Join(", ", paramTypes)})*";
                var funcPtr = $"{funcPtrType} @{className}_{methodName}";
                functionPtrs.Add(funcPtr);
            }

            var vtableInit = functionPtrs.Count > 0
                ? string.Join(", ", functionPtrs)
                : "i8* null";  // Empty vtable

            WriteLine($"@{className}_vtable_data = constant {vtableTypeName} {{ {vtableInit} }}");
        }

        private string GetFieldType(string className, string fieldName)
        {
            if (fieldName == "__vtable_ptr" && _classVtableTypes.ContainsKey(className))
            {
                return $"{_classVtableTypes[className]}*";
            }

            if (_module.Classes.TryGetValue(className, out var irClass))
            {
                var field = irClass.Fields.FirstOrDefault(f => f.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
                if (field != null)
                    return MapType(field.Type);
            }
            return "i32";  // Default
        }

        private void GenerateDelegate(IRDelegate irDelegate)
        {
            var delegateName = SanitizeLLVMName(irDelegate.Name);
            var returnType = MapType(irDelegate.ReturnType);
            var paramTypes = string.Join(", ", irDelegate.Parameters.Select(p => MapTypeName(p.TypeName)));

            // Delegate as function pointer type
            WriteLine($"%delegate.{delegateName} = type {returnType} ({paramTypes})*");
        }

        private string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "i8*";
            return typeName.ToLowerInvariant() switch
            {
                "integer" => "i32",
                "long" => "i64",
                "single" or "float" => "float",
                "double" => "double",
                "string" => "i8*",
                "boolean" => "i1",
                "byte" => "i8",
                "short" => "i16",
                "object" => "i8*",
                "void" => "void",
                _ => "i8*"  // Default to pointer for unknown types
            };
        }

        /// <summary>
        /// Get LLVM linkage based on access modifier
        /// </summary>
        private string GetLLVMLinkage(AccessModifier access)
        {
            return access switch
            {
                AccessModifier.Private => "private ",
                AccessModifier.Protected => "internal ",  // LLVM doesn't have protected, use internal
                AccessModifier.Friend => "internal ",     // Module-level visibility
                _ => ""  // Public - default external linkage
            };
        }

        /// <summary>
        /// Get operator symbol from op_ prefixed function name
        /// </summary>
        private string GetOperatorSymbol(string funcName)
        {
            // Extract the operator name after op_
            var opIndex = funcName.IndexOf("op_");
            if (opIndex < 0) return null;

            var opName = funcName.Substring(opIndex + 3);
            // Remove any trailing parts (like parameter suffix)
            var dotIndex = opName.IndexOf('.');
            if (dotIndex >= 0) opName = opName.Substring(0, dotIndex);

            return opName switch
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
                "Implicit" => "implicit",
                "Explicit" => "explicit",
                _ => opName
            };
        }

        private void GenerateInterfaceVtable(IRInterface irInterface)
        {
            var interfaceName = SanitizeLLVMName(irInterface.Name);
            var methodPtrs = new List<string>();

            foreach (var method in irInterface.Methods)
            {
                var returnType = MapType(method.ReturnType);
                var paramTypes = new List<string> { "i8*" };  // this pointer
                paramTypes.AddRange(method.Parameters.Select(p => MapTypeName(p.TypeName)));
                methodPtrs.Add($"{returnType} ({string.Join(", ", paramTypes)})*");
            }

            var vtableBody = methodPtrs.Count > 0 ? string.Join(", ", methodPtrs) : "i8*";
            WriteLine($"%vtable.{interfaceName} = type {{ {vtableBody} }}");
        }

        private void GenerateClassMethods(IRClass irClass)
        {
            var className = SanitizeLLVMName(irClass.Name);

            // Generate constructors
            foreach (var ctor in irClass.Constructors)
            {
                if (ctor.Implementation != null)
                {
                    GenerateConstructor(irClass, ctor);
                    WriteLine();
                }
            }

            // Generate methods
            foreach (var method in irClass.Methods)
            {
                if (method.Implementation != null)
                {
                    GenerateMethod(irClass, method);
                    WriteLine();
                }
            }

            // Generate property getters/setters
            foreach (var prop in irClass.Properties)
            {
                if (prop.Getter != null)
                {
                    GeneratePropertyGetter(irClass, prop);
                    WriteLine();
                }
                if (prop.Setter != null)
                {
                    GeneratePropertySetter(irClass, prop);
                    WriteLine();
                }
            }

            // Generate event add/remove handlers
            foreach (var evt in irClass.Events)
            {
                GenerateEventHandlers(irClass, evt);
                WriteLine();
            }
        }

        private void GenerateEventHandlers(IRClass irClass, IREvent evt)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var eventName = SanitizeLLVMName(evt.Name);
            var structType = _classStructTypes.GetValueOrDefault(irClass.Name, $"%class.{className}");
            var linkage = GetLLVMLinkage(evt.Access);

            // Events in LLVM are implemented as delegate fields with add/remove methods
            // Generate add_EventName method
            var delegateType = "i8*";  // Function pointer type
            WriteLine($"; Event: {evt.Name} (delegate type: {evt.DelegateType ?? "EventHandler"})");

            if (evt.IsStatic)
            {
                // Static event storage (global variable for handler list)
                WriteLine($"@{className}_{eventName}_handlers = internal global {delegateType} null");
                WriteLine();

                // Static event - add handler (combine delegates)
                WriteLine($"define {linkage}void @{className}_add_{eventName}({delegateType} %handler) {{");
                WriteLine("entry:");
                WriteLine($"  %current = load {delegateType}, {delegateType}* @{className}_{eventName}_handlers");
                WriteLine($"  %is_null = icmp eq {delegateType} %current, null");
                WriteLine($"  br i1 %is_null, label %set_new, label %combine");
                WriteLine("set_new:");
                WriteLine($"  store {delegateType} %handler, {delegateType}* @{className}_{eventName}_handlers");
                WriteLine($"  ret void");
                WriteLine("combine:");
                WriteLine($"  ; Combine delegates using runtime helper");
                WriteLine($"  %combined = call {delegateType} @__delegate_combine({delegateType} %current, {delegateType} %handler)");
                WriteLine($"  store {delegateType} %combined, {delegateType}* @{className}_{eventName}_handlers");
                WriteLine("  ret void");
                WriteLine("}");
                WriteLine();

                // Static event - remove handler
                WriteLine($"define {linkage}void @{className}_remove_{eventName}({delegateType} %handler) {{");
                WriteLine("entry:");
                WriteLine($"  %current = load {delegateType}, {delegateType}* @{className}_{eventName}_handlers");
                WriteLine($"  %is_null = icmp eq {delegateType} %current, null");
                WriteLine($"  br i1 %is_null, label %done, label %remove");
                WriteLine("remove:");
                WriteLine($"  %result = call {delegateType} @__delegate_remove({delegateType} %current, {delegateType} %handler)");
                WriteLine($"  store {delegateType} %result, {delegateType}* @{className}_{eventName}_handlers");
                WriteLine($"  br label %done");
                WriteLine("done:");
                WriteLine("  ret void");
                WriteLine("}");
            }
            else
            {
                // Instance event - add handler
                WriteLine($"define {linkage}void @{className}_add_{eventName}({structType}* %this, {delegateType} %handler) {{");
                WriteLine("entry:");
                WriteLine($"  ; Get event field pointer from instance");
                WriteLine($"  %field_ptr = getelementptr {structType}, {structType}* %this, i32 0, i32 0  ; Adjust field index");
                WriteLine($"  %current = load {delegateType}, {delegateType}* %field_ptr");
                WriteLine($"  %is_null = icmp eq {delegateType} %current, null");
                WriteLine($"  br i1 %is_null, label %set_new, label %combine");
                WriteLine("set_new:");
                WriteLine($"  store {delegateType} %handler, {delegateType}* %field_ptr");
                WriteLine($"  ret void");
                WriteLine("combine:");
                WriteLine($"  %combined = call {delegateType} @__delegate_combine({delegateType} %current, {delegateType} %handler)");
                WriteLine($"  store {delegateType} %combined, {delegateType}* %field_ptr");
                WriteLine("  ret void");
                WriteLine("}");
                WriteLine();

                // Instance event - remove handler
                WriteLine($"define {linkage}void @{className}_remove_{eventName}({structType}* %this, {delegateType} %handler) {{");
                WriteLine("entry:");
                WriteLine($"  %field_ptr = getelementptr {structType}, {structType}* %this, i32 0, i32 0");
                WriteLine($"  %current = load {delegateType}, {delegateType}* %field_ptr");
                WriteLine($"  %is_null = icmp eq {delegateType} %current, null");
                WriteLine($"  br i1 %is_null, label %done, label %remove");
                WriteLine("remove:");
                WriteLine($"  %result = call {delegateType} @__delegate_remove({delegateType} %current, {delegateType} %handler)");
                WriteLine($"  store {delegateType} %result, {delegateType}* %field_ptr");
                WriteLine($"  br label %done");
                WriteLine("done:");
                WriteLine("  ret void");
                WriteLine("}");
            }
        }

        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var structType = _classStructTypes.GetValueOrDefault(irClass.Name, $"%class.{className}");

            _currentFunction = ctor.Implementation;
            _llvmNames.Clear();
            _declaredIdentifiers.Clear();
            _tempCounter = 0;

            // Constructor returns pointer to the class
            var paramList = new List<string>();
            if (ctor.Implementation != null)
            {
                foreach (var param in ctor.Implementation.Parameters)
                {
                    _declaredIdentifiers.Add(param.Name);
                    paramList.Add($"{MapType(param.Type)} %{SanitizeLLVMName(param.Name)}");
                }
            }

            WriteLine($"define {structType}* @{className}_ctor({string.Join(", ", paramList)}) {{");
            WriteLine("entry:");

            // Allocate the object on heap using malloc
            // Calculate size of struct
            var sizeofTemp = $"%sizeof_{_tempCounter++}";
            WriteLine($"  {sizeofTemp} = getelementptr {structType}, {structType}* null, i32 1");
            var sizeTemp = $"%size_{_tempCounter++}";
            WriteLine($"  {sizeTemp} = ptrtoint {structType}* {sizeofTemp} to i64");

            // Call malloc
            var mallocTemp = $"%malloc_{_tempCounter++}";
            WriteLine($"  {mallocTemp} = call i8* @malloc(i64 {sizeTemp})");

            // Cast to class pointer
            WriteLine($"  %this = bitcast i8* {mallocTemp} to {structType}*");

            // Initialize vtable pointer if class has vtable
            if (_classVtableTypes.ContainsKey(irClass.Name))
            {
                var vtableTypeName = _classVtableTypes[irClass.Name];
                var vtableIdx = 0; // vtable is always first field
                var vtablePtrTemp = $"%vtable_ptr_{_tempCounter++}";
                WriteLine($"  {vtablePtrTemp} = getelementptr inbounds {structType}, {structType}* %this, i32 0, i32 {vtableIdx}");
                WriteLine($"  store {vtableTypeName}* @{className}_vtable_data, {vtableTypeName}** {vtablePtrTemp}");
            }

            // Allocate parameter addresses
            if (ctor.Implementation != null)
            {
                foreach (var param in ctor.Implementation.Parameters)
                {
                    var paramType = MapType(param.Type);
                    var paramName = SanitizeLLVMName(param.Name);
                    WriteLine($"  %{paramName}.addr = alloca {paramType}");
                    WriteLine($"  store {paramType} %{paramName}, {paramType}* %{paramName}.addr");
                }

                // Process constructor body
                foreach (var local in ctor.Implementation.LocalVariables)
                {
                    _declaredIdentifiers.Add(local.Name);
                    var localType = MapType(local.Type);
                    var localName = SanitizeLLVMName(local.Name);
                    WriteLine($"  %{localName}.addr = alloca {localType}");
                    var defaultVal = GetDefaultValue(local.Type, localType);
                    WriteLine($"  store {localType} {defaultVal}, {localType}* %{localName}.addr");
                }

                if (ctor.Implementation.EntryBlock != null)
                {
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(ctor.Implementation.EntryBlock, visited, isEntry: true);
                }
            }

            WriteLine($"  ret {structType}* %this");
            WriteLine("}");
        }

        private void GenerateMethod(IRClass irClass, IRMethod method)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var methodName = SanitizeLLVMName(method.Name);
            var structType = _classStructTypes.GetValueOrDefault(irClass.Name, $"%class.{className}");
            var returnType = MapType(method.ReturnType);

            // Handle abstract methods - just declare, no body
            if (method.IsAbstract)
            {
                var abstractParams = new List<string>();
                if (!method.IsStatic)
                {
                    abstractParams.Add($"{structType}*");
                }
                foreach (var param in method.Parameters ?? new List<IRVariable>())
                {
                    abstractParams.Add(MapType(param.Type));
                }
                WriteLine($"; abstract method - declaration only");
                WriteLine($"declare {returnType} @{className}_{methodName}({string.Join(", ", abstractParams)})");
                WriteLine();
                return;
            }

            _currentFunction = method.Implementation;
            _llvmNames.Clear();
            _declaredIdentifiers.Clear();
            _tempCounter = 0;

            // Build parameter list with 'this' pointer for instance methods
            var paramList = new List<string>();
            if (!method.IsStatic)
            {
                paramList.Add($"{structType}* %this");
            }

            if (method.Implementation != null)
            {
                foreach (var param in method.Implementation.Parameters)
                {
                    _declaredIdentifiers.Add(param.Name);
                    paramList.Add($"{MapType(param.Type)} %{SanitizeLLVMName(param.Name)}");
                }
            }

            // Determine linkage based on access modifier
            var linkage = GetLLVMLinkage(method.Access);
            var modifiers = new List<string>();
            if (method.IsVirtual) modifiers.Add("virtual");
            if (method.IsOverride) modifiers.Add("override");
            if (method.IsSealed) modifiers.Add("sealed");

            // Add generic parameters info
            if (method.GenericParameters != null && method.GenericParameters.Count > 0)
            {
                modifiers.Add($"generic<{string.Join(", ", method.GenericParameters)}>");
            }

            var modComment = modifiers.Count > 0 ? $" ; {string.Join(", ", modifiers)}" : "";

            WriteLine($"define {linkage}{returnType} @{className}_{methodName}({string.Join(", ", paramList)}){modComment} {{");
            WriteLine("entry:");

            if (method.Implementation != null)
            {
                // Allocate parameter addresses
                foreach (var param in method.Implementation.Parameters)
                {
                    var paramType = MapType(param.Type);
                    var paramName = SanitizeLLVMName(param.Name);
                    WriteLine($"  %{paramName}.addr = alloca {paramType}");
                    WriteLine($"  store {paramType} %{paramName}, {paramType}* %{paramName}.addr");
                }

                // Allocate locals
                foreach (var local in method.Implementation.LocalVariables)
                {
                    _declaredIdentifiers.Add(local.Name);
                    var localType = MapType(local.Type);
                    var localName = SanitizeLLVMName(local.Name);
                    WriteLine($"  %{localName}.addr = alloca {localType}");
                    var defaultVal = GetDefaultValue(local.Type, localType);
                    WriteLine($"  store {localType} {defaultVal}, {localType}* %{localName}.addr");
                }

                if (method.Implementation.EntryBlock != null)
                {
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(method.Implementation.EntryBlock, visited, isEntry: true);
                }
            }

            // Ensure return for void functions
            if (returnType == "void" && !EndsWithTerminator())
            {
                WriteLine("  ret void");
            }

            WriteLine("}");
        }

        private void GeneratePropertyGetter(IRClass irClass, IRProperty prop)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var propName = SanitizeLLVMName(prop.Name);
            var structType = _classStructTypes.GetValueOrDefault(irClass.Name, $"%class.{className}");
            var returnType = MapType(prop.Type);

            _currentFunction = prop.Getter;
            _llvmNames.Clear();
            _declaredIdentifiers.Clear();
            _tempCounter = 0;

            var paramList = prop.IsStatic ? "" : $"{structType}* %this";
            var linkage = GetLLVMLinkage(prop.Access);
            WriteLine($"define {linkage}{returnType} @{className}_get_{propName}({paramList}) {{");
            WriteLine("entry:");

            if (prop.Getter?.EntryBlock != null)
            {
                foreach (var local in prop.Getter.LocalVariables)
                {
                    _declaredIdentifiers.Add(local.Name);
                    var localType = MapType(local.Type);
                    var localName = SanitizeLLVMName(local.Name);
                    WriteLine($"  %{localName}.addr = alloca {localType}");
                }

                var visited = new HashSet<BasicBlock>();
                GenerateBasicBlock(prop.Getter.EntryBlock, visited, isEntry: true);
            }

            WriteLine("}");
        }

        private void GeneratePropertySetter(IRClass irClass, IRProperty prop)
        {
            var className = SanitizeLLVMName(irClass.Name);
            var propName = SanitizeLLVMName(prop.Name);
            var structType = _classStructTypes.GetValueOrDefault(irClass.Name, $"%class.{className}");
            var valueType = MapType(prop.Type);

            _currentFunction = prop.Setter;
            _llvmNames.Clear();
            _declaredIdentifiers.Clear();
            _tempCounter = 0;

            var paramList = prop.IsStatic
                ? $"{valueType} %value"
                : $"{structType}* %this, {valueType} %value";

            var linkage = GetLLVMLinkage(prop.Access);
            WriteLine($"define {linkage}void @{className}_set_{propName}({paramList}) {{");
            WriteLine("entry:");

            if (prop.Setter?.EntryBlock != null)
            {
                foreach (var local in prop.Setter.LocalVariables)
                {
                    _declaredIdentifiers.Add(local.Name);
                    var localType = MapType(local.Type);
                    var localName = SanitizeLLVMName(local.Name);
                    WriteLine($"  %{localName}.addr = alloca {localType}");
                }

                var visited = new HashSet<BasicBlock>();
                GenerateBasicBlock(prop.Setter.EntryBlock, visited, isEntry: true);
            }

            if (!EndsWithTerminator())
            {
                WriteLine("  ret void");
            }

            WriteLine("}");
        }

        private void CollectStringConstants(IRModule module)
        {
            foreach (var function in module.Functions)
            {
                foreach (var block in function.Blocks)
                {
                    foreach (var instruction in block.Instructions)
                    {
                        CollectStringsFromInstruction(instruction);
                    }
                }
            }
        }

        private void CollectStringsFromInstruction(IRInstruction instruction)
        {
            if (instruction is IRConstant constant && constant.Value is string str)
            {
                if (!_stringConstants.ContainsKey(str))
                {
                    _stringConstants[str] = _stringCounter++;
                }
            }

            if (instruction is IRCall call)
            {
                foreach (var arg in call.Arguments)
                {
                    if (arg is IRConstant c && c.Value is string s)
                    {
                        if (!_stringConstants.ContainsKey(s))
                        {
                            _stringConstants[s] = _stringCounter++;
                        }
                    }
                }
            }

            if (instruction is IRBinaryOp binOp)
            {
                CollectStringsFromInstruction(binOp.Left as IRInstruction);
                CollectStringsFromInstruction(binOp.Right as IRInstruction);
            }
        }

        private void GenerateHeader(IRModule module)
        {
            WriteLine($"; ModuleID = '{module.Name}'");
            WriteLine($"source_filename = \"{module.Name}.bas\"");
            WriteLine("target datalayout = \"e-m:w-i64:64-f80:128-n8:16:32:64-S128\"");
            WriteLine("target triple = \"x86_64-pc-windows-msvc\"");
            WriteLine();
        }

        private void GenerateStringConstants()
        {
            // Generate format strings for printf
            WriteLine("; Format strings for printf");
            WriteLine("@.fmt.int = private unnamed_addr constant [4 x i8] c\"%d\\0A\\00\"");
            WriteLine("@.fmt.long = private unnamed_addr constant [5 x i8] c\"%ld\\0A\\00\"");
            WriteLine("@.fmt.double = private unnamed_addr constant [4 x i8] c\"%f\\0A\\00\"");
            WriteLine("@.fmt.str = private unnamed_addr constant [4 x i8] c\"%s\\0A\\00\"");
            WriteLine("@.fmt.0 = private unnamed_addr constant [4 x i8] c\"%d\\0A\\00\"");  // Default int format
            WriteLine();

            if (_stringConstants.Count == 0) return;

            WriteLine("; String constants");
            foreach (var (str, id) in _stringConstants)
            {
                var escaped = EscapeLLVMString(str);
                var len = str.Length + 1; // +1 for null terminator
                WriteLine($"@.str.{id} = private unnamed_addr constant [{len} x i8] c\"{escaped}\\00\"");
            }
            WriteLine();
        }

        private void GenerateExternals()
        {
            WriteLine("; External function declarations");
            WriteLine("declare i32 @printf(i8*, ...)");
            WriteLine("declare i32 @puts(i8*)");
            WriteLine("declare i32 @scanf(i8*, ...)");
            WriteLine("declare double @sqrt(double)");
            WriteLine("declare double @pow(double, double)");
            WriteLine("declare double @sin(double)");
            WriteLine("declare double @cos(double)");
            WriteLine("declare double @tan(double)");
            WriteLine("declare double @log(double)");
            WriteLine("declare double @exp(double)");
            WriteLine("declare double @floor(double)");
            WriteLine("declare double @ceil(double)");
            WriteLine("declare double @fabs(double)");
            WriteLine("declare i32 @rand()");
            WriteLine("declare void @srand(i32)");
            WriteLine("declare i64 @time(i64*)");
            WriteLine();

            // String functions
            WriteLine("; String functions");
            WriteLine("declare i64 @strlen(i8*)");
            WriteLine("declare i8* @strcpy(i8*, i8*)");
            WriteLine("declare i8* @strcat(i8*, i8*)");
            WriteLine("declare i8* @malloc(i64)");
            WriteLine("declare void @free(i8*)");
            WriteLine();

            // Generate helper for string concatenation
            GenerateStringConcatHelper();
        }

        private void GenerateStringConcatHelper()
        {
            WriteLine("; String concatenation helper");
            WriteLine("define i8* @__concat_strings(i8* %s1, i8* %s2) {");
            WriteLine("entry:");
            WriteLine("  %len1 = call i64 @strlen(i8* %s1)");
            WriteLine("  %len2 = call i64 @strlen(i8* %s2)");
            WriteLine("  %total = add i64 %len1, %len2");
            WriteLine("  %total1 = add i64 %total, 1");
            WriteLine("  %buf = call i8* @malloc(i64 %total1)");
            WriteLine("  call i8* @strcpy(i8* %buf, i8* %s1)");
            WriteLine("  call i8* @strcat(i8* %buf, i8* %s2)");
            WriteLine("  ret i8* %buf");
            WriteLine("}");
            WriteLine();
        }

        private void GenerateFunction(IRFunction function)
        {
            _currentFunction = function;
            _llvmNames.Clear();
            _declaredIdentifiers.Clear();
            _tempCounter = 0;
            _labelCounter = 0;

            // Collect declared identifiers
            foreach (var param in function.Parameters)
                _declaredIdentifiers.Add(param.Name);
            foreach (var local in function.LocalVariables)
                _declaredIdentifiers.Add(local.Name);

            // Generate function signature
            var returnType = MapType(function.ReturnType);
            var funcName = SanitizeLLVMName(function.Name);
            var paramList = string.Join(", ", function.Parameters.Select(p =>
                $"{MapType(p.Type)} %{SanitizeLLVMName(p.Name)}"));

            // Check if this is an operator overload
            var opComment = "";
            if (function.Name.Contains("op_") || function.Name.Contains(".op_"))
            {
                var opName = GetOperatorSymbol(function.Name);
                if (!string.IsNullOrEmpty(opName))
                {
                    opComment = $" ; operator {opName}";
                }
            }

            WriteLine($"define {returnType} @{funcName}({paramList}){opComment} {{");

            // Entry block
            WriteLine("entry:");

            // Allocate stack space for locals
            foreach (var local in function.LocalVariables)
            {
                var localType = MapType(local.Type);
                var localName = SanitizeLLVMName(local.Name);
                WriteLine($"  %{localName}.addr = alloca {localType}");

                // Initialize to zero/default
                var defaultVal = GetDefaultValue(local.Type, localType);
                WriteLine($"  store {localType} {defaultVal}, {localType}* %{localName}.addr");
            }

            // Allocate stack space for parameters (so they can be modified)
            foreach (var param in function.Parameters)
            {
                var paramType = MapType(param.Type);
                var paramName = SanitizeLLVMName(param.Name);
                WriteLine($"  %{paramName}.addr = alloca {paramType}");
                WriteLine($"  store {paramType} %{paramName}, {paramType}* %{paramName}.addr");
            }

            if (function.LocalVariables.Count > 0 || function.Parameters.Count > 0)
                WriteLine();

            // Generate basic blocks
            var visitedBlocks = new HashSet<BasicBlock>();
            if (function.EntryBlock != null)
            {
                GenerateBasicBlock(function.EntryBlock, visitedBlocks, isEntry: true);
            }

            // Ensure function has a return if void
            if (returnType == "void" && !EndsWithTerminator())
            {
                WriteLine("  ret void");
            }

            WriteLine("}");
        }

        private bool EndsWithTerminator()
        {
            var lines = _output.ToString().Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith(";")) continue;
                return line.StartsWith("ret ") || line.StartsWith("br ") || line.StartsWith("unreachable");
            }
            return false;
        }

        private void GenerateBasicBlock(BasicBlock block, HashSet<BasicBlock> visited, bool isEntry = false)
        {
            if (visited.Contains(block)) return;
            visited.Add(block);

            // Emit label (skip for entry block as we already emitted "entry:")
            if (!isEntry)
            {
                WriteLine($"{SanitizeLLVMLabel(block.Name)}:");
            }

            // Process instructions
            foreach (var instruction in block.Instructions)
            {
                instruction.Accept(this);
            }

            // Process successor blocks
            foreach (var successor in block.Successors.Where(s => !visited.Contains(s)))
            {
                GenerateBasicBlock(successor, visited);
            }
        }

        private string GetLLVMName(IRValue value)
        {
            if (value is IRConstant constant)
                return EmitConstantValue(constant);

            if (_llvmNames.TryGetValue(value, out var name))
                return name;

            if (value is IRVariable variable)
            {
                name = $"%{SanitizeLLVMName(variable.Name)}";
            }
            else
            {
                name = $"%t{_tempCounter++}";
            }

            _llvmNames[value] = name;
            return name;
        }

        private string EmitConstantValue(IRConstant constant)
        {
            if (constant.Value == null) return "null";
            if (constant.Value is bool b) return b ? "true" : "false";
            if (constant.Value is string s)
            {
                if (_stringConstants.TryGetValue(s, out var id))
                {
                    var len = s.Length + 1;
                    return $"getelementptr inbounds ([{len} x i8], [{len} x i8]* @.str.{id}, i64 0, i64 0)";
                }
                return "null";
            }
            if (constant.Value is char c) return ((int)c).ToString();
            if (constant.Value is float f) return FormatFloat(f);
            if (constant.Value is double d) return FormatDouble(d);
            return constant.Value.ToString();
        }

        private string FormatFloat(float f)
        {
            if (float.IsPositiveInfinity(f)) return "0x7FF0000000000000";
            if (float.IsNegativeInfinity(f)) return "0xFFF0000000000000";
            if (float.IsNaN(f)) return "0x7FF8000000000000";
            return f.ToString("G17");
        }

        private string FormatDouble(double d)
        {
            if (double.IsPositiveInfinity(d)) return "0x7FF0000000000000";
            if (double.IsNegativeInfinity(d)) return "0xFFF0000000000000";
            if (double.IsNaN(d)) return "0x7FF8000000000000";
            return d.ToString("G17");
        }

        private string GetDefaultValue(TypeInfo type, string llvmType)
        {
            if (type == null) return "zeroinitializer";

            var typeName = type.Name?.ToLower() ?? "";
            return typeName switch
            {
                "integer" or "long" or "short" or "byte" or "char" => "0",
                "single" or "float" => "0.0",
                "double" => "0.0",
                "boolean" => "false",
                "string" => "null",
                _ when llvmType.EndsWith("*") => "null",
                _ => "zeroinitializer"
            };
        }

        private string SanitizeLLVMName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";

            var result = new StringBuilder();
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                    result.Append(ch);
                else
                    result.Append('_');
            }

            var sanitized = result.ToString();
            if (char.IsDigit(sanitized[0]))
                sanitized = "_" + sanitized;

            return sanitized;
        }

        private string SanitizeLLVMLabel(string name)
        {
            return SanitizeLLVMName(name).Replace(".", "_");
        }

        private string EscapeLLVMString(string str)
        {
            var sb = new StringBuilder();
            foreach (var ch in str)
            {
                if (ch == '\\') sb.Append("\\5C");
                else if (ch == '"') sb.Append("\\22");
                else if (ch == '\n') sb.Append("\\0A");
                else if (ch == '\r') sb.Append("\\0D");
                else if (ch == '\t') sb.Append("\\09");
                else if (ch < 32 || ch > 126) sb.Append($"\\{((int)ch):X2}");
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private bool IsFloatType(TypeInfo type)
        {
            if (type == null) return false;
            var name = type.Name?.ToLower() ?? "";
            return name is "single" or "float" or "double";
        }

        private bool IsNamedDestination(IRValue value)
        {
            if (value == null) return false;
            if (string.IsNullOrEmpty(value.Name)) return false;
            return _declaredIdentifiers.Contains(value.Name);
        }

        #region Visitor Methods

        public override void Visit(IRFunction function) { }
        public override void Visit(BasicBlock block) { }
        public override void Visit(IRConstant constant) { }
        public override void Visit(IRVariable variable) { }

        public override void Visit(IRBinaryOp binaryOp)
        {
            var leftVal = GetLLVMName(binaryOp.Left);
            var rightVal = GetLLVMName(binaryOp.Right);
            var result = GetLLVMName(binaryOp);
            var type = MapType(binaryOp.Type);

            // Load values if they're addresses
            leftVal = LoadIfNeeded(binaryOp.Left, leftVal);
            rightVal = LoadIfNeeded(binaryOp.Right, rightVal);

            // Handle string concatenation specially
            if (binaryOp.Operation == BinaryOpKind.Concat)
            {
                WriteLine($"  {result} = call i8* @__concat_strings(i8* {leftVal}, i8* {rightVal})");

                // If this is assigned to a declared variable, store it
                if (IsNamedDestination(binaryOp))
                {
                    var varName = SanitizeLLVMName(binaryOp.Name);
                    WriteLine($"  store i8* {result}, i8** %{varName}.addr");
                }
                return;
            }

            string op;
            if (IsFloatType(binaryOp.Type))
            {
                op = ((LLVMTypeMapper)_typeMapper).MapFloatBinaryOperator(binaryOp.Operation);
            }
            else
            {
                op = _typeMapper.MapBinaryOperator(binaryOp.Operation);
            }

            WriteLine($"  {result} = {op} {type} {leftVal}, {rightVal}");

            // If this is assigned to a declared variable, store it
            if (IsNamedDestination(binaryOp))
            {
                var varName = SanitizeLLVMName(binaryOp.Name);
                WriteLine($"  store {type} {result}, {type}* %{varName}.addr");
            }
        }

        public override void Visit(IRUnaryOp unaryOp)
        {
            var operandVal = GetLLVMName(unaryOp.Operand);
            var result = GetLLVMName(unaryOp);
            var type = MapType(unaryOp.Type);

            operandVal = LoadIfNeeded(unaryOp.Operand, operandVal);

            switch (unaryOp.Operation)
            {
                case UnaryOpKind.Neg:
                    if (IsFloatType(unaryOp.Type))
                        WriteLine($"  {result} = fneg {type} {operandVal}");
                    else
                        WriteLine($"  {result} = sub {type} 0, {operandVal}");
                    break;
                case UnaryOpKind.Not:
                    WriteLine($"  {result} = xor {type} {operandVal}, true");
                    break;
                case UnaryOpKind.BitwiseNot:
                    WriteLine($"  {result} = xor {type} {operandVal}, -1");
                    break;
                default:
                    WriteLine($"  ; Unsupported unary op: {unaryOp.Operation}");
                    break;
            }
        }

        public override void Visit(IRCompare compare)
        {
            var leftVal = GetLLVMName(compare.Left);
            var rightVal = GetLLVMName(compare.Right);
            var result = GetLLVMName(compare);
            var type = MapType(compare.Left.Type);

            leftVal = LoadIfNeeded(compare.Left, leftVal);
            rightVal = LoadIfNeeded(compare.Right, rightVal);

            string cmpOp;
            string cmpInst;

            if (IsFloatType(compare.Left.Type))
            {
                cmpInst = "fcmp";
                cmpOp = ((LLVMTypeMapper)_typeMapper).MapFloatComparisonOperator(compare.Comparison);
            }
            else
            {
                cmpInst = "icmp";
                cmpOp = _typeMapper.MapComparisonOperator(compare.Comparison);
            }

            WriteLine($"  {result} = {cmpInst} {cmpOp} {type} {leftVal}, {rightVal}");
        }

        public override void Visit(IRAssignment assignment)
        {
            var value = GetLLVMName(assignment.Value);
            var targetName = SanitizeLLVMName(assignment.Target.Name);
            var type = MapType(assignment.Target.Type);

            value = LoadIfNeeded(assignment.Value, value);

            WriteLine($"  store {type} {value}, {type}* %{targetName}.addr");
        }

        public override void Visit(IRLoad load)
        {
            var result = GetLLVMName(load);
            var type = MapType(load.Type);

            if (load.Address is IRVariable variable)
            {
                var varName = SanitizeLLVMName(variable.Name);
                WriteLine($"  {result} = load {type}, {type}* %{varName}.addr");
            }
            else
            {
                var addr = GetLLVMName(load.Address);
                WriteLine($"  {result} = load {type}, {type}* {addr}");
            }
        }

        public override void Visit(IRStore store)
        {
            var value = GetLLVMName(store.Value);
            var type = MapType(store.Value.Type);

            value = LoadIfNeeded(store.Value, value);

            if (store.Address is IRVariable variable)
            {
                var varName = SanitizeLLVMName(variable.Name);
                WriteLine($"  store {type} {value}, {type}* %{varName}.addr");
            }
            else if (store.Address is IRGetElementPtr gep)
            {
                var gepResult = GetLLVMName(gep);
                WriteLine($"  store {type} {value}, {type}* {gepResult}");
            }
            else
            {
                var addr = GetLLVMName(store.Address);
                WriteLine($"  store {type} {value}, {type}* {addr}");
            }
        }

        public override void Visit(IRCall call)
        {
            var args = new List<string>();
            foreach (var arg in call.Arguments)
            {
                var argVal = GetLLVMName(arg);
                argVal = LoadIfNeeded(arg, argVal);
                var argType = MapType(arg.Type);
                args.Add($"{argType} {argVal}");
            }

            var funcName = call.FunctionName;
            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Check if this is an extern function call
            if (_module != null && _module.IsExtern(funcName))
            {
                var externDecl = _module.GetExtern(funcName);
                if (externDecl != null && externDecl.HasImplementation("LLVM"))
                {
                    var impl = externDecl.GetImplementation("LLVM");
                    var extRetType = MapType(call.Type);

                    // For LLVM, the implementation is typically just the function name to call
                    var extArgList = string.Join(", ", args);
                    var externCall = $"call {extRetType} @{impl}({extArgList})";

                    if (hasReturn && !string.IsNullOrEmpty(call.Name))
                    {
                        var extResultName = GetLLVMName(call);
                        if (_declaredIdentifiers.Contains(call.Name))
                        {
                            WriteLine($"  {extResultName} = {externCall}");
                            WriteLine($"  store {extRetType} {extResultName}, {extRetType}* %{SanitizeLLVMName(call.Name)}.addr");
                        }
                        else
                        {
                            WriteLine($"  {extResultName} = {externCall}");
                        }
                    }
                    else
                    {
                        WriteLine($"  {externCall}");
                    }
                    return;
                }
            }

            // Handle standard library calls
            var stdlibResult = EmitStdLibCall(funcName, call.Arguments.ToList(), args);
            if (stdlibResult != null)
            {
                if (hasReturn && !string.IsNullOrEmpty(call.Name))
                {
                    var resultName = GetLLVMName(call);
                    var resultType = MapType(call.Type);

                    // If it's a named destination (local variable), store it
                    if (_declaredIdentifiers.Contains(call.Name))
                    {
                        WriteLine($"  {resultName} = {stdlibResult}");
                        WriteLine($"  store {resultType} {resultName}, {resultType}* %{SanitizeLLVMName(call.Name)}.addr");
                    }
                    else
                    {
                        WriteLine($"  {resultName} = {stdlibResult}");
                    }
                }
                else
                {
                    WriteLine($"  {stdlibResult}");
                }
                return;
            }

            // Regular function call
            var returnType = MapType(call.Type);
            var sanitizedName = SanitizeLLVMName(funcName);
            var argList = string.Join(", ", args);

            if (hasReturn && !string.IsNullOrEmpty(call.Name))
            {
                var resultName = GetLLVMName(call);

                if (_declaredIdentifiers.Contains(call.Name))
                {
                    WriteLine($"  {resultName} = call {returnType} @{sanitizedName}({argList})");
                    WriteLine($"  store {returnType} {resultName}, {returnType}* %{SanitizeLLVMName(call.Name)}.addr");
                }
                else
                {
                    WriteLine($"  {resultName} = call {returnType} @{sanitizedName}({argList})");
                }
            }
            else
            {
                WriteLine($"  call {returnType} @{sanitizedName}({argList})");
            }
        }

        private string EmitStdLibCall(string funcName, List<IRValue> argValues, List<string> args)
        {
            var lower = funcName.ToLower();

            switch (lower)
            {
                case "printline":
                    if (argValues.Count > 0)
                    {
                        var arg = argValues[0];
                        var argType = MapType(arg.Type);

                        if (arg is IRConstant c && c.Value is string str)
                        {
                            if (_stringConstants.TryGetValue(str, out var id))
                            {
                                var len = str.Length + 1;
                                return $"call i32 @puts(i8* getelementptr inbounds ([{len} x i8], [{len} x i8]* @.str.{id}, i64 0, i64 0))";
                            }
                        }

                        // For non-string types, use printf with format
                        var formatId = GetOrCreateFormatString(argType);
                        var argVal = args[0];
                        return $"call i32 (i8*, ...) @printf(i8* getelementptr inbounds ([4 x i8], [4 x i8]* @.fmt.{formatId}, i64 0, i64 0), {argVal})";
                    }
                    return null;

                case "print":
                    if (args.Count > 0)
                    {
                        var argType = MapType(argValues[0].Type);
                        var formatId = GetOrCreateFormatString(argType, newline: false);
                        return $"call i32 (i8*, ...) @printf(i8* getelementptr inbounds ([3 x i8], [3 x i8]* @.fmt.{formatId}, i64 0, i64 0), {args[0]})";
                    }
                    return null;

                case "sqrt":
                    return $"call double @sqrt({args[0]})";
                case "pow":
                    return $"call double @pow({args[0]}, {args[1]})";
                case "sin":
                    return $"call double @sin({args[0]})";
                case "cos":
                    return $"call double @cos({args[0]})";
                case "tan":
                    return $"call double @tan({args[0]})";
                case "log":
                    return $"call double @log({args[0]})";
                case "exp":
                    return $"call double @exp({args[0]})";
                case "floor":
                    return $"call double @floor({args[0]})";
                case "ceiling":
                    return $"call double @ceil({args[0]})";
                case "abs":
                    return $"call double @fabs({args[0]})";
                case "rnd":
                    // rand() returns int, convert to double in [0,1)
                    return "call i32 @rand()"; // Caller needs to convert
                case "randomize":
                    return "call void @srand(i32 0)"; // Simplified

                default:
                    return null;
            }
        }

        private Dictionary<string, int> _formatStrings = new Dictionary<string, int>();
        private int _formatCounter = 0;

        private int GetOrCreateFormatString(string llvmType, bool newline = true)
        {
            var key = $"{llvmType}_{newline}";
            if (_formatStrings.TryGetValue(key, out var id))
                return id;

            id = _formatCounter++;
            _formatStrings[key] = id;

            // We'll need to add these format strings to the output
            // For now, assume they exist
            return id;
        }

        public override void Visit(IRReturn ret)
        {
            if (ret.Value != null)
            {
                var value = GetLLVMName(ret.Value);
                var type = MapType(ret.Value.Type);
                value = LoadIfNeeded(ret.Value, value);
                WriteLine($"  ret {type} {value}");
            }
            else
            {
                WriteLine("  ret void");
            }
        }

        public override void Visit(IRBranch branch)
        {
            var target = SanitizeLLVMLabel(branch.Target.Name);
            WriteLine($"  br label %{target}");
        }

        public override void Visit(IRConditionalBranch condBranch)
        {
            var condition = GetLLVMName(condBranch.Condition);
            condition = LoadIfNeeded(condBranch.Condition, condition);

            var trueTarget = SanitizeLLVMLabel(condBranch.TrueTarget.Name);
            var falseTarget = SanitizeLLVMLabel(condBranch.FalseTarget.Name);

            WriteLine($"  br i1 {condition}, label %{trueTarget}, label %{falseTarget}");
        }

        public override void Visit(IRSwitch switchInst)
        {
            var value = GetLLVMName(switchInst.Value);
            value = LoadIfNeeded(switchInst.Value, value);
            var type = MapType(switchInst.Value.Type);
            var defaultTarget = SanitizeLLVMLabel(switchInst.DefaultTarget.Name);

            var cases = new StringBuilder();
            foreach (var (caseValue, target) in switchInst.Cases)
            {
                var caseVal = GetLLVMName(caseValue);
                var caseTarget = SanitizeLLVMLabel(target.Name);
                cases.Append($" {type} {caseVal}, label %{caseTarget}");
            }

            WriteLine($"  switch {type} {value}, label %{defaultTarget} [{cases}]");
        }

        public override void Visit(IRPhi phi)
        {
            var result = GetLLVMName(phi);
            var type = MapType(phi.Type);

            var incomingValues = new List<string>();
            foreach (var (value, block) in phi.Operands)
            {
                var val = GetLLVMName(value);
                var blockLabel = SanitizeLLVMLabel(block.Name);
                incomingValues.Add($"[ {val}, %{blockLabel} ]");
            }

            WriteLine($"  {result} = phi {type} {string.Join(", ", incomingValues)}");
        }

        public override void Visit(IRAlloca alloca)
        {
            var result = GetLLVMName(alloca);
            var type = MapType(alloca.Type);

            if (alloca.Size > 1)
            {
                WriteLine($"  {result} = alloca {type}, i32 {alloca.Size}");
            }
            else
            {
                WriteLine($"  {result} = alloca {type}");
            }
        }

        public override void Visit(IRGetElementPtr gep)
        {
            var result = GetLLVMName(gep);
            var basePtr = GetLLVMName(gep.BasePointer);
            var baseType = MapType(gep.BasePointer.Type);

            // Remove pointer suffix for element type
            var elementType = baseType.EndsWith("*") ? baseType.Substring(0, baseType.Length - 1) : baseType;

            var indices = string.Join(", ", gep.Indices.Select(i =>
            {
                var idx = GetLLVMName(i);
                idx = LoadIfNeeded(i, idx);
                return $"i64 {idx}";
            }));

            WriteLine($"  {result} = getelementptr inbounds {elementType}, {baseType} {basePtr}, {indices}");
        }

        public override void Visit(IRCast cast)
        {
            var result = GetLLVMName(cast);
            var value = GetLLVMName(cast.Value);
            value = LoadIfNeeded(cast.Value, value);

            var fromType = MapType(cast.Value.Type);
            var toType = MapType(cast.Type);

            var castOp = GetCastOperation(cast.Value.Type, cast.Type);
            WriteLine($"  {result} = {castOp} {fromType} {value} to {toType}");
        }

        private string GetCastOperation(TypeInfo fromType, TypeInfo toType)
        {
            var fromFloat = IsFloatType(fromType);
            var toFloat = IsFloatType(toType);
            var fromSize = GetTypeSize(fromType);
            var toSize = GetTypeSize(toType);

            if (fromFloat && !toFloat) return "fptosi";
            if (!fromFloat && toFloat) return "sitofp";
            if (fromFloat && toFloat)
            {
                return fromSize < toSize ? "fpext" : "fptrunc";
            }

            // Integer conversions
            if (fromSize < toSize) return "sext";
            if (fromSize > toSize) return "trunc";
            return "bitcast";
        }

        private int GetTypeSize(TypeInfo type)
        {
            if (type == null) return 32;
            var name = type.Name?.ToLower() ?? "";
            return name switch
            {
                "byte" or "char" or "boolean" => 8,
                "short" => 16,
                "integer" or "single" or "float" => 32,
                "long" or "double" => 64,
                _ => 32
            };
        }

        public override void Visit(IRLabel label)
        {
            WriteLine($"{SanitizeLLVMLabel(label.Name)}:");
        }

        public override void Visit(IRComment comment)
        {
            if (_options.GenerateComments)
            {
                WriteLine($"  ; {comment.Text}");
            }
        }

        public override void Visit(IRArrayAlloc arrayAlloc)
        {
            var elementType = MapType(arrayAlloc.ElementType);
            var result = $"%{SanitizeLLVMName(arrayAlloc.Name)}";
            WriteLine($"  {result} = alloca [{arrayAlloc.Size} x {elementType}]");
        }

        public override void Visit(IRArrayStore arrayStore)
        {
            var arrayName = arrayStore.Array is IRVariable v ? SanitizeLLVMName(v.Name) : arrayStore.Array.Name;
            var elementType = MapType(arrayStore.Array.Type?.ElementType ?? new TypeInfo("i32", TypeKind.Primitive));
            var indexVal = arrayStore.Index is IRConstant ic ? $"i32 {ic.Value}" : $"i32 {GetLLVMName(arrayStore.Index)}";
            var valueVal = arrayStore.Value is IRConstant vc ? $"{vc.Value}" : GetLLVMName(arrayStore.Value);
            var gep = $"%t{_tempCounter++}";
            var arraySize = (arrayStore.Array as IRArrayAlloc)?.Size ?? 0;
            WriteLine($"  {gep} = getelementptr [{arraySize} x {elementType}], [{arraySize} x {elementType}]* %{arrayName}, i32 0, {indexVal}");
            WriteLine($"  store {elementType} {valueVal}, {elementType}* {gep}");
        }

        public override void Visit(IRAwait awaitInst)
        {
            // LLVM doesn't have native async/await, generate a comment
            WriteLine($"  ; await - LLVM async not supported");
        }

        public override void Visit(IRYield yieldInst)
        {
            // LLVM doesn't have native yield, generate a comment
            if (yieldInst.IsBreak)
                WriteLine("  ; yield break - LLVM iterators not supported");
            else
                WriteLine($"  ; yield return - LLVM iterators not supported");
        }

        public override void Visit(IRNewObject newObj)
        {
            var className = SanitizeLLVMName(newObj.ClassName);
            var structType = _classStructTypes.GetValueOrDefault(newObj.ClassName, $"%class.{className}");
            var result = GetLLVMName(newObj);

            // Build constructor arguments
            var args = new List<string>();
            foreach (var arg in newObj.Arguments)
            {
                var argVal = GetLLVMName(arg);
                argVal = LoadIfNeeded(arg, argVal);
                var argType = MapType(arg.Type);
                args.Add($"{argType} {argVal}");
            }

            var argList = string.Join(", ", args);

            // Call constructor
            WriteLine($"  {result} = call {structType}* @{className}_ctor({argList})");

            // Store to variable if named destination
            if (IsNamedDestination(newObj))
            {
                var varName = SanitizeLLVMName(newObj.Name);
                WriteLine($"  store {structType}* {result}, {structType}** %{varName}.addr");
            }
        }

        public override void Visit(IRInstanceMethodCall methodCall)
        {
            var className = methodCall.Object?.Type?.Name ?? "Unknown";
            var sanitizedClassName = SanitizeLLVMName(className);
            var methodName = SanitizeLLVMName(methodCall.MethodName);
            var structType = _classStructTypes.GetValueOrDefault(className, $"%class.{sanitizedClassName}");
            var returnType = MapType(methodCall.Type);

            // Get object pointer
            var objVal = GetLLVMName(methodCall.Object);
            objVal = LoadIfNeeded(methodCall.Object, objVal);

            // Build arguments (this + actual args)
            var args = new List<string> { $"{structType}* {objVal}" };
            foreach (var arg in methodCall.Arguments)
            {
                var argVal = GetLLVMName(arg);
                argVal = LoadIfNeeded(arg, argVal);
                var argType = MapType(arg.Type);
                args.Add($"{argType} {argVal}");
            }

            var argList = string.Join(", ", args);
            var hasReturn = methodCall.Type != null && !methodCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Check if this is a virtual call
            bool isVirtual = methodCall.IsVirtual || (_classVirtualMethods.ContainsKey(className) &&
                             _classVirtualMethods[className].Any(m => m.Name.Equals(methodCall.MethodName, StringComparison.OrdinalIgnoreCase) && (m.IsVirtual || m.IsOverride)));

            if (isVirtual && _classVtableTypes.ContainsKey(className))
            {
                // Virtual dispatch via vtable
                var vtableTypeName = _classVtableTypes[className];
                var virtualMethods = _classVirtualMethods[className];
                var methodIndex = virtualMethods.FindIndex(m => m.Name.Equals(methodCall.MethodName, StringComparison.OrdinalIgnoreCase));

                if (methodIndex >= 0)
                {
                    // Load vtable pointer
                    var vtablePtrGep = $"%t{_tempCounter++}";
                    WriteLine($"  {vtablePtrGep} = getelementptr inbounds {structType}, {structType}* {objVal}, i32 0, i32 0");
                    var vtablePtr = $"%t{_tempCounter++}";
                    WriteLine($"  {vtablePtr} = load {vtableTypeName}*, {vtableTypeName}** {vtablePtrGep}");

                    // Get function pointer from vtable
                    var funcPtrGep = $"%t{_tempCounter++}";
                    WriteLine($"  {funcPtrGep} = getelementptr inbounds {vtableTypeName}, {vtableTypeName}* {vtablePtr}, i32 0, i32 {methodIndex}");

                    // Build function pointer type
                    var paramTypes = new List<string> { $"{structType}*" };
                    var method = virtualMethods[methodIndex];
                    if (method.Implementation != null)
                    {
                        foreach (var param in method.Implementation.Parameters)
                        {
                            paramTypes.Add(MapType(param.Type));
                        }
                    }
                    var funcPtrType = $"{returnType} ({string.Join(", ", paramTypes)})*";

                    var funcPtr = $"%t{_tempCounter++}";
                    WriteLine($"  {funcPtr} = load {funcPtrType}, {funcPtrType}* {funcPtrGep}");

                    // Call via function pointer
                    if (hasReturn)
                    {
                        var result = GetLLVMName(methodCall);
                        WriteLine($"  {result} = call {returnType} {funcPtr}({argList})");

                        if (IsNamedDestination(methodCall))
                        {
                            var varName = SanitizeLLVMName(methodCall.Name);
                            WriteLine($"  store {returnType} {result}, {returnType}* %{varName}.addr");
                        }
                    }
                    else
                    {
                        WriteLine($"  call {returnType} {funcPtr}({argList})");
                    }
                    return;
                }
            }

            // Non-virtual direct call
            if (hasReturn)
            {
                var result = GetLLVMName(methodCall);
                WriteLine($"  {result} = call {returnType} @{sanitizedClassName}_{methodName}({argList})");

                if (IsNamedDestination(methodCall))
                {
                    var varName = SanitizeLLVMName(methodCall.Name);
                    WriteLine($"  store {returnType} {result}, {returnType}* %{varName}.addr");
                }
            }
            else
            {
                WriteLine($"  call {returnType} @{sanitizedClassName}_{methodName}({argList})");
            }
        }

        public override void Visit(IRBaseMethodCall baseCall)
        {
            // For base calls, we call the parent class method directly
            // We need to determine the base class from the current context
            var methodName = SanitizeLLVMName(baseCall.MethodName);
            var returnType = MapType(baseCall.Type);

            // Build arguments
            var args = new List<string> { "i8* %this" };  // Simplified this pointer
            foreach (var arg in baseCall.Arguments)
            {
                var argVal = GetLLVMName(arg);
                argVal = LoadIfNeeded(arg, argVal);
                var argType = MapType(arg.Type);
                args.Add($"{argType} {argVal}");
            }

            var argList = string.Join(", ", args);
            var hasReturn = baseCall.Type != null && !baseCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Generate comment for now since we don't track base class in IR
            WriteLine($"  ; base.{baseCall.MethodName}() call");
            if (hasReturn)
            {
                var result = GetLLVMName(baseCall);
                WriteLine($"  {result} = call {returnType} @Base_{methodName}({argList})");
            }
            else
            {
                WriteLine($"  call {returnType} @Base_{methodName}({argList})");
            }
        }

        public override void Visit(IRFieldAccess fieldAccess)
        {
            var className = fieldAccess.Object?.Type?.Name ?? "Unknown";
            var sanitizedClassName = SanitizeLLVMName(className);
            var fieldName = fieldAccess.FieldName;
            var structType = _classStructTypes.GetValueOrDefault(className, $"%class.{sanitizedClassName}");
            var fieldType = MapType(fieldAccess.Type);

            // Get field index (accounting for vtable pointer if present)
            var fieldIndex = 0;
            if (_classFieldIndices.TryGetValue(className, out var indices) && indices.TryGetValue(fieldName, out var idx))
            {
                fieldIndex = idx;
            }
            else
            {
                // If not found in this class, might be an inherited field - search base classes
                var currentClass = className;
                while (!string.IsNullOrEmpty(currentClass))
                {
                    if (_module.Classes.TryGetValue(currentClass, out var irClass))
                    {
                        if (_classFieldIndices.TryGetValue(currentClass, out var baseIndices) && baseIndices.TryGetValue(fieldName, out idx))
                        {
                            fieldIndex = idx;
                            break;
                        }
                        currentClass = irClass.BaseClass;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Get object pointer
            var objVal = GetLLVMName(fieldAccess.Object);
            objVal = LoadIfNeeded(fieldAccess.Object, objVal);

            var result = GetLLVMName(fieldAccess);
            var gepResult = $"%t{_tempCounter++}";

            // GEP to get field pointer
            WriteLine($"  {gepResult} = getelementptr inbounds {structType}, {structType}* {objVal}, i32 0, i32 {fieldIndex}");

            // Load the field value
            WriteLine($"  {result} = load {fieldType}, {fieldType}* {gepResult}");

            // Store to variable if named destination
            if (IsNamedDestination(fieldAccess))
            {
                var varName = SanitizeLLVMName(fieldAccess.Name);
                WriteLine($"  store {fieldType} {result}, {fieldType}* %{varName}.addr");
            }
        }

        public override void Visit(IRFieldStore fieldStore)
        {
            var className = fieldStore.Object?.Type?.Name ?? "Unknown";
            var sanitizedClassName = SanitizeLLVMName(className);
            var fieldName = fieldStore.FieldName;
            var structType = _classStructTypes.GetValueOrDefault(className, $"%class.{sanitizedClassName}");
            var fieldType = MapType(fieldStore.Value?.Type);

            // Get field index (accounting for vtable pointer if present)
            var fieldIndex = 0;
            if (_classFieldIndices.TryGetValue(className, out var indices) && indices.TryGetValue(fieldName, out var idx))
            {
                fieldIndex = idx;
            }
            else
            {
                // If not found in this class, might be an inherited field - search base classes
                var currentClass = className;
                while (!string.IsNullOrEmpty(currentClass))
                {
                    if (_module.Classes.TryGetValue(currentClass, out var irClass))
                    {
                        if (_classFieldIndices.TryGetValue(currentClass, out var baseIndices) && baseIndices.TryGetValue(fieldName, out idx))
                        {
                            fieldIndex = idx;
                            break;
                        }
                        currentClass = irClass.BaseClass;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            // Get object pointer
            var objVal = GetLLVMName(fieldStore.Object);
            objVal = LoadIfNeeded(fieldStore.Object, objVal);

            // Get value to store
            var value = GetLLVMName(fieldStore.Value);
            value = LoadIfNeeded(fieldStore.Value, value);

            var gepResult = $"%t{_tempCounter++}";

            // GEP to get field pointer
            WriteLine($"  {gepResult} = getelementptr inbounds {structType}, {structType}* {objVal}, i32 0, i32 {fieldIndex}");

            // Store the value
            WriteLine($"  store {fieldType} {value}, {fieldType}* {gepResult}");
        }

        public override void Visit(IRTupleElement tupleElement)
        {
            // LLVM tuple element access - extract value from tuple struct
            var tupleVal = GetLLVMName(tupleElement.Tuple);
            tupleVal = LoadIfNeeded(tupleElement.Tuple, tupleVal);
            var elemType = MapType(tupleElement.Type);
            var resultName = SanitizeLLVMName(tupleElement.Name);

            // Use extractvalue for LLVM tuple/struct element access
            var result = $"%t{_tempCounter++}";
            WriteLine($"  {result} = extractvalue {{ {elemType} }} {tupleVal}, {tupleElement.Index}");
            _llvmNames[tupleElement] = result;
        }

        public override void Visit(IRTryCatch tryCatch)
        {
            // LLVM exception handling using invoke/landingpad
            // Note: Requires linking with C++ runtime for full exception support

            var tryLabel = $"try.{_labelCounter++}";
            var catchLabel = $"catch.{_labelCounter++}";
            var endLabel = $"try.end.{_labelCounter++}";
            var landingLabel = $"landing.{_labelCounter++}";

            WriteLine($"; Try-Catch block");
            WriteLine($"  br label %{tryLabel}");
            WriteLine();

            // Try block
            WriteLine($"{tryLabel}:");
            foreach (var inst in tryCatch.TryBlock.Instructions)
            {
                if (inst is IRBranch or IRConditionalBranch) continue;

                // For calls in try blocks, we should use invoke instead of call
                // But for simplicity, we'll emit regular instructions and rely on
                // the C++ runtime's exception handling
                inst.Accept(this);
            }
            WriteLine($"  br label %{endLabel}");
            WriteLine();

            // Landing pad for exception handling
            WriteLine($"{landingLabel}:");
            WriteLine($"  %exc = landingpad {{ i8*, i32 }}");
            WriteLine($"          catch i8* null  ; catch all exceptions");
            WriteLine($"  %exc.ptr = extractvalue {{ i8*, i32 }} %exc, 0");
            WriteLine($"  br label %{catchLabel}");
            WriteLine();

            // Catch blocks
            foreach (var catchClause in tryCatch.CatchClauses)
            {
                var exType = catchClause.ExceptionType?.Name ?? "Exception";
                WriteLine($"{catchLabel}:  ; Catch {exType}");

                // If catch has a variable, store the exception pointer
                if (!string.IsNullOrEmpty(catchClause.VariableName))
                {
                    var varName = SanitizeName(catchClause.VariableName);
                    WriteLine($"  ; Exception stored in {varName} (ptr: %exc.ptr)");
                }

                foreach (var inst in catchClause.Block.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
                WriteLine($"  br label %{endLabel}");
                WriteLine();
            }

            // Finally block (if present)
            if (tryCatch.FinallyBlock != null)
            {
                var finallyLabel = $"finally.{_labelCounter++}";
                WriteLine($"{finallyLabel}:");
                foreach (var inst in tryCatch.FinallyBlock.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
                WriteLine($"  br label %{endLabel}");
                WriteLine();
            }

            // End block
            WriteLine($"{endLabel}:");
        }

        public override void Visit(IRInlineCode inlineCode)
        {
            if (inlineCode.Language.ToLower() == "llvm")
            {
                // Emit the LLVM IR code directly
                WriteLine("; Inline LLVM IR code");
                foreach (var line in inlineCode.Code.Split('\n'))
                {
                    WriteLine(line.TrimEnd());
                }
            }
            else
            {
                // For non-LLVM inline code, emit a comment indicating it's not supported
                WriteLine($"; WARNING: Inline {inlineCode.Language} code not supported in LLVM backend");
                WriteLine($"; Original code ({inlineCode.Code.Length} chars) was skipped");
            }
        }

        public override void Visit(IRForEach forEach)
        {
            // LLVM doesn't have native foreach - lower to index-based loop
            // For arrays: iterate from 0 to length-1
            // For collections: use GetEnumerator pattern (simplified here)

            var loopVar = SanitizeName(forEach.VariableName);
            var elemType = MapType(forEach.ElementType);
            var collectionVal = GetValueName(forEach.Collection);

            var initLabel = $"foreach.init.{_labelCounter++}";
            var condLabel = $"foreach.cond.{_labelCounter++}";
            var bodyLabel = $"foreach.body.{_labelCounter++}";
            var incLabel = $"foreach.inc.{_labelCounter++}";
            var endLabel = $"foreach.end.{_labelCounter++}";

            WriteLine($"; ForEach loop over {collectionVal}");
            WriteLine($"  br label %{initLabel}");
            WriteLine();

            // Initialize index
            WriteLine($"{initLabel}:");
            var indexVar = $"%foreach.idx.{_tempCounter++}";
            WriteLine($"  {indexVar} = alloca i32");
            WriteLine($"  store i32 0, i32* {indexVar}");

            // Get collection length (simplified - assumes array with known length method)
            var lenVar = $"%foreach.len.{_tempCounter++}";
            WriteLine($"  ; Get collection length");
            WriteLine($"  {lenVar} = call i32 @__get_collection_length(i8* {collectionVal})");
            WriteLine($"  br label %{condLabel}");
            WriteLine();

            // Condition check
            WriteLine($"{condLabel}:");
            var curIdx = $"%foreach.curidx.{_tempCounter++}";
            WriteLine($"  {curIdx} = load i32, i32* {indexVar}");
            var cmpResult = $"%foreach.cmp.{_tempCounter++}";
            WriteLine($"  {cmpResult} = icmp slt i32 {curIdx}, {lenVar}");
            WriteLine($"  br i1 {cmpResult}, label %{bodyLabel}, label %{endLabel}");
            WriteLine();

            // Body - get current element
            WriteLine($"{bodyLabel}:");
            var elemPtr = $"%foreach.elem.{_tempCounter++}";
            WriteLine($"  ; Get element at index {curIdx}");
            WriteLine($"  {elemPtr} = call {elemType}* @__get_collection_element(i8* {collectionVal}, i32 {curIdx})");
            var elemVal = $"%{loopVar}";
            WriteLine($"  {elemVal} = load {elemType}, {elemType}* {elemPtr}");
            // Loop variable is now available as %{loopVar} for body instructions

            // Process body block
            if (forEach.BodyBlock != null)
            {
                foreach (var inst in forEach.BodyBlock.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
            }
            WriteLine($"  br label %{incLabel}");
            WriteLine();

            // Increment index
            WriteLine($"{incLabel}:");
            var nextIdx = $"%foreach.next.{_tempCounter++}";
            WriteLine($"  {nextIdx} = add i32 {curIdx}, 1");
            WriteLine($"  store i32 {nextIdx}, i32* {indexVar}");
            WriteLine($"  br label %{condLabel}");
            WriteLine();

            // End
            WriteLine($"{endLabel}:");
        }

        public override void Visit(IRIndexerAccess indexer)
        {
            // LLVM indexer access - use getelementptr for arrays/collections
            var collectionVal = GetValueName(indexer.Collection);
            var resultType = MapType(indexer.Type);

            if (indexer.Indices.Count == 1)
            {
                var indexVal = GetValueName(indexer.Indices[0]);
                var ptrResult = $"%idx.ptr.{_tempCounter++}";
                var result = $"%idx.val.{_tempCounter++}";

                WriteLine($"; Indexer access: {collectionVal}[{indexVal}]");
                WriteLine($"  {ptrResult} = getelementptr {resultType}, {resultType}* {collectionVal}, i32 {indexVal}");
                WriteLine($"  {result} = load {resultType}, {resultType}* {ptrResult}");
                _llvmNames[indexer] = result;
            }
            else
            {
                // Multi-dimensional indexing
                var indices = string.Join(", ", indexer.Indices.Select(i => $"i32 {GetValueName(i)}"));
                var ptrResult = $"%idx.ptr.{_tempCounter++}";
                var result = $"%idx.val.{_tempCounter++}";

                WriteLine($"; Multi-dimensional indexer access");
                WriteLine($"  {ptrResult} = getelementptr {resultType}, {resultType}* {collectionVal}, {indices}");
                WriteLine($"  {result} = load {resultType}, {resultType}* {ptrResult}");
                _llvmNames[indexer] = result;
            }
        }

        #endregion

        private string LoadIfNeeded(IRValue value, string llvmValue)
        {
            // If this is a variable reference, we need to load from its address
            if (value is IRVariable variable && _declaredIdentifiers.Contains(variable.Name))
            {
                var type = MapType(variable.Type);
                var loadResult = $"%t{_tempCounter++}";
                var varName = SanitizeLLVMName(variable.Name);
                WriteLine($"  {loadResult} = load {type}, {type}* %{varName}.addr");
                return loadResult;
            }
            return llvmValue;
        }

        private void WriteLine(string text = "")
        {
            _output.AppendLine(text);
        }
    }

    public class LLVMCodeGenOptions
    {
        public bool GenerateComments { get; set; } = true;
        public bool OptimizationEnabled { get; set; } = false;
        public string TargetTriple { get; set; } = "x86_64-pc-windows-msvc";
    }
}
