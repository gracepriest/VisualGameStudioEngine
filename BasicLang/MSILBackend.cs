using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.MSIL
{
    /// <summary>
    /// MSIL (.NET IL) code generator - emits textual IL assembly (.il files)
    /// Can be compiled with ilasm to create .NET assemblies
    /// </summary>
    public class MSILCodeGenerator : CodeGeneratorBase
    {
        private readonly StringBuilder _output;
        private readonly MSILCodeGenOptions _options;
        private readonly Dictionary<string, int> _localIndices;
        private readonly Dictionary<string, int> _paramIndices;
        private readonly Dictionary<IRValue, int> _tempIndices;
        private readonly Dictionary<string, int> _tempNameIndices;
        private readonly HashSet<string> _declaredIdentifiers;
        private readonly List<string> _stringConstants;
        private string _moduleName;
        private int _localCounter;
        private int _labelCounter;
        private int _maxStack;
        private int _currentStack;
        private IRModule _module;
        private IRClass _currentClass;

        public override string BackendName => "MSIL";
        public override TargetPlatform Target => TargetPlatform.MSIL;

        public MSILCodeGenerator(MSILCodeGenOptions options = null)
        {
            _output = new StringBuilder();
            _options = options ?? new MSILCodeGenOptions();
            _localIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _paramIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _tempIndices = new Dictionary<IRValue, int>();
            _tempNameIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _declaredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _stringConstants = new List<string>();
            _typeMapper = new MSILTypeMapper();
        }

        protected override void InitializeTypeMap()
        {
            // MSIL type mappings
            _typeMap["Integer"] = "int32";
            _typeMap["Long"] = "int64";
            _typeMap["Single"] = "float32";
            _typeMap["Double"] = "float64";
            _typeMap["String"] = "string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "object";
            _typeMap["Byte"] = "uint8";
            _typeMap["Short"] = "int16";
        }

        public override string Generate(IRModule module)
        {
            _module = module;
            _output.Clear();
            _stringConstants.Clear();
            _labelCounter = 0;

            // Generate assembly header
            GenerateHeader(module);

            // Generate enums
            foreach (var irEnum in module.Enums.Values)
            {
                GenerateEnum(irEnum);
                WriteLine();
            }

            // Generate delegates
            foreach (var irDelegate in module.Delegates.Values)
            {
                GenerateDelegate(irDelegate);
                WriteLine();
            }

            // Generate interfaces
            foreach (var irInterface in module.Interfaces.Values)
            {
                GenerateInterface(irInterface);
                WriteLine();
            }

            // Generate user-defined classes
            foreach (var irClass in module.Classes.Values)
            {
                GenerateUserClass(irClass);
                WriteLine();
            }

            // Generate main module class with standalone methods
            GenerateClass(module);

            return _output.ToString();
        }

        private void GenerateEnum(IREnum irEnum)
        {
            var enumName = SanitizeName(irEnum.Name);
            var underlyingType = "int32";
            if (irEnum.UnderlyingType != null)
            {
                underlyingType = MapType(irEnum.UnderlyingType);
            }

            WriteLine($".class public auto ansi sealed {enumName}");
            WriteLine("       extends [mscorlib]System.Enum");
            WriteLine("{");

            // Value field
            WriteLine($"  .field public specialname rtspecialname {underlyingType} value__");

            // Enum members as static literal fields
            foreach (var member in irEnum.Members)
            {
                var value = member.Value ?? 0;
                WriteLine($"  .field public static literal valuetype {enumName} {SanitizeName(member.Name)} = {underlyingType}({value})");
            }

            WriteLine($"}} // end of class {enumName}");
        }

        private void GenerateDelegate(IRDelegate irDelegate)
        {
            var delegateName = SanitizeName(irDelegate.Name);
            var returnType = MapType(irDelegate.ReturnType);
            var paramTypes = string.Join(", ", irDelegate.Parameters.Select(p => MapTypeName(p.TypeName)));

            WriteLine($".class public auto ansi sealed {delegateName}");
            WriteLine("       extends [mscorlib]System.MulticastDelegate");
            WriteLine("{");

            // Constructor
            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor(object 'object', native int 'method') runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method .ctor");
            WriteLine();

            // Invoke method
            WriteLine("  .method public hidebysig newslot virtual");
            WriteLine($"          instance {returnType} Invoke({paramTypes}) runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method Invoke");
            WriteLine();

            // BeginInvoke
            WriteLine("  .method public hidebysig newslot virtual");
            WriteLine($"          instance class [mscorlib]System.IAsyncResult BeginInvoke({paramTypes}, class [mscorlib]System.AsyncCallback callback, object 'object') runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method BeginInvoke");
            WriteLine();

            // EndInvoke
            WriteLine("  .method public hidebysig newslot virtual");
            WriteLine($"          instance {returnType} EndInvoke(class [mscorlib]System.IAsyncResult result) runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method EndInvoke");

            WriteLine($"}} // end of class {delegateName}");
        }

        private string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "object";
            return typeName.ToLowerInvariant() switch
            {
                "integer" => "int32",
                "long" => "int64",
                "single" => "float32",
                "double" => "float64",
                "string" => "string",
                "boolean" => "bool",
                "byte" => "uint8",
                "short" => "int16",
                "object" => "object",
                "void" => "void",
                _ => SanitizeName(typeName)
            };
        }

        private void GenerateInterface(IRInterface irInterface)
        {
            var interfaceName = SanitizeName(irInterface.Name);

            // Build implements list
            var implements = "";
            if (irInterface.BaseInterfaces.Count > 0)
            {
                implements = " implements " + string.Join(", ", irInterface.BaseInterfaces.Select(b => SanitizeName(b)));
            }

            WriteLine($".class interface public abstract auto ansi {interfaceName}{implements}");
            WriteLine("{");

            // Interface methods
            foreach (var method in irInterface.Methods)
            {
                var returnType = MapType(method.ReturnType);
                var methodName = SanitizeName(method.Name);
                var paramTypes = string.Join(", ", method.Parameters.Select(p => MapTypeName(p.TypeName)));

                WriteLine("  .method public hidebysig newslot abstract virtual");
                WriteLine($"          instance {returnType} {methodName}({paramTypes}) cil managed");
                WriteLine("  {");
                WriteLine("  } // end of method " + methodName);
                WriteLine();
            }

            // Interface properties
            foreach (var prop in irInterface.Properties)
            {
                var propType = MapType(prop.Type);
                var propName = SanitizeName(prop.Name);

                WriteLine($"  .property instance {propType} {propName}()");
                WriteLine("  {");
                if (prop.HasGetter)
                    WriteLine($"    .get instance {propType} {interfaceName}::get_{propName}()");
                if (prop.HasSetter)
                    WriteLine($"    .set instance void {interfaceName}::set_{propName}({propType})");
                WriteLine("  }");
                WriteLine();

                // Getter method
                if (prop.HasGetter)
                {
                    WriteLine("  .method public hidebysig newslot specialname abstract virtual");
                    WriteLine($"          instance {propType} get_{propName}() cil managed");
                    WriteLine("  {");
                    WriteLine("  }");
                }

                // Setter method
                if (prop.HasSetter)
                {
                    WriteLine("  .method public hidebysig newslot specialname abstract virtual");
                    WriteLine($"          instance void set_{propName}({propType} 'value') cil managed");
                    WriteLine("  {");
                    WriteLine("  }");
                }
            }

            WriteLine($"}} // end of interface {interfaceName}");
        }

        private void GenerateUserClass(IRClass irClass)
        {
            _currentClass = irClass;
            var className = SanitizeName(irClass.Name);

            // Build extends and implements
            var extends = "[mscorlib]System.Object";
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                extends = SanitizeName(irClass.BaseClass);
            }

            var implements = "";
            if (irClass.Interfaces.Count > 0)
            {
                implements = " implements " + string.Join(", ", irClass.Interfaces.Select(i => SanitizeName(i)));
            }

            WriteLine($".class public auto ansi beforefieldinit {className}");
            WriteLine($"       extends {extends}{implements}");
            WriteLine("{");

            // Fields
            foreach (var field in irClass.Fields)
            {
                var access = MapAccessModifier(field.Access);
                var staticMod = field.IsStatic ? "static " : "";
                var fieldType = MapType(field.Type);
                var fieldName = SanitizeName(field.Name);
                WriteLine($"  .field {access} {staticMod}{fieldType} {fieldName}");
            }

            if (irClass.Fields.Count > 0)
                WriteLine();

            // Events
            foreach (var evt in irClass.Events)
            {
                GenerateEvent(irClass, evt);
            }

            // Properties
            foreach (var prop in irClass.Properties)
            {
                GenerateProperty(irClass, prop);
            }

            // Constructors
            foreach (var ctor in irClass.Constructors)
            {
                GenerateConstructor(irClass, ctor);
            }

            // Default constructor if none defined
            if (irClass.Constructors.Count == 0)
            {
                GenerateDefaultCtorForClass(irClass);
            }

            // Methods
            foreach (var method in irClass.Methods)
            {
                GenerateClassMethod(irClass, method);
            }

            WriteLine($"}} // end of class {className}");
            _currentClass = null;
        }

        private string MapAccessModifier(AccessModifier access)
        {
            return access switch
            {
                AccessModifier.Public => "public",
                AccessModifier.Private => "private",
                AccessModifier.Protected => "family",
                AccessModifier.Friend => "assembly",
                _ => "private"
            };
        }

        private void GenerateEvent(IRClass irClass, IREvent evt)
        {
            var delegateType = SanitizeName(evt.DelegateType);
            var eventName = SanitizeName(evt.Name);
            var staticMod = evt.IsStatic ? "static " : "";

            // Backing field
            WriteLine($"  .field private {staticMod}class {delegateType} {eventName}");

            // Event declaration
            WriteLine($"  .event class {delegateType} {eventName}");
            WriteLine("  {");
            WriteLine($"    .addon instance void {SanitizeName(irClass.Name)}::add_{eventName}(class {delegateType})");
            WriteLine($"    .removeon instance void {SanitizeName(irClass.Name)}::remove_{eventName}(class {delegateType})");
            WriteLine("  }");
            WriteLine();

            // Add method
            WriteLine($"  .method public hidebysig specialname {staticMod}instance void");
            WriteLine($"          add_{eventName}(class {delegateType} 'value') cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            if (!evt.IsStatic) WriteLine("    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    ldsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ldarg.1");
            WriteLine($"    call class [mscorlib]System.Delegate [mscorlib]System.Delegate::Combine(class [mscorlib]System.Delegate, class [mscorlib]System.Delegate)");
            WriteLine($"    castclass {delegateType}");
            if (!evt.IsStatic) WriteLine($"    stfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    stsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ret");
            WriteLine("  }");
            WriteLine();

            // Remove method
            WriteLine($"  .method public hidebysig specialname {staticMod}instance void");
            WriteLine($"          remove_{eventName}(class {delegateType} 'value') cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            if (!evt.IsStatic) WriteLine("    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    ldsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ldarg.1");
            WriteLine($"    call class [mscorlib]System.Delegate [mscorlib]System.Delegate::Remove(class [mscorlib]System.Delegate, class [mscorlib]System.Delegate)");
            WriteLine($"    castclass {delegateType}");
            if (!evt.IsStatic) WriteLine($"    stfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    stsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ret");
            WriteLine("  }");
            WriteLine();
        }

        private void GenerateProperty(IRClass irClass, IRProperty prop)
        {
            var propType = MapType(prop.Type);
            var propName = SanitizeName(prop.Name);
            var className = SanitizeName(irClass.Name);
            var staticMod = prop.IsStatic ? "static " : "";
            var instanceMod = prop.IsStatic ? "" : "instance ";

            // Property declaration
            WriteLine($"  .property {instanceMod}{propType} {propName}()");
            WriteLine("  {");
            if (!prop.IsWriteOnly)
                WriteLine($"    .get {instanceMod}{propType} {className}::get_{propName}()");
            if (!prop.IsReadOnly)
                WriteLine($"    .set {instanceMod}void {className}::set_{propName}({propType})");
            WriteLine("  }");
            WriteLine();

            // Getter
            if (prop.Getter != null && !prop.IsWriteOnly)
            {
                var virtualMod = "";  // Could add virtual if needed
                WriteLine($"  .method public hidebysig specialname {virtualMod}{staticMod}");
                WriteLine($"          {instanceMod}{propType} get_{propName}() cil managed");
                WriteLine("  {");
                WriteLine("    .maxstack 8");

                if (prop.Getter.EntryBlock != null)
                {
                    _currentFunction = prop.Getter;
                    InitializeMethodContext(prop.Getter);
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(prop.Getter.EntryBlock, visited, isEntry: true);
                    _currentFunction = null;
                }
                else
                {
                    // Default: return default value
                    WriteLine($"    ldnull");
                    WriteLine("    ret");
                }

                WriteLine($"  }} // end of method get_{propName}");
                WriteLine();
            }

            // Setter
            if (prop.Setter != null && !prop.IsReadOnly)
            {
                var virtualMod = "";
                WriteLine($"  .method public hidebysig specialname {virtualMod}{staticMod}");
                WriteLine($"          {instanceMod}void set_{propName}({propType} 'value') cil managed");
                WriteLine("  {");
                WriteLine("    .maxstack 8");

                if (prop.Setter.EntryBlock != null)
                {
                    _currentFunction = prop.Setter;
                    InitializeMethodContext(prop.Setter);
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(prop.Setter.EntryBlock, visited, isEntry: true);
                    _currentFunction = null;
                }

                if (!EndsWithRet())
                    WriteLine("    ret");

                WriteLine($"  }} // end of method set_{propName}");
                WriteLine();
            }
        }

        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var className = SanitizeName(irClass.Name);
            var paramTypes = "";

            if (ctor.Implementation != null)
            {
                paramTypes = string.Join(", ", ctor.Implementation.Parameters.Select(p =>
                    $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
            }

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine($"          instance void .ctor({paramTypes}) cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");

            // Call base constructor
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : SanitizeName(irClass.BaseClass);
            WriteLine("    ldarg.0");
            WriteLine($"    call instance void {baseClass}::.ctor()");

            // Generate constructor body
            if (ctor.Implementation?.EntryBlock != null)
            {
                _currentFunction = ctor.Implementation;
                InitializeMethodContext(ctor.Implementation);
                var visited = new HashSet<BasicBlock>();
                GenerateBasicBlock(ctor.Implementation.EntryBlock, visited, isEntry: true);
                _currentFunction = null;
            }

            if (!EndsWithRet())
                WriteLine("    ret");

            WriteLine("  } // end of method .ctor");
            WriteLine();
        }

        private void GenerateDefaultCtorForClass(IRClass irClass)
        {
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : SanitizeName(irClass.BaseClass);

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            WriteLine($"    call instance void {baseClass}::.ctor()");
            WriteLine("    ret");
            WriteLine("  } // end of method .ctor");
            WriteLine();
        }

        private void GenerateClassMethod(IRClass irClass, IRMethod method)
        {
            var className = SanitizeName(irClass.Name);
            var methodName = SanitizeName(method.Name);
            var returnType = MapType(method.ReturnType);
            var staticMod = method.IsStatic ? "static " : "";
            var instanceMod = method.IsStatic ? "" : "instance ";

            // Handle virtual/override/abstract/sealed modifiers properly
            var modifiers = "";
            if (method.IsAbstract)
            {
                modifiers = "abstract virtual ";
            }
            else if (method.IsOverride && method.IsSealed)
            {
                modifiers = "final virtual ";
            }
            else if (method.IsOverride)
            {
                modifiers = "virtual ";
            }
            else if (method.IsVirtual)
            {
                modifiers = "newslot virtual ";
            }

            var paramTypes = "";
            if (method.Implementation != null)
            {
                paramTypes = string.Join(", ", method.Implementation.Parameters.Select(p =>
                    $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
            }

            WriteLine($"  .method public hidebysig {modifiers}{staticMod}");
            WriteLine($"          {instanceMod}{returnType} {methodName}({paramTypes}) cil managed");
            WriteLine("  {");

            if (method.Implementation != null && !method.IsAbstract)
            {
                _currentFunction = method.Implementation;
                InitializeMethodContext(method.Implementation);

                // Calculate max stack
                _maxStack = Math.Max(8, _localIndices.Count + _tempIndices.Count + 4);
                WriteLine($"    .maxstack {_maxStack}");

                // Declare locals
                if (_localIndices.Count > 0 || _tempIndices.Count > 0)
                {
                    GenerateLocalsDeclaration(method.Implementation);
                }

                WriteLine();

                // Generate body
                if (method.Implementation.EntryBlock != null)
                {
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(method.Implementation.EntryBlock, visited, isEntry: true);
                }

                _currentFunction = null;
            }

            // Ensure return for non-abstract methods
            if (!method.IsAbstract && returnType == "void" && !EndsWithRet())
            {
                WriteLine("    ret");
            }

            WriteLine($"  }} // end of method {methodName}");
            WriteLine();
        }

        private void InitializeMethodContext(IRFunction function)
        {
            _localIndices.Clear();
            _paramIndices.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;
            _maxStack = 8;
            _currentStack = 0;

            foreach (var param in function.Parameters)
            {
                _declaredIdentifiers.Add(param.Name);
                _paramIndices[param.Name] = _paramIndices.Count;
            }

            foreach (var local in function.LocalVariables)
            {
                _declaredIdentifiers.Add(local.Name);
                _localIndices[local.Name] = _localIndices.Count;
            }

            AllocateTemporaries(function);
        }

        private void GenerateHeader(IRModule module)
        {
            WriteLine("// MSIL Assembly generated by BasicLang Compiler");
            WriteLine($"// Module: {module.Name}");
            WriteLine();
            WriteLine(".assembly extern mscorlib");
            WriteLine("{");
            WriteLine("  .publickeytoken = (B7 7A 5C 56 19 34 E0 89)");
            WriteLine("  .ver 4:0:0:0");
            WriteLine("}");
            WriteLine();
            WriteLine($".assembly {SanitizeName(module.Name)}");
            WriteLine("{");
            WriteLine("  .ver 1:0:0:0");
            WriteLine("}");
            WriteLine();
            WriteLine($".module {SanitizeName(module.Name)}.exe");
            WriteLine();
        }

        private void GenerateClass(IRModule module)
        {
            _moduleName = SanitizeName(module.Name);

            WriteLine($".class public auto ansi beforefieldinit {_moduleName}");
            WriteLine("       extends [mscorlib]System.Object");
            WriteLine("{");

            // Generate methods
            foreach (var function in module.Functions)
            {
                if (!function.IsExternal)
                {
                    GenerateMethod(function);
                    WriteLine();
                }
            }

            // Generate default constructor
            GenerateDefaultConstructor();

            WriteLine("} // end of class " + _moduleName);
        }

        private void GenerateDefaultConstructor()
        {
            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            WriteLine("    call instance void [mscorlib]System.Object::.ctor()");
            WriteLine("    ret");
            WriteLine("  } // end of method .ctor");
        }

        private void GenerateMethod(IRFunction function)
        {
            _currentFunction = function;
            _localIndices.Clear();
            _paramIndices.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;
            _maxStack = 8; // Default, will be calculated
            _currentStack = 0;

            // Collect declared identifiers
            foreach (var param in function.Parameters)
            {
                _declaredIdentifiers.Add(param.Name);
                _paramIndices[param.Name] = _paramIndices.Count;
            }

            foreach (var local in function.LocalVariables)
            {
                _declaredIdentifiers.Add(local.Name);
                _localIndices[local.Name] = _localIndices.Count;
            }

            // Allocate indices for temporaries
            AllocateTemporaries(function);

            // Generate method signature
            var returnType = MapType(function.ReturnType);
            var methodName = SanitizeName(function.Name);
            var isMain = methodName.Equals("Main", StringComparison.OrdinalIgnoreCase);

            // Method attributes
            WriteLine($"  .method public hidebysig static");

            // Parameters
            var paramList = string.Join(", ", function.Parameters.Select(p =>
                $"{MapType(p.Type)} {SanitizeName(p.Name)}"));

            WriteLine($"          {returnType} {methodName}({paramList}) cil managed");

            if (isMain)
            {
                WriteLine("  {");
                WriteLine("    .entrypoint");
            }
            else
            {
                WriteLine("  {");
            }

            // Calculate max stack (estimate)
            _maxStack = Math.Max(8, _localIndices.Count + _tempIndices.Count + 4);
            WriteLine($"    .maxstack {_maxStack}");

            // Declare locals
            if (_localIndices.Count > 0 || _tempIndices.Count > 0)
            {
                GenerateLocalsDeclaration(function);
            }

            WriteLine();

            // Generate method body
            if (function.EntryBlock != null)
            {
                var visitedBlocks = new HashSet<BasicBlock>();
                GenerateBasicBlock(function.EntryBlock, visitedBlocks, isEntry: true);
            }

            // Ensure method ends with ret
            if (returnType == "void" && !EndsWithRet())
            {
                WriteLine("    ret");
            }

            WriteLine($"  }} // end of method {methodName}");
        }

        private void AllocateTemporaries(IRFunction function)
        {
            foreach (var block in function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    if (instruction is IRValue value &&
                        !(value is IRConstant) &&
                        !(value is IRVariable) &&
                        !string.IsNullOrEmpty(value.Name) &&
                        !_declaredIdentifiers.Contains(value.Name))
                    {
                        if (!_tempIndices.ContainsKey(value))
                        {
                            var newIdx = _localIndices.Count + _tempIndices.Count;
                            _tempIndices[value] = newIdx;

                            // Track by string representation for cross-reference lookup
                            var valueStr = value.ToString();
                            if (!string.IsNullOrEmpty(valueStr))
                                _tempNameIndices[valueStr] = newIdx;

                            // Also track by short name (e.g., "t0") for return value matching
                            if (!string.IsNullOrEmpty(value.Name) && !_tempNameIndices.ContainsKey(value.Name))
                                _tempNameIndices[value.Name] = newIdx;
                        }
                    }
                }
            }
        }

        private void GenerateLocalsDeclaration(IRFunction function)
        {
            var locals = new List<string>();

            // Declared local variables
            foreach (var local in function.LocalVariables)
            {
                var localType = MapType(local.Type);
                locals.Add($"      [{_localIndices[local.Name]}] {localType} {SanitizeName(local.Name)}");
            }

            // Temporary variables
            foreach (var (temp, index) in _tempIndices)
            {
                var tempType = MapType(temp.Type);
                locals.Add($"      [{index}] {tempType} V_{index}");
            }

            if (locals.Count > 0)
            {
                WriteLine("    .locals init (");
                for (int i = 0; i < locals.Count; i++)
                {
                    var comma = i < locals.Count - 1 ? "," : "";
                    WriteLine($"{locals[i]}{comma}");
                }
                WriteLine("    )");
            }
        }

        private bool EndsWithRet()
        {
            var lines = _output.ToString().Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                return line == "ret" || line.StartsWith("ret");
            }
            return false;
        }

        private void GenerateBasicBlock(BasicBlock block, HashSet<BasicBlock> visited, bool isEntry = false)
        {
            if (visited.Contains(block)) return;
            visited.Add(block);

            // Emit label (skip for entry block)
            if (!isEntry)
            {
                WriteLine($"  {SanitizeLabel(block.Name)}:");
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

        private string SanitizeLabel(string name)
        {
            return SanitizeName(name).Replace(".", "_");
        }

        private int GetLocalIndex(string name)
        {
            if (_localIndices.TryGetValue(name, out var idx))
                return idx;
            return -1;
        }

        private int GetParamIndex(string name)
        {
            if (_paramIndices.TryGetValue(name, out var idx))
                return idx;
            return -1;
        }

        private int GetTempIndex(IRValue value)
        {
            if (_tempIndices.TryGetValue(value, out var idx))
                return idx;

            // Try by name as well
            var valueName = value?.ToString() ?? "";
            if (!string.IsNullOrEmpty(valueName) && _tempNameIndices.TryGetValue(valueName, out idx))
                return idx;

            // Allocate new temp
            var newIdx = _localIndices.Count + _tempIndices.Count;
            _tempIndices[value] = newIdx;
            if (!string.IsNullOrEmpty(valueName))
                _tempNameIndices[valueName] = newIdx;
            return newIdx;
        }

        private void EmitLoadLocal(string name)
        {
            var idx = GetLocalIndex(name);
            if (idx >= 0)
            {
                EmitLdloc(idx);
                return;
            }

            idx = GetParamIndex(name);
            if (idx >= 0)
            {
                EmitLdarg(idx);
                return;
            }

            WriteLine($"    // WARNING: Unknown local '{name}'");
        }

        private void EmitStoreLocal(string name)
        {
            var idx = GetLocalIndex(name);
            if (idx >= 0)
            {
                EmitStloc(idx);
                return;
            }

            WriteLine($"    // WARNING: Cannot store to '{name}'");
        }

        private void EmitLdloc(int index)
        {
            switch (index)
            {
                case 0: WriteLine("    ldloc.0"); break;
                case 1: WriteLine("    ldloc.1"); break;
                case 2: WriteLine("    ldloc.2"); break;
                case 3: WriteLine("    ldloc.3"); break;
                default:
                    if (index < 256)
                        WriteLine($"    ldloc.s {index}");
                    else
                        WriteLine($"    ldloc {index}");
                    break;
            }
            _currentStack++;
        }

        private void EmitStloc(int index)
        {
            switch (index)
            {
                case 0: WriteLine("    stloc.0"); break;
                case 1: WriteLine("    stloc.1"); break;
                case 2: WriteLine("    stloc.2"); break;
                case 3: WriteLine("    stloc.3"); break;
                default:
                    if (index < 256)
                        WriteLine($"    stloc.s {index}");
                    else
                        WriteLine($"    stloc {index}");
                    break;
            }
            _currentStack--;
        }

        private void EmitLdarg(int index)
        {
            switch (index)
            {
                case 0: WriteLine("    ldarg.0"); break;
                case 1: WriteLine("    ldarg.1"); break;
                case 2: WriteLine("    ldarg.2"); break;
                case 3: WriteLine("    ldarg.3"); break;
                default:
                    if (index < 256)
                        WriteLine($"    ldarg.s {index}");
                    else
                        WriteLine($"    ldarg {index}");
                    break;
            }
            _currentStack++;
        }

        private void EmitLoadValue(IRValue value)
        {
            if (value is IRConstant constant)
            {
                EmitLoadConstant(constant);
            }
            else if (value is IRVariable variable)
            {
                EmitLoadLocal(variable.Name);
            }
            else if (_tempIndices.TryGetValue(value, out var idx))
            {
                EmitLdloc(idx);
            }
            else if (!string.IsNullOrEmpty(value.Name) && _declaredIdentifiers.Contains(value.Name))
            {
                EmitLoadLocal(value.Name);
            }
            else
            {
                // Try by string representation
                var valueName = value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(valueName) && _tempNameIndices.TryGetValue(valueName, out idx))
                {
                    EmitLdloc(idx);
                }
                // Try by short name (e.g., "t0")
                else if (!string.IsNullOrEmpty(value?.Name) && _tempNameIndices.TryGetValue(value.Name, out idx))
                {
                    EmitLdloc(idx);
                }
                else
                {
                    WriteLine($"    // WARNING: Cannot load value '{value}'");
                }
            }
        }

        private void EmitLoadConstant(IRConstant constant)
        {
            if (constant.Value == null)
            {
                WriteLine("    ldnull");
                _currentStack++;
                return;
            }

            switch (constant.Value)
            {
                case bool b:
                    WriteLine(b ? "    ldc.i4.1" : "    ldc.i4.0");
                    break;
                case int i:
                    EmitLdcI4(i);
                    break;
                case long l:
                    WriteLine($"    ldc.i8 {l}");
                    break;
                case float f:
                    WriteLine($"    ldc.r4 {f:G9}");
                    break;
                case double d:
                    WriteLine($"    ldc.r8 {d:G17}");
                    break;
                case string s:
                    WriteLine($"    ldstr \"{EscapeString(s)}\"");
                    break;
                case char c:
                    EmitLdcI4((int)c);
                    break;
                default:
                    WriteLine($"    // WARNING: Unknown constant type: {constant.Value.GetType()}");
                    WriteLine("    ldc.i4.0");
                    break;
            }
            _currentStack++;
        }

        private void EmitLdcI4(int value)
        {
            switch (value)
            {
                case -1: WriteLine("    ldc.i4.m1"); break;
                case 0: WriteLine("    ldc.i4.0"); break;
                case 1: WriteLine("    ldc.i4.1"); break;
                case 2: WriteLine("    ldc.i4.2"); break;
                case 3: WriteLine("    ldc.i4.3"); break;
                case 4: WriteLine("    ldc.i4.4"); break;
                case 5: WriteLine("    ldc.i4.5"); break;
                case 6: WriteLine("    ldc.i4.6"); break;
                case 7: WriteLine("    ldc.i4.7"); break;
                case 8: WriteLine("    ldc.i4.8"); break;
                default:
                    if (value >= -128 && value <= 127)
                        WriteLine($"    ldc.i4.s {value}");
                    else
                        WriteLine($"    ldc.i4 {value}");
                    break;
            }
        }

        private string EscapeString(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32 || ch > 126)
                            sb.Append($"\\u{(int)ch:X4}");
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        #region Visitor Methods

        public override void Visit(IRFunction function) { }
        public override void Visit(BasicBlock block) { }
        public override void Visit(IRConstant constant) { }
        public override void Visit(IRVariable variable) { }

        public override void Visit(IRBinaryOp binaryOp)
        {
            // Handle string concatenation specially
            if (binaryOp.Operation == BinaryOpKind.Concat)
            {
                EmitLoadValue(binaryOp.Left);
                EmitLoadValue(binaryOp.Right);
                WriteLine("    call string [mscorlib]System.String::Concat(string, string)");
                _currentStack--; // Two pops, one push = net -1

                // Store result
                if (!string.IsNullOrEmpty(binaryOp.Name) && _declaredIdentifiers.Contains(binaryOp.Name))
                {
                    EmitStoreLocal(binaryOp.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(binaryOp);
                    EmitStloc(tempIdx);
                }
                return;
            }

            // Load operands onto stack
            EmitLoadValue(binaryOp.Left);
            EmitLoadValue(binaryOp.Right);

            // Emit operation
            var op = _typeMapper.MapBinaryOperator(binaryOp.Operation);
            WriteLine($"    {op}");
            _currentStack--; // Two pops, one push = net -1

            // Store result
            if (!string.IsNullOrEmpty(binaryOp.Name) && _declaredIdentifiers.Contains(binaryOp.Name))
            {
                // Store to declared variable
                EmitStoreLocal(binaryOp.Name);
            }
            else
            {
                // Store to temp
                var tempIdx = GetTempIndex(binaryOp);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRUnaryOp unaryOp)
        {
            EmitLoadValue(unaryOp.Operand);

            var op = _typeMapper.MapUnaryOperator(unaryOp.Operation);
            WriteLine($"    {op}");

            // Store result
            if (!string.IsNullOrEmpty(unaryOp.Name) && _declaredIdentifiers.Contains(unaryOp.Name))
            {
                EmitStoreLocal(unaryOp.Name);
            }
            else
            {
                var tempIdx = GetTempIndex(unaryOp);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRCompare compare)
        {
            EmitLoadValue(compare.Left);
            EmitLoadValue(compare.Right);

            switch (compare.Comparison)
            {
                case CompareKind.Eq:
                    WriteLine("    ceq");
                    break;
                case CompareKind.Ne:
                    WriteLine("    ceq");
                    WriteLine("    ldc.i4.0");
                    WriteLine("    ceq"); // Not equal = !(a == b)
                    break;
                case CompareKind.Lt:
                    WriteLine("    clt");
                    break;
                case CompareKind.Le:
                    WriteLine("    cgt");
                    WriteLine("    ldc.i4.0");
                    WriteLine("    ceq"); // <= is !(a > b)
                    break;
                case CompareKind.Gt:
                    WriteLine("    cgt");
                    break;
                case CompareKind.Ge:
                    WriteLine("    clt");
                    WriteLine("    ldc.i4.0");
                    WriteLine("    ceq"); // >= is !(a < b)
                    break;
            }

            _currentStack--; // Net effect

            // Store result
            if (!string.IsNullOrEmpty(compare.Name) && _declaredIdentifiers.Contains(compare.Name))
            {
                EmitStoreLocal(compare.Name);
            }
            else
            {
                var tempIdx = GetTempIndex(compare);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRAssignment assignment)
        {
            EmitLoadValue(assignment.Value);
            EmitStoreLocal(assignment.Target.Name);
        }

        public override void Visit(IRLoad load)
        {
            if (load.Address is IRVariable variable)
            {
                EmitLoadLocal(variable.Name);
            }
            else
            {
                EmitLoadValue(load.Address);
                var elemType = MapType(load.Type);
                WriteLine($"    ldind.{GetIndirectSuffix(load.Type)}");
            }

            // Store to temp if needed
            if (_tempIndices.ContainsKey(load))
            {
                var tempIdx = GetTempIndex(load);
                EmitStloc(tempIdx);
            }
        }

        private string GetIndirectSuffix(TypeInfo type)
        {
            if (type == null) return "ref";
            var name = type.Name?.ToLower() ?? "";
            return name switch
            {
                "integer" or "int" => "i4",
                "long" => "i8",
                "single" or "float" => "r4",
                "double" => "r8",
                "boolean" or "bool" => "i1",
                "byte" => "u1",
                "short" => "i2",
                "char" => "u2",
                _ => "ref"
            };
        }

        public override void Visit(IRStore store)
        {
            if (store.Address is IRVariable variable)
            {
                EmitLoadValue(store.Value);
                EmitStoreLocal(variable.Name);
            }
            else
            {
                // Indirect store
                EmitLoadValue(store.Address);
                EmitLoadValue(store.Value);
                var suffix = GetIndirectSuffix(store.Value.Type);
                WriteLine($"    stind.{suffix}");
                _currentStack -= 2;
            }
        }

        public override void Visit(IRCall call)
        {
            var funcName = call.FunctionName;
            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Check if this is an extern function call
            if (_module != null && _module.IsExtern(funcName))
            {
                var externDecl = _module.GetExtern(funcName);
                if (externDecl != null && externDecl.HasImplementation("MSIL"))
                {
                    var impl = externDecl.GetImplementation("MSIL");

                    // Load arguments
                    foreach (var arg in call.Arguments)
                    {
                        EmitLoadValue(arg);
                    }

                    // Emit the IL call (implementation should be fully qualified IL call)
                    WriteLine($"    {impl}");
                    _currentStack -= call.Arguments.Count;
                    if (hasReturn) _currentStack++;

                    // Store result if needed
                    if (hasReturn && !string.IsNullOrEmpty(call.Name))
                    {
                        if (_declaredIdentifiers.Contains(call.Name))
                        {
                            EmitStoreLocal(call.Name);
                        }
                        else
                        {
                            var tempIdx = GetTempIndex(call);
                            EmitStloc(tempIdx);
                        }
                    }
                    else if (hasReturn)
                    {
                        WriteLine("    pop");
                        _currentStack--;
                    }
                    return;
                }
            }

            // Handle standard library calls
            if (TryEmitStdLibCall(funcName, call.Arguments.ToList(), hasReturn))
            {
                // Store result if needed
                if (hasReturn && !string.IsNullOrEmpty(call.Name))
                {
                    if (_declaredIdentifiers.Contains(call.Name))
                    {
                        EmitStoreLocal(call.Name);
                    }
                    else
                    {
                        var tempIdx = GetTempIndex(call);
                        EmitStloc(tempIdx);
                    }
                }
                return;
            }

            // Load arguments
            foreach (var arg in call.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Generate call
            var returnType = MapType(call.Type);
            var paramTypes = string.Join(", ", call.Arguments.Select(a => MapType(a.Type)));
            var sanitizedName = SanitizeName(funcName);

            // Use module name for class reference
            var className = _moduleName ?? "Program";
            WriteLine($"    call {returnType} {className}::{sanitizedName}({paramTypes})");

            _currentStack -= call.Arguments.Count;
            if (hasReturn) _currentStack++;

            // Store result if needed
            if (hasReturn && !string.IsNullOrEmpty(call.Name))
            {
                if (_declaredIdentifiers.Contains(call.Name))
                {
                    EmitStoreLocal(call.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(call);
                    EmitStloc(tempIdx);
                }
            }
            else if (hasReturn)
            {
                // Discard result if not used
                WriteLine("    pop");
                _currentStack--;
            }
        }

        private bool TryEmitStdLibCall(string funcName, List<IRValue> args, bool hasReturn)
        {
            var lower = funcName.ToLower();

            switch (lower)
            {
                case "printline":
                    if (args.Count > 0)
                    {
                        EmitLoadValue(args[0]);
                        var argType = MapType(args[0].Type);

                        if (argType == "string")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(string)");
                        }
                        else if (argType == "int32")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(int32)");
                        }
                        else if (argType == "int64")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(int64)");
                        }
                        else if (argType == "float64" || argType == "float32")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(float64)");
                        }
                        else if (argType == "bool")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(bool)");
                        }
                        else
                        {
                            WriteLine("    box object");
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(object)");
                        }
                        _currentStack--;
                    }
                    else
                    {
                        WriteLine("    call void [mscorlib]System.Console::WriteLine()");
                    }
                    return true;

                case "print":
                    if (args.Count > 0)
                    {
                        EmitLoadValue(args[0]);
                        var argType = MapType(args[0].Type);
                        WriteLine($"    call void [mscorlib]System.Console::Write({argType})");
                        _currentStack--;
                    }
                    return true;

                case "readline":
                    WriteLine("    call string [mscorlib]System.Console::ReadLine()");
                    _currentStack++;
                    return true;

                case "sqrt":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Sqrt(float64)");
                    return true;

                case "pow":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    call float64 [mscorlib]System.Math::Pow(float64, float64)");
                    _currentStack--;
                    return true;

                case "sin":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Sin(float64)");
                    return true;

                case "cos":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Cos(float64)");
                    return true;

                case "tan":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Tan(float64)");
                    return true;

                case "log":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Log(float64)");
                    return true;

                case "exp":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Exp(float64)");
                    return true;

                case "floor":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Floor(float64)");
                    return true;

                case "ceiling":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Ceiling(float64)");
                    return true;

                case "abs":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Abs(float64)");
                    return true;

                case "round":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Round(float64)");
                    return true;

                case "min":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    call float64 [mscorlib]System.Math::Min(float64, float64)");
                    _currentStack--;
                    return true;

                case "max":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    call float64 [mscorlib]System.Math::Max(float64, float64)");
                    _currentStack--;
                    return true;

                case "len":
                    EmitLoadValue(args[0]);
                    WriteLine("    callvirt instance int32 [mscorlib]System.String::get_Length()");
                    return true;

                case "cint":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.i4");
                    return true;

                case "clng":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.i8");
                    return true;

                case "cdbl":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.r8");
                    return true;

                case "csng":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.r4");
                    return true;

                case "cstr":
                    EmitLoadValue(args[0]);
                    var srcType = MapType(args[0].Type);
                    if (srcType != "string")
                    {
                        WriteLine($"    box [{srcType}]");
                        WriteLine("    callvirt instance string [mscorlib]System.Object::ToString()");
                    }
                    return true;

                case "rnd":
                    // Use System.Random - simplified version
                    WriteLine("    newobj instance void [mscorlib]System.Random::.ctor()");
                    WriteLine("    callvirt instance float64 [mscorlib]System.Random::NextDouble()");
                    _currentStack++;
                    return true;

                default:
                    return false;
            }
        }

        public override void Visit(IRReturn ret)
        {
            if (ret.Value != null)
            {
                EmitLoadValue(ret.Value);
            }
            WriteLine("    ret");
        }

        public override void Visit(IRBranch branch)
        {
            var target = SanitizeLabel(branch.Target.Name);
            WriteLine($"    br {target}");
        }

        public override void Visit(IRConditionalBranch condBranch)
        {
            EmitLoadValue(condBranch.Condition);

            var trueTarget = SanitizeLabel(condBranch.TrueTarget.Name);
            var falseTarget = SanitizeLabel(condBranch.FalseTarget.Name);

            WriteLine($"    brtrue {trueTarget}");
            WriteLine($"    br {falseTarget}");
            _currentStack--;
        }

        public override void Visit(IRSwitch switchInst)
        {
            EmitLoadValue(switchInst.Value);

            var targets = switchInst.Cases.Select(c => SanitizeLabel(c.Target.Name)).ToList();
            var targetList = string.Join(", ", targets);

            WriteLine($"    switch ({targetList})");
            WriteLine($"    br {SanitizeLabel(switchInst.DefaultTarget.Name)}");
            _currentStack--;
        }

        public override void Visit(IRPhi phi)
        {
            // Phi nodes are handled during SSA deconstruction
            // For now, emit a comment
            WriteLine($"    // phi: {phi.Name}");
        }

        public override void Visit(IRAlloca alloca)
        {
            // In MSIL, locals are already allocated via .locals init
            // Nothing to emit here
        }

        public override void Visit(IRGetElementPtr gep)
        {
            // Array element access
            EmitLoadValue(gep.BasePointer);

            if (gep.Indices.Count > 0)
            {
                EmitLoadValue(gep.Indices[0]);
                var elemType = gep.BasePointer.Type?.ElementType;
                var ilType = elemType != null ? MapType(elemType) : "object";
                WriteLine($"    ldelema {ilType}");
                _currentStack--; // array + index -> address
            }

            // Store to temp
            if (_tempIndices.ContainsKey(gep))
            {
                var tempIdx = GetTempIndex(gep);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRCast cast)
        {
            EmitLoadValue(cast.Value);

            var targetType = cast.Type?.Name?.ToLower() ?? "";

            switch (targetType)
            {
                case "integer":
                    WriteLine("    conv.i4");
                    break;
                case "long":
                    WriteLine("    conv.i8");
                    break;
                case "single":
                    WriteLine("    conv.r4");
                    break;
                case "double":
                    WriteLine("    conv.r8");
                    break;
                case "byte":
                    WriteLine("    conv.u1");
                    break;
                case "short":
                    WriteLine("    conv.i2");
                    break;
                case "char":
                    WriteLine("    conv.u2");
                    break;
                case "boolean":
                    // Convert to bool (0 or 1)
                    WriteLine("    ldc.i4.0");
                    WriteLine("    cgt.un");
                    break;
                default:
                    WriteLine($"    // WARNING: Unknown cast to {targetType}");
                    break;
            }

            // Store to temp
            if (!string.IsNullOrEmpty(cast.Name) && _declaredIdentifiers.Contains(cast.Name))
            {
                EmitStoreLocal(cast.Name);
            }
            else if (_tempIndices.ContainsKey(cast))
            {
                var tempIdx = GetTempIndex(cast);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRLabel label)
        {
            WriteLine($"  {SanitizeLabel(label.Name)}:");
        }

        public override void Visit(IRComment comment)
        {
            if (_options.GenerateComments)
            {
                WriteLine($"    // {comment.Text}");
            }
        }

        public override void Visit(IRArrayAlloc arrayAlloc)
        {
            var elementType = MapType(arrayAlloc.ElementType);
            WriteLine($"    ldc.i4 {arrayAlloc.Size}");
            WriteLine($"    newarr {elementType}");
            WriteLine($"    stloc {arrayAlloc.Name}");
        }

        public override void Visit(IRArrayStore arrayStore)
        {
            var elementType = MapType(arrayStore.Array.Type?.ElementType ?? new TypeInfo("object", TypeKind.Class));
            WriteLine($"    ldloc {arrayStore.Array.Name}");
            if (arrayStore.Index is IRConstant c)
                WriteLine($"    ldc.i4 {c.Value}");
            else
                WriteLine($"    ldloc {arrayStore.Index.Name}");
            if (arrayStore.Value is IRConstant vc)
                EmitLoadConstant(vc);
            else
                WriteLine($"    ldloc {arrayStore.Value.Name}");
            WriteLine($"    stelem {elementType}");
        }

        public override void Visit(IRAwait awaitInst)
        {
            // MSIL async/await requires complex state machine generation
            // For now, generate a comment
            WriteLine($"    // await - MSIL async not fully implemented");
        }

        public override void Visit(IRYield yieldInst)
        {
            // MSIL yield requires iterator state machine generation
            // For now, generate a comment
            if (yieldInst.IsBreak)
                WriteLine("    // yield break - MSIL iterators not fully implemented");
            else
                WriteLine($"    // yield return - MSIL iterators not fully implemented");
        }

        public override void Visit(IRNewObject newObj)
        {
            // Generate newobj instruction
            var className = SanitizeName(newObj.ClassName);

            // Load arguments first
            foreach (var arg in newObj.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Build constructor signature with parameter types
            var paramTypes = string.Join(", ", newObj.Arguments.Select(a => MapType(a.Type)));

            // Emit newobj with proper constructor signature
            WriteLine($"    newobj instance void {className}::.ctor({paramTypes})");
            _currentStack -= newObj.Arguments.Count; // Arguments consumed
            _currentStack++; // Object reference pushed

            // Store result if needed
            if (!string.IsNullOrEmpty(newObj.Name))
            {
                if (_declaredIdentifiers.Contains(newObj.Name))
                {
                    EmitStoreLocal(newObj.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(newObj);
                    EmitStloc(tempIdx);
                }
            }
        }

        public override void Visit(IRInstanceMethodCall methodCall)
        {
            var hasReturn = methodCall.Type != null && !methodCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Load 'this' reference (the object on which the method is called)
            EmitLoadValue(methodCall.Object);

            // Load arguments
            foreach (var arg in methodCall.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Build method signature
            var returnType = MapType(methodCall.Type);
            var paramTypes = string.Join(", ", methodCall.Arguments.Select(a => MapType(a.Type)));
            var className = methodCall.Object?.Type?.Name != null ? SanitizeName(methodCall.Object.Type.Name) : "object";
            var methodName = SanitizeName(methodCall.MethodName);

            // Use callvirt for virtual dispatch (polymorphic behavior)
            // For non-virtual calls, the backend should use 'call instance' instead, but callvirt is safer as default
            var callInstruction = methodCall.IsVirtual || !methodCall.IsVirtual ? "callvirt" : "call";
            WriteLine($"    {callInstruction} instance {returnType} {className}::{methodName}({paramTypes})");

            // Update stack: pop 'this' + args, push return value if any
            _currentStack -= (1 + methodCall.Arguments.Count);
            if (hasReturn) _currentStack++;

            // Store result if needed
            if (hasReturn && !string.IsNullOrEmpty(methodCall.Name))
            {
                if (_declaredIdentifiers.Contains(methodCall.Name))
                {
                    EmitStoreLocal(methodCall.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(methodCall);
                    EmitStloc(tempIdx);
                }
            }
            else if (hasReturn)
            {
                // Discard unused return value
                WriteLine("    pop");
                _currentStack--;
            }
        }

        public override void Visit(IRBaseMethodCall baseCall)
        {
            var hasReturn = baseCall.Type != null && !baseCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Load 'this' (ldarg.0 for instance methods)
            WriteLine("    ldarg.0");
            _currentStack++;

            // Load arguments
            foreach (var arg in baseCall.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Build method signature
            var returnType = MapType(baseCall.Type);
            var paramTypes = string.Join(", ", baseCall.Arguments.Select(a => MapType(a.Type)));
            var methodName = SanitizeName(baseCall.MethodName);

            // For base calls, we need to know the base class name from the current class context
            // Use 'call instance' instead of 'callvirt' to call base class method non-virtually
            var baseClassName = _currentClass != null && !string.IsNullOrEmpty(_currentClass.BaseClass)
                ? SanitizeName(_currentClass.BaseClass)
                : "[mscorlib]System.Object";
            WriteLine($"    call instance {returnType} {baseClassName}::{methodName}({paramTypes})");

            // Update stack: pop 'this' + args, push return value if any
            _currentStack -= (1 + baseCall.Arguments.Count);
            if (hasReturn) _currentStack++;

            // Store result if needed
            if (hasReturn && !string.IsNullOrEmpty(baseCall.Name))
            {
                if (_declaredIdentifiers.Contains(baseCall.Name))
                {
                    EmitStoreLocal(baseCall.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(baseCall);
                    EmitStloc(tempIdx);
                }
            }
            else if (hasReturn)
            {
                // Discard unused return value
                WriteLine("    pop");
                _currentStack--;
            }
        }

        public override void Visit(IRFieldAccess fieldAccess)
        {
            // Load object reference
            EmitLoadValue(fieldAccess.Object);

            // Load field value from object
            var fieldType = MapType(fieldAccess.Type);
            var className = fieldAccess.Object?.Type?.Name != null ? SanitizeName(fieldAccess.Object.Type.Name) : "object";
            var fieldName = SanitizeName(fieldAccess.FieldName);

            WriteLine($"    ldfld {fieldType} {className}::{fieldName}");
            _currentStack--; // Pop object reference
            _currentStack++; // Push field value

            // Store result if needed
            if (!string.IsNullOrEmpty(fieldAccess.Name))
            {
                if (_declaredIdentifiers.Contains(fieldAccess.Name))
                {
                    EmitStoreLocal(fieldAccess.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(fieldAccess);
                    EmitStloc(tempIdx);
                }
            }
        }

        public override void Visit(IRFieldStore fieldStore)
        {
            // Load object reference
            EmitLoadValue(fieldStore.Object);

            // Load value to store
            EmitLoadValue(fieldStore.Value);

            // Store value to field
            var fieldType = MapType(fieldStore.Value?.Type);
            var className = fieldStore.Object?.Type?.Name != null ? SanitizeName(fieldStore.Object.Type.Name) : "object";
            var fieldName = SanitizeName(fieldStore.FieldName);

            WriteLine($"    stfld {fieldType} {className}::{fieldName}");

            // Update stack: pop object + value
            _currentStack -= 2;
        }

        public override void Visit(IRTupleElement tupleElement)
        {
            // Load tuple value onto stack
            EmitLoadValue(tupleElement.Tuple);

            // Call the Item property getter (Item1, Item2, etc.)
            var tupleType = MapType(tupleElement.Tuple?.Type) ?? "object";
            var elemType = MapType(tupleElement.Type);
            var itemIndex = tupleElement.Index + 1; // 1-based

            WriteLine($"    ldfld {elemType} {tupleType}::Item{itemIndex}");

            // Store to local variable
            var varName = SanitizeName(tupleElement.Name);
            if (!_localIndices.ContainsKey(varName))
            {
                var localIndex = _localCounter++;
                _localIndices[varName] = localIndex;
            }
            WriteLine($"    stloc {_localIndices[varName]}");
        }

        public override void Visit(IRTryCatch tryCatch)
        {
            // MSIL exception handling with .try/.catch directives
            WriteLine("    .try");
            WriteLine("    {");
            foreach (var inst in tryCatch.TryBlock.Instructions)
            {
                if (inst is IRBranch or IRConditionalBranch) continue;
                inst.Accept(this);
            }
            WriteLine("        leave EndTry");
            WriteLine("    }");

            foreach (var catchClause in tryCatch.CatchClauses)
            {
                var exType = catchClause.ExceptionType?.Name ?? "[mscorlib]System.Exception";
                if (!exType.StartsWith("["))
                    exType = $"[mscorlib]System.{exType}";
                WriteLine($"    catch {exType}");
                WriteLine("    {");

                // Store exception to local if variable name is provided
                if (!string.IsNullOrEmpty(catchClause.VariableName))
                {
                    var varName = SanitizeName(catchClause.VariableName);
                    if (!_localIndices.ContainsKey(varName))
                    {
                        var localIndex = _localCounter++;
                        _localIndices[varName] = localIndex;
                    }
                    WriteLine($"        stloc {_localIndices[varName]}");
                }
                else
                {
                    WriteLine("        pop"); // Pop exception if not used
                }

                foreach (var inst in catchClause.Block.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
                WriteLine("        leave EndTry");
                WriteLine("    }");
            }

            WriteLine("EndTry:");
        }

        public override void Visit(IRInlineCode inlineCode)
        {
            if (inlineCode.Language.ToLower() == "msil")
            {
                // Emit the MSIL code directly
                WriteLine("        // Inline MSIL code");
                foreach (var line in inlineCode.Code.Split('\n'))
                {
                    WriteLine($"        {line.TrimEnd()}");
                }
            }
            else
            {
                // For non-MSIL inline code, emit a comment indicating it's not supported
                WriteLine($"        // WARNING: Inline {inlineCode.Language} code not supported in MSIL backend");
                WriteLine($"        // Original code ({inlineCode.Code.Length} chars) was skipped");
            }
        }

        public override void Visit(IRForEach forEach)
        {
            // MSIL foreach would use GetEnumerator pattern
            WriteLine("        // TODO: IRForEach not fully implemented in MSIL backend");
        }

        public override void Visit(IRIndexerAccess indexer)
        {
            // MSIL indexer access handled in expression emission
            WriteLine("        // TODO: IRIndexerAccess not fully implemented in MSIL backend");
        }

        #endregion

        private void WriteLine(string text = "")
        {
            _output.AppendLine(text);
        }
    }

    public class MSILCodeGenOptions
    {
        public bool GenerateComments { get; set; } = true;
        public string AssemblyName { get; set; } = "GeneratedAssembly";
    }
}
